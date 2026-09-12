// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private readonly Dictionary<string, HashSet<NeoMember>> nodesByValueId =
            new(StringComparer.Ordinal);

        private void AddNodeValueIndex(NeoMember node, string? valueId)
        {
            if (valueId == null) return;
            if (!nodesByValueId.TryGetValue(valueId, out var nodes))
                nodesByValueId[valueId] = nodes = new HashSet<NeoMember>();
            nodes.Add(node);
        }

        private void RemoveNodeValueIndex(NeoMember node, string? valueId)
        {
            if (valueId == null || !nodesByValueId.TryGetValue(valueId, out var nodes)) return;
            nodes.Remove(node);
            if (nodes.Count == 0) nodesByValueId.Remove(valueId);
        }

        internal void UpdateNodeValueIndex(NeoMember node, string? previousId, string? nextId)
        {
            if (!node.IsRegisteredWithClient || previousId == nextId) return;
            // The override binding remains relevant even when its row is absent
            // or the node resolves a declaration default under another id.
            if (previousId != node.overrideValueId) RemoveNodeValueIndex(node, previousId);
            if (nextId != node.overrideValueId) AddNodeValueIndex(node, nextId);
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
