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
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    public class P75VirtualInstanceValueTests
    {
        [Test]
        public void SparseInstanceTracksDefaultAndWritesAtStableVirtualId()
        {
            using NeoClient first = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            NeoMemberClassWritable firstThing =
                first.save.Get<NeoMemberClassWritable>("Thing");
            NeoMemberIntWritable firstCount =
                firstThing.Get<NeoMemberIntWritable>("Count");

            Assert.AreEqual(5d, firstCount.value!.value);
            string virtualId = firstCount.value.id;
            Assert.AreEqual("35f55577-5ef0-5bf5-861b-070aa19817f5", virtualId);

            firstCount.Set(9);

            Assert.AreEqual(virtualId, firstCount.value!.id);
            Assert.AreEqual(9d, firstCount.value.value);
            Assert.AreEqual(virtualId, first.saveValues[virtualId].id);
            // The authored root stays sparse: runtime writes shadow the leaf
            // directly and do not materialize a parent spine into the save.
            Assert.AreEqual(1, first.saveValues.Count);

            using NeoClient second = NeoTestSaveStack.ClientFromSchema(BuildProjectData(7));
            NeoMemberIntWritable secondCount = second.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count");
            Assert.AreEqual(7d, secondCount.value!.value);
            Assert.AreEqual(virtualId, secondCount.value.id);
        }

        [Test]
        public void SparseSpineRowsAtVirtualIdsPreserveDeeperOverrides()
        {
            string nestedId;
            string deepId;
            string countId;
            using (NeoClient probe = NeoTestSaveStack.ClientFromSchema(
                BuildNestedProjectData()))
            {
                NeoMemberClassWritable nested = probe.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberClassWritable>("Nested");
                NeoMemberClassWritable deep = nested
                    .Get<NeoMemberClassWritable>("Deep");
                nestedId = nested.value!.id;
                deepId = deep.value!.id;
                countId = deep.Get<NeoMemberIntWritable>("Count").value!.id;
            }

            ProjectData data = BuildNestedProjectData();
            // Web sparse writes keep Class spine rows at their virtual ids,
            // without reattaching them to each ancestor body.
            data.values[nestedId] = ObjectValue(nestedId, "nested-class");
            data.values[deepId] = ObjectValue(deepId, "deep-class");
            data.values[countId] = new NumberMemberValue
            {
                id = countId,
                value = 91,
            };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoMemberClassWritable nestedValue = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberClassWritable>("Nested");
            NeoMemberClassWritable deepValue = nestedValue
                .Get<NeoMemberClassWritable>("Deep");

            Assert.AreEqual(nestedId, nestedValue.value!.id);
            Assert.AreEqual(deepId, deepValue.value!.id);
            Assert.AreEqual(
                91d,
                deepValue.Get<NeoMemberIntWritable>("Count").value!.value);
        }

        [Test]
        public void SystemRootPropagatesItsNamespaceToVirtualChildren()
        {
            ProjectData data = BuildProjectData();
            ObjectMemberValue root = (ObjectMemberValue)data.values["thing-instance"];
            data.values.Remove(root.id);
            root.id = "system_thing-instance";
            data.values[root.id] = root;
            ((ObjectMemberValue)data.values["value-save"]).value!["Thing"] = root.id;

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            string countId = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count")
                .value!.id;

            Assert.AreEqual(
                "system_35f55577-5ef0-5bf5-861b-070aa19817f5",
                countId);
        }

        [Test]
        public void ExternalSaveApplyRebuildsSparseVirtualSpines()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildNestedProjectData());
            NeoMemberClassWritable thing = client.save
                .Get<NeoMemberClassWritable>("Thing");
            NeoMemberClassWritable nested = thing
                .Get<NeoMemberClassWritable>("Nested");
            NeoMemberClassWritable deep = nested
                .Get<NeoMemberClassWritable>("Deep");
            string nestedId = nested.value!.id;
            string deepId = deep.value!.id;
            string countId = deep.Get<NeoMemberIntWritable>("Count").value!.id;

            JObject incoming = JObject.Parse(client.SerializeSaveData());
            var values = (JObject)incoming["values"]!;
            values[nestedId] = JObject.FromObject(ObjectValue(
                nestedId,
                "nested-class"));
            values[deepId] = JObject.FromObject(ObjectValue(
                deepId,
                "deep-class"));
            values[countId] = JObject.FromObject(new NumberMemberValue
            {
                id = countId,
                value = 73,
            });

            client.ApplyExternalSaveContent(incoming.ToString());

            Assert.AreEqual(
                73d,
                thing
                    .Get<NeoMemberClassWritable>("Nested")
                    .Get<NeoMemberClassWritable>("Deep")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value);
        }

        [Test]
        public void ResolveEffectiveRowReadsVirtualValuesForUntypedConsumers()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            NeoMemberIntWritable count = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count");

            NumberMemberValue row = (NumberMemberValue)client.ResolveEffectiveRow(
                count.value!.id)!;

            Assert.AreEqual(5d, row.value);
            Assert.AreEqual(count.value.id, row.id);
        }

        [Test]
        public void VirtualUnorderedListRegistersItsEntriesAndContainment()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildUnorderedListProjectData());
            NeoMemberList items = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberList>("Items");

            string[] entryIds = items
                .Select(item => item.value!.id)
                .OrderBy(id => id, System.StringComparer.Ordinal)
                .ToArray();

            Assert.AreEqual(2, entryIds.Length);
            CollectionAssert.AreEqual(
                entryIds,
                client.GetUnorderedListEntryIds(items.value!.id).ToArray());
            foreach (string entryId in entryIds)
            {
                Assert.IsTrue(client.TryResolveContainerIdForValueId(
                    entryId,
                    out string? containerId));
                Assert.AreEqual(items.value.id, containerId);
                Assert.IsInstanceOf<StringMemberValue>(
                    client.ResolveEffectiveRow(entryId));
            }
        }

        [Test]
        public void SparseReplayFillsAnOmittedDefaultedConstructorArgument()
        {
            ProjectData data = BuildProjectData();
            ObjectMemberValue root = (ObjectMemberValue)data.values["thing-instance"];
            root.instanceConstructorId = "thing-ctor";
            root.constructorArgs = new Dictionary<string, JToken?>();
            data.classes["thing-class"].constructorIds = new[] { "thing-ctor" };
            var optional = new FunctionArgumentTypeInfo
            {
                name = "Label",
                type = MemberKind.String,
                required = true,
                defaultValue = new ParameterDefaultValue { value = "default" },
            };
            data.constructors = new Dictionary<string, ConstructorRecord>
            {
                ["thing-ctor"] = new ConstructorRecord
                {
                    id = "thing-ctor",
                    projectId = "p75-project",
                    classId = "thing-class",
                    argumentTypes = new[] { optional },
                    action = new FunctionWithReturnType
                    {
                        compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                        parameters = new[]
                        {
                            new Variable
                            {
                                id = "__this__",
                                typeInfo = new ClassTypeInfo
                                {
                                    type = MemberKind.Class,
                                    required = true,
                                    classId = "thing-class",
                                },
                            },
                            new Variable
                            {
                                id = "__root__",
                                typeInfo = new ClassTypeInfo
                                {
                                    type = MemberKind.Class,
                                    required = true,
                                    classId = "save-root-class",
                                },
                            },
                            new Variable { id = "__arg_0__", typeInfo = optional },
                        },
                        typeInfo = new PrimitiveTypeInfo
                        {
                            type = MemberKind.Null,
                            required = true,
                        },
                        instructions = System.Array.Empty<Instruction>(),
                    },
                },
            };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            Assert.AreEqual(
                5d,
                client.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value);
        }

        [Test]
        public void ConstructorProvenanceRoundTripsExplicitImplicitNull()
        {
            const string json = @"{
  'id':'instance',
  'classId':'thing-class',
  'value':{},
  'constructorArgs':{},
  'instanceConstructorId':null,
  'createdAt':'2026-08-22T00:00:00.000Z',
  'updatedAt':'2026-08-22T00:00:00.000Z'
}";
            MemberValue row = Newtonsoft.Json.JsonConvert
                .DeserializeObject<MemberValue>(json)!;

            Assert.IsTrue(row.hasInstanceConstructorId);
            Assert.IsNull(row.instanceConstructorId);
            StringAssert.Contains(
                "\"instanceConstructorId\":null",
                Newtonsoft.Json.JsonConvert.SerializeObject(row));
        }

        [Test]
        public void PersistedSparseRootOutsideAClassPlacementFailsClosed()
        {
            ProjectData data = BuildProjectData();
            ObjectMemberValue orphan = ObjectValue("orphan-instance", "thing-class");
            orphan.constructorArgs = new Dictionary<string, JToken?>();
            orphan.instanceConstructorId = null;
            data.values[orphan.id] = orphan;

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => NeoTestSaveStack.ClientFromSchema(data))!;

            StringAssert.Contains("not reachable through a Class member", error.ToString());
        }

        [Test]
        public void RuntimeConstructionStampsTheCanonicalProvenancePair()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());

            NeoMemberClassWritable constructed =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "thing-class",
                    constructorId: null,
                    Array.Empty<NeoDeclaredConstructorArgument>());

            ObjectMemberValue row = constructed.value!;
            Assert.IsTrue(
                row.hasInstanceConstructorId,
                "An implicitly constructed row must carry an EXPLICIT null constructor id.");
            Assert.IsNull(row.instanceConstructorId);
            Assert.IsNotNull(row.constructorArgs);
            Assert.AreEqual(0, row.constructorArgs!.Count);
            Assert.IsTrue(NeoClient.IsVirtualInstanceRoot(row));
            StringAssert.Contains(
                "\"instanceConstructorId\":null",
                Newtonsoft.Json.JsonConvert.SerializeObject(row));
        }

        [Test]
        public void ConstructorArgsAloneMakeARowASparseInstanceRoot()
        {
            ProjectData data = BuildProjectData();
            // The web's schema-impact repair can rewrite a row's arguments
            // without re-stamping the constructor id.
            var root = (ObjectMemberValue)data.values["thing-instance"];
            data.values["thing-instance"] = Newtonsoft.Json.JsonConvert
                .DeserializeObject<ObjectMemberValue>(@"{
  'id':'thing-instance',
  'classId':'thing-class',
  'value':{},
  'constructorArgs':{},
  'createdAt':'2026-08-22T00:00:00.000Z',
  'updatedAt':'2026-08-22T00:00:00.000Z'
}".Replace('\'', '"'))!;
            Assert.IsFalse(data.values["thing-instance"].hasInstanceConstructorId);
            Assert.AreEqual("thing-class", root.classId);

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            Assert.AreEqual(
                5d,
                client.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value);
        }

        [Test]
        public void ClearingAVariantToBaseKeepsTheInstanceExpanding()
        {
            ProjectData data = BuildProjectData();
            // A variant-only root: the web stamped the selected variant and
            // nothing else, so `instanceVariantId` is its ONLY eligibility
            // marker.
            var authored = (ObjectMemberValue)data.values["thing-instance"];
            data.values["thing-instance"] = ObjectValue(authored.id, authored.classId!);

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoMemberClassWritable thing = client.save
                .Get<NeoMemberClassWritable>("Thing");
            ((ObjectMemberValue)client.values["thing-instance"]).instanceVariantId =
                "thing-variant";

            client.StampVirtualInstanceVariant(
                thing,
                NeoValueOwnership.Save,
                variantId: null,
                rowValueId: null);

            NeoMemberIntWritable count = thing.Get<NeoMemberIntWritable>("Count");
            Assert.AreEqual(5d, count.value!.value);
            string virtualId = count.value.id;

            string content = client.SerializeSaveData();
            var written = (JObject)JObject.Parse(content)["values"]!["thing-instance"]!;
            Assert.IsTrue(
                written.TryGetValue("instanceConstructorId", out JToken? stampedId),
                "Clearing to Base must leave the row a valid P75 root.");
            Assert.AreEqual(JTokenType.Null, stampedId!.Type);
            Assert.IsNotNull(written["constructorArgs"]);

            client.ApplyExternalSaveContent(content);

            NeoMemberIntWritable reapplied = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count");
            Assert.AreEqual(5d, reapplied.value!.value);
            Assert.AreEqual(virtualId, reapplied.value.id);
        }

        [Test]
        public void GarbageCollectorKeepsOverridesWrittenUnderASparseSpine()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildNestedProjectData());
            NeoMemberIntWritable count = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberClassWritable>("Nested")
                .Get<NeoMemberClassWritable>("Deep")
                .Get<NeoMemberIntWritable>("Count");

            count.Set(42);
            string virtualId = count.value!.id;
            Assert.IsTrue(client.saveValues.ContainsKey(virtualId));

            // The spine is sparse by design: no ancestor body links this id,
            // so only the virtual index can prove it reachable.
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
            Assert.AreEqual(0, client.RunGarbageCollector());

            Assert.IsTrue(client.saveValues.ContainsKey(virtualId));
            Assert.AreEqual(
                42d,
                client.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberClassWritable>("Nested")
                    .Get<NeoMemberClassWritable>("Deep")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value);
        }

        [Test]
        public void LiveApplyScopesAMalformedRootAndKeepsTheOthers()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildTwoRootProjectData());
            Assert.AreEqual(
                5d,
                client.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value);

            JObject incoming = JObject.Parse(client.SerializeSaveData());
            var values = (JObject)incoming["values"]!;
            // A root whose recipe cannot resolve. Before scoping, this
            // exception escaped the whole apply and left the index gutted.
            values["other-instance"] = JObject.Parse(@"{
  'id':'other-instance',
  'classId':'thing-class',
  'value':{},
  'constructorArgs':{},
  'instanceConstructorId':'missing-ctor',
  'createdAt':'2026-08-22T00:00:00.000Z',
  'updatedAt':'2026-08-22T00:00:00.000Z'
}".Replace('\'', '"'));

            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex(
                    "could not replay instance root 'other-instance'"));
            client.ApplyExternalSaveContent(incoming.ToString());

            Assert.AreEqual(
                5d,
                client.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value,
                "A malformed root must not cost every other root its virtual values.");
        }

        [Test]
        public void FullVirtualRebuildRetiresWrappersHoldingTheOldExpansion()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildNestedProjectData());
            NeoMemberClassWritable thing = client.save
                .Get<NeoMemberClassWritable>("Thing");
            NeoMemberIntWritable held = thing
                .Get<NeoMemberClassWritable>("Nested")
                .Get<NeoMemberClassWritable>("Deep")
                .Get<NeoMemberIntWritable>("Count");
            Assert.AreEqual(5d, held.value!.value);
            string countId = held.value.id;

            JObject incoming = JObject.Parse(client.SerializeSaveData());
            ((JObject)incoming["values"]!)[countId] = JObject.FromObject(
                new NumberMemberValue { id = countId, value = 73 });

            client.ApplyExternalSaveContent(incoming.ToString());

            // A rebuild mints new rows at the same deterministic ids, so a
            // wrapper that survives keeps serving the previous expansion while
            // the resolver serves the new one.
            Assert.IsTrue(
                held.isDisposed,
                "A wrapper bound to a replaced virtual row must be retired, not left serving the old value.");
            Assert.AreEqual(
                73d,
                thing
                    .Get<NeoMemberClassWritable>("Nested")
                    .Get<NeoMemberClassWritable>("Deep")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value);
        }

        // -------------------------------------------------------------------
        // Three-way id parity. Every literal below is uuidv5 (RFC 4122,
        // SHA-1, big-endian) of "{bareRootId}:{sourceIdentity}" under the P75
        // namespace 3e8ca0b3-e3f1-5d5f-bf2f-6ab5ee3896d0, so the TypeScript
        // suite can assert the same strings from the same two inputs.
        // -------------------------------------------------------------------

        [Test]
        public void UnorderedListEntryIdsFollowTheDeclaredDefaultOrder()
        {
            // The parity risk: C# indexes unordered entries in wrapper
            // enumeration order and TypeScript in resolver visit order. A
            // replayed entry carries no authored-child provenance, so the id
            // falls back to the POSITIONAL identity and the two runtimes agree
            // only while both walk the declared default in its own order. The
            // literals below pin that walk; the entry VALUE ids the fixture
            // declares deliberately do not appear in them.
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildUnorderedListProjectData("entry-a", "entry-b", "entry-c"));
            NeoMemberList items = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberList>("Items");

            string[] entryIds = items
                .Select(item => item.value!.id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(
                new[]
                {
                    // path:thing-item:$/{class Items}/{list 1}
                    "3e2e712d-a6f4-596c-9add-a0f2457f78c0",
                    // path:thing-item:$/{class Items}/{list 2}
                    "4b374fc6-bee8-5552-a484-70e70e67c4e5",
                    // path:thing-item:$/{class Items}/{list 0}
                    "631ac656-2e46-555b-8664-420a080fc2a0",
                },
                entryIds);
        }

        [Test]
        public void PositionalSourceIdentitySpellsAnIdLessMemberAsInline()
        {
            var row = new NumberMemberValue { id = "unused" };
            var named = new IntMember
            {
                id = "thing-count",
                name = "Count",
                kind = MemberKind.Int,
            };
            var inline = new IntMember { id = string.Empty, name = "Count", kind = MemberKind.Int };
            const string path = "$/{\"kind\":\"class\",\"schemaKey\":\"Count\"}";

            Assert.AreEqual(
                "path:thing-count:" + path,
                NeoClient.VirtualSourceIdentity(row, named, path));
            Assert.AreEqual(
                "path:<inline>:" + path,
                NeoClient.VirtualSourceIdentity(row, inline, path));
            Assert.AreEqual(
                "35f55577-5ef0-5bf5-861b-070aa19817f5",
                NeoClient.VirtualValueId(
                    "thing-instance",
                    NeoClient.VirtualSourceIdentity(row, named, path)));
            Assert.AreEqual(
                "77712819-a4bd-5105-a9bf-eb4925f94bc3",
                NeoClient.VirtualValueId(
                    "thing-instance",
                    NeoClient.VirtualSourceIdentity(row, inline, path)));
        }

        [Test]
        public void OrderedListEntryIdsArePositional()
        {
            const string listPath = "$/{\"kind\":\"class\",\"schemaKey\":\"Items\"}";
            var expected = new[]
            {
                "631ac656-2e46-555b-8664-420a080fc2a0",
                "3e2e712d-a6f4-596c-9add-a0f2457f78c0",
                "4b374fc6-bee8-5552-a484-70e70e67c4e5",
            };

            for (int index = 0; index < expected.Length; index++)
            {
                Assert.AreEqual(
                    expected[index],
                    NeoClient.VirtualValueId(
                        "thing-instance",
                        $"path:thing-item:{listPath}/{{\"kind\":\"list\",\"index\":{index}}}"),
                    $"Ordered entry {index} must derive from its index, not its identity.");
            }
        }

        [Test]
        public void LiveApplyReexpandsOnlyTheRootsThePatchTouches()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildTwoRootProjectData());
            NeoMemberIntWritable thingCount = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count");
            NeoMemberIntWritable otherCount = client.save
                .Get<NeoMemberClassWritable>("Other")
                .Get<NeoMemberIntWritable>("Count");
            Assert.AreEqual(5d, thingCount.value!.value);
            Assert.AreEqual(5d, otherCount.value!.value);
            string thingCountId = thingCount.value.id;

            JObject incoming = JObject.Parse(client.SerializeSaveData());
            ((JObject)incoming["values"]!)[thingCountId] = JObject.FromObject(
                new NumberMemberValue { id = thingCountId, value = 31 });

            client.ApplyExternalSaveContent(incoming.ToString());

            // Replaying the touched root retires its wrappers; every other
            // root keeps both its index entries and its live wrappers, which
            // is the observable difference between a scoped invalidation and
            // a full project re-expansion.
            Assert.IsTrue(thingCount.isDisposed);
            Assert.IsFalse(
                otherCount.isDisposed,
                "An untouched root must not be re-expanded by a patch that never reached it.");
            Assert.AreEqual(5d, otherCount.value!.value);
            Assert.AreEqual(
                31d,
                client.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value);
        }

        private static ProjectData BuildTwoRootProjectData()
        {
            ProjectData data = BuildProjectData();
            data.classes["save-root-class"].schema["Other"] = "other-member";
            data.members["other-member"] = new ClassMember
            {
                id = "other-member",
                projectId = "p75-project",
                name = "Other",
                kind = MemberKind.Class,
                classId = "thing-class",
                required = true,
                storage = "save",
            };
            ObjectMemberValue other = ObjectValue("other-instance", "thing-class");
            other.constructorArgs = new Dictionary<string, JToken?>();
            other.instanceConstructorId = null;
            data.values[other.id] = other;
            ((ObjectMemberValue)data.values["value-save"]).value!["Other"] = other.id;
            return data;
        }

        private static ProjectData BuildProjectData(double defaultCount = 5)
        {
            const string projectId = "p75-project";
            var assetsRootClass = SchemaClass("assets-root-class", "AssetsRoot", "immutable");
            var saveRootClass = SchemaClass("save-root-class", "SaveRoot", "save");
            var sessionRootClass = SchemaClass("session-root-class", "SessionRoot", "session");
            var thingClass = SchemaClass("thing-class", "Thing", "save");

            saveRootClass.schema["Thing"] = "thing-member";
            thingClass.schema["Count"] = "thing-count";

            var assetsRoot = RootMember(
                projectId,
                "assets-root",
                "Assets",
                assetsRootClass.id,
                "immutable",
                "value-assets");
            var saveRoot = RootMember(
                projectId,
                "save-root",
                "Save",
                saveRootClass.id,
                "save",
                "value-save");
            var sessionRoot = RootMember(
                projectId,
                "session-root",
                "Session",
                sessionRootClass.id,
                "session",
                "value-session");
            var thingMember = new ClassMember
            {
                id = "thing-member",
                projectId = projectId,
                name = "Thing",
                kind = MemberKind.Class,
                classId = thingClass.id,
                required = true,
                storage = "save",
            };
            var countMember = new IntMember
            {
                id = "thing-count",
                projectId = projectId,
                name = "Count",
                kind = MemberKind.Int,
                required = true,
                defaultValue = new NumberMemberValueBase { value = defaultCount },
            };
            var thing = ObjectValue("thing-instance", thingClass.id);
            thing.constructorArgs = new Dictionary<string, JToken?>();
            thing.instanceConstructorId = null;

            return new ProjectData
            {
                project = new Project
                {
                    id = projectId,
                    name = "P75",
                    rootAssetsMemberId = assetsRoot.id,
                    rootSaveFileMemberId = saveRoot.id,
                    rootSessionMemberId = sessionRoot.id,
                },
                members = new Dictionary<string, JsonMember>
                {
                    [assetsRoot.id] = assetsRoot,
                    [saveRoot.id] = saveRoot,
                    [sessionRoot.id] = sessionRoot,
                    [thingMember.id] = thingMember,
                    [countMember.id] = countMember,
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["value-assets"] = ObjectValue("value-assets", assetsRootClass.id),
                    ["value-save"] = ObjectValue(
                        "value-save",
                        saveRootClass.id,
                        new Dictionary<string, string> { ["Thing"] = thing.id }),
                    ["value-session"] = ObjectValue("value-session", sessionRootClass.id),
                    [thing.id] = thing,
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [assetsRootClass.id] = assetsRootClass,
                    [saveRootClass.id] = saveRootClass,
                    [sessionRootClass.id] = sessionRootClass,
                    [thingClass.id] = thingClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static ProjectData BuildUnorderedListProjectData(
            params string[] entryValueIds)
        {
            if (entryValueIds.Length == 0)
                entryValueIds = new[] { "thing-item-a", "thing-item-b" };
            ProjectData data = BuildProjectData();
            data.classes["thing-class"].schema.Remove("Count");
            data.classes["thing-class"].schema["Items"] = "thing-items";
            data.members.Remove("thing-count");
            data.members["thing-item"] = new StringMember
            {
                id = "thing-item",
                projectId = "p75-project",
                name = "Item",
                kind = MemberKind.String,
                required = true,
            };
            data.members["thing-items"] = new ListMember
            {
                id = "thing-items",
                projectId = "p75-project",
                name = "Items",
                kind = MemberKind.List,
                required = true,
                listKind = NeoListKinds.Unordered,
                entryMemberId = "thing-item",
                defaultValue = new ArrayMemberValueBase { value = entryValueIds },
            };
            for (int index = 0; index < entryValueIds.Length; index++)
            {
                data.values[entryValueIds[index]] = new StringMemberValue
                {
                    id = entryValueIds[index],
                    value = ((char)('A' + index)).ToString(),
                };
            }
            return data;
        }

        private static ProjectData BuildNestedProjectData()
        {
            ProjectData data = BuildProjectData();
            data.classes["thing-class"].schema.Remove("Count");
            data.classes["thing-class"].schema["Nested"] = "thing-nested";
            data.members.Remove("thing-count");
            data.members["thing-nested"] = new ClassMember
            {
                id = "thing-nested",
                projectId = "p75-project",
                name = "Nested",
                kind = MemberKind.Class,
                classId = "nested-class",
                required = true,
            };
            data.members["nested-deep"] = new ClassMember
            {
                id = "nested-deep",
                projectId = "p75-project",
                name = "Deep",
                kind = MemberKind.Class,
                classId = "deep-class",
                required = true,
            };
            data.members["deep-count"] = new IntMember
            {
                id = "deep-count",
                projectId = "p75-project",
                name = "Count",
                kind = MemberKind.Int,
                required = true,
                defaultValue = new NumberMemberValueBase { value = 5 },
            };
            data.classes["nested-class"] = SchemaClass(
                "nested-class",
                "Nested",
                "save");
            data.classes["nested-class"].schema["Deep"] = "nested-deep";
            data.classes["deep-class"] = SchemaClass(
                "deep-class",
                "Deep",
                "save");
            data.classes["deep-class"].schema["Count"] = "deep-count";
            return data;
        }

        private static NeoSchemaClass SchemaClass(
            string id,
            string name,
            string storage)
        {
            return new NeoSchemaClass
            {
                id = id,
                projectId = "p75-project",
                name = name,
                allowedStorage = storage,
                schema = new Dictionary<string, string>(),
            };
        }

        private static ClassMember RootMember(
            string projectId,
            string id,
            string name,
            string classId,
            string storage,
            string valueId)
        {
            return new ClassMember
            {
                id = id,
                projectId = projectId,
                name = name,
                kind = MemberKind.Class,
                classId = classId,
                required = true,
                storage = storage,
                valueId = valueId,
            };
        }

        private static ObjectMemberValue ObjectValue(
            string id,
            string classId,
            Dictionary<string, string>? value = null)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = value ?? new Dictionary<string, string>(),
            };
        }
    }
}
