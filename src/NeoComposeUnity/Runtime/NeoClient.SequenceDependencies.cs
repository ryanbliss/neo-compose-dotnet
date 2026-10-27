// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private object? sequenceSchema;
        private readonly HashSet<string> sequenceInterfaces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, SequenceGeneration>> sequenceGenerations = new(StringComparer.Ordinal);
        internal IReadOnlyList<SequenceGeneration>? ActiveSequenceOwners;

        internal bool IsSequenceType(TypeInfo type)
        {
            if (type is not InterfaceTypeInfo protocol)
                return false;
            if (!ReferenceEquals(sequenceSchema, SchemaResolution))
            {
                sequenceSchema = SchemaResolution;
                sequenceInterfaces.Clear();
                var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var declaration in data.interfaces.Values)
                    foreach (InterfaceConstruction parentConstruction in declaration.extendsInterfaces ?? new List<InterfaceConstruction>())
                    {
                        string parent = parentConstruction.interfaceId;
                        if (!children.TryGetValue(parent, out var descendants))
                            children[parent] = descendants = new();
                        descendants.Add(declaration.id);
                    }
                var queue = new Queue<string>();
                queue.Enqueue(NeoSequenceContract.Enumerable);
                queue.Enqueue(NeoSequenceContract.Enumerator);
                while (queue.Count != 0)
                {
                    string id = queue.Dequeue();
                    if (!sequenceInterfaces.Add(id) || !children.TryGetValue(id, out var descendants))
                        continue;
                    foreach (string child in descendants)
                        queue.Enqueue(child);
                }
            }
            return sequenceInterfaces.Contains(protocol.interfaceId);
        }

        // The group itself is the generation's identity. It is never revived,
        // even when the existing memo reuses an entry with identical reads.
        internal sealed class SequenceGeneration
        {
            internal readonly string Row;
            internal readonly string Getter;
            internal readonly HashSet<GetterMemoEntry> Entries = new();
            internal bool Live = true;
            internal SequenceGeneration(string row, string getter)
            {
                Row = row;
                Getter = getter;
            }
        }

        internal SequenceGeneration? MemoizeSequenceReads(GetterMemoKey key, GetterCaptureFrame capture)
        {
            string canonical = TryGetMember(key.memberId, out Member? member) ? CanonicalListenerMemberId(member) : key.memberId;
            if (!WatchesGetters(key.rowId) || !CanMemoizeGetters || departedNodes.Count != 0 || gridChangesPending != 0)
            {
                RetireSequenceGetter(key.rowId, canonical);
                RecycleGetterCapture(capture);
                return null;
            }
            if (!sequenceGenerations.TryGetValue(key.rowId, out var groups))
                sequenceGenerations[key.rowId] = groups = new(StringComparer.Ordinal);
            if (!groups.TryGetValue(canonical, out var group))
                groups[canonical] = group = new SequenceGeneration(key.rowId, canonical);
            if (getterMemo.TryGetValue(key, out var existing) && !existing.forgotten && ReferenceEquals(existing.sequenceGeneration, group))
            {
                AppendCapture(existing, capture);
                RecycleGetterCapture(capture);
            }
            else
            {
                GetterMemoEntry? entry = Memoize(key, capture, valueless: true);
                if (entry is null)
                    return null;
                entry.sequenceGeneration = group;
                group.Entries.Add(entry);
            }
            return group;
        }

        private void AppendCapture(GetterMemoEntry entry, GetterCaptureFrame capture)
        {
            if (capture.reads is { } reads)
                for (int i = 0; i < reads.Count; i++)
                    AppendSequenceRow(entry, reads.items[i]);
            if (capture.gridReads is { } grid)
                for (int i = 0; i < grid.Count; i++)
                    AppendSequenceGrid(entry, grid[i]);
        }

        private bool CanAppendSequence(SequenceGeneration group)
        {
            if (!group.Live)
                return false;
            if (WatchesGetters(group.Row) && CanMemoizeGetters && departedNodes.Count == 0 && gridChangesPending == 0)
                return true;
            RetireSequenceGeneration(group);
            return false;
        }

        private void NoteSequenceRow(string id)
        {
            if (ActiveSequenceOwners is not { } owners)
                return;
            for (int i = 0; i < owners.Count; i++)
            {
                SequenceGeneration group = owners[i];
                if (!CanAppendSequence(group))
                    continue;
                foreach (GetterMemoEntry entry in group.Entries)
                    AppendSequenceRow(entry, id);
            }
        }

        private void NoteSequenceGrid(in GridRead read)
        {
            if (ActiveSequenceOwners is not { } owners)
                return;
            for (int i = 0; i < owners.Count; i++)
            {
                SequenceGeneration group = owners[i];
                if (!CanAppendSequence(group))
                    continue;
                foreach (GetterMemoEntry entry in group.Entries)
                    AppendSequenceGrid(entry, read);
            }
        }

        private void AppendSequenceRow(GetterMemoEntry entry, string id)
        {
            if (entry.sequenceRows is null)
            {
                entry.sequenceRows = new(StringComparer.Ordinal) { entry.key.rowId };
                if (entry.reads is { } previous)
                    for (int i = 0; i < previous.Count; i++)
                        entry.sequenceRows.Add(previous.items[i]);
            }
            if (!entry.sequenceRows.Add(id))
                return;
            (entry.reads ??= RentReadList()).Add(id);
            IndexRowReader(id, entry);
        }

        private void AppendSequenceGrid(GetterMemoEntry entry, in GridRead read)
        {
            if (entry.sequenceGridReads is null)
            {
                entry.sequenceGridReads = new(GridReadIdentity.Instance);
                entry.sequenceGrids = new(StringComparer.Ordinal);
                if (entry.gridReads is { } previous)
                    for (int i = 0; i < previous.Count; i++)
                    {
                        entry.sequenceGridReads.Add(previous[i]);
                        entry.sequenceGrids.Add(previous[i].content.Primitive.GridValueId);
                    }
            }
            if (!entry.sequenceGridReads.Add(read))
                return;
            (entry.gridReads ??= new GridReadBuffer()).Add(read);
            string grid = read.content.Primitive.GridValueId;
            var reader = new GridMemoReader(entry);
            if (entry.sequenceGrids!.Add(grid))
                IndexGridReader(getterMemoEntriesByGrid, grid, reader);
            if (read.cell is UnityEngine.Vector2Int cell)
                IndexGridReader(getterMemoEntriesByGridCell, new GridCellRead(grid, grid.GetHashCode(), cell, read.tile), reader);
            else
                IndexGridReader(getterMemoEntriesByPlacement, read.placementId, reader);
        }

        private void RetireSequenceGeneration(SequenceGeneration group)
        {
            if (!group.Live)
                return;
            group.Live = false;
            if (sequenceGenerations.TryGetValue(group.Row, out var groups))
            {
                groups.Remove(group.Getter);
                if (groups.Count == 0)
                    sequenceGenerations.Remove(group.Row);
            }
            GetterMemoEntry? notify = null;
            foreach (GetterMemoEntry entry in group.Entries)
            {
                entry.sequenceGeneration = null;
                entry.sequenceRows = null;
                entry.sequenceGridReads = null;
                entry.sequenceGrids = null;
                if (ForgetMemoEntry(entry))
                    notify ??= entry;
            }
            group.Entries.Clear();
            if (notify is not null && HasGetterWatchers)
                QueueGetterChange(notify.key);
        }

        private void RetireSequenceGetter(string row, string getter)
        {
            if (sequenceGenerations.TryGetValue(row, out var groups) && groups.TryGetValue(getter, out var group))
                RetireSequenceGeneration(group);
        }

        private void RetireUnwatchedSequenceRow(string row)
        {
            if (WatchesGetters(row) || !sequenceGenerations.TryGetValue(row, out var groups))
                return;
            // Only this row is visited. Retirement removes entries from its map.
            foreach (SequenceGeneration group in new List<SequenceGeneration>(groups.Values))
                RetireSequenceGeneration(group);
        }
    }
}
