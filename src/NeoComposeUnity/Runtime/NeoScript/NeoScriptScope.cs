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
        // The scope stays on the layout and a flag tracks its use, so a call
        // stores no reference into the layout (Mono write-barriers each one).
        // A plain flag, like the IR's other caches: evaluation is single-threaded.
        private NeoScriptScope? pooledScope;
        private bool pooledScopeInUse;

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
                if (parameter.id == RootParameterId)
                    rootSlot = Slots.Count;
                else if (parameter.id == ThisParameterId)
                    thisSlot = Slots.Count;
                else if (parameter.id == ContextParameterId)
                    contextSlot = Slots.Count;
                else if (parameter.id == ValueParameterId)
                    valueSlot = Slots.Count;
                Slots.Add(parameter.id, Slots.Count);
            }
            AddDeclarations(instructions);
            temporaryListSlots = FindTemporaryListSlots(instructions);
        }

        /// <summary>
        /// Slots of locals no one else can hold the list of: declared once
        /// from a Where, whose result is always a fresh array, and otherwise
        /// only read as the collection an operator consumes. A completed body
        /// returns their arrays to <see cref="NSGetterEvaluator.TemporaryLists"/>.
        /// </summary>
        internal readonly int[]? temporaryListSlots;

        private int[]? FindTemporaryListSlots(Instruction[] instructions)
        {
            Dictionary<string, Pointer?>? declared = null;
            CollectDeclarations(instructions, ref declared);
            HashSet<string>? candidates = null;
            foreach (KeyValuePair<string, Pointer?> declaration in declared ?? new Dictionary<string, Pointer?>())
            {
                if (declaration.Value is FunctionPointer { function: WhereFunction })
                    (candidates ??= new HashSet<string>(StringComparer.Ordinal)).Add(declaration.Key);
            }
            if (candidates is null)
                return null;
            var consumed = new HashSet<Pointer>();
            try
            {
                // Pre-order: an operator marks its collection before the
                // walk reaches it, and any other read or write escapes.
                NeoScriptIrWalker.AnyPointer(instructions, pointer =>
                {
                    if (pointer is FunctionPointer { function: var function }
                        && ConsumedCollection(function) is VariablePointer source)
                        consumed.Add(source);
                    else if (pointer is VariablePointer variable && !consumed.Contains(variable))
                        candidates.Remove(variable.variableId);
                    return candidates.Count == 0;
                });
            }
            catch (NotSupportedException)
            {
                return null;
            }
            if (candidates.Count == 0)
                return null;
            var slots = new int[candidates.Count];
            int index = 0;
            foreach (string id in candidates)
                slots[index++] = Slots[id];
            return slots;
        }

        // Each slot's declaration initializer; null when the id is declared
        // more than once or binds a loop, so it never qualifies.
        private static void CollectDeclarations(Instruction[] instructions, ref Dictionary<string, Pointer?>? declared)
        {
            foreach (var instruction in instructions)
            {
                switch (instruction)
                {
                    case VariableInstruction variable:
                        declared ??= new Dictionary<string, Pointer?>(StringComparer.Ordinal);
                        declared[variable.variable.id] = declared.ContainsKey(variable.variable.id)
                            ? null
                            : variable.variable.pointer;
                        break;
                    case ForEachInstruction forEach:
                        (declared ??= new Dictionary<string, Pointer?>(StringComparer.Ordinal))[forEach.binding.id] = null;
                        break;
                    case ForInstruction loop:
                        (declared ??= new Dictionary<string, Pointer?>(StringComparer.Ordinal))[loop.initializer.id] = null;
                        break;
                    case IfInstruction conditional:
                        foreach (var branch in conditional.branches)
                            CollectDeclarations(branch.instructions, ref declared);
                        if (conditional.elseInstructions is not null)
                            CollectDeclarations(conditional.elseInstructions, ref declared);
                        break;
                }
            }
        }

        /// <summary>The collection an operator reads entries of without keeping it.</summary>
        private static Pointer? ConsumedCollection(Function function) => function switch
        {
            CountFunction count => count.info.collectionPointer,
            ContainsFunction contains => contains.info.collectionPointer,
            IndexOfFunction indexOf => indexOf.info.collectionPointer,
            FirstFunction first => first.info.collectionPointer,
            FirstOrDefaultFunction first => first.info.collectionPointer,
            ListIndexFunction index => index.info.collectionPointer,
            WhereFunction where => where.info.collectionPointer,
            SelectFunction select => select.info.collectionPointer,
            CollectionQueryFunction query => query.info.collectionPointer,
            _ => null,
        };

        private const string RootParameterId = "__root__";
        private const string ThisParameterId = "__this__";
        private const string ContextParameterId = "__context__";
        internal const string ValueParameterId = "__value__";

        // The pooled scope keeps its root parameter between calls: the root
        // outlives them, and rebinding the same one then skips its store's
        // write barrier.
        internal readonly int rootSlot = -1;
        // The receiver, dialogue context and setter value parameters' slots, or -1.
        internal readonly int thisSlot = -1;
        internal readonly int contextSlot = -1;
        internal readonly int valueSlot = -1;

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
                        AddRunInPlaceDeclarations(forEach.instructions);
                        break;
                    case ForInstruction loop:
                        Add(loop.initializer.id);
                        AddRunInPlaceDeclarations(loop.instructions);
                        break;
                    case WhileInstruction loop:
                        AddRunInPlaceDeclarations(loop.instructions);
                        break;
                    case SwitchInstruction switchInstruction:
                        foreach (var section in switchInstruction.sections)
                            AddRunInPlaceDeclarations(section.instructions);
                        if (switchInstruction.defaultInstructions is not null)
                            AddRunInPlaceDeclarations(switchInstruction.defaultInstructions);
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

        // A loop or switch body without locals runs in this scope, so the
        // loop bindings nested in it are this layout's too.
        private void AddRunInPlaceDeclarations(Instruction[] body)
        {
            if (!DeclaresLocals(body))
                AddDeclarations(body);
        }

        /// <summary>
        /// Whether a block declares a local into the scope it runs in. If
        /// branches run in their enclosing scope, so their locals count.
        /// </summary>
        internal static bool DeclaresLocals(Instruction[] instructions)
        {
            for (int i = 0; i < instructions.Length; i++)
            {
                switch (instructions[i])
                {
                    case VariableInstruction:
                        return true;
                    case IfInstruction conditional:
                        foreach (var branch in conditional.branches)
                        {
                            if (DeclaresLocals(branch.instructions))
                                return true;
                        }
                        if (conditional.elseInstructions is not null
                            && DeclaresLocals(conditional.elseInstructions))
                        {
                            return true;
                        }
                        break;
                }
            }
            return false;
        }

        private void Add(string id)
        {
            if (!Slots.ContainsKey(id))
                Slots.Add(id, Slots.Count);
        }

        internal NeoScriptScope RentScope()
        {
            if (pooledScopeInUse)
                return new NeoScriptScope(this);
            pooledScopeInUse = true;
            return pooledScope ??= new NeoScriptScope(this);
        }

        /// <summary>Releases a completed body's scope for the body's next call.</summary>
        /// <param name="boundParameters">
        /// How many leading parameter slots every call binds. Those keep their
        /// values, as a callback's do, so rebinding an unchanged argument
        /// skips its store's write barrier.
        /// </param>
        internal void ReturnScope(NeoScriptScope scope, int boundParameters = 0)
        {
            if (temporaryListSlots is not null)
                scope.ReturnTemporaryLists(temporaryListSlots);
            if (!ReferenceEquals(scope, pooledScope))
                return;
            // Only a dynamic binding grows the dictionary.
            if (scope.hasDynamicBindings && scope.BindingCapacity > MaxPooledBindings)
            {
                pooledScope = null;
            }
            else
            {
                // Release all local references but the root and the bound
                // parameters before retaining the frame.
                scope.ResetLocals(rootSlot, boundParameters);
                scope.hasDynamicBindings = false;
                scope.ReleaseParent();
            }
            pooledScopeInUse = false;
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
            pooledScopeInUse = false;
        }
    }

    /// <summary>
    /// A local list that <c>Add</c> grows in amortized steps. A list value
    /// is an exact-length array, so a read takes a snapshot, kept until the
    /// next Add. An array a read was handed is never written again.
    /// </summary>
    internal sealed class LocalList
    {
        private object?[] entries;
        private int count;
        private readonly Member? entryMember;
        private object?[]? snapshot;

        /// <summary><paramref name="list"/> with <paramref name="entry"/> added.</summary>
        internal LocalList(object?[] list, object? entry, Member? entryMember)
        {
            // The first Add allocates exactly, as a one-off Add did.
            entries = new object?[list.Length + 1];
            Array.Copy(list, entries, list.Length);
            entries[list.Length] = entry;
            count = entries.Length;
            this.entryMember = entryMember;
        }

        /// <summary>
        /// Whether a read holds the list as it stands. Only then can anything
        /// else have learned of it, as a row's or detached object's list.
        /// </summary>
        internal bool Read => snapshot is not null;

        internal bool IsSnapshot(object?[] list) => ReferenceEquals(snapshot, list);

        internal void Add(object? entry)
        {
            // A full array may be a read's snapshot: grow into a new one.
            if (count == entries.Length)
                Array.Resize(ref entries, Math.Max(4, count * 2));
            entries[count++] = entry;
            snapshot = null;
        }

        internal object?[] Snapshot()
        {
            if (snapshot is not null)
                return snapshot;
            object?[] exact = entries;
            if (count != entries.Length)
            {
                exact = new object?[count];
                Array.Copy(entries, exact, count);
            }
            NSGetterEvaluator.KeepEntryMember(exact, entryMember);
            return snapshot = exact;
        }
    }

    /// <summary>
    /// A lexical NeoScript scope frame. Writes stay local while reads and
    /// read-only diagnostics walk the parent chain.
    /// </summary>
    internal sealed class NeoScriptScope
    {
        private readonly Dictionary<string, EvaluationValue>? bindings;
        // Set by every dynamic binding and cleared only when the layout pools
        // the scope, which checks the dictionary's growth first. A frame that
        // binds only slots skips the dictionary's Clear and capacity calls.
        internal bool hasDynamicBindings;
        private readonly Dictionary<string, object?>? externalBindings;
        /// <summary>
        /// Read-only marks, one per enclosing foreach or catch binding, so a
        /// scan beats hashing every assignment's id. A nested mark of one id
        /// stacks; one binding id is one declaration, so its marks carry the
        /// same error.
        /// </summary>
        private ReadOnlyMark[] readOnlyMarks = Array.Empty<ReadOnlyMark>();
        private int readOnlyMarkCount;

        private struct ReadOnlyMark
        {
            internal string bindingId;
            internal string error;
        }

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
            // list, aliased rowAlias's row, or no row or detached slot when
            // rowAlias is null.
            internal object? aliasIndex;
            internal NSGetterEvaluator.RowReference? rowAlias;
            internal int aliasEpoch;
        }

        private const byte EmptySlot = 0;
        private const byte ValueSlot = 1;
        private const byte NumberSlot = 2;
        // A LocalList that Add grows.
        private const byte ListSlot = 3;
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
            parentScope = parent ?? throw new ArgumentNullException(nameof(parent));
            bindings = new Dictionary<string, EvaluationValue>(capacity, StringComparer.Ordinal);
            this.block = block;
        }

        // A statement block's locals end with it, while its assignments to
        // enclosing bindings land where those bindings were declared.
        private bool block;

        internal NeoScriptScope? Parent => parentScope;
        // A plain field so ReleaseParent's null store pays no write barrier;
        // one through a property setter does.
        private NeoScriptScope? parentScope;

        /// <summary>Adopts a pooled scope as a child of <paramref name="parent"/>, or releases it.</summary>
        internal void BindParent(NeoScriptScope? parent)
        {
            parentScope = parent;
            block = false;
        }

        // A constant null store skips the GC write barrier that storing a
        // null argument pays.
        internal void ReleaseParent()
        {
            parentScope = null;
            block = false;
        }

        /// <summary>Adopts a pooled scope as a statement block of <paramref name="parent"/>.</summary>
        internal void BindBlock(NeoScriptScope parent)
        {
            parentScope = parent;
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
        /// Writes the binding <paramref name="variable"/> names in the nearest
        /// block that holds it, else declares it in the nearest frame. Frames
        /// answer from the pointer's cached slot, so a write hashes only
        /// frames that hold dynamic bindings.
        /// </summary>
        /// <param name="value">A value, or an <see cref="NSGetterEvaluator.EvaluateValue"/> result.</param>
        internal void Assign(VariablePointer variable, object? value, double number = 0)
        {
            NeoScriptScope target = this;
            while (true)
            {
                int slot = target.OccupiedSlot(variable);
                if (slot >= 0)
                {
                    target.SetSlot(slot, value, number);
                    return;
                }
                if (!target.block || target.ContainsDynamicLocal(variable.variableId))
                    break;
                target = target.Parent!;
            }
            target.SetEvaluationValue(variable.variableId, value, number);
        }

        private bool ContainsDynamicLocal(string bindingId) =>
            externalBindings?.ContainsKey(bindingId) ?? (bindings!.Count != 0 && bindings.ContainsKey(bindingId));

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
            {
                bindings![bindingId] = new EvaluationValue(value);
                hasDynamicBindings = true;
            }
        }

        /// <param name="value">An <see cref="NSGetterEvaluator.EvaluateValue"/> result.</param>
        internal void SetEvaluationValue(string bindingId, object? value, double number)
        {
            if (externalBindings is not null)
                externalBindings[bindingId] = EvaluationValue.Box(value, number);
            else if (layout is not null && layout.Slots.TryGetValue(bindingId, out int slot))
                SetSlot(slot, value, number);
            else
            {
                bindings![bindingId] = new EvaluationValue(value, number);
                hasDynamicBindings = true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetParameter(int index, object? value)
        {
            // A pooled frame's kept root is usually the one being bound.
            if (slotKinds[index] == ValueSlot && ReferenceEquals(slotValues[index].value, value))
                return;
            SetSlotValue(index, value);
        }

        private void SetSlot(int slot, object? value, double number)
        {
            if (!ReferenceEquals(value, EvaluationValue.BareNumber))
            {
                SetSlotValue(slot, value);
                return;
            }
            byte kind = slotKinds[slot];
            if (kind != NumberSlot)
            {
                // An empty slot already holds null: only a value slot's
                // reference needs the barriered store that drops it.
                if (kind == EmptySlot)
                    occupiedCount++;
                else
                    slotValues[slot].value = null;
                slotKinds[slot] = NumberSlot;
            }
            slotNumbers[slot] = number;
        }

        private void SetSlotValue(int slot, object? value)
        {
            if (slotKinds[slot] == EmptySlot)
                occupiedCount++;
            slotKinds[slot] = ValueSlot;
            // A cleared slot holds null, and a rebound one often the same
            // value: skip that store's write barrier.
            ref var entry = ref slotValues[slot];
            if (!ReferenceEquals(entry.value, value))
                entry.value = value;
            entry.aliasEpoch = 0;
        }

        // A number slot boxes on read, as the stored struct did.
        private object? SlotValue(int slot) => slotKinds[slot] switch
        {
            NumberSlot => NSGetterEvaluator.Box(slotNumbers[slot]),
            ListSlot => ((LocalList)slotValues[slot].value!).Snapshot(),
            _ => slotValues[slot].value,
        };

        /// <summary>
        /// The <see cref="LocalList"/> the slot local <paramref name="variable"/>
        /// holds, read without taking its snapshot, or null.
        /// </summary>
        internal LocalList? ReadLocalList(VariablePointer variable)
        {
            for (NeoScriptScope? scope = this; scope is not null; scope = scope.Parent)
            {
                int slot = scope.OccupiedSlot(variable);
                if (slot >= 0)
                    return scope.slotKinds[slot] == ListSlot ? (LocalList)scope.slotValues[slot].value! : null;
                if (scope.TryGetDynamicValue(variable, out _))
                    return null;
            }
            return null;
        }

        /// <summary>
        /// Stores <paramref name="list"/> in the slot that holds
        /// <paramref name="variable"/>, as <see cref="Assign"/> would its
        /// snapshot. False when the variable is not a slot local.
        /// </summary>
        internal bool AssignLocalList(VariablePointer variable, LocalList list)
        {
            NeoScriptScope target = this;
            while (true)
            {
                int slot = target.OccupiedSlot(variable);
                if (slot >= 0)
                {
                    if (target.slotKinds[slot] != ListSlot || !ReferenceEquals(target.slotValues[slot].value, list))
                    {
                        target.SetSlotValue(slot, list);
                        target.slotKinds[slot] = ListSlot;
                    }
                    return true;
                }
                if (!target.block || target.ContainsDynamicLocal(variable.variableId))
                    return false;
                target = target.Parent!;
            }
        }

        /// <param name="value">An <see cref="NSGetterEvaluator.EvaluateValue"/> result.</param>
        internal void SetEvaluationValue(Variable variable, object? value, double number) =>
            SetEvaluationValue(variable.id, ref variable.runtimeBinding, value, number);

        /// <summary>Binds a <c>foreach</c> variable to its next entry.</summary>
        internal void SetLocal(LoopBinding loopBinding, object? value) =>
            SetEvaluationValue(loopBinding.id, ref loopBinding.runtimeBinding, value, 0);

        // The declaration's slot is cached on it, so a rebinding hashes nothing.
        private void SetEvaluationValue(string bindingId, ref NeoScriptVariableBinding? cachedBinding, object? value, double number)
        {
            if (layout is null)
            {
                SetEvaluationValue(bindingId, value, number);
                return;
            }
            var binding = cachedBinding;
            if (!ReferenceEquals(binding?.Layout, layout))
            {
                if (!layout.Slots.TryGetValue(bindingId, out int slot))
                {
                    SetEvaluationValue(bindingId, value, number);
                    return;
                }
                cachedBinding = binding = new NeoScriptVariableBinding(layout, slot);
            }
            SetSlot(binding!.Slot, value, number);
        }

        /// <summary>
        /// Reads a variable through the scope chain. Each frame answers from
        /// the pointer's cached slot when the frame's layout declared it, so a
        /// read from a nested block or callback hashes only frames that hold
        /// dynamic bindings. The value comes back as the return value: copying
        /// the stored struct through an <c>out</c> would pay a GC write barrier
        /// for its reference on every read. <paramref name="remembered"/> is
        /// true when the value is a list <see cref="RememberListAlias"/>
        /// recorded for <paramref name="aliasIndex"/> since no list became an
        /// alias; <paramref name="rowAlias"/> then gets the row it aliased,
        /// if any, and is left alone otherwise.
        /// </summary>
        internal object? ReadVariable(
            VariablePointer variable,
            object aliasIndex,
            out bool found,
            out bool remembered,
            ref NSGetterEvaluator.RowReference? rowAlias)
        {
            for (NeoScriptScope? scope = this; scope is not null; scope = scope.Parent)
            {
                int slot = scope.OccupiedSlot(variable);
                if (slot >= 0)
                {
                    found = true;
                    byte kind = scope.slotKinds[slot];
                    if (kind != ValueSlot)
                    {
                        remembered = false;
                        return kind == NumberSlot
                            ? NSGetterEvaluator.Box(scope.slotNumbers[slot])
                            : ((LocalList)scope.slotValues[slot].value!).Snapshot();
                    }
                    // Only RememberListAlias sets the epoch, on the list the
                    // slot still holds, and every store resets it.
                    ref Slot stored = ref scope.slotValues[slot];
                    remembered = stored.aliasEpoch == NSGetterEvaluator.CollectionAliasEpoch
                        && ReferenceEquals(stored.aliasIndex, aliasIndex);
                    if (remembered && stored.rowAlias is not null)
                        rowAlias = stored.rowAlias;
                    return stored.value;
                }
                if (scope.TryGetDynamicValue(variable, out EvaluationValue value))
                {
                    found = true;
                    remembered = false;
                    return value.Box();
                }
            }
            found = false;
            remembered = false;
            return null;
        }

        /// <summary>
        /// Records that the slot <paramref name="variable"/> reads, while it
        /// still holds <paramref name="list"/>, aliased
        /// <paramref name="rowAlias"/>'s row, or nothing when it is null, in
        /// <paramref name="aliasIndex"/> as of <paramref name="epoch"/>, read
        /// before the lookups that found the alias.
        /// </summary>
        internal void RememberListAlias(
            VariablePointer variable,
            object?[] list,
            object aliasIndex,
            int epoch,
            NSGetterEvaluator.RowReference? rowAlias)
        {
            for (NeoScriptScope? scope = this; scope is not null; scope = scope.Parent)
            {
                int slot = scope.OccupiedSlot(variable);
                if (slot >= 0)
                {
                    ref Slot stored = ref scope.slotValues[slot];
                    if (ReferenceEquals(stored.value, list))
                    {
                        // Each reference store pays a GC write barrier; a
                        // constant null or an unchanged index skips it.
                        if (!ReferenceEquals(stored.aliasIndex, aliasIndex))
                            stored.aliasIndex = aliasIndex;
                        if (rowAlias is null)
                            stored.rowAlias = null;
                        else
                            stored.rowAlias = rowAlias;
                        stored.aliasEpoch = epoch;
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
                // OccupiedSlot, reading the slot's kind once.
                var binding = variable.runtimeBinding;
                int slot = binding is not null && ReferenceEquals(binding.Layout, scope.layout)
                    ? binding.Slot
                    : scope.BindSlot(variable);
                if (slot >= 0)
                {
                    byte kind = scope.slotKinds[slot];
                    if (kind == NumberSlot)
                    {
                        number = scope.slotNumbers[slot];
                        return true;
                    }
                    if (kind != EmptySlot)
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

        /// <param name="keptSlot">A slot to leave bound, or -1.</param>
        /// <param name="keptParameters">How many leading slots to leave bound.</param>
        /// <summary>Returns the arrays the layout proved only these slots hold.</summary>
        internal void ReturnTemporaryLists(int[] slots)
        {
            foreach (int slot in slots)
            {
                if (slotKinds[slot] == ValueSlot && slotValues[slot].value is object?[] entries)
                    NSGetterEvaluator.TemporaryLists.Return(entries);
            }
        }

        internal void ResetLocals(int keptSlot = -1, int keptParameters = 0)
        {
            if (hasDynamicBindings)
                bindings!.Clear();
            if (occupiedCount > 0)
            {
                // Frames hold a handful of slots: a loop beats Array.Clear's
                // native call. It starts past the kept parameters.
                for (int i = keptParameters; i < slotKinds.Length; i++)
                {
                    if (slotKinds[i] == EmptySlot || i == keptSlot)
                        continue;
                    slotKinds[i] = EmptySlot;
                    slotValues[i].value = null;
                    slotValues[i].aliasIndex = null;
                    slotValues[i].rowAlias = null;
                    occupiedCount--;
                }
            }
            externalBindings?.Clear();
            ClearReadOnlyMarks();
        }

        /// <summary>
        /// Clears locals created by the previous callback body while retaining
        /// the callback's fixed parameter slots for overwrite on the next
        /// entry. The parameter layout is prepared once per operator.
        /// </summary>
        internal void ResetInvocationLocals(int parameterCount)
        {
            // A frame without dynamic bindings holds only its slots, so it
            // skips reading the dictionary's count.
            if (occupiedCount > parameterCount
                || (hasDynamicBindings || externalBindings is not null) && LocalBindingCount > parameterCount)
            {
                ResetLocals();
            }
            ClearReadOnlyMarks();
        }

        internal bool Remove(string bindingId)
        {
            if (externalBindings is not null)
                return externalBindings.Remove(bindingId);
            if (layout is not null && layout.Slots.TryGetValue(bindingId, out int slot) && slotKinds[slot] != EmptySlot)
            {
                slotKinds[slot] = EmptySlot;
                slotValues[slot].value = null;
                slotValues[slot].aliasIndex = null;
                slotValues[slot].rowAlias = null;
                occupiedCount--;
                return true;
            }
            return bindings!.Remove(bindingId);
        }

        internal bool TryGetValue(string bindingId, out object? value)
        {
            if (externalBindings is not null)
            {
                if (externalBindings.TryGetValue(bindingId, out value))
                    return true;
            }
            else
            {
                if (layout is not null && layout.Slots.TryGetValue(bindingId, out int slot) && slotKinds[slot] != EmptySlot)
                {
                    value = SlotValue(slot);
                    return true;
                }
                if (bindings!.Count > 0 && bindings.TryGetValue(bindingId, out var stored))
                {
                    value = stored.Box();
                    return true;
                }
            }
            if (Parent is not null)
                return Parent.TryGetValue(bindingId, out value);
            value = null;
            return false;
        }

        internal void MarkReadOnly(string bindingId, string error)
        {
            if (readOnlyMarkCount == readOnlyMarks.Length)
                Array.Resize(ref readOnlyMarks, Math.Max(2, readOnlyMarkCount * 2));
            readOnlyMarks[readOnlyMarkCount++] = new ReadOnlyMark { bindingId = bindingId, error = error };
        }

        internal void UnmarkReadOnly(string bindingId)
        {
            int index = ReadOnlyMarkIndex(bindingId);
            if (index < 0)
                return;
            readOnlyMarkCount--;
            Array.Copy(readOnlyMarks, index + 1, readOnlyMarks, index, readOnlyMarkCount - index);
            readOnlyMarks[readOnlyMarkCount] = default;
        }

        private void ClearReadOnlyMarks()
        {
            if (readOnlyMarkCount == 0)
                return;
            Array.Clear(readOnlyMarks, 0, readOnlyMarkCount);
            readOnlyMarkCount = 0;
        }

        private int ReadOnlyMarkIndex(string bindingId)
        {
            for (int index = readOnlyMarkCount - 1; index >= 0; index--)
            {
                if (string.Equals(readOnlyMarks[index].bindingId, bindingId))
                    return index;
            }
            return -1;
        }

        internal bool TryGetReadOnlyError(string bindingId, out string? error)
        {
            for (NeoScriptScope? scope = this; scope is not null; scope = scope.Parent)
            {
                // Only foreach iterators and catch messages mark a scope.
                if (scope.readOnlyMarkCount == 0)
                    continue;
                int index = scope.ReadOnlyMarkIndex(bindingId);
                if (index >= 0)
                {
                    error = scope.readOnlyMarks[index].error;
                    return true;
                }
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
