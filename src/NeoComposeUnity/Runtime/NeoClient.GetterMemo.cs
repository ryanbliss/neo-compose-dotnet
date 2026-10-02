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
        // drops the entries that read a changed row, and a grid change drops
        // the entries that read a cell or placement it names. Only results
        // that can be re-resolved in any evaluation context are kept: scalars,
        // pointers to authored or Save rows, and derived lists of those.
        // Session rows are skipped because a getter that constructs its
        // result must construct again. The one exception is a static getter
        // read as a pattern argument's receiver: only a native call reads the
        // pattern, so its offsets are kept under StaticGetterRowId.
        //
        // The memo is also the getters' only dependency tracking: dropping an
        // entry because something it read changed is what tells the views
        // watching its receiver row that the getter changed (WatchGetters).
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
            // Whether reads holds a grid read. Such an entry answers only
            // while no grid change is pending: a write can move the grid's
            // indexes before the change that names the cells it moved.
            public bool readsGrid;
            // Every value id the evaluation reported to a dependency capture
            // (an animation segment source, a nested constructor). A hit
            // reports the same ids, so a capture sees exactly what the
            // evaluation would have told it.
            public string[]? valueReads;
            // Set once the memo drops the entry, so a row reference that
            // kept it knows to look the getter up again.
            public bool forgotten;
            // A watched getter whose result the memo can't keep still keeps
            // its reads, so a change reaches its watchers. It never hits.
            public bool valueless;
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

        // A grid cell a getter queried, named the way a grid change names it.
        // The caller hashes the grid id once for all the cells it names.
        private readonly struct GridCellRead : IEquatable<GridCellRead>
        {
            private readonly string grid;
            private readonly UnityEngine.Vector2Int cell;
            private readonly bool tile;
            private readonly int hash;

            public GridCellRead(string grid, int gridHash, UnityEngine.Vector2Int cell, bool tile)
            {
                this.grid = grid;
                this.cell = cell;
                this.tile = tile;
                hash = unchecked((cell.GetHashCode() * 31 + gridHash) * 2 + (tile ? 1 : 0));
            }

            public bool Equals(GridCellRead other) =>
                hash == other.hash && cell == other.cell && tile == other.tile && grid == other.grid;
            public override bool Equals(object? obj) => obj is GridCellRead other && Equals(other);
            public override int GetHashCode() => hash;
        }

        private readonly Dictionary<GetterMemoKey, GetterMemoEntry> getterMemo = new();
        private readonly Dictionary<string, HashSet<GetterMemoKey>> getterMemoKeysByRow = new(StringComparer.Ordinal);
        private readonly Dictionary<GridCellRead, HashSet<GetterMemoKey>> getterMemoKeysByGridCell = new();
        // A query reads its receiver's placement, so moving it changes the result.
        private readonly Dictionary<string, HashSet<GetterMemoKey>> getterMemoKeysByPlacement = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<GetterMemoKey>> getterMemoKeysByGrid = new(StringComparer.Ordinal);
        // Open while a write has changed a grid's indexes but not yet
        // published the change that forgets the getters that read them.
        private int gridChangesPending;
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
        internal void ReplayGetterReads(GetterMemoEntry entry)
        {
            if (getterReadCapture is not null
                || capturedValueReads is not null
                || getterValueReadCapture is not null)
                ReplayObservedGetterReads(entry);
        }

        private void ReplayObservedGetterReads(GetterMemoEntry entry)
        {
            if (entry.valueReads is not null && (capturedValueReads is not null || getterValueReadCapture is not null))
                foreach (string id in entry.valueReads)
                    NoteValueRead(id);
            if (entry.reads is not null)
                getterReadCapture?.AddRange(entry.reads);
        }

        /// <summary>
        /// False while any read must observe proposed rather than committed
        /// state, or committed rather than a held script batch's pending
        /// state, as a plan commit under one does. Dependency captures (<see cref="CaptureValueReads"/>)
        /// do not disable memoization: a hit replays the entry's recorded reads.
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
            if (!getterMemo.TryGetValue(key, out GetterMemoEntry? entry) || entry.valueless)
                return null;
            return entry.readsGrid ? CurrentGridReader(key, entry) : entry;
        }

        /// <summary>
        /// Whether an entry a caller kept (a node's or row reference's slot)
        /// still answers without asking the memo: one that read a grid must
        /// ask while a grid change or a held script batch is pending.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HoldsCurrentReads(GetterMemoEntry entry) =>
            !entry.readsGrid || (gridChangesPending == 0 && scriptWriteBatch is null);

        private GetterMemoEntry? CurrentGridReader(GetterMemoKey key, GetterMemoEntry entry)
        {
            // The grid's indexes already changed, but the change that
            // forgets what read them is not yet published.
            if (gridChangesPending != 0)
                return null;
            if (scriptWriteBatch is null)
                return entry;
            // The batch forgets the getters that read what it touches, but a
            // grid query reads through indexes it can't name.
            CommitScriptWritesForGrid();
            return getterMemo.TryGetValue(key, out entry) && !entry.valueless ? entry : null;
        }

        /// <summary>
        /// Memoizes a getter's result and returns the entry, or null while a
        /// pending grid change holds the entry it would replace.
        /// </summary>
        internal GetterMemoEntry? MemoizeGetter(
            GetterMemoKey key,
            object? scalar,
            NeoScript.NSGetterEvaluator.RowReference? row,
            GetterCaptureFrame capture,
            object?[]? list = null,
            Member? listEntryMember = null) =>
            Memoize(key, new GetterMemoEntry { scalar = scalar, row = row, list = list, listEntryMember = listEntryMember }, capture);

        /// <summary>
        /// Keeps the reads of a getter whose result the memo can't keep, for
        /// the views watching its receiver; recycles them when none does.
        /// </summary>
        internal void MemoizeGetterReads(GetterMemoKey key, GetterCaptureFrame capture)
        {
            // A live entry already holds these reads: none of them changed.
            if (!WatchesGetters(key.rowId)
                || (getterMemo.TryGetValue(key, out GetterMemoEntry? kept) && kept.valueless))
                RecycleGetterCapture(capture);
            else
                Memoize(key, new GetterMemoEntry { valueless = true }, capture);
        }

        private GetterMemoEntry? Memoize(GetterMemoKey key, GetterMemoEntry entry, GetterCaptureFrame capture)
        {
            // The pending change must still find the entry that read the old
            // grid, to forget it and tell its watchers.
            if (gridChangesPending != 0 && getterMemo.TryGetValue(key, out GetterMemoEntry? held) && held.readsGrid)
            {
                RecycleGetterCapture(capture);
                return null;
            }
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
            IndexMemoDependency(getterMemoKeysByRow, key.rowId, key);
            if (entry.reads is null)
                return entry;
            string? indexedGrid = null;
            int gridHash = 0;
            foreach (GetterRead read in entry.reads)
            {
                if (read.content is null)
                {
                    IndexMemoDependency(getterMemoKeysByRow, read.id, key);
                    continue;
                }
                entry.readsGrid = true;
                string grid = read.content.Primitive.GridValueId;
                // A query records each cell under one grid.
                if (!ReferenceEquals(grid, indexedGrid))
                {
                    IndexMemoDependency(getterMemoKeysByGrid, grid, key);
                    indexedGrid = grid;
                    gridHash = grid.GetHashCode();
                }
                if (read.cell is UnityEngine.Vector2Int cell)
                    IndexMemoDependency(getterMemoKeysByGridCell, new GridCellRead(grid, gridHash, cell, read.tile), key);
                else
                    IndexMemoDependency(getterMemoKeysByPlacement, read.id, key);
            }
            return entry;
        }

        private void IndexMemoDependency<TRead>(Dictionary<TRead, HashSet<GetterMemoKey>> index, TRead read, GetterMemoKey key)
        {
            if (!index.TryGetValue(read, out HashSet<GetterMemoKey>? keys))
                index[read] = keys = memoKeySetPool.Count != 0
                    ? memoKeySetPool.Pop()
                    : new HashSet<GetterMemoKey>();
            keys.Add(key);
        }

        /// <summary>
        /// Drops a memoized getter without telling its watchers: a read is
        /// replacing it. Returns whether the memo held it.
        /// </summary>
        internal bool ForgetMemoizedGetter(GetterMemoKey key)
        {
            if (!getterMemo.Remove(key, out GetterMemoEntry? entry))
                return false;
            entry.forgotten = true;
            UnindexMemoDependency(getterMemoKeysByRow, key.rowId, key);
            List<GetterRead>? reads = entry.reads;
            if (reads is null)
                return true;
            string? unindexedGrid = null;
            int gridHash = 0;
            foreach (GetterRead read in reads)
            {
                if (read.content is null)
                {
                    UnindexMemoDependency(getterMemoKeysByRow, read.id, key);
                    continue;
                }
                string grid = read.content.Primitive.GridValueId;
                if (!ReferenceEquals(grid, unindexedGrid))
                {
                    UnindexMemoDependency(getterMemoKeysByGrid, grid, key);
                    unindexedGrid = grid;
                    gridHash = grid.GetHashCode();
                }
                if (read.cell is UnityEngine.Vector2Int cell)
                    UnindexMemoDependency(getterMemoKeysByGridCell, new GridCellRead(grid, gridHash, cell, read.tile), key);
                else
                    UnindexMemoDependency(getterMemoKeysByPlacement, read.id, key);
            }
            // The entry owned the list; nothing replays a forgotten entry.
            entry.reads = null;
            reads.Clear();
            readCapturePool.Push(reads);
            return true;
        }

        private void UnindexMemoDependency<TRead>(Dictionary<TRead, HashSet<GetterMemoKey>> index, TRead read, GetterMemoKey key)
        {
            if (!index.TryGetValue(read, out HashSet<GetterMemoKey>? keys))
                return;
            keys.Remove(key);
            if (keys.Count != 0)
                return;
            index.Remove(read);
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
            memoInvalidationScratch.Clear();
            memoInvalidationScratch.AddRange(keys);
            ForgetChangedGetters();
        }

        /// <summary>Drops every memoized getter that read a cell or placement the grid change names.</summary>
        internal void InvalidateGetterMemoForGridChange(NeoTileGridChangedArgs change)
        {
            string grid = change.GridValueId;
            if (getterMemo.Count == 0 || !getterMemoKeysByGrid.ContainsKey(grid))
                return;
            memoInvalidationScratch.Clear();
            int gridHash = grid.GetHashCode();
            IReadOnlyList<NeoObjectLayerChangedArgs> objectLayers = change.ObjectLayers;
            for (int i = 0; i < objectLayers.Count; i++)
            {
                NeoObjectLayerChangedArgs layer = objectLayers[i];
                IReadOnlyList<NeoObjectInstanceId> instances = layer.ChangedInstances;
                for (int j = 0; j < instances.Count; j++)
                    if (getterMemoKeysByPlacement.TryGetValue(instances[j].Value, out HashSet<GetterMemoKey>? keys))
                        memoInvalidationScratch.AddRange(keys);
                CollectCellReaders(grid, gridHash, layer.ChangedCells, tile: false);
            }
            IReadOnlyList<NeoTileLayerChangedArgs> tileLayers = change.TileLayers;
            for (int i = 0; i < tileLayers.Count; i++)
                CollectCellReaders(grid, gridHash, tileLayers[i].ChangedCells, tile: true);
            ForgetChangedGetters();
        }

        private void CollectCellReaders(string grid, int gridHash, IReadOnlyList<UnityEngine.Vector2Int> cells, bool tile)
        {
            for (int i = 0; i < cells.Count; i++)
                if (getterMemoKeysByGridCell.TryGetValue(new GridCellRead(grid, gridHash, cells[i], tile), out HashSet<GetterMemoKey>? keys))
                    memoInvalidationScratch.AddRange(keys);
        }

        /// <summary>Drops every memoized getter that read a grid whose indexes changed without naming what.</summary>
        internal void InvalidateGetterMemoForGrid(string gridValueId)
        {
            if (getterMemo.Count == 0 || !getterMemoKeysByGrid.TryGetValue(gridValueId, out HashSet<GetterMemoKey>? keys))
                return;
            memoInvalidationScratch.Clear();
            memoInvalidationScratch.AddRange(keys);
            ForgetChangedGetters();
        }

        /// <summary>
        /// Forgets the collected keys, a key collected twice once, and tells
        /// the watchers of their receivers.
        /// </summary>
        private void ForgetChangedGetters()
        {
            for (int i = 0; i < memoInvalidationScratch.Count; i++)
            {
                GetterMemoKey key = memoInvalidationScratch[i];
                if (ForgetMemoizedGetter(key) && getterWatchersByRow.Count != 0)
                    QueueGetterChange(key);
            }
            memoInvalidationScratch.Clear();
            FlushGetterChanges();
        }

        /// <summary>Drops every memoized getter, telling the watchers of each.</summary>
        internal void InvalidateGetterMemo()
        {
            foreach (var pair in getterMemo)
            {
                pair.Value.forgotten = true;
                if (getterWatchersByRow.Count != 0)
                    QueueGetterChange(pair.Key);
            }
            getterMemo.Clear();
            getterMemoKeysByRow.Clear();
            getterMemoKeysByGridCell.Clear();
            getterMemoKeysByPlacement.Clear();
            getterMemoKeysByGrid.Clear();
            FlushGetterChanges();
        }

        // The views that hear a getter on the row they show change, by row
        // id, once per subscription. A getter's node is shared by every
        // instance of its class, so a change raises on the view's node
        // instead, naming that instance alone.
        private readonly Dictionary<string, List<NeoMemberClass>> getterWatchersByRow = new(StringComparer.Ordinal);
        private List<(NeoMemberClass node, NeoMemberNSProperty getter)> pendingGetterChanges = new();
        private List<(NeoMemberClass node, NeoMemberNSProperty getter)>? spareGetterChanges;
        private readonly HashSet<(NeoMemberClass node, NeoMemberNSProperty getter)> pendingGetterChangeSet = new();
        private int getterChangeHolds;

        /// <summary>
        /// Raises <paramref name="node"/>'s OnChanged, with the getter's node
        /// as the changed child, whenever the memo drops a getter on
        /// <paramref name="rowId"/> because something it read changed. A
        /// watch hears a getter once per read: the first change drops the
        /// entry, and the next read records it again.
        /// </summary>
        internal IDisposable WatchGetters(string rowId, NeoMemberClass node)
        {
            if (!getterWatchersByRow.TryGetValue(rowId, out List<NeoMemberClass>? nodes))
                getterWatchersByRow[rowId] = nodes = new List<NeoMemberClass>(1);
            nodes.Add(node);
            return new NeoDisposableSubscription(() =>
            {
                if (!getterWatchersByRow.TryGetValue(rowId, out List<NeoMemberClass>? watching))
                    return;
                watching.Remove(node);
                if (watching.Count == 0)
                    getterWatchersByRow.Remove(rowId);
            });
        }

        /// <summary>Whether a view watches the getters on <paramref name="rowId"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool WatchesGetters(string rowId) =>
            getterWatchersByRow.Count != 0 && getterWatchersByRow.ContainsKey(rowId);

        private void QueueGetterChange(GetterMemoKey key)
        {
            if (!getterWatchersByRow.TryGetValue(key.rowId, out List<NeoMemberClass>? nodes))
                return;
            for (int i = 0; i < nodes.Count; i++)
            {
                NeoMemberClass node = nodes[i];
                // The view names the getter by its own child, so a key-filtered
                // listener finds it.
                if (node.FindGetterChild(key.memberId) is { } getter
                    && pendingGetterChangeSet.Add((node, getter)))
                    pendingGetterChanges.Add((node, getter));
            }
        }

        /// <summary>
        /// Raises the queued getter changes. Inside a commit they join its
        /// change batch; a leaf, placement or partition write holds them
        /// until every index it touches is current.
        /// </summary>
        private void FlushGetterChanges()
        {
            if (getterChangeHolds != 0 || pendingGetterChanges.Count == 0)
                return;
            // A listener's own write queues and raises its own changes.
            var draining = pendingGetterChanges;
            pendingGetterChanges = spareGetterChanges ?? new();
            spareGetterChanges = null;
            pendingGetterChangeSet.Clear();
            try
            {
                foreach (var (node, getter) in draining)
                    if (!node.isDisposed)
                        RaiseChanged(node, getter);
            }
            finally
            {
                draining.Clear();
                spareGetterChanges = draining;
            }
        }

        internal void HoldGetterChanges() => getterChangeHolds++;

        internal void ReleaseGetterChanges()
        {
            if (--getterChangeHolds == 0)
                FlushGetterChanges();
        }

        /// <summary>Opens a window in which grid indexes changed ahead of the change that names them.</summary>
        internal void BeginGridChange() => gridChangesPending++;

        internal void EndGridChange() => gridChangesPending--;
    }
}
