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
    public class NeoScriptVectorParityTests
    {
        [Test]
        public void SharedVectorFixtureMatchesExactly()
        {
            var context = new NSGetterEvaluator.Context(BuildClient(), null, null);
            foreach (JObject test in (JArray)JObject.Parse(NeoScriptVectorParityFixture.Json)["evaluateCases"]!)
            {
                string name = (string)test["name"]!;
                if (test["expectedError"] is JToken error)
                {
                    var thrown = Assert.Catch<NSGetterRuntimeError>(() => Evaluate(test, context), name);
                    Assert.AreEqual((string)error, thrown!.Message, name);
                    continue;
                }
                object? result = Evaluate(test, context);
                JToken expected = test["expected"]!;
                if (expected is JObject components)
                {
                    JObject actual = JObject.FromObject(result!);
                    foreach (var component in components.Properties())
                        AssertNumber(component.Value, actual[component.Name]!.Value<double>(), name + "." + component.Name);
                }
                else if (expected.Type == JTokenType.Null)
                    Assert.IsNull(result, name);
                else
                    AssertNumber(expected, Convert.ToDouble(result), name);
            }
        }

        private static void AssertNumber(JToken expected, double actual, string name)
        {
            if (expected.Type != JTokenType.String)
                Assert.AreEqual(expected.Value<double>(), actual, name);
            else if ((string)expected == "NaN")
                Assert.IsTrue(double.IsNaN(actual), name);
            else
                Assert.AreEqual((string)expected == "Infinity" ? double.PositiveInfinity : double.NegativeInfinity, actual, name);
        }

        private static object? Evaluate(JObject test, NSGetterEvaluator.Context context) => Evaluate(
            test["pointer"]!.ToObject<Pointer>()!, test["typeInfo"]!.ToObject<TypeInfo>()!, context);

        private static object? Evaluate(Pointer pointer, TypeInfo type, NSGetterEvaluator.Context? context = null) =>
            NSGetterEvaluator.Evaluate(new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                instructions = new Instruction[] { new ReturnInstruction { type = InstructionKind.Return, pointer = pointer } },
                typeInfo = type,
            }, context ?? new NSGetterEvaluator.Context(BuildClient(), null, null));

        [Test]
        public void CompilerIndexedCompoundEvaluatesIndexAndRightOperandOnce()
        {
            var getter = JsonConvert.DeserializeObject<FunctionWithReturnType>(NeoScriptVectorCompoundFixture.Json)!;
            var calls = new List<string>();
            var ctx = new NSGetterEvaluator.Context(BuildClient(), null, null) { DebugSink = value => calls.Add(value.Message) };
            var result = (NeoVector2Value)NSGetterEvaluator.Evaluate(getter, ctx)!;
            CollectionAssert.AreEqual(new[] { "index", "scalar" }, calls);
            Assert.AreEqual(2d, result.x);
            Assert.AreEqual(4d, result.y);
        }

        [Test]
        public void SerializedIntegerComponentsAndUnityAdaptersKeepPrecision()
        {
            var vector = JsonConvert.DeserializeObject<NeoVector3Value>("{\"x\":16777217,\"y\":-16777217,\"z\":2147483647}")!;
            Assert.AreEqual(16777217d, vector.x);
            var unity = NeoGeneratedTypesSupport.ReadVector3IntValue(vector)!.Value;
            Assert.AreEqual(16777217, unity.x);
            Assert.AreEqual(-16777217, unity.y);
            Assert.AreEqual(int.MaxValue, unity.z);
            var dict = new Dictionary<string, object?> { ["x"] = 16777217d, ["y"] = -16777217d };
            Assert.AreEqual(16777217, NeoGeneratedTypesSupport.ReadVector2IntValue(dict)!.Value.x);
        }

        [TestCase("Color", "add", 2)]
        [TestCase("Vector2Int", "normalized", 1)]
        [TestCase("Vector2", "cross", 2)]
        [TestCase("Vector4", "magnitude", 1)]
        [TestCase("Vector2", "bogus", 1)]
        [TestCase("Vector2", "add", 1)]
        public void InvalidIntrinsicFailsAtDeserialization(string kind, string op, int arity)
        {
            var args = new JArray();
            for (int i = 0; i < arity; i++)
                args.Add(JObject.Parse("{\"type\":\"value\",\"value\":{\"typeInfo\":{\"type\":2,\"required\":true},\"value\":0}}"));
            var function = new JObject { ["type"] = "vectorOp", ["info"] = new JObject { ["kind"] = kind, ["op"] = op, ["argPointers"] = args } };
            Assert.Throws<JsonSerializationException>(() => function.ToObject<Function>());
        }

        [Test]
        public void FractionalIntegerVectorOperandFailsPrecisely()
        {
            var pointer = JObject.Parse("{\"type\":\"function\",\"function\":{\"type\":\"vectorOp\",\"info\":{\"kind\":\"Vector2Int\",\"op\":\"negate\",\"argPointers\":[{\"type\":\"value\",\"value\":{\"typeInfo\":{\"type\":15,\"required\":true},\"value\":{\"x\":1.5,\"y\":2}}}]}}}").ToObject<Pointer>()!;
            var error = Assert.Catch<NSGetterRuntimeError>(() => Evaluate(pointer, new PrimitiveTypeInfo { type = MemberKind.Vector2Int, required = true }));
            Assert.AreEqual("Vector2Int.negate argument 1 component 'x' must be an integer.", error!.Message);
        }

        [Test]
        public void OptionalDerivedPropertyPreservesNull()
        {
            var pointer = JObject.Parse("{\"type\":\"function\",\"function\":{\"type\":\"vectorOp\",\"info\":{\"kind\":\"Vector2\",\"op\":\"normalized\",\"optional\":true,\"argPointers\":[{\"type\":\"value\",\"value\":{\"typeInfo\":{\"type\":14,\"required\":false},\"value\":null}}]}}}").ToObject<Pointer>()!;
            Assert.IsNull(Evaluate(pointer, new PrimitiveTypeInfo { type = MemberKind.Vector2, required = false }));
        }

        private static NeoClient BuildClient()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "class-root",
                projectId = "project-math-parity",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            return NeoTestSaveStack.ClientFromSchema(new ProjectData
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
            }.WithUserRoot());
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
