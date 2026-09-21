// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private NestedConstructorCapture? nestedConstructorCapture;
        private Dictionary<string, NestedConstructorCapture>? nestedConstructedRoots;
        private Dictionary<string, NestedConstructorCapture>? nestedConstructedRows;
        private readonly Dictionary<string, NestedReplayBoundary> nestedReplayBoundaries = new();
        private readonly Dictionary<string, HashSet<string>> nestedReplayRootsByOwner = new();

        private void InstallNestedReplayBoundary(NestedReplayBoundary boundary)
        {
            nestedReplayBoundaries[boundary.Root.id] = boundary;
            if (!nestedReplayRootsByOwner.TryGetValue(boundary.NamespaceRoot.id, out var children))
                nestedReplayRootsByOwner[boundary.NamespaceRoot.id] = children = new();
            children.Add(boundary.Root.id);
        }

        private void ClearNestedReplayBoundary(string rootId)
        {
            if (nestedReplayRootsByOwner.Remove(rootId, out var children))
                foreach (string child in children.ToArray()) ClearVirtualInstanceRoot(child);
            if (nestedReplayBoundaries.Remove(rootId, out var boundary)
                && nestedReplayRootsByOwner.TryGetValue(boundary.NamespaceRoot.id, out var siblings))
            {
                siblings.Remove(rootId);
                if (siblings.Count == 0) nestedReplayRootsByOwner.Remove(boundary.NamespaceRoot.id);
            }
        }

        internal bool TryGetReplayReference(string id, out MemberValue? row, NeoValueOwnership? ownership = null)
        {
            if (!isReplayingVirtualInstance || capturedValueReads is null)
                return ownership is NeoValueOwnership store ? TryGetValue(store, id, out row) : TryGetValue(id, out row);
            var reads = capturedValueReads;
            using var capture = SuppressValueReads();
            bool found = ownership is NeoValueOwnership preferred ? TryGetValue(preferred, id, out row) : TryGetValue(id, out row);
            reads.Add(row is ObjectMemberValue { classId: not null } ? "identity:" + id : id);
            return found;
        }

        internal void ReadReplayField(string? id, string key)
        {
            if (isReplayingVirtualInstance && id is not null)
                capturedValueReads?.Add("field:" + id + "\n" + key);
        }

        private static string DependencyValueId(string dependency)
            => dependency.StartsWith("field:", StringComparison.Ordinal)
                ? dependency.Substring(6, dependency.IndexOf('\n') - 6)
                : dependency.StartsWith("identity:", StringComparison.Ordinal) ? dependency.Substring(9) : dependency;

        private readonly Dictionary<string, HashSet<string>> replayFieldsByValueId = new();

        private void EnqueueReplayFields(Queue<string> pending, string id, NeoWritePlan? plan = null)
        {
            if (!replayFieldsByValueId.TryGetValue(id, out var fields)) return;
            foreach (string field in fields)
                if (plan is null || !SameReplayField(plan, field, null)) pending.Enqueue(field);
        }

        private bool SameReplayField(NeoWritePlan plan, string dependency, CandidateReplay? candidate)
        {
            int separator = dependency.IndexOf('\n');
            string id = dependency.Substring("field:".Length, separator - "field:".Length);
            string key = dependency.Substring(separator + 1);
            MemberValue? previous = PreviousReplayRow(id);
            var next = plan.Resolve(id);
            return Child(previous, false) == Child(next, true);

            string? Child(MemberValue? row, bool proposed)
            {
                if (row is not ObjectMemberValue { value: not null } obj) return null;
                if (obj.value.TryGetValue(key, out var child)) return child;
                if (proposed && candidate is not null && candidate.ClassChildren.TryGetValue(id, out var changed))
                    return changed.GetValueOrDefault(key);
                return virtualClassChildren.TryGetValue(id, out var defaults) ? defaults.GetValueOrDefault(key) : null;
            }
        }

        private MemberValue? PreviousReplayRow(string id) => sessionData.values.TryGetValue(id, out var session) ? session
            : saveData.values.TryGetValue(id, out var save) ? save
            : data.values.TryGetValue(id, out var asset) ? asset
            : virtualValues.TryGetValue(id, out var cached) ? cached : null;

        private static bool SameReplayIdentity(MemberValue? left, MemberValue? right)
            => ReferenceEquals(left, right) || left is not null && right is not null
                && left.GetType() == right.GetType() && left.classId == right.classId
                && left.IsRemoved == right.IsRemoved && left.containerId == right.containerId
                && NeoSemanticJson.MapsEqual(left.genericBindings, right.genericBindings)
                && (left is not ObjectMemberValue a || right is ObjectMemberValue b && (a.value is null) == (b.value is null));

        private IDisposable BeginNestedReplay()
        {
            var roots = nestedConstructedRoots;
            var rows = nestedConstructedRows;
            var scope = nestedConstructorCapture;
            nestedConstructedRoots = new();
            nestedConstructedRows = new();
            nestedConstructorCapture = null;
            return new NeoDisposableAction(() =>
            {
                nestedConstructedRoots = roots;
                nestedConstructedRows = rows;
                nestedConstructorCapture = scope;
            });
        }

        internal NestedConstructorCapture? BeginNestedConstructorCapture()
            => isReplayingVirtualInstance && nestedConstructedRoots is not null
                ? new NestedConstructorCapture(this) : null;

        // Passing a newly constructed class as an owned child does not consume
        // its contents. Reading one of its fields does: the enclosing replay
        // must then depend on the inputs that produced that field.
        internal void ReadNestedConstructorResult(string id)
        {
            if (nestedConstructedRows?.TryGetValue(id, out var producer) == true
                && producer.Root?.id != id)
            {
                producer.HasExternalReads = true;
                capturedValueReads?.UnionWith(producer.Reads);
            }
        }

        internal sealed class NestedConstructorCapture : IDisposable
        {
            private readonly NeoClient client;
            private readonly HashSet<string>? previousReads;
            internal readonly NestedConstructorCapture? Parent;
            internal readonly HashSet<string> Reads = new();
            internal readonly HashSet<string> Allocations = new();
            internal readonly HashSet<string> Fields = new();
            internal ObjectMemberValue? Root;
            internal bool HasExternalWrites;
            internal bool HasExternalReads;
            private bool hasNestedConstructor;
            private bool complete;

            internal NestedConstructorCapture(NeoClient client)
            {
                this.client = client;
                Parent = client.nestedConstructorCapture;
                if (Parent is not null) Parent.hasNestedConstructor = true;
                previousReads = client.capturedValueReads;
                client.capturedValueReads = Reads;
                client.nestedConstructorCapture = this;
            }

            internal IDisposable ReadCallSite()
            {
                client.capturedValueReads = previousReads;
                return new NeoDisposableAction(() => client.capturedValueReads = Reads);
            }

            internal void Complete(ObjectMemberValue root,
                IReadOnlyList<NeoGeneratedTypesSupport.RuntimeConstructorField> fields)
            {
                Root = root;
                foreach (var field in fields) Fields.Add(field.schemaKey);
                // Only independent leaf constructions can be replayed here.
                // Compound call-site fields need their original evaluator and
                // retain the enclosing replay instead.
                Reads.RemoveWhere(id => Allocations.Contains(DependencyValueId(id)));
                complete = Reads.Count > 0 && Parent is not null && !hasNestedConstructor && !HasExternalWrites
                    && root.value is not null
                    && root.value.Values.All(id => client.ResolveValueRow(id) is not ObjectMemberValue { classId: not null })
                    && fields.All(field => root.value.TryGetValue(field.schemaKey, out var id)
                        && client.ResolveValueRow(id) is not ObjectMemberValue);
                if (!complete) return;
                client.nestedConstructedRoots![root.id] = this;
                foreach (string id in Allocations) client.nestedConstructedRows![id] = this;
            }

            public void Dispose()
            {
                client.nestedConstructorCapture = Parent;
                client.capturedValueReads = previousReads;
                if (!complete) previousReads?.UnionWith(Reads);
            }
        }

        private sealed class NestedReplayBoundary
        {
            internal ObjectMemberValue Root = null!;
            internal ObjectMemberValue NamespaceRoot = null!;
            internal NeoValueOwnership Ownership;
            internal string Path = null!;
            internal readonly Dictionary<string, MemberValue> Fields = new();
            internal readonly Dictionary<string, string> Ids = new();
        }

        private void RestoreNestedCallSiteFields(VirtualExpansionNode graph, NestedReplayBoundary boundary)
        {
            graph.virtualId = boundary.Root.id;
            foreach (var child in graph.classChildren.Values)
                if (boundary.Ids.TryGetValue(child.path, out var id)) child.virtualId = id;
            foreach (var pair in boundary.Fields)
                if (graph.classChildren.TryGetValue(pair.Key, out var child))
                    child.row = pair.Value;
        }

        private void PartitionNestedReplay(VirtualExpansionNode graph, PreparedVirtualExpansion expansion,
            ObjectMemberValue instanceRoot)
        {
            var partitioned = new HashSet<string>();
            Visit(graph);
            // A produced value may be replaced or cloned before it is attached.
            // Keep the conservative dependency whenever no independent boundary
            // for that result could be installed.
            foreach (var pair in nestedConstructedRoots!)
                if (!partitioned.Contains(pair.Key))
                {
                    expansion.Dependencies.UnionWith(pair.Value.Reads);
                }

            void Visit(VirtualExpansionNode node)
            {
                if (node != graph && nestedConstructedRoots!.TryGetValue(node.row.id, out var capture)
                    && node.effectiveId is string effectiveId
                    && !capture.HasExternalWrites && !capture.HasExternalReads
                    && expansion.Values.TryGetValue(effectiveId, out var row)
                    && row is ObjectMemberValue root && IsVirtualInstanceRoot(root)
                    && !node.classChildren.Values.Any(child => child.classChildren.Count != 0
                        || child.listChildren.Count != 0 || child.dictionaryChildren.Count != 0))
                {
                    var boundary = new NestedReplayBoundary
                    { Root = root, NamespaceRoot = instanceRoot, Path = node.path, Ownership = expansion.Ownership[effectiveId] };
                    foreach (var child in node.classChildren.Values) boundary.Ids[child.path] = child.virtualId;
                    foreach (string field in capture.Fields)
                        if (node.classChildren.TryGetValue(field, out var child)
                            && child.effectiveId is string fieldId && expansion.Values.TryGetValue(fieldId, out var fieldRow))
                            boundary.Fields[field] = fieldRow;
                    var nested = new PreparedVirtualExpansion(root) { Boundary = boundary };
                    nested.Dependencies.UnionWith(capture.Reads);
                    Move(node, nested, isRoot: true);
                    expansion.Nested.Add(nested);
                    partitioned.Add(node.row.id);
                    return;
                }
                foreach (var child in node.classChildren.Values) Visit(child);
                foreach (var child in node.listChildren) Visit(child);
                foreach (var child in node.dictionaryChildren.Values) Visit(child);
            }

            void Move(VirtualExpansionNode node, PreparedVirtualExpansion nested, bool isRoot = false)
            {
                string id = node.effectiveId!;
                if (expansion.Values.Remove(id, out var value))
                { nested.Values[id] = value; nested.Ownership[id] = expansion.Ownership[id]; expansion.Ownership.Remove(id); }
                if (expansion.Footprint.Remove(id)) nested.Footprint.Add(id);
                if (expansion.ClassChildren.Remove(id, out var children)) nested.ClassChildren[id] = children;
                // The root's placement belongs to its enclosing collection.
                if (!isRoot && expansion.Placements.Remove(id, out var placement))
                { placement.rootId = nested.Root.id; nested.Placements[id] = placement; }
                foreach (var child in node.classChildren.Values) Move(child, nested);
                foreach (var child in node.listChildren) Move(child, nested);
                foreach (var child in node.dictionaryChildren.Values) Move(child, nested);
            }
        }
    }
}
