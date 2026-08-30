// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Deterministic work counters for List identity/declared indexes. These
    /// counters make complexity regressions testable without flaky timing
    /// thresholds across Unity runtimes.
    /// </summary>
    internal sealed class NeoListIndexDiagnostics
    {
        internal long IdentityBuildCount;
        internal long IdentityBuildEntryCount;
        internal long IdentityLookupCount;
        internal long DerivedBuildCount;
        internal long DerivedBuildEntryCount;
        internal long DerivedLookupCount;
        internal long DerivedIncrementalUpdateCount;
    }

    /// <summary>
    /// Shared raw (wire-key to entry-value-id) cache. Both generated typed
    /// views and the NeoScript evaluator delegate to this state so collision
    /// and invalidation behavior cannot drift.
    /// </summary>
    internal sealed class NeoRawListIndex
    {
        private readonly NeoMemberList list;
        private readonly ListIndexDefinition definition;
        private Dictionary<string, List<string>>? buckets;
        private Dictionary<string, string?>? keysByValueId;
        private HashSet<string>? duplicateKeys;
        private string? resolvedKeyKind;
        private string? resolvedKeyEnumId;

        internal NeoRawListIndex(
            NeoMemberList list,
            ListIndexDefinition definition)
        {
            this.list = list ?? throw new ArgumentNullException(nameof(list));
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        internal IEnumerable<string> Keys
        {
            get
            {
                EnsureBuiltAndValid();
                return buckets!.Keys;
            }
        }

        internal int Count
        {
            get
            {
                EnsureBuiltAndValid();
                return buckets!.Count;
            }
        }

        internal bool TryGetUnique(string rawKey, [NotNullWhen(true)] out string? valueId)
        {
            if (rawKey is null) throw new ArgumentNullException(nameof(rawKey));
            EnsureBuiltAndValid();
            list.IndexDiagnostics.DerivedLookupCount += 1;
            if (buckets!.TryGetValue(rawKey, out List<string>? bucket)
                && bucket.Count == 1)
            {
                valueId = bucket[0];
                return true;
            }
            valueId = null;
            return false;
        }

        internal IReadOnlyList<string> GetMany(string rawKey)
        {
            if (rawKey is null) throw new ArgumentNullException(nameof(rawKey));
            EnsureBuiltAndValid();
            list.IndexDiagnostics.DerivedLookupCount += 1;
            return buckets!.TryGetValue(rawKey, out List<string>? bucket)
                ? bucket
                : Array.Empty<string>();
        }

        internal void ValidateKeyContract(string keyKind, string? keyEnumId)
        {
            EnsureBuiltAndValid();
            // An empty/all-null List has no runtime field node to inspect;
            // schema validation remains authoritative until an entry exists.
            if (resolvedKeyKind is null) return;
            if (!string.Equals(resolvedKeyKind, keyKind, StringComparison.Ordinal)
                || (resolvedKeyKind == ListIndexKeyKind.Enum
                    && !string.Equals(
                        resolvedKeyEnumId,
                        keyEnumId,
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"List index '{definition.schemaKey}' runtime key contract is "
                    + $"'{resolvedKeyKind}'{(resolvedKeyEnumId is null ? "" : $" ({resolvedKeyEnumId})")}, "
                    + $"but compiled IR expects '{keyKind}'{(keyEnumId is null ? "" : $" ({keyEnumId})")}.");
            }
        }

        internal void UpdateEntry(string valueId)
        {
            if (buckets is null || keysByValueId is null) return;
            RemoveEntry(valueId);
            if (!list.TryGetChildById(valueId, out NeoMember? child)) return;
            string? rawKey = ReadRawKey(child);
            keysByValueId[valueId] = rawKey;
            if (rawKey is not null) AddToBucket(rawKey, valueId);
            list.IndexDiagnostics.DerivedIncrementalUpdateCount += 1;
        }

        internal void RemoveEntry(string valueId)
        {
            if (buckets is null || keysByValueId is null) return;
            if (!keysByValueId.TryGetValue(valueId, out string? oldKey)) return;
            keysByValueId.Remove(valueId);
            if (oldKey is null) return;
            if (!buckets.TryGetValue(oldKey, out List<string>? bucket)) return;
            int previousCount = bucket.Count;
            bucket.Remove(valueId);
            if (bucket.Count == 0)
            {
                buckets.Remove(oldKey);
                duplicateKeys?.Remove(oldKey);
            }
            else if (definition.Kind == NeoListIndexKind.Unique && previousCount > 1 && bucket.Count == 1)
            {
                duplicateKeys?.Remove(oldKey);
            }
        }

        internal void Clear()
        {
            if (buckets is null) return;
            buckets.Clear();
            keysByValueId!.Clear();
            duplicateKeys?.Clear();
        }

        internal void Invalidate()
        {
            buckets = null;
            keysByValueId = null;
            duplicateKeys = null;
            resolvedKeyKind = null;
            resolvedKeyEnumId = null;
        }

        private void EnsureBuiltAndValid()
        {
            EnsureBuilt();
            if (definition.Kind == NeoListIndexKind.Unique && duplicateKeys is { Count: > 0 })
            {
                throw new InvalidOperationException(
                    $"Unique List index '{definition.schemaKey}' on member "
                    + $"'{list.member.id}' contains duplicate key(s): "
                    + string.Join(", ", duplicateKeys));
            }
        }

        private void EnsureBuilt()
        {
            if (buckets is not null) return;
            // Declared indexes store value ids, so warm their shared identity
            // map at the same lazy boundary. Constructing generated accessor
            // objects alone remains allocation-light.
            list.EnsureIdentityIndex();
            var nextBuckets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var nextKeys = new Dictionary<string, string?>(StringComparer.Ordinal);
            var nextDuplicates = definition.Kind == NeoListIndexKind.Unique
                ? new HashSet<string>(StringComparer.Ordinal)
                : null;

            // Install the state before adding entries so AddToBucket uses the
            // same helpers as incremental updates. Roll back on malformed
            // runtime data so a repaired project can retry later.
            buckets = nextBuckets;
            keysByValueId = nextKeys;
            duplicateKeys = nextDuplicates;
            try
            {
                foreach (NeoMember child in list)
                {
                    string valueId = list.EntryValueId(child);
                    string? rawKey = ReadRawKey(child);
                    nextKeys.Add(valueId, rawKey);
                    if (rawKey is not null) AddToBucket(rawKey, valueId);
                    list.IndexDiagnostics.DerivedBuildEntryCount += 1;
                }
                list.IndexDiagnostics.DerivedBuildCount += 1;
            }
            catch
            {
                Invalidate();
                throw;
            }
        }

        private void AddToBucket(string rawKey, string valueId)
        {
            if (!buckets!.TryGetValue(rawKey, out List<string>? bucket))
            {
                bucket = new List<string>();
                buckets.Add(rawKey, bucket);
            }
            bucket.Add(valueId);
            if (definition.Kind == NeoListIndexKind.Unique && bucket.Count > 1)
            {
                duplicateKeys!.Add(rawKey);
            }
        }

        private string? ReadRawKey(NeoMember entry)
        {
            if (entry is not NeoMemberClass classNode || classNode.value is null)
            {
                return null;
            }
            if (!classNode.TryGet(definition.schemaKey, out NeoMember? keyNode))
            {
                throw new InvalidOperationException(
                    $"List index '{definition.schemaKey}' on member "
                    + $"'{list.member.id}' could not resolve that field on "
                    + $"entry value '{list.EntryValueId(entry)}'.");
            }
            switch (keyNode)
            {
                case NeoMemberString text:
                    if (text.member.Format == NeoStringFormatKind.Localized)
                    {
                        throw InvalidKeyKind(entry,
                            "localized String fields are not indexable");
                    }
                    ObserveKeyContract(ListIndexKeyKind.String, null, entry);
                    return text.value?.value;
                case NeoMemberEnum selected:
                    if (selected.member.Selection == NeoMemberSelectionKind.Multi)
                    {
                        throw InvalidKeyKind(entry,
                            "multi-select Enum fields are not indexable");
                    }
                    ObserveKeyContract(
                        ListIndexKeyKind.Enum,
                        selected.member.enumId,
                        entry);
                    string[] optionIds = selected.Selected();
                    if (optionIds.Length == 0) return null;
                    if (optionIds.Length > 1)
                    {
                        throw InvalidKeyKind(entry,
                            "single-select Enum runtime data contains multiple option ids");
                    }
                    return optionIds[0];
                default:
                    throw InvalidKeyKind(entry,
                        $"field runtime kind '{keyNode.GetType().Name}' is not String or Enum");
            }
        }

        private void ObserveKeyContract(
            string keyKind,
            string? keyEnumId,
            NeoMember entry)
        {
            if (resolvedKeyKind is null)
            {
                resolvedKeyKind = keyKind;
                resolvedKeyEnumId = keyEnumId;
                return;
            }
            if (resolvedKeyKind != keyKind
                || (keyKind == ListIndexKeyKind.Enum
                    && resolvedKeyEnumId != keyEnumId))
            {
                throw InvalidKeyKind(
                    entry,
                    "polymorphic entries resolve incompatible index key contracts");
            }
        }

        private InvalidOperationException InvalidKeyKind(
            NeoMember entry,
            string reason)
        {
            return new InvalidOperationException(
                $"List index '{definition.schemaKey}' on member "
                + $"'{list.member.id}' is invalid for entry value "
                + $"'{list.EntryValueId(entry)}': {reason}.");
        }
    }

    /// <summary>
    /// Read-only typed view of a unique derived List index. Missing keys
    /// return null; duplicate keys invalidate the entire view until repaired.
    /// </summary>
    public sealed class NeoUniqueListIndex<TKey, TItem>
        : IReadOnlyDictionary<TKey, TItem>
        where TItem : class
    {
        private readonly NeoClient client;
        private readonly NeoMemberList list;
        private readonly NeoRawListIndex index;
        private readonly Func<NeoClient, NeoMember, TItem?> createItem;
        private readonly Func<string, TKey> fromRawKey;
        private readonly Func<TKey, string> toRawKey;

        public NeoUniqueListIndex(
            NeoClient client,
            NeoMemberList list,
            string schemaKey,
            Func<NeoClient, NeoMember, TItem?> createItem)
            : this(client, list, schemaKey, createItem, StringKeyFromRaw, StringKeyToRaw)
        {
        }

        public NeoUniqueListIndex(
            NeoClient client,
            NeoMemberList list,
            string schemaKey,
            Func<NeoClient, NeoMember, TItem?> createItem,
            Func<string, TKey> fromRawKey,
            Func<TKey, string> toRawKey)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.list = list ?? throw new ArgumentNullException(nameof(list));
            this.createItem = createItem ?? throw new ArgumentNullException(nameof(createItem));
            this.fromRawKey = fromRawKey ?? throw new ArgumentNullException(nameof(fromRawKey));
            this.toRawKey = toRawKey ?? throw new ArgumentNullException(nameof(toRawKey));
            index = list.GetDerivedIndex(schemaKey, unique: true);
        }

        public TItem? this[TKey key] =>
            TryGetValue(key, out TItem? item) ? item : null;

        TItem IReadOnlyDictionary<TKey, TItem>.this[TKey key] =>
            TryGetValue(key, out TItem? item)
                ? item
                : throw new KeyNotFoundException(
                    $"List index does not contain key '{key}'.");

        public IEnumerable<TKey> Keys
        {
            get
            {
                foreach (string rawKey in index.Keys) yield return fromRawKey(rawKey);
            }
        }

        public IEnumerable<TItem> Values
        {
            get
            {
                foreach (string rawKey in index.Keys)
                {
                    if (index.TryGetUnique(rawKey, out string? valueId))
                    {
                        yield return Materialize(valueId);
                    }
                }
            }
        }

        public int Count => index.Count;

        public bool ContainsKey(TKey key) =>
            index.TryGetUnique(RawKey(key), out _);

        public bool TryGetValue(
            TKey key,
            [NotNullWhen(true)] out TItem? item)
        {
            if (index.TryGetUnique(RawKey(key), out string? valueId))
            {
                item = Materialize(valueId);
                return true;
            }
            item = null;
            return false;
        }

        bool IReadOnlyDictionary<TKey, TItem>.TryGetValue(
            TKey key,
            out TItem item)
        {
            if (TryGetValue(key, out TItem? found))
            {
                item = found;
                return true;
            }
            item = null!;
            return false;
        }

        public IEnumerator<KeyValuePair<TKey, TItem>> GetEnumerator()
        {
            foreach (string rawKey in index.Keys)
            {
                if (index.TryGetUnique(rawKey, out string? valueId))
                {
                    yield return new KeyValuePair<TKey, TItem>(
                        fromRawKey(rawKey),
                        Materialize(valueId));
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private TItem Materialize(string valueId)
        {
            if (!list.TryGetChildById(valueId, out NeoMember? child))
            {
                throw new InvalidOperationException(
                    $"List index resolved entry '{valueId}', but it is no longer a member of the List.");
            }
            return createItem(client, child) ?? throw new InvalidOperationException(
                $"List index resolved non-null entry '{valueId}', but its generated item factory returned null.");
        }

        private string RawKey(TKey key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return toRawKey(key) ?? throw new InvalidOperationException(
                "List index key codec returned null.");
        }

        private static TKey StringKeyFromRaw(string key)
        {
            if (typeof(TKey) != typeof(string))
            {
                throw new InvalidOperationException(
                    $"The codec-free List index constructor only supports String keys, not {typeof(TKey).Name}.");
            }
            return (TKey)(object)key;
        }

        private static string StringKeyToRaw(TKey key)
        {
            if (key is string text) return text;
            throw new InvalidOperationException(
                $"The codec-free List index constructor only supports String keys, not {typeof(TKey).Name}.");
        }
    }

    /// <summary>Read-only typed view of a zero-or-many derived List index.</summary>
    public sealed class NeoMultiListIndex<TKey, TItem>
        : IReadOnlyDictionary<TKey, IReadOnlyList<TItem>>
        where TItem : class
    {
        private readonly NeoClient client;
        private readonly NeoMemberList list;
        private readonly NeoRawListIndex index;
        private readonly Func<NeoClient, NeoMember, TItem?> createItem;
        private readonly Func<string, TKey> fromRawKey;
        private readonly Func<TKey, string> toRawKey;

        public NeoMultiListIndex(
            NeoClient client,
            NeoMemberList list,
            string schemaKey,
            Func<NeoClient, NeoMember, TItem?> createItem)
            : this(client, list, schemaKey, createItem, StringKeyFromRaw, StringKeyToRaw)
        {
        }

        public NeoMultiListIndex(
            NeoClient client,
            NeoMemberList list,
            string schemaKey,
            Func<NeoClient, NeoMember, TItem?> createItem,
            Func<string, TKey> fromRawKey,
            Func<TKey, string> toRawKey)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.list = list ?? throw new ArgumentNullException(nameof(list));
            this.createItem = createItem ?? throw new ArgumentNullException(nameof(createItem));
            this.fromRawKey = fromRawKey ?? throw new ArgumentNullException(nameof(fromRawKey));
            this.toRawKey = toRawKey ?? throw new ArgumentNullException(nameof(toRawKey));
            index = list.GetDerivedIndex(schemaKey, unique: false);
        }

        public IReadOnlyList<TItem> this[TKey key] => Materialize(index.GetMany(RawKey(key)));

        IReadOnlyList<TItem> IReadOnlyDictionary<TKey, IReadOnlyList<TItem>>.this[TKey key] =>
            TryGetValue(key, out IReadOnlyList<TItem> items)
                ? items
                : throw new KeyNotFoundException(
                    $"List index does not contain key '{key}'.");

        public IEnumerable<TKey> Keys
        {
            get
            {
                foreach (string rawKey in index.Keys) yield return fromRawKey(rawKey);
            }
        }

        public IEnumerable<IReadOnlyList<TItem>> Values
        {
            get
            {
                foreach (string rawKey in index.Keys)
                {
                    yield return Materialize(index.GetMany(rawKey));
                }
            }
        }

        public int Count => index.Count;

        public bool ContainsKey(TKey key) => index.GetMany(RawKey(key)).Count > 0;

        public bool TryGetValues(TKey key, out IReadOnlyList<TItem> items)
        {
            IReadOnlyList<string> valueIds = index.GetMany(RawKey(key));
            if (valueIds.Count == 0)
            {
                items = Array.Empty<TItem>();
                return false;
            }
            items = Materialize(valueIds);
            return true;
        }

        public bool TryGetValue(TKey key, out IReadOnlyList<TItem> items) =>
            TryGetValues(key, out items);

        public IEnumerator<KeyValuePair<TKey, IReadOnlyList<TItem>>> GetEnumerator()
        {
            foreach (string rawKey in index.Keys)
            {
                yield return new KeyValuePair<TKey, IReadOnlyList<TItem>>(
                    fromRawKey(rawKey),
                    Materialize(index.GetMany(rawKey)));
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private IReadOnlyList<TItem> Materialize(IReadOnlyList<string> valueIds)
        {
            if (valueIds.Count == 0) return Array.Empty<TItem>();
            var items = new List<TItem>(valueIds.Count);
            foreach (string valueId in valueIds)
            {
                if (!list.TryGetChildById(valueId, out NeoMember? child))
                {
                    throw new InvalidOperationException(
                        $"List index resolved entry '{valueId}', but it is no longer a member of the List.");
                }
                items.Add(createItem(client, child) ?? throw new InvalidOperationException(
                    $"List index resolved non-null entry '{valueId}', but its generated item factory returned null."));
            }
            return items;
        }

        private string RawKey(TKey key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return toRawKey(key) ?? throw new InvalidOperationException(
                "List index key codec returned null.");
        }

        private static TKey StringKeyFromRaw(string key)
        {
            if (typeof(TKey) != typeof(string))
            {
                throw new InvalidOperationException(
                    $"The codec-free List index constructor only supports String keys, not {typeof(TKey).Name}.");
            }
            return (TKey)(object)key;
        }

        private static string StringKeyToRaw(TKey key)
        {
            if (key is string text) return text;
            throw new InvalidOperationException(
                $"The codec-free List index constructor only supports String keys, not {typeof(TKey).Name}.");
        }
    }
}
