// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Member = NeoCompose.Runtime.Json.Member;
using TypeInfo = NeoCompose.Runtime.Json.TypeInfo;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// P127 §1.2, §1.3 and §14: what an <c>object</c>, <c>ValueType</c>,
    /// <c>Enum</c>, <c>Type</c>, <c>CultureInfo</c> or
    /// <c>NumberFormatInfo</c> slot holds, crossed to and from C#.
    /// </summary>
    public static partial class NeoGeneratedTypesSupport
    {
        // The C# object a box row reads as for the rest of the run: the box
        // last read from the row, or the one written into it (§1.2). A write
        // replaces the row, so the slot reads its new box; a reload builds
        // new rows, so each slot reads its own box, as C# deserialization does.
        private static readonly ConditionalWeakTable<MemberValue, object> rowBoxes = new();

        // The held type of a collection view or delegate read from a box, so
        // writing it back stores the type it was read as.
        private static readonly ConditionalWeakTable<object, TypeBinding> heldTypes = new();

        private static readonly ConcurrentDictionary<Type, Func<NeoClient, Member, NeoMember, object?>> codecReaders = new();
        private static readonly ConcurrentDictionary<Type, Func<NeoMemberDelegate, NeoClient, Delegate>> delegateBinders = new();

        /// <summary>
        /// An object field's value (P127 §1.2, §14): null, a generated class
        /// value, or the C# value a box holds. Repeat reads of one stored box
        /// return the same object.
        /// </summary>
        public static object? ReadObjectValue(NeoMemberObject node) => node.ReadObject();

        /// <summary>
        /// The write payload for an object field (§1.3, §14): null, a
        /// generated class value, <c>new object()</c>, a primitive, string,
        /// Unity vector, <c>Color</c>, <c>Sprite</c>, <c>AudioClip</c>,
        /// <c>NeoDialogueReference</c>, <c>NeoReference&lt;T&gt;</c>,
        /// generated enum, <c>ValueTuple</c>, collection, <c>NeoDelegate</c>,
        /// <c>Type</c>, invariant <c>CultureInfo</c>,
        /// <c>NumberFormatInfo</c> or built-in comparer. The field must be
        /// able to hold it.
        /// </summary>
        public static NeoValueWritePayload? ObjectValue(NeoClient client, NeoMemberObject node, object? value)
        {
            if (value is null)
                return NeoValueWritePayload.FromValue(null);
            // Writing back the collection the slot already holds keeps it.
            if (value is INeoCollectionView { CollectionNode: { value: { } held } } && ReferenceEquals(held, node.value))
                return NeoValueWritePayload.FromValueReference(held.id);
            var parent = node.parent as NeoMemberClass;
            return ObjectWritePayload(client, node.member, node.member.name, node.ownership, value, new NeoSlotContext(client, parent?.value, parent?.member));
        }

        /// <summary>
        /// The write payload for a new <c>object</c> entry of
        /// <paramref name="collection"/>: a list, set or dictionary entry
        /// stores what an object field stores (§1.3).
        /// </summary>
        internal static NeoValueWritePayload? ObjectEntryValue(NeoClient client, ObjectMember entry, NeoMember collection, object? value)
        {
            if (value is null)
                return NeoValueWritePayload.FromValue(null);
            NeoValueOwnership ownership = client.ChildOwnership(entry, collection.ownership);
            return ObjectWritePayload(client, entry, collection.member.name, ownership, value, new NeoSlotContext(client));
        }

        private static NeoValueWritePayload ObjectWritePayload(
            NeoClient client,
            ObjectMember slot,
            string name,
            NeoValueOwnership ownership,
            object value,
            in NeoSlotContext slotContext)
        {
            if (value is NeoGeneratedClassValue generated)
            {
                RequireObjectSlotHolds(client, slot, name, new ObjectMemberValue { id = name, classId = generated.classId }, slotContext);
                return ValueReference(generated);
            }
            var rows = new List<MemberValue>();
            var context = client.RentDirectFunctionContext(ownership);
            try
            {
                // A class value inside a box is imported as one assigned to a
                // field of the held type is: copied from Asset or Save data,
                // adopted from Session, refused when it has another owner.
                return NeoValueWritePayload.FromValue(ObjectSlotPayload(
                    client,
                    slot,
                    name,
                    ownership,
                    value,
                    slotContext,
                    rows,
                    item => NSGetterEvaluator.ConstructorReferenceOf(item, context) is { valueId: { Length: > 0 } } source
                        ? new NeoConstructorValueReference(client.ImportValueReference(ownership, source.valueId), ownership)
                        : null));
            }
            finally
            {
                client.ReturnDirectFunctionContext(context, null);
            }
        }

        /// <summary>
        /// The payload an object slot stores for a non-null C# value that
        /// isn't a class value: a <c>Type</c>, <c>CultureInfo</c> or
        /// <c>NumberFormatInfo</c> slot's own payload, or a box.
        /// </summary>
        private static NeoValuePayload ObjectSlotPayload(
            NeoClient client,
            ObjectMember slot,
            string name,
            NeoValueOwnership ownership,
            object value,
            in NeoSlotContext slotContext,
            List<MemberValue> rows,
            Func<object?, NeoConstructorValueReference?> valueReference)
        {
            if (slot.objectType is NeoObjectTypeKind.Type or NeoObjectTypeKind.CultureInfo or NeoObjectTypeKind.NumberFormatInfo)
                return NeoValuePayload.Declared(DeclaredPayload(client, slot.objectType.Value, name, value), value);
            JObject? comparer = IsComparer(value) ? ComparerPayload(client, name, value) : null;
            TypeBinding valueType = comparer is null ? HeldType(client, name, value) : ComparerType(client, name, comparer);
            RequireObjectSlotHolds(client, slot, name, new NullMemberValue { id = name, valueType = valueType }, slotContext);
            NeoBoxForm form = client.TypeRules.Box(valueType);
            switch (form.Kind)
            {
                case NeoBoxFormKind.Identity:
                    return NeoValuePayload.Boxed(null, valueType, null, Array.Empty<MemberValue>(), value);
                case NeoBoxFormKind.Comparer:
                    return NeoValuePayload.Boxed(comparer, valueType, null, Array.Empty<MemberValue>(), value);
                case NeoBoxFormKind.Member:
                    break;
                default:
                    throw new InvalidOperationException($"Cannot write object field '{name}': {form.Invalid}");
            }
            Member held = form.Member!;
            object? payload = BoxPayload(client, held, name, ownership, value, rows, valueReference);
            // A collection box reads as a view over its new row, not as the
            // collection it was copied from.
            object? origin = IsCollectionMember(held) ? null : value;
            return NeoValuePayload.Boxed(payload, valueType, held, rows, origin);
        }

        /// <summary>
        /// A factory's <c>object</c> constructor argument (P127 §14): the box
        /// row, minted into <paramref name="rows"/> with the rows its payload
        /// references. The new value is Session data until it's attached.
        /// </summary>
        private static string MaterializeObjectBox(
            NeoClient client,
            ObjectMember slot,
            object value,
            List<MemberValue> rows,
            NeoTimestamp now,
            Func<object?, NeoConstructorValueReference?> valueReference,
            string path)
        {
            var boxRows = new List<MemberValue>();
            NeoValuePayload payload = ObjectSlotPayload(client, slot, path, NeoValueOwnership.Session, value, new NeoSlotContext(client), boxRows, valueReference);
            string valueId = Guid.NewGuid().ToString();
            rows.AddRange(boxRows);
            rows.Add(MemberValueFactory.Create(slot, payload, valueId, now, now));
            return valueId;
        }

        private static void RequireObjectSlotHolds(NeoClient client, Member slot, string name, MemberValue probe, in NeoSlotContext context)
        {
            if (client.TypeRules.SlotViolation(slot, probe, context) is string refusal)
                throw new InvalidOperationException($"Cannot write object field '{name}': {refusal}");
        }

        private static bool IsCollectionMember(Member member) => member switch
        {
            ListMember or DictionaryMember => true,
            EnumMember { Selection: NeoMemberSelectionKind.Multi } => true,
            LookupMember { Selection: NeoMemberSelectionKind.Multi } => true,
            DialogueLookupMember { Selection: NeoMemberSelectionKind.Multi } => true,
            _ => false,
        };

        /// <summary>The closed type a C# value boxes as (§1.2), or a refusal naming the slot and the C# type.</summary>
        private static TypeBinding HeldType(NeoClient client, string name, object value)
        {
            if (value is INeoCollectionView view)
                return CollectionViewType(client, name, view);
            if (!value.GetType().IsValueType && heldTypes.TryGetValue(value, out TypeBinding? read))
                return read;
            switch (value)
            {
                case Type:
                    return TypeBinding.Closed(new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.Type));
                case CultureInfo:
                    return TypeBinding.Closed(new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.CultureInfo));
                case NumberFormatInfo:
                    return TypeBinding.Closed(new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.NumberFormatInfo));
            }
            if (BindingOfClr(client, value.GetType()) is TypeBinding binding && binding.Kind == TypeBindingKind.Closed)
                return binding;
            throw new NotSupportedException($"Object field '{name}' can't store a {CSharpTypeName(value.GetType())}, which has no NeoScript type.");
        }

        /// <summary>
        /// A Neo collection view's type. A view the slot doesn't own belongs
        /// to another slot, and only a <c>Reference</c> shares a row, so it
        /// copies only from immutable asset data.
        /// </summary>
        private static TypeBinding CollectionViewType(NeoClient client, string name, INeoCollectionView view)
        {
            if (view.CollectionNode is not NeoMember node)
                throw new NotSupportedException($"Object field '{name}' can't store a pending {CSharpTypeName(view.GetType())}; attach its owner first.");
            if (node.ownership != NeoValueOwnership.Asset)
            {
                throw new InvalidOperationException(
                    $"Cannot write object field '{name}': the {CSharpTypeName(view.GetType())} already belongs to another field, and only a Reference shares a collection. Copy it into a new collection first.");
            }
            if (heldTypes.TryGetValue(view, out TypeBinding? read))
                return read;
            TypeInfo typeInfo = NeoNSFunctionRuntime.TypeInfoFromBindingMember(
                client,
                node.member,
                NodeEnv(client, node),
                new HashSet<string>(StringComparer.Ordinal),
                NeoNSFunctionRuntime.BindingTypeProjection.Stored);
            return Erased(NeoTypeBindings.FromTypeInfo(NeoTypeRules.Declared(typeInfo)));
        }

        private static IReadOnlyDictionary<string, NeoGenericEnvEntry> NodeEnv(NeoClient client, NeoMember node)
        {
            if (NeoTypeSources.EntryStamp(client, node.value, node.member) is { } stamp)
                return NeoGenericResolution.EnvFromStamp(client, stamp);
            if (node.parent is NeoMemberClass parentClass)
                return parentClass.GenericEnv;
            return NeoGenericResolution.EmptyEnv;
        }

        /// <summary>
        /// A held type erases nullable references and tuple labels at every
        /// depth (§1.3, §12.1); a value type keeps its <c>?</c>.
        /// </summary>
        private static TypeBinding Erased(TypeBinding binding)
        {
            if (binding.Kind != TypeBindingKind.Closed)
                return binding;
            TypeHead head = binding.Head!.WithoutNames();
            if (!head.Required && !TypeBindings.IsValueType(head))
                head = head.WithRequired(true);
            if (binding.Bindings is not { } bindings)
                return TypeBinding.Closed(head);
            var erased = new Dictionary<string, TypeBinding>(bindings.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, TypeBinding> pair in bindings)
                erased[pair.Key] = Erased(pair.Value);
            return TypeBinding.Closed(head, erased);
        }

        /// <summary>A box's payload, stored as a field of its held type stores its value (§1.3).</summary>
        private static object? BoxPayload(
            NeoClient client,
            Member held,
            string name,
            NeoValueOwnership ownership,
            object value,
            List<MemberValue> rows,
            Func<object?, NeoConstructorValueReference?> valueReference)
        {
            switch (held)
            {
                case ObjectMember { objectType: NeoObjectTypeKind declared }:
                    return DeclaredPayload(client, declared, name, value);
                case DelegateMember:
                    {
                        NeoDelegateValue binding = NeoMemberDelegate.PersistedBindingOf((Delegate)value, out NeoClient? source)!;
                        // User data takes only a delegate read from the user
                        // client, and Save data none that names User data.
                        if (ownership != NeoValueOwnership.User)
                            client.CheckDelegateReferences(ownership, binding);
                        else if (!ReferenceEquals(source, client))
                            throw new InvalidOperationException(NeoClient.UserDelegateProvenanceError);
                        return binding;
                    }
                case DictionaryMember when value is not IDictionary and not INeoGeneratedConstructorDictionary:
                    value = EnumKeyedEntries(value);
                    break;
            }
            object? payload = ComputeRuntimeConstructorPayload(client, held, value, rows, NeoTimestamp.Now(),
                valueReference, NeoGenericResolution.EmptyEnv, name, scope: null);
            return payload is NeoValuePayload wrapped ? wrapped.value : payload;
        }

        /// <summary>An enum-keyed dictionary's pairs, keyed by its enum values for the constructor payload.</summary>
        private static IDictionary EnumKeyedEntries(object value)
        {
            var entries = new System.Collections.Specialized.OrderedDictionary();
            foreach (object? pair in (IEnumerable)value)
            {
                Type pairType = pair!.GetType();
                entries.Add(pairType.GetProperty("Key")!.GetValue(pair)!, pairType.GetProperty("Value")!.GetValue(pair));
            }
            return entries;
        }

        // ------------------------------------------------------------------
        // Type, CultureInfo and NumberFormatInfo payloads (§12.1).
        // ------------------------------------------------------------------

        private static JObject DeclaredPayload(NeoClient client, NeoObjectTypeKind objectType, string name, object value)
        {
            switch (objectType)
            {
                case NeoObjectTypeKind.Type when value is Type type:
                    {
                        TypeBinding binding = BindingOfClr(client, type)
                            ?? throw new NotSupportedException($"Object field '{name}' can't store typeof({CSharpTypeName(type)}), which names no NeoScript type.");
                        return new JObject { ["type"] = JObject.FromObject(binding) };
                    }
                case NeoObjectTypeKind.CultureInfo when value is CultureInfo culture:
                    return CulturePayload(name, culture);
                case NeoObjectTypeKind.NumberFormatInfo when value is NumberFormatInfo format:
                    return NeoStorablePayloads.Encode(format);
                default:
                    throw new InvalidOperationException(
                        $"Cannot write object field '{name}': a {NeoTypeNames.ObjectName(objectType)} slot can't hold a {CSharpTypeName(value.GetType())}.");
            }
        }

        private static JObject CulturePayload(string name, CultureInfo culture)
        {
            if (culture.Name.Length > 0)
                throw new NotSupportedException($"Object field '{name}' can't store culture '{culture.Name}'; NeoScript has only the invariant culture.");
            return NeoStorablePayloads.Encode(culture);
        }

        /// <summary>
        /// A <c>Type</c>, <c>CultureInfo</c> or <c>NumberFormatInfo</c>
        /// slot's value, the same object for the rest of the run. A node that
        /// can't write the slot reads a writable culture or number format as
        /// a read-only copy (see <see cref="ReadOnlyCopy"/>).
        /// </summary>
        internal static object ReadDeclared(NeoClient client, ObjectMember slot, MemberValue row, bool writable)
        {
            if (rowBoxes.TryGetValue(row, out object? cached))
                return writable ? cached : ReadOnlyCopy(cached);
            return Remember(row, ReadDeclaredPayload(client, slot.objectType!.Value, row, slot.name), writable);
        }

        /// <summary>
        /// What a node that can't write its slot reads a culture or number
        /// format as (§12.1): a read-only copy of a writable one, so a C#
        /// write throws, as a vector write through a read-only owner does,
        /// instead of changing Asset data or a value the node doesn't own.
        /// </summary>
        private static object ReadOnlyCopy(object read) => read switch
        {
            CultureInfo { IsReadOnly: false } culture => CultureInfo.ReadOnly(culture),
            NumberFormatInfo { IsReadOnly: false } format => NumberFormatInfo.ReadOnly(format),
            _ => read,
        };

        private static object ReadDeclaredPayload(NeoClient client, NeoObjectTypeKind objectType, MemberValue row, string name)
        {
            if (row is not PayloadMemberValue { value: JObject payload })
                throw new InvalidOperationException($"Object field '{name}' holds value '{row.id}', which isn't a {NeoTypeNames.ObjectName(objectType)} payload.");
            switch (objectType)
            {
                case NeoObjectTypeKind.Type:
                    {
                        TypeBinding binding = TypeBindings.Read((JObject)payload["type"]!, $"Object field '{name}' type");
                        return ClrTypeOf(client, binding)
                            ?? throw new NotSupportedException(
                                $"Object field '{name}' holds typeof({NeoTypeNames.Of(binding, client.ProjectDataForRuntime)}), which has no C# type in this project.");
                    }
                case NeoObjectTypeKind.CultureInfo:
                    return ReadCulture(payload);
                default:
                    return ReadNumberFormat(payload);
            }
        }

        private static CultureInfo ReadCulture(JObject payload)
        {
            if (payload[NeoStorablePayloads.InvariantKey] is not null)
                return CultureInfo.InvariantCulture;
            var culture = new CultureInfo("")
            {
                NumberFormat = ReadNumberFormat((JObject)payload["numberFormat"]!),
            };
            return payload.Value<bool>(NeoStorablePayloads.ReadOnlyKey) ? CultureInfo.ReadOnly(culture) : culture;
        }

        private static NumberFormatInfo ReadNumberFormat(JObject payload)
        {
            if (payload[NeoStorablePayloads.InvariantKey] is not null)
                return NumberFormatInfo.InvariantInfo;
            var format = new NumberFormatInfo();
            foreach (string key in NeoStorablePayloads.NumberFormatKeys)
            {
                PropertyInfo property = NeoStorablePayloads.NumberFormatProperty(key);
                object value = property.PropertyType == typeof(DigitShapes)
                    ? (DigitShapes)payload.Value<int>(key)
                    : payload[key]!.ToObject(property.PropertyType)!;
                property.SetValue(format, value);
            }
            return payload.Value<bool>(NeoStorablePayloads.ReadOnlyKey) ? NumberFormatInfo.ReadOnly(format) : format;
        }

        // ------------------------------------------------------------------
        // Built-in comparers (§1.3, §12.1, §14).
        // ------------------------------------------------------------------

        private static bool IsComparer(object value)
        {
            if (value is NeoReferenceEqualityComparer or NeoScriptComparer)
                return true;
            foreach (Type contract in value.GetType().GetInterfaces())
            {
                if (!contract.IsGenericType)
                    continue;
                Type definition = contract.GetGenericTypeDefinition();
                if (definition == typeof(IEqualityComparer<>) || definition == typeof(IComparer<>))
                    return true;
            }
            return false;
        }

        private static JObject ComparerPayload(NeoClient client, string name, object value)
        {
            if (value is NeoReferenceEqualityComparer)
                return Comparer(NeoBuiltInComparerKind.ReferenceEquality, NeoComparerFamilyKind.Equality, null);
            if (value is NeoScriptComparer script)
                return (JObject)script.Payload.DeepClone();
            Type type = value.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(NeoSetEqualityComparer<>))
                return Comparer(NeoBuiltInComparerKind.SetEquality, NeoComparerFamilyKind.Equality, ComparerArgument(client, name, type.GetGenericArguments()[0]));
            foreach (Type contract in type.GetInterfaces())
            {
                if (!contract.IsGenericType)
                    continue;
                Type definition = contract.GetGenericTypeDefinition();
                bool equality = definition == typeof(IEqualityComparer<>);
                if (!equality && definition != typeof(IComparer<>))
                    continue;
                Type argument = contract.GetGenericArguments()[0];
                Type system = (equality ? typeof(EqualityComparer<>) : typeof(Comparer<>)).MakeGenericType(argument);
                if (ReferenceEquals(system.GetProperty("Default")!.GetValue(null), value))
                {
                    return Comparer(
                        NeoBuiltInComparerKind.Default,
                        equality ? NeoComparerFamilyKind.Equality : NeoComparerFamilyKind.Comparison,
                        ComparerArgument(client, name, argument));
                }
            }
            throw new NotSupportedException(
                $"Object field '{name}' can't store the C# comparer {CSharpTypeName(type)}; it stores only built-in comparers and comparers read from Neo data.");
        }

        private static TypeBinding ComparerArgument(NeoClient client, string name, Type argument) =>
            BindingOfClr(client, argument) is TypeBinding { Kind: TypeBindingKind.Closed } binding
                ? Erased(binding)
                : throw new NotSupportedException($"Object field '{name}' can't store a comparer of {CSharpTypeName(argument)}, which has no NeoScript type.");

        private static JObject Comparer(NeoBuiltInComparerKind kind, NeoComparerFamilyKind family, TypeBinding? type)
        {
            var payload = new JObject { ["comparer"] = (int)kind, ["family"] = (int)family };
            if (type is not null)
                payload["type"] = JObject.FromObject(type);
            return payload;
        }

        /// <summary>The system class a comparer payload came from, bound to its argument (§1.3).</summary>
        private static TypeBinding ComparerType(NeoClient client, string name, JObject payload)
        {
            var kind = (NeoBuiltInComparerKind)payload.Value<int>("comparer");
            var family = (NeoComparerFamilyKind)payload.Value<int>("family");
            string className = kind == NeoBuiltInComparerKind.ReferenceEquality
                ? NeoSystemComparerClasses.ReferenceEqualityComparer
                : family == NeoComparerFamilyKind.Equality
                    ? NeoSystemComparerClasses.EqualityComparer
                    : NeoSystemComparerClasses.Comparer;
            NeoSchemaClass? system = null;
            foreach (NeoSchemaClass candidate in client.ProjectDataForRuntime.classes.Values)
            {
                if (NeoSystemComparerClasses.NameOf(candidate.id) == className)
                {
                    system = candidate;
                    break;
                }
            }
            if (system is null)
                throw new NotSupportedException($"Object field '{name}' can't store a {className}: the project declares no {className} class.");
            var head = new TypeHead(MemberKind.Class, true, classId: system.id);
            if (kind == NeoBuiltInComparerKind.ReferenceEquality)
                return TypeBinding.Closed(head);
            TypeBinding argument = TypeBindings.Read((JObject)payload["type"]!, $"Object field '{name}' comparer type");
            if (kind == NeoBuiltInComparerKind.SetEquality)
                argument = TypeBinding.Closed(new TypeHead(MemberKind.Set, true), new Dictionary<string, TypeBinding>(StringComparer.Ordinal) { ["0"] = argument });
            return TypeBinding.Closed(head, new Dictionary<string, TypeBinding>(StringComparer.Ordinal) { [system.genericParams![0].id] = argument });
        }

        private static object ReadComparer(NeoClient client, NeoMemberObject node, MemberValue row)
        {
            if (row is not PayloadMemberValue { value: JObject payload })
                throw new InvalidOperationException($"Object field '{node.member.name}' holds value '{row.id}', which isn't a comparer payload.");
            var kind = (NeoBuiltInComparerKind)payload.Value<int>("comparer");
            var family = (NeoComparerFamilyKind)payload.Value<int>("family");
            if (kind == NeoBuiltInComparerKind.ReferenceEquality)
                return NeoReferenceEqualityComparer.Instance;
            TypeBinding argument = TypeBindings.Read((JObject)payload["type"]!, $"Object field '{node.member.name}' comparer type");
            Type element = ClrTypeOf(client, argument)
                ?? throw new NotSupportedException(
                    $"Object field '{node.member.name}' holds a comparer of {NeoTypeNames.Of(argument, client.ProjectDataForRuntime)}, which has no C# type in this project.");
            switch (kind)
            {
                case NeoBuiltInComparerKind.Default:
                    {
                        Type system = (family == NeoComparerFamilyKind.Equality ? typeof(EqualityComparer<>) : typeof(Comparer<>)).MakeGenericType(element);
                        return system.GetProperty("Default")!.GetValue(null)!;
                    }
                case NeoBuiltInComparerKind.SetEquality:
                    return Activator.CreateInstance(typeof(NeoSetEqualityComparer<>).MakeGenericType(element))!;
                default:
                    return NeoScriptComparer.Create(element, client, node.ownership, (JObject)payload.DeepClone(), family);
            }
        }

        // ------------------------------------------------------------------
        // Reading a box (§14).
        // ------------------------------------------------------------------

        /// <summary>
        /// The C# object a box row reads as, the same one for the rest of the
        /// run. A node that can't write the slot reads a writable culture or
        /// number format as a read-only copy (see <see cref="ReadOnlyCopy"/>).
        /// </summary>
        internal static object? ReadBox(NeoClient client, NeoMemberObject node, MemberValue row, TypeBinding valueType, bool writable)
        {
            if (rowBoxes.TryGetValue(row, out object? cached))
                return writable ? cached : ReadOnlyCopy(cached);
            object? box = ReadNewBox(client, node, row, valueType);
            return box is null ? null : Remember(row, box, writable);
        }

        // A read-only copy isn't cached, so the row holds no object C# can change.
        private static object Remember(MemberValue row, object box, bool writable)
        {
            object read = writable ? box : ReadOnlyCopy(box);
            if (ReferenceEquals(read, box))
                RememberBox(row, box);
            return read;
        }

        /// <summary>Caches <paramref name="box"/> as what <paramref name="row"/> reads as.</summary>
        internal static void RememberBox(MemberValue row, object box)
        {
            rowBoxes.Remove(row);
            rowBoxes.Add(row, box);
            // C#'s writes to a stored culture or number format persist through the slot (§12.1).
            if (row is PayloadMemberValue payload && box is CultureInfo or NumberFormatInfo)
                payload.live = box;
        }

        private static object? ReadNewBox(NeoClient client, NeoMemberObject node, MemberValue row, TypeBinding valueType)
        {
            NeoBoxForm form = client.TypeRules.Box(valueType);
            switch (form.Kind)
            {
                case NeoBoxFormKind.Identity:
                    return new object();
                case NeoBoxFormKind.Comparer:
                    return ReadComparer(client, node, row);
                case NeoBoxFormKind.Member:
                    break;
                default:
                    throw new InvalidOperationException($"Object field '{node.member.name}' holds value '{row.id}': {form.Invalid}");
            }
            Member held = form.Member!;
            if (held is ObjectMember { objectType: NeoObjectTypeKind declared })
                return ReadDeclaredPayload(client, declared, row, node.member.name);
            if (IsCollectionMember(held) || held is DelegateMember || held is LookupMember)
                return ReadNodeBox(client, node, row, held, valueType);
            Type type = BoxClrType(client, valueType)
                ?? throw new NotSupportedException(
                    $"Object field '{node.member.name}' holds a '{NeoTypeNames.Of(valueType, client.ProjectDataForRuntime)}' box, which has no C# type in this project.");
            NSGetterEvaluator.Context context = client.RentDirectFunctionContext(node.ownership);
            object? unwrapped = null;
            try
            {
                unwrapped = NSGetterEvaluator.ResolveHostCollection(
                    NSGetterEvaluator.UnwrapRow(row, context, node.ownership, held), context);
                return unwrapped is null ? null : ConvertTypeArgument(client, unwrapped, type, null, null, null);
            }
            finally
            {
                client.ReturnDirectFunctionContext(context, unwrapped);
            }
        }

        /// <summary>Whether a tuple holds an <c>object</c> element, directly or in a nested tuple.</summary>
        internal static bool HoldsObjectElement(NeoClient client, TupleMember tuple)
        {
            foreach (string id in tuple.elementMemberIds)
            {
                if (!client.TryGetMember(id, out Member? element))
                    continue;
                if (element is ObjectMember || element is TupleMember nested && HoldsObjectElement(client, nested))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A stored tuple holding an <c>object</c> element (§1.3), read
        /// element by element: an <c>object</c> element reads as its box,
        /// as a field does, and any other element as the evaluator reads it.
        /// </summary>
        internal static object? ReadObjectTuple(NeoClient client, TupleMember tuple, ArrayMemberValue? row, NeoValueOwnership ownership, Type projection)
        {
            if (row?.value is not { } ids)
                return null;
            Type type = Nullable.GetUnderlyingType(projection) ?? projection;
            Type[] elementTypes = type.GetGenericArguments();
            if (elementTypes.Length != ids.Length)
                throw new NeoTypeArgumentException($"Tuple member '{tuple.name}' has {ids.Length} elements, so it can't convert to {CSharpTypeName(projection)}.");
            var elements = new object?[ids.Length];
            for (int index = 0; index < ids.Length; index++)
            {
                if (!client.TryGetMember(tuple.elementMemberIds[index], out Member? element))
                    throw new InvalidOperationException($"Tuple member '{tuple.name}' element {index + 1} has no member descriptor.");
                elements[index] = ReadTupleElement(client, element, ids[index], ownership, elementTypes[index]);
            }
            return Activator.CreateInstance(type, elements);
        }

        private static object? ReadTupleElement(NeoClient client, Member element, string id, NeoValueOwnership ownership, Type type)
        {
            NeoMember node = NeoMember.CreateWritable(client, element, id, ownership);
            if (node is NeoMemberObject held)
                return held.ReadObject();
            if (node is NeoMemberTuple { member: var tuple } nested && HoldsObjectElement(client, tuple))
                return ReadObjectTuple(client, tuple, nested.value as ArrayMemberValue, ownership, type);
            if (node.value is not MemberValue row)
                return null;
            NSGetterEvaluator.Context context = client.RentDirectFunctionContext(ownership);
            object? unwrapped = null;
            try
            {
                unwrapped = NSGetterEvaluator.ResolveHostCollection(NSGetterEvaluator.UnwrapRow(row, context, ownership, element), context);
                return unwrapped is null ? null : ConvertTypeArgument(client, unwrapped, type, null, null, null);
            }
            finally
            {
                client.ReturnDirectFunctionContext(context, unwrapped);
            }
        }

        /// <summary>
        /// A box whose C# value is a view over its row: a collection, a
        /// delegate or a slot reference, read through the codec a field of
        /// the held type uses.
        /// </summary>
        private static object? ReadNodeBox(NeoClient client, NeoMemberObject node, MemberValue row, Member held, TypeBinding valueType)
        {
            bool writable = node is NeoMemberObjectWritable;
            NeoMember payloadNode = NeoMember.CreateWritable(client, held, row.id, node.ownership);
            object? box;
            if (held is DelegateMember)
            {
                Type delegateType = ClrTypeOf(client, valueType)
                    ?? throw new NotSupportedException(
                        $"Object field '{node.member.name}' holds a '{NeoTypeNames.Of(valueType, client.ProjectDataForRuntime)}' box, which has no C# type in this project.");
                box = delegateBinders.GetOrAdd(delegateType, CreateDelegateBinder)((NeoMemberDelegate)payloadNode, client);
            }
            else
            {
                Type view = ViewType(client, valueType, held, writable)
                    ?? throw new NotSupportedException(
                        $"Object field '{node.member.name}' holds a '{NeoTypeNames.Of(valueType, client.ProjectDataForRuntime)}' box, which has no C# type in this project.");
                box = codecReaders.GetOrAdd(view, CreateCodecReader)(client, held, payloadNode);
            }
            if (box is not null && !box.GetType().IsValueType)
            {
                heldTypes.Remove(box);
                heldTypes.Add(box, valueType);
            }
            return box;
        }

        private static Func<NeoClient, Member, NeoMember, object?> CreateCodecReader(Type view) =>
            (Func<NeoClient, Member, NeoMember, object?>)typeof(NeoGeneratedTypesSupport)
                .GetMethod(nameof(ReadThroughCodec), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(view)
                .CreateDelegate(typeof(Func<NeoClient, Member, NeoMember, object?>));

        private static object? ReadThroughCodec<T>(NeoClient client, Member held, NeoMember node) =>
            NeoGenericBindings.ResolveForMember<T>(client, held, node).Read(node);

        private static Func<NeoMemberDelegate, NeoClient, Delegate> CreateDelegateBinder(Type delegateType)
        {
            Type[] arguments = delegateType.GetGenericArguments();
            MethodInfo? bind = null;
            foreach (MethodInfo method in typeof(NeoMemberDelegate).GetMethods())
            {
                if (method.Name == nameof(NeoMemberDelegate.Bind) && method.GetGenericArguments().Length == arguments.Length)
                {
                    bind = method.MakeGenericMethod(arguments);
                    break;
                }
            }
            MethodInfo converter = typeof(NeoGeneratedTypesSupport)
                .GetMethod(nameof(DelegateResultConverter), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(arguments[0]);
            return (node, client) => (Delegate)bind!.Invoke(node, new[] { converter.Invoke(null, new object[] { client }) })!;
        }

        private static Func<object?, TResult> DelegateResultConverter<TResult>(NeoClient client) =>
            result => TryConvertValue<TResult>(client, result, null, null, out TResult converted)
                ? converted
                : throw new NeoTypeArgumentException($"A NeoDelegate result can't convert to {CSharpTypeName(typeof(TResult))}.");

        /// <summary>
        /// The SDK view a collection or reference box reads as: the type a
        /// field of the held type projects, writable when the slot is.
        /// </summary>
        private static Type? ViewType(NeoClient client, TypeBinding binding, Member held, bool writable)
        {
            TypeHead head = binding.Head!;
            switch (head.Type)
            {
                case MemberKind.List:
                    return EntryType(client, binding.At(0), writable) is Type listEntry
                        ? (writable ? typeof(NeoList<>) : typeof(NeoReadOnlyList<>)).MakeGenericType(listEntry)
                        : null;
                case MemberKind.Set:
                    {
                        TypeBinding element = binding.At(0);
                        switch (held)
                        {
                            case EnumMember:
                                return ClrTypeOf(client, element) is Type option
                                    ? (writable ? typeof(NeoEnumSet<>) : typeof(NeoReadOnlyEnumSet<>)).MakeGenericType(option)
                                    : null;
                            case DialogueLookupMember:
                                return writable ? typeof(NeoDialogueReferenceSet) : typeof(NeoReadOnlyDialogueReferenceSet);
                            case LookupMember:
                                return EntryType(client, element, writable) is Type target
                                    ? (writable ? typeof(NeoLookupSet<>) : typeof(NeoReadOnlyLookupSet<>)).MakeGenericType(target)
                                    : null;
                        }
                        return EntryType(client, element, writable) is Type setEntry
                            ? (writable ? typeof(NeoSet<>) : typeof(NeoReadOnlySet<>)).MakeGenericType(setEntry)
                            : null;
                    }
                case MemberKind.Dictionary:
                    {
                        if (EntryType(client, binding.At(1), writable) is not Type value)
                            return null;
                        TypeBinding key = binding.At(0);
                        if (key.Head!.Type != MemberKind.Enum)
                            return (writable ? typeof(NeoDictionary<>) : typeof(NeoReadOnlyDictionary<>)).MakeGenericType(value);
                        return ClrTypeOf(client, key) is Type option
                            ? (writable ? typeof(NeoDictionary<,>) : typeof(NeoReadOnlyDictionary<,>)).MakeGenericType(option, value)
                            : null;
                    }
                case MemberKind.Reference:
                    return EntryType(client, binding, writable);
                default:
                    return null;
            }
        }

        /// <summary>The C# type a collection entry of <paramref name="binding"/> reads as.</summary>
        private static Type? EntryType(NeoClient client, TypeBinding binding, bool writable)
        {
            TypeHead head = binding.Head!;
            switch (head.Type)
            {
                case MemberKind.List:
                case MemberKind.Set:
                case MemberKind.Dictionary:
                    return ViewType(client, binding, client.TypeRules.Box(binding).Member!, writable);
                case MemberKind.Reference:
                    {
                        TypeBinding target = binding.At(0);
                        if (target.Head!.Type is MemberKind.Class)
                            return ClrTypeOf(client, target);
                        return ClrTypeOf(client, target) is Type referenced ? typeof(NeoReference<>).MakeGenericType(referenced) : null;
                    }
                default:
                    return BoxClrType(client, binding);
            }
        }

        /// <summary>The C# type a scalar or tuple box converts to, or null.</summary>
        private static Type? BoxClrType(NeoClient client, TypeBinding binding)
        {
            if (binding.Kind != TypeBindingKind.Closed)
                return null;
            TypeHead head = binding.Head!;
            switch (head.Type)
            {
                case MemberKind.List:
                case MemberKind.Set:
                case MemberKind.Dictionary:
                case MemberKind.NSDelegate:
                case MemberKind.NSAction:
                case MemberKind.Reference:
                    return null;
                case MemberKind.Tuple:
                    {
                        int arity = binding.Bindings?.Count ?? 0;
                        if (arity < 2 || arity > valueTupleDefinitions.Length + 1)
                            return null;
                        var elements = new Type[arity];
                        for (int index = 0; index < arity; index++)
                        {
                            if (TupleElementType(client, binding.At(index)) is not Type element)
                                return null;
                            elements[index] = element;
                        }
                        Type tuple = valueTupleDefinitions[arity - 2].MakeGenericType(elements);
                        return head.Required ? tuple : typeof(Nullable<>).MakeGenericType(tuple);
                    }
                default:
                    return ClrTypeOf(client, binding);
            }
        }

        /// <summary>A boxed tuple's element: a collection reads as its read-only snapshot.</summary>
        private static Type? TupleElementType(NeoClient client, TypeBinding element)
        {
            switch (element.Head!.Type)
            {
                case MemberKind.List:
                    return EntryType(client, element.At(0), false) is Type entry ? typeof(IReadOnlyList<>).MakeGenericType(entry) : null;
                case MemberKind.Set:
                    return EntryType(client, element.At(0), false) is Type member ? typeof(INeoReadOnlySet<>).MakeGenericType(member) : null;
                case MemberKind.Dictionary:
                    return element.At(0).Head!.Type == MemberKind.String && EntryType(client, element.At(1), false) is Type value
                        ? typeof(IReadOnlyDictionary<,>).MakeGenericType(typeof(string), value)
                        : null;
                default:
                    return BoxClrType(client, element);
            }
        }

        // ------------------------------------------------------------------
        // NeoScript types and C# types (§14: "NeoScript Type values map to
        // the generated or CLR type, and back").
        // ------------------------------------------------------------------

        private static readonly (Type type, MemberKind kind)[] leafTypes =
        {
            (typeof(int), MemberKind.Int),
            (typeof(float), MemberKind.Float),
            (typeof(decimal), MemberKind.Decimal),
            (typeof(bool), MemberKind.Bool),
            (typeof(string), MemberKind.String),
            (typeof(Vector2), MemberKind.Vector2),
            (typeof(Vector2Int), MemberKind.Vector2Int),
            (typeof(Vector3), MemberKind.Vector3),
            (typeof(Vector3Int), MemberKind.Vector3Int),
            (typeof(Color), MemberKind.Color),
            (typeof(Sprite), MemberKind.Sprite),
            (typeof(AudioClip), MemberKind.Audio),
            (typeof(NeoDialogueReference), MemberKind.DialogueLookup),
        };

        private static readonly (Type type, NeoObjectTypeKind? objectType)[] objectTypes =
        {
            (typeof(object), null),
            (typeof(ValueType), NeoObjectTypeKind.ValueType),
            (typeof(System.Enum), NeoObjectTypeKind.Enum),
            (typeof(Type), NeoObjectTypeKind.Type),
            (typeof(CultureInfo), NeoObjectTypeKind.CultureInfo),
            (typeof(NumberFormatInfo), NeoObjectTypeKind.NumberFormatInfo),
        };

        private static readonly Type[] valueTupleDefinitions =
        {
            typeof(ValueTuple<,>),
            typeof(ValueTuple<,,>),
            typeof(ValueTuple<,,,>),
            typeof(ValueTuple<,,,,>),
            typeof(ValueTuple<,,,,,>),
            typeof(ValueTuple<,,,,,,>),
        };

        private static readonly Type[] delegateDefinitions =
        {
            typeof(NeoDelegate<>),
            typeof(NeoDelegate<,>),
            typeof(NeoDelegate<,,>),
            typeof(NeoDelegate<,,,>),
            typeof(NeoDelegate<,,,,>),
            typeof(NeoDelegate<,,,,,>),
            typeof(NeoDelegate<,,,,,,>),
            typeof(NeoDelegate<,,,,,,,>),
            typeof(NeoDelegate<,,,,,,,,>),
            typeof(NeoDelegate<,,,,,,,,,>),
            typeof(NeoDelegate<,,,,,,,,,,>),
            typeof(NeoDelegate<,,,,,,,,,,,>),
            typeof(NeoDelegate<,,,,,,,,,,,,>),
            typeof(NeoDelegate<,,,,,,,,,,,,,>),
            typeof(NeoDelegate<,,,,,,,,,,,,,,>),
            typeof(NeoDelegate<,,,,,,,,,,,,,,,>),
            typeof(NeoDelegate<,,,,,,,,,,,,,,,,>),
        };

        /// <summary>
        /// The NeoScript type a C# type names: closed, a generic definition,
        /// or one of a definition's parameters. A C# list, set or dictionary
        /// type, and the SDK's views of them, name <c>List</c>, <c>Set</c>
        /// and <c>Dictionary</c>. Null when it names none.
        /// </summary>
        internal static TypeBinding? BindingOfClr(NeoClient client, Type type)
        {
            if (type.IsGenericParameter)
                return ParameterOfClr(client, type);
            foreach ((Type clr, NeoObjectTypeKind? objectType) in objectTypes)
            {
                if (clr == type)
                    return TypeBinding.Closed(new TypeHead(MemberKind.Object, true, objectType: objectType));
            }
            foreach ((Type clr, MemberKind kind) in leafTypes)
            {
                if (clr == type)
                    return TypeBinding.Closed(new TypeHead(kind, true));
            }
            if (type == typeof(NeoReferenceEqualityComparer))
                return SystemComparer(client, NeoSystemComparerClasses.ReferenceEqualityComparer, null);
            if (Nullable.GetUnderlyingType(type) is Type underlying)
                return BindingOfClr(client, underlying) is TypeBinding { Kind: TypeBindingKind.Closed } value ? value.WithRequired(false) : null;
            if (type.IsGenericTypeDefinition)
                return DefinitionOfClr(client, type) is TypeDefinition definition ? TypeBinding.OfDefinition(definition) : null;
            if (client.TypeArguments.TryGetId(type, out MemberKind registered, out string id))
                return TypeBinding.Closed(Declared(registered, id));
            if (!type.IsGenericType)
                return null;
            Type owner = type.GetGenericTypeDefinition();
            Type[] arguments = type.GetGenericArguments();
            var bound = new TypeBinding[arguments.Length];
            for (int index = 0; index < arguments.Length; index++)
            {
                if (BindingOfClr(client, arguments[index]) is not TypeBinding argument)
                    return null;
                bound[index] = argument;
            }
            if (owner == typeof(NeoDictionary<>) || owner == typeof(NeoReadOnlyDictionary<>))
                return Positional(MemberKind.Dictionary, TypeBinding.Closed(new TypeHead(MemberKind.String, true)), bound[0]);
            if (owner == typeof(EqualityComparer<>))
                return SystemComparer(client, NeoSystemComparerClasses.EqualityComparer, bound[0]);
            if (owner == typeof(Comparer<>))
                return SystemComparer(client, NeoSystemComparerClasses.Comparer, bound[0]);
            if (DefinitionOfClr(client, owner) is not TypeDefinition defined)
                return null;
            if (defined.Type is MemberKind.Class or MemberKind.Interface)
            {
                List<GenericParamDeclaration> parameters = DeclaredParameters(client, defined)!;
                var declared = new Dictionary<string, TypeBinding>(arguments.Length, StringComparer.Ordinal);
                for (int index = 0; index < arguments.Length; index++)
                    declared[parameters[index].id] = bound[index];
                return TypeBinding.Closed(Declared(defined.Type, (defined.ClassId ?? defined.InterfaceId)!), declared);
            }
            return Positional(defined.Type, bound);
        }

        private static TypeHead Declared(MemberKind kind, string id) => kind switch
        {
            MemberKind.Enum => new TypeHead(MemberKind.Enum, true, enumId: id),
            MemberKind.Interface => new TypeHead(MemberKind.Interface, true, interfaceId: id),
            _ => new TypeHead(MemberKind.Class, true, classId: id),
        };

        private static TypeBinding Positional(MemberKind kind, params TypeBinding[] arguments)
        {
            var positions = new Dictionary<string, TypeBinding>(arguments.Length, StringComparer.Ordinal);
            for (int index = 0; index < arguments.Length; index++)
                positions[index.ToString(CultureInfo.InvariantCulture)] = arguments[index];
            return TypeBinding.Closed(new TypeHead(kind, true), positions);
        }

        private static TypeBinding? SystemComparer(NeoClient client, string name, TypeBinding? argument)
        {
            foreach (NeoSchemaClass candidate in client.ProjectDataForRuntime.classes.Values)
            {
                if (NeoSystemComparerClasses.NameOf(candidate.id) != name)
                    continue;
                var head = new TypeHead(MemberKind.Class, true, classId: candidate.id);
                return argument is null
                    ? TypeBinding.Closed(head)
                    : TypeBinding.Closed(head, new Dictionary<string, TypeBinding>(StringComparer.Ordinal) { [candidate.genericParams![0].id] = argument });
            }
            return null;
        }

        /// <summary>The NeoScript definition a C# generic definition names, or null.</summary>
        private static TypeDefinition? DefinitionOfClr(NeoClient client, Type definition)
        {
            if (definition == typeof(Nullable<>))
                return new TypeDefinition(MemberKind.Unknown, nullable: true);
            if (definition == typeof(List<>) || definition == typeof(NeoList<>) || definition == typeof(NeoReadOnlyList<>))
                return new TypeDefinition(MemberKind.List);
            if (definition == typeof(HashSet<>) || definition == typeof(NeoSet<>) || definition == typeof(NeoReadOnlySet<>)
                || definition == typeof(NeoEnumSet<>) || definition == typeof(NeoReadOnlyEnumSet<>))
                return new TypeDefinition(MemberKind.Set);
            if (definition == typeof(Dictionary<,>) || definition == typeof(NeoDictionary<,>) || definition == typeof(NeoReadOnlyDictionary<,>))
                return new TypeDefinition(MemberKind.Dictionary);
            if (definition == typeof(NeoReference<>))
                return new TypeDefinition(MemberKind.Reference);
            int tuple = Array.IndexOf(valueTupleDefinitions, definition);
            if (tuple >= 0)
                return new TypeDefinition(MemberKind.Tuple, arity: tuple + 2);
            int callable = Array.IndexOf(delegateDefinitions, definition);
            if (callable >= 0)
                return new TypeDefinition(MemberKind.NSDelegate, arity: callable + 1);
            if (!client.TypeArguments.TryGetId(definition, out MemberKind kind, out string id))
                return null;
            return kind == MemberKind.Interface
                ? new TypeDefinition(MemberKind.Interface, interfaceId: id)
                : new TypeDefinition(MemberKind.Class, classId: id);
        }

        private static TypeBinding? ParameterOfClr(NeoClient client, Type parameter)
        {
            if (parameter.DeclaringMethod is not null || parameter.DeclaringType is not Type owner)
                return null;
            if (DefinitionOfClr(client, owner) is not TypeDefinition definition)
                return null;
            int position = parameter.GenericParameterPosition;
            string param = DeclaredParameters(client, definition) is { } parameters
                ? parameters[position].id
                : position.ToString(CultureInfo.InvariantCulture);
            return TypeBinding.Open(definition, param);
        }

        private static List<GenericParamDeclaration>? DeclaredParameters(NeoClient client, TypeDefinition definition)
        {
            if (definition.Type == MemberKind.Class && client.TryGetClass(definition.ClassId!, out NeoSchemaClass? schemaClass))
                return schemaClass.genericParams;
            if (definition.Type == MemberKind.Interface && client.TryGetInterface(definition.InterfaceId!, out Interface? record))
                return record.genericParams;
            return null;
        }

        /// <summary>The C# type a NeoScript type maps to, the inverse of <see cref="BindingOfClr"/>, or null.</summary>
        internal static Type? ClrTypeOf(NeoClient client, TypeBinding binding)
        {
            switch (binding.Kind)
            {
                case TypeBindingKind.Definition:
                    return ClrDefinitionOf(client, binding.Definition!);
                case TypeBindingKind.Open:
                    {
                        if (ClrDefinitionOf(client, binding.Definition!) is not Type owner)
                            return null;
                        int position = DeclaredParameters(client, binding.Definition!) is { } parameters
                            ? parameters.FindIndex(parameter => parameter.id == binding.Param)
                            : int.Parse(binding.Param!, CultureInfo.InvariantCulture);
                        Type[] open = owner.GetGenericArguments();
                        return position >= 0 && position < open.Length ? open[position] : null;
                    }
                case TypeBindingKind.MethodParam:
                    return null;
            }
            TypeHead head = binding.Head!;
            if (head.IsOpaque)
                return null;
            if (!head.Required && TypeBindings.IsValueType(head))
                return ClrTypeOf(client, binding.WithRequired(true)) is Type value ? typeof(Nullable<>).MakeGenericType(value) : null;
            switch (head.Type)
            {
                case MemberKind.Object:
                    foreach ((Type clr, NeoObjectTypeKind? objectType) in objectTypes)
                    {
                        if (objectType == head.ObjectType)
                            return clr;
                    }
                    return null;
                case MemberKind.Enum:
                    return client.TypeArguments.TryGetEnumType(head.EnumId!, out Type option) ? option : null;
                case MemberKind.Class:
                    switch (NeoSystemComparerClasses.NameOf(head.ClassId!))
                    {
                        case NeoSystemComparerClasses.ReferenceEqualityComparer:
                            return typeof(NeoReferenceEqualityComparer);
                        case NeoSystemComparerClasses.EqualityComparer:
                            return Construct(client, typeof(EqualityComparer<>), binding, null);
                        case NeoSystemComparerClasses.Comparer:
                            return Construct(client, typeof(Comparer<>), binding, null);
                    }
                    return client.TypeArguments.TryGetClassType(head.ClassId!, out Type schemaClass)
                        ? Construct(client, schemaClass, binding, ClassParameters(client, head.ClassId!))
                        : null;
                case MemberKind.Interface:
                    return client.TypeArguments.TryGetInterfaceType(head.InterfaceId!, out Type contract)
                        ? Construct(client, contract, binding, client.TryGetInterface(head.InterfaceId!, out Interface? record) ? record.genericParams : null)
                        : null;
            }
            if (head.Type is MemberKind.List or MemberKind.Set or MemberKind.Dictionary or MemberKind.Tuple or MemberKind.NSDelegate or MemberKind.Reference)
            {
                Type? definition = ClrDefinitionOf(client, new TypeDefinition(head.Type, arity: binding.Bindings?.Count));
                return definition is null ? null : Construct(client, definition, binding, null);
            }
            foreach ((Type clr, MemberKind kind) in leafTypes)
            {
                if (kind == head.Type)
                    return clr;
            }
            return null;
        }

        private static List<GenericParamDeclaration>? ClassParameters(NeoClient client, string classId) =>
            client.TryGetClass(classId, out NeoSchemaClass? schemaClass) ? schemaClass.genericParams : null;

        /// <summary><paramref name="definition"/> closed over <paramref name="binding"/>'s arguments, in declared or positional order.</summary>
        private static Type? Construct(NeoClient client, Type definition, TypeBinding binding, List<GenericParamDeclaration>? parameters)
        {
            if (!definition.IsGenericTypeDefinition)
                return binding.Bindings is null ? definition : null;
            if (binding.Bindings is not { } bindings)
                return null;
            var arguments = new Type[bindings.Count];
            for (int index = 0; index < arguments.Length; index++)
            {
                string key = parameters is { } declared ? declared[index].id : index.ToString(CultureInfo.InvariantCulture);
                if (!bindings.TryGetValue(key, out TypeBinding? argument) || ClrTypeOf(client, argument) is not Type clr)
                    return null;
                arguments[index] = clr;
            }
            return definition.GetGenericArguments().Length == arguments.Length ? definition.MakeGenericType(arguments) : null;
        }

        private static Type? ClrDefinitionOf(NeoClient client, TypeDefinition definition)
        {
            if (definition.Nullable)
                return typeof(Nullable<>);
            int arity = definition.Arity ?? 0;
            switch (definition.Type)
            {
                case MemberKind.List:
                    return typeof(List<>);
                case MemberKind.Set:
                    return typeof(HashSet<>);
                case MemberKind.Dictionary:
                    return typeof(Dictionary<,>);
                case MemberKind.Reference:
                    return typeof(NeoReference<>);
                case MemberKind.Tuple:
                    return arity >= 2 && arity <= valueTupleDefinitions.Length + 1 ? valueTupleDefinitions[arity - 2] : null;
                case MemberKind.NSDelegate:
                    return arity >= 1 && arity <= delegateDefinitions.Length ? delegateDefinitions[arity - 1] : null;
                case MemberKind.Class:
                    return client.TypeArguments.TryGetClassType(definition.ClassId!, out Type schemaClass) ? schemaClass : null;
                case MemberKind.Interface:
                    return client.TypeArguments.TryGetInterfaceType(definition.InterfaceId!, out Type contract) ? contract : null;
                default:
                    return null;
            }
        }
    }
}
