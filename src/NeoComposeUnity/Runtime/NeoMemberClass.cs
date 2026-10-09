// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a Class-valued member. Children are keyed by
    /// the schema field name (from <see cref="NeoSchemaClass.schema"/>);
    /// each value is a <see cref="NeoMember"/> for that schema's
    /// underlying member id, bound to the value referenced from the
    /// parent record's value-map entry.
    /// </summary>
    public class NeoMemberClass
        : NeoMember<ClassMember, ObjectMemberValue>,
          IEnumerable<KeyValuePair<string, NeoMember>>
    {
        protected NeoSchemaClass schemaClass;

        /// <summary>The effective class, including the member type when its row omits classId.</summary>
        public string ClassId => schemaClass.id;
        /// <summary>
        /// Inheritance chain (child-first) for the row's effective
        /// class. Empty when the chain is cyclic — see
        /// <see cref="ResolveClassContext"/>.
        /// </summary>
        public IList<NeoSchemaClass> inheritanceChain { get; private set; } = System.Array.Empty<NeoSchemaClass>();
        /// <summary>
        /// Schema entries merged across <see cref="inheritanceChain"/>
        /// (base-first; child overrides win at the same key). Replaces
        /// direct <c>schemaClass.schema</c> access so descendants see fields
        /// inherited from ancestor Classes.
        /// </summary>
        public IList<MergedSchemaEntry> mergedSchema { get; private set; } = System.Array.Empty<MergedSchemaEntry>();
        /// <summary>
        /// Generic binding environment of the row's effective class
        /// (specs/class-generics.md §9): every param in the chain's
        /// scope resolved to its terminal binding at this class, overlaid
        /// with the slot member's constructed <c>classArguments</c> and the
        /// value row's immutable <c>genericBindings</c> stamp
        /// (§4.1) so instances of the declared open class resolve the params
        /// the slot binds at the usage site. Child
        /// member records substitute through this before node dispatch,
        /// and freshly-minted collection rows stamp their
        /// <c>genericBindings</c> from it. Empty for non-generic chains.
        /// </summary>
        internal IReadOnlyDictionary<string, NeoGenericEnvEntry> GenericEnv
        {
            get; private set;
        }
            = NeoGenericResolution.EmptyEnv;
        // Construction replaces this with the built children; until then every
        // node shares one empty map, which nothing writes.
        private static readonly Dictionary<string, NeoMember> NoChildren = new();
        protected Dictionary<string, NeoMember> childMembers = NoChildren;
        // The generated view the client's registry last gave this node, and
        // the registry generation it is current for.
        internal NeoGeneratedClassValue? keptGeneratedValue;
        internal int keptGeneration = -1;
        private NeoClassNode? classNode;
        private List<string>? reboundKeys;
        // Whether a function or getter child is still unbound.
        private bool callablesDeferred;
        // Whether this node belongs to the candidate replay it was built in.
        private readonly bool candidateNode;
        private string? reportingKey;

        public NeoMemberClass(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership)
        {
            candidateNode = client.BuildsCandidateNodes;
            schemaClass = ResolveSchemaClass();
            ResolveClassContext();
            // Schema-driven init runs after `schemaClass` + merged schema are
            // wired so child member lookups via the merged schema
            // resolve correctly; the base ctor's value-driven
            // Initialize ran without walking children because the
            // schema was empty then. We re-walk now.
            ReinitializeChildren();
        }

        public NeoMemberClass(NeoClient client, ClassMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership)
        {
            candidateNode = client.BuildsCandidateNodes;
            schemaClass = ResolveSchemaClass();
            ResolveClassContext();
            ReinitializeChildren();
        }

        /// <summary>
        /// Hook for child instantiation — returns the read-only kind for
        /// Inherit children. <see cref="NeoMemberClassWritable"/>
        /// overrides this to return Writable kinds so descendants of a
        /// writeable Class are also writeable. An explicit declared
        /// storage on the child (specs/member-storage.md §2.1) overrides
        /// the family default in both directions: a Save, Session, or
        /// Writable child is writable even under a read-only parent. Sets
        /// <see cref="NeoMember.parent"/> on the constructed child so
        /// consumers (e.g., <see cref="NeoMemberNSProperty.Compute"/>)
        /// can walk up.
        /// </summary>
        protected virtual NeoMember CreateChild(
            NeoClient client,
            Member childMember,
            string? overrideValueId)
        {
            return CreateOwnedChild(client, childMember, overrideValueId, writableFamily: false);
        }

        public NeoMember this[string key]
        {
            get => Get<NeoMember>(key);
        }

        public TNeoMember Get<TNeoMember>(string key)
            where TNeoMember : NeoMember
        {
            // Not through TryGet: a shared generic call and its out parameter
            // cost more than the lookup on every generated accessor read.
            if (FindChild(key) is TNeoMember member)
                return member;
            throw new System.Collections.Generic.KeyNotFoundException(
                $"No child {nameof(NeoMember)} for {nameof(key)} '{key}' on {nameof(NeoMemberClass)} {this.member.id}");
        }

        public bool TryGet<TNeoMember>(string key, [NotNullWhen(true)] out TNeoMember? outMember)
            where TNeoMember : NeoMember
        {
            if (FindChild(key) is TNeoMember match)
            {
                outMember = match;
                return true;
            }
            outMember = null;
            return false;
        }

        // Generated accessors look children up by the same literal keys on
        // every read, so the children found last answer by reference before
        // the dictionary hashes the key. Slots hold while childMembers is the
        // dictionary they were found in and nothing removed from it.
        private struct ChildSlot
        {
            public string key;
            public NeoMember child;
        }

        private ChildSlot[]? childSlots;
        private int childSlotCount;
        private Dictionary<string, NeoMember>? childSlotsSource;
        private const int MaxChildSlots = 8;

        private protected void ForgetChildSlots() => childSlotCount = 0;

        private protected NeoMember? FindChild(string key)
        {
            if (ReferenceEquals(childSlotsSource, childMembers))
            {
                for (int i = 0; i < childSlotCount; i++)
                {
                    if (ReferenceEquals(childSlots![i].key, key))
                        return childSlots[i].child;
                }
                // A key built at runtime matches by value.
                for (int i = 0; i < childSlotCount; i++)
                {
                    if (childSlots![i].key == key)
                        return childSlots[i].child;
                }
            }
            else
            {
                childSlotCount = 0;
                childSlotsSource = childMembers;
            }
            if (!childMembers.TryGetValue(key, out NeoMember? child))
            {
                if (!callablesDeferred)
                    return null;
                child = BindCallable(key);
                if (child is null)
                    return null;
            }
            // Bounded: further children take the dictionary.
            if (childSlotCount < MaxChildSlots)
            {
                childSlots ??= new ChildSlot[MaxChildSlots];
                childSlots[childSlotCount++] = new ChildSlot { key = key, child = child };
            }
            return child;
        }

        /// <summary>
        /// Writable view over the same record (same member / value id)
        /// in the requested inherited ownership context. Generated values
        /// use this to let inherited child members resolve storage from the
        /// concrete owner while explicit child storage stamps still win.
        /// </summary>
        internal NeoMemberClassWritable AsWritableView(
            NeoValueOwnership? inheritedOwnership = null)
        {
            NeoValueOwnership viewOwnership = inheritedOwnership ?? ownership;
            if (this is NeoMemberClassWritable writable
                && writable.ownership == viewOwnership)
            {
                return writable;
            }
            var view = new NeoMemberClassWritable(client, member, overrideValueId, viewOwnership)
            {
                parent = parent,
            };
            return view;
        }

        /// <summary>The children bound so far, without binding a deferred function or getter.</summary>
        internal Dictionary<string, NeoMember>.ValueCollection BoundChildren => childMembers.Values;

        /// <summary>The computed member child whose getter is <paramref name="memberId"/>, if this node holds one.</summary>
        internal NeoMemberNSProperty? FindGetterChild(string memberId)
        {
            BindCallables();
            foreach (var pair in childMembers)
                if (pair.Value is NeoMemberNSProperty getter && getter.member.id == memberId)
                    return getter;
            return null;
        }

        internal bool TryGetSchemaKeyForChild(
            NeoMember child,
            [NotNullWhen(true)] out string? schemaKey)
        {
            foreach (var pair in childMembers)
            {
                if (ReferenceEquals(pair.Value, child))
                {
                    schemaKey = pair.Key;
                    return true;
                }
            }
            schemaKey = null;
            return false;
        }

        protected TValue? GetValueData<TValue>(string key) where TValue : MemberValue
        {
            if (!TryGetValueData(key, out TValue? value))
            {
                if (member.Requirement == NeoMemberRequirementKind.Required)
                {
                    throw new System.NullReferenceException(
                        $"{member.Requirement == NeoMemberRequirementKind.Required} is true, but value not found");
                }
                return null;
            }
            return value;
        }

        protected bool TryGetValueData<TValue>(string key, [NotNullWhen(true)] out TValue? outValue)
            where TValue : MemberValue
        {
            if (value?.value is not null && value.value.TryGetValue(key, out string valueIdForKey))
            {
                return client.TryGetValue(valueIdForKey, out outValue);
            }
            outValue = null;
            return false;
        }

        protected TMember GetMember<TMember>(string key)
            where TMember : Member
        {
            if (!TryGetMember(key, out TMember? childMember))
            {
                throw new System.NullReferenceException(
                    $"member for {nameof(key)} '{key}' not found");
            }
            return childMember;
        }

        protected bool TryGetMember<TMember>(string key, [NotNullWhen(true)] out TMember? outMember)
            where TMember : Member
        {
            // Walks the merged schema rather than `schemaClass.schema` directly
            // so a descendant Class row sees keys inherited from
            // ancestor classes in its `extendsClassId` chain. Generic slots
            // substitute to their binding member before the kind check,
            // so callers asking for the concrete kind resolve correctly.
            string? memberIdForKey = LookupMergedMemberId(key);
            if (memberIdForKey is not null
                && client.TryGetMember(memberIdForKey, out Member? raw)
                && SubstituteChildMember(raw) is TMember match)
            {
                outMember = match;
                return true;
            }
            outMember = null;
            return false;
        }

        /// <summary>
        /// Substitutes generic references in a merged-schema child record
        /// through this node's <see cref="GenericEnv"/>
        /// (specs/class-generics.md Decision 10) — a <c>T</c> slot on
        /// a closed instance resolves to its binding member BEFORE the
        /// child node kind is dispatched, so it constructs the concrete
        /// wrapper (e.g. <see cref="NeoMemberFloat"/>). Identity for
        /// non-generic records.
        /// </summary>
        protected Member SubstituteChildMember(Member childMember)
        {
            return NeoGenericResolution.SubstituteMember(client, childMember, GenericEnv);
        }

        /// <summary>
        /// Returns the resolved member id for <paramref name="key"/>
        /// according to the merged schema (child overrides win), or null
        /// when the key isn't in any ancestor's schema.
        /// </summary>
        protected string? LookupMergedMemberId(string key) =>
            SurfaceEntry(key)?.memberId;

        private protected MergedSchemaEntry? SurfaceEntry(string key) =>
            classNode?.SurfaceMember(key);

        protected override void Initialize(ObjectMemberValue value)
        {
            base.Initialize(value);
            // Children are walked from ReinitializeChildren — `schemaClass`
            // isn't set yet on the first base-ctor pass.
        }

        protected override void RefreshValueIdChain()
        {
            base.RefreshValueIdChain();
            schemaClass = ResolveSchemaClass();
            ResolveClassContext();
            // The new value's record may carry a different keyset —
            // re-walk so disposed-orphans get released and any new
            // schema-keys get nodes. A write to this row can rebind a field
            // to another row (a NeoScript Class assignment does); P75 replay
            // refreshes before it publishes, so record the rebound fields for
            // the next change notification.
            ReinitializeChildren(recordRebound: true);
        }

        // Field watchers key changes by child, so report each rebound field
        // after this node's own change.
        protected override void OnValueIdChainChanged()
        {
            base.OnValueIdChainChanged();
            if (reboundKeys is not { } rebound)
                return;
            reboundKeys = null;
            foreach (string key in rebound)
            {
                if (key != reportingKey && childMembers.TryGetValue(key, out NeoMember? child))
                    NotifyChanged(child);
            }
        }

        // A setter reports its own key once it has written, so its refresh
        // skips that key until the key is first reported. A watcher's later
        // rebind is a new change. Frames nest: a watcher may write another
        // field.
        private protected string? BeginReporting(string key)
        {
            string? outer = reportingKey;
            reportingKey = key;
            return outer;
        }

        private protected void EndReporting(string key, string? outer)
        {
            reboundKeys?.Remove(key);
            reportingKey = outer;
        }

        public override void Dispose()
        {
            if (!BeginDisposeChildren())
                return;
            DisposeChildren(childMembers);
            childMembers.Clear();
            callablesDeferred = false;
            ForgetChildSlots();
            base.Dispose();
        }

        /// <summary>
        /// Rebinds children after a declared constructor has finished. The
        /// wrapper is created before constructor bodies run so NeoScript can
        /// bind <c>this</c>; a body may then introduce a previously absent
        /// required row, which must become visible through this same wrapper.
        /// </summary>
        internal void RefreshChildrenAfterConstruction() =>
            ReinitializeChildren();

        /// <summary>
        /// Walks <c>value.value</c> and rebuilds the
        /// <see cref="childMembers"/> dict from scratch using the
        /// current <see cref="schemaClass"/>'s schema. Called after the
        /// schema is wired (post-base-ctor), and again whenever a
        /// Writable mutation invalidates the cached children.
        /// </summary>
        protected void ReinitializeChildren(bool recordRebound = false)
        {
            var previousChildren = childMembers;
            childMembers = new(mergedSchema.Count);
            callablesDeferred = false;
            // A Class member explicitly bound to a Null row has no object
            // graph to descend into. Do not confuse it with a missing or
            // malformed Object row, which must retain the existing fail-fast
            // behavior for required data.
            string? resolvedValueId = valueId;
            if (resolvedValueId is not null
                && (client.TryGetOverlaidValue(
                    ownership,
                    resolvedValueId,
                    out NullMemberValue? _)
                    || (client.TryGetWritableValue(ownership, resolvedValueId, out MemberValue? stored)
                        && stored.IsRemoved)))
            {
                DisposeChildren(previousChildren);
                return;
            }
            // An optional Class member without a row, or with a null one,
            // reads null and has no children to bind.
            if (member.Requirement != NeoMemberRequirementKind.Required
                && value?.value is null)
            {
                DisposeChildren(previousChildren);
                return;
            }
            for (int entryIndex = 0; entryIndex < mergedSchema.Count; entryIndex++)
            {
                MergedSchemaEntry entry = mergedSchema[entryIndex];
                if (!HasChild(entry))
                    continue;
                // A function or getter binds when first read, which most
                // trees never do. One already bound keeps its node.
                if (IsCallable(entry.member!) && !previousChildren.ContainsKey(entry.schemaKey))
                {
                    callablesDeferred = true;
                    continue;
                }
                BindChild(entry, previousChildren, resolvedValueId, recordRebound, committed: false);
            }
            DisposeChildren(previousChildren);
        }

        private bool HasChild(MergedSchemaEntry entry) =>
            entry.member is not null
            && (member.Payload != NeoMemberPayloadKind.Partial
                || value?.value is not null && value.value.ContainsKey(entry.schemaKey));

        // A node built outside the active candidate replay outlives it.
        private bool OutlivesCandidate => !candidateNode && client.BuildsCandidateNodes;

        // Functions and getters hold no row of their own.
        private static bool IsCallable(Member member) =>
            member is NSFunctionMember or NSPropertyMember or FunctionMember;

        private NeoMember? BindCallable(string key)
        {
            if (classNode?.SurfaceMember(key) is not { member: { } declared } entry
                || !IsCallable(declared)
                || !HasChild(entry))
                return null;
            BindChild(entry, NoChildren, valueId, recordRebound: false, OutlivesCandidate);
            return childMembers.TryGetValue(key, out NeoMember? child) ? child : null;
        }

        /// <summary>Binds every deferred callable, for readers that walk all children.</summary>
        private void BindCallables()
        {
            if (!callablesDeferred)
                return;
            callablesDeferred = false;
            bool committed = OutlivesCandidate;
            for (int entryIndex = 0; entryIndex < mergedSchema.Count; entryIndex++)
            {
                MergedSchemaEntry entry = mergedSchema[entryIndex];
                if (HasChild(entry) && IsCallable(entry.member!) && !childMembers.ContainsKey(entry.schemaKey))
                    BindChild(entry, NoChildren, valueId, recordRebound: false, committed);
            }
        }

        /// <param name="committed">
        /// Create the child in the committed graph: a node that outlives a
        /// candidate replay binding a deferred callable during it would
        /// otherwise hold a child the candidate disposes.
        /// </param>
        private void BindChild(
            MergedSchemaEntry entry,
            Dictionary<string, NeoMember> previousChildren,
            string? resolvedValueId,
            bool recordRebound,
            bool committed)
        {
            Member childMember = SubstituteChildMember(entry.member!);
            if (member.useDeclarationDefaults)
            {
                // Class references and sparse layer settings inherit declaration
                // defaults. A declaration's editable stored row is not that default.
                var declaration = childMember.ShallowClone();
                declaration.valueId = null;
                declaration.useDeclarationDefaults = true;
                declaration.substitutedDeclarationIdentity =
                    $"__neo_class_default_member:{member.RuntimeDeclarationIdentity}/{childMember.RuntimeDeclarationIdentity}";
                childMember = declaration;
            }
            string? childValueId = null;
            if (value?.value is not null
                && value.value.TryGetValue(entry.schemaKey, out string valueIdForKey))
            {
                childValueId = valueIdForKey;
            }
            else if (overrideValueId is null
                && member.defaultValue?.value is not null
                && member.defaultValue.value.TryGetValue(
                    entry.schemaKey,
                    out string defaultValueIdForKey))
            {
                // A member's own authored row may be sparse even when
                // its declaration carries a composite default. Missing
                // keys inherit that default child row; authored row
                // keys still win above. Externally-bound instance rows
                // keep absence meaningful (for example an omitted
                // optional tile-grid assetValueId). Partial Class
                // members never reach this branch for missing keys.
                childValueId = defaultValueIdForKey;
            }
            else if (resolvedValueId is not null
                && client.TryGetVirtualClassChildValueId(
                    resolvedValueId,
                    entry.schemaKey,
                    out string? virtualChildValueId))
            {
                childValueId = virtualChildValueId;
            }
            else if (client.ConstantDeclarationValueId(childMember) is string declaredValueId)
            {
                // Instances share a constant member's declaration value.
                childValueId = declaredValueId;
            }
            if (previousChildren.TryGetValue(entry.schemaKey, out NeoMember? existing)
                // A P75 rebuild mints new rows at the SAME deterministic
                // virtual ids and disposes the wrappers holding the old
                // ones. Matching ids therefore no longer implies the
                // wrapper is still usable — a disposed one would serve the
                // previous expansion for the rest of its life.
                && !existing.isDisposed
                && existing.member.id == childMember.id
                && (existing.overrideValueId == childValueId
                    || existing.value?.id == childValueId))
            {
                childMembers[entry.schemaKey] = existing;
                previousChildren.Remove(entry.schemaKey);
                return;
            }
            // Only replay evaluates a computed child that a row omits. A
            // sparse root's wrapper tree exists before its replay, and the
            // child binds when replay refreshes that tree, including through
            // intermediate Class children. When replay could not supply the
            // child, the loader reported its row; the child stays unbound
            // instead of failing the whole tree.
            if (childValueId is null
                && MemberValueFactory.InitializerOf(childMember) is not null
                && (client.IsAwaitingVirtualInstanceInitializers(value)
                    || childMember.valueId is null))
            {
                return;
            }
            NeoMember child;
            using (client.EnterVirtualInstanceChildConstruction(value))
            {
                child = committed
                    ? client.CreateCommittedNode(() => CreateChild(client, childMember, childValueId))
                    : CreateChild(client, childMember, childValueId);
            }
            child.Hold(this);
            childMembers[entry.schemaKey] = child;
            if (recordRebound
                && (child.overrideValueId ?? child.value?.id)
                    != (previousChildren.TryGetValue(entry.schemaKey, out NeoMember? replaced)
                        ? replaced.overrideValueId ?? replaced.value?.id
                        : null))
            {
                (reboundKeys ??= new List<string>()).Add(entry.schemaKey);
            }
        }

        // Over the pairs: a dictionary's Values view is an allocation of its own.
        private void DisposeChildren(Dictionary<string, NeoMember> children)
        {
            foreach (var pair in children)
                pair.Value.Release(this);
        }

        protected internal override void HandleChildChanged(NeoMember child)
        {
            if (reportingKey is not null
                && ReferenceEquals(FindChild(reportingKey), child))
            {
                reportingKey = null;
            }
            NotifyChanged(child);
        }

        /// <summary>
        /// Whether <paramref name="before"/>, <paramref name="key"/>'s child
        /// before a write to its row, heard that write and bubbled it: it is
        /// still the key's live child. A Class child retires on a tombstone
        /// instead, and a rebuilt key has a new child.
        /// </summary>
        private protected bool ChildBubbledOwnChange(string key, NeoMember? before) =>
            before is { isDisposed: false }
            && ReferenceEquals(FindChild(key), before);

        protected void NotifyChildChanged(string key)
        {
            if (key == reportingKey)
                reportingKey = null;
            if (FindChild(key) is { } child)
            {
                NotifyChanged(child);
                return;
            }
            NotifyChanged();
        }

        public IEnumerator<KeyValuePair<string, NeoMember>> GetEnumerator()
        {
            BindCallables();
            return childMembers.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private NeoSchemaClass ResolveSchemaClass()
        {
            string classId = member.classId;
            if (!string.IsNullOrEmpty(value?.classId))
            {
                classId = value!.classId!;
            }
            else if (!string.IsNullOrEmpty(member.defaultValue?.classId))
            {
                classId = member.defaultValue!.classId!;
            }

            if (!client.TryGetClass(classId, out NeoSchemaClass? match))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(member.classId),
                    $"No class for {nameof(member)}.{nameof(member.classId)} {classId}");
            }
            return match;
        }

        /// <summary>
        /// Walks the <c>extendsClassId</c> chain from <see cref="schemaClass"/>
        /// upward and computes the merged schema. Cycles are caught and
        /// degrade to an empty chain / schema (matching the TS-side
        /// ClassValueNodeVM behavior — UI shows no fields rather than
        /// throwing an unrecoverable error). Computed once at
        /// construction; the wire DTOs are read-mostly so we don't
        /// invalidate on class-graph changes.
        /// </summary>
        private void ResolveClassContext()
        {
            try
            {
                classNode = client.ResolveClassNode(schemaClass.id);
                inheritanceChain = classNode.Chain;
                mergedSchema = classNode.Surface;
                // The chain env alone misses constructed slots: an instance
                // of the DECLARED open class (`classId: null` rows under a
                // `GenericTest<Color>` slot) binds its params through the
                // slot member's `classArguments`, not a named
                // subclass's chain (specs/class-generics.md §4.1).
                // The value stamp is the durable environment the instance was
                // authored under, so it wins over a stale or forwarding usage-
                // site argument. Concrete documents pay nothing: both overlays
                // are skipped when they carry no arguments.
                IReadOnlyDictionary<string, GenericBinding>? classArguments =
                    NeoGenericResolution.CloseClassArgumentsFromStamp(
                        value?.genericBindings,
                        member.classArguments);
                GenericEnv = NeoGenericResolution.ResolveInstanceEnv(
                    inheritanceChain,
                    classArguments);
            }
            catch (CircularInheritanceError ex)
            {
                Debug.LogError(ex);
                classNode = null;
                inheritanceChain = System.Array.Empty<NeoSchemaClass>();
                mergedSchema = System.Array.Empty<MergedSchemaEntry>();
                GenericEnv = NeoGenericResolution.EmptyEnv;
            }
        }
    }

    /// <summary>
    /// Writeable variant of <see cref="NeoMemberClass"/>. All
    /// descendants are also Saved (the
    /// <see cref="CreateChild"/> override returns
    /// <see cref="NeoMember.CreateWritable"/> kinds).
    /// </summary>
    public class NeoMemberClassWritable : NeoMemberClass
    {
        public NeoMemberClassWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberClassWritable(NeoClient client, ClassMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        /// <summary>
        /// The row id bound for <paramref name="key"/>. A P75 sparse root
        /// omits every member still sitting at its construction value, so a
        /// key the body lacks resolves at its deterministic virtual id —
        /// exactly where a write or tombstone has to land for the web to
        /// read the same value.
        /// </summary>
        internal string? ChildValueId(string key)
        {
            if (value?.value?.TryGetValue(key, out string? childValueId) == true)
            {
                return childValueId;
            }
            return valueId is string parentValueId
                && client.TryGetVirtualClassChildValueId(parentValueId, key, out string? virtualChildValueId)
                ? virtualChildValueId
                : null;
        }

        protected override NeoMember CreateChild(
            NeoClient client,
            Member childMember,
            string? overrideValueId)
        {
            return CreateOwnedChild(client, childMember, overrideValueId, writableFamily: true);
        }

        public TNeoMember GetOrCreateCollection<TNeoMember>(string key)
            where TNeoMember : NeoMember
        {
            // A child that already resolves a value (authored default or a
            // prior write) is returned as-is — clone-on-write happens lazily
            // on its first mutation. A child with no resolved value (optional
            // key absent from the record, no authored default) is bound to an
            // empty collection now, so the returned node tracks its array/map
            // by a stable id instead of mint-binding mid-mutation.
            if (TryGet(key, out TNeoMember? existing) && existing.value is not null)
            {
                existing.parent = this;
                return existing;
            }

            string? schemaKeyedMemberId = LookupMergedMemberId(key);
            if (schemaKeyedMemberId is null)
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Merged schema for class {schemaClass.id} (chain depth {inheritanceChain.Count}) does not contain key '{key}'");
            }
            if (!client.TryGetMember(schemaKeyedMemberId, out Member? childMember))
            {
                throw new System.Exception(
                    $"No member for {nameof(schemaKeyedMemberId)} '{schemaKeyedMemberId}'");
            }
            childMember = SubstituteChildMember(childMember);

            NeoValueWritePayload initialValue = childMember switch
            {
                ListMember => NeoValueWritePayload.FromValue(System.Array.Empty<string>()),
                DictionaryMember => NeoValueWritePayload.FromValue(new Dictionary<string, string>()),
                _ => throw new System.InvalidOperationException(
                    $"Member '{key}' is not a collection member."),
            };
            SetSerializedValue(key, initialValue);
            var created = Get<TNeoMember>(key);
            created.parent = this;
            return created;
        }

        public NeoMemberLookupWritable GetOrCreateLookup(string key)
        {
            if (TryGet(key, out NeoMemberLookupWritable? existing) && existing.value is not null)
            {
                existing.parent = this;
                return existing;
            }

            string? schemaKeyedMemberId = LookupMergedMemberId(key);
            if (schemaKeyedMemberId is null)
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Merged schema for class {schemaClass.id} (chain depth {inheritanceChain.Count}) does not contain key '{key}'");
            }
            if (!client.TryGetMember(schemaKeyedMemberId, out Member? childMember))
            {
                throw new System.Exception(
                    $"No member for {nameof(schemaKeyedMemberId)} '{schemaKeyedMemberId}'");
            }
            if (childMember is not LookupMember)
            {
                throw new System.InvalidOperationException(
                    $"Member '{key}' is not a lookup member.");
            }

            SetSerializedValue(key, NeoValueWritePayload.FromValue(System.Array.Empty<string>()));
            var created = Get<NeoMemberLookupWritable>(key);
            created.parent = this;
            return created;
        }

        public NeoMemberDialogueLookupWritable GetOrCreateDialogueLookup(string key)
        {
            if (TryGet(key, out NeoMemberDialogueLookupWritable? existing) && existing.value is not null)
            {
                existing.parent = this;
                return existing;
            }

            string? schemaKeyedMemberId = LookupMergedMemberId(key);
            if (schemaKeyedMemberId is null)
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Merged schema for class {schemaClass.id} (chain depth {inheritanceChain.Count}) does not contain key '{key}'");
            }
            if (!client.TryGetMember(schemaKeyedMemberId, out Member? childMember))
            {
                throw new System.Exception(
                    $"No member for {nameof(schemaKeyedMemberId)} '{schemaKeyedMemberId}'");
            }
            if (childMember is not DialogueLookupMember)
            {
                throw new System.InvalidOperationException(
                    $"Member '{key}' is not a dialogue lookup member.");
            }

            SetSerializedValue(key, NeoValueWritePayload.FromValue(System.Array.Empty<string>()));
            var created = Get<NeoMemberDialogueLookupWritable>(key);
            created.parent = this;
            return created;
        }

        public NeoMemberStringWritable GetOrCreateString(
            string key,
            string? initialValue = null)
        {
            if (TryGet(key, out NeoMemberStringWritable? existing) && existing.value is not null)
            {
                return existing;
            }

            string? schemaKeyedMemberId = LookupMergedMemberId(key);
            if (schemaKeyedMemberId is null)
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Merged schema for class {schemaClass.id} (chain depth {inheritanceChain.Count}) does not contain key '{key}'");
            }
            if (!client.TryGetMember(schemaKeyedMemberId, out Member? childMember))
            {
                throw new System.Exception(
                    $"No member for {nameof(schemaKeyedMemberId)} '{schemaKeyedMemberId}'");
            }
            if (SubstituteChildMember(childMember) is not StringMember)
            {
                throw new System.InvalidOperationException(
                    $"Member '{key}' is not a string member.");
            }

            SetSerializedValue(key, NeoValueWritePayload.FromValue(initialValue));
            return Get<NeoMemberStringWritable>(key);
        }

        public void SetStringLiteral(string key, string? value)
        {
            string? schemaKeyedMemberId = LookupMergedMemberId(key);
            if (schemaKeyedMemberId is null)
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Merged schema for class {schemaClass.id} (chain depth {inheritanceChain.Count}) does not contain key '{key}'");
            }
            if (!client.TryGetMember(schemaKeyedMemberId, out Member? childMember))
            {
                throw new System.Exception(
                    $"No member for {nameof(schemaKeyedMemberId)} '{schemaKeyedMemberId}'");
            }
            if (SubstituteChildMember(childMember) is not StringMember)
            {
                throw new System.InvalidOperationException(
                    $"Member '{key}' is not a string member.");
            }

            SetSerializedValue(key, NeoValueWritePayload.FromValue(value));
        }

        /// <summary>
        /// Sets the schema-keyed child to <paramref name="setValue"/>.
        /// Reuses the existing entry's stable id when one is bound
        /// (clone-on-writing the record + entry rows so they shadow the
        /// authored defaults); otherwise mints a fresh value row and links
        /// it into the record's (clone-on-write) value-map under
        /// <paramref name="key"/>.
        /// </summary>
        private void NotifyPendingLeaf(NeoWritePlan plan, string key, NeoMember? existingChild)
        {
            plan.AfterNotifications(() =>
            {
                if (isDisposed)
                    return;
                if (existingChild is null || existingChild.isDisposed)
                    ReinitializeChildren();
                if (!ChildBubbledOwnChange(key, existingChild))
                    NotifyChildChanged(key);
            });
        }

        internal void SetSerializedValue(string key, NeoValueWritePayload? setValue) =>
            SetSerializedValue(key, setValue, placement: false);

        /// <summary>
        /// Writes a member that carries a grid placement invariant: an object's
        /// Position or a tile's Cell. Generated setters and NeoScript route
        /// those members here so the grid indexes learn about the move;
        /// <see cref="SetSerializedValue(string, NeoValueWritePayload?)"/>
        /// stores a scalar without consulting the grid. Any other member
        /// written through here is an ordinary write.
        /// </summary>
        public void SetPlacementValue(string key, NeoValueWritePayload? setValue) =>
            SetSerializedValue(key, setValue, placement: true);

        private void SetSerializedValue(string key, NeoValueWritePayload? setValue, bool placement)
        {
            string? outer = BeginReporting(key);
            try
            {
                WriteSerializedValue(key, setValue, placement);
            }
            finally { EndReporting(key, outer); }
        }

        private void WriteSerializedValue(string key, NeoValueWritePayload? setValue, bool placement)
        {
            AssertContainingClassesCanBeConstructed();
            NeoTimestamp nowIso = NeoTimestamp.Now();

            // Resolution flows through the merged schema (inheritance
            // chain), so a Set against a key inherited from an ancestor
            // class still resolves the right child member.
            MergedSchemaEntry? entry = SurfaceEntry(key);
            if (entry is null)
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Merged schema for class {schemaClass.id} (chain depth {inheritanceChain.Count}) does not contain key '{key}'");
            }
            // The class node resolved the entry's authored member; only a
            // variant target member needs the client's lookup.
            Member? childMember = entry.member;
            if (childMember is null && !client.TryGetMember(entry.memberId, out childMember))
            {
                throw new System.Exception(
                    $"No member for schemaKeyedMemberId '{entry.memberId}'");
            }
            // Generic slots substitute to their binding before any typed
            // dispatch below (required travels with the binding —
            // specs/class-generics.md Decision 10).
            childMember = SubstituteChildMember(childMember);
            RejectReadOnlyInstanceMutation(key, childMember);
            if (childMember.Requirement == NeoMemberRequirementKind.Required && (setValue is null || setValue.isNull))
            {
                throw new System.ArgumentNullException(
                    nameof(setValue),
                    $"Cannot be null when child member '{key}' is required");
            }

            // Per-placement storage (specs/member-storage.md): a declared
            // storage stamp on the child pins which writable store the leaf
            // shadows into, independent of this record's own ownership — the
            // headline case being a Save-stamped field on a static record.
            NeoValueOwnership childOwnership =
                client.ChildOwnership(childMember, ownership);
            if (childOwnership == NeoValueOwnership.Asset)
            {
                throw new System.InvalidOperationException(
                    $"Cannot write '{key}' on Class '{member.id}': its effective storage is immutable.");
            }
            bool recordWritable = ownership != NeoValueOwnership.Asset;
            // The plan is built where a write is certain: a scalar write that
            // repeats the stored value returns before it exists.
            NeoWritePlan plan;

            // Unordered lists never store membership in the array: a
            // whole-list assignment translates to Clear + Add-each, and
            // assigning null clears the members then sets the discriminator
            // to null (spec §1.6/§3.8).
            if (childMember is ListMember childListMember
                && client.IsUnorderedList(childListMember))
            {
                plan = new NeoWritePlan(client);
                SetSerializedUnorderedList(plan, key, setValue, recordWritable);
                return;
            }

            string? existingValueId = ChildValueId(key);
            // One node answers every read of the entry's row, and the leaf
            // write. The entry's live child already holds it.
            NeoMember? existingChild = existingValueId is null ? null : FindChild(key);
            NeoValueNode? existingNode = existingChild?.HeldValueNode(existingValueId!);
            if (existingValueId is not null
                && client.ReadValue(childOwnership, existingValueId, ref existingNode) is { } existing)
            {
                if (setValue?.isValueReference == true)
                {
                    if (!recordWritable)
                    {
                        plan = new NeoWritePlan(client);
                        NeoShadowImport shadowed = client.StageShadowImport(
                            plan,
                            childOwnership,
                            setValue.valueId!,
                            existing,
                            childMember,
                            out _);
                        if (shadowed == NeoShadowImport.Moved)
                            RetargetMovedReferenceAfterCommit(plan, setValue, childMember, existingValueId, childOwnership);
                        plan.Commit();
                        if (shadowed == NeoShadowImport.Unchanged)
                            return;
                        ReinitializeChildren();
                        NotifyChildChanged(key);
                        return;
                    }
                    plan = new NeoWritePlan(client);
                    string importedValueId = client.ImportValueReference(
                        plan,
                        childOwnership,
                        setValue.valueId!,
                        out bool sourceMoved,
                        existingValueId);
                    if (sourceMoved)
                        RetargetMovedReferenceAfterCommit(plan, setValue, childMember, importedValueId, childOwnership);
                    if (importedValueId == existingValueId)
                    {
                        plan.Commit();
                        return;
                    }
                    ObjectMemberValue record = EnsureWritableObject(plan, nowIso);
                    record.value![key] = importedValueId;
                    record.updatedAt = nowIso;
                    plan.Set(ownership, record);
                    client.StageUnlinkedRemovals(plan, childOwnership, existingValueId, childMember);
                    plan.Commit();
                    value = record;
                    ReinitializeChildren();
                    NotifyChildChanged(key);
                    return;
                }
                if (client.ReadWritableValue(childOwnership, existingValueId, ref existingNode) is { } stored
                    && MemberValueFactory.MatchesLeaf(childMember, setValue?.value, stored))
                    return;
                // Reuse the entry's stable id: a fresh row at the same id
                // shadows the authored default in the child's writable store.
                MemberValue next = MemberValueFactory.Create(
                    childMember,
                    setValue?.value,
                    existingValueId,
                    existing.createdAt,
                    nowIso);
                // A shadow of a stamped collection row keeps the immutable
                // stamp (spec Decision 9/16); a row that predates the stamp
                // recomputes the identical value from this record's env.
                next.mapKey = existing.mapKey;
                next.genericBindings = existing.genericBindings;
                NeoGenericResolution.StampGenericBindings(client, childMember, next, GenericEnv);
                // A leaf replacement with no payload rows stores in place or
                // joins a held script plan. Only committed writes notify.
                NeoWritePlan? pendingLeaf = null;
                bool storedLeaf = false;
                if (setValue?.value is not NeoValuePayload { valueRows: { Count: > 0 } })
                {
                    if (placement)
                        storedLeaf = value is not null && client.TryWritePlacement(childOwnership, value, key, next, childMember);
                    else if (client.TracksLeafWrites)
                        storedLeaf = client.TryWriteLeafTracked(childOwnership, next, childMember, "value", out pendingLeaf, existingNode);
                    else
                        storedLeaf = client.TryWriteLeaf(childOwnership, next, childMember, "value", existingNode);
                }
                if (storedLeaf)
                {
                    if (pendingLeaf is not null)
                    {
                        NotifyPendingLeaf(pendingLeaf, key, existingChild);
                        return;
                    }
                    if (existingChild is null || existingChild.isDisposed)
                        ReinitializeChildren();
                    if (!ChildBubbledOwnChange(key, existingChild))
                        NotifyChildChanged(key);
                    return;
                }
                plan = new NeoWritePlan(client);
                client.StageWritablePayloadRows(plan, childOwnership, setValue?.value);
                client.StageInPlaceReplacement(
                    plan,
                    childOwnership,
                    next,
                    childMember,
                    childMember is ClassMember or ListMember or DictionaryMember
                        ? null
                        : "value");
                plan.Commit();
                // A scalar replacement keeps the same field binding and its
                // node receives the value change directly. Rebuild only for a
                // missing node or a potentially changed container shape.
                if (existingChild is null || existingChild.isDisposed
                    || childMember is ClassMember or ListMember or DictionaryMember)
                    ReinitializeChildren();
                if (!ChildBubbledOwnChange(key, existingChild))
                {
                    NotifyChildChanged(key);
                }
                return;
            }

            // No existing entry for this key — mint a fresh value row and
            // link it under the record's value-map. A static record cannot
            // gain keys at runtime: linking mutates the record itself.
            if (!recordWritable)
            {
                throw new System.InvalidOperationException(
                    $"Cannot write '{key}' on static Class '{member.id}': the stamped leaf has no authored value to shadow, and a static record cannot gain new keys at runtime. Author a value for '{key}' in the web editor.");
            }
            plan = new NeoWritePlan(client);
            string newValueId;
            if (setValue?.isValueReference == true)
            {
                newValueId = client.ImportValueReference(
                        plan,
                    childOwnership,
                    setValue.valueId!,
                    out bool sourceMoved);
                if (sourceMoved)
                {
                    RetargetMovedReferenceAfterCommit(plan, setValue, childMember, newValueId, childOwnership);
                }
            }
            else
            {
                newValueId = System.Guid.NewGuid().ToString();
                MemberValue newValueRow = MemberValueFactory.Create(
                    childMember, setValue?.value, newValueId, nowIso, nowIso);
                newValueRow.mapKey = client.ResolveCreatedValueMapKey(
                    childMember,
                    value?.mapKey,
                    value?.classId);
                // Freshly-minted collection rows carry the Decision-9 stamp
                // computed from this record's env (the SDK walks top-down
                // with the document in memory).
                NeoGenericResolution.StampGenericBindings(client, childMember, newValueRow, GenericEnv);
                client.StageWritablePayloadRows(plan, childOwnership, setValue?.value);
                plan.Set(childOwnership, newValueRow);
            }

            ObjectMemberValue keyedRecord = EnsureWritableObject(plan, nowIso);
            keyedRecord.value![key] = newValueId;
            keyedRecord.updatedAt = nowIso;
            plan.Set(ownership, keyedRecord);
            plan.Commit();
            value = keyedRecord;

            ReinitializeChildren();
            NotifyChildChanged(key);
        }

        // Keep the reference-transfer capture out of WriteSerializedValue so scalar
        // writes do not allocate a closure for a branch they never execute.
        private void RetargetMovedReferenceAfterCommit(
            NeoWritePlan plan, NeoValueWritePayload payload, Member childMember,
            string valueId, NeoValueOwnership childOwnership)
        {
            plan.AfterCommit(() => payload.RetargetMovedReference(client, childMember, valueId, childOwnership));
        }

        private void SetSerializedUnorderedList(
            NeoWritePlan plan, string key, NeoValueWritePayload? setValue, bool recordWritable)
        {
            if (Get<NeoMember>(key) is not NeoMemberListWritable listNode)
                throw new System.InvalidOperationException($"Unordered list '{key}' does not permit writes.");
            if (!recordWritable && listNode.value is null)
                throw new System.InvalidOperationException($"Cannot bind an unordered list on immutable Class '{member.id}'.");
            // The list reports its own Replace, which bubbles here.
            listNode.PrepareAssignSerialized(plan, setValue);
            plan.Commit();
            ReinitializeChildren();
        }

        internal override void BindChildValueId(NeoWritePlan plan, NeoMember child, string childValueId)
        {
            if (!TryGetSchemaKeyForChild(child, out string? key))
            {
                throw new System.InvalidOperationException(
                    $"Cannot bind a child value on Class '{member.id}': child is not a registered schema field.");
            }
            NeoTimestamp nowIso = NeoTimestamp.Now();
            ObjectMemberValue record = EnsureWritableObject(plan, nowIso);
            record.value![key] = childValueId;
            record.updatedAt = nowIso;
            plan.Set(ownership, record);
            plan.AfterCommit(() =>
            {
                value = record;
                ReinitializeChildren();
            });
        }

        internal void AssertUnboundObjectCanBeConstructed()
        {
            if (value?.value is not null)
                return;
            try
            {
                NeoGeneratedTypesSupport.ValidateRuntimeClassConstructorMetadata(
                    client,
                    new ClassTypeInfo { type = MemberKind.Class, classId = schemaClass.id, required = true },
                    System.Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>(),
                    member.classArguments);
            }
            catch (System.InvalidOperationException error)
            {
                throw new System.InvalidOperationException(
                    $"Construct and assign Class '{schemaClass.name}' before writing its members. {error.Message}", error);
            }
        }

        /// <summary>
        /// Returns the record's own object row guaranteed writable (a
        /// clone-on-write shadow at the stable id), minting + binding a
        /// fresh empty record through the parent when nothing is bound yet.
        /// </summary>
        private ObjectMemberValue EnsureWritableObject(NeoTimestamp nowIso)
        {
            var plan = new NeoWritePlan(client);
            var row = EnsureWritableObject(plan, nowIso);
            if (plan.Rows.Count > 0)
                plan.Commit();
            return row;
        }

        private ObjectMemberValue EnsureWritableObject(NeoWritePlan plan, NeoTimestamp nowIso)
        {
            AssertContainingClassesCanBeConstructed();
            var writable = WritableCandidate(plan);
            if (writable is not null)
            {
                writable.value ??= new Dictionary<string, string>();
                return writable;
            }
            // Seed the freshly minted clone-on-write row from the currently-
            // effective record (the authored default's schema-key entries),
            // NOT an empty map — otherwise Remove/Unset on a default-only
            // record would index a key the caller can see but the fresh row
            // lacks (throwing KeyNotFoundException), and overwriting one key
            // would silently drop the sibling default fields.
            ObjectMemberValue record = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = value?.value is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(value.value),
            };
            BindNewValue(plan, record);
            return record;
        }

        /// <summary>
        /// Removes the schema-keyed child under <paramref name="key"/>.
        /// Disposes the child <see cref="NeoMember"/>, drops the
        /// key from the parent record, persists, and cascade-deletes
        /// the orphaned value graph from
        /// <see cref="ProjectSaveData.values"/>.
        /// No-op if the key isn't present.
        /// </summary>
        public void Remove(string key)
        {
            string? memberId = LookupMergedMemberId(key);
            if (memberId is not null
                && client.TryGetMember(memberId, out Member? rawMember))
            {
                RejectReadOnlyInstanceMutation(key, SubstituteChildMember(rawMember));
            }
            if (value?.value is null)
                return;
            if (!value.value.ContainsKey(key))
                return;
            string? schemaKeyedMemberId = LookupMergedMemberId(key);
            Member? removedMember = schemaKeyedMemberId is not null
                && client.TryGetMember(schemaKeyedMemberId, out Member? resolvedMember)
                    ? SubstituteChildMember(resolvedMember)
                    : null;
            NeoTimestamp nowIso = NeoTimestamp.Now();

            // Clone-on-write the record (shadowing the authored default at
            // its stable id) and drop the key.
            var plan = new NeoWritePlan(client);
            ObjectMemberValue record = EnsureWritableObject(plan, nowIso);
            string removedValueId = record.value![key];
            record.value.Remove(key);
            record.updatedAt = nowIso;
            plan.Set(ownership, record);
            NeoValueOwnership removedOwnership =
                client.ChildOwnership(removedMember, ownership);
            client.StageUnlinkedRemovals(plan, removedOwnership, removedValueId, removedMember);
            plan.Commit();
            value = record;

            if (childMembers.TryGetValue(key, out NeoMember? child))
            {
                child.Release(this);
                childMembers.Remove(key);
                ForgetChildSlots();
            }

            NotifyChanged();
        }

        private static void RejectReadOnlyInstanceMutation(
            string key,
            Member childMember)
        {
            if (childMember.Mutability != NeoMemberMutabilityKind.ReadOnly)
                return;
            throw new System.InvalidOperationException(
                $"Cannot write '{key}': readonly member '{childMember.name}' ({childMember.id}) is set only at construction.");
        }

        /// <summary>
        /// Explicitly unsets the optional schema-keyed field under
        /// <paramref name="key"/> by stamping a removal tombstone at the child's
        /// stable value id, so the field resolves as unset rather than the
        /// authored default. Sparse: the record keeps the key and is left
        /// untouched (contrast <see cref="Remove"/>, which drops the key and
        /// reverts to the authored default). No-op when the key is not bound to a
        /// value. Throws if the field is required.
        /// </summary>
        public void Unset(string key)
        {
            string? memberId = LookupMergedMemberId(key);
            if (memberId is null)
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Merged schema for class {schemaClass.id} (chain depth {inheritanceChain.Count}) does not contain key '{key}'");
            }
            // The tombstone lands where a write would: a storage-stamped
            // field on a static record shadows into its own writable store.
            NeoValueOwnership childOwnership = ownership;
            if (client.TryGetMember(memberId, out Member? childMember))
            {
                childMember = SubstituteChildMember(childMember);
                RejectReadOnlyInstanceMutation(key, childMember);
                if (childMember.Requirement == NeoMemberRequirementKind.Required)
                {
                    throw new System.InvalidOperationException(
                        $"Cannot unset required field '{key}'.");
                }
                childOwnership = client.ChildOwnership(childMember, ownership);
            }
            string? childValueId = ChildValueId(key);
            if (childValueId is null)
            {
                return;
            }
            if (childOwnership == NeoValueOwnership.Asset)
            {
                throw new System.InvalidOperationException(
                    $"Cannot unset '{key}' on Class '{member.id}': its effective storage is immutable.");
            }
            childMembers.TryGetValue(key, out NeoMember? existingChild);
            client.WriteRemovalTombstone(childOwnership, childValueId);
            ReinitializeChildren();
            if (!ChildBubbledOwnChange(key, existingChild))
                NotifyChildChanged(key);
        }
    }
}
