// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    public partial class NSFunctionRuntimeTests
    {
        [Test]
        public void P127GeneratedDeferredInterfaceBodyResumesItsOwnCall()
        {
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
            Assert.Throws<InvalidOperationException>(() => NeoGeneratedTypesSupport.InvokeInterfaceMember(client, target, "slot",
                NeoInterfaceBody.Function, construction, Array.Empty<object?>()));
            var first = NeoGeneratedTypesSupport.InvokeInterfaceMemberAsync(client, target, "slot", NeoInterfaceBody.Function, construction, Array.Empty<object?>());
            var second = NeoGeneratedTypesSupport.InvokeInterfaceMemberAsync(client, target, "slot", NeoInterfaceBody.Function, construction, Array.Empty<object?>());
            Assert.That(first.IsCompleted, Is.False);
            pending[1].Complete(22);
            pending[0].Complete(11);
            Assert.That(first.GetAwaiter().GetResult(), Is.EqualTo(11));
            Assert.That(second.GetAwaiter().GetResult(), Is.EqualTo(22));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void P127NativeTypeAndBoxCrossInBothDirections(bool typeValue)
        {
            var type = new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = typeValue ? NeoObjectTypeKind.Type : null };
            var native = NativeFunction("native-identity", "Identity", false);
            native.Modifier = NeoMemberModifierKind.Static;
            native.argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "value", type = MemberKind.Object, required = true, objectType = type.objectType } };
            native.returnTypeInfo = type;
            using var client = BuildClient(new JsonMember[] { native }, ReceiverClass(("Identity", native.id)));
            object original = typeValue ? (object)NeoTypeValue.Of(TypeBinding.Closed(new TypeHead(MemberKind.Int, true)))
                : NeoBox.Of(TypeBinding.Closed(new TypeHead(MemberKind.Int, true)), 17d);
            object? first = null;
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                [native.id] = (_, _, args, _) =>
                {
                    Assert.That(args[0], typeValue ? Is.EqualTo(typeof(int)) : Is.EqualTo(17));
                    if (first is not null)
                        Assert.That(args[0], Is.SameAs(first));
                    first = args[0];
                    return args[0];
                },
            });
            Assert.That(client.InvokeNativeFunction(native.id, null, new object?[] { original }), Is.SameAs(original));
            Assert.That(client.InvokeNativeFunction(native.id, null, new object?[] { original }), Is.SameAs(original));
        }

        private sealed class GeneratedProvider : IFormatProvider
        {
            internal readonly NumberFormatInfo Format = new();
            public object? GetFormat(Type? type) => type == typeof(NumberFormatInfo) ? Format : null;
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void P127GeneratedProviderFunctionKeepsItsHostObject(int kind)
        {
            string id = NeoPrimitiveConformance.InterfaceId("IFormatProvider");
            var type = new InterfaceTypeInfo { type = MemberKind.Interface, required = true, interfaceId = id };
            var args = new[] { new FunctionArgumentTypeInfo { name = "provider", type = MemberKind.Interface, required = true, interfaceId = id } };
            var function = ScriptFunction("provider-identity", "Identity", false, type, args, Action(type, args, Return(Variable("__arg_0__"))));
            function.Modifier = NeoMemberModifierKind.Static;
            using var client = BuildClient(new JsonMember[] { function }, ReceiverClass(("Identity", function.id)), interfaces: new()
            {
                [id] = new Interface { id = id, name = "IFormatProvider", members = new(), projectId = ProjectId },
            });
            IFormatProvider original = kind == 0 ? (IFormatProvider)new CultureInfo("") : kind == 1 ? new NumberFormatInfo() : new GeneratedProvider();
            var call = NeoMemberNSFunction.GetOrCreateStatic(client, function.id);
            Assert.That(call.InvokeStatic(original), Is.SameAs(original));
            var format = (NumberFormatInfo)original.GetFormat(typeof(NumberFormatInfo))!;
            format.NumberDecimalSeparator = "!";
            var second = (IFormatProvider)call.InvokeStatic(original)!;
            Assert.That(second, Is.SameAs(original));
            Assert.That(((NumberFormatInfo)second.GetFormat(typeof(NumberFormatInfo))!).NumberDecimalSeparator, Is.EqualTo("!"));
        }

        [Test]
        public void P127GeneratedStaticPropertyKeepsItsClosedConstruction()
        {
            var count = new IntMember
            {
                id = "count",
                name = "Count",
                kind = MemberKind.Int,
                Modifier = NeoMemberModifierKind.Static,
                Storage = NeoMemberStorage.Save,
                Requirement = NeoMemberRequirementKind.Required
            };
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
            var place = new StaticMemberPointer { memberId = count.id, staticConstruction = Construction(generic) };
            var property = new NSPropertyMember
            {
                id = "computed",
                name = "Computed",
                kind = MemberKind.NSProperty,
                Modifier = NeoMemberModifierKind.Static,
                code = "return Count;",
                setterCode = "Count = value;",
                returnTypeInfo = IntType(),
                getter = Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(place)),
                setter = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = Array.Empty<Variable>(),
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                    instructions = new Instruction[] { new AssignInstruction { operatorValue = "=", pointer = Variable("__value__"),
                        target = new WriteTarget { pointer = place, typeInfo = IntType(), writability = WritabilityKind.Save } } }
                }
            };
            var owner = ReceiverClass(("Count", count.id), ("Computed", property.id));
            owner.genericParams = new() { new() { id = "T", name = "T" } };
            using var client = BuildClient(new JsonMember[] { count, property }, owner);
            var integerType = Construction(IntType());
            var integer = NeoMemberNSProperty.GetOrCreateStatic(client, property.id, integerType);
            var text = NeoMemberNSProperty.GetOrCreateStatic(client, property.id, Construction(new PrimitiveTypeInfo { type = MemberKind.String, required = true }));
            Assert.That(integer, Is.SameAs(NeoMemberNSProperty.GetOrCreateStatic(client, property.id, integerType)));
            integer.Set(11);
            text.Set(22);
            var a = integer.Compute();
            a.ThrowIfFailed();
            var b = text.Compute();
            b.ThrowIfFailed();
            Assert.That(a.value, Is.EqualTo(11));
            Assert.That(b.value, Is.EqualTo(22));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void P127GeneratedOutPublishesOnlyOnNormalReturn(bool throws)
        {
            var arg = Argument("result", MemberKind.Int);
            arg.mode = NeoParameterModeKind.Out;
            var args = new[] { arg };
            var function = ScriptFunction("generated-out", "TryGet", false, IntType(), args,
                Action(IntType(), args, AssignLocal("__arg_0__", Number(37), IntType()), Return(throws ? Variable("missing") : Number(1))));
            function.Modifier = NeoMemberModifierKind.Static;
            using var client = BuildClient(new JsonMember[] { function }, ReceiverClass(("TryGet", function.id)));
            var cell = new NeoOutArgument { Value = "unchanged" };
            var node = NeoMemberNSFunction.GetOrCreateStatic(client, function.id);
            if (throws)
            {
                Assert.Throws<NeoScriptException>(() => node.InvokeStatic(cell));
                Assert.That(cell.Value, Is.EqualTo("unchanged"));
            }
            else
            {
                Assert.That(node.InvokeStatic(cell), Is.EqualTo(1));
                Assert.That(cell.Value, Is.EqualTo(37));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void P127NativeOutPublishesOnlyOnNormalReturn(bool throws)
        {
            var native = NativeFunction("native-out", "TryGet", false);
            var arg = Argument("result", MemberKind.Int);
            arg.mode = NeoParameterModeKind.Out;
            native.argumentTypes = new[] { arg };
            native.Modifier = NeoMemberModifierKind.Static;
            using var client = BuildClient(new JsonMember[] { native }, ReceiverClass(("TryGet", native.id)));
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                [native.id] = (_, _, args, _) =>
                {
                    ((NeoOutArgument)args[0]!).Value = 42;
                    if (throws)
                        throw new InvalidOperationException("native failure");
                    return 1;
                },
            });
            var target = NeoOutReference.Discard();
            target.Write(9);
            if (throws)
            {
                Assert.Throws<InvalidOperationException>(() => client.InvokeNativeFunction(native.id, null, new object?[] { target }));
                Assert.That(target.Read(), Is.EqualTo(9));
            }
            else
            {
                Assert.That(client.InvokeNativeFunction(native.id, null, new object?[] { target }), Is.EqualTo(1));
                Assert.That(target.Read(), Is.EqualTo(42));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void P127GeneratedBoxAndTypeResultsAreClrValues(bool typeValue)
        {
            TypeInfo returns = new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = typeValue ? NeoObjectTypeKind.Type : null };
            var arg = new FunctionArgumentTypeInfo { type = MemberKind.Object, name = "value", required = true, objectType = typeValue ? NeoObjectTypeKind.Type : null };
            var args = new[] { arg };
            var function = ScriptFunction("generated-identity", "Identity", false, returns, args, Action(returns, args, Return(Variable("__arg_0__"))));
            function.Modifier = NeoMemberModifierKind.Static;
            using var client = BuildClient(new JsonMember[] { function }, ReceiverClass(("Identity", function.id)));
            var node = NeoMemberNSFunction.GetOrCreateStatic(client, function.id);
            object input = typeValue ? typeof(int) : (object)123;
            Assert.That(node.InvokeStatic(input), Is.SameAs(input));
            Assert.That(node.InvokeStatic(input), Is.SameAs(input));
        }
    }
}
