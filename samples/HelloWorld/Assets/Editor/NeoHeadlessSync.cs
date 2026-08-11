// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Unity.Editor;
using UnityEditor;

namespace HelloWorld.Editor
{
    /// <summary>
    /// In-editor affordance for the package's headless synchronize pipeline
    /// (<see cref="NeoComposeBatchSync"/>): runs the Neo Compose synchronize
    /// pipeline without the editor window or any dialog, logging progress and
    /// completion so external tooling (MCP, CI) can await it by watching the
    /// console.
    /// </summary>
    /// <remarks>
    /// Batch runs should call the package entry point directly —
    /// <c>-executeMethod NeoCompose.Unity.Editor.NeoComposeBatchSync.Run</c> —
    /// which additionally exits the editor with the run's status. This menu item
    /// deliberately uses the non-exiting variant.
    /// </remarks>
    public static class NeoHeadlessSync
    {
        [MenuItem("Neo Compose/Headless Sync")]
        public static void Run()
        {
            NeoComposeBatchSync.RunWithoutExit();
        }
    }
}
