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
    /// A runtime library failure with .NET's message. Until the §3.5 classes
    /// are minted, each throws as its nearest minted base class, as the
    /// compose twin's <c>neoErrorClassId</c> does.
    /// </summary>
    internal sealed class NeoRuntimeError : NSGetterRuntimeError
    {
        internal NeoRuntimeError(NeoErrorClass errorClass, string message)
            : base(message, ClassId(errorClass))
        {
            ErrorClass = errorClass;
        }

        internal NeoErrorClass ErrorClass
        {
            get;
        }

        private static string ClassId(NeoErrorClass errorClass) => errorClass switch
        {
            NeoErrorClass.NeoArithmeticError => NeoErrorContract.NeoArithmeticError,
            NeoErrorClass.NeoOverflowError => NeoErrorContract.NeoArithmeticError,
            NeoErrorClass.NeoDivideByZeroError => NeoErrorContract.NeoArithmeticError,
            NeoErrorClass.NeoInvalidOperationError => NeoErrorContract.NeoInvalidOperationError,
            NeoErrorClass.NeoFormatError => NeoErrorContract.NeoError,
            NeoErrorClass.NeoInvalidCastError => NeoErrorContract.NeoError,
            NeoErrorClass.NeoNotSupportedError => NeoErrorContract.NeoError,
            NeoErrorClass.NeoTypeInitializationError => NeoErrorContract.NeoError,
            _ => NeoErrorContract.NeoArgumentError,
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
