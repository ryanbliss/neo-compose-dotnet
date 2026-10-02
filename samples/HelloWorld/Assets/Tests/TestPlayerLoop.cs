// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using NUnit.Framework.Interfaces;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(HelloWorld.Assets.Tests.TestPlayerLoop))]

namespace HelloWorld.Assets.Tests
{
    // EditMode awaits use Unity's synchronization context. Request player-loop
    // updates while tests run so continuations do not wait for an idle scene.
    public sealed class TestPlayerLoop : ITestRunCallback
    {
        private static bool running;

        public void RunStarted(ITest testsToRun) => Start();

        private static void Start()
        {
            if (running)
                return;
            running = true;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        public void RunFinished(ITestResult testResults) => Stop();
        // EnterPlayMode tests can reload the domain mid-run. Reattach when the
        // next test starts, without persisting settings outside this test run.
        public void TestStarted(ITest test) => Start();
        public void TestFinished(ITestResult result)
        {
        }

        private static void Update()
        {
            if (!Application.isPlaying)
                EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void Stop()
        {
            running = false;
            EditorApplication.update -= Update;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.quitting -= Stop;
        }
    }
}
