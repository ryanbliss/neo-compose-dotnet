// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// The RuleTile that generated smart tile assets persist as. Unity finds a
    /// ScriptableObject's script by file name, so this class must stay in
    /// NeoRuleTile.cs; otherwise a saved asset has no script and can't load.
    /// </summary>
    public sealed class NeoRuleTile : RuleTile
    {
        [SerializeField]
        private List<NeoRuleTileCustomNeighbor> serializedCustomNeighbors = new();

        [NonSerialized]
        private Dictionary<int, NeoRuleTileNeighbor>? customNeighbors;

        private INeoSmartTileNeighborMatcher? matcher;

        public void Configure(INeoSmartTileNeighborMatcher? neighborMatcher)
        {
            matcher = neighborMatcher;
            customNeighbors = null;
        }

        public int RegisterCustomNeighbor(NeoRuleTileNeighbor neighbor)
        {
            if (neighbor == null)
                throw new ArgumentNullException(nameof(neighbor));
            var neighbors = CustomNeighbors;
            int id = 1000 + neighbors.Count;
            neighbors[id] = neighbor;
            serializedCustomNeighbors.Add(NeoRuleTileCustomNeighbor.From(id, neighbor));
            return id;
        }

        public override bool RuleMatch(int neighbor, TileBase other)
        {
            if (CustomNeighbors.TryGetValue(neighbor, out var customNeighbor))
            {
                return matcher?.Matches(customNeighbor, other) ?? false;
            }

            return base.RuleMatch(neighbor, other);
        }

        private Dictionary<int, NeoRuleTileNeighbor> CustomNeighbors
        {
            get
            {
                if (customNeighbors != null)
                    return customNeighbors;
                customNeighbors = new Dictionary<int, NeoRuleTileNeighbor>();
                foreach (var entry in serializedCustomNeighbors)
                {
                    customNeighbors[entry.Id] = entry.ToNeighbor();
                }
                return customNeighbors;
            }
        }
    }

    [Serializable]
    internal sealed class NeoRuleTileCustomNeighbor
    {
        [SerializeField]
        private int id;
        [SerializeField]
        private Vector3Int offset;
        [SerializeField]
        private NeoSmartTileNeighborKind kind;
        [SerializeField]
        private string tileClassId = "";

        public int Id => id;

        public static NeoRuleTileCustomNeighbor From(
            int id,
            NeoRuleTileNeighbor neighbor)
        {
            return new NeoRuleTileCustomNeighbor
            {
                id = id,
                offset = neighbor.Offset,
                kind = neighbor.Kind,
                tileClassId = neighbor.TileClassId ?? "",
            };
        }

        public NeoRuleTileNeighbor ToNeighbor()
        {
            return new NeoRuleTileNeighbor(
                offset,
                kind,
                string.IsNullOrWhiteSpace(tileClassId) ? null : tileClassId);
        }
    }
}
