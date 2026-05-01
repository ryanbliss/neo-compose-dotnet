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
            try
            {
                string saveJson = loadSave.Invoke();
                try
                {
                    saveData = DeserializeSaveData(saveJson);
                }
                catch (System.Exception exception)
                {
                    Debug.LogError(exception);
                }
            }
            catch (System.Exception)
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
