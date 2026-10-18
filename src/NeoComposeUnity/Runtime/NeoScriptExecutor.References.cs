// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Runtime
{
    /// <summary>P112: writes through references, and the rules a stored reference keeps.</summary>
    internal static partial class NeoScriptExecutor
    {
        // A write through a reference reuses its slot's write path, so it
        // needs a target for the slot. One per assignment site and slot keeps
        // the IR caches those targets fill bounded.
        private static readonly ConditionalWeakTable<WriteTarget, Dictionary<(string memberId, string key), WriteTarget>> slotWriteTargets = new();

        /// <summary>P112 §3.2. Writes <paramref name="assigned"/> into the slot <paramref name="reference"/> names.</summary>
        private static void WriteThrough(
            NeoClient client,
            WriteTarget target,
            object? reference,
            object? assigned,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            if (reference is null)
                throw new NSGetterRuntimeError("Cannot write through a null reference. Check it for null first.");
            if (reference is not NSSlotReference slot)
                throw new NSGetterRuntimeError($"A write through a reference reached a {reference.GetType().Name}, not a reference.");
            NSSlotPlace? place = WritePlace(slot, ctx, out string? containerId);
            WriteThrough(client, target, slot, place, containerId, assigned, scope, ctx);
        }

        /// <summary>
        /// The slot a write through <paramref name="slot"/> lands in; null for
        /// an entry of the List or Set <paramref name="containerId"/> names.
        /// </summary>
        private static NSSlotPlace? WritePlace(NSSlotReference slot, NSGetterEvaluator.Context ctx, out string? containerId)
        {
            containerId = null;
            if (slot.Place is { } place)
            {
                NSGetterEvaluator.AssertPlaceAttached(slot, place, ctx);
                return place;
            }
            NSGetterEvaluator.AssertReferencedRow(slot, ctx);
            return NSGetterEvaluator.SlotPlaceOfRow(slot, ctx, out containerId);
        }

        private static void WriteThrough(
            NeoClient client,
            WriteTarget target,
            NSSlotReference slot,
            NSSlotPlace? place,
            string? containerId,
            object? assigned,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            if (place is null)
            {
                WriteEntry(client, target, slot, containerId, assigned, ctx);
                return;
            }
            JsonMember slotMember = SlotMember(client, slot, place, ctx);
            if (place.IsStatic)
            {
                if (slotMember.Mutability == NeoMemberMutabilityKind.ReadOnly)
                    throw ReadonlyTargetError(slotMember, slot, ctx);
                if (SlotStore(client, place, slotMember, ctx) == NeoValueOwnership.Asset)
                    throw AssetTargetError();
                AssignTarget(client, SlotWriteTarget(client, target, target.typeInfo, slotMember, null), assigned, scope, ctx);
                return;
            }
            object owner = place.Owner!;
            if (slotMember.Mutability == NeoMemberMutabilityKind.ReadOnly && !ctx.IsConstructing(owner))
                throw ReadonlyTargetError(slotMember, slot, ctx);
            if (SlotStore(client, place, slotMember, ctx) == NeoValueOwnership.Asset)
                throw AssetTargetError();
            WriteTarget memberTarget = SlotWriteTarget(client, target, target.typeInfo, slotMember, place.Key);
            AssignKeyOf(
                client,
                memberTarget,
                (KeyOfPointer)memberTarget.pointer,
                owner,
                assigned,
                scope,
                ctx,
                NSGetterEvaluator.SlotHoldsRow(owner, place.Key!, slot, ctx) ? null : slot.TargetId);
        }

        private static JsonMember SlotMember(NeoClient client, NSSlotReference slot, NSSlotPlace place, NSGetterEvaluator.Context ctx)
        {
            if (client.TryGetMember(place.MemberId, out JsonMember? slotMember))
                return slotMember;
            throw new NSGetterRuntimeError(
                $"{NSGetterEvaluator.ReferenceSubject(slot.MemberId, ctx)} names a slot of missing member '{place.MemberId}'.");
        }

        /// <summary>The write target for a member or static slot, shaped as the IR's own.</summary>
        /// <param name="site">The assignment the target serves, which keeps it.</param>
        private static WriteTarget SlotWriteTarget(NeoClient client, WriteTarget site, TypeInfo typeInfo, JsonMember slotMember, string? key)
        {
            Dictionary<(string, string), WriteTarget> targets = slotWriteTargets.GetOrCreateValue(site);
            if (targets.TryGetValue((slotMember.id, key ?? ""), out WriteTarget? cached))
                return cached;
            WriteTarget slotTarget = key is null
                ? new WriteTarget
                {
                    pointer = new StaticMemberPointer { type = PointerKind.StaticMember, memberId = slotMember.id },
                    typeInfo = typeInfo,
                    writability = client.ResolveStaticOwnership(slotMember) switch
                    {
                        NeoValueOwnership.Save => WritabilityKind.Save,
                        NeoValueOwnership.Session => WritabilityKind.Session,
                        _ => WritabilityKind.Runtime,
                    },
                }
                : new WriteTarget
                {
                    // The owner is handed to the write, so this receiver is never read.
                    pointer = new KeyOfPointer
                    {
                        type = PointerKind.KeyOf,
                        memberId = slotMember.id,
                        keyOf = new KeyOf
                        {
                            pointer = new VariablePointer { type = PointerKind.Variable, variableId = "__owner__" },
                            key = StringKeyPointer(key),
                        },
                    },
                    typeInfo = typeInfo,
                    writability = WritabilityKind.Runtime,
                };
            targets.Add((slotMember.id, key ?? ""), slotTarget);
            return slotTarget;
        }

        private static ValuePointer StringKeyPointer(string key) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                value = new JValue(key),
            },
        };

        /// <summary>
        /// §1.3. A reference to a List entry writes the entry at its index. A
        /// Set entry is its own key, so it can't change in place.
        /// </summary>
        private static void WriteEntry(
            NeoClient client,
            WriteTarget target,
            NSSlotReference slot,
            string? containerId,
            object? assigned,
            NSGetterEvaluator.Context ctx)
        {
            string subject = NSGetterEvaluator.ReferenceSubject(slot.MemberId, ctx);
            if (containerId is null)
                throw new NSGetterRuntimeError($"{subject} target '{slot.TargetId}' is not held by a member slot.");
            if (client.TryInferMemberForValueId(containerId, out JsonMember? container) && container is SetMember)
            {
                throw new NSGetterRuntimeError(
                    $"{subject} names an entry of a Set, which can't be written in place. Remove it and add the new value.");
            }
            if (!client.TryGetValueOwnership(containerId, out NeoValueOwnership ownership))
                throw NSGetterEvaluator.DanglingReferenceError(slot.MemberId, slot.TargetId, ctx);
            object? index = NSGetterEvaluator.StoredEntryKey(NSGetterEvaluator.ResolveValueIfId(containerId, ctx), slot.TargetId, ctx);
            if (index is not int at)
                throw NSGetterEvaluator.DanglingReferenceError(slot.MemberId, slot.TargetId, ctx);
            new NeoListIndexWriteTarget(containerId, at, target.typeInfo, ownership).Write(client, assigned, ctx);
        }

        /// <summary>§3.6. The target row decides the write, and an asset row is immutable.</summary>
        private static NSGetterRuntimeError AssetTargetError() =>
            new("Cannot write runtime-owned target because its value is Asset-owned.");

        /// <summary>The store a slot's row lives in; null for a value no row holds.</summary>
        private static NeoValueOwnership? SlotStore(NeoClient client, NSSlotPlace place, JsonMember slotMember, NSGetterEvaluator.Context ctx) =>
            place.IsStatic
                ? client.ResolveStaticOwnership(slotMember)
                : NSGetterEvaluator.FindRowOwnershipByReference(place.Owner, ctx);

        /// <summary>§3.6. A readonly slot accepts writes only while its owner initializes.</summary>
        private static NSGetterRuntimeError ReadonlyTargetError(JsonMember slotMember, NSSlotReference reference, NSGetterEvaluator.Context ctx)
        {
            string writer = reference.MemberId is null ? "a reference" : NSGetterEvaluator.ReferenceSubject(reference.MemberId, ctx);
            return new NSGetterRuntimeError(
                $"`{NSGetterEvaluator.QualifiedMemberLabel(slotMember.id, ctx)}` is readonly, so {writer} can't write it.");
        }

        /// <summary>
        /// Readies an assignment to a class member that stores references:
        /// a Lookup's selection, or a closure's captures. Returns true when a
        /// generic Lookup bound to a slot kind wrote the value through its
        /// reference instead (§3.5).
        /// </summary>
        private static bool PrepareReferenceAssignment(
            NeoClient client,
            WriteTarget writeTarget,
            KeyOfPointer keyOfPointer,
            object? receiver,
            ref object? assigned,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            if (receiver is null)
                return false;
            JsonMember? member = keyOfPointer.memberId is { } memberId && client.TryGetMember(memberId, out JsonMember? declared)
                ? declared
                : null;
            if (member is null && (assigned is NeoDelegateValue || writeTarget.typeInfo.type is MemberKind.Lookup or MemberKind.Reference or MemberKind.Generic))
            {
                member = Eval(keyOfPointer.keyOf.key, scope, ctx) is string key
                    ? NSGetterEvaluator.RuntimeSchemaMember(receiver, key, keyOfPointer.keyOf, ctx)
                    : null;
            }
            if (member is LookupMember lookup)
            {
                TypeInfo target = NSGetterEvaluator.LookupTarget(lookup, receiver, ctx);
                bool slot = NeoReferenceTypes.Category(target, client) == NeoReferenceCategory.Slot;
                if (slot
                    && assigned is not NSSlotReference
                    && lookup.declaredTypeInfo is { } declaredTarget
                    && NeoNSFunctionRuntime.ContainsGeneric(declaredTarget))
                {
                    string key = ToStringKey(Eval(keyOfPointer.keyOf.key, scope, ctx), "Reference member name");
                    if (NSGetterEvaluator.ReadRecordKey(receiver, key, ctx) is not NSSlotReference through)
                    {
                        throw new NSGetterRuntimeError(
                            $"Cannot write through Reference `{NSGetterEvaluator.QualifiedMemberLabel(lookup.id, ctx)}`, because it names no target.");
                    }
                    WriteThrough(client, writeTarget, through, assigned, scope, ctx);
                    return true;
                }
                assigned = StoredSelection(lookup, target, slot, assigned, HolderOwnership(client, writeTarget, receiver, scope, ctx), scope, ctx);
            }
            else if (member is not null && assigned is NeoDelegateValue { captures: { Length: > 0 } } closure)
            {
                AssertClosureCaptures(member, closure, HolderOwnership(client, writeTarget, receiver, scope, ctx), ctx);
            }
            return false;
        }

        /// <summary><see cref="PrepareReferenceAssignment"/> for a static member.</summary>
        private static void PrepareStaticReferenceAssignment(
            NeoClient client,
            StaticMemberPointer pointer,
            ref object? assigned,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            if (!client.TryGetMember(pointer.memberId, out JsonMember? member))
                return;
            if (member is LookupMember lookup)
            {
                TypeInfo target = NSGetterEvaluator.LookupTargetTypeInfo(lookup, ctx);
                bool slot = NeoReferenceTypes.Category(target, client) == NeoReferenceCategory.Slot;
                assigned = StoredSelection(lookup, target, slot, assigned, client.ResolveStaticOwnership(member), scope, ctx);
            }
            else if (member is ListMember or DictionaryMember
                && assigned is not null
                && client.TryResolveStaticBinding(member.id, out _, out _, out string? boundId)
                && TryGetValueReferenceId(assigned, ctx, out string? sourceId)
                && sourceId != boundId)
            {
                // §4.1. A bound collection static keeps its row; another row's contents are copied in.
                assigned = NeoGeneratedTypesSupport.MaterializeCollectionAssignment(
                    client,
                    member,
                    NSGetterEvaluator.CopiedCollectionContents(assigned, ctx),
                    client.ResolveStaticOwnership(member),
                    ctx);
            }
            else if (assigned is NeoDelegateValue { captures: { Length: > 0 } } closure)
            {
                AssertClosureCaptures(member, closure, client.ResolveStaticOwnership(member), ctx);
            }
        }

        /// <summary>The store an assigned member's row lives in; null for a value no row holds.</summary>
        private static NeoValueOwnership? HolderOwnership(
            NeoClient client,
            WriteTarget writeTarget,
            object receiver,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx) =>
            NSGetterEvaluator.FindRowIdByReference(receiver, ctx) is null
                ? null
                : TargetOwnership(client, writeTarget, scope, ctx, receiver);

        /// <summary>
        /// §4.1 to §4.3 on rebind. The ids a Lookup assignment stores: each
        /// names a row that exists, fits, and outlives the Lookup's store. A
        /// slot reference names its slot's row, created at its derived id on
        /// first use. A single handle stores its row's id; a handle without
        /// one, and a multi-select's entries, keep their value for the write.
        /// </summary>
        private static object? StoredSelection(
            LookupMember lookup,
            TypeInfo target,
            bool slot,
            object? assigned,
            NeoValueOwnership? holder,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            if (assigned is null)
                return null;
            string subject = $"Reference `{NSGetterEvaluator.QualifiedMemberLabel(lookup.id, ctx)}`";
            // A single selection is one handle, even when the handle is a collection.
            if (!slot && lookup.Selection != NeoMemberSelectionKind.Multi)
            {
                if (!TryGetValueReferenceId(assigned, ctx, out string? handleId))
                    return assigned;
                AssertReferenceLifetime(subject, holder, handleId!, ctx);
                return new[] { handleId! };
            }
            if (!slot)
            {
                foreach (object? entry in ReferenceEntries(assigned))
                {
                    if (entry is not null && TryGetValueReferenceId(entry, ctx, out string? id))
                        AssertReferenceLifetime(subject, holder, id!, ctx);
                }
                return assigned;
            }
            var ids = new List<string>();
            foreach (object? entry in ReferenceEntries(assigned))
            {
                string id = entry is NSSlotReference reference
                    ? StoredReferenceId(reference, scope, ctx)
                    : entry as string
                        ?? throw new NSGetterRuntimeError(
                            $"{subject} was assigned a {entry?.GetType().Name ?? "null"}, not a reference.");
                NSGetterEvaluator.AssertReferencedRow(new NSSlotReference(id, null, target, lookup.id), ctx);
                AssertReferenceLifetime(subject, holder, id, ctx);
                ids.Add(id);
            }
            return ids.ToArray();
        }

        /// <summary>
        /// §4.1 to §4.3 for a Set of references: an added reference names a
        /// row that exists, fits, and outlives the Set's store.
        /// </summary>
        private static string AddedReferenceId(
            NeoClient client,
            string setRowId,
            LookupTypeInfo typeInfo,
            NeoValueOwnership ownership,
            NSSlotReference reference,
            NSGetterEvaluator.Context ctx)
        {
            if (typeInfo.entryTypeInfo is not ReferenceTypeInfo { targetTypeInfo: { } target })
                throw new NSGetterRuntimeError($"Set '{setRowId}' holds no references, so it can't add one.");
            string? memberId = client.TryInferMemberForValueId(setRowId, out JsonMember? member) ? member.id : null;
            string id = StoredReferenceId(reference, new NeoScriptScope(), ctx);
            NSGetterEvaluator.AssertReferencedRow(new NSSlotReference(id, null, target, memberId), ctx);
            AssertReferenceLifetime(NSGetterEvaluator.ReferenceSubject(memberId, ctx), ownership, id, ctx);
            return id;
        }

        private static IEnumerable<object?> ReferenceEntries(object value) => value switch
        {
            RuntimeSet set => set.Snapshot(),
            object?[] entries => entries,
            _ => new[] { value },
        };

        /// <summary>
        /// The id a stored reference keeps: the row its slot holds now, which a
        /// slot without one gets at the reference's derived id (§4.1).
        /// </summary>
        private static string StoredReferenceId(NSSlotReference reference, NeoScriptScope scope, NSGetterEvaluator.Context ctx)
        {
            if (reference.Place is not { } place)
                return reference.TargetId;
            if (place.IsStatic)
            {
                if (ctx.client.TryResolveStaticBinding(place.MemberId, out _, out _, out string? boundId))
                    return boundId;
                ctx.client.StaticBinding(place.MemberId).BindEmpty();
                return reference.TargetId;
            }
            object owner = place.Owner!;
            string key = place.Key!;
            if (NSGetterEvaluator.RecordSlotId(owner, key) is { } held)
                return held;
            if (!NSGetterEvaluator.TryReadReferenceRow(reference, ctx))
                MaterializeSlotRow(ctx.client, place, reference.TargetId, scope, ctx);
            return reference.TargetId;
        }

        // The assignment site a slot's first row is created through.
        private static readonly WriteTarget MaterializeSite = new();

        /// <summary>Gives an unbound member slot its row at <paramref name="targetId"/>, holding the slot's current value.</summary>
        private static void MaterializeSlotRow(
            NeoClient client,
            NSSlotPlace place,
            string targetId,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            if (!client.TryGetMember(place.MemberId, out JsonMember? slotMember))
                throw new NSGetterRuntimeError($"A reference names a slot of missing member '{place.MemberId}'.");
            // A class owner's write ignores the type; a Dictionary entry's is the entry's.
            TypeInfo typeInfo = NeoReferenceTypes.SlotTypeInfo(client, slotMember) ?? new UnknownTypeInfo { required = true };
            WriteTarget slotTarget = SlotWriteTarget(client, MaterializeSite, typeInfo, slotMember, place.Key);
            object? current = NSGetterEvaluator.ReadSlot(place, ctx);
            AssignKeyOf(client, slotTarget, (KeyOfPointer)slotTarget.pointer, place.Owner, current, scope, ctx, targetId);
        }

        private static readonly Dictionary<NeoValueOwnership, int> StoreLifetime = new()
        {
            [NeoValueOwnership.Asset] = 4,
            [NeoValueOwnership.User] = 3,
            [NeoValueOwnership.Save] = 2,
            [NeoValueOwnership.Session] = 1,
        };

        /// <summary>§4.2. Authored rows outlive every store; a runtime row lives as long as its store.</summary>
        private static void AssertReferenceLifetime(string subject, NeoValueOwnership? holder, string targetId, NSGetterEvaluator.Context ctx)
        {
            if (holder is not NeoValueOwnership holderStore)
                return;
            if (ctx.client.IsAuthoredValue(targetId))
                return;
            if (!ctx.client.TryGetValueOwnership(targetId, out NeoValueOwnership targetStore))
                return;
            if (StoreLifetime[targetStore] >= StoreLifetime[holderStore])
                return;
            throw new NSGetterRuntimeError(
                $"{subject} is {StoreName(holderStore)}-stored and can't name '{targetId}', which lives only in the {StoreName(targetStore)}.");
        }

        private static string StoreName(NeoValueOwnership store) =>
            store == NeoValueOwnership.Asset ? "Immutable" : store.ToString();

        /// <summary>§4.2. A stored closure can only capture references that outlive it.</summary>
        private static void AssertClosureCaptures(JsonMember member, NeoDelegateValue closure, NeoValueOwnership? holder, NSGetterEvaluator.Context ctx)
        {
            if (holder is null)
                return;
            string subject = $"Closure `{NSGetterEvaluator.QualifiedMemberLabel(member.id, ctx)}`";
            VisitCaptures(closure.captures!, subject, holder, ctx);
        }

        // §8. C# reads, writes and rebinds references through these, so it
        // keeps NeoScript's rules.

        private static NSGetterEvaluator.Context HostContext(NeoClient client)
        {
            NSGetterEvaluator.Context ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            ctx.BindRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx));
            return ctx;
        }

        /// <summary>The value <paramref name="reference"/> names, as NeoScript reads it.</summary>
        internal static object? ReadReference(NeoClient client, NeoReference reference)
        {
            NSGetterEvaluator.Context ctx = HostContext(client);
            return NSGetterEvaluator.ReadThroughReference(reference.Slot(ctx), ctx);
        }

        /// <summary>§2.2. The id of <paramref name="owner"/>'s slot <paramref name="key"/>.</summary>
        internal static string ReferenceTargetId(NeoClient client, NeoGeneratedClassValue owner, string key)
        {
            NSGetterEvaluator.Context ctx = HostContext(client);
            if (NSGetterEvaluator.ReferenceToKey(owner, key, ctx) is NSSlotReference slot)
                return slot.TargetId;
            throw new InvalidOperationException($"`{key}` holds a class or collection, which is already a reference. Use its value.");
        }

        /// <summary>Whether <paramref name="lookup"/> names value slots rather than classes or collections.</summary>
        internal static bool IsSlotLookup(NeoClient client, LookupMember lookup, string? lookupRowId) =>
            NSGetterEvaluator.LookupSlotTarget(lookup, HostContext(client), lookupRowId) is not null;

        /// <summary>§4.3. Whether row <paramref name="targetId"/> exists and fits <paramref name="lookup"/>'s target.</summary>
        internal static bool ReferenceFits(NeoClient client, LookupMember lookup, string? lookupRowId, string targetId)
        {
            NSGetterEvaluator.Context ctx = HostContext(client);
            TypeInfo target = NSGetterEvaluator.LookupSlotTarget(lookup, ctx, lookupRowId) ?? NSGetterEvaluator.LookupTargetTypeInfo(lookup, ctx);
            return NSGetterEvaluator.ReferenceFits(new NSSlotReference(targetId, null, target, lookup.id), ctx);
        }

        // One assignment site per written type, which keeps its slots' targets.
        private static readonly ConditionalWeakTable<TypeInfo, WriteTarget> hostWriteSites = new();

        /// <summary>
        /// Writes <paramref name="value"/> through <paramref name="reference"/>,
        /// and throws when the target is readonly or its store takes no writes.
        /// </summary>
        internal static void WriteReference(NeoClient client, NeoReference reference, object? value)
        {
            NSGetterEvaluator.Context ctx = HostContext(client);
            NSSlotReference slot = reference.Slot(ctx);
            NSSlotPlace? place = WritePlace(slot, ctx, out string? containerId);
            WriteReference(client, slot, place, containerId, value, ctx);
        }

        /// <summary>
        /// <see cref="WriteReference(NeoClient, NeoReference, object?)"/>, or
        /// false when the target is readonly or its store takes no writes.
        /// </summary>
        internal static bool TrySetReference(NeoClient client, NeoReference reference, object? value)
        {
            NSGetterEvaluator.Context ctx = HostContext(client);
            NSSlotReference slot = reference.Slot(ctx);
            NSSlotPlace? place = WritePlace(slot, ctx, out string? containerId);
            if (!HostWritable(client, slot, place, containerId, ctx))
                return false;
            WriteReference(client, slot, place, containerId, value, ctx);
            return true;
        }

        private static void WriteReference(
            NeoClient client,
            NSSlotReference slot,
            NSSlotPlace? place,
            string? containerId,
            object? value,
            NSGetterEvaluator.Context ctx)
        {
            TypeInfo type = slot.Target ?? NSGetterEvaluator.TargetSlotType(slot, place, ctx);
            object? assigned = NeoScriptValueMarshaller.Normalize(client, NeoValueOwnership.Session, value, type, ctx, "Reference value");
            WriteTarget site = hostWriteSites.GetValue(type, written => new WriteTarget { typeInfo = written });
            client.EnterScriptWrites();
            try
            {
                WriteThrough(client, site, slot, place, containerId, assigned, new NeoScriptScope(), ctx);
            }
            finally
            {
                client.ExitScriptWrites();
            }
        }

        /// <summary>
        /// Whether C# may write the slot: it isn't readonly, and its store
        /// isn't the immutable asset store. Anything else the write refuses throws.
        /// </summary>
        private static bool HostWritable(
            NeoClient client,
            NSSlotReference slot,
            NSSlotPlace? place,
            string? containerId,
            NSGetterEvaluator.Context ctx)
        {
            if (place is null)
            {
                return containerId is null
                    || !client.TryGetValueOwnership(containerId, out NeoValueOwnership entryStore)
                    || entryStore != NeoValueOwnership.Asset;
            }
            JsonMember slotMember = SlotMember(client, slot, place, ctx);
            if (slotMember.Mutability == NeoMemberMutabilityKind.ReadOnly)
                return false;
            return SlotStore(client, place, slotMember, ctx) != NeoValueOwnership.Asset;
        }

        /// <summary>
        /// §4.1 to §4.3 for a C# rebind of <paramref name="lookup"/>, held in
        /// <paramref name="holder"/>: the ids it stores for
        /// <paramref name="assigned"/>, which is a reference, a target id, a
        /// sequence of them, or null.
        /// </summary>
        /// <param name="lookupRowId">The Lookup's row, which binds a generic target (§3.5).</param>
        internal static string[]? ReferenceSelection(
            NeoClient client,
            LookupMember lookup,
            string? lookupRowId,
            NeoValueOwnership holder,
            object? assigned)
        {
            NSGetterEvaluator.Context ctx = HostContext(client);
            TypeInfo? target = NSGetterEvaluator.LookupSlotTarget(lookup, ctx, lookupRowId);
            if (target is null)
                return HandleSelection(lookup, holder, assigned, ctx);
            client.EnterScriptWrites();
            try
            {
                return (string[]?)StoredSelection(lookup, target, slot: true, HostSelected(assigned, ctx), holder, new NeoScriptScope(), ctx);
            }
            finally
            {
                client.ExitScriptWrites();
            }
        }

        /// <summary>
        /// A rebind of a reference to a class or collection: the row the
        /// assigned reference or collection view names, which must exist,
        /// fit, and outlive <paramref name="holder"/>.
        /// </summary>
        private static string[]? HandleSelection(
            LookupMember lookup,
            NeoValueOwnership holder,
            object? assigned,
            NSGetterEvaluator.Context ctx)
        {
            if (assigned is null)
                return null;
            string subject = $"Reference `{NSGetterEvaluator.QualifiedMemberLabel(lookup.id, ctx)}`";
            if (lookup.Selection != NeoMemberSelectionKind.Multi)
                return new[] { HandleId(lookup, subject, holder, assigned, ctx) };
            var ids = new List<string>();
            foreach (object? entry in (System.Collections.IEnumerable)assigned)
                ids.Add(HandleId(lookup, subject, holder, entry, ctx));
            return ids.ToArray();
        }

        private static string HandleId(
            LookupMember lookup,
            string subject,
            NeoValueOwnership holder,
            object? assigned,
            NSGetterEvaluator.Context ctx)
        {
            string id = assigned switch
            {
                NeoReference reference => reference.TargetId,
                INeoValueReference { valueId: { } valueId } => valueId,
                INeoCollectionView { CollectionNode: { value: { } row } } => row.id,
                INeoCollectionView => throw new InvalidOperationException($"{subject} was assigned a collection that has no row yet."),
                null => throw new InvalidOperationException($"{subject} was assigned a null entry."),
                _ => throw new InvalidOperationException($"{subject} was assigned a {assigned.GetType().Name}, not a reference, class, or collection."),
            };
            NSGetterEvaluator.AssertReferencedRow(new NSSlotReference(id, null, NSGetterEvaluator.LookupTargetTypeInfo(lookup, ctx), lookup.id), ctx);
            AssertReferenceLifetime(subject, holder, id, ctx);
            return id;
        }

        private static object? HostSelected(object? assigned, NSGetterEvaluator.Context ctx)
        {
            switch (assigned)
            {
                case null:
                case string:
                    return assigned;
                case NeoReference reference:
                    return reference.Slot(ctx);
                case System.Collections.IEnumerable entries:
                    var selected = new List<object?>();
                    foreach (object? entry in entries)
                        selected.Add(HostSelected(entry, ctx));
                    return selected.ToArray();
                default:
                    throw new InvalidOperationException($"A reference was assigned a {assigned.GetType().Name}, not a reference.");
            }
        }

        private static void VisitCaptures(IEnumerable<object?> captures, string subject, NeoValueOwnership? holder, NSGetterEvaluator.Context ctx)
        {
            foreach (object? captured in captures)
            {
                switch (captured)
                {
                    case NSSlotReference reference:
                        AssertReferenceLifetime(subject, holder, reference.TargetId, ctx);
                        break;
                    case RuntimeTuple tuple:
                        VisitCaptures(tuple, subject, holder, ctx);
                        break;
                    case RuntimeSet set:
                        VisitCaptures(set.Snapshot(), subject, holder, ctx);
                        break;
                    case object?[] entries:
                        VisitCaptures(entries, subject, holder, ctx);
                        break;
                    // A local Dictionary; a row record's fields hold ids, not references.
                    case Dictionary<string, object?> entries when entries is not INeoValueReference:
                        VisitCaptures(entries.Values, subject, holder, ctx);
                        break;
                    case NeoDelegateValue { captures: { } nested }:
                        VisitCaptures(nested, subject, holder, ctx);
                        break;
                }
            }
        }
    }
}
