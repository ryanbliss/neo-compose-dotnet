// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // P130 §4. A materialized row never replays, so a row built from
        // mutable state would come back different after a save and reload.
        // While a root materializes, each construction step records whether
        // it read mutable state. The leaves it produced pin into the persisted
        // overlay, and the attribution is discarded once the root installs.
        internal sealed class PinFrame
        {
            internal PinFrame? Parent;
            internal bool Mutable;
        }

        private sealed class PinScope
        {
            internal PinScope? Parent;
            internal string RootId = null!;
            // Rows a mutable step wrote pin themselves. A value an initializer
            // produced from mutable reads pins its whole subtree.
            internal readonly HashSet<string> MutableRows = new(StringComparer.Ordinal);
            internal readonly HashSet<string> MutableSubtrees = new(StringComparer.Ordinal);
        }

        private PinScope? pinScope;
        private PinFrame? pinFrame;

        private IDisposable BeginPinScope(string rootId)
        {
            var scope = new PinScope { Parent = pinScope, RootId = rootId };
            var frame = new PinFrame { Parent = pinFrame };
            pinScope = scope;
            pinFrame = frame;
            return new NeoDisposableAction(() =>
            {
                pinScope = scope.Parent;
                pinFrame = frame.Parent;
            });
        }

        /// <summary>A construction step whose reads belong only to the rows it produces.</summary>
        internal PinFrame? EnterPinFrame()
        {
            if (pinScope is null)
                return null;
            return pinFrame = new PinFrame { Parent = pinFrame };
        }

        /// <summary>Ends <paramref name="frame"/> and attributes its reads to <paramref name="producedId"/>.</summary>
        internal void ExitPinFrame(PinFrame? frame, string? producedId)
        {
            if (frame is null)
                return;
            pinFrame = frame.Parent;
            if (frame.Mutable && producedId is not null)
                pinScope?.MutableSubtrees.Add(producedId);
        }

        /// <summary>
        /// Rows a replay commits take the reads made since the step's previous
        /// commit, so a literal written after a capture does not pin.
        /// </summary>
        private void AttributePinnedWrites(NeoWritePlan plan)
        {
            if (pinFrame is not { Mutable: true } frame || pinScope is not { } scope)
                return;
            foreach (var key in plan.Rows.Keys)
                if (key.ownership == NeoValueOwnership.Session)
                    scope.MutableRows.Add(key.id);
            frame.Mutable = false;
        }

        private void NotePinRead(string dependency)
        {
            if (pinFrame is not { Mutable: false } frame || pinScope is not { } scope)
                return;
            string id = DependencyValueId(dependency);
            if (id == scope.RootId)
                return;
            if (IsPinAllocation(id))
            {
                if (scope.MutableRows.Contains(id) || scope.MutableSubtrees.Contains(id))
                    frame.Mutable = true;
                return;
            }
            // Resolving ownership reads rows of its own.
            PinFrame? reading = pinFrame;
            pinFrame = null;
            try
            {
                if (TryGetReplayDefaultRow(id, out _))
                    return;
                if (TryGetValueOwnership(id, out NeoValueOwnership ownership) && ownership != NeoValueOwnership.Asset)
                    frame.Mutable = true;
            }
            finally
            {
                pinFrame = reading;
            }
        }

        private bool IsPinAllocation(string id)
        {
            for (var scope = replayAllocationScope; scope is not null; scope = scope.Parent)
                if (scope.Ids.Contains(id))
                    return true;
            return candidateReplay?.Allocations.ContainsKey(id) == true;
        }

        /// <summary>
        /// The persisted leaves of <paramref name="graph"/> that came from
        /// mutable reads. Session roots never pin: nothing reloads them.
        /// </summary>
        private void CollectPins(VirtualExpansionNode graph, PreparedVirtualExpansion expansion)
        {
            if (pinScope is not { } scope || scope.MutableRows.Count == 0 && scope.MutableSubtrees.Count == 0)
                return;
            if (expansion.RootOwnership is not (NeoValueOwnership.Save or NeoValueOwnership.User))
                return;
            Walk(graph, false);

            void Walk(VirtualExpansionNode node, bool inherited)
            {
                if (node.independentRoot is not null && node != graph)
                    return;
                bool subtree = inherited || scope.MutableSubtrees.Contains(node.row.id);
                if ((subtree || scope.MutableRows.Contains(node.row.id))
                    && IsPinnableLeaf(node) && ValueOwner(expansion, node.virtualId) is { } owner)
                    (owner.Pins ??= new()).Add(node.virtualId);
                foreach (var child in node.classChildren.Values)
                    Walk(child, subtree);
                foreach (var child in node.listChildren)
                    Walk(child, subtree);
                foreach (var child in node.dictionaryChildren.Values)
                    Walk(child, subtree);
            }
        }

        /// <summary>The expansion, outer or partitioned nested, that answers <paramref name="id"/>.</summary>
        private static PreparedVirtualExpansion? ValueOwner(PreparedVirtualExpansion expansion, string id)
        {
            if (expansion.Values.ContainsKey(id))
                return expansion;
            foreach (var nested in expansion.Nested)
                if (ValueOwner(nested, id) is { } owner)
                    return owner;
            return null;
        }

        private static bool IsPinnableLeaf(VirtualExpansionNode node) =>
            node.classChildren.Count == 0 && node.listChildren.Count == 0 && node.dictionaryChildren.Count == 0
            && IsLeafRow(node.row, node.member);

        /// <summary>
        /// Promotes pinned virtual rows into the overlay without a change:
        /// the value is the one consumers already see.
        /// </summary>
        private void InstallPins(PreparedVirtualExpansion expansion)
        {
            string rootId = expansion.Root.id;
            // A recipe change discards the previous materialization, and the
            // pins that held its captures go with it.
            if (installedPins.TryGetValue(rootId, out var previous) && ChangesRecipe(previous.Recipe, expansion.Root))
            {
                foreach (var (ownership, id) in previous.Rows)
                    if (GetWritableStore(ownership).values.Remove(id))
                        SyncValueNode(id);
                installedPins.Remove(rootId);
                previous = null;
            }
            if (expansion.Pins is null)
                return;
            if (previous is null)
                installedPins[rootId] = previous = new InstalledPins((ObjectMemberValue)CloneRowForWrite(expansion.Root));
            foreach (string id in expansion.Pins)
            {
                if (!virtualValues.TryGetValue(id, out MemberValue? row)
                    || !virtualValueOwnership.TryGetValue(id, out NeoValueOwnership ownership))
                    continue;
                if (ownership is not (NeoValueOwnership.Save or NeoValueOwnership.User))
                    continue;
                if (ownership == NeoValueOwnership.User && !IsUserClient)
                    continue;
                if (GetWritableStore(ownership).values.ContainsKey(id))
                    continue;
                StoreWritableValue(ownership, CloneRowForWrite(row));
                previous.Rows.Add((ownership, id));
            }
        }

        // Pins this session installed, by root. A reloaded save cannot tell a
        // pin from an explicit override, so only live pins retire this way.
        private readonly Dictionary<string, InstalledPins> installedPins = new(StringComparer.Ordinal);

        private sealed class InstalledPins
        {
            internal readonly ObjectMemberValue Recipe;
            internal readonly List<(NeoValueOwnership ownership, string id)> Rows = new();
            internal InstalledPins(ObjectMemberValue recipe) => Recipe = recipe;
        }
    }
}
