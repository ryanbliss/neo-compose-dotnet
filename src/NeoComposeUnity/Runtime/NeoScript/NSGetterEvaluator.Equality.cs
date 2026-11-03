// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript.Ordering;
using NeoCompose.Runtime.NeoScript.Hashing;
using NeoCompose.Runtime.NeoScript.Numerics;

namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {

        private sealed class EqualityOperandMemo
        {
            internal object? Receiver, Environment, Arguments, Static, Interface, Schema;
            internal TypeBinding? Binding;
            internal TypeBinding Read(TypeInfo type, Context ctx)
            {
                if (Binding is not null && ReferenceEquals(Receiver, ctx.thisValue)
                    && ReferenceEquals(Environment, ctx.initializerGenericEnv)
                    && ReferenceEquals(Arguments, ctx.functionTypeArguments)
                    && ReferenceEquals(Static, ctx.staticConstruction)
                    && ReferenceEquals(Interface, ctx.interfaceConstruction)
                    && ReferenceEquals(Schema, ctx.client.SchemaResolution))
                    return Binding;
                Receiver = ctx.thisValue;
                Environment = ctx.initializerGenericEnv;
                Arguments = ctx.functionTypeArguments;
                Static = ctx.staticConstruction;
                Interface = ctx.interfaceConstruction;
                Schema = ctx.client.SchemaResolution;
                return Binding = RuntimeTypeOf(CloseContextType(type, ctx));
            }
        }
        private static readonly ConditionalWeakTable<Context, Dictionary<TypeInfo, EqualityOperandMemo>> equalityOperands = new();
        private static TypeBinding EqualityOperandBinding(TypeInfo type, Context ctx)
        {
            var cache = equalityOperands.GetValue(ctx, _ => new());
            if (!cache.TryGetValue(type, out var memo))
                cache.Add(type, memo = new());
            return memo.Read(type, ctx);
        }
        // Enum selections share the wire array representation with lists,
        // but retain value equality. Keep their type evidence weakly with the
        // array so legacy conditions need no structural collection fallback.
        private static readonly ConditionalWeakTable<object, object> enumValues = new();
        private static readonly object enumValueMarker = new();
        internal static object?[] MarkEnumValue(object?[] value)
        {
            enumValues.GetValue(value, _ => enumValueMarker);
            return value;
        }

        private static bool IsEnumValue(object value) => value is object?[] && enumValues.TryGetValue(value, out _);

        private static bool EnumValueEquals(object a, object b)
        {
            if (a is object?[] left && b is object?[] right)
            {
                if (left.Length != right.Length)
                    return false;
                for (int i = 0; i < left.Length; i++)
                    if (left[i] is not string x || right[i] is not string y || x != y)
                        return false;
                return true;
            }
            return a is object?[] { Length: 1 } first && b is string second && first[0] is string id && id == second
                || b is object?[] { Length: 1 } last && a is string initial && last[0] is string other && other == initial;
        }

        private static readonly ConditionalWeakTable<NeoClient, ComparerSchemaCache> comparerCaches = new();

        private sealed class ComparerSchemaCache
        {
            internal readonly object Schema;
            internal readonly Dictionary<TypeBinding, RuntimeEquality> Equalities = new();
            internal readonly Dictionary<TypeBinding, RuntimeComparison> Comparisons = new();
            internal readonly Dictionary<TypeBinding, (TypeBinding Construction, string Member)?> ObjectComparisons = new();
            internal Dictionary<string, bool>? CustomEquality;
            internal bool HasLocalIdentityHashes;
            internal readonly Dictionary<string, int> AttachedIdentityHashes = new(StringComparer.Ordinal);
            internal ComparerMetadata? Metadata;
            internal readonly DelegateCaches Delegates = new();
            internal readonly Dictionary<(NeoValueOwnership, string), StoredEqualityIndex> StoredIndexes = new();
            internal readonly Dictionary<(TypeBinding, NeoComparerFamilyKind), NeoBuiltInComparer> BuiltIns = new();
            internal readonly Dictionary<TypeBinding, NeoBuiltInComparer> SetComparers = new();
            internal readonly ConditionalWeakTable<object, NeoBuiltInComparer> CollectionComparers = new();
            internal readonly ConditionalWeakTable<object, Dictionary<System.Type, object>> HostComparers = new();
            internal readonly NeoBuiltInComparer ReferenceComparer = new(NeoBuiltInComparerKind.ReferenceEquality, NeoComparerFamilyKind.Equality, null);
            internal ComparerSchemaCache(NeoClient client) => Schema = client.SchemaResolution;
        }

        private static ComparerSchemaCache ComparerCache(Context ctx)
        {
            var cache = comparerCaches.GetValue(ctx.client, client => new ComparerSchemaCache(client));
            if (!ReferenceEquals(cache.Schema, ctx.client.SchemaResolution))
            {
                comparerCaches.Remove(ctx.client);
                cache = comparerCaches.GetValue(ctx.client, client => new ComparerSchemaCache(client));
            }
            return cache;
        }

        private static (TypeBinding Construction, string Member)? DefaultInterface(TypeBinding type, string contract, string name, Context ctx)
        {
            string id = NeoPrimitiveConformance.InterfaceId(contract);
            if (!ctx.client.ProjectDataForRuntime.interfaces.TryGetValue(id, out var declaration))
                return null;
            Dictionary<string, TypeBinding>? arguments = declaration.genericParams is { Count: > 0 } parameters
                ? new Dictionary<string, TypeBinding> { [parameters[0].id] = type.WithRequired(true) } : null;
            var requested = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: id), arguments);
            var dispatch = NeoInterfaceDispatch.Of(ctx.client);
            foreach (var actual in dispatch.Closure(type.WithRequired(true)))
                if (actual.Head!.InterfaceId == id && dispatch.Converts(actual, requested))
                    return (actual, declaration.members[name].id!);
            return null;
        }

        internal sealed class RuntimeEquality
        {
            internal readonly TypeBinding Type;
            internal readonly TypeInfo StaticType;
            internal readonly string Name;
            internal readonly bool RawIdentity;
            internal bool KeyEquivalent => RawIdentity || Type.Head!.Type is MemberKind.Int or MemberKind.Float or MemberKind.Decimal or MemberKind.String or MemberKind.Bool or MemberKind.Enum;
            private readonly (TypeBinding Construction, string Member)? equatable;
            private readonly bool primitive;

            internal RuntimeEquality(TypeBinding type, Context ctx)
            {
                Type = type;
                StaticType = NeoTypeBindings.ToTypeInfo(type);
                primitive = NeoPrimitiveConformance.Kind(type.WithRequired(true)) is not null;
                if (type.Head!.Type is MemberKind.Class or MemberKind.Interface)
                    equatable = DefaultInterface(type, "IEquatable`1", "Equals", ctx);
                RawIdentity = type.Head.Type == MemberKind.Class && HasOnlyIdentityClasses(type.Head.ClassId!, ctx);
                Name = type.Head.Type == MemberKind.String ? "StringEqualityComparer"
                    : type.Head.Type == MemberKind.Enum ? "EnumEqualityComparer`1"
                    : !type.Head.Required && TypeBindings.IsValueType(type.Head) ? "NullableEqualityComparer`1"
                    : primitive || equatable is not null ? "GenericEqualityComparer`1" : "ObjectEqualityComparer`1";
            }

            internal bool Equals(object? entry, object? candidate, Context ctx)
            {
                if (entry is null || candidate is null)
                    return entry is null && candidate is null;
                if (entry is NeoDelegateValue leftDelegate && candidate is NeoDelegateValue rightDelegate)
                    return DelegateEquals(leftDelegate, rightDelegate, ctx);
                if (equatable is { } call)
                    return JsTruthy(InvokeMappedInterface(entry, call.Construction, call.Member, new[] { candidate }, ctx));
                if (Type.Head!.Type == MemberKind.Decimal)
                    return NeoDecimalMath.Compare(CoerceDecimalOperand(entry, "Equals"), CoerceDecimalOperand(candidate, "Equals")) == 0;
                if (primitive)
                    return PayloadEquals(Type.WithRequired(true), entry, candidate, ctx);
                if (Type.Head.Type is MemberKind.List or MemberKind.Set or MemberKind.Dictionary or MemberKind.Lookup)
                    return ReferenceEqual(entry, candidate, ctx);
                return ObjectEquals(entry, StaticType, candidate, ctx);
            }

            internal int Hash(object? value, Context ctx) => value is null ? 0
                : ObjectHash(Type.Head!.Type == MemberKind.Decimal ? CoerceDecimalOperand(value, "GetHashCode") : value, StaticType, ctx);
        }

        internal sealed class RuntimeComparison
        {
            internal readonly TypeBinding Type;
            internal readonly TypeInfo StaticType;
            internal readonly string Name;
            internal readonly NeoSortHelper Helper;
            internal readonly bool Float;
            private readonly bool primitive;
            private readonly (TypeBinding Construction, string Member)? comparable;

            internal RuntimeComparison(TypeBinding type, Context ctx)
            {
                Type = type;
                StaticType = NeoTypeBindings.ToTypeInfo(type);
                TypeBinding underlying = type.WithRequired(true);
                var kind = NeoPrimitiveConformance.Kind(underlying);
                primitive = kind is not null && NeoPrimitiveConformance.Table[kind.Value].Any(row => row.InterfaceId == NeoPrimitiveConformance.InterfaceId("IComparable`1"));
                if (type.Head!.Type is MemberKind.Class or MemberKind.Interface)
                    comparable = DefaultInterface(type, "IComparable`1", "CompareTo", ctx);
                bool nullable = !type.Head.Required && TypeBindings.IsValueType(type.Head);
                Helper = !nullable && (primitive || comparable is not null) ? NeoSortHelper.Comparable : NeoSortHelper.Comparer;
                Float = type.Head.Type == MemberKind.Float && !nullable;
                Name = type.Head.Type == MemberKind.Enum ? "EnumComparer`1" : nullable ? "NullableComparer`1"
                    : primitive || comparable is not null ? "GenericComparer`1" : "ObjectComparer`1";
            }

            internal int Compare(object? x, object? y, Context ctx)
            {
                if (x is null)
                    return y is null ? 0 : -1;
                if (y is null)
                    return 1;
                return CompareNonNull(x, y, ctx);
            }

            internal int CompareNonNull(object? x, object? y, Context ctx)
            {
                if (x is null)
                    throw new NSGetterRuntimeError(NullReferenceMessage, NeoErrorContract.NeoNullReferenceError);
                if (comparable is { } call)
                    return NeoInt32.Wrap(Number(InvokeMappedInterface(x, call.Construction, call.Member, new[] { y }, ctx)!));
                if (primitive || Type.Head!.Type == MemberKind.Enum)
                {
                    if (Type.Head!.Type == MemberKind.Decimal)
                        return PrimitiveCompare(CoerceDecimalOperand(x, "CompareTo"), y is null ? null : CoerceDecimalOperand(y, "CompareTo"), Type.WithRequired(true), false, ctx);
                    return PrimitiveCompare(x, y, Type.WithRequired(true), false, ctx);
                }
                if (SameObject(x, y, ctx))
                    return 0;
                if (y is null)
                    return 1;
                if (TryObjectComparison(x, y, ctx, out int result))
                    return result;
                if (TryObjectComparison(y, x, ctx, out result))
                    return unchecked(-result);
                throw NeoOrderingErrors.ImplementIComparable();
            }
        }

        internal static RuntimeEquality SelectEquality(TypeBinding type, Context ctx)
        {
            type = NeoInterfaceDispatch.Identity(type);
            var cache = ComparerCache(ctx).Equalities;
            if (!cache.TryGetValue(type, out var result))
                cache[type] = result = new RuntimeEquality(type, ctx);
            return result;
        }

        private static bool HasOnlyIdentityClasses(string classId, Context ctx)
        {
            var cache = ComparerCache(ctx);
            if (cache.CustomEquality is null)
            {
                var custom = new Dictionary<string, bool>(StringComparer.Ordinal);
                var interfaceCache = new Dictionary<string, bool>(StringComparer.Ordinal);
                string equatable = NeoPrimitiveConformance.InterfaceId("IEquatable`1");
                bool InterfaceHasEquality(string id)
                {
                    if (interfaceCache.TryGetValue(id, out bool known))
                        return known;
                    interfaceCache[id] = false;
                    return interfaceCache[id] = id == equatable || ctx.client.ProjectDataForRuntime.interfaces[id].extendsInterfaces?.Any(parent => InterfaceHasEquality(parent.interfaceId)) == true;
                }
                bool ClassHasEquality(string id)
                {
                    if (custom.TryGetValue(id, out bool known))
                        return known;
                    custom[id] = false;
                    var declaration = ctx.client.ProjectDataForRuntime.classes[id];
                    bool Overrides(string name, string canonical) => declaration.schema.TryGetValue(name + "#" + canonical, out string member) && member != canonical;
                    return custom[id] = Overrides("Equals", NeoObjectContract.EqualsMember)
                        || Overrides("GetHashCode", NeoObjectContract.GetHashCodeMember)
                        || declaration.implementsInterfaces?.Any(item => InterfaceHasEquality(item.interfaceId)) == true
                        || declaration.extendsClassId is { } parent && ClassHasEquality(parent);
                }
                foreach (string id in ctx.client.ProjectDataForRuntime.classes.Keys)
                    ClassHasEquality(id);
                // Mark ancestors once. Selecting another closed class then
                // asks one cached question, without scanning all subclasses.
                var affected = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var item in custom)
                {
                    if (!item.Value)
                        continue;
                    string? current = item.Key;
                    while (current is not null && !affected.ContainsKey(current))
                    {
                        affected.Add(current, true);
                        current = ctx.client.ProjectDataForRuntime.classes[current].extendsClassId;
                    }
                }
                cache.CustomEquality = affected;
            }
            return !cache.CustomEquality.ContainsKey(classId);
        }

        internal sealed class BoundEquality : IEqualityComparer<object?>
        {
            internal readonly RuntimeEquality Default;
            internal readonly object? Comparer;
            private readonly Context ctx;
            private readonly TypeBinding? construction;
            private readonly string? equalsMember;
            private readonly string? hashMember;
            internal BoundEquality(TypeBinding type, object? comparer, Context ctx)
            {
                this.ctx = ctx;
                Default = SelectEquality(type, ctx);
                Comparer = comparer;
                if (comparer is null or NeoBuiltInComparer)
                    return;
                string id = NeoPrimitiveConformance.InterfaceId("IEqualityComparer`1");
                var declaration = ctx.client.ProjectDataForRuntime.interfaces[id];
                construction = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: id),
                    new Dictionary<string, TypeBinding> { [declaration.genericParams![0].id] = type });
                equalsMember = declaration.members["Equals"].id;
                hashMember = declaration.members["GetHashCode"].id;
            }
            internal object DictionaryKey(string key) => Default.Type.Head!.Type == MemberKind.Enum ? MarkEnumValue(new object?[] { key }) : key;
            internal string KeyText(object? key) => PayloadText(key, Default.Type, ctx);
            internal bool UsesDefault => Comparer is null || UnwrapComparer(Comparer) is { Kind: NeoBuiltInComparerKind.Default, Family: NeoComparerFamilyKind.Equality } builtIn && builtIn.Type!.Equals(Default.Type);
            public new bool Equals(object? entry, object? candidate) => Comparer is null
                ? Default.Equals(entry, candidate, ctx)
                : Comparer is NeoBuiltInComparer builtIn ? BuiltInEquals(builtIn, entry, candidate, ctx)
                : JsTruthy(InvokeMappedInterface(Comparer, construction!, equalsMember!, new[] { entry, candidate }, ctx));
            public int GetHashCode(object? value) => Comparer is null ? Default.Hash(value, ctx)
                : Comparer is NeoBuiltInComparer builtIn ? BuiltInHash(builtIn, value, ctx)
                : NeoInt32.Wrap(Number(InvokeMappedInterface(Comparer, construction!, hashMember!, new[] { value }, ctx)!));
        }

        private static bool SameEquality(BoundEquality left, BoundEquality right, Context ctx)
        {
            if (!left.Default.Type.Equals(right.Default.Type))
                return false;
            object a = left.Comparer ?? DefaultComparer(left.Default.Type, NeoComparerFamilyKind.Equality, ctx);
            object b = right.Comparer ?? DefaultComparer(right.Default.Type, NeoComparerFamilyKind.Equality, ctx);
            return ObjectEquals(a, null, b, ctx);
        }

        internal static BoundEquality BindEquality(EqualitySite site, NeoScriptScope scope, Context ctx) =>
            new(RuntimeTypeOf(CloseContextType(site.type, ctx)), site.comparer is null ? null : EvalPointer(site.comparer, scope, ctx), ctx);

        private sealed class BoundComparison
        {
            internal readonly RuntimeComparison Default;
            internal readonly object? Comparer;
            private readonly Context ctx;
            private readonly TypeBinding? construction;
            private readonly string? member;
            internal BoundComparison(TypeBinding type, object? comparer, Context ctx)
            {
                this.ctx = ctx;
                Default = SelectComparison(type, ctx);
                Comparer = comparer;
                if (comparer is null or NeoBuiltInComparer)
                    return;
                string id = NeoPrimitiveConformance.InterfaceId("IComparer`1");
                var declaration = ctx.client.ProjectDataForRuntime.interfaces[id];
                construction = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: id),
                    new Dictionary<string, TypeBinding> { [declaration.genericParams![0].id] = type });
                member = declaration.members["Compare"].id;
            }
            internal bool UsesDefault => Comparer is null || UnwrapComparer(Comparer) is { Kind: NeoBuiltInComparerKind.Default } builtIn && builtIn.Type!.Equals(Default.Type);
            internal int Compare(object? x, object? y) => Comparer is null ? Default.Compare(x, y, ctx)
                : Comparer is NeoBuiltInComparer builtIn ? BuiltInCompare(builtIn, x, y, ctx)
                : NeoInt32.Wrap(Number(InvokeMappedInterface(Comparer, construction!, member!, new[] { x, y }, ctx)!));
            internal int CompareNonNull(object? x, object? y) => UsesDefault ? Default.CompareNonNull(x, y, ctx) : Compare(x, y);
            internal string Text() => Comparer is null ? $"System.Collections.Generic.{Default.Name}[{ClrTypeName(Default.Type, ctx)}]"
                : ObjectText(Comparer, null, ctx) ?? "";
        }

        private static BoundComparison BindComparison(OrderingSite site, NeoScriptScope scope, Context ctx) =>
            new(RuntimeTypeOf(CloseContextType(site.type, ctx)), site.comparer is null ? null : EvalPointer(site.comparer, scope, ctx), ctx);

        internal static int EqualityIndexOf(object? collection, object? target, BoundEquality equality, bool last, Context ctx)
        {
            if (!last && equality.Comparer is null && equality.Default.RawIdentity && target is not null)
                return ReferenceIndexOf(collection, target, ctx);
            int found = -1;
            var cursor = new CollectionCursor(collection, ctx);
            try
            {
                if (last)
                {
                    var entries = new List<object?>();
                    while (cursor.MoveNextUnresolved())
                        entries.Add(cursor.ResolveEntry(ctx));
                    for (int index = entries.Count - 1; index >= 0; index--)
                        if (equality.Equals(entries[index], target))
                            return index;
                    return -1;
                }
                while (cursor.MoveNextUnresolved())
                    if (equality.Equals(cursor.ResolveEntry(ctx), target))
                        return cursor.Index;
                return found;
            }
            catch (Exception error) when (cursor.CaptureFailure(error)) { throw; }
            finally { cursor.Dispose(); }
        }

        internal static RuntimeComparison SelectComparison(TypeBinding type, Context ctx)
        {
            type = NeoInterfaceDispatch.Identity(type);
            var cache = ComparerCache(ctx).Comparisons;
            if (!cache.TryGetValue(type, out var result))
                cache[type] = result = new RuntimeComparison(type, ctx);
            return result;
        }

        private static bool TryObjectComparison(object receiver, object other, Context ctx, out int result)
        {
            TypeBinding runtimeType = RuntimeTypeOfValue(receiver, null, ctx);
            var cache = ComparerCache(ctx).ObjectComparisons;
            if (!cache.TryGetValue(runtimeType, out var call))
                cache[runtimeType] = call = DefaultInterface(runtimeType, "IComparable", "CompareTo", ctx);
            if (call is null)
            {
                result = 0;
                return false;
            }
            if (receiver is NeoBox box)
                result = PrimitiveCompare(box.Payload!, other, runtimeType, true, ctx);
            else if (receiver is string text)
                result = PrimitiveCompare(text, other, runtimeType, true, ctx);
            else
                result = NeoInt32.Wrap(Number(InvokeMappedInterface(receiver, call.Value.Construction, call.Value.Member, new[] { other }, ctx)!));
            return true;
        }

        private static bool EvalTupleEquality(FunctionTupleEqualInfo info, NeoScriptScope scope, Context ctx)
        {
            object? left = EvalPointer(info.left, scope, ctx);
            object? right = EvalPointer(info.right, scope, ctx);
            var type = RuntimeTupleType(info.typeInfo, ctx);
            return info.equality is null ? TupleEqual(left, right, type, ctx)
                : SelectEquality(RuntimeTypeOf(type), ctx).Equals(left, right, ctx);
        }

        private static bool PredefinedEquality(TypeBinding type, object? left, object? right, Context ctx)
        {
            if (left is null || right is null)
                return left is null && right is null;
            if (left is NeoBox || right is NeoBox)
                return SameObject(left, right, ctx);
            switch (type.Head!.Type)
            {
                case MemberKind.Int:
                case MemberKind.Float:
                    return Number(left) == Number(right);
                case MemberKind.Decimal:
                    return NeoDecimalMath.Compare(CoerceDecimalOperand(left, "equality"), CoerceDecimalOperand(right, "equality")) == 0;
                case MemberKind.Bool:
                    return (bool)left == (bool)right;
                case MemberKind.String:
                case MemberKind.Enum:
                    return JsEqual(left, right, ctx);
                case MemberKind.NSDelegate:
                    return DelegateEquals((NeoDelegateValue)left, (NeoDelegateValue)right, ctx);
                case MemberKind.Tuple:
                    var a = (RuntimeTuple)left;
                    var b = (RuntimeTuple)right;
                    if (a.Count != b.Count)
                        return false;
                    for (int index = 0; index < a.Count; index++)
                        if (!PredefinedEquality(type.At(index), a[index], b[index], ctx))
                            return false;
                    return true;
                case MemberKind.Vector2:
                case MemberKind.Vector2Int:
                case MemberKind.Vector3:
                case MemberKind.Vector3Int:
                    var x = (NeoVector2Value)left;
                    var y = (NeoVector2Value)right;
                    if (type.Head.Type is MemberKind.Vector2Int or MemberKind.Vector3Int)
                        return x.x == y.x && x.y == y.y && (x is not NeoVector3Value x3 || x3.z == ((NeoVector3Value)y).z);
                    if (x is NeoVector3Value vx)
                        return new UnityEngine.Vector3((float)vx.x, (float)vx.y, (float)vx.z)
                            == new UnityEngine.Vector3((float)y.x, (float)y.y, (float)((NeoVector3Value)y).z);
                    return new UnityEngine.Vector2((float)x.x, (float)x.y) == new UnityEngine.Vector2((float)y.x, (float)y.y);
                case MemberKind.Color:
                    var c = (NeoColorValue)left;
                    var d = (NeoColorValue)right;
                    return new UnityEngine.Color(c.r, c.g, c.b, c.a) == new UnityEngine.Color(d.r, d.g, d.b, d.a);
                case MemberKind.Reference:
                    return left is NSSlotReference l && right is NSSlotReference r ? l.TargetId == r.TargetId : ReferenceEqual(left, right, ctx);
                default:
                    return SameObject(left, right, ctx);
            }
        }

        private sealed class IdentityHashValue
        {
            internal readonly int Value;
            internal IdentityHashValue(int value) => Value = value;
        }
        private static readonly ConditionalWeakTable<object, IdentityHashValue> identityHashes = new();
        private static int nextIdentityHash;

        private static int IdentityHash(object? value, Context ctx)
        {
            if (value is null)
                return 0;
            if (CachedClosureKey(value) is { } closureKey)
                return NeoHashes.String(closureKey);
            value = UnwrapComparer(value) ?? value;
            if (value is NeoBox box && !ReferenceEquals(box.IdentityKey, box))
                return box.ValueType.Head!.Type == MemberKind.String ? NeoHashes.String((string)box.Payload!) : box.IdentityKey.GetHashCode();
            object key = ReferenceKeyOf(value, ctx) ?? SequenceCollectionIdentity(value);
            if (key is string rowId)
                return comparerCaches.TryGetValue(ctx.client, out var existing) && existing.AttachedIdentityHashes.TryGetValue(rowId, out int attached)
                    ? attached : NeoHashes.String(rowId);
            ComparerCache(ctx).HasLocalIdentityHashes = true;
            return identityHashes.GetValue(key, _ => new IdentityHashValue(System.Threading.Interlocked.Increment(ref nextIdentityHash))).Value;
        }

        // Adoption changes a local object's storage address, not its identity.
        // Preserve a hash already used by a live set before assigning that row.
        private static void PreserveIdentityHash(Context ctx, object alias, string rowId)
        {
            if (!comparerCaches.TryGetValue(ctx.client, out var cache) || !cache.HasLocalIdentityHashes)
                return;
            object key = alias is RuntimeListIdentity list ? list.origin
                : alias is NeoGeneratedClassValue { PendingValue: NeoScriptObject pending } ? pending
                : SequenceCollectionIdentity(alias);
            if (identityHashes.TryGetValue(key, out var hash))
                cache.AttachedIdentityHashes.TryAdd(rowId, hash.Value);
        }

    }
}
