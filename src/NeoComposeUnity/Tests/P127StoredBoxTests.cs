// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §1.3 box storage, §9.5 comparer rows and §5.3 generic statics: a
    /// box stores its payload as a field of its held type, with child rows
    /// under in-memory payload views, and no stored binding names a generic static.
    /// </summary>
    public class P127StoredBoxTests
    {
        private const string ProjectId = "p127-boxes";
        private const string BoxParam = "box-t";
        private const string IBoxId = "ibox";
        private const string StaticRefusal = "Static 'Box<T>.Items' has state per closed construction, so only code can reference it.";

        [Test]
        public void BoxedTupleElementsLoadUnderPayloadViews()
        {
            using NeoClient client = BuildClient();

            var tuple = (TupleMember)NeoTypeBindingViews.PayloadView(TestTypeBindings.FromKey("(int,float)"), out string invalid)!;
            Assert.That(invalid, Is.Empty);
            Assert.That(client.TryGetMember(tuple.elementMemberIds[1], out Member? element), Is.True);
            Assert.That(element, Is.InstanceOf<FloatMember>());
            // The walk reads the element row through its position's view,
            // so the stored symbol decodes to a float.
            var ratio = (NumberMemberValue)client.ResolveValueRow("asset-held-ratio")!;
            Assert.That(ratio.value, Is.NaN);
        }

        // Each tuple position reads as its own view, so a tuple's element ids
        // stay distinct when its element types repeat.
        [TestCase("(int,int)", "Int", "Int")]
        [TestCase("(int,(int,int))", "Int", "Tuple")]
        [TestCase("List<(int,int)>", "Int", "Int")]
        [TestCase("Dictionary<string,(float,float)>", "Float", "Float")]
        [TestCase("(string,string,string)", "String", "String", "String")]
        public void TuplePositionsHaveTheirOwnViews(string key, params string[] positions)
        {
            Member? view = NeoTypeBindingViews.PayloadView(TestTypeBindings.FromKey(key), out string invalid);
            Assert.That(view, Is.Not.Null, invalid);
            string tupleId = view switch
            {
                ListMember list => list.entryMemberId,
                DictionaryMember dictionary => dictionary.entryMemberId,
                _ => view!.id,
            };
            var tuple = (TupleMember)NeoTypeBindingViews.Member(tupleId)!;
            Assert.That(tuple.elementMemberIds, Is.Unique);
            Assert.That(tuple.elementMemberIds, Has.Length.EqualTo(positions.Length));
            for (int index = 0; index < positions.Length; index++)
            {
                Member position = NeoTypeBindingViews.Member(tuple.elementMemberIds[index])!;
                Assert.That(position.kind.ToString(), Is.EqualTo(positions[index]));
                if (position is TupleMember nested)
                    Assert.That(nested.elementMemberIds, Is.Unique);
            }
        }

        // A homogeneous tuple box loads in an object slot and in the
        // IEquatable<(int,int)> slot the conformance table admits it to.
        [TestCase(false)]
        [TestCase(true)]
        public void AHomogeneousTupleBoxLoads(bool interfaceSlot)
        {
            using NeoClient client = BuildClient(data =>
            {
                if (interfaceSlot)
                    AddInterfaceSlot(data, IEquatableOfT, "IEquatable", "tuple-arg", "(int,int)", "['pair-0','pair-1']");
                else
                {
                    RegisterComparerClasses(data);
                    AddSlotMember(data, new ObjectMember { kind = MemberKind.Object });
                    data.values["asset-slot"] = Box("(int,int)", "['pair-0','pair-1']", data);
                }
                data.values["pair-0"] = new NumberMemberValue { id = "pair-0", value = 1 };
                data.values["pair-1"] = new NumberMemberValue { id = "pair-1", value = 2 };
            });

            Assert.That(KeyOf(client, "asset-slot"), Is.EqualTo("(int,int)"));
            Assert.That(client.ResolveValueRow("pair-1"), Is.InstanceOf<NumberMemberValue>());
        }

        [Test]
        public void NewObjectOutsideAnObjectSlotIsRefused()
        {
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(data =>
            {
                data.members["boxed-field"].valueId = "asset-boxed";
                data.values["asset-boxed"] = new ObjectMemberValue
                {
                    id = "asset-boxed",
                    valueType = TestTypeBindings.FromKey("object"),
                    value = new Dictionary<string, string>(),
                };
            }))!;

            StringAssert.Contains(
                "value \"asset-boxed\" (Boxed) holds 'object', but only an object slot holds a new object().",
                error.Message);
        }

        [Test]
        public void ObjectSlotRowWithoutAClassIsRefused()
        {
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(data =>
                data.values["asset-held"] = new ObjectMemberValue
                {
                    id = "asset-held",
                    value = new Dictionary<string, string>(),
                }))!;

            StringAssert.Contains(
                "value \"asset-held\" (Held) must name its value's type with a classId or valueType.",
                error.Message);
        }

        [Test]
        public void ComparerRowIsWalkedAndMustBeAClassRow()
        {
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(data =>
                data.values["asset-numbers"].comparer = new NeoCollectionComparer
                {
                    kind = NeoCollectionComparerKind.Value,
                    valueId = "asset-comparer",
                }))!;

            StringAssert.Contains(
                "value \"asset-comparer\" (comparer) is an authored comparer, so it must be a class row.",
                error.Message);
        }

        [TestCase("{'id':'x','valueType':'int','classId':'money','value':{}}", "has valueType 'int' and classId 'money'; a box holds no class row.")]
        [TestCase("{'id':'x','valueType':'object','value':{'a':'b'}}", "'object' payload must be the empty record {}, as a field of that type stores it.")]
        [TestCase("{'id':'x','valueType':'int','value':'three'}", "A member value row 'int' payload must store an integer.")]
        [TestCase("{'id':'x','valueType':'int','value':1.5}", "A member value row 'int' payload must store an integer.")]
        [TestCase("{'id':'x','valueType':'int','value':2147483648}", "A member value row 'int' payload must fit in an int32.")]
        [TestCase("{'id':'x','valueType':'int','value':-2147483649}", "A member value row 'int' payload must fit in an int32.")]
        [TestCase("{'id':'x','valueType':'Vector2Int','value':{'x':1,'y':2147483648}}", "A member value row Vector2Int field \"y\" must fit in an int32, not 2147483648.")]
        [TestCase("{'id':'x','valueType':'Vector2Int','value':{'x':1.5,'y':0}}", "A member value row Vector2Int field \"x\" must be a whole number, not 1.5.")]
        [TestCase("{'id':'x','valueType':'Vector3Int','value':{'x':0,'y':0,'z':-2147483649}}", "A member value row Vector3Int field \"z\" must fit in an int32, not -2147483649.")]
        [TestCase("{'id':'x','valueType':'Vector3Int','value':{'x':0,'y':'NaN','z':0}}", "A member value row Vector3Int field \"y\" must be a whole number, not NaN.")]
        [TestCase("{'id':'x','valueType':'(int,int)','value':['a']}", "'(int, int)' payload must be an array of 2 element value ids, as a field of that type stores it.")]
        [TestCase("{'id':'x','valueType':'Reference<int>','value':'v1'}", "'Reference<int>' payload must be an array of at most one target value id, as a field of that type stores it.")]
        [TestCase("{'id':'x','valueType':'Reference<int>','value':['v1','v2']}", "'Reference<int>' payload must be an array of at most one target value id, as a field of that type stores it.")]
        [TestCase("{'id':'x','valueType':'NeoDialogueReference','value':'d1'}", "'NeoDialogueReference' payload must be an array of dialogue ids, as a field of that type stores it.")]
        public void BoxPayloadStoresAsAFieldOfItsType(string row, string expected)
        {
            var error = Assert.Throws<JsonSerializationException>(() => Row(row))!;

            StringAssert.Contains(expected, error.Message);
        }

        [TestCase("{'id':'x','valueType':'int','value':2147483647}")]
        [TestCase("{'id':'x','valueType':'int','value':-2147483648}")]
        [TestCase("{'id':'x','valueType':'int','value':3.0}")]
        [TestCase("{'id':'x','valueType':'Vector2Int','value':{'x':2147483647,'y':-2147483648}}")]
        public void BoxedIntsAdmitTheWholeInt32Range(string row)
        {
            Assert.DoesNotThrow(() => Row(row));
        }

        [TestCase("['v1']")]
        [TestCase("[]")]
        public void ReferenceBoxStoresASelectionArray(string payload)
        {
            var row = Row($"{{'id':'x','valueType':'Reference<int>','value':{payload}}}");

            Assert.That(row, Is.InstanceOf<ArrayMemberValue>());
        }

        [Test]
        public void LookupCollectionOnAGenericStaticIsRefused()
        {
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(data =>
                data.members["pick-field"] = new LookupMember
                {
                    id = "pick-field",
                    projectId = ProjectId,
                    name = "Pick",
                    kind = MemberKind.Lookup,
                    collectionMemberId = "box-items-field",
                }))!;

            StringAssert.Contains($"Member \"Pick\" (pick-field) binds collection \"box-items-field\". {StaticRefusal}", error.Message);
        }

        [Test]
        public void ClassTargetOnAGenericStaticIsRefused()
        {
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(data =>
                data.classes["empty-class"].targetMemberId = "box-items-field"))!;

            StringAssert.Contains($"Class \"Empty\" (empty-class) targets member \"box-items-field\". {StaticRefusal}", error.Message);
        }

        [TestCase("box-items")]
        [TestCase("box-item-1")]
        public void ReferenceIntoAGenericStaticIsRefused(string target)
        {
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(data =>
            {
                ((ObjectMemberValue)data.values["assets"]).value!["Ref"] = "asset-ref";
                data.values["asset-ref"] = new ArrayMemberValue { id = "asset-ref", value = new[] { target } };
            }))!;

            StringAssert.Contains($"Lookup \"Ref\" value \"asset-ref\" targets value \"{target}\". {StaticRefusal}", error.Message);
        }

        [TestCase("box-items")]
        [TestCase("box-item-1")]
        public void HostWritesRefuseAGenericStaticTarget(string target)
        {
            using NeoClient client = BuildClient();
            NeoMemberLookupWritable lookup = client.save.Get<NeoMemberLookupWritable>("Ref");
            string violation = $"targets value \"{target}\". {StaticRefusal}";

            Assert.AreEqual($"Lookup 'Ref' {violation}", Assert.Throws<InvalidOperationException>(() => lookup.Add(target))!.Message);
            Assert.AreEqual($"Lookup 'Ref' {violation}", Assert.Throws<InvalidOperationException>(() => lookup.Set(new[] { target }))!.Message);
            // A generated setter writes the selection through its owner.
            var error = Assert.Throws<InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.SetValue(client.save, "Ref", NeoValueWritePayload.FromValue(new[] { target })))!;
            Assert.AreEqual($"Lookup 'Holder.Ref' {violation}", error.Message);
        }

        [Test]
        public void VariantFolderBindingIntoAGenericStaticIsRefused()
        {
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(data =>
                data.variantFolders["folder-1"] = new VariantFolderRecord
                {
                    id = "folder-1",
                    classId = "empty-class",
                    path = "Items",
                    binding = new VariantFolderBinding
                    {
                        collectionMemberId = "box-items-field",
                        collectionValueId = "box-items",
                    },
                }))!;

            StringAssert.Contains($"Variant folder \"folder-1\" binds \"collectionValueId\" to \"box-items\". {StaticRefusal}", error.Message);
        }

        [Test]
        public void DialogueLinkedToAGenericStaticIsRefused()
        {
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(data =>
                data.dialogues["greeting"] = new Dialogue
                {
                    id = "greeting",
                    projectId = ProjectId,
                    name = "Greeting",
                    nodes = new Dictionary<string, DialogueBodyNode>(),
                    linkedValues = new[]
                    {
                        new DialogueLinkedValue { valueId = "box-item-1", source = DialogueLinkedValueSource.Manual },
                    },
                    settings = new DialogueSettings(),
                }))!;

            StringAssert.Contains($"Dialogue \"Greeting\" (greeting) binds \"valueId\" to \"box-item-1\". {StaticRefusal}", error.Message);
        }

        private const string StaticGetter =
            "'getter':{'compilerRevision':17,'parameters':[],'typeInfo':{'required':true,'type':1},"
            + "'instructions':[{'type':'return','pointer':{'type':'staticMember','memberId':'box-items-field'}}]}";

        // The server's isNeoFlowCode: UI logic and inline code are bindings,
        // compiled bodies included; a reusable function's code is not.
        [TestCase("{'type':0,'id':'c1'," + StaticGetter + "}")]
        [TestCase("{'type':1,'id':'c1','code':'Box.Items','sourceFunctionId':'f1','sourceInline':true," + StaticGetter + "}")]
        [TestCase("{'type':1,'id':'c1','sourceFunctionId':'f1'," + StaticGetter + "}")]
        public void DialogueLogicBodyNamingAGenericStaticIsRefused(string condition)
        {
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(data => AddGate(data, condition)))!;

            StringAssert.Contains($"Dialogue group \"Gate\" (gate) binds \"memberId\" to \"box-items-field\". {StaticRefusal}", error.Message);
        }

        [Test]
        public void ReusableNeoFlowFunctionNamingAGenericStaticLoads()
        {
            using NeoClient client = BuildClient(data =>
                AddGate(data, "{'type':1,'id':'c1','code':'Box.Items','sourceFunctionId':'f1'," + StaticGetter + "}"));

            Assert.That(client, Is.Not.Null);
        }

        private static void AddGate(ProjectData data, string condition) =>
            data.dialogueGroups["gate"] = new StandardDialogueGroup
            {
                id = "gate",
                projectId = ProjectId,
                name = "Gate",
                conditions = new[] { JsonConvert.DeserializeObject<LogicCondition>(condition)! },
            };

        // §8.1's system interface ids; the comparer class ids stand in for the system schema's.
        private const string IComparableOfT = "system_27194c76-8e29-41c7-b75f-fbe298a69173";
        private const string IEqualityComparerOfT = "system_7b45289d-274c-47a7-a924-37560a6b41ef";
        private const string IEnumerableOfT = "system_d1413390-a2a6-4137-a8f8-e931da847345";
        private const string EqualityComparerId = "system_7e0c3a52-1f0d-4c55-9a3e-5f2b8d1c0e01";
        private const string ReferenceEqualityComparerId = "system_7e0c3a52-1f0d-4c55-9a3e-5f2b8d1c0e03";
        private const string IntComparer = "{'comparer':0,'family':0,'type':'int'}";
        private const string StringEqualityPayload = "{'comparer':0,'family':0,'type':'string'}";
        private const string ReferenceEqualityPayload = "{'comparer':1,'family':0}";
        private const string BoxOfIntEqualityPayload = "{'comparer':0,'family':0,'type':'c:box-class<int>'}";
        private const string IEquatableOfT = "system_48afe7ca-fe39-41b3-8fa3-cdcb780434ac";
        private const string IEqualityComparer = "system_ea60b68e-c1f3-4d05-b801-aa93b949a6ae";

        // §1.3: a box in an interface slot holds a type the closed interface
        // admits through §8.1's table, with type arguments. Worded as the
        // server's stored-value-graph-semantics.ts.
        [TestCase(IComparableOfT, "IComparable", "int-arg", "string", "'x'", "holds 'string', which doesn't implement IComparable<int>.")]
        [TestCase(IComparableOfT, "IComparable", "int-arg", "float", "1.5", "holds 'float', which doesn't implement IComparable<int>.")]
        [TestCase(IComparableOfT, "IComparable", "vector-arg", "Vector2", "{'x':1,'y':2}", "holds 'Vector2', which doesn't implement IComparable<Vector2>.")]
        [TestCase(IComparableOfT, "IComparable", "int-arg", "AudioClipInfo", "{'fileId':'f'}", "holds 'AudioClipInfo', which implements no system interface, so an IComparable<int> slot can't hold it.")]
        [TestCase(IComparableOfT, "IComparable", "int-arg", "NeoDialogueReference", "['d']", "holds 'NeoDialogueReference', which implements no system interface, so an IComparable<int> slot can't hold it.")]
        // A multi-select dialogue reference is a Set, judged by its member's shape.
        [TestCase(IComparableOfT, "IComparable", "int-arg", "Set<NeoDialogueReference>", "['d']", "holds 'Set<NeoDialogueReference>', a built-in collection, which an interface slot never stores (P125), so an IComparable<int> slot can't hold it.")]
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "int-arg", "c:" + EqualityComparerId + "<string>", StringEqualityPayload, "holds 'EqualityComparer<string>', which doesn't implement IEqualityComparer<int>.")]
        // ReferenceEqualityComparer : IEqualityComparer<object>, contravariant over reference types only.
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "int-arg", "c:" + ReferenceEqualityComparerId, ReferenceEqualityPayload, "holds 'ReferenceEqualityComparer', which doesn't implement IEqualityComparer<int>.")]
        // string converts to IComparable<string>, not IComparable<int> (§8.1).
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "int-arg", "c:" + EqualityComparerId + "<i:" + IComparableOfT + "<int>>", "{'comparer':0,'family':0,'type':'i:" + IComparableOfT + "<int>'}", "holds 'EqualityComparer<IComparable<int>>', which doesn't implement IEqualityComparer<string>.", "string-arg")]
        // A class's type arguments match invariantly.
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "int-arg", "c:" + EqualityComparerId + "<c:box-class<int>>", BoxOfIntEqualityPayload, "holds 'EqualityComparer<Box<int>>', which doesn't implement IEqualityComparer<Box<string>>.", "box-of-string-arg")]
        [TestCase(IComparableOfT, "IComparable", "int-arg", "int", "3", "holds 'int', which doesn't implement IComparable<Reference<Holder>>.", "lookup-arg")]
        [TestCase(IComparableOfT, "IComparable", "int-arg", "int", "3", "holds 'int', which doesn't implement IComparable<int?>.", "optional-int-arg")]
        // P125: a built-in collection is never stored in an interface slot.
        [TestCase(IComparableOfT, "IComparable", "int-arg", "List<int>", "[]", "holds 'List<int>', a built-in collection, which an interface slot never stores (P125), so an IComparable<int> slot can't hold it.")]
        [TestCase(IEnumerableOfT, "IEnumerable", "int-arg", "List<int>", "[]", "holds 'List<int>', a built-in collection, which an interface slot never stores (P125), so an IEnumerable<int> slot can't hold it.")]
        // The refusal names the slot argument's declared type.
        [TestCase(IEquatableOfT, "IEquatable", "int-arg", "int", "3", "holds 'int', which doesn't implement IEquatable<ValueType>.", "value-type-arg")]
        [TestCase(IEquatableOfT, "IEquatable", "int-arg", "int", "3", "holds 'int', which doesn't implement IEquatable<Dictionary<Color, int>>.", "by-color-arg")]
        [TestCase(IEquatableOfT, "IEquatable", "int-arg", "int", "3", "holds 'int', which doesn't implement IEquatable<Set<NeoDialogueReference>>.", "dialogues-arg")]
        [TestCase(IEquatableOfT, "IEquatable", "int-arg", "int", "3", "holds 'int', which doesn't implement IEquatable<NeoAction<int>>.", "action-arg")]
        // Each object type, and a single and a multi-select dialogue reference, is its own type.
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "int-arg", "c:" + EqualityComparerId + "<CultureInfo>", "{'comparer':0,'family':0,'type':'CultureInfo'}", "holds 'EqualityComparer<CultureInfo>', which doesn't implement IEqualityComparer<NumberFormatInfo>.", "number-format-arg")]
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "int-arg", "c:" + EqualityComparerId + "<Type>", "{'comparer':0,'family':0,'type':'Type'}", "holds 'EqualityComparer<Type>', which doesn't implement IEqualityComparer<CultureInfo>.", "culture-arg")]
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "int-arg", "c:" + EqualityComparerId + "<ValueType>", "{'comparer':0,'family':0,'type':'ValueType'}", "holds 'EqualityComparer<ValueType>', which doesn't implement IEqualityComparer<object?>.", "object-arg")]
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "int-arg", "c:" + EqualityComparerId + "<Enum>", "{'comparer':0,'family':0,'type':'Enum'}", "holds 'EqualityComparer<Enum>', which doesn't implement IEqualityComparer<ValueType>.", "value-type-arg")]
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "int-arg", "c:" + EqualityComparerId + "<NeoDialogueReference>", "{'comparer':0,'family':0,'type':'NeoDialogueReference'}", "holds 'EqualityComparer<NeoDialogueReference>', which doesn't implement IEqualityComparer<Set<NeoDialogueReference>>.", "dialogues-arg")]
        // A delegate type argument has a key, so a comparer over another delegate type is refused by it.
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "int-arg", "c:" + EqualityComparerId + "<NeoDelegate<int,string>>", "{'comparer':0,'family':0,'type':'NeoDelegate<int,string>'}", "holds 'EqualityComparer<NeoDelegate<int, string>>', which doesn't implement IEqualityComparer<NeoDelegate<string, int>>.", "string-delegate-arg")]
        public void InterfaceSlotRefusesATypeItsConstructionDoesNotAdmit(
            string interfaceId,
            string interfaceName,
            string argument,
            string valueTypeKey,
            string payload,
            string expected,
            string? slotArgument = null)
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                BuildClient(data => AddInterfaceSlot(data, interfaceId, interfaceName, slotArgument ?? argument, valueTypeKey, payload)))!;

            StringAssert.Contains($"value \"asset-slot\" (Slot) {expected}", error.Message);
        }

        [TestCase(IComparableOfT, "IComparable", "int-arg", "int", "3")]
        [TestCase(IComparableOfT, "IComparable", "string-arg", "string", "'x'")]
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "int-arg", "c:" + EqualityComparerId + "<int>", IntComparer)]
        // ReferenceEqualityComparer : IEqualityComparer<object?>, contravariant over any reference type.
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "string-arg", "c:" + ReferenceEqualityComparerId, ReferenceEqualityPayload)]
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "object-arg", "c:" + ReferenceEqualityComparerId, ReferenceEqualityPayload)]
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "box-of-int-arg", "c:" + EqualityComparerId + "<c:box-class<int>>", BoxOfIntEqualityPayload)]
        // string converts to IComparable<string> through the table (§4.8, §8.1).
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "string-arg", "c:" + EqualityComparerId + "<i:" + IComparableOfT + "<string>>", "{'comparer':0,'family':0,'type':'i:" + IComparableOfT + "<string>'}")]
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "number-format-arg", "c:" + EqualityComparerId + "<NumberFormatInfo>", "{'comparer':0,'family':0,'type':'NumberFormatInfo'}")]
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "culture-arg", "c:" + EqualityComparerId + "<object>", "{'comparer':0,'family':0,'type':'object'}")]
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "dialogues-arg", "c:" + EqualityComparerId + "<Set<NeoDialogueReference>>", "{'comparer':0,'family':0,'type':'Set<NeoDialogueReference>'}")]
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "dialogue-arg", "c:" + EqualityComparerId + "<NeoDialogueReference>", "{'comparer':0,'family':0,'type':'NeoDialogueReference'}")]
        // A delegate type argument keys as §5.3 spells it.
        [TestCase(IEqualityComparerOfT, "IEqualityComparer", "int-delegate-arg", "c:" + EqualityComparerId + "<NeoDelegate<int,string>>", "{'comparer':0,'family':0,'type':'NeoDelegate<int,string>'}")]
        public void InterfaceSlotAdmitsATypeItsConstructionLists(
            string interfaceId, string interfaceName, string argument, string valueTypeKey, string payload)
        {
            using NeoClient client = BuildClient(data => AddInterfaceSlot(data, interfaceId, interfaceName, argument, valueTypeKey, payload));

            Assert.That(KeyOf(client, "asset-slot"), Is.EqualTo(valueTypeKey));
        }

        // A non-generic interface: a comparer of any T implements it.
        [TestCase("c:" + EqualityComparerId + "<int>", IntComparer, null)]
        [TestCase("c:" + ReferenceEqualityComparerId, ReferenceEqualityPayload, null)]
        [TestCase("int", "3", "holds 'int', which doesn't implement IEqualityComparer.")]
        public void NonGenericInterfaceSlotAdmitsEveryComparer(string valueTypeKey, string payload, string? expected)
        {
            void Configure(ProjectData data) => AddInterfaceSlot(data, IEqualityComparer, "IEqualityComparer", null, valueTypeKey, payload);

            if (expected is null)
            {
                using NeoClient client = BuildClient(Configure);
                Assert.That(KeyOf(client, "asset-slot"), Is.EqualTo(valueTypeKey));
                return;
            }
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(Configure))!;
            StringAssert.Contains($"value \"asset-slot\" (Slot) {expected}", error.Message);
        }

        // An argument no environment closes, directly or nested, is refused
        // in the server's words.
        [TestCase(false)]
        [TestCase(true)]
        public void InterfaceSlotWithAnOpenArgumentIsRefused(bool nested)
        {
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(data =>
            {
                AddInterfaceSlot(data, IComparableOfT, "IComparable", "int-arg", "int", "3");
                TypeBinding open = TestTypeBindings.Open("slots-class", "outer-t");
                ((ClassMember)data.members["slot-field"]).interfaceArguments!["slot-t"] = nested
                    ? TestTypeBindings.List(open)
                    : open;
            }))!;

            StringAssert.Contains(
                "value \"asset-slot\" (Slot) holds 'int', but the slot's IComparable type argument \"slot-t\" can't be determined.",
                error.Message);
        }

        // A slot on a generic class instance resolves its arguments in the
        // instance's environment, nested positions included.
        [TestCase(false, null)]
        [TestCase(true, "holds 'int', which doesn't implement IComparable<List<int>>.")]
        public void InterfaceSlotOnAGenericInstanceResolvesItsArguments(bool nested, string? expected)
        {
            void Configure(ProjectData data)
            {
                AddInterfaceSlot(data, IComparableOfT, "IComparable", "int-arg", "int", "3");
                data.values.Remove("asset-slot");
                data.members["wrapped-field"] = new ClassMember
                {
                    id = "wrapped-field",
                    projectId = ProjectId,
                    name = "Wrapped",
                    kind = MemberKind.Class,
                    DeclaredClassId = "wrapper-class",
                    classArguments = new Dictionary<string, TypeBinding>
                    {
                        ["wrapper-t"] = Argument("int-arg"),
                    },
                };
                data.members["wrapper-item-field"] = new ClassMember
                {
                    id = "wrapper-item-field",
                    projectId = ProjectId,
                    name = "Item",
                    kind = MemberKind.Class,
                    DeclaredInterfaceId = IComparableOfT,
                    interfaceArguments = new Dictionary<string, TypeBinding>
                    {
                        ["slot-t"] = nested
                            ? TestTypeBindings.List(TestTypeBindings.Open("wrapper-class", "wrapper-t"))
                            : TestTypeBindings.Open("wrapper-class", "wrapper-t"),
                    },
                };
                data.classes["wrapper-class"] = Class("wrapper-class", "Wrapper", new Dictionary<string, string> { ["Item"] = "wrapper-item-field" });
                data.classes["wrapper-class"].genericParams = new List<GenericParamDeclaration> { new() { id = "wrapper-t", name = "T" } };
                data.classes["holder-class"].schema["Wrapped"] = "wrapped-field";
                ((ObjectMemberValue)data.values["assets"]).value["Wrapped"] = "asset-wrapped";
                data.values["asset-wrapped"] = new ObjectMemberValue
                {
                    id = "asset-wrapped",
                    classId = "wrapper-class",
                    value = new Dictionary<string, string> { ["Item"] = "asset-item" },
                };
                data.values["asset-item"] = Row("{'id':'asset-item','valueType':'int','value':3}");
            }

            if (expected is null)
            {
                using NeoClient client = BuildClient(Configure);
                Assert.That(KeyOf(client, "asset-item"), Is.EqualTo("int"));
                return;
            }
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(Configure))!;
            StringAssert.Contains($"value \"asset-item\" (Item) {expected}", error.Message);
        }

        // IEqualityComparer<in T> takes a comparer over any type the slot's
        // argument converts to: C#'s Enum : ValueType : object, with Type,
        // CultureInfo and NumberFormatInfo deriving from object (§2.1). The
        // compose type-info-compatibility.test.ts ancestor pairs, both ways.
        [TestCase("enum-type-arg", "ValueType", null)]
        [TestCase("enum-type-arg", "object", null)]
        [TestCase("value-type-arg", "object", null)]
        [TestCase("type-arg", "object", null)]
        [TestCase("culture-arg", "object", null)]
        [TestCase("number-format-arg", "object", null)]
        [TestCase("string-arg", "object", null)]
        // A reference reads through to its target, which converts to object.
        [TestCase("lookup-arg", "object", null)]
        [TestCase("value-type-arg", "Enum", "Enum")]
        [TestCase("required-object-arg", "Enum", "Enum")]
        [TestCase("required-object-arg", "ValueType", "ValueType")]
        [TestCase("required-object-arg", "Type", "Type")]
        [TestCase("required-object-arg", "CultureInfo", "CultureInfo")]
        [TestCase("required-object-arg", "NumberFormatInfo", "NumberFormatInfo")]
        [TestCase("type-arg", "CultureInfo", "CultureInfo")]
        [TestCase("string-arg", "ValueType", "ValueType")]
        [TestCase("lookup-arg", "ValueType", "ValueType")]
        public void ContravariantArgumentsConvertUpTheBuiltInHierarchy(string slotArgument, string comparerArgument, string? refused)
        {
            string key = $"c:{EqualityComparerId}<{comparerArgument}>";
            void Configure(ProjectData data) => AddInterfaceSlot(
                data, IEqualityComparerOfT, "IEqualityComparer", slotArgument, key, $"{{'comparer':0,'family':0,'type':'{comparerArgument}'}}");

            if (refused is null)
            {
                using NeoClient client = BuildClient(Configure);
                Assert.That(KeyOf(client, "asset-slot"), Is.EqualTo(key));
                return;
            }
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(Configure))!;
            string wanted = slotArgument switch
            {
                "required-object-arg" => "object",
                "type-arg" => "Type",
                "string-arg" => "string",
                "lookup-arg" => "Reference<Holder>",
                _ => "ValueType",
            };
            StringAssert.Contains($"value \"asset-slot\" (Slot) holds 'EqualityComparer<{comparerArgument}>', which doesn't implement IEqualityComparer<{wanted}>.", error.Message);
        }

        // IEqualityComparer<in T>: a comparer over a base class serves a
        // derived slot argument only with the base's type arguments, as the
        // compose type-info-compatibility.test.ts base-class cases convert.
        // Derived<T> : Base<T>, Derived2<T> : Derived<List<T>>, Closed : Base<int>.
        [TestCase("derived-of-int-arg", "c:base<int>", null, null)]
        [TestCase("derived-of-int-arg", "c:base<string>", "Base<string>", "Derived<int>")]
        [TestCase("derived2-of-int-arg", "c:derived<List<int>>", null, null)]
        [TestCase("derived2-of-int-arg", "c:base<List<int>>", null, null)]
        [TestCase("derived2-of-int-arg", "c:base<List<string>>", "Base<List<string>>", "Derived2<int>")]
        [TestCase("derived2-of-int-arg", "c:base<int>", "Base<int>", "Derived2<int>")]
        [TestCase("closed-arg", "c:base<int>", null, null)]
        [TestCase("closed-arg", "c:base<string>", "Base<string>", "Closed")]
        public void ContravariantClassArgumentsConvertToABaseOnlyWithItsTypeArguments(
            string slotArgument, string comparerArgument, string? shownArgument, string? refused)
        {
            string key = $"c:{EqualityComparerId}<{comparerArgument}>";
            void Configure(ProjectData data)
            {
                AddBaseClassChain(data);
                AddInterfaceSlot(
                    data, IEqualityComparerOfT, "IEqualityComparer", slotArgument, key, $"{{'comparer':0,'family':0,'type':'{comparerArgument}'}}");
            }

            if (refused is null)
            {
                using NeoClient client = BuildClient(Configure);
                Assert.That(KeyOf(client, "asset-slot"), Is.EqualTo(key));
                return;
            }
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(Configure))!;
            StringAssert.Contains($"value \"asset-slot\" (Slot) holds 'EqualityComparer<{shownArgument}>', which doesn't implement IEqualityComparer<{refused}>.", error.Message);
        }

        // The compose "erases reference nullability at every depth of a
        // contravariant conversion": an IBox<string> comparer serves
        // Box<string?>, where Box<T> : IBox<T>, but not Box<int>.
        [TestCase("box-of-optional-string-arg", null)]
        [TestCase("box-of-string-arg", null)]
        [TestCase("box-of-int-arg", "Box<int>")]
        public void ContravariantConversionErasesNestedReferenceNullability(string slotArgument, string? refused)
        {
            string key = $"c:{EqualityComparerId}<i:{IBoxId}<string>>";
            void Configure(ProjectData data) => AddInterfaceSlot(
                data, IEqualityComparerOfT, "IEqualityComparer", slotArgument, key, $"{{'comparer':0,'family':0,'type':'i:{IBoxId}<string>'}}");

            if (refused is null)
            {
                using NeoClient client = BuildClient(Configure);
                Assert.That(KeyOf(client, "asset-slot"), Is.EqualTo(key));
                return;
            }
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(Configure))!;
            StringAssert.Contains($"value \"asset-slot\" (Slot) holds 'EqualityComparer<IBox<string>>', which doesn't implement IEqualityComparer<{refused}>.", error.Message);
        }

        // The compose generics.test.ts "Object, dictionary and dialogue
        // signatures keep their declared type (P127 §1.3)": a binding's type
        // info, its signature and formatTypeInfo's text.
        [Test]
        public void ObjectDictionaryAndDialogueSignaturesKeepTheirDeclaredType()
        {
            using NeoClient client = BuildClient(AddArgumentMembers);
            NeoClassArgumentSignature SignatureOf(string id) =>
                NeoGenericResolution.SignatureOfMember(client, client.TryGetMember(id, out Member? member) ? member : throw new AssertionException(id), NeoGenericResolution.EmptyEnv);
            TypeInfo TypeOf(string id) => NeoGenericResolution.TypeInfoFromArgumentSignature(SignatureOf(id));
            string Spell(string id) => NeoReferenceTypes.Format(client, TypeOf(id));

            Assert.That(Spell("required-object-arg"), Is.EqualTo("object"));
            Assert.That(Spell("value-type-arg"), Is.EqualTo("ValueType"));
            Assert.That(Spell("enum-type-arg"), Is.EqualTo("Enum"));
            Assert.That(Spell("type-arg"), Is.EqualTo("Type"));
            Assert.That(Spell("culture-arg"), Is.EqualTo("CultureInfo"));
            Assert.That(Spell("number-format-arg"), Is.EqualTo("NumberFormatInfo"));
            Assert.That(NeoGenericResolution.SignaturesEqual(SignatureOf("required-object-arg"), SignatureOf("value-type-arg")), Is.False);
            Assert.That(NeoReferenceTypes.InvariantlyEqual(TypeOf("required-object-arg"), TypeOf("value-type-arg")), Is.False);

            Assert.That(Spell("by-color-arg"), Is.EqualTo("Dictionary<Color, int>"));

            Assert.That(Spell("dialogue-arg"), Is.EqualTo("NeoDialogueReference"));
            Assert.That(Spell("dialogues-arg"), Is.EqualTo("Set<NeoDialogueReference>"));
            Assert.That(NeoGenericResolution.SignaturesEqual(SignatureOf("dialogue-arg"), SignatureOf("dialogues-arg")), Is.False);

            // Nested nullability counts in the annotated comparison.
            Assert.That(Spell("box-of-optional-string-arg"), Is.EqualTo("Box<string?>"));
            Assert.That(NeoReferenceTypes.InvariantlyEqual(TypeOf("box-of-optional-string-arg"), TypeOf("box-of-string-arg")), Is.False);
        }

        // §1.3: a ValueType slot holds only value types, an Enum slot only enums.
        [TestCase(NeoObjectTypeKind.ValueType, "string", "'x'", "holds 'string', but a ValueType slot holds only value types.")]
        [TestCase(NeoObjectTypeKind.ValueType, "c:" + EqualityComparerId + "<int>", IntComparer, "holds comparer 'EqualityComparer<int>', but a ValueType slot holds only value types.")]
        [TestCase(NeoObjectTypeKind.Enum, "int", "3", "holds 'int', but an Enum slot holds only enums.")]
        [TestCase(NeoObjectTypeKind.ValueType, "int", "3", null)]
        public void ValueTypeAndEnumSlotsNarrowTheirBoxes(NeoObjectTypeKind objectType, string valueTypeKey, string payload, string? expected)
        {
            void Configure(ProjectData data)
            {
                RegisterComparerClasses(data);
                AddSlotMember(data, new ObjectMember { kind = MemberKind.Object, objectType = objectType });
                data.values["asset-slot"] = Box(valueTypeKey, payload, data);
            }

            if (expected is null)
            {
                using NeoClient client = BuildClient(Configure);
                Assert.That(KeyOf(client, "asset-slot"), Is.EqualTo(valueTypeKey));
                return;
            }
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(Configure))!;
            StringAssert.Contains($"value \"asset-slot\" (Slot) {expected}", error.Message);
        }

        [TestCase("int", IntComparer, null)]
        [TestCase("string", StringEqualityPayload, "whose type argument for \"eq-t\" differs from the slot's.")]
        public void ClassTypedComparerSlotMatchesItsTypeArguments(string typeKey, string payload, string? expected)
        {
            void Configure(ProjectData data)
            {
                RegisterComparerClasses(data);
                data.classes[EqualityComparerId] = Class(EqualityComparerId, "EqualityComparer", new Dictionary<string, string>());
                data.classes[EqualityComparerId].genericParams = new List<GenericParamDeclaration> { new() { id = "eq-t", name = "T" } };
                AddSlotMember(data, new ClassMember
                {
                    kind = MemberKind.Class,
                    DeclaredClassId = EqualityComparerId,
                    classArguments = new Dictionary<string, TypeBinding>
                    {
                        ["eq-t"] = Argument("int-arg"),
                    },
                });
                data.values["asset-slot"] = Box($"c:{EqualityComparerId}<{typeKey}>", payload, data);
            }

            if (expected is null)
            {
                using NeoClient client = BuildClient(Configure);
                Assert.That(client.ResolveValueRow("asset-slot"), Is.Not.Null);
                return;
            }
            var error = Assert.Throws<InvalidOperationException>(() => BuildClient(Configure))!;
            StringAssert.Contains($"value \"asset-slot\" (Slot) holds 'EqualityComparer<{typeKey}>', {expected}", error.Message);
        }

        /// <summary>An interface slot closed over <paramref name="argument"/>, or non-generic when it is null.</summary>
        private static void AddInterfaceSlot(
            ProjectData data, string interfaceId, string interfaceName, string? argument, string valueTypeKey, string payload)
        {
            RegisterComparerClasses(data);
            data.interfaces[interfaceId] = new Interface
            {
                id = interfaceId,
                projectId = ProjectId,
                name = interfaceName,
                members = new Dictionary<string, InterfaceMember>(),
                genericParams = argument is null
                    ? null
                    : new List<GenericParamDeclaration> { new() { id = "slot-t", name = "T" } },
            };
            // The table's string row conforms to IComparable<string>.
            data.interfaces.TryAdd(IComparableOfT, new Interface
            {
                id = IComparableOfT,
                projectId = ProjectId,
                name = "IComparable",
                members = new Dictionary<string, InterfaceMember>(),
                genericParams = new List<GenericParamDeclaration> { new() { id = "slot-t", name = "T" } },
            });
            AddSlotMember(data, new ClassMember
            {
                kind = MemberKind.Class,
                DeclaredInterfaceId = interfaceId,
                interfaceArguments = argument is null
                    ? null
                    : new Dictionary<string, TypeBinding>
                    {
                        ["slot-t"] = Argument(argument),
                    },
            });
            data.values["asset-slot"] = Box(valueTypeKey, payload, data);
        }

        /// <summary>A slot on the uninstantiated slots class, with its row as the member default and the arguments it closes over.</summary>
        private static void AddSlotMember(ProjectData data, Member slot)
        {
            slot.id = "slot-field";
            slot.projectId = ProjectId;
            slot.name = "Slot";
            slot.valueId = "asset-slot";
            data.members[slot.id] = slot;
            data.classes["slots-class"].schema["Slot"] = slot.id;
            AddArgumentMembers(data);
        }

        /// <summary>The members slots close their type arguments over.</summary>
        private static void AddArgumentMembers(ProjectData data)
        {
            data.members["int-arg"] = new IntMember { id = "int-arg", projectId = ProjectId, name = "int", kind = MemberKind.Int, Requirement = NeoMemberRequirementKind.Required };
            data.members["string-arg"] = new StringMember { id = "string-arg", projectId = ProjectId, name = "string", kind = MemberKind.String, Requirement = NeoMemberRequirementKind.Required };
            data.members["vector-arg"] = new Vector2Member { id = "vector-arg", projectId = ProjectId, name = "Vector2", kind = MemberKind.Vector2, Requirement = NeoMemberRequirementKind.Required };
            data.members["int-arg-2"] = new IntMember { id = "int-arg-2", projectId = ProjectId, name = "int", kind = MemberKind.Int, Requirement = NeoMemberRequirementKind.Required };
            data.members["tuple-arg"] = new TupleMember
            {
                id = "tuple-arg",
                projectId = ProjectId,
                name = "(int, int)",
                kind = MemberKind.Tuple,
                Requirement = NeoMemberRequirementKind.Required,
                elementMemberIds = new[] { "int-arg", "int-arg-2" },
            };
            data.members["optional-int-arg"] = new IntMember { id = "optional-int-arg", projectId = ProjectId, name = "int", kind = MemberKind.Int, Requirement = NeoMemberRequirementKind.Optional };
            data.members["optional-string-arg"] = new StringMember { id = "optional-string-arg", projectId = ProjectId, name = "string", kind = MemberKind.String, Requirement = NeoMemberRequirementKind.Optional };
            data.members["object-arg"] = new ObjectMember { id = "object-arg", projectId = ProjectId, name = "object", kind = MemberKind.Object };
            data.members["required-object-arg"] = new ObjectMember
            {
                id = "required-object-arg",
                projectId = ProjectId,
                name = "object",
                kind = MemberKind.Object,
                Requirement = NeoMemberRequirementKind.Required,
            };
            foreach ((string id, NeoObjectTypeKind objectType) in new[]
            {
                ("value-type-arg", NeoObjectTypeKind.ValueType),
                ("enum-type-arg", NeoObjectTypeKind.Enum),
                ("type-arg", NeoObjectTypeKind.Type),
                ("culture-arg", NeoObjectTypeKind.CultureInfo),
                ("number-format-arg", NeoObjectTypeKind.NumberFormatInfo),
            })
            {
                data.members[id] = new ObjectMember
                {
                    id = id,
                    projectId = ProjectId,
                    name = "T",
                    kind = MemberKind.Object,
                    objectType = objectType,
                    Requirement = NeoMemberRequirementKind.Required,
                };
            }
            data.members["dialogue-arg"] = new DialogueLookupMember { id = "dialogue-arg", projectId = ProjectId, name = "T", kind = MemberKind.DialogueLookup, Requirement = NeoMemberRequirementKind.Required };
            data.members["dialogues-arg"] = new DialogueLookupMember
            {
                id = "dialogues-arg",
                projectId = ProjectId,
                name = "T",
                kind = MemberKind.DialogueLookup,
                Requirement = NeoMemberRequirementKind.Required,
                Selection = NeoMemberSelectionKind.Multi,
            };
            data.enums["color"] = new NeoCompose.Runtime.Json.Enum
            {
                id = "color",
                projectId = ProjectId,
                name = "Color",
                options = new Dictionary<string, EnumOption> { ["red"] = new EnumOption { text = "Red" } },
                optionKeyOrder = new List<string> { "red" },
            };
            data.members["by-color-arg"] = new DictionaryMember
            {
                id = "by-color-arg",
                projectId = ProjectId,
                name = "T",
                kind = MemberKind.Dictionary,
                Requirement = NeoMemberRequirementKind.Required,
                KeyKind = NeoDictionaryKeyKind.Enum,
                keyEnumId = "color",
                entryMemberId = "int-arg",
            };
            data.members["int-delegate-arg"] = Delegate("int-delegate-arg", MemberKind.Int, MemberKind.String);
            data.members["string-delegate-arg"] = Delegate("string-delegate-arg", MemberKind.String, MemberKind.Int);
            data.members["action-arg"] = new ActionMember
            {
                id = "action-arg",
                projectId = ProjectId,
                name = "T",
                kind = MemberKind.NSAction,
                argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "x", type = MemberKind.Int, required = true } },
            };
            data.members["box-of-int-arg"] = BoxOf("box-of-int-arg", "int-arg");
            data.members["box-of-string-arg"] = BoxOf("box-of-string-arg", "string-arg");
            data.members["box-of-optional-string-arg"] = BoxOf("box-of-optional-string-arg", "optional-string-arg");
            data.members["lookup-arg"] = new LookupMember
            {
                id = "lookup-arg",
                projectId = ProjectId,
                name = "Ref",
                kind = MemberKind.Lookup,
                Requirement = NeoMemberRequirementKind.Required,
                declaredTypeInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "holder-class" },
            };
        }

        /// <summary>
        /// Base&lt;T&gt;, Derived&lt;T&gt; : Base&lt;T&gt;,
        /// Derived2&lt;T&gt; : Derived&lt;List&lt;T&gt;&gt; and
        /// Closed : Base&lt;int&gt;, with slot arguments over each.
        /// </summary>
        private static void AddBaseClassChain(ProjectData data)
        {
            NeoSchemaClass Generic(string id, string name, string? extendsClassId, string? baseParam, TypeBinding? binding)
            {
                NeoSchemaClass schemaClass = Class(id, name, new Dictionary<string, string>());
                schemaClass.genericParams = new List<GenericParamDeclaration> { new() { id = $"{id}.T", name = "T" } };
                schemaClass.extendsClassId = extendsClassId;
                if (baseParam is not null)
                    schemaClass.extendsGenericBindings = new Dictionary<string, TypeBinding> { [baseParam] = binding! };
                data.classes[id] = schemaClass;
                return schemaClass;
            }
            Generic("base", "Base", null, null, null);
            Generic("derived", "Derived", "base", "base.T", TestTypeBindings.Open("derived", "derived.T"));
            Generic("derived2", "Derived2", "derived", "derived.T", TestTypeBindings.List(TestTypeBindings.Open("derived2", "derived2.T")));
            Generic("closed", "Closed", "base", "base.T", Argument("int-arg")).genericParams = null;
            data.members["derived-of-int-arg"] = ClassOf("derived-of-int-arg", "derived", "int-arg");
            data.members["derived2-of-int-arg"] = ClassOf("derived2-of-int-arg", "derived2", "int-arg");
            data.members["closed-arg"] = ClassOf("closed-arg", "closed", null);
        }

        private static ClassMember ClassOf(string id, string classId, string? argument) => new()
        {
            id = id,
            projectId = ProjectId,
            name = classId,
            kind = MemberKind.Class,
            Requirement = NeoMemberRequirementKind.Required,
            DeclaredClassId = classId,
            classArguments = argument is null
                ? null
                : new Dictionary<string, TypeBinding>
                {
                    [$"{classId}.T"] = Argument(argument),
                },
        };

        /// <summary><c>NeoDelegate&lt;result, argument&gt;</c>.</summary>
        private static DelegateMember Delegate(string id, MemberKind result, MemberKind argument) => new()
        {
            id = id,
            projectId = ProjectId,
            name = "T",
            kind = MemberKind.NSDelegate,
            Requirement = NeoMemberRequirementKind.Required,
            returnTypeInfo = new PrimitiveTypeInfo { type = result, required = true },
            argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "x", type = argument, required = true } },
        };

        private static ClassMember BoxOf(string id, string argument) => new()
        {
            id = id,
            projectId = ProjectId,
            name = "Box",
            kind = MemberKind.Class,
            Requirement = NeoMemberRequirementKind.Required,
            DeclaredClassId = "box-class",
            classArguments = new Dictionary<string, TypeBinding>
            {
                [BoxParam] = Argument(argument),
            },
        };

        /// <summary>The type argument member <paramref name="id"/> of <see cref="AddArgumentMembers"/> declares.</summary>
        private static TypeBinding Argument(string id) => id switch
        {
            "optional-int-arg" => Argument("int-arg").WithRequired(false),
            "optional-string-arg" => Argument("string-arg").WithRequired(false),
            "object-arg" => Argument("required-object-arg").WithRequired(false),
            "box-of-int-arg" => TestTypeBindings.Class("box-class", true, (BoxParam, Argument("int-arg"))),
            "box-of-string-arg" => TestTypeBindings.Class("box-class", true, (BoxParam, Argument("string-arg"))),
            "box-of-optional-string-arg" => TestTypeBindings.Class("box-class", true, (BoxParam, Argument("optional-string-arg"))),
            "derived-of-int-arg" => TestTypeBindings.Class("derived", true, ("derived.T", Argument("int-arg"))),
            "derived2-of-int-arg" => TestTypeBindings.Class("derived2", true, ("derived2.T", Argument("int-arg"))),
            "closed-arg" => TestTypeBindings.Class("closed"),
            _ => TestTypeBindings.FromKey(id switch
            {
                "int-arg" or "int-arg-2" => "int",
                "string-arg" => "string",
                "vector-arg" => "Vector2",
                "tuple-arg" => "(int,int)",
                "required-object-arg" => "object",
                "value-type-arg" => "ValueType",
                "enum-type-arg" => "Enum",
                "type-arg" => "Type",
                "culture-arg" => "CultureInfo",
                "number-format-arg" => "NumberFormatInfo",
                "dialogue-arg" => "NeoDialogueReference",
                "dialogues-arg" => "Set<NeoDialogueReference>",
                "by-color-arg" => "Dictionary<e:color,int>",
                "int-delegate-arg" => "NeoDelegate<int,string>",
                "string-delegate-arg" => "NeoDelegate<string,int>",
                "action-arg" => "NeoAction<int>",
                "lookup-arg" => "Reference<c:holder-class>",
                _ => throw new ArgumentException($"No argument member '{id}'.", nameof(id)),
            }),
        };

        private static MemberValue Box(string valueTypeKey, string payload, ProjectData data) =>
            Row($"{{'id':'asset-slot','valueType':'{valueTypeKey}','value':{payload}}}", data);

        /// <summary>
        /// A stored row whose <c>valueType</c>, and a comparer payload's
        /// <c>type</c>, are spelled as runtime type keys and stored as the
        /// bindings they name.
        /// </summary>
        private static MemberValue Row(string json, ProjectData? data = null)
        {
            JObject row = JObject.Parse(json);
            if (row["valueType"] is JValue { Type: JTokenType.String } valueType)
                row["valueType"] = TestTypeBindings.JsonFromKey((string)valueType!, data);
            if (row["value"] is JObject payload && payload["comparer"] is not null && payload["type"] is JValue { Type: JTokenType.String } type)
                payload["type"] = TestTypeBindings.JsonFromKey((string)type!, data);
            return row.ToObject<MemberValue>()!;
        }

        private static string KeyOf(NeoClient client, string valueId) =>
            client.TypeRules.KeyOf(client.ResolveValueRow(valueId)!.valueType!)!;

        /// <summary>The comparer classes, registered as an export's load registers them.</summary>
        private static void RegisterComparerClasses(ProjectData data)
        {
            JObject classes = JObject.Parse($@"{{
                '{EqualityComparerId}': {{ 'id': '{EqualityComparerId}', 'name': 'EqualityComparer', 'genericParams': [{{ 'id': 'eq-t', 'name': 'T' }}] }},
                '{ReferenceEqualityComparerId}': {{ 'id': '{ReferenceEqualityComparerId}', 'name': 'ReferenceEqualityComparer' }}
            }}");
            NeoSystemComparerClasses.Register(classes);
            data.classes.TryAdd(EqualityComparerId, Class(EqualityComparerId, "EqualityComparer", new Dictionary<string, string>()));
            data.classes[EqualityComparerId].genericParams = new List<GenericParamDeclaration> { new() { id = "eq-t", name = "T" } };
            data.classes.TryAdd(ReferenceEqualityComparerId, Class(ReferenceEqualityComparerId, "ReferenceEqualityComparer", new Dictionary<string, string>()));
        }

        private static NeoClient BuildClient(Action<ProjectData>? configure = null) =>
            NeoTestSaveStack.ClientFromSchema(Schema(configure));

        private static ProjectData Schema(Action<ProjectData>? configure)
        {
            var members = new Dictionary<string, Member>
            {
                ["assets-member"] = Root("assets-member", "assets", NeoMemberStorage.Immutable),
                ["save-member"] = Root("save-member", "save", NeoMemberStorage.Save),
                ["session-member"] = Root("session-member", "session", NeoMemberStorage.Session),
                // Object slots have no runtime node yet, so their rows load as
                // member defaults, which the authored ownership walk visits.
                ["held-field"] = new ObjectMember
                {
                    id = "held-field",
                    projectId = ProjectId,
                    name = "Held",
                    kind = MemberKind.Object,
                    valueId = "asset-held",
                },
                ["boxed-field"] = new ObjectMember
                {
                    id = "boxed-field",
                    projectId = ProjectId,
                    name = "Boxed",
                    kind = MemberKind.Object,
                    objectType = NeoObjectTypeKind.ValueType,
                },
                ["ref-field"] = new LookupMember
                {
                    id = "ref-field",
                    projectId = ProjectId,
                    name = "Ref",
                    kind = MemberKind.Lookup,
                },
                ["numbers-field"] = new SetMember
                {
                    id = "numbers-field",
                    projectId = ProjectId,
                    name = "Numbers",
                    kind = MemberKind.Set,
                    entryMemberId = "numbers-entry",
                    Requirement = NeoMemberRequirementKind.Required,
                },
                ["numbers-entry"] = new IntMember
                {
                    id = "numbers-entry",
                    projectId = ProjectId,
                    name = "Number",
                    kind = MemberKind.Int,
                    Requirement = NeoMemberRequirementKind.Required,
                },
                ["box-items-field"] = new ListMember
                {
                    id = "box-items-field",
                    projectId = ProjectId,
                    name = "Items",
                    kind = MemberKind.List,
                    entryMemberId = "box-items-entry",
                    Requirement = NeoMemberRequirementKind.Required,
                    Modifier = NeoMemberModifierKind.Static,
                    valueId = "box-items",
                },
                ["box-items-entry"] = new IntMember
                {
                    id = "box-items-entry",
                    projectId = ProjectId,
                    name = "Item",
                    kind = MemberKind.Int,
                    Requirement = NeoMemberRequirementKind.Required,
                },
            };
            var values = new Dictionary<string, MemberValue>
            {
                ["assets"] = new ObjectMemberValue
                {
                    id = "assets",
                    classId = "holder-class",
                    value = new Dictionary<string, string>
                    {
                        ["Numbers"] = "asset-numbers",
                    },
                },
                ["save"] = new ObjectMemberValue { id = "save", classId = "holder-class", value = new Dictionary<string, string>() },
                ["session"] = new ObjectMemberValue { id = "session", classId = "empty-class", value = new Dictionary<string, string>() },
                ["asset-held"] = new ArrayMemberValue
                {
                    id = "asset-held",
                    valueType = TestTypeBindings.FromKey("(int,float)"),
                    value = new[] { "asset-held-count", "asset-held-ratio" },
                },
                ["asset-held-count"] = new NumberMemberValue { id = "asset-held-count", value = 3 },
                ["asset-held-ratio"] = new StringMemberValue { id = "asset-held-ratio", value = "NaN" },
                ["asset-numbers"] = new ArrayMemberValue { id = "asset-numbers", value = Array.Empty<string>() },
                ["asset-comparer"] = new NumberMemberValue { id = "asset-comparer", value = 1 },
                ["box-items"] = new ArrayMemberValue { id = "box-items", value = new[] { "box-item-1" } },
                ["box-item-1"] = new NumberMemberValue { id = "box-item-1", value = 1 },
            };
            var data = new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "P127 boxes",
                    rootAssetsMemberId = "assets-member",
                    rootSaveFileMemberId = "save-member",
                    rootSessionMemberId = "session-member",
                },
                members = members,
                values = values,
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["empty-class"] = Class("empty-class", "Empty", new Dictionary<string, string>()),
                    ["holder-class"] = Class("holder-class", "Holder", new Dictionary<string, string>
                    {
                        ["Ref"] = "ref-field",
                        ["Numbers"] = "numbers-field",
                    }),
                    ["slots-class"] = Class("slots-class", "Slots", new Dictionary<string, string>
                    {
                        ["Held"] = "held-field",
                        ["Boxed"] = "boxed-field",
                    }),
                    ["box-class"] = Class("box-class", "Box", new Dictionary<string, string>
                    {
                        ["Items"] = "box-items-field",
                    }),
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
                variantFolders = new Dictionary<string, VariantFolderRecord>(),
            }.WithUserRoot();
            data.classes["box-class"].genericParams = new List<GenericParamDeclaration> { new() { id = BoxParam, name = "T" } };
            // Box<T> : IBox<T>.
            data.interfaces[IBoxId] = new Interface
            {
                id = IBoxId,
                projectId = ProjectId,
                name = "IBox",
                members = new Dictionary<string, InterfaceMember>(),
                genericParams = new List<GenericParamDeclaration> { new() { id = "ibox-t", name = "T" } },
            };
            data.classes["box-class"].implementsInterfaces = new List<InterfaceConstruction>
            {
                new()
                {
                    interfaceId = IBoxId,
                    arguments = new Dictionary<string, TypeBinding>
                    {
                        ["ibox-t"] = TestTypeBindings.Open("box-class", BoxParam),
                    },
                },
            };
            configure?.Invoke(data);
            return data;
        }

        private static ClassMember Root(string id, string valueId, NeoMemberStorage storage) => new()
        {
            id = id,
            projectId = ProjectId,
            name = id,
            kind = MemberKind.Class,
            DeclaredClassId = valueId == "session" ? "empty-class" : "holder-class",
            valueId = valueId,
            Storage = storage,
        };

        private static NeoSchemaClass Class(string id, string name, Dictionary<string, string> schema) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            schema = schema,
        };
    }
}
