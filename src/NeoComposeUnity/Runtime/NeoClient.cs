// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using InvalidOperationException = System.InvalidOperationException;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// NeoClient owns a live save file instance.
    /// </summary>
    public partial class NeoClient : INeoClient
    {
        private static readonly HashSet<NeoClient> activeClients = new();

        public delegate string BuildSaveName();
        public delegate object? NeoNativeFunctionInvoker(
            NeoClient client,
            object? receiver,
            object?[] args);
        public delegate void NeoDeferredNativeFunctionInvoker(
            NeoClient client,
            object? receiver,
            object?[] args,
            NeoDeferredFunctionBase deferred);
        public NeoMemberClass assets { get; protected set; }
        public NeoMemberClassWritable save { get; protected set; }
        public NeoMemberClassWritable session { get; protected set; }
        public NeoMemberClass AssetsRoot => assets;
        public NeoMemberClassWritable SaveRoot => save;
        public NeoMemberClassWritable SessionRoot => session;
        public NeoLocalization Localization { get; }

        /// <summary>
        /// The active-save abstraction this client persists through (normally a
        /// <see cref="NeoSaveSynchronizer"/>). Exposed so callers can reach the
        /// synchronizer's lifecycle events and active-save state.
        /// </summary>
        public INeoSaveLoader Synchronizer => loader;

        /// <summary>
        /// The cloud save transport, or null when the active loader is local-only or
        /// a custom <see cref="INeoSaveLoader"/> without one.
        /// </summary>
        public INeoApiClient? ApiClient => (loader as NeoSaveSynchronizer)?.ApiClient;

        /// <summary>
        /// The runtime authentication backing cloud sync, or null when local-only or a
        /// custom loader without one.
        /// </summary>
        public NeoAuthentication? Authentication => (loader as NeoSaveSynchronizer)?.Authentication;

        /// <summary>
        /// Flat registry of every constructed <see cref="NeoMember"/>,
        /// keyed by <see cref="MakeNodeKey"/>. Each
        /// <see cref="NeoMember"/> registers itself at the end of
        /// construction so consumers (and the
        /// <see cref="NeoMember.Create"/> /
        /// <see cref="NeoMember.CreateWritable"/> factories) can look up
        /// existing instances and reuse them rather than constructing
        /// duplicates that share the same wire identity.
        /// </summary>
        internal IReadOnlyDictionary<string, NeoMember> nodes => nodesInternal;
        private readonly Dictionary<string, NeoMember> nodesInternal = new();
        private readonly Dictionary<string, NeoGeneratedClassValue> generatedValuesInternal = new();
        private readonly Dictionary<string, object> animationClips = new();
        /// <summary>
        /// P67 §7.2 — one <c>NeoVariant&lt;T&gt;</c> handle per (variant id or
        /// base class id, T). Typed <c>object</c> for the same reason
        /// <see cref="animationClips"/> is: the handle is an open generic, so
        /// the type check happens on retrieval.
        /// </summary>
        private readonly Dictionary<string, object> variantHandles = new();
        /// <summary>
        /// Compiled definitions, keyed exactly as <see cref="animationClips"/>
        /// is. Kept apart from the handle dictionary because that one is typed
        /// <c>object</c> over an open generic; this one is what
        /// <see cref="System.IDisposable"/> has to reach.
        /// </summary>
        private readonly Dictionary<string, NeoAnimationDefinition> animationDefinitions =
            new();
        private readonly HashSet<string> reportedAnimationChildSkips =
            new(System.StringComparer.Ordinal);
        private readonly HashSet<string> reportedAnimationApplySkips =
            new(System.StringComparer.Ordinal);
        private readonly NeoAnimationCoordinator animationCoordinator = new();
        private readonly HashSet<NeoDialogue> activeDialogues = new();
        private readonly HashSet<NeoDeferredFunctionBase> activeDirectDeferredFunctions = new();
        private readonly object activeDirectDeferredFunctionsLock = new();
        private readonly Dictionary<string, NeoResolvedNSFunction> resolvedNSFunctions = new();
        private readonly object resolvedNSFunctionsLock = new();
        private readonly Dictionary<string, IList<NeoSchemaClass>> classInheritanceChains = new();
        private readonly Dictionary<string, IList<MergedSchemaEntry>> instanceSurfaceSchemas = new();
        private readonly Dictionary<string, IList<MergedSchemaEntry>> storedInstanceSchemas = new();
        private readonly Dictionary<string, IList<MergedSchemaEntry>> readOnlyMemberSchemas = new();
        private readonly Dictionary<string, MemberValue> virtualValues = new();
        private readonly Dictionary<string, NeoValueOwnership> virtualValueOwnership = new();
        private readonly Dictionary<string, Dictionary<string, string>> virtualClassChildren = new();
        private readonly Dictionary<string, HashSet<string>> virtualEntriesByContainer = new();
        private readonly Dictionary<string, string> virtualContainerByRow = new();
        private readonly Dictionary<string, HashSet<string>> virtualValueIdsByRoot = new();
        private readonly Dictionary<string, HashSet<string>> virtualClassParentIdsByRoot = new();
        private IReadOnlyDictionary<string, MemberValue> readOnlyAuthoredRows =
            new Dictionary<string, MemberValue>();
        private IReadOnlyDictionary<string, string> readOnlyAuthoredClassIds =
            new Dictionary<string, string>();
        internal bool RetainsReadOnlyValidationProjection =>
            readOnlyAuthoredRows.Count != 0 || readOnlyAuthoredClassIds.Count != 0;
        private readonly List<(string valueId, Member member)> recoveredReadOnlySaveValues = new();
        private bool isDisposed;

        internal bool TryGetResolvedNSFunction(
            string memberId,
            [NotNullWhen(true)] out NeoResolvedNSFunction? function)
        {
            lock (resolvedNSFunctionsLock)
            {
                return resolvedNSFunctions.TryGetValue(memberId, out function);
            }
        }

        internal NeoResolvedNSFunction CacheResolvedNSFunction(
            NeoResolvedNSFunction function)
        {
            lock (resolvedNSFunctionsLock)
            {
                if (resolvedNSFunctions.TryGetValue(
                        function.MemberId,
                        out NeoResolvedNSFunction existing))
                {
                    return existing;
                }
                resolvedNSFunctions[function.MemberId] = function;
                return function;
            }
        }

        /// <summary>
        /// Read-only views over the underlying project + save maps.
        /// Exposed for evaluators / inspectors that need to enumerate
        /// the full set rather than fetch one-at-a-time via
        /// <see cref="TryGetMember{T}"/> et al. The returned
        /// dictionaries are the same instances the client reads from
        /// internally — mutations through these views propagate.
        /// </summary>
        internal IReadOnlyDictionary<string, Member> members => data.members;
        internal IReadOnlyDictionary<string, MemberValue> values => data.values;
        internal IReadOnlyDictionary<string, NeoSchemaClass> classes => data.classes;
        internal IReadOnlyDictionary<string, ConstructorRecord> constructors =>
            data.constructors;
        internal IReadOnlyDictionary<string, VariantRecord> variants =>
            data.variants;
        internal IReadOnlyDictionary<string, VariantFolderRecord> variantFolders =>
            data.variantFolders;
        internal IReadOnlyDictionary<string, Interface> interfaces => data.interfaces;
        internal IReadOnlyDictionary<string, Enum> enums => data.enums;
        internal IReadOnlyDictionary<string, Dialogue> dialogues => data.dialogues;
        internal IReadOnlyDictionary<string, DialogueGroup> dialogueGroups => data.dialogueGroups;

        /// <summary>
        /// The dialogues API (the generated <c>NeoDialogues</c>), registered by
        /// <see cref="NeoDialoguesBase"/> at construction. Lets runtime values
        /// such as <see cref="NeoDialogueReference"/> trigger dialogues without a
        /// compile-time dependency on the generated client.
        /// </summary>
        internal NeoDialoguesBase? DialoguesApi { get; private set; }

        internal void RegisterDialoguesApi(NeoDialoguesBase api)
        {
            DialoguesApi = api;
        }
        internal IReadOnlyDictionary<string, PriorityGroup> priorityGroups => data.priorityGroups;
        internal IReadOnlyDictionary<string, MemberValue> saveValues => saveData.values;
        internal IReadOnlyDictionary<string, MemberValue> sessionValues => sessionData.values;
        internal Project project => data.project;
        internal ProjectData ProjectDataForRuntime => data;
        internal bool IsDisposed => isDisposed;
        internal int ActiveDialogueCount => activeDialogues.Count;
        internal NeoAnimationCoordinator AnimationCoordinator => animationCoordinator;

        /// <summary>
        /// P67 §7.2 — the cached handle for one declared variant. Cached like a
        /// clip is, and for the same reason: a `Variants` entry is a property,
        /// so a loop that reads it every frame must not mint a handle per read.
        /// </summary>
        internal NeoVariant<T> GetOrCreateVariant<T>(string variantId)
            where T : NeoGeneratedClassValue
        {
            EnsureNotDisposed();
            if (!TryGetVariant(variantId, out VariantRecord? record))
            {
                throw new InvalidOperationException(
                    $"Variant '{variantId}' is not in this project export. Re-export the project, or regenerate the C# types if the variant was deleted.");
            }
            return GetOrCreateVariantHandle<T>(
                $"variant\u001f{variantId}",
                record.classId,
                record);
        }

        /// <summary>
        /// P67 §3.4 — the cached handle for a class's reserved `Base` entry.
        /// Keyed by class id in the same map: a base entry and a declared
        /// variant can never collide, because a record id and a class id are
        /// distinguished by the key prefix - and the requesting `T` is part of
        /// the key too (see <see cref="GetOrCreateVariantHandle"/>).
        /// </summary>
        internal NeoVariant<T> GetOrCreateBaseVariant<T>(string classId)
            where T : NeoGeneratedClassValue
        {
            EnsureNotDisposed();
            return GetOrCreateVariantHandle<T>(
                $"base\u001f{classId}",
                classId,
                record: null);
        }

        /// <summary>P68 §7 — cached generated-tree handle for a lookup variant.</summary>
        internal NeoLookupVariant<T, TValue> GetOrCreateLookupVariant<T, TValue>(
            string variantId)
            where T : NeoGeneratedClassValue
            where TValue : NeoGeneratedClassValue
        {
            EnsureNotDisposed();
            if (!TryGetVariant(variantId, out VariantRecord? record))
            {
                throw new InvalidOperationException(
                    $"Variant '{variantId}' is not in this project export. Re-export the project.");
            }
            string key = $"lookup\u001f{variantId}\u001f{typeof(T).FullName}\u001f{typeof(TValue).FullName}";
            if (variantHandles.TryGetValue(key, out object existing))
            {
                if (existing is NeoLookupVariant<T, TValue> match) return match;
                throw new InvalidOperationException(
                    $"Lookup variant cache key '{key}' changed target type; regenerate the project's C# types.");
            }
            var handle = new NeoLookupVariant<T, TValue>(this, record);
            variantHandles.Add(key, handle);
            return handle;
        }

        private NeoVariant<T> GetOrCreateVariantHandle<T>(
            string cacheKey,
            string classId,
            VariantRecord? record)
            where T : NeoGeneratedClassValue
        {
            // P67 6 covariance is DATA-level: the stored classId may be a
            // subclass of a member's declared target, so one record is
            // legitimately resolved under more than one `T` in the same client
            // - as `NeoVariant<Base>` through a base-typed member read, and as
            // `NeoVariant<Sub>` through the subclass's own Variants tree.
            // `NeoVariant<T>` stays sealed and invariant, so the handle is
            // per-(record, T) and the key has to be too; keying by record
            // alone made whichever surface asked second throw.
            string typedKey = $"{cacheKey}{typeof(T).FullName}";
            if (variantHandles.TryGetValue(typedKey, out object existing))
            {
                if (existing is NeoVariant<T> match) return match;
                throw new InvalidOperationException(
                    $"Variant cache key '{typedKey}' changed target type; regenerate the project's C# types.");
            }
            var handle = new NeoVariant<T>(this, classId, record);
            variantHandles.Add(typedKey, handle);
            return handle;
        }

        /// <summary>
        /// Generated-code support for resolving the per-instance cached
        /// animation handle for one clip schema key. Application code should
        /// normally use the generated clip property instead.
        /// </summary>
        [System.ComponentModel.EditorBrowsable(
            System.ComponentModel.EditorBrowsableState.Never)]
        public NeoAnimationClip<T> GetOrCreateAnimationClip<T>(
            T target,
            string schemaKey)
            where T : NeoGeneratedClassValue
        {
            EnsureNotDisposed();
            string cacheKey = $"{target.AnimationInstanceIdentity}\u001f{schemaKey}";
            if (animationClips.TryGetValue(cacheKey, out object existing))
            {
                if (existing is NeoAnimationClip<T> match) return match;
                throw new InvalidOperationException(
                    $"Animation clip cache key '{schemaKey}' changed target type; regenerate the project's C# types.");
            }
            NeoAnimationDefinition definition = NeoAnimationCompiler.Compile(target, schemaKey);
            var clip = new NeoAnimationClip<T>(
                target,
                target.AnimationInstanceIdentity,
                definition.FPS,
                definition.Duration,
                animationCoordinator,
                definition.PreparePlayback,
                definition.ApplyFrame);
            animationClips.Add(cacheKey, clip);
            // The definition is what P48 §3.1's segment sources hang off, and
            // they hold OnWritableValueChanged handlers. The handle has no
            // reference to it — only its two delegates — so the cache keeps the
            // definition beside the handle and disposes the two together.
            animationDefinitions.Add(cacheKey, definition);
            return clip;
        }

        /// <summary>
        /// Whether a skipped animation child reference has not been reported
        /// yet, claiming it if so. The spec's granularity is one log per
        /// (clip, reference) — not per instance, and not once more per parent
        /// for a shared child clip, whose nested compile bypasses the clip
        /// cache entirely — so fifty placements missing the same optional slot
        /// produce one warning between them.
        /// <para>
        /// Cleared by <see cref="InvalidateAnimationClips"/>, which drops every
        /// compiled clip, so a genuine re-compile of the whole project reports
        /// again. Deliberately <b>not</b> cleared by
        /// <see cref="ReleaseAnimationClips"/>: that drops one value's clips,
        /// and this key is not value-scoped — resetting it there would let a
        /// single despawn mid-spawn-wave restore exactly the per-instance
        /// warning storm the dedup exists to stop, while telling an author
        /// nothing new about an unchanged authored graph.
        /// </para>
        /// </summary>
        internal bool ShouldReportAnimationChildSkip(
            string clipKey,
            string sourceChildId)
        {
            return reportedAnimationChildSkips.Add(
                $"{clipKey}\u001f{sourceChildId}");
        }

        /// <summary>
        /// Whether an APPLY-time animation skip has not been reported yet,
        /// claiming it if so. P42 §1.4's grain is one warning per (clip, frame,
        /// member); <paramref name="skipKey"/> is that triple, pre-built lazily
        /// by the compiled write so a clip looping at 8 FPS allocates nothing
        /// per tick on the overwhelmingly common non-skipping path.
        /// <para>
        /// A separate set from <see cref="ShouldReportAnimationChildSkip"/>:
        /// that one is compile-time and keyed by (clip, child reference), and
        /// conflating the two would let one silence the other. Cleared on the
        /// same schedule — <see cref="InvalidateAnimationClips"/> and dispose —
        /// and deliberately NOT by <see cref="ReleaseAnimationClips"/>, for the
        /// same reason plus one more: a looping clip re-enters the same frame
        /// forever, so an apply-time set faces strictly more pressure than the
        /// compile-time one.
        /// </para>
        /// </summary>
        internal bool ShouldReportAnimationApplySkip(string skipKey)
        {
            return reportedAnimationApplySkips.Add(skipKey);
        }

        internal void ReleaseAnimationClips(NeoGeneratedClassValue target)
        {
            string prefix = $"{target.AnimationInstanceIdentity}\u001f";
            var remove = new List<string>();
            var players = new List<INeoAnimationPlayer>();
            foreach (var pair in new List<KeyValuePair<string, object>>(animationClips))
            {
                if (!pair.Key.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
                if (pair.Value is INeoAnimationPlayer player)
                {
                    players.Add(player);
                }
                remove.Add(pair.Key);
            }
            foreach (string key in remove)
            {
                animationClips.Remove(key);
                if (animationDefinitions.TryGetValue(
                        key,
                        out NeoAnimationDefinition definition))
                {
                    animationDefinitions.Remove(key);
                    definition.Dispose();
                }
            }
            foreach (INeoAnimationPlayer player in players)
            {
                player.StopFromCoordinator();
            }
        }

        internal void InvalidateAnimationClips()
        {
            foreach (object cached in new List<object>(animationClips.Values))
            {
                if (cached is INeoAnimationPlayer player)
                {
                    player.StopFromCoordinator();
                }
            }
            animationClips.Clear();
            // A variant handle is minted against this client's records, so it
            // is dropped on the same schedule a recompile drops clips.
            variantHandles.Clear();
            DisposeAnimationDefinitions();
            reportedAnimationChildSkips.Clear();
            reportedAnimationApplySkips.Clear();
        }

        private void DisposeAnimationDefinitions()
        {
            foreach (NeoAnimationDefinition definition in
                new List<NeoAnimationDefinition>(animationDefinitions.Values))
            {
                definition.Dispose();
            }
            animationDefinitions.Clear();
        }

        internal static void InvalidateAllAnimationClips()
        {
            foreach (NeoClient client in new List<NeoClient>(activeClients))
            {
                if (!client.isDisposed) client.InvalidateAnimationClips();
            }
        }

        /// <summary>
        /// Fired whenever a save-side value row is added, replaced, or
        /// removed. The argument is the affected value id.
        /// Generated wrappers and runtime collection helpers use this as a
        /// coarse invalidation signal after <c>*Writable</c> mutations.
        /// </summary>
        internal event System.Action<string>? OnSaveValueChanged;
        internal event System.Action<NeoValueOwnership, string>? OnWritableValueChanged;
        /// <summary>
        /// Fired when the member-id keyed target of a Save/Session static
        /// member changes. Value-row mutations continue to use
        /// <see cref="OnWritableValueChanged"/>; this event covers rebinding,
        /// clearing, and restoring the authored target.
        /// </summary>
        internal event System.Action<NeoValueOwnership, string>? OnStaticBindingChanged;

        // --- Live auto-save (specs/live-save-sessions.md) -------------------
        // While a live session is active, every save-value write schedules an
        // automatic commit: the game never calls CommitAsync to stream into
        // its live snapshot. A short coalescing delay batches the burst of
        // writes a single frame/action produces into one serialize + stage;
        // the synchronizer's own debounce then throttles the network flush.
        private bool liveAutoCommitScheduled;
        private bool suppressLiveAutoCommit;
        internal double LiveAutoCommitDelaySeconds = 0.3;
        internal System.Func<double, Awaitable> LiveAutoCommitDelay = DefaultLiveAutoCommitDelay;

        /// <summary>Single chokepoint for save-value change notifications: raises
        /// the invalidation events and, in a live session, schedules the
        /// auto-commit that streams the change to the live snapshot.</summary>
        private void RaiseSaveValueChanged(string id, string? field = null)
        {
            OnSaveValueChanged?.Invoke(id);
            if (!suppressLiveAutoCommit && loader is NeoSaveSynchronizer synchronizer)
            {
                synchronizer.MarkDirtyValue(id, field);
            }
            ScheduleLiveAutoCommit();
        }

        private void ScheduleLiveAutoCommit()
        {
            if (liveAutoCommitScheduled || suppressLiveAutoCommit) return;
            if (loader is not NeoSaveSynchronizer synchronizer) return;
            if (!synchronizer.IsLiveSessionActive) return;
            liveAutoCommitScheduled = true;
            RunLiveAutoCommit();
        }

        /// <summary><c>async void</c> on purpose: fire-and-forget off a setter;
        /// never throws past its own catch.</summary>
        private async void RunLiveAutoCommit()
        {
            try
            {
                await LiveAutoCommitDelay(LiveAutoCommitDelaySeconds);
                if (loader is not NeoSaveSynchronizer synchronizer) return;
                if (!synchronizer.IsLiveSessionActive) return;
                // No unlinked-values warning: transient factory values mid-action
                // are normal between explicit saves, and the auto-commit cadence
                // would turn the hint into spam.
                await CommitCoreAsync(
                    replaceSnapshot: false,
                    warnUnlinked: false,
                    flushLiveImmediately: false);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning(
                    "[NeoCompose] Live auto-save failed; the change is still staged locally " +
                    $"and retries on the next save-value write. " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                liveAutoCommitScheduled = false;
            }
        }

        private static async Awaitable DefaultLiveAutoCommitDelay(double seconds)
        {
            await Task.Delay(System.TimeSpan.FromSeconds(seconds));
        }

        internal void EnsureNotDisposed()
        {
            if (isDisposed)
            {
                throw new System.ObjectDisposedException(nameof(NeoClient));
            }
        }

        internal System.IDisposable RegisterDialogue(NeoDialogue dialogue)
        {
            EnsureNotDisposed();
            activeDialogues.Add(dialogue);
            return new NeoDisposableAction(() => activeDialogues.Remove(dialogue));
        }

        protected ProjectData data;
        internal NeoInternalRecordRelationGraph InternalRecordRelations { get; private set; } = null!;
        private IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>?
            generatedReadOnlyClassFactories;
        private IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>?
            generatedWritableClassFactories;
        protected ProjectSaveData saveData;
        protected ProjectSaveData sessionData;
        private readonly INeoSaveLoader loader;
        private INeoLiveContentSource? liveContentSource;
        private JObject? committedSaveState;
        private JToken? committedSaveSemanticState;

        /// <summary>
        /// The origin of the change currently being applied; change handlers
        /// read this when dispatching so subscribers can tell local writes
        /// apart from externally-applied content.
        /// </summary>
        internal NeoChangeSource CurrentChangeSource { get; private set; } = NeoChangeSource.Local;
        public NeoSaveOptions SaveOptions { get; }
        internal NeoAssetDatabase? assetDatabase;
        private IReadOnlyDictionary<string, NeoNativeFunctionInvoker>? nativeFunctionInvokers;
        private IReadOnlyDictionary<string, NeoDeferredNativeFunctionInvoker>? deferredNativeFunctionInvokers;
        private static readonly object saveNameRandomLock = new();
        private static readonly System.Random saveNameRandom = new();
        private static readonly string[] saveNameAdjectives =
        {
            "wandering",
            "enduring",
            "brave",
            "curious",
            "lucky",
            "moonlit",
            "gentle",
            "nimble",
            "steadfast",
            "bright",
            "hidden",
            "wild",
        };
        private static readonly string[] saveNameNouns =
        {
            "cat",
            "mouse",
            "fox",
            "sparrow",
            "otter",
            "lantern",
            "comet",
            "river",
            "meadow",
            "acorn",
            "button",
            "cloud",
        };

        /// <summary>
        /// Constructs a client over an <see cref="INeoSaveLoader"/> (normally a
        /// <see cref="NeoSaveSynchronizer"/>). The project schema comes from
        /// <see cref="INeoSaveLoader.Schema"/>; <paramref name="loadedSaveContent"/> is
        /// the already-resolved active-save JSON (or null for a brand-new draft, in
        /// which case the client builds save defaults from the schema and persists
        /// nothing until the first <see cref="CommitAsync"/>). Use
        /// <see cref="NeoLoader.Load"/> to resolve the content asynchronously before
        /// constructing.
        /// </summary>
        public NeoClient(
            INeoSaveLoader loader,
            string? loadedSaveContent = null,
            NeoAssetDatabase? assetDatabase = null,
            NeoLocalization? localization = null,
            NeoSaveOptions? saveOptions = null)
        {
            this.loader = loader ?? throw new System.ArgumentNullException(nameof(loader));
            this.data = loader.Schema;
            ValidateExportSchemaVersion(data.metadata);
            ValidateClassMemberPayload(data);
            NormalizeClassSchemas();
            ValidateReadOnlyMembers();
            ValidateInternalRecordRelations(data);
            InternalRecordRelations = new NeoInternalRecordRelationGraph(data);
            ValidateNoLegacyTileGridContents(data);
            AdoptStampedMainValueRows();
            SaveOptions = saveOptions ?? new NeoSaveOptions();
            this.assetDatabase = assetDatabase;
            Localization = localization ?? NeoLocalization.CreateEmpty(data.localization);
            ValidateRootClassMember(data.project.rootAssetsMemberId, nameof(Project.rootAssetsMemberId));
            ValidateRootClassMember(data.project.rootSaveFileMemberId, nameof(Project.rootSaveFileMemberId));
            ValidateRootClassMember(data.project.rootSessionMemberId, nameof(Project.rootSessionMemberId));
            ValidateCallableMembers();
            ValidateConstructorRecords();
            bool loadedExistingSave = LoadSaveDataOrDefault(loadedSaveContent);
            sessionData = BuildDefaultSessionData();
            BuildMembershipIndex();
            BuildAuthoredOwnershipMap();
            RemoveRecoveredReadOnlySaveValues();
            InitializeSaveDefaults();
            InitializeSessionDefaults();
            assets = new(this, data.project.rootAssetsMemberId, null);
            save = new(this, data.project.rootSaveFileMemberId, null, NeoValueOwnership.Save);
            session = new(this, data.project.rootSessionMemberId, null, NeoValueOwnership.Session);
            InitializeVirtualInstanceValues();
            NeoAnimationCompiler.ValidateProject(this);
            if (loadedExistingSave)
            {
                CaptureCommittedSaveState();
            }
            if (loader is INeoLiveContentSource liveSource)
            {
                liveContentSource = liveSource;
                liveSource.OnLiveContentChanged += HandleLiveContentChanged;
            }
            activeClients.Add(this);
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            activeClients.Remove(this);
            if (liveContentSource != null)
            {
                liveContentSource.OnLiveContentChanged -= HandleLiveContentChanged;
                liveContentSource = null;
            }
            foreach (var dialogue in new List<NeoDialogue>(activeDialogues))
            {
                dialogue.DisposeFromClient();
            }
            activeDialogues.Clear();
            NeoDeferredFunctionBase[] directDeferredFunctions;
            lock (activeDirectDeferredFunctionsLock)
            {
                directDeferredFunctions = new NeoDeferredFunctionBase[activeDirectDeferredFunctions.Count];
                activeDirectDeferredFunctions.CopyTo(directDeferredFunctions);
                activeDirectDeferredFunctions.Clear();
            }
            foreach (NeoDeferredFunctionBase deferred in directDeferredFunctions)
            {
                deferred.DisposeFromOwner("NeoClient disposed");
            }
            lock (resolvedNSFunctionsLock)
            {
                resolvedNSFunctions.Clear();
            }
            animationCoordinator.Dispose();
            animationClips.Clear();
            // A variant handle is minted against this client's records, so it
            // is dropped on the same schedule a recompile drops clips.
            variantHandles.Clear();
            DisposeAnimationDefinitions();
            reportedAnimationChildSkips.Clear();
            reportedAnimationApplySkips.Clear();
            assets.Dispose();
            save.Dispose();
            session.Dispose();
            OnSaveValueChanged = null;
            OnWritableValueChanged = null;
            OnStaticBindingChanged = null;
        }

        internal bool TryGetMember<TMember>(string id, [NotNullWhen(true)] out TMember? member) where TMember : Member
        {
            if (data.members.TryGetValue(id, out Member idMatch))
            {
                if (idMatch is TMember match)
                {
                    member = match;
                    return true;
                }
            }
            member = null;
            return false;
        }

        internal bool TryGetClass(string id, [NotNullWhen(true)] out NeoSchemaClass? schemaClass)
        {
            if (data.classes.TryGetValue(id, out NeoSchemaClass idMatch))
            {
                schemaClass = idMatch;
                return true;
            }
            schemaClass = null;
            return false;
        }

        /// <summary>
        /// P43 §6.2 — resolves a declared constructor record by id.
        /// </summary>
        internal bool TryGetConstructor(
            string id,
            [NotNullWhen(true)] out ConstructorRecord? record)
        {
            if (data.constructors is not null
                && data.constructors.TryGetValue(id, out ConstructorRecord idMatch))
            {
                record = idMatch;
                return true;
            }
            record = null;
            return false;
        }

        /// <summary>
        /// P67 §9 — resolves a variant record by id.
        /// </summary>
        internal bool TryGetVariant(
            string id,
            [NotNullWhen(true)] out VariantRecord? record)
        {
            if (data.variants is not null
                && data.variants.TryGetValue(id, out VariantRecord idMatch))
            {
                record = idMatch;
                return true;
            }
            record = null;
            return false;
        }

        internal bool TryGetInterface(
            string id,
            [NotNullWhen(true)] out Interface? declaration)
        {
            if (data.interfaces.TryGetValue(id, out Interface idMatch))
            {
                declaration = idMatch;
                return true;
            }
            declaration = null;
            return false;
        }

        internal bool TryGetValue<TValue>(string id, [NotNullWhen(true)] out TValue? value) where TValue : MemberValue
        {
            if (sessionData.values.TryGetValue(id, out MemberValue sessionIdMatch))
            {
                if (sessionIdMatch is TValue match)
                {
                    value = match;
                    return true;
                }
            }
            if (saveData.values.TryGetValue(id, out MemberValue saveIdMatch))
            {
                if (saveIdMatch is TValue match)
                {
                    value = match;
                    return true;
                }
            }
            if (data.values.TryGetValue(id, out MemberValue idMatch))
            {
                if (idMatch is TValue match)
                {
                    value = match;
                    return true;
                }
            }
            if (virtualValues.TryGetValue(id, out MemberValue virtualMatch)
                && virtualMatch is TValue typedVirtual)
            {
                value = typedVirtual;
                return true;
            }
            value = null;
            return false;
        }

        internal bool TryGetValue<TValue>(
            NeoValueOwnership ownership,
            string id,
            [NotNullWhen(true)] out TValue? value) where TValue : MemberValue
        {
            value = null;
            switch (ownership)
            {
                case NeoValueOwnership.Session:
                    if (sessionData.values.TryGetValue(id, out MemberValue sessionMatch))
                    {
                        if (sessionMatch is TValue typedSession)
                        {
                            value = typedSession;
                            return true;
                        }
                        return false;
                    }
                    break;
                case NeoValueOwnership.Save:
                    if (saveData.values.TryGetValue(id, out MemberValue saveMatch))
                    {
                        if (saveMatch is TValue typedSave)
                        {
                            value = typedSave;
                            return true;
                        }
                        return false;
                    }
                    break;
                case NeoValueOwnership.Asset:
                    break;
                default:
                    throw new System.InvalidOperationException(
                        $"Unknown value ownership '{ownership}'.");
            }
            if (data.values.TryGetValue(id, out MemberValue assetMatch)
                && assetMatch is TValue typedAsset)
            {
                value = typedAsset;
                return true;
            }
            if (virtualValues.TryGetValue(id, out MemberValue virtualMatch)
                && virtualMatch is TValue typedVirtual)
            {
                value = typedVirtual;
                return true;
            }
            return false;
        }

        // Authored ownership is the per-value effective storage
        // (specs/member-storage.md §2): every authored value id maps to the
        // writable graph it belongs to, resolved positionally from the three
        // stamped roots with each member's declared storage (resolved
        // through the extends chain) overriding the inherited context. A
        // *sparse* overlay can therefore answer "is this value writable
        // here?" before the value has been shadowed into the writable store
        // (runtime-minted ids are caught by store membership below). Only
        // Save/Session entries are recorded — Immutable-effective values fall
        // through to the Asset default.
        private readonly Dictionary<string, NeoValueOwnership> authoredOwnership = new();

        private void BuildAuthoredOwnershipMap()
        {
            var visited = new HashSet<string>();
            MarkAuthoredOwnership(data.project.rootAssetsMemberId, NeoValueOwnership.Asset, visited);
            MarkAuthoredOwnership(data.project.rootSaveFileMemberId, NeoValueOwnership.Save, visited);
            MarkAuthoredOwnership(data.project.rootSessionMemberId, NeoValueOwnership.Session, visited);
            foreach (Member member in data.members.Values)
            {
                if (member.valueId is null) continue;
                if (member.isStatic)
                {
                    WalkAuthoredOwnership(
                        member.valueId,
                        member,
                        ResolveStaticOwnership(member),
                        visited);
                    continue;
                }

                // A schema member's authored default can be absent from its
                // containing object row and therefore unreachable from the
                // three root maps above. Walk it as an Asset-context root so
                // explicit member storage and concrete Class storage
                // can establish a fixed writable subtree. Pure inherited
                // defaults remain Asset here and are still classified from
                // their actual Save/Session placement when that path exists.
                WalkAuthoredOwnership(
                    member.valueId,
                    member,
                    NeoValueOwnership.Asset,
                    visited);
            }
        }

        private void MarkAuthoredOwnership(
            string rootMemberId,
            NeoValueOwnership ownership,
            HashSet<string> visited)
        {
            if (!data.members.TryGetValue(rootMemberId, out Member rootMember)
                || rootMember.valueId is null)
            {
                return;
            }
            WalkAuthoredOwnership(rootMember.valueId, rootMember, ownership, visited);
        }

        private void WalkAuthoredOwnership(
            string valueId,
            Member? member,
            NeoValueOwnership inherited,
            HashSet<string> visited)
        {
            NeoValueOwnership effective =
                (member is null ? null : DeclaredOwnership(member)) ?? inherited;
            if (!data.values.TryGetValue(valueId, out MemberValue row)) return;
            if (row is ObjectMemberValue obj
                && obj.classId is string runtimeClassId
                && TryResolveSchemaClassAllowedOwnership(runtimeClassId, out NeoValueOwnership typeOwnership))
            {
                effective = typeOwnership;
            }
            string visitKey = $"{effective}:{member?.id ?? ""}:{valueId}";
            if (!visited.Add(visitKey)) return;
            if (effective != NeoValueOwnership.Asset)
            {
                authoredOwnership[valueId] = effective;
            }
            foreach (var child in EnumerateOwnedChildLinks(row, member))
            {
                WalkAuthoredOwnership(child.valueId, child.member, effective, visited);
            }
        }

        internal bool TryResolveSchemaClassAllowedOwnership(
            string classId,
            out NeoValueOwnership ownership)
        {
            foreach (NeoSchemaClass schemaClass in ResolveClassInheritanceChain(classId))
            {
                NeoValueOwnership? declared = NeoMemberStorageResolution.ToOwnership(
                    NeoMemberStorageResolution.Parse(schemaClass.allowedStorage));
                if (declared is not null)
                {
                    ownership = declared.Value;
                    return true;
                }
            }
            ownership = NeoValueOwnership.Asset;
            return false;
        }

        /// <summary>
        /// Declared storage of a member resolved through its
        /// <see cref="Member.extendsMemberId"/> chain (capped) —
        /// mirrors the TS-side <c>declaredMemberStorage</c>.
        /// </summary>
        internal NeoMemberStorage DeclaredStorage(Member member)
        {
            string? storage = NeoSchemaClassInheritance.WalkExtendsMemberChain(
                member,
                id => data.members.TryGetValue(id, out Member? value) ? value : null,
                current => NeoMemberStorageResolution.Parse(current.storage)
                    == NeoMemberStorage.Inherit
                        ? null
                        : current.storage);
            return NeoMemberStorageResolution.Parse(storage);
        }

        /// <summary>
        /// Ownership a declared storage stamp forces, or null when the
        /// member inherits its placement parent's ownership.
        /// </summary>
        internal NeoValueOwnership? DeclaredOwnership(Member member)
        {
            return NeoMemberStorageResolution.ToOwnership(DeclaredStorage(member));
        }

        /// <summary>
        /// Resolves a member's canonical storage-partition declaration
        /// through its override chain. Unlike runtime storage ownership, an
        /// explicit <c>"inherit"</c> is itself a declaration and therefore
        /// stops override-chain lookup.
        /// </summary>
        internal string DeclaredStorageKey(Member member)
        {
            string? storageKey = NeoSchemaClassInheritance.WalkExtendsMemberChain(
                member,
                id => data.members.TryGetValue(id, out Member? value) ? value : null,
                current => current.storageKey);
            return storageKey is null
                ? "inherit"
                : NormalizeStorageKey(storageKey);
        }

        /// <summary>
        /// Resolves the partition stamp for a newly created value at one
        /// schema placement boundary. <c>$parentClass</c> uses the concrete
        /// runtime class of the placement parent; static members pass their
        /// declaring class as that parent.
        /// </summary>
        internal string? ResolveCreatedValueMapKey(
            Member member,
            string? parentMapKey,
            string? parentClassId)
        {
            if (member.extendsMemberId is null
                && member.storageKey is null)
            {
                return NormalizeMapKey(parentMapKey);
            }
            string declaration = DeclaredStorageKey(member);
            if (declaration == "inherit") return NormalizeMapKey(parentMapKey);
            if (declaration == "main") return null;
            const string parentClassToken = "$parentClass";
            if (declaration.Contains(parentClassToken))
            {
                if (string.IsNullOrEmpty(parentClassId))
                {
                    throw new System.InvalidOperationException(
                        $"Storage key '{declaration}' on member '{member.name}' references {parentClassToken}, but its placement parent has no runtime classId.");
                }
                declaration = declaration.Replace(parentClassToken, parentClassId);
            }
            return NormalizeMapKey(declaration);
        }

        internal string? ResolveStaticMapKey(Member member)
        {
            if (!member.isStatic)
            {
                throw new System.InvalidOperationException(
                    $"Member '{member.id}' is not a static Class member.");
            }
            SchemaPlacement? placement = NeoSchemaClassInheritance.FindSchemaPlacement(
                member.id,
                data.classes.Values);
            if (placement is null)
            {
                throw new System.InvalidOperationException(
                    $"Static member '{member.id}' has no declaring Class.");
            }
            return ResolveCreatedValueMapKey(
                member,
                parentMapKey: null,
                parentClassId: placement.ownerClass.id);
        }

        private static string NormalizeStorageKey(string? declaration)
        {
            if (string.IsNullOrEmpty(declaration)) return "inherit";
            if (declaration == "all")
            {
                throw new System.InvalidOperationException(
                    "Storage key 'all' is reserved for partition-scoped reads and cannot be declared on a member.");
            }
            return declaration!;
        }

        private static string? NormalizeMapKey(string? mapKey)
        {
            return string.IsNullOrEmpty(mapKey) || mapKey == "main"
                ? null
                : mapKey;
        }

        /// <summary>
        /// Resolves the independent storage anchor of a class-owned member.
        /// Unlike an instance field, a static declaration has no placement
        /// parent: explicit member storage wins, then the declaring class's
        /// inherited <c>allowedStorage</c>, and an unconstrained class defaults
        /// to Session.
        /// </summary>
        internal NeoValueOwnership ResolveStaticOwnership(Member member)
        {
            if (!member.isStatic)
            {
                throw new System.InvalidOperationException(
                    $"Member '{member.id}' is not a static Class member.");
            }
            if (DeclaredOwnership(member) is NeoValueOwnership declared)
            {
                return declared;
            }

            SchemaPlacement? placement = NeoSchemaClassInheritance.FindSchemaPlacement(
                member.id,
                data.classes.Values);
            if (placement is null)
            {
                throw new System.InvalidOperationException(
                    $"Static member '{member.id}' has no declaring Class.");
            }
            if (TryResolveSchemaClassAllowedOwnership(
                    placement.ownerClass.id,
                    out NeoValueOwnership allowed))
            {
                return allowed;
            }
            return NeoValueOwnership.Session;
        }

        internal NeoValueOwnership ResolveStaticOwnership(string memberId)
        {
            if (!TryGetMember(memberId, out Member? member))
            {
                throw new System.ArgumentException(
                    $"No member exists for static binding '{memberId}'.",
                    nameof(memberId));
            }
            return ResolveStaticOwnership(member);
        }

        /// <summary>
        /// Resolves a static member's active target. Missing overlay entries
        /// inherit <see cref="Member.valueId"/>; a present null entry is an
        /// explicit unset tombstone.
        /// </summary>
        internal bool TryResolveStaticBinding(
            string memberId,
            [NotNullWhen(true)] out Member? member,
            out NeoValueOwnership ownership,
            [NotNullWhen(true)] out string? valueId)
        {
            if (!TryGetMember(memberId, out member))
            {
                ownership = NeoValueOwnership.Asset;
                valueId = null;
                return false;
            }
            ownership = ResolveStaticOwnership(member);
            if (ownership == NeoValueOwnership.Asset)
            {
                valueId = member.valueId;
                return valueId is not null;
            }
            Dictionary<string, string?> bindings = GetWritableStore(ownership).staticBindings;
            if (bindings.TryGetValue(member.id, out string? overlaid))
            {
                valueId = overlaid;
                return valueId is not null;
            }
            valueId = member.valueId;
            return valueId is not null;
        }

        internal void SetStaticBinding(
            string memberId,
            NeoValueOwnership ownership,
            string? valueId)
        {
            if (!TryGetMember(memberId, out Member? member))
            {
                throw new System.ArgumentException(
                    $"No member exists for static binding '{memberId}'.",
                    nameof(memberId));
            }
            NeoValueOwnership resolvedOwnership = ResolveStaticOwnership(member);
            if (resolvedOwnership == NeoValueOwnership.Asset)
            {
                throw new System.InvalidOperationException(
                    $"Static member '{member.name}' is Immutable and cannot be rebound at runtime.");
            }
            if (ownership != resolvedOwnership)
            {
                throw new System.InvalidOperationException(
                    $"Static member '{member.name}' belongs to {resolvedOwnership} storage, not {ownership}.");
            }
            if (valueId is null && member.required)
            {
                throw new System.ArgumentNullException(
                    nameof(valueId),
                    $"Required static member '{member.name}' cannot be cleared.");
            }

            ProjectSaveData store = GetWritableStore(ownership);
            if (store.staticBindings.TryGetValue(memberId, out string? current)
                && current == valueId)
            {
                return;
            }
            store.staticBindings[memberId] = valueId;
            TouchWritableStoreUpdatedAt(ownership);
            OnStaticBindingChanged?.Invoke(ownership, memberId);
            if (ownership == NeoValueOwnership.Save)
            {
                if (loader is NeoSaveSynchronizer synchronizer)
                {
                    synchronizer.MarkDirtyStaticBinding(memberId);
                }
                ScheduleLiveAutoCommit();
            }
        }

        internal bool RestoreStaticBinding(
            string memberId,
            NeoValueOwnership ownership)
        {
            if (!TryGetMember(memberId, out Member? member))
            {
                throw new System.ArgumentException(
                    $"No member exists for static binding '{memberId}'.",
                    nameof(memberId));
            }
            NeoValueOwnership resolvedOwnership = ResolveStaticOwnership(member);
            if (resolvedOwnership == NeoValueOwnership.Asset)
            {
                return false;
            }
            if (ownership != resolvedOwnership)
            {
                throw new System.InvalidOperationException(
                    $"Static member '{member.name}' belongs to {resolvedOwnership} storage, not {ownership}.");
            }
            ProjectSaveData store = GetWritableStore(ownership);
            if (!store.staticBindings.Remove(memberId)) return false;
            TouchWritableStoreUpdatedAt(ownership);
            OnStaticBindingChanged?.Invoke(ownership, memberId);
            if (ownership == NeoValueOwnership.Save)
            {
                if (loader is NeoSaveSynchronizer synchronizer)
                {
                    synchronizer.MarkDirtyStaticBinding(memberId);
                }
                ScheduleLiveAutoCommit();
            }
            return true;
        }

        private static void ValidateExportSchemaVersion(ProjectExportMetadata? metadata)
        {
            // P61 §3 / §4 — 16 materializes every instance initializer at
            // the push trust boundary. A 15 export may still carry an
            // unmaterialized instance row whose `init` this runtime would
            // incorrectly treat like declaration code, so it fails closed
            // rather than constructing a second, divergent graph in-game.
            const int currentVersion =
                NeoProjectExportContract.CurrentSchemaVersion;
            if (metadata is null)
            {
                throw new System.InvalidOperationException(
                    $"Project export metadata is missing (this SDK requires schema version {currentVersion}). Re-export the project from the current web app.");
            }
            if (metadata.schemaVersion != currentVersion)
            {
                string action = metadata.schemaVersion < currentVersion
                    ? "Re-export the project from the current web app."
                    : "Update the NeoCompose SDK.";
                throw new System.InvalidOperationException(
                    $"Project export schema version {metadata.schemaVersion} is unsupported; this SDK accepts only schema version {currentVersion}. Older releases must be upgraded through the supported release-data migration boundary before loading. {action}");
            }
        }

        private static void ValidateInternalRecordRelations(ProjectData data)
        {
            if (data.internalRecordRelations is null)
            {
                throw new System.InvalidOperationException(
                    $"Project export schema version {NeoProjectExportContract.CurrentSchemaVersion} is missing the required 'internalRecordRelations' collection. Re-export the project from the current web app.");
            }

            var knownKinds = new HashSet<string>(System.StringComparer.Ordinal)
            {
                InternalRecordRelationKinds.WorldGridTileImport,
                InternalRecordRelationKinds.WorldGridObjectImport,
                InternalRecordRelationKinds.WorldGridTileLayer,
                InternalRecordRelationKinds.WorldGridObjectLayer,
                InternalRecordRelationKinds.WorldTileCompatibleLayer,
                InternalRecordRelationKinds.WorldTileDefaultLayer,
                InternalRecordRelationKinds.WorldObjectCompatibleLayer,
                InternalRecordRelationKinds.WorldObjectDefaultLayer,
                InternalRecordRelationKinds.WorldTileLayerLinkTarget,
                InternalRecordRelationKinds.WorldObjectLayerLinkTarget,
                InternalRecordRelationKinds.WorldSmartTileNeighborTile,
            };
            var edgeKeys = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var pair in data.internalRecordRelations)
            {
                InternalRecordRelation relation = pair.Value
                    ?? throw new System.InvalidOperationException(
                        $"Internal record relation '{pair.Key}' is null.");
                if (!string.Equals(pair.Key, relation.id, System.StringComparison.Ordinal))
                {
                    throw new System.InvalidOperationException(
                        $"Internal record relation dictionary key '{pair.Key}' does not match row id '{relation.id}'.");
                }
                if (!knownKinds.Contains(relation.relationKind))
                {
                    throw new System.InvalidOperationException(
                        $"Internal record relation '{relation.id}' uses unsupported relation kind '{relation.relationKind}'. Update the NeoCompose SDK.");
                }
                bool smartTileNeighbor = relation.relationKind
                    == InternalRecordRelationKinds.WorldSmartTileNeighborTile;
                string expectedSourceKind = smartTileNeighbor ? "value" : "class";
                if (!string.Equals(
                    relation.sourceRecordKind,
                    expectedSourceKind,
                    System.StringComparison.Ordinal))
                {
                    throw new System.InvalidOperationException(
                        $"World relation '{relation.id}' has unsupported source record kind '{relation.sourceRecordKind}'.");
                }
                if (!string.Equals(relation.targetRecordKind, "class", System.StringComparison.Ordinal))
                {
                    throw new System.InvalidOperationException(
                        $"World relation '{relation.id}' has unsupported target record kind '{relation.targetRecordKind}'.");
                }
                bool sourceExists = smartTileNeighbor
                    ? data.values.ContainsKey(relation.sourceRecordId)
                    : data.classes.ContainsKey(relation.sourceRecordId);
                if (!sourceExists)
                {
                    throw new System.InvalidOperationException(
                        $"Internal record relation '{relation.id}' references missing source {expectedSourceKind} '{relation.sourceRecordId}'.");
                }
                if (!data.classes.ContainsKey(relation.targetRecordId))
                {
                    throw new System.InvalidOperationException(
                        $"Internal record relation '{relation.id}' references missing target class '{relation.targetRecordId}'.");
                }
                bool ordered =
                    relation.relationKind == InternalRecordRelationKinds.WorldGridTileLayer
                    || relation.relationKind == InternalRecordRelationKinds.WorldGridObjectLayer;
                if (ordered && string.IsNullOrEmpty(relation.orderKey))
                {
                    throw new System.InvalidOperationException(
                        $"Ordered internal record relation '{relation.id}' is missing orderKey.");
                }
                if (!ordered && relation.orderKey is not null)
                {
                    throw new System.InvalidOperationException(
                        $"Unordered internal record relation '{relation.id}' must not define orderKey.");
                }
                string edgeKey = string.Join("\n", new[]
                {
                    relation.relationKind,
                    relation.sourceRecordKind,
                    relation.sourceRecordId,
                    relation.targetRecordKind,
                    relation.targetRecordId,
                });
                if (!edgeKeys.Add(edgeKey))
                {
                    throw new System.InvalidOperationException(
                        $"Internal record relation '{relation.id}' duplicates a live edge.");
                }
            }
        }

        private static void ValidateClassMemberPayload(ProjectData data)
        {
            if (data.classes is null)
            {
                throw new System.InvalidOperationException(
                    "Project export is missing the required 'classes' collection. Re-export the project with the schema-8 Class/Member contract.");
            }
            if (data.members is null)
            {
                throw new System.InvalidOperationException(
                    "Project export is missing the required 'members' collection. Re-export the project with the schema-8 Class/Member contract.");
            }
            if (data.values is null)
            {
                throw new System.InvalidOperationException(
                    "Project export is missing the required 'values' collection. Re-export the project with the schema-8 Class/Member contract.");
            }
        }

        internal IList<NeoSchemaClass> ResolveClassInheritanceChain(string classId)
        {
            if (!classInheritanceChains.TryGetValue(classId, out var chain))
            {
                chain = NeoSchemaClassInheritance.ResolveChain(
                    classId,
                    id => data.classes.TryGetValue(id, out NeoSchemaClass match)
                        ? match
                        : null);
                classInheritanceChains[classId] = chain;
            }
            return chain;
        }

        internal IList<MergedSchemaEntry> ResolveInstanceSurfaceSchema(string classId)
        {
            if (!instanceSurfaceSchemas.TryGetValue(classId, out var schema))
            {
                schema = NeoSchemaClassInheritance.MergeInstanceSurfaceSchema(
                    ResolveClassInheritanceChain(classId),
                    id => data.members.TryGetValue(id, out Member match)
                        ? match
                        : null);
                instanceSurfaceSchemas[classId] = schema;
            }
            return schema;
        }

        internal IList<MergedSchemaEntry> ResolveStoredInstanceSchema(string classId)
        {
            if (!storedInstanceSchemas.TryGetValue(classId, out var schema))
            {
                schema = NeoSchemaClassInheritance.MergeStoredInstanceSchema(
                    ResolveClassInheritanceChain(classId),
                    id => data.members.TryGetValue(id, out Member match)
                        ? match
                        : null);
                storedInstanceSchemas[classId] = schema;
            }
            return schema;
        }

        internal IList<MergedSchemaEntry> ResolveReadOnlyMemberSchema(string classId)
        {
            if (!readOnlyMemberSchemas.TryGetValue(classId, out var schema))
            {
                schema = NeoSchemaClassInheritance.MergeReadOnlyMembers(
                    ResolveClassInheritanceChain(classId),
                    id => data.members.TryGetValue(id, out Member match)
                        ? match
                        : null);
                readOnlyMemberSchemas[classId] = schema;
            }
            return schema;
        }

        /// <summary>
        /// Invalidates memoized inheritance/schema projections after an
        /// internal test or tooling seam mutates the otherwise read-mostly
        /// exported schema dictionaries in place.
        /// </summary>
        internal void InvalidateSchemaResolutionCaches()
        {
            classInheritanceChains.Clear();
            instanceSurfaceSchemas.Clear();
            storedInstanceSchemas.Clear();
            readOnlyMemberSchemas.Clear();
        }

        private void NormalizeClassSchemas()
        {
            foreach (NeoSchemaClass schemaClass in data.classes.Values)
            {
                // Minimal legacy fixtures treat an omitted schema as empty.
                schemaClass.schema ??= new Dictionary<string, string>();
            }
        }

        private void ValidateReadOnlyMembers()
        {
            if (!data.members.Values.Any(member => member.isReadOnly == true))
            {
                return;
            }

            BuildReadOnlyAuthoredValueContext();

            var placements = new Dictionary<string, List<(NeoSchemaClass owner, string key)>>();
            foreach (NeoSchemaClass schemaClass in data.classes.Values)
            {
                foreach (var entry in schemaClass.schema)
                {
                    if (!placements.TryGetValue(entry.Value, out var memberPlacements))
                    {
                        memberPlacements = new List<(NeoSchemaClass, string)>();
                        placements[entry.Value] = memberPlacements;
                    }
                    memberPlacements.Add((schemaClass, entry.Key));
                }
            }

            var effectivePlacements =
                new Dictionary<string, List<(NeoSchemaClass owner, string key)>>();
            foreach (NeoSchemaClass schemaClass in data.classes.Values)
            {
                foreach (MergedSchemaEntry entry in ResolveInstanceSurfaceSchema(schemaClass.id))
                {
                    if (!effectivePlacements.TryGetValue(entry.memberId, out var memberPlacements))
                    {
                        memberPlacements = new List<(NeoSchemaClass, string)>();
                        effectivePlacements[entry.memberId] = memberPlacements;
                    }
                    memberPlacements.Add((schemaClass, entry.schemaKey));
                }
            }

            var entryTemplateIds = new HashSet<string>();
            var genericBindingIds = new HashSet<string>();
            foreach (Member candidate in data.members.Values)
            {
                switch (candidate)
                {
                    case ListMember list:
                        entryTemplateIds.Add(list.entryMemberId);
                        break;
                    case DictionaryMember dictionary:
                        entryTemplateIds.Add(dictionary.entryMemberId);
                        break;
                    case ClassMember classMember when classMember.classArguments is not null:
                        foreach (GenericBinding binding in classMember.classArguments.Values)
                        {
                            if (!binding.IsForward && binding.memberId is not null)
                            {
                                genericBindingIds.Add(binding.memberId);
                            }
                        }
                        break;
                }
            }
            foreach (NeoSchemaClass schemaClass in data.classes.Values)
            {
                if (schemaClass.extendsGenericBindings is null) continue;
                foreach (GenericBinding binding in schemaClass.extendsGenericBindings.Values)
                {
                    if (!binding.IsForward && binding.memberId is not null)
                    {
                        genericBindingIds.Add(binding.memberId);
                    }
                }
            }

            foreach (Member declaration in data.members.Values)
            {
                if (declaration.isReadOnly != true) continue;
                string subject = $"Read-only member '{declaration.name}' ({declaration.id})";
                if (!placements.TryGetValue(declaration.id, out var memberPlacements)
                    || memberPlacements.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"{subject} is not placed directly in a Class schema.");
                }
                if (declaration.id == data.project.rootAssetsMemberId
                    || declaration.id == data.project.rootSaveFileMemberId
                    || declaration.id == data.project.rootSessionMemberId
                    || entryTemplateIds.Contains(declaration.id)
                    || genericBindingIds.Contains(declaration.id))
                {
                    throw new InvalidOperationException(
                        $"{subject} has a non-Class placement; read-only is valid only on concrete Class fields.");
                }
                if (declaration.isStatic)
                {
                    throw new InvalidOperationException(
                        $"{subject} cannot be static.");
                }
                if (ResolveDeclaredStorage(declaration) != NeoMemberStorage.Immutable)
                {
                    throw new InvalidOperationException(
                        $"{subject} must declare resolved Immutable storage.");
                }
                if (declaration.valueId is not null)
                {
                    throw new InvalidOperationException(
                        $"{subject} cannot have a member-owned valueId binding.");
                }
                if (declaration is ListMember indexed
                    && indexed.indexes is { Length: > 0 })
                {
                    throw new InvalidOperationException(
                        $"{subject} cannot declare per-instance List indexes.");
                }
                if (declaration is StringMember searchable
                    && searchable.searchKey == true)
                {
                    throw new InvalidOperationException(
                        $"{subject} cannot opt into the per-instance String search index.");
                }

                if (!effectivePlacements.TryGetValue(
                    declaration.id,
                    out var declarationPlacements))
                {
                    declarationPlacements = memberPlacements;
                }

                bool isAbstract = declaration.isAbstract == true;
                if (isAbstract && HasExplicitDefaultValue(declaration))
                {
                    throw new InvalidOperationException(
                        $"{subject} is an abstract getter contract and cannot declare a defaultValue.");
                }

                foreach (var placement in declarationPlacements)
                {
                    Member resolved = declaration;
                    if (declaration is GenericMember genericDeclaration)
                    {
                        var env = NeoGenericResolution.ResolveEnv(
                            ResolveClassInheritanceChain(placement.owner.id));
                        if (env.TryGetValue(
                                genericDeclaration.genericParamId,
                                out NeoGenericEnvEntry entry)
                            && !entry.IsBound)
                        {
                            // Open generic classes are not constructible. Their
                            // closed descendants appear separately in the
                            // effective-placement map and are checked there.
                            continue;
                        }
                        resolved = NeoGenericResolution.SubstituteMember(
                            this,
                            declaration,
                            env);
                    }
                    if (!IsReadOnlyValueBearing(resolved))
                    {
                        throw new InvalidOperationException(
                            $"{subject} at Class '{placement.owner.name}' key '{placement.key}' is not value-bearing.");
                    }
                    if (isAbstract)
                    {
                        // Abstract read-only members are getter contracts. A
                        // Generic slot still must close to a value-bearing kind,
                        // but neither the slot nor its binding supplies this
                        // declaration's concrete default graph.
                        continue;
                    }
                    if (!HasExplicitDefaultValue(resolved))
                    {
                        throw new InvalidOperationException(
                            $"{subject} at Class '{placement.owner.name}' key '{placement.key}' requires an explicit defaultValue.");
                    }
                    ValidateReadOnlyOwnedSchema(resolved, subject, new HashSet<string>());
                    ValidateReadOnlyLookupDefault(resolved, subject);
                }
            }

            ValidateReadOnlyAbstractContracts();

            foreach (Member declaration in data.members.Values)
            {
                if (declaration is not ClassMember classMember
                    || classMember.defaultValue?.value is null)
                {
                    continue;
                }
                ValidateReadOnlyInstanceObject(
                    classMember.defaultValue.classId ?? classMember.classId,
                    classMember.defaultValue.value.Keys,
                    $"defaultValue:{classMember.id}",
                    "declaration default");
            }

            ValidateReadOnlyInstanceKeys(
                readOnlyAuthoredRows,
                readOnlyAuthoredClassIds,
                "project export");
        }

        private void ValidateReadOnlyAbstractContracts()
        {
            foreach (Member member in data.members.Values)
            {
                if (string.IsNullOrEmpty(member.extendsMemberId)) continue;

                Member? abstractContract = null;
                NeoSchemaClassInheritance.WalkExtendsMemberChain(
                    member,
                    id => data.members.TryGetValue(id, out Member? value) ? value : null,
                    current =>
                    {
                        if (current.id == member.id || current.isAbstract != true)
                        {
                            return null;
                        }
                        abstractContract = current;
                        return current.id;
                    });
                if (abstractContract is null) continue;

                if (abstractContract.isReadOnly == true
                    && member.isReadOnly != true)
                {
                    throw new InvalidOperationException(
                        $"Member '{member.name}' ({member.id}) cannot implement abstract read-only member '{abstractContract.name}' ({abstractContract.id}) with a non-read-only, instance-backed override.");
                }

                if (member.isReadOnly == true
                    && abstractContract.isReadOnly != true
                    && !IsGetterOnlyAbstractContract(abstractContract))
                {
                    throw new InvalidOperationException(
                        $"Read-only member '{member.name}' ({member.id}) cannot implement setter-required abstract member '{abstractContract.name}' ({abstractContract.id}).");
                }
            }

            foreach (NeoSchemaClass schemaClass in data.classes.Values)
            {
                IList<MergedSchemaEntry> surface =
                    ResolveInstanceSurfaceSchema(schemaClass.id);
                if (!schemaClass.isAbstract)
                {
                    foreach (MergedSchemaEntry entry in surface)
                    {
                        if (!data.members.TryGetValue(entry.memberId, out Member? member)
                            || member.isAbstract != true
                            || member.isReadOnly != true)
                        {
                            continue;
                        }
                        throw new InvalidOperationException(
                            $"Concrete Class '{schemaClass.name}' ({schemaClass.id}) does not implement abstract read-only member '{member.name}' ({member.id}). Add a concrete read-only override with a defaultValue.");
                    }
                }

                ValidateReadOnlyInterfaceSetters(schemaClass, surface);
            }
        }

        private bool IsGetterOnlyAbstractContract(Member member)
        {
            if (ResolveDeclaredStorage(member) == NeoMemberStorage.Immutable)
            {
                return true;
            }
            return member is NSPropertyMember property
                && property.setterCode is null
                && property.setter is null;
        }

        private void ValidateReadOnlyInterfaceSetters(
            NeoSchemaClass schemaClass,
            IList<MergedSchemaEntry> surface)
        {
            var readOnlyKeys = new Dictionary<string, Member>();
            foreach (MergedSchemaEntry entry in surface)
            {
                if (data.members.TryGetValue(entry.memberId, out Member? member)
                    && member.isReadOnly == true)
                {
                    readOnlyKeys[entry.schemaKey] = member;
                }
            }
            if (readOnlyKeys.Count == 0) return;

            var visited = new HashSet<string>();
            foreach (NeoSchemaClass ancestor in ResolveClassInheritanceChain(schemaClass.id))
            {
                if (ancestor.implementsInterfaceIds is null) continue;
                foreach (string interfaceId in ancestor.implementsInterfaceIds)
                {
                    ValidateReadOnlyInterfaceSetters(
                        schemaClass,
                        interfaceId,
                        readOnlyKeys,
                        visited);
                }
            }
        }

        private void ValidateReadOnlyInterfaceSetters(
            NeoSchemaClass schemaClass,
            string interfaceId,
            IReadOnlyDictionary<string, Member> readOnlyKeys,
            HashSet<string> visited)
        {
            if (!visited.Add(interfaceId)
                || !data.interfaces.TryGetValue(interfaceId, out Interface? contract))
            {
                return;
            }
            if (contract.members is not null)
            {
                foreach (var pair in contract.members)
                {
                    if (pair.Value.kind != "property"
                        || pair.Value.settable != true
                        || !readOnlyKeys.TryGetValue(pair.Key, out Member? member))
                    {
                        continue;
                    }
                    throw new InvalidOperationException(
                        $"Read-only member '{member.name}' ({member.id}) on Class '{schemaClass.name}' cannot fulfill settable interface property '{pair.Key}' from Interface '{contract.name}' ({contract.id}).");
                }
            }
            if (contract.extendsInterfaceIds is null) return;
            foreach (string parentId in contract.extendsInterfaceIds)
            {
                ValidateReadOnlyInterfaceSetters(
                    schemaClass,
                    parentId,
                    readOnlyKeys,
                    visited);
            }
        }

        private NeoMemberStorage ResolveDeclaredStorage(Member member)
            => DeclaredStorage(member);

        private static bool IsReadOnlyValueBearing(Member member) =>
            member is not NSPropertyMember
            && member is not FunctionMember
            && member is not NSFunctionMember
            && member is not GenericMember;

        private static bool HasExplicitDefaultValue(Member member) => member switch
        {
            Member<object?> typed => typed.defaultValue is not null,
            BoolMember typed => typed.defaultValue is not null,
            IntMember typed => typed.defaultValue is not null,
            FloatMember typed => typed.defaultValue is not null,
            StringMember typed => typed.defaultValue is not null,
            DictionaryMember typed => typed.defaultValue is not null,
            ListMember typed => typed.defaultValue is not null,
            ClassMember typed => typed.defaultValue is not null,
            EnumMember typed => typed.defaultValue is not null,
            LookupMember typed => typed.defaultValue is not null,
            DialogueLookupMember typed => typed.defaultValue is not null,
            SpriteMember typed => typed.defaultValue is not null,
            AudioMember typed => typed.defaultValue is not null,
            Vector2Member typed => typed.defaultValue is not null,
            Vector2IntMember typed => typed.defaultValue is not null,
            Vector3Member typed => typed.defaultValue is not null,
            Vector3IntMember typed => typed.defaultValue is not null,
            ColorMember typed => typed.defaultValue is not null,
            DecimalMember typed => typed.defaultValue is not null,
            // P67 §6 — a defaulted variant member is settled, so it must stop
            // being demanded as a runtime constructor argument.
            VariantMember typed => typed.defaultValue is not null,
            _ => false,
        };

        internal MemberValue? CreateDeclarationDefaultValue(
            Member member,
            string syntheticId)
        {
            return MemberValueFactory.CreateFromDefault(
                member,
                syntheticId,
                member.createdAt,
                member.updatedAt);
        }

        private void ValidateReadOnlyLookupDefault(Member member, string subject)
        {
            if (member is not LookupMember lookup) return;
            ArrayMemberValue? defaultValue = CreateDeclarationDefaultValue(
                lookup,
                $"__neo_readonly_default_validation:{lookup.RuntimeDeclarationIdentity}")
                as ArrayMemberValue;
            string[] selections = defaultValue?.value ?? System.Array.Empty<string>();
            if (!lookup.multiselect && selections.Length > 1)
            {
                throw new InvalidOperationException(
                    $"{subject} defaultValue selects {selections.Length} Lookup entries, but the Lookup is single-select.");
            }
            if (lookup.collectionValueId?.StartsWith(
                    "__neo_readonly_default:",
                    System.StringComparison.Ordinal) == true)
            {
                throw new InvalidOperationException(
                    $"{subject} defaultValue references runtime-only synthetic Lookup collection value '{lookup.collectionValueId}'. Persisted project data must target an authored collection value.");
            }
            if (selections.Length == 0) return;

            if (!data.members.TryGetValue(lookup.collectionMemberId, out Member? collectionMember))
            {
                throw new InvalidOperationException(
                    $"{subject} defaultValue references missing Lookup collection member '{lookup.collectionMemberId}'.");
            }
            if (collectionMember is not ListMember && collectionMember is not DictionaryMember)
            {
                throw new InvalidOperationException(
                    $"{subject} defaultValue Lookup target '{lookup.collectionMemberId}' is not a List or Dictionary.");
            }

            string? collectionValueId = lookup.collectionValueId
                ?? ResolveAuthoredLookupCollectionValueId(collectionMember);
            if (collectionValueId?.StartsWith(
                    "__neo_readonly_default:",
                    System.StringComparison.Ordinal) == true)
            {
                throw new InvalidOperationException(
                    $"{subject} defaultValue references runtime-only synthetic Lookup collection value '{collectionValueId}'. Persisted project data must target an authored collection value.");
            }
            if (string.IsNullOrEmpty(collectionValueId)
                || !readOnlyAuthoredRows.TryGetValue(
                    collectionValueId!,
                    out MemberValue? collectionValue))
            {
                throw new InvalidOperationException(
                    $"{subject} defaultValue cannot resolve Lookup collection value '{collectionValueId ?? "<unbound>"}'.");
            }

            foreach (string selection in selections)
            {
                if (selection.StartsWith(
                        "__neo_readonly_default:",
                        System.StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{subject} defaultValue selects runtime-only synthetic Lookup value '{selection}'. Persisted project data must select an authored value row.");
                }
                bool selectable = collectionValue switch
                {
                    ArrayMemberValue array when array.value is not null =>
                        System.Array.IndexOf(array.value, selection) >= 0,
                    ObjectMemberValue obj when obj.value is not null =>
                        obj.value.ContainsValue(selection),
                    _ => false,
                };
                if (!selectable)
                {
                    throw new InvalidOperationException(
                        $"{subject} defaultValue selects Lookup value '{selection}', which is not present in collection '{collectionValueId}'.");
                }
                if (!readOnlyAuthoredRows.ContainsKey(selection))
                {
                    throw new InvalidOperationException(
                        $"{subject} defaultValue selects Lookup value '{selection}', but no persisted authored value row exists for that selection.");
                }
            }
        }

        private string? ResolveAuthoredLookupCollectionValueId(Member collectionMember)
        {
            if (!string.IsNullOrEmpty(collectionMember.valueId))
            {
                return collectionMember.valueId;
            }
            string? resolved = null;
            var matchCache = new Dictionary<string, bool>();
            bool MatchesCollectionDeclaration(string candidateMemberId)
            {
                if (matchCache.TryGetValue(candidateMemberId, out bool cached))
                {
                    return cached;
                }
                bool matches = NeoSchemaClassInheritance.WalkExtendsMemberChain(
                    candidateMemberId,
                    id => data.members.TryGetValue(id, out Member? value) ? value : null,
                    current => current.id == collectionMember.id ? current : null)
                    is not null;
                matchCache[candidateMemberId] = matches;
                return matches;
            }
            foreach (MemberValue row in readOnlyAuthoredRows.Values)
            {
                if (row is not ObjectMemberValue obj
                    || obj.value is null
                    || !readOnlyAuthoredClassIds.TryGetValue(
                        row.id,
                        out string? effectiveClassId))
                {
                    continue;
                }
                foreach (MergedSchemaEntry entry in ResolveInstanceSurfaceSchema(effectiveClassId))
                {
                    // A schema key is meaningful only within the row's
                    // effective runtime Class. Unrelated Classes may reuse
                    // the same key for a different member and must not make
                    // an otherwise unambiguous authored binding conflict.
                    if (!MatchesCollectionDeclaration(entry.memberId)
                        || !obj.value.TryGetValue(entry.schemaKey, out string candidate))
                    {
                        continue;
                    }
                    if (resolved is null) resolved = candidate;
                    else if (resolved != candidate) return null;
                }
            }
            return resolved;
        }

        private void ValidateReadOnlyOwnedSchema(
            Member member,
            string rootSubject,
            HashSet<string> visiting)
        {
            if (!visiting.Add(member.id)) return;
            try
            {
                if (member is ListMember list
                    && data.members.TryGetValue(list.entryMemberId, out Member entry))
                {
                    ValidateReadOnlyOwnedMember(entry, rootSubject, visiting);
                    return;
                }
                if (member is DictionaryMember dictionary
                    && data.members.TryGetValue(dictionary.entryMemberId, out entry))
                {
                    ValidateReadOnlyOwnedMember(entry, rootSubject, visiting);
                    return;
                }
                if (member is not ClassMember classMember) return;
                foreach (MergedSchemaEntry schemaEntry in
                    ResolveInstanceSurfaceSchema(classMember.classId))
                {
                    if (!data.members.TryGetValue(schemaEntry.memberId, out Member child)) continue;
                    ValidateReadOnlyOwnedMember(child, rootSubject, visiting);
                }
            }
            finally
            {
                visiting.Remove(member.id);
            }
        }

        private void ValidateReadOnlyOwnedMember(
            Member member,
            string rootSubject,
            HashSet<string> visiting)
        {
            NeoMemberStorage storage = ResolveDeclaredStorage(member);
            if (storage is NeoMemberStorage.Save or NeoMemberStorage.Session)
            {
                throw new InvalidOperationException(
                    $"{rootSubject} owns writable descendant member '{member.name}' ({member.id}); its complete default graph must be Immutable.");
            }
            // Lookup selections re-root at independently placed targets.
            if (member is LookupMember) return;
            ValidateReadOnlyOwnedSchema(member, rootSubject, visiting);
        }

        private void ValidateReadOnlyInstanceKeys(
            IReadOnlyDictionary<string, MemberValue> rows,
            IReadOnlyDictionary<string, string> effectiveClassIds,
            string source)
        {
            foreach (var pair in rows)
            {
                if (pair.Value is not ObjectMemberValue row) continue;
                string? effectiveClassId = row.classId;
                if (string.IsNullOrEmpty(effectiveClassId)
                    && effectiveClassIds.TryGetValue(pair.Key, out string? inferred))
                {
                    effectiveClassId = inferred;
                }
                ValidateReadOnlyInstanceObject(
                    effectiveClassId,
                    row.value?.Keys,
                    pair.Key,
                    source);
            }
        }

        private void BuildReadOnlyAuthoredValueContext()
        {
            var rows = new Dictionary<string, MemberValue>(data.values);
            if (data.valuePartitions is not null)
            {
                foreach (var partition in data.valuePartitions)
                {
                    if (partition.Value is not JObject partitionObject) continue;
                    Dictionary<string, MemberValue>? partitionRows =
                        partitionObject.ToObject<Dictionary<string, MemberValue>>();
                    if (partitionRows is null) continue;
                    foreach (var pair in partitionRows)
                    {
                        // LoadValuePartition owns the precise collision
                        // diagnostic. Validation needs one deterministic graph.
                        if (!rows.ContainsKey(pair.Key)) rows[pair.Key] = pair.Value;
                    }
                }
            }
            readOnlyAuthoredRows = rows;
            readOnlyAuthoredClassIds = BuildTrustedEffectiveClassIds(rows);
        }

        private void ReleaseReadOnlyAuthoredValueContext()
        {
            readOnlyAuthoredRows = new Dictionary<string, MemberValue>();
            readOnlyAuthoredClassIds = new Dictionary<string, string>();
        }

        private Dictionary<string, string> BuildTrustedEffectiveClassIds(
            IReadOnlyDictionary<string, MemberValue> rows,
            IReadOnlyDictionary<string, string?>? staticBindings = null,
            bool skipIncompatiblePlacements = false)
        {
            var effectiveClassIds = new Dictionary<string, string>();
            var incompatibleValueIds = new HashSet<string>();
            var visited = new HashSet<string>();
            var rowsByContainer = new Dictionary<string, List<MemberValue>>();
            foreach (MemberValue row in rows.Values)
            {
                if (string.IsNullOrEmpty(row.containerId)) continue;
                if (!rowsByContainer.TryGetValue(
                        row.containerId!,
                        out List<MemberValue>? members))
                {
                    members = new List<MemberValue>();
                    rowsByContainer[row.containerId!] = members;
                }
                members.Add(row);
            }

            void RecordClass(string valueId, string classId)
            {
                if (incompatibleValueIds.Contains(valueId)) return;
                if (effectiveClassIds.TryGetValue(valueId, out string? existing)
                    && existing != classId)
                {
                    if (ClassExtendsClass(classId, existing))
                    {
                        // The same classId-less row may be exposed through a
                        // Base and Derived placement. Validate/recover against
                        // the most-derived surface, which includes both.
                        effectiveClassIds[valueId] = classId;
                        return;
                    }
                    if (ClassExtendsClass(existing, classId)) return;
                    if (!skipIncompatiblePlacements)
                    {
                        throw new InvalidOperationException(
                            $"Class value '{valueId}' is reached through incompatible trusted Class placements '{existing}' and '{classId}'.");
                    }
                    effectiveClassIds.Remove(valueId);
                    incompatibleValueIds.Add(valueId);
                    Debug.LogWarning(
                        $"Skipped read-only save recovery for classId-less Class value '{valueId}' because it is reached through incompatible Class placements '{existing}' and '{classId}'. Add an explicit classId or repair the conflicting save links.");
                    return;
                }
                effectiveClassIds[valueId] = classId;
            }

            void Visit(string valueId, Member? governingMember)
            {
                if (!rows.TryGetValue(valueId, out MemberValue? row)) return;
                string? classId = row.classId
                    ?? (governingMember as ClassMember)?.classId;
                string visitKey =
                    $"{valueId}:{governingMember?.RuntimeDeclarationIdentity ?? "<none>"}:{classId ?? "<none>"}";
                if (!visited.Add(visitKey)) return;

                if (row is ObjectMemberValue
                    && !string.IsNullOrEmpty(classId)
                    && data.classes.ContainsKey(classId!))
                {
                    RecordClass(valueId, classId!);
                }

                foreach (var child in EnumerateOwnedChildLinks(row, governingMember))
                {
                    Visit(child.valueId, child.member);
                }

                if (governingMember is ListMember list
                    && IsUnorderedList(list)
                    && TryResolveCollectionEntryMember(list) is Member entryMember
                    && rowsByContainer.TryGetValue(
                        valueId,
                        out List<MemberValue>? containedRows))
                {
                    foreach (MemberValue candidate in containedRows)
                    {
                        Visit(candidate.id, entryMember);
                    }
                }
            }

            // Keep the previous protection for explicitly typed rows even if
            // malformed data leaves them unreachable from a project root.
            foreach (MemberValue row in rows.Values)
            {
                if (row is ObjectMemberValue && !string.IsNullOrEmpty(row.classId))
                {
                    Visit(row.id, null);
                }
            }

            // Member value bindings (including all roots) and declaration
            // defaults provide trusted type context for classId-less rows.
            foreach (Member member in data.members.Values)
            {
                if (!string.IsNullOrEmpty(member.valueId))
                {
                    Visit(member.valueId!, member);
                }

                if (member is not ClassMember
                    && member is not ListMember
                    && member is not DictionaryMember)
                {
                    continue;
                }
                // Trusted projection can traverse only declaration defaults
                // that already carry literal value-row links. An initializer
                // creates its rows later, in a constructor evaluation context;
                // trying to materialize it here both lacks that context and
                // incorrectly rejects otherwise valid computed defaults.
                if (MemberValueFactory.InitializerOf(member) is not null)
                {
                    continue;
                }
                MemberValue? declarationDefault = CreateDeclarationDefaultValue(
                    member,
                    $"__neo_readonly_default_projection:{member.RuntimeDeclarationIdentity}");
                if (declarationDefault is null) continue;
                foreach (var child in EnumerateOwnedChildLinks(declarationDefault, member))
                {
                    Visit(child.valueId, child.member);
                }
            }

            if (staticBindings is not null)
            {
                foreach (var binding in staticBindings)
                {
                    if (!string.IsNullOrEmpty(binding.Value)
                        && data.members.TryGetValue(binding.Key, out Member? member))
                    {
                        Visit(binding.Value!, member);
                    }
                }
            }

            return effectiveClassIds;
        }

        private void ValidateReadOnlyInstanceObject(
            string? classId,
            IEnumerable<string>? keys,
            string rowId,
            string source)
        {
            if (string.IsNullOrEmpty(classId) || keys is null) return;
            if (!data.classes.ContainsKey(classId!)) return;
            IList<MergedSchemaEntry> readOnly = ResolveReadOnlyMemberSchema(classId!);
            var presentKeys = new HashSet<string>(keys);
            foreach (MergedSchemaEntry entry in readOnly)
            {
                if (!presentKeys.Contains(entry.schemaKey)) continue;
                throw new InvalidOperationException(
                    $"Class value '{rowId}' in {source} contains read-only declaration member key '{entry.schemaKey}' ({entry.memberId}); read-only declaration members cannot have instance values.");
            }
        }

        private void RecoverReadOnlySaveInstanceKeys()
        {
            if (!data.members.Values.Any(member => member.isReadOnly == true)) return;
            var overlaidRows = new Dictionary<string, MemberValue>(readOnlyAuthoredRows);
            foreach (var pair in saveData.values) overlaidRows[pair.Key] = pair.Value;
            IReadOnlyDictionary<string, string> effectiveClassIds =
                BuildTrustedEffectiveClassIds(
                    overlaidRows,
                    saveData.staticBindings,
                    skipIncompatiblePlacements: true);
            foreach (var pair in saveData.values)
            {
                if (pair.Value is not ObjectMemberValue row
                    || row.value is null)
                {
                    continue;
                }
                string? effectiveClassId = row.classId;
                if (string.IsNullOrEmpty(effectiveClassId)
                    && effectiveClassIds.TryGetValue(pair.Key, out string? inferred))
                {
                    effectiveClassId = inferred;
                }
                if (string.IsNullOrEmpty(effectiveClassId)
                    || !data.classes.ContainsKey(effectiveClassId!))
                {
                    continue;
                }
                foreach (MergedSchemaEntry entry in ResolveReadOnlyMemberSchema(effectiveClassId!))
                {
                    if (!row.value.TryGetValue(entry.schemaKey, out string staleValueId))
                    {
                        continue;
                    }
                    row.value.Remove(entry.schemaKey);
                    if (data.members.TryGetValue(entry.memberId, out Member? member))
                    {
                        recoveredReadOnlySaveValues.Add((staleValueId, member));
                    }
                    Debug.LogWarning(
                        $"Removed stale read-only declaration member key '{entry.schemaKey}' ({entry.memberId}) from save Class value '{pair.Key}'. The declaration default is now authoritative.");
                }
            }
        }

        private void RemoveRecoveredReadOnlySaveValues()
        {
            foreach (var recovered in recoveredReadOnlySaveValues)
            {
                RemoveWritableValueAndDescendantsIfUnlinked(
                    NeoValueOwnership.Save,
                    recovered.valueId,
                    recovered.member);
            }
            recoveredReadOnlySaveValues.Clear();
        }

        /// <summary>
        /// Guard + adoption for partition-stamped rows found in the main
        /// values map at construction (spec §6). Guard choice: a stamp whose
        /// partition the export does NOT ship is REJECTED LOUDLY (a
        /// hand-edited or miswritten export — main-partition rows are never
        /// stamped). A stamp whose partition DOES ship is adopted as
        /// already-loaded: <see cref="ProjectData"/> is shared across clients
        /// (one <c>NeoProjectStore</c> schema, many saves), so a sibling
        /// client's <see cref="LoadValuePartition"/> legitimately leaves the
        /// partition's rows merged in — re-rejecting or re-loading them would
        /// double-load. The stamp stays on the row either way.
        /// </summary>
        private static void ValidateNoLegacyTileGridContents(ProjectData data)
        {
            // Defense in depth beside the schema-version gate: a hand-built or
            // version-stripped export still may not smuggle legacy derived
            // regions past the values-native contract.
            if (data.tileGridContents is null) return;
            if (data.tileGridContents.Count == 0) return;
            throw new System.InvalidOperationException(
                "Project export contains a non-empty 'tileGridContents' payload, which predates the values-native tile grid. Re-export the project from the current web app; tile data now ships exclusively in 'values'.");
        }

        private void AdoptStampedMainValueRows()
        {
            // A freshly parsed export's `values` map carries no `mapKey` stamps
            // (partition rows ship under `valuePartitions`). But this ctor also
            // runs against a ProjectData whose partitions were already merged
            // into `values` by an earlier client's LoadValuePartition (the same
            // schema object reused across reconstructions) — those rows ARE
            // stamped and ARE legitimately loaded. Distinguish the two:
            //   - stamped AND present in valuePartitions[mapKey] => already
            //     loaded, adopt it into the loaded-partition tracking.
            //   - stamped but NOT backed by its partition => corrupt export,
            //     reject loudly (a main row must not claim a partition it does
            //     not belong to).
            foreach (var pair in data.values)
            {
                string? mapKey = pair.Value?.mapKey;
                if (string.IsNullOrEmpty(mapKey)) continue;
                if (!PartitionShipsRow(mapKey!, pair.Key))
                {
                    throw new System.InvalidOperationException(
                        $"Value '{pair.Key}' in the main 'values' map is stamped with partition '{mapKey}', but no such row ships under 'valuePartitions[\"{mapKey}\"]'. Partition rows must ship in their partition; re-export the project from the current web app.");
                }
                if (!loadedPartitionRowIds.TryGetValue(mapKey!, out var rowIds))
                {
                    rowIds = new HashSet<string>();
                    loadedPartitionRowIds[mapKey!] = rowIds;
                }
                rowIds.Add(pair.Key);
            }
        }

        private bool PartitionShipsRow(string mapKey, string rowId)
        {
            if (data.valuePartitions is null) return false;
            if (!data.valuePartitions.TryGetValue(mapKey, out var token)) return false;
            return token is Newtonsoft.Json.Linq.JObject partition
                && partition[rowId] is not null;
        }

        internal bool TryGetValueOwnership(string id, out NeoValueOwnership ownership)
        {
            if (sessionData.values.ContainsKey(id))
            {
                ownership = NeoValueOwnership.Session;
                return true;
            }
            if (saveData.values.ContainsKey(id))
            {
                ownership = NeoValueOwnership.Save;
                return true;
            }
            // Reachable from a writable root but not yet shadowed (sparse): still
            // writable in that store.
            if (authoredOwnership.TryGetValue(id, out ownership))
            {
                return true;
            }
            if (data.values.ContainsKey(id))
            {
                ownership = NeoValueOwnership.Asset;
                return true;
            }
            if (virtualValueOwnership.TryGetValue(id, out ownership))
            {
                return true;
            }
            ownership = NeoValueOwnership.Asset;
            return false;
        }

        internal bool HasWritableValue(
            NeoValueOwnership ownership,
            string valueId)
        {
            if (ownership == NeoValueOwnership.Asset) return false;
            return GetWritableStore(ownership).values.ContainsKey(valueId);
        }

        /// <summary>
        /// Test/seed helper: shadow a save value at its stable id. Value ids are
        /// stable instance identities, so seeding a value is just a write at its
        /// own id; <paramref name="memberId"/> is retained for call-site
        /// readability but no longer maps through an override table.
        /// </summary>
        internal void AddSaveValue<TMemberValue>(string memberId, TMemberValue value) where TMemberValue : MemberValue
        {
            _ = memberId;
            SetWritableValue(NeoValueOwnership.Save, value);
        }

        internal void SetSaveValue<TMemberValue>(TMemberValue value) where TMemberValue : MemberValue
        {
            SetWritableValue(NeoValueOwnership.Save, value);
        }

        internal void SetSaveValueSilently<TMemberValue>(TMemberValue value) where TMemberValue : MemberValue
        {
            SetWritableValueSilently(NeoValueOwnership.Save, value);
        }

        /// <summary>
        /// Stable-id overlay resolution for a writable (Save/Session) node: the
        /// selected ownership store's row when present, then the authored
        /// default. Save and Session are independent overlays; neither falls
        /// through to the other. A removal tombstone
        /// (<see cref="MemberValue.IsRemoved"/>) resolves as <b>unset</b>
        /// (returns false, never falling through) — otherwise the authored asset
        /// default. This is the shadow rule <c>save.values[id] ?? authored</c>
        /// shared with the web overlay, and is what lets a save stay sparse:
        /// untouched values read through to the defaults by their stable id.
        /// </summary>
        internal bool TryGetOverlaidValue<TValue>(
            NeoValueOwnership ownership,
            string id,
            [NotNullWhen(true)] out TValue? value) where TValue : MemberValue
        {
            value = null;
            if (ownership != NeoValueOwnership.Asset)
            {
                var store = GetWritableStore(ownership);
                if (store.values.TryGetValue(id, out MemberValue overlaid))
                {
                    if (overlaid.IsRemoved) return false;
                    value = overlaid as TValue;
                    return value is not null;
                }
            }
            if (data.values.TryGetValue(id, out MemberValue assetRow))
            {
                value = assetRow as TValue;
                return value is not null;
            }
            if (virtualValues.TryGetValue(id, out MemberValue virtualRow))
            {
                value = virtualRow as TValue;
                return value is not null;
            }
            return false;
        }

        /// <summary>
        /// Resolves the row a writable mutation should clone. Session writes
        /// may shadow an existing Save row, while ordinary Session member
        /// reads remain independent from Save via
        /// <see cref="TryGetOverlaidValue{TValue}"/>.
        /// </summary>
        internal bool TryGetWritableShadowSource<TValue>(
            NeoValueOwnership ownership,
            string id,
            [NotNullWhen(true)] out TValue? value) where TValue : MemberValue
        {
            value = null;
            if (ownership != NeoValueOwnership.Asset)
            {
                var store = GetWritableStore(ownership);
                if (store.values.TryGetValue(id, out MemberValue ownedRow))
                {
                    if (ownedRow.IsRemoved) return false;
                    value = ownedRow as TValue;
                    return value is not null;
                }
            }
            if (ownership == NeoValueOwnership.Session
                && saveData.values.TryGetValue(id, out MemberValue saveRow))
            {
                if (saveRow.IsRemoved) return false;
                value = saveRow as TValue;
                return value is not null;
            }
            if (data.values.TryGetValue(id, out MemberValue authoredRow))
            {
                value = authoredRow as TValue;
                return value is not null;
            }
            if (virtualValues.TryGetValue(id, out MemberValue virtualRow))
            {
                value = virtualRow as TValue;
                return value is not null;
            }
            return false;
        }

        /// <summary>
        /// Returns a writable clone of <paramref name="row"/> keeping its id, so a
        /// writable <c>Set</c> can mutate + shadow it without touching the shared
        /// authored asset object it may have resolved through.
        /// </summary>
        internal MemberValue CloneRowForWrite(MemberValue row) => CloneValueRow(row);

        /// <summary>
        /// Ensures the value at <paramref name="id"/> is present in the
        /// <paramref name="ownership"/> store as a writable shadow — cloning
        /// the resolved (authored or already-owned) row at the <b>same</b>
        /// id when nothing is shadowed yet. No-op when already shadowed.
        /// This is the row-level clone-on-write primitive: because ids are
        /// stable, shadowing the single mutated row is sufficient (its
        /// parent already references this id). Returns false when there is
        /// no row to shadow.
        /// </summary>
        internal bool EnsureWritableShadow(NeoValueOwnership ownership, string id)
        {
            if (ownership == NeoValueOwnership.Asset) return false;
            var store = GetWritableStore(ownership);
            if (store.values.ContainsKey(id)) return true;
            if (!TryGetOverlaidValue(ownership, id, out MemberValue? resolved)) return false;
            SetWritableValueSilently(ownership, CloneValueRow(resolved));
            return true;
        }

        /// <summary>
        /// Writes a minimal removal <b>tombstone</b>
        /// (<see cref="NeoValueMarks.Removed"/>) at <paramref name="id"/> so
        /// resolution returns <b>unset</b> instead of falling through to the
        /// authored default. This is sparse explicit removal — only the single
        /// tombstone row is shadowed; the parent record is untouched. (Contrast
        /// <see cref="RemoveWritableShadow"/>, which drops the shadow so the
        /// authored default resurfaces.) The marker carries no payload or child
        /// references, so the replaced value's owned descendants become
        /// collectable — see <see cref="WriteRemovalTombstone"/> for the hard-remove
        /// variant that reclaims them.
        /// </summary>
        internal void WriteTombstone(NeoValueOwnership ownership, string id)
        {
            if (ownership == NeoValueOwnership.Asset)
            {
                throw new System.InvalidOperationException(
                    "Cannot tombstone an asset-owned value.");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            // Minimal marker — no payload, no child links. Resolution only checks
            // `mark`, so the stored value is genuinely null and the removed value's
            // children are no longer referenced through it.
            var tombstone = new NullMemberValue
            {
                id = id,
                createdAt = nowIso,
                updatedAt = nowIso,
                mark = NeoValueMarks.Removed,
            };
            SetWritableValue(ownership, tombstone, "mark");
        }

        /// <summary>
        /// Hard-removes the value at <paramref name="id"/>: stamps a minimal
        /// removal <see cref="WriteTombstone">tombstone</see> in its place and
        /// reclaims the replaced value's now-orphaned descendants from the writable
        /// store (mirroring the collection <c>Remove</c> ops' GC). Descendants
        /// still referenced elsewhere are preserved by the reachability check, and
        /// authored-only rows are never touched.
        /// </summary>
        internal void WriteRemovalTombstone(NeoValueOwnership ownership, string id)
        {
            var formerChildren = new List<(string valueId, Member? member)>();
            if (TryGetOverlaidValue(ownership, id, out MemberValue? replaced))
            {
                Member? sourceMember = TryInferMemberForValueId(
                    id,
                    out Member? inferredMember)
                        ? inferredMember
                        : null;
                formerChildren.AddRange(EnumerateOwnedChildLinks(replaced, sourceMember));
                if (sourceMember is ListMember list && IsUnorderedList(list))
                {
                    Member? entryMember = TryResolveCollectionEntryMember(sourceMember);
                    if (entryMember is not null)
                    {
                        foreach (string memberId in EnumerateContainerMemberValueIds(ownership, id))
                        {
                            formerChildren.Add((memberId, entryMember));
                        }
                    }
                }
            }
            WriteTombstone(ownership, id);
            if (formerChildren.Count == 0) return;
            // Build reachability after the tombstone replaces the value, so the
            // former children are seen as orphaned (unless shared elsewhere).
            var reachableByOwnership = new Dictionary<NeoValueOwnership, HashSet<string>>();
            foreach (var child in formerChildren)
            {
                NeoValueOwnership childOwnership =
                    (child.member is null ? null : DeclaredOwnership(child.member)) ?? ownership;
                if (childOwnership == NeoValueOwnership.Asset) continue;
                if (!reachableByOwnership.TryGetValue(childOwnership, out var reachable))
                {
                    reachable = BuildReachableWritableValueIds(childOwnership);
                    reachableByOwnership[childOwnership] = reachable;
                }
                RemoveWritableValueAndDescendantsIfUnlinked(
                    childOwnership,
                    child.valueId,
                    reachable,
                    child.member,
                    new HashSet<string>(),
                    new HashSet<string>());
            }
        }

        /// <summary>
        /// Drops the writable shadow at <paramref name="id"/> so resolution
        /// falls back through the overlay to the authored default. Notifies
        /// bound nodes so they refresh. Returns true when a shadow was
        /// removed.
        /// </summary>
        internal bool RemoveWritableShadow(NeoValueOwnership ownership, string id)
        {
            if (ownership == NeoValueOwnership.Asset) return false;
            var store = GetWritableStore(ownership);
            TryResolveContainerIdForValueId(id, out string? memberContainerId);
            if (!store.values.Remove(id)) return false;
            IndexStoreRemove(ownership, id);
            TouchWritableStoreUpdatedAt(ownership);
            OnWritableValueChanged?.Invoke(ownership, id);
            if (memberContainerId is not null)
            {
                RaiseContainerChanged(ownership, memberContainerId);
            }
            if (ownership == NeoValueOwnership.Save)
            {
                RaiseSaveValueChanged(id);
            }
            return true;
        }

        internal void SetWritableValue<TMemberValue>(
            NeoValueOwnership ownership,
            TMemberValue value,
            string? changedField = null) where TMemberValue : MemberValue
        {
            StampMapKeyForWrite(ownership, value);
            GetWritableStore(ownership).values[value.id] = value;
            IndexStoreWrite(ownership, value);
            TouchWritableStoreUpdatedAt(ownership);
            OnWritableValueChanged?.Invoke(ownership, value.id);
            NotifyContainerMembershipChanged(ownership, value.id);
            if (ownership == NeoValueOwnership.Save)
            {
                RaiseSaveValueChanged(value.id, changedField);
            }
        }

        internal void SetWritableValueSilently<TMemberValue>(
            NeoValueOwnership ownership,
            TMemberValue value) where TMemberValue : MemberValue
        {
            StampMapKeyForWrite(ownership, value);
            GetWritableStore(ownership).values[value.id] = value;
            IndexStoreWrite(ownership, value);
        }

        /// <summary>
        /// Publishes one constructor graph after its complete row set has been
        /// collision-, ownership-, shape-, and partition-validated. Every id
        /// is fresh, so ordinary overlay stamping and replacement-index checks
        /// are redundant. Rows become visible as one graph before callbacks
        /// run; subscribers therefore cannot observe a partially published
        /// constructor result.
        /// </summary>
        internal void PublishConstructedSessionRows(
            IReadOnlyList<MemberValue> values)
        {
            foreach (MemberValue value in values)
            {
                sessionData.values.Add(value.id, value);
                if (!string.IsNullOrEmpty(value.containerId))
                {
                    AddMembership(
                        sessionEntriesByContainer,
                        sessionContainerByRow,
                        value.id,
                        value.containerId!);
                }
            }
            foreach (MemberValue value in values)
            {
                OnWritableValueChanged?.Invoke(
                    NeoValueOwnership.Session,
                    value.id);
                if (!string.IsNullOrEmpty(value.containerId))
                {
                    RaiseContainerChanged(
                        NeoValueOwnership.Session,
                        value.containerId!);
                }
            }
        }

        private void TouchWritableStoreUpdatedAt(NeoValueOwnership ownership)
        {
            if (ownership == NeoValueOwnership.Save)
            {
                saveData.updatedAt = NeoTimestamp.Now();
            }
        }

        internal void SetSavePayloadRows(object? payload)
        {
            if (payload is not NeoValuePayload wrapped) return;
            foreach (var row in wrapped.valueRows)
            {
                SetSaveValue(row);
            }
        }

        internal void SetWritablePayloadRows(NeoValueOwnership ownership, object? payload)
        {
            if (payload is not NeoValuePayload wrapped) return;
            foreach (var row in wrapped.valueRows)
            {
                SetWritableValue(ownership, row);
            }
        }

        internal string ImportValueReference(
            NeoValueOwnership targetOwnership,
            string sourceValueId,
            string? currentDestinationValueId = null)
        {
            return ImportValueReference(
                targetOwnership,
                sourceValueId,
                out _,
                currentDestinationValueId);
        }

        internal string ImportValueReference(
            NeoValueOwnership targetOwnership,
            string sourceValueId,
            out bool sourceMoved,
            string? currentDestinationValueId = null)
        {
            if (targetOwnership == NeoValueOwnership.Asset)
            {
                throw new System.InvalidOperationException("Cannot import a writable value into asset ownership.");
            }
            sourceMoved = false;
            if (!TryGetValueOwnership(sourceValueId, out NeoValueOwnership sourceOwnership))
            {
                return sourceValueId;
            }
            if (sourceOwnership == targetOwnership)
            {
                // Reassigning a slot to the exact row it already owns is a
                // harmless no-op. Every other same-store assignment must
                // adopt a parentless row; otherwise two owned edges would
                // silently form a DAG.
                if (currentDestinationValueId == sourceValueId)
                {
                    return sourceValueId;
                }
                if (TryFindOwnedParent(targetOwnership, sourceValueId, out string? parentValueId))
                {
                    throw new System.InvalidOperationException(
                        $"Class value '{sourceValueId}' is already owned by parent value '{parentValueId}' and cannot be assigned to another parent. Use .Clone() to create an independent Class value before assigning it.");
                }
                return sourceValueId;
            }
            // Stable-id overlay imports normally preserve the source id.
            // If that id is already owned inside the destination graph,
            // preserving it would attach the same destination row twice.
            // This is the one cross-store correction required by strict-tree
            // ownership: import a fresh-id graph instead.
            if (currentDestinationValueId != sourceValueId
                && TryFindOwnedParent(targetOwnership, sourceValueId, out _))
            {
                return CloneOwnedValueGraphWithFreshIdsAtomic(
                    targetOwnership,
                    sourceOwnership,
                    sourceValueId,
                    TryInferMemberForValueId(sourceValueId, out Member? collisionMember)
                        ? collisionMember
                        : null);
            }
            if (sourceOwnership == NeoValueOwnership.Session
                && targetOwnership == NeoValueOwnership.Save
                && !BuildReachableWritableValueIds(NeoValueOwnership.Session).Contains(sourceValueId))
            {
                Member? sourceMember = TryInferMemberForValueId(
                    sourceValueId,
                    out Member? inferredSourceMember)
                        ? inferredSourceMember
                        : null;
                // Parentless runtime Session aggregates are deliberately not
                // global reachability roots. Their authoritative owned edges
                // still matter: moving one of their descendants into Save
                // would leave the Session parent dangling. A parented source
                // therefore clones for the new Save owner even when ordinary
                // global reachability does not include it.
                if (TryFindOwnedParent(
                        NeoValueOwnership.Session,
                        sourceValueId,
                        out _))
                {
                    return CloneOwnedValueReferenceForNewParent(
                        targetOwnership,
                        sourceOwnership,
                        sourceValueId,
                        sourceMember);
                }
                // A parentless Session graph is normally moved into Save with
                // stable ids so live aliases can be retargeted. If any owned
                // row id already participates in the Save graph, moving would
                // overwrite that row and silently give it two parents. Clone
                // the complete source graph with fresh ids instead.
                if (OwnedValueGraphCollidesWithOwnership(
                        sourceOwnership,
                        targetOwnership,
                        sourceValueId,
                        sourceMember,
                        new HashSet<string>()))
                {
                    return CloneOwnedValueReferenceForNewParent(
                        targetOwnership,
                        sourceOwnership,
                        sourceValueId,
                        sourceMember);
                }
                PromoteValueGraph(
                    NeoValueOwnership.Session,
                    NeoValueOwnership.Save,
                    sourceValueId,
                    new HashSet<string>(),
                    sourceMember);
                sourceMoved = true;
                return sourceValueId;
            }
            return CloneValueGraphToOwnership(
                targetOwnership,
                sourceValueId,
                new Dictionary<string, string>(),
                TryInferMemberForValueId(sourceValueId, out Member? inferredMember)
                    ? inferredMember
                : null);
        }

        /// <summary>
        /// Creates a fresh-id copy of an authoritative owned graph for a new
        /// parent in another writable store. Reference edges such as Lookup
        /// selections retain their target ids; only schema-owned rows clone.
        /// </summary>
        internal string CloneOwnedValueReferenceForNewParent(
            NeoValueOwnership targetOwnership,
            NeoValueOwnership sourceOwnership,
            string sourceValueId,
            Member? sourceMember)
        {
            if (targetOwnership == NeoValueOwnership.Asset)
            {
                throw new System.InvalidOperationException(
                    "Cannot clone an owned value graph into asset ownership.");
            }
            return CloneOwnedValueGraphWithFreshIdsAtomic(
                targetOwnership,
                sourceOwnership,
                sourceValueId,
                sourceMember);
        }

        /// <summary>
        /// Creates a complete, parentless copy of an owned Class value graph
        /// in Session storage. Unlike sparse overlay import, every owned row
        /// receives a fresh id. Lookup selections remain references.
        /// </summary>
        internal string CloneValueReference(
            string sourceValueId,
            NeoValueOwnership? sourceOwnership = null)
        {
            ObjectMemberValue? sourceRow;
            bool foundSource = sourceOwnership is NeoValueOwnership exactOwnership
                ? TryGetValue(exactOwnership, sourceValueId, out sourceRow)
                : TryGetValue(sourceValueId, out sourceRow);
            if (string.IsNullOrEmpty(sourceValueId) || !foundSource || sourceRow is null)
            {
                throw new System.InvalidOperationException(
                    $"Cannot clone Class value '{sourceValueId}': its object value row does not exist.");
            }

            Member? sourceMember = TryInferMemberForValueId(sourceValueId, out Member? inferred)
                ? inferred
                : null;
            string? sourceClassId = sourceRow.classId
                ?? (sourceMember as ClassMember)?.classId;
            if (!string.IsNullOrEmpty(sourceClassId)
                && TryResolveSchemaClassAllowedOwnership(sourceClassId!, out var allowedOwnership)
                && allowedOwnership == NeoValueOwnership.Asset)
            {
                throw new System.InvalidOperationException(
                    $"Cannot clone Class value '{sourceValueId}' of immutable-only class '{sourceClassId}'. Immutable Classes cannot produce writable clones.");
            }
            return CloneOwnedValueGraphWithFreshIdsAtomic(
                NeoValueOwnership.Session,
                sourceOwnership ?? (TryGetValueOwnership(sourceValueId, out var inferredSourceOwnership)
                    ? inferredSourceOwnership
                    : NeoValueOwnership.Asset),
                sourceRow.id,
                sourceMember);
        }

        private string CloneOwnedValueGraphWithFreshIdsAtomic(
            NeoValueOwnership targetOwnership,
            NeoValueOwnership sourceOwnership,
            string sourceValueId,
            Member? sourceMember)
        {
            var createdValueIds = new HashSet<string>();
            try
            {
                return CloneOwnedValueGraphWithFreshIds(
                    targetOwnership,
                    sourceOwnership,
                    sourceValueId,
                    sourceMember,
                    new HashSet<string>(),
                    clonedContainerId: null,
                    createdValueIds);
            }
            catch
            {
                // Recursive fresh-id cloning publishes children before their
                // parents. If a later descendant/cycle/event fails, reclaim
                // every exact row created by this attempt; none can predate
                // the transaction because all ids are fresh GUIDs.
                foreach (string createdValueId in
                         new List<string>(createdValueIds))
                {
                    RemoveTemporaryWritableValueGraph(
                        targetOwnership,
                        createdValueId);
                }
                throw;
            }
        }

        private string CloneOwnedValueGraphWithFreshIds(
            NeoValueOwnership targetOwnership,
            NeoValueOwnership sourceOwnership,
            string sourceValueId,
            Member? sourceMember,
            HashSet<string> path,
            string? clonedContainerId,
            HashSet<string> createdValueIds)
        {
            if (!path.Add(sourceValueId))
            {
                throw new System.InvalidOperationException(
                    $"Cannot clone value graph rooted at '{sourceValueId}': an owned-value cycle reaches '{sourceValueId}'.");
            }
            try
            {
                if (!TryGetValue(sourceOwnership, sourceValueId, out MemberValue? sourceRow))
                {
                    throw new System.InvalidOperationException(
                        $"Cannot clone value graph: owned value row '{sourceValueId}' does not exist.");
                }

                MemberValue clone = CloneValueRow(sourceRow);
                clone.id = System.Guid.NewGuid().ToString();
                clone.containerId = clonedContainerId;

                switch (clone)
                {
                    case ObjectMemberValue obj when obj.value is not null:
                    {
                        var remapped = new Dictionary<string, string>();
                        foreach (var pair in obj.value)
                        {
                            Member? childMember =
                                TryResolveOwnedChildMember(sourceRow, sourceMember, pair.Key);
                            remapped[pair.Key] = childMember is not null
                                && TryGetValue(
                                    DeclaredOwnership(childMember) ?? sourceOwnership,
                                    pair.Value,
                                    out MemberValue? _)
                                    ? CloneOwnedValueGraphWithFreshIds(
                                        targetOwnership,
                                        DeclaredOwnership(childMember) ?? sourceOwnership,
                                        pair.Value,
                                        childMember,
                                        path,
                                        null,
                                        createdValueIds)
                                    : pair.Value;
                        }
                        obj.value = remapped;
                        break;
                    }
                    case ArrayMemberValue arr when arr.value is not null:
                    {
                        // A Lookup row owns the row itself but its selections
                        // are reference edges and must retain their target ids.
                        if (sourceMember is LookupMember) break;
                        Member? entryMember = TryResolveCollectionEntryMember(sourceMember);
                        if (entryMember is null) break;
                        var remapped = new string[arr.value.Length];
                        for (int i = 0; i < arr.value.Length; i++)
                        {
                            NeoValueOwnership entryOwnership =
                                DeclaredOwnership(entryMember) ?? sourceOwnership;
                            remapped[i] = TryGetValue(entryOwnership, arr.value[i], out MemberValue? _)
                                ? CloneOwnedValueGraphWithFreshIds(
                                    targetOwnership,
                                    entryOwnership,
                                    arr.value[i],
                                    entryMember,
                                    path,
                                    null,
                                    createdValueIds)
                                : arr.value[i];
                        }
                        arr.value = remapped;
                        break;
                    }
                }

                createdValueIds.Add(clone.id);
                SetWritableValue(targetOwnership, clone);

                // Unordered list membership is stored on the member rows,
                // rather than as ids in the list's (empty) array payload.
                if (sourceMember is ListMember list
                    && IsUnorderedList(list))
                {
                    Member? entryMember = TryResolveCollectionEntryMember(sourceMember);
                    if (entryMember is not null)
                    {
                        NeoValueOwnership entryOwnership =
                            DeclaredOwnership(entryMember) ?? sourceOwnership;
                        foreach (string memberId in EnumerateContainerMemberValueIds(
                            sourceOwnership,
                            sourceValueId))
                        {
                            CloneOwnedValueGraphWithFreshIds(
                                targetOwnership,
                                entryOwnership,
                                memberId,
                                entryMember,
                                path,
                                clone.id,
                                createdValueIds);
                        }
                    }
                }
                return clone.id;
            }
            finally
            {
                path.Remove(sourceValueId);
            }
        }

        private IEnumerable<string> EnumerateContainerMemberValueIds(
            NeoValueOwnership sourceOwnership,
            string containerId)
        {
            ProjectSaveData? store = null;
            if (sourceOwnership != NeoValueOwnership.Asset)
            {
                store = GetWritableStore(sourceOwnership);
                var (storeByContainer, _) = MembershipMaps(sourceOwnership);
                if (storeByContainer.TryGetValue(containerId, out var indexedStoreMembers))
                {
                    // Snapshot because deletion can remove members while
                    // consuming this iterator.
                    foreach (string memberId in new List<string>(indexedStoreMembers))
                    {
                        yield return memberId;
                    }
                }
            }

            if (!authoredEntriesByContainer.TryGetValue(containerId, out var authoredMembers))
            {
                yield break;
            }
            if (!data.values.TryGetValue(containerId, out MemberValue authoredContainer)
                || EffectiveAuthoredOwnership(containerId, authoredContainer) != sourceOwnership)
            {
                yield break;
            }
            foreach (string memberId in authoredMembers)
            {
                // A row in the selected writable overlay replaces its authored
                // counterpart, including moving it to another container or
                // tombstoning it. The membership indexes let this remain O(the
                // members of this container), rather than O(all value rows).
                if (store is not null && store.values.ContainsKey(memberId)) continue;
                yield return memberId;
            }
        }

        /// <summary>
        /// Finds the authoritative owned parent edge for a value in one
        /// writable ownership graph. Wrapper instances are deliberately not
        /// consulted: they are only views and may not currently exist.
        /// </summary>
        internal bool TryFindOwnedParent(
            NeoValueOwnership childOwnership,
            string childValueId,
            [NotNullWhen(true)] out string? parentValueId)
        {
            // Unordered-list ownership is an immutable membership stamp on
            // the child row itself.
            MemberValue? child = null;
            var childStore = GetWritableStore(childOwnership);
            if (!childStore.values.TryGetValue(childValueId, out child)
                && data.values.TryGetValue(childValueId, out MemberValue authoredChild)
                && EffectiveAuthoredOwnership(childValueId, authoredChild) == childOwnership)
            {
                child = authoredChild;
            }
            if (child is not null
                && !string.IsNullOrEmpty(child.containerId))
            {
                parentValueId = child.containerId;
                return true;
            }

            foreach (var candidate in EnumerateEffectiveParentRows())
            {
                string candidateId = candidate.valueId;
                MemberValue parent = candidate.row;
                // Member inference walks schema/value paths and is materially
                // more expensive than inspecting a row payload. Almost every row
                // is irrelevant to a particular child, so reject those first.
                // We still resolve the schema for an actual payload match below,
                // which is what distinguishes owned Class/List/Dictionary edges
                // from lookup/reference edges.
                if (!DirectlyReferencesValueId(parent, childValueId)) continue;
                Member? parentMember = TryInferMemberForValueId(
                    candidateId,
                    out Member? inferredParent)
                        ? inferredParent
                        : null;
                foreach (var ownedChild in EnumerateOwnedChildLinks(parent, parentMember))
                {
                    NeoValueOwnership edgeOwnership =
                        DeclaredOwnership(ownedChild.member!) ?? candidate.ownership;
                    if (edgeOwnership == childOwnership
                        && ownedChild.valueId == childValueId)
                    {
                        parentValueId = candidateId;
                        return true;
                    }
                }
            }

            // Member valueIds are also owning roots, including schema
            // placements whose wrappers have never been instantiated.
            foreach (Member candidate in data.members.Values)
            {
                if (candidate.valueId != childValueId) continue;
                NeoValueOwnership effective;
                if (DeclaredOwnership(candidate) is NeoValueOwnership declared)
                {
                    effective = declared;
                }
                else if (data.values.TryGetValue(childValueId, out MemberValue authoredRoot))
                {
                    effective = EffectiveAuthoredOwnership(childValueId, authoredRoot);
                }
                else
                {
                    effective = NeoValueOwnership.Asset;
                }
                if (effective == childOwnership)
                {
                    parentValueId = $"member:{candidate.id}";
                    return true;
                }
            }

            // Class-owned members are independent owning roots. Their active
            // Save/Session overlay binding may differ from member.valueId,
            // so it participates in strict-tree ownership like an ordinary
            // member root. Without this check a constructor could attach a
            // Session-static aggregate beneath a second parent.
            foreach (Member candidate in data.members.Values)
            {
                if (!candidate.isStatic
                    || !TryResolveStaticBinding(
                        candidate.id,
                        out _,
                        out NeoValueOwnership staticOwnership,
                        out string? staticValueId)
                    || staticOwnership != childOwnership
                    || staticValueId != childValueId)
                {
                    continue;
                }
                parentValueId = $"static:{candidate.id}";
                return true;
            }

            parentValueId = null;
            return false;
        }

        /// <summary>
        /// Verifies one already schema-validated owned edge without performing
        /// the global parent search. Constructor allocation cleanup uses this
        /// to guard its short-lived parent links against subsequent mutation:
        /// a stale link falls back to <see cref="TryFindOwnedParent"/>.
        /// </summary>
        internal bool StillHasOwnedChildReference(
            NeoValueOwnership ownership,
            string parentValueId,
            string childValueId)
        {
            if (!TryGetValue(
                    ownership,
                    parentValueId,
                    out MemberValue? parent))
            {
                return false;
            }
            if (TryGetValue(
                    ownership,
                    childValueId,
                    out MemberValue? child)
                && child.containerId == parentValueId)
            {
                return true;
            }
            return DirectlyReferencesValueId(parent!, childValueId);
        }

        private static bool DirectlyReferencesValueId(
            MemberValue parent,
            string childValueId)
        {
            switch (parent)
            {
                case ObjectMemberValue obj when obj.value is not null:
                    return obj.value.ContainsValue(childValueId);
                case ArrayMemberValue arr when arr.value is not null:
                    return System.Array.IndexOf(arr.value, childValueId) >= 0;
                default:
                    return false;
            }
        }

        private IEnumerable<(string valueId, MemberValue row, NeoValueOwnership ownership)>
            EnumerateEffectiveParentRows()
        {
            // Inspect each writable overlay independently. The same stable id
            // can legitimately be present in both stores with different row
            // payloads; neither may hide the other during ownership checks.
            foreach (var pair in sessionData.values)
            {
                yield return (pair.Key, pair.Value, NeoValueOwnership.Session);
            }
            foreach (var pair in saveData.values)
            {
                yield return (pair.Key, pair.Value, NeoValueOwnership.Save);
            }
            foreach (var pair in data.values)
            {
                NeoValueOwnership authoredOwnershipForRow =
                    EffectiveAuthoredOwnership(pair.Key, pair.Value);
                // A writable shadow replaces this authored row only in its
                // own graph. A distinct shadow in the other store does not.
                if (authoredOwnershipForRow != NeoValueOwnership.Asset
                    && GetWritableStore(authoredOwnershipForRow).values.ContainsKey(pair.Key))
                {
                    continue;
                }
                yield return (pair.Key, pair.Value, authoredOwnershipForRow);
            }
        }

        private NeoValueOwnership EffectiveAuthoredOwnership(
            string valueId,
            MemberValue row)
        {
            if (authoredOwnership.TryGetValue(valueId, out NeoValueOwnership ownership))
            {
                return ownership;
            }
            if (row is ObjectMemberValue obj
                && obj.classId is string runtimeClassId
                && TryResolveSchemaClassAllowedOwnership(runtimeClassId, out ownership))
            {
                return ownership;
            }
            return NeoValueOwnership.Asset;
        }

        private void PromoteValueGraph(
            NeoValueOwnership sourceOwnership,
            NeoValueOwnership targetOwnership,
            string valueId,
            HashSet<string> visited,
            Member? sourceMember = null)
        {
            if (!visited.Add(valueId)) return;
            var sourceStore = GetWritableStore(sourceOwnership);
            var targetStore = GetWritableStore(targetOwnership);
            if (!sourceStore.values.TryGetValue(valueId, out MemberValue? row)) return;

            targetStore.values[valueId] = row;
            IndexStoreWrite(targetOwnership, row);
            foreach (var child in EnumerateOwnedChildLinks(row, sourceMember))
            {
                NeoValueOwnership childOwnership =
                    DeclaredOwnership(child.member!) ?? sourceOwnership;
                if (childOwnership != sourceOwnership) continue;
                PromoteValueGraph(
                    sourceOwnership,
                    targetOwnership,
                    child.valueId,
                    visited,
                    child.member);
            }
            sourceStore.values.Remove(valueId);
            IndexStoreRemove(sourceOwnership, valueId);
            OnWritableValueChanged?.Invoke(sourceOwnership, valueId);
            OnWritableValueChanged?.Invoke(targetOwnership, valueId);
            if (targetOwnership == NeoValueOwnership.Save) RaiseSaveValueChanged(valueId);
        }

        private bool OwnedValueGraphCollidesWithOwnership(
            NeoValueOwnership sourceOwnership,
            NeoValueOwnership targetOwnership,
            string valueId,
            Member? sourceMember,
            HashSet<string> visited)
        {
            if (!visited.Add(valueId)) return false;
            ProjectSaveData targetStore = GetWritableStore(targetOwnership);
            if (targetStore.values.ContainsKey(valueId)
                || data.values.TryGetValue(valueId, out MemberValue authored)
                    && EffectiveAuthoredOwnership(valueId, authored) == targetOwnership
                || TryFindOwnedParent(targetOwnership, valueId, out _))
            {
                return true;
            }
            if (!TryGetValue(
                    sourceOwnership,
                    valueId,
                    out MemberValue? sourceRow))
            {
                return false;
            }
            foreach (var child in EnumerateOwnedChildLinks(
                sourceRow!, sourceMember))
            {
                NeoValueOwnership childOwnership =
                    DeclaredOwnership(child.member!) ?? sourceOwnership;
                if (childOwnership != sourceOwnership) continue;
                if (OwnedValueGraphCollidesWithOwnership(
                    sourceOwnership,
                    targetOwnership,
                    child.valueId,
                    child.member,
                    visited))
                {
                    return true;
                }
            }
            if (sourceMember is ListMember list
                && IsUnorderedList(list)
                && TryResolveCollectionEntryMember(sourceMember)
                    is Member entryMember
                && (DeclaredOwnership(entryMember) ?? sourceOwnership)
                    == sourceOwnership)
            {
                foreach (string memberId in EnumerateContainerMemberValueIds(
                    sourceOwnership,
                    valueId))
                {
                    if (OwnedValueGraphCollidesWithOwnership(
                        sourceOwnership,
                        targetOwnership,
                        memberId,
                        entryMember,
                        visited))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private string CloneValueGraphToOwnership(
            NeoValueOwnership targetOwnership,
            string sourceValueId,
            Dictionary<string, string> remappedIds,
            Member? sourceMember = null)
        {
            if (remappedIds.TryGetValue(sourceValueId, out string existingId)) return existingId;
            if (!TryGetValue(sourceValueId, out MemberValue? sourceRow))
            {
                return sourceValueId;
            }

            var clone = CloneValueRow(sourceRow);
            // Stable-id overlay: the copy keeps the authored id (it shadows that
            // id in the target store), never a fresh GUID.
            clone.id = sourceRow.id;
            remappedIds[sourceValueId] = clone.id;

            switch (clone)
            {
                case ObjectMemberValue obj when obj.value is not null:
                {
                    var remapped = new Dictionary<string, string>();
                    foreach (var pair in obj.value)
                    {
                        Member? childMember = TryResolveOwnedChildMember(sourceRow, sourceMember, pair.Key);
                        remapped[pair.Key] = childMember is not null && TryGetValue(pair.Value, out MemberValue? _)
                            ? CloneValueGraphToOwnership(targetOwnership, pair.Value, remappedIds, childMember)
                            : pair.Value;
                    }
                    obj.value = remapped;
                    break;
                }
                case ArrayMemberValue arr when arr.value is not null:
                {
                    if (sourceMember is LookupMember)
                    {
                        break;
                    }
                    Member? entryMember = TryResolveCollectionEntryMember(sourceMember);
                    var remapped = new string[arr.value.Length];
                    for (int i = 0; i < arr.value.Length; i++)
                    {
                        string childId = arr.value[i];
                        remapped[i] = entryMember is not null && TryGetValue(childId, out MemberValue? _)
                            ? CloneValueGraphToOwnership(targetOwnership, childId, remappedIds, entryMember)
                            : childId;
                    }
                    arr.value = remapped;
                    break;
                }
            }

            SetWritableValue(targetOwnership, clone);
            return clone.id;
        }

        private IEnumerable<(string valueId, Member? member)> EnumerateOwnedChildLinks(
            MemberValue row,
            Member? sourceMember)
        {
            switch (row)
            {
                case ObjectMemberValue obj when obj.value is not null:
                    foreach (var pair in obj.value)
                    {
                        Member? childMember = TryResolveOwnedChildMember(row, sourceMember, pair.Key);
                        if (childMember is not null)
                        {
                            yield return (pair.Value, childMember);
                        }
                    }
                    break;
                case ArrayMemberValue arr when arr.value is not null:
                    if (sourceMember is LookupMember)
                    {
                        yield break;
                    }
                    Member? entryMember = TryResolveCollectionEntryMember(sourceMember);
                    if (entryMember is null)
                    {
                        yield break;
                    }
                    foreach (var childId in arr.value)
                    {
                        yield return (childId, entryMember);
                    }
                    break;
            }
        }

        private Member? TryResolveOwnedChildMember(
            MemberValue row,
            Member? sourceMember,
            string key)
        {
            if (sourceMember is DictionaryMember dictionary)
            {
                return TryGetMember(dictionary.entryMemberId, out Member? entryMember)
                    ? entryMember
                    : null;
            }

            string? classId = (row as ObjectMemberValue)?.classId;
            if (string.IsNullOrEmpty(classId) && sourceMember is ClassMember classMember)
            {
                classId = classMember.classId;
            }
            if (string.IsNullOrEmpty(classId)) return null;
            if (!TryResolveMergedSchemaMember(classId!, key, out Member? childMember))
            {
                return null;
            }
            return childMember;
        }

        private Member? TryResolveCollectionEntryMember(Member? collectionMember)
        {
            string? entryMemberId = collectionMember switch
            {
                ListMember list => list.entryMemberId,
                DictionaryMember dictionary => dictionary.entryMemberId,
                _ => null,
            };
            return !string.IsNullOrEmpty(entryMemberId)
                && TryGetMember(entryMemberId!, out Member? entryMember)
                    ? entryMember
                    : null;
        }

        private bool TryResolveMergedSchemaMember(
            string classId,
            string key,
            [NotNullWhen(true)] out Member? member)
        {
            member = null;
            var merged = ResolveStoredInstanceSchema(classId);
            foreach (var entry in merged)
            {
                if (entry.schemaKey == key
                    && TryGetMember(entry.memberId, out Member? childMember))
                {
                    member = childMember;
                    return true;
                }
            }
            return false;
        }

        internal bool TryInferMemberForValueId(
            string valueId,
            [NotNullWhen(true)] out Member? member)
        {
            return TryInferMemberForValueId(valueId, new HashSet<string>(), out member);
        }

        private bool TryInferMemberForValueId(
            string valueId,
            HashSet<string> visitingValueIds,
            [NotNullWhen(true)] out Member? member)
        {
            // Value data is expected to be a tree, but inference also runs while
            // validating/importing hand-built or partially-mutated graphs. Keep
            // the traversal bounded when malformed owned edges form a cycle.
            // Each recursive branch receives a copy of this path set below, so
            // adding here detects only ancestors and does not suppress a valid
            // search through a sibling branch.
            if (!visitingValueIds.Add(valueId))
            {
                member = null;
                return false;
            }

            foreach (var candidate in data.members.Values)
            {
                if (candidate.valueId == valueId)
                {
                    member = candidate;
                    return true;
                }
            }

            // Unordered-list membership is stored on the child row rather
            // than in the list body's discriminator. Follow that back-pointer
            // before scanning body-owned placements so partition-loaded tile
            // entries infer the same declared entry member as ordered lists.
            if (TryGetValue(valueId, out MemberValue? containedValue)
                && !string.IsNullOrEmpty(containedValue.containerId)
                && TryInferMemberForValueId(
                    containedValue.containerId!,
                    new HashSet<string>(visitingValueIds),
                    out Member? containerMember)
                && TryResolveCollectionEntryMember(containerMember) is Member containedMember)
            {
                member = containedMember;
                return true;
            }

            foreach (var parent in EnumerateAllValueRows())
            {
                if (parent.Value is not ObjectMemberValue objectValue
                    || objectValue.value == null)
                {
                    continue;
                }

                foreach (var pair in objectValue.value)
                {
                    if (pair.Value != valueId) continue;
                    if (TryInferMemberForValueId(
                            parent.Key,
                            new HashSet<string>(visitingValueIds),
                            out Member? parentMember)
                        && TryResolveCollectionEntryMember(parentMember) is Member parentEntryMember)
                    {
                        member = parentEntryMember;
                        return true;
                    }

                    if (TryInferNeoSchemaClassIdForValueId(
                            parent.Key,
                            new HashSet<string>(visitingValueIds),
                            out string? parentClassId)
                        && !string.IsNullOrEmpty(parentClassId)
                        && TryResolveMergedSchemaMember(parentClassId!, pair.Key, out Member? childMember))
                    {
                        member = childMember;
                        return true;
                    }
                }
            }

            foreach (var parent in EnumerateAllValueRows())
            {
                if (parent.Value is ArrayMemberValue arrayValue
                    && arrayValue.value != null
                    && System.Array.IndexOf(arrayValue.value, valueId) >= 0
                    && TryInferMemberForValueId(
                        parent.Key,
                        new HashSet<string>(visitingValueIds),
                        out Member? collectionMember)
                    && TryResolveCollectionEntryMember(collectionMember) is Member entryMember)
                {
                    member = entryMember;
                    return true;
                }

                if (parent.Value is ObjectMemberValue dictionaryValue
                    && dictionaryValue.value != null
                    && dictionaryValue.value.ContainsValue(valueId)
                    && TryInferMemberForValueId(
                        parent.Key,
                        new HashSet<string>(visitingValueIds),
                        out collectionMember)
                    && TryResolveCollectionEntryMember(collectionMember) is Member dictionaryEntryMember)
                {
                    member = dictionaryEntryMember;
                    return true;
                }
            }

            member = null;
            return false;
        }

        private bool TryInferNeoSchemaClassIdForValueId(
            string valueId,
            HashSet<string> visitingValueIds,
            [NotNullWhen(true)] out string? classId)
        {
            if (!visitingValueIds.Add(valueId))
            {
                classId = null;
                return false;
            }
            if (TryGetValue(valueId, out ObjectMemberValue? value)
                && !string.IsNullOrEmpty(value.classId))
            {
                classId = value.classId;
                return true;
            }
            // Member inference owns the visit marker for this same node.
            // Keep ancestor markers, but let that traversal add valueId once;
            // otherwise the shared defensive path set would reject the first
            // legitimate inference step as though it were a cycle.
            visitingValueIds.Remove(valueId);
            if (TryInferMemberForValueId(
                    valueId,
                    visitingValueIds,
                    out Member? member)
                && member is ClassMember classMember
                && !string.IsNullOrEmpty(classMember.classId))
            {
                classId = classMember.classId;
                return true;
            }
            classId = null;
            return false;
        }

        private bool TryInferDirectMemberForValueId(
            string valueId,
            [NotNullWhen(true)] out Member? member)
        {
            foreach (var candidate in data.members.Values)
            {
                if (candidate.valueId == valueId)
                {
                    member = candidate;
                    return true;
                }
            }
            member = null;
            return false;
        }

        private IEnumerable<KeyValuePair<string, MemberValue>> EnumerateAllValueRows()
        {
            foreach (var pair in sessionData.values) yield return pair;
            foreach (var pair in saveData.values) yield return pair;
            foreach (var pair in data.values) yield return pair;
        }

        /// <summary>
        /// Resolves an authored value id to the nearest effective row carrying
        /// that id as <see cref="MemberValue.sourceValueId"/> in the lexical
        /// receiver's ownership graph. This is the runtime half of
        /// <c>Reference&lt;T&gt;(id: ..., withProvenance: true)</c>.
        /// </summary>
        internal bool TryResolveProvenanceReference(
            NeoValueOwnership receiverOwnership,
            string receiverValueId,
            string sourceValueId,
            [NotNullWhen(true)] out MemberValue? value,
            out NeoValueOwnership ownership)
        {
            // Resolve one effective overlay, just like TryGetValue does for an
            // evaluation context. Save and Session are independent graphs;
            // rows with the same stable id in the other overlay must neither
            // contribute parent edges nor become duplicate provenance matches.
            var rows = new List<
                (string valueId, MemberValue row, NeoValueOwnership ownership)>();
            ProjectSaveData? overlay = receiverOwnership == NeoValueOwnership.Asset
                ? null
                : GetWritableStore(receiverOwnership);
            if (overlay is not null)
            {
                foreach (var pair in overlay.values)
                {
                    if (!pair.Value.IsRemoved)
                    {
                        rows.Add((pair.Key, pair.Value, receiverOwnership));
                    }
                }
            }
            foreach (var pair in data.values)
            {
                if (overlay?.values.ContainsKey(pair.Key) == true) continue;
                rows.Add((
                    pair.Key,
                    pair.Value,
                    EffectiveAuthoredOwnership(pair.Key, pair.Value)));
            }
            if (!rows.Any(entry => entry.valueId == receiverValueId))
            {
                value = null;
                ownership = receiverOwnership;
                return false;
            }

            // Match the web evaluator's structural index exactly: every row
            // id contained by another row is a parent link, as is the explicit
            // containerId used by unordered lists. Do not key these links by
            // runtime storage ownership. Untyped List/Dictionary rows inherit
            // the ownership of their surrounding Class graph and may still be
            // represented by authored asset rows in a sparse Save/Session
            // overlay. Adding ownership here would split one constructed graph
            // at those aggregate rows.
            var parents = new Dictionary<string, HashSet<string>>();
            void AddParent(string childId, string parentId)
            {
                if (!parents.TryGetValue(childId, out var values))
                {
                    values = new HashSet<string>();
                    parents[childId] = values;
                }
                values.Add(parentId);
            }
            foreach (var entry in rows)
            {
                if (!string.IsNullOrEmpty(entry.row.containerId))
                {
                    AddParent(entry.valueId, entry.row.containerId!);
                }
                switch (entry.row)
                {
                    case ObjectMemberValue obj when obj.value is not null:
                        foreach (string childId in obj.value.Values)
                        {
                            AddParent(childId, entry.valueId);
                        }
                        break;
                    case ArrayMemberValue arr when arr.value is not null:
                        foreach (string childId in arr.value)
                        {
                            AddParent(childId, entry.valueId);
                        }
                        break;
                }
            }

            Dictionary<string, int> AncestorDistances(string start)
            {
                var distances = new Dictionary<string, int> { [start] = 0 };
                var pending = new Queue<string>();
                pending.Enqueue(start);
                while (pending.Count > 0)
                {
                    var current = pending.Dequeue();
                    int nextDistance = distances[current] + 1;
                    if (!parents.TryGetValue(current, out var currentParents))
                    {
                        continue;
                    }
                    foreach (var parent in currentParents)
                    {
                        if (distances.TryGetValue(parent, out int prior)
                            && prior <= nextDistance)
                        {
                            continue;
                        }
                        distances[parent] = nextDistance;
                        pending.Enqueue(parent);
                    }
                }
                return distances;
            }

            var receiverDistances = AncestorDistances(receiverValueId);
            int nearestDistance = int.MaxValue;
            var nearest = new List<
                (string valueId, MemberValue row, NeoValueOwnership ownership)>();
            foreach (var candidate in rows)
            {
                if (!string.Equals(
                        candidate.row.sourceValueId,
                        sourceValueId,
                        System.StringComparison.Ordinal))
                {
                    continue;
                }
                var candidateDistances = AncestorDistances(candidate.valueId);
                int distance = int.MaxValue;
                foreach (var receiverAncestor in receiverDistances)
                {
                    if (candidateDistances.TryGetValue(
                            receiverAncestor.Key,
                            out int candidateDistance))
                    {
                        distance = System.Math.Min(
                            distance,
                            receiverAncestor.Value + candidateDistance);
                    }
                }
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest.Clear();
                    nearest.Add(candidate);
                }
                else if (distance == nearestDistance
                    && distance != int.MaxValue)
                {
                    nearest.Add(candidate);
                }
            }

            if (nearest.Count == 0)
            {
                value = null;
                ownership = receiverOwnership;
                return false;
            }
            if (nearest.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Value reference '{sourceValueId}' is ambiguous within the constructed object graph.");
            }
            var selected = nearest[0];
            value = selected.row;
            ownership = selected.ownership;
            return true;
        }

        private static MemberValue CloneValueRow(MemberValue row)
        {
            MemberValue clone = row switch
            {
                NullMemberValue n => new NullMemberValue { value = n.value },
                BoolMemberValue b => new BoolMemberValue { value = b.value },
                NumberMemberValue n => new NumberMemberValue { value = n.value },
                StringMemberValue s => new StringMemberValue
                {
                    value = s.value,
                    neoLocalizationMode = s.neoLocalizationMode,
                },
                ArrayMemberValue a => new ArrayMemberValue
                {
                    value = a.value == null ? null : (string[])a.value.Clone(),
                },
                ObjectMemberValue o => new ObjectMemberValue
                {
                    value = o.value == null ? null : new Dictionary<string, string>(o.value),
                },
                FileMemberValue f => new FileMemberValue
                {
                    value = f.value == null ? null : new FileValue { fileId = f.value.fileId },
                },
                SpriteMemberValue s => new SpriteMemberValue
                {
                    value = s.value == null
                        ? null
                        : new SpriteValue
                        {
                            fileId = s.value.fileId,
                            sliceIndex = s.value.sliceIndex,
                        },
                },
                Vector2MemberValue v => new Vector2MemberValue
                {
                    value = v.value == null
                        ? null
                        : new NeoVector2Value
                        {
                            x = v.value.x,
                            y = v.value.y,
                        },
                },
                Vector3MemberValue v => new Vector3MemberValue
                {
                    value = v.value == null
                        ? null
                        : new NeoVector3Value
                        {
                            x = v.value.x,
                            y = v.value.y,
                            z = v.value.z,
                        },
                },
                ColorMemberValue c => new ColorMemberValue
                {
                    value = c.value == null
                        ? null
                        : new NeoColorValue
                        {
                            r = c.value.r,
                            g = c.value.g,
                            b = c.value.b,
                            a = c.value.a,
                        },
                },
                // A listener set is mutable, so the shadow deep-copies it:
                // a runtime `+=` on the Save/Session row must not reach the
                // authored asset row it was cloned from (P62 §3.3).
                ActionMemberValue a => new ActionMemberValue
                {
                    value = a.value?.PersistedCopy() ?? new NeoActionValue(),
                },
                // A delegate payload may carry a transient lexical capture,
                // so the shadow takes a persisted copy: only the wire union
                // (member target or closure) crosses into the Save/Session
                // row. An unbound row stays null — there is no empty value
                // in the strict NeoDelegate union.
                DelegateMemberValue d => new DelegateMemberValue
                {
                    value = d.value?.PersistedCopy(),
                },
                // A P42 '$partial' envelope is plain scalar field tokens, so
                // the shadow deep-copies them: a later field write on the
                // Save/Session row must not reach the authored envelope it
                // was cloned from.
                PartialLeafMemberValue p => new PartialLeafMemberValue
                {
                    value = p.value?.Clone(),
                },
                _ => throw new System.InvalidOperationException(
                    $"Unsupported save value row type '{row.GetType().Name}'."),
            };
            clone.id = row.id;
            clone.createdAt = row.createdAt;
            clone.updatedAt = row.updatedAt;
            clone.classId = row.classId;
            // containerId is immutable membership identity: a clone-on-write
            // shadow of a member row stays a member of the same container.
            clone.containerId = row.containerId;
            // mapKey is immutable partition identity: a shadow of a
            // partition-stamped row stays in the same storage partition.
            clone.mapKey = row.mapKey;
            // Authored-child provenance is immutable placement identity and
            // must survive every Save/Session clone-on-write shadow.
            clone.sourceValueId = row.sourceValueId;
            clone.constructorArgs = row.constructorArgs is null
                ? null
                : row.constructorArgs.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value?.DeepClone());
            if (row.hasInstanceConstructorId)
            {
                clone.instanceConstructorId = row.instanceConstructorId;
            }
            clone.instanceVariantId = row.instanceVariantId;
            clone.instanceVariantRowValueId = row.instanceVariantRowValueId;
            // genericBindings is immutable creation-time context
            // (specs/class-generics.md Decision 9): a shadow of a
            // stamped collection row keeps its entry-substitution stamp.
            clone.genericBindings = row.genericBindings is null
                ? null
                : new Dictionary<string, string>(row.genericBindings);
            clone.mark = row.mark;
            return clone;
        }

        /// <summary>
        /// Recursively deletes <paramref name="valueId"/> and every value
        /// reached by an authoritative owned edge from
        /// <see cref="ProjectSaveData.values"/>. Walks
        /// <see cref="ObjectMemberValue"/> Class/Dictionary records and
        /// <see cref="ArrayMemberValue"/> Lists. Lookup selections and
        /// other reference-style links are intentionally not followed.
        ///
        /// <para>Used by collection <c>*Writable</c> Remove operations to
        /// keep the save file lean — when a parent removes a child
        /// reference, the child's value (and any descendants it owned)
        /// becomes unreachable and shouldn't sit in the save forever.</para>
        ///
        /// <para>Only mutates <see cref="ProjectSaveData.values"/> —
        /// <see cref="ProjectData"/> (the authored asset tree) is
        /// never touched. Values that exist only in the asset tree
        /// (no save override) won't be in <c>saveData.values</c> in the
        /// first place, so the recursion safely skips them.</para>
        /// </summary>
        internal void RemoveSaveValueAndDescendants(string valueId)
        {
            RemoveWritableValueAndDescendants(NeoValueOwnership.Save, valueId);
        }

        internal void RemoveWritableValueAndDescendants(
            NeoValueOwnership ownership,
            string valueId)
        {
            Member? sourceMember = TryInferMemberForValueId(valueId, out Member? inferred)
                ? inferred
                : null;
            RemoveWritableValueAndDescendantsCore(
                ownership,
                valueId,
                sourceMember,
                new HashSet<string>(),
                removed: null);
        }

        internal void RemoveWritableValueAndDescendants(
            NeoValueOwnership ownership,
            string valueId,
            Member? sourceMember)
        {
            RemoveWritableValueAndDescendantsCore(
                ownership,
                valueId,
                sourceMember,
                new HashSet<string>(),
                removed: null);
        }

        /// <summary>
        /// Reclaims a NeoScript constructor allocation after the allocation
        /// tracker has proved that it neither escaped nor acquired an
        /// authoritative external owner. Unlike ordinary orphan GC, this
        /// deliberately ignores unloaded-partition reachability protection:
        /// every row in this graph was minted by the current invocation and
        /// cannot belong to an unloaded authored graph.
        /// </summary>
        internal IReadOnlyCollection<string> RemoveTemporaryWritableValueGraph(
            NeoValueOwnership ownership,
            string valueId)
        {
            Member? sourceMember = TryInferMemberForValueId(
                valueId,
                out Member? inferred)
                    ? inferred
                    : null;
            var removed = new HashSet<string>();
            RemoveWritableValueAndDescendantsCore(
                ownership,
                valueId,
                sourceMember,
                new HashSet<string>(),
                removed);
            return removed;
        }

        private void RemoveWritableValueAndDescendantsCore(
            NeoValueOwnership ownership,
            string valueId,
            Member? sourceMember,
            HashSet<string> visited,
            HashSet<string>? removed)
        {
            if (!visited.Add(valueId)) return;
            var store = GetWritableStore(ownership);
            if (!store.values.TryGetValue(valueId, out MemberValue val)) return;
            // Follow only authoritative owned edges. Lookup selections and
            // other reference payloads deliberately survive deletion. A
            // defensive visited set prevents malformed cyclic data from
            // overflowing the stack while retaining bottom-up removal.
            foreach (var child in EnumerateOwnedChildLinks(val, sourceMember))
            {
                NeoValueOwnership childOwnership =
                    (child.member is null ? null : DeclaredOwnership(child.member)) ?? ownership;
                if (childOwnership != ownership) continue;
                RemoveWritableValueAndDescendantsCore(
                    ownership,
                    child.valueId,
                    child.member,
                    visited,
                    removed);
            }
            if (sourceMember is ListMember list && IsUnorderedList(list))
            {
                Member? entryMember = TryResolveCollectionEntryMember(sourceMember);
                NeoValueOwnership entryOwnership =
                    (entryMember is null ? null : DeclaredOwnership(entryMember)) ?? ownership;
                if (entryMember is not null && entryOwnership == ownership)
                {
                    foreach (string memberId in EnumerateContainerMemberValueIds(ownership, valueId))
                    {
                        RemoveWritableValueAndDescendantsCore(
                            ownership,
                            memberId,
                            entryMember,
                            visited,
                            removed);
                    }
                }
            }
            TryResolveContainerIdForValueId(valueId, out string? memberContainerId);
            if (store.values.Remove(valueId))
            {
                removed?.Add(valueId);
                IndexStoreRemove(ownership, valueId);
                TouchWritableStoreUpdatedAt(ownership);
                OnWritableValueChanged?.Invoke(ownership, valueId);
                if (memberContainerId is not null)
                {
                    RaiseContainerChanged(ownership, memberContainerId);
                }
                if (ownership == NeoValueOwnership.Save)
                {
                    RaiseSaveValueChanged(valueId);
                }
            }
        }

        internal void RemoveSaveValueAndDescendantsIfUnlinked(string valueId)
        {
            var removed = new HashSet<string>();
            RemoveWritableValueAndDescendantsIfUnlinked(
                NeoValueOwnership.Save,
                valueId,
                BuildReachableSaveValueIds(),
                removed);
        }

        internal IReadOnlyCollection<string> RemoveWritableValueAndDescendantsIfUnlinked(
            NeoValueOwnership ownership,
            string valueId)
        {
            var removed = new HashSet<string>();
            RemoveWritableValueAndDescendantsIfUnlinked(
                ownership,
                valueId,
                BuildReachableWritableValueIds(ownership),
                removed);
            return removed;
        }

        private void RemoveWritableValueAndDescendantsIfUnlinked(
            NeoValueOwnership ownership,
            string valueId,
            HashSet<string> reachable,
            HashSet<string> removed)
        {
            Member? sourceMember = TryInferMemberForValueId(valueId, out Member? inferred)
                ? inferred
                : null;
            RemoveWritableValueAndDescendantsIfUnlinked(
                ownership,
                valueId,
                reachable,
                sourceMember,
                new HashSet<string>(),
                removed);
        }

        internal IReadOnlyCollection<string> RemoveWritableValueAndDescendantsIfUnlinked(
            NeoValueOwnership ownership,
            string valueId,
            Member? sourceMember)
        {
            var removed = new HashSet<string>();
            RemoveWritableValueAndDescendantsIfUnlinked(
                ownership,
                valueId,
                BuildReachableWritableValueIds(ownership),
                sourceMember,
                new HashSet<string>(),
                removed);
            return removed;
        }

        private void RemoveWritableValueAndDescendantsIfUnlinked(
            NeoValueOwnership ownership,
            string valueId,
            HashSet<string> reachable,
            Member? sourceMember,
            HashSet<string> visited,
            HashSet<string> removed)
        {
            if (reachable.Contains(valueId)) return;
            if (!visited.Add(valueId)) return;
            var store = GetWritableStore(ownership);
            if (!store.values.TryGetValue(valueId, out MemberValue val)) return;

            foreach (var child in EnumerateOwnedChildLinks(val, sourceMember))
            {
                NeoValueOwnership childOwnership =
                    (child.member is null ? null : DeclaredOwnership(child.member)) ?? ownership;
                if (childOwnership != ownership) continue;
                RemoveWritableValueAndDescendantsIfUnlinked(
                    ownership,
                    child.valueId,
                    reachable,
                    child.member,
                    visited,
                    removed);
            }
            if (sourceMember is ListMember list && IsUnorderedList(list))
            {
                Member? entryMember = TryResolveCollectionEntryMember(sourceMember);
                NeoValueOwnership entryOwnership =
                    (entryMember is null ? null : DeclaredOwnership(entryMember)) ?? ownership;
                if (entryMember is not null && entryOwnership == ownership)
                {
                    foreach (string memberId in EnumerateContainerMemberValueIds(ownership, valueId))
                    {
                        RemoveWritableValueAndDescendantsIfUnlinked(
                            ownership,
                            memberId,
                            reachable,
                            entryMember,
                            visited,
                            removed);
                    }
                }
            }
            TryResolveContainerIdForValueId(valueId, out string? memberContainerId);
            if (store.values.Remove(valueId))
            {
                removed.Add(valueId);
                IndexStoreRemove(ownership, valueId);
                TouchWritableStoreUpdatedAt(ownership);
                OnWritableValueChanged?.Invoke(ownership, valueId);
                if (memberContainerId is not null)
                {
                    RaiseContainerChanged(ownership, memberContainerId);
                }
                if (ownership == NeoValueOwnership.Save)
                {
                    RaiseSaveValueChanged(valueId);
                }
            }
        }


        internal bool TryGetWritableValue<TValue>(
            NeoValueOwnership ownership,
            string id,
            [NotNullWhen(true)] out TValue? value) where TValue : MemberValue
        {
            value = null;
            if (ownership == NeoValueOwnership.Asset) return false;
            if (!GetWritableStore(ownership).values.TryGetValue(id, out MemberValue row))
            {
                return false;
            }
            if (row is not TValue typed)
            {
                return false;
            }
            value = typed;
            return true;
        }

        private ProjectSaveData GetWritableStore(NeoValueOwnership ownership)
        {
            return ownership switch
            {
                NeoValueOwnership.Save => saveData,
                NeoValueOwnership.Session => sessionData,
                _ => throw new System.InvalidOperationException(
                    $"Ownership '{ownership}' does not have a writable store."),
            };
        }

        // -----------------------------------------------------------------
        // Unordered-list membership index (specs/list-member-and-tilegrid
        // -scaling.md §1.2/§3.3). Membership of an unordered list value is
        // the set of live rows whose `containerId` names it. The index is
        // layered like the value overlay: authored rows (data.values) plus
        // per-store overlay rows join; an overlay tombstone
        // (`mark: "removed"`) at a member id subtracts that member.
        // Built in one pass at load and maintained incrementally by the
        // store write/remove chokepoints below.
        // -----------------------------------------------------------------

        private readonly Dictionary<string, HashSet<string>> authoredEntriesByContainer = new();
        private readonly Dictionary<string, string> authoredContainerByRow = new();
        private readonly Dictionary<string, HashSet<string>> saveEntriesByContainer = new();
        private readonly Dictionary<string, string> saveContainerByRow = new();
        private readonly Dictionary<string, HashSet<string>> sessionEntriesByContainer = new();
        private readonly Dictionary<string, string> sessionContainerByRow = new();

        private void BuildMembershipIndex()
        {
            authoredEntriesByContainer.Clear();
            authoredContainerByRow.Clear();
            foreach (var row in data.values.Values)
            {
                if (string.IsNullOrEmpty(row.containerId)) continue;
                AddMembership(
                    authoredEntriesByContainer, authoredContainerByRow, row.id, row.containerId!);
            }
            RebuildStoreMembership(NeoValueOwnership.Save);
            RebuildStoreMembership(NeoValueOwnership.Session);
        }

        private void RebuildStoreMembership(NeoValueOwnership ownership)
        {
            var (byContainer, byRow) = MembershipMaps(ownership);
            byContainer.Clear();
            byRow.Clear();
            foreach (var row in GetWritableStore(ownership).values.Values)
            {
                if (string.IsNullOrEmpty(row.containerId)) continue;
                AddMembership(byContainer, byRow, row.id, row.containerId!);
            }
        }

        private (Dictionary<string, HashSet<string>> byContainer, Dictionary<string, string> byRow)
            MembershipMaps(NeoValueOwnership ownership)
        {
            return ownership switch
            {
                NeoValueOwnership.Save => (saveEntriesByContainer, saveContainerByRow),
                NeoValueOwnership.Session => (sessionEntriesByContainer, sessionContainerByRow),
                _ => throw new System.InvalidOperationException(
                    $"Ownership '{ownership}' does not have a membership index."),
            };
        }

        private static void AddMembership(
            Dictionary<string, HashSet<string>> byContainer,
            Dictionary<string, string> byRow,
            string rowId,
            string containerId)
        {
            if (!byContainer.TryGetValue(containerId, out var members))
            {
                members = new HashSet<string>();
                byContainer[containerId] = members;
            }
            members.Add(rowId);
            byRow[rowId] = containerId;
        }

        /// <summary>Index maintenance chokepoint for a store write at <c>value.id</c>.</summary>
        private void IndexStoreWrite(NeoValueOwnership ownership, MemberValue value)
        {
            var (byContainer, byRow) = MembershipMaps(ownership);
            if (byRow.TryGetValue(value.id, out string previousContainerId)
                && previousContainerId != value.containerId)
            {
                if (byContainer.TryGetValue(previousContainerId, out var previousMembers))
                {
                    previousMembers.Remove(value.id);
                }
                byRow.Remove(value.id);
            }
            if (!string.IsNullOrEmpty(value.containerId))
            {
                AddMembership(byContainer, byRow, value.id, value.containerId!);
            }
        }

        /// <summary>Index maintenance chokepoint for a store removal at <paramref name="id"/>.</summary>
        private void IndexStoreRemove(NeoValueOwnership ownership, string id)
        {
            var (byContainer, byRow) = MembershipMaps(ownership);
            if (!byRow.TryGetValue(id, out string containerId)) return;
            if (byContainer.TryGetValue(containerId, out var members))
            {
                members.Remove(id);
            }
            byRow.Remove(id);
        }

        // -----------------------------------------------------------------
        // Storage partitions (specs/list-member-and-tilegrid-scaling.md
        // §6). A partition is a named subset of the authored values map that
        // ships under `project.json`'s `valuePartitions[mapKey]` and stays
        // raw JSON until loaded. Loading materializes the partition's rows
        // into the ONE authored dictionary (in-memory stays a single map per
        // ownership); unloading removes exactly those rows again.
        //
        // A world grid keys its partition on its CONCRETE grid type id —
        // `world:<gridClassId>` — and the partition covers ONLY the grid's
        // `Children` placement subtree. The grid root row and its light
        // metadata (CellSize, PixelsPerUnit, DisplayName, and the palette
        // reference lookups Tiles/TileLayers/Objects/ObjectLayers) stay in
        // the main partition, so worlds are enumerable and nameable without
        // loading heavy placement content. Two grid instances of the same
        // concrete leaf type co-load one partition — still correct, value
        // rows carry unique ids. The partition is auto-loaded lazily by the
        // tile grid primitive's content-resolution path.
        //
        // Partition subtrees are asset content by contract (Save/Session
        // storage overrides force the "main" partition on the web side), so
        // load/unload never touches the authored-ownership overlay map.
        // -----------------------------------------------------------------

        private readonly Dictionary<string, HashSet<string>> loadedPartitionRowIds = new();

        /// <summary>Partition keys currently merged into the authored values map.</summary>
        public IReadOnlyCollection<string> LoadedValuePartitions => loadedPartitionRowIds.Keys;

        /// <summary>
        /// Raised after a partition's rows are merged into (load) or removed
        /// from (unload) the authored values map. Derived indexes over
        /// authored content (tile grid lookup caches) key on this to drop and
        /// lazily rebuild.
        /// </summary>
        internal event System.Action<string>? OnValuePartitionChanged;

        /// <summary>True when the partition's rows are currently loaded.</summary>
        public bool IsValuePartitionLoaded(string mapKey) =>
            loadedPartitionRowIds.ContainsKey(mapKey);

        /// <summary>True when the export ships a partition under <paramref name="mapKey"/>.</summary>
        internal bool HasValuePartition(string mapKey) =>
            data.valuePartitions is not null && data.valuePartitions.ContainsKey(mapKey);

        /// <summary>
        /// Auto-load hook for the tile grid resolution path: loads the grid's
        /// <c>world:&lt;gridClassId&gt;</c> placement partition when the export
        /// ships one and it isn't loaded yet. The grid root row lives in the
        /// main partition, so its concrete class id — the partition key — is
        /// resolvable before the placement subtree loads. No-op when the bound
        /// value id resolves no row yet (a deep placement node binding before
        /// its own partition is merged), carries no class id, or names a grid
        /// whose content is authored in the main partition, so the public
        /// GetTile/etc. surface works unchanged either way.
        /// </summary>
        internal void EnsureWorldPartitionLoaded(string gridValueId)
        {
            string? gridClassId = ResolveEffectiveRow(gridValueId)?.classId;
            if (string.IsNullOrEmpty(gridClassId)) return;
            string mapKey = MakeWorldPartitionKey(gridClassId!);
            if (loadedPartitionRowIds.ContainsKey(mapKey)) return;
            if (!HasValuePartition(mapKey)) return;
            LoadValuePartition(mapKey);
        }

        /// <summary>The partition key a world grid's placement subtree is
        /// stamped with — derived from the grid's concrete class id.</summary>
        public static string MakeWorldPartitionKey(string gridClassId) => $"world:{gridClassId}";

        /// <summary>
        /// Materializes the partition's raw rows into typed
        /// <see cref="MemberValue"/>s and merges them into the authored
        /// values map, updating the membership index incrementally and
        /// invalidating derived indexes. Idempotent — loading a loaded
        /// partition is a no-op. Throws when the export has no such
        /// partition (listing the available keys).
        /// </summary>
        public void LoadValuePartition(string mapKey)
        {
            EnsureNotDisposed();
            if (string.IsNullOrEmpty(mapKey))
            {
                throw new System.ArgumentException(
                    "Partition mapKey cannot be null or empty.", nameof(mapKey));
            }
            if (loadedPartitionRowIds.ContainsKey(mapKey)) return;
            if (data.valuePartitions is null
                || !data.valuePartitions.TryGetValue(mapKey, out Newtonsoft.Json.Linq.JToken token))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(mapKey),
                    $"Project export has no value partition '{mapKey}'. Available partitions: [{string.Join(", ", AvailableValuePartitionKeys())}].");
            }
            if (token is not Newtonsoft.Json.Linq.JObject partitionObject)
            {
                throw new System.InvalidOperationException(
                    $"Value partition '{mapKey}' is not a JSON object of value rows (got {token.Type}).");
            }

            var rows = partitionObject.ToObject<Dictionary<string, MemberValue>>();
            if (rows is null)
            {
                throw new System.InvalidOperationException(
                    $"Value partition '{mapKey}' could not be deserialized into value rows.");
            }

            var rowIds = new HashSet<string>();
            foreach (var pair in rows)
            {
                MemberValue row = pair.Value;
                if (row.id != pair.Key)
                {
                    throw new System.InvalidOperationException(
                        $"Value partition '{mapKey}' row keyed '{pair.Key}' carries mismatched id '{row.id}'.");
                }
                if (data.values.ContainsKey(row.id))
                {
                    throw new System.InvalidOperationException(
                        $"Value partition '{mapKey}' row '{row.id}' collides with a value id already loaded in another partition or the main values map.");
                }
                if (!string.IsNullOrEmpty(row.mapKey) && row.mapKey != mapKey)
                {
                    throw new System.InvalidOperationException(
                        $"Value partition '{mapKey}' row '{row.id}' is stamped with a different partition '{row.mapKey}'.");
                }
                // Partition residency is the stamp's source of truth —
                // self-heal a missing per-row stamp.
                row.mapKey = mapKey;
                data.values[row.id] = row;
                rowIds.Add(row.id);
                if (!string.IsNullOrEmpty(row.containerId))
                {
                    AddMembership(
                        authoredEntriesByContainer, authoredContainerByRow, row.id, row.containerId!);
                }
            }
            loadedPartitionRowIds[mapKey] = rowIds;
            // A partition owns its placement roots. Replaying only those rows
            // avoids O(project) work and prevents an unrelated malformed root
            // elsewhere in the corpus from breaking this load.
            InitializeVirtualInstanceValuesForLoadedRows(rows.Values);
            OnValuePartitionChanged?.Invoke(mapKey);
        }

        /// <summary>
        /// Removes a loaded partition's authored rows and the derived
        /// index entries / member wrappers touching them. Throws when the
        /// partition isn't loaded, and when a save/session overlay still
        /// shadows a row in the partition — unloading with pending overlay
        /// writes is a caller bug (commit or discard them first).
        /// </summary>
        public void UnloadValuePartition(string mapKey)
        {
            EnsureNotDisposed();
            if (!loadedPartitionRowIds.TryGetValue(mapKey, out HashSet<string> rowIds))
            {
                throw new System.InvalidOperationException(
                    $"Value partition '{mapKey}' is not loaded. Loaded partitions: [{string.Join(", ", loadedPartitionRowIds.Keys)}].");
            }
            ThrowIfOverlayShadowsPartition(NeoValueOwnership.Save, mapKey, rowIds);
            ThrowIfOverlayShadowsPartition(NeoValueOwnership.Session, mapKey, rowIds);
            IReadOnlyCollection<string> virtualRowIds =
                ClearVirtualInstanceValuesForAuthoredRows(rowIds);
            DisposeWrappersTouchingRows(rowIds.Concat(virtualRowIds));
            foreach (var rowId in rowIds)
            {
                if (authoredContainerByRow.TryGetValue(rowId, out string containerId))
                {
                    if (authoredEntriesByContainer.TryGetValue(containerId, out var members))
                    {
                        members.Remove(rowId);
                    }
                    authoredContainerByRow.Remove(rowId);
                }
                data.values.Remove(rowId);
            }
            loadedPartitionRowIds.Remove(mapKey);
            OnValuePartitionChanged?.Invoke(mapKey);
        }

        private IEnumerable<string> AvailableValuePartitionKeys()
        {
            if (data.valuePartitions is null) yield break;
            foreach (var key in data.valuePartitions.Keys) yield return key;
        }

        private void ThrowIfOverlayShadowsPartition(
            NeoValueOwnership ownership,
            string mapKey,
            HashSet<string> rowIds)
        {
            foreach (var pair in GetWritableStore(ownership).values)
            {
                bool inPartition = rowIds.Contains(pair.Key) || pair.Value.mapKey == mapKey;
                if (!inPartition) continue;
                throw new System.InvalidOperationException(
                    $"Cannot unload value partition '{mapKey}': the {ownership} overlay still shadows row '{pair.Key}' in that partition. Commit or discard the overlay writes before unloading.");
            }
        }

        /// <summary>
        /// Disposes every registered <see cref="NeoMember"/> wrapper (and
        /// generated class value) bound to one of the partition rows being
        /// unloaded, so no live wrapper keeps referencing a removed row.
        /// </summary>
        internal void DisposeWrappersTouchingRows(IEnumerable<string> rowIds)
        {
            HashSet<string> rowIdSet = rowIds as HashSet<string>
                ?? new HashSet<string>(rowIds);
            var staleNodes = new List<NeoMember>();
            foreach (var node in nodesInternal.Values)
            {
                bool touches =
                    (node.value is not null && rowIdSet.Contains(node.value.id))
                    || (node.overrideValueId is not null && rowIdSet.Contains(node.overrideValueId));
                if (touches) staleNodes.Add(node);
            }
            var staleGenerated = new List<NeoGeneratedClassValue>();
            foreach (var generated in generatedValuesInternal.Values)
            {
                if (generated.valueId is not null && rowIdSet.Contains(generated.valueId))
                {
                    staleGenerated.Add(generated);
                }
            }
            foreach (var generated in staleGenerated)
            {
                generated.Dispose();
            }
            foreach (var node in staleNodes)
            {
                node.Dispose();
            }
        }

        /// <summary>
        /// Partition-stamp inheritance chokepoint for overlay writes (spec
        /// §6): a row written without a stamp inherits, in order, the stamp
        /// of the row it shadows (authored, or the save row beneath a session
        /// write), then its containment container's effective stamp (created
        /// member rows live in their container's partition). Rows that
        /// resolve no stamp are main-partition and stay unstamped.
        /// </summary>
        private void StampMapKeyForWrite(NeoValueOwnership ownership, MemberValue value)
        {
            if (!string.IsNullOrEmpty(value.mapKey)) return;
            if (data.values.TryGetValue(value.id, out MemberValue authored))
            {
                value.mapKey = authored.mapKey;
                return;
            }
            if (ownership == NeoValueOwnership.Session
                && saveData.values.TryGetValue(value.id, out MemberValue saveRow)
                && !string.IsNullOrEmpty(saveRow.mapKey))
            {
                value.mapKey = saveRow.mapKey;
                return;
            }
            if (string.IsNullOrEmpty(value.containerId)) return;
            value.mapKey = ResolveEffectiveRow(value.containerId!)?.mapKey;
        }

        /// <summary>
        /// Resolves the container the row at <paramref name="valueId"/>
        /// belongs to (its stamped <see cref="MemberValue.containerId"/>),
        /// looking across all layers — including through overlay tombstones,
        /// which carry no containerId themselves but subtract an authored or
        /// lower-layer member.
        /// </summary>
        internal bool TryResolveContainerIdForValueId(
            string valueId,
            [NotNullWhen(true)] out string? containerId)
        {
            if (sessionContainerByRow.TryGetValue(valueId, out string sessionContainer))
            {
                containerId = sessionContainer;
                return true;
            }
            if (saveContainerByRow.TryGetValue(valueId, out string saveContainer))
            {
                containerId = saveContainer;
                return true;
            }
            if (authoredContainerByRow.TryGetValue(valueId, out string authoredContainer))
            {
                containerId = authoredContainer;
                return true;
            }
            if (virtualContainerByRow.TryGetValue(valueId, out string virtualContainer))
            {
                containerId = virtualContainer;
                return true;
            }
            containerId = null;
            return false;
        }

        /// <summary>
        /// Live member ids of the unordered list value at
        /// <paramref name="containerValueId"/>, id-sorted (ordinal) for
        /// deterministic enumeration. Respects the overlay cascade AND the
        /// null-vs-present discriminator: the container value resolves
        /// through the overlay (session → save → authored); a missing,
        /// tombstoned, or <c>null</c>-valued container yields no members.
        /// </summary>
        internal IReadOnlyCollection<string> GetUnorderedListEntryIds(string containerValueId)
        {
            var containerRow = ResolveEffectiveRow(containerValueId);
            if (containerRow is null) return System.Array.Empty<string>();
            if (containerRow.IsRemoved) return System.Array.Empty<string>();
            if (containerRow is not ArrayMemberValue arrayRow) return System.Array.Empty<string>();
            if (arrayRow.value is null) return System.Array.Empty<string>();

            var members = new List<string>();
            var seen = new HashSet<string>();
            CollectLiveMembers(authoredEntriesByContainer, containerValueId, seen, members);
            CollectLiveMembers(virtualEntriesByContainer, containerValueId, seen, members);
            CollectLiveMembers(saveEntriesByContainer, containerValueId, seen, members);
            CollectLiveMembers(sessionEntriesByContainer, containerValueId, seen, members);
            members.Sort(System.StringComparer.Ordinal);
            return members;
        }

        private void CollectLiveMembers(
            Dictionary<string, HashSet<string>> byContainer,
            string containerValueId,
            HashSet<string> seen,
            List<string> members)
        {
            if (!byContainer.TryGetValue(containerValueId, out var candidates)) return;
            foreach (var memberId in candidates)
            {
                if (!seen.Add(memberId)) continue;
                var effective = ResolveEffectiveRow(memberId);
                if (effective is null) continue;
                if (effective.IsRemoved) continue;
                members.Add(memberId);
            }
        }

        /// <summary>Raw overlay resolution (session → save → authored) WITHOUT
        /// tombstone fallthrough — a tombstone row is returned as-is so callers
        /// can distinguish "explicitly removed" from "absent".</summary>
        internal MemberValue? ResolveEffectiveRow(string valueId)
        {
            if (sessionData.values.TryGetValue(valueId, out MemberValue sessionRow)) return sessionRow;
            if (saveData.values.TryGetValue(valueId, out MemberValue saveRow)) return saveRow;
            if (data.values.TryGetValue(valueId, out MemberValue authoredRow)) return authoredRow;
            if (virtualValues.TryGetValue(valueId, out MemberValue virtualRow)) return virtualRow;
            return null;
        }

        /// <summary>
        /// Notifies subscribers bound to a member row's CONTAINER that its
        /// membership changed (a member row was added, replaced, tombstoned,
        /// or dropped). Unordered containment never writes the container row
        /// itself, so this is the coarse invalidation signal container-bound
        /// consumers (spatial indexes, link renderers) key on.
        /// </summary>
        private void NotifyContainerMembershipChanged(
            NeoValueOwnership ownership,
            string memberValueId)
        {
            if (!TryResolveContainerIdForValueId(memberValueId, out string? containerId)) return;
            RaiseContainerChanged(ownership, containerId!);
        }

        // Bulk membership operations (Clear, whole-list assignment) suspend
        // per-member container notifications and flush one coalesced
        // notification per container when the scope disposes, so container
        // subscribers observe a single membership change per bulk edit.
        private int containerNotificationSuspensions;
        private readonly List<(NeoValueOwnership ownership, string containerId)>
            pendingContainerNotifications = new();

        internal System.IDisposable SuspendContainerNotifications()
        {
            containerNotificationSuspensions += 1;
            return new NeoDisposableAction(FlushContainerNotifications);
        }

        private void FlushContainerNotifications()
        {
            containerNotificationSuspensions -= 1;
            if (containerNotificationSuspensions > 0) return;
            if (pendingContainerNotifications.Count == 0) return;
            var pending = new List<(NeoValueOwnership, string)>(pendingContainerNotifications);
            pendingContainerNotifications.Clear();
            foreach (var (ownership, containerId) in pending)
            {
                OnWritableValueChanged?.Invoke(ownership, containerId);
            }
        }

        private void RaiseContainerChanged(NeoValueOwnership ownership, string containerId)
        {
            if (containerNotificationSuspensions > 0)
            {
                if (!pendingContainerNotifications.Contains((ownership, containerId)))
                {
                    pendingContainerNotifications.Add((ownership, containerId));
                }
                return;
            }
            OnWritableValueChanged?.Invoke(ownership, containerId);
        }

        /// <summary>
        /// Resolves whether <paramref name="member"/> declares the
        /// unordered list kind, walking the <see cref="Member.extendsMemberId"/>
        /// override chain like other inherited member fields.
        /// </summary>
        internal bool IsUnorderedList(ListMember member)
        {
            string? listKind = NeoSchemaClassInheritance.WalkExtendsMemberChain(
                member,
                id => data.members.TryGetValue(id, out Member? value) ? value : null,
                current => current is ListMember list
                    && !string.IsNullOrEmpty(list.listKind)
                        ? list.listKind
                        : null,
                requireKind: MemberKind.List);
            return listKind == NeoListKinds.Unordered;
        }

        internal bool TryResolveLookupCollectionValueId(
            string collectionMemberId,
            string? collectionValueId,
            [NotNullWhen(true)] out string? valueId)
        {
            valueId = null;
            if (!TryGetMember(collectionMemberId, out Member? collectionMember))
            {
                return false;
            }

            valueId = ResolveLookupCollectionValueId(collectionMember, collectionValueId);
            return valueId is not null;
        }

        private string? ResolveLookupCollectionValueId(
            Member collectionMember,
            string? collectionValueId)
        {
            if (collectionValueId is not null) return collectionValueId;
            if (collectionMember.isStatic)
            {
                return TryResolveStaticBinding(
                    collectionMember.id,
                    out _,
                    out _,
                    out string? staticValueId)
                        ? staticValueId
                        : null;
            }
            // Stable-id overlay: the collection resolves by its authored id
            // (a save/session shadows that id in place), so there is no
            // override-map hop — fall through to the schema binding /
            // authored valueId.
            if (TryFindBoundValueIdForMember(collectionMember.id, out string? boundValueId))
            {
                return boundValueId;
            }
            return collectionMember.valueId;
        }

        private bool TryFindBoundValueIdForMember(
            string memberId,
            [NotNullWhen(true)] out string? valueId)
        {
            valueId = null;
            var schemaKeys = new HashSet<string>();
            foreach (var schemaClass in data.classes.Values)
            {
                foreach (var pair in schemaClass.schema)
                {
                    if (pair.Value == memberId)
                    {
                        schemaKeys.Add(pair.Key);
                    }
                }
            }
            if (schemaKeys.Count == 0) return false;

            var candidates = new List<string>();
            AddBoundValueCandidates(sessionData.values.Values, schemaKeys, candidates);
            AddBoundValueCandidates(saveData.values.Values, schemaKeys, candidates);
            AddBoundValueCandidates(data.values.Values, schemaKeys, candidates);

            foreach (var candidate in candidates)
            {
                if (valueId is null)
                {
                    valueId = candidate;
                    continue;
                }
                if (valueId != candidate)
                {
                    valueId = null;
                    return false;
                }
            }
            return valueId is not null;
        }

        private static void AddBoundValueCandidates(
            IEnumerable<MemberValue> rows,
            HashSet<string> schemaKeys,
            List<string> candidates)
        {
            foreach (var row in rows)
            {
                if (row is not ObjectMemberValue obj || obj.value is null) continue;
                foreach (var key in schemaKeys)
                {
                    if (obj.value.TryGetValue(key, out string childValueId)
                        && !candidates.Contains(childValueId))
                    {
                        candidates.Add(childValueId);
                    }
                }
            }
        }

        /// <summary>
        /// Looks up a previously-registered <see cref="NeoMember"/>
        /// by member id (and optional override-value id). Returns
        /// false when nothing is registered for the composed key.
        /// Callers typically reach for
        /// <see cref="NeoMember.Create"/> /
        /// <see cref="NeoMember.CreateWritable"/> instead — those check
        /// here automatically before constructing.
        /// </summary>
        internal bool TryGetNode(
            string memberId,
            string? overrideValueId,
            NeoValueOwnership ownership,
            [NotNullWhen(true)] out NeoMember? node)
        {
            string key = MakeNodeKey(memberId, overrideValueId, ownership);
            return nodesInternal.TryGetValue(key, out node);
        }

        internal bool TryGetNode(string memberId, string? overrideValueId, [NotNullWhen(true)] out NeoMember? node)
        {
            return TryGetNode(memberId, overrideValueId, NeoValueOwnership.Asset, out node);
        }

        /// <summary>
        /// Adds <paramref name="node"/> to the flat registry under the
        /// composed key (computed from the node's own
        /// <see cref="NeoMember.member"/>.id and
        /// <see cref="NeoMember.overrideValueId"/>). Called by the
        /// <see cref="NeoMember"/> base ctor at the end of
        /// construction; callers shouldn't need to call directly.
        /// Last-write-wins — direct <c>new NeoMemberXyz(…)</c>
        /// construction overrides any previously-cached instance for
        /// the same key.
        /// </summary>
        internal void RegisterNode(NeoMember node)
        {
            string key = MakeNodeKey(
                node.member.RuntimeDeclarationIdentity,
                node.overrideValueId,
                node.ownership);
            nodesInternal[key] = node;
        }

        /// <summary>
        /// Removes <paramref name="node"/> from the registry. Called
        /// by <see cref="NeoMember.Dispose"/>; idempotent — a key
        /// that's already absent (or that points at a different
        /// instance, e.g. a same-key replacement) is left alone.
        /// </summary>
        internal void UnregisterNode(NeoMember node)
        {
            string key = MakeNodeKey(
                node.member.RuntimeDeclarationIdentity,
                node.overrideValueId,
                node.ownership);
            // Only remove if the registered instance is the one we're
            // unregistering — guards against the "I disposed an
            // instance that was already replaced in the registry by a
            // newer ctor call for the same key" race.
            if (nodesInternal.TryGetValue(key, out NeoMember existing) && existing == node)
            {
                nodesInternal.Remove(key);
            }
        }

        internal TGenerated GetOrCreateGeneratedClassValue<TGenerated>(
            NeoMemberClass node,
            System.Func<TGenerated> create)
            where TGenerated : NeoGeneratedClassValue
        {
            string key = MakeNodeKey(
                node.member.RuntimeDeclarationIdentity,
                node.overrideValueId,
                node.ownership);
            if (generatedValuesInternal.TryGetValue(key, out NeoGeneratedClassValue existing))
            {
                if (existing is TGenerated match) return match;
                existing.Dispose();
            }

            TGenerated generated = create();
            generatedValuesInternal[key] = generated;
            return generated;
        }

        internal void RegisterGeneratedClassValue(
            NeoGeneratedClassValue generated,
            NeoMemberClass node)
        {
            string key = MakeNodeKey(
                node.member.RuntimeDeclarationIdentity,
                node.overrideValueId,
                node.ownership);
            if (generatedValuesInternal.TryGetValue(key, out NeoGeneratedClassValue existing)
                && !ReferenceEquals(existing, generated))
            {
                existing.Dispose();
            }
            generatedValuesInternal[key] = generated;
        }

        internal void UnregisterGeneratedClassValue(NeoGeneratedClassValue generated, NeoMemberClass node)
        {
            string key = MakeNodeKey(
                node.member.RuntimeDeclarationIdentity,
                node.overrideValueId,
                node.ownership);
            if (generatedValuesInternal.TryGetValue(key, out NeoGeneratedClassValue existing)
                && ReferenceEquals(existing, generated))
            {
                generatedValuesInternal.Remove(key);
            }
        }

        /// <summary>
        /// Registers the project-specific generated class factories used by
        /// relation-backed runtime features that resolve class defaults or
        /// optional instance overrides without a generated lookup member.
        /// Generated project constructors call this once before constructing
        /// their root wrappers.
        /// </summary>
        public void RegisterGeneratedClassFactories(
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
                readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
                writableFactories)
        {
            if (readOnlyFactories is null)
            {
                throw new System.ArgumentNullException(nameof(readOnlyFactories));
            }
            if (writableFactories is null)
            {
                throw new System.ArgumentNullException(nameof(writableFactories));
            }
            if (readOnlyFactories.Count == 0
                && writableFactories.Count == 0
                && generatedReadOnlyClassFactories is not null)
            {
                return;
            }
            generatedReadOnlyClassFactories = readOnlyFactories;
            generatedWritableClassFactories = writableFactories;
        }

        internal NeoGeneratedClassValue? ResolveRegisteredGeneratedClassValue(
            string valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId)) return null;
            EnsureGeneratedClassFactoriesRegistered();
            return NeoGeneratedTypesSupport.ResolveClassValue(
                    this,
                    valueId,
                    generatedReadOnlyClassFactories!,
                    generatedWritableClassFactories!)
                as NeoGeneratedClassValue;
        }

        internal NeoGeneratedClassValue? ResolveRegisteredGeneratedAsset(
            string assetClassId,
            string? assetValueId)
        {
            if (string.IsNullOrWhiteSpace(assetClassId)) return null;
            EnsureGeneratedClassFactoriesRegistered();
            NeoGeneratedClassValue? generated = string.IsNullOrWhiteSpace(assetValueId)
                ? NeoGeneratedTypesSupport.CreateReadOnlyClassDefault(
                    this,
                    assetClassId,
                    generatedReadOnlyClassFactories!)
                : ResolveRegisteredGeneratedClassValue(assetValueId!);
            if (generated is null) return null;
            return ClassExtendsClass(generated.classId, assetClassId) ? generated : null;
        }

        private void EnsureGeneratedClassFactoriesRegistered()
        {
            if (generatedReadOnlyClassFactories is not null
                && generatedWritableClassFactories is not null)
            {
                return;
            }
            throw new System.InvalidOperationException(
                "Generated class factories are not registered on this NeoClient. "
                    + "Construct the generated project facade before using relation-backed runtime queries.");
        }

        private bool ClassExtendsClass(string? classId, string expectedClassId)
        {
            var visited = new HashSet<string>(System.StringComparer.Ordinal);
            string? cursor = classId;
            while (!string.IsNullOrWhiteSpace(cursor) && visited.Add(cursor!))
            {
                if (string.Equals(cursor, expectedClassId, System.StringComparison.Ordinal))
                {
                    return true;
                }
                cursor = data.classes.TryGetValue(cursor!, out NeoSchemaClass? schemaClass)
                    ? schemaClass.extendsClassId
                    : null;
            }
            return false;
        }

        public void RegisterNativeFunctionInvokers(
            IReadOnlyDictionary<string, NeoNativeFunctionInvoker> invokers)
        {
            nativeFunctionInvokers = invokers;
        }

        public void RegisterDeferredNativeFunctionInvokers(
            IReadOnlyDictionary<string, NeoDeferredNativeFunctionInvoker> invokers)
        {
            deferredNativeFunctionInvokers = invokers;
        }

        internal object? InvokeNativeFunction(
            string memberId,
            object? receiver,
            object?[] args)
        {
            FunctionMember member = PrepareNativeFunctionInvocation(
                memberId,
                args,
                expectedDeferred: false,
                out object?[] preparedArgs);
            ValidateNativeFunctionReceiver(member, receiver);
            if (nativeFunctionInvokers is null)
            {
                throw new NeoScript.NativeFunctionDelegateUnavailableError(
                    "Native Function invocation requires constructing the generated ProjectNeo client wrapper before evaluating NeoScript.");
            }
            if (!nativeFunctionInvokers.TryGetValue(memberId, out var invoker))
            {
                throw new NeoScript.NativeFunctionDelegateUnavailableError(
                    $"No native Function invoker is registered for member '{memberId}'.");
            }
            return NormalizeNativeFunctionReturn(
                member.returnTypeInfo,
                invoker(this, receiver, preparedArgs));
        }

        /// <summary>
        /// Invokes a deferred native Function directly from generated C# and
        /// completes when its handler calls <see cref="NeoDeferredFunction.Complete"/>.
        /// </summary>
        public Task InvokeDeferredNativeFunction(
            string memberId,
            object? receiver,
            object?[] args)
        {
            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            NeoDeferredFunctionBase? deferred = null;
            try
            {
                deferred = StartDeferredNativeFunctionCore(
                    memberId,
                    receiver,
                    args,
                    complete: value =>
                    {
                        RemoveDirectDeferredFunction(deferred);
                        completion.TrySetResult(value);
                    },
                    fail: exception =>
                    {
                        RemoveDirectDeferredFunction(deferred);
                        completion.TrySetException(exception);
                    },
                    invokerReturned: static () => { },
                    dispose: _ =>
                    {
                        RemoveDirectDeferredFunction(deferred);
                        completion.TrySetCanceled();
                    },
                    normalizeReturnValue: false,
                    captureInvokerException: true);
                TrackDirectDeferredFunction(deferred);
            }
            catch (System.Exception exception)
            {
                completion.TrySetException(exception);
            }
            return completion.Task;
        }

        /// <summary>
        /// Invokes a non-void deferred native Function directly from generated
        /// C# and returns its eventual typed result.
        /// </summary>
        public Task<T> InvokeDeferredNativeFunction<T>(
            string memberId,
            object? receiver,
            object?[] args)
        {
            var completion = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            NeoDeferredFunctionBase? deferred = null;
            try
            {
                deferred = StartDeferredNativeFunctionCore(
                    memberId,
                    receiver,
                    args,
                    complete: value =>
                    {
                        RemoveDirectDeferredFunction(deferred);
                        if (value is T typedValue)
                        {
                            completion.TrySetResult(typedValue);
                            return;
                        }
                        if (value is null)
                        {
                            completion.TrySetResult(default!);
                            return;
                        }
                        completion.TrySetException(
                            new NeoDeferredFunctionRuntimeError(
                                $"Deferred Function '{memberId}' completed with {value.GetType().Name}, expected {typeof(T).Name}."));
                    },
                    fail: exception =>
                    {
                        RemoveDirectDeferredFunction(deferred);
                        completion.TrySetException(exception);
                    },
                    invokerReturned: static () => { },
                    dispose: _ =>
                    {
                        RemoveDirectDeferredFunction(deferred);
                        completion.TrySetCanceled();
                    },
                    normalizeReturnValue: false,
                    captureInvokerException: true);
                TrackDirectDeferredFunction(deferred);
            }
            catch (System.Exception exception)
            {
                completion.TrySetException(exception);
            }
            return completion.Task;
        }

        internal NeoDeferredFunctionBase StartDeferredNativeFunction(
            string memberId,
            object? receiver,
            object?[] args,
            System.Action<object?> complete,
            System.Action<System.Exception> fail,
            System.Action invokerReturned,
            System.Action<string>? dispose = null)
        {
            return StartDeferredNativeFunctionCore(
                memberId,
                receiver,
                args,
                complete,
                fail,
                invokerReturned,
                dispose,
                normalizeReturnValue: true,
                captureInvokerException: false);
        }

        private NeoDeferredFunctionBase StartDeferredNativeFunctionCore(
            string memberId,
            object? receiver,
            object?[] args,
            System.Action<object?> complete,
            System.Action<System.Exception> fail,
            System.Action invokerReturned,
            System.Action<string>? dispose,
            bool normalizeReturnValue,
            bool captureInvokerException)
        {
            FunctionMember member = PrepareNativeFunctionInvocation(
                memberId,
                args,
                expectedDeferred: true,
                out object?[] preparedArgs);
            ValidateNativeFunctionReceiver(member, receiver);
            if (deferredNativeFunctionInvokers is null)
            {
                throw new NeoScript.NativeFunctionDelegateUnavailableError(
                    "Deferred native Function invocation requires constructing the generated ProjectNeo client wrapper before evaluating NeoScript.");
            }
            if (!deferredNativeFunctionInvokers.TryGetValue(memberId, out var invoker))
            {
                throw new NeoScript.NativeFunctionDelegateUnavailableError(
                    $"No deferred native Function invoker is registered for member '{memberId}'.");
            }

            System.Action<object?> completeResult = normalizeReturnValue
                ? value => complete(NormalizeNativeFunctionReturn(member.returnTypeInfo, value))
                : complete;
            NeoDeferredFunctionBase deferred = member.returnTypeInfo is VoidTypeInfo
                ? new NeoDeferredFunction(memberId, member.name, completeResult, fail, dispose)
                : CreateTypedDeferredFunction(member, completeResult, fail, dispose);
            try
            {
                invoker(this, receiver, preparedArgs, deferred);
            }
            catch (System.Exception exception)
            {
                if (!captureInvokerException)
                {
                    throw;
                }
                if (deferred.Pending)
                {
                    deferred.Fail(exception);
                }
            }
            finally
            {
                invokerReturned();
            }
            return deferred;
        }

        private static void ValidateNativeFunctionReceiver(
            FunctionMember member,
            object? receiver)
        {
            if (member.isStatic)
            {
                if (receiver is not null)
                {
                    throw new NeoScript.NSGetterRuntimeError(
                        $"Static Function '{member.name}' must be invoked without an instance receiver.");
                }
                return;
            }
            if (receiver is null)
            {
                throw new NeoScript.NSGetterRuntimeError(
                    $"Cannot invoke instance Function '{member.name}' on a null receiver.");
            }
        }

        private FunctionMember PrepareNativeFunctionInvocation(
            string memberId,
            object?[]? args,
            bool expectedDeferred,
            out object?[] preparedArgs)
        {
            if (!TryResolveFunctionMember(memberId, out FunctionMember? signature))
            {
                throw new NeoScript.NSGetterRuntimeError(
                    $"Function '{memberId}' has no effective native signature; its override chain or compiled call IR is stale/corrupt.");
            }

            string functionName = data.members.TryGetValue(
                    memberId, out Member? effectiveMember)
                ? effectiveMember.name
                : signature.name;
            bool actualDeferred = signature.deferred == true;
            if (actualDeferred != expectedDeferred)
            {
                string expected = expectedDeferred ? "deferred" : "immediate";
                string actual = actualDeferred ? "deferred" : "immediate";
                string message =
                    $"Function '{functionName}' ({memberId}) deferred-mode mismatch: " +
                    $"the call path requires {expected}, but its effective signature is {actual}; " +
                    "compiled call IR is stale/corrupt.";
                if (actualDeferred)
                {
                    throw new NeoDeferredFunctionRuntimeError(message);
                }
                throw new NeoScript.NSGetterRuntimeError(message);
            }

            object?[] sourceArgs = args ?? System.Array.Empty<object?>();
            if (sourceArgs.Length != signature.argumentTypes.Length)
            {
                throw new NeoScript.NSGetterRuntimeError(
                    $"Function '{functionName}' ({memberId}) expects " +
                    $"{signature.argumentTypes.Length} arguments but received {sourceArgs.Length}; " +
                    "compiled call IR or caller is stale/corrupt.");
            }

            if (sourceArgs.Length == 0)
            {
                preparedArgs = System.Array.Empty<object?>();
                return signature;
            }

            preparedArgs = new object?[sourceArgs.Length];
            for (int i = 0; i < sourceArgs.Length; i++)
            {
                FunctionArgumentTypeInfo argument = signature.argumentTypes[i];
                string subject =
                    $"argument {i} '{argument.name}' of Function '{functionName}' ({memberId})";
                try
                {
                    NeoScriptValueMarshaller.ValidateRuntimeValue(
                        sourceArgs[i],
                        argument,
                        subject);
                    object? prepared = NormalizeNativeFunctionArgument(
                        sourceArgs[i],
                        argument,
                        subject);
                    NeoScriptValueMarshaller.ValidateRuntimeValue(
                        prepared,
                        argument,
                        subject);
                    preparedArgs[i] = prepared;
                }
                catch (System.Exception exception)
                {
                    throw new NeoScript.NSGetterRuntimeError(
                        $"Function '{functionName}' ({memberId}) argument {i} " +
                        $"'{argument.name}' is incompatible with declared {argument.type}; " +
                        "compiled call IR or caller is stale/corrupt: " +
                        exception.Message);
                }
            }
            return signature;
        }

        private object? NormalizeNativeFunctionArgument(
            object? value,
            FunctionArgumentTypeInfo typeInfo,
            string subject)
        {
            if (value is null) return null;
            switch (typeInfo.type)
            {
                case MemberKind.Int:
                    return System.Convert.ToInt32(value);
                case MemberKind.Float:
                    return System.Convert.ToDouble(value);
                case MemberKind.Decimal:
                    if (value is decimal decimalValue)
                    {
                        return NeoDecimalValues.Format(decimalValue);
                    }
                    if (value is double or float or int or long or short)
                    {
                        return NeoScript.NSGetterEvaluator.CoerceDecimalOperand(
                            value,
                            subject);
                    }
                    return value;
                case MemberKind.Enum:
                {
                    string[] optionIds = NormalizeNativeFunctionEnumArgument(
                        value,
                        subject);
                    if (typeInfo.required && optionIds.Length == 0)
                    {
                        throw new System.InvalidOperationException(
                            $"Required {subject} has no enum option id.");
                    }
                    return optionIds;
                }
                case MemberKind.Sprite:
                    // NeoReadOnlySprite first: since P42 §4.1 a generated
                    // sprite property hands out the wrapper, and it carries
                    // the addressable pair directly — resolving it to a
                    // UnityEngine.Sprite only to reverse-resolve it back would
                    // throw for an unsynchronized asset and lose sliceIndex.
                    if (value is NeoReadOnlySprite spriteWrapper)
                    {
                        return NeoGeneratedTypesSupport.SpriteValue(this, spriteWrapper);
                    }
                    return value is Sprite sprite
                        ? NeoGeneratedTypesSupport.SpriteValue(this, sprite)
                        : value;
                case MemberKind.Audio:
                    return value is AudioClip audio
                        ? NeoGeneratedTypesSupport.AudioValue(this, audio)
                        : value;
                default:
                    return value;
            }
        }

        private static string[] NormalizeNativeFunctionEnumArgument(
            object value,
            string subject)
        {
            if (value is string text) return new[] { text };
            string? optionId = NeoScriptValueMarshaller.EnumOptionId(value);
            if (optionId is not null) return new[] { optionId };
            if (value is not IEnumerable enumerable)
            {
                throw new System.InvalidOperationException(
                    $"{subject} must be an enum option or option-id collection.");
            }
            var result = new List<string>();
            foreach (object? entry in enumerable)
            {
                string? entryId = entry as string
                    ?? NeoScriptValueMarshaller.EnumOptionId(entry);
                if (entryId is null)
                {
                    throw new System.InvalidOperationException(
                        $"{subject} contains an entry without an enum option id.");
                }
                result.Add(entryId);
            }
            return result.ToArray();
        }

        internal void TrackDirectDeferredFunction(NeoDeferredFunctionBase deferred)
        {
            bool disposeImmediately = false;
            lock (activeDirectDeferredFunctionsLock)
            {
                if (isDisposed)
                {
                    disposeImmediately = deferred.Pending;
                }
                else if (deferred.Pending)
                {
                    activeDirectDeferredFunctions.Add(deferred);
                }
            }
            if (disposeImmediately)
            {
                deferred.DisposeFromOwner("NeoClient disposed");
            }
        }

        internal void RemoveDirectDeferredFunction(NeoDeferredFunctionBase? deferred)
        {
            if (deferred is null) return;
            lock (activeDirectDeferredFunctionsLock)
            {
                activeDirectDeferredFunctions.Remove(deferred);
            }
        }

        private static object? NormalizeNativeFunctionReturn(
            Json.TypeInfo returnTypeInfo,
            object? value)
        {
            if (returnTypeInfo is VoidTypeInfo) return null;
            switch (returnTypeInfo.type)
            {
                case MemberKind.Vector2:
                {
                    Vector2? vector = NeoGeneratedTypesSupport.ReadVector2Value(value);
                    return NormalizeVectorResult(
                        returnTypeInfo,
                        value,
                        vector,
                        v => NeoVectorValues.FromVector2(v));
                }
                case MemberKind.Vector2Int:
                {
                    Vector2Int? vector = NeoGeneratedTypesSupport.ReadVector2IntValue(value);
                    return NormalizeVectorResult(
                        returnTypeInfo,
                        value,
                        vector,
                        v => NeoVectorValues.FromVector2Int(v));
                }
                case MemberKind.Vector3:
                {
                    Vector3? vector = NeoGeneratedTypesSupport.ReadVector3Value(value);
                    return NormalizeVectorResult(
                        returnTypeInfo,
                        value,
                        vector,
                        v => NeoVectorValues.FromVector3(v));
                }
                case MemberKind.Vector3Int:
                {
                    Vector3Int? vector = NeoGeneratedTypesSupport.ReadVector3IntValue(value);
                    return NormalizeVectorResult(
                        returnTypeInfo,
                        value,
                        vector,
                        v => NeoVectorValues.FromVector3Int(v));
                }
                case MemberKind.Color:
                {
                    Color? color = NeoGeneratedTypesSupport.ReadColorValue(value);
                    return NormalizeVectorResult(
                        returnTypeInfo,
                        value,
                        color,
                        c => NeoColorValues.FromColor(c));
                }
                case MemberKind.Decimal:
                {
                    // Decimal values travel through the evaluator as canonical
                    // strings (specs/decimal-member.md §6.4); a native
                    // Function returning `decimal` normalizes to that form.
                    if (value is decimal decimalValue)
                    {
                        return NeoDecimalValues.Format(decimalValue);
                    }
                    if (value is string canonical)
                    {
                        return canonical;
                    }
                    if (value is null && !returnTypeInfo.required)
                    {
                        return null;
                    }
                    throw new NeoDeferredFunctionRuntimeError(
                        $"Native Function returned a value that could not be converted to {MemberKind.Decimal}.");
                }
                default:
                    return value;
            }
        }

        private static object? NormalizeVectorResult<TVector>(
            Json.TypeInfo returnTypeInfo,
            object? rawValue,
            TVector? vector,
            System.Func<TVector, object> toRaw)
            where TVector : struct
        {
            if (vector.HasValue) return toRaw(vector.Value);
            if (rawValue is null && !returnTypeInfo.required) return null;
            string typeName = returnTypeInfo.type.ToString();
            throw new NeoDeferredFunctionRuntimeError(
                $"Native Function returned a value that could not be converted to {typeName}.");
        }

        internal bool IsNativeFunctionDeferred(string memberId)
        {
            return TryResolveFunctionMember(memberId, out var member)
                && member.deferred == true;
        }

        private NeoDeferredFunctionBase CreateTypedDeferredFunction(
            FunctionMember member,
            System.Action<object?> complete,
            System.Action<System.Exception> fail,
            System.Action<string>? dispose = null)
        {
            return member.returnTypeInfo.type switch
            {
                MemberKind.Bool => new NeoDeferredFunction<bool>(member.id, member.name, complete, fail, dispose),
                MemberKind.Int => new NeoDeferredFunction<int>(member.id, member.name, complete, fail, dispose),
                MemberKind.Float => new NeoDeferredFunction<float>(member.id, member.name, complete, fail, dispose),
                MemberKind.String => new NeoDeferredFunction<string?>(member.id, member.name, complete, fail, dispose),
                MemberKind.Vector2 => new NeoDeferredFunction<Vector2>(member.id, member.name, complete, fail, dispose),
                MemberKind.Vector2Int => new NeoDeferredFunction<Vector2Int>(member.id, member.name, complete, fail, dispose),
                MemberKind.Vector3 => new NeoDeferredFunction<Vector3>(member.id, member.name, complete, fail, dispose),
                MemberKind.Vector3Int => new NeoDeferredFunction<Vector3Int>(member.id, member.name, complete, fail, dispose),
                MemberKind.Color => new NeoDeferredFunction<Color>(member.id, member.name, complete, fail, dispose),
                MemberKind.Decimal => new NeoDeferredFunction<decimal>(member.id, member.name, complete, fail, dispose),
                _ => new NeoDeferredFunction<object?>(member.id, member.name, complete, fail, dispose),
            };
        }

        internal bool TryResolveFunctionMember(
            string memberId,
            [NotNullWhen(true)] out FunctionMember? member)
        {
            var visited = new HashSet<string>();
            string? currentId = memberId;
            while (!string.IsNullOrEmpty(currentId) && visited.Add(currentId))
            {
                if (!data.members.TryGetValue(currentId!, out Member? current))
                {
                    break;
                }
                if (current is FunctionMember function
                    && function.returnTypeInfo is not null
                    && function.argumentTypes is not null
                    && function.deferred.HasValue)
                {
                    member = function;
                    return true;
                }
                currentId = current.extendsMemberId;
            }
            member = null;
            return false;
        }

        /// <summary>
        /// P43 §6.6.1 — fails closed on a malformed or orphaned constructor
        /// record. The class's ordered <c>constructorIds</c> and the record's
        /// own <c>classId</c> are two representations of one relationship, so
        /// both directions are checked: an id with no live record is an error,
        /// and so is a record whose class disowns it.
        ///
        /// <para>P49 §1.1 adds a second way for a class to own a record — its
        /// <c>requiredConstructorId</c> — so ownership is collected from both
        /// fields before the disowned-record sweep runs.</para>
        /// </summary>
        private void ValidateConstructorRecords()
        {
            // An export written before P43 omits the collection entirely, and a
            // hand-built ProjectData may leave it null. Both mean "declares
            // none", so normalize once instead of null-checking every read.
            data.constructors ??= new Dictionary<string, ConstructorRecord>();
            // P67 §9 — same reasoning: an export written before P67 omits the
            // collection and a hand-built ProjectData may leave it null. Both
            // mean "declares no variants".
            data.variants ??= new Dictionary<string, VariantRecord>();
            data.variantFolders ??= new Dictionary<string, VariantFolderRecord>();
            var claimedByClass = new Dictionary<string, string>();
            foreach (var pair in data.classes)
            {
                NeoSchemaClass schemaClass = pair.Value;
                string[] constructorIds =
                    schemaClass.constructorIds ?? System.Array.Empty<string>();
                ClaimRequiredConstructor(
                    pair.Key,
                    schemaClass,
                    constructorIds,
                    claimedByClass);
                var seen = new HashSet<string>(System.StringComparer.Ordinal);
                var positionalSignatures =
                    new Dictionary<string, string>(System.StringComparer.Ordinal);
                foreach (string constructorId in constructorIds)
                {
                    if (string.IsNullOrEmpty(constructorId))
                    {
                        throw new InvalidOperationException(
                            $"Class '{schemaClass.name}' declares an empty constructor id.");
                    }
                    if (!seen.Add(constructorId))
                    {
                        throw new InvalidOperationException(
                            $"Class '{schemaClass.name}' lists constructor '{constructorId}' more than once.");
                    }
                    if (!TryGetConstructor(constructorId, out ConstructorRecord? record))
                    {
                        throw new InvalidOperationException(
                            $"Class '{schemaClass.name}' lists constructor '{constructorId}', which is missing from the export. Re-export the project from the current web app.");
                    }
                    if (record!.classId != pair.Key)
                    {
                        throw new InvalidOperationException(
                            $"Constructor '{constructorId}' is listed by class '{schemaClass.name}' but names class '{record.classId}' as its owner.");
                    }
                    claimedByClass[constructorId] = pair.Key;

                    foreach (string signature in
                        ConstructorEffectivePositionalSignatures(record))
                    {
                        if (positionalSignatures.TryGetValue(
                                signature,
                                out string? collidingId))
                        {
                            throw new InvalidOperationException(
                                $"Constructors '{collidingId}' and '{constructorId}' on class '{schemaClass.name}' have the same positional signature and would generate two identical C# constructors. Overloads must differ by arity or parameter type, not only by parameter name.");
                        }
                        positionalSignatures[signature] = constructorId;
                    }
                }
            }

            foreach (var pair in data.constructors)
            {
                ConstructorRecord record = pair.Value
                    ?? throw new InvalidOperationException(
                        $"Constructor '{pair.Key}' is null.");
                if (!string.Equals(pair.Key, record.id, System.StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Constructor dictionary key '{pair.Key}' does not match record id '{record.id}'.");
                }
                if (!TryGetClass(record.classId, out NeoSchemaClass? owningClass))
                {
                    throw new InvalidOperationException(
                        $"Constructor '{record.id}' names missing class '{record.classId}'.");
                }
                if (!claimedByClass.TryGetValue(record.id, out string? claimingClassId)
                    || claimingClassId != record.classId)
                {
                    throw new InvalidOperationException(
                        $"Constructor '{record.id}' is not listed in the constructorIds of its class '{owningClass!.name}'; a disowned constructor is unreachable.");
                }
                ValidateConstructorRecord(record, owningClass!);
            }
        }

        /// <summary>
        /// P49 §1.1/§1.3 — records the class's required constructor as owned.
        /// The record is an ordinary constructor record that is deliberately
        /// absent from <c>constructorIds</c>, so without this claim the
        /// disowned-record guard would reject every project containing a class
        /// with a required constructor. Declaring one alongside member
        /// constructors is rejected here rather than tolerated: a required
        /// constructor is the class's only way in, and an overload beside it
        /// reintroduces the ambiguity the form exists to remove.
        /// </summary>
        private void ClaimRequiredConstructor(
            string classId,
            NeoSchemaClass schemaClass,
            string[] constructorIds,
            Dictionary<string, string> claimedByClass)
        {
            string? requiredConstructorId = schemaClass.requiredConstructorId;
            if (requiredConstructorId is null) return;
            if (requiredConstructorId.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Class '{schemaClass.name}' declares an empty required constructor id.");
            }
            if (constructorIds.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Class '{schemaClass.name}' declares a required constructor and {constructorIds.Length} member constructors; a required constructor is the class's only way in.");
            }
            if (!TryGetConstructor(requiredConstructorId, out ConstructorRecord? record))
            {
                throw new InvalidOperationException(
                    $"Class '{schemaClass.name}' names required constructor '{requiredConstructorId}', which is missing from the export. Re-export the project from the current web app.");
            }
            if (record!.classId != classId)
            {
                throw new InvalidOperationException(
                    $"Required constructor '{requiredConstructorId}' is named by class '{schemaClass.name}' but names class '{record.classId}' as its owner.");
            }
            claimedByClass[requiredConstructorId] = classId;
        }

        /// <summary>
        /// <c>record.code</c> is deliberately unchecked: it is authoring
        /// source, and P49 §1.2 made its absence meaningful (a required
        /// constructor that declares no <c>init</c> block stores no code at
        /// all, which is indistinguishable at runtime from a declared but empty
        /// block). The SDK executes <c>record.action</c>, so that — not the
        /// source — is what a truncated export has to be caught by.
        /// </summary>
        private void ValidateConstructorRecord(
            ConstructorRecord record,
            NeoSchemaClass owningClass)
        {
            if (record.action is null)
            {
                throw new InvalidOperationException(
                    $"Constructor '{record.id}' on class '{owningClass.name}' is missing its compiled action. Re-export the project from the current web app.");
            }
            if (record.argumentTypes is null)
            {
                throw new InvalidOperationException(
                    $"Constructor '{record.id}' on class '{owningClass.name}' is missing argumentTypes.");
            }
            var argumentNames = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (FunctionArgumentTypeInfo argument in record.argumentTypes)
            {
                if (string.IsNullOrEmpty(argument.name))
                {
                    throw new InvalidOperationException(
                        $"Constructor '{record.id}' on class '{owningClass.name}' declares an unnamed parameter.");
                }
                if (!argumentNames.Add(argument.name))
                {
                    throw new InvalidOperationException(
                        $"Constructor '{record.id}' on class '{owningClass.name}' declares parameter '{argument.name}' more than once.");
                }
            }

            int expectedParameters = record.argumentTypes.Length + 2;
            if (record.action.parameters is null
                || record.action.parameters.Length != expectedParameters)
            {
                throw new InvalidOperationException(
                    $"Constructor '{record.id}' compiled action has {record.action.parameters?.Length ?? 0} parameters; expected {expectedParameters} (__this__, __root__, and {record.argumentTypes.Length} arguments).");
            }
            if (record.action.parameters[0].id != "__this__"
                || record.action.parameters[1].id != "__root__")
            {
                throw new InvalidOperationException(
                    $"Constructor '{record.id}' compiled action must begin with __this__ and __root__ parameters.");
            }
            for (int i = 0; i < record.argumentTypes.Length; i++)
            {
                string expectedId = $"__arg_{i}__";
                if (record.action.parameters[i + 2].id != expectedId)
                {
                    throw new InvalidOperationException(
                        $"Constructor '{record.id}' compiled argument {i} must use parameter id '{expectedId}'.");
                }
                if (!TypeInfoMatches(
                        record.argumentTypes[i],
                        record.action.parameters[i + 2].typeInfo))
                {
                    throw new InvalidOperationException(
                        $"Constructor '{record.id}' compiled argument {i} type does not match its declared signature.");
                }
            }
            // A constructor body is void — `this` IS the product, so there is
            // nothing to return and the compiled body carries the statement-body
            // Null marker.
            Json.TypeInfo? actionReturnType = record.action.typeInfo;
            if (actionReturnType is null
                || actionReturnType.type != MemberKind.Null
                || !actionReturnType.required)
            {
                throw new InvalidOperationException(
                    $"Constructor '{record.id}' compiled action must declare a required Null return type; a constructor body returns nothing.");
            }

            ConstructorBaseArgument[] baseArguments =
                record.baseArguments ?? System.Array.Empty<ConstructorBaseArgument>();
            // P49 §1.5 — a base clause is a construction expression, so it may
            // carry arguments, an initializer block, or both. Either half alone
            // is a base clause.
            ConstructorBaseInitializerField[] baseInitializerFields =
                record.baseInitializerFields
                ?? System.Array.Empty<ConstructorBaseInitializerField>();
            if (baseArguments.Length == 0
                && record.compiledBaseArguments is not null
                && record.compiledBaseArguments.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Constructor '{record.id}' has compiled base getters but no base clause.");
            }
            if (baseInitializerFields.Length == 0
                && record.compiledBaseInitializerFields is not null
                && record.compiledBaseInitializerFields.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Constructor '{record.id}' has compiled base initializer getters but no base initializer block.");
            }
            if (baseArguments.Length == 0 && baseInitializerFields.Length == 0) return;
            if (string.IsNullOrEmpty(owningClass.extendsClassId))
            {
                throw new InvalidOperationException(
                    $"Constructor '{record.id}' has a base clause but class '{owningClass.name}' extends nothing.");
            }
            if (baseArguments.Length != 0)
            {
                if (record.compiledBaseArguments is null
                    || record.compiledBaseArguments.Length != baseArguments.Length)
                {
                    throw new InvalidOperationException(
                        $"Constructor '{record.id}' has {baseArguments.Length} base arguments but {record.compiledBaseArguments?.Length ?? 0} compiled base getters. Re-export the project from the current web app.");
                }
                AssertBaseClauseNamesAreUnique(
                    record.id,
                    System.Array.ConvertAll(baseArguments, argument => argument.name),
                    "base argument");
            }
            if (baseInitializerFields.Length != 0)
            {
                if (record.compiledBaseInitializerFields is null
                    || record.compiledBaseInitializerFields.Length
                        != baseInitializerFields.Length)
                {
                    throw new InvalidOperationException(
                        $"Constructor '{record.id}' has {baseInitializerFields.Length} base initializer fields but {record.compiledBaseInitializerFields?.Length ?? 0} compiled base initializer getters. Re-export the project from the current web app.");
                }
                AssertBaseClauseNamesAreUnique(
                    record.id,
                    System.Array.ConvertAll(baseInitializerFields, field => field.name),
                    "base initializer field");
            }
        }

        /// <summary>
        /// The two halves of a base clause are checked separately because they
        /// name different things — an argument names a base parameter, a field
        /// names a base member — so a name may legitimately appear once in each.
        /// </summary>
        private static void AssertBaseClauseNamesAreUnique(
            string constructorId,
            IReadOnlyList<string> names,
            string subject)
        {
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name))
                {
                    throw new InvalidOperationException(
                        $"Constructor '{constructorId}' has an unnamed {subject}.");
                }
                if (!seen.Add(name))
                {
                    throw new InvalidOperationException(
                        $"Constructor '{constructorId}' binds {subject} '{name}' more than once.");
                }
            }
        }

        /// <summary>
        /// P43 §6.1.1 — the signatures C# would see. <c>required</c> is
        /// deliberately excluded: <c>T</c> and <c>T?</c> are the same positional
        /// type for a reference type, so two overloads differing only there
        /// would generate two identical C# constructors.
        ///
        /// <para>P65 §2.2 re-judges the rule against every effective arity a
        /// defaulted signature exposes: <c>Foo(int a, int b = 2)</c> occupies
        /// both <c>(int)</c> and <c>(int, int)</c> positionally, so each
        /// prefix from the non-defaulted count up to the full arity is one
        /// signature here, exactly as the web-side declaration-time check
        /// keys them.</para>
        /// </summary>
        private static IReadOnlyList<string> ConstructorEffectivePositionalSignatures(
            ConstructorRecord record)
        {
            FunctionArgumentTypeInfo[] argumentTypes =
                record.argumentTypes ?? System.Array.Empty<FunctionArgumentTypeInfo>();
            var parts = new List<string>(argumentTypes.Length);
            foreach (FunctionArgumentTypeInfo argument in argumentTypes)
            {
                parts.Add(ConstructorPositionalTypeKey(argument));
            }
            int minimumArity = NeoParameterDefaults.NonDefaultedCount(argumentTypes);
            var signatures = new List<string>(argumentTypes.Length - minimumArity + 1);
            for (int arity = minimumArity; arity <= argumentTypes.Length; arity++)
            {
                signatures.Add(string.Join(",", parts.GetRange(0, arity)));
            }
            return signatures;
        }

        /// <summary>
        /// One parameter's positional identity, recursing exactly as the
        /// web-side authority <c>neoScriptPositionalTypeKey</c>
        /// (packages/neoscript-language/src/declared-constructors.ts) does.
        ///
        /// <para>Recursion is the whole point: the element type of a
        /// collection, the key enum of an enum-keyed dictionary, and a closed
        /// generic's type arguments all reach the generated C# signature, so
        /// <c>List&lt;int&gt;</c> and <c>List&lt;string&gt;</c> must be two
        /// keys here for the same reason they are two keys on the push side.
        /// Flattening them to the outer <c>MemberKind</c> alone rejects
        /// overload sets the server already accepted, and the rejection lands
        /// at project load rather than at push.</para>
        /// </summary>
        private static string ConstructorPositionalTypeKey(Json.TypeInfo typeInfo)
        {
            string discriminator = string.Empty;
            string keyDiscriminator = string.Empty;
            Json.TypeInfo? entryTypeInfo = null;
            Dictionary<string, Json.TypeInfo>? typeArguments = null;
            Json.TypeInfo? delegateReturnType = null;
            IReadOnlyList<Json.TypeInfo>? delegateArgumentTypes = null;
            switch (typeInfo)
            {
                // The argument carrier is flat: it holds every discriminator
                // the nested subclasses split across their own fields.
                case FunctionArgumentTypeInfo argument:
                    discriminator = argument.classId
                        ?? argument.interfaceId
                        ?? argument.enumId
                        ?? argument.genericParamId
                        ?? argument.collectionMemberId
                        ?? string.Empty;
                    keyDiscriminator = argument.keyEnumId ?? string.Empty;
                    entryTypeInfo = argument.entryTypeInfo;
                    typeArguments = argument.typeArguments;
                    delegateReturnType = argument.returnTypeInfo;
                    delegateArgumentTypes = argument.argumentTypes;
                    break;
                case ClassTypeInfo classTypeInfo:
                    discriminator = classTypeInfo.classId;
                    typeArguments = classTypeInfo.typeArguments;
                    break;
                case InterfaceTypeInfo interfaceTypeInfo:
                    discriminator = interfaceTypeInfo.interfaceId;
                    break;
                case EnumTypeInfo enumTypeInfo:
                    discriminator = enumTypeInfo.enumId;
                    break;
                case GenericTypeInfo genericTypeInfo:
                    discriminator = genericTypeInfo.genericParamId;
                    break;
                case CollectionTypeInfo collectionTypeInfo:
                    keyDiscriminator = collectionTypeInfo.keyEnumId ?? string.Empty;
                    entryTypeInfo = collectionTypeInfo.entryTypeInfo;
                    break;
                case LookupTypeInfo lookupTypeInfo:
                    discriminator = lookupTypeInfo.collectionMemberId;
                    entryTypeInfo = lookupTypeInfo.entryTypeInfo;
                    break;
                case DelegateTypeInfo delegateTypeInfo:
                    delegateReturnType = delegateTypeInfo.returnTypeInfo;
                    delegateArgumentTypes = delegateTypeInfo.argumentTypes;
                    break;
                // An action contributes its parameter list and no return
                // type; the leading MemberKind ordinal already separates it
                // from a delegate with the same parameters.
                case ActionTypeInfo actionTypeInfo:
                    delegateArgumentTypes = actionTypeInfo.argumentTypes;
                    break;
            }

            var key = new System.Text.StringBuilder();
            key.Append((int)typeInfo.type)
                .Append('|')
                .Append(discriminator)
                .Append('|')
                .Append(keyDiscriminator);
            if (entryTypeInfo is not null)
            {
                key.Append('<')
                    .Append(ConstructorPositionalTypeKey(entryTypeInfo))
                    .Append('>');
            }
            if (typeArguments is not null && typeArguments.Count != 0)
            {
                // Ordered so two identical closed generics key identically
                // regardless of the JSON's property order.
                var names = new List<string>(typeArguments.Keys);
                names.Sort(System.StringComparer.Ordinal);
                key.Append('{');
                for (int i = 0; i < names.Count; i++)
                {
                    if (i != 0) key.Append(',');
                    key.Append(names[i])
                        .Append('=')
                        .Append(ConstructorPositionalTypeKey(typeArguments[names[i]]));
                }
                key.Append('}');
            }
            if (delegateReturnType is not null || delegateArgumentTypes is not null)
            {
                key.Append("->");
                if (delegateReturnType is not null)
                {
                    key.Append(ConstructorPositionalTypeKey(delegateReturnType));
                }
                key.Append('(');
                if (delegateArgumentTypes is not null)
                {
                    for (int i = 0; i < delegateArgumentTypes.Count; i++)
                    {
                        if (i != 0) key.Append(',');
                        key.Append(ConstructorPositionalTypeKey(
                            delegateArgumentTypes[i]));
                    }
                }
                key.Append(')');
            }
            return key.ToString();
        }

        private void ValidateCallableMembers()
        {
            foreach (var pair in data.members)
            {
                if (pair.Value is DelegateMember delegateMember)
                {
                    ValidateDelegateMember(delegateMember);
                    continue;
                }
                if (pair.Value is ActionMember actionMember)
                {
                    ValidateActionMember(actionMember);
                    continue;
                }
                if (pair.Value is FunctionMember function)
                {
                    ValidateCallableSignature(
                        function,
                        function.returnTypeInfo,
                        function.argumentTypes,
                        function.deferred,
                        "Function",
                        rejectOverrideFields: false);
                    continue;
                }
                if (pair.Value is not NSFunctionMember nsFunction) continue;
                ValidateCallableSignature(
                    nsFunction,
                    nsFunction.returnTypeInfo,
                    nsFunction.argumentTypes,
                    nsFunction.deferred,
                    "NSFunction",
                    rejectOverrideFields: true);
                ValidateNSFunctionMember(nsFunction);
            }
        }

        private static void ValidateDelegateMember(DelegateMember member)
        {
            if (member.returnTypeInfo is null)
            {
                throw new System.InvalidOperationException(
                    $"NSDelegate member '{member.id}' is missing returnTypeInfo.");
            }
            if (member.argumentTypes is null)
            {
                throw new System.InvalidOperationException(
                    $"NSDelegate member '{member.id}' is missing argumentTypes.");
            }
            if (member.argumentTypes.Length > 16)
            {
                throw new System.InvalidOperationException(
                    $"NSDelegate member '{member.id}' exceeds the 16-argument arity cap.");
            }
            FunctionWithReturnType? action = member.defaultValue?.value?.action;
            if (action is null) return;
            int expectedParameters = member.argumentTypes.Length + 2;
            if (action.parameters is null
                || action.parameters.Length != expectedParameters)
            {
                throw new System.InvalidOperationException(
                    $"NSDelegate member '{member.id}' closure has {action.parameters?.Length ?? 0} parameters; expected {expectedParameters} (__this__, __root__, and {member.argumentTypes.Length} arguments).");
            }
            if (action.parameters[0].id != "__this__"
                || action.parameters[1].id != "__root__")
            {
                throw new System.InvalidOperationException(
                    $"NSDelegate member '{member.id}' closure must begin with __this__ and __root__ parameters.");
            }
            var parameterIds = new HashSet<string>(System.StringComparer.Ordinal)
            {
                "__this__",
                "__root__",
            };
            // Delegate invocation is position-aligned. Revision-12 lambdas
            // deliberately use closure-unique ids so nested frames cannot
            // collide; load validation therefore checks uniqueness and type,
            // not the top-level NSFunction __arg_N__ spelling.
            for (int i = 0; i < member.argumentTypes.Length; i++)
            {
                Variable parameter = action.parameters[i + 2];
                if (string.IsNullOrEmpty(parameter.id))
                {
                    throw new System.InvalidOperationException(
                        $"NSDelegate member '{member.id}' closure parameter {i} has no id.");
                }
                if (!parameterIds.Add(parameter.id))
                {
                    throw new System.InvalidOperationException(
                        $"NSDelegate member '{member.id}' closure parameter {i} repeats id '{parameter.id}'.");
                }
                if (!TypeInfoMatches(
                        member.argumentTypes[i],
                        parameter.typeInfo))
                {
                    throw new System.InvalidOperationException(
                        $"NSDelegate member '{member.id}' closure parameter {i} type does not match its declared signature.");
                }
            }
            if (!TypeInfoMatches(member.returnTypeInfo, action.typeInfo))
            {
                throw new System.InvalidOperationException(
                    $"NSDelegate member '{member.id}' closure return type does not match its declared signature.");
            }
        }

        /// <summary>
        /// P62 §5.3 load validation. Mirrors
        /// <see cref="ValidateDelegateMember"/> minus every closure check —
        /// an action holds member targets only, so there is no closure
        /// envelope to validate — and minus the return slot, because void is
        /// structural. It adds the two rules the listener <i>set</i> brings:
        /// each entry is a member target, and no two entries share a
        /// <c>(memberId, valueId)</c> identity.
        /// </summary>
        private static void ValidateActionMember(ActionMember member)
        {
            if (member.argumentTypes is null)
            {
                throw new System.InvalidOperationException(
                    $"NSAction member '{member.id}' is missing argumentTypes.");
            }
            if (member.argumentTypes.Length > 16)
            {
                throw new System.InvalidOperationException(
                    $"NSAction member '{member.id}' exceeds the 16-argument arity cap.");
            }
            if (member.required)
            {
                throw new System.InvalidOperationException(
                    $"NSAction member '{member.id}' declares required; an action is never nullable, and its rest state is an empty listener set.");
            }
            NeoActionValue? value = member.defaultValue?.value;
            if (value is null) return;
            var identities = new HashSet<string>(System.StringComparer.Ordinal);
            for (int index = 0; index < value.listeners.Count; index++)
            {
                NeoDelegateValue listener = value.listeners[index];
                if (listener is null || !listener.IsMemberTarget)
                {
                    throw new System.InvalidOperationException(
                        $"NSAction member '{member.id}' listener {index} is not a member target; only member references are listeners.");
                }
                if (listener.IsClosure)
                {
                    throw new System.InvalidOperationException(
                        $"NSAction member '{member.id}' listener {index} carries a closure; a listener must be a member reference so it can be deduplicated and removed by identity.");
                }
                if (!identities.Add(NeoActionValue.ListenerIdentity(listener)))
                {
                    throw new System.InvalidOperationException(
                        $"NSAction member '{member.id}' listener {index} duplicates an earlier listener identity; a listener set holds each (memberId, valueId) once.");
                }
            }
        }

        private static void ValidateCallableSignature(
            Member member,
            Json.TypeInfo? returnTypeInfo,
            FunctionArgumentTypeInfo[]? argumentTypes,
            bool? deferred,
            string kind,
            bool rejectOverrideFields)
        {
            bool isOverride = !string.IsNullOrEmpty(member.extendsMemberId);
            if (isOverride)
            {
                if (rejectOverrideFields
                    && (returnTypeInfo is not null
                    || argumentTypes is not null
                    || deferred.HasValue))
                {
                    throw new System.InvalidOperationException(
                        $"{kind} override '{member.id}' must inherit returnTypeInfo, argumentTypes, and deferred from its declaration.");
                }
                return;
            }
            if (returnTypeInfo is null)
            {
                throw new System.InvalidOperationException(
                    $"{kind} member '{member.id}' is missing returnTypeInfo.");
            }
            if (argumentTypes is null)
            {
                throw new System.InvalidOperationException(
                    $"{kind} member '{member.id}' is missing argumentTypes.");
            }
            if (!deferred.HasValue)
            {
                throw new System.InvalidOperationException(
                    $"{kind} member '{member.id}' is missing deferred.");
            }
        }

        private void ValidateNSFunctionMember(NSFunctionMember member)
        {
            if (member.bodyMode is not null && member.bodyMode != "ui")
            {
                throw new System.InvalidOperationException(
                    $"NSFunction member '{member.id}' has unsupported bodyMode '{member.bodyMode}'.");
            }
            if (member.bodyMode == "ui" && member.uiAction is null)
            {
                throw new System.InvalidOperationException(
                    $"UI-mode NSFunction member '{member.id}' is missing uiAction.");
            }
            if (member.bodyMode is null && member.uiAction is not null)
            {
                throw new System.InvalidOperationException(
                    $"Custom-code NSFunction member '{member.id}' cannot declare uiAction.");
            }
            if (member.required
                || member.defaultValue is not null
                || !string.IsNullOrEmpty(member.valueId)
                || !string.IsNullOrEmpty(member.storage))
            {
                throw new System.InvalidOperationException(
                    $"NSFunction member '{member.id}' is value-less and cannot declare required/default/value/storage fields.");
            }
            bool hasLocalCodeField = member.code is not null;
            bool hasLocalAction = member.action is not null;
            if (member.bodyMode == "ui" && !hasLocalAction)
            {
                throw new System.InvalidOperationException(
                    $"UI-mode NSFunction member '{member.id}' is missing its compiled action.");
            }
            if (member.bodyMode is null && hasLocalCodeField != hasLocalAction)
            {
                throw new System.InvalidOperationException(
                    $"NSFunction member '{member.id}' must export its local code and compiled action together.");
            }
            if (hasLocalCodeField && string.IsNullOrWhiteSpace(member.code))
            {
                throw new System.InvalidOperationException(
                    $"NSFunction member '{member.id}' local code must not be empty.");
            }
            if (member.isAbstract == true) return;

            if (string.IsNullOrEmpty(member.extendsMemberId)
                && !hasLocalAction)
            {
                throw new System.InvalidOperationException(
                    $"Concrete NSFunction declaration '{member.id}' is missing its compiled action.");
            }

            NSFunctionMember? signature = ResolveNSFunctionSignature(member.id);
            FunctionWithReturnType? action = ResolveNSFunctionAction(member.id);
            if (signature is null || action is null)
            {
                throw new System.InvalidOperationException(
                    $"Concrete NSFunction member '{member.id}' is missing its compiled action or inherited signature.");
            }
            int expectedParameters = signature.argumentTypes.Length + 2;
            if (action.parameters is null || action.parameters.Length != expectedParameters)
            {
                throw new System.InvalidOperationException(
                    $"NSFunction member '{member.id}' compiled action has {action.parameters?.Length ?? 0} parameters; expected {expectedParameters} (__this__, __root__, and {signature.argumentTypes.Length} arguments).");
            }
            if (action.parameters[0].id != "__this__"
                || action.parameters[1].id != "__root__")
            {
                throw new System.InvalidOperationException(
                    $"NSFunction member '{member.id}' compiled action must begin with __this__ and __root__ parameters.");
            }
            for (int i = 0; i < signature.argumentTypes.Length; i++)
            {
                string expectedId = $"__arg_{i}__";
                if (action.parameters[i + 2].id != expectedId)
                {
                    throw new System.InvalidOperationException(
                        $"NSFunction member '{member.id}' compiled argument {i} must use parameter id '{expectedId}'.");
                }
                if (!TypeInfoMatches(
                        signature.argumentTypes[i],
                        action.parameters[i + 2].typeInfo))
                {
                    throw new System.InvalidOperationException(
                        $"NSFunction member '{member.id}' compiled argument {i} type does not match its declared signature.");
                }
            }
            bool validReturn = signature.returnTypeInfo is VoidTypeInfo
                ? action.typeInfo?.type == MemberKind.Null
                    && action.typeInfo.required
                : TypeInfoMatches(signature.returnTypeInfo, action.typeInfo);
            if (!validReturn)
            {
                throw new System.InvalidOperationException(
                    $"NSFunction member '{member.id}' compiled action return type does not match its declared return type.");
            }
        }

        private NSFunctionMember? ResolveNSFunctionSignature(string memberId)
        {
            return NeoSchemaClassInheritance.WalkExtendsMemberChain(
                memberId,
                id => data.members.TryGetValue(id, out Member? value) ? value : null,
                current => current is NSFunctionMember function
                    && function.returnTypeInfo is not null
                    && function.argumentTypes is not null
                    && function.deferred.HasValue
                        ? function
                        : null,
                requireKind: MemberKind.NSFunction);
        }

        private FunctionWithReturnType? ResolveNSFunctionAction(string memberId)
        {
            return NeoSchemaClassInheritance.WalkExtendsMemberChain(
                memberId,
                id => data.members.TryGetValue(id, out Member? value) ? value : null,
                current => current is NSFunctionMember function
                    ? function.action
                    : null,
                requireKind: MemberKind.NSFunction);
        }

        private static bool TypeInfoMatches(Json.TypeInfo? left, Json.TypeInfo? right)
        {
            if (left is null || right is null) return left is null && right is null;
            if (left.type != right.type || left.required != right.required) return false;
            return (left, right) switch
            {
                (FunctionArgumentTypeInfo a, FunctionArgumentTypeInfo b) =>
                    a.classId == b.classId
                    && a.interfaceId == b.interfaceId
                    && a.enumId == b.enumId
                    && a.ownerClassId == b.ownerClassId
                    && a.genericParamId == b.genericParamId
                    && a.collectionMemberId == b.collectionMemberId
                    && a.collectionValueId == b.collectionValueId
                    && TypeInfoMatches(a.entryTypeInfo, b.entryTypeInfo)
                    && TypeArgumentsMatch(a.typeArguments, b.typeArguments)
                    && TypeInfoMatches(a.returnTypeInfo, b.returnTypeInfo)
                    && TypeInfoListsMatch(a.argumentTypes, b.argumentTypes),
                (ClassTypeInfo a, ClassTypeInfo b) =>
                    a.classId == b.classId
                    && TypeArgumentsMatch(a.typeArguments, b.typeArguments),
                (FunctionArgumentTypeInfo a, ClassTypeInfo b) =>
                    a.classId == b.classId
                    && TypeArgumentsMatch(a.typeArguments, b.typeArguments),
                (ClassTypeInfo a, FunctionArgumentTypeInfo b) =>
                    a.classId == b.classId
                    && TypeArgumentsMatch(a.typeArguments, b.typeArguments),
                (InterfaceTypeInfo a, InterfaceTypeInfo b) => a.interfaceId == b.interfaceId,
                (FunctionArgumentTypeInfo a, InterfaceTypeInfo b) =>
                    a.interfaceId == b.interfaceId,
                (InterfaceTypeInfo a, FunctionArgumentTypeInfo b) =>
                    a.interfaceId == b.interfaceId,
                (EnumTypeInfo a, EnumTypeInfo b) => a.enumId == b.enumId,
                (FunctionArgumentTypeInfo a, EnumTypeInfo b) =>
                    a.enumId == b.enumId,
                (EnumTypeInfo a, FunctionArgumentTypeInfo b) =>
                    a.enumId == b.enumId,
                (GenericTypeInfo a, GenericTypeInfo b) =>
                    a.ownerClassId == b.ownerClassId
                    && a.genericParamId == b.genericParamId,
                (FunctionArgumentTypeInfo a, GenericTypeInfo b) =>
                    a.ownerClassId == b.ownerClassId
                    && a.genericParamId == b.genericParamId,
                (GenericTypeInfo a, FunctionArgumentTypeInfo b) =>
                    a.ownerClassId == b.ownerClassId
                    && a.genericParamId == b.genericParamId,
                (CollectionTypeInfo a, CollectionTypeInfo b) =>
                    TypeInfoMatches(a.entryTypeInfo, b.entryTypeInfo),
                (FunctionArgumentTypeInfo a, CollectionTypeInfo b) =>
                    TypeInfoMatches(a.entryTypeInfo, b.entryTypeInfo),
                (CollectionTypeInfo a, FunctionArgumentTypeInfo b) =>
                    TypeInfoMatches(a.entryTypeInfo, b.entryTypeInfo),
                (LookupTypeInfo a, LookupTypeInfo b) =>
                    a.collectionMemberId == b.collectionMemberId
                    && a.collectionValueId == b.collectionValueId
                    && TypeInfoMatches(a.entryTypeInfo, b.entryTypeInfo),
                (FunctionArgumentTypeInfo a, LookupTypeInfo b) =>
                    a.collectionMemberId == b.collectionMemberId
                    && a.collectionValueId == b.collectionValueId
                    && TypeInfoMatches(a.entryTypeInfo, b.entryTypeInfo),
                (LookupTypeInfo a, FunctionArgumentTypeInfo b) =>
                    a.collectionMemberId == b.collectionMemberId
                    && a.collectionValueId == b.collectionValueId
                    && TypeInfoMatches(a.entryTypeInfo, b.entryTypeInfo),
                (DelegateTypeInfo a, DelegateTypeInfo b) =>
                    TypeInfoMatches(a.returnTypeInfo, b.returnTypeInfo)
                    && TypeInfoListsMatch(a.argumentTypes, b.argumentTypes),
                (FunctionArgumentTypeInfo a, DelegateTypeInfo b)
                    when a.type == MemberKind.NSDelegate =>
                    TypeInfoMatches(a.returnTypeInfo, b.returnTypeInfo)
                    && TypeInfoListsMatch(a.argumentTypes, b.argumentTypes),
                (DelegateTypeInfo a, FunctionArgumentTypeInfo b)
                    when b.type == MemberKind.NSDelegate =>
                    TypeInfoMatches(a.returnTypeInfo, b.returnTypeInfo)
                    && TypeInfoListsMatch(a.argumentTypes, b.argumentTypes),
                // Actions compare on parameters alone — there is no return
                // slot, because void is structural for them (P62 §2).
                (ActionTypeInfo a, ActionTypeInfo b) =>
                    TypeInfoListsMatch(a.argumentTypes, b.argumentTypes),
                (FunctionArgumentTypeInfo a, ActionTypeInfo b)
                    when a.type == MemberKind.NSAction =>
                    TypeInfoListsMatch(a.argumentTypes, b.argumentTypes),
                (ActionTypeInfo a, FunctionArgumentTypeInfo b)
                    when b.type == MemberKind.NSAction =>
                    TypeInfoListsMatch(a.argumentTypes, b.argumentTypes),
                _ => true,
            };
        }

        private static bool TypeInfoListsMatch(
            IReadOnlyList<Json.TypeInfo>? left,
            IReadOnlyList<Json.TypeInfo>? right)
        {
            if (left is null || right is null) return left is null && right is null;
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (!TypeInfoMatches(left[i], right[i])) return false;
            }
            return true;
        }

        private static bool TypeArgumentsMatch(
            Dictionary<string, Json.TypeInfo>? left,
            Dictionary<string, Json.TypeInfo>? right)
        {
            if (left is null || right is null) return left is null && right is null;
            if (left.Count != right.Count) return false;
            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out Json.TypeInfo? other)
                    || !TypeInfoMatches(pair.Value, other))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Composes the registry key from a member id and an
        /// optional override-value id. Format mirrors the user-facing
        /// spec:
        ///   - <c>memberId</c> when no override
        ///   - <c>$"{memberId}_{overrideValueId}"</c> when an override is set
        /// </summary>
        internal static string MakeNodeKey(
            string memberId,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
        {
            string prefix = ownership switch
            {
                NeoValueOwnership.Asset => "asset",
                NeoValueOwnership.Save => "save",
                NeoValueOwnership.Session => "session",
                _ => "unknown",
            };
            return string.IsNullOrEmpty(overrideValueId)
                ? $"{prefix}:{memberId}"
                : $"{prefix}:{memberId}_{overrideValueId}";
        }

        internal bool TryGetEnum<TEnum>(string id, [NotNullWhen(true)] out TEnum? enumInfo) where TEnum : Enum
        {
            if (data.enums.TryGetValue(id, out Enum idMatch))
            {
                if (idMatch is TEnum match)
                {
                    enumInfo = match;
                    return true;
                }
            }
            enumInfo = null;
            return false;
        }

        protected ProjectSaveData BuildDefaultSaveData()
        {
            ProjectSaveData empty = new()
            {
                name = BuildNewSaveName(),
                projectId = data.project.id,
                version = BuildSaveVersionData(),
                createdAt = NeoTimestamp.Now(),
                // leave `values` empty until value(s) are written at runtime (sparse overlay)
                values = new(),
            };
            return empty;
        }

        protected ProjectSaveData BuildDefaultSessionData()
        {
            return new()
            {
                name = "",
                projectId = data.project.id,
                version = BuildSaveVersionData(),
                createdAt = NeoTimestamp.Now(),
                values = new(),
            };
        }

        protected string BuildNewSaveName()
        {
            string? configuredName = SaveOptions.BuildSaveName?.Invoke();
            return string.IsNullOrWhiteSpace(configuredName) ? BuildDefaultSaveName() : configuredName!;
        }

        private static string BuildDefaultSaveName()
        {
            lock (saveNameRandomLock)
            {
                string adjective = saveNameAdjectives[saveNameRandom.Next(saveNameAdjectives.Length)];
                string noun = saveNameNouns[saveNameRandom.Next(saveNameNouns.Length)];
                int suffix = saveNameRandom.Next(100, 1000);
                return $"{adjective}-{noun}-{suffix}";
            }
        }

        protected VersionData BuildSaveVersionData()
        {
            string id = data.metadata?.versionId ?? "";
            string label = data.metadata?.semver?.label ?? "";
            if (string.IsNullOrWhiteSpace(label)) label = id;
            return new VersionData
            {
                id = id,
                label = label,
            };
        }

        // Sparse overlay: Save/Session stores start empty. The root and
        // every authored default resolve by their stable id through
        // <see cref="TryGetOverlaidValue"/> (save/session value ?? authored),
        // so there is nothing to seed up-front — a value only enters a
        // writable store when it is first written (clone-on-write at its
        // stable id). Eager full-graph cloning is intentionally gone.
        private void InitializeSessionDefaults() { }

        private void InitializeSaveDefaults() { }

        private void ValidateRootClassMember(string memberId, string projectFieldName)
        {
            if (string.IsNullOrEmpty(memberId))
            {
                throw new System.InvalidOperationException(
                    $"Project field '{projectFieldName}' is required.");
            }
            if (!data.members.TryGetValue(memberId, out Member? member))
            {
                throw new System.InvalidOperationException(
                    $"Project field '{projectFieldName}' references missing member '{memberId}'.");
            }
            if (member is not ClassMember)
            {
                throw new System.InvalidOperationException(
                    $"Project field '{projectFieldName}' must reference a Class member, but '{memberId}' is {member.GetType().Name}.");
            }
        }

        protected ProjectSaveData? DeserializeSaveData(string json)
        {
            return JsonConvert.DeserializeObject<ProjectSaveData>(json);
        }

        public string SerializeSaveData()
        {
            return JsonConvert.SerializeObject(saveData);
        }

        private bool SaveHasSemanticChanges()
        {
            if (committedSaveSemanticState is null) return true;
            var current = JObject.Parse(SerializeSaveData());
            return !JToken.DeepEquals(
                committedSaveSemanticState,
                NeoSemanticJson.SaveEnvelope(current));
        }

        private void CaptureCommittedSaveState(string? content = null)
        {
            committedSaveState = JObject.Parse(content ?? SerializeSaveData());
            committedSaveSemanticState = NeoSemanticJson.SaveEnvelope(committedSaveState);
        }

        /// <summary>
        /// Same-value setters may have stamped their in-memory row before the
        /// commit boundary proved the batch semantic no-op. Put those
        /// server-managed timestamps back so a suppressed commit is also
        /// observationally timestamp-neutral to the running game.
        /// </summary>
        private void RestoreCommittedSaveMetadata()
        {
            if (committedSaveState is null) return;
            saveData.projectId = committedSaveState["projectId"]?.Value<string>()
                ?? saveData.projectId;
            saveData.createdAt = ReadTimestamp(
                committedSaveState["createdAt"],
                saveData.createdAt);
            saveData.updatedAt = ReadTimestamp(
                committedSaveState["updatedAt"],
                saveData.updatedAt);

            if (committedSaveState["values"] is not JObject baselineValues) return;
            foreach (var pair in saveData.values)
            {
                if (!baselineValues.TryGetValue(pair.Key, out var baselineRow)) continue;
                var currentRow = JToken.FromObject(pair.Value);
                if (!NeoSemanticJson.ProjectRecordsEqual(currentRow, baselineRow)) continue;
                pair.Value.createdAt = ReadTimestamp(
                    baselineRow["createdAt"],
                    pair.Value.createdAt);
                pair.Value.updatedAt = ReadTimestamp(
                    baselineRow["updatedAt"],
                    pair.Value.updatedAt);
            }
        }

        private static NeoTimestamp ReadTimestamp(
            JToken? value,
            NeoTimestamp fallback)
        {
            return value?.Type is JTokenType.Integer or JTokenType.Float
                ? new NeoTimestamp(value.Value<double>())
                : fallback;
        }

        /// <summary>
        /// Applies an externally-merged save content blob into the running
        /// value graph <b>in place</b> — the inbound side of live save sessions
        /// (<c>specs/live-save-sessions.md</c>): a co-editor (e.g. the web
        /// tool) patched the session's live snapshot and the synchronizer
        /// delivered the merged content via
        /// <see cref="INeoLiveContentSource.OnLiveContentChanged"/> (the client
        /// subscribes itself on construction). Each changed overlay row is
        /// re-shadowed at its stable id, raising the same typed change events
        /// a local write raises (so generated subscriptions like
        /// <c>Save.OnChanged(...)</c> fire, with
        /// <see cref="NeoChangeSource.External"/>), and rows the incoming
        /// content no longer carries fall back to the authored defaults.
        /// </summary>
        internal void ApplyExternalSaveContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new System.ArgumentException(
                    "External save content cannot be empty.", nameof(content));
            }

            if (!LocalGameSaveLoader.TryLoad(content, out var incoming))
            {
                throw new System.InvalidOperationException(
                    "External save content could not be parsed as a save file.");
            }

            if (!incoming.TryDeserializeValues(out var rows))
            {
                Debug.LogWarning(
                    "[NeoCompose] External save content's values cannot be read against the " +
                    "current schema; skipping the live apply (the cloud copy is untouched).");
                return;
            }

            // An inbound apply is already server state: re-shadowing it must
            // raise the typed change events but never loop back into the live
            // auto-commit (the synchronizer's baseline already covers it).
            suppressLiveAutoCommit = true;
            CurrentChangeSource = NeoChangeSource.External;
            try
            {
                // Shadows the incoming content no longer carries fall back to the
                // authored defaults (the web's "Reset to default").
                var removedIds = new List<string>();
                foreach (var id in saveValues.Keys)
                {
                    if (!rows.ContainsKey(id)) removedIds.Add(id);
                }

                foreach (var id in removedIds)
                {
                    RemoveWritableShadow(NeoValueOwnership.Save, id);
                }

                foreach (var row in rows.Values)
                {
                    if (saveValues.TryGetValue(row.id, out var existing)
                        && JsonConvert.SerializeObject(existing) == JsonConvert.SerializeObject(row))
                    {
                        continue; // unchanged: don't disturb bound nodes
                    }

                    SetSaveValue(row);
                }

                // Live patches may introduce or remove sparse Class spine rows
                // and root provenance. Rebuild the virtual index only after the
                // incoming overlay is complete so readers never see a partial
                // replay graph. A live apply is not a place to fail closed:
                // one malformed root must not gut the index for a running
                // game, so failures are scoped to their own root.
                InitializeVirtualInstanceValues(failClosed: false);

                // Binding metadata is independent from the target rows. Keep
                // absent (authored fallback) distinct from present-null
                // (explicit unset), and invalidate static wrappers even when
                // no value row changed in this live patch.
                var staticMemberIds = new HashSet<string>(saveData.staticBindings.Keys);
                staticMemberIds.UnionWith(incoming.staticBindings.Keys);
                foreach (string memberId in staticMemberIds)
                {
                    bool hadBefore = saveData.staticBindings.TryGetValue(
                        memberId,
                        out string? before);
                    bool hasAfter = incoming.staticBindings.TryGetValue(
                        memberId,
                        out string? after);
                    if (hadBefore == hasAfter && (!hadBefore || before == after))
                    {
                        continue;
                    }
                    if (hasAfter)
                    {
                        saveData.staticBindings[memberId] = after;
                    }
                    else
                    {
                        saveData.staticBindings.Remove(memberId);
                    }
                    OnStaticBindingChanged?.Invoke(
                        NeoValueOwnership.Save,
                        memberId);
                }
            }
            finally
            {
                suppressLiveAutoCommit = false;
                CurrentChangeSource = NeoChangeSource.Local;
            }
        }

        private void HandleLiveContentChanged(string content)
        {
            if (isDisposed) return;
            ApplyExternalSaveContent(content);
        }

        public Awaitable CommitAsync(bool replaceSnapshot = false) =>
            CommitCoreAsync(
                replaceSnapshot,
                warnUnlinked: true,
                flushLiveImmediately: true);

        private async Awaitable CommitCoreAsync(
            bool replaceSnapshot,
            bool warnUnlinked,
            bool flushLiveImmediately)
        {
            if (!SaveHasSemanticChanges())
            {
                RestoreCommittedSaveMetadata();
                return;
            }

            if (warnUnlinked)
            {
                var unlinkedValueIds = FindUnlinkedSaveValueIds();
                if (unlinkedValueIds.Count > 0)
                {
                    Debug.LogWarning(
                        $"NeoCompose save contains {unlinkedValueIds.Count} unlinked value(s). " +
                        "This can happen when generated factory values are created but never assigned. " +
                        "Call RunGarbageCollector() before CommitAsync() to delete unlinked values.");
                }
            }
            var savedAt = NeoTimestamp.Now();
            saveData.updatedAt = savedAt;
            CaptureSaveDiagnostics(savedAt);
            var content = SerializeSaveData();
            if (loader is NeoSaveSynchronizer synchronizer)
            {
                await synchronizer.CommitSaveContentAsync(
                    content,
                    replaceSnapshot,
                    flushLiveImmediately,
                    useTrackedMutations: true);
                CaptureCommittedSaveState(content);
                return;
            }

            await loader.CommitSaveContentAsync(content, replaceSnapshot);
            CaptureCommittedSaveState(content);
        }

        public int RunGarbageCollector()
        {
            var unlinkedValueIds = FindUnlinkedSaveValueIds();
            foreach (var valueId in unlinkedValueIds)
            {
                RemoveSaveValueAndDescendants(valueId);
            }
            return unlinkedValueIds.Count;
        }

        public IReadOnlyList<string> FindUnlinkedSaveValueIds()
        {
            var reachable = BuildReachableSaveValueIds();

            var unlinked = new List<string>();
            foreach (var valueId in saveData.values.Keys)
            {
                if (!reachable.Contains(valueId)) unlinked.Add(valueId);
            }
            return unlinked;
        }

        private HashSet<string> BuildReachableSaveValueIds()
        {
            return BuildReachableWritableValueIds(NeoValueOwnership.Save);
        }

        private HashSet<string> BuildReachableWritableValueIds(NeoValueOwnership ownership)
        {
            // Sparse stable-id overlay: reachability is rooted at the save/session
            // root member's authored value id (a write shadows that id in
            // place), then walks the overlaid graph. There is no override map.
            var reachable = new HashSet<string>();
            string rootMemberId = ownership == NeoValueOwnership.Save
                ? data.project.rootSaveFileMemberId
                : data.project.rootSessionMemberId;
            if (data.members.TryGetValue(rootMemberId, out Member rootMember)
                && rootMember.valueId is not null)
            {
                MarkReachableValue(ownership, rootMember.valueId, reachable);
            }
            // Class-owned members are independent roots. Resolve through the
            // selected Save/Session binding layer so a rebound graph remains
            // live while an overwritten or tombstoned target becomes eligible
            // for ordinary orphan collection.
            foreach (Member staticMember in data.members.Values)
            {
                if (!staticMember.isStatic
                    || ResolveStaticOwnership(staticMember) != ownership)
                {
                    continue;
                }
                if (TryResolveStaticBinding(
                        staticMember.id,
                        out _,
                        out _,
                        out string? staticValueId))
                {
                    MarkReachableValue(
                        ownership,
                        staticValueId,
                        reachable,
                        staticMember);
                }
            }
            foreach (var pair in authoredOwnership)
            {
                if (pair.Value == ownership)
                {
                    MarkReachableValue(ownership, pair.Key, reachable);
                }
            }
            foreach (var row in data.values.Values)
            {
                if (row is ObjectMemberValue obj
                    && obj.classId is string runtimeClassId
                    && TryResolveSchemaClassAllowedOwnership(runtimeClassId, out NeoValueOwnership typeOwnership)
                    && typeOwnership == ownership)
                {
                    MarkReachableValue(ownership, row.id, reachable);
                }
            }
            // Storage partitions: overlay rows stamped into a partition that
            // is NOT currently loaded cannot be judged — their containment
            // container (and the rest of the authored subtree anchoring them)
            // is not in memory. Keep them; reachability re-applies normally
            // once the partition loads.
            var store = GetWritableStore(ownership);
            foreach (var row in store.values.Values)
            {
                if (string.IsNullOrEmpty(row.mapKey)) continue;
                if (loadedPartitionRowIds.ContainsKey(row.mapKey!)) continue;
                reachable.Add(row.id);
            }
            ExpandContainmentReachability(ownership, reachable);
            return reachable;
        }

        /// <summary>
        /// Containment is a reachability edge (spec §1.6): a member row of an
        /// unordered list is reachable iff its container row is reachable AND
        /// the container's resolved value is <c>[]</c> (present). Removal
        /// tombstones at member ids are reachable whenever their container is
        /// — they are the durable record that an authored member was removed
        /// and must survive GC. Live member rows behind a null container are
        /// anomalies per the cascade rule and stay collectable. Members may
        /// themselves own containers, so newly reached rows are
        /// queued and each reachable container is expanded once.
        /// </summary>
        private void ExpandContainmentReachability(
            NeoValueOwnership ownership,
            HashSet<string> reachable)
        {
            var store = GetWritableStore(ownership);
            var (storeByContainer, _) = MembershipMaps(ownership);
            var pendingContainers = new Queue<string>(reachable);
            var expandedContainers = new HashSet<string>();
            while (pendingContainers.Count > 0)
            {
                string containerId = pendingContainers.Dequeue();
                if (!expandedContainers.Add(containerId)) continue;
                if (!authoredEntriesByContainer.ContainsKey(containerId)
                    && !storeByContainer.ContainsKey(containerId))
                {
                    continue;
                }
                bool containerPresent =
                    TryGetOverlaidValue(ownership, containerId, out ArrayMemberValue? containerRow)
                    && containerRow.value is not null;

                foreach (var memberId in EnumerateContainerMemberIds(
                    ownership,
                    storeByContainer,
                    containerId))
                {
                    bool isTombstone =
                        store.values.TryGetValue(memberId, out MemberValue overlayRow)
                        && overlayRow.IsRemoved;
                    if (isTombstone)
                    {
                        if (reachable.Add(memberId))
                        {
                            pendingContainers.Enqueue(memberId);
                        }
                        continue;
                    }
                    if (!containerPresent || reachable.Contains(memberId)) continue;
                    MarkReachableValue(
                        ownership,
                        memberId,
                        reachable,
                        null,
                        pendingContainers);
                }
            }
        }

        private IEnumerable<string> EnumerateContainerMemberIds(
            NeoValueOwnership ownership,
            Dictionary<string, HashSet<string>> storeByContainer,
            string containerId)
        {
            var store = GetWritableStore(ownership);
            var emitted = new HashSet<string>();
            if (authoredEntriesByContainer.TryGetValue(containerId, out var authoredMembers))
            {
                foreach (var id in authoredMembers)
                {
                    if (store.values.TryGetValue(id, out MemberValue overlayRow))
                    {
                        // A removal marker remains reachable containment state
                        // for this authored membership. Any live overlay row
                        // replaces the authored stamp and is emitted only from
                        // its current indexed container below.
                        if (overlayRow.IsRemoved && emitted.Add(id)) yield return id;
                        continue;
                    }
                    if (emitted.Add(id)) yield return id;
                }
            }
            if (storeByContainer.TryGetValue(containerId, out var storeMembers))
            {
                foreach (var id in storeMembers)
                {
                    if (emitted.Add(id)) yield return id;
                }
            }
        }

        private void MarkReachableValue(
            NeoValueOwnership ownership,
            string valueId,
            HashSet<string> reachable,
            Member? sourceMember = null,
            Queue<string>? newlyReachable = null)
        {
            var store = GetWritableStore(ownership);
            var pending = new Queue<(string valueId, Member? member)>();
            pending.Enqueue((valueId, sourceMember));
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                if (!reachable.Add(current.valueId)) continue;
                newlyReachable?.Enqueue(current.valueId);
                if (!store.values.TryGetValue(current.valueId, out MemberValue? val)
                    && !data.values.TryGetValue(current.valueId, out val))
                {
                    continue;
                }
                Member? currentMember = current.member;
                currentMember ??= TryInferMemberForValueId(
                    current.valueId,
                    out Member? inferredMember)
                        ? inferredMember
                        : null;
                foreach (var child in EnumerateOwnedChildLinks(val, currentMember))
                {
                    NeoValueOwnership childOwnership =
                        (child.member is null ? null : DeclaredOwnership(child.member)) ?? ownership;
                    if (childOwnership == ownership)
                    {
                        pending.Enqueue((child.valueId, child.member));
                    }
                }
            }
        }

        private void CaptureSaveDiagnostics(NeoTimestamp savedAt)
        {
            if (!SaveOptions.DiagnosticsEnabled)
            {
                saveData.platforms = null;
                saveData.systems = null;
                saveData.inputDevices = null;
                return;
            }

            saveData.platforms ??= new List<GameRuntimePlatform>();
            saveData.systems ??= new List<GameSystemInfo>();
            saveData.inputDevices ??= new List<GameInputDeviceInfo>();

            UpsertPlatform(saveData.platforms, CaptureRuntimePlatform(), savedAt);
            UpsertSystem(saveData.systems, CaptureSystemInfo(), savedAt);
            foreach (var device in CaptureInputDevices())
            {
                UpsertInputDevice(saveData.inputDevices, device, savedAt);
            }
        }

        private static GameRuntimePlatform CaptureRuntimePlatform()
        {
            return new GameRuntimePlatform
            {
                kind = Application.platform.ToString(),
            };
        }

        private static GameSystemInfo CaptureSystemInfo()
        {
            return new GameSystemInfo
            {
                deviceType = SystemInfo.deviceType.ToString(),
                deviceModel = SystemInfo.deviceModel ?? "",
                deviceName = SystemInfo.deviceName ?? "",
                operatingSystem = SystemInfo.operatingSystem ?? "",
            };
        }

        private static List<GameInputDeviceInfo> CaptureInputDevices()
        {
            var devices = new List<GameInputDeviceInfo>();
            CaptureLegacyInputDevices(devices);
            CaptureInputSystemDevices(devices);
            return devices;
        }

        private static void CaptureLegacyInputDevices(List<GameInputDeviceInfo> devices)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            var names = Input.GetJoystickNames();
            for (int i = 0; i < names.Length; i++)
            {
                var name = names[i] ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;
                devices.Add(new GameInputDeviceInfo
                {
                    source = "legacy-input",
                    kind = "joystick",
                    name = name,
                    displayName = name,
                    layout = "",
                    manufacturer = "",
                    product = "",
                    slot = i,
                });
            }
#endif
        }

        private static void CaptureInputSystemDevices(List<GameInputDeviceInfo> devices)
        {
            var inputSystemType = System.Type.GetType("UnityEngine.InputSystem.InputSystem, Unity.InputSystem");
            if (inputSystemType == null) return;
            var devicesProperty = inputSystemType.GetProperty("devices", BindingFlags.Public | BindingFlags.Static);
            if (devicesProperty?.GetValue(null) is not System.Collections.IEnumerable inputDevices) return;

            foreach (var device in inputDevices)
            {
                if (device == null) continue;
                var deviceType = device.GetType();
                var description = deviceType.GetProperty("description")?.GetValue(device);
                var descriptionType = description?.GetType();
                var deviceClass = GetStringProperty(description, descriptionType, "deviceClass");
                var layout = GetStringProperty(device, deviceType, "layout");
                devices.Add(new GameInputDeviceInfo
                {
                    source = "input-system",
                    kind = string.IsNullOrWhiteSpace(deviceClass) ? InferInputDeviceKind(layout) : deviceClass.ToLowerInvariant(),
                    name = GetStringProperty(device, deviceType, "name"),
                    displayName = GetStringProperty(device, deviceType, "displayName"),
                    layout = layout,
                    manufacturer = GetStringProperty(description, descriptionType, "manufacturer"),
                    product = GetStringProperty(description, descriptionType, "product"),
                    slot = null,
                });
            }
        }

        private static string GetStringProperty(object? target, System.Type? targetType, string propertyName)
        {
            if (target == null || targetType == null) return "";
            return targetType.GetProperty(propertyName)?.GetValue(target)?.ToString() ?? "";
        }

        private static string InferInputDeviceKind(string layout)
        {
            if (layout.IndexOf("gamepad", System.StringComparison.OrdinalIgnoreCase) >= 0) return "gamepad";
            if (layout.IndexOf("joystick", System.StringComparison.OrdinalIgnoreCase) >= 0) return "joystick";
            if (layout.IndexOf("keyboard", System.StringComparison.OrdinalIgnoreCase) >= 0) return "keyboard";
            if (layout.IndexOf("mouse", System.StringComparison.OrdinalIgnoreCase) >= 0) return "mouse";
            if (layout.IndexOf("touch", System.StringComparison.OrdinalIgnoreCase) >= 0) return "touch";
            return "unknown";
        }

        private static void UpsertPlatform(List<GameRuntimePlatform> platforms, GameRuntimePlatform next, NeoTimestamp savedAt)
        {
            var match = platforms.Find(existing => existing.kind == next.kind);
            if (match == null)
            {
                next.lastSavedAt = savedAt;
                platforms.Add(next);
                return;
            }
            match.lastSavedAt = savedAt;
        }

        private static void UpsertSystem(List<GameSystemInfo> systems, GameSystemInfo next, NeoTimestamp savedAt)
        {
            var match = systems.Find(existing =>
                existing.deviceType == next.deviceType
                && existing.deviceModel == next.deviceModel
                && existing.deviceName == next.deviceName
                && existing.operatingSystem == next.operatingSystem);
            if (match == null)
            {
                next.lastSavedAt = savedAt;
                systems.Add(next);
                return;
            }
            match.lastSavedAt = savedAt;
        }

        private static void UpsertInputDevice(List<GameInputDeviceInfo> devices, GameInputDeviceInfo next, NeoTimestamp savedAt)
        {
            var match = devices.Find(existing =>
                existing.source == next.source
                && existing.kind == next.kind
                && existing.name == next.name
                && existing.displayName == next.displayName
                && existing.layout == next.layout
                && existing.manufacturer == next.manufacturer
                && existing.product == next.product
                && existing.slot == next.slot);
            if (match == null)
            {
                next.lastSavedAt = savedAt;
                devices.Add(next);
                return;
            }
            match.lastSavedAt = savedAt;
        }

        /// <summary>
        /// Adopts the loader-resolved active-save content, or builds save defaults
        /// from the schema when there is nothing to load (a brand-new draft, or
        /// content that failed to parse). Persistence is the loader's job — nothing
        /// is written here; the first <see cref="CommitAsync"/> persists the draft.
        /// </summary>
        [MemberNotNull(nameof(saveData))]
        private bool LoadSaveDataOrDefault(string? content)
        {
            ProjectSaveData? parsed = null;
            if (!string.IsNullOrEmpty(content))
            {
                try
                {
                    parsed = DeserializeSaveData(content);
                }
                catch (System.Exception exception)
                {
                    Debug.LogError(exception);
                }
            }

            // `DeserializeSaveData` returns null on empty/whitespace without throwing,
            // so a null/empty resolution still needs the default-build fallback.
            saveData = parsed ?? BuildDefaultSaveData();
            saveData.values ??= new();
            saveData.staticBindings ??= new();
            try
            {
                RecoverReadOnlySaveInstanceKeys();
            }
            finally
            {
                // Partition rows are materialized solely for constructor-time
                // readonly validation/recovery. Do not retain that merged
                // projection for the client's lifetime.
                ReleaseReadOnlyAuthoredValueContext();
            }
            return parsed is not null;
        }
    }
}
