// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a String-typed member. Read-only — use
    /// <see cref="NeoMemberStringWritable"/> to mutate.
    /// </summary>
    public class NeoMemberString
        : NeoMember<StringMember, StringMemberValue>
    {
        public NeoMemberString(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberString(NeoClient client, StringMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        public string? Text => ResolveText(value);

        public string? TextId
        {
            get
            {
                if (member.Format == NeoStringFormatKind.Plain) return null;
                if (value?.neoLocalizationMode == NeoStringLocalizationMode.Literal) return null;
                return value?.value;
            }
        }

        protected string? ResolveText(StringMemberValue? row)
        {
            if (row?.value == null) return null;
            if (member.Format == NeoStringFormatKind.Plain) return row.value;
            if (row.neoLocalizationMode == NeoStringLocalizationMode.Literal) return row.value;
            return client.Localization.ResolveText(row.value);
        }
    }

    /// <summary>
    /// Writeable variant of <see cref="NeoMemberString"/>.
    /// </summary>
    public class NeoMemberStringWritable : NeoMemberString
    {
        public NeoMemberStringWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberStringWritable(NeoClient client, StringMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        /// <summary>
        /// Sets the underlying string. Clone-on-writes the existing value
        /// row (shadowing the authored default at its stable id) when one
        /// is bound; otherwise mints a fresh row and binds it through the
        /// parent container.
        /// </summary>
        public void Set(string? newValue)
        {
            SetLiteralOverride(newValue);
        }

        public void SetLiteralOverride(string? newValue)
        {
            if (member.Requirement == NeoMemberRequirementKind.Required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(member)} requirement is Required");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");

            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = newValue;
                writable.neoLocalizationMode = NeoStringLocalizationMode.Literal;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable, "value");
                // No NotifyChanged() here — the write above already raised it
                // through this node's own OnValueIdChainChanged. See that
                // method's remarks.
                return;
            }

            StringMemberValue newRow = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = newValue,
                neoLocalizationMode = NeoStringLocalizationMode.Literal,
            };
            BindNewValue(newRow);
            NotifyChanged();
        }

        /// <summary>
        /// Drops this node's Save/Session shadow so it reverts to the
        /// authored default (the overlay falls through to the asset row).
        /// </summary>
        public void ClearOverride()
        {
            if (ownership == NeoValueOwnership.Asset) return;
            string? id = valueId;
            if (id is null) return;
            client.RemoveWritableShadow(ownership, id);
        }
    }
}
