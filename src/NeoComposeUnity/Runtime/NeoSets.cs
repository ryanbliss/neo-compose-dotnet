// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// The read side every P108 set shares: owned Sets, multi-select Enums,
    /// and multi-select Lookups. Unity's .NET profile has no
    /// <c>IReadOnlySet&lt;T&gt;</c>, so generated interface members typed
    /// <c>Set&lt;T&gt;</c> return this.
    /// </summary>
    public interface INeoReadOnlySet<T> : IReadOnlyCollection<T>
    {
        bool Contains(T item);

        /// <summary>
        /// P112 §8. The writable view of this set, when its store takes
        /// writes and its member isn't readonly.
        /// </summary>
        bool TryWritable(out ICollection<T> writable);
    }

    /// <summary>
    /// What <see cref="NeoSetAlgebra"/> needs from a set: entry keys, and the
    /// key an item would hold (null when it can match no entry).
    /// </summary>
    internal interface INeoKeyedSet<T>
    {
        int Count
        {
            get;
        }

        string? KeyOf(T item);

        bool HoldsKey(string key);

        List<string> EntryKeys();

        bool Add(T item);

        bool Remove(T item);

        /// <summary>Removes the entries holding <paramref name="keys"/> with one write.</summary>
        void RemoveKeys(IEnumerable<string> keys);
    }

    /// <summary>
    /// The <see cref="ISet{T}"/> operations, written once over entry keys so
    /// owned and enum sets agree on equality. Each snapshots
    /// <c>other</c> before writing, so <c>set.UnionWith(set)</c> is safe.
    /// </summary>
    internal static class NeoSetAlgebra
    {
        private readonly struct Snapshot<T>
        {
            internal readonly List<T> Items;
            internal readonly HashSet<string> Keys;
            // Items no entry can equal, e.g. new Class values.
            internal readonly int Unkeyed;

            internal Snapshot(INeoKeyedSet<T> set, IEnumerable<T> other)
            {
                if (other is null)
                    throw new ArgumentNullException(nameof(other));
                Items = new List<T>(other);
                Keys = new HashSet<string>(StringComparer.Ordinal);
                Unkeyed = 0;
                foreach (T item in Items)
                {
                    if (set.KeyOf(item) is { } key)
                        Keys.Add(key);
                    else
                        Unkeyed++;
                }
            }

            internal int DistinctCount => Keys.Count + Unkeyed;
        }

        internal static void UnionWith<T>(INeoKeyedSet<T> set, IEnumerable<T> other)
        {
            foreach (T item in new Snapshot<T>(set, other).Items)
                set.Add(item);
        }

        internal static void ExceptWith<T>(INeoKeyedSet<T> set, IEnumerable<T> other)
        {
            var snapshot = new Snapshot<T>(set, other);
            if (snapshot.Keys.Count > 0)
                set.RemoveKeys(snapshot.Keys);
        }

        internal static void IntersectWith<T>(INeoKeyedSet<T> set, IEnumerable<T> other)
        {
            var snapshot = new Snapshot<T>(set, other);
            var removed = new List<string>();
            foreach (string key in set.EntryKeys())
            {
                if (!snapshot.Keys.Contains(key))
                    removed.Add(key);
            }
            if (removed.Count > 0)
                set.RemoveKeys(removed);
        }

        internal static void SymmetricExceptWith<T>(INeoKeyedSet<T> set, IEnumerable<T> other)
        {
            var snapshot = new Snapshot<T>(set, other);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (T item in snapshot.Items)
            {
                string? key = set.KeyOf(item);
                if (key is null)
                {
                    set.Add(item);
                    // Adding the first entry can resolve an open generic slot.
                    if (set.KeyOf(item) is { } addedKey)
                        seen.Add(addedKey);
                    continue;
                }
                if (!seen.Add(key))
                    continue;
                if (set.HoldsKey(key))
                    set.Remove(item);
                else
                    set.Add(item);
            }
        }

        internal static bool IsSubsetOf<T>(INeoKeyedSet<T> set, IEnumerable<T> other) =>
            IsSubsetOf(set, new Snapshot<T>(set, other));

        internal static bool IsProperSubsetOf<T>(INeoKeyedSet<T> set, IEnumerable<T> other)
        {
            var snapshot = new Snapshot<T>(set, other);
            return snapshot.DistinctCount > set.Count && IsSubsetOf(set, snapshot);
        }

        internal static bool IsSupersetOf<T>(INeoKeyedSet<T> set, IEnumerable<T> other) =>
            IsSupersetOf(set, new Snapshot<T>(set, other));

        internal static bool IsProperSupersetOf<T>(INeoKeyedSet<T> set, IEnumerable<T> other)
        {
            var snapshot = new Snapshot<T>(set, other);
            return set.Count > snapshot.DistinctCount && IsSupersetOf(set, snapshot);
        }

        internal static bool Overlaps<T>(INeoKeyedSet<T> set, IEnumerable<T> other)
        {
            foreach (string key in new Snapshot<T>(set, other).Keys)
            {
                if (set.HoldsKey(key))
                    return true;
            }
            return false;
        }

        internal static bool SetEquals<T>(INeoKeyedSet<T> set, IEnumerable<T> other)
        {
            var snapshot = new Snapshot<T>(set, other);
            return snapshot.DistinctCount == set.Count && IsSupersetOf(set, snapshot);
        }

        private static bool IsSubsetOf<T>(INeoKeyedSet<T> set, Snapshot<T> snapshot)
        {
            foreach (string key in set.EntryKeys())
            {
                if (!snapshot.Keys.Contains(key))
                    return false;
            }
            return true;
        }

        private static bool IsSupersetOf<T>(INeoKeyedSet<T> set, Snapshot<T> snapshot)
        {
            if (snapshot.Unkeyed > 0)
                return false;
            foreach (string key in snapshot.Keys)
            {
                if (!set.HoldsKey(key))
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// A P108 Set member: an ordered List whose entries are unique by their
    /// §3 key. Enumeration follows stored order, and indexed wrappers reuse
    /// the List's index accessors.
    /// </summary>
    public class NeoReadOnlySet<T> : NeoReadOnlyList<T>, INeoReadOnlySet<T>
    {
        private protected readonly Func<T, NeoValueWritePayload?> serializeItem;

        public NeoReadOnlySet(
            NeoClient client,
            NeoMemberList node,
            Func<NeoClient, NeoMember, T> createItem,
            Func<T, NeoValueWritePayload?> serializeItem)
            : base(client, node, createItem)
        {
            this.serializeItem = serializeItem ?? throw new ArgumentNullException(nameof(serializeItem));
        }

        /// <summary>
        /// Subscribes to any change inside this set (adds, removes, entry
        /// edits). The handler receives this set and the change source.
        /// Dispose the returned subscription to stop listening.
        /// </summary>
        public IDisposable OnChanged(Action<NeoReadOnlySet<T>, NeoChangeSource> handler)
        {
            return NeoCollectionSubscription.Watch(node, client, this, handler);
        }

        public bool Contains(T item) => KeyOf(serializeItem(item)) is { } key && node.FindSetEntry(key) is not null;

        /// <summary>
        /// P112 §8. The writable view of this set, when its store takes writes and its member isn't readonly.
        /// </summary>
        public bool TryWritable(out ICollection<T> writable)
        {
            if (this is NeoSet<T> match && !match.IsReadOnly)
            {
                writable = match;
                return true;
            }

            writable = null!;
            return false;
        }

        /// <summary>
        /// The key an entry written from <paramref name="payload"/> would
        /// hold: the referenced value id for a Class entry, or null when it
        /// can equal no entry (null, or a new Class value).
        /// </summary>
        private protected string? KeyOf(NeoValueWritePayload? payload)
        {
            if (payload is { isValueReference: true })
                return payload.valueId;
            // An empty generic Set has no stamp, so its entry member is
            // still the unbound slot; nothing can match an empty Set anyway.
            if (node.Count == 0 && node.EntryMember is GenericMember)
                return null;
            if (node.EntryMember is ClassMember)
                return null;
            // P127 §9.5: every null is one key, so a Set holds at most one null.
            if (payload is null || payload.isNull)
                return NeoSetEntryKey.NullEntryKey;
            return node.SetEntryKeyOf(payload);
        }
    }

    /// <summary>
    /// Writable <see cref="NeoReadOnlySet{T}"/>. <see cref="Add"/> returns
    /// false and writes nothing when an equal entry exists.
    /// </summary>
    public class NeoSet<T> : NeoReadOnlySet<T>, ISet<T>, INeoKeyedSet<T>
    {
        private readonly Func<NeoMemberListWritable> getWritableNode;
        private readonly Action? beforeWrite;
        private readonly Func<bool>? isReadOnly;

        public NeoSet(
            NeoClient client,
            NeoMemberListWritable node,
            Func<NeoClient, NeoMember, T> createItem,
            Func<T, NeoValueWritePayload?> serializeItem)
            : this(client, node, () => node, createItem, serializeItem)
        {
        }

        public NeoSet(
            NeoClient client,
            NeoMemberList node,
            Func<NeoMemberListWritable> getWritableNode,
            Func<NeoClient, NeoMember, T> createItem,
            Func<T, NeoValueWritePayload?> serializeItem,
            Action? beforeWrite = null,
            Func<bool>? isReadOnly = null)
            : base(client, node, createItem, serializeItem)
        {
            this.getWritableNode = getWritableNode ?? throw new ArgumentNullException(nameof(getWritableNode));
            this.beforeWrite = beforeWrite;
            this.isReadOnly = isReadOnly;
        }

        private NeoMemberListWritable WritableNode()
        {
            var writableNode = getWritableNode();
            node = writableNode;
            return writableNode;
        }

        public bool IsReadOnly => isReadOnly?.Invoke() ?? false;

        /// <summary>
        /// Appends <paramref name="item"/> unless an equal entry exists.
        /// Returns false, writing nothing, when one does.
        /// </summary>
        public bool Add(T item)
        {
            beforeWrite?.Invoke();
            NeoValueWritePayload? payload = serializeItem(item);
            if (KeyOf(payload) is { } key && node.FindSetEntry(key) is not null)
                return false;
            WritableNode().AddSerialized(payload);
            return true;
        }

        void ICollection<T>.Add(T item) => Add(item);

        public bool Remove(T item)
        {
            beforeWrite?.Invoke();
            if (KeyOf(serializeItem(item)) is not { } key)
                return false;
            if (node.FindSetEntry(key) is not { } valueId)
                return false;
            WritableNode().RemoveById(valueId);
            return true;
        }

        public void Clear()
        {
            beforeWrite?.Invoke();
            WritableNode().ClearSerialized();
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array is null)
                throw new ArgumentNullException(nameof(array));
            foreach (T item in this)
                array[arrayIndex++] = item;
        }

        public void UnionWith(IEnumerable<T> other) => NeoSetAlgebra.UnionWith(this, other);

        public void ExceptWith(IEnumerable<T> other) => NeoSetAlgebra.ExceptWith(this, other);

        public void IntersectWith(IEnumerable<T> other) => NeoSetAlgebra.IntersectWith(this, other);

        public void SymmetricExceptWith(IEnumerable<T> other) => NeoSetAlgebra.SymmetricExceptWith(this, other);

        public bool IsSubsetOf(IEnumerable<T> other) => NeoSetAlgebra.IsSubsetOf(this, other);

        public bool IsProperSubsetOf(IEnumerable<T> other) => NeoSetAlgebra.IsProperSubsetOf(this, other);

        public bool IsSupersetOf(IEnumerable<T> other) => NeoSetAlgebra.IsSupersetOf(this, other);

        public bool IsProperSupersetOf(IEnumerable<T> other) => NeoSetAlgebra.IsProperSupersetOf(this, other);

        public bool Overlaps(IEnumerable<T> other) => NeoSetAlgebra.Overlaps(this, other);

        public bool SetEquals(IEnumerable<T> other) => NeoSetAlgebra.SetEquals(this, other);

        string? INeoKeyedSet<T>.KeyOf(T item) => KeyOf(serializeItem(item));

        bool INeoKeyedSet<T>.HoldsKey(string key) => node.FindSetEntry(key) is not null;

        List<string> INeoKeyedSet<T>.EntryKeys()
        {
            var keys = new List<string>(node.Count);
            foreach (NeoMember child in node)
                keys.Add(node.SetEntryKeyOfEntry(child));
            return keys;
        }

        void INeoKeyedSet<T>.RemoveKeys(IEnumerable<string> keys)
        {
            beforeWrite?.Invoke();
            var valueIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                if (node.FindSetEntry(key) is { } valueId)
                    valueIds.Add(valueId);
            }
            if (valueIds.Count > 0)
                WritableNode().RemoveByIds(valueIds);
        }
    }

    /// <summary>
    /// A NeoScript getter's or native argument's Set, read once. Entries are
    /// already unique; generated values compare by value id, the way stored
    /// Class entries key.
    /// </summary>
    internal sealed class NeoScriptSet<T> : INeoReadOnlySet<T>
    {
        private readonly T[] entries;
        private HashSet<T>? lookup;

        internal NeoScriptSet(T[] entries)
        {
            this.entries = entries;
        }

        public int Count => entries.Length;

        public bool TryWritable(out ICollection<T> writable)
        {
            writable = null!;
            return false;
        }

        public bool Contains(T item) =>
            (lookup ??= new HashSet<T>(entries, IdentityComparer.Instance)).Contains(item);

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)entries).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => entries.GetEnumerator();

        private sealed class IdentityComparer : IEqualityComparer<T>
        {
            internal static readonly IdentityComparer Instance = new();

            public bool Equals(T x, T y)
            {
                if (x is INeoValueReference left && y is INeoValueReference right)
                    return left.valueId is not null && left.valueId == right.valueId;
                return EqualityComparer<T>.Default.Equals(x, y);
            }

            public int GetHashCode(T value)
            {
                if (value is INeoValueReference reference)
                    return reference.valueId?.GetHashCode() ?? 0;
                return value is null ? 0 : EqualityComparer<T>.Default.GetHashCode(value);
            }
        }
    }

    /// <summary>
    /// A multi-select Enum as a set of options (P108 §5.2). Enumeration
    /// follows stored order; stale option ids read through
    /// <c>fromOptionId</c> exactly like dangling Enum values do.
    /// </summary>
    public class NeoReadOnlyEnumSet<TEnum> : INeoReadOnlySet<TEnum>, INeoCollectionView
        where TEnum : struct, System.Enum
    {
        protected readonly NeoClient? client;
        protected NeoMemberEnum? node;
        private readonly string[]? detachedOptionIds;
        protected readonly Func<string, TEnum> fromOptionId;
        protected readonly Func<TEnum, string> toOptionId;

        public NeoReadOnlyEnumSet(
            NeoClient client,
            NeoMemberEnum node,
            Func<string, TEnum> fromOptionId,
            Func<TEnum, string> toOptionId)
            : this(fromOptionId, toOptionId)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.node = node ?? throw new ArgumentNullException(nameof(node));
        }

        /// <summary>A snapshot of option ids, e.g. a NeoScript getter's result.</summary>
        internal NeoReadOnlyEnumSet(
            string[] optionIds,
            Func<string, TEnum> fromOptionId,
            Func<TEnum, string> toOptionId)
            : this(fromOptionId, toOptionId)
        {
            detachedOptionIds = optionIds ?? throw new ArgumentNullException(nameof(optionIds));
        }

        private NeoReadOnlyEnumSet(Func<string, TEnum> fromOptionId, Func<TEnum, string> toOptionId)
        {
            this.fromOptionId = fromOptionId ?? throw new ArgumentNullException(nameof(fromOptionId));
            this.toOptionId = toOptionId ?? throw new ArgumentNullException(nameof(toOptionId));
        }

        private protected string[] OptionIds => node?.Selected() ?? detachedOptionIds!;

        /// <summary>
        /// Subscribes to any change to this set's selection. The handler
        /// receives this set and the change source. Dispose the returned
        /// subscription to stop listening.
        /// </summary>
        public IDisposable OnChanged(Action<NeoReadOnlyEnumSet<TEnum>, NeoChangeSource> handler)
        {
            if (node is null)
            {
                throw new InvalidOperationException(
                    "A NeoScript result enum set is a snapshot; it has no stored member to watch.");
            }
            return NeoCollectionSubscription.Watch(node, client!, this, handler);
        }

        public int Count => OptionIds.Length;

        NeoClient? INeoCollectionView.CollectionClient => client;
        NeoMember? INeoCollectionView.CollectionNode => node;

        /// <summary>
        /// P112 §8. The writable view of this set, when its store takes writes and its member isn't readonly.
        /// </summary>
        public bool TryWritable(out ICollection<TEnum> writable)
        {
            if (this is NeoEnumSet<TEnum> match && !match.IsReadOnly)
            {
                writable = match;
                return true;
            }

            writable = null!;
            return false;
        }

        public bool Contains(TEnum item) => Array.IndexOf(OptionIds, toOptionId(item)) >= 0;

        public Enumerator GetEnumerator() => new(OptionIds, fromOptionId);

        IEnumerator<TEnum> IEnumerable<TEnum>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Enumerates the selected options. A struct, so a <c>foreach</c>
        /// over the set allocates nothing.
        /// </summary>
        public struct Enumerator : IEnumerator<TEnum>
        {
            private readonly string[] optionIds;
            private readonly Func<string, TEnum> fromOptionId;
            private int index;
            private TEnum current;

            internal Enumerator(string[] optionIds, Func<string, TEnum> fromOptionId)
            {
                this.optionIds = optionIds;
                this.fromOptionId = fromOptionId;
                index = 0;
                current = default;
            }

            public TEnum Current => current;

            object IEnumerator.Current => current;

            public bool MoveNext()
            {
                if (index == optionIds.Length)
                    return false;
                current = fromOptionId(optionIds[index++]);
                return true;
            }

            void IEnumerator.Reset() => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }
    }

    /// <summary>
    /// Writable <see cref="NeoReadOnlyEnumSet{TEnum}"/>. In-place writes
    /// replace the stored option-id array; <see cref="Add"/> returns false
    /// and writes nothing when the option is already selected.
    /// </summary>
    public class NeoEnumSet<TEnum> : NeoReadOnlyEnumSet<TEnum>, ISet<TEnum>, INeoKeyedSet<TEnum>
        where TEnum : struct, System.Enum
    {
        private readonly Func<NeoMemberEnumWritable> getWritableNode;
        private readonly Action? beforeWrite;
        private readonly Func<bool>? isReadOnly;

        public NeoEnumSet(
            NeoClient client,
            NeoMemberEnumWritable node,
            Func<string, TEnum> fromOptionId,
            Func<TEnum, string> toOptionId)
            : this(client, node, () => node, fromOptionId, toOptionId)
        {
        }

        public NeoEnumSet(
            NeoClient client,
            NeoMemberEnum node,
            Func<NeoMemberEnumWritable> getWritableNode,
            Func<string, TEnum> fromOptionId,
            Func<TEnum, string> toOptionId,
            Action? beforeWrite = null,
            Func<bool>? isReadOnly = null)
            : base(client, node, fromOptionId, toOptionId)
        {
            this.getWritableNode = getWritableNode ?? throw new ArgumentNullException(nameof(getWritableNode));
            this.beforeWrite = beforeWrite;
            this.isReadOnly = isReadOnly;
        }

        private void Write(string[] optionIds)
        {
            var writableNode = getWritableNode();
            node = writableNode;
            writableNode.Set(optionIds);
        }

        public bool IsReadOnly => isReadOnly?.Invoke() ?? false;

        public bool Add(TEnum item)
        {
            beforeWrite?.Invoke();
            string optionId = toOptionId(item);
            string[] current = OptionIds;
            if (Array.IndexOf(current, optionId) >= 0)
                return false;
            var next = new string[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[current.Length] = optionId;
            Write(next);
            return true;
        }

        void ICollection<TEnum>.Add(TEnum item) => Add(item);

        public bool Remove(TEnum item)
        {
            beforeWrite?.Invoke();
            string[] current = OptionIds;
            int index = Array.IndexOf(current, toOptionId(item));
            if (index < 0)
                return false;
            var next = new string[current.Length - 1];
            Array.Copy(current, next, index);
            Array.Copy(current, index + 1, next, index, current.Length - index - 1);
            Write(next);
            return true;
        }

        public void Clear()
        {
            beforeWrite?.Invoke();
            if (OptionIds.Length == 0)
                return;
            Write(Array.Empty<string>());
        }

        public void CopyTo(TEnum[] array, int arrayIndex)
        {
            if (array is null)
                throw new ArgumentNullException(nameof(array));
            foreach (TEnum item in this)
                array[arrayIndex++] = item;
        }

        public void UnionWith(IEnumerable<TEnum> other) => NeoSetAlgebra.UnionWith(this, other);

        public void ExceptWith(IEnumerable<TEnum> other) => NeoSetAlgebra.ExceptWith(this, other);

        public void IntersectWith(IEnumerable<TEnum> other) => NeoSetAlgebra.IntersectWith(this, other);

        public void SymmetricExceptWith(IEnumerable<TEnum> other) => NeoSetAlgebra.SymmetricExceptWith(this, other);

        public bool IsSubsetOf(IEnumerable<TEnum> other) => NeoSetAlgebra.IsSubsetOf(this, other);

        public bool IsProperSubsetOf(IEnumerable<TEnum> other) => NeoSetAlgebra.IsProperSubsetOf(this, other);

        public bool IsSupersetOf(IEnumerable<TEnum> other) => NeoSetAlgebra.IsSupersetOf(this, other);

        public bool IsProperSupersetOf(IEnumerable<TEnum> other) => NeoSetAlgebra.IsProperSupersetOf(this, other);

        public bool Overlaps(IEnumerable<TEnum> other) => NeoSetAlgebra.Overlaps(this, other);

        public bool SetEquals(IEnumerable<TEnum> other) => NeoSetAlgebra.SetEquals(this, other);

        string? INeoKeyedSet<TEnum>.KeyOf(TEnum item) => toOptionId(item);

        bool INeoKeyedSet<TEnum>.HoldsKey(string key) => Array.IndexOf(OptionIds, key) >= 0;

        List<string> INeoKeyedSet<TEnum>.EntryKeys() => new(OptionIds);

        void INeoKeyedSet<TEnum>.RemoveKeys(IEnumerable<string> keys)
        {
            beforeWrite?.Invoke();
            var removed = new HashSet<string>(keys, StringComparer.Ordinal);
            var next = new List<string>(OptionIds.Length);
            foreach (string optionId in OptionIds)
            {
                if (!removed.Contains(optionId))
                    next.Add(optionId);
            }
            if (next.Count < OptionIds.Length)
                Write(next.ToArray());
        }
    }
}
