// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>Keyed membership with insertion order and constant-time deletion.</summary>
    internal sealed class OrderedMap<TKey, TValue>
    {
        private readonly Dictionary<TKey, LinkedListNode<TValue>> keys;
        private readonly LinkedList<TValue> entries = new();
        private TValue[]? snapshot;
        internal OrderedMap(IEqualityComparer<TKey> comparer) => keys = new(comparer);
        internal int Count => keys.Count;
        internal IEnumerable<TValue> Values => entries;
        internal bool Contains(TKey key) => keys.ContainsKey(key);
        internal bool Add(TKey key, TValue value)
        {
            if (keys.ContainsKey(key))
                return false;
            keys.Add(key, entries.AddLast(value));
            snapshot = null;
            return true;
        }
        internal bool Remove(TKey key)
        {
            if (!keys.TryGetValue(key, out var node))
                return false;
            keys.Remove(key);
            entries.Remove(node);
            snapshot = null;
            return true;
        }
        internal void Clear()
        {
            keys.Clear();
            entries.Clear();
            snapshot = null;
        }
        internal TValue[] Snapshot()
        {
            if (snapshot is not null)
                return snapshot;
            snapshot = new TValue[Count];
            entries.CopyTo(snapshot, 0);
            return snapshot;
        }
    }
}
