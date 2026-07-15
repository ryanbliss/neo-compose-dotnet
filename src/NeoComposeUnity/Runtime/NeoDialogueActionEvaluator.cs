// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using JsonAttribute = NeoCompose.Runtime.Json.Attribute;

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
        internal static NeoScriptExecutionResult Execute(
            NeoClient client,
            FunctionWithReturnType body,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options = null,
            Func<NeoScriptExecutionResult, NeoScriptExecutionResult>?
                normalizeTerminal = null)
        {
            bool allocationScopeClosed = false;
            ctx.allocationTracker.EnterExecution();
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
                if (result.IsPaused)
                {
                    return result
                        .Then(ExitAllocationScopeWhenTerminal)
                        .ObserveFailure(_ => CloseAllocationScope(null));
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

        private static NeoScriptExecutionResult ValidateTerminalAgainstBody(
            FunctionWithReturnType body,
            NeoScriptExecutionResult execution)
        {
            TypeInfo returnType = body.typeInfo
                ?? throw new NSGetterRuntimeError(
                    "NeoScript body is missing its compiled return type.");
            if (returnType is VoidTypeInfo
                || returnType.type == AttributeType.Void
                // Existing action/setter IR uses Null as its statement-body
                // result marker. Preserve fallthrough for that wire shape;
                // NSGetterEvaluator still enforces an explicit return at the
                // getter boundary after allocation cleanup.
                || returnType.type == AttributeType.Null)
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
            // internal representation for a declared Custom/List/Dictionary
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
            Dictionary<string, object?> scope,
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
                                    if (branchResult.IsPaused || branchResult.Returned)
                                    {
                                        return ThenWhenCompleted(branchResult, afterBranch =>
                                            afterBranch.Returned
                                                ? afterBranch
                                                : ExecuteInstructions(client, instructions, returnTypeInfo, scope, ctx, i + 1, null, options));
                                    }
                                    break;
                                }
                            }
                            if (!matched && ifInstruction.elseInstructions != null)
                            {
                                var elseResult = ExecuteInstructions(client, ifInstruction.elseInstructions, returnTypeInfo, scope, ctx, 0, null, options);
                                if (elseResult.IsPaused || elseResult.Returned)
                                {
                                    return ThenWhenCompleted(elseResult, afterElse =>
                                        afterElse.Returned
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
                            if (returnTypeInfo.type == AttributeType.Decimal
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
                    default:
                        throw new NSGetterRuntimeError(
                            $"Unknown instruction kind {instruction.GetType().Name}");
                }
            }
            return NeoScriptExecutionResult.Completed(returned: false, returnValue: null);
        }

        private static NeoScriptExecutionResult PauseAtInstruction(
            NeoClient client,
            Instruction[] instructions,
            TypeInfo returnTypeInfo,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx,
            int instructionIndex,
            ExpressionResumeState expressionState,
            NeoFunctionCallSuspended suspended,
            NeoScriptExecutionOptions? options)
        {
            options?.WarnDeferred(suspended.AttributeId);
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
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions? options)
        {
            object? rhs = Eval(instruction.pointer, scope, ctx);
            if (instruction.target.pointer is VariablePointer variablePointer)
            {
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

        private static void ExecuteCollectionCall(
            NeoClient client,
            CollectionCallInstruction instruction,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx)
        {
            object?[] args = new object?[instruction.args.Length];
            for (int i = 0; i < instruction.args.Length; i++)
            {
                args[i] = Eval(instruction.args[i], scope, ctx);
            }

            if (instruction.target.pointer is VariablePointer variablePointer)
            {
                if (!scope.TryGetValue(variablePointer.variableId, out var local))
                {
                    throw new NSGetterRuntimeError(
                        $"Variable '{variablePointer.variableId}' is not in scope");
                }
                MutateLocalCollection(local, instruction.mutation, args);
                scope[variablePointer.variableId] = local;
                return;
            }

            var target = ResolveCollectionTarget(client, instruction.target, scope, ctx);
            target.Mutate(client, instruction.mutation, args, ctx);
        }

        private static object? Eval(
            Pointer pointer,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx)
        {
            return NSGetterEvaluator.EvaluatePointer(pointer, scope, ctx);
        }

        private static object? EvalFunctionCall(
            NeoClient client,
            CallFunctionPointer pointer,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx,
            ExpressionResumeState expressionState,
            NeoScriptExecutionOptions? options)
        {
            if (string.IsNullOrEmpty(pointer.callSiteId))
            {
                throw new NSGetterRuntimeError(
                    "Function call is missing its schema-6 callSiteId.");
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
                string attributeId = NSGetterEvaluator.ResolveFunctionAttributeId(
                    pointer,
                    receiver,
                    ctx);
                object? value;
                if (client.TryGetAttribute(attributeId, out NSFunctionAttribute? nsFunction))
                {
                    bool deferred = NeoNSFunctionRuntime.ResolveSignature(
                        client,
                        attributeId).Deferred;
                    if (deferred && options?.AllowDeferredFunctionCalls != true)
                    {
                        throw new NeoDeferredFunctionRuntimeError(
                            $"NSFunction '{nsFunction!.name}' ({attributeId}) deferred-mode mismatch: " +
                            "an immediate NeoScript frame called its deferred signature; " +
                            "compiled call IR is stale/corrupt.");
                    }
                    NeoScriptExecutionResult nested = NeoNSFunctionRuntime.Execute(
                        client,
                        attributeId,
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
                            attributeId,
                            nested);
                    }
                    value = nested.ReturnValue;
                }
                else
                {
                    bool deferred = client.IsNativeFunctionDeferred(attributeId);
                    if (!deferred)
                    {
                        value = client.InvokeNativeFunction(attributeId, receiver, args);
                    }
                    else
                    {
                        if (options?.AllowDeferredFunctionCalls != true)
                        {
                            string functionName = client.TryGetAttribute(
                                attributeId, out JsonAttribute? deferredAttribute)
                                    ? deferredAttribute.name
                                    : attributeId;
                            throw new NeoDeferredFunctionRuntimeError(
                                $"Function '{functionName}' ({attributeId}) deferred-mode mismatch: " +
                                "an immediate NeoScript frame called its deferred signature; " +
                                "compiled call IR is stale/corrupt.");
                        }
                        var suspension = new DeferredNativeFunctionSuspension();
                        var deferredHandle = client.StartDeferredNativeFunction(
                            attributeId,
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
                                attributeId,
                                NeoScriptExecutionResult.Paused(
                                    attributeId,
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
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx)
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
            throw new NSGetterRuntimeError("If condition did not evaluate to bool.");
        }

        private static NeoScriptExecutionResult ExecuteSetterAssignment(
            NeoClient client,
            AssignInstruction instruction,
            object? rhs,
            Dictionary<string, object?> scope,
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

            string effectiveAttributeId = isStatic
                ? callGetter.attributeId
                : ResolveEffectiveSetterAttributeId(
                    client,
                    callGetter.attributeId,
                    receiver!,
                    ctx);
            if (isStatic
                && (!client.TryGetAttribute(
                        effectiveAttributeId,
                        out JsonAttribute? staticAttribute)
                    || !staticAttribute.isStatic
                    || callGetter.receiver.attributeId != effectiveAttributeId))
            {
                throw new NSGetterRuntimeError(
                    $"Static setter target '{effectiveAttributeId}' is missing, not static, or does not match its receiver.");
            }
            if (ctx.setterCallStack.Contains(effectiveAttributeId))
            {
                string circularName = client.TryGetAttribute(
                    effectiveAttributeId, out JsonAttribute? circularAttribute)
                        ? circularAttribute.name
                        : effectiveAttributeId;
                throw new NSGetterRuntimeError(
                    $"Circular setter call: '{circularName}'.");
            }

            FunctionWithReturnType? setter = ResolveCompiledSetter(
                effectiveAttributeId,
                client);
            if (setter is null)
            {
                string missingName = client.TryGetAttribute(
                    effectiveAttributeId, out JsonAttribute? missingAttribute)
                        ? missingAttribute.name
                        : effectiveAttributeId;
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
                .WithSetterPushed(effectiveAttributeId)
                .WithThis(isStatic ? null : receiver);
            var nestedOptions = (options ?? NeoScriptExecutionOptions.ForUnity(client))
                .ForProperty(effectiveAttributeId);
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
            if (typeInfo.type == AttributeType.Decimal
                && value is double or float or int or long or short or decimal)
            {
                return NSGetterEvaluator.CoerceDecimalOperand(value, "setter value");
            }
            return value;
        }

        internal static string ResolveEffectiveSetterAttributeId(
            NeoClient client,
            string staticAttributeId,
            object receiver,
            NSGetterEvaluator.Context ctx)
        {
            var placement = CustomTypeInheritance.FindSchemaPlacement(
                staticAttributeId,
                client.types.Values);
            if (placement is null) return staticAttributeId;

            string? runtimeTypeId = NSGetterEvaluator.FindRowTypeIdByReference(
                receiver,
                ctx);
            if (string.IsNullOrEmpty(runtimeTypeId)) return staticAttributeId;

            IList<CustomType> chain;
            try
            {
                chain = CustomTypeInheritance.ResolveChain(
                    runtimeTypeId!,
                    id => client.TryGetType(id, out CustomType? type) ? type : null);
            }
            catch (CircularInheritanceError)
            {
                return staticAttributeId;
            }
            foreach (var entry in CustomTypeInheritance.MergeInstanceSchema(
                chain,
                id => client.TryGetAttribute(id, out JsonAttribute? attribute)
                    ? attribute
                    : null))
            {
                if (entry.schemaKey == placement.schemaKey)
                {
                    return entry.attributeId;
                }
            }
            return staticAttributeId;
        }

        internal static FunctionWithReturnType? ResolveCompiledSetter(
            string attributeId,
            NeoClient client)
        {
            return CustomTypeInheritance.WalkExtendsAttributeChain(
                attributeId,
                id => client.TryGetAttribute(id, out JsonAttribute? attribute)
                    ? attribute
                    : null,
                attribute => attribute is NSPropertyAttribute property
                    ? property.setter
                    : null,
                requireType: AttributeType.NSProperty);
        }

        private static NeoResolvedWriteTarget ResolveTarget(
            NeoClient client,
            WriteTarget target,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx)
        {
            switch (target.pointer)
            {
                case StaticMemberPointer staticMember:
                    return new NeoStaticMemberWriteTarget(
                        new NeoStaticBinding(
                            client,
                            staticMember.attributeId,
                            client.ResolveStaticOwnership(staticMember.attributeId)),
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
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx)
        {
            NeoValueOwnership ownership = TargetOwnership(client, target, scope, ctx);
            string? rowId;
            if (target.pointer is StaticMemberPointer staticMember)
            {
                var binding = new NeoStaticBinding(
                    client,
                    staticMember.attributeId,
                    ownership);
                if (binding.ValueId is null)
                {
                    object initialValue = target.typeInfo.type == AttributeType.Dictionary
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
                    && client.TryGetValue(receiverRowId, out ObjectAttributeValue? receiverRow)
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
            if (!client.TryGetValue(ownership, rowId, out AttributeValue? row))
            {
                throw new NSGetterRuntimeError($"Missing collection row '{rowId}'.");
            }
            if (row is ArrayAttributeValue)
            {
                if (target.typeInfo is LookupTypeInfo lookupTypeInfo)
                {
                    return new NeoLookupSetWriteTarget(rowId, lookupTypeInfo, ownership);
                }
                return new NeoListWriteTarget(rowId, EntryTypeInfo(target.typeInfo), ownership);
            }
            if (row is ObjectAttributeValue)
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
            Dictionary<string, object?> scope,
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
            if (!client.TryGetValue(ownership, receiverRowId, out AttributeValue? row))
            {
                throw new NSGetterRuntimeError($"Missing receiver row '{receiverRowId}'.");
            }

            if (row is ArrayAttributeValue)
            {
                if (key is string)
                {
                    throw new NSGetterRuntimeError(
                        "Assignment through a List value-id index is read-only; mutate the returned entry or use a positional index.");
                }
                return new NeoListIndexWriteTarget(receiverRowId, ToInt(key, "List assignment index"), targetType, ownership);
            }
            if (row is ObjectAttributeValue objectRow)
            {
                string keyString = ToStringKey(key, "Dictionary/custom assignment key");
                if (!string.IsNullOrEmpty(objectRow.typeId)
                    && TryResolveCustomMemberAttribute(client, objectRow.typeId!, keyString, out JsonAttribute? memberAttribute))
                {
                    return new NeoCustomMemberWriteTarget(receiverRowId, keyString, memberAttribute!, ownership);
                }
                return new NeoDictionaryEntryWriteTarget(receiverRowId, keyString, targetType, ownership);
            }
            throw new NSGetterRuntimeError("Assignment receiver must be a list, dictionary, or custom object.");
        }

        private static NeoValueOwnership TargetOwnership(
            NeoClient client,
            WriteTarget target,
            Dictionary<string, object?> scope,
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
            Dictionary<string, object?> scope,
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
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx,
            out NeoValueOwnership ownership)
        {
            return TryResolveTargetOwnership(client, pointer, scope, ctx, out ownership)
                && ownership != NeoValueOwnership.Asset;
        }

        private static bool TryResolveTargetOwnership(
            NeoClient client,
            Pointer pointer,
            Dictionary<string, object?> scope,
            NSGetterEvaluator.Context ctx,
            out NeoValueOwnership ownership)
        {
            ownership = NeoValueOwnership.Asset;
            if (pointer is StaticMemberPointer staticMember)
            {
                ownership = client.ResolveStaticOwnership(staticMember.attributeId);
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

        private static bool TryResolveCustomMemberAttribute(
            NeoClient client,
            string customTypeId,
            string key,
            out JsonAttribute? attribute)
        {
            attribute = null;
            IList<MergedSchemaEntry> merged;
            try
            {
                merged = CustomTypeInheritance.MergeInstanceSchema(
                    CustomTypeInheritance.ResolveChain(
                        customTypeId,
                        id => client.TryGetType(id, out CustomType? type) ? type : null),
                    id => client.TryGetAttribute(id, out JsonAttribute? attribute)
                        ? attribute
                        : null);
            }
            catch (CircularInheritanceError)
            {
                return false;
            }
            foreach (var entry in merged)
            {
                if (entry.schemaKey != key) continue;
                return client.TryGetAttribute(entry.attributeId, out attribute);
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

        private static bool TryGetCustomValueReferenceId(
            object? value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx,
            out string? valueId)
        {
            valueId = null;
            if (typeInfo.type != AttributeType.Custom) return false;
            if (value is INeoValueReference reference
                && !string.IsNullOrEmpty(reference.valueId))
            {
                valueId = reference.valueId;
                return true;
            }
            valueId = FindValueId(value, ctx);
            return !string.IsNullOrEmpty(valueId);
        }

        private static string ImportCustomValueReference(
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

        private static TypeInfo AttributeTypeInfo(JsonAttribute attribute)
        {
            return attribute switch
            {
                NullAttribute => new PrimitiveTypeInfo { type = AttributeType.Null, required = attribute.required },
                BoolAttribute => new PrimitiveTypeInfo { type = AttributeType.Bool, required = attribute.required },
                IntAttribute => new PrimitiveTypeInfo { type = AttributeType.Int, required = attribute.required },
                FloatAttribute => new PrimitiveTypeInfo { type = AttributeType.Float, required = attribute.required },
                StringAttribute => new PrimitiveTypeInfo { type = AttributeType.String, required = attribute.required },
                Vector2Attribute => new PrimitiveTypeInfo { type = AttributeType.Vector2, required = attribute.required },
                Vector2IntAttribute => new PrimitiveTypeInfo { type = AttributeType.Vector2Int, required = attribute.required },
                Vector3Attribute => new PrimitiveTypeInfo { type = AttributeType.Vector3, required = attribute.required },
                Vector3IntAttribute => new PrimitiveTypeInfo { type = AttributeType.Vector3Int, required = attribute.required },
                ColorAttribute => new PrimitiveTypeInfo { type = AttributeType.Color, required = attribute.required },
                DecimalAttribute => new PrimitiveTypeInfo { type = AttributeType.Decimal, required = attribute.required },
                CustomAttribute custom => new CustomTypeInfo
                {
                    type = AttributeType.Custom,
                    required = attribute.required,
                    typeId = custom.customTypeId,
                },
                EnumAttribute enumAttribute => new EnumTypeInfo
                {
                    type = AttributeType.Enum,
                    required = attribute.required,
                    enumId = enumAttribute.enumId,
                },
                _ => new PrimitiveTypeInfo { type = attribute.type, required = attribute.required },
            };
        }

        private static JsonAttribute AttributeFromTypeInfo(TypeInfo typeInfo)
        {
            var id = "__neo_dialogue_action_value";
            switch (typeInfo.type)
            {
                case AttributeType.Null:
                    return new NullAttribute { id = id, type = AttributeType.Null };
                case AttributeType.Bool:
                    return new BoolAttribute { id = id, type = AttributeType.Bool };
                case AttributeType.Int:
                    return new IntAttribute { id = id, type = AttributeType.Int };
                case AttributeType.Float:
                    return new FloatAttribute { id = id, type = AttributeType.Float };
                case AttributeType.String:
                    return new StringAttribute { id = id, type = AttributeType.String };
                case AttributeType.Vector2:
                    return new Vector2Attribute { id = id, type = AttributeType.Vector2 };
                case AttributeType.Vector2Int:
                    return new Vector2IntAttribute { id = id, type = AttributeType.Vector2Int };
                case AttributeType.Vector3:
                    return new Vector3Attribute { id = id, type = AttributeType.Vector3 };
                case AttributeType.Vector3Int:
                    return new Vector3IntAttribute { id = id, type = AttributeType.Vector3Int };
                case AttributeType.Color:
                    return new ColorAttribute { id = id, type = AttributeType.Color };
                case AttributeType.Decimal:
                    // A Decimal write flows through AttributeValueFactory as
                    // a DecimalAttribute → StringAttributeValue row
                    // (specs/decimal-attribute.md decision 5); the payload is
                    // the canonical decimal string the evaluator produced.
                    return new DecimalAttribute { id = id, type = AttributeType.Decimal };
                case AttributeType.Custom:
                    return new CustomAttribute
                    {
                        id = id,
                        type = AttributeType.Custom,
                        customTypeId = ((CustomTypeInfo)typeInfo).typeId,
                    };
                case AttributeType.List:
                    return new ListAttribute
                    {
                        id = id,
                        type = AttributeType.List,
                        entryAttributeId = id,
                    };
                case AttributeType.Dictionary:
                    return new DictionaryAttribute
                    {
                        id = id,
                        type = AttributeType.Dictionary,
                        entryAttributeId = id,
                    };
                case AttributeType.Enum:
                    return new EnumAttribute
                    {
                        id = id,
                        type = AttributeType.Enum,
                        enumId = ((EnumTypeInfo)typeInfo).enumId,
                    };
                case AttributeType.Lookup:
                    return new LookupAttribute
                    {
                        id = id,
                        type = AttributeType.Lookup,
                        collectionAttributeId = id,
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

        private static AttributeValue CreateValueRow(
            NeoClient client,
            NeoValueOwnership ownership,
            JsonAttribute attribute,
            object? value,
            string id,
            string createdAt,
            string updatedAt)
        {
            var payload = value is INeoValuePayloadProvider provider
                ? provider.ToNeoValuePayload()
                : value;
            client.SetWritablePayloadRows(ownership, payload);
            return AttributeValueFactory.Create(
                attribute,
                payload,
                id,
                createdAt,
                updatedAt);
        }

        private static object? ReadRowValue(AttributeValue row)
        {
            return row switch
            {
                BoolAttributeValue b => b.value,
                NumberAttributeValue n => n.value,
                StringAttributeValue s => s.value,
                ArrayAttributeValue a => a.value,
                ObjectAttributeValue o => o.value,
                Vector2AttributeValue v => v.value,
                Vector3AttributeValue v => v.value,
                ColorAttributeValue c => c.value,
                NullAttributeValue => null,
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
            if (!client.TryGetAttribute(lookupTypeInfo.collectionAttributeId, out JsonAttribute? collectionAttribute))
            {
                throw new NSGetterRuntimeError(
                    $"Lookup collection attribute '{lookupTypeInfo.collectionAttributeId}' was not found.");
            }
            string? collectionValueId = client.TryResolveLookupCollectionValueId(
                collectionAttribute.id,
                lookupTypeInfo.collectionValueId,
                out string? resolvedCollectionValueId)
                    ? resolvedCollectionValueId
                    : null;
            if (collectionValueId is null || !client.TryGetValue(collectionValueId, out AttributeValue? collectionValue))
            {
                throw new NSGetterRuntimeError(
                    $"Lookup collection value '{collectionValueId ?? "<null>"}' was not found.");
            }

            if (lookupTypeInfo.entryTypeInfo.type == AttributeType.Custom)
            {
                string? valueId = value is string id
                    ? id
                    : FindValueId(value, ctx);
                if (string.IsNullOrWhiteSpace(valueId))
                {
                    throw new NSGetterRuntimeError(
                        "Lookup set custom argument must be a selected value id or generated custom value.");
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
            AttributeValue collectionValue,
            string valueId)
        {
            return collectionValue switch
            {
                ArrayAttributeValue array when array.value is not null =>
                    Array.IndexOf(array.value, valueId) >= 0,
                ObjectAttributeValue obj when obj.value is not null =>
                    obj.value.ContainsValue(valueId),
                _ => false,
            };
        }

        private static string? FindLookupCollectionValueByPayload(
            NeoClient client,
            AttributeValue collectionValue,
            object? value)
        {
            IEnumerable<string> childIds = collectionValue switch
            {
                ArrayAttributeValue array when array.value is not null => array.value,
                ObjectAttributeValue obj when obj.value is not null => obj.value.Values,
                _ => Array.Empty<string>(),
            };
            foreach (var childId in childIds)
            {
                if (!client.TryGetValue(childId, out AttributeValue? child)) continue;
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

        private static void MutateLocalCollection(
            object? local,
            string mutation,
            object?[] args)
        {
            if (local is object?[] array)
            {
                var arrayList = new List<object?>(array);
                MutateLocalList(arrayList, mutation, args);
                return;
            }
            if (local is List<object?> list)
            {
                MutateLocalList(list, mutation, args);
                return;
            }
            if (local is IDictionary<string, object?> dict)
            {
                MutateLocalDictionary(dict, mutation, args);
                return;
            }
            throw new NSGetterRuntimeError("Collection mutation target must be a list or dictionary.");
        }

        private static void MutateLocalList(
            List<object?> list,
            string mutation,
            object?[] args)
        {
            switch (mutation)
            {
                case CollectionMutationKind.Add:
                    list.Add(args[0]);
                    return;
                case CollectionMutationKind.Remove:
                    list.RemoveAll(item => JsEqual(item, args[0]));
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
            object?[] args)
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

        private static void StoreWritableRow(
            NeoClient client,
            NeoValueOwnership ownership,
            AttributeValue row,
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
                if (!client.TryGetValue(ownership, writableRowId, out AttributeValue? row))
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
                if (!client.TryGetValue(ownership, writableRowId, out AttributeValue? existing))
                {
                    throw new NSGetterRuntimeError($"Missing target row '{writableRowId}'.");
                }
                var next = CreateValueRow(
                    client,
                    ownership,
                    AttributeFromTypeInfo(typeInfo),
                    value,
                    writableRowId,
                    existing.createdAt,
                    DateTime.UtcNow.ToString("o"));
                next.typeId = existing.typeId;
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
                        out AttributeValue? row))
                {
                    throw new NSGetterRuntimeError(
                        $"Static member '{binding.AttributeId}' is bound to missing value '{valueId}'.");
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
                    if (typeInfo.type == AttributeType.Custom)
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
                            out AttributeValue? updatedRow))
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

        private sealed class NeoCustomMemberWriteTarget : NeoResolvedWriteTarget
        {
            private readonly string parentRowId;
            private readonly string key;
            private readonly JsonAttribute attribute;
            private readonly NeoValueOwnership ownership;

            public NeoCustomMemberWriteTarget(
                string parentRowId,
                string key,
                JsonAttribute attribute,
                NeoValueOwnership ownership)
            {
                this.parentRowId = parentRowId;
                this.key = key;
                this.attribute = attribute;
                this.ownership = ownership;
            }

            public override object? ReadCurrentValue(
                NeoClient client,
                NSGetterEvaluator.Context ctx)
            {
                if (!client.TryGetValue(ownership, parentRowId, out ObjectAttributeValue? parent)
                    || parent.value == null
                    || !parent.value.TryGetValue(key, out string childId)
                    || !client.TryGetValue(ownership, childId, out AttributeValue? child))
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
                if (!client.TryGetValue(ownership, writableParentRowId, out ObjectAttributeValue? parent))
                {
                    throw new NSGetterRuntimeError($"Missing parent row '{writableParentRowId}'.");
                }
                parent.value ??= new Dictionary<string, string>();
                var now = DateTime.UtcNow.ToString("o");
                // Reusing the entry's stable id below clone-on-writes it
                // (a fresh row at the same id shadows the authored default),
                // so no path pre-materialization is needed.
                if (parent.value.TryGetValue(key, out string existingId)
                    && client.TryGetValue(ownership, existingId, out AttributeValue? existing))
                {
                    if (TryGetCustomValueReferenceId(
                            value,
                            AttributeTypeInfo(attribute),
                            ctx,
                            out string? referenceId))
                    {
                        string importedId = ImportCustomValueReference(
                            client,
                            ownership,
                            referenceId!,
                            ctx,
                            existingId);
                        if (importedId == existingId) return;
                        parent.value[key] = importedId;
                        parent.updatedAt = now;
                        StoreWritableRow(client, ownership, parent, ctx);
                        client.RemoveWritableValueAndDescendantsIfUnlinked(
                            ownership, existingId, attribute);
                        return;
                    }
                    var next = CreateValueRow(client, ownership, attribute, value, existingId, existing.createdAt, now);
                    next.typeId = existing.typeId;
                    StoreWritableRow(client, ownership, next, ctx);
                }
                else
                {
                    if (TryGetCustomValueReferenceId(
                            value,
                            AttributeTypeInfo(attribute),
                            ctx,
                            out string? referenceId))
                    {
                        parent.value[key] = ImportCustomValueReference(
                            client,
                            ownership,
                            referenceId!,
                            ctx);
                        parent.updatedAt = now;
                        StoreWritableRow(client, ownership, parent, ctx);
                        return;
                    }
                    var childId = Guid.NewGuid().ToString();
                    var next = CreateValueRow(client, ownership, attribute, value, childId, now, now);
                    StoreWritableRow(client, ownership, next, ctx);
                    parent.value[key] = childId;
                }
                parent.updatedAt = now;
                StoreWritableRow(client, ownership, parent, ctx);
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
                if (!client.TryGetValue(parentRowId, out ObjectAttributeValue? parent)
                    || parent.value == null
                    || !parent.value.TryGetValue(key, out string childId)
                    || !client.TryGetValue(childId, out AttributeValue? child))
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
                if (!client.TryGetValue(parentRowId, out ArrayAttributeValue? parent)
                    || parent.value == null
                    || index < 0
                    || index >= parent.value.Length
                    || !client.TryGetValue(parent.value[index], out AttributeValue? child))
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
                if (!client.TryGetValue(parentRowId, out ArrayAttributeValue? parent)
                    || parent.value == null
                    || index < 0
                    || index >= parent.value.Length)
                {
                    throw new NSGetterRuntimeError($"List index out of bounds: {index}");
                }
                var childId = parent.value[index];
                if (TryGetCustomValueReferenceId(
                        value,
                        typeInfo,
                        ctx,
                        out string? referenceId))
                {
                    string importedId = ImportCustomValueReference(
                        client,
                        ownership,
                        referenceId!,
                        ctx,
                        childId);
                    if (importedId == childId) return;
                    parent.value[index] = importedId;
                    parent.updatedAt = DateTime.UtcNow.ToString("o");
                    StoreWritableRow(client, ownership, parent, ctx);
                    client.RemoveWritableValueAndDescendantsIfUnlinked(
                        ownership, childId, AttributeFromTypeInfo(typeInfo));
                    return;
                }
                if (!client.TryGetValue(childId, out AttributeValue? existing))
                {
                    throw new NSGetterRuntimeError($"Missing list child row '{childId}'.");
                }
                var next = CreateValueRow(
                    client,
                    ownership,
                    AttributeFromTypeInfo(typeInfo),
                    value,
                    childId,
                    existing.createdAt,
                    DateTime.UtcNow.ToString("o"));
                next.typeId = existing.typeId;
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
                if (!client.TryGetValue(rowId, out ArrayAttributeValue? row))
                {
                    throw new NSGetterRuntimeError($"Missing list row '{rowId}'.");
                }
                row.value ??= Array.Empty<string>();
                var now = DateTime.UtcNow.ToString("o");
                switch (mutation)
                {
                    case CollectionMutationKind.Add:
                    {
                        if (TryGetCustomValueReferenceId(
                                args[0],
                                entryTypeInfo,
                                ctx,
                                out string? referenceId))
                        {
                            var referencedNext = new string[row.value.Length + 1];
                            Array.Copy(row.value, referencedNext, row.value.Length);
                            referencedNext[row.value.Length] = ImportCustomValueReference(
                                client,
                                ownership,
                                referenceId!,
                                ctx);
                            row.value = referencedNext;
                            row.updatedAt = now;
                            StoreWritableRow(client, ownership, row, ctx);
                            return;
                        }
                        var childId = Guid.NewGuid().ToString();
                        var child = CreateValueRow(
                            client,
                            ownership,
                            AttributeFromTypeInfo(entryTypeInfo),
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
                        string? referenceId = TryGetCustomValueReferenceId(
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
                                RemoveAt(client, ownership, row, i, now, entryTypeInfo, ctx);
                                return;
                            }
                            if (!client.TryGetValue(row.value[i], out AttributeValue? child)) continue;
                            if (!JsEqual(ReadRowValue(child), args[0])) continue;
                            RemoveAt(client, ownership, row, i, now, entryTypeInfo, ctx);
                            return;
                        }
                        return;
                    }
                    case CollectionMutationKind.Clear:
                    {
                        var removedIds = row.value;
                        row.value = Array.Empty<string>();
                        row.updatedAt = now;
                        StoreWritableRow(client, ownership, row, ctx);
                        foreach (var childId in removedIds)
                        {
                            client.RemoveWritableValueAndDescendantsIfUnlinked(
                                ownership, childId, AttributeFromTypeInfo(entryTypeInfo));
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
                ArrayAttributeValue row,
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
                    ownership, removedId, AttributeFromTypeInfo(entryTypeInfo));
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
                if (!client.TryGetValue(rowId, out ArrayAttributeValue? row))
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
                        if (Array.IndexOf(row.value, selectionId) >= 0) return;
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
                        int index = Array.IndexOf(row.value, selectionId);
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
                if (!client.TryGetValue(rowId, out ObjectAttributeValue? row))
                {
                    throw new NSGetterRuntimeError($"Missing dictionary row '{rowId}'.");
                }
                row.value ??= new Dictionary<string, string>();
                var now = DateTime.UtcNow.ToString("o");
                if (row.value.TryGetValue(key, out string existingId)
                    && client.TryGetValue(existingId, out AttributeValue? existing))
                {
                    if (TryGetCustomValueReferenceId(
                            value,
                            entryTypeInfo,
                            ctx,
                            out string? referenceId))
                    {
                        string importedId = ImportCustomValueReference(
                            client,
                            ownership,
                            referenceId!,
                            ctx,
                            existingId);
                        if (importedId == existingId) return;
                        row.value[key] = importedId;
                        row.updatedAt = now;
                        StoreWritableRow(client, ownership, row, ctx);
                        client.RemoveWritableValueAndDescendantsIfUnlinked(
                            ownership, existingId, AttributeFromTypeInfo(entryTypeInfo));
                        return;
                    }
                    var next = CreateValueRow(
                        client,
                        ownership,
                        AttributeFromTypeInfo(entryTypeInfo),
                        value,
                        existingId,
                        existing.createdAt,
                        now);
                    next.typeId = existing.typeId;
                    StoreWritableRow(client, ownership, next, ctx);
                }
                else
                {
                    if (TryGetCustomValueReferenceId(
                            value,
                            entryTypeInfo,
                            ctx,
                            out string? referenceId))
                    {
                        row.value[key] = ImportCustomValueReference(
                            client,
                            ownership,
                            referenceId!,
                            ctx);
                        row.updatedAt = now;
                        StoreWritableRow(client, ownership, row, ctx);
                        return;
                    }
                    var childId = Guid.NewGuid().ToString();
                    var next = CreateValueRow(
                        client,
                        ownership,
                        AttributeFromTypeInfo(entryTypeInfo),
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
                if (!client.TryGetValue(rowId, out ObjectAttributeValue? row)
                    || row.value == null
                    || !row.value.TryGetValue(key, out string removedId))
                {
                    return;
                }
                row.value.Remove(key);
                row.updatedAt = DateTime.UtcNow.ToString("o");
                StoreWritableRow(client, ownership, row, ctx);
                client.RemoveWritableValueAndDescendantsIfUnlinked(
                    ownership, removedId, AttributeFromTypeInfo(entryTypeInfo));
            }

            private void Clear(
                NeoClient client,
                NSGetterEvaluator.Context ctx)
            {
                EnsureWritableRow(client, rowId, ownership);
                if (!client.TryGetValue(rowId, out ObjectAttributeValue? row)
                    || row.value == null)
                {
                    return;
                }
                var removedIds = new List<string>(row.value.Values);
                row.value.Clear();
                row.updatedAt = DateTime.UtcNow.ToString("o");
                StoreWritableRow(client, ownership, row, ctx);
                foreach (var childId in removedIds)
                {
                    client.RemoveWritableValueAndDescendantsIfUnlinked(
                        ownership, childId, AttributeFromTypeInfo(entryTypeInfo));
                }
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
            string attributeId,
            NeoScriptExecutionResult execution)
        {
            ResumeKey = resumeKey;
            AttributeId = attributeId;
            Execution = execution;
        }

        internal string ResumeKey { get; }
        internal string AttributeId { get; }
        internal NeoScriptExecutionResult Execution { get; }
    }

    internal sealed class NeoScriptExecutionOptions
    {
        private readonly NeoClient client;
        private readonly Action<string> warning;
        private readonly string? propertyAttributeId;
        internal bool AllowDeferredFunctionCalls { get; }
        internal bool CancelContinuationOnDeferredDisposal { get; }

        private NeoScriptExecutionOptions(
            NeoClient client,
            Action<string> warning,
            string? propertyAttributeId,
            bool allowDeferredFunctionCalls,
            bool cancelContinuationOnDeferredDisposal)
        {
            this.client = client;
            this.warning = warning;
            this.propertyAttributeId = propertyAttributeId;
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

        internal NeoScriptExecutionOptions ForProperty(string attributeId)
        {
            return new NeoScriptExecutionOptions(
                client,
                warning,
                attributeId,
                AllowDeferredFunctionCalls,
                CancelContinuationOnDeferredDisposal);
        }

        internal NeoScriptExecutionOptions ForFunction(bool deferred)
        {
            return new NeoScriptExecutionOptions(
                client,
                warning,
                propertyAttributeId,
                allowDeferredFunctionCalls: deferred,
                cancelContinuationOnDeferredDisposal:
                    CancelContinuationOnDeferredDisposal);
        }

        internal void WarnDeferred(string functionAttributeId)
        {
            if (propertyAttributeId is null) return;
            string propertyName = client.TryGetAttribute(
                propertyAttributeId, out JsonAttribute? propertyAttribute)
                    ? propertyAttribute.name
                    : propertyAttributeId;
            string functionName = client.TryGetAttribute(
                functionAttributeId, out JsonAttribute? functionAttribute)
                    ? functionAttribute.name
                    : functionAttributeId;
            warning(
                $"NeoScript property setter '{propertyName}' ({propertyAttributeId}) " +
                $"called deferred Function '{functionName}' ({functionAttributeId}), " +
                "which did not call Complete/Fail inline. The setter will continue " +
                "asynchronously; any later error will be logged by the Neo Compose SDK.");
        }
    }

    internal sealed class NeoScriptExecutionResult
    {
        private readonly Func<object?, NeoScriptExecutionResult>? resume;
        private readonly DeferredNativeFunctionSuspension? suspension;
        private readonly Action<Exception>? failureObserver;

        private NeoScriptExecutionResult(
            bool isPaused,
            bool returned,
            object? returnValue,
            string? suspendedAttributeId,
            NeoDeferredFunctionBase? deferred,
            DeferredNativeFunctionSuspension? suspension,
            Func<object?, NeoScriptExecutionResult>? resume,
            Action<Exception>? failureObserver)
        {
            IsPaused = isPaused;
            Returned = returned;
            ReturnValue = returnValue;
            SuspendedAttributeId = suspendedAttributeId;
            Deferred = deferred;
            this.suspension = suspension;
            this.resume = resume;
            this.failureObserver = failureObserver;
        }

        internal bool IsPaused { get; }
        internal bool Returned { get; }
        internal object? ReturnValue { get; }
        internal string? SuspendedAttributeId { get; }
        internal NeoDeferredFunctionBase? Deferred { get; }

        internal static NeoScriptExecutionResult Completed(
            bool returned,
            object? returnValue)
        {
            return new NeoScriptExecutionResult(
                false,
                returned,
                returnValue,
                null,
                null,
                null,
                null,
                null);
        }

        internal static NeoScriptExecutionResult Paused(
            string suspendedAttributeId,
            NeoDeferredFunctionBase deferred,
            DeferredNativeFunctionSuspension suspension,
            Func<object?, NeoScriptExecutionResult> resume)
        {
            return new NeoScriptExecutionResult(
                true,
                false,
                null,
                suspendedAttributeId,
                deferred,
                suspension,
                resume,
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
                    failureObserver?.Invoke(exception);
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
            if (!IsPaused) return next(this);
            if (Deferred == null || suspension == null || resume == null)
            {
                throw new InvalidOperationException(
                    "Paused action result is missing deferred continuation state.");
            }
            return new NeoScriptExecutionResult(
                true,
                false,
                null,
                SuspendedAttributeId
                    ?? throw new InvalidOperationException(
                        "Paused action result is missing its Function attribute id."),
                Deferred,
                suspension,
                value => next(resume(value)),
                failureObserver);
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
            Action<Exception> combined = failureObserver is null
                ? observer
                : exception =>
                {
                    failureObserver(exception);
                    observer(exception);
                };
            return new NeoScriptExecutionResult(
                true,
                false,
                null,
                SuspendedAttributeId,
                Deferred,
                suspension,
                resume,
                combined);
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
