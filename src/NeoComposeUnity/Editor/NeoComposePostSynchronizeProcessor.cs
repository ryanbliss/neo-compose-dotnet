// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        private const string PendingKey = "NeoCompose.PostSynchronize.Pending";
        private const string ProjectJsonPathKey = "NeoCompose.PostSynchronize.ProjectJsonPath";
        private const string GeneratedTypesPathKey = "NeoCompose.PostSynchronize.GeneratedTypesPath";
        private const string AssetDatabasePathKey = "NeoCompose.PostSynchronize.AssetDatabasePath";
        private const string NamespaceKey = "NeoCompose.PostSynchronize.Namespace";
        private const string AttemptsKey = "NeoCompose.PostSynchronize.Attempts";
        private const string GeneratedTileAssetDirectory = "Assets/Neo/Generated/Tiles";
        private const string GeneratedRuleTileAssetDirectory = "Assets/Neo/Generated/RuleTiles";
        private const int MaxAttempts = 20;

        static NeoComposePostSynchronizeProcessor()
        {
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

            SessionState.SetString(PendingKey, "1");
            SessionState.SetString(ProjectJsonPathKey, projectJsonPath);
            SessionState.SetString(GeneratedTypesPathKey, generatedTypesPath);
            SessionState.SetString(AssetDatabasePathKey, assetDatabasePath);
            SessionState.SetString(NamespaceKey, config.namespaceForGeneratedTypes);
            SessionState.SetString(AttemptsKey, "0");

            AssetDatabase.ImportAsset(projectJsonPath);
            AssetDatabase.ImportAsset(generatedTypesPath);
            AssetDatabase.Refresh();
            EditorApplication.delayCall += TryRunPending;
        }

        private static void TryRunPending()
        {
            if (SessionState.GetString(PendingKey, "") != "1") return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryRunPending;
                return;
            }

            string projectJsonPath = SessionState.GetString(ProjectJsonPathKey, "");
            string assetDatabasePath = SessionState.GetString(AssetDatabasePathKey, "");
            string generatedNamespace = SessionState.GetString(NamespaceKey, "");
            int attempts = int.TryParse(SessionState.GetString(AttemptsKey, "0"), out int parsedAttempts)
                ? parsedAttempts
                : 0;

            try
            {
                if (!File.Exists(projectJsonPath))
                {
                    ClearPending();
                    return;
                }

                string projectJson = File.ReadAllText(projectJsonPath);
                ProjectData projectData = JsonConvert.DeserializeObject<ProjectData>(projectJson)
                    ?? throw new InvalidOperationException("Neo Compose project JSON could not be deserialized after synchronization.");
                Type? generatedProjectType = FindGeneratedProjectType(generatedNamespace);
                if (generatedProjectType == null)
                {
                    RetryOrFail(attempts, generatedNamespace);
                    return;
                }

                Run(projectJson, projectData, assetDatabasePath, generatedProjectType);
                ClearPending();
                SetStatus("Neo Compose files synchronized.");
            }
            catch (Exception exception)
            {
                ClearPending();
                SetStatus("Synchronized, but post-sync validation failed: " + exception.Message);
                Debug.LogError(exception);
            }
        }

        /// <summary>
        /// Writes the terminal status the editor window reads, then repaints any open
        /// window so the message replaces the last mid-sync progress line (which a
        /// domain reload can otherwise leave stuck).
        /// </summary>
        private static void SetStatus(string message)
        {
            SessionState.SetString(NeoComposeEditorWindow.StatusSessionKey, message);
            // The post-reload message is informational; without this a stale
            // error severity from an earlier failed attempt would colour it.
            SessionState.SetInt(
                NeoComposeEditorWindow.StatusSeveritySessionKey,
                (int)MessageType.Info);
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
            SynchronizeGeneratedTileAssets(
                projectData,
                assetDatabasePath,
                project,
                resolveMethod);

            foreach (string valueId in EnumerateProjectValueIds(projectData))
            {
                object? resolved = resolveMethod.Invoke(project, new object[] { valueId });
                if (resolved is not NeoGeneratedCustomValue custom) continue;
                string key = custom.valueId ?? valueId;
                if (!synchronized.Add(key)) continue;

                InvokeOnDidSynchronize(custom);
            }
        }

        private static void InvokeOnDidSynchronize(NeoGeneratedCustomValue custom)
        {
            MethodInfo? method = custom.GetType().GetMethod(
                "OnDidSynchronize",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            method?.Invoke(custom, Array.Empty<object>());
        }

        private static void SynchronizeGeneratedTileAssets(
            ProjectData projectData,
            string assetDatabasePath,
            IDisposable project,
            MethodInfo resolveMethod)
        {
            if (string.IsNullOrWhiteSpace(assetDatabasePath)) return;
            var assetDatabase = AssetDatabase.LoadAssetAtPath<NeoAssetDatabase>(assetDatabasePath);
            if (assetDatabase == null) return;

            var tileValueIds = new HashSet<string>(EnumerateReferencedTileValueIds(projectData));
            foreach (var stale in assetDatabase.FindMissingTileAssets(tileValueIds))
            {
                DeleteGeneratedTileAsset(stale.AssetPath);
                assetDatabase.RemoveTileAsset(stale.TileValueId);
            }

            foreach (string tileValueId in tileValueIds.OrderBy(id => id, StringComparer.Ordinal))
            {
                object? resolved = resolveMethod.Invoke(project, new object[] { tileValueId });
                if (resolved is not NeoGeneratedCustomValue custom) continue;
                var generatedTile = NeoTileAssetFactory.CreateTransientTileBase(custom);
                if (generatedTile == null) continue;

                string assetPath = GeneratedTileAssetPath(tileValueId, generatedTile);
                var existingEntry = assetDatabase.TryGetTileEntry(tileValueId);
                if (existingEntry != null &&
                    !string.IsNullOrWhiteSpace(existingEntry.AssetPath) &&
                    existingEntry.AssetPath != assetPath)
                {
                    DeleteGeneratedTileAsset(existingEntry.AssetPath);
                }
                DeleteAlternateGeneratedTileAsset(tileValueId, assetPath);

                TileBase persistedTile = PersistGeneratedTileAsset(assetPath, generatedTile);
                assetDatabase.SetTileAsset(
                    tileValueId,
                    custom.typeId,
                    assetPath,
                    GeneratedTileContentHash(projectData, tileValueId, custom),
                    persistedTile);
            }

            EditorUtility.SetDirty(assetDatabase);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Tile asset value ids referenced by tile placements. Placements are
        /// containment members (rows carrying a containerId) whose record has
        /// a "Tile" single-select Lookup — the values-native shape that
        /// replaced the derived tileGridContents regions.
        /// </summary>
        private static IEnumerable<string> EnumerateReferencedTileValueIds(ProjectData projectData)
        {
            var seen = new HashSet<string>();
            foreach (var row in projectData.values.Values)
            {
                if (row is not ObjectAttributeValue placement) continue;
                if (string.IsNullOrEmpty(placement.containerId)) continue;
                if (string.IsNullOrEmpty(placement.typeId)) continue;
                if (placement.value == null) continue;
                string? tileKey = FindTileSchemaKey(projectData, placement.typeId!);
                if (tileKey == null) continue;
                if (!placement.value.TryGetValue(tileKey, out string tileLookupId)) continue;
                if (!projectData.values.TryGetValue(tileLookupId, out AttributeValue lookupRow)) continue;
                if (lookupRow is not ArrayAttributeValue lookupArray || lookupArray.value == null) continue;
                foreach (var tileValueId in lookupArray.value)
                {
                    if (string.IsNullOrWhiteSpace(tileValueId)) continue;
                    if (seen.Add(tileValueId)) yield return tileValueId;
                }
            }
        }

        private static readonly string[] TileSchemaKeyCandidates = { "Tile", "tileValue", "tileValueId" };

        private static string? FindTileSchemaKey(ProjectData projectData, string typeId)
        {
            if (!projectData.types.TryGetValue(typeId, out CustomType type) || type == null) return null;
            IList<MergedSchemaEntry> merged;
            try
            {
                merged = CustomTypeInheritance.MergeSchemas(
                    CustomTypeInheritance.ResolveChain(
                        typeId,
                        id => projectData.types.TryGetValue(id, out CustomType match) ? match : null));
            }
            catch (CircularInheritanceError)
            {
                return null;
            }
            foreach (var candidate in TileSchemaKeyCandidates)
            {
                foreach (var entry in merged)
                {
                    if (string.Equals(entry.schemaKey, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.schemaKey;
                    }
                }
            }
            return null;
        }

        private static string GeneratedTileAssetPath(string tileValueId, TileBase tileBase)
        {
            string fileName = $"{SanitizeAssetFileName(tileValueId)}.asset";
            return tileBase is NeoRuleTile
                ? $"{GeneratedRuleTileAssetDirectory}/{fileName}"
                : $"{GeneratedTileAssetDirectory}/{fileName}";
        }

        private static void DeleteAlternateGeneratedTileAsset(string tileValueId, string keepPath)
        {
            string fileName = $"{SanitizeAssetFileName(tileValueId)}.asset";
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
            string tileValueId,
            NeoGeneratedCustomValue custom)
        {
            string updatedAt = projectData.values.TryGetValue(tileValueId, out var row)
                ? row.updatedAt.ToString()
                : "";
            string tileKind = NeoTileAssetFactory.TryResolveSmartTile(custom, out _)
                ? "rule"
                : "tile";
            return $"{custom.typeId ?? ""}:{updatedAt}:{tileKind}";
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

        private static void RetryOrFail(int attempts, string generatedNamespace)
        {
            if (attempts >= MaxAttempts)
            {
                ClearPending();
                Debug.LogError(
                    "Neo Compose post-synchronize hooks could not run because no generated project type " +
                    $"implementing {nameof(INeoClient)} was found in namespace '{generatedNamespace}'.");
                return;
            }

            SessionState.SetString(AttemptsKey, (attempts + 1).ToString());
            EditorApplication.delayCall += TryRunPending;
        }

        private static void ClearPending()
        {
            SessionState.EraseString(PendingKey);
            SessionState.EraseString(ProjectJsonPathKey);
            SessionState.EraseString(GeneratedTypesPathKey);
            SessionState.EraseString(AssetDatabasePathKey);
            SessionState.EraseString(NamespaceKey);
            SessionState.EraseString(AttemptsKey);
        }
    }
}
