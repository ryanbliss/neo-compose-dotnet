// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private ConstructionListenerCapture? constructionListenerCapture;

        internal sealed class DefaultListenerProjection
        {
            internal NeoChangeListenerMap Authored = null!;
            internal NeoChangeListenerEndpoints? Endpoints;
            internal bool PreserveProductListeners;
            internal Dictionary<string, string> ClonedIds = null!;
            internal string TemplateRootId = null!;
            internal MemberValue Root = null!;
        }


        internal ConstructionListenerCapture? BeginConstructionListeners(bool onlyIfNeeded = false) =>
            isReplayingVirtualInstance || (onlyIfNeeded && constructionListenerCapture is not null)
                ? null : new ConstructionListenerCapture(this);

        internal sealed class ConstructionListenerCapture : IDisposable
        {
            private readonly NeoClient client;
            internal readonly ConstructionListenerCapture? Parent;
            internal NeoChangeListenerMap? ExistingDefaults;
            internal List<DefaultListenerProjection>? ExistingProjections;
            internal NeoChangeListenerMap Defaults => ExistingDefaults ??= new();
            internal List<DefaultListenerProjection> Projections => ExistingProjections ??= new();
            private string? rootId;

            internal ConstructionListenerCapture(NeoClient client)
            {
                this.client = client;
                Parent = client.constructionListenerCapture;
                client.constructionListenerCapture = this;
            }

            internal void Complete(string id) => rootId = id;

            public void Dispose()
            {
                client.constructionListenerCapture = Parent;
                if (rootId is null)
                    return;
                if (Parent is not null)
                {
                    if (ExistingProjections is { Count: > 0 })
                        Parent.Projections.AddRange(ExistingProjections);
                    if (ExistingDefaults is not null)
                        foreach (var owner in ExistingDefaults)
                            Parent.Defaults[owner.Key] = owner.Value;
                    return;
                }
                if (ExistingDefaults is not { Count: > 0 })
                    return;
                if (client.candidateReplay is { } candidate)
                {
                    foreach (var owner in Defaults)
                        candidate.ConstructionDefaults[owner.Key] = (rootId, owner.Value);
                    var owners = new List<string>(Defaults.Keys);
                    candidate.Plan.AfterCommit(() =>
                    {
                        foreach (string ownerId in owners)
                            if (candidate.ConstructionDefaults.TryGetValue(ownerId, out var entry)
                                && client.TryGetValueOwnership(ownerId, out var ownership))
                                client.InstallListenerDefault(entry.rootId, ownership, ownerId, entry.members);
                    });
                    return;
                }
                foreach (var owner in Defaults)
                    if (client.TryGetValue(NeoValueOwnership.Session, owner.Key, out MemberValue? row)
                        && row is ObjectMemberValue { IsRemoved: false })
                        client.InstallListenerDefault(rootId, NeoValueOwnership.Session, owner.Key, owner.Value);
            }
        }

        private string ListenerDefaultTemplateId(Member member)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (!member.DeclaresWireField("defaultValue") && member.extendsMemberId is string parentId)
            {
                if (!seen.Add(member.id))
                    throw new InvalidOperationException($"Default listener declaration '{member.id}' has a cyclic override chain.");
                if (!TryGetMember(parentId, out Member? parent))
                    throw new InvalidOperationException($"Default listener declaration '{parentId}' is missing.");
                member = parent;
            }
            return DerivedMemberValueId(member.id);
        }

        internal void CaptureClonedListenerDefaults(Dictionary<string, string> clonedIds, Member member, MemberValue root)
        {
            NeoChangeListenerMap? inherited = null;
            foreach (string sourceId in clonedIds.Keys)
            {
                if (!TryGetValueOwnership(sourceId, out var ownership)
                    || !defaultChangeListeners.TryGetValue((ownership, sourceId), out var entry))
                    continue;
                (inherited ??= new())[sourceId] = entry.members;
            }
            // The completed occurrence journal includes sibling receivers and
            // excludes Save/Session overrides, which are never declaration defaults.
            CaptureAuthoredListenerDefaults(inherited, null, clonedIds, member, root);
        }

        internal void CaptureAuthoredListenerDefaults(NeoChangeListenerMap? authored, NeoChangeListenerEndpoints? endpoints,
            Dictionary<string, string> clonedIds, Member member, MemberValue root, string? sourceRootId = null, bool resolveImmediately = true, bool preserveProductListeners = false)
        {
            if (authored is not { Count: > 0 })
                return;
            var projections = replayAllocationScope?.ListenerProjections ?? constructionListenerCapture?.Projections;
            if (projections is null)
                throw new InvalidOperationException("Default listener projection requires a construction scope.");
            string templateRootId = sourceRootId ?? ListenerDefaultTemplateId(member);
            // Retain only this map's referenced correspondences. A parent's shared
            // journal can clone the same stored source again after this occurrence.
            var occurrenceIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var owner in authored)
            {
                Remember(owner.Key);
                foreach (var targets in owner.Value.Values)
                    foreach (var target in targets)
                        if (target.valueId is { } receiver)
                            Remember(receiver);
            }
            if (endpoints is not null)
                foreach (var endpoint in endpoints.Values)
                    if (endpoint.rootId is { } frame)
                        Remember(frame);
            occurrenceIds[templateRootId] = root.id;
            void Remember(string id)
            {
                if (clonedIds.TryGetValue(id, out string? copy))
                    occurrenceIds[id] = copy;
            }
            projections.Add(new DefaultListenerProjection
            {
                Authored = authored,
                PreserveProductListeners = preserveProductListeners,
                Endpoints = endpoints,
                ClonedIds = occurrenceIds,
                TemplateRootId = templateRootId,
                Root = root,
            });
            if (resolveImmediately && TryGetValue(NeoValueOwnership.Session, root.id, out MemberValue? _))
                ResolveConstructedListenerDefaults(root, member);
        }

        internal void ResolveConstructedListenerDefaults(MemberValue root, Member member)
        {
            var projections = replayAllocationScope?.ExistingListenerProjections ?? constructionListenerCapture?.ExistingProjections;
            if (projections is not { Count: > 0 })
                return;
            VirtualExpansionNode? graph = null;
            ResolveConstructedListenerProjections(projections, () => graph ??= IndexVirtualExpansion(root as ObjectMemberValue ?? new ObjectMemberValue { id = root.id }, root, member, "$",
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<MemberValue, IReadOnlyDictionary<string, NeoGenericEnvEntry>>()));
        }

        private void ResolveConstructedListenerProjections(List<DefaultListenerProjection> projections, Func<VirtualExpansionNode> graph)
        {
            ListenerProjectionIndex? index = null;
            int retained = 0;
            for (int projectionIndex = 0; projectionIndex < projections.Count; projectionIndex++)
            {
                var projection = projections[projectionIndex];
                if (!TryGetValue(NeoValueOwnership.Session, projection.Root.id, out MemberValue? _))
                {
                    projections[retained++] = projection;
                    continue;
                }
                if (projection.Endpoints is { Count: > 0 })
                {
                    index ??= new ListenerProjectionIndex(graph());
                    if (!index.ById.TryGetValue(projection.Root.id, out var occurrence))
                    {
                        projections[retained++] = projection;
                        continue;
                    }
                    foreach (var endpoint in projection.Endpoints)
                    {
                        string frameId = endpoint.Value.rootId ?? projection.TemplateRootId;
                        if (!projection.ClonedIds.TryGetValue(frameId, out string? producedFrame)
                            || !index.ById.TryGetValue(producedFrame, out var frame)
                            || !index.Contains(occurrence, frame))
                            continue;
                        if (index.Resolve(frame, frameId, endpoint.Key, endpoint.Value) is { } target)
                            projection.ClonedIds[endpoint.Key] = target.row.id;
                    }
                }
                ProjectAuthoredListenerDefaults(projection.Authored, projection.ClonedIds, projection.Endpoints, projection.PreserveProductListeners);
            }
            projections.RemoveRange(retained, projections.Count - retained);
        }

        private sealed class ListenerProjectionIndex
        {
            internal readonly Dictionary<string, VirtualExpansionNode> ById = new(StringComparer.Ordinal);
            private readonly Dictionary<string, VirtualExpansionNode> byPath = new(StringComparer.Ordinal);
            private readonly Dictionary<VirtualExpansionNode, (int start, int end)> spans = new();
            private readonly Dictionary<string, List<(int index, VirtualExpansionNode node)>> sources = new(StringComparer.Ordinal);

            internal ListenerProjectionIndex(VirtualExpansionNode root)
            {
                int sequence = 0;
                void Visit(VirtualExpansionNode node)
                {
                    int start = sequence++;
                    ById[node.row.id] = node;
                    byPath[node.path] = node;
                    if (node.row.sourceValueId is { } source)
                    {
                        if (!sources.TryGetValue(source, out var entries))
                            sources[source] = entries = new();
                        entries.Add((start, node));
                    }
                    foreach (var child in node.classChildren.Values)
                        Visit(child);
                    foreach (var child in node.listChildren)
                        Visit(child);
                    foreach (var child in node.dictionaryChildren.Values)
                        Visit(child);
                    spans[node] = (start, sequence);
                }
                Visit(root);
            }

            internal bool Contains(VirtualExpansionNode root, VirtualExpansionNode node) =>
                spans[node].start >= spans[root].start && spans[node].start < spans[root].end;

            internal VirtualExpansionNode? Resolve(VirtualExpansionNode root, string sourceRootId, string id, NeoChangeListenerEndpoint descriptor)
            {
                descriptor.Validate();
                VirtualExpansionNode? first = null;
                if (descriptor.sourceValueId is { } source)
                {
                    if (!sources.TryGetValue(source, out var entries))
                        return null;
                    var span = spans[root];
                    int low = 0, high = entries.Count;
                    while (low < high)
                    {
                        int middle = (low + high) / 2;
                        if (entries[middle].index <= span.start)
                            low = middle + 1;
                        else
                            high = middle;
                    }
                    if (low >= entries.Count || entries[low].index >= span.end)
                        return null;
                    first = entries[low].node;
                    if (VirtualValueId(sourceRootId, source) == id)
                        return first;
                }
                if (!byPath.TryGetValue(root.path + descriptor.pathKey.Substring(1), out var node)
                    || !Contains(root, node) || node.row.sourceValueId != descriptor.sourceValueId)
                    return null;
                string identity;
                if (descriptor.sourceValueId is null)
                {
                    if (node.member.id != descriptor.memberId)
                        return null;
                    identity = $"path:{descriptor.memberId}:{descriptor.pathKey}";
                }
                else
                {
                    if (ReferenceEquals(first, node))
                        return null;
                    identity = $"{descriptor.sourceValueId}:{descriptor.pathKey}";
                }
                return VirtualValueId(sourceRootId, identity) == id ? node : null;
            }
        }

        private Dictionary<string, NeoDelegateValue[]>? PendingConstructionListeners(string ownerId)
        {
            for (var capture = constructionListenerCapture; capture is not null; capture = capture.Parent)
                if (capture.ExistingDefaults?.TryGetValue(ownerId, out var members) == true)
                    return members;
            if (candidateReplay?.ConstructionDefaults.TryGetValue(ownerId, out var pending) == true)
                return pending.members;
            return defaultChangeListeners.TryGetValue((NeoValueOwnership.Session, ownerId), out var defaults)
                ? defaults.members : null;
        }

        private void InstallListenerDefault(string expansionId, NeoValueOwnership scope, string ownerId,
            Dictionary<string, NeoDelegateValue[]> members, string? bindingRoot = null)
        {
            var key = (scope, ownerId);
            if (defaultChangeListeners.TryGetValue(key, out var old)
                && defaultListenerOwnersByExpansion.TryGetValue(old.expansionId, out var oldOwners))
            {
                oldOwners.Remove(key);
                if (oldOwners.Count == 0)
                    defaultListenerOwnersByExpansion.Remove(old.expansionId);
            }
            bindingRoot ??= ListenerBindingRoot(ownerId, scope);
            defaultChangeListeners[key] = (expansionId, bindingRoot, members);
            if (!defaultListenerOwnersByExpansion.TryGetValue(expansionId, out var owners))
                defaultListenerOwnersByExpansion[expansionId] = owners = new();
            owners.Add(key);
            RefreshListenerOwnerRegistration(scope, bindingRoot, ownerId);
        }

        private void RemoveListenerDefault(NeoValueOwnership scope, string ownerId)
        {
            var key = (scope, ownerId);
            if (!defaultChangeListeners.Remove(key, out var entry))
                return;
            if (defaultListenerOwnersByExpansion.TryGetValue(entry.expansionId, out var owners))
            {
                owners.Remove(key);
                if (owners.Count == 0)
                    defaultListenerOwnersByExpansion.Remove(entry.expansionId);
            }
            RefreshListenerOwnerRegistration(scope, entry.rootId, ownerId);
        }
    }
}
