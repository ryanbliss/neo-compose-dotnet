// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private readonly Dictionary<string, Action<NeoValueOwnership, string>> writableValueSubscriptions = new();

        internal IDisposable SubscribeWritableValue(string valueId, Action<NeoValueOwnership, string> handler)
        {
            writableValueSubscriptions.TryGetValue(valueId, out var previous);
            writableValueSubscriptions[valueId] = previous + handler;
            return new NeoDisposableSubscription(() =>
            {
                if (!writableValueSubscriptions.TryGetValue(valueId, out var current)) return;
                current -= handler;
                if (current is null) writableValueSubscriptions.Remove(valueId);
                else writableValueSubscriptions[valueId] = current;
            });
        }

        private void PublishWritableValueChange(NeoValueOwnership ownership, string valueId)
        {
            // Multicast delegates retain a stable invocation snapshot across
            // reentrant writes, subscriptions and disposal during a callback.
            if (writableValueSubscriptions.TryGetValue(valueId, out var handlers))
                handlers(ownership, valueId);
            OnWritableValueChanged?.Invoke(ownership, valueId);
        }
    }
}
