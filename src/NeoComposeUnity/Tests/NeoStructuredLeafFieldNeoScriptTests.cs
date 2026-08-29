// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P42 §3. NeoScript reads and writes of a structured leaf's fields on the
    /// .NET evaluators.
    ///
    /// <para>The spec claims assignment to a keyOf target "already works end to
    /// end". That is true of the TypeScript evaluator and was false of this
    /// one: vector and colour receivers were never reverse-indexed, so a field
    /// assignment died at "Assignment receiver is not backed by a Neo value
    /// row"; a sprite receiver reached the row and then fell off the end of
    /// <c>ResolveKeyOfTarget</c>'s list/dictionary/class branches; and reading
    /// a colour channel at all threw "Cannot index into … with key 'a'" while
    /// the TS evaluator read it happily. These tests pin all three fixes and
    /// the read-modify-write semantics of §1.2 and §1.4.</para>
    /// </summary>
    public class NeoStructuredLeafFieldNeoScriptTests
    {
        private const string LegFileId = "file-body-normal-walk-side-leg";
        private const string PantsFileId = "file-pants-long-walk-side-leg";

        // ------------------------------------------------------------------
        // §3 reads.
        // ------------------------------------------------------------------

        [Test]
        public void ColorChannel_IsReadable()
        {
            // The regression this test exists for: before P42 a ColorMemberValue
            // unwrapped to a bare NeoColorValue, which is neither a vector nor
            // an IDictionary, so this threw.
            Assert.AreEqual(0.25f, ReadLeafField("Tint", "a"));
            Assert.AreEqual(1f, ReadLeafField("Tint", "r"));
            Assert.AreEqual(0.5f, ReadLeafField("Tint", "g"));
            Assert.AreEqual(0f, ReadLeafField("Tint", "b"));
        }

        [Test]
        public void ColorChannel_ReadsEveryColorShape()
        {
            // The three CLR shapes a Color receiver can arrive as. The row
            // shape is covered above; these two reach the evaluator through a
            // generated wrapper or a Unity-native value.
            Assert.AreEqual(
                0.25f,
                ReadThisKey(new Color(1f, 0.5f, 0f, 0.25f), "a"));
            Assert.AreEqual(
                0.5f,
                ReadThisKey(new NeoReadOnlyColor(1f, 0.5f, 0f, 0.25f), "g"));
        }

        [Test]
        public void ColorChannel_DoesNotAnswerVectorKeys()
        {
            // A colour is four named channels, not a four-component vector.
            var error = Assert.Throws<NSGetterRuntimeError>(
                () => ReadLeafField("Tint", "z"));
            StringAssert.Contains("with key 'z'", error!.Message);
        }

        [Test]
        public void VectorComponent_IsReadable()
        {
            // Negative control: this worked before P42 and must keep working.
            Assert.AreEqual(1f, ReadLeafField("Position", "x"));
            Assert.AreEqual(0f, ReadLeafField("Position", "y"));
            Assert.AreEqual(3f, ReadLeafField("Position", "z"));
            Assert.AreEqual(2f, ReadLeafField("Cell", "x"));
            Assert.AreEqual(4f, ReadLeafField("Cell", "y"));
        }

        [Test]
        public void SpriteFields_AreReadable()
        {
            Assert.AreEqual(LegFileId, ReadLeafField("Sprite", "fileId"));
            Assert.AreEqual(0, ReadLeafField("Sprite", "sliceIndex"));
        }

        // ------------------------------------------------------------------
        // §2.3 — the imageSlice intrinsic.
        // ------------------------------------------------------------------

        [Test]
        public void ImageSlice_DeserializesFromTheWire()
        {
            // The parity fixture exercises the arm's behaviour; this pins that
            // the discriminator actually resolves, which the fixture (which
            // builds the IR in memory) cannot catch.
            var function = JsonConvert.DeserializeObject<Function>(
                @"{
                    ""type"": ""imageSlice"",
                    ""info"": {
                        ""filePointer"": {
                            ""type"": ""value"",
                            ""value"": {
                                ""typeInfo"": { ""type"": 3, ""required"": true },
                                ""value"": ""file-a""
                            }
                        },
                        ""sliceIndexPointer"": {
                            ""type"": ""value"",
                            ""value"": {
                                ""typeInfo"": { ""type"": 2, ""required"": true },
                                ""value"": 2
                            }
                        }
                    }
                }");

            Assert.IsInstanceOf<ImageSliceFunction>(function);
            var imageSlice = (ImageSliceFunction)function!;
            Assert.IsInstanceOf<ValuePointer>(imageSlice.info.filePointer);
            Assert.IsInstanceOf<ValuePointer>(imageSlice.info.sliceIndexPointer);
        }

        [Test]
        public void ImageSlice_ProducesTheStoredSpriteRecordShape()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            object? produced = NSGetterEvaluator.Evaluate(
                Getter(ImageSlicePointer(PantsFileId, 3)),
                ctx);

            Assert.IsInstanceOf<IDictionary<string, object?>>(produced);
            var record = (IDictionary<string, object?>)produced!;
            Assert.AreEqual(2, record.Count);
            Assert.AreEqual(PantsFileId, record["fileId"]);
            Assert.AreEqual(3, record["sliceIndex"]);
        }

        // ------------------------------------------------------------------
        // §1.2 / §3 writes.
        // ------------------------------------------------------------------

        [Test]
        public void VectorComponentAssignment_WritesOneComponentAndLeavesSiblings()
        {
            NeoClient client = BuildClient();
            AssignLeafField(client, "Position", "y", NumberLiteral(0.25), FloatType());

            NeoVector3Value position = RequireVector3(client, "value-position");
            Assert.AreEqual(0.25f, position.y);
            // §1.4: the siblings are copied from whatever the row held, never
            // re-asserted from the authored default.
            Assert.AreEqual(1f, position.x);
            Assert.AreEqual(3f, position.z);
        }

        [Test]
        public void ColorChannelAssignment_WritesOneChannel()
        {
            NeoClient client = BuildClient();
            AssignLeafField(client, "Tint", "a", NumberLiteral(0.5), FloatType());

            NeoColorValue tint = RequireColor(client, "value-tint");
            Assert.AreEqual(0.5f, tint.a);
            Assert.AreEqual(1f, tint.r);
            Assert.AreEqual(0.5f, tint.g);
            Assert.AreEqual(0f, tint.b);
        }

        [Test]
        public void SpriteSliceIndexAssignment_LeavesFileIdAlone()
        {
            NeoClient client = BuildClient();
            AssignLeafField(client, "Sprite", "sliceIndex", NumberLiteral(2), IntType());

            SpriteValue sprite = RequireSprite(client, "value-sprite");
            Assert.AreEqual(2, sprite.sliceIndex);
            Assert.AreEqual(LegFileId, sprite.fileId);
        }

        [Test]
        public void SpriteFileIdAssignment_RebindsTheSheetAndKeepsTheSliceIndex()
        {
            // §2.2. The registry symbol lowered to the project file record id,
            // so the right-hand side is a bare string here.
            NeoClient client = BuildClient();
            AssignLeafField(client, "Sprite", "sliceIndex", NumberLiteral(1), IntType());
            AssignLeafField(
                client,
                "Sprite",
                "fileId",
                StringLiteral(PantsFileId),
                StringType());

            SpriteValue sprite = RequireSprite(client, "value-sprite");
            Assert.AreEqual(PantsFileId, sprite.fileId);
            Assert.AreEqual(1, sprite.sliceIndex);
        }

        [Test]
        public void TwoFieldWritesToOneLeaf_LandOnOneRow()
        {
            // §1.2: ordering within a frame is not observable and both writes
            // address the same value row — there is no second storage unit.
            NeoClient client = BuildClient();
            var ctx = ContextWithRoot(client, out Dictionary<string, object?> scope);
            NeoScriptExecutor.Execute(
                client,
                Action(
                    AssignInstruction("Position", "x", NumberLiteral(4), FloatType()),
                    AssignInstruction("Position", "z", NumberLiteral(5), FloatType())),
                scope,
                ctx);

            NeoVector3Value position = RequireVector3(client, "value-position");
            Assert.AreEqual(4f, position.x);
            Assert.AreEqual(0f, position.y);
            Assert.AreEqual(5f, position.z);
        }

        [Test]
        public void FieldWrite_IsVisibleToAFollowingFieldRead()
        {
            // The row's unwrapped CLR payload is what the receiver aliases, so
            // a field write has to keep the evaluator's row cache coherent or
            // the very next read in the same frame returns the stale value.
            NeoClient client = BuildClient();
            var ctx = ContextWithRoot(client, out Dictionary<string, object?> scope);
            var getter = Getter(LeafFieldPointer("Position", "y"));
            NeoScriptExecutor.Execute(
                client,
                Action(AssignInstruction("Position", "y", NumberLiteral(0.25), FloatType())),
                scope,
                ctx);

            Assert.AreEqual(0.25f, NSGetterEvaluator.Evaluate(getter, ctx));
        }

        [Test]
        public void SpriteFieldWrite_IsVisibleToAFollowingFieldRead()
        {
            NeoClient client = BuildClient();
            var ctx = ContextWithRoot(client, out Dictionary<string, object?> scope);
            var getter = Getter(LeafFieldPointer("Sprite", "sliceIndex"));
            NeoScriptExecutor.Execute(
                client,
                Action(AssignInstruction("Sprite", "sliceIndex", NumberLiteral(3), IntType())),
                scope,
                ctx);

            Assert.AreEqual(3, NSGetterEvaluator.Evaluate(getter, ctx));
        }

        // ------------------------------------------------------------------
        // Rejections.
        // ------------------------------------------------------------------

        [Test]
        public void IntVectorComponentAssignment_RejectsAFractionalValue()
        {
            // §1.4: a non-integer component is an error, with no runtime
            // coercion. The resolver types an integer vector's components as
            // Int, which is the signal a stored Vector2MemberValue cannot give.
            NeoClient client = BuildClient();
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                AssignLeafField(client, "Cell", "x", NumberLiteral(0.5), IntType()));

            StringAssert.Contains("must be an integer", error!.Message);
            Assert.AreEqual(2f, RequireVector2(client, "value-cell").x);
        }

        [Test]
        public void ColorChannelAssignment_RejectsOutOfRangeRatherThanClamping()
        {
            // Decision D2. NeoColorValueConverter already refuses on
            // deserialize; a field write must not become the way around it.
            NeoClient client = BuildClient();
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                AssignLeafField(client, "Tint", "a", NumberLiteral(1.5), FloatType()));

            StringAssert.Contains("[0, 1]", error!.Message);
            Assert.AreEqual(0.25f, RequireColor(client, "value-tint").a);
        }

        [Test]
        public void SpriteSliceIndexAssignment_RejectsANegativeIndex()
        {
            NeoClient client = BuildClient();
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                AssignLeafField(client, "Sprite", "sliceIndex", NumberLiteral(-1), IntType()));

            StringAssert.Contains("0 or greater", error!.Message);
        }

        [Test]
        public void UnknownField_IsRejectedByNameWithItsKindsLegalFields()
        {
            NeoClient client = BuildClient();
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                AssignLeafField(client, "Cell", "z", NumberLiteral(1), IntType()));

            StringAssert.Contains("'z' is not a field of a Vector2Int value", error!.Message);
            StringAssert.Contains("x, y", error.Message);
        }

        [Test]
        public void FieldAssignment_OnAnImmutableTargetFailsLikeAWholeValueAssignment()
        {
            // §3: "a field assignment on an Immutable-resolved member fails
            // exactly as a whole-value assignment would". Both go through
            // TargetOwnership, which runs before the target is resolved at all,
            // so the two produce the identical message.
            NeoClient client = BuildClient();
            var ctx = ContextWithRoot(client, out Dictionary<string, object?> scope);

            var fieldError = Assert.Throws<NSGetterRuntimeError>(() =>
                NeoScriptExecutor.Execute(
                    client,
                    Action(AssignInstruction(
                        "Position",
                        "y",
                        NumberLiteral(0.25),
                        FloatType(),
                        WritabilityKind.Immutable)),
                    scope,
                    ctx));
            var wholeError = Assert.Throws<NSGetterRuntimeError>(() =>
                NeoScriptExecutor.Execute(
                    client,
                    Action(new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        target = new WriteTarget
                        {
                            pointer = LeafPointer("Position"),
                            typeInfo = new PrimitiveTypeInfo
                            {
                                type = MemberKind.Vector3,
                                required = true,
                            },
                            writability = WritabilityKind.Immutable,
                        },
                        operatorValue = "=",
                        pointer = NumberLiteral(0),
                    }),
                    scope,
                    ctx));

            Assert.AreEqual(wholeError!.Message, fieldError!.Message);
            StringAssert.Contains("read-only", fieldError.Message);
            Assert.AreEqual(0f, RequireVector3(client, "value-position").y);
        }

        [Test]
        public void FieldAssignment_OnANullLeafIsRejected()
        {
            // §1.3: there is no record to merge a field into. The receiver
            // itself evaluates to null, so this is rejected before any field
            // handling runs — the same way reading through a null leaf is.
            NeoClient client = BuildClient();
            Assert.Throws<NSGetterRuntimeError>(() =>
                AssignLeafField(client, "Highlight", "a", NumberLiteral(0.5), FloatType()));
        }

        // ------------------------------------------------------------------
        // Harness.
        // ------------------------------------------------------------------

        private static object? ReadLeafField(string leaf, string field)
        {
            NeoClient client = BuildClient();
            var ctx = ContextWithRoot(client, out _);
            return NSGetterEvaluator.Evaluate(Getter(LeafFieldPointer(leaf, field)), ctx);
        }

        private static object? ReadThisKey(object receiver, string key)
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, receiver, null);
            return NSGetterEvaluator.Evaluate(
                Getter(KeyOf(ThisVariable(), key)),
                ctx);
        }

        private static void AssignLeafField(
            NeoClient client,
            string leaf,
            string field,
            Pointer value,
            TypeInfo fieldType)
        {
            var ctx = ContextWithRoot(client, out Dictionary<string, object?> scope);
            NeoScriptExecutor.Execute(
                client,
                Action(AssignInstruction(leaf, field, value, fieldType)),
                scope,
                ctx);
        }

        private static NSGetterEvaluator.Context ContextWithRoot(
            NeoClient client,
            out Dictionary<string, object?> scope)
        {
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            Dictionary<string, object?> root = RuntimeRoot(client, ctx);
            ctx = ctx.WithRoot(root);
            scope = new Dictionary<string, object?> { ["__root__"] = root };
            return ctx;
        }

        private static AssignInstruction AssignInstruction(
            string leaf,
            string field,
            Pointer value,
            TypeInfo fieldType,
            string writability = WritabilityKind.Session)
        {
            return new AssignInstruction
            {
                type = InstructionKind.Assign,
                target = new WriteTarget
                {
                    pointer = LeafFieldPointer(leaf, field),
                    typeInfo = fieldType,
                    writability = writability,
                },
                operatorValue = "=",
                pointer = value,
            };
        }

        private static SpriteValue RequireSprite(NeoClient client, string valueId)
        {
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                valueId,
                out SpriteMemberValue? row));
            Assert.IsNotNull(row!.value);
            return row.value!;
        }

        private static NeoVector3Value RequireVector3(NeoClient client, string valueId)
        {
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                valueId,
                out Vector3MemberValue? row));
            Assert.IsNotNull(row!.value);
            return row.value!;
        }

        private static NeoVector2Value RequireVector2(NeoClient client, string valueId)
        {
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                valueId,
                out Vector2MemberValue? row));
            Assert.IsNotNull(row!.value);
            return row.value!;
        }

        private static NeoColorValue RequireColor(NeoClient client, string valueId)
        {
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                valueId,
                out ColorMemberValue? row));
            Assert.IsNotNull(row!.value);
            return row.value!;
        }

        // ------------------------------------------------------------------
        // IR builders.
        // ------------------------------------------------------------------

        private static FunctionWithReturnType Getter(Pointer pointer)
        {
            var getter = Action(new ReturnInstruction
            {
                type = InstructionKind.Return,
                pointer = pointer,
            });
            getter.typeInfo = FloatType();
            return getter;
        }

        private static FunctionWithReturnType Action(params Instruction[] instructions)
        {
            return new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                instructions = instructions,
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.Null,
                    required = true,
                },
            };
        }

        private static KeyOfPointer LeafPointer(string leaf) =>
            KeyOf(KeyOf(KeyOf(RootVariable(), "Session"), "Leaf"), leaf);

        private static KeyOfPointer LeafFieldPointer(string leaf, string field) =>
            KeyOf(LeafPointer(leaf), field);

        private static FunctionPointer ImageSlicePointer(string fileId, double sliceIndex)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new ImageSliceFunction
                {
                    type = FunctionKind.ImageSlice,
                    info = new FunctionImageSliceInfo
                    {
                        filePointer = StringLiteral(fileId),
                        sliceIndexPointer = NumberLiteral(sliceIndex),
                    },
                },
            };
        }

        private static KeyOfPointer KeyOf(Pointer receiver, string key) => new()
        {
            type = PointerKind.KeyOf,
            keyOf = new KeyOf
            {
                pointer = receiver,
                key = StringLiteral(key),
            },
        };

        private static VariablePointer RootVariable() => new()
        {
            type = PointerKind.Variable,
            variableId = "__root__",
        };

        private static VariablePointer ThisVariable() => new()
        {
            type = PointerKind.Variable,
            variableId = "__this__",
        };

        private static ValuePointer NumberLiteral(double value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = FloatType(),
                value = JToken.FromObject(value),
            },
        };

        private static ValuePointer StringLiteral(string value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = StringType(),
                value = JToken.FromObject(value),
            },
        };

        private static PrimitiveTypeInfo IntType() => new()
        {
            type = MemberKind.Int,
            required = true,
        };

        private static PrimitiveTypeInfo FloatType() => new()
        {
            type = MemberKind.Float,
            required = true,
        };

        private static PrimitiveTypeInfo StringType() => new()
        {
            type = MemberKind.String,
            required = true,
        };

        // ------------------------------------------------------------------
        // Schema.
        // ------------------------------------------------------------------

        private static Dictionary<string, object?> RuntimeRoot(
            NeoClient client,
            NSGetterEvaluator.Context ctx)
        {
            return new Dictionary<string, object?>
            {
                ["Assets"] = client.assets.value is ObjectMemberValue assets
                    ? NSGetterEvaluator.UnwrapRow(assets, ctx, NeoValueOwnership.Asset)
                    : null,
                ["Save"] = client.save.value is ObjectMemberValue save
                    ? NSGetterEvaluator.UnwrapRow(save, ctx, NeoValueOwnership.Save)
                    : null,
                ["Session"] = client.session.value is ObjectMemberValue session
                    ? NSGetterEvaluator.UnwrapRow(session, ctx, NeoValueOwnership.Session)
                    : null,
            };
        }

        private static NeoClient BuildClient()
        {
            var rootAssets = ClassMember(
                "member-root-assets", "Assets", "class-root", "value-assets");
            var rootSave = ClassMember(
                "member-root-save", "Save", "class-root", "value-save", NeoMemberStorage.Save);
            var rootSession = ClassMember(
                "member-root-session", "Session", "class-root", "value-session", NeoMemberStorage.Session);
            var leafMember = ClassMember(
                "member-leaf", "Leaf", "class-leaf", "value-leaf");

            var data = new ProjectData
            {
                project = new Project
                {
                    id = "project-leaf-fields",
                    name = "Structured leaf fields",
                    rootAssetsMemberId = rootAssets.id,
                    rootSaveFileMemberId = rootSave.id,
                    rootSessionMemberId = rootSession.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                members = new Dictionary<string, JsonMember>
                {
                    [rootAssets.id] = rootAssets,
                    [rootSave.id] = rootSave,
                    [rootSession.id] = rootSession,
                    [leafMember.id] = leafMember,
                    ["member-sprite"] = new SpriteMember
                    {
                        id = "member-sprite",
                        projectId = "project-leaf-fields",
                        name = "Sprite",
                        kind = MemberKind.Sprite,
                        valueId = "value-sprite",
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["member-position"] = new Vector3Member
                    {
                        id = "member-position",
                        projectId = "project-leaf-fields",
                        name = "Position",
                        kind = MemberKind.Vector3,
                        valueId = "value-position",
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["member-cell"] = new Vector2IntMember
                    {
                        id = "member-cell",
                        projectId = "project-leaf-fields",
                        name = "Cell",
                        kind = MemberKind.Vector2Int,
                        valueId = "value-cell",
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["member-tint"] = new ColorMember
                    {
                        id = "member-tint",
                        projectId = "project-leaf-fields",
                        name = "Tint",
                        kind = MemberKind.Color,
                        valueId = "value-tint",
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["member-highlight"] = new ColorMember
                    {
                        id = "member-highlight",
                        projectId = "project-leaf-fields",
                        name = "Highlight",
                        kind = MemberKind.Color,
                        valueId = "value-highlight",
                        requirement = NeoMemberRequirementKind.Optional,
                        createdAt = "x",
                        updatedAt = "x",
                    },
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["value-assets"] = ObjectValue("value-assets", "class-root"),
                    ["value-save"] = ObjectValue("value-save", "class-root"),
                    ["value-session"] = ObjectValue(
                        "value-session",
                        "class-root",
                        ("Leaf", "value-leaf")),
                    ["value-leaf"] = ObjectValue(
                        "value-leaf",
                        "class-leaf",
                        ("Sprite", "value-sprite"),
                        ("Position", "value-position"),
                        ("Cell", "value-cell"),
                        ("Tint", "value-tint"),
                        ("Highlight", "value-highlight")),
                    ["value-sprite"] = new SpriteMemberValue
                    {
                        id = "value-sprite",
                        value = new SpriteValue { fileId = LegFileId, sliceIndex = 0 },
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["value-position"] = new Vector3MemberValue
                    {
                        id = "value-position",
                        value = new NeoVector3Value { x = 1f, y = 0f, z = 3f },
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["value-cell"] = new Vector2MemberValue
                    {
                        id = "value-cell",
                        value = new NeoVector2Value { x = 2f, y = 4f },
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["value-tint"] = new ColorMemberValue
                    {
                        id = "value-tint",
                        value = new NeoColorValue { r = 1f, g = 0.5f, b = 0f, a = 0.25f },
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    // Present but null-valued: §1.3's "no record to merge into".
                    ["value-highlight"] = new ColorMemberValue
                    {
                        id = "value-highlight",
                        value = null,
                        createdAt = "x",
                        updatedAt = "x",
                    },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["class-root"] = SchemaClass(
                        "class-root",
                        "Root",
                        ("Leaf", "member-leaf")),
                    ["class-leaf"] = SchemaClass(
                        "class-leaf",
                        "Leaf",
                        ("Sprite", "member-sprite"),
                        ("Position", "member-position"),
                        ("Cell", "member-cell"),
                        ("Tint", "member-tint"),
                        ("Highlight", "member-highlight")),
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
            return NeoTestSaveStack.ClientFromSchema(data);
        }

        private static ClassMember ClassMember(
            string id,
            string name,
            string classId,
            string valueId,
            NeoMemberStorage storage = NeoMemberStorage.Inherit)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-leaf-fields",
                name = name,
                kind = MemberKind.Class,
                classId = classId,
                valueId = valueId,
                storage = storage,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static NeoSchemaClass SchemaClass(
            string id,
            string name,
            params (string key, string memberId)[] schema)
        {
            var entries = new Dictionary<string, string>();
            foreach (var entry in schema) entries[entry.key] = entry.memberId;
            return new NeoSchemaClass
            {
                id = id,
                projectId = "project-leaf-fields",
                name = name,
                schema = entries,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static ObjectMemberValue ObjectValue(
            string id,
            string classId,
            params (string key, string valueId)[] entries)
        {
            var value = new Dictionary<string, string>();
            foreach (var entry in entries) value[entry.key] = entry.valueId;
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = value,
                createdAt = "x",
                updatedAt = "x",
            };
        }
    }
}
