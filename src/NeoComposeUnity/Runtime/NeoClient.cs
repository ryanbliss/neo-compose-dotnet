// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Tasks;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// NeoClient owns a live save file instance.
    /// </summary>
    public class NeoClient : INeoClient
    {
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
        public NeoAttributeCustom assets { get; protected set; }
        public NeoAttributeCustomWritable save { get; protected set; }
        public NeoAttributeCustomWritable session { get; protected set; }
        public NeoAttributeCustom AssetsRoot => assets;
        public NeoAttributeCustomWritable SaveRoot => save;
        public NeoAttributeCustomWritable SessionRoot => session;
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
        /// Flat registry of every constructed <see cref="NeoAttribute"/>,
        /// keyed by <see cref="MakeNodeKey"/>. Each
        /// <see cref="NeoAttribute"/> registers itself at the end of
        /// construction so consumers (and the
        /// <see cref="NeoAttribute.Create"/> /
        /// <see cref="NeoAttribute.CreateWritable"/> factories) can look up
        /// existing instances and reuse them rather than constructing
        /// duplicates that share the same wire identity.
        /// </summary>
        internal IReadOnlyDictionary<string, NeoAttribute> nodes => nodesInternal;
        private readonly Dictionary<string, NeoAttribute> nodesInternal = new();
        private readonly Dictionary<string, NeoGeneratedCustomValue> generatedValuesInternal = new();
        private readonly HashSet<NeoDialogue> activeDialogues = new();
        private bool isDisposed;

        /// <summary>
        /// Read-only views over the underlying project + save maps.
        /// Exposed for evaluators / inspectors that need to enumerate
        /// the full set rather than fetch one-at-a-time via
        /// <see cref="TryGetAttribute{T}"/> et al. The returned
        /// dictionaries are the same instances the client reads from
        /// internally — mutations through these views propagate.
        /// </summary>
        internal IReadOnlyDictionary<string, Attribute> attributes => data.attributes;
        internal IReadOnlyDictionary<string, AttributeValue> values => data.values;
        internal IReadOnlyDictionary<string, CustomType> types => data.types;
        internal IReadOnlyDictionary<string, Enum> enums => data.enums;
        internal IReadOnlyDictionary<string, Dialogue> dialogues => data.dialogues;
        internal IReadOnlyDictionary<string, DialogueGroup> dialogueGroups => data.dialogueGroups;
        internal IReadOnlyDictionary<string, PriorityGroup> priorityGroups => data.priorityGroups;
        internal IReadOnlyDictionary<string, AttributeValue> saveValues => saveData.values;
        internal IReadOnlyDictionary<string, AttributeValue> sessionValues => sessionData.values;
        internal Project project => data.project;
        internal bool IsDisposed => isDisposed;

        /// <summary>
        /// Fired whenever a save-side value row is added, replaced, or
        /// removed. The argument is the affected value id.
        /// Generated wrappers and runtime collection helpers use this as a
        /// coarse invalidation signal after <c>*Writable</c> mutations.
        /// </summary>
        internal event System.Action<string>? OnSaveValueChanged;
        internal event System.Action<NeoValueOwnership, string>? OnWritableValueChanged;

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
        private void RaiseSaveValueChanged(string id)
        {
            OnSaveValueChanged?.Invoke(id);
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
                await CommitCoreAsync(replaceSnapshot: false, warnUnlinked: false);
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
        protected ProjectSaveData saveData;
        protected ProjectSaveData sessionData;
        private readonly INeoSaveLoader loader;
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
            SaveOptions = saveOptions ?? new NeoSaveOptions();
            this.assetDatabase = assetDatabase;
            Localization = localization ?? NeoLocalization.CreateEmpty(data.localization);
            ValidateRootCustomAttribute(data.project.rootAssetsAttributeId, nameof(Project.rootAssetsAttributeId));
            ValidateRootCustomAttribute(data.project.rootSaveFileAttributeId, nameof(Project.rootSaveFileAttributeId));
            ValidateRootCustomAttribute(data.project.rootSessionAttributeId, nameof(Project.rootSessionAttributeId));
            ValidateFunctionAttributes();
            LoadSaveDataOrDefault(loadedSaveContent);
            sessionData = BuildDefaultSessionData();
            BuildAuthoredOwnershipMap();
            InitializeSaveDefaults();
            InitializeSessionDefaults();
            assets = new(this, data.project.rootAssetsAttributeId, null);
            save = new(this, data.project.rootSaveFileAttributeId, null, NeoValueOwnership.Save);
            session = new(this, data.project.rootSessionAttributeId, null, NeoValueOwnership.Session);
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            foreach (var dialogue in new List<NeoDialogue>(activeDialogues))
            {
                dialogue.DisposeFromClient();
            }
            activeDialogues.Clear();
            assets.Dispose();
            save.Dispose();
            session.Dispose();
            OnSaveValueChanged = null;
            OnWritableValueChanged = null;
        }

        internal bool TryGetAttribute<TAttribute>(string id, [NotNullWhen(true)] out TAttribute? attribute) where TAttribute : Attribute
        {
            if (data.attributes.TryGetValue(id, out Attribute idMatch))
            {
                if (idMatch is TAttribute match)
                {
                    attribute = match;
                    return true;
                }
            }
            attribute = null;
            return false;
        }

        internal bool TryGetType(string id, [NotNullWhen(true)] out CustomType? type)
        {
            if (data.types.TryGetValue(id, out CustomType idMatch))
            {
                type = idMatch;
                return true;
            }
            type = null;
            return false;
        }

        internal bool TryGetValue<TValue>(string id, [NotNullWhen(true)] out TValue? value) where TValue : AttributeValue
        {
            if (sessionData.values.TryGetValue(id, out AttributeValue sessionIdMatch))
            {
                if (sessionIdMatch is TValue match)
                {
                    value = match;
                    return true;
                }
            }
            if (saveData.values.TryGetValue(id, out AttributeValue saveIdMatch))
            {
                if (saveIdMatch is TValue match)
                {
                    value = match;
                    return true;
                }
            }
            if (data.values.TryGetValue(id, out AttributeValue idMatch))
            {
                if (idMatch is TValue match)
                {
                    value = match;
                    return true;
                }
            }
            value = null;
            return false;
        }

        internal bool TryGetValue<TValue>(
            NeoValueOwnership ownership,
            string id,
            [NotNullWhen(true)] out TValue? value) where TValue : AttributeValue
        {
            value = null;
            switch (ownership)
            {
                case NeoValueOwnership.Session:
                    if (sessionData.values.TryGetValue(id, out AttributeValue sessionMatch))
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
                    if (saveData.values.TryGetValue(id, out AttributeValue saveMatch))
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
            if (data.values.TryGetValue(id, out AttributeValue assetMatch)
                && assetMatch is TValue typedAsset)
            {
                value = typedAsset;
                return true;
            }
            return false;
        }

        // Save and Session graphs are disjoint subtrees of the authored asset graph.
        // Precomputed once at load, this maps every authored value id to the
        // writable graph it belongs to — so a *sparse* overlay can still answer
        // "is this value writable here?" before the value has been shadowed into the
        // writable store (runtime-minted ids are caught by store membership below).
        private readonly Dictionary<string, NeoValueOwnership> authoredOwnership = new();

        private void BuildAuthoredOwnershipMap()
        {
            MarkAuthoredOwnership(data.project.rootSaveFileAttributeId, NeoValueOwnership.Save);
            MarkAuthoredOwnership(data.project.rootSessionAttributeId, NeoValueOwnership.Session);
        }

        private void MarkAuthoredOwnership(string rootAttributeId, NeoValueOwnership ownership)
        {
            if (!data.attributes.TryGetValue(rootAttributeId, out Attribute rootAttribute)
                || rootAttribute.valueId is null)
            {
                return;
            }
            WalkAuthoredOwnership(rootAttribute.valueId, rootAttribute, ownership, new HashSet<string>());
        }

        private void WalkAuthoredOwnership(
            string valueId,
            Attribute? attribute,
            NeoValueOwnership ownership,
            HashSet<string> visited)
        {
            if (!visited.Add(valueId)) return;
            authoredOwnership[valueId] = ownership;
            if (!data.values.TryGetValue(valueId, out AttributeValue row)) return;
            foreach (var child in EnumerateOwnedChildLinks(row, attribute))
            {
                WalkAuthoredOwnership(child.valueId, child.attribute, ownership, visited);
            }
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
            ownership = NeoValueOwnership.Asset;
            return false;
        }

        /// <summary>
        /// Test/seed helper: shadow a save value at its stable id. Value ids are
        /// stable instance identities, so seeding a value is just a write at its
        /// own id; <paramref name="attributeId"/> is retained for call-site
        /// readability but no longer maps through an override table.
        /// </summary>
        internal void AddSaveValue<TAttributeValue>(string attributeId, TAttributeValue value) where TAttributeValue : AttributeValue
        {
            _ = attributeId;
            SetWritableValue(NeoValueOwnership.Save, value);
        }

        internal void SetSaveValue<TAttributeValue>(TAttributeValue value) where TAttributeValue : AttributeValue
        {
            SetWritableValue(NeoValueOwnership.Save, value);
        }

        internal void SetSaveValueSilently<TAttributeValue>(TAttributeValue value) where TAttributeValue : AttributeValue
        {
            SetWritableValueSilently(NeoValueOwnership.Save, value);
        }

        /// <summary>
        /// Stable-id overlay resolution for a writable (Save/Session) node: the
        /// ownership store's row when present — a removal tombstone
        /// (<see cref="AttributeValue.IsRemoved"/>) resolves as <b>unset</b>
        /// (returns false, never falling through) — otherwise the authored asset
        /// default. This is the shadow rule <c>save.values[id] ?? authored</c>
        /// shared with the web overlay, and is what lets a save stay sparse:
        /// untouched values read through to the defaults by their stable id.
        /// </summary>
        internal bool TryGetOverlaidValue<TValue>(
            NeoValueOwnership ownership,
            string id,
            [NotNullWhen(true)] out TValue? value) where TValue : AttributeValue
        {
            value = null;
            if (ownership != NeoValueOwnership.Asset)
            {
                var store = GetWritableStore(ownership);
                if (store.values.TryGetValue(id, out AttributeValue overlaid))
                {
                    if (overlaid.IsRemoved) return false; // explicit unset; no fallthrough
                    value = overlaid as TValue;
                    return value is not null;
                }
            }
            if (data.values.TryGetValue(id, out AttributeValue assetRow))
            {
                value = assetRow as TValue;
                return value is not null;
            }
            return false;
        }

        /// <summary>
        /// Returns a writable clone of <paramref name="row"/> keeping its id, so a
        /// writable <c>Set</c> can mutate + shadow it without touching the shared
        /// authored asset object it may have resolved through.
        /// </summary>
        internal AttributeValue CloneRowForWrite(AttributeValue row) => CloneValueRow(row);

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
            if (!TryGetOverlaidValue(ownership, id, out AttributeValue? resolved)) return false;
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
            var tombstone = new NullAttributeValue
            {
                id = id,
                createdAt = nowIso,
                updatedAt = nowIso,
                mark = NeoValueMarks.Removed,
            };
            SetWritableValue(ownership, tombstone);
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
            var formerChildIds = new List<string>();
            if (TryGetOverlaidValue(ownership, id, out AttributeValue? replaced))
            {
                CollectDirectChildValueIds(replaced, formerChildIds);
            }
            WriteTombstone(ownership, id);
            if (formerChildIds.Count == 0) return;
            // Build reachability after the tombstone replaces the value, so the
            // former children are seen as orphaned (unless shared elsewhere).
            var reachable = BuildReachableWritableValueIds(ownership);
            foreach (var childId in formerChildIds)
            {
                RemoveWritableValueAndDescendantsIfUnlinked(ownership, childId, reachable);
            }
        }

        private static void CollectDirectChildValueIds(AttributeValue row, List<string> into)
        {
            switch (row)
            {
                case ObjectAttributeValue obj when obj.value is not null:
                    into.AddRange(obj.value.Values);
                    break;
                case ArrayAttributeValue arr when arr.value is not null:
                    into.AddRange(arr.value);
                    break;
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
            if (!store.values.Remove(id)) return false;
            TouchWritableStoreUpdatedAt(ownership);
            OnWritableValueChanged?.Invoke(ownership, id);
            if (ownership == NeoValueOwnership.Save)
            {
                RaiseSaveValueChanged(id);
            }
            return true;
        }

        internal void SetWritableValue<TAttributeValue>(
            NeoValueOwnership ownership,
            TAttributeValue value) where TAttributeValue : AttributeValue
        {
            GetWritableStore(ownership).values[value.id] = value;
            TouchWritableStoreUpdatedAt(ownership);
            OnWritableValueChanged?.Invoke(ownership, value.id);
            if (ownership == NeoValueOwnership.Save)
            {
                RaiseSaveValueChanged(value.id);
            }
        }

        internal void SetWritableValueSilently<TAttributeValue>(
            NeoValueOwnership ownership,
            TAttributeValue value) where TAttributeValue : AttributeValue
        {
            GetWritableStore(ownership).values[value.id] = value;
            TouchWritableStoreUpdatedAt(ownership);
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
            string sourceValueId)
        {
            if (targetOwnership == NeoValueOwnership.Asset)
            {
                throw new System.InvalidOperationException("Cannot import a writable value into asset ownership.");
            }
            if (!TryGetValueOwnership(sourceValueId, out NeoValueOwnership sourceOwnership))
            {
                return sourceValueId;
            }
            if (sourceOwnership == targetOwnership)
            {
                return sourceValueId;
            }
            if (sourceOwnership == NeoValueOwnership.Session
                && targetOwnership == NeoValueOwnership.Save
                && !BuildReachableWritableValueIds(NeoValueOwnership.Session).Contains(sourceValueId))
            {
                PromoteValueGraph(
                    NeoValueOwnership.Session,
                    NeoValueOwnership.Save,
                    sourceValueId,
                    new HashSet<string>(),
                    TryInferAttributeForValueId(sourceValueId, out Attribute? sourceAttribute)
                        ? sourceAttribute
                        : null);
                return sourceValueId;
            }
            return CloneValueGraphToOwnership(
                targetOwnership,
                sourceValueId,
                new Dictionary<string, string>(),
                TryInferAttributeForValueId(sourceValueId, out Attribute? inferredAttribute)
                    ? inferredAttribute
                    : null);
        }

        private void PromoteValueGraph(
            NeoValueOwnership sourceOwnership,
            NeoValueOwnership targetOwnership,
            string valueId,
            HashSet<string> visited,
            Attribute? sourceAttribute = null)
        {
            if (!visited.Add(valueId)) return;
            var sourceStore = GetWritableStore(sourceOwnership);
            var targetStore = GetWritableStore(targetOwnership);
            if (!sourceStore.values.TryGetValue(valueId, out AttributeValue? row)) return;

            targetStore.values[valueId] = row;
            foreach (var child in EnumerateOwnedChildLinks(row, sourceAttribute))
            {
                PromoteValueGraph(
                    sourceOwnership,
                    targetOwnership,
                    child.valueId,
                    visited,
                    child.attribute);
            }
            sourceStore.values.Remove(valueId);
            OnWritableValueChanged?.Invoke(sourceOwnership, valueId);
            OnWritableValueChanged?.Invoke(targetOwnership, valueId);
            if (targetOwnership == NeoValueOwnership.Save) RaiseSaveValueChanged(valueId);
        }

        private string CloneValueGraphToOwnership(
            NeoValueOwnership targetOwnership,
            string sourceValueId,
            Dictionary<string, string> remappedIds,
            Attribute? sourceAttribute = null)
        {
            if (remappedIds.TryGetValue(sourceValueId, out string existingId)) return existingId;
            if (!TryGetValue(sourceValueId, out AttributeValue? sourceRow))
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
                case ObjectAttributeValue obj when obj.value is not null:
                {
                    var remapped = new Dictionary<string, string>();
                    foreach (var pair in obj.value)
                    {
                        Attribute? childAttribute = TryResolveOwnedChildAttribute(sourceRow, sourceAttribute, pair.Key);
                        remapped[pair.Key] = childAttribute is not null && TryGetValue(pair.Value, out AttributeValue? _)
                            ? CloneValueGraphToOwnership(targetOwnership, pair.Value, remappedIds, childAttribute)
                            : pair.Value;
                    }
                    obj.value = remapped;
                    break;
                }
                case ArrayAttributeValue arr when arr.value is not null:
                {
                    if (sourceAttribute is LookupAttribute)
                    {
                        break;
                    }
                    Attribute? entryAttribute = TryResolveCollectionEntryAttribute(sourceAttribute);
                    var remapped = new string[arr.value.Length];
                    for (int i = 0; i < arr.value.Length; i++)
                    {
                        string childId = arr.value[i];
                        remapped[i] = entryAttribute is not null && TryGetValue(childId, out AttributeValue? _)
                            ? CloneValueGraphToOwnership(targetOwnership, childId, remappedIds, entryAttribute)
                            : childId;
                    }
                    arr.value = remapped;
                    break;
                }
            }

            SetWritableValue(targetOwnership, clone);
            return clone.id;
        }

        private IEnumerable<(string valueId, Attribute? attribute)> EnumerateOwnedChildLinks(
            AttributeValue row,
            Attribute? sourceAttribute)
        {
            switch (row)
            {
                case ObjectAttributeValue obj when obj.value is not null:
                    foreach (var pair in obj.value)
                    {
                        Attribute? childAttribute = TryResolveOwnedChildAttribute(row, sourceAttribute, pair.Key);
                        if (childAttribute is not null)
                        {
                            yield return (pair.Value, childAttribute);
                        }
                    }
                    break;
                case ArrayAttributeValue arr when arr.value is not null:
                    if (sourceAttribute is LookupAttribute)
                    {
                        yield break;
                    }
                    Attribute? entryAttribute = TryResolveCollectionEntryAttribute(sourceAttribute);
                    if (entryAttribute is null)
                    {
                        yield break;
                    }
                    foreach (var childId in arr.value)
                    {
                        yield return (childId, entryAttribute);
                    }
                    break;
            }
        }

        private Attribute? TryResolveOwnedChildAttribute(
            AttributeValue row,
            Attribute? sourceAttribute,
            string key)
        {
            if (sourceAttribute is DictionaryAttribute dictionary)
            {
                return TryGetAttribute(dictionary.entryAttributeId, out Attribute? entryAttribute)
                    ? entryAttribute
                    : null;
            }

            string? customTypeId = sourceAttribute is CustomAttribute custom
                ? custom.customTypeId
                : (row as ObjectAttributeValue)?.typeId;
            if (string.IsNullOrEmpty(customTypeId)) return null;
            if (!TryResolveMergedSchemaAttribute(customTypeId!, key, out Attribute? childAttribute))
            {
                return null;
            }
            return childAttribute;
        }

        private Attribute? TryResolveCollectionEntryAttribute(Attribute? collectionAttribute)
        {
            string? entryAttributeId = collectionAttribute switch
            {
                ListAttribute list => list.entryAttributeId,
                DictionaryAttribute dictionary => dictionary.entryAttributeId,
                _ => null,
            };
            return !string.IsNullOrEmpty(entryAttributeId)
                && TryGetAttribute(entryAttributeId!, out Attribute? entryAttribute)
                    ? entryAttribute
                    : null;
        }

        private bool TryResolveMergedSchemaAttribute(
            string customTypeId,
            string key,
            [NotNullWhen(true)] out Attribute? attribute)
        {
            attribute = null;
            var merged = CustomTypeInheritance.MergeSchemas(
                CustomTypeInheritance.ResolveChain(
                    customTypeId,
                    id => TryGetType(id, out CustomType? match) ? match : null));
            foreach (var entry in merged)
            {
                if (entry.schemaKey == key
                    && TryGetAttribute(entry.attributeId, out Attribute? childAttribute))
                {
                    attribute = childAttribute;
                    return true;
                }
            }
            return false;
        }

        internal bool TryInferAttributeForValueId(
            string valueId,
            [NotNullWhen(true)] out Attribute? attribute)
        {
            return TryInferAttributeForValueId(valueId, new HashSet<string>(), out attribute);
        }

        private bool TryInferAttributeForValueId(
            string valueId,
            HashSet<string> visitingValueIds,
            [NotNullWhen(true)] out Attribute? attribute)
        {
            foreach (var candidate in data.attributes.Values)
            {
                if (candidate.valueId == valueId)
                {
                    attribute = candidate;
                    return true;
                }
            }

            foreach (var parent in EnumerateAllValueRows())
            {
                if (parent.Value is not ObjectAttributeValue objectValue
                    || objectValue.value == null)
                {
                    continue;
                }

                foreach (var pair in objectValue.value)
                {
                    if (pair.Value != valueId) continue;
                    if (TryInferAttributeForValueId(
                            parent.Key,
                            new HashSet<string>(visitingValueIds),
                            out Attribute? parentAttribute)
                        && TryResolveCollectionEntryAttribute(parentAttribute) is Attribute parentEntryAttribute)
                    {
                        attribute = parentEntryAttribute;
                        return true;
                    }

                    if (TryInferCustomTypeIdForValueId(
                            parent.Key,
                            new HashSet<string>(visitingValueIds),
                            out string? parentTypeId)
                        && !string.IsNullOrEmpty(parentTypeId)
                        && TryResolveMergedSchemaAttribute(parentTypeId!, pair.Key, out Attribute? childAttribute))
                    {
                        attribute = childAttribute;
                        return true;
                    }
                }
            }

            foreach (var parent in EnumerateAllValueRows())
            {
                if (parent.Value is ArrayAttributeValue arrayValue
                    && arrayValue.value != null
                    && System.Array.IndexOf(arrayValue.value, valueId) >= 0
                    && TryInferAttributeForValueId(
                        parent.Key,
                        new HashSet<string>(visitingValueIds),
                        out Attribute? collectionAttribute)
                    && TryResolveCollectionEntryAttribute(collectionAttribute) is Attribute entryAttribute)
                {
                    attribute = entryAttribute;
                    return true;
                }

                if (parent.Value is ObjectAttributeValue dictionaryValue
                    && dictionaryValue.value != null
                    && dictionaryValue.value.ContainsValue(valueId)
                    && TryInferAttributeForValueId(
                        parent.Key,
                        new HashSet<string>(visitingValueIds),
                        out collectionAttribute)
                    && TryResolveCollectionEntryAttribute(collectionAttribute) is Attribute dictionaryEntryAttribute)
                {
                    attribute = dictionaryEntryAttribute;
                    return true;
                }
            }

            attribute = null;
            return false;
        }

        private bool TryInferCustomTypeIdForValueId(
            string valueId,
            HashSet<string> visitingValueIds,
            [NotNullWhen(true)] out string? typeId)
        {
            if (!visitingValueIds.Add(valueId))
            {
                typeId = null;
                return false;
            }
            if (TryGetValue(valueId, out ObjectAttributeValue? value)
                && !string.IsNullOrEmpty(value.typeId))
            {
                typeId = value.typeId;
                return true;
            }
            if (TryInferAttributeForValueId(
                    valueId,
                    visitingValueIds,
                    out Attribute? attribute)
                && attribute is CustomAttribute customAttribute
                && !string.IsNullOrEmpty(customAttribute.customTypeId))
            {
                typeId = customAttribute.customTypeId;
                return true;
            }
            typeId = null;
            return false;
        }

        private bool TryInferDirectAttributeForValueId(
            string valueId,
            [NotNullWhen(true)] out Attribute? attribute)
        {
            foreach (var candidate in data.attributes.Values)
            {
                if (candidate.valueId == valueId)
                {
                    attribute = candidate;
                    return true;
                }
            }
            attribute = null;
            return false;
        }

        private IEnumerable<KeyValuePair<string, AttributeValue>> EnumerateAllValueRows()
        {
            foreach (var pair in sessionData.values) yield return pair;
            foreach (var pair in saveData.values) yield return pair;
            foreach (var pair in data.values) yield return pair;
        }

        private static AttributeValue CloneValueRow(AttributeValue row)
        {
            AttributeValue clone = row switch
            {
                NullAttributeValue n => new NullAttributeValue { value = n.value },
                BoolAttributeValue b => new BoolAttributeValue { value = b.value },
                NumberAttributeValue n => new NumberAttributeValue { value = n.value },
                StringAttributeValue s => new StringAttributeValue
                {
                    value = s.value,
                    neoLocalizationMode = s.neoLocalizationMode,
                },
                ArrayAttributeValue a => new ArrayAttributeValue
                {
                    value = a.value == null ? null : (string[])a.value.Clone(),
                },
                ObjectAttributeValue o => new ObjectAttributeValue
                {
                    value = o.value == null ? null : new Dictionary<string, string>(o.value),
                },
                FileAttributeValue f => new FileAttributeValue
                {
                    value = f.value == null ? null : new FileValue { fileId = f.value.fileId },
                },
                SpriteAttributeValue s => new SpriteAttributeValue
                {
                    value = s.value == null
                        ? null
                        : new SpriteValue
                        {
                            fileId = s.value.fileId,
                            sliceIndex = s.value.sliceIndex,
                        },
                },
                _ => throw new System.InvalidOperationException(
                    $"Unsupported save value row type '{row.GetType().Name}'."),
            };
            clone.id = row.id;
            clone.createdAt = row.createdAt;
            clone.updatedAt = row.updatedAt;
            clone.typeId = row.typeId;
            return clone;
        }

        /// <summary>
        /// Recursively deletes <paramref name="valueId"/> and every
        /// value referenced by its content from
        /// <see cref="ProjectSaveData.values"/>. Walks
        /// <see cref="ObjectAttributeValue"/> (Custom / Dictionary
        /// records — values are nested value-ids) and
        /// <see cref="ArrayAttributeValue"/> (List / Enum / Lookup —
        /// entries may be nested value-ids); leaves and primitives have
        /// no nested ids so the recursion bottoms out cleanly.
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
            var store = GetWritableStore(ownership);
            if (!store.values.TryGetValue(valueId, out AttributeValue val)) return;
            // Recurse first so children are pruned before we drop the
            // parent. Order doesn't strictly matter (no cycles in a
            // valid save) but doing it depth-first matches the
            // bottom-up nature of GC.
            switch (val)
            {
                case ObjectAttributeValue obj when obj.value is not null:
                    foreach (var nestedId in obj.value.Values)
                    {
                        RemoveWritableValueAndDescendants(ownership, nestedId);
                    }
                    break;
                case ArrayAttributeValue arr when arr.value is not null:
                    foreach (var nestedId in arr.value)
                    {
                        RemoveWritableValueAndDescendants(ownership, nestedId);
                    }
                    break;
            }
            if (store.values.Remove(valueId))
            {
                TouchWritableStoreUpdatedAt(ownership);
                OnWritableValueChanged?.Invoke(ownership, valueId);
                if (ownership == NeoValueOwnership.Save)
                {
                    RaiseSaveValueChanged(valueId);
                }
            }
        }

        internal void RemoveSaveValueAndDescendantsIfUnlinked(string valueId)
        {
            RemoveWritableValueAndDescendantsIfUnlinked(
                NeoValueOwnership.Save,
                valueId,
                BuildReachableSaveValueIds());
        }

        internal void RemoveWritableValueAndDescendantsIfUnlinked(
            NeoValueOwnership ownership,
            string valueId)
        {
            RemoveWritableValueAndDescendantsIfUnlinked(
                ownership,
                valueId,
                BuildReachableWritableValueIds(ownership));
        }

        private void RemoveWritableValueAndDescendantsIfUnlinked(
            NeoValueOwnership ownership,
            string valueId,
            HashSet<string> reachable)
        {
            if (reachable.Contains(valueId)) return;
            var store = GetWritableStore(ownership);
            if (!store.values.TryGetValue(valueId, out AttributeValue val)) return;

            switch (val)
            {
                case ObjectAttributeValue obj when obj.value is not null:
                    foreach (var nestedId in obj.value.Values)
                    {
                        RemoveWritableValueAndDescendantsIfUnlinked(ownership, nestedId, reachable);
                    }
                    break;
                case ArrayAttributeValue arr when arr.value is not null:
                    foreach (var nestedId in arr.value)
                    {
                        RemoveWritableValueAndDescendantsIfUnlinked(ownership, nestedId, reachable);
                    }
                    break;
            }
            if (store.values.Remove(valueId))
            {
                TouchWritableStoreUpdatedAt(ownership);
                OnWritableValueChanged?.Invoke(ownership, valueId);
                if (ownership == NeoValueOwnership.Save)
                {
                    RaiseSaveValueChanged(valueId);
                }
            }
        }


        internal bool TryGetWritableValue<TValue>(
            NeoValueOwnership ownership,
            string id,
            [NotNullWhen(true)] out TValue? value) where TValue : AttributeValue
        {
            value = null;
            if (ownership == NeoValueOwnership.Asset) return false;
            if (!GetWritableStore(ownership).values.TryGetValue(id, out AttributeValue row))
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

        internal bool TryResolveLookupCollectionValueId(
            string collectionAttributeId,
            string? collectionValueId,
            [NotNullWhen(true)] out string? valueId)
        {
            valueId = null;
            if (!TryGetAttribute(collectionAttributeId, out Attribute? collectionAttribute))
            {
                return false;
            }

            valueId = ResolveLookupCollectionValueId(collectionAttribute, collectionValueId);
            return valueId is not null;
        }

        private string? ResolveLookupCollectionValueId(
            Attribute collectionAttribute,
            string? collectionValueId)
        {
            if (collectionValueId is not null) return collectionValueId;
            // Stable-id overlay: the collection resolves by its authored id
            // (a save/session shadows that id in place), so there is no
            // override-map hop — fall through to the schema binding /
            // authored valueId.
            if (TryFindBoundValueIdForAttribute(collectionAttribute.id, out string? boundValueId))
            {
                return boundValueId;
            }
            return collectionAttribute.valueId;
        }

        private bool TryFindBoundValueIdForAttribute(
            string attributeId,
            [NotNullWhen(true)] out string? valueId)
        {
            valueId = null;
            var schemaKeys = new HashSet<string>();
            foreach (var type in data.types.Values)
            {
                foreach (var pair in type.schema)
                {
                    if (pair.Value == attributeId)
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
            IEnumerable<AttributeValue> rows,
            HashSet<string> schemaKeys,
            List<string> candidates)
        {
            foreach (var row in rows)
            {
                if (row is not ObjectAttributeValue obj || obj.value is null) continue;
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
        /// Looks up a previously-registered <see cref="NeoAttribute"/>
        /// by attribute id (and optional override-value id). Returns
        /// false when nothing is registered for the composed key.
        /// Callers typically reach for
        /// <see cref="NeoAttribute.Create"/> /
        /// <see cref="NeoAttribute.CreateWritable"/> instead — those check
        /// here automatically before constructing.
        /// </summary>
        internal bool TryGetNode(
            string attributeId,
            string? overrideValueId,
            NeoValueOwnership ownership,
            [NotNullWhen(true)] out NeoAttribute? node)
        {
            string key = MakeNodeKey(attributeId, overrideValueId, ownership);
            return nodesInternal.TryGetValue(key, out node);
        }

        internal bool TryGetNode(string attributeId, string? overrideValueId, [NotNullWhen(true)] out NeoAttribute? node)
        {
            return TryGetNode(attributeId, overrideValueId, NeoValueOwnership.Asset, out node);
        }

        /// <summary>
        /// Adds <paramref name="node"/> to the flat registry under the
        /// composed key (computed from the node's own
        /// <see cref="NeoAttribute.attribute"/>.id and
        /// <see cref="NeoAttribute.overrideValueId"/>). Called by the
        /// <see cref="NeoAttribute"/> base ctor at the end of
        /// construction; callers shouldn't need to call directly.
        /// Last-write-wins — direct <c>new NeoAttributeXyz(…)</c>
        /// construction overrides any previously-cached instance for
        /// the same key.
        /// </summary>
        internal void RegisterNode(NeoAttribute node)
        {
            string key = MakeNodeKey(node.attribute.id, node.overrideValueId, node.ownership);
            nodesInternal[key] = node;
        }

        /// <summary>
        /// Removes <paramref name="node"/> from the registry. Called
        /// by <see cref="NeoAttribute.Dispose"/>; idempotent — a key
        /// that's already absent (or that points at a different
        /// instance, e.g. a same-key replacement) is left alone.
        /// </summary>
        internal void UnregisterNode(NeoAttribute node)
        {
            string key = MakeNodeKey(node.attribute.id, node.overrideValueId, node.ownership);
            // Only remove if the registered instance is the one we're
            // unregistering — guards against the "I disposed an
            // instance that was already replaced in the registry by a
            // newer ctor call for the same key" race.
            if (nodesInternal.TryGetValue(key, out NeoAttribute existing) && existing == node)
            {
                nodesInternal.Remove(key);
            }
        }

        internal TGenerated GetOrCreateGeneratedCustomValue<TGenerated>(
            NeoAttributeCustom node,
            System.Func<TGenerated> create)
            where TGenerated : NeoGeneratedCustomValue
        {
            string key = MakeNodeKey(node.attribute.id, node.overrideValueId, node.ownership);
            if (generatedValuesInternal.TryGetValue(key, out NeoGeneratedCustomValue existing))
            {
                if (existing is TGenerated match) return match;
                existing.Dispose();
            }

            TGenerated generated = create();
            generatedValuesInternal[key] = generated;
            return generated;
        }

        internal void UnregisterGeneratedCustomValue(NeoGeneratedCustomValue generated, NeoAttributeCustom node)
        {
            string key = MakeNodeKey(node.attribute.id, node.overrideValueId, node.ownership);
            if (generatedValuesInternal.TryGetValue(key, out NeoGeneratedCustomValue existing)
                && ReferenceEquals(existing, generated))
            {
                generatedValuesInternal.Remove(key);
            }
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
            string attributeId,
            object? receiver,
            object?[] args)
        {
            if (IsNativeFunctionDeferred(attributeId))
            {
                throw new NeoDeferredFunctionRuntimeError(
                    $"Deferred Function '{attributeId}' can only be invoked from dialogue action runtime.");
            }
            if (nativeFunctionInvokers is null)
            {
                throw new NeoScript.NSGetterRuntimeError(
                    "Native Function invocation requires constructing the generated ProjectNeo client wrapper before evaluating NeoScript.");
            }
            if (!nativeFunctionInvokers.TryGetValue(attributeId, out var invoker))
            {
                throw new NeoScript.NSGetterRuntimeError(
                    $"No native Function invoker is registered for attribute '{attributeId}'.");
            }
            return invoker(this, receiver, args);
        }

        public void InvokeDeferredNativeFunction(
            string attributeId,
            object? receiver,
            object?[] args)
        {
            throw new NeoDeferredFunctionRuntimeError(
                $"Deferred Function '{attributeId}' can only be invoked from dialogue action runtime.");
        }

        internal NeoDeferredFunctionBase StartDeferredNativeFunction(
            string attributeId,
            object? receiver,
            object?[] args,
            System.Action<object?> complete,
            System.Action<System.Exception> fail)
        {
            if (!TryResolveFunctionAttribute(attributeId, out var attribute))
            {
                throw new NeoScript.NSGetterRuntimeError(
                    $"No Function attribute exists for '{attributeId}'.");
            }
            if (attribute.deferred != true)
            {
                throw new NeoScript.NSGetterRuntimeError(
                    $"Function '{attribute.name}' ({attributeId}) is not deferred.");
            }
            if (deferredNativeFunctionInvokers is null)
            {
                throw new NeoScript.NSGetterRuntimeError(
                    "Deferred native Function invocation requires constructing the generated ProjectNeo client wrapper before evaluating NeoScript.");
            }
            if (!deferredNativeFunctionInvokers.TryGetValue(attributeId, out var invoker))
            {
                throw new NeoScript.NSGetterRuntimeError(
                    $"No deferred native Function invoker is registered for attribute '{attributeId}'.");
            }

            NeoDeferredFunctionBase deferred = attribute.returnTypeInfo is VoidTypeInfo
                ? new NeoDeferredFunction(attributeId, attribute.name, complete, fail)
                : CreateTypedDeferredFunction(attribute, complete, fail);
            invoker(this, receiver, args, deferred);
            return deferred;
        }

        internal bool IsNativeFunctionDeferred(string attributeId)
        {
            return TryResolveFunctionAttribute(attributeId, out var attribute)
                && attribute.deferred == true;
        }

        private NeoDeferredFunctionBase CreateTypedDeferredFunction(
            FunctionAttribute attribute,
            System.Action<object?> complete,
            System.Action<System.Exception> fail)
        {
            return attribute.returnTypeInfo.type switch
            {
                AttributeType.Bool => new NeoDeferredFunction<bool>(attribute.id, attribute.name, complete, fail),
                AttributeType.Int => new NeoDeferredFunction<int>(attribute.id, attribute.name, complete, fail),
                AttributeType.Float => new NeoDeferredFunction<float>(attribute.id, attribute.name, complete, fail),
                AttributeType.String => new NeoDeferredFunction<string?>(attribute.id, attribute.name, complete, fail),
                _ => new NeoDeferredFunction<object?>(attribute.id, attribute.name, complete, fail),
            };
        }

        private bool TryResolveFunctionAttribute(
            string attributeId,
            [NotNullWhen(true)] out FunctionAttribute? attribute)
        {
            var visited = new HashSet<string>();
            string? currentId = attributeId;
            while (!string.IsNullOrEmpty(currentId) && visited.Add(currentId))
            {
                if (!data.attributes.TryGetValue(currentId!, out Attribute? current))
                {
                    break;
                }
                if (current is FunctionAttribute function
                    && function.returnTypeInfo is not null
                    && function.argumentTypes is not null
                    && function.deferred.HasValue)
                {
                    attribute = function;
                    return true;
                }
                currentId = current.extendsAttributeId;
            }
            attribute = null;
            return false;
        }

        private void ValidateFunctionAttributes()
        {
            foreach (var pair in data.attributes)
            {
                if (pair.Value is not FunctionAttribute function) continue;
                if (!string.IsNullOrEmpty(function.extendsAttributeId)) continue;
                if (function.returnTypeInfo is null)
                {
                    throw new System.InvalidOperationException(
                        $"Function attribute '{function.id}' is missing returnTypeInfo.");
                }
                if (function.argumentTypes is null)
                {
                    throw new System.InvalidOperationException(
                        $"Function attribute '{function.id}' is missing argumentTypes.");
                }
                if (!function.deferred.HasValue)
                {
                    throw new System.InvalidOperationException(
                        $"Function attribute '{function.id}' is missing deferred.");
                }
            }
        }

        /// <summary>
        /// Composes the registry key from an attribute id and an
        /// optional override-value id. Format mirrors the user-facing
        /// spec:
        ///   - <c>attributeId</c> when no override
        ///   - <c>$"{attributeId}_{overrideValueId}"</c> when an override is set
        /// </summary>
        internal static string MakeNodeKey(
            string attributeId,
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
                ? $"{prefix}:{attributeId}"
                : $"{prefix}:{attributeId}_{overrideValueId}";
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
            string? custom = SaveOptions.BuildSaveName?.Invoke();
            return string.IsNullOrWhiteSpace(custom) ? BuildDefaultSaveName() : custom!;
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

        private void ValidateRootCustomAttribute(string attributeId, string projectFieldName)
        {
            if (string.IsNullOrEmpty(attributeId))
            {
                throw new System.InvalidOperationException(
                    $"Project field '{projectFieldName}' is required.");
            }
            if (!data.attributes.TryGetValue(attributeId, out Attribute? attribute))
            {
                throw new System.InvalidOperationException(
                    $"Project field '{projectFieldName}' references missing attribute '{attributeId}'.");
            }
            if (attribute is not CustomAttribute)
            {
                throw new System.InvalidOperationException(
                    $"Project field '{projectFieldName}' must reference a Custom attribute, but '{attributeId}' is {attribute.GetType().Name}.");
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

        /// <summary>
        /// Applies an externally-merged save content blob into the running
        /// value graph <b>in place</b> — the inbound side of live save sessions
        /// (<c>specs/live-save-sessions.md</c>): a co-editor (e.g. the web
        /// tool) patched the session's live snapshot and the synchronizer
        /// delivered the merged content via
        /// <see cref="NeoSaveSynchronizer.OnLiveContentChanged"/>. Each changed
        /// overlay row is re-shadowed at its stable id, raising the same typed
        /// change events a local write raises (so generated subscriptions like
        /// <c>Save.OnChanged(...)</c> fire), and rows the incoming content no
        /// longer carries fall back to the authored defaults.
        /// </summary>
        public void ApplyExternalSaveContent(string content)
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
            }
            finally
            {
                suppressLiveAutoCommit = false;
            }
        }

        public Awaitable CommitAsync(bool replaceSnapshot = false) =>
            CommitCoreAsync(replaceSnapshot, warnUnlinked: true);

        private async Awaitable CommitCoreAsync(bool replaceSnapshot, bool warnUnlinked)
        {
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
            await loader.CommitSaveContentAsync(SerializeSaveData(), replaceSnapshot);
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
            // root attribute's authored value id (a write shadows that id in
            // place), then walks the overlaid graph. There is no override map.
            var reachable = new HashSet<string>();
            string rootAttributeId = ownership == NeoValueOwnership.Save
                ? data.project.rootSaveFileAttributeId
                : data.project.rootSessionAttributeId;
            if (data.attributes.TryGetValue(rootAttributeId, out Attribute rootAttribute)
                && rootAttribute.valueId is not null)
            {
                MarkReachableValue(ownership, rootAttribute.valueId, reachable);
            }
            return reachable;
        }

        private void MarkReachableValue(
            NeoValueOwnership ownership,
            string valueId,
            HashSet<string> reachable,
            Attribute? sourceAttribute = null)
        {
            if (!reachable.Add(valueId)) return;
            var store = GetWritableStore(ownership);
            if (!store.values.TryGetValue(valueId, out AttributeValue? val)
                && !data.values.TryGetValue(valueId, out val))
            {
                return;
            }
            sourceAttribute ??= TryInferAttributeForValueId(valueId, out Attribute? inferredAttribute)
                ? inferredAttribute
                : null;
            foreach (var child in EnumerateOwnedChildLinks(val, sourceAttribute))
            {
                MarkReachableValue(ownership, child.valueId, reachable, child.attribute);
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
        private void LoadSaveDataOrDefault(string? content)
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
        }
    }
}
