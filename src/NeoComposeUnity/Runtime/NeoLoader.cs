// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using NeoCompose.Runtime.Json;
using Newtonsoft.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Loads Neo types
    /// </summary>
    public class NeoLoader
    {
        public NeoClient Load(string projectJson, NeoClient.LoadSave loadSave, NeoClient.HandleSave handleSave)
        {
            ProjectData data = JsonConvert.DeserializeObject<ProjectData>(projectJson);
            return new(data, loadSave, handleSave);
        }
    }
}
