// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
        // be re-resolved in any evaluation context are kept: scalars,
        // pointers to authored or Save rows, and derived lists of those.
        // Session rows are skipped because a getter that constructs its
        // result must construct again. The one exception is a static getter
        // read as a pattern argument's receiver: only a native call reads the
        // pattern, so its offsets are kept under StaticGetterRowId.
        internal const string StaticGetterRowId = "";

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

        // A class, so a hit hands back one reference: copying a struct of
        // references out of the table costs a GC write barrier per field.
        internal sealed class GetterMemoEntry
        {
            public object? scalar;
            public NeoScript.NSGetterEvaluator.RowReference? row;
            // A derived list result, rebuilt into a fresh array on every hit:
            // each entry is a scalar or the RowReference of a row entry.
            public object?[]? list;
            public Member? listEntryMember;
            // The rows and grid cells the evaluation read, in order. They are
            // the entry's invalidation set, and a hit under dependency capture
            // (an NSProperty compute) reports them as the evaluation would have.
            public List<GetterRead>? reads;
            // Whether reads holds a grid read. Until a recorder holds a grid
            // it drops value reads, so replaying an entry without one into an
            // empty recorder records nothing.
            public bool readsGrid;
            // Every value id the evaluation reported to a dependency capture
            // (an animation segment source, a nested constructor). A hit
            // reports the same ids, so a capture sees exactly what the
            // evaluation would have told it.
            public string[]? valueReads;
            // Set once the memo drops the entry, so a row reference that
            // kept it knows to look the getter up again.
            public bool forgotten;
        }

        internal readonly struct GetterRead
        {
            public readonly INeoTileGridContent? content;
            public readonly UnityEngine.Vector2Int? cell;
            public readonly bool tile;
            public readonly NeoValueOwnership ownership;
            /// <summary>The row id, or a grid read's placement id.</summary>
            public readonly string id;

            public GetterRead(NeoValueOwnership ownership, string id)
            {
                this.ownership = ownership;
                this.id = id;
                content = null;
                cell = null;
                tile = false;
            }

            public GetterRead(INeoTileGridContent content, string placementId, UnityEngine.Vector2Int? cell, bool tile)
            {
                this.content = content;
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

        /// <summary>
        /// Stops the current capture, folding its reads into the enclosing
        /// one, and returns it. The caller hands it to <see cref="MemoizeGetter"/>
        /// or <see cref="RecycleGetterCapture"/>.
        /// </summary>
        internal GetterCaptureFrame EndGetterReadCapture(GetterCaptureFrame previous)
        {
            var capture = new GetterCaptureFrame(getterReadCapture, getterValueReadCapture);
            getterReadCapture = previous.reads;
            getterValueReadCapture = previous.valueReads;
            if (capture.reads is { Count: not 0 })
                previous.reads?.AddRange(capture.reads);
            if (capture.valueReads is { Count: not 0 })
                previous.valueReads?.UnionWith(capture.valueReads);
            return capture;
        }

        /// <summary>Returns a capture no memo entry kept to the pools.</summary>
        internal void RecycleGetterCapture(GetterCaptureFrame capture)
        {
            if (capture.reads is not null)
            {
                capture.reads.Clear();
                readCapturePool.Push(capture.reads);
            }
            if (capture.valueReads is not null)
            {
                capture.valueReads.Clear();
                valueReadCapturePool.Push(capture.valueReads);
            }
        }

        /// <summary>A value-store read, reported to the active dependency captures.</summary>
        // Every row read lands here, mostly with no capture open, so the
        // check inlines into the reader.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void NoteValueRead(string id)
        {
            if (capturedValueReads is not null || getterValueReadCapture is not null || scriptWriteBatch?.Touches(id) == true)
                RecordValueRead(id);
        }

        private void RecordValueRead(string id)
        {
            if (scriptWriteBatch?.Touches(id) == true)
                ObserveScriptWrites(id);
            capturedValueReads?.Add(id);
            getterValueReadCapture?.Add(id);
        }

        /// <summary>
        /// A read of what a held script batch holds pending, reported to the
        /// active captures without committing it.
        /// </summary>
        internal void NotePendingRead(NeoValueOwnership ownership, string id)
        {
            capturedValueReads?.Add(id);
            getterValueReadCapture?.Add(id);
            if (getterReadCapture is { } reads)
                RecordRowRead(reads, ownership, id);
        }

        internal void NoteValueReads(IEnumerable<string> ids)
        {
            capturedValueReads?.UnionWith(ids);
            getterValueReadCapture?.UnionWith(ids);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void NoteRowRead(NeoValueOwnership ownership, string rowId)
        {
            if (scriptWriteBatch?.Touches(rowId) == true)
                ObserveScriptWrites(rowId);
            if (getterReadCapture is { } reads)
                RecordRowRead(reads, ownership, rowId);
        }

        private static void RecordRowRead(List<GetterRead> reads, NeoValueOwnership ownership, string rowId)
        {
            // A member read notes its receiver before each child; a repeat of
            // the previous read adds nothing to the invalidation set.
            if (reads.Count != 0)
            {
                GetterRead previous = reads[reads.Count - 1];
                if (previous.content is null
                    && previous.ownership == ownership
                    && ReferenceEquals(previous.id, rowId))
                    return;
            }
            reads.Add(new GetterRead(ownership, rowId));
        }

        internal void NoteGridRead(INeoTileGridContent content, string placementId, UnityEngine.Vector2Int? cell, bool tile) =>
            getterReadCapture?.Add(new GetterRead(content, placementId, cell, tile));

        /// <summary>Reports a memoized getter's recorded reads as if it had run.</summary>
        // Every memo hit calls this, and usually nothing observes its reads.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReplayGetterReads(GetterMemoEntry entry, NeoScriptGridReads? gridReads)
        {
            if (gridReads is not null
                || getterReadCapture is not null
                || capturedValueReads is not null
                || getterValueReadCapture is not null)
                ReplayObservedGetterReads(entry, gridReads);
        }

        private void ReplayObservedGetterReads(GetterMemoEntry entry, NeoScriptGridReads? gridReads)
        {
            if (entry.valueReads is not null && (capturedValueReads is not null || getterValueReadCapture is not null))
                foreach (string id in entry.valueReads)
                    NoteValueRead(id);
            // Nothing observes the reads outside a capture or grid query.
            if (entry.reads is null || (gridReads is null && getterReadCapture is null))
                return;
            if (gridReads is not null && (entry.readsGrid || gridReads.RecordsGrid))
            {
                for (int i = 0; i < entry.reads.Count; i++)
                {
                    GetterRead read = entry.reads[i];
                    if (read.content is null)
                        gridReads.RecordValue(this, read.ownership, read.id);
                    else
                        gridReads.Record(read.content, read.id, read.cell, read.tile);
                }
            }
            getterReadCapture?.AddRange(entry.reads);
        }

        /// <summary>
        /// False while any read must observe proposed rather than committed
        /// state, or committed rather than a held script batch's pending
        /// state, as a plan commit under one does. Dependency captures (<see cref="CaptureValueReads"/>) and
        /// grid-read capture (<see cref="NeoScriptGridReads"/>) do not
        /// disable memoization: a hit replays the entry's recorded reads.
        /// </summary>
        internal bool CanMemoizeGetters
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return candidateReplay is null
                    && candidateReadPlan is null
                    && replayAllocationScope is null
                    && !isReplayingVirtualInstance
                    && commitsUnderScriptBatch == 0;
            }
        }

        internal GetterMemoEntry? FindMemoizedGetter(GetterMemoKey key)
        {
            if (!getterMemo.TryGetValue(key, out GetterMemoEntry? entry))
                return null;
            if (scriptWriteBatch is null || !entry.readsGrid)
                return entry;
            // The batch forgets the getters that read what it touches, but a
            // grid query reads through indexes it can't name.
            CommitScriptWritesForGrid();
            return getterMemo.TryGetValue(key, out entry) ? entry : null;
        }

        /// <summary>Memoizes a getter's result and returns the entry.</summary>
        internal GetterMemoEntry MemoizeGetter(
            GetterMemoKey key,
            object? scalar,
            NeoScript.NSGetterEvaluator.RowReference? row,
            GetterCaptureFrame capture,
            object?[]? list = null,
            Member? listEntryMember = null)
        {
            var entry = new GetterMemoEntry { scalar = scalar, row = row, list = list, listEntryMember = listEntryMember };
            if (capture.reads is { Count: not 0 })
                entry.reads = capture.reads;
            else if (capture.reads is not null)
                readCapturePool.Push(capture.reads);
            if (capture.valueReads is not null)
            {
                if (capture.valueReads.Count != 0)
                    entry.valueReads = capture.valueReads.ToArray();
                capture.valueReads.Clear();
                valueReadCapturePool.Push(capture.valueReads);
            }
            ForgetMemoizedGetter(key);
            getterMemo[key] = entry;
            IndexMemoDependency(key.rowId, key);
            if (entry.reads is null)
                return entry;
            foreach (GetterRead read in entry.reads)
            {
                if (read.content is null)
                {
                    IndexMemoDependency(read.id, key);
                }
                else
                {
                    entry.readsGrid = true;
                    gridDependentGetterMemoKeys.Add(key);
                }
            }
            return entry;
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
            if (!getterMemo.Remove(key, out GetterMemoEntry? entry))
                return;
            entry.forgotten = true;
            UnindexMemoDependency(key.rowId, key);
            gridDependentGetterMemoKeys.Remove(key);
            List<GetterRead>? reads = entry.reads;
            if (reads is null)
                return;
            foreach (GetterRead read in reads)
                if (read.content is null)
                    UnindexMemoDependency(read.id, key);
            // The entry owned the list; nothing replays a forgotten entry.
            entry.reads = null;
            reads.Clear();
            readCapturePool.Push(reads);
        }

        private void UnindexMemoDependency(string rowId, GetterMemoKey key)
        {
            if (!getterMemoKeysByRow.TryGetValue(rowId, out HashSet<GetterMemoKey>? keys))
                return;
            keys.Remove(key);
            if (keys.Count != 0)
                return;
            getterMemoKeysByRow.Remove(rowId);
            memoKeySetPool.Push(keys);
        }

        // Forgetting mutates the key sets being walked, so each pass copies
        // into one reused list rather than allocating a copy per changed row.
        private readonly List<GetterMemoKey> memoInvalidationScratch = new();

        /// <summary>Drops every memoized getter that read one of the changed rows.</summary>
        private void InvalidateGetterMemoForRows(HashSet<(NeoValueOwnership ownership, string valueId)> changed)
        {
            if (getterMemo.Count == 0)
                return;
            foreach (var (_, valueId) in changed)
                InvalidateGetterMemoForRow(valueId);
        }

        /// <summary>Drops every memoized getter that read one row.</summary>
        internal void InvalidateGetterMemoForRow(string valueId)
        {
            if (getterMemo.Count == 0
                || !getterMemoKeysByRow.TryGetValue(valueId, out HashSet<GetterMemoKey>? keys))
                return;
            ForgetMemoizedGetters(keys);
        }

        /// <summary>Drops every memoized getter that queried a grid.</summary>
        internal void InvalidateGridDependentGetterMemo()
        {
            if (gridDependentGetterMemoKeys.Count == 0)
                return;
            ForgetMemoizedGetters(gridDependentGetterMemoKeys);
        }

        private void ForgetMemoizedGetters(HashSet<GetterMemoKey> keys)
        {
            memoInvalidationScratch.Clear();
            memoInvalidationScratch.AddRange(keys);
            for (int i = 0; i < memoInvalidationScratch.Count; i++)
                ForgetMemoizedGetter(memoInvalidationScratch[i]);
            memoInvalidationScratch.Clear();
        }

        internal void InvalidateGetterMemo()
        {
            foreach (GetterMemoEntry entry in getterMemo.Values)
                entry.forgotten = true;
            getterMemo.Clear();
            getterMemoKeysByRow.Clear();
            gridDependentGetterMemoKeys.Clear();
        }

        private readonly Dictionary<string, bool> worldClassIds = new(StringComparer.Ordinal);

        /// <summary>Whether rows of this class are grid, tile or object placements, so a write to one can change grid queries.</summary>
        private bool IsWorldClass(string? classId)
        {
            if (string.IsNullOrEmpty(classId))
                return false;
            if (!worldClassIds.TryGetValue(classId!, out bool world))
                worldClassIds[classId!] = world = HasWorldKind(classId, "tileGrid") || HasWorldKind(classId, "tile") || HasWorldKind(classId, "object");
            return world;
        }
    }
}
