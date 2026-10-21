// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object?[], RuntimeSet> runtimeSets = new();

        internal sealed class RuntimeListIdentity
        {
            internal readonly object?[] origin;
            internal object?[] current;
            internal LocalList? growing;
            internal bool captured;
            internal RowReference? reference;
            internal object? referenceIndex;

            internal RuntimeListIdentity(object?[] origin)
            {
                this.origin = origin;
                current = origin;
            }
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object?[], RuntimeListIdentity> runtimeLists = new();

        internal static object? RuntimeCollection(object? value, Context ctx)
        {
            if (value is not object?[] array)
                return value;
            if (runtimeSets.TryGetValue(array, out var set))
                return set;
            if (!runtimeLists.TryGetValue(array, out var list))
                return array;
            if (ReferenceEquals(list.referenceIndex, ctx.rowReverseIndex) && list.reference is { } reference
                && ctx.client.ReadValue(reference.ownership, reference.valueId, ref reference.node) is ArrayMemberValue { value: not null } row)
                return ReadAssignedList(list, row, reference, ctx);
            return list.growing?.Snapshot() ?? list.current;
        }

        private static object? ReadAssignedList(RuntimeListIdentity identity, ArrayMemberValue row, RowReference reference, Context ctx)
        {
            object? current = UnwrapCached(row, ctx, reference.ownership, reference.member, reference.node);
            if (current is object?[] array && !ReferenceEquals(identity.current, array))
            {
                identity.current = array;
                runtimeLists.AddOrUpdate(array, identity);
            }
            return current;
        }

        // Field handles copy into another field. Only an unowned local list
        // can bind its identity to the destination. Composite entries need
        // their own retention policy when their owning list is replaced.
        internal static RuntimeListIdentity? AssignedListIdentity(object? value, ListMember member, Context ctx)
        {
            if (!ctx.client.TryGetMember(member.entryMemberId, out Member? entry)
                || entry is ClassMember or ListMember or DictionaryMember or TupleMember or GenericMember
                || value is not object?[] array
                || FindRowReference(array, ctx) is not null
                || ctx.ArrayDetachedOrigin(array) is not null)
                return null;
            return runtimeLists.TryGetValue(array, out var identity) && identity.captured ? identity : null;
        }

        // An assigned local list follows its destination until that field is
        // reassigned. Register its identity, not its value-shaped CLR arrays,
        // so row cache refreshes cannot replace their entries with storage ids.
        internal static void PrepareAssignedListAlias(
            NeoWritePlan plan, Context ctx, NeoValueOwnership ownership,
            string valueId, ListMember member, object? assigned)
        {
            RuntimeListIdentity? incoming = AssignedListIdentity(assigned, member, ctx);
            foreach (object alias in ctx.rowAliases.Get(ownership, valueId))
            {
                if (alias is not RuntimeListIdentity previous || ReferenceEquals(previous, incoming))
                    continue;
                var reference = new RowReference(valueId, ownership, null, member);
                if (ctx.client.ReadValue(ownership, valueId, ref reference.node) is not ArrayMemberValue { value: not null } row)
                    continue;
                object? snapshot = CopiedCollectionContents(UnwrapCached(row, ctx, ownership, member, reference.node), ctx);
                plan.AfterCommit(() =>
                {
                    ctx.rowAliases.DetachRuntimeList(ctx, ownership, valueId, previous);
                    InvalidateCachedCollection(valueId, ownership, ctx);
                    ReplaceRuntimeList(previous.origin, snapshot);
                    NoteCollectionAlias();
                });
            }
            if (incoming is not null)
            {
                plan.AfterCommit(() =>
                {
                    var reference = new RowReference(valueId, ownership, null, member);
                    if (ctx.client.ReadValue(ownership, valueId, ref reference.node) is ArrayMemberValue row)
                    {
                        reference = new RowReference(valueId, ownership, row.classId, member);
                        incoming.growing = null;
                        SetRowReference(ctx, incoming, reference);
                        InvalidateCachedCollection(valueId, ownership, ctx);
                        ReadAssignedList(incoming, row, reference, ctx);
                        NoteCollectionAlias();
                    }
                });
            }
        }

        internal static void CaptureRuntimeList(object?[] list) =>
            runtimeLists.GetValue(list, array => new RuntimeListIdentity(array)).captured = true;

        internal static void AdvanceRuntimeList(object?[] previous, LocalList growing)
        {
            if (!runtimeLists.TryGetValue(previous, out RuntimeListIdentity identity))
                return;
            identity.growing = growing;
            growing.IdentityOrigin = identity.origin;
        }

        internal static void LinkRuntimeListSnapshot(object?[] origin, object?[] snapshot)
        {
            RuntimeListIdentity identity = runtimeLists.GetValue(origin, array => new RuntimeListIdentity(array));
            identity.current = snapshot;
            runtimeLists.AddOrUpdate(snapshot, identity);
        }

        internal static bool TryGrowRuntimeList(object? value, object? entry, Context ctx)
        {
            if (value is not object?[] array
                || !runtimeLists.TryGetValue(array, out RuntimeListIdentity identity)
                || identity.growing is not { } growing)
                return false;
            ctx.allocationTracker.ActiveGeneratorRetention?.Mutation(growing, entry);
            growing.Add(entry);
            return true;
        }

        internal static void CommitRuntimeListSnapshot(object?[] snapshot)
        {
            if (runtimeLists.TryGetValue(snapshot, out RuntimeListIdentity identity))
                identity.growing?.AcceptSnapshot(snapshot);
        }

        internal static void ReplaceRuntimeList(object? previous, object? next)
        {
            if (previous is not object?[] before || next is not object?[] after)
                return;
            if (!runtimeLists.TryGetValue(before, out RuntimeListIdentity identity))
                return;
            identity.growing = null;
            identity.current = after;
            runtimeLists.AddOrUpdate(after, identity);
        }

        internal static RuntimeSet MakeRuntimeSet(object?[] source, bool reference, bool enumEntries, Context ctx, bool cache = true, bool decimalEntries = false)
        {
            var sets = runtimeSets;
            if (cache && sets.TryGetValue(source, out var existing))
                return existing;
            var set = new RuntimeSet(source, reference, enumEntries, decimalEntries,
                reference ? EqualityComparer<object?>.Default : new JsValueComparer(ctx));
            var cursor = new CollectionCursor(source, ctx);
            while (cursor.MoveNextUnresolved())
            {
                object? entry = cursor.ResolveEntry(ctx);
                object? key = RuntimeSetKey(entry, set, ctx);
                set.Add(key, enumEntries ? key : entry);
            }
            KeepDerived(set, CollectionEntryMember(source, ctx), false);
            if (orderedResults.TryGetValue(source, out var ordering))
                orderedResults.Add(set, ordering);
            if (cache)
                sets.Add(source, set);
            return set;
        }

        internal static object? DeclaredRuntimeSet(object? value, TypeInfo type, Context ctx)
        {
            if (type.type != MemberKind.Lookup || value is not object?[] array || FindRowReference(value, ctx) is not null)
                return RuntimeCollection(value, ctx);
            TypeInfo? entryType = type switch
            {
                LookupTypeInfo lookup => lookup.entryTypeInfo,
                FunctionArgumentTypeInfo argument => argument.entryTypeInfo,
                _ => null
            };
            if (entryType is null)
                return value;
            MemberKind entry = entryType.type;
            return MakeRuntimeSet(array, entry is MemberKind.Class or MemberKind.Interface or MemberKind.List or MemberKind.Dictionary or MemberKind.Lookup,
                entry is MemberKind.Enum or MemberKind.DialogueLookup, ctx, decimalEntries: entry == MemberKind.Decimal);
        }

        internal static object? RuntimeSetKey(object? value, RuntimeSet set, Context ctx)
        {
            if (set.EnumEntries && value is object?[] { Length: 1 } selected)
                value = selected[0];
            if (set.DecimalEntries && value is not null)
                return NeoSetEntryKey.DecimalKey(CoerceDecimalOperand(value, "set entry"));
            return set.Reference && value is not null ? ReferenceKeyOf(value, ctx) ?? value : value;
        }

        internal static void TrackRuntimeSetReference(object value, RuntimeSet set)
        {
            switch (value)
            {
                case NeoGeneratedClassValue { PendingValue: NeoScriptObject pending }:
                    (pending.setReferences ??= new()).Track(set, value);
                    break;
                case NeoScriptObject detached:
                    (detached.setReferences ??= new()).Track(set, value);
                    break;
                case NeoObjectRecord record:
                    (record.setReferences ??= new()).Track(set, value);
                    break;
            }
        }

        private sealed class LookupKeys
        {
            internal object? scope;
            internal long generation;
            internal RuntimeSet? set;
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object?[], LookupKeys> lookupKeys = new();

        private static bool? LookupContains(object? collection, object? target, Context ctx, bool reference)
        {
            if (collection is not object?[] array || FindRowReference(collection, ctx) is not { } row)
                return null;
            Member? member = row.CollectionMember(ctx.client);
            bool enumeration = member is EnumMember { Selection: NeoMemberSelectionKind.Multi } or DialogueLookupMember { Selection: NeoMemberSelectionKind.Multi };
            if (!enumeration && member is not LookupMember { Selection: NeoMemberSelectionKind.Multi })
                return null;
            object? scope = ctx.client.GetterCaptureId != 0 ? ctx.client
                : ctx.allocationTracker.ActiveExecutionCount != 0 ? ctx.allocationTracker : null;
            long generation = ctx.client.GetterCaptureId != 0 ? ctx.client.GetterCaptureId : ctx.allocationTracker.Generation;
            LookupKeys index = lookupKeys.GetOrCreateValue(array);
            if (scope is null || !ReferenceEquals(index.scope, scope) || index.generation != generation || index.set is null)
            {
                index.set = MakeRuntimeSet(array, reference, enumeration, ctx, cache: false);
                index.scope = scope;
                index.generation = generation;
            }
            return index.set.Contains(RuntimeSetKey(target, index.set, ctx));
        }

        internal static void InvalidateCollectionOrdering(object? value)
        {
            if (value is null)
                return;
            orderedResults.Remove(value);
            if (value is RuntimeSet set)
                orderedResults.Remove(set.Source);
            else if (value is object?[] array && runtimeSets.TryGetValue(array, out var paired))
                orderedResults.Remove(paired);
        }

        private static object? QueryEntry(object? entry, bool? enumEntries) => enumEntries == true && entry is string ? new object?[] { entry } : entry;

        private static object? EvalCollectionQuery(
            FunctionCollectionQueryInfo info,
            NeoScriptScope scope,
            Context ctx)
        {
            object? collection = EvalPointer(info.collectionPointer, scope, ctx);
            bool isList = CollectionIsList(collection);
            if (info.op is "Aggregate" or "Zip")
                return EvalPairQuery(info, collection, scope, ctx);
            double count = 0;
            if (info.op is "Skip" or "Take")
            {
                if (info.countPointer is null)
                    throw new NSGetterRuntimeError($"{info.op} requires a count.");
                if (!TryAsDouble(EvalPointer(info.countPointer, scope, ctx), out count))
                    throw new NSGetterRuntimeError($"{info.op} count must be a number.");
                if (!NeoNumbers.IsWhole(count))
                    throw new NSGetterRuntimeError($"{info.op} count must be an integer.");
            }
            object? other = null;
            if (info.op == "Concat")
            {
                if (info.otherPointer is null)
                    throw new NSGetterRuntimeError("Concat requires another collection.");
                other = EvalPointer(info.otherPointer, scope, ctx);
                CollectionIsList(other);
            }
            if (info.op == "Take" && count <= 0)
                return new object?[0];
            bool projection = info.op is not ("Any" or "All" or "Last" or "LastOrDefault" or "Single" or "SingleOrDefault" or "SkipWhile" or "TakeWhile");
            bool required = info.op is "SelectMany" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" or "MinBy" or "MaxBy" or "GroupBy" or "ToDictionary" or "All" or "SkipWhile" or "TakeWhile";
            if (required && info.function is null)
                throw new NSGetterRuntimeError($"{info.op} requires a callback.");
            PreparedCollectionCallback callback = default;
            bool hasCallback = info.function is not null;
            if (hasCallback)
            {
                callback = new PreparedCollectionCallback(
                    info.function!, scope, ctx, isList,
                    projection ? CollectionCallbackReturnContract.Projection : CollectionCallbackReturnContract.Predicate);
            }
            object? result = null;
            try
            {
                if (info.op is "Sum" or "Average" or "Min" or "Max" or "MinBy" or "MaxBy")
                    return result = ReduceCollection(info, collection, ref callback, ctx);
                if (info.op is "GroupBy" or "ToDictionary")
                    return result = DictionaryQuery(info, collection, ref callback, scope, ctx);
                if (info.op is "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending")
                {
                    object?[] ordered = OrderCollection(collection, info, ref callback, ctx);
                    // Each entry was resolved, so a string there is a value.
                    return KeepDerived(ordered, CollectionEntryMember(collection, ctx), HasString(ordered));
                }
                object?[] values = Array.Empty<object?>();
                int produced = 0;
                int matches = 0;
                bool skipping = true;
                bool all = true;
                object? selected = null;
                HashSet<object?>? seenSetKeys = null;
                var cursor = new CollectionCursor(collection, ctx);
                while (cursor.MoveNextUnresolved())
                {
                    object? entry = cursor.ResolveEntry(ctx);
                    switch (info.op)
                    {
                        case "Any":
                        case "All":
                        case "Last":
                        case "LastOrDefault":
                        case "Single":
                        case "SingleOrDefault":
                            bool matched = !hasCallback || callback.Test(in cursor, entry);
                            if (info.op == "All")
                            {
                                if (!matched)
                                {
                                    all = false;
                                    goto Finished;
                                }
                                break;
                            }
                            if (!matched)
                                break;
                            matches++;
                            selected = QueryEntry(entry, info.enumEntries);
                            if (info.op == "Any")
                                goto Finished;
                            if (matches == 2 && info.op is "Single" or "SingleOrDefault")
                                throw new NSGetterRuntimeError($"{info.op}() found more than one matching entry.");
                            break;
                        case "SelectMany":
                            object? inner = callback.Project(in cursor, entry);
                            CollectionIsList(inner);
                            var innerCursor = new CollectionCursor(inner, ctx);
                            while (innerCursor.MoveNextUnresolved())
                                AppendResult(ref values, ref produced, QueryEntry(innerCursor.ResolveEntry(ctx), info.enumEntries));
                            break;
                        case "Skip":
                            if (cursor.Index >= count)
                                AppendResult(ref values, ref produced, QueryEntry(entry, info.enumEntries));
                            break;
                        case "Take":
                            AppendResult(ref values, ref produced, QueryEntry(entry, info.enumEntries));
                            if (produced >= count)
                                goto Finished;
                            break;
                        case "SkipWhile":
                        case "TakeWhile":
                            if (skipping && !callback.Test(in cursor, entry))
                                skipping = false;
                            if (info.op == "TakeWhile" && !skipping)
                                goto Finished;
                            if (info.op == "SkipWhile" && skipping)
                                break;
                            AppendResult(ref values, ref produced, QueryEntry(entry, info.enumEntries));
                            break;
                        case "Keys":
                            AppendResult(ref values, ref produced, cursor.Key);
                            break;
                        case "Values":
                        case "Reverse":
                        case "Concat":
                            AppendResult(ref values, ref produced, QueryEntry(entry, info.enumEntries));
                            break;
                        case "Distinct":
                        case "ToSet":
                            seenSetKeys ??= new HashSet<object?>(info.reference ? EqualityComparer<object?>.Default : new JsValueComparer(ctx));
                            object? setKey = info.reference && entry is not null
                                ? cursor.ValueId ?? ReferenceKeyOf(entry, ctx) ?? entry
                                : entry;
                            if (info.decimalEntries && entry is not null)
                                setKey = NeoSetEntryKey.DecimalKey(CoerceDecimalOperand(entry, "Distinct entry"));
                            if (seenSetKeys.Add(setKey))
                                AppendResult(ref values, ref produced, QueryEntry(entry, info.enumEntries));
                            break;
                        default:
                            throw new NSGetterRuntimeError($"Unsupported collection query '{info.op}'.");
                    }
                }
            Finished:
                switch (info.op)
                {
                    case "Any":
                        result = matches != 0;
                        break;
                    case "All":
                        result = all;
                        break;
                    case "Last":
                    case "Single":
                        if (matches == 0)
                            throw new NSGetterRuntimeError($"{info.op}() found no matching entry.");
                        result = selected;
                        break;
                    case "LastOrDefault":
                    case "SingleOrDefault":
                        result = selected;
                        break;
                    default:
                        if (info.op == "Concat")
                        {
                            var otherCursor = new CollectionCursor(other, ctx);
                            while (otherCursor.MoveNextUnresolved())
                                AppendResult(ref values, ref produced, QueryEntry(otherCursor.ResolveEntry(ctx), info.enumEntries));
                        }
                        if (info.op == "Reverse")
                            Array.Reverse(values, 0, produced);
                        object?[] trimmed = TrimResult(values, produced);
                        // Each entry was resolved, so a string there is a value.
                        result = KeepDerived(
                            trimmed,
                            info.op is "SelectMany" or "Concat" ? null : CollectionEntryMember(collection, ctx),
                            HasString(trimmed));
                        break;
                }
                if (info.op == "ToSet" && result is object?[] setEntries)
                    return MakeRuntimeSet(setEntries.Length == 0 ? new object?[0] : setEntries, info.reference, info.enumEntries == true, ctx, decimalEntries: info.decimalEntries);
                return result;
            }
            catch (Exception error) when (ctx.trace.Attach(error)) { throw; }
            finally
            {
                if (hasCallback)
                {
                    callback.CompleteOperator(result);
                    callback.Dispose();
                }
            }
        }

        /// <summary>
        /// Set-key equality, including NaN, so ToSet dedupes value entries
        /// in one pass without changing expression or list-search equality.
        /// </summary>
        internal sealed class JsValueComparer : IEqualityComparer<object?>
        {
            private readonly Context context;
            internal JsValueComparer(Context context) => this.context = context;

            bool IEqualityComparer<object?>.Equals(object? x, object? y) =>
                TryAsDouble(x, out double left) && TryAsDouble(y, out double right)
                    ? left.Equals(right)
                    : JsEqual(x, y, context);

            int IEqualityComparer<object?>.GetHashCode(object? value) => Hash(value);

            internal int Hash(object? value)
            {
                switch (value)
                {
                    case RuntimeTuple tuple:
                        return TupleHash(tuple, context, this);
                    case NeoVector3Value vector:
                        return HashCode.Combine(vector.x, vector.y, vector.z);
                    case NeoVector2Value vector:
                        return HashCode.Combine(vector.x, vector.y);
                    case NeoColorValue color:
                        return HashCode.Combine(color.r, color.g, color.b, color.a);
                    case null:
                        return 0;
                    case string text:
                        return StringComparer.Ordinal.GetHashCode(text);
                    case bool flag:
                        return flag ? 1 : 2;
                    case object?[] array:
                        {
                            int hash = 17;
                            foreach (object? entry in array)
                                hash = hash * 31 + Hash(entry);
                            return hash;
                        }
                    case IDictionary<string, object?> record:
                        {
                            // Field order doesn't matter to JsEqual.
                            int hash = 19;
                            foreach (KeyValuePair<string, object?> field in record)
                                hash ^= StringComparer.Ordinal.GetHashCode(field.Key) * 31 + Hash(field.Value);
                            return hash;
                        }
                    case NeoDelegateValue:
                        return 3;
                    case NSSlotReference reference:
                        return StringComparer.Ordinal.GetHashCode(reference.TargetId);
                }
                if (TryAsDouble(value, out double number))
                    return number == 0 ? 0 : number.GetHashCode();
                return value.GetHashCode();
            }
        }

        private sealed class OrderedResult
        {
            internal object?[][] Keys = null!;
            internal List<(string? type, bool descending)> Levels = null!;
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, OrderedResult> orderedResults = new();

        private static object?[] OrderCollection(object? collection, FunctionCollectionQueryInfo info, ref PreparedCollectionCallback callback, Context ctx)
        {
            bool secondary = info.op is "ThenBy" or "ThenByDescending";
            OrderedResult? previous = null;
            if (secondary && (collection is null || !orderedResults.TryGetValue(collection, out previous)))
                throw new NSGetterRuntimeError($"{info.op} requires an ordered result.");
            var levels = previous is null ? new List<(string? type, bool descending)>() : new(previous.Levels);
            levels.Add((info.keyType, info.op.EndsWith("Descending", StringComparison.Ordinal)));
            var entries = new List<(object? value, object?[] keys)>();
            var cursor = new CollectionCursor(collection, ctx);
            while (cursor.MoveNextUnresolved())
            {
                object? entry = cursor.ResolveEntry(ctx);
                var keys = new object?[levels.Count];
                if (previous is not null)
                    Array.Copy(previous.Keys[cursor.Index], keys, levels.Count - 1);
                keys[levels.Count - 1] = callback.Project(in cursor, entry);
                entries.Add((QueryEntry(entry, info.enumEntries), keys));
            }
            var sorted = StableCollectionSort(entries.ToArray(), (a, b) =>
            {
                for (int level = 0; level < levels.Count; level++)
                {
                    int order = CompareCollectionKeys(a.keys[level], b.keys[level], levels[level].type);
                    if (order != 0)
                        return levels[level].descending ? -order : order;
                }
                return 0;
            });
            var result = new object?[sorted.Length];
            var resultKeys = new object?[sorted.Length][];
            for (int index = 0; index < sorted.Length; index++)
            {
                result[index] = sorted[index].value;
                resultKeys[index] = sorted[index].keys;
            }
            orderedResults.Add(result, new OrderedResult { Keys = resultKeys, Levels = levels });
            return result;
        }

        private static T[] StableCollectionSort<T>(T[] entries, Comparison<T> compare)
        {
            var scratch = new T[entries.Length];
            for (int width = 1; width < entries.Length; width *= 2)
            {
                for (int start = 0; start < entries.Length; start += width * 2)
                {
                    int middle = Math.Min(start + width, entries.Length);
                    int end = Math.Min(start + width * 2, entries.Length);
                    int left = start, right = middle;
                    for (int destination = start; destination < end; destination++)
                    {
                        bool useLeft = left < middle && (right >= end || compare(entries[left], entries[right]) <= 0);
                        scratch[destination] = useLeft ? entries[left++] : entries[right++];
                    }
                }
                (entries, scratch) = (scratch, entries);
            }
            return entries;
        }

        private static int CompareCollectionKeys(object? left, object? right, string? keyType)
        {
            if (left is null)
                return right is null ? 0 : -1;
            if (right is null)
                return 1;
            if (keyType == "decimal")
                return NeoDecimalMath.Compare((string)left, (string)right);
            if (keyType == "string")
                return string.CompareOrdinal((string)left, (string)right);
            if (keyType == "bool")
                return ((bool)left).CompareTo((bool)right);
            if (!TryAsDouble(left, out double a))
                throw new NSGetterRuntimeError("OrderBy left key must be a number.");
            if (!TryAsDouble(right, out double b))
                throw new NSGetterRuntimeError("OrderBy right key must be a number.");
            return a.CompareTo(b);
        }
    }
}
