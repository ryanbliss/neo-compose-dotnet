// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// One value id's rows across the client's stores, found with one lookup
    /// instead of one per store. The client refreshes the writable and
    /// virtual rows wherever those stores change. Authored rows live in a
    /// <see cref="ProjectData"/> that sibling clients share, so the node
    /// re-reads its authored row whenever that map's epoch moves.
    /// </summary>
    internal sealed class NeoValueNode
    {
        internal readonly string id;
        internal MemberValue? session;
        internal MemberValue? save;
        internal MemberValue? virtualRow;
        internal NeoValueOwnership virtualOwnership;
        /// <summary>False once the client dropped the node; a holder resolves the id again.</summary>
        internal bool live = true;
        private MemberValue? asset;
        private int assetEpoch = -1;

        internal NeoValueNode(string id)
        {
            this.id = id;
        }

        internal MemberValue? Asset(ProjectData data)
        {
            if (assetEpoch != data.valuesEpoch)
            {
                data.values.TryGetValue(id, out asset);
                assetEpoch = data.valuesEpoch;
            }
            return asset;
        }
    }
}
