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
        /// <summary>
        /// Interfaces declared by this type. Implemented interfaces inherited
        /// through <see cref="extendsTypeId"/> are not repeated here.
        /// </summary>
        public List<string>? implementsInterfaceIds;
        public bool hiddenInAttributeSelector;
        public bool isAbstract;
        /// <summary>
        /// Storage placement constraint (specs/attribute-storage.md §4.3):
        /// "static", "save", or "session". Absent/null means the type may be
        /// placed at any storage class. Inherited through
        /// <see cref="extendsTypeId"/> (narrow-only).
        /// </summary>
        public string? allowedStorage;
        /// <summary>
        /// Ordered generic parameter declarations
        /// (specs/custom-type-generics.md Decision 2). Absent/empty means a
        /// non-generic type — nullable-tolerant for stale local
        /// <c>project.json</c> files, same semantics as the web model.
        /// </summary>
        public List<GenericParamDeclaration>? genericParams;
        /// <summary>
        /// Present iff <see cref="extendsTypeId"/> references a type with
        /// unbound generics in scope: how each parent-scope param is
        /// implemented, keyed by that param's id (Decision 4). Immutable
        /// after creation (re-binding is a migration, not an edit).
        /// </summary>
        public Dictionary<string, GenericBinding>? extendsGenericBindings;
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;
    }
}
