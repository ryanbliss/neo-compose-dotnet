// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using Assets.Scripts.Neo;
using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Integration coverage for the NSGetter evaluator port. The synth
    /// fixture's three NSGetter attributes
    /// (<c>attr-score</c>, <c>attr-manifest</c>, <c>attr-active</c>)
    /// were authored on the TS side specifically to exercise every
    /// pointer kind, both operations, and the major function variants
    /// (where, count). Running them through
    /// <see cref="NeoAttributeNSGetter.Compute"/> verifies that the
    /// C# evaluator produces the same value the TS evaluator would.
    ///
    /// <para>This isn't comprehensive parity coverage with the TS
    /// 80-case test suite — that's a follow-up. These tests pin the
    /// happy paths through every pointer kind plus a handful of
    /// runtime-error edge cases.</para>
    /// </summary>
    public class NSGetterEvaluatorTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        private static string LoadFixture(string fileName)
        {
            return File.ReadAllText(Path.Combine(PackageRoot, fileName));
        }

        private static NeoClient LoadClient()
        {
            var loader = new NeoLoader();
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;
            return loader.Load(LoadFixture("synth-example.json"), loadSave, handleSave);
        }

        private static NSGetterAttribute RequireNSGetter(NeoClient client, string id)
        {
            if (!client.TryGetAttribute(id, out NSGetterAttribute? attr))
            {
                Assert.Fail($"Fixture is missing NSGetterAttribute '{id}'");
                throw new System.InvalidOperationException("unreachable");
            }
            return attr;
        }

        [Test]
        public void Json_FunctionAttributeAndNativeCallIR_Deserializes()
        {
            var attribute = JsonConvert.DeserializeObject<Attribute>(
                @"{
                    ""id"": ""attr-fn"",
                    ""_id"": ""attr-fn"",
                    ""projectId"": ""test-project"",
                    ""name"": ""BeginAnimation"",
                    ""type"": 13,
                    ""locked"": false,
                    ""required"": false,
                    ""createdAt"": ""2024-01-01T00:00:00.000Z"",
                    ""updatedAt"": ""2024-01-01T00:00:00.000Z"",
                    ""returnTypeInfo"": { ""type"": ""Void"", ""required"": true },
                    ""argumentTypes"": [
                        { ""name"": ""animationName"", ""type"": 3, ""required"": true }
                    ]
                }");

            Assert.IsInstanceOf<FunctionAttribute>(attribute);
            var function = (FunctionAttribute)attribute!;
            Assert.IsInstanceOf<VoidTypeInfo>(function.returnTypeInfo);
            Assert.AreEqual(AttributeType.Void, function.returnTypeInfo.type);
            Assert.AreEqual("animationName", function.argumentTypes[0].name);
            Assert.AreEqual(AttributeType.String, function.argumentTypes[0].type);

            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<TypeInfo>(
                    @"{ ""type"": ""Void"", ""required"": true }"));
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<FunctionArgumentTypeInfo>(
                    @"{ ""name"": ""bad"", ""type"": ""Void"", ""required"": true }"));

            var instruction = JsonConvert.DeserializeObject<Instruction>(
                @"{
                    ""type"": ""nativeCall"",
                    ""call"": {
                        ""type"": ""callNativeFunction"",
                        ""attributeId"": ""attr-fn"",
                        ""thisPointer"": {
                            ""type"": ""value"",
                            ""value"": {
                                ""typeInfo"": { ""type"": 3, ""required"": true },
                                ""value"": ""receiver""
                            }
                        },
                        ""args"": []
                    }
                }");

            Assert.IsInstanceOf<NativeCallInstruction>(instruction);
            Assert.IsInstanceOf<CallNativeFunctionPointer>(
                ((NativeCallInstruction)instruction!).call);
        }

        [Test]
        public void Evaluate_CallNativeFunction_InvokesRegisteredBridge()
        {
            var client = LoadClient();
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["attr-native"] = (_, receiver, args) => $"{receiver}:{args[0]}",
            });
            var getter = new FunctionWithReturnType
            {
                parameters = new Variable[0],
                typeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.String,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new CallNativeFunctionPointer
                        {
                            type = PointerKind.CallNativeFunction,
                            attributeId = "attr-native",
                            thisPointer = StringValuePointer("receiver"),
                            args = new Pointer[] { StringValuePointer("hello") },
                        },
                    },
                },
            };

            var result = NSGetterEvaluator.Evaluate(
                getter,
                new NSGetterEvaluator.Context(client, null, null));

            Assert.AreEqual("receiver:hello", result);
        }

        [Test]
        public void Evaluate_CallNativeFunction_ResolvesGeneratedWrapperAndUsesCachedHandler()
        {
            var client = LoadNativeFunctionClient(out CustomAttribute receiverAttribute);
            var readOnlyFactories =
                new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>
                {
                    ["type-native-receiver"] = (factoryClient, node) =>
                        FunctionTestValue.Create(factoryClient, node),
                };
            var savedFactories =
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>();
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["attr-native-ping"] = (invokeClient, receiver, args) =>
                {
                    var target = NeoGeneratedTypesSupport.ResolveNativeFunctionReceiver<FunctionTestValue>(
                        invokeClient,
                        receiver,
                        readOnlyFactories,
                        savedFactories,
                        "Ping",
                        "attr-native-ping");
                    return target.Ping((string)args[0]!);
                },
            });
            var node = (NeoAttributeCustom)NeoAttribute.Create(
                client,
                receiverAttribute,
                "v-native-receiver");
            var wrapper = FunctionTestValue.Create(client, node);
            var handler = new TestFunctionHandler();
            wrapper.FunctionHandler = handler;
            var getter = new FunctionWithReturnType
            {
                parameters = new Variable[0],
                typeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.String,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new CallNativeFunctionPointer
                        {
                            type = PointerKind.CallNativeFunction,
                            attributeId = "attr-native-ping",
                            thisPointer = new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "v-native-receiver",
                            },
                            args = new Pointer[] { StringValuePointer("hello") },
                        },
                    },
                },
            };

            var result = NSGetterEvaluator.Evaluate(
                getter,
                new NSGetterEvaluator.Context(client, null, null));

            Assert.AreEqual("handled:hello", result);
            Assert.AreSame(
                wrapper,
                FunctionTestValue.Create(client, node),
                "Generated wrapper cache should preserve the assigned FunctionHandler.");
            Assert.AreEqual(1, handler.CallCount);
        }

        [Test]
        public void Evaluate_NativeFunctionErrorCheck_HandlesSuccessAndThrow()
        {
            var client = LoadClient();
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["attr-ok"] = (_, _, _) => null,
                ["attr-throws"] = (_, _, _) => throw new NeoFunctionHandlerMissingException("missing handler"),
            });
            var getter = new FunctionWithReturnType
            {
                parameters = new Variable[0],
                typeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.Bool,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new NativeFunctionErrorCheckPointer
                        {
                            type = PointerKind.NativeFunctionErrorCheck,
                            mode = NativeFunctionErrorCheckKind.Throws,
                            call = new CallNativeFunctionPointer
                            {
                                type = PointerKind.CallNativeFunction,
                                attributeId = "attr-throws",
                                thisPointer = StringValuePointer("receiver"),
                                args = new Pointer[0],
                            },
                        },
                    },
                },
            };

            var result = NSGetterEvaluator.Evaluate(
                getter,
                new NSGetterEvaluator.Context(client, null, null));

            Assert.AreEqual(true, result);
        }

        [Test]
        public void Evaluate_CallNativeFunction_WithoutGeneratedWrapperThrowsClearError()
        {
            var client = LoadClient();
            var getter = new FunctionWithReturnType
            {
                parameters = new Variable[0],
                typeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.Null,
                    required = false,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new CallNativeFunctionPointer
                        {
                            type = PointerKind.CallNativeFunction,
                            attributeId = "attr-native",
                            thisPointer = StringValuePointer("receiver"),
                            args = new Pointer[0],
                        },
                    },
                },
            };

            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    getter,
                    new NSGetterEvaluator.Context(client, null, null)));

            StringAssert.Contains("generated ProjectNeo client wrapper", error!.Message);
        }

        private static ValuePointer StringValuePointer(string value)
        {
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = AttributeType.String,
                        required = true,
                    },
                    value = JToken.FromObject(value),
                },
            };
        }

        // ---------------------------------------------------------------
        // attr-score — exercises the gnarliest IR shape:
        //   local int x = 1 + 2;                       (variable + arithmetic + value)
        //   local string label = (this.Name ?? "Unknown")!;  (forceUnwrap + coalesce + keyOf + value)
        //   if ((label is string) && (x != 0)) {       (boolean op + isCheck + comparison)
        //     return [1,2,3].Where(n => n != 0).Count();  (listLiteral + where + count)
        //   } else { throw "bad"; }
        //   return;                                    (bare return)
        //
        // The fixture binds `__this__` to a Custom of type-hero. We pass
        // an explicit thisValue so the test doesn't rely on the parent-
        // chain walk (covered separately).
        // ---------------------------------------------------------------

        [Test]
        public void Compute_AttrScore_RunsFullIR_ReturnsCount()
        {
            var client = LoadClient();
            var scoreAttr = RequireNSGetter(client, "attr-score");
            var node = new NeoAttributeNSGetter(client, scoreAttr, null);

            // `__this__` is a Custom record with a Name field; the IR
            // reads `this.Name`. v-name is "hero" in the fixture.
            var thisValue = new Dictionary<string, object?>
            {
                { "Name", "v-name" }, // resolves through the schema → attr-name → row v-name
            };

            var result = node.Compute(thisValue);

            Assert.IsTrue(result.ok, $"Expected ok; got error: {result.error}");
            // [1,2,3].Where(n => n != 0).Count() = 3
            Assert.AreEqual(3.0, result.value);
        }

        // ---------------------------------------------------------------
        // attr-manifest — stringify + dictLiteral coverage. The IR is:
        //   return $"{ {[ "k1" ]: 1} }";
        //   → stringify(dictLiteral([{key: "k1", value: 1}]))
        //   → Dictionary<int> formatted via formatForInterp
        //
        // The dictLiteral has no source row to reference-equality-match
        // against, so the formatted output should fall back to
        // "(Dictionary<int>, Value<<unknown>>)".
        // ---------------------------------------------------------------

        [Test]
        public void Compute_AttrManifest_StringifiesDictLiteral()
        {
            var client = LoadClient();
            var manifestAttr = RequireNSGetter(client, "attr-manifest");
            var node = new NeoAttributeNSGetter(client, manifestAttr, null);

            var result = node.Compute();

            Assert.IsTrue(result.ok, $"Expected ok; got error: {result.error}");
            Assert.AreEqual("(Dictionary<int>, Value<<unknown>>)", result.value);
        }

        // ---------------------------------------------------------------
        // attr-active — callGetter + toBool coverage. The IR is:
        //   return Boolean(this.Score);
        //   → toBool(callGetter("attr-score", thisPointer = __this__))
        //
        // attr-score is invoked via dispatchNSGetterById; the result
        // (a number) is coerced to bool via JsTruthy. Number 3 → true.
        // ---------------------------------------------------------------

        [Test]
        public void Compute_AttrActive_DispatchesCallGetterAndCoercesToBool()
        {
            var client = LoadClient();
            var activeAttr = RequireNSGetter(client, "attr-active");
            var node = new NeoAttributeNSGetter(client, activeAttr, null);

            var thisValue = new Dictionary<string, object?>
            {
                { "Name", "v-name" },
            };

            var result = node.Compute(thisValue);

            Assert.IsTrue(result.ok, $"Expected ok; got error: {result.error}");
            Assert.AreEqual(true, result.value);
        }

        [Test]
        public void Evaluate_SyntheticCustomId_ReturnsBackingRowId()
        {
            var client = LoadClient();
            var ctx = new NSGetterEvaluator.Context(client, thisValue: null, rootValue: null);
            var row = new ObjectAttributeValue
            {
                id = "outpost-row",
                typeId = "type-hero",
                createdAt = "x",
                updatedAt = "x",
                value = new Dictionary<string, string>(),
            };
            var thisValue = NSGetterEvaluator.UnwrapRow(row, ctx);
            var getter = new FunctionWithReturnType
            {
                parameters = new Variable[0],
                typeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.String,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new KeyOfPointer
                        {
                            type = PointerKind.KeyOf,
                            keyOf = new KeyOf
                            {
                                pointer = new VariablePointer
                                {
                                    type = PointerKind.Variable,
                                    variableId = "__this__",
                                },
                                key = new ValuePointer
                                {
                                    type = PointerKind.Value,
                                    value = new Value
                                    {
                                        typeInfo = new PrimitiveTypeInfo
                                        {
                                            type = AttributeType.String,
                                            required = true,
                                        },
                                        value = JToken.FromObject("Id"),
                                    },
                                },
                            },
                        },
                    },
                },
            };

            var result = NSGetterEvaluator.Evaluate(getter, ctx.WithThis(thisValue));

            Assert.AreEqual("outpost-row", result);
        }

        [Test]
        public void Evaluate_GeneratedCustomThis_AllowsSchemaMemberAccess()
        {
            var client = LoadClient();
            if (!client.TryGetAttribute("attr-hero", out CustomAttribute? heroAttr))
            {
                Assert.Fail("Fixture is missing attr-hero");
                return;
            }
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "generated-this-row",
                typeId = "type-hero",
                createdAt = "x",
                updatedAt = "x",
                value = new Dictionary<string, string>
                {
                    ["Name"] = "v-str",
                },
            });
            var node = (NeoAttributeCustom)NeoAttribute.Create(
                client,
                heroAttr,
                "generated-this-row");
            var generatedThis = ReadOnlyHero.Create(client, node);
            var getter = ReturnFunction(
                KeyOf(
                    new VariablePointer
                    {
                        type = PointerKind.Variable,
                        variableId = "__this__",
                    },
                    "Name"),
                AttributeType.String);
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: generatedThis,
                rootValue: null);

            var result = NSGetterEvaluator.Evaluate(getter, ctx);

            Assert.AreEqual("hello", result);
        }

        [Test]
        public void Evaluate_GeneratedCustomThis_AllKnownAttributeTypes_ReadOnlyAndWritable()
        {
            var client = LoadGeneratedValueSurfaceClient(
                out CustomAttribute testAttribute,
                out ObjectAttributeValue readOnlyRow,
                out ObjectAttributeValue savedRow);
            var readOnlyNode = (NeoAttributeCustom)NeoAttribute.Create(
                client,
                testAttribute,
                readOnlyRow.id);
            var writableNode = (NeoAttributeCustomWritable)NeoAttribute.CreateWritable(
                client,
                testAttribute,
                savedRow.id);

            AssertGeneratedValueSurface(
                client,
                new TestReadOnlyGeneratedValue(client, readOnlyNode));
            AssertGeneratedValueSurface(
                client,
                new TestGeneratedValue(client, writableNode));
        }

        [Test]
        public void Evaluate_VisitCountAndHasVisited_ReadDialogueMemoryStore()
        {
            var client = LoadClient();
            var memory = new TestMemoryStore();
            var dialogueMemory = memory.GetOrCreateTestDialogueMemory("dialogue-1");
            dialogueMemory.VisitCount = 2;
            var textNodeMemory = (TestTextNodeMemory)dialogueMemory
                .GetOrCreateTextNodeMemory("text-1");
            textNodeMemory.VisitCount = 3;
            textNodeMemory.AddChoice("option-1", "now");
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                memoryStore: memory);

            Assert.AreEqual(
                2,
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(
                        new FunctionPointer
                        {
                            type = PointerKind.Function,
                            function = new VisitCountFunction
                            {
                                type = FunctionKind.VisitCount,
                                info = new FunctionDialogueMemoryInfo
                                {
                                    pointer = StringPointer("dialogue-1"),
                                },
                            },
                        },
                        AttributeType.Int),
                    ctx));
            Assert.AreEqual(
                3,
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(
                        new FunctionPointer
                        {
                            type = PointerKind.Function,
                            function = new VisitCountFunction
                            {
                                type = FunctionKind.VisitCount,
                                info = new FunctionDialogueMemoryInfo
                                {
                                    pointer = StringPointer("dialogue-1,text-1"),
                                },
                            },
                        },
                        AttributeType.Int),
                    ctx));
            Assert.AreEqual(
                true,
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(
                        new FunctionPointer
                        {
                            type = PointerKind.Function,
                            function = new HasVisitedFunction
                            {
                                type = FunctionKind.HasVisited,
                                info = new FunctionDialogueMemoryInfo
                                {
                                    pointer = StringPointer("dialogue-1,text-1,option-1"),
                                },
                            },
                        },
                        AttributeType.Bool),
                    ctx));
            Assert.AreEqual(
                false,
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(
                        new FunctionPointer
                        {
                            type = PointerKind.Function,
                            function = new HasVisitedFunction
                            {
                                type = FunctionKind.HasVisited,
                                info = new FunctionDialogueMemoryInfo
                                {
                                    pointer = StringPointer("dialogue-1,text-1,option-2"),
                                },
                            },
                        },
                        AttributeType.Bool),
                    ctx));
        }

        [Test]
        public void Evaluate_VisitCount_ReturnsZeroForUnknownOrInvalidPointers()
        {
            var client = LoadClient();
            var memory = new TestMemoryStore();
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                memoryStore: memory);

            Assert.AreEqual(
                0,
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(
                        new FunctionPointer
                        {
                            type = PointerKind.Function,
                            function = new VisitCountFunction
                            {
                                type = FunctionKind.VisitCount,
                                info = new FunctionDialogueMemoryInfo
                                {
                                    pointer = StringPointer("missing"),
                                },
                            },
                        },
                        AttributeType.Int),
                    ctx));
            Assert.AreEqual(
                0,
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(
                        new FunctionPointer
                        {
                            type = PointerKind.Function,
                            function = new VisitCountFunction
                            {
                                type = FunctionKind.VisitCount,
                                info = new FunctionDialogueMemoryInfo
                                {
                                    pointer = StringPointer("dialogue-1,,option-1"),
                                },
                            },
                        },
                        AttributeType.Int),
                    ctx));
        }

        // ---------------------------------------------------------------
        // resolvedGetter / resolvedReturnTypeInfo — pin the chain-walk.
        // attr-score has its own getter + returnTypeInfo so resolution
        // shouldn't need to walk anywhere.
        // ---------------------------------------------------------------

        [Test]
        public void ResolvedGetter_ReturnsInstanceGetter_WhenPresent()
        {
            var client = LoadClient();
            var scoreAttr = RequireNSGetter(client, "attr-score");
            var node = new NeoAttributeNSGetter(client, scoreAttr, null);

            Assert.AreSame(scoreAttr.getter, node.resolvedGetter);
        }

        [Test]
        public void ResolvedReturnTypeInfo_ReturnsInstanceTypeInfo_WhenPresent()
        {
            var client = LoadClient();
            var scoreAttr = RequireNSGetter(client, "attr-score");
            var node = new NeoAttributeNSGetter(client, scoreAttr, null);

            Assert.AreSame(scoreAttr.returnTypeInfo, node.resolvedReturnTypeInfo);
            Assert.AreEqual(AttributeType.Int, node.resolvedReturnTypeInfo!.type);
        }

        // ---------------------------------------------------------------
        // Runtime-error paths.
        // ---------------------------------------------------------------

        [Test]
        public void Compute_NoCompiledGetter_ReturnsErrorResult()
        {
            // Synthesize a fresh NSGetterAttribute with no `getter` and
            // no extends chain — simulates an unsaved override.
            var client = LoadClient();
            var attr = new NSGetterAttribute
            {
                id = "test-orphan-getter",
                _id = "test-orphan-getter",
                projectId = "p",
                name = "Orphan",
                type = AttributeType.NSGetter,
                code = "// not compiled",
                returnTypeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.Int,
                    required = true,
                },
                getter = null!,  // simulate "no getter yet"
                createdAt = "x",
                updatedAt = "x",
            };
            var node = new NeoAttributeNSGetter(client, attr, null);

            var result = node.Compute();

            Assert.IsFalse(result.ok);
            Assert.That(result.error, Does.Contain("Compiled `getter`"));
        }

        [Test]
        public void Compute_OptionalChaining_SurvivesNullThis()
        {
            // attr-score reads `this?.Name ?? "Unknown"` — the keyOf
            // is optional, so a null `__this__` short-circuits to null,
            // the coalesce substitutes "Unknown", and the function
            // continues to its tail (which doesn't depend on `this`).
            // Pinning that the optional/coalesce path resolves cleanly
            // without throwing — the TS evaluator's behavior we're
            // mirroring.
            var client = LoadClient();
            var scoreAttr = RequireNSGetter(client, "attr-score");
            var node = new NeoAttributeNSGetter(client, scoreAttr, null);

            var result = node.Compute();  // no thisValue, no parent

            Assert.IsTrue(result.ok, $"Expected ok via optional chaining; got: {result.error}");
            Assert.AreEqual(3.0, result.value);
        }

        [Test]
        public void ForceUnwrap_OnNullValue_ThrowsRuntimeError()
        {
            // Build a tiny getter that just force-unwraps a null literal.
            // Pins the force-unwrap-throws-on-null path that the TS
            // evaluator uses.
            var client = LoadClient();
            var attr = new NSGetterAttribute
            {
                id = "test-force-unwrap-null",
                _id = "test-force-unwrap-null",
                projectId = "p",
                name = "ForceUnwrapNull",
                type = AttributeType.NSGetter,
                code = "// `return (null as string?)!;`",
                returnTypeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.String,
                    required = true,
                },
                getter = new FunctionWithReturnType
                {
                    parameters = new Variable[0],
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = AttributeType.String,
                        required = true,
                    },
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction
                        {
                            type = "return",
                            pointer = new ForceUnwrapPointer
                            {
                                type = "forceUnwrap",
                                pointer = new ValuePointer
                                {
                                    type = "value",
                                    value = new Value
                                    {
                                        typeInfo = new PrimitiveTypeInfo
                                        {
                                            type = AttributeType.String,
                                            required = false,
                                        },
                                        value = null,
                                    },
                                },
                            },
                        },
                    },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            var node = new NeoAttributeNSGetter(client, attr, null);

            var result = node.Compute();

            Assert.IsFalse(result.ok);
            Assert.That(result.error, Does.Contain("force-unwrapping"));
        }

        // ---------------------------------------------------------------
        // Auto-resolution of __this__ from the parent chain.
        //
        // Build a wrapper tree where a Custom record contains an
        // NSGetter as one of its schema-keyed children. When we look
        // up that NSGetter via the parent and Compute() with no
        // explicit thisValue, the evaluator should walk parent up to
        // find the Custom record.
        // ---------------------------------------------------------------

        [Test]
        public void Compute_AutoResolvesThisValue_FromParentChain()
        {
            var client = LoadClient();
            // attr-hero is a Custom of type-hero whose schema has
            // { Name: attr-name, Health: attr-health }. Bind to v-dict
            // (which has `{ Name: "v-name", Level: "v-level" }` —
            // Level isn't in the schema so only Name walks).
            var heroAttr = client.TryGetAttribute("attr-hero", out CustomAttribute? ha)
                ? ha
                : null;
            Assert.IsNotNull(heroAttr);
            var hero = (NeoAttributeCustom)NeoAttribute.Create(client, heroAttr!, "v-dict");

            // Now manually attach an NSGetter child under the hero.
            var scoreAttr = RequireNSGetter(client, "attr-score");
            var nsg = new NeoAttributeNSGetter(client, scoreAttr, null);
            nsg.parent = hero;  // simulates collection-side wiring

            var result = nsg.Compute();  // no explicit thisValue

            Assert.IsTrue(result.ok, $"Expected ok via parent walk; got: {result.error}");
            Assert.AreEqual(3.0, result.value);
        }

        private static FunctionWithReturnType ReturnFunction(
            Pointer pointer,
            AttributeType returnType)
        {
            return new FunctionWithReturnType
            {
                parameters = new Variable[0],
                typeInfo = new PrimitiveTypeInfo
                {
                    type = returnType,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = pointer,
                    },
                },
            };
        }

        private static void AssertGeneratedValueSurface(
            NeoClient client,
            NeoGeneratedCustomValue generated)
        {
            Assert.IsNull(EvaluateThisMember(client, generated, "Null"));
            Assert.AreEqual(true, EvaluateThisMember(client, generated, "Bool"));
            Assert.AreEqual(7.0, EvaluateThisMember(client, generated, "Int"));
            Assert.AreEqual(2.5, EvaluateThisMember(client, generated, "Float"));
            Assert.AreEqual("hello", EvaluateThisMember(client, generated, "String"));
            Assert.AreEqual("computed", EvaluateThisMember(client, generated, "Getter"));

            var list = EvaluateThisMember(client, generated, "List") as object?[];
            Assert.IsNotNull(list);
            Assert.AreEqual(2, list!.Length);
            Assert.AreEqual("v-list-1", list[0]);

            var dictionary = EvaluateThisMember(client, generated, "Dictionary")
                as IDictionary<string, object?>;
            Assert.IsNotNull(dictionary);
            Assert.AreEqual("v-dict-value", dictionary!["first"]);

            Assert.AreEqual(
                "child",
                EvaluatePointer(
                    client,
                    generated,
                    KeyOf(ThisPointer(), "CustomChild", "Text")));

            var selectedEnum = EvaluateThisMember(client, generated, "Enum") as object?[];
            Assert.IsNotNull(selectedEnum);
            Assert.AreEqual("red", selectedEnum![0]);

            var lookup = EvaluateThisMember(client, generated, "LookupSet") as object?[];
            Assert.IsNotNull(lookup);
            Assert.AreEqual(2, lookup!.Length);
            Assert.AreEqual("v-list-2", lookup[1]);
        }

        private static object? EvaluateThisMember(
            NeoClient client,
            NeoGeneratedCustomValue generated,
            string key)
        {
            return EvaluatePointer(client, generated, KeyOf(ThisPointer(), key));
        }

        private static object? EvaluatePointer(
            NeoClient client,
            NeoGeneratedCustomValue generated,
            Pointer pointer)
        {
            return NSGetterEvaluator.Evaluate(
                ReturnFunction(pointer, AttributeType.String),
                new NSGetterEvaluator.Context(
                    client,
                    thisValue: generated,
                    rootValue: null));
        }

        private static NeoClient LoadNativeFunctionClient(
            out CustomAttribute receiverAttribute)
        {
            var functionAttribute = new FunctionAttribute
            {
                id = "attr-native-ping",
                _id = "attr-native-ping",
                projectId = "project-native-function",
                name = "Ping",
                type = AttributeType.Function,
                returnTypeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.String,
                    required = true,
                },
                argumentTypes = new FunctionArgumentTypeInfo[]
                {
                    new FunctionArgumentTypeInfo
                    {
                        name = "message",
                        type = AttributeType.String,
                        required = true,
                    },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            receiverAttribute = CustomAttribute(
                "attr-native-receiver",
                "NativeReceiver",
                "type-native-receiver");
            receiverAttribute.valueId = "v-native-receiver";
            var rootAttribute = CustomAttribute(
                "attr-native-root",
                "Root",
                "type-native-root");
            var rootSaveAttribute = CustomAttribute(
                "attr-native-save",
                "Save",
                "type-native-root");
            var rootSessionAttribute = CustomAttribute(
                "attr-native-session",
                "Session",
                "type-native-root");
            var receiverType = CustomType(
                "type-native-receiver",
                "NativeReceiver",
                new Dictionary<string, string>
                {
                    ["Ping"] = functionAttribute.id,
                });
            var rootType = CustomType(
                "type-native-root",
                "NativeRoot",
                new Dictionary<string, string>());
            var data = new ProjectData
            {
                project = new Project
                {
                    id = "project-native-function",
                    _id = "project-native-function",
                    name = "Native Function",
                    rootAssetsAttributeId = rootAttribute.id,
                    rootSaveFileAttributeId = rootSaveAttribute.id,
                    rootSessionAttributeId = rootSessionAttribute.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                attributes = new Dictionary<string, NeoCompose.Runtime.Json.Attribute>
                {
                    [functionAttribute.id] = functionAttribute,
                    [receiverAttribute.id] = receiverAttribute,
                    [rootAttribute.id] = rootAttribute,
                    [rootSaveAttribute.id] = rootSaveAttribute,
                    [rootSessionAttribute.id] = rootSessionAttribute,
                },
                values = new Dictionary<string, AttributeValue>
                {
                    ["v-native-root"] = ObjectValue(
                        "v-native-root",
                        rootType.id,
                        new Dictionary<string, string>()),
                    ["v-native-receiver"] = ObjectValue(
                        "v-native-receiver",
                        receiverType.id,
                        new Dictionary<string, string>()),
                },
                types = new Dictionary<string, CustomType>
                {
                    [rootType.id] = rootType,
                    [receiverType.id] = receiverType,
                },
            };
            return new NeoClient(data, () => "", _ => { });
        }

        private static NeoClient LoadGeneratedValueSurfaceClient(
            out CustomAttribute testAttribute,
            out ObjectAttributeValue readOnlyRow,
            out ObjectAttributeValue savedRow)
        {
            var childTextAttribute = StringAttribute("attr-child-text", "ChildText");
            var childType = CustomType("type-child", "Child", new Dictionary<string, string>
            {
                ["Text"] = childTextAttribute.id,
            });

            var nullAttribute = NullAttribute("attr-null", "Null");
            var boolAttribute = BoolAttribute("attr-bool", "Bool");
            var intAttribute = IntAttribute("attr-int", "Int");
            var floatAttribute = FloatAttribute("attr-float", "Float");
            var stringAttribute = StringAttribute("attr-string", "String");
            var listEntryAttribute = StringAttribute("attr-list-entry", "ListEntry");
            var listAttribute = ListAttribute("attr-list", "List", listEntryAttribute.id);
            var dictionaryEntryAttribute = StringAttribute("attr-dict-entry", "DictionaryEntry");
            var dictionaryAttribute = DictionaryAttribute(
                "attr-dictionary",
                "Dictionary",
                dictionaryEntryAttribute.id);
            var customChildAttribute = CustomAttribute("attr-custom-child", "CustomChild", childType.id);
            var enumModel = new NeoCompose.Runtime.Json.Enum
            {
                id = "enum-color",
                _id = "enum-color",
                projectId = "project-generated-surface",
                name = "Color",
                options = new Dictionary<string, EnumOption>
                {
                    ["red"] = new EnumOption { text = "Red" },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            var enumAttribute = EnumAttribute("attr-enum", "Enum", enumModel.id);
            var lookupAttribute = LookupAttribute(
                "attr-lookup-set",
                "LookupSet",
                listAttribute.id,
                "v-list");
            var getterAttribute = NSGetterAttribute("attr-getter", "Getter");

            testAttribute = CustomAttribute("attr-test", "Test", "type-test");
            var rootAttribute = CustomAttribute("attr-root", "Root", "type-root");
            var rootSaveAttribute = CustomAttribute("attr-save", "Save", "type-root");
            var testType = CustomType("type-test", "GeneratedSurface", new Dictionary<string, string>
            {
                ["Null"] = nullAttribute.id,
                ["Bool"] = boolAttribute.id,
                ["Int"] = intAttribute.id,
                ["Float"] = floatAttribute.id,
                ["String"] = stringAttribute.id,
                ["List"] = listAttribute.id,
                ["Dictionary"] = dictionaryAttribute.id,
                ["CustomChild"] = customChildAttribute.id,
                ["Enum"] = enumAttribute.id,
                ["LookupSet"] = lookupAttribute.id,
                ["Getter"] = getterAttribute.id,
            });
            var rootType = CustomType("type-root", "Root", new Dictionary<string, string>());

            var values = new Dictionary<string, AttributeValue>
            {
                ["v-assets"] = ObjectValue("v-assets", "type-root", new Dictionary<string, string>()),
                ["v-null"] = NullValue("v-null"),
                ["v-bool"] = BoolValue("v-bool", true),
                ["v-int"] = NumberValue("v-int", 7),
                ["v-float"] = NumberValue("v-float", 2.5),
                ["v-string"] = StringValue("v-string", "hello"),
                ["v-list-1"] = StringValue("v-list-1", "first"),
                ["v-list-2"] = StringValue("v-list-2", "second"),
                ["v-list"] = ArrayValue("v-list", "v-list-1", "v-list-2"),
                ["v-dict-value"] = StringValue("v-dict-value", "dict"),
                ["v-dictionary"] = ObjectValue(
                    "v-dictionary",
                    null,
                    new Dictionary<string, string>
                    {
                        ["first"] = "v-dict-value",
                    }),
                ["v-child-text"] = StringValue("v-child-text", "child"),
                ["v-child"] = ObjectValue(
                    "v-child",
                    childType.id,
                    new Dictionary<string, string>
                    {
                        ["Text"] = "v-child-text",
                    }),
                ["v-enum"] = ArrayValue("v-enum", "red"),
                ["v-lookup"] = ArrayValue("v-lookup", "v-list-1", "v-list-2"),
            };
            readOnlyRow = ObjectValue(
                "v-readonly-test",
                testType.id,
                GeneratedValueSurfaceMap());
            savedRow = ObjectValue(
                "v-saved-test",
                testType.id,
                GeneratedValueSurfaceMap());
            values[readOnlyRow.id] = readOnlyRow;
            var data = new ProjectData
            {
                project = new Project
                {
                    id = "project-generated-surface",
                    _id = "project-generated-surface",
                    name = "Generated Surface",
                    rootAssetsAttributeId = rootAttribute.id,
                    rootSaveFileAttributeId = rootSaveAttribute.id,
                    rootSessionAttributeId = rootSaveAttribute.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                attributes = new Dictionary<string, NeoCompose.Runtime.Json.Attribute>
                {
                    [rootAttribute.id] = rootAttribute,
                    [rootSaveAttribute.id] = rootSaveAttribute,
                    [testAttribute.id] = testAttribute,
                    [nullAttribute.id] = nullAttribute,
                    [boolAttribute.id] = boolAttribute,
                    [intAttribute.id] = intAttribute,
                    [floatAttribute.id] = floatAttribute,
                    [stringAttribute.id] = stringAttribute,
                    [listEntryAttribute.id] = listEntryAttribute,
                    [listAttribute.id] = listAttribute,
                    [dictionaryEntryAttribute.id] = dictionaryEntryAttribute,
                    [dictionaryAttribute.id] = dictionaryAttribute,
                    [customChildAttribute.id] = customChildAttribute,
                    [childTextAttribute.id] = childTextAttribute,
                    [enumAttribute.id] = enumAttribute,
                    [lookupAttribute.id] = lookupAttribute,
                    [getterAttribute.id] = getterAttribute,
                },
                values = values,
                types = new Dictionary<string, CustomType>
                {
                    [rootType.id] = rootType,
                    [testType.id] = testType,
                    [childType.id] = childType,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>
                {
                    [enumModel.id] = enumModel,
                },
            };
            var client = new NeoClient(data, () => "", _ => { });
            client.SetSaveValue(savedRow);
            return client;
        }

        private static Dictionary<string, string> GeneratedValueSurfaceMap()
        {
            return new Dictionary<string, string>
            {
                ["Null"] = "v-null",
                ["Bool"] = "v-bool",
                ["Int"] = "v-int",
                ["Float"] = "v-float",
                ["String"] = "v-string",
                ["List"] = "v-list",
                ["Dictionary"] = "v-dictionary",
                ["CustomChild"] = "v-child",
                ["Enum"] = "v-enum",
                ["LookupSet"] = "v-lookup",
            };
        }

        private static ValuePointer StringPointer(string value)
        {
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = AttributeType.String,
                        required = true,
                    },
                    value = JToken.FromObject(value),
                },
            };
        }

        private static KeyOfPointer KeyOf(Pointer receiver, string key)
        {
            return new KeyOfPointer
            {
                type = PointerKind.KeyOf,
                keyOf = new KeyOf
                {
                    pointer = receiver,
                    key = StringPointer(key),
                },
            };
        }

        private static KeyOfPointer KeyOf(Pointer receiver, string firstKey, string secondKey)
        {
            return KeyOf(KeyOf(receiver, firstKey), secondKey);
        }

        private static VariablePointer ThisPointer()
        {
            return new VariablePointer
            {
                type = PointerKind.Variable,
                variableId = "__this__",
            };
        }

        private static CustomType CustomType(
            string id,
            string name,
            Dictionary<string, string> schema)
        {
            return new CustomType
            {
                id = id,
                _id = id,
                projectId = "project-generated-surface",
                name = name,
                schema = schema,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static NullAttribute NullAttribute(string id, string name)
        {
            return new NullAttribute
            {
                id = id,
                _id = id,
                projectId = "project-generated-surface",
                name = name,
                type = AttributeType.Null,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static BoolAttribute BoolAttribute(string id, string name)
        {
            return new BoolAttribute
            {
                id = id,
                _id = id,
                projectId = "project-generated-surface",
                name = name,
                type = AttributeType.Bool,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static IntAttribute IntAttribute(string id, string name)
        {
            return new IntAttribute
            {
                id = id,
                _id = id,
                projectId = "project-generated-surface",
                name = name,
                type = AttributeType.Int,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static FloatAttribute FloatAttribute(string id, string name)
        {
            return new FloatAttribute
            {
                id = id,
                _id = id,
                projectId = "project-generated-surface",
                name = name,
                type = AttributeType.Float,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static StringAttribute StringAttribute(string id, string name)
        {
            return new StringAttribute
            {
                id = id,
                _id = id,
                projectId = "project-generated-surface",
                name = name,
                type = AttributeType.String,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static ListAttribute ListAttribute(
            string id,
            string name,
            string entryAttributeId)
        {
            return new ListAttribute
            {
                id = id,
                _id = id,
                projectId = "project-generated-surface",
                name = name,
                type = AttributeType.List,
                entryAttributeId = entryAttributeId,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static DictionaryAttribute DictionaryAttribute(
            string id,
            string name,
            string entryAttributeId)
        {
            return new DictionaryAttribute
            {
                id = id,
                _id = id,
                projectId = "project-generated-surface",
                name = name,
                type = AttributeType.Dictionary,
                entryAttributeId = entryAttributeId,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static CustomAttribute CustomAttribute(
            string id,
            string name,
            string customTypeId)
        {
            return new CustomAttribute
            {
                id = id,
                _id = id,
                projectId = "project-generated-surface",
                name = name,
                type = AttributeType.Custom,
                customTypeId = customTypeId,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static EnumAttribute EnumAttribute(
            string id,
            string name,
            string enumId)
        {
            return new EnumAttribute
            {
                id = id,
                _id = id,
                projectId = "project-generated-surface",
                name = name,
                type = AttributeType.Enum,
                enumId = enumId,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static LookupAttribute LookupAttribute(
            string id,
            string name,
            string collectionAttributeId,
            string collectionValueId)
        {
            return new LookupAttribute
            {
                id = id,
                _id = id,
                projectId = "project-generated-surface",
                name = name,
                type = AttributeType.Lookup,
                collectionAttributeId = collectionAttributeId,
                collectionValueId = collectionValueId,
                multiselect = true,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static NSGetterAttribute NSGetterAttribute(string id, string name)
        {
            return new NSGetterAttribute
            {
                id = id,
                _id = id,
                projectId = "project-generated-surface",
                name = name,
                type = AttributeType.NSGetter,
                code = "return \"computed\";",
                returnTypeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.String,
                    required = true,
                },
                getter = ReturnFunction(StringPointer("computed"), AttributeType.String),
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static NullAttributeValue NullValue(string id)
        {
            return new NullAttributeValue
            {
                id = id,
                createdAt = "x",
                updatedAt = "x",
                value = null,
            };
        }

        private static BoolAttributeValue BoolValue(string id, bool value)
        {
            return new BoolAttributeValue
            {
                id = id,
                createdAt = "x",
                updatedAt = "x",
                value = value,
            };
        }

        private static NumberAttributeValue NumberValue(string id, double value)
        {
            return new NumberAttributeValue
            {
                id = id,
                createdAt = "x",
                updatedAt = "x",
                value = value,
            };
        }

        private static StringAttributeValue StringValue(string id, string value)
        {
            return new StringAttributeValue
            {
                id = id,
                createdAt = "x",
                updatedAt = "x",
                value = value,
            };
        }

        private static ArrayAttributeValue ArrayValue(string id, params string[] value)
        {
            return new ArrayAttributeValue
            {
                id = id,
                createdAt = "x",
                updatedAt = "x",
                value = value,
            };
        }

        private static ObjectAttributeValue ObjectValue(
            string id,
            string? typeId,
            Dictionary<string, string> value)
        {
            return new ObjectAttributeValue
            {
                id = id,
                typeId = typeId,
                createdAt = "x",
                updatedAt = "x",
                value = value,
            };
        }

        private sealed class TestReadOnlyGeneratedValue : NeoGeneratedCustomValue
        {
            public TestReadOnlyGeneratedValue(NeoClient client, NeoAttributeCustom node)
                : base(client, node, "type-test")
            {
            }
        }

        private sealed class TestGeneratedValue : NeoGeneratedCustomValue
        {
            public TestGeneratedValue(NeoClient client, NeoAttributeCustomWritable node)
                : base(client, node, "type-test")
            {
            }
        }

        private interface IFunctionTestValueFunctionHandler
        {
            string Ping(string message);
        }

        private sealed class TestFunctionHandler : IFunctionTestValueFunctionHandler
        {
            public int CallCount { get; private set; }

            public string Ping(string message)
            {
                CallCount += 1;
                return $"handled:{message}";
            }
        }

        private sealed class FunctionTestValue : NeoGeneratedCustomValue
        {
            private FunctionTestValue(NeoClient client, NeoAttributeCustom node)
                : base(client, node, "type-native-receiver")
            {
            }

            public IFunctionTestValueFunctionHandler? FunctionHandler
            {
                get => FunctionHandlerObject as IFunctionTestValueFunctionHandler;
                set => FunctionHandlerObject = value;
            }

            public static FunctionTestValue Create(
                NeoClient client,
                NeoAttributeCustom node)
            {
                return NeoGeneratedTypesSupport.GetOrCreateGeneratedCustomValue(
                    client,
                    node,
                    () => new FunctionTestValue(client, node));
            }

            public string Ping(string message)
            {
                if (FunctionHandler is null)
                {
                    throw new NeoFunctionHandlerMissingException(
                        "Cannot invoke Function 'Ping' on FunctionTestValue because FunctionHandler is not set.");
                }
                return FunctionHandler.Ping(message);
            }
        }

        private sealed class TestMemoryStore : INeoDialogueMemoryStore
        {
            private readonly Dictionary<string, TestDialogueMemory> dialogues = new();

            public TestDialogueMemory GetOrCreateTestDialogueMemory(string dialogueId)
            {
                return (TestDialogueMemory)GetOrCreateDialogueMemory(dialogueId);
            }

            public INeoDialogueMemory GetOrCreateDialogueMemory(string dialogueId)
            {
                if (!dialogues.TryGetValue(dialogueId, out TestDialogueMemory memory))
                {
                    memory = new TestDialogueMemory();
                    dialogues[dialogueId] = memory;
                }
                return memory;
            }

            public INeoDialogueMemory? FindDialogueMemory(string dialogueId)
            {
                return dialogues.TryGetValue(dialogueId, out TestDialogueMemory memory)
                    ? memory
                    : null;
            }
        }

        private sealed class TestDialogueMemory : INeoDialogueMemory
        {
            private readonly Dictionary<string, TestTextNodeMemory> textNodes = new();

            public int VisitCount { get; set; }
            public string? LastVisitedAt { get; set; }

            public INeoTextNodeMemory GetOrCreateTextNodeMemory(string textNodeId)
            {
                if (!textNodes.TryGetValue(textNodeId, out TestTextNodeMemory memory))
                {
                    memory = new TestTextNodeMemory();
                    textNodes[textNodeId] = memory;
                }
                return memory;
            }

            public INeoTextNodeMemory? FindTextNodeMemory(string textNodeId)
            {
                return textNodes.TryGetValue(textNodeId, out TestTextNodeMemory memory)
                    ? memory
                    : null;
            }
        }

        private sealed class TestTextNodeMemory : INeoTextNodeMemory
        {
            private readonly HashSet<string> choices = new();

            public int VisitCount { get; set; }
            public string? LastVisitedAt { get; set; }
            public string? MostRecentChoiceId { get; set; }

            public bool HasChoice(string choiceId)
            {
                return choices.Contains(choiceId);
            }

            public void AddChoice(string choiceId, string createdAt)
            {
                choices.Add(choiceId);
            }
        }
    }
}
