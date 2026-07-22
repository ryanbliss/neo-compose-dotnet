// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Non-generic polymorphic root for the NeoMember hierarchy.
    /// Lets heterogeneous containers (e.g., Class / Dictionary
    /// child maps, List child arrays) hold any
    /// <see cref="NeoMember{TMember, TValue}"/> instance under
    /// one element type. Consumers iterating a non-generic
    /// <c>NeoMember</c> reference reach the typed view via
    /// pattern-matching:
    /// <code>
    /// if (child is NeoMemberString s) { /* s.member is StringMember */ }
    /// </code>
    ///
    /// <para>The two properties (<c>member</c>, <c>value</c>) are
    /// concrete here and shadowed (via <c>new</c>) on the typed
    /// <see cref="NeoMember{TMember, TValue}"/> intermediate so
    /// pattern-matched references reach a strongly-typed accessor
    /// without an extra cast. (Shadowing rather than covariant
    /// returns because the netstandard2.1 / Mono target Unity uses
    /// doesn't support runtime covariance.)</para>
    /// </summary>
    public abstract class NeoMember : NeoNode, System.IDisposable
    {
        public Member member { get; }
        public NeoValueOwnership ownership { get; }
        /// <summary>
        /// The override-value-id passed to the ctor — together with
        /// <see cref="Member.id"/> it composes the registry key
        /// (<see cref="NeoClient.MakeNodeKey"/>). Lifted to the base so
        /// <see cref="Dispose"/> can compute the unregister key without
        /// reaching into the typed intermediate.
        /// </summary>
        public string? overrideValueId { get; }
        public MemberValue? value { get; protected set; }
        /// <summary>
        /// Parent <see cref="NeoMember"/> in the wrapper tree, or
        /// null at the root. Set by collection classes
        /// (<see cref="NeoMemberClass"/> /
        /// <see cref="NeoMemberDictionary"/> /
        /// <see cref="NeoMemberList"/>) when they construct child
        /// nodes; consumers (notably
        /// <see cref="NeoMemberNSProperty.Compute"/>) walk this chain
        /// to resolve <c>__this__</c> from the nearest Class-shaped
        /// ancestor.
        /// </summary>
        public NeoMember? parent { get; internal set; }
        public event System.Action<NeoMember>? OnChanged;
        public event System.Action<NeoMember>? OnDisposed;
        /// <summary>
        /// True after <see cref="Dispose"/> has run. Subclasses must
        /// short-circuit further work (events, setter mutations) when
        /// disposed; consumers holding stale references shouldn't expect
        /// further updates.
        /// </summary>
        public bool isDisposed { get; private set; }
        private int declarationReferenceCount;

        protected NeoMember(
            NeoClient client,
            Member member,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset) : base(client)
        {
            this.member = member;
            this.overrideValueId = overrideValueId;
            this.ownership = ownership;
            // Storage partitions (spec §6): binding a node to a world grid's
            // value id IS content access — lazily merge the grid's
            // `world:<gridClassId>` placement partition before subclasses
            // resolve the row. The bound row (the grid root, in main) yields
            // the type id the key is derived from; a no-op for non-grid value
            // ids (their type id keys no shipped partition) and for deep
            // placement ids that resolve no row until their partition loads.
            string? boundValueId = overrideValueId ?? member.valueId;
            if (boundValueId is not null)
            {
                client.EnsureWorldPartitionLoaded(boundValueId);
            }
        }

        /// <summary>
        /// Releases this node from the client's flat registry and marks
        /// it disposed. Idempotent. Subclasses override to release
        /// additional state (event subscriptions on the typed
        /// intermediate; child <see cref="NeoMember"/>s on
        /// collection classes) — they should still call
        /// <c>base.Dispose()</c> last so this method's bookkeeping
        /// runs after their cleanup.
        /// </summary>
        public virtual void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            OnDisposed?.Invoke(this);
            client.UnregisterNode(this);
        }

        internal void RetainDeclarationReference()
        {
            declarationReferenceCount++;
        }

        internal void ReleaseDeclarationReference()
        {
            if (declarationReferenceCount <= 0) return;
            declarationReferenceCount--;
            if (declarationReferenceCount == 0) Dispose();
        }

        protected void NotifyChanged()
        {
            NotifyChanged(this);
        }

        protected void NotifyChanged(NeoMember changed)
        {
            if (isDisposed) return;
            OnChanged?.Invoke(changed);
        }

        /// <summary>
        /// Binds <paramref name="childValueId"/> into this container's
        /// value row so a <paramref name="child"/> that minted a
        /// brand-new value (no authored default and not previously bound)
        /// becomes reachable by its stable id. Container classes
        /// (<see cref="NeoMemberClass"/> /
        /// <see cref="NeoMemberDictionary"/>) override this to write the
        /// key→id pair into their own (clone-on-write) value map and
        /// re-walk children. The base throws — leaves and list-positional
        /// entries don't bind children this way (list entries always carry
        /// an id at construction).
        /// </summary>
        internal virtual void BindChildValueId(NeoMember child, string childValueId)
        {
            throw new System.InvalidOperationException(
                $"Member '{member.id}' ({GetType().Name}) cannot bind a child value id; "
                + "only Class and Dictionary containers bind unkeyed child values.");
        }

        /// <summary>
        /// Read-only factory — instantiates the matching
        /// <c>NeoMember{Kind}</c> for the given member. Use
        /// <see cref="CreateWritable"/> when constructing a writeable
        /// sub-tree (e.g., descendants of <c>NeoClient.save</c>).
        ///
        /// <para>Returns the registry-cached instance for
        /// <paramref name="member"/> + <paramref name="overrideValueId"/>
        /// when one already exists — see
        /// <see cref="NeoClient.TryGetNode"/>. Construction is the
        /// fallback when nothing is cached. Mixing
        /// <see cref="Create"/> / <see cref="CreateWritable"/> for the same
        /// key returns whichever was first; callers wanting the
        /// writeable variant should bootstrap their sub-tree through
        /// <see cref="CreateWritable"/> from the root down.</para>
        /// </summary>
        public static NeoMember Create(
            NeoClient client,
            Member member,
            string? overrideValueId)
        {
            if (client.TryGetNode(
                    member.RuntimeDeclarationIdentity,
                    overrideValueId,
                    NeoValueOwnership.Asset,
                    out NeoMember? existing))
            {
                return existing;
            }
            return member switch
            {
                NullMember n => new NeoMemberNull(client, n, overrideValueId),
                BoolMember b => new NeoMemberBool(client, b, overrideValueId),
                IntMember i => new NeoMemberInt(client, i, overrideValueId),
                FloatMember f => new NeoMemberFloat(client, f, overrideValueId),
                StringMember s => new NeoMemberString(client, s, overrideValueId),
                DictionaryMember d => new NeoMemberDictionary(client, d, overrideValueId),
                ListMember l => new NeoMemberList(client, l, overrideValueId),
                ClassMember c => new NeoMemberClass(client, c, overrideValueId),
                EnumMember e => new NeoMemberEnum(client, e, overrideValueId),
                LookupMember lk => new NeoMemberLookup(client, lk, overrideValueId),
                DialogueLookupMember dl => new NeoMemberDialogueLookup(client, dl, overrideValueId),
                NSPropertyMember ng => new NeoMemberNSProperty(client, ng, overrideValueId),
                NSFunctionMember nsf => new NeoMemberNSFunction(client, nsf, overrideValueId),
                FunctionMember fn => new NeoMemberFunction(client, fn, overrideValueId),
                SpriteMember sp => new NeoMemberSprite(client, sp, overrideValueId),
                AudioMember au => new NeoMemberAudio(client, au, overrideValueId),
                Vector2Member v2 => new NeoMemberVector2(client, v2, overrideValueId),
                Vector2IntMember v2i => new NeoMemberVector2Int(client, v2i, overrideValueId),
                Vector3Member v3 => new NeoMemberVector3(client, v3, overrideValueId),
                Vector3IntMember v3i => new NeoMemberVector3Int(client, v3i, overrideValueId),
                ColorMember col => new NeoMemberColor(client, col, overrideValueId),
                DecimalMember dec => new NeoMemberDecimal(client, dec, overrideValueId),
                GenericMember g => throw new System.InvalidOperationException(
                    $"Member '{g.id}' ({g.name}) is Generic-typed (param '{g.genericParamId}') — Generic slots must be substituted to their binding member (NeoGenericResolution.SubstituteMember) before node construction."),
                _ => throw new System.ArgumentException(
                    $"Unknown member type {member.GetType().Name}", nameof(member)),
            };
        }

        /// <summary>
        /// Writeable factory — instantiates <c>NeoMember{Kind}Writable</c>
        /// for kinds that support write-back, falling through to the
        /// read-only variant for Null and NSProperty (which have no
        /// stored value to set).
        ///
        /// <para>Same registry-first semantics as
        /// <see cref="Create"/>: if a node is already registered for
        /// <paramref name="member"/> + <paramref name="overrideValueId"/>
        /// it's returned as-is, even if it's a read-only kind from a
        /// prior <see cref="Create"/> call.</para>
        /// </summary>
        public static NeoMember CreateWritable(
            NeoClient client,
            Member member,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Session)
        {
            if (client.TryGetNode(
                    member.RuntimeDeclarationIdentity,
                    overrideValueId,
                    ownership,
                    out NeoMember? existing)
                && IsWritableCompatible(member, existing))
            {
                return existing;
            }
            return member switch
            {
                NullMember n => new NeoMemberNull(client, n, overrideValueId, ownership),
                BoolMember b => new NeoMemberBoolWritable(client, b, overrideValueId, ownership),
                IntMember i => new NeoMemberIntWritable(client, i, overrideValueId, ownership),
                FloatMember f => new NeoMemberFloatWritable(client, f, overrideValueId, ownership),
                StringMember s => new NeoMemberStringWritable(client, s, overrideValueId, ownership),
                DictionaryMember d => new NeoMemberDictionaryWritable(client, d, overrideValueId, ownership),
                ListMember l => new NeoMemberListWritable(client, l, overrideValueId, ownership),
                ClassMember c => new NeoMemberClassWritable(client, c, overrideValueId, ownership),
                EnumMember e => new NeoMemberEnumWritable(client, e, overrideValueId, ownership),
                LookupMember lk => new NeoMemberLookupWritable(client, lk, overrideValueId, ownership),
                DialogueLookupMember dl => new NeoMemberDialogueLookupWritable(client, dl, overrideValueId, ownership),
                NSPropertyMember ng => new NeoMemberNSProperty(client, ng, overrideValueId, ownership),
                NSFunctionMember nsf => new NeoMemberNSFunction(client, nsf, overrideValueId, ownership),
                FunctionMember fn => new NeoMemberFunction(client, fn, overrideValueId, ownership),
                SpriteMember sp => new NeoMemberSpriteWritable(client, sp, overrideValueId, ownership),
                AudioMember au => new NeoMemberAudioWritable(client, au, overrideValueId, ownership),
                Vector2Member v2 => new NeoMemberVector2Writable(client, v2, overrideValueId, ownership),
                Vector2IntMember v2i => new NeoMemberVector2IntWritable(client, v2i, overrideValueId, ownership),
                Vector3Member v3 => new NeoMemberVector3Writable(client, v3, overrideValueId, ownership),
                Vector3IntMember v3i => new NeoMemberVector3IntWritable(client, v3i, overrideValueId, ownership),
                ColorMember col => new NeoMemberColorWritable(client, col, overrideValueId, ownership),
                DecimalMember dec => new NeoMemberDecimalWritable(client, dec, overrideValueId, ownership),
                GenericMember g => throw new System.InvalidOperationException(
                    $"Member '{g.id}' ({g.name}) is Generic-typed (param '{g.genericParamId}') — Generic slots must be substituted to their binding member (NeoGenericResolution.SubstituteMember) before writable node construction."),
                _ => throw new System.ArgumentException(
                    $"Unknown member type {member.GetType().Name}", nameof(member)),
            };
        }

        private static bool IsWritableCompatible(Member member, NeoMember existing)
        {
            return member switch
            {
                NullMember => existing is NeoMemberNull,
                BoolMember => existing is NeoMemberBoolWritable,
                IntMember => existing is NeoMemberIntWritable,
                FloatMember => existing is NeoMemberFloatWritable,
                StringMember => existing is NeoMemberStringWritable,
                DictionaryMember => existing is NeoMemberDictionaryWritable,
                ListMember => existing is NeoMemberListWritable,
                ClassMember => existing is NeoMemberClassWritable,
                EnumMember => existing is NeoMemberEnumWritable,
                LookupMember => existing is NeoMemberLookupWritable,
                DialogueLookupMember => existing is NeoMemberDialogueLookupWritable,
                NSPropertyMember => existing is NeoMemberNSProperty,
                NSFunctionMember => existing is NeoMemberNSFunction,
                FunctionMember => existing is NeoMemberFunction,
                SpriteMember => existing is NeoMemberSpriteWritable,
                AudioMember => existing is NeoMemberAudioWritable,
                Vector2Member => existing is NeoMemberVector2Writable,
                Vector2IntMember => existing is NeoMemberVector2IntWritable,
                Vector3Member => existing is NeoMemberVector3Writable,
                Vector3IntMember => existing is NeoMemberVector3IntWritable,
                ColorMember => existing is NeoMemberColorWritable,
                DecimalMember => existing is NeoMemberDecimalWritable,
                GenericMember => false,
                _ => false,
            };
        }
    }

    /// <summary>
    /// Read-only marker node for native Function members. Function
    /// members describe callable schema members and intentionally have no
    /// backing value row.
    /// </summary>
    public sealed class NeoMemberFunction : NeoMember
    {
        public new FunctionMember member => (FunctionMember)base.member;

        public NeoMemberFunction(
            NeoClient client,
            FunctionMember member,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership)
        {
            client.RegisterNode(this);
        }
    }

    /// <summary>
    /// Typed intermediate for the <see cref="NeoMember"/> hierarchy.
    /// Carries the kind-specific <typeparamref name="TMember"/> /
    /// <typeparamref name="TValue"/> bindings and overrides the
    /// abstract base properties with covariant typed returns (C# 9+),
    /// so a downcast through pattern-matching gives strongly-typed
    /// access without a second cast.
    ///
    /// <para>Subclasses extend this with the matching DTO pair, e.g.
    /// <c>NeoMemberString : NeoMember&lt;StringMember, StringMemberValue&gt;</c>.
    /// Read-only by default; the <c>*Writable</c> variants add a typed
    /// <c>Set</c> (or <c>Add</c>/<c>Insert</c>/<c>RemoveAt</c> for
    /// collection classes) that funnels through
    /// <c>client.SetSaveValue</c> / <c>client.AddSaveValue</c>.</para>
    /// </summary>
    public abstract class NeoMember<TMember, TValue> : NeoMember
        where TMember : Member
        where TValue : MemberValue
    {
        /// <summary>
        /// Typed accessor for <see cref="NeoMember.member"/>.
        /// Shadows the base via <c>new</c> — calls through a
        /// pattern-matched typed reference resolve here; calls
        /// through a plain <see cref="NeoMember"/> reference resolve
        /// to the base's untyped property. Both return the same
        /// underlying instance — the value is set once via the base
        /// ctor (get-only), no setter shadow needed.
        /// </summary>
        public new TMember member => (TMember)base.member;

        /// <summary>
        /// Typed accessor for <see cref="NeoMember.value"/>.
        /// Shadows the base via <c>new</c> for the same reason as
        /// <see cref="member"/>.
        /// </summary>
        public new TValue? value
        {
            get => (TValue?)base.value;
            protected set => base.value = value;
        }

        /// <summary>
        /// Resolves the current value-id via the chain:
        /// <c>overrideValueId</c> (the id this node was bound to by its
        /// parent's value-map) → authored <c>member.valueId</c> (the
        /// authored default, used by the root and by directly-resolved
        /// members). Returns null if nothing is bound.
        ///
        /// <para>Value ids are stable instance identities: a Save/Session
        /// shadows an authored value at the <b>same</b> id, so resolution
        /// is identical for asset/save/session — the per-ownership choice
        /// of which row wins happens in
        /// <see cref="NeoClient.TryGetOverlaidValue"/>. There is no longer
        /// an <c>memberValueOverrides</c> indirection hop.</para>
        /// </summary>
        protected string? valueId => overrideValueId ?? boundValueId ?? member.valueId;

        /// <summary>
        /// Id minted by <see cref="BindNewValue"/> for a <b>parentless</b>
        /// node that had no authored default (a standalone writable root in
        /// isolation — real saves hang every value off a root that carries
        /// an authored <c>valueId</c>). Kept in-memory on the node rather
        /// than in a shared member-keyed map, so it never mis-keys
        /// sibling collection items that share a template member id.
        /// </summary>
        private string? boundValueId;

        /// <summary>
        /// Live read of the bound value through
        /// <see cref="NeoClient.TryGetValue{T}"/>. Returns null when
        /// no value is stored. Used to refresh the cached value after
        /// a Set creates a new row.
        /// </summary>
        protected TValue? valueData
        {
            get
            {
                var resolvedValueId = valueId;
                if (resolvedValueId is null)
                {
                    if (member.isReadOnly == true)
                    {
                        return client.CreateDeclarationDefaultValue(
                            member,
                            $"__neo_readonly_default:{member.RuntimeDeclarationIdentity}") as TValue;
                    }
                    return MemberValueFactory.CreateFromDefault(
                        member,
                        $"__neo_default:{member.id}",
                        member.createdAt,
                        member.updatedAt) as TValue;
                }

                if (!client.TryGetOverlaidValue(ownership, resolvedValueId, out TValue? match)) return null;
                return match;
            }
        }

        public NeoMember(
            NeoClient client,
            TMember member,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership)
        {
            InitFromValueData();
            // Subscribe before registering so the first value-row change is
            // observable from the moment the node exists.
            client.OnWritableValueChanged += HandleWritableValueChanged;
            // Last step in the base ctor — children walked from a
            // collection-type derived ctor body run after this, but they
            // register under their own keys, so registration order is
            // parent-then-children which is what consumers expect when
            // walking the registry.
            client.RegisterNode(this);
        }

        public NeoMember(
            NeoClient client,
            string memberId,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, ResolveMember(client, memberId), overrideValueId, ownership)
        {
            InitFromValueData();
            client.OnWritableValueChanged += HandleWritableValueChanged;
            client.RegisterNode(this);
        }

        public override void Dispose()
        {
            if (isDisposed) return;
            client.OnWritableValueChanged -= HandleWritableValueChanged;
            base.Dispose();
        }

        private void HandleWritableValueChanged(
            NeoValueOwnership changedOwnership,
            string changedValueId)
        {
            if (changedOwnership != ownership) return;
            if (changedValueId != valueId) return;
            if (this is NeoMemberDictionary
                || this is NeoMemberList)
            {
                // Local collection mutators reinitialize explicitly after
                // completing their multi-row write and publish one precise
                // collection change. External live-save reconciliation has
                // no such mutator frame, so collection nodes must refresh
                // here or their membership and derived indexes stay stale.
                if (client.CurrentChangeSource != NeoChangeSource.External)
                {
                    return;
                }
            }
            OnValueIdChainChanged();
        }

        /// <summary>
        /// Called when this node's bound value row changes (its
        /// <see cref="valueId"/> was written, shadowed, or its shadow
        /// dropped). Default implementation refreshes <see cref="value"/>
        /// from <see cref="valueData"/> (so it tracks the row, including
        /// becoming null when the shadow is cleared and there is no authored
        /// default). Collection-kind subclasses override to also re-walk
        /// their children.
        /// </summary>
        protected virtual void OnValueIdChainChanged()
        {
            // valueData reads through the resolution chain; if nothing
            // is bound any more, we end up with `value = null` —
            // matching the user-visible "valueId becomes null → value
            // becomes null" semantic.
            value = valueData;
            NotifyChanged();
        }

        /// <summary>
        /// Static helper for the memberId-based ctor — resolves the
        /// <typeparamref name="TMember"/> from the client up-front so
        /// it can be passed to the base ctor (which initializes the
        /// get-only <see cref="NeoMember.member"/> property).
        /// </summary>
        private static TMember ResolveMember(NeoClient client, string memberId)
        {
            if (!client.TryGetMember(memberId, out TMember? resolved))
            {
                throw new System.ArgumentException(
                    $"No {typeof(TMember).Name} for member {memberId}",
                    nameof(memberId));
            }
            return resolved;
        }

        private void InitFromValueData()
        {
            var data = valueData;
            if (data is null) BuildEmptyData();
            else Initialize(data);
        }

        /// <summary>
        /// Re-reads the current value through the resolution chain
        /// and re-runs <see cref="Initialize"/> if a value is now
        /// bound. Writable variants call this after creating a new
        /// top-level row so the cached <c>value</c> and any
        /// child-tree state are in sync.
        /// </summary>
        protected void RefreshFromValueData()
        {
            var data = valueData;
            if (data is not null) Initialize(data);
        }

        /// <summary>
        /// Returns this node's value row guaranteed to be writable — i.e.
        /// present in the node's own Save/Session store — so a mutator can
        /// change it in place and persist. When the resolved value is the
        /// shared authored asset row (sparse overlay; nothing shadowed
        /// yet) a clone is registered at the <b>same</b> id and
        /// <see cref="value"/> is retargeted to it. Value ids are stable
        /// instance identities: the parent already references this id, so
        /// shadowing needs no relink and no path materialization. A write
        /// also clears any removal tombstone (resurrecting the slot).
        /// Returns null only when nothing is bound — callers handle that by
        /// minting + binding a fresh row via <see cref="BindNewValue"/>.
        /// </summary>
        protected TValue? EnsureWritableValue()
        {
            if (ownership == NeoValueOwnership.Asset) return value;
            string? id = valueId;
            if (id is null) return null;
            if (client.TryGetWritableValue(ownership, id, out TValue? owned))
            {
                owned.mark = null;
                value = owned;
                return owned;
            }
            if (!client.TryGetOverlaidValue(ownership, id, out TValue? authored)) return null;
            var clone = (TValue)client.CloneRowForWrite(authored);
            client.SetWritableValueSilently(ownership, clone);
            value = clone;
            return clone;
        }

        /// <summary>
        /// Persists a freshly-minted <paramref name="newRow"/> for a node
        /// that had no bound value, then binds its id into the parent
        /// container's value map (via <see cref="BindChildValueId"/>) so it
        /// is reachable by the parent's stable reference. The bind re-walks
        /// the parent's children, which replaces this (now-stale) node with
        /// one bound to <paramref name="newRow"/>'s id; callers re-fetch
        /// through the parent. Throws when there is no parent to bind into
        /// (a value-less root — which shouldn't occur for valid projects,
        /// whose roots carry an authored <c>valueId</c>).
        /// </summary>
        protected void BindNewValue(TValue newRow)
        {
            if (ownership == NeoValueOwnership.Asset)
            {
                throw new System.InvalidOperationException(
                    $"Cannot bind a new value on an asset-owned member '{member.id}'.");
            }
            client.SetWritableValue(ownership, newRow);
            value = newRow;
            boundValueId = newRow.id;
            if (parent is not null)
            {
                parent.BindChildValueId(this, newRow.id);
                return;
            }
            // Parentless root with no authored default — remember the minted
            // id on the node so its own resolution chain finds it.
        }

        /// <summary>
        /// Override on collection classes to spin up children.
        /// Default no-op; the <c>BuildEmpty</c> path is for read-only
        /// instances where there's nothing to initialize. Saved
        /// variants may pre-allocate.
        /// </summary>
        virtual protected void BuildEmptyData() { }

        /// <summary>
        /// Override on collection classes to walk the value's children
        /// and instantiate <see cref="NeoMember"/> instances under
        /// them. Always call <c>base.Initialize(value)</c> first to
        /// cache the value reference.
        /// </summary>
        virtual protected void Initialize(TValue value)
        {
            this.value = value;
            NotifyChanged();
        }
    }
}
