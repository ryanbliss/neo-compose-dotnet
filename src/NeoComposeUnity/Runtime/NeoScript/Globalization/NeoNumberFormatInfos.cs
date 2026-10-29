// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace NeoCompose.Runtime.NeoScript.Globalization
{
    /// <summary>
    /// An authored NeoScript <c>IFormatProvider</c>. The evaluator implements
    /// <see cref="GetNumberFormat"/> by calling the authored
    /// <c>GetFormat(typeof(NumberFormatInfo))</c>.
    /// </summary>
    internal interface INeoAuthoredFormatProvider
    {
        object? GetNumberFormat();
    }

    /// <summary>
    /// NeoScript's <c>NumberFormatInfo</c> (P127 §11.2, §14 "Culture types").
    /// A NeoScript value is the CLR instance itself; this class holds what
    /// NeoScript does differently from calling the CLR member: setters check
    /// in .NET 10's order before assigning, the invariant singletons, provider
    /// resolution, and the retained <c>NativeDigits</c> list.
    /// </summary>
    internal static class NeoNumberFormatInfos
    {
        // .NET's lenient minus characters (NumberFormatInfo.InitializeInvariantAndNegativeSignFlags).
        private const string HyphenAliases = "‒⁻₋−➖﹣－";

        private static readonly ConditionalWeakTable<NumberFormatInfo, RetainedDigits> retainedDigits = new();

        /// <summary>
        /// <c>InvariantInfo</c> and <c>CurrentInfo</c>: the invariant culture's
        /// read-only number format, so the two and
        /// <c>InvariantCulture.NumberFormat</c> are one instance, as in .NET 10.
        /// </summary>
        internal static NumberFormatInfo InvariantInfo => CultureInfo.InvariantCulture.NumberFormat;

        internal static NumberFormatInfo CurrentInfo => InvariantInfo;

        /// <summary><c>NumberFormatInfo.GetInstance</c>, the provider resolution of §11.3.</summary>
        internal static NumberFormatInfo GetInstance(object? provider)
        {
            switch (provider)
            {
                case null:
                    return CurrentInfo;
                case CultureInfo culture:
                    return culture.NumberFormat;
                case NumberFormatInfo info:
                    return info;
                case INeoAuthoredFormatProvider authored:
                    return authored.GetNumberFormat() as NumberFormatInfo ?? CurrentInfo;
                case IFormatProvider clr:
                    return clr.GetFormat(typeof(NumberFormatInfo)) as NumberFormatInfo ?? CurrentInfo;
                default:
                    throw new ArgumentException($"'{provider.GetType().Name}' is not an IFormatProvider.", nameof(provider));
            }
        }

        /// <summary><c>NumberFormatInfo.ReadOnly</c>.</summary>
        internal static NumberFormatInfo ReadOnly(NumberFormatInfo? info)
        {
            if (info is null)
                throw NeoRuntimeError.ArgumentNull("nfi");
            NumberFormatInfo copy = NumberFormatInfo.ReadOnly(info);
            ShareRetainedDigits(info, copy);
            return copy;
        }

        /// <summary>A writable memberwise copy; it shares a retained <c>NativeDigits</c> list.</summary>
        internal static NumberFormatInfo Clone(NumberFormatInfo info)
        {
            var copy = (NumberFormatInfo)info.Clone();
            ShareRetainedDigits(info, copy);
            return copy;
        }

        /// <summary>Whether the parser may take its ASCII sign fast path.</summary>
        internal static bool HasInvariantNumberSigns(NumberFormatInfo info) =>
            info.PositiveSign == "+" && info.NegativeSign == "-";

        /// <summary>Whether the parser accepts <c>-</c> for a lenient minus negative sign.</summary>
        internal static bool AllowHyphenDuringParsing(NumberFormatInfo info)
        {
            string negativeSign = info.NegativeSign;
            return negativeSign.Length == 1 && HyphenAliases.IndexOf(negativeSign[0]) >= 0;
        }

        /// <summary>
        /// The <c>NativeDigits</c> getter: a copy of the list the last NeoScript
        /// write retained, while the CLR instance still holds what that write
        /// stored; otherwise a copy of the CLR's.
        /// </summary>
        internal static string?[] GetNativeDigits(NumberFormatInfo info)
        {
            string[] current = info.NativeDigits;
            if (retainedDigits.TryGetValue(info, out RetainedDigits? retained))
            {
                if (SameDigits(current, retained.stored))
                {
                    var copy = new string?[retained.list.Count];
                    for (int index = 0; index < copy.Length; index++)
                        copy[index] = retained.list[index] as string;
                    return copy;
                }
                retainedDigits.Remove(info);
            }
            return (string[])current.Clone();
        }

        /// <summary>
        /// A NeoScript property write. <paramref name="value"/> is an
        /// <see cref="int"/> for the digit, pattern and
        /// <c>DigitSubstitution</c> properties, a <see cref="string"/> for
        /// separators and symbols, a list of <see cref="int"/> for group
        /// sizes and a list of <see cref="string"/> for <c>NativeDigits</c>.
        /// The checks run in .NET 10's order before the CLR setter.
        /// </summary>
        internal static void Set(NumberFormatInfo info, string property, object? value)
        {
            switch (property)
            {
                case nameof(NumberFormatInfo.CurrencyDecimalDigits):
                    CheckRange((int)value!, 99, info);
                    Assign(() => info.CurrencyDecimalDigits = (int)value!);
                    return;
                case nameof(NumberFormatInfo.NumberDecimalDigits):
                    CheckRange((int)value!, 99, info);
                    Assign(() => info.NumberDecimalDigits = (int)value!);
                    return;
                case nameof(NumberFormatInfo.PercentDecimalDigits):
                    CheckRange((int)value!, 99, info);
                    Assign(() => info.PercentDecimalDigits = (int)value!);
                    return;
                case nameof(NumberFormatInfo.CurrencyNegativePattern):
                    CheckRange((int)value!, 16, info);
                    Assign(() => info.CurrencyNegativePattern = (int)value!);
                    return;
                case nameof(NumberFormatInfo.CurrencyPositivePattern):
                    CheckRange((int)value!, 3, info);
                    Assign(() => info.CurrencyPositivePattern = (int)value!);
                    return;
                case nameof(NumberFormatInfo.NumberNegativePattern):
                    CheckRange((int)value!, 4, info);
                    Assign(() => info.NumberNegativePattern = (int)value!);
                    return;
                case nameof(NumberFormatInfo.PercentNegativePattern):
                    CheckRange((int)value!, 11, info);
                    Assign(() => info.PercentNegativePattern = (int)value!);
                    return;
                case nameof(NumberFormatInfo.PercentPositivePattern):
                    CheckRange((int)value!, 3, info);
                    Assign(() => info.PercentPositivePattern = (int)value!);
                    return;
                case nameof(NumberFormatInfo.CurrencyGroupSizes):
                    {
                        int[] sizes = GroupSizes(info, value);
                        Assign(() => info.CurrencyGroupSizes = sizes);
                        return;
                    }
                case nameof(NumberFormatInfo.NumberGroupSizes):
                    {
                        int[] sizes = GroupSizes(info, value);
                        Assign(() => info.NumberGroupSizes = sizes);
                        return;
                    }
                case nameof(NumberFormatInfo.PercentGroupSizes):
                    {
                        int[] sizes = GroupSizes(info, value);
                        Assign(() => info.PercentGroupSizes = sizes);
                        return;
                    }
                case nameof(NumberFormatInfo.CurrencyDecimalSeparator):
                    CheckDecimalSeparator(info, value);
                    Assign(() => info.CurrencyDecimalSeparator = (string)value!);
                    return;
                case nameof(NumberFormatInfo.NumberDecimalSeparator):
                    CheckDecimalSeparator(info, value);
                    Assign(() => info.NumberDecimalSeparator = (string)value!);
                    return;
                case nameof(NumberFormatInfo.PercentDecimalSeparator):
                    CheckDecimalSeparator(info, value);
                    Assign(() => info.PercentDecimalSeparator = (string)value!);
                    return;
                case nameof(NumberFormatInfo.CurrencyGroupSeparator):
                    CheckGroupSeparator(info, value);
                    Assign(() => info.CurrencyGroupSeparator = (string)value!);
                    return;
                case nameof(NumberFormatInfo.NumberGroupSeparator):
                    CheckGroupSeparator(info, value);
                    Assign(() => info.NumberGroupSeparator = (string)value!);
                    return;
                case nameof(NumberFormatInfo.PercentGroupSeparator):
                    CheckGroupSeparator(info, value);
                    Assign(() => info.PercentGroupSeparator = (string)value!);
                    return;
                case nameof(NumberFormatInfo.CurrencySymbol):
                    CheckSymbol(info, value);
                    Assign(() => info.CurrencySymbol = (string)value!);
                    return;
                case nameof(NumberFormatInfo.NaNSymbol):
                    CheckSymbol(info, value);
                    Assign(() => info.NaNSymbol = (string)value!);
                    return;
                case nameof(NumberFormatInfo.NegativeInfinitySymbol):
                    CheckSymbol(info, value);
                    Assign(() => info.NegativeInfinitySymbol = (string)value!);
                    return;
                case nameof(NumberFormatInfo.PositiveInfinitySymbol):
                    CheckSymbol(info, value);
                    Assign(() => info.PositiveInfinitySymbol = (string)value!);
                    return;
                case nameof(NumberFormatInfo.NegativeSign):
                    CheckSymbol(info, value);
                    Assign(() => info.NegativeSign = (string)value!);
                    return;
                case nameof(NumberFormatInfo.PositiveSign):
                    CheckSymbol(info, value);
                    Assign(() => info.PositiveSign = (string)value!);
                    return;
                case nameof(NumberFormatInfo.PercentSymbol):
                    CheckSymbol(info, value);
                    Assign(() => info.PercentSymbol = (string)value!);
                    return;
                case nameof(NumberFormatInfo.PerMilleSymbol):
                    CheckSymbol(info, value);
                    Assign(() => info.PerMilleSymbol = (string)value!);
                    return;
                case nameof(NumberFormatInfo.NativeDigits):
                    SetNativeDigits(info, (IList?)value);
                    return;
                case nameof(NumberFormatInfo.DigitSubstitution):
                    {
                        CheckWritable(info);
                        int shape = (int)value!;
                        if (shape != (int)DigitShapes.Context && shape != (int)DigitShapes.None && shape != (int)DigitShapes.NativeNational)
                        {
                            throw new NeoRuntimeError(
                                NeoErrorClass.NeoArgumentError,
                                "The DigitSubstitution property must be of a valid member of the DigitShapes enumeration. Valid entries include Context, NativeNational or None. (Parameter 'value')");
                        }
                        Assign(() => info.DigitSubstitution = (DigitShapes)shape);
                        return;
                    }
                default:
                    throw new ArgumentException($"NumberFormatInfo has no settable property '{property}'.", nameof(property));
            }
        }

        private static void CheckWritable(NumberFormatInfo info)
        {
            if (info.IsReadOnly)
                throw NeoRuntimeError.ReadOnly();
        }

        private static void CheckRange(int value, int max, NumberFormatInfo info)
        {
            if (value < 0 || value > max)
            {
                throw new NeoRuntimeError(
                    NeoErrorClass.NeoArgumentOutOfRangeError,
                    $"Valid values are between 0 and {NeoNumberFormatting.Int32Text(max)}, inclusive. (Parameter 'value')\nActual value was {NeoNumberFormatting.Int32Text(value)}.");
            }
            CheckWritable(info);
        }

        private static int[] GroupSizes(NumberFormatInfo info, object? value)
        {
            if (value is null)
                throw NeoRuntimeError.ArgumentNull("value");
            CheckWritable(info);
            var list = (IList)value;
            var sizes = new int[list.Count];
            for (int index = 0; index < sizes.Length; index++)
                sizes[index] = (int)list[index]!;
            for (int index = 0; index < sizes.Length; index++)
            {
                int size = sizes[index];
                if (size == 0 && index == sizes.Length - 1)
                    break;
                if (size < 1 || size > 9)
                {
                    throw new NeoRuntimeError(
                        NeoErrorClass.NeoArgumentError,
                        "Every element in the value array should be between one and nine, except for the last element, which can be zero. (Parameter 'value')");
                }
            }
            return sizes;
        }

        private static void CheckDecimalSeparator(NumberFormatInfo info, object? value)
        {
            CheckWritable(info);
            if (value is null)
                throw NeoRuntimeError.ArgumentNull("value");
            if (((string)value).Length == 0)
            {
                throw new NeoRuntimeError(
                    NeoErrorClass.NeoArgumentError,
                    "The value cannot be an empty string. (Parameter 'value')");
            }
        }

        private static void CheckGroupSeparator(NumberFormatInfo info, object? value)
        {
            CheckWritable(info);
            if (value is null)
                throw NeoRuntimeError.ArgumentNull("value");
        }

        private static void CheckSymbol(NumberFormatInfo info, object? value)
        {
            if (value is null)
                throw NeoRuntimeError.ArgumentNull("value");
            CheckWritable(info);
        }

        /// <summary>Keeps the caller's list, as .NET keeps the array.</summary>
        private static void SetNativeDigits(NumberFormatInfo info, IList? digits)
        {
            CheckWritable(info);
            if (digits is null)
                throw NeoRuntimeError.ArgumentNull("nativeDig");
            if (digits.Count != 10)
            {
                throw new NeoRuntimeError(
                    NeoErrorClass.NeoArgumentError,
                    "The NativeDigits array must contain exactly ten members. (Parameter 'value')");
            }
            var stored = new string[10];
            for (int index = 0; index < 10; index++)
            {
                if (digits[index] is not string digit)
                {
                    throw new NeoRuntimeError(
                        NeoErrorClass.NeoArgumentNullError,
                        "Found a null value within an array. (Parameter 'value')");
                }
                if (!IsNativeDigit(digit, index))
                {
                    throw new NeoRuntimeError(
                        NeoErrorClass.NeoArgumentError,
                        "Each member of the NativeDigits array must be a single text element (one or more UTF-16 code points) with a Unicode Nd (Number, Decimal Digit) property indicating it is a digit. (Parameter 'value')");
                }
                stored[index] = digit;
            }
            Assign(() => info.NativeDigits = stored);
            retainedDigits.Remove(info);
            retainedDigits.Add(info, new RetainedDigits(digits, stored));
        }

        private static bool IsNativeDigit(string digit, int index)
        {
            if (digit.Length != 1 && digit.Length != 2)
                return false;
            int codePoint = digit[0];
            if (digit.Length == 2)
            {
                if (!char.IsSurrogatePair(digit[0], digit[1]))
                    return false;
                codePoint = char.ConvertToUtf32(digit[0], digit[1]);
            }
            return NeoUnicode.DecimalDigitValue(codePoint) == index || NeoUnicode.IsPrivateUse(codePoint);
        }

        /// <summary>Gives a memberwise copy of <paramref name="source"/> the list it retained.</summary>
        internal static void ShareRetainedDigits(NumberFormatInfo source, NumberFormatInfo copy)
        {
            if (ReferenceEquals(source, copy))
                return;
            if (retainedDigits.TryGetValue(source, out RetainedDigits? retained)
                && SameDigits(source.NativeDigits, retained.stored))
            {
                retainedDigits.Remove(copy);
                retainedDigits.Add(copy, retained);
            }
        }

        private static bool SameDigits(string[] current, string[] stored)
        {
            if (current.Length != stored.Length)
                return false;
            for (int index = 0; index < current.Length; index++)
            {
                if (!string.Equals(current[index], stored[index], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Runs the CLR setter once NeoScript's checks passed. Unity's Mono
        /// can still refuse (it caps <c>CurrencyNegativePattern</c> at 15,
        /// §14); its exception becomes the matching NeoScript error with
        /// Mono's message.
        /// </summary>
        private static void Assign(Action assign)
        {
            try
            {
                assign();
            }
            catch (ArgumentOutOfRangeException error)
            {
                throw new NeoRuntimeError(NeoErrorClass.NeoArgumentOutOfRangeError, error.Message);
            }
            catch (ArgumentNullException error)
            {
                throw new NeoRuntimeError(NeoErrorClass.NeoArgumentNullError, error.Message);
            }
            catch (ArgumentException error)
            {
                throw new NeoRuntimeError(NeoErrorClass.NeoArgumentError, error.Message);
            }
            catch (InvalidOperationException error)
            {
                throw new NeoRuntimeError(NeoErrorClass.NeoInvalidOperationError, error.Message);
            }
        }

        /// <summary>The list a NeoScript write retained, and the copy the CLR instance stored.</summary>
        private sealed class RetainedDigits
        {
            internal readonly IList list;
            internal readonly string[] stored;

            internal RetainedDigits(IList list, string[] stored)
            {
                this.list = list;
                this.stored = stored;
            }
        }
    }
}
