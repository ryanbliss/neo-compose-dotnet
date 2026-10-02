// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using NeoCompose.Runtime;

namespace HelloWorld.Assets.Tests
{
    internal static class SampleProjectFixture
    {
        internal static readonly string Json = File.ReadAllText("Assets/Resources/Neo/project.json");
        // The source caches project-scoped schema. Tests still own separate stores,
        // saves, clients, and subscriptions; custom-schema tests use their own source.
        internal static readonly NeoJsonProjectDataSource Source = new(Json);
    }
}
