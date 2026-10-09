// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    public abstract class NeoGeneratedClassValue
        : NeoNode, IDisposable, INeoValuePayloadProvider, INeoValueReference, INeoWritableValueListener
    {
        /// <summary>
        /// The backing node. A view over a pending NeoScript temporary has
        /// none until something needs a row; reading this attaches it.
        /// </summary>
        protected NeoMemberClass node
        {
            get => nodeStore ?? AttachDetachedNode();
            private set => nodeStore = value;
        }
        private NeoMemberClass? nodeStore;
        /// <summary>The pending temporary this view reads until it attaches.</summary>
        private NeoScriptObject? detached;
        // The registry key a view claimed when its temporary's rows attached,
        // until its node takes it over. The view watches the root row as
        // long as it holds the key.
        private NeoNodeKey? attachedRegistryKey;
        private readonly string fallbackClassId;
        private bool isDisposed;
        // Most views never subscribe; the list comes with the first.
        private List<IDisposable>? subscriptions;
        private NeoMemberClassWritable? writableNodeCache;
        // A view's member views, by key and view type. A class has few and
        // generated accessors read them by literal key, so a scan by
        // reference beats hashing the key and the type.
        private sealed class StoredView
        {
            internal readonly string key;
            internal readonly Type type;
            internal NeoMember? node;
            internal object view;

            internal StoredView(string key, Type type, NeoMember? node, object view)
            {
                this.key = key;
                this.type = type;
                this.node = node;
                this.view = view;
            }
        }

        private List<StoredView>? storedViews;
        private bool isClassDefaultReference;
        // Minted on first use: only an animated view without a row needs it.
        private string? animationWrapperIdentity;
        protected object? FunctionHandlerObject
        {
            get; set;
        }
        protected NeoValueOwnership InheritedStorageOwnership
        {
            get; private set;
        }
        protected NeoMemberClassWritable writableNode =>
            writableNodeCache ??= NeoGeneratedTypesSupport.AsWritable(node, InheritedStorageOwnership);

        /// <summary>
        /// Resolves an exact internal-record relation declared from this
        /// wrapper's backing record. Unlike <see cref="valueId"/>, the source
        /// identity remains available to generated class-default wrappers.
        /// </summary>
        protected string? ResolveExactInternalRecordRelationTarget(
            string relationKind,
            string sourceRecordKind,
            string targetRecordKind)
        {
            string? sourceRecordId = node.overrideValueId ?? node.value?.id;
            if (string.IsNullOrWhiteSpace(sourceRecordId))
                return null;
            return client.InternalRecordRelations.ResolveExactTargetId(
                relationKind,
                sourceRecordKind,
                sourceRecordId!,
                targetRecordKind);
        }

        public string? valueId => isClassDefaultReference
            ? null
            : nodeStore is null
                ? AttachDetachedRows()
                : node.overrideValueId ?? node.value?.id;
        public string? classId => detached?.plan.classId ?? node.ClassId;
        internal ClassMember BackingMember => node.member;
        public bool IsReadOnly
        {
            get;
        }
        internal NeoClient Client => client;
        internal NeoValueOwnership ValueOwnership => node.ownership;
        internal NeoMemberClass BackingNode => node;
        internal NeoMemberClassWritable WritableBackingNode => writableNode;
        /// <summary>The store this view's calls write through, as <see cref="InvokeFunction(string, object?[])"/> uses.</summary>
        internal NeoValueOwnership StorageOwnership => InheritedStorageOwnership;
        internal string AnimationInstanceIdentity =>
            valueId ?? $"wrapper:{animationWrapperIdentity ??= System.Guid.NewGuid().ToString("N")}";

        internal void MarkClassDefaultReference()
        {
            isClassDefaultReference = true;
        }

        protected NeoGeneratedClassValue(
            NeoClient client,
            NeoMemberClass node,
            string fallbackClassId,
            bool isReadOnly = true,
            NeoValueOwnership inheritedStorageOwnership = NeoValueOwnership.Asset)
            : base(client)
        {
            this.node = node;
            this.fallbackClassId = fallbackClassId;
            IsReadOnly = isReadOnly;
            // A read-only generated interface is still a view of its backing
            // store. Its factory's default Asset argument must not redirect
            // computed members or methods away from a Save/Session receiver.
            InheritedStorageOwnership = inheritedStorageOwnership == NeoValueOwnership.Asset
                ? node.ownership : inheritedStorageOwnership;
            this.node.OnChanged += HandleNodeChanged;
            this.node.OnDisposed += HandleNodeDisposed;
            LazyInitialize();
            // A view over a value no member holds (C# `new`, a clone) is the
            // row's identity: NeoScript calling back into the row must reach
            // this wrapper's FunctionHandler. Never evict an existing view.
            if (node.member.unplaced)
                client.ClaimGeneratedClassValue(this, node);
        }

        /// <summary>
        /// A Session view over a NeoScript temporary that has not become rows.
        /// Stored members read its slots through <see cref="TryReadDetached"/>;
        /// anything else attaches it.
        /// </summary>
        protected NeoGeneratedClassValue(NeoClient client, NeoDetachedValue value, bool isReadOnly)
            : base(client)
        {
            detached = (NeoScriptObject)value;
            fallbackClassId = detached.plan.classId;
            IsReadOnly = isReadOnly;
            InheritedStorageOwnership = NeoValueOwnership.Session;
            LazyInitialize();
        }

        /// <summary>
        /// Reads stored member <paramref name="key"/> of a pending temporary
        /// without making rows. False once attached, or for a member only a
        /// row can answer; the caller then reads <see cref="node"/>.
        /// </summary>
        protected internal bool TryReadDetached(string key, out object? value)
        {
            if (detached is null)
            {
                value = null;
                return false;
            }
            return NSGetterEvaluator.TryReadDetachedView(detached, key, out value);
        }

        /// <summary>The temporary this view reads until it builds its node; its rows may already be attached.</summary>
        internal NeoDetachedValue? PendingValue => detached;

        /// <summary>
        /// Reads single-select lookup <paramref name="key"/> of a pending
        /// temporary without making rows: the selected entry, or null when
        /// unset. False once it attached; the caller then reads its row.
        /// </summary>
        protected bool TryReadDetachedLookup(string key, out NeoMember? selected)
        {
            selected = null;
            if (!TryReadDetached(key, out object? value))
                return false;
            if (NeoGeneratedTypesSupport.ReadSelectedId(value) is { } id)
            {
                var lookup = (LookupMember)detached!.plan.slots[detached.plan.slotByKey[key]].member;
                selected = NeoMemberLookup.ResolveSelected(client, lookup, id);
            }
            return true;
        }

        /// <summary>
        /// Computes NSProperty <paramref name="key"/> on this view's receiver.
        /// A pending temporary computes as itself, so the read makes no rows.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        protected NSGetterResult ComputeProperty(string key)
        {
            if (detached is { attachedId: null } pending)
                return NSGetterEvaluator.ComputeDetachedProperty(pending, key);
            return writableNode.Get<NeoMemberNSProperty>(key).Compute(valueId!);
        }

        // Generated calls pass their arguments positionally, so a call
        // allocates no argument array: the scope copies the arguments in.
        protected object? InvokeFunction(string key) => InvokeFunction(key, Array.Empty<object?>());

        protected object? InvokeFunction(string key, object? arg0)
        {
            object?[] args = NeoArgumentArrays.Rent(1);
            args[0] = arg0;
            try
            {
                return InvokeFunction(key, args);
            }
            finally
            {
                NeoArgumentArrays.Return(args);
            }
        }

        protected object? InvokeFunction(string key, object? arg0, object? arg1)
        {
            object?[] args = NeoArgumentArrays.Rent(2);
            args[0] = arg0;
            args[1] = arg1;
            try
            {
                return InvokeFunction(key, args);
            }
            finally
            {
                NeoArgumentArrays.Return(args);
            }
        }

        protected object? InvokeFunction(string key, object? arg0, object? arg1, object? arg2)
        {
            object?[] args = NeoArgumentArrays.Rent(3);
            args[0] = arg0;
            args[1] = arg1;
            args[2] = arg2;
            try
            {
                return InvokeFunction(key, args);
            }
            finally
            {
                NeoArgumentArrays.Return(args);
            }
        }

        protected object? InvokeFunction(string key, object? arg0, object? arg1, object? arg2, object? arg3)
        {
            object?[] args = NeoArgumentArrays.Rent(4);
            args[0] = arg0;
            args[1] = arg1;
            args[2] = arg2;
            args[3] = arg3;
            try
            {
                return InvokeFunction(key, args);
            }
            finally
            {
                NeoArgumentArrays.Return(args);
            }
        }

        /// <summary>
        /// Calls immediate instance NSFunction <paramref name="key"/> on this
        /// view's receiver. A pending temporary is called as itself, so the
        /// call makes no rows.
        /// </summary>
        protected object? InvokeFunction(string key, object?[] args)
        {
            if (detached is { attachedId: null } pending)
                return NeoNSFunctionRuntime.InvokeDetached(pending, key, args);
            if (valueId is null)
            {
                throw new InvalidOperationException(
                    $"Cannot invoke NSFunction '{key}' without a backing receiver value id.");
            }
            // The writable view keeps an inherited Save/Session ownership.
            return writableNode.Get<NeoMemberNSFunction>(key).Invoke(valueId!, args);
        }

        internal bool IsDisposed => isDisposed;

        /// <summary>
        /// Attaches the pending temporary's rows and claims the registry key
        /// its node will have, without building the node: an id is often all
        /// a caller needs, and a list add retargets the view to the entry's
        /// own node anyway.
        /// </summary>
        private string AttachDetachedRows()
        {
            NeoScriptObject value = detached!;
            // An attached view keeps its id after disposal, as a node-backed one does.
            if (value.attachedId is string attached && (attachedRegistryKey is not null || isDisposed))
                return attached;
            if (isDisposed)
                throw new ObjectDisposedException(GetType().Name);
            string id = NSGetterEvaluator.AttachDetached(value, null);
            if (attachedRegistryKey is null)
            {
                var key = new NeoNodeKey(
                    NeoGeneratedTypesSupport.UnplacedClassMemberId(value.plan.classId),
                    id,
                    NeoValueOwnership.Session);
                attachedRegistryKey = key;
                client.RegisterGeneratedClassValue(this, key);
                client.AddWritableValueListener(id, this);
            }
            return id;
        }

        // A node disposes its view when its row is removed; a view without one watches the row itself.
        void INeoWritableValueListener.OnWritableValueChanged(NeoValueOwnership ownership, string changedValueId)
        {
            if (ownership == NeoValueOwnership.Session
                && !client.TryGetOverlaidValue(ownership, changedValueId, out MemberValue? _))
            {
                Dispose();
            }
        }

        private void ReleaseAttachedRows()
        {
            if (attachedRegistryKey is not NeoNodeKey key)
                return;
            client.RemoveWritableValueListener(key.valueId!, this);
            client.UnregisterGeneratedClassValue(this, key);
            attachedRegistryKey = null;
        }

        private NeoMemberClass AttachDetachedNode()
        {
            NeoScriptObject value = detached!;
            string id = AttachDetachedRows();
            NeoMemberClass attached = NeoGeneratedTypesSupport.ClassValueNode(
                client,
                id,
                value.plan.classId,
                NeoValueOwnership.Session);
            ReleaseAttachedRows();
            nodeStore = attached;
            detached = null;
            attached.OnChanged += HandleNodeChanged;
            attached.OnDisposed += HandleNodeDisposed;
            client.RegisterGeneratedClassValue(this, attached);
            return attached;
        }

        // Views read their current backing node; cache the wrapper, never its value.
        // Owner-local entries preserve the permission callbacks of writable views.
        protected bool TryGetStoredView<TView>(
            string key, NeoMember member, out TView view) where TView : class
        {
            if (FindStoredView(key, typeof(TView)) is { } cached
                && ReferenceEquals(cached.node, member)
                && !member.isDisposed)
            {
                view = (TView)cached.view;
                return true;
            }
            view = null!;
            return false;
        }

        protected TView CacheStoredView<TView>(
            string key, NeoMember member, TView view) where TView : class
        {
            StoreView(key, typeof(TView), member, view);
            return view;
        }

        private StoredView? FindStoredView(string key, Type type)
        {
            if (storedViews is null)
                return null;
            for (int i = 0; i < storedViews.Count; i++)
            {
                StoredView entry = storedViews[i];
                if (ReferenceEquals(entry.key, key) && ReferenceEquals(entry.type, type))
                    return entry;
            }
            // A key built at runtime matches by value.
            for (int i = 0; i < storedViews.Count; i++)
            {
                StoredView entry = storedViews[i];
                if (ReferenceEquals(entry.type, type) && entry.key == key)
                    return entry;
            }
            return null;
        }

        private void StoreView(string key, Type type, NeoMember? member, object view)
        {
            if (FindStoredView(key, type) is { } entry)
            {
                entry.node = member;
                entry.view = view;
                return;
            }
            (storedViews ??= new List<StoredView>()).Add(new StoredView(key, type, member, view));
        }

        /// <summary>A pending view's List member, reading its slot until the owner attaches.</summary>
        protected NeoList<T> DetachedList<T>(
            string key,
            Func<object?, T> readEntry,
            Func<NeoClient, NeoMember, T> createItem,
            Func<T, NeoValueWritePayload?> serializeItem,
            Action? beforeWrite = null,
            Func<bool>? isReadOnly = null)
        {
            return CacheDetachedView(key, new NeoList<T>(
                client,
                this,
                key,
                readEntry,
                createItem,
                serializeItem,
                beforeWrite,
                isReadOnly));
        }

        /// <inheritdoc cref="DetachedList{T}"/>
        protected NeoReadOnlyList<T> DetachedReadOnlyList<T>(
            string key,
            Func<object?, T> readEntry,
            Func<NeoClient, NeoMember, T> createItem)
        {
            return CacheDetachedView(key, new NeoReadOnlyList<T>(client, this, key, readEntry, createItem));
        }

        /// <summary>The cached view a <see cref="DetachedList{T}"/> call made, checked first so a repeat read allocates nothing.</summary>
        protected bool TryGetDetachedView<TView>(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TView? view)
            where TView : class
        {
            if (FindStoredView(key, typeof(TView)) is { node: null } cached)
            {
                view = (TView)cached.view;
                return true;
            }
            view = null;
            return false;
        }

        private TView CacheDetachedView<TView>(string key, TView view) where TView : class
        {
            StoreView(key, typeof(TView), null, view);
            return view;
        }

        protected void ThrowIfReadOnly(string memberName)
        {
            if (!IsReadOnly)
                return;
            throw new InvalidOperationException(
                $"Cannot write generated Neo member '{memberName}' because this {GetType().Name} value is read-only.");
        }

        /// <summary>A readonly collection's membership is set only at construction.</summary>
        protected static void ThrowReadOnlyMembership(string memberName)
        {
            throw new InvalidOperationException(
                $"Cannot change the entries of readonly Neo member '{memberName}'. It is set only at construction.");
        }

        public bool TryWritable<TWritable>(out TWritable writable)
            where TWritable : class, INeoValueReference
        {
            if (!IsReadOnly && this is TWritable match)
            {
                writable = match;
                return true;
            }

            writable = null!;
            return false;
        }

        public virtual void Dispose()
        {
            if (isDisposed)
                return;
            isDisposed = true;
            storedViews?.Clear();
            // A pending view has no node, and disposing it must not make rows.
            if (nodeStore is null)
            {
                ReleaseAttachedRows();
                return;
            }
            if (OwnsBackingValueLifetime)
                client.ReleaseAnimationClips(this);
            if (subscriptions is not null)
            {
                foreach (var subscription in subscriptions.ToArray())
                {
                    subscription.Dispose();
                }
                subscriptions.Clear();
            }
            node.OnChanged -= HandleNodeChanged;
            node.OnDisposed -= HandleNodeDisposed;
            if (writableNodeCache is not null && !ReferenceEquals(writableNodeCache, node))
            {
                writableNodeCache.Dispose();
            }
            writableNodeCache = null;
            client.UnregisterGeneratedClassValue(this, node);
        }

        internal void RetargetWritableReference(
            ClassMember member,
            string valueId,
            NeoValueOwnership ownership)
        {
            if (IsReadOnly)
                return;
            if (ownership == NeoValueOwnership.Asset)
                return;
            // An attached view that never built its node retargets without one.
            NeoMemberClass? previous = nodeStore;
            if (previous is not null
                && previous.member.id == member.id
                && previous.overrideValueId == valueId
                && previous.ownership == ownership)
            {
                InheritedStorageOwnership = ownership;
                return;
            }

            var next = NeoMember.CreateWritable(
                client,
                member,
                valueId,
                ownership) as NeoMemberClassWritable;
            if (next is null)
            {
                throw new InvalidOperationException(
                    $"Cannot retarget generated value '{GetType().Name}' to non-class member '{member.id}'.");
            }

            storedViews?.Clear();
            if (previous is not null)
            {
                previous.OnChanged -= HandleNodeChanged;
                previous.OnDisposed -= HandleNodeDisposed;
                client.UnregisterGeneratedClassValue(this, previous);
            }
            else
            {
                ReleaseAttachedRows();
            }
            detached = null;
            if (writableNodeCache is not null && !ReferenceEquals(writableNodeCache, previous))
            {
                writableNodeCache.Dispose();
            }

            node = next;
            InheritedStorageOwnership = ownership;
            writableNodeCache = next;
            node.OnChanged += HandleNodeChanged;
            node.OnDisposed += HandleNodeDisposed;
            client.RegisterGeneratedClassValue(this, node);

            if (previous is not null && !ReferenceEquals(previous, next))
            {
                previous.Dispose();
            }
        }

        /// <summary>
        /// Optionally use to lazy initialize class data.
        /// Useful for non-generated partial class members to do their own initialization even when internal constructor is used.
        /// </summary>
        protected virtual void LazyInitialize()
        {
            // Do nothing by default
        }

        /// <summary>
        /// Whether disposing this wrapper should release everything keyed to
        /// the BACKING VALUE — currently the compiled animation clips, which
        /// <see cref="NeoClient.ReleaseAnimationClips"/> drops by value
        /// identity, not by wrapper identity.
        ///
        /// <para>True for every generated class: a generated wrapper is the
        /// value's representative, so its disposal is the value going away.
        /// A short-lived wrapper minted over a value someone else owns must
        /// override this to false, or disposing it would stop the real owner's
        /// running animations and throw away definitions it is still using
        /// (P67 §7.2's variant-application adapter is the case in point).</para>
        /// </summary>
        protected virtual bool OwnsBackingValueLifetime => true;

#if UNITY_EDITOR
        public virtual void OnDidSynchronize()
        {
            // Do nothing by default
        }

#endif

        NeoValuePayload INeoValuePayloadProvider.ToNeoValuePayload()
        {
            return NeoGeneratedTypesSupport.ValuePayload(node, fallbackClassId);
        }

        private void HandleNodeChanged(NeoMember changed)
        {
            // Subscriptions are registered through generated OnChanged
            // methods. This root listener keeps the generated wrapper alive
            // as the single owner of child subscriptions.
        }

        private void HandleNodeDisposed(NeoMember disposed)
        {
            Dispose();
        }

        internal IDisposable WatchAnyChange(
            Action<NeoGeneratedClassValue, NeoMember, NeoChangeSource> handler)
        {
            if (handler is null)
                throw new ArgumentNullException(nameof(handler));
            void Handle(NeoMember changed)
            {
                handler(this, changed, client.CurrentChangeSource);
            }
            node.OnChanged += Handle;
            // A handler that hears every change reads what it needs itself,
            // which records the reads its getters will be heard by.
            IDisposable? getters = WatchGetters();
            return TrackSubscription(new NeoDisposableSubscription(() =>
            {
                node.OnChanged -= Handle;
                getters?.Dispose();
            }));
        }

        protected IDisposable WatchField<T>(
            NeoField<T> field,
            Action<T, NeoChangeSource> handler,
            Func<object?> readValue)
        {
            if (handler is null)
                throw new ArgumentNullException(nameof(handler));
            void Handle(NeoMember changed)
            {
                if (!CanReadChange())
                    return;
                if (node.TryGetSchemaKeyForChild(changed, out string? key) && key == field.Key)
                {
                    handler((T)readValue()!, client.CurrentChangeSource);
                }
            }
#if ENABLE_MONO
            // Prepare the typed callback when it is bound, without reading or
            // invoking user code. Mono otherwise JITs its generic trampoline
            // on the first gameplay notification (about 18 ms in Neowyn).
            Action<NeoMember> callback = Handle;
            _ = callback.Method.MethodHandle.GetFunctionPointer();
#endif
            node.OnChanged += Handle;
            IDisposable? getters = WatchGetters();
            if (getters is not null)
                ReadGetter(field.Key);
            return TrackSubscription(new NeoDisposableSubscription(() =>
            {
                node.OnChanged -= Handle;
                getters?.Dispose();
            }));
        }

        protected IDisposable WatchChanges<TFields>(
            IReadOnlyDictionary<INeoField, Func<object?>> readers,
            Action<NeoChangedArgs<TFields>> handler)
        {
            if (handler is null)
                throw new ArgumentNullException(nameof(handler));
            var readersByKey = new Dictionary<string, KeyValuePair<INeoField, Func<object?>>>(StringComparer.Ordinal);
            var orderedReaders = new KeyValuePair<INeoField, Func<object?>>[readers.Count];
            int readerIndex = 0;
            foreach (var pair in readers)
            {
                orderedReaders[readerIndex++] = pair;
                // Preserve first-match behavior if a caller provides distinct
                // field tokens carrying the same schema key.
                if (!readersByKey.ContainsKey(pair.Key.Key))
                    readersByKey.Add(pair.Key.Key, pair);
            }
            void Handle(NeoMember changed)
            {
                if (!CanReadChange())
                    return;
                if (node.TryGetSchemaKeyForChild(changed, out string? key)
                    && readersByKey.TryGetValue(key, out var reader))
                {
                    handler(new NeoChangedArgs<TFields>(reader.Key, reader.Value(), client.CurrentChangeSource));
                    return;
                }
                var changes = new Dictionary<INeoField, object?>();
                if (key is null)
                    foreach (var pair in orderedReaders)
                        changes[pair.Key] = pair.Value();
                handler(new NeoChangedArgs<TFields>(changes, client.CurrentChangeSource));
            }
            node.OnChanged += Handle;
            IDisposable? getters = WatchGetters();
            if (getters is not null)
                foreach (var pair in orderedReaders)
                    ReadGetter(pair.Key.Key);
            return TrackSubscription(new NeoDisposableSubscription(() =>
            {
                node.OnChanged -= Handle;
                getters?.Dispose();
            }));
        }

        /// <summary>
        /// Raises this view's node when a getter on its row loses its
        /// memoized result because something it read changed. A getter is
        /// heard once per read, so each handler reads it again.
        /// </summary>
        private IDisposable? WatchGetters() =>
            isClassDefaultReference || valueId is not string id
                ? null
                : client.WatchGetters(id, node);

        /// <summary>Reads getter <paramref name="key"/>, if it is one, so its reads are recorded.</summary>
        private void ReadGetter(string key)
        {
            if (node.TryGet<NeoMemberNSProperty>(key, out _))
                _ = ComputeProperty(key);
        }

        private bool CanReadChange()
        {
            // Grid dependencies publish after row removal but before every
            // view/listener has been retired. Never evaluate a dead receiver.
            return !isDisposed && (node.overrideValueId is not string id
                || client.TryGetValue(node.ownership, id, out MemberValue? _));
        }

        private IDisposable TrackSubscription(IDisposable subscription)
        {
            List<IDisposable> tracked = subscriptions ??= new List<IDisposable>();
            tracked.Add(subscription);
            return new NeoDisposableSubscription(() =>
            {
                subscription.Dispose();
                tracked.Remove(subscription);
            });
        }
    }
}
