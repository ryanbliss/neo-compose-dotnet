// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript.Hashing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {
        private sealed class DelegateCaches
        {
            internal readonly Dictionary<string, (string Container, TypeInfo? Type)> Sites = new(StringComparer.Ordinal);
            internal Dictionary<string, string>? InterfaceOwners;
            internal readonly ConditionalWeakTable<object, RootDelegateCaches> Roots = new();
        }

        // Values can refer back to their root or receiver through lexical
        // captures. Weak keys let those cycles die with the owning scope.
        private sealed class RootDelegateCaches
        {
            internal readonly Dictionary<string, NeoDelegateValue> Closures = new(StringComparer.Ordinal);
            internal readonly ConditionalWeakTable<object, Dictionary<(object Literal, string Frame), NeoDelegateValue>> Literals = new();
            internal readonly Dictionary<(string Container, TypeBinding? Construction, TypeBinding Type, string Target), NeoDelegateValue> Groups = new();
        }

        private static readonly object NoDelegateReceiver = new();
        private static readonly ConditionalWeakTable<TypeBinding, IdentityText> delegateBindingKeys = new();
        private sealed class IdentityText
        {
            internal readonly string Text;
            internal IdentityText(string text) => Text = text;
        }
        private static string DelegateBindingKey(TypeBinding? binding) => binding is null ? ""
            : delegateBindingKeys.GetValue(binding, value => new IdentityText(NeoTypeSources.Key(NeoInterfaceDispatch.Identity(value)))).Text;
        private static string DelegateArgumentKey(TypeBinding[]? arguments) => arguments is null ? ""
            : new JArray(arguments.Select(DelegateBindingKey)).ToString(Formatting.None);
        private static string DelegateFrameKey(Context ctx) => DelegateArgumentKey(ctx.functionTypeArguments?.Select(RuntimeTypeOf).ToArray());
        private static RootDelegateCaches RootDelegates(Context ctx) =>
            ComparerCache(ctx).Delegates.Roots.GetValue(ctx.rootValue ?? NoDelegateReceiver, _ => new RootDelegateCaches());

        private static (string Container, TypeInfo? Type) DelegateSite(string owner, Context ctx)
        {
            var cache = ComparerCache(ctx).Delegates;
            if (cache.Sites.TryGetValue(owner, out var site))
                return site;
            string? classId = FindSchemaPlacementCached(owner, ctx)?.ownerClass.id;
            if (classId is null && ctx.client.TryGetConstructor(owner, out var constructor))
                classId = constructor.classId;
            TypeInfo? type = null;
            string container = owner;
            if (classId is not null && ctx.client.TryGetClass(classId, out var declaration))
            {
                container = classId;
                if (declaration.genericParams is { Count: > 0 })
                    type = new ClassTypeInfo
                    {
                        type = MemberKind.Class,
                        required = true,
                        classId = classId,
                        typeArguments = declaration.genericParams.ToDictionary(p => p.id, p => (TypeInfo)new GenericTypeInfo
                        {
                            type = MemberKind.Generic,
                            required = true,
                            ownerClassId = classId,
                            genericParamId = p.id
                        })
                    };
            }
            else
            {
                if (cache.InterfaceOwners is null)
                {
                    cache.InterfaceOwners = new(StringComparer.Ordinal);
                    foreach (var iface in ctx.client.ProjectDataForRuntime.interfaces.Values)
                        foreach (var member in iface.members.Values)
                            if (member.implementationMemberId is { } implementation)
                                cache.InterfaceOwners[implementation] = iface.id;
                }
                if (cache.InterfaceOwners.TryGetValue(owner, out string? interfaceId))
                {
                    container = interfaceId;
                    var iface = ctx.client.ProjectDataForRuntime.interfaces[interfaceId];
                    if (iface.genericParams is { Count: > 0 })
                        type = new InterfaceTypeInfo
                        {
                            type = MemberKind.Interface,
                            required = true,
                            interfaceId = interfaceId,
                            typeArguments = iface.genericParams.ToDictionary(p => p.id, p => (TypeInfo)new GenericTypeInfo
                            {
                                type = MemberKind.Generic,
                                required = true,
                                ownerInterfaceId = interfaceId,
                                genericParamId = p.id
                            })
                        };
                }
            }
            return cache.Sites[owner] = (container, type);
        }

        private static string ClosureOwner(string site) => site.Substring(0, site.LastIndexOf(':', site.LastIndexOf(':') - 1));
        private static TypeBinding? DelegateSiteConstruction(string owner, Context ctx, object? receiver = null)
        {
            var site = DelegateSite(owner, ctx);
            if (site.Type is null)
                return null;
            if (receiver is not null && site.Type is ClassTypeInfo)
                return NeoInterfaceDispatch.Of(ctx.client).ClassChain(RuntimeTypeOfValue(receiver, null, ctx))
                    .FirstOrDefault(type => type.Head!.ClassId == site.Container);
            TypeInfo closed = CloseContextType(site.Type, ctx);
            return NeoNSFunctionRuntime.ContainsGeneric(closed) ? null : RuntimeTypeOf(closed);
        }

        private static string? CachedClosureKey(object? value)
        {
            if (value is NeoBox box)
                value = box.Payload;
            if (value is not NeoDelegateValue { IsClosure: true, site: { } site, placement: null } closure)
                return null;
            return closure.cachedClosureIdentity ??= new JArray(site, closure.code ?? "", DelegateArgumentKey(closure.typeArguments),
                DelegateBindingKey(closure.declaringConstruction)).ToString(Formatting.None);
        }

        private static NeoDelegateValue CacheClosure(NeoDelegateValue value, Context ctx)
        {
            if (CachedClosureKey(value) is not { } key)
                return value;
            var cache = RootDelegates(ctx).Closures;
            if (cache.TryGetValue(key, out var stored))
                return stored;
            cache.Add(key, value);
            return value;
        }

        private static NeoDelegateValue CacheLiteralClosure(object literal, NeoDelegateValue value, Context ctx)
        {
            if (!value.IsClosure)
                return value;
            string frame = DelegateFrameKey(ctx) + "|" + DelegateBindingKey(value.declaringConstruction);
            var key = (literal, frame);
            var cache = RootDelegates(ctx).Literals.GetValue(ctx.thisValue ?? NoDelegateReceiver, _ => new());
            if (cache.TryGetValue(key, out var stored))
                return stored;
            return cache[key] = CacheClosure(value, ctx);
        }

        private static NeoDelegateValue CacheMethodGroup(NeoDelegateValue value, NeoScriptMethodGroupCache? cache, Context ctx)
        {
            if (cache is null)
                return value;
            if (value.IsClosure || value.valueId is not null)
                throw new NeoScriptPreExecutionValidationError("A cached method group must be a static member or built-in target.");
            string container = cache.method == true ? cache.owner + "<" + DelegateFrameKey(ctx) + ">" : DelegateSite(cache.owner, ctx).Container;
            TypeBinding type = RuntimeDelegateType(cache.delegateType, ctx);
            value.delegateType ??= type;
            string target = value.builtIn is { } builtIn
                ? new JArray(DelegateBindingKey(builtIn.DeclaringType), builtIn.DeclaringClass, builtIn.Name, builtIn.Arity,
                    DelegateArgumentKey(builtIn.ParameterTypes), DelegateBindingKey(builtIn.ExplicitInterface), DelegateArgumentKey(builtIn.TypeArguments)).ToString(Formatting.None)
                : new JArray(value.memberId, DelegateBindingKey(value.staticConstruction), DelegateBindingKey(value.interfaceConstruction),
                    DelegateArgumentKey(value.typeArguments)).ToString(Formatting.None);
            var key = (container, DelegateSiteConstruction(cache.owner, ctx), type, target);
            var groups = RootDelegates(ctx).Groups;
            if (groups.TryGetValue(key, out var stored))
                return stored;
            return groups[key] = value;
        }
    }
}
