// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime
{
    public static partial class NeoGeneratedTypesSupport
    {
        public static I? ReadInterfaceObject<I>(NeoClient client, NeoMemberClass parent, string schemaKey, bool readOnly) where I : class =>
            parent.TryGet<NeoMember>(schemaKey, out var node) ? InterfaceObjectEntry<I>(client, node, readOnly) : null;

        public static I? ReadInterfaceObject<I>(NeoClient client, NeoStaticBinding binding, bool readOnly) where I : class =>
            binding.TryGetNode<NeoMember>(out var node) ? InterfaceObjectEntry<I>(client, node, readOnly) : null;

        public static I? InterfaceObjectEntry<I>(NeoClient client, NeoMember entry, bool readOnly) where I : class
        {
            entry.ObserveScriptWrites();
            if (entry.value is not { } row || row is NullMemberValue || row is ObjectMemberValue { value: null, valueType: null })
                return null;
            object? result;
            if (row.valueType is TypeBinding held)
            {
                bool writable = !readOnly && entry.ownership != NeoValueOwnership.Asset;
                if (writable && entry is NeoMemberInterfaceBox box && row is PayloadMemberValue { value: JObject payload } stored
                    && NeoStorablePayloads.IsWritable(payload))
                    row = box.OwnHeldPayload(stored);
                result = ReadBox(client, entry, row, held, writable);
            }
            else if (entry is NeoMemberClass node)
                result = ResolveInterfaceObject<object>(client, node, readOnly);
            else
                throw new InvalidOperationException($"Slot '{entry.member.name}' has neither a class row nor a boxed value.");
            client.NoteGlobalizationValue(result);
            return result is null ? null : result as I
                ?? throw new InvalidOperationException($"Slot '{entry.member.name}' holds {result.GetType()}, which does not implement {typeof(I)}.");
        }

        public static void SetInterfaceObject(NeoClient client, NeoMemberClassWritable parent, string schemaKey, object? value, string slotLabel) =>
            SetValue(parent, schemaKey, InterfaceSlotPayload(client, parent.GetMember<Member>(schemaKey), parent.ownership,
                value, slotLabel, new NeoSlotContext(client, parent.value, parent.member), false));

        public static void BindInterfaceObject(NeoClient client, NeoStaticBinding binding, object? value, string slotLabel) =>
            binding.SetValue(InterfaceSlotPayload(client, NeoGenericResolution.SubstituteMember(client, binding.Member, binding.GenericEnvironment),
                binding.Ownership, value, slotLabel, new NeoSlotContext(client), false));

        public static NeoValueWritePayload? InterfaceObjectEntryValue(NeoClient client, NeoMember collection, object? value, string slotLabel) =>
            InterfaceEntryPayload(client, collection, value, slotLabel, false);

        public static TComparer? ReadComparer<TComparer>(NeoClient client, NeoMemberClass parent, string schemaKey, bool readOnly) where TComparer : class =>
            ReadInterfaceObject<TComparer>(client, parent, schemaKey, readOnly);

        public static TComparer? ReadComparer<TComparer>(NeoClient client, NeoStaticBinding binding, bool readOnly) where TComparer : class =>
            ReadInterfaceObject<TComparer>(client, binding, readOnly);

        public static TComparer? ComparerEntry<TComparer>(NeoClient client, NeoMember entry, bool readOnly) where TComparer : class =>
            InterfaceObjectEntry<TComparer>(client, entry, readOnly);

        public static void SetComparer(NeoClient client, NeoMemberClassWritable parent, string schemaKey, object? value, string slotLabel) =>
            SetValue(parent, schemaKey, InterfaceSlotPayload(client, parent.GetMember<Member>(schemaKey), parent.ownership,
                value, slotLabel, new NeoSlotContext(client, parent.value, parent.member), true));

        public static void BindComparer(NeoClient client, NeoStaticBinding binding, object? value, string slotLabel) =>
            binding.SetValue(InterfaceSlotPayload(client, NeoGenericResolution.SubstituteMember(client, binding.Member, binding.GenericEnvironment),
                binding.Ownership, value, slotLabel, new NeoSlotContext(client), true));

        public static NeoValueWritePayload? ComparerEntryValue(NeoClient client, NeoMember collection, object? value, string slotLabel) =>
            InterfaceEntryPayload(client, collection, value, slotLabel, true);

        private static NeoValueWritePayload? InterfaceEntryPayload(NeoClient client, NeoMember collection, object? value, string label, bool comparer)
        {
            Member? entry = collection switch
            {
                NeoMemberList list => list.EntryMember,
                NeoMemberDictionary dictionary => dictionary.EntryMember,
                _ => TryResolveCollectionEntryMember(client, collection.member, out var declared) ? declared : null,
            };
            if (entry is null)
                throw new InvalidOperationException($"Slot '{label}' has no collection entry member.");
            var parent = collection.parent as NeoMemberClass;
            return InterfaceSlotPayload(client, entry, client.ChildOwnership(entry, collection.ownership), value, label,
                new NeoSlotContext(client, parent?.value, parent?.member), comparer);
        }

        private static NeoValueWritePayload? InterfaceSlotPayload(NeoClient client, Member slot, NeoValueOwnership ownership,
            object? value, string label, in NeoSlotContext context, bool comparer)
        {
            if (value is null)
                return NeoValueWritePayload.FromValue(null);
            if (value is NeoGeneratedClassValue generated)
            {
                RequireObjectSlotHolds(client, slot, label, new ObjectMemberValue { id = label, classId = generated.classId }, context);
                return ValueReference(generated);
            }
            NeoValueWritePayload payload;
            try
            {
                payload = ObjectWritePayload(client, new ObjectMember { id = slot.id, name = label, kind = MemberKind.Object },
                    label, ownership, value, context);
            }
            catch (NotSupportedException)
            {
                throw new NSGetterRuntimeError($"Slot '{label}' cannot store {(comparer ? "comparer" : "a value")} of CLR type '{value.GetType()}'.",
                    NeoErrorContract.NeoNotSupportedError);
            }
            if (payload.value is NeoValuePayload boxed)
                RequireObjectSlotHolds(client, slot, label, new NullMemberValue { id = label, valueType = boxed.valueType }, context);
            else if (payload.valueId is string id && client.TryGetValue(ownership, id, out var row))
                RequireObjectSlotHolds(client, slot, label, row!, context);
            return payload;
        }
    }
}
