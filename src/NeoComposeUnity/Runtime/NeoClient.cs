// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

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

        public bool TryGetAttribute<TAttribute>(string id, out TAttribute attribute) where TAttribute : Attribute
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

        public bool TryGetType(string id, out CustomType type)
        {
            if (data.types.TryGetValue(id, out CustomType idMatch))
            {
                type = idMatch;
                return true;
            }
            type = null;
            return false;
        }

        public bool TryGetValue<TValue>(string id, out TValue value) where TValue : AttributeValue
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
        public bool TryGetSaveOverrideValueId(string attributeId, out string valueId)
        {
            return saveData.attributeValueOverrides.TryGetValue(attributeId, out valueId);
        }

        public bool TryGetEnum<TEnum>(string id, out TEnum enumInfo) where TEnum : Enum
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

        protected ProjectSaveData DeserializeSaveData(string json)
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

            if (!string.IsNullOrEmpty(saveJson))
            {
                try
                {
                    saveData = DeserializeSaveData(saveJson);
                }
                catch (System.Exception exception)
                {
                    Debug.LogError(exception);
                }
            }

            // `DeserializeSaveData` returns null on an empty / whitespace
            // string without throwing, so a successful-but-empty load
            // still needs the default-build fallback.
            if (saveData == null)
            {
                saveData = BuildDefaultSaveData();
                EmitHandleSave();
            }
        }

        protected void LoadUnsafe()
        {
            string saveJson = loadSave.Invoke();
            saveData = DeserializeSaveData(saveJson);
        }
    }
}
