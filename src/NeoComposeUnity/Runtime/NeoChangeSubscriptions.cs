// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime
{
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

    public sealed class NeoChangedArgs<TFields>
    {
        public IReadOnlyDictionary<INeoField, object?> Changes { get; }

        public NeoChangedArgs(IReadOnlyDictionary<INeoField, object?> changes)
        {
            Changes = changes;
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
