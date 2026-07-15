// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Decimal member SDK support (specs/decimal-member.md §6.5):
    /// a Decimal row reuses the String row shape (decision 5), the node reads
    /// the canonical string, and <see cref="NeoMemberDecimalWritable.Set"/>
    /// writes a canonical string row / clears optionals / rejects a required
    /// null.
    /// </summary>
    public class NeoDecimalMemberTests
    {
        // ------------------------------------------------------------------
        // Decision 5 — no new row shape; a decimal value is a string row and
        // typed identity comes from the schema's DecimalMember, never the
        // value shape.
        // ------------------------------------------------------------------

        [Test]
        public void DecimalRow_ResolvesAsStringMemberValue()
        {
            var row = JsonConvert.DeserializeObject<MemberValue>(
                "{\"id\":\"v1\",\"value\":\"1.25\"}");
            Assert.IsInstanceOf<StringMemberValue>(row);
            Assert.AreEqual("1.25", ((StringMemberValue)row!).value);
        }

        [Test]
        public void DecimalMember_DeserializesByKindOrdinal()
        {
            var member = JsonConvert.DeserializeObject<NeoCompose.Runtime.Json.Member>(
                "{\"id\":\"a1\",\"projectId\":\"p\",\"name\":\"Speed\",\"kind\":20,\"isStatic\":false," +
                "\"minValue\":\"0\",\"maxValue\":\"100.5\",\"decimalPoints\":4," +
                "\"defaultValue\":{\"value\":\"1.25\"}}");
            Assert.IsInstanceOf<DecimalMember>(member);
            var decimalMember = (DecimalMember)member!;
            Assert.AreEqual(MemberKind.Decimal, decimalMember.kind);
            Assert.AreEqual("0", decimalMember.minValue);
            Assert.AreEqual("100.5", decimalMember.maxValue);
            Assert.AreEqual(4d, decimalMember.decimalPoints);
            Assert.AreEqual("1.25", decimalMember.defaultValue!.value);
        }

        // ------------------------------------------------------------------
        // Read.
        // ------------------------------------------------------------------

        [Test]
        public void Read_NodeExposesCanonicalStringRow()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var speed = client.save.Get<NeoMemberDecimalWritable>("Speed");

            Assert.AreEqual("1.25", speed.value?.value);
            Assert.AreEqual(1.25m, NeoDecimalValues.Parse(speed.value!.value!));
        }

        // ------------------------------------------------------------------
        // Writable.Set — writes a canonical string row.
        // ------------------------------------------------------------------

        [Test]
        public void Set_WritesCanonicalStringRow()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var speed = client.save.Get<NeoMemberDecimalWritable>("Speed");

            speed.Set(2.5m);

            Assert.AreEqual("2.5", speed.value?.value);
        }

        [Test]
        public void Set_PreservesScaleInStoredString()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var speed = client.save.Get<NeoMemberDecimalWritable>("Speed");

            speed.Set(2.50m);

            Assert.AreEqual("2.50", speed.value?.value);
        }

        [Test]
        public void Set_NullOnRequiredThrowsArgumentNull()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var speed = client.save.Get<NeoMemberDecimalWritable>("Speed");

            var error = Assert.Throws<System.ArgumentNullException>(() => speed.Set(null));
            StringAssert.Contains("required", error!.Message);
        }

        [Test]
        public void Set_NullOnOptionalClearsValue()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var bonus = client.save.Get<NeoMemberDecimalWritable>("Bonus");
            Assert.AreEqual("2.50", bonus.value?.value);

            bonus.Set(null);

            Assert.IsNull(client.save.Get<NeoMemberDecimalWritable>("Bonus").value?.value);
        }

        // ------------------------------------------------------------------
        // Fixture.
        // ------------------------------------------------------------------

        private static ProjectData BuildProjectData()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var saveRootClass = new NeoSchemaClass
            {
                id = "save-root-class",
                projectId = "project-a",
                name = "Save Root",
                schema = new Dictionary<string, string>
                {
                    ["Speed"] = "speed-member",
                    ["Bonus"] = "bonus-member",
                },
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Decimal Members",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", saveRootClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                    ["speed-member"] = DecimalMemberDefinition("speed-member", "Speed", required: true),
                    ["bonus-member"] = DecimalMemberDefinition("bonus-member", "Bonus", required: false),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id, new()),
                    ["root-save-value"] = ObjectValue(
                        "root-save-value",
                        saveRootClass.id,
                        new Dictionary<string, string>
                        {
                            ["Speed"] = "speed-value",
                            ["Bonus"] = "bonus-value",
                        }),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id, new()),
                    ["speed-value"] = DecimalValueRow("speed-value", "1.25"),
                    ["bonus-value"] = DecimalValueRow("bonus-value", "2.50"),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [saveRootClass.id] = saveRootClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static ClassMember RootMember(string id, string valueId, string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-a",
                name = id,
                kind = MemberKind.Class,
                required = true,
                valueId = valueId,
                classId = classId,
            };
        }

        private static DecimalMember DecimalMemberDefinition(
            string id,
            string name,
            bool required,
            string? defaultValue = null)
        {
            return new DecimalMember
            {
                id = id,
                projectId = "project-a",
                name = name,
                kind = MemberKind.Decimal,
                required = required,
                defaultValue = defaultValue is null
                    ? null
                    : new StringMemberValueBase { value = defaultValue },
            };
        }

        private static StringMemberValue DecimalValueRow(string id, string value)
        {
            return new StringMemberValue
            {
                id = id,
                value = value,
            };
        }

        private static ObjectMemberValue ObjectValue(
            string id,
            string classId,
            Dictionary<string, string> record)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = record,
            };
        }
    }
}
