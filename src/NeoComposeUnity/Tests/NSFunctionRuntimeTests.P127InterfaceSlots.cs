// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime;
using System.Reflection;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    public partial class NSFunctionRuntimeTests
    {
        private sealed class InterfaceSlotRuntimeDecision : IDisposable
        {
#if UNITY_EDITOR
            private readonly bool? saved = NeoGeneratedTypesSupport.TestTrustRuntimeInterfaceDispatch;
#endif
            internal InterfaceSlotRuntimeDecision(bool? decision)
            {
#if UNITY_EDITOR
                NeoGeneratedTypesSupport.TestTrustRuntimeInterfaceDispatch = decision;
#endif
            }
            public void Dispose()
            {
#if UNITY_EDITOR
                NeoGeneratedTypesSupport.TestTrustRuntimeInterfaceDispatch = saved;
#endif
            }
        }

        private static InterfaceTypeInfo SlotConstruction() => new()
        {
            type = MemberKind.Interface,
            required = true,
            interfaceId = "slot-contract"
        };

        private static object SlotReceiver(NeoClient client) =>
            NSGetterEvaluator.ResolveValueIfId("receiver-value", new NSGetterEvaluator.Context(client, null, null))!;

        private static NeoClient InterfaceSlotClient(bool property = false, bool classImplementation = false)
        {
            JsonMember Body(string id, int answer) => property
                ? new NSPropertyMember
                {
                    id = id,
                    name = "M",
                    projectId = ProjectId,
                    kind = MemberKind.NSProperty,
                    code = "compiled getter",
                    returnTypeInfo = IntType(),
                    getter = Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(answer)))
                }
                : ScriptFunction(id, "M", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                    Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(answer))));
            var body = Body("slot-default", 42);
            var implementation = Body("slot-class", 17);
            var declaration = new Interface
            {
                id = "slot-contract",
                name = "ISlot",
                projectId = ProjectId,
                members = new()
                {
                    ["M"] = new()
                    {
                        id = "slot",
                        kind = property ? NeoInterfaceMemberKind.Property : NeoInterfaceMemberKind.Function,
                        typeInfo = property ? IntType() : null,
                        returnTypeInfo = property ? null : IntType(),
                        argumentTypes = property ? null : new(),
                        implementationMemberId = body.id
                    }
                }
            };
            var owner = classImplementation ? ReceiverClass(("M", implementation.id)) : ReceiverClass();
            owner.implementsInterfaces = new() { new() { interfaceId = declaration.id } };
            return BuildClient(new[] { body, implementation }, owner, interfaces: new()
            {
                [declaration.id] = declaration
            });
        }

        [TestCase(false, false, 42)]
        [TestCase(false, true, 17)]
        [TestCase(true, false, 42)]
        [TestCase(true, true, 17)]
        public void P127Interface_SlotEntryResolvesExactAndKeepsWarmWorkConstant(bool property, bool classImplementation, int expected)
        {
            using var decision = new InterfaceSlotRuntimeDecision(false);
            using var client = InterfaceSlotClient(property, classImplementation);
            object receiver = SlotReceiver(client);
            object? Invoke() => NeoGeneratedTypesSupport.InvokeInterfaceSlot(client, receiver, "slot",
                property ? NeoInterfaceBody.Get : NeoInterfaceBody.Function, SlotConstruction(), "slot", SlotConstruction(), Array.Empty<object?>());
            Assert.That(Invoke(), Is.EqualTo(expected));
#if UNITY_EDITOR
            var dispatch = NeoInterfaceDispatch.Of(client);
            var cold = (dispatch.TestConversions, dispatch.TestGraphPops, dispatch.TestGraphEdges,
                dispatch.TestIndexPops, dispatch.TestIndexEdges, dispatch.TestGlobalNodes, dispatch.TestGlobalEdges);
            Assert.That(dispatch.TestConversions, Is.GreaterThan(0));
#endif
            for (int index = 0; index < 100; index++)
                Assert.That(Invoke(), Is.EqualTo(expected));
#if UNITY_EDITOR
            Assert.That((dispatch.TestConversions, dispatch.TestGraphPops, dispatch.TestGraphEdges,
                dispatch.TestIndexPops, dispatch.TestIndexEdges, dispatch.TestGlobalNodes, dispatch.TestGlobalEdges), Is.EqualTo(cold));
#endif
        }

        private interface ISlotCallingBody
        {
            int M()
            {
                var value = (SlotCallingValue)this;
                if (++value.Entries != 1)
                    throw new InvalidOperationException("The generated default body was re-entered.");
                return Convert.ToInt32(NeoGeneratedTypesSupport.InvokeInterfaceSlot(value.Client, value, "slot",
                    NeoInterfaceBody.Function, SlotConstruction(), "slot", SlotConstruction(), Array.Empty<object?>()));
            }
        }

        private sealed class SlotCallingValue : NeoGeneratedClassValue, ISlotCallingBody
        {
            internal int Entries;
            internal SlotCallingValue(NeoClient client) : base(client,
                NeoGeneratedTypesSupport.CreateWritableClassValue(client, "receiver-class"), "receiver-class", false)
            {
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void P127Interface_SlotDefaultBodyRunsDirectlyWithoutClrRecursion(bool trusted)
        {
            using var decision = new InterfaceSlotRuntimeDecision(trusted);
            using var client = InterfaceSlotClient();
            using var receiver = new SlotCallingValue(client);
            Assert.That(((ISlotCallingBody)receiver).M(), Is.EqualTo(42));
            Assert.That(receiver.Entries, Is.EqualTo(1));
        }

        [Test]
        public void P127Interface_SlotEntryRejectsInvalidCalls()
        {
            using var decision = new InterfaceSlotRuntimeDecision(false);
            using var client = InterfaceSlotClient(property: true);
            using var other = InterfaceSlotClient(property: true);
            using var generated = new SlotCallingValue(other);
            object receiver = SlotReceiver(client);
            object? Invoke(object? target, NeoInterfaceBody body, params object?[] args) =>
                NeoGeneratedTypesSupport.InvokeInterfaceSlot(client, target, "slot", body, SlotConstruction(), "slot", SlotConstruction(), args);
            Assert.That(Assert.Throws<ArgumentNullException>(() => Invoke(null, NeoInterfaceBody.Get))!.ParamName, Is.EqualTo("receiver"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => Invoke(generated, NeoInterfaceBody.Get))!.Message,
                Does.Contain("different project client"));
            Assert.Throws<NSGetterRuntimeError>(() => Invoke(new object(), NeoInterfaceBody.Get));
            Assert.Throws<ArgumentException>(() => Invoke(receiver, NeoInterfaceBody.Function));
            Assert.That(Assert.Throws<ArgumentException>(() => Invoke(receiver, NeoInterfaceBody.Get, 1))!.Message,
                Does.Contain("getter takes no arguments"));
            Assert.That(Assert.Throws<ArgumentException>(() => Invoke(receiver, NeoInterfaceBody.Set))!.Message,
                Does.Contain("setter takes one argument"));
            client.ProjectDataForRuntime.interfaces["slot-contract"].members["M"].IsStatic = true;
            Assert.That(Assert.Throws<ArgumentException>(() => Invoke(receiver, NeoInterfaceBody.Get))!.Message,
                Is.EqualTo("Interface member 'slot' is static; InvokeInterfaceSlot runs instance slots."));
        }

        [Test]
        public void P127Interface_SlotEntryReportsMissingImplementation()
        {
            using var decision = new InterfaceSlotRuntimeDecision(false);
            using var client = InterfaceSlotClient();
            client.ProjectDataForRuntime.interfaces["slot-contract"].members["M"].implementationMemberId = null;
            var error = Assert.Throws<InvalidOperationException>(() => NeoGeneratedTypesSupport.InvokeInterfaceSlot(client,
                SlotReceiver(client), "slot", NeoInterfaceBody.Function, SlotConstruction(), "slot", SlotConstruction(), Array.Empty<object?>()));
            Assert.That(error!.Message, Does.StartWith("Interface member 'slot' has no implementation for '"));
            Assert.That(error.InnerException, Is.TypeOf<NeoMissingInterfaceImplementationError>());
        }

        private static NeoClient AmbiguousInterfaceSlotClient()
        {
            var client = InterfaceSlotClient();
            foreach (string id in new[] { "left", "right" })
                client.ProjectDataForRuntime.interfaces[id] = new Interface
                {
                    id = id,
                    name = id,
                    projectId = ProjectId,
                    extendsInterfaces = new() { new() { interfaceId = "slot-contract" } },
                    members = new()
                    {
                        ["M"] = new()
                        {
                            id = id + "-slot",
                            kind = NeoInterfaceMemberKind.Function,
                            returnTypeInfo = IntType(),
                            argumentTypes = new(),
                            implementationMemberId = id == "left" ? "slot-default" : "slot-class",
                            explicitImplementation = new()
                            {
                                interfaceId = "slot-contract",
                                interfaceMemberId = "slot"
                            }
                        }
                    }
                };
            client.ProjectDataForRuntime.classes["receiver-class"].implementsInterfaces = new()
            {
                new() { interfaceId = "left" }, new() { interfaceId = "right" }
            };
            return client;
        }

        [Test]
        public void P127Interface_SlotEntryTranslatesResolutionAmbiguity()
        {
            using var decision = new InterfaceSlotRuntimeDecision(false);
            using var client = AmbiguousInterfaceSlotClient();
            var receiverType = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: "receiver-class"));
            var requested = NeoTypeBindings.FromTypeInfo(SlotConstruction());
            Assert.Throws<NeoAmbiguousImplementationError>(() => NeoInterfaceDispatch.Of(client).Resolve(receiverType, requested, "slot", "call"));
            var error = Assert.Throws<AmbiguousImplementationException>(() => NeoGeneratedTypesSupport.InvokeInterfaceSlot(client,
                SlotReceiver(client), "slot", NeoInterfaceBody.Function, SlotConstruction(), "slot", SlotConstruction(), Array.Empty<object?>()));
            Assert.That(error!.Message, Does.Contain("has competing implementations"));
            Assert.Throws<AmbiguousImplementationException>(() => NeoGeneratedTypesSupport.InvokeInterfaceSlotAsync(client,
                SlotReceiver(client), "slot", NeoInterfaceBody.Function, SlotConstruction(), "slot", SlotConstruction(), Array.Empty<object?>()).GetAwaiter().GetResult());
        }

        [Test]
        public void P127Interface_SlotAsyncCompletesPropertiesAndRejectsImmediateFunctions()
        {
            using var decision = new InterfaceSlotRuntimeDecision(false);
            using var propertyClient = InterfaceSlotClient(property: true);
            var value = NeoGeneratedTypesSupport.InvokeInterfaceSlotAsync(propertyClient, SlotReceiver(propertyClient), "slot",
                NeoInterfaceBody.Get, SlotConstruction(), "slot", SlotConstruction(), Array.Empty<object?>());
            Assert.That(value.IsCompleted, Is.True);
            Assert.That(value.GetAwaiter().GetResult(), Is.EqualTo(42));
            using var functionClient = InterfaceSlotClient();
            Assert.Throws<ArgumentException>(() => NeoGeneratedTypesSupport.InvokeInterfaceSlot(functionClient,
                SlotReceiver(functionClient), "slot", NeoInterfaceBody.Get, SlotConstruction(), "slot", SlotConstruction(), Array.Empty<object?>()));
            Assert.Throws<ArgumentException>(() => NeoGeneratedTypesSupport.InvokeInterfaceSlot(functionClient,
                SlotReceiver(functionClient), "slot", NeoInterfaceBody.Function, SlotConstruction(), "slot", SlotConstruction(), new object?[] { 1 }));
            Assert.That(Assert.Throws<InvalidOperationException>(() => NeoGeneratedTypesSupport.InvokeInterfaceSlotAsync(functionClient,
                SlotReceiver(functionClient), "slot", NeoInterfaceBody.Function, SlotConstruction(), "slot", SlotConstruction(), Array.Empty<object?>()).GetAwaiter().GetResult())!.Message,
                Does.Contain("immediate"));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void P127Interface_SlotDeferredFunctionsKeepCallsSeparate(bool trusted)
        {
            using var decision = new InterfaceSlotRuntimeDecision(trusted);
            var native = NativeFunction("interface-fetch", "Fetch", true);
            var body = ScriptFunction("interface-body", "M", true, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Call(native.id, "interface-wait"))));
            var contract = new Interface
            {
                id = "contract",
                name = "I",
                projectId = ProjectId,
                members = new()
                {
                    ["M"] = new InterfaceMember
                    {
                        id = "slot",
                        kind = NeoInterfaceMemberKind.Function,
                        Dispatch = NeoFunctionDispatchKind.Asynchronous,
                        returnTypeInfo = IntType(),
                        argumentTypes = new(),
                        implementationMemberId = body.id
                    }
                }
            };
            var owner = ReceiverClass(("Fetch", native.id));
            owner.implementsInterfaces = new() { new() { interfaceId = contract.id } };
            using var client = BuildClient(new JsonMember[] { native, body }, owner, interfaces: new()
            {
                [contract.id] = contract
            });
            var pending = new List<NeoDeferredFunction<int>>();
            client.RegisterDeferredNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
            {
                [native.id] = (_, _, _, _, handle) => pending.Add(NeoGeneratedTypesSupport.ResolveDeferredFunction<NeoDeferredFunction<int>>(handle, native.name)),
            });
            var context = new NSGetterEvaluator.Context(client, null, null);
            object target = NSGetterEvaluator.ResolveValueIfId("receiver-value", context)!;
            var construction = new InterfaceTypeInfo { type = MemberKind.Interface, required = true, interfaceId = contract.id };
            Assert.Throws<InvalidOperationException>(() => NeoGeneratedTypesSupport.InvokeInterfaceSlot(client, target, "slot",
                NeoInterfaceBody.Function, construction, "slot", construction, Array.Empty<object?>()));
            var first = NeoGeneratedTypesSupport.InvokeInterfaceSlotAsync(client, target, "slot", NeoInterfaceBody.Function, construction, "slot", construction, Array.Empty<object?>());
            var second = NeoGeneratedTypesSupport.InvokeInterfaceSlotAsync(client, target, "slot", NeoInterfaceBody.Function, construction, "slot", construction, Array.Empty<object?>());
            Assert.That(first.IsCompleted, Is.False);
            pending[1].Complete(22);
            pending[0].Complete(11);
            Assert.That(first.GetAwaiter().GetResult(), Is.EqualTo(11));
            Assert.That(second.GetAwaiter().GetResult(), Is.EqualTo(22));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void P127Interface_SlotSetterUsesResolvedClassOrDefaultBody(bool classBody)
        {
            using var decision = new InterfaceSlotRuntimeDecision(false);
            var stored = new IntMember
            {
                id = "slot-stored",
                projectId = ProjectId,
                name = "Stored",
                kind = MemberKind.Int,
                Modifier = NeoMemberModifierKind.Static,
                Storage = NeoMemberStorage.Save,
                Requirement = NeoMemberRequirementKind.Required
            };
            var body = new NSPropertyMember
            {
                id = "slot-setter",
                projectId = ProjectId,
                name = "P",
                kind = MemberKind.NSProperty,
                code = "return Stored;",
                setterCode = "Stored = value;",
                returnTypeInfo = IntType(),
                DeclaredAccessors = NeoPropertyAccessorsKind.GetSet,
                getter = Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(new StaticMemberPointer { memberId = stored.id })),
                setter = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = Array.Empty<Variable>(),
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                    instructions = new Instruction[]
                    {
                        new AssignInstruction
                        {
                            operatorValue = "=", pointer = Variable("__value__"),
                            target = new WriteTarget { pointer = new StaticMemberPointer { memberId = stored.id }, typeInfo = IntType(), writability = WritabilityKind.Save }
                        }
                    }
                }
            };
            var declaration = new Interface
            {
                id = "slot-contract",
                name = "ISlot",
                projectId = ProjectId,
                members = new()
                {
                    ["P"] = new()
                    {
                        id = "slot",
                        kind = NeoInterfaceMemberKind.Property,
                        typeInfo = IntType(),
                        Accessors = NeoPropertyAccessorsKind.GetSet,
                        implementationMemberId = classBody ? null : body.id
                    }
                }
            };
            var owner = classBody ? ReceiverClass(("Stored", stored.id), ("P", body.id)) : ReceiverClass(("Stored", stored.id));
            owner.implementsInterfaces = new() { new() { interfaceId = declaration.id } };
            using var client = BuildClient(new JsonMember[] { stored, body }, owner, interfaces: new()
            {
                [declaration.id] = declaration
            });
            object receiver = SlotReceiver(client);
            Assert.That(NeoGeneratedTypesSupport.InvokeInterfaceSlot(client, receiver, "slot", NeoInterfaceBody.Set,
                SlotConstruction(), "slot", SlotConstruction(), new object?[] { 31 }), Is.Null);
            Assert.That(NeoGeneratedTypesSupport.InvokeInterfaceSlot(client, receiver, "slot", NeoInterfaceBody.Get,
                SlotConstruction(), "slot", SlotConstruction(), Array.Empty<object?>()), Is.EqualTo(31));
            var task = NeoGeneratedTypesSupport.InvokeInterfaceSlotAsync(client, receiver, "slot", NeoInterfaceBody.Set,
                SlotConstruction(), "slot", SlotConstruction(), new object?[] { 47 });
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.GetAwaiter().GetResult(), Is.Null);
            Assert.That(NeoGeneratedTypesSupport.InvokeInterfaceSlot(client, receiver, "slot", NeoInterfaceBody.Get,
                SlotConstruction(), "slot", SlotConstruction(), Array.Empty<object?>()), Is.EqualTo(47));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void P127Interface_SlotAmbiguityUsesCallerOnlyOnTrustedRuntimes(bool trusted)
        {
            using var decision = new InterfaceSlotRuntimeDecision(trusted);
            using var client = AmbiguousInterfaceSlotClient();
            object receiver = SlotReceiver(client);
            var caller = new InterfaceTypeInfo { type = MemberKind.Interface, required = true, interfaceId = "right" };
            var dispatch = NeoInterfaceDispatch.Of(client);
#if UNITY_EDITOR
            long before = dispatch.TestResolveCalls;
#endif
            object? Invoke() => NeoGeneratedTypesSupport.InvokeInterfaceSlot(client, receiver, "slot", NeoInterfaceBody.Function,
                SlotConstruction(), "right-slot", caller, Array.Empty<object?>());
            if (trusted)
                Assert.That(Invoke(), Is.EqualTo(17), "Run the caller's own body directly.");
            else
                Assert.Throws<AmbiguousImplementationException>(() => Invoke());
            var asyncCall = NeoGeneratedTypesSupport.InvokeInterfaceSlotAsync(client, receiver, "slot", NeoInterfaceBody.Function,
                SlotConstruction(), "right-slot", caller, Array.Empty<object?>());
            if (trusted)
                Assert.That(Assert.Throws<InvalidOperationException>(() => asyncCall.GetAwaiter().GetResult())!.Message, Does.Contain("immediate"));
            else
                Assert.Throws<AmbiguousImplementationException>(() => asyncCall.GetAwaiter().GetResult());
#if UNITY_EDITOR
            Assert.That(dispatch.TestResolveCalls - before, Is.EqualTo(trusted ? 0 : 2));
#endif
        }

        [Test]
        public void P127Interface_SlotRuntimeProbeRunsOnce()
        {
            using var decision = new InterfaceSlotRuntimeDecision(null);
            var results = new bool[100];
            System.Threading.Tasks.Parallel.For(0, results.Length,
                index => results[index] = NeoGeneratedTypesSupport.TrustRuntimeInterfaceDispatch);
            bool actual = NeoGeneratedTypesSupport.TrustRuntimeInterfaceDispatch;
            TestContext.WriteLine($"Runtime interface dispatch trusted: {actual}");
            foreach (bool result in results)
                Assert.That(result, Is.EqualTo(actual));
#if UNITY_EDITOR
            Assert.That(NeoGeneratedTypesSupport.TestAmbiguityProbeRuns, Is.EqualTo(1));
#endif
        }

        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        public void P127Interface_SlotProbeFailureSelectsSdkResolution(int outcome, bool expected)
        {
            int Probe() => outcome switch
            {
                0 => 1,
                1 => throw new AmbiguousImplementationException(),
                _ => throw new InvalidProgramException("The runtime cannot execute this diamond."),
            };
            Assert.That(NeoGeneratedTypesSupport.ProbeRuntimeInterfaceDispatch(Probe), Is.EqualTo(expected));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void P127Interface_SlotDefaultUsesWinningHoldersClosedEnvironment(bool property)
        {
            using var decision = new InterfaceSlotRuntimeDecision(false);
            var resultType = new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = NeoObjectTypeKind.Type };
            var parameter = new GenericTypeInfo { type = MemberKind.Generic, required = true, ownerInterfaceId = "slot-holder", genericParamId = "U" };
            var action = Action(resultType, Array.Empty<FunctionArgumentTypeInfo>(), Return(new TypeOfPointer { typeInfo = parameter }));
            JsonMember body = property
                ? new NSPropertyMember
                {
                    id = "holder-body",
                    name = "M",
                    projectId = ProjectId,
                    kind = MemberKind.NSProperty,
                    code = "return typeof(U);",
                    returnTypeInfo = resultType,
                    getter = action
                }
                : ScriptFunction("holder-body", "M", false, resultType, Array.Empty<FunctionArgumentTypeInfo>(), action);
            InterfaceMember Member(string id) => new()
            {
                id = id,
                kind = property ? NeoInterfaceMemberKind.Property : NeoInterfaceMemberKind.Function,
                typeInfo = property ? resultType : null,
                returnTypeInfo = property ? null : resultType,
                argumentTypes = property ? null : new()
            };
            var contract = new Interface
            {
                id = "slot-contract",
                name = "ISlot",
                projectId = ProjectId,
                members = new() { ["M"] = Member("slot") },
                genericParams = new() { new() { id = "T", name = "T" } }
            };
            var implementation = Member("holder-slot");
            implementation.implementationMemberId = body.id;
            implementation.explicitImplementation = new()
            {
                interfaceId = contract.id,
                interfaceMemberId = "slot",
                arguments = new()
                {
                    ["T"] = TypeBinding.Open(new TypeDefinition(MemberKind.Interface, interfaceId: "slot-holder"), "V")
                }
            };
            var holder = new Interface
            {
                id = "slot-holder",
                name = "IHolder",
                projectId = ProjectId,
                genericParams = new() { new() { id = "V", name = "V" }, new() { id = "U", name = "U" } },
                extendsInterfaces = new() { new() { interfaceId = contract.id, arguments = implementation.explicitImplementation.arguments } },
                members = new() { ["M"] = implementation }
            };
            var owner = ReceiverClass();
            owner.implementsInterfaces = new()
            {
                new() { interfaceId = holder.id, arguments = new()
                {
                    ["V"] = TypeBinding.Closed(new TypeHead(MemberKind.Int, true)),
                    ["U"] = TypeBinding.Closed(new TypeHead(MemberKind.String, true))
                } }
            };
            using var client = BuildClient(new[] { body }, owner, interfaces: new()
            {
                [contract.id] = contract,
                [holder.id] = holder
            });
            var construction = new InterfaceTypeInfo
            {
                type = MemberKind.Interface,
                required = true,
                interfaceId = contract.id,
                typeArguments = new() { ["T"] = IntType() }
            };
            var caller = new InterfaceTypeInfo
            {
                type = MemberKind.Interface,
                required = true,
                interfaceId = holder.id,
                typeArguments = new() { ["V"] = IntType(), ["U"] = new PrimitiveTypeInfo { type = MemberKind.String, required = true } }
            };
            Assert.That(NeoGeneratedTypesSupport.InvokeInterfaceSlot(client, SlotReceiver(client), "slot",
                property ? NeoInterfaceBody.Get : NeoInterfaceBody.Function, construction, "holder-slot", caller, Array.Empty<object?>()), Is.EqualTo(typeof(string)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void P127Interface_SlotClassAnswerCanBeANativeFunction(bool deferred)
        {
            using var decision = new InterfaceSlotRuntimeDecision(false);
            var native = NativeFunction("slot-native", "M", deferred);
            var declaration = new Interface
            {
                id = "slot-contract",
                name = "ISlot",
                projectId = ProjectId,
                members = new()
                {
                    ["M"] = new()
                    {
                        id = "slot",
                        kind = NeoInterfaceMemberKind.Function,
                        Dispatch = deferred ? NeoFunctionDispatchKind.Asynchronous : NeoFunctionDispatchKind.Synchronous,
                        returnTypeInfo = IntType(),
                        argumentTypes = new()
                    }
                }
            };
            var owner = ReceiverClass(("M", native.id));
            owner.implementsInterfaces = new() { new() { interfaceId = declaration.id } };
            using var client = BuildClient(new JsonMember[] { native }, owner, interfaces: new()
            {
                [declaration.id] = declaration
            });
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                [native.id] = (_, _, _, _) => 51
            });
            client.RegisterDeferredNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
            {
                [native.id] = (_, _, _, _, handle) => NeoGeneratedTypesSupport.ResolveDeferredFunction<NeoDeferredFunction<int>>(handle, native.name).Complete(52)
            });
            object receiver = SlotReceiver(client);
            if (deferred)
            {
                Assert.Throws<InvalidOperationException>(() => NeoGeneratedTypesSupport.InvokeInterfaceSlot(client, receiver, "slot",
                    NeoInterfaceBody.Function, SlotConstruction(), "slot", SlotConstruction(), Array.Empty<object?>()));
                Assert.That(NeoGeneratedTypesSupport.InvokeInterfaceSlotAsync(client, receiver, "slot",
                    NeoInterfaceBody.Function, SlotConstruction(), "slot", SlotConstruction(), Array.Empty<object?>()).GetAwaiter().GetResult(), Is.EqualTo(52));
            }
            else
                Assert.That(NeoGeneratedTypesSupport.InvokeInterfaceSlot(client, receiver, "slot",
                    NeoInterfaceBody.Function, SlotConstruction(), "slot", SlotConstruction(), Array.Empty<object?>()), Is.EqualTo(51));
        }
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void P127Interface_SlotTrustedCallerWinsEvenWhenExactResolutionDiffers(bool trusted, bool property)
        {
            using var decision = new InterfaceSlotRuntimeDecision(trusted);
            using var client = InterfaceSlotClient(property, classImplementation: true);
            object receiver = SlotReceiver(client);
            var dispatch = NeoInterfaceDispatch.Of(client);
#if UNITY_EDITOR
            long before = dispatch.TestResolveCalls;
#endif
            for (int repeat = 0; repeat < 100; repeat++)
                Assert.That(NeoGeneratedTypesSupport.InvokeInterfaceSlot(client, receiver, "slot",
                    property ? NeoInterfaceBody.Get : NeoInterfaceBody.Function, SlotConstruction(), "slot", SlotConstruction(), Array.Empty<object?>()),
                    Is.EqualTo(trusted ? 42 : 17));
#if UNITY_EDITOR
            Assert.That(dispatch.TestResolveCalls - before, Is.EqualTo(trusted ? 0 : 100), "Trusted caller execution never resolves the implemented slot");
#endif
        }

        [Test]
        public void P127Interface_SlotRepeatedAmbiguityDoesNoWarmSearchWork()
        {
            using var decision = new InterfaceSlotRuntimeDecision(false);
            using var client = AmbiguousInterfaceSlotClient();
            object receiver = SlotReceiver(client);
            var caller = new InterfaceTypeInfo { type = MemberKind.Interface, required = true, interfaceId = "right" };
            object? Invoke() => NeoGeneratedTypesSupport.InvokeInterfaceSlot(client, receiver, "slot", NeoInterfaceBody.Function,
                SlotConstruction(), "right-slot", caller, Array.Empty<object?>());
            Assert.Throws<AmbiguousImplementationException>(() => Invoke());
#if UNITY_EDITOR
            var dispatch = NeoInterfaceDispatch.Of(client);
            var cold = (dispatch.TestDefaultOwnerVisits, dispatch.TestDefaultMemberVisits, dispatch.TestDefaultCandidateVisits, dispatch.TestConversions);
#endif
            for (int repeat = 0; repeat < 100; repeat++)
                Assert.Throws<AmbiguousImplementationException>(() => Invoke());
#if UNITY_EDITOR
            Assert.That((dispatch.TestDefaultOwnerVisits, dispatch.TestDefaultMemberVisits, dispatch.TestDefaultCandidateVisits, dispatch.TestConversions), Is.EqualTo(cold));
#endif
        }
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void P127Interface_SlotRequiresCallerOnEitherRuntime(bool trusted, bool missingMember)
        {
            using var decision = new InterfaceSlotRuntimeDecision(trusted);
            using var client = InterfaceSlotClient();
            string? member = missingMember ? null : "slot";
            var construction = missingMember ? SlotConstruction() : null;
            string parameter = missingMember ? "callerMemberId" : "callerConstruction";
            Assert.That(Assert.Throws<ArgumentNullException>(() => NeoGeneratedTypesSupport.InvokeInterfaceSlot(client,
                SlotReceiver(client), "slot", NeoInterfaceBody.Function, SlotConstruction(), member!, construction!, Array.Empty<object?>()))!.ParamName,
                Is.EqualTo(parameter));
            Assert.That(Assert.Throws<ArgumentNullException>(() => NeoGeneratedTypesSupport.InvokeInterfaceSlotAsync(client,
                SlotReceiver(client), "slot", NeoInterfaceBody.Function, SlotConstruction(), member!, construction!, Array.Empty<object?>()).GetAwaiter().GetResult())!.ParamName,
                Is.EqualTo(parameter));
        }
    }
}
