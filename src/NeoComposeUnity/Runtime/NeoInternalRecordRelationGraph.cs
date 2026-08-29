// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public sealed class NeoEffectiveInternalRecordRelation
    {
        internal NeoEffectiveInternalRecordRelation(
            InternalRecordRelation declaration,
            string targetRecordId,
            int sourceAncestryDepth)
        {
            RelationKind = declaration.relationKind;
            SourceRecordId = declaration.sourceRecordId;
            TargetRecordId = targetRecordId;
            DeclaredSourceRecordId = declaration.sourceRecordId;
            RelationIds = new List<string> { declaration.id };
            OrderKey = declaration.orderKey;
            SourceAncestryDepth = sourceAncestryDepth;
        }

        public string RelationKind { get; }
        public string SourceRecordId { get; }
        public string TargetRecordId { get; }
        public string DeclaredSourceRecordId { get; }
        public IReadOnlyList<string> RelationIds { get; internal set; }
        public string? OrderKey { get; }
        public int SourceAncestryDepth { get; }
    }

    /// <summary>
    /// Resolves declared class relations with the same source-inheritance,
    /// target-expansion, ordering, and nearest-single semantics as web
    /// authoring/codegen. No inherited relation rows are materialized.
    /// </summary>
    public sealed class NeoInternalRecordRelationGraph
    {
        private enum MergeKind
        {
            Union,
            OrderedUnion,
            NearestSingle,
        }

        private sealed class Contract
        {
            public Contract(bool expandTargets, MergeKind merge)
            {
                ExpandTargets = expandTargets;
                Merge = merge;
            }

            public bool ExpandTargets { get; }
            public MergeKind Merge { get; }
        }

        private sealed class TargetSelection
        {
            public TargetSelection(InternalRecordRelation relation, int depth)
            {
                Selected = relation;
                SelectedDepth = depth;
                Declarations.Add(relation);
            }

            public InternalRecordRelation Selected { get; set; }
            public int SelectedDepth { get; set; }
            public List<InternalRecordRelation> Declarations { get; } = new();
        }

        private static readonly IReadOnlyDictionary<string, Contract> Contracts =
            new Dictionary<string, Contract>(StringComparer.Ordinal)
            {
                [InternalRecordRelationKinds.WorldGridTileImport] =
                    new Contract(true, MergeKind.Union),
                [InternalRecordRelationKinds.WorldGridObjectImport] =
                    new Contract(true, MergeKind.Union),
                [InternalRecordRelationKinds.WorldGridTileLayer] =
                    new Contract(false, MergeKind.OrderedUnion),
                [InternalRecordRelationKinds.WorldGridObjectLayer] =
                    new Contract(false, MergeKind.OrderedUnion),
                [InternalRecordRelationKinds.WorldTileCompatibleLayer] =
                    new Contract(true, MergeKind.Union),
                [InternalRecordRelationKinds.WorldTileDefaultLayer] =
                    new Contract(false, MergeKind.NearestSingle),
                [InternalRecordRelationKinds.WorldObjectCompatibleLayer] =
                    new Contract(true, MergeKind.Union),
                [InternalRecordRelationKinds.WorldObjectDefaultLayer] =
                    new Contract(false, MergeKind.NearestSingle),
                [InternalRecordRelationKinds.WorldTileLayerLinkTarget] =
                    new Contract(false, MergeKind.NearestSingle),
                [InternalRecordRelationKinds.WorldObjectLayerLinkTarget] =
                    new Contract(false, MergeKind.NearestSingle),
            };

        private readonly ProjectData data;
        private readonly Dictionary<string, IReadOnlyList<NeoEffectiveInternalRecordRelation>> cache =
            new(StringComparer.Ordinal);

        public NeoInternalRecordRelationGraph(ProjectData data)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public IReadOnlyList<NeoEffectiveInternalRecordRelation> Resolve(
            string relationKind,
            string sourceClassId)
        {
            if (!Contracts.TryGetValue(relationKind, out Contract? contract))
            {
                throw new InvalidOperationException(
                    $"Unknown internal record relation kind '{relationKind}'.");
            }
            if (!data.classes.ContainsKey(sourceClassId))
            {
                throw new InvalidOperationException(
                    $"Internal record relation source class '{sourceClassId}' does not exist.");
            }
            string cacheKey = relationKind + "\n" + sourceClassId;
            if (cache.TryGetValue(cacheKey, out var cached)) return cached;

            var ancestry = ResolveAncestry(sourceClassId);
            var sourceDepth = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < ancestry.Count; i++) sourceDepth[ancestry[i]] = i;

            var declarations = new List<InternalRecordRelation>();
            foreach (InternalRecordRelation relation in data.internalRecordRelations.Values)
            {
                if (!string.Equals(relation.relationKind, relationKind, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!sourceDepth.ContainsKey(relation.sourceRecordId)) continue;
                declarations.Add(relation);
            }

            IReadOnlyList<NeoEffectiveInternalRecordRelation> result;
            if (contract.Merge == MergeKind.NearestSingle)
            {
                declarations.Sort((left, right) =>
                {
                    int depth = sourceDepth[left.sourceRecordId]
                        .CompareTo(sourceDepth[right.sourceRecordId]);
                    return depth != 0
                        ? depth
                        : string.CompareOrdinal(left.id, right.id);
                });
                if (declarations.Count > 1)
                {
                    int nearestDepth = sourceDepth[declarations[0].sourceRecordId];
                    string nearestTargetId = declarations[0].targetRecordId;
                    for (int i = 1; i < declarations.Count; i++)
                    {
                        InternalRecordRelation declaration = declarations[i];
                        if (sourceDepth[declaration.sourceRecordId] != nearestDepth) break;
                        if (!string.Equals(
                            declaration.targetRecordId,
                            nearestTargetId,
                            StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Internal record relation kind '{relationKind}' has ambiguous nearest declarations for source class '{sourceClassId}' at ancestry depth {nearestDepth}: targets '{nearestTargetId}' and '{declaration.targetRecordId}'.");
                        }
                    }
                }
                result = declarations.Count == 0
                    ? Array.Empty<NeoEffectiveInternalRecordRelation>()
                    : new[]
                    {
                        new NeoEffectiveInternalRecordRelation(
                            declarations[0],
                            declarations[0].targetRecordId,
                            sourceDepth[declarations[0].sourceRecordId]),
                    };
            }
            else
            {
                var byTarget = new Dictionary<string, TargetSelection>(StringComparer.Ordinal);
                foreach (InternalRecordRelation declaration in declarations)
                {
                    int depth = sourceDepth[declaration.sourceRecordId];
                    IReadOnlyList<string> targets = contract.ExpandTargets
                        ? ResolveConcreteDescendants(declaration.targetRecordId)
                        : new[] { declaration.targetRecordId };
                    foreach (string targetId in targets)
                    {
                        if (!byTarget.TryGetValue(targetId, out TargetSelection? selection))
                        {
                            byTarget[targetId] = new TargetSelection(declaration, depth);
                            continue;
                        }
                        selection.Declarations.Add(declaration);
                        if (depth < selection.SelectedDepth
                            || (depth == selection.SelectedDepth
                                && string.CompareOrdinal(
                                    declaration.id,
                                    selection.Selected.id) < 0))
                        {
                            selection.Selected = declaration;
                            selection.SelectedDepth = depth;
                        }
                    }
                }

                var effective = new List<NeoEffectiveInternalRecordRelation>();
                foreach (var pair in byTarget)
                {
                    var relation = new NeoEffectiveInternalRecordRelation(
                        pair.Value.Selected,
                        pair.Key,
                        pair.Value.SelectedDepth);
                    var ids = new List<string>();
                    foreach (var declaration in pair.Value.Declarations)
                    {
                        ids.Add(declaration.id);
                    }
                    ids.Sort(StringComparer.Ordinal);
                    relation.RelationIds = ids;
                    effective.Add(relation);
                }
                if (contract.Merge == MergeKind.OrderedUnion)
                {
                    effective.Sort((left, right) =>
                    {
                        int order = string.CompareOrdinal(
                            left.OrderKey ?? "",
                            right.OrderKey ?? "");
                        if (order != 0) return order;
                        int depth = left.SourceAncestryDepth.CompareTo(
                            right.SourceAncestryDepth);
                        if (depth != 0) return depth;
                        return string.CompareOrdinal(
                            left.RelationIds[0],
                            right.RelationIds[0]);
                    });
                }
                else
                {
                    effective.Sort((left, right) =>
                        string.CompareOrdinal(left.TargetRecordId, right.TargetRecordId));
                }
                result = effective;
            }
            cache[cacheKey] = result;
            return result;
        }

        public IReadOnlyList<string> ResolveTargetIds(
            string relationKind,
            string sourceClassId)
        {
            var result = new List<string>();
            foreach (var relation in Resolve(relationKind, sourceClassId))
            {
                result.Add(relation.TargetRecordId);
            }
            return result;
        }

        /// <summary>
        /// Resolves a singleton relation whose endpoints are exact records,
        /// without applying class source inheritance or target expansion.
        /// This is the generic path for non-class sources such as a smart-tile
        /// neighbor value targeting its selected tile class.
        /// </summary>
        public string? ResolveExactTargetId(
            string relationKind,
            string sourceRecordKind,
            string sourceRecordId,
            string targetRecordKind)
        {
            if (!string.Equals(
                relationKind,
                InternalRecordRelationKinds.WorldSmartTileNeighborTile,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Internal record relation kind '{relationKind}' does not use exact endpoint resolution.");
            }

            string? targetId = null;
            foreach (InternalRecordRelation relation in data.internalRecordRelations.Values)
            {
                if (!string.Equals(relation.relationKind, relationKind, StringComparison.Ordinal)
                    || !string.Equals(
                        relation.sourceRecordKind,
                        sourceRecordKind,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        relation.sourceRecordId,
                        sourceRecordId,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        relation.targetRecordKind,
                        targetRecordKind,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (targetId is not null)
                {
                    throw new InvalidOperationException(
                        $"Exact internal record relation '{relationKind}' has multiple targets for {sourceRecordKind} '{sourceRecordId}'.");
                }
                targetId = relation.targetRecordId;
            }
            return targetId;
        }

        private IReadOnlyList<string> ResolveAncestry(string classId)
        {
            var result = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = classId;
            while (cursor is not null)
            {
                if (!visited.Add(cursor))
                {
                    throw new InvalidOperationException(
                        $"Class inheritance contains a cycle at '{cursor}'.");
                }
                result.Add(cursor);
                cursor = data.classes.TryGetValue(cursor, out NeoSchemaClass? schemaClass)
                    ? schemaClass.extendsClassId
                    : null;
            }
            return result;
        }

        private IReadOnlyList<string> ResolveConcreteDescendants(string classId)
        {
            var result = new List<string>();
            foreach (NeoSchemaClass candidate in data.classes.Values)
            {
                if (candidate.EffectiveModifier == NeoClassModifierKind.Abstract) continue;
                if (ContainsClass(ResolveAncestry(candidate.id), classId))
                {
                    result.Add(candidate.id);
                }
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static bool ContainsClass(
            IReadOnlyList<string> classIds,
            string classId)
        {
            for (int i = 0; i < classIds.Count; i++)
            {
                if (string.Equals(classIds[i], classId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
