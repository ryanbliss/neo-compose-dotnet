// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a Lookup-typed member. Stores the selected ids
    /// (in the target collection) as a string-array value. The target
    /// collection is the member named by
    /// <see cref="LookupMember.collectionMemberId"/>; the target
    /// value is either <see cref="LookupMember.CollectionValueId"/>
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

        private NeoMember? firstSelection;

        /// <summary>Resolves the current first selection without allocating a result list.</summary>
        public NeoMember? GetFirstSelected()
        {
            string[] ids = Selected();
            if (ids.Length == 0)
            {
                firstSelection = null;
                return null;
            }
            ResolveTargetValue(out NeoValueOwnership targetOwnership);
            Member entry = ResolveEntryMemberForLookup();
            // Reuse the composed key, but still consult the active registry:
            // candidate replay and same-key replacement must resolve their own node.
            if (firstSelection is not null
                && firstSelection.overrideValueId == ids[0]
                && firstSelection.ownership == targetOwnership
                && firstSelection.member.RuntimeDeclarationIdentity == entry.RuntimeDeclarationIdentity
                && client.TryGetNode(firstSelection.RegistryKey, out NeoMember? current)
                && (targetOwnership == NeoValueOwnership.Asset || IsWritableCompatible(entry, current)))
            {
                firstSelection = current;
                return current;
            }
            firstSelection = ResolveSelection(entry, ids[0], targetOwnership);
            return firstSelection;
        }

        /// <summary>Resolves the current selections against their target collection.</summary>
        public IList<NeoMember> GetSelected()
        {
            List<NeoMember> resolved = new();
            string[] ids = Selected();
            if (ids.Length == 0) return resolved;
            ResolveTargetValue(out NeoValueOwnership targetOwnership);
            Member entry = ResolveEntryMemberForLookup();
            foreach (string id in ids)
                resolved.Add(ResolveSelection(entry, id, targetOwnership));
            return resolved;
        }

        private NeoMember ResolveSelection(Member entry, string id, NeoValueOwnership targetOwnership) =>
            targetOwnership == NeoValueOwnership.Save || targetOwnership == NeoValueOwnership.Session
                ? CreateWritable(client, entry, id, targetOwnership)
                : Create(client, entry, id);

        internal bool IsSelectableId(string valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId)) return false;
            MemberValue targetValue = ResolveTargetValue(out _);
            return ResolveCollectionEntryIds(client, ResolveTargetMember(), targetValue).Contains(valueId);
        }

        internal static IEnumerable<string> ResolveCollectionEntryIds(NeoClient client, Member collection, MemberValue value)
        {
            if (collection is ListMember list && value is ArrayMemberValue array)
                return NeoMemberList.ResolveEntryValueIds(client, array, client.IsUnorderedList(list));
            if (value is ObjectMemberValue obj && obj.value != null) return obj.value.Values;
            return System.Array.Empty<string>();
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
                member.CollectionValueId,
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
        /// <see cref="LookupMember.Selection == NeoMemberSelectionKind.Multi"/> is false, only
        /// the first id is honored.
        /// </summary>
        public void Set(string[]? selectedIds)
        {
            if (member.Requirement == NeoMemberRequirementKind.Required && (selectedIds is null || selectedIds.Length == 0))
            {
                throw new System.ArgumentNullException(
                    nameof(selectedIds),
                    $"Cannot be null/empty when {nameof(member)} requirement is Required");
            }

            string[]? normalized = selectedIds;
            if (normalized is not null && member.Selection != NeoMemberSelectionKind.Multi && normalized.Length > 1)
            {
                normalized = new[] { normalized[0] };
            }

            string nowIso = System.DateTime.UtcNow.ToString("o");

            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = normalized;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable, "value");
                // No NotifyChanged() here — the write above already raised it
                // through this node's own OnValueIdChainChanged. See that
                // method's remarks.
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
