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

        // Opt in only from fixtures that do not mutate schema declarations.
        // Every load still creates a fresh store, save, and client.
        internal static readonly NeoJsonProjectDataSource SynthExample = NeoTestExport.Source(
            System.IO.File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/synth-example.json"));

        private NeoTestSaveStack(NeoProjectStore store, INeoLocalSaveStore localStore)
        {
            Store = store;
            LocalStore = localStore;
            Synchronizer = store.Open(SaveCustomId);
        }

        public NeoProjectStore Store
        {
            get;
        }
        public INeoLocalSaveStore LocalStore
        {
            get;
        }
        public NeoSaveSynchronizer Synchronizer
        {
            get;
        }

        /// <summary>Builds a fresh stack (Ready) over a one-document test corpus (see <see cref="NeoTestExport"/>).</summary>
        public static NeoTestSaveStack Create(
            string projectJson,
            NeoSaveOptions? options = null,
            INeoLocalSaveStore? localStore = null)
        {
            return Create(NeoTestExport.Source(projectJson), options, localStore);
        }

        public static NeoTestSaveStack Create(
            IProjectDataSource projectSource,
            NeoSaveOptions? options = null,
            INeoLocalSaveStore? localStore = null)
        {
            localStore ??= new NeoInMemoryLocalSaveStore();
            // A user file loads a frame later, so a store driven synchronously
            // is a tooling store: its saves read authored User defaults.
            var store = new NeoProjectStore(
                dataSource: projectSource,
                localStore: localStore,
                options: options,
                loadUserFile: false);
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
            return LoadSynchronously(Synchronizer, options: options);
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
            return LoadSynchronously(stack.Synchronizer, localizationOptions, localizationFileSource, options);
        }

        // Pure logic fixtures construct clients synchronously. Loader integration
        // tests await NeoLoader.Load or the generated loader instead.
        public static NeoClient LoadSynchronously(
            INeoSaveLoader loader, NeoLocalizationOptions? localizationOptions = null,
            INeoLocalizationLocaleFileSource? localizationFileSource = null,
            NeoSaveOptions? options = null)
        {
            NeoProjectDataValidator.Validate(loader.Schema);
            localizationOptions ??= NeoComposeConfig.LoadDefault()?.ToLocalizationOptions();
            var localization = NeoLocalization.LoadMain(loader.Schema.localization,
                localizationFileSource ?? new NeoResourcesLocalizationLocaleFileSource(), localizationOptions);
            return new NeoClient(loader, loader.LoadSaveContentAsync().GetAwaiter().GetResult(),
                NeoAssetDatabase.LoadDefault(), localization, options);
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
                    schemaVersion = NeoProjectExportContract.CurrentSchemaVersion,
                    projectId = schema.project.id,
                    versionId = "unit-test-version",
                };
                schema.internalRecordRelations ??=
                    new System.Collections.Generic.Dictionary<
                        string,
                        InternalRecordRelation>();
            }
            schema.variantFolders ??=
                new System.Collections.Generic.Dictionary<
                    string,
                    VariantFolderRecord>();
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

            public ProjectData Schema
            {
                get;
            }
            public string CustomId => SaveCustomId;
            public Awaitable<string?> LoadSaveContentAsync() => NeoAwaitable.FromResult<string?>(null);
            public Awaitable CommitSaveContentAsync(string content, bool replaceSnapshot) => NeoAwaitable.Completed();
        }
    }

    /// <summary>
    /// The User root every project carries (P104), as the retired
    /// <c>p104-user-root</c> migration minted it: an empty User-storage class,
    /// its root member, and the member's empty object row.
    /// </summary>
    internal static class NeoTestUserRoot
    {
        public const string MemberId = "root-user";
        public const string ClassId = "root-user-class";
        public const string ValueId = "root-user-value";

        public static ProjectData WithUserRoot(this ProjectData data)
        {
            string projectId = data.project.id;
            data.project.rootUserMemberId = MemberId;
            data.members[MemberId] = new ClassMember
            {
                id = MemberId,
                projectId = projectId,
                name = "User",
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                Storage = NeoMemberStorage.User,
                classId = ClassId,
                valueId = ValueId,
            };
            data.classes[ClassId] = new NeoSchemaClass
            {
                id = ClassId,
                projectId = projectId,
                name = "User",
                schema = new System.Collections.Generic.Dictionary<string, string>(),
                allowedStorage = NeoMemberStorage.User,
                UiVisibility = NeoClassVisibilityKind.Hidden,
            };
            data.values[ValueId] = new ObjectMemberValue
            {
                id = ValueId,
                classId = ClassId,
                value = new System.Collections.Generic.Dictionary<string, string>(),
            };
            return data;
        }
    }
}
