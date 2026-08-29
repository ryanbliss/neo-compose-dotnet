// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using UnityEngine;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Runtime wrapper for a NeoScript-backed Function member. Immediate
    /// functions execute synchronously; deferred functions expose a Task that
    /// owns every nested native/NeoScript continuation until settlement.
    /// </summary>
    public sealed class NeoMemberNSFunction
        : NeoMember<NSFunctionMember, NullMemberValue>
    {
        public NeoMemberNSFunction(
            NeoClient client,
            string memberId,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberNSFunction(
            NeoClient client,
            NSFunctionMember member,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        public FunctionWithReturnType? resolvedAction =>
            NeoNSFunctionRuntime.TryResolve(client, member.id)?.Action;

        public TypeInfo? resolvedReturnTypeInfo =>
            NeoNSFunctionRuntime.TryResolve(client, member.id)?.ReturnTypeInfo;

        public FunctionArgumentTypeInfo[]? resolvedArgumentTypes =>
            NeoNSFunctionRuntime.TryResolve(client, member.id)?.ArgumentTypes;

        public bool resolvedDeferred =>
            NeoNSFunctionRuntime.TryResolve(client, member.id)?.Deferred ?? false;

        public object? Invoke(string thisValueId, object?[] args)
        {
            Invocation invocation = PrepareInvocation(thisValueId, args);
            if (invocation.Function.Deferred)
            {
                throw new InvalidOperationException(
                    $"NSFunction '{invocation.Function.Member.name}' is deferred; use InvokeAsync.");
            }

            NeoScriptExecutionResult result = NeoNSFunctionRuntime.ExecuteResolved(
                client,
                invocation.Function,
                invocation.Receiver,
                args,
                invocation.Context,
                NeoScriptExecutionOptions.ForImmediate(client));
            if (result.IsPaused)
            {
                result.Deferred?.DisposeFromOwner(
                    "synchronous NSFunction invocation suspended");
                throw new NSGetterRuntimeError(
                    $"Non-deferred NSFunction '{invocation.Function.Member.name}' suspended; its compiled IR is stale or corrupt.");
            }
            return result.ReturnValue;
        }

        public Task<object?> InvokeAsync(string thisValueId, object?[] args)
        {
            try
            {
                Invocation invocation = PrepareInvocation(thisValueId, args);
                if (!invocation.Function.Deferred)
                {
                    throw new InvalidOperationException(
                        $"NSFunction '{invocation.Function.Member.name}' is immediate; use Invoke.");
                }
                NeoScriptExecutionResult result = NeoNSFunctionRuntime.ExecuteResolved(
                    client,
                    invocation.Function,
                    invocation.Receiver,
                    args,
                    invocation.Context,
                    NeoScriptExecutionOptions.ForDirectFunction(client));
                return AwaitExecution(result);
            }
            catch (Exception exception)
            {
                return Task.FromException<object?>(exception);
            }
        }

        private Invocation PrepareInvocation(string thisValueId, object?[] args)
        {
            if (string.IsNullOrWhiteSpace(thisValueId))
            {
                throw new ArgumentException(
                    "A non-empty receiver value id is required.",
                    nameof(thisValueId));
            }
            args ??= Array.Empty<object?>();
            if (!client.TryGetValue(ownership, thisValueId, out MemberValue? row))
            {
                throw new NSGetterRuntimeError(
                    $"thisValueId '{thisValueId}' was not found in {ownership.ToString().ToLowerInvariant()} values.");
            }

            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: ownership);
            object? root = NeoScriptValueMarshaller.ResolveRoot(client, ctx);
            ctx = ctx.WithRoot(root);
            object? receiver = NSGetterEvaluator.UnwrapRow(row, ctx, ownership);
            if (receiver is null)
            {
                throw new NSGetterRuntimeError(
                    $"NSFunction '{member.name}' cannot be invoked on a null receiver.");
            }

            string effectiveMemberId = NSGetterEvaluator.ResolveFunctionMemberId(
                new CallFunctionPointer
                {
                    type = PointerKind.CallFunction,
                    memberId = member.id,
                    receiver = new CallReceiver
                    {
                        kind = CallReceiverKind.Instance,
                        pointer = new VariablePointer
                        {
                            type = PointerKind.Variable,
                            variableId = "__this__",
                        },
                    },
                    args = Array.Empty<Pointer>(),
                    callSiteId = "__direct__",
                },
                receiver,
                ctx);
            return new Invocation(
                NeoNSFunctionRuntime.ResolveSignature(client, effectiveMemberId),
                receiver,
                ctx);
        }

        /// <summary>Invokes a receiverless static NSFunction.</summary>
        public object? InvokeStatic(object?[] args)
        {
            Invocation invocation = PrepareStaticInvocation(args);
            if (invocation.Function.Deferred)
            {
                throw new InvalidOperationException(
                    $"NSFunction '{invocation.Function.Member.name}' is deferred; use InvokeStaticAsync.");
            }
            NeoScriptExecutionResult result = NeoNSFunctionRuntime.ExecuteResolved(
                client,
                invocation.Function,
                receiver: null,
                args,
                invocation.Context,
                NeoScriptExecutionOptions.ForImmediate(client));
            if (result.IsPaused)
            {
                result.Deferred?.DisposeFromOwner(
                    "synchronous static NSFunction invocation suspended");
                throw new NSGetterRuntimeError(
                    $"Non-deferred static NSFunction '{invocation.Function.Member.name}' suspended; its compiled IR is stale or corrupt.");
            }
            return result.ReturnValue;
        }

        /// <summary>Invokes a deferred receiverless static NSFunction.</summary>
        public Task<object?> InvokeStaticAsync(object?[] args)
        {
            try
            {
                Invocation invocation = PrepareStaticInvocation(args);
                if (!invocation.Function.Deferred)
                {
                    throw new InvalidOperationException(
                        $"NSFunction '{invocation.Function.Member.name}' is immediate; use InvokeStatic.");
                }
                return AwaitExecution(NeoNSFunctionRuntime.ExecuteResolved(
                    client,
                    invocation.Function,
                    receiver: null,
                    args,
                    invocation.Context,
                    NeoScriptExecutionOptions.ForDirectFunction(client)));
            }
            catch (Exception exception)
            {
                return Task.FromException<object?>(exception);
            }
        }

        private Invocation PrepareStaticInvocation(object?[] args)
        {
            args ??= Array.Empty<object?>();
            NeoResolvedNSFunction function = NeoNSFunctionRuntime.ResolveSignature(
                client,
                member.id);
            if (function.Member.EffectiveModifier != NeoMemberModifierKind.Static)
            {
                throw new NSGetterRuntimeError(
                    $"NSFunction '{function.Member.name}' is an instance member and requires a receiver.");
            }
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: NeoValueOwnership.Session);
            return new Invocation(
                function,
                receiver: null,
                ctx.WithRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx)));
        }

        private Task<object?> AwaitExecution(NeoScriptExecutionResult initial)
        {
            if (!initial.IsPaused) return Task.FromResult(initial.ReturnValue);

            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Observe(initial);
            return completion.Task;

            void Observe(NeoScriptExecutionResult execution)
            {
                if (!execution.IsPaused)
                {
                    completion.TrySetResult(execution.ReturnValue);
                    return;
                }
                NeoDeferredFunctionBase deferred = execution.Deferred
                    ?? throw new InvalidOperationException(
                        "Paused NSFunction execution is missing its deferred handle.");
                client.TrackDirectDeferredFunction(deferred);
                execution.WhenDeferredSettled(
                    resumed =>
                    {
                        client.RemoveDirectDeferredFunction(deferred);
                        Observe(resumed);
                    },
                    exception =>
                    {
                        client.RemoveDirectDeferredFunction(deferred);
                        if (exception is OperationCanceledException
                            || exception is ObjectDisposedException)
                        {
                            completion.TrySetCanceled();
                        }
                        else
                        {
                            completion.TrySetException(exception);
                        }
                    });
            }
        }

        private readonly struct Invocation
        {
            internal Invocation(
                NeoResolvedNSFunction function,
                object? receiver,
                NSGetterEvaluator.Context context)
            {
                Function = function;
                Receiver = receiver;
                Context = context;
            }

            internal NeoResolvedNSFunction Function { get; }
            internal object? Receiver { get; }
            internal NSGetterEvaluator.Context Context { get; }
        }
    }

    internal sealed class NeoResolvedNSFunction
    {
        internal NeoResolvedNSFunction(
            string memberId,
            NSFunctionMember member,
            FunctionWithReturnType action,
            TypeInfo returnTypeInfo,
            FunctionArgumentTypeInfo[] argumentTypes,
            bool deferred)
        {
            MemberId = memberId;
            Member = member;
            Action = action;
            ReturnTypeInfo = returnTypeInfo;
            ArgumentTypes = argumentTypes;
            Deferred = deferred;
        }

        internal string MemberId { get; }
        internal NSFunctionMember Member { get; }
        internal FunctionWithReturnType Action { get; }
        internal TypeInfo ReturnTypeInfo { get; }
        internal FunctionArgumentTypeInfo[] ArgumentTypes { get; }
        internal bool Deferred { get; }
    }

    internal static class NeoNSFunctionRuntime
    {
        private const int MaxCallableDepth = 64;

        internal static object? InvokeImmediate(
            NeoClient client,
            string memberId,
            object? receiver,
            object?[] args,
            NSGetterEvaluator.Context ctx)
        {
            NeoResolvedNSFunction function = ResolveSignature(client, memberId);
            if (function.Deferred)
            {
                throw new NeoDeferredFunctionRuntimeError(
                    $"NSFunction '{function.Member.name}' ({memberId}) deferred-mode mismatch: " +
                    "an immediate NeoScript frame called its deferred signature; " +
                    "compiled call IR is stale/corrupt.");
            }
            NeoScriptExecutionResult result = ExecuteResolved(
                client,
                function,
                receiver,
                args,
                ctx,
                NeoScriptExecutionOptions.ForImmediate(client));
            if (result.IsPaused)
            {
                result.Deferred?.DisposeFromOwner(
                    "immediate NSFunction invocation suspended");
                throw new NSGetterRuntimeError(
                    $"Non-deferred NSFunction '{function.Member.name}' suspended; its compiled IR is stale or corrupt.");
            }
            return result.ReturnValue;
        }

        internal static NeoScriptExecutionResult Execute(
            NeoClient client,
            string memberId,
            object? receiver,
            object?[] args,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions options)
        {
            return ExecuteResolved(
                client,
                ResolveSignature(client, memberId),
                receiver,
                args,
                ctx,
                options);
        }

        internal static NeoScriptExecutionResult ExecuteResolved(
            NeoClient client,
            NeoResolvedNSFunction function,
            object? receiver,
            object?[] args,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions options)
        {
            bool isStatic = function.Member.EffectiveModifier == NeoMemberModifierKind.Static;
            if (receiver is null && !isStatic)
            {
                throw new NSGetterRuntimeError(
                    $"Cannot invoke NSFunction '{function.Member.name}' on a null receiver.");
            }
            if (receiver is not null && isStatic)
            {
                throw new NSGetterRuntimeError(
                    $"Static NSFunction '{function.Member.name}' must be invoked without an instance receiver.");
            }
            args ??= Array.Empty<object?>();
            // P65 §2.5 callee-side fill: a positionally short call is
            // completed from the callee record's current defaults before the
            // `__arg_N__` parameters bind. Below the non-defaulted minimum and
            // above the full arity remain hard errors.
            args = NeoParameterDefaults.FillTrailingDefaults(
                args,
                function.ArgumentTypes,
                $"NSFunction '{function.Member.name}' ({function.MemberId})");
            if (ctx.functionCallStack.Count >= MaxCallableDepth)
            {
                var names = new List<string>(ctx.functionCallStack.Count + 1);
                foreach (string id in ctx.functionCallStack)
                {
                    names.Add(client.TryGetMember(id, out JsonMember? item)
                        ? item.name
                        : id);
                }
                names.Add(function.Member.name);
                throw new NSGetterRuntimeError(
                    $"NeoScript Function call depth exceeded {MaxCallableDepth}: {string.Join(" -> ", names)}.");
            }

            FunctionWithReturnType action = function.Action;
            int receiverParameterCount = isStatic ? 1 : 2;
            int expectedParameters = function.ArgumentTypes.Length + receiverParameterCount;
            if (action.parameters is null || action.parameters.Length != expectedParameters)
            {
                throw new NSGetterRuntimeError(
                    $"NSFunction '{function.Member.name}' compiled action parameter count is stale (expected {expectedParameters}, found {action.parameters?.Length ?? 0}).");
            }

            TypeInfo effectiveReturnType = function.ReturnTypeInfo;
            TypeInfo[] effectiveArgumentTypes = function.ArgumentTypes;
            if (isStatic
                && (ContainsGeneric(function.ReturnTypeInfo)
                    || Array.Exists(function.ArgumentTypes, ContainsGeneric)))
            {
                throw new NSGetterRuntimeError(
                    $"Static NSFunction '{function.Member.name}' cannot use receiver-bound Generic signature classes.");
            }
            if (!isStatic
                && (ContainsGeneric(function.ReturnTypeInfo)
                || Array.Exists(function.ArgumentTypes, ContainsGeneric))
               )
            {
                IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv =
                    ResolveReceiverGenericEnv(client, receiver!, ctx, function);
                effectiveReturnType = ResolveInvocationTypeInfo(
                    client,
                    function.ReturnTypeInfo,
                    genericEnv,
                    new HashSet<string>());
                effectiveArgumentTypes = new TypeInfo[function.ArgumentTypes.Length];
                for (int i = 0; i < function.ArgumentTypes.Length; i++)
                {
                    effectiveArgumentTypes[i] = ResolveInvocationTypeInfo(
                        client,
                        function.ArgumentTypes[i],
                        genericEnv,
                        new HashSet<string>());
                }
            }

            var scope = new Dictionary<string, object?>(expectedParameters);
            int rootParameterIndex;
            int argumentParameterOffset;
            if (isStatic)
            {
                rootParameterIndex = 0;
                argumentParameterOffset = 1;
            }
            else
            {
                scope[action.parameters[0].id] = receiver;
                rootParameterIndex = 1;
                argumentParameterOffset = 2;
            }
            scope[action.parameters[rootParameterIndex].id] = ctx.rootValue;
            for (int i = 0; i < args.Length; i++)
            {
                FunctionArgumentTypeInfo argument = function.ArgumentTypes[i];
                try
                {
                    scope[action.parameters[i + argumentParameterOffset].id] = NeoScriptValueMarshaller.Normalize(
                        client,
                        ctx.valueOwnership,
                        args[i],
                        effectiveArgumentTypes[i],
                        ctx,
                        $"argument {i} '{argument.name}' of NSFunction '{function.Member.name}'");
                }
                catch (Exception exception)
                {
                    throw new NSGetterRuntimeError(
                        $"NSFunction '{function.Member.name}' ({function.MemberId}) argument {i} " +
                        $"'{argument.name}' is incompatible with declared {argument.type}; " +
                        "compiled call IR or caller is stale/corrupt: " +
                        exception.Message);
                }
            }

            NSGetterEvaluator.Context nestedCtx = ctx
                .WithFunctionPushed(function.MemberId)
                .WithThis(isStatic ? null : receiver);
            NeoScriptExecutionResult execution = NeoScriptExecutor.Execute(
                client,
                action,
                scope,
                nestedCtx,
                options.ForFunction(function.Deferred),
                terminal => NormalizeTerminal(
                    client,
                    nestedCtx,
                    terminal,
                    function,
                    effectiveReturnType));
            return execution;
        }

        private static NeoScriptExecutionResult NormalizeTerminal(
            NeoClient client,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionResult execution,
            NeoResolvedNSFunction function,
            TypeInfo effectiveReturnType)
        {
            if (execution.IsPaused)
                throw new InvalidOperationException(
                    "NSFunction terminal normalization received a paused execution.");
            if (effectiveReturnType is VoidTypeInfo
                || effectiveReturnType.type == MemberKind.Void)
            {
                if (execution.ReturnValue is not null)
                {
                    throw new NSGetterRuntimeError(
                        $"Void NSFunction '{function.Member.name}' returned a value; its compiled IR is stale or corrupt.");
                }
                return NeoScriptExecutionResult.Completed(
                    execution.Returned,
                    returnValue: null);
            }
            if (!execution.Returned)
            {
                throw new NSGetterRuntimeError(
                    $"NSFunction '{function.Member.name}' ended without returning a value; its compiled IR is stale or corrupt.");
            }
            string subject =
                $"return value of NSFunction '{function.Member.name}'";
            object? normalized = NeoScriptValueMarshaller.Normalize(
                client,
                ctx.valueOwnership,
                execution.ReturnValue,
                effectiveReturnType,
                ctx,
                subject);
            NeoScriptValueMarshaller.ValidateResolvedRuntimeValue(
                client,
                normalized,
                effectiveReturnType,
                ctx,
                subject);
            return NeoScriptExecutionResult.Completed(
                returned: true,
                normalized);
        }

        private static bool ContainsGeneric(TypeInfo typeInfo)
        {
            if (typeInfo.type == MemberKind.Generic) return true;
            TypeInfo? delegateReturn = typeInfo switch
            {
                DelegateTypeInfo delegateType => delegateType.returnTypeInfo,
                FunctionArgumentTypeInfo argument
                    when argument.type == MemberKind.NSDelegate => argument.returnTypeInfo,
                _ => null,
            };
            if (delegateReturn is not null && ContainsGeneric(delegateReturn)) return true;
            TypeInfo[]? delegateArguments = typeInfo switch
            {
                DelegateTypeInfo delegateType => delegateType.argumentTypes,
                // An action carries arguments and no return slot, so it joins
                // only this scan (P62 §2.1).
                ActionTypeInfo actionType => actionType.argumentTypes,
                FunctionArgumentTypeInfo argument
                    when argument.type is MemberKind.NSDelegate
                        or MemberKind.NSAction => argument.argumentTypes,
                _ => null,
            };
            if (delegateArguments is not null
                && Array.Exists(delegateArguments, ContainsGeneric))
            {
                return true;
            }
            TypeInfo? entryTypeInfo = typeInfo switch
            {
                FunctionArgumentTypeInfo argument => argument.entryTypeInfo,
                CollectionTypeInfo collection => collection.entryTypeInfo,
                LookupTypeInfo lookup => lookup.entryTypeInfo,
                _ => null,
            };
            if (entryTypeInfo is not null && ContainsGeneric(entryTypeInfo))
            {
                return true;
            }
            Dictionary<string, TypeInfo>? typeArguments = typeInfo switch
            {
                FunctionArgumentTypeInfo argument => argument.typeArguments,
                ClassTypeInfo classType => classType.typeArguments,
                _ => null,
            };
            if (typeArguments is null) return false;
            foreach (TypeInfo argument in typeArguments.Values)
            {
                if (ContainsGeneric(argument)) return true;
            }
            return false;
        }

        internal static IReadOnlyDictionary<string, NeoGenericEnvEntry>
            ResolveReceiverGenericEnv(
                NeoClient client,
                object receiver,
                NSGetterEvaluator.Context ctx,
                NeoResolvedNSFunction function)
        {
            string? runtimeClassId = NSGetterEvaluator.FindRowClassIdByReference(
                receiver,
                ctx);
            if (string.IsNullOrEmpty(runtimeClassId))
            {
                throw new NSGetterRuntimeError(
                    $"NSFunction '{function.Member.name}' uses generic signature classes, but its receiver has no runtime class.");
            }

            string? receiverValueId = NSGetterEvaluator.FindRowIdByReference(
                receiver,
                ctx);
            string? cacheKey = string.IsNullOrEmpty(receiverValueId)
                ? null
                : runtimeClassId + "\n" + receiverValueId;
            if (cacheKey is not null
                && ctx.genericEnvironmentCache.TryGetValue(
                    cacheKey, out IReadOnlyDictionary<
                        string, NeoGenericEnvEntry>? cached))
            {
                return cached;
            }

            IReadOnlyDictionary<string, GenericBinding>? constructedArguments = null;
            if (!string.IsNullOrEmpty(receiverValueId)
                && client.TryInferMemberForValueId(
                    receiverValueId!,
                    out JsonMember? placementMember)
                && placementMember is ClassMember classPlacement)
            {
                constructedArguments = classPlacement.classArguments;
            }

            try
            {
                IReadOnlyDictionary<string, NeoGenericEnvEntry> resolved =
                    NeoGenericResolution.ResolveInstanceEnv(
                    client,
                    runtimeClassId!,
                    constructedArguments);
                if (cacheKey is not null)
                {
                    ctx.genericEnvironmentCache[cacheKey] = resolved;
                }
                return resolved;
            }
            catch (Exception exception)
            {
                throw new NSGetterRuntimeError(
                    $"NSFunction '{function.Member.name}' could not resolve the receiver's generic environment: {exception.Message}");
            }
        }

        internal static TypeInfo ResolveInvocationTypeInfo(
            NeoClient client,
            TypeInfo typeInfo,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv)
        {
            return ResolveInvocationTypeInfo(
                client,
                typeInfo,
                genericEnv,
                new HashSet<string>());
        }

        private static TypeInfo ResolveInvocationTypeInfo(
            NeoClient client,
            TypeInfo typeInfo,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
            HashSet<string> visitingMembers)
        {
            if (typeInfo.type == MemberKind.Generic)
            {
                string? genericParamId = typeInfo switch
                {
                    GenericTypeInfo generic => generic.genericParamId,
                    FunctionArgumentTypeInfo argument => argument.genericParamId,
                    _ => null,
                };
                if (string.IsNullOrEmpty(genericParamId)
                    || !genericEnv.TryGetValue(
                        genericParamId!,
                        out NeoGenericEnvEntry? binding)
                    || !binding.IsBound
                    || string.IsNullOrEmpty(binding.memberId))
                {
                    throw new NSGetterRuntimeError(
                        $"Generic NSFunction type '{genericParamId ?? "<missing>"}' is unbound for this receiver.");
                }
                if (!client.TryGetMember(
                    binding.memberId!,
                    out JsonMember? bindingMember))
                {
                    throw new NSGetterRuntimeError(
                        $"Generic NSFunction type '{genericParamId}' references missing binding member '{binding.memberId}'.");
                }
                return TypeInfoFromBindingMember(
                    client,
                    bindingMember,
                    genericEnv,
                    visitingMembers);
            }

            if (typeInfo.type == MemberKind.Class)
            {
                string? classId = typeInfo switch
                {
                    FunctionArgumentTypeInfo argument => argument.classId,
                    ClassTypeInfo classType => classType.classId,
                    _ => null,
                };
                Dictionary<string, TypeInfo>? typeArguments = typeInfo switch
                {
                    FunctionArgumentTypeInfo argument => argument.typeArguments,
                    ClassTypeInfo classType => classType.typeArguments,
                    _ => null,
                };
                if (string.IsNullOrEmpty(classId))
                {
                    throw new NSGetterRuntimeError(
                        "Class NSFunction type is missing its classId.");
                }
                return new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = typeInfo.required,
                    classId = classId!,
                    typeArguments = ResolveInvocationTypeArguments(
                        client,
                        typeArguments,
                        genericEnv,
                        visitingMembers),
                };
            }

            if (typeInfo.type == MemberKind.NSDelegate)
            {
                TypeInfo? delegateReturn = typeInfo switch
                {
                    DelegateTypeInfo delegateType => delegateType.returnTypeInfo,
                    FunctionArgumentTypeInfo argument => argument.returnTypeInfo,
                    _ => null,
                };
                TypeInfo[]? delegateArguments = typeInfo switch
                {
                    DelegateTypeInfo delegateType => delegateType.argumentTypes,
                    FunctionArgumentTypeInfo argument => argument.argumentTypes,
                    _ => null,
                };
                if (delegateReturn is null || delegateArguments is null)
                {
                    throw new NSGetterRuntimeError(
                        "NeoDelegate NSFunction type is missing its signature.");
                }
                var resolvedArguments = new TypeInfo[delegateArguments.Length];
                for (int i = 0; i < delegateArguments.Length; i++)
                {
                    resolvedArguments[i] = ResolveInvocationTypeInfo(
                        client,
                        delegateArguments[i],
                        genericEnv,
                        visitingMembers);
                }
                return new DelegateTypeInfo
                {
                    type = MemberKind.NSDelegate,
                    required = typeInfo.required,
                    returnTypeInfo = delegateReturn is VoidTypeInfo
                        ? delegateReturn
                        : ResolveInvocationTypeInfo(
                            client,
                            delegateReturn,
                            genericEnv,
                            visitingMembers),
                    argumentTypes = resolvedArguments,
                };
            }

            if (typeInfo.type == MemberKind.NSAction)
            {
                TypeInfo[]? actionArguments = typeInfo switch
                {
                    ActionTypeInfo actionType => actionType.argumentTypes,
                    FunctionArgumentTypeInfo argument => argument.argumentTypes,
                    _ => null,
                };
                if (actionArguments is null)
                {
                    throw new NSGetterRuntimeError(
                        "NeoAction NSFunction type is missing its signature.");
                }
                var resolvedActionArguments = new TypeInfo[actionArguments.Length];
                for (int i = 0; i < actionArguments.Length; i++)
                {
                    resolvedActionArguments[i] = ResolveInvocationTypeInfo(
                        client,
                        actionArguments[i],
                        genericEnv,
                        visitingMembers);
                }
                // No return slot to substitute: void is structural for an
                // action, so the delegate arm's void carve-outs do not exist.
                return new ActionTypeInfo
                {
                    type = MemberKind.NSAction,
                    required = typeInfo.required,
                    argumentTypes = resolvedActionArguments,
                };
            }

            TypeInfo? entryTypeInfo = typeInfo switch
            {
                FunctionArgumentTypeInfo argument => argument.entryTypeInfo,
                CollectionTypeInfo collection => collection.entryTypeInfo,
                LookupTypeInfo lookup => lookup.entryTypeInfo,
                _ => null,
            };
            if (typeInfo.type is MemberKind.List or MemberKind.Dictionary
                && entryTypeInfo is not null
                && ContainsGeneric(entryTypeInfo))
            {
                return new CollectionTypeInfo
                {
                    type = typeInfo.type,
                    required = typeInfo.required,
                    keyEnumId = typeInfo switch
                    {
                        FunctionArgumentTypeInfo argument => argument.keyEnumId,
                        CollectionTypeInfo collection => collection.keyEnumId,
                        _ => null,
                    },
                    listMemberId = typeInfo switch
                    {
                        FunctionArgumentTypeInfo argument => argument.listMemberId,
                        CollectionTypeInfo collection => collection.listMemberId,
                        _ => null,
                    },
                    entryTypeInfo = ResolveInvocationTypeInfo(
                        client,
                        entryTypeInfo,
                        genericEnv,
                        visitingMembers),
                };
            }
            if (typeInfo.type == MemberKind.Lookup
                && entryTypeInfo is not null
                && ContainsGeneric(entryTypeInfo))
            {
                string? collectionMemberId = typeInfo switch
                {
                    FunctionArgumentTypeInfo argument =>
                        argument.collectionMemberId,
                    LookupTypeInfo lookup => lookup.collectionMemberId,
                    _ => null,
                };
                string? collectionValueId = typeInfo switch
                {
                    FunctionArgumentTypeInfo argument => argument.collectionValueId,
                    LookupTypeInfo lookup => lookup.collectionValueId,
                    _ => null,
                };
                return new LookupTypeInfo
                {
                    type = MemberKind.Lookup,
                    required = typeInfo.required,
                    collectionMemberId = collectionMemberId ?? "",
                    collectionValueId = collectionValueId,
                    entryTypeInfo = ResolveInvocationTypeInfo(
                        client,
                        entryTypeInfo,
                        genericEnv,
                        visitingMembers),
                };
            }
            return typeInfo;
        }

        private static Dictionary<string, TypeInfo>?
            ResolveInvocationTypeArguments(
                NeoClient client,
                IReadOnlyDictionary<string, TypeInfo>? typeArguments,
                IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
                HashSet<string> visitingMembers)
        {
            if (typeArguments is null) return null;
            var resolved = new Dictionary<string, TypeInfo>(typeArguments.Count);
            foreach (var pair in typeArguments)
            {
                resolved[pair.Key] = ResolveInvocationTypeInfo(
                    client,
                    pair.Value,
                    genericEnv,
                    visitingMembers);
            }
            return resolved;
        }

        private static TypeInfo TypeInfoFromBindingMember(
            NeoClient client,
            JsonMember member,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
            HashSet<string> visitingMembers)
        {
            if (!visitingMembers.Add(member.id))
            {
                throw new NSGetterRuntimeError(
                    $"Generic NSFunction binding member cycle detected at '{member.id}'.");
            }
            try
            {
                switch (member)
                {
                    case GenericMember generic:
                        return ResolveInvocationTypeInfo(
                            client,
                            new GenericTypeInfo
                            {
                                type = MemberKind.Generic,
                                required = generic.EffectiveRequirement == NeoMemberRequirementKind.Required,
                                genericParamId = generic.genericParamId,
                            },
                            genericEnv,
                            visitingMembers);
                    case ClassMember classMember:
                        return new ClassTypeInfo
                        {
                            type = MemberKind.Class,
                            required = classMember.EffectiveRequirement == NeoMemberRequirementKind.Required,
                            classId = classMember.classId,
                            typeArguments = ResolveBindingTypeArguments(
                                client,
                                classMember.classArguments,
                                genericEnv,
                                visitingMembers),
                        };
                    case EnumMember enumMember:
                        return new EnumTypeInfo
                        {
                            type = MemberKind.Enum,
                            required = enumMember.EffectiveRequirement == NeoMemberRequirementKind.Required,
                            enumId = enumMember.enumId,
                        };
                    case ListMember list:
                        return CollectionBindingTypeInfo(
                            client,
                            list,
                            list.entryMemberId,
                            genericEnv,
                            visitingMembers);
                    case DictionaryMember dictionary:
                        return CollectionBindingTypeInfo(
                            client,
                            dictionary,
                            dictionary.entryMemberId,
                            genericEnv,
                            visitingMembers);
                    case LookupMember lookup:
                    {
                        if (!client.TryGetMember(
                                lookup.collectionMemberId,
                                out ListMember? collection)
                            || !client.TryGetMember(
                                collection.entryMemberId,
                                out JsonMember? entryMember))
                        {
                            throw new NSGetterRuntimeError(
                                $"Generic NSFunction Lookup binding '{lookup.id}' has a missing collection entry type.");
                        }
                        return new LookupTypeInfo
                        {
                            type = MemberKind.Lookup,
                            required = lookup.EffectiveRequirement == NeoMemberRequirementKind.Required,
                            collectionMemberId = lookup.collectionMemberId,
                            collectionValueId = lookup.collectionValueId,
                            entryTypeInfo = TypeInfoFromBindingMember(
                                client,
                                entryMember,
                                genericEnv,
                                visitingMembers),
                        };
                    }
                    case DelegateMember delegateMember:
                    {
                        var arguments = new TypeInfo[delegateMember.argumentTypes.Length];
                        for (int i = 0; i < arguments.Length; i++)
                        {
                            arguments[i] = ResolveInvocationTypeInfo(
                                client,
                                delegateMember.argumentTypes[i],
                                genericEnv,
                                visitingMembers);
                        }
                        return new DelegateTypeInfo
                        {
                            type = MemberKind.NSDelegate,
                            required = delegateMember.EffectiveRequirement == NeoMemberRequirementKind.Required,
                            returnTypeInfo = delegateMember.returnTypeInfo is VoidTypeInfo
                                ? delegateMember.returnTypeInfo
                                : ResolveInvocationTypeInfo(
                                    client,
                                    delegateMember.returnTypeInfo,
                                    genericEnv,
                                    visitingMembers),
                            argumentTypes = arguments,
                        };
                    }
                    case ActionMember actionMember:
                    {
                        var arguments = new TypeInfo[actionMember.argumentTypes.Length];
                        for (int i = 0; i < arguments.Length; i++)
                        {
                            arguments[i] = ResolveInvocationTypeInfo(
                                client,
                                actionMember.argumentTypes[i],
                                genericEnv,
                                visitingMembers);
                        }
                        return new ActionTypeInfo
                        {
                            type = MemberKind.NSAction,
                            required = actionMember.EffectiveRequirement == NeoMemberRequirementKind.Required,
                            argumentTypes = arguments,
                        };
                    }
                    case NullMember:
                    case BoolMember:
                    case IntMember:
                    case FloatMember:
                    case StringMember:
                    case SpriteMember:
                    case AudioMember:
                    case Vector2Member:
                    case Vector2IntMember:
                    case Vector3Member:
                    case Vector3IntMember:
                    case ColorMember:
                    case DecimalMember:
                        return new PrimitiveTypeInfo
                        {
                            type = member.kind,
                            required = member.EffectiveRequirement == NeoMemberRequirementKind.Required,
                        };
                    default:
                        throw new NSGetterRuntimeError(
                            $"Member '{member.name}' ({member.id}) of type {member.kind} is not a valid concrete generic NSFunction binding.");
                }
            }
            finally
            {
                visitingMembers.Remove(member.id);
            }
        }

        private static Dictionary<string, TypeInfo>? ResolveBindingTypeArguments(
            NeoClient client,
            IReadOnlyDictionary<string, GenericBinding>? bindings,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
            HashSet<string> visitingMembers)
        {
            if (bindings is null) return null;
            var resolved = new Dictionary<string, TypeInfo>(bindings.Count);
            foreach (var pair in bindings)
            {
                GenericBinding binding = pair.Value;
                string? memberId;
                if (binding.kind == NeoGenericBindingKind.Member)
                {
                    memberId = binding.memberId;
                }
                else if (binding.kind == NeoGenericBindingKind.Generic
                    && !string.IsNullOrEmpty(binding.genericParamId)
                    && genericEnv.TryGetValue(
                        binding.genericParamId!,
                        out NeoGenericEnvEntry? forwarded)
                    && forwarded.IsBound)
                {
                    memberId = forwarded.memberId;
                }
                else
                {
                    throw new NSGetterRuntimeError(
                        $"Constructed Class NSFunction type argument '{pair.Key}' is unbound.");
                }
                if (string.IsNullOrEmpty(memberId)
                    || !client.TryGetMember(
                        memberId!,
                        out JsonMember? bindingMember))
                {
                    throw new NSGetterRuntimeError(
                        $"Constructed Class NSFunction type argument '{pair.Key}' references missing binding member '{memberId ?? "<missing>"}'.");
                }
                resolved[pair.Key] = TypeInfoFromBindingMember(
                    client,
                    bindingMember,
                    genericEnv,
                    visitingMembers);
            }
            return resolved;
        }

        private static CollectionTypeInfo CollectionBindingTypeInfo(
            NeoClient client,
            JsonMember collection,
            string entryMemberId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
            HashSet<string> visitingMembers)
        {
            if (!client.TryGetMember(
                entryMemberId,
                out JsonMember? entryMember))
            {
                throw new NSGetterRuntimeError(
                    $"Generic NSFunction collection binding '{collection.id}' references missing entry member '{entryMemberId}'.");
            }
            return new CollectionTypeInfo
            {
                type = collection.kind,
                required = collection.EffectiveRequirement == NeoMemberRequirementKind.Required,
                keyEnumId = (collection as DictionaryMember)?.keyEnumId,
                listMemberId = collection is ListMember ? collection.id : null,
                entryTypeInfo = TypeInfoFromBindingMember(
                    client,
                    entryMember,
                    genericEnv,
                    visitingMembers),
            };
        }

        internal static NeoResolvedNSFunction ResolveSignature(
            NeoClient client,
            string memberId)
        {
            return TryResolve(client, memberId)
                ?? throw new NSGetterRuntimeError(
                    $"NSFunction '{memberId}' has a broken override chain, missing signature, or missing compiled action.");
        }

        internal static NeoResolvedNSFunction? TryResolve(
            NeoClient client,
            string memberId)
        {
            if (client.TryGetResolvedNSFunction(
                    memberId,
                    out NeoResolvedNSFunction? cached))
            {
                return cached;
            }
            var visited = new HashSet<string>();
            string? cursor = memberId;
            NSFunctionMember? effectiveMember = null;
            FunctionWithReturnType? action = null;
            bool actionResolved = false;
            TypeInfo? returnTypeInfo = null;
            FunctionArgumentTypeInfo[]? argumentTypes = null;
            for (int hops = 0; !string.IsNullOrEmpty(cursor) && hops < 32; hops++)
            {
                if (!visited.Add(cursor!))
                {
                    throw new NSGetterRuntimeError(
                        $"Circular NSFunction override chain detected at '{cursor}' while resolving '{memberId}'.");
                }
                if (!client.TryGetMember(cursor!, out NSFunctionMember? current))
                {
                    return null;
                }
                effectiveMember ??= current;
                if (!actionResolved)
                {
                    // The centralized member projection already resolves an
                    // inherited action or an authored-body null clear.
                    action = current.action;
                    actionResolved = true;
                }
                returnTypeInfo ??= current.returnTypeInfo;
                argumentTypes ??= current.argumentTypes;
                if (action is not null
                    && returnTypeInfo is not null
                    && argumentTypes is not null)
                {
                    return client.CacheResolvedNSFunction(
                        new NeoResolvedNSFunction(
                            memberId,
                            effectiveMember,
                            action,
                            returnTypeInfo,
                            argumentTypes,
                            effectiveMember.EffectiveDispatch
                                == NeoFunctionDispatchKind.Asynchronous));
                }
                cursor = current.extendsMemberId;
            }
            return null;
        }
    }

    /// <summary>
    /// Shared C#-consumer-to-NeoScript boundary codec used by NSProperty
    /// setters and NSFunction arguments.
    /// </summary>
    internal static class NeoScriptValueMarshaller
    {
        internal static object? ResolveRoot(
            NeoClient client,
            NSGetterEvaluator.Context ctx)
        {
            return new Dictionary<string, object?>(3)
            {
                ["Assets"] = client.assets.value is ObjectMemberValue assets
                    ? NSGetterEvaluator.UnwrapRow(assets, ctx, NeoValueOwnership.Asset)
                    : null,
                ["Save"] = client.save.value is ObjectMemberValue save
                    ? NSGetterEvaluator.UnwrapRow(save, ctx, NeoValueOwnership.Save)
                    : null,
                ["Session"] = client.session.value is ObjectMemberValue session
                    ? NSGetterEvaluator.UnwrapRow(session, ctx, NeoValueOwnership.Session)
                    : null,
            };
        }

        internal static object? Normalize(
            NeoClient client,
            NeoValueOwnership ownership,
            object? value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx,
            string subject)
        {
            if (value is NeoValueWritePayload payload)
            {
                value = payload.isValueReference
                    ? payload.valueReference ?? (object?)payload.valueId
                    : payload.value;
            }
            if (value is null)
            {
                if (typeInfo.required && typeInfo.type != MemberKind.Null)
                {
                    throw new InvalidOperationException(
                        $"Required {subject} cannot be null.");
                }
                return null;
            }

            if (value is NeoLookupSelection selection)
            {
                value = UnwrapValueReference(
                    client,
                    ownership,
                    selection.valueId,
                    ctx,
                    subject);
            }
            else if (value is NeoDialogueReference dialogueReference)
            {
                value = typeInfo.type == MemberKind.DialogueLookup
                    ? new object?[] { dialogueReference.Id }
                    : dialogueReference.Id;
            }
            else if (typeInfo.type == MemberKind.Generic
                && value is not string
                && EnumOptionId(value) is string genericOptionId)
            {
                // A remaining open Generic is used by older NSProperty
                // setter paths that do not have a receiver environment at
                // this boundary. NSFunctions substitute their Generic
                // signature first, so concrete Enum calls take the canonical
                // string[] branch below.
                value = genericOptionId;
            }
            else if (value is INeoValueReference reference
                && !string.IsNullOrEmpty(reference.valueId))
            {
                NeoValueOwnership referenceOwnership =
                    value is NeoGeneratedClassValue generated
                        ? generated.ValueOwnership
                        : NSGetterEvaluator.FindRowOwnershipByReference(
                            value,
                            ctx) ?? ownership;
                value = UnwrapValueReference(
                    client,
                    referenceOwnership,
                    reference.valueId!,
                    ctx,
                    subject);
            }

            switch (typeInfo.type)
            {
                case MemberKind.Decimal:
                    if (value is decimal decimalValue)
                        value = NeoDecimalValues.Format(decimalValue);
                    else if (value is double or float or int or long or short)
                        value = NSGetterEvaluator.CoerceDecimalOperand(value, subject);
                    break;
                case MemberKind.Vector2:
                    if (value is NeoReadOnlyVector2 vector2)
                        value = NeoGeneratedTypesSupport.Vector2Value(vector2.Value);
                    else if (value is Vector2 unityVector2)
                        value = NeoGeneratedTypesSupport.Vector2Value(unityVector2);
                    break;
                case MemberKind.Vector2Int:
                    if (value is NeoReadOnlyVector2Int vector2Int)
                        value = NeoGeneratedTypesSupport.Vector2IntValue(vector2Int.Value);
                    else if (value is Vector2Int unityVector2Int)
                        value = NeoGeneratedTypesSupport.Vector2IntValue(unityVector2Int);
                    break;
                case MemberKind.Vector3:
                    if (value is NeoReadOnlyVector3 vector3)
                        value = NeoGeneratedTypesSupport.Vector3Value(vector3.Value);
                    else if (value is Vector3 unityVector3)
                        value = NeoGeneratedTypesSupport.Vector3Value(unityVector3);
                    break;
                case MemberKind.Vector3Int:
                    if (value is NeoReadOnlyVector3Int vector3Int)
                        value = NeoGeneratedTypesSupport.Vector3IntValue(vector3Int.Value);
                    else if (value is Vector3Int unityVector3Int)
                        value = NeoGeneratedTypesSupport.Vector3IntValue(unityVector3Int);
                    break;
                case MemberKind.Color:
                    if (value is NeoReadOnlyColor color)
                        value = NeoGeneratedTypesSupport.ColorValue(color.Value);
                    else if (value is Color unityColor)
                        value = NeoGeneratedTypesSupport.ColorValue(unityColor);
                    break;
                case MemberKind.Sprite:
                    // The wrapper arm mirrors Color/Vector above, and is what
                    // an author passing a generated sprite property now hands
                    // in: since P42 §4.1 `obj.Portrait` is a NeoSprite, not a
                    // UnityEngine.Sprite. Read the addressable pair off the
                    // wrapper rather than resolving it — the argument is data,
                    // and an unsynchronized asset must not throw on a call
                    // that never needed the Unity object.
                    if (value is NeoReadOnlySprite spriteWrapper)
                        value = NeoGeneratedTypesSupport.SpriteValue(client, spriteWrapper);
                    else if (value is Sprite sprite)
                        value = NeoGeneratedTypesSupport.SpriteValue(client, sprite);
                    if (value is SpriteValue spriteValue)
                    {
                        value = new Dictionary<string, object?>
                        {
                            ["fileId"] = spriteValue.fileId,
                            ["sliceIndex"] = spriteValue.sliceIndex,
                        };
                    }
                    break;
                case MemberKind.Audio:
                    if (value is AudioClip audio)
                        value = NeoGeneratedTypesSupport.AudioValue(client, audio);
                    if (value is FileValue fileValue)
                    {
                        value = new Dictionary<string, object?>
                        {
                            ["fileId"] = fileValue.fileId,
                        };
                    }
                    break;
                case MemberKind.Enum:
                {
                    string[] optionIds = NormalizeEnumOptions(value, subject);
                    if (typeInfo.required && optionIds.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"Required {subject} has no enum option id.");
                    }
                    value = optionIds;
                    break;
                }
                case MemberKind.List:
                case MemberKind.Lookup:
                    value = NormalizeEnumerable(
                        client, ownership, value, typeInfo, ctx, subject);
                    break;
                case MemberKind.DialogueLookup:
                    value = NormalizeDialogueLookup(value, subject);
                    break;
                case MemberKind.Dictionary:
                    value = NormalizeDictionary(
                        client, ownership, value, typeInfo, ctx, subject);
                    break;
            }

            ValidateRuntimeValue(value, typeInfo, subject);
            return value;
        }

        internal static void ValidateRuntimeValue(
            object? value,
            TypeInfo typeInfo,
            string subject)
        {
            if (value is null)
            {
                if (typeInfo.required && typeInfo.type != MemberKind.Null)
                {
                    throw new InvalidOperationException(
                        $"Required {subject} evaluated to null.");
                }
                return;
            }

            bool valid = typeInfo.type switch
            {
                MemberKind.Null => false,
                MemberKind.Bool => value is bool,
                MemberKind.Int => IsIntegralNumber(value),
                MemberKind.Float => IsNumber(value),
                MemberKind.String => value is string,
                MemberKind.Decimal => value is string decimalText
                    && NeoDecimalValues.GetViolation(decimalText)
                        == NeoDecimalValues.Violation.None
                    || value is decimal,
                MemberKind.Enum => value is string
                    || EnumOptionId(value) is not null
                    || value is IEnumerable && value is not string,
                MemberKind.Class or MemberKind.Interface =>
                    value is IDictionary<string, object?>
                    || value is INeoValueReference,
                MemberKind.List or MemberKind.Lookup =>
                    value is IEnumerable && value is not string,
                MemberKind.DialogueLookup =>
                    IsDialogueLookupWireValue(value),
                MemberKind.Dictionary =>
                    value is IDictionary || value is IEnumerable,
                MemberKind.Vector2 or MemberKind.Vector2Int =>
                    value is NeoVector2Value
                    || value is Vector2
                    || value is Vector2Int
                    || value is NeoReadOnlyVector2
                    || value is NeoReadOnlyVector2Int,
                MemberKind.Vector3 or MemberKind.Vector3Int =>
                    value is NeoVector3Value
                    || value is Vector3
                    || value is Vector3Int
                    || value is NeoReadOnlyVector3
                    || value is NeoReadOnlyVector3Int,
                MemberKind.Color => value is NeoColorValue
                    || value is Color
                    || value is NeoReadOnlyColor,
                MemberKind.Sprite => value is SpriteValue
                    || value is Sprite
                    || value is NeoReadOnlySprite
                    || HasDictionaryString(value, "fileId")
                        && HasDictionaryNumber(value, "sliceIndex"),
                MemberKind.Audio => value is FileValue
                    || value is AudioClip
                    || HasDictionaryString(value, "fileId"),
                MemberKind.NSDelegate => value is NeoDelegateValue,
                MemberKind.NSAction => value is NeoActionValue,
                MemberKind.Unknown or MemberKind.Generic => true,
                _ => true,
            };
            if (!valid)
            {
                throw new InvalidOperationException(
                    $"{subject} has runtime type '{value.GetType().Name}', expected {typeInfo.type}.");
            }
        }

        /// <summary>
        /// Completes the structural checks above with the resolved signature's
        /// nominal Class/Interface identity and recursively validates
        /// collection entries. This runs only at public invocation boundaries,
        /// after normalization has canonicalized scalars such as Decimal.
        /// </summary>
        internal static void ValidateResolvedRuntimeValue(
            NeoClient client,
            object? value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx,
            string subject)
        {
            ValidateRuntimeValue(value, typeInfo, subject);
            if (value is null) return;

            switch (typeInfo.type)
            {
                case MemberKind.Class:
                {
                    string? expectedClassId = typeInfo switch
                    {
                        ClassTypeInfo classType => classType.classId,
                        FunctionArgumentTypeInfo argument => argument.classId,
                        _ => null,
                    };
                    if (string.IsNullOrEmpty(expectedClassId))
                    {
                        throw new InvalidOperationException(
                            $"{subject} is missing its declared Class id.");
                    }
                    string? actualClassId =
                        NSGetterEvaluator.FindRowClassIdByReference(value, ctx);
                    if (string.IsNullOrEmpty(actualClassId)
                        || !IsAssignableNeoSchemaClass(
                            client,
                            actualClassId!,
                            expectedClassId!))
                    {
                        throw new InvalidOperationException(
                            $"{subject} has runtime Class '{actualClassId ?? "<unbound>"}', expected '{expectedClassId}'.");
                    }
                    return;
                }
                case MemberKind.Interface:
                {
                    string? interfaceId = typeInfo switch
                    {
                        InterfaceTypeInfo interfaceType => interfaceType.interfaceId,
                        FunctionArgumentTypeInfo argument => argument.interfaceId,
                        _ => null,
                    };
                    string? actualClassId =
                        NSGetterEvaluator.FindRowClassIdByReference(value, ctx);
                    if (string.IsNullOrEmpty(interfaceId)
                        || string.IsNullOrEmpty(actualClassId)
                        || !NeoInterfaceResolution.ClassImplements(
                            actualClassId!,
                            interfaceId!,
                            client.ProjectDataForRuntime))
                    {
                        throw new InvalidOperationException(
                            $"{subject} has runtime Class '{actualClassId ?? "<unbound>"}', which does not implement Interface '{interfaceId ?? "<missing>"}'.");
                    }
                    return;
                }
                case MemberKind.List:
                case MemberKind.Lookup:
                {
                    TypeInfo? entryType = typeInfo switch
                    {
                        FunctionArgumentTypeInfo argument => argument.entryTypeInfo,
                        CollectionTypeInfo collection => collection.entryTypeInfo,
                        LookupTypeInfo lookup => lookup.entryTypeInfo,
                        _ => null,
                    };
                    if (entryType is null) return;
                    int index = 0;
                    foreach (object? entry in (System.Collections.IEnumerable)value)
                    {
                        ValidateResolvedRuntimeValue(
                            client,
                            entry,
                            entryType,
                            ctx,
                            $"entry {index++} of {subject}");
                    }
                    return;
                }
                case MemberKind.Dictionary:
                {
                    TypeInfo? entryType = typeInfo switch
                    {
                        FunctionArgumentTypeInfo argument => argument.entryTypeInfo,
                        CollectionTypeInfo collection => collection.entryTypeInfo,
                        _ => null,
                    };
                    if (entryType is null) return;
                    if (value is not System.Collections.IDictionary dictionary)
                    {
                        throw new InvalidOperationException(
                            $"{subject} did not normalize to a dictionary.");
                    }
                    foreach (System.Collections.DictionaryEntry entry in dictionary)
                    {
                        ValidateResolvedRuntimeValue(
                            client,
                            entry.Value,
                            entryType,
                            ctx,
                            $"key '{entry.Key}' of {subject}");
                    }
                    return;
                }
            }
        }

        private static bool IsAssignableNeoSchemaClass(
            NeoClient client,
            string actualClassId,
            string expectedClassId)
        {
            try
            {
                foreach (NeoSchemaClass schemaClass in NeoSchemaClassInheritance.ResolveChain(
                    actualClassId,
                    id => client.TryGetClass(id, out NeoSchemaClass? candidate)
                        ? candidate
                        : null))
                {
                    if (schemaClass.id == expectedClassId) return true;
                }
            }
            catch (CircularInheritanceError)
            {
                return false;
            }
            return false;
        }

        private static object UnwrapValueReference(
            NeoClient client,
            NeoValueOwnership fallbackOwnership,
            string valueId,
            NSGetterEvaluator.Context ctx,
            string subject)
        {
            if (!client.TryGetValue(
                    fallbackOwnership,
                    valueId,
                    out MemberValue? row))
            {
                throw new InvalidOperationException(
                    $"Neo value '{valueId}' for {subject} was not found in {fallbackOwnership} storage.");
            }
            return NSGetterEvaluator.UnwrapRow(row, ctx, fallbackOwnership)
                ?? throw new InvalidOperationException(
                    $"Neo value '{valueId}' for {subject} resolved to null.");
        }

        private static object?[] NormalizeEnumerable(
            NeoClient client,
            NeoValueOwnership ownership,
            object value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx,
            string subject)
        {
            if (value is string || value is not IEnumerable enumerable)
            {
                throw new InvalidOperationException(
                    $"Expected an enumerable {subject} for {typeInfo.type}.");
            }
            TypeInfo? entryType = typeInfo switch
            {
                FunctionArgumentTypeInfo argument => argument.entryTypeInfo,
                CollectionTypeInfo collection => collection.entryTypeInfo,
                LookupTypeInfo lookup => lookup.entryTypeInfo,
                _ => null,
            };
            var result = new List<object?>();
            foreach (object? entry in enumerable)
            {
                result.Add(entryType is null
                    ? entry
                    : Normalize(
                        client,
                        ownership,
                        entry,
                        entryType,
                        ctx,
                        $"entry of {subject}"));
            }
            return result.ToArray();
        }

        private static object?[] NormalizeDialogueLookup(
            object value,
            string subject)
        {
            if (value is string || value is not IEnumerable enumerable)
            {
                throw new InvalidOperationException(
                    $"{subject} must be exactly one dialogue reference.");
            }
            var result = new List<object?>();
            foreach (object? entry in enumerable)
            {
                string? dialogueId = entry switch
                {
                    string id => id,
                    NeoDialogueReference reference => reference.Id,
                    _ => null,
                };
                if (string.IsNullOrEmpty(dialogueId))
                {
                    throw new InvalidOperationException(
                        $"{subject} contains an invalid dialogue reference.");
                }
                result.Add(dialogueId);
            }
            if (result.Count != 1)
            {
                throw new InvalidOperationException(
                    $"{subject} must contain exactly one dialogue reference.");
            }
            return result.ToArray();
        }

        private static bool IsDialogueLookupWireValue(object value)
        {
            if (value is string || value is not IEnumerable enumerable)
            {
                return false;
            }
            int count = 0;
            foreach (object? entry in enumerable)
            {
                if (entry is not string || ++count > 1) return false;
            }
            return count == 1;
        }

        private static Dictionary<string, object?> NormalizeDictionary(
            NeoClient client,
            NeoValueOwnership ownership,
            object value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx,
            string subject)
        {
            TypeInfo? entryType = typeInfo switch
            {
                FunctionArgumentTypeInfo argument => argument.entryTypeInfo,
                CollectionTypeInfo collection => collection.entryTypeInfo,
                _ => null,
            };
            var result = new Dictionary<string, object?>();
            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    AddDictionaryEntry(
                        result,
                        entry.Key,
                        entry.Value,
                        entryType,
                        client,
                        ownership,
                        ctx,
                        subject);
                }
                return result;
            }
            if (value is IEnumerable entries && value is not string)
            {
                foreach (object? entry in entries)
                {
                    if (entry is null) continue;
                    Type entryTypeInfo = entry.GetType();
                    var keyProperty = entryTypeInfo.GetProperty("Key");
                    var valueProperty = entryTypeInfo.GetProperty("Value");
                    if (keyProperty is null || valueProperty is null)
                    {
                        throw new InvalidOperationException(
                            $"Dictionary {subject} entry '{entryTypeInfo.FullName}' does not expose Key/Value properties.");
                    }
                    AddDictionaryEntry(
                        result,
                        keyProperty.GetValue(entry),
                        valueProperty.GetValue(entry),
                        entryType,
                        client,
                        ownership,
                        ctx,
                        subject);
                }
                return result;
            }
            throw new InvalidOperationException(
                $"Expected a dictionary {subject}.");
        }

        private static void AddDictionaryEntry(
            Dictionary<string, object?> result,
            object? keyValue,
            object? value,
            TypeInfo? entryType,
            NeoClient client,
            NeoValueOwnership ownership,
            NSGetterEvaluator.Context ctx,
            string subject)
        {
            string key = EnumOptionId(keyValue)
                ?? keyValue?.ToString()
                ?? "null";
            result[key] = entryType is null
                ? value
                : Normalize(
                    client,
                    ownership,
                    value,
                    entryType,
                    ctx,
                    $"dictionary value of {subject}");
        }

        internal static string? EnumOptionId(object? value)
        {
            if (value is string text) return text;
            var property = value?.GetType().GetProperty(
                "optionId",
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public);
            return property?.PropertyType == typeof(string)
                ? property.GetValue(value) as string
                : null;
        }

        private static string[] NormalizeEnumOptions(
            object value,
            string subject)
        {
            if (value is string text) return new[] { text };
            string? optionId = EnumOptionId(value);
            if (optionId is not null) return new[] { optionId };
            if (value is not IEnumerable enumerable)
            {
                throw new InvalidOperationException(
                    $"{subject} must be an enum option or option-id collection.");
            }

            var result = new List<string>();
            foreach (object? entry in enumerable)
            {
                string? entryId = entry as string ?? EnumOptionId(entry);
                if (entryId is null)
                {
                    throw new InvalidOperationException(
                        $"{subject} contains an entry without an enum option id.");
                }
                result.Add(entryId);
            }
            return result.ToArray();
        }

        private static bool IsNumber(object value)
        {
            return value switch
            {
                double number => !double.IsNaN(number)
                    && !double.IsInfinity(number),
                float number => !float.IsNaN(number)
                    && !float.IsInfinity(number),
                int or long or short => true,
                _ => false,
            };
        }

        private static bool IsIntegralNumber(object value)
        {
            return value switch
            {
                int or long or short => true,
                double number => !double.IsNaN(number)
                    && !double.IsInfinity(number)
                    && number == Math.Truncate(number),
                float number => !float.IsNaN(number)
                    && !float.IsInfinity(number)
                    && number == Math.Truncate(number),
                _ => false,
            };
        }

        private static bool HasDictionaryString(object value, string key)
        {
            return value is IDictionary<string, object?> dictionary
                && dictionary.TryGetValue(key, out object? field)
                && field is string;
        }

        private static bool HasDictionaryNumber(object value, string key)
        {
            return value is IDictionary<string, object?> dictionary
                && dictionary.TryGetValue(key, out object? field)
                && IsIntegralNumber(field!);
        }
    }
}
