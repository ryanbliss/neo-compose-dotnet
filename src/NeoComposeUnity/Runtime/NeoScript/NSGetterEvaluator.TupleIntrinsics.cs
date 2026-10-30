// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript.Hashing;
namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {
        private static bool DefaultTupleElementEquals(TypeBinding type, object? a, object? b, Context ctx)
        {
            if (a is null || b is null)
                return a is null && b is null;
            if (NeoInterfaceDispatch.Of(ctx.client).ImplementsOwnEquatable(type))
                throw new NeoRuntimeError(NeoErrorClass.NeoNotSupportedError, $"EqualityComparer<{TypeName(type, ctx)}>.Default isn't supported yet (P127 B6).");
            TypeInfo staticType = NeoTypeBindings.ToTypeInfo(type);
            if (type.Head!.Type is MemberKind.List or MemberKind.Set or MemberKind.Lookup or MemberKind.Dictionary
                || staticType is ReferenceTypeInfo reference && NeoReferenceTypes.Category(reference.targetTypeInfo, ctx.client) == NeoReferenceCategory.Handle)
                return TupleHandleEqual(a, b, ctx);
            if (type.Head!.Type is MemberKind.Class or MemberKind.Interface || type.Head.Type == MemberKind.Object && type.Head.ObjectType is null)
                return ObjectEquals(a, staticType, b, ctx);
            return PayloadEquals(type, a, b, ctx);
        }

        private static int DefaultTupleElementCompare(TypeBinding type, object? a, object? b, Context ctx)
        {
            if (a is null)
                return b is null ? 0 : -1;
            if (b is null)
                return 1;
            if (NeoPrimitiveConformance.Kind(type) is null)
                throw new NeoRuntimeError(NeoErrorClass.NeoNotSupportedError, $"Comparer<{TypeName(type, ctx)}>.Default isn't supported yet (P127 B6).");
            return PrimitiveCompare(a, b, type, false, ctx);
        }

        private static string ClrTypeName(TypeBinding type, Context ctx) => type.Head!.Type switch
        {
            MemberKind.Int => "System.Int32",
            MemberKind.Float => "System.Single",
            MemberKind.Decimal => "System.Decimal",
            MemberKind.Bool => "System.Boolean",
            MemberKind.String => "System.String",
            MemberKind.Vector2 or MemberKind.Vector2Int or MemberKind.Vector3 or MemberKind.Vector3Int or MemberKind.Color => "UnityEngine." + type.Head.Type,
            MemberKind.Tuple => $"System.ValueTuple`{type.Bindings!.Count}[{string.Join(",", Enumerable.Range(0, type.Bindings.Count).Select(index => ClrTypeName(type.At(index), ctx)))}]",
            _ => TypeName(type, ctx),
        };

        private static object?[] TupleObjectElements(RuntimeTuple tuple, TypeBinding type) =>
            tuple.Select((value, index) => value is null or NeoBox ? value
                : TypeBindings.IsValueType(type.At(index).Head!) ? NeoBox.Of(type.At(index).WithRequired(true), value) : value).ToArray();

        private static object InvokeTupleStructural(string member, RuntimeTuple tuple, TypeBinding type, object?[] args, Context ctx)
        {
            object? comparer = args[member == "StructuralGetHashCode" ? 0 : 1];
            if (comparer is null)
                throw new NSGetterRuntimeError(NullReferenceMessage, NeoErrorContract.NeoNullReferenceError);
            var adapter = new TupleStructuralComparer(type, comparer, ctx);
            object?[] self = TupleObjectElements(tuple, type);
            if (member == "StructuralGetHashCode")
                return NeoTuples.TupleStructuralHash(self, adapter);
            var other = args[0] is NeoBox box && box.ValueType.Equals(type) && box.Payload is RuntimeTuple otherTuple
                ? TupleObjectElements(otherTuple, type) : null;
            return member == "StructuralEquals" ? NeoTuples.TupleStructuralEquals(self, other, adapter)
                : NeoTuples.TupleStructuralCompareTo(self, other, args[0] is null, adapter, () => ClrTypeName(type, ctx));
        }

        private sealed class TupleStructuralComparer : INeoStructuralEqualityComparer<object?>, INeoStructuralOrderComparer<object?>
        {
            private readonly TypeBinding type;
            private readonly object receiver;
            private readonly Context ctx;
            internal TupleStructuralComparer(TypeBinding type, object receiver, Context ctx)
            {
                this.type = type;
                this.receiver = receiver;
                this.ctx = ctx;
            }
            private object? Call(string contract, string name, params object?[] args)
            {
                string id = NeoPrimitiveConformance.InterfaceId(contract);
                var construction = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: id));
                string memberId = ctx.client.ProjectDataForRuntime.interfaces[id].members[name].id!;
                return InvokeMappedInterface(receiver, construction, memberId, args, ctx);
            }
            public bool Equals(object? a, object? b) => Call("IEqualityComparer", "Equals", a, b) is true;
            public int Hash(object? value) => (int)Number(Call("IEqualityComparer", "GetHashCode", value)!);
            public int Compare(object? a, object? b) => (int)Number(Call("IComparer", "Compare", a, b)!);
            public object Rest(IReadOnlyList<object?> elements, int start)
            {
                if (elements.Count - start < 2)
                    throw new NeoRuntimeError(NeoErrorClass.NeoNotSupportedError, $"A structural comparison of '{TypeName(type, ctx)}' passes its one-element rest as ValueTuple<T8>, which NeoScript has no value for.");
                var restType = new TupleTypeInfo
                {
                    type = MemberKind.Tuple,
                    required = true,
                    elements = Enumerable.Range(start, elements.Count - start)
                    .Select(index => new TupleElementTypeInfo { typeInfo = NeoTypeBindings.ToTypeInfo(type.At(index)) }).ToArray()
                };
                var values = elements.Skip(start).Select(value => value is NeoBox box ? box.Payload : value).ToArray();
                return NeoBox.Of(RuntimeTypeOf(restType), new RuntimeTuple(values, restType));
            }
        }
    }
}
