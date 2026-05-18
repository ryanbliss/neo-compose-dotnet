// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
        public delegate string LoadSave();
        public delegate void HandleSave(string content);
        public delegate object? NeoNativeFunctionInvoker(
            NeoClient client,
            object? receiver,
            object?[] args);
        public NeoAttributeCustom assets { get; protected set; }
        public NeoAttributeCustomWritable save { get; protected set; }
        public NeoAttributeCustomWritable session { get; protected set; }
        public NeoAttributeCustom AssetsRoot => assets;
        public NeoAttributeCustomWritable SaveRoot => save;
        public NeoAttributeCustomWritable SessionRoot => session;

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
        internal IReadOnlyDictionary<string, string> saveOverrides => saveData.attributeValueOverrides;
        internal IReadOnlyDictionary<string, AttributeValue> sessionValues => sessionData.values;
        internal IReadOnlyDictionary<string, string> sessionOverrides => sessionData.attributeValueOverrides;
        internal Project project => data.project;

        /// <summary>
        /// Fired when the entry for <c>attributeId</c> in
        /// <see cref="ProjectSaveData.attributeValueOverrides"/> is
        /// added, replaced, or removed. Subscribers (notably
        /// <see cref="NeoAttribute"/>) recompute their resolved
        /// <c>valueId</c> chain and refresh their <c>value</c>.
        ///
        /// <para>The second argument is the *new* value id (or
        /// <c>null</c> if removed). Wire mutations of
        /// <see cref="Attribute.valueId"/> directly on the DTO
        /// don't fire here — those are out of band; consumers that
        /// mutate the wire DTO should call the affected node's
        /// refresh hook manually.</para>
        /// </summary>
        internal event System.Action<string, string?>? OnSaveOverrideChanged;
        /// <summary>
        /// Fired whenever a save-side value row is added, replaced, or
        /// removed. The argument is the affected value id.
        /// Generated wrappers and runtime collection helpers use this as a
        /// coarse invalidation signal after <c>*Writable</c> mutations.
        /// </summary>
        internal event System.Action<string>? OnSaveValueChanged;
        internal event System.Action<NeoValueOwnership, string, string?>? OnWritableOverrideChanged;
        internal event System.Action<NeoValueOwnership, string>? OnWritableValueChanged;

        protected ProjectData data;
        protected ProjectSaveData saveData;
        protected ProjectSaveData sessionData;
        protected LoadSave loadSave;
        protected HandleSave handleSave;
        internal NeoAssetDatabase? assetDatabase;
        private IReadOnlyDictionary<string, NeoNativeFunctionInvoker>? nativeFunctionInvokers;

        public NeoClient(
            ProjectData data,
            LoadSave loadSave,
            HandleSave handleSave,
            NeoAssetDatabase? assetDatabase = null)
        {
            this.data = data;
            this.loadSave = loadSave;
            this.handleSave = handleSave;
            this.assetDatabase = assetDatabase;
            ValidateRootCustomAttribute(data.project.rootAssetsAttributeId, nameof(Project.rootAssetsAttributeId));
            ValidateRootCustomAttribute(data.project.rootSaveFileAttributeId, nameof(Project.rootSaveFileAttributeId));
            ValidateRootCustomAttribute(data.project.rootSessionAttributeId, nameof(Project.rootSessionAttributeId));
            LoadOrCreateSafe();
            sessionData = BuildDefaultSessionData();
            InitializeSessionDefaults();
            assets = new(this, data.project.rootAssetsAttributeId, null);
            save = new(this, data.project.rootSaveFileAttributeId, null, NeoValueOwnership.Save);
            session = new(this, data.project.rootSessionAttributeId, null, NeoValueOwnership.Session);
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
            if (data.values.ContainsKey(id))
            {
                ownership = NeoValueOwnership.Asset;
                return true;
            }
            ownership = NeoValueOwnership.Asset;
            return false;
        }

        internal void AddSaveValue<TAttributeValue>(string attributeId, TAttributeValue value) where TAttributeValue : AttributeValue
        {
            AddWritableValue(NeoValueOwnership.Save, attributeId, value);
        }

        internal void SetSaveValue<TAttributeValue>(TAttributeValue value) where TAttributeValue : AttributeValue
        {
            SetWritableValue(NeoValueOwnership.Save, value);
        }

        internal void SetSaveValueSilently<TAttributeValue>(TAttributeValue value) where TAttributeValue : AttributeValue
        {
            SetWritableValueSilently(NeoValueOwnership.Save, value);
        }

        internal void AddWritableValue<TAttributeValue>(
            NeoValueOwnership ownership,
            string attributeId,
            TAttributeValue value) where TAttributeValue : AttributeValue
        {
            var store = GetWritableStore(ownership);
            store.attributeValueOverrides[attributeId] = value.id;
            SetWritableValue(ownership, value);
            OnWritableOverrideChanged?.Invoke(ownership, attributeId, value.id);
            if (ownership == NeoValueOwnership.Save)
            {
                OnSaveOverrideChanged?.Invoke(attributeId, value.id);
            }
        }

        internal void SetWritableValue<TAttributeValue>(
            NeoValueOwnership ownership,
            TAttributeValue value) where TAttributeValue : AttributeValue
        {
            GetWritableStore(ownership).values[value.id] = value;
            OnWritableValueChanged?.Invoke(ownership, value.id);
            if (ownership == NeoValueOwnership.Save)
            {
                OnSaveValueChanged?.Invoke(value.id);
            }
        }

        internal void SetWritableValueSilently<TAttributeValue>(
            NeoValueOwnership ownership,
            TAttributeValue value) where TAttributeValue : AttributeValue
        {
            GetWritableStore(ownership).values[value.id] = value;
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
            if (targetOwnership == NeoValueOwnership.Save) OnSaveValueChanged?.Invoke(valueId);
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
            clone.id = System.Guid.NewGuid().ToString();
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

        private bool TryInferAttributeForValueId(
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

            foreach (var pair in sessionData.attributeValueOverrides)
            {
                if (pair.Value == valueId
                    && TryGetAttribute(pair.Key, out Attribute? sessionAttribute))
                {
                    attribute = sessionAttribute;
                    return true;
                }
            }

            foreach (var pair in saveData.attributeValueOverrides)
            {
                if (pair.Value == valueId
                    && TryGetAttribute(pair.Key, out Attribute? saveAttribute))
                {
                    attribute = saveAttribute;
                    return true;
                }
            }

            foreach (var parent in EnumerateAllValueRows())
            {
                foreach (var child in EnumerateOwnedChildLinks(
                             parent.Value,
                             TryInferDirectAttributeForValueId(parent.Key, out Attribute? parentAttribute)
                                 ? parentAttribute
                                 : null))
                {
                    if (child.valueId == valueId && child.attribute is not null)
                    {
                        attribute = child.attribute;
                        return true;
                    }
                }
            }

            attribute = null;
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
            foreach (var pair in sessionData.attributeValueOverrides)
            {
                if (pair.Value == valueId
                    && TryGetAttribute(pair.Key, out Attribute? sessionAttribute))
                {
                    attribute = sessionAttribute;
                    return true;
                }
            }
            foreach (var pair in saveData.attributeValueOverrides)
            {
                if (pair.Value == valueId
                    && TryGetAttribute(pair.Key, out Attribute? saveAttribute))
                {
                    attribute = saveAttribute;
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

        internal bool TryMaterializeSavePath(string rowId)
        {
            string? rootValueId = save.value?.id;
            if (string.IsNullOrEmpty(rootValueId)) return false;

            var path = new List<string>();
            if (!TryFindValuePath(rootValueId!, rowId, new HashSet<string>(), path))
            {
                return false;
            }

            for (int i = 0; i < path.Count; i++)
            {
                string pathValueId = path[i];
                if (saveData.values.ContainsKey(pathValueId)) continue;
                if (!TryGetValue(pathValueId, out AttributeValue? row)) return false;

                var clone = CloneValueRow(row);
                if (i == 0)
                {
                    AddSaveValue(project.rootSaveFileAttributeId, clone);
                }
                else
                {
                    SetSaveValueSilently(clone);
                }
            }
            return true;
        }

        private bool TryFindValuePath(
            string currentValueId,
            string targetValueId,
            HashSet<string> visited,
            List<string> path)
        {
            if (!visited.Add(currentValueId)) return false;
            path.Add(currentValueId);
            if (currentValueId == targetValueId) return true;

            if (TryGetValue(currentValueId, out AttributeValue? row))
            {
                switch (row)
                {
                    case ObjectAttributeValue obj when obj.value != null:
                        foreach (var childValueId in obj.value.Values)
                        {
                            if (TryFindValuePath(childValueId, targetValueId, visited, path))
                            {
                                return true;
                            }
                        }
                        break;
                    case ArrayAttributeValue arr when arr.value != null:
                        foreach (var childValueId in arr.value)
                        {
                            if (TryFindValuePath(childValueId, targetValueId, visited, path))
                            {
                                return true;
                            }
                        }
                        break;
                }
            }

            path.RemoveAt(path.Count - 1);
            return false;
        }

        private static AttributeValue CloneValueRow(AttributeValue row)
        {
            AttributeValue clone = row switch
            {
                NullAttributeValue n => new NullAttributeValue { value = n.value },
                BoolAttributeValue b => new BoolAttributeValue { value = b.value },
                NumberAttributeValue n => new NumberAttributeValue { value = n.value },
                StringAttributeValue s => new StringAttributeValue { value = s.value },
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
        /// Removes the save-side override for <paramref name="attributeId"/>
        /// (i.e., the entry in
        /// <see cref="ProjectSaveData.attributeValueOverrides"/>). Fires
        /// <see cref="OnSaveOverrideChanged"/> with a null value id so
        /// subscribers refresh accordingly. Returns true if an entry
        /// was actually removed; false if nothing was registered.
        ///
        /// <para>Note: this only removes the override mapping. The
        /// underlying value row in <see cref="ProjectSaveData.values"/>
        /// stays — call
        /// <see cref="RemoveSaveValueAndDescendants"/> if you also want
        /// to GC it (and any nested values).</para>
        /// </summary>
        internal bool RemoveSaveOverride(string attributeId)
        {
            if (!saveData.attributeValueOverrides.Remove(attributeId)) return false;
            OnSaveOverrideChanged?.Invoke(attributeId, null);
            OnWritableOverrideChanged?.Invoke(NeoValueOwnership.Save, attributeId, null);
            return true;
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
                OnWritableValueChanged?.Invoke(ownership, valueId);
                if (ownership == NeoValueOwnership.Save)
                {
                    OnSaveValueChanged?.Invoke(valueId);
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
                OnWritableValueChanged?.Invoke(ownership, valueId);
                if (ownership == NeoValueOwnership.Save)
                {
                    OnSaveValueChanged?.Invoke(valueId);
                }
            }
        }

        /// <summary>
        /// Returns the save's override value-id for the given attribute,
        /// or null if no override is registered. Used by
        /// <see cref="NeoAttribute"/> to resolve `valueId` after a Set
        /// creates a new top-level save row.
        /// </summary>
        internal bool TryGetSaveOverrideValueId(string attributeId, [NotNullWhen(true)] out string? valueId)
        {
            return saveData.attributeValueOverrides.TryGetValue(attributeId, out valueId);
        }

        internal bool TryGetWritableOverrideValueId(
            NeoValueOwnership ownership,
            string attributeId,
            [NotNullWhen(true)] out string? valueId)
        {
            valueId = null;
            if (ownership == NeoValueOwnership.Asset) return false;
            return GetWritableStore(ownership).attributeValueOverrides.TryGetValue(attributeId, out valueId);
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
            if (TryGetWritableOverrideValueId(NeoValueOwnership.Session, collectionAttribute.id, out string? sessionValueId))
            {
                return sessionValueId;
            }
            if (TryGetSaveOverrideValueId(collectionAttribute.id, out string? saveValueId))
            {
                return saveValueId;
            }
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

        internal object? InvokeNativeFunction(
            string attributeId,
            object? receiver,
            object?[] args)
        {
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
                projectId = data.project.id,
                version = "0.0.0",
                // leave `values` and `attributeValueOverrides` empty until value(s) are set at runtime
                values = new(),
                attributeValueOverrides = new(),
            };
            return empty;
        }

        protected ProjectSaveData BuildDefaultSessionData()
        {
            return new()
            {
                projectId = data.project.id,
                version = "0.0.0",
                values = new(),
                attributeValueOverrides = new(),
            };
        }

        private void InitializeSessionDefaults()
        {
            if (!data.attributes.TryGetValue(data.project.rootSessionAttributeId, out Attribute rootSessionAttribute)
                || rootSessionAttribute.valueId is null)
            {
                return;
            }
            CopyAuthoredValueGraphToSession(
                rootSessionAttribute.valueId,
                new HashSet<string>(),
                rootSessionAttribute);
        }

        private void CopyAuthoredValueGraphToSession(
            string valueId,
            HashSet<string> visited,
            Attribute? sourceAttribute = null)
        {
            if (!visited.Add(valueId)) return;
            if (!data.values.TryGetValue(valueId, out AttributeValue? row)) return;

            var clone = CloneValueRow(row);
            SetWritableValueSilently(NeoValueOwnership.Session, clone);

            foreach (var child in EnumerateOwnedChildLinks(row, sourceAttribute))
            {
                CopyAuthoredValueGraphToSession(child.valueId, visited, child.attribute);
            }
        }

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

        public void Commit()
        {
            var unlinkedValueIds = FindUnlinkedSaveValueIds();
            if (unlinkedValueIds.Count > 0)
            {
                Debug.LogWarning(
                    $"NeoCompose save contains {unlinkedValueIds.Count} unlinked value(s). " +
                    "This can happen when generated factory values are created but never assigned. " +
                    "Call RunGarbageCollector() before Commit() to delete unlinked values.");
            }
            EmitHandleSave();
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
            var reachable = new HashSet<string>();
            var store = GetWritableStore(ownership);
            foreach (var valueId in store.attributeValueOverrides.Values)
            {
                MarkReachableValue(ownership, valueId, reachable);
            }
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

        protected void EmitHandleSave()
        {
            handleSave.Invoke(SerializeSaveData());
            LoadUnsafe();
        }

        [MemberNotNull(nameof(saveData))]
        protected void LoadOrCreateSafe()
        {
            string saveJson = "";
            try
            {
                saveJson = loadSave.Invoke();
            }
            catch (System.Exception)
            {
                // Host couldn't supply a save file at all — fall through to default.
            }

            ProjectSaveData? parsed = null;
            if (!string.IsNullOrEmpty(saveJson))
            {
                try
                {
                    parsed = DeserializeSaveData(saveJson);
                }
                catch (System.Exception exception)
                {
                    Debug.LogError(exception);
                }
            }

            // `DeserializeSaveData` returns null on an empty / whitespace
            // string without throwing, so a successful-but-empty load
            // still needs the default-build fallback.
            if (parsed == null)
            {
                saveData = BuildDefaultSaveData();
                EmitHandleSave();
            }
            else
            {
                saveData = parsed;
            }
        }

        protected void LoadUnsafe()
        {
            string saveJson = loadSave.Invoke();
            ProjectSaveData? parsed = DeserializeSaveData(saveJson);
            if (parsed != null) saveData = parsed;
        }
    }
}
