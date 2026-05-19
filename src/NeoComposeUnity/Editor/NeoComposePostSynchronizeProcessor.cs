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
using UnityEditor;
using UnityEngine;

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
            }
            catch (Exception exception)
            {
                ClearPending();
                Debug.LogError(exception);
            }
        }

        private static void Run(
            string projectJson,
            ProjectData projectData,
            string assetDatabasePath,
            Type generatedProjectType)
        {
            string currentSaveJson = "";
            using var project = LoadGeneratedProject(
                generatedProjectType,
                projectJson,
                assetDatabasePath,
                () => currentSaveJson,
                content => currentSaveJson = content);
            MethodInfo resolveMethod = generatedProjectType.GetMethod(
                    "ResolveDialogueValue",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(
                    generatedProjectType.FullName,
                    "ResolveDialogueValue");

            var synchronized = new HashSet<string>();
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

        private static IDisposable LoadGeneratedProject(
            Type generatedProjectType,
            string projectJson,
            string assetDatabasePath,
            NeoClient.LoadSave loadSave,
            NeoClient.HandleSave handleSave)
        {
            NeoAssetDatabase? assetDatabase = string.IsNullOrWhiteSpace(assetDatabasePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<NeoAssetDatabase>(assetDatabasePath);
            NeoClient client = new NeoLoader().Load(projectJson, loadSave, handleSave, assetDatabase);

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
