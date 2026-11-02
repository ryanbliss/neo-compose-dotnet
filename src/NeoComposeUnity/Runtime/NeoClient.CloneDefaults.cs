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
            internal ClonedGraph(NeoValueOwnership ownership) => Ownership = ownership;

            internal bool IsCurrent(NeoWritePlan plan)
            {
                if (Root is null || IdentitySensitive)
                    return false;
                foreach (var row in Rows)
                    if (!Immutable.Contains(row.Key) && !ReferenceEquals(plan.Resolve(Stores[row.Key], row.Key), row.Value))
                        return false;
                return true;
            }
        }

        // Only the standalone clone path calls this. A complete graph of new
        // Session identities has no old bindings or placements to invalidate,
        // just like a newly constructed graph. Copies with listener wiring,
        // replay defaults, or retained external inputs use the ordinary plan.
        private bool TryPublishFreshSessionClone(NeoWritePlan plan)
        {
            if (candidateReplay is not null || isReplayingVirtualInstance
                || plan.ClonedGraphs is not { Count: 1 })
                return false;
            ClonedGraph graph = plan.ClonedGraphs[0];
            if (graph.Ownership != NeoValueOwnership.Session || graph.Immutable.Count != 0
                || graph.Rows.Count != plan.Rows.Count || !graph.IsCurrent(plan))
                return false;
            foreach (var pair in plan.Rows)
                if (pair.Key.ownership != NeoValueOwnership.Session || pair.Value is null
                    || pair.Value.IsRemoved || pair.Value.copiedChangeListeners is not null
                    || pair.Value is ObjectMemberValue { classId: { } classId } && ResolveClassNode(classId).Tracked
                    || !graph.Rows.ContainsKey(pair.Key.id)
                    || TryGetCommittedValue(pair.Key.id, out MemberValue? _))
                    return false;
            var rows = new List<MemberValue>(plan.Rows.Count);
            foreach (var pair in plan.Rows)
            {
                StampMapKeyForWrite(pair.Key.ownership, pair.Value!);
                rows.Add(pair.Value!);
            }
            PublishConstructedSessionRows(rows);
            return true;
        }

        private void CopyCloneDependencies(ClonedGraph graph, ClonedValueOccurrence copies)
        {
            string? Resolve(string id) => copies.ResolveIdentity(this, id);
            var roots = new HashSet<string>();
            foreach (var copy in copies.Values)
            {
                if (virtualFootprintByRoot.ContainsKey(copy.Key.id))
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
            return expansion;
        }
    }
}
