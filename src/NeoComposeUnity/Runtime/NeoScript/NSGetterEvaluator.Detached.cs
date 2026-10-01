// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using JsonMember = NeoCompose.Runtime.Json.Member;
using DetachedClassPlan = NeoCompose.Runtime.NeoGeneratedTypesSupport.DetachedClassPlan;
using DetachedSlot = NeoCompose.Runtime.NeoGeneratedTypesSupport.DetachedSlot;
using DetachedSlotKind = NeoCompose.Runtime.NeoGeneratedTypesSupport.DetachedSlotKind;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// The evaluator's side of <see cref="NeoScriptObject"/>: slot reads and
    /// writes for objects that are still detached, and the forwarding that
    /// turns an attached one back into its row's canonical record.
    /// </summary>
    public static partial class NSGetterEvaluator
    {
        /// <summary>
        /// True for an evaluator-local array: not a row's entry ids, and not a
        /// query result derived from them.
        /// </summary>
        internal static bool IsLocalCollection(object?[] entries, Context ctx) =>
            !ctx.rowReverseIndex.TryGetValue(entries, out _)
            && !DerivedEntryMembers.TryGetValue(entries, out _);

        /// <summary>
        /// The row id of <paramref name="value"/>, materializing its detached
        /// root first. A root materialized while the invocation that created
        /// it is still running joins that invocation's temporaries, exactly as
        /// a row-built constructor result does; one materialized later (a
        /// return to C#) is kept.
        /// </summary>
        internal static string AttachDetached(NeoScriptObject value, Context? ctx)
        {
            if (value.attachedId is string attached)
                return attached;
            NeoScriptObject root = value;
            while (root.owner is not null)
                root = root.owner;
            NeoScriptAllocationTracker tracker = root.tracker;
            bool live = tracker.ActiveExecutionCount > 0
                && tracker.Generation == root.trackerGeneration;
            Context? scopeCtx = live && ctx?.allocationTracker == tracker ? ctx : null;
            string rootId;
            try
            {
                rootId = NeoGeneratedTypesSupport.MaterializeDetached(root, scopeCtx);
            }
            catch (Exception error)
                when (error is InvalidOperationException || error is ArgumentException)
            {
                throw new NSGetterRuntimeError($"Class constructor failed: {error.Message}");
            }
            if (live)
                tracker.RegisterSessionRoot(rootId);
            return value.attachedId!;
        }

        /// <summary>
        /// A declared construction's evaluator value: a detached object when
        /// the class can live in slots, otherwise rows built eagerly. Replays
        /// and variant preparation always build rows.
        /// </summary>
        internal static object? ConstructDeclared(
            NeoGeneratedTypesSupport.NeoResolvedDeclaredConstructor resolved,
            object?[] argumentValues,
            IReadOnlyList<NeoGeneratedTypesSupport.RuntimeConstructorField> fields,
            Context ctx,
            Action<Context>? evaluateFieldValues,
            bool replayContext)
        {
            if (!replayContext
                && !ctx.client.IsReplayingVirtualInstance
                && !ctx.client.IsPreparingVariant
                && resolved.metadata.DetachedPlan(
                    ctx.client,
                    resolved.classTypeInfo.classId) is { } plan)
            {
                NeoScriptObject detached = NeoGeneratedTypesSupport.ConstructDetachedDeclared(
                    resolved,
                    plan,
                    argumentValues,
                    fields,
                    ctx,
                    evaluateFieldValues);
                return detached.attachedId is null ? detached : ForwardDetached(detached, ctx);
            }
            NeoGeneratedTypesSupport.RuntimeConstructedClassValue constructed =
                NeoGeneratedTypesSupport.ConstructDeclaredClassValueData(
                    resolved,
                    argumentValues,
                    fields,
                    ctx,
                    evaluateFieldValues);
            ctx.allocationTracker.RegisterSessionRoot(constructed.value.id);
            return UnwrapCached(constructed.value, ctx, NeoValueOwnership.Session, constructed.member);
        }

        /// <summary>
        /// The canonical record of <paramref name="value"/>'s row, so an
        /// object that had to materialize reads, writes and compares exactly
        /// like one built as rows.
        /// </summary>
        internal static object? ForwardDetached(NeoScriptObject value, Context ctx)
        {
            string id = AttachDetached(value, ctx);
            NeoValueOwnership ownership = ctx.client.TryGetValueOwnership(id, out NeoValueOwnership stored)
                ? stored
                : NeoValueOwnership.Session;
            if (!ctx.client.TryGetValue(ownership, id, out MemberValue? row))
                throw new NSGetterRuntimeError($"Class value '{id}' is no longer stored.");
            return UnwrapCached(row!, ctx, ownership, value.plan.runtimePlan.factoryMember);
        }

        /// <summary>A <see cref="KeyOf"/>'s slot index for one object plan and key.</summary>
        internal sealed class DetachedSlotSite
        {
            internal readonly DetachedClassPlan plan;
            internal readonly string key;
            internal readonly int index;
            internal readonly DetachedSlotSite? next;
            internal readonly int count;

            internal DetachedSlotSite(DetachedClassPlan plan, string key, int index, DetachedSlotSite? next)
            {
                this.plan = plan;
                this.key = key;
                this.index = index;
                this.next = next;
                count = (next?.count ?? 0) + 1;
            }
        }

        /// <summary>
        /// <paramref name="key"/>'s slot in <paramref name="plan"/>, cached on
        /// <paramref name="site"/> so a hot read or write skips hashing the key.
        /// </summary>
        internal static bool TryFindDetachedSlot(
            DetachedClassPlan plan,
            string key,
            KeyOf? site,
            Context ctx,
            out int index)
        {
            if (site is null)
                return plan.slotByKey.TryGetValue(key, out index);
            DetachedSlotSite? slots = site.detachedSlots;
            for (DetachedSlotSite? slot = slots; slot is not null; slot = slot.next)
            {
                if (ReferenceEquals(slot.plan, plan) && SameId(slot.key, key))
                {
                    index = slot.index;
                    return true;
                }
            }
            if (!plan.slotByKey.TryGetValue(key, out index))
                return false;
            if ((slots?.count ?? 0) < CallSiteTarget.MaxTargets)
            {
                site.detachedSlots = new DetachedSlotSite(plan, key, index, slots);
                if (slots is null)
                    ctx.client.RememberSchemaResolutionSite(site);
            }
            return true;
        }

        /// <summary>
        /// Reads <paramref name="key"/> off a detached object: a stored slot,
        /// an NSProperty getter bound to the object, or a read-only
        /// declaration default. False leaves every other key to the row path.
        /// </summary>
        private static bool TryReadDetachedMember(
            NeoScriptObject value,
            string key,
            Context ctx,
            out object? result,
            KeyOf? site = null)
        {
            if (TryFindDetachedSlot(value.plan, key, site, ctx, out int index))
            {
                if (!TryReadDetachedSlot(value, index, ctx, out result))
                    return false;
                // A lookup slot holds its selected ids, as its row does.
                if (value.plan.slots[index].member is LookupMember lookup && result is object?[] ids)
                    result = ReadLookupSelection(ids, lookup, ctx);
                return true;
            }
            result = null;
            MergedSchemaEntry? entry;
            try
            {
                entry = ctx.client.ResolveClassNode(value.plan.classId).SurfaceMember(key);
            }
            catch (CircularInheritanceError)
            {
                return false;
            }
            JsonMember? member = entry?.member;
            if (member is null)
                return false;
            if (member.Mutability == NeoMemberMutabilityKind.ReadOnly)
            {
                result = ReadOnlyDeclarationDefault(member, ctx);
                return true;
            }
            if (entry!.member is NSPropertyMember { getter: not null })
            {
                result = DispatchNSGetterById(entry.memberId, value, ctx);
                return true;
            }
            return false;
        }

        /// <summary>
        /// C#'s read of a pending object's stored member: the slot's value, or
        /// a List slot's entries. False once it attached, and for a member
        /// only a row can answer; the generated view then reads its row.
        /// </summary>
        internal static bool TryReadDetachedView(NeoScriptObject value, string key, out object? result)
        {
            result = null;
            if (value.attachedId is not null || !value.plan.slotByKey.TryGetValue(key, out int index))
                return false;
            DetachedSlot slot = value.plan.slots[index];
            if (slot.kind == DetachedSlotKind.Leaf && value.State(index) == NeoScriptObject.WrittenSlot)
            {
                // C# parses a bare option or lookup id itself and keeps no
                // array, so only a NeoScript reader needs the shared one.
                result = value.Slot(index);
                return true;
            }
            if (value.State(index) == NeoScriptObject.WrittenSlot
                || !slot.hasLiteralDefault
                || !IsLocalizedDefault(slot.member))
                return TryReadDetachedSlot(value, index, null, out result);
            // A localized default resolves its text on every read, as its
            // row's NeoMemberString.Text does, so a view held across a
            // locale change follows it.
            Context ctx = value.client.RentDirectFunctionContext(NeoValueOwnership.Session);
            try
            {
                result = ReadDefaultLeaf(value, slot, ctx);
                return true;
            }
            finally
            {
                value.client.ReturnDirectFunctionContext(ctx, null);
            }
        }

        /// <summary>
        /// C#'s read of a pending object's NSProperty: the getter runs with
        /// the temporary as <c>this</c>, as a NeoScript read of it would, so
        /// the read makes no rows.
        /// </summary>
        internal static NSGetterResult ComputeDetachedProperty(NeoScriptObject value, string key)
        {
            Context ctx = value.client.RentDirectFunctionContext(NeoValueOwnership.Session);
            object? result = null;
            try
            {
                MergedSchemaEntry? entry = ctx.client.ResolveClassNode(value.plan.classId).SurfaceMember(key);
                if (entry?.member is not NSPropertyMember { getter: not null })
                {
                    return NSGetterResult.Error(
                        "Compiled `getter` not yet available — save the code to compile it.");
                }
                result = DispatchNSGetterById(entry.memberId, value, ctx);
                return NSGetterResult.Ok(result);
            }
            catch (NSGetterRuntimeError ex)
            {
                return NSGetterResult.Error(ex.Message);
            }
            catch (Exception ex)
            {
                return NSGetterResult.Error($"Evaluator error: {ex.Message}");
            }
            finally
            {
                value.client.ReturnDirectFunctionContext(ctx, result);
            }
        }

        private static bool TryReadDetachedSlot(
            NeoScriptObject value,
            int index,
            Context? ctx,
            out object? result)
        {
            DetachedSlot slot = value.plan.slots[index];
            if (value.State(index) != NeoScriptObject.DefaultSlot)
            {
                result = NeoGeneratedTypesSupport.ExposeDetachedValue(
                    value,
                    index,
                    slot.kind == DetachedSlotKind.List
                        ? NeoGeneratedTypesSupport.DetachedArray(value, index)
                        : NeoGeneratedTypesSupport.DetachedLeaf(value, index));
                return true;
            }
            result = null;
            if (!slot.hasLiteralDefault)
                return false;
            switch (slot.kind)
            {
                case DetachedSlotKind.List:
                    // Fresh, never shared: the array's identity is its alias.
                    result = NeoGeneratedTypesSupport.SetDetachedArray(
                        value,
                        index,
                        new object?[0]);
                    break;
                case DetachedSlotKind.Leaf:
                    result = ReadDefaultLeaf(value, slot, ctx!);
                    NeoGeneratedTypesSupport.SetDetachedLeaf(value, index, result);
                    break;
            }
            value.MarkDefaultRead(index);
            result = NeoGeneratedTypesSupport.ExposeDetachedValue(value, index, result);
            return true;
        }

        private static bool IsLocalizedDefault(JsonMember member) =>
            member is StringMember { defaultValue: StringMemberValueBase text }
            && text.neoLocalizationMode != NeoStringLocalizationMode.Literal;

        /// <summary>
        /// A leaf member's literal default, read the way its default row reads.
        /// A number, bool, literal string or null is read once per slot.
        /// </summary>
        private static object? ReadDefaultLeaf(NeoScriptObject owner, DetachedSlot slot, Context ctx)
        {
            if (!ReferenceEquals(slot.sharedDefault, NeoGeneratedTypesSupport.UnreadDefault))
                return slot.sharedDefault;
            object? value = CreateDefaultLeaf(owner, slot.member, ctx);
            if (value is null or double or bool
                || value is string && !IsLocalizedDefault(slot.member))
                slot.sharedDefault = value;
            return value;
        }

        private static object? CreateDefaultLeaf(NeoScriptObject owner, JsonMember member, Context ctx)
        {
            MemberValue? row = MemberValueFactory.CreateFromDefault(
                member,
                string.Empty,
                default,
                default);
            return row switch
            {
                NumberMemberValue number => number.BoxedValue,
                BoolMemberValue boolean => Box(boolean.value),
                StringMemberValue text => member is StringMember stringMember
                    && text.neoLocalizationMode != NeoStringLocalizationMode.Literal
                        ? ResolveStringValue(text, stringMember, ctx)
                        : text.value,
                ArrayMemberValue options => options.value is null ? null : ToObjectArray(options.value),
                Vector2MemberValue vector => OwnedLeaf(owner, vector.value),
                Vector3MemberValue vector => OwnedLeaf(owner, vector.value),
                ColorMemberValue color => OwnedLeaf(owner, color.value),
                _ => null,
            };
        }

        private static object? OwnedLeaf(NeoScriptObject owner, NeoVector2Value? value)
        {
            if (value is not null)
                value.detachedOwner = owner;
            return value;
        }

        private static object? OwnedLeaf(NeoScriptObject owner, NeoColorValue? value)
        {
            if (value is not null)
                value.detachedOwner = owner;
            return value;
        }

        /// <summary>
        /// The row of a vector or color read out of a detached object's slot
        /// or List slot: the owner materializes, the same member is read back
        /// through its row, and the slot's value becomes an alias of that row,
        /// so a field write through it lands where it would have had the
        /// object been built as rows.
        /// </summary>
        private static bool TryFindDetachedLeafRow(
            object leaf,
            NeoScriptObject owner,
            Context ctx,
            out RowReference rowRef)
        {
            rowRef = null!;
            DetachedSlot[] slots = owner.plan.slots;
            int found = -1;
            int entry = -1;
            for (int index = 0; index < slots.Length && found < 0; index++)
            {
                if (slots[index].kind == DetachedSlotKind.Leaf)
                {
                    if (ReferenceEquals(owner.Slot(index), leaf))
                        found = index;
                    continue;
                }
                if (slots[index].kind != DetachedSlotKind.List
                    || NeoGeneratedTypesSupport.DetachedArray(owner, index) is not object?[] entries)
                {
                    continue;
                }
                entry = Array.IndexOf(entries, leaf);
                if (entry >= 0)
                    found = index;
            }
            if (found < 0)
                return false;
            object? read = DispatchedValue(DispatchSchemaMember(
                ForwardDetached(owner, ctx),
                slots[found].schemaKey,
                ctx));
            if (entry >= 0)
            {
                if (read is not object?[] ids || entry >= ids.Length)
                    return false;
                RowReference? listRef = FindRowReference(ids, ctx);
                read = ResolveValueIfId(
                    ids[entry],
                    ctx,
                    listRef?.ownership,
                    CollectionEntryMember(listRef, ids, ctx));
            }
            if (read is null || !ctx.rowReverseIndex.TryGetValue(read, out rowRef))
                return false;
            SetRowReference(ctx, leaf, rowRef);
            return true;
        }

        /// <summary>
        /// A variable holding a detached slot's array reads the slot's
        /// current value, the way an alias of a row-backed array does.
        /// </summary>
        private static object? ReadDetachedArrayAlias(
            NeoGeneratedTypesSupport.DetachedArrayOrigin origin,
            Context ctx)
        {
            NeoScriptObject owner = origin.owner;
            if (owner.attachedId is null)
            {
                return NeoGeneratedTypesSupport.ExposeDetachedValue(
                    owner,
                    origin.index,
                    NeoGeneratedTypesSupport.DetachedArray(owner, origin.index));
            }
            return DispatchedValue(DispatchSchemaMember(
                ForwardDetached(owner, ctx),
                owner.plan.slots[origin.index].schemaKey,
                ctx));
        }

        /// <summary>Assigns a detached object's stored member when the value can stay detached.</summary>
        internal static bool TryWriteDetachedMember(
            NeoScriptObject value,
            string key,
            object? assigned,
            Context ctx,
            KeyOf site) =>
            TryFindDetachedSlot(value.plan, key, site, ctx, out int index)
            && NeoGeneratedTypesSupport.TryStoreDetachedSlot(value, index, assigned, ctx);
    }
}
