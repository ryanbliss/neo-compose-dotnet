// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // Removing an ordinary object cannot introduce a placement collision.
        // Prove that this plan only removes its owned rows and creates detached
        // Session aliases before retaining the unrelated grid records.
        private bool TryValidateObjectRemoval(NeoWritePlan plan, HashSet<string> grids)
        {
            if (grids.Count != 1 || plan.Bindings.Count != 0)
                return false;
            string gridId = grids.First();
            if (!gridLookupCaches.TryGetValue(gridId, out var cache)
                || cache.Primitive.CachedPlacementGraph is not { } graph)
                return false;
            var removed = new Dictionary<string, (string layer, NeoObjectPlacementRecord record)>();
            foreach (var write in plan.Rows)
            {
                if (plan.Resolve(write.Key.id) is { IsRemoved: false }
                    || !cache.TryGetIndexedObject(write.Key.id, out var layer, out var record))
                    continue;
                if (!graph.Nodes.TryGetValue(write.Key.id, out var node)
                    || node.Children.Count != 0 || cache.CarriesTiles(write.Key.id))
                    return false;
                removed[write.Key.id] = (layer!, record!);
            }
            if (removed.Count == 0)
                return false;
            var proven = new HashSet<string>(removed.Keys);
            var visiting = new HashSet<string>();
            foreach (var write in plan.Rows)
            {
                if (!TryGetCommittedValue(write.Key.id, out MemberValue? previous))
                {
                    // Detached copies preserve script aliases after removal.
                    if (write.Value is null)
                        continue;
                    if (write.Key.ownership != NeoValueOwnership.Session
                        || graph.Dependencies.Contains(write.Key.id) || cache.DependsOn(write.Key.id)
                        || PlacementParents(write.Key.id).Any(parent =>
                            !plan.Rows.ContainsKey((NeoValueOwnership.Session, parent))
                                || TryGetCommittedValue(parent, out MemberValue? _))
                        || write.Value.containerId is string container &&
                            (!plan.Rows.ContainsKey((NeoValueOwnership.Session, container))
                                || TryGetCommittedValue(container, out MemberValue? _)))
                        return false;
                    continue;
                }
                if (NeoSemanticJson.MemberRowsEqual(previous, write.Value))
                {
                    if (!TryGetCommittedValue(write.Key.ownership, write.Key.id, out MemberValue? _))
                        return false;
                    continue;
                }
                if (write.Value is { IsRemoved: false } || !BelongsToRemoved(write.Key.id))
                    return false;
            }
            if (candidateReplay is not null)
                foreach (string root in candidateReplay.AffectedRoots)
                    if (!BelongsToRemoved(root))
                        return false;
            plan.ValidatedObjectRemovalGrid = gridId;
            cache.PrepareObjectRemoval(plan, removed);
            return true;

            bool BelongsToRemoved(string id)
            {
                if (proven.Contains(id))
                    return true;
                if (!visiting.Add(id))
                    return false;
                var parents = new HashSet<string>(PlacementParents(id));
                if (virtualClassPlacementByChildId.TryGetValue(id, out var placement))
                    parents.Add(placement.parentValueId);
                if (TryGetCommittedValue(id, out MemberValue? row) && row.containerId is string container)
                    parents.Add(container);
                bool belongs = parents.Count != 0 && parents.All(BelongsToRemoved);
                visiting.Remove(id);
                if (belongs)
                    proven.Add(id);
                return belongs;
            }
        }
    }

    internal sealed partial class NeoTileGridLookupCache
    {
        internal void PrepareObjectRemoval(NeoWritePlan plan,
            Dictionary<string, (string layer, NeoObjectPlacementRecord record)> removed)
        {
            plan.AfterCommit(() =>
            {
                var changes = new List<NeoObjectLayerChangedArgs>();
                foreach (var group in removed.GroupBy(pair => pair.Value.layer))
                {
                    var index = objectLayers[group.Key];
                    var ids = new HashSet<string>(group.Select(pair => pair.Key));
                    var cells = new HashSet<Vector2Int>();
                    foreach (var pair in group)
                    {
                        primitive.RemovePlacementGraphObject(pair.Key);
                        cells.UnionWith(pair.Value.record.Footprint);
                    }
                    // Keep lists already handed to an iterator stable. Reindex
                    // cached records without rereading any surviving value graph.
                    var records = new List<NeoObjectPlacementRecord>(index.Records.Count - ids.Count);
                    foreach (var record in index.Records)
                        if (!ids.Contains(record.InstanceId))
                            records.Add(record.Order == records.Count ? record : WithOrder(record, records.Count));
                    objectLayers[group.Key] = BuildObjectLayerIndex(records, index.DependencyIds);
                    changes.Add(new NeoObjectLayerChangedArgs(group.Key,
                        ids.Select(id => new NeoObjectInstanceId(id)).ToArray(),
                        Array.Empty<NeoObjectInstanceId>(), new List<Vector2Int>(cells),
                        NeoTileGridChangeSourceKind.Direct, null));
                }
                DropObjectLayers();
                var change = new NeoTileGridChangedArgs(primitive.GridValueId, objectLayers: changes,
                    source: primitive.Client.CurrentChangeSource);
                primitive.Client.ScriptGridQueries.NotifyChanged(change);
                plan.AfterNotifications(() => Changed?.Invoke(change));
            });
        }
    }
}
