// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime
{
    public abstract class NeoGeneratedCustomValue
        : NeoNode, IDisposable, INeoValuePayloadProvider
    {
        protected readonly NeoAttributeCustom node;
        private readonly string fallbackTypeId;
        private bool isDisposed;

        public event Action? OnChanged;
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
        }

        public virtual void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            node.OnChanged -= HandleNodeChanged;
            node.OnDisposed -= HandleNodeDisposed;
        }

        NeoValuePayload INeoValuePayloadProvider.ToNeoValuePayload()
        {
            return NeoGeneratedTypesSupport.ValuePayload(node, fallbackTypeId);
        }

        private void HandleNodeChanged(NeoAttribute changed)
        {
            OnChanged?.Invoke();
        }

        private void HandleNodeDisposed(NeoAttribute disposed)
        {
            Dispose();
        }
    }
}
