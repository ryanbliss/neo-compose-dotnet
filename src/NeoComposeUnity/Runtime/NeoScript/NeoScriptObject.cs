// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// A NeoScript <c>new C(...)</c> temporary that has not become rows yet.
    /// Its stored members live in plain slots laid out by a
    /// <see cref="NeoGeneratedTypesSupport.DetachedClassPlan"/>, so building,
    /// filling and reading a result object costs no store traffic.
    ///
    /// <para>The first time anything needs a row — its id, a write the slots
    /// cannot take, an assignment into a stored owner — the
    /// object materializes as one Session graph through the ordinary
    /// constructor staging path, together with every detached object it owns.
    /// From then on it forwards to that row: the evaluator swaps in the row's
    /// canonical record, and this dictionary view reads the live row.</para>
    /// </summary>
    internal sealed class NeoScriptObject : NeoDetachedValue, IDictionary<string, object?>, INeoValueReference
    {
        internal const byte DefaultSlot = 0;
        /// <summary>A default was read into the slot; materialization still leaves it to the class default.</summary>
        internal const byte DefaultReadSlot = 1;
        internal const byte WrittenSlot = 2;

        internal readonly NeoClient client;
        internal readonly NeoGeneratedTypesSupport.DetachedClassPlan plan;
        // A plan of up to InlineSlots slots keeps them in fields, so the
        // object is one allocation; a wider plan keeps them in an array.
        internal const int InlineSlots = 8;
        private object? slot0, slot1, slot2, slot3, slot4, slot5, slot6, slot7;
        private readonly object?[]? wideSlots;
        // Slot states as bitmasks, so a plan of up to 64 slots allocates no
        // state array; a wider plan keeps one byte per slot. A written slot
        // never returns to a default state.
        private ulong writtenSlots;
        private ulong defaultReadSlots;
        private readonly byte[]? wideStates;
        // Slots whose current array a read handed out, so its alias origin is recorded.
        private ulong exposedArraySlots;
        /// <summary>Registered with its tracker to seal its List slots' append buffers.</summary>
        internal bool listBuffersNoted;
        /// <summary>The detached object whose slot holds this one; it materializes through that root.</summary>
        internal NeoScriptObject? owner;
        internal string? attachedId;
        // A getter result has an independent lifetime. Its graph may be read
        // and mutated by callers, but a stored owner must take a copy.
        internal bool sharedGetterResult;
        internal void ShareGetterResult()
        {
            NeoScriptObject root = this;
            while (root.owner is not null)
                root = root.owner;
            if (root.sharedGetterResult)
                return;
            // Mark each owned object once so every later field read and
            // assignment checks one flag, independent of graph depth.
            Stack<NeoScriptObject>? pending = null;
            NeoScriptObject current = root;
            while (true)
            {
                current.sharedGetterResult = true;
                for (int i = 0; i < current.SlotCount; i++)
                {
                    if (current.Slot(i) is NeoScriptObject child && !child.sharedGetterResult)
                        (pending ??= new()).Push(child);
                    else if (current.Slot(i) is IReadOnlyList<object?> entries)
                        for (int j = 0; j < entries.Count; j++)
                            if (entries[j] is NeoScriptObject entry && !entry.sharedGetterResult)
                                (pending ??= new()).Push(entry);
                }
                if (pending is null || pending.Count == 0)
                    break;
                current = pending.Pop();
            }
        }
        /// <summary>
        /// The P75 creation recipe of a declared construction, serialized onto
        /// the row at materialization; null for a schema-derived construction.
        /// </summary>
        internal object?[]? constructorArgs;
        internal ConstructorRecord? constructor;
        /// <summary>A declared constructor is still running, so a materialization may leave required members unset.</summary>
        internal bool constructing;
        internal readonly NeoScriptAllocationTracker tracker;
        internal readonly int trackerGeneration;
        /// <summary>The generated C# view handed out for this object, so C# sees one identity.</summary>
        internal NeoGeneratedClassValue? view;
        /// <summary>
        /// What a native read of an immutable native-backed class built from
        /// the slots, so later native calls reuse it instead of rebuilding it.
        /// </summary>
        internal object? nativeValue;

        internal NeoScriptObject(
            NeoClient client,
            NeoGeneratedTypesSupport.DetachedClassPlan plan,
            NeoScriptAllocationTracker tracker)
        {
            this.client = client;
            this.plan = plan;
            if (plan.slots.Length > InlineSlots)
                wideSlots = new object?[plan.slots.Length];
            if (plan.slots.Length > 64)
                wideStates = new byte[plan.slots.Length];
            this.tracker = tracker;
            trackerGeneration = tracker.Generation;
        }

        internal int SlotCount => plan.slots.Length;

        /// <summary>Slot <paramref name="index"/>'s storage.</summary>
        internal ref object? Slot(int index)
        {
            if (wideSlots is not null)
                return ref wideSlots[index];
            switch (index)
            {
                case 0:
                    return ref slot0;
                case 1:
                    return ref slot1;
                case 2:
                    return ref slot2;
                case 3:
                    return ref slot3;
                case 4:
                    return ref slot4;
                case 5:
                    return ref slot5;
                case 6:
                    return ref slot6;
                case 7:
                    return ref slot7;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        internal byte State(int index)
        {
            if (wideStates is not null)
                return wideStates[index];
            ulong bit = 1UL << index;
            if ((writtenSlots & bit) != 0)
                return WrittenSlot;
            return (defaultReadSlots & bit) != 0 ? DefaultReadSlot : DefaultSlot;
        }

        internal void MarkWritten(int index)
        {
            if (wideStates is not null)
                wideStates[index] = WrittenSlot;
            else
                writtenSlots |= 1UL << index;
        }

        /// <summary>Marks the slot's current array handed out; false when it already was.</summary>
        internal bool MarkArrayExposed(int index)
        {
            if (index >= 64)
                return true;
            ulong bit = 1UL << index;
            if ((exposedArraySlots & bit) != 0)
                return false;
            exposedArraySlots |= bit;
            return true;
        }

        internal void ClearArrayExposed(int index)
        {
            if (index < 64)
                exposedArraySlots &= ~(1UL << index);
        }

        internal void MarkDefaultRead(int index)
        {
            if (wideStates is not null)
                wideStates[index] = DefaultReadSlot;
            else
                defaultReadSlots |= 1UL << index;
        }

        public string? valueId => NSGetterEvaluator.AttachDetached(this, null);

        private Dictionary<string, string> Live()
        {
            string id = NSGetterEvaluator.AttachDetached(this, null);
            return client.TryGetValueOwnership(id, out NeoValueOwnership ownership)
                && client.TryGetValue(ownership, id, out ObjectMemberValue? row)
                && row!.value is not null
                    ? row.value
                    : new Dictionary<string, string>();
        }

        public object? this[string key]
        {
            get => Live()[key];
            set => throw new NotSupportedException();
        }

        public ICollection<string> Keys => Live().Keys;

        public ICollection<object?> Values
        {
            get
            {
                var result = new List<object?>();
                foreach (string id in Live().Values)
                    result.Add(id);
                return result;
            }
        }

        public int Count => Live().Count;

        public bool IsReadOnly => true;

        public bool ContainsKey(string key) => Live().ContainsKey(key);

        public bool TryGetValue(string key, out object? value)
        {
            bool found = Live().TryGetValue(key, out string? id);
            value = id;
            return found;
        }

        public bool Contains(KeyValuePair<string, object?> item) =>
            Live().TryGetValue(item.Key, out string? id) && Equals(id, item.Value);

        public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
        {
            foreach (KeyValuePair<string, object?> pair in this)
                array[arrayIndex++] = pair;
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            foreach (KeyValuePair<string, string> pair in Live())
                yield return new KeyValuePair<string, object?>(pair.Key, pair.Value);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(string key, object? value) => throw new NotSupportedException();

        public void Add(KeyValuePair<string, object?> item) => throw new NotSupportedException();

        public bool Remove(string key) => throw new NotSupportedException();

        public bool Remove(KeyValuePair<string, object?> item) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();
    }
}
