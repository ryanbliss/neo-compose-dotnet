// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Indexes derived only from a <see cref="ProjectData"/>'s authored rows
    /// (P104 §4.2). Every client of that instance shares one, so a user
    /// client and the save clients after it build them once. Partition
    /// loads and unloads update them in place, as they update the shared rows.
    /// </summary>
    internal sealed class NeoAuthoredIndexes
    {
        internal readonly Dictionary<string, HashSet<string>> entriesByContainer = new();
        internal readonly Dictionary<string, string> containerByRow = new();
        internal readonly Dictionary<string, MemberValue> listenerRoots = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, NeoValueOwnership> ownership = new();
        internal readonly Dictionary<string, NeoValueOwnership> storageRoots = new();
        internal bool membershipBuilt;
        internal bool ownershipBuilt;
        internal int ownershipEpoch;
    }
}
