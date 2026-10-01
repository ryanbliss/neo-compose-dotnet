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

        public object? Invoke(string thisValueId, object?[] args, NeoScriptGridReads? gridReads = null)
        {
            args ??= Array.Empty<object?>();
            MemberValue row = ReceiverRow(thisValueId);
            NSGetterEvaluator.Context ctx = client.RentDirectFunctionContext(ownership);
            object receiver = UnwrapReceiver(row, ctx);
            NeoResolvedNSFunction function = ResolveInstanceFunction(receiver, ctx);
            ctx.gridReads = gridReads;
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
                NeoScriptExecutionOptions.ForImmediate(client), ownsContext: true);
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

        public Task<object?> InvokeAsync(string thisValueId, object?[] args, NeoScriptGridReads? gridReads = null)
        {
            args ??= Array.Empty<object?>();
            try
            {
                MemberValue row = ReceiverRow(thisValueId);
                NSGetterEvaluator.Context ctx = CreateDirectContext(ownership);
                object receiver = UnwrapReceiver(row, ctx);
                NeoResolvedNSFunction function = ResolveInstanceFunction(receiver, ctx);
                ctx.gridReads = gridReads;
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
                    NeoScriptExecutionOptions.ForDirectFunction(client), ownsContext: true);
                return AwaitExecution(result);
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
            if (string.IsNullOrWhiteSpace(thisValueId))
            {
                throw new ArgumentException(
                    "A non-empty receiver value id is required.",
                    nameof(thisValueId));
            }
            NeoValueNode? node = receiverNode;
            if (node is not null && !string.Equals(node.id, thisValueId, StringComparison.Ordinal))
                node = null;
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

        private NeoResolvedNSFunction ResolveInstanceFunction(object receiver, NSGetterEvaluator.Context ctx)
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
                ctx)!;
            return target.function ?? NeoNSFunctionRuntime.ResolveSignature(client, target.memberId);
        }

        /// <summary>Invokes a receiverless static NSFunction.</summary>
        public object? InvokeStatic(object?[] args)
        {
            args ??= Array.Empty<object?>();
            NeoResolvedNSFunction function = ResolveStaticFunction();
            if (function.Deferred)
            {
                throw new InvalidOperationException(
                    $"NSFunction '{function.Member.name}' is deferred; use InvokeStaticAsync.");
            }
            NSGetterEvaluator.Context ctx = client.RentDirectFunctionContext(NeoValueOwnership.Session);
            NeoScriptExecutionResult result = NeoNSFunctionRuntime.ExecuteResolved(
                client,
                function,
                receiver: null,
                args,
                ctx,
                NeoScriptExecutionOptions.ForImmediate(client), ownsContext: true);
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
        public Task<object?> InvokeStaticAsync(object?[] args)
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
                return AwaitExecution(NeoNSFunctionRuntime.ExecuteResolved(
                    client,
                    function,
                    receiver: null,
                    args,
                    ctx,
                    NeoScriptExecutionOptions.ForDirectFunction(client), ownsContext: true));
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

        private Task<object?> AwaitExecution(NeoScriptExecutionResult initial)
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
        internal NeoScriptValueMarshaller.ValueSubject[] Subjects => subjects ??= CreateSubjects();

        private NeoScriptValueMarshaller.ValueSubject[] CreateSubjects()
        {
            var created = new NeoScriptValueMarshaller.ValueSubject[ArgumentTypes.Length + 1];
            created[0] = $"return value of NSFunction '{Member.name}'";
            for (int i = 0; i < ArgumentTypes.Length; i++)
                created[i + 1] = $"argument {i} '{ArgumentTypes[i].name}' of NSFunction '{Member.name}'";
            return created;
        }
    }

    internal static class NeoNSFunctionRuntime
    {
        private const int MaxCallableDepth = 64;

        /// <summary>
        /// Calls immediate instance NSFunction <paramref name="schemaKey"/>
        /// with a pending temporary as <c>this</c>, as a NeoScript call on it
        /// would, so a call through its C# view makes no rows. The
        /// temporary's own Class picks the override.
        /// </summary>
        internal static object? InvokeDetached(NeoScriptObject receiver, string schemaKey, object?[] args)
        {
            NeoClient client = receiver.client;
            string memberId = client.ResolveClassNode(receiver.plan.classId).SurfaceMember(schemaKey)?.memberId
                ?? throw new NSGetterRuntimeError(
                    $"Class '{receiver.plan.classId}' has no NSFunction '{schemaKey}'.");
            NeoResolvedNSFunction function = ResolveSignature(client, memberId);
            if (function.Deferred)
            {
                throw new InvalidOperationException(
                    $"NSFunction '{function.Member.name}' is deferred; use InvokeAsync.");
            }
            NSGetterEvaluator.Context ctx = client.RentDirectFunctionContext(NeoValueOwnership.Session);
            NeoScriptExecutionResult result = ExecuteResolved(
                client,
                function,
                receiver,
                args,
                ctx,
                NeoScriptExecutionOptions.ForImmediate(client), ownsContext: true);
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
            NeoScriptScope? siteScope = null)
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
            TypeInfo effectiveReturnType = function.ReturnTypeInfo;
            TypeInfo[] effectiveArgumentTypes = function.ArgumentTypes;
            if (function.HasGenericSignature)
            {
                effectiveReturnType = ResolveInvocationSignature(
                    client,
                    function,
                    receiver!,
                    ctx,
                    out effectiveArgumentTypes);
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
                BindParameters(client, function, action, scope, receiver, args, site, siteScope, ctx, effectiveArgumentTypes);

                NSGetterEvaluator.Context nestedCtx;
                if (ownsContext)
                {
                    ctx.BindFunction(function.DirectCallStack, isStatic ? null : receiver);
                    nestedCtx = ctx;
                }
                else if (!function.Deferred)
                {
                    inPlaceFrame = ctx.EnterFunction(function.MemberId, function.DirectCallStack, isStatic ? null : receiver);
                    nestedCtx = ctx;
                }
                else
                {
                    // A suspended deferred frame outlives this call.
                    nestedCtx = ctx.WithFunctionPushed(function.MemberId, function.DirectCallStack, isStatic ? null : receiver);
                }
                NeoScriptExecutionOptions functionOptions = options.ForFunction(function.Deferred);
                NeoScriptExecutor.PrepareFunctionContext(nestedCtx, functionOptions, inPlaceFrame);
                // Only the C# entry points own their context.
                bool boundary = ownsContext;
                NeoScriptTerminalNormalizer normalizer;
                if (!ReferenceEquals(effectiveReturnType, function.ReturnTypeInfo))
                    normalizer = CreateTerminalNormalizer(client, function, effectiveReturnType, boundary);
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
            bool isStatic = function.IsStatic;
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
                throw new NSGetterRuntimeError(
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
            out TypeInfo[] argumentTypes)
        {
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv =
                ResolveReceiverGenericEnv(client, receiver, ctx, function);
            TypeInfo returnType = ResolveInvocationTypeInfo(
                client,
                function.ReturnTypeInfo,
                genericEnv,
                new HashSet<string>());
            argumentTypes = new TypeInfo[function.ArgumentTypes.Length];
            for (int i = 0; i < function.ArgumentTypes.Length; i++)
            {
                argumentTypes[i] = ResolveInvocationTypeInfo(
                    client,
                    function.ArgumentTypes[i],
                    genericEnv,
                    new HashSet<string>());
            }
            return returnType;
        }

        /// <summary>The leading parameter slots <see cref="BindParameters"/> binds on every call.</summary>
        private static int BoundParameterCount(NeoResolvedNSFunction function) => function.ArgumentTypes.Length + 2;

        private static void BindParameters(
            NeoClient client,
            NeoResolvedNSFunction function,
            FunctionWithReturnType action,
            NeoScriptScope scope,
            object? receiver,
            object?[] args,
            CallFunctionPointer? site,
            NeoScriptScope? siteScope,
            NSGetterEvaluator.Context ctx,
            TypeInfo[] argumentTypes)
        {
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
                        : NSGetterEvaluator.EvaluateFunctionArgument(site, i, siteScope!, ctx);
                try
                {
                    if (i >= argumentCount)
                        value = NeoParameterDefaults.DefaultRuntimeValue(argument, function.CallSubject);
                    // A pattern argument left as offsets stays that way
                    // when the body only hands it to grid queries.
                    if (value is NeoCellPattern pattern)
                    {
                        if (NeoCellPatternRuntime.OnlyQueriesGrid(action, i + argumentParameterOffset))
                        {
                            scope.SetParameter(i + argumentParameterOffset, pattern);
                            continue;
                        }
                        value = NeoCellPatternStorage.Materialize(pattern, ctx);
                    }
                    scope.SetParameter(i + argumentParameterOffset, NeoScriptValueMarshaller.Normalize(
                        client,
                        ctx.valueOwnership,
                        value,
                        argumentTypes[i],
                        ctx,
                        in subjects[i + 1]));
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
        // non-generic signatures build it once per resolved function.
        private static NeoScriptTerminalNormalizer CreateTerminalNormalizer(
            NeoClient client,
            NeoResolvedNSFunction function,
            TypeInfo effectiveReturnType,
            bool boundary) =>
            (terminal, ctx) => NormalizeTerminal(client, ctx, terminal, function, effectiveReturnType, boundary);

        private static NeoScriptExecutionResult NormalizeTerminal(
            NeoClient client,
            NSGetterEvaluator.Context ctx,
            NeoScriptExecutionResult execution,
            NeoResolvedNSFunction function,
            TypeInfo effectiveReturnType,
            bool boundary)
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
            object? normalized = NeoScriptValueMarshaller.Normalize(
                client,
                ctx.valueOwnership,
                execution.ReturnValue,
                effectiveReturnType,
                ctx,
                in function.Subjects[0],
                resolvedIdentity: boundary);
            // Marshalling usually returns the evaluator's own value; reuse
            // the executor's result instead of allocating a copy of it.
            if (ReferenceEquals(normalized, execution.ReturnValue))
                return execution;
            return NeoScriptExecutionResult.Completed(
                returned: true,
                normalized);
        }

        internal static bool ContainsGeneric(TypeInfo typeInfo)
        {
            if (typeInfo.type == MemberKind.Generic)
                return true;
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

            IReadOnlyDictionary<string, GenericBinding>? constructedArguments = null;
            if (!string.IsNullOrEmpty(receiverValueId)
                && client.TryInferMemberForValueId(
                    receiverValueId!,
                    out JsonMember? placementMember)
                && placementMember is ClassMember classPlacement)
            {
                constructedArguments = classPlacement.classArguments;
            }

            // A persisted closure closes forwarded placement parameters.
            NeoValueOwnership receiverOwnership = NSGetterEvaluator.FindRowOwnershipByReference(receiver, ctx) ?? ctx.valueOwnership;
            if (receiverValueId is not null && client.TryGetValue(receiverOwnership, receiverValueId, out MemberValue? row)
                && row.genericBindings is not null)
            {
                constructedArguments = NeoGenericResolution.CloseClassArgumentsFromStamp(
                    row.genericBindings, constructedArguments);
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
                TypeInfo resolved = TypeInfoFromBindingMember(
                    client,
                    bindingMember,
                    genericEnv,
                    visitingMembers);
                resolved.required &= typeInfo.required;
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
                    collectionMemberId = collectionMemberId,
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
            if (typeArguments is null)
                return null;
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
                                required = generic.Requirement == NeoMemberRequirementKind.Required,
                                genericParamId = generic.genericParamId,
                            },
                            genericEnv,
                            visitingMembers);
                    case ClassMember classMember:
                        return new ClassTypeInfo
                        {
                            type = MemberKind.Class,
                            required = classMember.Requirement == NeoMemberRequirementKind.Required,
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
                            required = enumMember.Requirement == NeoMemberRequirementKind.Required,
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
                                required = lookup.Requirement == NeoMemberRequirementKind.Required,
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
                                required = delegateMember.Requirement == NeoMemberRequirementKind.Required,
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
                        return new PrimitiveTypeInfo
                        {
                            type = member.kind,
                            required = member.Requirement == NeoMemberRequirementKind.Required,
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
            if (bindings is null)
                return null;
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
                required = collection.Requirement == NeoMemberRequirementKind.Required,
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
        internal static object? ResolveRoot(
            NeoClient client,
            NSGetterEvaluator.Context ctx)
        {
            return new RuntimeRoot(client, ctx);
        }

        // Binding the three root names is not a read of their values. Resolve
        // only the root the script accesses, so constructor dependency capture
        // does not subscribe every constructor to all three global graphs.
        private sealed class RuntimeRoot : IDictionary<string, object?>
        {
            private readonly NeoClient client;
            private readonly NSGetterEvaluator.Context context;
            private static readonly string[] names = { "Assets", "Save", "Session" };
            internal RuntimeRoot(NeoClient client, NSGetterEvaluator.Context context)
            {
                this.client = client;
                this.context = context;
            }
            public bool TryGetValue(string key, out object? value)
            {
                NeoMemberClass? node = key switch
                {
                    "Assets" => client.assets,
                    "Save" => client.save,
                    "Session" => client.session,
                    _ => null,
                };
                if (node is null)
                {
                    value = null;
                    return false;
                }
                NeoValueOwnership ownership = key == "Assets" ? NeoValueOwnership.Asset
                    : key == "Save" ? NeoValueOwnership.Save : NeoValueOwnership.Session;
                value = node.value is ObjectMemberValue row
                    && client.TryGetValue(ownership, row.id, out ObjectMemberValue? current)
                    ? NSGetterEvaluator.UnwrapRow(current, context, ownership) : null;
                return true;
            }
            public object? this[string key]
            {
                get => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);
                set => throw new NotSupportedException();
            }
            public ICollection<string> Keys => Array.AsReadOnly(names);
            public ICollection<object?> Values => new[] { this["Assets"], this["Save"], this["Session"] };
            public int Count => 3;
            public bool IsReadOnly => true;
            public bool ContainsKey(string key) => key is "Assets" or "Save" or "Session";
            public bool Contains(KeyValuePair<string, object?> item) =>
                TryGetValue(item.Key, out var value) && Equals(value, item.Value);
            public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
            {
                foreach (string key in names)
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
            // Primitives, value references and enum ids match none of the
            // rewrites in NormalizeRewritten, so they skip its chain.
            switch (typeInfo.type)
            {
                // A value of its own kind is already valid.
                case MemberKind.Bool when value is bool:
                case MemberKind.Float when value is double && IsNumber(value):
                case MemberKind.String when value is string:
                case MemberKind.Int when value is double && IsIntegralNumber(value):
                    return value;
                case MemberKind.Bool or MemberKind.Int or MemberKind.Float or MemberKind.String
                    when value is double or bool or string or int:
                    break;
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
            ValidateRuntimeValue(value, typeInfo, subject);
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
            if (value is NeoCellPattern pattern && typeInfo.type == MemberKind.Class)
                value = NeoCellPatternStorage.Materialize(pattern, ctx);
            if (value is NeoCellPatternExcluding excluding && typeInfo.type == MemberKind.Enum)
                value = NeoCellPatternStorage.ExcludingIds(excluding);
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
            else if (value is INeoValueReference reference)
            {
                value = UnwrapReference(client, ownership, value, reference, ctx, subject);
            }

            switch (typeInfo.type)
            {
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
                    if (NeoGeneratedTypesSupport.ReadVector2IntValue(value) is Vector2Int normalizedVector2Int)
                        value = NeoGeneratedTypesSupport.Vector2IntValue(normalizedVector2Int);
                    break;
                case MemberKind.Vector3:
                    if (NeoGeneratedTypesSupport.ReadVector3Value(value) is Vector3 normalizedVector3)
                        value = NeoGeneratedTypesSupport.Vector3Value(normalizedVector3);
                    break;
                case MemberKind.Vector3Int:
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
            }

            ValidateRuntimeValue(value, typeInfo, subject);
            if (resolvedIdentity)
                ValidateResolvedIdentity(client, value, typeInfo, ctx, subject);
            return value;
        }

        internal static void ValidateRuntimeValue(
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
            ValueSubject subject)
        {
            ValidateRuntimeValue(value, typeInfo, subject);
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
            ValueSubject subject) =>
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
            // A detached temporary passes by reference, like the row it
            // stands in for; nothing about it needs a row here.
            if (value is NeoScriptObject { attachedId: null } || string.IsNullOrEmpty(reference.valueId))
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

        private static object?[] NormalizeEnumerable(
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
                    : selections;
            }
            // Row-backed collections contain child ids. Validate their values,
            // then preserve the collection identity for indexing and writes.
            // Each entry resolves once for both checks.
            if (rowOwnership is not null && value is object?[] rows)
            {
                if (entryType is null)
                    return rows;
                bool rowEntries = entryType.type is MemberKind.Class or MemberKind.Interface;
                for (int i = 0; i < rows.Length; i++)
                {
                    object? entry = NSGetterEvaluator.ResolveListEntry(rows, i, rowRef, rowOwnership, ctx);
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
            return normalized;
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
                return result;
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
            if (value is INeoEnumOption option)
                return option.optionId;
            if (value is null)
                return null;
            // Duck-typed option wrappers expose a public string optionId.
            // Type lookups dominate, so each type reflects once.
            var property = OptionIdProperties.GetOrAdd(
                value.GetType(),
                type => type.GetProperty(
                    "optionId",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public) is { } found
                    && found.PropertyType == typeof(string)
                        ? found
                        : null);
            return property?.GetValue(value) as string;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo?>
            OptionIdProperties = new();

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

        internal static bool IsIntegralNumber(object value)
        {
            return value switch
            {
                int or long or short => true,
                double number => !double.IsNaN(number)
                    && !double.IsInfinity(number)
                    && NeoNumbers.IsWhole(number),
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
