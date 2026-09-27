// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Where a change originated: <see cref="Local"/> for writes made by this
    /// process (gameplay code, dialogue actions), <see cref="External"/> for
    /// content applied from outside the process (e.g. a live save session
    /// co-editor patching the running save).
    /// </summary>
    public enum NeoChangeSource
    {
        Local,
        External,
    }

    public interface INeoField
    {
        string Key { get; }
        Type ValueType { get; }
    }

    public sealed class NeoField<T> : INeoField
    {
        public string Key { get; }
        public Type ValueType => typeof(T);

        public NeoField(string key)
        {
            Key = key;
        }
    }

    public sealed class NeoChangedArgs<TFields> : IReadOnlyDictionary<INeoField, object?>
    {
        private readonly IReadOnlyDictionary<INeoField, object?>? changes;
        private readonly INeoField? singleField;
        private readonly object? singleValue;
        public IReadOnlyDictionary<INeoField, object?> Changes => changes ?? this;
        public NeoChangeSource Source { get; }

        public NeoChangedArgs(
            IReadOnlyDictionary<INeoField, object?> changes,
            NeoChangeSource source)
        {
            this.changes = changes ?? throw new ArgumentNullException(nameof(changes));
            Source = source;
        }

        // A single-field notification is its own immutable dictionary. Callers
        // may retain it across later writes without retaining pooled storage.
        internal NeoChangedArgs(INeoField field, object? value, NeoChangeSource source)
        {
            singleField = field;
            singleValue = value;
            Source = source;
        }

        int IReadOnlyCollection<KeyValuePair<INeoField, object?>>.Count => changes?.Count ?? 1;
        object? IReadOnlyDictionary<INeoField, object?>.this[INeoField key] =>
            Changes.TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();
        bool IReadOnlyDictionary<INeoField, object?>.ContainsKey(INeoField key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            return changes?.ContainsKey(key) ?? Equals(key, singleField);
        }
        bool IReadOnlyDictionary<INeoField, object?>.TryGetValue(INeoField key, out object? value)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (changes is not null) return changes.TryGetValue(key, out value);
            bool found = Equals(key, singleField);
            value = found ? singleValue : null;
            return found;
        }
        IEnumerable<INeoField> IReadOnlyDictionary<INeoField, object?>.Keys => EnumerateKeys();
        IEnumerable<object?> IReadOnlyDictionary<INeoField, object?>.Values => EnumerateValues();
        private IEnumerable<INeoField> EnumerateKeys()
        {
            if (changes is not null) { foreach (var key in changes.Keys) yield return key; }
            else yield return singleField!;
        }
        private IEnumerable<object?> EnumerateValues()
        {
            if (changes is not null) { foreach (var value in changes.Values) yield return value; }
            else yield return singleValue;
        }
        IEnumerator<KeyValuePair<INeoField, object?>> IEnumerable<KeyValuePair<INeoField, object?>>.GetEnumerator()
            => changes is not null ? changes.GetEnumerator() : SingleEntry().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => ((IEnumerable<KeyValuePair<INeoField, object?>>)this).GetEnumerator();
        private IEnumerable<KeyValuePair<INeoField, object?>> SingleEntry()
        {
            yield return new KeyValuePair<INeoField, object?>(singleField!, singleValue);
        }

        public bool Has<T>(NeoField<T> field)
        {
            return Changes.ContainsKey(field);
        }

        public bool TryGet<T>(NeoField<T> field, out T value)
        {
            if (Changes.TryGetValue(field, out object? raw))
            {
                if (raw is null)
                {
                    value = default!;
                    return true;
                }
                if (raw is T typed)
                {
                    value = typed;
                    return true;
                }
            }
            value = default!;
            return false;
        }
    }

    internal sealed class NeoDisposableSubscription : IDisposable
    {
        private Action? dispose;

        public NeoDisposableSubscription(Action dispose)
        {
            this.dispose = dispose;
        }

        public void Dispose()
        {
            Action? callback = dispose;
            if (callback is null) return;
            dispose = null;
            callback();
        }
    }
}
