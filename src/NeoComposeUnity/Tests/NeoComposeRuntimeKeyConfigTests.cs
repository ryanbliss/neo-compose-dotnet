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
        public void RuntimeApiKey_LivesInSecretAsset_NotTheCommittedConfig()
        {
            // The key is no longer a field on the committed config — it moved to the
            // separate, gitignored secret asset so it never lands in source control.
            var secret = ScriptableObject.CreateInstance<NeoComposeRuntimeSecret>();
            try
            {
                Assert.AreEqual("", secret.RuntimeApiKey, "Defaults to empty (optional).");

                secret.RuntimeApiKey = "ncrk_live_example";
                Assert.AreEqual("ncrk_live_example", secret.RuntimeApiKey);

                // A missing runtime key must never block using a project.
                var config = ScriptableObject.CreateInstance<NeoComposeConfig>();
                try
                {
                    config.projectId = "project-1";
                    Assert.IsTrue(config.HasProject);
                }
                finally
                {
                    Object.DestroyImmediate(config);
                }
            }
            finally
            {
                Object.DestroyImmediate(secret);
            }
        }
    }
}
