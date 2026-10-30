// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript.Globalization;
using NeoCompose.Runtime.NeoScript.Hashing;
using NeoCompose.Runtime.NeoScript.NumberMembers;
using NeoCompose.Runtime.NeoScript.Numerics;

namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {
        internal static bool TryPrimitiveInterfaceCall(CallFunctionPointer pointer, object? receiver, NeoScriptScope scope,
            Context ctx, out object? result)
        {
            result = null;
            if (pointer.interfaceMember is null || receiver is null)
                return false;
            TypeBinding type = InterfaceReceiverType(receiver, ctx);
            if (NeoPrimitiveConformance.Kind(type) is null)
                return false;
            var target = ResolveInterfaceCall(pointer, receiver, ctx);
            if (target.PrimitiveMember is null)
                return false;
            var args = new object?[pointer.args.Length];
            for (int i = 0; i < args.Length; i++)
                args[i] = EvalPointer(pointer.args[i], scope, ctx);
            TypeInfo[]? typeArguments = null;
            if (pointer.typeArguments is not null)
            {
                typeArguments = new TypeInfo[pointer.typeArguments.Length];
                for (int i = 0; i < typeArguments.Length; i++)
                    typeArguments[i] = CloseContextType(pointer.typeArguments[i], ctx);
            }
            result = InvokePrimitiveImplementation(target, receiver, args, ctx, typeArguments);
            return true;
        }

        private static object? InvokePrimitiveImplementation(NeoInterfaceDispatch.Implementation target, object? receiver,
            object?[] args, Context ctx, TypeInfo[]? typeArguments = null) =>
            InvokePrimitiveMember(NeoTypeBindings.ToTypeInfo(target.PrimitiveType!), target.PrimitiveMember!, receiver,
                NormalizePrimitiveArguments(target.PrimitiveType!, target.PrimitiveMember!, args), ctx, typeArguments);

        private static object?[] NormalizePrimitiveArguments(TypeBinding type, string member, object?[] args)
        {
            int style = type.Head!.Type switch
            {
                MemberKind.Int => 7,
                MemberKind.Float => 231,
                _ => 111
            };
            if (member == "Parse" && args.Length == 2)
                return new object?[] { args[0], style, args[1] };
            if (member == "TryParse" && args.Length == 3)
                return new object?[] { args[0], style, args[1], args[2] };
            return args;
        }

        private static object? InvokePrimitiveDelegate(NeoBuiltInTarget target, object?[] args, Context ctx)
        {
            var selected = NeoPrimitiveBuiltIns.Of(ctx.client).Resolve(ctx.client, target);
            var typeArguments = target.TypeArguments?.Select(NeoTypeBindings.ToTypeInfo).ToArray();
            if (selected.PrimitiveMember is not null)
                return InvokePrimitiveImplementation(selected, null, args, ctx, typeArguments);
            var call = selected.CallTarget ??= new CallSiteTarget(ctx.client, null, selected.MemberId, null);
            return NeoNSFunctionRuntime.InvokeImmediate(ctx.client, call.function!, null, args, ctx,
                boundStaticConstruction: selected.StaticConstruction, boundInterfaceConstruction: selected.InterfaceConstruction,
                typeArguments: typeArguments);
        }

        private static readonly Dictionary<string, NeoNumberPredicate> NumberPredicates =
            System.Enum.GetValues(typeof(NeoNumberPredicate)).Cast<NeoNumberPredicate>().ToDictionary(value => value.ToString());

        private static object? EvalPrimitiveMember(PrimitiveMemberPointer pointer, NeoScriptScope scope, Context ctx)
        {
            object? receiver = pointer.receiver is null ? null : EvalPointer(pointer.receiver, scope, ctx);
            var args = new object?[pointer.args.Length];
            for (int i = 0; i < args.Length; i++)
                args[i] = EvalPointer(pointer.args[i], scope, ctx);
            if (pointer.receiver is not null && receiver is null)
                throw new NSGetterRuntimeError(NullReferenceMessage, NeoErrorContract.NeoNullReferenceError);
            return InvokePrimitiveMember(CloseContextType(pointer.primitive, ctx), pointer.member, receiver, args, ctx);
        }

        private static object? InvokePrimitiveMember(TypeInfo type, string member, object? receiver, object?[] args,
            Context ctx, TypeInfo[]? typeArguments = null)
        {
            TypeBinding held = RuntimeTypeOf(type);
            if (receiver is NeoBox box)
                receiver = box.Payload;
            if (member is "StructuralEquals" or "StructuralGetHashCode" or "StructuralCompareTo")
                return InvokeTupleStructural(member, (RuntimeTuple)receiver!, held, args, ctx);
            if (member == "GetFormat")
            {
                if (args[0] is not NeoTypeValue formatType || !formatType.Binding.Equals(TypeBinding.Closed(new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.NumberFormatInfo))))
                    return null;
                return receiver switch
                {
                    CultureInfo culture => culture.NumberFormat,
                    NumberFormatInfo format => format,
                    _ => throw new NeoScriptPreExecutionValidationError("GetFormat has no format provider receiver."),
                };
            }
            if (member == "Parse" || member == "TryParse")
                return PrimitiveParse(type.type, member == "TryParse", args, ctx);
            if (member == "EqualsObject")
                return receiver is not null && ObjectEquals(receiver, type, args[0], ctx);
            if (member == "Equals")
                return PrimitiveEquals(receiver, args[0], held, ctx);
            if (member == "GetHashCode")
                return ObjectHash(receiver!, type, ctx);
            if (member == "CompareTo" || member == "CompareToObject")
                return PrimitiveCompare(receiver!, args[0], held, member == "CompareToObject", ctx);
            if (member == "ToString")
                return PrimitiveText(receiver!, held, args, ctx);
            if (member == "TrueString")
                return "True";
            if (member == "FalseString")
                return "False";
            if (type.type == MemberKind.String)
            {
                return member switch
                {
                    "StaticEquals" => string.Equals((string?)args[0], (string?)args[1], StringComparison.Ordinal),
                    "Compare" => NeoStrings.Compare((string?)args[0], (string?)args[1]),
                    "CompareOrdinal" => NeoStrings.CompareOrdinal((string?)args[0], (string?)args[1]),
                    _ => throw UnknownPrimitiveMember(type, member),
                };
            }
            if (member.StartsWith("TryConvert", StringComparison.Ordinal))
                return PrimitiveConversion(type, member, args, typeArguments ?? ctx.functionTypeArguments, ctx);
            if (NumberPredicates.TryGetValue(member, out var predicate))
            {
                return type.type switch
                {
                    MemberKind.Int => NeoInt32Members.Is(predicate, NeoInt32.Wrap(Number(args[0]!))),
                    MemberKind.Float => NeoSingleMembers.Is(predicate, (float)Number(args[0]!)),
                    MemberKind.Decimal => NeoDecimalMembers.Is(predicate, (string)args[0]!),
                    _ => throw UnknownPrimitiveMember(type, member),
                };
            }
            if (type.type == MemberKind.Int)
                return IntMember(member, args);
            if (type.type == MemberKind.Float)
                return SingleMember(member, args);
            if (type.type == MemberKind.Decimal)
                return InvokeDecimalMember(member, args);
            if (member == "HasFlag" && type.type == MemberKind.Enum)
            {
                object? flag = args[0];
                if (flag is null)
                    throw NeoRuntimeError.ArgumentNull("flag");
                TypeBinding flagType = flag is NeoBox flagBox ? flagBox.ValueType : held;
                object flagValue = flag is NeoBox boxed ? boxed.Payload! : flag;
                return NeoEnums.HasFlag(EnumNumber(receiver!, held.Head!.EnumId!, ctx), TypeName(held, ctx),
                    new NeoEnumArgument(held.Equals(flagType), TypeName(flagType, ctx),
                        held.Equals(flagType) ? EnumNumber(flagValue, held.Head.EnumId!, ctx) : 0));
            }
            throw UnknownPrimitiveMember(type, member);
        }

        private static NSGetterRuntimeError UnknownPrimitiveMember(TypeInfo type, string member) =>
            new($"Primitive '{type.type}' has no intrinsic member '{member}'.");

        private static object IntMember(string member, object?[] args)
        {
            int A(int index) => NeoInt32.Wrap(Number(args[index]!));
            return member switch
            {
                "Zero" or "AdditiveIdentity" => 0,
                "One" or "MultiplicativeIdentity" => 1,
                "NegativeOne" => -1,
                "Radix" => 2,
                "MinValue" => int.MinValue,
                "MaxValue" => int.MaxValue,
                "Abs" => NeoInt32.Abs(A(0)),
                "Sign" => NeoInt32Members.Sign(A(0)),
                "CopySign" => NeoInt32Members.CopySign(A(0), A(1)),
                "Clamp" => NeoInt32Members.Clamp(A(0), A(1), A(2)),
                "ClampNative" => NeoInt32Members.ClampNative(A(0), A(1), A(2)),
                "Max" or "MaxNumber" => NeoInt32Members.Max(A(0), A(1)),
                "Min" or "MinNumber" => NeoInt32Members.Min(A(0), A(1)),
                "MaxNative" => NeoInt32Members.MaxNative(A(0), A(1)),
                "MinNative" => NeoInt32Members.MinNative(A(0), A(1)),
                "MaxMagnitude" or "MaxMagnitudeNumber" => NeoInt32Members.MaxMagnitude(A(0), A(1)),
                "MinMagnitude" or "MinMagnitudeNumber" => NeoInt32Members.MinMagnitude(A(0), A(1)),
                "MultiplyAddEstimate" => NeoInt32Members.MultiplyAddEstimate(A(0), A(1), A(2)),
                "op_Addition" => NeoInt32.Add(A(0), A(1), false),
                "op_CheckedAddition" => NeoInt32.Add(A(0), A(1), true),
                "op_Subtraction" => NeoInt32.Subtract(A(0), A(1), false),
                "op_CheckedSubtraction" => NeoInt32.Subtract(A(0), A(1), true),
                "op_Multiply" => NeoInt32.Multiply(A(0), A(1), false),
                "op_CheckedMultiply" => NeoInt32.Multiply(A(0), A(1), true),
                "op_Division" or "op_CheckedDivision" => NeoInt32.Divide(A(0), A(1)),
                "op_Modulus" => NeoInt32.Remainder(A(0), A(1)),
                "op_UnaryPlus" => A(0),
                "op_UnaryNegation" => NeoInt32.Negate(A(0), false),
                "op_CheckedUnaryNegation" => NeoInt32.Negate(A(0), true),
                "op_Increment" => NeoInt32.Increment(A(0), false),
                "op_CheckedIncrement" => NeoInt32.Increment(A(0), true),
                "op_Decrement" => NeoInt32.Decrement(A(0), false),
                "op_CheckedDecrement" => NeoInt32.Decrement(A(0), true),
                "op_BitwiseAnd" => A(0) & A(1),
                "op_BitwiseOr" => A(0) | A(1),
                "op_ExclusiveOr" => A(0) ^ A(1),
                "op_OnesComplement" => ~A(0),
                "op_LeftShift" => NeoInt32.ShiftLeft(A(0), A(1)),
                "op_RightShift" => NeoInt32.ShiftRight(A(0), A(1)),
                "op_UnsignedRightShift" => NeoInt32.UnsignedShiftRight(A(0), A(1)),
                "op_Equality" => A(0) == A(1),
                "op_Inequality" => A(0) != A(1),
                "op_LessThan" => A(0) < A(1),
                "op_GreaterThan" => A(0) > A(1),
                "op_LessThanOrEqual" => A(0) <= A(1),
                "op_GreaterThanOrEqual" => A(0) >= A(1),
                _ => throw new NSGetterRuntimeError($"Unknown int intrinsic '{member}'."),
            };
        }

        private static object SingleMember(string member, object?[] args)
        {
            float A(int index) => (float)Number(args[index]!);
            object result = member switch
            {
                "Zero" or "AdditiveIdentity" => 0f,
                "One" or "MultiplicativeIdentity" => 1f,
                "NegativeOne" => -1f,
                "Radix" => 2,
                "MinValue" => float.MinValue,
                "MaxValue" => float.MaxValue,
                "Abs" => NeoSingleMembers.Abs(A(0)),
                "Sign" => NeoSingleMembers.Sign(A(0)),
                "CopySign" => NeoSingleMembers.CopySign(A(0), A(1)),
                "Clamp" => NeoSingleMembers.Clamp(A(0), A(1), A(2)),
                "ClampNative" => NeoSingleMembers.ClampNative(A(0), A(1), A(2)),
                "Max" => NeoSingleMembers.Max(A(0), A(1)),
                "Min" => NeoSingleMembers.Min(A(0), A(1)),
                "MaxNumber" => NeoSingleMembers.MaxNumber(A(0), A(1)),
                "MinNumber" => NeoSingleMembers.MinNumber(A(0), A(1)),
                "MaxNative" => NeoSingleMembers.MaxNative(A(0), A(1)),
                "MinNative" => NeoSingleMembers.MinNative(A(0), A(1)),
                "MaxMagnitude" => NeoSingleMembers.MaxMagnitude(A(0), A(1)),
                "MinMagnitude" => NeoSingleMembers.MinMagnitude(A(0), A(1)),
                "MaxMagnitudeNumber" => NeoSingleMembers.MaxMagnitudeNumber(A(0), A(1)),
                "MinMagnitudeNumber" => NeoSingleMembers.MinMagnitudeNumber(A(0), A(1)),
                "MultiplyAddEstimate" => NeoSingleMembers.MultiplyAddEstimate(A(0), A(1), A(2)),
                "op_Addition" or "op_CheckedAddition" => A(0) + A(1),
                "op_Subtraction" or "op_CheckedSubtraction" => A(0) - A(1),
                "op_Multiply" or "op_CheckedMultiply" => A(0) * A(1),
                "op_Division" or "op_CheckedDivision" => A(0) / A(1),
                "op_Modulus" => A(0) % A(1),
                "op_UnaryPlus" => A(0),
                "op_UnaryNegation" or "op_CheckedUnaryNegation" => -A(0),
                "op_Increment" or "op_CheckedIncrement" => A(0) + 1f,
                "op_Decrement" or "op_CheckedDecrement" => A(0) - 1f,
                "op_BitwiseAnd" => NeoSingleMembers.And(A(0), A(1)),
                "op_BitwiseOr" => NeoSingleMembers.Or(A(0), A(1)),
                "op_ExclusiveOr" => NeoSingleMembers.Xor(A(0), A(1)),
                "op_OnesComplement" => NeoSingleMembers.Not(A(0)),
                "op_Equality" => A(0) == A(1),
                "op_Inequality" => A(0) != A(1),
                "op_LessThan" => A(0) < A(1),
                "op_GreaterThan" => A(0) > A(1),
                "op_LessThanOrEqual" => A(0) <= A(1),
                "op_GreaterThanOrEqual" => A(0) >= A(1),
                _ => throw new NSGetterRuntimeError($"Unknown float intrinsic '{member}'."),
            };
            return result is float value && float.IsNaN(value) ? float.NaN : result;
        }

        private static object InvokeDecimalMember(string member, object?[] args)
        {
            string A(int index) => (string)args[index]!;
            return member switch
            {
                "Zero" or "AdditiveIdentity" => "0",
                "One" or "MultiplicativeIdentity" => "1",
                "NegativeOne" => "-1",
                "Radix" => 10,
                "MinValue" => NeoDecimalMembers.MinValue,
                "MaxValue" => NeoDecimalMembers.MaxValue,
                "Abs" => NeoDecimalMembers.Abs(A(0)),
                "Sign" => NeoDecimalMembers.Sign(A(0)),
                "CopySign" => NeoDecimalMembers.CopySign(A(0), A(1)),
                "Clamp" => NeoDecimalMembers.Clamp(A(0), A(1), A(2)),
                "ClampNative" => NeoDecimalMembers.ClampNative(A(0), A(1), A(2)),
                "Max" or "MaxNumber" => NeoDecimalMembers.Max(A(0), A(1)),
                "Min" or "MinNumber" => NeoDecimalMembers.Min(A(0), A(1)),
                "MaxNative" => NeoDecimalMembers.MaxNative(A(0), A(1)),
                "MinNative" => NeoDecimalMembers.MinNative(A(0), A(1)),
                "MaxMagnitude" or "MaxMagnitudeNumber" => NeoDecimalMembers.MaxMagnitude(A(0), A(1)),
                "MinMagnitude" or "MinMagnitudeNumber" => NeoDecimalMembers.MinMagnitude(A(0), A(1)),
                "MultiplyAddEstimate" => NeoDecimalMath.MultiplyAddEstimate(A(0), A(1), A(2)),
                "Add" or "op_Addition" or "op_CheckedAddition" => NeoDecimalMath.Add(A(0), A(1)),
                "Subtract" or "op_Subtraction" or "op_CheckedSubtraction" => NeoDecimalMath.Subtract(A(0), A(1)),
                "Multiply" or "op_Multiply" or "op_CheckedMultiply" => NeoDecimalMath.Multiply(A(0), A(1)),
                "Divide" or "op_Division" or "op_CheckedDivision" => NeoDecimalMath.Divide(A(0), A(1)),
                "Remainder" or "op_Modulus" => NeoDecimalMath.Remainder(A(0), A(1)),
                "op_UnaryPlus" => A(0),
                "Negate" or "op_UnaryNegation" or "op_CheckedUnaryNegation" => NeoDecimalMembers.Negate(A(0)),
                "op_Increment" or "op_CheckedIncrement" => NeoDecimalMath.Add(A(0), "1"),
                "op_Decrement" or "op_CheckedDecrement" => NeoDecimalMath.Subtract(A(0), "1"),
                "StaticEquals" or "op_Equality" => NeoDecimalMath.Compare(A(0), A(1)) == 0,
                "op_Inequality" => NeoDecimalMath.Compare(A(0), A(1)) != 0,
                "op_LessThan" => NeoDecimalMath.Compare(A(0), A(1)) < 0,
                "op_GreaterThan" => NeoDecimalMath.Compare(A(0), A(1)) > 0,
                "op_LessThanOrEqual" => NeoDecimalMath.Compare(A(0), A(1)) <= 0,
                "op_GreaterThanOrEqual" => NeoDecimalMath.Compare(A(0), A(1)) >= 0,
                "Compare" => NeoDecimalMath.Compare(A(0), A(1)),
                "ToInt32" => NeoNumericConversions.DecimalToInt32(A(0)),
                "ToSingle" => NeoNumericConversions.DecimalToSingle(A(0)),
                _ => throw new NSGetterRuntimeError($"Unknown decimal intrinsic '{member}'."),
            };
        }

        private static NeoNumericKind? NumericKind(MemberKind kind) => kind switch
        {
            MemberKind.Int => NeoNumericKind.Int,
            MemberKind.Float => NeoNumericKind.Float,
            MemberKind.Decimal => NeoNumericKind.Decimal,
            _ => null,
        };

        private static object NumericKernelValue(object value, NeoNumericKind? kind) => kind switch
        {
            NeoNumericKind.Int => NeoInt32.Wrap(Number(value)),
            NeoNumericKind.Float => (float)Number(value),
            _ => value,
        };

        private static object PrimitiveConversion(TypeInfo self, string member, object?[] args, TypeInfo[]? typeArguments, Context ctx)
        {
            if (typeArguments is not { Length: 1 })
                throw new NSGetterRuntimeError("A numeric conversion hook requires one closed type argument.");
            TypeInfo other = CloseContextType(typeArguments[0], ctx);
            var selfKind = NumericKind(self.type)!.Value;
            var otherKind = NumericKind(other.type);
            NeoCreateMode mode = member.EndsWith("Checked", StringComparison.Ordinal) ? NeoCreateMode.Checked
                : member.EndsWith("Saturating", StringComparison.Ordinal) ? NeoCreateMode.Saturating : NeoCreateMode.Truncating;
            bool from = member.StartsWith("TryConvertFrom", StringComparison.Ordinal);
            object value = NumericKernelValue(args[0]!, from ? otherKind : selfKind);
            object? result;
            bool converted = from ? NeoNumberConversionMembers.TryConvertFrom(mode, selfKind, otherKind, value, out result)
                : NeoNumberConversionMembers.TryConvertTo(mode, selfKind, otherKind, value, out result);
            ((NeoOutReference)args[1]!).Write(converted ? result : DefaultOf(from ? self : other, ctx.client));
            return converted;
        }

        private static object? PrimitiveParse(MemberKind kind, bool tryParse, object?[] args, Context ctx)
        {
            string? text = (string?)args[0];
            bool numeric = kind is MemberKind.Int or MemberKind.Float or MemberKind.Decimal;
            int? style = !numeric || args[1] is null ? null : NeoInt32.Wrap(Number(args[1]!));
            object? provider = numeric ? PrimitiveFormatProvider(args[2], ctx) : null;
            object? result;
            bool success = true;
            switch (kind)
            {
                case MemberKind.Int:
                    if (tryParse)
                    {
                        success = NeoNumberParsing.TryParseInt32(text, style, provider, out int integer);
                        result = integer;
                    }
                    else
                        result = NeoNumberParsing.ParseInt32(text, style, provider);
                    break;
                case MemberKind.Float:
                    if (tryParse)
                    {
                        success = NeoNumberParsing.TryParseSingle(text, style, provider, out float single);
                        result = single;
                    }
                    else
                        result = NeoNumberParsing.ParseSingle(text, style, provider);
                    break;
                case MemberKind.Decimal:
                    decimal number;
                    if (tryParse)
                        success = NeoNumberParsing.TryParseDecimal(text, style, provider, out number);
                    else
                        number = NeoNumberParsing.ParseDecimal(text, style, provider);
                    result = DecimalText(number);
                    break;
                case MemberKind.Bool:
                    if (tryParse)
                    {
                        success = NeoStrings.TryParseBoolean(text, out bool boolean);
                        result = boolean;
                    }
                    else
                        result = NeoStrings.ParseBoolean(text);
                    break;
                case MemberKind.String:
                    if (tryParse)
                    {
                        success = NeoStrings.TryParseString(text, out string? value);
                        result = value;
                    }
                    else
                        result = NeoStrings.ParseString(text);
                    break;
                default:
                    throw new NSGetterRuntimeError($"Cannot parse primitive '{kind}'.");
            }
            if (!tryParse)
                return result;
            ((NeoOutReference)args[args.Length - 1]!).Write(result);
            return success;
        }

        private static string DecimalText(decimal value)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            return value == 0m && decimal.GetBits(value)[3] < 0 ? "-" + text : text;
        }

        private static bool PrimitiveEquals(object? left, object? right, TypeBinding type, Context ctx)
        {
            if (left is null || right is null)
                return left is null && right is null;
            return PayloadEquals(type, left, right, ctx);
        }

        private static int PrimitiveCompare(object receiver, object? other, TypeBinding type, bool objectArgument, Context ctx)
        {
            if (other is null)
                return 1;
            if (objectArgument)
            {
                TypeBinding? otherType = HeldType(other, null, ctx, out object otherPayload);
                if (otherType is null || !otherType.Equals(type))
                {
                    if (type.Head!.Type == MemberKind.Tuple)
                        throw NeoHashingErrors.TupleIncorrectType(ClrTypeName(type, ctx));
                    if (type.Head!.Type == MemberKind.Enum)
                        throw NeoHashingErrors.EnumCompareType(HeldName(other, HeldType(other, null, ctx, out _), ctx), TypeName(type, ctx));
                    string label = type.Head!.Type switch
                    {
                        MemberKind.Int => "Int32",
                        MemberKind.Float => "Single",
                        MemberKind.Decimal => "Decimal",
                        MemberKind.Bool => "Boolean",
                        MemberKind.String => "String",
                        _ => TypeName(type, ctx),
                    };
                    throw new NeoRuntimeError(NeoErrorClass.NeoArgumentError, $"Object must be of type {label}.");
                }
                other = otherPayload;
            }
            return type.Head!.Type switch
            {
                MemberKind.Int => NeoInt32Members.CompareTo(NeoInt32.Wrap(Number(receiver)), NeoInt32.Wrap(Number(other!))),
                MemberKind.Float => NeoSingleMembers.CompareTo((float)Number(receiver), (float)Number(other!)),
                MemberKind.Decimal => NeoDecimalMembers.CompareTo((string)receiver, (string)other!),
                MemberKind.Bool => NeoEnums.BoolCompareTo((bool)receiver, (bool)other!),
                MemberKind.String => NeoStrings.Compare((string)receiver, (string?)other),
                MemberKind.Tuple => NeoTuples.TupleCompareTo((RuntimeTuple)receiver, (RuntimeTuple)other!,
                    (index, a, b) => DefaultTupleElementCompare(type.At(index), a, b, ctx)),
                MemberKind.Enum => NeoEnums.CompareTo(EnumNumber(receiver, type.Head.EnumId!, ctx), EnumNumber(other!, type.Head.EnumId!, ctx)),
                _ => throw new NSGetterRuntimeError($"CompareTo on '{TypeName(type, ctx)}' is not implemented."),
            };
        }

        private static string PrimitiveText(object receiver, TypeBinding type, object?[] args, Context ctx)
        {
            string? format = args.Length > 0 ? (string?)args[0] : null;
            object? provider = PrimitiveFormatProvider(args.Length > 1 ? args[1] : null, ctx);
            return type.Head!.Type switch
            {
                MemberKind.Int => NeoNumberFormatting.FormatInt32(NeoInt32.Wrap(Number(receiver)), format, provider),
                MemberKind.Float => NeoNumberFormatting.FormatSingle((float)Number(receiver), format, provider),
                MemberKind.Decimal => NeoNumberFormatting.FormatDecimal(decimal.Parse((string)receiver, NumberStyles.Float, CultureInfo.InvariantCulture), format, provider),
                _ => FormattableText(receiver, type, format, ctx) ?? PayloadText(receiver, type, ctx),
            };
        }

        private static object? PrimitiveFormatProvider(object? provider, Context ctx)
        {
            if (provider is NeoBox box)
                provider = box.Payload;
            return provider is null or IFormatProvider or INeoAuthoredFormatProvider
                ? provider : new AuthoredPrimitiveFormatProvider(provider, ctx);
        }

        private sealed class AuthoredPrimitiveFormatProvider : INeoAuthoredFormatProvider
        {
            private readonly object receiver;
            private readonly Context context;

            internal AuthoredPrimitiveFormatProvider(object receiver, Context context)
            {
                this.receiver = receiver;
                this.context = context;
            }

            public object? GetNumberFormat()
            {
                string id = NeoPrimitiveConformance.InterfaceId("IFormatProvider");
                TypeBinding construction = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: id));
                string memberId = context.client.ProjectDataForRuntime.interfaces[id].members["GetFormat"].id!;
                object? value = InvokeMappedInterface(receiver, construction, memberId,
                    new object?[] { NeoTypeValue.Of(TypeBinding.Closed(new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.NumberFormatInfo))) }, context);
                return value is NeoBox box ? box.Payload : value;
            }
        }

        private static object? InvokeMappedInterface(object receiver, TypeBinding construction, string memberId, object?[] args, Context ctx)
        {
            var selected = NeoInterfaceDispatch.Of(ctx.client).Resolve(RuntimeTypeOfValue(receiver, null, ctx), construction, memberId, "call");
            if (selected.PrimitiveMember is not null)
                return InvokePrimitiveImplementation(selected, receiver, args, ctx);
            var target = selected.CallTarget ??= new CallSiteTarget(ctx.client, null, selected.MemberId, null);
            if (target.function is { } function)
                return NeoNSFunctionRuntime.InvokeImmediate(ctx.client, function, receiver, args, ctx,
                    boundStaticConstruction: selected.StaticConstruction, boundInterfaceConstruction: selected.InterfaceConstruction);
            return InvokeNativeFunction(target.memberId, target.nativeFunction, receiver, args, ctx);
        }
    }
}
