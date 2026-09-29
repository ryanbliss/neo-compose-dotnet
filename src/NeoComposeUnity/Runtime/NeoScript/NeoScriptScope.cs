// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using EvaluationValue = NeoCompose.Runtime.NeoScript.NSGetterEvaluator.ArithmeticValue;

namespace NeoCompose.Runtime.NeoScript
{
    internal sealed class NeoScriptVariableBinding
    {
        internal readonly NeoScriptScopeLayout Layout;
        internal readonly int Slot;
        internal NeoScriptVariableBinding(NeoScriptScopeLayout layout, int slot)
        {
            Layout = layout;
            Slot = slot;
        }
    }

    internal sealed class NeoScriptScopeLayout
    {
        internal readonly Dictionary<string, int> Slots = new(StringComparer.Ordinal);
        internal NeoScriptScopeLayout(FunctionWithReturnType body)
        {
            // Parameter i is slot i, so a call binds its arguments by position.
            foreach (var parameter in body.parameters ?? Array.Empty<Variable>())
            {
                if (Slots.ContainsKey(parameter.id))
                    throw new InvalidOperationException(
                        $"NeoScript body declares parameter '{parameter.id}' twice; its compiled IR is stale or corrupt.");
                Slots.Add(parameter.id, Slots.Count);
            }
            // Nested/dynamic bindings continue through the general scope path.
            // The common function-level locals have stable slots per body.
            foreach (var instruction in body.instructions)
                if (instruction is VariableInstruction variable)
                    Add(variable.variable.id);
        }
        private void Add(string id)
        {
            if (!Slots.ContainsKey(id))
                Slots.Add(id, Slots.Count);
        }
    }

    /// <summary>
    /// A lexical NeoScript scope frame. Writes stay local while reads and
    /// read-only diagnostics walk the parent chain.
    /// </summary>
    internal sealed class NeoScriptScope
    {
        private readonly Dictionary<string, EvaluationValue>? bindings;
        private readonly Dictionary<string, object?>? externalBindings;
        private Dictionary<string, List<string>>? readOnlyBindings;
        private NeoScriptScopeLayout? layout;
        private EvaluationValue[] slots = Array.Empty<EvaluationValue>();
        private bool[] occupied = Array.Empty<bool>();
        private int occupiedCount;

        internal void UseLayout(NeoScriptScopeLayout prepared)
        {
            layout = prepared;
            if (slots.Length < prepared.Slots.Count)
            {
                slots = new EvaluationValue[prepared.Slots.Count];
                occupied = new bool[prepared.Slots.Count];
            }
        }

        internal NeoScriptScope(int capacity = 0)
        {
            bindings = new Dictionary<string, EvaluationValue>(capacity, StringComparer.Ordinal);
        }

        internal NeoScriptScope(Dictionary<string, object?> rootBindings)
        {
            externalBindings = rootBindings ?? throw new ArgumentNullException(nameof(rootBindings));
        }

        private NeoScriptScope(NeoScriptScope parent, int capacity, bool block = false)
        {
            Parent = parent ?? throw new ArgumentNullException(nameof(parent));
            bindings = new Dictionary<string, EvaluationValue>(capacity, StringComparer.Ordinal);
            this.block = block;
        }

        // A statement block's locals end with it, while its assignments to
        // enclosing bindings land where those bindings were declared.
        private readonly bool block;

        internal NeoScriptScope? Parent
        {
            get;
            private set;
        }

        /// <summary>Adopts a pooled scope as a child of <paramref name="parent"/>, or releases it.</summary>
        internal void BindParent(NeoScriptScope? parent) => Parent = parent;
        internal int LocalBindingCount => externalBindings?.Count ?? bindings!.Count + occupiedCount;
        internal int BindingCapacity => externalBindings?.EnsureCapacity(0) ?? bindings!.EnsureCapacity(0) + slots.Length;

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
                foreach (string bindingId in LocalKeys)
                {
                    if (!inherited.Contains(bindingId))
                        yield return bindingId;
                }
            }
        }

        private IEnumerable<string> LocalKeys
        {
            get
            {
                if (externalBindings is not null)
                {
                    foreach (var key in externalBindings.Keys)
                        yield return key;
                }
                else
                {
                    foreach (var key in bindings!.Keys)
                        yield return key;
                    if (layout is not null)
                        foreach (var pair in layout.Slots)
                            if (occupied[pair.Value])
                                yield return pair.Key;
                }
            }
        }

        internal NeoScriptScope CreateChild(int capacity = 0) =>
            new(this, capacity);

        internal NeoScriptScope CreateBlock() =>
            new(this, 0, block: true);

        /// <summary>
        /// Assigns an existing binding in the scope that declared it, when
        /// that is an enclosing scope this block belongs to.
        /// </summary>
        internal void Assign(string bindingId, object? value)
        {
            NeoScriptScope target = this;
            while (target.block && !target.ContainsLocal(bindingId))
                target = target.Parent!;
            target.SetLocal(bindingId, value);
        }

        internal bool ContainsLocal(string bindingId) =>
            externalBindings?.ContainsKey(bindingId) ?? (bindings!.ContainsKey(bindingId)
                || layout is not null && layout.Slots.TryGetValue(bindingId, out int slot) && occupied[slot]);

        internal void SetLocal(string bindingId, object? value) => SetEvaluationValue(bindingId, new EvaluationValue(value));

        internal void SetEvaluationValue(string bindingId, EvaluationValue value)
        {
            if (externalBindings is not null)
                externalBindings[bindingId] = value.Box();
            else if (layout is not null && layout.Slots.TryGetValue(bindingId, out int slot))
                SetSlot(slot, value);
            else
                bindings![bindingId] = value;
        }

        internal void SetParameter(int index, object? value) =>
            SetSlot(index, new EvaluationValue(value));

        private void SetSlot(int slot, EvaluationValue value)
        {
            if (!occupied[slot])
            {
                occupied[slot] = true;
                occupiedCount++;
            }
            slots[slot] = value;
        }

        internal void SetEvaluationValue(Variable variable, EvaluationValue value)
        {
            if (layout is null)
            {
                SetEvaluationValue(variable.id, value);
                return;
            }
            var binding = variable.runtimeBinding;
            if (!ReferenceEquals(binding?.Layout, layout))
            {
                if (!layout.Slots.TryGetValue(variable.id, out int slot))
                {
                    SetEvaluationValue(variable.id, value);
                    return;
                }
                variable.runtimeBinding = binding = new NeoScriptVariableBinding(layout, slot);
            }
            SetSlot(binding!.Slot, value);
        }

        internal bool TryGetEvaluationValue(VariablePointer variable, out EvaluationValue value)
        {
            if (layout is not null)
            {
                var binding = variable.runtimeBinding;
                if (!ReferenceEquals(binding?.Layout, layout)
                    && layout.Slots.TryGetValue(variable.variableId, out int slot))
                    variable.runtimeBinding = binding = new NeoScriptVariableBinding(layout, slot);
                if (ReferenceEquals(binding?.Layout, layout) && occupied[binding!.Slot])
                {
                    value = slots[binding.Slot];
                    return true;
                }
            }
            return TryGetEvaluationValue(variable.variableId, out value);
        }

        internal void ResetLocals()
        {
            bindings?.Clear();
            Array.Clear(slots, 0, slots.Length);
            Array.Clear(occupied, 0, occupied.Length);
            occupiedCount = 0;
            layout = null;
            externalBindings?.Clear();
            readOnlyBindings?.Clear();
        }

        /// <summary>
        /// Clears locals created by the previous callback body while retaining
        /// the callback's fixed parameter slots for overwrite on the next
        /// entry. The parameter layout is prepared once per operator.
        /// </summary>
        internal void ResetInvocationLocals(int parameterCount)
        {
            if (LocalBindingCount > parameterCount)
                ResetLocals();
            readOnlyBindings?.Clear();
        }

        internal bool Remove(string bindingId)
        {
            if (externalBindings is not null)
                return externalBindings.Remove(bindingId);
            if (layout is not null && layout.Slots.TryGetValue(bindingId, out int slot) && occupied[slot])
            {
                occupied[slot] = false;
                slots[slot] = default;
                occupiedCount--;
                return true;
            }
            return bindings!.Remove(bindingId);
        }

        internal bool TryGetValue(string bindingId, out object? value)
        {
            bool found = TryGetEvaluationValue(bindingId, out var stored);
            value = stored.Box();
            return found;
        }

        internal bool TryGetEvaluationValue(string bindingId, out EvaluationValue value)
        {
            if (externalBindings is not null)
            {
                if (externalBindings.TryGetValue(bindingId, out var external))
                {
                    value = new EvaluationValue(external);
                    return true;
                }
            }
            else
            {
                if (layout is not null && layout.Slots.TryGetValue(bindingId, out int slot) && occupied[slot])
                {
                    value = slots[slot];
                    return true;
                }
                if (bindings!.TryGetValue(bindingId, out value))
                    return true;
            }
            if (Parent is not null)
                return Parent.TryGetEvaluationValue(bindingId, out value);
            value = default;
            return false;
        }

        internal void MarkReadOnly(string bindingId, string error)
        {
            readOnlyBindings ??= new(StringComparer.Ordinal);
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
            if (readOnlyBindings is null || !readOnlyBindings.TryGetValue(
                    bindingId,
                    out List<string>? errors))
            {
                return;
            }
            if (errors.Count > 0)
                errors.RemoveAt(errors.Count - 1);
            if (errors.Count == 0)
                readOnlyBindings.Remove(bindingId);
        }

        internal bool TryGetReadOnlyError(string bindingId, out string? error)
        {
            if (readOnlyBindings is not null && readOnlyBindings.TryGetValue(
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
            if (externalBindings is not null)
                foreach (var binding in externalBindings)
                    result[binding.Key] = binding.Value;
            else
                foreach (var binding in bindings)
                    result[binding.Key] = binding.Value.Box();
            if (layout is not null)
                foreach (var pair in layout.Slots)
                    if (occupied[pair.Value])
                        result[pair.Key] = slots[pair.Value].Box();
            return result;
        }
    }
}
