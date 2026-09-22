// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
            // Every value id the evaluation reported to a dependency capture
            // (an animation segment source, a nested constructor). A hit
            // reports the same ids, so a capture sees exactly what the
            // evaluation would have told it.
            public string[]? valueReads;
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
        private HashSet<string>? getterValueReadCapture;
        private readonly Stack<HashSet<string>> valueReadCapturePool = new();
        // Every write forgets the getters that read the row and the next
        // evaluation records them again, so the read lists and per-row key
        // sets are recycled instead of reallocated each frame.
        private readonly Stack<List<GetterRead>> readCapturePool = new();
        private readonly Stack<HashSet<GetterMemoKey>> memoKeySetPool = new();

        internal readonly struct GetterCaptureFrame
        {
            internal readonly List<GetterRead>? reads;
            internal readonly HashSet<string>? valueReads;

            internal GetterCaptureFrame(List<GetterRead>? reads, HashSet<string>? valueReads)
            {
                this.reads = reads;
                this.valueReads = valueReads;
            }
        }

        /// <summary>Starts recording reads for a getter being memoized; returns the enclosing capture.</summary>
        internal GetterCaptureFrame BeginGetterReadCapture()
        {
            var previous = new GetterCaptureFrame(getterReadCapture, getterValueReadCapture);
            getterReadCapture = readCapturePool.Count != 0
                ? readCapturePool.Pop()
                : new List<GetterRead>();
            getterValueReadCapture = valueReadCapturePool.Count != 0
                ? valueReadCapturePool.Pop()
                : new HashSet<string>(StringComparer.Ordinal);
            return previous;
        }

        /// <summary>Stops the current capture, folding its reads into the enclosing one.</summary>
        internal List<GetterRead>? EndGetterReadCapture(GetterCaptureFrame previous, out string[]? valueReads)
        {
            List<GetterRead>? reads = getterReadCapture;
            HashSet<string> values = getterValueReadCapture!;
            getterReadCapture = previous.reads;
            getterValueReadCapture = previous.valueReads;
            if (reads is not null && reads.Count != 0) previous.reads?.AddRange(reads);
            valueReads = values.Count == 0 ? null : values.ToArray();
            previous.valueReads?.UnionWith(values);
            values.Clear();
            valueReadCapturePool.Push(values);
            if (reads is null) return null;
            if (reads.Count != 0) return reads;
            readCapturePool.Push(reads);
            return null;
        }

        /// <summary>A value-store read, reported to the active dependency captures.</summary>
        internal void NoteValueRead(string id)
        {
            capturedValueReads?.Add(id);
            getterValueReadCapture?.Add(id);
        }

        internal void NoteValueReads(IEnumerable<string> ids)
        {
            capturedValueReads?.UnionWith(ids);
            getterValueReadCapture?.UnionWith(ids);
        }

        internal void NoteRowRead(NeoValueOwnership ownership, string rowId) =>
            getterReadCapture?.Add(new GetterRead(ownership, rowId));

        internal void NoteGridRead(INeoTileGridContent content, string placementId, UnityEngine.Vector2Int? cell, bool tile) =>
            getterReadCapture?.Add(new GetterRead(content, placementId, cell, tile));

        /// <summary>Reports a memoized getter's recorded reads as if it had run.</summary>
        internal void ReplayGetterReads(GetterMemoEntry entry, NeoScriptGridReads? gridReads)
        {
            if (entry.valueReads is not null) NoteValueReads(entry.valueReads);
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
        /// state. Dependency captures (<see cref="CaptureValueReads"/>) and
        /// grid-read capture (<see cref="NeoScriptGridReads"/>) do not
        /// disable memoization: a hit replays the entry's recorded reads.
        /// </summary>
        internal bool CanMemoizeGetters =>
            candidateReplay is null
            && candidateReadPlan is null
            && replayAllocationScope is null
            && !isReplayingVirtualInstance;

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
                getterMemoKeysByRow[rowId] = keys = memoKeySetPool.Count != 0
                    ? memoKeySetPool.Pop()
                    : new HashSet<GetterMemoKey>();
            keys.Add(key);
        }

        internal void ForgetMemoizedGetter(GetterMemoKey key)
        {
            if (!getterMemo.Remove(key, out GetterMemoEntry? entry)) return;
            UnindexMemoDependency(key.rowId, key);
            gridDependentGetterMemoKeys.Remove(key);
            List<GetterRead>? reads = entry.reads;
            if (reads is null) return;
            foreach (GetterRead read in reads)
                if (read.content is null) UnindexMemoDependency(read.id, key);
            // The entry owned the list; nothing replays a forgotten entry.
            entry.reads = null;
            reads.Clear();
            readCapturePool.Push(reads);
        }

        private void UnindexMemoDependency(string rowId, GetterMemoKey key)
        {
            if (!getterMemoKeysByRow.TryGetValue(rowId, out HashSet<GetterMemoKey>? keys)) return;
            keys.Remove(key);
            if (keys.Count != 0) return;
            getterMemoKeysByRow.Remove(rowId);
            memoKeySetPool.Push(keys);
        }

        // Forgetting mutates the key sets being walked, so each pass copies
        // into one reused list rather than allocating a copy per changed row.
        private readonly List<GetterMemoKey> memoInvalidationScratch = new();

        /// <summary>Drops every memoized getter that read one of the changed rows.</summary>
        private void InvalidateGetterMemoForRows(IEnumerable<(NeoValueOwnership ownership, string valueId)> changed)
        {
            if (getterMemo.Count == 0) return;
            foreach (var (_, valueId) in changed) InvalidateGetterMemoForRow(valueId);
        }

        /// <summary>Drops every memoized getter that read one row.</summary>
        private void InvalidateGetterMemoForRow(string valueId)
        {
            if (getterMemo.Count == 0
                || !getterMemoKeysByRow.TryGetValue(valueId, out HashSet<GetterMemoKey>? keys)) return;
            ForgetMemoizedGetters(keys);
        }

        /// <summary>Drops every memoized getter that queried a grid.</summary>
        internal void InvalidateGridDependentGetterMemo()
        {
            if (gridDependentGetterMemoKeys.Count == 0) return;
            ForgetMemoizedGetters(gridDependentGetterMemoKeys);
        }

        private void ForgetMemoizedGetters(HashSet<GetterMemoKey> keys)
        {
            memoInvalidationScratch.Clear();
            memoInvalidationScratch.AddRange(keys);
            for (int i = 0; i < memoInvalidationScratch.Count; i++) ForgetMemoizedGetter(memoInvalidationScratch[i]);
            memoInvalidationScratch.Clear();
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
