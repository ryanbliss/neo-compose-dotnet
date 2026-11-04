// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// An <c>object</c>, <c>ValueType</c> or <c>Enum</c> field (P127 §1.2):
    /// its row is null, a class value, or a box whose <c>valueType</c> names
    /// the held type. A <c>Type</c>, <c>CultureInfo</c> or
    /// <c>NumberFormatInfo</c> field stores its payload directly (§1.3). Generated accessors read and write it through
    /// <see cref="NeoGeneratedTypesSupport.ReadObjectValue"/> and
    /// <see cref="NeoGeneratedTypesSupport.ObjectValue"/>.
    /// </summary>
    public class NeoMemberObject : NeoMember<ObjectMember, MemberValue>
    {
        public NeoMemberObject(NeoClient client, ObjectMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        internal object? ReadObject()
        {
            ObserveScriptWrites();
            if (value is not { } row || row is NullMemberValue)
                return null;
            bool writable = this is NeoMemberObjectWritable && ownership != NeoValueOwnership.Asset;
            if (writable && row is PayloadMemberValue { value: JObject payload } held && NeoStorablePayloads.IsWritable(payload))
                row = OwnHeldPayload(held);
            object? result = member.objectType is NeoObjectTypeKind.Type or NeoObjectTypeKind.CultureInfo or NeoObjectTypeKind.NumberFormatInfo
                ? NeoGeneratedTypesSupport.ReadDeclared(client, member, row, writable)
                : row.valueType is TypeBinding valueType
                    ? NeoGeneratedTypesSupport.ReadBox(client, this, row, valueType, writable)
                    : ReadClass(row);
            client.NoteGlobalizationValue(result);
            return result;
        }

        private object? ReadClass(MemberValue row)
        {
            if (row is not ObjectMemberValue { classId: string })
            {
                throw new System.InvalidOperationException(
                    $"Object field '{member.name}' holds value '{row.id}', which has neither a valueType nor a classId.");
            }
            return client.ResolveRegisteredGeneratedClassValue(row.id, ownership);
        }
    }

    public sealed class NeoMemberObjectWritable : NeoMemberObject
    {
        public NeoMemberObjectWritable(NeoClient client, ObjectMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Session)
            : base(client, member, overrideValueId, ownership) { }
    }
}
