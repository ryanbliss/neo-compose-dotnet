// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public static partial class NeoGeneratedTypesSupport
    {
        // A lookup borrows the arguments; a cache entry owns a snapshot so a
        // caller changing its map or a binding cannot change the cache key.
        private readonly struct GenericBindingsKey : IEquatable<GenericBindingsKey>
        {
            internal readonly IReadOnlyDictionary<string, GenericBinding>? Bindings;
            internal GenericBindingsKey(IReadOnlyDictionary<string, GenericBinding>? bindings) => Bindings = bindings;
            internal GenericBindingsKey Snapshot()
            {
                if (Bindings is null || Bindings.Count == 0)
                    return default;
                var copy = new Dictionary<string, GenericBinding>(Bindings.Count, StringComparer.Ordinal);
                foreach (var pair in Bindings)
                    copy.Add(pair.Key, new GenericBinding { kind = pair.Value.kind, memberId = pair.Value.memberId, genericParamId = pair.Value.genericParamId });
                return new GenericBindingsKey(copy);
            }
            public bool Equals(GenericBindingsKey other)
            {
                if (ReferenceEquals(Bindings, other.Bindings))
                    return true;
                if ((Bindings?.Count ?? 0) != (other.Bindings?.Count ?? 0))
                    return false;
                if (Bindings is null || Bindings.Count == 0)
                    return true;
                foreach (var pair in Bindings)
                    if (!other.Bindings!.TryGetValue(pair.Key, out var value)
                        || pair.Value.kind != value.kind || pair.Value.memberId != value.memberId
                        || pair.Value.genericParamId != value.genericParamId)
                        return false;
                return true;
            }
            public override bool Equals(object? other) => other is GenericBindingsKey key && Equals(key);
            public override int GetHashCode()
            {
                int hash = Bindings?.Count ?? 0;
                if (Bindings is not null)
                    foreach (var pair in Bindings)
                        hash ^= HashCode.Combine(pair.Key, pair.Value.kind, pair.Value.memberId, pair.Value.genericParamId);
                return hash;
            }
        }
    }
}
