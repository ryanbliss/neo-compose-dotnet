// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime
{
    public abstract class NeoGeneratedCustomValue
        : NeoNode, IDisposable, INeoValuePayloadProvider, INeoValueReference
    {
        protected readonly NeoAttributeCustom node;
        private readonly string fallbackTypeId;
        private bool isDisposed;
        private readonly List<IDisposable> subscriptions = new();
        protected object? FunctionHandlerObject { get; set; }

        public string? valueId => node.overrideValueId ?? node.value?.id;

        protected NeoGeneratedCustomValue(
            NeoClient client,
            NeoAttributeCustom node,
            string fallbackTypeId)
            : base(client)
        {
            this.node = node;
            this.fallbackTypeId = fallbackTypeId;
            this.node.OnChanged += HandleNodeChanged;
            this.node.OnDisposed += HandleNodeDisposed;
            LazyInitialize();
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
            node.OnChanged -= HandleNodeChanged;
            node.OnDisposed -= HandleNodeDisposed;
            client.UnregisterGeneratedCustomValue(this, node);
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

        protected IDisposable WatchField<T>(
            NeoField<T> field,
            Action<T> handler,
            Func<object?> readValue)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            void Handle(NeoAttribute changed)
            {
                if (node.TryGetSchemaKeyForChild(changed, out string? key) && key == field.Key)
                {
                    handler((T)readValue()!);
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
                handler(new NeoChangedArgs<TFields>(changes));
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
