// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        internal Dictionary<string, GameSaveListenerEndpointLocator> CaptureListenerEndpoints(
            IReadOnlyDictionary<string, HashSet<string>>? dirtyOwners = null)
        {
            var result = new Dictionary<string, GameSaveListenerEndpointLocator>(StringComparer.Ordinal);
            if (saveData.changeListeners is null)
                return result;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rootId in dirtyOwners?.Keys ?? saveData.changeListeners.Keys)
            {
                if (!saveData.changeListeners.TryGetValue(rootId, out var map))
                    continue;
                IEnumerable<string> owners = dirtyOwners is null ? map.Keys : dirtyOwners[rootId];
                foreach (string ownerId in owners)
                {
                    if (!map.TryGetValue(ownerId, out var entry))
                        continue;
                    ids.Add(ownerId);
                    foreach (var targets in entry.Values)
                        foreach (var target in targets)
                            if (target.valueId is not null)
                                ids.Add(target.valueId);
                }
            }
            var containers = new Dictionary<(NeoValueOwnership scope, string id), Dictionary<string, GameSaveListenerEndpointStep>>();
            foreach (string id in ids)
            {
                // Previously saved stale targets may be removed without resolving them.
                var scope = ListenerOwnerScope(NeoValueOwnership.Save, id);
                if (!TryGetValue(scope, id, out MemberValue? row) || row.IsRemoved)
                    continue;
                var locator = new GameSaveListenerEndpointLocator { valueId = id };
                var visited = new HashSet<(NeoValueOwnership scope, string id)>();
                string current = id;
                while (true)
                {
                    if (!visited.Add((scope, current)))
                        break;
                    if (locator.steps.Count > 64)
                        break;
                    if (!TryFindOwnedParent(scope, current, out string? parent, out var parentScope))
                        break;
                    if (parent.StartsWith("member:", StringComparison.Ordinal) || parent.StartsWith("static:", StringComparison.Ordinal))
                    {
                        locator.rootId = current;
                        locator.rootMemberId = parent.Substring(parent.IndexOf(':') + 1);
                        locator.steps.Reverse();
                        result.Add(id, locator);
                        break;
                    }
                    if (!containers.TryGetValue((parentScope, parent), out var locations))
                    {
                        locations = ListenerChildLocations(parentScope, parent);
                        containers.Add((parentScope, parent), locations);
                    }
                    if (!locations.TryGetValue(current, out var step))
                        break;
                    locator.steps.Add(step);
                    current = parent;
                    scope = parentScope;
                }
            }
            return result;
        }

        private Dictionary<string, GameSaveListenerEndpointStep> ListenerChildLocations(NeoValueOwnership scope, string parentId)
        {
            var result = new Dictionary<string, GameSaveListenerEndpointStep>(StringComparer.Ordinal);
            if (!TryGetValue(scope, parentId, out MemberValue? parent) || parent.IsRemoved)
                return result;
            TryInferMemberForValueId(parentId, out Member? member);
            if (member is ListMember list && IsUnorderedList(list))
            {
                foreach (string childId in EnumerateContainerMemberValueIds(scope, parentId))
                    result[childId] = new GameSaveListenerEntryStep { valueId = childId };
                return result;
            }
            if (parent is ArrayMemberValue array)
            {
                if (array.value is not null)
                    for (int index = 0; index < array.value.Length; index++)
                        result.TryAdd(array.value[index], new GameSaveListenerListStep { index = index });
                return result;
            }
            if (parent is not ObjectMemberValue obj)
                return result;
            if (obj.value is not null)
                foreach (var child in obj.value)
                    Add(child.Key, child.Value);
            if (TryResolveVirtualClassChildren(parentId, out var virtualChildren))
                foreach (var child in virtualChildren)
                    if (obj.value?.ContainsKey(child.Key) != true)
                        Add(child.Key, child.Value);
            if (member is not DictionaryMember)
                foreach (var child in EnumerateOwnedChildLinks(obj, member))
                    if (child.member is not null)
                        result.TryAdd(child.valueId, new GameSaveListenerMemberStep { memberId = child.member.id });
            return result;

            void Add(string key, string childId)
            {
                if (member is DictionaryMember)
                    result.TryAdd(childId, new GameSaveListenerDictionaryStep { key = key });
                else if (TryResolveOwnedChildMember(obj, member, key) is Member childMember)
                    result.TryAdd(childId, new GameSaveListenerMemberStep { memberId = childMember.id });
            }
        }
    }
}
