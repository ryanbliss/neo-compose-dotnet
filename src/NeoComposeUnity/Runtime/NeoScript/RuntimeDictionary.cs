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
    internal sealed class RuntimeDictionary : IDictionary<string, object?>
    {
        internal readonly NSGetterEvaluator.BoundEquality Equality;
        internal readonly NeoHashTable<object?, object?> Table;
        internal RuntimeDictionary(NSGetterEvaluator.BoundEquality equality, int capacity = 0)
        {
            Equality = equality;
            Table = new NeoHashTable<object?, object?>(equality, NeoHashTableKind.Dictionary, capacity, equality.KeyText);
        }
        internal RuntimeDictionary(NSGetterEvaluator.BoundEquality equality, NeoHashTable<object?, object?> table)
        {
            Equality = equality;
            Table = table;
        }
        public object? this[string key]
        {
            get => TryGetValue(key, out object? value) ? value : throw new KeyNotFoundException();
            set => Table.Add(Equality.DictionaryKey(key), value, NeoInsertBehavior.Overwrite);
        }
        private static string KeyString(object? key) => key is object?[] options ? (string)options[0]! : (string)key!;
        public int Count => Table.Count;
        public bool IsReadOnly => false;
        public ICollection<string> Keys => Table.Keys().ConvertAll(KeyString);
        public ICollection<object?> Values => Table.Values();
        public void Add(string key, object? value) => Table.Add(Equality.DictionaryKey(key), value, NeoInsertBehavior.Throw);
        public bool ContainsKey(string key) => Table.Contains(Equality.DictionaryKey(key));
        public bool Remove(string key) => Table.Remove(Equality.DictionaryKey(key));
        public bool TryGetValue(string key, out object? value)
        {
            int slot = Table.IndexOf(Equality.DictionaryKey(key));
            value = slot < 0 ? null : Table.ValueAt(slot);
            return slot >= 0;
        }
        public void Clear() => Table.Clear();
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
            var keys = Table.Keys();
            var values = Table.Values();
            for (int i = 0; i < keys.Count; i++)
                yield return new KeyValuePair<string, object?>(KeyString(keys[i]), values[i]);
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
