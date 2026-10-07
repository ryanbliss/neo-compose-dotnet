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
                return departedReads == DepartedReads.Prefer && !node.live ? departedNodes[id] : node;
            return departedNodes.Count == 0 ? CreateValueNode(id) : DepartedValueNode(id);
        }

        private NeoValueNode? CreateValueNode(string id)
        {
            // Removed and unwritten ids are read often; a miss makes no node.
            if (!HoldsRow(id))
                return null;
            var node = new NeoValueNode(id);
            if (!FillValueNode(node))
                return null;
            writableValueSubscriptions.TryGetValue(id, out node.subscribers);
            valueNodes.Add(id, node);
            return node;
        }

        private bool HoldsRow(string id) =>
            sessionData.values.ContainsKey(id)
            || saveData.values.ContainsKey(id)
            || userSource.userData.values.ContainsKey(id)
            || TryGetVirtualRow(id, out _, out _)
            || data.values.ContainsKey(id);

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
            switch (ownership)
            {
                case NeoValueOwnership.Session:
                    node.session = value;
                    break;
                case NeoValueOwnership.Save:
                    node.save = value;
                    break;
                default:
                    node.user = value;
                    break;
            }
        }

        /// <summary>The row a node holds for a writable store, or null for Asset.</summary>
        private static MemberValue? StoredRow(NeoValueNode? node, NeoValueOwnership ownership) => ownership switch
        {
            NeoValueOwnership.Session => node?.session,
            NeoValueOwnership.Save => node?.save,
            NeoValueOwnership.User => node?.user,
            NeoValueOwnership.Asset => null,
            _ => throw new System.InvalidOperationException($"Unknown value ownership '{ownership}'."),
        };

        /// <summary>
        /// A virtual row and its ownership. A save client's User virtual rows
        /// live in its user client, which replays them (P104 §4.3).
        /// </summary>
        private bool TryGetVirtualRow(string id, out MemberValue? row, out NeoValueOwnership ownership)
        {
            if (virtualValues.TryGetValue(id, out row))
            {
                virtualValueOwnership.TryGetValue(id, out ownership);
                return true;
            }
            if (!ReferenceEquals(userSource, this) && userSource.virtualValues.TryGetValue(id, out row))
            {
                ownership = NeoValueOwnership.User;
                return true;
            }
            ownership = default;
            return false;
        }

        private bool FillValueNode(NeoValueNode node)
        {
            sessionData.values.TryGetValue(node.id, out node.session);
            saveData.values.TryGetValue(node.id, out node.save);
            userSource.userData.values.TryGetValue(node.id, out node.user);
            TryGetVirtualRow(node.id, out node.virtualRow, out node.virtualOwnership);
            return node.session is not null
                || node.save is not null
                || node.user is not null
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
                if (node.session is null && node.save is null && node.user is null && node.Asset(data) is null)
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
