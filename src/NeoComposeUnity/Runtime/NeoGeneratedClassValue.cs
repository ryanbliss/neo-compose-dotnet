// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public abstract class NeoGeneratedClassValue
        : NeoNode, IDisposable, INeoValuePayloadProvider, INeoValueReference
    {
        protected NeoMemberClass node { get; private set; }
        private readonly string fallbackClassId;
        private bool isDisposed;
        private readonly List<IDisposable> subscriptions = new();
        private NeoMemberClassWritable? writableNodeCache;
        private bool isClassDefaultReference;
        protected object? FunctionHandlerObject { get; set; }
        protected NeoValueOwnership InheritedStorageOwnership { get; private set; }
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
            if (string.IsNullOrWhiteSpace(sourceRecordId)) return null;
            return client.InternalRecordRelations.ResolveExactTargetId(
                relationKind,
                sourceRecordKind,
                sourceRecordId!,
                targetRecordKind);
        }

        public string? valueId => isClassDefaultReference
            ? null
            : node.overrideValueId ?? node.value?.id;
        public string? classId => node.value?.classId ?? fallbackClassId;
        public bool IsReadOnly { get; }
        internal NeoClient Client => client;
        internal NeoValueOwnership ValueOwnership => node.ownership;
        internal NeoRenderBindingStore RenderBindings { get; } = new();

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
            InheritedStorageOwnership = inheritedStorageOwnership;
            this.node.OnChanged += HandleNodeChanged;
            this.node.OnDisposed += HandleNodeDisposed;
            LazyInitialize();
        }

        protected void ThrowIfReadOnly(string memberName)
        {
            if (!IsReadOnly) return;
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
            if (isDisposed) return;
            isDisposed = true;
            foreach (var subscription in subscriptions.ToArray())
            {
                subscription.Dispose();
            }
            subscriptions.Clear();
            RenderBindings.Dispose();
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
            if (IsReadOnly) return;
            if (ownership == NeoValueOwnership.Asset) return;
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
            if (handler is null) throw new ArgumentNullException(nameof(handler));
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
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            void Handle(NeoMember changed)
            {
                if (node.TryGetSchemaKeyForChild(changed, out string? key) && key == field.Key)
                {
                    handler((T)readValue()!, client.CurrentChangeSource);
                }
            }
            node.OnChanged += Handle;
            return TrackSubscription(new NeoDisposableSubscription(
                () => node.OnChanged -= Handle));
        }

        protected IDisposable WatchChanges<TFields>(
            IReadOnlyDictionary<INeoField, Func<object?>> readers,
            Action<NeoChangedArgs<TFields>> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            void Handle(NeoMember changed)
            {
                var changes = new Dictionary<INeoField, object?>();
                if (node.TryGetSchemaKeyForChild(changed, out string? key))
                {
                    foreach (var pair in readers)
                    {
                        if (pair.Key.Key == key)
                        {
                            changes[pair.Key] = pair.Value();
                            break;
                        }
                    }
                }
                else
                {
                    foreach (var pair in readers)
                    {
                        changes[pair.Key] = pair.Value();
                    }
                }
                handler(new NeoChangedArgs<TFields>(changes, client.CurrentChangeSource));
            }
            node.OnChanged += Handle;
            return TrackSubscription(new NeoDisposableSubscription(
                () => node.OnChanged -= Handle));
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
