// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // P98 §2.4: while some class has a departure hook, a mutation that
        // removes a row, or replaces an object row with another class, first
        // keeps the id's rows as a departed node. Lifecycle hooks in the drain
        // at that boundary read through them, and the drain drops them.

        private enum DepartedReads
        {
            // Outside lifecycle hooks: departed rows are invisible.
            None,
            // A removed id reads its departed rows; a replacement wins.
            Fallback,
            // A departure hook whose receiver left: departed rows win.
            Prefer,
        }

        // The store layer a mutation replaces.
        private enum RowLayer
        {
            Session,
            Save,
            Virtual,
            Asset,
        }

        private readonly Dictionary<string, NeoValueNode> departedNodes = new(StringComparer.Ordinal);
        // Nodes made for a departed id's replacement. They stay out of
        // live so every holder resolves through ValueNode until the drain ends.
        private readonly List<NeoValueNode> heldReplacements = new();
        private DepartedReads departedReads;

        private bool KeepsDepartedRows => scriptRuntimeStarted && (schemaHooks & NeoLifecycleHooks.Departure) != 0;

        /// <summary>
        /// Keeps the rows at <paramref name="id"/> before a mutation sets
        /// <paramref name="layer"/> to <paramref name="next"/>, null for a
        /// removal, when the id stops resolving or changes class.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NoteDeparture(string id, RowLayer layer, MemberValue? next)
        {
            // A leaf overwrite never departs: a row's kind is its member's.
            if (KeepsDepartedRows && next is null or { IsRemoved: true } or ObjectMemberValue)
                CaptureDeparture(id, layer, next);
        }

        /// <summary>
        /// Notes every departure a plan makes, judged by the rows the whole
        /// plan leaves, so a row it moves between the Session and Save stores stays.
        /// </summary>
        private void NoteDepartures(NeoWritePlan plan)
        {
            if (!KeepsDepartedRows)
                return;
            foreach (var pair in plan.Rows)
            {
                if (pair.Value is null or { IsRemoved: true } or ObjectMemberValue)
                {
                    RowLayer layer = pair.Key.ownership == NeoValueOwnership.Session ? RowLayer.Session : RowLayer.Save;
                    CaptureDeparture(pair.Key.id, layer, pair.Value, plan.Rows);
                }
            }
        }

        /// <param name="planRows">The rows a plan writes, which decide its writable layers.</param>
        private void CaptureDeparture(
            string id,
            RowLayer layer,
            MemberValue? next,
            Dictionary<(NeoValueOwnership ownership, string id), MemberValue?>? planRows = null)
        {
            if (departedNodes.ContainsKey(id))
                return;
            MemberValue? session, save, asset, virtualRow;
            NeoValueOwnership virtualOwnership = default;
            if (valueNodes.TryGetValue(id, out NeoValueNode? node))
            {
                session = node.session;
                save = node.save;
                asset = node.Asset(data);
                virtualRow = node.virtualRow;
                virtualOwnership = node.virtualOwnership;
            }
            else
            {
                sessionData.values.TryGetValue(id, out session);
                saveData.values.TryGetValue(id, out save);
                data.values.TryGetValue(id, out asset);
                if (virtualValues.TryGetValue(id, out virtualRow))
                    virtualValueOwnership.TryGetValue(id, out virtualOwnership);
            }
            MemberValue? previous = session ?? save ?? asset ?? virtualRow;
            if (previous is null or { IsRemoved: true })
                return;
            MemberValue? afterSession = layer == RowLayer.Session ? next : session;
            MemberValue? afterSave = layer == RowLayer.Save ? next : save;
            if (planRows is not null)
            {
                if (planRows.TryGetValue((NeoValueOwnership.Session, id), out MemberValue? planned))
                    afterSession = planned;
                if (planRows.TryGetValue((NeoValueOwnership.Save, id), out planned))
                    afterSave = planned;
            }
            MemberValue? after = afterSession
                ?? afterSave
                ?? (layer == RowLayer.Asset ? next : asset)
                ?? (layer == RowLayer.Virtual ? next : virtualRow);
            if (after is { IsRemoved: false }
                && (previous is not ObjectMemberValue old
                    || after is ObjectMemberValue { classId: var classId } && classId == old.classId))
                return;
            departedNodes.Add(id, NeoValueNode.Departed(id, session, save, asset, virtualRow, virtualOwnership));
            // Holders resolve the id again, and meet the departed rows or the
            // replacement.
            if (node is not null)
                DropValueNode(node);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NoteDeparture(NeoValueOwnership ownership, string id, MemberValue? next) =>
            NoteDeparture(id, ownership == NeoValueOwnership.Session ? RowLayer.Session : RowLayer.Save, next);

        /// <summary>ValueNode's miss path while departed rows are kept.</summary>
        private NeoValueNode? DepartedValueNode(string id)
        {
            NeoValueNode? node = CreateValueNode(id);
            if (!departedNodes.TryGetValue(id, out NeoValueNode? departed))
                return node;
            if (node is null)
                return departedReads == DepartedReads.None ? null : departed;
            node.live = false;
            heldReplacements.Add(node);
            return departedReads == DepartedReads.Prefer ? departed : node;
        }

        internal bool IsDeparted(string id) => departedNodes.Count != 0 && departedNodes.ContainsKey(id);

        /// <summary>A lifecycle hook may not write a row that left the data (P98 §2.4).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ThrowIfDepartedWrite(string id)
        {
            if (departedReads != DepartedReads.None && departedNodes.Count != 0)
                CheckDepartedWrite(id);
        }

        private void CheckDepartedWrite(string id)
        {
            if (!departedNodes.ContainsKey(id))
                return;
            if (departedReads == DepartedReads.Fallback && HoldsRow(id))
                return;
            throw new InvalidOperationException(
                $"Value '{id}' left the data in this boundary, so a lifecycle hook cannot write it.");
        }

        private void ReleaseDepartedRows()
        {
            departedNodes.Clear();
            foreach (NeoValueNode node in heldReplacements)
                if (valueNodes.TryGetValue(node.id, out NeoValueNode? held) && ReferenceEquals(held, node))
                    node.live = true;
            heldReplacements.Clear();
        }
    }
}
