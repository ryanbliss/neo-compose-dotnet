// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// NeoClient owns a live save file instance.
    /// </summary>
    public class NeoClient
    {
        public delegate string LoadSave();
        public delegate void HandleSave(string content);
        public NeoAttributeCustom assets { get; protected set; }
        public NeoAttributeCustomSaved save { get; protected set; }

        /// <summary>
        /// Flat registry of every constructed <see cref="NeoAttribute"/>,
        /// keyed by <see cref="MakeNodeKey"/>. Each
        /// <see cref="NeoAttribute"/> registers itself at the end of
        /// construction so consumers (and the
        /// <see cref="NeoAttribute.Create"/> /
        /// <see cref="NeoAttribute.CreateSaved"/> factories) can look up
        /// existing instances and reuse them rather than constructing
        /// duplicates that share the same wire identity.
        /// </summary>
        public IReadOnlyDictionary<string, NeoAttribute> nodes => nodesInternal;
        private readonly Dictionary<string, NeoAttribute> nodesInternal = new();

        protected ProjectData data;
        protected ProjectSaveData saveData;
        protected LoadSave loadSave;
        protected HandleSave handleSave;

        public NeoClient(ProjectData data, LoadSave loadSave, HandleSave handleSave)
        {
            this.data = data;
            this.loadSave = loadSave;
            this.handleSave = handleSave;
            LoadOrCreateSafe();
            assets = new(this, data.project.rootAssetsAttributeId, null);
            save = new(this, data.project.rootSaveFileAttributeId, null);
        }

        public bool TryGetAttribute<TAttribute>(string id, [NotNullWhen(true)] out TAttribute? attribute) where TAttribute : Attribute
        {
            if (data.attributes.TryGetValue(id, out Attribute idMatch))
            {
                if (idMatch is TAttribute match)
                {
                    attribute = match;
                    return true;
                }
            }
            attribute = null;
            return false;
        }

        public bool TryGetType(string id, [NotNullWhen(true)] out CustomType? type)
        {
            if (data.types.TryGetValue(id, out CustomType idMatch))
            {
                type = idMatch;
                return true;
            }
            type = null;
            return false;
        }

        public bool TryGetValue<TValue>(string id, [NotNullWhen(true)] out TValue? value) where TValue : AttributeValue
        {
            if (saveData.values.TryGetValue(id, out AttributeValue saveIdMatch))
            {
                if (saveIdMatch is TValue match)
                {
                    value = match;
                    return true;
                }
            }
            if (data.values.TryGetValue(id, out AttributeValue idMatch))
            {
                if (idMatch is TValue match)
                {
                    value = match;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public void AddSaveValue<TAttributeValue>(string attributeId, TAttributeValue value) where TAttributeValue : AttributeValue
        {
            saveData.attributeValueOverrides[attributeId] = value.id;
            SetSaveValue(value);
        }

        public void SetSaveValue<TAttributeValue>(TAttributeValue value) where TAttributeValue : AttributeValue
        {
            saveData.values[value.id] = value;
        }

        /// <summary>
        /// Returns the save's override value-id for the given attribute,
        /// or null if no override is registered. Used by
        /// <see cref="NeoAttribute"/> to resolve `valueId` after a Set
        /// creates a new top-level save row.
        /// </summary>
        public bool TryGetSaveOverrideValueId(string attributeId, [NotNullWhen(true)] out string? valueId)
        {
            return saveData.attributeValueOverrides.TryGetValue(attributeId, out valueId);
        }

        /// <summary>
        /// Looks up a previously-registered <see cref="NeoAttribute"/>
        /// by attribute id (and optional override-value id). Returns
        /// false when nothing is registered for the composed key.
        /// Callers typically reach for
        /// <see cref="NeoAttribute.Create"/> /
        /// <see cref="NeoAttribute.CreateSaved"/> instead — those check
        /// here automatically before constructing.
        /// </summary>
        public bool TryGetNode(string attributeId, string? overrideValueId, [NotNullWhen(true)] out NeoAttribute? node)
        {
            string key = MakeNodeKey(attributeId, overrideValueId);
            return nodesInternal.TryGetValue(key, out node);
        }

        /// <summary>
        /// Adds <paramref name="node"/> to the flat registry under the
        /// composed key. Called by the <see cref="NeoAttribute"/>
        /// base ctor at the end of construction; callers shouldn't need
        /// to call directly. Last-write-wins — direct
        /// <c>new NeoAttributeXyz(…)</c> construction overrides any
        /// previously-cached instance for the same key.
        /// </summary>
        public void RegisterNode(NeoAttribute node, string? overrideValueId)
        {
            string key = MakeNodeKey(node.attribute.id, overrideValueId);
            nodesInternal[key] = node;
        }

        /// <summary>
        /// Composes the registry key from an attribute id and an
        /// optional override-value id. Format mirrors the user-facing
        /// spec:
        ///   - <c>attributeId</c> when no override
        ///   - <c>$"{attributeId}_{overrideValueId}"</c> when an override is set
        /// </summary>
        public static string MakeNodeKey(string attributeId, string? overrideValueId)
        {
            return string.IsNullOrEmpty(overrideValueId)
                ? attributeId
                : $"{attributeId}_{overrideValueId}";
        }

        public bool TryGetEnum<TEnum>(string id, [NotNullWhen(true)] out TEnum? enumInfo) where TEnum : Enum
        {
            if (data.enums.TryGetValue(id, out Enum idMatch))
            {
                if (idMatch is TEnum match)
                {
                    enumInfo = match;
                    return true;
                }
            }
            enumInfo = null;
            return false;
        }

        protected ProjectSaveData BuildDefaultSaveData()
        {
            ProjectSaveData empty = new()
            {
                projectId = data.project.id,
                version = "0.0.0",
                // leave `values` and `attributeValueOverrides` empty until value(s) are set at runtime
                values = new(),
                attributeValueOverrides = new(),
            };
            return empty;
        }

        protected ProjectSaveData? DeserializeSaveData(string json)
        {
            return JsonConvert.DeserializeObject<ProjectSaveData>(json);
        }

        protected string SerializeSaveData()
        {
            return JsonConvert.SerializeObject(saveData);
        }

        protected void EmitHandleSave()
        {
            handleSave.Invoke(SerializeSaveData());
            LoadUnsafe();
        }

        [MemberNotNull(nameof(saveData))]
        protected void LoadOrCreateSafe()
        {
            string saveJson = "";
            try
            {
                saveJson = loadSave.Invoke();
            }
            catch (System.Exception)
            {
                // Host couldn't supply a save file at all — fall through to default.
            }

            ProjectSaveData? parsed = null;
            if (!string.IsNullOrEmpty(saveJson))
            {
                try
                {
                    parsed = DeserializeSaveData(saveJson);
                }
                catch (System.Exception exception)
                {
                    Debug.LogError(exception);
                }
            }

            // `DeserializeSaveData` returns null on an empty / whitespace
            // string without throwing, so a successful-but-empty load
            // still needs the default-build fallback.
            if (parsed == null)
            {
                saveData = BuildDefaultSaveData();
                EmitHandleSave();
            }
            else
            {
                saveData = parsed;
            }
        }

        protected void LoadUnsafe()
        {
            string saveJson = loadSave.Invoke();
            ProjectSaveData? parsed = DeserializeSaveData(saveJson);
            if (parsed != null) saveData = parsed;
        }
    }
}
