// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;
using UnityEngine;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §1.3 and §14: the evaluator reads a stored box as one
    /// <see cref="NeoBox"/>, writes its boxes and <c>Type</c> values to an
    /// object slot C# reads back, and boxes a C# argument to an
    /// <c>object</c> parameter.
    /// </summary>
    public sealed class P127TypesStorageTests
    {
        private const string ProjectId = "p127-types-storage";
        private const string RootClass = "root-class";

        private static readonly TypeBinding IntBinding = TypeBinding.Closed(new TypeHead(MemberKind.Int, true));

        [TestCase(MemberKind.Sprite)]
        [TestCase(MemberKind.Audio)]
        public void SeparateStoredAssetRowsShareIdentityAndRepeatReads(MemberKind kind)
        {
            using NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            TypeBinding type = TypeBinding.Closed(new TypeHead(kind, true));
            MemberValue Row(string id, string file, int slice) => kind == MemberKind.Sprite
                ? new SpriteMemberValue { id = id, valueType = type, value = new SpriteValue { fileId = file, sliceIndex = slice } }
                : new FileMemberValue { id = id, valueType = type, value = new FileValue { fileId = file } };
            var firstRow = Row("asset-one", "same-file", 2);
            var secondRow = Row("asset-two", "same-file", 2);
            object? Read(MemberValue row) => NSGetterEvaluator.UnwrapRow(row, ctx, NeoValueOwnership.Save, Slot(client, "slot-field"));
            object? first = Read(firstRow);
            object? second = Read(secondRow);
            Assert.That(Read(firstRow), Is.SameAs(first));
            Assert.That(Read(secondRow), Is.SameAs(second));
            Assert.That(NSGetterEvaluator.ReferenceEqual(first, second, ctx), Is.True);
            Assert.That(NSGetterEvaluator.ReferenceEqual(first, Read(Row("asset-three", "other-file", 2)), ctx), Is.False);
            if (kind == MemberKind.Sprite)
                Assert.That(NSGetterEvaluator.ReferenceEqual(first, Read(Row("asset-four", "same-file", 3)), ctx), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ScriptNullWritesUseTheStoredNullError(bool existingField)
        {
            using NeoClient client = BuildClient();
            if (existingField)
                Write(client, "Name", NeoValueWritePayload.FromValue("before"));
            var ctx = new NSGetterEvaluator.Context(client, null, null, valueOwnership: NeoValueOwnership.Save);
            object? root = NSGetterEvaluator.UnwrapRow(client.save.value!, ctx, NeoValueOwnership.Save, client.save.member);
            var scope = new NeoScriptScope(new Dictionary<string, object?> { ["save"] = root });
            var target = new KeyOfPointer
            {
                type = PointerKind.KeyOf,
                keyOf = new KeyOf
                {
                    pointer = new VariablePointer { type = PointerKind.Variable, variableId = "save" },
                    key = Newtonsoft.Json.JsonConvert.DeserializeObject<Pointer>("{\"type\":\"value\",\"value\":{\"typeInfo\":{\"type\":3,\"required\":true},\"value\":\"Name\"}}")!,
                }
            };
            Pointer pointer = new OutPointer
            {
                type = PointerKind.Out,
                target = new WriteTarget
                {
                    pointer = target,
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                    writability = WritabilityKind.Save
                }
            };
            var alias = (NeoOutReference)NSGetterEvaluator.EvalPointer(pointer, scope, ctx)!;
            var error = Assert.Throws<NSGetterRuntimeError>(() => alias.Write(null))!;
            Assert.That(error.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoInvalidOperationError));
            Assert.That(error.Message, Is.EqualTo("'Root.Name' is required and cannot store null."));
            if (existingField)
                Assert.That(client.save.Get<NeoMemberString>("Name").Text, Is.EqualTo("before"));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void TupleBoxReassignmentKeepsTheExactCollectionPlacement(bool nested, bool boxedElement)
        {
            using NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            TypeBinding list = TypeBinding.Closed(new TypeHead(MemberKind.List, true), new Dictionary<string, TypeBinding> { ["0"] = IntBinding });
            TypeBinding pair = TypeBinding.Closed(new TypeHead(MemberKind.Tuple, true), new Dictionary<string, TypeBinding> { ["0"] = list, ["1"] = IntBinding });
            TypeBinding type = nested
                ? TypeBinding.Closed(new TypeHead(MemberKind.Tuple, true), new Dictionary<string, TypeBinding> { ["0"] = boxedElement ? TypeBinding.Closed(new TypeHead(MemberKind.Object, true)) : pair, ["1"] = IntBinding })
                : pair;
            var child = new object?[] { 1d };
            NeoBox Box(double sibling)
            {
                var inner = new RuntimeTuple(new object?[] { child, sibling }, (TupleTypeInfo)NeoTypeBindings.ToTypeInfo(pair));
                return NeoBox.Of(type, nested
                    ? new RuntimeTuple(new object?[] { boxedElement ? NeoBox.Of(pair, inner) : inner, 4d }, (TupleTypeInfo)NeoTypeBindings.ToTypeInfo(type))
                    : inner);
            }
            NeoBox box = Box(2d);
            StoreCollectionBox(client, ctx, box, "same-slot");
            string childId = NSGetterEvaluator.FindRowReference(child, ctx)!.valueId;
            StoreCollectionBox(client, ctx, box, "same-slot");
            var updated = (ArrayMemberValue)StoreCollectionBox(client, ctx, Box(3d), "same-slot");
            Assert.That(NSGetterEvaluator.FindRowReference(child, ctx)!.valueId, Is.EqualTo(childId));
            var innerRow = updated;
            if (nested)
            {
                Assert.That(client.TryGetValue(NeoValueOwnership.Session, updated.value![0], out ArrayMemberValue? found), Is.True);
                innerRow = found!;
            }
            Assert.That(innerRow.value![0], Is.EqualTo(childId));
            Assert.That(client.TryGetValue(NeoValueOwnership.Session, innerRow.value[1], out NumberMemberValue? scalar), Is.True);
            Assert.That(scalar!.value, Is.EqualTo(3d));
            var listMember = (ListMember)client.TypeRules.Box(list).Member!;
            var view = new NeoMemberListWritable(client, listMember, childId, NeoValueOwnership.Session);
            view.SetSerialized(0, NeoValueWritePayload.FromValue(9d));
            StoreCollectionBox(client, ctx, Box(5d), "same-slot");
            Assert.That(NSGetterEvaluator.ResolveHostCollection(NSGetterEvaluator.RuntimeCollection(child, ctx), ctx), Is.EqualTo(new object?[] { 9d }));
            MemberValue previous = StoreCollectionBox(client, ctx, NeoBox.Of(IntBinding, 7d), "other-slot");
            int count = client.sessionValues.Count;
            Assert.That(Assert.Throws<NSGetterRuntimeError>(() => StoreCollectionBox(client, ctx, box, "other-slot"))!.Message,
                Does.StartWith("Writing a boxed "));
            Assert.That(client.sessionValues.Count, Is.EqualTo(count));
            Assert.That(client.TryGetValue(NeoValueOwnership.Session, "other-slot", out MemberValue? current), Is.True);
            Assert.That(current, Is.SameAs(previous));
        }

        [Test]
        public void TupleBoxCannotMoveAnOwnedChildToAnotherElement()
        {
            using NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            TypeBinding list = TypeBinding.Closed(new TypeHead(MemberKind.List, true), new Dictionary<string, TypeBinding> { ["0"] = IntBinding });
            TypeBinding pair = TypeBinding.Closed(new TypeHead(MemberKind.Tuple, true), new Dictionary<string, TypeBinding> { ["0"] = list, ["1"] = list });
            NeoBox Box(object? first, object? second) => NeoBox.Of(pair,
                new RuntimeTuple(new[] { first, second }, (TupleTypeInfo)NeoTypeBindings.ToTypeInfo(pair)));
            var first = new object?[] { 1d };
            var second = new object?[] { 2d };
            var fresh = new object?[] { 3d };
            NeoBox box = Box(first, second);
            MemberValue previous = StoreCollectionBox(client, ctx, box, "pair-slot");
            int count = client.sessionValues.Count;
            // The first element is prepared before the second refuses a moved child.
            Assert.That(Assert.Throws<NSGetterRuntimeError>(() => StoreCollectionBox(client, ctx, Box(fresh, first), "pair-slot"))!.Message,
                Does.Contain("already owned by another slot"));
            Assert.That(client.sessionValues.Count, Is.EqualTo(count));
            Assert.That(NSGetterEvaluator.FindRowReference(fresh, ctx), Is.Null);
            Assert.That(client.TryGetValue(NeoValueOwnership.Session, "pair-slot", out MemberValue? current), Is.True);
            Assert.That(current, Is.SameAs(previous));
            StoreCollectionBox(client, ctx, box, "pair-slot");
        }

        [Test]
        public void TupleRootAdoptionRetainsAliasesAndSurvivesSerializedReplay()
        {
            string serialized;
            using (NeoClient client = BuildClient())
            {
                var ctx = new NSGetterEvaluator.Context(client, null, null, valueOwnership: NeoValueOwnership.Save);
                TypeBinding list = TypeBinding.Closed(new TypeHead(MemberKind.List, true), new Dictionary<string, TypeBinding> { ["0"] = IntBinding });
                TypeBinding tuple = TypeBinding.Closed(new TypeHead(MemberKind.Tuple, true), new Dictionary<string, TypeBinding> { ["0"] = list, ["1"] = IntBinding });
                var child = new object?[] { 1d };
                var box = NeoBox.Of(tuple, new RuntimeTuple(new object?[] { child, 2d }, (TupleTypeInfo)NeoTypeBindings.ToTypeInfo(tuple)));
                var row = (ArrayMemberValue)StoreCollectionBox(client, ctx, box, "tuple-save", NeoValueOwnership.Save);
                Write(client, "Slot", NeoValueWritePayload.FromValueReference(row.id));
                var held = (TupleMember)client.TypeRules.Box(tuple).Member!;
                Assert.That(client.TryGetMember(held.elementMemberIds[0], out JsonMember? listMember), Is.True);
                var node = new NeoMemberListWritable(client, (ListMember)listMember!, row.value![0], NeoValueOwnership.Save);
                var scope = new NeoScriptScope(new Dictionary<string, object?> { ["items"] = child });
                Pointer address = Newtonsoft.Json.JsonConvert.DeserializeObject<Pointer>("{\"type\":\"out\",\"target\":{\"pointer\":{\"type\":\"keyOf\",\"keyOf\":{\"pointer\":{\"type\":\"variable\",\"variableId\":\"items\"},\"key\":{\"type\":\"value\",\"value\":{\"typeInfo\":{\"type\":2,\"required\":true},\"value\":0}}}},\"typeInfo\":{\"type\":2,\"required\":true},\"writability\":\"runtime\"}}")!;
                ((NeoOutReference)NSGetterEvaluator.EvalPointer(address, scope, ctx)!).Write(8d);
                Assert.That(((NumberMemberValue)node[0].value!).value, Is.EqualTo(8d));
                Assert.That(NSGetterEvaluator.ResolveHostCollection(NSGetterEvaluator.RuntimeCollection(child, ctx), ctx), Is.EqualTo(new object?[] { 8d }));
                serialized = client.SerializeSaveData();
            }
            using NeoClient reopened = BuildClient(serialized);
            var replayCtx = new NSGetterEvaluator.Context(reopened, null, null);
            var restored = (NeoBox)NSGetterEvaluator.UnwrapRow(Node(reopened, "Slot").value!, replayCtx, NeoValueOwnership.Save, Slot(reopened, "slot-field"))!;
            Assert.That(((RuntimeTuple)restored.Payload!)[1], Is.EqualTo(2d));
            Assert.That(NSGetterEvaluator.ResolveHostCollection(((RuntimeTuple)restored.Payload!)[0], replayCtx), Is.EqualTo(new object?[] { 8d }));
        }

        [TestCase("field")]
        [TestCase("add")]
        [TestCase("index")]
        [TestCase("dictionary")]
        [TestCase("tuple")]
        [TestCase("bulk")]
        public void NullFromAnObjectHeldListIsRefusedAtRequiredStoredDestinations(string destination)
        {
            using NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            TypeBinding type = TypeBinding.Closed(new TypeHead(MemberKind.List, true), new Dictionary<string, TypeBinding>
            {
                ["0"] = TypeBinding.Closed(new TypeHead(MemberKind.String, true)),
            });
            NeoBox box = NeoBox.Of(type, new object?[] { null });
            StoreCollectionBox(client, ctx, box, "nullable-box");
            var scope = new NeoScriptScope(new Dictionary<string, object?> { ["box"] = box });
            object? cast = NSGetterEvaluator.EvalPointer(new CastPointer
            {
                type = PointerKind.Cast,
                pointer = new VariablePointer { type = PointerKind.Variable, variableId = "box" },
                sourceType = new ObjectTypeInfo { type = MemberKind.Object, required = true },
                targetType = NeoTypeBindings.ToTypeInfo(type),
                mode = CastModeKind.Cast,
            }, scope, ctx);
            object? missing = ((object?[])NSGetterEvaluator.ResolveHostCollection(cast is NeoBox held ? held.Payload : cast, ctx)!)[0];
            Assert.That(missing, Is.Null);
            var list = client.save.Get<NeoMemberListWritable>("Names");
            string path = destination switch
            {
                "field" => "Root.Name",
                "dictionary" => "Root.Map[key]",
                "tuple" => "Root.Pair.Item1",
                "bulk" => "Root.Names[0]",
                "index" => "Root.Names[0]",
                _ => "Root.Names[1]"
            };
            var error = Assert.Throws<System.ArgumentNullException>(() =>
            {
                switch (destination)
                {
                    case "field":
                        Write(client, "Name", NeoValueWritePayload.FromValue(missing));
                        break;
                    case "add":
                        list.AddSerialized(NeoValueWritePayload.FromValue(missing));
                        break;
                    case "index":
                        list.SetSerialized(0, NeoValueWritePayload.FromValue(missing));
                        break;
                    case "dictionary":
                        client.save.Get<NeoMemberDictionaryWritable>("Map").SetSerialized("key", NeoValueWritePayload.FromValue(missing));
                        break;
                    case "bulk":
                        Write(client, "Names", NeoValueWritePayload.FromValue(new NeoValuePayload(new[] { "null-child" }, valueRows: new MemberValue[]
                        {
                            new StringMemberValue { id = "null-child", value = null },
                        })));
                        break;
                    case "tuple":
                        Write(client, "Pair", NeoValueWritePayload.FromValue(new NeoValuePayload(new[] { "null-child", "int-child" }, valueRows: new MemberValue[]
                        {
                            new StringMemberValue { id = "null-child", value = null }, new NumberMemberValue { id = "int-child", value = 1 },
                        })));
                        break;
                }
            })!;
            Assert.That(error.ParamName, Is.EqualTo("value"));
            Assert.That(error.Message, Does.StartWith($"'{path}' is required and cannot store null."));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClrBoxCrossingsRetainIdentityInBothDirections(bool plain)
        {
            using NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var type = new ObjectTypeInfo { type = MemberKind.Object, required = true };
            object original = plain ? new object() : (object)7;
            object first = Normalize(client, original, type, ctx)!;
            Assert.That(Normalize(client, original, type, ctx), Is.SameAs(first));
            object returned = NeoGeneratedTypesSupport.ReadTypeArgument<object>(client, first, "T", null!, null!);
            Assert.That(returned, Is.SameAs(original));
            NeoBox neo = plain ? NeoBox.NewObject() : NeoBox.Of(IntBinding, 7d);
            object converted = NeoGeneratedTypesSupport.ReadTypeArgument<object>(client, neo, "T", null!, null!);
            Assert.That(NeoGeneratedTypesSupport.ReadTypeArgument<object>(client, neo, "T", null!, null!), Is.SameAs(converted));
            Assert.That(Normalize(client, converted, type, ctx), Is.SameAs(neo));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TupleRootCollectionsAreAdoptedOrRefusedAtomically(bool owned)
        {
            using NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            TypeBinding list = TypeBinding.Closed(new TypeHead(MemberKind.List, true), new Dictionary<string, TypeBinding> { ["0"] = IntBinding });
            var child = new object?[] { 1d };
            if (owned)
                StoreCollectionBox(client, ctx, NeoBox.Of(list, child), "child-owner");
            MemberValue previous = StoreCollectionBox(client, ctx, NeoBox.Of(IntBinding, 7d), "destination");
            TypeBinding tupleType = TypeBinding.Closed(new TypeHead(MemberKind.Tuple, true), new Dictionary<string, TypeBinding> { ["0"] = list, ["1"] = IntBinding });
            var tuple = NeoBox.Of(tupleType, new RuntimeTuple(new object?[] { child, 2d }, (TupleTypeInfo)NeoTypeBindings.ToTypeInfo(tupleType)));
            if (owned)
            {
                var error = Assert.Throws<NSGetterRuntimeError>(() => StoreCollectionBox(client, ctx, tuple, previous.id))!;
                Assert.That(error.Message, Is.EqualTo("Writing a boxed (List<int>, int) into Slot would share a collection already owned by another slot. Call Clone() or use Reference."));
                Assert.That(client.TryGetValue(NeoValueOwnership.Session, previous.id, out MemberValue? current), Is.True);
                Assert.That(current, Is.SameAs(previous));
            }
            else
            {
                var row = (ArrayMemberValue)StoreCollectionBox(client, ctx, tuple, previous.id);
                Assert.That(NSGetterEvaluator.FindRowReference(child, ctx)?.valueId, Is.EqualTo(row.value![0]));
            }
        }

        [Test]
        public void AStoredBoxReadsAsOneBox()
        {
            using NeoClient client = BuildClient();
            Write(client, "Slot", NeoGeneratedTypesSupport.ObjectValue(client, Node(client, "Slot"), 7));
            MemberValue row = Node(client, "Slot").value!;
            NSGetterEvaluator.Context ctx = client.RentDirectFunctionContext(NeoValueOwnership.Save);
            try
            {
                object? first = NSGetterEvaluator.UnwrapRow(row, ctx, NeoValueOwnership.Save, Slot(client, "slot-field"));
                Assert.That(first, Is.InstanceOf<NeoBox>());
                Assert.That(((NeoBox)first!).ValueType, Is.EqualTo(IntBinding));
                Assert.That(((NeoBox)first).Payload, Is.EqualTo(7d));
                Assert.That(NSGetterEvaluator.UnwrapRow(row, ctx, NeoValueOwnership.Save, Slot(client, "slot-field")), Is.SameAs(first));
                // A read as the held type's field gets the payload, as C# reads it.
                Assert.That(NSGetterEvaluator.UnwrapRow(row, ctx, NeoValueOwnership.Save, client.TypeRules.Box(IntBinding).Member!), Is.EqualTo(7d));
            }
            finally
            {
                client.ReturnDirectFunctionContext(ctx, null);
            }
        }

        [Test]
        public void EvaluatorBoxesWriteWhatCSharpReads()
        {
            using NeoClient client = BuildClient();
            WriteEvaluated(client, "Slot", "slot-field", NeoBox.Of(IntBinding, 7d));
            Assert.That(NeoGeneratedTypesSupport.ReadObjectValue(Node(client, "Slot")), Is.EqualTo(7));
            Assert.That(Node(client, "Slot").value!.valueType, Is.EqualTo(IntBinding));

            var vector = new NeoVector2Value { x = 1.5, y = -2 };
            WriteEvaluated(client, "Slot", "slot-field", NeoBox.Of(TypeBinding.Closed(new TypeHead(MemberKind.Vector2, true)), vector));
            Assert.That(NeoGeneratedTypesSupport.ReadObjectValue(Node(client, "Slot")), Is.EqualTo(new Vector2(1.5f, -2f)));

            WriteEvaluated(client, "Slot", "slot-field", NeoBox.NewObject());
            Assert.That(NeoGeneratedTypesSupport.ReadObjectValue(Node(client, "Slot"))!.GetType(), Is.EqualTo(typeof(object)));
        }

        [Test]
        public void TypeValuesWriteAndReadBack()
        {
            using NeoClient client = BuildClient();
            NeoTypeValue intType = NeoTypeValue.Of(IntBinding);
            WriteEvaluated(client, "Slot", "slot-field", intType);
            WriteEvaluated(client, "TypeSlot", "type-field", intType);
            Assert.That(NeoGeneratedTypesSupport.ReadObjectValue(Node(client, "Slot")), Is.EqualTo(typeof(int)));
            Assert.That(NeoGeneratedTypesSupport.ReadObjectValue(Node(client, "TypeSlot")), Is.EqualTo(typeof(int)));
            NSGetterEvaluator.Context ctx = client.RentDirectFunctionContext(NeoValueOwnership.Save);
            try
            {
                Assert.That(NSGetterEvaluator.UnwrapRow(Node(client, "Slot").value!, ctx, NeoValueOwnership.Save, Slot(client, "slot-field")), Is.SameAs(intType));
                Assert.That(NSGetterEvaluator.UnwrapRow(Node(client, "TypeSlot").value!, ctx, NeoValueOwnership.Save, Slot(client, "type-field")), Is.SameAs(intType));
            }
            finally
            {
                client.ReturnDirectFunctionContext(ctx, null);
            }
        }

        [Test]
        public void CSharpArgumentsToObjectParametersBox()
        {
            using NeoClient client = BuildClient();
            var objectType = new ObjectTypeInfo { type = MemberKind.Object, required = false };
            NSGetterEvaluator.Context ctx = client.RentDirectFunctionContext(NeoValueOwnership.Session);
            try
            {
                object? boxed = Normalize(client, 7, objectType, ctx);
                Assert.That(boxed, Is.InstanceOf<NeoBox>());
                Assert.That(((NeoBox)boxed!).ValueType, Is.EqualTo(IntBinding));
                Assert.That(((NeoBox)boxed).Payload, Is.EqualTo(7d));
                var identity = new object();
                Assert.That(Normalize(client, identity, objectType, ctx), Is.SameAs(Normalize(client, identity, objectType, ctx)));
                Assert.That(Normalize(client, typeof(int), objectType, ctx), Is.SameAs(NeoTypeValue.Of(IntBinding)));
                NeoBox box = NeoBox.Of(IntBinding, 3d);
                Assert.That(Normalize(client, box, objectType, ctx), Is.SameAs(box));
            }
            finally
            {
                client.ReturnDirectFunctionContext(ctx, null);
            }
        }

        [Test]
        public void StoredBoxConstructorArgumentsRetainTheirRow()
        {
            using NeoClient client = BuildClient();
            WriteEvaluated(client, "Slot", "slot-field", NeoBox.Of(IntBinding, 7d));
            MemberValue row = Node(client, "Slot").value!;
            NSGetterEvaluator.Context ctx = client.RentDirectFunctionContext(NeoValueOwnership.Save);
            try
            {
                object? box = NSGetterEvaluator.UnwrapRow(row, ctx, NeoValueOwnership.Save, Slot(client, "slot-field"));
                NeoConstructorValueReference? reference = NSGetterEvaluator.ConstructorReferenceOf(box, ctx);
                Assert.That(reference?.valueId, Is.EqualTo(row.id));
                Assert.That(reference?.ownership, Is.EqualTo(NeoValueOwnership.Save));
            }
            finally
            {
                client.ReturnDirectFunctionContext(ctx, null);
            }
        }

        [TestCase(MemberKind.List)]
        [TestCase(MemberKind.Set)]
        public void FreshBoxedCollectionsAdoptTheirRowsAndAllowTheSamePlacement(MemberKind kind)
        {
            using NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            TypeBinding type = TypeBinding.Closed(new TypeHead(kind, true), new Dictionary<string, TypeBinding> { ["0"] = IntBinding });
            object items = kind == MemberKind.Set
                ? NSGetterEvaluator.MakeRuntimeSet(new object?[] { 1d, 2d }, false, false, ctx)
                : new object?[] { 1d, 2d };
            NeoBox box = NeoBox.Of(type, items);
            var row = (ArrayMemberValue)StoreCollectionBox(client, ctx, box, "collection-box");
            Assert.That(row.valueType, Is.EqualTo(type));
            Assert.That(row.value, Has.Length.EqualTo(2));
            foreach (string child in row.value!)
                Assert.That(client.TryGetValue(NeoValueOwnership.Session, child, out NumberMemberValue? _), Is.True);
            Assert.That(NSGetterEvaluator.FindRowReference(items, ctx)?.valueId, Is.EqualTo(row.id));
            Assert.That(NSGetterEvaluator.ConstructorReferenceOf(box, ctx)?.valueId, Is.EqualTo(row.id));
            var reassigned = (ArrayMemberValue)StoreCollectionBox(client, ctx, box, row.id);
            Assert.That(reassigned.value, Is.EqualTo(row.value));
            var error = Assert.Throws<NSGetterRuntimeError>(() => StoreCollectionBox(client, ctx, box, "another-box"))!;
            Assert.That(error.Message, Is.EqualTo($"Writing a boxed {NeoTypeNames.Of(type, client.ProjectDataForRuntime)} into Slot would share a collection already owned by another slot. Call Clone() or use Reference."));
            Assert.That(client.TryGetValue(NeoValueOwnership.Session, "another-box", out MemberValue? _), Is.False);
        }

        [Test]
        public void NestedOwnedCollectionRefusalPreservesTheOldBox()
        {
            using NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            TypeBinding list = TypeBinding.Closed(new TypeHead(MemberKind.List, true), new Dictionary<string, TypeBinding> { ["0"] = IntBinding });
            var child = new object?[] { 1d };
            StoreCollectionBox(client, ctx, NeoBox.Of(list, child), "child-owner");
            MemberValue previous = StoreCollectionBox(client, ctx, NeoBox.Of(list, new object?[] { 7d }), "destination");
            TypeBinding nested = TypeBinding.Closed(new TypeHead(MemberKind.List, true), new Dictionary<string, TypeBinding> { ["0"] = list });
            var error = Assert.Throws<NSGetterRuntimeError>(() => StoreCollectionBox(client, ctx, NeoBox.Of(nested, new object?[] { child }), previous.id))!;
            Assert.That(error.Message, Does.StartWith("Writing a boxed List<List<int>> into Slot would share"));
            Assert.That(client.TryGetValue(NeoValueOwnership.Session, previous.id, out MemberValue? current), Is.True);
            Assert.That(current, Is.SameAs(previous));
        }

        [Test]
        public void OwnedCollectionInsideABoxedTupleEntryIsNotCopied()
        {
            using NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            TypeBinding list = TypeBinding.Closed(new TypeHead(MemberKind.List, true), new Dictionary<string, TypeBinding> { ["0"] = IntBinding });
            var child = new object?[] { 1d };
            StoreCollectionBox(client, ctx, NeoBox.Of(list, child), "child-owner");
            TypeBinding tupleType = TypeBinding.Closed(new TypeHead(MemberKind.Tuple, true), new Dictionary<string, TypeBinding> { ["0"] = list, ["1"] = IntBinding });
            var tupleInfo = new TupleTypeInfo
            {
                type = MemberKind.Tuple,
                required = true,
                elements = new[]
                {
                    new TupleElementTypeInfo { typeInfo = new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true } } },
                    new TupleElementTypeInfo { typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true } },
                },
            };
            var tuple = NeoBox.Of(tupleType, new RuntimeTuple(new object?[] { child, 2d }, tupleInfo));
            TypeBinding objects = TypeBinding.Closed(new TypeHead(MemberKind.List, true), new Dictionary<string, TypeBinding> { ["0"] = TypeBinding.Closed(new TypeHead(MemberKind.Object, true)) });
            var error = Assert.Throws<NSGetterRuntimeError>(() => StoreCollectionBox(client, ctx, NeoBox.Of(objects, new object?[] { tuple }), "destination"))!;
            Assert.That(error.Message, Does.StartWith("Writing a boxed List<object> into Slot would share"));
            Assert.That(client.TryGetValue(NeoValueOwnership.Session, "destination", out MemberValue? _), Is.False);
        }

        private static MemberValue StoreCollectionBox(NeoClient client, NSGetterEvaluator.Context ctx, NeoBox box, string id, NeoValueOwnership ownership = NeoValueOwnership.Session)
        {
            var slot = (ObjectMember)Slot(client, "slot-field");
            var plan = new NeoWritePlan(client);
            NeoValuePayload payload = NeoGeneratedTypesSupport.EvaluatorObjectPayload(client, slot, ownership, box, ctx, id, plan);
            client.StageWritablePayloadRows(plan, ownership, payload);
            NeoTimestamp now = NeoTimestamp.Now();
            MemberValue row = MemberValueFactory.Create(slot, payload, id, now, now);
            plan.Set(ownership, row);
            plan.Commit();
            return row;
        }

        private static object? Normalize(NeoClient client, object value, TypeInfo type, NSGetterEvaluator.Context ctx) =>
            NeoScriptValueMarshaller.Normalize(client, NeoValueOwnership.Session, value, type, ctx, "o");

        private static void WriteEvaluated(NeoClient client, string key, string slotId, object value)
        {
            NSGetterEvaluator.Context ctx = client.RentDirectFunctionContext(NeoValueOwnership.Save);
            try
            {
                NeoValuePayload payload = NeoGeneratedTypesSupport.EvaluatorObjectPayload(
                    client, (ObjectMember)Slot(client, slotId), NeoValueOwnership.Save, value, ctx);
                Write(client, key, NeoValueWritePayload.FromValue(payload));
            }
            finally
            {
                client.ReturnDirectFunctionContext(ctx, null);
            }
        }

        private static void Write(NeoClient client, string key, NeoValueWritePayload? payload) =>
            NeoGeneratedTypesSupport.SetValue(client.save, key, payload);

        private static NeoMemberObject Node(NeoClient client, string key) => client.save.Get<NeoMemberObject>(key);

        private static JsonMember Slot(NeoClient client, string id)
        {
            Assert.That(client.TryGetMember(id, out JsonMember? member), Is.True, id);
            return member!;
        }

        private static NeoClient BuildClient(string? saved = null)
        {
            var data = new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    _id = ProjectId,
                    name = "Types storage",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, JsonMember>
                {
                    ["root-assets"] = Root("root-assets"),
                    ["root-save"] = Root("root-save"),
                    ["root-session"] = Root("root-session"),
                    ["slot-field"] = new ObjectMember { id = "slot-field", projectId = ProjectId, name = "Slot", kind = MemberKind.Object },
                    ["type-field"] = new ObjectMember
                    {
                        id = "type-field",
                        projectId = ProjectId,
                        name = "TypeSlot",
                        kind = MemberKind.Object,
                        objectType = NeoObjectTypeKind.Type,
                    },
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = RootValue("root-assets-value"),
                    ["root-save-value"] = RootValue("root-save-value"),
                    ["root-session-value"] = RootValue("root-session-value"),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [RootClass] = new NeoSchemaClass
                    {
                        id = RootClass,
                        projectId = ProjectId,
                        name = "Root",
                        schema = new Dictionary<string, string>
                        {
                            ["Slot"] = "slot-field",
                            ["TypeSlot"] = "type-field",
                        },
                    },
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
            data.members["required-text"] = new StringMember { id = "required-text", name = "Name", kind = MemberKind.String, Requirement = NeoMemberRequirementKind.Required };
            data.members["names"] = new ListMember { id = "names", name = "Names", kind = MemberKind.List, entryMemberId = "required-text" };
            data.members["map"] = new DictionaryMember { id = "map", name = "Map", kind = MemberKind.Dictionary, entryMemberId = "required-text" };
            data.members["pair-int"] = new IntMember { id = "pair-int", name = "Item2", kind = MemberKind.Int, Requirement = NeoMemberRequirementKind.Required };
            data.members["pair"] = new TupleMember { id = "pair", name = "Pair", kind = MemberKind.Tuple, elementMemberIds = new[] { "required-text", "pair-int" } };
            var schema = data.classes[RootClass].schema;
            schema["Name"] = "required-text";
            schema["Names"] = "names";
            schema["Map"] = "map";
            schema["Pair"] = "pair";
            data.values["text"] = new StringMemberValue { id = "text", value = "ok" };
            data.values["names-value"] = new ArrayMemberValue { id = "names-value", value = new[] { "text" } };
            ((ObjectMemberValue)data.values["root-save-value"]).value!["Names"] = "names-value";
            return NeoTestSaveStack.ClientFromSchema(data.WithUserRoot(), loadedSaveContent: saved);
        }

        private static ClassMember Root(string id) => new()
        {
            id = id,
            projectId = ProjectId,
            name = id,
            kind = MemberKind.Class,
            Requirement = NeoMemberRequirementKind.Required,
            valueId = id + "-value",
            DeclaredClassId = RootClass,
        };

        private static ObjectMemberValue RootValue(string id) => new()
        {
            id = id,
            classId = RootClass,
            value = new Dictionary<string, string>(),
        };
    }
}
