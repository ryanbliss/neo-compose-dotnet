// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
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
        public void ExtensionFunctionUsesScalarThisAndRejectsTheWrongReceiver()
        {
            NSFunctionMember extension = ScriptFunction("extension", "Twice", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Add(Variable("__this__"), Variable("__this__")))));
            extension.Modifier = NeoMemberModifierKind.Sealed;
            extension.extension = new ExtensionMemberDescriptor { receiverType = IntType() };
            extension.action.parameters[0].typeInfo = IntType();
            using var client = BuildClient(new JsonMember[] { extension }, ReceiverClass());
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var call = Call(extension.id, "extension-call");
            call.receiver = CallReceiver.Instance(Number(4));
            Assert.That(NSGetterEvaluator.EvaluatePointer(call, new Dictionary<string, object?>(), ctx), Is.EqualTo(8));
            Assert.That(NeoExtensionRuntime.Invoke(client, extension.id, 5, Array.Empty<object?>()), Is.EqualTo(10));
            call.receiver = CallReceiver.Instance(Variable("text"));
            Assert.That(() => NSGetterEvaluator.EvaluatePointer(call, new Dictionary<string, object?> { ["text"] = "wrong" }, ctx),
                Throws.Exception.With.Message.Contains("receiver of extension"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExtensionRuntimeRejectsForgedConstraintArguments(bool isStatic)
        {
            NSFunctionMember extension = ScriptFunction("constrained-extension", "Probe", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(42))));
            extension.Modifier = isStatic ? NeoMemberModifierKind.Static : NeoMemberModifierKind.Sealed;
            extension.genericParams = new[] { new FunctionGenericParam { name = "T", constraints = new TypeInfo[] { new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "receiver-class" } } } };
            extension.extension = new ExtensionMemberDescriptor { receiverType = IntType() };
            using var client = BuildClient(new JsonMember[] { extension }, ReceiverClass());
            Assert.That(() => NeoExtensionRuntime.Invoke(client, extension.id, isStatic ? null : (object)7, Array.Empty<object?>(), new[] { IntType() }),
                Throws.Exception.With.Message.Contains("does not satisfy its constraint"));
        }

        // P127 §8.1: a primitive, vector, enum or tuple satisfies an
        // interface constraint through the conformance table, as the
        // server's satisfiesTypeInfoConstraint admits it. An enum implements
        // only the non-generic IComparable, as in C#.
        [TestCase("int", "IComparable`1", true)]
        [TestCase("float", "IComparable`1", true)]
        [TestCase("string", "IComparable`1", true)]
        [TestCase("tuple", "IComparable`1", true)]
        [TestCase("enum", "IComparable", true)]
        [TestCase("enum", "IComparable`1", false)]
        [TestCase("Vector2", "IEquatable`1", true)]
        [TestCase("Vector2", "IComparable`1", false)]
        [TestCase("class", "IComparable`1", false)]
        [TestCase("int?", "IComparable`1", false)]
        public void ExtensionConstraintsAdmitWhatTheConformanceTableLists(string argument, string interfaceName, bool admitted)
        {
            const string id = "constrained-extension";
            string interfaceId = NeoPrimitiveConformance.InterfaceId(interfaceName);
            bool generic = interfaceName.EndsWith("`1", StringComparison.Ordinal);
            var constraint = new InterfaceTypeInfo
            {
                type = MemberKind.Interface,
                required = true,
                interfaceId = interfaceId,
                typeArguments = generic
                    ? new Dictionary<string, TypeInfo> { ["iface-t"] = new FunctionGenericTypeInfo { type = MemberKind.Generic, required = true, ownerMemberId = id, index = 0 } }
                    : null,
            };
            NSFunctionMember extension = ScriptFunction(id, "Probe", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(42))));
            extension.Modifier = NeoMemberModifierKind.Static;
            extension.genericParams = new[] { new FunctionGenericParam { name = "T", constraints = new TypeInfo[] { constraint } } };
            extension.extension = new ExtensionMemberDescriptor { receiverType = IntType() };
            var interfaces = new Dictionary<string, Interface>
            {
                [interfaceId] = new Interface
                {
                    id = interfaceId,
                    projectId = ProjectId,
                    name = interfaceName.Split('`')[0],
                    members = new Dictionary<string, InterfaceMember>(),
                    genericParams = generic ? new List<GenericParamDeclaration> { new() { id = "iface-t", name = "T" } } : null,
                },
            };
            TypeInfo typeArgument = argument switch
            {
                "int" => IntType(),
                "int?" => IntType(required: false),
                "float" => new PrimitiveTypeInfo { type = MemberKind.Float, required = true },
                "string" => StringType(),
                "Vector2" => new PrimitiveTypeInfo { type = MemberKind.Vector2, required = true },
                "enum" => new EnumTypeInfo { type = MemberKind.Enum, required = true, enumId = "color" },
                "tuple" => new TupleTypeInfo
                {
                    type = MemberKind.Tuple,
                    required = true,
                    elements = new[] { new TupleElementTypeInfo { typeInfo = IntType() }, new TupleElementTypeInfo { typeInfo = StringType() } },
                },
                _ => new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "receiver-class" },
            };
            using var client = BuildClient(new JsonMember[] { extension }, ReceiverClass(), interfaces: interfaces);

            object? Run() => NeoExtensionRuntime.Invoke(client, id, null, Array.Empty<object?>(), new[] { typeArgument });

            if (admitted)
            {
                Assert.That(Run(), Is.EqualTo(42));
                return;
            }
            string expected = argument == "int?"
                ? "Type argument 'T' of extension 'Probe' must be non-nullable."
                : "Type argument 'T' of extension 'Probe' does not satisfy its constraint.";
            Assert.That(() => Run(), Throws.Exception.With.Message.EqualTo(expected));
        }

        // P113 §2.4: an interface constraint's `object` type arguments compare
        // invariantly, so `T : IContainer<CultureInfo>` refuses a class that
        // implements IContainer<Type>, as the server does.
        [TestCase("culture-box", true)]
        [TestCase("type-box", false)]
        public void ExtensionInterfaceConstraintsCompareObjectTypeArguments(string classId, bool admitted)
        {
            const string id = "container-extension";
            const string containerId = "container";
            NSFunctionMember extension = ScriptFunction(id, "Probe", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(42))));
            extension.Modifier = NeoMemberModifierKind.Static;
            var constraint = new InterfaceTypeInfo
            {
                type = MemberKind.Interface,
                required = true,
                interfaceId = containerId,
                typeArguments = new Dictionary<string, TypeInfo>
                {
                    ["container.T"] = new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = NeoObjectTypeKind.CultureInfo },
                },
            };
            extension.genericParams = new[] { new FunctionGenericParam { name = "T", constraints = new TypeInfo[] { constraint } } };
            extension.extension = new ExtensionMemberDescriptor { receiverType = IntType() };
            var interfaces = new Dictionary<string, Interface>
            {
                [containerId] = new Interface
                {
                    id = containerId,
                    projectId = ProjectId,
                    name = "IContainer",
                    members = new Dictionary<string, InterfaceMember>(),
                    genericParams = new List<GenericParamDeclaration> { new() { id = "container.T", name = "T" } },
                },
            };
            NeoSchemaClass Implementing(string boxId, NeoObjectTypeKind objectType)
            {
                NeoSchemaClass schemaClass = ReceiverClass();
                schemaClass.id = boxId;
                schemaClass.name = boxId;
                schemaClass.implementsInterfaces = new List<InterfaceConstruction>
                {
                    new()
                    {
                        interfaceId = containerId,
                        arguments = new Dictionary<string, TypeBinding>
                        {
                            ["container.T"] = TypeBinding.Closed(new TypeHead(MemberKind.Object, true, objectType: objectType)),
                        },
                    },
                };
                return schemaClass;
            }
            using var client = BuildClient(
                new JsonMember[] { extension },
                ReceiverClass(),
                new[] { Implementing("culture-box", NeoObjectTypeKind.CultureInfo), Implementing("type-box", NeoObjectTypeKind.Type) },
                interfaces: interfaces);
            var argument = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = classId };

            object? Run() => NeoExtensionRuntime.Invoke(client, id, null, Array.Empty<object?>(), new TypeInfo[] { argument });

            if (admitted)
                Assert.That(Run(), Is.EqualTo(42));
            else
                Assert.That(() => Run(), Throws.Exception.With.Message.EqualTo("Type argument 'T' of extension 'Probe' does not satisfy its constraint."));
        }

        // P127 §4.8: a class converts to a base only with the base's type
        // arguments, invariantly. The compose type-info-compatibility.test.ts
        // cases: Derived<T> : Base<T>, Derived2<T> : Derived<List<T>>,
        // Closed : Base<int>.
        [TestCase("derived", "int", "base", "int", true)]
        [TestCase("derived", "int", "base", "string", false)]
        [TestCase("derived2", "int", "derived", "list-int", true)]
        [TestCase("derived2", "int", "base", "list-int", true)]
        [TestCase("derived2", "int", "base", "list-string", false)]
        [TestCase("derived2", "int", "base", "int", false)]
        [TestCase("closed", null, "base", "int", true)]
        [TestCase("closed", null, "base", "string", false)]
        // An open target names no arguments to compare.
        [TestCase("closed", null, "base", null, true)]
        // A closed derived class doesn't reach a base over an open parameter.
        [TestCase("derived", null, "base", "int", false)]
        // A nested string? isn't string: the annotated comparison.
        [TestCase("derived", "string", "base", "string", true)]
        [TestCase("derived", "string?", "base", "string", false)]
        [TestCase("derived", "string?", "base", "string?", true)]
        [TestCase("derived2", "string?", "base", "list-string", false)]
        public void ExtensionClassConstraintsConvertToABaseOnlyWithItsTypeArguments(
            string fromClass, string? fromArgument, string toClass, string? toArgument, bool admitted)
        {
            const string id = "base-extension";
            TypeInfo Argument(string name) => name switch
            {
                "int" => IntType(),
                "string" => StringType(),
                "string?" => new PrimitiveTypeInfo { type = MemberKind.String, required = false },
                "list-int" => new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = IntType() },
                _ => new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = StringType() },
            };
            ClassTypeInfo Of(string classId, string? argument) => new()
            {
                type = MemberKind.Class,
                required = true,
                classId = classId,
                typeArguments = argument is null ? null : new Dictionary<string, TypeInfo> { [$"{classId}.T"] = Argument(argument) },
            };
            NSFunctionMember extension = ScriptFunction(id, "Probe", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(42))));
            extension.Modifier = NeoMemberModifierKind.Static;
            extension.genericParams = new[] { new FunctionGenericParam { name = "T", constraints = new TypeInfo[] { Of(toClass, toArgument) } } };
            extension.extension = new ExtensionMemberDescriptor { receiverType = IntType() };
            using var client = BuildClient(
                new JsonMember[] { extension },
                ReceiverClass(),
                BaseClassChain());

            object? Run() => NeoExtensionRuntime.Invoke(client, id, null, Array.Empty<object?>(), new TypeInfo[] { Of(fromClass, fromArgument) });

            if (admitted)
                Assert.That(Run(), Is.EqualTo(42));
            else
                Assert.That(() => Run(), Throws.Exception.With.Message.EqualTo("Type argument 'T' of extension 'Probe' does not satisfy its constraint."));
        }

        [Test]
        public void ExtensionRefusesAnUnknownTypeArgumentAsTheServerWordsIt()
        {
            NSFunctionMember extension = ScriptFunction("unknown-extension", "Probe", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(42))));
            extension.Modifier = NeoMemberModifierKind.Static;
            extension.genericParams = new[] { new FunctionGenericParam { name = "T" } };
            extension.extension = new ExtensionMemberDescriptor { receiverType = IntType() };
            using var client = BuildClient(new JsonMember[] { extension }, ReceiverClass());

            Assert.That(
                () => NeoExtensionRuntime.Invoke(client, extension.id, null, Array.Empty<object?>(), new TypeInfo[] { new UnknownTypeInfo { type = MemberKind.Unknown, required = true } }),
                Throws.Exception.With.Message.EqualTo("Type argument 'T' of extension 'Probe' must be concrete."));
        }

        /// <summary>Base&lt;T&gt;, Derived&lt;T&gt; : Base&lt;T&gt;, Derived2&lt;T&gt; : Derived&lt;List&lt;T&gt;&gt; and Closed : Base&lt;int&gt;.</summary>
        private static NeoSchemaClass[] BaseClassChain()
        {
            NeoSchemaClass Generic(string id, string? extendsClassId, string? baseParam, TypeBinding? binding)
            {
                NeoSchemaClass schemaClass = ReceiverClass();
                schemaClass.id = id;
                schemaClass.name = id;
                schemaClass.genericParams = new List<GenericParamDeclaration> { new() { id = $"{id}.T", name = "T" } };
                schemaClass.extendsClassId = extendsClassId;
                if (baseParam is not null)
                    schemaClass.extendsGenericBindings = new Dictionary<string, TypeBinding> { [baseParam] = binding! };
                return schemaClass;
            }
            NeoSchemaClass closed = Generic("closed", "base", "base.T", TestTypeBindings.Of(MemberKind.Int));
            closed.genericParams = null;
            return new[]
            {
                Generic("base", null, null, null),
                Generic("derived", "base", "base.T", TestTypeBindings.Open("derived", "derived.T")),
                Generic("derived2", "derived", "derived.T", TestTypeBindings.List(TestTypeBindings.Open("derived2", "derived2.T"))),
                closed,
            };
        }

        // P127 §4.7: `T : Enum` admits every enum and Enum itself, and nothing else.
        [TestCase("enum", true)]
        [TestCase("Enum", true)]
        [TestCase("int", false)]
        [TestCase("ValueType", false)]
        public void ExtensionAnyEnumConstraintAdmitsOnlyEnums(string argument, bool admitted)
        {
            const string id = "enum-extension";
            NSFunctionMember extension = ScriptFunction(id, "Probe", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(42))));
            extension.Modifier = NeoMemberModifierKind.Static;
            extension.genericParams = new[]
            {
                new FunctionGenericParam
                {
                    name = "T",
                    constraints = new TypeInfo[] { new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = NeoObjectTypeKind.Enum } },
                },
            };
            extension.extension = new ExtensionMemberDescriptor { receiverType = IntType() };
            TypeInfo typeArgument = argument switch
            {
                "enum" => new EnumTypeInfo { type = MemberKind.Enum, required = true, enumId = "color" },
                "Enum" => new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = NeoObjectTypeKind.Enum },
                "ValueType" => new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = NeoObjectTypeKind.ValueType },
                _ => IntType(),
            };
            using var client = BuildClient(new JsonMember[] { extension }, ReceiverClass());

            object? Run() => NeoExtensionRuntime.Invoke(client, id, null, Array.Empty<object?>(), new[] { typeArgument });

            if (admitted)
                Assert.That(Run(), Is.EqualTo(42));
            else
                Assert.That(() => Run(), Throws.Exception.With.Message.EqualTo("Type argument 'T' of extension 'Probe' does not satisfy its constraint."));
        }

        [Test]
        public void NullableExtensionAcceptsNullButOptionalAccessSkipsIt()
        {
            NSFunctionMember extension = ScriptFunction("extension", "OrZero", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(7))));
            extension.Modifier = NeoMemberModifierKind.Sealed;
            TypeInfo nullable = IntType();
            nullable.required = false;
            extension.extension = new ExtensionMemberDescriptor { receiverType = nullable };
            using var client = BuildClient(new JsonMember[] { extension }, ReceiverClass());
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var call = Call(extension.id, "extension-null");
            call.receiver = CallReceiver.Instance(Variable("nothing"));
            var scope = new Dictionary<string, object?> { ["nothing"] = null };
            Assert.That(NSGetterEvaluator.EvaluatePointer(call, scope, ctx), Is.EqualTo(7));
            call.optional = true;
            Assert.That(NSGetterEvaluator.EvaluatePointer(call, scope, ctx), Is.Null);
            Assert.That(() => NSGetterEvaluator.InvokeDelegate(new NeoDelegateValue { memberId = extension.id }, Array.Empty<object?>(), ctx),
                Throws.Exception.With.Message.EqualTo("Extension delegate has no bound receiver."));
        }

        [Test]
        public void ExtensionPropertyBindsReceiverGenericsAndRestoresTheCaller()
        {
            var parameter = new FunctionGenericTypeInfo { type = MemberKind.Generic, required = true, ownerMemberId = "extension-property", index = 0 };
            var property = new NSPropertyMember
            {
                id = "extension-property",
                name = "IsInt",
                kind = MemberKind.NSProperty,
                Modifier = NeoMemberModifierKind.Sealed,
                code = "return 1 is T;",
                returnTypeInfo = BoolType(),
                genericParams = new[] { new FunctionGenericParam { name = "T" } },
                extension = new ExtensionMemberDescriptor
                {
                    receiverType = new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = parameter },
                    genericParameterCount = 1,
                },
                getter = Action(BoolType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(new IsCheckPointer { type = PointerKind.IsCheck, pointer = Number(1), checkType = parameter })),
            };
            using var client = BuildClient(new JsonMember[] { property }, ReceiverClass());
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var scope = new Dictionary<string, object?> { ["items"] = Array.Empty<object?>() };
            var read = new CallGetterPointer
            {
                type = PointerKind.CallGetter,
                memberId = property.id,
                receiver = CallReceiver.Instance(Variable("items")),
                typeArguments = new[] { IntType() },
            };
            Assert.That(NSGetterEvaluator.EvaluatePointer(read, scope, ctx), Is.True);
            Assert.That(ctx.functionTypeArguments, Is.Null);
            read.typeArguments = new[] { StringType() };
            Assert.That(NSGetterEvaluator.EvaluatePointer(read, scope, ctx), Is.False);
            Assert.That(ctx.functionTypeArguments, Is.Null);
            Assert.That(NeoExtensionRuntime.Get(client, property.id, Array.Empty<int>(), new[] { IntType() }), Is.True);
            Assert.That(NeoExtensionRuntime.Get(client, property.id, Array.Empty<string>(), new[] { StringType() }), Is.False);
            read.typeArguments = Array.Empty<TypeInfo>();
            Assert.That(() => NSGetterEvaluator.EvaluatePointer(read, scope, ctx), Throws.Exception.With.Message.Contains("no type argument"));
        }

        [Test]
        public void StaticExtensionsNeverBecomeSaveStorageRoots()
        {
            NSFunctionMember extension = ScriptFunction("extension-static", "Seven", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(7))));
            extension.Modifier = NeoMemberModifierKind.Static;
            extension.extension = new ExtensionMemberDescriptor { receiverType = IntType() };
            using var client = BuildClient(new JsonMember[] { extension }, ReceiverClass());
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var call = Call(extension.id, "static-extension");
            call.receiver = CallReceiver.Static(extension.id);
            Assert.That(NSGetterEvaluator.EvaluatePointer(call, new Dictionary<string, object?>(), ctx), Is.EqualTo(7));
            Assert.That(NeoExtensionRuntime.Invoke(client, extension.id, null, Array.Empty<object?>()), Is.EqualTo(7));
            Assert.That(() => NeoExtensionRuntime.Invoke(client, extension.id, 1, Array.Empty<object?>()),
                Throws.Exception.With.Message.Contains("without an instance receiver"));
            Assert.DoesNotThrow(() => client.RemoveSaveValueAndDescendantsIfUnlinked("missing-row"));
        }
    }
}
