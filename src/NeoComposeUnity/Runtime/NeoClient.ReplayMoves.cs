// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private void InstallRehomedExpansion(PreparedVirtualExpansion expansion)
        {
            foreach (var pair in expansion.Ownership)
            {
                var previous = virtualValueOwnership[pair.Key];
                if (previous == pair.Value)
                    continue;
                InvalidateListenerOwner(previous, pair.Key);
                InvalidateListenerOwner(pair.Value, pair.Key);
                virtualValueOwnership[pair.Key] = pair.Value;
                NoteEffectRowChange(previous, pair.Key, expansion.Values[pair.Key]);
                NoteEffectRowChange(pair.Value, pair.Key, expansion.Values[pair.Key]);
                EvictSharedEvaluationRow(pair.Key);
                SyncValueNode(pair.Key);
            }
            if (virtualClassParentIdsByRoot.TryGetValue(expansion.Root.id, out var parents))
                foreach (var pair in expansion.ClassParentOwnership)
                    parents[pair.Key] = pair.Value;
            foreach (var pair in expansion.Placements)
            {
                var previous = virtualClassPlacementByChildId[pair.Key];
                var next = pair.Value;
                if (previous.ownership != next.ownership || previous.parentOwnership != next.parentOwnership)
                    TrackVirtualClassPlacement(next.rootId, next.parentValueId, pair.Key, next.member, next.ownership,
                        next.parentOwnership, next.path, next.schemaKey);
            }
            if (expansion.Boundary is not null)
                InstallNestedReplayBoundary(expansion.Boundary);
            if (expansion.ListenerDefaults.Count != 0)
            {
                ClearListenerDefaults(expansion.Root.id);
                InstallListenerDefaults(expansion);
            }
        }

        // Moving an unchanged instance between stores changes inherited storage,
        // not its evaluated defaults. Rebuild only the storage indexes.
        private PreparedVirtualExpansion RehomeVirtualExpansion(ObjectMemberValue root, NeoValueOwnership ownership, NeoWritePlan plan)
        {
            var result = new PreparedVirtualExpansion(root) { RootOwnership = ownership };
            var scopes = new Dictionary<string, NeoValueOwnership> { [root.id] = ownership };
            NeoValueOwnership Scope(string id)
            {
                if (scopes.TryGetValue(id, out var scope))
                    return scope;
                if (virtualClassPlacementByChildId.TryGetValue(id, out var slot) && slot.parentValueId != id)
                    scope = ChildOwnership(slot.member, Scope(slot.parentValueId));
                else if (!plan.TryGetOwnership(id, out scope))
                    scope = NeoValueOwnership.Asset;
                scopes[id] = scope;
                return scope;
            }
            if (virtualValueIdsByRoot.TryGetValue(root.id, out var values))
                foreach (string id in values)
                {
                    result.Values[id] = virtualValues[id];
                    result.Ownership[id] = Scope(id);
                }
            if (virtualClassParentIdsByRoot.TryGetValue(root.id, out var parents))
                foreach (string id in parents.Keys)
                {
                    result.ClassChildren[id] = virtualClassChildren[id];
                    result.ClassParentOwnership[id] = Scope(id);
                }
            if (virtualClassChildIdsByRoot.TryGetValue(root.id, out var children))
                foreach (string id in children)
                {
                    var slot = virtualClassPlacementByChildId[id];
                    result.TrackPlacement(slot.parentValueId, id, slot.member, Scope(id), Scope(slot.parentValueId), slot.path, slot.schemaKey);
                }
            if (virtualFootprintByRoot.TryGetValue(root.id, out var footprint))
                result.Footprint.UnionWith(footprint);
            if (constructorArgumentValueIdsByRoot.TryGetValue(root.id, out var dependencies))
                result.Dependencies.UnionWith(dependencies);
            if (defaultListenerOwnersByExpansion.TryGetValue(root.id, out var owners))
                foreach (var owner in owners)
                    if (defaultChangeListeners.TryGetValue(owner, out var defaults))
                        result.ListenerDefaults[(Scope(owner.ownerId), owner.ownerId)] = defaults.members;
            if (nestedReplayBoundaries.TryGetValue(root.id, out var boundary))
            {
                var copy = new NestedReplayBoundary
                {
                    Root = root,
                    NamespaceRoot = boundary.NamespaceRoot,
                    OwnerRootId = boundary.OwnerRootId,
                    IncomingAnswers = boundary.IncomingAnswers,
                    Ownership = ownership,
                    Path = boundary.Path
                };
                foreach (var field in boundary.Fields)
                    copy.Fields.Add(field.Key, field.Value);
                foreach (var id in boundary.Ids)
                    copy.Ids.Add(id.Key, id.Value);
                result.Boundary = copy;
            }
            if (nestedReplayRootsByOwner.TryGetValue(root.id, out var nested))
                foreach (string id in nested)
                    result.Nested.Add(RehomeVirtualExpansion(nestedReplayBoundaries[id].Root, Scope(id), plan));
            return result;
        }
    }
}
