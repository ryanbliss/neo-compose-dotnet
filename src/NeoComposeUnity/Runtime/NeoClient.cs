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
        public NeoAttributeCustom assets { get; protected set; }
        public NeoAttributeCustomSaved save { get; protected set; }

        /// <summary>
        /// Flat registry of every constructed <see cref="NeoAttribute"/>,
        /// keyed by <see cref="MakeNodeKey"/>. Each
        /// <see cref="NeoAttribute"/> registers itself at the end of
        /// construction so consumers (and the
        /// <see cref="NeoAttribute.Create"/> /
        /// <see cref="NeoAttribute.CreateSaved"/> factories) can look up
        /// existing instances and reuse them rather than constructing
        /// duplicates that share the same wire identity.
        /// </summary>
        internal IReadOnlyDictionary<string, NeoAttribute> nodes => nodesInternal;
        private readonly Dictionary<string, NeoAttribute> nodesInternal = new();

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
        /// coarse invalidation signal after <c>*Saved</c> mutations.
        /// </summary>
        internal event System.Action<string>? OnSaveValueChanged;

        protected ProjectData data;
        protected ProjectSaveData saveData;
        protected LoadSave loadSave;
        protected HandleSave handleSave;

        public NeoClient(ProjectData data, LoadSave loadSave, HandleSave handleSave)
        {
            this.data = data;
            this.loadSave = loadSave;
            this.handleSave = handleSave;
            LoadOrCreateSafe();
            assets = new(this, data.project.rootAssetsAttributeId, null);
            save = new(this, data.project.rootSaveFileAttributeId, null);
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

        internal void AddSaveValue<TAttributeValue>(string attributeId, TAttributeValue value) where TAttributeValue : AttributeValue
        {
            saveData.attributeValueOverrides[attributeId] = value.id;
            SetSaveValue(value);
            OnSaveOverrideChanged?.Invoke(attributeId, value.id);
        }

        internal void SetSaveValue<TAttributeValue>(TAttributeValue value) where TAttributeValue : AttributeValue
        {
            saveData.values[value.id] = value;
            OnSaveValueChanged?.Invoke(value.id);
        }

        internal void SetSavePayloadRows(object? payload)
        {
            if (payload is not NeoValuePayload wrapped) return;
            foreach (var row in wrapped.valueRows)
            {
                SetSaveValue(row);
            }
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
        /// <para>Used by collection <c>*Saved</c> Remove operations to
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
            if (!saveData.values.TryGetValue(valueId, out AttributeValue val)) return;
            // Recurse first so children are pruned before we drop the
            // parent. Order doesn't strictly matter (no cycles in a
            // valid save) but doing it depth-first matches the
            // bottom-up nature of GC.
            switch (val)
            {
                case ObjectAttributeValue obj when obj.value is not null:
                    foreach (var nestedId in obj.value.Values)
                    {
                        RemoveSaveValueAndDescendants(nestedId);
                    }
                    break;
                case ArrayAttributeValue arr when arr.value is not null:
                    foreach (var nestedId in arr.value)
                    {
                        RemoveSaveValueAndDescendants(nestedId);
                    }
                    break;
            }
            if (saveData.values.Remove(valueId))
            {
                OnSaveValueChanged?.Invoke(valueId);
            }
        }

        internal void RemoveSaveValueAndDescendantsIfUnlinked(string valueId)
        {
            RemoveSaveValueAndDescendantsIfUnlinked(
                valueId,
                BuildReachableSaveValueIds());
        }

        private void RemoveSaveValueAndDescendantsIfUnlinked(
            string valueId,
            HashSet<string> reachable)
        {
            if (reachable.Contains(valueId)) return;
            if (!saveData.values.TryGetValue(valueId, out AttributeValue val)) return;

            switch (val)
            {
                case ObjectAttributeValue obj when obj.value is not null:
                    foreach (var nestedId in obj.value.Values)
                    {
                        RemoveSaveValueAndDescendantsIfUnlinked(nestedId, reachable);
                    }
                    break;
                case ArrayAttributeValue arr when arr.value is not null:
                    foreach (var nestedId in arr.value)
                    {
                        RemoveSaveValueAndDescendantsIfUnlinked(nestedId, reachable);
                    }
                    break;
            }
            if (saveData.values.Remove(valueId))
            {
                OnSaveValueChanged?.Invoke(valueId);
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

        /// <summary>
        /// Looks up a previously-registered <see cref="NeoAttribute"/>
        /// by attribute id (and optional override-value id). Returns
        /// false when nothing is registered for the composed key.
        /// Callers typically reach for
        /// <see cref="NeoAttribute.Create"/> /
        /// <see cref="NeoAttribute.CreateSaved"/> instead — those check
        /// here automatically before constructing.
        /// </summary>
        internal bool TryGetNode(string attributeId, string? overrideValueId, [NotNullWhen(true)] out NeoAttribute? node)
        {
            string key = MakeNodeKey(attributeId, overrideValueId);
            return nodesInternal.TryGetValue(key, out node);
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
            string key = MakeNodeKey(node.attribute.id, node.overrideValueId);
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
            string key = MakeNodeKey(node.attribute.id, node.overrideValueId);
            // Only remove if the registered instance is the one we're
            // unregistering — guards against the "I disposed an
            // instance that was already replaced in the registry by a
            // newer ctor call for the same key" race.
            if (nodesInternal.TryGetValue(key, out NeoAttribute existing) && existing == node)
            {
                nodesInternal.Remove(key);
            }
        }

        /// <summary>
        /// Composes the registry key from an attribute id and an
        /// optional override-value id. Format mirrors the user-facing
        /// spec:
        ///   - <c>attributeId</c> when no override
        ///   - <c>$"{attributeId}_{overrideValueId}"</c> when an override is set
        /// </summary>
        internal static string MakeNodeKey(string attributeId, string? overrideValueId)
        {
            return string.IsNullOrEmpty(overrideValueId)
                ? attributeId
                : $"{attributeId}_{overrideValueId}";
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
            var reachable = new HashSet<string>();
            foreach (var valueId in saveData.attributeValueOverrides.Values)
            {
                MarkReachableValue(valueId, reachable);
            }
            if (data.attributes.TryGetValue(data.project.rootSaveFileAttributeId, out Attribute rootSaveAttribute)
                && rootSaveAttribute.valueId is not null)
            {
                MarkReachableValue(rootSaveAttribute.valueId, reachable);
            }
            return reachable;
        }

        private void MarkReachableValue(string valueId, HashSet<string> reachable)
        {
            if (!reachable.Add(valueId)) return;
            if (!saveData.values.TryGetValue(valueId, out AttributeValue? val)
                && !data.values.TryGetValue(valueId, out val))
            {
                return;
            }
            switch (val)
            {
                case ObjectAttributeValue obj when obj.value is not null:
                    foreach (var nestedId in obj.value.Values)
                    {
                        MarkReachableValue(nestedId, reachable);
                    }
                    break;
                case ArrayAttributeValue arr when arr.value is not null:
                    foreach (var nestedId in arr.value)
                    {
                        MarkReachableValue(nestedId, reachable);
                    }
                    break;
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
