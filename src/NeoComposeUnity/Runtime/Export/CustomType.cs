// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace NeoCompose.Export
{
    /// <summary>
    /// Custom type declaration. Mirrors the TS-side <c>ICustomType</c>.
    /// The <see cref="schema"/> field is the wire-form
    /// <c>Record&lt;string, string&gt;</c> mapping each schema key to
    /// the attribute id that backs it.
    /// </summary>
    public class CustomType
    {
        public string id;
        public string _id;
        public string projectId;
        public string name;
        public Dictionary<string, string> schema;
        public string extendsTypeId;
        public bool hiddenInAttributeSelector;
        public bool isAbstract;
        [Newtonsoft.Json.JsonConverter(typeof(TolerantStringConverter))]
        public string createdAt;
        [Newtonsoft.Json.JsonConverter(typeof(TolerantStringConverter))]
        public string updatedAt;
    }
}
