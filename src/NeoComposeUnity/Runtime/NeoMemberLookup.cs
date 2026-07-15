// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a Lookup-typed member. Stores the selected ids
    /// (in the target collection) as a string-array value. The target
    /// collection is the member named by
    /// <see cref="LookupMember.collectionMemberId"/>; the target
    /// value is either <see cref="LookupMember.collectionValueId"/>
    /// (when set) or the target member's own <c>valueId</c>.
    /// </summary>
    public class NeoMemberLookup
        : NeoMember<LookupMember, ArrayMemberValue>
    {
        public NeoMemberLookup(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberLookup(NeoClient client, LookupMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        /// <summary>Selected ids in the target collection. Empty when nothing is set.</summary>
        public string[] Selected() => value?.value ?? System.Array.Empty<string>();

        /// <summary>
        /// Resolves the selected ids against the looked-up collection
        /// and returns the matching <see cref="NeoMember"/>s.
        /// Walks: <c>collectionMemberId</c> → target member →
        /// target value (using <c>collectionValueId</c> if set, else
        /// the target member's <c>valueId</c>) → entries indexed by
        /// each selected id.
        ///
        /// <para>Resolved instances are constructed ad-hoc per call —
        /// this layer doesn't pin a global cache. Callers that hit the
        /// same Lookup repeatedly should cache the result.</para>
        /// </summary>
        public IList<NeoMember> GetSelected()
        {
            List<NeoMember> resolved = new();
            string[] selectedIds = Selected();
            if (selectedIds.Length == 0) return resolved;

            if (!client.TryGetMember(member.collectionMemberId, out Member? targetMember))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(member.collectionMemberId),
                    $"No member for collection target {member.collectionMemberId}");
            }

            string? targetValueId = ResolveTargetValueId(targetMember);
            if (targetValueId is null)
            {
                throw new System.InvalidOperationException(
                    $"Lookup target {member.collectionMemberId} has no bound value");
            }
            if (!client.TryGetValue(targetValueId, out MemberValue? targetValue))
            {
                throw new System.InvalidOperationException(
                    $"Lookup target value {targetValueId} not found");
            }
            client.TryGetValueOwnership(targetValueId, out NeoValueOwnership targetOwnership);

            // The entry member defines the type of each selected
            // entry. List/Lookup → entryMemberId; Dictionary →
            // entryMemberId; Class → schema-keyed (lookup into
            // Class collections isn't currently supported).
            Member entryMember = ResolveEntryMember(targetMember);

            foreach (var id in selectedIds)
            {
                resolved.Add(targetOwnership == NeoValueOwnership.Save || targetOwnership == NeoValueOwnership.Session
                    ? CreateWritable(client, entryMember, id, targetOwnership)
                    : Create(client, entryMember, id));
            }
            return resolved;
        }

        internal bool IsSelectableId(string valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId)) return false;
            MemberValue targetValue = ResolveTargetValue(out _);
            return targetValue switch
            {
                ArrayMemberValue array when array.value is not null =>
                    System.Array.IndexOf(array.value, valueId) >= 0,
                ObjectMemberValue obj when obj.value is not null =>
                    obj.value.ContainsValue(valueId),
                _ => false,
            };
        }

        internal Member ResolveEntryMemberForLookup() =>
            ResolveEntryMember(ResolveTargetMember());

        private Member ResolveTargetMember()
        {
            if (!client.TryGetMember(member.collectionMemberId, out Member? targetMember))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(member.collectionMemberId),
                    $"No member for collection target {member.collectionMemberId}");
            }
            return targetMember;
        }

        private MemberValue ResolveTargetValue(out NeoValueOwnership targetOwnership)
        {
            Member targetMember = ResolveTargetMember();
            string? targetValueId = ResolveTargetValueId(targetMember);
            if (targetValueId is null)
            {
                throw new System.InvalidOperationException(
                    $"Lookup target {member.collectionMemberId} has no bound value");
            }
            if (!client.TryGetValue(targetValueId, out MemberValue? targetValue))
            {
                throw new System.InvalidOperationException(
                    $"Lookup target value {targetValueId} not found");
            }
            client.TryGetValueOwnership(targetValueId, out targetOwnership);
            return targetValue;
        }

        private string? ResolveTargetValueId(Member targetMember)
        {
            return client.TryResolveLookupCollectionValueId(
                targetMember.id,
                member.collectionValueId,
                out string? targetValueId)
                    ? targetValueId
                    : null;
        }

        private Member ResolveEntryMember(Member targetMember)
        {
            string entryMemberId = targetMember switch
            {
                ListMember l => l.entryMemberId,
                DictionaryMember d => d.entryMemberId,
                _ => throw new System.NotSupportedException(
                    $"Lookup target must be List or Dictionary; got {targetMember.GetType().Name}"),
            };
            if (!client.TryGetMember(entryMemberId, out Member? entryMember))
            {
                throw new System.InvalidOperationException(
                    $"Lookup entry member {entryMemberId} not found");
            }
            return entryMember;
        }
    }

    public class NeoMemberLookupWritable : NeoMemberLookup
    {
        public NeoMemberLookupWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberLookupWritable(NeoClient client, LookupMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        /// <summary>
        /// Overwrites the selected ids. When
        /// <see cref="LookupMember.multiselect"/> is false, only
        /// the first id is honored.
        /// </summary>
        public void Set(string[]? selectedIds)
        {
            if (member.required && (selectedIds is null || selectedIds.Length == 0))
            {
                throw new System.ArgumentNullException(
                    nameof(selectedIds),
                    $"Cannot be null/empty when {nameof(member)}.{nameof(member.required)} is true");
            }

            string[]? normalized = selectedIds;
            if (normalized is not null && !member.multiselect && normalized.Length > 1)
            {
                normalized = new[] { normalized[0] };
            }

            string nowIso = System.DateTime.UtcNow.ToString("o");

            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = normalized;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable);
                NotifyChanged();
                return;
            }

            ArrayMemberValue newRow = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = normalized,
            };
            BindNewValue(newRow);
            NotifyChanged();
        }

        public bool Add(string valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId))
            {
                throw new System.InvalidOperationException(
                    "Lookup selection id cannot be null or empty.");
            }
            if (!IsSelectableId(valueId))
            {
                throw new System.InvalidOperationException(
                    $"Lookup selection id '{valueId}' is not present in the configured lookup collection.");
            }
            var selected = new List<string>(Selected());
            if (selected.Contains(valueId)) return false;
            selected.Add(valueId);
            Set(selected.ToArray());
            return true;
        }

        public bool Remove(string valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId)) return false;
            var selected = new List<string>(Selected());
            bool removed = selected.Remove(valueId);
            if (!removed) return false;
            Set(selected.ToArray());
            return true;
        }

        public void Clear()
        {
            Set(System.Array.Empty<string>());
        }
    }
}
