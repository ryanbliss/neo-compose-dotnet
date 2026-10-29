// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime.NeoScript.Hashing
{
    /// <summary>The hashing library's failures, with .NET 10's messages.</summary>
    internal static class NeoHashingErrors
    {
        /// <summary>A negative <c>HashSet</c> or <c>Dictionary</c> capacity.</summary>
        internal static NeoRuntimeError CapacityRange() => new(
            NeoErrorClass.NeoArgumentOutOfRangeError,
            "Specified argument was out of the range of valid values. (Parameter 'capacity')");

        /// <summary><c>Dictionary.Add</c> of a key it already holds; <c>key</c> is the key's <c>ToString()</c>.</summary>
        internal static NeoRuntimeError DuplicateKey(string key) => new(
            NeoErrorClass.NeoArgumentError,
            "An item with the same key has already been added. Key: " + key);

        /// <summary><c>ValueTuple</c>'s <c>CompareTo(object?)</c> with another type; <c>type</c> is the receiver's type name.</summary>
        internal static NeoRuntimeError TupleIncorrectType(string type) => new(
            NeoErrorClass.NeoArgumentError,
            "Argument must be of type " + type + ". (Parameter 'other')");

        internal static NeoRuntimeError InvalidEnumFormat() => new(
            NeoErrorClass.NeoFormatError,
            "Format string can be only \"G\", \"g\", \"X\", \"x\", \"F\", \"f\", \"D\" or \"d\".");

        /// <summary><c>Enum.HasFlag</c> with a flag of another enum type.</summary>
        internal static NeoRuntimeError EnumFlagType(string flagType, string enumType) => new(
            NeoErrorClass.NeoArgumentError,
            "The argument type, '" + flagType + "', is not the same as the enum type '" + enumType + "'.");

        /// <summary><c>Enum.CompareTo(object?)</c> with a value of another type.</summary>
        internal static NeoRuntimeError EnumCompareType(string targetType, string enumType) => new(
            NeoErrorClass.NeoArgumentError,
            "Object must be the same type as the enum. The type passed in was '" + targetType + "'; the enum type was '" + enumType + "'.");

        /// <summary><c>bool.CompareTo(object?)</c> with a value of another type.</summary>
        internal static NeoRuntimeError MustBeBoolean() => new(
            NeoErrorClass.NeoArgumentError,
            "Object must be of type Boolean.");
    }
}
