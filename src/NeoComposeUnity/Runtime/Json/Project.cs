// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Top-level project record. Mirrors the TS-side <c>IProject</c>
    /// (which extends <c>IProjectProps</c> + <c>IWithMongoId</c>).
    ///
    /// Date fields arrive as Convex epoch-millisecond numbers on the
    /// wire. They deserialize through <see cref="NeoTimestamp"/>, which
    /// also accepts legacy ISO-8601 strings so older captured fixtures
    /// and saved data continue to load.
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
        public string rootSessionAttributeId = null!;
        public string? defaultPriorityGroupId;
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;
    }
}
