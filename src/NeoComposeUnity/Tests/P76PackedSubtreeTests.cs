// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P76 packed subtree storage — the SDK's read half.
    ///
    /// <para>The suite is built around one invariant (P76 R6): a packed export
    /// and the sparse export of the same logical graph must be
    /// indistinguishable to everything above the reader. So every decode test
    /// starts from a SPARSE fixture, folds named children into their parents,
    /// and then asserts the same surface the sparse form already serves —
    /// rather than asserting against a hand-written expectation that could
    /// drift from what the rest of the runtime believes a row looks like.</para>
    /// </summary>
    public class P76PackedSubtreeTests
    {
        private const string ProjectId = "p76-project";

        // -------------------------------------------------------------------
        // Canonical child ids. Every literal is uuidv5 (RFC 4122, SHA-1,
        // big-endian) of "{instanceRootId}:{sourceValueId}" under the P75
        // namespace 3e8ca0b3-e3f1-5d5f-bf2f-6ab5ee3896d0 — the SAME derivation
        // and the same namespace P75 uses, because P76 adds no id family
        // (spec §5). The derivation inputs are spelled beside each literal so
        // the TypeScript suite can pin the identical strings from the identical
        // two inputs.
        // -------------------------------------------------------------------

        /// <summary>thing-instance : thing-count-source</summary>
        private const string CountValueId = "321d6c90-94e3-5101-8ffb-6d43b95dc1b4";

        /// <summary>thing-instance : thing-nested-source</summary>
        private const string NestedValueId = "69b985a8-ede4-51f7-af71-ff811da8b90d";

        /// <summary>
        /// thing-instance : nested-depth-source
        ///
        /// <para>Derived from <c>thing-instance</c>, NOT from its own parent
        /// <c>NestedValueId</c>: the construction root is the nearest ENCLOSING
        /// stamped row, and the Nested row carries no construction stamp of its
        /// own. Getting this wrong would put a real child at an id nothing
        /// references.</para>
        /// </summary>
        private const string DepthValueId = "bce78666-fccd-59b6-abea-05c17490b93a";

        /// <summary>
        /// Minted, not derived: the Label row carries no <c>sourceValueId</c>,
        /// so its position has no canonical derivation and the packed entry
        /// must store this id verbatim.
        /// </summary>
        private const string LabelValueId = "aaaa1111-2222-4333-8444-555566667777";

        // -------------------------------------------------------------------
        // Decoding.
        // -------------------------------------------------------------------

        [Test]
        public void PackedExportExposesEveryChildAsAStoredRow()
        {
            using NeoClient packed = LoadClient(PackedProjectJson());

            Assert.IsTrue(
                packed.TryGetValue(CountValueId, out NumberMemberValue? count),
                "A packed child must resolve through the ordinary value lookup: "
                + "it is a durable authored row that happens to live inside its "
                + "parent's document, not a replayed virtual one.");
            Assert.AreEqual(7d, count!.value);
            Assert.AreEqual(CountValueId, count.id);
            Assert.AreEqual("thing-count-source", count.sourceValueId);

            Assert.IsTrue(
                packed.TryGetValue(NestedValueId, out ObjectMemberValue? nested));
            Assert.AreEqual("nested-class", nested!.classId);
            Assert.AreEqual(
                DepthValueId,
                nested.value!["Depth"],
                "A packed parent's own content must come back as child-id "
                + "strings, so a nested packed subtree reads exactly like a "
                + "sparse one.");

            Assert.IsTrue(
                packed.TryGetValue(DepthValueId, out NumberMemberValue? depth));
            Assert.AreEqual(3d, depth!.value);

            Assert.IsTrue(
                packed.TryGetValue(LabelValueId, out StringMemberValue? label));
            Assert.AreEqual("hello", label!.value);
        }

        [Test]
        public void PackedAndSparseExportsServeTheSameMemberSurfaces()
        {
            using NeoClient sparse = LoadClient(SparseProjectJson());
            using NeoClient packed = LoadClient(PackedProjectJson());

            Assert.AreEqual(ReadThroughMembers(sparse), ReadThroughMembers(packed));
            Assert.AreEqual("7|hello|3", ReadThroughMembers(packed));
        }

        [Test]
        public void PackedParentRowIsRestoredToItsSparseBytes()
        {
            ProjectData packed = Deserialize(PackedProjectJson());
            ProjectData sparse = Deserialize(SparseProjectJson());

            var packedThing = (ObjectMemberValue)packed.values["thing-instance"];
            var sparseThing = (ObjectMemberValue)sparse.values["thing-instance"];
            CollectionAssert.AreEqual(sparseThing.value, packedThing.value);
            Assert.AreEqual(sparse.values.Count, packed.values.Count);
        }

        [Test]
        public void PackedChildInheritsItsParentStoragePartition()
        {
            // The partition is a serialization concept and a packed child is
            // serialized inside its parent, so it can only ever be in the
            // parent's partition. Storing one is rejected; inheriting it is the
            // only representable answer.
            JObject root = PackedProject();
            var values = (JObject)root["values"]!;
            values["thing-instance"]!["mapKey"] = "world:thing-grid";

            ProjectData data = Deserialize(root.ToString());
            Assert.AreEqual("world:thing-grid", data.values[CountValueId].mapKey);
            Assert.AreEqual("world:thing-grid", data.values[DepthValueId].mapKey);
        }

        [Test]
        public void UnpackedProjectIsHandedBackUnchanged()
        {
            // The fast path the whole boundary rests on: until a packed member
            // exists, expansion is one shallow scan and the caller keeps its
            // own row objects.
            var values = (JObject)SparseProject()["values"]!;
            Assert.AreSame(values, NeoPackedValue.Expand(values, "test values"));
        }

        // -------------------------------------------------------------------
        // Malformed payloads. Each case is one failure condition with its own
        // message — a packed payload that disagrees with the encoding is
        // corrupt rather than old, so there is no tolerant arm anywhere here.
        // -------------------------------------------------------------------

        [Test]
        public void RedundantStoredIdIsRejected()
        {
            AssertRejects(
                entry => entry["id"] = CountValueId,
                "which its position already derives");
        }

        [Test]
        public void MissingIdWithNoDerivationIsRejected()
        {
            AssertRejects(
                entry => entry.Remove("sourceValueId"),
                "stores no \"id\" and its position derives none");
        }

        [Test]
        public void StoredProjectIdIsRejected()
        {
            AssertRejects(
                entry => entry["projectId"] = ProjectId,
                "stores \"projectId\", which decoding derives from its owning row");
        }

        [Test]
        public void StoredContainerIdIsRejected()
        {
            AssertRejects(
                entry => entry["containerId"] = "some-unordered-list",
                "stores \"containerId\", which decoding derives from its owning row");
        }

        [Test]
        public void StoredMapKeyIsRejected()
        {
            AssertRejects(
                entry => entry["mapKey"] = "world:other-grid",
                "stores \"mapKey\", which decoding derives from its owning row");
        }

        [Test]
        public void EntryCarryingBothValueAndInitIsRejected()
        {
            AssertRejects(
                entry => entry["init"] = new JObject { ["code"] = "1" },
                "stores both \"value\" and \"init\"");
        }

        [Test]
        public void EntryCarryingNeitherValueNorInitIsRejected()
        {
            AssertRejects(
                entry => entry.Remove("value"),
                "stores neither \"value\" nor \"init\"");
        }

        [Test]
        public void EntryWithoutTimestampsIsRejected()
        {
            AssertRejects(
                entry => entry.Remove("updatedAt"),
                "stores no numeric \"updatedAt\"");
        }

        [Test]
        public void MalformedPayloadNamesItsPositionInsideTheParent()
        {
            JsonSerializationException error = Reject(
                entry => entry.Remove("createdAt"));
            StringAssert.Contains(
                "Packed value entry at value \"thing-instance\".value[\"Count\"]",
                error.Message,
                "The message has to locate the corrupt entry inside its "
                + "distribution root; a packed child has no row of its own to "
                + "report against.");
        }

        [Test]
        public void UnexpandedEnvelopeReachingTheValueConverterIsRejected()
        {
            // Reached only if a row set skipped the expansion boundary. Without
            // this guard a Class body holding an envelope fails deep inside
            // Newtonsoft with a dictionary-conversion message, and an array
            // body succeeds with a child id of "{}" — silently losing the
            // subtree, which is the exact failure schema 31 exists to prevent.
            var row = JObject.Parse(
                @"{
  ""id"": ""thing-instance"",
  ""classId"": ""thing-class"",
  ""createdAt"": 0,
  ""updatedAt"": 0,
  ""value"": { ""Count"": { ""~packed"": { ""value"": 7, ""createdAt"": 0, ""updatedAt"": 0 } } }
}");

            var error = Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<MemberValue>(row.ToString()));
            StringAssert.Contains("unexpanded '~packed' child envelope", error!.Message);
        }

        [Test]
        public void PackedEnvelopeInAMemberDeclarationDefaultIsRejected()
        {
            // A declaration default owns no value row, so there is no parent to
            // pack into and no id for a child to derive from.
            var member = JObject.Parse(
                @"{
  ""id"": ""thing-nested"",
  ""projectId"": ""p76-project"",
  ""name"": ""Nested"",
  ""kind"": 7,
  ""classId"": ""nested-class"",
  ""defaultValue"": {
    ""value"": { ""Depth"": { ""~packed"": { ""value"": 3, ""createdAt"": 0, ""updatedAt"": 0 } } }
  }
}");

            var error = Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<JsonMember>(member.ToString()));
            StringAssert.Contains("a declaration default owns no row", error!.Message);
        }

        // -------------------------------------------------------------------
        // Schema gate.
        // -------------------------------------------------------------------

        [Test]
        public void SchemaThirtyExportIsRejected()
        {
            JObject root = SparseProject();
            root["metadata"]!["schemaVersion"] = 30;

            var error = Assert.Throws<JsonSerializationException>(
                () => Deserialize(root.ToString()));
            StringAssert.Contains(
                "Project export schema version 30 is unsupported",
                error!.Message);
            StringAssert.Contains("only schema version 31", error.Message);
        }

        // -------------------------------------------------------------------
        // Distribution setting (§1). Absence is the automatic state.
        // -------------------------------------------------------------------

        [Test]
        public void AbsentDistributionResolvesThroughTheAutomaticTable()
        {
            var count = new IntMember { kind = MemberKind.Int };
            var list = new ListMember { kind = MemberKind.List };
            var property = new NSPropertyMember { kind = MemberKind.NSProperty };

            Assert.IsNull(count.DeclaredDistribution);
            Assert.AreEqual(NeoSubtreeDistributionKind.Packed, count.Distribution);
            Assert.AreEqual(NeoSubtreeDistributionKind.Sparse, list.Distribution);
            Assert.IsNull(
                property.Distribution,
                "A value-less kind has no distribution at all, which is not the "
                + "same answer as Sparse.");
        }

        [Test]
        public void StringDistributionFollowsFormatAndLookupFollowsSelection()
        {
            var localized = new StringMember { kind = MemberKind.String };
            var plain = new StringMember
            {
                kind = MemberKind.String,
                Format = NeoStringFormatKind.Plain,
            };
            var single = new LookupMember { kind = MemberKind.Lookup };
            var multi = new LookupMember
            {
                kind = MemberKind.Lookup,
                Selection = NeoMemberSelectionKind.Multi,
            };

            Assert.AreEqual(
                NeoSubtreeDistributionKind.Packed,
                localized.Distribution,
                "An absent format is Localized, and a localized value stores "
                + "only the localized-text id.");
            Assert.AreEqual(NeoSubtreeDistributionKind.Sparse, plain.Distribution);
            Assert.AreEqual(NeoSubtreeDistributionKind.Packed, single.Distribution);
            Assert.AreEqual(NeoSubtreeDistributionKind.Sparse, multi.Distribution);
        }

        [Test]
        public void GrowthRiskIsAProjectionOfTheSameTable()
        {
            Assert.AreEqual(
                NeoDistributionGrowthReason.None,
                NeoSubtreeDistribution.Automatic(
                    MemberKind.Vector3,
                    NeoStringFormatKind.Localized,
                    NeoMemberSelectionKind.Single).Growth);
            Assert.AreEqual(
                NeoDistributionGrowthReason.EntryCount,
                NeoSubtreeDistribution.Automatic(
                    MemberKind.List,
                    NeoStringFormatKind.Localized,
                    NeoMemberSelectionKind.Single).Growth);
            Assert.AreEqual(
                NeoDistributionGrowthReason.UnboundedText,
                NeoSubtreeDistribution.Automatic(
                    MemberKind.String,
                    NeoStringFormatKind.Plain,
                    NeoMemberSelectionKind.Single).Growth);
            Assert.IsFalse(
                NeoSubtreeDistribution.KindSupportsDistribution(MemberKind.Function));
            Assert.IsTrue(
                NeoSubtreeDistribution.KindSupportsDistribution(MemberKind.NSAction));
        }

        [Test]
        public void OpenGenericSlotHasNoAutomaticDistribution()
        {
            var slot = new GenericMember { kind = MemberKind.Generic };
            Assert.IsNull(
                slot.Distribution,
                "An open slot genuinely has no distribution until it is closed; "
                + "answering Sparse would drop every closed binding out of "
                + "packing.");
            Assert.Throws<System.InvalidOperationException>(
                () => NeoSubtreeDistribution.Automatic(
                    MemberKind.Generic,
                    NeoStringFormatKind.Localized,
                    NeoMemberSelectionKind.Single));
        }

        [Test]
        public void ExplicitDistributionSurvivesTheWireAndAbsenceStaysAbsent()
        {
            const string explicitSparse =
                @"{""id"":""m1"",""projectId"":""p"",""name"":""N"",""kind"":2,""distribution"":0}";
            const string absent =
                @"{""id"":""m2"",""projectId"":""p"",""name"":""N"",""kind"":2}";

            var sparse = JsonConvert.DeserializeObject<JsonMember>(explicitSparse)!;
            var automatic = JsonConvert.DeserializeObject<JsonMember>(absent)!;

            Assert.AreEqual(NeoSubtreeDistributionKind.Sparse, sparse.DeclaredDistribution);
            Assert.AreEqual(NeoSubtreeDistributionKind.Sparse, sparse.Distribution);
            Assert.IsNull(automatic.DeclaredDistribution);
            Assert.AreEqual(
                NeoSubtreeDistributionKind.Packed,
                automatic.Distribution,
                "An Int packs automatically, so folding absence into the "
                + "ordinal zero would invert the stored meaning of every "
                + "member that says nothing.");

            StringAssert.Contains(
                @"""distribution"":0",
                JsonConvert.SerializeObject(sparse));
            StringAssert.DoesNotContain(
                "distribution",
                JsonConvert.SerializeObject(automatic));
        }

        [Test]
        public void ExplicitDistributionInheritsThroughTheOverrideChain()
        {
            var root = new IntMember
            {
                id = "root",
                projectId = ProjectId,
                name = "Count",
                kind = MemberKind.Int,
                Distribution = NeoSubtreeDistributionKind.Sparse,
            };
            var inheriting = new IntMember
            {
                id = "override",
                projectId = ProjectId,
                name = "Count",
                kind = MemberKind.Int,
                extendsMemberId = "root",
            };
            var members = new Dictionary<string, JsonMember>
            {
                [root.id] = root,
                [inheriting.id] = inheriting,
            };

            NeoMemberShapeResolution.ResolveAll(members);

            Assert.AreEqual(
                NeoSubtreeDistributionKind.Sparse,
                inheriting.Distribution,
                "An absent override inherits the nearest explicit setting; it "
                + "does not fall through to the automatic result.");
            Assert.IsNull(
                inheriting.DeclaredDistribution,
                "Inheriting must not materialize the ancestor's choice onto the "
                + "override record.");
        }

        // -------------------------------------------------------------------
        // Fixture.
        // -------------------------------------------------------------------

        private static string ReadThroughMembers(NeoClient client)
        {
            NeoMemberClassWritable thing = client.save
                .Get<NeoMemberClassWritable>("Thing");
            double? count = thing.Get<NeoMemberIntWritable>("Count").value!.value;
            string? label = thing.Get<NeoMemberStringWritable>("Label").value!.value;
            double? depth = thing
                .Get<NeoMemberClassWritable>("Nested")
                .Get<NeoMemberIntWritable>("Depth")
                .value!.value;
            return $"{count}|{label}|{depth}";
        }

        private static NeoClient LoadClient(string projectJson) =>
            NeoTestSaveStack.ClientFromSchema(Deserialize(projectJson));

        private static ProjectData Deserialize(string projectJson) =>
            JsonConvert.DeserializeObject<ProjectData>(projectJson)!;

        private static string SparseProjectJson() => SparseProject().ToString();

        private static string PackedProjectJson() => PackedProject().ToString();

        /// <summary>
        /// Applies <paramref name="corrupt"/> to the Count entry of an
        /// otherwise valid packed export and asserts the decode refuses.
        /// </summary>
        private static void AssertRejects(
            System.Action<JObject> corrupt,
            string expectedFragment)
        {
            StringAssert.Contains(expectedFragment, Reject(corrupt).Message);
        }

        private static JsonSerializationException Reject(System.Action<JObject> corrupt)
        {
            JObject root = PackedProject();
            var entry = (JObject)root["values"]!["thing-instance"]!["value"]!["Count"]!
                [NeoPackedValue.EnvelopeKey]!;
            corrupt(entry);
            return Assert.Throws<JsonSerializationException>(
                () => Deserialize(root.ToString()))!;
        }

        /// <summary>
        /// The logical graph as schema 30 would have shipped it: every child is
        /// its own entry in <c>values</c>.
        /// </summary>
        private static JObject SparseProject() =>
            JObject.FromObject(BuildFixtureData());

        /// <summary>
        /// The same graph as schema 31 ships it: the Thing instance carries its
        /// whole materialized subtree, and none of those children has an entry
        /// of its own. Deepest first, so each parent is already packed when its
        /// own position is folded.
        /// </summary>
        private static JObject PackedProject()
        {
            JObject root = SparseProject();
            var values = (JObject)root["values"]!;
            Pack(values, NestedValueId, "Depth", DepthValueId, storeId: false);
            Pack(values, "thing-instance", "Count", CountValueId, storeId: false);
            Pack(values, "thing-instance", "Nested", NestedValueId, storeId: false);
            Pack(values, "thing-instance", "Label", LabelValueId, storeId: true);
            return root;
        }

        /// <summary>
        /// Folds one child row into the schema-key position of its parent that
        /// holds its id. The encoder omits a derivable id and stores a minted
        /// one; <paramref name="storeId"/> picks the arm the position requires.
        /// </summary>
        private static void Pack(
            JObject values,
            string parentId,
            string schemaKey,
            string childId,
            bool storeId)
        {
            var child = (JObject)values[childId]!;
            values.Remove(childId);
            if (!storeId) child.Remove("id");
            var parentBody = (JObject)values[parentId]!["value"]!;
            Assert.AreEqual(
                childId,
                parentBody.Value<string>(schemaKey),
                $"The sparse fixture must place '{childId}' at "
                + $"{parentId}.value[\"{schemaKey}\"] for packing to be the only "
                + "difference between the two forms.");
            parentBody[schemaKey] = new JObject
            {
                [NeoPackedValue.EnvelopeKey] = child,
            };
        }

        private static ProjectData BuildFixtureData()
        {
            NeoSchemaClass assetsRootClass =
                SchemaClass("assets-root-class", "AssetsRoot", NeoMemberStorage.Immutable);
            NeoSchemaClass saveRootClass =
                SchemaClass("save-root-class", "SaveRoot", NeoMemberStorage.Save);
            NeoSchemaClass sessionRootClass =
                SchemaClass("session-root-class", "SessionRoot", NeoMemberStorage.Session);
            NeoSchemaClass thingClass =
                SchemaClass("thing-class", "Thing", NeoMemberStorage.Save);
            NeoSchemaClass nestedClass =
                SchemaClass("nested-class", "Nested", NeoMemberStorage.Save);

            saveRootClass.schema["Thing"] = "thing-member";
            thingClass.schema["Count"] = "thing-count";
            thingClass.schema["Label"] = "thing-label";
            thingClass.schema["Nested"] = "thing-nested";
            nestedClass.schema["Depth"] = "nested-depth";

            ClassMember assetsRoot = RootMember(
                "assets-root", "Assets", assetsRootClass.id, NeoMemberStorage.Immutable, "value-assets");
            ClassMember saveRoot = RootMember(
                "save-root", "Save", saveRootClass.id, NeoMemberStorage.Save, "value-save");
            ClassMember sessionRoot = RootMember(
                "session-root", "Session", sessionRootClass.id, NeoMemberStorage.Session, "value-session");

            var thingMember = new ClassMember
            {
                id = "thing-member",
                projectId = ProjectId,
                name = "Thing",
                kind = MemberKind.Class,
                classId = thingClass.id,
                Requirement = NeoMemberRequirementKind.Required,
                Storage = NeoMemberStorage.Save,
            };
            var nestedMember = new ClassMember
            {
                id = "thing-nested",
                projectId = ProjectId,
                name = "Nested",
                kind = MemberKind.Class,
                classId = nestedClass.id,
                Requirement = NeoMemberRequirementKind.Required,
            };
            var countMember = new IntMember
            {
                id = "thing-count",
                projectId = ProjectId,
                name = "Count",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new NumberMemberValueBase { value = 0d },
            };
            var depthMember = new IntMember
            {
                id = "nested-depth",
                projectId = ProjectId,
                name = "Depth",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new NumberMemberValueBase { value = 0d },
            };
            var labelMember = new StringMember
            {
                id = "thing-label",
                projectId = ProjectId,
                name = "Label",
                kind = MemberKind.String,
                Requirement = NeoMemberRequirementKind.Required,
                Format = NeoStringFormatKind.Plain,
                defaultValue = new StringMemberValueBase { value = string.Empty },
            };

            // The distribution root: a P75 collapse-stamped instance, which is
            // what gives its deterministic children a canonical derivation and
            // therefore lets their packed entries omit an id.
            ObjectMemberValue thing = ObjectValue(
                "thing-instance",
                thingClass.id,
                new Dictionary<string, string>
                {
                    ["Count"] = CountValueId,
                    ["Label"] = LabelValueId,
                    ["Nested"] = NestedValueId,
                });
            thing.constructorArgs = new Dictionary<string, JToken?>();
            thing.instanceConstructorId = null;

            ObjectMemberValue nested = ObjectValue(
                NestedValueId,
                nestedClass.id,
                new Dictionary<string, string> { ["Depth"] = DepthValueId });
            nested.sourceValueId = "thing-nested-source";

            return new ProjectData
            {
                metadata = new ProjectExportMetadata
                {
                    schemaVersion = NeoProjectExportContract.CurrentSchemaVersion,
                    projectId = ProjectId,
                    versionId = "p76-version",
                },
                project = new Project
                {
                    id = ProjectId,
                    name = "P76",
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
                    [nestedMember.id] = nestedMember,
                    [countMember.id] = countMember,
                    [depthMember.id] = depthMember,
                    [labelMember.id] = labelMember,
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
                    [CountValueId] = new NumberMemberValue
                    {
                        id = CountValueId,
                        value = 7d,
                        sourceValueId = "thing-count-source",
                    },
                    [LabelValueId] = new StringMemberValue
                    {
                        id = LabelValueId,
                        value = "hello",
                    },
                    [nested.id] = nested,
                    [DepthValueId] = new NumberMemberValue
                    {
                        id = DepthValueId,
                        value = 3d,
                        sourceValueId = "nested-depth-source",
                    },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [assetsRootClass.id] = assetsRootClass,
                    [saveRootClass.id] = saveRootClass,
                    [sessionRootClass.id] = sessionRootClass,
                    [thingClass.id] = thingClass,
                    [nestedClass.id] = nestedClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
                variantFolders = new Dictionary<string, VariantFolderRecord>(),
                internalRecordRelations = new Dictionary<string, InternalRecordRelation>(),
            };
        }

        private static NeoSchemaClass SchemaClass(
            string id,
            string name,
            NeoMemberStorage storage) =>
            new NeoSchemaClass
            {
                id = id,
                projectId = ProjectId,
                name = name,
                allowedStorage = storage,
                schema = new Dictionary<string, string>(),
            };

        private static ClassMember RootMember(
            string id,
            string name,
            string classId,
            NeoMemberStorage storage,
            string valueId) =>
            new ClassMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Class,
                classId = classId,
                Requirement = NeoMemberRequirementKind.Required,
                Storage = storage,
                valueId = valueId,
            };

        private static ObjectMemberValue ObjectValue(
            string id,
            string classId,
            Dictionary<string, string>? value = null) =>
            new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = value ?? new Dictionary<string, string>(),
            };
    }
}
