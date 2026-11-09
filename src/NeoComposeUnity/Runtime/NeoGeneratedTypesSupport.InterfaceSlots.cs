// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Runtime;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.Scripting;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    public static partial class NeoGeneratedTypesSupport
    {
        public static object? InvokeInterfaceSlot(NeoClient client, object? receiver, string slotMemberId,
            NeoInterfaceBody body, TypeInfo slotConstruction, string callerMemberId, TypeInfo callerConstruction,
            object?[] args, TypeInfo[]? typeArguments = null)
        {
            if (callerMemberId is null)
                throw new ArgumentNullException(nameof(callerMemberId));
            if (callerConstruction is null)
                throw new ArgumentNullException(nameof(callerConstruction));
            if (TrustRuntimeInterfaceDispatch)
                return InvokeInterfaceMember(client, receiver, callerMemberId, body, callerConstruction, args, typeArguments);
            var ctx = client.RentDirectFunctionContext(receiver is NeoGeneratedClassValue generated
                ? generated.StorageOwnership : NeoValueOwnership.Session, function: body == NeoInterfaceBody.Function);
            object? result = null;
            try
            {
                object? target = receiver;
                var implementation = ResolveInterfaceSlot(client, ref target, slotMemberId, body, slotConstruction,
                    args, ctx, out var contract);
                if (body == NeoInterfaceBody.Get)
                    return result = GeneratedResult(NSGetterEvaluator.ReadImplementation(implementation, target, ctx),
                        NSGetterEvaluator.CloseContextType(contract.typeInfo!, ctx), ctx);
                if (body == NeoInterfaceBody.Set)
                {
                    var type = NSGetterEvaluator.CloseContextType(contract.typeInfo!, ctx);
                    object? value = NeoScriptValueMarshaller.Normalize(client, ctx.valueOwnership, args[0], type, ctx, "interface setter value");
                    NSGetterEvaluator.WriteImplementation(implementation, target, value, type, new NeoScriptScope(), ctx);
                    return null;
                }
                var call = implementation.CallTarget ??= new NSGetterEvaluator.CallSiteTarget(client, null, implementation.MemberId, null);
                var function = call.function;
                if (function?.Deferred ?? call.nativeFunction?.signature.Dispatch == NeoFunctionDispatchKind.Asynchronous)
                    throw new InvalidOperationException($"Interface function '{slotMemberId}' is deferred; use InvokeInterfaceSlotAsync.");
                if (function is null)
                    return result = NSGetterEvaluator.InvokeNativeFunction(implementation.MemberId, call.nativeFunction, target, args, ctx,
                        delegateTypeArguments: typeArguments);
                var execution = NeoNSFunctionRuntime.ExecuteResolved(client, function, target, args, ctx,
                    NeoScriptExecutionOptions.ForImmediate(client), ownsContext: true, typeArguments: typeArguments,
                    boundInterfaceConstruction: implementation.InterfaceConstruction);
                if (execution.IsPaused)
                {
                    execution.Deferred?.DisposeFromOwner("synchronous interface invocation suspended");
                    throw new InvalidOperationException($"Interface function '{slotMemberId}' suspended.");
                }
                return result = execution.ReturnValue;
            }
            finally
            {
                client.ReturnDirectFunctionContext(ctx, result);
            }
        }

        public static Task<object?> InvokeInterfaceSlotAsync(NeoClient client, object? receiver, string slotMemberId,
            NeoInterfaceBody body, TypeInfo slotConstruction, string callerMemberId, TypeInfo callerConstruction,
            object?[] args, TypeInfo[]? typeArguments = null)
        {
            try
            {
                if (callerMemberId is null)
                    throw new ArgumentNullException(nameof(callerMemberId));
                if (callerConstruction is null)
                    throw new ArgumentNullException(nameof(callerConstruction));
                if (TrustRuntimeInterfaceDispatch)
                    return InvokeInterfaceMemberAsync(client, receiver, callerMemberId, body, callerConstruction, args, typeArguments);
                if (body != NeoInterfaceBody.Function)
                    return Task.FromResult(InvokeInterfaceSlot(client, receiver, slotMemberId, body, slotConstruction, callerMemberId, callerConstruction, args, typeArguments));
                var ctx = client.CreateGetterContext(receiver is NeoGeneratedClassValue generated
                    ? generated.StorageOwnership : NeoValueOwnership.Session);
                ctx.BindRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx));
                object? target = receiver;
                var implementation = ResolveInterfaceSlot(client, ref target, slotMemberId, body, slotConstruction,
                    args, ctx, out _);
                var call = implementation.CallTarget ??= new NSGetterEvaluator.CallSiteTarget(client, null, implementation.MemberId, null);
                var function = call.function;
                if (!(function?.Deferred ?? call.nativeFunction?.signature.Dispatch == NeoFunctionDispatchKind.Asynchronous))
                    throw new InvalidOperationException($"Interface function '{slotMemberId}' is immediate; use InvokeInterfaceSlot.");
                if (function is null)
                    return client.InvokeDeferredNativeFunction<object?>(implementation.MemberId, target, args, typeArguments);
                return NeoMemberNSFunction.AwaitExecution(client, NeoNSFunctionRuntime.ExecuteResolved(client,
                    function, target, args, ctx, NeoScriptExecutionOptions.ForDirectFunction(client), ownsContext: true,
                    typeArguments: typeArguments, boundInterfaceConstruction: implementation.InterfaceConstruction));
            }
            catch (Exception exception)
            {
                return Task.FromException<object?>(exception);
            }
        }

        private static NeoInterfaceDispatch.Implementation ResolveInterfaceSlot(NeoClient client, ref object? receiver,
            string slotMemberId, NeoInterfaceBody body, TypeInfo slotConstruction, object?[] args,
            NSGetterEvaluator.Context ctx, out InterfaceMember contract)
        {
            if (receiver is null)
                throw new ArgumentNullException(nameof(receiver));
            if (receiver is NeoGeneratedClassValue generated && !ReferenceEquals(client, generated.Client))
                throw new InvalidOperationException("An interface receiver belongs to a different project client.");
            receiver = NSGetterEvaluator.UnwrapGeneratedValue(receiver, ctx);
            var receiverType = NSGetterEvaluator.InterfaceReceiverType(receiver!, ctx);
            var construction = ConstructionIdentity(slotConstruction);
            var dispatch = NeoInterfaceDispatch.Of(client);
            contract = dispatch.Declaration(construction, slotMemberId).Member;
            if (contract.IsStatic)
                throw new ArgumentException($"Interface member '{slotMemberId}' is static; InvokeInterfaceSlot runs instance slots.");
            string accessor = body switch
            {
                NeoInterfaceBody.Get => "get",
                NeoInterfaceBody.Set => "set",
                NeoInterfaceBody.Function => "call",
                _ => throw new ArgumentException("Unknown interface body kind.", nameof(body)),
            };
            if ((body == NeoInterfaceBody.Function) != (contract.kind == NeoInterfaceMemberKind.Function))
                throw new ArgumentException($"Interface member '{slotMemberId}' does not have a {body} body.", nameof(body));
            if (body == NeoInterfaceBody.Get && args.Length != 0)
                throw new ArgumentException("An interface getter takes no arguments.", nameof(args));
            if (body == NeoInterfaceBody.Set && args.Length != 1)
                throw new ArgumentException("An interface setter takes one argument.", nameof(args));
            if (body == NeoInterfaceBody.Function && args.Length != contract.argumentTypes!.Count)
            {
                try
                {
                    NeoParameterDefaults.ValidateArity(args.Length, contract.argumentTypes.ToArray(), $"Interface function '{slotMemberId}'");
                }
                catch (NSGetterRuntimeError error)
                {
                    throw new ArgumentException(error.Message, nameof(args), error);
                }
            }
            ctx.BindThis(receiver);
            ctx.interfaceConstruction = construction;
            try
            {
                return dispatch.Resolve(receiverType, construction, slotMemberId, accessor, direct: false);
            }
            catch (NeoAmbiguousImplementationError error)
            {
                // Kept separate from execution: an ambiguity in an authored
                // body is not a failure to resolve this entry's slot.
                throw new AmbiguousImplementationException(error.Message);
            }
            catch (NeoMissingInterfaceImplementationError error)
            {
                throw new InvalidOperationException($"Interface member '{slotMemberId}' has no implementation for '{receiverType}'.", error);
            }
        }

        private static class RuntimeInterfaceDispatchProbe
        {
            internal static readonly bool Trusted = ProbeRuntimeInterfaceDispatch();
        }

        // The diamond classifies the runtime, not each individual request.
        // It does not establish generated-C# parity for every IL2CPP shape.
        internal static bool TrustRuntimeInterfaceDispatch
        {
            get
            {
#if UNITY_EDITOR
                if (TestTrustRuntimeInterfaceDispatch is bool decision)
                    return decision;
#endif
                return RuntimeInterfaceDispatchProbe.Trusted;
            }
        }
#if UNITY_EDITOR
        [ThreadStatic]
        internal static bool? TestTrustRuntimeInterfaceDispatch;
        internal static int TestAmbiguityProbeRuns;
#endif

        private static bool ProbeRuntimeInterfaceDispatch()
        {
#if UNITY_EDITOR
            TestAmbiguityProbeRuns++;
#endif
            return ProbeRuntimeInterfaceDispatch(() => ((INeoInterfaceDispatchProbeJ<object>)new NeoInterfaceDispatchProbeC<object, object>()).V);
        }

        internal static bool ProbeRuntimeInterfaceDispatch(Func<int> probe)
        {
            try
            {
                _ = probe();
                return false;
            }
            catch (AmbiguousImplementationException)
            {
                return true;
            }
            catch (Exception)
            {
                // Unsupported runtime behavior must select SDK resolution,
                // not poison the holder with a TypeInitializationException.
                return false;
            }
        }

        [Preserve]
        internal interface INeoInterfaceDispatchProbeJ<out T>
        {
            [Preserve]
            int V => 0;
        }

        [Preserve]
        internal interface INeoInterfaceDispatchProbeA<out T, U> : INeoInterfaceDispatchProbeJ<T>
        {
            [Preserve]
            int INeoInterfaceDispatchProbeJ<T>.V => 1;
        }

        [Preserve]
        internal class NeoInterfaceDispatchProbeB<T> : INeoInterfaceDispatchProbeA<T, int>
        {
        }

        [Preserve]
        internal class NeoInterfaceDispatchProbeC<T, U> : NeoInterfaceDispatchProbeB<T>, INeoInterfaceDispatchProbeA<U, string>
        {
        }
    }
}
