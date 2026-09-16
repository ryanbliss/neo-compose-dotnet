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
        internal void AssertConstructedValueAdoptable(
            NeoValueOwnership targetOwnership, NeoGeneratedClassValue value)
        {
            if (!ReferenceEquals(value.Client, this))
                throw new ArgumentException("Constructed object belongs to another client.", nameof(value));
            if (value.IsReadOnly)
                throw new InvalidOperationException("Spawn requires a constructed writable object. Clone an authored object explicitly before spawning it.");
            string valueId = value.valueId ?? throw new InvalidOperationException("Constructed object has no backing row.");
            NeoValueOwnership sourceOwnership = value.ValueOwnership;
            if (sourceOwnership == NeoValueOwnership.Asset)
                throw new InvalidOperationException("Spawn cannot adopt an immutable asset object.");
            if (TryFindOwnedParent(sourceOwnership, valueId, out string? parentId))
                throw new InvalidOperationException($"Object '{valueId}' is already owned by '{parentId}'. Clone it explicitly before spawning it again.");
            if (sourceOwnership == targetOwnership) return;
            if (sourceOwnership != NeoValueOwnership.Session || targetOwnership != NeoValueOwnership.Save)
                throw new InvalidOperationException($"Cannot adopt object '{valueId}' from {sourceOwnership} into {targetOwnership}.");
            if (BuildReachableWritableValueIds(sourceOwnership).Contains(valueId))
                throw new InvalidOperationException($"Object '{valueId}' is a bound Session value and cannot be moved into Save. Clone it explicitly before spawning it.");
            if (OwnedValueGraphCollidesWithOwnership(
                    sourceOwnership, targetOwnership, valueId, value.BackingNode.member, new HashSet<string>()))
                throw new InvalidOperationException($"Object '{valueId}' has rows already owned by the destination graph.");
        }

        internal void ConvertTile(NeoValueOwnership ownership, string valueId, string targetClassId)
        {
            if (IsReplayingVirtualInstance)
                throw new InvalidOperationException("Stored constructor replay cannot convert a placed tile.");
            if (ownership == NeoValueOwnership.Asset)
                throw new InvalidOperationException("Cannot convert an immutable asset tile.");
            if (!TryGetWritableShadowSource(ownership, valueId, out ObjectMemberValue? source))
                throw new InvalidOperationException($"Tile conversion receiver '{valueId}' has no class row.");
            if (string.IsNullOrEmpty(source.classId) || !IsTileClass(source.classId!))
                throw new InvalidOperationException($"Value '{valueId}' is not a NeoTile instance.");
            if (!TryGetClass(targetClassId, out NeoSchemaClass? target))
                throw new ArgumentException($"Tile conversion target class '{targetClassId}' does not exist.", nameof(targetClassId));
            if (!IsTileClass(targetClassId))
                throw new ArgumentException($"Tile conversion target '{target.name}' is not a NeoTile class.", nameof(targetClassId));
            if (target.Modifier == NeoClassModifierKind.Abstract)
                throw new ArgumentException($"Tile conversion target '{target.name}' is abstract.", nameof(targetClassId));
            if (target.genericParams is { Count: > 0 })
                throw new ArgumentException($"Tile conversion target '{target.name}' has unbound generic parameters.", nameof(targetClassId));
            if (source.classId == targetClassId) return;
            MemberValue? effectiveCell = ResolveClassChildRow(source, "Cell", ownership);
            bool hasInstanceCell = effectiveCell is not null;
            if (effectiveCell is null)
            {
                // Sparse rows can inherit Cell from the declaration. Resolve
                // through the normal member reader before discarding the recipe.
                var receiverMember = new ClassMember
                {
                    id = "__neo_tile_conversion_receiver",
                    name = "TileConversionReceiver",
                    kind = MemberKind.Class,
                    classId = source.classId,
                };
                using var receiver = new NeoMemberClass(this, receiverMember, source.id, ownership);
                effectiveCell = receiver.Get<NeoMemberVector2Int>("Cell").value;
            }
            if (effectiveCell is not Vector2MemberValue { value: not null })
                throw new InvalidOperationException($"Tile conversion receiver '{valueId}' has no valid Cell value.");
            string? linkedCellId = null;
            bool keepPhysicalCell = source.value?.TryGetValue("Cell", out linkedCellId) == true
                && linkedCellId == effectiveCell.id && effectiveCell.mapKey == source.mapKey
                && (GetWritableStore(ownership).values.TryGetValue(linkedCellId, out MemberValue? physicalCell)
                    || data.values.TryGetValue(linkedCellId, out physicalCell))
                && ReferenceEquals(physicalCell, effectiveCell);
            MemberValue cell = keepPhysicalCell ? effectiveCell : CloneRowForWrite(effectiveCell);
            // A declaration default is shared. Give the placed child its own
            // identity; concrete and virtual instance child ids stay stable.
            if (!hasInstanceCell)
                cell.id = Guid.NewGuid().ToString();
            if (!keepPhysicalCell) cell.mapKey = source.mapKey;
            string cellValueId = cell.id;

            // A class swap has no constructor invocation. Keep the placement
            // envelope and its Cell, and read the new class's declaration defaults.
            var converted = new ObjectMemberValue
            {
                id = source.id,
                createdAt = source.createdAt,
                updatedAt = NeoTimestamp.Now(),
                classId = targetClassId,
                containerId = source.containerId,
                mapKey = source.mapKey,
                sourceValueId = source.sourceValueId,
                value = new Dictionary<string, string> { ["Cell"] = cellValueId },
            };
            if (keepPhysicalCell) SetWritableValue(ownership, converted);
            else SetWritableValues(ownership, new MemberValue[] { cell, converted });
        }

        private bool IsTileClass(string classId)
        {
            foreach (NeoSchemaClass schemaClass in ResolveClassInheritanceChain(classId))
            {
                if (schemaClass.system?["worldKind"]?.ToString() == "tile") return true;
            }
            return false;
        }
    }
}
