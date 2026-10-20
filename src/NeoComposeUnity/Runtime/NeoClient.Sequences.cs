// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        internal NeoScript.NSGetterEvaluator.Context? ActiveScriptContext;
        internal readonly ConditionalWeakTable<object, TypeInfo> SequenceCollectionElements = new();
        private void ValidateStoredSequenceMembers()
        {
            var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (Interface declaration in data.interfaces.Values)
            {
                if (declaration.extendsInterfaceIds is null)
                    continue;
                foreach (string parent in declaration.extendsInterfaceIds)
                {
                    if (!children.TryGetValue(parent, out var descendants))
                        children[parent] = descendants = new();
                    descendants.Add(declaration.id);
                }
            }
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(NeoSequenceContract.Enumerator);
            while (queue.Count != 0)
            {
                string id = queue.Dequeue();
                if (!cursors.Add(id) || !children.TryGetValue(id, out var descendants))
                    continue;
                foreach (string child in descendants)
                    queue.Enqueue(child);
            }
            bool hasCursorShape = false;
            foreach (Member member in data.members.Values)
                if (member is ClassMember { DeclaredInterfaceId: { } id } && cursors.Contains(id))
                {
                    hasCursorShape = true;
                    break;
                }
            if (!hasCursorShape)
                return;
            foreach (NeoSchemaClass declaration in data.classes.Values)
            {
                var env = NeoGenericResolution.ResolveEnv(this, declaration.id);
                var checkedMembers = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (MergedSchemaEntry entry in ResolveInstanceSurfaceSchema(declaration.id))
                {
                    if (TryGetMember(entry.memberId, out Member? member) && ContainsCursor(member))
                        throw new InvalidOperationException($"Stored member '{declaration.name}.{entry.schemaKey}' contains IEnumerator<T>. Use a computed property or store a List explicitly.");
                }

                bool ContainsCursor(Member member)
                {
                    if (member is NSFunctionMember or FunctionMember or NSPropertyMember or DelegateMember)
                        return false;
                    if (checkedMembers.TryGetValue(member.id, out bool known))
                        return known;
                    string declarationId = member.id;
                    checkedMembers[declarationId] = false;
                    member = NeoGenericResolution.SubstituteMember(this, member, env);
                    bool result = member switch
                    {
                        ClassMember { DeclaredInterfaceId: { } id } when cursors.Contains(id) => true,
                        ClassMember { TypeArguments: { } arguments } => HasCursorArgument(arguments),
                        ListMember list => ChildContainsCursor(list.entryMemberId),
                        DictionaryMember dictionary => ChildContainsCursor(dictionary.entryMemberId),
                        TupleMember tuple => TupleContainsCursor(tuple),
                        _ => false,
                    };
                    checkedMembers[declarationId] = result;
                    return result;
                }
                bool ChildContainsCursor(string id) => TryGetMember(id, out Member? member) && ContainsCursor(member);
                bool TupleContainsCursor(TupleMember tuple)
                {
                    foreach (string id in tuple.elementMemberIds)
                        if (ChildContainsCursor(id))
                            return true;
                    return false;
                }
                bool HasCursorArgument(Dictionary<string, GenericBinding> arguments)
                {
                    foreach (GenericBinding binding in arguments.Values)
                    {
                        string? id = binding.memberId;
                        if (binding.IsForward && binding.genericParamId is { } parameter && env.TryGetValue(parameter, out var resolved))
                            id = resolved.memberId;
                        if (id is not null && ChildContainsCursor(id))
                            return true;
                    }
                    return false;
                }
            }
        }

    }
}
