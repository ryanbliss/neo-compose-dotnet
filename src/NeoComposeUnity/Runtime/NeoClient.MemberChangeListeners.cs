// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private readonly Dictionary<(NeoValueOwnership scope, string rootId), NeoChangeListenerMap> sessionChangeListeners = new();
        private readonly Dictionary<(NeoValueOwnership scope, string ownerId), (string expansionId, string rootId, Dictionary<string, NeoDelegateValue[]> members)> defaultChangeListeners = new();
        private readonly Dictionary<string, HashSet<(NeoValueOwnership scope, string ownerId)>> defaultListenerOwnersByExpansion = new();
        private readonly Dictionary<string, string> canonicalListenerMembers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, string>> effectiveListenerMembers = new(StringComparer.Ordinal);
        private object? listenerSchemaResolution;
        private NSGetterEvaluator.Context? memberChangeContext;
        private sealed class ListenerChangeBatch
        {
            internal readonly Dictionary<(NeoValueOwnership scope, string id), (MemberValue? before, MemberValue? after)> Rows = new();
            internal readonly List<(NeoValueOwnership scope, ObjectMemberValue? before, ObjectMemberValue? after)> Owners = new();
            internal readonly HashSet<(NeoValueOwnership scope, string id)> Collections = new();
            internal readonly HashSet<(NeoValueOwnership scope, string id)> Replacements = new();
            internal readonly List<(NeoValueOwnership scope, string ownerId, string memberId)> Changes = new();
            internal readonly HashSet<(NeoValueOwnership scope, string ownerId, string memberId)> Keys = new();
            internal bool IsEmpty = true;
            internal void Clear()
            {
                Rows.Clear();
                Owners.Clear();
                Collections.Clear();
                Replacements.Clear();
                Changes.Clear();
                Keys.Clear();
                IsEmpty = true;
            }
        }

        private ListenerChangeBatch pendingListenerChanges = new();
        private ListenerChangeBatch? spareListenerChanges;

        private sealed class ListenerSlot
        {
            internal NeoValueOwnership Scope;
            internal string OwnerId = null!;
            internal string RootId = null!;
            internal string MemberId = null!;
            internal string SchemaKey = null!;
            internal Member Member = null!;
            internal NeoValueOwnership Storage;
            internal string? ClassId;
            internal object? GenericStamp;
            internal bool Active;
            internal KeyOfPointer Pointer = null!;
            internal Json.TypeInfo ObservedType = null!;
            internal readonly Dictionary<(string memberId, string? classId), (object? genericStamp, string? error)> HandlerSignatures = new();
            internal readonly List<(NeoValueOwnership scope, string id)> Values = new();
        }

        private readonly Dictionary<(NeoValueOwnership scope, string id), List<ListenerSlot>> listenerSlotsByValue = new();
        private readonly Dictionary<(NeoValueOwnership scope, string id), Dictionary<string, ListenerSlot>> listenerSlotsByOwner = new();
        private readonly Dictionary<string, Dictionary<string, MergedSchemaEntry>> listenerSchemaByClass = new(StringComparer.Ordinal);
        private object? listenerSchemaByClassResolution;
        private const int ListenerDiagnosticSlotLimit = 8;
        private const int ListenerDiagnosticIdLength = 96;

        private Dictionary<string, MergedSchemaEntry> ListenerSchema(string classId)
        {
            if (!ReferenceEquals(listenerSchemaByClassResolution, SchemaResolution))
            {
                listenerSchemaByClass.Clear();
                listenerSchemaByClassResolution = SchemaResolution;
            }
            if (listenerSchemaByClass.TryGetValue(classId, out var fields))
                return fields;
            fields = new Dictionary<string, MergedSchemaEntry>(StringComparer.Ordinal);
            foreach (MergedSchemaEntry field in ResolveInstanceSurfaceSchema(classId))
                if (TryGetMember(field.memberId, out Member? declaration))
                    fields.TryAdd(CanonicalListenerMemberId(declaration), field);
            listenerSchemaByClass.Add(classId, fields);
            return fields;
        }

        private object? listenerCapacitySchema;
        private int listenerRootMemberIdByteLimit;

        private int ListenerRootMemberIdByteLimit()
        {
            if (!ReferenceEquals(listenerCapacitySchema, SchemaResolution))
            {
                listenerRootMemberIdByteLimit = 0;
                foreach (string id in data.members.Keys)
                    listenerRootMemberIdByteLimit = Math.Max(listenerRootMemberIdByteLimit, System.Text.Encoding.UTF8.GetByteCount(id));
                listenerCapacitySchema = SchemaResolution;
            }
            return listenerRootMemberIdByteLimit;
        }

        private bool listenerSlotsDirty = true;
        private object? listenerSlotSchema;
        private Dictionary<string, MemberValue> authoredListenerRoots => authoredIndexes.listenerRoots;
        private readonly HashSet<(NeoValueOwnership scope, string id)> dirtyListenerOwners = new();
        private readonly Dictionary<string, List<ListenerSlot>> listenerCollectionsById = new(StringComparer.Ordinal);

        private bool hasListenerSources;

        private void RefreshListenerSources() => hasListenerSources = sessionChangeListeners.Count != 0
            || PersistedData.changeListeners?.Count > 0 || defaultChangeListeners.Count != 0
            || copiedListenerRoots.Count != 0 || authoredListenerRoots.Count != 0;

        internal void RecordListenerReplacement(NeoValueOwnership scope, string id)
        {
            if (hasListenerSources)
            {
                pendingListenerChanges.Replacements.Add((scope, id));
                pendingListenerChanges.IsEmpty = false;
            }
        }

        private void RecordListenerRow(NeoValueOwnership scope, MemberValue? before, MemberValue? after,
            bool preservesOwnerFields = false)
        {
            string? id = after?.id ?? before?.id;
            if (id is null)
                return;
            if (!preservesOwnerFields && (before is ObjectMemberValue { classId: not null } || after is ObjectMemberValue { classId: not null }))
                pendingListenerChanges.Owners.Add((scope, before as ObjectMemberValue, after as ObjectMemberValue));
            var key = (scope, id);
            if (pendingListenerChanges.Rows.TryGetValue(key, out var previous))
                before = previous.before;
            pendingListenerChanges.Rows[key] = (before, after);
            pendingListenerChanges.IsEmpty = false;
        }

        private void EnsureListenerSlots()
        {
            RefreshCopiedListenerIndexes();
            if (!listenerSlotsDirty && ReferenceEquals(listenerSlotSchema, SchemaResolution))
            {
                foreach (var key in dirtyListenerOwners)
                    if (listenerSlotsByOwner.TryGetValue(key, out var slots))
                        foreach (var slot in slots.Values)
                            BindListenerSlot(slot);
                dirtyListenerOwners.Clear();
                return;
            }
            listenerSlotsByValue.Clear();
            listenerCollectionsById.Clear();
            listenerSlotsByOwner.Clear();
            var seen = new HashSet<(NeoValueOwnership scope, string ownerId, string memberId)>();
            foreach (var root in authoredListenerRoots)
                if (root.Value.changeListeners is { } authored
                    && ListensToAuthoredRoot(ResolveAuthoredOwnership(root.Key, root.Value)) is { } authoredScope)
                    AddListenerSlots(root.Key, authored, authoredScope, seen);
            if (PersistedData.changeListeners is { } saved)
                foreach (var root in saved)
                    AddListenerSlots(root.Key, root.Value, PersistedOwnership, seen);
            foreach (var root in sessionChangeListeners)
                AddListenerSlots(root.Key.rootId, root.Value, root.Key.scope, seen);
            var copiedBindingRoots = new Dictionary<(NeoValueOwnership scope, string id), string>();
            foreach (var owner in copiedListenerRootByOwner)
                AddListenerOwner(ListenerBindingRoot(owner.Key.id, owner.Key.scope, copiedBindingRoots), owner.Key.id,
                    copiedListenerRoots[owner.Value][owner.Key.id], owner.Key.scope, seen);
            foreach (var owner in defaultChangeListeners)
                AddListenerOwner(owner.Value.rootId, owner.Key.ownerId, owner.Value.members, owner.Key.scope, seen);
            listenerSlotsDirty = false;
            listenerSlotSchema = SchemaResolution;
            dirtyListenerOwners.Clear();
        }

        private void AddListenerSlots(string rootId, NeoChangeListenerMap map, NeoValueOwnership scope,
            HashSet<(NeoValueOwnership scope, string ownerId, string memberId)> seen)
        {
            foreach (var entry in map)
                AddListenerOwner(rootId, entry.Key, entry.Value, scope, seen);
        }

        private NeoValueOwnership ListenerOwnerScope(NeoValueOwnership scope, string ownerId)
        {
            // A durable map must never select a Session shadow with the same id.
            if (scope == NeoValueOwnership.Session)
                return scope;
            if (TryGetWritableValue(scope, ownerId, out MemberValue? current) && !current.IsRemoved)
                return scope;
            if (data.values.TryGetValue(ownerId, out var authored))
                return ResolveAuthoredOwnership(ownerId, authored);
            if (TryResolveVirtualOwnership(ownerId, out var virtualScope)
                && virtualScope != NeoValueOwnership.Session)
                return virtualScope;
            return scope;
        }

        private void AddListenerOwner(string rootId, string ownerId, Dictionary<string, NeoDelegateValue[]> entry,
            NeoValueOwnership scope, HashSet<(NeoValueOwnership scope, string ownerId, string memberId)> seen)
        {
            NeoValueOwnership ownerScope = ListenerOwnerScope(scope, ownerId);
            foreach (string canonical in entry.Keys)
            {
                if (!seen.Add((ownerScope, ownerId, canonical))
                    || !TryGetMember(canonical, out Member? declaration))
                    continue;
                var slot = new ListenerSlot
                {
                    Scope = ownerScope,
                    OwnerId = ownerId,
                    RootId = rootId,
                    MemberId = canonical,
                    SchemaKey = declaration.name,
                    Member = declaration,
                };
                if (!listenerSlotsByOwner.TryGetValue((ownerScope, ownerId), out var slots))
                    listenerSlotsByOwner[(ownerScope, ownerId)] = slots = new();
                slots.Add(canonical, slot);
                BindListenerSlot(slot);
            }
        }

        private void RefreshListenerOwnerRegistration(NeoValueOwnership scope, string rootId, string ownerId)
        {
            RefreshCopiedListenerIndexes();
            RefreshListenerSources();
            if (listenerSlotsDirty || !ReferenceEquals(listenerSlotSchema, SchemaResolution))
                return;
            var key = (ListenerOwnerScope(scope, ownerId), ownerId);
            if (listenerSlotsByOwner.TryGetValue(key, out var previous))
            {
                foreach (var slot in previous.Values)
                    UnbindListenerSlot(slot);
                listenerSlotsByOwner.Remove(key);
            }
            var seen = new HashSet<(NeoValueOwnership scope, string ownerId, string memberId)>();
            if (CopiedListenerBaseline(scope, ownerId, out _) is { } copied)
                AddListenerOwner(rootId, ownerId, copied, scope, seen);
            if (defaultChangeListeners.TryGetValue(key, out var defaults))
                AddListenerOwner(rootId, ownerId, defaults.members, scope, seen);
            if (AuthoredListenerRoot(rootId)?.changeListeners?.TryGetValue(ownerId, out var authored) == true)
                AddListenerOwner(rootId, ownerId, authored, scope, seen);
            if (!IsTransientListenerTier(scope)
                && PersistedData.changeListeners?.GetValueOrDefault(rootId)?.TryGetValue(ownerId, out var saved) == true)
                AddListenerOwner(rootId, ownerId, saved, scope, seen);
            if (sessionChangeListeners.GetValueOrDefault((scope, rootId))?.TryGetValue(ownerId, out var session) == true)
                AddListenerOwner(rootId, ownerId, session, scope, seen);
        }

        /// <summary>
        /// The scope an authored listener root dispatches under here, or null
        /// when its wiring is the other client's behaviour.
        /// </summary>
        private NeoValueOwnership? ListensToAuthoredRoot(NeoValueOwnership ownership) =>
            RunsBehaviourFor(ownership) ? ownership : null;

        private MemberValue? AuthoredListenerRoot(string rootId) =>
            authoredListenerRoots.GetValueOrDefault(rootId) is { } root
                && ListensToAuthoredRoot(ResolveAuthoredOwnership(rootId, root)) is not null
                ? root
                : null;

        private void InvalidateListenerOwner(NeoValueOwnership scope, string id)
        {
            if (listenerSlotsByOwner.ContainsKey((scope, id)))
                dirtyListenerOwners.Add((scope, id));
        }

        private void UnbindListenerSlot(ListenerSlot slot)
        {
            slot.Active = false;
            foreach (var key in slot.Values)
                if (listenerSlotsByValue.TryGetValue(key, out var previous))
                {
                    previous.Remove(slot);
                    if (listenerCollectionsById.TryGetValue(key.id, out var collections))
                    {
                        collections.Remove(slot);
                        if (collections.Count == 0)
                            listenerCollectionsById.Remove(key.id);
                    }
                    if (previous.Count == 0)
                        listenerSlotsByValue.Remove(key);
                }
            slot.Values.Clear();
        }

        private void BindListenerSlot(ListenerSlot slot)
        {
            UnbindListenerSlot(slot);
            if (!TryGetValue(slot.Scope, slot.OwnerId, out MemberValue? row)
                || row is not ObjectMemberValue { classId: not null } owner || owner.IsRemoved)
                return;
            if (slot.ClassId != owner.classId || !ReferenceEquals(slot.GenericStamp, owner.genericBindings))
            {
                var fields = ListenerSchema(owner.classId);
                Member? resolved = null;
                if (fields.TryGetValue(slot.MemberId, out var placement)
                    && TryGetMember(placement.memberId, out Member? storedMember))
                {
                    resolved = ResolveOwnedMemberType(owner, null, owner.classId, storedMember);
                    slot.SchemaKey = placement.schemaKey;
                }
                if (resolved is null)
                    return;
                var storage = ChildOwnership(resolved, slot.Scope);
                if (!IsChangeListenerMember(resolved) || storage == NeoValueOwnership.Asset)
                    return;
                var observedType = NeoNSFunctionRuntime.TypeInfoFromBindingMember(this, resolved,
                    ListenerOwnerEnvironment(owner), new HashSet<string>());
                slot.Member = resolved;
                slot.Storage = storage;
                slot.ObservedType = observedType;
                slot.Pointer = ListenerValuePointer(slot);
                slot.HandlerSignatures.Clear();
                // Publish the validity stamp only after every fallible resolver.
                slot.ClassId = owner.classId;
                slot.GenericStamp = owner.genericBindings;
            }
            if (!IsChangeListenerMember(slot.Member) || slot.Storage == NeoValueOwnership.Asset)
                return;
            slot.Active = true;
            string? childId = owner.value?.GetValueOrDefault(slot.SchemaKey);
            if (childId is null)
                TryGetVirtualClassChildValueId(slot.OwnerId, slot.SchemaKey, out childId);
            if (childId is null)
                return;
            IndexListenerValue(slot, childId);
            if (slot.Member is ListMember or DictionaryMember)
            {
                if (!listenerCollectionsById.TryGetValue(childId, out var collections))
                    listenerCollectionsById[childId] = collections = new();
                if (!collections.Contains(slot))
                    collections.Add(slot);
            }
        }

        private void IndexListenerValue(ListenerSlot slot, string id)
        {
            var key = (slot.Storage, id);
            if (!listenerSlotsByValue.TryGetValue(key, out var slots))
                listenerSlotsByValue[key] = slots = new();
            if (slots.Contains(slot))
                return;
            slots.Add(slot);
            slot.Values.Add(key);
        }

        private static void QueueListenerSlot(ListenerChangeBatch batch, ListenerSlot slot)
        {
            var key = (slot.Scope, slot.OwnerId, slot.MemberId);
            if (batch.Keys.Add(key))
                batch.Changes.Add(key);
        }

        private void CollectListenerCollectionParents(ListenerChangeBatch batch,
            (NeoValueOwnership scope, string id) changed, bool replacement)
        {
            if (listenerCollectionsById.Count == 0)
                return;
            HashSet<string> parents = RentIdSet();
            try
            {
                CollectPlacementParents(changed.id, parents);
                if (batch.Rows.TryGetValue(changed, out var rows))
                {
                    if (rows.before?.containerId is string before)
                        parents.Add(before);
                    if (rows.after?.containerId is string after)
                        parents.Add(after);
                }
                foreach (string parentId in parents)
                {
                    if (!listenerCollectionsById.TryGetValue(parentId, out var slots))
                        continue;
                    foreach (ListenerSlot slot in slots)
                    {
                        if (!TryGetValue(slot.Storage, parentId, out MemberValue? parent))
                            continue;
                        Member? entry = TryResolveCollectionEntryMember(slot.Member, parent);
                        if (entry is null || ChildOwnership(entry, slot.Storage) != changed.scope
                            || (!replacement && entry is ClassMember or ListMember or DictionaryMember))
                            continue;
                        if (slot.Member is ListMember list && IsUnorderedList(list)
                            && batch.Rows.TryGetValue(changed, out var child)
                            && (child.before?.containerId == parentId || child.after?.containerId == parentId))
                        {
                            QueueListenerSlot(batch, slot);
                            continue;
                        }
                        if (HasCollectionEntryLink(slot.Storage, parentId, changed.id, parent))
                            QueueListenerSlot(batch, slot);
                    }
                }
            }
            finally
            {
                ReturnIdSet(parents);
            }
        }

        private void ResolvePendingListenerChanges(ListenerChangeBatch batch)
        {
            EnsureListenerSlots();
            if (listenerSlotsByOwner.Count == 0)
                return;
            foreach (var change in batch.Rows)
            {
                if (listenerSlotsByValue.TryGetValue(change.Key, out var direct))
                    foreach (ListenerSlot slot in direct)
                        if (change.Value.before is not ObjectMemberValue { classId: not null } before
                            || change.Value.after is not ObjectMemberValue after || before.classId != after.classId)
                            QueueListenerSlot(batch, slot);
                CollectListenerCollectionParents(batch, change.Key, false);
            }
            foreach (var change in batch.Owners)
            {
                string id = change.after?.id ?? change.before!.id;
                if (!listenerSlotsByOwner.TryGetValue((change.scope, id), out var owned))
                    continue;
                foreach (ListenerSlot slot in owned.Values)
                {
                    string? before = change.before?.value?.GetValueOrDefault(slot.SchemaKey);
                    string? after = change.after?.value?.GetValueOrDefault(slot.SchemaKey);
                    if (before != after)
                        QueueListenerSlot(batch, slot);
                    BindListenerSlot(slot);
                }
            }
            foreach (var key in batch.Collections)
                if (listenerSlotsByValue.TryGetValue(key, out var slots))
                    foreach (ListenerSlot slot in slots)
                        QueueListenerSlot(batch, slot);
            foreach (var key in batch.Replacements)
            {
                if (listenerSlotsByValue.TryGetValue(key, out var slots))
                    foreach (ListenerSlot slot in slots)
                        QueueListenerSlot(batch, slot);
                CollectListenerCollectionParents(batch, key, true);
            }
        }

        private static KeyOfPointer ListenerValuePointer(ListenerSlot slot)
        {
            return new KeyOfPointer
            {
                type = PointerKind.KeyOf,
                memberId = slot.Member.id,
                keyOf = new KeyOf
                {
                    pointer = new VariablePointer { type = PointerKind.Variable, variableId = "listenerOwner" },
                    key = new ValuePointer
                    {
                        type = PointerKind.Value,
                        value = new Value
                        {
                            typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                            value = new JValue(slot.SchemaKey),
                        },
                    },
                },
            };
        }

        private sealed class ListenerDispatchScratch
        {
            internal readonly List<(NeoDelegateValue target, bool session)> Snapshot = new();
            internal readonly HashSet<(NeoValueOwnership? scope, string target)> Identities = new();
            internal readonly NeoScriptScope Scope = new();
            internal readonly object?[] Arguments = new object?[1];
        }

        private ListenerDispatchScratch? listenerDispatchScratch;
        private bool listenerDispatchScratchInUse;

        private void DispatchMemberChange(NeoValueOwnership ownership, string ownerId, string memberId)
        {
            if (!TryGetValue(ownership, ownerId, out MemberValue? row)
                || row is not ObjectMemberValue { classId: not null } owner || row.IsRemoved)
                return;
            if (!listenerSlotsByOwner.TryGetValue((ownership, ownerId), out var slots)
                || !slots.TryGetValue(memberId, out var slot) || !slot.Active)
                return;
            Member observed = slot.Member;
            string rootId = slot.RootId;
            NeoValueOwnership observedLifetime = ChildOwnership(observed, ownership);
            NeoDelegateValue[] durable = IsTransientListenerTier(ownership) ? Array.Empty<NeoDelegateValue>()
                : PersistedData.changeListeners?.GetValueOrDefault(rootId)?.Find(ownerId, memberId)
                    ?? InheritedChangeListeners(rootId, ownerId, memberId, PersistedOwnership, observedLifetime, ownership);
            NeoDelegateValue[] session = sessionChangeListeners.GetValueOrDefault((ownership, rootId))?.Find(ownerId, memberId)
                ?? InheritedChangeListeners(rootId, ownerId, memberId, NeoValueOwnership.Session, observedLifetime, ownership);
            if (durable.Length + session.Length == 0)
                return;
            bool pooled = !listenerDispatchScratchInUse;
            listenerDispatchScratchInUse = true;
            var scratch = pooled ? listenerDispatchScratch : null;
            scratch ??= new ListenerDispatchScratch();
            if (pooled)
                listenerDispatchScratch = scratch;
            var snapshot = scratch.Snapshot;
            var identities = scratch.Identities;
            var context = CreateGetterContext(ownership);
            context.BindRoot(NeoScriptValueMarshaller.ResolveRoot(this, context));
            var enclosingContext = memberChangeContext;
            if (enclosingContext is not null)
                context.ShareAllocations(enclosingContext);
            try
            {
                foreach (var target in durable)
                    snapshot.Add((target.PersistedCopy(), false));
                foreach (var target in session)
                    snapshot.Add((target.PersistedCopy(), true));
                object? receiver = NSGetterEvaluator.UnwrapRow(owner, context, ownership);
                scratch.Scope.SetLocal("listenerOwner", receiver);
                object? value = NSGetterEvaluator.EvalPointer(slot.Pointer, scratch.Scope, context);
                scratch.Arguments[0] = value;
                memberChangeContext = context;
                foreach (var registration in snapshot)
                {
                    NeoDelegateValue target = registration.target;
                    NeoValueOwnership? receiverScope = null;
                    if (target.valueId is string targetId)
                    {
                        TryGetValueOwnership(targetId, out var currentScope);
                        receiverScope = registration.session ? currentScope : ListenerOwnerScope(PersistedOwnership, targetId);
                    }
                    bool sessionTarget = IsSessionListenerTarget(target, observedLifetime, receiverScope);
                    if (!registration.session && sessionTarget)
                        continue;
                    Member? handler = ChangeListenerHandler(target, owner, receiverScope, slot.ObservedType, slot.HandlerSignatures);
                    if (handler is null)
                        continue;
                    NeoDelegateValue invocation = target.PersistedCopy();
                    invocation.memberId = CanonicalListenerMemberId(handler);
                    if (!identities.Add((receiverScope, NeoActionValue.ListenerIdentity(invocation))))
                        continue;
                    invocation.memberId = handler.id;
                    NSGetterEvaluator.InvokeDelegate(invocation, scratch.Arguments, context, receiver, receiverScope);
                }
            }
            finally
            {
                memberChangeContext = enclosingContext;
                snapshot.Clear();
                identities.Clear();
                scratch.Scope.SetLocal("listenerOwner", null);
                scratch.Arguments[0] = null;
                if (pooled)
                    listenerDispatchScratchInUse = false;
            }
        }

        /// <summary>
        /// The member <paramref name="target"/> runs, or null when its
        /// receiver or member is gone or it can't take the observed value.
        /// </summary>
        private Member? ChangeListenerHandler(NeoDelegateValue target, ObjectMemberValue owner, NeoValueOwnership? receiverScope,
            Json.TypeInfo observedType, Dictionary<(string memberId, string? classId), (object? genericStamp, string? error)> signatures)
        {
            var resolved = ResolveChangeListenerHandler(target, owner, receiverScope);
            if (resolved is null)
                return null;
            var signatureKey = (resolved.Value.member.id, resolved.Value.receiver.classId);
            var genericStamp = resolved.Value.receiver.genericBindings;
            if (!signatures.TryGetValue(signatureKey, out var signature)
                || !ReferenceEquals(signature.genericStamp, genericStamp))
            {
                signature = (genericStamp, ChangeListenerSignatureError(target.memberId!, observedType,
                    resolved.Value.member, resolved.Value.receiver));
                signatures[signatureKey] = signature;
            }
            return signature.error is null ? resolved.Value.member : null;
        }

        internal string CanonicalListenerMemberId(Member member)
        {
            if (!ReferenceEquals(listenerSchemaResolution, SchemaResolution))
            {
                canonicalListenerMembers.Clear();
                effectiveListenerMembers.Clear();
                listenerSchemaResolution = SchemaResolution;
            }
            string identity = member.RuntimeDeclarationIdentity;
            if (canonicalListenerMembers.TryGetValue(identity, out string cached))
                return cached;
            var chain = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string canonical;
            while (true)
            {
                string current = member.RuntimeDeclarationIdentity;
                if (canonicalListenerMembers.TryGetValue(current, out canonical))
                    break;
                if (!seen.Add(current))
                    throw new InvalidOperationException($"Member '{identity}' has a cyclic override chain.");
                chain.Add(current);
                if (string.IsNullOrEmpty(member.extendsMemberId))
                {
                    canonical = current;
                    break;
                }
                if (!TryGetMember(member.extendsMemberId!, out Member? parent))
                    throw new InvalidOperationException($"Member '{member.id}' has a missing override declaration.");
                member = parent;
            }
            foreach (string id in chain)
                canonicalListenerMembers[id] = canonical;
            return canonical;
        }

        private string ListenerBindingRoot(string ownerId, NeoValueOwnership ownership,
            Dictionary<(NeoValueOwnership scope, string id), string>? cache = null)
        {
            var seen = new HashSet<(NeoValueOwnership scope, string id)>();
            string current = ownerId;
            while (true)
            {
                if (cache?.TryGetValue((ownership, current), out string? cached) == true)
                {
                    current = cached;
                    break;
                }
                if (!seen.Add((ownership, current)))
                    throw new InvalidOperationException($"Listener owner '{ownerId}' has cyclic ownership.");
                if (!TryFindOwnedParent(ownership, current, out string? parent, out var parentOwnership)
                    || parent.StartsWith("member:", StringComparison.Ordinal)
                    || parent.StartsWith("static:", StringComparison.Ordinal))
                    break;
                current = parent;
                ownership = parentOwnership;
            }
            if (cache is not null)
                foreach (var key in seen)
                    cache[key] = current;
            return current;
        }

        private static bool IsChangeListenerMember(Member member) =>
            member.Mutability != NeoMemberMutabilityKind.ReadOnly
            && member.Modifier != NeoMemberModifierKind.Static
            && member is not (NSPropertyMember or FunctionMember or NSFunctionMember or FunctionRefMember or DelegateMember or ActionMember);

        internal void EditMemberChangeListener(string ownerId, NeoValueOwnership ownership,
            string memberId, Json.TypeInfo observedType, NeoDelegateValue listener, bool add) =>
            EditMemberChangeListeners(ownerId, ownership, memberId, observedType, listener, add);

        /// <summary><c>member.OnChanged.Clear()</c>: every handler leaves Save and Session wiring.</summary>
        internal void ClearMemberChangeListeners(string ownerId, NeoValueOwnership ownership,
            string memberId, Json.TypeInfo observedType) =>
            EditMemberChangeListeners(ownerId, ownership, memberId, observedType, listener: null, add: false);

        // A null listener clears the set.
        private void EditMemberChangeListeners(string ownerId, NeoValueOwnership ownership,
            string memberId, Json.TypeInfo observedType, NeoDelegateValue? listener, bool add)
        {
            if (!TryGetValue(ownership, ownerId, out MemberValue? owner)
                || owner is not ObjectMemberValue { classId: not null } instance)
                throw new NSGetterRuntimeError($"Listener owner '{ownerId}' is not a live class value.");
            if (!TryGetMember(memberId, out Member? declaration))
                throw new NSGetterRuntimeError($"Observed member '{memberId}' does not exist.");
            if (declaration is NSPropertyMember)
            {
                EditGetterChangeListener(instance, declaration, observedType, listener, add);
                return;
            }
            string canonical = CanonicalListenerMemberId(declaration);
            Member? observed = null;
            if (ListenerSchema(instance.classId).TryGetValue(canonical, out var slot)
                && TryGetMember(slot.memberId, out Member? member))
                observed = ResolveOwnedMemberType(instance, null, instance.classId, member);
            if (observed is null)
                throw new NSGetterRuntimeError($"Observed member '{memberId}' does not belong to owner '{ownerId}'.");
            if (!IsChangeListenerMember(observed))
                throw new NSGetterRuntimeError($"Observed member '{memberId}' must be a writable stored instance member.");
            NeoValueOwnership lifetime = ChildOwnership(observed, ownership);
            NeoValueOwnership observedLifetime = lifetime;
            if (lifetime == NeoValueOwnership.Asset)
                throw new NSGetterRuntimeError($"Observed member '{memberId}' is immutable at runtime.");
            var environment = ListenerOwnerEnvironment(instance);
            Json.TypeInfo actualType = NeoNSFunctionRuntime.TypeInfoFromBindingMember(this, observed, environment, new HashSet<string>());
            if (!TypeInfoMatches(observedType, actualType))
                throw new NSGetterRuntimeError($"Observed member '{memberId}' no longer matches its compiled type.");
            if (listener is not null)
            {
                ValidateChangeListenerHandler(listener, observedType, instance);
                listener = listener.PersistedCopy();
                if (TryGetMember(listener.memberId!, out Member? handler) && handler.Modifier != NeoMemberModifierKind.Static)
                    listener.memberId = CanonicalListenerMemberId(handler);
                if (listener.valueId is string receiverId)
                {
                    if (!TryGetValueOwnership(receiverId, out NeoValueOwnership receiverOwnership))
                        throw new NSGetterRuntimeError($"Listener receiver '{receiverId}' is not live.");
                    if (IsTransientListenerTier(receiverOwnership))
                        lifetime = NeoValueOwnership.Session;
                }
            }
            // A save client's wiring on User data is its own, never the
            // user file's (P104 §3.5).
            if (IsTransientListenerTier(lifetime))
                lifetime = NeoValueOwnership.Session;

            if (isReplayingVirtualInstance && replayAllocationScope is not null)
            {
                ReplayAllocationScope? ownerScope = replayAllocationScope;
                while (ownerScope is not null && !ownerScope.Ids.Contains(ownerId))
                    ownerScope = ownerScope.Parent;
                if (ownerScope is null)
                    throw new NSGetterRuntimeError($"Stored constructor replay cannot subscribe to existing owner '{ownerId}'.");
                if (nestedConstructedRows is not null
                    && nestedConstructedRows.TryGetValue(ownerId, out var producer)
                    && !ReferenceEquals(producer, nestedConstructorCapture))
                    producer.HasExternalWrites = true;
                ownerScope.ListenerDefaults.TryGetValue(ownerId, out var previous);
                if (TryEditListenerEntry(previous, canonical, Array.Empty<NeoDelegateValue>(), listener, add, out var updated, inheritedComplete: false))
                {
                    if (updated is null)
                        ownerScope.ListenerDefaults.Remove(ownerId);
                    else
                        ownerScope.ListenerDefaults[ownerId] = updated;
                }
                return;
            }

            if (constructionListenerCapture is not null && ownership == NeoValueOwnership.Session)
            {
                var previous = PendingConstructionListeners(ownerId);
                if (TryEditListenerEntry(previous, canonical, Array.Empty<NeoDelegateValue>(), listener, add, out var updated, inheritedComplete: false))
                    constructionListenerCapture.Defaults[ownerId] = updated ?? new(StringComparer.Ordinal);
                return;
            }

            string rootId = ListenerBindingRoot(ownerId, ownership);
            NeoWritePlan? pendingPlan = candidateReadPlan;
            if (pendingPlan is null && scriptWriteDepth != 0 && candidateReplay is null
                && replayAllocationScope is null && nestedConstructorCapture is null && !isReplayingVirtualInstance)
            {
                scriptWriteBatch ??= new NeoWriteBatch(new NeoWritePlan(this), held: true);
                pendingPlan = scriptWriteBatch.Plan;
            }
            if (pendingPlan is not null)
                PrepareListenerMove(pendingPlan, ownership, ownerId);
            bool inheritedComplete = true;
            foreach (var target in InheritedChangeListenerTargets(rootId, ownerId, canonical, ownership))
                if (target.valueId is string inheritedReceiver && !TryGetValueOwnership(inheritedReceiver, out _)
                    && HasUnloadedListenerPartitions())
                {
                    inheritedComplete = false;
                    break;
                }
            NeoWritePlan? plan = pendingPlan;
            if (listener is null)
            {
                // Clearing empties both tiers. A Session owner has no durable one.
                if (lifetime == PersistedOwnership)
                    StageListenerEntry(ref plan, pendingPlan, PersistedOwnership, ownership, rootId, ownerId, canonical,
                        observedLifetime, null, false, inheritedComplete);
                StageListenerEntry(ref plan, pendingPlan, NeoValueOwnership.Session, ownership, rootId, ownerId, canonical,
                    observedLifetime, null, false, inheritedComplete);
            }
            else
            {
                // Promotion retains existing temporary registrations. Repeating +=
                // must not turn one into persisted wiring, and -= must still find it.
                if (lifetime == PersistedOwnership)
                {
                    Dictionary<string, NeoDelegateValue[]>? temporary;
                    if (pendingPlan?.ListenerEntries?.TryGetValue((NeoValueOwnership.Session, ownership, rootId, ownerId), out temporary) != true)
                        temporary = sessionChangeListeners.GetValueOrDefault((ownership, rootId))?.GetValueOrDefault(ownerId);
                    if (temporary?.TryGetValue(canonical, out var targets) == true)
                        foreach (var target in targets)
                            if (NeoActionValue.ListenerIdentity(target) == NeoActionValue.ListenerIdentity(listener))
                            {
                                lifetime = NeoValueOwnership.Session;
                                break;
                            }
                }
                StageListenerEntry(ref plan, pendingPlan, lifetime, ownership, rootId, ownerId, canonical,
                    observedLifetime, listener, add, inheritedComplete);
            }
            if (pendingPlan is null)
                plan?.Commit();
        }

        private void StageListenerEntry(ref NeoWritePlan? plan, NeoWritePlan? pendingPlan, NeoValueOwnership lifetime,
            NeoValueOwnership ownership, string rootId, string ownerId, string canonical, NeoValueOwnership observedLifetime,
            NeoDelegateValue? listener, bool add, bool inheritedComplete)
        {
            NeoChangeListenerMap? explicitMap = lifetime == NeoValueOwnership.Session
                ? sessionChangeListeners.GetValueOrDefault((ownership, rootId))
                : PersistedData.changeListeners?.GetValueOrDefault(rootId);
            Dictionary<string, NeoDelegateValue[]>? previousEntry = null;
            if (pendingPlan?.ListenerEntries?.TryGetValue((lifetime, ownership, rootId, ownerId), out previousEntry) != true)
                explicitMap?.TryGetValue(ownerId, out previousEntry);
            NeoDelegateValue[] inherited = InheritedChangeListeners(rootId, ownerId, canonical, lifetime, observedLifetime, ownership);
            if (!TryEditListenerEntry(previousEntry, canonical, inherited, listener, add, out var entry, inheritedComplete))
                return;
            plan ??= new NeoWritePlan(this);
            plan.SetListenerEntry(lifetime, rootId, ownerId, entry, ownership);
        }

        // A null listener clears the set.
        private static bool TryEditListenerEntry(Dictionary<string, NeoDelegateValue[]>? previous, string memberId,
            NeoDelegateValue[] inherited, NeoDelegateValue? listener, bool add, out Dictionary<string, NeoDelegateValue[]>? entry,
            bool inheritedComplete = true)
        {
            NeoDelegateValue[] current = previous?.GetValueOrDefault(memberId) ?? inherited;
            entry = previous;
            var next = new List<NeoDelegateValue>(current.Length + (add ? 1 : 0));
            if (listener is null)
            {
                if (current.Length == 0)
                    return false;
            }
            else
            {
                string identity = NeoActionValue.ListenerIdentity(listener);
                int found = Array.FindIndex(current, item => NeoActionValue.ListenerIdentity(item) == identity);
                if (add == (found >= 0))
                    return false;
                for (int index = 0; index < current.Length; index++)
                    if (add || index != found)
                        next.Add(current[index].PersistedCopy());
                if (add)
                    next.Add(listener.PersistedCopy());
            }
            entry = previous is null ? new(StringComparer.Ordinal) : new(previous, StringComparer.Ordinal);
            if (next.Count == 0 && inherited.Length == 0 && inheritedComplete)
                entry.Remove(memberId);
            else
                entry[memberId] = next.ToArray();
            if (entry.Count == 0)
                entry = null;
            return true;
        }

        private void ProjectAuthoredListenerDefaults(NeoChangeListenerMap? authored,
            IReadOnlyDictionary<string, string> clonedIds, NeoChangeListenerEndpoints? endpoints, bool preserveProductListeners = false)
        {
            NeoChangeListenerMap? defaults = replayAllocationScope?.ListenerDefaults ?? constructionListenerCapture?.Defaults;
            if (authored is null || defaults is null)
                return;
            foreach (var owner in authored)
            {
                if (!clonedIds.TryGetValue(owner.Key, out string? ownerId))
                    continue;
                if (!defaults.TryGetValue(ownerId, out var members))
                    defaults[ownerId] = members = new Dictionary<string, NeoDelegateValue[]>(StringComparer.Ordinal);
                foreach (var member in owner.Value)
                {
                    var targets = new List<NeoDelegateValue>(member.Value.Length);
                    foreach (NeoDelegateValue source in member.Value)
                    {
                        NeoDelegateValue target = source.PersistedCopy();
                        if (target.valueId is string receiver)
                        {
                            if (clonedIds.TryGetValue(receiver, out string? clonedReceiver))
                                target.valueId = clonedReceiver;
                            else if (endpoints?.ContainsKey(receiver) == true)
                                continue;
                        }
                        targets.Add(target);
                    }
                    string canonical = TryGetMember(member.Key, out Member? declaration)
                        ? CanonicalListenerMemberId(declaration) : member.Key;
                    if (!preserveProductListeners || !members.ContainsKey(canonical))
                        members[canonical] = targets.ToArray();
                }
            }
        }

        private void InstallListenerDefaults(PreparedVirtualExpansion expansion)
        {
            foreach (var pair in expansion.ListenerDefaults)
                InstallListenerDefault(expansion.Root.id, pair.Key.scope, pair.Key.ownerId, pair.Value);
        }

        private void ClearListenerDefaults(string expansionId)
        {
            if (!defaultListenerOwnersByExpansion.Remove(expansionId, out var owners))
                return;
            foreach (var owner in owners)
                if (defaultChangeListeners.TryGetValue(owner, out var entry) && entry.expansionId == expansionId)
                {
                    defaultChangeListeners.Remove(owner);
                    RefreshListenerOwnerRegistration(owner.scope, entry.rootId, owner.ownerId);
                }
        }

        private NeoDelegateValue[] InheritedChangeListenerTargets(string rootId, string ownerId, string memberId,
            NeoValueOwnership scope, bool ignoreCopied = false)
        {
            if (!ignoreCopied && CopiedListenerBaseline(scope, ownerId, out bool adopted)?.GetValueOrDefault(memberId) is { } copied)
            {
                if (!adopted)
                    return copied;
                if (TryGetValue(scope, ownerId, out MemberValue? row) && row is ObjectMemberValue { classId: not null } owner
                    && TryGetMember(memberId, out Member? declaration))
                {
                    Member member = ResolveOwnedMemberType(owner, null, owner.classId, declaration);
                    var observedLifetime = ChildOwnership(member, scope);
                    var combined = new List<NeoDelegateValue>();
                    foreach (var target in InheritedChangeListenerTargets(rootId, ownerId, memberId, scope, ignoreCopied: true))
                        if (!IsSessionListenerTarget(target, observedLifetime))
                            combined.Add(target);
                    foreach (var target in copied)
                        if (IsSessionListenerTarget(target, observedLifetime))
                            combined.Add(target);
                    return combined.ToArray();
                }
            }
            NeoDelegateValue[]? targets = data.values.GetValueOrDefault(rootId) is { changeListeners: { } authored } authoredRoot
                && ListensToAuthoredRoot(ResolveAuthoredOwnership(rootId, authoredRoot)) is not null
                ? authored.Find(ownerId, memberId)
                : null;
            if (targets is not null)
                return targets;
            if (candidateReplay?.ConstructionDefaults.TryGetValue(ownerId, out var pending) == true)
                return pending.members.GetValueOrDefault(memberId) ?? Array.Empty<NeoDelegateValue>();
            if (candidateReplay?.ListenerDefaults.TryGetValue((scope, ownerId), out var prepared) == true)
                return prepared.GetValueOrDefault(memberId) ?? Array.Empty<NeoDelegateValue>();
            if (defaultChangeListeners.TryGetValue((scope, ownerId), out var defaults)
                && !(candidateReplay?.AffectedRoots.Contains(defaults.expansionId) == true
                    && !candidateReplay.RetainedRoots.Contains(defaults.expansionId)))
                return defaults.members.GetValueOrDefault(memberId) ?? Array.Empty<NeoDelegateValue>();
            return Array.Empty<NeoDelegateValue>();
        }

        private NeoDelegateValue[] InheritedChangeListeners(string rootId, string ownerId, string memberId,
            NeoValueOwnership lifetime, NeoValueOwnership observedLifetime, NeoValueOwnership scope)
        {
            NeoDelegateValue[] targets = InheritedChangeListenerTargets(rootId, ownerId, memberId, scope);
            var result = new List<NeoDelegateValue>();
            foreach (var target in targets)
            {
                bool session = IsSessionListenerTarget(target, observedLifetime);
                if (session == (lifetime == NeoValueOwnership.Session))
                    result.Add(target);
            }
            return result.ToArray();
        }

        private IReadOnlyDictionary<string, NeoGenericEnvEntry> ListenerOwnerEnvironment(ObjectMemberValue owner,
            Dictionary<ObjectMemberValue, IReadOnlyDictionary<string, NeoGenericEnvEntry>>? cache = null)
        {
            if (cache?.TryGetValue(owner, out var cached) == true)
                return cached;
            var arguments = NeoGenericResolution.CloseClassArgumentsFromStamp(owner.genericBindings, null);
            var environment = NeoGenericResolution.ResolveInstanceEnv(this, owner.classId!, arguments);
            if (cache is not null)
                cache[owner] = environment;
            return environment;
        }

        private (Member member, ObjectMemberValue receiver)? ResolveChangeListenerHandler(NeoDelegateValue target, ObjectMemberValue owner, NeoValueOwnership? receiverScope)
        {
            if (target.memberId is null || !TryGetMember(target.memberId, out Member? member))
                return null;
            ObjectMemberValue receiver = owner;
            if (target.valueId is string receiverId)
            {
                bool found = receiverScope is { } scope
                    ? TryGetValue(scope, receiverId, out MemberValue? row)
                    : TryGetValue(receiverId, out row);
                if (!found || row!.IsRemoved
                    || row is not ObjectMemberValue { classId: not null } instance)
                    return null;
                receiver = instance;
            }
            if (member.Modifier == NeoMemberModifierKind.Static)
                return (member, receiver);
            Member? effective = EffectiveInstanceMember(receiver.classId!, CanonicalListenerMemberId(member));
            return effective is null ? null : (effective, receiver);
        }

        /// <summary>The member <paramref name="classId"/> instances run for canonical declaration <paramref name="canonical"/>.</summary>
        private Member? EffectiveInstanceMember(string classId, string canonical)
        {
            if (!effectiveListenerMembers.TryGetValue(classId, out var members))
            {
                members = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var slot in ResolveInstanceSurfaceSchema(classId))
                    if (TryGetMember(slot.memberId, out Member? candidate))
                        members[CanonicalListenerMemberId(candidate)] = slot.memberId;
                effectiveListenerMembers[classId] = members;
            }
            return members.TryGetValue(canonical, out string id) && TryGetMember(id, out Member? effective) ? effective : null;
        }

        private void ValidateChangeListenerHandler(NeoDelegateValue target, Json.TypeInfo observedType, ObjectMemberValue owner)
        {
            string? error = ChangeListenerHandlerError(target, observedType, owner, out _);
            if (error is not null)
                throw new NSGetterRuntimeError(error);
        }

        private string? ChangeListenerHandlerError(NeoDelegateValue target, Json.TypeInfo observedType, ObjectMemberValue owner,
            out (Member member, ObjectMemberValue receiver)? resolved,
            Dictionary<ObjectMemberValue, IReadOnlyDictionary<string, NeoGenericEnvEntry>>? environments = null,
            NeoValueOwnership? receiverScope = null)
        {
            resolved = null;
            if (!target.IsMemberTarget || target.IsClosure)
                return "A change listener requires a persisted method target.";
            if (!TryGetMember(target.memberId!, out Member? _))
                return $"Listener method '{target.memberId}' does not exist.";
            resolved = ResolveChangeListenerHandler(target, owner, receiverScope);
            if (resolved is null)
                return $"Listener method '{target.memberId}' does not belong to a live receiver.";
            return ChangeListenerSignatureError(target.memberId!, observedType, resolved.Value.member, resolved.Value.receiver, environments);
        }

        private string? ChangeListenerSignatureError(string targetMemberId, Json.TypeInfo observedType, Member member,
            ObjectMemberValue receiver, Dictionary<ObjectMemberValue, IReadOnlyDictionary<string, NeoGenericEnvEntry>>? environments = null)
        {
            Json.TypeInfo? result;
            FunctionArgumentTypeInfo[]? arguments;
            switch (member)
            {
                case FunctionMember function:
                    if (!TryResolveFunctionMember(function.id, out FunctionMember? native))
                        return $"Listener method '{function.id}' has no resolved signature.";
                    if (native.Dispatch == NeoFunctionDispatchKind.Asynchronous)
                        return $"Listener method '{function.id}' must run immediately.";
                    result = native.returnTypeInfo;
                    arguments = native.argumentTypes;
                    break;
                case NSFunctionMember function:
                    var signature = ResolveNSFunctionSignature(function.id);
                    if (signature?.Dispatch == NeoFunctionDispatchKind.Asynchronous)
                        return $"Listener method '{function.id}' must run immediately.";
                    result = signature?.returnTypeInfo;
                    arguments = signature?.argumentTypes;
                    break;
                case DelegateMember callable:
                    result = callable.returnTypeInfo;
                    arguments = callable.argumentTypes;
                    break;
                case ActionMember action:
                    result = new VoidTypeInfo { type = MemberKind.Void, required = true };
                    arguments = action.argumentTypes;
                    break;
                default:
                    return $"Listener member '{targetMemberId}' is not callable.";
            }
            if (result?.type != MemberKind.Void)
                return $"Listener method '{targetMemberId}' must return void.";
            if (arguments?.Length != 1)
                return $"Listener method '{targetMemberId}' must take one argument.";
            Json.TypeInfo argument;
            try
            {
                argument = NeoNSFunctionRuntime.ResolveInvocationTypeInfo(this, arguments[0], ListenerOwnerEnvironment(receiver, environments));
            }
            catch (NSGetterRuntimeError error)
            {
                return $"Listener method '{targetMemberId}' has an unresolved argument type: {error.Message}";
            }
            if (!TypeInfoMatches(argument, observedType))
                return $"Listener method '{targetMemberId}' has an incompatible argument type.";
            return null;
        }

        private bool HasUnloadedListenerPartitions() =>
            data.valuePartitions.Count > loadedPartitionRowIds.Count;

        private sealed class ListenerPruningContext
        {
            private readonly NeoClient client;
            private readonly bool hasUnloadedPartitions;
            private readonly Dictionary<(NeoValueOwnership scope, string id), (MemberValue? row, bool authoritative)> endpoints = new();
            internal readonly Dictionary<ObjectMemberValue, IReadOnlyDictionary<string, NeoGenericEnvEntry>> Environments = new();
            internal ListenerPruningContext(NeoClient client)
            {
                this.client = client;
                hasUnloadedPartitions = client.HasUnloadedListenerPartitions();
            }
            internal (MemberValue? row, bool authoritative) Endpoint(NeoValueOwnership scope, string id)
            {
                if (endpoints.TryGetValue((scope, id), out var found))
                    return found;
                client.TryGetValue(scope, id, out MemberValue? row);
                bool authoritative = row is null ? !hasUnloadedPartitions
                    : row.mapKey is null || !client.HasValuePartition(row.mapKey) || client.IsValuePartitionLoaded(row.mapKey);
                endpoints[(scope, id)] = found = (row, authoritative);
                return found;
            }
        }

        private bool KeepListenerTarget(NeoDelegateValue target, ObjectMemberValue owner, Json.TypeInfo observedType,
            NeoValueOwnership observedLifetime, NeoValueOwnership mapTier, ListenerPruningContext context, bool inherited)
        {
            NeoValueOwnership? receiverScope = null;
            if (target.valueId is string receiverId)
            {
                TryGetValueOwnership(receiverId, out var currentScope);
                receiverScope = mapTier == NeoValueOwnership.Session ? currentScope : ListenerOwnerScope(PersistedOwnership, receiverId);
                var receiver = context.Endpoint(receiverScope.Value, receiverId);
                if (!receiver.authoritative)
                    return true;
                if (receiver.row is null || receiver.row.IsRemoved)
                    return false;
            }
            bool session = IsSessionListenerTarget(target, observedLifetime, receiverScope);
            if (inherited && session != (mapTier == NeoValueOwnership.Session))
                return false;
            if (mapTier != NeoValueOwnership.Session && session)
                return false;
            return ChangeListenerHandlerError(target, observedType, owner, out _, context.Environments, receiverScope) is null;
        }

        private void PruneListenerEntries(NeoWritePlan plan,
            Dictionary<(NeoValueOwnership ownership, NeoValueOwnership scope, string rootId), NeoChangeListenerMap> maps)
        {
            var context = new ListenerPruningContext(this);
            using var candidate = ReadCandidate(plan);

            foreach (var root in maps)
            {
                foreach (string ownerId in new List<string>(root.Value.Keys))
                {
                    NeoValueOwnership ownerScope = ListenerOwnerScope(root.Key.scope, ownerId);
                    var endpoint = context.Endpoint(ownerScope, ownerId);
                    if (!endpoint.authoritative)
                        continue;
                    var previous = root.Value[ownerId];
                    Dictionary<string, NeoDelegateValue[]>? next = null;
                    if (endpoint.row is ObjectMemberValue { classId: not null } owner && !owner.IsRemoved)
                    {
                        var schema = ListenerSchema(owner.classId);
                        next = new(StringComparer.Ordinal);
                        foreach (var slot in previous)
                        {
                            if (!schema.TryGetValue(slot.Key, out var field)
                                || !TryGetMember(field.memberId, out Member? declaration))
                                continue;
                            NeoValueOwnership observedLifetime;
                            Json.TypeInfo observedType;
                            try
                            {
                                Member member = ResolveOwnedMemberType(owner, null, owner.classId, declaration);
                                observedLifetime = ChildOwnership(member, ownerScope);
                                if (!IsChangeListenerMember(member) || observedLifetime == NeoValueOwnership.Asset)
                                    continue;
                                observedType = NeoNSFunctionRuntime.TypeInfoFromBindingMember(this, member,
                                    ListenerOwnerEnvironment(owner, context.Environments), new HashSet<string>());
                            }
                            catch (NSGetterRuntimeError)
                            {
                                continue;
                            }
                            var retained = new List<NeoDelegateValue>(slot.Value.Length);
                            foreach (var target in slot.Value)
                                if (KeepListenerTarget(target, owner, observedType, observedLifetime, root.Key.ownership, context, inherited: false))
                                    retained.Add(target);
                            if (retained.Count == 0)
                            {
                                bool inherited = false;
                                foreach (var target in InheritedChangeListenerTargets(root.Key.rootId, ownerId, slot.Key, ownerScope))
                                {
                                    if (KeepListenerTarget(target, owner, observedType, observedLifetime, root.Key.ownership, context, inherited: true))
                                    {
                                        inherited = true;
                                        break;
                                    }
                                }
                                if (!inherited)
                                    continue;
                            }
                            next[slot.Key] = retained.ToArray();
                        }
                        if (next.Count == 0)
                            next = null;
                    }
                    bool changed = next is null || previous.Count != next.Count;
                    if (!changed)
                        foreach (var slot in previous)
                            if (!next!.TryGetValue(slot.Key, out var retained) || slot.Value.Length != retained.Length)
                            {
                                changed = true;
                                break;
                            }
                    if (!changed)
                        continue;
                    if (next is null)
                        root.Value.Remove(ownerId);
                    else
                        root.Value[ownerId] = next;
                    plan.SetListenerEntry(root.Key.ownership, root.Key.rootId, ownerId, next, root.Key.scope);
                }
            }
        }

        private Dictionary<(NeoValueOwnership ownership, NeoValueOwnership scope, string rootId), NeoChangeListenerMap>? PrepareListenerEntries(NeoWritePlan plan)
        {
            if (plan.ListenerEntries is null || plan.ListenerEntries.Count == 0)
                return null;
            var maps = new Dictionary<(NeoValueOwnership ownership, NeoValueOwnership scope, string rootId), NeoChangeListenerMap>();
            foreach (var change in plan.ListenerEntries)
            {
                var key = (change.Key.ownership, change.Key.scope, change.Key.rootId);
                if (!maps.TryGetValue(key, out var map))
                {
                    NeoChangeListenerMap? previous = key.ownership == NeoValueOwnership.Session
                        ? sessionChangeListeners.GetValueOrDefault((key.scope, key.rootId))
                        : PersistedData.changeListeners?.GetValueOrDefault(key.rootId);
                    maps[key] = map = previous?.Copy() ?? new NeoChangeListenerMap();
                }
                if (change.Value is null)
                    map.Remove(change.Key.ownerId);
                else
                    map[change.Key.ownerId] = change.Value;
            }
            PruneListenerEntries(plan, maps);
            foreach (var pair in maps)
            {
                MemberValue? root = plan.Resolve(pair.Key.scope, pair.Key.rootId);
                if (pair.Value.Count == 0)
                    continue;
                if (root is null || root.IsRemoved)
                    throw new NSGetterRuntimeError($"Listener binding root '{pair.Key.rootId}' is not live.");
                int bytes = NeoChangeListenerPatches.EncodedRecordSize(PersistedData, pair.Key.rootId, pair.Value, ListenerRootMemberIdByteLimit());
                if (bytes > NeoChangeListenerPatches.MaxRecordBytes)
                {
                    var slots = new List<string>();
                    var previousMap = pair.Key.ownership == NeoValueOwnership.Session
                        ? sessionChangeListeners.GetValueOrDefault((pair.Key.scope, pair.Key.rootId))
                        : PersistedData.changeListeners?.GetValueOrDefault(pair.Key.rootId);
                    foreach (var edit in plan.ListenerEntries)
                    {
                        if (edit.Key.rootId != pair.Key.rootId || edit.Key.scope != pair.Key.scope || edit.Key.ownership != pair.Key.ownership)
                            continue;
                        var previous = previousMap?.GetValueOrDefault(edit.Key.ownerId);
                        var names = previous is null ? new HashSet<string>() : new HashSet<string>(previous.Keys);
                        if (edit.Value is not null)
                            names.UnionWith(edit.Value.Keys);
                        foreach (string memberId in names)
                        {
                            var before = previous?.GetValueOrDefault(memberId);
                            var after = edit.Value?.GetValueOrDefault(memberId);
                            if (NeoChangeListenerPatches.TargetsEqual(before, after))
                                continue;
                            slots.Add($"{ListenerDiagnosticId(edit.Key.ownerId)}.{ListenerDiagnosticId(memberId)}");
                            if (slots.Count == ListenerDiagnosticSlotLimit)
                                break;
                        }
                        if (slots.Count == ListenerDiagnosticSlotLimit)
                            break;
                    }
                    throw new NSGetterRuntimeError($"Listener binding root '{ListenerDiagnosticId(root.id)}' requires {bytes} bytes and exceeds the {NeoChangeListenerPatches.MaxRecordBytes}-byte record limit. Affected slots: {string.Join(", ", slots)}.");
                }
            }
            return maps;
        }

        private static string ListenerDiagnosticId(string id) => id.Length <= ListenerDiagnosticIdLength ? id : id.Substring(0, ListenerDiagnosticIdLength - 3) + "...";

        private void CommitListenerEntries(NeoWritePlan plan,
            Dictionary<(NeoValueOwnership ownership, NeoValueOwnership scope, string rootId), NeoChangeListenerMap>? prepared)
        {
            if (prepared is null)
                return;
            foreach (var pair in prepared)
            {
                if (pair.Key.ownership == NeoValueOwnership.Session)
                {
                    if (pair.Value.Count == 0)
                        sessionChangeListeners.Remove((pair.Key.scope, pair.Key.rootId));
                    else
                        sessionChangeListeners[(pair.Key.scope, pair.Key.rootId)] = pair.Value;
                    continue;
                }
                if (pair.Value.Count == 0)
                {
                    PersistedData.changeListeners?.Remove(pair.Key.rootId);
                    if (PersistedData.changeListeners?.Count == 0)
                        PersistedData.changeListeners = null;
                }
                else
                {
                    PersistedData.changeListeners ??= new Dictionary<string, NeoChangeListenerMap>(StringComparer.Ordinal);
                    PersistedData.changeListeners[pair.Key.rootId] = pair.Value;
                    PersistedData.requiredSaveFormatRevision = NeoSaveFormat.Combine(PersistedData.requiredSaveFormatRevision, NeoSaveFormat.ListenerRevision);
                }
                TouchWritableStoreUpdatedAt(pair.Key.ownership);
            }
            foreach (var change in plan.ListenerEntries!)
                RefreshListenerOwnerRegistration(change.Key.scope, change.Key.rootId, change.Key.ownerId);
        }
    }
}
