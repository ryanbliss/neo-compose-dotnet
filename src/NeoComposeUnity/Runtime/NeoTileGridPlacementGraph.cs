// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public partial class NeoReadOnlyTileGridPrimitive
    {
        private GridPlacementGraph? placementGraph;

        internal bool RetainPlacementGraph;
        internal GridPlacementGraph PlacementGraph => RetainPlacementGraph
            ? placementGraph ??= BuildPlacementGraph() : BuildPlacementGraph();

        internal void ForgetPlacementGraph() => placementGraph = null;

        internal bool PlacementGraphDependsOn(HashSet<string> ids) => placementGraph?.Dependencies.Overlaps(ids) != false;

        // One node per placement or ancestor leading to placements. Pure visual
        // subtrees contribute structural dependencies but occupy no graph nodes.
        internal sealed class PlacementNode
        {
            internal readonly ObjectMemberValue Row;
            internal readonly List<PlacementNode> Children = new();
            internal PlacementNode? Parent;
            internal NeoGridLayerLinkModel? Link;
            internal string? PlacementLayerId;
            internal string? SourceLinkId;
            internal string? RootObjectId;
            internal string? ParentObjectId;
            internal NeoValueOwnership? Ownership;
            internal (double x, double y) Position;
            internal (double x, double y) Offset;
            internal bool Enabled;
            internal bool IsRootPlacement;
            internal bool CarriesTiles;
            internal string? PositionRowId;
            internal string? EnabledRowId;

            internal PlacementNode(ObjectMemberValue row) => Row = row;

            internal Vector2Int Cell => RoundedPlacementCell(Offset.x, Offset.y);
        }

        internal sealed class GridPlacementGraph
        {
            internal readonly Dictionary<string, PlacementNode> Nodes = new(StringComparer.Ordinal);
            internal readonly List<PlacementNode> Ordered = new();
            internal readonly HashSet<string> Dependencies = new(StringComparer.Ordinal);
            internal readonly HashSet<string> EnabledDependencies = new(StringComparer.Ordinal);
            internal readonly List<string> TileLayerIds = new();
            internal readonly List<string> ObjectLayerIds = new();
        }

        internal GridPlacementGraph BuildDetachedPlacementGraph(ObjectMemberValue root)
        {
            var graph = new GridPlacementGraph();
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            if (ResolveLinkModel(root.id, graph.Dependencies) is { } link)
            {
                var node = new PlacementNode(root) { Link = link, Enabled = true };
                AddPlacementNode(graph, node);
                visiting.Add(root.id);
                if (!link.IsTileLink)
                    CollectPlacementMembers(graph, node, visiting, 0);
            }
            else
            {
                var parent = new PlacementNode(root) { RootObjectId = root.id, Enabled = true };
                CollectPlacementNode(graph, root.id, parent, visiting, 0);
            }
            return graph;
        }

        private GridPlacementGraph BuildPlacementGraph()
        {
            var graph = new GridPlacementGraph();
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var links = ResolveGridLinks(graph.Dependencies);
            // Root layer order governs cross-layer queries even when an earlier
            // object contributes a nested link to a later layer.
            foreach (var link in links)
            {
                var ids = link.IsTileLink ? graph.TileLayerIds : graph.ObjectLayerIds;
                if (!ids.Contains(link.LayerId))
                    ids.Add(link.LayerId);
            }
            foreach (var link in links)
            {
                if (client.ResolveValueRow(link.LinkValueId) is not ObjectMemberValue row)
                    continue;
                var node = new PlacementNode(row)
                {
                    Link = link,
                    Position = ReadPosition(row, graph.Dependencies, null),
                    Enabled = ReadEnabled(row, graph.Dependencies, graph.EnabledDependencies),
                    PositionRowId = client.ResolveClassChildRow(row, "Position")?.id,
                    EnabledRowId = client.ResolveClassChildRow(row, "Enabled")?.id,
                    CarriesTiles = link.IsTileLink,
                };
                node.Offset = node.Position;
                AddPlacementNode(graph, node);
                visiting.Add(row.id);
                if (!link.IsTileLink)
                    CollectPlacementMembers(graph, node, visiting, 0);
                node.CarriesTiles |= node.Children.Exists(child => child.CarriesTiles);
                visiting.Remove(row.id);
            }
            return graph;
        }

        private static void AddPlacementNode(GridPlacementGraph graph, PlacementNode node)
        {
            if (!graph.Nodes.TryAdd(node.Row.id, node))
                throw new NeoPlacementValidationException("placement-owner-ambiguous", $"World object '{node.Row.id}' has multiple owners.");
            graph.Ordered.Add(node);
            if (node.Link is { } link)
            {
                var ids = link.IsTileLink ? graph.TileLayerIds : graph.ObjectLayerIds;
                if (!ids.Contains(link.LayerId))
                    ids.Add(link.LayerId);
            }
        }

        private void CollectPlacementMembers(GridPlacementGraph graph, PlacementNode link,
            HashSet<string> visiting, int depth)
        {
            var ownership = ResolveCollectionOwnership(link.Link!.ListValueId);
            foreach (string id in ResolveListEntryIds(link.Link.ListValueId, graph.Dependencies))
            {
                var node = CollectPlacementNode(graph, id, link, visiting, depth + 1,
                    link.Link.LayerId, link.Link.LinkValueId, ownership);
                if (node is not null)
                    link.Children.Add(node);
            }
        }

        private PlacementNode? CollectPlacementNode(GridPlacementGraph graph, string id, PlacementNode parent,
            HashSet<string> visiting, int depth, string? layerId = null, string? sourceLinkId = null,
            NeoValueOwnership? ownership = null)
        {
            graph.Dependencies.Add(id);
            if (client.ResolveValueRow(id) is not ObjectMemberValue { classId: not null } row || row.IsRemoved)
                return null;
            if (depth > MaxCompositionDepth || !visiting.Add(id))
                throw new NeoPlacementValidationException("placement-containment-cycle", $"World composition at '{id}' is cyclic or exceeds {MaxCompositionDepth} levels.");
            var node = new PlacementNode(row)
            {
                Parent = parent,
                PlacementLayerId = layerId,
                SourceLinkId = sourceLinkId,
                Ownership = ownership,
                RootObjectId = parent.RootObjectId,
                ParentObjectId = parent.PlacementLayerId is null ? parent.ParentObjectId : parent.Row.id,
                Enabled = parent.Enabled,
            };
            if (layerId is not null && node.RootObjectId is null)
            {
                node.IsRootPlacement = true;
                node.RootObjectId = id;
            }

            bool tileLink = client.HasWorldKind(row.classId, "tileLayerLink");
            bool objectLink = client.HasWorldKind(row.classId, "objectLayerLink");
            if (tileLink || objectLink)
            {
                string key = tileLink ? "Tiles" : "Objects";
                if (client.ResolveClassChildRow(row, key) is ArrayMemberValue list)
                {
                    graph.Dependencies.Add(list.id);
                    string target = NeoWorldLayerLinkResolver.ResolveTargetLayerClassId(client, id, row.classId, tileLink);
                    node.Link = new NeoGridLayerLinkModel(id, row.classId, target, null, list.id, tileLink);
                }
            }

            // Descendants need the accumulated origin. Position dependencies
            // for pruned visual leaves are removed below.
            node.Position = ReadPosition(row, null, null);
            node.Offset = (parent.Offset.x + node.Position.x, parent.Offset.y + node.Position.y);
            node.Enabled &= ReadEnabled(row, null, null);

            // Append the parent before its descendants, preserving authored
            // order across layer links. Remove it if it has no placement role.
            int first = graph.Ordered.Count;
            graph.Ordered.Add(node);
            if (node.Link is { IsTileLink: false })
                CollectPlacementMembers(graph, node, visiting, depth);
            if (client.ResolveClassChildRow(row, "Children") is ArrayMemberValue children)
            {
                foreach (string childId in ResolveListEntryIds(children.id, graph.Dependencies))
                {
                    var child = CollectPlacementNode(graph, childId, node, visiting, depth + 1);
                    if (child is not null)
                        node.Children.Add(child);
                }
            }
            visiting.Remove(id);
            if (node.Link is null && layerId is null && node.Children.Count == 0)
            {
                graph.Ordered.RemoveAt(first);
                return null;
            }
            if (!graph.Nodes.TryAdd(id, node))
                throw new NeoPlacementValidationException("placement-owner-ambiguous", $"World object '{id}' has multiple owners.");
            node.CarriesTiles = node.Link is { IsTileLink: true } || node.Children.Exists(child => child.CarriesTiles);
            node.PositionRowId = client.ResolveClassChildRow(row, "Position")?.id;
            node.EnabledRowId = client.ResolveClassChildRow(row, "Enabled")?.id;
            ReadPosition(row, graph.Dependencies, null);
            ReadEnabled(row, graph.Dependencies, graph.EnabledDependencies);
            if (node.Link is { } retainedLink)
            {
                var ids = retainedLink.IsTileLink ? graph.TileLayerIds : graph.ObjectLayerIds;
                if (!ids.Contains(retainedLink.LayerId))
                    ids.Add(retainedLink.LayerId);
            }
            return node;
        }
    }
}
