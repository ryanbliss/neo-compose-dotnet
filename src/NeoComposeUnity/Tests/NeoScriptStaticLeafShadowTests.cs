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

        // One execution: ShapeBox Make() => new ShapeBox { Width = 7 };
        // A later one, from another C# entry: this.Child.Shape = s; s.Width = 9;
        [Test]
        public void ObjectBuiltInOneContextWritesTheLeafFromAnother()
        {
            using NeoClient client = BuildClient();
            object? box = Make(client, 7);
            Assert.IsInstanceOf<NeoScriptObject>(box, "The constructed value stays detached on its way out.");

            Run(client, Locals(("s", box)),
                AssignShape(Variable("s")),
                Assign(Key(Variable("s"), "Width", "width-member"), IntType(), Int(9)));

            Assert.AreEqual(9, WidthOf(client, NeoValueOwnership.Session, "shape-leaf"));
            Assert.AreEqual("shape-leaf", ((NeoScriptObject)box!).attachedId, "The object names the leaf for good.");
            Assert.AreEqual(1, BoxRows(client, NeoValueOwnership.Session));
            AssertRecordUntouched(client);
        }

        // As above, with the object attached before the assignment and an
        // unrelated execution between the assignment and the write. All
        // three run in one context, so only the object's own id can carry
        // the move across executions.
        [Test]
        public void ObjectWritesTheLeafAfterAnUnrelatedExecution()
        {
            using NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            object? box = Make(client, 7);
            Assert.IsNotNull(((NeoScriptObject)box!).valueId, "The object is attached before the assignment.");

            Run(client, ctx, Locals(("s", box)), AssignShape(Variable("s")));
            Run(client, ctx, null, Local("other", NewBox(3)));
            Run(client, ctx, Locals(("s", box)), Assign(Key(Variable("s"), "Width", "width-member"), IntType(), Int(9)));

            Assert.AreEqual(9, WidthOf(client, NeoValueOwnership.Session, "shape-leaf"));
            Assert.AreEqual(1, BoxRows(client, NeoValueOwnership.Session), "No orphaned source row.");
            AssertRecordUntouched(client);
        }

        // C#: var s = rig.Make(); rig.Assign(s); s.Width = 9;
        // where Assign runs this.Child.Shape = s;
        [Test]
        public void PendingCSharpViewWritesTheLeafAfterTheAssignment()
        {
            using NeoClient client = BuildClient();
            TestBox view = NeoGeneratedTypesSupport.ReadRequiredNSPropertyClass(
                client, Make(client, 7), true, null, TestBox.CreateWritable, TestBox.CreateDetached);
            Assert.IsNotNull(view.PendingValue, "The view starts over the detached temporary.");

            // An NSFunction normalizes its argument as the marshaller does.
            Run(client,
                ctx => new Dictionary<string, object?>
                {
                    ["s"] = NeoScriptValueMarshaller.Normalize(client, NeoValueOwnership.Session, view, BoxTypeInfo, ctx, "s"),
                },
                AssignShape(Variable("s")));
            view.Width = 9;

            Assert.IsFalse(view.IsDisposed);
            Assert.AreEqual("shape-leaf", view.valueId);
            Assert.AreEqual(9, view.Width);
            Assert.AreEqual(9, WidthOf(client, NeoValueOwnership.Session, "shape-leaf"));
            Assert.AreEqual(1, BoxRows(client, NeoValueOwnership.Session), "No Session box row is left behind.");
            AssertRecordUntouched(client);
        }

        // C#: var s = rig.Make(); string id = s.valueId; ... rig.Assign(s); s.Width = 9;
        // where C# holds the node-backed view of the attached Session root.
        [Test]
        public void RegisteredCSharpViewOfTheSourceRowWritesTheLeaf()
        {
            using NeoClient client = BuildClient();
            var box = (NeoScriptObject)Make(client, 7)!;
            string sourceId = box.valueId!;
            TestBox view = NeoGeneratedTypesSupport.ReadRequiredNSPropertyClass(
                client, box, true, null, TestBox.CreateWritable, TestBox.CreateDetached);
            Assert.IsNull(view.PendingValue, "The view reads its own node, not the temporary.");
            Assert.IsNull(box.view, "Only the source row's registry entry reaches the view.");
            Assert.AreEqual(sourceId, view.valueId);

            Run(client,
                ctx => new Dictionary<string, object?>
                {
                    ["s"] = NeoScriptValueMarshaller.Normalize(client, NeoValueOwnership.Session, view, BoxTypeInfo, ctx, "s"),
                },
                AssignShape(Variable("s")));
            view.Width = 9;

            Assert.IsFalse(view.IsDisposed);
            Assert.AreEqual("shape-leaf", view.valueId);
            Assert.AreEqual(9, view.Width);
            Assert.AreEqual(9, WidthOf(client, NeoValueOwnership.Session, "shape-leaf"));
            Assert.AreEqual(1, BoxRows(client, NeoValueOwnership.Session), "No Session box row is left behind.");
            AssertRecordUntouched(client);
        }

        // ------------------------------------------------------------------
        // Harness
        // ------------------------------------------------------------------

        [TestCase(NeoValueOwnership.Session, false)]
        [TestCase(NeoValueOwnership.Save, false)]
        [TestCase(NeoValueOwnership.Session, true)]
        [TestCase(NeoValueOwnership.Save, true)]
        public void ShadowImportRemapsRootAndDescendantListenerReceivers(NeoValueOwnership destination, bool runtimeOverride)
        {
            using NeoClient client = BuildClient(data =>
            {
                data.classes["box"].schema["Nested"] = "nested-box-member";
                data.members["nested-box-member"] = ClassOf("nested-box-member", "Nested", "nested-box", NeoMemberRequirementKind.Optional, null);
                data.classes["nested-box"] = new NeoSchemaClass
                {
                    id = "nested-box",
                    name = "NestedBox",
                    schema = new() { ["Width"] = "width-member" },
                };
                data.members["width-member"].Requirement = NeoMemberRequirementKind.Required;
                foreach (string handler in new[] { "copied-handler", "runtime-handler" })
                {
                    data.classes["box"].schema[handler] = handler;
                    data.classes["nested-box"].schema[handler] = handler;
                    data.members[handler] = new FunctionMember
                    {
                        id = handler,
                        name = handler,
                        kind = MemberKind.Function,
                        returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                        argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "next", type = MemberKind.Int, required = true } },
                    };
                }
            });
            var heard = new List<(string handler, string receiverWidth, double value)>();
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["copied-handler"] = (_, receiver, args) => Hear("copied-handler", receiver, args),
                ["runtime-handler"] = (_, receiver, args) => Hear("runtime-handler", receiver, args),
            });

            object? Hear(string handler, object? receiver, IReadOnlyList<object?> args)
            {
                heard.Add((handler, (string)((IDictionary<string, object?>)receiver!)["Width"]!, Convert.ToDouble(args[0])));
                return null;
            }

            var defaults = new NeoChangeListenerMap();
            foreach (string owner in new[] { "source-box", "source-nested" })
                defaults[owner] = new Dictionary<string, NeoDelegateValue[]>
                {
                    ["width-member"] = new[] { new NeoDelegateValue { memberId = "copied-handler", valueId = "source-box" } },
                };
            var seed = new NeoWritePlan(client);
            seed.Set(NeoValueOwnership.Session, new ObjectMemberValue
            {
                id = "source-box",
                classId = "box",
                value = new() { ["Width"] = "source-width", ["Nested"] = "source-nested" },
                copiedChangeListeners = defaults,
            });
            seed.Set(NeoValueOwnership.Session, new ObjectMemberValue
            {
                id = "source-nested",
                classId = "nested-box",
                value = new() { ["Width"] = "nested-width" },
            });
            seed.Set(NeoValueOwnership.Session, new NumberMemberValue { id = "source-width", value = 1 });
            seed.Set(NeoValueOwnership.Session, new NumberMemberValue { id = "nested-width", value = 2 });
            seed.Commit();
            if (runtimeOverride)
                foreach (string owner in new[] { "source-box", "source-nested" })
                    client.EditMemberChangeListener(owner, NeoValueOwnership.Session, "width-member",
                        new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                        new NeoDelegateValue { memberId = "runtime-handler", valueId = "source-box" }, true);

            string leafId = destination == NeoValueOwnership.Save ? "kept-leaf" : "shape-leaf";
            string leafMemberId = destination == NeoValueOwnership.Save ? "kept-member" : "shape-member";
            heard.Clear();
            var import = new NeoWritePlan(client);
            Assert.That(client.TryGetMember(leafMemberId, out Member? leafMember), Is.True);
            client.StageShadowImport(import, destination, "source-box", client.ResolveValueRow(leafId)!, leafMember!, out _);
            import.Commit();

            ObjectMemberValue root = Writable<ObjectMemberValue>(client, destination, leafId);
            string nestedId = root.value!["Nested"];
            Assert.That(root.copiedChangeListeners!.Keys, Is.EquivalentTo(new[] { leafId, nestedId }));
            foreach (var entry in root.copiedChangeListeners.Values)
                Assert.That(entry["width-member"][0].valueId, Is.EqualTo(leafId), "Both self and descendant receivers follow the renamed root.");
            Assert.That(client.HasWritableValue(NeoValueOwnership.Session, "source-box"), Is.False, "The old parentless owner is released.");
            Assert.That(client.HasWritableValue(NeoValueOwnership.Save, "source-box"), Is.False);
            Assert.That(heard, Is.Empty, "Importing metadata is initialization, not a gameplay write.");

            using var rootView = new NeoMemberClassWritable(client, leafMemberId, leafId, destination);
            using var nestedView = new NeoMemberClassWritable(client, "nested-box-member", nestedId, destination);
            rootView.Get<NeoMemberIntWritable>("Width").Set(7);
            nestedView.Get<NeoMemberIntWritable>("Width").Set(8);
            Assert.That(heard.Count, Is.EqualTo(runtimeOverride ? 4 : 2));
            Assert.That(heard.Select(item => item.receiverWidth), Is.All.EqualTo(root.value["Width"]));
            Assert.That(heard.Where(item => item.handler == "copied-handler").Select(item => item.value), Is.EqualTo(new[] { 7d, 8d }));
            string saved = client.SerializeSaveData();
            Assert.That(saved, Does.Not.Contain("source-box"), "No old owner or receiver id survives persistence.");
            Assert.That(saved, Does.Not.Contain("runtime-handler"), "Session overrides stay transient even when adopted by a Save leaf.");

            if (runtimeOverride)
            {
                foreach (string owner in new[] { leafId, nestedId })
                    client.EditMemberChangeListener(owner, destination, "width-member",
                        new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                        new NeoDelegateValue { memberId = "runtime-handler", valueId = leafId }, false);
                heard.Clear();
                rootView.Get<NeoMemberIntWritable>("Width").Set(9);
                nestedView.Get<NeoMemberIntWritable>("Width").Set(10);
                Assert.That(heard.Select(item => item.handler), Is.EqualTo(new[] { "copied-handler", "copied-handler" }), "Runtime entries can be removed by their new identities.");
            }
            AssertRecordUntouched(client);
        }

        private static readonly ClassTypeInfo BoxTypeInfo = new() { type = MemberKind.Class, required = true, classId = "box" };

        [TestCase(false)]
        [TestCase(true)]
        public void ShadowImportKeepsRepeatedVirtualChildOccurrencesSeparate(bool materializedParents)
        {
            using NeoClient client = BuildClient(data =>
            {
                data.members["width-member"].Requirement = NeoMemberRequirementKind.Required;
                ((IntMember)data.members["width-member"]).defaultValue = new NumberMemberValueBase { value = 0 };
                data.classes["shadow-frame"] = new NeoSchemaClass { id = "shadow-frame", name = "ShadowFrame", schema = new() { ["Child"] = "frame-child" } };
                data.classes["frame-child-class"] = new NeoSchemaClass { id = "frame-child-class", name = "FrameChild", schema = new() { ["Width"] = "width-member" } };
                var child = ClassOf("frame-child", "Child", "frame-child-class", NeoMemberRequirementKind.Required, null);
                child.defaultValue = new ObjectMemberValueBase { classId = "frame-child-class", value = new() };
                data.members[child.id] = child;
                data.values["shared-frame-child-source"] = new ObjectMemberValue
                {
                    id = "shared-frame-child-source",
                    classId = "frame-child-class",
                    value = new(),
                    instanceConstructorId = null,
                    constructorArgs = new(),
                };
                foreach (string name in new[] { "NestedA", "NestedB" })
                {
                    var frame = ClassOf("frame-" + name, name, "shadow-frame", NeoMemberRequirementKind.Required, null);
                    frame.defaultValue = new ObjectMemberValueBase
                    {
                        classId = "shadow-frame",
                        value = new() { ["Child"] = "shared-frame-child-source" },
                    };
                    data.members[frame.id] = frame;
                    data.classes["box"].schema[name] = frame.id;
                    string handler = "handler-" + name;
                    data.classes["frame-child-class"].schema[handler] = handler;
                    data.members[handler] = new FunctionMember
                    {
                        id = handler,
                        name = handler,
                        kind = MemberKind.Function,
                        returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                        argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "next", type = MemberKind.Int, required = true } },
                    };
                }
            });
            var heard = new List<(string frame, double value)>();
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["handler-NestedA"] = (_, _, args) => { heard.Add(("A", Convert.ToDouble(args[0]))); return null; },
                ["handler-NestedB"] = (_, _, args) => { heard.Add(("B", Convert.ToDouble(args[0]))); return null; },
            });
            const string sourceId = "nested-frame-source";
            var seed = new NeoWritePlan(client);
            seed.Set(NeoValueOwnership.Session, new ObjectMemberValue
            {
                id = sourceId,
                classId = "box",
                value = new() { ["Width"] = "nested-frame-root-width" },
                instanceConstructorId = null,
                constructorArgs = new(),
            });
            if (materializedParents)
            {
                var root = (ObjectMemberValue)seed.Resolve(NeoValueOwnership.Session, sourceId)!;
                foreach (string name in new[] { "NestedA", "NestedB" })
                {
                    string frameId = "physical-" + name;
                    root.value![name] = frameId;
                    seed.Set(NeoValueOwnership.Session, new ObjectMemberValue
                    {
                        id = frameId,
                        classId = "shadow-frame",
                        value = new(),
                        instanceConstructorId = null,
                        constructorArgs = new(),
                    });
                }
            }
            seed.Set(NeoValueOwnership.Session, new NumberMemberValue { id = "nested-frame-root-width", value = 1 });
            seed.Commit();
            using var source = new NeoMemberClassWritable(client, "shape-member", sourceId, NeoValueOwnership.Session);
            string[] names = { "NestedA", "NestedB" };
            var oldFrames = names.Select(name => source.Get<NeoMemberClassWritable>(name)).ToArray();
            var oldChildren = oldFrames.Select(frame => frame.Get<NeoMemberClassWritable>("Child")).ToArray();
            for (int index = 0; index < names.Length; index++)
            {
                Assert.That((materializedParents ? oldFrames[index] : oldChildren[index]).value!.hasInstanceConstructorId, Is.True, "Each construction root retains its provenance.");
                Assert.That(client.HasWritableValue(NeoValueOwnership.Session, oldFrames[index].value!.id), Is.EqualTo(materializedParents));
                Assert.That(client.HasWritableValue(NeoValueOwnership.Session, oldChildren[index].value!.id), Is.False);
                client.EditMemberChangeListener(oldChildren[index].value!.id, NeoValueOwnership.Session, "width-member",
                    new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                    new NeoDelegateValue { memberId = "handler-" + names[index], valueId = null }, true);
            }
            string[] oldFrameIds = oldFrames.Select(frame => frame.value!.id).ToArray();
            string[] oldChildIds = oldChildren.Select(child => child.value!.id).ToArray();
            var import = new NeoWritePlan(client);
            Assert.That(client.TryGetMember("kept-member", out Member? member), Is.True);
            client.StageShadowImport(import, NeoValueOwnership.Save, sourceId, client.ResolveValueRow("kept-leaf")!, member!, out _);
            import.Commit();
            using var target = new NeoMemberClassWritable(client, "kept-member", "kept-leaf", NeoValueOwnership.Save);
            var newFrames = names.Select(name => target.Get<NeoMemberClassWritable>(name)).ToArray();
            var newChildren = newFrames.Select(frame => frame.Get<NeoMemberClassWritable>("Child")).ToArray();
            for (int index = 0; index < names.Length; index++)
            {
                Assert.That(newFrames[index].value!.id == oldFrameIds[index], Is.EqualTo(materializedParents));
                Assert.That(newChildren[index].value!.id == oldChildIds[index], Is.EqualTo(materializedParents));
                Assert.That(client.HasWritableValue(NeoValueOwnership.Save, newFrames[index].value!.id), Is.EqualTo(materializedParents), "Moving wiring preserves parent materialization.");
                Assert.That(client.HasWritableValue(NeoValueOwnership.Save, newChildren[index].value!.id), Is.False);
            }
            Assert.That(heard, Is.Empty, "Constructor replay and listener transfer do not dispatch callbacks.");
            newChildren[0].Get<NeoMemberIntWritable>("Width").Set(7);
            newChildren[1].Get<NeoMemberIntWritable>("Width").Set(8);
            CollectionAssert.AreEqual(new[] { ("A", 7d), ("B", 8d) }, heard, "Repeated source children retain distinct occurrence wiring.");
            Assert.That(client.SerializeSaveData(), Does.Not.Contain("handler-Nested"), "The transferred registrations keep their Session lifetime.");
        }

        [TestCase(NeoValueOwnership.Session, false)]
        [TestCase(NeoValueOwnership.Save, false)]
        [TestCase(NeoValueOwnership.Session, true)]
        [TestCase(NeoValueOwnership.Save, true)]
        public void ShadowImportReprojectsVirtualDescendantWiring(NeoValueOwnership destination, bool runtimeOverride)
        {
            using NeoClient client = BuildClient(data =>
            {
                data.classes["box"].schema["Nested"] = "nested-box-member";
                data.classes["nested-box"] = new NeoSchemaClass
                {
                    id = "nested-box",
                    name = "NestedBox",
                    schema = new() { ["Width"] = "width-member", ["Changed"] = "copied-handler", ["RuntimeChanged"] = "runtime-handler" },
                };
                data.members["width-member"].Requirement = NeoMemberRequirementKind.Required;
                ((IntMember)data.members["width-member"]).defaultValue = new NumberMemberValueBase { value = 0 };
                var nested = ClassOf("nested-box-member", "Nested", "nested-box", NeoMemberRequirementKind.Required, null);
                nested.defaultValue = new ObjectMemberValueBase
                {
                    classId = "nested-box",
                    value = new() { ["Width"] = "virtual-width-source" },
                    changeListeners = new NeoChangeListenerMap
                    {
                        [NeoClient.DerivedMemberValueId("nested-box-member")] = new Dictionary<string, NeoDelegateValue[]>
                        {
                            ["width-member"] = new[] { new NeoDelegateValue { memberId = "copied-handler", valueId = null } },
                        },
                    },
                };
                data.members["nested-box-member"] = nested;
                data.values["virtual-width-source"] = new NumberMemberValue { id = "virtual-width-source", value = 2 };
                foreach (string handler in new[] { "copied-handler", "runtime-handler" })
                    data.members[handler] = new FunctionMember
                    {
                        id = handler,
                        name = handler,
                        kind = MemberKind.Function,
                        returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                        argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "next", type = MemberKind.Int, required = true } },
                    };
            });
            var heard = new List<(string handler, double value)>();
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["copied-handler"] = (_, _, args) => { heard.Add(("copied", Convert.ToDouble(args[0]))); return null; },
                ["runtime-handler"] = (_, _, args) => { heard.Add(("runtime", Convert.ToDouble(args[0]))); return null; },
            });
            const string sourceId = "virtual-source-box";
            var seed = new NeoWritePlan(client);
            seed.Set(NeoValueOwnership.Session, new ObjectMemberValue
            {
                id = sourceId,
                classId = "box",
                value = new() { ["Width"] = "virtual-root-width" },
                instanceConstructorId = null,
                constructorArgs = new(),
            });
            seed.Set(NeoValueOwnership.Session, new NumberMemberValue { id = "virtual-root-width", value = 1 });
            seed.Commit();
            using var source = new NeoMemberClassWritable(client, "shape-member", sourceId, NeoValueOwnership.Session);
            NeoMemberClassWritable oldNested = source.Get<NeoMemberClassWritable>("Nested");
            string oldNestedId = oldNested.value!.id;
            Assert.That(oldNestedId, Does.Not.StartWith("__neo_default:"), "The fixture uses a P75 occurrence, not the declaration template wrapper.");
            Assert.That(client.HasWritableValue(NeoValueOwnership.Session, oldNestedId), Is.False, "The source descendant is a virtual default.");
            if (runtimeOverride)
                client.EditMemberChangeListener(oldNestedId, NeoValueOwnership.Session, "width-member",
                    new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                    new NeoDelegateValue { memberId = "runtime-handler", valueId = null }, true);
            string leafId = destination == NeoValueOwnership.Save ? "kept-leaf" : "shape-leaf";
            string memberId = destination == NeoValueOwnership.Save ? "kept-member" : "shape-member";
            var import = new NeoWritePlan(client);
            Assert.That(client.TryGetMember(memberId, out Member? member), Is.True);
            client.StageShadowImport(import, destination, sourceId, client.ResolveValueRow(leafId)!, member!, out _);
            import.Commit();
            using var target = new NeoMemberClassWritable(client, memberId, leafId, destination);
            NeoMemberClassWritable newNested = target.Get<NeoMemberClassWritable>("Nested");
            Assert.That(newNested.value!.id, Is.Not.EqualTo(oldNestedId), "A virtual descendant follows the replacement root's namespace.");
            Assert.That(heard, Is.Empty, "Replaying virtual defaults and importing their graph are initialization.");
            newNested.Get<NeoMemberIntWritable>("Width").Set(7);
            CollectionAssert.AreEqual(runtimeOverride ? new[] { ("copied", 7d), ("runtime", 7d) } : new[] { ("copied", 7d) }, heard,
                $"Wiring follows virtual descendant {oldNestedId} to {newNested.value!.id} beneath {leafId}.");
            Assert.That(client.SerializeSaveData(), Does.Not.Contain("runtime-handler"), "A virtual Session registration remains temporary after Save adoption.");
            Assert.That(client.HasWritableValue(NeoValueOwnership.Session, sourceId), Is.False);
            AssertRecordUntouched(client);
        }

        [Test]
        public void ShadowImportRemapsExternalSessionObserverWithoutRenamingSameIdSaveReceiver()
        {
            using NeoClient client = BuildClient(data =>
            {
                data.members["width-member"].Requirement = NeoMemberRequirementKind.Required;
                foreach (string handler in new[] { "saved-observer-handler", "session-observer-handler", "inherited-observer-handler", "saved-self-handler" })
                {
                    data.classes["box"].schema[handler] = handler;
                    data.members[handler] = new FunctionMember
                    {
                        id = handler,
                        name = handler,
                        kind = MemberKind.Function,
                        returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                        argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "next", type = MemberKind.Int, required = true } },
                    };
                }
            });
            var heard = new List<(string handler, string receiverWidth, double value)>();
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["saved-observer-handler"] = (_, receiver, args) => Hear("save", receiver, args),
                ["session-observer-handler"] = (_, receiver, args) => Hear("session", receiver, args),
                ["inherited-observer-handler"] = (_, receiver, args) => Hear("inherited", receiver, args),
                ["saved-self-handler"] = (_, receiver, args) => Hear("saved-self", receiver, args),
            });
            object? Hear(string handler, object? receiver, object?[] args)
            {
                heard.Add((handler, (string)((IDictionary<string, object?>)receiver!)["Width"]!, Convert.ToDouble(args[0])));
                return null;
            }

            void AddBox(NeoWritePlan plan, NeoValueOwnership scope, string id, string widthId, NeoChangeListenerMap? defaults = null)
            {
                plan.Set(scope, new ObjectMemberValue { id = id, classId = "box", value = new() { ["Width"] = widthId }, copiedChangeListeners = defaults });
                plan.Set(scope, new NumberMemberValue { id = widthId, value = 1 });
            }
            var savedSeed = new NeoWritePlan(client);
            AddBox(savedSeed, NeoValueOwnership.Save, "source-box", "saved-source-width", new NeoChangeListenerMap
            {
                ["source-box"] = new Dictionary<string, NeoDelegateValue[]>
                {
                    ["width-member"] = new[] { new NeoDelegateValue { memberId = "saved-self-handler", valueId = null } },
                },
            });
            AddBox(savedSeed, NeoValueOwnership.Save, "saved-observer", "saved-observer-width");
            savedSeed.Commit();
            var observed = new PrimitiveTypeInfo { type = MemberKind.Int, required = true };
            client.EditMemberChangeListener("saved-observer", NeoValueOwnership.Save, "width-member", observed,
                new NeoDelegateValue { memberId = "saved-observer-handler", valueId = "source-box" }, true);

            var sessionSeed = new NeoWritePlan(client);
            AddBox(sessionSeed, NeoValueOwnership.Session, "source-box", "session-source-width");
            AddBox(sessionSeed, NeoValueOwnership.Session, "session-observer", "session-observer-width", new NeoChangeListenerMap
            {
                ["session-observer"] = new Dictionary<string, NeoDelegateValue[]>
                {
                    ["width-member"] = new[] { new NeoDelegateValue { memberId = "inherited-observer-handler", valueId = "source-box" } },
                },
            });
            sessionSeed.Commit();
            client.EditMemberChangeListener("session-observer", NeoValueOwnership.Session, "width-member", observed,
                new NeoDelegateValue { memberId = "session-observer-handler", valueId = "source-box" }, true);
            var import = new NeoWritePlan(client);
            Assert.That(client.TryGetMember("shape-member", out Member? leafMember), Is.True);
            client.StageShadowImport(import, NeoValueOwnership.Session, "source-box", client.ResolveValueRow("shape-leaf")!, leafMember!, out _);
            import.Commit();

            var savedMap = JObject.Parse(client.SerializeSaveData())["changeListeners"]!;
            Assert.That(savedMap["saved-observer"]!["saved-observer"]!["width-member"]![0]!["valueId"]!.Value<string>(), Is.EqualTo("source-box"),
                "A durable receiver in the opposite store has a distinct logical identity despite the equal id.");
            Assert.That(savedMap.ToString(), Does.Not.Contain("shape-leaf"));
            Assert.That(client.HasWritableValue(NeoValueOwnership.Session, "source-box"), Is.False);
            Assert.That(client.HasWritableValue(NeoValueOwnership.Save, "source-box"), Is.True);
            Assert.That(Writable<ObjectMemberValue>(client, NeoValueOwnership.Save, "source-box").copiedChangeListeners!.Keys,
                Is.EquivalentTo(new[] { "source-box" }), "A copied baseline owner key is scoped to its carrier, not the renamed Session root.");

            heard.Clear();
            using var savedObserver = new NeoMemberClassWritable(client, "kept-member", "saved-observer", NeoValueOwnership.Save);
            using var sessionObserver = new NeoMemberClassWritable(client, "shape-member", "session-observer", NeoValueOwnership.Session);
            savedObserver.Get<NeoMemberIntWritable>("Width").Set(3);
            sessionObserver.Get<NeoMemberIntWritable>("Width").Set(4);
            using var savedSource = new NeoMemberClassWritable(client, "kept-member", "source-box", NeoValueOwnership.Save);
            savedSource.Get<NeoMemberIntWritable>("Width").Set(5);
            CollectionAssert.AreEqual(new[] { ("save", "saved-source-width", 3d), ("inherited", "session-source-width", 4d), ("session", "session-source-width", 4d), ("saved-self", "saved-source-width", 5d) }, heard,
                "Only the external observer whose receiver moved follows the new leaf.");
        }

        private static void Run(NeoClient client, params JObject[] instructions) =>
            Run(client, null, null, instructions);

        private static void Run(
            NeoClient client,
            Func<NSGetterEvaluator.Context, Dictionary<string, object?>>? locals,
            params JObject[] instructions) =>
            Run(client, null, locals, instructions);

        private static void Run(
            NeoClient client,
            NSGetterEvaluator.Context? ctx,
            Func<NSGetterEvaluator.Context, Dictionary<string, object?>>? locals,
            params JObject[] instructions) =>
            Execute(client, ctx, locals, new JObject { ["type"] = 0, ["required"] = true }, instructions);

        /// <summary>One execution that returns <c>new ShapeBox { Width = width }</c> to C#.</summary>
        private static object? Make(NeoClient client, int width) =>
            Execute(client, null, null, BoxType(required: true), new JObject { ["type"] = "return", ["pointer"] = NewBox(width) });

        private static object? Execute(
            NeoClient client,
            NSGetterEvaluator.Context? ctx,
            Func<NSGetterEvaluator.Context, Dictionary<string, object?>>? locals,
            JObject returnType,
            params JObject[] instructions)
        {
            var body = new JObject
            {
                ["compilerRevision"] = FunctionWithReturnType.CurrentCompilerRevision,
                ["parameters"] = new JArray(),
                ["instructions"] = new JArray(instructions.Cast<object>().ToArray()),
                ["typeInfo"] = returnType,
            };
            FunctionWithReturnType function = JsonConvert.DeserializeObject<FunctionWithReturnType>(body.ToString())!;
            ctx ??= new NSGetterEvaluator.Context(client, null, null);
            var scope = locals?.Invoke(ctx) ?? new Dictionary<string, object?>();
            scope["__this__"] = NSGetterEvaluator.UnwrapRow(client.ResolveValueRow("rig")!, ctx, NeoValueOwnership.Asset);
            NeoScriptExecutionResult result = NeoScriptExecutor.Execute(client, function, scope, ctx);
            if (result.IsFailed)
                throw result.Failure!;
            return result.ReturnValue;
        }

        private static Func<NSGetterEvaluator.Context, Dictionary<string, object?>> Locals(params (string id, object? value)[] locals) =>
            _ => locals.ToDictionary(local => local.id, local => local.value);

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
        private static NeoClient BuildClient(Action<ProjectData>? configure = null)
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
            configure?.Invoke(data);
            return NeoTestSaveStack.ClientFromSchema(data);
        }

        /// <summary>The shape generated C# takes for <c>class ShapeBox</c>.</summary>
        private sealed class TestBox : NeoGeneratedClassValue
        {
            private TestBox(NeoClient client, NeoMemberClass node)
                : base(client, node, "box", false, node.ownership)
            {
            }

            private TestBox(NeoClient client, NeoDetachedValue value, bool isReadOnly)
                : base(client, value, isReadOnly)
            {
            }

            internal static TestBox CreateWritable(NeoClient client, NeoMemberClassWritable node) =>
                NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                    client,
                    node,
                    static (factoryClient, factoryNode) => new TestBox(factoryClient, factoryNode));

            internal static TestBox CreateDetached(NeoClient client, NeoDetachedValue value, bool saved) =>
                new(client, value, !saved);

            public int Width
            {
                get
                {
                    if (TryReadDetached("Width", out object? detachedValue))
                        return Convert.ToInt32(detachedValue);
                    return NeoGeneratedTypesSupport.ReadInt(node.Get<NeoMemberInt>("Width"))
                        ?? throw new InvalidOperationException("Int 'Width' has no value.");
                }
                set
                {
                    NeoGeneratedTypesSupport.SetValue(writableNode, "Width", NeoGeneratedTypesSupport.Value(value));
                }
            }
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
