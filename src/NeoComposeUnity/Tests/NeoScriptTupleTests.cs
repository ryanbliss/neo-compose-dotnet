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
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    public class NeoScriptTupleTests
    {
        public static IEnumerable<TestCaseData> SharedCases()
        {
            foreach (JObject test in JArray.Parse(NeoScriptTupleParityFixture.Json))
                yield return new TestCaseData(test).SetName("Tuple parity: " + (string)test["name"]!);
        }

        [TestCaseSource(nameof(SharedCases))]
        public void SharedCompiledFixtureMatchesExactly(JObject test)
        {
            FunctionWithReturnType getter = test["getter"]!.ToObject<FunctionWithReturnType>()!;
            var context = new NSGetterEvaluator.Context(BuildClient(), null, null);
            object? expected = ((JValue)test["expected"]!).Value;
            Assert.AreEqual(expected, NSGetterEvaluator.Evaluate(getter, context));
        }

        [Test]
        public void ExecutingGenericTupleUsesClosedHandleEquality()
        {
            var test = JObject.Parse(NeoScriptTupleGenericFixture.Json);
            var context = new NSGetterEvaluator.Context(BuildClient(), null, null);
            context.BindFunctionTypeArguments(new TypeInfo[] {
                new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true } }
            });
            object?[] left = { 1d };
            foreach (JToken body in (JArray)test["getters"]!)
            {
                var getter = body.ToObject<FunctionWithReturnType>()!;
                Assert.AreEqual(false, NSGetterEvaluator.Evaluate(getter, context, new object?[] { left, new object?[] { 1d } }));
                Assert.AreEqual(true, NSGetterEvaluator.Evaluate(getter, context, new object?[] { left, left }));
            }
        }

        [Test]
        public void GenericListCapturePreservesMutationsInCaller()
        {
            var test = JObject.Parse(NeoScriptTupleGenericFixture.Json)["capture"]!;
            using NeoClient client = BuildClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            context.BindFunctionTypeArguments(new TypeInfo[] {
                new CollectionTypeInfo { type = MemberKind.List, required = true,
                    entryTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true } }
            });
            var getter = test["getter"]!.ToObject<FunctionWithReturnType>()!;
            Assert.AreEqual(2, NSGetterEvaluator.Evaluate(getter, context, new object?[] { new object?[] { 1d } }));
        }

        [Test]
        public void NativeTupleTypesPreserveNestedAndRestElements()
        {
            NeoClient client = BuildClient();
            var arguments = new NeoTypeArguments(client);
            var tuple = (TupleTypeInfo)arguments.Of<(int, (string, decimal), int, int, int, int, int, int, bool)>();
            Assert.AreEqual(9, tuple.elements.Length);
            Assert.AreEqual(MemberKind.Bool, tuple.elements[8].typeInfo.type);
            Assert.AreEqual(MemberKind.Tuple, tuple.elements[1].typeInfo.type);
            Assert.AreEqual(MemberKind.Decimal, ((TupleTypeInfo)tuple.elements[1].typeInfo).elements[1].typeInfo.type);
            Assert.IsFalse(arguments.Of<(int, int)?>().required);
        }

        [Test]
        public void NativeTuplesRoundTripThroughRecursiveRestAndNullableElements()
        {
            NeoClient client = BuildClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var expected = (1, ("nested", 2.00m), 3, 4, 5, 6, 7, 8, (int?)null);
            TypeInfo type = new NeoTypeArguments(client).Of<(int, (string, decimal), int, int, int, int, int, int, int?)>();
            object? runtime = NeoScriptValueMarshaller.Normalize(client, NeoValueOwnership.Session, expected, type, context, "tuple argument");
            var actual = NeoGeneratedTypesSupport.ReadReferenceValue<(int, (string, decimal), int, int, int, int, int, int, int?)>(client, runtime);
            Assert.AreEqual(expected, actual);
            Assert.IsNull(NeoGeneratedTypesSupport.ReadReferenceValue<(int, int)?>(client, null));
        }

        [Test]
        public void SerializedTupleCapturesRestoreNestedTypes()
        {
            NeoClient client = BuildClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var expected = (4, ("hello", 2.5m));
            TypeInfo type = new NeoTypeArguments(client).Of<(int, (string, decimal))>();
            object? runtime = NeoScriptValueMarshaller.Normalize(client, NeoValueOwnership.Session, expected, type, context, "tuple capture");
            string json = JsonConvert.SerializeObject(runtime);
            Assert.AreEqual("{\"elements\":[4,{\"elements\":[\"hello\",\"2.5\"]}]}", json);
            object? captured = NeoJsonRuntimeValue.From(JToken.Parse(json));
            object? restored = NeoScriptValueMarshaller.Normalize(client, NeoValueOwnership.Session, captured, type, context, "tuple capture");
            Assert.AreEqual(expected, NeoGeneratedTypesSupport.ReadReferenceValue<(int, (string, decimal))>(client, restored));
        }

        [Test]
        public void FunctionGenericsBindInsideTupleArguments()
        {
            var argument = JsonConvert.DeserializeObject<FunctionArgumentTypeInfo>(@"{""name"":""pair"",""type"":30,""required"":true,""elements"":[{""typeInfo"":{""type"":21,""required"":true,""ownerMemberId"":""f"",""index"":0}},{""typeInfo"":{""type"":2,""required"":true}}]}")!;
            Assert.IsTrue(NeoFunctionTypeArguments.Contains(argument));
            var bound = NeoFunctionTypeArguments.BindArgument(argument, new TypeInfo[] { new PrimitiveTypeInfo { type = MemberKind.String, required = true } });
            Assert.AreEqual(MemberKind.String, bound.elements![0].typeInfo.type);
            Assert.AreEqual(MemberKind.Generic, argument.elements![0].typeInfo.type);
        }

        [TestCase("{\"type\":\"tupleElement\",\"info\":{\"index\":-1,\"tuplePointer\":null}}")]
        [TestCase("{\"type\":\"tupleCreate\",\"info\":{\"elementPointers\":[]}}")]
        [TestCase("{\"type\":\"tupleEqual\",\"info\":{}}")]
        public void MalformedTupleOperationsAreRejected(string json)
        {
            Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<Function>(json));
        }

        [TestCase("{\"type\":30,\"required\":true}")]
        [TestCase("{\"type\":30,\"required\":true,\"elements\":[]}")]
        [TestCase("{\"type\":30,\"required\":true,\"elements\":[null,null]}")]
        public void MalformedTupleTypesAreRejected(string json)
        {
            Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<TypeInfo>(json));
        }

        [Test]
        public void StoredTuplesResolveTypedChildrenAndOwnedEdges()
        {
            NeoClient client = BuildClient(storedTuple: true);
            Assert.IsTrue(client.TryGetMember("tuple-member", out JsonMember? member));
            Assert.IsTrue(client.TryGetValue("tuple-value", out MemberValue? row));
            var context = new NSGetterEvaluator.Context(client, null, null);
            var value = (RuntimeTuple)NSGetterEvaluator.UnwrapRow(row!, context, NeoValueOwnership.Asset, member!)!;
            Assert.AreEqual(7d, value[0]);
            Assert.AreEqual(true, value[1]);
            Assert.AreEqual("Count", value.TypeInfo.elements[0].name);
            Assert.IsTrue(client.TryInferMemberForValueId("tuple-int-value", out JsonMember? child));
            Assert.AreEqual("tuple-int", child!.id);
            var owned = new List<string>();
            foreach (var link in client.EnumerateOwnedChildLinks(row!, member))
                owned.Add(link.member!.id);
            CollectionAssert.AreEqual(new[] { "tuple-int", "tuple-bool" }, owned);
        }

        [Test]
        public void TupleAssignmentMaterializesTypedRowsAndStoredSetKeysMatchRuntimeValues()
        {
            NeoClient client = BuildClient(storedTuple: true);
            Assert.IsTrue(client.TryGetMember("tuple-member", out TupleMember? member));
            Assert.IsTrue(client.TryGetValue("tuple-value", out MemberValue? row));
            var context = new NSGetterEvaluator.Context(client, null, null);
            var runtime = (RuntimeTuple)NSGetterEvaluator.UnwrapRow(row!, context, NeoValueOwnership.Asset, member!)!;
            Assert.AreEqual("[[2,\"7\"],[1,\"true\"]]", NeoSetEntryKey.Of(member!, row!, client));
            Assert.AreEqual(NeoSetEntryKey.Of(member!, row!, client), NeoSetEntryKey.OfRuntimeTuple(member!, runtime, context));
            var payload = (NeoValuePayload)NeoGeneratedTypesSupport.MaterializeCollectionAssignment(client, member!, (9, false), NeoValueOwnership.Session, context)!;
            var ids = (string[])payload.value!;
            Assert.AreEqual(2, ids.Length);
            Assert.AreEqual(2, payload.valueRows.Count);
            Assert.AreEqual("[[2,\"9\"],[1,\"false\"]]", NeoSetEntryKey.OfPayload(member!, payload, client));
            Assert.AreEqual(9, ((NumberMemberValue)payload.valueRows[0]).value);
            Assert.AreEqual(false, ((BoolMemberValue)payload.valueRows[1]).value);
            Assert.AreNotEqual("tuple-int-value", ids[0]);
        }

        [TestCase(NeoValueOwnership.Save)]
        [TestCase(NeoValueOwnership.Session)]
        public void WholeTupleWritesPreserveSnapshotsAndSaveReload(NeoValueOwnership ownership)
        {
            NeoClient client = BuildClient(storedTuple: true);
            NeoMemberClassWritable root = ownership == NeoValueOwnership.Save ? client.save : client.session;
            NeoMemberTuple slot = root.Get<NeoMemberTuple>("Pair");
            NeoGeneratedTypesSupport.SetValue(root, "Pair", NeoGeneratedTypesSupport.TupleValue(client, slot, (11, true)));
            var snapshot = NeoGeneratedTypesSupport.ReadReferenceValue<(int, bool)>(client, NeoGeneratedTypesSupport.ReadTupleValue(root.Get<NeoMemberTuple>("Pair")));
            NeoGeneratedTypesSupport.SetValue(root, "Pair", NeoGeneratedTypesSupport.TupleValue(client, root.Get<NeoMemberTuple>("Pair"), (12, false)));
            Assert.AreEqual((11, true), snapshot);
            Assert.AreEqual((12, false), NeoGeneratedTypesSupport.ReadReferenceValue<(int, bool)>(client, NeoGeneratedTypesSupport.ReadTupleValue(root.Get<NeoMemberTuple>("Pair"))));
            if (ownership == NeoValueOwnership.Save)
            {
                NeoClient reloaded = NeoTestSaveStack.ClientFromSchema(client.Synchronizer.Schema, loadedSaveContent: client.SerializeSaveData());
                Assert.AreEqual((12, false), NeoGeneratedTypesSupport.ReadReferenceValue<(int, bool)>(reloaded, NeoGeneratedTypesSupport.ReadTupleValue(reloaded.save.Get<NeoMemberTuple>("Pair"))));
            }
        }

        [Test]
        public void TupleSetKeepsMembershipWhenPendingHandleAttaches()
        {
            NeoClient client = BuildClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var plan = NeoGeneratedTypesSupport.ResolveDetachedClassPlan(client, "class-root")!;
            var pending = new NeoScriptObject(client, plan, context.allocationTracker);
            var type = new TupleTypeInfo
            {
                type = MemberKind.Tuple,
                required = true,
                elements = new[] {
                    new TupleElementTypeInfo { typeInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "class-root" } },
                    new TupleElementTypeInfo { typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true } },
                },
            };
            var tuple = new RuntimeTuple(new object?[] { pending, 7 }, type);
            RuntimeSet set = NSGetterEvaluator.MakeRuntimeSet(new object?[] { tuple }, false, false, context);
            pending.attachedId = "attached-tuple-handle";
            Assert.IsTrue(set.Contains(tuple));
            var alias = new RuntimeTuple(new object?[] { "attached-tuple-handle", 7 }, type);
            Assert.IsTrue(set.Contains(alias));
            set.Add(alias, alias);
            Assert.AreEqual(1, set.Count);
            set.Remove(alias);
            Assert.AreEqual(0, set.Count);
        }

        [Test]
        public void DeepStoredTupleKeysHaveLinearSizeAndReads()
        {
            NeoClient client = BuildClient(storedTuple: true, tupleDepth: 64);
            Assert.IsTrue(client.TryGetMember("deep-63", out JsonMember? member));
            Assert.IsTrue(client.TryGetValue("deep-value-63", out MemberValue? row));
            int reads = 0;
            string key = NeoSetEntryKey.Of(member!, row!, client, id =>
            {
                reads++;
                return client.TryGetValue(id, out MemberValue? value) ? value : null;
            });
            Assert.AreEqual(130, reads);
            Assert.Less(key.Length, 3000);
        }

        private static NeoClient BuildClient(bool storedTuple = false, int tupleDepth = 0)
        {
            var rootClass = new NeoSchemaClass
            {
                id = "class-root",
                projectId = "project-math-parity",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var data = new ProjectData
            {
                project = new Project
                {
                    id = "project-math-parity",
                    _id = "project-math-parity",
                    name = "Math parity",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, JsonMember>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", rootClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id),
                    ["root-save-value"] = ObjectValue("root-save-value", rootClass.id),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
            if (storedTuple)
            {
                rootClass.schema["Pair"] = "tuple-member";
                ((ObjectMemberValue)data.values["root-assets-value"]).value!["Pair"] = "tuple-value";
                data.members["tuple-member"] = new TupleMember
                {
                    id = "tuple-member",
                    projectId = "project-math-parity",
                    name = "Pair",
                    kind = MemberKind.Tuple,
                    elementMemberIds = new[] { "tuple-int", "tuple-bool" },
                };
                data.members["tuple-int"] = new IntMember { id = "tuple-int", projectId = "project-math-parity", name = "Count", kind = MemberKind.Int, Requirement = NeoMemberRequirementKind.Required };
                data.members["tuple-bool"] = new BoolMember { id = "tuple-bool", projectId = "project-math-parity", name = "Item2", kind = MemberKind.Bool, Requirement = NeoMemberRequirementKind.Required };
                data.values["tuple-value"] = new ArrayMemberValue { id = "tuple-value", value = new[] { "tuple-int-value", "tuple-bool-value" } };
                data.values["tuple-int-value"] = new NumberMemberValue { id = "tuple-int-value", value = 7 };
                data.values["tuple-bool-value"] = new BoolMemberValue { id = "tuple-bool-value", value = true };
            }
            string nestedMember = "tuple-member";
            string nestedValue = "tuple-value";
            for (int depth = 0; depth < tupleDepth; depth++)
            {
                string memberId = $"deep-{depth}";
                string valueId = $"deep-value-{depth}";
                data.members[memberId] = new TupleMember { id = memberId, projectId = data.project.id, name = "Nested", kind = MemberKind.Tuple, elementMemberIds = new[] { nestedMember, "tuple-bool" } };
                data.values[valueId] = new ArrayMemberValue { id = valueId, value = new[] { nestedValue, "tuple-bool-value" } };
                nestedMember = memberId;
                nestedValue = valueId;
            }
            return NeoTestSaveStack.ClientFromSchema(data.WithUserRoot());
        }

        private static ClassMember RootMember(string id, string valueId, string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-math-parity",
                name = id,
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = valueId,
                DeclaredClassId = classId,
            };
        }

        private static ObjectMemberValue ObjectValue(string id, string classId)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = new Dictionary<string, string>(),
            };
        }
    }
}
