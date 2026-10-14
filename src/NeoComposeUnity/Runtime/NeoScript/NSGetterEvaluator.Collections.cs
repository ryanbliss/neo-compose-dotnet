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

        internal static object? RuntimeCollection(object? value, Context ctx) =>
            value is object?[] array && runtimeSets.TryGetValue(array, out var set) ? set : value;

        internal static RuntimeSet MakeRuntimeSet(object?[] source, bool reference, bool enumEntries, Context ctx, bool cache = true)
        {
            var sets = runtimeSets;
            if (cache && sets.TryGetValue(source, out var existing))
                return existing;
            var set = new RuntimeSet(source, reference, enumEntries,
                reference ? EqualityComparer<object?>.Default : JsValueComparer.Instance);
            var cursor = new CollectionCursor(source, ctx);
            while (cursor.MoveNextUnresolved())
            {
                object? entry = cursor.ResolveEntry(ctx);
                object? key = RuntimeSetKey(entry, set, ctx);
                set.Add(key, enumEntries ? key : entry);
            }
            KeepDerived(set, CollectionEntryMember(source, ctx), false);
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
                entry is MemberKind.Enum or MemberKind.DialogueLookup, ctx);
        }

        internal static object? RuntimeSetKey(object? value, RuntimeSet set, Context ctx)
        {
            if (set.EnumEntries && value is object?[] { Length: 1 } selected)
                value = selected[0];
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

        private static object? QueryEntry(object? entry, bool? enumEntries) => enumEntries == true && entry is string ? new object?[] { entry } : entry;

        private static object? EvalCollectionQuery(
            FunctionCollectionQueryInfo info,
            NeoScriptScope scope,
            Context ctx)
        {
            object? collection = EvalPointer(info.collectionPointer, scope, ctx);
            bool isList = CollectionIsList(collection);
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
                return Array.Empty<object?>();
            bool projection = info.op is "SelectMany" or "OrderBy" or "OrderByDescending";
            bool required = projection || info.op is "All" or "SkipWhile" or "TakeWhile";
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
                if (info.op is "OrderBy" or "OrderByDescending")
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
                        case "Reverse":
                        case "Concat":
                            AppendResult(ref values, ref produced, QueryEntry(entry, info.enumEntries));
                            break;
                        case "ToSet":
                            seenSetKeys ??= new HashSet<object?>(info.reference ? EqualityComparer<object?>.Default : JsValueComparer.Instance);
                            object? setKey = info.reference && entry is not null
                                ? cursor.ValueId ?? ReferenceKeyOf(entry, ctx) ?? entry
                                : entry;
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
                    return MakeRuntimeSet(setEntries.Length == 0 ? new object?[0] : setEntries, info.reference, info.enumEntries == true, ctx);
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
        /// <see cref="JsEqual"/> as a hash comparer, so ToSet dedupes value
        /// entries in one pass.
        /// </summary>
        internal sealed class JsValueComparer : IEqualityComparer<object?>
        {
            internal static readonly JsValueComparer Instance = new();

            bool IEqualityComparer<object?>.Equals(object? x, object? y) => JsEqual(x, y);

            int IEqualityComparer<object?>.GetHashCode(object? value) => Hash(value);

            private static int Hash(object? value)
            {
                switch (value)
                {
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
                }
                if (TryAsDouble(value, out double number))
                    return number == 0 ? 0 : number.GetHashCode();
                return value.GetHashCode();
            }
        }

        private struct CollectionSortEntry
        {
            internal object? Value;
            internal object? Key;
        }

        private static object?[] OrderCollection(
            object? collection,
            FunctionCollectionQueryInfo info,
            ref PreparedCollectionCallback callback,
            Context ctx)
        {
            var entries = new CollectionSortEntry[CollectionEntryCount(collection)];
            int count = 0;
            var cursor = new CollectionCursor(collection, ctx);
            while (cursor.MoveNextUnresolved())
            {
                object? entry = cursor.ResolveEntry(ctx);
                object? key = callback.Project(in cursor, entry);
                if (count == entries.Length)
                    Array.Resize(ref entries, Math.Max(4, count * 2));
                entries[count++] = new CollectionSortEntry { Value = entry, Key = key };
            }
            var scratch = new CollectionSortEntry[count];
            for (int width = 1; width < count; width *= 2)
            {
                for (int start = 0; start < count; start += width * 2)
                {
                    int middle = Math.Min(start + width, count);
                    int end = Math.Min(start + width * 2, count);
                    int left = start;
                    int right = middle;
                    for (int destination = start; destination < end; destination++)
                    {
                        bool useLeft = right >= end;
                        if (left < middle && right < end)
                        {
                            int order = CompareCollectionKeys(entries[left].Key, entries[right].Key, info.keyType);
                            useLeft = info.op == "OrderBy" ? order <= 0 : order >= 0;
                        }
                        if (left >= middle)
                            useLeft = false;
                        scratch[destination] = useLeft ? entries[left++] : entries[right++];
                    }
                }
                (entries, scratch) = (scratch, entries);
            }
            var result = new object?[count];
            for (int index = 0; index < count; index++)
                result[index] = QueryEntry(entries[index].Value, info.enumEntries);
            return result;
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
