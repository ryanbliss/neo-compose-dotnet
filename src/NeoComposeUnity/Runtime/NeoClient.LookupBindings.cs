// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private Dictionary<string, Dictionary<(NeoValueOwnership, string), ObjectMemberValue>>? writableLookupBindingsByField;
        private readonly Dictionary<(NeoValueOwnership, string), ObjectMemberValue> writableLookupBindingRows = new();
        private readonly Dictionary<string, HashSet<string>> virtualLookupBindingsByMember = new(StringComparer.Ordinal);

        private void IndexLookupBindingRow(NeoValueOwnership ownership, string id, MemberValue? value)
        {
            if (writableLookupBindingsByField is null)
                return;
            var key = (ownership, id);
            if (writableLookupBindingRows.Remove(key, out var before) && before.value is not null)
                foreach (string field in before.value.Keys)
                    if (writableLookupBindingsByField.TryGetValue(field, out var rows))
                    {
                        rows.Remove(key);
                        if (rows.Count == 0)
                            writableLookupBindingsByField.Remove(field);
                    }
            if (value is not ObjectMemberValue { value: not null } obj)
                return;
            writableLookupBindingRows[key] = obj;
            foreach (string field in obj.value.Keys)
            {
                if (!writableLookupBindingsByField.TryGetValue(field, out var rows))
                    writableLookupBindingsByField[field] = rows = new();
                rows[key] = obj;
            }
        }

        private void RemoveVirtualLookupBinding(string memberId, string childId)
        {
            if (virtualLookupBindingsByMember.TryGetValue(memberId, out var bindings))
            {
                bindings.Remove(childId);
                if (bindings.Count == 0)
                    virtualLookupBindingsByMember.Remove(memberId);
            }
        }

        private Dictionary<string, Dictionary<(NeoValueOwnership, string), ObjectMemberValue>> WritableLookupBindings()
        {
            if (writableLookupBindingsByField is null)
            {
                writableLookupBindingsByField = new(StringComparer.Ordinal);
                foreach (var row in sessionData.values.Values)
                    IndexLookupBindingRow(NeoValueOwnership.Session, row.id, row);
                foreach (var row in saveData.values.Values)
                    IndexLookupBindingRow(NeoValueOwnership.Save, row.id, row);
                foreach (var row in userData.values.Values)
                    IndexLookupBindingRow(NeoValueOwnership.User, row.id, row);
            }
            return writableLookupBindingsByField;
        }

        private bool TryFindIndexedLookupBinding(string memberId, [NotNullWhen(true)] out string? valueId)
        {
            valueId = null;
            var index = ValueInferenceIndex;
            if (!index.SchemaKeysByMember.TryGetValue(memberId, out var schemaKeys))
                return false;
            var writableBindings = WritableLookupBindings();
            // A save client reads the User layer's bindings from its user client (P104 §4.3).
            var userBindings = AttachedUserClient?.WritableLookupBindings();
            string? found = null;
            foreach (string key in schemaKeys)
            {
                if (writableBindings.TryGetValue(key, out var writableRows))
                    foreach (var row in writableRows.Values)
                        if (!Accept(row.value![key]))
                            return false;
                if (userBindings?.TryGetValue(key, out var userRows) == true)
                    foreach (var row in userRows)
                        if (row.Key.Item1 == NeoValueOwnership.User && !Accept(row.Value.value![key]))
                            return false;
                if (index.ObjectRowsByField.TryGetValue(key, out var authoredRows))
                    foreach (var row in authoredRows)
                        if (!Accept(row.value![key]))
                            return false;
            }
            if (virtualLookupBindingsByMember.TryGetValue(memberId, out var virtualIds)
                || AttachedUserClient?.virtualLookupBindingsByMember.TryGetValue(memberId, out virtualIds) == true)
                foreach (string id in virtualIds)
                    if (TryResolveVirtualPlacement(id, out var placement)
                        && ResolveValueRow(placement.parentValueId) is ObjectMemberValue parent && !parent.IsRemoved)
                        foreach (string key in schemaKeys)
                            if (ResolveClassChildRow(parent, key)?.id == id && !Accept(id))
                                return false;
            valueId = found;
            return found is not null;

            bool Accept(string childId)
            {
                if (found is null)
                    found = childId;
                return found == childId;
            }
        }
    }
}
