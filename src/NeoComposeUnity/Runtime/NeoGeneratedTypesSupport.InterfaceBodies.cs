// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Threading.Tasks;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    public enum NeoInterfaceBody
    {
        Function = 0, Get = 1, Set = 2
    }

    public static partial class NeoGeneratedTypesSupport
    {
        public static object? InvokeInterfaceMember(NeoClient client, object? receiver, string interfaceMemberId,
            NeoInterfaceBody body, TypeInfo construction, object?[] args, TypeInfo[]? typeArguments = null)
        {
            var closed = ConstructionIdentity(construction);
            Member implementation = InterfaceBodyMember(client, closed, interfaceMemberId, body);
            var ctx = client.RentDirectFunctionContext(receiver is NeoGeneratedClassValue generated
                ? generated.StorageOwnership : NeoValueOwnership.Session, function: body == NeoInterfaceBody.Function);
            object? result = null;
            try
            {
                object? target = InterfaceBodyReceiver(client, receiver, implementation, closed, ctx);
                if (body == NeoInterfaceBody.Function)
                {
                    var function = NeoNSFunctionRuntime.ResolveSignature(client, implementation.id);
                    if (function.Deferred)
                        throw new InvalidOperationException($"Interface function '{interfaceMemberId}' is deferred; use InvokeInterfaceMemberAsync.");
                    var execution = NeoNSFunctionRuntime.ExecuteResolved(client, function, target, args, ctx,
                        NeoScriptExecutionOptions.ForImmediate(client), ownsContext: true, typeArguments: typeArguments,
                        boundStaticConstruction: function.IsStatic ? closed : null,
                        boundInterfaceConstruction: function.IsStatic ? null : closed);
                    if (execution.IsPaused)
                    {
                        execution.Deferred?.DisposeFromOwner("synchronous interface invocation suspended");
                        throw new InvalidOperationException($"Interface function '{interfaceMemberId}' suspended.");
                    }
                    return result = execution.ReturnValue;
                }
                var property = (NSPropertyMember)implementation;
                ctx.BindThis(target);
                if (body == NeoInterfaceBody.Get)
                {
                    if (args.Length != 0)
                        throw new ArgumentException("An interface getter takes no arguments.", nameof(args));
                    return result = GeneratedResult(
                        NSGetterEvaluator.Evaluate(property.getter!, ctx, Array.Empty<object?>(), handlerFrame: -1),
                        NSGetterEvaluator.CloseContextType(property.returnTypeInfo!, ctx), ctx);
                }
                if (args.Length != 1)
                    throw new ArgumentException("An interface setter takes one argument.", nameof(args));
                var parameterType = NSGetterEvaluator.CloseContextType(property.returnTypeInfo!, ctx);
                object? value = NeoScriptValueMarshaller.Normalize(client, ctx.valueOwnership, args[0], parameterType, ctx, "interface setter value");
                ctx.PushSetter(property.id, target);
                var assigned = NeoScriptExecutor.ExecuteSetter(client, property.setter!, value, ctx,
                    NeoScriptExecutionOptions.ForImmediate(client));
                if (assigned.IsPaused)
                {
                    assigned.Deferred?.DisposeFromOwner("synchronous interface setter suspended");
                    throw new InvalidOperationException($"Interface setter '{interfaceMemberId}' suspended.");
                }
                return null;
            }
            finally
            {
                client.ReturnDirectFunctionContext(ctx, result);
            }
        }

        public static Task<object?> InvokeInterfaceMemberAsync(NeoClient client, object? receiver, string interfaceMemberId,
            NeoInterfaceBody body, TypeInfo construction, object?[] args, TypeInfo[]? typeArguments = null)
        {
            try
            {
                var closed = ConstructionIdentity(construction);
                Member implementation = InterfaceBodyMember(client, closed, interfaceMemberId, body);
                if (body != NeoInterfaceBody.Function)
                    return Task.FromResult(InvokeInterfaceMember(client, receiver, interfaceMemberId, body, construction, args, typeArguments));
                var function = NeoNSFunctionRuntime.ResolveSignature(client, implementation.id);
                if (!function.Deferred)
                    throw new InvalidOperationException($"Interface function '{interfaceMemberId}' is immediate; use InvokeInterfaceMember.");
                var ctx = client.CreateGetterContext(receiver is NeoGeneratedClassValue generated
                    ? generated.StorageOwnership : NeoValueOwnership.Session);
                ctx.BindRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx));
                object? target = InterfaceBodyReceiver(client, receiver, implementation, closed, ctx);
                return NeoMemberNSFunction.AwaitExecution(client, NeoNSFunctionRuntime.ExecuteResolved(client,
                    function, target, args, ctx, NeoScriptExecutionOptions.ForDirectFunction(client), ownsContext: true,
                    typeArguments: typeArguments, boundStaticConstruction: function.IsStatic ? closed : null,
                    boundInterfaceConstruction: function.IsStatic ? null : closed));
            }
            catch (Exception exception)
            {
                return Task.FromException<object?>(exception);
            }
        }

        private static Member InterfaceBodyMember(NeoClient client, TypeBinding construction, string memberId, NeoInterfaceBody body)
        {
            var declaration = NeoInterfaceDispatch.Of(client).Declaration(construction, memberId).Member;
            if (declaration.implementationMemberId is not string id || !client.TryGetMember(id, out Member? member)
                || !(body switch
                {
                    NeoInterfaceBody.Function => member is NSFunctionMember { action: not null },
                    NeoInterfaceBody.Get => member is NSPropertyMember { getter: not null },
                    NeoInterfaceBody.Set => member is NSPropertyMember { setter: not null },
                    _ => false,
                }))
                throw new InvalidOperationException($"Interface member '{memberId}' has no declared {body} body.");
            return member!;
        }

        private static object? InterfaceBodyReceiver(NeoClient client, object? receiver, Member implementation,
            TypeBinding construction, NSGetterEvaluator.Context ctx)
        {
            bool isStatic = implementation.Modifier == NeoMemberModifierKind.Static;
            if (isStatic)
            {
                client.EnsureStaticConstruction(construction);
                ctx.staticConstruction = construction;
                return null;
            }
            if (receiver is null)
                throw new ArgumentNullException(nameof(receiver));
            if (receiver is NeoGeneratedClassValue generated && !ReferenceEquals(client, generated.Client))
                throw new InvalidOperationException("An interface receiver belongs to a different project client.");
            ctx.interfaceConstruction = construction;
            return NSGetterEvaluator.UnwrapGeneratedValue(receiver, ctx);
        }
    }
}
