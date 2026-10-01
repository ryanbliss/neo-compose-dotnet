// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // A value id's nodes: almost always one node, else a set.
        private readonly Dictionary<string, object> nodesByValueId =
            new(StringComparer.Ordinal);

        private void AddNodeValueIndex(NeoMember node, string? valueId)
        {
            if (valueId == null)
                return;
            if (!nodesByValueId.TryGetValue(valueId, out object? nodes))
                nodesByValueId[valueId] = node;
            else if (nodes is HashSet<NeoMember> set)
                set.Add(node);
            else if (!ReferenceEquals(nodes, node))
                nodesByValueId[valueId] = new HashSet<NeoMember> { (NeoMember)nodes, node };
        }

        private void RemoveNodeValueIndex(NeoMember node, string? valueId)
        {
            if (valueId == null || !nodesByValueId.TryGetValue(valueId, out object? nodes))
                return;
            if (nodes is HashSet<NeoMember> set)
            {
                set.Remove(node);
                if (set.Count == 0)
                    nodesByValueId.Remove(valueId);
            }
            else if (ReferenceEquals(nodes, node))
            {
                nodesByValueId.Remove(valueId);
            }
        }

        /// <summary>The nodes indexed under <paramref name="valueId"/>, copied so callers may refresh or dispose them.</summary>
        private NeoMember[] IndexedNodes(string valueId)
        {
            if (!nodesByValueId.TryGetValue(valueId, out object? nodes))
                return Array.Empty<NeoMember>();
            if (nodes is not HashSet<NeoMember> set)
                return new[] { (NeoMember)nodes };
            var copy = new NeoMember[set.Count];
            set.CopyTo(copy);
            return copy;
        }

        internal void UpdateNodeValueIndex(NeoMember node, string? previousId, string? nextId)
        {
            if (!node.IsRegisteredWithClient || previousId == nextId)
                return;
            // The override binding remains relevant even when its row is absent
            // or the node resolves a declaration default under another id.
            if (previousId != node.overrideValueId)
                RemoveNodeValueIndex(node, previousId);
            if (nextId != node.overrideValueId)
                AddNodeValueIndex(node, nextId);
        }

        private void IndexNode(NeoMember node)
        {
            node.IsRegisteredWithClient = true;
            AddNodeValueIndex(node, node.overrideValueId);
            AddNodeValueIndex(node, node.value?.id);
        }

        private void UnindexNode(NeoMember node)
        {
            node.IsRegisteredWithClient = false;
            RemoveNodeValueIndex(node, node.overrideValueId);
            RemoveNodeValueIndex(node, node.value?.id);
        }
    }
}
