// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>Wrapper for a Sprite-typed file member.</summary>
    public class NeoMemberSprite
        : NeoMember<SpriteMember, SpriteMemberValue>
    {
        public NeoMemberSprite(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberSprite(NeoClient client, SpriteMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        /// <summary>
        /// Resolves the current file reference through the synchronized
        /// <see cref="NeoAssetDatabase"/>.
        /// </summary>
        public Sprite? Resolve()
        {
            return NeoAssetResolver.ResolveSprite(client.assetDatabase, value?.value);
        }
    }

    /// <summary>Writeable variant of <see cref="NeoMemberSprite"/>.</summary>
    public class NeoMemberSpriteWritable : NeoMemberSprite
    {
        public NeoMemberSpriteWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberSpriteWritable(NeoClient client, SpriteMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        public void Set(SpriteValue? newValue)
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
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable, "value");
                // No NotifyChanged() here — the write above already raised it
                // through this node's own OnValueIdChainChanged. See that
                // method's remarks.
                return;
            }

            SpriteMemberValue newRow = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = newValue,
            };
            BindNewValue(newRow);
            NotifyChanged();
        }

        public void Set(Sprite? sprite)
        {
            Set(
                sprite == null
                    ? null
                    : NeoAssetResolver.ValueForSprite(
                        client.assetDatabase,
                        sprite,
                        member.templateId,
                        member.name));
        }
    }

    /// <summary>Wrapper for an AudioClip-typed file member.</summary>
    public class NeoMemberAudio
        : NeoMember<AudioMember, FileMemberValue>
    {
        public NeoMemberAudio(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberAudio(NeoClient client, AudioMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        /// <summary>
        /// Resolves the current file reference through the synchronized
        /// <see cref="NeoAssetDatabase"/>.
        /// </summary>
        public AudioClip? Resolve()
        {
            return NeoAssetResolver.ResolveAudioClip(client.assetDatabase, value?.value);
        }
    }

    /// <summary>Writeable variant of <see cref="NeoMemberAudio"/>.</summary>
    public class NeoMemberAudioWritable : NeoMemberAudio
    {
        public NeoMemberAudioWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberAudioWritable(NeoClient client, AudioMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        public void Set(FileValue? newValue)
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
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable, "value");
                // No NotifyChanged() here — the write above already raised it
                // through this node's own OnValueIdChainChanged. See that
                // method's remarks.
                return;
            }

            FileMemberValue newRow = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = newValue,
            };
            BindNewValue(newRow);
            NotifyChanged();
        }

        public void Set(AudioClip? audioClip)
        {
            Set(
                audioClip == null
                    ? null
                    : NeoAssetResolver.ValueForAudioClip(
                        client.assetDatabase,
                        audioClip,
                        member.templateId,
                        member.name));
        }
    }
}
