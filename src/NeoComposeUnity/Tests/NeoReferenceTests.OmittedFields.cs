// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Parity = NeoCompose.Tests.ReferenceParity;

namespace NeoCompose.Tests
{
    public partial class NeoReferenceTests
    {
        [TestCase(true, true)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void NullableReferenceThroughIndexedClassGetterMatchesGeneratedCSharp(bool seasonal, bool core)
        {
            ProjectData data = OmittedReferenceData(seasonal, core);
            using var app = new Parity.TestProjectNeo(Client(data));
            NeoClient client = app.Client;
            string? expected = seasonal ? "seasonal-audio" : core ? "core-audio" : null;
            var context = new NSGetterEvaluator.Context(client, null, null, NeoValueOwnership.Asset);
            object? result = NSGetterEvaluator.Evaluate(OmittedReferenceGetter(data), context);
            Assert.AreEqual(expected, NSGetterEvaluator.FindRowIdByReference(result, context));
            if (expected is null)
                Assert.IsNull(result);
            var list = client.assets.Get<NeoMemberList>("Seasons");
            var index = new NeoUniqueListIndex<Parity.Mood, Parity.Gauge>(client, list, "Feel",
                (owner, node) => Parity.Gauge.Create(owner, (NeoMemberClass)node),
                Parity.MoodOptions.FromOptionId, Parity.MoodOptions.OptionId);
            Parity.Gauge current = index[Parity.Mood.Calm]!;
            Assert.AreEqual("omitted-season", current.valueId);
            Assert.AreEqual(seasonal ? "seasonal-audio" : null, current.TRef?.valueId);
            if (!seasonal)
                Assert.IsFalse(((ObjectMemberValue)data.values["omitted-season"]).value!.ContainsKey("TRef"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OmittedNullableReferenceWithExplicitEmptyDefaultMatchesGeneratedCSharp(bool emptyArray)
        {
            ProjectData data = OmittedReferenceData(false, false);
            SetOmittedReferenceDefault(data, emptyArray ? Array.Empty<string>() : null);
            using var app = new Parity.TestProjectNeo(Client(data));
            var context = new NSGetterEvaluator.Context(app.Client, null, null, NeoValueOwnership.Asset);
            Assert.IsNull(NSGetterEvaluator.Evaluate(OmittedReferenceGetter(data), context));
            var list = app.Client.assets.Get<NeoMemberList>("Seasons");
            Assert.IsNull(Parity.Gauge.Create(app.Client, (NeoMemberClass)list[0]).TRef);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptyStoredReferenceSelectionCoalesces(bool core)
        {
            ProjectData data = OmittedReferenceData(false, core);
            ((ObjectMemberValue)data.values["omitted-season"]).value!["TRef"] = "empty-reference";
            data.values["empty-reference"] = new ArrayMemberValue { id = "empty-reference", value = Array.Empty<string>() };
            using var app = new Parity.TestProjectNeo(Client(data));
            var context = new NSGetterEvaluator.Context(app.Client, null, null, NeoValueOwnership.Asset);
            object? result = NSGetterEvaluator.Evaluate(OmittedReferenceGetter(data), context);
            if (core)
                Assert.AreEqual("core-audio", NSGetterEvaluator.FindRowIdByReference(result, context));
            else
                Assert.IsNull(result);
            var list = app.Client.assets.Get<NeoMemberList>("Seasons");
            Assert.IsNull(Parity.Gauge.Create(app.Client, (NeoMemberClass)list[0]).TRef);
        }

        [Test]
        public void EmptyMultipleReferenceSelectionRemainsACollection()
        {
            ProjectData data = OmittedReferenceData(false, false);
            LookupMember picks = MemberNamed<LookupMember>(data, "Picks");
            Assert.AreEqual(NeoMemberSelectionKind.Multi, picks.Selection);
            picks.Requirement = NeoMemberRequirementKind.Optional;
            ((ObjectMemberValue)data.values["omitted-season"]).value!["Picks"] = "empty-references";
            data.values["empty-references"] = new ArrayMemberValue { id = "empty-references", value = Array.Empty<string>() };
            using var app = new Parity.TestProjectNeo(Client(data));
            var context = new NSGetterEvaluator.Context(app.Client, null, null, NeoValueOwnership.Asset);
            object? result = NSGetterEvaluator.Evaluate(OmittedGetter(OmittedKey(OmittedRow("omitted-season"), "Picks")), context);
            Assert.That(result, Is.InstanceOf<object[]>().And.Empty);
            var list = app.Client.assets.Get<NeoMemberList>("Seasons");
            Assert.AreEqual(0, Parity.Gauge.Create(app.Client, (NeoMemberClass)list[0]).Picks.Count);
        }

        [TestCase("Unknown")]
        [TestCase("Required")]
        [TestCase("Partial")]
        public void OmittedReferenceDoesNotHideInvalidMemberReads(string invalid)
        {
            ProjectData data = OmittedReferenceData(false, false);
            string key = invalid == "Unknown" ? "NotAField" : "TRef";
            if (invalid == "Required")
                MemberNamed<LookupMember>(data, "TRef").Requirement = NeoMemberRequirementKind.Required;
            if (invalid == "Partial")
                ((ClassMember)data.members["omitted-season-member"]).Payload = NeoMemberPayloadKind.Partial;
            var assetsMember = (ClassMember)data.members[data.project.rootAssetsMemberId];
            data.classes[assetsMember.DeclaredClassId!].schema["Season"] = "omitted-season-member";
            ((ObjectMemberValue)data.values[assetsMember.valueId!]).value!["Season"] = "omitted-season";
            using NeoClient client = Client(data);
            var context = new NSGetterEvaluator.Context(client, null, null, NeoValueOwnership.Asset);
            var getter = OmittedGetter(OmittedKey(OmittedKey(OmittedRow(assetsMember.valueId!), "Season"), key));
            StringAssert.Contains($"Missing key '{key}'", Assert.Catch(() => NSGetterEvaluator.Evaluate(getter, context)).Message);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void VirtualNullableReferenceMatchesGeneratedCSharp(bool assigned)
        {
            ProjectData data = OmittedReferenceData(false, false);
            SetOmittedReferenceDefault(data, assigned ? new[] { "seasonal-audio" } : null);
            var season = (ObjectMemberValue)data.values["omitted-season"];
            var template = (ObjectMemberValue)GRoot(data);
            season.instanceConstructorId = template.instanceConstructorId;
            season.constructorArgs = template.constructorArgs;
            using var app = new Parity.TestProjectNeo(Client(data));
            NeoClient client = app.Client;
            Assert.IsTrue(client.TryGetVirtualClassChildValueId(season.id, "TRef", out _), "Constructor replay supplies the omitted child.");
            var context = new NSGetterEvaluator.Context(client, null, null, NeoValueOwnership.Asset);
            object? result = NSGetterEvaluator.Evaluate(OmittedReferenceGetter(data), context);
            Assert.AreEqual(assigned ? "seasonal-audio" : null, NSGetterEvaluator.FindRowIdByReference(result, context));
            if (!assigned)
                Assert.IsNull(result);
            var list = client.assets.Get<NeoMemberList>("Seasons");
            Assert.AreEqual(assigned ? "seasonal-audio" : null, Parity.Gauge.Create(client, (NeoMemberClass)list[0]).TRef?.valueId);
            Assert.IsFalse(season.value!.ContainsKey("TRef"));
        }

        private static void SetOmittedReferenceDefault(ProjectData data, string[]? selection)
        {
            LookupMember original = MemberNamed<LookupMember>(data, "TRef");
            // Replace the parsed declaration so its wire-field presence includes the new default.
            data.members[original.id] = new LookupMember
            {
                id = original.id,
                name = original.name,
                kind = original.kind,
                declaredTypeInfo = original.declaredTypeInfo,
                defaultValue = new ArrayMemberValueBase { value = selection },
            };
        }

        private static ProjectData OmittedReferenceData(bool seasonal, bool core)
        {
            ProjectData data = Data();
            var assetsMember = (ClassMember)data.members[data.project.rootAssetsMemberId];
            var assetsClass = data.classes[assetsMember.DeclaredClassId!];
            var assets = (ObjectMemberValue)data.values[assetsMember.valueId!];
            string gaugeId = data.classes.Values.Single(c => c.name == "Gauge").id;
            string tallyId = data.classes.Values.Single(c => c.name == "Tally").id;
            assetsClass.schema["Seasons"] = "omitted-seasons-member";
            assetsClass.schema["CurrentSeason"] = "omitted-current-season";
            assetsClass.schema["CoreAudio"] = "omitted-core-audio";
            assets.value!["Seasons"] = "omitted-seasons";
            data.members["omitted-seasons-member"] = new ListMember
            {
                id = "omitted-seasons-member",
                name = "Seasons",
                kind = MemberKind.List,
                Requirement = NeoMemberRequirementKind.Required,
                entryMemberId = "omitted-season-member",
                indexes = new[] { new ListIndexDefinition { schemaKey = "Feel", Kind = NeoListIndexKind.Unique } },
            };
            data.members["omitted-season-member"] = new ClassMember
            {
                id = "omitted-season-member",
                name = "Season",
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                DeclaredClassId = gaugeId,
            };
            data.values["omitted-seasons"] = new ArrayMemberValue { id = "omitted-seasons", value = new[] { "omitted-season" } };
            var season = new ObjectMemberValue
            {
                id = "omitted-season",
                classId = gaugeId,
                value = new Dictionary<string, string> { ["Feel"] = "omitted-feel" },
            };
            data.values[season.id] = season;
            data.values["omitted-feel"] = new ArrayMemberValue
            {
                id = "omitted-feel",
                value = new[] { Parity.MoodOptions.OptionId(Parity.Mood.Calm) },
            };
            var referenceType = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = tallyId };
            data.members["omitted-core-audio"] = new LookupMember
            {
                id = "omitted-core-audio",
                name = "CoreAudio",
                kind = MemberKind.Lookup,
                Requirement = NeoMemberRequirementKind.Optional,
                declaredTypeInfo = referenceType,
            };
            foreach (string id in new[] { "seasonal-audio", "core-audio" })
            {
                assetsClass.schema[id] = id + "-member";
                assets.value[id] = id;
                data.members[id + "-member"] = new ClassMember
                {
                    id = id + "-member",
                    name = id,
                    kind = MemberKind.Class,
                    Requirement = NeoMemberRequirementKind.Required,
                    DeclaredClassId = tallyId,
                };
                data.values[id] = new ObjectMemberValue
                {
                    id = id,
                    classId = tallyId,
                    value = new Dictionary<string, string> { ["N"] = id + "-n" },
                };
                data.values[id + "-n"] = new NumberMemberValue { id = id + "-n", value = 1 };
                data.values[id + "-selection"] = new ArrayMemberValue { id = id + "-selection", value = new[] { id } };
            }
            if (seasonal)
                season.value["TRef"] = "seasonal-audio-selection";
            if (core)
                assets.value["CoreAudio"] = "core-audio-selection";
            var enumMember = MemberNamed<EnumMember>(data, "Feel");
            var selected = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new ListIndexFunction
                {
                    type = FunctionKind.ListIndex,
                    info = new FunctionListIndexInfo
                    {
                        collectionPointer = OmittedRow("omitted-seasons"),
                        listMemberId = "omitted-seasons-member",
                        schemaKey = "Feel",
                        unique = true,
                        keyKind = ListIndexKeyKind.Enum,
                        keyEnumId = enumMember.enumId,
                        keyPointer = new ValuePointer
                        {
                            type = PointerKind.Value,
                            value = new Value
                            {
                                typeInfo = new EnumTypeInfo { type = MemberKind.Enum, required = true, enumId = enumMember.enumId },
                                value = new JValue(Parity.MoodOptions.OptionId(Parity.Mood.Calm)),
                            },
                        },
                    },
                },
            };
            data.members["omitted-current-season"] = new NSPropertyMember
            {
                id = "omitted-current-season",
                name = "CurrentSeason",
                kind = MemberKind.NSProperty,
                getter = OmittedGetter(selected, new ClassTypeInfo { type = MemberKind.Class, required = true, classId = gaugeId }),
            };
            return data;
        }

        private static FunctionWithReturnType OmittedReferenceGetter(ProjectData data)
        {
            string assetsId = data.members[data.project.rootAssetsMemberId].valueId!;
            var current = new CallGetterPointer
            {
                type = PointerKind.CallGetter,
                memberId = "omitted-current-season",
                receiver = CallReceiver.Instance(OmittedRow(assetsId)),
            };
            return OmittedGetter(new CoalescePointer
            {
                type = PointerKind.Coalesce,
                left = OmittedKey(current, "TRef", MemberNamed<LookupMember>(data, "TRef").id),
                right = OmittedKey(OmittedRow(assetsId), "CoreAudio", "omitted-core-audio"),
            });
        }

        private static ReferencePointer OmittedRow(string id) => new() { type = PointerKind.Reference, valueId = id };

        private static KeyOfPointer OmittedKey(Pointer receiver, string key, string? memberId = null) => new()
        {
            type = PointerKind.KeyOf,
            memberId = memberId,
            keyOf = new KeyOf
            {
                pointer = receiver,
                key = new ValuePointer
                {
                    type = PointerKind.Value,
                    value = new Value { typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true }, value = new JValue(key) },
                },
            },
        };

        private static FunctionWithReturnType OmittedGetter(Pointer pointer, TypeInfo? type = null) => new()
        {
            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
            parameters = Array.Empty<Variable>(),
            typeInfo = type ?? new UnknownTypeInfo { type = MemberKind.Unknown },
            instructions = new Instruction[] { new ReturnInstruction { type = InstructionKind.Return, pointer = pointer } },
        };
    }
}
