#nullable enable
using System;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    internal static partial class NeoScriptExecutor
    {
        internal sealed class ErrorSelection
        {
            internal readonly ErrorHandlerFrame Handler;
            internal readonly CatchClause Clause;
            internal readonly NeoScriptScope Scope;
            internal ErrorSelection(ErrorHandlerFrame handler, CatchClause clause, NeoScriptScope scope)
            {
                Handler = handler;
                Clause = clause;
                Scope = scope;
            }
        }

        // Frames are invocation-owned immutable links, so suspension never leaves
        // an active handler installed on another invocation's context.
        internal sealed class ErrorHandlerFrame
        {
            internal readonly ErrorHandlerFrame? Parent;
            private readonly NeoClient client;
            private readonly TryInstruction instruction;
            private readonly TypeInfo returnType;
            private readonly NeoScriptScope scope;
            private readonly NSGetterEvaluator.Context context;
            private readonly NeoScriptExecutionOptions? options;

            internal ErrorHandlerFrame(NeoClient client, TryInstruction instruction, TypeInfo returnType,
                NeoScriptScope scope, NSGetterEvaluator.Context context, NeoScriptExecutionOptions? options)
            {
                Parent = context.errorHandler;
                this.client = client;
                this.instruction = instruction;
                this.returnType = returnType;
                this.scope = scope;
                this.context = context;
                this.options = options;
            }

            internal NeoScriptExecutionResult Execute()
            {
                return RunSettled(
                    () => ExecuteInstructions(client, instruction.instructions, returnType,
                        scope.CreateBlock(), context.WithErrorHandler(this, context.caughtError), 0, null, options),
                    result => result.IsFailed ? HandleFailure(result.Failure!) : Cleanup(result));
            }

            private NeoScriptExecutionResult HandleFailure(Exception exception)
            {
                Exception normalized = NSGetterEvaluator.NormalizeError(exception, context);
                if (!IsAuthoredCatchableError(normalized))
                    return NeoScriptExecutionResult.Failed(normalized);
                var error = (NSGetterRuntimeError)normalized;
                if (error.Payload.Searched)
                    return Unwind(error);
                error.Payload.Searched = true;
                return ThenWhenCompleted(Search(error, 0), result => result.IsFailed ? result : Unwind(error));
            }

            private NeoScriptExecutionResult Unwind(NSGetterRuntimeError error)
            {
                ErrorSelection? selected = error.Payload.Selection;
                if (selected?.Handler != this)
                    return Cleanup(NeoScriptExecutionResult.Failed(error));
                error.Payload.Searched = false;
                error.Payload.Selection = null;
                return RunSettled(
                    () => ExecuteInstructions(client, selected.Clause.instructions, returnType,
                        selected.Scope, context.WithErrorHandler(Parent, error), 0, null, options),
                    result => SearchBeforeCleanup(result, Parent, Cleanup));
            }

            private NeoScriptExecutionResult SearchBeforeCleanup(NeoScriptExecutionResult result,
                ErrorHandlerFrame? first, Func<NeoScriptExecutionResult, NeoScriptExecutionResult> finish)
            {
                if (!result.IsFailed || !IsAuthoredCatchableError(result.Failure!))
                    return finish(result);
                var error = (NSGetterRuntimeError)result.Failure!;
                if (error.Payload.Searched)
                    return finish(result);
                error.Payload.Searched = true;
                return ThenWhenCompleted(first?.Search(error, 0) ?? default,
                    searched => searched.IsFailed ? searched : finish(result));
            }

            private NeoScriptExecutionResult Cleanup(NeoScriptExecutionResult pending)
            {
                if (pending.IsFailed && !IsAuthoredCatchableError(pending.Failure!))
                    return pending;
                if (instruction.finallyInstructions is not { } cleanup)
                    return pending;
                return RunSettled(
                    () => ExecuteInstructions(client, cleanup, returnType, scope.CreateBlock(),
                        context.WithErrorHandler(Parent), 0, null, options),
                    result =>
                    {
                        if (result.IsFailed)
                            return SearchBeforeCleanup(result, Parent, failure => failure);
                        if (!result.IsFallthrough)
                            throw new NeoScriptPreExecutionValidationError("Control transfer cannot escape finally.");
                        return pending;
                    });
            }

            // First pass. Each filter owns its expression cursor across suspension.
            private NeoScriptExecutionResult Search(NSGetterRuntimeError error, int index)
            {
                while (index < instruction.catches.Length)
                {
                    CatchClause clause = instruction.catches[index];
                    if (!NSGetterEvaluator.MatchesError(error, clause.binding.typeInfo, context))
                    {
                        index++;
                        continue;
                    }
                    var catchScope = scope.CreateBlock();
                    catchScope[clause.binding.id] = clause.binding.typeInfo.type == MemberKind.String
                        ? error.Payload.Message : NSGetterEvaluator.ErrorValue(error.Payload, context);
                    MarkReadOnlyBinding(catchScope, clause.binding.id, "Cannot assign to a read-only catch binding.");
                    if (clause.filter is null)
                    {
                        error.Payload.Selection = new ErrorSelection(this, clause, catchScope);
                        return default;
                    }
                    var expression = ExpressionResumeState.ForInstruction(instruction, options);
                    return Filter(error, index, clause, catchScope, expression);
                }
                return Parent?.Search(error, 0) ?? default;
            }

            private NeoScriptExecutionResult Filter(NSGetterRuntimeError error, int index,
                CatchClause clause, NeoScriptScope catchScope, ExpressionResumeState expression)
            {
                expression.BeginInstructionAttempt();
                try
                {
                    // A filter failure searches only handlers established inside it.
                    var filterContext = ExpressionContextFor(client, context.WithErrorHandler(null, context.caughtError), expression, options);
                    if (EvaluateBoolean(clause.filter!, catchScope, filterContext))
                    {
                        error.Payload.Selection = new ErrorSelection(this, clause, catchScope);
                        return default;
                    }
                }
                catch (NeoFunctionCallSuspended suspended)
                {
                    return PauseLoopExpression(suspended, expression, options,
                        () => Filter(error, index, clause, catchScope, expression))
                        .RecoverFailure(exception => RejectFilter(error, index, exception));
                }
                catch (Exception exception)
                {
                    return RejectFilter(error, index, exception);
                }
                return Search(error, index + 1);
            }

            private NeoScriptExecutionResult RejectFilter(NSGetterRuntimeError error, int index, Exception exception)
            {
                Exception failure = NSGetterEvaluator.NormalizeError(exception, context);
                if (!IsAuthoredCatchableError(failure))
                    return NeoScriptExecutionResult.Failed(failure);
                return Search(error, index + 1);
            }

            private NeoScriptExecutionResult RunSettled(Func<NeoScriptExecutionResult> run,
                Func<NeoScriptExecutionResult, NeoScriptExecutionResult> settle)
            {
                NeoScriptExecutionResult result;
                try
                {
                    result = run();
                }
                catch (NeoFunctionCallSuspended) { throw; }
                catch (Exception exception)
                {
                    return settle(NeoScriptExecutionResult.Failed(NSGetterEvaluator.NormalizeError(exception, context)));
                }
                if (!result.IsPaused)
                    return settle(result);
                bool recovered = false;
                return ThenWhenCompleted(result.RecoverFailure(exception =>
                {
                    recovered = true;
                    return settle(NeoScriptExecutionResult.Failed(NSGetterEvaluator.NormalizeError(exception, context)));
                }), completed => recovered ? completed : settle(completed));
            }
        }

        private static NeoScriptExecutionResult ExecuteTry(NeoClient client, TryInstruction instruction,
            TypeInfo returnTypeInfo, NeoScriptScope scope, NSGetterEvaluator.Context context,
            NeoScriptExecutionOptions? options)
        {
            ValidateTryInstructionMetadata(instruction);
            NSGetterEvaluator.ValidateCatchTypes(instruction, context);
            return new ErrorHandlerFrame(client, instruction, returnTypeInfo, scope, context, options).Execute();
        }
    }
}
