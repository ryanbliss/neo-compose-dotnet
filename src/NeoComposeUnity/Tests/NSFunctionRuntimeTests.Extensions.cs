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
            extension.genericParams = new[] { new FunctionGenericParam { name = "T", constraint = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "receiver-class" } } };
            extension.extension = new ExtensionMemberDescriptor { receiverType = IntType() };
            using var client = BuildClient(new JsonMember[] { extension }, ReceiverClass());
            Assert.That(() => NeoExtensionRuntime.Invoke(client, extension.id, isStatic ? null : (object)7, Array.Empty<object?>(), new[] { IntType() }),
                Throws.Exception.With.Message.Contains("does not satisfy its constraint"));
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
