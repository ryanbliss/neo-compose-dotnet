// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class NSFunctionRuntimeTests
    {
        [Test]
        public void P127Interface_StaticListenersDeduplicatePerConstructionAndRunInItsFrame()
        {
            IntMember Field(string id) => new()
            {
                id = id,
                name = id,
                kind = MemberKind.Int,
                projectId = ProjectId,
                Modifier = NeoMemberModifierKind.Static,
                Storage = NeoMemberStorage.Save,
                Requirement = NeoMemberRequirementKind.Required
            };
            var held = Field("Held");
            var hits = Field("Hits");
            var last = Field("Last");
            var generic = new GenericTypeInfo { type = MemberKind.Generic, required = true, ownerClassId = "receiver-class", genericParamId = "T" };
            ClassTypeInfo Construction(TypeInfo argument) => new()
            {
                type = MemberKind.Class,
                required = true,
                classId = "receiver-class",
                typeArguments = new()
                {
                    ["T"] = argument
                }
            };
            StaticMemberPointer Slot(string id) => new()
            {
                memberId = id,
                staticConstruction = Construction(generic)
            };
            AssignInstruction Write(string id, Pointer value) => new()
            {
                operatorValue = "=",
                pointer = value,
                target = new()
                {
                    pointer = Slot(id),
                    typeInfo = IntType(),
                    writability = WritabilityKind.Save
                }
            };
            var arguments = new[] { new FunctionArgumentTypeInfo { type = MemberKind.Int, required = true } };
            var nothing = new VoidTypeInfo { type = MemberKind.Void, required = true };
            var handler = ScriptFunction("Handler", "Handler", false, nothing, arguments,
                Action(nothing, arguments, Write(hits.id, Add(Slot(hits.id), Number(1))), Write(last.id, Variable("__arg_0__"))));
            handler.Modifier = NeoMemberModifierKind.Static;
            handler.action!.typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true };
            var owner = ReceiverClass(("Held", held.id), ("Hits", hits.id), ("Last", last.id), ("Handler", handler.id));
            owner.genericParams = new() { new() { id = "T", name = "T" } };
            using var client = BuildClient(new NeoCompose.Runtime.Json.Member[] { held, hits, last, handler }, owner);
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var listener = new NeoDelegateValue { memberId = handler.id };
            var target = new ChangeListenerTarget { kind = "static", memberId = held.id, typeInfo = IntType(), writability = WritabilityKind.Save };
            void Edit(ChangeListenerInstruction instruction)
            {
                var body = Action(nothing, Array.Empty<FunctionArgumentTypeInfo>(), instruction);
                NeoScriptExecutor.Execute(client, body, new NeoScriptScope(), ctx, NeoScriptExecutionOptions.ForImmediate(client));
            }
            var add = new AddChangeListenerInstruction
            {
                type = "addChangeListener",
                target = target,
                listener = new ValuePointer { primitiveResolved = true, primitive = listener }
            };
            Edit(add);
            Edit(add);
            TypeBinding integer = NeoTypeBindings.FromTypeInfo(Construction(IntType()));
            TypeBinding text = NeoTypeBindings.FromTypeInfo(Construction(new PrimitiveTypeInfo { type = MemberKind.String, required = true }));
            var intHeld = client.ConstructedStaticBinding(held.id, integer);
            var textHeld = client.ConstructedStaticBinding(held.id, text);
            int Read(string id, TypeBinding type) => NeoGeneratedTypesSupport.ReadInt(client.ConstructedStaticBinding(id, type).GetRequiredNode<NeoMemberInt>())!.Value;
            Assert.That(Read(hits.id, integer), Is.Zero, "Initialization does not notify.");
            client.RunTransaction(() =>
            {
                intHeld.SetValue(NeoValueWritePayload.FromInt(1));
                intHeld.SetValue(NeoValueWritePayload.FromInt(3));
                textHeld.SetValue(NeoValueWritePayload.FromInt(7));
                Assert.That(Read(hits.id, integer), Is.Zero);
            });
            Assert.That(Read(hits.id, integer), Is.EqualTo(1));
            Assert.That(Read(hits.id, text), Is.EqualTo(1));
            Assert.That(Read(last.id, integer), Is.EqualTo(3));
            Assert.That(Read(last.id, text), Is.EqualTo(7));
            Edit(new RemoveChangeListenerInstruction { type = "removeChangeListener", target = target, listener = add.listener });
            intHeld.SetValue(NeoValueWritePayload.FromInt(9));
            Assert.That(Read(hits.id, integer), Is.EqualTo(1));
            Edit(add);
            Edit(new ClearChangeListenersInstruction { type = "clearChangeListeners", target = target });
            intHeld.SetValue(NeoValueWritePayload.FromInt(11));
            Assert.That(Read(hits.id, integer), Is.EqualTo(1));
            Edit(add);
            ctx.constructorBody = true;
            Assert.Throws<NSGetterRuntimeError>(() => client.EditStaticChangeListener(held.id, IntType(), listener, true, ctx));
            ctx.constructorBody = false;
            using var restored = NeoTestSaveStack.ClientFromSchema(client.ProjectDataForRuntime, loadedSaveContent: client.SerializeSaveData());
            restored.ConstructedStaticBinding(held.id, integer).SetValue(NeoValueWritePayload.FromInt(13));
            Assert.That(NeoGeneratedTypesSupport.ReadInt(restored.ConstructedStaticBinding(hits.id, integer).GetRequiredNode<NeoMemberInt>()), Is.EqualTo(1));
        }

        [Test]
        public void P127Interface_StaticListenerCollectionMutationDoesNotNotifyAndSignaturesAreChecked()
        {
            var entry = new GenericMember { id = "entry", kind = MemberKind.Generic, genericParamId = "T", Requirement = NeoMemberRequirementKind.Required };
            var generic = new GenericTypeInfo { type = MemberKind.Generic, required = true, ownerClassId = "receiver-class", genericParamId = "T" };
            var items = new ListMember
            {
                id = "items",
                name = "Items",
                kind = MemberKind.List,
                entryMemberId = entry.id,
                Modifier = NeoMemberModifierKind.Static,
                Storage = NeoMemberStorage.Session,
                Requirement = NeoMemberRequirementKind.Required
            };
            var handler = NativeFunction("handler", "Handler", false);
            handler.Modifier = NeoMemberModifierKind.Static;
            handler.returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true };
            handler.argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "value", type = MemberKind.List, required = true, entryTypeInfo = generic } };
            var bad = ScriptFunction("bad", "Bad", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(1))));
            bad.Modifier = NeoMemberModifierKind.Static;
            var owner = ReceiverClass(("Items", items.id), ("Handler", handler.id), ("Bad", bad.id));
            owner.genericParams = new() { new() { id = "T", name = "T" } };
            using var client = BuildClient(new NeoCompose.Runtime.Json.Member[] { entry, items, handler, bad }, owner);
            int calls = 0;
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                [handler.id] = (_, _, _, _) => { calls++; return null; },
            });
            var construction = new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = owner.id,
                typeArguments = new() { ["T"] = IntType() }
            };
            var context = new NSGetterEvaluator.Context(client, null, null);
            var listType = ListType(IntType());
            client.EditStaticChangeListener(items.id, ListType(generic), new NeoDelegateValue { memberId = handler.id }, true, context);
            var error = Assert.Throws<NSGetterRuntimeError>(() => client.EditStaticChangeListener(items.id, listType,
                new NeoDelegateValue { memberId = bad.id }, true, context));
            Assert.That(error!.Message, Does.Contain("return void"));
            var target = new WriteTarget
            {
                pointer = new StaticMemberPointer { type = PointerKind.StaticMember, memberId = items.id, staticConstruction = construction },
                typeInfo = listType,
                writability = WritabilityKind.Session
            };
            void Run(Instruction instruction) => NeoScriptExecutor.Execute(client,
                Action(new PrimitiveTypeInfo { type = MemberKind.Null, required = true }, Array.Empty<FunctionArgumentTypeInfo>(), instruction),
                new NeoScriptScope(), context, NeoScriptExecutionOptions.ForImmediate(client));
            Run(new AssignInstruction
            {
                type = InstructionKind.Assign,
                operatorValue = "=",
                target = target,
                pointer = new ListLiteralPointer { type = PointerKind.ListLiteral, typeInfo = listType, entries = Array.Empty<Pointer>() }
            });
            Assert.That(calls, Is.EqualTo(1));
            Run(new CollectionCallInstruction
            {
                type = InstructionKind.CollectionCall,
                target = target,
                mutation = CollectionMutationKind.Add,
                args = new Pointer[] { Number(7) }
            });
            Assert.That(calls, Is.EqualTo(1), "Only slot assignments notify, not in-place list edits.");
            Assert.That((object?[])NSGetterEvaluator.EvaluatePointer(target.pointer, new NeoScriptScope(), context)!, Has.Length.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void P127Interface_GenericClosureSiteSurvivesSerialization(bool literal)
        {
            var owner = ReceiverClass();
            owner.genericParams = new() { new() { id = "T", name = "T" } };
            using var client = BuildClient(Array.Empty<NeoCompose.Runtime.Json.Member>(), owner);
            var typeType = new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = NeoObjectTypeKind.Type };
            var generic = new GenericTypeInfo { type = MemberKind.Generic, required = true, ownerClassId = owner.id, genericParamId = "T" };
            var action = Action(typeType, Array.Empty<FunctionArgumentTypeInfo>(), Return(new TypeOfPointer { type = PointerKind.TypeOf, typeInfo = generic }));
            action.parameters[0].typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = false };
            var construction = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: owner.id),
                new Dictionary<string, TypeBinding> { ["T"] = TypeBinding.Closed(new TypeHead(MemberKind.String, true)) });
            var ctx = new NSGetterEvaluator.Context(client, null, null) { staticConstruction = construction };
            var lambda = new NeoDelegateValue { action = action, site = "lambda-owner:function:0" };
            Pointer pointer = literal ? new ValuePointer
            {
                value = new Value
                {
                    typeInfo = new DelegateTypeInfo
                    {
                        type = MemberKind.NSDelegate,
                        required = true,
                        returnTypeInfo = typeType,
                        argumentTypes = Array.Empty<FunctionArgumentTypeInfo>()
                    },
                    value = Newtonsoft.Json.Linq.JToken.FromObject(lambda)
                }
            }
                : new DelegateClosurePointer { action = action, site = lambda.site, captures = Array.Empty<Pointer>() };
            var bound = (NeoDelegateValue)NSGetterEvaluator.EvaluatePointer(pointer, new NeoScriptScope(), ctx)!;
            var restored = JsonConvert.DeserializeObject<NeoDelegateValue>(JsonConvert.SerializeObject(bound))!;
            Assert.That(restored.site, Is.EqualTo(lambda.site));
            Assert.That(restored.declaringConstruction, Is.EqualTo(construction));
            Assert.That(((NeoTypeValue)NSGetterEvaluator.InvokeDelegate(restored, Array.Empty<object?>(), new NSGetterEvaluator.Context(client, null, null))!).Binding.Head!.Type, Is.EqualTo(MemberKind.String));
        }

        [TestCase(false, 3)]
        [TestCase(true, 2)]
        public void P127Interface_DefaultAndSealedDispatch(bool sealedMember, int expected)
        {
            var body = ScriptFunction("default-body", "M", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(2))));
            var implementation = ScriptFunction("class-body", "M", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(3))));
            var contract = new InterfaceMember { id = "slot", kind = NeoInterfaceMemberKind.Function, returnTypeInfo = IntType(), argumentTypes = new(), implementationMemberId = body.id };
            if (sealedMember)
                contract.DeclaredModifier = NeoMemberModifierKind.Sealed;
            var declaration = new Interface { id = "contract", name = "I", projectId = ProjectId, members = new() { ["M"] = contract } };
            var owner = ReceiverClass(("M", implementation.id));
            owner.implementsInterfaces = new() { new() { interfaceId = declaration.id } };
            using var client = BuildClient(new NeoCompose.Runtime.Json.Member[] { body, implementation }, owner, interfaces: new()
            {
                [declaration.id] = declaration
            });
            var call = new CallFunctionPointer
            {
                type = PointerKind.CallFunction,
                callSiteId = "test",
                args = Array.Empty<Pointer>(),
                interfaceMember = new() { interfaceId = declaration.id, memberId = contract.id! },
                receiver = new() { kind = CallReceiverKind.Instance, pointer = new ReferencePointer { valueId = "receiver-value" } },
            };
            var context = new NSGetterEvaluator.Context(client, null, null);
            Assert.That(NSGetterEvaluator.EvaluatePointer(call, new NeoScriptScope(), context), Is.EqualTo(expected));
            Assert.That(NSGetterEvaluator.EvaluatePointer(call, new NeoScriptScope(), context), Is.EqualTo(expected));
        }

        [TestCase(MemberKind.Int)]
        [TestCase(MemberKind.String)]
        public void P127Interface_DefaultFrameAndBoundDelegateKeepConstruction(MemberKind argument)
        {
            var typeType = new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = NeoObjectTypeKind.Type };
            var generic = new GenericTypeInfo { type = MemberKind.Generic, required = true, ownerInterfaceId = "contract", genericParamId = "T" };
            var body = ScriptFunction("default-type", "Kind", false, typeType, Array.Empty<FunctionArgumentTypeInfo>(),
                Action(typeType, Array.Empty<FunctionArgumentTypeInfo>(), Return(new TypeOfPointer { typeInfo = generic })));
            var declaration = new Interface
            {
                id = "contract",
                name = "I",
                projectId = ProjectId,
                genericParams = new() { new() { id = "T", name = "T" } },
                members = new()
                {
                    ["Kind"] = new InterfaceMember
                    {
                        id = "slot",
                        kind = NeoInterfaceMemberKind.Function,
                        returnTypeInfo = typeType,
                        argumentTypes = new(),
                        implementationMemberId = body.id
                    }
                },
            };
            var owner = ReceiverClass();
            owner.implementsInterfaces = new()
            {
                new() { interfaceId = declaration.id, arguments = new() { ["T"] = TypeBinding.Closed(new TypeHead(MemberKind.Int, true)) } },
                new() { interfaceId = declaration.id, arguments = new() { ["T"] = TypeBinding.Closed(new TypeHead(MemberKind.String, true)) } },
            };
            using var client = BuildClient(new NeoCompose.Runtime.Json.Member[] { body }, owner, interfaces: new()
            {
                [declaration.id] = declaration
            });
            var reference = new InterfaceMemberRef
            {
                interfaceId = declaration.id,
                memberId = "slot",
                arguments = new()
                {
                    ["T"] = new PrimitiveTypeInfo { type = argument, required = true }
                }
            };
            var receiver = new CallReceiver { kind = CallReceiverKind.Instance, pointer = new ReferencePointer { valueId = "receiver-value" } };
            var context = new NSGetterEvaluator.Context(client, null, null);
            var call = new CallFunctionPointer { interfaceMember = reference, receiver = receiver, args = Array.Empty<Pointer>(), callSiteId = "generic-default" };
            var result = (NeoTypeValue)NSGetterEvaluator.EvaluatePointer(call, new NeoScriptScope(), context)!;
            Assert.That(result.Binding.Head!.Type, Is.EqualTo(argument));
            var group = (NeoDelegateValue)NSGetterEvaluator.EvaluatePointer(new MemberTargetPointer { receiver = receiver, interfaceMember = reference }, new NeoScriptScope(), context)!;
            var persisted = JsonConvert.DeserializeObject<NeoDelegateValue>(JsonConvert.SerializeObject(group))!;
            Assert.That(((NeoTypeValue)NSGetterEvaluator.InvokeDelegate(persisted, Array.Empty<object?>(), context)!).Binding.Head!.Type, Is.EqualTo(argument));
        }

        [TestCase(false, 20)]
        [TestCase(true, 10)]
        public void P127Interface_GetterOverrideAndDirectDispatch(bool direct, int expected)
        {
            var root = new NSPropertyMember
            {
                id = "root-property",
                name = "P",
                kind = MemberKind.NSProperty,
                code = "return 10;",
                returnTypeInfo = IntType(),
                getter = Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(10)))
            };
            var derived = new NSPropertyMember
            {
                id = "derived-property",
                name = "P",
                kind = MemberKind.NSProperty,
                extendsMemberId = root.id,
                code = "return 20;",
                getter = Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(20)))
            };
            var owner = ReceiverClass(("P", derived.id));
            owner.extendsClassId = "base-class";
            var baseClass = new NeoSchemaClass { id = owner.extendsClassId, name = "Base", projectId = ProjectId, schema = new() { ["P"] = root.id } };
            using var client = BuildClient(new NeoCompose.Runtime.Json.Member[] { root, derived }, owner, new[] { baseClass });
            var call = new CallGetterPointer
            {
                memberId = root.id,
                direct = direct,
                receiver = new() { kind = CallReceiverKind.Instance, pointer = new ReferencePointer { valueId = "receiver-value" } }
            };
            Assert.That(NSGetterEvaluator.EvaluatePointer(call, new NeoScriptScope(), new NSGetterEvaluator.Context(client, null, null)), Is.EqualTo(expected));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void P127Interface_ImplicitResultTypeMustMatchRequestedConstruction(bool property)
        {
            var generic = new GenericTypeInfo { type = MemberKind.Generic, required = true, ownerInterfaceId = "result-contract", genericParamId = "result-T" };
            var textType = new PrimitiveTypeInfo { type = MemberKind.String, required = true };
            var arguments = Array.Empty<FunctionArgumentTypeInfo>();
            var defaultAction = Action(generic, arguments, Return(new DefaultPointer { typeInfo = generic }));
            var classAction = Action(textType, arguments, Return(Text("class")));
            NeoCompose.Runtime.Json.Member body = property
                ? new NSPropertyMember { id = "default-result", name = "Result", projectId = ProjectId, kind = MemberKind.NSProperty, code = "return default(T);", returnTypeInfo = generic, getter = defaultAction }
                : ScriptFunction("default-result", "Result", false, generic, arguments, defaultAction);
            NeoCompose.Runtime.Json.Member implementation = property
                ? new NSPropertyMember { id = "class-result", name = "Result", projectId = ProjectId, kind = MemberKind.NSProperty, code = "return \"class\";", returnTypeInfo = textType, getter = classAction }
                : ScriptFunction("class-result", "Result", false, textType, arguments, classAction);
            var declaration = new Interface
            {
                id = "result-contract",
                name = "IResult",
                projectId = ProjectId,
                genericParams = new() { new() { id = "result-T", name = "T" } },
                members = new()
                {
                    ["Result"] = new InterfaceMember
                    {
                        id = "result-slot",
                        kind = property ? NeoInterfaceMemberKind.Property : NeoInterfaceMemberKind.Function,
                        typeInfo = property ? generic : null,
                        returnTypeInfo = property ? null : generic,
                        argumentTypes = property ? null : new(),
                        implementationMemberId = body.id
                    }
                }
            };
            var owner = ReceiverClass(("Result", implementation.id));
            InterfaceConstruction Declared(MemberKind kind) => new()
            {
                interfaceId = declaration.id,
                arguments = new()
                {
                    ["result-T"] = TypeBinding.Closed(new TypeHead(kind, true))
                }
            };
            owner.implementsInterfaces = new() { Declared(MemberKind.String), Declared(MemberKind.Int) };
            using var client = BuildClient(new[] { body, implementation }, owner, interfaces: new()
            {
                [declaration.id] = declaration
            });
            var receiver = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: owner.id));
            TypeBinding Requested(MemberKind kind) => TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: declaration.id), Declared(kind).arguments);
            var dispatch = NeoInterfaceDispatch.Of(client);
            string accessor = property ? "get" : "call";
            Assert.That(dispatch.Resolve(receiver, Requested(MemberKind.String), "result-slot", accessor).MemberId, Is.EqualTo(implementation.id));
            var fallback = dispatch.Resolve(receiver, Requested(MemberKind.Int), "result-slot", accessor);
            Assert.That(fallback.MemberId, Is.EqualTo(body.id));
            Assert.That(fallback.InterfaceConstruction, Is.EqualTo(Requested(MemberKind.Int)));
        }

        [Test]
        public void P127Interface_IndexedMappingsKeepExplicitImplicitAndVirtualSlots()
        {
            const int count = 6;
            var records = new List<NeoCompose.Runtime.Json.Member>();
            var contract = new Interface { id = "scaling-interface", name = "IScaling", projectId = ProjectId, members = new() };
            var root = ReceiverClass();
            root.id = "scaling-root";
            root.implementsInterfaces = new() { new() { interfaceId = contract.id } };
            var receiver = ReceiverClass();
            receiver.extendsClassId = root.id;
            var expected = new string[count];
            for (int i = 0; i < count; i++)
            {
                string key = "P" + i;
                string slot = "slot-" + i;
                contract.members[key] = new InterfaceMember { id = slot, kind = NeoInterfaceMemberKind.Property, typeInfo = IntType(), Accessors = NeoPropertyAccessorsKind.Get };
                if (i % 3 == 2)
                {
                    var first = new NSPropertyMember
                    {
                        id = "base-" + i,
                        name = key,
                        projectId = ProjectId,
                        kind = MemberKind.NSProperty,
                        Modifier = NeoMemberModifierKind.Virtual,
                        code = "return 1;",
                        returnTypeInfo = IntType(),
                        getter = Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(1)))
                    };
                    var last = new NSPropertyMember
                    {
                        id = "override-" + i,
                        name = key,
                        projectId = ProjectId,
                        kind = MemberKind.NSProperty,
                        extendsMemberId = first.id,
                        code = "return 2;",
                        returnTypeInfo = IntType(),
                        getter = Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(2)))
                    };
                    root.schema[key] = first.id;
                    receiver.schema[key] = last.id;
                    records.Add(first);
                    records.Add(last);
                    expected[i] = last.id;
                }
                else
                {
                    var field = new IntMember { id = "field-" + i, name = key, projectId = ProjectId, kind = MemberKind.Int, Requirement = NeoMemberRequirementKind.Required };
                    if (i % 3 == 1)
                        field.explicitImplementation = new()
                        {
                            interfaceId = contract.id,
                            interfaceMemberId = slot
                        };
                    root.schema[i % 3 == 1 ? key + "#" + field.id : key] = field.id;
                    records.Add(field);
                    expected[i] = field.id;
                }
            }
            var target = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: contract.id));
            var actual = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: receiver.id));
            using var client = BuildClient(records.ToArray(), receiver, new[] { root }, interfaces: new()
            {
                [contract.id] = contract
            });
            var dispatch = NeoInterfaceDispatch.Of(client);
            for (int i = 0; i < count; i++)
            {
                var resolved = dispatch.Resolve(actual, target, "slot-" + i, "get");
                Assert.That(resolved.MemberId, Is.EqualTo(expected[i]));
                Assert.That(dispatch.Resolve(actual, target, "slot-" + i, "get"), Is.SameAs(resolved));
            }
        }

        [TestCase(NeoMemberStorage.Save)]
        [TestCase(NeoMemberStorage.Session)]
        public void P127Interface_PartiallyRestoredConstructionInitializesMissingFields(NeoMemberStorage missingStorage)
        {
            IntMember Field(string id, string name, NeoMemberStorage storage) => new()
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Int,
                Modifier = NeoMemberModifierKind.Static,
                Storage = storage,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = id + "-template"
            };
            var saved = Field("saved", "Saved", NeoMemberStorage.Save);
            var missing = Field("missing", "Missing", missingStorage);
            var owner = ReceiverClass(("Saved", saved.id), ("Missing", missing.id));
            owner.genericParams = new() { new() { id = "T", name = "T" } };
            using var client = BuildClient(new NeoCompose.Runtime.Json.Member[] { saved, missing }, owner,
                additionalValues: new MemberValue[]
                {
                    new NumberMemberValue { id = saved.valueId!, value = 4, createdAt = "x", updatedAt = "x" },
                    new NumberMemberValue { id = missing.valueId!, value = 17, createdAt = "x", updatedAt = "x" }
                });
            var construction = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: owner.id),
                new Dictionary<string, TypeBinding> { ["T"] = TypeBinding.Closed(new TypeHead(MemberKind.Int, true)) });
            client.ConstructedStaticBinding(saved.id, construction).SetValue(NeoValueWritePayload.FromInt(9));
            var snapshot = JsonConvert.DeserializeObject<ProjectSaveData>(client.SerializeSaveData())!;
            snapshot.staticConstructions!.RemoveAll(entry => entry.memberId == missing.id);
            using var restored = NeoTestSaveStack.ClientFromSchema(client.ProjectDataForRuntime,
                loadedSaveContent: JsonConvert.SerializeObject(snapshot));
            int? Read(string id) => NeoGeneratedTypesSupport.ReadInt(restored.ConstructedStaticBinding(id, construction).GetRequiredNode<NeoMemberInt>());
            Assert.That(Read(saved.id), Is.EqualTo(9), "The saved field must not rerun its initializer.");
            Assert.That(Read(missing.id), Is.EqualTo(17), "Only the missing field runs its initializer.");
            Assert.That(Read(missing.id), Is.EqualTo(17));
        }

        [Test]
        public void P127Interface_CandidateConstructionOwnersFollowRebindTombstoneAndRollback()
        {
            using var client = BuildClient(Array.Empty<NeoCompose.Runtime.Json.Member>(), ReceiverClass());
            var construction = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: "receiver-class"));
            var plan = new NeoWritePlan(client);
            plan.BindConstruction(NeoValueOwnership.Save, "a", construction, "shared");
            plan.BindConstruction(NeoValueOwnership.Save, "b", construction, "shared");
            plan.BindConstruction(NeoValueOwnership.Session, "c", construction, "shared");
            using (client.ReadCandidate(plan))
            {
                Assert.That(client.ConstructionOfValue(NeoValueOwnership.Save, "shared")!.memberId, Is.EqualTo("a").Or.EqualTo("b"));
                Assert.That(client.ConstructionOfValue(NeoValueOwnership.Session, "shared")!.memberId, Is.EqualTo("c"));
                var outer = plan.Open();
                plan.BindConstruction(NeoValueOwnership.Save, "a", construction, "new");
                Assert.That(client.ConstructionOfValue(NeoValueOwnership.Save, "shared")!.memberId, Is.EqualTo("b"));
                Assert.That(client.ConstructionOfValue(NeoValueOwnership.Save, "new")!.memberId, Is.EqualTo("a"));
                var inner = plan.Open();
                plan.BindConstruction(NeoValueOwnership.Save, "b", construction, null);
                plan.BindConstruction(NeoValueOwnership.Save, "d", construction, "added");
                Assert.That(client.ConstructionOfValue(NeoValueOwnership.Save, "shared"), Is.Null);
                plan.Rollback(inner);
                Assert.That(client.ConstructionOfValue(NeoValueOwnership.Save, "shared")!.memberId, Is.EqualTo("b"));
                Assert.That(client.ConstructionOfValue(NeoValueOwnership.Save, "added"), Is.Null);
                plan.Rollback(outer);
                Assert.That(client.ConstructionOfValue(NeoValueOwnership.Save, "new"), Is.Null);
                plan.BindConstruction(NeoValueOwnership.Save, "b", construction, null);
                Assert.That(client.ConstructionOfValue(NeoValueOwnership.Save, "shared")!.memberId, Is.EqualTo("a"));
            }
        }

        [Test]
        public void P127Interface_GenericStaticsHaveSeparateStateAndPersist()
        {
            var count = new IntMember
            {
                id = "count",
                projectId = ProjectId,
                name = "Count",
                kind = MemberKind.Int,
                Modifier = NeoMemberModifierKind.Static,
                Storage = NeoMemberStorage.Save,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = "count-template"
            };
            var owner = ReceiverClass(("Count", count.id));
            owner.genericParams = new() { new() { id = "T", name = "T" } };
            using var client = BuildClient(new NeoCompose.Runtime.Json.Member[] { count }, owner, additionalValues: new MemberValue[]
            {
                new NumberMemberValue { id = count.valueId, value = 4, createdAt = "x", updatedAt = "x" },
            });
            TypeBinding Construction(MemberKind kind) => TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: owner.id),
                new Dictionary<string, TypeBinding> { ["T"] = TypeBinding.Closed(new TypeHead(kind, true)) });
            var integer = client.ConstructedStaticBinding(count.id, Construction(MemberKind.Int));
            var floating = client.ConstructedStaticBinding(count.id, Construction(MemberKind.Float));
            Assert.That(integer.ValueId, Is.Not.EqualTo(floating.ValueId));
            integer.SetValue(NeoValueWritePayload.FromInt(9));
            Assert.That(NeoGeneratedTypesSupport.ReadInt(integer.GetRequiredNode<NeoMemberInt>()), Is.EqualTo(9));
            Assert.That(NeoGeneratedTypesSupport.ReadInt(floating.GetRequiredNode<NeoMemberInt>()), Is.EqualTo(4));
            var context = new NSGetterEvaluator.Context(client, null, null);
            var reference = (NSSlotReference)NSGetterEvaluator.EvaluatePointer(new RowOfPointer
            {
                place = new StaticMemberPointer { memberId = count.id, staticConstruction = NeoTypeBindings.ToTypeInfo(Construction(MemberKind.Int)) },
                typeInfo = IntType(),
            }, new NeoScriptScope(), context)!;
            Assert.That(reference.TargetId, Is.EqualTo(integer.ValueId));
            Assert.That(NSGetterEvaluator.ReadThroughReference(reference, context), Is.EqualTo(9));
            var storedReference = new NSSlotReference(reference.TargetId, null, IntType(), null);
            Assert.That(NSGetterEvaluator.ReadThroughReference(storedReference, context), Is.EqualTo(9));
            var write = Action(new PrimitiveTypeInfo { type = MemberKind.Null, required = true }, Array.Empty<FunctionArgumentTypeInfo>(),
                new AssignInstruction
                {
                    operatorValue = "=",
                    pointer = Number(13),
                    target = new WriteTarget
                    {
                        pointer = new DerefPointer { pointer = new ValuePointer { primitiveResolved = true, primitive = reference } },
                        typeInfo = IntType(),
                        writability = WritabilityKind.Runtime
                    }
                });
            NeoScriptExecutor.Execute(client, write, new NeoScriptScope(), context, NeoScriptExecutionOptions.ForImmediate(client));
            Assert.That(NSGetterEvaluator.ReadThroughReference(storedReference, context), Is.EqualTo(13));
            integer.SetValue(NeoValueWritePayload.FromInt(9));
            var saved = JsonConvert.DeserializeObject<ProjectSaveData>(client.SerializeSaveData())!;
            Assert.That(saved.staticConstructions, Has.Count.EqualTo(2));
            using var restored = NeoTestSaveStack.ClientFromSchema(client.ProjectDataForRuntime, loadedSaveContent: client.SerializeSaveData());
            Assert.That(NeoGeneratedTypesSupport.ReadInt(restored.ConstructedStaticBinding(count.id, Construction(MemberKind.Int)).GetRequiredNode<NeoMemberInt>()), Is.EqualTo(9));
            Assert.That(client.ProjectDataForRuntime.values[count.valueId], Is.TypeOf<NumberMemberValue>());
            Assert.That(((NumberMemberValue)client.ProjectDataForRuntime.values[count.valueId]).value, Is.EqualTo(4));
        }

        [Test]
        public void P127Interface_ConstructionWriteRollbackAndSnapshotRestoreKeepSeparateBindings()
        {
            var count = new IntMember
            {
                id = "count",
                name = "Count",
                kind = MemberKind.Int,
                projectId = ProjectId,
                Modifier = NeoMemberModifierKind.Static,
                Storage = NeoMemberStorage.Save,
                Requirement = NeoMemberRequirementKind.Required
            };
            var owner = ReceiverClass(("Count", count.id));
            owner.genericParams = new() { new() { id = "T", name = "T" } };
            using var client = BuildClient(new NeoCompose.Runtime.Json.Member[] { count }, owner);
            TypeBinding Of(MemberKind kind) => TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: owner.id),
                new Dictionary<string, TypeBinding> { ["T"] = TypeBinding.Closed(new TypeHead(kind, true)) });
            var integer = client.ConstructedStaticBinding(count.id, Of(MemberKind.Int));
            var text = client.ConstructedStaticBinding(count.id, Of(MemberKind.String));
            integer.SetValue(NeoValueWritePayload.FromInt(3));
            text.SetValue(NeoValueWritePayload.FromInt(5));
            string snapshot = client.SerializeSaveData();
            int? Read(NeoStaticBinding binding) => NeoGeneratedTypesSupport.ReadInt(binding.GetRequiredNode<NeoMemberInt>());
            var plan = new NeoWritePlan(client);
            void Bind(TypeBinding construction, string id, int value)
            {
                plan.Set(NeoValueOwnership.Save, new NumberMemberValue { id = id, value = value, createdAt = "x", updatedAt = "x" });
                plan.BindConstruction(NeoValueOwnership.Save, count.id, construction, id);
            }
            Bind(Of(MemberKind.Int), "kept", 11);
            var outer = plan.Open();
            Bind(Of(MemberKind.String), "rolled-back-string", 19);
            var inner = plan.Open();
            Bind(Of(MemberKind.Int), "rolled-back-int", 23);
            plan.Rollback(inner);
            Assert.That(plan.ConstructionBindings![(NeoValueOwnership.Save, count.id, Of(MemberKind.Int))], Is.EqualTo("kept"));
            plan.Rollback(outer);
            Assert.That(plan.ConstructionBindings, Has.Count.EqualTo(1));
            plan.Commit();
            Assert.That(Read(integer), Is.EqualTo(11));
            Assert.That(Read(text), Is.EqualTo(5));
            bool notified = false;
            client.OnWritableValuesChanged += _ =>
            {
                notified = true;
                Assert.That(Read(integer), Is.EqualTo(3), "The restored binding is installed before row notifications.");
                Assert.That(Read(text), Is.EqualTo(5));
            };
            client.ApplyExternalSaveContent(snapshot);
            Assert.That(notified, Is.True);
            Assert.That(Read(integer), Is.EqualTo(3));
            Assert.That(Read(text), Is.EqualTo(5));
        }

        [Test]
        public void P127Interface_PropertyMapsGetterAndSetterSeparately()
        {
            var stored = new IntMember
            {
                id = "stored",
                projectId = ProjectId,
                name = "Stored",
                kind = MemberKind.Int,
                Modifier = NeoMemberModifierKind.Static,
                Storage = NeoMemberStorage.Save,
                Requirement = NeoMemberRequirementKind.Required
            };
            var root = new NSPropertyMember
            {
                id = "root-property",
                name = "P",
                kind = MemberKind.NSProperty,
                code = "return 1;",
                setterCode = "Stored = value;",
                returnTypeInfo = IntType(),
                getter = Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(1))),
                setter = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = Array.Empty<Variable>(),
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                    instructions = Array.Empty<Instruction>()
                }
            };
            var derived = new NSPropertyMember
            {
                id = "derived-property",
                name = "P",
                kind = MemberKind.NSProperty,
                extendsMemberId = root.id,
                setterCode = "Stored = value;",
                DeclaredAccessors = NeoPropertyAccessorsKind.Set,
                setter = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = Array.Empty<Variable>(),
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                    instructions = new Instruction[] { new AssignInstruction { operatorValue = "=", pointer = Variable("__value__"),
                        target = new WriteTarget { pointer = new StaticMemberPointer { memberId = stored.id }, typeInfo = IntType(), writability = WritabilityKind.Save } } }
                }
            };
            var contract = new Interface
            {
                id = "contract",
                name = "I",
                projectId = ProjectId,
                members = new()
                {
                    ["P"] = new InterfaceMember { id = "slot", kind = NeoInterfaceMemberKind.Property, typeInfo = IntType(), Accessors = NeoPropertyAccessorsKind.GetSet }
                }
            };
            var baseClass = new NeoSchemaClass
            {
                id = "base-class",
                name = "Base",
                projectId = ProjectId,
                schema = new() { ["P"] = root.id },
                implementsInterfaces = new() { new() { interfaceId = contract.id } }
            };
            var owner = ReceiverClass(("P", derived.id), ("Stored", stored.id));
            owner.extendsClassId = baseClass.id;
            using var client = BuildClient(new NeoCompose.Runtime.Json.Member[] { root, derived, stored }, owner,
                additionalClasses: new[] { baseClass }, interfaces: new()
                {
                    [contract.id] = contract
                });
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var pointer = new CallGetterPointer
            {
                receiver = new CallReceiver { kind = CallReceiverKind.Instance, pointer = new ReferencePointer { valueId = "receiver-value" } },
                interfaceMember = new() { interfaceId = contract.id, memberId = "slot" }
            };
            Assert.That(NSGetterEvaluator.EvaluatePointer(pointer, new NeoScriptScope(), ctx), Is.EqualTo(1));
            NSGetterEvaluator.WriteInterfacePointer(pointer, 11, IntType(), new NeoScriptScope(), ctx);
            Assert.That(NSGetterEvaluator.EvaluatePointer(new StaticMemberPointer { memberId = stored.id }, new NeoScriptScope(), ctx), Is.EqualTo(11));
            Assert.That(NSGetterEvaluator.EvaluatePointer(pointer, new NeoScriptScope(), ctx), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void P127Interface_StaticTypeParameterUsesDefaultOrClassBody(bool classBody)
        {
            var body = ScriptFunction("static-body", "Make", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(17))));
            body.Modifier = NeoMemberModifierKind.Static;
            var contract = new InterfaceMember
            {
                id = "slot",
                kind = NeoInterfaceMemberKind.Function,
                IsStatic = true,
                returnTypeInfo = IntType(),
                argumentTypes = new(),
                implementationMemberId = classBody ? null : body.id
            };
            var declaration = new Interface { id = "contract", name = "I", projectId = ProjectId, members = new() { ["Make"] = contract } };
            var owner = classBody ? ReceiverClass(("Make", body.id)) : ReceiverClass();
            owner.implementsInterfaces = new() { new() { interfaceId = declaration.id } };
            using var client = BuildClient(new NeoCompose.Runtime.Json.Member[] { body }, owner, interfaces: new()
            {
                [declaration.id] = declaration
            });
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            ctx.BindFunctionTypeArguments(new TypeInfo[] { classBody
                ? new ClassTypeInfo { type = MemberKind.Class, required = true, classId = owner.id }
                : new InterfaceTypeInfo { type = MemberKind.Interface, required = true, interfaceId = declaration.id } });
            var pointer = new TypeParameterStaticPointer
            {
                typeParameter = new FunctionGenericTypeInfo { type = MemberKind.Generic, ownerMemberId = "caller", index = 0, required = true },
                interfaceMember = new() { interfaceId = declaration.id, memberId = "slot" },
                operation = new() { kind = "call", args = Array.Empty<Pointer>(), callSiteId = "static-call" },
            };
            Assert.That(NSGetterEvaluator.EvaluatePointer(pointer, new NeoScriptScope(), ctx), Is.EqualTo(17));
            pointer.operation = new()
            {
                kind = "methodGroup"
            };
            var group = (NeoDelegateValue)NSGetterEvaluator.EvaluatePointer(pointer, new NeoScriptScope(), ctx)!;
            var restored = JsonConvert.DeserializeObject<NeoDelegateValue>(JsonConvert.SerializeObject(group))!;
            Assert.That(NSGetterEvaluator.InvokeDelegate(restored, Array.Empty<object?>(), ctx), Is.EqualTo(17));
        }

        [Test]
        public void P127Interface_StaticFunctionAndMethodGroupCloseTheDeclaringFrame()
        {
            var typeType = new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = NeoObjectTypeKind.Type };
            var generic = new GenericTypeInfo { type = MemberKind.Generic, required = true, ownerClassId = "receiver-class", genericParamId = "T" };
            var body = ScriptFunction("static-type", "Kind", false, typeType, Array.Empty<FunctionArgumentTypeInfo>(),
                Action(typeType, Array.Empty<FunctionArgumentTypeInfo>(), Return(new TypeOfPointer { typeInfo = generic })));
            body.Modifier = NeoMemberModifierKind.Static;
            var count = new IntMember
            {
                id = "count",
                projectId = ProjectId,
                name = "Count",
                kind = MemberKind.Int,
                Modifier = NeoMemberModifierKind.Static,
                Storage = NeoMemberStorage.Save,
                Requirement = NeoMemberRequirementKind.Required
            };
            var owner = ReceiverClass(("Kind", body.id), ("Count", count.id));
            owner.genericParams = new() { new() { id = "T", name = "T" } };
            using var client = BuildClient(new NeoCompose.Runtime.Json.Member[] { body, count }, owner);
            var construction = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = owner.id, typeArguments = new() { ["T"] = IntType() } };
            var receiver = new CallReceiver { kind = CallReceiverKind.Static, memberId = body.id };
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var call = new CallFunctionPointer { memberId = body.id, receiver = receiver, args = Array.Empty<Pointer>(), staticConstruction = construction, callSiteId = "generic-static" };
            Assert.That(((NeoTypeValue)NSGetterEvaluator.EvaluatePointer(call, new NeoScriptScope(), ctx)!).Binding.Head!.Type, Is.EqualTo(MemberKind.Int));
            var group = (NeoDelegateValue)NSGetterEvaluator.EvaluatePointer(new MemberTargetPointer { memberId = body.id, receiver = receiver, staticConstruction = construction }, new NeoScriptScope(), ctx)!;
            var restored = JsonConvert.DeserializeObject<NeoDelegateValue>(JsonConvert.SerializeObject(group))!;
            Assert.That(((NeoTypeValue)NSGetterEvaluator.InvokeDelegate(restored, Array.Empty<object?>(), ctx)!).Binding.Head!.Type, Is.EqualTo(MemberKind.Int));
            Assert.That(JsonConvert.DeserializeObject<ProjectSaveData>(client.SerializeSaveData())!.staticConstructions, Has.Count.EqualTo(1));
            Assert.That(NSGetterEvaluator.EvaluatePointer(new StaticMemberPointer { memberId = count.id, staticConstruction = construction }, new NeoScriptScope(), ctx), Is.EqualTo(0));
        }

        [Test]
        public void P127Interface_FailedInitializationIsStickyAndPublishesNoConstruction()
        {
            var first = new IntMember
            {
                id = "first",
                projectId = ProjectId,
                name = "First",
                kind = MemberKind.Int,
                Modifier = NeoMemberModifierKind.Static,
                Storage = NeoMemberStorage.Save,
                Requirement = NeoMemberRequirementKind.Required
            };
            var second = new IntMember
            {
                id = "second",
                projectId = ProjectId,
                name = "Second",
                kind = MemberKind.Int,
                Modifier = NeoMemberModifierKind.Static,
                Storage = NeoMemberStorage.Save,
                Requirement = NeoMemberRequirementKind.Required
            };
            var owner = ReceiverClass(("First", first.id), ("Second", second.id));
            owner.schemaKeyOrder = new[] { "First", "Second" };
            owner.genericParams = new() { new() { id = "T", name = "T" } };
            using var client = BuildClient(new NeoCompose.Runtime.Json.Member[] { first, second }, owner);
            // Inject a failing initializer after import to exercise the defensive runtime path.
            second.valueId = "missing-initializer";
            var construction = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: owner.id), new Dictionary<string, TypeBinding> { ["T"] = TypeBinding.Closed(new TypeHead(MemberKind.Int, true)) });
            var error = Assert.Throws<NeoRuntimeError>(() => client.ConstructedStaticBinding(first.id, construction));
            Assert.That(error!.ErrorClass, Is.EqualTo(NeoErrorClass.NeoTypeInitializationError));
            Assert.That(error.InnerException, Is.Not.Null);
            second.valueId = null;
            var repeated = Assert.Throws<NeoRuntimeError>(() => client.ConstructedStaticBinding(first.id, construction));
            Assert.That(repeated!.InnerException, Is.SameAs(error.InnerException));
            Assert.That(JsonConvert.DeserializeObject<ProjectSaveData>(client.SerializeSaveData())!.staticConstructions, Is.Null.Or.Empty);
        }

        [Test]
        public void P127Interface_VarianceKeepsAllConstructionsAndRejectsValueArguments()
        {
            var declaration = new Interface
            {
                id = "contract",
                name = "I",
                projectId = ProjectId,
                members = new(),
                genericParams = new() { new() { id = "T", name = "T", variance = NeoGenericParamVarianceKind.Out } }
            };
            var owner = ReceiverClass();
            owner.implementsInterfaces = new()
            {
                new() { interfaceId = declaration.id, arguments = new() { ["T"] = TypeBinding.Closed(new TypeHead(MemberKind.String, true)) } },
                new() { interfaceId = declaration.id, arguments = new() { ["T"] = TypeBinding.Closed(new TypeHead(MemberKind.Int, true)) } },
            };
            using var client = BuildClient(Array.Empty<NeoCompose.Runtime.Json.Member>(), owner, interfaces: new()
            {
                [declaration.id] = declaration
            });
            var cache = NeoInterfaceDispatch.Of(client);
            var receiver = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: owner.id));
            TypeBinding Target(MemberKind kind) => TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: declaration.id),
                new Dictionary<string, TypeBinding> { ["T"] = TypeBinding.Closed(new TypeHead(kind, true)) });
            Assert.That(cache.Closure(receiver), Has.Count.EqualTo(2));
            Assert.That(cache.Closure(receiver), Is.SameAs(cache.Closure(receiver)));
            Assert.That(cache.Converts(receiver, Target(MemberKind.Object)), Is.True);
            Assert.That(cache.Converts(Target(MemberKind.Int), Target(MemberKind.Object)), Is.False);
            Assert.That(client.TypeRules.Assignable(client, NeoTypeBindings.ToTypeInfo(receiver), NeoTypeBindings.ToTypeInfo(Target(MemberKind.Object))), Is.True);
            Assert.That(client.TypeRules.Assignable(client, NeoTypeBindings.ToTypeInfo(receiver), NeoTypeBindings.ToTypeInfo(Target(MemberKind.Int))), Is.True);
        }
    }
}
