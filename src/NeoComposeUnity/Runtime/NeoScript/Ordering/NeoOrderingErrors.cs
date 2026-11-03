// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime.NeoScript.Ordering
{
    /// <summary>The ordering library's failures, with .NET 10's messages.</summary>
    internal static class NeoOrderingErrors
    {
        /// <summary>
        /// .NET's <c>InvalidOperationException</c> for a comparer that threw,
        /// with the original as <c>InnerError</c>.
        /// </summary>
        internal static NeoRuntimeError CompareFailed(System.Exception inner)
        {
            return new(NeoErrorClass.NeoInvalidOperationError, "Failed to compare two elements in the array.", inner);
        }

        internal static NeoRuntimeError InconsistentComparer(string comparer) => new(
            NeoErrorClass.NeoArgumentError,
            "Unable to sort because the IComparer.Compare() method returns inconsistent results. Either a value does not compare equal to itself, or one value repeatedly compared to another value yields different results. IComparer: '"
                + comparer
                + "'.");

        internal static NeoRuntimeError NoElements() => new(
            NeoErrorClass.NeoInvalidOperationError,
            "Sequence contains no elements");

        internal static NeoRuntimeError ImplementIComparable() => new(
            NeoErrorClass.NeoArgumentError,
            "At least one object must implement IComparable.");

        internal static NeoRuntimeError NeedNonNegative(string parameter) => new(
            NeoErrorClass.NeoArgumentOutOfRangeError,
            "Non-negative number required. (Parameter '" + parameter + "')");

        internal static NeoRuntimeError InvalidOffsetLength() => new(
            NeoErrorClass.NeoArgumentError,
            "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
    }
}
