// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // Nodes exist only for ids some store holds. Each store mutation
        // refreshes the node it touches, so a read costs one lookup.
        private readonly Dictionary<string, NeoValueNode> valueNodes = new(System.StringComparer.Ordinal);

        /// <summary>The node for <paramref name="id"/>, or null when no store holds it.</summary>
        internal NeoValueNode? ValueNode(string id)
        {
            if (valueNodes.TryGetValue(id, out NeoValueNode node))
                return node;
            node = new NeoValueNode(id);
            if (!FillValueNode(node))
                return null;
            writableValueSubscriptions.TryGetValue(id, out node.subscribers);
            valueNodes.Add(id, node);
            return node;
        }

        /// <summary>The node for <paramref name="id"/> if one was already made.</summary>
        internal NeoValueNode? ExistingValueNode(string id) =>
            valueNodes.TryGetValue(id, out NeoValueNode node) ? node : null;

        /// <summary>Re-reads a node's rows after a store changed <paramref name="id"/>.</summary>
        private void SyncValueNode(string id)
        {
            if (valueNodes.TryGetValue(id, out NeoValueNode node) && !FillValueNode(node))
                DropValueNode(node);
        }

        /// <summary>
        /// Points a node at the row a store write just set. Only that
        /// store's slot changed, so the node's other slots still hold.
        /// </summary>
        /// <param name="node">The live node of <paramref name="value"/>'s id, when the caller holds it.</param>
        private void SyncStoredValueNode(NeoValueOwnership ownership, MemberValue value, NeoValueNode? node)
        {
            if (node is null && !valueNodes.TryGetValue(value.id, out node))
                return;
            if (ownership == NeoValueOwnership.Session)
                node.session = value;
            else
                node.save = value;
        }

        private bool FillValueNode(NeoValueNode node)
        {
            sessionData.values.TryGetValue(node.id, out node.session);
            saveData.values.TryGetValue(node.id, out node.save);
            if (virtualValues.TryGetValue(node.id, out node.virtualRow))
                virtualValueOwnership.TryGetValue(node.id, out node.virtualOwnership);
            return node.session is not null
                || node.save is not null
                || node.virtualRow is not null
                || node.Asset(data) is not null;
        }

        private void ClearVirtualValueNodes()
        {
            List<NeoValueNode>? empty = null;
            foreach (NeoValueNode node in valueNodes.Values)
            {
                if (node.virtualRow is null)
                    continue;
                node.virtualRow = null;
                if (node.session is null && node.save is null && node.Asset(data) is null)
                    (empty ??= new List<NeoValueNode>()).Add(node);
            }
            if (empty is not null)
                foreach (NeoValueNode node in empty)
                    DropValueNode(node);
        }

        private void DropValueNode(NeoValueNode node)
        {
            node.live = false;
            valueNodes.Remove(node.id);
        }
    }
}
