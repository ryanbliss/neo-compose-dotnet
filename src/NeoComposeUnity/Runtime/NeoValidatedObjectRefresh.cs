// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// A write to a placed object that the shared placement validator proved
    /// leaves its placement alone: the object keeps its container, class,
    /// asset, cell and footprint, and carries no tiles. A variant swap is the
    /// common case. The lookup cache keeps its layers and reports the object
    /// changed, without rebuilding them.
    /// </summary>
    internal sealed class NeoValidatedObjectRefresh
    {
        internal NeoValidatedObjectRefresh(
            string gridValueId,
            string layerId,
            string objectId,
            string containerId,
            IReadOnlyList<Vector2Int> footprint,
            HashSet<string> placementDependencyIds,
            HashSet<string> carriedDependencyIds)
        {
            GridValueId = gridValueId;
            LayerId = layerId;
            ObjectId = objectId;
            ContainerId = containerId;
            Footprint = footprint;
            PlacementDependencyIds = placementDependencyIds;
            CarriedDependencyIds = carriedDependencyIds;
        }

        internal string GridValueId
        {
            get;
        }
        internal string LayerId
        {
            get;
        }
        internal string ObjectId
        {
            get;
        }
        internal string ContainerId
        {
            get;
        }
        internal IReadOnlyList<Vector2Int> Footprint
        {
            get;
        }
        // What the object's placement reads now, which may name rows its
        // indexed record never read.
        internal HashSet<string> PlacementDependencyIds
        {
            get;
        }
        // The composition a tile layer searches for carried links.
        internal HashSet<string> CarriedDependencyIds
        {
            get;
        }
    }

    public partial class NeoClient
    {
        /// <summary>
        /// Whether written object <paramref name="candidate"/> still places as
        /// a committed layer index records it. Such a write changes no
        /// placement around the object, so it is validated alone and its grid
        /// is not walked. Reads the candidate graph and builds no index.
        /// </summary>
        private bool TryValidateUnmovedObject(NeoWritePlan plan, MemberValue? candidate, MemberValue? previous,
            List<NeoValidatedObjectRefresh> refreshes)
        {
            if (plan.Bindings.Count != 0
                || candidate is not ObjectMemberValue { classId: not null } next
                || next.IsRemoved
                || previous is not ObjectMemberValue { IsRemoved: false } before
                || before.classId != next.classId
                || string.IsNullOrEmpty(next.containerId)
                || before.containerId != next.containerId)
                return false;
            NeoTileGridLookupCache? cache = null;
            string? layerId = null;
            NeoObjectPlacementRecord? record = null;
            foreach (var pair in gridLookupCaches)
            {
                if (pair.Value.TryGetIndexedObject(next.id, out layerId, out record))
                {
                    cache = pair.Value;
                    break;
                }
            }
            // Tiles an object carried before the write are not proven here.
            if (cache is null || cache.CarriesTiles(next.id))
                return false;
            var placementDependencies = new HashSet<string>(StringComparer.Ordinal);
            var carriedDependencies = new HashSet<string>(StringComparer.Ordinal);
            if (!cache.Primitive.PlacesAs(next, record!, placementDependencies)
                || cache.Primitive.HasObjectCarriedTiles(next, carriedDependencies))
                return false;
            // The checks a whole-grid validation runs on one object; its
            // membership, class and layer are unchanged.
            RequirePlacementClass(next.id, "object");
            ValidateObjectPosition(next);
            ValidateObjectVariant(next, next.classId);
            refreshes.Add(new NeoValidatedObjectRefresh(cache.Primitive.GridValueId, layerId!, next.id,
                next.containerId!, record!.Footprint, placementDependencies, carriedDependencies));
            return true;
        }
    }

    internal sealed partial class NeoTileGridLookupCache
    {
        // Refreshed objects whose layers stayed built, reported once the commit publishes.
        private List<NeoValidatedObjectRefresh>? refreshedObjects;

        /// <summary>The built object layer index holding <paramref name="objectId"/>, building none.</summary>
        internal bool TryGetIndexedObject(string objectId, out string? layerId, out NeoObjectPlacementRecord? record)
        {
            foreach (var pair in objectLayers)
            {
                if (pair.Value.ById.TryGetValue(objectId, out record))
                {
                    layerId = pair.Key;
                    return true;
                }
            }
            layerId = null;
            record = null;
            return false;
        }

        /// <summary>Whether a built tile layer holds a tile <paramref name="objectId"/> carries.</summary>
        internal bool CarriesTiles(string objectId)
        {
            foreach (var layer in tileLayers.Values)
            {
                if (layer.RecordIndicesByObject.ContainsKey(objectId))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A refreshed object's row and its container: the container hears
        /// about the row, but its membership did not change, and the row
        /// places as before. Neither changes a record of this grid.
        /// </summary>
        private void ExcludeRefreshedObjects(NeoWritePlan plan, HashSet<string> ids)
        {
            if (plan.ValidatedObjectRefreshes is not { } refreshes)
                return;
            foreach (var refresh in refreshes)
            {
                if (refresh.GridValueId != primitive.GridValueId)
                    continue;
                ids.Remove(refresh.ObjectId);
                ids.Remove(refresh.ContainerId);
            }
        }

        /// <summary>
        /// Keeps each refreshed object's layers, which now also depend on
        /// whatever its placement and composition read.
        /// </summary>
        private void KeepRefreshedObjects(NeoWritePlan plan)
        {
            if (plan.ValidatedObjectRefreshes is not { } refreshes)
                return;
            foreach (var refresh in refreshes)
            {
                if (refresh.GridValueId != primitive.GridValueId)
                    continue;
                foreach (var layer in tileLayers.Values)
                    layer.DependencyIds.UnionWith(refresh.CarriedDependencyIds);
                // A dropped layer reports the object through its rebuild.
                if (!objectLayers.TryGetValue(refresh.LayerId, out var index)
                    || !index.ById.ContainsKey(refresh.ObjectId))
                    continue;
                index.DependencyIds.UnionWith(refresh.PlacementDependencyIds);
                (refreshedObjects ??= new()).Add(refresh);
            }
        }

        /// <summary>Reports each refreshed object changed in place, as a rebuild's diff would.</summary>
        private void AddRefreshedObjectChanges(List<NeoObjectLayerChangedArgs> objects)
        {
            if (refreshedObjects is not { Count: not 0 } refreshes)
                return;
            for (int i = 0; i < refreshes.Count; i++)
            {
                string layerId = refreshes[i].LayerId;
                if (changedObjectLayers.ContainsKey(layerId))
                    continue;
                bool reported = false;
                for (int j = 0; j < i; j++)
                    reported |= refreshes[j].LayerId == layerId;
                if (reported)
                    continue;
                var updated = new List<NeoObjectInstanceId>();
                var cells = new HashSet<Vector2Int>();
                for (int j = i; j < refreshes.Count; j++)
                {
                    if (refreshes[j].LayerId != layerId)
                        continue;
                    updated.Add(refreshes[j].ObjectId);
                    cells.UnionWith(refreshes[j].Footprint);
                }
                objects.Add(new NeoObjectLayerChangedArgs(layerId, Array.Empty<NeoObjectInstanceId>(), updated,
                    new List<Vector2Int>(cells), NeoTileGridChangeSourceKind.Direct, null));
            }
            refreshes.Clear();
        }
    }
}
