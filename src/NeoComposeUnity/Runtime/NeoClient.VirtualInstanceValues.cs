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
        private bool virtualInstanceValuesDirty;

        private sealed class VirtualExpansionNode
        {
            internal MemberValue row = null!;
            internal Member member = null!;
            internal string path = null!;
            internal string virtualId = null!;
            internal VirtualExpansionNode? parent;
            internal readonly Dictionary<string, VirtualExpansionNode> classChildren = new();
            internal readonly List<VirtualExpansionNode> listChildren = new();
            internal readonly Dictionary<string, VirtualExpansionNode> dictionaryChildren = new();
        }

        /// <summary>
        /// P75 root eligibility. A row is a sparse instance root when it
        /// carries ANY arm of the creation-provenance stamp: an explicit
        /// <c>instanceConstructorId</c> (present even when its value is
        /// null &#8212; the implicit <c>new()</c>), evaluated
        /// <c>constructorArgs</c>, or a selected variant. The
        /// <c>constructorArgs</c> arm matters on its own because the web's
        /// schema-impact repair can produce a row that carries only the
        /// arguments; without it that row would silently stop expanding.
        /// </summary>
        internal static bool IsVirtualInstanceRoot(MemberValue row)
        {
            return row.hasInstanceConstructorId
                || row.constructorArgs is not null
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
        /// </summary>
        internal static JToken? ConstructorArgumentToken(
            object? value,
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
            return virtualClassChildren.TryGetValue(
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
            if (isInitializingVirtualInstanceValues)
            {
                virtualInstanceValuesDirty = true;
                return;
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
                    InitializeVirtualInstanceValuesCore(failClosed);
                }
                while (virtualInstanceValuesDirty);
            }
            finally
            {
                isInitializingVirtualInstanceValues = false;
            }
        }

        private void InitializeVirtualInstanceValuesCore(bool failClosed)
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
            virtualValues.Clear();
            virtualValueOwnership.Clear();
            virtualClassChildren.Clear();
            virtualEntriesByContainer.Clear();
            virtualContainerByRow.Clear();
            virtualValueIdsByRoot.Clear();
            virtualClassParentIdsByRoot.Clear();
            MemberValue[] allRows = data.values.Values
                .Concat(saveData.values.Values)
                .Concat(sessionData.values.Values)
                .GroupBy(row => row.id, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            Dictionary<string, string> parentByValueId =
                BuildParentByValueId(allRows);
            ObjectMemberValue[] roots = allRows
                .OfType<ObjectMemberValue>()
                .Where(row => row.classId is not null)
                .Where(IsVirtualInstanceRoot)
                // A containing replay may index the nested root using its
                // outer-root virtual scope. Replaying shallow-to-deep lets the
                // nested root's own durable recipe overwrite that provisional
                // mapping with the authoritative nested-root scope.
                .OrderBy(row => AuthoredContainmentDepth(row.id, parentByValueId))
                .ThenBy(row => row.id, StringComparer.Ordinal)
                .ToArray();

            foreach (ObjectMemberValue root in roots)
            {
                ExpandVirtualInstanceRootOrReport(root, failClosed);
            }

            DisposeWrappersTouchingRows(outgoingVirtualIds);

            // The three roots were created before replay so constructor and
            // variant code could resolve Assets/Save/Session. Rebind their
            // wrapper trees once the virtual child index is complete.
            RefreshVirtualWrapperTree(assets);
            RefreshVirtualWrapperTree(save);
            RefreshVirtualWrapperTree(session);
        }

        /// <summary>
        /// Whether the row NAMES its creation recipe rather than merely
        /// carrying evaluated arguments. Pre-P75 corpora stamp
        /// <c>constructorArgs</c> from P61 evaluation without recording which
        /// overload ran, so an arguments-only row is shape-identical to a root
        /// the web's schema-impact repair produced. Explicit rows are the only
        /// ones a replay failure can be blamed on.
        /// </summary>
        private static bool HasExplicitInstanceProvenance(MemberValue row)
        {
            return row.hasInstanceConstructorId
                || row.instanceVariantId is not null;
        }

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
                if (!sessionData.values.ContainsKey(root.id))
                    AssertPersistedVirtualInstanceRootIsClassPlacement(root);
                ExpandVirtualInstanceRoot(root);
            }
            catch (Exception error)
            {
                ClearVirtualInstanceRoot(root.id);
                if (!HasExplicitInstanceProvenance(root))
                {
                    // Legacy arguments-only row. It loaded fully materialized
                    // and stays that way; there is nothing to warn about.
                    return;
                }
                if (failClosed) throw;
                Debug.LogWarning(
                    $"[NeoCompose] P75 could not replay instance root '{root.id}' of class '{root.classId}' from the incoming live content; its virtual values are unavailable until the next successful apply. {error}");
            }
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
            string? rowValueId)
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
            if (!EnsureWritableShadow(ownership, valueId)
                || !TryGetOverlaidValue(
                    ownership,
                    valueId,
                    out ObjectMemberValue? root))
            {
                throw new InvalidOperationException(
                    $"ToVariant could not shadow instance root '{valueId}'.");
            }
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
            ExpandVirtualInstanceRoot(root);
            RefreshVirtualWrapperTree(node);
        }

        /// <summary>
        /// Child value id -> the id of the row that owns it, over whichever
        /// corpus is supplied: class/dictionary bodies, ordered-list bodies,
        /// and unordered-list containment stamps.
        /// </summary>
        private static Dictionary<string, string> BuildParentByValueId(
            IEnumerable<MemberValue> rows)
        {
            var parentByValueId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (MemberValue row in rows)
            {
                if (row is ObjectMemberValue objectRow && objectRow.value is not null)
                {
                    foreach (string childId in objectRow.value.Values)
                        parentByValueId.TryAdd(childId, row.id);
                }
                else if (row is ArrayMemberValue arrayRow && arrayRow.value is not null)
                {
                    foreach (string childId in arrayRow.value)
                        parentByValueId.TryAdd(childId, row.id);
                }
                if (row.containerId is not null)
                    parentByValueId.TryAdd(row.id, row.containerId);
            }
            return parentByValueId;
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
                .Where(IsVirtualInstanceRoot)
                .OrderBy(row => AuthoredContainmentDepth(row.id, parentByValueId))
                .ThenBy(row => row.id, StringComparer.Ordinal))
            {
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
                    || !IsVirtualInstanceRoot(root))
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
            if (virtualValueIdsByRoot.TryGetValue(
                    instanceRoot.id,
                    out HashSet<string>? priorVirtualIds))
            {
                // Wrapper nodes retain the row object they were built from.
                // A variant swap reuses stable virtual ids with new effective
                // values, so release the prior wrappers before replacing the
                // index or they keep serving the old variant indefinitely.
                DisposeWrappersTouchingRows(priorVirtualIds);
            }
            ClearVirtualInstanceRoot(instanceRoot.id);
            NeoValueOwnership ownership = TryGetValueOwnership(
                instanceRoot.id,
                out NeoValueOwnership resolvedOwnership)
                    ? resolvedOwnership
                    : EffectiveAuthoredOwnership(instanceRoot.id, instanceRoot);
            var before = new HashSet<string>(
                sessionData.values.Keys,
                StringComparer.Ordinal);
            NeoMemberClassWritable constructed;
            try
            {
                constructed = ReplayVirtualInstance(instanceRoot);
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    $"P75 could not replay sparse instance '{instanceRoot.id}' of class '{instanceRoot.classId}'.",
                    error);
            }

            string temporaryRootId = constructed.value?.id
                ?? throw new InvalidOperationException(
                    $"P75 replay for '{instanceRoot.id}' produced no root row.");
            if (!sessionData.values.TryGetValue(
                    temporaryRootId,
                    out MemberValue? expandedRoot))
            {
                throw new InvalidOperationException(
                    $"P75 replay for '{instanceRoot.id}' lost temporary root '{temporaryRootId}'.");
            }

            try
            {
                var claimedVirtualIds = new Dictionary<string, string>(StringComparer.Ordinal);
                VirtualExpansionNode graph = IndexVirtualExpansion(
                    instanceRoot,
                    constructed,
                    "$",
                    claimedVirtualIds);
                OverlaySparseInstance(
                    graph,
                    instanceRoot.id,
                    ownership,
                    instanceRoot);
            }
            finally
            {
                IReadOnlyCollection<string> removed = RemoveTemporaryWritableValueGraph(
                    NeoValueOwnership.Session,
                    temporaryRootId);
                DisposeWrappersTouchingRows(removed);
                foreach (string leakedId in sessionData.values.Keys
                    .Where(id => !before.Contains(id))
                    .ToArray())
                {
                    IReadOnlyCollection<string> leaked = RemoveTemporaryWritableValueGraph(
                        NeoValueOwnership.Session,
                        leakedId);
                    DisposeWrappersTouchingRows(leaked);
                }
            }
        }

        private NeoMemberClassWritable ReplayVirtualInstance(ObjectMemberValue root)
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
                    replayArguments.Add(new NeoDeclaredConstructorArgument(
                        argument.name,
                        VirtualReplayArgument(value, argument)));
                }
            }
            return NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                this,
                root.classId!,
                root.instanceConstructorId,
                replayArguments.ToArray());
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
            FunctionArgumentTypeInfo typeInfo)
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
                return UnwrapVirtualReplayRow(token.Value<string>()!);
            }
            return typeInfo.type switch
            {
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
            if (!TryGetValue(valueId, out MemberValue? row))
            {
                throw new InvalidOperationException(
                    $"Constructor argument references missing value '{valueId}'.");
            }
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
            NeoMember wrapper,
            string path,
            Dictionary<string, string> claimedVirtualIds)
        {
            MemberValue row = wrapper.value
                ?? throw new InvalidOperationException(
                    $"Virtual expansion path '{path}' has no value row.");
            Member member = wrapper.member;
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

            if (member is ClassMember classMember
                && row is ObjectMemberValue classRow
                && wrapper is NeoMemberClass classNode)
            {
                foreach (var pair in classNode)
                {
                    string childPath = AppendVirtualPath(
                        path,
                        "class",
                        "schemaKey",
                        pair.Key);
                    if (pair.Value.value is null) continue;
                    VirtualExpansionNode child = IndexVirtualExpansion(
                        instanceRoot,
                        pair.Value,
                        childPath,
                        claimedVirtualIds);
                    child.parent = node;
                    node.classChildren[pair.Key] = child;
                }
            }
            else if (member is ListMember
                && wrapper is NeoMemberList listNode)
            {
                int index = 0;
                foreach (NeoMember childWrapper in listNode)
                {
                    if (childWrapper.value is null) continue;
                    string childPath = AppendVirtualPath(
                        path,
                        "list",
                        "index",
                        index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        numericValue: true);
                    VirtualExpansionNode child = IndexVirtualExpansion(
                        instanceRoot,
                        childWrapper,
                        childPath,
                        claimedVirtualIds);
                    child.parent = node;
                    node.listChildren.Add(child);
                    index++;
                }
            }
            else if (member is DictionaryMember
                && wrapper is NeoMemberDictionary dictionaryNode)
            {
                foreach (var pair in dictionaryNode)
                {
                    if (pair.Value.value is null) continue;
                    string childPath = AppendVirtualPath(
                        path,
                        "dictionary",
                        "key",
                        pair.Key);
                    VirtualExpansionNode child = IndexVirtualExpansion(
                        instanceRoot,
                        pair.Value,
                        childPath,
                        claimedVirtualIds);
                    child.parent = node;
                    node.dictionaryChildren[pair.Key] = child;
                }
            }
            return node;
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

        private void OverlaySparseInstance(
            VirtualExpansionNode node,
            string? materializedId,
            NeoValueOwnership ownership,
            ObjectMemberValue instanceRoot)
        {
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
                TryGetOverlaidValue(ownership, node.virtualId, out materialized);
            }
            if (materialized is null)
            {
                IndexVirtualSubtree(node, instanceRoot, ownership);
                return;
            }
            if (materialized.id != instanceRoot.id
                && materialized is ObjectMemberValue nestedRoot
                && IsVirtualInstanceRoot(nestedRoot))
            {
                // A nested construction owns its own UUID namespace and is
                // replayed independently. Do not retain an unreachable copy
                // of its virtual remainder in the outer root's namespace.
                return;
            }
            string effectiveId = materialized?.id ?? node.virtualId;

            if (node.member is ClassMember)
            {
                Dictionary<string, string>? stored =
                    (materialized as ObjectMemberValue)?.value;
                foreach (var pair in node.classChildren)
                {
                    string? childMaterializedId = null;
                    if (stored is not null)
                        stored.TryGetValue(pair.Key, out childMaterializedId);
                    if (childMaterializedId is null)
                    {
                        if (!virtualClassChildren.TryGetValue(
                                effectiveId,
                                out Dictionary<string, string>? links))
                        {
                            links = new Dictionary<string, string>(StringComparer.Ordinal);
                            virtualClassChildren[effectiveId] = links;
                            TrackVirtualClassParent(instanceRoot.id, effectiveId);
                        }
                        links[pair.Key] = pair.Value.virtualId;
                    }
                    OverlaySparseInstance(
                        pair.Value,
                        childMaterializedId,
                        ownership,
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
                    MemberValue? entry = ResolveEffectiveRow(entryId);
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
                    if (childMaterializedId is null)
                    {
                        IndexVirtualSubtree(
                            child,
                            instanceRoot,
                            ownership,
                            effectiveId);
                    }
                    else
                    {
                        OverlaySparseInstance(
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
                        pair.Value,
                        childMaterializedId,
                        ownership,
                        instanceRoot);
                }
            }
        }

        private void IndexVirtualSubtree(
            VirtualExpansionNode node,
            ObjectMemberValue instanceRoot,
            NeoValueOwnership ownership,
            string? unorderedContainerId = null)
        {
            MemberValue virtualRow = RewriteVirtualRow(node, instanceRoot);
            if (unorderedContainerId is not null)
                virtualRow.containerId = unorderedContainerId;
            virtualValues[node.virtualId] = virtualRow;
            virtualValueOwnership[node.virtualId] = ownership;
            TrackVirtualValue(instanceRoot.id, node.virtualId);
            if (!string.IsNullOrEmpty(virtualRow.containerId))
            {
                AddMembership(
                    virtualEntriesByContainer,
                    virtualContainerByRow,
                    virtualRow.id,
                    virtualRow.containerId!);
            }
            foreach (VirtualExpansionNode child in node.classChildren.Values)
                IndexVirtualSubtree(child, instanceRoot, ownership);
            foreach (VirtualExpansionNode child in node.listChildren)
                IndexVirtualSubtree(child, instanceRoot, ownership);
            foreach (VirtualExpansionNode child in node.dictionaryChildren.Values)
                IndexVirtualSubtree(child, instanceRoot, ownership);
        }

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
            if (virtualClassChildren.TryGetValue(
                    valueId,
                    out Dictionary<string, string>? links))
            {
                foreach (string childId in links.Values) yield return childId;
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
                if (virtualValueOwnership.TryGetValue(
                        expansionId,
                        out NeoValueOwnership expansionOwnership)
                    && expansionOwnership != ownership)
                {
                    continue;
                }
                yield return expansionId;
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

        private void ClearVirtualInstanceRoot(string rootId)
        {
            if (virtualValueIdsByRoot.TryGetValue(rootId, out HashSet<string>? valueIds))
            {
                foreach (string valueId in valueIds)
                {
                    virtualValues.Remove(valueId);
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
