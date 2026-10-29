// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime.NeoScript.NumberMembers
{
    /// <summary>The number members' failures, with .NET 10's messages.</summary>
    internal static class NeoNumberMemberErrors
    {
        /// <summary><c>float.Sign(NaN)</c>'s <c>ArithmeticException</c>.</summary>
        internal static NeoRuntimeError NotANumber() => new(
            NeoErrorClass.NeoArithmeticError,
            "Function does not accept floating point Not-a-Number values.");

        /// <summary><c>Math.ThrowMinMaxException</c>, with both bounds formatted by <c>ToString()</c>.</summary>
        internal static NeoRuntimeError MinMax(string min, string max) => new(
            NeoErrorClass.NeoArgumentError,
            $"'{min}' cannot be greater than {max}.");

        /// <summary><c>Math.ThrowNegateTwosCompOverflow</c>.</summary>
        internal static NeoRuntimeError NegateMinValue() => new(
            NeoErrorClass.NeoOverflowError,
            "Negating the minimum value of a twos complement number is invalid.");

        /// <summary><c>ThrowHelper.ThrowNotSupportedException</c>, when both conversion hooks refuse.</summary>
        internal static NeoRuntimeError NotSupported() => new(
            NeoErrorClass.NeoNotSupportedError,
            "Specified method is not supported.");

        /// <summary><c>CompareTo(object)</c> given another type: <c>SR.Arg_MustBeInt32</c> and its siblings.</summary>
        internal static NeoRuntimeError MustBeType(NeoNumericKind kind) => new(
            NeoErrorClass.NeoArgumentError,
            $"Object must be of type {TypeName(kind)}.");

        private static string TypeName(NeoNumericKind kind) => kind switch
        {
            NeoNumericKind.Int => "Int32",
            NeoNumericKind.Float => "Single",
            _ => "Decimal",
        };
    }
}
