// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private ReplayDefaultGraph? replayDefaultGraph;
        private ReplayDefaultMember? replayDefaultMember;

        internal void BindReplayDefaultRoot(Dictionary<string, string> fields)
        {
            if (replayDefaultGraph is { } graph)
                graph.RootFields = fields;
        }

        internal void BindReplayDefaultClass(ObjectMemberValue row, List<MemberValue> rows)
        {
            for (var graph = replayDefaultGraph; graph is not null; graph = graph.Parent)
            {
                if (ReferenceEquals(graph.RootFields, row.value))
                {
                    graph.Bind(row, graph.Root.id, graph.RootPath, graph.RootOwnership, rows);
                    return;
                }
            }
            if (replayDefaultMember is { Member: ClassMember } pending
                && pending.Graph == replayDefaultGraph)
            {
                pending.PendingRow = row;
                pending.Graph.Bind(row, row.id, AppendVirtualPath(pending.Owner.Path, "class", "schemaKey", pending.Key),
                    ChildOwnership(pending.Member, pending.Owner.Ownership), rows);
            }
        }

        internal ReplayDefaultMember? BeginReplayDefaultMember(
            Dictionary<string, string> fields, string key, Member member, List<MemberValue> rows)
        {
            for (var graph = replayDefaultGraph; graph is not null; graph = graph.Parent)
                if (graph.Builders.TryGetValue(fields, out var builder))
                    return new ReplayDefaultMember(this, graph, builder, key, member, rows);
            return null;
        }

        // A replay's read view uses the same identity and overlay rules as its
        // final expansion. It never publishes unfinished rows to client stores.
        internal sealed class ReplayDefaultGraph : IDisposable
        {
            private readonly NeoClient client;
            internal readonly ReplayDefaultGraph? Parent;
            internal readonly ObjectMemberValue Root;
            internal Dictionary<string, string>? RootFields;
            internal readonly string RootPath;
            internal readonly NeoValueOwnership RootOwnership;
            private readonly ObjectMemberValue namespaceRoot;
            internal readonly Dictionary<string, MemberValue> TemporaryRows = new(StringComparer.Ordinal);
            internal readonly Dictionary<Dictionary<string, string>, DefaultClass> Builders = new();
            private readonly Dictionary<string, DefaultClass> classes = new(StringComparer.Ordinal);
            private readonly Dictionary<List<MemberValue>, int> indexedCounts = new();
            private ProjectionState? projection;
            private ObjectMemberValue? logicalRoot;
            private HashSet<string>? projectingIds;

            // Most constructors never read through root. Allocate the identity
            // index and hash only when a completed default is actually read.
            private sealed class ProjectionState : IDisposable
            {
                internal readonly PreparedVirtualExpansion Expansion;
                internal readonly Dictionary<string, string> ClaimedIds = new(StringComparer.Ordinal);
                internal readonly Dictionary<MemberValue, IReadOnlyDictionary<string, NeoGenericEnvEntry>> Environments = new();
                internal readonly HashSet<string> Ids = new(StringComparer.Ordinal);
                internal readonly Dictionary<string, HashSet<DefaultChild>> Readers = new(StringComparer.Ordinal);
                internal readonly SHA1 Hash = SHA1.Create();
                internal ProjectionState(ObjectMemberValue root) => Expansion = new PreparedVirtualExpansion(root);
                public void Dispose() => Hash.Dispose();
            }

            internal bool Projects(string id) => projection?.Ids.Contains(id) == true;

            internal bool ProjectsInScope(string id)
            {
                // An inner replay can read an earlier default from any
                // enclosing replay before that default is installed.
                for (ReplayDefaultGraph? graph = this; graph is not null; graph = graph.Parent)
                    if (graph.Projects(id))
                        return true;
                return false;
            }

            internal void Invalidate(string id)
            {
                if (projection?.Readers.TryGetValue(id, out var readers) == true)
                    foreach (var child in readers)
                        child.Valid = false;
            }

            private void TrackProjection(VirtualExpansionNode node, DefaultChild child)
            {
                Track(node.row.id);
                Track(node.effectiveId ?? node.virtualId);
                projection!.Ids.Add(node.effectiveId ?? node.virtualId);
                foreach (var nested in node.classChildren.Values)
                    TrackProjection(nested, child);
                foreach (var nested in node.listChildren)
                    TrackProjection(nested, child);
                foreach (var nested in node.dictionaryChildren.Values)
                    TrackProjection(nested, child);

                void Track(string id)
                {
                    if (!projection!.Readers.TryGetValue(id, out var readers))
                        projection.Readers[id] = readers = new();
                    readers.Add(child);
                }
            }

            internal ReplayDefaultGraph(NeoClient client, ObjectMemberValue root, ObjectMemberValue namespaceRoot, string rootPath, NeoValueOwnership ownership)
            {
                this.client = client;
                Root = root;
                Parent = client.replayDefaultGraph;
                client.replayDefaultGraph = this;
                this.namespaceRoot = namespaceRoot;
                RootPath = rootPath;
                RootOwnership = ownership;
            }

            internal sealed class DefaultClass
            {
                internal ObjectMemberValue Row = null!;
                internal List<MemberValue>? Rows;
                internal string Path = null!;
                internal NeoValueOwnership Ownership;
                internal readonly Dictionary<string, ReplayDefaultMember> Members = new(StringComparer.Ordinal);
                internal readonly Dictionary<string, DefaultChild> Children = new(StringComparer.Ordinal);
            }

            internal sealed class DefaultChild
            {
                internal MemberValue Row = null!;
                internal string Id = null!;
                internal bool Valid;
            }

            // The field dictionary identifies a construction before it has a
            // logical id. When a child is projected, its logical name points
            // to that same builder rather than a second copy of the fields.
            internal void Bind(ObjectMemberValue row, string id, string path, NeoValueOwnership ownership, List<MemberValue>? rows = null)
            {
                if (!Builders.TryGetValue(row.value!, out var builder) && !classes.TryGetValue(row.id, out builder))
                    builder = new DefaultClass { Row = row, Rows = rows };
                builder.Path = path;
                builder.Ownership = ownership;
                classes[id] = builder;
                Builders[row.value!] = builder;
                TemporaryRows[row.id] = row;
            }

            internal void IndexRows(List<MemberValue> rows)
            {
                int indexed = indexedCounts.GetValueOrDefault(rows);
                while (indexed < rows.Count)
                {
                    MemberValue row = rows[indexed++];
                    TemporaryRows[row.id] = row;
                }
                indexedCounts[rows] = indexed;
            }

            internal bool TryGetChild(string parentId, string key, out string? id)
            {
                id = null;
                if (!classes.TryGetValue(parentId, out var builder))
                {
                    ProjectIndexedChild(parentId);
                    if (!classes.TryGetValue(parentId, out builder))
                        return projection?.Expansion.ClassChildren.TryGetValue(parentId, out var children) == true
                            && children.TryGetValue(key, out id);
                }
                if (builder.Rows is not null)
                    IndexRows(builder.Rows);
                builder.Members.TryGetValue(key, out var member);
                MemberValue? row = member?.PendingRow;
                var current = client.ResolveValueRow(builder.Row.id) as ObjectMemberValue ?? builder.Row;
                Member? declaration = member?.Member ?? client.TryResolveOwnedChildMember(current, null, key);
                if (declaration is null)
                    return false;
                if (current.value!.TryGetValue(key, out string sourceId))
                    row = client.ResolveValueRow(sourceId);
                if (row is null)
                    return false;
                if (builder.Children.TryGetValue(key, out var cached) && ReferenceEquals(cached.Row, row) && cached.Valid)
                {
                    id = cached.Id;
                    return true;
                }
                string path = AppendVirtualPath(builder.Path, "class", "schemaKey", key);
                ObjectMemberValue? partial = row as ObjectMemberValue;
                // Class defaults remain lazy while their own initializers run.
                // Leaf/aggregate defaults are complete when attached here.
                MemberValue indexedRow = row;
                if (partial is { value: not null } && declaration is ClassMember)
                {
                    var shallow = (ObjectMemberValue)client.CloneRowForWrite(partial);
                    shallow.value = new Dictionary<string, string>();
                    indexedRow = shallow;
                }
                projection ??= new ProjectionState(Root);
                VirtualExpansionNode node = client.IndexVirtualExpansion(namespaceRoot, indexedRow, declaration,
                    path, projection.ClaimedIds, projection.Hash, projection.Environments);
                NeoValueOwnership ownership = client.ChildOwnership(declaration, builder.Ownership);
                client.OverlaySparseInstance(projection.Expansion, node, null, ownership, Root);
                id = node.effectiveId ?? node.virtualId;
                cached ??= new DefaultChild();
                cached.Row = row;
                cached.Id = id;
                cached.Valid = true;
                builder.Children[key] = cached;
                TrackProjection(node, cached);
                if (partial is { value: not null } && declaration is ClassMember)
                    Bind(partial, id, path, ownership);
                return true;
            }

            internal bool TryGetVirtualRow(string id, out MemberValue row)
            {
                // Candidate replay hides the prior nested boundary's root.
                // Keep its logical handle resolvable while its fields rebuild.
                if (id == Root.id && RootFields is not null)
                {
                    if (logicalRoot is null)
                    {
                        logicalRoot = (ObjectMemberValue)client.CloneRowForWrite(Root);
                        logicalRoot.value = new Dictionary<string, string>();
                    }
                    row = logicalRoot;
                    return true;
                }
                row = null!;
                if (projection?.Expansion.Values.TryGetValue(id, out row) == true)
                    return true;
                ProjectIndexedChild(id);
                return projection?.Expansion.Values.TryGetValue(id, out row) == true;
            }

            // Stored edges can enter below an unread parent. Warm replays have
            // virtual placements; cold loads use the existing stored-parent index.
            // Both paths must reach this replay's builder before projecting a child.
            private void ProjectIndexedChild(string id)
            {
                if (id == Root.id || RootFields is null)
                    return;
                projectingIds ??= new(StringComparer.Ordinal);
                if (!projectingIds.Add(id))
                    return;
                try
                {
                    if (client.virtualClassPlacementByChildId.TryGetValue(id, out var placement)
                        && placement.rootId == Root.id
                        && placement.parentValueId != id
                        && placement.schemaKey is string key)
                    {
                        TryGetChild(placement.parentValueId, key, out _);
                        return;
                    }
                    foreach (var parent in client.InferMemberParents(id))
                    {
                        if (parent.Value is not ObjectMemberValue { value: not null, IsRemoved: false } row
                            || parent.Key == id)
                            continue;
                        if (!classes.ContainsKey(parent.Key))
                            ProjectIndexedChild(parent.Key);
                        if (!classes.ContainsKey(parent.Key))
                            continue;
                        foreach (var field in row.value)
                            if (field.Value == id && TryGetChild(parent.Key, field.Key, out string? projectedId)
                                && projectedId == id)
                                return;
                    }
                }
                finally
                {
                    projectingIds.Remove(id);
                }
            }

            internal bool TryGetOwnership(string id, out NeoValueOwnership ownership)
            {
                if (id == Root.id && RootFields is not null)
                {
                    ownership = RootOwnership;
                    return true;
                }
                ownership = default;
                return projection?.Expansion.Ownership.TryGetValue(id, out ownership) == true;
            }

            public void Dispose()
            {
                client.replayDefaultGraph = Parent;
                projection?.Dispose();
            }
        }

        internal sealed class ReplayDefaultMember : IDisposable
        {
            private readonly NeoClient client;
            private readonly ReplayDefaultMember? previous;
            internal readonly ReplayDefaultGraph Graph;
            internal readonly Member Member;
            internal readonly ReplayDefaultGraph.DefaultClass Owner;
            internal readonly string Key;
            internal readonly List<MemberValue> Rows;
            internal ObjectMemberValue? PendingRow;

            internal ReplayDefaultMember(NeoClient client, ReplayDefaultGraph graph,
                ReplayDefaultGraph.DefaultClass builder, string key, Member member, List<MemberValue> rows)
            {
                this.client = client;
                Graph = graph;
                Member = member;
                Owner = builder;
                Key = key;
                Rows = rows;
                previous = client.replayDefaultMember;
                client.replayDefaultMember = this;
                builder.Members[key] = this;
            }

            public void Dispose()
            {
                Graph.IndexRows(Rows);
                client.replayDefaultMember = previous;
            }
        }

        private bool TryGetReplayDefaultRow(string id, out MemberValue? row)
        {
            // Construction allocates Session rows before publishing the whole
            // graph. Once published, writes replace those rows, so the store
            // must win. Caller-supplied durable rows must never be shadowed.
            row = null;
            for (var graph = replayDefaultGraph; graph is not null; graph = graph.Parent)
                if (graph.TemporaryRows.TryGetValue(id, out row))
                {
                    if (sessionValues.ContainsKey(id) || saveData.values.ContainsKey(id)
                        || userSource.userData.values.ContainsKey(id) || data.values.ContainsKey(id)
                        || candidateReplay?.Allocations.ContainsKey(id) == true)
                        break;
                    return true;
                }
            row = null;
            return false;
        }

        // Overlay probes ask whether a value is already materialized. No
        // active replay's read-only projection is materialized, including an
        // enclosing replay that shares this nested boundary's id namespace.
        private bool TryGetReplayOverlay(NeoValueOwnership ownership, string id, out MemberValue? row)
        {
            var previous = replayDefaultGraph;
            replayDefaultGraph = null;
            try
            {
                return TryGetOverlaidValue(ownership, id, out row);
            }
            finally
            {
                replayDefaultGraph = previous;
            }
        }

        private bool TryGetReplayDefaultVirtualRow(string id, out MemberValue row)
        {
            for (var graph = replayDefaultGraph; graph is not null; graph = graph.Parent)
                if (graph.TryGetVirtualRow(id, out row))
                    return true;
            row = null!;
            return false;
        }

        private void ValidateReplayDefaultWrites(NeoWritePlan plan)
        {
            for (var graph = replayDefaultGraph; graph is not null; graph = graph.Parent)
                foreach (var key in plan.Rows.Keys)
                {
                    if (graph.Projects(key.id) || key.id == graph.Root.id)
                        throw new InvalidOperationException("Constructor replay cannot write through root to its own defaults. Write through this during construction.");
                    graph.Invalidate(key.id);
                }
        }
    }
}
