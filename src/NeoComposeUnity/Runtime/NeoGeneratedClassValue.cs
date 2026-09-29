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
        : NeoNode, IDisposable, INeoValuePayloadProvider, INeoValueReference
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
        private readonly string fallbackClassId;
        private bool isDisposed;
        private readonly List<IDisposable> subscriptions = new();
        private NeoMemberClassWritable? writableNodeCache;
        private Dictionary<(string key, Type type), (NeoMember? node, object view)>? storedViews;
        private bool isClassDefaultReference;
        private readonly string animationWrapperIdentity =
            System.Guid.NewGuid().ToString("N");
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
        internal string AnimationInstanceIdentity =>
            valueId ?? $"wrapper:{animationWrapperIdentity}";

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

        /// <summary>The temporary this view reads until it attaches; null once it has.</summary>
        internal NeoDetachedValue? PendingValue => detached;

        internal bool IsDisposed => isDisposed;

        private NeoMemberClass AttachDetachedNode()
        {
            if (isDisposed)
                throw new ObjectDisposedException(GetType().Name);
            NeoScriptObject value = detached!;
            string id = NSGetterEvaluator.AttachDetached(value, null);
            NeoMemberClass attached = NeoGeneratedTypesSupport.ClassValueNode(
                client,
                id,
                value.plan.classId,
                NeoValueOwnership.Session);
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
            if (storedViews is not null
                && storedViews.TryGetValue((key, typeof(TView)), out var cached)
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
            storedViews ??= new();
            storedViews[(key, typeof(TView))] = (member, view);
            return view;
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
            if (TryGetDetachedView(key, out NeoList<T>? cached))
                return cached;
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
            if (TryGetDetachedView(key, out NeoReadOnlyList<T>? cached))
                return cached;
            return CacheDetachedView(key, new NeoReadOnlyList<T>(client, this, key, readEntry, createItem));
        }

        private bool TryGetDetachedView<TView>(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TView? view)
            where TView : class
        {
            if (storedViews is not null
                && storedViews.TryGetValue((key, typeof(TView)), out var cached)
                && cached.node is null)
            {
                view = (TView)cached.view;
                return true;
            }
            view = null;
            return false;
        }

        private TView CacheDetachedView<TView>(string key, TView view) where TView : class
        {
            storedViews ??= new();
            storedViews[(key, typeof(TView))] = (null, view);
            return view;
        }

        protected void ThrowIfReadOnly(string memberName)
        {
            if (!IsReadOnly)
                return;
            throw new InvalidOperationException(
                $"Cannot write generated Neo member '{memberName}' because this {GetType().Name} value is read-only.");
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
            // A pending view never attached: nothing was registered for it,
            // and disposing it must not make rows.
            if (nodeStore is null)
                return;
            if (OwnsBackingValueLifetime)
                client.ReleaseAnimationClips(this);
            foreach (var subscription in subscriptions.ToArray())
            {
                subscription.Dispose();
            }
            subscriptions.Clear();
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
            if (node.member.id == member.id
                && node.overrideValueId == valueId
                && node.ownership == ownership)
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
            var previous = node;
            previous.OnChanged -= HandleNodeChanged;
            previous.OnDisposed -= HandleNodeDisposed;
            client.UnregisterGeneratedClassValue(this, previous);
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

            if (!ReferenceEquals(previous, next))
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
            return TrackSubscription(new NeoDisposableSubscription(
                () => node.OnChanged -= Handle));
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
            return TrackSubscription(new NeoDisposableSubscription(
                () => node.OnChanged -= Handle));
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
            return TrackSubscription(new NeoDisposableSubscription(
                () => node.OnChanged -= Handle));
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
            subscriptions.Add(subscription);
            return new NeoDisposableSubscription(() =>
            {
                subscription.Dispose();
                subscriptions.Remove(subscription);
            });
        }
    }
}
