// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Scene binding for edit-mode previews of a synchronized Neo TileGrid.
    /// The ids are persisted so editor tooling can target a specific
    /// project/version/grid; the preview itself is resolved from the synchronized
    /// project JSON and generated C# wrappers in the Unity project.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class NeoTileGridAuthoringBinding : MonoBehaviour
    {
        public string projectId = "";
        public string versionId = "";
        public string memberId = "";
        public string valueId = "";
        public new NeoTileGridRenderer? renderer;
        public bool refreshOnEnable = true;

        private NeoProjectStore? previewStore;
        private IDisposable? previewProject;
        private bool refreshInFlight;

        private void OnEnable()
        {
            if (!refreshOnEnable) return;
            RefreshPreview();
        }

        private void OnDisable()
        {
            DisposePreview();
        }

        [ContextMenu("Refresh Neo TileGrid Preview")]
        public void RefreshPreview()
        {
            if (refreshInFlight) return;
            RefreshPreviewAsync();
        }

        public async void RefreshPreviewAsync()
        {
            refreshInFlight = true;
            try
            {
                DisposePreview();
                if (string.IsNullOrWhiteSpace(valueId))
                {
                    Debug.LogWarning(
                        "[NeoCompose] TileGrid authoring binding has no valueId.",
                        this);
                    return;
                }

                var targetRenderer = renderer ?? GetComponent<NeoTileGridRenderer>();
                if (targetRenderer == null)
                {
                    targetRenderer = gameObject.AddComponent<NeoTileGridRenderer>();
                    renderer = targetRenderer;
                }

                var assetDatabase = NeoAssetDatabase.LoadDefault();
                targetRenderer.AssetDatabase = assetDatabase;

                previewStore = new NeoProjectStore();
                await previewStore.LoadAsync();
                var synchronizer = previewStore.CreateNew();
                var client = await new NeoLoader().Load(synchronizer, assetDatabase);
                var generatedProjectType = FindGeneratedProjectType();
                if (generatedProjectType == null)
                {
                    client.Dispose();
                    throw new InvalidOperationException(
                        "Could not find the generated Neo project type. Synchronize " +
                        "Neo Compose first so generated C# wrappers are available.");
                }

                previewProject = ConstructGeneratedProject(generatedProjectType, client);
                object content = ResolveGeneratedGridContent(generatedProjectType, previewProject, valueId);
                targetRenderer.Render(content);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[NeoCompose] Could not refresh TileGrid authoring preview: " +
                    exception.Message,
                    this);
            }
            finally
            {
                refreshInFlight = false;
            }
        }

        private void DisposePreview()
        {
            previewProject?.Dispose();
            previewProject = null;
            previewStore?.Dispose();
            previewStore = null;
        }

        private static Type? FindGeneratedProjectType()
        {
            var configuredNamespace = NeoComposeConfig.LoadDefault()?.namespaceForGeneratedTypes;
            var candidates = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(SafeGetTypes)
                .Where(type =>
                    typeof(INeoClient).IsAssignableFrom(type) &&
                    type.GetMethod(
                        "ResolveDialogueValue",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null)
                .ToArray();
            if (!string.IsNullOrWhiteSpace(configuredNamespace))
            {
                var configured = candidates.FirstOrDefault(type =>
                    type.Namespace == configuredNamespace ||
                    (type.FullName?.StartsWith(
                        configuredNamespace + ".",
                        StringComparison.Ordinal) ?? false));
                if (configured != null) return configured;
            }

            return candidates.FirstOrDefault();
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null).Cast<Type>().ToArray();
            }
        }

        private static IDisposable ConstructGeneratedProject(
            Type generatedProjectType,
            NeoClient client)
        {
            var constructor = generatedProjectType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(NeoClient), typeof(NeoDialogueRuntimeOptions) },
                modifiers: null);
            if (constructor != null)
            {
                return (IDisposable)constructor.Invoke(new object?[] { client, null });
            }

            constructor = generatedProjectType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(NeoClient) },
                modifiers: null);
            if (constructor != null)
            {
                return (IDisposable)constructor.Invoke(new object[] { client });
            }

            client.Dispose();
            throw new MissingMethodException(
                generatedProjectType.FullName,
                ".ctor(NeoClient, NeoDialogueRuntimeOptions)");
        }

        private static object ResolveGeneratedGridContent(
            Type generatedProjectType,
            IDisposable generatedProject,
            string gridValueId)
        {
            var resolveMethod = generatedProjectType.GetMethod(
                    "ResolveDialogueValue",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(
                    generatedProjectType.FullName,
                    "ResolveDialogueValue");
            var grid = resolveMethod.Invoke(generatedProject, new object[] { gridValueId })
                ?? throw new InvalidOperationException(
                    $"Generated project could not resolve TileGrid value '{gridValueId}'.");
            var content = grid.GetType()
                .GetProperty("Content", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(grid);
            if (content == null)
            {
                throw new InvalidOperationException(
                    $"Generated value '{gridValueId}' does not expose TileGrid Content.");
            }
            return content;
        }
    }
}
