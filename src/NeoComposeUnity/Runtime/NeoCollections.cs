// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;

namespace NeoCompose.Runtime
{
    public class NeoReadOnlyList<T> : IReadOnlyList<T>
    {
        protected readonly NeoClient client;
        protected readonly NeoAttributeList node;
        protected readonly Func<NeoClient, NeoAttribute, T> createItem;

        public event Action? OnChanged;

        public NeoReadOnlyList(
            NeoClient client,
            NeoAttributeList node,
            Func<NeoClient, NeoAttribute, T> createItem)
        {
            this.client = client;
            this.node = node;
            this.createItem = createItem;
            this.node.OnChanged += HandleNodeChanged;
        }

        public T this[int index] => createItem(client, node[index]);

        public int Count => node.Count;

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var child in node)
            {
                yield return createItem(client, child);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        protected void HandleNodeChanged(NeoAttribute changed)
        {
            OnChanged?.Invoke();
        }
    }

    public class NeoList<T> : NeoReadOnlyList<T>, IList<T>
    {
        private readonly NeoAttributeListSaved savedNode;
        private readonly Func<T, object?> serializeItem;

        public NeoList(
            NeoClient client,
            NeoAttributeListSaved node,
            Func<NeoClient, NeoAttribute, T> createItem,
            Func<T, object?> serializeItem)
            : base(client, node, createItem)
        {
            savedNode = node;
            this.serializeItem = serializeItem;
        }

        public new T this[int index]
        {
            get => base[index];
            set => savedNode.Set(index, serializeItem(value));
        }

        public void SetValue(int index, object? value) => savedNode.Set(index, value);

        public bool IsReadOnly => false;

        public void Add(T item) => savedNode.Add(serializeItem(item));

        public void Add(object? item) => savedNode.Add(item);

        public void Clear()
        {
            for (int i = Count - 1; i >= 0; i--)
            {
                savedNode.RemoveAt(i);
            }
        }

        public bool Contains(T item) => IndexOf(item) >= 0;

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array is null) throw new ArgumentNullException(nameof(array));
            for (int i = 0; i < Count; i++)
            {
                array[arrayIndex + i] = this[i];
            }
        }

        public int IndexOf(T item)
        {
            var comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < Count; i++)
            {
                if (comparer.Equals(this[i], item)) return i;
            }
            return -1;
        }

        public void Insert(int index, T item)
        {
            throw new NotSupportedException(
                "NeoList.Insert is not supported yet; append with Add instead.");
        }

        public bool Remove(T item)
        {
            int index = IndexOf(item);
            if (index < 0) return false;
            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index) => savedNode.RemoveAt(index);
    }

    public class NeoReadOnlyDictionary<T> : IReadOnlyDictionary<string, T>
    {
        protected readonly NeoClient client;
        protected readonly NeoAttributeDictionary node;
        protected readonly Func<NeoClient, NeoAttribute, T> createItem;

        public event Action? OnChanged;

        public NeoReadOnlyDictionary(
            NeoClient client,
            NeoAttributeDictionary node,
            Func<NeoClient, NeoAttribute, T> createItem)
        {
            this.client = client;
            this.node = node;
            this.createItem = createItem;
            this.node.OnChanged += HandleNodeChanged;
        }

        public T this[string key] => createItem(client, node[key]);

        public IEnumerable<string> Keys
        {
            get
            {
                foreach (var kvp in node)
                {
                    yield return kvp.Key;
                }
            }
        }

        public IEnumerable<T> Values
        {
            get
            {
                foreach (var kvp in node)
                {
                    yield return createItem(client, kvp.Value);
                }
            }
        }

        public int Count => node.Count;

        public bool ContainsKey(string key) => node.ContainsKey(key);

        public IEnumerator<KeyValuePair<string, T>> GetEnumerator()
        {
            foreach (var kvp in node)
            {
                yield return new KeyValuePair<string, T>(
                    kvp.Key,
                    createItem(client, kvp.Value));
            }
        }

        public bool TryGetValue(string key, out T value)
        {
            if (node.TryGet<NeoAttribute>(key, out NeoAttribute? child))
            {
                value = createItem(client, child);
                return true;
            }
            value = default!;
            return false;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        protected void HandleNodeChanged(NeoAttribute changed)
        {
            OnChanged?.Invoke();
        }
    }

    public class NeoDictionary<T> : NeoReadOnlyDictionary<T>, IDictionary<string, T>
    {
        private readonly NeoAttributeDictionarySaved savedNode;
        private readonly Func<T, object?> serializeItem;

        public NeoDictionary(
            NeoClient client,
            NeoAttributeDictionarySaved node,
            Func<NeoClient, NeoAttribute, T> createItem,
            Func<T, object?> serializeItem)
            : base(client, node, createItem)
        {
            savedNode = node;
            this.serializeItem = serializeItem;
        }

        public new T this[string key]
        {
            get => base[key];
            set => savedNode.Set(key, serializeItem(value));
        }

        public void SetValue(string key, object? value) => savedNode.Set(key, value);

        public new ICollection<string> Keys
        {
            get
            {
                var keys = new List<string>();
                foreach (var key in base.Keys) keys.Add(key);
                return keys;
            }
        }

        public new ICollection<T> Values
        {
            get
            {
                var values = new List<T>();
                foreach (var value in base.Values) values.Add(value);
                return values;
            }
        }

        public bool IsReadOnly => false;

        public void Add(string key, T value) =>
            savedNode.Set(key, serializeItem(value));

        public void AddValue(string key, object? value) =>
            savedNode.Set(key, value);

        public void Add(KeyValuePair<string, T> item) => Add(item.Key, item.Value);

        public void Clear()
        {
            var keys = new List<string>(Keys);
            foreach (var key in keys)
            {
                savedNode.Remove(key);
            }
        }

        public bool Contains(KeyValuePair<string, T> item)
        {
            if (!TryGetValue(item.Key, out T existing)) return false;
            return EqualityComparer<T>.Default.Equals(existing, item.Value);
        }

        public void CopyTo(KeyValuePair<string, T>[] array, int arrayIndex)
        {
            if (array is null) throw new ArgumentNullException(nameof(array));
            foreach (var kvp in this)
            {
                array[arrayIndex++] = kvp;
            }
        }

        public bool Remove(string key)
        {
            if (!ContainsKey(key)) return false;
            savedNode.Remove(key);
            return true;
        }

        public bool Remove(KeyValuePair<string, T> item)
        {
            if (!Contains(item)) return false;
            return Remove(item.Key);
        }
    }
}
