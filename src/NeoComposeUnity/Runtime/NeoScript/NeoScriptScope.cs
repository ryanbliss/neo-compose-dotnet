// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// A lexical NeoScript scope frame. Writes stay local while reads and
    /// read-only diagnostics walk the parent chain.
    /// </summary>
    internal sealed class NeoScriptScope
    {
        private readonly Dictionary<string, object?> bindings;
        private readonly Dictionary<string, List<string>> readOnlyBindings =
            new(StringComparer.Ordinal);

        internal NeoScriptScope(int capacity = 0)
        {
            bindings = new Dictionary<string, object?>(capacity, StringComparer.Ordinal);
        }

        internal NeoScriptScope(Dictionary<string, object?> rootBindings)
        {
            bindings = rootBindings
                ?? throw new ArgumentNullException(nameof(rootBindings));
        }

        private NeoScriptScope(NeoScriptScope parent, int capacity)
        {
            Parent = parent ?? throw new ArgumentNullException(nameof(parent));
            bindings = new Dictionary<string, object?>(capacity, StringComparer.Ordinal);
        }

        internal NeoScriptScope? Parent { get; }
        internal int LocalBindingCount => bindings.Count;

        internal object? this[string bindingId]
        {
            set => SetLocal(bindingId, value);
        }

        internal IEnumerable<string> Keys
        {
            get
            {
                var inherited = new HashSet<string>(StringComparer.Ordinal);
                if (Parent is not null)
                {
                    foreach (string bindingId in Parent.Keys)
                    {
                        inherited.Add(bindingId);
                        yield return bindingId;
                    }
                }
                foreach (string bindingId in bindings.Keys)
                {
                    if (!inherited.Contains(bindingId)) yield return bindingId;
                }
            }
        }

        internal NeoScriptScope CreateChild(int capacity = 0) =>
            new(this, capacity);

        internal bool ContainsLocal(string bindingId) =>
            bindings.ContainsKey(bindingId);

        internal void SetLocal(string bindingId, object? value) =>
            bindings[bindingId] = value;

        internal bool Remove(string bindingId) => bindings.Remove(bindingId);

        internal bool TryGetValue(string bindingId, out object? value)
        {
            if (bindings.TryGetValue(bindingId, out value)) return true;
            if (Parent is not null) return Parent.TryGetValue(bindingId, out value);
            value = null;
            return false;
        }

        internal void MarkReadOnly(string bindingId, string error)
        {
            if (!readOnlyBindings.TryGetValue(
                    bindingId,
                    out List<string>? errors))
            {
                errors = new List<string>();
                readOnlyBindings[bindingId] = errors;
            }
            errors.Add(error);
        }

        internal void UnmarkReadOnly(string bindingId)
        {
            if (!readOnlyBindings.TryGetValue(
                    bindingId,
                    out List<string>? errors))
            {
                return;
            }
            if (errors.Count > 0) errors.RemoveAt(errors.Count - 1);
            if (errors.Count == 0) readOnlyBindings.Remove(bindingId);
        }

        internal bool TryGetReadOnlyError(string bindingId, out string? error)
        {
            if (readOnlyBindings.TryGetValue(
                    bindingId,
                    out List<string>? errors)
                && errors.Count > 0)
            {
                error = errors[errors.Count - 1];
                return true;
            }
            if (Parent is not null)
            {
                return Parent.TryGetReadOnlyError(bindingId, out error);
            }
            error = null;
            return false;
        }

        internal Dictionary<string, object?> Materialize()
        {
            var result = Parent?.Materialize()
                ?? new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> binding in bindings)
            {
                result[binding.Key] = binding.Value;
            }
            return result;
        }
    }
}
