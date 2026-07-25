// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>Runtime wrapper for a FunctionRef value row.</summary>
    public sealed class NeoMemberFunctionRef
        : NeoMember<FunctionRefMember, ObjectMemberValue>
    {
        public NeoMemberFunctionRef(
            NeoClient client,
            string memberId,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberFunctionRef(
            NeoClient client,
            FunctionRefMember member,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        /// <summary>The referenced schema member id, or null for an empty row.</summary>
        public string? FunctionMemberId
        {
            get
            {
                if (value?.value is null) return null;
                return value.value.TryGetValue("functionMemberId", out string id)
                    ? id
                    : null;
            }
        }
    }
}
