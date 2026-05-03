// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Shared helper methods used by web-generated C# facade types.
    /// Kept in the SDK runtime so generated files only contain
    /// project-specific schema wrappers.
    /// </summary>
    public static class NeoGeneratedTypesSupport
    {
        public static NeoValueWritePayload? Value<T>(T? value)
        {
            return NeoValueWritePayload.FromValue(value);
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
            foreach (var row in valueRows)
            {
                client.SetSaveValue(row);
            }

            var parentRow = new ObjectAttributeValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = value,
                typeId = customTypeId,
            };
            client.SetSaveValue(parentRow);

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
