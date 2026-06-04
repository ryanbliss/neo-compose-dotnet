// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;

namespace NeoCompose.Runtime.Json
{
    public class ProjectExportMetadataSemver
    {
        public string label = null!;
        public int major;
        public int minor;
        public int patch;
    }

    public class ProjectExportMetadata
    {
        public int schemaVersion;
        public string projectId = null!;
        public string versionId = null!;
        public ProjectExportMetadataSemver? semver;
    }

    /// <summary>
    /// Top-level deserialization target — pass to
    /// <c>JsonConvert.DeserializeObject&lt;ProjectExport&gt;(json)</c>.
    /// Mirrors the TS-side <c>IProjectUnityExport</c> wrapper:
    /// the project record nested under <see cref="project"/>, plus four
    /// keyed-by-id payloads as <see cref="Dictionary{TKey, TValue}"/>.
    ///
    /// JSON shape:
    ///
    /// <code>
    /// {
    ///   "project":    { ... },
    ///   "attributes": { "&lt;id&gt;": { ... } },
    ///   "enums":      { "&lt;id&gt;": { ... } },
    ///   "types":      { "&lt;id&gt;": { ... } },
    ///   "values":     { "&lt;id&gt;": { ... } }
    /// }
    /// </code>
    /// </summary>
    public class ProjectData
    {
        public ProjectExportMetadata? metadata;
        public Project project = null!;
        public Dictionary<string, Attribute> attributes = null!;
        public Dictionary<string, AttributeValue> values = null!;
        public Dictionary<string, CustomType> types = null!;
        public Dictionary<string, Enum> enums = null!;
        public Dictionary<string, ProjectFile> files = new();
        public Dictionary<string, UnityTexture2DImportSettingsTemplate> textureTemplates = new();
        public Dictionary<string, UnityAudioClipImportSettingsTemplate> audioClipTemplates = new();
        public Dictionary<string, Dialogue> dialogues = new();
        public Dictionary<string, DialogueGroup> dialogueGroups = new();
        public Dictionary<string, PriorityGroup> priorityGroups = new();
        public ProjectLocalizationExport? localization;
    }
}
