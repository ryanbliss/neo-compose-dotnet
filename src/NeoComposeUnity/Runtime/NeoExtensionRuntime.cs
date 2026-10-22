// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    public static class NeoExtensionRuntime
    {
        /// <summary>The receiver's owning client, or null for an unattached CLR value.</summary>
        public static NeoClient? ClientOf(object? receiver) => receiver switch
        {
            NeoNode node => node.Client,
            INeoCollectionView collection => collection.CollectionClient,
            NeoScriptObject value => value.client,
            NSGetterEvaluator.ScriptSequence sequence => sequence.Owner,
            NSGetterEvaluator.ScriptCursor cursor => cursor.Owner,
            NeoSequences.IScriptSequenceView view => view.Owner,
            _ => null,
        };

        /// <summary>Invokes an immediate extension by its stable member ID.</summary>
        public static object? Invoke(NeoClient client, string memberId, object? receiver, object?[] args, TypeInfo[]? typeArguments = null)
        {
            NeoResolvedNSFunction function = NeoNSFunctionRuntime.ResolveSignature(client, memberId);
            if (function.Deferred)
                throw new InvalidOperationException($"Extension '{function.Member.name}' is deferred; use InvokeAsync.");
            var ctx = CreateContext(client, receiver);
            object? normalized = NormalizeReceiver(function.Member, receiver, typeArguments, ctx, out TypeInfo[]? table);
            NeoScriptExecutionResult result = NeoNSFunctionRuntime.ExecuteResolved(
                client, function, normalized, args, ctx, NeoScriptExecutionOptions.ForImmediate(client),
                ownsContext: true, typeArguments: table);
            if (result.IsPaused)
            {
                result.Deferred?.DisposeFromOwner("synchronous extension invocation suspended");
                throw new NSGetterRuntimeError($"Immediate extension '{function.Member.name}' suspended.");
            }
            client.ReturnDirectFunctionContext(ctx, result.ReturnValue);
            return result.ReturnValue;
        }

        /// <summary>Invokes a deferred extension, retaining its receiver until settlement.</summary>
        public static Task<object?> InvokeAsync(NeoClient client, string memberId, object? receiver, object?[] args, TypeInfo[]? typeArguments = null)
        {
            try
            {
                NeoResolvedNSFunction function = NeoNSFunctionRuntime.ResolveSignature(client, memberId);
                if (!function.Deferred)
                    throw new InvalidOperationException($"Extension '{function.Member.name}' is immediate; use Invoke.");
                var ctx = CreateContext(client, receiver);
                object? normalized = NormalizeReceiver(function.Member, receiver, typeArguments, ctx, out TypeInfo[]? table);
                return NeoMemberNSFunction.AwaitExecution(client, NeoNSFunctionRuntime.ExecuteResolved(
                    client, function, normalized, args, ctx, NeoScriptExecutionOptions.ForDirectFunction(client),
                    ownsContext: true, typeArguments: table));
            }
            catch (Exception exception)
            {
                return Task.FromException<object?>(exception);
            }
        }

        /// <summary>Reads an extension property, including its receiver generic arguments.</summary>
        public static object? Get(NeoClient client, string memberId, object? receiver, TypeInfo[]? typeArguments = null)
        {
            NSPropertyMember property = ResolveProperty(client, memberId);
            var ctx = CreateContext(client, receiver);
            object? normalized = NormalizeReceiver(property, receiver, typeArguments, ctx, out TypeInfo[]? table);
            object? value = NSGetterEvaluator.DispatchNSGetterById(memberId, normalized, ctx, property.getter,
                typeArguments: table, receiverGeneric: property.extension!.genericParameterCount > 0);
            object? result = NSGetterEvaluator.ResolveHostCollection(value, ctx);
            client.DrainDeferredEffects();
            client.ReturnDirectFunctionContext(ctx, result);
            return result;
        }

        /// <summary>Writes an extension property through the ordinary property execution contract.</summary>
        public static NSSetterResult Set(NeoClient client, string memberId, object? receiver, object? value, TypeInfo[]? typeArguments = null)
        {
            NSPropertyMember property = ResolveProperty(client, memberId);
            FunctionWithReturnType setter = property.setter
                ?? throw new NSGetterRuntimeError($"Extension property '{property.name}' has no compiled setter.");
            TypeInfo returnType = property.returnTypeInfo
                ?? throw new NSGetterRuntimeError($"Extension property '{property.name}' has no return type.");
            var ctx = CreateContext(client, receiver);
            object? normalized = NormalizeReceiver(property, receiver, typeArguments, ctx, out TypeInfo[]? table);
            ctx.BindFunctionTypeArguments(table);
            if (table is not null)
                returnType = NeoFunctionTypeArguments.Bind(returnType, table);
            object? normalizedValue = NeoScriptValueMarshaller.Normalize(client, ctx.valueOwnership, value, returnType, ctx, "setter value");
            return NeoMemberNSProperty.ExecutePreparedSetter(client, property, setter, normalized, normalizedValue, ctx);
        }

        private static NSPropertyMember ResolveProperty(NeoClient client, string memberId)
        {
            if (!client.TryGetMember(memberId, out Member? member))
                throw new NSGetterRuntimeError($"Extension member '{memberId}' does not exist.");
            if (member is not NSPropertyMember property)
                throw new NSGetterRuntimeError($"Extension member '{memberId}' is not a property.");
            return property;
        }

        private static NSGetterEvaluator.Context CreateContext(NeoClient client, object? receiver)
        {
            NeoClient? attached = ClientOf(receiver);
            if (attached is not null && !ReferenceEquals(attached, client))
                throw new NSGetterRuntimeError("Extension receiver belongs to a different client.");
            NeoValueOwnership ownership = receiver switch
            {
                NeoGeneratedClassValue generated => generated.StorageOwnership,
                NeoMember node => node.ownership,
                INeoCollectionView collection => collection.CollectionNode?.ownership ?? NeoValueOwnership.Session,
                _ => NeoValueOwnership.Session,
            };
            return client.RentDirectFunctionContext(ownership);
        }

        private static object? NormalizeReceiver(Member member, object? receiver, TypeInfo[]? arguments,
            NSGetterEvaluator.Context ctx, out TypeInfo[]? table)
        {
            ExtensionMemberDescriptor extension = Descriptor(member)
                ?? throw new NSGetterRuntimeError($"Member '{member.name}' is not an extension.");
            table = NeoFunctionTypeArguments.ForCall(arguments, false, Parameters(member), member.name, null);
            if (member.Modifier == NeoMemberModifierKind.Static)
            {
                ValidateReceiver(member, receiver, table, ctx);
                return null;
            }
            TypeInfo type = table is null ? extension.receiverType : NeoFunctionTypeArguments.Bind(extension.receiverType, table);
            if (receiver is NeoGeneratedClassValue { PendingValue: { } pending })
                receiver = pending;
            object? normalized = NeoScriptValueMarshaller.Normalize(ctx.client, ctx.valueOwnership, receiver, type, ctx, "extension receiver");
            ValidateReceiver(member, normalized, table, ctx);
            return normalized;
        }

        internal static ExtensionMemberDescriptor? Descriptor(Member? member) => member switch
        {
            NSFunctionMember function => function.extension,
            NSPropertyMember property => property.extension,
            _ => null,
        };

        internal static FunctionGenericParam[]? Parameters(Member member) => member switch
        {
            NSFunctionMember function => function.genericParams,
            NSPropertyMember property => property.genericParams,
            _ => null,
        };

        internal static void ValidateReceiver(Member member, object? receiver, TypeInfo[]? table, NSGetterEvaluator.Context ctx)
        {
            if (Descriptor(member) is not { } extension)
                return;
            FunctionGenericParam[]? parameters = Parameters(member);
            if (extension.receiverType is null)
                throw new NSGetterRuntimeError($"Extension '{member.name}' has no receiver type.");
            if (extension.genericParameterCount < 0)
                throw new NSGetterRuntimeError($"Extension '{member.name}' has a negative receiver generic parameter count.");
            if (extension.genericParameterCount > (parameters?.Length ?? 0))
                throw new NSGetterRuntimeError($"Extension '{member.name}' has more receiver generic parameters than declared type parameters.");
            if ((table?.Length ?? 0) != (parameters?.Length ?? 0))
                throw new NSGetterRuntimeError($"Extension '{member.name}' has an invalid type argument count.");
            for (int index = 0; index < (parameters?.Length ?? 0); index++)
            {
                FunctionGenericParam parameter = parameters![index];
                TypeInfo argument = table![index];
                if (!argument.required)
                    throw new NSGetterRuntimeError($"Type argument '{parameter.name}' of extension '{member.name}' must be non-nullable.");
                if (argument.type == MemberKind.Generic)
                    throw new NSGetterRuntimeError($"Type argument '{parameter.name}' of extension '{member.name}' must be concrete.");
                if (argument.type == MemberKind.Unknown)
                    throw new NSGetterRuntimeError($"Type argument '{parameter.name}' of extension '{member.name}' is unknown.");
                if (parameter.constraint is { } constraint && !SatisfiesConstraint(argument, NeoFunctionTypeArguments.Bind(constraint, table), ctx.client))
                    throw new NSGetterRuntimeError($"Type argument '{parameter.name}' of extension '{member.name}' does not satisfy its constraint.");
            }
            if (member.Modifier == NeoMemberModifierKind.Static)
            {
                if (receiver is not null)
                    throw new NSGetterRuntimeError($"Static extension '{member.name}' must be invoked without an instance receiver.");
                return;
            }
            TypeInfo receiverType = table is null ? extension.receiverType : NeoFunctionTypeArguments.Bind(extension.receiverType, table);
            NeoScriptValueMarshaller.ValidateResolvedRuntimeValue(ctx.client, receiver, receiverType, ctx, $"receiver of extension '{member.name}'");
        }

        internal static bool SatisfiesConstraint(TypeInfo argument, TypeInfo constraint, NeoClient client)
        {
            if (constraint is EnumTypeInfo expectedEnum)
                return argument is EnumTypeInfo actualEnum && actualEnum.enumId == expectedEnum.enumId;
            if (constraint is ClassTypeInfo expectedClass)
            {
                if (argument is not ClassTypeInfo actualClass || !client.ClassChainContains(actualClass.classId, expectedClass.classId))
                    return false;
                if (expectedClass.typeArguments is not { Count: > 0 })
                    return true;
                var environment = NeoGenericResolution.ResolveEnv(client, actualClass.classId);
                foreach (var pair in expectedClass.typeArguments)
                {
                    TypeInfo? actual = null;
                    if (actualClass.typeArguments?.TryGetValue(pair.Key, out actual) != true && environment.TryGetValue(pair.Key, out var entry) && entry.IsBound)
                        actual = NeoGenericResolution.TypeInfoFromArgumentSignature(NeoGenericResolution.SignatureOfBinding(client,
                            new GenericBinding { kind = NeoGenericBindingKind.Member, memberId = entry.memberId }, environment));
                    if (actual is null || !NeoClient.TypeInfoMatches(pair.Value, actual))
                        return false;
                }
                return true;
            }
            if (constraint is not InterfaceTypeInfo expectedInterface)
                return false;
            TypeInfo? collectionElement = argument switch
            {
                CollectionTypeInfo collection => collection.entryTypeInfo,
                LookupTypeInfo lookup => lookup.entryTypeInfo,
                _ => null,
            };
            if (expectedInterface.interfaceId == NeoSequenceContract.Enumerable && collectionElement is not null)
                return expectedInterface.typeArguments is { Count: 1 }
                    && expectedInterface.typeArguments.TryGetValue(NeoSequenceContract.EnumerableElement, out TypeInfo expectedElement)
                    && NeoClient.TypeInfoMatches(collectionElement, expectedElement);
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, GenericBinding>> constructions;
            IReadOnlyDictionary<string, TypeInfo>? arguments;
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env = NeoGenericResolution.EmptyEnv;
            if (argument is ClassTypeInfo schemaClass)
            {
                constructions = client.InterfaceImplementers.Constructions(schemaClass.classId);
                arguments = schemaClass.typeArguments;
                env = NeoGenericResolution.ResolveEnv(client, schemaClass.classId);
            }
            else if (argument is InterfaceTypeInfo schemaInterface)
            {
                constructions = client.InterfaceImplementers.InterfaceClosure(schemaInterface.interfaceId);
                arguments = schemaInterface.typeArguments;
            }
            else
                return false;
            if (!constructions.TryGetValue(expectedInterface.interfaceId, out var construction))
                return false;
            if (expectedInterface.typeArguments is null)
                return true;
            foreach (var pair in expectedInterface.typeArguments)
            {
                if (!construction.TryGetValue(pair.Key, out var binding))
                    return false;
                TypeInfo actual = null!;
                if (binding.IsForward && arguments?.TryGetValue(binding.genericParamId!, out actual) == true)
                {
                    if (!NeoClient.TypeInfoMatches(pair.Value, actual))
                        return false;
                }
                else
                {
                    actual = NeoGenericResolution.TypeInfoFromArgumentSignature(NeoGenericResolution.SignatureOfBinding(client, binding, env));
                    if (!NeoClient.TypeInfoMatches(pair.Value, actual))
                        return false;
                }
            }
            return true;
        }

        internal static TypeInfo[]? BindProperty(NSPropertyMember member, TypeInfo[]? written, object? receiver, NSGetterEvaluator.Context ctx)
        {
            TypeInfo[]? table = NeoFunctionTypeArguments.ForCall(written, written is not null && NeoFunctionTypeArguments.Forwards(written), member.genericParams, member.name, ctx.functionTypeArguments);
            ValidateReceiver(member, receiver, table, ctx);
            return table;
        }

        internal static string? ReceiverClassId(Member member)
        {
            TypeInfo? receiver = Descriptor(member)?.receiverType;
            if (receiver is FunctionGenericTypeInfo generic)
            {
                FunctionGenericParam[]? parameters = Parameters(member);
                receiver = parameters is not null && (uint)generic.index < (uint)parameters.Length
                    ? parameters[generic.index].constraint : null;
            }
            return (receiver as ClassTypeInfo)?.classId;
        }
    }
}
