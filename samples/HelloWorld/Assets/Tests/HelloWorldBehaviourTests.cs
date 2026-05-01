// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using NeoCompose;
using NUnit.Framework;
using UnityEngine;

namespace HelloWorld.Tests
{
    public class HelloWorldBehaviourTests
    {
        [Test]
        public void Behaviour_StartInstantiatesNeoComposeCore()
        {
            // Verifies the sample can reference + use the package's surface.
            // Direct invocation of `Start` via SendMessage so we don't need
            // a frame to tick.
            var go = new GameObject("HelloWorld");
            try
            {
                var behaviour = go.AddComponent<HelloWorldBehaviour>();
                behaviour.SendMessage("Start", SendMessageOptions.DontRequireReceiver);
                Assert.IsNotNull(behaviour);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NeoComposeCore_IsReachableFromSample()
        {
            // Smoke check that the sample's asmdef references resolve.
            var instance = new NeoComposeCore();
            Assert.IsNotNull(instance);
        }
    }
}
