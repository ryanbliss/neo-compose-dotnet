// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    public class NeoComposeRuntimeKeyConfigTests
    {
        // UAUTH-051 / UAUTH-052 / UAUTH-054
        [Test]
        public void RuntimeApiKey_IsOptionalAndRoundTrips()
        {
            var config = ScriptableObject.CreateInstance<NeoComposeConfig>();
            try
            {
                // Optional: absent by default and not required to use a project.
                Assert.AreEqual("", config.projectRuntimeApiKey);
                Assert.IsFalse(config.HasProject);

                config.projectId = "project-1";
                Assert.IsTrue(config.HasProject, "A missing runtime key must not block project use.");

                config.projectRuntimeApiKey = "rk_live_example";
                Assert.AreEqual("rk_live_example", config.projectRuntimeApiKey);

                // Clearing the project must not touch the project-wide runtime key.
                config.ClearProject();
                Assert.AreEqual("rk_live_example", config.projectRuntimeApiKey);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
