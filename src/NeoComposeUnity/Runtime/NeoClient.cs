// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using NeoCompose.Runtime.Json;

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
        protected LoadSave loadSave;
        protected HandleSave handleSave;

        public NeoClient(ProjectData data, LoadSave loadSave, HandleSave handleSave)
        {
            this.data = data;
            this.loadSave = loadSave;
            this.handleSave = handleSave;
            LoadOrCreate();
        }

        protected string BuildDefaultSaveData()
        {
            return "";
        }

        protected void LoadOrCreate()
        {
            try
            {
                string saveData = loadSave.Invoke();
            }
            catch (System.Exception)
            {
                handleSave.Invoke(BuildDefaultSaveData());
            }
        }
    }
}
