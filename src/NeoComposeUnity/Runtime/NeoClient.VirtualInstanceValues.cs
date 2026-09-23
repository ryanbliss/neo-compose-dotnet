// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private const string VirtualValueNamespace =
            "3e8ca0b3-e3f1-5d5f-bf2f-6ab5ee3896d0";

        /// <summary>
        /// Convergence bound for the re-entrant replay loop. Each pass is a
        /// full project replay; more than a handful means the graph is not
        /// settling rather than that the project is large.
        /// </summary>
        private const int MaxVirtualInstanceRebuildPasses = 8;
        private bool isInitializingVirtualInstanceValues;
        private bool isReplayingVirtualInstance;
        private readonly HashSet<string> replayingVirtualRootIds = new(StringComparer.Ordinal);
        private string? replayingVirtualInstanceRootId;
        private string? replayingVirtualInstanceClassId;
        private IReadOnlyDictionary<string, GenericBinding>?
            replayingVirtualInstanceClassArguments;
        private IReadOnlyDictionary<string, string>?
            replayingVirtualInstanceGenericBindings;

        /// <summary>
        /// True while a P75 sparse instance is replaying its construction.
        /// Replay completeness follows the WEB contract: the web's replay
        /// OMITS a required member it cannot construct (no declared default,
        /// no constructor coverage) and the sparse root's MATERIALIZED rows
        /// supply it in the overlay — merged completeness is what the
        /// server-side collapse verifier proved, not construction-only
        /// completeness. Constructed-graph validation consults this to relax
        /// exactly that check during replay, and nothing else.
        /// </summary>
        internal bool IsReplayingVirtualInstance => isReplayingVirtualInstance;

        internal string? ReplayingVirtualInstanceRootId =>
            replayingVirtualInstanceRootId;

        /// <summary>
        /// The closed generic context of the P75 root currently being replayed.
        /// A named variant constructs through its Initialize closure, so the
        /// constructor intrinsic is several frames below the replay call and
        /// cannot receive this placement context as an ordinary argument.
        /// </summary>
        internal bool TryGetReplayingVirtualInstanceClassContext(
            string classId,
            out IReadOnlyDictionary<string, GenericBinding>? classArguments,
            out IReadOnlyDictionary<string, string>? storedGenericBindings)
        {
            classArguments = null;
            storedGenericBindings = null;
            if (!isReplayingVirtualInstance
                || replayingVirtualInstanceClassId != classId
                || replayingVirtualInstanceClassArguments is null)
            {
                return false;
            }
            classArguments = replayingVirtualInstanceClassArguments;
            storedGenericBindings = replayingVirtualInstanceGenericBindings;
            return true;
        }

        internal bool IsReferenceOwnedByReplayingVirtualInstance(
            NeoValueOwnership ownership,
            string valueId)
        {
            return isReplayingVirtualInstance
                && TryFindOwnedParent(ownership, valueId, out string? parentValueId)
                && parentValueId == replayingVirtualInstanceRootId;
        }

        /// <summary>
        /// False until the client constructor has assigned Assets, Save, and
        /// Session. Replay evaluates constructor bodies whose context binds
        /// <c>root</c> through those members, so an expansion that runs
        /// earlier dereferences a null root: a world-grid member constructed
        /// inside the root tree loads its value partition, and the
        /// partition's eager per-row replay fired mid-construction. Until
        /// this flips, partition loads merge their rows and leave replay to
        /// the constructor's own full <see cref="InitializeVirtualInstanceValues"/>
        /// pass, which scans <c>data.values</c> and therefore covers them.
        /// </summary>
        private bool virtualInstanceReplayReady;

        private int awaitingVirtualInstanceChildDepth;
        // Constructor replay publishes a temporary Session graph before all
        // member initializers have run. Only rows allocated in this
        // scope may defer a missing computed child; a preexisting sibling
        // read during the same initializer must keep its ordinary behavior.
        private HashSet<string>? replayingVirtualInstanceAllocatedSessionValueIds;

        internal bool IsAwaitingVirtualInstanceInitializers(
            ObjectMemberValue? row) =>
            (isReplayingVirtualInstance
                && row is not null
                && sessionValues.ContainsKey(row.id)
                && replayingVirtualInstanceAllocatedSessionValueIds is not null
                && replayingVirtualInstanceAllocatedSessionValueIds.Contains(row.id))
            // Replay also reaches rows that are NOT in the temporary Session
            // graph: an implicit construction delegates its content to the
            // placement declaration's authored default, and a variant-stamped
            // root resolves its variant graph, both of which are asset rows.
            // Those await their initializers for the whole replay, not just
            // from the moment replay published a temporary graph.
            || ((!virtualInstanceReplayReady || isReplayingVirtualInstance)
                && (awaitingVirtualInstanceChildDepth > 0
                    || (row is not null && IsStoredClassDefaultRoot(row))));

        internal VirtualInstanceChildConstructionScope EnterVirtualInstanceChildConstruction(
            ObjectMemberValue? row)
        {
            bool entered = IsAwaitingVirtualInstanceInitializers(row);
            if (entered) awaitingVirtualInstanceChildDepth++;
            return new VirtualInstanceChildConstructionScope(this, entered);
        }

        internal readonly struct VirtualInstanceChildConstructionScope : IDisposable
        {
            private readonly NeoClient? client;

            internal VirtualInstanceChildConstructionScope(
                NeoClient client,
                bool entered)
            {
                this.client = entered ? client : null;
            }

            public void Dispose()
            {
                if (client is null) return;
                client.awaitingVirtualInstanceChildDepth--;
            }
        }

        /// <summary>
        /// Every row id one instance root's expansion touched — the virtual
        /// ids it minted AND the materialized ids that answered its nodes.
        /// This is the attribution a live apply needs: it turns "these rows
        /// changed" into "these roots have to be replayed", so the rest of the
        /// project keeps its index entries.
        /// </summary>
        private readonly Dictionary<string, HashSet<string>> virtualFootprintByRoot = new();
        private readonly Dictionary<string, string> virtualRootByFootprintId = new();
        private readonly Dictionary<string, HashSet<string>>
            constructorArgumentRootsByValueId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>>
            constructorArgumentValueIdsByRoot = new(StringComparer.Ordinal);
        private bool virtualInstanceValuesDirty;

        private sealed class VirtualExpansionNode
        {
            internal MemberValue row = null!;
            internal Member member = null!;
            internal string path = null!;
            internal string virtualId = null!;
            internal string? effectiveId;
            internal VirtualExpansionNode? parent;
            internal readonly Dictionary<string, VirtualExpansionNode> classChildren = new();
            internal readonly List<VirtualExpansionNode> listChildren = new();
            internal readonly Dictionary<string, VirtualExpansionNode> dictionaryChildren = new();
        }

        private sealed class VirtualClassPlacement
        {
            internal string rootId = null!;
            internal string parentValueId = null!;
            internal Member member = null!;
            internal NeoValueOwnership ownership;
        }

        /// <summary>
        /// P75 root eligibility. A row is a sparse instance root when it
        /// carries an arm of the creation-provenance stamp: an explicit
        /// <c>instanceConstructorId</c> (present even when its value is
        /// null &#8212; the implicit <c>new()</c>) or a selected variant.
        /// <c>constructorArgs</c> is not an arm: every writer stamps the
        /// constructor key beside the arguments, and the web restores
        /// historical bodies through the same convergence, so a row carrying
        /// arguments alone names no construction. This must answer exactly as
        /// the web's <c>isVirtualInstanceRootShape</c> does, or the same
        /// persisted row expands in the editor and renders unresolved in game.
        /// </summary>
        internal static bool IsVirtualInstanceRoot(MemberValue row)
        {
            return row.hasInstanceConstructorId
                || row.instanceVariantId is not null;
        }

        /// <summary>
        /// Writes the canonical P75 creation-provenance PAIR onto a row the
        /// runtime just constructed, so it is durably a sparse instance root
        /// and replays through <see cref="ExpandVirtualInstanceRoot"/> on
        /// every later load. The implicit <c>new()</c> stamps an explicit
        /// null constructor id (which serializes) alongside empty arguments.
        /// </summary>
        internal static void StampConstructionProvenance(
            ObjectMemberValue root,
            string? constructorId,
            Dictionary<string, JToken?> constructorArgs)
        {
            root.instanceConstructorId = constructorId;
            root.constructorArgs = constructorArgs;
        }

        /// <summary>
        /// Serializes one evaluated constructor argument into the creation
        /// data <see cref="MemberValue.constructorArgs"/> stores: literals
        /// stay literals, a constructed argument becomes the id of its
        /// materialized row, and structured runtime values keep their JSON
        /// shape. This is the exact inverse of
        /// <see cref="VirtualReplayArgument"/>.
        ///
        /// <para><paramref name="resolveRowId"/> supplies the row id for the
        /// argument kinds replay reads back as an id — Class, Interface, List
        /// and Dictionary. A row-backed argument does not always arrive as a
        /// generated wrapper: once it has passed through the evaluator it is
        /// the plain record or array shape, which carries no id of its own.
        /// Serializing that shape would record the row's <i>contents</i> as
        /// the recipe, and replay would then rebuild the instance from a
        /// payload map instead of the row it was actually built from.</para>
        /// </summary>
        internal static JToken? ConstructorArgumentToken(
            object? value,
            string describeArgument,
            Func<object?, string?>? resolveRowId = null)
        {
            switch (value)
            {
                case null:
                    return JValue.CreateNull();
                case JToken token:
                    return token.DeepClone();
                case string text:
                    return new JValue(text);
                case bool flag:
                    return new JValue(flag);
                case NeoMember member:
                    return member.value is null
                        ? JValue.CreateNull()
                        : new JValue(member.value.id);
                case NeoGeneratedClassValue generated:
                    return generated.valueId is null
                        ? JValue.CreateNull()
                        : new JValue(generated.valueId);
                case sbyte or byte or short or ushort or int or uint or long:
                    return new JValue(Convert.ToInt64(value));
                case float or double or decimal:
                    return new JValue(Convert.ToDouble(value));
            }
            if (resolveRowId?.Invoke(value) is string rowId
                && !string.IsNullOrEmpty(rowId))
            {
                return new JValue(rowId);
            }
            try
            {
                return JToken.FromObject(value);
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    $"Constructor argument {describeArgument} of runtime type '{value.GetType().FullName}' cannot be recorded as P75 creation data.",
                    error);
            }
        }

        internal bool TryGetVirtualClassChildValueId(
            string parentValueId,
            string schemaKey,
            out string? childValueId)
        {
            childValueId = null;
            // Constructor bodies can read sparse shared catalog values that are
            // not constructor arguments. Resolve only the value actually read;
            // ordinary post-construction reads never trigger replay here.
            if (isReplayingVirtualInstance && !TryResolveVirtualClassChildren(parentValueId, out _))
                EnsureVirtualReplayArgumentReady(parentValueId);
            return TryResolveVirtualClassChildren(
                    parentValueId,
                    out Dictionary<string, string>? children)
                && children.TryGetValue(schemaKey, out childValueId);
        }

        /// <summary>
        /// P75: replays sparse instance roots against the current declaration
        /// defaults, then indexes their omitted rows under deterministic ids.
        /// The replay graph is temporary Session data; only immutable read
        /// rows and parent/schema-key links survive this method.
        /// </summary>
        private void InitializeVirtualInstanceValues(bool failClosed = true)
        {
            foreach (var step in InitializeVirtualInstanceValuesSteps(failClosed)) { }
        }

        private IEnumerable<byte> InitializeVirtualInstanceValuesSteps(bool failClosed)
        {
            if (isInitializingVirtualInstanceValues)
            {
                virtualInstanceValuesDirty = true;
                yield break;
            }
            isInitializingVirtualInstanceValues = true;
            try
            {
                int pass = 0;
                do
                {
                    // A replay can publish rows that make another root's
                    // expansion stale, which re-enters here and asks for one
                    // more pass. That must converge: an expansion whose own
                    // output keeps re-dirtying the index would otherwise spin
                    // forever with no diagnostic.
                    if (++pass > MaxVirtualInstanceRebuildPasses)
                    {
                        throw new InvalidOperationException(
                            $"P75 virtual instance replay did not converge after {MaxVirtualInstanceRebuildPasses} passes. A constructor or variant Initialize is writing rows that invalidate the instance index on every pass.");
                    }
                    virtualInstanceValuesDirty = false;
                    foreach (var step in InitializeVirtualInstanceValuesCore(failClosed)) yield return step;
                }
                while (virtualInstanceValuesDirty);
            }
            finally
            {
                isInitializingVirtualInstanceValues = false;
            }
        }

        private IEnumerable<byte> InitializeVirtualInstanceValuesCore(bool failClosed)
        {
            // Wrapper nodes retain the row object they were built from, and a
            // full rebuild mints new rows at the SAME deterministic ids. The
            // per-root dispose guard in ExpandVirtualInstanceRoot reads the
            // index that is about to be cleared, so it cannot see them:
            // snapshot the outgoing ids here and release their wrappers once
            // the new index is in place, or a held wrapper serves the old
            // expansion forever.
            var outgoingVirtualIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (HashSet<string> ids in virtualValueIdsByRoot.Values)
                outgoingVirtualIds.UnionWith(ids);
            sharedEvaluationContext = null;
            virtualValues.Clear();
            virtualValueOwnership.Clear();
            virtualClassChildren.Clear();
            virtualClassPlacementByChildId.Clear();
            virtualEntriesByContainer.Clear();
            virtualContainerByRow.Clear();
            virtualValueIdsByRoot.Clear();
            virtualClassParentIdsByRoot.Clear();
            virtualClassChildIdsByRoot.Clear();
            virtualFootprintByRoot.Clear();
            virtualRootByFootprintId.Clear();
            nestedReplayBoundaries.Clear();
            nestedReplayRootsByOwner.Clear();
            constructorArgumentRootsByValueId.Clear();
            replayFieldsByValueId.Clear();
            constructorArgumentValueIdsByRoot.Clear();
            MemberValue[] allRows = data.values.Values
                .Concat(saveData.values.Values)
                .Concat(sessionData.values.Values)
                .GroupBy(row => row.id, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            var parentByValueId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var step in BuildParentByValueIdSteps(allRows, parentByValueId)) yield return step;
            ObjectMemberValue[] roots = allRows
                .OfType<ObjectMemberValue>()
                .Where(row => row.classId is not null)
                .Where(IsStoredClassDefaultRoot)
                // A containing replay may index the nested root using its
                // outer-root virtual scope. Replaying shallow-to-deep lets the
                // nested root's own durable recipe overwrite that provisional
                // mapping with the authoritative nested-root scope.
                .OrderBy(row => VariantGraphs.ContainsKey(row.id) ? 0 : 1)
                .ThenBy(row => AuthoredContainmentDepth(row.id, parentByValueId))
                .ThenBy(row => row.id, StringComparer.Ordinal)
                .ToArray();

            var pendingRoots = roots.ToDictionary(
                root => root.id,
                StringComparer.Ordinal);
            var readyRoots = new Queue<ObjectMemberValue>();
            foreach (ObjectMemberValue root in roots)
            {
                yield return 0;
                if (!CanReplayVirtualInstanceRoot(root)) continue;
                pendingRoots.Remove(root.id);
                readyRoots.Enqueue(root);
            }
            while (readyRoots.Count > 0)
            {
                yield return 0;
                ObjectMemberValue root = readyRoots.Dequeue();
                if (!virtualValueIdsByRoot.ContainsKey(root.id))
                    ExpandVirtualInstanceRootOrReport(root, failClosed);
                if (!virtualClassChildIdsByRoot.TryGetValue(
                        root.id,
                        out HashSet<string>? classChildIds))
                {
                    continue;
                }
                foreach (string classChildId in classChildIds)
                {
                    if (!pendingRoots.Remove(
                            classChildId,
                            out ObjectMemberValue? nestedRoot))
                    {
                        continue;
                    }
                    readyRoots.Enqueue(nestedRoot);
                }
            }
            // Recovery already removed illegal read-only Class keys, but its
            // stale value rows cannot be judged until sparse writable paths
            // have entered the virtual index. Delete only after every root
            // reachable through those paths has replayed.
            RemoveRecoveredReadOnlySaveValues();
            // Unreferenced authored rows are not part of a runtime graph.
            // Exports can retain detached historical children; do not execute
            // their constructors. Persisted save roots still fail closed.
            foreach (ObjectMemberValue root in pendingRoots.Values)
            {
                yield return 0;
                if (saveData.values.TryGetValue(root.id, out MemberValue? saved)
                    && saved is ObjectMemberValue currentRoot
                    && IsVirtualInstanceRoot(currentRoot))
                {
                    ExpandVirtualInstanceRootOrReport(currentRoot, failClosed);
                }
            }

            DisposeWrappersTouchingRows(outgoingVirtualIds);

            // The three roots were created before replay so constructor and
            // variant code could resolve Assets/Save/Session. Rebind their
            // wrapper trees once the virtual child index is complete.
            RefreshAllVirtualWrapperTrees();
        }

        private bool CanReplayVirtualInstanceRoot(ObjectMemberValue root)
        {
            return sessionData.values.ContainsKey(root.id)
                || (TryInferMemberForValueId(root.id, out Member? member)
                    && member is ClassMember);
        }

        private static bool IsStoredClassDefaultRoot(ObjectMemberValue root) =>
            IsVirtualInstanceRoot(root) || (root.classId is not null && root.constructorArgs is null && root.value is not null);

        /// <summary>
        /// Expands one root, scoping any failure to that root. A malformed
        /// root must not gut the whole index: the live path keeps every other
        /// root's virtual values and surfaces the failure, mirroring how a
        /// partition load replays only its own placement roots.
        /// </summary>
        private void ExpandVirtualInstanceRootOrReport(
            ObjectMemberValue root,
            bool failClosed)
        {
            try
            {
                // A materialized spine is overlaid by the root that owns it.
                if (IsVirtualInstanceRoot(root)
                    && virtualRootByFootprintId.TryGetValue(root.id, out string? spineOwner)
                    && spineOwner != root.id) return;
                if (!IsVirtualInstanceRoot(root))
                {
                    if (!virtualFootprintByRoot.ContainsKey(root.id)
                        && ResolveStoredInstanceSchema(root.classId!).All(entry => root.value!.ContainsKey(entry.schemaKey))) return;
                    if ((virtualClassChildren.ContainsKey(root.id)
                            && virtualRootByFootprintId.TryGetValue(root.id, out string? owner) && owner != root.id)
                        || !TryInferMemberForValueId(root.id, out Member? inferred)
                        || inferred is not ClassMember placement
                        || placement.Payload == NeoMemberPayloadKind.Partial) return;
                }
                if (!sessionData.values.ContainsKey(root.id))
                    AssertPersistedVirtualInstanceRootIsClassPlacement(root);
                ExpandVirtualInstanceRoot(root);
            }
            catch (Exception error)
            {
                ClearVirtualInstanceRoot(root.id);
                if (failClosed) throw;
                Debug.LogWarning(
                    $"[NeoCompose] P75 could not replay instance root '{root.id}' of class '{root.classId}' from the incoming live content; its virtual values are unavailable until the next successful apply. {error}");
            }
        }

        private void IndexConstructorArgumentRows(ObjectMemberValue root)
        {
            RemoveConstructorArgumentRows(root.id);
            if (root.constructorArgs is null) return;
            foreach (JToken? argument in root.constructorArgs.Values)
                if (argument?.Type == JTokenType.String && argument.Value<string>() is string id)
                    TrackReplayDependency(root.id, ResolveValueRow(id) is ObjectMemberValue { classId: not null }
                        ? "identity:" + id : id);
        }

        private void TrackReplayDependency(string rootId, string valueId)
        {
            string indexKey = valueId;
            if (valueId.StartsWith("field:", StringComparison.Ordinal))
            {
                string parent = valueId.Substring(6, valueId.IndexOf('\n') - 6);
                if (!replayFieldsByValueId.TryGetValue(parent, out var fields)) replayFieldsByValueId[parent] = fields = new();
                fields.Add(valueId);
            }
            if (!constructorArgumentRootsByValueId.TryGetValue(indexKey, out var roots))
                constructorArgumentRootsByValueId[indexKey] = roots = new HashSet<string>(StringComparer.Ordinal);
            roots.Add(rootId);
            if (!constructorArgumentValueIdsByRoot.TryGetValue(rootId, out var values))
                constructorArgumentValueIdsByRoot[rootId] = values = new HashSet<string>(StringComparer.Ordinal);
            values.Add(valueId);
        }

        private void RemoveConstructorArgumentRows(string rootId)
        {
            if (!constructorArgumentValueIdsByRoot.Remove(
                    rootId,
                    out HashSet<string>? valueIds))
            {
                return;
            }
            foreach (string valueId in valueIds)
            {
                if (!constructorArgumentRootsByValueId.TryGetValue(
                        valueId,
                        out HashSet<string>? roots))
                {
                    continue;
                }
                roots.Remove(rootId);
                if (roots.Count == 0)
                {
                    constructorArgumentRootsByValueId.Remove(valueId);
                    if (valueId.StartsWith("field:", StringComparison.Ordinal))
                    {
                        string parent = valueId.Substring(6, valueId.IndexOf('\n') - 6);
                        if (replayFieldsByValueId.TryGetValue(parent, out var fields) && fields.Remove(valueId) && fields.Count == 0)
                            replayFieldsByValueId.Remove(parent);
                    }
                }
            }
        }

        private void RefreshAllVirtualWrapperTrees()
        {
            RefreshVirtualWrapperTree(assets);
            RefreshVirtualWrapperTree(save);
            RefreshVirtualWrapperTree(session);
        }

        private void AssertPersistedVirtualInstanceRootIsClassPlacement(
            ObjectMemberValue root)
        {
            if (!TryInferMemberForValueId(root.id, out Member? member)
                || member is not ClassMember classMember)
            {
                throw new InvalidOperationException(
                    $"P75 sparse instance root '{root.id}' is not reachable through a Class member placement.");
            }
            if (string.IsNullOrEmpty(root.classId)
                || string.IsNullOrEmpty(classMember.classId))
            {
                throw new InvalidOperationException(
                    $"P75 sparse instance root '{root.id}' has no resolvable Class type.");
            }
            if (!data.classes.ContainsKey(root.classId))
            {
                throw new InvalidOperationException(
                    $"P75 sparse instance root '{root.id}' references missing class '{root.classId}'.");
            }
        }

        /// <summary>
        /// P75 variant swaps persist one root-level provenance delta. The
        /// imperative Apply closure has already run; declarative variant halves
        /// stay virtual and are replayed here after their answered pins clear.
        /// </summary>
        internal void StampVirtualInstanceVariant(
            NeoMemberClassWritable node,
            NeoValueOwnership ownership,
            string? variantId,
            string? rowValueId,
            bool replay = true)
        {
            string valueId = node.value?.id
                ?? throw new InvalidOperationException(
                    "ToVariant receiver has no backing value row.");
            if (TryGetOverlaidValue(
                    ownership,
                    valueId,
                    out ObjectMemberValue? current)
                && current.instanceVariantId == variantId
                && current.instanceVariantRowValueId == rowValueId)
            {
                return;
            }
            if (ownership == NeoValueOwnership.Asset)
            {
                throw new InvalidOperationException(
                    "ToVariant cannot change an immutable asset instance.");
            }
            if (!TryGetOverlaidValue(
                    ownership,
                    valueId,
                    out ObjectMemberValue? source))
            {
                throw new InvalidOperationException(
                    $"ToVariant could not shadow instance root '{valueId}'.");
            }
            var root = (ObjectMemberValue)CloneRowForWrite(source);
            root.instanceVariantId = variantId;
            root.instanceVariantRowValueId = rowValueId;
            // `instanceVariantId` is the row's ONLY eligibility marker while
            // a variant is selected, and it serializes with
            // NullValueHandling.Ignore &#8212; clearing to Base would therefore
            // erase every trace that this row is a P75 root and strand its
            // whole virtual layer. Re-establish (or preserve) the constructor
            // pair so the row keeps expanding through its own construction.
            StampConstructionProvenance(
                root,
                root.hasInstanceConstructorId ? root.instanceConstructorId : null,
                root.constructorArgs ?? new Dictionary<string, JToken?>());
            SetWritableValue(ownership, root, "instanceVariantId");
            if (replay)
            {
                if (IsPreparingVariant && !isReplayingVirtualInstance) RefreshPreparedVariant(node, root);
                else RefreshVirtualWrapperTree(node);
            }
        }

        /// <summary>
        /// P75 acceptance criterion 3 — records the variant a runtime
        /// <c>Variants.X.Initialize()</c> was built from on the row it
        /// produced, then expands it so members the construction left at
        /// their declared values resolve through the virtual layer instead of
        /// being frozen at construction time.
        ///
        /// <para>The Base selection needs nothing here: its construction
        /// already stamped the constructor pair, which is the whole recipe.
        /// </para>
        /// </summary>
        internal void StampConstructedVariantInstance(
            NeoMemberClassWritable node,
            VariantRecord? record,
            string? lookupRowValueId)
        {
            if (record is null) return;
            // A replay constructs a throwaway graph by calling straight back
            // into this path. Expanding it there would recurse without bound,
            // and its rows are discarded anyway.
            if (isReplayingVirtualInstance) return;
            ObjectMemberValue source = node.value
                ?? throw new InvalidOperationException(
                    $"Variant '{record.id}' produced a node with no backing value row.");
            var root = (ObjectMemberValue)CloneRowForWrite(source);
            root.instanceVariantId = record.id;
            root.instanceVariantRowValueId = lookupRowValueId;
            SetWritableValue(node.ownership, root, "instanceVariantId");
        }

        internal void RefreshVirtualInstanceVariant(
            NeoMemberClassWritable node,
            NeoValueOwnership ownership)
        {
            string valueId = node.value?.id
                ?? throw new InvalidOperationException(
                    "ToVariant receiver has no backing value row.");
            if (!TryGetOverlaidValue(
                    ownership,
                    valueId,
                    out ObjectMemberValue? root))
            {
                throw new InvalidOperationException(
                    $"ToVariant could not refresh instance root '{valueId}'.");
            }
            if (IsPreparingVariant && !isReplayingVirtualInstance) RefreshPreparedVariant(node, root);
            else
            {
                ExpandVirtualInstanceRoot(root);
                RefreshVirtualWrapperTree(node);
            }
        }

        /// <summary>
        /// Child value id -> the id of the row that owns it, over whichever
        /// corpus is supplied: class/dictionary bodies, constructor-settled
        /// aggregate arguments, ordered-list bodies, and unordered-list
        /// containment stamps.
        /// </summary>
        private Dictionary<string, string> BuildParentByValueId(
            IEnumerable<MemberValue> rows)
        {
            var parentByValueId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var step in BuildParentByValueIdSteps(rows, parentByValueId)) { }
            return parentByValueId;
        }

        private IEnumerable<byte> BuildParentByValueIdSteps(
            IEnumerable<MemberValue> rows, Dictionary<string, string> parentByValueId)
        {
            MemberValue[] snapshot = rows.ToArray();
            var arrayById = new Dictionary<string, ArrayMemberValue>(StringComparer.Ordinal);
            foreach (ArrayMemberValue array in snapshot.OfType<ArrayMemberValue>())
                arrayById.TryAdd(array.id, array);
            var lookupByConflictingArrayId =
                new Dictionary<string, bool?>(StringComparer.Ordinal);
            // Object and explicit container edges identify the owning graph
            // without schema inference. Establish those first so an array only
            // needs its member type when one of its entries already has a
            // stronger parent. That collision is the only place List ownership
            // and Lookup reference semantics differ for replay ordering.
            foreach (MemberValue row in snapshot)
            {
                yield return 0;
                if (row is ObjectMemberValue objectRow)
                {
                    if (objectRow.value is not null) foreach (string childId in objectRow.value.Values)
                        if (childId is not null)
                            parentByValueId.TryAdd(childId, row.id);

                    if (!string.IsNullOrEmpty(objectRow.classId)
                        && !string.IsNullOrEmpty(objectRow.instanceConstructorId)
                        && objectRow.constructorArgs != null)
                    {
                        foreach (var link in
                            EnumerateConstructorSettledAggregateLinks(objectRow))
                        {
                            parentByValueId.TryAdd(link.valueId, row.id);
                        }
                    }
                }
                if (row.containerId is not null)
                    parentByValueId.TryAdd(row.id, row.containerId);
            }
            foreach (ArrayMemberValue arrayRow in snapshot.OfType<ArrayMemberValue>())
            {
                yield return 0;
                if (arrayRow.value is null) continue;
                foreach (string childId in arrayRow.value)
                {
                    if (childId is null
                        || lookupByConflictingArrayId.TryGetValue(
                            arrayRow.id,
                            out bool? knownCurrent)
                        && knownCurrent == true)
                    {
                        continue;
                    }
                    if (parentByValueId.TryAdd(childId, arrayRow.id)) continue;
                    if (!parentByValueId.TryGetValue(
                            childId,
                            out string? priorParentId)
                        || priorParentId == arrayRow.id)
                    {
                        continue;
                    }
                    bool? currentLookup = IsLookup(arrayRow);
                    bool? priorLookup = arrayById.TryGetValue(
                            priorParentId,
                            out ArrayMemberValue? priorArray)
                        ? IsLookup(priorArray)
                        : false;
                    if (priorLookup == true) RemoveTentativeEdges(priorArray!);
                    if (currentLookup == true) RemoveTentativeEdges(arrayRow);
                    if (priorLookup == true && currentLookup == false)
                        parentByValueId[childId] = arrayRow.id;
                }
            }

            bool? IsLookup(ArrayMemberValue array)
            {
                if (lookupByConflictingArrayId.TryGetValue(
                        array.id,
                        out bool? cached))
                    return cached;
                bool? result = TryInferMemberForValueId(
                        array.id,
                        out Member? member)
                    ? member is LookupMember
                    : null;
                lookupByConflictingArrayId.Add(array.id, result);
                return result;
            }

            void RemoveTentativeEdges(ArrayMemberValue array)
            {
                foreach (string valueId in array.value ?? Array.Empty<string>())
                {
                    if (valueId is not null
                        && parentByValueId.TryGetValue(
                            valueId,
                            out string? parentId)
                        && parentId == array.id)
                    {
                        parentByValueId.Remove(valueId);
                    }
                }
            }
        }

        private static int AuthoredContainmentDepth(
            string valueId,
            IReadOnlyDictionary<string, string> parentByValueId)
        {
            int depth = 0;
            string cursor = valueId;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (seen.Add(cursor)
                && parentByValueId.TryGetValue(cursor, out string? parentId))
            {
                depth++;
                cursor = parentId;
            }
            return depth;
        }

        private void InitializeVirtualInstanceValuesForLoadedRows(
            IEnumerable<MemberValue> loadedRows)
        {
            MemberValue[] rows = loadedRows.ToArray();
            // Only this partition's roots are replayed, but their DEPTH has to
            // be measured against the whole corpus: a partition root nested
            // under a main-map parent has no parent inside the partition and
            // would sort to depth 0, replaying before the outer root whose
            // scope must lose to it.
            Dictionary<string, string> parentByValueId = BuildParentByValueId(
                data.values.Values
                    .Concat(saveData.values.Values)
                    .Concat(sessionData.values.Values));

            foreach (ObjectMemberValue root in rows
                .OfType<ObjectMemberValue>()
                .Where(row => row.classId is not null)
                .Where(IsStoredClassDefaultRoot)
                .OrderBy(row => VariantGraphs.ContainsKey(row.id) ? 0 : 1)
                .ThenBy(row => AuthoredContainmentDepth(row.id, parentByValueId))
                .ThenBy(row => row.id, StringComparer.Ordinal))
            {
                if (CanReplayVirtualInstanceRoot(root))
                    ExpandVirtualInstanceRootOrReport(root, failClosed: true);
            }
        }

        private IReadOnlyCollection<string> ClearVirtualInstanceValuesForAuthoredRows(
            IEnumerable<string> authoredRowIds)
        {
            var removedVirtualIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string rootId in authoredRowIds)
            {
                if (!data.values.TryGetValue(rootId, out MemberValue? row)
                    || row is not ObjectMemberValue root
                    || !IsStoredClassDefaultRoot(root))
                {
                    continue;
                }
                if (virtualValueIdsByRoot.TryGetValue(
                        rootId,
                        out HashSet<string>? valueIds))
                {
                    removedVirtualIds.UnionWith(valueIds);
                }
                ClearVirtualInstanceRoot(rootId);
            }
            return removedVirtualIds;
        }

        private static void RefreshVirtualWrapperTree(NeoMember node)
        {
            switch (node)
            {
                case NeoMemberClass classNode:
                    classNode.RefreshChildrenAfterConstruction();
                    foreach (NeoMember child in classNode.Select(pair => pair.Value).ToArray())
                        RefreshVirtualWrapperTree(child);
                    break;
                case NeoMemberList listNode:
                    foreach (NeoMember child in listNode.ToArray())
                        RefreshVirtualWrapperTree(child);
                    break;
                case NeoMemberDictionary dictionaryNode:
                    foreach (NeoMember child in dictionaryNode.Select(pair => pair.Value).ToArray())
                        RefreshVirtualWrapperTree(child);
                    break;
            }
        }

        private void ExpandVirtualInstanceRoot(ObjectMemberValue instanceRoot)
        {
            if (!replayingVirtualRootIds.Add(instanceRoot.id))
                throw new InvalidOperationException($"Sparse constructor dependency cycle at '{instanceRoot.id}'.");
            try { ExpandVirtualInstanceRootCore(instanceRoot); }
            finally { replayingVirtualRootIds.Remove(instanceRoot.id); }
        }

        private void EnsureVirtualReplayArgumentReady(string valueId)
        {
            if (!virtualInstanceReplayReady || !isReplayingVirtualInstance
                || valueId == replayingVirtualInstanceRootId) return;
            if (candidateReplay is not null)
            {
                if (candidateReplay.AffectedRoots.Contains(valueId)) PrepareCandidateRoot(valueId);
                return;
            }
            if ((data.values.ContainsKey(valueId) || saveData.values.ContainsKey(valueId))
                && ResolveValueRow(valueId) is ObjectMemberValue root
                && IsStoredClassDefaultRoot(root)
                && !virtualValueIdsByRoot.ContainsKey(root.id)
                && CanReplayVirtualInstanceRoot(root))
                ExpandVirtualInstanceRootOrReport(root, failClosed: true);
        }

        private static readonly Unity.Profiling.ProfilerMarker ReplayRootMarker = new("NeoCompose.Replay.Root");
        private static readonly Unity.Profiling.ProfilerMarker ReplayConstructMarker = new("NeoCompose.Replay.Construct");
        private static readonly Unity.Profiling.ProfilerMarker ReplayIndexMarker = new("NeoCompose.Replay.Index");
        private static readonly Unity.Profiling.ProfilerMarker ReplayOverlayMarker = new("NeoCompose.Replay.Overlay");
        private static readonly Unity.Profiling.ProfilerMarker ReplayCleanupMarker = new("NeoCompose.Replay.Cleanup");

        private ReplayAllocationScope? replayAllocationScope;

        private void RecordReplayAllocation(string id)
        {
            for (var scope = replayAllocationScope; scope is not null; scope = scope.Parent)
                scope.Ids.Add(id);
        }

        // Temporary rows belong to a replay, not to the whole Session store.
        // Nested scopes include their allocations in the enclosing scope so an
        // initializer failure also reclaims rows created before the failure.
        private sealed class ReplayAllocationScope : IDisposable
        {
            private readonly NeoClient client;
            internal readonly ReplayAllocationScope? Parent;
            internal readonly HashSet<string> Ids = new(StringComparer.Ordinal);
            internal readonly List<NeoMember> Nodes = new();

            internal ReplayAllocationScope(NeoClient client)
            {
                this.client = client;
                Parent = client.replayAllocationScope;
                client.replayAllocationScope = this;
            }

            public void Dispose()
            {
                using var marker = ReplayCleanupMarker.Auto();
                client.replayAllocationScope = Parent;
                // Retire wrappers before removing rows so no listener can read
                // a partially reclaimed constructor graph.
                foreach (NeoMember node in Nodes)
                    if (!node.isDisposed && (node.overrideValueId ?? node.value?.id) is string id && Ids.Contains(id))
                        node.Dispose();
                if (client.candidateReplay is not null)
                {
                    foreach (string id in Ids) client.candidateReplay.SetAllocation(id, null);
                    return;
                }
                foreach (string id in Ids)
                {
                    client.EvictSharedEvaluationRow(id);
                    if (client.sessionData.values.ContainsKey(id))
                        client.RemoveTemporaryWritableValueGraph(NeoValueOwnership.Session, id);
                }
            }
        }

        private PreparedVirtualExpansion ExpandVirtualInstanceRootCore(ObjectMemberValue instanceRoot, bool prepareOnly = false, NeoValueOwnership? replayOwnership = null)
        {
            using var marker = ReplayRootMarker.Auto();
            using var nestedReplay = BeginNestedReplay();
            nestedReplayBoundaries.TryGetValue(instanceRoot.id, out var replayBoundary);
            if (replayBoundary is not null
                && !NeoSemanticJson.MemberRowsEqual(replayBoundary.Root, instanceRoot, ignoreObjectFields: true))
            {
                // A new recipe owns its initializers. Retain the enclosing id
                // namespace, but never reapply the prior call-site fields.
                replayBoundary = new NestedReplayBoundary
                {
                    Root = instanceRoot, NamespaceRoot = replayBoundary.NamespaceRoot,
                    Path = replayBoundary.Path, Ownership = replayOwnership ?? replayBoundary.Ownership,
                };
            }
            var dependencyIds = new HashSet<string>();
            var releasedVirtualIds = new HashSet<string>(StringComparer.Ordinal);
            if (!prepareOnly && virtualValueIdsByRoot.TryGetValue(
                    instanceRoot.id,
                    out HashSet<string>? priorVirtualIds))
            {
                // Wrapper nodes retain the row object they were built from.
                // A variant swap reuses stable virtual ids with new effective
                // values, so release the prior wrappers before replacing the
                // index or they keep serving the old variant indefinitely.
                releasedVirtualIds.UnionWith(priorVirtualIds);
                DisposeWrappersTouchingRows(priorVirtualIds);
            }
            if (!prepareOnly) ClearVirtualInstanceRoot(instanceRoot.id);
            NeoValueOwnership ownership = replayOwnership ?? replayBoundary?.Ownership ?? (TryGetValueOwnership(
                instanceRoot.id,
                out NeoValueOwnership resolvedOwnership)
                    ? resolvedOwnership
                    : ResolveAuthoredOwnership(instanceRoot.id, instanceRoot));
            using var allocations = new ReplayAllocationScope(this);
            ClassMember? placementMember = null;
            if (TryInferMemberForValueId(
                    instanceRoot.id,
                    out Member? inferredPlacement)
                && inferredPlacement is ClassMember classPlacement)
            {
                placementMember = classPlacement;
            }
            IReadOnlyDictionary<string, GenericBinding>? replayClassArguments =
                NeoGenericResolution.CloseClassArgumentsFromStamp(
                    instanceRoot.genericBindings,
                    placementMember?.classArguments);
            NeoGeneratedTypesSupport.RuntimeConstructedClassValue constructed;
            bool wasReplaying = isReplayingVirtualInstance;
            string? previousReplayingRootId = replayingVirtualInstanceRootId;
            string? previousReplayingClassId = replayingVirtualInstanceClassId;
            IReadOnlyDictionary<string, GenericBinding>?
                previousReplayingClassArguments =
                    replayingVirtualInstanceClassArguments;
            IReadOnlyDictionary<string, string>?
                previousReplayingGenericBindings =
                    replayingVirtualInstanceGenericBindings;
            HashSet<string>? previousAllocatedSessionValueIds =
                replayingVirtualInstanceAllocatedSessionValueIds;
            isReplayingVirtualInstance = true;
            replayingVirtualInstanceRootId = instanceRoot.id;
            replayingVirtualInstanceClassId = instanceRoot.classId;
            replayingVirtualInstanceClassArguments = replayClassArguments;
            replayingVirtualInstanceGenericBindings = instanceRoot.genericBindings;
            // Nested replay starts after the outer constructor published its
            // temporary Session graph. Keep the outer boundary so those rows
            // still await their remaining member initializers.
            if (!wasReplaying)
                replayingVirtualInstanceAllocatedSessionValueIds = allocations.Ids;
            var captureReads = CaptureValueReads(dependencyIds);
            try
            {
                using var constructMarker = ReplayConstructMarker.Auto();
                if (!IsVirtualInstanceRoot(instanceRoot))
                {
                    var declaration = (ClassMember)placementMember!.ShallowClone();
                    declaration.classId = instanceRoot.classId!;
                    if (!DerivesContentFromPlacementDefault(instanceRoot, declaration))
                        declaration.defaultValue = new ObjectMemberValueBase { value = new Dictionary<string, string>() };
                    constructed = NeoGeneratedTypesSupport.MaterializeStoredClassMemberDefault(
                        this, declaration, instanceRoot, declarationOnly: true);
                }
                else
                {
                    NeoMemberClassWritable replayed = ReplayVirtualInstance(instanceRoot, placementMember, replayClassArguments);
                    constructed = new NeoGeneratedTypesSupport.RuntimeConstructedClassValue(replayed.value!, replayed.member);
                }
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    $"P75 could not replay sparse instance '{instanceRoot.id}' of class '{instanceRoot.classId}'. {error.Message}",
                    error);
            }
            finally
            {
                captureReads.Dispose();
                isReplayingVirtualInstance = wasReplaying;
                replayingVirtualInstanceRootId = previousReplayingRootId;
                replayingVirtualInstanceClassId = previousReplayingClassId;
                replayingVirtualInstanceClassArguments =
                    previousReplayingClassArguments;
                replayingVirtualInstanceGenericBindings =
                    previousReplayingGenericBindings;
                replayingVirtualInstanceAllocatedSessionValueIds =
                    previousAllocatedSessionValueIds;
            }

            string temporaryRootId = constructed.value?.id
                ?? throw new InvalidOperationException(
                    $"P75 replay for '{instanceRoot.id}' produced no root row.");
            if (!sessionValues.TryGetValue(
                    temporaryRootId,
                    out MemberValue? expandedRoot))
            {
                throw new InvalidOperationException(
                    $"P75 replay for '{instanceRoot.id}' lost temporary root '{temporaryRootId}'.");
            }

            var claimedVirtualIds = new Dictionary<string, string>(StringComparer.Ordinal);
            VirtualExpansionNode graph;
            using (ReplayIndexMarker.Auto()) graph = IndexVirtualExpansion(
                replayBoundary?.NamespaceRoot ?? instanceRoot,
                expandedRoot,
                constructed.member,
                replayBoundary?.Path ?? "$",
                claimedVirtualIds,
                new Dictionary<MemberValue, IReadOnlyDictionary<string, NeoGenericEnvEntry>>());
            if (replayBoundary is not null)
            {
                graph.virtualId = instanceRoot.id;
                RestoreNestedCallSiteFields(graph, replayBoundary);
            }
            var expansion = new PreparedVirtualExpansion(instanceRoot);
            foreach (string dependency in dependencyIds)
                if (dependency.StartsWith("static:", StringComparison.Ordinal)
                    || dependency.StartsWith("identity:", StringComparison.Ordinal)
                    || dependency.StartsWith("field:", StringComparison.Ordinal)
                    || (candidateReplay?.Allocations.ContainsKey(dependency) == true && !allocations.Ids.Contains(dependency))
                    || sessionData.values.ContainsKey(dependency)
                    || data.values.ContainsKey(dependency) || saveData.values.ContainsKey(dependency)
                    || virtualValues.ContainsKey(dependency)
                    || candidateReplay?.Values.ContainsKey(dependency) == true)
                    expansion.Dependencies.Add(dependency);
            using (ReplayOverlayMarker.Auto())
            {
                OverlaySparseInstance(
                    expansion,
                    graph,
                    instanceRoot.id,
                    ownership,
                    instanceRoot);
                RemapVirtualDelegateReceivers(graph, expansion);
                if (replayBoundary is not null) expansion.Boundary = replayBoundary;
                else PartitionNestedReplay(graph, expansion, instanceRoot);
            }
            if (prepareOnly) return expansion;
            InstallVirtualExpansion(expansion);
            // The sweep above only covers ids that were ALREADY virtual.
            // A member the previous pass found materialized contributed no
            // prior id, so a pass that turns it back into a virtual one —
            // an imperative pin being cleared, an override being removed —
            // released nothing, and the wrapper still bound to that id
            // keeps serving the row it held when the override vanished
            // (null, since the shadow it named is gone). Release those too:
            // the release set is every id this root answers afterwards, not
            // just the ones it answered before.
            if (virtualValueIdsByRoot.TryGetValue(
                    instanceRoot.id,
                    out HashSet<string>? publishedVirtualIds))
            {
                var newlyVirtualIds = new HashSet<string>(
                    publishedVirtualIds,
                    StringComparer.Ordinal);
                newlyVirtualIds.ExceptWith(releasedVirtualIds);
                if (newlyVirtualIds.Count > 0)
                {
                    DisposeWrappersTouchingRows(newlyVirtualIds);
                }
            }
            return expansion;
        }

        private NeoMemberClassWritable ReplayVirtualInstance(
            ObjectMemberValue root,
            ClassMember? placementMember,
            IReadOnlyDictionary<string, GenericBinding>? classArguments)
        {
            if (root.instanceVariantId is not null)
            {
                VariantRecord? variant = NeoVariantSupport.ResolveRecord(
                    this,
                    root.classId!,
                    root.instanceVariantId);
                object? lookupRow = null;
                if (root.instanceVariantRowValueId is string lookupRowId)
                {
                    lookupRow = UnwrapVirtualReplayRow(lookupRowId);
                }
                return NeoVariantSupport.InitializeNode(
                    this,
                    root.classId!,
                    variant,
                    lookupRow,
                    root.instanceVariantRowValueId);
            }

            if (DerivesContentFromPlacementDefault(root, placementMember))
            {
                return NeoGeneratedTypesSupport.EvaluateStoredClassMemberDefault(
                    this,
                    placementMember!,
                    root);
            }

            ConstructorRecord? constructor = null;
            if (root.instanceConstructorId is string constructorId)
            {
                if (!data.constructors.TryGetValue(
                        constructorId,
                        out constructor)
                    || constructor.classId != root.classId)
                {
                    throw new InvalidOperationException(
                        $"Instance names missing constructor '{constructorId}'.");
                }
            }
            Dictionary<string, JToken?> stored = root.constructorArgs
                ?? (constructor is null
                    ? new Dictionary<string, JToken?>()
                    : throw new InvalidOperationException(
                        "Instance has no constructorArgs creation data."));
            var replayArguments = new List<NeoDeclaredConstructorArgument>();
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv =
                NeoGenericResolution.ResolveInstanceEnv(
                    this,
                    root.classId!,
                    classArguments);
            if (constructor is not null)
            {
                for (int index = 0; index < constructor.argumentTypes.Length; index++)
                {
                    FunctionArgumentTypeInfo argument = constructor.argumentTypes[index];
                    string parameterId = ConstructorParameterId(constructor, index);
                    if (!stored.TryGetValue(parameterId, out JToken? value))
                    {
                        if (NeoParameterDefaults.HasDefault(argument)) continue;
                        throw new InvalidOperationException(
                            $"Constructor '{constructor.id}' is missing argument '{parameterId}'.");
                    }
                    TypeInfo replayType =
                        NeoNSFunctionRuntime.ResolveInvocationTypeInfo(
                            this,
                            argument,
                            genericEnv);
                    replayArguments.Add(new NeoDeclaredConstructorArgument(
                        argument.name,
                        VirtualReplayArgument(value, replayType)));
                }
            }
            return NeoGeneratedTypesSupport.EvaluateStoredDeclaredConstructor(
                this,
                root.classId!,
                root.instanceConstructorId,
                replayArguments.ToArray(),
                classArguments,
                root.genericBindings);
        }

        /// <summary>
        /// The web's declaration-default replay rule. An implicit construction
        /// pair records no creation choice beyond "use the position's
        /// declaration"; when that Class member carries authored content,
        /// replaying bare <c>new C()</c> would silently discard it.
        /// </summary>
        private static bool DerivesContentFromPlacementDefault(
            ObjectMemberValue root,
            ClassMember? placementMember)
        {
            if (placementMember?.Payload == NeoMemberPayloadKind.Partial) return false;
            if (placementMember?.defaultValue?.value is not { Count: > 0 })
                return false;
            string effectiveClassId = placementMember.defaultValue.classId
                ?? placementMember.classId;
            if (effectiveClassId != root.classId) return false;
            if (root.instanceVariantId is not null) return false;
            if (root.instanceConstructorId is not null) return false;
            return root.constructorArgs is null || root.constructorArgs.Count == 0;
        }

        /// <summary>
        /// The <see cref="MemberValue.constructorArgs"/> key for one declared
        /// parameter: the compiled action's own parameter id when it has one
        /// (slots 0 and 1 are <c>__this__</c> and <c>__root__</c>), and a
        /// positional fallback otherwise.
        /// </summary>
        internal static string ConstructorParameterId(
            ConstructorRecord constructor,
            int index)
        {
            Variable[] parameters = constructor.action?.parameters
                ?? Array.Empty<Variable>();
            return index + 2 < parameters.Length
                && !string.IsNullOrEmpty(parameters[index + 2].id)
                    ? parameters[index + 2].id
                    : $"__arg_{index}__";
        }

        private object? VirtualReplayArgument(
            JToken? token,
            TypeInfo typeInfo)
        {
            if (token is null || token.Type is JTokenType.Null or JTokenType.Undefined)
            {
                return null;
            }
            if (token.Type == JTokenType.String
                && typeInfo.type is MemberKind.Class
                    or MemberKind.Interface
                    or MemberKind.List
                    or MemberKind.Dictionary)
            {
                string valueId = token.Value<string>()!;
                var reads = capturedValueReads;
                using var metadataReads = SuppressValueReads();
                EnsureVirtualReplayArgumentReady(valueId);
                if (!TryGetValue(valueId, out MemberValue? row))
                {
                    throw new InvalidOperationException(
                        $"Constructor argument references missing value '{valueId}'.");
                }
                if (!TryGetValueOwnership(
                        valueId,
                        out NeoValueOwnership ownership))
                {
                    throw new InvalidOperationException(
                        $"Constructor argument value '{valueId}' has no resolvable storage ownership, so the instance cannot be replayed against it.");
                }
                reads?.Add(row is ObjectMemberValue { classId: not null } ? "identity:" + valueId : valueId);
                return new NeoConstructorValueReference(valueId, ownership);
            }
            return typeInfo.type switch
            {
                MemberKind.Int => token.ToObject<int>(),
                MemberKind.Float => token.ToObject<double>(),
                MemberKind.Bool => token.ToObject<bool>(),
                MemberKind.NSDelegate => token.ToObject<NeoDelegateValue>(),
                MemberKind.NSAction => token.ToObject<NeoActionValue>(),
                MemberKind.Vector2 or MemberKind.Vector2Int =>
                    token.ToObject<NeoVector2Value>(),
                MemberKind.Vector3 or MemberKind.Vector3Int =>
                    token.ToObject<NeoVector3Value>(),
                MemberKind.Color => token.ToObject<NeoColorValue>(),
                MemberKind.Sprite => token.ToObject<SpriteValue>(),
                MemberKind.Audio => token.ToObject<FileValue>(),
                _ => RuntimeJsonValue(token),
            };
        }

        private object UnwrapVirtualReplayRow(string valueId)
        {
            var referenceReads = capturedValueReads;
            using var wrapperReads = SuppressValueReads();
            EnsureVirtualReplayArgumentReady(valueId);
            if (!TryGetValue(valueId, out MemberValue? row))
            {
                throw new InvalidOperationException(
                    $"Constructor argument references missing value '{valueId}'.");
            }
            referenceReads?.Add(row is ObjectMemberValue { classId: not null } ? "identity:" + valueId : valueId);
            // Defaulting to Asset here would read a Save/Session argument row
            // through the immutable store and silently replay the instance
            // from the wrong layer. The row exists (the lookup above
            // succeeded), so an unresolvable ownership is an index defect, not
            // a value the caller can be given.
            if (!TryGetValueOwnership(valueId, out NeoValueOwnership ownership))
            {
                throw new InvalidOperationException(
                    $"Constructor argument value '{valueId}' has no resolvable storage ownership, so the instance cannot be replayed against it.");
            }
            var ctx = new NSGetterEvaluator.Context(
                this,
                thisValue: null,
                rootValue: null,
                valueOwnership: ownership);
            ctx = ctx.WithRoot(NeoScriptValueMarshaller.ResolveRoot(this, ctx));
            return NSGetterEvaluator.UnwrapRow(row, ctx, ownership)
                ?? throw new InvalidOperationException(
                    $"Constructor argument value '{valueId}' resolved to null.");
        }

        private static object? RuntimeJsonValue(JToken token)
        {
            return token.Type switch
            {
                JTokenType.Boolean => token.Value<bool>(),
                JTokenType.Integer => token.Value<long>(),
                JTokenType.Float => token.Value<double>(),
                JTokenType.String => token.Value<string>(),
                JTokenType.Array => ((JArray)token)
                    .Select(entry => RuntimeJsonValue(entry))
                    .ToArray(),
                JTokenType.Object => ((JObject)token).Properties()
                    .ToDictionary(
                        property => property.Name,
                        property => RuntimeJsonValue(property.Value)),
                JTokenType.Null or JTokenType.Undefined => null,
                _ => token.ToObject<object>(),
            };
        }

        private VirtualExpansionNode IndexVirtualExpansion(
            ObjectMemberValue instanceRoot,
            MemberValue row,
            Member member,
            string path,
            Dictionary<string, string> claimedVirtualIds,
            Dictionary<MemberValue, IReadOnlyDictionary<string, NeoGenericEnvEntry>> environments)
        {
            string sourceIdentity = VirtualSourceIdentity(row, member, path);
            string virtualId = path == "$"
                ? instanceRoot.id
                : VirtualValueId(instanceRoot.id, sourceIdentity);
            if (claimedVirtualIds.TryGetValue(
                    virtualId,
                    out string? claimedPath)
                && claimedPath != path)
            {
                sourceIdentity = $"{sourceIdentity}:{path}";
                virtualId = VirtualValueId(instanceRoot.id, sourceIdentity);
            }
            claimedVirtualIds[virtualId] = path;
            var node = new VirtualExpansionNode
            {
                row = row,
                member = member,
                path = path,
                virtualId = virtualId,
            };

            // Replay rows live in temporary Session storage, including immutable
            // members. Read the constructed graph directly: read-only wrappers
            // select Asset storage and can omit those temporary children.
            if (member is ClassMember classMember && row is ObjectMemberValue classRow)
            {
                string classId = classRow.classId ?? classMember.classId;
                foreach (var entry in ResolveStoredInstanceSchema(classId))
                {
                    if (classRow.value is null || !classRow.value.TryGetValue(entry.schemaKey, out string childId)
                        || !TryGetMember(entry.memberId, out Member? declaration)
                        || TryResolveOwnedChildMember(classRow, classMember, entry.schemaKey, environments, declaration) is not Member childMember) continue;
                    node.classChildren[entry.schemaKey] = Child(childId, childMember,
                        AppendVirtualPath(path, "class", "schemaKey", entry.schemaKey));
                }
            }
            else if (member is ListMember list && row is ArrayMemberValue array)
            {
                if (TryResolveCollectionEntryMember(list, row) is not Member entry)
                    throw new InvalidOperationException($"Missing list entry declaration '{list.entryMemberId}'.");
                IEnumerable<string> ids = IsUnorderedList(list) ? GetUnorderedListEntryIds(row.id) : array.value ?? Array.Empty<string>();
                int index = 0;
                foreach (string id in ids)
                    node.listChildren.Add(Child(id, entry, AppendVirtualPath(path, "list", "index",
                        (index++).ToString(System.Globalization.CultureInfo.InvariantCulture), numericValue: true)));
            }
            else if (member is DictionaryMember dictionary && row is ObjectMemberValue entries)
            {
                if (TryResolveCollectionEntryMember(dictionary, row) is not Member entry)
                    throw new InvalidOperationException($"Missing dictionary entry declaration '{dictionary.entryMemberId}'.");
                foreach (var pair in entries.value ?? new Dictionary<string, string>())
                    node.dictionaryChildren[pair.Key] = Child(pair.Value, entry,
                        AppendVirtualPath(path, "dictionary", "key", pair.Key));
            }
            VirtualExpansionNode Child(string id, Member childMember, string childPath)
            {
                var child = IndexVirtualExpansion(instanceRoot,
                    ResolveValueRow(id) ?? throw new InvalidOperationException($"Replay lost child '{id}' at '{childPath}'."),
                    childMember, childPath, claimedVirtualIds, environments);
                child.parent = node;
                return child;
            }
            return node;
        }

        private void RemapVirtualDelegateReceivers(VirtualExpansionNode root, PreparedVirtualExpansion expansion)
        {
            var identities = new Dictionary<string, string>();
            var selectors = new List<DelegateMemberValue>();
            var pending = new Stack<VirtualExpansionNode>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                VirtualExpansionNode node = pending.Pop();
                if (node.effectiveId is null) continue;
                identities[node.row.id] = node.effectiveId;
                if (expansion.Values.TryGetValue(node.effectiveId, out MemberValue? row)
                    && row is DelegateMemberValue selector)
                    selectors.Add(selector);
                foreach (var child in node.classChildren.Values) pending.Push(child);
                foreach (var child in node.listChildren) pending.Push(child);
                foreach (var child in node.dictionaryChildren.Values) pending.Push(child);
            }
            // Replay's temporary rows are removed next. Preserve bindings to the
            // effective stored/virtual instance, including forward sibling targets.
            foreach (DelegateMemberValue selector in selectors)
            {
                if (selector.value?.valueId is { } receiver
                    && identities.TryGetValue(receiver, out string? effective))
                    selector.value.valueId = effective;
            }
        }

        private MemberValue RewriteVirtualRow(
            VirtualExpansionNode node,
            ObjectMemberValue instanceRoot)
        {
            MemberValue clone = CloneValueRow(node.row);
            clone.id = node.virtualId;
            clone.createdAt = instanceRoot.createdAt;
            clone.updatedAt = instanceRoot.updatedAt;
            clone.mapKey = instanceRoot.mapKey;
            if (node.parent?.member is ListMember parentList
                && IsUnorderedList(parentList))
            {
                clone.containerId = node.parent.virtualId;
            }
            switch (clone)
            {
                case ObjectMemberValue obj when obj.value is not null:
                    if (node.member is ClassMember)
                    {
                        obj.value = node.classChildren.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value.virtualId);
                        if (obj.constructorArgs is not null)
                        {
                            foreach (var link in
                                EnumerateConstructorSettledAggregateLinks(
                                    obj,
                                    node.member,
                                    includeMaterializedChildren: true))
                            {
                                if (!node.classChildren.TryGetValue(
                                        link.schemaKey,
                                        out VirtualExpansionNode? child)
                                    || child.row.id != link.valueId)
                                {
                                    continue;
                                }
                                obj.constructorArgs[link.parameterId] =
                                    new JValue(child.virtualId);
                            }
                        }
                    }
                    else if (node.member is DictionaryMember)
                    {
                        obj.value = node.dictionaryChildren.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value.virtualId);
                    }
                    break;
                case ArrayMemberValue array when array.value is not null:
                    if (node.member is ListMember list && !IsUnorderedList(list))
                    {
                        array.value = node.listChildren
                            .Select(child => child.virtualId)
                            .ToArray();
                    }
                    break;
            }
            return clone;
        }

        private sealed class PreparedVirtualExpansion
        {
            internal readonly ObjectMemberValue Root;
            internal readonly Dictionary<string, MemberValue> Values = new();
            internal readonly Dictionary<string, NeoValueOwnership> Ownership = new();
            internal readonly Dictionary<string, Dictionary<string, string>> ClassChildren = new();
            internal readonly Dictionary<string, VirtualClassPlacement> Placements = new();
            internal readonly HashSet<string> Footprint = new();
            internal readonly HashSet<string> Dependencies = new();
            internal readonly List<PreparedVirtualExpansion> Nested = new();
            internal NestedReplayBoundary? Boundary;

            internal PreparedVirtualExpansion(ObjectMemberValue root) => Root = root;

            internal void TrackPlacement(string parentId, string childId, Member member, NeoValueOwnership ownership)
            {
                Placements[childId] = new VirtualClassPlacement
                {
                    rootId = Root.id, parentValueId = parentId, member = member, ownership = ownership,
                };
            }
        }

        private void InstallVirtualExpansion(PreparedVirtualExpansion expansion, bool includeNested = true)
        {
            string rootId = expansion.Root.id;
            foreach (var pair in expansion.Values)
            {
                virtualValues[pair.Key] = pair.Value;
                EvictSharedEvaluationRow(pair.Key);
                virtualValueOwnership[pair.Key] = expansion.Ownership[pair.Key];
                TrackVirtualValue(rootId, pair.Key);
                if (!string.IsNullOrEmpty(pair.Value.containerId))
                    AddMembership(virtualEntriesByContainer, virtualContainerByRow, pair.Key, pair.Value.containerId!);
            }
            foreach (var pair in expansion.ClassChildren)
            {
                virtualClassChildren[pair.Key] = pair.Value;
                TrackVirtualClassParent(rootId, pair.Key);
            }
            foreach (var pair in expansion.Placements)
                TrackVirtualClassPlacement(rootId, pair.Value.parentValueId, pair.Key, pair.Value.member, pair.Value.ownership);
            foreach (string id in expansion.Footprint) TrackVirtualFootprint(rootId, id);
            IndexConstructorArgumentRows(expansion.Root);
            foreach (string id in expansion.Dependencies) TrackReplayDependency(rootId, id);
            if (expansion.Boundary is not null) InstallNestedReplayBoundary(expansion.Boundary);
            if (includeNested)
                foreach (var nested in expansion.Nested) InstallVirtualExpansion(nested);
        }

        private void OverlaySparseInstance(
            PreparedVirtualExpansion expansion,
            VirtualExpansionNode node,
            string? materializedId,
            NeoValueOwnership ownership,
            ObjectMemberValue instanceRoot)
        {
            // Removed fields are intentionally absent, not missing defaults.
            // Do not recreate their virtual descendants or keep them reachable.
            if (TryGetWritableValue(ownership, materializedId ?? node.virtualId, out MemberValue? storedRow)
                && storedRow.IsRemoved) return;
            MemberValue? materialized = null;
            if (materializedId is not null)
                TryGetOverlaidValue(ownership, materializedId, out materialized);
            // Sparse class spines are stored at their deterministic virtual
            // ids without requiring every ancestor body to point at them.
            // Probe that stable id before treating the whole subtree as
            // virtual; otherwise a stored empty Class row hides all deeper
            // virtual links and truncates web-authored overrides in Unity.
            if (materialized is null
                && materializedId != node.virtualId)
            {
                if (TryGetWritableValue(ownership, node.virtualId, out MemberValue? fallbackRow)
                    && fallbackRow.IsRemoved) return;
                TryGetOverlaidValue(ownership, node.virtualId, out materialized);
            }
            if (materialized is null)
            {
                // A materialized nested instance can retain default-child edges
                // from its outer constructor namespace. Recreate the default at
                // the persisted edge's identity so that reference stays valid.
                if (materializedId is not null && IsVirtualInstanceRoot(instanceRoot))
                    node.virtualId = materializedId;
                IndexVirtualSubtree(expansion, node, instanceRoot, ownership);
                return;
            }
            node.effectiveId = materialized.id;
            if (node.parent is not null)
                expansion.TrackPlacement(node.parent.effectiveId, node.effectiveId, node.member, ownership);
            if (materialized.id != instanceRoot.id
                && materialized is ObjectMemberValue nestedRoot
                && IsVirtualInstanceRoot(nestedRoot)
                && !IsMaterializedSpine(nestedRoot, node))
            {
                // A nested construction owns its own UUID namespace and is
                // replayed independently. Do not retain an unreachable copy
                // of its virtual remainder in the outer root's namespace.
                return;
            }
            string effectiveId = materialized?.id ?? node.virtualId;
            expansion.Footprint.Add(effectiveId);

            if (node.member is ClassMember)
            {
                Dictionary<string, string>? stored =
                    (materialized as ObjectMemberValue)?.value;
                foreach (var pair in node.classChildren)
                {
                    string? childMaterializedId = null;
                    if (stored is not null)
                        stored.TryGetValue(pair.Key, out childMaterializedId);
                    NeoValueOwnership childOwnership =
                        ChildOwnership(pair.Value.member, ownership);
                    if (childMaterializedId is null)
                    {
                        if (!expansion.ClassChildren.TryGetValue(
                                effectiveId,
                                out Dictionary<string, string>? links))
                        {
                            links = new Dictionary<string, string>(StringComparer.Ordinal);
                            expansion.ClassChildren[effectiveId] = links;
                        }
                        links[pair.Key] = pair.Value.virtualId;
                        if (pair.Value.member is not null)
                        {
                            expansion.TrackPlacement(
                                effectiveId,
                                pair.Value.virtualId,
                                pair.Value.member,
                                childOwnership);
                        }
                    }
                    // Ownership switches at declared member boundaries: a
                    // Save-storage member's pins live in the SAVE store, and
                    // probing (and stamping) them under the root's Asset
                    // ownership hid materialized save overrides and starved
                    // the save sweep's virtual seeds.
                    OverlaySparseInstance(
                        expansion,
                        pair.Value,
                        childMaterializedId,
                        childOwnership,
                        instanceRoot);
                }
                return;
            }

            if (node.member is ListMember list && !IsUnorderedList(list))
            {
                string[]? stored = (materialized as ArrayMemberValue)?.value;
                for (int index = 0; index < node.listChildren.Count; index++)
                {
                    OverlaySparseInstance(
                        expansion,
                        node.listChildren[index],
                        stored is not null && index < stored.Length
                            ? stored[index]
                            : null,
                        ownership,
                        instanceRoot);
                }
                return;
            }
            if (node.member is ListMember unorderedList
                && IsUnorderedList(unorderedList))
            {
                var materializedBySource = new Dictionary<string, Queue<string>>(
                    StringComparer.Ordinal);
                foreach (string entryId in GetUnorderedListEntryIds(effectiveId))
                {
                    MemberValue? entry = ResolveValueRow(entryId);
                    if (string.IsNullOrEmpty(entry?.sourceValueId)) continue;
                    if (!materializedBySource.TryGetValue(
                            entry!.sourceValueId!,
                            out Queue<string>? matches))
                    {
                        matches = new Queue<string>();
                        materializedBySource[entry.sourceValueId!] = matches;
                    }
                    matches.Enqueue(entry.id);
                }
                foreach (VirtualExpansionNode child in node.listChildren)
                {
                    string? childMaterializedId = null;
                    if (!string.IsNullOrEmpty(child.row.sourceValueId)
                        && materializedBySource.TryGetValue(
                            child.row.sourceValueId!,
                            out Queue<string>? matches)
                        && matches.Count > 0)
                    {
                        childMaterializedId = matches.Dequeue();
                    }
                    // A write under a nested instance stores it at its
                    // deterministic id, with no source id to match above.
                    // Probe that stable id before minting the entry again, or
                    // this root claims a second copy of a stored row.
                    if (childMaterializedId is null
                        && IsNestedInstanceNode(child)
                        && TryGetOverlaidValue(ownership, child.virtualId, out MemberValue? _))
                    {
                        childMaterializedId = child.virtualId;
                    }
                    if (childMaterializedId is null)
                    {
                        IndexVirtualSubtree(
                            expansion,
                            child,
                            instanceRoot,
                            ownership,
                            effectiveId);
                    }
                    else
                    {
                        OverlaySparseInstance(
                            expansion,
                            child,
                            childMaterializedId,
                            ownership,
                            instanceRoot);
                    }
                }
                return;
            }
            if (node.member is DictionaryMember)
            {
                Dictionary<string, string>? stored =
                    (materialized as ObjectMemberValue)?.value;
                foreach (var pair in node.dictionaryChildren)
                {
                    string? childMaterializedId = null;
                    if (stored is not null)
                        stored.TryGetValue(pair.Key, out childMaterializedId);
                    OverlaySparseInstance(
                        expansion,
                        pair.Value,
                        childMaterializedId,
                        ownership,
                        instanceRoot);
                }
            }
        }

        private void IndexVirtualSubtree(
            PreparedVirtualExpansion expansion,
            VirtualExpansionNode node,
            ObjectMemberValue instanceRoot,
            NeoValueOwnership ownership,
            string? unorderedContainerId = null)
        {
            node.effectiveId = node.virtualId;
            // Collection entries need their closed placement just as class
            // fields do, including when a candidate replay hides the old graph.
            if (node.parent is not null)
                expansion.TrackPlacement(node.parent.effectiveId, node.virtualId, node.member, ownership);
            expansion.Footprint.Add(node.virtualId);
            MemberValue virtualRow = RewriteVirtualRow(node, instanceRoot);
            if (unorderedContainerId is not null)
                virtualRow.containerId = unorderedContainerId;
            expansion.Values[node.virtualId] = virtualRow;
            expansion.Ownership[node.virtualId] = ownership;
            foreach (VirtualExpansionNode child in node.classChildren.Values)
            {
                // Ownership switches at declared member boundaries, exactly as
                // the reachability walk's own edge filter does: a Save-storage
                // member under an Asset root owns a SAVE slot, and stamping it
                // Asset made the save sweep skip the very expansion ids its
                // pins materialize under (an Outpost's OutpostSaveData after
                // the collapse virtualized the key).
                NeoValueOwnership childOwnership =
                    ChildOwnership(child.member, ownership);
                if (child.member is not null)
                {
                    expansion.TrackPlacement(
                        node.virtualId,
                        child.virtualId,
                        child.member,
                        childOwnership);
                }
                IndexVirtualChild(expansion, child, instanceRoot, childOwnership);
            }
            foreach (VirtualExpansionNode child in node.listChildren)
                IndexVirtualChild(expansion, child, instanceRoot, ownership);
            foreach (VirtualExpansionNode child in node.dictionaryChildren.Values)
                IndexVirtualChild(expansion, child, instanceRoot, ownership);
        }

        /// <summary>
        /// A write under a nested instance stores that instance at its
        /// deterministic id while the virtual rows above it stay virtual:
        /// the entry object under a still-virtual Children list. Reads see
        /// the stored row at any depth, so the index must probe for it
        /// before minting the subtree, or this root claims a second copy of
        /// the stored row and the two disagree about who answers its omitted
        /// members.
        /// </summary>
        private void IndexVirtualChild(
            PreparedVirtualExpansion expansion,
            VirtualExpansionNode child,
            ObjectMemberValue instanceRoot,
            NeoValueOwnership ownership)
        {
            if (IsNestedInstanceNode(child))
                OverlaySparseInstance(expansion, child, null, ownership, instanceRoot);
            else
                IndexVirtualSubtree(expansion, child, instanceRoot, ownership);
        }

        private static bool IsNestedInstanceNode(VirtualExpansionNode node)
            => node.row is ObjectMemberValue row && IsVirtualInstanceRoot(row);

        /// <summary>
        /// P75 §3.1: materializing a value writes a real row under the id its
        /// virtual copy had. A write under this expansion's copy of a nested
        /// construction therefore stores that copy at the id this expansion
        /// minted, carrying the construction stamp its replay row had. It is a
        /// spine of this graph, not a separate root: its omitted members,
        /// call-site initializers included, and whatever this root's variant
        /// applied to it, resolve here. Only a variant selected on the nested
        /// value itself gives it a recipe of its own; a new construction
        /// assigned into the slot keeps its own id, so it never lands here.
        /// </summary>
        private static bool IsMaterializedSpine(
            ObjectMemberValue stored,
            VirtualExpansionNode node)
            => stored.id == node.virtualId && SameVariantSelection(stored, node.row);

        private static bool SameVariantSelection(MemberValue left, MemberValue right)
            => left.instanceVariantId == right.instanceVariantId
                && left.instanceVariantRowValueId == right.instanceVariantRowValueId;

        /// <summary>
        /// The reachability edges the virtual index owns for one already
        /// reachable value: the sparse-spine schema-key links hanging off it,
        /// and — when it is an instance root — every id inside its expansion.
        /// A row materialized at one of those ids is a user override of an
        /// omitted member and is reachable exactly because its root is.
        /// </summary>
        private IEnumerable<string> EnumerateVirtualReachableChildIds(
            NeoValueOwnership ownership,
            string valueId)
        {
            foreach (var link in EnumerateVirtualReachableChildLinks(
                ownership,
                valueId))
            {
                yield return link.valueId;
            }
        }

        private IEnumerable<(string valueId, Member? member)>
            EnumerateVirtualReachableChildLinks(
                NeoValueOwnership ownership,
                string valueId)
        {
            if (TryGetOverlaidValue(ownership, valueId, out ObjectMemberValue? current)
                && current.value is null)
                yield break;
            if (TryResolveVirtualClassChildren(
                    valueId,
                    out Dictionary<string, string>? links))
            {
                // The parent's class types each link edge: a virtual-linked
                // child sits at a v5 id no raw body names, so member
                // inference by raw parents cannot recover it downstream.
                string? parentClassId = null;
                if ((data.values.TryGetValue(valueId, out MemberValue? parent)
                        || saveData.values.TryGetValue(valueId, out parent)
                        || sessionData.values.TryGetValue(valueId, out parent)
                        || TryResolveVirtualValue(valueId, out parent))
                    && parent is ObjectMemberValue parentObject)
                {
                    parentClassId = parentObject.classId;
                }
                foreach (var pair in links)
                {
                    Member? linkMember = null;
                    if (parentClassId is not null)
                    {
                        try
                        {
                            foreach (MergedSchemaEntry entry
                                in ResolveInstanceSurfaceSchema(parentClassId))
                            {
                                if (entry.schemaKey != pair.Key) continue;
                                TryGetMember(entry.memberId, out linkMember);
                                break;
                            }
                        }
                        catch (CircularInheritanceError)
                        {
                            linkMember = null;
                        }
                    }
                    yield return (pair.Value, linkMember);
                }
            }
            if (!virtualValueIdsByRoot.TryGetValue(
                    valueId,
                    out HashSet<string>? expansionIds))
            {
                yield break;
            }
            foreach (string expansionId in expansionIds)
            {
                // The expansion is indexed under the root's own ownership;
                // a differently-owned id belongs to another store's sweep.
                if (TryResolveVirtualOwnership(
                        expansionId,
                        out NeoValueOwnership expansionOwnership)
                    && expansionOwnership != ownership)
                {
                    continue;
                }
                yield return (expansionId, null);
            }
        }

        /// <summary>
        /// Virtual storage boundaries are independent writable roots. Ordinary
        /// descendants remain reachable only through their owning graph.
        /// </summary>
        private IEnumerable<string> VirtualValueIdsByOwnership(
            NeoValueOwnership ownership)
        {
            foreach (var pair in virtualValueOwnership)
            {
                if (pair.Value == ownership
                    && TryResolveVirtualPlacement(pair.Key, out VirtualClassPlacement? placement)
                    && TryGetValueOwnership(placement.parentValueId, out NeoValueOwnership parentOwnership)
                    && parentOwnership != ownership)
                    yield return pair.Key;
            }
        }

        private void TrackVirtualValue(string rootId, string valueId)
        {
            if (!virtualValueIdsByRoot.TryGetValue(rootId, out HashSet<string>? ids))
            {
                ids = new HashSet<string>(StringComparer.Ordinal);
                virtualValueIdsByRoot[rootId] = ids;
            }
            ids.Add(valueId);
        }

        private void TrackVirtualClassParent(string rootId, string parentId)
        {
            if (!virtualClassParentIdsByRoot.TryGetValue(
                    rootId,
                    out HashSet<string>? ids))
            {
                ids = new HashSet<string>(StringComparer.Ordinal);
                virtualClassParentIdsByRoot[rootId] = ids;
            }
            ids.Add(parentId);
        }

        private void TrackVirtualClassPlacement(
            string rootId,
            string parentValueId,
            string childValueId,
            Member member,
            NeoValueOwnership ownership)
        {
            virtualClassPlacementByChildId[childValueId] =
                new VirtualClassPlacement
                {
                    rootId = rootId,
                    parentValueId = parentValueId,
                    member = member,
                    ownership = ownership,
                };
            if (!virtualClassChildIdsByRoot.TryGetValue(
                    rootId,
                    out HashSet<string>? ids))
            {
                ids = new HashSet<string>(StringComparer.Ordinal);
                virtualClassChildIdsByRoot[rootId] = ids;
            }
            ids.Add(childValueId);
        }

        private void TrackVirtualFootprint(string rootId, string valueId)
        {
            if (!virtualFootprintByRoot.TryGetValue(rootId, out HashSet<string>? ids))
            {
                ids = new HashSet<string>(StringComparer.Ordinal);
                virtualFootprintByRoot[rootId] = ids;
            }
            if (ids.Add(valueId)) virtualRootByFootprintId[valueId] = rootId;
        }

        private void ClearVirtualInstanceRoot(string rootId)
        {
            ClearNestedReplayBoundary(rootId);
            RemoveConstructorArgumentRows(rootId);
            if (virtualFootprintByRoot.TryGetValue(
                    rootId,
                    out HashSet<string>? footprint))
            {
                foreach (string valueId in footprint)
                {
                    if (virtualRootByFootprintId.TryGetValue(
                            valueId,
                            out string? owner)
                        && owner == rootId)
                    {
                        virtualRootByFootprintId.Remove(valueId);
                    }
                }
                virtualFootprintByRoot.Remove(rootId);
            }
            if (virtualValueIdsByRoot.TryGetValue(rootId, out HashSet<string>? valueIds))
            {
                foreach (string valueId in valueIds)
                {
                    virtualValues.Remove(valueId);
                    EvictSharedEvaluationRow(valueId);
                    virtualValueOwnership.Remove(valueId);
                    if (virtualContainerByRow.TryGetValue(
                            valueId,
                            out string? containerId))
                    {
                        virtualContainerByRow.Remove(valueId);
                        if (virtualEntriesByContainer.TryGetValue(
                                containerId,
                                out HashSet<string>? entries))
                        {
                            entries.Remove(valueId);
                            if (entries.Count == 0)
                                virtualEntriesByContainer.Remove(containerId);
                        }
                    }
                }
                virtualValueIdsByRoot.Remove(rootId);
            }
            if (virtualClassParentIdsByRoot.TryGetValue(
                    rootId,
                    out HashSet<string>? parentIds))
            {
                foreach (string parentId in parentIds)
                    virtualClassChildren.Remove(parentId);
                virtualClassParentIdsByRoot.Remove(rootId);
            }
            if (virtualClassChildIdsByRoot.TryGetValue(
                    rootId,
                    out HashSet<string>? childIds))
            {
                foreach (string childId in childIds)
                {
                    if (virtualClassPlacementByChildId.TryGetValue(
                            childId,
                            out VirtualClassPlacement? placement)
                        && placement.rootId == rootId)
                    {
                        virtualClassPlacementByChildId.Remove(childId);
                    }
                }
                virtualClassChildIdsByRoot.Remove(rootId);
            }
        }

        private static string AppendVirtualPath(
            string parent,
            string kind,
            string valueName,
            string value,
            bool numericValue = false)
        {
            string encodedName = Newtonsoft.Json.JsonConvert.SerializeObject(valueName);
            string encodedValue = numericValue
                ? value
                : Newtonsoft.Json.JsonConvert.SerializeObject(value);
            return $"{parent}/{{\"kind\":\"{kind}\",{encodedName}:{encodedValue}}}";
        }

        /// <summary>
        /// The name half of the deterministic id: the row's authored-child
        /// provenance when it has one, and otherwise its position, spelled
        /// <c>path:{memberId}:{pathKey}</c>.
        ///
        /// <para>Both runtimes must spell this identically or the same
        /// omitted value lands at two different ids. A member with no id is
        /// written as <see cref="InlineMemberSentinel"/> rather than as an
        /// empty segment, which is what the web emits for an inline
        /// declaration.</para>
        /// </summary>
        internal static string VirtualSourceIdentity(
            MemberValue row,
            Member member,
            string path)
        {
            if (!string.IsNullOrEmpty(row.sourceValueId)) return row.sourceValueId!;
            string memberId = string.IsNullOrEmpty(member.id)
                ? InlineMemberSentinel
                : member.id;
            return $"path:{memberId}:{path}";
        }

        /// <summary>
        /// Stands in for the member id of an inline (id-less) declaration in
        /// a positional source identity. Byte-identical to the web's.
        /// </summary>
        internal const string InlineMemberSentinel = "<inline>";

        /// <summary>
        /// uuidv5 (RFC 4122, SHA-1, big-endian) of
        /// <c>{bareRootId}:{sourceIdentity}</c> under the P75 namespace. A
        /// <c>system_</c> root keeps its prefix on the derived id and is
        /// hashed without it, so a platform record's virtual children stay in
        /// the platform namespace.
        /// </summary>
        internal static string VirtualValueId(string instanceRootId, string sourceIdentity)
        {
            const string systemPrefix = "system_";
            bool isSystemRecord = instanceRootId.StartsWith(
                systemPrefix,
                StringComparison.Ordinal);
            string bareRootId = isSystemRecord
                ? instanceRootId.Substring(systemPrefix.Length)
                : instanceRootId;
            string namespaceHex = VirtualValueNamespace.Replace("-", string.Empty);
            var namespaceBytes = new byte[namespaceHex.Length / 2];
            for (int index = 0; index < namespaceBytes.Length; index++)
            {
                namespaceBytes[index] = Convert.ToByte(
                    namespaceHex.Substring(index * 2, 2),
                    16);
            }
            byte[] nameBytes = Encoding.UTF8.GetBytes(
                $"{bareRootId}:{sourceIdentity}");
            byte[] input = new byte[namespaceBytes.Length + nameBytes.Length];
            Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
            Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);
            byte[] hash;
            using (SHA1 sha1 = SHA1.Create()) hash = sha1.ComputeHash(input);
            hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
            hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
            string hex = BitConverter.ToString(hash, 0, 16)
                .Replace("-", string.Empty)
                .ToLowerInvariant();
            string valueId = $"{hex.Substring(0, 8)}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}-{hex.Substring(16, 4)}-{hex.Substring(20, 12)}";
            return isSystemRecord ? systemPrefix + valueId : valueId;
        }
    }
}
