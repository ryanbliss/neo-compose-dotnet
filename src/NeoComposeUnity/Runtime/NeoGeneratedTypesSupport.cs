// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
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

        public delegate object SavedCustomFactory(
            NeoClient client,
            NeoAttributeCustomSaved node);

        public static NeoValueWritePayload? Value<T>(T? value)
        {
            return NeoValueWritePayload.FromValue(value);
        }

        public static object? ResolveCustomValue(
            NeoClient client,
            string valueId,
            IReadOnlyDictionary<string, ReadOnlyCustomFactory> readOnlyFactories,
            IReadOnlyDictionary<string, SavedCustomFactory> savedFactories)
        {
            if (!client.TryGetValue(valueId, out ObjectAttributeValue? value)
                || string.IsNullOrEmpty(value.typeId))
            {
                return null;
            }

            var attribute = new CustomAttribute
            {
                id = $"__neo_resolved_custom_{value.typeId}",
                _id = $"__neo_resolved_custom_{value.typeId}",
                name = "ResolvedCustomValue",
                type = AttributeType.Custom,
                customTypeId = value.typeId,
                createdAt = value.createdAt,
                updatedAt = value.updatedAt,
            };

            if (client.saveValues.ContainsKey(valueId)
                && savedFactories.TryGetValue(value.typeId, out var savedFactory))
            {
                return savedFactory(
                    client,
                    new NeoAttributeCustomSaved(client, attribute, valueId));
            }

            if (readOnlyFactories.TryGetValue(value.typeId, out var readOnlyFactory))
            {
                return readOnlyFactory(
                    client,
                    new NeoAttributeCustom(client, attribute, valueId));
            }

            return null;
        }

        public static NeoValueWritePayload? ValueReference(
            INeoValueReference? value)
        {
            return value is null
                ? null
                : NeoValueWritePayload.FromValueReference(
                    LookupSelectionId(value.valueId));
        }

        public static void SetValue(
            NeoAttributeCustomSaved node,
            string key,
            NeoValueWritePayload? value)
        {
            node.SetSerializedValue(key, value);
        }

        public static void SetValue(
            NeoAttributeDictionarySaved node,
            string key,
            NeoValueWritePayload? value)
        {
            node.SetSerialized(key, value);
        }

        public static void AddValue(
            NeoAttributeListSaved node,
            NeoValueWritePayload? value)
        {
            node.AddSerialized(value);
        }

        public static void SetValue(
            NeoAttributeListSaved node,
            int index,
            NeoValueWritePayload? value)
        {
            node.SetSerialized(index, value);
        }

        public static NeoAttributeCustomSaved CreateSavedCustomValue(
            NeoClient client,
            string customTypeId,
            Dictionary<string, string> value,
            IReadOnlyList<AttributeValue> valueRows)
        {
            var nowIso = DateTime.UtcNow.ToString("o");
            var rows = new List<AttributeValue>(valueRows);
            var parentRow = CreateSavedCustomValueRow(
                client,
                customTypeId,
                value,
                rows,
                nowIso,
                new HashSet<string>());
            rows.Add(parentRow);

            foreach (var row in rows)
            {
                client.SetSaveValue(row);
            }

            var factoryAttribute = new CustomAttribute
            {
                id = $"__neo_factory_custom_{customTypeId}",
                _id = $"__neo_factory_custom_{customTypeId}",
                name = "Factory",
                type = AttributeType.Custom,
                customTypeId = customTypeId,
                createdAt = nowIso,
                updatedAt = nowIso,
            };
            return new NeoAttributeCustomSaved(
                client,
                factoryAttribute,
                parentRow.id);
        }

        private static ObjectAttributeValue CreateSavedCustomValueRow(
            NeoClient client,
            string customTypeId,
            Dictionary<string, string>? providedValue,
            List<AttributeValue> rows,
            string nowIso,
            HashSet<string> customTypeStack)
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

                var mergedSchema = ResolveMergedSchema(client, customTypeId);
                foreach (var entry in mergedSchema)
                {
                    if (value.ContainsKey(entry.schemaKey)) continue;
                    if (!client.TryGetAttribute(entry.attributeId, out Attribute? attribute))
                    {
                        throw new InvalidOperationException(
                            $"Custom type '{customTypeId}' schema key '{entry.schemaKey}' references missing attribute '{entry.attributeId}'.");
                    }
                    if (!attribute.required) continue;

                    var defaultRow = CreateDefaultValueRow(
                        client,
                        attribute,
                        rows,
                        nowIso,
                        customTypeStack);
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
            string customTypeId)
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
            HashSet<string> customTypeStack)
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
                case StringAttribute attr:
                    return attr.defaultValue is null
                        ? null
                        : new StringAttributeValue
                        {
                            id = Guid.NewGuid().ToString(),
                            createdAt = nowIso,
                            updatedAt = nowIso,
                            value = attr.defaultValue.value,
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
                        attr,
                        nowIso);
                case ListAttribute attr:
                    return CreateDefaultListValueRow(
                        attr,
                        nowIso);
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
            var provided = CloneDefaultCustomChildren(
                client,
                attribute.defaultValue?.value,
                effectiveTypeId,
                rows,
                nowIso,
                customTypeStack);
            return CreateSavedCustomValueRow(
                client,
                effectiveTypeId,
                provided,
                rows,
                nowIso,
                customTypeStack);
        }

        private static Dictionary<string, string> CloneDefaultCustomChildren(
            NeoClient client,
            Dictionary<string, string>? source,
            string customTypeId,
            List<AttributeValue> rows,
            string nowIso,
            HashSet<string> customTypeStack)
        {
            var result = new Dictionary<string, string>();
            if (source is null || source.Count == 0) return result;

            var schemaByKey = new Dictionary<string, MergedSchemaEntry>();
            foreach (var entry in ResolveMergedSchema(client, customTypeId))
            {
                schemaByKey[entry.schemaKey] = entry;
            }

            foreach (var pair in source)
            {
                if (!schemaByKey.TryGetValue(pair.Key, out var entry)) continue;
                if (!client.TryGetAttribute(entry.attributeId, out Attribute? attribute)) continue;
                if (!client.TryGetValue(pair.Value, out AttributeValue? sourceRow)) continue;

                var cloned = CloneStoredValueForAttribute(
                    client,
                    attribute,
                    sourceRow,
                    rows,
                    nowIso,
                    customTypeStack);
                if (cloned is null) continue;

                rows.Add(cloned);
                result[pair.Key] = cloned.id;
            }
            return result;
        }

        private static ObjectAttributeValue CreateDefaultDictionaryValueRow(
            DictionaryAttribute attribute,
            string nowIso)
        {
            return new ObjectAttributeValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = new Dictionary<string, string>(),
            };
        }

        private static ArrayAttributeValue CreateDefaultListValueRow(
            ListAttribute attribute,
            string nowIso)
        {
            return new ArrayAttributeValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = Array.Empty<string>(),
            };
        }

        private static AttributeValue? CloneStoredValueForAttribute(
            NeoClient client,
            Attribute attribute,
            AttributeValue source,
            List<AttributeValue> rows,
            string nowIso,
            HashSet<string> customTypeStack)
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
                case StringAttribute when source is StringAttributeValue sourceValue:
                    return new StringAttributeValue
                    {
                        id = Guid.NewGuid().ToString(),
                        createdAt = nowIso,
                        updatedAt = nowIso,
                        value = sourceValue.value,
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
                    return CreateSavedCustomValueRow(
                        client,
                        sourceValue.typeId ?? customAttribute.customTypeId,
                        CloneDefaultCustomChildren(
                            client,
                            sourceValue.value,
                            sourceValue.typeId ?? customAttribute.customTypeId,
                            rows,
                            nowIso,
                            customTypeStack),
                        rows,
                        nowIso,
                        customTypeStack);
                case DictionaryAttribute dictionaryAttribute
                    when source is ObjectAttributeValue sourceValue:
                    return CloneDictionaryValueRow(
                        client,
                        dictionaryAttribute,
                        sourceValue,
                        rows,
                        nowIso,
                        customTypeStack);
                case ListAttribute listAttribute
                    when source is ArrayAttributeValue sourceValue:
                    return CloneListValueRow(
                        client,
                        listAttribute,
                        sourceValue,
                        rows,
                        nowIso,
                        customTypeStack);
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
            HashSet<string> customTypeStack)
        {
            var value = new Dictionary<string, string>();
            if (source.value is not null
                && client.TryGetAttribute(attribute.entryAttributeId, out Attribute? entryAttribute))
            {
                foreach (var pair in source.value)
                {
                    if (!client.TryGetValue(pair.Value, out AttributeValue? sourceRow)) continue;
                    var cloned = CloneStoredValueForAttribute(
                        client,
                        entryAttribute,
                        sourceRow,
                        rows,
                        nowIso,
                        customTypeStack);
                    if (cloned is null) continue;

                    rows.Add(cloned);
                    value[pair.Key] = cloned.id;
                }
            }

            return new ObjectAttributeValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = value,
                typeId = source.typeId,
            };
        }

        private static ArrayAttributeValue CloneListValueRow(
            NeoClient client,
            ListAttribute attribute,
            ArrayAttributeValue source,
            List<AttributeValue> rows,
            string nowIso,
            HashSet<string> customTypeStack)
        {
            var value = new List<string>();
            if (source.value is not null
                && client.TryGetAttribute(attribute.entryAttributeId, out Attribute? entryAttribute))
            {
                foreach (var sourceId in source.value)
                {
                    if (!client.TryGetValue(sourceId, out AttributeValue? sourceRow)) continue;
                    var cloned = CloneStoredValueForAttribute(
                        client,
                        entryAttribute,
                        sourceRow,
                        rows,
                        nowIso,
                        customTypeStack);
                    if (cloned is null) continue;

                    rows.Add(cloned);
                    value.Add(cloned.id);
                }
            }

            return new ArrayAttributeValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = value.ToArray(),
                typeId = source.typeId,
            };
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

        private static string[]? CloneArray(string[]? source)
        {
            if (source is null) return null;
            var clone = new string[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
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

        public static object? ReadNSGetter(NeoAttributeNSGetter attribute)
        {
            var result = attribute.Compute();
            if (!result.ok)
            {
                throw new InvalidOperationException(
                    result.error ?? "NSGetter evaluation failed.");
            }
            return result.value;
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
    }
}
