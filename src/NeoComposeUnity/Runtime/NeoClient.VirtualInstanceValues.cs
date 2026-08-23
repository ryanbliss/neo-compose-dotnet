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

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private const string VirtualValueNamespace =
            "3e8ca0b3-e3f1-5d5f-bf2f-6ab5ee3896d0";

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
        private void InitializeVirtualInstanceValues()
        {
            virtualValues.Clear();
            virtualValueOwnership.Clear();
            virtualClassChildren.Clear();
            MemberValue[] allRows = data.values.Values
                .Concat(saveData.values.Values)
                .Concat(sessionData.values.Values)
                .GroupBy(row => row.id, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            var parentByValueId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (MemberValue row in allRows)
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
            ObjectMemberValue[] roots = allRows
                .OfType<ObjectMemberValue>()
                .Where(row => row.classId is not null)
                .Where(row => row.hasInstanceConstructorId
                    || row.instanceVariantId is not null)
                // A containing replay may index the nested root using its
                // outer-root virtual scope. Replaying shallow-to-deep lets the
                // nested root's own durable recipe overwrite that provisional
                // mapping with the authoritative nested-root scope.
                .OrderBy(row => AuthoredContainmentDepth(row.id, parentByValueId))
                .ThenBy(row => row.id, StringComparer.Ordinal)
                .ToArray();

            foreach (ObjectMemberValue root in roots)
            {
                ExpandVirtualInstanceRoot(root);
            }

            // The three roots were created before replay so constructor and
            // variant code could resolve Assets/Save/Session. Rebind their
            // wrapper trees once the virtual child index is complete.
            RefreshVirtualWrapperTree(assets);
            RefreshVirtualWrapperTree(save);
            RefreshVirtualWrapperTree(session);
        }

        /// <summary>
        /// P75 variant swaps persist one root-level provenance delta. The
        /// imperative Apply closure has already written every value it touched;
        /// declarative variant halves stay virtual and are replayed here.
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
            SetWritableValue(ownership, root, "instanceVariantId");
            InitializeVirtualInstanceValues();
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
            NeoValueOwnership ownership = EffectiveAuthoredOwnership(
                instanceRoot.id,
                instanceRoot);
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
                var nodes = new List<VirtualExpansionNode>();
                CollectVirtualNodes(graph, nodes);
                foreach (VirtualExpansionNode node in nodes)
                {
                    if (node.path == "$") continue;
                    MemberValue virtualRow = RewriteVirtualRow(
                        node,
                        instanceRoot);
                    virtualValues[node.virtualId] = virtualRow;
                    virtualValueOwnership[node.virtualId] = ownership;
                }
                OverlaySparseInstance(
                    graph,
                    instanceRoot.id,
                    ownership,
                    instanceRoot.mapKey);
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
                ?? throw new InvalidOperationException(
                    "Instance has no constructorArgs creation data.");
            NeoDeclaredConstructorArgument[] arguments = constructor is null
                ? Array.Empty<NeoDeclaredConstructorArgument>()
                : constructor.argumentTypes.Select((argument, index) =>
                {
                    string parameterId = ConstructorParameterId(constructor, index);
                    if (!stored.TryGetValue(parameterId, out JToken? value))
                    {
                        throw new InvalidOperationException(
                            $"Constructor '{constructor.id}' is missing argument '{parameterId}'.");
                    }
                    return new NeoDeclaredConstructorArgument(
                        argument.name,
                        VirtualReplayArgument(value, argument));
                }).ToArray();
            return NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                this,
                root.classId!,
                root.instanceConstructorId,
                arguments);
        }

        private static string ConstructorParameterId(
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
            NeoValueOwnership ownership = TryGetValueOwnership(
                valueId,
                out NeoValueOwnership resolved)
                    ? resolved
                    : NeoValueOwnership.Asset;
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
            string sourceIdentity = !string.IsNullOrEmpty(row.sourceValueId)
                ? row.sourceValueId!
                : $"path:{member.id}:{path}";
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

        private static void CollectVirtualNodes(
            VirtualExpansionNode node,
            List<VirtualExpansionNode> result)
        {
            result.Add(node);
            foreach (VirtualExpansionNode child in node.classChildren.Values)
                CollectVirtualNodes(child, result);
            foreach (VirtualExpansionNode child in node.listChildren)
                CollectVirtualNodes(child, result);
            foreach (VirtualExpansionNode child in node.dictionaryChildren.Values)
                CollectVirtualNodes(child, result);
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
            string? mapKey)
        {
            MemberValue? materialized = null;
            if (materializedId is not null)
                TryGetOverlaidValue(ownership, materializedId, out materialized);
            string effectiveId = materialized?.id ?? node.virtualId;
            virtualValueOwnership[node.virtualId] = ownership;

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
                        }
                        links[pair.Key] = pair.Value.virtualId;
                    }
                    OverlaySparseInstance(
                        pair.Value,
                        childMaterializedId,
                        ownership,
                        mapKey);
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
                        mapKey);
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
                        mapKey);
                }
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

        private static string VirtualValueId(string instanceRootId, string sourceIdentity)
        {
            string namespaceHex = VirtualValueNamespace.Replace("-", string.Empty);
            var namespaceBytes = new byte[namespaceHex.Length / 2];
            for (int index = 0; index < namespaceBytes.Length; index++)
            {
                namespaceBytes[index] = Convert.ToByte(
                    namespaceHex.Substring(index * 2, 2),
                    16);
            }
            byte[] nameBytes = Encoding.UTF8.GetBytes(
                $"{instanceRootId}:{sourceIdentity}");
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
            return $"{hex.Substring(0, 8)}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}-{hex.Substring(16, 4)}-{hex.Substring(20, 12)}";
        }
    }
}
