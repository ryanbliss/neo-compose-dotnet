// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace NeoCompose.Runtime.Json
{
    /// <summary>Authored correspondence for virtual endpoints in a default template.</summary>
    public sealed class NeoChangeListenerEndpoints : Dictionary<string, NeoChangeListenerEndpoint>
    {
        public NeoChangeListenerEndpoints() : base(StringComparer.Ordinal) { }

        [OnDeserialized]
        private void Validate(StreamingContext context)
        {
            foreach (var pair in this)
            {
                if (string.IsNullOrEmpty(pair.Key))
                    throw new JsonSerializationException("Listener endpoint requires a nonempty logical id.");
                if (pair.Value is null)
                    throw new JsonSerializationException($"Listener endpoint '{pair.Key}' requires a descriptor.");
                pair.Value.Validate();
            }
        }

        internal NeoChangeListenerEndpoints Copy()
        {
            var copy = new NeoChangeListenerEndpoints();
            foreach (var pair in this)
                copy.Add(pair.Key, new NeoChangeListenerEndpoint
                {
                    pathKey = pair.Value.pathKey,
                    memberId = pair.Value.memberId,
                    sourceValueId = pair.Value.sourceValueId,
                    rootId = pair.Value.rootId
                });
            return copy;
        }

        internal static bool Same(NeoChangeListenerEndpoints? left, NeoChangeListenerEndpoints? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null || right is null || left.Count != right.Count)
                return false;
            foreach (var pair in left)
                if (!right.TryGetValue(pair.Key, out var other) || pair.Value.pathKey != other.pathKey
                    || pair.Value.memberId != other.memberId || pair.Value.sourceValueId != other.sourceValueId
                    || pair.Value.rootId != other.rootId)
                    return false;
            return true;
        }
    }

    public sealed class NeoChangeListenerEndpoint
    {
        public string pathKey = null!;
        public string memberId = null!;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? sourceValueId;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? rootId;

        internal void Validate()
        {
            if (pathKey is null || !pathKey.StartsWith("$/", StringComparison.Ordinal))
                throw new JsonSerializationException("Listener endpoint path must be relative to its P75 source frame.");
            if (string.IsNullOrEmpty(memberId))
                throw new JsonSerializationException("Listener endpoint requires a declaring member id.");
            if (sourceValueId is { Length: 0 })
                throw new JsonSerializationException("Listener endpoint source id cannot be empty.");
            if (rootId is { Length: 0 })
                throw new JsonSerializationException("Listener endpoint frame id cannot be empty.");
        }
    }

    /// <summary>Binding-root metadata indexed by logical owner and canonical member id.</summary>
    public sealed class NeoChangeListenerMap : Dictionary<string, Dictionary<string, NeoDelegateValue[]>>
    {
        public NeoChangeListenerMap() : base(StringComparer.Ordinal)
        {
        }

        [OnDeserialized]
        private void Validate(StreamingContext context)
        {
            foreach (var owner in this)
            {
                if (string.IsNullOrEmpty(owner.Key))
                    throw new JsonSerializationException("Change listener metadata requires a nonempty owner id.");
                if (owner.Value is null)
                    throw new JsonSerializationException($"Change listener owner '{owner.Key}' requires a member map.");
                foreach (var member in owner.Value)
                {
                    if (string.IsNullOrEmpty(member.Key))
                        throw new JsonSerializationException($"Change listener owner '{owner.Key}' has an empty member id.");
                    if (member.Value is null)
                        throw new JsonSerializationException($"Change listener member '{member.Key}' requires an array.");
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var target in member.Value)
                    {
                        if (target is null || !target.IsMemberTarget || target.IsClosure)
                            throw new JsonSerializationException($"Change listener member '{member.Key}' requires member targets.");
                        if (!seen.Add(NeoActionValue.ListenerIdentity(target)))
                            throw new JsonSerializationException($"Change listener member '{member.Key}' repeats a handler identity.");
                    }
                }
            }
        }

        internal NeoChangeListenerMap Copy()
        {
            var copy = new NeoChangeListenerMap();
            foreach (var owner in this)
            {
                var entry = new Dictionary<string, NeoDelegateValue[]>(StringComparer.Ordinal);
                foreach (var member in owner.Value)
                {
                    var targets = new NeoDelegateValue[member.Value.Length];
                    for (int i = 0; i < targets.Length; i++)
                        targets[i] = member.Value[i].PersistedCopy();
                    entry.Add(member.Key, targets);
                }
                copy.Add(owner.Key, entry);
            }
            return copy;
        }

        internal static bool Same(NeoChangeListenerMap? left, NeoChangeListenerMap? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null || right is null || left.Count != right.Count)
                return false;
            foreach (var owner in left)
            {
                if (!right.TryGetValue(owner.Key, out var entry) || owner.Value.Count != entry.Count)
                    return false;
                foreach (var member in owner.Value)
                {
                    if (!entry.TryGetValue(member.Key, out var targets) || member.Value.Length != targets.Length)
                        return false;
                    for (int i = 0; i < targets.Length; i++)
                        if (member.Value[i].memberId != targets[i].memberId || member.Value[i].valueId != targets[i].valueId)
                            return false;
                }
            }
            return true;
        }

        internal NeoDelegateValue[]? Find(string ownerId, string memberId) =>
            TryGetValue(ownerId, out var entry) && entry.TryGetValue(memberId, out var targets) ? targets : null;

        internal void Set(string ownerId, string memberId, NeoDelegateValue[]? targets)
        {
            if (targets is null)
            {
                if (TryGetValue(ownerId, out var existing))
                {
                    existing.Remove(memberId);
                    if (existing.Count == 0)
                        Remove(ownerId);
                }
                return;
            }
            if (!TryGetValue(ownerId, out var entry))
                Add(ownerId, entry = new Dictionary<string, NeoDelegateValue[]>(StringComparer.Ordinal));
            entry[memberId] = targets;
        }
    }
}
