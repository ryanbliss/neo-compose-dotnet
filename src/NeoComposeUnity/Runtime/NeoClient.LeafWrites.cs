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
        // A runtime leaf write replaces one scalar row at its stable id. The
        // row keeps its type, class, container, map key and source, and no
        // ancestor is a world placement, so nothing structural can change.
        // Such a write skips the write plan, candidate replay and validation
        // walk: it stores the row, bumps the revision, forgets the getters
        // that read it and fans out the notifications a committed plan
        // would. Every other write still goes through CommitWritePlan.
        private readonly Dictionary<string, bool> worldPlacementClassIds = new(StringComparer.Ordinal);
        private readonly List<string> leafWriteAncestorScratch = new();
        private static readonly Unity.Profiling.ProfilerMarker LeafWriteMarker = new("NeoCompose.Write.Leaf");

        /// <summary>
        /// Stores <paramref name="next"/> in place of the committed row at the
        /// same id when the write is a plain leaf replacement. Returns false,
        /// having changed nothing, when the write needs a full plan.
        /// </summary>
        internal bool TryWriteLeaf(NeoValueOwnership ownership, MemberValue next, Member member, string? changedField)
        {
            if (ownership == NeoValueOwnership.Asset || !IsLeafRow(next, member)) return false;
            if (candidateReplay is not null || candidateReadPlan is not null
                || nestedConstructorCapture is not null || replayAllocationScope is not null
                || isReplayingVirtualInstance)
                return false;
            if (next.IsRemoved || next.hasInstanceConstructorId || next.constructorArgs is not null
                || next.instanceVariantId is not null || next.instanceVariantRowValueId is not null)
                return false;
            // The first write over a virtual (sparse) child materializes it
            // through the plan; from then on the store holds the row.
            if (!GetWritableStore(ownership).values.TryGetValue(next.id, out MemberValue? previous)
                && !data.values.TryGetValue(next.id, out previous))
                return false;
            StampMapKeyForWrite(ownership, next);
            if (previous.IsRemoved || previous.GetType() != next.GetType()
                || previous.classId != next.classId || previous.containerId != next.containerId
                || previous.mapKey != next.mapKey || previous.sourceValueId != next.sourceValueId
                || IsUnderWorldPlacement(next.id))
                return false;

            using var marker = LeafWriteMarker.Auto();
            // Same bookkeeping as a committed plan: a row a nested constructor
            // produced can no longer be replayed from its arguments.
            if (nestedConstructedRows is not null
                && nestedConstructedRows.TryGetValue(next.id, out var producer)
                && !ReferenceEquals(producer, nestedConstructorCapture))
                producer.HasExternalWrites = true;
            StoreWritableValue(ownership, next);
            TouchWritableStoreUpdatedAt(ownership);
            WriteRevision++;
            InvalidateGetterMemoForRow(next.id);
            if (!string.IsNullOrEmpty(next.containerId)) InvalidateGetterMemoForRow(next.containerId!);
            NotifyWritableValueChanged(ownership, next.id, changedField);
            return true;
        }

        private static bool IsLeafRow(MemberValue row, Member member) => row switch
        {
            NumberMemberValue or StringMemberValue or BoolMemberValue
                or Vector2MemberValue or Vector3MemberValue or ColorMemberValue
                or FileMemberValue or SpriteMemberValue => true,
            ArrayMemberValue => member is EnumMember or LookupMember or DialogueLookupMember,
            _ => false,
        };

        /// <summary>
        /// Whether two leaf rows hold the same value, so a setter can skip
        /// rewriting an override the store already holds.
        /// </summary>
        internal static bool SameLeafValue(MemberValue before, MemberValue after) => (before, after) switch
        {
            (NumberMemberValue a, NumberMemberValue b) => a.value == b.value,
            (BoolMemberValue a, BoolMemberValue b) => a.value == b.value,
            (StringMemberValue a, StringMemberValue b) => a.value == b.value && a.neoLocalizationMode == b.neoLocalizationMode,
            (ArrayMemberValue a, ArrayMemberValue b) => a.value is null ? b.value is null
                : b.value is not null && System.Linq.Enumerable.SequenceEqual(a.value, b.value),
            (Vector3MemberValue a, Vector3MemberValue b) => a.value is null ? b.value is null
                : b.value is not null && a.value.x == b.value.x && a.value.y == b.value.y && a.value.z == b.value.z,
            (Vector2MemberValue a, Vector2MemberValue b) => a.value is null ? b.value is null
                : b.value is not null && a.value.x == b.value.x && a.value.y == b.value.y,
            (ColorMemberValue a, ColorMemberValue b) => a.value is null ? b.value is null
                : b.value is not null && a.value.r == b.value.r && a.value.g == b.value.g
                    && a.value.b == b.value.b && a.value.a == b.value.a,
            (SpriteMemberValue a, SpriteMemberValue b) => a.value is null ? b.value is null
                : b.value is not null && a.value.fileId == b.value.fileId && a.value.sliceIndex == b.value.sliceIndex,
            (FileMemberValue a, FileMemberValue b) => a.value is null ? b.value is null
                : b.value is not null && a.value.fileId == b.value.fileId,
            _ => false,
        };

        // Grid, tile, object, layer and link rows carry placement invariants
        // that a write anywhere beneath them must validate. The scan follows
        // the same parent index the validation walk uses, without its
        // scratch sets; it is a handful of dictionary lookups per ancestor.
        private bool IsUnderWorldPlacement(string id)
        {
            List<string> pending = leafWriteAncestorScratch;
            pending.Clear();
            pending.Add(id);
            const int MaxVisited = 64;
            for (int index = 0; index < pending.Count; index++)
            {
                if (index == MaxVisited) { pending.Clear(); return true; }
                int start = pending.Count;
                CollectPlacementParents(pending[index], pending);
                for (int parent = start; parent < pending.Count; parent++)
                    if (IsWorldPlacementClass(ResolveValueRow(pending[parent])?.classId))
                    { pending.Clear(); return true; }
            }
            pending.Clear();
            return false;
        }

        private bool IsWorldPlacementClass(string? classId)
        {
            if (string.IsNullOrEmpty(classId)) return false;
            if (!worldPlacementClassIds.TryGetValue(classId!, out bool world))
                worldPlacementClassIds[classId!] = world = HasWorldKind(classId, "tileGrid")
                    || HasWorldKind(classId, "tile") || HasWorldKind(classId, "object")
                    || HasWorldKind(classId, "tileLayer") || HasWorldKind(classId, "objectLayer")
                    || HasWorldKind(classId, "tileLayerLink") || HasWorldKind(classId, "objectLayerLink");
            return world;
        }
    }
}
