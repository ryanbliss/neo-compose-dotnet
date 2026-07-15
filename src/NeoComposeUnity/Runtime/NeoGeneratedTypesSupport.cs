// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Attribute = NeoCompose.Runtime.Json.Attribute;

namespace NeoCompose.Runtime
{
    internal readonly struct NeoGeneratedConstructorDictionaryEntry
    {
        internal NeoGeneratedConstructorDictionaryEntry(
            object? key,
            object? value)
        {
            Key = key;
            Value = value;
        }

        internal object? Key { get; }
        internal object? Value { get; }
    }

    /// <summary>
    /// Ownership-qualified reference captured from a generated wrapper or a
    /// NeoScript row-backed value. Stable ids can coexist in Session and Save,
    /// so constructor attachment must not rediscover ownership from the id
    /// after the caller has already selected a concrete value.
    /// </summary>
    internal readonly struct NeoConstructorValueReference
    {
        internal NeoConstructorValueReference(
            string valueId,
            NeoValueOwnership? ownership)
        {
            this.valueId = valueId;
            this.ownership = ownership;
        }

        internal string valueId { get; }
        internal NeoValueOwnership? ownership { get; }
    }

    /// <summary>
    /// Non-generic bridge used by generated dictionary views so constructor
    /// materialization does not need reflection for the common
    /// <c>NeoDictionary&lt;T&gt;</c> path.
    /// </summary>
    internal interface INeoGeneratedConstructorDictionary
    {
        IEnumerable<NeoGeneratedConstructorDictionaryEntry>
            EnumerateGeneratedConstructorEntries();
    }

    /// <summary>
    /// Stable generated-constructor argument descriptor. Generated facades
    /// identify each supplied value by both merged schema key and attribute id
    /// so stale generated code fails before any value rows are published.
    /// </summary>
    public sealed class NeoGeneratedConstructorValue
    {
        public string schemaKey { get; }
        public string attributeId { get; }
        public object? value { get; }

        public NeoGeneratedConstructorValue(
            string schemaKey,
            string attributeId,
            object? value)
        {
            this.schemaKey = schemaKey
                ?? throw new ArgumentNullException(nameof(schemaKey));
            this.attributeId = attributeId
                ?? throw new ArgumentNullException(nameof(attributeId));
            this.value = value;
        }
    }

    /// <summary>
    /// Common option-id surface implemented by generated enum option classes.
    /// It lets the shared constructor materializer handle enum values without
    /// reflection or project-specific generated helpers.
    /// </summary>
    public interface INeoEnumOption
    {
        string optionId { get; }
    }

    /// <summary>
    /// Shared helper methods used by web-generated C# facade types.
    /// Kept in the SDK runtime so generated files only contain
    /// project-specific schema wrappers.
    /// </summary>
    public static class NeoGeneratedTypesSupport
    {
        private sealed class ConstructorKeyValuePairAccessors
        {
            internal System.Reflection.PropertyInfo key = null!;
            internal System.Reflection.PropertyInfo value = null!;
        }

        private static readonly object ConstructorDictionaryShapeLock = new();
        private static readonly Dictionary<Type, bool>
            ConstructorDictionaryShapeCache = new();
        private static readonly Dictionary<Type, ConstructorKeyValuePairAccessors?>
            ConstructorKeyValuePairAccessorsCache = new();

        internal sealed class RuntimeConstructorField
        {
            internal string schemaKey = null!;
            internal string attributeId = null!;
            internal object? value;
        }

        private sealed class RuntimeConstructorMetadata
        {
            internal Dictionary<string, Attribute> attributesBySchemaKey = null!;
            internal IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv = null!;
        }

        public delegate object ReadOnlyCustomFactory(
            NeoClient client,
            NeoAttributeCustom node);

        public delegate object WritableCustomFactory(
            NeoClient client,
            NeoAttributeCustomWritable node);

        public static NeoValueWritePayload? Value<T>(T? value)
        {
            return NeoValueWritePayload.FromValue(value);
        }

        /// <summary>
        /// Builds a live attribute-id keyed static-member view. Generated
        /// properties call this from the active project singleton, so every
        /// access observes the current authored/Save/Session binding.
        /// </summary>
        public static NeoStaticBinding StaticBinding(
            NeoClient client,
            string attributeId,
            NeoValueOwnership ownership)
        {
            return new NeoStaticBinding(client, attributeId, ownership);
        }

        public static SpriteValue? SpriteValue(
            NeoClient client,
            Sprite? sprite,
            string? expectedTemplateId = null,
            string? attributeName = null)
        {
            return sprite is null
                ? null
                : NeoAssetResolver.ValueForSprite(
                    client.assetDatabase,
                    sprite,
                    expectedTemplateId,
                    attributeName);
        }

        public static FileValue? AudioValue(
            NeoClient client,
            AudioClip? audioClip,
            string? expectedTemplateId = null,
            string? attributeName = null)
        {
            return audioClip is null
                ? null
                : NeoAssetResolver.ValueForAudioClip(
                    client.assetDatabase,
                    audioClip,
                    expectedTemplateId,
                    attributeName);
        }

        public static NeoVector2Value Vector2Value(Vector2 value)
        {
            return NeoVectorValues.FromVector2(value);
        }

        public static NeoVector2Value Vector2IntValue(Vector2Int value)
        {
            return NeoVectorValues.FromVector2Int(value);
        }

        public static NeoVector3Value Vector3Value(Vector3 value)
        {
            return NeoVectorValues.FromVector3(value);
        }

        public static NeoVector3Value Vector3IntValue(Vector3Int value)
        {
            return NeoVectorValues.FromVector3Int(value);
        }

        public static NeoColorValue ColorValue(Color value)
        {
            return NeoColorValues.FromColor(value);
        }

        public static Color? ReadColorValue(object? value)
        {
            if (value is null) return null;
            if (value is Color color) return color;
            if (value is NeoReadOnlyColor wrapper) return wrapper.Value;
            if (value is NeoColorValue raw) return NeoColorValues.ToColor(raw);
            if (TryReadColorComponents(value, out float r, out float g, out float b, out float a))
            {
                return new Color(r, g, b, a);
            }
            return null;
        }

        public static Vector2? ReadVector2Value(object? value)
        {
            if (value is null) return null;
            if (value is Vector2 vector) return vector;
            if (value is NeoReadOnlyVector2 wrapper) return wrapper.Value;
            if (value is NeoVector2Value raw) return NeoVectorValues.ToVector2(raw);
            if (TryReadVectorComponents(value, false, out float x, out float y, out _))
            {
                return new Vector2(x, y);
            }
            return null;
        }

        public static Vector2Int? ReadVector2IntValue(object? value)
        {
            if (value is null) return null;
            if (value is Vector2Int vector) return vector;
            if (value is NeoReadOnlyVector2Int wrapper) return wrapper.Value;
            if (value is NeoVector2Value raw) return NeoVectorValues.ToVector2Int(raw);
            if (TryReadVectorComponents(value, false, out float x, out float y, out _))
            {
                return NeoVectorValues.ToVector2Int(new NeoVector2Value { x = x, y = y });
            }
            return null;
        }

        public static Vector3? ReadVector3Value(object? value)
        {
            if (value is null) return null;
            if (value is Vector3 vector) return vector;
            if (value is NeoReadOnlyVector3 wrapper) return wrapper.Value;
            if (value is NeoVector3Value raw) return NeoVectorValues.ToVector3(raw);
            if (TryReadVectorComponents(value, true, out float x, out float y, out float z))
            {
                return new Vector3(x, y, z);
            }
            return null;
        }

        public static Vector3Int? ReadVector3IntValue(object? value)
        {
            if (value is null) return null;
            if (value is Vector3Int vector) return vector;
            if (value is NeoReadOnlyVector3Int wrapper) return wrapper.Value;
            if (value is NeoVector3Value raw) return NeoVectorValues.ToVector3Int(raw);
            if (TryReadVectorComponents(value, true, out float x, out float y, out float z))
            {
                return NeoVectorValues.ToVector3Int(new NeoVector3Value { x = x, y = y, z = z });
            }
            return null;
        }

        public static void SetVector2(
            NeoAttributeCustomWritable node,
            string key,
            Vector2 value)
        {
            SetValue(node, key, Value(Vector2Value(value)));
        }

        public static void SetVector2Int(
            NeoAttributeCustomWritable node,
            string key,
            Vector2Int value)
        {
            SetValue(node, key, Value(Vector2IntValue(value)));
        }

        public static void SetVector3(
            NeoAttributeCustomWritable node,
            string key,
            Vector3 value)
        {
            SetValue(node, key, Value(Vector3Value(value)));
        }

        public static void SetVector3Int(
            NeoAttributeCustomWritable node,
            string key,
            Vector3Int value)
        {
            SetValue(node, key, Value(Vector3IntValue(value)));
        }

        // ------------------------------------------------------------------
        // Wrapper-typed write funnels (specs/color-attribute.md §4/§6.2).
        // Generated property setters route through these: `obj.Position = v`
        // assigns a (bound or detached) wrapper whose *current* value is
        // written — value-copy semantics, never a live link. The native-typed
        // overloads above stay for NeoScript marshalling and value-row
        // creation. The null guard throws a distinct ArgumentNullException
        // because an implicit-conversion NRE would otherwise surface with a
        // useless message.
        // ------------------------------------------------------------------

        public static void SetVector2(
            NeoAttributeCustomWritable node,
            string key,
            NeoReadOnlyVector2 value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    $"Cannot assign a null Vector2 wrapper to required attribute '{key}'.");
            }
            SetVector2(node, key, value.Value);
        }

        public static void SetVector2OrClear(
            NeoAttributeCustomWritable node,
            string key,
            NeoReadOnlyVector2? value)
        {
            if (value is null)
            {
                node.Unset(key);
                return;
            }
            SetVector2(node, key, value.Value);
        }

        public static void SetVector2Int(
            NeoAttributeCustomWritable node,
            string key,
            NeoReadOnlyVector2Int value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    $"Cannot assign a null Vector2Int wrapper to required attribute '{key}'.");
            }
            SetVector2Int(node, key, value.Value);
        }

        public static void SetVector2IntOrClear(
            NeoAttributeCustomWritable node,
            string key,
            NeoReadOnlyVector2Int? value)
        {
            if (value is null)
            {
                node.Unset(key);
                return;
            }
            SetVector2Int(node, key, value.Value);
        }

        public static void SetVector3(
            NeoAttributeCustomWritable node,
            string key,
            NeoReadOnlyVector3 value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    $"Cannot assign a null Vector3 wrapper to required attribute '{key}'.");
            }
            SetVector3(node, key, value.Value);
        }

        public static void SetVector3OrClear(
            NeoAttributeCustomWritable node,
            string key,
            NeoReadOnlyVector3? value)
        {
            if (value is null)
            {
                node.Unset(key);
                return;
            }
            SetVector3(node, key, value.Value);
        }

        public static void SetVector3Int(
            NeoAttributeCustomWritable node,
            string key,
            NeoReadOnlyVector3Int value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    $"Cannot assign a null Vector3Int wrapper to required attribute '{key}'.");
            }
            SetVector3Int(node, key, value.Value);
        }

        public static void SetVector3IntOrClear(
            NeoAttributeCustomWritable node,
            string key,
            NeoReadOnlyVector3Int? value)
        {
            if (value is null)
            {
                node.Unset(key);
                return;
            }
            SetVector3Int(node, key, value.Value);
        }

        public static void SetColor(
            NeoAttributeCustomWritable node,
            string key,
            NeoReadOnlyColor value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    $"Cannot assign a null Color wrapper to required attribute '{key}'.");
            }
            SetValue(node, key, Value(ColorValue(value.Value)));
        }

        public static void SetColorOrClear(
            NeoAttributeCustomWritable node,
            string key,
            NeoReadOnlyColor? value)
        {
            if (value is null)
            {
                node.Unset(key);
                return;
            }
            SetValue(node, key, Value(ColorValue(value.Value)));
        }

        public static TGenerated GetOrCreateGeneratedCustomValue<TGenerated>(
            NeoClient client,
            NeoAttributeCustom node,
            Func<TGenerated> create)
            where TGenerated : NeoGeneratedCustomValue
        {
            return client.GetOrCreateGeneratedCustomValue(node, create);
        }

        /// <summary>
        /// Core of the generated <c>GetChild&lt;T&gt;</c> family. Enumerates a generated
        /// Children collection live (no caching), returning the first child assignable
        /// to <typeparamref name="TChild"/> in list order, optionally filtered by an
        /// ordinal match on the child's <c>Name</c>. Each match is resolved to its
        /// writable twin when one exists, otherwise returned as-is.
        /// </summary>
        public static bool TryGetGeneratedChild<TChild>(
            System.Collections.IEnumerable? children,
            string? name,
            out TChild child)
            where TChild : NeoGeneratedCustomValue
        {
            if (children is not null)
            {
                foreach (var item in children)
                {
                    var resolved = ResolveGeneratedChild<TChild>(item, name);
                    if (resolved is null) continue;
                    child = resolved;
                    return true;
                }
            }

            child = null!;
            return false;
        }

        /// <summary>
        /// Required variant of <see cref="TryGetGeneratedChild{TChild}"/> for children
        /// the content contract guarantees to exist.
        /// </summary>
        public static TChild GetRequiredGeneratedChild<TChild>(
            NeoGeneratedCustomValue owner,
            System.Collections.IEnumerable? children,
            string? name)
            where TChild : NeoGeneratedCustomValue
        {
            if (owner is null) throw new ArgumentNullException(nameof(owner));
            if (TryGetGeneratedChild(children, name, out TChild child))
            {
                return child;
            }

            string nameFilter = name is null ? string.Empty : $" named '{name}'";
            throw new InvalidOperationException(
                $"Generated value '{owner.GetType().Name}' (valueId '{owner.valueId}') has no child of type '{typeof(TChild).Name}'{nameFilter}.");
        }

        /// <summary>
        /// Plural variant of <see cref="TryGetGeneratedChild{TChild}"/>: every child
        /// assignable to <typeparamref name="TChild"/>, in list order.
        /// </summary>
        public static IReadOnlyList<TChild> GetGeneratedChildren<TChild>(
            System.Collections.IEnumerable? children)
            where TChild : NeoGeneratedCustomValue
        {
            if (children is null) return Array.Empty<TChild>();

            var matches = new List<TChild>();
            foreach (var item in children)
            {
                var resolved = ResolveGeneratedChild<TChild>(item, name: null);
                if (resolved is null) continue;
                matches.Add(resolved);
            }
            return matches;
        }

        private static TChild? ResolveGeneratedChild<TChild>(object? item, string? name)
            where TChild : NeoGeneratedCustomValue
        {
            if (item is not NeoGeneratedCustomValue value) return null;

            TChild? typed;
            if (value.TryWritable(out TChild writable))
            {
                typed = writable;
            }
            else
            {
                typed = value as TChild;
            }
            if (typed is null) return null;

            if (name is not null
                && !string.Equals(ReadGeneratedName(typed), name, StringComparison.Ordinal))
            {
                return null;
            }

            return typed;
        }

        private static string? ReadGeneratedName(NeoGeneratedCustomValue value)
        {
            var nameProperty = value.GetType().GetProperty("Name", typeof(string));
            if (nameProperty is null || !nameProperty.CanRead) return null;
            return nameProperty.GetValue(value) as string;
        }

        public static object? ResolveCustomValue(
            NeoClient client,
            string valueId,
            IReadOnlyDictionary<string, ReadOnlyCustomFactory> readOnlyFactories,
            IReadOnlyDictionary<string, WritableCustomFactory> savedFactories)
        {
            if (!client.TryGetValue(valueId, out ObjectAttributeValue? value))
            {
                return null;
            }
            string? typeId = ResolveCustomValueTypeId(client, valueId, value);
            if (string.IsNullOrEmpty(typeId)) return null;

            CustomAttribute attribute;
            if (TryInferAttributeForValueId(
                    client,
                    valueId,
                    new HashSet<string>(),
                    out Attribute? inferredAttribute)
                && inferredAttribute is CustomAttribute inferredCustomAttribute)
            {
                attribute = inferredCustomAttribute;
            }
            else
            {
                attribute = new CustomAttribute
                {
                    id = $"__neo_resolved_custom_{typeId}",
                    name = "ResolvedCustomValue",
                    type = AttributeType.Custom,
                    customTypeId = typeId,
                    createdAt = value.createdAt,
                    updatedAt = value.updatedAt,
                };
            }

            if (client.TryGetValueOwnership(valueId, out NeoValueOwnership ownership)
                && (ownership == NeoValueOwnership.Save || ownership == NeoValueOwnership.Session)
                && savedFactories.TryGetValue(typeId, out var savedFactory))
            {
                return savedFactory(
                    client,
                    new NeoAttributeCustomWritable(client, attribute, valueId, ownership));
            }

            if (readOnlyFactories.TryGetValue(typeId, out var readOnlyFactory))
            {
                return readOnlyFactory(
                    client,
                    new NeoAttributeCustom(client, attribute, valueId));
            }

            return null;
        }

        public static T ResolveNativeFunctionReceiver<T>(
            NeoClient client,
            object? receiver,
            IReadOnlyDictionary<string, ReadOnlyCustomFactory> readOnlyFactories,
            IReadOnlyDictionary<string, WritableCustomFactory> savedFactories,
            string functionName,
            string attributeId)
            where T : class
        {
            if (receiver is T typed) return typed;
            string? valueId = ValueId(receiver);
            if (!string.IsNullOrEmpty(valueId))
            {
                var resolved = ResolveCustomValue(
                    client,
                    valueId!,
                    readOnlyFactories,
                    savedFactories);
                if (resolved is T resolvedTyped) return resolvedTyped;
            }
            throw new NeoScript.NSGetterRuntimeError(
                $"Cannot invoke Function '{functionName}' ({attributeId}) because receiver type '{receiver?.GetType().Name ?? "null"}' is not supported.");
        }

        public static T? ResolveNativeFunctionCustomArgument<T>(
            NeoClient client,
            object? value,
            bool required,
            IReadOnlyDictionary<string, ReadOnlyCustomFactory> readOnlyFactories,
            IReadOnlyDictionary<string, WritableCustomFactory> savedFactories,
            string argumentName)
            where T : class
        {
            if (value is null)
            {
                if (required)
                {
                    throw new NeoScript.NSGetterRuntimeError(
                        $"Native Function argument '{argumentName}' is required.");
                }
                return null;
            }
            if (value is T typed) return typed;
            string? valueId = ValueId(value);
            if (!string.IsNullOrEmpty(valueId))
            {
                var resolved = ResolveCustomValue(
                    client,
                    valueId!,
                    readOnlyFactories,
                    savedFactories);
                if (resolved is T resolvedTyped) return resolvedTyped;
            }
            throw new NeoScript.NSGetterRuntimeError(
                $"Native Function argument '{argumentName}' could not be converted to {typeof(T).Name}.");
        }

        public static TDeferred ResolveDeferredFunction<TDeferred>(
            NeoDeferredFunctionBase deferred,
            string functionName)
            where TDeferred : NeoDeferredFunctionBase
        {
            if (deferred is TDeferred typed) return typed;
            var expectedType = typeof(TDeferred);
            if (expectedType == typeof(NeoDeferredFunction))
            {
                return (TDeferred)(NeoDeferredFunctionBase)new NeoDeferredFunction(
                    deferred.StateCore);
            }
            if (expectedType.IsGenericType
                && expectedType.GetGenericTypeDefinition() == typeof(NeoDeferredFunction<>))
            {
                var created = Activator.CreateInstance(
                    expectedType,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    binder: null,
                    args: new object[] { deferred.StateCore },
                    culture: null);
                if (created is TDeferred createdTyped) return createdTyped;
            }
            throw new NeoScript.NSGetterRuntimeError(
                $"Deferred Function '{functionName}' expected handle type {expectedType.Name}, got {deferred.GetType().Name}.");
        }

        private static string? ResolveCustomValueTypeId(
            NeoClient client,
            string valueId,
            ObjectAttributeValue value)
        {
            if (!string.IsNullOrEmpty(value.typeId)) return value.typeId;
            return TryInferCustomTypeId(
                client,
                valueId,
                new HashSet<string>(),
                out string? typeId)
                ? typeId
                : null;
        }

        private static bool TryInferCustomTypeId(
            NeoClient client,
            string valueId,
            HashSet<string> visitingValueIds,
            out string? typeId)
        {
            if (!visitingValueIds.Add(valueId))
            {
                typeId = null;
                return false;
            }

            if (client.TryGetValue(valueId, out ObjectAttributeValue? value)
                && !string.IsNullOrEmpty(value.typeId))
            {
                typeId = value.typeId;
                return true;
            }

            if (TryInferAttributeForValueId(
                    client,
                    valueId,
                    visitingValueIds,
                    out Attribute? attribute)
                && attribute != null
                && TryResolveDirectCustomTypeIdFromAttribute(attribute, out typeId))
            {
                return true;
            }

            typeId = null;
            return false;
        }

        private static bool TryInferAttributeForValueId(
            NeoClient client,
            string valueId,
            HashSet<string> visitingValueIds,
            out Attribute? attribute)
        {
            foreach (var candidate in client.attributes.Values)
            {
                if (candidate.valueId == valueId)
                {
                    attribute = candidate;
                    return true;
                }
            }

            foreach (var parent in EnumerateValues(client))
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
                            client,
                            parent.Key,
                            new HashSet<string>(visitingValueIds),
                            out Attribute? parentAttribute)
                        && TryResolveCollectionEntryAttribute(
                            client,
                            parentAttribute,
                            out Attribute? parentEntryAttribute))
                    {
                        attribute = parentEntryAttribute;
                        return true;
                    }

                    if (!TryInferCustomTypeId(
                            client,
                            parent.Key,
                            new HashSet<string>(visitingValueIds),
                            out string? parentTypeId)
                        || string.IsNullOrEmpty(parentTypeId)
                        || !client.types.TryGetValue(parentTypeId, out CustomType? parentType))
                    {
                        continue;
                    }

                    MergedSchemaEntry? matchedEntry = null;
                    foreach (MergedSchemaEntry entry in CustomTypeInheritance.MergeInstanceSchema(
                        CustomTypeInheritance.ResolveChain(
                            parentType.id,
                            id => client.TryGetType(id, out CustomType? candidate)
                                ? candidate
                                : null),
                        id => client.TryGetAttribute(id, out Attribute? candidate)
                            ? candidate
                            : null))
                    {
                        if (entry.schemaKey == pair.Key)
                        {
                            matchedEntry = entry;
                            break;
                        }
                    }
                    if (matchedEntry is null
                        || !client.TryGetAttribute(
                            matchedEntry.attributeId,
                            out Attribute? childAttribute))
                    {
                        continue;
                    }

                    attribute = childAttribute;
                    return true;
                }
            }

            foreach (var parent in EnumerateValues(client))
            {
                if (parent.Value is ArrayAttributeValue arrayValue
                    && arrayValue.value != null
                    && Contains(arrayValue.value, valueId)
                    && TryInferAttributeForValueId(
                        client,
                        parent.Key,
                        new HashSet<string>(visitingValueIds),
                        out Attribute? collectionAttribute)
                    && TryResolveCollectionEntryAttribute(
                        client,
                        collectionAttribute,
                        out Attribute? entryAttribute))
                {
                    attribute = entryAttribute;
                    return true;
                }

                if (parent.Value is ObjectAttributeValue dictionaryValue
                    && dictionaryValue.value != null
                    && dictionaryValue.value.ContainsValue(valueId)
                    && TryInferAttributeForValueId(
                        client,
                        parent.Key,
                        new HashSet<string>(visitingValueIds),
                        out collectionAttribute)
                    && TryResolveCollectionEntryAttribute(
                        client,
                        collectionAttribute,
                        out entryAttribute))
                {
                    attribute = entryAttribute;
                    return true;
                }
            }

            attribute = null;
            return false;
        }

        private static bool TryResolveDirectCustomTypeIdFromAttribute(
            Attribute attribute,
            out string? typeId)
        {
            if (attribute is CustomAttribute custom
                && !string.IsNullOrEmpty(custom.customTypeId))
            {
                typeId = custom.customTypeId;
                return true;
            }

            typeId = null;
            return false;
        }

        private static bool TryResolveCollectionEntryAttribute(
            NeoClient client,
            Attribute? attribute,
            out Attribute? entryAttribute)
        {
            string? entryAttributeId = attribute switch
            {
                ListAttribute list => list.entryAttributeId,
                DictionaryAttribute dictionary => dictionary.entryAttributeId,
                _ => null,
            };
            if (attribute is LookupAttribute lookup
                && client.TryGetAttribute(
                    lookup.collectionAttributeId,
                    out Attribute? collectionAttribute))
            {
                return TryResolveCollectionEntryAttribute(
                    client,
                    collectionAttribute,
                    out entryAttribute);
            }

            if (string.IsNullOrEmpty(entryAttributeId)
                || !client.TryGetAttribute(entryAttributeId!, out Attribute? resolved))
            {
                entryAttribute = null;
                return false;
            }

            entryAttribute = resolved;
            return true;
        }

        private static IEnumerable<KeyValuePair<string, AttributeValue>> EnumerateValues(
            NeoClient client)
        {
            foreach (var pair in client.sessionValues) yield return pair;
            foreach (var pair in client.saveValues) yield return pair;
            foreach (var pair in client.values) yield return pair;
        }

        private static bool Contains(string[] values, string value)
        {
            foreach (var item in values)
            {
                if (item == value) return true;
            }
            return false;
        }

        private static bool TryReadVectorComponents(
            object value,
            bool zRequired,
            out float x,
            out float y,
            out float z)
        {
            x = 0;
            y = 0;
            z = 0;
            if (value is IDictionary<string, object?> dict)
            {
                if (dict.Count != (zRequired ? 3 : 2)) return false;
                return TryReadFloat(dict.TryGetValue("x", out var xv) ? xv : null, out x)
                    && TryReadFloat(dict.TryGetValue("y", out var yv) ? yv : null, out y)
                    && (!zRequired || TryReadFloat(dict.TryGetValue("z", out var zv) ? zv : null, out z));
            }
            if (value is JObject obj)
            {
                if (obj.Count != (zRequired ? 3 : 2)) return false;
                return TryReadFloat(obj["x"], out x)
                    && TryReadFloat(obj["y"], out y)
                    && (!zRequired || TryReadFloat(obj["z"], out z));
            }
            return false;
        }

        private static bool TryReadColorComponents(
            object value,
            out float r,
            out float g,
            out float b,
            out float a)
        {
            r = 0;
            g = 0;
            b = 0;
            a = 0;
            if (value is IDictionary<string, object?> dict)
            {
                if (dict.Count != 4) return false;
                return TryReadFloat(dict.TryGetValue("r", out var rv) ? rv : null, out r)
                    && TryReadFloat(dict.TryGetValue("g", out var gv) ? gv : null, out g)
                    && TryReadFloat(dict.TryGetValue("b", out var bv) ? bv : null, out b)
                    && TryReadFloat(dict.TryGetValue("a", out var av) ? av : null, out a);
            }
            if (value is JObject obj)
            {
                if (obj.Count != 4) return false;
                return TryReadFloat(obj["r"], out r)
                    && TryReadFloat(obj["g"], out g)
                    && TryReadFloat(obj["b"], out b)
                    && TryReadFloat(obj["a"], out a);
            }
            return false;
        }

        private static bool TryReadFloat(object? value, out float result)
        {
            switch (value)
            {
                case float f:
                    result = f;
                    return !float.IsNaN(f) && !float.IsInfinity(f);
                case double d:
                    result = (float)d;
                    return !float.IsNaN(result) && !float.IsInfinity(result);
                case int i:
                    result = i;
                    return true;
                case long l:
                    result = l;
                    return true;
                case JValue token:
                    return TryReadFloat(token.Value, out result);
                default:
                    result = 0;
                    return false;
            }
        }

        public static NeoValueWritePayload? ValueReference(
            INeoValueReference? value)
        {
            return value is null
                ? null
                : NeoValueWritePayload.FromValueReference(
                    LookupSelectionId(value.valueId),
                    value);
        }

        /// <summary>
        /// Deep-clones a generated Custom value into a new parentless Session
        /// graph. The returned writable node preserves the source's runtime
        /// Custom type while every owned row has a fresh value id.
        /// </summary>
        public static NeoAttributeCustomWritable CloneCustomValue(
            NeoClient client,
            INeoValueReference source)
        {
            if (source is null || string.IsNullOrEmpty(source.valueId))
            {
                throw new ArgumentNullException(
                    nameof(source),
                    "Cannot clone a Custom value without a backing value id.");
            }
            NeoValueOwnership sourceOwnership = source is NeoGeneratedCustomValue generated
                ? generated.ValueOwnership
                : (client.TryGetValueOwnership(source.valueId!, out var inferredOwnership)
                    ? inferredOwnership
                    : NeoValueOwnership.Asset);
            string clonedValueId = client.CloneValueReference(
                source.valueId!,
                sourceOwnership);
            if (!client.TryGetValue(clonedValueId, out ObjectAttributeValue? clone))
            {
                throw new InvalidOperationException(
                    $"Cloned Custom value '{clonedValueId}' has no object value row.");
            }
            string? clonedTypeId = ResolveCustomValueTypeId(client, clonedValueId, clone);
            if (string.IsNullOrEmpty(clonedTypeId))
            {
                throw new InvalidOperationException(
                    $"Cloned Custom value '{clonedValueId}' has no resolvable runtime typeId.");
            }
            var factoryAttribute = new CustomAttribute
            {
                id = $"__neo_clone_custom_{clonedTypeId}",
                name = "Clone",
                type = AttributeType.Custom,
                customTypeId = clonedTypeId!,
                createdAt = clone.createdAt,
                updatedAt = clone.updatedAt,
            };
            return new NeoAttributeCustomWritable(
                client,
                factoryAttribute,
                clonedValueId,
                NeoValueOwnership.Session);
        }

        public static void SetValue(
            NeoAttributeCustomWritable node,
            string key,
            NeoValueWritePayload? value)
        {
            node.SetSerializedValue(key, value);
        }

        /// <summary>
        /// Writable view over a (possibly read-only) Custom node. Generated
        /// classes use the overload with an inherited ownership context when
        /// inherited members should resolve storage from the concrete owner.
        /// </summary>
        public static NeoAttributeCustomWritable AsWritable(NeoAttributeCustom node)
        {
            return node.AsWritableView();
        }

        public static NeoAttributeCustomWritable AsWritable(
            NeoAttributeCustom node,
            NeoValueOwnership inheritedOwnership)
        {
            return node.AsWritableView(inheritedOwnership);
        }

        public static void SetValue(
            NeoAttributeDictionaryWritable node,
            string key,
            NeoValueWritePayload? value)
        {
            node.SetSerialized(key, value);
        }

        public static void AddValue(
            NeoAttributeListWritable node,
            NeoValueWritePayload? value)
        {
            node.AddSerialized(value);
        }

        public static void SetValue(
            NeoAttributeListWritable node,
            int index,
            NeoValueWritePayload? value)
        {
            node.SetSerialized(index, value);
        }

        public static NeoAttributeCustomWritable CreateWritableCustomValue(
            NeoClient client,
            string customTypeId,
            Dictionary<string, string> value,
            IReadOnlyList<AttributeValue> valueRows)
        {
            return CreateWritableCustomValueCore(
                client,
                customTypeId,
                value,
                valueRows,
                referenceOwnershipByPath: null);
        }

        private static NeoAttributeCustomWritable CreateWritableCustomValueCore(
            NeoClient client,
            string customTypeId,
            Dictionary<string, string> value,
            IReadOnlyList<AttributeValue> valueRows,
            IReadOnlyDictionary<string, NeoValueOwnership>?
                referenceOwnershipByPath)
        {
            ValidateConstructibleCustomType(client, customTypeId);
            var nowIso = DateTime.UtcNow.ToString("o");
            var rows = new List<AttributeValue>(valueRows);
            var parentRow = CreateWritableCustomValueRow(
                client,
                customTypeId,
                value,
                rows,
                nowIso,
                new HashSet<string>());
            rows.Add(parentRow);

            PrepareConstructedGraph(
                client,
                parentRow,
                rows,
                referenceOwnershipByPath);

            foreach (var row in rows)
            {
                client.SetWritableValue(NeoValueOwnership.Session, row);
            }

            var factoryAttribute = new CustomAttribute
            {
                id = $"__neo_factory_custom_{customTypeId}",
                name = "Factory",
                type = AttributeType.Custom,
                customTypeId = customTypeId,
                createdAt = nowIso,
                updatedAt = nowIso,
            };
            return new NeoAttributeCustomWritable(
                client,
                factoryAttribute,
                parentRow.id,
                NeoValueOwnership.Session);
        }

        /// <summary>
        /// Materializes generated public-constructor arguments through the
        /// same recursive, atomic supplied-value path as NeoScript
        /// <c>new Custom(...)</c>. Optional null arguments are omitted, while
        /// null entries inside collections retain their position/key as an
        /// explicit nullable value row.
        /// </summary>
        public static NeoAttributeCustomWritable CreateWritableCustomValue(
            NeoClient client,
            string customTypeId,
            params NeoGeneratedConstructorValue[] suppliedValues)
        {
            if (client is null) throw new ArgumentNullException(nameof(client));
            if (customTypeId is null)
                throw new ArgumentNullException(nameof(customTypeId));
            suppliedValues ??= Array.Empty<NeoGeneratedConstructorValue>();
            var fields = new List<RuntimeConstructorField>(suppliedValues.Length);
            foreach (NeoGeneratedConstructorValue supplied in suppliedValues)
            {
                if (supplied is null)
                {
                    throw new ArgumentException(
                        "Generated constructor arguments cannot contain null descriptors.",
                        nameof(suppliedValues));
                }
                fields.Add(new RuntimeConstructorField
                {
                    schemaKey = supplied.schemaKey,
                    attributeId = supplied.attributeId,
                    value = supplied.value,
                });
            }
            return CreateSuppliedCustomValue(
                client,
                new CustomTypeInfo
                {
                    type = AttributeType.Custom,
                    required = true,
                    typeId = customTypeId,
                },
                fields,
                value => GeneratedValueReference(client, value));
        }

        private static NeoConstructorValueReference?
            GeneratedValueReference(NeoClient client, object? value)
        {
            if (value is INeoValueReference reference
                && !string.IsNullOrEmpty(reference.valueId))
            {
                NeoValueOwnership? ownership = value is NeoGeneratedCustomValue generated
                    ? generated.ValueOwnership
                    : client.TryGetValueOwnership(
                        reference.valueId!,
                        out NeoValueOwnership inferred)
                            ? inferred
                            : null;
                return new NeoConstructorValueReference(
                    reference.valueId!,
                    ownership);
            }
            if (value is NeoValueWritePayload payload
                && payload.isValueReference)
            {
                string? valueId = payload.valueReference?.valueId
                    ?? payload.valueId;
                if (string.IsNullOrEmpty(valueId)) return null;
                NeoValueOwnership? ownership =
                    payload.valueReference is NeoGeneratedCustomValue generated
                        ? generated.ValueOwnership
                        : client.TryGetValueOwnership(
                            valueId!,
                            out NeoValueOwnership inferred)
                                ? inferred
                                : null;
                return new NeoConstructorValueReference(
                    valueId!,
                    ownership);
            }
            return null;
        }

        private sealed class PendingConstructorReference
        {
            internal string sourceValueId = null!;
            internal NeoValueOwnership sourceOwnership;
            internal Attribute attribute = null!;
            internal string path = null!;
            internal string? expectedMapKey;
            internal string? expectedContainerId;
            internal Action<string> replaceValueId = null!;
        }

        private static void ValidateConstructibleCustomType(
            NeoClient client,
            string customTypeId)
        {
            if (!client.TryGetType(customTypeId, out CustomType? type))
            {
                throw new InvalidOperationException(
                    $"Cannot construct missing Custom type '{customTypeId}'.");
            }
            if (type!.isAbstract)
            {
                throw new InvalidOperationException(
                    $"Cannot construct abstract Custom type '{type.name}'.");
            }
            if (client.TryResolveCustomTypeAllowedOwnership(
                    customTypeId,
                    out NeoValueOwnership ownership)
                && ownership == NeoValueOwnership.Asset)
            {
                throw new InvalidOperationException(
                    $"Cannot construct immutable-only Custom type '{type.name}'.");
            }
            // Also validates inheritance, closed generic bindings, and merged
            // schema integrity before any Session row can be published.
            _ = ResolveMergedSchema(client, customTypeId);
        }

        /// <summary>
        /// Validates and normalizes the complete generated/runtime constructor
        /// graph before publication. Existing owned Custom references use the
        /// ordinary Session import funnel, while every freshly staged row is
        /// schema-shaped, singly owned, and partition-stamped first.
        /// </summary>
        private static void PrepareConstructedGraph(
            NeoClient client,
            ObjectAttributeValue root,
            List<AttributeValue> rows,
            IReadOnlyDictionary<string, NeoValueOwnership>?
                referenceOwnershipByPath)
        {
            if (!string.IsNullOrEmpty(root.mapKey))
            {
                throw new InvalidOperationException(
                    $"Parentless constructed Custom root '{root.id}' cannot arrive pre-stamped with partition '{root.mapKey}'.");
            }
            root.mapKey = null;
            var stagedById = new Dictionary<string, AttributeValue>();
            foreach (AttributeValue row in rows)
            {
                if (string.IsNullOrEmpty(row.id))
                {
                    throw new InvalidOperationException(
                        "Constructed value graph contains a row without an id.");
                }
                if (!stagedById.TryAdd(row.id, row))
                {
                    throw new InvalidOperationException(
                        $"Constructed value graph contains duplicate row id '{row.id}'.");
                }
                if (client.TryGetValue(row.id, out AttributeValue? _))
                {
                    throw new InvalidOperationException(
                        $"Constructed value graph row id '{row.id}' collides with an existing value.");
                }
            }

            var reachableStagedIds = new HashSet<string> { root.id };
            var ownedByPath = new Dictionary<string, string>();
            var pending = new List<PendingConstructorReference>();
            ValidateConstructedCustomRow(
                client,
                root,
                root.typeId
                    ?? throw new InvalidOperationException(
                        "Constructed Custom root has no runtime typeId."),
                stagedById,
                reachableStagedIds,
                ownedByPath,
                pending,
                path: root.typeId!,
                new HashSet<string>(),
                referenceOwnershipByPath);

            foreach (string stagedId in stagedById.Keys)
            {
                if (!reachableStagedIds.Contains(stagedId))
                {
                    throw new InvalidOperationException(
                        $"Constructed value graph contains orphan staged row '{stagedId}'.");
                }
            }

            // Preflight every ownership decision before the first import. This
            // keeps an already-owned reference error from leaving earlier
            // imported rows behind.
            foreach (PendingConstructorReference reference in pending)
            {
                NeoValueOwnership ownership;
                if (referenceOwnershipByPath is not null
                    && referenceOwnershipByPath.TryGetValue(
                        reference.path,
                        out NeoValueOwnership suppliedOwnership))
                {
                    ownership = suppliedOwnership;
                    if (!client.TryGetValue(
                            ownership,
                            reference.sourceValueId,
                            out AttributeValue? _))
                    {
                        throw new InvalidOperationException(
                            $"Constructed field '{reference.path}' references missing {ownership} value '{reference.sourceValueId}'.");
                    }
                }
                else if (!client.TryGetValueOwnership(
                             reference.sourceValueId,
                             out ownership))
                {
                    throw new InvalidOperationException(
                        $"Constructed field '{reference.path}' references missing value '{reference.sourceValueId}'.");
                }
                reference.sourceOwnership = ownership;
                if (ownership == NeoValueOwnership.Session
                    && client.TryFindOwnedParent(
                        ownership,
                        reference.sourceValueId,
                        out string? parentValueId))
                {
                    throw new InvalidOperationException(
                        $"Constructed field '{reference.path}' cannot attach value '{reference.sourceValueId}' because it is already owned by parent value '{parentValueId}'. Call Clone() explicitly before constructing another owner.");
                }
            }

            var newlyImportedRoots = new List<string>();
            try
            {
                foreach (PendingConstructorReference reference in pending)
                {
                    bool existedInSession =
                        reference.sourceOwnership == NeoValueOwnership.Session
                        && client.HasWritableValue(
                            NeoValueOwnership.Session,
                            reference.sourceValueId);
                    string importedValueId = reference.sourceOwnership
                        == NeoValueOwnership.Session
                            ? client.ImportValueReference(
                                NeoValueOwnership.Session,
                                reference.sourceValueId)
                            : client.CloneOwnedValueReferenceForNewParent(
                                NeoValueOwnership.Session,
                                reference.sourceOwnership,
                                reference.sourceValueId,
                                reference.attribute);
                    reference.replaceValueId(importedValueId);
                    if ((reference.sourceOwnership != NeoValueOwnership.Session
                            || !existedInSession)
                        && client.HasWritableValue(
                            NeoValueOwnership.Session,
                            importedValueId))
                    {
                        newlyImportedRoots.Add(importedValueId);
                    }
                    StampImportedConstructorGraph(
                        client,
                        importedValueId,
                        reference.attribute,
                        reference.expectedMapKey,
                        new HashSet<string>(),
                        expectedContainerId: reference.expectedContainerId);
                }
            }
            catch
            {
                foreach (string importedValueId in newlyImportedRoots)
                {
                    client.RemoveTemporaryWritableValueGraph(
                        NeoValueOwnership.Session,
                        importedValueId);
                }
                throw;
            }
        }

        private static void ValidateConstructedCustomRow(
            NeoClient client,
            ObjectAttributeValue row,
            string customTypeId,
            IReadOnlyDictionary<string, AttributeValue> stagedById,
            HashSet<string> reachableStagedIds,
            Dictionary<string, string> ownedByPath,
            List<PendingConstructorReference> pending,
            string path,
            HashSet<string> traversal,
            IReadOnlyDictionary<string, NeoValueOwnership>?
                referenceOwnershipByPath)
        {
            if (!traversal.Add(row.id))
            {
                throw new InvalidOperationException(
                    $"Constructed value graph contains an owned cycle at '{path}'/'{row.id}'.");
            }
            try
            {
                if (!IsAssignableCustomType(client, customTypeId, customTypeId))
                {
                    throw new InvalidOperationException(
                        $"Constructed Custom row '{row.id}' has unknown runtime type '{customTypeId}'.");
                }
                if (row.value is null)
                {
                    throw new InvalidOperationException(
                        $"Constructed Custom root '{path}' cannot have a null record payload.");
                }
                IList<MergedSchemaEntry> schema = ResolveMergedSchema(
                    client,
                    customTypeId);
                var schemaByKey = new Dictionary<string, MergedSchemaEntry>();
                foreach (MergedSchemaEntry entry in schema)
                {
                    if (!schemaByKey.TryAdd(entry.schemaKey, entry))
                    {
                        throw new InvalidOperationException(
                            $"Merged schema for '{customTypeId}' contains duplicate key '{entry.schemaKey}'.");
                    }
                }
                foreach (string key in row.value.Keys)
                {
                    if (!schemaByKey.ContainsKey(key))
                    {
                        throw new InvalidOperationException(
                            $"Constructed Custom row '{path}' contains unknown schema key '{key}'.");
                    }
                }

                var env = NeoGenericResolution.ResolveInstanceEnv(
                    client,
                    customTypeId,
                    customTypeArguments: null);
                foreach (MergedSchemaEntry entry in schema)
                {
                    if (!client.TryGetAttribute(
                            entry.attributeId,
                            out Attribute? rawAttribute))
                    {
                        throw new InvalidOperationException(
                            $"Constructed Custom row '{path}' schema key '{entry.schemaKey}' references missing attribute '{entry.attributeId}'.");
                    }
                    Attribute attribute = NeoGenericResolution.SubstituteAttribute(
                        client,
                        rawAttribute,
                        env);
                    if (!IsStoredConstructorAttribute(attribute))
                    {
                        if (row.value.ContainsKey(entry.schemaKey))
                        {
                            throw new InvalidOperationException(
                                $"Constructed Custom row '{path}' contains non-stored member '{entry.schemaKey}'.");
                        }
                        continue;
                    }
                    if (!row.value.TryGetValue(entry.schemaKey, out string? childId))
                    {
                        if (attribute.required)
                        {
                            throw new InvalidOperationException(
                                $"Constructed Custom row '{path}' is missing required member '{entry.schemaKey}'/'{entry.attributeId}'.");
                        }
                        continue;
                    }
                    if (string.IsNullOrEmpty(childId))
                    {
                        throw new InvalidOperationException(
                            $"Constructed Custom row '{path}.{entry.schemaKey}' references an empty value id.");
                    }
                    string key = entry.schemaKey;
                    ValidateConstructedValueLink(
                        client,
                        attribute,
                        childId,
                        replacement => row.value[key] = replacement,
                        row.mapKey,
                        customTypeId,
                        stagedById,
                        reachableStagedIds,
                        ownedByPath,
                        pending,
                        $"{path}.{entry.schemaKey}",
                        traversal,
                        env,
                        referenceOwnershipByPath);
                }
            }
            finally
            {
                traversal.Remove(row.id);
            }
        }

        private static void ValidateConstructedValueLink(
            NeoClient client,
            Attribute attribute,
            string valueId,
            Action<string> replaceValueId,
            string? parentMapKey,
            string? parentTypeId,
            IReadOnlyDictionary<string, AttributeValue> stagedById,
            HashSet<string> reachableStagedIds,
            Dictionary<string, string> ownedByPath,
            List<PendingConstructorReference> pending,
            string path,
            HashSet<string> traversal,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            IReadOnlyDictionary<string, NeoValueOwnership>?
                referenceOwnershipByPath,
            string? expectedContainerId = null)
        {
            if (ownedByPath.TryGetValue(valueId, out string? priorPath))
            {
                throw new InvalidOperationException(
                    $"Constructed value '{valueId}' would have two owned parents ('{priorPath}' and '{path}'). Call Clone() explicitly for a second owner.");
            }
            ownedByPath[valueId] = path;
            string? expectedMapKey = client.ResolveCreatedValueMapKey(
                attribute,
                parentMapKey,
                parentTypeId);

            if (!stagedById.TryGetValue(valueId, out AttributeValue? row))
            {
                if (attribute is not CustomAttribute customAttribute)
                {
                    throw new InvalidOperationException(
                        $"Constructed field '{path}' references unstaged value '{valueId}' for non-Custom attribute '{attribute.id}'.");
                }
                bool sourceExists = referenceOwnershipByPath is not null
                    && referenceOwnershipByPath.TryGetValue(
                        path,
                        out NeoValueOwnership suppliedOwnership)
                        ? client.TryGetValue(
                            suppliedOwnership,
                            valueId,
                            out ObjectAttributeValue? source)
                        : client.TryGetValue(
                            valueId,
                            out source);
                if (!sourceExists)
                {
                    throw new InvalidOperationException(
                        $"Constructed Custom field '{path}' references missing object value '{valueId}'.");
                }
                string actualTypeId = source!.typeId ?? customAttribute.customTypeId;
                if (!IsAssignableCustomType(
                        client,
                        actualTypeId,
                        customAttribute.customTypeId))
                {
                    throw new InvalidOperationException(
                        $"Constructed Custom field '{path}' expects '{customAttribute.customTypeId}' but value '{valueId}' has runtime type '{actualTypeId}'.");
                }
                if (!MapKeyCanMoveTo(source.mapKey, expectedMapKey))
                {
                    throw new InvalidOperationException(
                        $"Constructed Custom field '{path}' cannot attach value '{valueId}' from partition '{source.mapKey ?? "main"}' to '{expectedMapKey ?? "main"}'.");
                }
                pending.Add(new PendingConstructorReference
                {
                    sourceValueId = valueId,
                    attribute = attribute,
                    path = path,
                    expectedMapKey = expectedMapKey,
                    expectedContainerId = expectedContainerId,
                    replaceValueId = replaceValueId,
                });
                return;
            }

            reachableStagedIds.Add(valueId);
            if (!MapKeyCanMoveTo(row.mapKey, expectedMapKey))
            {
                throw new InvalidOperationException(
                    $"Constructed field '{path}' carries partition '{row.mapKey ?? "main"}' but resolves to '{expectedMapKey ?? "main"}'.");
            }
            row.mapKey = expectedMapKey;
            if (expectedContainerId is not null)
            {
                if (!string.IsNullOrEmpty(row.containerId)
                    && row.containerId != expectedContainerId)
                {
                    throw new InvalidOperationException(
                        $"Constructed field '{path}' already belongs to unordered list '{row.containerId}', expected '{expectedContainerId}'.");
                }
                row.containerId = expectedContainerId;
            }
            ValidateConstructedRowShape(client, attribute, row, path);

            switch (attribute)
            {
                case CustomAttribute customAttribute
                    when row is ObjectAttributeValue customRow
                    && customRow.value is not null:
                {
                    string actualTypeId = customRow.typeId
                        ?? customAttribute.customTypeId;
                    if (!IsAssignableCustomType(
                            client,
                            actualTypeId,
                            customAttribute.customTypeId))
                    {
                        throw new InvalidOperationException(
                            $"Constructed Custom field '{path}' expects '{customAttribute.customTypeId}' but staged row '{valueId}' has runtime type '{actualTypeId}'.");
                    }
                    customRow.typeId = actualTypeId;
                    ValidateConstructedCustomRow(
                        client,
                        customRow,
                        actualTypeId,
                        stagedById,
                        reachableStagedIds,
                        ownedByPath,
                        pending,
                        path,
                        traversal,
                        referenceOwnershipByPath);
                    break;
                }
                case ListAttribute listAttribute
                    when row is ArrayAttributeValue listRow
                    && listRow.value is not null:
                {
                    if (!client.TryGetAttribute(
                            listAttribute.entryAttributeId,
                            out Attribute? entryAttribute))
                    {
                        throw new InvalidOperationException(
                            $"Constructed List field '{path}' references missing entry attribute '{listAttribute.entryAttributeId}'.");
                    }
                    entryAttribute = NeoGenericResolution.SubstituteAttribute(
                        client,
                        entryAttribute,
                        env);
                    bool isUnordered = client.IsUnorderedList(listAttribute);
                    var memberIds = new List<string>(listRow.value);
                    if (isUnordered)
                    {
                        // A low-level generated constructor may already carry
                        // canonical unordered membership on staged rows. The
                        // shared runtime materializer temporarily carries ids
                        // inline so external Custom references can participate
                        // in the same ownership validation before publication.
                        foreach (AttributeValue stagedRow in stagedById.Values)
                        {
                            if (stagedRow.containerId == listRow.id
                                && !memberIds.Contains(stagedRow.id))
                            {
                                memberIds.Add(stagedRow.id);
                            }
                        }
                    }
                    for (int index = 0; index < memberIds.Count; index++)
                    {
                        int capturedIndex = index;
                        ValidateConstructedValueLink(
                            client,
                            entryAttribute,
                            memberIds[index],
                            isUnordered
                                ? _ => { }
                                : replacement => listRow.value[capturedIndex] = replacement,
                            listRow.mapKey,
                            listRow.typeId,
                            stagedById,
                            reachableStagedIds,
                            ownedByPath,
                            pending,
                            $"{path}[{index}]",
                            traversal,
                            env,
                            referenceOwnershipByPath,
                            expectedContainerId: isUnordered
                                ? listRow.id
                                : null);
                    }
                    if (isUnordered)
                    {
                        // Unordered List payload is only the present/null
                        // discriminator; membership lives on entry rows.
                        listRow.value = Array.Empty<string>();
                    }
                    break;
                }
                case DictionaryAttribute dictionaryAttribute
                    when row is ObjectAttributeValue dictionaryRow
                    && dictionaryRow.value is not null:
                {
                    if (!client.TryGetAttribute(
                            dictionaryAttribute.entryAttributeId,
                            out Attribute? entryAttribute))
                    {
                        throw new InvalidOperationException(
                            $"Constructed Dictionary field '{path}' references missing entry attribute '{dictionaryAttribute.entryAttributeId}'.");
                    }
                    entryAttribute = NeoGenericResolution.SubstituteAttribute(
                        client,
                        entryAttribute,
                        env);
                    foreach (string key in new List<string>(dictionaryRow.value.Keys))
                    {
                        string capturedKey = key;
                        ValidateConstructedValueLink(
                            client,
                            entryAttribute,
                            dictionaryRow.value[key],
                            replacement => dictionaryRow.value[capturedKey] = replacement,
                            dictionaryRow.mapKey,
                            dictionaryRow.typeId,
                            stagedById,
                            reachableStagedIds,
                            ownedByPath,
                            pending,
                            $"{path}[{key}]",
                            traversal,
                            env,
                            referenceOwnershipByPath);
                    }
                    break;
                }
            }
        }

        private static void ValidateConstructedRowShape(
            NeoClient client,
            Attribute attribute,
            AttributeValue row,
            string path)
        {
            bool shapeMatches = attribute switch
            {
                NullAttribute => row is NullAttributeValue,
                BoolAttribute => row is BoolAttributeValue,
                IntAttribute => row is NumberAttributeValue number
                    && (number.value is null
                        || number.value.Value == Math.Truncate(number.value.Value)),
                FloatAttribute => row is NumberAttributeValue,
                StringAttribute or DecimalAttribute => row is StringAttributeValue,
                DictionaryAttribute or CustomAttribute => row is ObjectAttributeValue,
                ListAttribute or EnumAttribute or LookupAttribute or DialogueLookupAttribute =>
                    row is ArrayAttributeValue,
                SpriteAttribute => row is SpriteAttributeValue,
                AudioAttribute => row is FileAttributeValue,
                Vector2Attribute or Vector2IntAttribute => row is Vector2AttributeValue,
                Vector3Attribute or Vector3IntAttribute => row is Vector3AttributeValue,
                ColorAttribute => row is ColorAttributeValue,
                _ => false,
            };
            if (!shapeMatches)
            {
                throw new InvalidOperationException(
                    $"Constructed field '{path}' has row shape '{row.GetType().Name}', incompatible with schema attribute '{attribute.id}' ({attribute.type}).");
            }
            if (attribute.required && IsNullStoredValue(row))
            {
                throw new InvalidOperationException(
                    $"Constructed required field '{path}' has a null value.");
            }
            if (attribute is DecimalAttribute
                && row is StringAttributeValue decimalRow
                && decimalRow.value is not null
                && NeoDecimalValues.GetViolation(decimalRow.value)
                    != NeoDecimalValues.Violation.None)
            {
                throw new InvalidOperationException(
                    $"Constructed Decimal field '{path}' is not a canonical decimal value.");
            }
            if (attribute is CustomAttribute customAttribute
                && row is ObjectAttributeValue customRow)
            {
                string actualTypeId = customRow.typeId
                    ?? customAttribute.customTypeId;
                if (!IsAssignableCustomType(
                        client,
                        actualTypeId,
                        customAttribute.customTypeId))
                {
                    throw new InvalidOperationException(
                        $"Constructed Custom field '{path}' has incompatible runtime type '{actualTypeId}'.");
                }
            }
        }

        private static bool IsNullStoredValue(AttributeValue row)
        {
            return row switch
            {
                NullAttributeValue => true,
                BoolAttributeValue value => value.value is null,
                NumberAttributeValue value => value.value is null,
                StringAttributeValue value => value.value is null,
                ArrayAttributeValue value => value.value is null,
                ObjectAttributeValue value => value.value is null,
                SpriteAttributeValue value => value.value is null,
                FileAttributeValue value => value.value is null,
                Vector2AttributeValue value => value.value is null,
                Vector3AttributeValue value => value.value is null,
                ColorAttributeValue value => value.value is null,
                _ => true,
            };
        }

        private static bool IsAssignableCustomType(
            NeoClient client,
            string actualTypeId,
            string expectedTypeId)
        {
            if (!client.TryGetType(actualTypeId, out CustomType? _)) return false;
            try
            {
                foreach (CustomType type in CustomTypeInheritance.ResolveChain(
                    actualTypeId,
                    id => client.TryGetType(id, out CustomType? candidate)
                        ? candidate
                        : null))
                {
                    if (type.id == expectedTypeId) return true;
                }
            }
            catch (CircularInheritanceError)
            {
                return false;
            }
            return false;
        }

        private static bool MapKeyCanMoveTo(
            string? currentMapKey,
            string? expectedMapKey)
        {
            return string.IsNullOrEmpty(currentMapKey)
                || currentMapKey == expectedMapKey;
        }

        private static void StampImportedConstructorGraph(
            NeoClient client,
            string valueId,
            Attribute attribute,
            string? expectedMapKey,
            HashSet<string> visited,
            bool requireValue = true,
            string? expectedContainerId = null)
        {
            if (!visited.Add(valueId)) return;
            if (!client.TryGetValue(
                    NeoValueOwnership.Session,
                    valueId,
                    out AttributeValue? row))
            {
                // Stable-id authored aggregates may contain sparse child
                // references whose value row resolves through the schema
                // default rather than a stored row. Ordinary assignment/import
                // preserves those ids; constructor attachment must do the same.
                if (!requireValue) return;
                throw new InvalidOperationException(
                    $"Imported constructor value '{valueId}' is missing from Session storage.");
            }
            if (!MapKeyCanMoveTo(row!.mapKey, expectedMapKey))
            {
                throw new InvalidOperationException(
                    $"Imported constructor value '{valueId}' has partition '{row.mapKey ?? "main"}', expected '{expectedMapKey ?? "main"}'.");
            }
            if (!string.IsNullOrEmpty(row.containerId)
                && expectedContainerId is not null
                && row.containerId != expectedContainerId)
            {
                throw new InvalidOperationException(
                    $"Imported constructor value '{valueId}' already belongs to unordered list '{row.containerId}', expected '{expectedContainerId}'.");
            }
            if (row.mapKey != expectedMapKey
                || (expectedContainerId is not null
                    && row.containerId != expectedContainerId))
            {
                AttributeValue writable = client.CloneRowForWrite(row);
                writable.mapKey = expectedMapKey;
                if (expectedContainerId is not null)
                {
                    writable.containerId = expectedContainerId;
                }
                client.SetWritableValue(NeoValueOwnership.Session, writable);
                row = writable;
            }

            switch (attribute)
            {
                case CustomAttribute customAttribute
                    when row is ObjectAttributeValue customRow
                    && customRow.value is not null:
                {
                    string actualTypeId = customRow.typeId
                        ?? customAttribute.customTypeId;
                    IList<MergedSchemaEntry> schema = ResolveMergedSchema(
                        client,
                        actualTypeId);
                    var env = NeoGenericResolution.ResolveInstanceEnv(
                        client,
                        actualTypeId,
                        customTypeArguments: null);
                    foreach (MergedSchemaEntry entry in schema)
                    {
                        if (!customRow.value.TryGetValue(
                                entry.schemaKey,
                                out string? childId)
                            || !client.TryGetAttribute(
                                entry.attributeId,
                                out Attribute? childAttribute))
                        {
                            continue;
                        }
                        childAttribute = NeoGenericResolution.SubstituteAttribute(
                            client,
                            childAttribute,
                            env);
                        if (!IsStoredConstructorAttribute(childAttribute)) continue;
                        string? childMapKey = client.ResolveCreatedValueMapKey(
                            childAttribute,
                            row.mapKey,
                            actualTypeId);
                        StampImportedConstructorGraph(
                            client,
                            childId,
                            childAttribute,
                            childMapKey,
                            visited,
                            requireValue: false);
                    }
                    break;
                }
                case ListAttribute listAttribute
                    when row is ArrayAttributeValue listRow
                    && listRow.value is not null
                    && client.TryGetAttribute(
                        listAttribute.entryAttributeId,
                        out Attribute? entryAttribute):
                    foreach (string childId in listRow.value)
                    {
                        string? childMapKey = client.ResolveCreatedValueMapKey(
                            entryAttribute,
                            row.mapKey,
                            row.typeId);
                        StampImportedConstructorGraph(
                            client,
                            childId,
                            entryAttribute,
                            childMapKey,
                            visited,
                            requireValue: false);
                    }
                    break;
                case DictionaryAttribute dictionaryAttribute
                    when row is ObjectAttributeValue dictionaryRow
                    && dictionaryRow.value is not null
                    && client.TryGetAttribute(
                        dictionaryAttribute.entryAttributeId,
                        out Attribute? entryAttribute):
                    foreach (string childId in dictionaryRow.value.Values)
                    {
                        string? childMapKey = client.ResolveCreatedValueMapKey(
                            entryAttribute,
                            row.mapKey,
                            row.typeId);
                        StampImportedConstructorGraph(
                            client,
                            childId,
                            entryAttribute,
                            childMapKey,
                            visited,
                            requireValue: false);
                    }
                    break;
            }
        }

        /// <summary>
        /// Materializes the shared NeoScript <c>customConstructor</c>
        /// intrinsic through the same Session-backed value graph used by
        /// generated public C# constructors. Explicit fields are applied by
        /// schema/attribute id; ordinary required defaults are then filled by
        /// <see cref="CreateWritableCustomValue"/>.
        /// </summary>
        internal static NeoAttributeCustomWritable CreateRuntimeCustomValue(
            NeoClient client,
            CustomTypeInfo customTypeInfo,
            IReadOnlyList<RuntimeConstructorField> fields,
            Func<object?, NeoConstructorValueReference?> valueReference)
        {
            return CreateSuppliedCustomValue(
                client,
                customTypeInfo,
                fields,
                valueReference);
        }

        private static NeoAttributeCustomWritable CreateSuppliedCustomValue(
            NeoClient client,
            CustomTypeInfo customTypeInfo,
            IReadOnlyList<RuntimeConstructorField> fields,
            Func<object?, NeoConstructorValueReference?> valueReference)
        {
            RuntimeConstructorMetadata metadata =
                ValidateRuntimeCustomConstructorMetadataCore(
                    client,
                    customTypeInfo,
                    fields);

            var value = new Dictionary<string, string>();
            var rows = new List<AttributeValue>();
            var referenceOwnershipByPath =
                new Dictionary<string, NeoValueOwnership>();
            string nowIso = DateTime.UtcNow.ToString("o");
            foreach (RuntimeConstructorField field in fields)
            {
                Attribute attribute = metadata.attributesBySchemaKey[field.schemaKey];
                if (field.value is null
                    && !RequiresRuntimeConstructorArgument(attribute))
                {
                    // Matches generated C# optional parameters: null means the
                    // field is omitted and its ordinary constructor/default
                    // behavior applies.
                    continue;
                }
                string? fieldValueId = MaterializeRuntimeConstructorValue(
                    client,
                    attribute,
                    field.value,
                    rows,
                    nowIso,
                    valueReference,
                    metadata.genericEnv,
                    $"{customTypeInfo.typeId}.{field.schemaKey}",
                    referenceOwnershipByPath);
                if (fieldValueId is not null)
                {
                    value[field.schemaKey] = fieldValueId;
                }
            }
            return CreateWritableCustomValueCore(
                client,
                customTypeInfo.typeId,
                value,
                rows,
                referenceOwnershipByPath);
        }

        /// <summary>
        /// Validates all schema/type/field metadata carried by constructor IR
        /// without inspecting argument values. The evaluator invokes this
        /// before evaluating any argument pointer, matching NeoScript's
        /// compile-time call-shape ordering and preventing stale IR from
        /// running argument side effects.
        /// </summary>
        internal static void ValidateRuntimeCustomConstructorMetadata(
            NeoClient client,
            CustomTypeInfo customTypeInfo,
            IReadOnlyList<RuntimeConstructorField> fields)
        {
            ValidateRuntimeCustomConstructorMetadataCore(
                client,
                customTypeInfo,
                fields);
        }

        private static RuntimeConstructorMetadata
            ValidateRuntimeCustomConstructorMetadataCore(
                NeoClient client,
                CustomTypeInfo customTypeInfo,
                IReadOnlyList<RuntimeConstructorField> fields)
        {
            if (!client.TryGetType(customTypeInfo.typeId, out CustomType? customType))
            {
                throw new InvalidOperationException(
                    $"NeoScript construction references missing Custom type '{customTypeInfo.typeId}'.");
            }
            if (customType!.isAbstract)
            {
                throw new InvalidOperationException(
                    $"Cannot construct abstract Custom type '{customType.name}'.");
            }
            if (customTypeInfo.type != AttributeType.Custom)
            {
                throw new InvalidOperationException(
                    $"Custom constructor for '{customTypeInfo.typeId}' carries non-Custom runtime type metadata '{customTypeInfo.type}'.");
            }
            if (client.TryResolveCustomTypeAllowedOwnership(
                    customTypeInfo.typeId,
                    out NeoValueOwnership allowedOwnership)
                && allowedOwnership == NeoValueOwnership.Asset)
            {
                throw new InvalidOperationException(
                    $"Cannot construct immutable-only Custom type '{customType.name}'.");
            }
            var genericEnv = NeoGenericResolution.ResolveInstanceEnv(
                client,
                customTypeInfo.typeId,
                customTypeArguments: null);
            string? unboundParamId = NeoGenericResolution.FirstUnboundParamId(
                genericEnv);
            if (unboundParamId is not null)
            {
                throw new InvalidOperationException(
                    $"Cannot construct open generic Custom type '{customType.name}'; generic param '{unboundParamId}' is unbound. Construct a closed named descendant.");
            }
            ValidateRuntimeConstructorTypeArguments(
                client,
                customTypeInfo,
                genericEnv);
            IList<MergedSchemaEntry> schema = ResolveMergedSchema(
                client,
                customTypeInfo.typeId);
            var schemaByKey = new Dictionary<string, MergedSchemaEntry>();
            var attributesBySchemaKey = new Dictionary<string, Attribute>();
            foreach (MergedSchemaEntry entry in schema)
            {
                if (!schemaByKey.TryAdd(entry.schemaKey, entry))
                {
                    throw new InvalidOperationException(
                        $"Custom constructor schema for '{customTypeInfo.typeId}' contains duplicate merged key '{entry.schemaKey}'.");
                }
            }

            var suppliedSchemaKeys = new HashSet<string>();
            foreach (RuntimeConstructorField field in fields)
            {
                if (!suppliedSchemaKeys.Add(field.schemaKey))
                {
                    throw new InvalidOperationException(
                        $"Custom constructor for '{customTypeInfo.typeId}' contains duplicate field '{field.schemaKey}'.");
                }
            }
            foreach (MergedSchemaEntry entry in schema)
            {
                if (!client.TryGetAttribute(entry.attributeId, out Attribute? attribute))
                {
                    throw new InvalidOperationException(
                        $"Custom constructor schema field '{entry.schemaKey}' references missing attribute '{entry.attributeId}'.");
                }
                attribute = NeoGenericResolution.SubstituteAttribute(
                    client,
                    attribute,
                    genericEnv);
                attributesBySchemaKey[entry.schemaKey] = attribute;
                if (!IsStoredConstructorAttribute(attribute)) continue;
                if (RequiresRuntimeConstructorArgument(attribute)
                    && !suppliedSchemaKeys.Contains(entry.schemaKey))
                {
                    throw new InvalidOperationException(
                        $"Custom constructor for '{customTypeInfo.typeId}' is missing required field '{entry.schemaKey}'/'{entry.attributeId}'. Regenerate the NeoScript IR from the current schema.");
                }
            }
            foreach (RuntimeConstructorField field in fields)
            {
                if (!schemaByKey.TryGetValue(field.schemaKey, out MergedSchemaEntry? entry)
                    || entry.attributeId != field.attributeId)
                {
                    throw new InvalidOperationException(
                        $"Custom constructor for '{customTypeInfo.typeId}' contains stale field '{field.schemaKey}'/'{field.attributeId}'. Regenerate the NeoScript IR from the current schema.");
                }
                Attribute attribute = attributesBySchemaKey[field.schemaKey];
                if (!IsStoredConstructorAttribute(attribute))
                {
                    throw new InvalidOperationException(
                        $"Custom constructor field '{field.schemaKey}' references non-stored attribute '{entry.attributeId}'.");
                }
            }
            return new RuntimeConstructorMetadata
            {
                attributesBySchemaKey = attributesBySchemaKey,
                genericEnv = genericEnv,
            };
        }

        private static void ValidateRuntimeConstructorTypeArguments(
            NeoClient client,
            CustomTypeInfo customTypeInfo,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv)
        {
            if (customTypeInfo.typeArguments is null) return;
            foreach (var pair in customTypeInfo.typeArguments)
            {
                if (!genericEnv.TryGetValue(pair.Key, out NeoGenericEnvEntry? binding)
                    || !binding.IsBound
                    || string.IsNullOrEmpty(binding.attributeId))
                {
                    throw new InvalidOperationException(
                        $"Custom constructor type argument '{pair.Key}' is not a bound parameter of closed type '{customTypeInfo.typeId}'.");
                }
                if (!client.TryGetAttribute(binding.attributeId!, out Attribute? bindingAttribute))
                {
                    throw new InvalidOperationException(
                        $"Custom constructor type argument '{pair.Key}' references missing binding attribute '{binding.attributeId}'.");
                }
                if (!RuntimeConstructorTypeMatchesAttribute(
                        client,
                        pair.Value,
                        bindingAttribute))
                {
                    throw new InvalidOperationException(
                        $"Custom constructor type argument '{pair.Key}' does not match closed type '{customTypeInfo.typeId}' binding attribute '{binding.attributeId}'.");
                }
            }
        }

        private static bool RuntimeConstructorTypeMatchesAttribute(
            NeoClient client,
            TypeInfo typeInfo,
            Attribute attribute)
        {
            if (typeInfo.type != attribute.type
                || typeInfo.required != attribute.required)
            {
                return false;
            }
            if (typeInfo is CustomTypeInfo customType
                && attribute is CustomAttribute customAttribute)
            {
                return customType.typeId == customAttribute.customTypeId;
            }
            if (typeInfo is EnumTypeInfo enumType
                && attribute is EnumAttribute enumAttribute)
            {
                return enumType.enumId == enumAttribute.enumId;
            }
            if (typeInfo is CollectionTypeInfo collectionType
                && attribute is ListAttribute listAttribute
                && client.TryGetAttribute(
                    listAttribute.entryAttributeId,
                    out Attribute? listEntry))
            {
                return RuntimeConstructorTypeMatchesAttribute(
                    client,
                    collectionType.entryTypeInfo,
                    listEntry);
            }
            if (typeInfo is CollectionTypeInfo dictionaryType
                && attribute is DictionaryAttribute dictionaryAttribute
                && client.TryGetAttribute(
                    dictionaryAttribute.entryAttributeId,
                    out Attribute? dictionaryEntry))
            {
                return RuntimeConstructorTypeMatchesAttribute(
                    client,
                    dictionaryType.entryTypeInfo,
                    dictionaryEntry);
            }
            return true;
        }

        private static bool RequiresRuntimeConstructorArgument(
            Attribute attribute)
        {
            return attribute.required && !HasExplicitDefaultValue(attribute);
        }

        private static bool IsStoredConstructorAttribute(Attribute attribute)
        {
            return !attribute.isStatic
                && attribute is not NSPropertyAttribute
                && attribute is not FunctionAttribute
                && attribute is not NSFunctionAttribute;
        }

        private static bool HasExplicitDefaultValue(Attribute attribute)
        {
            return attribute switch
            {
                NullAttribute attr => attr.defaultValue is not null,
                BoolAttribute attr => attr.defaultValue is not null,
                IntAttribute attr => attr.defaultValue is not null,
                FloatAttribute attr => attr.defaultValue is not null,
                StringAttribute attr => attr.defaultValue is not null,
                DictionaryAttribute attr => attr.defaultValue is not null,
                ListAttribute attr => attr.defaultValue is not null,
                CustomAttribute attr => attr.defaultValue is not null,
                GenericAttribute attr => attr.defaultValue is not null,
                EnumAttribute attr => attr.defaultValue is not null,
                LookupAttribute attr => attr.defaultValue is not null,
                DialogueLookupAttribute attr => attr.defaultValue is not null,
                SpriteAttribute attr => attr.defaultValue is not null,
                AudioAttribute attr => attr.defaultValue is not null,
                Vector2Attribute attr => attr.defaultValue is not null,
                Vector2IntAttribute attr => attr.defaultValue is not null,
                Vector3Attribute attr => attr.defaultValue is not null,
                Vector3IntAttribute attr => attr.defaultValue is not null,
                ColorAttribute attr => attr.defaultValue is not null,
                DecimalAttribute attr => attr.defaultValue is not null,
                _ => false,
            };
        }

        private static string? MaterializeRuntimeConstructorValue(
            NeoClient client,
            Attribute attribute,
            object? runtimeValue,
            List<AttributeValue> rows,
            string nowIso,
            Func<object?, NeoConstructorValueReference?> valueReference,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> genericEnv,
            string path,
            Dictionary<string, NeoValueOwnership> referenceOwnershipByPath,
            bool preserveOptionalNull = false)
        {
            if (runtimeValue is null)
            {
                if (attribute.required)
                {
                    throw new InvalidOperationException(
                        $"Required constructor field '{attribute.name}' received null.");
                }
                if (!preserveOptionalNull) return null;
            }

            if (runtimeValue is not null && attribute is CustomAttribute)
            {
                NeoConstructorValueReference? source = valueReference(runtimeValue);
                if (source is null || string.IsNullOrEmpty(source.Value.valueId))
                {
                    throw new InvalidOperationException(
                        $"Custom constructor field '{attribute.name}' is not backed by a Neo value.");
                }
                // Ownership import is deliberately deferred until the entire
                // staged constructor graph has passed schema/shape validation.
                // PrepareConstructedGraph then applies the same ordinary
                // parentless-attach / already-owned rejection rule used by
                // generated C# constructors and normal assignments.
                if (source.Value.ownership is NeoValueOwnership ownership)
                {
                    referenceOwnershipByPath[path] = ownership;
                }
                return source.Value.valueId;
            }

            NeoValuePayload? wrappedPayload = runtimeValue
                is INeoValuePayloadProvider provider
                    ? provider.ToNeoValuePayload()
                    : null;
            object? suppliedValue = wrappedPayload?.value ?? runtimeValue;
            if (suppliedValue is null && attribute.required)
            {
                throw new InvalidOperationException(
                    $"Required constructor field '{attribute.name}' received null.");
            }
            bool materializeExplicitNull = suppliedValue is null;
            object? payload = suppliedValue;
            string valueId = Guid.NewGuid().ToString();
            if (materializeExplicitNull)
            {
                // A null optional field is normally omitted. Once it appears
                // inside a collection, however, it is a real element and must
                // become the correctly typed null row (including Array/Object
                // rows for enum, lookup, nested list, and nested dictionary
                // entries) so list positions and dictionary keys are stable.
                payload = null;
            }
            else if (attribute is ListAttribute listAttribute)
            {
                if (!client.TryGetAttribute(
                        listAttribute.entryAttributeId,
                        out Attribute? entryAttribute))
                {
                    throw new InvalidOperationException(
                        $"List constructor field '{attribute.name}' references missing entry attribute '{listAttribute.entryAttributeId}'.");
                }
                entryAttribute = NeoGenericResolution.SubstituteAttribute(
                    client,
                    entryAttribute,
                    genericEnv);
                var ids = new List<string>();
                if (suppliedValue is System.Collections.IEnumerable enumerable
                    && suppliedValue is not string)
                {
                    foreach (object? item in enumerable)
                    {
                        string? id = MaterializeRuntimeConstructorValue(
                            client,
                            entryAttribute,
                            item,
                            rows,
                            nowIso,
                            valueReference,
                            genericEnv,
                            $"{path}[{ids.Count}]",
                            referenceOwnershipByPath,
                            preserveOptionalNull: true);
                        if (id is null)
                        {
                            throw new InvalidOperationException(
                                $"List constructor field '{attribute.name}' failed to materialize an entry.");
                        }
                        ids.Add(id);
                    }
                }
                else
                {
                    throw new InvalidOperationException(
                        $"List constructor field '{attribute.name}' requires a collection value.");
                }
                payload = ids.ToArray();
            }
            else if (attribute is DictionaryAttribute dictionaryAttribute)
            {
                if (!client.TryGetAttribute(
                        dictionaryAttribute.entryAttributeId,
                        out Attribute? entryAttribute))
                {
                    throw new InvalidOperationException(
                        $"Dictionary constructor field '{attribute.name}' references missing entry attribute '{dictionaryAttribute.entryAttributeId}'.");
                }
                entryAttribute = NeoGenericResolution.SubstituteAttribute(
                    client,
                    entryAttribute,
                    genericEnv);
                if (!TryEnumerateConstructorDictionary(
                        suppliedValue!,
                        out IEnumerable<NeoGeneratedConstructorDictionaryEntry>?
                            dictionaryEntries))
                {
                    throw new InvalidOperationException(
                        $"Dictionary constructor field '{attribute.name}' requires a dictionary value.");
                }
                var ids = new Dictionary<string, string>();
                foreach (NeoGeneratedConstructorDictionaryEntry pair in
                         dictionaryEntries)
                {
                    string key = pair.Key switch
                    {
                        INeoEnumOption option => option.optionId,
                        string text => text,
                        null => throw new InvalidOperationException(
                            $"Dictionary constructor field '{attribute.name}' contains a null key."),
                        _ => pair.Key.ToString()
                            ?? throw new InvalidOperationException(
                                $"Dictionary constructor field '{attribute.name}' contains an invalid key."),
                    };
                    string? id = MaterializeRuntimeConstructorValue(
                        client,
                        entryAttribute,
                        pair.Value,
                        rows,
                        nowIso,
                        valueReference,
                        genericEnv,
                        $"{path}[{key}]",
                        referenceOwnershipByPath,
                        preserveOptionalNull: true);
                    if (id is null)
                    {
                        throw new InvalidOperationException(
                            $"Dictionary constructor field '{attribute.name}' failed to materialize key '{key}'.");
                    }
                    ids[key] = id;
                }
                payload = ids;
            }
            else if (attribute is EnumAttribute enumAttribute)
            {
                payload = ConstructorEnumOptionIds(
                    suppliedValue!, enumAttribute);
            }
            else if (attribute is LookupAttribute lookupAttribute)
            {
                payload = ConstructorLookupIds(suppliedValue!, lookupAttribute);
            }
            else if (attribute is DialogueLookupAttribute dialogueAttribute)
            {
                payload = ConstructorDialogueIds(
                    suppliedValue!, dialogueAttribute);
            }
            else
            {
                payload = NormalizeGeneratedConstructorScalar(
                    client,
                    attribute,
                    suppliedValue);
            }

            if (wrappedPayload is not null)
            {
                payload = new NeoValuePayload(
                    payload,
                    wrappedPayload.typeId,
                    wrappedPayload.valueRows);
            }

            if (payload is NeoValuePayload finalWrappedPayload)
            {
                rows.AddRange(finalWrappedPayload.valueRows);
            }
            AttributeValue row = AttributeValueFactory.Create(
                attribute,
                payload,
                valueId,
                nowIso,
                nowIso);
            NeoGenericResolution.StampGenericBindings(
                client,
                attribute,
                row,
                genericEnv);
            rows.Add(row);
            return valueId;
        }

        private static string[] ConstructorEnumOptionIds(
            object runtimeValue,
            EnumAttribute attribute)
        {
            if (runtimeValue is string optionId)
            {
                return new[] { optionId };
            }
            if (runtimeValue is INeoEnumOption option)
            {
                return new[] { option.optionId };
            }
            if (runtimeValue is not System.Collections.IEnumerable values)
            {
                throw new InvalidOperationException(
                    $"Enum constructor field '{attribute.name}' requires an enum option or option collection.");
            }
            var optionIds = new List<string>();
            foreach (object? value in values)
            {
                string? id = value switch
                {
                    string text => text,
                    INeoEnumOption enumOption => enumOption.optionId,
                    _ => null,
                };
                if (string.IsNullOrEmpty(id))
                {
                    throw new InvalidOperationException(
                        $"Enum constructor field '{attribute.name}' contains an invalid option.");
                }
                optionIds.Add(id!);
            }
            string[] result = optionIds.ToArray();
            ValidateConstructorSelectionCardinality(
                result,
                attribute.multiselect,
                attribute.name,
                "Enum");
            return result;
        }

        private static bool TryEnumerateConstructorDictionary(
            object value,
            out IEnumerable<NeoGeneratedConstructorDictionaryEntry> entries)
        {
            if (value is INeoGeneratedConstructorDictionary generated)
            {
                entries = generated.EnumerateGeneratedConstructorEntries();
                return true;
            }
            if (value is System.Collections.IDictionary dictionary)
            {
                entries = EnumerateNonGenericConstructorDictionary(dictionary);
                return true;
            }
            if (value is System.Collections.IEnumerable enumerable
                && IsGenericStringKeyDictionary(value.GetType()))
            {
                entries = EnumerateGenericConstructorDictionary(enumerable);
                return true;
            }

            entries = Array.Empty<NeoGeneratedConstructorDictionaryEntry>();
            return false;
        }

        private static IEnumerable<NeoGeneratedConstructorDictionaryEntry>
            EnumerateNonGenericConstructorDictionary(
                System.Collections.IDictionary dictionary)
        {
            foreach (System.Collections.DictionaryEntry pair in dictionary)
            {
                yield return new NeoGeneratedConstructorDictionaryEntry(
                    pair.Key,
                    pair.Value);
            }
        }

        private static bool IsGenericStringKeyDictionary(Type type)
        {
            lock (ConstructorDictionaryShapeLock)
            {
                if (ConstructorDictionaryShapeCache.TryGetValue(
                        type,
                        out bool cached))
                {
                    return cached;
                }
            }

            bool matches = false;
            foreach (Type contract in type.GetInterfaces())
            {
                if (!contract.IsGenericType) continue;
                Type definition = contract.GetGenericTypeDefinition();
                if ((definition == typeof(IDictionary<,>)
                        || definition == typeof(IReadOnlyDictionary<,>))
                    && contract.GetGenericArguments()[0] == typeof(string))
                {
                    matches = true;
                    break;
                }
            }
            lock (ConstructorDictionaryShapeLock)
            {
                ConstructorDictionaryShapeCache[type] = matches;
            }
            return matches;
        }

        private static IEnumerable<NeoGeneratedConstructorDictionaryEntry>
            EnumerateGenericConstructorDictionary(
                System.Collections.IEnumerable dictionary)
        {
            foreach (object? pair in dictionary)
            {
                if (pair is null)
                {
                    throw new InvalidOperationException(
                        "Generated constructor dictionary yielded a null entry.");
                }
                ConstructorKeyValuePairAccessors? accessors =
                    ConstructorDictionaryAccessors(pair.GetType());
                if (accessors is null)
                {
                    throw new InvalidOperationException(
                        $"Generated constructor dictionary yielded unsupported entry type '{pair.GetType().FullName}'.");
                }
                yield return new NeoGeneratedConstructorDictionaryEntry(
                    accessors.key.GetValue(pair),
                    accessors.value.GetValue(pair));
            }
        }

        private static ConstructorKeyValuePairAccessors?
            ConstructorDictionaryAccessors(Type pairType)
        {
            lock (ConstructorDictionaryShapeLock)
            {
                if (ConstructorKeyValuePairAccessorsCache.TryGetValue(
                        pairType,
                        out ConstructorKeyValuePairAccessors? cached))
                {
                    return cached;
                }
            }

            ConstructorKeyValuePairAccessors? result = null;
            if (pairType.IsGenericType
                && pairType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
                && pairType.GetGenericArguments()[0] == typeof(string))
            {
                System.Reflection.PropertyInfo? key = pairType.GetProperty("Key");
                System.Reflection.PropertyInfo? value = pairType.GetProperty("Value");
                if (key is not null && value is not null)
                {
                    result = new ConstructorKeyValuePairAccessors
                    {
                        key = key,
                        value = value,
                    };
                }
            }
            lock (ConstructorDictionaryShapeLock)
            {
                ConstructorKeyValuePairAccessorsCache[pairType] = result;
            }
            return result;
        }

        private static string[] ConstructorLookupIds(
            object runtimeValue,
            LookupAttribute attribute)
        {
            var ids = ConstructorReferenceIds(
                runtimeValue,
                value => value switch
                {
                    NeoLookupSelection selection => selection.valueId,
                    INeoValueReference reference => reference.valueId,
                    string id => id,
                    _ => null,
                },
                $"Lookup constructor field '{attribute.name}'");
            ValidateConstructorSelectionCardinality(
                ids,
                attribute.multiselect,
                attribute.name,
                "Lookup");
            return ids;
        }

        private static string[] ConstructorDialogueIds(
            object runtimeValue,
            DialogueLookupAttribute attribute)
        {
            var ids = ConstructorReferenceIds(
                runtimeValue,
                value => value switch
                {
                    NeoDialogueReference reference => reference.Id,
                    string id => id,
                    _ => null,
                },
                $"DialogueLookup constructor field '{attribute.name}'");
            ValidateConstructorSelectionCardinality(
                ids,
                attribute.multiselect,
                attribute.name,
                "DialogueLookup");
            return ids;
        }

        private static string[] ConstructorReferenceIds(
            object runtimeValue,
            Func<object?, string?> valueId,
            string subject)
        {
            string? singleId = valueId(runtimeValue);
            if (!string.IsNullOrEmpty(singleId)) return new[] { singleId! };
            if (runtimeValue is string
                || runtimeValue is not System.Collections.IEnumerable values)
            {
                throw new InvalidOperationException(
                    $"{subject} requires a reference or reference collection.");
            }
            var ids = new List<string>();
            foreach (object? value in values)
            {
                string? id = valueId(value);
                if (string.IsNullOrEmpty(id))
                {
                    throw new InvalidOperationException(
                        $"{subject} contains an unbound reference.");
                }
                ids.Add(id!);
            }
            return ids.ToArray();
        }

        private static void ValidateConstructorSelectionCardinality(
            string[] ids,
            bool multiselect,
            string attributeName,
            string kind)
        {
            if (!multiselect && ids.Length != 1)
            {
                throw new InvalidOperationException(
                    $"{kind} constructor field '{attributeName}' requires exactly one selection.");
            }
        }

        private static object? NormalizeGeneratedConstructorScalar(
            NeoClient client,
            Attribute attribute,
            object? value)
        {
            switch (attribute)
            {
                case SpriteAttribute sprite when value is Sprite unitySprite:
                    return SpriteValue(
                        client,
                        unitySprite,
                        sprite.templateId,
                        sprite.name);
                case AudioAttribute audio when value is AudioClip unityAudio:
                    return AudioValue(
                        client,
                        unityAudio,
                        audio.templateId,
                        audio.name);
                case DecimalAttribute when value is double or float or int or long or short:
                    return NeoScript.NSGetterEvaluator.CoerceDecimalOperand(
                        value,
                        $"constructor field '{attribute.name}'");
                default:
                    return value;
            }
        }

        private static ObjectAttributeValue CreateWritableCustomValueRow(
            NeoClient client,
            string customTypeId,
            Dictionary<string, string>? providedValue,
            List<AttributeValue> rows,
            string nowIso,
            HashSet<string> customTypeStack,
            IReadOnlyDictionary<string, GenericBinding>? customTypeArguments = null)
        {
            if (!customTypeStack.Add(customTypeId))
            {
                throw new InvalidOperationException(
                    $"Recursive default custom value creation detected for type '{customTypeId}'.");
            }
            try
            {
                var value = providedValue is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(providedValue);

                var mergedSchema = ResolveMergedSchema(client, customTypeId, customTypeArguments);
                // Chain env overlaid with the owning slot's constructed
                // arguments (specs/custom-type-generics.md §4.1) — an
                // instance of the declared open type binds its params
                // through the slot, not a named subtype's chain.
                var env = NeoGenericResolution.ResolveInstanceEnv(
                    client,
                    customTypeId,
                    customTypeArguments);
                foreach (var entry in mergedSchema)
                {
                    if (value.ContainsKey(entry.schemaKey)) continue;
                    if (!client.TryGetAttribute(entry.attributeId, out Attribute? attribute))
                    {
                        throw new InvalidOperationException(
                            $"Custom type '{customTypeId}' schema key '{entry.schemaKey}' references missing attribute '{entry.attributeId}'.");
                    }
                    // Generic slots substitute to their binding before the
                    // required check and default construction — required and
                    // defaultValue travel with the binding
                    // (specs/custom-type-generics.md Decision 10).
                    attribute = NeoGenericResolution.SubstituteAttribute(client, attribute, env);
                    if (!IsStoredConstructorAttribute(attribute)) continue;
                    if (!attribute.required) continue;

                    var defaultRow = CreateDefaultValueRow(
                        client,
                        attribute,
                        rows,
                        nowIso,
                        customTypeStack,
                        env);
                    if (defaultRow is null) continue;

                    rows.Add(defaultRow);
                    value[entry.schemaKey] = defaultRow.id;
                }

                return new ObjectAttributeValue
                {
                    id = Guid.NewGuid().ToString(),
                    createdAt = nowIso,
                    updatedAt = nowIso,
                    value = value,
                    typeId = customTypeId,
                };
            }
            finally
            {
                customTypeStack.Remove(customTypeId);
            }
        }

        private static IList<MergedSchemaEntry> ResolveMergedSchema(
            NeoClient client,
            string customTypeId,
            IReadOnlyDictionary<string, GenericBinding>? customTypeArguments = null)
        {
            if (!client.TryGetType(customTypeId, out CustomType? type))
            {
                throw new InvalidOperationException(
                    $"Cannot create default custom value for missing type '{customTypeId}'.");
            }
            if (type.isAbstract)
            {
                throw new InvalidOperationException(
                    $"Cannot create default custom value for abstract type '{type.name}'.");
            }
            // Instantiability: every param must be bound by the chain OR the
            // owning slot's constructed arguments — `GenericTest<Color>` is
            // instantiable even though the named type is open
            // (specs/custom-type-generics.md §3.4).
            string? unboundParamId = NeoGenericResolution.FirstUnboundParamId(
                NeoGenericResolution.ResolveInstanceEnv(client, customTypeId, customTypeArguments));
            if (unboundParamId is not null)
            {
                throw new InvalidOperationException(
                    $"Cannot create default custom value for open generic type '{type.name}': generic param '{unboundParamId}' is unbound — every generic param must be bound before instantiation (specs/custom-type-generics.md Decision 6).");
            }
            return CustomTypeInheritance.MergeInstanceSchema(
                CustomTypeInheritance.ResolveChain(
                    customTypeId,
                    id => client.TryGetType(id, out CustomType? match)
                        ? match
                        : null),
                id => client.TryGetAttribute(id, out Attribute? attribute)
                    ? attribute
                    : null);
        }

        private static AttributeValue? CreateDefaultValueRow(
            NeoClient client,
            Attribute attribute,
            List<AttributeValue> rows,
            string nowIso,
            HashSet<string> customTypeStack,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            switch (attribute)
            {
                case NullAttribute attr:
                    return attr.defaultValue is null
                        ? null
                        : CreateNullValueRow(nowIso, attr.defaultValue.typeId);
                case BoolAttribute attr:
                    return attr.defaultValue is null
                        ? null
                        : new BoolAttributeValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = attr.defaultValue.value,
                            typeId = attr.defaultValue.typeId,
                        };
                case IntAttribute attr:
                    return attr.defaultValue is null
                        ? null
                        : new NumberAttributeValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = attr.defaultValue.value,
                            typeId = attr.defaultValue.typeId,
                        };
                case FloatAttribute attr:
                    return attr.defaultValue is null
                        ? null
                        : new NumberAttributeValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = attr.defaultValue.value,
                            typeId = attr.defaultValue.typeId,
                        };
                case Vector2Attribute attr:
                    return CreateDefaultVector2Row(nowIso, attr.defaultValue);
                case Vector2IntAttribute attr:
                    return CreateDefaultVector2Row(nowIso, attr.defaultValue);
                case Vector3Attribute attr:
                    return CreateDefaultVector3Row(nowIso, attr.defaultValue);
                case Vector3IntAttribute attr:
                    return CreateDefaultVector3Row(nowIso, attr.defaultValue);
                case ColorAttribute attr:
                    return CreateDefaultColorRow(nowIso, attr.defaultValue);
                case DecimalAttribute attr:
                    return CreateDefaultDecimalRow(nowIso, attr.defaultValue);
                case StringAttribute attr:
                    return attr.defaultValue is null
                        ? null
                        : new StringAttributeValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = attr.defaultValue.value,
                            neoLocalizationMode = attr.defaultValue is StringAttributeValueBase stringDefault
                                ? stringDefault.neoLocalizationMode
                                : null,
                            typeId = attr.defaultValue.typeId,
                        };
                case EnumAttribute attr:
                    return attr.defaultValue is null
                        ? null
                        : new ArrayAttributeValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = CloneArray(attr.defaultValue.value),
                            typeId = attr.defaultValue.typeId,
                        };
                case LookupAttribute attr:
                    return attr.defaultValue is null
                        ? null
                        : new ArrayAttributeValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = CloneArray(attr.defaultValue.value),
                            typeId = attr.defaultValue.typeId,
                        };
                case DialogueLookupAttribute attr:
                    return attr.defaultValue is null
                        ? null
                        : new ArrayAttributeValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = CloneArray(attr.defaultValue.value),
                            typeId = attr.defaultValue.typeId,
                        };
                case SpriteAttribute attr:
                    return attr.defaultValue is null
                        ? null
                        : AttributeValueFactory.Create(
                            attr,
                            attr.defaultValue.value,
                            Guid.NewGuid().ToString(),
                            nowIso,
                            nowIso);
                case AudioAttribute attr:
                    return attr.defaultValue is null
                        ? null
                        : AttributeValueFactory.Create(
                            attr,
                            attr.defaultValue.value,
                            Guid.NewGuid().ToString(),
                            nowIso,
                            nowIso);
                case CustomAttribute attr:
                    return CreateDefaultCustomValueRow(
                        client,
                        attr,
                        rows,
                        nowIso,
                        customTypeStack);
                case DictionaryAttribute attr:
                    return CreateDefaultDictionaryValueRow(
                        client,
                        attr,
                        rows,
                        nowIso,
                        customTypeStack,
                        env);
                case ListAttribute attr:
                    return CreateDefaultListValueRow(
                        client,
                        attr,
                        rows,
                        nowIso,
                        customTypeStack,
                        env);
                default:
                    return null;
            }
        }

        private static ObjectAttributeValue CreateDefaultCustomValueRow(
            NeoClient client,
            CustomAttribute attribute,
            List<AttributeValue> rows,
            string nowIso,
            HashSet<string> customTypeStack)
        {
            var effectiveTypeId = attribute.defaultValue?.typeId
                ?? attribute.customTypeId;
            // The slot's constructed arguments travel with every descent
            // below — the default's effective type may be the DECLARED open
            // type, closed only by the slot (specs/custom-type-generics.md
            // §4.1).
            var provided = CloneDefaultCustomChildren(
                client,
                attribute.defaultValue?.value,
                effectiveTypeId,
                rows,
                nowIso,
                customTypeStack,
                attribute.customTypeArguments);
            return CreateWritableCustomValueRow(
                client,
                effectiveTypeId,
                provided,
                rows,
                nowIso,
                customTypeStack,
                attribute.customTypeArguments);
        }

        private static Dictionary<string, string> CloneDefaultCustomChildren(
            NeoClient client,
            Dictionary<string, string>? source,
            string customTypeId,
            List<AttributeValue> rows,
            string nowIso,
            HashSet<string> customTypeStack,
            IReadOnlyDictionary<string, GenericBinding>? customTypeArguments = null)
        {
            var result = new Dictionary<string, string>();
            if (source is null || source.Count == 0) return result;

            var schemaByKey = new Dictionary<string, MergedSchemaEntry>();
            foreach (var entry in ResolveMergedSchema(client, customTypeId, customTypeArguments))
            {
                schemaByKey[entry.schemaKey] = entry;
            }
            var env = NeoGenericResolution.ResolveInstanceEnv(
                client,
                customTypeId,
                customTypeArguments);

            foreach (var pair in source)
            {
                if (!schemaByKey.TryGetValue(pair.Key, out var entry)) continue;
                if (!client.TryGetAttribute(entry.attributeId, out Attribute? attribute)) continue;
                if (!client.TryGetValue(pair.Value, out AttributeValue? sourceRow)) continue;

                var cloned = CloneStoredValueForAttribute(
                    client,
                    NeoGenericResolution.SubstituteAttribute(client, attribute, env),
                    sourceRow,
                    rows,
                    nowIso,
                    customTypeStack,
                    env);
                if (cloned is null) continue;

                rows.Add(cloned);
                result[pair.Key] = cloned.id;
            }
            return result;
        }

        private static ObjectAttributeValue? CreateDefaultDictionaryValueRow(
            NeoClient client,
            DictionaryAttribute attribute,
            List<AttributeValue> rows,
            string nowIso,
            HashSet<string> customTypeStack,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (attribute.defaultValue is null) return null;
            var source = new ObjectAttributeValue
            {
                id = "__neo_embedded_dictionary_default",
                value = attribute.defaultValue.value,
                typeId = attribute.defaultValue.typeId,
            };
            return CloneDictionaryValueRow(
                client,
                attribute,
                source,
                rows,
                nowIso,
                customTypeStack,
                env);
        }

        private static ArrayAttributeValue? CreateDefaultListValueRow(
            NeoClient client,
            ListAttribute attribute,
            List<AttributeValue> rows,
            string nowIso,
            HashSet<string> customTypeStack,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (attribute.defaultValue is null) return null;
            var source = new ArrayAttributeValue
            {
                id = "__neo_embedded_list_default",
                value = attribute.defaultValue.value,
                typeId = attribute.defaultValue.typeId,
            };
            return CloneListValueRow(
                client,
                attribute,
                source,
                rows,
                nowIso,
                customTypeStack,
                env);
        }

        private static AttributeValue? CloneStoredValueForAttribute(
            NeoClient client,
            Attribute attribute,
            AttributeValue source,
            List<AttributeValue> rows,
            string nowIso,
            HashSet<string> customTypeStack,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            switch (attribute)
            {
                case NullAttribute:
                    return CreateNullValueRow(nowIso, source.typeId);
                case BoolAttribute when source is BoolAttributeValue sourceValue:
                    return new BoolAttributeValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = sourceValue.value,
                        typeId = source.typeId,
                    };
                case IntAttribute or FloatAttribute
                    when source is NumberAttributeValue sourceValue:
                    return new NumberAttributeValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = sourceValue.value,
                        typeId = source.typeId,
                    };
                case Vector2Attribute or Vector2IntAttribute
                    when source is Vector2AttributeValue sourceValue:
                    return new Vector2AttributeValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = CloneVector2(sourceValue.value),
                        typeId = source.typeId,
                    };
                case Vector3Attribute or Vector3IntAttribute
                    when source is Vector3AttributeValue sourceValue:
                    return new Vector3AttributeValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = CloneVector3(sourceValue.value),
                        typeId = source.typeId,
                    };
                case ColorAttribute when source is ColorAttributeValue sourceValue:
                    return new ColorAttributeValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = CloneColor(sourceValue.value),
                        typeId = source.typeId,
                    };
                case DecimalAttribute when source is StringAttributeValue sourceValue:
                    return new StringAttributeValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = sourceValue.value,
                        typeId = source.typeId,
                    };
                case StringAttribute when source is StringAttributeValue sourceValue:
                    return new StringAttributeValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = sourceValue.value,
                        neoLocalizationMode = sourceValue.neoLocalizationMode,
                        typeId = source.typeId,
                    };
                case EnumAttribute or LookupAttribute or DialogueLookupAttribute
                    when source is ArrayAttributeValue sourceValue:
                    return new ArrayAttributeValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = CloneArray(sourceValue.value),
                        typeId = source.typeId,
                    };
                case SpriteAttribute when source is SpriteAttributeValue sourceValue:
                    return new SpriteAttributeValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = sourceValue.value is null
                            ? null
                            : new SpriteValue
                            {
                                fileId = sourceValue.value.fileId,
                                sliceIndex = sourceValue.value.sliceIndex,
                            },
                        typeId = source.typeId,
                    };
                case AudioAttribute when source is FileAttributeValue sourceValue:
                    return new FileAttributeValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = sourceValue.value is null
                            ? null
                            : new FileValue { fileId = sourceValue.value.fileId },
                        typeId = source.typeId,
                    };
                case CustomAttribute customAttribute
                    when source is ObjectAttributeValue sourceValue:
                    return CreateWritableCustomValueRow(
                        client,
                        sourceValue.typeId ?? customAttribute.customTypeId,
                        CloneDefaultCustomChildren(
                            client,
                            sourceValue.value,
                            sourceValue.typeId ?? customAttribute.customTypeId,
                            rows,
                            nowIso,
                            customTypeStack,
                            customAttribute.customTypeArguments),
                        rows,
                        nowIso,
                        customTypeStack,
                        customAttribute.customTypeArguments);
                case DictionaryAttribute dictionaryAttribute
                    when source is ObjectAttributeValue sourceValue:
                    return CloneDictionaryValueRow(
                        client,
                        dictionaryAttribute,
                        sourceValue,
                        rows,
                        nowIso,
                        customTypeStack,
                        env);
                case ListAttribute listAttribute
                    when source is ArrayAttributeValue sourceValue:
                    return CloneListValueRow(
                        client,
                        listAttribute,
                        sourceValue,
                        rows,
                        nowIso,
                        customTypeStack,
                        env);
                default:
                    return null;
            }
        }

        private static ObjectAttributeValue CloneDictionaryValueRow(
            NeoClient client,
            DictionaryAttribute attribute,
            ObjectAttributeValue source,
            List<AttributeValue> rows,
            string nowIso,
            HashSet<string> customTypeStack,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            // The clone keeps the source row's immutable Decision-9 stamp
            // (falling back to a fresh computation from the creation env
            // for pre-stamp authored rows), and entries substitute their
            // attribute through it.
            var entryEnv = source.genericBindings is null
                ? env
                : NeoGenericResolution.EnvFromStamp(source.genericBindings);
            var value = new Dictionary<string, string>();
            if (source.value is not null)
            {
                if (!client.TryGetAttribute(
                        attribute.entryAttributeId,
                        out Attribute? entryAttribute))
                {
                    throw new InvalidOperationException(
                        $"Dictionary default for '{attribute.name}' references missing entry attribute '{attribute.entryAttributeId}'.");
                }
                entryAttribute = NeoGenericResolution.SubstituteAttribute(client, entryAttribute, entryEnv);
                foreach (var pair in source.value)
                {
                    if (!client.TryGetValue(pair.Value, out AttributeValue? sourceRow))
                    {
                        throw new InvalidOperationException(
                            $"Dictionary default for '{attribute.name}' key '{pair.Key}' references missing value '{pair.Value}'.");
                    }
                    var cloned = CloneStoredValueForAttribute(
                        client,
                        entryAttribute,
                        sourceRow,
                        rows,
                        nowIso,
                        customTypeStack,
                        entryEnv);
                    if (cloned is null)
                    {
                        throw new InvalidOperationException(
                            $"Dictionary default for '{attribute.name}' key '{pair.Key}' has incompatible row shape '{sourceRow.GetType().Name}'.");
                    }

                    rows.Add(cloned);
                    value[pair.Key] = cloned.id;
                }
            }

            var row = new ObjectAttributeValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = value,
                typeId = source.typeId,
                genericBindings = source.genericBindings is null
                    ? null
                    : new Dictionary<string, string>(source.genericBindings),
            };
            NeoGenericResolution.StampGenericBindings(client, attribute, row, env);
            return row;
        }

        private static ArrayAttributeValue CloneListValueRow(
            NeoClient client,
            ListAttribute attribute,
            ArrayAttributeValue source,
            List<AttributeValue> rows,
            string nowIso,
            HashSet<string> customTypeStack,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            // Same stamp semantics as CloneDictionaryValueRow.
            var entryEnv = source.genericBindings is null
                ? env
                : NeoGenericResolution.EnvFromStamp(source.genericBindings);
            var value = new List<string>();
            if (source.value is not null)
            {
                if (!client.TryGetAttribute(
                        attribute.entryAttributeId,
                        out Attribute? entryAttribute))
                {
                    throw new InvalidOperationException(
                        $"List default for '{attribute.name}' references missing entry attribute '{attribute.entryAttributeId}'.");
                }
                entryAttribute = NeoGenericResolution.SubstituteAttribute(client, entryAttribute, entryEnv);
                foreach (var sourceId in source.value)
                {
                    if (!client.TryGetValue(sourceId, out AttributeValue? sourceRow))
                    {
                        throw new InvalidOperationException(
                            $"List default for '{attribute.name}' references missing value '{sourceId}'.");
                    }
                    var cloned = CloneStoredValueForAttribute(
                        client,
                        entryAttribute,
                        sourceRow,
                        rows,
                        nowIso,
                        customTypeStack,
                        entryEnv);
                    if (cloned is null)
                    {
                        throw new InvalidOperationException(
                            $"List default for '{attribute.name}' has incompatible row shape '{sourceRow.GetType().Name}'.");
                    }

                    rows.Add(cloned);
                    value.Add(cloned.id);
                }
            }

            var row = new ArrayAttributeValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = value.ToArray(),
                typeId = source.typeId,
                genericBindings = source.genericBindings is null
                    ? null
                    : new Dictionary<string, string>(source.genericBindings),
            };
            NeoGenericResolution.StampGenericBindings(client, attribute, row, env);
            return row;
        }

        private static NullAttributeValue CreateNullValueRow(
            string nowIso,
            string? typeId)
        {
            return new NullAttributeValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                typeId = typeId,
            };
        }

        private static Vector2AttributeValue? CreateDefaultVector2Row(
            string nowIso,
            AttributeValueBase<NeoVector2Value?>? defaultValue)
        {
            return defaultValue is null
                ? null
                : new Vector2AttributeValue
                {
                    id = Guid.NewGuid().ToString(),
                    createdAt = nowIso,
                    updatedAt = nowIso,
                    value = CloneVector2(defaultValue.value),
                    typeId = defaultValue.typeId,
                };
        }

        private static Vector3AttributeValue? CreateDefaultVector3Row(
            string nowIso,
            AttributeValueBase<NeoVector3Value?>? defaultValue)
        {
            return defaultValue is null
                ? null
                : new Vector3AttributeValue
                {
                    id = Guid.NewGuid().ToString(),
                    createdAt = nowIso,
                    updatedAt = nowIso,
                    value = CloneVector3(defaultValue.value),
                    typeId = defaultValue.typeId,
                };
        }

        /// <summary>
        /// Default-value row for a Color attribute. Unlike the vectors,
        /// Color has a well-defined identity default — opaque white
        /// (specs/color-attribute.md decision 4) — so an absent authored
        /// default still materializes a row rather than leaving a required
        /// field valueless.
        /// </summary>
        private static ColorAttributeValue CreateDefaultColorRow(
            string nowIso,
            AttributeValueBase<NeoColorValue?>? defaultValue)
        {
            return new ColorAttributeValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = CloneColor(defaultValue?.value)
                    ?? new NeoColorValue { r = 1f, g = 1f, b = 1f, a = 1f },
                typeId = defaultValue?.typeId,
            };
        }

        private static string[]? CloneArray(string[]? source)
        {
            if (source is null) return null;
            var clone = new string[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        private static NeoVector2Value? CloneVector2(NeoVector2Value? source)
        {
            return source is null ? null : new NeoVector2Value { x = source.x, y = source.y };
        }

        private static NeoVector3Value? CloneVector3(NeoVector3Value? source)
        {
            return source is null
                ? null
                : new NeoVector3Value { x = source.x, y = source.y, z = source.z };
        }

        private static NeoColorValue? CloneColor(NeoColorValue? source)
        {
            return source is null
                ? null
                : new NeoColorValue { r = source.r, g = source.g, b = source.b, a = source.a };
        }

        /// <summary>
        /// Default-value row for a Decimal attribute. Decimal has a
        /// well-defined non-null default — canonical "0"
        /// (specs/decimal-attribute.md decision 4) — so an absent authored
        /// default still materializes a row (a string row, decision 5) rather
        /// than leaving a required field valueless.
        /// </summary>
        private static StringAttributeValue CreateDefaultDecimalRow(
            string nowIso,
            AttributeValueBase<string?>? defaultValue)
        {
            return new StringAttributeValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = defaultValue?.value ?? "0",
            };
        }

        public static NeoValuePayload? ValuePayload(
            INeoValuePayloadProvider? value)
        {
            return value?.ToNeoValuePayload();
        }

        public static NeoValuePayload ValuePayload(
            NeoAttributeCustom node,
            string fallbackTypeId)
        {
            return new NeoValuePayload(
                node.value?.value,
                node.value?.typeId ?? fallbackTypeId);
        }

        public static int? ReadInt(NeoAttributeInt attribute)
        {
            var value = attribute.value?.value;
            return value.HasValue ? (int)value.Value : null;
        }

        public static string? ReadSingleSelected(NeoAttributeEnum attribute)
        {
            var selected = attribute.Selected();
            return selected.Length > 0 ? selected[0] : null;
        }

        public static string? ReadSingleSelected(NeoAttributeLookup attribute)
        {
            var selected = attribute.Selected();
            return selected.Length > 0 ? selected[0] : null;
        }

        public static string? ReadSingleSelected(NeoAttributeDialogueLookup attribute)
        {
            var selected = attribute.Selected();
            return selected.Length > 0 ? selected[0] : null;
        }

        public static TEnum? ReadEnumSingle<TEnum>(
            string[] optionIds,
            Func<string, TEnum> create)
        {
            return optionIds.Length == 0 ? default : create(optionIds[0]);
        }

        public static IReadOnlyList<TEnum> ReadEnumList<TEnum>(
            string[] optionIds,
            Func<string, TEnum> create)
        {
            var values = new List<TEnum>();
            foreach (var optionId in optionIds) values.Add(create(optionId));
            return values;
        }

        public static IReadOnlyList<T> ReadLookupList<T>(
            IList<NeoAttribute> nodes,
            Func<NeoAttribute, T> create)
        {
            var values = new List<T>();
            foreach (var node in nodes) values.Add(create(node));
            return values;
        }

        public static object? ReadNSProperty(NeoAttributeNSProperty attribute)
        {
            var result = attribute.Compute();
            if (!result.ok)
            {
                throw new InvalidOperationException(
                    result.error ?? "NSProperty getter evaluation failed.");
            }
            return result.value;
        }

        public static Sprite? ReadSprite(NeoClient client, object? value)
        {
            return NeoAssetResolver.ResolveSprite(
                client.assetDatabase,
                ToSpriteValue(value));
        }

        public static AudioClip? ReadAudioClip(NeoClient client, object? value)
        {
            return NeoAssetResolver.ResolveAudioClip(
                client.assetDatabase,
                ToFileValue(value));
        }

        private static SpriteValue? ToSpriteValue(object? value)
        {
            if (value is null) return null;
            if (value is SpriteValue spriteValue) return spriteValue;
            if (value is JObject obj)
            {
                var fileId = obj["fileId"]?.Value<string>();
                var sliceIndex = obj["sliceIndex"]?.Value<int?>();
                return string.IsNullOrWhiteSpace(fileId) || sliceIndex == null
                    ? null
                    : new SpriteValue { fileId = fileId!, sliceIndex = sliceIndex.Value };
            }
            if (value is IDictionary<string, object?> dict &&
                dict.TryGetValue("fileId", out var rawFileId) &&
                rawFileId is string dictFileId &&
                dict.TryGetValue("sliceIndex", out var rawSliceIndex))
            {
                return rawSliceIndex switch
                {
                    int i => new SpriteValue { fileId = dictFileId, sliceIndex = i },
                    long l => new SpriteValue { fileId = dictFileId, sliceIndex = (int)l },
                    double d => new SpriteValue { fileId = dictFileId, sliceIndex = Convert.ToInt32(d) },
                    _ => null,
                };
            }
            return null;
        }

        private static FileValue? ToFileValue(object? value)
        {
            if (value is null) return null;
            if (value is FileValue fileValue) return fileValue;
            if (value is JObject obj)
            {
                var fileId = obj["fileId"]?.Value<string>();
                return string.IsNullOrWhiteSpace(fileId)
                    ? null
                    : new FileValue { fileId = fileId! };
            }
            if (value is IDictionary<string, object?> dict &&
                dict.TryGetValue("fileId", out var rawFileId) &&
                rawFileId is string dictFileId)
            {
                return new FileValue { fileId = dictFileId };
            }
            return null;
        }

        public static T? ReadNSPropertyCustom<T>(
            NeoClient client,
            object? value,
            bool required,
            bool saved,
            Func<NeoClient, NeoAttributeCustom, T>? readOnlyFactory,
            // Nullable: an Immutable-constrained type (allowedStorage collapse)
            // generates no writable class, so codegen passes null here.
            Func<NeoClient, NeoAttributeCustomWritable, T>? savedFactory)
        {
            if (value is null)
            {
                if (required)
                {
                    throw new InvalidOperationException(
                        "NSProperty getter returned null for a required custom value.");
                }
                return default;
            }

            if (value is T typed) return typed;

            string? valueId = ValueId(value);
            if (string.IsNullOrEmpty(valueId))
            {
                throw new InvalidOperationException(
                    $"NSProperty getter returned a custom value without a backing value id. Runtime value type: {value.GetType().FullName}.");
            }

            if (!client.TryGetValue(valueId, out AttributeValue? untypedRow))
            {
                throw new InvalidOperationException(
                    $"NSProperty getter returned custom value id '{valueId}', but no backing value row exists. Runtime value type: {value.GetType().FullName}.");
            }

            if (untypedRow is not ObjectAttributeValue row)
            {
                throw new InvalidOperationException(
                    $"NSProperty getter returned custom value id '{valueId}', but the backing row is not an object value. Row type: {untypedRow.GetType().FullName}.");
            }
            string? customTypeId = ResolveCustomValueTypeId(client, valueId!, row);
            if (string.IsNullOrEmpty(customTypeId))
            {
                throw new InvalidOperationException(
                    $"NSProperty getter returned custom value id '{valueId}', but the backing row does not declare a typeId and its owning attribute could not be inferred.");
            }

            var attribute = new CustomAttribute
            {
                id = $"__neo_nsg_custom_{customTypeId}",
                name = "NSPropertyCustomValue",
                type = AttributeType.Custom,
                customTypeId = customTypeId,
                createdAt = row.createdAt,
                updatedAt = row.updatedAt,
            };

            if (saved)
            {
                // Stable-id overlay: a value reachable from the save/session
                // root reports that ownership directly (see the authored
                // ownership map); the returned writable node clone-on-writes
                // its own row at its stable id on first mutation, so there is
                // no path to pre-materialize here.
                if (!client.TryGetValueOwnership(valueId, out NeoValueOwnership ownership)
                    || (ownership != NeoValueOwnership.Save && ownership != NeoValueOwnership.Session))
                {
                    throw new InvalidOperationException(
                        "NSProperty getter returned an asset-owned custom value where a saved value was expected.");
                }

                if (savedFactory is null)
                {
                    throw new InvalidOperationException(
                        "NSProperty getter custom value resolved to a writable placement, but the type's allowedStorage is immutable (no writable factory exists).");
                }
                return savedFactory(
                    client,
                    new NeoAttributeCustomWritable(client, attribute, valueId, ownership));
            }

            if (readOnlyFactory is null)
            {
                throw new InvalidOperationException(
                    "NSProperty getter custom value requires a read-only factory.");
            }

            return readOnlyFactory(
                client,
                new NeoAttributeCustom(client, attribute, valueId));
        }

        public static T ReadRequiredNSPropertyCustom<T>(
            NeoClient client,
            object? value,
            bool saved,
            Func<NeoClient, NeoAttributeCustom, T>? readOnlyFactory,
            Func<NeoClient, NeoAttributeCustomWritable, T>? savedFactory)
        {
            T? resolved = ReadNSPropertyCustom(
                client,
                value,
                true,
                saved,
                readOnlyFactory,
                savedFactory);
            if (resolved is null)
            {
                throw new InvalidOperationException(
                    "NSProperty getter returned null for a required custom value.");
            }
            return resolved;
        }

        public static string? ValueId(object? value)
        {
            if (value is NeoLookupSelection selection) return selection.valueId;
            if (value is NeoDialogueReference dialogueReference) return dialogueReference.Id;
            return value is INeoValueReference reference
                ? reference.valueId
                : null;
        }

        public static string[] ToStringArray(object? value)
        {
            if (value is null) return Array.Empty<string>();
            if (value is string[] strings) return strings;
            if (value is object?[] objects)
            {
                var values = new List<string>();
                foreach (var item in objects)
                {
                    if (item is string str) values.Add(str);
                }
                return values.ToArray();
            }
            return Array.Empty<string>();
        }

        public static string[] LookupSelectionIds(
            IEnumerable<NeoLookupSelection>? selections)
        {
            if (selections is null) return Array.Empty<string>();
            var ids = new List<string>();
            foreach (var selection in selections) ids.Add(selection.valueId);
            return ids.ToArray();
        }

        public static string LookupSelectionId(string? valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId))
            {
                throw new InvalidOperationException(
                    "Generated value is not bound to a lookup-selectable value id.");
            }
            return valueId;
        }

        /// <summary>
        /// Flattens a set of <see cref="NeoDialogueReference"/>s to their stored
        /// <c>dialogueId</c>s for serialization (multiselect DialogueLookup).
        /// </summary>
        public static string[] DialogueReferenceIds(
            IEnumerable<NeoDialogueReference>? references)
        {
            if (references is null) return Array.Empty<string>();
            var ids = new List<string>();
            foreach (var reference in references) ids.Add(reference.Id);
            return ids.ToArray();
        }
    }
}
