// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Payload envelope for generated code that needs to create a new
    /// save-side value row while also carrying metadata that does not live
    /// inside the raw value payload, especially Class value <c>classId</c>.
    /// </summary>
    public sealed class NeoValuePayload
    {
        public object? value
        {
            get;
        }
        public string? classId
        {
            get;
        }
        public IReadOnlyList<MemberValue> valueRows
        {
            get;
        }

        public NeoValuePayload(
            object? value,
            string? classId = null,
            IReadOnlyList<MemberValue>? valueRows = null)
        {
            this.value = value;
            this.classId = classId;
            this.valueRows = valueRows ?? new List<MemberValue>();
        }

        /// <summary>The held type of a box for an object field (P127 §1.3); null for any other payload.</summary>
        internal TypeBinding? valueType
        {
            get;
            private set;
        }

        /// <summary>The member a box's <see cref="value"/> stores as; null for <c>new object()</c>.</summary>
        internal Member? heldMember
        {
            get;
            private set;
        }

        /// <summary>
        /// The C# object the box was written from, which its row reads as
        /// (§1.2); null when it reads as a new view over its row.
        /// </summary>
        internal object? origin
        {
            get;
            private set;
        }

        /// <summary>A <c>Type</c>, <c>CultureInfo</c> or <c>NumberFormatInfo</c> slot's payload, read back as <paramref name="origin"/>.</summary>
        internal static NeoValuePayload Declared(JObject value, object origin) =>
            new(value)
            {
                origin = origin,
            };

        internal static NeoValuePayload Boxed(
            object? value,
            TypeBinding valueType,
            Member? heldMember,
            IReadOnlyList<MemberValue> valueRows,
            object? origin) =>
            new(value, valueRows: valueRows)
            {
                valueType = valueType,
                heldMember = heldMember,
                origin = origin,
            };
    }
}
