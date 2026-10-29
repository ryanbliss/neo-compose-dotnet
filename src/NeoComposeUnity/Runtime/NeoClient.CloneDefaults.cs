// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // A clone copies resolved defaults as well as stored overrides. The
        // defaults stay outside writable storage, exactly as on the source.
        internal sealed class ClonedGraph
        {
            internal readonly NeoValueOwnership Ownership;
            internal ObjectMemberValue? Root;
            internal bool IdentitySensitive;
            internal readonly Dictionary<string, MemberValue> Rows = new();
            internal readonly HashSet<string> Immutable = new();
            internal readonly Dictionary<string, NeoValueOwnership> Stores = new();
            internal readonly Dictionary<string, Dictionary<string, string>> Fields = new();
            internal readonly Dictionary<string, (string parent, Member member, string path, string? key, string? identity, string? identityRoot)> Placements = new();
            internal readonly HashSet<string> Dependencies = new();
            internal ClonedGraph(NeoValueOwnership ownership) => Ownership = ownership;

            internal bool IsCurrent(NeoWritePlan plan)
            {
                if (Root is null || IdentitySensitive)
                    return false;
                foreach (var row in Rows)
                    if (!Immutable.Contains(row.Key) && !ReferenceEquals(plan.Resolve(Stores[row.Key], row.Key), row.Value))
                        return false;
                if (plan.Bindings.Count != 0)
                    foreach (var binding in plan.Bindings.Keys)
                        if (Dependencies.Contains($"static:{binding.ownership}:{binding.memberId}"))
                            return false;
                foreach (string dependency in Dependencies)
                {
                    string id = DependencyValueId(dependency);
                    if (!Rows.ContainsKey(id) && plan.WritesRow(id))
                        return false;
                }
                return true;
            }
        }

        private void CopyCloneDependencies(ClonedGraph graph, ClonedValueOccurrence copies)
        {
            string? Resolve(string id) => copies.ResolveIdentity(this, id);
            var roots = new HashSet<string>();
            foreach (var copy in copies.Values)
            {
                if (constructorArgumentValueIdsByRoot.ContainsKey(copy.Key.id))
                    roots.Add(copy.Key.id);
                if (virtualRootByFootprintId.TryGetValue(copy.Key.id, out var root))
                    roots.Add(root);
            }
            foreach (var copy in copies.Values)
                if (graph.Immutable.Contains(copy.Value.id))
                {
                    var row = graph.Rows[copy.Value.id];
                    if (row is ObjectMemberValue { constructorArgs: { Count: > 0 } } construction)
                        RemapConstructedReferences(construction, copy.Value.member as ClassMember, Resolve);
                    if (row is DelegateMemberValue { value: { valueId: { } receiver } } selector
                        && Resolve(receiver) is { } target)
                        selector.value.valueId = target;
                    if (copy.Value.member is LookupMember && row is ArrayMemberValue { value: not null } lookup)
                        for (int index = 0; index < lookup.value.Length; index++)
                            if (Resolve(lookup.value[index]) is { } selected)
                                lookup.value[index] = selected;
                }
            foreach (var copy in copies.Values)
                if (TryResolveVirtualPlacement(copy.Key.id, out var slot)
                    && copies.TryResolve((slot.parentOwnership, slot.parentValueId), out var parent)
                    && graph.Rows.ContainsKey(parent!))
                    graph.Placements[copy.Value.id] = (parent!, slot.member, slot.path, slot.schemaKey, slot.identity,
                        slot.identityRoot is not null ? Resolve(slot.identityRoot) : null);
            foreach (string root in roots)
                if (identitySensitiveExpansionRoots.Contains(root))
                    graph.IdentitySensitive = true;
            foreach (string root in roots)
                if (constructorArgumentValueIdsByRoot.TryGetValue(root, out var dependencies))
                    foreach (string dependency in dependencies)
                    {
                        string id = DependencyValueId(dependency);
                        if (Resolve(id) is { } copied)
                        {
                            if (dependency.StartsWith("field:", System.StringComparison.Ordinal))
                                graph.Dependencies.Add("field:" + copied + dependency.Substring(6 + id.Length));
                            else if (dependency.StartsWith("identity:", System.StringComparison.Ordinal))
                                graph.Dependencies.Add("identity:" + copied);
                            else
                                graph.Dependencies.Add(copied);
                        }
                        else
                            graph.Dependencies.Add(dependency);
                    }
        }

        private PreparedVirtualExpansion PrepareClonedExpansion(ClonedGraph graph)
        {
            var expansion = new PreparedVirtualExpansion(graph.Root!) { RootOwnership = graph.Ownership };
            NeoValueOwnership Scope(string id) => graph.Immutable.Contains(id) ? NeoValueOwnership.Asset : graph.Stores[id];
            foreach (string id in graph.Immutable)
            {
                expansion.Values[id] = graph.Rows[id];
                expansion.Ownership[id] = NeoValueOwnership.Asset;
            }
            foreach (var fields in graph.Fields)
            {
                expansion.ClassChildren[fields.Key] = fields.Value;
                expansion.ClassParentOwnership[fields.Key] = Scope(fields.Key);
            }
            foreach (var placement in graph.Placements)
            {
                var slot = placement.Value;
                expansion.TrackPlacement(slot.parent, placement.Key, slot.member, Scope(placement.Key), Scope(slot.parent), slot.path, slot.key);
                if (slot.identityRoot is not null)
                {
                    expansion.Placements[placement.Key].identity = slot.identity;
                    expansion.Placements[placement.Key].identityRoot = slot.identityRoot;
                }
            }
            expansion.Footprint.UnionWith(graph.Rows.Keys);
            expansion.Dependencies.UnionWith(graph.Dependencies);
            return expansion;
        }
    }
}
