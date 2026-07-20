// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using UnityEngine;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Test-only stand-in for an integration-owned generated Unity asset.
    /// It deliberately contains no navigation or game-specific types.
    /// </summary>
    public sealed class FixtureNavigationArtifact : ScriptableObject
    {
        public string OwnerValueId = "";
        public string ContentHash = "";
        public int Revision;
    }
}
