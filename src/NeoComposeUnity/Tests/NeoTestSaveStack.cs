// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using UnityEngine;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Test helper that stands up the Phase 9 save stack (project store → save
    /// synchronizer) over an in-memory local store and drives its async load/commit
    /// synchronously, so the many synchronous logic tests can construct a loaded
    /// <see cref="NeoClient"/> with a one-line call instead of the removed
    /// <c>loadSave</c>/<c>handleSave</c> delegates.
    /// </summary>
    /// <remarks>
    /// The async store/synchronizer load completes synchronously over the in-hand
    /// project JSON + in-memory store, so blocking on it here is safe. A single
    /// stack instance keeps one local store, so commit-then-reload round-trips work
    /// (use <see cref="Reopen"/> for the second load).
    /// </remarks>
    internal sealed class NeoTestSaveStack
    {
        private const string SaveCustomId = "test-save";

        private NeoTestSaveStack(NeoProjectStore store, INeoLocalSaveStore localStore)
        {
            Store = store;
            LocalStore = localStore;
            Synchronizer = store.Open(SaveCustomId);
        }

        public NeoProjectStore Store { get; }
        public INeoLocalSaveStore LocalStore { get; }
        public NeoSaveSynchronizer Synchronizer { get; }

        /// <summary>Builds a fresh stack (Ready) over the given project schema JSON.</summary>
        public static NeoTestSaveStack Create(
            string projectJson,
            NeoSaveOptions? options = null,
            INeoLocalSaveStore? localStore = null)
        {
            localStore ??= new NeoInMemoryLocalSaveStore();
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(projectJson),
                localStore: localStore,
                options: options);
            store.LoadAsync().GetAwaiter().GetResult();
            return new NeoTestSaveStack(store, localStore);
        }

        /// <summary>One-shot: build a stack and return a loaded <see cref="NeoClient"/>.</summary>
        public static NeoClient LoadClient(string projectJson, NeoSaveOptions? options = null)
        {
            return Create(projectJson, options).Load(options);
        }

        /// <summary>Resolves the active save and constructs a <see cref="NeoClient"/>.</summary>
        public NeoClient Load(NeoSaveOptions? options = null)
        {
            return new NeoLoader()
                .Load(Synchronizer, saveOptions: options)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// One-shot client load with explicit localization wiring (and optional
        /// pre-seeded save content so a load reads an existing save rather than a
        /// fresh draft).
        /// </summary>
        public static NeoClient LoadClient(
            string projectJson,
            NeoLocalizationOptions? localizationOptions,
            INeoLocalizationLocaleFileSource? localizationFileSource,
            string? initialSaveContent = null,
            NeoSaveOptions? options = null)
        {
            var localStore = new NeoInMemoryLocalSaveStore();
            if (!string.IsNullOrEmpty(initialSaveContent))
            {
                localStore.CommitSaveAsync(SaveCustomId, initialSaveContent!).GetAwaiter().GetResult();
            }

            var stack = Create(projectJson, options, localStore);
            return new NeoLoader()
                .Load(stack.Synchronizer, null, localizationOptions, localizationFileSource, options)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// A fresh synchronizer over the same local store, for a reload after a
        /// commit (it re-reads the committed content under the same custom id).
        /// </summary>
        public NeoSaveSynchronizer Reopen()
        {
            return Store.Open(SaveCustomId);
        }

        /// <summary>
        /// The raw persisted save JSON in the local store (what the removed
        /// <c>handleSave</c> buffer used to hold), or null before the first commit.
        /// </summary>
        public string? PersistedContent()
        {
            return LocalStore.LoadSaveAsync(SaveCustomId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Builds a <see cref="NeoClient"/> directly over a programmatically-built
        /// <see cref="ProjectData"/> schema with a fresh (empty) draft save — for
        /// tests that construct the schema in code rather than from JSON. The stub
        /// loader persists nothing.
        /// </summary>
        public static NeoClient ClientFromSchema(
            ProjectData schema,
            NeoSaveOptions? options = null,
            bool assumeCurrentSchema = true,
            string? loadedSaveContent = null,
            NeoLocalization? localization = null)
        {
            // Programmatically-built unit-test schemas predate export metadata
            // and are intentionally treated as current unless a schema-gate
            // test explicitly opts out. JSON fixtures still pass through the
            // production boundary unchanged.
            if (assumeCurrentSchema && schema.metadata is null)
            {
                schema.metadata = new ProjectExportMetadata
                {
                    schemaVersion = 12,
                    projectId = schema.project.id,
                    versionId = "unit-test-version",
                };
                schema.internalRecordRelations ??=
                    new System.Collections.Generic.Dictionary<
                        string,
                        InternalRecordRelation>();
            }
            return new NeoClient(
                new SchemaOnlyLoader(schema),
                loadedSaveContent,
                localization: localization,
                saveOptions: options);
        }

        /// <summary>
        /// A minimal <see cref="INeoSaveLoader"/> that exposes a schema and a
        /// brand-new draft (no content to load, commits are no-ops). For unit tests
        /// that only need a client over an in-code schema.
        /// </summary>
        private sealed class SchemaOnlyLoader : INeoSaveLoader
        {
            public SchemaOnlyLoader(ProjectData schema)
            {
                Schema = schema;
            }

            public ProjectData Schema { get; }
            public string CustomId => SaveCustomId;
            public Awaitable<string?> LoadSaveContentAsync() => NeoAwaitable.FromResult<string?>(null);
            public Awaitable CommitSaveContentAsync(string content, bool replaceSnapshot) => NeoAwaitable.Completed();
        }
    }
}
