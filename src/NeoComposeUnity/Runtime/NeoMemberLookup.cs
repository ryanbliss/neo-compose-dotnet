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

        /// <summary>
        /// The node last resolved for each selection slot. A kept node is
        /// reused while its id, ownership and declaration still match and the
        /// active registry still holds it, which skips composing its key.
        /// </summary>
        private NeoMember?[] selections = System.Array.Empty<NeoMember?>();

        /// <summary>Resolves the current first selection without allocating a result list.</summary>
        public NeoMember? GetFirstSelected()
        {
            string[] ids = Selected();
            if (ids.Length == 0)
                return null;
            Member entry = ResolveSelectionScope(out NeoValueOwnership targetOwnership);
            return ResolveSelectedAt(0, ids[0], entry, targetOwnership);
        }

        /// <summary>Resolves the current selections against their target collection.</summary>
        public IList<NeoMember> GetSelected()
        {
            List<NeoMember> resolved = new();
            string[] ids = Selected();
            if (ids.Length == 0)
                return resolved;
            Member entry = ResolveSelectionScope(out NeoValueOwnership targetOwnership);
            for (int i = 0; i < ids.Length; i++)
                resolved.Add(ResolveSelectedAt(i, ids[i], entry, targetOwnership));
            return resolved;
        }

        /// <summary>The entry member and ownership every selected id resolves against.</summary>
        internal Member ResolveSelectionScope(out NeoValueOwnership targetOwnership)
        {
            Member targetMember = ResolveTargetMember(client, member);
            ResolveTargetValue(client, member, targetMember, out targetOwnership);
            return ResolveEntryMember(client, targetMember);
        }

        /// <summary>Resolves selected id <paramref name="id"/> at <paramref name="index"/>.</summary>
        internal NeoMember ResolveSelectedAt(int index, string id, Member entry, NeoValueOwnership targetOwnership)
        {
            if (index >= selections.Length)
                System.Array.Resize(ref selections, System.Math.Max(index + 1, Selected().Length));
            NeoMember? kept = selections[index];
            // Reuse the composed key, but still consult the active registry:
            // candidate replay and same-key replacement must resolve their own node.
            if (kept is not null
                && kept.overrideValueId == id
                && kept.ownership == targetOwnership
                && kept.member.RuntimeDeclarationIdentity == entry.RuntimeDeclarationIdentity
                && client.TryGetNode(kept.RegistryKey, out NeoMember? current)
                && (targetOwnership == NeoValueOwnership.Asset || IsWritableCompatible(entry, current)))
            {
                return selections[index] = current;
            }
            return selections[index] = ResolveSelection(client, entry, id, targetOwnership);
        }

        /// <summary>
        /// Resolves selected id <paramref name="id"/> of
        /// <paramref name="lookup"/> as <see cref="GetFirstSelected"/> does,
        /// for a selection no lookup node holds.
        /// </summary>
        internal static NeoMember ResolveSelected(NeoClient client, LookupMember lookup, string id)
        {
            Member targetMember = ResolveTargetMember(client, lookup);
            ResolveTargetValue(client, lookup, targetMember, out NeoValueOwnership targetOwnership);
            return ResolveSelection(
                client,
                ResolveEntryMember(client, targetMember),
                id,
                targetOwnership);
        }

        private static NeoMember ResolveSelection(NeoClient client, Member entry, string id, NeoValueOwnership targetOwnership) =>
            targetOwnership == NeoValueOwnership.Save || targetOwnership == NeoValueOwnership.Session
                ? CreateWritable(client, entry, id, targetOwnership)
                : Create(client, entry, id);

        internal bool IsSelectableId(string valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId))
                return false;
            Member targetMember = ResolveTargetMember(client, member);
            MemberValue targetValue = ResolveTargetValue(client, member, targetMember, out _);
            return ResolveCollectionEntryIds(client, targetMember, targetValue).Contains(valueId);
        }

        internal static IEnumerable<string> ResolveCollectionEntryIds(NeoClient client, Member collection, MemberValue value)
        {
            if (collection is ListMember list && value is ArrayMemberValue array)
                return NeoMemberList.ResolveEntryValueIds(client, array, client.IsUnorderedList(list));
            if (value is ObjectMemberValue obj && obj.value != null)
                return obj.value.Values;
            return System.Array.Empty<string>();
        }

        private static Member ResolveTargetMember(NeoClient client, LookupMember lookup)
        {
            if (!client.TryGetMember(lookup.collectionMemberId, out Member? targetMember))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(lookup.collectionMemberId),
                    $"No member for collection target {lookup.collectionMemberId}");
            }
            return targetMember;
        }

        private static MemberValue ResolveTargetValue(
            NeoClient client,
            LookupMember lookup,
            Member targetMember,
            out NeoValueOwnership targetOwnership)
        {
            string? targetValueId = ResolveTargetValueId(client, lookup, targetMember);
            if (targetValueId is null)
            {
                throw new System.InvalidOperationException(
                    $"Lookup target {lookup.collectionMemberId} has no bound value");
            }
            NeoValueNode? node = null;
            MemberValue? targetValue = client.ReadValue(targetValueId, ref node);
            if (targetValue is null)
            {
                throw new System.InvalidOperationException(
                    $"Lookup target value {targetValueId} not found");
            }
            client.TryGetValueOwnership(targetValueId, ref node, out targetOwnership);
            return targetValue;
        }

        private static string? ResolveTargetValueId(NeoClient client, LookupMember lookup, Member targetMember)
        {
            return client.TryResolveLookupCollectionValueId(
                targetMember.id,
                lookup.CollectionValueId,
                out string? targetValueId)
                    ? targetValueId
                    : null;
        }

        private static Member ResolveEntryMember(NeoClient client, Member targetMember)
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

            NeoTimestamp nowIso = NeoTimestamp.Now();

            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = normalized;
                writable.updatedAt = nowIso;
                PublishWritableValue(writable);
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
            if (selected.Contains(valueId))
                return false;
            selected.Add(valueId);
            Set(selected.ToArray());
            return true;
        }

        public bool Remove(string valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId))
                return false;
            var selected = new List<string>(Selected());
            bool removed = selected.Remove(valueId);
            if (!removed)
                return false;
            Set(selected.ToArray());
            return true;
        }

        public void Clear()
        {
            Set(System.Array.Empty<string>());
        }
    }
}
