// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using Newtonsoft.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Loads Neo types
    /// </summary>
    public class NeoLoader
    {
        public NeoClient Load(
            string projectJson,
            NeoClient.LoadSave loadSave,
            NeoClient.HandleSave handleSave,
            NeoAssetDatabase? assetDatabase = null)
        {
            ProjectData data = JsonConvert.DeserializeObject<ProjectData>(projectJson)
                ?? throw new System.InvalidOperationException("Neo Compose project JSON could not be deserialized.");
            return new(data, loadSave, handleSave, assetDatabase ?? NeoAssetDatabase.LoadDefault());
        }
    }
}
