// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Issue #243. NeoScript writes a Class value to a static record's
    /// Session or Save leaf exactly as the generated C# setter does: the
    /// value replaces the leaf at its own id in the leaf's writable store, and
    /// the authored record is never cloned.
    ///
    /// <para>Each body mirrors the IR the compiler emits for the NeoScript in
    /// its comment, run through the executor against stored rows, with
    /// <c>this</c> bound to the static <c>ShapeRig</c> record.</para>
    /// </summary>
    public class NeoScriptStaticLeafShadowTests
    {
        // this.Child.Shape = new ShapeBox { Width = 7 };
        [Test]
        public void AssignedValueShadowsTheLeafWithoutCloningTheRecord()
        {
            using NeoClient client = BuildClient();
            NeoTimestamp createdAt = client.ResolveValueRow("shape-leaf")!.createdAt;

            Run(client, AssignShape(NewBox(7)));

            var shape = Session<ObjectMemberValue>(client, "shape-leaf");
            Assert.AreEqual("box", shape.classId);
            Assert.AreEqual(createdAt, shape.createdAt, "The leaf keeps its identity fields.");
            Assert.AreEqual(7, WidthOf(client, NeoValueOwnership.Session, "shape-leaf"));
            AssertRecordUntouched(client);
            Assert.AreEqual(1, BoxRows(client, NeoValueOwnership.Session), "The constructed root became the leaf; no copy is left behind.");
        }

        // ShapeBox box = new ShapeBox { Width = 7 };
        // this.Child.Shape = box;
        // box.Width = 9;
        [Test]
        public void LocalThatHeldTheAssignedValueWritesTheLeaf()
        {
            using NeoClient client = BuildClient();

            Run(client,
                Local("box", NewBox(7)),
                AssignShape(Variable("box")),
                Assign(Key(Variable("box"), "Width", "width-member"), IntType(), Int(9)));

            Assert.AreEqual(9, WidthOf(client, NeoValueOwnership.Session, "shape-leaf"));
            Assert.AreEqual(1, BoxRows(client, NeoValueOwnership.Session));
            AssertRecordUntouched(client);
        }

        // ShapeBox box = new ShapeBox { Width = 7 };
        // this.Child.Kept = box; where Kept is a Save leaf.
        // this.Child.Kept.Width = 9;
        [Test]
        public void SaveLeafTakesTheSessionValueAcrossStores()
        {
            using NeoClient client = BuildClient();
            JObject kept = Key(Child(This()), "Kept", "kept-member");

            Run(client,
                Local("box", NewBox(7)),
                Assign(kept, BoxType(required: false), Variable("box"), "save"),
                Assign(Key(kept, "Width", "width-member"), IntType(), Int(9), "save"));

            Assert.AreEqual("box", Writable<ObjectMemberValue>(client, NeoValueOwnership.Save, "kept-leaf").classId);
            Assert.AreEqual(9, WidthOf(client, NeoValueOwnership.Save, "kept-leaf"));
            Assert.AreEqual(0, BoxRows(client, NeoValueOwnership.Session), "The moved value leaves nothing in Session.");
            AssertRecordUntouched(client);
        }

        // this.Child.Shape = this.Child.Shape ?? new ShapeBox { Width = 5 };
        [Test]
        public void CoalescedAssignmentShadowsOnceThenKeepsTheLeaf()
        {
            using NeoClient client = BuildClient();
            JObject coalesce = AssignShape(new JObject
            {
                ["type"] = "coalesce",
                ["left"] = ShapeOf(This()),
                ["right"] = NewBox(5),
            });

            Run(client, coalesce);
            Run(client, Assign(Key(ShapeOf(This()), "Width", "width-member"), IntType(), Int(6)));
            Run(client, coalesce);

            Assert.AreEqual(6, WidthOf(client, NeoValueOwnership.Session, "shape-leaf"));
            Assert.AreEqual(1, BoxRows(client, NeoValueOwnership.Session));
            AssertRecordUntouched(client);
        }

        // this.Child.Shape = new ShapeBox { Width = 7 };
        // this.Child.Shape = null;
        [Test]
        public void NullClearsTheShadowedLeafAndReleasesItsValue()
        {
            using NeoClient client = BuildClient();
            Run(client, AssignShape(NewBox(7)));
            string widthId = Session<ObjectMemberValue>(client, "shape-leaf").value!["Width"];

            Run(client, AssignShape(Null(BoxType(required: false))));

            // A null Class value stores as an object row with no value map.
            Assert.IsNull(Session<ObjectMemberValue>(client, "shape-leaf").value);
            Assert.IsFalse(client.HasWritableValue(NeoValueOwnership.Session, widthId), "The replaced value's rows are released.");
            AssertRecordUntouched(client);
        }

        // this.Child.Missing = new ShapeBox { Width = 7 }; where the record authors no Missing.
        [Test]
        public void KeyTheRecordDoesNotAuthorStillThrows()
        {
            using NeoClient client = BuildClient();

            var error = Assert.Throws<NSGetterRuntimeError>(() => Run(client,
                Assign(Key(Child(This()), "Missing", "missing-member"), BoxType(required: false), NewBox(7))));

            StringAssert.Contains("Cannot bind missing member 'Missing'", error!.Message);
            AssertRecordUntouched(client);
            Assert.AreEqual(0, BoxRows(client, NeoValueOwnership.Session), "A failed write leaves no constructed value behind.");
        }

        // this.Child.Boxes.Add(new ShapeBox { Width = 4 });
        // this.Child.Boxes[0] = new ShapeBox { Width = 6 };
        // this.Child.ByName["a"] = new ShapeBox { Width = 8 };
        // this.Child.Boxes = [new ShapeBox { Width = 2 }];
        [Test]
        public void CollectionLeavesOfTheRecordTakeClassValues()
        {
            using NeoClient client = BuildClient();

            Run(client, new JObject
            {
                ["type"] = "collectionCall",
                ["target"] = Target(BoxesOf(This()), BoxListType(), "session"),
                ["mutation"] = "Add",
                ["args"] = new JArray(NewBox(4)),
                ["reference"] = true,
            });
            Run(client, Assign(Key(BoxesOf(This()), Int(1)), BoxType(required: true), NewBox(6)));
            Run(client, Assign(
                Key(Key(Child(This()), "ByName", "by-name-member"), String("a")),
                BoxType(required: true),
                NewBox(8)));

            string[] boxes = Session<ArrayMemberValue>(client, "boxes-leaf").value!;
            Assert.AreEqual(2, boxes.Length);
            Assert.AreEqual("box-0", boxes[0]);
            Assert.AreEqual(6, WidthOf(client, NeoValueOwnership.Session, boxes[1]));
            string entry = Session<ObjectMemberValue>(client, "by-name-leaf").value!["a"];
            Assert.AreEqual(8, WidthOf(client, NeoValueOwnership.Session, entry));

            Run(client, Assign(BoxesOf(This()), BoxListType(), new JObject
            {
                ["type"] = "listLiteral",
                ["typeInfo"] = new JObject { ["type"] = 6, ["required"] = true, ["entryTypeInfo"] = BoxType(required: true) },
                ["entries"] = new JArray(NewBox(2)),
            }));

            boxes = Session<ArrayMemberValue>(client, "boxes-leaf").value!;
            Assert.AreEqual(1, boxes.Length);
            Assert.AreEqual(2, WidthOf(client, NeoValueOwnership.Session, boxes[0]));
            AssertRecordUntouched(client);
        }

        // ShapeBox b = new ShapeBox { Width = 4 };
        // this.Child.Boxes.Add(b);
        // this.Child.Boxes.Remove(b);
        // this.Child.Shape = b;
        [Test]
        public void ValueThePendingBatchReleasedMovesOntoTheLeaf()
        {
            using NeoClient client = BuildClient();

            Run(client,
                Local("b", NewBox(4)),
                BoxesCall("Add", Variable("b")),
                BoxesCall("Remove", Variable("b")),
                AssignShape(Variable("b")),
                Assign(Key(Variable("b"), "Width", "width-member"), IntType(), Int(5)));

            Assert.AreEqual(5, WidthOf(client, NeoValueOwnership.Session, "shape-leaf"));
            CollectionAssert.AreEqual(new[] { "box-0" }, Writable<ArrayMemberValue>(client, NeoValueOwnership.Session, "boxes-leaf").value);
            Assert.AreEqual(1, BoxRows(client, NeoValueOwnership.Session));
        }

        // ShapeBox b = new ShapeBox { Width = 4 };
        // this.Child.Boxes.Add(b);
        // this.Child.Shape = b;
        [Test]
        public void ValueAnotherParentOwnsFailsAndLeavesTheLeaf()
        {
            using NeoClient client = BuildClient();

            var error = Assert.Throws<NSGetterRuntimeError>(() => Run(client,
                Local("b", NewBox(4)),
                BoxesCall("Add", Variable("b")),
                AssignShape(Variable("b"))));

            StringAssert.Contains("already owned by parent value 'boxes-leaf'", error!.Message);
            Assert.IsInstanceOf<NullMemberValue>(client.ResolveValueRow("shape-leaf"), "The failed assignment wrote nothing to the leaf.");
            AssertRecordUntouched(client);
        }

        // this.Child.Shape = new ShapeBox { Width = 7 }; then Width = 8, in one transaction.
        [Test]
        public void TransactionCommitsEachWriteAndRaisesTheLeafOnceAfter()
        {
            using NeoClient client = BuildClient();
            var child = new NeoMemberClassWritable(client, "child-member", "child");
            var changes = new List<string>();
            child.OnChanged += changed => changes.Add(changed.member.name);

            client.RunTransaction(() =>
            {
                Run(client, AssignShape(NewBox(7)));
                Assert.AreEqual(7, WidthOf(client, NeoValueOwnership.Session, "shape-leaf"), "Writes commit as they happen.");
                Run(client, Assign(Key(ShapeOf(This()), "Width", "width-member"), IntType(), Int(8)));
                CollectionAssert.IsEmpty(changes, "Handlers wait for the transaction.");
            });

            Assert.AreEqual(1, changes.Count(name => name == "Shape"), "The leaf raises once.");
            Assert.AreEqual(changes.Count, changes.Distinct().Count(), "Each changed member raises once.");
            Assert.AreEqual(8, WidthOf(client, NeoValueOwnership.Session, "shape-leaf"));
            child.Dispose();
        }

        // A transaction that throws after the shadow keeps the write.
        [Test]
        public void TransactionThatThrowsKeepsTheShadowedLeaf()
        {
            using NeoClient client = BuildClient();

            Assert.Throws<InvalidOperationException>(() => client.RunTransaction(() =>
            {
                Run(client, AssignShape(NewBox(7)));
                throw new InvalidOperationException("Transaction failed.");
            }));

            Assert.AreEqual(7, WidthOf(client, NeoValueOwnership.Session, "shape-leaf"));
            AssertRecordUntouched(client);
        }

        // ------------------------------------------------------------------
        // Harness
        // ------------------------------------------------------------------

        private static void Run(NeoClient client, params JObject[] instructions)
        {
            var body = new JObject
            {
                ["compilerRevision"] = FunctionWithReturnType.CurrentCompilerRevision,
                ["parameters"] = new JArray(),
                ["instructions"] = new JArray(instructions.Cast<object>().ToArray()),
                ["typeInfo"] = new JObject { ["type"] = 0, ["required"] = true },
            };
            FunctionWithReturnType function = JsonConvert.DeserializeObject<FunctionWithReturnType>(body.ToString())!;
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var scope = new Dictionary<string, object?>
            {
                ["__this__"] = NSGetterEvaluator.UnwrapRow(client.ResolveValueRow("rig")!, ctx, NeoValueOwnership.Asset),
            };
            NeoScriptExecutor.Execute(client, function, scope, ctx);
        }

        private static void AssertRecordUntouched(NeoClient client)
        {
            foreach (NeoValueOwnership ownership in new[] { NeoValueOwnership.Session, NeoValueOwnership.Save })
            {
                Assert.IsFalse(client.HasWritableValue(ownership, "child"), $"The static record is never cloned into {ownership}.");
                Assert.IsFalse(client.HasWritableValue(ownership, "rig"));
            }
        }

        private static T Session<T>(NeoClient client, string id) where T : MemberValue =>
            Writable<T>(client, NeoValueOwnership.Session, id);

        private static T Writable<T>(NeoClient client, NeoValueOwnership ownership, string id) where T : MemberValue
        {
            Assert.IsTrue(client.TryGetValue(ownership, id, out MemberValue? row), $"'{id}' has a {ownership} row.");
            Assert.IsTrue(client.HasWritableValue(ownership, id), $"'{id}' is written to {ownership}.");
            Assert.IsInstanceOf<T>(row);
            return (T)row!;
        }

        private static int WidthOf(NeoClient client, NeoValueOwnership ownership, string boxId)
        {
            var box = Writable<ObjectMemberValue>(client, ownership, boxId);
            Assert.IsTrue(client.TryGetValue(ownership, box.value!["Width"], out MemberValue? width));
            return Convert.ToInt32(((NumberMemberValue)width!).value);
        }

        private static int BoxRows(NeoClient client, NeoValueOwnership ownership) =>
            (ownership == NeoValueOwnership.Session ? client.sessionValues : client.saveValues)
                .Values.Count(row => row is ObjectMemberValue { classId: "box" } && !row.IsRemoved);

        private static JObject This() => Variable("__this__");

        private static JObject Child(JObject receiver) => Key(receiver, "Child", "child-member");

        private static JObject ShapeOf(JObject receiver) => Key(Child(receiver), "Shape", "shape-member");

        private static JObject BoxesOf(JObject receiver) => Key(Child(receiver), "Boxes", "boxes-member");

        private static JObject Variable(string id) => new() { ["type"] = "variable", ["variableId"] = id };

        private static JObject Key(JObject receiver, string key, string memberId) => new()
        {
            ["type"] = "keyOf",
            ["memberId"] = memberId,
            ["keyOf"] = new JObject { ["pointer"] = receiver, ["key"] = String(key) },
        };

        private static JObject Key(JObject receiver, JObject key) => new()
        {
            ["type"] = "keyOf",
            ["keyOf"] = new JObject { ["pointer"] = receiver, ["key"] = key },
        };

        private static JObject Value(JObject typeInfo, JToken? value) => new()
        {
            ["type"] = "value",
            ["value"] = new JObject { ["typeInfo"] = typeInfo, ["value"] = value ?? JValue.CreateNull() },
        };

        private static JObject String(string value) => Value(new JObject { ["type"] = 3, ["required"] = true }, value);

        private static JObject Int(int value) => Value(IntType(), value);

        private static JObject Null(JObject typeInfo) => Value(typeInfo, null);

        private static JObject IntType() => new() { ["type"] = 2, ["required"] = true };

        private static JObject BoxType(bool required) => new() { ["type"] = 7, ["required"] = required, ["classId"] = "box" };

        private static JObject BoxListType() => new()
        {
            ["type"] = 6,
            ["required"] = true,
            ["entryTypeInfo"] = BoxType(required: true),
            ["listMemberId"] = "boxes-member",
        };

        private static JObject Target(JObject pointer, JObject typeInfo, string writability) => new()
        {
            ["pointer"] = pointer,
            ["typeInfo"] = typeInfo,
            ["writability"] = writability,
        };

        private static JObject Assign(JObject target, JObject typeInfo, JObject value, string writability = "session") => new()
        {
            ["type"] = "assign",
            ["target"] = Target(target, typeInfo, writability),
            ["operator"] = "=",
            ["pointer"] = value,
        };

        private static JObject AssignShape(JObject value) =>
            Assign(ShapeOf(This()), BoxType(required: false), value);

        private static JObject Local(string id, JObject value) => new()
        {
            ["type"] = "variable",
            ["variable"] = new JObject { ["id"] = id, ["typeInfo"] = BoxType(required: true), ["pointer"] = value },
        };

        private static JObject BoxesCall(string mutation, JObject argument) => new()
        {
            ["type"] = "collectionCall",
            ["target"] = Target(BoxesOf(This()), BoxListType(), "session"),
            ["mutation"] = mutation,
            ["args"] = new JArray(argument),
            ["reference"] = true,
        };

        // new ShapeBox { Width = width }
        private static JObject NewBox(int width) => new()
        {
            ["type"] = "objectInitializer",
            ["receiver"] = new JObject
            {
                ["id"] = "$initialized_0",
                ["typeInfo"] = BoxType(required: true),
                ["pointer"] = new JObject
                {
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["type"] = "classConstructor",
                        ["info"] = new JObject { ["schemaClassInfo"] = BoxType(required: true), ["fields"] = new JArray() },
                    },
                },
            },
            ["assignments"] = new JArray(
                Assign(Key(Variable("$initialized_0"), "Width", "width-member"), IntType(), Int(width))),
        };

        /// <summary>
        /// A static <c>ShapeRig</c> record whose <c>Child</c> authors a
        /// Session <c>Shape</c>, a Save <c>Kept</c>, and Session collections.
        /// <c>Missing</c> is declared but not authored.
        /// </summary>
        private static NeoClient BuildClient()
        {
            var data = new ProjectData
            {
                project = new Project
                {
                    id = "static-leaf-shadow",
                    _id = "static-leaf-shadow",
                    name = "Static leaf shadow",
                    rootAssetsMemberId = "assets-root",
                    rootSaveFileMemberId = "save-root",
                    rootSessionMemberId = "session-root",
                },
                classes = new()
                {
                    ["assets"] = new NeoSchemaClass { id = "assets", name = "Assets", schema = new() { ["Rig"] = "rig-member" } },
                    ["empty"] = new NeoSchemaClass { id = "empty", name = "Empty", schema = new() },
                    ["rig"] = new NeoSchemaClass { id = "rig", name = "ShapeRig", schema = new() { ["Child"] = "child-member" } },
                    ["holder"] = new NeoSchemaClass
                    {
                        id = "holder",
                        name = "ShapeHolder",
                        schema = new()
                        {
                            ["Shape"] = "shape-member",
                            ["Kept"] = "kept-member",
                            ["Missing"] = "missing-member",
                            ["Boxes"] = "boxes-member",
                            ["ByName"] = "by-name-member",
                        },
                    },
                    ["box"] = new NeoSchemaClass { id = "box", name = "ShapeBox", schema = new() { ["Width"] = "width-member" } },
                },
                members = new()
                {
                    ["assets-root"] = Root("assets-root", "assets", "assets", NeoMemberStorage.Immutable),
                    ["save-root"] = Root("save-root", "save", "empty", NeoMemberStorage.Save),
                    ["session-root"] = Root("session-root", "session", "empty", NeoMemberStorage.Session),
                    ["rig-member"] = ClassOf("rig-member", "Rig", "rig", NeoMemberRequirementKind.Required, null),
                    ["child-member"] = ClassOf("child-member", "Child", "holder", NeoMemberRequirementKind.Required, null),
                    ["shape-member"] = ClassOf("shape-member", "Shape", "box", NeoMemberRequirementKind.Optional, NeoMemberStorage.Session),
                    ["kept-member"] = ClassOf("kept-member", "Kept", "box", NeoMemberRequirementKind.Optional, NeoMemberStorage.Save),
                    ["missing-member"] = ClassOf("missing-member", "Missing", "box", NeoMemberRequirementKind.Optional, NeoMemberStorage.Session),
                    ["box-entry"] = ClassOf("box-entry", "ShapeBox", "box", NeoMemberRequirementKind.Required, null),
                    ["boxes-member"] = new ListMember
                    {
                        id = "boxes-member",
                        name = "Boxes",
                        kind = MemberKind.List,
                        entryMemberId = "box-entry",
                        Requirement = NeoMemberRequirementKind.Required,
                        Storage = NeoMemberStorage.Session,
                    },
                    ["by-name-member"] = new DictionaryMember
                    {
                        id = "by-name-member",
                        name = "ByName",
                        kind = MemberKind.Dictionary,
                        entryMemberId = "box-entry",
                        KeyKind = NeoDictionaryKeyKind.String,
                        Requirement = NeoMemberRequirementKind.Required,
                        Storage = NeoMemberStorage.Session,
                    },
                    ["width-member"] = new IntMember
                    {
                        id = "width-member",
                        name = "Width",
                        kind = MemberKind.Int,
                        // Optional, so a constructor need not pass it.
                        Requirement = NeoMemberRequirementKind.Optional,
                    },
                },
                values = new()
                {
                    ["assets"] = new ObjectMemberValue { id = "assets", classId = "assets", value = new() { ["Rig"] = "rig" } },
                    ["save"] = new ObjectMemberValue { id = "save", classId = "empty", value = new() },
                    ["session"] = new ObjectMemberValue { id = "session", classId = "empty", value = new() },
                    ["rig"] = new ObjectMemberValue { id = "rig", classId = "rig", value = new() { ["Child"] = "child" } },
                    ["child"] = new ObjectMemberValue
                    {
                        id = "child",
                        classId = "holder",
                        value = new()
                        {
                            ["Shape"] = "shape-leaf",
                            ["Kept"] = "kept-leaf",
                            ["Boxes"] = "boxes-leaf",
                            ["ByName"] = "by-name-leaf",
                        },
                    },
                    ["shape-leaf"] = new NullMemberValue { id = "shape-leaf", createdAt = "2026-01-01T00:00:00.000Z" },
                    ["kept-leaf"] = new NullMemberValue { id = "kept-leaf" },
                    ["boxes-leaf"] = new ArrayMemberValue { id = "boxes-leaf", value = new[] { "box-0" } },
                    ["box-0"] = new ObjectMemberValue { id = "box-0", classId = "box", value = new() { ["Width"] = "box-0-width" } },
                    ["box-0-width"] = new NumberMemberValue { id = "box-0-width", value = 1 },
                    ["by-name-leaf"] = new ObjectMemberValue { id = "by-name-leaf", value = new() },
                },
                enums = new(),
            };
            return NeoTestSaveStack.ClientFromSchema(data);
        }

        private static ClassMember Root(string id, string valueId, string classId, NeoMemberStorage storage) => new()
        {
            id = id,
            name = id,
            kind = MemberKind.Class,
            valueId = valueId,
            classId = classId,
            Storage = storage,
            Requirement = NeoMemberRequirementKind.Required,
        };

        private static ClassMember ClassOf(
            string id,
            string name,
            string classId,
            NeoMemberRequirementKind requirement,
            NeoMemberStorage? storage)
        {
            var member = new ClassMember
            {
                id = id,
                name = name,
                kind = MemberKind.Class,
                classId = classId,
                Requirement = requirement,
            };
            if (storage is NeoMemberStorage stamped)
                member.Storage = stamped;
            return member;
        }
    }
}
