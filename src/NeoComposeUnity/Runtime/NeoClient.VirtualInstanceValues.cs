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

        private static readonly byte[] VirtualNamespaceBytes = CreateNamespaceBytes(VirtualValueNamespace);
        private static readonly byte[] MemberValueNamespaceBytes = CreateNamespaceBytes("7f2b9c14-58d6-5a07-b3e1-4c9d0a6f8b25");

        private static byte[] CreateNamespaceBytes(string value)
        {
            byte[] bytes = new Guid(value).ToByteArray();
            // Guid stores its first three fields in little-endian order. UUIDv5
            // hashes the namespace in network byte order.
            Array.Reverse(bytes, 0, 4);
            Array.Reverse(bytes, 4, 2);
            Array.Reverse(bytes, 6, 2);
            return bytes;
        }

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
        private IReadOnlyDictionary<string, TypeBinding>?
            replayingVirtualInstanceClassArguments;
        private IReadOnlyDictionary<string, TypeBinding>?
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
            out IReadOnlyDictionary<string, TypeBinding>? classArguments,
            out IReadOnlyDictionary<string, TypeBinding>? storedGenericBindings)
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

        private (string temporaryId, ObjectMemberValue durableRoot)? storedConstructorReplayRoot;

        internal StoredConstructorReplayRootScope BindStoredConstructorReplayRoot(ObjectMemberValue temporaryRoot)
        {
            var previous = storedConstructorReplayRoot;
            (string temporaryId, ObjectMemberValue durableRoot)? next = null;
            if (isReplayingVirtualInstance
                && temporaryRoot.classId == replayingVirtualInstanceClassId
                && replayingVirtualInstanceRootId is string durableId
                && TryGetReplayReference(durableId, out MemberValue? row)
                && row is ObjectMemberValue durableRoot
                && durableRoot.classId == temporaryRoot.classId)
            {
                next = (temporaryRoot.id, durableRoot);
            }
            storedConstructorReplayRoot = next;
            return new StoredConstructorReplayRootScope(this, previous);
        }

        internal readonly struct StoredConstructorReplayRootScope : IDisposable
        {
            private readonly NeoClient client;
            private readonly (string temporaryId, ObjectMemberValue durableRoot)? previous;

            internal StoredConstructorReplayRootScope(NeoClient client,
                (string temporaryId, ObjectMemberValue durableRoot)? previous)
            {
                this.client = client;
                this.previous = previous;
            }

            public void Dispose() => client.storedConstructorReplayRoot = previous;
        }

        internal string? StoredReplayDestinationValueId(string? temporaryRootId, string key, string? currentId, string? incomingId)
        {
            if (currentId is not null && currentId == incomingId)
                return currentId;
            if (storedConstructorReplayRoot is not { } replay
                || !isReplayingVirtualInstance
                || replay.temporaryId != temporaryRootId
                || replay.durableRoot.id != replayingVirtualInstanceRootId
                || replay.durableRoot.classId != replayingVirtualInstanceClassId)
                return currentId;
            return replay.durableRoot.value?.GetValueOrDefault(key) ?? currentId;
        }

        internal bool IsReferenceOwnedByReplayingVirtualInstance(
            NeoValueOwnership ownership,
            string valueId)
        {
            if (!isReplayingVirtualInstance)
                return false;
            var visited = RentIdSet();
            try
            {
                while (visited.Add(valueId))
                {
                    string? parentValueId;
                    NeoValueOwnership parentOwnership;
                    if (!TryFindOwnedParent(ownership, valueId, out parentValueId, out parentOwnership))
                    {
                        // Constructor arguments in a detached clone still
                        // reside in Session before their declared tiers apply.
                        if (replayingVirtualInstanceRootId is string rootId
                            && candidateReadPlan?.Resolve(NeoValueOwnership.Session, rootId) is ObjectMemberValue root)
                            foreach (var link in EnumerateConstructorSettledAggregateLinks(root, includeMaterializedChildren: true))
                                if (link.valueId == valueId)
                                    return true;
                        return false;
                    }
                    if (parentValueId == replayingVirtualInstanceRootId)
                        return true;
                    if (parentValueId is null)
                        return false;
                    valueId = parentValueId;
                    ownership = parentOwnership;
                }
                return false;
            }
            finally { ReturnIdSet(visited); }
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
            if (entered)
                awaitingVirtualInstanceChildDepth++;
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
                if (client is null)
                    return;
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
        private readonly HashSet<string> identitySensitiveExpansionRoots = new();
        private readonly Dictionary<string, HashSet<string>>
            constructorArgumentRootsByValueId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>>
            constructorArgumentValueIdsByRoot = new(StringComparer.Ordinal);
        private bool virtualInstanceValuesDirty;

        private sealed class VirtualExpansionNode
        {
            internal MemberValue row = null!;
            internal Member member = null!;
            internal Member payloadMember = null!;
            internal string path = null!;
            internal string? schemaKey;
            internal string virtualId = null!;
            internal string identityId = null!;
            internal string identity = null!;
            internal string identityRoot = null!;
            internal string? effectiveId;
            internal NeoValueOwnership effectiveOwnership;
            internal VirtualExpansionNode? parent;
            internal ObjectMemberValue? independentRoot;
            internal readonly Dictionary<string, VirtualExpansionNode> classChildren = new();
            internal readonly List<VirtualExpansionNode> listChildren = new();
            internal readonly Dictionary<string, VirtualExpansionNode> dictionaryChildren = new();
        }

        private sealed class VirtualClassPlacement
        {
            internal string rootId = null!;
            internal string path = null!;
            internal string parentValueId = null!;
            internal string? identity;
            internal string? identityRoot;
            internal string? schemaKey;
            internal Member member = null!;
            internal NeoValueOwnership ownership;
            internal NeoValueOwnership parentOwnership;
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
        /// <see cref="StampConstructionProvenance"/> on a Session row already
        /// in the store. The placement index links a recipe to the rows its
        /// arguments name, so the row is relinked too.
        /// </summary>
        internal void StampLiveConstructionProvenance(
            ObjectMemberValue live,
            string? constructorId,
            Dictionary<string, JToken?> constructorArgs)
        {
            using var metadataReads = SuppressValueReads();
            StampConstructionProvenance(live, constructorId, constructorArgs);
            if (NeoTypeSources.PlaceConstructorArguments(this, live) is { } placed)
                SetWritableValues(NeoValueOwnership.Session, placed);
            IndexPlacementParent(NeoValueOwnership.Session, live);
        }

        /// <summary>
        /// Serializes one evaluated constructor argument into the creation
        /// data <see cref="MemberValue.constructorArgs"/> stores: literals
        /// stay literals, a constructed argument becomes the id of its
        /// materialized row, and structured runtime values keep their JSON
        /// shape. This is the exact inverse of
        /// <see cref="VirtualReplayArgument"/>.
        ///
        /// <para>The caller resolves the row id of the argument kinds replay
        /// reads back as an id — Class, Interface, List and Dictionary. A
        /// row-backed argument does not always arrive as a generated wrapper:
        /// once it has passed through the evaluator it is the plain record or
        /// array shape, which carries no id of its own. Serializing that shape
        /// would record the row's <i>contents</i> as the recipe, and replay
        /// would then rebuild the instance from a payload map instead of the
        /// row it was actually built from.</para>
        ///
        /// <para><paramref name="type"/> is the parameter's closed type. Floats
        /// write by it through <see cref="NeoStoredFloat"/>, so a float's -0,
        /// NaN and infinities keep their symbols and an int's -0 does not
        /// gain one; decimals write as canonical strings (P127 §12.1,
        /// §3.4).</para>
        /// </summary>
        internal static JToken? ConstructorArgumentToken(
            object? value,
            TypeInfo? type,
            string describeArgument)
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
                // These runtime leaves have a fixed wire shape. Recording
                // constructor provenance need not rediscover it via reflection.
                case NeoVector3Value vector:
                    return new JObject { ["x"] = vector.x, ["y"] = vector.y, ["z"] = vector.z };
                case NeoVector2Value vector:
                    return new JObject { ["x"] = vector.x, ["y"] = vector.y };
                case NeoColorValue color:
                    return new JObject { ["r"] = color.r, ["g"] = color.g, ["b"] = color.b, ["a"] = color.a };
                case NeoVariantReference variant:
                    return JToken.FromObject(variant.ToStoredValue());
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
                // An int can be -0 at runtime; only a float writes the symbol.
                case float or double when type?.type != MemberKind.Float:
                    return new JValue(Convert.ToDouble(value));
                case decimal exact:
                    return new JValue(NeoDecimalValues.Format(exact));
            }
            try
            {
                using var writer = new JTokenWriter();
                NeoStoredNumberJsonConverter.WriteTyped(writer, value, type, NeoStoredNumberJsonConverter.Serializer);
                return writer.Token;
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    $"Constructor argument {describeArgument} of runtime type '{value.GetType().FullName}' cannot be recorded as P75 creation data.",
                    error);
            }
        }

        private int virtualClassChildrenEpoch;

        /// <summary>
        /// Moves whenever any parent's virtual children change, so a reader
        /// can keep a resolved child id until then; -1 while a replay reads
        /// proposed state or a hook can read departed fields, when nothing may be kept.
        /// </summary>
        internal int VirtualClassChildrenEpoch =>
            candidateReplay is null && !isReplayingVirtualInstance
                && (departedReads == DepartedReads.None || departedNodes.Count == 0)
                ? virtualClassChildrenEpoch : -1;

        internal bool TryGetVirtualClassChildValueId(
            string parentValueId,
            string schemaKey,
            out string? childValueId)
        {
            childValueId = null;
            for (var graph = replayDefaultGraph; graph is not null; graph = graph.Parent)
                if (graph.TryGetChild(parentValueId, schemaKey, out childValueId))
                    return true;
            // Constructor bodies can read sparse shared catalog values that are
            // not constructor arguments. Resolve only the value actually read;
            // ordinary post-construction reads never trigger replay here.
            if (isReplayingVirtualInstance && !TryResolveVirtualClassChildren(parentValueId, out _))
                EnsureVirtualReplayArgumentReady(parentValueId);
            bool found = TryResolveVirtualClassChildren(
                    parentValueId,
                    out Dictionary<string, string>? children)
                && children.TryGetValue(schemaKey, out childValueId);
            // An unchanged parent can still point to a nested constructor
            // whose defaults this candidate is rebuilding. Prepare that root
            // before the caller resolves its otherwise hidden logical id.
            if (found && candidateReplay?.AffectedRoots.Contains(childValueId!) == true
                && !replayingVirtualRootIds.Contains(childValueId!))
                EnsureVirtualReplayArgumentReady(childValueId!);
            return found;
        }

        /// <summary>
        /// Drops every Immutable key of a finished runtime construction's
        /// Session graph, at every depth, so those members read the replay's
        /// per-instance values exactly as they will after a reload. Returns
        /// the Session rows the dropped keys owned.
        /// </summary>
        internal IReadOnlyCollection<string> ReplayImmutableMembers(string rootValueId)
        {
            List<ObjectMemberValue>? stripped = null;
            List<string>? childIds = null;
            var pending = new Stack<(string valueId, Member? member)>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            pending.Push((rootValueId, null));
            while (pending.Count != 0)
            {
                (string valueId, Member? member) = pending.Pop();
                if (!visited.Add(valueId) || !sessionData.values.TryGetValue(valueId, out MemberValue? row))
                    continue;
                if (row is not ObjectMemberValue { classId: not null, value: not null } instance)
                {
                    foreach (var link in EnumerateOwnedChildLinks(row, member))
                        pending.Push(link);
                    continue;
                }
                ObjectMemberValue? sparse = null;
                foreach (KeyValuePair<string, string> pair in instance.value)
                {
                    Member? child = TryResolveOwnedChildMember(instance, member, pair.Key);
                    if (child is null)
                        continue;
                    if (child.Storage != NeoMemberStorage.Immutable)
                    {
                        pending.Push((pair.Value, child));
                        continue;
                    }
                    // An authored value stays shared with the export.
                    if (data.values.ContainsKey(pair.Value))
                        continue;
                    sparse ??= (ObjectMemberValue)CloneRowForWrite(instance);
                    sparse.value!.Remove(pair.Key);
                    if (!string.IsNullOrEmpty(pair.Value))
                        (childIds ??= new List<string>()).Add(pair.Value);
                }
                if (sparse is null)
                    continue;
                // A member-wise row replays its own class initializers.
                if (!IsVirtualInstanceRoot(sparse))
                    StampConstructionProvenance(sparse, null, new Dictionary<string, JToken?>());
                (stripped ??= new List<ObjectMemberValue>()).Add(sparse);
            }
            if (stripped is null)
                return Array.Empty<string>();
            // Committing the sparse rows replays their roots, which serves the
            // dropped keys from the virtual layer.
            var plan = new NeoWritePlan(this);
            foreach (ObjectMemberValue sparse in stripped)
                plan.Set(NeoValueOwnership.Session, sparse, silent: true);
            plan.Commit();
            if (childIds is null)
                return Array.Empty<string>();
            var removed = new List<string>();
            foreach (string childId in childIds)
                removed.AddRange(RemoveTemporaryWritableValueGraph(NeoValueOwnership.Session, childId));
            return removed;
        }

        // Every declaration value is indexed under this one pseudo root, so a
        // full virtual rebuild retires them with the instance expansions.
        private const string ConstantDeclarationValuesRoot = "__neo_constant_declarations";
        // Null caches "no shared value": a member that is not a constant, or
        // a constant whose declared value is null.
        private readonly Dictionary<string, string?> constantDeclarationValueIds = new(StringComparer.Ordinal);
        // Every row of a shared declaration value.
        private readonly HashSet<string> constantDeclarationRowIds = new(StringComparer.Ordinal);

        /// <summary>
        /// Whether a stored key names a constant's own row that no longer
        /// exists. Saves from before 0.66.1 key each instance's replayed
        /// copy of its constants, which replay no longer creates, so such a
        /// key reads as absent.
        /// </summary>
        internal bool IsUnresolvedConstantKey(Member member, string valueId) =>
            !string.IsNullOrEmpty(valueId)
            && NeoGeneratedTypesSupport.MayBeConstant(member)
            && !TryGetValue(valueId, out MemberValue? _);

        /// <summary>
        /// The value a constant member reads on an instance that stores none.
        /// Its value is the same for every instance, so it is evaluated once
        /// per declaration and kept in the virtual layer as Asset data.
        /// </summary>
        internal string? ConstantDeclarationValueId(Member member)
        {
            // A member bound to its own value reads that binding.
            if (member.valueId is not null || !NeoGeneratedTypesSupport.MayBeConstant(member))
                return null;
            string identity = member.RuntimeDeclarationIdentity;
            if (constantDeclarationValueIds.TryGetValue(identity, out string? cached)
                && (cached is null || TryGetValue(cached, out MemberValue? _)))
            {
                return cached;
            }
            if (!NeoGeneratedTypesSupport.IsConstantMember(this, member))
            {
                constantDeclarationValueIds[identity] = null;
                return null;
            }
            var rows = new List<MemberValue>();
            string? valueId;
            using (var allocations = new ReplayAllocationScope(this))
            {
                valueId = NeoGeneratedTypesSupport.MaterializeDeclarationValue(this, member, rows);
                // Rows the initializer constructed belong to the value; the
                // scope reclaims them from Session.
                foreach (string allocated in allocations.Ids)
                    if (sessionData.values.TryGetValue(allocated, out MemberValue? row))
                        rows.Add(CloneRowForWrite(row));
            }
            foreach (MemberValue row in rows)
            {
                virtualValues[row.id] = row;
                virtualValueOwnership[row.id] = NeoValueOwnership.Asset;
                SyncValueNode(row.id);
                TrackVirtualValue(ConstantDeclarationValuesRoot, row.id);
                constantDeclarationRowIds.Add(row.id);
                if (!string.IsNullOrEmpty(row.containerId))
                    AddMembership(virtualEntriesByContainer, virtualContainerByRow, row.id, row.containerId!);
            }
            // An initializer can return an authored row; it is shared too.
            if (valueId is not null && data.values.ContainsKey(valueId))
                constantDeclarationRowIds.Add(valueId);
            constantDeclarationValueIds[identity] = valueId;
            return valueId;
        }

        /// <summary>
        /// Rejects a write to a constant's shared value, which every instance
        /// reads. Like any <c>readonly</c> or Immutable value, it is set only
        /// at construction.
        /// </summary>
        internal void ThrowIfConstantWrite(string id)
        {
            if (constantDeclarationRowIds.Count != 0 && constantDeclarationRowIds.Contains(id))
            {
                throw new InvalidOperationException(
                    $"Value '{id}' is a constant member's shared value, which is set only at construction.");
            }
        }

        /// <summary>
        /// P75: replays sparse instance roots against the current declaration
        /// defaults, then indexes their omitted rows under deterministic ids.
        /// The replay graph is temporary Session data; only immutable read
        /// rows and parent/schema-key links survive this method.
        /// </summary>
        private void InitializeVirtualInstanceValues(bool failClosed = true)
        {
            foreach (var step in InitializeVirtualInstanceValuesSteps(failClosed))
            {
            }
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
                    foreach (var step in InitializeVirtualInstanceValuesCore(failClosed))
                        yield return step;
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
            InvalidateSharedEvaluationContext();
            if (Random.HasStoredInstances)
                foreach (var entry in virtualValueOwnership)
                    ForgetRandomInstance(entry.Key, entry.Value);
            virtualValues.Clear();
            effectRescanPending |= instancesTracked;
            virtualValueOwnership.Clear();
            ClearVirtualValueNodes();
            foreach (var parents in virtualClassParentIdsByRoot.Values)
                foreach (var parent in parents)
                    InvalidateListenerOwner(parent.Value, parent.Key);
            virtualClassChildren.Clear();
            virtualClassChildrenEpoch++;
            virtualClassPlacementByChildId.Clear();
            virtualPlacementsVersion++;
            virtualLookupBindingsByMember.Clear();
            virtualEntriesByContainer.Clear();
            virtualContainerByRow.Clear();
            virtualValueIdsByRoot.Clear();
            constantDeclarationValueIds.Clear();
            constantDeclarationRowIds.Clear();
            virtualClassParentIdsByRoot.Clear();
            foreach (string expansionId in defaultListenerOwnersByExpansion.Keys.ToArray())
                ClearListenerDefaults(expansionId);
            virtualClassChildIdsByRoot.Clear();
            virtualFootprintByRoot.Clear();
            virtualRootByFootprintId.Clear();
            identitySensitiveExpansionRoots.Clear();
            nestedReplayBoundaries.Clear();
            nestedReplayRootsByOwner.Clear();
            constructorArgumentRootsByValueId.Clear();
            replayFieldsByValueId.Clear();
            replayIdentityIds.Clear();
            constructorArgumentValueIdsByRoot.Clear();
            // A user client replays only User roots and reads only User rows;
            // a save client leaves User roots to its user client (P104 §4.3).
            MemberValue[] allRows = (IsUserClient ? AuthoredUserRows() : data.values.Values)
                .Concat(PersistedData.values.Values)
                .Concat(sessionData.values.Values)
                .GroupBy(row => row.id, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            var parentByValueId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var step in BuildParentByValueIdSteps(allRows, parentByValueId))
                yield return step;
            ObjectMemberValue[] roots = allRows
                .OfType<ObjectMemberValue>()
                .Where(row => row.classId is not null)
                .Where(IsStoredClassDefaultRoot)
                .Where(ReplaysVirtualRootHere)
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
                if (!CanReplayVirtualInstanceRoot(root))
                    continue;
                pendingRoots.Remove(root.id);
                readyRoots.Enqueue(root);
            }
            // Saved unordered entries can move beneath a constructor-created
            // list. Its replay exposes the container, not the added entry.
            var waitingByContainer = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (ObjectMemberValue root in pendingRoots.Values)
            {
                if (string.IsNullOrEmpty(root.containerId))
                    continue;
                if (!waitingByContainer.TryGetValue(root.containerId, out var waiting))
                    waitingByContainer[root.containerId] = waiting = new List<string>();
                waiting.Add(root.id);
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
                    if (pendingRoots.Remove(classChildId, out ObjectMemberValue? nestedRoot))
                        readyRoots.Enqueue(nestedRoot);
                    if (waitingByContainer.Remove(classChildId, out var waiting))
                        foreach (string entryId in waiting)
                            if (pendingRoots.Remove(entryId, out ObjectMemberValue? entryRoot))
                                readyRoots.Enqueue(entryRoot);
                }
            }
            // Unreferenced authored rows are not part of a runtime graph.
            // Exports can retain detached historical children; do not execute
            // their constructors. Persisted save roots still fail closed.
            foreach (ObjectMemberValue root in pendingRoots.Values)
            {
                yield return 0;
                if (PersistedData.values.TryGetValue(root.id, out MemberValue? saved)
                    && saved is ObjectMemberValue currentRoot
                    && IsVirtualInstanceRoot(currentRoot))
                {
                    ExpandVirtualInstanceRootOrReport(currentRoot, failClosed);
                }
            }

            DisposeWrappersTouchingRows(outgoingVirtualIds);

            // The roots were created before replay so constructor and
            // variant code could resolve Assets/Save/Session/User. Rebind their
            // wrapper trees once the virtual child index is complete.
            RefreshAllVirtualWrapperTrees();
        }

        /// <summary>The authored rows of the User subtree, as the authored-ownership walk classified them.</summary>
        private IEnumerable<MemberValue> AuthoredUserRows()
        {
            foreach (var pair in authoredOwnership)
                if (pair.Value == NeoValueOwnership.User && data.values.TryGetValue(pair.Key, out MemberValue row))
                    yield return row;
        }

        /// <summary>Whether this client replays <paramref name="root"/>: User roots replay only in a user client.</summary>
        private bool ReplaysVirtualRootHere(ObjectMemberValue root)
        {
            bool user = userData.values.ContainsKey(root.id)
                || authoredOwnership.TryGetValue(root.id, out NeoValueOwnership ownership) && ownership == NeoValueOwnership.User;
            return user == IsUserClient;
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
                    && spineOwner != root.id)
                    return;
                if (!IsVirtualInstanceRoot(root))
                {
                    if (IsAuthoredPartialValue(root.id))
                        return;
                    if (!virtualFootprintByRoot.ContainsKey(root.id)
                        && ResolveInstanceSurfaceSchema(root.classId!).All(entry => root.value!.ContainsKey(entry.schemaKey)))
                        return;
                    if ((virtualClassChildren.ContainsKey(root.id)
                            && virtualRootByFootprintId.TryGetValue(root.id, out string? owner) && owner != root.id)
                        || !TryInferMemberForValueId(root.id, out Member? inferred)
                        || inferred is not ClassMember placement
                        || placement.Payload == NeoMemberPayloadKind.Partial)
                        return;
                }
                if (!sessionData.values.ContainsKey(root.id))
                    AssertPersistedVirtualInstanceRootIsClassPlacement(root);
                ExpandVirtualInstanceRoot(root);
            }
            catch (Exception error)
            {
                ClearVirtualInstanceRoot(root.id);
                // A stamped root's recipe is durable creation data, so a load
                // fails closed on it. An unstamped row only projects
                // declaration defaults onto the fields it omits: when they
                // cannot be evaluated, report the row and keep loading.
                if (failClosed && !IsVirtualInstanceRoot(root))
                {
                    ReportUnavailableOmittedDefaults(root, error);
                    return;
                }
                if (failClosed)
                    throw;
                Debug.LogWarning(
                    $"[NeoCompose] P75 could not replay instance root '{root.id}' of class '{root.classId}' from the incoming live content; its virtual values are unavailable until the next successful apply. {error}");
            }
        }

        private static void ReportUnavailableOmittedDefaults(ObjectMemberValue root, Exception error) =>
            Debug.LogError(
                $"[NeoCompose] Stored row '{root.id}' of class '{root.classId}' could not evaluate the defaults of the fields it omits, so those fields are unavailable. {error.Message}");

        private void IndexConstructorArgumentRows(ObjectMemberValue root)
        {
            RemoveConstructorArgumentRows(root.id);
            if (root.constructorArgs is null)
                return;
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
                if (!replayFieldsByValueId.TryGetValue(parent, out var fields))
                    replayFieldsByValueId[parent] = fields = new();
                fields.Add(valueId);
            }
            if (!constructorArgumentRootsByValueId.TryGetValue(indexKey, out var roots))
            {
                constructorArgumentRootsByValueId[indexKey] = roots = new HashSet<string>(StringComparer.Ordinal);
                if (valueId.StartsWith("identity:", StringComparison.Ordinal))
                    replayIdentityIds.Add(valueId.Substring(9));
            }
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
                    else if (valueId.StartsWith("identity:", StringComparison.Ordinal))
                    {
                        replayIdentityIds.Remove(valueId.Substring(9));
                    }
                }
            }
        }

        private void RefreshAllVirtualWrapperTrees()
        {
            RefreshVirtualWrapperTree(assets);
            RefreshVirtualWrapperTree(save);
            RefreshVirtualWrapperTree(session);
            RefreshVirtualWrapperTree(user);
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
            if (string.IsNullOrEmpty(root.classId))
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
        /// P75 §6 — records the variant <c>ToVariant</c> applied as the root's
        /// applied layer. The imperative Apply closure has already run; the
        /// layer's declarative halves stay virtual and are replayed here after
        /// their answered pins clear. Base retires the applied layer and never
        /// touches the variant that built the root.
        /// </summary>
        /// <returns>False when the root already resolves through this variant.</returns>
        internal bool StampVirtualInstanceVariant(
            NeoMemberClassWritable node,
            NeoValueOwnership ownership,
            string? variantId,
            string? rowValueId)
        {
            string valueId = node.value?.id
                ?? throw new InvalidOperationException(
                    "ToVariant receiver has no backing value row.");
            if (TryGetOverlaidValue(
                    ownership,
                    valueId,
                    out ObjectMemberValue? current)
                && ResolvesThroughVariant(current, variantId, rowValueId))
            {
                return false;
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
            root.instanceAppliedVariantId = variantId;
            root.instanceAppliedVariantRowValueId = rowValueId;
            // The applied layer is no eligibility marker of its own. A row
            // with no construction recipe gains the implicit pair, so it
            // expands and the layer replays over its own construction.
            StampConstructionProvenance(
                root,
                root.hasInstanceConstructorId ? root.instanceConstructorId : null,
                root.constructorArgs ?? new Dictionary<string, JToken?>());
            SetWritableValue(ownership, root, "instanceAppliedVariantId");
            if (IsPreparingVariant && !isReplayingVirtualInstance)
                RefreshPreparedVariant(node, root);
            else
                RefreshVirtualWrapperTree(node);
            return true;
        }

        /// <summary>
        /// Whether <paramref name="root"/> already resolves through the
        /// selection: its applied layer when it has one, otherwise the
        /// variant that built it. Base matches a root with no applied layer.
        /// </summary>
        private static bool ResolvesThroughVariant(
            ObjectMemberValue root,
            string? variantId,
            string? rowValueId)
        {
            if (root.instanceAppliedVariantId is not null)
            {
                return root.instanceAppliedVariantId == variantId
                    && root.instanceAppliedVariantRowValueId == rowValueId;
            }
            if (variantId is null)
                return true;
            return root.instanceVariantId == variantId
                && root.instanceVariantRowValueId == rowValueId;
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
            if (record is null)
                return;
            // A replay constructs a throwaway graph by calling straight back
            // into this path. Expanding it there would recurse without bound,
            // and its rows are discarded anyway.
            if (isReplayingVirtualInstance)
                return;
            ObjectMemberValue source = node.value
                ?? throw new InvalidOperationException(
                    $"Variant '{record.id}' produced a node with no backing value row.");
            var root = (ObjectMemberValue)CloneRowForWrite(source);
            root.instanceVariantId = record.id;
            root.instanceVariantRowValueId = lookupRowValueId;
            SetWritableValue(node.ownership, root, "instanceVariantId");
        }

        /// <summary>
        /// A root built by <c>Variants.X.Initialize()</c> carries its complete
        /// graph and is not expanded until something replays it. Reselecting
        /// that same variant replays nothing, yet still clears the pins its
        /// declarative layer answers; without this root's expansion, a pin
        /// under a materialized nested Class replays that nested row's own
        /// declaration default instead of the variant. Expand it first, as
        /// loading it does. A root inside any expansion, its own or an
        /// enclosing one, is already overlaid.
        /// </summary>
        internal void ExpandInitializedVariantRoot(
            NeoMemberClassWritable node,
            NeoValueOwnership ownership,
            string variantId,
            string? rowValueId)
        {
            if (node.value?.id is not string valueId
                || virtualRootByFootprintId.ContainsKey(valueId)
                || !TryGetOverlaidValue(ownership, valueId, out ObjectMemberValue? root)
                || root.instanceVariantId != variantId
                || root.instanceVariantRowValueId != rowValueId)
                return;
            ExpandVirtualInstanceRoot(root);
            RefreshVirtualWrapperTree(node);
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
            if (IsPreparingVariant && !isReplayingVirtualInstance)
                RefreshPreparedVariant(node, root);
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
            foreach (var step in BuildParentByValueIdSteps(rows, parentByValueId))
            {
            }
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
                    if (objectRow.value is not null)
                        foreach (string childId in objectRow.value.Values)
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
                if (arrayRow.value is null)
                    continue;
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
                    if (parentByValueId.TryAdd(childId, arrayRow.id))
                        continue;
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
                    if (priorLookup == true)
                        RemoveTentativeEdges(priorArray!);
                    if (currentLookup == true)
                        RemoveTentativeEdges(arrayRow);
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
                    .Concat(PersistedData.values.Values)
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
                    // Newly bound descendants already see the current expansion.
                    // Only wrappers retained from before the refresh can be stale.
                    var previousChildren = classNode.BoundChildren.ToArray();
                    classNode.RefreshChildrenAfterConstruction();
                    foreach (NeoMember child in previousChildren)
                        if (!child.isDisposed)
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
            try
            {
                ExpandVirtualInstanceRootCore(instanceRoot);
            }
            finally { replayingVirtualRootIds.Remove(instanceRoot.id); }
            // Effects its constructors' writes queued run once no replay encloses them.
            DrainDeferredEffects();
        }

        private void EnsureVirtualReplayArgumentReady(string valueId)
        {
            if (!virtualInstanceReplayReady || !isReplayingVirtualInstance
                || valueId == replayingVirtualInstanceRootId)
                return;
            if (candidateReplay is not null)
            {
                if (candidateReplay.AffectedRoots.Contains(valueId))
                    PrepareCandidateRoot(valueId);
                return;
            }
            if ((data.values.ContainsKey(valueId) || PersistedData.values.ContainsKey(valueId))
                && ResolveValueRow(valueId) is ObjectMemberValue root
                && IsStoredClassDefaultRoot(root)
                && !virtualValueIdsByRoot.ContainsKey(root.id)
                && CanReplayVirtualInstanceRoot(root))
                ExpandVirtualInstanceRootOrReport(root, failClosed: true);
        }

#if NEO_COMPOSE_PROFILING
        private static readonly Unity.Profiling.ProfilerMarker ReplayRootMarker = new("NeoCompose.Replay.Root");
        private static readonly Unity.Profiling.ProfilerMarker ReplayConstructMarker = new("NeoCompose.Replay.Construct");
        private static readonly Unity.Profiling.ProfilerMarker ReplayIndexMarker = new("NeoCompose.Replay.Index");
        private static readonly Unity.Profiling.ProfilerMarker ReplayOverlayMarker = new("NeoCompose.Replay.Overlay");
        private static readonly Unity.Profiling.ProfilerMarker ReplayCleanupMarker = new("NeoCompose.Replay.Cleanup");
#endif

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
            internal NeoChangeListenerMap? ExistingListenerDefaults;
            internal List<DefaultListenerProjection>? ExistingListenerProjections;
            internal NeoChangeListenerMap ListenerDefaults => ExistingListenerDefaults ??= new();
            internal List<DefaultListenerProjection> ListenerProjections => ExistingListenerProjections ??= new();

            internal ReplayAllocationScope(NeoClient client)
            {
                this.client = client;
                Parent = client.replayAllocationScope;
                client.replayAllocationScope = this;
            }

            public void Dispose()
            {
#if NEO_COMPOSE_PROFILING
                using var marker = ReplayCleanupMarker.Auto();
#endif
                client.replayAllocationScope = Parent;
                // Retire wrappers before removing rows so no listener can read
                // a partially reclaimed constructor graph.
                foreach (NeoMember node in Nodes)
                    if (!node.isDisposed && (node.overrideValueId ?? node.value?.id) is string id && Ids.Contains(id))
                        node.Dispose();
                if (client.candidateReplay is not null)
                {
                    foreach (string id in Ids)
                        client.candidateReplay.SetAllocation(id, null);
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

        /// <summary>Every replay of a sparse root, prepared or installed.</summary>
        internal int VirtualReplayCount
        {
            get;
            private set;
        }

        private PreparedVirtualExpansion ExpandVirtualInstanceRootCore(ObjectMemberValue instanceRoot, bool prepareOnly = false, NeoValueOwnership? replayOwnership = null, NestedReplayBoundary? preparedBoundary = null)
        {
            long identityReads = Random.IdentityReadCount;
            using var randomFrame = Random.EnterIdentity(instanceRoot.id);
#if NEO_COMPOSE_PROFILING
            using var marker = ReplayRootMarker.Auto();
#endif
            VirtualReplayCount++;
            using var nestedReplay = BeginNestedReplay();
            var replayBoundary = preparedBoundary;
            if (replayBoundary is null)
                nestedReplayBoundaries.TryGetValue(instanceRoot.id, out replayBoundary);
            if (replayBoundary is not null
                && !NeoSemanticJson.MemberRowsEqual(replayBoundary.Root, instanceRoot, ignoreObjectFields: true))
            {
                // A new recipe owns its initializers. Retain the enclosing id
                // namespace, but never reapply the prior call-site fields.
                replayBoundary = new NestedReplayBoundary
                {
                    Root = instanceRoot,
                    NamespaceRoot = replayBoundary.NamespaceRoot,
                    OwnerRootId = replayBoundary.OwnerRootId,
                    IncomingAnswers = replayBoundary.IncomingAnswers,
                    Path = replayBoundary.Path,
                    Ownership = replayOwnership ?? replayBoundary.Ownership,
                };
            }
            var dependencyIds = new HashSet<string>();
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
            // P127 §12.1: a root that isn't a binding node reads its class's
            // bindings through its typeSource, one hop away.
            IReadOnlyDictionary<string, TypeBinding>? rootStamp = instanceRoot.typeSource is not null
                ? NeoTypeSources.TypeSourcedClassRowStamp(this, instanceRoot, instanceRoot.classId!)
                : instanceRoot.genericBindings;
            IReadOnlyDictionary<string, TypeBinding>? replayClassArguments =
                NeoGenericResolution.CloseClassArgumentsFromStamp(
                    this,
                    rootStamp,
                    SlotClassArguments(placementMember, instanceRoot.classId!));
            NeoGeneratedTypesSupport.RuntimeConstructedClassValue constructed;
            bool wasReplaying = isReplayingVirtualInstance;
            string? previousReplayingRootId = replayingVirtualInstanceRootId;
            string? previousReplayingClassId = replayingVirtualInstanceClassId;
            IReadOnlyDictionary<string, TypeBinding>?
                previousReplayingClassArguments =
                    replayingVirtualInstanceClassArguments;
            IReadOnlyDictionary<string, TypeBinding>?
                previousReplayingGenericBindings =
                    replayingVirtualInstanceGenericBindings;
            HashSet<string>? previousAllocatedSessionValueIds =
                replayingVirtualInstanceAllocatedSessionValueIds;
            isReplayingVirtualInstance = true;
            replayingVirtualInstanceRootId = instanceRoot.id;
            replayingVirtualInstanceClassId = instanceRoot.classId;
            replayingVirtualInstanceClassArguments = replayClassArguments;
            replayingVirtualInstanceGenericBindings = rootStamp;
            // Nested replay starts after the outer constructor published its
            // temporary Session graph. Keep the outer boundary so those rows
            // still await their remaining member initializers.
            if (!wasReplaying)
                replayingVirtualInstanceAllocatedSessionValueIds = allocations.Ids;
            var captureReads = CaptureValueReads(dependencyIds);
            var replayDefaults = new ReplayDefaultGraph(this, instanceRoot,
                replayBoundary?.NamespaceRoot ?? instanceRoot, replayBoundary?.Path ?? "$", ownership);
            try
            {
#if NEO_COMPOSE_PROFILING
                using var constructMarker = ReplayConstructMarker.Auto();
#endif
                if (!IsVirtualInstanceRoot(instanceRoot))
                {
                    var declaration = (ClassMember)placementMember!.ShallowClone();
                    declaration.DeclaredClassId = instanceRoot.classId!;
                    if (!DerivesContentFromPlacementDefault(instanceRoot, declaration))
                        declaration.defaultValue = new ObjectMemberValueBase { value = new Dictionary<string, string>() };
                    constructed = NeoGeneratedTypesSupport.MaterializeStoredClassMemberDefault(
                        this, declaration, instanceRoot, declarationOnly: true);
                }
                else
                {
                    constructed = ReplayVirtualInstance(instanceRoot, placementMember, replayClassArguments);
                    if (replayBoundary?.IncomingAnswers is { } incoming)
                    {
                        declarativeReplayReceiver = replayBoundary.OwnerRootId;
                        try
                        {
                            NeoMemberClassWritable replayed = ReplayView(constructed);
                            incoming.Apply(this, replayed);
                            constructed = new NeoGeneratedTypesSupport.RuntimeConstructedClassValue(replayed.value!, replayed.member);
                        }
                        finally { declarativeReplayReceiver = null; }
                    }
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
                replayDefaults.Dispose();
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

            using var replayIdHash = SHA1.Create();
            var claimedVirtualIds = new Dictionary<string, string>(StringComparer.Ordinal);
            VirtualExpansionNode graph;
#if NEO_COMPOSE_PROFILING
            using (ReplayIndexMarker.Auto())
#endif
            {
                graph = IndexVirtualExpansion(
                    replayBoundary?.NamespaceRoot ?? instanceRoot,
                    expandedRoot,
                    constructed.member,
                    replayBoundary?.Path ?? "$",
                    claimedVirtualIds,
                    replayIdHash,
                    new Dictionary<MemberValue, IReadOnlyDictionary<string, NeoGenericEnvEntry>>());
            }
            if (replayBoundary is not null)
            {
                graph.virtualId = instanceRoot.id;
                RestoreNestedCallSiteFields(graph, replayBoundary);
            }
            var expansion = new PreparedVirtualExpansion(instanceRoot) { RootOwnership = ownership };
            foreach (string dependency in dependencyIds)
                if (dependency.StartsWith("static:", StringComparison.Ordinal)
                    || dependency.StartsWith("identity:", StringComparison.Ordinal)
                    || dependency.StartsWith("field:", StringComparison.Ordinal)
                    || (candidateReplay?.Allocations.ContainsKey(dependency) == true && !allocations.Ids.Contains(dependency))
                    || sessionData.values.ContainsKey(dependency)
                    || data.values.ContainsKey(dependency) || saveData.values.ContainsKey(dependency)
                    || userSource.userData.values.ContainsKey(dependency)
                    // A first-load projected leaf is just as live a dependency
                    // as an already-installed virtual or saved leaf.
                    || replayDefaults.ProjectsInScope(dependency)
                    || virtualValues.ContainsKey(dependency)
                    || candidateReplay?.Values.ContainsKey(dependency) == true)
                    expansion.Dependencies.Add(dependency);
#if NEO_COMPOSE_PROFILING
            using (ReplayOverlayMarker.Auto())
#endif
            {
                OverlaySparseInstance(
                    expansion,
                    graph,
                    instanceRoot.id,
                    ownership,
                    instanceRoot);
                if (allocations.ExistingListenerProjections is { Count: > 0 } projections)
                    ResolveConstructedListenerProjections(projections, () => graph);
                RemapVirtualReplayIdentities(graph, expansion, allocations.ExistingListenerDefaults);
                ComposeIndependentDeclarativeAnswers(graph, expansion);
                // P127 §12.1: the replay built its rows under an ownerless
                // temporary root. Place them under the instance's own root.
                Member rootMember = placementMember
                    ?? NeoGeneratedTypesSupport.UnplacedClassMember(this, instanceRoot.classId!, null, instanceRoot);
                NeoTypeSources.PlaceVirtual(
                    this,
                    instanceRoot,
                    rootMember,
                    expansion.Values,
                    expansion.ClassChildren,
                    () => ReplayParents(graph, instanceRoot.id, rootMember),
                    ownership);
                if (replayBoundary is not null)
                    expansion.Boundary = replayBoundary;
                else
                    PartitionNestedReplay(graph, expansion, instanceRoot, allocations.Ids);
            }
            if (Random.IdentityReadCount != identityReads)
                MarkIdentitySensitive(expansion);
            static void MarkIdentitySensitive(PreparedVirtualExpansion value)
            {
                value.IdentitySensitive = true;
                foreach (var nested in value.Nested)
                    MarkIdentitySensitive(nested);
            }
            if (prepareOnly)
                return expansion;
            var releasedVirtualIds = new HashSet<string>(StringComparer.Ordinal);
            if (virtualValueIdsByRoot.TryGetValue(
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
            ClearVirtualInstanceRoot(instanceRoot.id);
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

        /// <summary>
        /// P75 §6 — a stored root's expansion: its construction, then the
        /// declarative halves of the variant <c>ToVariant</c> last applied.
        /// </summary>
        private NeoGeneratedTypesSupport.RuntimeConstructedClassValue ReplayVirtualInstance(
            ObjectMemberValue root,
            ClassMember? placementMember,
            IReadOnlyDictionary<string, TypeBinding>? classArguments)
        {
            var constructed = ReplayVirtualConstruction(
                root,
                placementMember,
                classArguments);
            if (root.instanceAppliedVariantId is string appliedVariantId)
            {
                NeoMemberClassWritable node = ReplayView(constructed);
                NeoVariantSupport.ReplayAppliedVariant(this, appliedVariantId, node);
                return new NeoGeneratedTypesSupport.RuntimeConstructedClassValue(node.value!, node.member);
            }
            return constructed;
        }

        private NeoMemberClassWritable ReplayView(NeoGeneratedTypesSupport.RuntimeConstructedClassValue constructed) =>
            new(this, constructed.member, constructed.value.id, NeoValueOwnership.Session, bindsOnRead: true);

        private NeoGeneratedTypesSupport.RuntimeConstructedClassValue ReplayVirtualConstruction(
            ObjectMemberValue root,
            ClassMember? placementMember,
            IReadOnlyDictionary<string, TypeBinding>? classArguments)
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
                var node = NeoVariantSupport.InitializeNode(
                    this,
                    root.classId!,
                    variant,
                    lookupRow,
                    root.instanceVariantRowValueId);
                return new NeoGeneratedTypesSupport.RuntimeConstructedClassValue(node.value!, node.member);
            }

            if (DerivesContentFromPlacementDefault(root, placementMember))
            {
                using var randomFrame = Random.EnterIdentity(root.id);
                return NeoGeneratedTypesSupport.MaterializeStoredClassMemberDefault(
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
            // Replay's arguments arrive closed (stamp over placement), and
            // the server's virtual-instance graph reads them with no owner env.
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
                    bool present = stored.TryGetValue(parameterId, out JToken? value);
                    bool outArgument = NeoScript.NeoConstructorOut.Validate(argument, value, parameterId);
                    if (!present)
                    {
                        if (NeoParameterDefaults.HasDefault(argument))
                            continue;
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
                        outArgument ? NeoScript.NeoConstructorOut.Replay(this, replayType) : VirtualReplayArgument(value, replayType)));
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
            if (placementMember?.Payload == NeoMemberPayloadKind.Partial)
                return false;
            if (placementMember?.defaultValue?.value is not { } fields
                || (fields.Count == 0 && placementMember.defaultValue.changeListeners is not { Count: > 0 }))
                return false;
            string effectiveClassId = placementMember.RowClassId(placementMember.defaultValue.classId);
            if (effectiveClassId != root.classId)
                return false;
            if (root.instanceVariantId is not null)
                return false;
            if (root.instanceConstructorId is not null)
                return false;
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
            if (typeInfo is ClassTypeInfo variantType
                && NeoReferenceTypes.IsVariantClass(this, variantType.classId))
            {
                VariantRefValue? selection = token.ToObject<VariantRefValue>();
                if (selection is null)
                {
                    throw new InvalidOperationException("Constructor variant argument has no selection.");
                }
                if (string.IsNullOrEmpty(selection.classId))
                {
                    throw new InvalidOperationException("Constructor variant argument has no target class id.");
                }
                return new NeoVariantReference(selection.classId, selection.variantId, selection.rowValueId);
            }
            if (typeInfo.type == MemberKind.Generic)
                throw new InvalidOperationException($"Constructor parameter \"{(typeInfo as FunctionArgumentTypeInfo)?.name}\" replays with an unclosed type parameter.");
            if (token.Type == JTokenType.String
                && NeoTypeSources.ConstructorArgumentStoresRow(typeInfo))
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
                // Decode aggregates in the constructor context so nested row views keep their reference metadata.
                return new NeoConstructorValueReference(valueId, ownership);
            }
            return typeInfo.type switch
            {
                MemberKind.Int => token.ToObject<int>(),
                MemberKind.Float => StoredFloatArgument(token),
                MemberKind.Decimal => StoredDecimalArgument(token),
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
                // Float entries and tuple elements read back by their type.
                _ => NeoStoredNumberJsonConverter.ReadTyped(NeoJsonRuntimeValue.From(token), typeInfo),
            };
        }

        /// <summary>A float argument's stored payload (P127 §12.1): a finite number or a symbol string.</summary>
        internal static double StoredFloatArgument(JToken token)
        {
            if (!NeoStoredFloat.TryDecode(token, out double value))
            {
                throw new InvalidOperationException(
                    $"Constructor argument '{token}' must be a finite number or one of \"NaN\", \"Infinity\", \"-Infinity\" and \"-0\".");
            }
            return value;
        }

        /// <summary>
        /// A decimal argument as the evaluator holds it: its canonical
        /// string (P127 §3.4). A number was written by an SDK before P127 as
        /// the decimal's double, so it reads back as that double's decimal.
        /// </summary>
        internal static string StoredDecimalArgument(JToken token)
        {
            if (token.Type is JTokenType.Integer or JTokenType.Float)
                return NeoDecimalValues.Format((decimal)token.Value<double>());
            if (token.Type != JTokenType.String || NeoDecimalValues.GetViolation(token.Value<string>()!) != NeoDecimalValues.Violation.None)
            {
                throw new InvalidOperationException(
                    $"Constructor argument '{token}' must be a canonical decimal string.");
            }
            return token.Value<string>()!;
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

        private VirtualExpansionNode IndexVirtualExpansion(
            ObjectMemberValue instanceRoot,
            MemberValue row,
            Member member,
            string path,
            Dictionary<string, string> claimedVirtualIds,
            SHA1 replayIdHash,
            Dictionary<MemberValue, IReadOnlyDictionary<string, NeoGenericEnvEntry>> environments,
            IReadOnlyDictionary<string, MemberValue>? identitySources = null)
        {
            MemberValue identityRow = identitySources?.GetValueOrDefault(row.id) ?? row;
            string sourceIdentity = VirtualSourceIdentity(identityRow, member, path);
            string virtualId = path == "$"
                ? instanceRoot.id
                : VirtualValueId(instanceRoot.id, sourceIdentity, replayIdHash);
            if (claimedVirtualIds.TryGetValue(
                    virtualId,
                    out string? claimedPath)
                && claimedPath != path)
            {
                sourceIdentity = $"{sourceIdentity}:{path}";
                virtualId = VirtualValueId(instanceRoot.id, sourceIdentity, replayIdHash);
            }
            claimedVirtualIds[virtualId] = path;
            // The slot controls ownership; a box's held type controls its child graph.
            Member payloadMember = row.valueType is { } heldType
                ? TypeRules.Box(heldType).Member ?? member
                : member;
            var node = new VirtualExpansionNode
            {
                row = row,
                member = member,
                payloadMember = payloadMember,
                path = path,
                virtualId = virtualId,
                identityId = virtualId,
                identity = sourceIdentity,
                identityRoot = instanceRoot.id,
            };

            // Replay rows live in temporary Session storage, including immutable
            // members. Read the constructed graph directly: read-only wrappers
            // select Asset storage and can omit those temporary children.
            if (payloadMember is ClassMember classMember && row is ObjectMemberValue { value: not null } classRow)
            {
                string classId = classMember.RowClassId(classRow.classId);
                foreach (var entry in ResolveInstanceSurfaceSchema(classId))
                {
                    bool constructed = classRow.value.TryGetValue(entry.schemaKey, out string childId);
                    if (!constructed && classMember.Payload == NeoMemberPayloadKind.Partial)
                        continue;
                    if (!TryGetMember(entry.memberId, out Member? declaration)
                        || TryResolveOwnedChildMember(classRow, classMember, entry.schemaKey, environments, declaration) is not Member childMember)
                        continue;
                    if (constructed)
                        node.classChildren[entry.schemaKey] = Child(childId, childMember,
                            AppendVirtualPath(path, "class", "schemaKey", entry.schemaKey));
                    // A null Class default is the one declared default a
                    // construction stores no row for. A slot that can hold
                    // another store's value needs the deterministic id a write
                    // materializes under: under an Asset record that id is the
                    // only place a Save or Session overlay row can live. An
                    // inheriting slot shares its parent's store, so the parent
                    // links its writes, and an immutable one rejects them.
                    else if (CanHoldOverlayClassValue(childMember))
                        node.classChildren[entry.schemaKey] = NullClassChild(childMember,
                            AppendVirtualPath(path, "class", "schemaKey", entry.schemaKey));
                    else
                        continue;
                    node.classChildren[entry.schemaKey].schemaKey = entry.schemaKey;
                }
            }
            else if (payloadMember is TupleMember tuple && row is ArrayMemberValue tupleRow && tupleRow.value is not null)
            {
                for (int index = 0; index < tupleRow.value.Length; index++)
                {
                    Member element = TryResolveTupleElementMember(tuple, row, index)
                        ?? throw new InvalidOperationException($"Missing tuple element declaration at {index}.");
                    node.listChildren.Add(Child(tupleRow.value[index], element, AppendVirtualPath(path, "list", "index",
                        index.ToString(System.Globalization.CultureInfo.InvariantCulture), numericValue: true)));
                }
            }
            else if (payloadMember is ListMember list && row is ArrayMemberValue array)
            {
                if (TryResolveCollectionEntryMember(list, row) is not Member entry)
                    throw new InvalidOperationException($"Missing list entry declaration '{list.entryMemberId}'.");
                IEnumerable<string> ids = IsUnorderedList(list) ? GetUnorderedListEntryIds(row.id) : array.value ?? Array.Empty<string>();
                int index = 0;
                foreach (string id in ids)
                    node.listChildren.Add(Child(id, entry, AppendVirtualPath(path, "list", "index",
                        (index++).ToString(System.Globalization.CultureInfo.InvariantCulture), numericValue: true)));
            }
            else if (payloadMember is DictionaryMember dictionary && row is ObjectMemberValue entries)
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
                    childMember, childPath, claimedVirtualIds, replayIdHash, environments, identitySources);
                child.parent = node;
                return child;
            }
            VirtualExpansionNode NullClassChild(Member childMember, string childPath)
            {
                // The null row has no replay identity of its own, so its id is
                // positional, and it takes that id once indexing derives it.
                MemberValue nullRow = MemberValueFactory.CreateFromDefault(
                    childMember, string.Empty, instanceRoot.createdAt, instanceRoot.updatedAt)!;
                var child = IndexVirtualExpansion(instanceRoot, nullRow, childMember, childPath,
                    claimedVirtualIds, replayIdHash, environments, identitySources);
                nullRow.id = child.virtualId;
                child.parent = node;
                return child;
            }
            return node;
        }

        private static bool CanHoldOverlayClassValue(Member member)
        {
            return member.Storage is not (NeoMemberStorage.Inherit or NeoMemberStorage.Immutable)
                && member.Mutability != NeoMemberMutabilityKind.ReadOnly
                && MemberValueFactory.IsLiteralNullClassDefault(member);
        }

        /// <summary>
        /// Points replayed delegate receivers and P112 reference selections at
        /// the effective rows their replay's temporary rows stand for.
        /// </summary>
        private void RemapVirtualReplayIdentities(VirtualExpansionNode root, PreparedVirtualExpansion expansion,
            NeoChangeListenerMap? listenerDefaults)
        {
            var identities = new Dictionary<string, string>();
            var scopes = listenerDefaults is { Count: > 0 } ? new Dictionary<string, NeoValueOwnership>() : null;
            var selectors = new List<DelegateMemberValue>();
            var selections = new List<ArrayMemberValue>();
            var constructions = new List<(ObjectMemberValue row, ClassMember? member)>();
            var pending = new Stack<VirtualExpansionNode>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                VirtualExpansionNode node = pending.Pop();
                if (node.effectiveId is null)
                    continue;
                identities[node.row.id] = node.effectiveId;
                if (scopes is not null)
                    scopes[node.row.id] = node.effectiveOwnership;
                if (expansion.Values.TryGetValue(node.effectiveId, out MemberValue? row))
                {
                    if (row is ObjectMemberValue { constructorArgs: { Count: > 0 } } construction)
                        constructions.Add((construction, node.member as ClassMember));
                    if (row is DelegateMemberValue selector)
                        selectors.Add(selector);
                    else if (node.payloadMember is LookupMember && row is ArrayMemberValue { value: not null } selection)
                        selections.Add(selection);
                }
                foreach (var child in node.classChildren.Values)
                    pending.Push(child);
                foreach (var child in node.listChildren)
                    pending.Push(child);
                foreach (var child in node.dictionaryChildren.Values)
                    pending.Push(child);
            }
            // Replay's temporary rows are removed next. Preserve bindings to the
            // effective stored/virtual instance, including forward sibling targets.
            foreach (DelegateMemberValue selector in selectors)
            {
                if (selector.value?.valueId is { } receiver
                    && identities.TryGetValue(receiver, out string? effective))
                    selector.value.valueId = effective;
            }
            foreach (ArrayMemberValue selection in selections)
            {
                string[] ids = (string[])selection.value!.Clone();
                for (int index = 0; index < ids.Length; index++)
                {
                    if (identities.TryGetValue(ids[index], out string? effective))
                        ids[index] = effective;
                }
                selection.value = ids;
            }
            string? Resolve(string id) => identities.GetValueOrDefault(id);
            foreach (var (row, member) in constructions)
                RemapConstructedReferences(row, member, Resolve);
            if (scopes is null)
                return;
            foreach (var owner in listenerDefaults!)
            {
                if (!identities.TryGetValue(owner.Key, out string? effectiveOwner))
                    continue;
                var entry = new Dictionary<string, NeoDelegateValue[]>(StringComparer.Ordinal);
                foreach (var member in owner.Value)
                {
                    var targets = new NeoDelegateValue[member.Value.Length];
                    for (int index = 0; index < targets.Length; index++)
                    {
                        NeoDelegateValue target = member.Value[index].PersistedCopy();
                        if (target.valueId is string receiver && identities.TryGetValue(receiver, out string? effectiveReceiver))
                            target.valueId = effectiveReceiver;
                        targets[index] = target;
                    }
                    entry[member.Key] = targets;
                }
                expansion.ListenerDefaults[(scopes[owner.Key], effectiveOwner)] = entry;
            }
        }

        private void RemapConstructedReferences(ObjectMemberValue row, ClassMember? member,
            Func<string, string?> resolve)
        {
            if (row.instanceConstructorId is not string constructorId
                || !data.constructors.TryGetValue(constructorId, out var constructor))
                return;
            IReadOnlyDictionary<string, NeoGenericEnvEntry>? environment = null;
            for (int index = 0; index < constructor.argumentTypes.Length; index++)
            {
                string parameterId = ConstructorParameterId(constructor, index);
                if (!row.constructorArgs!.TryGetValue(parameterId, out var argument)
                    || argument?.Type != JTokenType.String)
                    continue;
                if (resolve(argument.Value<string>()!) is not { } effective)
                    continue;
                environment ??= NeoGenericResolution.SlotRowEnv(this, member, null, row.genericBindings, constructor.classId);
                var parameter = NeoNSFunctionRuntime.ResolveInvocationTypeInfo(this,
                    constructor.argumentTypes[index], environment);
                if (parameter.type == MemberKind.Reference)
                    row.constructorArgs[parameterId] = effective;
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
            if (node.parent?.payloadMember is ListMember parentList
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
                    else if (node.payloadMember is DictionaryMember)
                    {
                        obj.value = node.dictionaryChildren.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value.virtualId);
                    }
                    break;
                case ArrayMemberValue array when array.value is not null:
                    if (node.payloadMember is TupleMember || node.payloadMember is ListMember list && !IsUnorderedList(list))
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
            internal readonly Dictionary<string, NeoValueOwnership> ClassParentOwnership = new();
            internal readonly Dictionary<(NeoValueOwnership scope, string ownerId), Dictionary<string, NeoDelegateValue[]>> ListenerDefaults = new();
            internal readonly Dictionary<string, VirtualClassPlacement> Placements = new();
            internal readonly HashSet<string> Footprint = new();
            internal readonly HashSet<string> Dependencies = new();
            internal readonly List<PreparedVirtualExpansion> Nested = new();
            internal NestedReplayBoundary? Boundary;
            internal bool IdentitySensitive;

            // The ownership a root expansion replayed under.
            internal NeoValueOwnership RootOwnership;

            internal PreparedVirtualExpansion(ObjectMemberValue root) => Root = root;

            internal void TrackPlacement(string parentId, string childId, Member member, NeoValueOwnership ownership, NeoValueOwnership parentOwnership, string path, string? schemaKey = null, VirtualExpansionNode? node = null)
            {
                Placements[childId] = new VirtualClassPlacement
                {
                    rootId = Root.id,
                    identity = childId == node?.identityId ? node.identity : null,
                    identityRoot = childId == node?.identityId ? node.identityRoot : null,
                    path = path,
                    parentValueId = parentId,
                    schemaKey = schemaKey,
                    member = member,
                    ownership = ownership,
                    parentOwnership = parentOwnership,
                };
            }
        }

        /// <summary>Each replayed row's parent row and that parent's member, by row id.</summary>
        private static Dictionary<string, (string ParentId, Member ParentMember)> ReplayParents(
            VirtualExpansionNode graph, string rootId, Member rootMember)
        {
            var parents = new Dictionary<string, (string ParentId, Member ParentMember)>(StringComparer.Ordinal);
            var pending = new Stack<VirtualExpansionNode>();
            pending.Push(graph);
            while (pending.Count > 0)
            {
                VirtualExpansionNode node = pending.Pop();
                string nodeId = node.effectiveId ?? node.virtualId;
                Member nodeMember = nodeId == rootId ? rootMember : node.member;
                foreach (VirtualExpansionNode child in Children(node))
                {
                    parents[child.effectiveId ?? child.virtualId] = (nodeId, nodeMember);
                    pending.Push(child);
                }
            }
            return parents;

            static IEnumerable<VirtualExpansionNode> Children(VirtualExpansionNode node)
            {
                foreach (VirtualExpansionNode child in node.classChildren.Values)
                    yield return child;
                foreach (VirtualExpansionNode child in node.listChildren)
                    yield return child;
                foreach (VirtualExpansionNode child in node.dictionaryChildren.Values)
                    yield return child;
            }
        }

        private void InstallVirtualExpansion(PreparedVirtualExpansion expansion, bool includeNested = true)
        {
            string rootId = expansion.Root.id;
            if (expansion.IdentitySensitive)
                identitySensitiveExpansionRoots.Add(rootId);
            foreach (var pair in expansion.Values)
                NoteDeparture(pair.Key, RowLayer.Virtual, pair.Value);
            foreach (var pair in expansion.Values)
            {
                virtualValues[pair.Key] = pair.Value;
                InvalidateListenerOwner(expansion.Ownership[pair.Key], pair.Key);
                NoteEffectRowChange(expansion.Ownership[pair.Key], pair.Key, pair.Value);
                EvictSharedEvaluationRow(pair.Key);
                virtualValueOwnership[pair.Key] = expansion.Ownership[pair.Key];
                SyncValueNode(pair.Key);
                TrackVirtualValue(rootId, pair.Key);
                if (!string.IsNullOrEmpty(pair.Value.containerId))
                    AddMembership(virtualEntriesByContainer, virtualContainerByRow, pair.Key, pair.Value.containerId!);
            }
            foreach (var pair in expansion.ClassChildren)
            {
                virtualClassChildren[pair.Key] = pair.Value;
                NeoValueOwnership parentOwnership = expansion.ClassParentOwnership[pair.Key];
                InvalidateListenerOwner(parentOwnership, pair.Key);
                TrackVirtualClassParent(rootId, pair.Key, parentOwnership);
            }
            virtualClassChildrenEpoch++;
            foreach (var pair in expansion.Placements)
            {
                TrackVirtualClassPlacement(rootId, pair.Value.parentValueId, pair.Key, pair.Value.member, pair.Value.ownership, pair.Value.parentOwnership, pair.Value.path, pair.Value.schemaKey, pair.Value.identity, pair.Value.identityRoot);
                // An authored row at a writable root's virtual id is that root's
                // materialized spine, which the authored walk's links don't reach.
                if (pair.Value.ownership != NeoValueOwnership.Asset
                    && data.values.ContainsKey(pair.Key)
                    && authoredOwnership.TryAdd(pair.Key, pair.Value.ownership))
                {
                    authoredOwnershipEpoch++;
                }
            }
            foreach (string id in expansion.Footprint)
                TrackVirtualFootprint(rootId, id);
            IndexConstructorArgumentRows(expansion.Root);
            foreach (string id in expansion.Dependencies)
                TrackReplayDependency(rootId, id);
            if (expansion.Boundary is not null)
                InstallNestedReplayBoundary(expansion.Boundary);
            InstallListenerDefaults(expansion);
            if (includeNested)
                foreach (var nested in expansion.Nested)
                    InstallVirtualExpansion(nested);
        }

        private void OverlaySparseInstance(
            PreparedVirtualExpansion expansion,
            VirtualExpansionNode node,
            string? materializedId,
            NeoValueOwnership ownership,
            ObjectMemberValue instanceRoot)
        {
            node.effectiveOwnership = ownership;
            // Removed fields are intentionally absent, not missing defaults.
            // Do not recreate their virtual descendants or keep them reachable.
            if (TryGetWritableValue(ownership, materializedId ?? node.virtualId, out MemberValue? storedRow)
                && storedRow.IsRemoved)
                return;
            MemberValue? materialized = null;
            if (materializedId is not null)
                TryGetReplayOverlay(ownership, materializedId, out materialized);
            // Sparse class spines are stored at their deterministic virtual
            // ids without requiring every ancestor body to point at them.
            // Probe that stable id before treating the whole subtree as
            // virtual; otherwise a stored empty Class row hides all deeper
            // virtual links and truncates web-authored overrides in Unity.
            if (materialized is null
                && materializedId != node.virtualId)
            {
                if (TryGetWritableValue(ownership, node.virtualId, out MemberValue? fallbackRow)
                    && fallbackRow.IsRemoved)
                    return;
                TryGetReplayOverlay(ownership, node.virtualId, out materialized);
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
                expansion.TrackPlacement(node.parent.effectiveId, node.effectiveId, node.member, ownership, node.parent.effectiveOwnership, node.path, node.schemaKey, node);
            if (materialized.id != instanceRoot.id
                && materialized is ObjectMemberValue nestedRoot
                && IsVirtualInstanceRoot(nestedRoot)
                && !IsMaterializedSpine(nestedRoot, node))
            {
                node.independentRoot = nestedRoot;
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
                            expansion.ClassParentOwnership[effectiveId] = ownership;
                        }
                        links[pair.Key] = pair.Value.virtualId;
                        if (pair.Value.member is not null)
                        {
                            expansion.TrackPlacement(
                                effectiveId,
                                pair.Value.virtualId,
                                pair.Value.member,
                                childOwnership,
                                ownership,
                                pair.Value.path,
                                pair.Key, pair.Value);
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

            if (node.payloadMember is ListMember orderedList && !IsUnorderedList(orderedList)
                && (materialized is null || materialized.id == node.virtualId))
            {
                // Sparse lists retain their virtual entry identities after removals
                // and insertions. Replaying by index would apply another entry's defaults.
                string[]? stored = (materialized as ArrayMemberValue)?.value;
                var membership = stored is null ? null : new HashSet<string>(stored, StringComparer.Ordinal);
                foreach (var child in node.listChildren)
                {
                    if (membership is null || membership.Contains(child.virtualId))
                        OverlaySparseInstance(expansion, child, membership is null ? null : child.virtualId, ownership, instanceRoot);
                }
                return;
            }
            // Fully materialized constructor lists have fresh row IDs. Their
            // authored child overrides still correspond by constructor position.
            if (node.payloadMember is TupleMember || node.payloadMember is ListMember list && !IsUnorderedList(list))
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
            if (node.payloadMember is ListMember unorderedList
                && IsUnorderedList(unorderedList))
            {
                var materializedBySource = new Dictionary<string, Queue<string>>(
                    StringComparer.Ordinal);
                foreach (string entryId in GetUnorderedListEntryIds(effectiveId))
                {
                    MemberValue? entry = ResolveValueRow(entryId);
                    if (string.IsNullOrEmpty(entry?.sourceValueId))
                        continue;
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
                        && TryGetReplayOverlay(ownership, child.virtualId, out _))
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
            if (node.payloadMember is DictionaryMember)
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
            node.effectiveOwnership = ownership;
            // Collection entries need their closed placement just as class
            // fields do, including when a candidate replay hides the old graph.
            if (node.parent is not null)
                expansion.TrackPlacement(node.parent.effectiveId, node.virtualId, node.member, ownership, node.parent.effectiveOwnership, node.path, node.schemaKey, node);
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
                        childOwnership,
                        ownership,
                        child.path,
                        child.schemaKey, child);
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
        /// An authored row that names another class at this id is a fresh
        /// construction of that class (P113 §4.2), so it isn't a spine either.
        /// </summary>
        private bool IsMaterializedSpine(ObjectMemberValue stored, VirtualExpansionNode node)
        {
            if (stored.id != node.virtualId || !SameClass(stored, node) || !SameVariantSelection(stored, node.row))
                return false;
            if ((stored.constructorArgs?.Count ?? 0) == 0 && (node.row.constructorArgs?.Count ?? 0) == 0)
                return stored.instanceConstructorId == node.row.instanceConstructorId;
            var replayed = RewriteVirtualRow(node, stored);
            return stored.instanceConstructorId == replayed.instanceConstructorId
                && NeoSemanticJson.MapsEqual(stored.constructorArgs, replayed.constructorArgs);
        }

        private static bool SameClass(ObjectMemberValue stored, VirtualExpansionNode node)
        {
            if (node.member is not ClassMember slot)
                return true;
            return slot.RowClassId(stored.classId) == slot.RowClassId(node.row.classId);
        }

        /// <summary>
        /// P75 §6 — whether <paramref name="valueId"/> lies inside another
        /// root's expansion, as a virtual row or a materialized spine. Its
        /// construction is that root's, so a variant applies to it in place.
        /// </summary>
        internal bool IsInsideAnotherExpansion(string valueId)
            => virtualRootByFootprintId.TryGetValue(valueId, out string? owner) && owner != valueId;

        private static bool SameVariantSelection(MemberValue left, MemberValue right)
            => left.instanceVariantId == right.instanceVariantId
                && left.instanceVariantRowValueId == right.instanceVariantRowValueId
                && left.instanceAppliedVariantId == right.instanceAppliedVariantId
                && left.instanceAppliedVariantRowValueId == right.instanceAppliedVariantRowValueId;

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
                        || userSource.userData.values.TryGetValue(valueId, out parent)
                        || sessionData.values.TryGetValue(valueId, out parent)
                        || TryResolveVirtualValue(valueId, out parent))
                    && parent is ObjectMemberValue parentObject)
                {
                    parentClassId = parentObject.classId;
                }
                foreach (var pair in links)
                {
                    if (CandidateSeversVirtualId(pair.Value, valueId))
                        continue;
                    Member? linkMember = null;
                    if (parentClassId is not null)
                    {
                        try
                        {
                            foreach (MergedSchemaEntry entry
                                in ResolveInstanceSurfaceSchema(parentClassId))
                            {
                                if (entry.schemaKey != pair.Key)
                                    continue;
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
                if (CandidateSeversVirtualId(expansionId, valueId))
                    continue;
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
        /// Whether the candidate plan unlinks <paramref name="valueId"/>, or
        /// an ancestor below <paramref name="rootId"/>, from that root's
        /// expansion. The committed expansion still links a replaced child
        /// until the write installs, which would keep its overrides alive.
        /// </summary>
        private bool CandidateSeversVirtualId(string valueId, string rootId)
        {
            if (candidateReadPlan is null)
                return false;
            var state = (candidateReadPlan, candidateReadPlan.Version, virtualPlacementsVersion, WriteRevision, userSource.WriteRevision);
            if (!severedVirtualIdsState.Equals(state))
            {
                severedVirtualIdsState = state;
                severedVirtualIds.Clear();
            }
            // Ids an expansion enumerates share ancestors, so each one is
            // walked once: every id on the walk shares its answer.
            int start = severedVirtualIdsPath.Count;
            bool severed = false;
            string id = valueId;
            while (id != rootId && virtualClassPlacementByChildId.TryGetValue(id, out VirtualClassPlacement? placement))
            {
                if (severedVirtualIds.TryGetValue((id, rootId), out severed))
                    break;
                severedVirtualIdsPath.Add(id);
                if (!CandidateRetainsVirtualParent(id, placement))
                {
                    severed = true;
                    break;
                }
                id = placement.parentValueId;
            }
            for (int i = start; i < severedVirtualIdsPath.Count; i++)
                severedVirtualIds[(severedVirtualIdsPath[i], rootId)] = severed;
            severedVirtualIdsPath.RemoveRange(start, severedVirtualIdsPath.Count - start);
            return severed;
        }

        // CandidateSeversVirtualId's answers, valid while the plan, its
        // version, the placements, and the stores they resolve against hold.
        private readonly Dictionary<(string valueId, string rootId), bool> severedVirtualIds = new();
        private (NeoWritePlan? plan, int version, int placements, long own, long user) severedVirtualIdsState;
        private readonly List<string> severedVirtualIdsPath = new();
        private int virtualPlacementsVersion;

        /// <summary>
        /// Virtual storage boundaries are independent writable roots: a slot
        /// a sparse expansion places under a parent another store owns. The
        /// slot is a root whether it is still virtual or a write materialized
        /// it at its deterministic id, since no stored body links either.
        /// Ordinary descendants remain reachable only through their owning
        /// graph.
        /// </summary>
        private IEnumerable<(string valueId, Member member)> VirtualStorageBoundaries(
            NeoValueOwnership ownership)
        {
            // A candidate replay can re-place a committed slot, so only its
            // own placement settles the ownership filter.
            foreach (var pair in virtualClassPlacementByChildId)
                if ((candidateReplay is not null || pair.Value.ownership == ownership)
                    && IsVirtualStorageBoundary(pair.Key, ownership, out VirtualClassPlacement placement))
                    yield return (pair.Key, placement.member);
            if (candidateReplay is null)
                yield break;
            foreach (var pair in candidateReplay.Placements)
                if (pair.Value.ownership == ownership
                    && !virtualClassPlacementByChildId.ContainsKey(pair.Key)
                    && IsVirtualStorageBoundary(pair.Key, ownership, out VirtualClassPlacement placement))
                    yield return (pair.Key, placement.member);
        }

        private bool IsVirtualStorageBoundary(
            string id,
            NeoValueOwnership ownership,
            out VirtualClassPlacement placement)
        {
            return TryResolveVirtualPlacement(id, out placement)
                && placement.ownership == ownership
                && TryGetValueOwnership(placement.parentValueId, out NeoValueOwnership parentOwnership)
                && parentOwnership != ownership;
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

        private void TrackVirtualClassParent(string rootId, string parentId, NeoValueOwnership ownership)
        {
            if (!virtualClassParentIdsByRoot.TryGetValue(rootId, out var parents))
                virtualClassParentIdsByRoot[rootId] = parents = new(StringComparer.Ordinal);
            parents[parentId] = ownership;
        }

        private void TrackVirtualClassPlacement(
            string rootId,
            string parentValueId,
            string childValueId,
            Member member,
            NeoValueOwnership ownership,
            NeoValueOwnership parentOwnership,
            string path,
            string? schemaKey, string? identity = null, string? identityRoot = null)
        {
            if (virtualClassPlacementByChildId.TryGetValue(childValueId, out var previousPlacement))
                RemoveVirtualLookupBinding(previousPlacement.member.id, childValueId);
            if (!virtualLookupBindingsByMember.TryGetValue(member.id, out var bindings))
                virtualLookupBindingsByMember[member.id] = bindings = new(StringComparer.Ordinal);
            bindings.Add(childValueId);
            virtualPlacementsVersion++;
            virtualClassPlacementByChildId[childValueId] =
                new VirtualClassPlacement
                {
                    rootId = rootId,
                    identity = identity,
                    identityRoot = identityRoot,
                    path = path,
                    parentValueId = parentValueId,
                    schemaKey = schemaKey,
                    member = member,
                    ownership = ownership,
                    parentOwnership = parentOwnership,
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
            // A nested root that expands itself keeps its own mapping, as the
            // full rebuild leaves it. An enclosing root replayed later must not
            // take it over, or the nested root's next write skips it as a spine
            // and drops its virtual children.
            if (ids.Add(valueId)
                && (valueId == rootId || !virtualFootprintByRoot.ContainsKey(valueId)))
                virtualRootByFootprintId[valueId] = rootId;
        }

        private void ClearVirtualInstanceRoot(string rootId)
        {
            identitySensitiveExpansionRoots.Remove(rootId);
            ClearListenerDefaults(rootId);
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
                    NoteDeparture(valueId, RowLayer.Virtual, null);
                foreach (string valueId in valueIds)
                {
                    InvalidateListenerOwner(virtualValueOwnership.GetValueOrDefault(valueId), valueId);
                    ForgetRandomInstance(valueId, virtualValueOwnership.GetValueOrDefault(valueId));
                    virtualValues.Remove(valueId);
                    NoteEffectRowChange(virtualValueOwnership.GetValueOrDefault(valueId), valueId, null);
                    EvictSharedEvaluationRow(valueId);
                    virtualValueOwnership.Remove(valueId);
                    SyncValueNode(valueId);
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
                    out Dictionary<string, NeoValueOwnership>? parentIds))
            {
                foreach (var parent in parentIds)
                {
                    InvalidateListenerOwner(parent.Value, parent.Key);
                    virtualClassChildren.Remove(parent.Key);
                }
                virtualClassChildrenEpoch++;
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
                        virtualPlacementsVersion++;
                        RemoveVirtualLookupBinding(placement.member.id, childId);
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
            string encodedValue = numericValue
                ? value
                : EncodeVirtualPathString(value);
            return string.Concat(
                parent,
                "/{\"kind\":\"",
                kind,
                "\",",
                EncodeVirtualPathString(valueName),
                ":",
                encodedValue,
                "}");
        }

        /// <summary>
        /// <see cref="Newtonsoft.Json.JsonConvert.ToString(string)"/>, without
        /// its writer for the printable ASCII that names and keys almost
        /// always are: such a string needs no escape, only its quotes.
        /// </summary>
        private static string EncodeVirtualPathString(string value)
        {
            foreach (char c in value)
            {
                if (c < ' ' || c > '~' || c == '"' || c == '\\')
                    return Newtonsoft.Json.JsonConvert.ToString(value);
            }
            return string.Concat("\"", value, "\"");
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
            if (!string.IsNullOrEmpty(row.sourceValueId))
                return row.sourceValueId!;
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
            using var sha1 = SHA1.Create();
            return VirtualValueId(instanceRootId, sourceIdentity, sha1);
        }

        private static string VirtualValueId(string instanceRootId, string sourceIdentity, SHA1 sha1) =>
            DerivedValueId(instanceRootId, VirtualNamespaceBytes, sourceIdentity, sha1);

        internal static string DerivedMemberValueId(string memberId)
        {
            using var sha1 = SHA1.Create();
            return DerivedValueId(memberId, MemberValueNamespaceBytes, null, sha1);
        }

        private static string DerivedValueId(string rootId, byte[] namespaceBytes, string? sourceIdentity, SHA1 sha1)
        {
            const string systemPrefix = "system_";
            bool isSystemRecord = rootId.StartsWith(systemPrefix, StringComparison.Ordinal);
            int rootStart = isSystemRecord ? systemPrefix.Length : 0;
            int rootLength = rootId.Length - rootStart;
            int rootBytes = Encoding.UTF8.GetByteCount(rootId.AsSpan(rootStart));
            int sourceBytes = sourceIdentity is null ? 0 : 1 + Encoding.UTF8.GetByteCount(sourceIdentity);
            var input = new byte[namespaceBytes.Length + rootBytes + sourceBytes];
            Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
            Encoding.UTF8.GetBytes(rootId, rootStart, rootLength, input, namespaceBytes.Length);
            if (sourceIdentity is not null)
            {
                int separator = namespaceBytes.Length + rootBytes;
                input[separator] = (byte)':';
                Encoding.UTF8.GetBytes(sourceIdentity, 0, sourceIdentity.Length, input, separator + 1);
            }
            byte[] hash = sha1.ComputeHash(input);
            hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
            hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
            string valueId = new Guid(
                (hash[0] << 24) | (hash[1] << 16) | (hash[2] << 8) | hash[3],
                (short)((hash[4] << 8) | hash[5]),
                (short)((hash[6] << 8) | hash[7]),
                hash[8], hash[9], hash[10], hash[11], hash[12], hash[13], hash[14], hash[15]).ToString();
            return isSystemRecord ? systemPrefix + valueId : valueId;
        }
    }
}
