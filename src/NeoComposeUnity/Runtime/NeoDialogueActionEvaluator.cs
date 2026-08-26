// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;
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
                terminal => NeoScriptExecutor.ValidateStatementTerminal(
                    terminal,
                    "Dialogue action"));
        }
    }

    /// <summary>
    /// Shared mutation-capable NeoScript executor. Getters, NSFunctions,
    /// setters, dialogue code actions, and collection callbacks supply their
    /// own scope/context while sharing write targets, calls, and deferred
    /// continuations.
    /// </summary>
    internal static class NeoScriptExecutor
    {
        internal const int MaxLoopIterations = 10_000;

        internal static NeoScriptExecutionResult Execute(
            NeoClient client,
            FunctionWithReturnType body,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options = null,
            Func<NeoScriptExecutionResult, NeoScriptExecutionResult>?
                normalizeTerminal = null) =>
            Execute(client, body, new NeoScriptScope(scope), ctx, options,
                normalizeTerminal);

        internal static NeoScriptExecutionResult Execute(
            NeoClient client,
            FunctionWithReturnType body,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options = null,
            Func<NeoScriptExecutionResult, NeoScriptExecutionResult>?
                normalizeTerminal = null)
        {
            ValidateBodyForExecution(body);
            bool allocationScopeClosed = false;
            ctx.allocationTracker.EnterExecution();
            try
            {
                ctx.allocationTracker.ConsumeWorkUnit();
                NeoScriptExecutionResult result = ExecuteInstructions(
                    client,
                    body.instructions,
                    body.typeInfo,
                    scope,
                    ctx,
                    0,
                    null,
                    options);
                return ExitAllocationScopeWhenTerminal(result);
            }
            catch
            {
                CloseAllocationScope(null);
                throw;
            }

            void CloseAllocationScope(NeoScriptExecutionResult? terminalResult)
            {
                if (allocationScopeClosed) return;
                allocationScopeClosed = true;
                ctx.allocationTracker.ExitExecution(
                    client,
                    ctx,
                    terminalResult);
            }

            NeoScriptExecutionResult ExitAllocationScopeWhenTerminal(
                NeoScriptExecutionResult result)
            {
                if (result.IsFailed)
                {
                    CloseAllocationScope(null);
                    throw result.Failure!;
                }
                if (result.IsPaused)
                {
                    return result
                        .Then(ExitAllocationScopeWhenTerminal)
                        .ObserveFailure(_ => CloseAllocationScope(null));
                }
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
                    result = normalizeTerminal(result);
                }
                CloseAllocationScope(allocationTerminal);
                return result;
            }
        }

        internal static PreparedCallback PrepareCallback(
            NeoClient client,
            FunctionWithReturnType body,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options = null)
        {
            ValidateBodyForExecution(body);
            return new PreparedCallback(client, body, ctx, options);
        }

        private static void ValidateBodyForExecution(
            FunctionWithReturnType body)
        {
            int compilerRevision = body.compilerRevision ?? 1;
            if (compilerRevision < 1
                || compilerRevision > FunctionWithReturnType.CurrentCompilerRevision)
            {
                throw new NeoScriptPreExecutionValidationError(
                    $"Unsupported NeoScript compiler revision {compilerRevision}; this runtime supports revisions 1 through {FunctionWithReturnType.CurrentCompilerRevision}.");
            }
            ValidateControlFlowInstructionMetadata(body.instructions);
            if (compilerRevision < FunctionWithReturnType.CurrentCompilerRevision)
            {
                IrDiscriminatorVerdict verdict = IrDiscriminatorVerdictFor(
                    body.instructions);
                if (compilerRevision < verdict.minimumCompilerRevision)
                {
                    throw new NeoScriptPreExecutionValidationError(
                        $"NeoScript {verdict.minimumRevisionFeature} IR requires compiler revision {verdict.minimumCompilerRevision}; body declares revision {compilerRevision}.");
                }
            }
        }

        /// <summary>
        /// One collection-operator callback session. Immutable body/options
        /// setup and allocation tracking are retained across entries, while
        /// Execute creates fresh expression replay state for every entry.
        /// </summary>
        internal sealed class PreparedCallback : IDisposable
        {
            private readonly NeoClient client;
            private readonly FunctionWithReturnType body;
            private readonly NSGetterEvaluator.Context ctx;
            private readonly NeoScriptExecutionOptions? options;
            private bool disposed;
            private NeoScriptExecutionResult? ownerTerminal;

            internal PreparedCallback(
                NeoClient client,
                FunctionWithReturnType body,
                NSGetterEvaluator.Context ctx,
                NeoScriptExecutionOptions? options)
            {
                this.client = client;
                this.body = body;
                this.ctx = ctx;
                this.options = options;
                ctx.allocationTracker.EnterExecution();
            }

            internal NeoScriptExecutionResult Execute(NeoScriptScope scope)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(
                        nameof(PreparedCallback));
                }
                ctx.allocationTracker.ConsumeWorkUnit();
                NeoScriptExecutionResult result = ExecuteInstructions(
                    client,
                    body.instructions,
                    body.typeInfo,
                    scope,
                    ctx,
                    0,
                    null,
                    options);
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

            internal void CompleteOwner(object? returnValue)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(
                        nameof(PreparedCallback));
                }
                ownerTerminal = NeoScriptExecutionResult.Completed(
                    returned: true, returnValue);
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                ctx.allocationTracker.ExitExecution(
                    client,
                    ctx,
                    ownerTerminal);
            }
        }

        /// <summary>
        /// What one JToken pass over a compiled body found. Every stale-body
        /// revision gate reads it, and it is computed once per instruction
        /// array: a body is deserialized once and then invoked arbitrarily
        /// often — per frame, for an animation setter — so re-serializing the
        /// whole IR tree on each call would be pure allocation churn. Spec §7
        /// promises no fleet recompile, so the pre-revision-8 bodies that trip
        /// these gates are the common case, not an edge case.
        /// </summary>
        private sealed class IrDiscriminatorVerdict
        {
            internal int minimumCompilerRevision = 1;
            internal string minimumRevisionFeature = "baseline";

            internal void Require(int revision, string feature)
            {
                if (revision <= minimumCompilerRevision) return;
                minimumCompilerRevision = revision;
                minimumRevisionFeature = feature;
            }
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
            Instruction[],
            IrDiscriminatorVerdict> IrDiscriminatorVerdicts = new();

        /// <summary>
        /// How many JToken passes an instruction array has paid. Zero until
        /// the first gated execution; never more than one thereafter.
        /// </summary>
        internal static int IrDiscriminatorScanCount(Instruction[] instructions) =>
            IrDiscriminatorVerdicts.TryGetValue(instructions, out _) ? 1 : 0;

        private static IrDiscriminatorVerdict IrDiscriminatorVerdictFor(
            Instruction[]? instructions)
        {
            if (instructions is null || instructions.Length == 0)
            {
                return EmptyIrDiscriminatorVerdict;
            }
            if (IrDiscriminatorVerdicts.TryGetValue(
                    instructions,
                    out IrDiscriminatorVerdict? cached))
            {
                return cached;
            }
            var verdict = new IrDiscriminatorVerdict();
            JContainer body = (JContainer)JToken.FromObject(instructions);
            foreach (JObject node in CompilerIrObjects(body))
            {
                string? type = node["type"]?.Value<string>();
                ObserveRevisionRequirement(verdict, type, node);
            }
            // A concurrent first execution of the same body may have raced us
            // here; either verdict is the same answer, so keep whichever
            // landed first.
            return IrDiscriminatorVerdicts.GetValue(instructions, _ => verdict);
        }

        private static IEnumerable<JObject> CompilerIrObjects(JToken token)
        {
            if (token is JArray array)
            {
                foreach (JToken child in array.Children())
                {
                    foreach (JObject nested in CompilerIrObjects(child))
                    {
                        yield return nested;
                    }
                }
                yield break;
            }
            if (token is not JObject obj) yield break;
            yield return obj;
            bool isLiteralValue = obj.Property("type") is null
                && obj.Property("typeInfo") is not null
                && obj.Property("value") is not null;
            foreach (JProperty property in obj.Properties())
            {
                if (isLiteralValue && property.Name == "value") continue;
                foreach (JObject nested in CompilerIrObjects(property.Value))
                {
                    yield return nested;
                }
            }
        }

        private static readonly IrDiscriminatorVerdict EmptyIrDiscriminatorVerdict =
            new();

        private static void ObserveRevisionRequirement(
            IrDiscriminatorVerdict verdict,
            string? type,
            JObject node)
        {
            switch (type)
            {
                case InstructionKind.For:
                case InstructionKind.ForEach:
                case InstructionKind.Break:
                case InstructionKind.Continue:
                    verdict.Require(4, "loop");
                    return;
                case InstructionKind.Switch:
                    verdict.Require(5, "switch");
                    return;
                case InstructionKind.Try:
                    verdict.Require(6, "try/catch");
                    return;
                case PointerKind.CallDelegate:
                    verdict.Require(7, "delegate-call");
                    return;
                case PointerKind.CallAction:
                case InstructionKind.AddActionListener:
                case InstructionKind.RemoveActionListener:
                    verdict.Require(8, "NSAction");
                    return;
                case PointerKind.Conditional:
                    verdict.Require(12, "conditional");
                    return;
                case PointerKind.DelegateClosure:
                    verdict.Require(12, "captured-closure");
                    return;
                case PointerKind.CallFunction:
                    if (string.Equals(
                            node["missingMemberFallback"]?.Value<string>(),
                            "valueEquality",
                            StringComparison.Ordinal))
                    {
                        verdict.Require(12, "generic-Equals");
                    }
                    return;
                case FunctionKind.IndexOf:
                    verdict.Require(13, "IndexOf");
                    return;
                case FunctionKind.Count:
                    JToken? countPredicate = node["info"]?["function"];
                    if (countPredicate is not null
                        && countPredicate.Type != JTokenType.Null)
                    {
                        verdict.Require(13, "predicate-Count");
                    }
                    return;
            }
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
            var expressionState = resumeState ?? new ExpressionResumeState();
            var actionCtx = ctx.WithFunctionCallHandler(
                (pointer, currentScope, currentCtx) =>
                    EvalFunctionCall(
                        client,
                        pointer,
                        currentScope,
                        currentCtx,
                        expressionState,
                        options));
            for (int i = startIndex; i < instructions.Length; i++)
            {
                ctx.allocationTracker.ConsumeWorkUnit();
                var instruction = instructions[i];
                // A callSiteId identifies a source location, not one dynamic
                // invocation. Reset only the per-attempt occurrence counters
                // so repeated calls from a collection lambda receive stable
                // frame keys while completed results survive a replay.
                expressionState.BeginInstructionAttempt();
                switch (instruction)
                {
                    case VariableInstruction variable:
                        try
                        {
                            scope[variable.variable.id] = Eval(variable.variable.pointer, scope, actionCtx);
                        }
                        catch (NeoFunctionCallSuspended suspended)
                        {
                            return PauseAtInstruction(client, instructions, returnTypeInfo, scope, ctx, i, expressionState, suspended, options);
                        }
                        break;
                    case IfInstruction ifInstruction:
                    {
                        try
                        {
                            bool matched = false;
                            foreach (var branch in ifInstruction.branches)
                            {
                                if (EvaluateBoolean(branch.expression, scope, actionCtx))
                                {
                                    matched = true;
                                    var branchResult = ExecuteInstructions(client, branch.instructions, returnTypeInfo, scope, ctx, 0, null, options);
                                    if (branchResult.IsPaused
                                        || !branchResult.IsFallthrough)
                                    {
                                        return ThenWhenCompleted(branchResult, afterBranch =>
                                            !afterBranch.IsFallthrough
                                                ? afterBranch
                                                : ExecuteInstructions(client, instructions, returnTypeInfo, scope, ctx, i + 1, null, options));
                                    }
                                    break;
                                }
                            }
                            if (!matched && ifInstruction.elseInstructions != null)
                            {
                                var elseResult = ExecuteInstructions(client, ifInstruction.elseInstructions, returnTypeInfo, scope, ctx, 0, null, options);
                                if (elseResult.IsPaused
                                    || !elseResult.IsFallthrough)
                                {
                                    return ThenWhenCompleted(elseResult, afterElse =>
                                        !afterElse.IsFallthrough
                                            ? afterElse
                                            : ExecuteInstructions(client, instructions, returnTypeInfo, scope, ctx, i + 1, null, options));
                                }
                            }
                        }
                        catch (NeoFunctionCallSuspended suspended)
                        {
                            return PauseAtInstruction(client, instructions, returnTypeInfo, scope, ctx, i, expressionState, suspended, options);
                        }
                        break;
                    }
                    case ReturnInstruction returnInstruction:
                        try
                        {
                            object? returnValue = returnInstruction.pointer is null
                                ? null
                                : Eval(returnInstruction.pointer, scope, actionCtx);
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
                        catch (NeoFunctionCallSuspended suspended)
                        {
                            return PauseAtInstruction(client, instructions, returnTypeInfo, scope, ctx, i, expressionState, suspended, options);
                        }
                    case ThrowInstruction throwInstruction:
                        try
                        {
                            throw new NSGetterRuntimeError(
                                Eval(throwInstruction.pointer, scope, actionCtx)?.ToString() ?? "null");
                        }
                        catch (NeoFunctionCallSuspended suspended)
                        {
                            return PauseAtInstruction(client, instructions, returnTypeInfo, scope, ctx, i, expressionState, suspended, options);
                        }
                    case AssignInstruction assign:
                        try
                        {
                            var nestedSetter = ExecuteAssign(
                                client,
                                assign,
                                scope,
                                actionCtx,
                                options);
                            if (nestedSetter is not null
                                && (nestedSetter.IsPaused || nestedSetter.Returned))
                            {
                                return ThenWhenCompleted(nestedSetter, _ =>
                                    ExecuteInstructions(
                                            client,
                                            instructions,
                                            returnTypeInfo,
                                            scope,
                                            ctx,
                                            i + 1,
                                            null,
                                            options));
                            }
                        }
                        catch (NeoFunctionCallSuspended suspended)
                        {
                            return PauseAtInstruction(client, instructions, returnTypeInfo, scope, ctx, i, expressionState, suspended, options);
                        }
                        break;
                    case ActionListenerInstruction listenerInstruction:
                        try
                        {
                            ExecuteActionListener(
                                client,
                                listenerInstruction,
                                scope,
                                actionCtx);
                        }
                        catch (NeoFunctionCallSuspended suspended)
                        {
                            return PauseAtInstruction(client, instructions, returnTypeInfo, scope, ctx, i, expressionState, suspended, options);
                        }
                        break;
                    case CollectionCallInstruction collectionCall:
                        try
                        {
                            ExecuteCollectionCall(client, collectionCall, scope, actionCtx);
                        }
                        catch (NeoFunctionCallSuspended suspended)
                        {
                            return PauseAtInstruction(client, instructions, returnTypeInfo, scope, ctx, i, expressionState, suspended, options);
                        }
                        break;
                    case FunctionCallInstruction functionCall:
                        try
                        {
                            Eval(functionCall.call, scope, actionCtx);
                        }
                        catch (NeoFunctionCallSuspended suspended)
                        {
                            return PauseAtInstruction(client, instructions, returnTypeInfo, scope, ctx, i, expressionState, suspended, options);
                        }
                        break;
                    case ForInstruction forInstruction:
                    {
                        NeoScriptExecutionResult loopResult = ExecuteFor(
                            client,
                            forInstruction,
                            returnTypeInfo,
                            scope,
                            ctx,
                            options);
                        if (loopResult.IsPaused || !loopResult.IsFallthrough)
                        {
                            return ThenWhenCompleted(loopResult, afterLoop =>
                                !afterLoop.IsFallthrough
                                    ? afterLoop
                                    : ExecuteInstructions(
                                        client,
                                        instructions,
                                        returnTypeInfo,
                                        scope,
                                        ctx,
                                        i + 1,
                                        null,
                                        options));
                        }
                        break;
                    }
                    case ForEachInstruction forEachInstruction:
                    {
                        NeoScriptExecutionResult loopResult = ExecuteForEach(
                            client,
                            forEachInstruction,
                            returnTypeInfo,
                            scope,
                            ctx,
                            options);
                        if (loopResult.IsPaused || !loopResult.IsFallthrough)
                        {
                            return ThenWhenCompleted(loopResult, afterLoop =>
                                !afterLoop.IsFallthrough
                                    ? afterLoop
                                    : ExecuteInstructions(
                                        client,
                                        instructions,
                                        returnTypeInfo,
                                        scope,
                                        ctx,
                                        i + 1,
                                        null,
                                        options));
                        }
                        break;
                    }
                    case SwitchInstruction switchInstruction:
                    {
                        NeoScriptExecutionResult switchResult = ExecuteSwitch(
                            client,
                            switchInstruction,
                            returnTypeInfo,
                            scope,
                            ctx,
                            options);
                        if (switchResult.IsPaused || !switchResult.IsFallthrough)
                        {
                            return ThenWhenCompleted(switchResult, afterSwitch =>
                                !afterSwitch.IsFallthrough
                                    ? afterSwitch
                                    : ExecuteInstructions(
                                        client,
                                        instructions,
                                        returnTypeInfo,
                                        scope,
                                        ctx,
                                        i + 1,
                                        null,
                                        options));
                        }
                        break;
                    }
                    case TryInstruction tryInstruction:
                    {
                        NeoScriptExecutionResult tryResult = ExecuteTry(
                            client,
                            tryInstruction,
                            returnTypeInfo,
                            scope,
                            ctx,
                            options);
                        if (tryResult.IsPaused || !tryResult.IsFallthrough)
                        {
                            return ThenWhenCompleted(tryResult, afterTry =>
                                !afterTry.IsFallthrough
                                    ? afterTry
                                    : ExecuteInstructions(
                                        client,
                                        instructions,
                                        returnTypeInfo,
                                        scope,
                                        ctx,
                                        i + 1,
                                        null,
                                        options));
                        }
                        break;
                    }
                    case BreakInstruction:
                        return NeoScriptExecutionResult.Control(
                            NeoScriptControlTransfer.Break);
                    case ContinueInstruction:
                        return NeoScriptExecutionResult.Control(
                            NeoScriptControlTransfer.Continue);
                    default:
                        throw new NSGetterRuntimeError(
                            $"Unknown instruction kind {instruction.GetType().Name}");
                }
            }
            return NeoScriptExecutionResult.Completed(returned: false, returnValue: null);
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
            var state = new ForExecutionState(instruction, scope);
            return RunFor(client, returnTypeInfo, scope, ctx, options, state);
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
                    ? result.ObserveFailure(_ => state.RestoreBinding(scope))
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
                            BuildExpressionContext(
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
                            return PauseLoopExpression(
                                suspended,
                                state.ExpressionState,
                                options,
                                () => RunFor(
                                    client,
                                    returnTypeInfo,
                                    scope,
                                    ctx,
                                    options,
                                    state));
                        }
                        state.MoveTo(ForPhase.Condition);
                        continue;
                    }
                    case ForPhase.Condition:
                    {
                        NSGetterEvaluator.Context expressionContext =
                            BuildExpressionContext(
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
                                expressionContext,
                                "for condition");
                        }
                        catch (NeoFunctionCallSuspended suspended)
                        {
                            return PauseLoopExpression(
                                suspended,
                                state.ExpressionState,
                                options,
                                () => RunFor(
                                    client,
                                    returnTypeInfo,
                                    scope,
                                    ctx,
                                    options,
                                    state));
                        }
                        if (!shouldEnter)
                        {
                            state.RestoreBinding(scope);
                            return NeoScriptExecutionResult.Completed(
                                returned: false,
                                returnValue: null);
                        }
                        ctx.allocationTracker.ConsumeLoopIteration();
                        state.MoveTo(ForPhase.Body);
                        continue;
                    }
                    case ForPhase.Body:
                    {
                        NeoScriptScope bodyScope =
                            state.EnsureBodyScope(scope);
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
                            return ThenWhenCompleted(
                                bodyResult,
                                afterBody => ResumeForAfterBody(
                                    client,
                                    returnTypeInfo,
                                    scope,
                                    ctx,
                                    options,
                                    state,
                                    afterBody));
                        }
                        state.SynchronizeBodyScope(scope);
                        NeoScriptExecutionResult? terminal =
                            ApplyForBodyTransfer(scope, state, bodyResult);
                        if (terminal is not null) return terminal;
                        continue;
                    }
                    case ForPhase.Iterator:
                    {
                        NSGetterEvaluator.Context expressionContext =
                            BuildExpressionContext(
                                client,
                                ctx,
                                state.ExpressionState,
                                options);
                        state.ExpressionState.BeginInstructionAttempt();
                        NeoScriptExecutionResult? nestedSetter;
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
                            return PauseLoopExpression(
                                suspended,
                                state.ExpressionState,
                                options,
                                () => RunFor(
                                    client,
                                    returnTypeInfo,
                                    scope,
                                    ctx,
                                    options,
                                    state));
                        }
                        if (nestedSetter is not null && nestedSetter.IsPaused)
                        {
                            return ThenWhenCompleted(nestedSetter, _ =>
                            {
                                state.MoveTo(ForPhase.Condition);
                                return RunFor(
                                    client,
                                    returnTypeInfo,
                                    scope,
                                    ctx,
                                    options,
                                    state);
                            });
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

        private static NeoScriptExecutionResult ResumeForAfterBody(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            ForExecutionState state,
            NeoScriptExecutionResult bodyResult)
        {
            state.SynchronizeBodyScope(scope);
            NeoScriptExecutionResult? terminal =
                ApplyForBodyTransfer(scope, state, bodyResult);
            return terminal ?? RunFor(
                client,
                returnTypeInfo,
                scope,
                ctx,
                options,
                state);
        }

        private static NeoScriptExecutionResult? ApplyForBodyTransfer(
            NeoScriptScope scope,
            ForExecutionState state,
            NeoScriptExecutionResult bodyResult)
        {
            if (bodyResult.Returned || bodyResult.IsFailed)
            {
                state.RestoreBinding(scope);
                return bodyResult;
            }
            if (bodyResult.IsBreak)
            {
                state.RestoreBinding(scope);
                return NeoScriptExecutionResult.Completed(
                    returned: false,
                    returnValue: null);
            }
            state.MoveTo(ForPhase.Iterator);
            return null;
        }

        private static NeoScriptExecutionResult ExecuteForEach(
            NeoClient client,
            ForEachInstruction instruction,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options)
        {
            ValidateForEachInstructionMetadata(instruction);
            var state = new ForEachExecutionState(instruction, scope);
            return RunForEach(
                client,
                returnTypeInfo,
                scope,
                ctx,
                options,
                state);
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
                    ? result.ObserveFailure(_ => state.RestoreBinding(scope))
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
                if (state.Snapshot is null)
                {
                    NSGetterEvaluator.Context expressionContext =
                        BuildExpressionContext(
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
                        return PauseLoopExpression(
                            suspended,
                            state.ExpressionState,
                            options,
                            () => RunForEach(
                                client,
                                returnTypeInfo,
                                scope,
                                ctx,
                                options,
                                state));
                    }
                    state.Snapshot =
                        NSGetterEvaluator.SnapshotCollectionEntries(
                            collection,
                            ctx);
                }

                if (state.Index >= state.Snapshot.Length)
                {
                    state.RestoreBinding(scope);
                    return NeoScriptExecutionResult.Completed(
                        returned: false,
                        returnValue: null);
                }

                ctx.allocationTracker.ConsumeLoopIteration();
                scope[state.Instruction.binding.id] =
                    CoerceSetterValue(
                        state.Snapshot[state.Index].Resolve(ctx),
                        state.Instruction.binding.typeInfo);
                NeoScriptScope bodyScope =
                    state.EnsureBodyScope(scope);
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
                    return ThenWhenCompleted(
                        bodyResult,
                        afterBody => ResumeForEachAfterBody(
                            client,
                            returnTypeInfo,
                            scope,
                            ctx,
                            options,
                            state,
                            afterBody));
                }
                state.SynchronizeBodyScope(scope);
                NeoScriptExecutionResult? terminal =
                    ApplyForEachBodyTransfer(scope, state, bodyResult);
                if (terminal is not null) return terminal;
            }
        }

        private static NeoScriptExecutionResult ResumeForEachAfterBody(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            ForEachExecutionState state,
            NeoScriptExecutionResult bodyResult)
        {
            state.SynchronizeBodyScope(scope);
            NeoScriptExecutionResult? terminal =
                ApplyForEachBodyTransfer(scope, state, bodyResult);
            return terminal ?? RunForEach(
                client,
                returnTypeInfo,
                scope,
                ctx,
                options,
                state);
        }

        private static NeoScriptExecutionResult? ApplyForEachBodyTransfer(
            NeoScriptScope scope,
            ForEachExecutionState state,
            NeoScriptExecutionResult bodyResult)
        {
            if (bodyResult.Returned || bodyResult.IsFailed)
            {
                state.RestoreBinding(scope);
                return bodyResult;
            }
            if (bodyResult.IsBreak)
            {
                state.RestoreBinding(scope);
                return NeoScriptExecutionResult.Completed(
                    returned: false,
                    returnValue: null);
            }
            state.Index++;
            return null;
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

        private static void ValidateSwitchInstructionMetadata(
            SwitchInstruction? instruction)
        {
            if (instruction?.selector is null
                || instruction.sections is null)
            {
                throw new NeoScriptPreExecutionValidationError(
                    "NeoScript switch is missing its selector or sections; its compiled IR is stale or corrupt.");
            }
            ValidateSwitchSelectorType(instruction.selectorTypeInfo);
            var labels = new HashSet<string>(StringComparer.Ordinal);
            foreach (SwitchSection? section in instruction.sections)
            {
                if (section?.labels is null
                    || section.labels.Length == 0
                    || section.instructions is null)
                {
                    throw new NeoScriptPreExecutionValidationError(
                        "NeoScript switch contains a malformed case section; its compiled IR is stale or corrupt.");
                }
                foreach (Value label in section.labels)
                {
                    if (!labels.Add(NormalizeSwitchLabel(
                            label,
                            instruction.selectorTypeInfo)))
                    {
                        throw new NeoScriptPreExecutionValidationError(
                            "NeoScript switch contains a duplicate normalized case label; its compiled IR is stale or corrupt.");
                    }
                }
            }
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
            var state = new SwitchExecutionState(instruction);
            return RunSwitch(
                client,
                returnTypeInfo,
                scope,
                ctx,
                options,
                state);
        }

        private static NeoScriptExecutionResult RunSwitch(
            NeoClient client,
            TypeInfo returnTypeInfo,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options,
            SwitchExecutionState state)
        {
            if (!state.SelectorCompleted)
            {
                NSGetterEvaluator.Context expressionContext =
                    BuildExpressionContext(
                        client,
                        ctx,
                        state.ExpressionState,
                        options);
                state.ExpressionState.BeginInstructionAttempt();
                object? selectorValue;
                try
                {
                    selectorValue = Eval(
                        state.Instruction.selector,
                        scope,
                        expressionContext);
                }
                catch (NeoFunctionCallSuspended suspended)
                {
                    return PauseLoopExpression(
                        suspended,
                        state.ExpressionState,
                        options,
                        () => RunSwitch(
                            client,
                            returnTypeInfo,
                            scope,
                            ctx,
                            options,
                            state));
                }
                state.CompleteSelector(selectorValue);
            }

            Instruction[]? selectedInstructions = state.SelectedInstructions;
            if (selectedInstructions is null)
            {
                return NeoScriptExecutionResult.Completed(
                    returned: false,
                    returnValue: null);
            }

            NeoScriptScope sectionScope =
                state.EnsureSectionScope(scope);
            NeoScriptExecutionResult bodyResult;
            try
            {
                bodyResult = ExecuteInstructions(
                    client,
                    selectedInstructions,
                    returnTypeInfo,
                    sectionScope,
                    ctx,
                    0,
                    null,
                    options);
            }
            catch
            {
                state.SynchronizeSectionScope(scope);
                throw;
            }
            if (bodyResult.IsPaused)
            {
                return ThenWhenCompleted(bodyResult, CompleteSwitchBody)
                    .ObserveFailure(_ => state.SynchronizeSectionScope(scope));
            }
            return CompleteSwitchBody(bodyResult);

            NeoScriptExecutionResult CompleteSwitchBody(
                NeoScriptExecutionResult completedBody)
            {
                state.SynchronizeSectionScope(scope);
                return ApplySwitchBodyTransfer(completedBody);
            }
        }

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
            var state = new TryExecutionState(instruction);
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
                            BuildExpressionContext(
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
                                expressionContext,
                                "catch filter");
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
                                    state.RejectCurrentClause(scope);
                                    return RunTry(
                                        client,
                                        returnTypeInfo,
                                        scope,
                                        ctx,
                                        options,
                                        state);
                                })
                                .ObserveFailure(_ =>
                                    state.SynchronizeCatchScope(scope));
                        }
                        catch (NSGetterRuntimeError exception) when (IsAuthoredCatchableError(exception))
                        {
                            state.RejectCurrentClause(scope);
                            continue;
                        }
                        catch
                        {
                            state.SynchronizeCatchScope(scope);
                            throw;
                        }
                        if (!matched)
                        {
                            state.RejectCurrentClause(scope);
                            continue;
                        }
                        state.SelectCurrentClause();
                        continue;
                    }
                    case TryPhase.CatchBody:
                    {
                        NeoScriptScope catchScope =
                            state.EnsureCatchScope(scope);
                        NeoScriptExecutionResult catchResult;
                        try
                        {
                            catchResult = ExecuteInstructions(
                                client,
                                state.CurrentClause.instructions,
                                returnTypeInfo,
                                catchScope,
                                ctx,
                                0,
                                null,
                                options);
                        }
                        catch
                        {
                            state.SynchronizeCatchScope(scope);
                            throw;
                        }
                        if (catchResult.IsPaused)
                        {
                            return ThenWhenCompleted(
                                    catchResult,
                                    CompleteCatchBody)
                                .ObserveFailure(_ =>
                                    state.SynchronizeCatchScope(scope));
                        }
                        return CompleteCatchBody(catchResult);

                        NeoScriptExecutionResult CompleteCatchBody(
                            NeoScriptExecutionResult completed)
                        {
                            state.SynchronizeCatchScope(scope);
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
                state.SynchronizeTryScope(scope);
                state.BeginCatches(exception);
                return RunTry(
                    client,
                    returnTypeInfo,
                    scope,
                    ctx,
                    options,
                    state);
            }
            catch
            {
                state.SynchronizeTryScope(scope);
                throw;
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
                            state.SynchronizeTryScope(scope);
                            state.BeginCatches(error);
                            return RunTry(
                                client,
                                returnTypeInfo,
                                scope,
                                ctx,
                                options,
                                state);
                        }),
                        CompleteTryBody)
                    .ObserveFailure(_ => state.SynchronizeTryScope(scope));
            }
            return CompleteTryBody(bodyResult);

            NeoScriptExecutionResult CompleteTryBody(
                NeoScriptExecutionResult completed)
            {
                state.SynchronizeTryScope(scope);
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

        private static string NormalizeSwitchSelector(
            TypeInfo selectorTypeInfo,
            object? value)
        {
            ValidateSwitchSelectorType(selectorTypeInfo);
            if (value is null)
            {
                if (selectorTypeInfo.required)
                {
                    throw new NSGetterRuntimeError(
                        "NeoScript switch selector evaluated to null for a required selector type; its compiled IR is stale or corrupt.");
                }
                return "null";
            }

            switch (selectorTypeInfo.type)
            {
                case MemberKind.Int:
                    if (!TryNormalizeSwitchInteger(value, out string? integerKey))
                    {
                        throw SwitchValueTypeError("selector", selectorTypeInfo);
                    }
                    return "int:" + integerKey;
                case MemberKind.String:
                    if (value is not string text)
                    {
                        throw SwitchValueTypeError("selector", selectorTypeInfo);
                    }
                    return "string:" + text;
                case MemberKind.Bool:
                    if (value is not bool boolean)
                    {
                        throw SwitchValueTypeError("selector", selectorTypeInfo);
                    }
                    return boolean ? "bool:true" : "bool:false";
                case MemberKind.Enum:
                    if (value is not object?[] options
                        || options.Length != 1
                        || options[0] is not string optionId
                        || string.IsNullOrEmpty(optionId))
                    {
                        throw SwitchValueTypeError("selector", selectorTypeInfo);
                    }
                    return "enum:" + ((EnumTypeInfo)selectorTypeInfo).enumId
                        + ":" + optionId;
                default:
                    throw SwitchValueTypeError("selector", selectorTypeInfo);
            }
        }

        private static string NormalizeSwitchLabel(
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
                return "null";
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
                            out string? integerKey))
                    {
                        throw SwitchMetadataTypeError(selectorTypeInfo);
                    }
                    return "int:" + integerKey;
                case MemberKind.String:
                    if (token?.Type != Newtonsoft.Json.Linq.JTokenType.String)
                    {
                        throw SwitchMetadataTypeError(selectorTypeInfo);
                    }
                    return "string:" + token.ToObject<string>();
                case MemberKind.Bool:
                    if (token?.Type != Newtonsoft.Json.Linq.JTokenType.Boolean)
                    {
                        throw SwitchMetadataTypeError(selectorTypeInfo);
                    }
                    return token.ToObject<bool>() ? "bool:true" : "bool:false";
                case MemberKind.Enum:
                    if (token is not Newtonsoft.Json.Linq.JArray enumOptions
                        || enumOptions.Count != 1
                        || enumOptions[0]?.Type
                            != Newtonsoft.Json.Linq.JTokenType.String
                        || string.IsNullOrEmpty(enumOptions[0]!.ToObject<string>()))
                    {
                        throw SwitchMetadataTypeError(selectorTypeInfo);
                    }
                    return "enum:" + ((EnumTypeInfo)selectorTypeInfo).enumId
                        + ":" + enumOptions[0]!.ToObject<string>();
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
            out string? key)
        {
            key = null;
            double number;
            switch (value)
            {
                case int integer: number = integer; break;
                case long integer: number = integer; break;
                case short integer: number = integer; break;
                case double floating: number = floating; break;
                case float floating: number = floating; break;
                default: return false;
            }
            if (double.IsNaN(number)
                || double.IsInfinity(number)
                || number != Math.Truncate(number)
                || Math.Abs(number) > 9007199254740991d)
            {
                return false;
            }
            if (number == 0d) number = 0d;
            key = number.ToString(
                "R",
                System.Globalization.CultureInfo.InvariantCulture);
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

        private static NSGetterEvaluator.Context BuildExpressionContext(
            NeoClient client,
            NSGetterEvaluator.Context ctx,
            ExpressionResumeState expressionState,
            NeoScriptExecutionOptions? options)
        {
            return ctx.WithFunctionCallHandler(
                (pointer, currentScope, currentCtx) =>
                    EvalFunctionCall(
                        client,
                        pointer,
                        currentScope,
                        currentCtx,
                        expressionState,
                        options));
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

        private static NeoScriptExecutionResult? ExecuteAssign(
            NeoClient client,
            AssignInstruction instruction,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options)
        {
            object? rhs = Eval(instruction.pointer, scope, ctx);
            if (instruction.target.pointer is VariablePointer variablePointer)
            {
                if (instruction.target.writability == WritabilityKind.ReadOnly)
                {
                    throw new NSGetterRuntimeError(
                        "Cannot assign to a read-only NeoScript binding.");
                }
                if (TryGetReadOnlyBindingError(
                        scope,
                        variablePointer.variableId,
                        out string? readOnlyError))
                {
                    throw new NSGetterRuntimeError(readOnlyError!);
                }
                scope[variablePointer.variableId] = CoerceSetterValue(
                    rhs,
                    instruction.target.typeInfo);
                return null;
            }

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
            var target = ResolveTarget(client, instruction.target, scope, ctx);
            // Existing storage/local IR carries the operator-applied value in
            // `pointer`; only Setter writability needs to re-read its getter
            // because the property has no storage row of its own.
            object? assigned = rhs;
            assigned = CoerceSetterValue(assigned, instruction.target.typeInfo);
            target.Write(client, assigned, ctx);
            return null;
        }

        private static bool TryGetReadOnlyBindingError(
            NeoScriptScope scope,
            string variableId,
            out string? error)
        {
            return scope.TryGetReadOnlyError(variableId, out error);
        }

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

        private static NeoScriptScope CreateChildScope(
            NeoScriptScope parentScope)
        {
            return parentScope.CreateChild();
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
                if (matches) present = true;
                if (matches && !add) continue;
                next.listeners.Add(existing);
            }
            if (add)
            {
                if (present) return;
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
            NSGetterEvaluator.Context ctx)
        {
            object?[] args = new object?[instruction.args.Length];
            for (int i = 0; i < instruction.args.Length; i++)
            {
                args[i] = Eval(instruction.args[i], scope, ctx);
            }
            if (instruction.mutation == CollectionMutationKind.Add)
            {
                ctx.allocationTracker.ConsumeProducedCollectionEntry();
            }

            if (instruction.target.pointer is VariablePointer variablePointer)
            {
                if (!scope.TryGetValue(variablePointer.variableId, out var local))
                {
                    throw new NSGetterRuntimeError(
                        $"Variable '{variablePointer.variableId}' is not in scope");
                }
                scope[variablePointer.variableId] = MutateLocalCollection(
                    local,
                    instruction.mutation,
                    args,
                    ctx);
                return;
            }

            var target = ResolveCollectionTarget(client, instruction.target, scope, ctx);
            target.Mutate(client, instruction.mutation, args, ctx);
        }

        private static object? Eval(
            Pointer pointer,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            return NSGetterEvaluator.EvaluatePointer(pointer, scope, ctx);
        }

        private static object? EvalFunctionCall(
            NeoClient client,
            CallFunctionPointer pointer,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            ExpressionResumeState expressionState,
            NeoScriptExecutionOptions? options)
        {
            if (string.IsNullOrEmpty(pointer.callSiteId))
            {
                throw new NSGetterRuntimeError(
                    "Function call is missing its required callSiteId.");
            }
            string callSiteKey = pointer.callSiteId;
            string resumeKey = expressionState.NextInvocationKey(callSiteKey);
            if (expressionState.TryGet(resumeKey, out object? cachedValue, out Exception? cachedError))
            {
                if (cachedError is not null) throw cachedError;
                return cachedValue;
            }
            try
            {
                var receiver = NSGetterEvaluator.EvalCallReceiver(
                    pointer.receiver,
                    scope,
                    ctx);
                if (pointer.optional == true && receiver is null)
                {
                    if (!pointer.receiver.IsStatic)
                    {
                        expressionState.StoreValue(resumeKey, null);
                        return null;
                    }
                }
                var args = new object?[pointer.args.Length];
                for (int i = 0; i < pointer.args.Length; i++)
                {
                    args[i] = NSGetterEvaluator.EvaluatePointer(pointer.args[i], scope, ctx);
                }
                string? memberId = NSGetterEvaluator.ResolveFunctionMemberId(
                    pointer,
                    receiver,
                    ctx);
                if (memberId is null)
                {
                    object? fallback = NSGetterEvaluator.EvaluateMissingMemberFallback(
                        pointer,
                        receiver,
                        args);
                    expressionState.StoreValue(resumeKey, fallback);
                    return fallback;
                }
                NSGetterEvaluator.ValidateValueEqualitySignature(
                    pointer,
                    memberId,
                    ctx);
                object? value;
                if (client.TryGetMember(memberId, out NSFunctionMember? nsFunction))
                {
                    bool deferred = NeoNSFunctionRuntime.ResolveSignature(
                        client,
                        memberId).Deferred;
                    if (deferred && options?.AllowDeferredFunctionCalls != true)
                    {
                        throw new NeoDeferredFunctionRuntimeError(
                            $"NSFunction '{nsFunction!.name}' ({memberId}) deferred-mode mismatch: " +
                            "an immediate NeoScript frame called its deferred signature; " +
                            "compiled call IR is stale/corrupt.");
                    }
                    NeoScriptExecutionResult nested = NeoNSFunctionRuntime.Execute(
                        client,
                        memberId,
                        receiver,
                        args,
                        ctx,
                        options ?? NeoScriptExecutionOptions.ForImmediate(client));
                    if (nested.IsPaused)
                    {
                        if (!deferred)
                        {
                            nested.Deferred?.DisposeFromOwner(
                                "non-deferred NSFunction suspended");
                            throw new NSGetterRuntimeError(
                                $"Non-deferred NSFunction '{nsFunction.name}' suspended; its compiled IR is stale or corrupt.");
                        }
                        throw new NeoFunctionCallSuspended(
                            resumeKey,
                            memberId,
                            nested);
                    }
                    value = nested.ReturnValue;
                }
                else
                {
                    ctx.allocationTracker.ConsumeWorkUnit();
                    bool deferred = client.IsNativeFunctionDeferred(memberId);
                    if (!deferred)
                    {
                        // P65 §2.5 — filled BEFORE dispatch so the native
                        // exact-arity check stands. Deferred functions reject
                        // defaulted parameters (§1.4), so the branch below
                        // stays unfilled.
                        value = client.InvokeNativeFunction(
                            memberId,
                            receiver,
                            NSGetterEvaluator.FillNativeCallSiteArguments(
                                memberId,
                                args,
                                ctx));
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
                        var suspension = new DeferredNativeFunctionSuspension();
                        var deferredHandle = client.StartDeferredNativeFunction(
                            memberId,
                            receiver,
                            args,
                            suspension.Complete,
                            suspension.Fail,
                            suspension.MarkInvokerReturned,
                            options?.CancelContinuationOnDeferredDisposal == true
                                ? suspension.Cancel
                                : suspension.Abandon);
                        if (suspension.TryGetInlineResult(
                                out object? inlineValue,
                                out Exception? inlineError))
                        {
                            if (inlineError is not null) throw inlineError;
                            value = inlineValue;
                        }
                        else
                        {
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
                    }
                }
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

        private static bool EvaluateBoolean(
            BooleanExpression expression,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
            string subject = "If condition")
        {
            object? result = Eval(new OperationPointer
            {
                type = PointerKind.Operation,
                operation = new BooleanOperation
                {
                    type = OperationKind.Boolean,
                    expression = expression,
                },
            }, scope, ctx);
            if (result is bool b) return b;
            throw new NSGetterRuntimeError(
                $"{subject} did not evaluate to bool.");
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
                : ResolveEffectiveSetterMemberId(
                    client,
                    callGetter.memberId,
                    receiver!,
                    ctx);
            if (isStatic
                && (!client.TryGetMember(
                        effectiveMemberId,
                        out JsonMember? staticMember)
                    || !staticMember.isStatic
                    || callGetter.receiver.memberId != effectiveMemberId))
            {
                throw new NSGetterRuntimeError(
                    $"Static setter target '{effectiveMemberId}' is missing, not static, or does not match its receiver.");
            }
            if (ctx.setterCallStack.Contains(effectiveMemberId))
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

            var nestedScope = new Dictionary<string, object?>
            {
                ["__root__"] = ctx.rootValue,
                ["__value__"] = value,
            };
            if (!isStatic) nestedScope["__this__"] = receiver;
            if (ctx.contextValue is not null)
            {
                nestedScope["__context__"] = ctx.contextValue;
            }

            var nestedCtx = ctx
                .WithSetterPushed(effectiveMemberId)
                .WithThis(isStatic ? null : receiver);
            var nestedOptions = (options ?? NeoScriptExecutionOptions.ForUnity(client))
                .ForProperty(effectiveMemberId);
            return Execute(
                client,
                setter,
                nestedScope,
                nestedCtx,
                nestedOptions,
                terminal => ValidateStatementTerminal(
                    terminal,
                    "NeoScript property setter"));
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

        internal static string ResolveEffectiveSetterMemberId(
            NeoClient client,
            string staticMemberId,
            object receiver,
            NSGetterEvaluator.Context ctx)
        {
            var placement = NeoSchemaClassInheritance.FindSchemaPlacement(
                staticMemberId,
                client.classes.Values);
            if (placement is null) return staticMemberId;

            string? runtimeClassId = NSGetterEvaluator.FindRowClassIdByReference(
                receiver,
                ctx);
            if (string.IsNullOrEmpty(runtimeClassId)) return staticMemberId;

            IList<NeoSchemaClass> chain;
            try
            {
                chain = NeoSchemaClassInheritance.ResolveChain(
                    runtimeClassId!,
                    id => client.TryGetClass(id, out NeoSchemaClass? schemaClass) ? schemaClass : null);
            }
            catch (CircularInheritanceError)
            {
                return staticMemberId;
            }
            foreach (var entry in NeoSchemaClassInheritance.MergeInstanceSchema(
                chain,
                id => client.TryGetMember(id, out JsonMember? member)
                    ? member
                    : null))
            {
                if (entry.schemaKey == placement.schemaKey)
                {
                    return entry.memberId;
                }
            }
            return staticMemberId;
        }

        internal static FunctionWithReturnType? ResolveCompiledSetter(
            string memberId,
            NeoClient client)
        {
            return NeoSchemaClassInheritance.WalkExtendsMemberChain(
                memberId,
                id => client.TryGetMember(id, out JsonMember? member)
                    ? member
                    : null,
                member => member is NSPropertyMember property
                    ? property.setter
                    : null,
                requireKind: MemberKind.NSProperty);
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
                        new NeoStaticBinding(
                            client,
                            staticMember.memberId,
                            client.ResolveStaticOwnership(staticMember.memberId)),
                        target.typeInfo);
                case ReferencePointer reference:
                {
                    NeoValueOwnership ownership = TargetOwnership(client, target, scope, ctx);
                    string rowId = EnsureWritableRow(client, reference.valueId, ownership);
                    return new NeoRowWriteTarget(rowId, target.typeInfo, ownership);
                }
                case KeyOfPointer keyOfPointer:
                {
                    NeoValueOwnership ownership = TargetOwnership(client, target, scope, ctx);
                    return ResolveKeyOfTarget(client, keyOfPointer.keyOf, target.typeInfo, ownership, scope, ctx);
                }
                default:
                    throw new NSGetterRuntimeError(
                        $"Unsupported assignment target '{target.pointer.GetType().Name}'.");
            }
        }

        private static NeoResolvedCollectionTarget ResolveCollectionTarget(
            NeoClient client,
            WriteTarget target,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            NeoValueOwnership ownership = TargetOwnership(client, target, scope, ctx);
            string? rowId;
            if (target.pointer is StaticMemberPointer staticMember)
            {
                var binding = new NeoStaticBinding(
                    client,
                    staticMember.memberId,
                    ownership);
                if (binding.ValueId is null)
                {
                    object initialValue = target.typeInfo.type == MemberKind.Dictionary
                        ? new Dictionary<string, string>()
                        : System.Array.Empty<string>();
                    binding.SetValue(NeoValueWritePayload.FromValue(initialValue));
                }
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
                if (receiverRowId is not null
                    && client.TryGetValue(receiverRowId, out ObjectMemberValue? receiverRow)
                    && receiverRow!.value is not null
                    && receiverRow.value.TryGetValue(
                        ToStringKey(key, "Lookup member key"), out string? memberRowId))
                {
                    rowId = memberRowId;
                }
            }
            else
            {
                object? value = Eval(target.pointer, scope, ctx);
                rowId = FindValueId(value, ctx);
            }
            if (rowId == null)
            {
                throw new NSGetterRuntimeError("Collection mutation target is not backed by a Neo value row.");
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
                    return new NeoLookupSetWriteTarget(rowId, lookupTypeInfo, ownership);
                }
                return new NeoListWriteTarget(rowId, EntryTypeInfo(target.typeInfo), ownership);
            }
            if (row is ObjectMemberValue)
            {
                return new NeoDictionaryWriteTarget(rowId, EntryTypeInfo(target.typeInfo), ownership);
            }
            throw new NSGetterRuntimeError("Collection mutation target must be a list or dictionary.");
        }

        private static NeoResolvedWriteTarget ResolveKeyOfTarget(
            NeoClient client,
            KeyOf keyOf,
            TypeInfo targetType,
            NeoValueOwnership ownership,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            object? receiver = Eval(keyOf.pointer, scope, ctx);
            object? key = Eval(keyOf.key, scope, ctx);
            string? receiverRowId = FindValueId(receiver, ctx);
            if (receiverRowId == null)
            {
                throw new NSGetterRuntimeError("Assignment receiver is not backed by a Neo value row.");
            }
            receiverRowId = EnsureWritableRow(client, receiverRowId, ownership);
            if (!client.TryGetValue(ownership, receiverRowId, out MemberValue? row))
            {
                throw new NSGetterRuntimeError($"Missing receiver row '{receiverRowId}'.");
            }

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
                    && TryResolveClassMemberMember(client, objectRow.classId!, keyString, out JsonMember? memberMember))
                {
                    if (memberMember!.isReadOnly == true)
                    {
                        throw new NSGetterRuntimeError(
                            $"Member '{memberMember.name}' is readonly and can only be changed through its class default.");
                    }
                    return new NeoClassMemberWriteTarget(receiverRowId, keyString, memberMember!, ownership);
                }
                return new NeoDictionaryEntryWriteTarget(receiverRowId, keyString, targetType, ownership);
            }
            throw new NSGetterRuntimeError("Assignment receiver must be a list, dictionary, or class object.");
        }

        private static NeoValueOwnership TargetOwnership(
            NeoClient client,
            WriteTarget target,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            if (target.writability is null)
            {
                if (TryInferTargetOwnership(client, target.pointer, scope, ctx, out NeoValueOwnership inferred))
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
                    client, target.pointer, scope, ctx),
                _ => throw new NSGetterRuntimeError("Cannot mutate read-only dialogue action target."),
            };
        }

        private static NeoValueOwnership ResolveRuntimeTargetOwnership(
            NeoClient client,
            Pointer pointer,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx)
        {
            if (!TryResolveTargetOwnership(client, pointer, scope, ctx, out NeoValueOwnership ownership))
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
            out NeoValueOwnership ownership)
        {
            return TryResolveTargetOwnership(client, pointer, scope, ctx, out ownership)
                && ownership != NeoValueOwnership.Asset;
        }

        private static bool TryResolveTargetOwnership(
            NeoClient client,
            Pointer pointer,
            NeoScriptScope scope,
            NSGetterEvaluator.Context ctx,
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
                    Eval(keyOfPointer.keyOf.pointer, scope, ctx),
                _ => Eval(pointer, scope, ctx),
            };
            NeoValueOwnership? contextualOwnership =
                NSGetterEvaluator.FindRowOwnershipByReference(
                    resolvedTarget,
                    ctx);
            if (contextualOwnership is not null)
            {
                ownership = contextualOwnership.Value;
                return true;
            }
            string? rowId = pointer is ReferencePointer reference
                ? reference.valueId
                : FindValueId(resolvedTarget, ctx);
            return rowId is not null
                && client.TryGetValueOwnership(rowId, out ownership);
        }

        private static string EnsureWritableRow(NeoClient client, string rowId, NeoValueOwnership ownership)
        {
            // Stable-id clone-on-write: shadow the single row at its own id
            // in the target store (no path walking — the parent already
            // references this id). The row must be reachable from the target
            // writable root; otherwise it isn't mutable in that store.
            if (ownership == NeoValueOwnership.Asset
                || !client.TryGetValueOwnership(rowId, out NeoValueOwnership currentOwnership)
                || currentOwnership != ownership
                || !client.EnsureWritableShadow(ownership, rowId))
            {
                throw new NSGetterRuntimeError(
                    $"Cannot mutate value '{rowId}' because it is not {ownership.ToString().ToLowerInvariant()}-owned.");
            }
            return rowId;
        }

        private static bool TryResolveClassMemberMember(
            NeoClient client,
            string classId,
            string key,
            out JsonMember? member)
        {
            member = null;
            IList<MergedSchemaEntry> merged;
            try
            {
                merged = NeoSchemaClassInheritance.MergeInstanceSchema(
                    NeoSchemaClassInheritance.ResolveChain(
                        classId,
                        id => client.TryGetClass(id, out NeoSchemaClass? schemaClass) ? schemaClass : null),
                    id => client.TryGetMember(id, out JsonMember? member)
                        ? member
                        : null);
            }
            catch (CircularInheritanceError)
            {
                return false;
            }
            foreach (var entry in merged)
            {
                if (entry.schemaKey != key) continue;
                return client.TryGetMember(entry.memberId, out member);
            }
            return false;
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
            if (typeInfo.type != MemberKind.Class) return false;
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
                NullMember => new PrimitiveTypeInfo { type = MemberKind.Null, required = member.required },
                BoolMember => new PrimitiveTypeInfo { type = MemberKind.Bool, required = member.required },
                IntMember => new PrimitiveTypeInfo { type = MemberKind.Int, required = member.required },
                FloatMember => new PrimitiveTypeInfo { type = MemberKind.Float, required = member.required },
                StringMember => new PrimitiveTypeInfo { type = MemberKind.String, required = member.required },
                Vector2Member => new PrimitiveTypeInfo { type = MemberKind.Vector2, required = member.required },
                Vector2IntMember => new PrimitiveTypeInfo { type = MemberKind.Vector2Int, required = member.required },
                Vector3Member => new PrimitiveTypeInfo { type = MemberKind.Vector3, required = member.required },
                Vector3IntMember => new PrimitiveTypeInfo { type = MemberKind.Vector3Int, required = member.required },
                ColorMember => new PrimitiveTypeInfo { type = MemberKind.Color, required = member.required },
                DecimalMember => new PrimitiveTypeInfo { type = MemberKind.Decimal, required = member.required },
                ClassMember classMember => new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = member.required,
                    classId = classMember.classId,
                },
                EnumMember enumMember => new EnumTypeInfo
                {
                    type = MemberKind.Enum,
                    required = member.required,
                    enumId = enumMember.enumId,
                },
                _ => new PrimitiveTypeInfo { type = member.kind, required = member.required },
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
                    return new DictionaryMember
                    {
                        id = id,
                        kind = MemberKind.Dictionary,
                        entryMemberId = id,
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
            NeoClient client,
            NeoValueOwnership ownership,
            JsonMember member,
            object? value,
            string id,
            string createdAt,
            string updatedAt)
        {
            var payload = value is INeoValuePayloadProvider provider
                ? provider.ToNeoValuePayload()
                : value;
            client.SetWritablePayloadRows(ownership, payload);
            return MemberValueFactory.Create(
                member,
                payload,
                id,
                createdAt,
                updatedAt);
        }

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
                case double d: return d;
                case float f: return f;
                case int i: return i;
                case long l: return l;
                default:
                    throw new NSGetterRuntimeError($"{name} must be numeric.");
            }
        }

        private static int ToInt(object? value, string name)
        {
            var numeric = ToDouble(value, name);
            if (numeric != Math.Truncate(numeric))
            {
                throw new NSGetterRuntimeError($"{name} must be an integer.");
            }
            return (int)numeric;
        }

        private static string ToStringKey(object? value, string name)
        {
            if (value is string s) return s;
            throw new NSGetterRuntimeError($"{name} must be a string.");
        }

        private static string ResolveLookupSelectionId(
            NeoClient client,
            LookupTypeInfo lookupTypeInfo,
            object? value,
            NSGetterEvaluator.Context ctx)
        {
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
                if (!LookupCollectionContainsValueId(collectionValue, valueId!))
                {
                    throw new NSGetterRuntimeError(
                        $"Lookup selection id '{valueId}' is not present in the configured lookup collection.");
                }
                return valueId!;
            }

            string? matchedValueId = FindLookupCollectionValueByPayload(client, collectionValue, value);
            if (matchedValueId is null)
            {
                throw new NSGetterRuntimeError(
                    "Lookup set argument was not found in the configured lookup collection.");
            }
            return matchedValueId;
        }

        private static bool LookupCollectionContainsValueId(
            MemberValue collectionValue,
            string valueId)
        {
            return collectionValue switch
            {
                ArrayMemberValue array when array.value is not null =>
                    Array.IndexOf(array.value, valueId) >= 0,
                ObjectMemberValue obj when obj.value is not null =>
                    obj.value.ContainsValue(valueId),
                _ => false,
            };
        }

        private static string? FindLookupCollectionValueByPayload(
            NeoClient client,
            MemberValue collectionValue,
            object? value)
        {
            IEnumerable<string> childIds = collectionValue switch
            {
                ArrayMemberValue array when array.value is not null => array.value,
                ObjectMemberValue obj when obj.value is not null => obj.value.Values,
                _ => Array.Empty<string>(),
            };
            foreach (var childId in childIds)
            {
                if (!client.TryGetValue(childId, out MemberValue? child)) continue;
                if (JsEqual(ReadRowValue(child), value)) return childId;
            }
            return null;
        }

        private static bool JsEqual(object? a, object? b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is double da && b is double db) return da == db;
            if (a is double da2 && b is int ib) return da2 == ib;
            if (a is int ia && b is double db2) return ia == db2;
            return Equals(a, b);
        }

        private static object? MutateLocalCollection(
            object? local,
            string mutation,
            object?[] args,
            NSGetterEvaluator.Context ctx)
        {
            if (local is object?[] array)
            {
                var arrayList = new List<object?>(array);
                MutateLocalList(arrayList, mutation, args, ctx);
                return arrayList.ToArray();
            }
            if (local is List<object?> list)
            {
                MutateLocalList(list, mutation, args, ctx);
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
            string mutation,
            object?[] args,
            NSGetterEvaluator.Context ctx)
        {
            switch (mutation)
            {
                case CollectionMutationKind.Add:
                    list.Add(args[0]);
                    return;
                case CollectionMutationKind.Remove:
                    for (int i = 0; i < list.Count; i++)
                    {
                        ctx.allocationTracker.ConsumeCollectionVisit();
                        if (!JsEqual(list[i], args[0])) continue;
                        list.RemoveAt(i);
                        break;
                    }
                    return;
                case CollectionMutationKind.RemoveAt:
                    list.RemoveAt(ToInt(args[0], "RemoveAt index"));
                    return;
                case CollectionMutationKind.Clear:
                    ctx.allocationTracker.ConsumeCollectionVisit(list.Count);
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
                    ctx.allocationTracker.ConsumeCollectionVisit(dict.Count);
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

        private static void StoreWritableRow(
            NeoClient client,
            NeoValueOwnership ownership,
            MemberValue row,
            NSGetterEvaluator.Context ctx)
        {
            client.SetWritableValue(ownership, row);
            NSGetterEvaluator.RefreshCachedRowAfterWrite(row, ctx, ownership);
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
                string writableRowId = EnsureWritableRow(client, rowId, ownership);
                if (!client.TryGetValue(ownership, writableRowId, out MemberValue? existing))
                {
                    throw new NSGetterRuntimeError($"Missing target row '{writableRowId}'.");
                }
                var next = CreateValueRow(
                    client,
                    ownership,
                    MemberFromTypeInfo(typeInfo),
                    value,
                    writableRowId,
                    existing.createdAt,
                    DateTime.UtcNow.ToString("o"));
                next.classId = existing.classId;
                StoreWritableRow(client, ownership, next, ctx);
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
                if (valueId is null) return null;
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
            private readonly string parentRowId;
            private readonly string key;
            private readonly JsonMember member;
            private readonly NeoValueOwnership ownership;

            public NeoClassMemberWriteTarget(
                string parentRowId,
                string key,
                JsonMember member,
                NeoValueOwnership ownership)
            {
                this.parentRowId = parentRowId;
                this.key = key;
                this.member = member;
                this.ownership = ownership;
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
            private bool TryResolveBoundChild(
                NeoClient client,
                string resolvedParentRowId,
                ObjectMemberValue parent,
                out string boundChildId,
                out MemberValue? boundChild)
            {
                boundChildId = string.Empty;
                boundChild = null;
                if (parent.value is not null
                    && parent.value.TryGetValue(key, out string bodyChildId)
                    && client.TryGetValue(ownership, bodyChildId, out MemberValue? bodyChild))
                {
                    boundChildId = bodyChildId;
                    boundChild = bodyChild;
                    return true;
                }
                if (client.TryGetVirtualClassChildValueId(
                        resolvedParentRowId,
                        key,
                        out string? virtualChildId)
                    && client.TryGetValue(ownership, virtualChildId!, out MemberValue? virtualChild))
                {
                    boundChildId = virtualChildId!;
                    boundChild = virtualChild;
                    return true;
                }
                return false;
            }

            public override object? ReadCurrentValue(
                NeoClient client,
                NSGetterEvaluator.Context ctx)
            {
                if (!client.TryGetValue(ownership, parentRowId, out ObjectMemberValue? parent)
                    || !TryResolveBoundChild(
                        client,
                        parentRowId,
                        parent,
                        out _,
                        out MemberValue? child)
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
                string writableParentRowId = EnsureWritableRow(client, parentRowId, ownership);
                if (!client.TryGetValue(ownership, writableParentRowId, out ObjectMemberValue? parent))
                {
                    throw new NSGetterRuntimeError($"Missing parent row '{writableParentRowId}'.");
                }
                parent.value ??= new Dictionary<string, string>();
                var now = DateTime.UtcNow.ToString("o");
                // Reusing the entry's stable id below clone-on-writes it
                // (a fresh row at the same id shadows the authored default),
                // so no path pre-materialization is needed. On a P75 sparse
                // root that stable id is the deterministic virtual child id:
                // writing there materializes the one member being changed and
                // leaves the rest of the root omitted, which is exactly the
                // "every value is its own instance, changing one materializes
                // that one" contract the web already implements.
                if (TryResolveBoundChild(
                        client,
                        writableParentRowId,
                        parent,
                        out string existingId,
                        out MemberValue? existing)
                    && existing is not null)
                {
                    if (TryGetClassValueReferenceId(
                            value,
                            MemberKindInfo(member),
                            ctx,
                            out string? referenceId))
                    {
                        string importedId = ImportClassValueReference(
                            client,
                            ownership,
                            referenceId!,
                            ctx,
                            existingId);
                        if (importedId == existingId) return;
                        parent.value[key] = importedId;
                        ctx.allocationTracker.RegisterConstructedParent(
                            importedId,
                            writableParentRowId);
                        parent.updatedAt = now;
                        StoreWritableRow(client, ownership, parent, ctx);
                        client.RemoveWritableValueAndDescendantsIfUnlinked(
                            ownership, existingId, member);
                        return;
                    }
                    var next = CreateValueRow(client, ownership, member, value, existingId, existing.createdAt, now);
                    next.classId = existing.classId;
                    StoreWritableRow(client, ownership, next, ctx);
                }
                else
                {
                    if (TryGetClassValueReferenceId(
                            value,
                            MemberKindInfo(member),
                            ctx,
                            out string? referenceId))
                    {
                        parent.value[key] = ImportClassValueReference(
                            client,
                            ownership,
                            referenceId!,
                            ctx);
                        ctx.allocationTracker.RegisterConstructedParent(
                            parent.value[key],
                            writableParentRowId);
                        parent.updatedAt = now;
                        StoreWritableRow(client, ownership, parent, ctx);
                        return;
                    }
                    var childId = Guid.NewGuid().ToString();
                    var next = CreateValueRow(client, ownership, member, value, childId, now, now);
                    StoreWritableRow(client, ownership, next, ctx);
                    parent.value[key] = childId;
                }
                parent.updatedAt = now;
                StoreWritableRow(client, ownership, parent, ctx);
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
                string writableRowId = EnsureWritableRow(client, rowId, ownership);
                if (!client.TryGetValue(ownership, writableRowId, out MemberValue? row))
                {
                    throw new NSGetterRuntimeError($"Missing target row '{writableRowId}'.");
                }
                ApplyField(row, value);
                row.updatedAt = DateTime.UtcNow.ToString("o");
                StoreWritableRow(client, ownership, row, ctx);
            }

            private object? ReadField(MemberValue row)
            {
                switch (row)
                {
                    case SpriteMemberValue sprite:
                    {
                        SpriteValue? current = sprite.value;
                        if (current is null) return null;
                        RequireLegalKey(NeoAnimationLeafKind.Sprite);
                        return field == NeoAnimationLeafFields.FileIdKey
                            ? current.fileId
                            : (object)current.sliceIndex;
                    }
                    case Vector3MemberValue vector3:
                    {
                        NeoVector3Value? current = vector3.value;
                        if (current is null) return null;
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
                        if (current is null) return null;
                        RequireLegalKey(NeoAnimationLeafKind.Vector2);
                        return field == "x" ? current.x : current.y;
                    }
                    case ColorMemberValue color:
                    {
                        NeoColorValue? current = color.value;
                        if (current is null) return null;
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
                if (fieldType.type != MemberKind.Int) return kind;
                return kind switch
                {
                    NeoAnimationLeafKind.Vector2 => NeoAnimationLeafKind.Vector2Int,
                    NeoAnimationLeafKind.Vector3 => NeoAnimationLeafKind.Vector3Int,
                    _ => kind,
                };
            }

            private void RequireLegalKey(NeoAnimationLeafKind kind)
            {
                if (NeoAnimationLeafFields.IsLegalKey(kind, field)) return;
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
                if (current is not null) return current;
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
                    && numeric != Math.Truncate(numeric))
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
                if (numeric != Math.Truncate(numeric)
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
                EnsureWritableRow(client, parentRowId, ownership);
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
                        client,
                        ownership,
                        referenceId!,
                        ctx,
                        childId);
                    if (importedId == childId) return;
                    parent.value[index] = importedId;
                    ctx.allocationTracker.RegisterConstructedParent(
                        importedId,
                        parentRowId);
                    parent.updatedAt = DateTime.UtcNow.ToString("o");
                    StoreWritableRow(client, ownership, parent, ctx);
                    client.RemoveWritableValueAndDescendantsIfUnlinked(
                        ownership, childId, MemberFromTypeInfo(typeInfo));
                    return;
                }
                if (!client.TryGetValue(childId, out MemberValue? existing))
                {
                    throw new NSGetterRuntimeError($"Missing list child row '{childId}'.");
                }
                var next = CreateValueRow(
                    client,
                    ownership,
                    MemberFromTypeInfo(typeInfo),
                    value,
                    childId,
                    existing.createdAt,
                    DateTime.UtcNow.ToString("o"));
                next.classId = existing.classId;
                StoreWritableRow(client, ownership, next, ctx);
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

        private sealed class NeoListWriteTarget : NeoResolvedCollectionTarget
        {
            private readonly string rowId;
            private readonly TypeInfo entryTypeInfo;
            private readonly NeoValueOwnership ownership;

            public NeoListWriteTarget(string rowId, TypeInfo entryTypeInfo, NeoValueOwnership ownership)
            {
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
                EnsureWritableRow(client, rowId, ownership);
                if (!client.TryGetValue(rowId, out ArrayMemberValue? row))
                {
                    throw new NSGetterRuntimeError($"Missing list row '{rowId}'.");
                }
                row.value ??= Array.Empty<string>();
                var now = DateTime.UtcNow.ToString("o");
                switch (mutation)
                {
                    case CollectionMutationKind.Add:
                    {
                        if (TryGetClassValueReferenceId(
                                args[0],
                                entryTypeInfo,
                                ctx,
                                out string? referenceId))
                        {
                            var referencedNext = new string[row.value.Length + 1];
                            Array.Copy(row.value, referencedNext, row.value.Length);
                            referencedNext[row.value.Length] = ImportClassValueReference(
                                client,
                                ownership,
                                referenceId!,
                                ctx);
                            ctx.allocationTracker.RegisterConstructedParent(
                                referencedNext[row.value.Length],
                                rowId);
                            row.value = referencedNext;
                            row.updatedAt = now;
                            StoreWritableRow(client, ownership, row, ctx);
                            return;
                        }
                        var childId = Guid.NewGuid().ToString();
                        var child = CreateValueRow(
                            client,
                            ownership,
                            MemberFromTypeInfo(entryTypeInfo),
                            args[0],
                            childId,
                            now,
                            now);
                        StoreWritableRow(client, ownership, child, ctx);
                        var next = new string[row.value.Length + 1];
                        Array.Copy(row.value, next, row.value.Length);
                        next[row.value.Length] = childId;
                        row.value = next;
                        row.updatedAt = now;
                        StoreWritableRow(client, ownership, row, ctx);
                        return;
                    }
                    case CollectionMutationKind.RemoveAt:
                        RemoveAt(
                            client,
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
                            ctx.allocationTracker.ConsumeCollectionVisit();
                            if (referenceId != null && row.value[i] == referenceId)
                            {
                                RemoveAt(client, ownership, row, i, now, entryTypeInfo, ctx);
                                return;
                            }
                            if (!client.TryGetValue(row.value[i], out MemberValue? child)) continue;
                            if (!JsEqual(ReadRowValue(child), args[0])) continue;
                            RemoveAt(client, ownership, row, i, now, entryTypeInfo, ctx);
                            return;
                        }
                        return;
                    }
                    case CollectionMutationKind.Clear:
                    {
                        var removedIds = row.value;
                        ctx.allocationTracker.ConsumeCollectionVisit(
                            removedIds.Length);
                        row.value = Array.Empty<string>();
                        row.updatedAt = now;
                        StoreWritableRow(client, ownership, row, ctx);
                        foreach (var childId in removedIds)
                        {
                            client.RemoveWritableValueAndDescendantsIfUnlinked(
                                ownership, childId, MemberFromTypeInfo(entryTypeInfo));
                        }
                        return;
                    }
                    default:
                        throw new NSGetterRuntimeError($"Unsupported list mutation '{mutation}'.");
                }
            }

            private static void RemoveAt(
                NeoClient client,
                NeoValueOwnership ownership,
                ArrayMemberValue row,
                int index,
                string now,
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
                    if (i == index) continue;
                    next[j++] = row.value[i];
                }
                row.value = next;
                row.updatedAt = now;
                StoreWritableRow(client, ownership, row, ctx);
                client.RemoveWritableValueAndDescendantsIfUnlinked(
                    ownership, removedId, MemberFromTypeInfo(entryTypeInfo));
            }
        }

        private sealed class NeoLookupSetWriteTarget : NeoResolvedCollectionTarget
        {
            private readonly string rowId;
            private readonly LookupTypeInfo typeInfo;
            private readonly NeoValueOwnership ownership;

            public NeoLookupSetWriteTarget(string rowId, LookupTypeInfo typeInfo, NeoValueOwnership ownership)
            {
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
                EnsureWritableRow(client, rowId, ownership);
                if (!client.TryGetValue(rowId, out ArrayMemberValue? row))
                {
                    throw new NSGetterRuntimeError($"Missing lookup row '{rowId}'.");
                }
                row.value ??= Array.Empty<string>();
                var now = DateTime.UtcNow.ToString("o");
                switch (mutation)
                {
                    case CollectionMutationKind.Add:
                    {
                        string selectionId = ResolveLookupSelectionId(client, typeInfo, args[0], ctx);
                        foreach (string existingId in row.value)
                        {
                            ctx.allocationTracker.ConsumeCollectionVisit();
                            if (existingId == selectionId) return;
                        }
                        var next = new string[row.value.Length + 1];
                        Array.Copy(row.value, next, row.value.Length);
                        next[row.value.Length] = selectionId;
                        row.value = next;
                        row.updatedAt = now;
                        StoreWritableRow(client, ownership, row, ctx);
                        return;
                    }
                    case CollectionMutationKind.Remove:
                    {
                        string selectionId = ResolveLookupSelectionId(client, typeInfo, args[0], ctx);
                        int index = -1;
                        for (int i = 0; i < row.value.Length; i++)
                        {
                            ctx.allocationTracker.ConsumeCollectionVisit();
                            if (row.value[i] == selectionId)
                            {
                                index = i;
                                break;
                            }
                        }
                        if (index < 0) return;
                        var next = new string[row.value.Length - 1];
                        for (int i = 0, j = 0; i < row.value.Length; i++)
                        {
                            if (i == index) continue;
                            next[j++] = row.value[i];
                        }
                        row.value = next;
                        row.updatedAt = now;
                        StoreWritableRow(client, ownership, row, ctx);
                        return;
                    }
                    case CollectionMutationKind.Clear:
                        ctx.allocationTracker.ConsumeCollectionVisit(
                            row.value.Length);
                        row.value = Array.Empty<string>();
                        row.updatedAt = now;
                        StoreWritableRow(client, ownership, row, ctx);
                        return;
                    default:
                        throw new NSGetterRuntimeError($"Unsupported lookup set mutation '{mutation}'.");
                }
            }
        }

        private sealed class NeoDictionaryWriteTarget : NeoResolvedCollectionTarget
        {
            private readonly string rowId;
            private readonly TypeInfo entryTypeInfo;
            private readonly NeoValueOwnership ownership;

            public NeoDictionaryWriteTarget(string rowId, TypeInfo entryTypeInfo, NeoValueOwnership ownership)
            {
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
                EnsureWritableRow(client, rowId, ownership);
                if (!client.TryGetValue(rowId, out ObjectMemberValue? row))
                {
                    throw new NSGetterRuntimeError($"Missing dictionary row '{rowId}'.");
                }
                row.value ??= new Dictionary<string, string>();
                var now = DateTime.UtcNow.ToString("o");
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
                            client,
                            ownership,
                            referenceId!,
                            ctx,
                            existingId);
                        if (importedId == existingId) return;
                        row.value[key] = importedId;
                        ctx.allocationTracker.RegisterConstructedParent(
                            importedId,
                            rowId);
                        row.updatedAt = now;
                        StoreWritableRow(client, ownership, row, ctx);
                        client.RemoveWritableValueAndDescendantsIfUnlinked(
                            ownership, existingId, MemberFromTypeInfo(entryTypeInfo));
                        return;
                    }
                    var next = CreateValueRow(
                        client,
                        ownership,
                        MemberFromTypeInfo(entryTypeInfo),
                        value,
                        existingId,
                        existing.createdAt,
                        now);
                    next.classId = existing.classId;
                    StoreWritableRow(client, ownership, next, ctx);
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
                            client,
                            ownership,
                            referenceId!,
                            ctx);
                        ctx.allocationTracker.RegisterConstructedParent(
                            row.value[key],
                            rowId);
                        row.updatedAt = now;
                        StoreWritableRow(client, ownership, row, ctx);
                        return;
                    }
                    var childId = Guid.NewGuid().ToString();
                    var next = CreateValueRow(
                        client,
                        ownership,
                        MemberFromTypeInfo(entryTypeInfo),
                        value,
                        childId,
                        now,
                        now);
                    StoreWritableRow(client, ownership, next, ctx);
                    row.value[key] = childId;
                }
                row.updatedAt = now;
                StoreWritableRow(client, ownership, row, ctx);
            }

            private void Remove(
                NeoClient client,
                string key,
                NSGetterEvaluator.Context ctx)
            {
                EnsureWritableRow(client, rowId, ownership);
                if (!client.TryGetValue(rowId, out ObjectMemberValue? row)
                    || row.value == null
                    || !row.value.TryGetValue(key, out string removedId))
                {
                    return;
                }
                row.value.Remove(key);
                row.updatedAt = DateTime.UtcNow.ToString("o");
                StoreWritableRow(client, ownership, row, ctx);
                client.RemoveWritableValueAndDescendantsIfUnlinked(
                    ownership, removedId, MemberFromTypeInfo(entryTypeInfo));
            }

            private void Clear(
                NeoClient client,
                NSGetterEvaluator.Context ctx)
            {
                EnsureWritableRow(client, rowId, ownership);
                if (!client.TryGetValue(rowId, out ObjectMemberValue? row)
                    || row.value == null)
                {
                    return;
                }
                var removedIds = new List<string>(row.value.Values);
                ctx.allocationTracker.ConsumeCollectionVisit(removedIds.Count);
                row.value.Clear();
                row.updatedAt = DateTime.UtcNow.ToString("o");
                StoreWritableRow(client, ownership, row, ctx);
                foreach (var childId in removedIds)
                {
                    client.RemoveWritableValueAndDescendantsIfUnlinked(
                        ownership, childId, MemberFromTypeInfo(entryTypeInfo));
                }
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
            private readonly string bindingId;
            private readonly bool hadPreviousBinding;
            private readonly object? previousBinding;
            private readonly bool readOnly;
            private NeoScriptScope? bodyScope;
            private string[]? bodyParentBindingIds;
            private bool bindingRestored;

            protected LoopExecutionState(
                string bindingId,
                NeoScriptScope scope,
                bool readOnly = false)
            {
                this.bindingId = bindingId;
                hadPreviousBinding = scope.TryGetValue(
                    bindingId,
                    out previousBinding);
                this.readOnly = readOnly;
                if (readOnly)
                {
                    MarkReadOnlyBinding(
                        scope,
                        bindingId,
                        "Cannot assign to a read-only foreach iterator binding.");
                }
            }

            internal NeoScriptScope EnsureBodyScope(
                NeoScriptScope parentScope)
            {
                if (bodyScope is not null) return bodyScope;
                bodyParentBindingIds = parentScope.Keys.ToArray();
                bodyScope = CreateChildScope(parentScope);
                return bodyScope;
            }

            internal void SynchronizeBodyScope(
                NeoScriptScope parentScope)
            {
                if (bodyScope is null) return;
                foreach (string parentBindingId in bodyParentBindingIds
                    ?? Array.Empty<string>())
                {
                    parentScope[parentBindingId] = bodyScope.TryGetValue(
                        parentBindingId,
                        out object? value)
                            ? value
                            : null;
                }
                bodyScope = null;
                bodyParentBindingIds = null;
            }

            internal void RestoreBinding(NeoScriptScope scope)
            {
                if (bindingRestored) return;
                bindingRestored = true;
                SynchronizeBodyScope(scope);
                if (readOnly)
                {
                    UnmarkReadOnlyBinding(scope, bindingId);
                }
                if (hadPreviousBinding)
                {
                    scope[bindingId] = previousBinding;
                }
                else
                {
                    scope.Remove(bindingId);
                }
            }
        }

        private sealed class ForExecutionState : LoopExecutionState
        {
            internal ForExecutionState(
                ForInstruction instruction,
                NeoScriptScope scope)
                : base(instruction.initializer.id, scope)
            {
                Instruction = instruction;
                Phase = ForPhase.Initializer;
                ExpressionState = new ExpressionResumeState();
            }

            internal ForInstruction Instruction { get; }
            internal ForPhase Phase { get; private set; }
            internal ExpressionResumeState ExpressionState { get; private set; }

            internal void MoveTo(ForPhase phase)
            {
                Phase = phase;
                ExpressionState = new ExpressionResumeState();
            }
        }

        private sealed class ForEachExecutionState : LoopExecutionState
        {
            internal ForEachExecutionState(
                ForEachInstruction instruction,
                NeoScriptScope scope)
                : base(instruction.binding.id, scope, readOnly: true)
            {
                Instruction = instruction;
                ExpressionState = new ExpressionResumeState();
            }

            internal ForEachInstruction Instruction { get; }
            internal ExpressionResumeState ExpressionState { get; }
            internal NSGetterEvaluator.CollectionEntrySnapshot[]? Snapshot
            {
                get;
                set;
            }
            internal int Index { get; set; }
        }

        private sealed class TryExecutionState
        {
            private NeoScriptScope? tryScope;
            private string[]? tryParentBindingIds;
            private bool tryScopeSynchronized;
            private NeoScriptScope? catchScope;
            private string[]? catchParentBindingIds;
            private bool catchScopeSynchronized;
            private int catchIndex;
            private NSGetterRuntimeError? originalFailure;

            internal TryExecutionState(TryInstruction instruction)
            {
                Instruction = instruction ?? throw new NeoScriptPreExecutionValidationError(
                    "NeoScript try instruction is missing; its compiled IR is stale or corrupt.");
                ValidateTryInstructionMetadata(instruction);
                Phase = TryPhase.Body;
                ExpressionState = new ExpressionResumeState();
            }

            internal TryInstruction Instruction { get; }
            internal TryPhase Phase { get; private set; }
            internal ExpressionResumeState ExpressionState { get; private set; }
            internal CatchClause CurrentClause =>
                catchIndex >= 0 && catchIndex < Instruction.catches.Length
                    ? Instruction.catches[catchIndex]
                    : throw new NSGetterRuntimeError(
                        "NeoScript try/catch selected an invalid catch clause; its compiled IR is stale or corrupt.");

            internal NeoScriptScope EnsureTryScope(
                NeoScriptScope parentScope)
            {
                if (tryScope is not null) return tryScope;
                tryParentBindingIds = parentScope.Keys.ToArray();
                tryScope = CreateChildScope(parentScope);
                return tryScope;
            }

            internal void SynchronizeTryScope(
                NeoScriptScope parentScope)
            {
                if (tryScopeSynchronized || tryScope is null) return;
                tryScopeSynchronized = true;
                foreach (string bindingId in tryParentBindingIds
                    ?? Array.Empty<string>())
                {
                    parentScope[bindingId] = tryScope.TryGetValue(
                        bindingId,
                        out object? value)
                            ? value
                            : null;
                }
            }

            internal void BeginCatches(NSGetterRuntimeError failure)
            {
                originalFailure = failure;
                catchIndex = 0;
                PrepareCurrentClause();
            }

            internal NeoScriptScope EnsureCatchScope(
                NeoScriptScope parentScope)
            {
                if (catchScope is not null) return catchScope;
                CatchClause clause = CurrentClause;
                catchParentBindingIds = parentScope.Keys.ToArray();
                catchScope = CreateChildScope(parentScope);
                catchScope[clause.binding.id] = originalFailure?.Message
                    ?? throw new NSGetterRuntimeError(
                        "NeoScript catch clause is missing its original error.");
                MarkReadOnlyBinding(
                    catchScope,
                    clause.binding.id,
                    "Cannot assign to a read-only catch message binding.");
                catchScopeSynchronized = false;
                return catchScope;
            }

            internal void SynchronizeCatchScope(
                NeoScriptScope parentScope)
            {
                if (catchScopeSynchronized || catchScope is null) return;
                catchScopeSynchronized = true;
                string catchBindingId = CurrentClause.binding.id;
                foreach (string bindingId in catchParentBindingIds
                    ?? Array.Empty<string>())
                {
                    if (string.Equals(
                        bindingId,
                        catchBindingId,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }
                    parentScope[bindingId] = catchScope.TryGetValue(
                        bindingId,
                        out object? value)
                            ? value
                            : null;
                }
                UnmarkReadOnlyBinding(catchScope, catchBindingId);
            }

            internal void RejectCurrentClause(
                NeoScriptScope parentScope)
            {
                SynchronizeCatchScope(parentScope);
                catchScope = null;
                catchParentBindingIds = null;
                catchScopeSynchronized = false;
                catchIndex++;
                PrepareCurrentClause();
            }

            internal void SelectCurrentClause()
            {
                Phase = TryPhase.CatchBody;
                ExpressionState = new ExpressionResumeState();
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
                ExpressionState = new ExpressionResumeState();
                Phase = catchIndex >= Instruction.catches.Length
                    ? TryPhase.NoMatch
                    : CurrentClause.filter is null
                        ? TryPhase.CatchBody
                        : TryPhase.Filter;
            }
        }

        private sealed class SwitchExecutionState
        {
            private readonly string[][] normalizedLabels;
            private NeoScriptScope? sectionScope;
            private string[]? parentBindingIds;
            private bool sectionScopeSynchronized;

            internal SwitchExecutionState(SwitchInstruction instruction)
            {
                Instruction = instruction ?? throw new NeoScriptPreExecutionValidationError(
                    "NeoScript switch instruction is missing; its compiled IR is stale or corrupt.");
                ValidateSwitchInstructionMetadata(instruction);
                normalizedLabels = new string[instruction.sections.Length][];
                for (int i = 0; i < instruction.sections.Length; i++)
                {
                    SwitchSection section = instruction.sections[i];
                    normalizedLabels[i] = new string[section.labels.Length];
                    for (int j = 0; j < section.labels.Length; j++)
                    {
                        normalizedLabels[i][j] = NormalizeSwitchLabel(
                            section.labels[j],
                            instruction.selectorTypeInfo);
                    }
                }
                ExpressionState = new ExpressionResumeState();
            }

            internal SwitchInstruction Instruction { get; }
            internal ExpressionResumeState ExpressionState { get; }
            internal bool SelectorCompleted { get; private set; }
            internal object? SelectorValue { get; private set; }
            internal int? SelectedSectionIndex { get; private set; }
            internal bool SelectedDefault { get; private set; }
            internal Instruction[]? SelectedInstructions =>
                SelectedSectionIndex is int index
                    ? Instruction.sections[index].instructions
                    : SelectedDefault
                        ? Instruction.defaultInstructions
                        : null;

            internal NeoScriptScope EnsureSectionScope(
                NeoScriptScope parentScope)
            {
                if (sectionScope is not null) return sectionScope;
                parentBindingIds = parentScope.Keys.ToArray();
                sectionScope = CreateChildScope(parentScope);
                return sectionScope;
            }

            internal void SynchronizeSectionScope(
                NeoScriptScope parentScope)
            {
                if (sectionScopeSynchronized || sectionScope is null) return;
                sectionScopeSynchronized = true;
                foreach (string bindingId in parentBindingIds
                    ?? Array.Empty<string>())
                {
                    parentScope[bindingId] = sectionScope.TryGetValue(
                        bindingId,
                        out object? value)
                            ? value
                            : null;
                }
            }

            internal void CompleteSelector(object? value)
            {
                SelectorValue = value;
                SelectorCompleted = true;
                string selectorKey = NormalizeSwitchSelector(
                    Instruction.selectorTypeInfo,
                    value);
                for (int i = 0; i < normalizedLabels.Length; i++)
                {
                    if (Array.IndexOf(normalizedLabels[i], selectorKey) < 0)
                    {
                        continue;
                    }
                    SelectedSectionIndex = i;
                    return;
                }
                SelectedDefault = Instruction.defaultInstructions is not null;
            }
        }

        private sealed class ExpressionResumeState
        {
            private readonly Dictionary<string, CachedFunctionResult> results = new();
            private readonly Dictionary<string, int> invocationCounts = new();

            internal void BeginInstructionAttempt()
            {
                invocationCounts.Clear();
            }

            internal string NextInvocationKey(string callSiteId)
            {
                invocationCounts.TryGetValue(callSiteId, out int occurrence);
                invocationCounts[callSiteId] = occurrence + 1;
                return callSiteId + "\n" + occurrence;
            }

            internal bool TryGet(
                string callSiteId,
                out object? value,
                out Exception? error)
            {
                if (results.TryGetValue(callSiteId, out CachedFunctionResult cached))
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
                results[callSiteId] = new CachedFunctionResult(value, null);
            }

            internal void StoreError(string callSiteId, Exception error)
            {
                results[callSiteId] = new CachedFunctionResult(null, error);
            }

            private readonly struct CachedFunctionResult
            {
                internal CachedFunctionResult(object? value, Exception? error)
                {
                    Value = value;
                    Error = error;
                }

                internal object? Value { get; }
                internal Exception? Error { get; }
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

        internal string ResumeKey { get; }
        internal string MemberId { get; }
        internal NeoScriptExecutionResult Execution { get; }
    }

    internal sealed class NeoScriptExecutionOptions
    {
        private readonly NeoClient client;
        private readonly Action<string> warning;
        private readonly string? propertyMemberId;
        internal bool AllowDeferredFunctionCalls { get; }
        internal bool CancelContinuationOnDeferredDisposal { get; }

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

        internal static NeoScriptExecutionOptions ForUnity(NeoClient client)
        {
            return new NeoScriptExecutionOptions(
                client,
                UnityEngine.Debug.LogWarning,
                null,
                allowDeferredFunctionCalls: true,
                cancelContinuationOnDeferredDisposal: false);
        }

        internal static NeoScriptExecutionOptions ForDirectFunction(
            NeoClient client)
        {
            return new NeoScriptExecutionOptions(
                client,
                UnityEngine.Debug.LogWarning,
                null,
                allowDeferredFunctionCalls: true,
                cancelContinuationOnDeferredDisposal: true);
        }

        internal static NeoScriptExecutionOptions ForImmediate(NeoClient client)
        {
            return new NeoScriptExecutionOptions(
                client,
                UnityEngine.Debug.LogWarning,
                null,
                allowDeferredFunctionCalls: false,
                cancelContinuationOnDeferredDisposal: false);
        }

        internal NeoScriptExecutionOptions ForProperty(string memberId)
        {
            return new NeoScriptExecutionOptions(
                client,
                warning,
                memberId,
                AllowDeferredFunctionCalls,
                CancelContinuationOnDeferredDisposal);
        }

        internal NeoScriptExecutionOptions ForFunction(bool deferred)
        {
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
            if (propertyMemberId is null) return;
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

    internal sealed class NeoScriptExecutionResult
    {
        private readonly Func<object?, NeoScriptExecutionResult>? resume;
        private readonly DeferredNativeFunctionSuspension? suspension;
        private readonly Action<Exception>? failureObserver;
        private readonly Action<Exception>? abandonmentObserver;
        private readonly Func<Exception, NeoScriptExecutionResult?>?
            failureRecovery;

        private NeoScriptExecutionResult(
            bool isPaused,
            NeoScriptControlTransfer transfer,
            object? returnValue,
            string? suspendedMemberId,
            NeoDeferredFunctionBase? deferred,
            DeferredNativeFunctionSuspension? suspension,
            Func<object?, NeoScriptExecutionResult>? resume,
            Action<Exception>? failureObserver,
            Action<Exception>? abandonmentObserver,
            Func<Exception, NeoScriptExecutionResult?>? failureRecovery,
            Exception? failure)
        {
            IsPaused = isPaused;
            Transfer = transfer;
            ReturnValue = returnValue;
            SuspendedMemberId = suspendedMemberId;
            Deferred = deferred;
            this.suspension = suspension;
            this.resume = resume;
            this.failureObserver = failureObserver;
            this.abandonmentObserver = abandonmentObserver;
            this.failureRecovery = failureRecovery;
            Failure = failure;
        }

        internal bool IsPaused { get; }
        internal NeoScriptControlTransfer Transfer { get; }
        internal bool Returned => Transfer == NeoScriptControlTransfer.Return;
        internal bool IsBreak => Transfer == NeoScriptControlTransfer.Break;
        internal bool IsContinue => Transfer == NeoScriptControlTransfer.Continue;
        internal bool IsFallthrough =>
            Failure is null
            && Transfer == NeoScriptControlTransfer.Fallthrough;
        internal bool IsFailed => Failure is not null;
        internal Exception? Failure { get; }
        internal object? ReturnValue { get; }
        internal string? SuspendedMemberId { get; }
        internal NeoDeferredFunctionBase? Deferred { get; }

        internal static NeoScriptExecutionResult Completed(
            bool returned,
            object? returnValue)
        {
            return new NeoScriptExecutionResult(
                false,
                returned
                    ? NeoScriptControlTransfer.Return
                    : NeoScriptControlTransfer.Fallthrough,
                returnValue,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        internal static NeoScriptExecutionResult Control(
            NeoScriptControlTransfer transfer)
        {
            if (transfer == NeoScriptControlTransfer.Return)
            {
                throw new ArgumentException(
                    "Return control must carry its value through Completed.",
                    nameof(transfer));
            }
            return new NeoScriptExecutionResult(
                false,
                transfer,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        internal static NeoScriptExecutionResult Failed(Exception failure)
        {
            return new NeoScriptExecutionResult(
                false,
                NeoScriptControlTransfer.Fallthrough,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                failure ?? throw new ArgumentNullException(nameof(failure)));
        }

        internal static NeoScriptExecutionResult Paused(
            string suspendedMemberId,
            NeoDeferredFunctionBase deferred,
            DeferredNativeFunctionSuspension suspension,
            Func<object?, NeoScriptExecutionResult> resume)
        {
            return new NeoScriptExecutionResult(
                true,
                NeoScriptControlTransfer.Fallthrough,
                null,
                suspendedMemberId,
                deferred,
                suspension,
                resume,
                null,
                null,
                null,
                null);
        }

        internal void WhenDeferredSettled(
            Action<NeoScriptExecutionResult> complete,
            Action<Exception> fail)
        {
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
                            complete(recovered);
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
            if (IsFailed) return this;
            if (!IsPaused) return next(this);
            if (Deferred == null || suspension == null || resume == null)
            {
                throw new InvalidOperationException(
                    "Paused action result is missing deferred continuation state.");
            }
            return new NeoScriptExecutionResult(
                true,
                NeoScriptControlTransfer.Fallthrough,
                null,
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
                    },
                null);
        }

        internal NeoScriptExecutionResult ObserveFailure(
            Action<Exception> observer)
        {
            if (!IsPaused) return this;
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
            return new NeoScriptExecutionResult(
                true,
                NeoScriptControlTransfer.Fallthrough,
                null,
                SuspendedMemberId,
                Deferred,
                suspension,
                resume,
                combinedFailure,
                combinedAbandonment,
                failureRecovery,
                null);
        }

        internal NeoScriptExecutionResult RecoverFailure(
            Func<Exception, NeoScriptExecutionResult?> recovery)
        {
            if (!IsPaused) return this;
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
                            if (recovered.IsFailed)
                            {
                                return recovery(recovered.Failure!)
                                    ?? recovered;
                            }
                            return recovered.IsPaused
                                ? recovered.RecoverFailure(recovery)
                                : recovered;
                        }
                    }
                    failureObserver?.Invoke(exception);
                    return recovery(exception);
                };
            return new NeoScriptExecutionResult(
                true,
                NeoScriptControlTransfer.Fallthrough,
                null,
                SuspendedMemberId,
                Deferred,
                suspension,
                value => resume(value).RecoverFailure(recovery),
                null,
                abandonmentObserver,
                combinedRecovery,
                null);
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
                if (completed || failed || abandoned) return;
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
