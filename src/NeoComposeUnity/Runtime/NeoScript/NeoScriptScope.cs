// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
        // Scopes whose dynamic bindings grew past this are dropped rather than retained.
        private const int MaxPooledBindings = 128;

        internal readonly Dictionary<string, int> Slots = new(StringComparer.Ordinal);

        // A synchronous body's scope is idle again before the body's next
        // call, so one retained scope serves every call but a recursive one.
        // The scope stays on the layout and an int flag tracks its use, so a
        // call stores no reference into the layout (Mono write-barriers each
        // one).
        private NeoScriptScope? pooledScope;
        private int pooledScopeInUse;

        /// <summary>
        /// Whether a constructor body's parameters are the positional
        /// <c>__this__</c>, <c>__root__</c>, <c>__arg_N__</c> envelope;
        /// null until its first call checks.
        /// </summary>
        internal bool? constructorEnvelope;

        internal NeoScriptScopeLayout(FunctionWithReturnType body)
            : this(body.parameters, body.instructions)
        {
        }

        internal NeoScriptScopeLayout(Variable[]? parameters, Instruction[] instructions)
        {
            // Parameter i is slot i, so a call binds its arguments by position.
            foreach (var parameter in parameters ?? Array.Empty<Variable>())
            {
                if (Slots.ContainsKey(parameter.id))
                    throw new InvalidOperationException(
                        $"NeoScript body declares parameter '{parameter.id}' twice; its compiled IR is stale or corrupt.");
                Slots.Add(parameter.id, Slots.Count);
            }
            AddDeclarations(instructions);
        }

        // The bindings this scope holds: its locals, the locals of if
        // branches (which run in it), and the bindings of loops run in it.
        // Anything else still binds through the scope's dictionary.
        private void AddDeclarations(Instruction[] instructions)
        {
            foreach (var instruction in instructions)
            {
                switch (instruction)
                {
                    case VariableInstruction variable:
                        Add(variable.variable.id);
                        break;
                    case ForEachInstruction forEach:
                        Add(forEach.binding.id);
                        break;
                    case ForInstruction loop:
                        Add(loop.initializer.id);
                        break;
                    case IfInstruction conditional:
                        foreach (var branch in conditional.branches)
                            AddDeclarations(branch.instructions);
                        if (conditional.elseInstructions is not null)
                            AddDeclarations(conditional.elseInstructions);
                        break;
                }
            }
        }

        private void Add(string id)
        {
            if (!Slots.ContainsKey(id))
                Slots.Add(id, Slots.Count);
        }

        internal NeoScriptScope RentScope()
        {
            if (System.Threading.Interlocked.CompareExchange(ref pooledScopeInUse, 1, 0) != 0)
                return new NeoScriptScope(this);
            return pooledScope ??= new NeoScriptScope(this);
        }

        /// <summary>Releases a completed body's scope for the body's next call.</summary>
        internal void ReturnScope(NeoScriptScope scope)
        {
            if (!ReferenceEquals(scope, pooledScope))
                return;
            if (scope.BindingCapacity > MaxPooledBindings)
            {
                pooledScope = null;
            }
            else
            {
                // Release all argument/local references before retaining the empty frame.
                scope.ResetLocals();
                scope.ReleaseParent();
            }
            System.Threading.Volatile.Write(ref pooledScopeInUse, 0);
        }

        /// <summary>
        /// Gives up a scope whose body threw or suspended. A continuation may
        /// still hold it, so the layout forgets it and pools a fresh one.
        /// </summary>
        internal void AbandonScope(NeoScriptScope scope)
        {
            if (!ReferenceEquals(scope, pooledScope))
                return;
            pooledScope = null;
            System.Threading.Volatile.Write(ref pooledScopeInUse, 0);
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
        /// <summary>
        /// Read-only marks with a nesting depth. One binding id is one
        /// declaration, so every mark on it carries the same error.
        /// </summary>
        private Dictionary<string, (string error, int depth)>? readOnlyBindings;
        private readonly NeoScriptScopeLayout? layout;
        // A layout slot holds a value, or a number no box holds. The two live
        // in separate arrays: Mono write-barriers a struct with a reference
        // field as a range copy, a lone reference as one card mark, and a
        // number not at all. A value sits in a struct because an object[]
        // store runs Mono's covariance-checking helper, and a field store
        // skips it; a constant null stored in a field also skips the barrier.
        private struct Slot
        {
            internal object? value;
            // The alias index and list alias epoch as of which value, a
            // list, aliased no row or detached slot.
            internal object? plainListIndex;
            internal int plainListEpoch;
        }

        private const byte EmptySlot = 0;
        private const byte ValueSlot = 1;
        private const byte NumberSlot = 2;
        private readonly Slot[] slotValues = Array.Empty<Slot>();
        private readonly double[] slotNumbers = Array.Empty<double>();
        private readonly byte[] slotKinds = Array.Empty<byte>();
        private int occupiedCount;

        internal NeoScriptScope(int capacity = 0)
        {
            bindings = new Dictionary<string, EvaluationValue>(capacity, StringComparer.Ordinal);
        }

        internal NeoScriptScope(NeoScriptScopeLayout layout)
        {
            bindings = new Dictionary<string, EvaluationValue>(StringComparer.Ordinal);
            this.layout = layout;
            slotValues = new Slot[layout.Slots.Count];
            slotNumbers = new double[layout.Slots.Count];
            slotKinds = new byte[layout.Slots.Count];
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
        private bool block;

        internal NeoScriptScope? Parent
        {
            get;
            private set;
        }

        /// <summary>Adopts a pooled scope as a child of <paramref name="parent"/>, or releases it.</summary>
        internal void BindParent(NeoScriptScope? parent)
        {
            Parent = parent;
            block = false;
        }

        // A constant null store skips the GC write barrier that storing a
        // null argument pays.
        internal void ReleaseParent()
        {
            Parent = null;
            block = false;
        }

        /// <summary>Adopts a pooled scope as a statement block of <paramref name="parent"/>.</summary>
        internal void BindBlock(NeoScriptScope parent)
        {
            Parent = parent;
            block = true;
        }
        internal int LocalBindingCount => externalBindings?.Count ?? bindings!.Count + occupiedCount;
        // Dynamic binding storage only: slots are sized by the layout.
        internal int BindingCapacity => externalBindings?.EnsureCapacity(0) ?? bindings!.EnsureCapacity(0);

        internal object? this[string bindingId]
        {
            set => SetLocal(bindingId, value);
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
                || layout is not null && layout.Slots.TryGetValue(bindingId, out int slot) && slotKinds[slot] != EmptySlot);

        internal void SetLocal(string bindingId, object? value)
        {
            if (externalBindings is not null)
                externalBindings[bindingId] = value;
            else if (layout is not null && layout.Slots.TryGetValue(bindingId, out int slot))
                SetSlotValue(slot, value);
            else
                bindings![bindingId] = new EvaluationValue(value);
        }

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
            SetSlotValue(index, value);

        private void SetSlot(int slot, EvaluationValue value)
        {
            if (value.Reference is not null || !value.IsNumber)
            {
                SetSlotValue(slot, value.Reference);
                return;
            }
            if (slotKinds[slot] == EmptySlot)
                occupiedCount++;
            slotKinds[slot] = NumberSlot;
            slotNumbers[slot] = value.Number;
            slotValues[slot].value = null;
        }

        private void SetSlotValue(int slot, object? value)
        {
            if (slotKinds[slot] == EmptySlot)
                occupiedCount++;
            slotKinds[slot] = ValueSlot;
            slotValues[slot].value = value;
            slotValues[slot].plainListEpoch = 0;
        }

        // A number slot boxes on read, as the stored struct did.
        private object? SlotValue(int slot) =>
            slotKinds[slot] == NumberSlot ? NSGetterEvaluator.Box(slotNumbers[slot]) : slotValues[slot].value;

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

        /// <summary>
        /// Reads a variable through the scope chain. Each frame answers from
        /// the pointer's cached slot when the frame's layout declared it, so a
        /// read from a nested block or callback hashes only frames that hold
        /// dynamic bindings. The value comes back as the return value: copying
        /// the stored struct through an <c>out</c> would pay a GC write barrier
        /// for its reference on every read. <paramref name="plainList"/> is
        /// true when the value is a list <see cref="RememberPlainList"/>
        /// recorded for <paramref name="aliasIndex"/> since no list became an
        /// alias.
        /// </summary>
        internal object? ReadVariable(VariablePointer variable, object aliasIndex, out bool found, out bool plainList)
        {
            for (NeoScriptScope? scope = this; scope is not null; scope = scope.Parent)
            {
                int slot = scope.OccupiedSlot(variable);
                if (slot >= 0)
                {
                    found = true;
                    if (scope.slotKinds[slot] == NumberSlot)
                    {
                        plainList = false;
                        return NSGetterEvaluator.Box(scope.slotNumbers[slot]);
                    }
                    // Only RememberPlainList sets the epoch, on the list the
                    // slot still holds, and every store resets it.
                    ref Slot stored = ref scope.slotValues[slot];
                    plainList = stored.plainListEpoch == NSGetterEvaluator.ListAliasEpoch
                        && ReferenceEquals(stored.plainListIndex, aliasIndex);
                    return stored.value;
                }
                if (scope.TryGetDynamicValue(variable, out EvaluationValue value))
                {
                    found = true;
                    plainList = false;
                    return value.Box();
                }
            }
            found = false;
            plainList = false;
            return null;
        }

        /// <summary>
        /// Records that the slot <paramref name="variable"/> reads, while it
        /// still holds <paramref name="list"/>, aliased nothing in
        /// <paramref name="aliasIndex"/> as of <paramref name="epoch"/>, read
        /// before the lookups that found no alias.
        /// </summary>
        internal void RememberPlainList(VariablePointer variable, object?[] list, object aliasIndex, int epoch)
        {
            for (NeoScriptScope? scope = this; scope is not null; scope = scope.Parent)
            {
                int slot = scope.OccupiedSlot(variable);
                if (slot >= 0)
                {
                    ref Slot stored = ref scope.slotValues[slot];
                    if (ReferenceEquals(stored.value, list))
                    {
                        stored.plainListIndex = aliasIndex;
                        stored.plainListEpoch = epoch;
                    }
                    return;
                }
                if (scope.TryGetDynamicValue(variable, out _))
                    return;
            }
        }

        /// <summary><see cref="ReadVariable"/> for arithmetic: false unless the variable holds a number.</summary>
        internal bool TryReadNumber(VariablePointer variable, out double number)
        {
            for (NeoScriptScope? scope = this; scope is not null; scope = scope.Parent)
            {
                int slot = scope.OccupiedSlot(variable);
                if (slot >= 0)
                {
                    if (scope.slotKinds[slot] == NumberSlot)
                    {
                        number = scope.slotNumbers[slot];
                        return true;
                    }
                    return NSGetterEvaluator.TryAsDouble(scope.slotValues[slot].value, out number);
                }
                if (scope.TryGetDynamicValue(variable, out EvaluationValue value))
                {
                    number = value.Number;
                    return value.IsNumber;
                }
            }
            number = 0;
            return false;
        }

        // The occupied slot this frame's layout declared for the variable, or -1.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int OccupiedSlot(VariablePointer variable)
        {
            // A binding always names a layout, so a scope without one misses.
            var binding = variable.runtimeBinding;
            if (binding is null || !ReferenceEquals(binding.Layout, layout))
                return BindSlot(variable);
            return slotKinds[binding.Slot] != EmptySlot ? binding.Slot : -1;
        }

        // A read's scope chain is lexical, so the layouts a pointer walks
        // through without a slot are few and are remembered, not rehashed.
        private const int MaxAbsentLayouts = 4;

        private int BindSlot(VariablePointer variable)
        {
            if (externalBindings is not null || layout is null)
                return -1;
            var absent = variable.absentLayouts;
            if (absent is not null)
                foreach (var known in absent)
                    if (ReferenceEquals(known, layout))
                        return -1;
            if (!layout.Slots.TryGetValue(variable.variableId, out int slot))
            {
                if (absent is null || absent.Length < MaxAbsentLayouts)
                {
                    var grown = new NeoScriptScopeLayout[(absent?.Length ?? 0) + 1];
                    absent?.CopyTo(grown, 0);
                    grown[^1] = layout;
                    variable.absentLayouts = grown;
                }
                return -1;
            }
            variable.runtimeBinding = new NeoScriptVariableBinding(layout, slot);
            return slotKinds[slot] != EmptySlot ? slot : -1;
        }

        // A binding outside the layout's slots: an external or dynamic one.
        private bool TryGetDynamicValue(VariablePointer variable, out EvaluationValue value)
        {
            if (externalBindings is not null)
            {
                bool found = externalBindings.TryGetValue(variable.variableId, out var external);
                value = new EvaluationValue(external);
                return found;
            }
            if (bindings!.Count > 0)
                return bindings.TryGetValue(variable.variableId, out value);
            value = default;
            return false;
        }

        internal void ResetLocals()
        {
            bindings?.Clear();
            if (occupiedCount > 0)
            {
                // Frames hold a handful of slots: a loop beats Array.Clear's
                // native call.
                for (int i = 0; i < slotKinds.Length; i++)
                {
                    if (slotKinds[i] != EmptySlot)
                    {
                        slotKinds[i] = EmptySlot;
                        slotValues[i].value = null;
                        slotValues[i].plainListIndex = null;
                    }
                }
                occupiedCount = 0;
            }
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
            if (layout is not null && layout.Slots.TryGetValue(bindingId, out int slot) && slotKinds[slot] != EmptySlot)
            {
                slotKinds[slot] = EmptySlot;
                slotValues[slot].value = null;
                slotValues[slot].plainListIndex = null;
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
                if (layout is not null && layout.Slots.TryGetValue(bindingId, out int slot) && slotKinds[slot] != EmptySlot)
                {
                    value = slotKinds[slot] == NumberSlot
                        ? new EvaluationValue(slotNumbers[slot])
                        : new EvaluationValue(slotValues[slot].value);
                    return true;
                }
                if (bindings!.Count > 0 && bindings.TryGetValue(bindingId, out value))
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
            readOnlyBindings.TryGetValue(bindingId, out var mark);
            readOnlyBindings[bindingId] = (error, mark.depth + 1);
        }

        internal void UnmarkReadOnly(string bindingId)
        {
            if (readOnlyBindings is null || !readOnlyBindings.TryGetValue(
                    bindingId,
                    out var mark))
            {
                return;
            }
            if (mark.depth > 1)
                readOnlyBindings[bindingId] = (mark.error, mark.depth - 1);
            else
                readOnlyBindings.Remove(bindingId);
        }

        internal bool TryGetReadOnlyError(string bindingId, out string? error)
        {
            if (readOnlyBindings is not null && readOnlyBindings.TryGetValue(
                    bindingId,
                    out var mark))
            {
                error = mark.error;
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
                    if (slotKinds[pair.Value] != EmptySlot)
                        result[pair.Key] = SlotValue(pair.Value);
            return result;
        }
    }
}
