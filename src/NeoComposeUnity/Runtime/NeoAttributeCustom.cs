using System.Collections;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

#nullable enable

namespace NeoCompose.Runtime
{
    public class NeoAttributeCustom : NeoAttribute<CustomAttribute, ObjectAttributeValue>, IEnumerable<KeyValuePair<string, NeoAttribute<Attribute, AttributeValue>>>
    {
        protected CustomType type;
        protected Dictionary<string, NeoAttribute<Attribute, AttributeValue>> childAttributes = new();

        public NeoAttributeCustom(NeoClient client, string attributeId, string? overrideValueId) : base(client, attributeId, overrideValueId)
        {
            if (!client.TryGetType(attribute.customTypeId, out CustomType match))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(attribute.customTypeId),
                    $"No custom type for {nameof(attribute)}.{nameof(attribute.customTypeId)} {attribute.customTypeId}"
                );
            }
            type = match;
        }

        public NeoAttributeCustom(NeoClient client, CustomAttribute attribute, string? overrideValueId) : base(client, attribute, overrideValueId)
        {
            if (!client.TryGetType(attribute.customTypeId, out CustomType match))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(attribute.customTypeId),
                    $"No custom type for {nameof(attribute)}.{nameof(attribute.customTypeId)} {attribute.customTypeId}"
                );
            }
            type = match;
        }

        protected TNeoAttribute Get<TNeoAttribute>(string key) where TNeoAttribute : NeoAttribute<Attribute, AttributeValue>
        {
            if (!TryGetValue(key, out TNeoAttribute attribute))
            {
                throw new System.NullReferenceException($"attribute for {nameof(key)} not found");
            }
            return attribute;
        }

        public bool TryGetValue<TNeoAttribute>(string key, out TNeoAttribute outAttribute) where TNeoAttribute : NeoAttribute<Attribute, AttributeValue>
        {
            if (childAttributes.TryGetValue(key, out NeoAttribute<Attribute, AttributeValue> check))
            {
                if (check is TNeoAttribute match)
                {
                    outAttribute = match;
                    return true;
                }
            }
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            outAttribute = null;
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
            return false;
        }

        protected TValue? GetValueData<TValue>(string key) where TValue : AttributeValue
        {
            if (!TryGetValueData(key, out TValue value))
            {
                if (attribute.required)
                {
                    throw new System.NullReferenceException($"{attribute.required} is true, but value not found");
                }
                return null;
            }
            return value;
        }

        protected bool TryGetValueData<TValue>(string key, out TValue outValue) where TValue : AttributeValue
        {
            if (value?.value is not null && value.value.TryGetValue(key, out string valueIdForKey))
            {
                return client.TryGetValue(valueIdForKey, out outValue);
            }

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            outValue = null;
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
            return false;
        }

        protected TAttribute GetAttribute<TAttribute>(string key) where TAttribute : Attribute
        {
            if (!TryGetAttribute(key, out TAttribute childAttribute))
            {
                throw new System.NullReferenceException($"attribute for {nameof(key)} not found");
            }

            return childAttribute;
        }

        protected bool TryGetAttribute<TAttribute>(string key, out TAttribute outAttribute) where TAttribute : Attribute
        {
            if (type.schema.TryGetValue(key, out string attributeIdForKey))
            {
                return client.TryGetAttribute(attributeIdForKey, out outAttribute);
            }

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            outAttribute = null;
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
            return false;
        }

        override protected void Initialize(ObjectAttributeValue value)
        {
            base.Initialize(value);
            if (value.value is null)
            {
                return;
            }
            foreach (var kvp in value.value)
            {
                var childAttribute = GetAttribute<Attribute>(kvp.Key);
                if (childAttribute is CustomAttribute customChildAttribute)
                {
                    NeoAttributeCustom customChild = new(client, customChildAttribute, kvp.Value);
                    childAttributes.Add(kvp.Key, customChild);
                }
                else if (childAttribute is StringAttribute stringAttribute)
                {
                    NeoAttributeString customChild = new(client, stringAttribute, kvp.Value);
                    childAttributes.Add(kvp.Key, customChild);
                }
            }
        }

        public IEnumerator<KeyValuePair<string, NeoAttribute<Attribute, AttributeValue>>> GetEnumerator()
        {
            return childAttributes.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public class NeoAttributeCustomSaved : NeoAttributeCustom
    {
        public NeoAttributeCustomSaved(NeoClient client, string attributeId, string? overrideValueId) : base(client, attributeId, overrideValueId)
        { }

        public void Set<TValue>(string key, TValue? setValue)
        {
            System.DateTime currentTime = new();
            string currentTimeString = currentTime.ToString();

            // We need to create a new value
            if (!type.schema.TryGetValue(key, out string schemaKeyedAttributeId))
            {
                throw new System.Exception($"schema does not contain an attribute for {nameof(key)} {key}");
            }
            if (!client.TryGetAttribute(schemaKeyedAttributeId, out Attribute childAttribute))
            {
                throw new System.Exception($"no attribute for {nameof(schemaKeyedAttributeId)} {schemaKeyedAttributeId}");
            }
            if (childAttribute.required && setValue is null)
            {
                throw new System.ArgumentNullException(nameof(value), $"Cannot be null when {nameof(attribute)}.{nameof(attribute.required)} is true");
            }

            if (TryGetValueData(key, out AttributeValue<TValue> existing))
            {
                // Should also set to NeoAttribute, since that stores reference to AttributeValue
                existing.value = setValue;
                existing.updatedAt = currentTimeString;
                client.SetSaveValue(existing);
                return;
            }

            
            string newValueId = new System.Guid().ToString();

            if (childAttribute is CustomAttribute customChildAttribute)
            {
                if (setValue is not Dictionary<string, string> setDictValue)
                {
                    if (!childAttribute.required && setValue is not null)
                    {
                        throw new System.Exception($"Invalid type of {nameof(setValue)}. Expected {typeof(Dictionary<string, string>)} or null");
                    }
                    throw new System.Exception($"Invalid type of {nameof(setValue)}. Expected {typeof(Dictionary<string, string>)}.");
                }
                ObjectAttributeValue value = new()
                {
                    id = newValueId,
                    createdAt = currentTimeString,
                    updatedAt = currentTimeString,
                    value = setDictValue
                };
                client.AddSaveValue(customChildAttribute.id, value);
                NeoAttributeCustom customChild = new(client, customChildAttribute, newValueId);
                childAttributes.Add(key, customChild);
            }
            else if (childAttribute is StringAttribute stringAttribute)
            {
                if (setValue is not string setText)
                {
                    if (!childAttribute.required && setValue is not null)
                    {
                        throw new System.Exception($"Invalid type of {nameof(setValue)}. Expected {typeof(string)} or null");
                    }
                    throw new System.Exception($"Invalid type of {nameof(setValue)}. Expected {typeof(string)}.");
                }
                StringAttributeValue value = new()
                {
                    id = newValueId,
                    createdAt = currentTimeString,
                    updatedAt = currentTimeString,
                    value = setText
                };
                client.AddSaveValue(stringAttribute.id, value);
                NeoAttributeString customChild = new(client, stringAttribute, newValueId);
                childAttributes.Add(key, customChild);
            }
        }
    }
}