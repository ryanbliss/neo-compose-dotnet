// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime
{
    internal sealed class NeoRenderBindingStore : IDisposable
    {
        private readonly Dictionary<object, IDisposable> bindings = new();

        public void Set(object owner, IDisposable binding)
        {
            if (owner is null) throw new ArgumentNullException(nameof(owner));
            if (binding is null) throw new ArgumentNullException(nameof(binding));

            Remove(owner);
            bindings[owner] = binding;
        }

        public bool Remove(object owner)
        {
            if (owner is null) return false;
            if (!bindings.Remove(owner, out var binding)) return false;
            binding.Dispose();
            return true;
        }

        public void Dispose()
        {
            foreach (var binding in bindings.Values)
            {
                binding.Dispose();
            }
            bindings.Clear();
        }
    }
}
