// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // One handler list per value id. A multicast delegate would copy its
        // whole invocation array on every subscribe and unsubscribe, which an
        // animation with sixty tracks sharing one dependency turns into an
        // O(n²) churn on every re-resolution.
        private readonly Dictionary<string, List<Action<NeoValueOwnership, string>>> writableValueSubscriptions = new(StringComparer.Ordinal);

        internal IDisposable SubscribeWritableValue(string valueId, Action<NeoValueOwnership, string> handler)
        {
            if (!writableValueSubscriptions.TryGetValue(valueId, out var handlers))
                writableValueSubscriptions[valueId] = handlers = new List<Action<NeoValueOwnership, string>>(1);
            handlers.Add(handler);
            return new WritableValueSubscription(this, valueId, handler);
        }

        private void UnsubscribeWritableValue(string valueId, Action<NeoValueOwnership, string> handler)
        {
            if (!writableValueSubscriptions.TryGetValue(valueId, out var handlers)) return;
            if (!handlers.Remove(handler)) return;
            if (handlers.Count == 0) writableValueSubscriptions.Remove(valueId);
        }

        private void PublishWritableValueChange(NeoValueOwnership ownership, string valueId)
        {
            RefreshSharedEvaluationRow(ownership, valueId);
            // Invoke over a snapshot so reentrant writes, subscriptions and
            // disposal during a callback neither skip nor repeat a handler.
            if (writableValueSubscriptions.TryGetValue(valueId, out var handlers) && handlers.Count != 0)
            {
                int count = handlers.Count;
                var snapshot = ArrayPool<Action<NeoValueOwnership, string>>.Shared.Rent(count);
                handlers.CopyTo(snapshot, 0);
                try
                {
                    for (int i = 0; i < count; i++) snapshot[i](ownership, valueId);
                }
                finally
                {
                    Array.Clear(snapshot, 0, count);
                    ArrayPool<Action<NeoValueOwnership, string>>.Shared.Return(snapshot);
                }
            }
            OnWritableValueChanged?.Invoke(ownership, valueId);
        }

        private sealed class WritableValueSubscription : IDisposable
        {
            private NeoClient? client;
            private readonly string valueId;
            private readonly Action<NeoValueOwnership, string> handler;

            internal WritableValueSubscription(NeoClient client, string valueId, Action<NeoValueOwnership, string> handler)
            {
                this.client = client;
                this.valueId = valueId;
                this.handler = handler;
            }

            public void Dispose()
            {
                NeoClient? owner = client;
                if (owner is null) return;
                client = null;
                owner.UnsubscribeWritableValue(valueId, handler);
            }
        }
    }
}
