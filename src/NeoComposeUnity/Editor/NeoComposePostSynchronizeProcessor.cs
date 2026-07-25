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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace NeoCompose.Unity.Editor
{
    [InitializeOnLoad]
    internal static class NeoComposePostSynchronizeProcessor
    {
        private const string GeneratedTileAssetDirectory = "Assets/Neo/Generated/Tiles";
        private const string GeneratedRuleTileAssetDirectory = "Assets/Neo/Generated/RuleTiles";
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
            if (interrupted != null) TaskCoordinator.RecoverInterrupted(interrupted);
            EditorApplication.delayCall += TryRunPending;
        }

        public static void Schedule(NeoComposeConfig config, string projectJsonPath)
        {
            string generatedTypesPath = NeoComposePathUtility.CombineAssetPath(
                config.generatedTypesDirectory,
                NeoComposeEditorDefaults.GeneratedTypesFileName);
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
                GeneratedTypesPath = generatedTypesPath,
                AssetDatabasePath = assetDatabasePath,
                GeneratedNamespace = config.namespaceForGeneratedTypes,
                Status = NeoPostSynchronizeGenerationStatus.Pending,
            };
            Persistence.Save(generation);

            AssetDatabase.ImportAsset(projectJsonPath);
            AssetDatabase.ImportAsset(generatedTypesPath);
            AssetDatabase.Refresh();
            EditorApplication.delayCall += TryRunPending;
        }

        private static async void TryRunPending()
        {
            if (isRunning) return;
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
                if (!IsAuthoritative(generation.GenerationId)) return;
                if (!File.Exists(generation.ProjectJsonPath))
                {
                    throw new FileNotFoundException(
                        "The synchronized Neo Compose project export could not be found.",
                        generation.ProjectJsonPath);
                }

                string projectJson = File.ReadAllText(generation.ProjectJsonPath);
                ProjectData projectData = JsonConvert.DeserializeObject<ProjectData>(projectJson)
                    ?? throw new InvalidOperationException("Neo Compose project JSON could not be deserialized after synchronization.");
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
                    Run(
                        projectJson,
                        projectData,
                        generation.AssetDatabasePath,
                        generatedProjectType);
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
                if (!IsAuthoritative(generation.GenerationId)) return;
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
        private static void SetStatus(string message, MessageType severity)
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

        private static void Run(
            string projectJson,
            ProjectData projectData,
            string assetDatabasePath,
            Type generatedProjectType)
        {
            // Immutable animation definitions may have changed. Stop live
            // players and discard compiled clip caches before callbacks see
            // the synchronized project data.
            NeoClient.InvalidateAllAnimationClips();
            using var project = LoadGeneratedProject(
                generatedProjectType,
                projectJson,
                assetDatabasePath);
            MethodInfo resolveMethod = generatedProjectType.GetMethod(
                    "ResolveDialogueValue",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(
                    generatedProjectType.FullName,
                    "ResolveDialogueValue");

            var synchronized = new HashSet<string>();
            var readOnlyFactories = GetGeneratedReadOnlyFactories(generatedProjectType);
            var client = GetGeneratedClient(generatedProjectType, project);
            SynchronizeGeneratedTileAssets(
                projectData,
                assetDatabasePath,
                project,
                resolveMethod,
                client,
                readOnlyFactories);

            foreach (string valueId in EnumerateProjectValueIds(projectData))
            {
                object? resolved = resolveMethod.Invoke(project, new object[] { valueId });
                if (resolved is not NeoGeneratedClassValue classValue) continue;
                string key = classValue.valueId ?? valueId;
                if (!synchronized.Add(key)) continue;

                InvokeOnDidSynchronize(classValue);
            }
        }

        private static void InvokeOnDidSynchronize(NeoGeneratedClassValue classValue)
        {
            MethodInfo? method = classValue.GetType().GetMethod(
                "OnDidSynchronize",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            method?.Invoke(classValue, Array.Empty<object>());
        }

        private static void SynchronizeGeneratedTileAssets(
            ProjectData projectData,
            string assetDatabasePath,
            IDisposable project,
            MethodInfo resolveMethod,
            NeoClient client,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
                readOnlyFactories)
        {
            if (string.IsNullOrWhiteSpace(assetDatabasePath)) return;
            var assetDatabase = AssetDatabase.LoadAssetAtPath<NeoAssetDatabase>(assetDatabasePath);
            if (assetDatabase == null) return;

            var assetValueIds = new HashSet<string>(
                EnumerateReferencedTileAssetValueIds(projectData));
            foreach (var stale in assetDatabase.FindMissingTileAssets(assetValueIds))
            {
                DeleteGeneratedTileAsset(stale.AssetPath);
                assetDatabase.RemoveTileAsset(stale.AssetValueId);
            }

            foreach (string assetValueId in assetValueIds.OrderBy(id => id, StringComparer.Ordinal))
            {
                object? resolved = resolveMethod.Invoke(project, new object[] { assetValueId });
                if (resolved is not NeoGeneratedClassValue classValue) continue;
                var generatedTile = NeoTileAssetFactory.CreateTransientTileBase(classValue);
                if (generatedTile == null) continue;

                string assetPath = GeneratedTileAssetPath(assetValueId, generatedTile);
                var existingEntry = assetDatabase.TryGetTileEntry(assetValueId);
                if (existingEntry != null &&
                    !string.IsNullOrWhiteSpace(existingEntry.AssetPath) &&
                    existingEntry.AssetPath != assetPath)
                {
                    DeleteGeneratedTileAsset(existingEntry.AssetPath);
                }
                DeleteAlternateGeneratedTileAsset(assetValueId, assetPath);

                TileBase persistedTile = PersistGeneratedTileAsset(assetPath, generatedTile);
                assetDatabase.SetTileAsset(
                    assetValueId,
                    classValue.classId,
                    assetPath,
                    GeneratedTileContentHash(projectData, assetValueId, classValue),
                    persistedTile);
            }

            var tileClassIds = new HashSet<string>(
                EnumerateReferencedTileClassIds(projectData));
            foreach (var stale in assetDatabase.FindMissingTileClassAssets(tileClassIds))
            {
                DeleteGeneratedTileAsset(stale.AssetPath);
                assetDatabase.RemoveTileClassAsset(stale.TileClassId);
            }

            foreach (string tileClassId in tileClassIds.OrderBy(id => id, StringComparer.Ordinal))
            {
                NeoGeneratedClassValue classValue =
                    NeoGeneratedTypesSupport.CreateReadOnlyClassDefault(
                        client,
                        tileClassId,
                        readOnlyFactories);
                var generatedTile = NeoTileAssetFactory.CreateTransientTileBase(classValue);
                if (generatedTile == null) continue;

                string assetPath = GeneratedTileAssetPath(tileClassId, generatedTile);
                var existingEntry = assetDatabase.TryGetTileEntryForClass(tileClassId);
                if (existingEntry != null
                    && !string.IsNullOrWhiteSpace(existingEntry.AssetPath)
                    && existingEntry.AssetPath != assetPath)
                {
                    DeleteGeneratedTileAsset(existingEntry.AssetPath);
                }
                DeleteAlternateGeneratedTileAsset(tileClassId, assetPath);

                TileBase persistedTile = PersistGeneratedTileAsset(assetPath, generatedTile);
                assetDatabase.SetTileClassAsset(
                    tileClassId,
                    assetPath,
                    GeneratedTileClassContentHash(projectData, tileClassId, classValue),
                    persistedTile);
            }

            EditorUtility.SetDirty(assetDatabase);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Optional asset override value ids referenced by class-backed tile
        /// placements. Default-backed placements are cached separately by
        /// their tile class id.
        /// </summary>
        internal static IEnumerable<string> EnumerateReferencedTileAssetValueIds(
            ProjectData projectData)
        {
            var seen = new HashSet<string>();
            foreach (var row in projectData.values.Values)
            {
                if (row is not ObjectMemberValue placement) continue;
                if (string.IsNullOrEmpty(placement.containerId)) continue;
                if (string.IsNullOrEmpty(placement.classId)) continue;
                if (placement.value == null) continue;
                if (!IsClassBackedTilePlacement(placement.value)) continue;
                string? assetValueId = ReadDirectReference(
                    placement.value,
                    "assetValueId");
                if (!string.IsNullOrWhiteSpace(assetValueId)
                    && seen.Add(assetValueId!))
                {
                    yield return assetValueId!;
                }
            }
        }

        internal static IEnumerable<string> EnumerateReferencedTileClassIds(
            ProjectData projectData)
        {
            var seen = new HashSet<string>();
            foreach (var row in projectData.values.Values)
            {
                if (row is not ObjectMemberValue placement) continue;
                if (string.IsNullOrEmpty(placement.containerId)) continue;
                if (placement.value == null || !IsClassBackedTilePlacement(placement.value))
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(
                    ReadDirectReference(placement.value, "assetValueId")))
                {
                    continue;
                }
                string? classId = ReadDirectReference(placement.value, "assetClassId");
                if (!string.IsNullOrWhiteSpace(classId) && seen.Add(classId!))
                {
                    yield return classId!;
                }
            }
        }

        private static bool IsClassBackedTilePlacement(
            IReadOnlyDictionary<string, string> value)
        {
            return ReadDirectReference(value, "assetClassId") != null
                && ReadDirectReference(value, "Cell") != null;
        }

        private static string? ReadDirectReference(
            IReadOnlyDictionary<string, string> value,
            string key)
        {
            foreach (var pair in value)
            {
                if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                return string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value;
            }
            return null;
        }

        private static string GeneratedTileAssetPath(string assetId, TileBase tileBase)
        {
            string fileName = $"{SanitizeAssetFileName(assetId)}.asset";
            return tileBase is NeoRuleTile
                ? $"{GeneratedRuleTileAssetDirectory}/{fileName}"
                : $"{GeneratedTileAssetDirectory}/{fileName}";
        }

        private static void DeleteAlternateGeneratedTileAsset(string assetId, string keepPath)
        {
            string fileName = $"{SanitizeAssetFileName(assetId)}.asset";
            foreach (var path in new[]
            {
                $"{GeneratedTileAssetDirectory}/{fileName}",
                $"{GeneratedRuleTileAssetDirectory}/{fileName}",
            })
            {
                if (path != keepPath) DeleteGeneratedTileAsset(path);
            }
        }

        private static TileBase PersistGeneratedTileAsset(string assetPath, TileBase generatedTile)
        {
            EnsureAssetDirectory(assetPath);
            var existing = AssetDatabase.LoadAssetAtPath<TileBase>(assetPath);
            if (existing == null || existing.GetType() != generatedTile.GetType())
            {
                DeleteGeneratedTileAsset(assetPath);
                AssetDatabase.CreateAsset(generatedTile, assetPath);
                return generatedTile;
            }

            EditorUtility.CopySerialized(generatedTile, existing);
            existing.name = generatedTile.name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(generatedTile);
            return existing;
        }

        private static void DeleteGeneratedTileAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return;
            if (AssetDatabase.LoadAssetAtPath<TileBase>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static void EnsureAssetDirectory(string assetPath)
        {
            var directory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrWhiteSpace(directory)) return;
            var normalized = directory.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(normalized)) return;

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

        private static string GeneratedTileContentHash(
            ProjectData projectData,
            string assetValueId,
            NeoGeneratedClassValue classValue)
        {
            string updatedAt = projectData.values.TryGetValue(assetValueId, out var row)
                ? row.updatedAt.ToString()
                : "";
            string tileKind = NeoTileAssetFactory.TryResolveSmartTile(classValue, out _)
                ? "rule"
                : "tile";
            return $"{classValue.classId ?? ""}:{updatedAt}:{tileKind}";
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

        private static NeoClient GetGeneratedClient(
            Type generatedProjectType,
            IDisposable project)
        {
            PropertyInfo property = generatedProjectType.GetProperty(
                    "Client",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(generatedProjectType.FullName, "Client");
            return property.GetValue(project) as NeoClient
                ?? throw new InvalidOperationException(
                    $"Generated project '{generatedProjectType.FullName}' did not expose a NeoClient.");
        }

        private static IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            GetGeneratedReadOnlyFactories(Type generatedProjectType)
        {
            PropertyInfo property = generatedProjectType.GetProperty(
                    "NeoReadOnlyValueFactories",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(
                    generatedProjectType.FullName,
                    "NeoReadOnlyValueFactories");
            return property.GetValue(null)
                    as IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
                ?? throw new InvalidOperationException(
                    $"Generated project '{generatedProjectType.FullName}' exposed an invalid NeoReadOnlyValueFactories map.");
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

        private static IDisposable LoadGeneratedProject(
            Type generatedProjectType,
            string projectJson,
            string assetDatabasePath)
        {
            NeoAssetDatabase? assetDatabase = string.IsNullOrWhiteSpace(assetDatabasePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<NeoAssetDatabase>(assetDatabasePath);
            // Editor validation only needs a constructed client over the just-synced
            // schema; a from-scratch local draft (empty save built from defaults) is
            // enough to enumerate generated values. The store/synchronizer load
            // completes synchronously over the in-hand JSON + in-memory store, so it's
            // safe to drive the async path inline here.
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(projectJson),
                localStore: new NeoInMemoryLocalSaveStore());
            store.LoadAsync().GetAwaiter().GetResult();
            NeoSaveSynchronizer synchronizer = store.CreateNew();
            NeoClient client = new NeoLoader()
                .Load(synchronizer, assetDatabase)
                .GetAwaiter()
                .GetResult();

            ConstructorInfo? constructor = generatedProjectType.GetConstructor(
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

        private static IEnumerable<string> EnumerateProjectValueIds(ProjectData projectData)
        {
            if (projectData.values == null) yield break;
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
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(INeoClient).IsAssignableFrom(type)) continue;
                    if (!string.IsNullOrWhiteSpace(generatedNamespace) && type.Namespace != generatedNamespace) continue;
                    if (type.GetMethod(
                            "ResolveDialogueValue",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null)
                    {
                        continue;
                    }
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
                    $"implementing {nameof(INeoClient)} was found in namespace " +
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
