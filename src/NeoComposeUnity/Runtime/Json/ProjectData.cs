// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

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

    public class TileGridManifest
    {
        public string id = null!;
        public string gridValueId = null!;
        public int schemaVersion;
        public int regionSize;
        public string? sourceVersionId;
        public List<string> layerOrder = new();
        public List<JToken> importedAssets = new();
        public JToken? boundsHint;
        public string contentHash = null!;
    }

    public class TileGridRegion
    {
        public string id = null!;
        public string gridValueId = null!;
        public string layerId = null!;
        public string layerKind = null!;
        public string regionKey = null!;
        public int regionX;
        public int regionY;
        public int dataSchemaVersion;
        public JToken? data;
        public string contentHash = null!;
        public bool deleted;
    }

    public class TileGridCell
    {
        public int x;
        public int y;
    }

    public class TileGridLayerLinkTile
    {
        public string id = null!;
        public string tileValueId = null!;
        public string? tileTypeId;
        public TileGridCell position = new();
        public int order;
        public JToken? overrides;
    }

    public class TileGridLayerLinkPayload
    {
        public string id = null!;
        public string gridValueId = null!;
        public string objectLayerId = null!;
        public string objectInstanceId = null!;
        public string objectValueId = null!;
        public string tileLayerLinkValueId = null!;
        public string targetTileLayerId = null!;
        public TileGridCell origin = new();
        public int order;
        public List<TileGridLayerLinkTile> tiles = new();
    }

    public class TileGridContent
    {
        public int schemaVersion;
        public TileGridManifest manifest = null!;
        public List<TileGridRegion> regions = new();
        public List<TileGridLayerLinkPayload> tileLayerLinks = new();
    }

    public class TileGridRegionDeltaPayload
    {
        public Dictionary<string, JToken> entries = new();
        public List<string> removedInstanceIds = new();
        public List<string> restoredToAuthored = new();
    }

    public class TileGridRegionDelta
    {
        public string gridValueId = null!;
        public string layerId = null!;
        public string layerKind = null!;
        public string regionKey = null!;
        public int regionX;
        public int regionY;
        public int dataSchemaVersion = 1;
        public TileGridRegionDeltaPayload delta = new();
        public string contentHash = "";
    }

    public class TileGridDeltaContent
    {
        public int schemaVersion = 1;
        public List<TileGridRegionDelta> regions = new();
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
        public Dictionary<string, TileGridContent> tileGridContents = new();
        public ProjectLocalizationExport? localization;
    }
}
