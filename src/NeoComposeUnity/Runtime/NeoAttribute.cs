// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Non-generic polymorphic root for the NeoAttribute hierarchy.
    /// Lets heterogeneous containers (e.g., Custom / Dictionary
    /// child maps, List child arrays) hold any
    /// <see cref="NeoAttribute{TAttribute, TValue}"/> instance under
    /// one element type. Consumers iterating a non-generic
    /// <c>NeoAttribute</c> reference reach the typed view via
    /// pattern-matching:
    /// <code>
    /// if (child is NeoAttributeString s) { /* s.attribute is StringAttribute */ }
    /// </code>
    ///
    /// <para>The two properties (<c>attribute</c>, <c>value</c>) are
    /// concrete here and shadowed (via <c>new</c>) on the typed
    /// <see cref="NeoAttribute{TAttribute, TValue}"/> intermediate so
    /// pattern-matched references reach a strongly-typed accessor
    /// without an extra cast. (Shadowing rather than covariant
    /// returns because the netstandard2.1 / Mono target Unity uses
    /// doesn't support runtime covariance.)</para>
    /// </summary>
    public abstract class NeoAttribute : NeoNode, System.IDisposable
    {
        public Attribute attribute { get; }
        public NeoValueOwnership ownership { get; }
        /// <summary>
        /// The override-value-id passed to the ctor — together with
        /// <see cref="Attribute.id"/> it composes the registry key
        /// (<see cref="NeoClient.MakeNodeKey"/>). Lifted to the base so
        /// <see cref="Dispose"/> can compute the unregister key without
        /// reaching into the typed intermediate.
        /// </summary>
        public string? overrideValueId { get; }
        public AttributeValue? value { get; protected set; }
        /// <summary>
        /// Parent <see cref="NeoAttribute"/> in the wrapper tree, or
        /// null at the root. Set by collection types
        /// (<see cref="NeoAttributeCustom"/> /
        /// <see cref="NeoAttributeDictionary"/> /
        /// <see cref="NeoAttributeList"/>) when they construct child
        /// nodes; consumers (notably
        /// <see cref="NeoAttributeNSGetter.Compute"/>) walk this chain
        /// to resolve <c>__this__</c> from the nearest Custom-shaped
        /// ancestor.
        /// </summary>
        public NeoAttribute? parent { get; internal set; }
        public event System.Action<NeoAttribute>? OnChanged;
        public event System.Action<NeoAttribute>? OnDisposed;
        /// <summary>
        /// True after <see cref="Dispose"/> has run. Subclasses must
        /// short-circuit further work (events, setter mutations) when
        /// disposed; consumers holding stale references shouldn't expect
        /// further updates.
        /// </summary>
        public bool isDisposed { get; private set; }

        protected NeoAttribute(
            NeoClient client,
            Attribute attribute,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset) : base(client)
        {
            this.attribute = attribute;
            this.overrideValueId = overrideValueId;
            this.ownership = ownership;
        }

        /// <summary>
        /// Releases this node from the client's flat registry and marks
        /// it disposed. Idempotent. Subclasses override to release
        /// additional state (event subscriptions on the typed
        /// intermediate; child <see cref="NeoAttribute"/>s on
        /// collection types) — they should still call
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

        protected void NotifyChanged()
        {
            NotifyChanged(this);
        }

        protected void NotifyChanged(NeoAttribute changed)
        {
            if (isDisposed) return;
            OnChanged?.Invoke(changed);
        }

        /// <summary>
        /// Binds <paramref name="childValueId"/> into this container's
        /// value row so a <paramref name="child"/> that minted a
        /// brand-new value (no authored default and not previously bound)
        /// becomes reachable by its stable id. Container types
        /// (<see cref="NeoAttributeCustom"/> /
        /// <see cref="NeoAttributeDictionary"/>) override this to write the
        /// key→id pair into their own (clone-on-write) value map and
        /// re-walk children. The base throws — leaves and list-positional
        /// entries don't bind children this way (list entries always carry
        /// an id at construction).
        /// </summary>
        internal virtual void BindChildValueId(NeoAttribute child, string childValueId)
        {
            throw new System.InvalidOperationException(
                $"Attribute '{attribute.id}' ({GetType().Name}) cannot bind a child value id; "
                + "only Custom and Dictionary containers bind unkeyed child values.");
        }

        /// <summary>
        /// Read-only factory — instantiates the matching
        /// <c>NeoAttribute{Kind}</c> for the given attribute. Use
        /// <see cref="CreateWritable"/> when constructing a writeable
        /// sub-tree (e.g., descendants of <c>NeoClient.save</c>).
        ///
        /// <para>Returns the registry-cached instance for
        /// <paramref name="attribute"/> + <paramref name="overrideValueId"/>
        /// when one already exists — see
        /// <see cref="NeoClient.TryGetNode"/>. Construction is the
        /// fallback when nothing is cached. Mixing
        /// <see cref="Create"/> / <see cref="CreateWritable"/> for the same
        /// key returns whichever was first; callers wanting the
        /// writeable variant should bootstrap their sub-tree through
        /// <see cref="CreateWritable"/> from the root down.</para>
        /// </summary>
        public static NeoAttribute Create(
            NeoClient client,
            Attribute attribute,
            string? overrideValueId)
        {
            if (client.TryGetNode(attribute.id, overrideValueId, NeoValueOwnership.Asset, out NeoAttribute? existing))
            {
                return existing;
            }
            return attribute switch
            {
                NullAttribute n => new NeoAttributeNull(client, n, overrideValueId),
                BoolAttribute b => new NeoAttributeBool(client, b, overrideValueId),
                IntAttribute i => new NeoAttributeInt(client, i, overrideValueId),
                FloatAttribute f => new NeoAttributeFloat(client, f, overrideValueId),
                StringAttribute s => new NeoAttributeString(client, s, overrideValueId),
                DictionaryAttribute d => new NeoAttributeDictionary(client, d, overrideValueId),
                ListAttribute l => new NeoAttributeList(client, l, overrideValueId),
                CustomAttribute c => new NeoAttributeCustom(client, c, overrideValueId),
                EnumAttribute e => new NeoAttributeEnum(client, e, overrideValueId),
                LookupAttribute lk => new NeoAttributeLookup(client, lk, overrideValueId),
                NSGetterAttribute ng => new NeoAttributeNSGetter(client, ng, overrideValueId),
                FunctionAttribute fn => new NeoAttributeFunction(client, fn, overrideValueId),
                SpriteAttribute sp => new NeoAttributeSprite(client, sp, overrideValueId),
                AudioAttribute au => new NeoAttributeAudio(client, au, overrideValueId),
                Vector2Attribute v2 => new NeoAttributeVector2(client, v2, overrideValueId),
                Vector2IntAttribute v2i => new NeoAttributeVector2Int(client, v2i, overrideValueId),
                Vector3Attribute v3 => new NeoAttributeVector3(client, v3, overrideValueId),
                Vector3IntAttribute v3i => new NeoAttributeVector3Int(client, v3i, overrideValueId),
                _ => throw new System.ArgumentException(
                    $"Unknown attribute type {attribute.GetType().Name}", nameof(attribute)),
            };
        }

        /// <summary>
        /// Writeable factory — instantiates <c>NeoAttribute{Kind}Writable</c>
        /// for kinds that support write-back, falling through to the
        /// read-only variant for Null and NSGetter (which have no
        /// stored value to set).
        ///
        /// <para>Same registry-first semantics as
        /// <see cref="Create"/>: if a node is already registered for
        /// <paramref name="attribute"/> + <paramref name="overrideValueId"/>
        /// it's returned as-is, even if it's a read-only kind from a
        /// prior <see cref="Create"/> call.</para>
        /// </summary>
        public static NeoAttribute CreateWritable(
            NeoClient client,
            Attribute attribute,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Session)
        {
            if (client.TryGetNode(attribute.id, overrideValueId, ownership, out NeoAttribute? existing)
                && IsWritableCompatible(attribute, existing))
            {
                return existing;
            }
            return attribute switch
            {
                NullAttribute n => new NeoAttributeNull(client, n, overrideValueId, ownership),
                BoolAttribute b => new NeoAttributeBoolWritable(client, b, overrideValueId, ownership),
                IntAttribute i => new NeoAttributeIntWritable(client, i, overrideValueId, ownership),
                FloatAttribute f => new NeoAttributeFloatWritable(client, f, overrideValueId, ownership),
                StringAttribute s => new NeoAttributeStringWritable(client, s, overrideValueId, ownership),
                DictionaryAttribute d => new NeoAttributeDictionaryWritable(client, d, overrideValueId, ownership),
                ListAttribute l => new NeoAttributeListWritable(client, l, overrideValueId, ownership),
                CustomAttribute c => new NeoAttributeCustomWritable(client, c, overrideValueId, ownership),
                EnumAttribute e => new NeoAttributeEnumWritable(client, e, overrideValueId, ownership),
                LookupAttribute lk => new NeoAttributeLookupWritable(client, lk, overrideValueId, ownership),
                NSGetterAttribute ng => new NeoAttributeNSGetter(client, ng, overrideValueId, ownership),
                FunctionAttribute fn => new NeoAttributeFunction(client, fn, overrideValueId, ownership),
                SpriteAttribute sp => new NeoAttributeSpriteWritable(client, sp, overrideValueId, ownership),
                AudioAttribute au => new NeoAttributeAudioWritable(client, au, overrideValueId, ownership),
                Vector2Attribute v2 => new NeoAttributeVector2Writable(client, v2, overrideValueId, ownership),
                Vector2IntAttribute v2i => new NeoAttributeVector2IntWritable(client, v2i, overrideValueId, ownership),
                Vector3Attribute v3 => new NeoAttributeVector3Writable(client, v3, overrideValueId, ownership),
                Vector3IntAttribute v3i => new NeoAttributeVector3IntWritable(client, v3i, overrideValueId, ownership),
                _ => throw new System.ArgumentException(
                    $"Unknown attribute type {attribute.GetType().Name}", nameof(attribute)),
            };
        }

        private static bool IsWritableCompatible(Attribute attribute, NeoAttribute existing)
        {
            return attribute switch
            {
                NullAttribute => existing is NeoAttributeNull,
                BoolAttribute => existing is NeoAttributeBoolWritable,
                IntAttribute => existing is NeoAttributeIntWritable,
                FloatAttribute => existing is NeoAttributeFloatWritable,
                StringAttribute => existing is NeoAttributeStringWritable,
                DictionaryAttribute => existing is NeoAttributeDictionaryWritable,
                ListAttribute => existing is NeoAttributeListWritable,
                CustomAttribute => existing is NeoAttributeCustomWritable,
                EnumAttribute => existing is NeoAttributeEnumWritable,
                LookupAttribute => existing is NeoAttributeLookupWritable,
                NSGetterAttribute => existing is NeoAttributeNSGetter,
                FunctionAttribute => existing is NeoAttributeFunction,
                SpriteAttribute => existing is NeoAttributeSpriteWritable,
                AudioAttribute => existing is NeoAttributeAudioWritable,
                Vector2Attribute => existing is NeoAttributeVector2Writable,
                Vector2IntAttribute => existing is NeoAttributeVector2IntWritable,
                Vector3Attribute => existing is NeoAttributeVector3Writable,
                Vector3IntAttribute => existing is NeoAttributeVector3IntWritable,
                _ => false,
            };
        }
    }

    /// <summary>
    /// Read-only marker node for native Function attributes. Function
    /// attributes describe callable schema members and intentionally have no
    /// backing value row.
    /// </summary>
    public sealed class NeoAttributeFunction : NeoAttribute
    {
        public new FunctionAttribute attribute => (FunctionAttribute)base.attribute;

        public NeoAttributeFunction(
            NeoClient client,
            FunctionAttribute attribute,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership)
        {
            client.RegisterNode(this);
        }
    }

    /// <summary>
    /// Typed intermediate for the <see cref="NeoAttribute"/> hierarchy.
    /// Carries the kind-specific <typeparamref name="TAttribute"/> /
    /// <typeparamref name="TValue"/> bindings and overrides the
    /// abstract base properties with covariant typed returns (C# 9+),
    /// so a downcast through pattern-matching gives strongly-typed
    /// access without a second cast.
    ///
    /// <para>Subclasses extend this with the matching DTO pair, e.g.
    /// <c>NeoAttributeString : NeoAttribute&lt;StringAttribute, StringAttributeValue&gt;</c>.
    /// Read-only by default; the <c>*Writable</c> variants add a typed
    /// <c>Set</c> (or <c>Add</c>/<c>Insert</c>/<c>RemoveAt</c> for
    /// collection types) that funnels through
    /// <c>client.SetSaveValue</c> / <c>client.AddSaveValue</c>.</para>
    /// </summary>
    public abstract class NeoAttribute<TAttribute, TValue> : NeoAttribute
        where TAttribute : Attribute
        where TValue : AttributeValue
    {
        /// <summary>
        /// Typed accessor for <see cref="NeoAttribute.attribute"/>.
        /// Shadows the base via <c>new</c> — calls through a
        /// pattern-matched typed reference resolve here; calls
        /// through a plain <see cref="NeoAttribute"/> reference resolve
        /// to the base's untyped property. Both return the same
        /// underlying instance — the value is set once via the base
        /// ctor (get-only), no setter shadow needed.
        /// </summary>
        public new TAttribute attribute => (TAttribute)base.attribute;

        /// <summary>
        /// Typed accessor for <see cref="NeoAttribute.value"/>.
        /// Shadows the base via <c>new</c> for the same reason as
        /// <see cref="attribute"/>.
        /// </summary>
        public new TValue? value
        {
            get => (TValue?)base.value;
            protected set => base.value = value;
        }

        /// <summary>
        /// Resolves the current value-id via the chain:
        /// <c>overrideValueId</c> (the id this node was bound to by its
        /// parent's value-map) → static <c>attribute.valueId</c> (the
        /// authored default, used by the root and by directly-resolved
        /// attributes). Returns null if nothing is bound.
        ///
        /// <para>Value ids are stable instance identities: a Save/Session
        /// shadows an authored value at the <b>same</b> id, so resolution
        /// is identical for asset/save/session — the per-ownership choice
        /// of which row wins happens in
        /// <see cref="NeoClient.TryGetOverlaidValue"/>. There is no longer
        /// an <c>attributeValueOverrides</c> indirection hop.</para>
        /// </summary>
        protected string? valueId => overrideValueId ?? boundValueId ?? attribute.valueId;

        /// <summary>
        /// Id minted by <see cref="BindNewValue"/> for a <b>parentless</b>
        /// node that had no authored default (a standalone writable root in
        /// isolation — real saves hang every value off a root that carries
        /// an authored <c>valueId</c>). Kept in-memory on the node rather
        /// than in a shared attribute-keyed map, so it never mis-keys
        /// sibling collection items that share a template attribute id.
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
                if (valueId is null) return null;
                if (!client.TryGetOverlaidValue(ownership, valueId, out TValue? match)) return null;
                return match;
            }
        }

        public NeoAttribute(
            NeoClient client,
            TAttribute attribute,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership)
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

        public NeoAttribute(
            NeoClient client,
            string attributeId,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, ResolveAttribute(client, attributeId), overrideValueId, ownership)
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
            if (this is NeoAttributeDictionary
                || this is NeoAttributeList)
            {
                return;
            }
            OnValueIdChainChanged();
        }

        /// <summary>
        /// Called when this node's bound value row changes (its
        /// <see cref="valueId"/> was written, shadowed, or its shadow
        /// dropped). Default implementation refreshes <see cref="value"/>
        /// from <see cref="valueData"/> (so it tracks the row, including
        /// becoming null when the shadow is cleared and there is no authored
        /// default). Collection-type subclasses override to also re-walk
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
        /// Static helper for the attributeId-based ctor — resolves the
        /// <typeparamref name="TAttribute"/> from the client up-front so
        /// it can be passed to the base ctor (which initializes the
        /// get-only <see cref="NeoAttribute.attribute"/> property).
        /// </summary>
        private static TAttribute ResolveAttribute(NeoClient client, string attributeId)
        {
            if (!client.TryGetAttribute(attributeId, out TAttribute? resolved))
            {
                throw new System.ArgumentException(
                    $"No {typeof(TAttribute).Name} for attribute {attributeId}",
                    nameof(attributeId));
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
                    $"Cannot bind a new value on an asset-owned attribute '{attribute.id}'.");
            }
            client.SetWritableValue(ownership, newRow);
            value = newRow;
            if (parent is not null)
            {
                parent.BindChildValueId(this, newRow.id);
                return;
            }
            // Parentless root with no authored default — remember the minted
            // id on the node so its own resolution chain finds it.
            boundValueId = newRow.id;
        }

        /// <summary>
        /// Override on collection types to spin up children.
        /// Default no-op; the <c>BuildEmpty</c> path is for read-only
        /// instances where there's nothing to initialize. Saved
        /// variants may pre-allocate.
        /// </summary>
        virtual protected void BuildEmptyData() { }

        /// <summary>
        /// Override on collection types to walk the value's children
        /// and instantiate <see cref="NeoAttribute"/> instances under
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
