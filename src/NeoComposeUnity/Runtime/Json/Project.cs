// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

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
    /// </summary>
    public class Project
    {
        public string id;
        public string _id;
        public string name;
        public string rootAssetsAttributeId;
        public string rootSaveFileAttributeId;
        [Newtonsoft.Json.JsonConverter(typeof(TolerantStringConverter))]
        public string createdAt;
        [Newtonsoft.Json.JsonConverter(typeof(TolerantStringConverter))]
        public string updatedAt;
    }
}
