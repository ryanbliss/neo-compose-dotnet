// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        /// <summary>
        /// Returns the client-owned wrapper for a static interpreted function.
        /// Schema invalidation resolves a new wrapper on the next call.
        /// </summary>
        public static NeoMemberNSFunction GetOrCreateStatic(NeoClient client, string memberId)
        {
            var function = NeoNSFunctionRuntime.ResolveSignature(client, memberId);
            if (!function.IsStatic)
                throw new NSGetterRuntimeError(
                    $"NSFunction '{function.Member.name}' is an instance member and requires a receiver.");
            if (function.StaticNode is null || function.StaticNode.isDisposed)
            {
                function.StaticNode = new NeoMemberNSFunction(client, function.Member, null, NeoValueOwnership.Session)
                {
                    resolved = function,
                };
            }
            return function.StaticNode;
        }

        // This node's resolved function, current while the client's schema
        // resolution is the one it was resolved under.
        private NeoResolvedNSFunction? resolved;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private NeoResolvedNSFunction ResolveFunction()
        {
            NeoResolvedNSFunction? function = resolved;
            if (function is not null && ReferenceEquals(function.SchemaResolution, client.SchemaResolution))
                return function;
            return resolved = NeoNSFunctionRuntime.ResolveSignature(client, member.id);
        }

        public FunctionWithReturnType? resolvedAction =>
            NeoNSFunctionRuntime.TryResolve(client, member.id)?.Action;

        public TypeInfo? resolvedReturnTypeInfo =>
            NeoNSFunctionRuntime.TryResolve(client, member.id)?.ReturnTypeInfo;

        public FunctionArgumentTypeInfo[]? resolvedArgumentTypes =>
            NeoNSFunctionRuntime.TryResolve(client, member.id)?.ArgumentTypes;

        public bool resolvedDeferred =>
            NeoNSFunctionRuntime.TryResolve(client, member.id)?.Deferred ?? false;

        // Generated static calls pass their arguments positionally, so a
        // call allocates no argument array: the scope copies the arguments in.
        public object? InvokeStatic() => InvokeStatic(Array.Empty<object?>());

        public object? InvokeStatic(object? arg0)
        {
            object?[] args = NeoArgumentArrays.Rent(1);
            args[0] = arg0;
            try
            {
                return InvokeStatic(args);
            }
            finally
            {
                NeoArgumentArrays.Return(args);
            }
        }

        public object? InvokeStatic(object? arg0, object? arg1)
        {
            object?[] args = NeoArgumentArrays.Rent(2);
            args[0] = arg0;
            args[1] = arg1;
            try
            {
                return InvokeStatic(args);
            }
            finally
            {
                NeoArgumentArrays.Return(args);
            }
        }

        public object? InvokeStatic(object? arg0, object? arg1, object? arg2)
        {
            object?[] args = NeoArgumentArrays.Rent(3);
            args[0] = arg0;
            args[1] = arg1;
            args[2] = arg2;
            try
            {
                return InvokeStatic(args);
            }
            finally
            {
                NeoArgumentArrays.Return(args);
            }
        }

        public object? InvokeStatic(object? arg0, object? arg1, object? arg2, object? arg3)
        {
            object?[] args = NeoArgumentArrays.Rent(4);
            args[0] = arg0;
            args[1] = arg1;
            args[2] = arg2;
            args[3] = arg3;
            try
            {
                return InvokeStatic(args);
            }
            finally
            {
                NeoArgumentArrays.Return(args);
            }
        }

        /// <param name="typeArguments">A generic function's type arguments; null binds its defaults.</param>
        public object? Invoke(string thisValueId, object?[] args, TypeInfo[]? typeArguments = null)
        {
            args ??= Array.Empty<object?>();
            MemberValue row = ReceiverRow(thisValueId);
            NSGetterEvaluator.Context ctx = client.RentDirectFunctionContext(ownership, function: true);
            object receiver = UnwrapReceiver(row, ctx);
            NeoResolvedNSFunction function = ResolveInstanceFunction(receiver, row, ctx);
            if (function.Deferred)
            {
                throw new InvalidOperationException(
                    $"NSFunction '{function.Member.name}' is deferred; use InvokeAsync.");
            }

            NeoScriptExecutionResult result = NeoNSFunctionRuntime.ExecuteResolved(
                client,
                function,
                receiver,
                args,
                ctx,
                NeoScriptExecutionOptions.ForImmediate(client), ownsContext: true, typeArguments: typeArguments);
            if (result.IsPaused)
            {
                result.Deferred?.DisposeFromOwner(
                    "synchronous NSFunction invocation suspended");
                throw new NSGetterRuntimeError(
                    $"Non-deferred NSFunction '{function.Member.name}' suspended; its compiled IR is stale or corrupt.");
            }
            client.ReturnDirectFunctionContext(ctx, result.ReturnValue);
            return result.ReturnValue;
        }

        /// <param name="typeArguments">A generic function's type arguments; null binds its defaults.</param>
        public Task<object?> InvokeAsync(string thisValueId, object?[] args, TypeInfo[]? typeArguments = null)
        {
            args ??= Array.Empty<object?>();
            try
            {
                MemberValue row = ReceiverRow(thisValueId);
                NSGetterEvaluator.Context ctx = CreateDirectContext(ownership);
                object receiver = UnwrapReceiver(row, ctx);
                NeoResolvedNSFunction function = ResolveInstanceFunction(receiver, row, ctx);
                if (!function.Deferred)
                {
                    throw new InvalidOperationException(
                        $"NSFunction '{function.Member.name}' is immediate; use Invoke.");
                }
                NeoScriptExecutionResult result = NeoNSFunctionRuntime.ExecuteResolved(
                    client,
                    function,
                    receiver,
                    args,
                    ctx,
                    NeoScriptExecutionOptions.ForDirectFunction(client), ownsContext: true, typeArguments: typeArguments);
                return AwaitExecution(client, result);
            }
            catch (Exception exception)
            {
                return Task.FromException<object?>(exception);
            }
        }

        // The synthetic call site that dispatches a direct invocation; it
        // depends only on this node's member id.
        private CallFunctionPointer? directCallPointer;
        // The last receiver's value node, so a repeat receiver skips its id
        // lookup and reuses the node's unwrap.
        private NeoValueNode? receiverNode;

        // Each step returns one reference: a multi-reference struct returned
        // through memory costs Mono a write barrier per reference.
        private MemberValue ReceiverRow(string thisValueId)
        {
            NeoValueNode? node = receiverNode;
            // A repeat receiver's id passed validation when its node was kept.
            // A generated view passes the same id string every call.
            if (node is null
                || !(ReferenceEquals(node.id, thisValueId) || string.Equals(node.id, thisValueId)))
            {
                if (string.IsNullOrWhiteSpace(thisValueId))
                {
                    throw new ArgumentException(
                        "A non-empty receiver value id is required.",
                        nameof(thisValueId));
                }
                node = null;
            }
            MemberValue? row = client.ReadValue(ownership, thisValueId, ref node);
            if (!ReferenceEquals(node, receiverNode))
                receiverNode = node;
            if (row is null)
            {
                throw new NSGetterRuntimeError(
                    $"thisValueId '{thisValueId}' was not found in {ownership.ToString().ToLowerInvariant()} values.");
            }
            return row;
        }

        private NSGetterEvaluator.Context CreateDirectContext(NeoValueOwnership contextOwnership)
        {
            var ctx = client.CreateGetterContext(contextOwnership);
            ctx.BindRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx));
            return ctx;
        }

        private object UnwrapReceiver(MemberValue row, NSGetterEvaluator.Context ctx)
        {
            return NSGetterEvaluator.UnwrapRow(row, ctx, ownership, receiverNode)
                ?? throw new NSGetterRuntimeError(
                    $"NSFunction '{member.name}' cannot be invoked on a null receiver.");
        }

        /// <param name="row">The row <paramref name="receiver"/> was unwrapped from.</param>
        private NeoResolvedNSFunction ResolveInstanceFunction(object receiver, MemberValue row, NSGetterEvaluator.Context ctx)
        {
            // The pointer carries no missing-member fallback, so resolution
            // throws rather than answering no target.
            NSGetterEvaluator.CallSiteTarget target = NSGetterEvaluator.ResolveCallTarget(
                directCallPointer ??= new CallFunctionPointer
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
                ctx,
                // The receiver's class comes from the row just read, except
                // during a virtual instance replay, whose class read records
                // a dependency an ordinary read doesn't.
                client.IsReplayingVirtualInstance ? null : row)!;
            return target.function ?? NeoNSFunctionRuntime.ResolveSignature(client, target.memberId);
        }

        /// <summary>Invokes a receiverless static NSFunction.</summary>
        public object? InvokeStatic(object?[] args) => InvokeStaticCore(args, null);

        /// <summary>Invokes a receiverless generic static NSFunction with <paramref name="typeArguments"/>.</summary>
        public object? InvokeGenericStatic(object?[] args, TypeInfo[] typeArguments) =>
            InvokeStaticCore(args, typeArguments);

        private object? InvokeStaticCore(object?[] args, TypeInfo[]? typeArguments)
        {
            args ??= Array.Empty<object?>();
            NeoResolvedNSFunction function = ResolveStaticFunction();
            if (function.Deferred)
            {
                throw new InvalidOperationException(
                    $"NSFunction '{function.Member.name}' is deferred; use InvokeStaticAsync.");
            }
            NSGetterEvaluator.Context ctx = client.RentDirectFunctionContext(NeoValueOwnership.Session, function: true);
            NeoScriptExecutionResult result = NeoNSFunctionRuntime.ExecuteResolved(
                client,
                function,
                receiver: null,
                args,
                ctx,
                NeoScriptExecutionOptions.ForImmediate(client), ownsContext: true, typeArguments: typeArguments);
            if (result.IsPaused)
            {
                result.Deferred?.DisposeFromOwner(
                    "synchronous static NSFunction invocation suspended");
                throw new NSGetterRuntimeError(
                    $"Non-deferred static NSFunction '{function.Member.name}' suspended; its compiled IR is stale or corrupt.");
            }
            client.ReturnDirectFunctionContext(ctx, result.ReturnValue);
            return result.ReturnValue;
        }

        /// <summary>Invokes a deferred receiverless static NSFunction.</summary>
        /// <param name="typeArguments">A generic function's type arguments; null binds its defaults.</param>
        public Task<object?> InvokeStaticAsync(object?[] args, TypeInfo[]? typeArguments = null)
        {
            args ??= Array.Empty<object?>();
            try
            {
                NeoResolvedNSFunction function = ResolveStaticFunction();
                NSGetterEvaluator.Context ctx = CreateDirectContext(NeoValueOwnership.Session);
                if (!function.Deferred)
                {
                    throw new InvalidOperationException(
                        $"NSFunction '{function.Member.name}' is immediate; use InvokeStatic.");
                }
                return AwaitExecution(client, NeoNSFunctionRuntime.ExecuteResolved(
                    client,
                    function,
                    receiver: null,
                    args,
                    ctx,
                    NeoScriptExecutionOptions.ForDirectFunction(client), ownsContext: true, typeArguments: typeArguments));
            }
            catch (Exception exception)
            {
                return Task.FromException<object?>(exception);
            }
        }

        private NeoResolvedNSFunction ResolveStaticFunction()
        {
            NeoResolvedNSFunction function = ResolveFunction();
            if (!function.IsStatic)
            {
                throw new NSGetterRuntimeError(
                    $"NSFunction '{function.Member.name}' is an instance member and requires a receiver.");
            }
            return function;
        }

        internal static Task<object?> AwaitExecution(NeoClient client, NeoScriptExecutionResult initial)
        {
            if (!initial.IsPaused)
                return Task.FromResult(initial.ReturnValue);

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

    }

    internal sealed class NeoResolvedNSFunction
    {
        internal NeoResolvedNSFunction(
            object schemaResolution,
            string memberId,
            NSFunctionMember member,
            FunctionWithReturnType action,
            TypeInfo returnTypeInfo,
            FunctionArgumentTypeInfo[] argumentTypes,
            bool deferred)
        {
            SchemaResolution = schemaResolution;
            MemberId = memberId;
            DirectCallStack = NSGetterEvaluator.Context.CallFrameStack.Push(
                Array.Empty<string>(), memberId);
            Member = member;
            Action = action;
            ReturnTypeInfo = returnTypeInfo;
            ArgumentTypes = argumentTypes;
            Deferred = deferred;
            IsStatic = member.Modifier == NeoMemberModifierKind.Static;
#if NEO_COMPOSE_PROFILING
            Profile = new Unity.Profiling.ProfilerMarker("NeoScript." + member.name);
#endif
            HasGenericSignature = NeoNSFunctionRuntime.ContainsGeneric(returnTypeInfo)
                || Array.Exists(argumentTypes, NeoNSFunctionRuntime.ContainsGeneric);
            GenericParams = member.genericParams;
            DefaultTypeArguments = NeoFunctionTypeArguments.Defaults(GenericParams);
        }

        /// <summary>The client's <see cref="NeoClient.SchemaResolution"/> this was resolved under.</summary>
        internal object SchemaResolution
        {
            get;
        }
        internal string MemberId
        {
            get;
        }
        internal IReadOnlyList<string> DirectCallStack
        {
            get;
        }
        internal NSFunctionMember Member
        {
            get;
        }
        internal bool IsStatic
        {
            get;
        }
        internal FunctionWithReturnType Action
        {
            get;
        }
        internal TypeInfo ReturnTypeInfo
        {
            get;
        }
        internal FunctionArgumentTypeInfo[] ArgumentTypes
        {
            get;
        }
        internal bool Deferred
        {
            get;
        }
#if NEO_COMPOSE_PROFILING
        internal Unity.Profiling.ProfilerMarker Profile
        {
            get;
        }
#endif
        // Receiver-bound generics are a property of the signature, not the call.
        internal bool HasGenericSignature
        {
            get;
        }
        /// <summary>P110. The function's own type parameters, or null when it is not generic.</summary>
        internal FunctionGenericParam[]? GenericParams
        {
            get;
        }
        /// <summary>The table a call that supplies no type arguments gets, or null when a parameter has no default.</summary>
        internal TypeInfo[]? DefaultTypeArguments
        {
            get;
        }
        // Bindings for C# calls, keyed by their cached table, and for calls
        // that write no type arguments. A NeoScript site keeps its own.
        internal NeoBoundNSFunctionSignature? BoundSignatures;
        // Terminal marshalling for the declared (non-generic) return type;
        // the resolved function is cached per client, so one delegate serves
        // every invocation. A C# caller's result also has its resolved
        // identity validated; a NeoScript caller's compiled IR already
        // proved it.
        internal NeoScriptTerminalNormalizer? TerminalNormalizer;
        internal NeoScriptTerminalNormalizer? BoundaryTerminalNormalizer;
        internal NeoMemberNSFunction? StaticNode;

        // Diagnostic subjects depend only on the signature. Formatting them
        // per call put three string allocations on every invocation.
        private string? callSubject;
        private NeoScriptValueMarshaller.ValueSubject[]? subjects;
        internal string CallSubject =>
            callSubject ??= $"NSFunction '{Member.name}' ({MemberId})";

        /// <summary>
        /// The return value's subject, then each argument's. Callers pass
        /// them by reference: building a subject stores its string, which
        /// costs a GC write barrier.
        /// </summary>
        internal NeoScriptValueMarshaller.ValueSubject[] Subjects
        {
            // Every call reads it, and Mono's size limit leaves even the
            // lazy null check a call.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => subjects ?? CreateSubjects();
        }

        private NeoScriptValueMarshaller.ValueSubject[] CreateSubjects()
        {
            var created = new NeoScriptValueMarshaller.ValueSubject[ArgumentTypes.Length + 1];
            created[0] = $"return value of NSFunction '{Member.name}'";
            for (int i = 0; i < ArgumentTypes.Length; i++)
                created[i + 1] = $"argument {i} '{ArgumentTypes[i].name}' of NSFunction '{Member.name}'";
            return subjects = created;
        }
    }

    /// <summary>P110 §5. A generic function's signature bound to one type-argument table.</summary>
    internal sealed class NeoBoundNSFunctionSignature
    {
        internal NeoBoundNSFunctionSignature(
            NeoResolvedNSFunction function,
            TypeInfo[]? key,
            NeoBoundNSFunctionSignature? next,
            TypeInfo[] typeArguments,
            TypeInfo returnTypeInfo,
            TypeInfo[] argumentTypes)
        {
            this.function = function;
            this.key = key;
            Next = next;
            TypeArguments = typeArguments;
            ReturnTypeInfo = returnTypeInfo;
            ArgumentTypes = argumentTypes;
        }

        private readonly NeoResolvedNSFunction function;
        private readonly TypeInfo[]? key;

        internal NeoBoundNSFunctionSignature? Next
        {
            get;
        }
        internal TypeInfo[] TypeArguments
        {
            get;
        }
        internal TypeInfo ReturnTypeInfo
        {
            get;
        }
        internal TypeInfo[] ArgumentTypes
        {
            get;
        }
        internal NeoScriptTerminalNormalizer? TerminalNormalizer;
        internal NeoScriptTerminalNormalizer? BoundaryTerminalNormalizer;

        /// <summary>Whether a call to <paramref name="function"/> with <paramref name="key"/> binds to this signature.</summary>
        internal bool Binds(NeoResolvedNSFunction function, TypeInfo[]? key) =>
            ReferenceEquals(this.function, function) && ReferenceEquals(this.key, key);
    }

    internal static class NeoNSFunctionRuntime
    {
        private const int MaxCallableDepth = 64;
        private const int MaxCachedSignatures = 16;

        /// <summary>
        /// Calls immediate instance NSFunction <paramref name="schemaKey"/>
        /// with a pending temporary as <c>this</c>, as a NeoScript call on it
        /// would, so a call through its C# view makes no rows. The
        /// temporary's own Class picks the override.
        /// </summary>
        internal static object? InvokeDetached(
            NeoScriptObject receiver,
            string schemaKey,
            object?[] args,
            TypeInfo[]? typeArguments = null)
        {
            NeoClient client = receiver.client;
            string memberId = receiver.plan.ClassNode(client).SurfaceMember(schemaKey)?.memberId
                ?? throw new NSGetterRuntimeError(
                    $"Class '{receiver.plan.classId}' has no NSFunction '{schemaKey}'.");
            NeoResolvedNSFunction function = ResolveSignature(client, memberId);
            if (function.Deferred)
            {
                throw new InvalidOperationException(
                    $"NSFunction '{function.Member.name}' is deferred; use InvokeAsync.");
            }
            NSGetterEvaluator.Context ctx = client.RentDirectFunctionContext(NeoValueOwnership.Session, function: true);
            NeoScriptExecutionResult result = ExecuteResolved(
                client,
                function,
                receiver,
                args,
                ctx,
                NeoScriptExecutionOptions.ForImmediate(client), ownsContext: true, typeArguments: typeArguments);
            if (result.IsPaused)
            {
                result.Deferred?.DisposeFromOwner(
                    "synchronous NSFunction invocation suspended");
                throw new NSGetterRuntimeError(
                    $"Non-deferred NSFunction '{function.Member.name}' suspended; its compiled IR is stale or corrupt.");
            }
            client.ReturnDirectFunctionContext(ctx, result.ReturnValue);
            return result.ReturnValue;
        }

        internal static object? InvokeImmediate(
            NeoClient client,
            string memberId,
            object? receiver,
            object?[] args,
            NSGetterEvaluator.Context ctx) =>
            InvokeImmediate(client, ResolveSignature(client, memberId), receiver, args, ctx);

        internal static object? InvokeImmediate(
            NeoClient client,
            NeoResolvedNSFunction function,
            object? receiver,
            object?[] args,
            NSGetterEvaluator.Context ctx,
            CallFunctionPointer? site = null,
            NeoScriptScope? siteScope = null)
        {
            if (function.Deferred)
            {
                throw new NeoDeferredFunctionRuntimeError(
                    $"NSFunction '{function.Member.name}' ({function.MemberId}) deferred-mode mismatch: " +
                    "an immediate NeoScript frame called its deferred signature; " +
                    "compiled call IR is stale/corrupt.");
            }
            NeoScriptExecutionResult result = ExecuteResolved(
                client,
                function,
                receiver,
                args,
                ctx,
                NeoScriptExecutionOptions.ForImmediate(client),
                site: site,
                siteScope: siteScope);
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
            NeoScriptExecutionOptions options,
            bool ownsContext = false,
            CallFunctionPointer? site = null,
            NeoScriptScope? siteScope = null,
            TypeInfo[]? typeArguments = null)
        {
#if NEO_COMPOSE_PROFILING
            using var sample = function.Profile.Auto();
#endif
            // Validation, generic resolution and parameter binding live in
            // their own methods: their locals and exception regions would
            // otherwise widen the frame every call zeroes.
            ValidateInvocation(client, function, receiver, site?.args.Length ?? args.Length, ctx);
            FunctionWithReturnType action = function.Action;
            bool isStatic = function.IsStatic;
            NeoBoundNSFunctionSignature? bound = function.GenericParams is null
                ? null
                : BindTypeArguments(function, typeArguments, site, ctx);
            NeoExtensionRuntime.ValidateReceiver(function.Member, receiver, bound?.TypeArguments, ctx);
            TypeInfo effectiveReturnType = bound?.ReturnTypeInfo ?? function.ReturnTypeInfo;
            TypeInfo[] effectiveArgumentTypes = bound?.ArgumentTypes ?? function.ArgumentTypes;
            if (function.HasGenericSignature)
            {
                effectiveReturnType = ResolveInvocationSignature(
                    client,
                    function,
                    receiver!,
                    ctx,
                    effectiveReturnType,
                    ref effectiveArgumentTypes);
            }

            // Immediate functions cannot retain a lexical scope after completion:
            // delegate literals capture values, and callbacks finish within the call.
            // Deferred or unexpectedly suspended frames keep their own scopes.
            bool poolScope = !function.Deferred;
            NeoScriptScopeLayout layout = action.scopeLayout ??= new NeoScriptScopeLayout(action);
            var scope = poolScope ? layout.RentScope() : new NeoScriptScope(layout);
            // Until the body runs nothing but this call holds the scope.
            bool started = false;
            bool completed = false;
            int inPlaceFrame = -1;
            try
            {
                BindParameters(client, function, scope, receiver, args, site, siteScope, ctx, effectiveArgumentTypes, ownsContext);

                NSGetterEvaluator.Context nestedCtx;
                if (ownsContext)
                {
                    ctx.BindFunction(function.DirectCallStack, isStatic ? null : receiver);
                    ctx.BindFunctionTypeArguments(bound?.TypeArguments);
                    nestedCtx = ctx;
                }
                else if (!function.Deferred)
                {
                    inPlaceFrame = ctx.EnterFunction(function.MemberId, function.DirectCallStack, isStatic ? null : receiver);
                    // A non-generic callee keeps its caller's table: its body names no type parameter.
                    if (bound is not null)
                        ctx.BindFunctionTypeArguments(bound.TypeArguments);
                    nestedCtx = ctx;
                }
                else
                {
                    // A suspended deferred frame outlives this call.
                    nestedCtx = ctx.WithFunctionPushed(
                        function.MemberId, function.DirectCallStack, isStatic ? null : receiver, bound?.TypeArguments);
                }
                NeoScriptExecutionOptions functionOptions = options.ForFunction(function.Deferred);
                NeoScriptExecutor.PrepareFunctionContext(nestedCtx, functionOptions, inPlaceFrame);
                // Only the C# entry points own their context.
                bool boundary = ownsContext;
                NeoScriptTerminalNormalizer normalizer;
                if (!ReferenceEquals(effectiveReturnType, bound?.ReturnTypeInfo ?? function.ReturnTypeInfo))
                    normalizer = CreateTerminalNormalizer(client, function, effectiveReturnType, boundary);
                else if (bound is not null)
                    normalizer = BoundTerminalNormalizer(client, function, bound, boundary);
                else if (boundary)
                    normalizer = function.BoundaryTerminalNormalizer ??= CreateTerminalNormalizer(client, function, effectiveReturnType, true);
                else
                    normalizer = function.TerminalNormalizer ??= CreateTerminalNormalizer(client, function, effectiveReturnType, false);
                started = true;
                NeoScriptExecutionResult execution = NeoScriptExecutor.Execute(
                    client,
                    action,
                    scope,
                    nestedCtx,
                    functionOptions,
                    normalizer);
                completed = !execution.IsPaused;
                return execution;
            }
            finally
            {
                if (inPlaceFrame >= 0)
                    ctx.ExitFunction(inPlaceFrame);
                // Failed/suspended execution may still own continuations. Let their
                // existing lifetime rules release those scopes instead of pooling them.
                if (poolScope)
                {
                    if (completed || !started)
                        layout.ReturnScope(scope, BoundParameterCount(function));
                    else
                        layout.AbandonScope(scope);
                }
            }
        }

        private static void ValidateInvocation(
            NeoClient client,
            NeoResolvedNSFunction function,
            object? receiver,
            int argumentCount,
            NSGetterEvaluator.Context ctx)
        {
            // The usual full-arity call passes every check below without
            // entering the method that names a failure.
            bool isStatic = function.IsStatic;
            if ((receiver is null) == isStatic
                && argumentCount == function.ArgumentTypes.Length
                && ctx.functionDepth < MaxCallableDepth
                && function.Action.parameters?.Length == argumentCount + 2
                && !(isStatic && function.HasGenericSignature))
            {
                return;
            }
            ValidateUnusualInvocation(client, function, receiver, argumentCount, ctx);
        }

        private static void ValidateUnusualInvocation(
            NeoClient client,
            NeoResolvedNSFunction function,
            object? receiver,
            int argumentCount,
            NSGetterEvaluator.Context ctx)
        {
            bool isStatic = function.IsStatic;
            if (receiver is null && !isStatic && NeoExtensionRuntime.Descriptor(function.Member)?.receiverType.required != false)
            {
                throw new NSGetterRuntimeError(
                    $"Cannot invoke NSFunction '{function.Member.name}' on a null receiver.");
            }
            if (receiver is not null && isStatic)
            {
                throw new NSGetterRuntimeError(
                    $"Static NSFunction '{function.Member.name}' must be invoked without an instance receiver.");
            }
            // P65 §2.5 callee-side fill: a positionally short call is
            // completed from the callee record's current defaults before the
            // `__arg_N__` parameters bind. Below the non-defaulted minimum and
            // above the full arity remain hard errors.
            // A full-arity call is valid: skip the call and the subject
            // getter, whose cold path keeps Mono from inlining it.
            if (argumentCount != function.ArgumentTypes.Length)
            {
                NeoParameterDefaults.ValidateArity(
                    argumentCount,
                    function.ArgumentTypes,
                    function.CallSubject);
            }
            if (ctx.functionDepth >= MaxCallableDepth)
            {
                var names = new List<string>(ctx.functionCallStack.Count + 1);
                foreach (string id in ctx.functionCallStack)
                {
                    names.Add(client.TryGetMember(id, out JsonMember? item)
                        ? item.name
                        : id);
                }
                names.Add(function.Member.name);
                throw new NeoScriptControlError(
                    $"NeoScript Function call depth exceeded {MaxCallableDepth}: {string.Join(" -> ", names)}.");
            }

            FunctionWithReturnType action = function.Action;
            // The compiler keeps both synthetic parameters for static functions too.
            int receiverParameterCount = 2;
            int expectedParameters = function.ArgumentTypes.Length + receiverParameterCount;
            if (action.parameters is null || action.parameters.Length != expectedParameters)
            {
                throw new NSGetterRuntimeError(
                    $"NSFunction '{function.Member.name}' compiled action parameter count is stale (expected {expectedParameters}, found {action.parameters?.Length ?? 0}).");
            }
            if (isStatic && function.HasGenericSignature)
            {
                throw new NSGetterRuntimeError(
                    $"Static NSFunction '{function.Member.name}' cannot use receiver-bound Generic signature classes.");
            }
        }

        /// <summary>A receiver-bound Generic signature's return type and argument types.</summary>
        private static TypeInfo ResolveInvocationSignature(
            NeoClient client,
            NeoResolvedNSFunction function,
            object receiver,
            NSGetterEvaluator.Context ctx,
            TypeInfo returnTypeInfo,
            ref TypeInfo[] argumentTypes)
        {
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv =
                ResolveReceiverGenericEnv(client, receiver, ctx, function);
            TypeInfo returnType = ResolveInvocationTypeInfo(
                client,
                returnTypeInfo,
                genericEnv,
                new HashSet<string>());
            var resolved = new TypeInfo[argumentTypes.Length];
            for (int i = 0; i < argumentTypes.Length; i++)
            {
                resolved[i] = ResolveInvocationTypeInfo(
                    client,
                    argumentTypes[i],
                    genericEnv,
                    new HashSet<string>());
            }
            argumentTypes = resolved;
            return returnType;
        }

        /// <summary>
        /// P110 §5. The generic callee's signature bound to its call's type
        /// arguments: <paramref name="typeArguments"/> from a C# caller, else
        /// the call site's, resolved in the caller's frame, with defaults
        /// for any it omits.
        /// </summary>
        private static NeoBoundNSFunctionSignature BindTypeArguments(
            NeoResolvedNSFunction function,
            TypeInfo[]? typeArguments,
            CallFunctionPointer? site,
            NSGetterEvaluator.Context ctx)
        {
            TypeInfo[]? written = typeArguments ?? site?.typeArguments;
            bool forwardsClass = written is not null && Array.Exists(written, ContainsGeneric);
            // A NeoScript site that writes type arguments keeps its own short
            // chain: one binding per callee, and per caller frame table when
            // it forwards. Any other call binds on the function by its table.
            CallFunctionPointer? bindingSite = typeArguments is null && site?.typeArguments is not null ? site : null;
            bool forwards = bindingSite is not null && bindingSite.TypeArgumentsForward;
            TypeInfo[]? callerTable = forwards ? ctx.functionTypeArguments : null;
            TypeInfo[]? key = bindingSite is null ? written : callerTable;
            NeoBoundNSFunctionSignature? chain = bindingSite is null ? function.BoundSignatures : bindingSite.BoundSignatures;
            if (forwardsClass)
                chain = null;
            int bindingCount = 0;
            for (NeoBoundNSFunctionSignature? bound = chain; bound is not null; bound = bound.Next)
            {
                if (bound.Binds(function, key))
                    return bound;
                bindingCount++;
            }
            // Forwarded caller tables may be transient. Keep retention and
            // lookup work bounded at each site, as for receiver targets.
            bool cacheBinding = !forwardsClass && bindingCount < MaxCachedSignatures;
            TypeInfo[] table = written is null && function.DefaultTypeArguments is { } defaults
                ? defaults
                : NeoFunctionTypeArguments.ForCall(
                    written,
                    forwards,
                    function.GenericParams,
                    function.Member.name,
                    callerTable)!;
            if (forwardsClass)
            {
                table = (TypeInfo[])table.Clone();
                for (int i = 0; i < table.Length; i++)
                    table[i] = NSGetterEvaluator.CloseContextType(table[i], ctx);
            }
            var argumentTypes = new TypeInfo[function.ArgumentTypes.Length];
            for (int i = 0; i < argumentTypes.Length; i++)
                argumentTypes[i] = NeoFunctionTypeArguments.BindArgument(function.ArgumentTypes[i], table);
            var created = new NeoBoundNSFunctionSignature(
                function,
                key,
                cacheBinding ? chain : null,
                table,
                NeoFunctionTypeArguments.Bind(function.ReturnTypeInfo, table),
                argumentTypes);
            // A class-forwarding call varies with the caller's receiver, not
            // just its function type-argument table.
            if (cacheBinding)
            {
                if (bindingSite is null)
                    function.BoundSignatures = created;
                else
                    bindingSite.BoundSignatures = created;
            }
            return created;
        }

        private static NeoScriptTerminalNormalizer BoundTerminalNormalizer(
            NeoClient client,
            NeoResolvedNSFunction function,
            NeoBoundNSFunctionSignature bound,
            bool boundary) =>
            boundary
                ? bound.BoundaryTerminalNormalizer ??= CreateTerminalNormalizer(client, function, bound.ReturnTypeInfo, true)
                : bound.TerminalNormalizer ??= CreateTerminalNormalizer(client, function, bound.ReturnTypeInfo, false);

        /// <summary>The leading parameter slots <see cref="BindParameters"/> binds on every call.</summary>
        private static int BoundParameterCount(NeoResolvedNSFunction function) => function.ArgumentTypes.Length + 2;

        private static void BindParameters(
            NeoClient client,
            NeoResolvedNSFunction function,
            NeoScriptScope scope,
            object? receiver,
            object?[] args,
            CallFunctionPointer? site,
            NeoScriptScope? siteScope,
            NSGetterEvaluator.Context ctx,
            TypeInfo[] argumentTypes,
            bool hostBoundary)
        {
            if (hostBoundary)
                NSGetterEvaluator.MarkHostArgument(receiver, receiver, ctx);
            const int rootParameterIndex = 1;
            const int argumentParameterOffset = 2;
            scope.SetParameter(0, receiver);
            scope.SetParameter(rootParameterIndex, ctx.rootValue);
            if (function.ArgumentTypes.Length == 0)
                return;
            int argumentCount = site?.args.Length ?? args.Length;
            NeoScriptValueMarshaller.ValueSubject[] subjects = function.Subjects;
            for (int i = 0; i < function.ArgumentTypes.Length; i++)
            {
                FunctionArgumentTypeInfo argument = function.ArgumentTypes[i];
                // A NeoScript call site's arguments evaluate straight into
                // their slots, in the caller's frame, rather than through an
                // argument array each store write-barriers again.
                object? value = i >= argumentCount
                    ? null
                    : site is null
                        ? args[i]
                        : NSGetterEvaluator.EvalPointer(site.args[i], siteScope!, ctx);
                try
                {
                    if (i >= argumentCount)
                        value = NeoParameterDefaults.DefaultRuntimeValue(argument, function.CallSubject);
                    object? normalized = NeoScriptValueMarshaller.Normalize(
                        client,
                        ctx.valueOwnership,
                        value,
                        argumentTypes[i],
                        ctx,
                        in subjects[i + 1]);
                    if (hostBoundary && i < argumentCount)
                        NSGetterEvaluator.MarkHostArgument(value, normalized, ctx);
                    // The effective signature already closed receiver and function
                    // parameters. Preserve only the collection erased by a bare T.
                    if (argument.type == MemberKind.Generic && normalized is not null)
                        normalized = NSGetterEvaluator.TagSequenceCollection(normalized, argumentTypes[i], ctx);
                    scope.SetParameter(i + argumentParameterOffset, normalized);
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
        }

        // A separate method keeps the closure off ExecuteResolved's frame;
        // non-generic signatures build it once per resolved function. The
        // body lives in the closure itself, saving a call per terminal.
        private static NeoScriptTerminalNormalizer CreateTerminalNormalizer(
            NeoClient client,
            NeoResolvedNSFunction function,
            TypeInfo effectiveReturnType,
            bool boundary)
        {
            bool isVoid = effectiveReturnType is VoidTypeInfo
                || effectiveReturnType.type == MemberKind.Void;
            return (execution, ctx) =>
            {
                if (execution.IsPaused)
                    throw new InvalidOperationException(
                        "NSFunction terminal normalization received a paused execution.");
                if (isVoid)
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
                // C# gets a stored collection's entry values, resolved while
                // a query result still carries its source's entry member.
                object? returned = boundary
                    ? NSGetterEvaluator.ResolveHostCollection(execution.ReturnValue, ctx)
                    : execution.ReturnValue;
                object? normalized;
                try
                {
                    normalized = NeoScriptValueMarshaller.Normalize(
                        client,
                        ctx.valueOwnership,
                        returned,
                        effectiveReturnType,
                        ctx,
                        in function.Subjects[0],
                        resolvedIdentity: boundary);
                }
                catch (InvalidOperationException error)
                {
                    throw new NSGetterRuntimeError(error.Message, NeoErrorContract.NeoArgumentError, error);
                }
                if (boundary)
                    normalized = NSGetterEvaluator.ResolveHostCollection(normalized, ctx);
                // Marshalling usually returns the evaluator's own value;
                // reuse the executor's result instead of allocating a copy
                // of it.
                if (ReferenceEquals(normalized, execution.ReturnValue))
                    return execution;
                return NeoScriptExecutionResult.Completed(
                    returned: true,
                    normalized);
            };
        }

        /// <summary>Whether <paramref name="typeInfo"/> names a receiver class's generic parameter.</summary>
        internal static bool ContainsGeneric(TypeInfo typeInfo)
        {
            if (TupleTypeInfo.From(typeInfo) is { } tuple)
                return Array.Exists(tuple.elements, element => ContainsGeneric(element.typeInfo));
            // A function's own type parameter binds per call, not per receiver.
            if (typeInfo.type == MemberKind.Generic)
                return !NeoFunctionTypeArguments.IsFunctionGeneric(typeInfo);
            TypeInfo? delegateReturn = typeInfo switch
            {
                DelegateTypeInfo delegateType => delegateType.returnTypeInfo,
                FunctionArgumentTypeInfo argument
                    when argument.type == MemberKind.NSDelegate => argument.returnTypeInfo,
                _ => null,
            };
            if (delegateReturn is not null && ContainsGeneric(delegateReturn))
                return true;
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
                FunctionArgumentTypeInfo argument => argument.entryTypeInfo ?? argument.targetTypeInfo,
                CollectionTypeInfo collection => collection.entryTypeInfo,
                LookupTypeInfo lookup => lookup.entryTypeInfo,
                ReferenceTypeInfo reference => reference.targetTypeInfo,
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
                InterfaceTypeInfo interfaceType => interfaceType.typeArguments,
                _ => null,
            };
            if (typeArguments is null)
                return false;
            foreach (TypeInfo argument in typeArguments.Values)
            {
                if (ContainsGeneric(argument))
                    return true;
            }
            return false;
        }

        internal static IReadOnlyDictionary<string, NeoGenericEnvEntry>
            ResolveReceiverGenericEnv(
                NeoClient client,
                object receiver,
                NSGetterEvaluator.Context ctx,
                NeoResolvedNSFunction function) =>
            ResolveReceiverGenericEnv(client, receiver, ctx, $"NSFunction '{function.Member.name}'");

        internal static IReadOnlyDictionary<string, NeoGenericEnvEntry> ResolveReceiverGenericEnv(
            NeoClient client, object receiver, NSGetterEvaluator.Context ctx, string subject)
        {
            string? runtimeClassId = NSGetterEvaluator.FindRowClassIdByReference(
                receiver,
                ctx);
            if (string.IsNullOrEmpty(runtimeClassId))
            {
                throw new NSGetterRuntimeError(
                    $"{subject} uses generic types, but its receiver has no runtime class.");
            }

            string? receiverValueId = NSGetterEvaluator.FindRowIdByReference(
                receiver,
                ctx);
            string? cacheKey = string.IsNullOrEmpty(receiverValueId)
                ? null
                : runtimeClassId + "\n" + NSGetterEvaluator.FindRowOwnershipByReference(receiver, ctx) + "\n" + receiverValueId;
            if (cacheKey is not null
                && ctx.genericEnvironmentCache.TryGetValue(
                    cacheKey, out IReadOnlyDictionary<
                        string, NeoGenericEnvEntry>? cached))
            {
                return cached;
            }

            // A persisted closure closes forwarded placement parameters, and
            // the placement reads its arguments in its owner's environment.
            NeoValueOwnership receiverOwnership = NSGetterEvaluator.FindRowOwnershipByReference(receiver, ctx) ?? ctx.valueOwnership;
            MemberValue? row = null;
            if (receiverValueId is not null)
                client.TryGetValue(receiverOwnership, receiverValueId, out row);
            try
            {
                ClassMember? placement = null;
                if (row?.typeSource is null && receiverValueId is not null
                    && client.TryInferMemberForValueId(receiverValueId, out JsonMember? placementMember))
                    placement = placementMember as ClassMember;
                IReadOnlyDictionary<string, NeoGenericEnvEntry> resolved =
                    row?.typeSource is not null
                        ? NeoTypeSources.TypeSourcedClassRowEnv(client, row, runtimeClassId!)
                        : NeoGenericResolution.SlotRowEnv(
                    client,
                    placement,
                    placement?.ownerEnvironment,
                    row?.genericBindings,
                    runtimeClassId!);
                if (cacheKey is not null)
                {
                    ctx.genericEnvironmentCache[cacheKey] = resolved;
                }
                return resolved;
            }
            catch (Exception exception)
            {
                throw new NSGetterRuntimeError(
                    $"{subject} could not resolve the receiver's generic environment: {exception.Message}");
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
            HashSet<string> visitingMembers,
            BindingTypeProjection projection = BindingTypeProjection.Read)
        {
            if (TupleTypeInfo.From(typeInfo) is { } tuple)
            {
                TupleElementTypeInfo[] elements = TupleTypeInfo.MapElements(tuple.elements,
                    element => ResolveInvocationTypeInfo(client, element, genericEnv, visitingMembers, projection));
                return ReferenceEquals(elements, tuple.elements) ? tuple : new TupleTypeInfo
                {
                    type = MemberKind.Tuple,
                    required = tuple.required,
                    elements = elements,
                };
            }
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
                TypeInfo resolved = TypeInfoFromBindingMember(
                    client,
                    bindingMember,
                    NeoGenericResolution.BindingEnv(genericEnv, binding),
                    visitingMembers, projection);
                resolved.required = NeoGeneratedTypesSupport.ClosedSlotRequired(resolved, declaredNullable: !typeInfo.required);
                return resolved;
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
                        visitingMembers, projection),
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
                        visitingMembers, projection);
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
                            visitingMembers, projection),
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
                        visitingMembers, projection);
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
            if (typeInfo.type is MemberKind.List or MemberKind.Set or MemberKind.Dictionary
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
                        visitingMembers, projection),
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
                    collectionMemberId = collectionMemberId,
                    collectionValueId = collectionValueId,
                    entryTypeInfo = ResolveInvocationTypeInfo(
                        client,
                        entryTypeInfo,
                        genericEnv,
                        visitingMembers, projection),
                };
            }
            return typeInfo;
        }

        private static Dictionary<string, TypeInfo>?
            ResolveInvocationTypeArguments(
                NeoClient client,
                IReadOnlyDictionary<string, TypeInfo>? typeArguments,
                IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
                HashSet<string> visitingMembers,
            BindingTypeProjection projection = BindingTypeProjection.Read)
        {
            if (typeArguments is null)
                return null;
            var resolved = new Dictionary<string, TypeInfo>(typeArguments.Count);
            foreach (var pair in typeArguments)
            {
                resolved[pair.Key] = ResolveInvocationTypeInfo(
                    client,
                    pair.Value,
                    genericEnv,
                    visitingMembers, projection);
            }
            return resolved;
        }

        /// <summary>The entry member of a lookup binding that declares no type of its own.</summary>
        private static JsonMember LookupBindingEntry(NeoClient client, LookupMember lookup)
        {
            if (lookup.collectionMemberId is null)
            {
                throw new NSGetterRuntimeError(
                    $"Generic NSFunction Lookup binding '{lookup.id}' is unbound and declares no type.");
            }
            client.TryGetMember(lookup.collectionMemberId, out JsonMember? collection);
            string? entryId = collection switch
            {
                ListMember list => list.entryMemberId,
                DictionaryMember dictionary => dictionary.entryMemberId,
                _ => null,
            };
            if (entryId is null || !client.TryGetMember(entryId, out JsonMember? entryMember))
            {
                throw new NSGetterRuntimeError(
                    $"Generic NSFunction Lookup binding '{lookup.id}' has a missing collection entry type.");
            }
            return entryMember;
        }

        internal enum BindingTypeProjection
        {
            Read,
            Stored,
        }

        internal static TypeInfo TypeInfoFromBindingMember(
            NeoClient client,
            JsonMember member,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
            HashSet<string> visitingMembers,
            BindingTypeProjection projection = BindingTypeProjection.Read)
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
                                required = generic.Requirement == NeoMemberRequirementKind.Required,
                                genericParamId = generic.genericParamId,
                            },
                            genericEnv,
                            visitingMembers, projection);
                    case ClassMember classMember:
                        return classMember.DeclaredTypeInfo(
                            classMember.Requirement == NeoMemberRequirementKind.Required,
                            ResolveBindingTypeArguments(
                                client,
                                classMember.TypeArguments,
                                genericEnv,
                                visitingMembers, projection));
                    case EnumMember enumMember:
                        return new EnumTypeInfo
                        {
                            type = MemberKind.Enum,
                            required = enumMember.Requirement == NeoMemberRequirementKind.Required,
                            enumId = enumMember.enumId,
                        };
                    case TupleMember tuple:
                        {
                            var elements = new TupleElementTypeInfo[tuple.elementMemberIds.Length];
                            for (int i = 0; i < elements.Length; i++)
                            {
                                if (!client.TryGetMember(tuple.elementMemberIds[i], out JsonMember? element))
                                    throw new NSGetterRuntimeError($"Tuple binding member '{tuple.id}' references missing element '{tuple.elementMemberIds[i]}'.");
                                elements[i] = new TupleElementTypeInfo
                                {
                                    name = element.name == $"Item{i + 1}" ? null : element.name,
                                    typeInfo = TypeInfoFromBindingMember(client, element, genericEnv, visitingMembers, projection),
                                };
                            }
                            return new TupleTypeInfo
                            {
                                type = MemberKind.Tuple,
                                required = tuple.Requirement == NeoMemberRequirementKind.Required,
                                elements = elements,
                            };
                        }
                    case ListMember list:
                        return CollectionBindingTypeInfo(
                            client,
                            list,
                            list.entryMemberId,
                            genericEnv,
                            visitingMembers, projection);
                    case DictionaryMember dictionary:
                        return CollectionBindingTypeInfo(
                            client,
                            dictionary,
                            dictionary.entryMemberId,
                            genericEnv,
                            visitingMembers, projection);
                    case LookupMember lookup:
                        {
                            TypeInfo entry = lookup.declaredTypeInfo is null
                                ? TypeInfoFromBindingMember(client, LookupBindingEntry(client, lookup), genericEnv, visitingMembers, projection)
                                : ResolveInvocationTypeInfo(client, lookup.declaredTypeInfo, genericEnv, visitingMembers, projection).ShallowClone();
                            bool multiple = lookup.Selection == NeoMemberSelectionKind.Multi;
                            bool required = multiple || lookup.Requirement == NeoMemberRequirementKind.Required;
                            // P112 §1.1. A handle reference types as its target; a slot one stays a reference.
                            if (projection == BindingTypeProjection.Stored
                                || NeoReferenceTypes.Category(entry, client) == NeoReferenceCategory.Slot)
                            {
                                entry = new ReferenceTypeInfo
                                {
                                    type = MemberKind.Reference,
                                    required = required,
                                    targetTypeInfo = entry,
                                };
                            }
                            else
                            {
                                entry.required = required;
                            }
                            if (!multiple)
                                return entry;
                            return new LookupTypeInfo
                            {
                                type = MemberKind.Lookup,
                                required = lookup.Requirement == NeoMemberRequirementKind.Required,
                                collectionMemberId = lookup.collectionMemberId,
                                collectionValueId = lookup.collectionValueId,
                                entryTypeInfo = entry,
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
                                    visitingMembers, projection);
                            }
                            return new DelegateTypeInfo
                            {
                                type = MemberKind.NSDelegate,
                                required = delegateMember.Requirement == NeoMemberRequirementKind.Required,
                                returnTypeInfo = delegateMember.returnTypeInfo is VoidTypeInfo
                                    ? delegateMember.returnTypeInfo
                                    : ResolveInvocationTypeInfo(
                                        client,
                                        delegateMember.returnTypeInfo,
                                        genericEnv,
                                        visitingMembers, projection),
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
                                    visitingMembers, projection);
                            }
                            return new ActionTypeInfo
                            {
                                type = MemberKind.NSAction,
                                required = actionMember.Requirement == NeoMemberRequirementKind.Required,
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
                    case DialogueLookupMember:
                        return new PrimitiveTypeInfo
                        {
                            type = member.kind,
                            required = member.Requirement == NeoMemberRequirementKind.Required,
                        };
                    case ObjectMember objectMember:
                        return new ObjectTypeInfo
                        {
                            type = MemberKind.Object,
                            required = objectMember.Requirement == NeoMemberRequirementKind.Required,
                            objectType = objectMember.objectType,
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
            IReadOnlyDictionary<string, TypeBinding>? bindings,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
            HashSet<string> visitingMembers,
            BindingTypeProjection projection = BindingTypeProjection.Read)
        {
            if (bindings is null)
                return null;
            var resolved = new Dictionary<string, TypeInfo>(bindings.Count);
            foreach (var pair in bindings)
            {
                TypeBinding binding = pair.Value;
                string? memberId;
                IReadOnlyDictionary<string, NeoGenericEnvEntry> memberEnv = genericEnv;
                if (!binding.IsOpen)
                {
                    memberId = NeoTypeBindingViews.Id(binding);
                }
                else if (genericEnv.TryGetValue(
                        binding.Param!,
                        out NeoGenericEnvEntry? forwarded)
                    && forwarded.IsBound)
                {
                    memberId = forwarded.memberId;
                    memberEnv = NeoGenericResolution.BindingEnv(genericEnv, forwarded);
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
                    memberEnv,
                    visitingMembers, projection);
            }
            return resolved;
        }

        private static CollectionTypeInfo CollectionBindingTypeInfo(
            NeoClient client,
            JsonMember collection,
            string entryMemberId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
            HashSet<string> visitingMembers,
            BindingTypeProjection projection = BindingTypeProjection.Read)
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
                required = collection.Requirement == NeoMemberRequirementKind.Required,
                keyEnumId = (collection as DictionaryMember)?.keyEnumId,
                listMemberId = collection is ListMember ? collection.id : null,
                entryTypeInfo = TypeInfoFromBindingMember(
                    client,
                    entryMember,
                    genericEnv,
                    visitingMembers, projection),
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
                            client.SchemaResolution,
                            memberId,
                            effectiveMember,
                            action,
                            returnTypeInfo,
                            argumentTypes,
                            effectiveMember.Dispatch
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
        internal static bool IsContextRoot(object? value) => value is RuntimeRoot;

        internal static object? ResolveRoot(
            NeoClient client,
            NSGetterEvaluator.Context ctx)
        {
            return new RuntimeRoot(client, ctx);
        }

        // Binding the root names is not a read of their values. Resolve
        // only the root the script accesses, so constructor dependency capture
        // does not subscribe every constructor to every global graph. A root
        // the client lacks (a user client has only User, §4.2) is absent.
        private sealed class RuntimeRoot : IDictionary<string, object?>
        {
            private readonly NeoClient client;
            private readonly NSGetterEvaluator.Context context;
            private static readonly string[] names = { "Assets", "Save", "Session", "User" };
            internal RuntimeRoot(NeoClient client, NSGetterEvaluator.Context context)
            {
                this.client = client;
                this.context = context;
            }
            // Each root's value node, so a read looks its row id up once and
            // unwraps through the node's memo.
            private NeoValueNode? assetsNode;
            private NeoValueNode? saveNode;
            private NeoValueNode? sessionNode;
            private NeoValueNode? userNode;

            public bool TryGetValue(string key, out object? value)
            {
                switch (key)
                {
                    case "Assets":
                        return TryRead(client.assets, NeoValueOwnership.Asset, ref assetsNode, out value);
                    case "Save":
                        return TryRead(client.save, NeoValueOwnership.Save, ref saveNode, out value);
                    case "Session":
                        return TryRead(client.session, NeoValueOwnership.Session, ref sessionNode, out value);
                    case "User":
                        return TryRead(client.user, NeoValueOwnership.User, ref userNode, out value);
                    default:
                        value = null;
                        return false;
                }
            }

            private bool TryRead(
                NeoMemberClass? root,
                NeoValueOwnership ownership,
                ref NeoValueNode? node,
                out object? value)
            {
                if (root is null)
                {
                    value = null;
                    return false;
                }
                value = null;
                if (root.value is ObjectMemberValue row)
                {
                    // A load can replace the root row.
                    if (node is not null && node.id != row.id)
                        node = null;
                    if (client.ReadValue(ownership, row.id, ref node) is ObjectMemberValue current)
                        value = NSGetterEvaluator.UnwrapRow(current, context, ownership, node);
                }
                return true;
            }
            public object? this[string key]
            {
                get => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);
                set => throw new NotSupportedException();
            }
            public ICollection<string> Keys => Array.FindAll(names, ContainsKey);
            public ICollection<object?> Values => Array.ConvertAll(Array.FindAll(names, ContainsKey), name => this[name]);
            public int Count => Array.FindAll(names, ContainsKey).Length;
            public bool IsReadOnly => true;
            public bool ContainsKey(string key) => TryGetRoot(key) is not null;
            private NeoMemberClass? TryGetRoot(string key) => key switch
            {
                "Assets" => client.assets,
                "Save" => client.save,
                "Session" => client.session,
                "User" => client.user,
                _ => null,
            };
            public bool Contains(KeyValuePair<string, object?> item) =>
                TryGetValue(item.Key, out var value) && Equals(value, item.Value);
            public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
            {
                foreach (string key in names)
                    if (ContainsKey(key))
                        yield return new KeyValuePair<string, object?>(key, this[key]);
            }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            public void CopyTo(KeyValuePair<string, object?>[] array, int index)
            {
                foreach (var pair in this)
                    array[index++] = pair;
            }
            public void Add(string key, object? value) => throw new NotSupportedException();
            public void Add(KeyValuePair<string, object?> item) => throw new NotSupportedException();
            public bool Remove(string key) => throw new NotSupportedException();
            public bool Remove(KeyValuePair<string, object?> item) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
        }

        internal static object? Normalize(
            NeoClient client,
            NeoValueOwnership ownership,
            object? value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx,
            in ValueSubject subject,
            bool resolvedIdentity = false)
        {
            if (value is NeoValueWritePayload payload)
            {
                value = payload.isValueReference
                    ? payload.valueReference ?? (object?)payload.valueId
                    : payload.value;
            }
            NeoClient? owner = NeoExtensionRuntime.ClientOf(value);
            if (owner is not null && !ReferenceEquals(client, owner))
                throw new InvalidOperationException("An SDK value belongs to a different project client.");
            if (owner?.IsDisposed == true)
                throw new ObjectDisposedException(nameof(NeoClient));
            if (typeInfo.type == MemberKind.Interface
                && !NSGetterEvaluator.IsIntrinsicSequence(value, typeInfo, ctx)
                && !NSGetterEvaluator.IsRowRecord(value, ctx))
            {
                if (value is not null && client.SequenceCollectionElements.TryGetValue(NSGetterEvaluator.SequenceCollectionIdentity(value), out _))
                    throw new InvalidOperationException($"{subject} has a collection element type incompatible with its declared interface.");
                value = NeoSequences.WriteBoxed(client, value, typeInfo);
            }
            // Primitives, value references and enum ids match none of the
            // rewrites in NormalizeRewritten, so they skip its chain.
            switch (typeInfo.type)
            {
                // A value of its own kind is already valid.
                case MemberKind.Bool when value is bool:
                case MemberKind.String when value is string:
                case MemberKind.Int when value is double number && IsIntegralNumber(number):
                // A C# caller's boxed int, which is always integral.
                case MemberKind.Int when value is int:
                    return value;
                case MemberKind.Float when IsNumber(value):
                    {
                        object? rounded = NSGetterEvaluator.CoerceNumericBinding(value, typeInfo, "float binding");
                        // A float holds the infinities, but a finite number
                        // that rounds to one is out of range.
                        if (rounded is double single && double.IsInfinity(single) && !IsNonFinite(value))
                            throw new InvalidOperationException($"{subject} is outside the float range.");
                        value = rounded;
                        break;
                    }
                case MemberKind.Bool or MemberKind.Int or MemberKind.Float or MemberKind.String
                    when value is double or bool or string or int:
                    break;
                case MemberKind.Interface when NSGetterEvaluator.IsIntrinsicSequence(value, typeInfo, ctx):
                    return value;
                // The usual Class value: a record this context unwrapped from
                // a row, which is canonical and passes the shape check.
                case MemberKind.Class or MemberKind.Interface
                    when !resolvedIdentity && NSGetterEvaluator.IsRowRecord(value, ctx):
                    return value;
                case MemberKind.Class or MemberKind.Interface
                    when value is INeoValueReference reference:
                    value = UnwrapReference(client, ownership, value, reference, ctx, subject);
                    break;
                case MemberKind.Enum when value is object?[] or string:
                    value = NormalizeEnum(value, typeInfo, subject);
                    break;
                default:
                    return NormalizeRewritten(
                        client, ownership, value, typeInfo, ctx, subject, resolvedIdentity);
            }
            ValidateRuntimeValue(client, value, typeInfo, subject);
            if (resolvedIdentity)
                ValidateResolvedIdentity(client, value, typeInfo, ctx, subject);
            return value;
        }

        private static object? NormalizeRewritten(
            NeoClient client,
            NeoValueOwnership ownership,
            object? value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx,
            ValueSubject subject,
            bool resolvedIdentity)
        {
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
            else if (value is INeoValueReference reference)
            {
                value = UnwrapReference(client, ownership, value, reference, ctx, subject);
            }

            switch (typeInfo.type)
            {
                case MemberKind.Tuple:
                    value = NormalizeTuple(client, ownership, value, typeInfo, ctx, subject);
                    break;
                case MemberKind.Decimal:
                    if (value is decimal decimalValue)
                        value = NeoDecimalValues.Format(decimalValue);
                    else if (value is double or float or int or long or short)
                        value = NSGetterEvaluator.CoerceDecimalOperand(value, subject.ToString());
                    break;
                case MemberKind.Vector2:
                    if (NeoGeneratedTypesSupport.ReadVector2Value(value) is Vector2 normalizedVector2)
                        value = NeoGeneratedTypesSupport.Vector2Value(normalizedVector2);
                    break;
                case MemberKind.Vector2Int:
                    if (value is NeoVector2Value)
                        break;
                    if (NeoGeneratedTypesSupport.ReadVector2IntValue(value) is Vector2Int normalizedVector2Int)
                        value = NeoGeneratedTypesSupport.Vector2IntValue(normalizedVector2Int);
                    break;
                case MemberKind.Vector3:
                    if (NeoGeneratedTypesSupport.ReadVector3Value(value) is Vector3 normalizedVector3)
                        value = NeoGeneratedTypesSupport.Vector3Value(normalizedVector3);
                    break;
                case MemberKind.Vector3Int:
                    if (value is NeoVector3Value)
                        break;
                    if (NeoGeneratedTypesSupport.ReadVector3IntValue(value) is Vector3Int normalizedVector3Int)
                        value = NeoGeneratedTypesSupport.Vector3IntValue(normalizedVector3Int);
                    break;
                case MemberKind.Color:
                    if (NeoGeneratedTypesSupport.ReadColorValue(value) is Color normalizedColor)
                        value = NeoGeneratedTypesSupport.ColorValue(normalizedColor);
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
                    value = NormalizeEnum(value, typeInfo, subject);
                    break;
                case MemberKind.List:
                case MemberKind.Set:
                case MemberKind.Lookup:
                    value = NormalizeEnumerable(
                        client, ownership, value, typeInfo, ctx, subject, ref resolvedIdentity);
                    break;
                case MemberKind.DialogueLookup:
                    value = NormalizeDialogueLookup(value, subject);
                    break;
                case MemberKind.Dictionary:
                    value = NormalizeDictionary(
                        client, ownership, value, typeInfo, ctx, subject);
                    break;
                case MemberKind.Reference:
                    if (value is NeoReference handed)
                        value = handed.Slot(ctx);
                    // P112 §1.1. A saved closure's capture holds a slot reference as its target's id.
                    else if (value is string targetId
                        && typeInfo is ReferenceTypeInfo reference
                        && NeoReferenceTypes.Category(reference.targetTypeInfo, client) == NeoReferenceCategory.Slot)
                    {
                        value = new NSSlotReference(targetId, null, reference.targetTypeInfo, null);
                    }
                    break;
            }

            ValidateRuntimeValue(client, value, typeInfo, subject);
            if (resolvedIdentity)
                ValidateResolvedIdentity(client, value, typeInfo, ctx, subject);
            return value;
        }

        private static RuntimeTuple NormalizeTuple(
            NeoClient client, NeoValueOwnership ownership, object value, TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx, ValueSubject subject)
        {
            TupleTypeInfo tupleType = TupleTypeInfo.From(typeInfo)
                ?? throw new InvalidOperationException($"Tuple {subject} has no element type information.");
            if (value is RuntimeTuple runtime)
                return (RuntimeTuple)NSGetterEvaluator.CoerceNumericBinding(runtime, tupleType, subject.ToString(), ctx)!;
            object?[]? captured = value is IDictionary<string, object?> record && record.TryGetValue("elements", out object? entries)
                ? entries as object?[] : null;
            var tuple = value as System.Runtime.CompilerServices.ITuple;
            if (captured is null && (tuple is null || !value.GetType().IsValueType))
                throw new InvalidOperationException($"Tuple {subject} requires a C# value tuple or serialized tuple capture.");
            int count = captured?.Length ?? tuple!.Length;
            if (count != tupleType.elements.Length)
                throw new InvalidOperationException($"Tuple {subject} has {count} elements; expected {tupleType.elements.Length}.");
            var elements = new object?[count];
            for (int i = 0; i < elements.Length; i++)
                elements[i] = Normalize(client, ownership, captured is null ? tuple![i] : captured[i], tupleType.elements[i].typeInfo, ctx, subject.Entry(i));
            return new RuntimeTuple(elements, tupleType);
        }

        internal static void ValidateRuntimeValue(
            NeoClient client,
            object? value,
            TypeInfo typeInfo,
            ValueSubject subject)
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
                MemberKind.Tuple => value is RuntimeTuple,
                MemberKind.Bool => value is bool,
                MemberKind.Int => IsIntegralNumber(value),
                MemberKind.Float => IsNumber(value),
                MemberKind.String => value is string,
                MemberKind.Decimal => value is string decimalText
                    && NeoDecimalValues.GetViolation(decimalText)
                        == NeoDecimalValues.Violation.None
                    || value is decimal,
                MemberKind.Enum => value is string
                    || value is IEnumerable
                    || EnumOptionId(value) is not null,
                MemberKind.Class when value is NeoVariantReference =>
                    NeoReferenceTypes.VariantSelectionMatches(client, value, typeInfo),
                MemberKind.Class or MemberKind.Interface =>
                    value is IDictionary<string, object?>
                    || value is INeoValueReference,
                MemberKind.List or MemberKind.Set or MemberKind.Lookup =>
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
            ValueSubject subject)
        {
            if (NSGetterEvaluator.IsIntrinsicSequence(value, typeInfo, ctx))
                return;
            ValidateRuntimeValue(client, value, typeInfo, subject);
            ValidateResolvedIdentity(client, value, typeInfo, ctx, subject);
        }

        /// <summary>
        /// <see cref="Normalize"/> followed by
        /// <see cref="ValidateResolvedRuntimeValue"/>, for a value returned
        /// across a public boundary. Normalization ends with the structural
        /// check, so only the resolved identity checks remain.
        /// </summary>
        internal static object? NormalizeResolved(
            NeoClient client,
            NeoValueOwnership ownership,
            object? value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx,
            in ValueSubject subject) =>
            Normalize(client, ownership, value, typeInfo, ctx, subject, resolvedIdentity: true);

        private static void ValidateResolvedIdentity(
            NeoClient client,
            object? value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx,
            ValueSubject subject)
        {
            if (value is null)
                return;

            switch (typeInfo.type)
            {
                case MemberKind.Tuple:
                    if (value is not RuntimeTuple tuple || TupleTypeInfo.From(typeInfo) is not { } tupleType)
                        throw new InvalidOperationException($"Tuple {subject} is malformed.");
                    for (int index = 0; index < tuple.Count; index++)
                        ValidateResolvedIdentity(client, tuple[index], tupleType.elements[index].typeInfo, ctx, subject.Entry(index));
                    return;
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
                        if (NeoReferenceTypes.VariantSelectionMatches(client, value, typeInfo))
                            return;
                        if (!NSGetterEvaluator.RuntimeClassExtends(value, expectedClassId!, ctx))
                        {
                            string? actualClassId =
                                NSGetterEvaluator.FindRowClassIdByReference(value, ctx);
                            throw new InvalidOperationException(
                                $"{subject} has runtime Class '{actualClassId ?? "<unbound>"}', expected '{expectedClassId}'.");
                        }
                        return;
                    }
                case MemberKind.Interface:
                    {
                        if (NSGetterEvaluator.IsIntrinsicSequence(value, typeInfo, ctx))
                            return;
                        string? interfaceId = typeInfo switch
                        {
                            InterfaceTypeInfo interfaceType => interfaceType.interfaceId,
                            FunctionArgumentTypeInfo argument => argument.interfaceId,
                            _ => null,
                        };
                        if (string.IsNullOrEmpty(interfaceId))
                        {
                            throw new InvalidOperationException(
                                $"{subject} is typed by an Interface type-info with no interface id.");
                        }
                        string? actualClassId =
                            NSGetterEvaluator.FindRowClassIdByReference(value, ctx);
                        if (string.IsNullOrEmpty(actualClassId))
                        {
                            throw new InvalidOperationException(
                                $"{subject} has no runtime Class, so it can't implement Interface '{interfaceId}'.");
                        }
                        if (!client.InterfaceImplementers.Implements(actualClassId!, interfaceId!))
                        {
                            throw new InvalidOperationException(
                                $"{subject} has runtime Class '{actualClassId}', which does not implement Interface '{interfaceId}'.");
                        }
                        return;
                    }
                case MemberKind.List:
                case MemberKind.Set:
                case MemberKind.Lookup:
                    {
                        TypeInfo? entryType = typeInfo switch
                        {
                            FunctionArgumentTypeInfo argument => argument.entryTypeInfo,
                            CollectionTypeInfo collection => collection.entryTypeInfo,
                            LookupTypeInfo lookup => lookup.entryTypeInfo,
                            _ => null,
                        };
                        if (entryType is null)
                            return;
                        NeoValueOwnership? rowOwnership =
                            NSGetterEvaluator.FindRowOwnershipByReference(value, ctx);
                        bool selectionIds = IsSelectionIdSet(typeInfo, entryType);
                        int index = 0;
                        // Arrays, the common case, iterate without boxing an enumerator.
                        if (value is object?[] entries)
                        {
                            foreach (object? entry in entries)
                                ValidateResolvedEntry(client, entry, entryType, selectionIds, rowOwnership, ctx, subject.Entry(index++));
                            return;
                        }
                        foreach (object? entry in (System.Collections.IEnumerable)value)
                            ValidateResolvedEntry(client, entry, entryType, selectionIds, rowOwnership, ctx, subject.Entry(index++));
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
                        if (entryType is null)
                            return;
                        if (value is not System.Collections.IDictionary dictionary)
                        {
                            throw new InvalidOperationException(
                                $"{subject} did not normalize to a dictionary.");
                        }
                        NeoValueOwnership? rowOwnership =
                            NSGetterEvaluator.FindRowOwnershipByReference(value, ctx);
                        foreach (System.Collections.DictionaryEntry entry in dictionary)
                        {
                            ValidateResolvedRuntimeValue(
                                client,
                                rowOwnership is null ? entry.Value
                                    : NSGetterEvaluator.ResolveValueIfId(entry.Value, ctx, rowOwnership),
                                entryType,
                                ctx,
                                subject.Key(entry.Key));
                        }
                        return;
                    }
            }
        }

        private static object UnwrapReference(
            NeoClient client,
            NeoValueOwnership ownership,
            object value,
            INeoValueReference reference,
            NSGetterEvaluator.Context ctx,
            ValueSubject subject)
        {
            // Preserve the original alias when a host caller already requested
            // the detached object's id, including objects inside argument arrays.
            NeoScriptObject? detached = value as NeoScriptObject
                ?? (value as NeoGeneratedClassValue)?.PendingValue as NeoScriptObject;
            if (detached is not null)
                return detached.attachedId is null ? detached : NSGetterEvaluator.ForwardDetached(detached, ctx)!;
            if (string.IsNullOrEmpty(reference.valueId))
                return value;
            // A record this context unwrapped from a row is already
            // canonical: unwrapping it again would only read its row.
            if (NSGetterEvaluator.IsRowRecord(value, ctx))
                return value;
            NeoValueOwnership referenceOwnership =
                value is NeoGeneratedClassValue generated
                    ? generated.ValueOwnership
                    : NSGetterEvaluator.FindRowOwnershipByReference(
                        value,
                        ctx) ?? ownership;
            return UnwrapValueReference(
                client,
                referenceOwnership,
                reference.valueId!,
                ctx,
                subject,
                NSGetterEvaluator.RecordNode(value, reference.valueId!, ctx));
        }

        private static object?[] NormalizeEnum(
            object value,
            TypeInfo typeInfo,
            ValueSubject subject)
        {
            object?[] optionIds = NormalizeEnumOptions(value, subject);
            if (typeInfo.required && optionIds.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Required {subject} has no enum option id.");
            }
            return optionIds;
        }

        private static object UnwrapValueReference(
            NeoClient client,
            NeoValueOwnership fallbackOwnership,
            string valueId,
            NSGetterEvaluator.Context ctx,
            ValueSubject subject,
            NeoValueNode? node = null)
        {
            if (client.ReadReplayReference(valueId, ref node, fallbackOwnership) is not { } row)
            {
                throw new InvalidOperationException(
                    $"Neo value '{valueId}' for {subject} was not found in {fallbackOwnership} storage.");
            }
            return NSGetterEvaluator.UnwrapRow(row, ctx, fallbackOwnership, node)
                ?? throw new InvalidOperationException(
                    $"Neo value '{valueId}' for {subject} resolved to null.");
        }

        private static object NormalizeEnumerable(
            NeoClient client,
            NeoValueOwnership ownership,
            object value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx,
            ValueSubject subject,
            ref bool resolvedIdentity)
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
            if (value is RuntimeSet set)
            {
                if (entryType is not null)
                    set.Validate(entryType, entry => Normalize(client, ownership, entry, entryType, ctx, subject.Entry()));
                return set;
            }
            NeoValueOwnership? rowOwnership = NSGetterEvaluator.FindRowOwnershipByReference(
                value, ctx, out NSGetterEvaluator.RowReference? rowRef);
            // Enum and dialogue sets carry selection ids directly, rather
            // than the one-element arrays used by their scalar members.
            if (IsSelectionIdSet(typeInfo, entryType))
            {
                object?[] selections = entryType!.type == MemberKind.Enum
                    ? NormalizeEnumOptions(value, subject)
                    : NormalizeDialogueLookup(value, subject, allowMultiple: true);
                return rowOwnership is not null && value is object?[] selectionRows
                    ? selectionRows
                    : NSGetterEvaluator.DeclaredRuntimeSet(selections, typeInfo, ctx)!;
            }
            // Row-backed collections contain child ids. Validate their values,
            // then preserve the collection identity for indexing and writes.
            // Each entry resolves once for both checks.
            if (rowOwnership is not null && value is object?[] rows)
            {
                if (entryType is null)
                    return rows;
                bool rowEntries = entryType.type is MemberKind.Class or MemberKind.Interface;
                Member? entryMember = rowRef?.EntryMember(client);
                for (int i = 0; i < rows.Length; i++)
                {
                    object? entry = NSGetterEvaluator.ResolveListEntry(rows, i, rowRef, rowOwnership, ctx, entryMember);
                    // A resolved row record is already canonical: normalizing
                    // it would only read its row again.
                    if (!rowEntries || !NSGetterEvaluator.IsRowRecord(entry, ctx))
                        Normalize(client, rowOwnership.Value, entry, entryType, ctx, subject.Entry());
                    if (resolvedIdentity)
                        ValidateResolvedRuntimeValue(client, entry, entryType, ctx, subject.Entry(i));
                }
                resolvedIdentity = false;
                return rows;
            }
            // Nested entries may still hold the array from before a mutation.
            // Normalize its current contents before advancing the shared identity.
            enumerable = (IEnumerable)NSGetterEvaluator.RuntimeCollection(value, ctx)!;
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
                        subject.Entry()));
            }
            // Fresh array per call: List.ToArray() returns the shared
            // Array.Empty singleton for empty lists, and origins key on identity.
            var normalized = new object?[result.Count];
            result.CopyTo(normalized);
            NeoGeneratedTypesSupport.PreserveConstructorCollectionOrigin(value, normalized);
            NSGetterEvaluator.KeepDerivedOf(value, normalized, entryType);
            if (typeInfo.type == MemberKind.List)
                NSGetterEvaluator.ReplaceRuntimeList(value, normalized);
            return NSGetterEvaluator.DeclaredRuntimeSet(normalized, typeInfo, ctx)!;
        }

        private static void ValidateResolvedEntry(
            NeoClient client,
            object? entry,
            TypeInfo entryType,
            bool selectionIds,
            NeoValueOwnership? rowOwnership,
            NSGetterEvaluator.Context ctx,
            ValueSubject entrySubject)
        {
            if (selectionIds)
            {
                if (entry is not string selectionId || string.IsNullOrEmpty(selectionId))
                    throw new InvalidOperationException(
                        $"{entrySubject} must be a nonempty selection id.");
                return;
            }
            ValidateResolvedRuntimeValue(
                client,
                rowOwnership is null ? entry
                    : NSGetterEvaluator.ResolveValueIfId(entry, ctx, rowOwnership),
                entryType,
                ctx,
                entrySubject);
        }

        private static bool IsSelectionIdSet(TypeInfo typeInfo, TypeInfo? entryType)
        {
            return typeInfo.type == MemberKind.Lookup
                && entryType?.type is MemberKind.Enum or MemberKind.DialogueLookup;
        }

        private static object?[] NormalizeDialogueLookup(
            object value,
            ValueSubject subject,
            bool allowMultiple = false)
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
            if (!allowMultiple && result.Count != 1)
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
                if (entry is not string || ++count > 1)
                    return false;
            }
            return count == 1;
        }

        private static Dictionary<string, object?> NormalizeDictionary(
            NeoClient client,
            NeoValueOwnership ownership,
            object value,
            TypeInfo typeInfo,
            NSGetterEvaluator.Context ctx,
            ValueSubject subject)
        {
            TypeInfo? entryType = typeInfo switch
            {
                FunctionArgumentTypeInfo argument => argument.entryTypeInfo,
                CollectionTypeInfo collection => collection.entryTypeInfo,
                _ => null,
            };
            NeoValueOwnership? rowOwnership = NSGetterEvaluator.FindRowOwnershipByReference(value, ctx);
            if (rowOwnership is not null && value is Dictionary<string, object?> rows)
            {
                if (entryType is not null)
                    foreach (object? entry in rows.Values)
                        Normalize(client, rowOwnership.Value,
                            NSGetterEvaluator.ResolveValueIfId(entry, ctx, rowOwnership),
                            entryType, ctx, subject.Entry());
                return rows;
            }
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
                return NSGetterEvaluator.KeepDerivedOf(value, result, entryType);
            }
            if (value is IEnumerable entries && value is not string)
            {
                foreach (object? entry in entries)
                {
                    if (entry is null)
                        continue;
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
                return NSGetterEvaluator.KeepDerivedOf(value, result, entryType);
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
            ValueSubject subject)
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
                    subject.DictionaryValue());
        }

        internal static string? EnumOptionId(object? value)
        {
            if (value is string text)
                return text;
            if (value is not System.Enum)
                return null;
            // Each enum type reflects its conversion once.
            return BoxedOptionIds.GetOrAdd(
                value.GetType(),
                type => (Func<object, string?>)typeof(NeoEnumOptions<>)
                    .MakeGenericType(type)
                    .GetMethod(
                        nameof(NeoEnumOptions<NeoValueOwnership>.BoxedOptionId),
                        System.Reflection.BindingFlags.Static
                            | System.Reflection.BindingFlags.NonPublic)!
                    .CreateDelegate(typeof(Func<object, string?>)))(value);
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object, string?>>
            BoxedOptionIds = new();

        /// <summary>The <paramref name="enumType"/> value an option id names; null for an enum without options.</summary>
        internal static object? EnumFromOptionId(Type enumType, string optionId)
        {
            // Each enum type reflects its conversion once.
            return BoxedEnumValues.GetOrAdd(
                enumType,
                type => (Func<string, object?>)typeof(NeoEnumOptions<>)
                    .MakeGenericType(type)
                    .GetMethod(
                        nameof(NeoEnumOptions<NeoValueOwnership>.BoxedFromOptionId),
                        System.Reflection.BindingFlags.Static
                            | System.Reflection.BindingFlags.NonPublic)!
                    .CreateDelegate(typeof(Func<string, object?>)))(optionId);
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<string, object?>>
            BoxedEnumValues = new();

        // A loop rather than Array.TrueForAll: every enum argument and
        // return passes here, and the predicate costs a delegate call per id.
        private static bool AllStrings(object?[] entries)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] is not string)
                    return false;
            }
            return true;
        }

        private static object?[] NormalizeEnumOptions(
            object value,
            ValueSubject subject)
        {
            if (value is string text)
                return new[] { text };
            // Enum values are never written in place, so an id array the
            // runtime built passes through as is. A caller's typed string[]
            // is copied: the caller may still change it.
            if (value is object?[] options
                && options.GetType() == typeof(object[])
                && AllStrings(options))
            {
                return options;
            }
            if (value is string[] typed && Array.TrueForAll(typed, entry => entry is not null))
                return (string[])typed.Clone();
            string? optionId = EnumOptionId(value);
            if (optionId is not null)
                return new[] { optionId };
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

        /// <summary>
        /// Whether <paramref name="value"/> is a float: any number, since a
        /// float holds NaN and both infinities as a C# float does (P127
        /// §12.1).
        /// </summary>
        private static bool IsNumber(object value) =>
            value is double or float or int or long or short;

        private static bool IsNonFinite(object? value) => value switch
        {
            double number => double.IsNaN(number) || double.IsInfinity(number),
            float number => float.IsNaN(number) || float.IsInfinity(number),
            _ => false,
        };

        internal static bool IsIntegralNumber(double number) =>
            !double.IsNaN(number)
            && !double.IsInfinity(number)
            && NeoNumbers.IsWhole(number);

        internal static bool IsIntegralNumber(object value)
        {
            // A NeoScript number is a double: test it first.
            return value switch
            {
                double number => IsIntegralNumber(number),
                int or long or short => true,
                float number => !float.IsNaN(number)
                    && !float.IsInfinity(number)
                    && NeoNumbers.IsWhole(number),
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

        /// <summary>
        /// Names a marshalled value in a failure message. An entry's subject
        /// formats only when its check fails; only an entry nested inside
        /// another entry formats its parent's subject up front.
        /// </summary>
        internal readonly struct ValueSubject
        {
            private readonly string subject;
            private readonly Kind kind;
            private readonly int index;
            private readonly object? key;

            private enum Kind : byte
            {
                Self,
                Entry,
                IndexedEntry,
                Key,
                DictionaryValue,
                NativeArgument,
                Closure,
            }

            private ValueSubject(string subject, Kind kind, int index, object key)
            {
                this.subject = subject;
                this.kind = kind;
                this.index = index;
                this.key = key;
            }

            // Leaves key null without storing it: a stored reference costs a
            // GC write barrier, and subjects are built on every normalization.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private ValueSubject(string subject, Kind kind, int index)
            {
                this = default;
                this.subject = subject;
                this.kind = kind;
                this.index = index;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static implicit operator ValueSubject(string subject) =>
                new(subject, Kind.Self, 0);

            internal ValueSubject Entry() => new(ToString(), Kind.Entry, 0);

            internal ValueSubject Entry(int index) => new(ToString(), Kind.IndexedEntry, index);

            internal ValueSubject Key(object key) => new(ToString(), Kind.Key, 0, key);

            internal ValueSubject DictionaryValue() => new(ToString(), Kind.DictionaryValue, 0);

            /// <summary>Argument <paramref name="index"/> of a native Function call.</summary>
            internal static ValueSubject NativeArgument(NeoClient.ResolvedNativeFunction function, int index) =>
                new(function.name, Kind.NativeArgument, index, function);

            /// <summary>The <paramref name="role"/> (argument or capture) <paramref name="index"/> of a NeoDelegate closure.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static ValueSubject Closure(string role, int index) => new(role, Kind.Closure, index);

            public override string ToString() => kind switch
            {
                Kind.Entry => $"entry of {subject}",
                Kind.IndexedEntry => $"entry {index} of {subject}",
                Kind.Key => $"key '{key}' of {subject}",
                Kind.DictionaryValue => $"dictionary value of {subject}",
                Kind.NativeArgument => ((NeoClient.ResolvedNativeFunction)key!).ArgumentSubject(index),
                Kind.Closure => $"{subject} {index} of NeoDelegate closure",
                _ => subject,
            };
        }
    }
}
