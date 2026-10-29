// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime.NeoScript.NumberMembers
{
    /// <summary>The <c>INumberBase</c> classification members, <c>TSelf.IsNaN(value)</c> and its siblings.</summary>
    internal enum NeoNumberPredicate
    {
        IsCanonical,
        IsComplexNumber,
        IsEvenInteger,
        IsFinite,
        IsImaginaryNumber,
        IsInfinity,
        IsInteger,
        IsNaN,
        IsNegative,
        IsNegativeInfinity,
        IsNormal,
        IsOddInteger,
        IsPositive,
        IsPositiveInfinity,
        IsRealNumber,
        IsSubnormal,
        IsZero,
    }
}
