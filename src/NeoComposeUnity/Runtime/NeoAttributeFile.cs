// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>Wrapper for a Sprite-typed file attribute.</summary>
    public class NeoAttributeSprite
        : NeoAttribute<SpriteAttribute, SpriteAttributeValue>
    {
        public NeoAttributeSprite(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeSprite(NeoClient client, SpriteAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

        /// <summary>
        /// Resolves the current file reference through the synchronized
        /// <see cref="NeoAssetDatabase"/>.
        /// </summary>
        public Sprite? Resolve()
        {
            return NeoAssetResolver.ResolveSprite(client.assetDatabase, value?.value);
        }
    }

    /// <summary>Writeable variant of <see cref="NeoAttributeSprite"/>.</summary>
    public class NeoAttributeSpriteWritable : NeoAttributeSprite
    {
        public NeoAttributeSpriteWritable(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeSpriteWritable(NeoClient client, SpriteAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

        public void Set(SpriteValue? newValue)
        {
            if (attribute.required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(attribute)}.{nameof(attribute.required)} is true");
            }

            string nowIso = System.DateTime.UtcNow.ToString("o");
            if (value is SpriteAttributeValue existing)
            {
                existing.value = newValue;
                existing.updatedAt = nowIso;
                client.SetWritableValue(ownership, existing);
                NotifyChanged();
                return;
            }

            string newValueId = System.Guid.NewGuid().ToString();
            SpriteAttributeValue newRow = new()
            {
                id = newValueId,
                createdAt = nowIso,
                updatedAt = nowIso,
                value = newValue,
            };
            client.AddWritableValue(ownership, attribute.id, newRow);
            RefreshFromValueData();
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
                        attribute.templateId,
                        attribute.name));
        }
    }

    /// <summary>Wrapper for an AudioClip-typed file attribute.</summary>
    public class NeoAttributeAudio
        : NeoAttribute<AudioAttribute, FileAttributeValue>
    {
        public NeoAttributeAudio(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeAudio(NeoClient client, AudioAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

        /// <summary>
        /// Resolves the current file reference through the synchronized
        /// <see cref="NeoAssetDatabase"/>.
        /// </summary>
        public AudioClip? Resolve()
        {
            return NeoAssetResolver.ResolveAudioClip(client.assetDatabase, value?.value);
        }
    }

    /// <summary>Writeable variant of <see cref="NeoAttributeAudio"/>.</summary>
    public class NeoAttributeAudioWritable : NeoAttributeAudio
    {
        public NeoAttributeAudioWritable(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeAudioWritable(NeoClient client, AudioAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

        public void Set(FileValue? newValue)
        {
            if (attribute.required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(attribute)}.{nameof(attribute.required)} is true");
            }

            string nowIso = System.DateTime.UtcNow.ToString("o");
            if (value is FileAttributeValue existing)
            {
                existing.value = newValue;
                existing.updatedAt = nowIso;
                client.SetWritableValue(ownership, existing);
                NotifyChanged();
                return;
            }

            string newValueId = System.Guid.NewGuid().ToString();
            FileAttributeValue newRow = new()
            {
                id = newValueId,
                createdAt = nowIso,
                updatedAt = nowIso,
                value = newValue,
            };
            client.AddWritableValue(ownership, attribute.id, newRow);
            RefreshFromValueData();
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
                        attribute.templateId,
                        attribute.name));
        }
    }
}
