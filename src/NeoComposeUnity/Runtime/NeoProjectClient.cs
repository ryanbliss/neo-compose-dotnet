// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// The base of every generated project client (<c>MyGameNeo</c>). The
    /// generated class adds only what depends on the project's schema, so an
    /// API added here reaches every project with a package update, without
    /// regenerating.
    /// </summary>
    public abstract class NeoProjectClient : INeoClient
    {
        private readonly IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> readOnlyFactories;
        private readonly IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory> writableFactories;

        /// <param name="client">The loaded runtime client the project reads and writes through.</param>
        /// <param name="readOnlyFactories">The generated read-only view factories, by class id.</param>
        /// <param name="writableFactories">The generated writable view factories, by class id.</param>
        /// <param name="classIdsByType">The class id of each generated class, by its type.</param>
        protected NeoProjectClient(
            NeoClient client,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory> writableFactories,
            IReadOnlyDictionary<Type, string> classIdsByType)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            this.readOnlyFactories = readOnlyFactories;
            this.writableFactories = writableFactories;
            ClassIdsByType = classIdsByType;
            client.RegisterGeneratedClassFactories(readOnlyFactories, writableFactories);
        }

        public NeoClient Client
        {
            get;
        }

        public NeoMemberClass AssetsRoot => Client.AssetsRoot;

        public NeoMemberClassWritable SaveRoot => Client.SaveRoot;

        public NeoMemberClassWritable SessionRoot => Client.SessionRoot;

        public NeoLocalization Localization => Client.Localization;

        public INeoSaveLoader Synchronizer => Client.Synchronizer;

        public INeoApiClient? ApiClient => Client.ApiClient;

        public NeoAuthentication? Authentication => Client.Authentication;

        internal IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> ReadOnlyValueFactories =>
            readOnlyFactories;

        internal IReadOnlyDictionary<Type, string> ClassIdsByType
        {
            get;
        }

        public string SerializeSaveData() => Client.SerializeSaveData();

        /// <inheritdoc cref="INeoClient.CommitAsync"/>
        public Awaitable CommitAsync(bool replaceSnapshot = false, bool forceCapture = false) => Client.CommitAsync(replaceSnapshot, forceCapture);

        public int RunGarbageCollector() => Client.RunGarbageCollector();

        public IReadOnlyList<string> FindUnlinkedSaveValueIds() => Client.FindUnlinkedSaveValueIds();

        /// <inheritdoc cref="NeoClient.RunTransaction"/>
        public void RunTransaction(Action transaction) => Client.RunTransaction(transaction);

        /// <summary>The generated view of the row <paramref name="valueId"/>, or null when it has none.</summary>
        internal object? ResolveValue(string valueId) =>
            NeoGeneratedTypesSupport.ResolveClassValue(Client, valueId, readOnlyFactories, writableFactories);

        public virtual void Dispose() => Client.Dispose();

        /// <summary>Constructs the generated project <paramref name="projectType"/> over <paramref name="client"/>, for tooling that finds it by type.</summary>
        internal static NeoProjectClient Construct(Type projectType, NeoClient client)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            ConstructorInfo? constructor = projectType.GetConstructor(
                flags,
                binder: null,
                types: new[] { typeof(NeoClient), typeof(NeoDialogueRuntimeOptions) },
                modifiers: null);
            if (constructor != null)
                return (NeoProjectClient)constructor.Invoke(new object?[] { client, null });
            throw new MissingMethodException(projectType.FullName, ".ctor(NeoClient, NeoDialogueRuntimeOptions)");
        }
    }
}
