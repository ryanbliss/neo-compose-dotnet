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
    /// cannot take, an assignment into a stored owner, a return to C# — the
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
        internal readonly object?[] values;
        internal readonly byte[] states;
        /// <summary>Growable entries of List slots mutated in place; the slot value is their snapshot.</summary>
        internal List<object?>?[]? listBuffers;
        /// <summary>The detached object whose slot holds this one; it materializes through that root.</summary>
        internal NeoScriptObject? owner;
        internal string? attachedId;
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

        internal NeoScriptObject(
            NeoClient client,
            NeoGeneratedTypesSupport.DetachedClassPlan plan,
            NeoScriptAllocationTracker tracker)
        {
            this.client = client;
            this.plan = plan;
            values = new object?[plan.slots.Length];
            states = new byte[plan.slots.Length];
            this.tracker = tracker;
            trackerGeneration = tracker.Generation;
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
