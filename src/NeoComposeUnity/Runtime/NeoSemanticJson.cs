// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Linq;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
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

        // Runtime replay compares thousands of already typed rows. Keep the
        // JSON fallback for uncommon payloads while avoiding serialization of
        // ordinary class maps, collections and scalar leaves.
        internal static bool MemberRowsEqual(MemberValue? left, MemberValue? right, bool ignoreObjectFields = false)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            if (left.GetType() != right.GetType() || left.init is not null || right.init is not null)
                return ProjectRecordsEqual(JObject.FromObject(left), JObject.FromObject(right));
            if (left.id != right.id || left.classId != right.classId || left.mark != right.mark
                || left.containerId != right.containerId || left.mapKey != right.mapKey
                || left.sourceValueId != right.sourceValueId
                || left.hasInstanceConstructorId != right.hasInstanceConstructorId
                || left.instanceConstructorId != right.instanceConstructorId
                || left.instanceVariantId != right.instanceVariantId
                || left.instanceVariantRowValueId != right.instanceVariantRowValueId
                || !MapsEqual(left.genericBindings, right.genericBindings)
                || !MapsEqual(left.constructorArgs, right.constructorArgs)) return false;
            return left switch
            {
                ObjectMemberValue a => ignoreObjectFields || MapsEqual(a.value, ((ObjectMemberValue)right).value),
                ArrayMemberValue a => ArraysEqual(a.value, ((ArrayMemberValue)right).value),
                NumberMemberValue a => a.value == ((NumberMemberValue)right).value,
                BoolMemberValue a => a.value == ((BoolMemberValue)right).value,
                StringMemberValue a => a.value == ((StringMemberValue)right).value
                    && a.neoLocalizationMode == ((StringMemberValue)right).neoLocalizationMode,
                Vector2MemberValue a => VectorEqual(a.value, ((Vector2MemberValue)right).value),
                Vector3MemberValue a => VectorEqual(a.value, ((Vector3MemberValue)right).value),
                ColorMemberValue a => ColorEqual(a.value, ((ColorMemberValue)right).value),
                DelegateMemberValue a when IsMemberTargetOrNull(a.value)
                    && IsMemberTargetOrNull(((DelegateMemberValue)right).value) =>
                    DelegateEqual(a.value, ((DelegateMemberValue)right).value),
                ActionMemberValue a => ActionEqual(a.value, ((ActionMemberValue)right).value),
                FileMemberValue a => ReferenceEquals(a.value, ((FileMemberValue)right).value)
                    || a.value is not null && ((FileMemberValue)right).value is { } file && a.value.fileId == file.fileId,
                SpriteMemberValue a => a.value?.fileId == ((SpriteMemberValue)right).value?.fileId
                    && a.value?.sliceIndex == ((SpriteMemberValue)right).value?.sliceIndex,
                _ => ProjectRecordsEqual(JObject.FromObject(left), JObject.FromObject(right)),
            };
        }

        private static bool IsMemberTargetOrNull(NeoDelegateValue? value) => value is null || value.IsMemberTarget;

        private static bool DelegateEqual(NeoDelegateValue? left, NeoDelegateValue? right) =>
            ReferenceEquals(left, right) || (left is not null && right is not null
                && left.memberId == right.memberId && left.valueId == right.valueId);

        private static bool ActionEqual(NeoActionValue? left, NeoActionValue? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null || left.listeners.Count != right.listeners.Count) return false;
            for (int i = 0; i < left.listeners.Count; i++)
                if (!DelegateEqual(left.listeners[i], right.listeners[i])) return false;
            return true;
        }

        private static bool ArraysEqual(string[]? left, string[]? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        internal static bool MapsEqual<T>(Dictionary<string, T>? left, Dictionary<string, T>? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null || left.Count != right.Count) return false;
            foreach (var pair in left)
                if (!right.TryGetValue(pair.Key, out var value)
                    || (pair.Value is JToken token ? !ReplayTokensEqual(token, value as JToken)
                        : !EqualityComparer<T>.Default.Equals(pair.Value, value))) return false;
            return true;
        }

        private static bool ReplayTokensEqual(JToken left, JToken? right)
        {
            if (right is null) return false;
            if (left.Type != right.Type
                && left.Type is JTokenType.Integer or JTokenType.Float
                && right.Type is JTokenType.Integer or JTokenType.Float)
            {
                // NeoScript numbers round-trip through both integer and
                // floating JSON tokens. Compare exact representable integers;
                // large JSON integers keep the conservative typed comparison.
                double integer = (left.Type == JTokenType.Integer ? left : right).Value<double>();
                return Math.Abs(integer) <= 9007199254740991d && left.Value<double>() == right.Value<double>();
            }
            return JToken.DeepEquals(left, right);
        }

        private static bool VectorEqual(NeoVector2Value? left, NeoVector2Value? right) =>
            ReferenceEquals(left, right) || (left is not null && right is not null
                && left.GetType() == right.GetType() && left.x == right.x && left.y == right.y
                && (left is not NeoVector3Value a || right is NeoVector3Value b && a.z == b.z));

        private static bool ColorEqual(NeoColorValue? left, NeoColorValue? right) =>
            ReferenceEquals(left, right) || (left is not null && right is not null
                && left.r == right.r && left.g == right.g && left.b == right.b && left.a == right.a);

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
