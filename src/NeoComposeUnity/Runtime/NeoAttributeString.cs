using NeoCompose.Runtime.Json;

#nullable enable

namespace NeoCompose.Runtime
{
    public class NeoAttributeString : NeoAttribute<StringAttribute, StringAttributeValue>
    {
        public NeoAttributeString(NeoClient client, string attributeId, string? overrideValueId) : base(client, attributeId, overrideValueId)
        { }

        public NeoAttributeString(NeoClient client, StringAttribute attribute, string? overrideValueId) : base(client, attribute, overrideValueId)
        { }
    }

    public class NeoAttributeStringSaved : NeoAttributeString
    {
        public NeoAttributeStringSaved(NeoClient client, string attributeId, string? overrideValueId) : base(client, attributeId, overrideValueId)
        { }

        public NeoAttributeStringSaved(NeoClient client, StringAttribute attribute, string? overrideValueId) : base(client, attribute, overrideValueId)
        { }

        public void Set(string? value)
        {
            if (attribute.required && value == null)
            {
                throw new System.ArgumentNullException(nameof(value), $"Cannot be null when {nameof(attribute)}.{nameof(attribute.required)} is true");
            }
            System.DateTime currentTime = new();
            string currentTimeString = currentTime.ToString();
            if (attribute.valueId is not null && client.TryGetValue(attribute.valueId, out StringAttributeValue existing))
            {
                existing.value = value;
                existing.updatedAt = currentTimeString;
                client.SetSaveValue(existing);
                return;
            }
            if (attribute.valueId is not null)
            {
                throw new System.Exception($"{nameof(attribute.valueId)} is set but no value was found");
            }
            string newValueId = new System.Guid().ToString();
            StringAttributeValue newValue = new()
            {
                id = newValueId,
                createdAt = currentTimeString,
                updatedAt = currentTimeString,
                value = value
            };
            client.AddSaveValue(attribute.id, newValue);
        }
    }
}