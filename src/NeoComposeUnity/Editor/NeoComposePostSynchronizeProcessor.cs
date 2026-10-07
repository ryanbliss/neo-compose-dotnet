// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace NeoCompose.Unity.Editor
{
    [InitializeOnLoad]
    internal static class NeoComposePostSynchronizeProcessor
    {
        // The sync owns its Tiles and RuleTiles folders under this one.
        private const string GeneratedAssetDirectory = "Assets/Neo/Generated";
        private const int MaxAttempts = 20;

        private static readonly INeoPostSynchronizeTaskPersistence Persistence =
            new NeoSessionStatePostSynchronizeTaskPersistence();
        private static readonly NeoPostSynchronizeTaskCoordinator TaskCoordinator =
            new(Persistence);
        private static readonly NeoPostSynchronizeCompletionPipeline CompletionPipeline =
            new(
                Persistence,
                TaskCoordinator,
                NeoTileGridAuthoringPreviewRefresher.RefreshBindingsAsync);

        private static CancellationTokenSource? activeCancellation;
        private static string? activeGenerationId;
        private static bool isRunning;

        static NeoComposePostSynchronizeProcessor()
        {
            var interrupted = Persistence.Load();
            if (interrupted != null)
                TaskCoordinator.RecoverInterrupted(interrupted);
            EditorApplication.delayCall += TryRunPending;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                    activeCancellation?.Cancel();
                if (state == PlayModeStateChange.EnteredEditMode)
                    EditorApplication.delayCall += TryRunPending;
            };
        }

        public static void Schedule(NeoComposeConfig config, string projectJsonPath)
        {
            string assetDatabasePath = NeoComposePathUtility.CombineAssetPath(
                config.projectJsonDirectory,
                NeoComposeEditorDefaults.AssetDatabaseFileName);

            activeCancellation?.Cancel();
            var generation = new NeoPostSynchronizeGenerationState
            {
                GenerationId = Guid.NewGuid().ToString("N"),
                ProjectId = config.projectId,
                VersionId = config.versionId,
                ProjectJsonPath = projectJsonPath,
                AssetDatabasePath = assetDatabasePath,
                GeneratedNamespace = config.namespaceForGeneratedTypes,
                Status = NeoPostSynchronizeGenerationStatus.Pending,
            };
            Persistence.Save(generation);
            EditorApplication.delayCall += TryRunPending;
        }

        private static async void TryRunPending()
        {
            if (isRunning || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            var generation = Persistence.Load();
            if (generation == null ||
                generation.Status == NeoPostSynchronizeGenerationStatus.Failed)
            {
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryRunPending;
                return;
            }

            isRunning = true;
            CancellationTokenSource? cancellation = null;

            try
            {
                if (!IsAuthoritative(generation.GenerationId))
                    return;
                if (!File.Exists(generation.ProjectJsonPath))
                {
                    throw new FileNotFoundException(
                        "The synchronized Neo Compose project export could not be found.",
                        generation.ProjectJsonPath);
                }

                NeoJsonProjectDataSource dataSource = NeoJsonProjectDataSource.FromFile(generation.ProjectJsonPath);
                ProjectData projectData = dataSource.ReadProjectData();
                Type? generatedProjectType = FindGeneratedProjectType(
                    generation.GeneratedNamespace);
                if (generatedProjectType == null)
                {
                    RetryOrFail(generation);
                    return;
                }

                generation.Status = NeoPostSynchronizeGenerationStatus.Running;
                generation.Error = null;
                Persistence.Save(generation);

                cancellation = new CancellationTokenSource();
                activeCancellation = cancellation;
                activeGenerationId = generation.GenerationId;

                using (TaskCoordinator.BeginCollection(generation))
                {
                    await RunAsync(
                        dataSource,
                        projectData,
                        generation.AssetDatabasePath,
                        generatedProjectType,
                        cancellation.Token);
                }

                await CompletionPipeline.RunAsync(
                    generation,
                    cancellation.Token,
                    descriptor => SetStatus(
                        $"Completing synchronized artifact '{descriptor.Name}' " +
                        $"(generation '{descriptor.GenerationId}', kind " +
                        $"'{descriptor.Kind}', owner value " +
                        $"'{descriptor.OwnerValueId}', attempt " +
                        $"{descriptor.Attempt}).",
                        MessageType.Info),
                    () => SetStatus(
                        $"Generated artifacts succeeded for generation " +
                        $"'{generation.GenerationId}'. Refreshing matching " +
                        "TileGrid authoring previews...",
                        MessageType.Info));

                cancellation.Token.ThrowIfCancellationRequested();
                if (!IsAuthoritative(generation.GenerationId))
                    return;
                Persistence.Clear();
                SetStatus("Neo Compose files synchronized.", MessageType.Info);
            }
            catch (OperationCanceledException)
            {
                if (IsAuthoritative(generation.GenerationId))
                {
                    generation.Status = NeoPostSynchronizeGenerationStatus.Pending;
                    generation.Error = null;
                    Persistence.Save(generation);
                }
            }
            catch (Exception exception)
            {
                // Reflection wraps lifecycle callback failures, while task and
                // preview failures already add the identifiers needed for a
                // useful terminal diagnostic. Preserve those coordinator
                // wrappers instead of unconditionally stripping their context.
                Exception diagnostic = exception is TargetInvocationException
                    ? exception.GetBaseException()
                    : exception;
                if (IsAuthoritative(generation.GenerationId))
                {
                    generation.Status = NeoPostSynchronizeGenerationStatus.Failed;
                    generation.Error = diagnostic.Message;
                    Persistence.Save(generation);
                    SetStatus(
                        "Synchronized, but post-sync validation failed: " +
                        diagnostic.Message,
                        MessageType.Error);
                }
                Debug.LogError(exception);
            }
            finally
            {
                if (activeGenerationId == generation.GenerationId)
                {
                    activeGenerationId = null;
                    activeCancellation = null;
                }
                cancellation?.Dispose();
                isRunning = false;

                var next = Persistence.Load();
                if (next != null &&
                    next.Status != NeoPostSynchronizeGenerationStatus.Failed)
                {
                    EditorApplication.delayCall += TryRunPending;
                }
            }
        }

        /// <summary>
        /// Writes the terminal status the editor window reads, then repaints any open
        /// window so the message replaces the last mid-sync progress line (which a
        /// domain reload can otherwise leave stuck).
        /// </summary>
        internal static void SetStatus(string message, MessageType severity)
        {
            SessionState.SetString(NeoComposeEditorWindow.StatusSessionKey, message);
            SessionState.SetInt(
                NeoComposeEditorWindow.StatusSeveritySessionKey,
                (int)severity);
            foreach (var window in Resources.FindObjectsOfTypeAll<NeoComposeEditorWindow>())
            {
                window.Repaint();
            }
        }

        private static async Awaitable RunAsync(
            NeoJsonProjectDataSource dataSource,
            ProjectData projectData,
            string assetDatabasePath,
            Type generatedProjectType,
            CancellationToken cancellationToken)
        {
            // Immutable animation definitions may have changed. Stop live
            // players and discard compiled clip caches before callbacks see
            // the synchronized project data.
            NeoClient.InvalidateAllAnimationClips();
            using var store = new NeoProjectStore(
                dataSource: dataSource,
                localStore: new NeoInMemoryLocalSaveStore(),
                loadUserFile: false);
            using var project = await LoadGeneratedProjectAsync(
                generatedProjectType,
                store,
                assetDatabasePath,
                cancellationToken);
            var synchronized = new HashSet<string>();
            NeoClient client = project.Client;
            SynchronizeGeneratedTileAssets(
                projectData,
                assetDatabasePath,
                client,
                project.ReadOnlyValueFactories);

            var callbackClassIds = GetSynchronizeCallbackClassIds(project.ClassIdsByType);
            if (callbackClassIds.Count == 0)
                return;
            foreach (string valueId in EnumerateProjectValueIds(projectData))
            {
                // Declaration/default rows are not necessarily constructed instances.
                // Do not materialize them merely to call the base no-op callback.
                if (!client.TryGetValue(valueId, out ObjectMemberValue? value)
                    || !callbackClassIds.Contains(
                        NeoGeneratedTypesSupport.ResolveClassValueClassId(client, valueId, value) ?? ""))
                    continue;
                object? resolved = project.ResolveValue(valueId);
                if (resolved is not NeoGeneratedClassValue classValue)
                    continue;
                string key = classValue.valueId ?? valueId;
                if (!synchronized.Add(key))
                    continue;

                InvokeOnDidSynchronize(classValue);
            }
        }

        internal static HashSet<string> GetSynchronizeCallbackClassIds(IReadOnlyDictionary<Type, string> classIds)
        {
            return classIds
                .Where(entry => entry.Key.GetMethod(nameof(NeoGeneratedClassValue.OnDidSynchronize))
                    ?.DeclaringType is Type declaringType && declaringType != typeof(NeoGeneratedClassValue))
                .Select(entry => entry.Value)
                .ToHashSet(StringComparer.Ordinal);
        }

        private static void InvokeOnDidSynchronize(NeoGeneratedClassValue classValue)
        {
            MethodInfo? method = classValue.GetType().GetMethod(
                "OnDidSynchronize",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            method?.Invoke(classValue, Array.Empty<object>());
        }

        /// <param name="generatedDirectory">The folder whose Tiles and RuleTiles the sync owns.</param>
        internal static void SynchronizeGeneratedTileAssets(
            ProjectData projectData,
            string assetDatabasePath,
            NeoClient client,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
                readOnlyFactories,
            string generatedDirectory = GeneratedAssetDirectory)
        {
            if (string.IsNullOrWhiteSpace(assetDatabasePath))
                return;
            var assetDatabase = AssetDatabase.LoadAssetAtPath<NeoAssetDatabase>(assetDatabasePath);
            if (assetDatabase == null)
                return;

            var tileClassIds = new HashSet<string>(
                EnumerateTileClassIds(projectData));
            // Old per-value mappings can share a class with its canonical asset.
            // Replace the mapping list once; retain canonical files so sync
            // refreshes them in place and preserves their Unity references.
            foreach (var old in assetDatabase.TileAssets)
            {
                if (!tileClassIds.Contains(old.TileClassId)
                    || !IsClassTileAssetPath(generatedDirectory, old.AssetPath, old.TileClassId))
                    DeleteGeneratedTileAsset(old.AssetPath);
            }
            var tileEntries = new List<NeoAssetDatabaseTileEntry>(tileClassIds.Count);

            foreach (string tileClassId in tileClassIds.OrderBy(id => id, StringComparer.Ordinal))
            {
                NeoGeneratedClassValue classValue =
                    NeoGeneratedTypesSupport.CreateReadOnlyClassDefault(
                        client,
                        tileClassId,
                        readOnlyFactories);
                var generatedTile = NeoTileAssetFactory.CreateTransientTileBase(classValue);
                if (generatedTile == null)
                {
                    DeleteAlternateGeneratedTileAsset(generatedDirectory, tileClassId, string.Empty);
                    continue;
                }

                string assetPath = GeneratedTileAssetPath(generatedDirectory, tileClassId, generatedTile);
                DeleteAlternateGeneratedTileAsset(generatedDirectory, tileClassId, assetPath);

                TileBase persistedTile = PersistGeneratedTileAsset(assetPath, generatedTile);
                tileEntries.Add(new NeoAssetDatabaseTileEntry
                {
                    TileClassId = tileClassId,
                    AssetPath = assetPath,
                    ContentHash = GeneratedTileClassContentHash(projectData, tileClassId, classValue),
                    TileBase = persistedTile,
                });
            }

            DeleteOrphanedGeneratedTileAssets(generatedDirectory, tileEntries);

            var previous = assetDatabase.TileAssets;
            if (previous.Count != tileEntries.Count || previous.Where((entry, index) =>
                    entry.TileClassId != tileEntries[index].TileClassId
                    || entry.AssetPath != tileEntries[index].AssetPath
                    || entry.ContentHash != tileEntries[index].ContentHash
                    || entry.TileBase != tileEntries[index].TileBase).Any())
            {
                assetDatabase.ReplaceTileAssets(tileEntries);
                EditorUtility.SetDirty(assetDatabase);
                AssetDatabase.SaveAssetIfDirty(assetDatabase);
            }
        }

        /// <summary>Concrete tile definitions, including classes used only by lazy placements.</summary>
        internal static IEnumerable<string> EnumerateTileClassIds(ProjectData projectData)
        {
            foreach (var type in projectData.classes.Values)
            {
                if (type.Modifier == NeoClassModifierKind.Abstract
                    || type.genericParams is { Count: > 0 })
                    continue;
                if (NeoSchemaClassInheritance.ResolveChain(
                        type.id,
                        id => projectData.classes.TryGetValue(id, out var resolved) ? resolved : null)
                    .Any(ancestor => ancestor.system?["worldKind"]?.ToString() == "tile"))
                    yield return type.id;
            }
        }

        private static string TileAssetDirectory(string generatedDirectory) => $"{generatedDirectory}/Tiles";

        private static string RuleTileAssetDirectory(string generatedDirectory) => $"{generatedDirectory}/RuleTiles";

        private static bool IsClassTileAssetPath(string generatedDirectory, string path, string classId)
        {
            string fileName = $"{SanitizeAssetFileName(classId)}.asset";
            return path == $"{TileAssetDirectory(generatedDirectory)}/{fileName}"
                || path == $"{RuleTileAssetDirectory(generatedDirectory)}/{fileName}";
        }

        private static string GeneratedTileAssetPath(string generatedDirectory, string assetId, TileBase tileBase)
        {
            string fileName = $"{SanitizeAssetFileName(assetId)}.asset";
            return tileBase is NeoRuleTile
                ? $"{RuleTileAssetDirectory(generatedDirectory)}/{fileName}"
                : $"{TileAssetDirectory(generatedDirectory)}/{fileName}";
        }

        private static void DeleteAlternateGeneratedTileAsset(string generatedDirectory, string assetId, string keepPath)
        {
            string fileName = $"{SanitizeAssetFileName(assetId)}.asset";
            foreach (var path in new[]
            {
                $"{TileAssetDirectory(generatedDirectory)}/{fileName}",
                $"{RuleTileAssetDirectory(generatedDirectory)}/{fileName}",
            })
            {
                if (path != keepPath)
                    DeleteGeneratedTileAsset(path);
            }
        }

        /// <summary>
        /// Deletes every asset in the sync's tile folders that no tile class
        /// maps to: a deleted class's, or one the database no longer lists.
        /// </summary>
        private static void DeleteOrphanedGeneratedTileAssets(
            string generatedDirectory,
            List<NeoAssetDatabaseTileEntry> tileEntries)
        {
            var kept = new HashSet<string>(tileEntries.Select(entry => entry.AssetPath), StringComparer.Ordinal);
            foreach (string directory in new[]
            {
                TileAssetDirectory(generatedDirectory),
                RuleTileAssetDirectory(generatedDirectory),
            })
            {
                if (!Directory.Exists(directory))
                    continue;
                foreach (string file in Directory.GetFiles(directory, "*.asset", SearchOption.TopDirectoryOnly))
                {
                    string path = file.Replace('\\', '/');
                    if (!kept.Contains(path))
                        DeleteGeneratedTileAsset(path);
                }
            }
        }

        private static TileBase PersistGeneratedTileAsset(string assetPath, TileBase generatedTile)
        {
            EnsureAssetDirectory(assetPath);
            // CreateAsset uses the filename. Use that name on subsequent syncs too.
            generatedTile.name = Path.GetFileNameWithoutExtension(assetPath);
            var existing = AssetDatabase.LoadAssetAtPath<TileBase>(assetPath);
            if (existing == null || existing.GetType() != generatedTile.GetType())
            {
                DeleteGeneratedTileAsset(assetPath);
                AssetDatabase.CreateAsset(generatedTile, assetPath);
                return generatedTile;
            }

            // Resolve the desired tile every time: dependencies can change without
            // changing this class's timestamp (inherited defaults, sprites, rules).
            EditorUtility.CopySerializedIfDifferent(generatedTile, existing);
            AssetDatabase.SaveAssetIfDirty(existing);
            UnityEngine.Object.DestroyImmediate(generatedTile);
            return existing;
        }

        private static void DeleteGeneratedTileAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return;
            // An asset whose script is missing doesn't load as a TileBase,
            // and it still has to go.
            if (AssetDatabase.AssetPathExists(assetPath))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static void EnsureAssetDirectory(string assetPath)
        {
            var directory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrWhiteSpace(directory))
                return;
            var normalized = directory.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(normalized))
                return;

            var segments = normalized.Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = $"{current}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }
                current = next;
            }
        }

        private static string GeneratedTileClassContentHash(
            ProjectData projectData,
            string tileClassId,
            NeoGeneratedClassValue classValue)
        {
            string updatedAt = projectData.classes.TryGetValue(tileClassId, out var row)
                ? row.updatedAt.ToString()
                : "";
            string tileKind = NeoTileAssetFactory.TryResolveSmartTile(classValue, out _)
                ? "rule"
                : "tile";
            return $"{tileClassId}:{updatedAt}:{tileKind}";
        }

        private static string SanitizeAssetFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value
                .Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch)
                .ToArray();
            var sanitized = new string(chars).Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "neo-tile" : sanitized;
        }

        internal static async Awaitable<NeoProjectClient> LoadGeneratedProjectAsync(
            Type generatedProjectType,
            NeoProjectStore store,
            string assetDatabasePath,
            CancellationToken cancellationToken = default)
        {
            NeoAssetDatabase? assetDatabase = string.IsNullOrWhiteSpace(assetDatabasePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<NeoAssetDatabase>(assetDatabasePath);
            await store.LoadAsync();
            cancellationToken.ThrowIfCancellationRequested();
            NeoSaveSynchronizer synchronizer = store.CreateNew();
            NeoClient client = await new NeoLoader().Load(
                synchronizer, assetDatabase, cancellationToken: cancellationToken);

            try
            {
                return NeoProjectClient.Construct(generatedProjectType, client);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private static IEnumerable<string> EnumerateProjectValueIds(ProjectData projectData)
        {
            if (projectData.values == null)
                yield break;
            foreach (string valueId in projectData.values.Keys.OrderBy(id => id, StringComparer.Ordinal))
            {
                yield return valueId;
            }
        }

        private static Type? FindGeneratedProjectType(string generatedNamespace)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types.Where(type => type != null).Cast<Type>().ToArray();
                }

                foreach (Type type in types)
                {
                    if (type.IsAbstract || type.IsInterface)
                        continue;
                    if (!typeof(NeoProjectClient).IsAssignableFrom(type))
                        continue;
                    if (!string.IsNullOrWhiteSpace(generatedNamespace) && type.Namespace != generatedNamespace)
                        continue;
                    return type;
                }
            }

            return null;
        }

        private static void RetryOrFail(
            NeoPostSynchronizeGenerationState generation)
        {
            generation.ProcessorAttempts += 1;
            if (generation.ProcessorAttempts >= MaxAttempts)
            {
                string message =
                    "Neo Compose post-synchronize hooks could not run because no generated project type " +
                    $"extending {nameof(NeoProjectClient)} was found in namespace " +
                    $"'{generation.GeneratedNamespace}'.";
                generation.Status = NeoPostSynchronizeGenerationStatus.Failed;
                generation.Error = message;
                Persistence.Save(generation);
                SetStatus(message, MessageType.Error);
                Debug.LogError(message);
                return;
            }

            generation.Status = NeoPostSynchronizeGenerationStatus.Pending;
            Persistence.Save(generation);
        }

        private static bool IsAuthoritative(string generationId) =>
            Persistence.Load()?.GenerationId == generationId;
    }
}
