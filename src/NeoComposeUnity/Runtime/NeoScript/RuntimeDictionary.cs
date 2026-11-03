// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using NeoCompose.Runtime.NeoScript.Hashing;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>A dictionary whose scripted comparer observes the F4 hash table's calls.</summary>
    internal sealed class RuntimeDictionary : IDictionary<string, object?>, IDictionary
    {
        internal readonly NSGetterEvaluator.BoundEquality Equality;
        internal readonly NeoHashTable<object?, object?> Table;
        // The table owns equality. This identity map preserves insertion order
        // without additional calls to the scripted comparer.
        private readonly OrderedMap<object, KeyValuePair<object, object?>> entries = new(NeoReferenceEqualityComparer.Instance);
        internal RuntimeDictionary(NSGetterEvaluator.BoundEquality equality, int capacity = 0)
        {
            Equality = equality;
            Table = new NeoHashTable<object?, object?>(equality, NeoHashTableKind.Dictionary, capacity, equality.KeyText);
        }
        internal RuntimeDictionary(NSGetterEvaluator.BoundEquality equality, NeoHashTable<object?, object?> table, RuntimeDictionary? source = null)
        {
            Equality = equality;
            Table = table;
            if (source is not null)
            {
                // A cached-hash copy may compact slots; retain the source's order.
                foreach (var entry in source.entries.Values)
                    entries.Add(entry.Key, entry);
            }
            else
            {
                var keys = table.Keys();
                var values = table.Values();
                for (int i = 0; i < keys.Count; i++)
                    entries.Add(keys[i]!, new KeyValuePair<object, object?>(keys[i]!, values[i]));
            }
        }
        public object? this[string key]
        {
            get => TryGetValue(key, out object? value) ? value : throw new KeyNotFoundException();
            set => Insert(key, value, NeoInsertBehavior.Overwrite);
        }
        private static string KeyString(object? key) => key is object?[] options ? (string)options[0]! : (string)key!;
        public int Count => Table.Count;
        public bool IsReadOnly => false;
        public ICollection<string> Keys
        {
            get
            {
                var keys = new List<string>(Count);
                foreach (var entry in entries.Values)
                    keys.Add(KeyString(entry.Key));
                return keys;
            }
        }
        public ICollection<object?> Values
        {
            get
            {
                var values = new List<object?>(Count);
                foreach (var entry in entries.Values)
                    values.Add(entry.Value);
                return values;
            }
        }
        private void Insert(string key, object? value, NeoInsertBehavior behavior)
        {
            Table.Add(Equality.DictionaryKey(key), value, behavior, out object? storedKey);
            entries.Set(storedKey!, new KeyValuePair<object, object?>(storedKey!, value));
        }
        public void Add(string key, object? value) => Insert(key, value, NeoInsertBehavior.Throw);
        public bool ContainsKey(string key) => Table.Contains(Equality.DictionaryKey(key));
        public bool Remove(string key)
        {
            if (!Table.Remove(Equality.DictionaryKey(key), out object? removed))
                return false;
            entries.Remove(removed!);
            return true;
        }
        public bool TryGetValue(string key, out object? value)
        {
            int slot = Table.IndexOf(Equality.DictionaryKey(key));
            value = slot < 0 ? null : Table.ValueAt(slot);
            return slot >= 0;
        }
        public void Clear()
        {
            Table.Clear();
            entries.Clear();
        }
        public void Add(KeyValuePair<string, object?> item) => Add(item.Key, item.Value);
        public bool Contains(KeyValuePair<string, object?> item) => TryGetValue(item.Key, out object? value) && ReferenceEquals(value, item.Value);
        public bool Remove(KeyValuePair<string, object?> item) => Contains(item) && Remove(item.Key);
        public void CopyTo(KeyValuePair<string, object?>[] array, int index)
        {
            foreach (var pair in this)
                array[index++] = pair;
        }
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            foreach (var entry in entries.Values)
                yield return new KeyValuePair<string, object?>(KeyString(entry.Key), entry.Value);
        }
        object? IDictionary.this[object key]
        {
            get => key is string text && TryGetValue(text, out object? value) ? value : null;
            set => this[(string)key] = value;
        }
        bool IDictionary.IsFixedSize => false;
        ICollection IDictionary.Keys => (ICollection)Keys;
        ICollection IDictionary.Values => (ICollection)Values;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;
        void IDictionary.Add(object key, object? value) => Add((string)key, value);
        bool IDictionary.Contains(object key) => key is string text && ContainsKey(text);
        void IDictionary.Remove(object key)
        {
            if (key is string text)
                Remove(text);
        }
        void ICollection.CopyTo(Array array, int index)
        {
            foreach (var pair in this)
                array.SetValue(array is DictionaryEntry[]? new DictionaryEntry(pair.Key, pair.Value) : (object)pair, index++);
        }
        IDictionaryEnumerator IDictionary.GetEnumerator() => new EntryEnumerator(GetEnumerator());
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class EntryEnumerator : IDictionaryEnumerator, IDisposable
        {
            private readonly IEnumerator<KeyValuePair<string, object?>> entries;
            internal EntryEnumerator(IEnumerator<KeyValuePair<string, object?>> entries) => this.entries = entries;
            public DictionaryEntry Entry => new(entries.Current.Key, entries.Current.Value);
            public object Key => entries.Current.Key;
            public object? Value => entries.Current.Value;
            public object Current => Entry;
            public bool MoveNext() => entries.MoveNext();
            public void Reset() => entries.Reset();
            public void Dispose() => entries.Dispose();
        }
    }
}
