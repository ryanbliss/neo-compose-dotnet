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
    /// Decimal attribute SDK support (specs/decimal-attribute.md §6.5):
    /// a Decimal row reuses the String row shape (decision 5), the node reads
    /// the canonical string, and <see cref="NeoAttributeDecimalWritable.Set"/>
    /// writes a canonical string row / clears optionals / rejects a required
    /// null.
    /// </summary>
    public class NeoDecimalAttributeTests
    {
        // ------------------------------------------------------------------
        // Decision 5 — no new row shape; a decimal value is a string row and
        // typed identity comes from the schema's DecimalAttribute, never the
        // value shape.
        // ------------------------------------------------------------------

        [Test]
        public void DecimalRow_ResolvesAsStringAttributeValue()
        {
            var row = JsonConvert.DeserializeObject<AttributeValue>(
                "{\"id\":\"v1\",\"value\":\"1.25\"}");
            Assert.IsInstanceOf<StringAttributeValue>(row);
            Assert.AreEqual("1.25", ((StringAttributeValue)row!).value);
        }

        [Test]
        public void DecimalAttribute_DeserializesByTypeOrdinal()
        {
            var attribute = JsonConvert.DeserializeObject<NeoCompose.Runtime.Json.Attribute>(
                "{\"id\":\"a1\",\"projectId\":\"p\",\"name\":\"Speed\",\"type\":20,\"isStatic\":false," +
                "\"minValue\":\"0\",\"maxValue\":\"100.5\",\"decimalPoints\":4," +
                "\"defaultValue\":{\"value\":\"1.25\"}}");
            Assert.IsInstanceOf<DecimalAttribute>(attribute);
            var decimalAttribute = (DecimalAttribute)attribute!;
            Assert.AreEqual(AttributeType.Decimal, decimalAttribute.type);
            Assert.AreEqual("0", decimalAttribute.minValue);
            Assert.AreEqual("100.5", decimalAttribute.maxValue);
            Assert.AreEqual(4d, decimalAttribute.decimalPoints);
            Assert.AreEqual("1.25", decimalAttribute.defaultValue!.value);
        }

        // ------------------------------------------------------------------
        // Read.
        // ------------------------------------------------------------------

        [Test]
        public void Read_NodeExposesCanonicalStringRow()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var speed = client.save.Get<NeoAttributeDecimalWritable>("Speed");

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
            var speed = client.save.Get<NeoAttributeDecimalWritable>("Speed");

            speed.Set(2.5m);

            Assert.AreEqual("2.5", speed.value?.value);
        }

        [Test]
        public void Set_PreservesScaleInStoredString()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var speed = client.save.Get<NeoAttributeDecimalWritable>("Speed");

            speed.Set(2.50m);

            Assert.AreEqual("2.50", speed.value?.value);
        }

        [Test]
        public void Set_NullOnRequiredThrowsArgumentNull()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var speed = client.save.Get<NeoAttributeDecimalWritable>("Speed");

            var error = Assert.Throws<System.ArgumentNullException>(() => speed.Set(null));
            StringAssert.Contains("required", error!.Message);
        }

        [Test]
        public void Set_NullOnOptionalClearsValue()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var bonus = client.save.Get<NeoAttributeDecimalWritable>("Bonus");
            Assert.AreEqual("2.50", bonus.value?.value);

            bonus.Set(null);

            Assert.IsNull(client.save.Get<NeoAttributeDecimalWritable>("Bonus").value?.value);
        }

        // ------------------------------------------------------------------
        // Fixture.
        // ------------------------------------------------------------------

        private static ProjectData BuildProjectData()
        {
            var rootType = new CustomType
            {
                id = "root-type",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var saveRootType = new CustomType
            {
                id = "save-root-type",
                projectId = "project-a",
                name = "Save Root",
                schema = new Dictionary<string, string>
                {
                    ["Speed"] = "speed-attribute",
                    ["Bonus"] = "bonus-attribute",
                },
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Decimal Attributes",
                    rootAssetsAttributeId = "root-assets",
                    rootSaveFileAttributeId = "root-save",
                    rootSessionAttributeId = "root-session",
                },
                attributes = new Dictionary<string, NeoCompose.Runtime.Json.Attribute>
                {
                    ["root-assets"] = RootAttribute("root-assets", "root-assets-value", rootType.id),
                    ["root-save"] = RootAttribute("root-save", "root-save-value", saveRootType.id),
                    ["root-session"] = RootAttribute("root-session", "root-session-value", rootType.id),
                    ["speed-attribute"] = DecimalAttributeDefinition("speed-attribute", "Speed", required: true),
                    ["bonus-attribute"] = DecimalAttributeDefinition("bonus-attribute", "Bonus", required: false),
                },
                values = new Dictionary<string, AttributeValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootType.id, new()),
                    ["root-save-value"] = ObjectValue(
                        "root-save-value",
                        saveRootType.id,
                        new Dictionary<string, string>
                        {
                            ["Speed"] = "speed-value",
                            ["Bonus"] = "bonus-value",
                        }),
                    ["root-session-value"] = ObjectValue("root-session-value", rootType.id, new()),
                    ["speed-value"] = DecimalValueRow("speed-value", "1.25"),
                    ["bonus-value"] = DecimalValueRow("bonus-value", "2.50"),
                },
                types = new Dictionary<string, CustomType>
                {
                    [rootType.id] = rootType,
                    [saveRootType.id] = saveRootType,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static CustomAttribute RootAttribute(string id, string valueId, string customTypeId)
        {
            return new CustomAttribute
            {
                id = id,
                projectId = "project-a",
                name = id,
                type = AttributeType.Custom,
                required = true,
                valueId = valueId,
                customTypeId = customTypeId,
            };
        }

        private static DecimalAttribute DecimalAttributeDefinition(
            string id,
            string name,
            bool required,
            string? defaultValue = null)
        {
            return new DecimalAttribute
            {
                id = id,
                projectId = "project-a",
                name = name,
                type = AttributeType.Decimal,
                required = required,
                defaultValue = defaultValue is null
                    ? null
                    : new StringAttributeValueBase { value = defaultValue },
            };
        }

        private static StringAttributeValue DecimalValueRow(string id, string value)
        {
            return new StringAttributeValue
            {
                id = id,
                value = value,
            };
        }

        private static ObjectAttributeValue ObjectValue(
            string id,
            string typeId,
            Dictionary<string, string> record)
        {
            return new ObjectAttributeValue
            {
                id = id,
                typeId = typeId,
                value = record,
            };
        }
    }
}
