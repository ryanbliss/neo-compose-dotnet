// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>P112: references to any value.</summary>
    public static partial class NSGetterEvaluator
    {
        /// <summary>P112 §2.2. A reference to the row <c>place</c> names, or to its derived id.</summary>
        private static object? EvalRowOf(RowOfPointer pointer, NeoScriptScope scope, Context ctx)
        {
            switch (pointer.place)
            {
                case KeyOfPointer keyOfPointer:
                    {
                        object? owner = EvalPointer(keyOfPointer.keyOf.pointer, scope, ctx);
                        object? keyValue = EvalPointer(keyOfPointer.keyOf.key, scope, ctx);
                        string key = keyValue as string ?? keyValue?.ToString() ?? "null";
                        if (owner is NeoScriptObject detached)
                            owner = ForwardDetached(detached, ctx);
                        else
                            owner = UnwrapGeneratedValue(owner, ctx);
                        if (owner is null)
                        {
                            if (keyOfPointer.optional == true)
                                return null;
                            throw new NSGetterRuntimeError($"Cannot reference '{key}' of null.");
                        }
                        return ReferenceToKey(owner, key, keyOfPointer.memberId, keyOfPointer.keyOf, ctx);
                    }
                case StaticMemberPointer staticPointer:
                    {
                        if (!ctx.client.TryGetMember(staticPointer.memberId, out JsonMember? member))
                            throw new NSGetterRuntimeError($"Static member '{staticPointer.memberId}' is missing, so it has no row to reference.");
                        string targetId = ctx.client.TryResolveStaticBinding(member.id, out _, out _, out string? boundId)
                            ? boundId
                            : StaticSlotId(member.id);
                        return new NSSlotReference(targetId, NSSlotPlace.Static(member.id), null, null);
                    }
                case ReferencePointer reference:
                    return new NSSlotReference(ProvenanceRowId(reference, ctx), null, pointer.typeInfo, null);
                default:
                    throw new NSGetterRuntimeError(
                        $"A row reference names a '{pointer.place.type}' pointer, not a member, static, or value id.");
            }
        }

        /// <summary>§2.2. A reference to <paramref name="owner"/>'s slot <paramref name="key"/>; a `T` slot bound to a handle kind is the handle.</summary>
        internal static object? ReferenceToKey(object owner, string key, string? declaredMemberId, ISchemaResolutionSite? site, Context ctx)
        {
            if (owner is object?[])
                throw new NSGetterRuntimeError($"Cannot reference '{key}' of a list, which has no member slots.");
            if (AsObjectRecord(owner) is null)
                throw new NSGetterRuntimeError($"Cannot reference '{key}' of a {ReceiverTypeName(owner)}, which has no member slots.");
            JsonMember? member = RuntimeSchemaMember(owner, key, site, ctx);
            string? memberId = member?.id ?? declaredMemberId;
            if (memberId is null)
                throw new NSGetterRuntimeError($"Cannot reference '{key}', which is not a declared member.");
            // §3.5.
            if (member is GenericMember
                && NeoReferenceTypes.Category(BoundSlotTypeInfo(owner, member, ctx), ctx.client) == NeoReferenceCategory.Handle)
            {
                return ReadRecordKey(owner, key, ctx);
            }
            return new NSSlotReference(
                SlotTargetId(owner, key, memberId, ctx),
                NSSlotPlace.Member(owner, key, memberId),
                null,
                null);
        }

        /// <summary>§8. A reference to a generated value's slot, as C# forms it.</summary>
        internal static object? ReferenceToKey(NeoGeneratedClassValue owner, string key, Context ctx) =>
            ReferenceToKey(UnwrapGeneratedValue(owner, ctx)!, key, null, null, ctx);

        /// <summary>The row id a static's first reference write creates (§2.2).</summary>
        internal static string StaticSlotId(string memberId) => NeoClient.VirtualValueId(memberId, "static");

        /// <summary>A reference default's row as this instance sees it.</summary>
        private static string ProvenanceRowId(ReferencePointer reference, Context ctx)
        {
            if (reference.withProvenance != true || FindRowIdByReference(ctx.thisValue, ctx) is not string receiverId)
                return reference.valueId;
            NeoValueOwnership receiverOwnership = FindRowOwnershipByReference(ctx.thisValue, ctx) ?? ctx.valueOwnership;
            try
            {
                return ctx.client.TryResolveProvenanceReference(
                        receiverOwnership,
                        receiverId,
                        reference.valueId,
                        out MemberValue? row,
                        out _)
                    ? row.id
                    : reference.valueId;
            }
            catch (System.InvalidOperationException error)
            {
                throw new NSGetterRuntimeError(error.Message);
            }
        }

        /// <summary>The member a class record's key names, as its runtime class resolves it.</summary>
        internal static JsonMember? RuntimeSchemaMember(object owner, string key, ISchemaResolutionSite? site, Context ctx)
        {
            RowReference? ownerRef = FindRowReference(owner, ctx);
            string? classId = ownerRef is not null
                ? ClassIdOfRowReference(ownerRef, ctx)
                : FindReferencedClassId(owner, ctx);
            return string.IsNullOrEmpty(classId)
                ? null
                : SurfaceRowMember(ownerRef, classId!, key, site, ctx)?.member;
        }

        /// <summary>A generic slot's stored type under its owner's binding (§3.5).</summary>
        private static TypeInfo BoundSlotTypeInfo(object owner, JsonMember member, Context ctx)
        {
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env = NeoNSFunctionRuntime.ResolveReceiverGenericEnv(
                ctx.client,
                owner,
                ctx,
                $"Member '{member.name}'");
            return NeoReferenceTypes.SlotTypeInfo(ctx.client, member, env)
                ?? throw new NSGetterRuntimeError($"Member '{member.name}' holds no value, so nothing can reference it.");
        }

        /// <summary>
        /// The id of the row a member slot holds (§2.2): its row, else its P75
        /// virtual id, else the id it is derived to, which its first write creates.
        /// </summary>
        private static string SlotTargetId(object owner, string key, string memberId, Context ctx)
        {
            string ownerId = FindRowIdByReference(owner, ctx)
                ?? throw new NSGetterRuntimeError($"Cannot reference '{key}', because its owner has no stored row.");
            if (AsObjectRecord(owner) is { } record && record.TryGetValue(key, out object? at) && at is string boundId)
                return boundId;
            if (ctx.client.TryGetVirtualClassChildValueId(ownerId, key, out string? virtualId) && !string.IsNullOrEmpty(virtualId))
                return virtualId!;
            return NeoClient.VirtualValueId(ownerId, memberId);
        }

        /// <summary>§3.1. The value a reference reads; anything else is already its value.</summary>
        internal static object? ReadThroughReference(object? value, Context ctx)
        {
            if (value is not NSSlotReference reference)
                return value;
            if (reference.Place is { } place)
            {
                AssertPlaceAttached(reference, place, ctx);
                if (TryReadReferenceRow(reference, ctx))
                    AssertReferenceFits(reference, ctx);
                return ReadSlot(place, ctx);
            }
            AssertReferencedRow(reference, ctx);
            NSSlotPlace? rowPlace = SlotPlaceOfRow(reference, ctx, out _);
            // A collection entry has no member slot, so it reads as its own row.
            return rowPlace is null
                ? ResolveValueIfId(reference.TargetId, ctx)
                : ReadSlot(rowPlace, ctx);
        }

        /// <summary>A slot's value, as a member read sees it.</summary>
        internal static object? ReadSlot(NSSlotPlace place, Context ctx) =>
            place.IsStatic
                ? ReadStaticMember(place.MemberId, ctx)
                : ReadRecordKey(place.Owner!, place.Key!, ctx);

        /// <summary>A record's key as a member read sees it.</summary>
        internal static object? ReadRecordKey(object owner, string key, Context ctx)
        {
            object? dispatched = DispatchSchemaMember(owner, key, ctx);
            if (Dispatched(dispatched))
                return dispatched;
            if (ReferenceEquals(dispatched, DispatchMatchedNoValue))
                return null;
            if (AsObjectRecord(owner) is { } record && record.TryGetValue(key, out object? at))
            {
                if (at is not string)
                    return at;
                RowReference? recordRef = FindRowReference(owner, ctx);
                if (recordRef is null && HoldsValues(owner))
                    return at;
                return ResolveValueIfId(at, ctx, RowOwnership(recordRef, owner), CollectionEntryMember(recordRef, owner, ctx));
            }
            throw new NSGetterRuntimeError($"Missing key '{key}' on object");
        }

        /// <summary>A stored reference's target row, which must exist and fit (§4.3, §4.4).</summary>
        internal static void AssertReferencedRow(NSSlotReference reference, Context ctx)
        {
            if (!TryReadReferenceRow(reference, ctx))
                throw DanglingReferenceError(reference.MemberId, reference.TargetId, ctx);
            if (IsRetired(reference.TargetId, ctx))
                throw DanglingReferenceError(reference.MemberId, reference.TargetId, ctx);
            AssertReferenceFits(reference, ctx);
        }

        /// <summary>§4.1. A member slot dangles with its owner, once no slot holds the owner.</summary>
        internal static void AssertPlaceAttached(NSSlotReference reference, NSSlotPlace place, Context ctx)
        {
            if (place.IsStatic || FindRowIdByReference(place.Owner, ctx) is not string ownerId)
                return;
            if (IsRetired(ownerId, ctx))
                throw DanglingReferenceError(reference.MemberId, reference.TargetId, ctx);
        }

        /// <summary>
        /// §4.1. Whether no slot holds row <paramref name="rowId"/> any more.
        /// Its holders lead to a root; a rebound static's authored default
        /// lingers there, held by nothing. A row with no holder is a value a
        /// script made and holds until it places it.
        /// </summary>
        private static bool IsRetired(string rowId, Context ctx)
        {
            if (!ctx.client.AnyReleasedStaticDefault())
                return false;
            string id = rowId;
            while (TryFindHolder(id, ctx, out string? parentId))
            {
                if (parentId.StartsWith("member:", System.StringComparison.Ordinal))
                    return ctx.client.IsReleasedStaticDefault(id);
                if (parentId.StartsWith("static:", System.StringComparison.Ordinal))
                    return false;
                id = parentId;
            }
            return false;
        }

        internal static bool TryReadReferenceRow(NSSlotReference reference, Context ctx)
        {
            NeoValueNode? node = null;
            NeoValueOwnership ownership = ResolveOwnershipForValueId(ctx, reference.TargetId, ref node);
            MemberValue? row = ctx.client.ReadReplayReference(reference.TargetId, ref node, ownership);
            return row is not null && !row.IsRemoved;
        }


        /// <summary>
        /// The slot that holds a row: a static binding or an owner's member.
        /// <paramref name="containerId"/> is the List or Set a slotless entry
        /// belongs to. A row nothing holds dangles (§4.4).
        /// </summary>
        internal static NSSlotPlace? SlotPlaceOfRow(NSSlotReference reference, Context ctx, out string? containerId)
        {
            containerId = null;
            string rowId = reference.TargetId;
            if (!ctx.client.TryInferMemberForValueId(rowId, out JsonMember? member))
                return null;
            if (member.Modifier == NeoMemberModifierKind.Static
                && ctx.client.TryResolveStaticBinding(member.id, out _, out _, out string? boundId)
                && boundId == rowId)
            {
                return NSSlotPlace.Static(member.id);
            }
            // A removed entry's row lingers, but no collection holds it.
            if (!TryFindHolder(rowId, ctx, out string? parentId))
                throw DanglingReferenceError(reference.MemberId, rowId, ctx);
            if (parentId.StartsWith("member:", System.StringComparison.Ordinal)
                || parentId.StartsWith("static:", System.StringComparison.Ordinal))
            {
                return null;
            }
            object? parent = ResolveValueIfId(parentId, ctx);
            if (parent is object?[] or RuntimeSet)
            {
                containerId = parentId;
                return null;
            }
            if (AsObjectRecord(parent) is not { } record)
                return null;
            string? key = SlotKeyHolding(record, parentId, rowId, member.name, ctx);
            return key is null ? null : NSSlotPlace.Member(parent!, key, member.id);
        }

        /// <summary>
        /// The row that holds <paramref name="rowId"/>. A sparse shadow's
        /// holder can still be its authored one (P75), while that holder's
        /// live entries still list it.
        /// </summary>
        private static bool TryFindHolder(string rowId, Context ctx, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? parentId)
        {
            NeoValueOwnership ownership = ResolveOwnershipForValueId(ctx, rowId);
            if (ctx.client.TryFindOwnedParent(ownership, rowId, out parentId))
                return true;
            if (!ctx.client.TryGetAuthoredRowOwnership(rowId, out NeoValueOwnership authored))
                return false;
            if (authored == ownership)
                return false;
            if (!ctx.client.TryFindOwnedParent(authored, rowId, out parentId))
                return false;
            return ResolveValueIfId(parentId, ctx) is not object?[] entries
                || System.Array.IndexOf(entries, rowId) >= 0;
        }

        /// <summary>The owner's key whose slot holds <paramref name="rowId"/>.</summary>
        private static string? SlotKeyHolding(
            IDictionary<string, object?> record,
            string ownerId,
            string rowId,
            string memberName,
            Context ctx)
        {
            // A class owner holds its member's row under the member's name;
            // only a Dictionary's key has to be found.
            if (record.TryGetValue(memberName, out object? at) && at is string id && id == rowId)
                return memberName;
            if (ctx.client.TryGetVirtualClassChildValueId(ownerId, memberName, out string? virtualId) && virtualId == rowId)
                return memberName;
            foreach (KeyValuePair<string, object?> entry in record)
            {
                if (entry.Value is string entryId && entryId == rowId)
                    return entry.Key;
            }
            return null;
        }

        /// <summary>§4.3. A reference whose target row is the wrong type is refused on use.</summary>
        internal static void AssertReferenceFits(NSSlotReference reference, Context ctx)
        {
            TypeInfo? target = reference.Target;
            if (target is null)
                return;
            // §4.4. No slot holds a removed entry's row, though the row lingers.
            if (!ctx.client.TryInferMemberForValueId(reference.TargetId, out JsonMember? member))
                throw DanglingReferenceError(reference.MemberId, reference.TargetId, ctx);
            TypeInfo stored = StoredSlotType(reference, member, reference.Place, ctx);
            if (NeoReferenceTypes.SlotFits(stored, target))
                return;
            throw new NSGetterRuntimeError(NeoReferenceTypes.FitError(
                ctx.client,
                ReferenceSubject(reference.MemberId, ctx),
                reference.TargetId,
                stored,
                target));
        }

        /// <summary>§4.3 without throwing: whether the reference's row exists and fits its target.</summary>
        internal static bool ReferenceFits(NSSlotReference reference, Context ctx)
        {
            if (!TryReadReferenceRow(reference, ctx))
                return false;
            if (!ctx.client.TryInferMemberForValueId(reference.TargetId, out JsonMember? member))
                return false;
            return reference.Target is null || NeoReferenceTypes.SlotFits(StoredSlotType(reference, member, reference.Place, ctx), reference.Target);
        }

        /// <summary>The stored type of the slot <paramref name="reference"/> names, which <paramref name="place"/> holds when known.</summary>
        internal static TypeInfo TargetSlotType(NSSlotReference reference, NSSlotPlace? place, Context ctx)
        {
            JsonMember? member = null;
            if (place is not null)
                ctx.client.TryGetMember(place.MemberId, out member);
            else
                ctx.client.TryInferMemberForValueId(reference.TargetId, out member);
            if (member is null)
                throw DanglingReferenceError(reference.MemberId, reference.TargetId, ctx);
            return StoredSlotType(reference, member, place, ctx);
        }

        private static TypeInfo StoredSlotType(NSSlotReference reference, JsonMember member, NSSlotPlace? place, Context ctx)
        {
            TypeInfo? stored = NeoReferenceTypes.SlotTypeInfo(ctx.client, member);
            // §3.5. A `T` slot's stored type is its owner's binding.
            if (stored is null || NeoNSFunctionRuntime.ContainsGeneric(stored))
            {
                place ??= SlotPlaceOfRow(reference, ctx, out _);
                if (place?.Owner is { } owner)
                    stored = BoundSlotTypeInfo(owner, member, ctx);
            }
            return stored
                ?? throw new NSGetterRuntimeError(
                    $"{ReferenceSubject(reference.MemberId, ctx)} target '{reference.TargetId}' is `{QualifiedMemberLabel(member.id, ctx)}`, which holds no value.");
        }

        /// <summary>
        /// A Lookup's target when it names a slot kind (P112 §1.1), whose
        /// stored ids read as <see cref="NSSlotReference"/>s; else null.
        /// <paramref name="lookupRowId"/> binds a generic target to the
        /// Lookup owner's arguments (§3.5).
        /// </summary>
        internal static TypeInfo? LookupSlotTarget(LookupMember lookup, Context ctx, string? lookupRowId = null)
        {
            object? owner = null;
            if (lookupRowId is not null
                && lookup.declaredTypeInfo is { } declared
                && NeoNSFunctionRuntime.ContainsGeneric(declared)
                && ctx.client.TryFindOwnedParent(ResolveOwnershipForValueId(ctx, lookupRowId), lookupRowId, out string? ownerId))
            {
                owner = ResolveValueIfId(ownerId, ctx);
            }
            TypeInfo target = LookupTarget(lookup, owner, ctx);
            return NeoReferenceTypes.Category(target, ctx.client) == NeoReferenceCategory.Slot ? target : null;
        }

        /// <summary>The Lookup's target, with <paramref name="owner"/>'s generic arguments applied.</summary>
        internal static TypeInfo LookupTarget(LookupMember lookup, object? owner, Context ctx)
        {
            TypeInfo target = LookupTargetTypeInfo(lookup, ctx);
            if (owner is null || !NeoNSFunctionRuntime.ContainsGeneric(target))
                return target;
            return NeoGenericResolution.SubstituteTypeInfo(
                ctx.client,
                target,
                NeoNSFunctionRuntime.ResolveReceiverGenericEnv(ctx.client, owner, ctx, $"Member '{lookup.name}'"));
        }

        /// <summary>Whether the owner's slot holds a row, so a write needs none created (§4.1).</summary>
        internal static bool SlotHoldsRow(object owner, string key, NSSlotReference reference, Context ctx) =>
            RecordSlotId(owner, key) is not null || TryReadReferenceRow(reference, ctx);

        /// <summary>The row id an owner's record stores under <paramref name="key"/>.</summary>
        internal static string? RecordSlotId(object owner, string key) =>
            AsObjectRecord(owner) is { } record && record.TryGetValue(key, out object? at) ? at as string : null;

        /// <summary>The index or key at which <paramref name="collection"/> stores row <paramref name="rowId"/>.</summary>
        internal static object? StoredEntryKey(object? collection, string rowId, Context ctx)
        {
            var cursor = new CollectionCursor(collection, ctx);
            while (cursor.MoveNextUnresolved())
            {
                if (cursor.ValueId == rowId)
                    return cursor.Key;
            }
            return null;
        }

        internal static NSSlotReference SlotReferenceOf(object? id, LookupMember lookup, TypeInfo target)
        {
            if (id is not string targetId)
            {
                throw new NSGetterRuntimeError(
                    $"Lookup member '{lookup.name}' holds a {id?.GetType().Name ?? "null"} target, not a value id.");
            }
            return new NSSlotReference(targetId, null, target, lookup.id);
        }

        private static readonly ConditionalWeakTable<LookupMember, TypeInfo> CollectionLookupTargets = new();

        private static readonly TypeInfo UnknownLookupTarget = new UnknownTypeInfo { required = true };

        /// <summary>The Lookup's declared target; a bound Lookup's is its collection's entry type.</summary>
        internal static TypeInfo LookupTargetTypeInfo(LookupMember lookup, Context ctx)
        {
            if (lookup.declaredTypeInfo is { } declared)
                return declared;
            if (CollectionLookupTargets.TryGetValue(lookup, out TypeInfo? cached))
                return cached;
            TypeInfo? stored = NeoReferenceTypes.SlotTypeInfo(ctx.client, lookup);
            TypeInfo? reference = stored is LookupTypeInfo set ? set.entryTypeInfo : stored;
            TypeInfo target = reference is ReferenceTypeInfo { targetTypeInfo: { } named } ? named : UnknownLookupTarget;
            CollectionLookupTargets.Add(lookup, target);
            return target;
        }

        /// <summary>
        /// §4.1. <paramref name="collection"/>'s contents as a local value, so a
        /// slot assigned another collection row keeps its own and shares no
        /// entry row. A class entry stays itself, for the write's ownership
        /// rule to decide.
        /// </summary>
        internal static object CopiedCollectionContents(object collection, Context ctx)
        {
            JsonMember? entryMember = CollectionEntryMember(FindRowReference(collection, ctx), collection, ctx);
            bool nested = entryMember is ListMember or DictionaryMember;
            var cursor = new CollectionCursor(collection, ctx);
            if (collection is not (object?[] or RuntimeSet))
            {
                var dictionary = new Dictionary<string, object?>();
                while (cursor.MoveNextUnresolved())
                    dictionary[cursor.Key.ToString()!] = CopiedEntry(cursor.ResolveEntry(ctx), nested, ctx);
                return KeepDerived(dictionary, null, values: true);
            }
            var entries = new List<object?>();
            while (cursor.MoveNextUnresolved())
                entries.Add(CopiedEntry(cursor.ResolveEntry(ctx), nested, ctx));
            return KeepDerived(entries.ToArray(), null, values: true);
        }

        private static object? CopiedEntry(object? entry, bool nested, Context ctx) =>
            nested && entry is not null ? CopiedCollectionContents(entry, ctx) : entry;

        /// <summary>§4.4. Reading through a reference whose target row is gone.</summary>
        internal static NSGetterRuntimeError DanglingReferenceError(string? memberId, string targetId, Context ctx) =>
            new($"{ReferenceSubject(memberId, ctx)} names value '{targetId}', which no longer exists.");

        /// <summary>"Reference `Owner.Member`" for a stored reference, else "A reference".</summary>
        internal static string ReferenceSubject(string? memberId, Context ctx) =>
            memberId is null ? "A reference" : $"Reference `{QualifiedMemberLabel(memberId, ctx)}`";

        internal static string QualifiedMemberLabel(string memberId, Context ctx)
        {
            string name = ctx.client.TryGetMember(memberId, out JsonMember? member) ? member.name : memberId;
            return ctx.client.FindSchemaPlacement(memberId) is { } placement
                ? $"{placement.ownerClass.name}.{name}"
                : name;
        }
    }
}
