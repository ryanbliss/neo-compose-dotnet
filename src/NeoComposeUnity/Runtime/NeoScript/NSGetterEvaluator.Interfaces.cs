// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Linq;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {
        internal static NeoStaticBinding ResolveStaticBinding(string memberId, TypeInfo? construction, Context ctx)
        {
            TypeBinding? closed = construction is null ? null : RuntimeTypeOf(CloseContextType(construction, ctx));
            return closed?.Bindings is { Count: > 0 } ? ctx.client.ConstructedStaticBinding(memberId, closed) : ctx.client.StaticBinding(memberId);
        }

        private static NeoDelegateValue BindMemberTarget(MemberTargetPointer pointer, NeoScriptScope scope, Context ctx)
        {
            object? receiver = pointer.receiver.IsStatic ? null : EvalCallReceiver(pointer.receiver, scope, ctx);
            var selected = pointer.interfaceMember is null ? null
                : NeoInterfaceDispatch.Of(ctx.client).Resolve(RuntimeTypeOfValue(receiver!, null, ctx),
                    InterfaceConstruction(pointer.interfaceMember, ctx), pointer.interfaceMember.memberId, "call", pointer.direct);
            TypeInfo? writtenConstruction = pointer.receiver.staticConstruction ?? pointer.staticConstruction;
            TypeBinding? construction = selected?.StaticConstruction ?? (writtenConstruction is null ? null : RuntimeTypeOf(CloseContextType(writtenConstruction, ctx)));
            return new NeoDelegateValue
            {
                memberId = selected?.MemberId ?? pointer.memberId,
                valueId = pointer.receiver.IsStatic ? null : FindRowIdByReference(receiver, ctx)
                    ?? throw new NSGetterRuntimeError("A delegate receiver must have a logical value identity."),
                staticConstruction = construction,
                interfaceConstruction = selected?.InterfaceConstruction,
                typeArguments = pointer.typeArguments?.Select(type => RuntimeTypeOf(CloseContextType(type, ctx))).ToArray(),
            };
        }

        internal static TypeBinding InterfaceConstruction(InterfaceMemberRef reference, Context ctx) =>
            RuntimeTypeOf(CloseContextType(new InterfaceTypeInfo
            {
                type = MemberKind.Interface,
                required = true,
                interfaceId = reference.interfaceId,
                typeArguments = reference.arguments,
            }, ctx));

        internal static NeoInterfaceDispatch.Implementation ResolveInterfaceCall(CallFunctionPointer pointer, object? receiver, Context ctx)
        {
            if (receiver is null)
                throw new NSGetterRuntimeError(NullReferenceMessage, NeoErrorContract.NeoNullReferenceError);
            var reference = pointer.interfaceMember!;
            return NeoInterfaceDispatch.Of(ctx.client).Resolve(RuntimeTypeOfValue(receiver, null, ctx),
                InterfaceConstruction(reference, ctx), reference.memberId, "call", pointer.direct);
        }

        private static object? ReadInterfaceProperty(CallGetterPointer pointer, NeoScriptScope scope, Context ctx)
        {
            object? receiver = EvalCallReceiver(pointer.receiver, scope, ctx);
            if (receiver is null)
            {
                if (pointer.optional == true)
                    return null;
                throw new NSGetterRuntimeError(NullReferenceMessage, NeoErrorContract.NeoNullReferenceError);
            }
            var reference = pointer.interfaceMember!;
            var target = NeoInterfaceDispatch.Of(ctx.client).Resolve(RuntimeTypeOfValue(receiver, null, ctx),
                InterfaceConstruction(reference, ctx), reference.memberId, "get", pointer.direct);
            return ReadImplementation(target, receiver, ctx);
        }

        private static object? ReadImplementation(NeoInterfaceDispatch.Implementation target, object? receiver, Context ctx)
        {
            ctx.client.TryGetMember(target.MemberId, out var member);
            if (member is NSPropertyMember property)
                return DispatchNSGetterById(target.MemberId, receiver, ctx, property.getter,
                    staticConstruction: target.StaticConstruction, interfaceConstruction: target.InterfaceConstruction);
            if (target.StaticConstruction is not null || member!.Modifier == NeoMemberModifierKind.Static)
                return ReadStaticMember(target.MemberId, ctx, target.StaticConstruction is null ? null : NeoTypeBindings.ToTypeInfo(target.StaticConstruction));
            string key = FindSchemaPlacementCached(target.MemberId, ctx)!.schemaKey;
            var call = new CallGetterPointer
            {
                memberId = target.MemberId,
                receiver = new CallReceiver { kind = CallReceiverKind.Instance, pointer = Constant(receiver) }
            };
            return ReadDispatchedGetterValue(DispatchSchemaMember(receiver, key, ctx, call, accessorMember: member), call, receiver, ctx);
        }

        private static Pointer Constant(object? value) => new ValuePointer { primitiveResolved = true, primitive = value };

        private static NeoInterfaceDispatch.Implementation ResolveStaticParameter(TypeParameterStaticPointer pointer, Context ctx, string accessor)
        {
            TypeInfo bound = CloseContextType(pointer.typeParameter, ctx);
            if (bound.type == MemberKind.Generic)
                throw new NSGetterRuntimeError($"Type parameter has no binding in this frame for static member '{pointer.interfaceMember.memberId}'.");
            if (bound.type is not (MemberKind.Class or MemberKind.Interface))
                throw new NSGetterRuntimeError($"Static member '{pointer.interfaceMember.memberId}' through a type parameter bound to '{bound.type}' needs the primitive conformance table (P127 B5).");
            return NeoInterfaceDispatch.Of(ctx.client).Resolve(RuntimeTypeOf(bound), InterfaceConstruction(pointer.interfaceMember, ctx), pointer.interfaceMember.memberId, accessor);
        }

        private static object? EvalTypeParameterStatic(TypeParameterStaticPointer pointer, NeoScriptScope scope, Context ctx)
        {
            var operation = pointer.operation;
            var target = ResolveStaticParameter(pointer, ctx, operation.kind == "get" ? "get" : "call");
            if (operation.kind == "get")
                return ReadImplementation(target, null, ctx);
            if (operation.kind == "methodGroup")
                return new NeoDelegateValue
                {
                    memberId = target.MemberId,
                    staticConstruction = target.StaticConstruction,
                    typeArguments = operation.typeArguments?.Select(type => RuntimeTypeOf(CloseContextType(type, ctx))).ToArray(),
                    delegateType = operation.delegateType is null ? null : RuntimeTypeOf(CloseContextType(operation.delegateType, ctx)),
                };
            var call = new CallFunctionPointer
            {
                memberId = target.MemberId,
                receiver = new CallReceiver { kind = CallReceiverKind.Static, memberId = target.MemberId },
                args = operation.args ?? Array.Empty<Pointer>(),
                typeArguments = operation.typeArguments,
                staticConstruction = target.StaticConstruction is null ? null : NeoTypeBindings.ToTypeInfo(target.StaticConstruction),
                callSiteId = operation.callSiteId,
            };
            return EvalFunctionCall(call, scope, ctx);
        }

        internal static void WriteClassProperty(NSPropertyMember property, object receiver, object? value, Context ctx)
        {
            NeoScriptExecutor.ExecuteSetterAssignment(ctx.client, new AssignInstruction
            {
                target = new WriteTarget
                {
                    pointer = new CallGetterPointer
                    {
                        memberId = property.id,
                        receiver = new CallReceiver { kind = CallReceiverKind.Instance, pointer = Constant(receiver) },
                    },
                    typeInfo = property.returnTypeInfo!,
                    writability = WritabilityKind.Setter,
                },
            }, value, new NeoScriptScope(), ctx, NeoScriptExecutionOptions.ForImmediate(ctx.client));
        }

        internal static void WriteInterfacePointer(Pointer pointer, object? value, TypeInfo type, NeoScriptScope scope, Context ctx)
        {
            object? receiver = null;
            NeoInterfaceDispatch.Implementation target;
            if (pointer is CallGetterPointer { interfaceMember: { } reference } property)
            {
                receiver = EvalCallReceiver(property.receiver, scope, ctx)
                    ?? throw new NSGetterRuntimeError(NullReferenceMessage, NeoErrorContract.NeoNullReferenceError);
                target = NeoInterfaceDispatch.Of(ctx.client).Resolve(RuntimeTypeOfValue(receiver, null, ctx),
                    InterfaceConstruction(reference, ctx), reference.memberId, "set", property.direct);
            }
            else
                target = ResolveStaticParameter((TypeParameterStaticPointer)pointer, ctx, "set");
            ctx.client.TryGetMember(target.MemberId, out var member);
            if (member is NSPropertyMember implementation)
            {
                if (target.StaticConstruction is not null)
                    ctx.client.EnsureStaticConstruction(target.StaticConstruction);
                var nested = ctx.WithSetterPushed(target.MemberId, receiver);
                nested.staticConstruction = target.StaticConstruction;
                nested.interfaceConstruction = target.InterfaceConstruction;
                NeoScriptExecutor.ExecuteSetter(ctx.client, implementation.setter!, value, nested,
                    NeoScriptExecutionOptions.ForUnityProperty(ctx.client, target.MemberId));
                return;
            }
            if (receiver is not null)
            {
                string rowId = FindRowIdByReference(receiver, ctx)
                    ?? throw new NSGetterRuntimeError("A field implementation needs an attached receiver.");
                NeoScriptExecutor.WriteConstructedClassMember(ctx.client, rowId, FindSchemaPlacementCached(target.MemberId, ctx)!.schemaKey,
                    member!, FindRowOwnershipByReference(receiver, ctx) ?? ctx.valueOwnership, value, ctx);
                return;
            }
            var storage = new StaticMemberPointer { memberId = target.MemberId, staticConstruction = target.StaticConstruction is null ? null : NeoTypeBindings.ToTypeInfo(target.StaticConstruction) };
            NeoScriptExecutor.AssignTarget(ctx.client, new WriteTarget { pointer = storage, typeInfo = type, writability = WritabilityKind.Runtime }, value, scope, ctx);
        }
    }
}
