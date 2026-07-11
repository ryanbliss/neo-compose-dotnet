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
    /// <summary>
    /// Shared helper methods used by web-generated C# facade types.
    /// Kept in the SDK runtime so generated files only contain
    /// project-specific schema wrappers.
    /// </summary>
    public static class NeoGeneratedTypesSupport
    {
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
                        || !client.types.TryGetValue(parentTypeId, out CustomType? parentType)
                        || parentType.schema == null
                        || !parentType.schema.TryGetValue(pair.Key, out string childAttributeId)
                        || !client.TryGetAttribute(childAttributeId, out Attribute? childAttribute))
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
            return CustomTypeInheritance.MergeSchemas(
                CustomTypeInheritance.ResolveChain(
                    customTypeId,
                    id => client.TryGetType(id, out CustomType? match)
                        ? match
                        : null));
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
                        nowIso,
                        env);
                case ListAttribute attr:
                    return CreateDefaultListValueRow(
                        client,
                        attr,
                        nowIso,
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

        private static ObjectAttributeValue CreateDefaultDictionaryValueRow(
            NeoClient client,
            DictionaryAttribute attribute,
            string nowIso,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            var row = new ObjectAttributeValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = new Dictionary<string, string>(),
            };
            NeoGenericResolution.StampGenericBindings(client, attribute, row, env);
            return row;
        }

        private static ArrayAttributeValue CreateDefaultListValueRow(
            NeoClient client,
            ListAttribute attribute,
            string nowIso,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            var row = new ArrayAttributeValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = Array.Empty<string>(),
            };
            NeoGenericResolution.StampGenericBindings(client, attribute, row, env);
            return row;
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
                case EnumAttribute or LookupAttribute
                    when source is ArrayAttributeValue sourceValue:
                    return new ArrayAttributeValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = CloneArray(sourceValue.value),
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
            if (source.value is not null
                && client.TryGetAttribute(attribute.entryAttributeId, out Attribute? entryAttribute))
            {
                entryAttribute = NeoGenericResolution.SubstituteAttribute(client, entryAttribute, entryEnv);
                foreach (var pair in source.value)
                {
                    if (!client.TryGetValue(pair.Value, out AttributeValue? sourceRow)) continue;
                    var cloned = CloneStoredValueForAttribute(
                        client,
                        entryAttribute,
                        sourceRow,
                        rows,
                        nowIso,
                        customTypeStack,
                        entryEnv);
                    if (cloned is null) continue;

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
            if (source.value is not null
                && client.TryGetAttribute(attribute.entryAttributeId, out Attribute? entryAttribute))
            {
                entryAttribute = NeoGenericResolution.SubstituteAttribute(client, entryAttribute, entryEnv);
                foreach (var sourceId in source.value)
                {
                    if (!client.TryGetValue(sourceId, out AttributeValue? sourceRow)) continue;
                    var cloned = CloneStoredValueForAttribute(
                        client,
                        entryAttribute,
                        sourceRow,
                        rows,
                        nowIso,
                        customTypeStack,
                        entryEnv);
                    if (cloned is null) continue;

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
            // Nullable: a Static-constrained type (allowedStorage collapse)
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
                        "NSProperty getter custom value resolved to a writable placement, but the type's allowedStorage is static (no writable factory exists).");
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
