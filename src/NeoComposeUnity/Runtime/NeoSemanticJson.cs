// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Canonical semantic JSON comparisons shared by save batching and the
    /// client commit boundary. Convex owns the four top-level record metadata
    /// fields below; changing only those fields must never make a client write.
    /// Nested fields with the same names remain authored domain data.
    /// </summary>
    internal static class NeoSemanticJson
    {
        private static readonly string[] ServerManagedRecordFields =
        {
            "_id",
            "projectId",
            "createdAt",
            "updatedAt",
        };

        internal static bool ProjectRecordsEqual(JToken? left, JToken? right) =>
            JToken.DeepEquals(ProjectRecord(left), ProjectRecord(right));

        internal static bool ValuesEqual(JToken? left, JToken? right) =>
            JToken.DeepEquals(
                Canonicalize(left ?? JValue.CreateNull()),
                Canonicalize(right ?? JValue.CreateNull()));

        internal static JToken ProjectRecord(JToken? value)
        {
            if (value is not JObject record)
            {
                return Canonicalize(value ?? JValue.CreateNull());
            }

            var semantic = new JObject();
            foreach (var property in record.Properties()
                         .Where(property => !ServerManagedRecordFields.Contains(property.Name))
                         .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                semantic[property.Name] = Canonicalize(property.Value);
            }
            return semantic;
        }

        /// <summary>
        /// Canonicalizes a save envelope while treating both its own server
        /// metadata and each immediate value row's server metadata as volatile.
        /// The row's nested <c>value</c> payload is canonicalized without removing
        /// anything, so a nested <c>updatedAt</c> remains semantic.
        /// </summary>
        internal static JToken SaveEnvelope(JToken? value)
        {
            if (value is not JObject envelope)
            {
                return Canonicalize(value ?? JValue.CreateNull());
            }

            var semantic = new JObject();
            foreach (var property in envelope.Properties()
                         .Where(property => !ServerManagedRecordFields.Contains(property.Name))
                         .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                if (property.Name == "values" && property.Value is JObject values)
                {
                    var semanticValues = new JObject();
                    foreach (var row in values.Properties()
                                 .OrderBy(row => row.Name, StringComparer.Ordinal))
                    {
                        semanticValues[row.Name] = ProjectRecord(row.Value);
                    }
                    semantic[property.Name] = semanticValues;
                    continue;
                }
                semantic[property.Name] = Canonicalize(property.Value);
            }
            return semantic;
        }

        internal static JToken Canonicalize(JToken value)
        {
            if (value is JObject obj)
            {
                var canonical = new JObject();
                foreach (var property in obj.Properties()
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    canonical[property.Name] = Canonicalize(property.Value);
                }
                return canonical;
            }
            if (value is JArray array)
            {
                return new JArray(array.Select(Canonicalize));
            }
            return value.DeepClone();
        }
    }
}
