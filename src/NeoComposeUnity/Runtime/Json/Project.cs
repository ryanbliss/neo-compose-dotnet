// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Top-level project record. Mirrors the TS-side <c>IProject</c>
    /// (which extends <c>IProjectProps</c> + <c>IWithMongoId</c>).
    ///
    /// Date fields arrive as ISO-8601 strings on the wire (TS
    /// <c>JSON.stringify</c> emits them from <c>Date</c> automatically);
    /// keeping them as <c>string</c> here avoids Newtonsoft's
    /// timezone-edge auto-conversion. Parse with
    /// <c>System.DateTime.Parse(createdAt, CultureInfo.InvariantCulture)</c>
    /// when a typed value is needed. Wrapped in
    /// <see cref="TolerantStringConverter"/> so the deserializer
    /// gracefully skips occasional malformed dates (an empty <c>{}</c>
    /// from upstream BSON corruption, etc.) instead of failing the
    /// whole export.
    ///
    /// All fields are required on the wire (TS-side <c>IProject</c> has
    /// no optionals). The <c>= null!</c> initializer is the canonical
    /// Newtonsoft + NRT pattern: silences the "non-nullable field
    /// uninitialized" warning while preserving the type-system claim
    /// that downstream readers can rely on the field being non-null
    /// after deserialization populates it.
    /// </summary>
    public class Project
    {
        public string id = null!;
        public string _id = null!;
        public string name = null!;
        public string rootAssetsAttributeId = null!;
        public string rootSaveFileAttributeId = null!;
        public string? defaultPriorityGroupId;
        [Newtonsoft.Json.JsonConverter(typeof(TolerantStringConverter))]
        public string createdAt = null!;
        [Newtonsoft.Json.JsonConverter(typeof(TolerantStringConverter))]
        public string updatedAt = null!;
    }
}
