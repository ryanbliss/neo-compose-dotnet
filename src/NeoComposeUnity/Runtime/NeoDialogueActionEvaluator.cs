// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;
using Unity.Collections.LowLevel.Unsafe;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Runtime
{
    internal static class NeoDialogueActionEvaluator
    {
        internal static NeoScriptExecutionResult Execute(
            NeoClient client,
            FunctionWithReturnType action,
            NeoDialogueContext dialogueContext,
            INeoDialogueMemoryStore? memoryStore = null,
            INeoDialogueLogger? logger = null)
        {
            var ctx = NeoDialogueConditionEvaluator.BuildContext(
                client,
                dialogueContext,
                memoryStore);
            var scope = new Dictionary<string, object?>
            {
                ["__this__"] = ctx.thisValue,
                ["__root__"] = ctx.rootValue,
                ["__context__"] = ctx.contextValue,
            };
            return NeoScriptExecutor.Execute(
                client,
                action,
                scope,
                ctx,
                NeoScriptExecutionOptions.ForDialogue(client, logger),
                (terminal, _) => NeoScriptExecutor.ValidateStatementTerminal(
                    terminal,
                    "Dialogue action"));
        }
    }

    /// <summary>
    /// Validates or marshals a completed body's terminal result. The frame
    /// context is passed in so NSFunctions can share one normalizer per
    /// resolved signature instead of closing over each invocation's context.
    /// </summary>
    internal delegate NeoScriptExecutionResult NeoScriptTerminalNormalizer(
        NeoScriptExecutionResult terminal,
        NSGetterEvaluator.Context ctx);

    /// <summary>
    /// Shared mutation-capable NeoScript executor. Getters, NSFunctions,
    /// setters, dialogue code actions, and collection callbacks supply their
    /// own scope/context while sharing write targets, calls, and deferred
    /// continuations.
    /// </summary>
    internal static class NeoScriptExecutor
    {
        internal static NeoScriptExecutionResult Execute(
            NeoClient client,
            FunctionWithReturnType body,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options = null,
            NeoScriptTerminalNormalizer? normalizeTerminal = null) =>
            Execute(client, body, new NeoScriptScope(scope), ctx, options,
                normalizeTerminal);

        internal static NeoScriptExecutionResult Execute(
            NeoClient client,
            FunctionWithReturnType body,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options = null,
            NeoScriptTerminalNormalizer? normalizeTerminal = null)
        {
            ValidateBodyForExecution(body);
            ctx.allocationTracker.EnterExecution();
            bool exited = false;
            try
            {
                NeoScriptExecutionResult result = ExecuteInstructions(
                    client,
                    body.instructions,
                    body.typeInfo,
                    scope,
                    ctx,
                    0,
                    null,
                    options);
                if (result.IsPaused)
                {
                    // Only a suspended body needs continuation state; the
                    // synchronous path completes without capturing anything.
                    return new SuspendedExecution(client, body, ctx, normalizeTerminal)
                        .Continue(result);
                }
                return CompleteExecution(
                    client, body, ctx, normalizeTerminal, result, ref exited);
            }
            catch
            {
                if (!exited)
                    ctx.allocationTracker.ExitExecution(client, ctx, default);
                throw;
            }
        }

        private static NeoScriptExecutionResult CompleteExecution(
            NeoClient client,
            FunctionWithReturnType body,
            NSGetterEvaluator.Context ctx,
            NeoScriptTerminalNormalizer? normalizeTerminal,
            NeoScriptExecutionResult result,
            ref bool exited)
        {
            if (result.IsFailed)
                throw result.Failure!;
            if (result.IsBreak || result.IsContinue)
            {
                throw new NSGetterRuntimeError(
                    $"NeoScript body ended with an unconsumed {result.Transfer.ToString().ToLowerInvariant()} transfer; its compiled IR is stale or corrupt.");
            }
            // Terminal marshalling may intentionally replace the CLR
            // value (for example, a receiver-generic Decimal number with
            // its canonical string). Allocation escape detection must
            // still inspect the evaluator's original row-backed object;
            // a copied List/Dictionary would otherwise lose its reverse
            // row identity and an empty returned constructor graph could
            // be reclaimed as though it never escaped.
            NeoScriptExecutionResult allocationTerminal = result;
            if (normalizeTerminal is null)
            {
                result = ValidateTerminalAgainstBody(body, result);
            }
            else
            {
                // NSFunctions resolve receiver-bound Generic return types
                // at invocation time. Their terminal callback is therefore
                // the authoritative validator/marshaller; validating the
                // unresolved compiled body type first would reject valid
                // closed invocations.
                result = normalizeTerminal(result, ctx);
            }
            exited = true;
            ctx.allocationTracker.ExitExecution(client, ctx, in allocationTerminal);
            return result;
        }

        /// <summary>
        /// Allocation-scope bookkeeping for a body that suspended on a
        /// deferred call: the scope exits exactly once, on the terminal
        /// continuation or on abandonment, whichever comes first.
        /// </summary>
        private sealed class SuspendedExecution
        {
            private readonly NeoClient client;
            private readonly FunctionWithReturnType body;
            private readonly NSGetterEvaluator.Context ctx;
            private readonly NeoScriptTerminalNormalizer? normalizeTerminal;
            private bool exited;

            internal SuspendedExecution(
                NeoClient client,
                FunctionWithReturnType body,
                NSGetterEvaluator.Context ctx,
                NeoScriptTerminalNormalizer? normalizeTerminal)
            {
                this.client = client;
                this.body = body;
                this.ctx = ctx;
                this.normalizeTerminal = normalizeTerminal;
            }

            internal NeoScriptExecutionResult Continue(NeoScriptExecutionResult result)
            {
                if (result.IsPaused)
                {
                    return result.Then(Continue).ObserveFailure(Abandon);
                }
                try
                {
                    return CompleteExecution(
                        client, body, ctx, normalizeTerminal, result, ref exited);
                }
                catch
                {
                    Abandon(null);
                    throw;
                }
            }

            private void Abandon(Exception? _)
            {
                if (exited)
                    return;
                exited = true;
                ctx.allocationTracker.ExitExecution(client, ctx, default);
            }
        }

        /// <summary>
        /// Opens one collection-operator callback session: the body is
        /// validated once and allocation tracking spans every entry. The
        /// caller closes it by exiting the context's allocation tracker.
        /// </summary>
        internal static void EnterCallback(
            FunctionWithReturnType body,
            NSGetterEvaluator.Context ctx)
        {
            ValidateBodyForExecution(body);
            ctx.allocationTracker.EnterExecution();
        }

        /// <summary>
        /// Runs one entry of a session <see cref="EnterCallback"/> opened,
        /// with fresh expression replay state.
        /// </summary>
        internal static NeoScriptExecutionResult ExecuteCallback(
            NeoClient client,
            FunctionWithReturnType body,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions options)
        {
            NeoScriptExecutionResult result;
            // A lambda that only returns an expression, the common shape,
            // runs it without the statement loop's frame; only a suspension
            // leaves a result the checks below can reject.
            if (body.instructions is { Length: 1 } instructions
                && instructions[0] is ReturnInstruction { pointer: { } returned }
                && !options.AllowDeferredFunctionCalls)
            {
                NSGetterEvaluator.Context expressionContext = ExpressionContextFor(
                    client,
                    ctx,
                    ExpressionResumeState.Immediate,
                    options);
                try
                {
                    return ValidateTerminalAgainstBody(
                        body,
                        ReturnResult(Eval(returned, scope, expressionContext), body.typeInfo));
                }
                catch (NeoFunctionCallSuspended suspended)
                {
                    result = PauseAtInstruction(client, instructions, body.typeInfo, scope, ctx, 0, ExpressionResumeState.Immediate, suspended, options);
                }
            }
            else
            {
                result = ExecuteInstructions(
                    client,
                    body.instructions,
                    body.typeInfo,
                    scope,
                    ctx,
                    0,
                    null,
                    options);
            }
            if (result.IsFailed)
            {
                throw result.Failure!;
            }
            if (result.IsPaused)
            {
                return result;
            }
            if (result.IsBreak || result.IsContinue)
            {
                throw new NSGetterRuntimeError(
                    $"NeoScript body ended with an unconsumed {result.Transfer.ToString().ToLowerInvariant()} transfer; its compiled IR is stale or corrupt.");
            }
            return ValidateTerminalAgainstBody(body, result);
        }

        private static NeoScriptExecutionResult ReturnResult(object? returnValue, TypeInfo returnTypeInfo)
        {
            if (returnTypeInfo.type == MemberKind.Decimal
                && returnValue is double or float or int or long or short)
            {
                returnValue = NSGetterEvaluator.CoerceDecimalOperand(
                    returnValue,
                    "return");
            }
            return NeoScriptExecutionResult.Completed(
                returned: true,
                returnValue);
        }

        /// <summary>
        /// Every executed body must be stamped with exactly
        /// <see cref="FunctionWithReturnType.CurrentCompilerRevision"/>. The
        /// deployment recompiles its whole fleet on every revision bump, so a
        /// stale or missing stamp means the export and this SDK disagree about
        /// the IR contract rather than that an old-but-valid body is running.
        /// </summary>
        private static void ValidateBodyForExecution(
            FunctionWithReturnType body)
        {
            if (body.compilerRevision is null)
            {
                throw new NeoScriptPreExecutionValidationError(
                    $"NeoScript body carries no compiler revision stamp; this SDK executes only revision {FunctionWithReturnType.CurrentCompilerRevision}. Re-export the project from a Neo Compose deployment at revision {FunctionWithReturnType.CurrentCompilerRevision}.");
            }
            if (body.compilerRevision.Value
                != FunctionWithReturnType.CurrentCompilerRevision)
            {
                throw new NeoScriptPreExecutionValidationError(
                    $"NeoScript body is stamped compiler revision {body.compilerRevision.Value}; this SDK executes only revision {FunctionWithReturnType.CurrentCompilerRevision}. Re-export the project from a deployment at revision {FunctionWithReturnType.CurrentCompilerRevision}, or install the SDK release that matches the export.");
            }
            if (body.validatedForExecution)
                return;
            ValidateControlFlowInstructionMetadata(body.instructions);
            body.validatedForExecution = true;
        }

        private static void ValidateControlFlowInstructionMetadata(
            Instruction[]? instructions)
        {
            if (instructions is null)
            {
                throw new NeoScriptPreExecutionValidationError(
                    "NeoScript body is missing its instructions; its compiled IR is stale or corrupt.");
            }
            foreach (Instruction? instruction in instructions)
            {
                if (instruction is null)
                {
                    throw new NeoScriptPreExecutionValidationError(
                        "NeoScript body contains a null instruction; its compiled IR is stale or corrupt.");
                }
                switch (instruction)
                {
                    case IfInstruction conditional:
                        foreach (ConditionalBranch branch in conditional.branches
                            ?? Array.Empty<ConditionalBranch>())
                        {
                            ValidateControlFlowInstructionMetadata(
                                branch?.instructions);
                        }
                        if (conditional.elseInstructions is not null)
                        {
                            ValidateControlFlowInstructionMetadata(
                                conditional.elseInstructions);
                        }
                        break;
                    case WhileInstruction loop:
                        ValidateWhileInstructionMetadata(loop);
                        ValidateControlFlowInstructionMetadata(loop.instructions);
                        break;
                    case ForInstruction loop:
                        ValidateForInstructionMetadata(loop);
                        ValidateControlFlowInstructionMetadata(loop.instructions);
                        break;
                    case ForEachInstruction loop:
                        ValidateForEachInstructionMetadata(loop);
                        ValidateControlFlowInstructionMetadata(loop.instructions);
                        break;
                    case SwitchInstruction switchInstruction:
                        ValidateSwitchInstructionMetadata(switchInstruction);
                        foreach (SwitchSection section in switchInstruction.sections)
                        {
                            ValidateControlFlowInstructionMetadata(
                                section.instructions);
                        }
                        if (switchInstruction.defaultInstructions is not null)
                        {
                            ValidateControlFlowInstructionMetadata(
                                switchInstruction.defaultInstructions);
                        }
                        break;
                    case TryInstruction tryInstruction:
                        ValidateTryInstructionMetadata(tryInstruction);
                        ValidateControlFlowInstructionMetadata(
                            tryInstruction.instructions);
                        foreach (CatchClause clause in tryInstruction.catches)
                        {
                            ValidateControlFlowInstructionMetadata(
                                clause.instructions);
                        }
                        break;
                }
            }
        }

        private static NeoScriptExecutionResult ValidateTerminalAgainstBody(
            FunctionWithReturnType body,
            NeoScriptExecutionResult execution)
        {
            TypeInfo returnType = body.typeInfo
                ?? throw new NSGetterRuntimeError(
                    "NeoScript body is missing its compiled return type.");
            if (returnType is VoidTypeInfo
                || returnType.type == MemberKind.Void
                // Existing action/setter IR uses Null as its statement-body
                // result marker. Preserve fallthrough for that wire shape;
                // NSGetterEvaluator still enforces an explicit return at the
                // getter boundary after allocation cleanup.
                || returnType.type == MemberKind.Null)
            {
                if (execution.ReturnValue is not null)
                {
                    throw new NSGetterRuntimeError(
                        "Void NeoScript body returned a value; its compiled IR is stale or corrupt.");
                }
                return execution;
            }
            if (!execution.Returned)
            {
                throw new NSGetterRuntimeError(
                    "NeoScript body ended without returning a value; its compiled IR is stale or corrupt.");
            }
            // Ordinary evaluator frames may carry a Neo row id as their
            // internal representation for a declared Class/List/Dictionary
            // return. Public NSFunction boundaries supply normalizeTerminal,
            // which performs the authoritative runtime validation/marshalling
            // against the resolved signature before allocation cleanup.
            return execution;
        }

        /// <summary>
        /// Setters and dialogue actions are statement bodies. Their compiled
        /// <see cref="FunctionWithReturnType.typeInfo"/> historically carries
        /// Null or the property's value type rather than Void, so falling off
        /// the end is successful. A non-null terminal value is nevertheless
        /// stale/corrupt IR and must be rejected before constructor allocation
        /// cleanup decides that the value escaped.
        /// </summary>
        internal static NeoScriptExecutionResult ValidateStatementTerminal(
            NeoScriptExecutionResult execution,
            string subject)
        {
            if (execution.IsPaused)
            {
                throw new InvalidOperationException(
                    $"{subject} terminal normalization received a paused execution.");
            }
            if (execution.ReturnValue is not null)
            {
                throw new NSGetterRuntimeError(
                    $"{subject} returned a value; its compiled IR is stale or corrupt.");
            }
            return execution;
        }

        private static NeoScriptExecutionResult ExecuteInstructions(
            NeoClient client,
            Instruction[] instructions,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            int startIndex,
            ExpressionResumeState? resumeState,
            NeoScriptExecutionOptions? options)
        {
            // Immediate frames record nothing, so every block of one frame can
            // share a single expression context and resume state.
            bool immediate = resumeState is null && options?.AllowDeferredFunctionCalls != true;
            ExpressionResumeState expressionState;
            NSGetterEvaluator.Context actionCtx;
            bool pendingFrame = false;
            if (immediate
                && options is not null
                && options.immediateHandlers is not null
                && ReferenceEquals(ctx.expressionHandlers, options.immediateHandlers))
            {
                // The caller's frame already installed the shared immediate
                // handlers for these options, so this frame (a nested call
                // or statement block) is its own expression context.
                expressionState = ExpressionResumeState.Immediate;
                actionCtx = ctx;
            }
            else if (immediate
                && ReferenceEquals(ctx.immediateExpressionSource, ctx)
                && ReferenceEquals(ctx.immediateExpressionOptions, options)
                && ctx.immediateExpressionContext is { } cachedCtx
                && ReferenceEquals(cachedCtx.client, client)
                && ctx.immediateExpressionState is ExpressionResumeState cachedState)
            {
                expressionState = cachedState;
                actionCtx = cachedCtx;
            }
            else if (!immediate && resumeState is null)
            {
                // A deferred frame records nothing until an instruction can
                // call, so it builds its recording context there: the
                // instructions before it run on the context as is.
                expressionState = ExpressionResumeState.Immediate;
                actionCtx = ctx;
                pendingFrame = true;
            }
            else
            {
                expressionState = resumeState ?? ExpressionResumeState.Immediate;
                actionCtx = BuildExpressionContext(client, ctx, expressionState, options);
                if (immediate)
                {
                    ctx.immediateExpressionSource = ctx;
                    ctx.immediateExpressionContext = actionCtx;
                    ctx.immediateExpressionState = expressionState;
                    ctx.immediateExpressionOptions = options;
                }
            }
            int i = startIndex;
            try
            {
                for (; i < instructions.Length; i++)
                {
                    var instruction = instructions[i];
                    if (pendingFrame && instruction.MayCall)
                    {
                        pendingFrame = false;
                        expressionState = new ExpressionResumeState();
                        actionCtx = BuildExpressionContext(client, ctx, expressionState, options);
                    }
                    // A callSiteId identifies a source location, not one dynamic
                    // invocation. Reset only the per-attempt occurrence counters
                    // so repeated calls from a collection lambda receive stable
                    // frame keys while completed results survive a replay.
                    expressionState.BeginInstructionAttempt();
                    switch (instruction.code)
                    {
                        case InstructionCode.Variable:
                            {
                                var variable = (VariableInstruction)instruction;
                                object? value = NSGetterEvaluator.EvaluateValue(
                                    variable.variable.pointer,
                                    scope,
                                    actionCtx,
                                    out double number);
                                scope.SetEvaluationValue(variable.variable, value, number);
                                break;
                            }
                        case InstructionCode.If:
                            {
                                var ifInstruction = (IfInstruction)instruction;
                                bool matched = false;
                                foreach (var branch in ifInstruction.branches)
                                {
                                    if (EvaluateBoolean(branch.expression, scope, actionCtx))
                                    {
                                        matched = true;
                                        var branchResult = ExecuteInstructions(client, branch.instructions, returnTypeInfo, scope, ctx, 0, null, options);
                                        if (branchResult.IsPaused)
                                            return ResumeInstructionsAfter(client, instructions, returnTypeInfo, scope, ctx, i + 1, options, branchResult, consumeTerminal: false);
                                        if (!branchResult.IsFallthrough)
                                            return branchResult;
                                        break;
                                    }
                                }
                                if (!matched && ifInstruction.elseInstructions != null)
                                {
                                    var elseResult = ExecuteInstructions(client, ifInstruction.elseInstructions, returnTypeInfo, scope, ctx, 0, null, options);
                                    if (elseResult.IsPaused)
                                        return ResumeInstructionsAfter(client, instructions, returnTypeInfo, scope, ctx, i + 1, options, elseResult, consumeTerminal: false);
                                    if (!elseResult.IsFallthrough)
                                        return elseResult;
                                }
                                break;
                            }
                        case InstructionCode.Return:
                            {
                                var returnInstruction = (ReturnInstruction)instruction;
                                return ReturnResult(
                                    returnInstruction.pointer is null
                                        ? null
                                        : Eval(returnInstruction.pointer, scope, actionCtx),
                                    returnTypeInfo);
                            }
                        case InstructionCode.Throw:
                            throw new NSGetterRuntimeError(
                                Eval(((ThrowInstruction)instruction).pointer, scope, actionCtx)?.ToString() ?? "null");
                        case InstructionCode.Assign:
                            {
                                NeoScriptExecutionResult nestedSetter = ExecuteAssign(
                                    client,
                                    (AssignInstruction)instruction,
                                    scope,
                                    actionCtx,
                                    options);
                                if (nestedSetter.IsPaused || nestedSetter.Returned)
                                {
                                    return ResumeInstructionsAfter(client, instructions, returnTypeInfo, scope, ctx, i + 1, options, nestedSetter, consumeTerminal: true);
                                }
                                break;
                            }
                        case InstructionCode.ActionListener:
                            ExecuteActionListener(
                                client,
                                (ActionListenerInstruction)instruction,
                                scope,
                                actionCtx);
                            break;
                        case InstructionCode.CollectionCall:
                            ExecuteCollectionCall(client, (CollectionCallInstruction)instruction, scope, actionCtx, expressionState, i);
                            break;
                        case InstructionCode.FunctionCall:
                            Eval(((FunctionCallInstruction)instruction).call, scope, actionCtx);
                            break;
                        case InstructionCode.While:
                            {
                                var loop = (WhileInstruction)instruction;
                                ValidateWhileInstructionMetadata(loop);
                                var state = new WhileExecutionState(loop, scope, options);
                                NeoScriptExecutionResult loopResult = RunWhile(
                                    client, returnTypeInfo, scope, ctx, options, state);
                                if (loopResult.IsPaused)
                                    return ResumeInstructionsAfter(client, instructions, returnTypeInfo, scope, ctx, i + 1, options, loopResult, consumeTerminal: false);
                                if (!loopResult.IsFallthrough)
                                    return loopResult;
                                break;
                            }
                        case InstructionCode.For:
                            {
                                NeoScriptExecutionResult loopResult = ExecuteFor(
                                    client,
                                    (ForInstruction)instruction,
                                    returnTypeInfo,
                                    scope,
                                    ctx,
                                    options);
                                if (loopResult.IsPaused)
                                    return ResumeInstructionsAfter(client, instructions, returnTypeInfo, scope, ctx, i + 1, options, loopResult, consumeTerminal: false);
                                if (!loopResult.IsFallthrough)
                                    return loopResult;
                                break;
                            }
                        case InstructionCode.ForEach:
                            {
                                NeoScriptExecutionResult loopResult = ExecuteForEach(
                                    client,
                                    (ForEachInstruction)instruction,
                                    returnTypeInfo,
                                    scope,
                                    ctx,
                                    options);
                                if (loopResult.IsPaused)
                                    return ResumeInstructionsAfter(client, instructions, returnTypeInfo, scope, ctx, i + 1, options, loopResult, consumeTerminal: false);
                                if (!loopResult.IsFallthrough)
                                    return loopResult;
                                break;
                            }
                        case InstructionCode.Switch:
                            {
                                NeoScriptExecutionResult switchResult = ExecuteSwitch(
                                    client,
                                    (SwitchInstruction)instruction,
                                    returnTypeInfo,
                                    scope,
                                    ctx,
                                    options);
                                if (switchResult.IsPaused)
                                    return ResumeInstructionsAfter(client, instructions, returnTypeInfo, scope, ctx, i + 1, options, switchResult, consumeTerminal: false);
                                if (!switchResult.IsFallthrough)
                                    return switchResult;
                                break;
                            }
                        case InstructionCode.Try:
                            {
                                NeoScriptExecutionResult tryResult = ExecuteTry(
                                    client,
                                    (TryInstruction)instruction,
                                    returnTypeInfo,
                                    scope,
                                    ctx,
                                    options);
                                if (tryResult.IsPaused)
                                    return ResumeInstructionsAfter(client, instructions, returnTypeInfo, scope, ctx, i + 1, options, tryResult, consumeTerminal: false);
                                if (!tryResult.IsFallthrough)
                                    return tryResult;
                                break;
                            }
                        case InstructionCode.Break:
                            return NeoScriptExecutionResult.Control(
                                NeoScriptControlTransfer.Break);
                        case InstructionCode.Continue:
                            return NeoScriptExecutionResult.Control(
                                NeoScriptControlTransfer.Continue);
                        default:
                            throw new NSGetterRuntimeError(
                                $"Unknown instruction kind {instruction.GetType().Name}");
                    }
                }
            }
            // One region for the frame: per-instruction regions bloat every
            // dispatch's frame. Loops, switch and try pause through their own
            // state, so a suspension escaping one keeps propagating.
            catch (NeoFunctionCallSuspended suspended) when (PausesAtInstruction(instructions[i]))
            {
                return PauseAtInstruction(client, instructions, returnTypeInfo, scope, ctx, i, expressionState, suspended, options);
            }
            return NeoScriptExecutionResult.Completed(returned: false, returnValue: null);
        }

        private static NeoScriptExecutionResult RunWhile(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            WhileExecutionState state)
        {
            try
            {
                while (true)
                {
                    if (state.CheckCondition)
                    {
                        var expressionContext = ExpressionContextFor(
                            client, ctx, state.ExpressionState, options);
                        state.ExpressionState.BeginInstructionAttempt();
                        bool shouldEnter;
                        try
                        {
                            shouldEnter = EvaluateBoolean(
                                state.Instruction.condition, scope, expressionContext);
                        }
                        catch (NeoFunctionCallSuspended suspended)
                        {
                            state.DetachBodyScope();
                            return PauseLoopExpression(
                                suspended, state.ExpressionState, options,
                                () => RunWhile(client, returnTypeInfo, scope, ctx, options, state))
                                .ObserveFailure(_ => state.RestoreBinding(scope));
                        }
                        if (!shouldEnter)
                        {
                            state.RestoreBinding(scope);
                            return NeoScriptExecutionResult.Completed(returned: false, returnValue: null);
                        }
                    }
                    NeoScriptExecutionResult result = ExecuteInstructions(
                        client, state.Instruction.instructions, returnTypeInfo,
                        state.EnsureBodyScope(scope, state.Instruction.instructions, ref state.Instruction.bodyLayout), ctx, 0, null, options);
                    if (result.IsPaused)
                    {
                        state.DetachBodyScope();
                        return ThenWhenCompleted(result, completed =>
                        {
                            state.ResetBodyScope();
                            var terminal = ApplyWhileBodyTransfer(completed);
                            if (terminal is not null)
                            {
                                state.RestoreBinding(scope);
                                return terminal.Value;
                            }
                            state.NextCondition();
                            return RunWhile(client, returnTypeInfo, scope, ctx, options, state);
                        }).ObserveFailure(_ => state.RestoreBinding(scope));
                    }
                    state.ResetBodyScope();
                    var transfer = ApplyWhileBodyTransfer(result);
                    if (transfer is not null)
                    {
                        state.RestoreBinding(scope);
                        return transfer.Value;
                    }
                    state.NextCondition();
                }
            }
            catch
            {
                state.RestoreBinding(scope);
                throw;
            }
        }

        private static bool PausesAtInstruction(Instruction instruction) =>
            instruction.code is not (InstructionCode.While or InstructionCode.For or InstructionCode.ForEach or InstructionCode.Switch or InstructionCode.Try);

        private static NeoScriptExecutionResult? ApplyWhileBodyTransfer(
            NeoScriptExecutionResult result)
        {
            if (!result.EndsLoop)
                return null;
            if (result.IsBreak)
                return NeoScriptExecutionResult.Completed(returned: false, returnValue: null);
            return result;
        }

        private static void ValidateWhileInstructionMetadata(WhileInstruction instruction)
        {
            if (instruction.condition?.condition is null || instruction.instructions is null)
                throw new NeoScriptPreExecutionValidationError(
                    "NeoScript conditional loop contains malformed metadata; its compiled IR is stale or corrupt.");
        }

        private static NeoScriptExecutionResult ExecuteFor(
            NeoClient client,
            ForInstruction instruction,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options)
        {
            ValidateForInstructionMetadata(instruction);
            // A run that completes leaves its state unreferenced, so the
            // instruction keeps it for the next run; a paused run's state
            // belongs to its continuation.
            var state = instruction.idleState as ForExecutionState;
            if (state is null)
            {
                state = new ForExecutionState(instruction, scope, options);
            }
            else
            {
                // Claimed, so a recursive run of this loop builds its own.
                instruction.idleState = null;
                state.Begin(scope, options);
            }
            NeoScriptExecutionResult result = RunFor(client, returnTypeInfo, scope, ctx, options, state);
            if (!result.IsPaused)
            {
                state.Park();
                instruction.idleState = state;
            }
            else
            {
                state.DetachBodyScope();
            }
            return result;
        }

        private static NeoScriptExecutionResult RunFor(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            ForExecutionState state)
        {
            try
            {
                NeoScriptExecutionResult result = RunForCore(
                    client,
                    returnTypeInfo,
                    scope,
                    ctx,
                    options,
                    state);
                return result.IsPaused
                    ? RestoreBindingOnFailure(result, state, scope)
                    : result;
            }
            catch
            {
                state.RestoreBinding(scope);
                throw;
            }
        }

        private static NeoScriptExecutionResult RunForCore(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            ForExecutionState state)
        {
            while (true)
            {
                switch (state.Phase)
                {
                    case ForPhase.Initializer:
                        {
                            NSGetterEvaluator.Context expressionContext =
                                ExpressionContextFor(
                                    client,
                                    ctx,
                                    state.ExpressionState,
                                    options);
                            state.ExpressionState.BeginInstructionAttempt();
                            try
                            {
                                scope[state.Instruction.initializer.id] = Eval(
                                    state.Instruction.initializer.pointer,
                                    scope,
                                    expressionContext);
                            }
                            catch (NeoFunctionCallSuspended suspended)
                            {
                                return PauseFor(
                                    client,
                                    returnTypeInfo,
                                    scope,
                                    ctx,
                                    options,
                                    state,
                                    suspended);
                            }
                            state.MoveTo(ForPhase.Condition);
                            continue;
                        }
                    case ForPhase.Condition:
                        {
                            NSGetterEvaluator.Context expressionContext =
                                ExpressionContextFor(
                                    client,
                                    ctx,
                                    state.ExpressionState,
                                    options);
                            state.ExpressionState.BeginInstructionAttempt();
                            bool shouldEnter;
                            try
                            {
                                shouldEnter = EvaluateBoolean(
                                    state.Instruction.condition,
                                    scope,
                                    expressionContext);
                            }
                            catch (NeoFunctionCallSuspended suspended)
                            {
                                return PauseFor(
                                    client,
                                    returnTypeInfo,
                                    scope,
                                    ctx,
                                    options,
                                    state,
                                    suspended);
                            }
                            if (!shouldEnter)
                            {
                                state.RestoreBinding(scope);
                                return NeoScriptExecutionResult.Completed(
                                    returned: false,
                                    returnValue: null);
                            }
                            state.MoveTo(ForPhase.Body);
                            continue;
                        }
                    case ForPhase.Body:
                        {
                            NeoScriptScope bodyScope = state.EnsureBodyScope(
                                scope,
                                state.Instruction.instructions,
                                ref state.Instruction.bodyLayout);
                            NeoScriptExecutionResult bodyResult = ExecuteInstructions(
                                client,
                                state.Instruction.instructions,
                                returnTypeInfo,
                                bodyScope,
                                ctx,
                                0,
                                null,
                                options);
                            if (bodyResult.IsPaused)
                            {
                                return ResumeForWhenCompleted(
                                    client,
                                    returnTypeInfo,
                                    scope,
                                    ctx,
                                    options,
                                    state,
                                    bodyResult,
                                    afterIterator: false);
                            }
                            state.ResetBodyScope();
                            if (EndsForBody(scope, state, bodyResult))
                                return LoopTerminal(bodyResult);
                            continue;
                        }
                    case ForPhase.Iterator:
                        {
                            NSGetterEvaluator.Context expressionContext =
                                ExpressionContextFor(
                                    client,
                                    ctx,
                                    state.ExpressionState,
                                    options);
                            state.ExpressionState.BeginInstructionAttempt();
                            NeoScriptExecutionResult nestedSetter;
                            try
                            {
                                nestedSetter = ExecuteAssign(
                                    client,
                                    state.Instruction.iterator,
                                    scope,
                                    expressionContext,
                                    options);
                            }
                            catch (NeoFunctionCallSuspended suspended)
                            {
                                return PauseFor(
                                    client,
                                    returnTypeInfo,
                                    scope,
                                    ctx,
                                    options,
                                    state,
                                    suspended);
                            }
                            if (nestedSetter.IsPaused)
                            {
                                return ResumeForWhenCompleted(
                                    client,
                                    returnTypeInfo,
                                    scope,
                                    ctx,
                                    options,
                                    state,
                                    nestedSetter,
                                    afterIterator: true);
                            }
                            state.MoveTo(ForPhase.Condition);
                            continue;
                        }
                    default:
                        throw new NSGetterRuntimeError(
                            "Unknown NeoScript for-loop execution phase.");
                }
            }
        }

        // The pause paths below live outside RunForCore so its hot frame
        // captures nothing: a lambda there allocates its closure on every
        // call, paused or not.
        private static NeoScriptExecutionResult PauseFor(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            ForExecutionState state,
            NeoFunctionCallSuspended suspended) =>
            PauseLoopExpression(
                suspended,
                state.ExpressionState,
                options,
                () => RunFor(client, returnTypeInfo, scope, ctx, options, state));

        private static NeoScriptExecutionResult ResumeForWhenCompleted(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            ForExecutionState state,
            NeoScriptExecutionResult result,
            bool afterIterator) =>
            ThenWhenCompleted(result, settled =>
            {
                if (!afterIterator)
                    return ResumeForAfterBody(client, returnTypeInfo, scope, ctx, options, state, settled);
                state.MoveTo(ForPhase.Condition);
                return RunFor(client, returnTypeInfo, scope, ctx, options, state);
            });

        private static NeoScriptExecutionResult RestoreBindingOnFailure(
            NeoScriptExecutionResult result,
            LoopExecutionState state,
            NeoScriptScope scope) =>
            result.ObserveFailure(_ => state.RestoreBinding(scope));

        private static NeoScriptExecutionResult ResumeForAfterBody(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            ForExecutionState state,
            NeoScriptExecutionResult bodyResult)
        {
            state.ResetBodyScope();
            return EndsForBody(scope, state, bodyResult)
                ? LoopTerminal(bodyResult)
                : RunFor(
                client,
                returnTypeInfo,
                scope,
                ctx,
                options,
                state);
        }

        // Whether a body's transfer ends the loop; if not, the loop moves on.
        // A bool, not a nullable result: that is wider than two registers, so
        // Mono copies it out through a write-barriered range copy.
        private static bool EndsForBody(
            NeoScriptScope scope,
            ForExecutionState state,
            NeoScriptExecutionResult bodyResult)
        {
            if (bodyResult.EndsLoop)
            {
                state.RestoreBinding(scope);
                return true;
            }
            state.MoveTo(ForPhase.Iterator);
            return false;
        }

        /// <summary>The result of a loop its body's transfer ended: a break falls through.</summary>
        private static NeoScriptExecutionResult LoopTerminal(NeoScriptExecutionResult bodyResult) =>
            bodyResult.IsBreak
                ? NeoScriptExecutionResult.Completed(
                    returned: false,
                    returnValue: null)
                : bodyResult;

        private static NeoScriptExecutionResult ExecuteForEach(
            NeoClient client,
            ForEachInstruction instruction,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options)
        {
            ValidateForEachInstructionMetadata(instruction);
            // Reused as ExecuteFor reuses its state.
            var state = instruction.idleState as ForEachExecutionState;
            if (state is null)
            {
                state = new ForEachExecutionState(instruction, scope, options);
            }
            else
            {
                // Claimed, so a recursive run of this loop builds its own.
                instruction.idleState = null;
                state.Begin(scope, options);
            }
            NeoScriptExecutionResult result = RunForEach(
                client,
                returnTypeInfo,
                scope,
                ctx,
                options,
                state);
            if (!result.IsPaused)
            {
                state.Park();
                instruction.idleState = state;
            }
            else
            {
                state.DetachBodyScope();
            }
            return result;
        }

        private static NeoScriptExecutionResult RunForEach(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            ForEachExecutionState state)
        {
            try
            {
                NeoScriptExecutionResult result = RunForEachCore(
                    client,
                    returnTypeInfo,
                    scope,
                    ctx,
                    options,
                    state);
                return result.IsPaused
                    ? RestoreBindingOnFailure(result, state, scope)
                    : result;
            }
            catch
            {
                state.RestoreBinding(scope);
                throw;
            }
        }

        private static NeoScriptExecutionResult RunForEachCore(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            ForEachExecutionState state)
        {
            while (true)
            {
                if (state.Snapshot.Count < 0)
                {
                    NSGetterEvaluator.Context expressionContext =
                        ExpressionContextFor(
                            client,
                            ctx,
                            state.ExpressionState,
                            options);
                    state.ExpressionState.BeginInstructionAttempt();
                    object? collection;
                    try
                    {
                        collection = Eval(
                            state.Instruction.collectionPointer,
                            scope,
                            expressionContext);
                    }
                    catch (NeoFunctionCallSuspended suspended)
                    {
                        return PauseForEach(
                            client,
                            returnTypeInfo,
                            scope,
                            ctx,
                            options,
                            state,
                            suspended);
                    }
                    state.Snapshot.Take(collection, ctx);
                    if (state.Instruction.collectionPointer is CallGetterPointer getterCall)
                        NSGetterEvaluator.RecycleMemoizedList(collection, getterCall.memberId);
                }

                if (state.Index >= state.Snapshot.Count)
                {
                    state.RestoreBinding(scope);
                    return NeoScriptExecutionResult.Completed(
                        returned: false,
                        returnValue: null);
                }

                scope.SetLocal(
                    state.Instruction.binding,
                    CoerceSetterValue(
                        state.Snapshot.Resolve(state.Index, ctx),
                        state.Instruction.binding.typeInfo));
                NeoScriptScope bodyScope = state.EnsureBodyScope(
                    scope,
                    state.Instruction.instructions,
                    ref state.Instruction.bodyLayout);
                NeoScriptExecutionResult bodyResult = ExecuteInstructions(
                    client,
                    state.Instruction.instructions,
                    returnTypeInfo,
                    bodyScope,
                    ctx,
                    0,
                    null,
                    options);
                if (bodyResult.IsPaused)
                {
                    return ResumeForEachWhenCompleted(
                        client,
                        returnTypeInfo,
                        scope,
                        ctx,
                        options,
                        state,
                        bodyResult);
                }
                state.ResetBodyScope();
                if (EndsForEachBody(scope, state, bodyResult))
                    return LoopTerminal(bodyResult);
            }
        }

        // Outside RunForEachCore for the same reason as PauseFor.
        private static NeoScriptExecutionResult PauseForEach(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            ForEachExecutionState state,
            NeoFunctionCallSuspended suspended) =>
            PauseLoopExpression(
                suspended,
                state.ExpressionState,
                options,
                () => RunForEach(client, returnTypeInfo, scope, ctx, options, state));

        private static NeoScriptExecutionResult ResumeForEachWhenCompleted(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            ForEachExecutionState state,
            NeoScriptExecutionResult bodyResult) =>
            ThenWhenCompleted(
                bodyResult,
                afterBody => ResumeForEachAfterBody(client, returnTypeInfo, scope, ctx, options, state, afterBody));

        private static NeoScriptExecutionResult ResumeForEachAfterBody(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            ForEachExecutionState state,
            NeoScriptExecutionResult bodyResult)
        {
            state.ResetBodyScope();
            return EndsForEachBody(scope, state, bodyResult)
                ? LoopTerminal(bodyResult)
                : RunForEach(
                client,
                returnTypeInfo,
                scope,
                ctx,
                options,
                state);
        }

        // As EndsForBody.
        private static bool EndsForEachBody(
            NeoScriptScope scope,
            ForEachExecutionState state,
            NeoScriptExecutionResult bodyResult)
        {
            if (bodyResult.EndsLoop)
            {
                state.RestoreBinding(scope);
                return true;
            }
            state.Index++;
            return false;
        }

        private static void ValidateForInstructionMetadata(
            ForInstruction? instruction)
        {
            if (instruction?.initializer is null
                || string.IsNullOrEmpty(instruction.initializer.id)
                || instruction.initializer.typeInfo is null
                || instruction.initializer.pointer is null
                || instruction.condition?.condition is null
                || instruction.iterator?.target is null
                || instruction.iterator.target.pointer is null
                || instruction.iterator.target.typeInfo is null
                || string.IsNullOrEmpty(instruction.iterator.operatorValue)
                || instruction.iterator.pointer is null
                || instruction.instructions is null)
            {
                throw new NeoScriptPreExecutionValidationError(
                    "NeoScript for loop contains malformed metadata; its compiled IR is stale or corrupt.");
            }
        }

        private static void ValidateForEachInstructionMetadata(
            ForEachInstruction? instruction)
        {
            bool validCollectionType = instruction?.collectionTypeInfo switch
            {
                CollectionTypeInfo collection =>
                    collection.required
                    && collection.entryTypeInfo is not null
                    && (collection.type == MemberKind.List
                        || collection.type == MemberKind.Dictionary),
                LookupTypeInfo lookup =>
                    lookup.required
                    && lookup.entryTypeInfo is not null
                    && lookup.type == MemberKind.Lookup,
                _ => false,
            };
            if (instruction?.binding is null
                || string.IsNullOrEmpty(instruction.binding.id)
                || instruction.binding.typeInfo is null
                || !instruction.binding.isReadonly
                || instruction.collectionPointer is null
                || !validCollectionType
                || instruction.instructions is null)
            {
                throw new NeoScriptPreExecutionValidationError(
                    "NeoScript foreach loop contains malformed metadata; its compiled IR is stale or corrupt.");
            }
        }

        /// <summary>
        /// Validates a switch once and returns its normalized case labels per
        /// section. Instructions are immutable after load, so the labels are
        /// cached on the instruction.
        /// </summary>
        private static SwitchLabelIndex ValidateSwitchInstructionMetadata(
            SwitchInstruction? instruction)
        {
            if (instruction?.sectionByLabel is { } cached)
                return cached;
            if (instruction?.selector is null
                || instruction.sections is null)
            {
                throw new NeoScriptPreExecutionValidationError(
                    "NeoScript switch is missing its selector or sections; its compiled IR is stale or corrupt.");
            }
            ValidateSwitchSelectorType(instruction.selectorTypeInfo);
            var sectionByLabel = new Dictionary<object, int>();
            for (int i = 0; i < instruction.sections.Length; i++)
            {
                SwitchSection? section = instruction.sections[i];
                if (section?.labels is null
                    || section.labels.Length == 0
                    || section.instructions is null)
                {
                    throw new NeoScriptPreExecutionValidationError(
                        "NeoScript switch contains a malformed case section; its compiled IR is stale or corrupt.");
                }
                for (int j = 0; j < section.labels.Length; j++)
                {
                    object label = NormalizeSwitchLabel(
                        section.labels[j],
                        instruction.selectorTypeInfo);
                    if (sectionByLabel.ContainsKey(label))
                    {
                        throw new NeoScriptPreExecutionValidationError(
                            "NeoScript switch contains a duplicate normalized case label; its compiled IR is stale or corrupt.");
                    }
                    sectionByLabel.Add(label, i);
                }
            }
            return instruction.sectionByLabel = new SwitchLabelIndex(sectionByLabel);
        }

        private static void ValidateTryInstructionMetadata(
            TryInstruction? instruction)
        {
            if (instruction?.instructions is null
                || instruction.catches is null
                || instruction.catches.Length == 0)
            {
                throw new NeoScriptPreExecutionValidationError(
                    "NeoScript try/catch is missing its body or catch clauses; its compiled IR is stale or corrupt.");
            }
            var bindingIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < instruction.catches.Length; i++)
            {
                CatchClause? clause = instruction.catches[i];
                if (clause?.binding is null
                    || string.IsNullOrEmpty(clause.binding.id)
                    || !bindingIds.Add(clause.binding.id)
                    || !clause.binding.isReadonly
                    || clause.binding.typeInfo is null
                    || clause.binding.typeInfo.type != MemberKind.String
                    || !clause.binding.typeInfo.required
                    || clause.instructions is null
                    || (clause.filter is null
                        && i != instruction.catches.Length - 1))
                {
                    throw new NeoScriptPreExecutionValidationError(
                        "NeoScript try/catch contains malformed or unordered catch metadata; its compiled IR is stale or corrupt.");
                }
            }
        }

        private static NeoScriptExecutionResult ExecuteSwitch(
            NeoClient client,
            SwitchInstruction instruction,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options)
        {
            if (instruction is null)
            {
                throw new NeoScriptPreExecutionValidationError(
                    "NeoScript switch instruction is missing; its compiled IR is stale or corrupt.");
            }
            SwitchLabelIndex sectionByLabel = ValidateSwitchInstructionMetadata(instruction);
            if (options?.AllowDeferredFunctionCalls == true)
            {
                return RunDeferredSwitch(
                    client,
                    instruction,
                    sectionByLabel,
                    ExpressionResumeState.ForInstruction(instruction, options),
                    returnTypeInfo,
                    scope,
                    ctx,
                    options);
            }
            // An immediate frame cannot suspend, so its selector needs no
            // resume state.
            object? selectorValue = Eval(
                instruction.selector,
                scope,
                ExpressionContextFor(
                    client,
                    ctx,
                    ExpressionResumeState.Immediate,
                    options));
            return RunSwitchSection(
                client,
                SelectSwitchInstructions(instruction, sectionByLabel, selectorValue),
                returnTypeInfo,
                scope,
                ctx,
                options);
        }

        private static NeoScriptExecutionResult RunDeferredSwitch(
            NeoClient client,
            SwitchInstruction instruction,
            SwitchLabelIndex sectionByLabel,
            ExpressionResumeState expressionState,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions options)
        {
            expressionState.BeginInstructionAttempt();
            object? selectorValue;
            try
            {
                selectorValue = Eval(
                    instruction.selector,
                    scope,
                    ExpressionContextFor(
                        client,
                        ctx,
                        expressionState,
                        options));
            }
            catch (NeoFunctionCallSuspended suspended)
            {
                // The resumed attempt replays the selector from the recorded
                // results of its completed calls.
                return PauseLoopExpression(
                    suspended,
                    expressionState,
                    options,
                    () => RunDeferredSwitch(
                        client,
                        instruction,
                        sectionByLabel,
                        expressionState,
                        returnTypeInfo,
                        scope,
                        ctx,
                        options));
            }
            return RunSwitchSection(
                client,
                SelectSwitchInstructions(instruction, sectionByLabel, selectorValue),
                returnTypeInfo,
                scope,
                ctx,
                options);
        }

        private static NeoScriptExecutionResult RunSwitchSection(
            NeoClient client,
            Instruction[]? selectedInstructions,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options)
        {
            if (selectedInstructions is null)
            {
                return NeoScriptExecutionResult.Completed(
                    returned: false,
                    returnValue: null);
            }

            NeoScriptExecutionResult bodyResult = ExecuteInstructions(
                client,
                selectedInstructions,
                returnTypeInfo,
                BlockScopeFor(selectedInstructions, scope),
                ctx,
                0,
                null,
                options);
            if (bodyResult.IsPaused)
                return ThenWhenCompleted(bodyResult, ApplySwitchBodyTransfer);
            return ApplySwitchBodyTransfer(bodyResult);
        }

        /// <summary>
        /// The scope a block runs in: its own child when it declares locals,
        /// which must not escape it, and otherwise the enclosing scope.
        /// </summary>
        private static NeoScriptScope BlockScopeFor(
            Instruction[] instructions,
            NeoScriptScope scope) =>
            NeoScriptScopeLayout.DeclaresLocals(instructions) ? scope.CreateBlock() : scope;

        private static NeoScriptExecutionResult ApplySwitchBodyTransfer(
            NeoScriptExecutionResult bodyResult)
        {
            if (bodyResult.IsBreak)
            {
                return NeoScriptExecutionResult.Completed(
                    returned: false,
                    returnValue: null);
            }
            if (bodyResult.IsFallthrough)
            {
                throw new NeoScriptPreExecutionValidationError(
                    "Corrupt NeoScript switch IR: selected section reached its end.");
            }
            return bodyResult;
        }

        private static bool IsAuthoredCatchableError(Exception exception) =>
            NeoScriptErrorClassification.IsAuthoredCatchable(exception);

        private static NeoScriptExecutionResult ExecuteTry(
            NeoClient client,
            TryInstruction instruction,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options)
        {
            var state = new TryExecutionState(instruction, options);
            return RunTry(
                client,
                returnTypeInfo,
                scope,
                ctx,
                options,
                state);
        }

        private static NeoScriptExecutionResult RunTry(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            TryExecutionState state)
        {
            while (true)
            {
                switch (state.Phase)
                {
                    case TryPhase.Body:
                        return RunProtectedTryBody(
                            client,
                            returnTypeInfo,
                            scope,
                            ctx,
                            options,
                            state);
                    case TryPhase.Filter:
                        {
                            CatchClause clause = state.CurrentClause;
                            NeoScriptScope catchScope =
                                state.EnsureCatchScope(scope);
                            NSGetterEvaluator.Context expressionContext =
                                ExpressionContextFor(
                                    client,
                                    ctx,
                                    state.ExpressionState,
                                    options);
                            state.ExpressionState.BeginInstructionAttempt();
                            bool matched;
                            try
                            {
                                matched = EvaluateBoolean(
                                    clause.filter!,
                                    catchScope,
                                    expressionContext);
                            }
                            catch (NeoFunctionCallSuspended suspended)
                            {
                                NeoScriptExecutionResult paused =
                                    PauseLoopExpression(
                                        suspended,
                                        state.ExpressionState,
                                        options,
                                        () => RunTry(
                                            client,
                                            returnTypeInfo,
                                            scope,
                                            ctx,
                                            options,
                                            state));
                                return paused
                                    .RecoverFailure(exception =>
                                    {
                                        if (state.Phase != TryPhase.Filter
                                            || !IsAuthoredCatchableError(exception))
                                        {
                                            return null;
                                        }
                                        state.RejectCurrentClause();
                                        return RunTry(
                                            client,
                                            returnTypeInfo,
                                            scope,
                                            ctx,
                                            options,
                                            state);
                                    });
                            }
                            catch (NSGetterRuntimeError exception) when (IsAuthoredCatchableError(exception))
                            {
                                state.RejectCurrentClause();
                                continue;
                            }
                            if (!matched)
                            {
                                state.RejectCurrentClause();
                                continue;
                            }
                            state.SelectCurrentClause();
                            continue;
                        }
                    case TryPhase.CatchBody:
                        {
                            NeoScriptScope catchScope =
                                state.EnsureCatchScope(scope);
                            NeoScriptExecutionResult catchResult = ExecuteInstructions(
                                client,
                                state.CurrentClause.instructions,
                                returnTypeInfo,
                                catchScope,
                                ctx,
                                0,
                                null,
                                options);
                            if (catchResult.IsPaused)
                            {
                                return ThenWhenCompleted(
                                        catchResult,
                                        CompleteCatchBody);
                            }
                            return CompleteCatchBody(catchResult);

                            NeoScriptExecutionResult CompleteCatchBody(
                                NeoScriptExecutionResult completed)
                            {
                                state.Complete();
                                return completed;
                            }
                        }
                    case TryPhase.NoMatch:
                        return NeoScriptExecutionResult.Failed(
                            state.CompleteWithoutMatch());
                    case TryPhase.Completed:
                        throw new NSGetterRuntimeError(
                            "NeoScript try/catch execution resumed after completion; its compiled IR is stale or corrupt.");
                    default:
                        throw new NSGetterRuntimeError(
                            "Unknown NeoScript try/catch execution phase.");
                }
            }
        }

        private static NeoScriptExecutionResult RunProtectedTryBody(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            TryExecutionState state)
        {
            NeoScriptScope tryScope =
                state.EnsureTryScope(scope);
            NeoScriptExecutionResult bodyResult;
            try
            {
                bodyResult = ExecuteInstructions(
                    client,
                    state.Instruction.instructions,
                    returnTypeInfo,
                    tryScope,
                    ctx,
                    0,
                    null,
                    options);
            }
            catch (NSGetterRuntimeError exception) when (IsAuthoredCatchableError(exception))
            {
                state.BeginCatches(exception);
                return RunTry(
                    client,
                    returnTypeInfo,
                    scope,
                    ctx,
                    options,
                    state);
            }

            if (bodyResult.IsPaused)
            {
                return ThenWhenCompleted(
                        bodyResult.RecoverFailure(exception =>
                        {
                            if (state.Phase != TryPhase.Body
                                || !IsAuthoredCatchableError(exception))
                            {
                                return null;
                            }
                            var error = (NSGetterRuntimeError)exception;
                            state.BeginCatches(error);
                            return RunTry(
                                client,
                                returnTypeInfo,
                                scope,
                                ctx,
                                options,
                                state);
                        }),
                        CompleteTryBody);
            }
            return CompleteTryBody(bodyResult);

            NeoScriptExecutionResult CompleteTryBody(
                NeoScriptExecutionResult completed)
            {
                if (state.Phase == TryPhase.Body
                    && completed.IsFailed
                    && completed.Failure is NSGetterRuntimeError error
                    && IsAuthoredCatchableError(error))
                {
                    state.BeginCatches(error);
                    return RunTry(
                        client,
                        returnTypeInfo,
                        scope,
                        ctx,
                        options,
                        state);
                }
                state.Complete();
                return completed;
            }
        }

        // One section map covers one selector type, so a case key needs no
        // type tag: numbers, bools, strings and enum option ids key directly.
        private static readonly object NullSwitchLabel = new();

        private static object SwitchSelectorKey(
            TypeInfo selectorTypeInfo,
            object? value)
        {
            if (value is null)
            {
                if (selectorTypeInfo.required)
                {
                    throw new NSGetterRuntimeError(
                        "NeoScript switch selector evaluated to null for a required selector type; its compiled IR is stale or corrupt.");
                }
                return NullSwitchLabel;
            }

            switch (selectorTypeInfo.type)
            {
                case MemberKind.Int:
                    if (!TryNormalizeSwitchInteger(value, out double number))
                    {
                        throw SwitchValueTypeError("selector", selectorTypeInfo);
                    }
                    return NSGetterEvaluator.Box(number);
                case MemberKind.String:
                    if (value is not string text)
                    {
                        throw SwitchValueTypeError("selector", selectorTypeInfo);
                    }
                    return text;
                case MemberKind.Bool:
                    if (value is not bool boolean)
                    {
                        throw SwitchValueTypeError("selector", selectorTypeInfo);
                    }
                    return NSGetterEvaluator.Box(boolean);
                case MemberKind.Enum:
                    if (value is not object?[] options
                        || options.Length != 1
                        || options[0] is not string optionId
                        || string.IsNullOrEmpty(optionId))
                    {
                        throw SwitchValueTypeError("selector", selectorTypeInfo);
                    }
                    return optionId;
                default:
                    throw SwitchValueTypeError("selector", selectorTypeInfo);
            }
        }

        /// <summary>The instructions a selector value runs, or null when no section and no default match.</summary>
        private static Instruction[]? SelectSwitchInstructions(
            SwitchInstruction instruction,
            SwitchLabelIndex sectionByLabel,
            object? value) =>
            sectionByLabel.TryGetSection(SwitchSelectorKey(instruction.selectorTypeInfo, value), out int section)
                ? instruction.sections[section].instructions
                : instruction.defaultInstructions;

        /// <summary>
        /// A switch's normalized case labels and their sections. Most switches
        /// have a few labels, which a scan compares faster than a dictionary
        /// hashes the selector.
        /// </summary>
        internal sealed class SwitchLabelIndex
        {
            private const int ScannedLabels = 8;
            private readonly object[]? labels;
            private readonly int[]? sections;
            private readonly Dictionary<object, int>? wide;

            internal SwitchLabelIndex(Dictionary<object, int> sectionByLabel)
            {
                if (sectionByLabel.Count > ScannedLabels)
                {
                    wide = sectionByLabel;
                    return;
                }
                labels = new object[sectionByLabel.Count];
                sections = new int[sectionByLabel.Count];
                int index = 0;
                foreach (var pair in sectionByLabel)
                {
                    labels[index] = pair.Key;
                    sections[index] = pair.Value;
                    index++;
                }
            }

            internal bool TryGetSection(object key, out int section)
            {
                if (wide is not null)
                    return wide.TryGetValue(key, out section);
                for (int i = 0; i < labels!.Length; i++)
                {
                    if (labels[i].Equals(key))
                    {
                        section = sections![i];
                        return true;
                    }
                }
                section = -1;
                return false;
            }
        }

        private static object NormalizeSwitchLabel(
            Value label,
            TypeInfo selectorTypeInfo)
        {
            if (label?.typeInfo is null)
            {
                throw new NeoScriptPreExecutionValidationError(
                    "NeoScript switch case label is missing type information; its compiled IR is stale or corrupt.");
            }
            TypeInfo labelTypeInfo = label.typeInfo;
            if (labelTypeInfo.type == MemberKind.Null)
            {
                if (!labelTypeInfo.required
                    || label.value?.Type != Newtonsoft.Json.Linq.JTokenType.Null
                    || selectorTypeInfo.required)
                {
                    throw SwitchMetadataTypeError(selectorTypeInfo);
                }
                return NullSwitchLabel;
            }

            if (!labelTypeInfo.required
                || labelTypeInfo.type != selectorTypeInfo.type
                || selectorTypeInfo.type == MemberKind.Enum
                    && (labelTypeInfo is not EnumTypeInfo labelEnum
                        || selectorTypeInfo is not EnumTypeInfo selectorEnum
                        || !string.Equals(
                            labelEnum.enumId,
                            selectorEnum.enumId,
                            StringComparison.Ordinal)))
            {
                throw SwitchMetadataTypeError(selectorTypeInfo);
            }

            Newtonsoft.Json.Linq.JToken? token = label.value;
            switch (selectorTypeInfo.type)
            {
                case MemberKind.Int:
                    if ((token?.Type != Newtonsoft.Json.Linq.JTokenType.Integer
                            && token?.Type != Newtonsoft.Json.Linq.JTokenType.Float)
                        || !TryNormalizeSwitchInteger(
                            token.ToObject<double>(),
                            out double integer))
                    {
                        throw SwitchMetadataTypeError(selectorTypeInfo);
                    }
                    return integer;
                case MemberKind.String:
                    if (token?.Type != Newtonsoft.Json.Linq.JTokenType.String)
                    {
                        throw SwitchMetadataTypeError(selectorTypeInfo);
                    }
                    return token.ToObject<string>()!;
                case MemberKind.Bool:
                    if (token?.Type != Newtonsoft.Json.Linq.JTokenType.Boolean)
                    {
                        throw SwitchMetadataTypeError(selectorTypeInfo);
                    }
                    return token.ToObject<bool>();
                case MemberKind.Enum:
                    if (token is not Newtonsoft.Json.Linq.JArray enumOptions
                        || enumOptions.Count != 1
                        || enumOptions[0]?.Type
                            != Newtonsoft.Json.Linq.JTokenType.String
                        || string.IsNullOrEmpty(enumOptions[0]!.ToObject<string>()))
                    {
                        throw SwitchMetadataTypeError(selectorTypeInfo);
                    }
                    return enumOptions[0]!.ToObject<string>()!;
                default:
                    throw SwitchMetadataTypeError(selectorTypeInfo);
            }
        }

        private static void ValidateSwitchSelectorType(TypeInfo? selectorTypeInfo)
        {
            if (selectorTypeInfo is null
                || selectorTypeInfo.type != MemberKind.Int
                    && selectorTypeInfo.type != MemberKind.String
                    && selectorTypeInfo.type != MemberKind.Bool
                    && selectorTypeInfo.type != MemberKind.Enum
                || selectorTypeInfo.type == MemberKind.Enum
                    && (selectorTypeInfo is not EnumTypeInfo enumType
                        || string.IsNullOrEmpty(enumType.enumId)))
            {
                throw new NeoScriptPreExecutionValidationError(
                    "NeoScript switch selector type must be int, string, bool, or enum; its compiled IR is stale or corrupt.");
            }
        }

        private static bool TryNormalizeSwitchInteger(
            object value,
            out double number)
        {
            switch (value)
            {
                case int integer:
                    number = integer;
                    break;
                case long integer:
                    number = integer;
                    break;
                case short integer:
                    number = integer;
                    break;
                case double floating:
                    number = floating;
                    break;
                case float floating:
                    number = floating;
                    break;
                default:
                    number = 0d;
                    return false;
            }
            if (double.IsNaN(number)
                || double.IsInfinity(number)
                || !NeoNumbers.IsWhole(number)
                || Math.Abs(number) > 9007199254740991d)
            {
                return false;
            }
            // -0 and 0 are one case.
            if (number == 0d)
                number = 0d;
            return true;
        }

        private static NSGetterRuntimeError SwitchValueTypeError(
            string subject,
            TypeInfo selectorTypeInfo) => new(
                $"NeoScript switch {subject} is inconsistent with declared " +
                $"{selectorTypeInfo.type} selector type; its compiled IR is stale or corrupt.");

        private static NeoScriptPreExecutionValidationError SwitchMetadataTypeError(
            TypeInfo selectorTypeInfo) => new(
                "NeoScript switch case label is inconsistent with declared " +
                $"{selectorTypeInfo.type} selector type; its compiled IR is stale or corrupt.");

        /// <summary>
        /// Expression context for a statement-level expression (loop phases,
        /// switch selectors, catch filters). Immediate frames reuse the
        /// context their block already carries; only recording frames need a
        /// fresh context per attempt.
        /// </summary>
        private static NSGetterEvaluator.Context ExpressionContextFor(
            NeoClient client,
            NSGetterEvaluator.Context ctx,
            ExpressionResumeState expressionState,
            NeoScriptExecutionOptions? options)
        {
            if (options?.AllowDeferredFunctionCalls == true)
            {
                // A deferred frame gives the shared state only to an
                // instruction that cannot call: it never uses the handlers.
                if (ReferenceEquals(expressionState, ExpressionResumeState.Immediate))
                    return ctx;
            }
            else
            {
                if (options is not null
                    && options.immediateHandlers is not null
                    && ReferenceEquals(ctx.expressionHandlers, options.immediateHandlers))
                {
                    return ctx;
                }
                if (ReferenceEquals(ctx.immediateExpressionSource, ctx)
                    && ReferenceEquals(ctx.immediateExpressionOptions, options)
                    && ctx.immediateExpressionContext is { } cached
                    && ReferenceEquals(cached.client, client))
                {
                    return cached;
                }
                if (ReferenceEquals(expressionState, ExpressionResumeState.Immediate))
                {
                    // Cached as an immediate frame caches its own.
                    NSGetterEvaluator.Context built = BuildExpressionContext(client, ctx, expressionState, options);
                    ctx.immediateExpressionSource = ctx;
                    ctx.immediateExpressionContext = built;
                    ctx.immediateExpressionState = expressionState;
                    ctx.immediateExpressionOptions = options;
                    return built;
                }
            }
            return BuildExpressionContext(client, ctx, expressionState, options);
        }

        // The handlers are immutable and ExitFunction restores the caller's, so
        // installing them on the function's context here avoids cloning it
        // again when its first instruction executes. frame is the in-place
        // frame the body runs in, or -1 on a context of its own.
        internal static void PrepareFunctionContext(
            NSGetterEvaluator.Context ctx, NeoScriptExecutionOptions options, int frame)
        {
            if (options.AllowDeferredFunctionCalls)
                return;
            EnsureImmediateHandlers(ctx.client, options);
            ctx.BindFrameHandlers(frame, options.immediateHandlers!);
        }

        private static void EnsureImmediateHandlers(NeoClient client, NeoScriptExecutionOptions options)
        {
            if (options.immediateHandlers is not null)
                return;
            InitializeImmediateHandlers(client, options);
        }

        private static void InitializeImmediateHandlers(NeoClient client, NeoScriptExecutionOptions options)
        {
            options.immediateHandlers = new ImmediateHandlers(client, options);
        }

        private static NSGetterEvaluator.Context BuildExpressionContext(
            NeoClient client,
            NSGetterEvaluator.Context ctx,
            ExpressionResumeState expressionState,
            NeoScriptExecutionOptions? options)
        {
            // Immediate frames cannot resume. Retaining every nested call's
            // result and dynamic occurrence key only adds allocations there.
            if (options?.AllowDeferredFunctionCalls != true)
                expressionState.DisableRecording();
            if (options is not null
                && ReferenceEquals(expressionState, ExpressionResumeState.Immediate)
                && ReferenceEquals(options.Client, client))
            {
                // The immediate resume state is stateless, so the handlers
                // depend only on (client, options): build them once and let
                // every nested frame inherit them through the context fork.
                EnsureImmediateHandlers(client, options);
                return ctx.WithExpressionHandlers(options.immediateHandlers!);
            }
            return ctx.WithExpressionHandlers(new FrameHandlers(client, expressionState, options));
        }

        /// <summary>
        /// The handlers every immediate frame of one (client, options) pair
        /// shares: the immediate resume state is stateless.
        /// </summary>
        private sealed class ImmediateHandlers : NSGetterEvaluator.Context.ExpressionHandlers
        {
            private readonly NeoClient client;
            private readonly NeoScriptExecutionOptions options;

            internal ImmediateHandlers(NeoClient client, NeoScriptExecutionOptions options)
            {
                this.client = client;
                this.options = options;
            }

            internal override object? Call(CallFunctionPointer pointer, NeoScriptScope scope, NSGetterEvaluator.Context ctx) =>
                CallFunction(client, pointer, scope, ctx, options, CallSiteKey(pointer));

            internal override object? Initialize(ObjectInitializerPointer pointer, NeoScriptScope scope, NSGetterEvaluator.Context ctx) =>
                EvalObjectInitializer(pointer, scope, ctx, ExpressionResumeState.Immediate, options);
        }

        /// <summary>The handlers of one frame, recording into its resume state.</summary>
        private sealed class FrameHandlers : NSGetterEvaluator.Context.ExpressionHandlers
        {
            private readonly NeoClient client;
            private readonly ExpressionResumeState expressionState;
            private readonly NeoScriptExecutionOptions? options;

            internal FrameHandlers(
                NeoClient client,
                ExpressionResumeState expressionState,
                NeoScriptExecutionOptions? options)
            {
                this.client = client;
                this.expressionState = expressionState;
                this.options = options;
            }

            internal override object? Call(CallFunctionPointer pointer, NeoScriptScope scope, NSGetterEvaluator.Context ctx) =>
                EvalFunctionCall(client, pointer, scope, ctx, expressionState, options);

            internal override object? Initialize(ObjectInitializerPointer pointer, NeoScriptScope scope, NSGetterEvaluator.Context ctx) =>
                EvalObjectInitializer(pointer, scope, ctx, expressionState, options);
        }

        private static NeoScriptExecutionResult PauseLoopExpression(
            NeoFunctionCallSuspended suspended,
            ExpressionResumeState expressionState,
            NeoScriptExecutionOptions? options,
            Func<NeoScriptExecutionResult> resume)
        {
            options?.WarnDeferred(suspended.MemberId);
            return ResumeAfterNestedCall(suspended.Execution);

            NeoScriptExecutionResult ResumeAfterNestedCall(
                NeoScriptExecutionResult nestedResult)
            {
                if (nestedResult.IsPaused)
                {
                    return nestedResult.Then(ResumeAfterNestedCall);
                }
                expressionState.StoreValue(
                    suspended.ResumeKey,
                    nestedResult.ReturnValue);
                return resume();
            }
        }

        private static NeoScriptExecutionResult PauseAtInstruction(
            NeoClient client,
            Instruction[] instructions,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            int instructionIndex,
            ExpressionResumeState expressionState,
            NeoFunctionCallSuspended suspended,
            NeoScriptExecutionOptions? options)
        {
            options?.WarnDeferred(suspended.MemberId);
            return ResumeAfterNestedCall(suspended.Execution);

            NeoScriptExecutionResult ResumeAfterNestedCall(
                NeoScriptExecutionResult nestedResult)
            {
                if (nestedResult.IsPaused)
                {
                    return nestedResult.Then(ResumeAfterNestedCall);
                }
                expressionState.StoreValue(
                    suspended.ResumeKey,
                    nestedResult.ReturnValue);
                return ExecuteInstructions(
                    client,
                    instructions,
                    returnTypeInfo,
                    scope,
                    ctx,
                    instructionIndex,
                    expressionState,
                    options);
            }
        }

        /// <summary>
        /// Continues a frame at <paramref name="nextIndex"/> once a nested
        /// block settles. Lives outside <see cref="ExecuteInstructions"/> so
        /// the frame loop itself captures nothing: a lambda in that method
        /// would allocate its closure on every frame, settled or not.
        /// <paramref name="consumeTerminal"/> continues past a nested
        /// setter's own return; otherwise a non-fallthrough result ends
        /// the frame.
        /// </summary>
        private static NeoScriptExecutionResult ResumeInstructionsAfter(
            NeoClient client,
            Instruction[] instructions,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            int nextIndex,
            NeoScriptExecutionOptions? options,
            NeoScriptExecutionResult result,
            bool consumeTerminal)
        {
            return ThenWhenCompleted(result, settled =>
                !consumeTerminal && !settled.IsFallthrough
                    ? settled
                    : ExecuteInstructions(client, instructions, returnTypeInfo, scope, ctx, nextIndex, null, options));
        }

        private static NeoScriptExecutionResult ThenWhenCompleted(
            NeoScriptExecutionResult result,
            Func<NeoScriptExecutionResult, NeoScriptExecutionResult> next)
        {
            if (result.IsPaused)
            {
                return result.Then(resumed => ThenWhenCompleted(resumed, next));
            }
            return next(result);
        }

        // A plain write returns the default fallthrough result, not a
        // nullable one: that is wider than two registers, so Mono copies it
        // out through a write-barriered range copy on every assignment.
        private static NeoScriptExecutionResult ExecuteAssign(
            NeoClient client,
            AssignInstruction instruction,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options)
        {
            if (instruction.target.pointer is VariablePointer variablePointer)
            {
                // A number stays unboxed into the local, as a declaration's does.
                object? local = NSGetterEvaluator.EvaluateValue(instruction.pointer, scope, ctx, out double number);
                if (instruction.target.writability == WritabilityKind.ReadOnly)
                {
                    throw new NSGetterRuntimeError(
                        "Cannot assign to a read-only NeoScript binding.");
                }
                if (scope.TryGetReadOnlyError(variablePointer.variableId, out string? readOnlyError))
                {
                    throw new NSGetterRuntimeError(readOnlyError!);
                }
                if (instruction.target.typeInfo.type == MemberKind.Decimal)
                {
                    local = CoerceSetterValue(
                        NSGetterEvaluator.ArithmeticValue.Box(local, number),
                        instruction.target.typeInfo);
                }
                scope.Assign(variablePointer, local, number);
                return default;
            }

            object? rhs = Eval(instruction.pointer, scope, ctx);

            if (instruction.target.writability == WritabilityKind.Setter)
            {
                return ExecuteSetterAssignment(
                    client,
                    instruction,
                    rhs,
                    scope,
                    ctx,
                    options);
            }
            // Existing storage/local IR carries the operator-applied value in
            // `pointer`; only Setter writability needs to re-read its getter
            // because the property has no storage row of its own.
            object? assigned = CoerceSetterValue(rhs, instruction.target.typeInfo);
            NeoResolvedWriteTarget target;
            if (instruction.target.pointer is KeyOfPointer keyOfPointer)
            {
                object? receiver = Eval(keyOfPointer.keyOf.pointer, scope, ctx);
                if (receiver is NeoScriptObject { attachedId: null } detached
                    && WritesSessionTarget(instruction.target.writability)
                    && Eval(keyOfPointer.keyOf.key, scope, ctx) is string key
                    && NSGetterEvaluator.TryWriteDetachedMember(detached, key, assigned, ctx, keyOfPointer.keyOf))
                {
                    return default;
                }
                target = ResolveKeyOfWriteTarget(client, instruction.target, keyOfPointer, receiver, scope, ctx, rent: true);
                if (target is NeoClassMemberWriteTarget rented)
                {
                    WriteRented(client, rented, assigned, ctx);
                    return default;
                }
            }
            else
            {
                target = ResolveTarget(client, instruction.target, scope, ctx);
            }
            target.Write(client, assigned, ctx);
            return default;
        }

        // Its own method so the exception region stays off ExecuteAssign's frame.
        private static void WriteRented(
            NeoClient client,
            NeoClassMemberWriteTarget target,
            object? value,
            NSGetterEvaluator.Context ctx)
        {
            try
            {
                target.Write(client, value, ctx);
            }
            finally
            {
                target.Release();
            }
        }

        /// <summary>
        /// Writabilities that resolve to a Session target, the only store a
        /// detached object's slots stand in for.
        /// </summary>
        private static bool WritesSessionTarget(string? writability) =>
            writability is null
                or WritabilityKind.Session
                or WritabilityKind.ImmutableToSessionLookup
                or WritabilityKind.Local
                or WritabilityKind.Runtime;

        private static void MarkReadOnlyBinding(
            NeoScriptScope scope,
            string variableId,
            string error)
        {
            scope.MarkReadOnly(variableId, error);
        }

        private static void UnmarkReadOnlyBinding(
            NeoScriptScope scope,
            string variableId)
        {
            scope.UnmarkReadOnly(variableId);
        }

        /// <summary>
        /// Executes <c>action += listener</c> / <c>action -= listener</c>
        /// (P62 §3.2). A subscription is an ordinary member-value write: read
        /// the current listener set, apply the set operation keyed by the
        /// listener's <c>(memberId, valueId)</c> identity, then write the full
        /// post-mutation value back through the same target an assign uses.
        /// Adding a present identity and removing an absent one are both
        /// no-ops, so a re-executed subscription path cannot double-subscribe.
        /// </summary>
        private static void ExecuteActionListener(
            NeoClient client,
            ActionListenerInstruction instruction,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            bool add = instruction is AddActionListenerInstruction;
            string operatorLabel = add ? "+=" : "-=";
            NeoDelegateValue listener = ResolveListenerTarget(
                Eval(instruction.listener, scope, ctx),
                operatorLabel);
            string identity = NeoActionValue.ListenerIdentity(listener);

            NeoResolvedWriteTarget target = ResolveTarget(
                client,
                instruction.target,
                scope,
                ctx);
            NeoActionValue current = ReadActionValue(
                target.ReadCurrentValue(client, ctx),
                operatorLabel);

            var next = new NeoActionValue();
            bool present = false;
            foreach (NeoDelegateValue existing in current.listeners)
            {
                bool matches = string.Equals(
                    NeoActionValue.ListenerIdentity(existing),
                    identity,
                    StringComparison.Ordinal);
                if (matches)
                    present = true;
                if (matches && !add)
                    continue;
                next.listeners.Add(existing);
            }
            if (add)
            {
                if (present)
                    return;
                // The evaluated pointer may be a live captured value carrying
                // the subscribing row's lexical environment. Persist the
                // identity fields only, exactly as every other write path does
                // (NeoMemberActionWritable.AddListener, MemberValueFactory,
                // NeoClient.CloneValueRow).
                next.listeners.Add(listener.PersistedCopy());
            }
            else if (!present)
            {
                return;
            }
            target.Write(client, next, ctx);
        }

        private static NeoDelegateValue ResolveListenerTarget(
            object? evaluated,
            string operatorLabel)
        {
            NeoDelegateValue? listener = evaluated switch
            {
                NeoDelegateValue typed => typed,
                JObject json => json.ToObject<NeoDelegateValue>(),
                _ => null,
            };
            if (listener is null)
            {
                throw new NSGetterRuntimeError(
                    $"NSAction '{operatorLabel}' requires a member-target listener; the right-hand side evaluated to {evaluated?.GetType().Name ?? "null"}.");
            }
            if (listener.IsClosure || !listener.IsMemberTarget)
            {
                throw new NSGetterRuntimeError(
                    $"NSAction '{operatorLabel}' requires a member-target listener; a closure has no identity to deduplicate or remove by.");
            }
            return listener;
        }

        private static NeoActionValue ReadActionValue(
            object? current,
            string operatorLabel)
        {
            switch (current)
            {
                case null:
                    // The empty set is an action's rest state, so an absent
                    // stored value subscribes onto a fresh listener set.
                    return new NeoActionValue();
                case NeoActionValue typed:
                    return typed;
                case JObject json:
                    return json.ToObject<NeoActionValue>() ?? new NeoActionValue();
                default:
                    throw new NSGetterRuntimeError(
                        $"NSAction '{operatorLabel}' target holds {current.GetType().Name}, which is not a listener set.");
            }
        }

        private static void ExecuteCollectionCall(
            NeoClient client,
            CollectionCallInstruction instruction,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            ExpressionResumeState expressionState,
            int instructionIndex)
        {
            CollectionMutation mutate;
            if (!expressionState.Recording)
            {
                mutate = ResolveCollectionMutation(client, instruction, scope, ctx);
            }
            else
            {
                // A frame that may suspend keeps the receiver it resolved, so
                // a resumed call does not read it again.
                string receiverKey = "collection-receiver:" + instructionIndex;
                if (expressionState.TryGet(receiverKey, out object? cached, out _))
                    mutate = (CollectionMutation)cached!;
                else
                {
                    mutate = ResolveCollectionMutation(client, instruction, scope, ctx);
                    expressionState.StoreValue(receiverKey, mutate);
                }
            }
            // Every mutation reads its arguments without keeping the array.
            object?[] args = NeoArgumentArrays.Rent(instruction.args.Length);
            try
            {
                for (int i = 0; i < instruction.args.Length; i++)
                    args[i] = Eval(instruction.args[i], scope, ctx);
                mutate.Apply(client, instruction, scope, ctx, args);
            }
            finally
            {
                NeoArgumentArrays.Return(args);
            }
        }

        /// <summary>
        /// A collection call's receiver, resolved before its arguments
        /// evaluate. A struct: an immediate frame applies it in place, and
        /// only a frame that may suspend boxes it into its resume state.
        /// </summary>
        private readonly struct CollectionMutation
        {
            private enum Kind
            {
                // A detached object's List slot; subject is the object.
                DetachedList,
                // A local collection value; subject is the value.
                Local,
                // Subject is the resolved target.
                Target,
                // A static member with no bound row yet.
                UnboundStatic,
            }

            private readonly Kind kind;
            private readonly object? subject;
            private readonly int slotIndex;

            private CollectionMutation(Kind kind, object? subject, int slotIndex = -1)
            {
                this.kind = kind;
                this.subject = subject;
                this.slotIndex = slotIndex;
            }

            internal static CollectionMutation DetachedList(NeoScriptObject owner, int slotIndex) =>
                new(Kind.DetachedList, owner, slotIndex);

            internal static CollectionMutation Local(object? local) => new(Kind.Local, local);

            internal static CollectionMutation Target(NeoResolvedCollectionTarget target) => new(Kind.Target, target);

            internal static CollectionMutation UnboundStatic() => new(Kind.UnboundStatic, null);

            internal void Apply(
                NeoClient client,
                CollectionCallInstruction instruction,
                NeoScriptScope scope,
                NSGetterEvaluator.Context ctx,
                object?[] args)
            {
                switch (kind)
                {
                    case Kind.DetachedList:
                        {
                            var owner = (NeoScriptObject)subject!;
                            if (owner.attachedId is null
                                && NeoGeneratedTypesSupport.TryAddDetachedListEntry(owner, slotIndex, args[0]))
                                return;
                            ResolveDetachedListTarget(client, owner, slotIndex, instruction.target.typeInfo, ctx)
                                .Mutate(client, instruction.mutation, args, ctx);
                            return;
                        }
                    case Kind.Local:
                        scope.Assign((VariablePointer)instruction.target.pointer, MutateLocalCollection(
                            subject, instruction.target.typeInfo, instruction.mutation, args, ctx));
                        return;
                    case Kind.Target:
                        ((NeoResolvedCollectionTarget)subject!).Mutate(client, instruction.mutation, args, ctx);
                        return;
                    default:
                        if (!TryMutateUnboundStatic(client, instruction,
                                (StaticMemberPointer)instruction.target.pointer, args, scope, ctx))
                            ResolveCollectionTarget(client, instruction.target, scope, ctx)
                                .Mutate(client, instruction.mutation, args, ctx);
                        return;
                }
            }
        }

        private static CollectionMutation ResolveCollectionMutation(
            NeoClient client,
            CollectionCallInstruction instruction,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            if (instruction.target.pointer is VariablePointer variablePointer)
            {
                if (!scope.TryGetValue(variablePointer.variableId, out var local))
                    throw new NSGetterRuntimeError($"Variable '{variablePointer.variableId}' is not in scope");
                if (local is object?[] aliased
                    && NeoGeneratedTypesSupport.TryGetDetachedArrayOrigin(aliased, out var origin))
                {
                    NeoScriptObject owner = origin!.owner;
                    if (owner.attachedId is null
                        && instruction.mutation == CollectionMutationKind.Add
                        && WritesSessionTarget(instruction.target.writability))
                        return CollectionMutation.DetachedList(owner, origin.index);
                    NSGetterEvaluator.ForwardDetached(owner, ctx);
                    local = Eval(variablePointer, scope, ctx);
                }
                if (local is not object?[] || FindValueId(local, ctx) is null)
                    return CollectionMutation.Local(local);
                return CollectionMutation.Target(ResolveCollectionTarget(client, new WriteTarget
                {
                    pointer = instruction.target.pointer,
                    typeInfo = instruction.target.typeInfo,
                    writability = null,
                }, scope, ctx));
            }
            if (instruction.target.pointer is KeyOfPointer keyOfTarget
                && instruction.target.typeInfo.type == MemberKind.List)
            {
                object? receiver = Eval(keyOfTarget.keyOf.pointer, scope, ctx);
                if (receiver is NeoScriptObject detached)
                {
                    if (detached.attachedId is null
                        && instruction.mutation == CollectionMutationKind.Add
                        && WritesSessionTarget(instruction.target.writability)
                        && Eval(keyOfTarget.keyOf.key, scope, ctx) is string key
                        && NSGetterEvaluator.TryFindDetachedSlot(detached.plan, key, keyOfTarget.keyOf, ctx, out int slotIndex))
                        return CollectionMutation.DetachedList(detached, slotIndex);
                    receiver = NSGetterEvaluator.ForwardDetached(detached, ctx);
                }
                return CollectionMutation.Target(ResolveCollectionTarget(client, instruction.target, scope, ctx,
                    evaluatedTarget: NSGetterEvaluator.EvaluateKeyOf(keyOfTarget, receiver, scope, ctx),
                    targetEvaluated: true, keyOfReceiver: receiver));
            }
            if (instruction.target.pointer is StaticMemberPointer staticMember
                && NeoGeneratedTypesSupport.StaticBinding(client, staticMember.memberId,
                    TargetOwnership(client, instruction.target, scope, ctx)).ValueId is null)
                return CollectionMutation.UnboundStatic();
            return CollectionMutation.Target(ResolveCollectionTarget(client, instruction.target, scope, ctx));
        }

        private static NeoResolvedCollectionTarget ResolveDetachedListTarget(
            NeoClient client,
            NeoScriptObject owner,
            int slotIndex,
            TypeInfo collectionType,
            NSGetterEvaluator.Context ctx)
        {
            NSGetterEvaluator.ForwardDetached(owner, ctx);
            string ownerId = owner.attachedId!;
            var slot = owner.plan.slots[slotIndex];
            string? listId = null;
            if (client.TryGetValue(ownerId, out ObjectMemberValue? row) && row.value is not null)
                row.value.TryGetValue(slot.schemaKey, out listId);
            if (listId is null)
                client.TryGetVirtualClassChildValueId(ownerId, slot.schemaKey, out listId);
            if (listId is null)
                throw new NSGetterRuntimeError($"Detached collection member '{slot.schemaKey}' was not materialized.");
            NeoValueOwnership ownership = client.TryGetValueOwnership(listId, out var resolved)
                ? resolved : NeoValueOwnership.Session;
            EnsureWritableRow(client, listId, ownership);
            client.TryGetValue(ownership, listId, out ArrayMemberValue? list);
            return new NeoListWriteTarget(listId, EntryTypeInfo(collectionType),
                client.TryResolveCollectionEntryMember(slot.member, list!), ownership);
        }

        // Its own method: the write's closure captures the call's state, and
        // C# allocates a closure where the captured variables are declared.
        private static bool TryMutateUnboundStatic(
            NeoClient client,
            CollectionCallInstruction instruction,
            StaticMemberPointer staticMember,
            object?[] args,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            var binding = NeoGeneratedTypesSupport.StaticBinding(client, staticMember.memberId,
                TargetOwnership(client, instruction.target, scope, ctx));
            if (binding.ValueId is not null)
                return false;
            PrepareWrite(client, plan =>
            {
                object initialValue = instruction.target.typeInfo.type == MemberKind.Dictionary
                    ? new Dictionary<string, string>() : Array.Empty<string>();
                binding.PreparePayload(plan, initialValue);
                ResolveCollectionTarget(client, instruction.target, scope, ctx, plan)
                    .Mutate(client, instruction.mutation, args, ctx);
            });
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object? Eval(
            Pointer pointer,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            return NSGetterEvaluator.EvaluatePointer(pointer, scope, ctx);
        }

        internal static object? EvaluateImmediateObjectInitializer(
            ObjectInitializerPointer pointer,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx) =>
            EvalObjectInitializer(pointer, scope, ctx, ExpressionResumeState.Immediate,
                NeoScriptExecutionOptions.ForImmediate(ctx.client));

        private static object? EvalObjectInitializer(
            ObjectInitializerPointer pointer,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            ExpressionResumeState expressionState,
            NeoScriptExecutionOptions? options)
        {
            string resumeKey = expressionState.NextInvocationKey("initializer:" + pointer.receiver.id);
            if (expressionState.TryGet(resumeKey, out object? cached, out Exception? error))
            {
                if (error is not null)
                    throw error;
                return cached;
            }
            try
            {
                // Execute as inline instructions, with no callable return validation.
                // A suspended child retains its receiver and completed assignments.
                var instructions = new Instruction[pointer.assignments.Length + 2];
                instructions[0] = new VariableInstruction { variable = pointer.receiver };
                Array.Copy(pointer.assignments, 0, instructions, 1, pointer.assignments.Length);
                instructions[instructions.Length - 1] = new ReturnInstruction
                {
                    pointer = new VariablePointer { variableId = pointer.receiver.id },
                };
                NeoScriptExecutionResult result = ExecuteInstructions(ctx.client, instructions,
                    pointer.receiver.typeInfo, scope.CreateChild(1), ctx, 0, null, options);
                if (result.IsPaused)
                    throw new NeoFunctionCallSuspended(resumeKey, result.SuspendedMemberId!, result);
                expressionState.StoreValue(resumeKey, result.ReturnValue);
                return result.ReturnValue;
            }
            catch (NeoFunctionCallSuspended) { throw; }
            catch (Exception exception)
            {
                expressionState.StoreError(resumeKey, exception);
                throw;
            }
        }

        /// <summary>
        /// Starts a deferred native Function and returns its inline result,
        /// or suspends the frame. Kept out of <see cref="EvalFunctionCall"/>
        /// so the completion closures are only allocated on this path.
        /// </summary>
        private static object? StartDeferredNativeFunction(
            NeoClient client,
            string memberId,
            object? receiver,
            object?[] args,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            string resumeKey)
        {
            var suspension = new DeferredNativeFunctionSuspension();
            var deferredHandle = client.StartDeferredNativeFunction(
                memberId,
                receiver,
                args,
                result => suspension.Complete(NSGetterEvaluator.NormalizeNativeResult(memberId, result, ctx)),
                suspension.Fail,
                suspension.MarkInvokerReturned,
                options?.CancelContinuationOnDeferredDisposal == true
                    ? suspension.Cancel
                    : suspension.Abandon);
            if (suspension.TryGetInlineResult(
                    out object? inlineValue,
                    out Exception? inlineError))
            {
                if (inlineError is not null)
                    throw inlineError;
                return inlineValue;
            }
            throw new NeoFunctionCallSuspended(
                resumeKey,
                memberId,
                NeoScriptExecutionResult.Paused(
                    memberId,
                    deferredHandle,
                    suspension,
                    inlineValue => NeoScriptExecutionResult.Completed(
                        returned: true,
                        inlineValue)));
        }

        private static object? EvalFunctionCall(
            NeoClient client,
            CallFunctionPointer pointer,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            ExpressionResumeState expressionState,
            NeoScriptExecutionOptions? options)
        {
            string callSiteKey = CallSiteKey(pointer);
            if (!expressionState.Recording)
                return CallFunction(client, pointer, scope, ctx, options, callSiteKey);
            string resumeKey = expressionState.NextInvocationKey(callSiteKey);
            bool hasCached = expressionState.TryGet(resumeKey, out object? cachedValue, out Exception? cachedError);
            if (hasCached)
            {
                if (cachedError is not null)
                    throw cachedError;
                return cachedValue;
            }
            try
            {
                object? value = CallFunction(client, pointer, scope, ctx, options, resumeKey);
                expressionState.StoreValue(resumeKey, value);
                return value;
            }
            catch (NeoFunctionCallSuspended)
            {
                throw;
            }
            catch (Exception exception)
            {
                expressionState.StoreError(resumeKey, exception);
                throw;
            }
        }

        private static string CallSiteKey(CallFunctionPointer pointer)
        {
            if (string.IsNullOrEmpty(pointer.callSiteId))
            {
                throw new NSGetterRuntimeError(
                    "Function call is missing its required callSiteId.");
            }
            return pointer.callSiteId;
        }

        /// <summary>
        /// One call through a call site. A frame that records results for a
        /// resume wraps it; an immediate frame, which has nothing to record,
        /// calls it directly.
        /// </summary>
        private static object? CallFunction(
            NeoClient client,
            CallFunctionPointer pointer,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            string resumeKey)
        {
            var receiver = NSGetterEvaluator.EvalCallReceiver(
                pointer.receiver,
                scope,
                ctx);
            if (pointer.optional == true && receiver is null)
            {
                if (!pointer.receiver.IsStatic)
                {
                    return null;
                }
            }
            // The target depends only on the receiver, so an NSFunction's
            // arguments can evaluate straight into its parameter slots.
            NSGetterEvaluator.CallSiteTarget? target = NSGetterEvaluator.ResolveCallTarget(
                pointer,
                receiver,
                ctx);
            if (target?.function is { } resolved)
            {
                bool deferred = resolved.Deferred;
                if (deferred && options?.AllowDeferredFunctionCalls != true)
                {
                    throw new NeoDeferredFunctionRuntimeError(
                        $"NSFunction '{resolved.Member.name}' ({target.memberId}) deferred-mode mismatch: " +
                        "an immediate NeoScript frame called its deferred signature; " +
                        "compiled call IR is stale/corrupt.");
                }
                NeoScriptExecutionResult nested = NeoNSFunctionRuntime.ExecuteResolved(
                    client,
                    resolved,
                    receiver,
                    Array.Empty<object?>(),
                    ctx,
                    options ?? NeoScriptExecutionOptions.ForImmediate(client),
                    site: pointer,
                    siteScope: scope);
                if (nested.IsPaused)
                {
                    if (!deferred)
                    {
                        nested.Deferred?.DisposeFromOwner(
                            "non-deferred NSFunction suspended");
                        throw new NSGetterRuntimeError(
                            $"Non-deferred NSFunction '{resolved.Member.name}' suspended; its compiled IR is stale or corrupt.");
                    }
                    throw new NeoFunctionCallSuspended(
                        resumeKey,
                        target.memberId,
                        nested);
                }
                return nested.ReturnValue;
            }
            object?[] args = NSGetterEvaluator.RentArguments(pointer);
            try
            {
                for (int i = 0; i < pointer.args.Length; i++)
                {
                    args[i] = NSGetterEvaluator.EvaluateFunctionArgument(pointer, i, scope, ctx);
                }
                NSGetterEvaluator.MaterializePatternArguments(target?.memberId, args, ctx);
                if (target is null)
                {
                    object? fallback = NSGetterEvaluator.EvaluateMissingMemberFallback(
                        pointer,
                        receiver,
                        args);
                    return fallback;
                }
                string memberId = target.memberId;
                NeoClient.ResolvedNativeFunction? nativeFunction = target.nativeFunction;
                FunctionMember? native = nativeFunction?.signature;
                if (native?.Dispatch != NeoFunctionDispatchKind.Asynchronous)
                {
                    // P65 §2.5 — filled BEFORE dispatch so the native
                    // exact-arity check stands. Deferred functions reject
                    // defaulted parameters (§1.4), so the branch below
                    // stays unfilled.
                    return NSGetterEvaluator.InvokeNativeFunction(
                        memberId, nativeFunction, target.native?.returnTypeInfo, receiver,
                        NSGetterEvaluator.FillNativeCallSiteArguments(memberId, native, args), ctx,
                        ownsArguments: true);
                }
                else
                {
                    if (options?.AllowDeferredFunctionCalls != true)
                    {
                        string functionName = client.TryGetMember(
                            memberId, out JsonMember? deferredMember)
                                ? deferredMember.name
                                : memberId;
                        throw new NeoDeferredFunctionRuntimeError(
                            $"Function '{functionName}' ({memberId}) deferred-mode mismatch: " +
                            "an immediate NeoScript frame called its deferred signature; " +
                            "compiled call IR is stale/corrupt.");
                    }
                    return StartDeferredNativeFunction(
                        client, memberId, receiver, args.AsSpan().ToArray(), ctx, options, resumeKey);
                }
            }
            finally
            {
                NSGetterEvaluator.ReturnArguments(pointer, args);
            }
        }

        private static bool EvaluateBoolean(
            BooleanExpression expression,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            return NSGetterEvaluator.EvalBooleanExpression(expression, scope, ctx);
        }

        private static NeoScriptExecutionResult ExecuteSetterAssignment(
            NeoClient client,
            AssignInstruction instruction,
            object? rhs,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options)
        {
            if (instruction.target.pointer is not CallGetterPointer callGetter)
            {
                throw new NSGetterRuntimeError(
                    "Setter write target must be a callGetter pointer.");
            }

            bool isStatic = callGetter.receiver.IsStatic;
            object? receiver = NSGetterEvaluator.EvalCallReceiver(
                callGetter.receiver,
                scope,
                ctx);
            if (!isStatic && receiver is null)
            {
                throw new NSGetterRuntimeError("Cannot invoke setter on a null receiver.");
            }

            string effectiveMemberId = isStatic
                ? callGetter.memberId
                : ResolveSetterMemberId(
                    client,
                    callGetter.memberId,
                    receiver!,
                    ctx);
            if (isStatic
                && (!client.TryGetMember(
                        effectiveMemberId,
                        out JsonMember? staticMember)
                    || staticMember.Modifier != NeoMemberModifierKind.Static
                    || callGetter.receiver.memberId != effectiveMemberId))
            {
                throw new NSGetterRuntimeError(
                    $"Static setter target '{effectiveMemberId}' is missing, not static, or does not match its receiver.");
            }
            if (NSGetterEvaluator.ContainsFrame(ctx.setterCallStack, effectiveMemberId))
            {
                string circularName = client.TryGetMember(
                    effectiveMemberId, out JsonMember? circularMember)
                        ? circularMember.name
                        : effectiveMemberId;
                throw new NSGetterRuntimeError(
                    $"Circular setter call: '{circularName}'.");
            }

            FunctionWithReturnType? setter = ResolveCompiledSetter(
                effectiveMemberId,
                client);
            if (setter is null)
            {
                string missingName = client.TryGetMember(
                    effectiveMemberId, out JsonMember? missingMember)
                        ? missingMember.name
                        : effectiveMemberId;
                throw new NSGetterRuntimeError(
                    $"NeoScript property '{missingName}' has no compiled setter — save its code to compile it.");
            }

            // The compiler lowers compound/increment assignment into the
            // fully operator-applied expression stored in `pointer` (which
            // includes the callGetter read). `operatorValue` is descriptive
            // metadata here, exactly as it is for storage-backed writes; do
            // not apply the operator a second time.
            object? value = CoerceSetterValue(rhs, instruction.target.typeInfo);

            var nestedCtx = ctx
                .WithSetterPushed(effectiveMemberId, isStatic ? null : receiver);
            var nestedOptions = options is null || ctx.constructorBody
                ? NeoScriptExecutionOptions.ForUnityProperty(client, effectiveMemberId)
                : options.ForProperty(effectiveMemberId);
            return ExecuteSetter(client, setter, value, nestedCtx, nestedOptions);
        }

        /// <summary>
        /// Runs a compiled setter in its body's pooled scope, binding the
        /// <c>[__this__, __root__, __value__]</c> parameters by slot. The
        /// receiver and root are <paramref name="ctx"/>'s.
        /// </summary>
        internal static NeoScriptExecutionResult ExecuteSetter(
            NeoClient client,
            FunctionWithReturnType setter,
            object? value,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions options)
        {
            NeoScriptScopeLayout layout = setter.scopeLayout ??= new NeoScriptScopeLayout(setter);
            NeoScriptScope scope = layout.RentScope();
            bool completed = false;
            try
            {
                if (layout.thisSlot >= 0)
                    scope.SetParameter(layout.thisSlot, ctx.thisValue);
                else
                    scope["__this__"] = ctx.thisValue;
                if (layout.rootSlot >= 0)
                    scope.SetParameter(layout.rootSlot, ctx.rootValue);
                else
                    scope["__root__"] = ctx.rootValue;
                if (layout.valueSlot >= 0)
                    scope.SetParameter(layout.valueSlot, value);
                else
                    scope[NeoScriptScopeLayout.ValueParameterId] = value;
                if (layout.contextSlot >= 0)
                    scope.SetParameter(layout.contextSlot, ctx.contextValue);
                else if (ctx.contextValue is not null)
                    scope["__context__"] = ctx.contextValue;
                NeoScriptExecutionResult result = Execute(
                    client,
                    setter,
                    scope,
                    ctx,
                    options,
                    (terminal, _) => ValidateStatementTerminal(
                        terminal,
                        "NeoScript property setter"));
                completed = !result.IsPaused;
                return result;
            }
            finally
            {
                // A suspended setter's continuation still holds the scope.
                if (completed)
                    layout.ReturnScope(scope);
                else
                    layout.AbandonScope(scope);
            }
        }

        private static object? CoerceSetterValue(object? value, TypeInfo typeInfo)
        {
            if (typeInfo.type == MemberKind.Decimal
                && value is double or float or int or long or short or decimal)
            {
                return NSGetterEvaluator.CoerceDecimalOperand(value, "setter value");
            }
            return value;
        }

        internal static string ResolveSetterMemberId(
            NeoClient client,
            string staticMemberId,
            object receiver,
            NSGetterEvaluator.Context ctx)
        {
            // The placement and the class's instance surface are the
            // client's cached schema resolution, as a getter dispatch reads.
            var placement = client.FindSchemaPlacement(staticMemberId);
            if (placement is null)
                return staticMemberId;

            string? runtimeClassId = NSGetterEvaluator.FindRowClassIdByReference(
                receiver,
                ctx);
            if (string.IsNullOrEmpty(runtimeClassId))
                return staticMemberId;

            try
            {
                return client.ResolveInstanceSurfaceMember(runtimeClassId!, placement.schemaKey)?.memberId
                    ?? staticMemberId;
            }
            catch (CircularInheritanceError)
            {
                return staticMemberId;
            }
        }

        internal static FunctionWithReturnType? ResolveCompiledSetter(
            string memberId,
            NeoClient client)
        {
            return client.TryGetMember(memberId, out NSPropertyMember? property)
                ? property.setter
                : null;
        }

        /// <summary>
        /// P43 §6.1 step 4 — writes one schema key of a freshly constructed
        /// class row through the exact target a <c>this.X = …</c> assignment
        /// resolves to. Sharing the target rather than re-implementing the
        /// write is what keeps a call-site initializer's replacement of a
        /// member the body already wrote behaving identically to the body
        /// writing it twice: the displaced child is unlinked, and an attached
        /// class value goes through the ordinary import funnel.
        /// </summary>
        internal static void WriteConstructedClassMember(
            NeoClient client,
            string parentRowId,
            string schemaKey,
            JsonMember member,
            NeoValueOwnership ownership,
            object? value,
            NSGetterEvaluator.Context ctx)
        {
            new NeoClassMemberWriteTarget(parentRowId, schemaKey, member, ownership)
                .Write(client, value, ctx);
        }

        private static NeoResolvedWriteTarget ResolveTarget(
            NeoClient client,
            WriteTarget target,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            switch (target.pointer)
            {
                case StaticMemberPointer staticMember:
                    return new NeoStaticMemberWriteTarget(
                        client.StaticBinding(staticMember.memberId),
                        target.typeInfo);
                case ReferencePointer reference:
                    {
                        NeoValueOwnership ownership = TargetOwnership(client, target, scope, ctx);
                        string rowId = EnsureWritableRow(client, reference.valueId, ownership);
                        return new NeoRowWriteTarget(rowId, target.typeInfo, ownership);
                    }
                case KeyOfPointer keyOfPointer:
                    return ResolveKeyOfWriteTarget(
                        client,
                        target,
                        keyOfPointer,
                        Eval(keyOfPointer.keyOf.pointer, scope, ctx),
                        scope,
                        ctx);
                default:
                    throw new NSGetterRuntimeError(
                        $"Unsupported assignment target '{target.pointer.GetType().Name}'.");
            }
        }

        /// <summary>
        /// Resolves a keyed write on a receiver the caller already evaluated.
        /// A detached receiver materializes here, since the write needs rows.
        /// </summary>
        /// <param name="rent">Whether a class member target comes from <see cref="NeoClassMemberWriteTarget.Rent"/>; the caller releases it.</param>
        private static NeoResolvedWriteTarget ResolveKeyOfWriteTarget(
            NeoClient client,
            WriteTarget target,
            KeyOfPointer keyOfPointer,
            object? receiver,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            bool rent = false)
        {
            if (receiver is NeoScriptObject detached)
                receiver = NSGetterEvaluator.ForwardDetached(detached, ctx);
            NeoValueOwnership ownership = TargetOwnership(client, target, scope, ctx, receiver);
            return ResolveKeyOfTarget(client, keyOfPointer.keyOf, target.typeInfo, ownership, receiver, scope, ctx, rent);
        }

        private static NeoResolvedCollectionTarget ResolveCollectionTarget(
            NeoClient client,
            WriteTarget target,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx, NeoWritePlan? preparedPlan = null,
            object? evaluatedTarget = null,
            bool targetEvaluated = false,
            object? keyOfReceiver = null)
        {
            NeoValueOwnership ownership = TargetOwnership(client, target, scope, ctx, keyOfReceiver);
            string? rowId;
            JsonMember? member = null;
            if (target.pointer is StaticMemberPointer staticMember)
            {
                client.TryGetMember(staticMember.memberId, out member);
                var binding = NeoGeneratedTypesSupport.StaticBinding(
                    client,
                    staticMember.memberId,
                    ownership);
                rowId = binding.ValueId;
            }
            else if (target.typeInfo is LookupTypeInfo && target.pointer is KeyOfPointer lookupKeyOf)
            {
                // A lookup READ resolves to the looked-up entries, so the
                // evaluated value reverse-maps to an ASSET row (the first
                // entry) — mutating that throws "not save-owned" even though
                // the author wrote a perfectly legal save mutation. Lookup
                // mutations target the save-side ref list: read the
                // receiver's raw member id instead of evaluating the lookup.
                object? receiver = Eval(lookupKeyOf.keyOf.pointer, scope, ctx);
                string? receiverRowId = FindValueId(receiver, ctx);
                object? key = Eval(lookupKeyOf.keyOf.key, scope, ctx);
                rowId = null;
                string lookupKey = ToStringKey(key, "Lookup member key");
                if (receiverRowId is not null
                    && client.TryGetValue(receiverRowId, out ObjectMemberValue? receiverRow)
                    && receiverRow!.value is not null
                    && receiverRow.value.TryGetValue(lookupKey, out string? memberRowId))
                {
                    rowId = memberRowId;
                }
                // P75: the save-side ref list's key is omitted on a
                // collapse-stamped receiver; the deterministic virtual id is
                // where the write materializes.
                else if (receiverRowId is not null
                    && client.TryGetVirtualClassChildValueId(
                        receiverRowId,
                        lookupKey,
                        out string? virtualLookupId))
                {
                    rowId = virtualLookupId;
                }
            }
            else
            {
                object? value = targetEvaluated ? evaluatedTarget : Eval(target.pointer, scope, ctx);
                member = NSGetterEvaluator.FindRowMemberByReference(value, ctx);
                rowId = FindValueId(value, ctx);
                if (rowId is null && target.pointer is KeyOfPointer collectionKeyOf)
                {
                    // P75: a collection read on a sparse receiver resolves
                    // through the virtual index and hands back a payload no
                    // reverse map has seen. Resolve the target the way the
                    // member-write path does — receiver row id plus schema
                    // key, stored body first, then the deterministic virtual
                    // id the write materializes under.
                    object? receiver = keyOfReceiver ?? Eval(collectionKeyOf.keyOf.pointer, scope, ctx);
                    string? receiverRowId = FindValueId(receiver, ctx);
                    object? key = Eval(collectionKeyOf.keyOf.key, scope, ctx);
                    string schemaKey = ToStringKey(key, "Collection member key");
                    if (receiverRowId is not null)
                    {
                        if (client.TryGetValue(
                                receiverRowId,
                                out ObjectMemberValue? receiverRow)
                            && receiverRow!.value is not null
                            && receiverRow.value.TryGetValue(
                                schemaKey,
                                out string? storedChildId))
                        {
                            rowId = storedChildId;
                        }
                        else if (client.TryGetVirtualClassChildValueId(
                                receiverRowId,
                                schemaKey,
                                out string? virtualChildId))
                        {
                            rowId = virtualChildId;
                        }
                    }
                }
            }
            if (rowId == null)
            {
                throw new NSGetterRuntimeError("Collection mutation target is not backed by a Neo value row.");
            }
            if (member is null && target.typeInfo.type == MemberKind.List)
                client.TryInferMemberForValueId(rowId, out member);
            if (target.typeInfo is not LookupTypeInfo
                && member is ListMember listMember && client.IsUnorderedList(listMember))
            {
                if (ownership == NeoValueOwnership.Asset
                    || !client.TryGetValueOwnership(rowId, out NeoValueOwnership currentOwnership)
                    || currentOwnership != ownership)
                    throw new NSGetterRuntimeError($"Cannot mutate value '{rowId}' because it is not {ownership.ToString().ToLowerInvariant()}-owned.");
                return new NeoUnorderedListWriteTarget(rowId, listMember, ownership, preparedPlan);
            }
            rowId = EnsureWritableRow(client, rowId, ownership);
            if (!client.TryGetValue(ownership, rowId, out MemberValue? row))
            {
                throw new NSGetterRuntimeError($"Missing collection row '{rowId}'.");
            }
            if (row is ArrayMemberValue)
            {
                if (target.typeInfo is LookupTypeInfo lookupTypeInfo)
                {
                    return new NeoLookupSetWriteTarget(rowId, lookupTypeInfo, ownership, preparedPlan);
                }
                return new NeoListWriteTarget(
                    rowId,
                    EntryTypeInfo(target.typeInfo),
                    client.TryResolveCollectionEntryMember(member, row),
                    ownership,
                    preparedPlan);
            }
            if (row is ObjectMemberValue)
            {
                return new NeoDictionaryWriteTarget(rowId, EntryTypeInfo(target.typeInfo), ownership, preparedPlan);
            }
            throw new NSGetterRuntimeError("Collection mutation target must be a list or dictionary.");
        }

        private static NeoResolvedWriteTarget ResolveKeyOfTarget(
            NeoClient client,
            KeyOf keyOf,
            TypeInfo targetType,
            NeoValueOwnership ownership,
            object? receiver,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            bool rent)
        {
            object? key = Eval(keyOf.key, scope, ctx);
            string? receiverRowId = FindValueId(receiver, ctx);
            if (receiverRowId == null)
            {
                throw new NSGetterRuntimeError("Assignment receiver is not backed by a Neo value row.");
            }
            NeoValueOwnership receiverOwnership = NSGetterEvaluator.FindRowOwnershipByReference(receiver, ctx, out var receiverRef)
                ?? (client.TryGetValueOwnership(receiverRowId, out var resolvedOwnership) ? resolvedOwnership : ownership);
            // The receiver's reference keeps its row's node, so the read
            // skips the id lookup.
            NeoValueNode? receiverNode = null;
            MemberValue? row;
            if (receiverRef is not null
                && receiverRef.valueId == receiverRowId
                && receiverRef.ownership == receiverOwnership)
            {
                row = client.ReadValue(receiverOwnership, receiverRowId, ref receiverRef.node);
                receiverNode = receiverRef.node;
            }
            else
            {
                row = client.ReadValue(receiverOwnership, receiverRowId, ref receiverNode);
            }
            if (row is null)
            {
                throw new NSGetterRuntimeError($"Missing receiver row '{receiverRowId}'.");
            }

            if (row is not ObjectMemberValue { classId: not null })
                EnsureWritableRow(client, receiverRowId, ownership);

            // P42 §1.2 / §3. A structured leaf is one value row, so a field
            // assignment is a read-modify-write of that row rather than a new
            // storage unit. This arm sits ahead of the collection arms because
            // a sprite receiver unwraps to an `IDictionary` and would
            // otherwise read as a plain dictionary entry write.
            //
            // Everything that governs a whole-value assignment has already
            // happened above and is untouched: `TargetOwnership` rejected an
            // Immutable/read-only target, and `EnsureWritableRow` rejected a
            // row that is not owned by the target store. A field write on an
            // Immutable-resolved member therefore fails identically to a
            // whole-value write on it.
            if (NeoStructuredLeafFieldWriteTarget.IsStructuredLeafRow(row))
            {
                return new NeoStructuredLeafFieldWriteTarget(
                    receiverRowId,
                    ToStringKey(key, "Structured leaf field name"),
                    targetType,
                    ownership);
            }
            if (row is ArrayMemberValue)
            {
                if (key is string)
                {
                    throw new NSGetterRuntimeError(
                        "Assignment through a List value-id index is read-only; mutate the returned entry or use a positional index.");
                }
                return new NeoListIndexWriteTarget(receiverRowId, ToInt(key, "List assignment index"), targetType, ownership);
            }
            if (row is ObjectMemberValue objectRow)
            {
                string keyString = ToStringKey(key, "Dictionary/class assignment key");
                if (!string.IsNullOrEmpty(objectRow.classId)
                    && NSGetterEvaluator.TryResolveSurfaceMember(
                        keyOf, receiver, objectRow.classId!, keyString, ctx, out JsonMember? memberMember, out MergedSchemaEntry? entry))
                {
                    if (memberMember!.Mutability == NeoMemberMutabilityKind.ReadOnly)
                    {
                        throw new NSGetterRuntimeError(
                            $"Member '{memberMember.name}' is readonly and can only be changed through its class default.");
                    }
                    if (memberMember is GenericMember)
                    {
                        memberMember = NeoGenericResolution.SubstituteMember(
                            client, memberMember,
                            NeoNSFunctionRuntime.ResolveReceiverGenericEnv(
                                client, receiver!, ctx, $"Member '{memberMember.name}'"));
                    }
                    NeoValueOwnership fieldOwnership = client.ChildOwnership(memberMember, receiverOwnership);
                    if (fieldOwnership != ownership || fieldOwnership == NeoValueOwnership.Asset)
                        throw new NSGetterRuntimeError($"Member '{memberMember!.name}' is not {ownership}-owned.");
                    NeoValueNode? childNode = NSGetterEvaluator.RememberedChildNode(receiver, entry!);
                    if (rent)
                    {
                        return NeoClassMemberWriteTarget.Rent(
                            receiverRowId,
                            keyString,
                            memberMember!,
                            ownership,
                            receiverOwnership,
                            receiverNode,
                            childNode);
                    }
                    return new NeoClassMemberWriteTarget(
                        receiverRowId,
                        keyString,
                        memberMember!,
                        ownership,
                        receiverOwnership,
                        receiverNode,
                        childNode);
                }
                EnsureWritableRow(client, receiverRowId, ownership);
                return new NeoDictionaryEntryWriteTarget(receiverRowId, keyString, targetType, ownership);
            }
            throw new NSGetterRuntimeError("Assignment receiver must be a list, dictionary, or class object.");
        }

        /// <param name="keyOfReceiver">A keyed target's already-evaluated receiver, when the caller has it.</param>
        private static NeoValueOwnership TargetOwnership(
            NeoClient client,
            WriteTarget target,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            object? keyOfReceiver = null)
        {
            if (target.writability is null)
            {
                if (TryInferTargetOwnership(client, target.pointer, scope, ctx, keyOfReceiver, out NeoValueOwnership inferred))
                {
                    return inferred;
                }
                throw new NSGetterRuntimeError("Cannot mutate read-only dialogue action target.");
            }
            return target.writability switch
            {
                WritabilityKind.Save => NeoValueOwnership.Save,
                WritabilityKind.ImmutableToSaveLookup => NeoValueOwnership.Save,
                WritabilityKind.Session => NeoValueOwnership.Session,
                WritabilityKind.ImmutableToSessionLookup => NeoValueOwnership.Session,
                WritabilityKind.Local => NeoValueOwnership.Session,
                WritabilityKind.Runtime => ResolveRuntimeTargetOwnership(
                    client, target.pointer, scope, ctx, keyOfReceiver),
                _ => throw new NSGetterRuntimeError("Cannot mutate read-only dialogue action target."),
            };
        }

        private static NeoValueOwnership ResolveRuntimeTargetOwnership(
            NeoClient client,
            Pointer pointer,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            object? keyOfReceiver)
        {
            if (!TryResolveTargetOwnership(client, pointer, scope, ctx, keyOfReceiver, out NeoValueOwnership ownership))
            {
                throw new NSGetterRuntimeError(
                    "Cannot write runtime-owned target because its value ownership could not be resolved.");
            }
            if (ownership == NeoValueOwnership.Asset)
            {
                throw new NSGetterRuntimeError(
                    "Cannot write runtime-owned target because its value is Asset-owned.");
            }
            return ownership;
        }

        private static bool TryInferTargetOwnership(
            NeoClient client,
            Pointer pointer,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            object? keyOfReceiver,
            out NeoValueOwnership ownership)
        {
            return TryResolveTargetOwnership(client, pointer, scope, ctx, keyOfReceiver, out ownership)
                && ownership != NeoValueOwnership.Asset;
        }

        private static bool TryResolveTargetOwnership(
            NeoClient client,
            Pointer pointer,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            object? keyOfReceiver,
            out NeoValueOwnership ownership)
        {
            ownership = NeoValueOwnership.Asset;
            if (pointer is StaticMemberPointer staticMember)
            {
                ownership = client.ResolveStaticOwnership(staticMember.memberId);
                return true;
            }
            object? resolvedTarget = pointer switch
            {
                ReferencePointer => null,
                KeyOfPointer keyOfPointer =>
                    keyOfReceiver ?? Eval(keyOfPointer.keyOf.pointer, scope, ctx),
                _ => Eval(pointer, scope, ctx),
            };
            NeoValueOwnership? contextualOwnership =
                NSGetterEvaluator.FindRowOwnershipByReference(
                    resolvedTarget,
                    ctx);
            if (contextualOwnership is not null)
            {
                ownership = contextualOwnership.Value;
                if (pointer is not KeyOfPointer)
                    return true;
            }
            string? rowId = pointer is ReferencePointer reference
                ? reference.valueId
                : FindValueId(resolvedTarget, ctx);
            if (contextualOwnership is null
                && (rowId is null || !client.TryGetValueOwnership(rowId, out ownership)))
            {
                return false;
            }
            // P93 §4. A class member write routes by the receiver row's
            // runtime-class member, so a Writable member of an asset row
            // resolves Session rather than the receiver's Asset.
            if (pointer is KeyOfPointer keyed
                && rowId is not null
                && TryResolveKeyedClassMember(client, keyed.keyOf, rowId, ownership, scope, ctx, out JsonMember? classMember))
            {
                ownership = client.ChildOwnership(classMember, ownership);
            }
            return true;
        }

        private static bool TryResolveKeyedClassMember(
            NeoClient client,
            KeyOf keyOf,
            string receiverRowId,
            NeoValueOwnership receiverOwnership,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            out JsonMember? member)
        {
            member = null;
            // A class member key compiles to a string literal, so the key
            // eval is a cached primitive read; it runs only for class rows.
            return client.TryGetValue(receiverOwnership, receiverRowId, out MemberValue? row)
                && row is ObjectMemberValue { classId: string classId } && classId.Length > 0
                && Eval(keyOf.key, scope, ctx) is string key
                && NSGetterEvaluator.TryResolveSurfaceMember(keyOf, null, classId, key, ctx, out member, out _)
                && member!.Mutability != NeoMemberMutabilityKind.ReadOnly;
        }

        private static string EnsureWritableRow(NeoClient client, string rowId, NeoValueOwnership ownership)
        {
            // Resolve targets without publishing a shadow. The write plan
            // clones the row only when preparing the mutation.
            if (ownership == NeoValueOwnership.Asset
                || !client.TryGetValueOwnership(rowId, out NeoValueOwnership currentOwnership)
                || currentOwnership != ownership)
            {
                throw new NSGetterRuntimeError(
                    $"Cannot mutate value '{rowId}' because it is not {ownership.ToString().ToLowerInvariant()}-owned.");
            }
            return rowId;
        }

        private static string? FindValueId(
            object? value,
            NSGetterEvaluator.Context ctx)
        {
            if (value is INeoValueReference reference
                && !string.IsNullOrEmpty(reference.valueId))
            {
                return reference.valueId;
            }
            return NSGetterEvaluator.FindRowIdByReference(value, ctx);
        }

        private static bool TryGetClassValueReferenceId(
            object? value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx,
            out string? valueId)
        {
            valueId = null;
            if (typeInfo.type != MemberKind.Class)
                return false;
            if (value is INeoValueReference reference
                && !string.IsNullOrEmpty(reference.valueId))
            {
                valueId = reference.valueId;
                return true;
            }
            valueId = FindValueId(value, ctx);
            return !string.IsNullOrEmpty(valueId);
        }

        /// <summary>
        /// Adopts a referenced value into <paramref name="ownership"/> through
        /// the ordinary import funnel, retargeting cached rows when the import
        /// moved the source rather than copying it. Shared with the P49 §4.4
        /// constructor seam, which adopts Class values nested inside a
        /// call-site-supplied collection the same way an assignment adopts a
        /// Class member.
        /// </summary>
        internal static string ImportClassValueReference(
            NeoClient client,
            NeoValueOwnership ownership,
            string sourceValueId,
            NSGetterEvaluator.Context ctx,
            string? currentDestinationValueId = null)
        {
            try
            {
                bool hadSourceOwnership = client.TryGetValueOwnership(
                    sourceValueId,
                    out NeoValueOwnership sourceOwnership);
                string importedId = client.ImportValueReference(
                    ownership,
                    sourceValueId,
                    out bool sourceMoved,
                    currentDestinationValueId);
                if (sourceMoved && hadSourceOwnership)
                {
                    NSGetterEvaluator.RetargetCachedRowsAfterMove(
                        ctx,
                        sourceOwnership,
                        ownership);
                }
                return importedId;
            }
            catch (InvalidOperationException ex)
            {
                throw new NSGetterRuntimeError(ex.Message);
            }
        }

        private static TypeInfo MemberKindInfo(JsonMember member)
        {
            return member switch
            {
                NullMember => new PrimitiveTypeInfo { type = MemberKind.Null, required = member.Requirement == NeoMemberRequirementKind.Required },
                BoolMember => new PrimitiveTypeInfo { type = MemberKind.Bool, required = member.Requirement == NeoMemberRequirementKind.Required },
                IntMember => new PrimitiveTypeInfo { type = MemberKind.Int, required = member.Requirement == NeoMemberRequirementKind.Required },
                FloatMember => new PrimitiveTypeInfo { type = MemberKind.Float, required = member.Requirement == NeoMemberRequirementKind.Required },
                StringMember => new PrimitiveTypeInfo { type = MemberKind.String, required = member.Requirement == NeoMemberRequirementKind.Required },
                Vector2Member => new PrimitiveTypeInfo { type = MemberKind.Vector2, required = member.Requirement == NeoMemberRequirementKind.Required },
                Vector2IntMember => new PrimitiveTypeInfo { type = MemberKind.Vector2Int, required = member.Requirement == NeoMemberRequirementKind.Required },
                Vector3Member => new PrimitiveTypeInfo { type = MemberKind.Vector3, required = member.Requirement == NeoMemberRequirementKind.Required },
                Vector3IntMember => new PrimitiveTypeInfo { type = MemberKind.Vector3Int, required = member.Requirement == NeoMemberRequirementKind.Required },
                ColorMember => new PrimitiveTypeInfo { type = MemberKind.Color, required = member.Requirement == NeoMemberRequirementKind.Required },
                DecimalMember => new PrimitiveTypeInfo { type = MemberKind.Decimal, required = member.Requirement == NeoMemberRequirementKind.Required },
                ClassMember classMember => new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = member.Requirement == NeoMemberRequirementKind.Required,
                    classId = classMember.classId,
                },
                EnumMember enumMember => new EnumTypeInfo
                {
                    type = MemberKind.Enum,
                    required = member.Requirement == NeoMemberRequirementKind.Required,
                    enumId = enumMember.enumId,
                },
                _ => new PrimitiveTypeInfo { type = member.kind, required = member.Requirement == NeoMemberRequirementKind.Required },
            };
        }

        private static JsonMember MemberFromTypeInfo(TypeInfo typeInfo)
        {
            var id = "__neo_dialogue_action_value";
            switch (typeInfo.type)
            {
                case MemberKind.Null:
                    return new NullMember { id = id, kind = MemberKind.Null };
                case MemberKind.Bool:
                    return new BoolMember { id = id, kind = MemberKind.Bool };
                case MemberKind.Int:
                    return new IntMember { id = id, kind = MemberKind.Int };
                case MemberKind.Float:
                    return new FloatMember { id = id, kind = MemberKind.Float };
                case MemberKind.String:
                    return new StringMember { id = id, kind = MemberKind.String };
                case MemberKind.Vector2:
                    return new Vector2Member { id = id, kind = MemberKind.Vector2 };
                case MemberKind.Vector2Int:
                    return new Vector2IntMember { id = id, kind = MemberKind.Vector2Int };
                case MemberKind.Vector3:
                    return new Vector3Member { id = id, kind = MemberKind.Vector3 };
                case MemberKind.Vector3Int:
                    return new Vector3IntMember { id = id, kind = MemberKind.Vector3Int };
                case MemberKind.Color:
                    return new ColorMember { id = id, kind = MemberKind.Color };
                case MemberKind.Decimal:
                    // A Decimal write flows through MemberValueFactory as
                    // a DecimalMember → StringMemberValue row
                    // (specs/decimal-member.md decision 5); the payload is
                    // the canonical decimal string the evaluator produced.
                    return new DecimalMember { id = id, kind = MemberKind.Decimal };
                case MemberKind.Class:
                    return new ClassMember
                    {
                        id = id,
                        kind = MemberKind.Class,
                        classId = ((ClassTypeInfo)typeInfo).classId,
                    };
                case MemberKind.List:
                    return new ListMember
                    {
                        id = id,
                        kind = MemberKind.List,
                        entryMemberId = id,
                    };
                case MemberKind.Dictionary:
                    var collectionTypeInfo = (CollectionTypeInfo)typeInfo;
                    return new DictionaryMember
                    {
                        id = id,
                        kind = MemberKind.Dictionary,
                        entryMemberId = id,
                        KeyKind = collectionTypeInfo.keyEnumId is null
                            ? NeoDictionaryKeyKind.String
                            : NeoDictionaryKeyKind.Enum,
                        keyEnumId = collectionTypeInfo.keyEnumId,
                    };
                case MemberKind.Enum:
                    return new EnumMember
                    {
                        id = id,
                        kind = MemberKind.Enum,
                        enumId = ((EnumTypeInfo)typeInfo).enumId,
                    };
                case MemberKind.Lookup:
                    return new LookupMember
                    {
                        id = id,
                        kind = MemberKind.Lookup,
                        collectionMemberId = id,
                    };
                case MemberKind.NSAction:
                    // A `+=`/`-=` subscription writes the whole listener set
                    // through this path (P62 §3.3); the signature is not part
                    // of the row, so the synthetic member carries none.
                    return new ActionMember
                    {
                        id = id,
                        kind = MemberKind.NSAction,
                        argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                    };
                default:
                    throw new NSGetterRuntimeError(
                        $"Unsupported write target type '{typeInfo.type}'.");
            }
        }

        private static TypeInfo EntryTypeInfo(TypeInfo typeInfo)
        {
            if (typeInfo is LookupTypeInfo lookupTypeInfo)
            {
                return lookupTypeInfo.entryTypeInfo;
            }
            if (typeInfo is CollectionTypeInfo collectionTypeInfo)
            {
                return collectionTypeInfo.entryTypeInfo;
            }
            throw new NSGetterRuntimeError("Collection target is missing entry type info.");
        }

        private static MemberValue CreateValueRow(
            NeoWritePlan plan,
            NeoClient client,
            NeoValueOwnership ownership,
            JsonMember member,
            object? value,
            string id,
            NeoTimestamp createdAt,
            NeoTimestamp updatedAt)
        {
            if (member is LookupMember lookup && value is not null)
                value = NeoGeneratedTypesSupport.ConstructorLookupIds(value, lookup);
            var payload = value is INeoValuePayloadProvider provider
                ? provider.ToNeoValuePayload()
                : value;
            client.StageWritablePayloadRows(plan, ownership, payload);
            return MemberValueFactory.Create(
                member,
                payload,
                id,
                createdAt,
                updatedAt);
        }

        /// <summary>
        /// An entry's value as a read returns it. A localized String entry
        /// stores its text id, so matching a script's value compares its text.
        /// </summary>
        private static object? ReadEntryValue(
            MemberValue entry,
            JsonMember? entryMember,
            NSGetterEvaluator.Context ctx) =>
            entry is StringMemberValue text && entryMember is StringMember stringMember
                ? NSGetterEvaluator.ResolveStringValue(text, stringMember, ctx)
                : ReadRowValue(entry);

        private static object? ReadRowValue(MemberValue row)
        {
            return row switch
            {
                BoolMemberValue b => b.value,
                NumberMemberValue n => n.value,
                StringMemberValue s => s.value,
                ArrayMemberValue a => a.value,
                ObjectMemberValue o => o.value,
                ActionMemberValue a => a.value,
                Vector2MemberValue v => v.value,
                Vector3MemberValue v => v.value,
                ColorMemberValue c => c.value,
                NullMemberValue => null,
                _ => null,
            };
        }

        private static double ToDouble(object? value, string name)
        {
            switch (value)
            {
                case double d:
                    return d;
                case float f:
                    return f;
                case int i:
                    return i;
                case long l:
                    return l;
                default:
                    throw new NSGetterRuntimeError($"{name} must be numeric.");
            }
        }

        private static int ToInt(object? value, string name)
        {
            var numeric = ToDouble(value, name);
            if (!NeoNumbers.IsWhole(numeric))
            {
                throw new NSGetterRuntimeError($"{name} must be an integer.");
            }
            return (int)numeric;
        }

        private static string ToStringKey(object? value, string name)
        {
            if (value is string s)
                return s;
            throw new NSGetterRuntimeError($"{name} must be a string.");
        }

        private static string ResolveLookupSelectionId(
            NeoClient client,
            LookupTypeInfo lookupTypeInfo,
            object? value,
            NSGetterEvaluator.Context ctx)
        {
            if (lookupTypeInfo.collectionMemberId is null
                && (lookupTypeInfo.entryTypeInfo.type == MemberKind.Enum
                    || lookupTypeInfo.entryTypeInfo.type == MemberKind.DialogueLookup))
            {
                if (value is object?[] { Length: 1 } selection)
                    value = selection[0];
                string? selectionId = NeoScriptValueMarshaller.EnumOptionId(value);
                if (string.IsNullOrEmpty(selectionId))
                {
                    throw new NSGetterRuntimeError("Set mutation requires one enum option or dialogue id.");
                }
                return selectionId!;
            }
            if (!client.TryGetMember(lookupTypeInfo.collectionMemberId, out JsonMember? collectionMember))
            {
                throw new NSGetterRuntimeError(
                    $"Lookup collection member '{lookupTypeInfo.collectionMemberId}' was not found.");
            }
            string? collectionValueId = client.TryResolveLookupCollectionValueId(
                collectionMember.id,
                lookupTypeInfo.collectionValueId,
                out string? resolvedCollectionValueId)
                    ? resolvedCollectionValueId
                    : null;
            if (collectionValueId is null || !client.TryGetValue(collectionValueId, out MemberValue? collectionValue))
            {
                throw new NSGetterRuntimeError(
                    $"Lookup collection value '{collectionValueId ?? "<null>"}' was not found.");
            }

            if (lookupTypeInfo.entryTypeInfo.type == MemberKind.Class)
            {
                string? valueId = value is string id
                    ? id
                    : FindValueId(value, ctx);
                if (string.IsNullOrWhiteSpace(valueId))
                {
                    throw new NSGetterRuntimeError(
                        "Lookup set class argument must be a selected value id or generated class value.");
                }
                if (!NeoMemberLookup.ResolveCollectionEntryIds(client, collectionMember, collectionValue).Contains(valueId!))
                {
                    throw new NSGetterRuntimeError(
                        $"Lookup selection id '{valueId}' is not present in the configured lookup collection.");
                }
                return valueId!;
            }

            string? matchedValueId = FindLookupCollectionValueByPayload(client, collectionMember, collectionValue, value, ctx);
            if (matchedValueId is null)
            {
                throw new NSGetterRuntimeError(
                    "Lookup set argument was not found in the configured lookup collection.");
            }
            return matchedValueId;
        }

        private static string? FindLookupCollectionValueByPayload(
            NeoClient client,
            JsonMember collectionMember,
            MemberValue collectionValue,
            object? value,
            NSGetterEvaluator.Context ctx)
        {
            var childIds = NeoMemberLookup.ResolveCollectionEntryIds(client, collectionMember, collectionValue);
            JsonMember? entryMember = client.TryResolveCollectionEntryMember(collectionMember, collectionValue);
            foreach (var childId in childIds)
            {
                if (!client.TryGetValue(childId, out MemberValue? child))
                    continue;
                if (JsEqual(ReadEntryValue(child, entryMember, ctx), value))
                    return childId;
            }
            return null;
        }

        private static bool JsEqual(object? a, object? b)
        {
            if (a == null || b == null)
                return a == null && b == null;
            if (a is double da && b is double db)
                return da == db;
            if (a is double da2 && b is int ib)
                return da2 == ib;
            if (a is int ia && b is double db2)
                return ia == db2;
            return Equals(a, b);
        }

        private static object? MutateLocalCollection(
            object? local,
            TypeInfo collectionType,
            string mutation,
            object?[] args,
            NSGetterEvaluator.Context ctx)
        {
            if (collectionType is LookupTypeInfo lookup && args.Length > 0
                && (lookup.entryTypeInfo.type == MemberKind.Enum
                    || lookup.entryTypeInfo.type == MemberKind.DialogueLookup))
            {
                args[0] = ResolveLookupSelectionId(ctx.client, lookup, args[0], ctx);
            }
            JsonMember? entryMember = NSGetterEvaluator.CollectionEntryMember(local, ctx);
            if (local is object?[] array)
            {
                // An Add builds its result directly; the List round trip
                // below costs two more copies and allocations per entry.
                if ((mutation == CollectionMutationKind.Add || mutation == CollectionMutationKind.Insert)
                    && collectionType.type != MemberKind.Lookup)
                {
                    int index = mutation == CollectionMutationKind.Insert
                        ? ToInt(args[0], "Insert index") : array.Length;
                    if (index < 0 || index > array.Length)
                        throw new NSGetterRuntimeError("List.Insert index is out of range.");
                    var added = new object?[array.Length + 1];
                    Array.Copy(array, 0, added, 0, index);
                    Array.Copy(array, index, added, index + 1, array.Length - index);
                    added[index] = args[mutation == CollectionMutationKind.Insert ? 1 : 0];
                    NSGetterEvaluator.KeepEntryMember(added, entryMember);
                    return added;
                }
                var arrayList = new List<object?>(array);
                MutateLocalList(arrayList, collectionType.type, mutation, args, entryMember, ctx);
                object?[] mutated = arrayList.ToArray();
                NSGetterEvaluator.KeepEntryMember(mutated, entryMember);
                return mutated;
            }
            if (local is List<object?> list)
            {
                MutateLocalList(list, collectionType.type, mutation, args, entryMember, ctx);
                return list;
            }
            if (local is IDictionary<string, object?> dict)
            {
                MutateLocalDictionary(dict, mutation, args, ctx);
                return dict;
            }
            throw new NSGetterRuntimeError("Collection mutation target must be a list or dictionary.");
        }

        private static void MutateLocalList(
            List<object?> list,
            MemberKind collectionKind,
            string mutation,
            object?[] args,
            JsonMember? entryMember,
            NSGetterEvaluator.Context ctx)
        {
            switch (mutation)
            {
                case CollectionMutationKind.Insert:
                    int index = ToInt(args[0], "Insert index");
                    if (index < 0 || index > list.Count)
                        throw new NSGetterRuntimeError("List.Insert index is out of range.");
                    list.Insert(index, args[1]);
                    return;
                case CollectionMutationKind.Add:
                    if (collectionKind == MemberKind.Lookup)
                    {
                        foreach (object? entry in list)
                        {
                            if (JsEqual(entry, args[0]))
                                return;
                        }
                    }
                    list.Add(args[0]);
                    return;
                case CollectionMutationKind.Remove:
                    for (int i = 0; i < list.Count; i++)
                    {
                        object? entry = entryMember is null
                            ? list[i]
                            : NSGetterEvaluator.ResolveValueIfId(list[i], ctx, member: entryMember);
                        if (!JsEqual(entry, args[0]))
                            continue;
                        list.RemoveAt(i);
                        break;
                    }
                    return;
                case CollectionMutationKind.RemoveAt:
                    list.RemoveAt(ToInt(args[0], "RemoveAt index"));
                    return;
                case CollectionMutationKind.Clear:
                    list.Clear();
                    return;
                default:
                    throw new NSGetterRuntimeError($"Unsupported collection mutation '{mutation}'.");
            }
        }

        private static void MutateLocalDictionary(
            IDictionary<string, object?> dict,
            string mutation,
            object?[] args,
            NSGetterEvaluator.Context ctx)
        {
            switch (mutation)
            {
                case CollectionMutationKind.Add:
                    dict[ToStringKey(args[0], "Dictionary Add key")] = args[1];
                    return;
                case CollectionMutationKind.Remove:
                    dict.Remove(ToStringKey(args[0], "Dictionary Remove key"));
                    return;
                case CollectionMutationKind.Clear:
                    dict.Clear();
                    return;
                default:
                    throw new NSGetterRuntimeError($"Unsupported dictionary mutation '{mutation}'.");
            }
        }

        private abstract class NeoResolvedWriteTarget
        {
            public abstract object? ReadCurrentValue(
                NeoClient client,
                NSGetterEvaluator.Context ctx);

            public abstract void Write(
                NeoClient client,
                object? value,
                NSGetterEvaluator.Context ctx);
        }

        private static void PrepareWrite(NeoClient client, Action<NeoWritePlan> prepare, NeoWritePlan? preparedPlan = null)
        {
            var plan = preparedPlan ?? new NeoWritePlan(client);
            using (client.ReadCandidate(plan))
                prepare(plan);
            if (preparedPlan is null && (plan.Rows.Count != 0 || plan.Bindings.Count != 0))
                plan.Commit();
        }

        private static string PrepareWritableRow(NeoWritePlan plan, NeoClient client,
            string rowId, NeoValueOwnership ownership)
        {
            EnsureWritableRow(client, rowId, ownership);
            if (!plan.TryGet(ownership, rowId, out MemberValue? row))
                throw new NSGetterRuntimeError($"Missing target row '{rowId}'.");
            plan.Set(ownership, client.CloneRowForWrite(row), silent: true);
            return rowId;
        }

        private static void StoreWritableRow(NeoWritePlan plan,
            NeoValueOwnership ownership, MemberValue row, NSGetterEvaluator.Context ctx)
        {
            plan.Set(ownership, row);
            plan.AfterCommit(() => NSGetterEvaluator.RefreshCachedRowAfterWrite(row, ctx, ownership));
        }

        // A member built from a TypeInfo names no schema entry member, so it
        // can't reach a nested List or Dictionary's entries. Releasing one
        // takes the schema member instead.
        private static JsonMember ReleaseMember(NeoClient client, string valueId, TypeInfo typeInfo) =>
            typeInfo.type is MemberKind.List or MemberKind.Dictionary
                && client.TryInferMemberForValueId(valueId, out JsonMember? member)
                ? member : MemberFromTypeInfo(typeInfo);

        private static JsonMember EntryReleaseMember(NeoClient client, MemberValue collectionRow, TypeInfo entryTypeInfo) =>
            entryTypeInfo.type is MemberKind.List or MemberKind.Dictionary
                && client.TryInferMemberForValueId(collectionRow.id, out JsonMember? collection)
                && client.TryResolveCollectionEntryMember(collection, collectionRow) is JsonMember entry
                ? entry : MemberFromTypeInfo(entryTypeInfo);

        // An assigned value replaces the slot's row at the same id, so the
        // previous value's owned rows are released.
        private static void StoreReplacedRow(NeoWritePlan plan, NeoClient client,
            NeoValueOwnership ownership, MemberValue next, JsonMember? member, NSGetterEvaluator.Context ctx)
        {
            client.StageInPlaceReplacement(plan, ownership, next, member);
            plan.AfterCommit(() => NSGetterEvaluator.RefreshCachedRowAfterWrite(next, ctx, ownership));
        }

        internal static string ImportClassValueReference(NeoWritePlan plan, NeoClient client,
            NeoValueOwnership ownership, string sourceValueId, NSGetterEvaluator.Context ctx,
            string? currentDestinationValueId = null)
        {
            try
            {
                bool hadSourceOwnership = plan.TryGetOwnership(sourceValueId, out NeoValueOwnership sourceOwnership);
                string importedId = client.ImportValueReference(plan, ownership, sourceValueId,
                    out bool moved, currentDestinationValueId);
                if (moved && hadSourceOwnership)
                    plan.AfterCommit(() => NSGetterEvaluator.RetargetCachedRowsAfterMove(ctx, sourceOwnership, ownership));
                return importedId;
            }
            catch (InvalidOperationException ex)
            {
                throw new NSGetterRuntimeError(ex.Message);
            }
        }

        private sealed class NeoRowWriteTarget : NeoResolvedWriteTarget
        {
            private readonly string rowId;
            private readonly TypeInfo typeInfo;
            private readonly NeoValueOwnership ownership;

            public NeoRowWriteTarget(string rowId, TypeInfo typeInfo, NeoValueOwnership ownership)
            {
                this.rowId = rowId;
                this.typeInfo = typeInfo;
                this.ownership = ownership;
            }

            public override object? ReadCurrentValue(
                NeoClient client,
                NSGetterEvaluator.Context ctx)
            {
                string writableRowId = EnsureWritableRow(client, rowId, ownership);
                if (!client.TryGetValue(ownership, writableRowId, out MemberValue? row))
                {
                    throw new NSGetterRuntimeError($"Missing target row '{writableRowId}'.");
                }
                return ReadRowValue(row);
            }

            public override void Write(
                NeoClient client,
                object? value,
                NSGetterEvaluator.Context ctx)
            {
                PrepareWrite(client, plan =>
                {
                    string writableRowId = PrepareWritableRow(plan, client, rowId, ownership);
                    if (!client.TryGetValue(ownership, writableRowId, out MemberValue? existing))
                    {
                        throw new NSGetterRuntimeError($"Missing target row '{writableRowId}'.");
                    }
                    var next = CreateValueRow(
                        plan, client,
                        ownership,
                        MemberFromTypeInfo(typeInfo),
                        value,
                        writableRowId,
                        existing.createdAt,
                        NeoTimestamp.Now());
                    next.classId = existing.classId;
                    StoreReplacedRow(plan, client, ownership, next, ReleaseMember(client, writableRowId, typeInfo), ctx);
                });
            }
        }

        private sealed class NeoStaticMemberWriteTarget : NeoResolvedWriteTarget
        {
            private readonly NeoStaticBinding binding;
            private readonly TypeInfo typeInfo;

            public NeoStaticMemberWriteTarget(
                NeoStaticBinding binding,
                TypeInfo typeInfo)
            {
                this.binding = binding;
                this.typeInfo = typeInfo;
            }

            public override object? ReadCurrentValue(
                NeoClient client,
                NSGetterEvaluator.Context ctx)
            {
                string? valueId = binding.ValueId;
                if (valueId is null)
                    return null;
                if (!client.TryGetOverlaidValue(
                        binding.Ownership,
                        valueId,
                        out MemberValue? row))
                {
                    throw new NSGetterRuntimeError(
                        $"Static member '{binding.MemberId}' is bound to missing value '{valueId}'.");
                }
                return ReadRowValue(row);
            }

            public override void Write(
                NeoClient client,
                object? value,
                NSGetterEvaluator.Context ctx)
            {
                try
                {
                    if (typeInfo.type == MemberKind.Class)
                    {
                        string? valueId = FindValueId(value, ctx);
                        NeoValueOwnership sourceOwnership = default;
                        bool hadSourceOwnership = valueId is not null
                            && client.TryGetValueOwnership(
                                valueId,
                                out sourceOwnership);
                        NeoValueWritePayload? payload = valueId is null
                            ? NeoValueWritePayload.FromValue(null)
                            : NeoValueWritePayload.FromValueReference(
                                valueId,
                                value as INeoValueReference);
                        binding.SetValue(payload);
                        if (hadSourceOwnership
                            && sourceOwnership == NeoValueOwnership.Session
                            && binding.Ownership == NeoValueOwnership.Save
                            && !client.HasWritableValue(sourceOwnership, valueId!)
                            && client.HasWritableValue(binding.Ownership, valueId!))
                        {
                            NSGetterEvaluator.RetargetCachedRowsAfterMove(
                                ctx,
                                sourceOwnership,
                                binding.Ownership);
                        }
                    }
                    else
                    {
                        object? payload = value is INeoValuePayloadProvider provider
                            ? provider.ToNeoValuePayload()
                            : value;
                        binding.SetValue(NeoValueWritePayload.FromValue(payload));
                    }
                    if (binding.ValueId is string updatedId
                        && client.TryGetOverlaidValue(
                            binding.Ownership,
                            updatedId,
                            out MemberValue? updatedRow))
                    {
                        NSGetterEvaluator.RefreshCachedRowAfterWrite(
                            updatedRow,
                            ctx,
                            binding.Ownership);
                    }
                }
                catch (InvalidOperationException error)
                {
                    throw new NSGetterRuntimeError(error.Message);
                }
            }
        }

        private sealed class NeoClassMemberWriteTarget : NeoResolvedWriteTarget
        {
            // An assignment writes through its target before it continues,
            // so one target serves every assignment but one that re-enters
            // NeoScript and assigns again: that finds it in use and
            // allocates. Unlocked, like the IR's other caches: evaluation is
            // single-threaded.
            private static NeoClassMemberWriteTarget? pooled;
            private bool inUse;
            private string parentRowId;
            private string key;
            private JsonMember member;
            private NeoValueOwnership ownership;
            private NeoValueOwnership parentOwnership;
            // The parent row's node, so the leaf write reads it without an id lookup.
            private NeoValueNode? parentNode;
            // The node of the child the last bound-child read found, handed
            // to the leaf store. It starts as the node the receiver's own
            // read of this member remembered.
            private NeoValueNode? childNode;

            public NeoClassMemberWriteTarget(
                string parentRowId,
                string key,
                JsonMember member,
                NeoValueOwnership ownership,
                NeoValueOwnership? parentOwnership = null,
                NeoValueNode? parentNode = null,
                NeoValueNode? childNode = null)
            {
                this.parentRowId = parentRowId;
                this.key = key;
                this.member = member;
                this.ownership = ownership;
                this.parentOwnership = parentOwnership ?? ownership;
                this.parentNode = parentNode;
                this.childNode = childNode;
            }

            /// <summary>The pooled target, bound to this write; <see cref="Release"/> returns it.</summary>
            internal static NeoClassMemberWriteTarget Rent(
                string parentRowId,
                string key,
                JsonMember member,
                NeoValueOwnership ownership,
                NeoValueOwnership parentOwnership,
                NeoValueNode? parentNode,
                NeoValueNode? childNode)
            {
                NeoClassMemberWriteTarget? target = pooled;
                if (target is null || target.inUse)
                {
                    target = new NeoClassMemberWriteTarget(
                        parentRowId, key, member, ownership, parentOwnership, parentNode, childNode);
                    pooled ??= target;
                }
                else
                {
                    // A repeat assignment binds the ids and member the target
                    // kept, and skipping those stores skips their write
                    // barriers. Release cleared the nodes.
                    if (!ReferenceEquals(target.parentRowId, parentRowId))
                        target.parentRowId = parentRowId;
                    if (!ReferenceEquals(target.key, key))
                        target.key = key;
                    if (!ReferenceEquals(target.member, member))
                        target.member = member;
                    target.ownership = ownership;
                    target.parentOwnership = parentOwnership;
                    if (parentNode is not null)
                        target.parentNode = parentNode;
                    if (childNode is not null)
                        target.childNode = childNode;
                }
                target.inUse = true;
                return target;
            }

            /// <summary>
            /// Ends a rented write. Clearing the nodes keeps a disposed
            /// client's rows unreachable from the pool, and constant null
            /// stores pay no write barrier. The ids and schema member it keeps
            /// reach nothing else.
            /// </summary>
            internal void Release()
            {
                parentNode = null;
                childNode = null;
                inUse = false;
            }

            /// <summary>
            /// P75: the row bound to <see cref="key"/> on the parent, resolved
            /// the way every other member path resolves it — the stored body
            /// first, then the deterministic virtual child id.
            ///
            /// <para>A collapse-stamped root stores only the members that
            /// differ from its construction, so an untouched member is absent
            /// from the body and lives only in the instance index. Consulting
            /// the body alone makes such a member read as null and write to a
            /// freshly minted random id — two different ids for the one logical
            /// member, where the web has one.</para>
            /// </summary>
            // A scalar assignment to a bound child of a parent the store
            // already holds replaces that child in place. Anything else
            // (first write under an authored root, class references,
            // collections, payload rows) prepares a plan below.
            private bool TryWriteLeaf(NeoClient client, object? value, NSGetterEvaluator.Context ctx)
            {
                if (member is not (BoolMember or IntMember or FloatMember or StringMember or EnumMember
                        or Vector2Member or Vector2IntMember or Vector3Member or Vector3IntMember or ColorMember)
                    || value is NeoValuePayload or INeoValuePayloadProvider
                    || !TryGetParent(client, out ObjectMemberValue? parent)
                    || !TryResolveBoundChild(client, parentRowId, parent, out string existingId, out MemberValue? existing, out MemberValue? stored)
                    || existing is null)
                    return false;
                MemberValue replaced = MemberValueFactory.Create(
                    member, value, existingId, existing.createdAt, NeoTimestamp.Now());
                replaced.classId = existing.classId;
                // An explicit override the store already holds with this value
                // is a no-op; an inherited default still pins on first write.
                if (stored is not null
                    && !stored.IsRemoved && NeoClient.SameLeafValue(stored, replaced))
                    return true;
                // An object's Position and a tile's Cell carry grid
                // invariants; the placement API keeps the grid indexes current
                // and falls through to the plain leaf write for every other
                // member.
                if (!client.TryWritePlacement(ownership, parent, key, replaced, member, childNode))
                    return false;
                // Scalar reads bypass the row cache, and the write that made
                // the row scalar refreshed whatever an earlier shape left, so
                // a scalar replacing a scalar has nothing to patch.
                if (!IsScalarRow(existing) || !IsScalarRow(replaced))
                    NSGetterEvaluator.RefreshCachedRowAfterWrite(replaced, ctx, ownership);
                return true;
            }

            private static bool IsScalarRow(MemberValue row) =>
                row is NumberMemberValue or BoolMemberValue or NullMemberValue;

            // A non-Asset parent must be one its own store holds.
            private bool TryGetParent(NeoClient client, [NotNullWhen(true)] out ObjectMemberValue? parent)
            {
                parent = (parentOwnership == NeoValueOwnership.Asset
                    ? client.ReadValue(parentOwnership, parentRowId, ref parentNode)
                    : client.ReadWritableValue(parentOwnership, parentRowId, ref parentNode)) as ObjectMemberValue;
                return parent is not null;
            }

            /// <param name="storedChild">The bound child's row in its own store, or null when it only inherits one.</param>
            private bool TryResolveBoundChild(
                NeoClient client,
                string resolvedParentRowId,
                ObjectMemberValue parent,
                out string boundChildId,
                out MemberValue? boundChild,
                out MemberValue? storedChild)
            {
                boundChildId = string.Empty;
                if (parent.value is not null
                    && parent.value.TryGetValue(key, out string bodyChildId)
                    && TryReadChild(client, bodyChildId, out boundChild, out storedChild))
                {
                    boundChildId = bodyChildId;
                    return true;
                }
                if (client.TryGetVirtualClassChildValueId(
                        resolvedParentRowId,
                        key,
                        out string? virtualChildId)
                    && TryReadChild(client, virtualChildId!, out boundChild, out storedChild))
                {
                    boundChildId = virtualChildId!;
                    return true;
                }
                boundChild = null;
                storedChild = null;
                return false;
            }

            // A read answers with the row the child's own store holds when
            // there is one, and both reads share one node lookup.
            private bool TryReadChild(
                NeoClient client,
                string childId,
                out MemberValue? child,
                out MemberValue? storedChild)
            {
                // A node is only ever handed on for its own id.
                if (childNode is not null && childNode.id != childId)
                    childNode = null;
                storedChild = client.ReadWritableValue(ownership, childId, ref childNode);
                child = storedChild ?? client.ReadValue(ownership, childId, ref childNode);
                return child is not null;
            }

            public override object? ReadCurrentValue(
                NeoClient client,
                NSGetterEvaluator.Context ctx)
            {
                if (!client.TryGetValue(parentOwnership, parentRowId, out ObjectMemberValue? parent)
                    || !TryResolveBoundChild(
                        client,
                        parentRowId,
                        parent,
                        out _,
                        out MemberValue? child,
                        out _)
                    || child is null)
                {
                    return null;
                }
                return ReadRowValue(child);
            }

            public override void Write(
                NeoClient client,
                object? value,
                NSGetterEvaluator.Context ctx)
            {
                if (TryWriteLeaf(client, value, ctx))
                    return;
                WriteThroughPlan(client, value, ctx);
            }

            // Separate from Write so a leaf write never pays for this
            // method's plan closure.
            private void WriteThroughPlan(
                NeoClient client,
                object? value,
                NSGetterEvaluator.Context ctx)
            {
                PrepareWrite(client, plan =>
                {
                    if (parentOwnership != NeoValueOwnership.Asset)
                    {
                        // The first write under an authored Save/Session root
                        // shadows the root at its stable id so the save file
                        // links the child it is about to hold. A parent the
                        // store already holds is left alone.
                        EnsureWritableRow(client, parentRowId, parentOwnership);
                        if (!client.HasWritableValue(parentOwnership, parentRowId))
                            PrepareWritableRow(plan, client, parentRowId, parentOwnership);
                    }
                    if (!client.TryGetValue(parentOwnership, parentRowId, out ObjectMemberValue? parent))
                    {
                        throw new NSGetterRuntimeError($"Missing parent row '{parentRowId}'.");
                    }
                    // Beyond that, the parent is rewritten only when its value
                    // map changes. A child replaced at its stable id leaves
                    // the parent untouched, exactly as the generated setters
                    // do; cloning and re-committing it on every scalar write
                    // cost a row clone, a second changed row and its
                    // notifications.
                    ObjectMemberValue? writableParent = null;
                    ObjectMemberValue WritableParent()
                    {
                        if (writableParent is not null)
                            return writableParent;
                        if (parentOwnership == NeoValueOwnership.Asset)
                            throw new NSGetterRuntimeError($"Cannot rebind '{key}' on an immutable parent.");
                        writableParent = (ObjectMemberValue)client.CloneRowForWrite(parent!);
                        writableParent.value ??= new Dictionary<string, string>();
                        return writableParent;
                    }
                    value = NeoGeneratedTypesSupport.MaterializeCollectionAssignment(client, member, value, ownership, ctx, plan);
                    NeoTimestamp now = NeoTimestamp.Now();
                    // Reusing the entry's stable id below clone-on-writes it
                    // (a fresh row at the same id shadows the authored default),
                    // so no path pre-materialization is needed. On a P75 sparse
                    // root that stable id is the deterministic virtual child id:
                    // writing there materializes the one member being changed and
                    // leaves the rest of the root omitted, which is exactly the
                    // "every value is its own instance, changing one materializes
                    // that one" contract the web already implements.
                    bool bound = TryResolveBoundChild(
                            client,
                            parentRowId,
                            parent!,
                            out string existingId,
                            out MemberValue? existing,
                            out _)
                        && existing is not null;
                    if (bound)
                    {
                        if (TryGetClassValueReferenceId(
                                value,
                                MemberKindInfo(member),
                                ctx,
                                out string? referenceId))
                        {
                            string importedId = ImportClassValueReference(
                                plan, client,
                                ownership,
                                referenceId!,
                                ctx,
                                existingId);
                            if (importedId == existingId)
                                return;
                            ObjectMemberValue rebound = WritableParent();
                            rebound.value![key] = importedId;
                            plan.AfterCommit(() => ctx.allocationTracker.RegisterConstructedParent(
                                importedId,
                                parentRowId));
                            rebound.updatedAt = now;
                            StoreWritableRow(plan, parentOwnership, rebound, ctx);
                            client.StageUnlinkedRemovals(plan, ownership, new[] { existingId }, member);
                            return;
                        }
                        if (member is ListMember listMember && client.IsUnorderedList(listMember))
                        {
                            object? payload = value is INeoValuePayloadProvider provider
                                ? provider.ToNeoValuePayload() : value;
                            client.StageWritablePayloadRows(plan, ownership, payload);
                            var list = (NeoMemberListWritable)NeoMember.CreateWritable(
                                client, listMember, existingId, ownership);
                            list.PrepareAssignSerialized(plan, NeoValueWritePayload.FromValue(
                                payload is NeoValuePayload wrapped ? wrapped.value : payload));
                            plan.AfterCommit(() => NSGetterEvaluator.InvalidateCachedCollection(existingId, ownership, ctx));
                            return;
                        }
                        var replaced = CreateValueRow(plan, client, ownership, member, value, existingId, existing!.createdAt, now);
                        replaced.classId = existing.classId;
                        StoreReplacedRow(plan, client, ownership, replaced, member, ctx);
                        return;
                    }
                    if (parentOwnership == NeoValueOwnership.Asset)
                        throw new NSGetterRuntimeError($"Cannot bind missing member '{key}' on an immutable parent.");
                    ObjectMemberValue linked = WritableParent();
                    if (TryGetClassValueReferenceId(
                            value,
                            MemberKindInfo(member),
                            ctx,
                            out string? linkedReferenceId))
                    {
                        linked.value![key] = ImportClassValueReference(
                            plan, client,
                            ownership,
                            linkedReferenceId!,
                            ctx);
                        plan.AfterCommit(() => ctx.allocationTracker.RegisterConstructedParent(
                            linked.value[key],
                            parentRowId));
                    }
                    else
                    {
                        var childId = Guid.NewGuid().ToString();
                        var next = CreateValueRow(plan, client, ownership, member, value, childId, now, now);
                        StoreWritableRow(plan, ownership, next, ctx);
                        linked.value![key] = childId;
                    }
                    linked.updatedAt = now;
                    StoreWritableRow(plan, parentOwnership, linked, ctx);
                });
            }
        }

        /// <summary>
        /// P42 §1.2 and §3. Write target for one <b>field</b> of a structured
        /// leaf — <c>Sprite.fileId</c>, <c>Sprite.sliceIndex</c>,
        /// <c>Position.y</c>, <c>Tint.a</c>.
        ///
        /// <para>The leaf stays the storage unit. Descending into it is
        /// addressing, not granularity: this reads the leaf's current value,
        /// replaces the one named field, and writes the whole row back through
        /// the ordinary write path. Sibling fields are copied from whatever the
        /// row holds <b>right now</b>, which is what lets a clip and game code
        /// own different components of the same vector (§1.4).</para>
        ///
        /// <para>The field table itself lives in
        /// <see cref="NeoAnimationLeafFields"/> — the same P42 §1.1 table the
        /// animation override path validates against, consulted here so the
        /// two write paths cannot drift on which keys exist per kind.</para>
        /// </summary>
        private sealed class NeoStructuredLeafFieldWriteTarget : NeoResolvedWriteTarget
        {
            private readonly string rowId;
            private readonly string field;
            private readonly TypeInfo fieldType;
            private readonly NeoValueOwnership ownership;

            internal NeoStructuredLeafFieldWriteTarget(
                string rowId,
                string field,
                TypeInfo fieldType,
                NeoValueOwnership ownership)
            {
                this.rowId = rowId;
                this.field = field;
                this.fieldType = fieldType;
                this.ownership = ownership;
            }

            /// <summary>
            /// The four stored-row types that carry a P42 §1.1 field surface.
            /// <c>FileMemberValue</c> is deliberately absent: an Audio or File
            /// value is a <c>fileId</c> and nothing else, and §"Non-goals"
            /// keeps it that way.
            /// </summary>
            internal static bool IsStructuredLeafRow(MemberValue row)
            {
                return row is SpriteMemberValue
                    || row is Vector2MemberValue
                    || row is Vector3MemberValue
                    || row is ColorMemberValue;
            }

            public override object? ReadCurrentValue(
                NeoClient client,
                NSGetterEvaluator.Context ctx)
            {
                if (!client.TryGetValue(ownership, rowId, out MemberValue? row))
                {
                    return null;
                }
                return ReadField(row);
            }

            public override void Write(
                NeoClient client,
                object? value,
                NSGetterEvaluator.Context ctx)
            {
                PrepareWrite(client, plan =>
                {
                    string writableRowId = PrepareWritableRow(plan, client, rowId, ownership);
                    if (!client.TryGetValue(ownership, writableRowId, out MemberValue? row))
                    {
                        throw new NSGetterRuntimeError($"Missing target row '{writableRowId}'.");
                    }
                    ApplyField(row, value);
                    row.updatedAt = NeoTimestamp.Now();
                    StoreWritableRow(plan, ownership, row, ctx);
                });
            }

            private object? ReadField(MemberValue row)
            {
                switch (row)
                {
                    case SpriteMemberValue sprite:
                        {
                            SpriteValue? current = sprite.value;
                            if (current is null)
                                return null;
                            RequireLegalKey(NeoAnimationLeafKind.Sprite);
                            return field == NeoAnimationLeafFields.FileIdKey
                                ? current.fileId
                                : (object)current.sliceIndex;
                        }
                    case Vector3MemberValue vector3:
                        {
                            NeoVector3Value? current = vector3.value;
                            if (current is null)
                                return null;
                            RequireLegalKey(NeoAnimationLeafKind.Vector3);
                            return field switch
                            {
                                "x" => current.x,
                                "y" => current.y,
                                _ => current.z,
                            };
                        }
                    case Vector2MemberValue vector2:
                        {
                            NeoVector2Value? current = vector2.value;
                            if (current is null)
                                return null;
                            RequireLegalKey(NeoAnimationLeafKind.Vector2);
                            return field == "x" ? current.x : current.y;
                        }
                    case ColorMemberValue color:
                        {
                            NeoColorValue? current = color.value;
                            if (current is null)
                                return null;
                            RequireLegalKey(NeoAnimationLeafKind.Color);
                            return field switch
                            {
                                "r" => current.r,
                                "g" => current.g,
                                "b" => current.b,
                                _ => current.a,
                            };
                        }
                    default:
                        return null;
                }
            }

            /// <summary>
            /// Read-modify-write. A fresh payload object is composed from the
            /// row's current value and then installed, rather than mutating the
            /// existing payload in place, so nothing that still aliases the
            /// pre-write instance observes a torn value.
            /// </summary>
            private void ApplyField(MemberValue row, object? value)
            {
                switch (row)
                {
                    case SpriteMemberValue sprite:
                        {
                            RequireLegalKey(NeoAnimationLeafKind.Sprite);
                            SpriteValue current = RequireLeafValue<SpriteValue>(
                                sprite.value,
                                NeoAnimationLeafKind.Sprite);
                            var composed = new SpriteValue
                            {
                                fileId = current.fileId,
                                sliceIndex = current.sliceIndex,
                            };
                            if (field == NeoAnimationLeafFields.FileIdKey)
                            {
                                // §2.2: the right-hand side is a registry symbol,
                                // which lowers to the project file record id — a
                                // bare string on the wire.
                                if (value is null)
                                {
                                    composed.fileId = null!;
                                }
                                else if (value is string fileId)
                                {
                                    composed.fileId = fileId;
                                }
                                else
                                {
                                    throw new NSGetterRuntimeError(
                                        "Sprite field 'fileId' must be a project image reference or null.");
                                }
                            }
                            else
                            {
                                int sliceIndex = RequireInteger(value);
                                if (sliceIndex < 0)
                                {
                                    throw new NSGetterRuntimeError(
                                        "Sprite field 'sliceIndex' must be 0 or greater.");
                                }
                                composed.sliceIndex = sliceIndex;
                            }
                            sprite.value = composed;
                            return;
                        }
                    case Vector3MemberValue vector3:
                        {
                            RequireLegalKey(NeoAnimationLeafKind.Vector3);
                            NeoVector3Value current = RequireLeafValue<NeoVector3Value>(
                                vector3.value,
                                NeoAnimationLeafKind.Vector3);
                            float component = RequireComponent(value);
                            vector3.value = new NeoVector3Value
                            {
                                x = field == "x" ? component : current.x,
                                y = field == "y" ? component : current.y,
                                z = field == "z" ? component : current.z,
                            };
                            return;
                        }
                    case Vector2MemberValue vector2:
                        {
                            RequireLegalKey(NeoAnimationLeafKind.Vector2);
                            NeoVector2Value current = RequireLeafValue<NeoVector2Value>(
                                vector2.value,
                                NeoAnimationLeafKind.Vector2);
                            float component = RequireComponent(value);
                            vector2.value = new NeoVector2Value
                            {
                                x = field == "x" ? component : current.x,
                                y = field == "y" ? component : current.y,
                            };
                            return;
                        }
                    case ColorMemberValue color:
                        {
                            RequireLegalKey(NeoAnimationLeafKind.Color);
                            NeoColorValue current = RequireLeafValue<NeoColorValue>(
                                color.value,
                                NeoAnimationLeafKind.Color);
                            float channel = RequireColorChannel(value);
                            color.value = new NeoColorValue
                            {
                                r = field == "r" ? channel : current.r,
                                g = field == "g" ? channel : current.g,
                                b = field == "b" ? channel : current.b,
                                a = field == "a" ? channel : current.a,
                            };
                            return;
                        }
                    default:
                        throw new NSGetterRuntimeError(
                            "Assignment receiver must be a list, dictionary, or class object.");
                }
            }

            /// <summary>
            /// A stored vector row cannot tell Vector2 from Vector2Int — the
            /// member declaration can, and so can the resolver, which types an
            /// integer vector's components as Int. That is the only signal
            /// available on this path, and it is the authoritative one.
            /// </summary>
            private NeoAnimationLeafKind NarrowKind(NeoAnimationLeafKind kind)
            {
                if (fieldType.type != MemberKind.Int)
                    return kind;
                return kind switch
                {
                    NeoAnimationLeafKind.Vector2 => NeoAnimationLeafKind.Vector2Int,
                    NeoAnimationLeafKind.Vector3 => NeoAnimationLeafKind.Vector3Int,
                    _ => kind,
                };
            }

            private void RequireLegalKey(NeoAnimationLeafKind kind)
            {
                if (NeoAnimationLeafFields.IsLegalKey(kind, field))
                    return;
                NeoAnimationLeafKind narrowed = NarrowKind(kind);
                throw new NSGetterRuntimeError(
                    $"'{field}' is not a field of a {NeoAnimationLeafFields.Describe(narrowed)} value. Legal fields: {string.Join(", ", NeoAnimationLeafFields.LegalKeys(narrowed))}.");
            }

            /// <summary>
            /// P42 §1.3: there is no record to merge a field into when the leaf
            /// is null, so the write is rejected rather than inventing siblings.
            /// </summary>
            private T RequireLeafValue<T>(T? current, NeoAnimationLeafKind kind)
                where T : class
            {
                if (current is not null)
                    return current;
                throw new NSGetterRuntimeError(
                    $"Cannot assign field '{field}' because the {NeoAnimationLeafFields.Describe(NarrowKind(kind))} value at '{rowId}' is null.");
            }

            /// <summary>
            /// A vector component. Integral only when the resolver typed the
            /// field as Int, which it does for exactly Vector2Int and
            /// Vector3Int — P42 §1.4's "no runtime coercion" rule.
            /// </summary>
            private float RequireComponent(object? value)
            {
                double numeric = ToDouble(value, $"Vector field '{field}'");
                if (double.IsNaN(numeric) || double.IsInfinity(numeric))
                {
                    throw new NSGetterRuntimeError(
                        $"Vector field '{field}' must be a finite number.");
                }
                if (fieldType.type == MemberKind.Int
                    && !NeoNumbers.IsWhole(numeric))
                {
                    throw new NSGetterRuntimeError(
                        $"Vector field '{field}' must be an integer on an integer vector; found {numeric}.");
                }
                return (float)numeric;
            }

            /// <summary>
            /// P42 decision D2: a colour channel outside <c>[0, 1]</c> is
            /// <b>rejected</b>, never clamped — matching
            /// <c>NeoColorValueConverter</c>, which already refuses one on
            /// deserialize.
            /// </summary>
            private float RequireColorChannel(object? value)
            {
                double numeric = ToDouble(value, $"Colour channel '{field}'");
                if (double.IsNaN(numeric) || double.IsInfinity(numeric))
                {
                    throw new NSGetterRuntimeError(
                        $"Colour channel '{field}' must be a finite number.");
                }
                if (numeric < 0d || numeric > 1d)
                {
                    throw new NSGetterRuntimeError(
                        $"Colour channel '{field}' must be within [0, 1]; found {numeric}.");
                }
                return (float)numeric;
            }

            private int RequireInteger(object? value)
            {
                double numeric = ToDouble(value, $"Sprite field '{field}'");
                if (!NeoNumbers.IsWhole(numeric)
                    || double.IsNaN(numeric)
                    || double.IsInfinity(numeric))
                {
                    throw new NSGetterRuntimeError(
                        $"Sprite field '{field}' must be a whole number.");
                }
                return (int)numeric;
            }
        }

        private sealed class NeoDictionaryEntryWriteTarget : NeoResolvedWriteTarget
        {
            private readonly string parentRowId;
            private readonly string key;
            private readonly TypeInfo typeInfo;
            private readonly NeoValueOwnership ownership;

            public NeoDictionaryEntryWriteTarget(
                string parentRowId,
                string key,
                TypeInfo typeInfo,
                NeoValueOwnership ownership)
            {
                this.parentRowId = parentRowId;
                this.key = key;
                this.typeInfo = typeInfo;
                this.ownership = ownership;
            }

            public override object? ReadCurrentValue(
                NeoClient client,
                NSGetterEvaluator.Context ctx)
            {
                if (!client.TryGetValue(parentRowId, out ObjectMemberValue? parent)
                    || parent.value == null
                    || !parent.value.TryGetValue(key, out string childId)
                    || !client.TryGetValue(childId, out MemberValue? child))
                {
                    return null;
                }
                return ReadRowValue(child);
            }

            public override void Write(
                NeoClient client,
                object? value,
                NSGetterEvaluator.Context ctx)
            {
                var target = new NeoDictionaryWriteTarget(parentRowId, typeInfo, ownership);
                target.Set(client, key, value, ctx);
            }
        }

        private sealed class NeoListIndexWriteTarget : NeoResolvedWriteTarget
        {
            private readonly string parentRowId;
            private readonly int index;
            private readonly TypeInfo typeInfo;
            private readonly NeoValueOwnership ownership;

            public NeoListIndexWriteTarget(
                string parentRowId,
                int index,
                TypeInfo typeInfo,
                NeoValueOwnership ownership)
            {
                this.parentRowId = parentRowId;
                this.index = index;
                this.typeInfo = typeInfo;
                this.ownership = ownership;
            }

            public override object? ReadCurrentValue(
                NeoClient client,
                NSGetterEvaluator.Context ctx)
            {
                if (!client.TryGetValue(parentRowId, out ArrayMemberValue? parent)
                    || parent.value == null
                    || index < 0
                    || index >= parent.value.Length
                    || !client.TryGetValue(parent.value[index], out MemberValue? child))
                {
                    throw new NSGetterRuntimeError($"List index out of bounds: {index}");
                }
                return ReadRowValue(child);
            }

            public override void Write(
                NeoClient client,
                object? value,
                NSGetterEvaluator.Context ctx)
            {
                PrepareWrite(client, plan =>
                {
                    PrepareWritableRow(plan, client, parentRowId, ownership);
                    if (!client.TryGetValue(parentRowId, out ArrayMemberValue? parent)
                        || parent.value == null
                        || index < 0
                        || index >= parent.value.Length)
                    {
                        throw new NSGetterRuntimeError($"List index out of bounds: {index}");
                    }
                    var childId = parent.value[index];
                    if (TryGetClassValueReferenceId(
                            value,
                            typeInfo,
                            ctx,
                            out string? referenceId))
                    {
                        string importedId = ImportClassValueReference(
                            plan, client,
                            ownership,
                            referenceId!,
                            ctx,
                            childId);
                        if (importedId == childId)
                            return;
                        parent.value[index] = importedId;
                        plan.AfterCommit(() => ctx.allocationTracker.RegisterConstructedParent(
                            importedId,
                            parentRowId));
                        parent.updatedAt = NeoTimestamp.Now();
                        StoreWritableRow(plan, ownership, parent, ctx);
                        client.StageUnlinkedRemovals(plan, ownership, new[] { childId }, EntryReleaseMember(client, parent, typeInfo));
                        return;
                    }
                    if (!client.TryGetValue(childId, out MemberValue? existing))
                    {
                        throw new NSGetterRuntimeError($"Missing list child row '{childId}'.");
                    }
                    var next = CreateValueRow(
                        plan, client,
                        ownership,
                        MemberFromTypeInfo(typeInfo),
                        value,
                        childId,
                        existing.createdAt,
                        NeoTimestamp.Now());
                    next.classId = existing.classId;
                    StoreReplacedRow(plan, client, ownership, next, EntryReleaseMember(client, parent, typeInfo), ctx);
                });
            }
        }

        private abstract class NeoResolvedCollectionTarget
        {
            public abstract void Mutate(
                NeoClient client,
                string mutation,
                object?[] args,
                NSGetterEvaluator.Context ctx);
        }

        private sealed class NeoUnorderedListWriteTarget : NeoResolvedCollectionTarget
        {
            private readonly NeoWritePlan? preparedPlan;
            private readonly string rowId;
            private readonly ListMember member;
            private readonly NeoValueOwnership ownership;

            public NeoUnorderedListWriteTarget(string rowId, ListMember member, NeoValueOwnership ownership, NeoWritePlan? preparedPlan = null)
            {
                this.preparedPlan = preparedPlan;
                this.rowId = rowId;
                this.member = member;
                this.ownership = ownership;
            }

            public override void Mutate(NeoClient client, string mutation, object?[] args,
                NSGetterEvaluator.Context ctx)
            {
                NeoMemberListWritable list = (NeoMemberListWritable)NeoMember.CreateWritable(client, member, rowId, ownership);
                TypeInfo entryType = MemberKindInfo(list.EntryMember);
                switch (mutation)
                {
                    case CollectionMutationKind.Add:
                        PrepareWrite(client, plan =>
                        {
                            NeoValueWritePayload payload;
                            if (TryGetClassValueReferenceId(args[0], entryType, ctx, out string? referenceId))
                            {
                                string importedId = ImportClassValueReference(plan, client, ownership, referenceId!, ctx);
                                payload = NeoValueWritePayload.FromValueReference(importedId, null);
                            }
                            else
                            {
                                object? value = args[0] is INeoValuePayloadProvider provider
                                    ? provider.ToNeoValuePayload() : args[0];
                                payload = NeoValueWritePayload.FromValue(value);
                            }
                            string addedId = list.PrepareAddSerialized(plan, payload);
                            plan.AfterCommit(() => ctx.allocationTracker.RegisterConstructedParent(addedId, rowId));
                        }, preparedPlan);
                        break;
                    case CollectionMutationKind.Remove:
                        string? removeId = TryGetClassValueReferenceId(args[0], entryType, ctx, out string? id)
                            ? id : null;
                        foreach (string entryId in list.ResolveEntryValueIds())
                        {
                            if (entryId == removeId || (removeId is null
                                && client.TryGetValue(ownership, entryId, out MemberValue? entry)
                                && JsEqual(ReadEntryValue(entry, list.EntryMember, ctx), args[0])))
                            {
                                list.RemoveById(entryId);
                                break;
                            }
                        }
                        break;
                    case CollectionMutationKind.Clear:
                        list.ClearSerialized();
                        break;
                    default:
                        throw new NSGetterRuntimeError($"Unsupported unordered list mutation '{mutation}'.");
                }
                if (preparedPlan is null)
                    NSGetterEvaluator.InvalidateCachedCollection(rowId, ownership, ctx);
                else
                    preparedPlan.AfterCommit(() => NSGetterEvaluator.InvalidateCachedCollection(rowId, ownership, ctx));
            }
        }

        private sealed class NeoListWriteTarget : NeoResolvedCollectionTarget
        {
            private readonly NeoWritePlan? preparedPlan;
            private readonly string rowId;
            private readonly TypeInfo entryTypeInfo;
            private readonly JsonMember? entryMember;
            private readonly NeoValueOwnership ownership;

            public NeoListWriteTarget(
                string rowId,
                TypeInfo entryTypeInfo,
                JsonMember? entryMember,
                NeoValueOwnership ownership,
                NeoWritePlan? preparedPlan = null)
            {
                this.preparedPlan = preparedPlan;
                this.rowId = rowId;
                this.entryTypeInfo = entryTypeInfo;
                this.entryMember = entryMember;
                this.ownership = ownership;
            }

            public override void Mutate(
                NeoClient client,
                string mutation,
                object?[] args,
                NSGetterEvaluator.Context ctx)
            {
                PrepareWrite(client, plan =>
                {
                    PrepareWritableRow(plan, client, rowId, ownership);
                    if (!client.TryGetValue(rowId, out ArrayMemberValue? row))
                    {
                        throw new NSGetterRuntimeError($"Missing list row '{rowId}'.");
                    }
                    row.value ??= Array.Empty<string>();
                    NeoTimestamp now = NeoTimestamp.Now();
                    switch (mutation)
                    {
                        case CollectionMutationKind.Insert:
                        case CollectionMutationKind.Add:
                            {
                                int insertionIndex = mutation == CollectionMutationKind.Insert
                                    ? ToInt(args[0], "Insert index")
                                    : row.value.Length;
                                if (insertionIndex < 0 || insertionIndex > row.value.Length)
                                    throw new NSGetterRuntimeError("List.Insert index is out of range.");
                                object? insertedValue = args[mutation == CollectionMutationKind.Insert ? 1 : 0];
                                if (TryGetClassValueReferenceId(
                                        insertedValue,
                                        entryTypeInfo,
                                        ctx,
                                        out string? referenceId))
                                {
                                    var referencedNext = new string[row.value.Length + 1];
                                    Array.Copy(row.value, 0, referencedNext, 0, insertionIndex);
                                    Array.Copy(row.value, insertionIndex, referencedNext, insertionIndex + 1, row.value.Length - insertionIndex);
                                    string importedId = ImportClassValueReference(
                                        plan, client,
                                        ownership,
                                        referenceId!,
                                        ctx);
                                    plan.AfterCommit(() => ctx.allocationTracker.RegisterConstructedParent(
                                        importedId,
                                        rowId));
                                    referencedNext[insertionIndex] = importedId;
                                    row.value = referencedNext;
                                    row.updatedAt = now;
                                    StoreWritableRow(plan, ownership, row, ctx);
                                    return;
                                }
                                var childId = Guid.NewGuid().ToString();
                                var child = CreateValueRow(
                                    plan, client,
                                    ownership,
                                    MemberFromTypeInfo(entryTypeInfo),
                                    insertedValue,
                                    childId,
                                    now,
                                    now);
                                StoreWritableRow(plan, ownership, child, ctx);
                                var next = new string[row.value.Length + 1];
                                Array.Copy(row.value, 0, next, 0, insertionIndex);
                                Array.Copy(row.value, insertionIndex, next, insertionIndex + 1, row.value.Length - insertionIndex);
                                next[insertionIndex] = childId;
                                row.value = next;
                                row.updatedAt = now;
                                StoreWritableRow(plan, ownership, row, ctx);
                                return;
                            }
                        case CollectionMutationKind.RemoveAt:
                            RemoveAt(
                                plan, client,
                                ownership,
                                row,
                                ToInt(args[0], "RemoveAt index"),
                                now,
                                entryTypeInfo,
                                ctx);
                            return;
                        case CollectionMutationKind.Remove:
                            {
                                string? referenceId = TryGetClassValueReferenceId(
                                    args[0],
                                    entryTypeInfo,
                                    ctx,
                                    out string? matchedReferenceId)
                                        ? matchedReferenceId
                                        : null;
                                for (int i = 0; i < row.value.Length; i++)
                                {
                                    if (referenceId != null && row.value[i] == referenceId)
                                    {
                                        RemoveAt(plan, client, ownership, row, i, now, entryTypeInfo, ctx);
                                        return;
                                    }
                                    if (!client.TryGetValue(row.value[i], out MemberValue? child))
                                        continue;
                                    if (!JsEqual(ReadEntryValue(child, entryMember, ctx), args[0]))
                                        continue;
                                    RemoveAt(plan, client, ownership, row, i, now, entryTypeInfo, ctx);
                                    return;
                                }
                                return;
                            }
                        case CollectionMutationKind.Clear:
                            {
                                var removedIds = row.value;
                                row.value = Array.Empty<string>();
                                row.updatedAt = now;
                                StoreWritableRow(plan, ownership, row, ctx);
                                client.StageUnlinkedRemovals(plan, ownership, removedIds, EntryReleaseMember(client, row, entryTypeInfo));
                                return;
                            }
                        default:
                            throw new NSGetterRuntimeError($"Unsupported list mutation '{mutation}'.");
                    }
                }, preparedPlan);
            }

            private static void RemoveAt(
                NeoWritePlan plan, NeoClient client,
                NeoValueOwnership ownership,
                ArrayMemberValue row,
                int index,
                NeoTimestamp now,
                TypeInfo entryTypeInfo,
                NSGetterEvaluator.Context ctx)
            {
                if (row.value == null || index < 0 || index >= row.value.Length)
                {
                    throw new NSGetterRuntimeError($"List index out of bounds: {index}");
                }
                string removedId = row.value[index];
                var next = new string[row.value.Length - 1];
                for (int i = 0, j = 0; i < row.value.Length; i++)
                {
                    if (i == index)
                        continue;
                    next[j++] = row.value[i];
                }
                row.value = next;
                row.updatedAt = now;
                StoreWritableRow(plan, ownership, row, ctx);
                client.StageUnlinkedRemovals(plan, ownership, new[] { removedId }, EntryReleaseMember(client, row, entryTypeInfo));
            }
        }

        private sealed class NeoLookupSetWriteTarget : NeoResolvedCollectionTarget
        {
            private readonly NeoWritePlan? preparedPlan;
            private readonly string rowId;
            private readonly LookupTypeInfo typeInfo;
            private readonly NeoValueOwnership ownership;

            public NeoLookupSetWriteTarget(string rowId, LookupTypeInfo typeInfo, NeoValueOwnership ownership, NeoWritePlan? preparedPlan = null)
            {
                this.preparedPlan = preparedPlan;
                this.rowId = rowId;
                this.typeInfo = typeInfo;
                this.ownership = ownership;
            }

            public override void Mutate(
                NeoClient client,
                string mutation,
                object?[] args,
                NSGetterEvaluator.Context ctx)
            {
                PrepareWrite(client, plan =>
                {
                    EnsureWritableRow(client, rowId, ownership);
                    if (!client.TryGetValue(rowId, out ArrayMemberValue? row))
                    {
                        throw new NSGetterRuntimeError($"Missing lookup row '{rowId}'.");
                    }
                    row = (ArrayMemberValue)client.CloneRowForWrite(row);
                    row.value ??= Array.Empty<string>();
                    NeoTimestamp now = NeoTimestamp.Now();
                    switch (mutation)
                    {
                        case CollectionMutationKind.Add:
                            {
                                string selectionId = ResolveLookupSelectionId(client, typeInfo, args[0], ctx);
                                foreach (string existingId in row.value)
                                {
                                    if (existingId == selectionId)
                                        return;
                                }
                                var next = new string[row.value.Length + 1];
                                Array.Copy(row.value, next, row.value.Length);
                                next[row.value.Length] = selectionId;
                                row.value = next;
                                row.updatedAt = now;
                                StoreWritableRow(plan, ownership, row, ctx);
                                return;
                            }
                        case CollectionMutationKind.Remove:
                            {
                                string selectionId = ResolveLookupSelectionId(client, typeInfo, args[0], ctx);
                                int index = -1;
                                for (int i = 0; i < row.value.Length; i++)
                                {
                                    if (row.value[i] == selectionId)
                                    {
                                        index = i;
                                        break;
                                    }
                                }
                                if (index < 0)
                                    return;
                                var next = new string[row.value.Length - 1];
                                for (int i = 0, j = 0; i < row.value.Length; i++)
                                {
                                    if (i == index)
                                        continue;
                                    next[j++] = row.value[i];
                                }
                                row.value = next;
                                row.updatedAt = now;
                                StoreWritableRow(plan, ownership, row, ctx);
                                return;
                            }
                        case CollectionMutationKind.Clear:
                            row.value = Array.Empty<string>();
                            row.updatedAt = now;
                            StoreWritableRow(plan, ownership, row, ctx);
                            return;
                        default:
                            throw new NSGetterRuntimeError($"Unsupported lookup set mutation '{mutation}'.");
                    }
                }, preparedPlan);
            }
        }

        private sealed class NeoDictionaryWriteTarget : NeoResolvedCollectionTarget
        {
            private readonly NeoWritePlan? preparedPlan;
            private readonly string rowId;
            private readonly TypeInfo entryTypeInfo;
            private readonly NeoValueOwnership ownership;

            public NeoDictionaryWriteTarget(string rowId, TypeInfo entryTypeInfo, NeoValueOwnership ownership, NeoWritePlan? preparedPlan = null)
            {
                this.preparedPlan = preparedPlan;
                this.rowId = rowId;
                this.entryTypeInfo = entryTypeInfo;
                this.ownership = ownership;
            }

            public override void Mutate(
                NeoClient client,
                string mutation,
                object?[] args,
                NSGetterEvaluator.Context ctx)
            {
                switch (mutation)
                {
                    case CollectionMutationKind.Add:
                        Set(
                            client,
                            ToStringKey(args[0], "Dictionary Add key"),
                            args[1],
                            ctx);
                        return;
                    case CollectionMutationKind.Remove:
                        Remove(
                            client,
                            ToStringKey(args[0], "Dictionary Remove key"),
                            ctx);
                        return;
                    case CollectionMutationKind.Clear:
                        Clear(client, ctx);
                        return;
                    default:
                        throw new NSGetterRuntimeError($"Unsupported dictionary mutation '{mutation}'.");
                }
            }

            public void Set(
                NeoClient client,
                string key,
                object? value,
                NSGetterEvaluator.Context ctx)
            {
                PrepareWrite(client, plan =>
                {
                    PrepareWritableRow(plan, client, rowId, ownership);
                    if (!client.TryGetValue(rowId, out ObjectMemberValue? row))
                    {
                        throw new NSGetterRuntimeError($"Missing dictionary row '{rowId}'.");
                    }
                    row.value ??= new Dictionary<string, string>();
                    NeoTimestamp now = NeoTimestamp.Now();
                    if (row.value.TryGetValue(key, out string existingId)
                        && client.TryGetValue(existingId, out MemberValue? existing))
                    {
                        if (TryGetClassValueReferenceId(
                                value,
                                entryTypeInfo,
                                ctx,
                                out string? referenceId))
                        {
                            string importedId = ImportClassValueReference(
                                plan, client,
                                ownership,
                                referenceId!,
                                ctx,
                                existingId);
                            if (importedId == existingId)
                                return;
                            row.value[key] = importedId;
                            plan.AfterCommit(() => ctx.allocationTracker.RegisterConstructedParent(
                                importedId,
                                rowId));
                            row.updatedAt = now;
                            StoreWritableRow(plan, ownership, row, ctx);
                            client.StageUnlinkedRemovals(plan, ownership, new[] { existingId }, EntryReleaseMember(client, row, entryTypeInfo));
                            return;
                        }
                        var next = CreateValueRow(
                            plan, client,
                            ownership,
                            MemberFromTypeInfo(entryTypeInfo),
                            value,
                            existingId,
                            existing.createdAt,
                            now);
                        next.classId = existing.classId;
                        StoreReplacedRow(plan, client, ownership, next, EntryReleaseMember(client, row, entryTypeInfo), ctx);
                    }
                    else
                    {
                        if (TryGetClassValueReferenceId(
                                value,
                                entryTypeInfo,
                                ctx,
                                out string? referenceId))
                        {
                            row.value[key] = ImportClassValueReference(
                                plan, client,
                                ownership,
                                referenceId!,
                                ctx);
                            plan.AfterCommit(() => ctx.allocationTracker.RegisterConstructedParent(
                                row.value[key],
                                rowId));
                            row.updatedAt = now;
                            StoreWritableRow(plan, ownership, row, ctx);
                            return;
                        }
                        var childId = Guid.NewGuid().ToString();
                        var next = CreateValueRow(
                            plan, client,
                            ownership,
                            MemberFromTypeInfo(entryTypeInfo),
                            value,
                            childId,
                            now,
                            now);
                        StoreWritableRow(plan, ownership, next, ctx);
                        row.value[key] = childId;
                    }
                    row.updatedAt = now;
                    StoreWritableRow(plan, ownership, row, ctx);
                }, preparedPlan);
            }

            private void Remove(
                NeoClient client,
                string key,
                NSGetterEvaluator.Context ctx)
            {
                PrepareWrite(client, plan =>
                {
                    PrepareWritableRow(plan, client, rowId, ownership);
                    if (!client.TryGetValue(rowId, out ObjectMemberValue? row)
                        || row.value == null
                        || !row.value.TryGetValue(key, out string removedId))
                    {
                        return;
                    }
                    row.value.Remove(key);
                    row.updatedAt = NeoTimestamp.Now();
                    StoreWritableRow(plan, ownership, row, ctx);
                    client.StageUnlinkedRemovals(plan, ownership, new[] { removedId }, EntryReleaseMember(client, row, entryTypeInfo));
                }, preparedPlan);
            }

            private void Clear(
                NeoClient client,
                NSGetterEvaluator.Context ctx)
            {
                PrepareWrite(client, plan =>
                {
                    PrepareWritableRow(plan, client, rowId, ownership);
                    if (!client.TryGetValue(rowId, out ObjectMemberValue? row)
                        || row.value == null)
                    {
                        return;
                    }
                    var removedIds = new List<string>(row.value.Values);
                    row.value.Clear();
                    row.updatedAt = NeoTimestamp.Now();
                    StoreWritableRow(plan, ownership, row, ctx);
                    client.StageUnlinkedRemovals(plan, ownership, removedIds, EntryReleaseMember(client, row, entryTypeInfo));
                }, preparedPlan);
            }
        }

        private enum TryPhase
        {
            Body,
            Filter,
            CatchBody,
            NoMatch,
            Completed,
        }

        private enum ForPhase
        {
            Initializer,
            Condition,
            Body,
            Iterator,
        }

        private abstract class LoopExecutionState
        {
            private string? bindingId;
            private bool hadPreviousBinding;
            private object? previousBinding;
            private bool readOnly;
            private NeoScriptScope? bodyScope;
            private NeoScriptScopeLayout? bodyLayout;
            private bool bodyLocalsKnown;
            private bool bodyDeclaresLocals;
            private bool bindingRestored;

            protected LoopExecutionState(
                string? bindingId,
                NeoScriptScope scope,
                bool readOnly = false) =>
                Begin(bindingId, scope, readOnly);

            /// <summary>
            /// Starts a run. A reused state keeps what describes its
            /// instruction's body, which never changes.
            /// </summary>
            protected void Begin(
                string? bindingId,
                NeoScriptScope scope,
                bool readOnly = false)
            {
                this.bindingId = bindingId;
                hadPreviousBinding = bindingId is not null && scope.TryGetValue(
                    bindingId,
                    out previousBinding);
                this.readOnly = readOnly;
                bindingRestored = false;
                if (readOnly)
                {
                    MarkReadOnlyBinding(
                        scope,
                        bindingId,
                        "Cannot assign to a read-only foreach iterator binding.");
                }
            }

            /// <summary>
            /// The scope the body runs in. A body without locals runs in the
            /// loop's scope, as other blocks do; one with locals gets a
            /// pooled block that <see cref="RestoreBinding"/> returns.
            /// </summary>
            internal NeoScriptScope EnsureBodyScope(
                NeoScriptScope parentScope,
                Instruction[] body,
                ref NeoScriptScopeLayout? layout)
            {
                if (bodyScope is not null)
                    return bodyScope;
                if (!bodyLocalsKnown)
                {
                    bodyDeclaresLocals = NeoScriptScopeLayout.DeclaresLocals(body);
                    bodyLocalsKnown = true;
                }
                if (!bodyDeclaresLocals)
                    return parentScope;
                bodyLayout = layout ??= new NeoScriptScopeLayout(null, body);
                bodyScope = bodyLayout.RentScope();
                bodyScope.BindBlock(parentScope);
                return bodyScope;
            }

            /// <summary>
            /// Clears the body's own locals. The body scope itself is kept:
            /// rebuilding it per iteration only allocated.
            /// </summary>
            internal void ResetBodyScope() => bodyScope?.ResetLocals();

            /// <summary>
            /// A paused run keeps its body scope for its continuation, so
            /// the layout stops pooling it.
            /// </summary>
            internal void DetachBodyScope()
            {
                if (bodyScope is not null)
                    bodyLayout!.AbandonScope(bodyScope);
            }

            internal void RestoreBinding(NeoScriptScope scope)
            {
                if (bindingRestored)
                    return;
                bindingRestored = true;
                if (bodyScope is not null)
                {
                    bodyLayout!.ReturnScope(bodyScope);
                    bodyScope = null;
                }
                if (readOnly)
                {
                    UnmarkReadOnlyBinding(scope, bindingId);
                }
                if (bindingId is null)
                    return;
                if (hadPreviousBinding)
                {
                    scope[bindingId] = previousBinding;
                    // A reused state must not keep the value alive.
                    previousBinding = null;
                }
                else
                {
                    scope.Remove(bindingId);
                }
            }
        }

        private sealed class WhileExecutionState : LoopExecutionState
        {
            private readonly NeoScriptExecutionOptions? options;

            internal WhileExecutionState(
                WhileInstruction instruction,
                NeoScriptScope scope,
                NeoScriptExecutionOptions? options)
                : base(null, scope)
            {
                Instruction = instruction;
                this.options = options;
                CheckCondition = instruction is not DoWhileInstruction;
                ExpressionState = ExpressionResumeState.ForInstruction(Instruction, options);
            }

            internal WhileInstruction Instruction
            {
                get;
            }
            internal bool CheckCondition
            {
                get; private set;
            }
            internal ExpressionResumeState ExpressionState
            {
                get; private set;
            }

            internal void NextCondition()
            {
                CheckCondition = true;
                // Only an instruction that cannot suspend gets the shared
                // state, which it keeps: it records nothing.
                if (!ReferenceEquals(ExpressionState, ExpressionResumeState.Immediate))
                    ExpressionState = ExpressionResumeState.ForInstruction(Instruction, options);
            }
        }

        private sealed class ForExecutionState : LoopExecutionState
        {
            private NeoScriptExecutionOptions? options;

            internal ForExecutionState(
                ForInstruction instruction,
                NeoScriptScope scope,
                NeoScriptExecutionOptions? options)
                : base(instruction.initializer.id, scope)
            {
                Instruction = instruction;
                Start(options);
            }

            internal ForInstruction Instruction
            {
                get;
            }

            /// <summary>Starts another run of the same instruction.</summary>
            internal void Begin(NeoScriptScope scope, NeoScriptExecutionOptions? options)
            {
                Begin(Instruction.initializer.id, scope);
                Start(options);
            }

            private void Start(NeoScriptExecutionOptions? options)
            {
                this.options = options;
                Phase = ForPhase.Initializer;
                ExpressionState = ExpressionResumeState.ForInstruction(Instruction, options);
            }
            internal ForPhase Phase
            {
                get; private set;
            }
            internal ExpressionResumeState ExpressionState
            {
                get; private set;
            }

            internal void MoveTo(ForPhase phase)
            {
                Phase = phase;
                // Only an instruction that cannot suspend gets the shared
                // state, which it keeps: it records nothing.
                if (!ReferenceEquals(ExpressionState, ExpressionResumeState.Immediate))
                    ExpressionState = ExpressionResumeState.ForInstruction(Instruction, options);
            }

            /// <summary>
            /// Drops the finished run's options and resume state: the
            /// instruction outlives the client they belong to.
            /// </summary>
            internal void Park()
            {
                options = null;
                ExpressionState = ExpressionResumeState.Immediate;
            }
        }

        private sealed class ForEachExecutionState : LoopExecutionState
        {
            internal ForEachExecutionState(
                ForEachInstruction instruction,
                NeoScriptScope scope,
                NeoScriptExecutionOptions? options)
                : base(instruction.binding.id, scope, readOnly: true)
            {
                Instruction = instruction;
                ExpressionState = ExpressionResumeState.ForInstruction(Instruction, options);
            }

            internal ForEachInstruction Instruction
            {
                get;
            }
            internal ExpressionResumeState ExpressionState
            {
                get; private set;
            }

            /// <summary>Starts another run of the same instruction.</summary>
            internal void Begin(NeoScriptScope scope, NeoScriptExecutionOptions? options)
            {
                Begin(Instruction.binding.id, scope, readOnly: true);
                ExpressionState = ExpressionResumeState.ForInstruction(Instruction, options);
                Index = 0;
            }

            // A reused state keeps its snapshot buffers.
            internal readonly NSGetterEvaluator.CollectionSnapshot Snapshot = new();

            /// <summary>
            /// Drops what the finished run held: its entries and resume
            /// state must not outlive it.
            /// </summary>
            internal void Park()
            {
                Snapshot.Clear();
                ExpressionState = ExpressionResumeState.Immediate;
            }
            internal int Index
            {
                get; set;
            }
        }

        private sealed class TryExecutionState
        {
            private NeoScriptScope? tryScope;
            private NeoScriptScope? catchScope;
            private int catchIndex;
            private NSGetterRuntimeError? originalFailure;

            private readonly NeoScriptExecutionOptions? options;

            internal TryExecutionState(
                TryInstruction instruction,
                NeoScriptExecutionOptions? options)
            {
                Instruction = instruction ?? throw new NeoScriptPreExecutionValidationError(
                    "NeoScript try instruction is missing; its compiled IR is stale or corrupt.");
                ValidateTryInstructionMetadata(instruction);
                this.options = options;
                Phase = TryPhase.Body;
                ExpressionState = ExpressionResumeState.ForInstruction(Instruction, options);
            }

            internal TryInstruction Instruction
            {
                get;
            }
            internal TryPhase Phase
            {
                get; private set;
            }
            internal ExpressionResumeState ExpressionState
            {
                get; private set;
            }
            internal CatchClause CurrentClause =>
                catchIndex >= 0 && catchIndex < Instruction.catches.Length
                    ? Instruction.catches[catchIndex]
                    : throw new NSGetterRuntimeError(
                        "NeoScript try/catch selected an invalid catch clause; its compiled IR is stale or corrupt.");

            internal NeoScriptScope EnsureTryScope(
                NeoScriptScope parentScope) =>
                tryScope ??= parentScope.CreateBlock();

            internal void BeginCatches(NSGetterRuntimeError failure)
            {
                originalFailure = failure;
                catchIndex = 0;
                PrepareCurrentClause();
            }

            internal NeoScriptScope EnsureCatchScope(
                NeoScriptScope parentScope)
            {
                if (catchScope is not null)
                    return catchScope;
                CatchClause clause = CurrentClause;
                catchScope = parentScope.CreateBlock();
                catchScope[clause.binding.id] = originalFailure?.Message
                    ?? throw new NSGetterRuntimeError(
                        "NeoScript catch clause is missing its original error.");
                MarkReadOnlyBinding(
                    catchScope,
                    clause.binding.id,
                    "Cannot assign to a read-only catch message binding.");
                return catchScope;
            }

            internal void RejectCurrentClause()
            {
                catchScope = null;
                catchIndex++;
                PrepareCurrentClause();
            }

            internal void SelectCurrentClause()
            {
                Phase = TryPhase.CatchBody;
                ExpressionState = ExpressionResumeState.ForInstruction(Instruction, options);
            }

            internal Exception CompleteWithoutMatch()
            {
                Exception failure = originalFailure
                    ?? new NSGetterRuntimeError(
                        "NeoScript try/catch is missing its original error.");
                Complete();
                return failure;
            }

            internal void Complete()
            {
                Phase = TryPhase.Completed;
            }

            private void PrepareCurrentClause()
            {
                ExpressionState = ExpressionResumeState.ForInstruction(Instruction, options);
                Phase = catchIndex >= Instruction.catches.Length
                    ? TryPhase.NoMatch
                    : CurrentClause.filter is null
                        ? TryPhase.CatchBody
                        : TryPhase.Filter;
            }
        }

        private sealed class ExpressionResumeState
        {
            /// <summary>
            /// The shared non-recording state for immediate frames. It never
            /// stores or counts anything, so one instance serves every frame.
            /// </summary>
            internal static readonly ExpressionResumeState Immediate = CreateImmediate();

            private Dictionary<string, CachedFunctionResult>? results;
            private Dictionary<string, int>? invocationCounts;
            private bool recording = true;

            private static ExpressionResumeState CreateImmediate()
            {
                var state = new ExpressionResumeState();
                state.DisableRecording();
                return state;
            }

            /// <summary>
            /// A recording state for an instruction that may suspend its
            /// frame; the shared immediate state otherwise.
            /// </summary>
            internal static ExpressionResumeState ForInstruction(
                Instruction instruction,
                NeoScriptExecutionOptions? options) =>
                options?.AllowDeferredFunctionCalls == true && instruction.MayCall
                    ? new ExpressionResumeState()
                    : Immediate;

            internal void DisableRecording() => recording = false;

            /// <summary>Whether this frame keeps results for a resume.</summary>
            internal bool Recording => recording;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void BeginInstructionAttempt()
            {
                invocationCounts?.Clear();
            }

            internal string NextInvocationKey(string callSiteId)
            {
                if (!recording)
                    return callSiteId;
                invocationCounts ??= new();
                invocationCounts.TryGetValue(callSiteId, out int occurrence);
                invocationCounts[callSiteId] = occurrence + 1;
                return callSiteId + "\n" + occurrence;
            }

            internal bool TryGet(
                string callSiteId,
                out object? value,
                out Exception? error)
            {
                if (recording && results is not null && results.TryGetValue(callSiteId, out CachedFunctionResult cached))
                {
                    value = cached.Value;
                    error = cached.Error;
                    return true;
                }
                value = null;
                error = null;
                return false;
            }

            internal void StoreValue(string callSiteId, object? value)
            {
                if (!recording)
                    return;
                NSGetterEvaluator.TemporaryLists.Forget(value);
                results ??= new();
                results[callSiteId] = new CachedFunctionResult(value, null);
            }

            internal void StoreError(string callSiteId, Exception error)
            {
                if (!recording)
                    return;
                results ??= new();
                results[callSiteId] = new CachedFunctionResult(null, error);
            }

            private readonly struct CachedFunctionResult
            {
                internal CachedFunctionResult(object? value, Exception? error)
                {
                    Value = value;
                    Error = error;
                }

                internal object? Value
                {
                    get;
                }
                internal Exception? Error
                {
                    get;
                }
            }
        }

    }

    internal sealed class NeoFunctionCallSuspended : Exception
    {
        internal NeoFunctionCallSuspended(
            string resumeKey,
            string memberId,
            NeoScriptExecutionResult execution)
        {
            ResumeKey = resumeKey;
            MemberId = memberId;
            Execution = execution;
        }

        internal string ResumeKey
        {
            get;
        }
        internal string MemberId
        {
            get;
        }
        internal NeoScriptExecutionResult Execution
        {
            get;
        }
    }

    internal sealed class NeoScriptExecutionOptions
    {
        private readonly NeoClient client;
        private readonly Action<string> warning;
        private readonly string? propertyMemberId;
        internal bool AllowDeferredFunctionCalls
        {
            get;
        }
        internal bool CancelContinuationOnDeferredDisposal
        {
            get;
        }

        private NeoScriptExecutionOptions(
            NeoClient client,
            Action<string> warning,
            string? propertyMemberId,
            bool allowDeferredFunctionCalls,
            bool cancelContinuationOnDeferredDisposal)
        {
            this.client = client;
            this.warning = warning;
            this.propertyMemberId = propertyMemberId;
            AllowDeferredFunctionCalls = allowDeferredFunctionCalls;
            CancelContinuationOnDeferredDisposal =
                cancelContinuationOnDeferredDisposal;
        }

        internal static NeoScriptExecutionOptions ForDialogue(
            NeoClient client,
            INeoDialogueLogger? logger)
        {
            return new NeoScriptExecutionOptions(
                client,
                logger is null
                    ? UnityEngine.Debug.LogWarning
                    : logger.LogWarning,
                null,
                allowDeferredFunctionCalls: true,
                cancelContinuationOnDeferredDisposal: false);
        }

        /// <summary>The options a property setter runs with when its caller has none.</summary>
        internal static NeoScriptExecutionOptions ForUnityProperty(NeoClient client, string memberId) =>
            (client.unityPropertyScriptExecutionOptions ??= new NeoScriptExecutionOptions(
                client,
                UnityEngine.Debug.LogWarning,
                null,
                allowDeferredFunctionCalls: true,
                cancelContinuationOnDeferredDisposal: false))
            .ForProperty(memberId);

        internal static NeoScriptExecutionOptions ForDirectFunction(
            NeoClient client) =>
            client.directFunctionScriptExecutionOptions ??= new NeoScriptExecutionOptions(
                client,
                UnityEngine.Debug.LogWarning,
                null,
                allowDeferredFunctionCalls: true,
                cancelContinuationOnDeferredDisposal: true);

        /// <summary>
        /// Immediate options carry no per-call state, so one instance per
        /// client serves every immediate frame and lets them share the
        /// cached expression handlers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static NeoScriptExecutionOptions ForImmediate(NeoClient client) =>
            client.immediateScriptExecutionOptions ?? CreateImmediate(client);

        // Out of line so ForImmediate's cached read inlines.
        private static NeoScriptExecutionOptions CreateImmediate(NeoClient client) =>
            client.immediateScriptExecutionOptions = new NeoScriptExecutionOptions(
                client,
                UnityEngine.Debug.LogWarning,
                null,
                allowDeferredFunctionCalls: false,
                cancelContinuationOnDeferredDisposal: false);

        internal NeoClient Client => client;

        /// <summary>
        /// Expression handlers for immediate frames executing with these
        /// options. Built once by the executor; see
        /// <see cref="NeoScriptExecutor"/>'s expression context construction.
        /// </summary>
        internal NSGetterEvaluator.Context.ExpressionHandlers? immediateHandlers;

        // Options carry no per-call state, so each property's are built once.
        private Dictionary<string, NeoScriptExecutionOptions>? propertyOptions;

        internal NeoScriptExecutionOptions ForProperty(string memberId)
        {
            propertyOptions ??= new Dictionary<string, NeoScriptExecutionOptions>(StringComparer.Ordinal);
            if (!propertyOptions.TryGetValue(memberId, out NeoScriptExecutionOptions? options))
            {
                options = new NeoScriptExecutionOptions(
                    client,
                    warning,
                    memberId,
                    AllowDeferredFunctionCalls,
                    CancelContinuationOnDeferredDisposal);
                propertyOptions.Add(memberId, options);
            }
            return options;
        }

        internal NeoScriptExecutionOptions ForFunction(bool deferred)
        {
            if (AllowDeferredFunctionCalls == deferred)
                return this;
            return new NeoScriptExecutionOptions(
                client,
                warning,
                propertyMemberId,
                allowDeferredFunctionCalls: deferred,
                cancelContinuationOnDeferredDisposal:
                    CancelContinuationOnDeferredDisposal);
        }

        internal void WarnDeferred(string functionMemberId)
        {
            if (propertyMemberId is null)
                return;
            string propertyName = client.TryGetMember(
                propertyMemberId, out JsonMember? propertyMember)
                    ? propertyMember.name
                    : propertyMemberId;
            string functionName = client.TryGetMember(
                functionMemberId, out JsonMember? functionMember)
                    ? functionMember.name
                    : functionMemberId;
            warning(
                $"NeoScript property setter '{propertyName}' ({propertyMemberId}) " +
                $"called deferred Function '{functionName}' ({functionMemberId}), " +
                "which did not call Complete/Fail inline. The setter will continue " +
                "asynchronously; any later error will be logged by the Neo Compose SDK.");
        }
    }

    internal enum NeoScriptControlTransfer
    {
        Fallthrough,
        Return,
        Break,
        Continue,
    }

    internal readonly struct NeoScriptExecutionResult
    {
        // Every instruction returns one of these by value, so the shape is
        // one reference: each reference a result stores costs a GC write
        // barrier. Null is the valueless fallthrough, a marker is any other
        // transfer, a failure or a suspension, and anything else is a
        // returned value. Only a return carries a value.
        private readonly object? state;

        private class Marker
        {
        }

        private static readonly Marker ReturnNullState = new();
        private static readonly Marker BreakState = new();
        private static readonly Marker ContinueState = new();

        private sealed class FailedState : Marker
        {
            internal readonly Exception failure;

            internal FailedState(Exception failure)
            {
                this.failure = failure;
            }
        }

        /// <summary>A suspended frame's continuation state.</summary>
        private sealed class PausedState : Marker
        {
            internal readonly string? suspendedMemberId;
            internal readonly NeoDeferredFunctionBase? deferred;
            internal readonly DeferredNativeFunctionSuspension? suspension;
            internal readonly Func<object?, NeoScriptExecutionResult>? resume;
            internal readonly Action<Exception>? failureObserver;
            internal readonly Action<Exception>? abandonmentObserver;
            internal readonly Func<Exception, NeoScriptExecutionResult?>? failureRecovery;

            internal PausedState(
                string? suspendedMemberId,
                NeoDeferredFunctionBase? deferred,
                DeferredNativeFunctionSuspension? suspension,
                Func<object?, NeoScriptExecutionResult>? resume,
                Action<Exception>? failureObserver,
                Action<Exception>? abandonmentObserver,
                Func<Exception, NeoScriptExecutionResult?>? failureRecovery)
            {
                this.suspendedMemberId = suspendedMemberId;
                this.deferred = deferred;
                this.suspension = suspension;
                this.resume = resume;
                this.failureObserver = failureObserver;
                this.abandonmentObserver = abandonmentObserver;
                this.failureRecovery = failureRecovery;
            }
        }

        private NeoScriptExecutionResult(object state)
        {
            this.state = state;
        }

        // The constructor's reference field store write-barriers even into a
        // stack temporary. Reading the argument's slot as this one-reference
        // struct copies it without one, for the results every return and
        // loop transfer builds; the result returns in a register.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static NeoScriptExecutionResult FromState(object state) =>
            UnsafeUtility.As<object, NeoScriptExecutionResult>(ref state);

        private PausedState? pausedState => state as PausedState;
        private DeferredNativeFunctionSuspension? suspension => pausedState?.suspension;
        private Func<object?, NeoScriptExecutionResult>? resume => pausedState?.resume;
        private Action<Exception>? failureObserver => pausedState?.failureObserver;
        private Action<Exception>? abandonmentObserver => pausedState?.abandonmentObserver;
        private Func<Exception, NeoScriptExecutionResult?>? failureRecovery => pausedState?.failureRecovery;

        // Every instruction reads these; Mono's size limit would otherwise
        // leave each a call.
        internal bool IsPaused
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => state is PausedState;
        }
        internal NeoScriptControlTransfer Transfer =>
            Returned ? NeoScriptControlTransfer.Return
            : ReferenceEquals(state, BreakState) ? NeoScriptControlTransfer.Break
            : ReferenceEquals(state, ContinueState) ? NeoScriptControlTransfer.Continue
            : NeoScriptControlTransfer.Fallthrough;
        internal bool Returned
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => state is not null && (state is not Marker || ReferenceEquals(state, ReturnNullState));
        }
        internal bool IsBreak
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ReferenceEquals(state, BreakState);
        }
        internal bool IsContinue
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ReferenceEquals(state, ContinueState);
        }
        /// <summary>
        /// A return, failure or break: a completed body that stops its loop.
        /// A fallthrough, the common case, answers on the null check.
        /// </summary>
        internal bool EndsLoop
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => state is not null && !ReferenceEquals(state, ContinueState) && state is not PausedState;
        }
        internal bool IsFallthrough
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => state is null or PausedState;
        }
        internal bool IsFailed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => state is FailedState;
        }
        internal Exception? Failure => (state as FailedState)?.failure;
        internal object? ReturnValue
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => state is Marker ? null : state;
        }
        internal string? SuspendedMemberId => pausedState?.suspendedMemberId;
        internal NeoDeferredFunctionBase? Deferred => pausedState?.deferred;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static NeoScriptExecutionResult Completed(
            bool returned,
            object? returnValue)
        {
            // The valueless fallthrough every block and loop iteration
            // produces is the default result.
            if (!returned)
            {
                if (returnValue is not null)
                    ThrowValuedFallthrough();
                return default;
            }
            return FromState(returnValue ?? ReturnNullState);
        }

        // Out of line so Completed stays inlinable.
        private static void ThrowValuedFallthrough() =>
            throw new ArgumentException("Only a return carries a value.", "returnValue");

        internal static NeoScriptExecutionResult Control(
            NeoScriptControlTransfer transfer)
        {
            if (transfer == NeoScriptControlTransfer.Return)
            {
                throw new ArgumentException(
                    "Return control must carry its value through Completed.",
                    nameof(transfer));
            }
            return transfer switch
            {
                NeoScriptControlTransfer.Break => FromState(BreakState),
                NeoScriptControlTransfer.Continue => FromState(ContinueState),
                _ => default,
            };
        }

        internal static NeoScriptExecutionResult Failed(Exception failure)
        {
            return new NeoScriptExecutionResult(
                new FailedState(failure ?? throw new ArgumentNullException(nameof(failure))));
        }

        internal static NeoScriptExecutionResult Paused(
            string suspendedMemberId,
            NeoDeferredFunctionBase deferred,
            DeferredNativeFunctionSuspension suspension,
            Func<object?, NeoScriptExecutionResult> resume)
        {
            return new NeoScriptExecutionResult(new PausedState(
                suspendedMemberId,
                deferred,
                suspension,
                resume,
                null,
                null,
                null));
        }

        internal void WhenDeferredSettled(
            Action<NeoScriptExecutionResult> complete,
            Action<Exception> fail)
        {
            var failureRecovery = this.failureRecovery;
            var failureObserver = this.failureObserver;
            var abandonmentObserver = this.abandonmentObserver;
            var resume = this.resume;

            if (suspension == null || resume == null)
            {
                throw new InvalidOperationException(
                    "Cannot attach deferred handlers to a completed action result.");
            }
            void FailObserved(Exception exception)
            {
                if (failureRecovery is not null)
                {
                    NeoScriptExecutionResult? recovered;
                    try
                    {
                        recovered = failureRecovery(exception);
                    }
                    catch (Exception recoveryException)
                    {
                        exception = recoveryException;
                        recovered = null;
                    }
                    if (recovered is not null)
                    {
                        try
                        {
                            complete(recovered.Value);
                        }
                        catch (Exception completionException)
                        {
                            fail(completionException);
                        }
                        return;
                    }
                }
                try
                {
                    failureObserver?.Invoke(exception);
                }
                catch (Exception observerException)
                {
                    fail(observerException);
                    return;
                }
                fail(exception);
            }

            suspension.SetContinuation(
                value =>
                {
                    NeoScriptExecutionResult resumed;
                    try
                    {
                        resumed = resume(value);
                    }
                    catch (Exception ex)
                    {
                        FailObserved(ex);
                        return;
                    }
                    try
                    {
                        complete(resumed);
                    }
                    catch (Exception ex)
                    {
                        // The NeoScript continuation already reached a result;
                        // do not run allocation failure observers a second time
                        // if the external completion callback itself fails.
                        fail(ex);
                    }
                },
                FailObserved);
            suspension.SetAbandonmentObserver(exception =>
            {
                try
                {
                    abandonmentObserver?.Invoke(exception);
                }
                catch (Exception observerException)
                {
                    fail(observerException);
                }
            });
        }

        internal NeoScriptExecutionResult Then(
            Func<NeoScriptExecutionResult, NeoScriptExecutionResult> next)
        {
            // A failed recovery is already terminal. Advancing it could let a
            // later continuation replace the original failure with success.
            if (IsFailed)
                return this;
            if (!IsPaused)
                return next(this);
            return ThenPaused(next);
        }

        private NeoScriptExecutionResult ThenPaused(
            Func<NeoScriptExecutionResult, NeoScriptExecutionResult> next)
        {
            var resume = this.resume;
            var failureRecovery = this.failureRecovery;
            if (Deferred == null || suspension == null || resume == null)
            {
                throw new InvalidOperationException(
                    "Paused action result is missing deferred continuation state.");
            }
            return new NeoScriptExecutionResult(new PausedState(
                SuspendedMemberId
                    ?? throw new InvalidOperationException(
                        "Paused action result is missing its Function member id."),
                Deferred,
                suspension,
                value => next(resume(value)),
                failureObserver,
                abandonmentObserver,
                failureRecovery is null
                    ? null
                    : exception =>
                    {
                        NeoScriptExecutionResult? recovered =
                            failureRecovery(exception);
                        return recovered?.Then(next);
                    }));
        }

        internal NeoScriptExecutionResult ObserveFailure(
            Action<Exception> observer)
        {
            if (!IsPaused)
                return this;
            return ObservePausedFailure(observer);
        }

        private NeoScriptExecutionResult ObservePausedFailure(Action<Exception> observer)
        {
            var failureObserver = this.failureObserver;
            var abandonmentObserver = this.abandonmentObserver;
            if (Deferred == null || suspension == null || resume == null)
            {
                throw new InvalidOperationException(
                    "Paused action result is missing deferred continuation state.");
            }
            Action<Exception> combinedFailure = failureObserver is null
                ? observer
                : exception =>
                {
                    failureObserver(exception);
                    observer(exception);
                };
            Action<Exception> combinedAbandonment = abandonmentObserver is null
                ? observer
                : exception =>
                {
                    abandonmentObserver(exception);
                    observer(exception);
                };
            return new NeoScriptExecutionResult(new PausedState(
                SuspendedMemberId,
                Deferred,
                suspension,
                resume,
                combinedFailure,
                combinedAbandonment,
                failureRecovery));
        }

        internal NeoScriptExecutionResult RecoverFailure(
            Func<Exception, NeoScriptExecutionResult?> recovery)
        {
            if (!IsPaused)
                return this;
            return RecoverPausedFailure(recovery);
        }

        private NeoScriptExecutionResult RecoverPausedFailure(
            Func<Exception, NeoScriptExecutionResult?> recovery)
        {
            var failureRecovery = this.failureRecovery;
            var failureObserver = this.failureObserver;
            var resume = this.resume;
            if (Deferred == null || suspension == null || resume == null)
            {
                throw new InvalidOperationException(
                    "Paused action result is missing deferred continuation state.");
            }

            Func<Exception, NeoScriptExecutionResult?> combinedRecovery =
                exception =>
                {
                    if (failureRecovery is not null)
                    {
                        NeoScriptExecutionResult? recovered;
                        try
                        {
                            recovered = failureRecovery(exception);
                        }
                        catch (Exception recoveryException)
                        {
                            failureObserver?.Invoke(recoveryException);
                            return recovery(recoveryException)
                                ?? NeoScriptExecutionResult.Failed(
                                    recoveryException);
                        }
                        if (recovered is not null)
                        {
                            if (recovered.Value.IsFailed)
                            {
                                return recovery(recovered.Value.Failure!)
                                    ?? recovered;
                            }
                            return recovered.Value.IsPaused
                                ? recovered.Value.RecoverFailure(recovery)
                                : recovered;
                        }
                    }
                    failureObserver?.Invoke(exception);
                    return recovery(exception);
                };
            return new NeoScriptExecutionResult(new PausedState(
                SuspendedMemberId,
                Deferred,
                suspension,
                value => resume(value).RecoverFailure(recovery),
                null,
                abandonmentObserver,
                combinedRecovery));
        }
    }

    internal sealed class DeferredNativeFunctionSuspension
    {
        private readonly object sync = new();
        private Action<object?>? completeContinuation;
        private Action<Exception>? failContinuation;
        private Action<Exception>? abandonmentObserver;
        private bool completed;
        private bool failed;
        private bool abandoned;
        private object? value;
        private Exception? exception;
        private Exception? abandonmentException;
        private bool invokerReturned;
        private bool completedInline;

        internal void Complete(object? completedValue)
        {
            Action<object?>? continuation;
            lock (sync)
            {
                if (completed || failed || abandoned)
                {
                    throw new InvalidOperationException(
                        "Deferred Function completion was signaled more than once.");
                }
                completed = true;
                completedInline = !invokerReturned;
                value = completedValue;
                continuation = completeContinuation;
            }
            continuation?.Invoke(completedValue);
        }

        internal void Fail(Exception ex)
        {
            Action<Exception>? continuation;
            lock (sync)
            {
                if (completed || failed || abandoned)
                {
                    throw new InvalidOperationException(
                        "Deferred Function completion was signaled more than once.");
                }
                failed = true;
                completedInline = !invokerReturned;
                exception = ex;
                continuation = failContinuation;
            }
            continuation?.Invoke(ex);
        }

        internal void Cancel(string reason)
        {
            Fail(new OperationCanceledException(reason));
        }

        /// <summary>
        /// Releases execution-owned resources when an owning dialogue/client
        /// is disposed without turning that expected lifecycle event into a
        /// dialogue failure or resuming its NeoScript continuation.
        /// </summary>
        internal void Abandon(string reason)
        {
            Action<Exception>? observer;
            var cancellation = new OperationCanceledException(reason);
            lock (sync)
            {
                if (completed || failed || abandoned)
                    return;
                abandoned = true;
                abandonmentException = cancellation;
                observer = abandonmentObserver;
            }
            observer?.Invoke(cancellation);
        }

        internal void MarkInvokerReturned()
        {
            lock (sync)
            {
                invokerReturned = true;
            }
        }

        internal bool TryGetInlineResult(
            out object? completedValue,
            out Exception? completedError)
        {
            lock (sync)
            {
                if (!completedInline)
                {
                    completedValue = null;
                    completedError = null;
                    return false;
                }
                completedValue = value;
                completedError = exception;
                return true;
            }
        }

        internal void SetContinuation(
            Action<object?> complete,
            Action<Exception> fail)
        {
            bool callComplete;
            bool callFail;
            object? completedValue;
            Exception? completedError;
            lock (sync)
            {
                completeContinuation = complete;
                failContinuation = fail;
                callComplete = completed;
                callFail = failed;
                completedValue = value;
                completedError = exception;
            }
            if (callComplete)
            {
                complete(completedValue);
            }
            else if (callFail && completedError != null)
            {
                fail(completedError);
            }
        }

        internal void SetAbandonmentObserver(Action<Exception> observer)
        {
            bool callObserver;
            Exception? abandonedWith;
            lock (sync)
            {
                abandonmentObserver = observer;
                callObserver = abandoned;
                abandonedWith = abandonmentException;
            }
            if (callObserver && abandonedWith != null)
            {
                observer(abandonedWith);
            }
        }
    }
}
