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
    public class NeoScriptBitwiseTests
    {
        private static readonly TypeInfo IntType = new PrimitiveTypeInfo() { type = MemberKind.Int, required = true };
        private static Pointer Literal(object? value) => new ValuePointer
        {
            type = PointerKind.Value,
            value = new Value { typeInfo = IntType, value = value is null ? JValue.CreateNull() : JToken.FromObject(value) },
        };
        private static object? Evaluate(string op, params Pointer[] args) => NSGetterEvaluator.Evaluate(new FunctionWithReturnType
        {
            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
            parameters = Array.Empty<Variable>(),
            typeInfo = IntType,
            instructions = new Instruction[]
            {
                new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = new FunctionPointer
                    {
                        type = PointerKind.Function,
                        function = new BitwiseOpFunction
                        {
                            type = FunctionKind.BitwiseOp,
                            info = new FunctionBitwiseOpInfo { op = op, argPointers = args },
                        },
                    },
                },
            },
        }, new NSGetterEvaluator.Context(BuildClient(), null, null));

        [Test]
        public void SharedFixtureMatchesExactly()
        {
            foreach (JObject test in JArray.Parse(NeoScriptBitwiseParityFixture.Json))
            {
                var args = new List<Pointer>();
                foreach (JToken arg in (JArray)test["args"]!)
                    args.Add(Literal(arg.Value<double>()));
                Assert.AreEqual(test["expected"]!.Value<double>(), Evaluate((string)test["op"]!, args.ToArray()), (string)test["name"]!);
            }
        }

        [Test]
        public void CompiledCompoundAssignmentsCaptureOnceAndPreserveOldValue()
        {
            var getter = JsonConvert.DeserializeObject<FunctionWithReturnType>(NeoScriptBitwiseCompoundFixture.Json)!;
            var calls = new List<string>();
            var ctx = new NSGetterEvaluator.Context(BuildClient(), null, null) { DebugSink = value => calls.Add(value.Message) };
            Assert.AreEqual(19d, NSGetterEvaluator.Evaluate(getter, ctx));
            CollectionAssert.AreEqual(new[] { "index", "rhs" }, calls);
        }

        [Test]
        public void InvalidOperandsAreRejected()
        {
            foreach (object? value in new object?[] { null, "1", true, 0.5d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Assert.Throws<NSGetterRuntimeError>(() => Evaluate("or", Literal(value), Literal(0)));
        }

        [TestCase("{\"type\":\"bitwiseOp\",\"info\":{\"op\":\"unknown\",\"argPointers\":[]}}")]
        [TestCase("{\"type\":\"bitwiseOp\",\"info\":{\"op\":\"not\",\"argPointers\":[]}}")]
        [TestCase("{\"type\":\"bitwiseOp\",\"info\":{\"op\":\"not\",\"argPointers\":[null]}}")]
        [TestCase("{\"type\":\"bitwiseOp\",\"info\":{\"op\":\"not\",\"argPointers\":[{}]}}")]
        [TestCase("{\"type\":\"bitwiseOp\",\"info\":{\"op\":3,\"argPointers\":[]}}")]
        public void MalformedJsonIsRejected(string json)
        {
            Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<Function>(json));
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
