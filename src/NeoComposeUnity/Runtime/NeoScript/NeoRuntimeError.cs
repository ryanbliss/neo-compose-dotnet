// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// The NeoScript error classes P127 §3.5 adds, plus the P122 classes the
    /// runtime libraries throw.
    /// </summary>
    internal enum NeoErrorClass
    {
        NeoArgumentError,
        NeoInvalidOperationError,
        NeoOverflowError,
        NeoDivideByZeroError,
        NeoFormatError,
        NeoInvalidCastError,
        NeoNotSupportedError,
        NeoArgumentNullError,
        NeoArgumentOutOfRangeError,
        NeoCultureNotFoundError,
        NeoTypeInitializationError,
        NeoArithmeticError,
    }

    /// <summary>
    /// A runtime library failure with .NET's message and its canonical §3.5 class.
    /// </summary>
    internal sealed class NeoRuntimeError : NSGetterRuntimeError
    {
        internal NeoRuntimeError(NeoErrorClass errorClass, string message)
            : base(message, ClassId(errorClass))
        {
            ErrorClass = errorClass;
        }

        internal NeoRuntimeError(NeoErrorClass errorClass, string message, System.Exception cause)
            : base(message, ClassId(errorClass), cause)
        {
            ErrorClass = errorClass;
            Payload.InnerError = (cause as NSGetterRuntimeError)?.Payload;
        }

        internal NeoErrorClass ErrorClass
        {
            get;
        }

        private static string ClassId(NeoErrorClass errorClass) => errorClass switch
        {
            NeoErrorClass.NeoArgumentError => NeoErrorContract.NeoArgumentError,
            NeoErrorClass.NeoInvalidOperationError => NeoErrorContract.NeoInvalidOperationError,
            NeoErrorClass.NeoArithmeticError => NeoErrorContract.NeoArithmeticError,
            NeoErrorClass.NeoOverflowError => NeoErrorContract.NeoOverflowError,
            NeoErrorClass.NeoDivideByZeroError => NeoErrorContract.NeoDivideByZeroError,
            NeoErrorClass.NeoFormatError => NeoErrorContract.NeoFormatError,
            NeoErrorClass.NeoInvalidCastError => NeoErrorContract.NeoInvalidCastError,
            NeoErrorClass.NeoNotSupportedError => NeoErrorContract.NeoNotSupportedError,
            NeoErrorClass.NeoArgumentNullError => NeoErrorContract.NeoArgumentNullError,
            NeoErrorClass.NeoArgumentOutOfRangeError => NeoErrorContract.NeoArgumentOutOfRangeError,
            NeoErrorClass.NeoCultureNotFoundError => NeoErrorContract.NeoCultureNotFoundError,
            NeoErrorClass.NeoTypeInitializationError => NeoErrorContract.NeoTypeInitializationError,
            _ => throw new System.ArgumentOutOfRangeException(nameof(errorClass)),
        };

        internal static NeoRuntimeError ArgumentNull(string parameter) => new(
            NeoErrorClass.NeoArgumentNullError,
            $"Value cannot be null. (Parameter '{parameter}')");

        internal static NeoRuntimeError ReadOnly() => new(
            NeoErrorClass.NeoInvalidOperationError,
            "Instance is read-only.");

        internal static NeoRuntimeError BadFormatSpecifier() => new(
            NeoErrorClass.NeoFormatError,
            "Format specifier was invalid.");

        internal static NeoRuntimeError InvalidNumber(string s) => new(
            NeoErrorClass.NeoFormatError,
            $"The input string '{s}' was not in a correct format.");
    }
}
