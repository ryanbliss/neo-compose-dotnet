// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace HelloWorld.Assets.Tests.PlayMode
{
    /// <summary>
    /// Player-compatible constructor profile shaped like a character animation
    /// rig. One small root expression eagerly builds six parts, each part builds
    /// six clips, and each clip builds six tracks. Every track owns four stored
    /// scalar rows, so one evaluation publishes 259 Class rows and 1,166 total
    /// Session rows through the same constructor/ownership path used by authored
    /// NeoScript in a game.
    ///
    /// <para>The graph intentionally lives in the .NET PlayMode suite instead of
    /// in a Neowyn project fixture. It therefore measures the SDK evaluator that
    /// executes on device, while retaining the recursive eager-initializer shape
    /// that exposed the web evaluator's constructor scaling problem.</para>
    /// </summary>
    public sealed class NeoScriptConstructorPerformanceTests
    {
        private const string ProjectId = "constructor-performance-project";
        private const string RootClassId = "constructor-performance-root";
        private const string RigClassId = "constructor-performance-rig";
        private const string PartClassId = "constructor-performance-part";
        private const string ClipClassId = "constructor-performance-clip";
        private const string TrackClassId = "constructor-performance-track";

        private const string RigPartsMemberId = "constructor-performance-rig-parts";
        private const string RigPartEntryMemberId = "constructor-performance-rig-part-entry";
        private const string PartClipsMemberId = "constructor-performance-part-clips";
        private const string PartClipEntryMemberId = "constructor-performance-part-clip-entry";
        private const string ClipTracksMemberId = "constructor-performance-clip-tracks";
        private const string ClipTrackEntryMemberId = "constructor-performance-clip-track-entry";

        private const int PartsPerRig = 6;
        private const int ClipsPerPart = 6;
        private const int TracksPerClip = 6;
        private const int WarmupCount = 4;
        private const int MeasurementCount = 15;

        [UnityTest]
        public IEnumerator AnimationShapedConstructorGraph_ProfilePlayerEvaluator()
        {
            Assert.IsTrue(
                Application.isPlaying,
                "This constructor profile must run through the PlayMode test runner.");
            yield return null;

            FunctionWithReturnType getter = RigGetter();
            ProjectData schema = BuildProjectData();

            for (int index = 0; index < WarmupCount; index++)
            {
                _ = Measure(schema, getter);
            }

            var samples = new Measurement[MeasurementCount];
            for (int index = 0; index < MeasurementCount; index++)
            {
                samples[index] = Measure(schema, getter);
            }

            Measurement median = samples
                .OrderBy(sample => sample.DurationMs)
                .ElementAt(samples.Length / 2);
            string durationSamples = string.Join(",", samples.Select(
                sample => sample.DurationMs.ToString("F3")));
            TestContext.WriteLine(
                "CONSTRUCTOR_DOTNET_PROFILE " +
                $"classes=259 rows=1166 " +
                $"medianDurationMs={median.DurationMs:F3} " +
                $"durationSamplesMs=[{durationSamples}]");
        }

        private static Measurement Measure(
            ProjectData schema,
            FunctionWithReturnType getter)
        {
            using var client = new NeoClient(new SchemaOnlyLoader(schema));
            var context = new NSGetterEvaluator.Context(client, null, null);

            var stopwatch = Stopwatch.StartNew();
            object? result = NSGetterEvaluator.Evaluate(getter, context);
            stopwatch.Stop();
            AssertGraphShape(client, result);
            GC.KeepAlive(result);
            return new Measurement(stopwatch.Elapsed.TotalMilliseconds);
        }

        private static void AssertGraphShape(
            NeoClient client,
            object? result)
        {
            Assert.IsInstanceOf<IDictionary<string, object?>>(result);
            Assert.IsInstanceOf<INeoValueReference>(result);
            string? rootId = ((INeoValueReference)result!).valueId;
            Assert.IsNotNull(rootId, "The constructed Rig has no backing row.");

            using var rig = new NeoMemberClassWritable(
                client,
                new ClassMember
                {
                    id = "__neo_constructor_profile_rig",
                    name = "Rig",
                    kind = MemberKind.Class,
                    required = true,
                    classId = RigClassId,
                },
                rootId,
                NeoValueOwnership.Session);
            var seen = new HashSet<string>();
            RegisterRow(rig, seen);
            NeoMemberList parts = rig.Get<NeoMemberList>("Parts");
            RegisterRow(parts, seen);
            Assert.AreEqual(PartsPerRig, parts.Count);
            foreach (NeoMember partMember in parts)
            {
                Assert.IsInstanceOf<NeoMemberClass>(partMember);
                var part = (NeoMemberClass)partMember;
                RegisterRow(part, seen);
                NeoMemberList clips = part.Get<NeoMemberList>("Clips");
                RegisterRow(clips, seen);
                Assert.AreEqual(ClipsPerPart, clips.Count);
                foreach (NeoMember clipMember in clips)
                {
                    Assert.IsInstanceOf<NeoMemberClass>(clipMember);
                    var clip = (NeoMemberClass)clipMember;
                    RegisterRow(clip, seen);
                    NeoMemberList tracks = clip.Get<NeoMemberList>("Tracks");
                    RegisterRow(tracks, seen);
                    Assert.AreEqual(TracksPerClip, tracks.Count);
                    foreach (NeoMember trackMember in tracks)
                    {
                        Assert.IsInstanceOf<NeoMemberClass>(trackMember);
                        var track = (NeoMemberClass)trackMember;
                        RegisterRow(track, seen);
                        int scalarCount = 0;
                        foreach (var pair in track)
                        {
                            scalarCount++;
                            RegisterRow(pair.Value, seen);
                        }
                        Assert.AreEqual(4, scalarCount);
                    }
                }
            }
            Assert.AreEqual(
                1166,
                seen.Count,
                "The measured graph did not retain its complete owned row tree.");
        }

        private static void RegisterRow(
            NeoMember member,
            HashSet<string> seen)
        {
            Assert.IsNotNull(member.overrideValueId);
            Assert.IsTrue(
                seen.Add(member.overrideValueId!),
                $"Owned constructor row '{member.overrideValueId}' appears more than once.");
            Assert.IsNotNull(
                member.value,
                $"Constructed row '{member.overrideValueId}' is unavailable.");
        }

        private static FunctionWithReturnType RigGetter()
        {
            return ReturnFunction(
                ClassType(RigClassId),
                Constructor(RigClassId));
        }

        private static FunctionWithReturnType ReturnFunction(
            TypeInfo typeInfo,
            Pointer pointer)
        {
            return new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = typeInfo,
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = pointer,
                    },
                },
            };
        }

        private static FunctionPointer Constructor(string classId)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new ClassConstructorFunction
                {
                    type = FunctionKind.ClassConstructor,
                    info = new FunctionClassConstructorInfo
                    {
                        schemaClassInfo = ClassType(classId),
                        fields = Array.Empty<FunctionClassConstructorField>(),
                    },
                },
            };
        }

        private static ClassTypeInfo ClassType(string classId)
        {
            return new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = classId,
            };
        }

        private static CollectionTypeInfo ListType(string entryClassId)
        {
            return new CollectionTypeInfo
            {
                type = MemberKind.List,
                required = true,
                entryTypeInfo = ClassType(entryClassId),
            };
        }

        private static InitializerBody ConstructedListInitializer(
            string entryClassId,
            int count)
        {
            var entries = new Pointer[count];
            for (int index = 0; index < entries.Length; index++)
            {
                entries[index] = Constructor(entryClassId);
            }
            CollectionTypeInfo listType = ListType(entryClassId);
            return new InitializerBody
            {
                code = $"[{string.Join(", ", Enumerable.Repeat($"new {entryClassId}()", count))}]",
                compiled = ReturnFunction(
                    listType,
                    new ListLiteralPointer
                    {
                        type = PointerKind.ListLiteral,
                        typeInfo = listType,
                        entries = entries,
                    }),
            };
        }

        private static ProjectData BuildProjectData()
        {
            var classes = new Dictionary<string, NeoSchemaClass>();
            var members = new Dictionary<string, JsonMember>();

            classes[RootClassId] = Class(RootClassId, "Root");
            classes[RigClassId] = Class(
                RigClassId,
                "AnimationRig",
                ("Parts", RigPartsMemberId));
            classes[PartClassId] = Class(
                PartClassId,
                "AnimationPart",
                ("Clips", PartClipsMemberId));
            classes[ClipClassId] = Class(
                ClipClassId,
                "AnimationClip",
                ("Tracks", ClipTracksMemberId));
            classes[TrackClassId] = Class(
                TrackClassId,
                "AnimationTrack",
                ("StartFrame", "constructor-performance-track-start"),
                ("OffsetStartIndex", "constructor-performance-track-offset-start"),
                ("OffsetEndIndex", "constructor-performance-track-offset-end"),
                ("Reverse", "constructor-performance-track-reverse"));

            members[RigPartsMemberId] = ConstructedClassListMember(
                RigPartsMemberId,
                "Parts",
                RigPartEntryMemberId,
                PartClassId,
                ConstructedListInitializer(PartClassId, PartsPerRig));
            members[RigPartEntryMemberId] = ClassEntryMember(
                RigPartEntryMemberId,
                PartClassId);
            members[PartClipsMemberId] = ConstructedClassListMember(
                PartClipsMemberId,
                "Clips",
                PartClipEntryMemberId,
                ClipClassId,
                ConstructedListInitializer(ClipClassId, ClipsPerPart));
            members[PartClipEntryMemberId] = ClassEntryMember(
                PartClipEntryMemberId,
                ClipClassId);
            members[ClipTracksMemberId] = ConstructedClassListMember(
                ClipTracksMemberId,
                "Tracks",
                ClipTrackEntryMemberId,
                TrackClassId,
                ConstructedListInitializer(TrackClassId, TracksPerClip));
            members[ClipTrackEntryMemberId] = ClassEntryMember(
                ClipTrackEntryMemberId,
                TrackClassId);

            members["constructor-performance-track-start"] = IntMember(
                "constructor-performance-track-start",
                "StartFrame",
                0);
            members["constructor-performance-track-offset-start"] = IntMember(
                "constructor-performance-track-offset-start",
                "OffsetStartIndex",
                0);
            members["constructor-performance-track-offset-end"] = IntMember(
                "constructor-performance-track-offset-end",
                "OffsetEndIndex",
                3);
            members["constructor-performance-track-reverse"] = new BoolMember
            {
                id = "constructor-performance-track-reverse",
                projectId = ProjectId,
                name = "Reverse",
                kind = MemberKind.Bool,
                required = true,
                defaultValue = new BoolMemberValueBase { value = false },
            };

            ClassMember rootAssets = RootMember(
                "constructor-performance-assets",
                "Assets",
                "immutable",
                "constructor-performance-assets-value");
            ClassMember rootSave = RootMember(
                "constructor-performance-save",
                "Save",
                "save",
                "constructor-performance-save-value");
            ClassMember rootSession = RootMember(
                "constructor-performance-session",
                "Session",
                "session",
                "constructor-performance-session-value");
            members[rootAssets.id] = rootAssets;
            members[rootSave.id] = rootSave;
            members[rootSession.id] = rootSession;

            return new ProjectData
            {
                metadata = new ProjectExportMetadata
                {
                    schemaVersion = NeoProjectExportContract.CurrentSchemaVersion,
                    projectId = ProjectId,
                    versionId = "constructor-performance-version",
                },
                project = new Project
                {
                    id = ProjectId,
                    _id = ProjectId,
                    name = "NeoScript constructor performance",
                    rootAssetsMemberId = rootAssets.id,
                    rootSaveFileMemberId = rootSave.id,
                    rootSessionMemberId = rootSession.id,
                },
                classes = classes,
                members = members,
                values = new Dictionary<string, MemberValue>
                {
                    [rootAssets.valueId!] = ObjectValue(rootAssets.valueId!),
                    [rootSave.valueId!] = ObjectValue(rootSave.valueId!),
                    [rootSession.valueId!] = ObjectValue(rootSession.valueId!),
                },
                constructors = new Dictionary<string, ConstructorRecord>(),
                internalRecordRelations =
                    new Dictionary<string, InternalRecordRelation>(),
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static NeoSchemaClass Class(
            string id,
            string name,
            params (string SchemaKey, string MemberId)[] fields)
        {
            return new NeoSchemaClass
            {
                id = id,
                projectId = ProjectId,
                name = name,
                schema = fields.ToDictionary(
                    field => field.SchemaKey,
                    field => field.MemberId),
            };
        }

        private static ListMember ConstructedClassListMember(
            string id,
            string name,
            string entryMemberId,
            string entryClassId,
            InitializerBody initializer)
        {
            return new ListMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.List,
                required = true,
                entryMemberId = entryMemberId,
                defaultValue = new ArrayMemberValueBase { init = initializer },
            };
        }

        private static ClassMember ClassEntryMember(string id, string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = ProjectId,
                name = "Entry",
                kind = MemberKind.Class,
                required = true,
                classId = classId,
            };
        }

        private static IntMember IntMember(string id, string name, double value)
        {
            return new IntMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Int,
                required = true,
                defaultValue = new NumberMemberValueBase { value = value },
            };
        }

        private static ClassMember RootMember(
            string id,
            string name,
            string storage,
            string valueId)
        {
            return new ClassMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Class,
                required = true,
                classId = RootClassId,
                storage = storage,
                valueId = valueId,
            };
        }

        private static ObjectMemberValue ObjectValue(string id)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = RootClassId,
                value = new Dictionary<string, string>(),
            };
        }

        private sealed class SchemaOnlyLoader : INeoSaveLoader
        {
            internal SchemaOnlyLoader(ProjectData schema)
            {
                Schema = schema;
            }

            public ProjectData Schema { get; }
            public string CustomId => "constructor-performance-save";
            public Awaitable<string?> LoadSaveContentAsync() =>
                NeoAwaitable.FromResult<string?>(null);
            public Awaitable CommitSaveContentAsync(
                string content,
                bool replaceSnapshot) => NeoAwaitable.Completed();
        }

        private readonly struct Measurement
        {
            internal Measurement(double durationMs)
            {
                DurationMs = durationMs;
            }

            internal double DurationMs { get; }
        }
    }
}
