#nullable enable
using NeoCompose.Runtime.NeoScript;
namespace NeoCompose.Runtime
{
    public abstract class NeoErrorBase : NeoGeneratedClassValue
    {
        protected NeoErrorBase(NeoClient client, NeoMemberClass node, string fallbackClassId,
            bool isReadOnly = true, NeoValueOwnership inheritedStorageOwnership = NeoValueOwnership.Asset)
            : base(client, node, fallbackClassId, isReadOnly, inheritedStorageOwnership) { }
        protected NeoErrorBase(NeoClient client, NeoDetachedValue value, bool isReadOnly)
            : base(client, value, isReadOnly) { }

        public string Message
        {
            get
            {
                if (!client.TryGetMember(NeoErrorContract.MessageMember, out Json.Member? member))
                    throw new NSGetterRuntimeError("Canonical NeoError.Message member is missing.");
                string key = member!.name;
                if (TryReadDetached(key, out object? value))
                    return value as string ?? throw new NSGetterRuntimeError("NeoError.Message is not a string.");
                return node.Get<NeoMemberString>(key).Text
                    ?? throw new NSGetterRuntimeError("NeoError.Message is missing.");
            }
        }
    }
}
