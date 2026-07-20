// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// The completed result of an authoring preview refresh. The content is
    /// borrowed from the binding and remains valid only until the next refresh,
    /// clear, disable, or destruction of that binding.
    /// </summary>
    public sealed class NeoTileGridPreviewResult
    {
        internal NeoTileGridPreviewResult(
            string valueId,
            NeoTileGridRenderer renderer,
            INeoTileGridContent content)
        {
            ValueId = valueId ?? throw new ArgumentNullException(nameof(valueId));
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }

        public string ValueId { get; }
        public NeoTileGridRenderer Renderer { get; }
        public INeoTileGridContent Content { get; }
    }

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
        public NeoTileGridRenderer? renderer;
        public bool refreshOnEnable = true;

        private NeoProjectStore? previewStore;
        private IDisposable? previewProject;
        private NeoTileGridPreviewResult? currentPreview;
        private CancellationTokenSource? activeRefreshCancellation;
        private long refreshGeneration;

        public event Action<NeoTileGridPreviewResult>? PreviewRendered;

        private void OnEnable()
        {
            if (!refreshOnEnable) return;
            RefreshPreview();
        }

        private void OnDisable()
        {
            ClearPreview();
        }

        private void OnDestroy()
        {
            ClearPreview();
        }

        [ContextMenu("Refresh Neo TileGrid Preview")]
        public async void RefreshPreview()
        {
            try
            {
                await RefreshPreviewAsync();
            }
            catch (OperationCanceledException)
            {
                // A newer refresh, disable, destruction, or caller cancellation
                // intentionally superseded this context-menu request.
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[NeoCompose] Could not refresh TileGrid authoring preview " +
                    $"'{valueId}': {exception.Message}",
                    this);
            }
        }

        /// <summary>
        /// Rebuilds this binding's preview. A newer call supersedes an older call;
        /// only the current generation may paint or publish completion.
        /// </summary>
        public async Awaitable<NeoTileGridPreviewResult> RefreshPreviewAsync(
            CancellationToken cancellationToken = default)
        {
            CancellationTokenSource refreshCancellation = BeginRefresh(cancellationToken);
            long generation = refreshGeneration;
            NeoProjectStore? nextStore = null;
            IDisposable? nextProject = null;
            NeoClient? nextClient = null;
            NeoTileGridRenderer? targetRenderer = null;
            bool published = false;
            try
            {
                if (string.IsNullOrWhiteSpace(valueId))
                {
                    throw new InvalidOperationException(
                        "TileGrid authoring binding cannot refresh without a valueId.");
                }

                ThrowIfRefreshIsStale(generation, refreshCancellation);
                targetRenderer = GetOrCreateRenderer();

                // Keep refresh observably asynchronous even when Resources and the
                // in-memory store complete synchronously. This gives a newer refresh,
                // disable, or caller cancellation a reliable supersession boundary
                // before any generated content is resolved or painted.
                await Awaitable.NextFrameAsync(refreshCancellation.Token);
                ThrowIfRefreshIsStale(generation, refreshCancellation);

                var assetDatabase = NeoAssetDatabase.LoadDefault();
                targetRenderer.AssetDatabase = assetDatabase;

                var config = NeoComposeConfig.LoadDefault()
                    ?? throw new InvalidOperationException(
                        "TileGrid authoring preview needs a NeoComposeConfig in Resources.");
                nextStore = new NeoProjectStore(
                    dataSource: NeoResourcesProjectDataSource.FromConfig(config),
                    localStore: new NeoInMemoryLocalSaveStore());
                await nextStore.LoadAsync();
                ThrowIfRefreshIsStale(generation, refreshCancellation);

                var synchronizer = nextStore.CreateNew();
                nextClient = await new NeoLoader().Load(synchronizer, assetDatabase);
                ThrowIfRefreshIsStale(generation, refreshCancellation);

                var generatedProjectType = FindGeneratedProjectType();
                if (generatedProjectType == null)
                {
                    throw new InvalidOperationException(
                        "Could not find the generated Neo project type. Synchronize " +
                        "Neo Compose first so generated C# wrappers are available.");
                }

                nextProject = ConstructGeneratedProject(generatedProjectType, nextClient);
                nextClient = null;
                INeoTileGridContent content = ResolveGeneratedGridContent(
                    generatedProjectType,
                    nextProject,
                    valueId);
                ThrowIfRefreshIsStale(generation, refreshCancellation);

                await targetRenderer.RenderAsync(
                    content,
                    new NeoTileGridRenderOptions
                    {
                        CancellationToken = refreshCancellation.Token,
                    });
                ThrowIfRefreshIsStale(generation, refreshCancellation);

                var result = new NeoTileGridPreviewResult(
                    valueId,
                    targetRenderer,
                    content);
                previewStore = nextStore;
                previewProject = nextProject;
                currentPreview = result;
                nextStore = null;
                nextProject = null;
                published = true;
                PreviewRendered?.Invoke(result);
                return result;
            }
            finally
            {
                try
                {
                    if (!published && OwnsRefresh(generation, refreshCancellation))
                    {
                        activeRefreshCancellation = null;
                        refreshGeneration += 1;
                        StopAndClearRenderer(targetRenderer);
                        currentPreview = null;
                    }
                }
                finally
                {
                    try
                    {
                        DisposePendingResources(nextClient, nextProject, nextStore);
                    }
                    finally
                    {
                        try
                        {
                            if (ReferenceEquals(
                                activeRefreshCancellation,
                                refreshCancellation))
                            {
                                activeRefreshCancellation = null;
                            }
                        }
                        finally
                        {
                            refreshCancellation.Dispose();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Cancels any active refresh and releases the painted preview. Safe to
        /// call repeatedly.
        /// </summary>
        public void ClearPreview()
        {
            var cancellation = activeRefreshCancellation;
            activeRefreshCancellation = null;
            cancellation?.Cancel();
            refreshGeneration += 1;

            var targetRenderer = renderer ?? GetComponent<NeoTileGridRenderer>();
            try
            {
                StopAndClearRenderer(targetRenderer);
            }
            finally
            {
                DisposePreviewResources();
            }
        }

        private CancellationTokenSource BeginRefresh(CancellationToken cancellationToken)
        {
            ClearPreview();
            var refreshCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            activeRefreshCancellation = refreshCancellation;
            return refreshCancellation;
        }

        private NeoTileGridRenderer GetOrCreateRenderer()
        {
            var targetRenderer = renderer ?? GetComponent<NeoTileGridRenderer>();
            if (targetRenderer != null) return targetRenderer;
            targetRenderer = gameObject.AddComponent<NeoTileGridRenderer>();
            renderer = targetRenderer;
            return targetRenderer;
        }

        private bool IsCurrentRefresh(
            long generation,
            CancellationTokenSource cancellation)
        {
            return OwnsRefresh(generation, cancellation) &&
                !cancellation.IsCancellationRequested;
        }

        private bool OwnsRefresh(
            long generation,
            CancellationTokenSource cancellation)
        {
            return generation == refreshGeneration &&
                ReferenceEquals(activeRefreshCancellation, cancellation);
        }

        private void ThrowIfRefreshIsStale(
            long generation,
            CancellationTokenSource cancellation)
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentRefresh(generation, cancellation))
            {
                throw new OperationCanceledException(cancellation.Token);
            }
        }

        private static void StopAndClearRenderer(NeoTileGridRenderer? targetRenderer)
        {
            if (targetRenderer == null) return;
            targetRenderer.StopLiveSync();
            targetRenderer.Clear();
        }

        private void DisposePreviewResources()
        {
            var project = previewProject;
            previewProject = null;
            var store = previewStore;
            previewStore = null;
            currentPreview = null;
            try
            {
                project?.Dispose();
            }
            finally
            {
                store?.Dispose();
            }
        }

        private static void DisposePendingResources(
            NeoClient? client,
            IDisposable? project,
            NeoProjectStore? store)
        {
            try
            {
                client?.Dispose();
            }
            finally
            {
                try
                {
                    project?.Dispose();
                }
                finally
                {
                    store?.Dispose();
                }
            }
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

            throw new MissingMethodException(
                generatedProjectType.FullName,
                ".ctor(NeoClient, NeoDialogueRuntimeOptions)");
        }

        private static INeoTileGridContent ResolveGeneratedGridContent(
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
            object? content = grid.GetType()
                .GetInterfaces()
                .Select(type => type.GetProperty(
                    "Content",
                    BindingFlags.Instance | BindingFlags.Public))
                .Where(property => property != null)
                .Where(property => typeof(INeoTileGridContent).IsAssignableFrom(
                    property!.PropertyType))
                .Select(property => property!.GetValue(grid))
                .FirstOrDefault(value => value is INeoTileGridContent);
            content ??= grid.GetType()
                .GetProperty("Content", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(grid);
            if (content is not INeoTileGridContent tileGridContent)
            {
                throw new InvalidOperationException(
                    $"Generated value '{gridValueId}' does not expose INeoTileGridContent.");
            }
            return tileGridContent;
        }
    }
}
