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
    /// <summary>Interface graphs and accessor mappings, scoped to the client's schema.</summary>
    internal sealed class NeoInterfaceDispatch
    {
        private static readonly ConditionalWeakTable<NeoClient, NeoInterfaceDispatch> Caches = new();
        private readonly NeoClient client;
        private readonly object schema;
        private readonly bool schemaValidation;
        private readonly Dictionary<TypeBinding, List<TypeBinding>> closures = new();
        private readonly Dictionary<(TypeBinding, TypeBinding), bool> primitiveConformance = new();
        private readonly Dictionary<TypeBinding, bool> ownEquatable = new();

        internal bool ImplementsOwnEquatable(TypeBinding type)
        {
            if (type.Head?.Type != MemberKind.Class)
                return false;
            type = DispatchIdentity(type);
            if (ownEquatable.TryGetValue(type, out bool cached))
                return cached;
            return ownEquatable[type] = Closure(type).Any(item => item.Head!.InterfaceId == NeoPrimitiveConformance.InterfaceId("IEquatable`1")
                && item.Bindings!.Values.Any(argument => DispatchIdentity(argument).Equals(type)));
        }
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
            internal readonly string? PrimitiveMember;
            internal readonly TypeBinding? PrimitiveType;
            internal NSGetterEvaluator.CallSiteTarget? CallTarget;
            internal Implementation(string memberId, TypeBinding? interfaceConstruction = null, TypeBinding? staticConstruction = null, string? primitiveMember = null, TypeBinding? primitiveType = null)
            {
                MemberId = memberId;
                InterfaceConstruction = interfaceConstruction;
                StaticConstruction = staticConstruction;
                PrimitiveMember = primitiveMember;
                PrimitiveType = primitiveType;
            }
        }

        private NeoInterfaceDispatch(NeoClient client, bool schemaValidation = false)
        {
            this.client = client;
            this.schemaValidation = schemaValidation;
            schema = client.SchemaResolution;
        }

        // Declaration validation uses symbolic parameters and separate caches;
        // runtime dispatch continues to require fully closed constructions.
        internal static NeoInterfaceDispatch ForSchemaValidation(NeoClient client) => new(client, schemaValidation: true);

        private TypeBinding DispatchIdentity(TypeBinding binding) => schemaValidation
            ? SchemaIdentity(binding) : Identity(binding);

        private static TypeBinding SchemaIdentity(TypeBinding binding) => binding.IsOpen ? binding
            : TypeBinding.Closed(binding.Head!.WithoutNames().WithRequired(
                TypeBindings.IsValueType(binding.Head) ? binding.Head.Required : true),
                binding.Bindings?.ToDictionary(pair => pair.Key, pair => SchemaIdentity(pair.Value)));

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

        private TypeBinding Construct(InterfaceConstruction declared, TypeBinding owner) =>
            DispatchIdentity(TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: declared.interfaceId),
                declared.arguments?.ToDictionary(pair => pair.Key, pair => Substitute(pair.Value, owner))));

        internal IReadOnlyList<TypeBinding> ClassChain(TypeBinding type)
        {
            type = DispatchIdentity(type);
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
                current = DispatchIdentity(TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: parent),
                    declaration.extendsGenericBindings?.ToDictionary(pair => pair.Key, pair => Substitute(pair.Value, current))));
            }
            classes[type] = chain;
            return chain;
        }

        internal IReadOnlyList<TypeBinding> PrimitiveClosure(TypeBinding type)
        {
            if (closures.TryGetValue(type, out var cached))
                return cached;
            var result = new List<TypeBinding>();
            if (NeoPrimitiveConformance.Kind(type) is not { } primitiveKind
                || type.Head is { Required: false } nullable && TypeBindings.IsValueType(nullable))
                return closures[type] = result;
            foreach (var row in NeoPrimitiveConformance.Table[primitiveKind])
            {
                if (!client.ProjectDataForRuntime.interfaces.TryGetValue(row.InterfaceId, out var declaration))
                    throw new NSGetterRuntimeError($"Canonical interface '{row.InterfaceId}' was not found.");
                var arguments = new Dictionary<string, TypeBinding>();
                for (int i = 0; i < row.Arguments.Length; i++)
                {
                    var argument = row.Arguments[i] switch
                    {
                        NeoConformanceArgumentKind.Self => type,
                        NeoConformanceArgumentKind.Int => TypeBinding.Closed(new TypeHead(MemberKind.Int, true)),
                        _ => TypeBinding.Closed(new TypeHead(MemberKind.Bool, true)),
                    };
                    arguments.Add(declaration.genericParams![i].id, argument);
                }
                result.Add(TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: row.InterfaceId), arguments));
            }
            closures[type] = result;
            return result;
        }

        internal IReadOnlyList<TypeBinding> Closure(TypeBinding type)
        {
            type = DispatchIdentity(type);
            if (closures.TryGetValue(type, out var found))
                return found;
            if (NeoPrimitiveConformance.Kind(type) is not null)
                return PrimitiveClosure(type);
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
            from = DispatchIdentity(from);
            to = DispatchIdentity(to);
            if (from.Equals(to))
                return true;
            if (from.IsOpen || to.IsOpen)
                return false;
            if (to.Head!.Type == MemberKind.Interface && NeoPrimitiveConformance.Kind(from) is not null)
                return PrimitiveConforms(from, to);
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

        internal bool PrimitiveConforms(TypeBinding type, TypeBinding requested)
        {
            type = DispatchIdentity(type);
            requested = DispatchIdentity(requested);
            var key = (type, requested);
            if (primitiveConformance.TryGetValue(key, out bool cached))
                return cached;
            if (NeoPrimitiveConformance.Kind(type) is not { } kind)
                return primitiveConformance[key] = false;
            if (!type.Head!.Required && TypeBindings.IsValueType(type.Head))
                return primitiveConformance[key] = false;
            if (!NeoPrimitiveConformance.Table[kind].Any(row => row.InterfaceId == requested.Head!.InterfaceId))
                return primitiveConformance[key] = false;
            return primitiveConformance[key] = Closure(type).Contains(requested);
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
            receiver = DispatchIdentity(receiver);
            requested = DispatchIdentity(requested);
            var key = (receiver, requested, memberId, accessor, direct);
            if (mappings.TryGetValue(key, out var cached))
                return cached;
            var declaration = Declaration(requested, memberId);
            var contract = declaration.Member;
            if (NeoPrimitiveConformance.Kind(receiver) is { } primitiveKind)
            {
                if (!PrimitiveConforms(receiver, requested))
                    throw new NeoScriptPreExecutionValidationError($"Primitive '{NeoTypeNames.Of(receiver)}' has no intrinsic for interface member '{memberId}'.");
                string intrinsic = NeoPrimitiveIntrinsics.Resolve(primitiveKind, requested.Head!.InterfaceId!, declaration.Name);
                return mappings[key] = intrinsic == "default" ? Body(contract, requested)
                    : new Implementation(memberId, primitiveMember: intrinsic, primitiveType: receiver);
            }
            if (direct || contract.Access == NeoMemberAccessKind.Private || contract.DeclaredModifier == NeoMemberModifierKind.Sealed
                || accessor == "get" && contract.GetterAccess == NeoMemberAccessKind.Private
                || accessor == "set" && contract.SetterAccess == NeoMemberAccessKind.Private)
                return mappings[key] = Body(contract, requested);
            if (receiver.Head!.Type == MemberKind.Class)
            {
                var mapped = ListedImplementer(receiver, requested, memberId, declaration.Name, contract, accessor);
                if (mapped is not null)
                    return mappings[key] = DispatchVirtual(receiver, mapped, accessor);
            }
            return mappings[key] = DefaultImplementation(receiver, requested, memberId, declaration.Name, contract, accessor);
        }

        // CoreCLR's two default passes: exact constructed targets first; only
        // then variance, in the interface-map order added by each class level.
        private Implementation DefaultImplementation(TypeBinding receiver, TypeBinding requested, string memberId,
            string name, InterfaceMember contract, string accessor)
        {
            var levels = DefaultLevels(receiver);
            bool exact = levels.Any(level => level.Contains(requested));
            var candidates = new List<(TypeBinding Owner, InterfaceMember Member)>();
            foreach (var level in levels)
                foreach (var owner in level)
                {
                    if (owner.Head!.InterfaceId == requested.Head!.InterfaceId
                        && (exact ? owner.Equals(requested) : Converts(owner, requested)))
                        candidates.Add((owner, contract));
                    else
                        foreach (var entry in client.ProjectDataForRuntime.interfaces[owner.Head.InterfaceId!].members.Values)
                            if (entry.explicitImplementation is { } site && site.interfaceMemberId == memberId
                                && (entry.kind != NeoInterfaceMemberKind.Property || InterfaceHasAccessor(entry, accessor))
                                && (exact ? Construct(site, owner).Equals(requested) : Converts(Construct(site, owner), requested)))
                                candidates.Add((owner, entry));
                }
            (TypeBinding Owner, InterfaceMember Member)? winner = null;
            if (exact)
            {
                var winners = candidates.Where(candidate => !candidates.Any(other =>
                    !other.Owner.Equals(candidate.Owner) && Converts(other.Owner, candidate.Owner))).ToList();
                if (winners.Count > 1)
                    throw new NSGetterRuntimeError($"No most specific implementation of '{name}' for '{NeoTypeNames.Of(receiver)}'.");
                if (winners.Count == 1)
                    winner = winners[0];
            }
            else
            {
                // The first CoreCLR candidate slot never disappears. Only a
                // more-specific holder of another declaration can replace it;
                // later unrelated slots cannot affect the first slot's answer.
                foreach (var candidate in candidates)
                    if (winner is null || candidate.Owner.Head!.InterfaceId != winner.Value.Owner.Head!.InterfaceId
                        && Converts(candidate.Owner, winner.Value.Owner))
                        winner = candidate;
            }
            if (winner is not { } selected || selected.Member.implementationMemberId is null
                || selected.Member.DeclaredModifier == NeoMemberModifierKind.Abstract)
                throw Missing(receiver, requested, name);
            return Body(selected.Member, selected.Owner);
        }

        private IReadOnlyList<List<TypeBinding>> DefaultLevels(TypeBinding receiver)
        {
            if (receiver.Head!.Type == MemberKind.Interface)
                return new[] { Closure(receiver).ToList() };
            var index = ReceiverListings(receiver);
            var levels = Enumerable.Range(0, index.Levels.Count).Select(_ => new List<TypeBinding>()).ToArray();
            // A repeated construction belongs to its oldest class level, even
            // when a derived class lists it before a newly added construction.
            var seen = new HashSet<TypeBinding>();
            foreach (var entry in index.All)
                if (index.ByConstruction[entry.Closed][^1].Level == entry.Level && seen.Add(entry.Closed))
                    levels[entry.Level].Add(entry.Closed);
            return levels;
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

        private NeoInterfaceDispatch? declarationDispatch;
        private NeoInterfaceDispatch DeclarationDispatch => schemaValidation
            ? this : declarationDispatch ??= ForSchemaValidation(client);

        private sealed class ClassListings
        {
            internal readonly TypeBinding Owner;
            internal readonly TypeBinding[] Listed;

            internal ClassListings(TypeBinding owner, TypeBinding[] listed)
            {
                Owner = owner;
                Listed = listed;
            }
        }

        private readonly Dictionary<string, ClassListings> classListings = new();

        private ClassListings Listings(string classId)
        {
            if (classListings.TryGetValue(classId, out var cached))
                return cached;
            var declaration = client.ProjectDataForRuntime.classes[classId];
            var owner = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: classId),
                declaration.genericParams?.ToDictionary(parameter => parameter.id,
                    parameter => TypeBinding.Open(new TypeDefinition(MemberKind.Class, classId: classId), parameter.id)));
            return classListings[classId] = new ClassListings(owner, Listed(owner).ToArray());
        }

        private sealed class ListingEntry
        {
            internal readonly int Level;
            internal readonly int Order;
            internal readonly TypeBinding Symbolic;
            internal readonly TypeBinding Closed;
            internal ListingEntry(int level, int order, TypeBinding symbolic, TypeBinding closed)
            {
                Level = level;
                Order = order;
                Symbolic = symbolic;
                Closed = closed;
            }
        }

        private sealed class ArgumentListings
        {
            internal readonly Dictionary<TypeBinding, List<ListingEntry>> Exact = new();
            internal readonly Dictionary<string, List<ListingEntry>> Classes = new();
            internal readonly List<ListingEntry> Other = new();
        }

        private sealed class ReceiverListingIndex
        {
            internal readonly Dictionary<string, int> Levels = new();
            internal readonly Dictionary<TypeBinding, List<ListingEntry>> ByConstruction = new();
            internal readonly Dictionary<string, List<ListingEntry>> ByInterface = new();
            internal readonly Dictionary<(string, string), ArgumentListings> Arguments = new();
            internal readonly List<ListingEntry> All = new();
        }

        private readonly Dictionary<TypeBinding, ReceiverListingIndex> receiverListings = new();

        private static void AddListing<TKey>(Dictionary<TKey, List<ListingEntry>> index, TKey key, ListingEntry entry)
        {
            if (!index.TryGetValue(key, out var entries))
                index[key] = entries = new();
            entries.Add(entry);
        }

        // Both exact and variant dispatch use this index. Closing an own listing
        // is receiver work, never repeated for each requested construction.
        private ReceiverListingIndex ReceiverListings(TypeBinding receiver)
        {
            if (receiverListings.TryGetValue(receiver, out var cached))
                return cached;
            var index = new ReceiverListingIndex();
            var chain = ClassChain(receiver);
            for (int level = 0; level < chain.Count; level++)
            {
                var owner = chain[level];
                index.Levels.Add(owner.Head!.ClassId!, level);
                foreach (var symbolic in DeclarationDispatch.Listings(owner.Head.ClassId!).Listed)
                {
                    var construction = SchemaIdentity(Substitute(symbolic, owner));
                    var entry = new ListingEntry(level, index.All.Count, symbolic, construction);
                    index.All.Add(entry);
                    AddListing(index.ByConstruction, construction, entry);
                    string id = construction.Head!.InterfaceId!;
                    AddListing(index.ByInterface, id, entry);
                    foreach (var argument in construction.Bindings ?? new Dictionary<string, TypeBinding>())
                    {
                        if (!index.Arguments.TryGetValue((id, argument.Key), out var postings))
                            index.Arguments[(id, argument.Key)] = postings = new();
                        AddListing(postings.Exact, argument.Value, entry);
                        if (argument.Value.Head?.ClassId is string classId)
                            AddListing(postings.Classes, classId, entry);
                        else
                            postings.Other.Add(entry);
                    }
                }
            }
            return receiverListings[receiver] = index;
        }

        private Dictionary<string, List<string>>? classChildren;

        private IEnumerable<string> RelatedClasses(string id, bool descendants)
        {
            if (descendants)
            {
                if (classChildren is null)
                {
                    classChildren = new();
                    foreach (var owner in client.ProjectDataForRuntime.classes.Values)
                        if (owner.extendsClassId is string parent)
                        {
                            if (!classChildren.TryGetValue(parent, out var children))
                                classChildren[parent] = children = new();
                            children.Add(owner.id);
                        }
                }
                var pending = new Stack<string>();
                pending.Push(id);
                while (pending.Count != 0)
                {
                    string current = pending.Pop();
                    yield return current;
                    if (classChildren.TryGetValue(current, out var children))
                        foreach (string child in children)
                            pending.Push(child);
                }
            }
            else
                for (string? current = id; current is not null; current = client.ProjectDataForRuntime.classes[current].extendsClassId)
                    yield return current;
        }

        // A posting is only a necessary condition; Converts remains the source
        // of truth. Invariant arguments use identity. Variant class arguments
        // use declaration ancestry, so unrelated Tag<T> queries don't scan every
        // listing. Other argument kinds conservatively retain their candidates.
        private IEnumerable<ListingEntry> MatchingListings(ReceiverListingIndex index, TypeBinding requested)
        {
            string id = requested.Head!.InterfaceId!;
            if (!index.ByInterface.TryGetValue(id, out var all))
                return Array.Empty<ListingEntry>();
            IEnumerable<ListingEntry> selected = all;
            int count = all.Count;
            foreach (var parameter in client.ProjectDataForRuntime.interfaces[id].genericParams ?? new())
            {
                var argument = requested.Bindings![parameter.id];
                var postings = index.Arguments[(id, parameter.id)];
                var buckets = new List<List<ListingEntry>>();
                if (parameter.variance is null || argument.IsOpen || TypeBindings.IsValueType(argument.Head!))
                {
                    if (postings.Exact.TryGetValue(argument, out var exact))
                        buckets.Add(exact);
                }
                else if (argument.Head!.ClassId is string classId)
                {
                    foreach (string related in RelatedClasses(classId, parameter.variance == NeoGenericParamVarianceKind.Out))
                        if (postings.Classes.TryGetValue(related, out var entries))
                            buckets.Add(entries);
                    buckets.Add(postings.Other);
                }
                else
                    continue;
                int size = buckets.Sum(bucket => bucket.Count);
                if (size < count)
                {
                    selected = buckets.SelectMany(bucket => bucket);
                    count = size;
                }
                if (count == 0)
                    break;
            }
            return selected.Where(entry => Converts(entry.Closed, requested))
                .OrderBy(entry => entry.Level).ThenBy(entry => entry.Closed.Equals(requested) ? 0 : 1).ThenBy(entry => entry.Order);
        }

        private readonly Dictionary<(string ClassId, string MemberId, string Name), string?> memberOwners = new();

        private string? ParentMemberOwner(string classId, string memberId, string name) =>
            client.ProjectDataForRuntime.classes[classId].extendsClassId is string parent
                ? NearestMemberOwner(parent, memberId, name) : null;

        // Member placement does not depend on an interface construction. Share
        // the nearest relevant declaration, including an absent declaration,
        // across all construction queries for this member.
        private string? NearestMemberOwner(string classId, string memberId, string name)
        {
            if (!schemaValidation)
                return DeclarationDispatch.NearestMemberOwner(classId, memberId, name);
            var pending = new List<string>();
            var seen = new HashSet<string>();
            string? result = null;
            for (string? current = classId; current is not null; current = client.ProjectDataForRuntime.classes[current].extendsClassId)
            {
                if (memberOwners.TryGetValue((current, memberId, name), out result))
                    break;
                if (!seen.Add(current))
                    throw new NSGetterRuntimeError($"Circular class extension detected at '{current}'.");
                pending.Add(current);
                if (ExplicitSites(current).ContainsKey(memberId)
                    || client.ProjectDataForRuntime.classes[current].schema.ContainsKey(name)
                    || ownFunctionsByName[current].ContainsKey(name))
                {
                    result = current;
                    break;
                }
            }
            foreach (string current in pending)
                memberOwners[(current, memberId, name)] = result;
            return result;
        }

        private static bool ClosedSignatureType(TypeBinding binding) => binding.Kind == TypeBindingKind.Closed
            && (binding.Bindings is null || binding.Bindings.Values.All(ClosedSignatureType));

        private static bool ClosedSignatureType(TypeInfo type) => ClosedSignatureType(NeoCallableSignature.TypeIdentity(type));

        private readonly Dictionary<(string ClassId, string AncestorId), TypeBinding> ancestorConstructions = new();

        // Compose a declaration-to-ancestor environment once, then substitute
        // each caller's arguments into it. Distinct interface requests share
        // both the member-placement search and this environment, including
        // generic and static members that cannot share a closed mapping.
        private TypeBinding AncestorConstruction(TypeBinding owner, string ancestorId)
        {
            if (!schemaValidation)
                return DeclarationDispatch.AncestorConstruction(owner, ancestorId);
            string classId = owner.Head!.ClassId!;
            if (classId == ancestorId)
                return owner;
            var key = (classId, ancestorId);
            if (!ancestorConstructions.TryGetValue(key, out var template))
            {
                var pending = new List<string>();
                var seen = new HashSet<string>();
                string current = classId;
                while (current != ancestorId && !ancestorConstructions.TryGetValue((current, ancestorId), out template))
                {
                    if (!seen.Add(current))
                        throw new NSGetterRuntimeError($"Circular class extension detected at '{current}'.");
                    pending.Add(current);
                    current = client.ProjectDataForRuntime.classes[current].extendsClassId
                        ?? throw new NSGetterRuntimeError($"'{ancestorId}' is not an ancestor of '{classId}'.");
                }
                if (current == ancestorId)
                    template = Listings(ancestorId).Owner;
                for (int index = pending.Count - 1; index >= 0; index--)
                {
                    string id = pending[index];
                    template = SchemaIdentity(Substitute(template!, BaseConstruction(Listings(id).Owner)!));
                    ancestorConstructions[(id, ancestorId)] = template;
                }
            }
            return SchemaIdentity(Substitute(template!, owner));
        }

        private readonly Dictionary<(TypeBinding Owner, TypeBinding Requested), bool> constructionQueries = new();

        private TypeBinding? BaseConstruction(TypeBinding owner)
        {
            var declaration = client.ProjectDataForRuntime.classes[owner.Head!.ClassId!];
            return declaration.extendsClassId is string parent
                ? TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: parent),
                    declaration.extendsGenericBindings?.ToDictionary(pair => pair.Key, pair => Substitute(pair.Value, owner)))
                : null;
        }

        // Alpha-normalize the whole query, not each argument separately: a base
        // bound as B<T,T> must remain distinct from B<T,U>. Parameter renames then
        // share suffix answers without retaining the calling descendant's names.
        private static (TypeBinding Owner, TypeBinding Requested) ConstructionQuery(TypeBinding owner, TypeBinding requested)
        {
            var parameters = new Dictionary<TypeBinding, TypeBinding>();
            TypeBinding Normalize(TypeBinding binding)
            {
                if (binding.IsOpen)
                {
                    if (!parameters.TryGetValue(binding, out var parameter))
                    {
                        parameter = TypeBinding.Open(new TypeDefinition(MemberKind.Class, classId: owner.Head!.ClassId!),
                            parameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        parameters.Add(binding, parameter);
                    }
                    return parameter;
                }
                return TypeBinding.Closed(binding.Head!, binding.Bindings?.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => Normalize(pair.Value)));
            }
            return (Normalize(owner), Normalize(requested));
        }

        // Answer only this membership question. Compress every visited suffix,
        // including misses; never materialize inherited construction prefixes.
        private bool InheritsConstruction(TypeBinding owner, TypeBinding requested)
        {
            var pending = new List<(TypeBinding Owner, TypeBinding Requested)>();
            var seen = new HashSet<string>();
            bool result = false;
            for (var current = BaseConstruction(owner); current is not null; current = BaseConstruction(current))
            {
                var key = ConstructionQuery(current, requested);
                if (constructionQueries.TryGetValue(key, out result))
                    break;
                current = key.Owner;
                requested = key.Requested;
                string id = current.Head!.ClassId!;
                if (!seen.Add(id))
                    throw new NSGetterRuntimeError($"Circular class extension detected at '{id}'.");
                pending.Add(key);
                foreach (var construction in Listings(id).Listed)
                    if (SchemaIdentity(Substitute(construction, current)).Equals(requested))
                    {
                        result = true;
                        break;
                    }
                if (result)
                    break;
            }
            foreach (var key in pending)
                constructionQueries[key] = result;
            return result;
        }

        // One class walk for invariant and variant interfaces. At each level,
        // exact own listings precede variant own listings. An unchanged inherited
        // mapping belongs to its base level, so it cannot pre-empt a derived
        // variant member. Each listing is decided in its declaration's parameters.
        private Implementation? ListedImplementer(TypeBinding receiver, TypeBinding requested,
            string memberId, string name, InterfaceMember contract, string accessor)
        {
            var chain = ClassChain(receiver);
            var index = ReceiverListings(receiver);
            var declarations = DeclarationDispatch;
            using var entries = MatchingListings(index, requested).GetEnumerator();
            bool hasEntry = entries.MoveNext();
            string? memberOwner = NearestMemberOwner(receiver.Head!.ClassId!, memberId, name);
            while (hasEntry || memberOwner is not null)
            {
                int level = hasEntry ? entries.Current.Level : chain.Count;
                int memberLevel = memberOwner is null ? chain.Count : index.Levels[memberOwner];
                if (memberLevel < level)
                {
                    var explicitSite = Explicit(chain[memberLevel], requested, memberId, accessor, contract.IsStatic);
                    if (explicitSite is not null)
                        return explicitSite;
                    memberOwner = ParentMemberOwner(memberOwner!, memberId, name);
                    continue;
                }
                var entry = entries.Current;
                var ownerConstruction = chain[level];
                var listing = declarations.Listings(ownerConstruction.Head!.ClassId!);
                var construction = entry.Symbolic;
                var own = declarations.Explicit(listing.Owner, construction, memberId, accessor, contract.IsStatic)
                    ?? declarations.Implicit(listing.Owner, construction, name, contract, accessor);
                var matching = index.ByConstruction[entry.Closed];
                bool inherited = own is null && matching[^1].Level > level
                    && declarations.InheritsConstruction(listing.Owner, construction);
                var mapped = inherited ? null : own ?? declarations.MapClass(listing.Owner, construction, memberId, name, contract, accessor);
                if (mapped is not null)
                {
                    if (declarations.classSchemaKeys.TryGetValue(mapped.MemberId, out var schemaKey))
                        classSchemaKeys[mapped.MemberId] = schemaKey;
                    return new Implementation(mapped.MemberId,
                        staticConstruction: mapped.StaticConstruction is null ? null : SchemaIdentity(Substitute(mapped.StaticConstruction, ownerConstruction)));
                }
                hasEntry = entries.MoveNext();
                if (memberLevel == level && (!hasEntry || entries.Current.Level != level))
                    memberOwner = ParentMemberOwner(memberOwner!, memberId, name);
            }
            return null;
        }

        private Implementation? MapClass(TypeBinding owner, TypeBinding requested, string memberId, string name, InterfaceMember contract, string accessor)
        {
            var key = (owner, requested, memberId, accessor);
            if (classMappings.TryGetValue(key, out var cached))
                return cached;
            var searched = new List<TypeBinding>();
            Implementation? result = null;
            for (var id = NearestMemberOwner(owner.Head!.ClassId!, memberId, name); id is not null;
                id = ParentMemberOwner(id, memberId, name))
            {
                var current = AncestorConstruction(owner, id);
                if (classMappings.TryGetValue((current, requested, memberId, accessor), out result))
                    break;
                searched.Add(current);
                result = Explicit(current, requested, memberId, accessor, contract.IsStatic)
                    ?? Implicit(current, requested, name, contract, accessor);
                if (result is not null)
                    break;
            }
            // Cache the relevant declaration sites, including misses. Empty
            // ancestry is shared by memberOwners, not retained per request.
            foreach (var current in searched)
                classMappings[(current, requested, memberId, accessor)] = result;
            return classMappings[key] = result;
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
                TypeInfo? actual = member is NSPropertyMember property
                    ? property.returnTypeInfo ?? NeoSchemaClassInheritance.WalkExtendsMemberChain(
                        property.id,
                        parent => client.ProjectDataForRuntime.members.GetValueOrDefault(parent),
                        ancestor => ancestor is NSPropertyMember inherited ? inherited.returnTypeInfo : null,
                        requireKind: MemberKind.NSProperty)
                    : NeoTypeRules.BindingTypeInfo(client, schemaValidation ? member : NeoGenericResolution.SubstituteMember(client, member, Environment(owner)));
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
            && (accessor != "set" || member.SetterAccess == NeoMemberAccessKind.Public
                && (member.DeclaredAccessors is NeoPropertyAccessorsKind.GetInit or NeoPropertyAccessorsKind.Init)
                    == (contract.Accessors is NeoPropertyAccessorsKind.GetInit or NeoPropertyAccessorsKind.Init));

        private bool MatchesType(TypeInfo actual, TypeInfo expected, TypeBinding owner, TypeBinding requested)
        {
            if (!schemaValidation)
                return NeoCallableSignature.TypeIdentity(NeoGenericResolution.SubstituteTypeInfo(client, actual, Environment(owner))).Equals(
                    NeoCallableSignature.TypeIdentity(NeoGenericResolution.SubstituteTypeInfo(client, expected, Environment(requested))));
            TypeBinding Bind(TypeInfo type, TypeBinding construction)
            {
                var identity = NeoCallableSignature.TypeIdentity(type);
                if (ClosedSignatureType(identity))
                    return SchemaIdentity(identity);
                var bindings = new Dictionary<string, TypeBinding>();
                var chain = construction.Head!.Type == MemberKind.Class ? ClassChain(construction) : Closure(construction);
                foreach (var entry in chain)
                    foreach (var pair in entry.Bindings ?? new Dictionary<string, TypeBinding>())
                        bindings.TryAdd(pair.Key, pair.Value);
                return SchemaIdentity(Substitute(NeoCallableSignature.TypeIdentity(type), TypeBinding.Closed(construction.Head, bindings)));
            }
            return Bind(actual, owner).Equals(Bind(expected, requested));
        }

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

        // "set" selects a write body, including init during construction. Interface
        // eligibility separately requires the implementation's exact write-accessor kind.
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
