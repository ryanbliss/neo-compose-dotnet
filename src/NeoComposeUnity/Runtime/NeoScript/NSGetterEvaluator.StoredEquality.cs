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
        // Stored collections retain duplicate rows. Buckets retain their source
        // order, unlike an in-memory HashSet, so lookup always returns the first.
        internal sealed class StoredEqualityIndex
        {
            private readonly BoundEquality equality;
            private readonly Dictionary<int, List<(string Id, object? Value)>> buckets = new();
            private readonly Dictionary<string, int> hashes = new(StringComparer.Ordinal);
            private readonly NeoLocalization localization;
            private readonly long localizationRevision;
            private bool valid = true;
            // Localized values can change equality and hashes without a row write.
            internal bool Valid
            {
                get => valid && localization.Revision == localizationRevision;
                set => valid = value;
            }
            internal int ProbeHash;
            internal object? ProbeValue;
            internal MemberValue? PendingRow;
            internal readonly HashSet<string> PendingEntries = new(StringComparer.Ordinal);
            internal StoredEqualityIndex(BoundEquality equality, NeoLocalization localization)
            {
                this.equality = equality;
                this.localization = localization;
                localizationRevision = localization.Revision;
            }
            internal string? Find(object? candidate)
            {
                int hash = candidate is null ? 0 : equality.GetHashCode(candidate);
                try
                {
                    if (buckets.TryGetValue(hash, out var entries))
                        foreach (var entry in entries)
                            if (equality.Equals(entry.Value, candidate))
                                return entry.Id;
                    return null;
                }
                finally
                {
                    // Authored equality can perform a nested lookup on this index.
                    ProbeValue = candidate;
                    ProbeHash = hash;
                }
            }
            internal void Add(string id, object? value, int? knownHash = null)
            {
                int hash = knownHash ?? (value is null ? 0 : equality.GetHashCode(value));
                if (!buckets.TryGetValue(hash, out var entries))
                    buckets.Add(hash, entries = new());
                entries.Add((id, value));
                hashes.Add(id, hash);
            }
            internal void Remove(string id)
            {
                if (!hashes.Remove(id, out int hash))
                    return;
                var entries = buckets[hash];
                entries.RemoveAll(entry => entry.Id == id);
                if (entries.Count == 0)
                    buckets.Remove(hash);
            }
            internal void Clear()
            {
                buckets.Clear();
                hashes.Clear();
            }
        }

        private interface IHostComparer
        {
            NeoClient Client
            {
                get;
            }
            object Value
            {
                get;
            }
        }

        internal static object? UnwrapHostComparer(object? value, NeoClient client)
        {
            if (value is not IHostComparer comparer)
                return value;
            if (!ReferenceEquals(comparer.Client, client))
                throw new InvalidOperationException("An SDK value belongs to a different project client.");
            if (client.IsDisposed)
                throw new ObjectDisposedException(nameof(NeoClient));
            return comparer.Value;
        }

        private sealed class HostEqualityComparer<T> : IEqualityComparer<T>, IHostComparer
        {
            private readonly BoundEquality equality;
            private readonly Context context;
            private readonly NeoValueOwnership ownership;
            internal HostEqualityComparer(BoundEquality equality, Context context, NeoValueOwnership ownership)
            {
                this.equality = equality;
                this.context = context;
                this.ownership = ownership;
            }
            NeoClient IHostComparer.Client => context.client;
            object IHostComparer.Value => equality.Comparer ?? DefaultComparer(equality.Default.Type, NeoComparerFamilyKind.Equality, context);
            private object? Value(T value) => value is null ? null : NeoScriptValueMarshaller.Normalize(
                context.client, ownership, value, equality.Default.StaticType, context, "comparer argument");
            public bool Equals(T x, T y) => equality.Equals(Value(x), Value(y));
            public int GetHashCode(T value) => equality.GetHashCode(Value(value));
            public override bool Equals(object? value) => value is HostEqualityComparer<T> other && SameEquality(equality, other.equality, context);
            public override int GetHashCode() => ObjectHash(equality.Comparer ?? DefaultComparer(equality.Default.Type, NeoComparerFamilyKind.Equality, context), null, context);
        }

        internal static IEqualityComparer<T> HostComparer<T>(MemberValue? row, TypeBinding type, NeoValueOwnership ownership, Context context)
        {
            var equality = row is null ? new BoundEquality(type, null, context) : StoredEquality(row, type, ownership, context);
            object value = equality.Comparer ?? DefaultComparer(type, NeoComparerFamilyKind.Equality, context);
            var adapters = ComparerCache(context).HostComparers.GetValue(value, _ => new());
            if (!adapters.TryGetValue(typeof(T), out var adapter))
                adapters.Add(typeof(T), adapter = new HostEqualityComparer<T>(equality, context, ownership));
            return (IEqualityComparer<T>)adapter;
        }

        internal static bool UsesStoredEquality(MemberValue row, TypeBinding type, Context ctx) =>
            row.comparer is not null || !SelectEquality(type, ctx).KeyEquivalent;

        internal static BoundEquality StoredEquality(MemberValue row, TypeBinding type, NeoValueOwnership ownership, Context ctx)
        {
            object? comparer = row.comparer switch
            {
                { kind: NeoCollectionComparerKind.BuiltIn, comparer: { } payload } descriptor =>
                    ComparerCache(ctx).CollectionComparers.GetValue(descriptor.runtimeIdentity ?? descriptor,
                        identity => identity as NeoBuiltInComparer ?? ReadBuiltInComparer(payload, ctx)),
                { kind: NeoCollectionComparerKind.Value, valueId: { } id } => ResolveValueIfId(id, ctx, preferredOwnership: ownership),
                _ => null,
            };
            return new BoundEquality(type, comparer, ctx);
        }

        internal static StoredEqualityIndex GetStoredEqualityIndex(MemberValue row, TypeBinding type, NeoValueOwnership ownership,
            Context ctx, IEnumerable<string> ids, Func<string, object?> resolve)
        {
            var indexes = ComparerCache(ctx).StoredIndexes;
            var key = (ownership, row.id);
            if (indexes.TryGetValue(key, out var index) && index.Valid)
                return index;
            index = new StoredEqualityIndex(StoredEquality(row, type, ownership, ctx), ctx.client.Localization);
            // Publish only after every entry hashes successfully.
            foreach (string id in ids)
                index.Add(id, resolve(id));
            indexes[key] = index;
            return index;
        }

        internal static StoredEqualityIndex? CachedStoredEquality(NeoClient client, NeoValueOwnership ownership, string rowId) =>
            comparerCaches.TryGetValue(client, out var cache) && cache.StoredIndexes.TryGetValue((ownership, rowId), out var index) && index.Valid
                ? index : null;

        internal static void PreserveStoredEquality(StoredEqualityIndex? index, MemberValue row, NeoWritePlan plan)
        {
            if (index is not { Valid: true } || ReferenceEquals(index.PendingRow, row))
                return;
            index.PendingRow = row;
            plan.AfterNotifications(() => { index.PendingRow = null; index.PendingEntries.Clear(); });
        }

        internal static void DiscardStoredEquality(NeoClient client)
        {
            if (!comparerCaches.TryGetValue(client, out var cache))
                return;
            foreach (var index in cache.StoredIndexes.Values)
                index.Valid = false;
            cache.StoredIndexes.Clear();
        }

        internal static bool HasStoredEqualityIndexes(NeoClient client) =>
            comparerCaches.TryGetValue(client, out var cache) && cache.StoredIndexes.Count != 0;

        internal static void InvalidateStoredEquality(NeoClient client, NeoValueOwnership ownership, string rowId, MemberValue? stagedRow = null)
        {
            if (!comparerCaches.TryGetValue(client, out var cache) || cache.StoredIndexes.Count == 0)
                return;
            string current = rowId;
            string? child = null;
            var visited = new HashSet<(NeoValueOwnership, string)>();
            do
            {
                if (!visited.Add((ownership, current)))
                    break;
                bool valueOnly = child is not null && cache.StoredIndexes.ContainsKey((ownership, current))
                    && client.TryGetValue(ownership, current, out ObjectMemberValue? dictionary)
                    && dictionary.comparer?.valueId != child;
                if (!valueOnly && cache.StoredIndexes.TryGetValue((ownership, current), out var index))
                {
                    bool ownMutation = child is null && index.PendingRow is { } expected
                        && (stagedRow is not null ? ReferenceEquals(expected, stagedRow)
                            : client.TryGetValue(ownership, current, out MemberValue? actual) && ReferenceEquals(expected, actual));
                    bool newEntry = stagedRow is null && child is not null && index.PendingEntries.Contains(child);
                    if (!ownMutation && !newEntry)
                    {
                        cache.StoredIndexes.Remove((ownership, current));
                        index.Valid = false;
                    }
                }
                child = current;
            }
            while (cache.StoredIndexes.Count != 0 && client.TryFindOwnedParent(ownership, current, out current, out ownership));
        }

        internal static string? StoredDictionaryKey(ObjectMemberValue row, NeoValueOwnership ownership, string key, Context ctx,
            out StoredEqualityIndex? index, DictionaryMember? declared = null)
        {
            index = null;
            if (row.comparer is null)
                return row.value?.ContainsKey(key) == true ? key : null;
            if (declared is null && ctx.client.TryInferMemberForValueId(row.id, out var member))
                declared = member as DictionaryMember;
            if (declared is not { } dictionary)
                throw new NSGetterRuntimeError($"Dictionary '{row.id}' has no declared key type.");
            object KeyValue(string item) => dictionary.KeyKind == NeoDictionaryKeyKind.Enum ? MarkEnumValue(new object?[] { item }) : item;
            index = GetStoredEqualityIndex(row, DictionaryKeyType(dictionary), ownership, ctx,
                row.value?.Keys ?? (IEnumerable<string>)Array.Empty<string>(), item => KeyValue(item));
            return index.Find(KeyValue(key));
        }

        private static bool TryStoredSetContains(object? collection, object? candidate, BoundEquality requested, Context ctx, out bool contains)
        {
            contains = false;
            if (FindRowReference(collection, ctx) is not { } reference || reference.CollectionMember(ctx.client) is not SetMember
                || ctx.client.ReadValue(reference.ownership, reference.valueId, ref reference.node) is not ArrayMemberValue row)
                return false;
            if (requested.Comparer is not null)
                return false;
            if (!UsesStoredEquality(row, requested.Default.Type, ctx))
            {
                contains = SetHoldsKey(collection, candidate, reference.EntryMember(ctx.client)!, ctx);
                return true;
            }
            var index = GetStoredEqualityIndex(row, requested.Default.Type, reference.ownership, ctx, row.value ?? Array.Empty<string>(),
                id => ResolveValueIfId(id, ctx, preferredOwnership: reference.ownership, member: reference.EntryMember(ctx.client)));
            contains = index.Find(candidate) is not null;
            return true;
        }
    }
}
