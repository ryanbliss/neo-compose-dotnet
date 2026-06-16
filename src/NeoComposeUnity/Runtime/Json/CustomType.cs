// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Custom type declaration. Mirrors the TS-side <c>ICustomType</c>.
    /// The <see cref="schema"/> field is the wire-form
    /// <c>Record&lt;string, string&gt;</c> mapping each schema key to
    /// the attribute id that backs it.
    /// </summary>
    public class CustomType
    {
        public string id = null!;
        public string projectId = null!;
        public string name = null!;
        public Dictionary<string, string> schema = null!;
        /// <summary>
        /// Optional inheritance — when set, names another custom-type id
        /// whose schema is extended by this type. Mirrors the TS-side
        /// <c>extendsTypeId?</c>.
        /// </summary>
        public string? extendsTypeId;
        public bool hiddenInAttributeSelector;
        public bool isAbstract;
        /// <summary>
        /// Storage placement constraint (specs/attribute-storage.md §4.3):
        /// "static", "save", or "session". Absent/null means the type may be
        /// placed at any storage class. Inherited through
        /// <see cref="extendsTypeId"/> (narrow-only).
        /// </summary>
        public string? allowedStorage;
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;
    }
}
