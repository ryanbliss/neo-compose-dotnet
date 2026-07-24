// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Class declaration. Mirrors the TS-side <c>INeoSchemaClass</c>.
    /// The <see cref="schema"/> field is the wire-form
    /// <c>Record&lt;string, string&gt;</c> mapping each schema key to
    /// the member id that backs it.
    /// </summary>
    public class NeoSchemaClass
    {
        public string id = null!;
        public string projectId = null!;
        public string name = null!;
        public Dictionary<string, string> schema = null!;
        /// <summary>
        /// Optional inheritance — when set, names another class id
        /// whose schema is extended by this class. Mirrors the TS-side
        /// <c>extendsClassId?</c>.
        /// </summary>
        public string? extendsClassId;
        /// <summary>
        /// Interfaces declared by this class. Implemented interfaces inherited
        /// through <see cref="extendsClassId"/> are not repeated here.
        /// </summary>
        public List<string>? implementsInterfaceIds;
        public bool hiddenInMemberSelector;
        public bool isAbstract;
        /// <summary>
        /// Optional system metadata emitted for protected authoring classes.
        /// Contract 3.4 identifies the protection rule with <c>kind</c>; world
        /// runtime validation additionally reads <c>worldKind</c> from this
        /// object, including through class ancestry.
        /// </summary>
        public JObject? system;
        /// <summary>
        /// Storage placement constraint (specs/member-storage.md §4.3):
        /// "immutable", "save", or "session". Absent/null means the class may be
        /// placed at any storage class. Inherited through
        /// <see cref="extendsClassId"/> (narrow-only).
        /// </summary>
        public string? allowedStorage;
        /// <summary>
        /// Ordered generic parameter declarations
        /// (specs/class-generics.md Decision 2). Absent/empty means a
        /// non-generic class.
        /// </summary>
        public List<GenericParamDeclaration>? genericParams;
        /// <summary>
        /// Present iff <see cref="extendsClassId"/> references a class with
        /// unbound generics in scope: how each parent-scope param is
        /// implemented, keyed by that param's id (Decision 4). Immutable
        /// after creation (re-binding is a migration, not an edit).
        /// </summary>
        public Dictionary<string, GenericBinding>? extendsGenericBindings;
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;
    }
}
