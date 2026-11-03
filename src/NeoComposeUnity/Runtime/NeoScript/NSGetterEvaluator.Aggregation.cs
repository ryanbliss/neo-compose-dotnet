// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript.Numerics;
using NeoCompose.Runtime.NeoScript.Ordering;

namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NeoScriptAllocationTracker, HashSet<object>> activeSorts = new();

        internal static bool HasActiveSort(Context ctx) => activeSorts.TryGetValue(ctx.allocationTracker, out var active) && active.Count != 0;

        internal static void AssertNotSorting(object? value, Context ctx)
        {
            if (value is null || !activeSorts.TryGetValue(ctx.allocationTracker, out var active) || active.Count == 0)
                return;
            if (active.Contains(SequenceCollectionIdentity(value)))
                throw new NeoSortIntegrityError("Cannot change a list while its Sort comparison is running.");
            if (FindRowReference(value, ctx) is { } row && active.Contains(row.valueId))
                throw new NeoSortIntegrityError("Cannot change a stored list while its Sort comparison is running.");
        }

        internal static object?[] SortCollection(object? collection, CollectionCallInstruction instruction, NeoScriptScope scope, Context ctx)
        {
            AssertNotSorting(collection, ctx);
            if (collection is not object?[])
                throw new NSGetterRuntimeError("Sort requires a list target.");
            if (FindRowReference(collection, ctx)?.CollectionMember(ctx.client) is ListMember listMember && ctx.client.IsUnorderedList(listMember))
                throw new NSGetterRuntimeError("Sort requires an ordered list.");
            int sortIndex = instruction.index is null ? 0 : NeoInt32.Wrap(Number(EvalPointer(instruction.index, scope, ctx)!));
            int? count = instruction.count is null ? null : NeoInt32.Wrap(Number(EvalPointer(instruction.count, scope, ctx)!));
            BoundComparison? comparison = instruction.ordering is null ? null : BindComparison(instruction.ordering, scope, ctx);
            var entries = new List<(object? raw, object? value)>();
            var cursor = new CollectionCursor(collection, ctx);
            try
            {
                while (cursor.MoveNextUnresolved())
                    entries.Add((cursor.Raw, cursor.ResolveEntry(ctx)));
                PreparedCollectionCallback callback = default;
                if (instruction.comparison is not null)
                    callback = new PreparedCollectionCallback(instruction.comparison, scope, ctx, false, CollectionCallbackReturnContract.Projection);
                var active = activeSorts.GetOrCreateValue(ctx.allocationTracker);
                string? id = FindRowReference(collection, ctx)?.valueId;
                // Parameter normalization can copy an array without changing its list identity.
                object identity = SequenceCollectionIdentity(collection);
                active.Add(identity);
                if (id is not null)
                    active.Add(id);
                object?[]? result = null;
                try
                {
                    var sorted = entries.ToArray();
                    NeoSortHelper helper = comparison is not null && comparison.UsesDefault && instruction.comparison is null
                        ? comparison.Default.Helper : NeoSortHelper.Comparer;
                    NeoIntroSort.SortList(sorted, sortIndex, count ?? sorted.Length, helper, (a, b) =>
                    {
                        if (instruction.comparison is not null)
                        {
                            object? compared = callback.ProjectPair(a.value, b.value);
                            if (!TryAsDouble(compared, out double number) || !NeoNumbers.IsWhole(number))
                                throw new NSGetterRuntimeError("Sort comparison must return a finite int.");
                            return NeoInt32.Wrap(number);
                        }
                        if (comparison is not null)
                            return helper == NeoSortHelper.Comparable ? comparison.CompareNonNull(a.value, b.value) : comparison.Compare(a.value, b.value);
                        return CompareCollectionKeys(a.value, b.value, instruction.keyType);
                    }, comparison?.Default.Float == true && helper == NeoSortHelper.Comparable ? entry => float.IsNaN((float)Number(entry.value!)) : null,
                        () => instruction.comparison is not null ? "System.Comparison`1[" + ClrTypeName(RuntimeTypeOf(CloseContextType(instruction.comparison.parameters[0].typeInfo, ctx)), ctx) + "]" : comparison?.Text() ?? "Comparer", NeoScriptErrorClassification.IsAuthoredCatchable, entry => entry.value is null);
                    result = new object?[sorted.Length];
                    for (int index = 0; index < result.Length; index++)
                        result[index] = sorted[index].raw;
                    return result;
                }
                finally
                {
                    active.Remove(identity);
                    if (id is not null)
                        active.Remove(id);
                    if (instruction.comparison is not null)
                    {
                        callback.CompleteOperator(result);
                        callback.Dispose();
                    }
                }
            }
            catch (Exception error) when (cursor.CaptureFailure(error)) { throw; }
            finally
            {
                cursor.Dispose();
            }
        }

        /// <summary>
        /// Whether the decimal total starts at <c>0</c>, as <c>Enumerable</c>
        /// does for <c>Sum</c> and for <c>Average</c> over a List with no
        /// selector (its span path). The selector and nullable <c>Average</c>
        /// overloads, and other sources, start from the first non-null value,
        /// which keeps a leading <c>-0</c>'s sign.
        /// </summary>
        private static bool DecimalSumStartsAtZero(FunctionCollectionQueryInfo info, object? collection, Context ctx)
        {
            if (info.op != "Average" || info.keyType != "decimal")
                return true;
            if (info.function is not null)
                return false;
            if (info.nullable)
                return false;
            // .NET reads a List through its span even as an IEnumerable.
            object? source = collection is EnumerableQuery query ? query.Source : collection;
            return source is object?[] && FindRowReference(source, ctx)?.CollectionMember(ctx.client) is not SetMember;
        }

        private static IEnumerable<(object? Value, CollectionCursor Position)> ReductionEntries(object? collection, Context ctx)
        {
            var cursor = new CollectionCursor(collection, ctx);
            try
            {
                while (true)
                {
                    object? value;
                    try
                    {
                        if (!cursor.MoveNextUnresolved())
                            break;
                        value = cursor.ResolveEntry(ctx);
                    }
                    catch (Exception error) when (cursor.CaptureFailure(error)) { throw; }
                    yield return (value, cursor);
                }
            }
            finally { cursor.Dispose(); }
        }

        private static object? ReduceOrderedCollection(FunctionCollectionQueryInfo info, object? collection, PreparedCollectionCallback callback, BoundComparison? comparison, Context ctx)
        {
            var source = ReductionEntries(collection, ctx);
            object? Select((object? Value, CollectionCursor Position) entry) => info.function is null ? entry.Value : callback.Project(in entry.Position, entry.Value);
            int Compare(object? left, object? right) => comparison is null
                ? CompareCollectionKeys(left, right, info.keyType) : comparison.Compare(left, right);
            bool nullable = info.nullable;
            if (info.emptyType is { } emptyType)
            {
                TypeBinding closed = RuntimeTypeOf(CloseContextType(emptyType, ctx));
                nullable = !closed.Head!.Required || !TypeBindings.IsValueType(closed.Head);
            }
            else if (info.op is "Min" or "Max" && comparison is not null)
            {
                // Older hand-written typed sites did not carry nullable.
                nullable |= !comparison.Default.Type.Head!.Required || !TypeBindings.IsValueType(comparison.Default.Type.Head);
            }
            NeoExtreme extreme = info.op is "Min" or "MinBy" ? NeoExtreme.Min : NeoExtreme.Max;
            if (info.op is "MinBy" or "MaxBy")
            {
                var selected = NeoEnumerableOrdering.MinMaxBy(source, Select, extreme, Compare, nullable, true);
                return QueryEntry(selected.Value, info.enumEntries);
            }
            if (comparison?.Comparer is null && (comparison?.Default.Type.Head!.Type == MemberKind.Float || info.keyType == "float"))
            {
                float? selected = NeoEnumerableOrdering.MinMaxFloat32(source, entry => Select(entry) is { } value ? (float)Number(value) : null, extreme, nullable);
                return selected is null ? null : Box((double)selected.Value);
            }
            return NeoEnumerableOrdering.MinMax(source, Select, extreme, Compare, nullable);
        }

        private static object? ReduceCollection(FunctionCollectionQueryInfo info, object? collection, ref PreparedCollectionCallback callback, NeoScriptScope scope, Context ctx)
        {
            BoundComparison? comparison = info.ordering is null ? null : BindComparison(info.ordering, scope, ctx);
            if (info.op is "Min" or "Max" or "MinBy" or "MaxBy")
                return ReduceOrderedCollection(info, collection, callback, comparison, ctx);
            int count = 0;
            double total = 0;
            // Enumerable over ints: Sum is checked, Average totals in 64 bits (P127 §3.1).
            int intSum = 0;
            long intTotal = 0;
            string decimalTotal = "0";
            bool decimalSeeded = DecimalSumStartsAtZero(info, collection, ctx);
            var cursor = new CollectionCursor(collection, ctx);
            try
            {
                while (cursor.MoveNextUnresolved())
                {
                    object? entry = cursor.ResolveEntry(ctx);
                    object? value = info.function is null ? entry : callback.Project(in cursor, entry);
                    if (value is null)
                        continue;
                    count++;
                    if (info.op is "Sum" or "Average")
                    {
                        if (info.keyType == "decimal")
                        {
                            string operand = CoerceDecimalOperand(value, info.op);
                            decimalTotal = decimalSeeded ? NeoDecimalMath.Add(decimalTotal, operand) : operand;
                            decimalSeeded = true;
                        }
                        else
                        {
                            if (!TryAsDouble(value, out double number))
                                throw new NSGetterRuntimeError($"{info.op} value must be numeric.");
                            if (info.keyType != "int")
                                total += number;
                            else if (info.op == "Sum")
                                intSum = NeoInt32.Add(intSum, NeoInt32.Wrap(number), true);
                            else
                                intTotal += NeoInt32.Wrap(number);
                        }
                        continue;
                    }

                }
                if (info.op == "Sum")
                {
                    return info.keyType switch
                    {
                        "decimal" => decimalTotal,
                        "int" => Box((double)intSum),
                        "float" => Box((double)(float)total),
                        _ => Box(total),
                    };
                }
                if (count == 0)
                {
                    if (info.nullable)
                        return null;
                    throw NeoOrderingErrors.NoElements();
                }
                if (info.op == "Average")
                {
                    if (info.keyType == "int")
                        return Box((double)(float)((double)intTotal / count));
                    if (info.keyType != "decimal")
                        return Box((double)(float)(total / count));
                    // Enumerable.Average: the sum over the count with decimal / (P127 §3.4).
                    return NeoDecimalMath.Divide(decimalTotal, count.ToString(CultureInfo.InvariantCulture));
                }
                throw new NSGetterRuntimeError($"Unknown reduction '{info.op}'.");
            }
            catch (Exception error) when (cursor.CaptureFailure(error)) { throw; }
            finally
            {
                cursor.Dispose();
            }
        }

        private static object DictionaryQuery(FunctionCollectionQueryInfo info, object? collection, ref PreparedCollectionCallback keySelector, NeoScriptScope scope, Context ctx)
        {
            BoundEquality? equality = info.equality is null ? null : BindEquality(info.equality, scope, ctx);
            IDictionary<string, object?> result = new RuntimeDictionary(equality ?? BindEquality(
                new EqualitySite { type = new PrimitiveTypeInfo { type = MemberKind.String, required = true } }, scope, ctx));
            var groups = new Dictionary<string, List<object?>>();
            var groupedKeys = equality is null ? null : new NeoCompose.Runtime.NeoScript.Hashing.NeoHashTable<object?, List<object?>>(equality, NeoCompose.Runtime.NeoScript.Hashing.NeoHashTableKind.Set);
            PreparedCollectionCallback valueSelector = default;
            if (info.valueFunction is not null)
                valueSelector = new PreparedCollectionCallback(info.valueFunction, scope, ctx, CollectionIsList(collection), CollectionCallbackReturnContract.Projection);
            try
            {
                var cursor = new CollectionCursor(collection, ctx);
                try
                {
                    while (cursor.MoveNextUnresolved())
                    {
                        object? entry = cursor.ResolveEntry(ctx);
                        object? key = keySelector.Project(in cursor, entry);
                        if (info.enumKey && key is object?[] { Length: 1 } options)
                            key = options[0];
                        if (key is not string text)
                            throw new NSGetterRuntimeError($"{info.op} key must be a non-null string or enum.");
                        object? value = info.valueFunction is null ? QueryEntry(entry, info.enumEntries) : valueSelector.Project(in cursor, entry);
                        if (info.op == "ToDictionary")
                        {
                            if (equality is null && result.ContainsKey(text))
                                throw new NSGetterRuntimeError($"ToDictionary() found duplicate key '{text}'.");
                            result.Add(text, value);
                        }
                        else
                        {
                            List<object?> group;
                            if (groupedKeys is not null)
                            {
                                // A single insertion performs the hash and bucket search.
                                group = new List<object?>();
                                groupedKeys.GetOrAdd(equality!.DictionaryKey(text), group, out group);
                            }
                            else if (!groups.TryGetValue(text, out group!))
                                groups.Add(text, group = new List<object?>());
                            group.Add(value);
                        }
                    }
                    if (info.op == "GroupBy")
                    {
                        // Group lookup uses the comparer; projecting the completed groups must not hash again.
                        if (groupedKeys is not null)
                        {
                            result = new RuntimeDictionary(equality!, groupedKeys.MapValues<object?>(
                                group => KeepDerived(group.ToArray(), CollectionEntryMember(collection, ctx), true),
                                NeoCompose.Runtime.NeoScript.Hashing.NeoHashTableKind.Dictionary));
                        }
                        else
                            foreach (var group in groups)
                                result.Add(group.Key, KeepDerived(group.Value.ToArray(), CollectionEntryMember(collection, ctx), true));
                    }
                    return KeepDerived(result, CollectionEntryMember(collection, ctx), true);
                }
                catch (Exception error) when (cursor.CaptureFailure(error)) { throw; }
                finally
                {
                    cursor.Dispose();
                }
            }
            finally
            {
                if (info.valueFunction is not null)
                {
                    valueSelector.CompleteOperator(result);
                    valueSelector.Dispose();
                }
            }
        }

        private static object? EvalPairQuery(FunctionCollectionQueryInfo info, object? collection, NeoScriptScope scope, Context ctx)
        {
            object? initial = info.seedPointer is null ? null : EvalPointer(info.seedPointer, scope, ctx);
            object? other = info.op == "Zip" ? EvalPointer(info.otherPointer!, scope, ctx) : null;
            if (info.op == "Zip")
                CollectionIsList(other);
            var callback = new PreparedCollectionCallback(info.function!, scope, ctx, false, CollectionCallbackReturnContract.Projection);
            object? result = null;
            try
            {
                var cursor = new CollectionCursor(collection, ctx);
                try
                {
                    if (info.op == "Aggregate")
                    {
                        result = initial;
                        bool started = info.seedPointer is not null;
                        while (cursor.MoveNextUnresolved())
                        {
                            object? entry = cursor.ResolveEntry(ctx);
                            result = started ? callback.ProjectPair(result, entry) : QueryEntry(entry, info.enumEntries);
                            started = true;
                        }
                        if (!started)
                            throw new NSGetterRuntimeError("Aggregate() found no matching entry.");
                    }
                    else
                    {
                        var right = new CollectionCursor(other, ctx);
                        try
                        {
                            var values = new List<object?>();
                            while (cursor.MoveNextUnresolved() && right.MoveNextUnresolved())
                                values.Add(callback.ProjectPair(cursor.ResolveEntry(ctx), right.ResolveEntry(ctx)));
                            // Empty lists need distinct identities, too.
                            var entries = new object?[values.Count];
                            values.CopyTo(entries);
                            result = KeepDerived(entries, null, true);
                        }
                        catch (Exception error) when (right.CaptureFailure(error)) { throw; }
                        finally
                        {
                            right.Dispose();
                        }
                    }
                    return result;
                }
                catch (Exception error) when (cursor.CaptureFailure(error)) { throw; }
                finally
                {
                    cursor.Dispose();
                }
            }
            finally { callback.CompleteOperator(result); callback.Dispose(); }
        }
    }
}
