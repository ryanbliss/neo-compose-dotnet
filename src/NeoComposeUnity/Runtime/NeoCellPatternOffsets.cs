// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Reads a NeoCellPattern's <c>_offsets</c> as one array. A row's read is
    /// a getter memo entry kept on its node, so a change to the list or any
    /// entry drops it like any memoized getter, and an enclosing getter
    /// records the rows it read. A pending temporary keeps its read until its
    /// offsets slot changes.
    /// </summary>
    internal static class NeoCellPatternOffsets
    {
        internal const string ClassId = "system_fb9c4080-0148-530e-b9c0-8cd1c17ff8e7";
        internal const string ConstructorId = "system_f68684d6-6ea0-5d57-9f96-d544b17bf369";
        // The memo keys a row's read by the offsets field.
        private const string OffsetsMemberId = "system_02a01003-41ff-531c-a63c-1e905ca88d7d";
        private const string OffsetsKey = "_offsets";

        private sealed class DetachedRead
        {
            internal readonly object?[] entries;
            internal readonly Vector2Int[] offsets;

            internal DetachedRead(object?[] entries, Vector2Int[] offsets)
            {
                this.entries = entries;
                this.offsets = offsets;
            }
        }

        internal static Vector2Int[] Read(object? value, NSGetterEvaluator.Context ctx)
        {
            if (value is NeoGeneratedCellPatternValue view)
                return view.Offsets;
            if (value is NeoScriptObject { attachedId: null } detached && detached.plan.classId == ClassId)
                return ReadDetached(detached);
            NSGetterEvaluator.RowReference? rowRef = NSGetterEvaluator.FindRowReference(value, ctx);
            string id = rowRef?.valueId ?? throw new NSGetterRuntimeError("Cell pattern value must be a NeoCellPattern instance.");
            NeoValueOwnership ownership = NSGetterEvaluator.RowOwnership(rowRef, value) ?? ctx.valueOwnership;
            NeoValueNode? node = rowRef!.node;
            Vector2Int[] offsets = ReadRow(ctx.client, ownership, id, ref node);
            if (!ReferenceEquals(rowRef.node, node))
                rowRef.node = node;
            return offsets;
        }

        /// <param name="rowNode">The view's row node, kept between reads.</param>
        internal static Vector2Int[] Read(NeoGeneratedCellPatternValue view, ref NeoValueNode? rowNode)
        {
            // Reading the backing node would attach a pending temporary.
            if (view.PendingValue is NeoScriptObject pending)
            {
                if (pending.attachedId is not string attached)
                    return ReadDetached(pending);
                return ReadRow(view.Client, NeoValueOwnership.Session, attached, ref rowNode);
            }
            NeoMemberClass node = view.BackingNode;
            string id = node.overrideValueId ?? node.value?.id
                ?? throw new InvalidOperationException("NeoCellPattern has no backing value.");
            return ReadRow(view.Client, node.ownership, id, ref rowNode);
        }

        private static Vector2Int[] ReadDetached(NeoScriptObject detached)
        {
            if (!detached.plan.slotByKey.TryGetValue(OffsetsKey, out int slot)
                || NeoGeneratedTypesSupport.DetachedArray(detached, slot) is not object?[] entries)
                throw new InvalidOperationException("NeoCellPattern offsets are missing.");
            if (detached.nativeValue is DetachedRead read && ReferenceEquals(read.entries, entries))
                return read.offsets;
            var offsets = new Vector2Int[entries.Length];
            for (int index = 0; index < offsets.Length; index++)
            {
                offsets[index] = entries[index] is NeoVector2Value vector
                    ? NeoVectorValues.ToVector2Int(vector)
                    : throw new NSGetterRuntimeError("NeoCellPattern offsets must contain Vector2Int values.");
            }
            // The constructor is still filling the slot.
            if (!detached.constructing)
                detached.nativeValue = new DetachedRead(entries, offsets);
            return offsets;
        }

        private static Vector2Int[] ReadRow(NeoClient client, NeoValueOwnership ownership, string id, ref NeoValueNode? node)
        {
            if (client.ReadValue(ownership, id, ref node) is not ObjectMemberValue { classId: ClassId } row)
                throw new NSGetterRuntimeError("Cell pattern value must be a NeoCellPattern instance.");
            // The overlay reads of a replay or plan leave no node to keep a read on.
            if (node is null || !client.CanMemoizeGetters)
                return Walk(client, row, ownership);
            if (node.nativeRead is NeoClient.GetterMemoEntry { forgotten: false } kept
                && kept.key.ownership == ownership
                && client.HoldsCurrentReads(kept))
            {
                client.ReplayGetterReads(kept);
                return (Vector2Int[])kept.scalar!;
            }
            NeoClient.GetterCaptureFrame enclosing = client.BeginGetterReadCapture();
            Vector2Int[] offsets;
            NeoClient.GetterCaptureFrame capture;
            try
            {
                offsets = Walk(client, row, ownership);
            }
            finally
            {
                capture = client.EndGetterReadCapture(enclosing);
            }
            node.nativeRead = client.MemoizeGetter(
                new NeoClient.GetterMemoKey(ownership, id, OffsetsMemberId, ownership), offsets, null, capture);
            return offsets;
        }

        private static Vector2Int[] Walk(NeoClient client, ObjectMemberValue row, NeoValueOwnership ownership)
        {
            client.NoteRowRead(row.id);
            if (client.ResolveClassChildRow(row, OffsetsKey, ownership) is not ArrayMemberValue list)
                throw new InvalidOperationException("NeoCellPattern offsets are missing.");
            client.NoteValueRead(list.id);
            client.NoteRowRead(list.id);
            string[] ids = list.value ?? Array.Empty<string>();
            var offsets = new Vector2Int[ids.Length];
            for (int index = 0; index < ids.Length; index++)
            {
                NeoValueNode? entryNode = null;
                if (client.ReadValue(ownership, ids[index], ref entryNode) is not Vector2MemberValue { value: not null } vector)
                    throw new NSGetterRuntimeError("NeoCellPattern offsets must contain Vector2Int values.");
                client.NoteRowRead(ids[index]);
                offsets[index] = NeoVectorValues.ToVector2Int(vector.value);
            }
            return offsets;
        }
    }
}
