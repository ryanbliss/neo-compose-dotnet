// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a `NeoVariant&lt;TTarget&gt;`-typed member (P67 §6).
    ///
    /// <para>The stored value is the atomic `{classId, variantId}` pair, so
    /// unlike a Class member there is no sub-tree to descend into: the node is
    /// a leaf, and resolving it into a usable handle is
    /// <see cref="NeoGeneratedTypesSupport.ResolveVariantValue{T}"/>'s job.
    /// </para>
    /// </summary>
    public class NeoMemberVariant
        : NeoMember<VariantMember, VariantMemberValue>
    {
        public NeoMemberVariant(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberVariant(NeoClient client, VariantMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        /// <summary>
        /// The client this node reads through.
        ///
        /// <para>Exposed so a variant member property resolves its handle from
        /// the node rather than through <c>RequireInstance()</c>. The generated
        /// `Variants` static tree has no node to start from and must go through
        /// the singleton (§7.1); a member read does have one, and inheriting a
        /// singleton-mode constraint it does not need would make variant
        /// members unusable in multi-client hosts.</para>
        /// </summary>
        internal NeoClient Client => client;
    }

    /// <summary>Writeable variant of <see cref="NeoMemberVariant"/>.</summary>
    public class NeoMemberVariantWritable : NeoMemberVariant
    {
        public NeoMemberVariantWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberVariantWritable(NeoClient client, VariantMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }
    }
}
