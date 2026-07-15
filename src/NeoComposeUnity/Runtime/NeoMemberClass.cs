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
        /// <summary>
        /// Inheritance chain (child-first) for the row's effective
        /// class. Empty when the chain is cyclic — see
        /// <see cref="ResolveClassContext"/>.
        /// </summary>
        public IList<NeoSchemaClass> inheritanceChain { get; private set; } = new List<NeoSchemaClass>();
        /// <summary>
        /// Schema entries merged across <see cref="inheritanceChain"/>
        /// (base-first; child overrides win at the same key). Replaces
        /// direct <c>schemaClass.schema</c> access so descendants see fields
        /// inherited from ancestor Classes.
        /// </summary>
        public IList<MergedSchemaEntry> mergedSchema { get; private set; } = new List<MergedSchemaEntry>();
        /// <summary>
        /// Generic binding environment of the row's effective class
        /// (specs/class-generics.md §9): every param in the chain's
        /// scope resolved to its terminal binding at this class, overlaid
        /// with the slot member's constructed <c>classArguments</c>
        /// (§4.1) so instances of the declared open class resolve the params
        /// the slot binds at the usage site. Child
        /// member records substitute through this before node dispatch,
        /// and freshly-minted collection rows stamp their
        /// <c>genericBindings</c> from it. Empty for non-generic chains.
        /// </summary>
        internal IReadOnlyDictionary<string, NeoGenericEnvEntry> GenericEnv { get; private set; }
            = NeoGenericResolution.EmptyEnv;
        protected Dictionary<string, NeoMember> childMembers = new();

        public NeoMemberClass(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership)
        {
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
        /// the family default in both directions: a Save/Session-stamped
        /// child is writable even under a read-only parent. Sets
        /// <see cref="NeoMember.parent"/> on the constructed child so
        /// consumers (e.g., <see cref="NeoMemberNSProperty.Compute"/>)
        /// can walk up.
        /// </summary>
        protected virtual NeoMember CreateChild(
            NeoClient client,
            Member childMember,
            string? overrideValueId)
        {
            NeoValueOwnership? declared = client.DeclaredOwnership(childMember);
            NeoMember child =
                declared == NeoValueOwnership.Save || declared == NeoValueOwnership.Session
                    ? CreateWritable(client, childMember, overrideValueId, declared.Value)
                    : Create(client, childMember, overrideValueId);
            child.parent = this;
            return child;
        }

        public NeoMember this[string key]
        {
            get => Get<NeoMember>(key);
        }

        public TNeoMember Get<TNeoMember>(string key)
            where TNeoMember : NeoMember
        {
            if (!TryGet(key, out TNeoMember? member))
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"No child {nameof(NeoMember)} for {nameof(key)} '{key}' on {nameof(NeoMemberClass)} {this.member.id}");
            }
            return member;
        }

        public bool TryGet<TNeoMember>(string key, [NotNullWhen(true)] out TNeoMember? outMember)
            where TNeoMember : NeoMember
        {
            if (childMembers.TryGetValue(key, out NeoMember? check) && check is TNeoMember match)
            {
                outMember = match;
                return true;
            }
            outMember = null;
            return false;
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
                if (member.required)
                {
                    throw new System.NullReferenceException(
                        $"{member.required} is true, but value not found");
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
        protected string? LookupMergedMemberId(string key)
        {
            foreach (var entry in mergedSchema)
            {
                if (entry.schemaKey == key) return entry.memberId;
            }
            return null;
        }

        protected override void Initialize(ObjectMemberValue value)
        {
            base.Initialize(value);
            // Children are walked from ReinitializeChildren — `schemaClass`
            // isn't set yet on the first base-ctor pass.
        }

        protected override void OnValueIdChainChanged()
        {
            base.OnValueIdChainChanged();
            // The new value's record may carry a different keyset —
            // re-walk so disposed-orphans get released and any new
            // schema-keys get nodes.
            ReinitializeChildren();
        }

        public override void Dispose()
        {
            if (isDisposed) return;
            foreach (var child in childMembers.Values)
            {
                child.OnChanged -= HandleChildChanged;
                child.Dispose();
            }
            childMembers.Clear();
            base.Dispose();
        }

        /// <summary>
        /// Walks <c>value.value</c> and rebuilds the
        /// <see cref="childMembers"/> dict from scratch using the
        /// current <see cref="schemaClass"/>'s schema. Called after the
        /// schema is wired (post-base-ctor), and again whenever a
        /// Writable mutation invalidates the cached children.
        /// </summary>
        protected void ReinitializeChildren()
        {
            var previousChildren = childMembers;
            childMembers = new();
            foreach (var entry in mergedSchema)
            {
                if (!client.TryGetMember(entry.memberId, out Member? childMember)) continue;
                childMember = SubstituteChildMember(childMember);
                string? childValueId = value?.value is not null
                    && value.value.TryGetValue(entry.schemaKey, out string valueIdForKey)
                        ? valueIdForKey
                        : null;
                if (previousChildren.TryGetValue(entry.schemaKey, out NeoMember? existing)
                    && existing.member.id == childMember.id
                    && (existing.overrideValueId == childValueId
                        || existing.value?.id == childValueId))
                {
                    childMembers[entry.schemaKey] = existing;
                    previousChildren.Remove(entry.schemaKey);
                    continue;
                }
                var child = CreateChild(client, childMember, childValueId);
                child.OnChanged += HandleChildChanged;
                childMembers[entry.schemaKey] = child;
            }
            foreach (var child in previousChildren.Values)
            {
                child.OnChanged -= HandleChildChanged;
                child.Dispose();
            }
        }

        protected void HandleChildChanged(NeoMember child)
        {
            NotifyChanged(child);
        }

        protected void NotifyChildChanged(string key)
        {
            if (childMembers.TryGetValue(key, out NeoMember? child))
            {
                NotifyChanged(child);
                return;
            }
            NotifyChanged();
        }

        protected static bool ChildSelfNotifies(NeoMember child)
        {
            return child is not NeoMemberDictionary
                && child is not NeoMemberList;
        }

        public IEnumerator<KeyValuePair<string, NeoMember>> GetEnumerator()
        {
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
                inheritanceChain = NeoSchemaClassInheritance.ResolveChain(
                    schemaClass.id,
                    id => client.TryGetClass(id, out var t) ? t : null);
                mergedSchema = NeoSchemaClassInheritance.MergeInstanceSchema(
                    inheritanceChain,
                    id => client.TryGetMember(id, out Member? member)
                        ? member
                        : null);
                // The chain env alone misses constructed slots: an instance
                // of the DECLARED open class (`classId: null` rows under a
                // `GenericTest<Color>` slot) binds its params through the
                // slot member's `classArguments`, not a named
                // subclass's chain (specs/class-generics.md §4.1).
                // Concrete documents pay nothing — the overlay is skipped
                // when the slot carries no arguments.
                GenericEnv = NeoGenericResolution.ResolveInstanceEnv(
                    inheritanceChain,
                    member.classArguments);
            }
            catch (CircularInheritanceError ex)
            {
                Debug.LogError(ex);
                inheritanceChain = new List<NeoSchemaClass>();
                mergedSchema = new List<MergedSchemaEntry>();
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

        protected override NeoMember CreateChild(
            NeoClient client,
            Member childMember,
            string? overrideValueId)
        {
            // An explicit declared storage fixes the child's shape in both
            // families (specs/member-storage.md §8.3): Immutable-stamped
            // children stay read-only even under a writable parent;
            // Save/Session stamps pin the child to that ownership store.
            NeoValueOwnership? declared = client.DeclaredOwnership(childMember);
            NeoMember child = declared switch
            {
                NeoValueOwnership.Asset => Create(client, childMember, overrideValueId),
                NeoValueOwnership.Save or NeoValueOwnership.Session =>
                    CreateWritable(client, childMember, overrideValueId, declared.Value),
                _ => CreateWritable(client, childMember, overrideValueId, ownership),
            };
            child.parent = this;
            return child;
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
        internal void SetSerializedValue(string key, NeoValueWritePayload? setValue)
        {
            string nowIso = System.DateTime.UtcNow.ToString("o");

            // Resolution flows through the merged schema (inheritance
            // chain), so a Set against a key inherited from an ancestor
            // class still resolves the right child member.
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
            // Generic slots substitute to their binding before any typed
            // dispatch below (required travels with the binding —
            // specs/class-generics.md Decision 10).
            childMember = SubstituteChildMember(childMember);
            if (childMember.required && (setValue is null || setValue.isNull))
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
                client.DeclaredOwnership(childMember) ?? ownership;
            if (childOwnership == NeoValueOwnership.Asset)
            {
                throw new System.InvalidOperationException(
                    $"Cannot write '{key}' on Class '{member.id}': its effective storage is immutable.");
            }
            bool recordWritable = ownership != NeoValueOwnership.Asset;

            // Unordered lists never store membership in the array: a
            // whole-list assignment translates to Clear + Add-each, and
            // assigning null clears the members then sets the discriminator
            // to null (spec §1.6/§3.8).
            if (childMember is ListMember childListMember
                && client.IsUnorderedList(childListMember))
            {
                SetSerializedUnorderedList(key, childListMember, setValue, childOwnership, recordWritable, nowIso);
                return;
            }

            if (value?.value is not null
                && value.value.TryGetValue(key, out string existingValueId)
                && client.TryGetValue(childOwnership, existingValueId, out MemberValue? existing))
            {
                if (setValue?.isValueReference == true)
                {
                    if (!recordWritable)
                    {
                        throw new System.InvalidOperationException(
                            $"Cannot rebind '{key}' on static Class '{member.id}': a static record's value map is authored data. Only the stamped leaf's own value may be written.");
                    }
                    string importedValueId = client.ImportValueReference(
                        childOwnership,
                        setValue.valueId!,
                        out bool sourceMoved,
                        existingValueId);
                    if (importedValueId == existingValueId)
                    {
                        return;
                    }
                    ObjectMemberValue record = EnsureWritableObject(nowIso);
                    record.value![key] = importedValueId;
                    record.updatedAt = nowIso;
                    client.SetWritableValue(ownership, record);
                    value = record;
                    client.RemoveWritableValueAndDescendantsIfUnlinked(
                        childOwnership, existingValueId, childMember);
                    ReinitializeChildren();
                    if (sourceMoved)
                    {
                        setValue.RetargetMovedReference(client, childMember, importedValueId, childOwnership);
                    }
                    NotifyChildChanged(key);
                    return;
                }
                bool childWillSelfNotify = childMembers.TryGetValue(key, out NeoMember? existingChild)
                    && ChildSelfNotifies(existingChild);
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
                next.genericBindings = existing.genericBindings;
                NeoGenericResolution.StampGenericBindings(client, childMember, next, GenericEnv);
                client.SetWritablePayloadRows(childOwnership, setValue?.value);
                client.SetWritableValue(childOwnership, next);
                ReinitializeChildren();
                if (!childWillSelfNotify)
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
            string newValueId;
            if (setValue?.isValueReference == true)
            {
                newValueId = client.ImportValueReference(
                    childOwnership,
                    setValue.valueId!,
                    out bool sourceMoved);
                if (sourceMoved)
                {
                    setValue.RetargetMovedReference(client, childMember, newValueId, childOwnership);
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
                client.SetWritablePayloadRows(childOwnership, setValue?.value);
                client.SetWritableValue(childOwnership, newValueRow);
            }

            ObjectMemberValue keyedRecord = EnsureWritableObject(nowIso);
            keyedRecord.value![key] = newValueId;
            keyedRecord.updatedAt = nowIso;
            client.SetWritableValue(ownership, keyedRecord);
            value = keyedRecord;

            ReinitializeChildren();
            NotifyChildChanged(key);
        }

        private void SetSerializedUnorderedList(
            string key,
            ListMember childListMember,
            NeoValueWritePayload? setValue,
            NeoValueOwnership childOwnership,
            bool recordWritable,
            string nowIso)
        {
            bool hasBoundRow = value?.value is not null
                && value.value.TryGetValue(key, out string existingListValueId)
                && client.TryGetValue(childOwnership, existingListValueId, out MemberValue? _);
            if (!hasBoundRow)
            {
                if (!recordWritable)
                {
                    throw new System.InvalidOperationException(
                        $"Cannot write '{key}' on static Class '{member.id}': the unordered list has no authored value to shadow, and a static record cannot gain new keys at runtime. Author a value for '{key}' in the web editor.");
                }
                // Mint the discriminator row (present, empty) and link it.
                var listRow = new ArrayMemberValue
                {
                    id = System.Guid.NewGuid().ToString(),
                    createdAt = nowIso,
                    updatedAt = nowIso,
                    value = System.Array.Empty<string>(),
                };
                NeoGenericResolution.StampGenericBindings(client, childListMember, listRow, GenericEnv);
                client.SetWritableValue(childOwnership, listRow);
                ObjectMemberValue record = EnsureWritableObject(nowIso);
                record.value![key] = listRow.id;
                record.updatedAt = nowIso;
                client.SetWritableValue(ownership, record);
                value = record;
                ReinitializeChildren();
            }

            if (Get<NeoMember>(key) is not NeoMemberListWritable listNode)
            {
                throw new System.InvalidOperationException(
                    $"Unordered list '{key}' on Class '{member.id}' did not resolve a writable list node; the record's ownership does not permit list writes.");
            }
            listNode.AssignSerialized(setValue);
            NotifyChildChanged(key);
        }

        internal override void BindChildValueId(NeoMember child, string childValueId)
        {
            if (!TryGetSchemaKeyForChild(child, out string? key))
            {
                throw new System.InvalidOperationException(
                    $"Cannot bind a child value on Class '{member.id}': child is not a registered schema field.");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            ObjectMemberValue record = EnsureWritableObject(nowIso);
            record.value![key] = childValueId;
            record.updatedAt = nowIso;
            client.SetWritableValue(ownership, record);
            value = record;
            ReinitializeChildren();
            NotifyChildChanged(key);
        }

        /// <summary>
        /// Returns the record's own object row guaranteed writable (a
        /// clone-on-write shadow at the stable id), minting + binding a
        /// fresh empty record through the parent when nothing is bound yet.
        /// </summary>
        private ObjectMemberValue EnsureWritableObject(string nowIso)
        {
            var writable = EnsureWritableValue();
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
            BindNewValue(record);
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
            if (value?.value is null) return;
            if (!value.value.ContainsKey(key)) return;
            string? schemaKeyedMemberId = LookupMergedMemberId(key);
            Member? removedMember = schemaKeyedMemberId is not null
                && client.TryGetMember(schemaKeyedMemberId, out Member? resolvedMember)
                    ? SubstituteChildMember(resolvedMember)
                    : null;
            string nowIso = System.DateTime.UtcNow.ToString("o");

            // Clone-on-write the record (shadowing the authored default at
            // its stable id) and drop the key.
            ObjectMemberValue record = EnsureWritableObject(nowIso);
            string removedValueId = record.value![key];
            record.value.Remove(key);
            record.updatedAt = nowIso;
            client.SetWritableValue(ownership, record);
            value = record;

            if (childMembers.TryGetValue(key, out NeoMember? child))
            {
                child.OnChanged -= HandleChildChanged;
                child.Dispose();
                childMembers.Remove(key);
            }

            NeoValueOwnership removedOwnership =
                (removedMember is null ? null : client.DeclaredOwnership(removedMember))
                ?? ownership;
            client.RemoveWritableValueAndDescendantsIfUnlinked(
                removedOwnership, removedValueId, removedMember);
            NotifyChanged();
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
            if (client.TryGetMember(memberId, out Member? childMember)
                && SubstituteChildMember(childMember).required)
            {
                throw new System.InvalidOperationException(
                    $"Cannot unset required field '{key}'.");
            }
            if (value?.value is null || !value.value.TryGetValue(key, out string childValueId))
            {
                return;
            }
            client.WriteRemovalTombstone(ownership, childValueId);
            ReinitializeChildren();
            NotifyChildChanged(key);
        }
    }
}
