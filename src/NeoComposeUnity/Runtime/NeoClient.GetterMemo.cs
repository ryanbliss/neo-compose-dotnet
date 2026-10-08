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
        // those changes. Constructed results retain their in-memory identity;
        // scalars, stored row references and derived lists keep their existing
        // representation. A constructed result becomes Session rows only when
        // a caller needs rows, never merely because the memo keeps it.
        //
        // The memo is also the getters' only dependency tracking: dropping an
        // entry because something it read changed is what tells the views
        // watching its receiver row that the getter changed (WatchGetters).
        // The same read index holds effects (P97 §3.1): a change queues an
        // effect where it forgets a getter.
        internal const string StaticGetterRowId = "";

        internal enum DependentKind : byte
        {
            Getter, Effect
        }

        internal readonly struct GetterMemoKey : IEquatable<GetterMemoKey>
        {
            public readonly NeoValueOwnership ownership;
            public readonly string rowId;
            public readonly string memberId;
            public readonly NeoValueOwnership readOwnership;
            public readonly DependentKind kind;
            // Hashed once: every memo lookup hashes the key.
            private readonly int hash;

            public GetterMemoKey(
                NeoValueOwnership ownership,
                string rowId,
                string memberId,
                NeoValueOwnership readOwnership,
                DependentKind kind = DependentKind.Getter)
            {
                this.ownership = ownership;
                this.rowId = rowId;
                this.memberId = memberId;
                this.readOwnership = readOwnership;
                this.kind = kind;
                hash = unchecked(
                    (((rowId.GetHashCode() * 31 + memberId.GetHashCode()) * 31 + (int)ownership) * 31 + (int)readOwnership) * 2
                    + (int)kind);
            }

            public bool Equals(GetterMemoKey other) =>
                hash == other.hash && ownership == other.ownership && readOwnership == other.readOwnership
                && kind == other.kind && rowId == other.rowId && memberId == other.memberId;
            public override bool Equals(object? obj) => obj is GetterMemoKey other && Equals(other);
            public override int GetHashCode() => hash;
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
            // The rows and grid cells the evaluation read, each once, in order.
            // They are the entry's invalidation set, and a hit under dependency
            // capture (an NSProperty compute) reports them as the evaluation
            // would have.
            public IdBuffer? reads;
            public GridReadBuffer? gridReads;
            // Every value id the evaluation reported to a dependency capture
            // (an animation segment source, a nested constructor). A hit
            // reports the same ids, so a capture sees exactly what the
            // evaluation would have told it. Null when no capture listened:
            // the entry then answers only reads no capture observes.
            public string[]? valueReads;
            // Set once the memo forgets the entry: something it read changed,
            // or a read is replacing it. A row reference that kept it skips
            // it; a forgotten entry never hits until the memo revives it.
            public bool forgotten;
            // The getter capture this entry's reads were last replayed into.
            public long replayedIn;
            public GetterMemoKey key;
            // A forgotten entry stays in the memo, retired, with its reads
            // and the index lists that hold it. Evaluating the getter again
            // over the same rows and cells revives it, indexing nothing. Grid
            // readers are numbered by generation, so a new set of grid reads
            // leaves the old ones dead in their lists.
            public int rowMemberships;
            public int gridMemberships;
            public int gridGeneration;
            // Out of the memo for good: a change to a row it read dropped a
            // list holding it, or a new entry replaced it.
            public bool abandoned;
            // A watched getter whose result the memo can't keep, or whose
            // read failed, still keeps its reads, so a change reaches its
            // watchers. It never hits.
            public bool valueless;

            // An entry that read a grid answers only while no grid change is
            // pending: a write can move the grid's indexes before the change
            // that names the cells it moved.
            public bool readsGrid => gridReads is not null;

            internal void ForgetResult()
            {
                forgotten = true;
                scalar = null;
                row = null;
                list = null;
                listEntryMember = null;
                valueReads = null;
            }
        }

        // A grid query's read: one cell, or with no cell the receiver's
        // placement. Row reads are bare ids, so the reads nearly every getter
        // records copy a reference rather than this struct.
        internal readonly struct GridRead
        {
            public readonly INeoTileGridContent content;
            public readonly UnityEngine.Vector2Int? cell;
            public readonly bool tile;
            public readonly string placementId;

            public GridRead(INeoTileGridContent content, string placementId, UnityEngine.Vector2Int? cell, bool tile)
            {
                this.content = content;
                this.cell = cell;
                this.tile = tile;
                this.placementId = placementId;
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
        // The entries that read each row, cell, placement and grid. Lists may
        // still hold abandoned entries and past grid generations, dead
        // members the counts say when to sweep.
        private readonly Dictionary<string, List<GetterMemoEntry>> getterMemoEntriesByRow = new(StringComparer.Ordinal);
        private readonly Dictionary<GridCellRead, List<GridMemoReader>> getterMemoEntriesByGridCell = new();
        // A query reads its receiver's placement, so moving it changes the result.
        private readonly Dictionary<string, List<GridMemoReader>> getterMemoEntriesByPlacement = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<GridMemoReader>> getterMemoEntriesByGrid = new(StringComparer.Ordinal);
        private int memoIndexEntries;
        private int deadMemoIndexEntries;
        // Open while a write has changed a grid's indexes but not yet
        // published the change that forgets the getters that read them.
        private int gridChangesPending;
        private IdBuffer? getterReadCapture;
        // Opened by a capture's first grid read.
        private GridReadBuffer? getterGridReadCapture;
        private HashSet<string>? getterConstructedRows;
        internal bool IsCapturingGetterReads => getterReadCapture is not null;
        // Appended as read, duplicates and all: only an entry that can hit
        // replays them, so only it pays to make them distinct.
        private IdBuffer? getterValueReadCapture;
        private readonly IdCompactor distinctIds = new();
        private readonly HashSet<GridRead> distinctGridReads = new(GridReadIdentity.Instance);
        // Every write forgets the getters that read the row and the next
        // evaluation records them again, so the read lists and index lists
        // are recycled instead of reallocated each frame.
        private readonly Stack<IdBuffer> readListPool = new();
        private readonly Stack<GridReadBuffer> gridReadPool = new();
        private readonly Stack<List<GetterMemoEntry>> memoEntryListPool = new();
        private readonly Stack<List<GridMemoReader>> gridMemoReaderListPool = new();

        private readonly struct GridMemoReader
        {
            internal readonly GetterMemoEntry entry;
            internal readonly int generation;

            internal GridMemoReader(GetterMemoEntry entry)
            {
                this.entry = entry;
                generation = entry.gridGeneration;
            }

            internal bool IsLive => generation == entry.gridGeneration;
        }

        internal readonly struct GetterCaptureFrame
        {
            internal readonly IdBuffer? reads;
            internal readonly GridReadBuffer? gridReads;
            internal readonly IdBuffer? valueReads;
            internal readonly HashSet<string>? constructedRows;
            internal readonly long id;

            internal GetterCaptureFrame(IdBuffer? reads, GridReadBuffer? gridReads, IdBuffer? valueReads, HashSet<string>? constructedRows, long id)
            {
                this.reads = reads;
                this.gridReads = gridReads;
                this.valueReads = valueReads;
                this.constructedRows = constructedRows;
                this.id = id;
            }
        }

        // Numbers each capture, so a reader can tell whether a row is
        // already in the open one. Zero while none is open.
        private long getterCaptureId;
        private long lastGetterCaptureId;

        /// <summary>
        /// Starts recording reads for a getter being memoized; returns the
        /// enclosing capture. Value ids are recorded only while a dependency
        /// capture listens: most getters are read with none open. A
        /// reads-only capture leaves them to the enclosing one, for a
        /// dependent that keeps only its reads.
        /// </summary>
        internal GetterCaptureFrame BeginGetterReadCapture(bool readsOnly = false)
        {
            var previous = new GetterCaptureFrame(getterReadCapture, getterGridReadCapture, getterValueReadCapture, getterConstructedRows, getterCaptureId);
            getterCaptureId = ++lastGetterCaptureId;
            getterConstructedRows = null;
            getterGridReadCapture = null;
            getterReadCapture = RentReadList();
            if (!readsOnly && CapturesValueReads)
                getterValueReadCapture = RentReadList();
            return previous;
        }

        private IdBuffer RentReadList() => readListPool.Count != 0 ? readListPool.Pop() : new IdBuffer();

        private void ReturnReadList(IdBuffer ids)
        {
            ids.Clear();
            readListPool.Push(ids);
        }

        private GridReadBuffer OpenGridReadCapture() =>
            getterGridReadCapture ??= gridReadPool.Count != 0 ? gridReadPool.Pop() : new GridReadBuffer();

        private bool CapturesValueReads
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => capturedValueReads is not null || getterValueReadCapture is not null;
        }

        /// <summary>
        /// Stops the current capture, folding its reads into the enclosing
        /// one, and returns it. The caller hands it to <see cref="MemoizeGetter"/>
        /// or <see cref="RecycleGetterCapture"/>.
        /// </summary>
        internal GetterCaptureFrame EndGetterReadCapture(GetterCaptureFrame previous)
        {
            // A capture no dependency capture listened to left the value list as it was.
            IdBuffer? valueReads = ReferenceEquals(getterValueReadCapture, previous.valueReads)
                ? null
                : getterValueReadCapture;
            var capture = new GetterCaptureFrame(getterReadCapture, getterGridReadCapture, valueReads, getterConstructedRows, getterCaptureId);
            // Accumulating into a freshly constructed result does not make
            // that result an input, and the capture keeps each read once.
            // Compact before enclosing captures inherit the reads, without
            // scanning the Session store.
            if (getterConstructedRows is { } constructed)
            {
                if (capture.reads is { } reads)
                    RemoveConstructed(reads, constructed);
                if (capture.valueReads is { } ids)
                    RemoveConstructed(ids, constructed);
            }
            if (capture.reads is not null)
                KeepDistinct(capture.reads);
            capture.gridReads?.KeepDistinct(distinctGridReads);
            getterConstructedRows = previous.constructedRows;
            getterCaptureId = previous.id;
            getterReadCapture = previous.reads;
            getterGridReadCapture = previous.gridReads;
            getterValueReadCapture = previous.valueReads;
            if (capture.reads is { Count: not 0 })
                previous.reads?.AddRange(capture.reads);
            if (capture.gridReads is not null && previous.reads is not null)
                OpenGridReadCapture().AddRange(capture.gridReads);
            if (capture.valueReads is { Count: not 0 })
                previous.valueReads?.AddRange(capture.valueReads);
            return capture;
        }

        private static void RemoveConstructed(IdBuffer ids, HashSet<string> constructed)
        {
            string[] items = ids.items;
            int kept = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                string id = items[i];
                if (!constructed.Contains(id))
                    items[kept++] = id;
            }
            ids.Truncate(kept);
        }

        // Only eager construction needs row bookkeeping. Detached results
        // neither allocate this set nor publish rows when they enter the memo.
        private void NoteGetterConstruction(List<MemberValue> rows)
        {
            if (getterReadCapture is null)
                return;
            getterConstructedRows ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (MemberValue row in rows)
                getterConstructedRows.Add(row.id);
        }

        internal bool HasSharedGetterAncestor(string valueId)
        {
            // Called only for an already-owned source that would otherwise
            // be rejected. Ordinary parentless imports never walk ancestors.
            HashSet<string> visited = RentIdSet();
            try
            {
                do
                {
                    if (ExistingValueNode(valueId)?.sharedGetterResult == true)
                        return true;
                } while (visited.Add(valueId)
                    && TryFindOwnedParent(NeoValueOwnership.Session, valueId, out valueId));
                return false;
            }
            finally
            {
                ReturnIdSet(visited);
            }
        }

        internal void ShareConstructedGetterRow(
            NeoScript.NSGetterEvaluator.RowReference row,
            object? result,
            NeoScript.NSGetterEvaluator.Context ctx,
            GetterCaptureFrame capture)
        {
            if (row.ownership != NeoValueOwnership.Session
                || capture.constructedRows?.Contains(row.valueId) != true)
                return;
            ctx.allocationTracker.MarkEscaped(result, ctx);
            // Protect the returned owned graph, including a returned child,
            // from adoption by an assignment into a stored member.
            var pending = new Stack<(string valueId, Member? member)>();
            TryInferMemberForValueId(row.valueId, out Member? member);
            pending.Push((row.valueId, member));
            while (pending.Count != 0)
            {
                var next = pending.Pop();
                NeoValueNode? node = ValueNode(next.valueId);
                if (node is null || node.sharedGetterResult)
                    continue;
                node.sharedGetterResult = true;
                if (TryGetValue(NeoValueOwnership.Session, next.valueId, out MemberValue? value))
                    foreach (var child in EnumerateOwnedChildLinks(value!, next.member))
                        pending.Push(child);
            }
        }

        /// <summary>Returns a capture no memo entry kept to the pools.</summary>
        internal void RecycleGetterCapture(GetterCaptureFrame capture)
        {
            if (capture.reads is not null)
                ReturnReadList(capture.reads);
            if (capture.gridReads is not null)
                ReturnGridReads(capture.gridReads);
            if (capture.valueReads is not null)
                ReturnReadList(capture.valueReads);
        }

        private void ReturnGridReads(GridReadBuffer reads)
        {
            reads.Clear();
            gridReadPool.Push(reads);
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
        internal void NotePendingRead(string id)
        {
            capturedValueReads?.Add(id);
            getterValueReadCapture?.Add(id);
            if (getterReadCapture is { } reads)
                RecordRowRead(reads, id);
        }

        internal void NoteValueReads(IEnumerable<string> ids)
        {
            capturedValueReads?.UnionWith(ids);
            getterValueReadCapture?.AddRange(ids);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void NoteRowRead(string rowId)
        {
            if (scriptWriteBatch?.Touches(rowId) == true)
                ObserveScriptWrites(rowId);
            if (getterReadCapture is { } reads)
                RecordRowRead(reads, rowId);
        }

        /// <summary>
        /// Notes a read of a row through a holder that stamps the capture it
        /// last entered, so rereads under one capture record nothing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void NoteRowRead(string rowId, ref long capturedIn)
        {
            if (scriptWriteBatch?.Touches(rowId) == true)
                ObserveScriptWrites(rowId);
            if (getterReadCapture is { } reads && capturedIn != getterCaptureId)
            {
                capturedIn = getterCaptureId;
                RecordRowRead(reads, rowId);
            }
        }

        private static void RecordRowRead(IdBuffer reads, string rowId)
        {
            // A member read notes its receiver before each child; a repeat of
            // the previous read adds nothing to the invalidation set.
            int count = reads.Count;
            if (count != 0 && ReferenceEquals(reads.items[count - 1], rowId))
                return;
            reads.Add(rowId);
        }

        internal void NoteGridRead(INeoTileGridContent content, string placementId, UnityEngine.Vector2Int? cell, bool tile)
        {
            if (getterReadCapture is not null)
                OpenGridReadCapture().Add(new GridRead(content, placementId, cell, tile));
        }

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
            if (entry.valueReads is { } ids && (capturedValueReads is not null || getterValueReadCapture is not null))
            {
                // Only a getter capture is open: it takes the ids as they are.
                if (capturedValueReads is null && scriptWriteBatch is null)
                    getterValueReadCapture!.AddRange(ids);
                else
                    foreach (string id in ids)
                        NoteValueRead(id);
            }
            // A capture already holds the reads of an entry it replayed.
            if (getterReadCapture is null || entry.replayedIn == getterCaptureId)
                return;
            entry.replayedIn = getterCaptureId;
            if (entry.reads is not null)
                getterReadCapture.AddRange(entry.reads);
            if (entry.gridReads is not null)
                OpenGridReadCapture().AddRange(entry.gridReads);
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
                    && commitsUnderScriptBatch == 0
                    // P98 §2.4: a departure hook reads its receiver's
                    // departed rows, which the memo's answers never saw.
                    && departedReads != DepartedReads.Prefer;
            }
        }

        internal GetterMemoEntry? FindMemoizedGetter(GetterMemoKey key) => FindMemoizedGetter(key, out _);

        /// <summary>
        /// Also says whether evaluating <paramref name="key"/> again can skip
        /// a capture of its own: a live valueless entry already holds its
        /// reads, so none changed and the result again can't be kept. An
        /// enclosing capture still records the reads as they happen.
        /// </summary>
        internal GetterMemoEntry? FindMemoizedGetter(GetterMemoKey key, out bool holdsValuelessReads)
        {
            holdsValuelessReads = false;
            if (!getterMemo.TryGetValue(key, out GetterMemoEntry? entry) || entry.forgotten)
                return null;
            if (entry.valueless)
            {
                holdsValuelessReads = HoldsCurrentReads(entry);
                return null;
            }
            if (entry.valueReads is null && CapturesValueReads)
                return null;
            return entry.readsGrid ? CurrentGridReader(key, entry) : entry;
        }

        /// <summary>
        /// Whether an entry a caller kept (a node's or row reference's slot)
        /// still answers without asking the memo: one that read a grid must
        /// ask while a grid change or a held script batch is pending, and one
        /// that kept no value ids while a dependency capture listens.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HoldsCurrentReads(GetterMemoEntry entry) =>
            (!entry.readsGrid || (gridChangesPending == 0 && scriptWriteBatch is null))
            && (entry.valueReads is not null || entry.valueless || !CapturesValueReads);

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
            return getterMemo.TryGetValue(key, out entry) && !entry.forgotten && !entry.valueless ? entry : null;
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
            Member? listEntryMember = null)
        {
            GetterMemoEntry? entry = Memoize(key, capture, valueless: false);
            if (entry is null)
                return null;
            entry.scalar = scalar;
            entry.row = row;
            entry.list = list;
            entry.listEntryMember = listEntryMember;
            return entry;
        }

        /// <summary>
        /// Keeps the reads of a getter whose result the memo can't keep, for
        /// the views watching its receiver; recycles them when none does.
        /// </summary>
        internal void MemoizeGetterReads(GetterMemoKey key, GetterCaptureFrame capture)
        {
            // A live entry stays even where it can't vouch for its reads: a
            // pending grid change refuses a new entry and forgets this one,
            // and committing a held batch forgets it if it touched what it read.
            if (!WatchesGetters(key.rowId)
                || (getterMemo.TryGetValue(key, out GetterMemoEntry? kept) && !kept.forgotten && kept.valueless))
                RecycleGetterCapture(capture);
            else
                Memoize(key, capture, valueless: true);
        }

        // Copies out each id's first occurrence; the caller clears ids.
        private string[] Distinct(IdBuffer ids)
        {
            distinctIds.KeepDistinct(ids);
            return ids.ToArray();
        }

        // A capture holds every nested read, a row once per member read
        // through it. The entry keeps each read once, so indexing, forgetting
        // and every replay into an enclosing capture walk only distinct ones.
        private void KeepDistinct(IdBuffer reads) => distinctIds.KeepDistinct(reads);

        /// <summary>
        /// The grid reads a capture recorded, in order. An array rather than
        /// a List, so a scan compares reads in place: copying one out copies
        /// its references, each behind a GC write barrier.
        /// </summary>
        internal sealed class GridReadBuffer
        {
            // A getter's few queries mostly reread the same cells, so a
            // capture this small scans its kept reads rather than hashing.
            private const int ScannedDistinctLimit = 32;

            private GridRead[] items = new GridRead[8];
            private int count;

            internal int Count => count;

            internal ref readonly GridRead this[int index] => ref items[index];

            internal void Add(in GridRead read)
            {
                if (count == items.Length)
                    Array.Resize(ref items, count * 2);
                items[count++] = read;
            }

            internal void AddRange(GridReadBuffer reads)
            {
                int needed = count + reads.count;
                if (needed > items.Length)
                {
                    int size = items.Length * 2;
                    while (size < needed)
                        size *= 2;
                    Array.Resize(ref items, size);
                }
                Array.Copy(reads.items, 0, items, count, reads.count);
                count = needed;
            }

            /// <summary>Compacts each read's first occurrence to the front.</summary>
            internal void KeepDistinct(HashSet<GridRead> seen)
            {
                int kept = 0;
                for (int i = 0; i < count; i++)
                {
                    ref readonly GridRead read = ref items[i];
                    if (count <= ScannedDistinctLimit ? Holds(read, kept) : !seen.Add(read))
                        continue;
                    if (kept != i)
                        items[kept] = read;
                    kept++;
                }
                seen.Clear();
                Array.Clear(items, kept, count - kept);
                count = kept;
            }

            // Whether the first `length` reads hold one equal to read.
            private bool Holds(in GridRead read, int length)
            {
                for (int i = 0; i < length; i++)
                {
                    if (Same(items[i], read))
                        return true;
                }
                return false;
            }

            internal bool SameReads(GridReadBuffer other)
            {
                if (other.count != count)
                    return false;
                for (int i = 0; i < count; i++)
                {
                    if (!Same(items[i], other.items[i]))
                        return false;
                }
                return true;
            }

            internal static bool Same(in GridRead x, in GridRead y) =>
                ReferenceEquals(x.placementId, y.placementId) && ReferenceEquals(x.content, y.content) && x.cell == y.cell && x.tile == y.tile;

            internal void Clear()
            {
                Array.Clear(items, 0, count);
                count = 0;
            }
        }

        /// <summary>
        /// The ids a capture recorded, in order. A sealed buffer rather than a
        /// List: Mono shares List code across reference types and inlines
        /// none of its calls, and every row read appends here.
        /// </summary>
        internal sealed class IdBuffer
        {
            internal string[] items = new string[16];
            private int count;

            internal int Count => count;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void Add(string id)
            {
                if (count == items.Length)
                    Array.Resize(ref items, count * 2);
                items[count++] = id;
            }

            internal void AddRange(IdBuffer ids) => AddRange(ids.items, ids.count);

            internal void AddRange(string[] ids) => AddRange(ids, ids.Length);

            internal void AddRange(IEnumerable<string> ids)
            {
                foreach (string id in ids)
                    Add(id);
            }

            private void AddRange(string[] ids, int length)
            {
                int needed = count + length;
                if (needed > items.Length)
                {
                    int size = items.Length * 2;
                    while (size < needed)
                        size *= 2;
                    Array.Resize(ref items, size);
                }
                Array.Copy(ids, 0, items, count, length);
                count = needed;
            }

            internal bool Contains(string id) => Array.IndexOf(items, id, 0, count) >= 0;

            internal string[] ToArray()
            {
                var ids = new string[count];
                Array.Copy(items, ids, count);
                return ids;
            }

            /// <summary>Keeps the first <paramref name="kept"/> ids.</summary>
            internal void Truncate(int kept)
            {
                Array.Clear(items, kept, count - kept);
                count = kept;
            }

            internal void Clear() => Truncate(0);
        }

        // Compacts each id's first occurrence to the front of a list. Ids
        // compare by reference: rows and values are read through the ids they
        // store, so one row's reads share one string, and nothing hashes its
        // characters. An id read through a copy is merely kept twice. Open
        // addressing over a table at most half full that holds kept
        // positions stamped by pass, so a pass stores no references, which
        // each cost a write barrier, and clears nothing.
        private sealed class IdCompactor
        {
            private int[] positions = new int[256];
            private int[] passes = new int[256];
            private int pass;

            internal void KeepDistinct(IdBuffer buffer)
            {
                string[] ids = buffer.items;
                int count = buffer.Count;
                int size = positions.Length;
                while (size < count * 2)
                    size *= 2;
                if (size != positions.Length)
                {
                    positions = new int[size];
                    passes = new int[size];
                    pass = 0;
                }
                if (++pass == int.MaxValue)
                {
                    Array.Clear(passes, 0, passes.Length);
                    pass = 1;
                }
                int mask = size - 1;
                int kept = 0;
                for (int i = 0; i < count; i++)
                {
                    string id = ids[i];
                    int slot = RuntimeHelpers.GetHashCode(id) & mask;
                    while (passes[slot] == pass && !ReferenceEquals(ids[positions[slot]], id))
                        slot = (slot + 1) & mask;
                    if (passes[slot] == pass)
                        continue;
                    passes[slot] = pass;
                    positions[slot] = kept;
                    if (kept != i)
                        ids[kept] = id;
                    kept++;
                }
                buffer.Truncate(kept);
            }
        }

        private sealed class GridReadIdentity : IEqualityComparer<GridRead>
        {
            internal static readonly GridReadIdentity Instance = new();

            public bool Equals(GridRead x, GridRead y) => GridReadBuffer.Same(x, y);

            public int GetHashCode(GridRead read) => unchecked(
                RuntimeHelpers.GetHashCode(read.placementId) * 31 + (read.cell?.GetHashCode() ?? 0));
        }

        // Returns the entry for the caller to fill with the result.
        private GetterMemoEntry? Memoize(GetterMemoKey key, GetterCaptureFrame capture, bool valueless)
        {
            // P98 §2.4: the memo is keyed by row id, so an answer read from
            // a departed row would outlive it.
            if (departedNodes.Count != 0)
            {
                RecycleGetterCapture(capture);
                return null;
            }
            getterMemo.TryGetValue(key, out GetterMemoEntry? entry);
            // The pending change must still find the entry that read the old
            // grid, to forget it and tell its watchers.
            if (gridChangesPending != 0 && entry is { forgotten: false, readsGrid: true })
            {
                RecycleGetterCapture(capture);
                return null;
            }
            string[]? valueReads = null;
            if (capture.valueReads is not null)
            {
                // A valueless entry never hits, so nothing replays its ids.
                if (!valueless)
                    valueReads = capture.valueReads.Count != 0 ? Distinct(capture.valueReads) : Array.Empty<string>();
                ReturnReadList(capture.valueReads);
            }
            // Whoever kept an entry that held a value answers from it, so it
            // never comes back valueless.
            if (valueless && entry is { valueless: false })
            {
                DropDependent(entry);
                entry = null;
            }
            GetterMemoEntry dependent = IndexDependent(key, entry, capture);
            if (!ReferenceEquals(dependent, entry))
                getterMemo[key] = dependent;
            dependent.valueless = valueless;
            dependent.valueReads = valueReads;
            return dependent;
        }

        /// <summary>
        /// Indexes a capture's reads for <paramref name="key"/>, reviving
        /// <paramref name="previous"/> when it read the same rows, and
        /// returns the live entry. Takes the capture's read lists.
        /// </summary>
        private GetterMemoEntry IndexDependent(GetterMemoKey key, GetterMemoEntry? previous, GetterCaptureFrame capture)
        {
            IdBuffer? reads = capture.reads;
            if (reads is { Count: 0 })
            {
                ReturnReadList(reads);
                reads = null;
            }
            GetterMemoEntry entry;
            if (previous is not null)
                ForgetMemoEntry(previous);
            if (previous is not null && SameReads(previous.reads, reads))
            {
                // The row lists still hold the entry.
                entry = previous;
                entry.forgotten = false;
                if (reads is not null)
                    ReturnReadList(reads);
            }
            else
            {
                if (previous is not null)
                    AbandonMemoEntry(previous);
                entry = new GetterMemoEntry { key = key, reads = reads };
                // An effect depends on what it read alone (P97 §3.1).
                if (key.kind == DependentKind.Getter)
                    IndexRowReader(key.rowId, entry);
                if (reads is not null)
                {
                    string[] ids = reads.items;
                    for (int i = 0; i < reads.Count; i++)
                        IndexRowReader(ids[i], entry);
                }
            }
            if (ReferenceEquals(entry, previous) && SameGridReads(entry.gridReads, capture.gridReads))
            {
                // The grid lists still hold the entry too.
                if (capture.gridReads is not null)
                    ReturnGridReads(capture.gridReads);
            }
            else
            {
                DropGridReaders(entry);
                entry.gridReads = capture.gridReads;
                if (entry.gridReads is not null)
                    IndexGridReads(entry);
            }
            if (deadMemoIndexEntries > MinDeadMemoIndexSweep && deadMemoIndexEntries * 2 > memoIndexEntries)
                SweepMemoIndexes();
            return entry;
        }

        /// <summary>Takes an entry out of every index for good.</summary>
        private void DropDependent(GetterMemoEntry entry)
        {
            ForgetMemoEntry(entry);
            AbandonMemoEntry(entry);
        }

        private static bool SameReads(IdBuffer? previous, IdBuffer? reads)
        {
            if (previous is null || reads is null)
                return previous is null && reads is null;
            int count = reads.Count;
            if (previous.Count != count)
                return false;
            // One row's reads share one id instance, so an evaluation over
            // the same rows mostly records the same references in the same
            // order. A rewritten row is read through its new row's id, an
            // equal string.
            string[] before = previous.items;
            string[] after = reads.items;
            for (int i = 0; i < count; i++)
                if (!string.Equals(before[i], after[i]))
                    return false;
            return true;
        }

        private static bool SameGridReads(GridReadBuffer? previous, GridReadBuffer? reads)
        {
            if (previous is null || reads is null)
                return previous is null && reads is null;
            return previous.SameReads(reads);
        }

        private void IndexRowReader(string id, GetterMemoEntry entry)
        {
            if (!getterMemoEntriesByRow.TryGetValue(id, out List<GetterMemoEntry>? entries))
                getterMemoEntriesByRow[id] = entries = memoEntryListPool.Count != 0
                    ? memoEntryListPool.Pop()
                    : new List<GetterMemoEntry>();
            entries.Add(entry);
            entry.rowMemberships++;
            memoIndexEntries++;
        }

        private void IndexGridReads(GetterMemoEntry entry)
        {
            var reader = new GridMemoReader(entry);
            string? indexedGrid = null;
            int gridHash = 0;
            GridReadBuffer reads = entry.gridReads!;
            for (int i = 0; i < reads.Count; i++)
            {
                ref readonly GridRead read = ref reads[i];
                string grid = read.content.Primitive.GridValueId;
                // A query records each cell under one grid.
                if (!ReferenceEquals(grid, indexedGrid))
                {
                    IndexGridReader(getterMemoEntriesByGrid, grid, reader);
                    indexedGrid = grid;
                    gridHash = grid.GetHashCode();
                }
                if (read.cell is UnityEngine.Vector2Int cell)
                    IndexGridReader(getterMemoEntriesByGridCell, new GridCellRead(grid, gridHash, cell, read.tile), reader);
                else
                    IndexGridReader(getterMemoEntriesByPlacement, read.placementId, reader);
            }
        }

        private void IndexGridReader<TRead>(Dictionary<TRead, List<GridMemoReader>> index, TRead read, GridMemoReader reader)
        {
            if (!index.TryGetValue(read, out List<GridMemoReader>? readers))
                index[read] = readers = gridMemoReaderListPool.Count != 0
                    ? gridMemoReaderListPool.Pop()
                    : new List<GridMemoReader>();
            readers.Add(reader);
            reader.entry.gridMemberships++;
            memoIndexEntries++;
        }

        /// <summary>
        /// Drops a memoized getter without telling its watchers: a read is
        /// replacing it. Returns whether the memo held it.
        /// </summary>
        internal bool ForgetMemoizedGetter(GetterMemoKey key) =>
            getterMemo.TryGetValue(key, out GetterMemoEntry? entry) && ForgetMemoEntry(entry);

        // Retires the entry: the index lists keep it, to revive or until a
        // change to a row it read abandons it, so forgetting costs the same
        // however many rows and cells the getter read.
        private bool ForgetMemoEntry(GetterMemoEntry entry)
        {
            if (entry.forgotten)
                return false;
            entry.ForgetResult();
            // A revived entry's reads may differ from those a capture still
            // open took from it.
            entry.replayedIn = 0;
            // Nothing replays a forgotten entry. It keeps its reads to compare
            // with the next evaluation's.
            return true;
        }

        // A forgotten entry the memo will not revive; the caller removes or
        // replaces its memo slot. Its readers die.
        private void AbandonMemoEntry(GetterMemoEntry entry)
        {
            entry.abandoned = true;
            deadMemoIndexEntries += entry.rowMemberships;
            if (entry.reads is { } reads)
            {
                entry.reads = null;
                ReturnReadList(reads);
            }
            DropGridReaders(entry);
        }

        // Kills the entry's grid readers where their lists hold them.
        private void DropGridReaders(GetterMemoEntry entry)
        {
            if (entry.gridReads is not { } gridReads)
                return;
            entry.gridReads = null;
            deadMemoIndexEntries += entry.gridMemberships;
            entry.gridMemberships = 0;
            entry.gridGeneration++;
            ReturnGridReads(gridReads);
        }

        private const int MinDeadMemoIndexSweep = 4096;

        // Once dead readers outnumber live ones, every index list drops them,
        // so the indexes stay within twice their live size and a sweep costs
        // no more than the changes that called for it. A retired entry is not
        // dead: it costs what it did live, and a later read revives it.
        private void SweepMemoIndexes()
        {
            memoIndexEntries = 0;
            deadMemoIndexEntries = 0;
            SweepMemoIndex(getterMemoEntriesByRow, entries => entries.RemoveAll(entry => entry.abandoned), memoEntryListPool);
            SweepMemoIndex(getterMemoEntriesByGridCell, readers => readers.RemoveAll(reader => !reader.IsLive), gridMemoReaderListPool);
            SweepMemoIndex(getterMemoEntriesByPlacement, readers => readers.RemoveAll(reader => !reader.IsLive), gridMemoReaderListPool);
            SweepMemoIndex(getterMemoEntriesByGrid, readers => readers.RemoveAll(reader => !reader.IsLive), gridMemoReaderListPool);
        }

        private void SweepMemoIndex<TRead, TReader>(
            Dictionary<TRead, List<TReader>> index,
            Func<List<TReader>, int> removeDead,
            Stack<List<TReader>> pool)
        {
            List<TRead>? emptied = null;
            foreach (var pair in index)
            {
                List<TReader> readers = pair.Value;
                removeDead(readers);
                memoIndexEntries += readers.Count;
                if (readers.Count == 0)
                    (emptied ??= new List<TRead>()).Add(pair.Key);
            }
            if (emptied is null)
                return;
            foreach (TRead read in emptied)
            {
                pool.Push(index[read]);
                index.Remove(read);
            }
        }

        // Forgetting may re-enter the memo through a watcher, so each pass
        // collects the readers first, into a reused list.
        private readonly List<GetterMemoEntry> memoInvalidationScratch = new();

        /// <summary>Drops every memoized getter that read one of the changed rows.</summary>
        private void InvalidateGetterMemoForRows(HashSet<(NeoValueOwnership ownership, string valueId)> changed)
        {
            if (!HasReadDependents)
                return;
            foreach (var (_, valueId) in changed)
                InvalidateGetterMemoForRow(valueId);
        }

        /// <summary>Drops every memoized getter that read one row.</summary>
        internal void InvalidateGetterMemoForRow(string valueId)
        {
            if (!HasReadDependents || !getterMemoEntriesByRow.Remove(valueId, out List<GetterMemoEntry>? entries))
                return;
            // A getter that read the row leaves the memo for good, retired or
            // not: no list would tell a revived entry the row changed again.
            // An effect keeps listening to its last run's reads until it runs
            // again, so only effects stay listed.
            int kept = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                GetterMemoEntry entry = entries[i];
                if (entry.abandoned)
                    continue;
                if (entry.key.kind == DependentKind.Effect)
                {
                    memoInvalidationScratch.Add(entry);
                    entries[kept++] = entry;
                    continue;
                }
                if (!entry.forgotten)
                    memoInvalidationScratch.Add(entry);
                getterMemo.Remove(entry.key);
                AbandonMemoEntry(entry);
            }
            DropDeadReaders(entries.Count - kept);
            KeepListed(getterMemoEntriesByRow, valueId, entries, kept, memoEntryListPool);
            ForgetChangedGetters();
        }

        /// <summary>Drops every memoized getter that read a cell or placement the grid change names.</summary>
        internal void InvalidateGetterMemoForGridChange(NeoTileGridChangedArgs change)
        {
            string grid = change.GridValueId;
            if (!HasReadDependents || !getterMemoEntriesByGrid.ContainsKey(grid))
                return;
            int gridHash = grid.GetHashCode();
            IReadOnlyList<NeoObjectLayerChangedArgs> objectLayers = change.ObjectLayers;
            for (int i = 0; i < objectLayers.Count; i++)
            {
                NeoObjectLayerChangedArgs layer = objectLayers[i];
                IReadOnlyList<NeoObjectInstanceId> instances = layer.ChangedInstances;
                for (int j = 0; j < instances.Count; j++)
                    CollectGridReaders(getterMemoEntriesByPlacement, instances[j].Value);
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
                CollectGridReaders(getterMemoEntriesByGridCell, new GridCellRead(grid, gridHash, cells[i], tile));
        }

        /// <summary>Drops every memoized getter that read a grid whose indexes changed without naming what.</summary>
        internal void InvalidateGetterMemoForGrid(string gridValueId)
        {
            if (!HasReadDependents)
                return;
            CollectGridReaders(getterMemoEntriesByGrid, gridValueId);
            ForgetChangedGetters();
        }

        private bool HasReadDependents
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return getterMemo.Count != 0 || effectsByKey.Count != 0;
            }
        }

        private void CollectGridReaders<TRead>(Dictionary<TRead, List<GridMemoReader>> index, TRead read)
        {
            if (!index.Remove(read, out List<GridMemoReader>? readers))
                return;
            // Every live reader stays listed: a forgotten getter that reads
            // the same cells again revives, and the next change must find it.
            int kept = 0;
            for (int i = 0; i < readers.Count; i++)
            {
                GridMemoReader reader = readers[i];
                if (!reader.IsLive)
                    continue;
                memoInvalidationScratch.Add(reader.entry);
                readers[kept++] = reader;
            }
            DropDeadReaders(readers.Count - kept);
            KeepListed(index, read, readers, kept, gridMemoReaderListPool);
        }

        // Lists the first kept readers again under the key, or recycles the list.
        private static void KeepListed<TRead, TReader>(
            Dictionary<TRead, List<TReader>> index,
            TRead read,
            List<TReader> readers,
            int kept,
            Stack<List<TReader>> pool)
        {
            if (kept != 0)
            {
                readers.RemoveRange(kept, readers.Count - kept);
                index.Add(read, readers);
                return;
            }
            readers.Clear();
            pool.Push(readers);
        }

        /// <summary>
        /// Forgets the collected entries, one collected twice once, and tells
        /// the watchers of their receivers; queues the collected effects.
        /// </summary>
        private void ForgetChangedGetters()
        {
            // Released where nothing else holds them: the effects run, then
            // the watchers hear the settled getters.
            HoldGetterChanges();
            try
            {
                for (int i = 0; i < memoInvalidationScratch.Count; i++)
                {
                    GetterMemoEntry entry = memoInvalidationScratch[i];
                    if (entry.key.kind == DependentKind.Effect)
                        QueueEffect(entry.key);
                    else if (ForgetMemoEntry(entry) && getterWatchersByRow.Count != 0)
                        QueueGetterChange(entry.key);
                }
                memoInvalidationScratch.Clear();
            }
            finally
            {
                ReleaseGetterChanges();
            }
        }

        private void DropDeadReaders(int count)
        {
            memoIndexEntries -= count;
            deadMemoIndexEntries -= count;
        }

        /// <summary>
        /// Drops every memoized getter, telling the watchers of each. Effects
        /// keep their reads: the caller names what changed for them.
        /// </summary>
        internal void InvalidateGetterMemo()
        {
            HoldGetterChanges();
            try
            {
                foreach (GetterMemoEntry entry in getterMemo.Values)
                {
                    if (ForgetMemoEntry(entry) && getterWatchersByRow.Count != 0)
                        QueueGetterChange(entry.key);
                    AbandonMemoEntry(entry);
                }
                getterMemo.Clear();
                if (effectsByKey.Count != 0)
                    return;
                getterMemoEntriesByRow.Clear();
                getterMemoEntriesByGridCell.Clear();
                getterMemoEntriesByPlacement.Clear();
                getterMemoEntriesByGrid.Clear();
                memoIndexEntries = 0;
                deadMemoIndexEntries = 0;
            }
            finally
            {
                ReleaseGetterChanges();
            }
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

        // A dialogue never starts or ends its turn inside NeoScript; one
        // requested or ended there waits for the outermost exit (P105 §1.3).
        private bool dialogueTurnPending;

        internal void RunDialogueTurn()
        {
            if (getterChangeHolds != 0)
            {
                dialogueTurnPending = true;
                return;
            }
            DialoguesApi?.RunTurn();
        }

        /// <summary>
        /// The outermost release runs the pending effects, still held so
        /// their own writes join this boundary, then raises the getter changes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReleaseGetterChanges()
        {
            // An idle boundary has nothing to drain or raise.
            if (getterChangeHolds > 1
                || (pendingGetterChanges.Count == 0 && !EffectsPending && !userChangesPending && !dialogueTurnPending))
                getterChangeHolds--;
            else
                ReleaseOutermostGetterChanges();
        }

        private void ReleaseOutermostGetterChanges()
        {
            try
            {
                if (getterChangeHolds == 1 && EffectsPending)
                    DrainEffects();
            }
            finally
            {
                if (--getterChangeHolds == 0)
                {
                    try
                    {
                        FlushGetterChanges();
                    }
                    finally
                    {
                        try
                        {
                            FlushUserChanges();
                        }
                        finally
                        {
                            // A throwing listener must not strand a queued dialogue.
                            if (dialogueTurnPending)
                            {
                                dialogueTurnPending = false;
                                DialoguesApi?.RunTurn();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Opens a window in which grid indexes changed ahead of the change that names them.</summary>
        internal void BeginGridChange() => gridChangesPending++;

        internal void EndGridChange() => gridChangesPending--;
    }
}
