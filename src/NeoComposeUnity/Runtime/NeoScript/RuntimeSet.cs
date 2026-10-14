// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>Evaluator-only ordered membership; host and stored values remain arrays.</summary>
    internal sealed class RuntimeSet : IEnumerable<object?>
    {
        private static readonly object NullKey = new();
        private readonly OrderedMap<object, object?> entries;
        private readonly Dictionary<object, Action<object?>> validations = new();
        internal readonly bool Reference;
        internal readonly bool EnumEntries;
        internal readonly object?[] Source;

        internal RuntimeSet(object?[] source, bool reference, bool enumEntries, IEqualityComparer<object?> comparer)
        {
            Source = source;
            Reference = reference;
            EnumEntries = enumEntries;
            entries = new OrderedMap<object, object?>(comparer);
        }

        internal int Count => entries.Count;
        internal bool Contains(object? key) => entries.Contains(key ?? NullKey);
        internal void Add(object? key, object? value)
        {
            key ??= NullKey;
            if (entries.Contains(key))
                return;
            foreach (var validate in validations.Values)
                validate(value);
            entries.Add(key, value);
            if (Reference && value is not null)
                NSGetterEvaluator.TrackRuntimeSetReference(value, this);
        }
        internal void RetargetReference(object oldKey, object newKey, object originalEntry)
        {
            if (entries.TryGetValue(oldKey, out object? entry) && ReferenceEquals(entry, originalEntry))
                entries.Retarget(oldKey, newKey);
        }
        internal void Remove(object? key) => entries.Remove(key ?? NullKey);
        internal void Clear() => entries.Clear();
        internal void Validate(object type, Action<object?> validate)
        {
            if (validations.ContainsKey(type))
                return;
            foreach (object? entry in entries.Values)
                validate(entry);
            validations.Add(type, validate);
        }
        internal object?[] Snapshot() => entries.Snapshot();
        public IEnumerator<object?> GetEnumerator() => entries.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Identity changes update live indexes without retaining discarded sets.</summary>
    internal sealed class RuntimeSetReferences
    {
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<RuntimeSet, object> sets = new();

        internal void Track(RuntimeSet set, object entry) => sets.AddOrUpdate(set, entry);

        internal void Retarget(object oldKey, object newKey)
        {
            foreach (var pair in sets)
                pair.Key.RetargetReference(oldKey, newKey, pair.Value);
        }
    }
}
