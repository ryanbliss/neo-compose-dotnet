#nullable enable
using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {
        internal static object ErrorValue(NeoScriptErrorPayload payload, Context ctx)
        {
            if (payload.Value is NeoErrorBase view)
            {
                if (!ReferenceEquals(view.Client, ctx.client))
                    throw new NeoScriptPreExecutionValidationError("Structured error belongs to another NeoClient.");
                return view.PendingValue ?? ResolveValueIfId(view.valueId, ctx)!;
            }
            if (payload.Value is { } value)
                return value;
            if (!ctx.client.TryGetClass(payload.ClassId, out NeoSchemaClass? schemaClass))
                throw new NeoScriptPreExecutionValidationError($"Runtime error class '{payload.ClassId}' is missing. Synchronize the project schema.");
            var fields = Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>();
            var resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(ctx.client,
                new ClassTypeInfo { type = MemberKind.Class, required = true, classId = payload.ClassId },
                schemaClass!.constructorIds[0], new[] { "message" }, fields);
            return payload.Value = ConstructDeclared(resolved, new object?[] { payload.Message }, fields, ctx, null, false)!;
        }

        internal static NeoErrorBase ErrorView(NeoScriptErrorPayload payload, Context ctx)
        {
            if (payload.Value is NeoErrorBase view)
                return view;
            object value = ErrorValue(payload, ctx);
            NeoGeneratedTypesSupport.InterfaceResolver? factory = ctx.client.FindGeneratedErrorFactory(payload.ClassId,
                type => RuntimeTypeCheck(value, type, ctx));
            NeoGeneratedClassValue? generated = value is NeoScriptObject detached
                ? ctx.client.ResolveDetachedGeneratedClassValue(detached)
                : ctx.client.ResolveRegisteredGeneratedClassValue(FindRowReference(value, ctx)?.valueId!, interfaceResolver: factory);
            if (generated is NeoErrorBase result)
                return result;
            ctx.client.TryGetClass(payload.ClassId, out NeoSchemaClass? schemaClass);
            var env = NeoNSFunctionRuntime.ResolveReceiverGenericEnv(ctx.client, value, ctx, "An error wrapper");
            var arguments = new Dictionary<string, TypeInfo>();
            foreach (var pair in env)
                if (pair.Value.IsBound)
                    arguments[pair.Key] = NeoGenericResolution.TypeInfoFromArgumentSignature(NeoGenericResolution.SignatureOfBinding(ctx.client,
                        new GenericBinding { kind = NeoGenericBindingKind.Member, memberId = pair.Value.memberId }, env));
            throw new InvalidOperationException($"No generated error wrapper for '{schemaClass?.name ?? payload.ClassId}' with closed arguments {Newtonsoft.Json.JsonConvert.SerializeObject(arguments)}. Call {schemaClass?.name ?? payload.ClassId}<...>.RegisterErrorFactory(client) for this construction before reading NeoScriptException.Error.");
        }

        internal static NSGetterRuntimeError AuthoredError(object? value, Context ctx)
        {
            if (value is string message)
                return new NSGetterRuntimeError(message, NeoErrorContract.NeoError);
            if (!RuntimeTypeCheck(value, new ClassTypeInfo { type = MemberKind.Class, required = true, classId = NeoErrorContract.NeoError }, ctx))
                throw new NeoScriptPreExecutionValidationError("Throw requires a non-null string or NeoError value.");
            string text = DispatchSchemaMember(value, "Message", ctx) as string
                ?? throw new NeoScriptPreExecutionValidationError("NeoError.Message must be a string.");
            var failure = new NSGetterRuntimeError(text, FindRowClassIdByReference(value, ctx)!);
            failure.Payload.Value = value;
            return failure;
        }

        internal static TypeInfo ClosedCatchType(TypeInfo type, Context ctx)
        {
            if (ctx.functionTypeArguments is { } arguments)
                type = NeoFunctionTypeArguments.Bind(type, arguments);
            if (NamesClassTypeParameter(type) && ctx.thisValue is { } receiver)
                type = NeoGenericResolution.SubstituteTypeInfo(ctx.client, type,
                    NeoNSFunctionRuntime.ResolveReceiverGenericEnv(ctx.client, receiver, ctx, "A catch type"));
            return type;
        }

        internal static bool MatchesError(NSGetterRuntimeError error, TypeInfo type, Context ctx) =>
            ClosedCatchType(type, ctx).type == MemberKind.String || RuntimeTypeCheck(ErrorValue(error.Payload, ctx), ClosedCatchType(type, ctx), ctx);

        internal static void ValidateCatchTypes(TryInstruction instruction, Context ctx)
        {
            var covered = new List<TypeInfo>();
            foreach (CatchClause clause in instruction.catches)
            {
                TypeInfo type = ClosedCatchType(clause.binding.typeInfo, ctx);
                if (type.type != MemberKind.String && (type is not ClassTypeInfo errorType ||
                    !ErrorClassExtends(errorType.classId, NeoErrorContract.NeoError, ctx)))
                    throw new NeoScriptPreExecutionValidationError("Catch type must derive from NeoError.");
                foreach (TypeInfo earlier in covered)
                    if (CatchTypeCovers(earlier, type, ctx))
                        throw new NeoScriptPreExecutionValidationError("An earlier unfiltered catch covers this type.");
                if (clause.filter is null)
                    covered.Add(type);
            }
        }

        private static bool ErrorClassExtends(string actual, string expected, Context ctx)
        {
            var seen = new HashSet<string>();
            while (seen.Add(actual))
            {
                if (actual == expected)
                    return true;
                if (!ctx.client.TryGetClass(actual, out NeoSchemaClass? schema) || schema!.extendsClassId is not { } parent)
                    return false;
                actual = parent;
            }
            return false;
        }

        private static bool CatchTypeCovers(TypeInfo earlier, TypeInfo later, Context ctx)
        {
            if (earlier.type == MemberKind.String)
                return true;
            var expected = (ClassTypeInfo)earlier;
            var candidate = later as ClassTypeInfo ?? new ClassTypeInfo { type = MemberKind.Class, required = true, classId = NeoErrorContract.NeoError };
            if (!ErrorClassExtends(candidate.classId, expected.classId, ctx))
                return false;
            if (expected.typeArguments is not { Count: > 0 })
                return true;
            var env = NeoGenericResolution.ResolveInstanceEnv(ctx.client, candidate.classId, null);
            foreach (var pair in expected.typeArguments)
            {
                if (!env.TryGetValue(pair.Key, out NeoGenericEnvEntry entry))
                    return false;
                TypeInfo? actual;
                if (!entry.IsBound)
                {
                    if (candidate.typeArguments is null || !candidate.typeArguments.TryGetValue(entry.unboundParamId!, out actual))
                        return false;
                }
                else
                    actual = NeoGenericResolution.TypeInfoFromArgumentSignature(NeoGenericResolution.SignatureOfBinding(ctx.client,
                    new GenericBinding { kind = NeoGenericBindingKind.Member, memberId = entry.memberId }, env));
                if (!NeoClient.TypeInfoMatches(pair.Value, actual))
                    return false;
            }
            return true;
        }

        private static NeoScriptException HostError(Exception error, Context ctx)
        {
            UnityEngine.Debug.LogException(error);
            return new NeoScriptException(new NSGetterRuntimeError(NeoErrorContract.HostMessage, NeoErrorContract.NeoHostError, error), ctx);
        }

        internal static Exception NormalizeError(Exception error, Context ctx)
        {
            if (error is NeoFunctionCallSuspended || error is OperationCanceledException)
                return error;
            if (error is NSGetterRuntimeError runtime)
            {
                if (!NeoScriptErrorClassification.IsAuthoredCatchable(runtime))
                    return runtime;
                if (runtime is NeoScriptException structured)
                {
                    if (!ReferenceEquals(structured.OriginClient, ctx.client))
                        return HostError(new InvalidOperationException("Native structured error belongs to another NeoClient."), ctx);
                    if (structured.Payload.Value is NeoErrorBase view)
                    {
                        if (!ReferenceEquals(view.Client, ctx.client) ||
                            !ErrorClassExtends(view.classId!, NeoErrorContract.NeoError, ctx))
                            return HostError(new InvalidOperationException("Native structured error has an invalid client or class."), ctx);
                        _ = ErrorValue(structured.Payload, ctx);
                    }
                    return structured;
                }
                if (!NeoScriptErrorClassification.IsRewrappable(runtime))
                    return runtime;
                return new NeoScriptException(runtime, ctx);
            }
            return HostError(error, ctx);
        }
    }
}
