// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    internal static partial class NeoScriptExecutor
    {
        private static void ValidateSequenceMetadata(FunctionWithReturnType body, NeoClient client)
        {
            bool generator = body.generator is not null;
            if (body.generator is { } marker)
            {
                string id = marker.kind == "enumerable" ? NeoSequenceContract.Enumerable
                    : marker.kind == "enumerator" ? NeoSequenceContract.Enumerator : "";
                string parameter = marker.kind == "enumerable" ? NeoSequenceContract.EnumerableElement : NeoSequenceContract.EnumeratorElement;
                if (id.Length == 0 || marker.elementType is null
                    || body.typeInfo is not InterfaceTypeInfo { required: true } result
                    || result.interfaceId != id || result.typeArguments is not { Count: 1 }
                    || !result.typeArguments.TryGetValue(parameter, out TypeInfo element)
                    || !NeoClient.TypeInfoMatches(element, marker.elementType))
                    throw new NeoScriptPreExecutionValidationError("Malformed generator return type or element metadata.");
            }
            bool found = ValidateYields(body.instructions, generator, generator);
            if (found != generator)
                throw new NeoScriptPreExecutionValidationError("Generator marker must match its yield statements.");
            NeoScriptIrWalker.AnyPointer(body.instructions, pointer =>
            {
                if (generator && pointer is CallFunctionPointer call && call.memberId is { } memberId
                    && client.TryGetMember(memberId, out Member? member)
                    && member is FunctionMember { Dispatch: NeoFunctionDispatchKind.Asynchronous })
                    throw new NeoScriptPreExecutionValidationError("A generator cannot invoke an asynchronous function.");
                if (pointer is DelegateClosurePointer closure && closure.action.generator is not null)
                    throw new NeoScriptPreExecutionValidationError("A delegate closure cannot be a generator.");
                if (pointer is RangePointer range && (range.start is null || range.end is null))
                    throw new NeoScriptPreExecutionValidationError("Range requires both bounds.");
                if (pointer is SequenceConversionPointer conversion)
                {
                    if (conversion.pointer is null)
                        throw new NeoScriptPreExecutionValidationError("Sequence conversion requires an operand.");
                    if (conversion.operation == "retainCollectionType")
                    {
                        if (conversion.collectionType is null)
                            throw new NeoScriptPreExecutionValidationError("Collection type retention requires its source type.");
                    }
                    else
                    {
                        if (conversion.operation is not ("getEnumerator" or "collectEnumerator" or "collectEnumerable" or "queryEnumerable"))
                            throw new NeoScriptPreExecutionValidationError("Unknown sequence conversion operation.");
                        if (conversion.elementType is null)
                            throw new NeoScriptPreExecutionValidationError("Sequence conversion requires an element type.");
                        if (conversion.optional && conversion.operation != "getEnumerator")
                            throw new NeoScriptPreExecutionValidationError("Only cursor acquisition may be optional.");
                    }
                }
                return false;
            });
        }

        private static bool ValidateYields(Instruction[]? instructions, bool allowed, bool generator)
        {
            if (instructions is null)
                throw new NeoScriptPreExecutionValidationError("Missing generator instruction block.");
            bool found = false;
            foreach (var instruction in instructions)
            {
                if (instruction is null)
                    throw new NeoScriptPreExecutionValidationError("Null generator instruction.");
                bool nested = false;
                switch (instruction)
                {
                    case YieldReturnInstruction yielded:
                        if (!allowed || yielded.pointer is null)
                            throw new NeoScriptPreExecutionValidationError("Yield is not permitted in this block.");
                        nested = true;
                        break;
                    case YieldBreakInstruction:
                        if (!allowed)
                            throw new NeoScriptPreExecutionValidationError("Yield is not permitted in this block.");
                        nested = true;
                        break;
                    case ReturnInstruction when generator:
                        throw new NeoScriptPreExecutionValidationError("A generator must use yield break instead of return.");
                    case IfInstruction conditional:
                        foreach (var branch in conditional.branches ?? Array.Empty<ConditionalBranch>())
                            nested |= ValidateYields(branch.instructions, allowed, generator);
                        if (conditional.elseInstructions is not null)
                            nested |= ValidateYields(conditional.elseInstructions, allowed, generator);
                        break;
                    case WhileInstruction loop:
                        nested = ValidateYields(loop.instructions, allowed, generator);
                        break;
                    case ForInstruction loop:
                        if (loop.rangeEndId is { Length: 0 })
                            throw new NeoScriptPreExecutionValidationError("A range loop requires its captured end binding.");
                        nested = ValidateYields(loop.instructions, allowed, generator);
                        break;
                    case ForEachInstruction loop:
                        nested = ValidateYields(loop.instructions, allowed, generator);
                        break;
                    case SwitchInstruction selection:
                        foreach (var section in selection.sections ?? Array.Empty<SwitchSection>())
                            nested |= ValidateYields(section.instructions, allowed, generator);
                        if (selection.defaultInstructions is not null)
                            nested |= ValidateYields(selection.defaultInstructions, allowed, generator);
                        break;
                    case TryInstruction region:
                        nested = ValidateYields(region.instructions, allowed && region.catches?.Length == 0, generator);
                        foreach (var clause in region.catches ?? Array.Empty<CatchClause>())
                            ValidateYields(clause.instructions, false, generator);
                        if (region.finallyInstructions is not null)
                            ValidateYields(region.finallyInstructions, false, generator);
                        break;
                }
                if (instruction.yields != nested)
                    throw new NeoScriptPreExecutionValidationError("Yield-bearing instruction metadata does not match its body.");
                found |= nested;
            }
            return found;
        }

        // Only yield-bearing control nodes need a retained frame. All other
        // instructions run through ExecuteInstructions, including protected
        // regions that do not suspend. Frames and loop scopes are reused.
        // A suspended frame retains new bindings and entries, not its whole
        // reachable graph again at every yield. Mutations of a retained local
        // collection enqueue only the inserted value.
        internal sealed class GeneratorRetention
        {
            private static readonly object Seen = new();
            private readonly ConditionalWeakTable<object, object> visited = new();
            private readonly List<object> pending = new();
            private readonly Dictionary<string, HashSet<string>?> children = new(StringComparer.Ordinal);
            internal GeneratorRetention? ParentActive;

            internal bool Visit(object value)
            {
                if (visited.TryGetValue(value, out _))
                    return false;
                visited.Add(value, Seen);
                return true;
            }

            internal void Track(object? value)
            {
                if (value is not null && value is not string && !ReferenceEquals(value, NSGetterEvaluator.ArithmeticValue.BareNumber) && !value.GetType().IsValueType && !visited.TryGetValue(value, out _))
                    pending.Add(value);
            }

            internal void Mutation(object? collection, object? value)
            {
                for (GeneratorRetention? owner = this; owner is not null; owner = owner.ParentActive)
                    if (collection is not null && owner.visited.TryGetValue(collection, out _))
                        owner.Track(value);
            }

            internal void Flush(NSGetterEvaluator.Context ctx)
            {
                foreach (object value in pending)
                    ctx.allocationTracker.MarkEscaped(value, ctx, null, children, this);
                pending.Clear();
                children.Clear();
            }
        }

        internal sealed class GeneratorExecution
        {
            private readonly FunctionWithReturnType body;
            private readonly TypeInfo elementType;
            private readonly GeneratorBlock root;
            private GeneratorBlock? current;
            internal readonly GeneratorRetention Retention = new();
            internal object? Current;

            internal GeneratorExecution(FunctionWithReturnType body, NeoScriptScope scope, TypeInfo elementType)
            {
                this.body = body;
                this.elementType = elementType;
                root = new GeneratorBlock(body.instructions, scope, null, null);
                current = root;
                scope.RetainBindings(Retention);
            }

            internal NeoScriptExecutionResult Run(NSGetterEvaluator.Context ctx)
            {
                var options = NeoScriptExecutionOptions.ForImmediate(ctx.client);
                try
                {
                    while (current is { } block)
                    {
                        if (block.Index == block.Instructions.Length)
                        {
                            Finish(default, ctx, options);
                            continue;
                        }
                        Instruction instruction = block.Instructions[block.Index];
                        ctx.trace.Position(instruction.source);
                        if (instruction is YieldReturnInstruction yielded)
                        {
                            Current = CoerceSetterValue(Eval(yielded.pointer, block.Scope, ctx), elementType, ctx);
                            block.Index++;
                            return NeoScriptExecutionResult.Yielded();
                        }
                        if (instruction is YieldBreakInstruction)
                        {
                            Close(ctx);
                            return default;
                        }
                        if (!instruction.yields)
                        {
                            int start = block.Index++;
                            while (block.Index < block.Instructions.Length && !block.Instructions[block.Index].yields
                                && block.Instructions[block.Index] is not (YieldReturnInstruction or YieldBreakInstruction))
                                block.Index++;
                            NeoScriptExecutionResult result = ExecuteInstructions(ctx.client, block.Instructions,
                                body.typeInfo, block.Scope, ctx, start, null, options, ctx, block.Index);
                            if (result.IsPaused || result.Returned)
                                throw new NeoScriptPreExecutionValidationError("A generator cannot suspend a native call or return a value.");
                            if (!result.IsFallthrough)
                                Finish(result, ctx, options);
                            continue;
                        }
                        block.Index++;
                        Enter(instruction, block, ctx, options);
                    }
                    Current = null;
                    return default;
                }
                catch (Exception error)
                {
                    Unwind(ctx, error);
                    throw;
                }
            }

            private void Enter(Instruction instruction, GeneratorBlock parent, NSGetterEvaluator.Context ctx, NeoScriptExecutionOptions options)
            {
                Instruction[]? instructions = null;
                switch (instruction)
                {
                    case IfInstruction conditional:
                        foreach (var branch in conditional.branches)
                        {
                            if (!EvaluateBoolean(branch.expression, parent.Scope, ctx))
                                continue;
                            instructions = branch.instructions;
                            break;
                        }
                        instructions ??= conditional.elseInstructions;
                        break;
                    case WhileInstruction loop:
                        instructions = loop.instructions;
                        break;
                    case ForInstruction loop:
                        instructions = loop.instructions;
                        break;
                    case ForEachInstruction loop:
                        instructions = loop.instructions;
                        break;
                    case SwitchInstruction selection:
                        instructions = SelectSwitchInstructions(selection, ValidateSwitchInstructionMetadata(selection), Eval(selection.selector, parent.Scope, ctx));
                        break;
                    case TryInstruction region:
                        instructions = region.instructions;
                        break;
                    default:
                        throw new NeoScriptPreExecutionValidationError("Unexpected yield-bearing instruction.");
                }
                if (instructions is null)
                    return;
                GeneratorBlock child = parent.Child(instructions, instruction);
                child.Index = 0;
                child.Scope = parent.Scope;
                current = child;
                switch (instruction)
                {
                    case WhileInstruction loop:
                        if (child.Loop is WhileExecutionState existingWhile)
                            existingWhile.Begin(parent.Scope, options);
                        else
                            child.Loop = new WhileExecutionState(loop, parent.Scope, options);
                        break;
                    case ForInstruction loop:
                        if (child.Loop is ForExecutionState existingFor)
                            existingFor.Begin(parent.Scope, options);
                        else
                            child.Loop = new ForExecutionState(loop, parent.Scope, options);
                        parent.Scope[loop.initializer.id] = Eval(loop.initializer.pointer, parent.Scope, ctx);
                        ValidateRangeLoop(loop, parent.Scope);
                        break;
                    case ForEachInstruction loop:
                        if (child.Loop is ForEachExecutionState existingEach)
                            existingEach.Begin(parent.Scope, options);
                        else
                            child.Loop = new ForEachExecutionState(loop, parent.Scope, options);
                        child.Cursor = NSGetterEvaluator.AcquireSequenceCursor(Eval(loop.collectionPointer, parent.Scope, ctx), loop.binding.typeInfo, ctx);
                        child.CursorAcquired = true;
                        break;
                }
                if (child.Loop is not null)
                {
                    if (!NextIteration(child, true, ctx, options))
                        Pop(child, ctx, true);
                }
                else if (instruction is SwitchInstruction or TryInstruction)
                {
                    if (NeoScriptScopeLayout.DeclaresLocals(instructions))
                    {
                        child.BlockScope ??= parent.Scope.CreateBlock();
                        child.BlockScope.BindBlock(parent.Scope);
                        child.Scope = child.BlockScope;
                    }
                }
            }

            private bool NextIteration(GeneratorBlock block, bool first, NSGetterEvaluator.Context ctx, NeoScriptExecutionOptions options)
            {
                NeoScriptScope scope = block.Parent!.Scope;
                block.Loop!.ResetBodyScope();
                switch (block.Control)
                {
                    case WhileInstruction loop:
                        if ((!first || loop is not DoWhileInstruction) && !EvaluateBoolean(loop.condition, scope, ctx))
                            return false;
                        block.Scope = block.Loop.EnsureBodyScope(scope, loop.instructions, ref loop.bodyLayout);
                        break;
                    case ForInstruction loop:
                        if (!first)
                        {
                            block.Iterator ??= new[] { loop.iterator };
                            NeoScriptExecutionResult result = ExecuteInstructions(ctx.client, block.Iterator, body.typeInfo, scope, ctx, 0, null, options, ctx);
                            if (!result.IsFallthrough || result.IsPaused)
                                throw new NeoScriptPreExecutionValidationError("A generator iterator must complete synchronously.");
                        }
                        if (!EvaluateBoolean(loop.condition, scope, ctx))
                            return false;
                        block.Scope = block.Loop.EnsureBodyScope(scope, loop.instructions, ref loop.bodyLayout);
                        break;
                    case ForEachInstruction loop:
                        if (!NSGetterEvaluator.AdvanceSequenceCursor(block.Cursor, ctx))
                            return false;
                        scope.SetLocal(loop.binding, CoerceSetterValue(NSGetterEvaluator.ReadSequenceCurrent(block.Cursor, ctx), loop.binding.typeInfo, ctx));
                        block.Scope = block.Loop.EnsureBodyScope(scope, loop.instructions, ref loop.bodyLayout);
                        break;
                }
                block.Index = 0;
                return true;
            }

            private void Finish(NeoScriptExecutionResult result, NSGetterEvaluator.Context ctx, NeoScriptExecutionOptions options)
            {
                while (current is { } block)
                {
                    if (block.Loop is not null && (result.IsFallthrough || result.IsContinue))
                    {
                        if (NextIteration(block, false, ctx, options))
                            return;
                        result = default;
                    }
                    else if (block.Loop is not null && result.IsBreak)
                        result = default;
                    else if (block.Control is SwitchInstruction)
                        result = ApplySwitchBodyTransfer(result);
                    Pop(block, ctx, true);
                    if (result.IsFallthrough)
                        return;
                }
                if (!result.IsFallthrough)
                    throw new NeoScriptPreExecutionValidationError("Generator ended with an unconsumed control transfer.");
            }

            private void Pop(GeneratorBlock block, NSGetterEvaluator.Context ctx, bool cleanup)
            {
                current = block.Parent;
                try
                {
                    if (block.CursorAcquired)
                    {
                        block.CursorAcquired = false;
                        if (cleanup)
                            NSGetterEvaluator.DisposeSequenceCursor(block.Cursor, ctx);
                        else if (block.Cursor is NSGetterEvaluator.ScriptCursor cursor)
                            cursor.Abandon();
                    }
                    if (cleanup && block.Control is TryInstruction { finallyInstructions: { } final })
                    {
                        NeoScriptExecutionResult result = ExecuteInstructions(ctx.client, final, body.typeInfo,
                            BlockScopeFor(final, block.Parent!.Scope), ctx, 0, null, NeoScriptExecutionOptions.ForImmediate(ctx.client), ctx);
                        if (!result.IsFallthrough || result.IsPaused)
                            throw new NeoScriptPreExecutionValidationError("Generator cleanup must complete synchronously.");
                    }
                }
                finally
                {
                    block.Cursor = null;
                    block.Loop?.RestoreBinding(block.Parent!.Scope);
                    block.BlockScope?.ResetLocals();
                }
            }

            private void Unwind(NSGetterEvaluator.Context ctx, Exception? failure)
            {
                while (current is { } block)
                {
                    try
                    {
                        Pop(block, ctx, failure is null || NeoScriptErrorClassification.IsAuthoredCatchable(failure));
                    }
                    catch (Exception cleanupError)
                    {
                        failure = cleanupError;
                    }
                }
                Current = null;
                if (failure is not null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }

            internal void Close(NSGetterEvaluator.Context ctx) => Unwind(ctx, null);

            internal void Abandon()
            {
                while (current is { } block)
                {
                    current = block.Parent;
                    if (block.Cursor is NSGetterEvaluator.ScriptCursor cursor)
                        cursor.Abandon();
                    block.Cursor = null;
                    block.CursorAcquired = false;
                    block.Loop?.RestoreBinding(block.Parent!.Scope);
                    block.BlockScope?.ResetLocals();
                }
                Current = null;
            }

            internal void MarkRetained(NSGetterEvaluator.Context ctx)
            {
                Retention.Track(Current);
                Retention.Flush(ctx);
            }

            private sealed class GeneratorBlock
            {
                internal readonly Instruction[] Instructions;
                internal readonly Instruction? Control;
                internal readonly GeneratorBlock? Parent;
                private Dictionary<Instruction[], GeneratorBlock>? children;
                internal NeoScriptScope Scope;
                internal NeoScriptScope? BlockScope;
                internal LoopExecutionState? Loop;
                internal Instruction[]? Iterator;
                internal object? Cursor;
                internal bool CursorAcquired;
                internal int Index;

                internal GeneratorBlock(Instruction[] instructions, NeoScriptScope scope, Instruction? control, GeneratorBlock? parent)
                {
                    Instructions = instructions;
                    Scope = scope;
                    Control = control;
                    Parent = parent;
                }

                internal GeneratorBlock Child(Instruction[] instructions, Instruction control)
                {
                    children ??= new();
                    if (!children.TryGetValue(instructions, out var child))
                    {
                        child = new GeneratorBlock(instructions, Scope, control, this);
                        children.Add(instructions, child);
                    }
                    return child;
                }
            }
        }
    }
}
