// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;

namespace NeoCompose.Tests.ClassRootReference420
{
    // Register only the two generated fixture classes; system wrappers are not needed by these tests.
    internal sealed class TestProjectNeo : NeoProjectClient
    {
        private const string ItemId = "fce9939b-d55d-41b8-9fca-6e4bab83b67a";
        private const string HolderId = "aaf02d5a-18b3-48c4-b91b-5370c03c3734";
        private static TestProjectNeo instance = null!;

        internal static TestProjectNeo RequireInstance() => instance;

        internal TestProjectNeo(NeoClient client) : base(client,
            new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [ItemId] = Item.Create,
                [HolderId] = RefHolder.Create,
            },
            new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
            {
                [ItemId] = Item.CreateWritable,
                [HolderId] = RefHolder.CreateWritable,
            },
            new Dictionary<Type, string> { [typeof(Item)] = ItemId, [typeof(RefHolder)] = HolderId })
        {
            instance = this;
        }
    }
}
