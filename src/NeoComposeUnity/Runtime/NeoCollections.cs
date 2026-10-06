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

        public NeoListChangeKind Kind
        {
            get;
        }
        public IReadOnlyList<string> RemovedValueIds
        {
            get;
        }
        public IReadOnlyList<string> AddedValueIds
        {
            get;
        }
        public IReadOnlyList<string> ReplacedValueIds
        {
            get;
        }
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
            NeoMember node,
            NeoClient client,
            TCollection collection,
            Action<TCollection, NeoChangeSource> handler)
        {
            if (handler is null)
                throw new ArgumentNullException(nameof(handler));
            void Handle(NeoMember changed) => handler(collection, client.CurrentChangeSource);
            node.OnChanged += Handle;
            return new NeoDisposableSubscription(() => node.OnChanged -= Handle);
        }

        public static IDisposable WatchList<T>(
            NeoMemberList node,
            NeoClient client,
            NeoReadOnlyList<T> collection,
            Action<NeoReadOnlyList<T>, NeoListChangedArgs, NeoChangeSource> handler)
        {
            if (handler is null)
                throw new ArgumentNullException(nameof(handler));
            void Handle(NeoMember changed) =>
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
        private NeoMemberList? nodeStore;
        protected readonly Func<NeoClient, NeoMember, T> createItem;
        /// <summary>The pending view whose List slot this list reads until it attaches.</summary>
        private readonly NeoGeneratedClassValue? detachedOwner;
        private readonly string? detachedKey;
        private readonly Func<object?, T>? readDetachedEntry;

        public NeoReadOnlyList(
            NeoClient client,
            NeoMemberList node,
            Func<NeoClient, NeoMember, T> createItem)
        {
            this.client = client;
            nodeStore = node;
            this.createItem = createItem;
        }

        /// <summary>
        /// A List member of a view over a pending NeoScript temporary: entries
        /// read from its slot, and anything else attaches the owner and reads
        /// its row.
        /// </summary>
        internal NeoReadOnlyList(
            NeoClient client,
            NeoGeneratedClassValue owner,
            string key,
            Func<object?, T> readEntry,
            Func<NeoClient, NeoMember, T> createItem)
        {
            this.client = client;
            this.createItem = createItem;
            detachedOwner = owner;
            detachedKey = key;
            readDetachedEntry = readEntry;
        }

        protected NeoMemberList node
        {
            get => nodeStore ??= detachedOwner!.WritableBackingNode.Get<NeoMemberListWritable>(detachedKey!);
            set => nodeStore = value;
        }

        private object?[]? DetachedEntries() =>
            nodeStore is null && detachedOwner!.TryReadDetached(detachedKey!, out object? entries)
                ? entries as object?[]
                : null;

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

        public T this[int index] => DetachedEntries() is { } entries
            ? readDetachedEntry!(entries[index])
            : createItem(client, node[index]);

        /// <summary>
        /// Resolves a List member by its stable value id. The underlying
        /// identity map is allocated lazily on first use.
        /// </summary>
        public T this[string valueId]
        {
            get
            {
                if (valueId is null)
                    throw new ArgumentNullException(nameof(valueId));
                if (!node.TryGetChildById(valueId, out NeoMember? child))
                {
                    throw new KeyNotFoundException(
                        $"Value '{valueId}' is not a member of List member '{node.member.id}'.");
                }
                return createItem(client, child);
            }
        }

        public bool TryGetById(
            string valueId,
            [MaybeNullWhen(false)] out T item)
        {
            if (valueId is null)
                throw new ArgumentNullException(nameof(valueId));
            if (node.TryGetChildById(valueId, out NeoMember? child))
            {
                item = createItem(client, child);
                return true;
            }
            item = default!;
            return false;
        }

        public bool ContainsId(string valueId)
        {
            if (valueId is null)
                throw new ArgumentNullException(nameof(valueId));
            return node.ContainsValueId(valueId);
        }

        public int Count => DetachedEntries() is { } entries ? entries.Length : node.Count;

        public Enumerator GetEnumerator()
        {
            object?[]? entries = DetachedEntries();
            return entries is null ? new(this, node.ChildEnumerator()) : new(this, entries);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Enumerates the list's items. A struct, so a <c>foreach</c> over the
        /// list allocates nothing.
        /// </summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly NeoReadOnlyList<T> list;
            private readonly object?[]? entries;
            private List<NeoMember>.Enumerator children;
            private int index;
            private T current;

            internal Enumerator(NeoReadOnlyList<T> list, object?[] entries)
            {
                this.list = list;
                this.entries = entries;
                children = default;
                index = 0;
                current = default!;
            }

            internal Enumerator(NeoReadOnlyList<T> list, List<NeoMember>.Enumerator children)
            {
                this.list = list;
                entries = null;
                this.children = children;
                index = 0;
                current = default!;
            }

            public T Current => current;

            object? IEnumerator.Current => current;

            public bool MoveNext()
            {
                if (entries is not null)
                {
                    if (index == entries.Length)
                        return false;
                    current = list.readDetachedEntry!(entries[index++]);
                    return true;
                }
                if (!children.MoveNext())
                    return false;
                current = list.createItem(list.client, children.Current);
                return true;
            }

            void IEnumerator.Reset() => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }
    }

    public class NeoList<T> : NeoReadOnlyList<T>, IList<T>
    {
        private readonly Func<NeoMemberListWritable> getWritableNode;
        private readonly Func<T, NeoValueWritePayload?> serializeItem;
        private readonly Action? beforeWrite;
        private readonly Func<bool>? isReadOnly;

        public NeoList(
            NeoClient client,
            NeoMemberListWritable node,
            Func<NeoClient, NeoMember, T> createItem,
            Func<T, NeoValueWritePayload?> serializeItem)
            : this(client, node, () => node, createItem, serializeItem)
        {
        }

        public NeoList(
            NeoClient client,
            NeoMemberList node,
            Func<NeoMemberListWritable> getWritableNode,
            Func<NeoClient, NeoMember, T> createItem,
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

        internal NeoList(
            NeoClient client,
            NeoGeneratedClassValue owner,
            string key,
            Func<object?, T> readEntry,
            Func<NeoClient, NeoMember, T> createItem,
            Func<T, NeoValueWritePayload?> serializeItem,
            Action? beforeWrite,
            Func<bool>? isReadOnly)
            : base(client, owner, key, readEntry, createItem)
        {
            getWritableNode = () => owner.WritableBackingNode.GetOrCreateCollection<NeoMemberListWritable>(key);
            this.serializeItem = serializeItem;
            this.beforeWrite = beforeWrite;
            this.isReadOnly = isReadOnly;
        }

        private NeoMemberListWritable RequireWritableNode()
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

        /// <summary>
        /// Getter-only stable-id overload. Replacement remains positional so
        /// assigning through a String key cannot be confused with changing an
        /// entry's identity.
        /// </summary>
        public new T this[string valueId] => base[valueId];

        public bool IsReadOnly => isReadOnly?.Invoke() ?? false;

        public void Add(T item) => RequireWritableNode().AddSerialized(serializeItem(item));

        public void Clear()
        {
            RequireWritableNode().ClearSerialized();
        }

        public bool Contains(T item) => IndexOf(item) >= 0;

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array is null)
                throw new ArgumentNullException(nameof(array));
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
                if (comparer.Equals(this[i], item))
                    return i;
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
            if (index < 0)
                return false;
            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index) => RequireWritableNode().RemoveAt(index);
    }

    public class NeoReadOnlyDictionary<T>
        : IReadOnlyDictionary<string, T>,
          INeoGeneratedConstructorDictionary
    {
        protected readonly NeoClient client;
        protected NeoMemberDictionary node;
        protected readonly Func<NeoClient, NeoMember, T> createItem;

        public NeoReadOnlyDictionary(
            NeoClient client,
            NeoMemberDictionary node,
            Func<NeoClient, NeoMember, T> createItem)
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

        /// <summary>
        /// Attaches a change subscription for an outer collection that
        /// delegates its storage to this one (the enum-keyed two-arity
        /// wrappers), so the outer surface can offer the same
        /// <c>OnChanged</c> shape without reaching into the node.
        /// </summary>
        internal IDisposable WatchNode<TCollection>(
            TCollection collection,
            Action<TCollection, NeoChangeSource> handler)
        {
            return NeoCollectionSubscription.Watch(node, client, collection, handler);
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

        public Enumerator GetEnumerator() => new(this, node.ChildEnumerator());

        IEnumerator<KeyValuePair<string, T>> IEnumerable<KeyValuePair<string, T>>.GetEnumerator() =>
            GetEnumerator();

        public bool TryGetValue(string key, out T value)
        {
            if (node.TryGet<NeoMember>(key, out NeoMember? child))
            {
                value = createItem(client, child);
                return true;
            }
            value = default!;
            return false;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        IEnumerable<NeoGeneratedConstructorDictionaryEntry>
            INeoGeneratedConstructorDictionary
                .EnumerateGeneratedConstructorEntries()
        {
            foreach (KeyValuePair<string, T> pair in this)
            {
                yield return new NeoGeneratedConstructorDictionaryEntry(
                    pair.Key,
                    pair.Value);
            }
        }

        /// <summary>
        /// Enumerates the dictionary's entries. A struct, so a <c>foreach</c>
        /// over the dictionary allocates nothing.
        /// </summary>
        public struct Enumerator : IEnumerator<KeyValuePair<string, T>>
        {
            private readonly NeoReadOnlyDictionary<T> dictionary;
            private Dictionary<string, NeoMember>.Enumerator children;
            private KeyValuePair<string, T> current;

            internal Enumerator(
                NeoReadOnlyDictionary<T> dictionary,
                Dictionary<string, NeoMember>.Enumerator children)
            {
                this.dictionary = dictionary;
                this.children = children;
                current = default;
            }

            public KeyValuePair<string, T> Current => current;

            object IEnumerator.Current => current;

            public bool MoveNext()
            {
                if (!children.MoveNext())
                    return false;
                KeyValuePair<string, NeoMember> child = children.Current;
                current = new KeyValuePair<string, T>(
                    child.Key,
                    dictionary.createItem(dictionary.client, child.Value));
                return true;
            }

            void IEnumerator.Reset() => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }
    }

    public class NeoDictionary<T> : NeoReadOnlyDictionary<T>, IDictionary<string, T>
    {
        private readonly Func<NeoMemberDictionaryWritable> getWritableNode;
        private readonly Func<T, NeoValueWritePayload?> serializeItem;
        private readonly Action? beforeWrite;
        private readonly Func<bool>? isReadOnly;

        public NeoDictionary(
            NeoClient client,
            NeoMemberDictionaryWritable node,
            Func<NeoClient, NeoMember, T> createItem,
            Func<T, NeoValueWritePayload?> serializeItem)
            : this(client, node, () => node, createItem, serializeItem)
        {
        }

        public NeoDictionary(
            NeoClient client,
            NeoMemberDictionary node,
            Func<NeoMemberDictionaryWritable> getWritableNode,
            Func<NeoClient, NeoMember, T> createItem,
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

        private NeoMemberDictionaryWritable RequireWritableNode()
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
                foreach (var key in base.Keys)
                    keys.Add(key);
                return keys;
            }
        }

        public new ICollection<T> Values
        {
            get
            {
                var values = new List<T>();
                foreach (var value in base.Values)
                    values.Add(value);
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
            if (!TryGetValue(item.Key, out T existing))
                return false;
            return EqualityComparer<T>.Default.Equals(existing, item.Value);
        }

        public void CopyTo(KeyValuePair<string, T>[] array, int arrayIndex)
        {
            if (array is null)
                throw new ArgumentNullException(nameof(array));
            foreach (var kvp in this)
            {
                array[arrayIndex++] = kvp;
            }
        }

        public bool Remove(string key)
        {
            if (!ContainsKey(key))
                return false;
            RequireWritableNode().Remove(key);
            return true;
        }

        public bool Remove(KeyValuePair<string, T> item)
        {
            if (!Contains(item))
                return false;
            return Remove(item.Key);
        }
    }

    /// <summary>
    /// Read-only view over an enum-keyed Dictionary member
    /// (specs/dictionary-key-classes.md §9). Same-name two-arity sibling of
    /// <see cref="NeoReadOnlyDictionary{T}"/> (the
    /// <c>System.Collections.Generic</c> arity precedent):
    /// <typeparamref name="TKey"/> is a generated enum and every key crosses
    /// the boundary through the codec supplied at construction
    /// (<c>fromOptionId</c> / <c>toOptionId</c>). Storage is NOT forked — all
    /// reads delegate to a single-arity <see cref="NeoReadOnlyDictionary{T}"/>
    /// over the same node, whose keys are the option-id strings on the wire.
    /// <see cref="Keys"/> and enumeration materialize keys via
    /// <c>fromOptionId</c>, so stale option ids (option deleted with "keep
    /// orphaned") become undeclared enum values exactly like dangling Enum
    /// values do.
    /// Enumeration order is the underlying record order (the web UI's
    /// enum-option-order sort is display-only).
    /// </summary>
    public class NeoReadOnlyDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
        where TKey : struct, System.Enum
    {
        protected readonly NeoReadOnlyDictionary<TValue> entries;
        protected readonly Func<string, TKey> fromOptionId;
        protected readonly Func<TKey, string> toOptionId;

        public NeoReadOnlyDictionary(
            NeoClient client,
            NeoMemberDictionary node,
            Func<NeoClient, NeoMember, TValue> createItem,
            Func<string, TKey> fromOptionId,
            Func<TKey, string> toOptionId)
            : this(
                new NeoReadOnlyDictionary<TValue>(client, node, createItem),
                fromOptionId,
                toOptionId)
        {
        }

        protected NeoReadOnlyDictionary(
            NeoReadOnlyDictionary<TValue> entries,
            Func<string, TKey> fromOptionId,
            Func<TKey, string> toOptionId)
        {
            this.entries = entries ?? throw new ArgumentNullException(nameof(entries));
            this.fromOptionId = fromOptionId ?? throw new ArgumentNullException(nameof(fromOptionId));
            this.toOptionId = toOptionId ?? throw new ArgumentNullException(nameof(toOptionId));
        }

        /// <summary>
        /// Subscribes to any change inside this dictionary. Mirrors the
        /// generated field subscription shape: the handler receives the
        /// current value (this dictionary) and the change source. Dispose the
        /// returned subscription to stop listening.
        /// </summary>
        public IDisposable OnChanged(
            Action<NeoReadOnlyDictionary<TKey, TValue>, NeoChangeSource> handler)
        {
            return entries.WatchNode(this, handler);
        }

        public TValue this[TKey key] => entries[toOptionId(key)];

        public IEnumerable<TKey> Keys
        {
            get
            {
                foreach (var key in entries.Keys)
                {
                    yield return fromOptionId(key);
                }
            }
        }

        public IEnumerable<TValue> Values => entries.Values;

        public int Count => entries.Count;

        public bool ContainsKey(TKey key) => entries.ContainsKey(toOptionId(key));

        public bool TryGetValue(TKey key, out TValue value) =>
            entries.TryGetValue(toOptionId(key), out value);

        public Enumerator GetEnumerator() => new(entries.GetEnumerator(), fromOptionId);

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() =>
            GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Enumerates the dictionary's entries. A struct, so a <c>foreach</c>
        /// over the dictionary allocates nothing.
        /// </summary>
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private NeoReadOnlyDictionary<TValue>.Enumerator entries;
            private readonly Func<string, TKey> fromOptionId;
            private KeyValuePair<TKey, TValue> current;

            internal Enumerator(
                NeoReadOnlyDictionary<TValue>.Enumerator entries,
                Func<string, TKey> fromOptionId)
            {
                this.entries = entries;
                this.fromOptionId = fromOptionId;
                current = default;
            }

            public KeyValuePair<TKey, TValue> Current => current;

            object IEnumerator.Current => current;

            public bool MoveNext()
            {
                if (!entries.MoveNext())
                    return false;
                KeyValuePair<string, TValue> entry = entries.Current;
                current = new KeyValuePair<TKey, TValue>(fromOptionId(entry.Key), entry.Value);
                return true;
            }

            void IEnumerator.Reset() => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }
    }

    /// <summary>
    /// Writable enum-keyed Dictionary wrapper — the two-arity sibling of
    /// <see cref="NeoDictionary{T}"/>. All mutations delegate key-wise
    /// (via the key codec) to a single-arity <see cref="NeoDictionary{T}"/>
    /// over the same node; see <see cref="NeoReadOnlyDictionary{TKey, TValue}"/>
    /// for the delegation and stale-key semantics.
    /// </summary>
    public class NeoDictionary<TKey, TValue>
        : NeoReadOnlyDictionary<TKey, TValue>, IDictionary<TKey, TValue>
        where TKey : struct, System.Enum
    {
        private readonly NeoDictionary<TValue> writableEntries;

        public NeoDictionary(
            NeoClient client,
            NeoMemberDictionaryWritable node,
            Func<NeoClient, NeoMember, TValue> createItem,
            Func<TValue, NeoValueWritePayload?> serializeItem,
            Func<string, TKey> fromOptionId,
            Func<TKey, string> toOptionId)
            : this(
                new NeoDictionary<TValue>(client, node, createItem, serializeItem),
                fromOptionId,
                toOptionId)
        {
        }

        public NeoDictionary(
            NeoClient client,
            NeoMemberDictionary node,
            Func<NeoMemberDictionaryWritable> getWritableNode,
            Func<NeoClient, NeoMember, TValue> createItem,
            Func<TValue, NeoValueWritePayload?> serializeItem,
            Func<string, TKey> fromOptionId,
            Func<TKey, string> toOptionId,
            Action? beforeWrite = null,
            Func<bool>? isReadOnly = null)
            : this(
                new NeoDictionary<TValue>(
                    client,
                    node,
                    getWritableNode,
                    createItem,
                    serializeItem,
                    beforeWrite,
                    isReadOnly),
                fromOptionId,
                toOptionId)
        {
        }

        private NeoDictionary(
            NeoDictionary<TValue> entries,
            Func<string, TKey> fromOptionId,
            Func<TKey, string> toOptionId)
            : base(entries, fromOptionId, toOptionId)
        {
            writableEntries = entries;
        }

        public new TValue this[TKey key]
        {
            get => base[key];
            set => writableEntries[toOptionId(key)] = value;
        }

        public new ICollection<TKey> Keys
        {
            get
            {
                var keys = new List<TKey>();
                foreach (var key in base.Keys)
                    keys.Add(key);
                return keys;
            }
        }

        public new ICollection<TValue> Values => writableEntries.Values;

        public bool IsReadOnly => writableEntries.IsReadOnly;

        public void Add(TKey key, TValue value) =>
            writableEntries.Add(toOptionId(key), value);

        public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

        public void Clear() => writableEntries.Clear();

        public bool Contains(KeyValuePair<TKey, TValue> item) =>
            writableEntries.Contains(
                new KeyValuePair<string, TValue>(toOptionId(item.Key), item.Value));

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            if (array is null)
                throw new ArgumentNullException(nameof(array));
            foreach (var kvp in this)
            {
                array[arrayIndex++] = kvp;
            }
        }

        public bool Remove(TKey key) => writableEntries.Remove(toOptionId(key));

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (!Contains(item))
                return false;
            return Remove(item.Key);
        }
    }

    public class NeoReadOnlyLookupSet<T> : IReadOnlyCollection<T>
    {
        protected readonly NeoClient client;
        protected NeoMemberLookup node;
        private readonly Func<NeoMember, T> createItem;

        public NeoReadOnlyLookupSet(
            NeoClient client,
            NeoMemberLookup node,
            Func<NeoMember, T> createItem)
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
                if (selectedId == valueId)
                    return true;
            }
            return false;
        }

        public bool Contains(T item)
        {
            string? valueId = NeoGeneratedTypesSupport.ValueId(item);
            return valueId is not null && Contains(valueId);
        }

        public Enumerator GetEnumerator() => new(this, node.Selected());

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Enumerates the selected items, resolving each as it is reached. A
        /// struct, so a <c>foreach</c> over the set allocates nothing.
        /// </summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly NeoReadOnlyLookupSet<T> set;
            private readonly NeoMemberLookup node;
            private readonly string[] ids;
            private readonly Member? entry;
            private readonly NeoValueOwnership targetOwnership;
            private int index;
            private T current;

            internal Enumerator(NeoReadOnlyLookupSet<T> set, string[] ids)
            {
                this.set = set;
                node = set.node;
                this.ids = ids;
                targetOwnership = default;
                entry = ids.Length == 0 ? null : node.ResolveSelectionScope(out targetOwnership);
                index = 0;
                current = default!;
            }

            public T Current => current;

            object? IEnumerator.Current => current;

            public bool MoveNext()
            {
                if (index == ids.Length)
                    return false;
                current = set.createItem(node.ResolveSelectedAt(index, ids[index], entry!, targetOwnership));
                index++;
                return true;
            }

            void IEnumerator.Reset() => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }
    }

    public class NeoLookupSet<T> : NeoReadOnlyLookupSet<T>, ICollection<T>
    {
        private readonly Func<NeoMemberLookupWritable> getWritableNode;
        private readonly Action? beforeWrite;
        private readonly Func<bool>? isReadOnly;

        public NeoLookupSet(
            NeoClient client,
            NeoMemberLookupWritable node,
            Func<NeoMember, T> createItem)
            : this(client, node, () => node, createItem)
        {
        }

        public NeoLookupSet(
            NeoClient client,
            NeoMemberLookup node,
            Func<NeoMemberLookupWritable> getWritableNode,
            Func<NeoMember, T> createItem,
            Action? beforeWrite = null,
            Func<bool>? isReadOnly = null)
            : base(client, node, createItem)
        {
            this.getWritableNode = getWritableNode ?? throw new ArgumentNullException(nameof(getWritableNode));
            this.beforeWrite = beforeWrite;
            this.isReadOnly = isReadOnly;
        }

        private NeoMemberLookupWritable RequireWritableNode()
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
            if (array is null)
                throw new ArgumentNullException(nameof(array));
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
    /// Read-only set of <see cref="NeoDialogueReference"/>s backing a multi-select
    /// DialogueLookup. Unlike <see cref="NeoReadOnlyLookupSet{T}"/> it is
    /// non-generic and resolves the stored <c>dialogueId</c>s directly (no
    /// collection <c>GetSelected()</c> walk). See spec §5.3.
    /// </summary>
    public class NeoReadOnlyDialogueReferenceSet : IReadOnlyCollection<NeoDialogueReference>
    {
        protected readonly NeoClient client;
        protected NeoMemberDialogueLookup node;

        public NeoReadOnlyDialogueReferenceSet(NeoClient client, NeoMemberDialogueLookup node)
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
                if (id == dialogueId)
                    return true;
            }
            return false;
        }

        public bool Contains(NeoDialogueReference item) =>
            item is not null && Contains(item.Id);

        public Enumerator GetEnumerator() => new(client, node.Selected());

        IEnumerator<NeoDialogueReference> IEnumerable<NeoDialogueReference>.GetEnumerator() =>
            GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Enumerates the selected dialogues. A struct, so a <c>foreach</c>
        /// over the set allocates only the references it yields.
        /// </summary>
        public struct Enumerator : IEnumerator<NeoDialogueReference>
        {
            private readonly NeoClient client;
            private readonly string[] ids;
            private int index;
            private NeoDialogueReference? current;

            internal Enumerator(NeoClient client, string[] ids)
            {
                this.client = client;
                this.ids = ids;
                index = 0;
                current = null;
            }

            public NeoDialogueReference Current => current!;

            object IEnumerator.Current => current!;

            public bool MoveNext()
            {
                if (index == ids.Length)
                    return false;
                current = new NeoDialogueReference(client, ids[index++]);
                return true;
            }

            void IEnumerator.Reset() => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }
    }

    public class NeoDialogueReferenceSet
        : NeoReadOnlyDialogueReferenceSet, ICollection<NeoDialogueReference>
    {
        private readonly Func<NeoMemberDialogueLookupWritable> getWritableNode;
        private readonly Action? beforeWrite;
        private readonly Func<bool>? isReadOnly;

        public NeoDialogueReferenceSet(
            NeoClient client,
            NeoMemberDialogueLookupWritable node)
            : this(client, node, () => node)
        {
        }

        public NeoDialogueReferenceSet(
            NeoClient client,
            NeoMemberDialogueLookup node,
            Func<NeoMemberDialogueLookupWritable> getWritableNode,
            Action? beforeWrite = null,
            Func<bool>? isReadOnly = null)
            : base(client, node)
        {
            this.getWritableNode = getWritableNode ?? throw new ArgumentNullException(nameof(getWritableNode));
            this.beforeWrite = beforeWrite;
            this.isReadOnly = isReadOnly;
        }

        private NeoMemberDialogueLookupWritable RequireWritableNode()
        {
            beforeWrite?.Invoke();
            var writableNode = getWritableNode();
            node = writableNode;
            return writableNode;
        }

        public bool IsReadOnly => isReadOnly?.Invoke() ?? false;

        public void Add(NeoDialogueReference item)
        {
            if (item is null)
                throw new ArgumentNullException(nameof(item));
            // The writable node enforces the dialogueGroupId scope.
            RequireWritableNode().Add(item.Id);
        }

        public bool Add(string dialogueId) => RequireWritableNode().Add(dialogueId);

        public void Clear() => RequireWritableNode().Clear();

        public void CopyTo(NeoDialogueReference[] array, int arrayIndex)
        {
            if (array is null)
                throw new ArgumentNullException(nameof(array));
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
