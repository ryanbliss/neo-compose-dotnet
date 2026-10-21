// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {
        private static RuntimeTuple? ReadStoredTuple(TupleMember member, ArrayMemberValue row, NeoValueOwnership ownership, Context ctx)
        {
            if (row.value is null)
                return null;
            if (row.value.Length != member.elementMemberIds.Length)
                throw new NSGetterRuntimeError($"Tuple row '{row.id}' has {row.value.Length} elements; expected {member.elementMemberIds.Length}.");
            var env = NeoGenericResolution.CollectionEntryEnv(ctx.client, member, row, false);
            var type = NeoReferenceTypes.SlotTypeInfo(ctx.client, member, env) as TupleTypeInfo
                ?? throw new NSGetterRuntimeError($"Tuple '{member.name}' has an unresolved element type.");
            var elements = new object?[row.value.Length];
            for (int i = 0; i < elements.Length; i++)
            {
                if (!ctx.client.TryGetMember(member.elementMemberIds[i], out Member? descriptor))
                    throw new NSGetterRuntimeError($"Tuple '{member.name}' element {i + 1} has no member descriptor.");
                if (env is not null)
                    descriptor = NeoGenericResolution.SubstituteMember(ctx.client, descriptor, env);
                elements[i] = ResolveValueIfId(row.value[i], ctx, ownership, descriptor);
            }
            return new RuntimeTuple(elements, type);
        }

        private static TupleTypeInfo RuntimeTupleType(TupleTypeInfo type, Context ctx)
        {
            TypeInfo closed = type;
            if (NeoFunctionTypeArguments.Contains(closed))
            {
                if (ctx.functionTypeArguments is not { } arguments)
                    throw new NSGetterRuntimeError("A generic tuple requires the function's type arguments.");
                closed = NeoFunctionTypeArguments.Bind(closed, arguments);
            }
            if (NeoNSFunctionRuntime.ContainsGeneric(closed))
            {
                var env = ctx.thisValue is { } receiver
                    ? NeoNSFunctionRuntime.ResolveReceiverGenericEnv(ctx.client, receiver, ctx, "A generic tuple")
                    : ctx.initializerGenericEnv ?? throw new NSGetterRuntimeError("A generic tuple requires its Class receiver.");
                closed = NeoGenericResolution.SubstituteTypeInfo(ctx.client, closed, env);
            }
            return (TupleTypeInfo)closed;
        }

        private static RuntimeTuple EvalTupleCreate(FunctionTupleCreateInfo info, NeoScriptScope scope, Context ctx)
        {
            TupleTypeInfo type = RuntimeTupleType(info.typeInfo, ctx);
            var elements = new object?[info.elementPointers.Length];
            for (int i = 0; i < elements.Length; i++)
                elements[i] = CoerceNumericBinding(EvalPointer(info.elementPointers[i], scope, ctx), type.elements[i].typeInfo, "tuple element", ctx);
            return new RuntimeTuple(elements, type);
        }

        private static object? EvalTupleElement(FunctionTupleElementInfo info, NeoScriptScope scope, Context ctx)
        {
            object? receiver = EvalPointer(info.tuplePointer, scope, ctx);
            if (receiver is null && info.optional)
                return null;
            if (receiver is not RuntimeTuple value)
                throw new NSGetterRuntimeError("Tuple element read requires a tuple.");
            if (info.index < 0 || info.index >= value.Count)
                throw new NSGetterRuntimeError("Tuple element index is out of range.");
            return value[info.index];
        }

        private static bool TupleEqual(object? left, object? right, TupleTypeInfo type, Context ctx)
        {
            if (left is null || right is null)
                return left is null && right is null;
            if (left is not RuntimeTuple a || right is not RuntimeTuple b)
                throw new NSGetterRuntimeError("Tuple equality requires two tuples.");
            if (a.Count != type.elements.Length || b.Count != type.elements.Length)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                object? x = a[i];
                object? y = b[i];
                if (x is null || y is null)
                {
                    if (x is not null || y is not null)
                        return false;
                    continue;
                }
                TypeInfo element = type.elements[i].typeInfo;
                bool equal = element switch
                {
                    TupleTypeInfo nested => TupleEqual(x, y, nested, ctx),
                    _ when NeoPrimitiveInterfaces.AdmitsPrimitives(element) => EvidenceEquality(x, y, new[] { element, element }, ctx),
                    { type: MemberKind.Decimal } => NeoSetEntryKey.DecimalKey(CoerceDecimalOperand(x, "tuple equality")) == NeoSetEntryKey.DecimalKey(CoerceDecimalOperand(y, "tuple equality")),
                    ReferenceTypeInfo reference when NeoReferenceTypes.Category(reference.targetTypeInfo, ctx.client) == NeoReferenceCategory.Handle => ReferenceEqual(x, y, ctx),
                    { type: MemberKind.Reference } => x is NSSlotReference rx && y is NSSlotReference ry && rx.TargetId == ry.TargetId,
                    _ when TupleHandle(element.type) => ReferenceEqual(x, y, ctx),
                    _ => JsEqual(x, y, ctx),
                };
                if (!equal)
                    return false;
            }
            return true;
        }

        private static bool TupleHandle(MemberKind kind) => kind is MemberKind.Class or MemberKind.Interface
            or MemberKind.List or MemberKind.Set or MemberKind.Dictionary or MemberKind.Lookup;

        private static int TupleHash(RuntimeTuple tuple, Context ctx, JsValueComparer values)
        {
            int hash = 17;
            for (int i = 0; i < tuple.Count; i++)
            {
                object? value = tuple[i];
                TypeInfo type = tuple.TypeInfo.elements[i].typeInfo;
                int elementHash;
                if (value is null)
                    elementHash = 0;
                else if (type.type == MemberKind.Decimal || IsDecimalEvidence(value, type, ctx))
                    elementHash = StringComparer.Ordinal.GetHashCode(NeoSetEntryKey.DecimalKey(CoerceDecimalOperand(value, "tuple key")));
                else if (NeoPrimitiveInterfaces.AdmitsPrimitives(type) && ClassRowOf(value, ctx) is null)
                    elementHash = values.Hash(value);
                else if (TupleHandle(type.type) || type is ReferenceTypeInfo reference && NeoReferenceTypes.Category(reference.targetTypeInfo, ctx.client) == NeoReferenceCategory.Handle)
                {
                    object? identity = ReferenceKeyOf(value, ctx);
                    if (EquatableHandleHash(value, ctx) is { } equatableHash)
                        elementHash = equatableHash;
                    else
                    {
                        elementHash = identity is NeoVariantReference variant ? variant.GetHashCode()
                            : StringComparer.Ordinal.GetHashCode(TupleHandleKey(value, ctx));
                    }
                }
                else
                    elementHash = values.Hash(value);
                hash = unchecked(hash * 31 + elementHash);
            }
            return hash;
        }
    }
}
