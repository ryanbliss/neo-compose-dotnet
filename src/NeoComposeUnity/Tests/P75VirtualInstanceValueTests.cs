// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
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
