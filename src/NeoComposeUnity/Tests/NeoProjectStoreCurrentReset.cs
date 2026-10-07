// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime;
using NeoCompose.Tests;
using NUnit.Framework.Interfaces;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(NeoProjectStoreCurrentReset))]

namespace NeoCompose.Tests
{
    /// <summary>
    /// Each test starts with no current store, as a play session does
    /// (P104 §4.1). Most tests never dispose their stores, so the first would
    /// otherwise own the user file for every later test, including the
    /// sample's. Run callbacks fire for every test in the run.
    /// </summary>
    public sealed class NeoProjectStoreCurrentReset : ITestRunCallback
    {
        public void RunStarted(ITest testsToRun)
        {
        }

        public void RunFinished(ITestResult testResults)
        {
        }

        public void TestStarted(ITest test)
        {
            if (!test.IsSuite)
                NeoProjectStore.ResetCurrent();
        }

        public void TestFinished(ITestResult result)
        {
        }
    }
}
