// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;
using Member = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Runtime
{
    public static partial class NeoGeneratedTypesSupport
    {
        internal static void InvalidateConstructorSchemaCaches(NeoClient client) => ConstructorSchemaCaches.Remove(client);

        private sealed class ConstructorInitializerIndex
        {
            internal readonly Dictionary<InitializerBody, string> membersByInitializer = new();
            internal readonly Dictionary<string, List<string>> declaringClasses = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, string> containerMembers = new(StringComparer.Ordinal);
        }

        /// <summary>
        /// The arguments each constructor of a declared chain was prepared
        /// with, aligned with the chain's links from the constructed class
        /// down. Preparation stops at the first base clause that reads
        /// <c>this</c>: its arguments only exist once member initialization
        /// has run. A single-link chain allocates nothing.
        /// </summary>
        internal readonly struct ConstructorChainArguments
        {
            private readonly NeoResolvedConstructorLink? link;
            private readonly object?[]? root;
            private readonly object?[]?[]? bases;

            internal ConstructorChainArguments(
                NeoResolvedConstructorLink link,
                object?[]? root,
                object?[]?[]? bases)
            {
                this.link = link;
                this.root = root;
                this.bases = bases;
            }

            /// <summary>The arguments prepared <paramref name="depth"/> links below the constructed class.</summary>
            internal object?[]? At(int depth) => depth == 0
                ? root
                : bases is not null && depth <= bases.Length ? bases[depth - 1] : null;

            internal bool TryGet(string classId, out object?[]? arguments)
            {
                NeoResolvedConstructorLink? current = link;
                for (int depth = 0; current?.record is ConstructorRecord record; depth++)
                {
                    if (record.classId == classId)
                    {
                        arguments = At(depth);
                        return arguments is not null;
                    }
                    current = current.baseLink;
                }
                arguments = null;
                return false;
            }
        }

        private static ConstructorChainArguments PrepareConstructorInitializerArguments(
            NeoClient client, NeoResolvedConstructorLink link, object?[] values, NeoScript.NSGetterEvaluator.Context ctx)
        {
            if (link.record is null)
                return default;
            if (link.baseLink?.record is null)
                return new ConstructorChainArguments(link, values, null);
            int baseCount = 0;
            for (NeoResolvedConstructorLink? baseLink = link.baseLink; baseLink?.record is not null; baseLink = baseLink.baseLink)
                baseCount++;
            var bases = new object?[]?[baseCount];
            object?[] root = values;
            ConstructorSchemaCache cache = ConstructorSchemaCaches.GetOrCreateValue(client);
            NeoResolvedConstructorLink current = link;
            for (int depth = 0; depth < baseCount; depth++)
            {
                ConstructorRecord record = current.record!;
                bool readsThis;
                lock (cache.gate)
                {
                    if (!cache.baseReadsThis.TryGetValue(record, out readsThis))
                    {
                        // Compiled IR is immutable. Inspect each constructor once
                        // through its typed graph, without serializing a second
                        // JSON tree or mistaking object-shaped literal data for IR.
                        readsThis = (record.compiledBaseArguments ?? Array.Empty<FunctionWithReturnType>())
                            .Any(body => body.parameters is { Length: > 0 }
                                && NeoScript.NeoScriptIrWalker.AnyPointer(
                                    body.instructions,
                                    pointer => pointer is VariablePointer variable
                                        && variable.variableId == body.parameters[0].id));
                        cache.baseReadsThis.Add(record, readsThis);
                    }
                }
                if (readsThis)
                    break;
                values = EvaluateDeclaredBaseArguments(client, current, values, null, ctx);
                bases[depth] = values;
                current = current.baseLink!;
            }
            return new ConstructorChainArguments(link, root, bases);
        }

        private static string? ResolveInitializerOwner(NeoClient client, InitializerBody init, Member member, string? constructedClassId)
        {
            if (constructedClassId is null)
                return null;
            ConstructorSchemaCache cache = ConstructorSchemaCaches.GetOrCreateValue(client);
            ConstructorInitializerIndex index;
            lock (cache.gate)
                index = cache.initializerIndex ??= BuildConstructorInitializerIndex(client);
            string? memberId = index.membersByInitializer.TryGetValue(init, out string? lexical) ? lexical : member.id;
            var chain = client.ResolveClassInheritanceChain(constructedClassId);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (memberId is not null && seen.Add(memberId))
            {
                if (index.declaringClasses.TryGetValue(memberId, out List<string>? owners))
                    foreach (var c in chain)
                        if (owners.Contains(c.id))
                            return c.id;
                if (index.containerMembers.TryGetValue(memberId, out string? container))
                {
                    memberId = container;
                    continue;
                }
                memberId = client.TryGetMember(memberId, out Member? declaration) ? declaration.extendsMemberId : null;
            }
            return null;
        }

        private static ConstructorInitializerIndex BuildConstructorInitializerIndex(NeoClient client)
        {
            var index = new ConstructorInitializerIndex();
            foreach (var c in client.classes.Values)
                foreach (var memberId in c.schema.Values)
                {
                    if (!index.declaringClasses.TryGetValue(memberId, out List<string>? owners))
                        index.declaringClasses.Add(memberId, owners = new List<string>());
                    owners.Add(c.id);
                }
            foreach (var member in client.members.Values)
            {
                // Resolved overrides may share a base declaration's initializer.
                // Its lexical declaration, not dictionary iteration order, wins.
                var init = InitializerOf(member);
                if (init is not null && member.DeclaresWireField("defaultValue"))
                    index.membersByInitializer[init] = member.id;
                string? entryId = member is ListMember list ? list.entryMemberId : member is DictionaryMember dict ? dict.entryMemberId : null;
                if (entryId is not null)
                    index.containerMembers.TryAdd(entryId, member.id);
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in client.members.Values)
            {
                if (InitializerOf(member) is not null)
                    continue;
                // Only owning aggregate defaults introduce lexical row scopes.
                if (member is not ClassMember && member is not ListMember && member is not DictionaryMember)
                    continue;
                var root = MemberValueFactory.CreateFromDefault(member, "initializer-index", default, default);
                if (root is not null)
                    Walk(root, member, member.id);
            }
            return index;

            void Visit(string id, Member? owner, string lexicalMemberId)
            {
                if (owner is null || !seen.Add(id) || !client.values.TryGetValue(id, out MemberValue? row))
                    return;
                if (row.init is not null)
                {
                    index.membersByInitializer.TryAdd(row.init, lexicalMemberId);
                    return;
                }
                Walk(row, owner, lexicalMemberId);
            }
            void Walk(MemberValue row, Member owner, string lexicalMemberId)
            {
                if (row is ObjectMemberValue obj && obj.value is not null && (owner is ClassMember || owner is DictionaryMember))
                    foreach (var pair in obj.value)
                        Visit(pair.Value, client.TryResolveOwnedChildMember(row, owner, pair.Key), lexicalMemberId);
                else if (row is ArrayMemberValue array && array.value is not null && owner is ListMember list && client.TryGetMember(list.entryMemberId, out Member? entry))
                    foreach (string id in array.value)
                        Visit(id, entry, lexicalMemberId);
            }
        }
    }
}
