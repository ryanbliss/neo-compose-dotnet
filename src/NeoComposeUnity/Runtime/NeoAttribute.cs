using NeoCompose.Runtime.Json;

#nullable enable
namespace NeoCompose.Runtime
{
    public abstract class NeoAttribute<TAttribute, TValue> : NeoNode where TAttribute : Attribute where TValue : AttributeValue
    {
        protected TAttribute attribute;
        protected string? overrideValueId;
        protected string? valueId => overrideValueId is not null ? overrideValueId : attribute.valueId;
        protected TValue? valueData
        {
            get
            {
                if (valueId is null)
                {
                    return null;
                }
                if (!client.TryGetValue(valueId, out TValue match))
                {
                    return null;
                }
                return match;
            }
        }
        protected TValue? value;
        public NeoAttribute(NeoClient client, TAttribute attribute, string? overrideValueId) : base(client)
        {
            this.overrideValueId = overrideValueId;
            this.attribute = attribute;
            var valueData = this.valueData;
            if (valueData is null)
            {
                BuildEmptyData();
            }
            else
            {
                Initialize(valueData);
            }
        }
        
        public NeoAttribute(NeoClient client, string attributeId, string? overrideValueId) : base(client)
        {
            if (!client.TryGetAttribute(attributeId, out TAttribute attribute))
            {
                throw new System.ArgumentException($"No {nameof(TAttribute)} for attribute {attributeId}", nameof(attributeId));
            }
            this.overrideValueId = overrideValueId;
            this.attribute = attribute;
            var valueData = this.valueData;
            if (valueData is null)
            {
                BuildEmptyData();
            }
            else
            {
                Initialize(valueData);
            }
        }

        virtual protected void BuildEmptyData()
        {
            // Do nothing by default, since only writeable attributes support setting values
        }

        virtual protected void Initialize(TValue value)
        {
            // Do nothing by default, since only writeable attributes support setting values
            this.value = value;
        }
    }
}