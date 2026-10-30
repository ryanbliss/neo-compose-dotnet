// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    /// <summary>Closed interface graphs and accessor mappings, scoped to the client's schema.</summary>
    internal sealed class NeoInterfaceDispatch
    {
        private static readonly ConditionalWeakTable<NeoClient, NeoInterfaceDispatch> Caches = new();
        private readonly NeoClient client;
        private readonly object schema;
        private readonly Dictionary<TypeBinding, List<TypeBinding>> closures = new();
        private readonly Dictionary<TypeBinding, IReadOnlyList<TypeBinding>> annotatedClosures = new();
        private readonly Dictionary<TypeBinding, List<TypeBinding>> classes = new();
        private readonly Dictionary<TypeBinding, IReadOnlyDictionary<string, NeoGenericEnvEntry>> environments = new();
        private readonly Dictionary<(string, string, string), Member> accessors = new();
        private readonly Dictionary<(TypeBinding, TypeBinding, string, string, bool), Implementation> mappings = new();
        private readonly Dictionary<(string, string), (string Name, InterfaceMember Member)> declarations = new();
        private readonly HashSet<string> indexedInterfaces = new();
        private readonly Dictionary<string, Dictionary<string, List<Member>>> explicitByClass = new();
        private readonly Dictionary<string, Dictionary<string, List<Member>>> ownFunctionsByName = new();
        private readonly Dictionary<string, string> classSchemaKeys = new();
        private readonly Dictionary<(TypeBinding, TypeBinding, string, string), Implementation?> classMappings = new();

        internal sealed class Implementation
        {
            internal readonly string MemberId;
            internal readonly TypeBinding? InterfaceConstruction;
            internal readonly TypeBinding? StaticConstruction;
            internal NSGetterEvaluator.CallSiteTarget? CallTarget;
            internal Implementation(string memberId, TypeBinding? interfaceConstruction = null, TypeBinding? staticConstruction = null)
            {
                MemberId = memberId;
                InterfaceConstruction = interfaceConstruction;
                StaticConstruction = staticConstruction;
            }
        }

        private NeoInterfaceDispatch(NeoClient client)
        {
            this.client = client;
            schema = client.SchemaResolution;
        }

        internal static NeoInterfaceDispatch Of(NeoClient client)
        {
            var cache = Caches.GetValue(client, owner => new NeoInterfaceDispatch(owner));
            if (!ReferenceEquals(cache.schema, client.SchemaResolution))
            {
                Caches.Remove(client);
                cache = Caches.GetValue(client, owner => new NeoInterfaceDispatch(owner));
            }
            return cache;
        }

        internal static TypeBinding Identity(TypeBinding binding)
        {
            if (binding.Kind != TypeBindingKind.Closed)
                throw new NSGetterRuntimeError("An interface dispatch construction must be closed.");
            var head = binding.Head!.WithoutNames();
            if (!TypeBindings.IsValueType(head))
                head = head.WithRequired(true);
            return TypeBinding.Closed(head, binding.Bindings?.ToDictionary(pair => pair.Key, pair => Identity(pair.Value)));
        }

        internal static TypeBinding Substitute(TypeBinding binding, TypeBinding owner)
        {
            if (binding.IsOpen)
            {
                if (owner.Bindings?.TryGetValue(binding.Param!, out var argument) == true)
                    return argument;
                throw new NSGetterRuntimeError($"Type parameter '{binding.Param}' has no binding in '{NeoTypeNames.Of(owner)}'.");
            }
            return TypeBinding.Closed(binding.Head!, binding.Bindings?.ToDictionary(pair => pair.Key, pair => Substitute(pair.Value, owner)));
        }

        private static TypeBinding Construct(InterfaceConstruction declared, TypeBinding owner) =>
            Identity(TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: declared.interfaceId),
                declared.arguments?.ToDictionary(pair => pair.Key, pair => Substitute(pair.Value, owner))));

        internal IReadOnlyList<TypeBinding> ClassChain(TypeBinding type)
        {
            type = Identity(type);
            if (classes.TryGetValue(type, out var found))
                return found;
            var chain = new List<TypeBinding>();
            var seen = new HashSet<string>();
            TypeBinding current = type;
            while (current.Head?.ClassId is string id && seen.Add(id))
            {
                chain.Add(current);
                var declaration = client.ProjectDataForRuntime.classes[id];
                if (declaration.extendsClassId is not string parent)
                    break;
                current = Identity(TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: parent),
                    declaration.extendsGenericBindings?.ToDictionary(pair => pair.Key, pair => Substitute(pair.Value, current))));
            }
            classes[type] = chain;
            return chain;
        }

        internal IReadOnlyList<TypeBinding> Closure(TypeBinding type)
        {
            type = Identity(type);
            if (closures.TryGetValue(type, out var found))
                return found;
            var result = new List<TypeBinding>();
            var seen = new HashSet<TypeBinding>();
            var active = new HashSet<string>();
            void Visit(TypeBinding construction)
            {
                string id = construction.Head!.InterfaceId!;
                if (!active.Add(id))
                    throw new NSGetterRuntimeError($"Circular interface extension detected at '{id}'.");
                if (seen.Add(construction))
                {
                    result.Add(construction);
                    foreach (var parent in client.ProjectDataForRuntime.interfaces[id].extendsInterfaces ?? new List<InterfaceConstruction>())
                        Visit(Construct(parent, construction));
                }
                active.Remove(id);
            }
            if (type.Head!.Type == MemberKind.Interface)
                Visit(type);
            else
                foreach (var owner in ClassChain(type))
                    foreach (var declared in client.ProjectDataForRuntime.classes[owner.Head!.ClassId!].implementsInterfaces ?? new List<InterfaceConstruction>())
                        Visit(Construct(declared, owner));
            closures[type] = result;
            return result;
        }

        // Stored-slot validation retains nullable annotations; dispatch identities erase them.
        internal IReadOnlyList<TypeBinding> AnnotatedClosure(TypeBinding type)
        {
            if (annotatedClosures.TryGetValue(type, out var cached))
                return cached;
            var result = new List<TypeBinding>();
            var seen = new HashSet<TypeBinding>();
            var active = new HashSet<string>();
            TypeBinding Bind(InterfaceConstruction declaration, TypeBinding owner) =>
                TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: declaration.interfaceId),
                    declaration.arguments?.ToDictionary(pair => pair.Key, pair => Substitute(pair.Value, owner)));
            void Visit(TypeBinding current)
            {
                string id = current.Head!.ClassId ?? current.Head.InterfaceId!;
                if (!active.Add(id))
                    throw new NSGetterRuntimeError($"Circular type inheritance at '{id}'.");
                if (seen.Add(current))
                {
                    if (current.Head.Type == MemberKind.Interface)
                    {
                        result.Add(current);
                        foreach (var parent in client.ProjectDataForRuntime.interfaces[id].extendsInterfaces ?? new())
                            Visit(Bind(parent, current));
                    }
                    else
                    {
                        var owner = client.ProjectDataForRuntime.classes[id];
                        foreach (var declared in owner.implementsInterfaces ?? new())
                            Visit(Bind(declared, current));
                        if (owner.extendsClassId is string parent)
                            Visit(TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: parent),
                                owner.extendsGenericBindings?.ToDictionary(pair => pair.Key, pair => Substitute(pair.Value, current))));
                    }
                }
                active.Remove(id);
            }
            Visit(type);
            return annotatedClosures[type] = result;
        }

        private IEnumerable<TypeBinding> Listed(TypeBinding owner)
        {
            var seen = new HashSet<TypeBinding>();
            foreach (var declared in client.ProjectDataForRuntime.classes[owner.Head!.ClassId!].implementsInterfaces ?? new List<InterfaceConstruction>())
                foreach (var entry in Closure(Construct(declared, owner)))
                    if (seen.Add(entry))
                        yield return entry;
        }

        internal IReadOnlyDictionary<string, NeoGenericEnvEntry> Environment(TypeBinding construction)
        {
            if (environments.TryGetValue(construction, out var cached))
                return cached;
            var result = new Dictionary<string, NeoGenericEnvEntry>();
            var owners = construction.Head!.Type == MemberKind.Class ? ClassChain(construction) : Closure(construction);
            foreach (var owner in owners)
                foreach (var pair in owner.Bindings ?? new Dictionary<string, TypeBinding>())
                    result.TryAdd(pair.Key, NeoTypeBindings.Entry(pair.Value));
            return environments[construction] = result;
        }

        internal bool Converts(TypeBinding from, TypeBinding to)
        {
            from = Identity(from);
            to = Identity(to);
            if (from.Equals(to))
                return true;
            if (TypeBindings.IsValueType(from.Head!) || TypeBindings.IsValueType(to.Head!))
                return false;
            if (from.Head!.Type == MemberKind.NSDelegate && to.Head!.Type == MemberKind.NSDelegate)
            {
                if ((from.Bindings?.Count ?? 0) != (to.Bindings?.Count ?? 0))
                    return false;
                foreach (var argument in from.Bindings!)
                {
                    if (!to.Bindings!.TryGetValue(argument.Key, out var target))
                        return false;
                    if (argument.Value.Equals(target))
                        continue;
                    if (!(argument.Key == "0" ? Converts(argument.Value, target) : Converts(target, argument.Value)))
                        return false;
                }
                return true;
            }
            if (to.Head!.Type == MemberKind.Object && to.Head.ObjectType is null)
                return true;
            if (to.Head.Type == MemberKind.Class && from.Head!.Type == MemberKind.Class)
                return ClassChain(from).Any(entry => entry.Equals(to));
            if (to.Head.Type != MemberKind.Interface || from.Head!.Type is not (MemberKind.Class or MemberKind.Interface))
                return false;
            foreach (var actual in Closure(from))
            {
                if (actual.Head!.InterfaceId != to.Head.InterfaceId)
                    continue;
                bool matches = true;
                foreach (var parameter in client.ProjectDataForRuntime.interfaces[to.Head.InterfaceId!].genericParams ?? new List<GenericParamDeclaration>())
                {
                    if (actual.Bindings?.TryGetValue(parameter.id, out var source) != true
                        || to.Bindings?.TryGetValue(parameter.id, out var target) != true)
                    {
                        matches = false;
                        break;
                    }
                    if (source!.Equals(target))
                        continue;
                    matches = parameter.variance switch
                    {
                        NeoGenericParamVarianceKind.Out => Converts(source, target!),
                        NeoGenericParamVarianceKind.In => Converts(target!, source),
                        _ => false,
                    };
                    if (!matches)
                        break;
                }
                if (matches)
                    return true;
            }
            return false;
        }

        internal (string Name, InterfaceMember Member) Declaration(TypeBinding construction, string memberId)
        {
            var key = (construction.Head!.InterfaceId!, memberId);
            if (declarations.TryGetValue(key, out var cached))
                return cached;
            if (indexedInterfaces.Add(key.Item1))
                foreach (var entry in client.ProjectDataForRuntime.interfaces[key.Item1].members)
                    declarations[(key.Item1, entry.Value.id ?? entry.Key)] = (NeoCallableSignature.Name(entry.Key), entry.Value);
            if (declarations.TryGetValue(key, out cached))
                return cached;
            throw new NSGetterRuntimeError($"Interface member '{memberId}' was not found.");
        }

        internal Implementation Resolve(TypeBinding receiver, TypeBinding requested, string memberId, string accessor, bool direct = false)
        {
            receiver = Identity(receiver);
            requested = Identity(requested);
            var key = (receiver, requested, memberId, accessor, direct);
            if (mappings.TryGetValue(key, out var cached))
                return cached;
            var declaration = Declaration(requested, memberId);
            var contract = declaration.Member;
            if (direct || contract.Access == NeoMemberAccessKind.Private || contract.DeclaredModifier == NeoMemberModifierKind.Sealed
                || accessor == "get" && contract.GetterAccess == NeoMemberAccessKind.Private
                || accessor == "set" && contract.SetterAccess == NeoMemberAccessKind.Private)
                return mappings[key] = Body(contract, requested);
            if (receiver.Head!.Type == MemberKind.Class)
            {
                foreach (var owner in ClassChain(receiver))
                {
                    var exact = Explicit(owner, requested, memberId, accessor, contract.IsStatic);
                    if (exact is not null)
                        return mappings[key] = DispatchVirtual(receiver, exact, accessor);
                    var listed = Listed(owner).ToList();
                    foreach (var candidate in listed.Where(item => item.Equals(requested)).Concat(listed.Where(item => !item.Equals(requested) && item.Head!.InterfaceId == requested.Head!.InterfaceId && Converts(item, requested))))
                    {
                        var mapped = MapClass(owner, candidate, memberId, declaration.Name, contract, accessor);
                        if (mapped is null)
                            continue;
                        var chain = ClassChain(owner);
                        var inherited = chain.Count > 1 && Closure(chain[1]).Contains(candidate)
                            ? MapClass(chain[1], candidate, memberId, declaration.Name, contract, accessor) : null;
                        if (inherited?.MemberId != mapped.MemberId)
                            return mappings[key] = DispatchVirtual(receiver, mapped, accessor);
                    }
                }
            }
            var closure = Closure(receiver);
            TypeBinding? implemented = closure.FirstOrDefault(item => item.Equals(requested))
                ?? closure.FirstOrDefault(item => item.Head!.InterfaceId == requested.Head!.InterfaceId && Converts(item, requested));
            if (implemented is null)
                throw Missing(receiver, requested, declaration.Name);
            var candidates = new List<(TypeBinding Owner, InterfaceMember Member)> { (implemented, contract) };
            foreach (var owner in closure)
                foreach (var entry in client.ProjectDataForRuntime.interfaces[owner.Head!.InterfaceId!].members.Values)
                    if (entry.explicitImplementation is { } implementation
                        && implementation.interfaceMemberId == memberId && Construct(implementation, owner).Equals(implemented)
                        && (entry.kind != NeoInterfaceMemberKind.Property || InterfaceHasAccessor(entry, accessor)))
                        candidates.Add((owner, entry));
            var winners = candidates.Where(candidate => !candidates.Any(other => !other.Owner.Equals(candidate.Owner) && Closure(other.Owner).Contains(candidate.Owner))).ToList();
            if (winners.Count != 1)
                throw new NSGetterRuntimeError($"No most specific implementation of '{declaration.Name}' for '{NeoTypeNames.Of(receiver)}'.");
            var winner = winners[0];
            if (winner.Member.implementationMemberId is null || winner.Member.DeclaredModifier == NeoMemberModifierKind.Abstract)
                throw Missing(receiver, requested, declaration.Name);
            return mappings[key] = Body(winner.Member, winner.Owner);
        }

        private static NSGetterRuntimeError Missing(TypeBinding receiver, TypeBinding requested, string name) =>
            new($"'{NeoTypeNames.Of(receiver)}' does not implement interface member '{NeoTypeNames.Of(requested)}.{name}'.");

        private static Implementation Body(InterfaceMember member, TypeBinding construction) =>
            member.implementationMemberId is string id
                ? new Implementation(id, member.IsStatic ? null : construction, member.IsStatic ? construction : null)
                : throw new NSGetterRuntimeError("The interface member has no implementation body.");

        private Dictionary<string, List<Member>> ExplicitSites(string classId)
        {
            if (explicitByClass.TryGetValue(classId, out var sites))
                return sites;
            sites = new();
            var functions = new Dictionary<string, List<Member>>();
            ownFunctionsByName[classId] = functions;
            var declaration = client.ProjectDataForRuntime.classes[classId];
            foreach (var key in (declaration.schemaKeyOrder ?? Array.Empty<string>()).Concat(declaration.schema.Keys).Distinct())
            {
                if (!declaration.schema.TryGetValue(key, out var id))
                    continue;
                var member = client.ProjectDataForRuntime.members[id];
                classSchemaKeys[member.id] = key;
                if (member is NSFunctionMember or FunctionMember && member.explicitImplementation is null)
                {
                    if (!functions.TryGetValue(member.name, out var overloads))
                        functions[member.name] = overloads = new();
                    overloads.Add(member);
                }
                if (member.explicitImplementation is not { } implementation)
                    continue;
                if (!sites.TryGetValue(implementation.interfaceMemberId, out var entries))
                    sites[implementation.interfaceMemberId] = entries = new();
                entries.Add(member);
            }
            return explicitByClass[classId] = sites;
        }

        private Implementation? Explicit(TypeBinding owner, TypeBinding requested, string memberId, string accessor, bool isStatic)
        {
            if (ExplicitSites(owner.Head!.ClassId!).TryGetValue(memberId, out var sites))
                foreach (var member in sites)
                    if (Construct(member.explicitImplementation!, owner).Equals(requested) && HasAccessor(member, accessor))
                        return new Implementation(member.id, staticConstruction: isStatic ? owner : null);
            return null;
        }

        private Implementation? MapClass(TypeBinding owner, TypeBinding requested, string memberId, string name, InterfaceMember contract, string accessor)
        {
            var key = (owner, requested, memberId, accessor);
            if (classMappings.TryGetValue(key, out var cached))
                return cached;
            var searched = new List<TypeBinding>();
            Implementation? result = null;
            foreach (var current in ClassChain(owner))
            {
                if (classMappings.TryGetValue((current, requested, memberId, accessor), out result))
                    break;
                searched.Add(current);
                result = Explicit(current, requested, memberId, accessor, contract.IsStatic)
                    ?? Implicit(current, requested, name, contract, accessor);
                if (result is not null)
                    break;
            }
            // Cache each searched suffix, including misses, so repeated base-list entries
            // do not restart the same inheritance walk for this member.
            foreach (var current in searched)
                classMappings[(current, requested, memberId, accessor)] = result;
            return result;
        }

        private Implementation? Implicit(TypeBinding owner, TypeBinding requested, string name, InterfaceMember contract, string accessor)
        {
            if (contract.kind == NeoInterfaceMemberKind.Function)
            {
                var matches = MatchingFunctions(owner, requested, name, contract, accessor);
                if (matches.Count == 0)
                    return null;
                var declaringOwner = owner;
                Member? chosen = LastVirtualRoot(matches);
                if (chosen is null)
                {
                    if (matches[0].extendsMemberId is null)
                        chosen = matches[0];
                    else
                    {
                        // An override reuses a slot. The nearest base with a matching
                        // virtual declaration supplies the slot, even if it is not this
                        // override's root; dispatch then follows that slot's own key.
                        var chain = ClassChain(owner);
                        for (int index = 1; index < chain.Count; index++)
                        {
                            chosen = LastVirtualRoot(MatchingFunctions(chain[index], requested, name, contract, accessor));
                            if (chosen is null)
                                continue;
                            declaringOwner = chain[index];
                            break;
                        }
                    }
                }
                // Valid overrides always have a root; retain the override like TS if none matches.
                chosen ??= matches[0];
                return new Implementation(chosen.id, staticConstruction: contract.IsStatic ? declaringOwner : null);
            }
            if (!client.ProjectDataForRuntime.classes[owner.Head!.ClassId!].schema.TryGetValue(name, out var id))
                return null;
            var member = client.ProjectDataForRuntime.members[id];
            if (!Eligible(member, contract, accessor))
                return null;
            if (contract.kind == NeoInterfaceMemberKind.Property)
            {
                TypeInfo? actual = member is NSPropertyMember property ? property.returnTypeInfo
                    : NeoTypeRules.BindingTypeInfo(client, NeoGenericResolution.SubstituteMember(client, member, Environment(owner)));
                if (actual is null || !MatchesType(actual, contract.typeInfo!, owner, requested))
                    return null;
            }
            return new Implementation(member.id, staticConstruction: contract.IsStatic ? owner : null);
        }

        // Called only while filling a class mapping. The per-class name index retains
        // schemaKeyOrder followed by omitted keys; warm Resolve calls use mappings.
        private IReadOnlyList<Member> MatchingFunctions(TypeBinding owner, TypeBinding requested,
            string name, InterfaceMember contract, string accessor)
        {
            ExplicitSites(owner.Head!.ClassId!);
            if (!ownFunctionsByName[owner.Head.ClassId!].TryGetValue(name, out var overloads))
                return Array.Empty<Member>();
            var matches = new List<Member>();
            foreach (var candidate in overloads)
            {
                if (!Eligible(candidate, contract, accessor))
                    continue;
                var signature = Signature(candidate);
                var parameters = signature.Arguments;
                if ((signature.Generics?.Length ?? 0) != (contract.genericParams?.Length ?? 0)
                    || parameters.Length != contract.argumentTypes!.Count)
                    continue;
                bool matchesParameters = true;
                for (int index = 0; index < parameters.Length; index++)
                {
                    if (parameters[index].mode != contract.argumentTypes[index].mode
                        || !MatchesType(parameters[index], contract.argumentTypes[index], owner, requested))
                    {
                        matchesParameters = false;
                        break;
                    }
                }
                if (matchesParameters && MatchesType(signature.Result, contract.returnTypeInfo!, owner, requested))
                    matches.Add(candidate);
            }
            return matches;
        }

        private static Member? LastVirtualRoot(IReadOnlyList<Member> matches)
        {
            for (int index = matches.Count - 1; index >= 0; index--)
                if (matches[index].extendsMemberId is null
                    && matches[index].Modifier is NeoMemberModifierKind.Virtual or NeoMemberModifierKind.Abstract)
                    return matches[index];
            return null;
        }

        private readonly Dictionary<string, (FunctionArgumentTypeInfo[] Arguments, FunctionGenericParam[]? Generics, TypeInfo Result)> signatures = new();

        private (FunctionArgumentTypeInfo[] Arguments, FunctionGenericParam[]? Generics, TypeInfo Result) Signature(Member member)
        {
            if (signatures.TryGetValue(member.id, out var cached))
                return cached;
            var arguments = member is NSFunctionMember function ? function.argumentTypes : ((FunctionMember)member).argumentTypes;
            var generics = member is NSFunctionMember script ? script.genericParams : ((FunctionMember)member).genericParams;
            var result = member is NSFunctionMember method ? method.returnTypeInfo : ((FunctionMember)member).returnTypeInfo;
            if (member.extendsMemberId is string parent)
            {
                var inherited = Signature(client.ProjectDataForRuntime.members[parent]);
                arguments ??= inherited.Arguments;
                generics ??= inherited.Generics;
                result ??= inherited.Result;
            }
            return signatures[member.id] = (arguments, generics, result);
        }

        private static bool Eligible(Member member, InterfaceMember contract, string accessor) =>
            member.explicitImplementation is null && member.Access == NeoMemberAccessKind.Public
            && (member.Modifier == NeoMemberModifierKind.Static) == contract.IsStatic && HasAccessor(member, accessor)
            && (accessor != "get" || member.GetterAccess == NeoMemberAccessKind.Public)
            && (accessor != "set" || member.SetterAccess == NeoMemberAccessKind.Public);

        private bool MatchesType(TypeInfo actual, TypeInfo expected, TypeBinding owner, TypeBinding requested) =>
            NeoCallableSignature.TypeIdentity(NeoGenericResolution.SubstituteTypeInfo(client, actual, Environment(owner))).Equals(
                NeoCallableSignature.TypeIdentity(NeoGenericResolution.SubstituteTypeInfo(client, expected, Environment(requested))));

        private static bool InterfaceHasAccessor(InterfaceMember member, string accessor) => accessor == "get"
            ? member.Accessors is NeoPropertyAccessorsKind.Get or NeoPropertyAccessorsKind.GetSet or NeoPropertyAccessorsKind.GetInit
            : member.Accessors is NeoPropertyAccessorsKind.Set or NeoPropertyAccessorsKind.GetSet or NeoPropertyAccessorsKind.GetInit or NeoPropertyAccessorsKind.Init;

        private readonly Dictionary<string, Member> storedMembers = new();

        // A bodied override changes dispatch; its nearest stored ancestor still owns the slot.
        internal Member StoredMember(Member member)
        {
            if (member is not NSPropertyMember || member.extendsMemberId is null)
                return member;
            if (storedMembers.TryGetValue(member.id, out var cached))
                return cached;
            var current = member;
            var seen = new HashSet<string>();
            while (current is NSPropertyMember && current.extendsMemberId is string parent && seen.Add(current.id)
                && client.ProjectDataForRuntime.members.TryGetValue(parent, out var inherited))
                current = inherited;
            return storedMembers[member.id] = current is NSPropertyMember or NSFunctionMember or FunctionMember ? member : current;
        }

        internal Member ClassAccessor(string? classId, string memberId, string accessor, bool direct = false)
        {
            var declared = client.ProjectDataForRuntime.members[memberId];
            if (direct || classId is null || declared.Access == NeoMemberAccessKind.Private
                || accessor == "get" && declared.GetterAccess == NeoMemberAccessKind.Private
                || accessor == "set" && declared.SetterAccess == NeoMemberAccessKind.Private)
                return DeclaredAccessor(memberId, accessor);
            var key = (classId, memberId, accessor);
            if (accessors.TryGetValue(key, out var cached))
                return cached;
            string? schemaKey = client.FindSchemaPlacement(memberId)?.schemaKey;
            string selected = schemaKey is null ? memberId : client.ResolveInstanceSurfaceMember(classId, schemaKey)?.memberId ?? memberId;
            return accessors[key] = DeclaredAccessor(selected, accessor);
        }

        private Member DeclaredAccessor(string memberId, string accessor)
        {
            var member = client.ProjectDataForRuntime.members[memberId];
            var visited = new HashSet<string>();
            while (!HasAccessor(member, accessor) && member.extendsMemberId is string parent && visited.Add(member.id))
                member = client.ProjectDataForRuntime.members[parent];
            return member;
        }

        internal static bool HasAccessor(Member member, string accessor) => accessor switch
        {
            "call" => member is NSFunctionMember or FunctionMember,
            "get" => member is NSPropertyMember property ? property.Accessors is NeoPropertyAccessorsKind.Get or NeoPropertyAccessorsKind.GetSet or NeoPropertyAccessorsKind.GetInit : member is not NSFunctionMember and not FunctionMember,
            "set" => member is NSPropertyMember property ? property.Accessors is NeoPropertyAccessorsKind.Set or NeoPropertyAccessorsKind.GetSet or NeoPropertyAccessorsKind.GetInit or NeoPropertyAccessorsKind.Init : member.Mutability != NeoMemberMutabilityKind.ReadOnly || member.DeclaredAccessors == NeoPropertyAccessorsKind.GetInit,
            _ => false,
        };

        private Implementation DispatchVirtual(TypeBinding receiver, Implementation selected, string accessor)
        {
            var target = client.ProjectDataForRuntime.members[selected.MemberId];
            if (target.Modifier is not (NeoMemberModifierKind.Virtual or NeoMemberModifierKind.Abstract) && target.extendsMemberId is null)
                return selected;
            if (classSchemaKeys.TryGetValue(target.id, out var schemaKey)
                && client.ResolveInstanceSurfaceMember(receiver.Head!.ClassId!, schemaKey) is { } entry)
            {
                var implementation = DeclaredAccessor(entry.memberId, accessor);
                if (implementation.id == target.id || Extends(implementation, target.id))
                    return new Implementation(implementation.id);
            }
            return selected;
        }

        private bool Extends(Member member, string targetId)
        {
            while (member.extendsMemberId is string parent)
            {
                if (parent == targetId)
                    return true;
                member = client.ProjectDataForRuntime.members[parent];
            }
            return false;
        }
    }
}
