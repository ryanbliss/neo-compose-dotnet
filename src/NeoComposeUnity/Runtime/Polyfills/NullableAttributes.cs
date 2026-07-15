// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

// Polyfill for nullable-analysis members that Roslyn understands by
// name but that aren't in the netstandard2.1 BCL (they were added to
// `System.Diagnostics.CodeAnalysis` in .NET 5+). Defining them locally
// in the same namespace lets the compiler pick them up — same trick
// most popular .NET libraries that still target netstandard use to
// benefit from C# 8+ flow analysis without bumping their TFM.

#if !NET5_0_OR_GREATER

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// Specifies that a method, when called, guarantees the named
    /// member(s) on <c>this</c> are non-null on return — used to make
    /// `[MemberNotNull(nameof(_field))]` lift the
    /// "non-nullable field uninitialized after constructor" warning
    /// when the constructor's late-init work happens inside a helper
    /// method rather than directly in the ctor body.
    /// </summary>
    [System.AttributeUsage(
        System.AttributeTargets.Method | System.AttributeTargets.Property,
        Inherited = false,
        AllowMultiple = true)]
    internal sealed class MemberNotNullAttribute : System.Attribute
    {
        public string[] Members { get; }
        public MemberNotNullAttribute(string member) => Members = new[] { member };
        public MemberNotNullAttribute(params string[] members) => Members = members;
    }
}

#endif
