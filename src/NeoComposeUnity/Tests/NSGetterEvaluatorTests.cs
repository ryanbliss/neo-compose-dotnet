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
    /// Integration coverage for the NSProperty evaluator port. The synth
    /// fixture's three NSProperty members
    /// (<c>member-score</c>, <c>member-manifest</c>, <c>member-active</c>)
    /// were authored on the TS side specifically to exercise every
    /// pointer kind, both operations, and the major function variants
    /// (where, count). Running them through
    /// <see cref="NeoMemberNSProperty.Compute"/> verifies that the
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
            return NeoTestSaveStack.LoadClient(LoadFixture("synth-example.json"));
        }

        private static NSPropertyMember RequireNSGetter(NeoClient client, string id)
        {
            if (!client.TryGetMember(id, out NSPropertyMember? member))
            {
                Assert.Fail($"Fixture is missing NSPropertyMember '{id}'");
                throw new System.InvalidOperationException("unreachable");
            }
            return member;
        }

        private static T RequireMember<T>(NeoClient client, string id)
            where T : NeoCompose.Runtime.Json.Member
        {
            if (!client.TryGetMember(id, out T? member))
            {
                Assert.Fail($"Fixture is missing member '{id}' of type {typeof(T).Name}");
                throw new System.InvalidOperationException("unreachable");
            }
            return member;
        }

        [Test]
        public void ConditionalPointer_RoundTripsAndDoesNotEvaluateTheUnselectedBranch()
        {
            var pointer = JsonConvert.DeserializeObject<Pointer>(
                @"{
                    'type':'conditional',
                    'condition':{'type':'value','value':{'typeInfo':{'type':1,'required':true},'value':true}},
                    'whenTrue':{'type':'value','value':{'typeInfo':{'type':3,'required':true},'value':'chosen'}},
                    'whenFalse':{'type':'forceUnwrap','pointer':{'type':'value','value':{'typeInfo':{'type':3,'required':false},'value':null}}}
                }");

            Assert.IsInstanceOf<ConditionalPointer>(pointer);
            var conditional = (ConditionalPointer)pointer!;
            Assert.IsInstanceOf<ValuePointer>(conditional.condition);
            Assert.IsInstanceOf<ForceUnwrapPointer>(conditional.whenFalse);

            FunctionWithReturnType getter = ReturnFunction(
                conditional,
                MemberKind.String);
            getter.compilerRevision = 12;
            object? result = NSGetterEvaluator.Evaluate(
                getter,
                new NSGetterEvaluator.Context(
                    LoadClient(),
                    thisValue: null,
                    rootValue: null));

            Assert.AreEqual("chosen", result);
        }

        [Test]
        public void GenericEqualsPointer_RoundTripsAndRejectsMalformedFallback()
        {
            const string json = @"{
                'type':'callFunction',
                'memberKey':'Equals',
                'receiver':{
                    'kind':'instance',
                    'pointer':{'type':'value','value':{'typeInfo':{'type':2,'required':true},'value':7}}
                },
                'args':[{'type':'value','value':{'typeInfo':{'type':2,'required':true},'value':7}}],
                'missingMemberFallback':'valueEquality',
                'callSiteId':'generic-equals-json'
            }";

            var pointer = JsonConvert.DeserializeObject<Pointer>(json);
            Assert.IsInstanceOf<CallFunctionPointer>(pointer);
            Assert.AreEqual(
                "valueEquality",
                ((CallFunctionPointer)pointer!).missingMemberFallback);

            string malformed = json.Replace(
                "'args':[{'type':'value','value':{'typeInfo':{'type':2,'required':true},'value':7}}]",
                "'args':[]");
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<Pointer>(malformed));
        }

        [Test]
        public void Json_FunctionMemberAndGeneralCallIR_Deserializes()
        {
            var member = JsonConvert.DeserializeObject<Member>(
                @"{
                    ""id"": ""member-fn"",
                    ""_id"": ""member-fn"",
                    ""projectId"": ""test-project"",
                    ""name"": ""BeginAnimation"",
                    ""kind"": 13,
                    ""locked"": false,
                    ""required"": false,
                    ""isStatic"": false, ""accessModifierKind"": ""public"",
                    ""createdAt"": ""2024-01-01T00:00:00.000Z"",
                    ""updatedAt"": ""2024-01-01T00:00:00.000Z"",
                    ""returnTypeInfo"": { ""type"": ""Void"", ""required"": true },
                    ""argumentTypes"": [
                        { ""name"": ""animationName"", ""type"": 3, ""required"": true }
                    ],
                    ""deferred"": false
                }");

            Assert.IsInstanceOf<FunctionMember>(member);
            var function = (FunctionMember)member!;
            Assert.IsInstanceOf<VoidTypeInfo>(function.returnTypeInfo);
            Assert.AreEqual(MemberKind.Void, function.returnTypeInfo.type);
            Assert.AreEqual("animationName", function.argumentTypes[0].name);
            Assert.AreEqual(MemberKind.String, function.argumentTypes[0].type);
            Assert.AreEqual(false, function.deferred);

            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<TypeInfo>(
                    @"{ ""type"": ""Void"", ""required"": true }"));
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<FunctionArgumentTypeInfo>(
                    @"{ ""name"": ""bad"", ""type"": ""Void"", ""required"": true }"));

            var instruction = JsonConvert.DeserializeObject<Instruction>(
                @"{
                    ""type"": ""functionCall"",
                    ""call"": {
                        ""type"": ""callFunction"",
                        ""memberId"": ""member-fn"",
                        ""callSiteId"": ""body:1:1#0"",
                        ""receiver"": {
                            ""kind"": ""instance"",
                            ""pointer"": {
                                ""type"": ""value"",
                                ""value"": {
                                    ""typeInfo"": { ""type"": 3, ""required"": true },
                                    ""value"": ""receiver""
                                }
                            }
                        },
                        ""args"": []
                    }
                }");

            Assert.IsInstanceOf<FunctionCallInstruction>(instruction);
            Assert.IsInstanceOf<CallFunctionPointer>(
                ((FunctionCallInstruction)instruction!).call);
            Assert.AreEqual(
                "body:1:1#0",
                ((FunctionCallInstruction)instruction).call.callSiteId);

            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<Instruction>(
                    @"{ ""type"": ""nativeCall"", ""call"": {} }"));
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<Pointer>(
                    @"{
                        ""type"": ""callNativeFunction"",
                        ""memberId"": ""member-fn"",
                        ""thisPointer"": { ""type"": ""variable"", ""variableId"": ""__this__"" },
                        ""args"": []
                    }"));
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<Pointer>(
                    @"{
                        ""type"": ""nativeFunctionErrorCheck"",
                        ""mode"": ""throws"",
                        ""call"": {}
                    }"));
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<Pointer>(
                    @"{
                        ""type"": ""callFunction"",
                        ""memberId"": ""member-fn"",
                        ""callSiteId"": ""body:1:1#0"",
                        ""receiver"": { ""kind"": ""instance"", ""pointer"": { ""type"": ""variable"", ""variableId"": ""__this__"" } }
                    }"));
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<Pointer>(
                    @"{
                        ""type"": ""callFunction"",
                        ""memberId"": ""member-fn"",
                        ""callSiteId"": ""body:1:1#0"",
                        ""args"": []
                    }"));
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<Pointer>(
                    @"{
                        ""type"": ""functionErrorCheck"",
                        ""mode"": ""sometimes"",
                        ""call"": {
                            ""type"": ""callFunction"",
                            ""memberId"": ""member-fn"",
                            ""callSiteId"": ""body:1:1#0"",
                            ""receiver"": { ""kind"": ""instance"", ""pointer"": { ""type"": ""variable"", ""variableId"": ""__this__"" } },
                            ""args"": []
                        }
                    }"));
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<Instruction>(
                    @"{ ""type"": ""functionCall"" }"));
        }

        [Test]
        public void Json_ClassCloneFunctionIR_DeserializesExactNeoSchemaClass()
        {
            var pointer = JsonConvert.DeserializeObject<Pointer>(
                @"{
                    ""type"": ""function"",
                    ""function"": {
                        ""type"": ""classClone"",
                        ""info"": {
                            ""receiverPointer"": {
                                ""type"": ""variable"",
                                ""variableId"": ""__this__""
                            },
                            ""schemaClassInfo"": {
                                ""type"": 7,
                                ""required"": true,
                                ""classId"": ""class-hero""
                            }
                        }
                    }
                }");

            Assert.IsInstanceOf<FunctionPointer>(pointer);
            var clone = ((FunctionPointer)pointer!).function as ClassCloneFunction;
            Assert.IsNotNull(clone);
            Assert.AreEqual("class-hero", clone!.info.schemaClassInfo.classId);
            Assert.IsTrue(clone.info.schemaClassInfo.required);
        }

        [Test]
        public void Evaluate_ClassClone_ReturnsFreshParentlessSessionValue()
        {
            var client = LoadClient();
            var sourceRow = new ObjectMemberValue
            {
                id = "clone-source",
                classId = "class-hero",
                createdAt = "x",
                updatedAt = "x",
                value = new Dictionary<string, string>(),
            };
            client.SetSaveValue(sourceRow);
            var context = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: NeoValueOwnership.Save);
            var source = NSGetterEvaluator.UnwrapRow(
                sourceRow,
                context,
                NeoValueOwnership.Save);
            var getter = new FunctionWithReturnType
            {
                parameters = System.Array.Empty<Variable>(),
                typeInfo = new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = true,
                    classId = "class-hero",
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new FunctionPointer
                        {
                            type = PointerKind.Function,
                            function = new ClassCloneFunction
                            {
                                type = FunctionKind.ClassClone,
                                info = new FunctionClassCloneInfo
                                {
                                    receiverPointer = new VariablePointer
                                    {
                                        type = PointerKind.Variable,
                                        variableId = "__this__",
                                    },
                                    schemaClassInfo = new ClassTypeInfo
                                    {
                                        type = MemberKind.Class,
                                        required = true,
                                        classId = "class-hero",
                                    },
                                },
                            },
                        },
                    },
                },
            };

            var result = NSGetterEvaluator.Evaluate(
                getter,
                context.WithThis(source));
            string? cloneId = NSGetterEvaluator.FindRowIdByReference(
                result,
                context);

            Assert.IsNotNull(cloneId);
            Assert.AreNotEqual(sourceRow.id, cloneId);
            Assert.IsTrue(client.TryGetValueOwnership(
                cloneId!,
                out NeoValueOwnership ownership));
            Assert.AreEqual(NeoValueOwnership.Session, ownership);
            Assert.AreEqual(0, sourceRow.value!.Count);
        }

        [Test]
        public void Evaluate_CallNativeFunction_InvokesRegisteredBridge()
        {
            var client = LoadClient();
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["member-native"] = (_, receiver, args) => $"{receiver}:{args[0]}",
            });
            var getter = new FunctionWithReturnType
            {
                parameters = new Variable[0],
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.String,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new CallFunctionPointer
                        {
                            type = PointerKind.CallFunction,
                            memberId = "member-native",
                            receiver = CallReceiver.Instance(StringValuePointer("receiver")),
                            args = new Pointer[] { StringValuePointer("hello") },
                            callSiteId = "native-bridge",
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
        public void Evaluate_InterfaceFunctionMember_ResolvesRuntimeSchemaKey()
        {
            var client = LoadNativeFunctionClient(out _);
            client.RegisterNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    ["member-native-ping"] = (_, _, args) => $"dynamic:{args[0]}",
                });
            var getter = new FunctionWithReturnType
            {
                parameters = System.Array.Empty<Variable>(),
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.String,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new CallFunctionPointer
                        {
                            type = PointerKind.CallFunction,
                            memberKey = "Ping",
                            receiver = CallReceiver.Instance(new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "v-native-receiver",
                            }),
                            args = new Pointer[] { StringValuePointer("hello") },
                            callSiteId = "interface-ping",
                        },
                    },
                },
            };

            var result = NSGetterEvaluator.Evaluate(
                getter,
                new NSGetterEvaluator.Context(client, null, null));

            Assert.AreEqual("dynamic:hello", result);
        }

        [Test]
        public void Evaluate_CallFunctionRejectsStaleArityBeforeNativeInvoker()
        {
            var client = LoadNativeFunctionClient(out _);
            int invocationCount = 0;
            client.RegisterNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    ["member-native-ping"] = (_, _, _) =>
                    {
                        invocationCount++;
                        return "unexpected";
                    },
                });
            var call = new CallFunctionPointer
            {
                type = PointerKind.CallFunction,
                memberId = "member-native-ping",
                receiver = CallReceiver.Instance(new ReferencePointer
                {
                    type = PointerKind.Reference,
                    valueId = "v-native-receiver",
                }),
                args = System.Array.Empty<Pointer>(),
                callSiteId = "bad-arity",
            };

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(call, MemberKind.String),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("Function 'Ping' (member-native-ping) expects 1 arguments", error.Message);
            StringAssert.Contains("stale/corrupt", error.Message);
            Assert.AreEqual(0, invocationCount);
        }

        [Test]
        public void Evaluate_CallFunctionRejectsWrongArgumentShapeBeforeNativeInvoker()
        {
            var client = LoadNativeFunctionClient(out _);
            int invocationCount = 0;
            client.RegisterNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    ["member-native-ping"] = (_, _, _) =>
                    {
                        invocationCount++;
                        return "unexpected";
                    },
                });
            var call = new CallFunctionPointer
            {
                type = PointerKind.CallFunction,
                memberId = "member-native-ping",
                receiver = CallReceiver.Instance(new ReferencePointer
                {
                    type = PointerKind.Reference,
                    valueId = "v-native-receiver",
                }),
                args = new Pointer[]
                {
                    new ValuePointer
                    {
                        type = PointerKind.Value,
                        value = new Value
                        {
                            typeInfo = new PrimitiveTypeInfo
                            {
                                type = MemberKind.Int,
                                required = true,
                            },
                            value = JToken.FromObject(7),
                        },
                    },
                },
                callSiteId = "bad-shape",
            };

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(call, MemberKind.String),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("argument 0 'message'", error.Message);
            StringAssert.Contains("declared String", error.Message);
            StringAssert.Contains("stale/corrupt", error.Message);
            Assert.AreEqual(0, invocationCount);
        }

        [Test]
        public void Evaluate_CallNativeFunction_ResolvesGeneratedWrapperAndUsesCachedHandler()
        {
            var client = LoadNativeFunctionClient(out ClassMember receiverMember);
            var readOnlyFactories =
                new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
                {
                    ["class-native-receiver"] = (factoryClient, node) =>
                        FunctionTestValue.Create(factoryClient, node),
                };
            var savedFactories =
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>();
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["member-native-ping"] = (invokeClient, receiver, args) =>
                {
                    var target = NeoGeneratedTypesSupport.ResolveNativeFunctionReceiver<FunctionTestValue>(
                        invokeClient,
                        receiver,
                        readOnlyFactories,
                        savedFactories,
                        "Ping",
                        "member-native-ping");
                    return target.Ping((string)args[0]!);
                },
            });
            var node = (NeoMemberClass)NeoMember.Create(
                client,
                receiverMember,
                "v-native-receiver");
            var wrapper = FunctionTestValue.Create(client, node);
            var handler = new TestFunctionHandler();
            wrapper.FunctionHandler = handler;
            var getter = new FunctionWithReturnType
            {
                parameters = new Variable[0],
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.String,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new CallFunctionPointer
                        {
                            type = PointerKind.CallFunction,
                            memberId = "member-native-ping",
                            receiver = CallReceiver.Instance(new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "v-native-receiver",
                            }),
                            args = new Pointer[] { StringValuePointer("hello") },
                            callSiteId = "generated-ping",
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
                ["member-ok"] = (_, _, _) => null,
                ["member-throws"] = (_, _, _) => throw new NeoFunctionHandlerMissingException("missing handler"),
            });
            var getter = new FunctionWithReturnType
            {
                parameters = new Variable[0],
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.Bool,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new FunctionErrorCheckPointer
                        {
                            type = PointerKind.FunctionErrorCheck,
                            mode = FunctionErrorCheckKind.Throws,
                            call = new CallFunctionPointer
                            {
                                type = PointerKind.CallFunction,
                                memberId = "member-throws",
                                receiver = CallReceiver.Instance(StringValuePointer("receiver")),
                                args = new Pointer[0],
                                callSiteId = "throws-check",
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
                    type = MemberKind.Null,
                    required = false,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new CallFunctionPointer
                        {
                            type = PointerKind.CallFunction,
                            memberId = "member-native",
                            receiver = CallReceiver.Instance(StringValuePointer("receiver")),
                            args = new Pointer[] { StringValuePointer("hello") },
                            callSiteId = "missing-wrapper",
                        },
                    },
                },
            };

            // Unavailability remains an NSGetterRuntimeError at the
            // public boundary, with an internal subtype so authored try/catch
            // can distinguish infrastructure from catchable script failures.
            var error = Assert.Catch<NSGetterRuntimeError>(() =>
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
                        type = MemberKind.String,
                        required = true,
                    },
                    value = JToken.FromObject(value),
                },
            };
        }

        // ---------------------------------------------------------------
        // member-score — exercises the gnarliest IR shape:
        //   local int x = 1 + 2;                       (variable + arithmetic + value)
        //   local string label = (this.Name ?? "Unknown")!;  (forceUnwrap + coalesce + keyOf + value)
        //   if ((label is string) && (x != 0)) {       (boolean op + isCheck + comparison)
        //     return [1,2,3].Where(n => n != 0).Count();  (listLiteral + where + count)
        //   } else { throw "bad"; }
        //   return;                                    (bare return)
        //
        // The fixture binds `__this__` to a Class of class-hero. We pass
        // an explicit thisValue so the test doesn't rely on the parent-
        // chain walk (covered separately).
        // ---------------------------------------------------------------

        [Test]
        public void Compute_AttrScore_RunsFullIR_ReturnsCount()
        {
            var client = LoadClient();
            var scoreMember = RequireNSGetter(client, "member-score");
            var node = new NeoMemberNSProperty(client, scoreMember, null);

            // `__this__` is a Class record with a Name field; the IR
            // reads `this.Name`. v-name is "hero" in the fixture.
            var thisValue = new Dictionary<string, object?>
            {
                { "Name", "v-name" }, // resolves through the schema → member-name → row v-name
            };

            var result = node.Compute(thisValue);

            Assert.IsTrue(result.ok, $"Expected ok; got error: {result.error}");
            // [1,2,3].Where(n => n != 0).Count() = 3
            Assert.AreEqual(3.0, result.value);
        }

        // ---------------------------------------------------------------
        // member-manifest — stringify + dictLiteral coverage. The IR is:
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
            var manifestMember = RequireNSGetter(client, "member-manifest");
            var node = new NeoMemberNSProperty(client, manifestMember, null);

            var result = node.Compute();

            Assert.IsTrue(result.ok, $"Expected ok; got error: {result.error}");
            Assert.AreEqual("(Dictionary<int>, Value<<unknown>>)", result.value);
        }

        // ---------------------------------------------------------------
        // member-active — callGetter + toBool coverage. The IR is:
        //   return Boolean(this.Score);
        //   → toBool(callGetter("member-score", receiver = __this__))
        //
        // member-score is invoked via dispatchNSGetterById; the result
        // (a number) is coerced to bool via JsTruthy. Number 3 → true.
        // ---------------------------------------------------------------

        [Test]
        public void Compute_AttrActive_DispatchesCallGetterAndCoercesToBool()
        {
            var client = LoadClient();
            var activeMember = RequireNSGetter(client, "member-active");
            var node = new NeoMemberNSProperty(client, activeMember, null);

            var thisValue = new Dictionary<string, object?>
            {
                { "Name", "v-name" },
            };

            var result = node.Compute(thisValue);

            Assert.IsTrue(result.ok, $"Expected ok; got error: {result.error}");
            Assert.AreEqual(true, result.value);
        }

        [Test]
        public void Evaluate_SyntheticClassId_ReturnsBackingRowId()
        {
            var client = LoadClient();
            var ctx = new NSGetterEvaluator.Context(client, thisValue: null, rootValue: null);
            var row = new ObjectMemberValue
            {
                id = "outpost-row",
                classId = "class-hero",
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
                    type = MemberKind.String,
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
                                            type = MemberKind.String,
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
        public void Evaluate_GeneratedClassThis_AllowsSchemaMemberAccess()
        {
            var client = LoadClient();
            if (!client.TryGetMember("member-hero", out ClassMember? heroMember))
            {
                Assert.Fail("Fixture is missing member-hero");
                return;
            }
            client.SetSaveValue(new ObjectMemberValue
            {
                id = "generated-this-row",
                classId = "class-hero",
                createdAt = "x",
                updatedAt = "x",
                value = new Dictionary<string, string>
                {
                    ["Name"] = "v-str",
                },
            });
            var node = (NeoMemberClass)NeoMember.Create(
                client,
                heroMember,
                "generated-this-row");
            var generatedThis = Hero.Create(client, node);
            var getter = ReturnFunction(
                KeyOf(
                    new VariablePointer
                    {
                        type = PointerKind.Variable,
                        variableId = "__this__",
                    },
                    "Name"),
                MemberKind.String);
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: generatedThis,
                rootValue: null);

            var result = NSGetterEvaluator.Evaluate(getter, ctx);

            Assert.AreEqual("hello", result);
        }

        [Test]
        public void Evaluate_ReadonlyClassDefaultDispatchesConcreteOverrideOfAbstractMember()
        {
            NeoClient client = LoadAbstractReadonlyClassDefaultClient(
                out ObjectMemberValue rootRow,
                out ClassMember statsMember,
                out IntMember abstractDamage);
            var context = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null);
            object? root = NSGetterEvaluator.UnwrapRow(
                rootRow,
                context,
                NeoValueOwnership.Asset);
            KeyOfPointer stats = KeyOf(ThisPointer(), "Stats");
            stats.memberId = statsMember.id;
            KeyOfPointer damage = KeyOf(stats, "Damage");
            damage.memberId = abstractDamage.id;

            object? result = NSGetterEvaluator.Evaluate(
                ReturnFunction(damage, MemberKind.Int),
                context.WithThis(root));

            Assert.AreEqual(42.0, result);
        }

        [Test]
        public void Evaluate_GeneratedClassThis_AllKnownMemberKinds_ReadOnlyAndWritable()
        {
            var client = LoadGeneratedValueSurfaceClient(
                out ClassMember testMember,
                out ObjectMemberValue readOnlyRow,
                out ObjectMemberValue savedRow);
            var readOnlyNode = (NeoMemberClass)NeoMember.Create(
                client,
                testMember,
                readOnlyRow.id);
            var writableNode = (NeoMemberClassWritable)NeoMember.CreateWritable(
                client,
                testMember,
                savedRow.id);

            AssertGeneratedValueSurface(
                client,
                new TestReadOnlyGeneratedValue(client, readOnlyNode));
            AssertGeneratedValueSurface(
                client,
                new TestGeneratedValue(client, writableNode));
        }

        [Test]
        public void Evaluate_GeneratedClassThis_LocalizesStringDereference()
        {
            var client = LoadGeneratedValueSurfaceClient(
                out ClassMember testMember,
                out ObjectMemberValue readOnlyRow,
                out _);
            RequireMember<StringMember>(client, "member-string").localizable = true;
            ((StringMemberValue)client.values["v-string"]).value = "text-string";
            var readOnlyNode = (NeoMemberClass)NeoMember.Create(
                client,
                testMember,
                readOnlyRow.id);

            var result = EvaluateThisMember(
                client,
                new TestReadOnlyGeneratedValue(client, readOnlyNode),
                "String");

            Assert.AreEqual("Localized string", result);
        }

        [Test]
        public void Evaluate_StringInterpolation_LocalizesEnumOptionText()
        {
            var client = LoadGeneratedValueSurfaceClient(
                out ClassMember testMember,
                out ObjectMemberValue readOnlyRow,
                out _);
            client.enums["enum-color"].options["red"].text = "text-red";
            var readOnlyNode = (NeoMemberClass)NeoMember.Create(
                client,
                testMember,
                readOnlyRow.id);

            var result = EvaluatePointer(
                client,
                new TestReadOnlyGeneratedValue(client, readOnlyNode),
                new StringifyPointer
                {
                    type = PointerKind.Stringify,
                    pointer = KeyOf(ThisPointer(), "Enum"),
                    sourceType = new EnumTypeInfo
                    {
                        type = MemberKind.Enum,
                        enumId = "enum-color",
                        required = true,
                    },
                });

            Assert.AreEqual("Localized red", result);
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
                        MemberKind.Int),
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
                        MemberKind.Int),
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
                        MemberKind.Bool),
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
                        MemberKind.Bool),
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
                        MemberKind.Int),
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
                        MemberKind.Int),
                    ctx));
        }

        // ---------------------------------------------------------------
        // resolvedGetter / resolvedReturnTypeInfo — pin the chain-walk.
        // member-score has its own getter + returnTypeInfo so resolution
        // shouldn't need to walk anywhere.
        // ---------------------------------------------------------------

        [Test]
        public void ResolvedGetter_ReturnsInstanceGetter_WhenPresent()
        {
            var client = LoadClient();
            var scoreMember = RequireNSGetter(client, "member-score");
            var node = new NeoMemberNSProperty(client, scoreMember, null);

            Assert.AreSame(scoreMember.getter, node.resolvedGetter);
        }

        [Test]
        public void ResolvedReturnTypeInfo_ReturnsInstanceTypeInfo_WhenPresent()
        {
            var client = LoadClient();
            var scoreMember = RequireNSGetter(client, "member-score");
            var node = new NeoMemberNSProperty(client, scoreMember, null);

            Assert.AreSame(scoreMember.returnTypeInfo, node.resolvedReturnTypeInfo);
            Assert.AreEqual(MemberKind.Int, node.resolvedReturnTypeInfo!.type);
        }

        // ---------------------------------------------------------------
        // Runtime-error paths.
        // ---------------------------------------------------------------

        [Test]
        public void Compute_NoCompiledGetter_ReturnsErrorResult()
        {
            // Synthesize a fresh NSPropertyMember with no `getter` and
            // no extends chain — simulates an unsaved override.
            var client = LoadClient();
            var member = new NSPropertyMember
            {
                id = "test-orphan-getter",
                projectId = "p",
                name = "Orphan",
                kind = MemberKind.NSProperty,
                code = "// not compiled",
                returnTypeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.Int,
                    required = true,
                },
                getter = null!,  // simulate "no getter yet"
                createdAt = "x",
                updatedAt = "x",
            };
            var node = new NeoMemberNSProperty(client, member, null);

            var result = node.Compute();

            Assert.IsFalse(result.ok);
            Assert.That(result.error, Does.Contain("Compiled `getter`"));
        }

        [Test]
        public void Compute_OptionalChaining_SurvivesNullThis()
        {
            // member-score reads `this?.Name ?? "Unknown"` — the keyOf
            // is optional, so a null `__this__` short-circuits to null,
            // the coalesce substitutes "Unknown", and the function
            // continues to its tail (which doesn't depend on `this`).
            // Pinning that the optional/coalesce path resolves cleanly
            // without throwing — the TS evaluator's behavior we're
            // mirroring.
            var client = LoadClient();
            var scoreMember = RequireNSGetter(client, "member-score");
            var node = new NeoMemberNSProperty(client, scoreMember, null);

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
            var member = new NSPropertyMember
            {
                id = "test-force-unwrap-null",
                projectId = "p",
                name = "ForceUnwrapNull",
                kind = MemberKind.NSProperty,
                code = "// `return (null as string?)!;`",
                returnTypeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.String,
                    required = true,
                },
                getter = new FunctionWithReturnType
                {
                    parameters = new Variable[0],
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.String,
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
                                            type = MemberKind.String,
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
            var node = new NeoMemberNSProperty(client, member, null);

            var result = node.Compute();

            Assert.IsFalse(result.ok);
            Assert.That(result.error, Does.Contain("force-unwrapping"));
        }

        // ---------------------------------------------------------------
        // Auto-resolution of __this__ from the parent chain.
        //
        // Build a wrapper tree where a Class record contains an
        // NSProperty as one of its schema-keyed children. When we look
        // up that NSProperty via the parent and Compute() with no
        // explicit thisValue, the evaluator should walk parent up to
        // find the Class record.
        // ---------------------------------------------------------------

        [Test]
        public void Compute_AutoResolvesThisValue_FromParentChain()
        {
            var client = LoadClient();
            // member-hero is a Class of class-hero whose schema has
            // { Name: member-name, Health: member-health }. Bind to v-dict
            // (which has `{ Name: "v-name", Level: "v-level" }` —
            // Level isn't in the schema so only Name walks).
            var heroMember = client.TryGetMember("member-hero", out ClassMember? ha)
                ? ha
                : null;
            Assert.IsNotNull(heroMember);
            var hero = (NeoMemberClass)NeoMember.Create(client, heroMember!, "v-dict");

            // Now manually attach an NSProperty child under the hero.
            var scoreMember = RequireNSGetter(client, "member-score");
            var nsg = new NeoMemberNSProperty(client, scoreMember, null);
            nsg.parent = hero;  // simulates collection-side wiring

            var result = nsg.Compute();  // no explicit thisValue

            Assert.IsTrue(result.ok, $"Expected ok via parent walk; got: {result.error}");
            Assert.AreEqual(3.0, result.value);
        }

        private static FunctionWithReturnType ReturnFunction(
            Pointer pointer,
            MemberKind returnType)
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
            NeoGeneratedClassValue generated)
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
                    KeyOf(ThisPointer(), "ClassChild", "Text")));

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
            NeoGeneratedClassValue generated,
            string key)
        {
            return EvaluatePointer(client, generated, KeyOf(ThisPointer(), key));
        }

        private static object? EvaluatePointer(
            NeoClient client,
            NeoGeneratedClassValue generated,
            Pointer pointer)
        {
            return NSGetterEvaluator.Evaluate(
                ReturnFunction(pointer, MemberKind.String),
                new NSGetterEvaluator.Context(
                    client,
                    thisValue: generated,
                    rootValue: null));
        }

        private static NeoClient LoadNativeFunctionClient(
            out ClassMember receiverMember)
        {
            var functionMember = new FunctionMember
            {
                id = "member-native-ping",
                projectId = "project-native-function",
                name = "Ping",
                kind = MemberKind.Function,
                returnTypeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.String,
                    required = true,
                },
                argumentTypes = new FunctionArgumentTypeInfo[]
                {
                    new FunctionArgumentTypeInfo
                    {
                        name = "message",
                        type = MemberKind.String,
                        required = true,
                    },
                },
                deferred = false,
                createdAt = "x",
                updatedAt = "x",
            };
            receiverMember = ClassMember(
                "member-native-receiver",
                "NativeReceiver",
                "class-native-receiver");
            receiverMember.valueId = "v-native-receiver";
            var rootMember = ClassMember(
                "member-native-root",
                "Root",
                "class-native-root");
            var rootSaveMember = ClassMember(
                "member-native-save",
                "Save",
                "class-native-root");
            var rootSessionMember = ClassMember(
                "member-native-session",
                "Session",
                "class-native-root");
            var receiverClass = NeoSchemaClass(
                "class-native-receiver",
                "NativeReceiver",
                new Dictionary<string, string>
                {
                    ["Ping"] = functionMember.id,
                });
            var rootClass = NeoSchemaClass(
                "class-native-root",
                "NativeRoot",
                new Dictionary<string, string>());
            var data = new ProjectData
            {
                project = new Project
                {
                    id = "project-native-function",
                    name = "Native Function",
                    rootAssetsMemberId = rootMember.id,
                    rootSaveFileMemberId = rootSaveMember.id,
                    rootSessionMemberId = rootSessionMember.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    [functionMember.id] = functionMember,
                    [receiverMember.id] = receiverMember,
                    [rootMember.id] = rootMember,
                    [rootSaveMember.id] = rootSaveMember,
                    [rootSessionMember.id] = rootSessionMember,
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["v-native-root"] = ObjectValue(
                        "v-native-root",
                        rootClass.id,
                        new Dictionary<string, string>()),
                    ["v-native-receiver"] = ObjectValue(
                        "v-native-receiver",
                        receiverClass.id,
                        new Dictionary<string, string>()),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [receiverClass.id] = receiverClass,
                },
            };
            return NeoTestSaveStack.ClientFromSchema(data);
        }

        private static NeoClient LoadGeneratedValueSurfaceClient(
            out ClassMember testMember,
            out ObjectMemberValue readOnlyRow,
            out ObjectMemberValue savedRow)
        {
            var childTextMember = StringMember("member-child-text", "ChildText");
            var childClass = NeoSchemaClass("class-child", "Child", new Dictionary<string, string>
            {
                ["Text"] = childTextMember.id,
            });

            var nullMember = NullMember("member-null", "Null");
            var boolMember = BoolMember("member-bool", "Bool");
            var intMember = IntMember("member-int", "Int");
            var floatMember = FloatMember("member-float", "Float");
            var stringMember = StringMember("member-string", "String");
            var listEntryMember = StringMember("member-list-entry", "ListEntry");
            var listMember = ListMember("member-list", "List", listEntryMember.id);
            var dictionaryEntryMember = StringMember("member-dict-entry", "DictionaryEntry");
            var dictionaryMember = DictionaryMember(
                "member-dictionary",
                "Dictionary",
                dictionaryEntryMember.id);
            var classChildMember = ClassMember("member-class-child", "ClassChild", childClass.id);
            var enumModel = new NeoCompose.Runtime.Json.Enum
            {
                id = "enum-color",
                projectId = "project-generated-surface",
                name = "Color",
                options = new Dictionary<string, EnumOption>
                {
                    ["red"] = new EnumOption { text = "Red" },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            var enumMember = EnumMember("member-enum", "Enum", enumModel.id);
            var lookupMember = LookupMember(
                "member-lookup-set",
                "LookupSet",
                listMember.id,
                "v-list");
            var getterMember = NSPropertyMember("member-getter", "Getter");

            testMember = ClassMember("member-test", "Test", "class-test");
            var rootMember = ClassMember("member-root", "Root", "class-root");
            var rootSaveMember = ClassMember("member-save", "Save", "class-root");
            var testType = NeoSchemaClass("class-test", "GeneratedSurface", new Dictionary<string, string>
            {
                ["Null"] = nullMember.id,
                ["Bool"] = boolMember.id,
                ["Int"] = intMember.id,
                ["Float"] = floatMember.id,
                ["String"] = stringMember.id,
                ["List"] = listMember.id,
                ["Dictionary"] = dictionaryMember.id,
                ["ClassChild"] = classChildMember.id,
                ["Enum"] = enumMember.id,
                ["LookupSet"] = lookupMember.id,
                ["Getter"] = getterMember.id,
            });
            var rootClass = NeoSchemaClass("class-root", "Root", new Dictionary<string, string>());

            var values = new Dictionary<string, MemberValue>
            {
                ["v-assets"] = ObjectValue("v-assets", "class-root", new Dictionary<string, string>()),
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
                    childClass.id,
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
                    name = "Generated Surface",
                    rootAssetsMemberId = rootMember.id,
                    rootSaveFileMemberId = rootSaveMember.id,
                    rootSessionMemberId = rootSaveMember.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    [rootMember.id] = rootMember,
                    [rootSaveMember.id] = rootSaveMember,
                    [testMember.id] = testMember,
                    [nullMember.id] = nullMember,
                    [boolMember.id] = boolMember,
                    [intMember.id] = intMember,
                    [floatMember.id] = floatMember,
                    [stringMember.id] = stringMember,
                    [listEntryMember.id] = listEntryMember,
                    [listMember.id] = listMember,
                    [dictionaryEntryMember.id] = dictionaryEntryMember,
                    [dictionaryMember.id] = dictionaryMember,
                    [classChildMember.id] = classChildMember,
                    [childTextMember.id] = childTextMember,
                    [enumMember.id] = enumMember,
                    [lookupMember.id] = lookupMember,
                    [getterMember.id] = getterMember,
                },
                values = values,
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [testType.id] = testType,
                    [childClass.id] = childClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>
                {
                    [enumModel.id] = enumModel,
                },
                localization = new ProjectLocalizationExport
                {
                    schemaVersion = 1,
                    mainLocale = "en-US",
                    supportedLocales = new[]
                    {
                        new ProjectLocalizationLocale { locale = "en-US" },
                    },
                    textIds = new[] { "text-string", "text-red" },
                    mainLocaleFileName = "en-US.json",
                    localeFileNames = new Dictionary<string, string>
                    {
                        ["en-US"] = "en-US.json",
                    },
                    formatting = new ProjectLocalizationFormatting
                    {
                        syntax = "smart-format",
                        sourceSyntax = "icu",
                    },
                },
            };
            var client = NeoTestSaveStack.ClientFromSchema(data);
            client.Localization.TryAddLoadedLocale(new ProjectLocalizationLocaleFile
            {
                schemaVersion = 1,
                projectId = data.project.id,
                versionId = "version-1",
                locale = "en-US",
                formattingSyntax = "smart-format",
                values = new Dictionary<string, string?>
                {
                    ["text-string"] = "Localized string",
                    ["text-red"] = "Localized red",
                },
            });
            client.SetSaveValue(savedRow);
            return client;
        }

        private static NeoClient LoadAbstractReadonlyClassDefaultClient(
            out ObjectMemberValue rootRow,
            out ClassMember statsMember,
            out IntMember abstractDamage)
        {
            abstractDamage = IntMember("member-abstract-damage", "Damage");
            abstractDamage.required = true;
            abstractDamage.storage = "immutable";
            abstractDamage.isVirtual = true;
            abstractDamage.isAbstract = true;
            abstractDamage.isReadOnly = true;

            var concreteDamage = IntMember("member-concrete-damage", "Damage");
            concreteDamage.required = true;
            concreteDamage.storage = "immutable";
            concreteDamage.isVirtual = true;
            concreteDamage.isReadOnly = true;
            concreteDamage.extendsMemberId = abstractDamage.id;
            concreteDamage.defaultValue = new NumberMemberValueBase { value = 42 };

            statsMember = ClassMember(
                "member-readonly-stats",
                "Stats",
                "class-abstract-stats");
            statsMember.required = true;
            statsMember.storage = "immutable";
            statsMember.isReadOnly = true;
            statsMember.defaultValue = new ObjectMemberValueBase
            {
                classId = "class-concrete-stats",
                value = new Dictionary<string, string>(),
            };

            var rootAssetsMember = ClassMember(
                "member-abstract-readonly-root-assets",
                "Assets",
                "class-abstract-readonly-root");
            rootAssetsMember.valueId = "value-abstract-readonly-root";
            var rootSaveMember = ClassMember(
                "member-abstract-readonly-root-save",
                "Save",
                "class-abstract-readonly-root");
            rootSaveMember.storage = "save";
            var rootSessionMember = ClassMember(
                "member-abstract-readonly-root-session",
                "Session",
                "class-abstract-readonly-root");
            rootSessionMember.storage = "session";

            var rootClass = NeoSchemaClass(
                "class-abstract-readonly-root",
                "AbstractReadonlyRoot",
                new Dictionary<string, string> { ["Stats"] = statsMember.id });
            var abstractStatsClass = NeoSchemaClass(
                "class-abstract-stats",
                "AbstractStats",
                new Dictionary<string, string> { ["Damage"] = abstractDamage.id });
            abstractStatsClass.isAbstract = true;
            var concreteStatsClass = NeoSchemaClass(
                "class-concrete-stats",
                "ConcreteStats",
                new Dictionary<string, string> { ["Damage"] = concreteDamage.id });
            concreteStatsClass.extendsClassId = abstractStatsClass.id;

            rootRow = ObjectValue(
                "value-abstract-readonly-root",
                rootClass.id,
                new Dictionary<string, string>());
            var data = new ProjectData
            {
                project = new Project
                {
                    id = "project-abstract-readonly-evaluator",
                    name = "Abstract Readonly Evaluator",
                    rootAssetsMemberId = rootAssetsMember.id,
                    rootSaveFileMemberId = rootSaveMember.id,
                    rootSessionMemberId = rootSessionMember.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    [rootAssetsMember.id] = rootAssetsMember,
                    [rootSaveMember.id] = rootSaveMember,
                    [rootSessionMember.id] = rootSessionMember,
                    [statsMember.id] = statsMember,
                    [abstractDamage.id] = abstractDamage,
                    [concreteDamage.id] = concreteDamage,
                },
                values = new Dictionary<string, MemberValue>
                {
                    [rootRow.id] = rootRow,
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [abstractStatsClass.id] = abstractStatsClass,
                    [concreteStatsClass.id] = concreteStatsClass,
                },
            };
            return NeoTestSaveStack.ClientFromSchema(data);
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
                ["ClassChild"] = "v-child",
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
                        type = MemberKind.String,
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

        private static NeoSchemaClass NeoSchemaClass(
            string id,
            string name,
            Dictionary<string, string> schema)
        {
            return new NeoSchemaClass
            {
                id = id,
                projectId = "project-generated-surface",
                name = name,
                schema = schema,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static NullMember NullMember(string id, string name)
        {
            return new NullMember
            {
                id = id,
                projectId = "project-generated-surface",
                name = name,
                kind = MemberKind.Null,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static BoolMember BoolMember(string id, string name)
        {
            return new BoolMember
            {
                id = id,
                projectId = "project-generated-surface",
                name = name,
                kind = MemberKind.Bool,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static IntMember IntMember(string id, string name)
        {
            return new IntMember
            {
                id = id,
                projectId = "project-generated-surface",
                name = name,
                kind = MemberKind.Int,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static FloatMember FloatMember(string id, string name)
        {
            return new FloatMember
            {
                id = id,
                projectId = "project-generated-surface",
                name = name,
                kind = MemberKind.Float,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static StringMember StringMember(string id, string name)
        {
            return new StringMember
            {
                id = id,
                projectId = "project-generated-surface",
                name = name,
                kind = MemberKind.String,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static ListMember ListMember(
            string id,
            string name,
            string entryMemberId)
        {
            return new ListMember
            {
                id = id,
                projectId = "project-generated-surface",
                name = name,
                kind = MemberKind.List,
                entryMemberId = entryMemberId,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static DictionaryMember DictionaryMember(
            string id,
            string name,
            string entryMemberId)
        {
            return new DictionaryMember
            {
                id = id,
                projectId = "project-generated-surface",
                name = name,
                kind = MemberKind.Dictionary,
                entryMemberId = entryMemberId,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static ClassMember ClassMember(
            string id,
            string name,
            string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-generated-surface",
                name = name,
                kind = MemberKind.Class,
                classId = classId,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static EnumMember EnumMember(
            string id,
            string name,
            string enumId)
        {
            return new EnumMember
            {
                id = id,
                projectId = "project-generated-surface",
                name = name,
                kind = MemberKind.Enum,
                enumId = enumId,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static LookupMember LookupMember(
            string id,
            string name,
            string collectionMemberId,
            string collectionValueId)
        {
            return new LookupMember
            {
                id = id,
                projectId = "project-generated-surface",
                name = name,
                kind = MemberKind.Lookup,
                collectionMemberId = collectionMemberId,
                collectionValueId = collectionValueId,
                multiselect = true,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static NSPropertyMember NSPropertyMember(string id, string name)
        {
            return new NSPropertyMember
            {
                id = id,
                projectId = "project-generated-surface",
                name = name,
                kind = MemberKind.NSProperty,
                code = "return \"computed\";",
                returnTypeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.String,
                    required = true,
                },
                getter = ReturnFunction(StringPointer("computed"), MemberKind.String),
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static NullMemberValue NullValue(string id)
        {
            return new NullMemberValue
            {
                id = id,
                createdAt = "x",
                updatedAt = "x",
                value = null,
            };
        }

        private static BoolMemberValue BoolValue(string id, bool value)
        {
            return new BoolMemberValue
            {
                id = id,
                createdAt = "x",
                updatedAt = "x",
                value = value,
            };
        }

        private static NumberMemberValue NumberValue(string id, double value)
        {
            return new NumberMemberValue
            {
                id = id,
                createdAt = "x",
                updatedAt = "x",
                value = value,
            };
        }

        private static StringMemberValue StringValue(string id, string value)
        {
            return new StringMemberValue
            {
                id = id,
                createdAt = "x",
                updatedAt = "x",
                value = value,
            };
        }

        private static ArrayMemberValue ArrayValue(string id, params string[] value)
        {
            return new ArrayMemberValue
            {
                id = id,
                createdAt = "x",
                updatedAt = "x",
                value = value,
            };
        }

        private static ObjectMemberValue ObjectValue(
            string id,
            string? classId,
            Dictionary<string, string> value)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                createdAt = "x",
                updatedAt = "x",
                value = value,
            };
        }

        private sealed class TestReadOnlyGeneratedValue : NeoGeneratedClassValue
        {
            public TestReadOnlyGeneratedValue(NeoClient client, NeoMemberClass node)
                : base(client, node, "class-test")
            {
            }
        }

        private sealed class TestGeneratedValue : NeoGeneratedClassValue
        {
            public TestGeneratedValue(NeoClient client, NeoMemberClassWritable node)
                : base(client, node, "class-test")
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

        private sealed class FunctionTestValue : NeoGeneratedClassValue
        {
            private FunctionTestValue(NeoClient client, NeoMemberClass node)
                : base(client, node, "class-native-receiver")
            {
            }

            public IFunctionTestValueFunctionHandler? FunctionHandler
            {
                get => FunctionHandlerObject as IFunctionTestValueFunctionHandler;
                set => FunctionHandlerObject = value;
            }

            public static FunctionTestValue Create(
                NeoClient client,
                NeoMemberClass node)
            {
                return NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
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
