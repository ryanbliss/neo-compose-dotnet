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
        /// True after <see cref="Dispose"/> has run. Subclasses must
        /// short-circuit further work (events, setter mutations) when
        /// disposed; consumers holding stale references shouldn't expect
        /// further updates.
        /// </summary>
        public bool isDisposed { get; private set; }

        protected NeoAttribute(NeoClient client, Attribute attribute, string? overrideValueId) : base(client)
        {
            this.attribute = attribute;
            this.overrideValueId = overrideValueId;
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
            client.UnregisterNode(this);
        }

        /// <summary>
        /// Read-only factory — instantiates the matching
        /// <c>NeoAttribute{Kind}</c> for the given attribute. Use
        /// <see cref="CreateSaved"/> when constructing a writeable
        /// sub-tree (e.g., descendants of <c>NeoClient.save</c>).
        ///
        /// <para>Returns the registry-cached instance for
        /// <paramref name="attribute"/> + <paramref name="overrideValueId"/>
        /// when one already exists — see
        /// <see cref="NeoClient.TryGetNode"/>. Construction is the
        /// fallback when nothing is cached. Mixing
        /// <see cref="Create"/> / <see cref="CreateSaved"/> for the same
        /// key returns whichever was first; callers wanting the
        /// writeable variant should bootstrap their sub-tree through
        /// <see cref="CreateSaved"/> from the root down.</para>
        /// </summary>
        public static NeoAttribute Create(
            NeoClient client,
            Attribute attribute,
            string? overrideValueId)
        {
            if (client.TryGetNode(attribute.id, overrideValueId, out NeoAttribute? existing))
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
                _ => throw new System.ArgumentException(
                    $"Unknown attribute type {attribute.GetType().Name}", nameof(attribute)),
            };
        }

        /// <summary>
        /// Writeable factory — instantiates <c>NeoAttribute{Kind}Saved</c>
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
        public static NeoAttribute CreateSaved(
            NeoClient client,
            Attribute attribute,
            string? overrideValueId)
        {
            if (client.TryGetNode(attribute.id, overrideValueId, out NeoAttribute? existing))
            {
                return existing;
            }
            return attribute switch
            {
                NullAttribute n => new NeoAttributeNull(client, n, overrideValueId),
                BoolAttribute b => new NeoAttributeBoolSaved(client, b, overrideValueId),
                IntAttribute i => new NeoAttributeIntSaved(client, i, overrideValueId),
                FloatAttribute f => new NeoAttributeFloatSaved(client, f, overrideValueId),
                StringAttribute s => new NeoAttributeStringSaved(client, s, overrideValueId),
                DictionaryAttribute d => new NeoAttributeDictionarySaved(client, d, overrideValueId),
                ListAttribute l => new NeoAttributeListSaved(client, l, overrideValueId),
                CustomAttribute c => new NeoAttributeCustomSaved(client, c, overrideValueId),
                EnumAttribute e => new NeoAttributeEnumSaved(client, e, overrideValueId),
                LookupAttribute lk => new NeoAttributeLookupSaved(client, lk, overrideValueId),
                NSGetterAttribute ng => new NeoAttributeNSGetter(client, ng, overrideValueId),
                _ => throw new System.ArgumentException(
                    $"Unknown attribute type {attribute.GetType().Name}", nameof(attribute)),
            };
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
    /// Read-only by default; the <c>*Saved</c> variants add a typed
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
        /// <c>overrideValueId</c> → save-side
        /// <c>attributeValueOverrides[attribute.id]</c> → static
        /// <c>attribute.valueId</c>. Returns null if nothing is bound.
        /// </summary>
        protected string? valueId
        {
            get
            {
                if (overrideValueId is not null) return overrideValueId;
                if (client.TryGetSaveOverrideValueId(attribute.id, out string? saveValId))
                {
                    return saveValId;
                }
                return attribute.valueId;
            }
        }

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
                if (!client.TryGetValue(valueId, out TValue? match)) return null;
                return match;
            }
        }

        public NeoAttribute(NeoClient client, TAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId)
        {
            InitFromValueData();
            // Subscribe before registering so the first save-override
            // change is observable from the moment the node exists.
            client.OnSaveOverrideChanged += HandleSaveOverrideChanged;
            // Last step in the base ctor — children walked from a
            // collection-type derived ctor body run after this, but they
            // register under their own keys, so registration order is
            // parent-then-children which is what consumers expect when
            // walking the registry.
            client.RegisterNode(this);
        }

        public NeoAttribute(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, ResolveAttribute(client, attributeId), overrideValueId)
        {
            InitFromValueData();
            client.OnSaveOverrideChanged += HandleSaveOverrideChanged;
            client.RegisterNode(this);
        }

        public override void Dispose()
        {
            if (isDisposed) return;
            client.OnSaveOverrideChanged -= HandleSaveOverrideChanged;
            base.Dispose();
        }

        /// <summary>
        /// Subscribed to <see cref="NeoClient.OnSaveOverrideChanged"/>;
        /// short-circuits if the changed attribute isn't this node's,
        /// otherwise forwards to the overridable
        /// <see cref="OnValueIdChainChanged"/> so collection types can
        /// also re-walk children.
        /// </summary>
        private void HandleSaveOverrideChanged(string changedAttributeId, string? newValueId)
        {
            if (changedAttributeId != attribute.id) return;
            OnValueIdChainChanged();
        }

        /// <summary>
        /// Called when the resolved <see cref="valueId"/> chain changes
        /// for this node — typically because
        /// <c>attributeValueOverrides[attribute.id]</c> in
        /// <see cref="ProjectSaveData"/> was added or removed. Default
        /// implementation refreshes <see cref="value"/> from
        /// <see cref="valueData"/> (so it tracks the new id, including
        /// becoming null when nothing is bound). Collection-type
        /// subclasses override to also re-walk their children.
        /// </summary>
        protected virtual void OnValueIdChainChanged()
        {
            // valueData reads through the resolution chain; if nothing
            // is bound any more, we end up with `value = null` —
            // matching the user-visible "valueId becomes null → value
            // becomes null" semantic.
            value = valueData;
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
        /// bound. Saved variants call this after creating a new
        /// top-level row so the cached <c>value</c> and any
        /// child-tree state are in sync.
        /// </summary>
        protected void RefreshFromValueData()
        {
            var data = valueData;
            if (data is not null) Initialize(data);
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
        }
    }
}
