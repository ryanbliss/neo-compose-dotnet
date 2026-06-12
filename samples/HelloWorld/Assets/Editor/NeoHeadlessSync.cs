// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Unity.Editor;
using UnityEditor;
using UnityEngine;

namespace HelloWorld.Editor
{
    /// <summary>
    /// Agent/CI affordance: runs the Neo Compose synchronize pipeline without
    /// the editor window, logging completion so external tooling (MCP, CI)
    /// can await it by watching the console.
    /// </summary>
    public static class NeoHeadlessSync
    {
        [MenuItem("Neo Compose/Headless Sync")]
        public static async void Run()
        {
            var config = NeoComposeConfigProvider.LoadOrCreate();
            var synchronizer = new NeoComposeSynchronizer(
                new NeoComposeEditorApiClient(),
                new NeoComposeEditorDialogConfirmationService(),
                new NeoComposeEditorAssetService());
            Debug.Log("[NeoHeadlessSync] starting…");
            try
            {
                var result = await synchronizer.SynchronizeAsync(config, status => Debug.Log($"[NeoHeadlessSync] {status}"));
                Debug.Log($"[NeoHeadlessSync] complete: success={result.success} {result.message}");
                AssetDatabase.Refresh();
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[NeoHeadlessSync] failed: {exception.Message}");
            }
        }
    }
}
