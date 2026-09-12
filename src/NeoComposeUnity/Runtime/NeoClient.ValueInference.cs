// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private int valueInferenceScopeDepth;
        private AuthoredValueInferenceIndex? authoredValueInferenceIndex;

        // Constructor replay asks for the placement of every sparse instance.
        // Index the immutable authored rows once for that operation. Writable
        // overlays remain live scans because constructors can change them.
        private IDisposable BeginValueInferenceScope()
        {
            valueInferenceScopeDepth++;
            return new NeoDisposableAction(() =>
            {
                if (--valueInferenceScopeDepth == 0) authoredValueInferenceIndex = null;
            });
        }

        private AuthoredValueInferenceIndex ValueInferenceIndex =>
            authoredValueInferenceIndex ??= new AuthoredValueInferenceIndex(data);

        private IEnumerable<KeyValuePair<string, MemberValue>> InferMemberParents(string childId)
        {
            if (valueInferenceScopeDepth == 0)
            {
                foreach (var pair in EnumerateAllValueRows())
                    if (MightReferenceChildValueId(pair.Value, childId)) yield return pair;
                yield break;
            }

            foreach (var pair in sessionData.values)
                if (MightReferenceChildValueId(pair.Value, childId)) yield return pair;
            foreach (var pair in saveData.values)
                if (MightReferenceChildValueId(pair.Value, childId)) yield return pair;
            if (ValueInferenceIndex.Parents.TryGetValue(childId, out var parents))
                foreach (var pair in parents) yield return pair;
        }

        private sealed class AuthoredValueInferenceIndex
        {
            internal readonly Dictionary<string, Member> Members = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, List<KeyValuePair<string, MemberValue>>> Parents =
                new(StringComparer.Ordinal);

            internal AuthoredValueInferenceIndex(ProjectData data)
            {
                // Preserve the first declaration and parent iteration order used
                // by the ordinary inference path, including ambiguous references.
                foreach (Member member in data.members.Values)
                    if (member.valueId != null) Members.TryAdd(member.valueId, member);
                foreach (var pair in data.values)
                {
                    if (pair.Value is ObjectMemberValue obj)
                    {
                        if (obj.value != null)
                            foreach (string childId in obj.value.Values) Add(childId, pair);
                        if (obj.constructorArgs != null)
                            foreach (JToken? argument in obj.constructorArgs.Values)
                                if (argument?.Type == JTokenType.String)
                                    Add(argument.Value<string>(), pair);
                    }
                    else if (pair.Value is ArrayMemberValue array && array.value != null)
                        foreach (string childId in array.value) Add(childId, pair);
                }
            }

            private void Add(string? childId, KeyValuePair<string, MemberValue> parent)
            {
                if (childId == null) return;
                if (!Parents.TryGetValue(childId, out var parents))
                    Parents[childId] = parents = new List<KeyValuePair<string, MemberValue>>(1);
                if (parents.Count == 0 || parents[parents.Count - 1].Key != parent.Key)
                    parents.Add(parent);
            }
        }
    }
}
