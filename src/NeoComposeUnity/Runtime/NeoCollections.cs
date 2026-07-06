// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;

namespace NeoCompose.Runtime
{
    public enum NeoListChangeKind
    {
        Unknown = 0,
        Add = 1,
        Set = 2,
        Remove = 3,
        Clear = 4,
        Replace = 5,
    }

    public sealed class NeoListChangedArgs
    {
        public static readonly NeoListChangedArgs Unknown =
            new(NeoListChangeKind.Unknown);

        public NeoListChangedArgs(
            NeoListChangeKind kind,
            IReadOnlyList<string>? removedValueIds = null,
            IReadOnlyList<string>? addedValueIds = null,
            IReadOnlyList<string>? replacedValueIds = null)
        {
            Kind = kind;
            RemovedValueIds = removedValueIds ?? Array.Empty<string>();
            AddedValueIds = addedValueIds ?? Array.Empty<string>();
            ReplacedValueIds = replacedValueIds ?? Array.Empty<string>();
        }

        public NeoListChangeKind Kind { get; }
        public IReadOnlyList<string> RemovedValueIds { get; }
        public IReadOnlyList<string> AddedValueIds { get; }
        public IReadOnlyList<string> ReplacedValueIds { get; }
    }

    internal static class NeoCollectionSubscription
    {
        /// <summary>
        /// Shared wiring for collection change subscriptions: relays the
        /// wrapper node's change event to the handler with the collection and
        /// the client's current change source, until the returned
        /// subscription is disposed.
        /// </summary>
        public static IDisposable Watch<TCollection>(
            NeoAttribute node,
            NeoClient client,
            TCollection collection,
            Action<TCollection, NeoChangeSource> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            void Handle(NeoAttribute changed) => handler(collection, client.CurrentChangeSource);
            node.OnChanged += Handle;
            return new NeoDisposableSubscription(() => node.OnChanged -= Handle);
        }

        public static IDisposable WatchList<T>(
            NeoAttributeList node,
            NeoClient client,
            NeoReadOnlyList<T> collection,
            Action<NeoReadOnlyList<T>, NeoListChangedArgs, NeoChangeSource> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            void Handle(NeoAttribute changed) =>
                handler(
                    collection,
                    node.ActiveListChange ?? NeoListChangedArgs.Unknown,
                    client.CurrentChangeSource);
            node.OnChanged += Handle;
            return new NeoDisposableSubscription(() => node.OnChanged -= Handle);
        }
    }

    public class NeoReadOnlyList<T> : IReadOnlyList<T>
    {
        protected readonly NeoClient client;
        protected NeoAttributeList node;
        protected readonly Func<NeoClient, NeoAttribute, T> createItem;

        public NeoReadOnlyList(
            NeoClient client,
            NeoAttributeList node,
            Func<NeoClient, NeoAttribute, T> createItem)
        {
            this.client = client;
            this.node = node;
            this.createItem = createItem;
        }

        /// <summary>
        /// Subscribes to any change inside this list (adds, removes, item
        /// edits). Mirrors the generated field subscription shape: the handler
        /// receives the current value (this list) and the change source.
        /// Dispose the returned subscription to stop listening.
        /// </summary>
        public IDisposable OnChanged(Action<NeoReadOnlyList<T>, NeoChangeSource> handler)
        {
            return NeoCollectionSubscription.Watch(node, client, this, handler);
        }

        public IDisposable OnChanged(
            Action<NeoReadOnlyList<T>, NeoListChangedArgs, NeoChangeSource> handler)
        {
            return NeoCollectionSubscription.WatchList(node, client, this, handler);
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
    }

    public class NeoList<T> : NeoReadOnlyList<T>, IList<T>
    {
        private readonly Func<NeoAttributeListWritable> getWritableNode;
        private readonly Func<T, NeoValueWritePayload?> serializeItem;
        private readonly Action? beforeWrite;
        private readonly Func<bool>? isReadOnly;

        public NeoList(
            NeoClient client,
            NeoAttributeListWritable node,
            Func<NeoClient, NeoAttribute, T> createItem,
            Func<T, NeoValueWritePayload?> serializeItem)
            : this(client, node, () => node, createItem, serializeItem)
        {
        }

        public NeoList(
            NeoClient client,
            NeoAttributeList node,
            Func<NeoAttributeListWritable> getWritableNode,
            Func<NeoClient, NeoAttribute, T> createItem,
            Func<T, NeoValueWritePayload?> serializeItem,
            Action? beforeWrite = null,
            Func<bool>? isReadOnly = null)
            : base(client, node, createItem)
        {
            this.getWritableNode = getWritableNode ?? throw new ArgumentNullException(nameof(getWritableNode));
            this.serializeItem = serializeItem;
            this.beforeWrite = beforeWrite;
            this.isReadOnly = isReadOnly;
        }

        private NeoAttributeListWritable RequireWritableNode()
        {
            beforeWrite?.Invoke();
            var writableNode = getWritableNode();
            node = writableNode;
            return writableNode;
        }

        public new T this[int index]
        {
            get => base[index];
            set => RequireWritableNode().SetSerialized(index, serializeItem(value));
        }

        public bool IsReadOnly => isReadOnly?.Invoke() ?? false;

        public void Add(T item) => RequireWritableNode().AddSerialized(serializeItem(item));

        public void Clear()
        {
            RequireWritableNode().ClearSerialized();
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

        public void RemoveAt(int index) => RequireWritableNode().RemoveAt(index);
    }

    public class NeoReadOnlyDictionary<T> : IReadOnlyDictionary<string, T>
    {
        protected readonly NeoClient client;
        protected NeoAttributeDictionary node;
        protected readonly Func<NeoClient, NeoAttribute, T> createItem;

        public NeoReadOnlyDictionary(
            NeoClient client,
            NeoAttributeDictionary node,
            Func<NeoClient, NeoAttribute, T> createItem)
        {
            this.client = client;
            this.node = node;
            this.createItem = createItem;
        }

        /// <summary>
        /// Subscribes to any change inside this dictionary. Mirrors the
        /// generated field subscription shape: the handler receives the
        /// current value (this dictionary) and the change source. Dispose the
        /// returned subscription to stop listening.
        /// </summary>
        public IDisposable OnChanged(Action<NeoReadOnlyDictionary<T>, NeoChangeSource> handler)
        {
            return NeoCollectionSubscription.Watch(node, client, this, handler);
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
    }

    public class NeoDictionary<T> : NeoReadOnlyDictionary<T>, IDictionary<string, T>
    {
        private readonly Func<NeoAttributeDictionaryWritable> getWritableNode;
        private readonly Func<T, NeoValueWritePayload?> serializeItem;
        private readonly Action? beforeWrite;
        private readonly Func<bool>? isReadOnly;

        public NeoDictionary(
            NeoClient client,
            NeoAttributeDictionaryWritable node,
            Func<NeoClient, NeoAttribute, T> createItem,
            Func<T, NeoValueWritePayload?> serializeItem)
            : this(client, node, () => node, createItem, serializeItem)
        {
        }

        public NeoDictionary(
            NeoClient client,
            NeoAttributeDictionary node,
            Func<NeoAttributeDictionaryWritable> getWritableNode,
            Func<NeoClient, NeoAttribute, T> createItem,
            Func<T, NeoValueWritePayload?> serializeItem,
            Action? beforeWrite = null,
            Func<bool>? isReadOnly = null)
            : base(client, node, createItem)
        {
            this.getWritableNode = getWritableNode ?? throw new ArgumentNullException(nameof(getWritableNode));
            this.serializeItem = serializeItem;
            this.beforeWrite = beforeWrite;
            this.isReadOnly = isReadOnly;
        }

        private NeoAttributeDictionaryWritable RequireWritableNode()
        {
            beforeWrite?.Invoke();
            var writableNode = getWritableNode();
            node = writableNode;
            return writableNode;
        }

        public new T this[string key]
        {
            get => base[key];
            set => RequireWritableNode().SetSerialized(key, serializeItem(value));
        }

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

        public bool IsReadOnly => isReadOnly?.Invoke() ?? false;

        public void Add(string key, T value) =>
            RequireWritableNode().SetSerialized(key, serializeItem(value));

        public void Add(KeyValuePair<string, T> item) => Add(item.Key, item.Value);

        public void Clear()
        {
            var writableNode = RequireWritableNode();
            var keys = new List<string>(Keys);
            foreach (var key in keys)
            {
                writableNode.Remove(key);
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
            RequireWritableNode().Remove(key);
            return true;
        }

        public bool Remove(KeyValuePair<string, T> item)
        {
            if (!Contains(item)) return false;
            return Remove(item.Key);
        }
    }

    public class NeoReadOnlyLookupSet<T> : IReadOnlyCollection<T>
    {
        protected readonly NeoClient client;
        protected NeoAttributeLookup node;
        private readonly Func<NeoAttribute, T> createItem;

        public NeoReadOnlyLookupSet(
            NeoClient client,
            NeoAttributeLookup node,
            Func<NeoAttribute, T> createItem)
        {
            this.client = client;
            this.node = node;
            this.createItem = createItem;
        }

        /// <summary>
        /// Subscribes to any change to this lookup set's selection. Mirrors
        /// the generated field subscription shape: the handler receives the
        /// current value (this set) and the change source. Dispose the
        /// returned subscription to stop listening.
        /// </summary>
        public IDisposable OnChanged(Action<NeoReadOnlyLookupSet<T>, NeoChangeSource> handler)
        {
            return NeoCollectionSubscription.Watch(node, client, this, handler);
        }

        public int Count => node.Selected().Length;

        public IReadOnlyList<string> Ids => node.Selected();

        public bool Contains(string valueId)
        {
            foreach (var selectedId in node.Selected())
            {
                if (selectedId == valueId) return true;
            }
            return false;
        }

        public bool Contains(T item)
        {
            string? valueId = NeoGeneratedTypesSupport.ValueId(item);
            return valueId is not null && Contains(valueId);
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var child in node.GetSelected())
            {
                yield return createItem(child);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class NeoLookupSet<T> : NeoReadOnlyLookupSet<T>, ICollection<T>
    {
        private readonly Func<NeoAttributeLookupWritable> getWritableNode;
        private readonly Action? beforeWrite;
        private readonly Func<bool>? isReadOnly;

        public NeoLookupSet(
            NeoClient client,
            NeoAttributeLookupWritable node,
            Func<NeoAttribute, T> createItem)
            : this(client, node, () => node, createItem)
        {
        }

        public NeoLookupSet(
            NeoClient client,
            NeoAttributeLookup node,
            Func<NeoAttributeLookupWritable> getWritableNode,
            Func<NeoAttribute, T> createItem,
            Action? beforeWrite = null,
            Func<bool>? isReadOnly = null)
            : base(client, node, createItem)
        {
            this.getWritableNode = getWritableNode ?? throw new ArgumentNullException(nameof(getWritableNode));
            this.beforeWrite = beforeWrite;
            this.isReadOnly = isReadOnly;
        }

        private NeoAttributeLookupWritable RequireWritableNode()
        {
            beforeWrite?.Invoke();
            var writableNode = getWritableNode();
            node = writableNode;
            return writableNode;
        }

        public bool IsReadOnly => isReadOnly?.Invoke() ?? false;

        public void Add(T item)
        {
            string? valueId = NeoGeneratedTypesSupport.ValueId(item);
            if (valueId is null)
            {
                throw new InvalidOperationException(
                    "Lookup set item must be a generated Neo value reference.");
            }
            RequireWritableNode().Add(valueId);
        }

        public bool Add(string valueId) => RequireWritableNode().Add(valueId);

        public void Clear() => RequireWritableNode().Clear();

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array is null) throw new ArgumentNullException(nameof(array));
            foreach (var item in this)
            {
                array[arrayIndex++] = item;
            }
        }

        public bool Remove(T item)
        {
            string? valueId = NeoGeneratedTypesSupport.ValueId(item);
            return valueId is not null && RequireWritableNode().Remove(valueId);
        }

        public bool Remove(string valueId) => RequireWritableNode().Remove(valueId);
    }

    /// <summary>
    /// Read-only set of <see cref="NeoDialogueReference"/>s backing a multiselect
    /// DialogueLookup. Unlike <see cref="NeoReadOnlyLookupSet{T}"/> it is
    /// non-generic and resolves the stored <c>dialogueId</c>s directly (no
    /// collection <c>GetSelected()</c> walk). See spec §5.3.
    /// </summary>
    public class NeoReadOnlyDialogueReferenceSet : IReadOnlyCollection<NeoDialogueReference>
    {
        protected readonly NeoClient client;
        protected NeoAttributeDialogueLookup node;

        public NeoReadOnlyDialogueReferenceSet(NeoClient client, NeoAttributeDialogueLookup node)
        {
            this.client = client;
            this.node = node;
        }

        /// <summary>
        /// Subscribes to any change to this set's selection. Mirrors the
        /// generated field subscription shape; dispose to stop listening.
        /// </summary>
        public IDisposable OnChanged(Action<NeoReadOnlyDialogueReferenceSet, NeoChangeSource> handler)
        {
            return NeoCollectionSubscription.Watch(node, client, this, handler);
        }

        public int Count => node.Selected().Length;

        public IReadOnlyList<string> Ids => node.Selected();

        public bool Contains(string dialogueId)
        {
            foreach (var id in node.Selected())
            {
                if (id == dialogueId) return true;
            }
            return false;
        }

        public bool Contains(NeoDialogueReference item) =>
            item is not null && Contains(item.Id);

        public IEnumerator<NeoDialogueReference> GetEnumerator()
        {
            foreach (var id in node.Selected())
            {
                yield return new NeoDialogueReference(client, id);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class NeoDialogueReferenceSet
        : NeoReadOnlyDialogueReferenceSet, ICollection<NeoDialogueReference>
    {
        private readonly Func<NeoAttributeDialogueLookupWritable> getWritableNode;
        private readonly Action? beforeWrite;
        private readonly Func<bool>? isReadOnly;

        public NeoDialogueReferenceSet(
            NeoClient client,
            NeoAttributeDialogueLookupWritable node)
            : this(client, node, () => node)
        {
        }

        public NeoDialogueReferenceSet(
            NeoClient client,
            NeoAttributeDialogueLookup node,
            Func<NeoAttributeDialogueLookupWritable> getWritableNode,
            Action? beforeWrite = null,
            Func<bool>? isReadOnly = null)
            : base(client, node)
        {
            this.getWritableNode = getWritableNode ?? throw new ArgumentNullException(nameof(getWritableNode));
            this.beforeWrite = beforeWrite;
            this.isReadOnly = isReadOnly;
        }

        private NeoAttributeDialogueLookupWritable RequireWritableNode()
        {
            beforeWrite?.Invoke();
            var writableNode = getWritableNode();
            node = writableNode;
            return writableNode;
        }

        public bool IsReadOnly => isReadOnly?.Invoke() ?? false;

        public void Add(NeoDialogueReference item)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            // The writable node enforces the dialogueGroupId scope.
            RequireWritableNode().Add(item.Id);
        }

        public bool Add(string dialogueId) => RequireWritableNode().Add(dialogueId);

        public void Clear() => RequireWritableNode().Clear();

        public void CopyTo(NeoDialogueReference[] array, int arrayIndex)
        {
            if (array is null) throw new ArgumentNullException(nameof(array));
            foreach (var item in this)
            {
                array[arrayIndex++] = item;
            }
        }

        public bool Remove(NeoDialogueReference item) =>
            item is not null && RequireWritableNode().Remove(item.Id);

        public bool Remove(string dialogueId) => RequireWritableNode().Remove(dialogueId);
    }
}
