// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Project version captured by a save file.
    /// </summary>
    public class VersionData
    {
        /// <summary>
        /// Stable project-version id. This is the value code should use to
        /// identify which exported project version the save was created
        /// against.
        /// </summary>
        public string id = null!;

        /// <summary>
        /// Human-readable version label copied into the save for inspection
        /// and debugging. It is intentionally secondary to <see cref="id"/>.
        /// </summary>
        public string label = null!;
    }

    /// <summary>
    /// Save file envelope for runtime-owned values.
    ///
    /// <para>The authored export in <see cref="ProjectData"/> remains the
    /// immutable asset/default graph. A save file only stores values that
    /// runtime code has created or changed, then uses
    /// <see cref="attributeValueOverrides"/> to point stable attribute ids at
    /// those writable value rows.</para>
    /// </summary>
    public class ProjectSaveData
    {
        /// <summary>
        /// Stable project id for the authored project this save belongs to.
        /// </summary>
        public string projectId = null!;

        /// <summary>
        /// Stable version id plus a readable label for the project export
        /// this save was created against.
        /// </summary>
        public VersionData version = null!;

        /// <summary>
        /// Time the save file was first created, serialized as Convex-style
        /// epoch milliseconds through <see cref="NeoTimestamp"/>.
        /// </summary>
        public NeoTimestamp createdAt;

        /// <summary>
        /// Time the save file was last emitted through the save callback.
        /// Loading or serializing the save does not update this value.
        /// </summary>
        public NeoTimestamp updatedAt;

        /// <summary>
        /// Runtime-owned value rows keyed by value id. These rows shadow or
        /// extend the authored value graph without mutating
        /// <see cref="ProjectData.values"/>.
        /// </summary>
        public Dictionary<string, AttributeValue> values = null!;

        /// <summary>
        /// Maps authored attribute ids to the current writable value row id
        /// for that attribute.
        ///
        /// <para>Resolution checks this map before falling back to the
        /// authored <c>Attribute.valueId</c>, so it is the top-level bridge
        /// from stable schema attributes to save/session values. It is not
        /// localization-specific; localization string overrides are just one
        /// kind of value that can be represented here.</para>
        /// </summary>
        public Dictionary<string, string> attributeValueOverrides = null!;
    }
}
