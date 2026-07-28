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
    /// Color member SDK support (specs/color-member.md §5): the strict
    /// {r,g,b,a} converter, shape sniffing, the NeoReadOnlyColor/NeoColor
    /// wrappers, the SetColor / SetColorOrClear write funnels, and the
    /// opaque-white default (decision 4).
    ///
    /// <para>P42 §4.1 overturns color-member.md §6 decisions 5–6, which made
    /// the wrapper family entirely get-only. <c>NeoReadOnlyColor</c> gains
    /// r/g/b/a channel accessors (it had none — reading a channel used to mean
    /// <c>obj.Tint.Value.a</c>), and <c>NeoColor</c> gains write-through
    /// channel setters: bound writes the whole leaf back immediately, detached
    /// mutates locally. Whole-value assignment still copies, and that is still
    /// asserted below.</para>
    ///
    /// <para>Channel writes reject out-of-[0,1] values rather than clamping
    /// them (§1.4 / decision D2), matching the converter on the read path.</para>
    /// </summary>
    public class NeoColorMemberTests
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
        public void ShapeSniffing_ColorShapeResolvesColorMemberValue()
        {
            var row = JsonConvert.DeserializeObject<MemberValue>(
                "{\"id\":\"v1\",\"value\":{\"r\":0.1,\"g\":0.2,\"b\":0.3,\"a\":1}}");
            Assert.IsInstanceOf<ColorMemberValue>(row);
            var color = (ColorMemberValue)row!;
            Assert.AreEqual(0.1f, color.value!.r);
            Assert.AreEqual(1f, color.value.a);
        }

        [Test]
        public void ShapeSniffing_ColorShapeResolvesColorMemberValueBase()
        {
            var carrier = JsonConvert.DeserializeObject<MemberValueBase>(
                "{\"value\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1}}");
            Assert.IsInstanceOf<ColorMemberValueBase>(carrier);
        }

        [Test]
        public void ShapeSniffing_ExistingObjectShapesAreUnaffected()
        {
            Assert.IsInstanceOf<Vector2MemberValue>(
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v1\",\"value\":{\"x\":1,\"y\":2}}"));
            Assert.IsInstanceOf<Vector3MemberValue>(
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v2\",\"value\":{\"x\":1,\"y\":2,\"z\":3}}"));
            Assert.IsInstanceOf<SpriteMemberValue>(
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v3\",\"value\":{\"fileId\":\"f\",\"sliceIndex\":0}}"));
            Assert.IsInstanceOf<FileMemberValue>(
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v4\",\"value\":{\"fileId\":\"f\"}}"));
            // An out-of-range component is not a Color shape; the generic
            // object fallback keeps handling it.
            Assert.IsInstanceOf<ObjectMemberValue>(
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v5\",\"value\":{\"r\":2,\"g\":1,\"b\":1,\"a\":\"s\"}}"));
        }

        [Test]
        public void ColorMember_DeserializesByKindOrdinal()
        {
            var member = JsonConvert.DeserializeObject<NeoCompose.Runtime.Json.Member>(
                "{\"id\":\"a1\",\"projectId\":\"p\",\"name\":\"Tint\",\"kind\":19,\"isStatic\":false,\"accessModifierKind\":\"public\"," +
                "\"defaultValue\":{\"value\":{\"r\":0.25,\"g\":0.5,\"b\":0.75,\"a\":1}}}");
            Assert.IsInstanceOf<ColorMember>(member);
            var color = (ColorMember)member!;
            Assert.AreEqual(MemberKind.Color, color.kind);
            Assert.AreEqual(0.25f, color.defaultValue!.value!.r);
        }

        // ------------------------------------------------------------------
        // Wrappers + write funnels.
        // ------------------------------------------------------------------

        [Test]
        public void Wrapper_BoundReadViaImplicitConversionAndValue()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var wrapper = new NeoColor(client.save.Get<NeoMemberColorWritable>("Tint"));

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

            var tint = new NeoColor(client.save.Get<NeoMemberColorWritable>("Tint"));
            Assert.AreEqual(Color.red, tint.Value);
        }

        [Test]
        public void SetColor_BoundWrapperCopiesValueWithoutLinking()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var accent = new NeoColor(client.save.Get<NeoMemberColorWritable>("Accent"));
            var accentBefore = accent.Value;

            NeoGeneratedTypesSupport.SetColor(client.save, "Tint", accent);
            var tint = new NeoColor(client.save.Get<NeoMemberColorWritable>("Tint"));
            Assert.AreEqual(accentBefore, tint.Value);

            // Changing the source leaf afterwards must not affect the target —
            // the assignment copied a value, it did not create a link. That
            // holds for whole-value writes...
            NeoGeneratedTypesSupport.SetColor(client.save, "Accent", new NeoColor(Color.blue));
            Assert.AreEqual(Color.blue, accent.Value);
            Assert.AreEqual(accentBefore, tint.Value);

            // ...and for P42's write-through channel writes.
            accent.g = 0.25f;
            Assert.AreEqual(0.25f, accent.g);
            Assert.AreEqual(accentBefore, tint.Value);
        }

        // ------------------------------------------------------------------
        // P42 §4.1 — channel accessors and write-through channel setters.
        // ------------------------------------------------------------------

        [Test]
        public void ReadOnlyWrapper_ExposesChannelAccessors()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var bound = new NeoReadOnlyColor(client.save.Get<NeoMemberColor>("Tint"));

            Assert.AreEqual(0.1f, bound.r);
            Assert.AreEqual(0.2f, bound.g);
            Assert.AreEqual(0.3f, bound.b);
            Assert.AreEqual(1f, bound.a);

            var detached = new NeoReadOnlyColor(0.4f, 0.5f, 0.6f, 0.7f);
            Assert.AreEqual(0.4f, detached.r);
            Assert.AreEqual(0.7f, detached.a);
        }

        [Test]
        public void Wrapper_BoundChannelSetterWritesThrough()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var tint = new NeoColor(client.save.Get<NeoMemberColorWritable>("Tint"));

            tint.a = 0.5f;

            var reread = new NeoColor(client.save.Get<NeoMemberColorWritable>("Tint"));
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 0.5f), reread.Value);
            // The other three channels survived the read-modify-write.
            Assert.AreEqual(0.1f, reread.r);
            Assert.AreEqual(0.2f, reread.g);
            Assert.AreEqual(0.3f, reread.b);
        }

        [Test]
        public void Wrapper_DetachedChannelSetterStaysLocal()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var detached = new NeoColor(Color.red);

            detached.g = 0.5f;

            Assert.AreEqual(new Color(1f, 0.5f, 0f, 1f), detached.Value);
            var tint = new NeoColor(client.save.Get<NeoMemberColorWritable>("Tint"));
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 1f), tint.Value);

            // ...until the detached copy is assigned.
            NeoGeneratedTypesSupport.SetColor(client.save, "Tint", detached);
            Assert.AreEqual(new Color(1f, 0.5f, 0f, 1f), tint.Value);
        }

        [Test]
        public void ChannelSetter_RejectsComponentBelowZero()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var tint = new NeoColor(client.save.Get<NeoMemberColorWritable>("Tint"));

            var error = Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                tint.r = -0.1f);
            StringAssert.Contains("'r' must not be less than 0", error!.Message);
            // Rejected, not clamped: the leaf is untouched.
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 1f), tint.Value);
        }

        [Test]
        public void ChannelSetter_RejectsComponentAboveOne()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var tint = new NeoColor(client.save.Get<NeoMemberColorWritable>("Tint"));

            var error = Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                tint.b = 1.5f);
            StringAssert.Contains("'b' must not be greater than 1", error!.Message);
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 1f), tint.Value);
        }

        [Test]
        public void ChannelSetter_RejectsNonFiniteComponent()
        {
            var detached = new NeoColor(Color.red);

            var error = Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                detached.a = float.NaN);
            StringAssert.Contains("'a' must be a finite number", error!.Message);
            Assert.AreEqual(Color.red, detached.Value);
        }

        [Test]
        public void ChannelSetter_AcceptsTheRangeBounds()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var tint = new NeoColor(client.save.Get<NeoMemberColorWritable>("Tint"));

            tint.r = 0f;
            tint.g = 1f;

            Assert.AreEqual(new Color(0f, 1f, 0.3f, 1f), tint.Value);
        }

        [Test]
        public void ChannelSetter_WithoutACurrentValueThrows()
        {
            // A field write is a read-modify-write, so there has to be
            // something to modify. The sprite wrapper says so through
            // RequireValue; Color says so through the same read its channel
            // setter composes from. Neither may invent a base value to merge
            // the one channel into.
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            NeoGeneratedTypesSupport.SetColorOrClear(client.save, "Glow", null);
            var glow = new NeoColor(client.save.Get<NeoMemberColorWritable>("Glow"));
            Assert.IsNull(client.save.Get<NeoMemberColorWritable>("Glow").value);

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                glow.a = 0.5f);
            StringAssert.Contains("has no value", error!.Message);
            StringAssert.Contains("'a'", error.Message);
            StringAssert.DoesNotContain("Required", error.Message);

            // Nothing was composed against a phantom base and written back.
            Assert.IsNull(client.save.Get<NeoMemberColorWritable>("Glow").value);
        }

        [Test]
        public void ChannelAccessor_WithoutACurrentValueThrows()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            NeoGeneratedTypesSupport.SetColorOrClear(client.save, "Glow", null);
            var glow = new NeoReadOnlyColor(client.save.Get<NeoMemberColor>("Glow"));

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                _ = glow.a);
            StringAssert.Contains("has no value", error!.Message);
        }

        // The message must not claim the member is required when it is not:
        // Glow is optional, and "Required Color 'Glow' has no value." — what
        // the wrapper used to say for every member alike — was simply false.
        // It names the channel that was read instead, the way the sprite
        // wrapper does.
        [Test]
        public void ChannelAccessor_OnAnOptionalMemberDoesNotClaimItIsRequired()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            NeoGeneratedTypesSupport.SetColorOrClear(client.save, "Glow", null);
            var glow = new NeoReadOnlyColor(client.save.Get<NeoMemberColor>("Glow"));

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                _ = glow.a);
            Assert.AreEqual(
                "Cannot read 'a': Color 'Glow' has no value.",
                error!.Message);
        }

        // One message shape per condition — a required member with no value
        // reports exactly the same thing, minus any claim about requiredness.
        [Test]
        public void ChannelAccessor_OnARequiredMemberReportsTheSameMessage()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var halo = new NeoReadOnlyColor(client.save.Get<NeoMemberColor>("Halo"));

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                _ = halo.r);
            Assert.AreEqual(
                "Cannot read 'r': Color 'Halo' has no value.",
                error!.Message);
        }

        // The whole-value read has no one channel to blame, so it names none.
        [Test]
        public void ValueAccessor_WithoutACurrentValueNamesNoField()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var halo = new NeoReadOnlyColor(client.save.Get<NeoMemberColor>("Halo"));

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                _ = halo.Value);
            Assert.AreEqual("Color 'Halo' has no value.", error!.Message);
        }

        // A detached wrapper always has a value; nothing about the new
        // message path may make one throw.
        [Test]
        public void ChannelAccessor_OnADetachedWrapperNeverThrows()
        {
            var detached = new NeoReadOnlyColor(Color.cyan);

            Assert.AreEqual(Color.cyan.r, detached.r);
            Assert.AreEqual(Color.cyan.g, detached.g);
            Assert.AreEqual(Color.cyan.b, detached.b);
            Assert.AreEqual(Color.cyan.a, detached.a);
        }

        [Test]
        public void ChannelSetter_OnNonWritableNodeThrows()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var wrapper = new NeoColor(new NeoMemberColor(
                client,
                "tint-member",
                "tint-value",
                NeoValueOwnership.Save));

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                wrapper.a = 0.5f);
            StringAssert.Contains("read-only", error!.Message);
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 1f), wrapper.Value);
        }

        [Test]
        public void ChannelSetter_OnReadOnlyOwnerThrows()
        {
            // Decision D5 — the node is writable, but the generated value that
            // handed out the wrapper is not.
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var owner = new NeoReadOnlyClassValueDouble(client, client.save, "save-root-class");
            var wrapper = new NeoColor(
                client.save.Get<NeoMemberColorWritable>("Tint"),
                owner);

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                wrapper.r = 0.5f);
            StringAssert.Contains("read-only", error!.Message);
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 1f), wrapper.Value);
        }

        [Test]
        public void SetColorOrClear_NullClearsOptionalValue()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var glow = client.save.Get<NeoMemberColorWritable>("Glow");
            Assert.IsNotNull(glow.value?.value);

            NeoGeneratedTypesSupport.SetColorOrClear(client.save, "Glow", null);

            Assert.IsNull(client.save.Get<NeoMemberColorWritable>("Glow").value);
        }

        [Test]
        public void SetColorOrClear_NonNullWritesValue()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());

            NeoGeneratedTypesSupport.SetColorOrClear(client.save, "Glow", new NeoColor(Color.green));

            var glow = new NeoColor(client.save.Get<NeoMemberColorWritable>("Glow"));
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
        // Change notification. A channel write is a read-modify-write of the
        // whole leaf, so subscribers see exactly what a whole-value write
        // raises.
        // ------------------------------------------------------------------

        [Test]
        public void ChannelWrite_NotifiesSubscribersLikeAWholeValueWrite()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var node = client.save.Get<NeoMemberColorWritable>("Tint");
            int changes = 0;
            node.OnChanged += _ => changes++;

            node.Set(Color.red);
            Assert.AreEqual(1, changes, "whole-value write");

            new NeoColor(node).g = 0.25f;
            Assert.AreEqual(2, changes, "channel write");

            new NeoColor(node).a = 0f;
            Assert.AreEqual(3, changes, "second channel write");
        }

        [Test]
        public void RejectedChannelWrite_NotifiesNobody()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var node = client.save.Get<NeoMemberColorWritable>("Tint");
            int changes = 0;
            node.OnChanged += _ => changes++;

            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new NeoColor(node).r = 1.5f);

            Assert.AreEqual(0, changes);
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

            var tint = new NeoColor(client.save.Get<NeoMemberColorWritable>("Tint"));
            var accent = new NeoColor(client.save.Get<NeoMemberColorWritable>("Accent"));

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
            var palette = NeoGeneratedTypesSupport.CreateWritableClassValue(
                client,
                PaletteClassId,
                new Dictionary<string, string>(),
                System.Array.Empty<MemberValue>());

            var main = new NeoColor(palette.Get<NeoMemberColorWritable>("Main"));
            Assert.AreEqual(Color.white, main.Value);
        }

        [Test]
        public void DefaultColorRow_AuthoredDefaultIsCloned()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var palette = NeoGeneratedTypesSupport.CreateWritableClassValue(
                client,
                PaletteClassId,
                new Dictionary<string, string>(),
                System.Array.Empty<MemberValue>());

            var alt = new NeoColor(palette.Get<NeoMemberColorWritable>("Alt"));
            Assert.AreEqual(new Color(0.25f, 0.5f, 0.75f, 0.5f), alt.Value);
        }

        // ------------------------------------------------------------------
        // Fixture.
        // ------------------------------------------------------------------

        private const string PaletteClassId = "palette-class";

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
                    ["Tint"] = "tint-member",
                    ["Accent"] = "accent-member",
                    ["Glow"] = "glow-member",
                    ["Halo"] = "halo-member",
                },
            };
            var paletteClass = new NeoSchemaClass
            {
                id = PaletteClassId,
                projectId = "project-a",
                name = "Palette",
                schema = new Dictionary<string, string>
                {
                    ["Main"] = "palette-main-member",
                    ["Alt"] = "palette-alt-member",
                },
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Color Members",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", saveRootClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                    ["tint-member"] = ColorMemberDefinition("tint-member", "Tint", required: true),
                    ["accent-member"] = ColorMemberDefinition("accent-member", "Accent", required: true),
                    ["glow-member"] = ColorMemberDefinition("glow-member", "Glow", required: false),
                    // Required, and deliberately left without a value row (no
                    // entry in the save record below) so the missing-value
                    // message can be pinned for the required case too.
                    ["halo-member"] = ColorMemberDefinition("halo-member", "Halo", required: true),
                    ["palette-main-member"] = ColorMemberDefinition("palette-main-member", "Main", required: true),
                    ["palette-alt-member"] = ColorMemberDefinition(
                        "palette-alt-member",
                        "Alt",
                        required: true,
                        defaultValue: new NeoColorValue { r = 0.25f, g = 0.5f, b = 0.75f, a = 0.5f }),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id, new()),
                    ["root-save-value"] = ObjectValue(
                        "root-save-value",
                        saveRootClass.id,
                        new Dictionary<string, string>
                        {
                            ["Tint"] = "tint-value",
                            ["Accent"] = "accent-value",
                            ["Glow"] = "glow-value",
                        }),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id, new()),
                    ["tint-value"] = ColorValueRow("tint-value", 0.1f, 0.2f, 0.3f, 1f),
                    ["accent-value"] = ColorValueRow("accent-value", 0.4f, 0.5f, 0.6f, 1f),
                    ["glow-value"] = ColorValueRow("glow-value", 0.7f, 0.8f, 0.9f, 0.5f),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [saveRootClass.id] = saveRootClass,
                    [paletteClass.id] = paletteClass,
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

        private static ColorMember ColorMemberDefinition(
            string id,
            string name,
            bool required,
            NeoColorValue? defaultValue = null)
        {
            return new ColorMember
            {
                id = id,
                projectId = "project-a",
                name = name,
                kind = MemberKind.Color,
                required = required,
                defaultValue = defaultValue is null
                    ? null
                    : new ColorMemberValueBase { value = defaultValue },
            };
        }

        private static ColorMemberValue ColorValueRow(
            string id,
            float r,
            float g,
            float b,
            float a)
        {
            return new ColorMemberValue
            {
                id = id,
                value = new NeoColorValue { r = r, g = g, b = b, a = a },
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
