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
using JsonAttribute = NeoCompose.Runtime.Json.Attribute;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Runtime wrapper for a NeoScript-backed Function attribute. Immediate
    /// functions execute synchronously; deferred functions expose a Task that
    /// owns every nested native/NeoScript continuation until settlement.
    /// </summary>
    public sealed class NeoAttributeNSFunction
        : NeoAttribute<NSFunctionAttribute, NullAttributeValue>
    {
        public NeoAttributeNSFunction(
            NeoClient client,
            string attributeId,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeNSFunction(
            NeoClient client,
            NSFunctionAttribute attribute,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

        public FunctionWithReturnType? resolvedAction =>
            NeoNSFunctionRuntime.TryResolve(client, attribute.id)?.Action;

        public TypeInfo? resolvedReturnTypeInfo =>
            NeoNSFunctionRuntime.TryResolve(client, attribute.id)?.ReturnTypeInfo;

        public FunctionArgumentTypeInfo[]? resolvedArgumentTypes =>
            NeoNSFunctionRuntime.TryResolve(client, attribute.id)?.ArgumentTypes;

        public bool resolvedDeferred =>
            NeoNSFunctionRuntime.TryResolve(client, attribute.id)?.Deferred ?? false;

        public object? Invoke(string thisValueId, object?[] args)
        {
            Invocation invocation = PrepareInvocation(thisValueId, args);
            if (invocation.Function.Deferred)
            {
                throw new InvalidOperationException(
                    $"NSFunction '{invocation.Function.Attribute.name}' is deferred; use InvokeAsync.");
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
                    $"Non-deferred NSFunction '{invocation.Function.Attribute.name}' suspended; its compiled IR is stale or corrupt.");
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
                        $"NSFunction '{invocation.Function.Attribute.name}' is immediate; use Invoke.");
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
            if (!client.TryGetValue(ownership, thisValueId, out AttributeValue? row))
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
                    $"NSFunction '{attribute.name}' cannot be invoked on a null receiver.");
            }

            string effectiveAttributeId = NSGetterEvaluator.ResolveFunctionAttributeId(
                new CallFunctionPointer
                {
                    type = PointerKind.CallFunction,
                    attributeId = attribute.id,
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
                NeoNSFunctionRuntime.ResolveSignature(client, effectiveAttributeId),
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
                    $"NSFunction '{invocation.Function.Attribute.name}' is deferred; use InvokeStaticAsync.");
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
                    $"Non-deferred static NSFunction '{invocation.Function.Attribute.name}' suspended; its compiled IR is stale or corrupt.");
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
                        $"NSFunction '{invocation.Function.Attribute.name}' is immediate; use InvokeStatic.");
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
                attribute.id);
            if (!function.Attribute.isStatic)
            {
                throw new NSGetterRuntimeError(
                    $"NSFunction '{function.Attribute.name}' is an instance member and requires a receiver.");
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
            string attributeId,
            NSFunctionAttribute attribute,
            FunctionWithReturnType action,
            TypeInfo returnTypeInfo,
            FunctionArgumentTypeInfo[] argumentTypes,
            bool deferred)
        {
            AttributeId = attributeId;
            Attribute = attribute;
            Action = action;
            ReturnTypeInfo = returnTypeInfo;
            ArgumentTypes = argumentTypes;
            Deferred = deferred;
        }

        internal string AttributeId { get; }
        internal NSFunctionAttribute Attribute { get; }
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
            string attributeId,
            object? receiver,
            object?[] args,
            NSGetterEvaluator.Context ctx)
        {
            NeoResolvedNSFunction function = ResolveSignature(client, attributeId);
            if (function.Deferred)
            {
                throw new NeoDeferredFunctionRuntimeError(
                    $"NSFunction '{function.Attribute.name}' ({attributeId}) deferred-mode mismatch: " +
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
                    $"Non-deferred NSFunction '{function.Attribute.name}' suspended; its compiled IR is stale or corrupt.");
            }
            return result.ReturnValue;
        }

        internal static NeoScriptExecutionResult Execute(
            NeoClient client,
            string attributeId,
            object? receiver,
            object?[] args,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionOptions options)
        {
            return ExecuteResolved(
                client,
                ResolveSignature(client, attributeId),
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
            bool isStatic = function.Attribute.isStatic;
            if (receiver is null && !isStatic)
            {
                throw new NSGetterRuntimeError(
                    $"Cannot invoke NSFunction '{function.Attribute.name}' on a null receiver.");
            }
            if (receiver is not null && isStatic)
            {
                throw new NSGetterRuntimeError(
                    $"Static NSFunction '{function.Attribute.name}' must be invoked without an instance receiver.");
            }
            args ??= Array.Empty<object?>();
            if (args.Length != function.ArgumentTypes.Length)
            {
                throw new NSGetterRuntimeError(
                    $"NSFunction '{function.Attribute.name}' ({function.AttributeId}) expects " +
                    $"{function.ArgumentTypes.Length} arguments but received {args.Length}; " +
                    "compiled call IR or caller is stale/corrupt.");
            }
            if (ctx.functionCallStack.Count >= MaxCallableDepth)
            {
                var names = new List<string>(ctx.functionCallStack.Count + 1);
                foreach (string id in ctx.functionCallStack)
                {
                    names.Add(client.TryGetAttribute(id, out JsonAttribute? item)
                        ? item.name
                        : id);
                }
                names.Add(function.Attribute.name);
                throw new NSGetterRuntimeError(
                    $"NeoScript Function call depth exceeded {MaxCallableDepth}: {string.Join(" -> ", names)}.");
            }

            FunctionWithReturnType action = function.Action;
            int receiverParameterCount = isStatic ? 1 : 2;
            int expectedParameters = function.ArgumentTypes.Length + receiverParameterCount;
            if (action.parameters is null || action.parameters.Length != expectedParameters)
            {
                throw new NSGetterRuntimeError(
                    $"NSFunction '{function.Attribute.name}' compiled action parameter count is stale (expected {expectedParameters}, found {action.parameters?.Length ?? 0}).");
            }

            TypeInfo effectiveReturnType = function.ReturnTypeInfo;
            TypeInfo[] effectiveArgumentTypes = function.ArgumentTypes;
            if (isStatic
                && (ContainsGeneric(function.ReturnTypeInfo)
                    || Array.Exists(function.ArgumentTypes, ContainsGeneric)))
            {
                throw new NSGetterRuntimeError(
                    $"Static NSFunction '{function.Attribute.name}' cannot use receiver-bound Generic signature types.");
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
                        $"argument {i} '{argument.name}' of NSFunction '{function.Attribute.name}'");
                }
                catch (Exception exception)
                {
                    throw new NSGetterRuntimeError(
                        $"NSFunction '{function.Attribute.name}' ({function.AttributeId}) argument {i} " +
                        $"'{argument.name}' is incompatible with declared {argument.type}; " +
                        "compiled call IR or caller is stale/corrupt: " +
                        exception.Message);
                }
            }

            NSGetterEvaluator.Context nestedCtx = ctx
                .WithFunctionPushed(function.AttributeId)
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
                || effectiveReturnType.type == AttributeType.Void)
            {
                if (execution.ReturnValue is not null)
                {
                    throw new NSGetterRuntimeError(
                        $"Void NSFunction '{function.Attribute.name}' returned a value; its compiled IR is stale or corrupt.");
                }
                return NeoScriptExecutionResult.Completed(
                    execution.Returned,
                    returnValue: null);
            }
            if (!execution.Returned)
            {
                throw new NSGetterRuntimeError(
                    $"NSFunction '{function.Attribute.name}' ended without returning a value; its compiled IR is stale or corrupt.");
            }
            string subject =
                $"return value of NSFunction '{function.Attribute.name}'";
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
            if (typeInfo.type == AttributeType.Generic) return true;
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
                CustomTypeInfo custom => custom.typeArguments,
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
            string? runtimeTypeId = NSGetterEvaluator.FindRowTypeIdByReference(
                receiver,
                ctx);
            if (string.IsNullOrEmpty(runtimeTypeId))
            {
                throw new NSGetterRuntimeError(
                    $"NSFunction '{function.Attribute.name}' uses generic signature types, but its receiver has no runtime custom type.");
            }

            string? receiverValueId = NSGetterEvaluator.FindRowIdByReference(
                receiver,
                ctx);
            string? cacheKey = string.IsNullOrEmpty(receiverValueId)
                ? null
                : runtimeTypeId + "\n" + receiverValueId;
            if (cacheKey is not null
                && ctx.genericEnvironmentCache.TryGetValue(
                    cacheKey, out IReadOnlyDictionary<
                        string, NeoGenericEnvEntry>? cached))
            {
                return cached;
            }

            IReadOnlyDictionary<string, GenericBinding>? constructedArguments = null;
            if (!string.IsNullOrEmpty(receiverValueId)
                && client.TryInferAttributeForValueId(
                    receiverValueId!,
                    out JsonAttribute? placementAttribute)
                && placementAttribute is CustomAttribute customPlacement)
            {
                constructedArguments = customPlacement.customTypeArguments;
            }

            try
            {
                IReadOnlyDictionary<string, NeoGenericEnvEntry> resolved =
                    NeoGenericResolution.ResolveInstanceEnv(
                    client,
                    runtimeTypeId!,
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
                    $"NSFunction '{function.Attribute.name}' could not resolve the receiver's generic environment: {exception.Message}");
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
            HashSet<string> visitingAttributes)
        {
            if (typeInfo.type == AttributeType.Generic)
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
                    || string.IsNullOrEmpty(binding.attributeId))
                {
                    throw new NSGetterRuntimeError(
                        $"Generic NSFunction type '{genericParamId ?? "<missing>"}' is unbound for this receiver.");
                }
                if (!client.TryGetAttribute(
                    binding.attributeId!,
                    out JsonAttribute? bindingAttribute))
                {
                    throw new NSGetterRuntimeError(
                        $"Generic NSFunction type '{genericParamId}' references missing binding attribute '{binding.attributeId}'.");
                }
                return TypeInfoFromBindingAttribute(
                    client,
                    bindingAttribute,
                    genericEnv,
                    visitingAttributes);
            }

            if (typeInfo.type == AttributeType.Custom)
            {
                string? typeId = typeInfo switch
                {
                    FunctionArgumentTypeInfo argument => argument.typeId,
                    CustomTypeInfo custom => custom.typeId,
                    _ => null,
                };
                Dictionary<string, TypeInfo>? typeArguments = typeInfo switch
                {
                    FunctionArgumentTypeInfo argument => argument.typeArguments,
                    CustomTypeInfo custom => custom.typeArguments,
                    _ => null,
                };
                if (string.IsNullOrEmpty(typeId))
                {
                    throw new NSGetterRuntimeError(
                        "Custom NSFunction type is missing its typeId.");
                }
                return new CustomTypeInfo
                {
                    type = AttributeType.Custom,
                    required = typeInfo.required,
                    typeId = typeId!,
                    typeArguments = ResolveInvocationTypeArguments(
                        client,
                        typeArguments,
                        genericEnv,
                        visitingAttributes),
                };
            }

            TypeInfo? entryTypeInfo = typeInfo switch
            {
                FunctionArgumentTypeInfo argument => argument.entryTypeInfo,
                CollectionTypeInfo collection => collection.entryTypeInfo,
                LookupTypeInfo lookup => lookup.entryTypeInfo,
                _ => null,
            };
            if (typeInfo.type is AttributeType.List or AttributeType.Dictionary
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
                    listAttributeId = typeInfo switch
                    {
                        FunctionArgumentTypeInfo argument => argument.listAttributeId,
                        CollectionTypeInfo collection => collection.listAttributeId,
                        _ => null,
                    },
                    entryTypeInfo = ResolveInvocationTypeInfo(
                        client,
                        entryTypeInfo,
                        genericEnv,
                        visitingAttributes),
                };
            }
            if (typeInfo.type == AttributeType.Lookup
                && entryTypeInfo is not null
                && ContainsGeneric(entryTypeInfo))
            {
                string? collectionAttributeId = typeInfo switch
                {
                    FunctionArgumentTypeInfo argument =>
                        argument.collectionAttributeId,
                    LookupTypeInfo lookup => lookup.collectionAttributeId,
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
                    type = AttributeType.Lookup,
                    required = typeInfo.required,
                    collectionAttributeId = collectionAttributeId ?? "",
                    collectionValueId = collectionValueId,
                    entryTypeInfo = ResolveInvocationTypeInfo(
                        client,
                        entryTypeInfo,
                        genericEnv,
                        visitingAttributes),
                };
            }
            return typeInfo;
        }

        private static Dictionary<string, TypeInfo>?
            ResolveInvocationTypeArguments(
                NeoClient client,
                IReadOnlyDictionary<string, TypeInfo>? typeArguments,
                IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
                HashSet<string> visitingAttributes)
        {
            if (typeArguments is null) return null;
            var resolved = new Dictionary<string, TypeInfo>(typeArguments.Count);
            foreach (var pair in typeArguments)
            {
                resolved[pair.Key] = ResolveInvocationTypeInfo(
                    client,
                    pair.Value,
                    genericEnv,
                    visitingAttributes);
            }
            return resolved;
        }

        private static TypeInfo TypeInfoFromBindingAttribute(
            NeoClient client,
            JsonAttribute attribute,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
            HashSet<string> visitingAttributes)
        {
            if (!visitingAttributes.Add(attribute.id))
            {
                throw new NSGetterRuntimeError(
                    $"Generic NSFunction binding attribute cycle detected at '{attribute.id}'.");
            }
            try
            {
                switch (attribute)
                {
                    case GenericAttribute generic:
                        return ResolveInvocationTypeInfo(
                            client,
                            new GenericTypeInfo
                            {
                                type = AttributeType.Generic,
                                required = generic.required,
                                genericParamId = generic.genericParamId,
                            },
                            genericEnv,
                            visitingAttributes);
                    case CustomAttribute custom:
                        return new CustomTypeInfo
                        {
                            type = AttributeType.Custom,
                            required = custom.required,
                            typeId = custom.customTypeId,
                            typeArguments = ResolveBindingTypeArguments(
                                client,
                                custom.customTypeArguments,
                                genericEnv,
                                visitingAttributes),
                        };
                    case EnumAttribute enumAttribute:
                        return new EnumTypeInfo
                        {
                            type = AttributeType.Enum,
                            required = enumAttribute.required,
                            enumId = enumAttribute.enumId,
                        };
                    case ListAttribute list:
                        return CollectionBindingTypeInfo(
                            client,
                            list,
                            list.entryAttributeId,
                            genericEnv,
                            visitingAttributes);
                    case DictionaryAttribute dictionary:
                        return CollectionBindingTypeInfo(
                            client,
                            dictionary,
                            dictionary.entryAttributeId,
                            genericEnv,
                            visitingAttributes);
                    case LookupAttribute lookup:
                    {
                        if (!client.TryGetAttribute(
                                lookup.collectionAttributeId,
                                out ListAttribute? collection)
                            || !client.TryGetAttribute(
                                collection.entryAttributeId,
                                out JsonAttribute? entryAttribute))
                        {
                            throw new NSGetterRuntimeError(
                                $"Generic NSFunction Lookup binding '{lookup.id}' has a missing collection entry type.");
                        }
                        return new LookupTypeInfo
                        {
                            type = AttributeType.Lookup,
                            required = lookup.required,
                            collectionAttributeId = lookup.collectionAttributeId,
                            collectionValueId = lookup.collectionValueId,
                            entryTypeInfo = TypeInfoFromBindingAttribute(
                                client,
                                entryAttribute,
                                genericEnv,
                                visitingAttributes),
                        };
                    }
                    case NullAttribute:
                    case BoolAttribute:
                    case IntAttribute:
                    case FloatAttribute:
                    case StringAttribute:
                    case SpriteAttribute:
                    case AudioAttribute:
                    case Vector2Attribute:
                    case Vector2IntAttribute:
                    case Vector3Attribute:
                    case Vector3IntAttribute:
                    case ColorAttribute:
                    case DecimalAttribute:
                        return new PrimitiveTypeInfo
                        {
                            type = attribute.type,
                            required = attribute.required,
                        };
                    default:
                        throw new NSGetterRuntimeError(
                            $"Attribute '{attribute.name}' ({attribute.id}) of type {attribute.type} is not a valid concrete generic NSFunction binding.");
                }
            }
            finally
            {
                visitingAttributes.Remove(attribute.id);
            }
        }

        private static Dictionary<string, TypeInfo>? ResolveBindingTypeArguments(
            NeoClient client,
            IReadOnlyDictionary<string, GenericBinding>? bindings,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
            HashSet<string> visitingAttributes)
        {
            if (bindings is null) return null;
            var resolved = new Dictionary<string, TypeInfo>(bindings.Count);
            foreach (var pair in bindings)
            {
                GenericBinding binding = pair.Value;
                string? attributeId;
                if (binding.kind == NeoGenericBindingKinds.Attribute)
                {
                    attributeId = binding.attributeId;
                }
                else if (binding.kind == NeoGenericBindingKinds.Generic
                    && !string.IsNullOrEmpty(binding.genericParamId)
                    && genericEnv.TryGetValue(
                        binding.genericParamId!,
                        out NeoGenericEnvEntry? forwarded)
                    && forwarded.IsBound)
                {
                    attributeId = forwarded.attributeId;
                }
                else
                {
                    throw new NSGetterRuntimeError(
                        $"Constructed Custom NSFunction type argument '{pair.Key}' is unbound.");
                }
                if (string.IsNullOrEmpty(attributeId)
                    || !client.TryGetAttribute(
                        attributeId!,
                        out JsonAttribute? bindingAttribute))
                {
                    throw new NSGetterRuntimeError(
                        $"Constructed Custom NSFunction type argument '{pair.Key}' references missing binding attribute '{attributeId ?? "<missing>"}'.");
                }
                resolved[pair.Key] = TypeInfoFromBindingAttribute(
                    client,
                    bindingAttribute,
                    genericEnv,
                    visitingAttributes);
            }
            return resolved;
        }

        private static CollectionTypeInfo CollectionBindingTypeInfo(
            NeoClient client,
            JsonAttribute collection,
            string entryAttributeId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
            HashSet<string> visitingAttributes)
        {
            if (!client.TryGetAttribute(
                entryAttributeId,
                out JsonAttribute? entryAttribute))
            {
                throw new NSGetterRuntimeError(
                    $"Generic NSFunction collection binding '{collection.id}' references missing entry attribute '{entryAttributeId}'.");
            }
            return new CollectionTypeInfo
            {
                type = collection.type,
                required = collection.required,
                keyEnumId = (collection as DictionaryAttribute)?.keyEnumId,
                listAttributeId = collection is ListAttribute ? collection.id : null,
                entryTypeInfo = TypeInfoFromBindingAttribute(
                    client,
                    entryAttribute,
                    genericEnv,
                    visitingAttributes),
            };
        }

        internal static NeoResolvedNSFunction ResolveSignature(
            NeoClient client,
            string attributeId)
        {
            return TryResolve(client, attributeId)
                ?? throw new NSGetterRuntimeError(
                    $"NSFunction '{attributeId}' has a broken override chain, missing signature, or missing compiled action.");
        }

        internal static NeoResolvedNSFunction? TryResolve(
            NeoClient client,
            string attributeId)
        {
            if (client.TryGetResolvedNSFunction(
                    attributeId,
                    out NeoResolvedNSFunction? cached))
            {
                return cached;
            }
            var visited = new HashSet<string>();
            string? cursor = attributeId;
            NSFunctionAttribute? effectiveAttribute = null;
            FunctionWithReturnType? action = null;
            TypeInfo? returnTypeInfo = null;
            FunctionArgumentTypeInfo[]? argumentTypes = null;
            bool? deferred = null;
            for (int hops = 0; !string.IsNullOrEmpty(cursor) && hops < 32; hops++)
            {
                if (!visited.Add(cursor!))
                {
                    throw new NSGetterRuntimeError(
                        $"Circular NSFunction override chain detected at '{cursor}' while resolving '{attributeId}'.");
                }
                if (!client.TryGetAttribute(cursor!, out NSFunctionAttribute? current))
                {
                    return null;
                }
                effectiveAttribute ??= current;
                action ??= current.action;
                returnTypeInfo ??= current.returnTypeInfo;
                argumentTypes ??= current.argumentTypes;
                deferred ??= current.deferred;
                if (action is not null
                    && returnTypeInfo is not null
                    && argumentTypes is not null
                    && deferred.HasValue)
                {
                    return client.CacheResolvedNSFunction(
                        new NeoResolvedNSFunction(
                            attributeId,
                            effectiveAttribute,
                            action,
                            returnTypeInfo,
                            argumentTypes,
                            deferred.Value));
                }
                cursor = current.extendsAttributeId;
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
                ["Assets"] = client.assets.value is ObjectAttributeValue assets
                    ? NSGetterEvaluator.UnwrapRow(assets, ctx, NeoValueOwnership.Asset)
                    : null,
                ["Save"] = client.save.value is ObjectAttributeValue save
                    ? NSGetterEvaluator.UnwrapRow(save, ctx, NeoValueOwnership.Save)
                    : null,
                ["Session"] = client.session.value is ObjectAttributeValue session
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
                if (typeInfo.required && typeInfo.type != AttributeType.Null)
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
                value = typeInfo.type == AttributeType.DialogueLookup
                    ? new object?[] { dialogueReference.Id }
                    : dialogueReference.Id;
            }
            else if (typeInfo.type == AttributeType.Generic
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
                    value is NeoGeneratedCustomValue generated
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
                case AttributeType.Decimal:
                    if (value is decimal decimalValue)
                        value = NeoDecimalValues.Format(decimalValue);
                    else if (value is double or float or int or long or short)
                        value = NSGetterEvaluator.CoerceDecimalOperand(value, subject);
                    break;
                case AttributeType.Vector2:
                    if (value is NeoReadOnlyVector2 vector2)
                        value = NeoGeneratedTypesSupport.Vector2Value(vector2.Value);
                    else if (value is Vector2 unityVector2)
                        value = NeoGeneratedTypesSupport.Vector2Value(unityVector2);
                    break;
                case AttributeType.Vector2Int:
                    if (value is NeoReadOnlyVector2Int vector2Int)
                        value = NeoGeneratedTypesSupport.Vector2IntValue(vector2Int.Value);
                    else if (value is Vector2Int unityVector2Int)
                        value = NeoGeneratedTypesSupport.Vector2IntValue(unityVector2Int);
                    break;
                case AttributeType.Vector3:
                    if (value is NeoReadOnlyVector3 vector3)
                        value = NeoGeneratedTypesSupport.Vector3Value(vector3.Value);
                    else if (value is Vector3 unityVector3)
                        value = NeoGeneratedTypesSupport.Vector3Value(unityVector3);
                    break;
                case AttributeType.Vector3Int:
                    if (value is NeoReadOnlyVector3Int vector3Int)
                        value = NeoGeneratedTypesSupport.Vector3IntValue(vector3Int.Value);
                    else if (value is Vector3Int unityVector3Int)
                        value = NeoGeneratedTypesSupport.Vector3IntValue(unityVector3Int);
                    break;
                case AttributeType.Color:
                    if (value is NeoReadOnlyColor color)
                        value = NeoGeneratedTypesSupport.ColorValue(color.Value);
                    else if (value is Color unityColor)
                        value = NeoGeneratedTypesSupport.ColorValue(unityColor);
                    break;
                case AttributeType.Sprite:
                    if (value is Sprite sprite)
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
                case AttributeType.Audio:
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
                case AttributeType.Enum:
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
                case AttributeType.List:
                case AttributeType.Lookup:
                    value = NormalizeEnumerable(
                        client, ownership, value, typeInfo, ctx, subject);
                    break;
                case AttributeType.DialogueLookup:
                    value = NormalizeDialogueLookup(value, subject);
                    break;
                case AttributeType.Dictionary:
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
                if (typeInfo.required && typeInfo.type != AttributeType.Null)
                {
                    throw new InvalidOperationException(
                        $"Required {subject} evaluated to null.");
                }
                return;
            }

            bool valid = typeInfo.type switch
            {
                AttributeType.Null => false,
                AttributeType.Bool => value is bool,
                AttributeType.Int => IsIntegralNumber(value),
                AttributeType.Float => IsNumber(value),
                AttributeType.String => value is string,
                AttributeType.Decimal => value is string decimalText
                    && NeoDecimalValues.GetViolation(decimalText)
                        == NeoDecimalValues.Violation.None
                    || value is decimal,
                AttributeType.Enum => value is string
                    || EnumOptionId(value) is not null
                    || value is IEnumerable && value is not string,
                AttributeType.Custom or AttributeType.Interface =>
                    value is IDictionary<string, object?>
                    || value is INeoValueReference,
                AttributeType.List or AttributeType.Lookup =>
                    value is IEnumerable && value is not string,
                AttributeType.DialogueLookup =>
                    IsDialogueLookupWireValue(value),
                AttributeType.Dictionary =>
                    value is IDictionary || value is IEnumerable,
                AttributeType.Vector2 or AttributeType.Vector2Int =>
                    value is NeoVector2Value
                    || value is Vector2
                    || value is Vector2Int
                    || value is NeoReadOnlyVector2
                    || value is NeoReadOnlyVector2Int,
                AttributeType.Vector3 or AttributeType.Vector3Int =>
                    value is NeoVector3Value
                    || value is Vector3
                    || value is Vector3Int
                    || value is NeoReadOnlyVector3
                    || value is NeoReadOnlyVector3Int,
                AttributeType.Color => value is NeoColorValue
                    || value is Color
                    || value is NeoReadOnlyColor,
                AttributeType.Sprite => value is SpriteValue
                    || value is Sprite
                    || HasDictionaryString(value, "fileId")
                        && HasDictionaryNumber(value, "sliceIndex"),
                AttributeType.Audio => value is FileValue
                    || value is AudioClip
                    || HasDictionaryString(value, "fileId"),
                AttributeType.Unknown or AttributeType.Generic => true,
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
        /// nominal Custom/Interface identity and recursively validates
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
                case AttributeType.Custom:
                {
                    string? expectedTypeId = typeInfo switch
                    {
                        CustomTypeInfo custom => custom.typeId,
                        FunctionArgumentTypeInfo argument => argument.typeId,
                        _ => null,
                    };
                    if (string.IsNullOrEmpty(expectedTypeId))
                    {
                        throw new InvalidOperationException(
                            $"{subject} is missing its declared Custom type id.");
                    }
                    string? actualTypeId =
                        NSGetterEvaluator.FindRowTypeIdByReference(value, ctx);
                    if (string.IsNullOrEmpty(actualTypeId)
                        || !IsAssignableCustomType(
                            client,
                            actualTypeId!,
                            expectedTypeId!))
                    {
                        throw new InvalidOperationException(
                            $"{subject} has runtime Custom type '{actualTypeId ?? "<unbound>"}', expected '{expectedTypeId}'.");
                    }
                    return;
                }
                case AttributeType.Interface:
                {
                    string? interfaceId = typeInfo switch
                    {
                        InterfaceTypeInfo interfaceType => interfaceType.interfaceId,
                        FunctionArgumentTypeInfo argument => argument.interfaceId,
                        _ => null,
                    };
                    string? actualTypeId =
                        NSGetterEvaluator.FindRowTypeIdByReference(value, ctx);
                    if (string.IsNullOrEmpty(interfaceId)
                        || string.IsNullOrEmpty(actualTypeId)
                        || !NeoInterfaceResolution.TypeImplements(
                            actualTypeId!,
                            interfaceId!,
                            client.ProjectDataForRuntime))
                    {
                        throw new InvalidOperationException(
                            $"{subject} has runtime Custom type '{actualTypeId ?? "<unbound>"}', which does not implement Interface '{interfaceId ?? "<missing>"}'.");
                    }
                    return;
                }
                case AttributeType.List:
                case AttributeType.Lookup:
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
                case AttributeType.Dictionary:
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

        private static bool IsAssignableCustomType(
            NeoClient client,
            string actualTypeId,
            string expectedTypeId)
        {
            try
            {
                foreach (CustomType type in CustomTypeInheritance.ResolveChain(
                    actualTypeId,
                    id => client.TryGetType(id, out CustomType? candidate)
                        ? candidate
                        : null))
                {
                    if (type.id == expectedTypeId) return true;
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
                    out AttributeValue? row))
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
