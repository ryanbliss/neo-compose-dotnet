// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Color attribute SDK support (specs/color-attribute.md §5): the strict
    /// {r,g,b,a} converter, shape sniffing, the NeoReadOnlyColor/NeoColor
    /// wrappers (assignment convention, decisions 5–6), the SetColor /
    /// SetColorOrClear write funnels, and the opaque-white default
    /// (decision 4).
    /// </summary>
    public class NeoColorAttributeTests
    {
        // ------------------------------------------------------------------
        // NeoColorValueConverter — strict shape + range validation.
        // ------------------------------------------------------------------

        [Test]
        public void ColorConverter_AcceptsExactShapeIncludingBounds()
        {
            var value = JsonConvert.DeserializeObject<NeoColorValue>(
                "{\"r\":0,\"g\":1,\"b\":0.5,\"a\":0.25}")!;

            Assert.AreEqual(0f, value.r);
            Assert.AreEqual(1f, value.g);
            Assert.AreEqual(0.5f, value.b);
            Assert.AreEqual(0.25f, value.a);
        }

        [Test]
        public void ColorConverter_RejectsWrongFieldCount()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<NeoColorValue>(
                    "{\"r\":1,\"g\":1,\"b\":1}"));
            StringAssert.Contains(
                "exactly the numeric fields 'r', 'g', 'b', and 'a'",
                error!.Message);
        }

        [Test]
        public void ColorConverter_RejectsExtraKey()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<NeoColorValue>(
                    "{\"r\":1,\"g\":1,\"b\":1,\"a\":1,\"x\":1}"));
            StringAssert.Contains(
                "exactly the numeric fields 'r', 'g', 'b', and 'a'",
                error!.Message);
        }

        [Test]
        public void ColorConverter_RejectsMissingKeyWithDistinctMessage()
        {
            // Four keys (passes the count gate) but 'a' is absent.
            var error = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<NeoColorValue>(
                    "{\"r\":1,\"g\":1,\"b\":1,\"x\":1}"));
            StringAssert.Contains("missing 'a'", error!.Message);
        }

        [Test]
        public void ColorConverter_RejectsNonNumericComponent()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<NeoColorValue>(
                    "{\"r\":\"red\",\"g\":1,\"b\":1,\"a\":1}"));
            StringAssert.Contains("'r' must be a number", error!.Message);
        }

        [Test]
        public void ColorConverter_RejectsNonFiniteComponent()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<NeoColorValue>(
                    "{\"r\":NaN,\"g\":1,\"b\":1,\"a\":1}"));
            StringAssert.Contains("'r' must be a finite number", error!.Message);
        }

        [Test]
        public void ColorConverter_RejectsComponentBelowZero()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<NeoColorValue>(
                    "{\"r\":1,\"g\":-0.1,\"b\":1,\"a\":1}"));
            StringAssert.Contains("'g' must not be less than 0", error!.Message);
        }

        [Test]
        public void ColorConverter_RejectsComponentAboveOne()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<NeoColorValue>(
                    "{\"r\":1,\"g\":1,\"b\":1.5,\"a\":1}"));
            StringAssert.Contains("'b' must not be greater than 1", error!.Message);
        }

        // ------------------------------------------------------------------
        // Shape sniffing — {r,g,b,a} routes to the Color concretes without
        // disturbing the existing object shapes.
        // ------------------------------------------------------------------

        [Test]
        public void ShapeSniffing_ColorShapeResolvesColorAttributeValue()
        {
            var row = JsonConvert.DeserializeObject<AttributeValue>(
                "{\"id\":\"v1\",\"value\":{\"r\":0.1,\"g\":0.2,\"b\":0.3,\"a\":1}}");
            Assert.IsInstanceOf<ColorAttributeValue>(row);
            var color = (ColorAttributeValue)row!;
            Assert.AreEqual(0.1f, color.value!.r);
            Assert.AreEqual(1f, color.value.a);
        }

        [Test]
        public void ShapeSniffing_ColorShapeResolvesColorAttributeValueBase()
        {
            var carrier = JsonConvert.DeserializeObject<AttributeValueBase>(
                "{\"value\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1}}");
            Assert.IsInstanceOf<ColorAttributeValueBase>(carrier);
        }

        [Test]
        public void ShapeSniffing_ExistingObjectShapesAreUnaffected()
        {
            Assert.IsInstanceOf<Vector2AttributeValue>(
                JsonConvert.DeserializeObject<AttributeValue>(
                    "{\"id\":\"v1\",\"value\":{\"x\":1,\"y\":2}}"));
            Assert.IsInstanceOf<Vector3AttributeValue>(
                JsonConvert.DeserializeObject<AttributeValue>(
                    "{\"id\":\"v2\",\"value\":{\"x\":1,\"y\":2,\"z\":3}}"));
            Assert.IsInstanceOf<SpriteAttributeValue>(
                JsonConvert.DeserializeObject<AttributeValue>(
                    "{\"id\":\"v3\",\"value\":{\"fileId\":\"f\",\"sliceIndex\":0}}"));
            Assert.IsInstanceOf<FileAttributeValue>(
                JsonConvert.DeserializeObject<AttributeValue>(
                    "{\"id\":\"v4\",\"value\":{\"fileId\":\"f\"}}"));
            // An out-of-range component is not a Color shape; the generic
            // object fallback keeps handling it.
            Assert.IsInstanceOf<ObjectAttributeValue>(
                JsonConvert.DeserializeObject<AttributeValue>(
                    "{\"id\":\"v5\",\"value\":{\"r\":2,\"g\":1,\"b\":1,\"a\":\"s\"}}"));
        }

        [Test]
        public void ColorAttribute_DeserializesByTypeOrdinal()
        {
            var attribute = JsonConvert.DeserializeObject<NeoCompose.Runtime.Json.Attribute>(
                "{\"id\":\"a1\",\"projectId\":\"p\",\"name\":\"Tint\",\"type\":19,\"isStatic\":false," +
                "\"defaultValue\":{\"value\":{\"r\":0.25,\"g\":0.5,\"b\":0.75,\"a\":1}}}");
            Assert.IsInstanceOf<ColorAttribute>(attribute);
            var color = (ColorAttribute)attribute!;
            Assert.AreEqual(AttributeType.Color, color.type);
            Assert.AreEqual(0.25f, color.defaultValue!.value!.r);
        }

        // ------------------------------------------------------------------
        // Wrappers + write funnels.
        // ------------------------------------------------------------------

        [Test]
        public void Wrapper_BoundReadViaImplicitConversionAndValue()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var wrapper = new NeoColor(client.save.Get<NeoAttributeColorWritable>("Tint"));

            Color viaImplicit = wrapper;
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 1f), viaImplicit);
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 1f), wrapper.Value);
        }

        [Test]
        public void Wrapper_DetachedCtorAndImplicitFromColor()
        {
            var detached = new NeoColor(Color.red);
            Assert.AreEqual(Color.red, detached.Value);

            NeoColor converted = Color.blue;
            Assert.AreEqual(Color.blue, converted.Value);

            var componentCtor = new NeoReadOnlyColor(0.1f, 0.2f, 0.3f, 0.4f);
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 0.4f), componentCtor.Value);
        }

        // Note: the Color→NeoColor implicit conversion is declared on the
        // derived wrapper, so it applies at generated-property assignment
        // (`obj.Tint = Color.red;` — target type NeoColor) but is not
        // considered when converting to the base NeoReadOnlyColor parameter.
        // Direct funnel calls therefore pass a detached wrapper explicitly,
        // exactly what the generated setter forwards.
        [Test]
        public void SetColor_WritesDetachedWrapperThroughNode()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());

            NeoGeneratedTypesSupport.SetColor(client.save, "Tint", new NeoColor(Color.red));

            var tint = new NeoColor(client.save.Get<NeoAttributeColorWritable>("Tint"));
            Assert.AreEqual(Color.red, tint.Value);
        }

        [Test]
        public void SetColor_BoundWrapperCopiesValueWithoutLinking()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var accent = new NeoColor(client.save.Get<NeoAttributeColorWritable>("Accent"));
            var accentBefore = accent.Value;

            NeoGeneratedTypesSupport.SetColor(client.save, "Tint", accent);
            var tint = new NeoColor(client.save.Get<NeoAttributeColorWritable>("Tint"));
            Assert.AreEqual(accentBefore, tint.Value);

            // Mutating the source afterwards must not affect the target —
            // the assignment copied a value, it did not create a link.
            NeoGeneratedTypesSupport.SetColor(client.save, "Accent", new NeoColor(Color.blue));
            Assert.AreEqual(Color.blue, accent.Value);
            Assert.AreEqual(accentBefore, tint.Value);
        }

        [Test]
        public void SetColorOrClear_NullClearsOptionalValue()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var glow = client.save.Get<NeoAttributeColorWritable>("Glow");
            Assert.IsNotNull(glow.value?.value);

            NeoGeneratedTypesSupport.SetColorOrClear(client.save, "Glow", null);

            Assert.IsNull(client.save.Get<NeoAttributeColorWritable>("Glow").value);
        }

        [Test]
        public void SetColorOrClear_NonNullWritesValue()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());

            NeoGeneratedTypesSupport.SetColorOrClear(client.save, "Glow", new NeoColor(Color.green));

            var glow = new NeoColor(client.save.Get<NeoAttributeColorWritable>("Glow"));
            Assert.AreEqual(Color.green, glow.Value);
        }

        [Test]
        public void SetColor_NullWrapperThrowsArgumentNull()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());

            var error = Assert.Throws<System.ArgumentNullException>(() =>
                NeoGeneratedTypesSupport.SetColor(client.save, "Tint", null!));
            StringAssert.Contains("Tint", error!.Message);
        }

        // ------------------------------------------------------------------
        // Value-based equality (decision 6).
        // ------------------------------------------------------------------

        [Test]
        public void Equality_BoundWrappersCompareByValue()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            NeoGeneratedTypesSupport.SetColor(client.save, "Tint", new NeoColor(Color.red));
            NeoGeneratedTypesSupport.SetColor(client.save, "Accent", new NeoColor(Color.red));

            var tint = new NeoColor(client.save.Get<NeoAttributeColorWritable>("Tint"));
            var accent = new NeoColor(client.save.Get<NeoAttributeColorWritable>("Accent"));

            Assert.IsFalse(ReferenceEquals(tint, accent));
            Assert.IsTrue(tint == accent);
            Assert.IsFalse(tint != accent);

            NeoGeneratedTypesSupport.SetColor(client.save, "Accent", new NeoColor(Color.blue));
            Assert.IsFalse(tint == accent);
            Assert.IsTrue(tint != accent);
        }

        [Test]
        public void Equality_MixedWrapperAndNativeForms()
        {
            var detached = new NeoColor(Color.red);

            Assert.IsTrue(detached == Color.red);
            Assert.IsTrue(Color.red == detached);
            Assert.IsFalse(detached != Color.red);
            Assert.IsFalse(Color.red != detached);
            Assert.IsFalse(detached == Color.blue);
            Assert.IsTrue(Color.blue != detached);
        }

        [Test]
        public void Equality_IsNullSafeOnBothSides()
        {
            NeoReadOnlyColor? left = null;
            NeoReadOnlyColor? right = null;
            var detached = new NeoColor(Color.red);

            Assert.IsTrue(left == right);
            Assert.IsFalse(left != right);
            Assert.IsFalse(left == detached);
            Assert.IsTrue(left != detached);
            Assert.IsFalse(detached == right);
            Assert.IsTrue(detached != right);
            Assert.IsFalse(right == Color.red);
            Assert.IsTrue(right != Color.red);
            Assert.IsFalse(Color.red == left);
            Assert.IsTrue(Color.red != left);
        }

        [Test]
        public void Equality_EqualsAndHashCodeFollowValue()
        {
            var first = new NeoColor(Color.red);
            var second = new NeoReadOnlyColor(Color.red);
            var different = new NeoColor(Color.blue);

            Assert.IsTrue(first.Equals(second));
            Assert.IsTrue(first.Equals(Color.red));
            Assert.IsFalse(first.Equals(different));
            Assert.IsFalse(first.Equals(null));
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        }

        // ------------------------------------------------------------------
        // Default-value rows (decision 4 — opaque white).
        // ------------------------------------------------------------------

        [Test]
        public void DefaultColorRow_MissingAuthoredDefaultIsOpaqueWhite()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var palette = NeoGeneratedTypesSupport.CreateWritableCustomValue(
                client,
                PaletteTypeId,
                new Dictionary<string, string>(),
                System.Array.Empty<AttributeValue>());

            var main = new NeoColor(palette.Get<NeoAttributeColorWritable>("Main"));
            Assert.AreEqual(Color.white, main.Value);
        }

        [Test]
        public void DefaultColorRow_AuthoredDefaultIsCloned()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var palette = NeoGeneratedTypesSupport.CreateWritableCustomValue(
                client,
                PaletteTypeId,
                new Dictionary<string, string>(),
                System.Array.Empty<AttributeValue>());

            var alt = new NeoColor(palette.Get<NeoAttributeColorWritable>("Alt"));
            Assert.AreEqual(new Color(0.25f, 0.5f, 0.75f, 0.5f), alt.Value);
        }

        // ------------------------------------------------------------------
        // Fixture.
        // ------------------------------------------------------------------

        private const string PaletteTypeId = "palette-type";

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
                    ["Tint"] = "tint-attribute",
                    ["Accent"] = "accent-attribute",
                    ["Glow"] = "glow-attribute",
                },
            };
            var paletteType = new CustomType
            {
                id = PaletteTypeId,
                projectId = "project-a",
                name = "Palette",
                schema = new Dictionary<string, string>
                {
                    ["Main"] = "palette-main-attribute",
                    ["Alt"] = "palette-alt-attribute",
                },
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Color Attributes",
                    rootAssetsAttributeId = "root-assets",
                    rootSaveFileAttributeId = "root-save",
                    rootSessionAttributeId = "root-session",
                },
                attributes = new Dictionary<string, NeoCompose.Runtime.Json.Attribute>
                {
                    ["root-assets"] = RootAttribute("root-assets", "root-assets-value", rootType.id),
                    ["root-save"] = RootAttribute("root-save", "root-save-value", saveRootType.id),
                    ["root-session"] = RootAttribute("root-session", "root-session-value", rootType.id),
                    ["tint-attribute"] = ColorAttributeDefinition("tint-attribute", "Tint", required: true),
                    ["accent-attribute"] = ColorAttributeDefinition("accent-attribute", "Accent", required: true),
                    ["glow-attribute"] = ColorAttributeDefinition("glow-attribute", "Glow", required: false),
                    ["palette-main-attribute"] = ColorAttributeDefinition("palette-main-attribute", "Main", required: true),
                    ["palette-alt-attribute"] = ColorAttributeDefinition(
                        "palette-alt-attribute",
                        "Alt",
                        required: true,
                        defaultValue: new NeoColorValue { r = 0.25f, g = 0.5f, b = 0.75f, a = 0.5f }),
                },
                values = new Dictionary<string, AttributeValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootType.id, new()),
                    ["root-save-value"] = ObjectValue(
                        "root-save-value",
                        saveRootType.id,
                        new Dictionary<string, string>
                        {
                            ["Tint"] = "tint-value",
                            ["Accent"] = "accent-value",
                            ["Glow"] = "glow-value",
                        }),
                    ["root-session-value"] = ObjectValue("root-session-value", rootType.id, new()),
                    ["tint-value"] = ColorValueRow("tint-value", 0.1f, 0.2f, 0.3f, 1f),
                    ["accent-value"] = ColorValueRow("accent-value", 0.4f, 0.5f, 0.6f, 1f),
                    ["glow-value"] = ColorValueRow("glow-value", 0.7f, 0.8f, 0.9f, 0.5f),
                },
                types = new Dictionary<string, CustomType>
                {
                    [rootType.id] = rootType,
                    [saveRootType.id] = saveRootType,
                    [paletteType.id] = paletteType,
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

        private static ColorAttribute ColorAttributeDefinition(
            string id,
            string name,
            bool required,
            NeoColorValue? defaultValue = null)
        {
            return new ColorAttribute
            {
                id = id,
                projectId = "project-a",
                name = name,
                type = AttributeType.Color,
                required = required,
                defaultValue = defaultValue is null
                    ? null
                    : new ColorAttributeValueBase { value = defaultValue },
            };
        }

        private static ColorAttributeValue ColorValueRow(
            string id,
            float r,
            float g,
            float b,
            float a)
        {
            return new ColorAttributeValue
            {
                id = id,
                value = new NeoColorValue { r = r, g = g, b = b, a = a },
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
