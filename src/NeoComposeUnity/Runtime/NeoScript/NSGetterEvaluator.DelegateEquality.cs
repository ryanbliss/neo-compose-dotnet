// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Linq;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript.Hashing;

namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {
        // Older closure IR used Null for a no-result callback. Its runtime
        // signature is the same as Void; Null remains invalid as a value type.
        private static TypeBinding RuntimeDelegateType(TypeInfo type, Context ctx)
        {
            type = CloseContextType(type, ctx);
            if (type is DelegateTypeInfo { returnTypeInfo: { type: MemberKind.Null } } legacy)
                type = new DelegateTypeInfo
                {
                    type = MemberKind.NSDelegate,
                    required = legacy.required,
                    argumentTypes = legacy.argumentTypes,
                    returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                };
            return RuntimeTypeOf(type);
        }

        private static NeoDelegateValue? ReadDelegate(DelegateMemberValue row, Member? member, NeoValueOwnership ownership, Context ctx)
        {
            var value = row.value;
            if (value is null)
                return null;
            if (value.IsClosure && value.site is not null && value.declaringConstruction is null)
            {
                object? receiver = null;
                string ownerId = row.id;
                var ownerLayer = ownership;
                var visited = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                while (visited.Add(ownerId) && ctx.client.TryFindOwnedParent(ownerLayer, ownerId, out string? parentId, out ownerLayer))
                {
                    ownerId = parentId!;
                    if (ctx.client.TryGetValue(ownerLayer, ownerId, out ObjectMemberValue? owner) && owner.classId is not null)
                    {
                        receiver = UnwrapRow(owner, ctx, ownerLayer);
                        break;
                    }
                }
                value.declaringConstruction = DelegateSiteConstruction(ClosureOwner(value.site), ctx, receiver);
            }
            if (value.delegateType is not null || member is not DelegateMember declaration)
                return value.IsClosure ? CacheClosure(value, ctx) : value;
            if (ctx.client.TryInferMemberForValueId(row.id, out var inferred, ownership) && inferred is DelegateMember placed)
                declaration = placed;
            TypeInfo type = new DelegateTypeInfo
            {
                type = MemberKind.NSDelegate,
                required = declaration.Requirement == NeoMemberRequirementKind.Required,
                returnTypeInfo = declaration.returnTypeInfo,
                argumentTypes = declaration.argumentTypes,
            };
            if (NeoNSFunctionRuntime.ContainsGeneric(type))
            {
                var environment = declaration.ownerEnvironment;
                string ownerId = row.id;
                var visited = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                while (environment is null && visited.Add(ownerId)
                    && ctx.client.TryFindOwnedParent(ownership, ownerId, out string? parentId, out ownership))
                {
                    ownerId = parentId!;
                    if (ctx.client.TryGetValue(ownership, ownerId, out ObjectMemberValue? owner) && owner.classId is not null)
                        environment = NeoNSFunctionRuntime.ResolveReceiverGenericEnv(ctx.client, UnwrapRow(owner, ctx, ownership)!, ctx, "A stored delegate");
                }
                if (environment is not null)
                    type = NeoGenericResolution.SubstituteTypeInfo(ctx.client, type, environment);
            }
            value.delegateType = RuntimeDelegateType(type, ctx);
            return value.IsClosure ? CacheClosure(value, ctx) : value;
        }

        private sealed class DelegateImplementation
        {
            internal readonly object Schema;
            internal readonly string Member;
            internal readonly TypeBinding? Construction;
            internal DelegateImplementation(object schema, string member, TypeBinding? construction)
            {
                Schema = schema;
                Member = member;
                Construction = construction;
            }
        }

        private static DelegateImplementation DelegateImplementationOf(NeoDelegateValue value, Context ctx)
        {
            if (value.identityResolution is DelegateImplementation cached && ReferenceEquals(cached.Schema, ctx.client.SchemaResolution))
                return cached;
            string member = value.memberId!;
            TypeBinding? construction = value.interfaceConstruction ?? value.staticConstruction;
            if (value.valueId is { } id)
            {
                object? receiver = ResolveValueIfId(id, ctx);
                var pointer = new CallFunctionPointer
                {
                    memberId = member,
                    receiver = new CallReceiver { kind = CallReceiverKind.Instance, pointer = Constant(receiver) },
                };
                member = ResolveCallTarget(pointer, receiver, ctx)?.memberId ?? member;
            }
            var result = new DelegateImplementation(ctx.client.SchemaResolution, member, construction);
            value.identityResolution = result;
            return result;
        }

        private static object? DelegateIdentityTarget(NeoDelegateValue value, Context ctx)
        {
            if (!value.identityTargetBound)
            {
                value.identityTarget = value.placement switch
                {
                    "environment" => new object(),
                    "this" => value.hasLexicalEnvironment ? value.lexicalThis : PersistedClosureLexicalThis(value, value.action!, ctx),
                    _ => null,
                };
                value.identityTargetBound = true;
            }
            return value.identityTarget;
        }

        internal static bool DelegateEquals(NeoDelegateValue left, NeoDelegateValue right, Context ctx)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (!Equals(left.builtIn?.DelegateType ?? left.delegateType, right.builtIn?.DelegateType ?? right.delegateType))
                return false;
            if (left.IsClosure != right.IsClosure || left.IsBuiltInTarget != right.IsBuiltInTarget)
                return false;
            if (left.IsClosure)
            {
                if (left.site is null || right.site is null || left.code is null || right.code is null)
                    return false;
                return left.site == right.site && left.code == right.code
                    && Equals(left.declaringConstruction, right.declaringConstruction)
                    && (left.typeArguments ?? Array.Empty<TypeBinding>()).SequenceEqual(right.typeArguments ?? Array.Empty<TypeBinding>())
                    && SameObject(DelegateIdentityTarget(left, ctx), DelegateIdentityTarget(right, ctx), ctx);
            }
            if (left.builtIn is { } builtIn)
                return builtIn.Equals(right.builtIn);
            if (left.valueId != right.valueId)
                return false;
            var a = DelegateImplementationOf(left, ctx);
            var b = DelegateImplementationOf(right, ctx);
            return a.Member == b.Member && Equals(a.Construction, b.Construction)
                && (left.typeArguments ?? Array.Empty<TypeBinding>()).SequenceEqual(right.typeArguments ?? Array.Empty<TypeBinding>());
        }

        private static int CombineDelegateHash(int hash, int value) => unchecked(hash * 31 + value);
        private static int BindingHash(TypeBinding? binding) => binding is null ? 0 : NeoHashes.String(NeoTypeSources.Key(binding));
        private static int BindingsHash(TypeBinding[]? bindings)
        {
            int hash = 17;
            foreach (var binding in bindings ?? Array.Empty<TypeBinding>())
                hash = CombineDelegateHash(hash, BindingHash(binding));
            return hash;
        }

        internal static int DelegateHash(NeoDelegateValue value, Context ctx)
        {
            int hash = 17;
            if (value.IsClosure)
            {
                if (value.site is null || value.code is null)
                    return IdentityHash(value, ctx);
                hash = CombineDelegateHash(hash, NeoHashes.String(value.site));
                hash = CombineDelegateHash(hash, BindingHash(value.declaringConstruction));
                hash = CombineDelegateHash(hash, BindingsHash(value.typeArguments));
                hash = CombineDelegateHash(hash, IdentityHash(DelegateIdentityTarget(value, ctx), ctx));
            }
            else if (value.builtIn is { } builtIn)
            {
                hash = CombineDelegateHash(hash, BindingHash(builtIn.DeclaringType));
                hash = CombineDelegateHash(hash, NeoHashes.String(builtIn.DeclaringClass ?? ""));
                hash = CombineDelegateHash(hash, NeoHashes.String(builtIn.Name));
                hash = CombineDelegateHash(hash, builtIn.Arity);
                hash = CombineDelegateHash(hash, BindingsHash(builtIn.ParameterTypes));
                hash = CombineDelegateHash(hash, BindingsHash(builtIn.TypeArguments));
                hash = CombineDelegateHash(hash, BindingHash(builtIn.ExplicitInterface));
            }
            else
            {
                var implementation = DelegateImplementationOf(value, ctx);
                hash = CombineDelegateHash(hash, NeoHashes.String(implementation.Member));
                hash = CombineDelegateHash(hash, BindingHash(implementation.Construction));
                hash = CombineDelegateHash(hash, BindingsHash(value.typeArguments));
                hash = CombineDelegateHash(hash, value.valueId is null ? 0 : NeoHashes.String(value.valueId));
            }
            return CombineDelegateHash(hash, BindingHash(value.builtIn?.DelegateType ?? value.delegateType));
        }
    }
}
