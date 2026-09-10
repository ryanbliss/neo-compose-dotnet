// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// The compiler-revision handshake. A deployment recompiles its whole
    /// fleet on every revision bump, so this SDK executes exactly
    /// <see cref="FunctionWithReturnType.CurrentCompilerRevision"/>: an older
    /// stamp, a newer stamp and an absent stamp are all rejected before any
    /// instruction runs. The fixture sweep keeps the vendored corpora on that
    /// same revision so a bump cannot leave a test body behind.
    /// </summary>
    public class NeoScriptCompilerRevisionTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        // ------------------------------------------------------------------
        // Execution gate.
        // ------------------------------------------------------------------

        [Test]
        public void CurrentRevision_Executes()
        {
            Assert.AreEqual(
                7d,
                Evaluate(Body(FunctionWithReturnType.CurrentCompilerRevision)));
        }

        [Test]
        public void MissingStamp_IsRejectedWithAReExportInstruction()
        {
            NeoScriptPreExecutionValidationError error =
                Assert.Throws<NeoScriptPreExecutionValidationError>(
                    () => Evaluate(Body(null)))!;

            Assert.AreEqual(
                "NeoScript body carries no compiler revision stamp; this SDK "
                    + "executes only revision "
                    + FunctionWithReturnType.CurrentCompilerRevision
                    + ". Re-export the project from a Neo Compose deployment "
                    + "at revision "
                    + FunctionWithReturnType.CurrentCompilerRevision
                    + ".",
                error.Message);
        }

        [Test]
        public void PriorRevision_IsRejected()
        {
            AssertStampRejected(
                FunctionWithReturnType.CurrentCompilerRevision - 1);
        }

        [Test]
        public void FutureRevision_IsRejected()
        {
            AssertStampRejected(
                FunctionWithReturnType.CurrentCompilerRevision + 1);
        }

        private static void AssertStampRejected(int stamp)
        {
            NeoScriptPreExecutionValidationError error =
                Assert.Throws<NeoScriptPreExecutionValidationError>(
                    () => Evaluate(Body(stamp)))!;

            Assert.AreEqual(
                "NeoScript body is stamped compiler revision "
                    + stamp
                    + "; this SDK executes only revision "
                    + FunctionWithReturnType.CurrentCompilerRevision
                    + ". Re-export the project from a deployment at revision "
                    + FunctionWithReturnType.CurrentCompilerRevision
                    + ", or install the SDK release that matches the export.",
                error.Message);
        }

        // ------------------------------------------------------------------
        // Fixture sweep. Every compiled body the test assembly can load must
        // already be on the revision the runtime accepts, or the fixture is
        // dead weight that no test can execute.
        // ------------------------------------------------------------------

        [Test]
        public void EveryLoadableFixtureIsStampedAtTheCurrentRevision()
        {
            var stale = new List<string>();
            var swept = new List<string>();

            string[] fixtureFiles = Directory.GetFiles(PackageRoot, "*.json");
            string[] compressedFixtureFiles =
                Directory.GetFiles(PackageRoot, "*.json.gz");
            Assert.IsNotEmpty(
                fixtureFiles,
                $"No JSON fixtures were enumerable under '{PackageRoot}'.");
            foreach (string path in fixtureFiles)
            {
                CollectStale(
                    Path.GetFileName(path),
                    File.ReadAllText(path),
                    swept,
                    stale);
            }
            foreach (string path in compressedFixtureFiles)
            {
                CollectStale(
                    Path.GetFileName(path),
                    ReadCompressed(path),
                    swept,
                    stale);
            }

            CollectStale(
                nameof(NeoConstructorParityFixture),
                NeoConstructorParityFixture.Json,
                swept,
                stale);
            CollectStale(
                nameof(NeoScriptControlFlowParityFixture),
                NeoScriptControlFlowParityFixture.Json,
                swept,
                stale);

            CollectionAssert.IsEmpty(
                stale,
                "Re-stamp these bodies at revision "
                    + FunctionWithReturnType.CurrentCompilerRevision
                    + "; the runtime executes nothing else. Swept: "
                    + string.Join(", ", swept));
        }

        private static string ReadCompressed(string path)
        {
            using FileStream file = File.OpenRead(path);
            using var decompressed = new GZipStream(
                file,
                CompressionMode.Decompress);
            using var reader = new StreamReader(decompressed);
            return reader.ReadToEnd();
        }

        private static void CollectStale(
            string label,
            string json,
            List<string> swept,
            List<string> stale)
        {
            int bodies = 0;
            foreach (JObject body in CompiledBodies(JToken.Parse(json)))
            {
                bodies++;
                JToken? stamp = body["compilerRevision"];
                if (stamp is not null
                    && stamp.Type == JTokenType.Integer
                    && stamp.Value<int>()
                        == FunctionWithReturnType.CurrentCompilerRevision)
                {
                    continue;
                }
                stale.Add(
                    $"{label}: {body.Path} = {stamp?.ToString() ?? "absent"}");
            }
            swept.Add($"{label} ({bodies} bodies)");
        }

        /// <summary>
        /// Every compiled-body-shaped object in the document: the wire shape
        /// carries a parameter list and an instruction list.
        /// </summary>
        private static IEnumerable<JObject> CompiledBodies(JToken token)
        {
            if (token is not JContainer container) yield break;
            foreach (JToken descendant in container.DescendantsAndSelf())
            {
                if (descendant is not JObject obj) continue;
                if (obj["parameters"] is JArray && obj["instructions"] is JArray)
                {
                    yield return obj;
                }
            }
        }

        // ------------------------------------------------------------------
        // Fixture.
        // ------------------------------------------------------------------

        private static object? Evaluate(FunctionWithReturnType body) =>
            NSGetterEvaluator.Evaluate(
                body,
                new NSGetterEvaluator.Context(
                    NeoTestSaveStack.LoadClient(
                        File.ReadAllText(
                            Path.Combine(PackageRoot, "synth-example.json"))),
                    thisValue: null,
                    rootValue: null));

        /// <summary>A getter that returns 7 under the supplied stamp.</summary>
        private static FunctionWithReturnType Body(int? compilerRevision)
        {
            var intType = new PrimitiveTypeInfo
            {
                type = MemberKind.Int,
                required = true,
            };
            return new FunctionWithReturnType
            {
                compilerRevision = compilerRevision,
                parameters = System.Array.Empty<Variable>(),
                typeInfo = intType,
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new ValuePointer
                        {
                            type = PointerKind.Value,
                            value = new Value
                            {
                                typeInfo = intType,
                                value = JToken.FromObject(7),
                            },
                        },
                    },
                },
            };
        }
    }
}
