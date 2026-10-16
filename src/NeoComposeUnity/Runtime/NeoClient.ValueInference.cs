// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private AuthoredValueInferenceIndex? authoredValueInferenceIndex;

        // Authored rows are immutable between partition/schema changes.
        // Writable parent edges are maintained by the shared write path.
        private AuthoredValueInferenceIndex ValueInferenceIndex =>
            authoredValueInferenceIndex ??= new AuthoredValueInferenceIndex(data);

        /// <summary>Whether <see cref="InferMemberParents"/> reads only committed parent edges.</summary>
        internal bool InfersCommittedParents => candidateReplay is null && candidateReadPlan is null;

        internal IEnumerable<KeyValuePair<string, MemberValue>> InferMemberParents(string childId)
        {
            HashSet<string> candidates = RentIdSet();
            try
            {
                CollectPlacementParents(childId, candidates);
                if (candidateReadPlan is not null)
                {
                    candidateReadPlan.CollectParentCandidates(childId, candidates);
                    if (candidateReplay?.Parents.TryGetValue(childId, out var allocatedParents) == true)
                        candidates.UnionWith(allocatedParents);
                }
                foreach (var pair in IndexedWritableParents(childId, NeoValueOwnership.Session, sessionData.values, candidates))
                    yield return pair;
                foreach (var pair in IndexedWritableParents(childId, NeoValueOwnership.Save, saveData.values, candidates))
                    yield return pair;
                foreach (var pair in IndexedWritableParents(childId, NeoValueOwnership.User, userSource.userData.values, candidates))
                    yield return pair;
                if (ValueInferenceIndex.Parents.TryGetValue(childId, out var parents))
                    foreach (var pair in parents)
                        yield return pair;
                if (candidateReadPlan is not null)
                    foreach (string id in candidates)
                        if (!sessionData.values.ContainsKey(id) && !saveData.values.ContainsKey(id)
                            && !userSource.userData.values.ContainsKey(id)
                            && !data.values.ContainsKey(id)
                            && !TryGetWritableValue(NeoValueOwnership.Session, id, out MemberValue? _)
                            && !TryGetWritableValue(NeoValueOwnership.Save, id, out MemberValue? _)
                            && !TryGetWritableValue(NeoValueOwnership.User, id, out MemberValue? _)
                            && ResolveValueRow(id) is MemberValue row && MightReferenceChildValueId(row, childId))
                            yield return new KeyValuePair<string, MemberValue>(id, row);
            }
            finally
            {
                ReturnIdSet(candidates);
            }
        }

        private IEnumerable<KeyValuePair<string, MemberValue>> IndexedWritableParents(
            string childId, NeoValueOwnership ownership,
            IReadOnlyDictionary<string, MemberValue> rows, HashSet<string> candidates)
        {
            KeyValuePair<string, MemberValue>? first = null;
            foreach (string id in candidates)
            {
                MemberValue? row;
                bool found = candidateReadPlan is null
                    ? rows.TryGetValue(id, out row)
                    : TryGetWritableValue(ownership, id, out row);
                if (!found || !MightReferenceChildValueId(row!, childId))
                    continue;
                if (first is not null)
                {
                    // Replacements retain their store position; new staged rows
                    // and private allocations follow existing rows. Only ambiguous
                    // references need this ordered fallback.
                    foreach (string orderedId in OrderedWritableParentIds(ownership, rows))
                    {
                        if (!candidates.Contains(orderedId))
                            continue;
                        found = candidateReadPlan is null
                            ? rows.TryGetValue(orderedId, out row)
                            : TryGetWritableValue(ownership, orderedId, out row);
                        if (found && MightReferenceChildValueId(row!, childId))
                            yield return new KeyValuePair<string, MemberValue>(orderedId, row!);
                    }
                    yield break;
                }
                first = new KeyValuePair<string, MemberValue>(id, row!);
            }
            if (first is not null)
                yield return first.Value;
        }

        private IEnumerable<string> OrderedWritableParentIds(
            NeoValueOwnership ownership, IReadOnlyDictionary<string, MemberValue> rows)
        {
            foreach (string id in rows.Keys)
                yield return id;
            if (candidateReadPlan is null)
                yield break;
            foreach (var pair in candidateReadPlan.Rows)
                if (pair.Key.ownership == ownership && !rows.ContainsKey(pair.Key.id))
                    yield return pair.Key.id;
            if (ownership == NeoValueOwnership.Session && candidateReplay is not null)
                foreach (string id in candidateReplay.Allocations.Keys)
                    if (!rows.ContainsKey(id) && !candidateReadPlan.Rows.ContainsKey((ownership, id)))
                        yield return id;
        }

        /// <summary>
        /// A row's owner, as grid queries climb to their grid. A collection
        /// move the script batch holds pending answers first, and a collection
        /// that released the row leaves it no owner. <paramref name="stamped"/>
        /// says the owner is an unordered list holding the row by its stamp.
        /// </summary>
        internal bool TryGetGridQueryParent(string childId, [NotNullWhen(true)] out string? parentId, out bool stamped)
        {
            stamped = false;
            if (scriptWriteBatch?.TryGetEntryParent(childId, out parentId) == true)
                return parentId is not null;
            if (TryGetValue(childId, out MemberValue? child) && !string.IsNullOrEmpty(child.containerId))
            {
                stamped = true;
                parentId = child.containerId!;
                return true;
            }
            if (TryGetValueOwnership(childId, out NeoValueOwnership ownership)
                && ownership != NeoValueOwnership.Asset
                && TryFindOwnedParent(ownership, childId, out parentId))
                return true;
            if (TryResolveVirtualPlacement(childId, out var placement))
            {
                parentId = placement.parentValueId;
                return true;
            }
            return TryGetAuthoredOwner(childId, out parentId);
        }

        /// <summary>
        /// The authored row that owns <paramref name="childId"/>, unless a
        /// writable overlay of it has released the row. Authored rows change
        /// only with the index, so each child's owner is found once.
        /// </summary>
        private bool TryGetAuthoredOwner(string childId, [NotNullWhen(true)] out string? ownerId)
        {
            AuthoredValueInferenceIndex index = ValueInferenceIndex;
            if (!index.Owners.TryGetValue(childId, out ownerId))
            {
                if (!index.Parents.TryGetValue(childId, out var parents))
                    return false;
                ownerId = null;
                foreach (var pair in parents)
                {
                    if (OwnsChild(pair.Key, pair.Value, childId))
                    {
                        ownerId = pair.Key;
                        break;
                    }
                }
                index.Owners[childId] = ownerId;
            }
            if (ownerId is null || !TryGetValue(ownerId, out MemberValue? owner))
                return false;
            return ReferenceEquals(owner, data.values[ownerId]) || OwnsChild(ownerId, owner, childId);
        }

        private bool OwnsChild(string parentId, MemberValue parent, string childId)
        {
            Member? member = TryInferMemberForValueId(parentId, out Member? inferred) ? inferred : null;
            foreach (var link in EnumerateOwnedChildLinks(parent, member))
            {
                if (link.valueId == childId)
                    return true;
            }
            return false;
        }

        private sealed class AuthoredValueInferenceIndex
        {
            internal readonly Dictionary<string, HashSet<string>> SchemaKeysByMember = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, List<ObjectMemberValue>> ObjectRowsByField = new(StringComparer.Ordinal);
            internal readonly List<Member> StaticMembers = new();
            internal readonly Dictionary<string, Member> Members = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, List<Member>> MembersByValueId = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, List<KeyValuePair<string, MemberValue>>> Parents =
                new(StringComparer.Ordinal);
            // Each asked child's authored owner, or null when only references name it.
            internal readonly Dictionary<string, string?> Owners = new(StringComparer.Ordinal);

            internal AuthoredValueInferenceIndex(ProjectData data)
            {
                foreach (var schemaClass in data.classes.Values)
                    foreach (var field in schemaClass.schema)
                    {
                        if (!SchemaKeysByMember.TryGetValue(field.Value, out var keys))
                            SchemaKeysByMember[field.Value] = keys = new(StringComparer.Ordinal);
                        keys.Add(field.Key);
                    }
                // Preserve the first declaration and parent iteration order used
                // by the ordinary inference path, including ambiguous references.
                foreach (Member member in data.members.Values)
                {
                    if (member.Modifier == NeoMemberModifierKind.Static)
                        StaticMembers.Add(member);
                    if (member.valueId != null)
                    {
                        Members.TryAdd(member.valueId, member);
                        if (!MembersByValueId.TryGetValue(member.valueId, out var declared))
                            MembersByValueId[member.valueId] = declared = new List<Member>(1);
                        declared.Add(member);
                    }
                }
                foreach (var pair in data.values)
                {
                    if (pair.Value is ObjectMemberValue obj)
                    {
                        if (obj.value != null)
                            foreach (var field in obj.value)
                            {
                                Add(field.Value, pair);
                                if (!ObjectRowsByField.TryGetValue(field.Key, out var rows))
                                    ObjectRowsByField[field.Key] = rows = new();
                                rows.Add(obj);
                            }
                        if (obj.constructorArgs != null)
                            foreach (JToken? argument in obj.constructorArgs.Values)
                                if (argument?.Type == JTokenType.String)
                                    Add(argument.Value<string>(), pair);
                    }
                    else if (pair.Value is ArrayMemberValue array && array.value != null)
                        foreach (string childId in array.value)
                            Add(childId, pair);
                }
            }

            private void Add(string? childId, KeyValuePair<string, MemberValue> parent)
            {
                if (childId == null)
                    return;
                if (!Parents.TryGetValue(childId, out var parents))
                    Parents[childId] = parents = new List<KeyValuePair<string, MemberValue>>(1);
                if (parents.Count == 0 || parents[parents.Count - 1].Key != parent.Key)
                    parents.Add(parent);
            }
        }
    }
}
