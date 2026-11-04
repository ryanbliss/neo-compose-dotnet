// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using NeoCompose.Runtime.Json;

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
                throw new NSGetterRuntimeError("Cannot change a list while its Sort comparison is running.");
            if (FindRowReference(value, ctx) is { } row && active.Contains(row.valueId))
                throw new NSGetterRuntimeError("Cannot change a stored list while its Sort comparison is running.");
        }

        internal static object?[] SortCollection(object? collection, CollectionCallInstruction instruction, NeoScriptScope scope, Context ctx)
        {
            AssertNotSorting(collection, ctx);
            if (collection is not object?[])
                throw new NSGetterRuntimeError("Sort requires a list target.");
            if (FindRowReference(collection, ctx)?.CollectionMember(ctx.client) is ListMember listMember && ctx.client.IsUnorderedList(listMember))
                throw new NSGetterRuntimeError("Sort requires an ordered list.");
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
                    var sorted = StableCollectionSort(entries.ToArray(), (a, b) =>
                    {
                        if (instruction.comparison is null)
                            return CompareCollectionKeys(a.value, b.value, instruction.keyType);
                        object? compared = callback.ProjectPair(a.value, b.value);
                        if (!TryAsDouble(compared, out double number) || !NeoNumbers.IsWhole(number))
                            throw new NSGetterRuntimeError("Sort comparison must return a finite int.");
                        return number.CompareTo(0d);
                    });
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

        private static object? ReduceCollection(FunctionCollectionQueryInfo info, object? collection, ref PreparedCollectionCallback callback, Context ctx)
        {
            int count = 0;
            double total = 0;
            string decimalTotal = "0";
            bool decimalSeeded = DecimalSumStartsAtZero(info, collection, ctx);
            object? selected = null, bestKey = null, first = null;
            bool visited = false;
            bool by = info.op is "MinBy" or "MaxBy";
            var cursor = new CollectionCursor(collection, ctx);
            try
            {
                while (cursor.MoveNextUnresolved())
                {
                    object? entry = cursor.ResolveEntry(ctx);
                    if (!visited)
                        first = QueryEntry(entry, info.enumEntries);
                    visited = true;
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
                            total += number;
                        }
                        if (info.keyType == "int" && (double.IsNaN(total) || double.IsInfinity(total)))
                            throw new NSGetterRuntimeError($"{info.op} integer total must be finite.");
                        continue;
                    }
                    int order = count == 1 ? 0 : CompareCollectionKeys(value, bestKey, info.keyType);
                    if (count == 1 || (info.op is "Min" or "MinBy" ? order < 0 : order > 0))
                    {
                        bestKey = value;
                        selected = by ? QueryEntry(entry, info.enumEntries) : value;
                    }
                    if (info.op == "Min" && TryAsDouble(value, out double candidate) && double.IsNaN(candidate))
                        break;
                }
                if (info.op == "Sum")
                    return info.keyType == "decimal" ? decimalTotal : Box(info.keyType == "float" ? (double)(float)total : total);
                if (count == 0)
                {
                    if (by && visited)
                        return first;
                    if (info.nullable)
                        return null;
                    throw new NSGetterRuntimeError($"{info.op}() found no matching entry.");
                }
                if (info.op == "Average")
                {
                    if (info.keyType != "decimal")
                        return Box((double)(float)(total / count));
                    // Enumerable.Average: the sum over the count with decimal / (P127 §3.4).
                    return NeoDecimalMath.Divide(decimalTotal, count.ToString(CultureInfo.InvariantCulture));
                }
                return selected;
            }
            catch (Exception error) when (cursor.CaptureFailure(error)) { throw; }
            finally
            {
                cursor.Dispose();
            }
        }

        private static object DictionaryQuery(FunctionCollectionQueryInfo info, object? collection, ref PreparedCollectionCallback keySelector, NeoScriptScope scope, Context ctx)
        {
            var result = new Dictionary<string, object?>();
            var groups = new Dictionary<string, List<object?>>();
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
                            if (result.ContainsKey(text))
                                throw new NSGetterRuntimeError($"ToDictionary() found duplicate key '{text}'.");
                            result.Add(text, value);
                        }
                        else
                        {
                            if (!groups.TryGetValue(text, out var group))
                                groups.Add(text, group = new List<object?>());
                            group.Add(value);
                        }
                    }
                    if (info.op == "GroupBy")
                        foreach (var group in groups)
                            result.Add(group.Key, KeepDerived(group.Value.ToArray(), CollectionEntryMember(collection, ctx), true));
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
