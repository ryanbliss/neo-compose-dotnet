// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P42 §2.3. The .NET half of the file-registry parity gate.
    ///
    /// <para><c>Images.&lt;Name&gt;</c> is the only NeoScript construct whose
    /// two runtimes resolve from <b>different sources</b>: the TypeScript side
    /// resolves the registry symbol against the project document while the
    /// compiler runs, and this side resolves the resulting file id through
    /// <see cref="NeoAssetDatabase"/> while the game runs. Nothing structural
    /// forces those to agree, so the agreement is pinned by a hand-written
    /// fixture shared verbatim with
    /// <c>src/models/neoscript/neoscript-registry-parity.test.ts</c>.</para>
    ///
    /// <para>Three assertions, matching the ones the web half documents:
    /// every registry entry's file id resolves to a live asset through the
    /// database seeded from the same path; every evaluate case, fed to the
    /// <c>imageSlice</c> arm, produces exactly the fixture's record or throws
    /// exactly its error; and resolving that record's file id through the
    /// database does not change the record. The fixture's <c>compileCases</c>
    /// have no counterpart here — there is no C# NeoScript compiler.</para>
    /// </summary>
    public class NeoScriptRegistryParityTests
    {
        private const int SeededSliceCount = 4;

        [Test]
        public void EveryRegistryEntryResolvesThroughTheAssetDatabase()
        {
            WithSeededDatabase((database, _) =>
            {
                foreach (JToken entry in RegistryEntries("Images"))
                {
                    string fileId = Text(entry, "fileId");
                    Assert.AreEqual(
                        Text(entry, "path"),
                        database.TryGetAssetPath(fileId),
                        $"Image '{Text(entry, "symbol")}' did not resolve to its seeded asset path.");
                    Assert.IsNotNull(
                        database.TryGetSprite(fileId, 0),
                        $"Image '{Text(entry, "symbol")}' resolved no sprite at slice 0.");
                }
                foreach (JToken entry in RegistryEntries("AudioClips"))
                {
                    string fileId = Text(entry, "fileId");
                    Assert.AreEqual(
                        Text(entry, "path"),
                        database.TryGetAssetPath(fileId),
                        $"Audio clip '{Text(entry, "symbol")}' did not resolve to its seeded asset path.");
                    Assert.IsNotNull(
                        database.TryGetAudioClip(fileId),
                        $"Audio clip '{Text(entry, "symbol")}' resolved no clip.");
                }
            });
        }

        [Test]
        public void EveryEvaluateCaseProducesTheSharedSpriteRecord()
        {
            WithSeededDatabase((database, client) =>
            {
                var ctx = new NSGetterEvaluator.Context(client, null, null);
                foreach (JToken testCase in EvaluateCases())
                {
                    string name = Text(testCase, "name");
                    string fileId = Text(testCase, "fileId");
                    double sliceIndex = testCase["sliceIndex"]!.Value<double>();

                    if (testCase["expectedError"] is JToken expectedError)
                    {
                        var error = Assert.Throws<NSGetterRuntimeError>(
                            () => Evaluate(ctx, fileId, sliceIndex),
                            $"Case '{name}' was expected to throw.");
                        Assert.AreEqual(
                            expectedError.Value<string>(),
                            error!.Message,
                            $"Case '{name}' threw a different message than the shared fixture.");
                        continue;
                    }

                    IDictionary<string, object?> produced = RequireRecord(
                        Evaluate(ctx, fileId, sliceIndex),
                        name);
                    JObject expected = (JObject)testCase["expected"]!;
                    AssertMatchesExpected(expected, produced, name);
                }
            });
        }

        [Test]
        public void ResolvingTheRecordThroughTheAssetDatabaseDoesNotChangeIt()
        {
            WithSeededDatabase((database, client) =>
            {
                var ctx = new NSGetterEvaluator.Context(client, null, null);
                foreach (JToken testCase in EvaluateCases())
                {
                    if (testCase["expected"] is not JObject expected) continue;
                    string name = Text(testCase, "name");
                    IDictionary<string, object?> produced = RequireRecord(
                        Evaluate(
                            ctx,
                            Text(testCase, "fileId"),
                            testCase["sliceIndex"]!.Value<double>()),
                        name);

                    // This is the step the TypeScript runtime never takes, and
                    // therefore the whole reason the fixture exists.
                    var fileId = (string)produced["fileId"]!;
                    var resolvedIndex = (int)produced["sliceIndex"]!;
                    Assert.IsNotNull(
                        database.TryGetSprite(fileId, resolvedIndex),
                        $"Case '{name}' produced a record that NeoAssetDatabase could not resolve.");

                    AssertMatchesExpected(expected, produced, name);
                }
            });
        }

        // ------------------------------------------------------------------
        // Harness.
        // ------------------------------------------------------------------

        private static object? Evaluate(
            NSGetterEvaluator.Context ctx,
            string fileId,
            double sliceIndex)
        {
            return NSGetterEvaluator.Evaluate(
                new FunctionWithReturnType
                {
                    parameters = Array.Empty<Variable>(),
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction
                        {
                            type = InstructionKind.Return,
                            pointer = new FunctionPointer
                            {
                                type = PointerKind.Function,
                                function = new ImageSliceFunction
                                {
                                    type = FunctionKind.ImageSlice,
                                    info = new FunctionImageSliceInfo
                                    {
                                        filePointer = StringLiteral(fileId),
                                        sliceIndexPointer = NumberLiteral(sliceIndex),
                                    },
                                },
                            },
                        },
                    },
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.Sprite,
                        required = true,
                    },
                },
                ctx);
        }

        private static IDictionary<string, object?> RequireRecord(
            object? produced,
            string caseName)
        {
            Assert.IsInstanceOf<IDictionary<string, object?>>(
                produced,
                $"Case '{caseName}' did not produce a sprite record.");
            return (IDictionary<string, object?>)produced!;
        }

        /// <summary>
        /// The produced record must be exactly the fixture's — same keys, same
        /// values, no extras. "Byte-for-byte identical on both runtimes" is the
        /// fixture's own wording, so an extra key is a failure even if every
        /// shared key matches.
        /// </summary>
        private static void AssertMatchesExpected(
            JObject expected,
            IDictionary<string, object?> produced,
            string caseName)
        {
            Assert.AreEqual(
                expected.Count,
                produced.Count,
                $"Case '{caseName}' produced a record with a different key count.");
            Assert.AreEqual(
                expected["fileId"]!.Value<string>(),
                produced["fileId"],
                $"Case '{caseName}' produced a different fileId.");
            Assert.AreEqual(
                expected["sliceIndex"]!.Value<int>(),
                produced["sliceIndex"],
                $"Case '{caseName}' produced a different sliceIndex.");
        }

        private static JObject Fixture()
        {
            return JObject.Parse(NeoScriptRegistryParityFixture.Json);
        }

        private static IEnumerable<JToken> RegistryEntries(string registry)
        {
            return (JArray)Fixture()["registries"]![registry]!;
        }

        private static IEnumerable<JToken> EvaluateCases()
        {
            return (JArray)Fixture()["evaluateCases"]!;
        }

        private static string Text(JToken token, string key)
        {
            return token[key]!.Value<string>()!;
        }

        /// <summary>
        /// Seeds a <see cref="NeoAssetDatabase"/> from the fixture's registries
        /// — the same <c>path</c> the web half hands the compiler — and tears
        /// the Unity objects down afterwards.
        /// </summary>
        private static void WithSeededDatabase(Action<NeoAssetDatabase, NeoClient> body)
        {
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            var texture = new Texture2D(SeededSliceCount, 1);
            var sprites = new Sprite[SeededSliceCount];
            var audioClips = new List<AudioClip>();
            try
            {
                for (int index = 0; index < SeededSliceCount; index++)
                {
                    sprites[index] = Sprite.Create(
                        texture,
                        new Rect(index, 0, 1, 1),
                        new Vector2(0.5f, 0.5f));
                }
                foreach (JToken entry in RegistryEntries("Images"))
                {
                    SeedFile(database, entry, sprites, null);
                }
                foreach (JToken entry in RegistryEntries("AudioClips"))
                {
                    var clip = AudioClip.Create(Text(entry, "symbol"), 1, 1, 44100, false);
                    audioClips.Add(clip);
                    SeedFile(database, entry, null, clip);
                }

                NeoClient client = BuildClient();
                client.assetDatabase = database;
                body(database, client);
            }
            finally
            {
                foreach (Sprite sprite in sprites)
                {
                    if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite);
                }
                foreach (AudioClip clip in audioClips)
                {
                    if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
                }
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(database);
            }
        }

        private static void SeedFile(
            NeoAssetDatabase database,
            JToken entry,
            Sprite[]? sprites,
            AudioClip? audioClip)
        {
            string path = Text(entry, "path");
            database.SetFile(
                Text(entry, "fileId"),
                path.Substring(path.LastIndexOf('/') + 1),
                path,
                updatedAt: "",
                lastDownloadedAt: "",
                fileRecordHash: "",
                templateId: null,
                templateRecordHash: "",
                importSettingsVersion: "",
                sprites: sprites,
                audioClip: audioClip);
        }

        private static ValuePointer StringLiteral(string value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.String,
                    required = true,
                },
                value = JToken.FromObject(value),
            },
        };

        private static ValuePointer NumberLiteral(double value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.Int,
                    required = true,
                },
                value = JToken.FromObject(value),
            },
        };

        /// <summary>
        /// The evaluator needs a client for its context and nothing more: the
        /// <c>imageSlice</c> arm reads no project state, which is exactly the
        /// property this fixture is here to keep true.
        /// </summary>
        private static NeoClient BuildClient()
        {
            var rootAssets = RootMember("member-root-assets", "Assets", "value-assets", null);
            var rootSave = RootMember("member-root-save", "Save", "value-save", "save");
            var rootSession = RootMember(
                "member-root-session", "Session", "value-session", "session");
            var data = new ProjectData
            {
                project = new Project
                {
                    id = "project-registry-parity",
                    name = "Registry parity",
                    rootAssetsMemberId = rootAssets.id,
                    rootSaveFileMemberId = rootSave.id,
                    rootSessionMemberId = rootSession.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                members = new Dictionary<string, JsonMember>
                {
                    [rootAssets.id] = rootAssets,
                    [rootSave.id] = rootSave,
                    [rootSession.id] = rootSession,
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["value-assets"] = RootValue("value-assets"),
                    ["value-save"] = RootValue("value-save"),
                    ["value-session"] = RootValue("value-session"),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["class-root"] = new NeoSchemaClass
                    {
                        id = "class-root",
                        projectId = "project-registry-parity",
                        name = "Root",
                        schema = new Dictionary<string, string>(),
                        createdAt = "x",
                        updatedAt = "x",
                    },
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
            return NeoTestSaveStack.ClientFromSchema(data);
        }

        private static ClassMember RootMember(
            string id,
            string name,
            string valueId,
            string? storage)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-registry-parity",
                name = name,
                kind = MemberKind.Class,
                classId = "class-root",
                valueId = valueId,
                storage = storage,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static ObjectMemberValue RootValue(string id) => new()
        {
            id = id,
            classId = "class-root",
            value = new Dictionary<string, string>(),
            createdAt = "x",
            updatedAt = "x",
        };
    }
}
