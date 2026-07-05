// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public abstract class NeoGeneratedCustomValue
        : NeoNode, IDisposable, INeoValuePayloadProvider, INeoValueReference
    {
        protected NeoAttributeCustom node { get; private set; }
        private readonly string fallbackTypeId;
        private bool isDisposed;
        private readonly List<IDisposable> subscriptions = new();
        private NeoAttributeCustomWritable? writableNodeCache;
        protected object? FunctionHandlerObject { get; set; }
        protected NeoValueOwnership InheritedStorageOwnership { get; private set; }
        protected NeoAttributeCustomWritable writableNode =>
            writableNodeCache ??= NeoGeneratedTypesSupport.AsWritable(node, InheritedStorageOwnership);

        public string? valueId => node.overrideValueId ?? node.value?.id;
        public string? typeId => node.value?.typeId ?? fallbackTypeId;
        public bool IsReadOnly { get; }
        internal NeoClient Client => client;
        internal NeoRenderBindingStore RenderBindings { get; } = new();

        protected NeoGeneratedCustomValue(
            NeoClient client,
            NeoAttributeCustom node,
            string fallbackTypeId,
            bool isReadOnly = true,
            NeoValueOwnership inheritedStorageOwnership = NeoValueOwnership.Asset)
            : base(client)
        {
            this.node = node;
            this.fallbackTypeId = fallbackTypeId;
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
            client.UnregisterGeneratedCustomValue(this, node);
        }

        internal void RetargetWritableReference(
            CustomAttribute attribute,
            string valueId,
            NeoValueOwnership ownership)
        {
            if (IsReadOnly) return;
            if (ownership == NeoValueOwnership.Asset) return;
            if (node.attribute.id == attribute.id
                && node.overrideValueId == valueId
                && node.ownership == ownership)
            {
                InheritedStorageOwnership = ownership;
                return;
            }

            var next = NeoAttribute.CreateWritable(
                client,
                attribute,
                valueId,
                ownership) as NeoAttributeCustomWritable;
            if (next is null)
            {
                throw new InvalidOperationException(
                    $"Cannot retarget generated value '{GetType().Name}' to non-custom attribute '{attribute.id}'.");
            }

            var previous = node;
            previous.OnChanged -= HandleNodeChanged;
            previous.OnDisposed -= HandleNodeDisposed;
            client.UnregisterGeneratedCustomValue(this, previous);
            if (writableNodeCache is not null && !ReferenceEquals(writableNodeCache, previous))
            {
                writableNodeCache.Dispose();
            }

            node = next;
            InheritedStorageOwnership = ownership;
            writableNodeCache = next;
            node.OnChanged += HandleNodeChanged;
            node.OnDisposed += HandleNodeDisposed;
            client.RegisterGeneratedCustomValue(this, node);

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
            return NeoGeneratedTypesSupport.ValuePayload(node, fallbackTypeId);
        }

        private void HandleNodeChanged(NeoAttribute changed)
        {
            // Subscriptions are registered through generated OnChanged
            // methods. This root listener keeps the generated wrapper alive
            // as the single owner of child subscriptions.
        }

        private void HandleNodeDisposed(NeoAttribute disposed)
        {
            Dispose();
        }

        internal IDisposable WatchAnyChange(
            Action<NeoGeneratedCustomValue, NeoAttribute, NeoChangeSource> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            void Handle(NeoAttribute changed)
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
            void Handle(NeoAttribute changed)
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
            void Handle(NeoAttribute changed)
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
