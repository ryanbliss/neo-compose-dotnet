// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript.Globalization;
using NeoCompose.Runtime.NeoScript.Hashing;
using NeoCompose.Runtime.NeoScript.Numerics;
using JsonEnum = NeoCompose.Runtime.Json.Enum;
using JsonMember = NeoCompose.Runtime.Json.Member;
using JObject = Newtonsoft.Json.Linq.JObject;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// P127 §1 to §4: boxes, casts, explicit conversions, <c>default</c>,
    /// <c>typeof</c> and the <c>object</c> members.
    /// </summary>
    public static partial class NSGetterEvaluator
    {
        private const string NullReferenceMessage = "Object reference not set to an instance of an object.";

        private static readonly TypeBinding TypeType =
            TypeBinding.Closed(new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.Type));

        // Each enum's options in authored order, indexed once for their numeric values.
        private static readonly ConditionalWeakTable<JsonEnum, EnumShape> enumShapes = new();

        private sealed class EnumShape
        {
            internal readonly List<string> Order;
            internal readonly NeoEnumOptions Options;

            internal EnumShape(JsonEnum enumType)
            {
                Order = enumType.OptionKeyOrder();
                var options = new List<NeoEnumOption>(Order.Count);
                foreach (string optionId in Order)
                    options.Add(new NeoEnumOption(optionId, enumType.options[optionId].text));
                Options = new NeoEnumOptions(options);
            }
        }

        /// <summary>
        /// Whether a read of a <c>valueType</c> row through
        /// <paramref name="member"/> wants the payload: the caller names the
        /// held type's member. A read through the slot, or with no member,
        /// gets the box (§1.3).
        /// </summary>
        private static bool ReadsPayload(JsonMember? member) =>
            member is not null and not ObjectMember && !(member is ClassMember slot && slot.TryGetInterfaceId(out _));

        /// <summary>
        /// §1.3. A stored box as the evaluator holds it: a <c>Type</c> value,
        /// <c>new object()</c>'s identity, or a <see cref="NeoBox"/> of the
        /// payload read as a field of the held type.
        /// </summary>
        private static object? ReadBoxRow(MemberValue row, TypeBinding valueType, NeoValueOwnership ownership, Context ctx)
        {
            NeoBoxForm form = ctx.client.TypeRules.Box(valueType);
            if (NeoBox.WrittenTo(row) is { } written)
            {
                if (written.Payload is not null && form.Member is { } heldMember
                    && valueType.Head!.Type is MemberKind.List or MemberKind.Set or MemberKind.Dictionary)
                    BindInsertedCollection(written.Payload, heldMember, row.id, ownership, ctx);
                return written;
            }
            switch (form.Kind)
            {
                case NeoBoxFormKind.Identity:
                    NeoBox identity = NeoBox.NewObject();
                    NeoBox.Remember(row, identity);
                    return identity;
                case NeoBoxFormKind.Comparer:
                    throw new NSGetterRuntimeError("Reading a stored comparer isn't supported yet (P127 B6).");
                case NeoBoxFormKind.Invalid:
                    throw new NSGetterRuntimeError($"Value '{row.id}' has {form.Invalid}.");
            }
            if (form.Member is ObjectMember { objectType: NeoObjectTypeKind declared })
                return ReadDeclaredRow(row, declared);
            object? held = UnwrapCached(row, ctx, ownership, form.Member);
            if (held is null)
                return null;
            NeoBox box = NeoBox.Of(valueType, held);
            NeoBox.Remember(row, box);
            return box;
        }

        /// <summary>
        /// A stored <c>Type</c>, as a <c>Type</c> slot or a box holds it
        /// (§12.1). The evaluator reads no stored culture or number format
        /// until P127 B7.
        /// </summary>
        private static object ReadDeclaredRow(MemberValue row, NeoObjectTypeKind objectType)
        {
            if (objectType != NeoObjectTypeKind.Type)
                throw new NSGetterRuntimeError($"Reading a stored {NeoTypeNames.ObjectName(objectType)} isn't supported yet (P127 B7).");
            if (row is not PayloadMemberValue { value: JObject payload })
                throw new NSGetterRuntimeError($"Value '{row.id}' holds a Type without a Type payload.");
            return NeoTypeValue.Of(TypeBindings.Read((JObject)payload["type"]!, $"Value '{row.id}' type"));
        }

        /// <summary>§3.1 <c>box</c>: rows, boxes and <c>Type</c> values pass through; any other value boxes.</summary>
        private static object? EvalBox(BoxPointer box, NeoScriptScope scope, Context ctx)
        {
            object? value = EvalPointer(box.pointer, scope, ctx);
            if (value is null or NeoBox or NeoTypeValue)
                return value;
            TypeInfo source = CloseContextType(box.sourceType, ctx);
            if (IsReferenceStatic(source) && value is not NeoVariantReference)
                return value;
            TypeBinding valueType = value is NeoDelegateValue { delegateType: { } own }
                ? NeoGeneratedTypesSupport.Erased(own)
                : HeldTypeOf(source);
            return NeoBox.Of(valueType, value);
        }

        /// <summary>§3.2 <c>cast</c> and <c>as</c>.</summary>
        private static object? EvalCast(CastPointer cast, NeoScriptScope scope, Context ctx)
        {
            object? value = EvalPointer(cast.pointer, scope, ctx);
            TypeInfo target = CloseContextType(cast.targetType, ctx);
            bool isAs = cast.mode == CastModeKind.As;
            if (value is null)
            {
                if (!isAs && target.required && NeoTypeRules.IsValueKind(target.type))
                    throw new NSGetterRuntimeError(NullReferenceMessage, NeoErrorContract.NeoNullReferenceError);
                return null;
            }
            TypeBinding? held = HeldType(value, cast.sourceType, ctx, out object payload);
            if (TryCast(value, held, payload, target, ctx, out object? result))
                return result;
            if (isAs)
                return null;
            throw InvalidCast(HeldName(value, held, ctx), TypeName(RuntimeTypeOf(target), ctx));
        }

        /// <summary>
        /// Whether a non-null value converts to <paramref name="target"/>
        /// (§3.2 steps 4 and 5), and what it converts to: the payload for a
        /// value-kind or other non-reference target, the value itself, boxed
        /// when it was raw, for <c>object</c>, <c>ValueType</c> and <c>Enum</c>.
        /// </summary>
        /// <param name="held">The value's held type; null for a row, or a raw value read through an interface or <c>object</c>.</param>
        private static bool TryCast(object value, TypeBinding? held, object payload, TypeInfo target, Context ctx, out object? result)
        {
            result = null;
            switch (target)
            {
                case ObjectTypeInfo objectType:
                    {
                        bool matches = objectType.objectType switch
                        {
                            null => true,
                            NeoObjectTypeKind.ValueType => held is { } valueType && TypeBindings.IsValueType(valueType.Head!),
                            NeoObjectTypeKind.Enum => held?.Head!.Type == MemberKind.Enum,
                            NeoObjectTypeKind.Type => value is NeoTypeValue,
                            // No evaluator value is a culture, format or comparer before P127 B6 and B7.
                            _ => false,
                        };
                        if (matches)
                            result = held is null || value is NeoBox or NeoTypeValue ? value : NeoBox.Of(held, value);
                        return matches;
                    }
                case InterfaceTypeInfo interfaceType:
                    if (held is null)
                    {
                        result = value;
                        return RuntimeTypeCheck(value, target, ctx);
                    }
                    if (NeoPrimitiveConformance.Kind(held) is not null)
                    {
                        if (!NeoInterfaceDispatch.Of(ctx.client).PrimitiveConforms(held, RuntimeTypeOf(CloseContextType(interfaceType, ctx))))
                            return false;
                        result = value is NeoBox ? value : NeoBox.Of(held, payload);
                        return true;
                    }
                    // Restore the held collection's element type before checking its protocol.
                    result = RetainSequenceCollectionType(payload, NeoTypeBindings.ToTypeInfo(held), ctx);
                    return IsIntrinsicSequence(result, target, ctx);
                default:
                    if (held is null)
                    {
                        result = value;
                        return RuntimeTypeCheck(value, target, ctx);
                    }
                    result = payload;
                    return held.Equals(RuntimeTypeOf(target).WithRequired(true));
            }
        }

        /// <summary>§3.3 <c>convert</c>: explicit conversions between int, float, decimal and enums.</summary>
        private static object? EvalConvert(ConvertPointer convert, NeoScriptScope scope, Context ctx)
        {
            object? value = EvalPointer(convert.pointer, scope, ctx);
            TypeInfo target = CloseContextType(convert.targetType, ctx);
            if (value is null)
            {
                if (target.required)
                    throw new NeoRuntimeError(NeoErrorClass.NeoInvalidOperationError, "Nullable object must have a value.");
                return null;
            }
            TypeInfo source = CloseContextType(convert.sourceType, ctx);
            bool isChecked = convert.isChecked == true;
            switch (target.type)
            {
                case MemberKind.Int:
                    return Box((double)ConvertToInt32(value, source, isChecked, ctx));
                case MemberKind.Float:
                    return source.type switch
                    {
                        MemberKind.Float => value,
                        MemberKind.Decimal => Box((double)NeoNumericConversions.DecimalToSingle((string)value)),
                        _ => Box((double)(float)ConvertToInt32(value, source, isChecked, ctx)),
                    };
                case MemberKind.Decimal:
                    return source.type switch
                    {
                        MemberKind.Decimal => value,
                        MemberKind.Float => NeoNumericConversions.SingleToDecimal((float)Number(value)),
                        _ => NeoNumericConversions.DecimalFromInt32(ConvertToInt32(value, source, isChecked, ctx)),
                    };
                case MemberKind.Enum:
                    return EnumOfNumber(ConvertToInt32(value, source, isChecked, ctx), ((EnumTypeInfo)target).enumId, ctx);
                default:
                    throw new NSGetterRuntimeError($"A conversion can't target {target.type}.");
            }
        }

        /// <summary>A number or enum as the int an explicit conversion reads (§3.3).</summary>
        private static int ConvertToInt32(object value, TypeInfo source, bool isChecked, Context ctx) => source.type switch
        {
            MemberKind.Int => NeoInt32.Wrap(Number(value)),
            MemberKind.Float => NeoNumericConversions.SingleToInt32((float)Number(value), isChecked),
            MemberKind.Decimal => NeoNumericConversions.DecimalToInt32((string)value),
            MemberKind.Enum => EnumNumber(value, ((EnumTypeInfo)source).enumId, ctx),
            _ => throw new NSGetterRuntimeError($"A conversion can't read {source.type}."),
        };

        private static double Number(object value) =>
            TryAsDouble(value, out double number) ? number : throw new NSGetterRuntimeError("A conversion's operand must be a number.");

        /// <summary>§8.6. An enum value's number: its option's declaration index, or an orphan's number.</summary>
        private static int EnumNumber(object value, string enumId, Context ctx) =>
            enumId == NeoPrimitiveConformance.NumberStylesEnumId ? NeoInt32.Wrap(Number(value))
                : EnumShapeOf(enumId, ctx.client).Options.NumericValue(EnumOptionId(value));

        /// <summary>The declared option numbered <paramref name="number"/>; any other number fails, as §3.3 requires.</summary>
        private static object EnumOfNumber(int number, string enumId, Context ctx)
        {
            if (enumId == NeoPrimitiveConformance.NumberStylesEnumId)
                return number;
            List<string> order = EnumShapeOf(enumId, ctx.client).Order;
            if (number < 0 || number >= order.Count)
                throw InvalidCast("int", EnumOf(enumId, ctx.client).name);
            return new object?[] { order[number] };
        }

        private static string EnumOptionId(object value) =>
            value is object?[] { Length: 1 } selection && selection[0] is string optionId
                ? optionId
                : throw new NSGetterRuntimeError("An enum value must hold one option.");

        private static JsonEnum EnumOf(string enumId, NeoClient client) =>
            client.TryGetEnum(enumId, out JsonEnum? enumType)
                ? enumType
                : throw new NSGetterRuntimeError($"Enum '{enumId}' was not found.");

        private static EnumShape EnumShapeOf(string enumId, NeoClient client) =>
            enumShapes.GetValue(EnumOf(enumId, client), enumType => new EnumShape(enumType));

        /// <summary>§3.4 <c>default</c> of a type with a type parameter in it, closed over the frame.</summary>
        private static object? EvalDefault(DefaultPointer pointer, Context ctx) =>
            DefaultOf(CloseContextType(pointer.typeInfo, ctx), ctx.client);

        /// <summary>
        /// P127 §4.1. An omitted parameter's default: <c>= default</c> gives
        /// <c>default</c> of <paramref name="frameType"/>, the parameter's
        /// type closed in the callee's frame, and any other default is its
        /// stored constant.
        /// </summary>
        internal static object? ParameterDefault(NeoClient client, FunctionArgumentTypeInfo parameter, TypeInfo frameType, string subject)
        {
            if (parameter.defaultValue is not { isDefault: true })
            {
                if (parameter.type is MemberKind.Object or MemberKind.Interface && parameter.defaultValue?.value is not null)
                {
                    string declared = NeoTypeNames.Of(NeoTypeBindings.ArgumentOf(NeoTypeRules.Declared(frameType)), client.ProjectDataForRuntime);
                    throw new NSGetterRuntimeError($"{subject} parameter '{parameter.name}' of declared type '{declared}' has a non-null default. Reference-type parameter defaults must be null or default.");
                }
                return NeoParameterDefaults.DefaultRuntimeValue(parameter, subject);
            }
            if (NeoNSFunctionRuntime.ContainsGeneric(frameType))
                throw new NSGetterRuntimeError($"{subject} parameter '{parameter.name}' declares '= default' of a type parameter its frame doesn't bind.");
            return DefaultOf(frameType, client);
        }

        internal static object? DefaultOf(TypeInfo type, NeoClient client)
        {
            if (!type.required)
                return null;
            switch (type.type)
            {
                case MemberKind.Int:
                case MemberKind.Float:
                    return Box(0d);
                case MemberKind.Decimal:
                    return "0";
                case MemberKind.Bool:
                    return Box(false);
                case MemberKind.Enum:
                    {
                        // A parameter's own type carries its enum id inline.
                        string enumId = type is FunctionArgumentTypeInfo parameter ? parameter.enumId! : ((EnumTypeInfo)type).enumId;
                        if (enumId == NeoPrimitiveConformance.NumberStylesEnumId)
                            return 0;
                        List<string> order = EnumShapeOf(enumId, client).Order;
                        if (order.Count == 0)
                        {
                            throw new NeoRuntimeError(
                                NeoErrorClass.NeoInvalidOperationError,
                                $"Enum '{EnumOf(enumId, client).name}' has no options, so it has no default value.");
                        }
                        return new object?[] { order[0] };
                    }
                case MemberKind.Vector2:
                case MemberKind.Vector2Int:
                    return new NeoVector2Value();
                case MemberKind.Vector3:
                case MemberKind.Vector3Int:
                    return new NeoVector3Value();
                case MemberKind.Color:
                    return new NeoColorValue();
                case MemberKind.Tuple:
                    {
                        TupleTypeInfo tuple = TupleTypeInfo.From(type)!;
                        var elements = new object?[tuple.elements.Length];
                        for (int index = 0; index < elements.Length; index++)
                            elements[index] = DefaultOf(tuple.elements[index].typeInfo, client);
                        return new RuntimeTuple(elements, tuple);
                    }
                default:
                    return null;
            }
        }

        /// <summary>§3.5 <c>typeof</c>: the interned <c>Type</c> of a type closed over the frame, or of a definition.</summary>
        private static NeoTypeValue EvalTypeOf(TypeOfPointer pointer, Context ctx)
        {
            if (pointer.definition is { } definition)
                return NeoTypeValue.Of(TypeBinding.OfDefinition(definition.ToDefinition()));
            return NeoTypeValue.Of(RuntimeTypeOf(CloseContextType(pointer.typeInfo!, ctx)));
        }

        /// <summary>§3.6. The <c>object</c> members.</summary>
        private static object? EvalObjectMember(ObjectMemberPointer pointer, NeoScriptScope scope, Context ctx)
        {
            switch (pointer.member)
            {
                case ObjectMemberKind.ReferenceEquals:
                    return Box(SameObject(EvalPointer(pointer.args[0], scope, ctx), EvalPointer(pointer.args[1], scope, ctx), ctx));
                case ObjectMemberKind.StaticEquals:
                    {
                        object? left = EvalPointer(pointer.args[0], scope, ctx);
                        object? right = EvalPointer(pointer.args[1], scope, ctx);
                        if (SameObject(left, right, ctx))
                            return Box(true);
                        if (left is null || right is null)
                            return Box(false);
                        return Box(ObjectEquals(left, null, right, ctx));
                    }
            }
            object? receiver = EvalPointer(pointer.receiver!, scope, ctx);
            if (receiver is null && pointer.optional == true)
                return null;
            object? argument = pointer.member == ObjectMemberKind.Equals ? EvalPointer(pointer.args[0], scope, ctx) : null;
            if (receiver is null)
                throw new NSGetterRuntimeError(NullReferenceMessage, NeoErrorContract.NeoNullReferenceError);
            switch (pointer.member)
            {
                case ObjectMemberKind.GetType:
                    return NeoTypeValue.Of(RuntimeTypeOfValue(receiver, pointer.receiverType, ctx));
                case ObjectMemberKind.Equals:
                    return Box(ObjectEquals(receiver, pointer.receiverType, argument, ctx, pointer.dispatch == "base"));
                case ObjectMemberKind.GetHashCode:
                    return Box((double)ObjectHash(receiver, pointer.receiverType, ctx, pointer.dispatch == "base"));
                case ObjectMemberKind.ToString:
                    return ObjectText(receiver, pointer.receiverType, ctx, pointer.dispatch == "base");
                case ObjectMemberKind.Text:
                    return EnumOptionText(receiver, pointer.receiverType, ctx);
                default:
                    throw new NSGetterRuntimeError($"Unknown object member '{pointer.member}'.");
            }
        }

        /// <summary>An enum value's <c>Text()</c>: its option's localized text, or the id of an option the enum doesn't declare.</summary>
        private static string EnumOptionText(object receiver, TypeInfo? receiverType, Context ctx)
        {
            if (HeldType(receiver, receiverType, ctx, out object payload) is not { Head: { Type: MemberKind.Enum } head })
                throw new NSGetterRuntimeError("Text() has a non-enum receiver.");
            string optionId = EnumOptionId(payload);
            if (!ctx.client.TryGetEnum(head.EnumId!, out JsonEnum? enumType) || !enumType.options.TryGetValue(optionId, out EnumOption? option))
                return optionId;
            return ctx.client.Localization.TryResolveText(option.text, out string localized) ? localized : option.text;
        }

        /// <summary><c>object.ReferenceEquals</c> (§1.2): one row, one box, string boxes of equal strings, or both null.</summary>
        private static bool SameObject(object? left, object? right, Context ctx)
        {
            if (NeoBox.SameIdentity(left, right))
                return true;
            if (left is NeoBox or NeoTypeValue || right is NeoBox or NeoTypeValue)
                return false;
            return ReferenceEqual(left, right, ctx);
        }

        /// <summary>The virtual <c>Equals(object?)</c>: a row's override or identity, else the payload type's.</summary>
        private static bool ObjectEquals(object receiver, TypeInfo? receiverType, object? other, Context ctx, bool direct = false)
        {
            TypeBinding? held = HeldType(receiver, receiverType, ctx, out object payload);
            if (held is null)
            {
                if (!direct && RowOverride(receiver, "Equals", ctx) is { } overrideId)
                    return JsTruthy(InvokeObjectOverride(overrideId, receiver, new[] { other }, ctx));
                return SameObject(receiver, other, ctx);
            }
            if (other is null)
                return false;
            if (receiver is NeoTypeValue)
                return ReferenceEquals(receiver, other);
            TypeBinding? otherHeld = HeldType(other, null, ctx, out object otherPayload);
            if (otherHeld is null || !held.Equals(otherHeld))
                return false;
            return PayloadEquals(held, payload, otherPayload, ctx, receiver, other);
        }

        private static bool PayloadEquals(TypeBinding held, object payload, object otherPayload, Context ctx,
            object? leftIdentity = null, object? rightIdentity = null)
        {
            switch (held.Head!.Type)
            {
                case MemberKind.Float:
                    return SingleEquals(Number(payload), Number(otherPayload));
                case MemberKind.Decimal:
                    return NeoDecimalMath.Compare((string)payload, (string)otherPayload) == 0;
                case MemberKind.Int:
                case MemberKind.Bool:
                case MemberKind.String:
                case MemberKind.Enum:
                case MemberKind.Sprite:
                case MemberKind.Audio:
                case MemberKind.DialogueLookup:
                    return JsEqual(payload, otherPayload, ctx);
                case MemberKind.Vector2:
                case MemberKind.Vector3:
                    {
                        var left = (NeoVector2Value)payload;
                        var right = (NeoVector2Value)otherPayload;
                        return (float)left.x == (float)right.x
                            && (float)left.y == (float)right.y
                            && (left is not NeoVector3Value left3 || (float)left3.z == (float)((NeoVector3Value)right).z);
                    }
                case MemberKind.Vector2Int:
                case MemberKind.Vector3Int:
                    {
                        var left = (NeoVector2Value)payload;
                        var right = (NeoVector2Value)otherPayload;
                        return NeoInt32.Wrap(left.x) == NeoInt32.Wrap(right.x)
                            && NeoInt32.Wrap(left.y) == NeoInt32.Wrap(right.y)
                            && (left is not NeoVector3Value left3 || NeoInt32.Wrap(left3.z) == NeoInt32.Wrap(((NeoVector3Value)right).z));
                    }
                case MemberKind.Color:
                    {
                        var left = (NeoColorValue)payload;
                        var right = (NeoColorValue)otherPayload;
                        return left.r.Equals(right.r) && left.g.Equals(right.g) && left.b.Equals(right.b) && left.a.Equals(right.a);
                    }
                case MemberKind.Tuple:
                    return NeoTuples.TupleEquals((RuntimeTuple)payload, (RuntimeTuple)otherPayload,
                        (index, a, b) => DefaultTupleElementEquals(held.At(index), a, b, ctx));
                case MemberKind.NSDelegate:
                    throw new NSGetterRuntimeError($"Equals on a boxed {TypeName(held, ctx)} isn't supported yet (P127 B6).");
                default:
                    // new object(), collections, slot references, variants and sequences compare by identity.
                    return SameObject(leftIdentity ?? payload, rightIdentity ?? otherPayload, ctx) || ReferenceEquals(payload, otherPayload);
            }
        }

        /// <summary><c>float.Equals</c>: NaN equals NaN.</summary>
        private static bool SingleEquals(double left, double right) => ((float)left).Equals((float)right);

        /// <summary>The virtual <c>GetHashCode()</c>: a row's override or the identity hash of its id, else the payload type's.</summary>
        private static int ObjectHash(object receiver, TypeInfo? receiverType, Context ctx, bool direct = false)
        {
            TypeBinding? held = HeldType(receiver, receiverType, ctx, out object payload);
            if (held is null)
            {
                if (!direct && RowOverride(receiver, "GetHashCode", ctx) is { } overrideId)
                    return NeoInt32.Wrap(Number(InvokeObjectOverride(overrideId, receiver, System.Array.Empty<object?>(), ctx)!));
                string rowId = FindRowIdByReference(receiver, ctx)
                    ?? throw new NSGetterRuntimeError("GetHashCode() needs a row identity; attach the object first.");
                return NeoHashes.String(rowId);
            }
            switch (held.Head!.Type)
            {
                case MemberKind.Int:
                    return NeoInt32.Wrap(Number(payload));
                case MemberKind.Float:
                    return NeoHashes.Float((float)Number(payload));
                case MemberKind.Decimal:
                    return NeoHashes.Decimal((string)payload);
                case MemberKind.Bool:
                    return NeoHashes.Bool((bool)payload);
                case MemberKind.String:
                    return NeoHashes.String((string)payload);
                case MemberKind.Enum:
                    return EnumNumber(payload, held.Head.EnumId!, ctx);
                case MemberKind.Vector2:
                    {
                        var vector = (NeoVector2Value)payload;
                        return NeoHashes.Vector2((float)vector.x, (float)vector.y);
                    }
                case MemberKind.Vector2Int:
                    {
                        var vector = (NeoVector2Value)payload;
                        return NeoHashes.Vector2Int(NeoInt32.Wrap(vector.x), NeoInt32.Wrap(vector.y));
                    }
                case MemberKind.Vector3:
                    {
                        var vector = (NeoVector3Value)payload;
                        return NeoHashes.Vector3((float)vector.x, (float)vector.y, (float)vector.z);
                    }
                case MemberKind.Vector3Int:
                    {
                        var vector = (NeoVector3Value)payload;
                        return NeoHashes.Vector3Int(NeoInt32.Wrap(vector.x), NeoInt32.Wrap(vector.y), NeoInt32.Wrap(vector.z));
                    }
                case MemberKind.Tuple:
                    return NeoTuples.TupleHash((RuntimeTuple)payload, (index, value) => ObjectHash(value!, NeoTypeBindings.ToTypeInfo(held.At(index)), ctx));
                case MemberKind.Color:
                    {
                        var color = (NeoColorValue)payload;
                        return NeoHashes.Color(color.r, color.g, color.b, color.a);
                    }
                default:
                    throw new NSGetterRuntimeError($"GetHashCode() on a {TypeName(held, ctx)} isn't supported yet (P127 B6).");
            }
        }

        /// <summary>The virtual <c>ToString()</c>: a row's override or its type's full name, else the payload type's text.</summary>
        private static string? ObjectText(object receiver, TypeInfo? receiverType, Context ctx, bool direct = false)
        {
            TypeBinding? held = HeldType(receiver, receiverType, ctx, out object payload);
            if (held is null)
            {
                if (!direct && RowOverride(receiver, "ToString", ctx) is { } overrideId)
                    return (string?)InvokeObjectOverride(overrideId, receiver, System.Array.Empty<object?>(), ctx);
                return TypeName(RuntimeTypeOfValue(receiver, receiverType, ctx), ctx);
            }
            return PayloadText(payload, held, ctx);
        }

        /// <summary>§4.6 <c>format</c>: an interpolation hole or a concatenation operand as text.</summary>
        private static string EvalFormat(FormatPointer pointer, NeoScriptScope scope, Context ctx)
        {
            object? value = EvalPointer(pointer.pointer, scope, ctx);
            string text = value is null ? "" : FormatText(value, pointer, ctx);
            return pointer.alignment is int alignment ? NeoStrings.Align(text, alignment) : text;
        }

        /// <summary>A hole formats a built-in <c>IFormattable</c> with <c>ToString(format, null)</c>; anything else calls <c>ToString()</c>.</summary>
        private static string FormatText(object value, FormatPointer pointer, Context ctx)
        {
            switch (pointer.mode)
            {
                case FormatModeKind.Interpolation:
                    if (HeldType(value, pointer.sourceType, ctx, out object payload) is { } held
                        && FormattableText(payload, held, pointer.format, ctx) is { } formatted)
                    {
                        return formatted;
                    }
                    string formattableId = NeoPrimitiveConformance.InterfaceId("IFormattable");
                    if (FindRowClassIdByReference(value, ctx) is not null
                        && ctx.client.ProjectDataForRuntime.interfaces.TryGetValue(formattableId, out Interface? formattable))
                    {
                        var construction = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: formattableId));
                        if (NeoInterfaceDispatch.Of(ctx.client).Converts(RuntimeTypeOfValue(value, null, ctx), construction))
                            return (string?)InvokeMappedInterface(value, construction, formattable.members["ToString"].id!,
                                new object?[] { pointer.format, null }, ctx) ?? "";
                    }
                    break;
                case FormatModeKind.Concatenation:
                    break;
                default:
                    throw new NSGetterRuntimeError($"Unknown format mode '{pointer.mode}'.");
            }
            return ObjectText(value, pointer.sourceType, ctx) ?? "";
        }

        /// <summary>
        /// <c>IFormattable.ToString(format, null)</c> of a built-in
        /// formattable value, or null when its type isn't one. Unity's vectors
        /// and <c>Color</c> format their components with the invariant culture
        /// and default to <c>F2</c> / <c>F3</c>.
        /// </summary>
        private static string? FormattableText(object payload, TypeBinding held, string? format, Context ctx)
        {
            switch (held.Head!.Type)
            {
                case MemberKind.Int:
                    return NeoNumberFormatting.FormatInt32(NeoInt32.Wrap(Number(payload)), format, null);
                case MemberKind.Float:
                    return NeoNumberFormatting.FormatSingle((float)Number(payload), format, null);
                case MemberKind.Decimal:
                    return NeoNumberFormatting.FormatDecimal(decimal.Parse((string)payload, NumberStyles.Float, CultureInfo.InvariantCulture), format, null);
                case MemberKind.Enum:
                    return held.Head.EnumId == NeoPrimitiveConformance.NumberStylesEnumId
                        ? NeoNumberStyles.Format(NeoInt32.Wrap(Number(payload)), format)
                        : EnumShapeOf(held.Head.EnumId!, ctx.client).Options.Format(EnumOptionId(payload), format);
                case MemberKind.Vector2:
                    {
                        var vector = (NeoVector2Value)payload;
                        string component = UnityFormat(format, "F2");
                        return $"({UnitySingle(vector.x, component)}, {UnitySingle(vector.y, component)})";
                    }
                case MemberKind.Vector3:
                    {
                        var vector = (NeoVector3Value)payload;
                        string component = UnityFormat(format, "F2");
                        return $"({UnitySingle(vector.x, component)}, {UnitySingle(vector.y, component)}, {UnitySingle(vector.z, component)})";
                    }
                case MemberKind.Vector2Int:
                    {
                        var vector = (NeoVector2Value)payload;
                        return $"({UnityInt32(vector.x, format)}, {UnityInt32(vector.y, format)})";
                    }
                case MemberKind.Vector3Int:
                    {
                        var vector = (NeoVector3Value)payload;
                        return $"({UnityInt32(vector.x, format)}, {UnityInt32(vector.y, format)}, {UnityInt32(vector.z, format)})";
                    }
                case MemberKind.Color:
                    {
                        var color = (NeoColorValue)payload;
                        string component = UnityFormat(format, "F3");
                        return $"RGBA({UnitySingle(color.r, component)}, {UnitySingle(color.g, component)}, {UnitySingle(color.b, component)}, {UnitySingle(color.a, component)})";
                    }
                default:
                    return null;
            }
        }

        /// <summary>A held value's <c>ToString()</c> (§1.2's table), invariant until P127 B7 reads the current culture.</summary>
        private static string PayloadText(object? payload, TypeBinding held, Context ctx)
        {
            if (FormattableText(payload!, held, null, ctx) is { } formatted)
                return formatted;
            switch (held.Head!.Type)
            {
                case MemberKind.Bool:
                    return NeoStrings.FormatBoolean((bool)payload!);
                case MemberKind.String:
                    return (string)payload!;
                case MemberKind.Object when held.Head.ObjectType is null:
                    return "object";
                case MemberKind.Object when held.Head.ObjectType == NeoObjectTypeKind.Type:
                    return TypeName(((NeoTypeValue)payload!).Binding, ctx);
                case MemberKind.Tuple:
                    {
                        var tuple = (RuntimeTuple)payload!;
                        var parts = new string[tuple.Count];
                        for (int index = 0; index < parts.Length; index++)
                            parts[index] = ElementText(tuple[index], held.At(index), ctx);
                        return "(" + string.Join(", ", parts) + ")";
                    }
                case MemberKind.Sprite:
                    return AssetName(payload, ctx) + " (UnityEngine.Sprite)";
                case MemberKind.Audio:
                    return AssetName(payload, ctx) + " (UnityEngine.AudioClip)";
                default:
                    // Collections, sequences, slot references, delegates, variants and dialogue references print their type.
                    return TypeName(held, ctx);
            }
        }

        /// <summary>
        /// A sprite or audio clip's <c>Name</c>: its project file's name, a
        /// slice's without the extension and with <c>_</c> and its index.
        /// </summary>
        private static string AssetName(object? payload, Context ctx)
        {
            if (payload is not IDictionary<string, object?> record
                || !record.TryGetValue("fileId", out object? rawFileId)
                || rawFileId is not string { Length: > 0 } fileId)
            {
                throw new NSGetterRuntimeError("An asset value has no asset record.");
            }
            string fileName = ctx.client.ProjectDataForRuntime.files.TryGetValue(fileId, out ProjectFile? file) ? file.name : fileId;
            if (!record.TryGetValue("sliceIndex", out object? sliceIndex) || sliceIndex is null)
                return fileName;
            int dot = fileName.LastIndexOf('.');
            string stem = dot >= 0 && dot < fileName.Length - 1 && fileName.IndexOf('/', dot) < 0 ? fileName.Substring(0, dot) : fileName;
            return $"{stem}_{NeoNumberFormatting.FormatInt32(System.Convert.ToInt32(sliceIndex, CultureInfo.InvariantCulture), null, CultureInfo.InvariantCulture)}";
        }

        /// <summary>A tuple element's <c>ToString()</c>, null as empty, as <c>ValueTuple</c> prints it.</summary>
        private static string ElementText(object? element, TypeBinding elementType, Context ctx)
        {
            if (element is null)
                return "";
            return IsReferenceStatic(NeoTypeBindings.ToTypeInfo(elementType))
                ? ObjectText(element, null, ctx) ?? ""
                : PayloadText(element, elementType.WithRequired(true), ctx);
        }

        private static string UnitySingle(double value, string format) =>
            NeoNumberFormatting.FormatSingle((float)value, format, CultureInfo.InvariantCulture);

        private static string UnityInt32(double value, string? format) =>
            NeoNumberFormatting.FormatInt32(NeoInt32.Wrap(value), format, CultureInfo.InvariantCulture);

        /// <summary>A Unity float component's format: <paramref name="fallback"/> when none is given.</summary>
        private static string UnityFormat(string? format, string fallback) =>
            string.IsNullOrEmpty(format) ? fallback : format!;

        /// <summary>The canonical object slot in the cached merged class schema.</summary>
        private static string? RowOverride(object row, string name, Context ctx)
        {
            string? classId = row is NeoScriptObject pending ? pending.plan.classId : FindRowClassIdByReference(row, ctx);
            if (classId is null)
                return null;
            string canonical = name switch
            {
                "Equals" => NeoObjectContract.EqualsMember,
                "GetHashCode" => NeoObjectContract.GetHashCodeMember,
                "ToString" => NeoObjectContract.ToStringMember,
                _ => throw new System.ArgumentOutOfRangeException(nameof(name)),
            };
            string? memberId = ctx.client.ResolveClassNode(classId).SurfaceMember(name + "#" + canonical)?.memberId;
            return memberId == canonical ? null : memberId;
        }

        private static object? InvokeObjectOverride(string memberId, object receiver, object?[] args, Context ctx)
        {
            if (ctx.client.TryGetMember(memberId, out JsonMember? member) && member is NSFunctionMember)
                return NeoNSFunctionRuntime.InvokeImmediate(ctx.client, memberId, receiver, args, ctx);
            return InvokeNativeFunction(memberId, null, receiver, args, ctx);
        }

        /// <summary>
        /// A value's held type and payload: a box's own, a <c>Type</c>'s,
        /// or the closed static type of a raw value. Null for a row, or a
        /// raw value whose static type is a class, an interface or <c>object</c>.
        /// </summary>
        private static TypeBinding? HeldType(object value, TypeInfo? staticType, Context ctx, out object payload)
        {
            switch (value)
            {
                case NeoBox box:
                    payload = box.Payload ?? box;
                    return box.ValueType;
                case NeoTypeValue:
                    payload = value;
                    return TypeType;
            }
            payload = value;
            if (staticType is null)
                return value is string ? TypeBinding.Closed(new TypeHead(MemberKind.String, true)) : null;
            TypeInfo closed = CloseContextType(staticType, ctx);
            return IsReferenceStatic(closed) && value is not NeoVariantReference ? null : HeldTypeOf(closed);
        }

        /// <summary>§3.6 <c>GetType()</c>: a row's class with its bindings, a box's held type, a raw value's closed static type.</summary>
        private static TypeBinding RuntimeTypeOfValue(object value, TypeInfo? staticType, Context ctx)
        {
            if (staticType is null && value is string)
                return TypeBinding.Closed(new TypeHead(MemberKind.String, true));
            if (HeldType(value, staticType, ctx, out _) is { } held)
                return held;
            if (FindRowClassIdByReference(value, ctx) is not { } classId)
                throw new NSGetterRuntimeError("GetType() on a value read through an interface isn't supported yet (P127 B3).");
            if (!ctx.client.TryGetClass(classId, out NeoSchemaClass? schemaClass) || schemaClass.genericParams is not { Count: > 0 } parameters)
                return TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: classId));
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env =
                NeoNSFunctionRuntime.ResolveReceiverGenericEnv(ctx.client, value, ctx, "GetType()");
            var bindings = new Dictionary<string, TypeBinding>(parameters.Count, System.StringComparer.Ordinal);
            foreach (GenericParamDeclaration parameter in parameters)
                bindings[parameter.id] = NeoGeneratedTypesSupport.Erased(NeoTypeBindings.OfEntry(ctx.client, env[parameter.id], env));
            return TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: classId), bindings);
        }

        /// <summary>Whether a value of this static type is a row or passes through as one: class, interface or <c>object</c>.</summary>
        private static bool IsReferenceStatic(TypeInfo type) =>
            type.type is MemberKind.Class or MemberKind.Interface or MemberKind.Object;

        /// <summary>The held type a raw value of the closed static type boxes as: never nullable at the top.</summary>
        private static TypeBinding HeldTypeOf(TypeInfo closed) => RuntimeTypeOf(closed).WithRequired(true);

        /// <summary>
        /// A closed type's runtime type (§2): nullable references and tuple
        /// labels erased, a nullable value type's <c>?</c> kept.
        /// </summary>
        private static TypeBinding RuntimeTypeOf(TypeInfo closed)
        {
            if (NeoNSFunctionRuntime.ContainsGeneric(closed))
                throw new NSGetterRuntimeError("A runtime type over a class's type parameter outside its instance isn't supported yet (P127 B3).");
            return NeoGeneratedTypesSupport.Erased(NeoTypeBindings.FromTypeInfo(NeoTypeRules.Declared(closed)));
        }

        private static string TypeName(TypeBinding type, Context ctx) => NeoTypeNames.Of(type, ctx.client.ProjectDataForRuntime);

        private static string HeldName(object value, TypeBinding? held, Context ctx)
        {
            if (held is not null)
                return TypeName(held, ctx);
            if (FindRowClassIdByReference(value, ctx) is not null)
                return TypeName(RuntimeTypeOfValue(value, null, ctx), ctx);
            return "object";
        }

        private static NeoRuntimeError InvalidCast(string held, string target) =>
            new(NeoErrorClass.NeoInvalidCastError, $"Unable to cast object of type '{held}' to type '{target}'.");

        /// <summary>
        /// §6.1. An <c>is</c> pattern's binding: a box's payload for a
        /// value-kind or other non-reference type, the value itself otherwise.
        /// </summary>
        private static object? IsCheckBinding(object? value, TypeInfo checkType) =>
            value is NeoBox box
                && (!IsReferenceStatic(checkType) || checkType.type == MemberKind.Interface && !TypeBindings.IsValueType(box.ValueType.Head!))
                    ? box.Payload
                    : value;

        /// <summary>§6.1. An <c>is</c> test of a box, as a cast to <paramref name="checkType"/> would succeed.</summary>
        private static bool BoxMatches(NeoBox box, TypeInfo checkType, Context ctx) =>
            TryCast(box, box.ValueType, box.Payload ?? box, CloseContextType(checkType, ctx), ctx, out _);
    }
}
