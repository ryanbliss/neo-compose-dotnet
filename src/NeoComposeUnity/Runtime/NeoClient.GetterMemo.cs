// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // NSProperty getters are pure functions of their receiver row and the
        // rows and grid cells they read, so a result stays valid until one of
        // those changes. Every memoized evaluation records its reads; a commit
        // drops the entries that read a changed row, and a world-content
        // change drops the entries that queried a grid. Only results that can
        // be re-resolved in any evaluation context are kept: scalars, and
        // pointers to authored or Save rows. Session rows are skipped because
        // a getter that constructs its result must construct again.
        internal readonly struct GetterMemoKey : IEquatable<GetterMemoKey>
        {
            public readonly NeoValueOwnership ownership;
            public readonly string rowId;
            public readonly string memberId;
            public readonly NeoValueOwnership readOwnership;

            public GetterMemoKey(NeoValueOwnership ownership, string rowId, string memberId, NeoValueOwnership readOwnership)
            {
                this.ownership = ownership;
                this.rowId = rowId;
                this.memberId = memberId;
                this.readOwnership = readOwnership;
            }

            public bool Equals(GetterMemoKey other) =>
                ownership == other.ownership && readOwnership == other.readOwnership
                && rowId == other.rowId && memberId == other.memberId;
            public override bool Equals(object? obj) => obj is GetterMemoKey other && Equals(other);
            public override int GetHashCode() => unchecked(
                ((rowId.GetHashCode() * 31 + memberId.GetHashCode()) * 31 + (int)ownership) * 31 + (int)readOwnership);
        }

        internal sealed class GetterMemoEntry
        {
            public object? scalar;
            public NeoScript.NSGetterEvaluator.RowReference? row;
            // The rows and grid cells the evaluation read, in order. They are
            // the entry's invalidation set, and a hit under dependency capture
            // (an NSProperty compute) reports them as the evaluation would have.
            public List<GetterRead>? reads;
        }

        internal readonly struct GetterRead
        {
            public readonly INeoTileGridContent? content;
            public readonly string? placementId;
            public readonly UnityEngine.Vector2Int? cell;
            public readonly bool tile;
            public readonly NeoValueOwnership ownership;
            public readonly string id;

            public GetterRead(NeoValueOwnership ownership, string id)
            {
                this.ownership = ownership;
                this.id = id;
                content = null;
                placementId = null;
                cell = null;
                tile = false;
            }

            public GetterRead(INeoTileGridContent content, string placementId, UnityEngine.Vector2Int? cell, bool tile)
            {
                this.content = content;
                this.placementId = placementId;
                this.cell = cell;
                this.tile = tile;
                ownership = default;
                id = placementId;
            }
        }

        private readonly Dictionary<GetterMemoKey, GetterMemoEntry> getterMemo = new();
        private readonly Dictionary<string, HashSet<GetterMemoKey>> getterMemoKeysByRow = new(StringComparer.Ordinal);
        private readonly HashSet<GetterMemoKey> gridDependentGetterMemoKeys = new();
        private List<GetterRead>? getterReadCapture;

        /// <summary>Starts recording reads for a getter being memoized; returns the enclosing capture.</summary>
        internal List<GetterRead>? BeginGetterReadCapture()
        {
            List<GetterRead>? previous = getterReadCapture;
            getterReadCapture = new List<GetterRead>();
            return previous;
        }

        /// <summary>Stops the current capture, folding its reads into the enclosing one.</summary>
        internal List<GetterRead>? EndGetterReadCapture(List<GetterRead>? previous)
        {
            List<GetterRead>? reads = getterReadCapture;
            getterReadCapture = previous;
            if (reads is not null && reads.Count != 0) previous?.AddRange(reads);
            return reads is null || reads.Count == 0 ? null : reads;
        }

        internal void NoteRowRead(NeoValueOwnership ownership, string rowId) =>
            getterReadCapture?.Add(new GetterRead(ownership, rowId));

        internal void NoteGridRead(INeoTileGridContent content, string placementId, UnityEngine.Vector2Int? cell, bool tile) =>
            getterReadCapture?.Add(new GetterRead(content, placementId, cell, tile));

        /// <summary>Reports a memoized getter's recorded reads as if it had run.</summary>
        internal void ReplayGetterReads(GetterMemoEntry entry, NeoScriptGridReads? gridReads)
        {
            if (entry.reads is null) return;
            foreach (GetterRead read in entry.reads)
            {
                if (read.content is null) gridReads?.RecordValue(this, read.ownership, read.id);
                else gridReads?.Record(read.content, read.placementId!, read.cell, read.tile);
                getterReadCapture?.Add(read);
            }
        }

        /// <summary>
        /// False while any read must observe proposed rather than committed
        /// state, or while replay reads are being captured: a memoized result
        /// would skip that capture. Grid-read capture
        /// (<see cref="NeoScriptGridReads"/>) is replayed from the entry.
        /// </summary>
        internal bool CanMemoizeGetters =>
            candidateReplay is null
            && candidateReadPlan is null
            && replayAllocationScope is null
            && !isReplayingVirtualInstance
            && capturedValueReads is null;

        internal bool TryGetMemoizedGetter(GetterMemoKey key, [NotNullWhen(true)] out GetterMemoEntry? entry) =>
            getterMemo.TryGetValue(key, out entry);

        internal void MemoizeGetter(GetterMemoKey key, GetterMemoEntry entry)
        {
            ForgetMemoizedGetter(key);
            getterMemo[key] = entry;
            IndexMemoDependency(key.rowId, key);
            if (entry.reads is null) return;
            foreach (GetterRead read in entry.reads)
            {
                if (read.content is null) IndexMemoDependency(read.id, key);
                else gridDependentGetterMemoKeys.Add(key);
            }
        }

        private void IndexMemoDependency(string rowId, GetterMemoKey key)
        {
            if (!getterMemoKeysByRow.TryGetValue(rowId, out HashSet<GetterMemoKey>? keys))
                getterMemoKeysByRow[rowId] = keys = new HashSet<GetterMemoKey>();
            keys.Add(key);
        }

        internal void ForgetMemoizedGetter(GetterMemoKey key)
        {
            if (!getterMemo.Remove(key, out GetterMemoEntry? entry)) return;
            UnindexMemoDependency(key.rowId, key);
            gridDependentGetterMemoKeys.Remove(key);
            if (entry.reads is null) return;
            foreach (GetterRead read in entry.reads)
                if (read.content is null) UnindexMemoDependency(read.id, key);
        }

        private void UnindexMemoDependency(string rowId, GetterMemoKey key)
        {
            if (!getterMemoKeysByRow.TryGetValue(rowId, out HashSet<GetterMemoKey>? keys)) return;
            keys.Remove(key);
            if (keys.Count == 0) getterMemoKeysByRow.Remove(rowId);
        }

        /// <summary>Drops every memoized getter that read one of the changed rows.</summary>
        private void InvalidateGetterMemoForRows(IEnumerable<(NeoValueOwnership ownership, string valueId)> changed)
        {
            if (getterMemo.Count == 0) return;
            foreach (var (_, valueId) in changed)
            {
                if (!getterMemoKeysByRow.TryGetValue(valueId, out HashSet<GetterMemoKey>? keys)) continue;
                foreach (GetterMemoKey key in new List<GetterMemoKey>(keys)) ForgetMemoizedGetter(key);
            }
        }

        /// <summary>Drops every memoized getter that queried a grid.</summary>
        internal void InvalidateGridDependentGetterMemo()
        {
            if (gridDependentGetterMemoKeys.Count == 0) return;
            foreach (GetterMemoKey key in new List<GetterMemoKey>(gridDependentGetterMemoKeys)) ForgetMemoizedGetter(key);
        }

        internal void InvalidateGetterMemo()
        {
            getterMemo.Clear();
            getterMemoKeysByRow.Clear();
            gridDependentGetterMemoKeys.Clear();
        }

        private readonly Dictionary<string, bool> worldClassIds = new(StringComparer.Ordinal);

        /// <summary>Whether rows of this class are grid, tile or object placements, so a write to one can change grid queries.</summary>
        private bool IsWorldClass(string? classId)
        {
            if (string.IsNullOrEmpty(classId)) return false;
            if (!worldClassIds.TryGetValue(classId!, out bool world))
                worldClassIds[classId!] = world = HasWorldKind(classId, "tileGrid") || HasWorldKind(classId, "tile") || HasWorldKind(classId, "object");
            return world;
        }
    }
}
