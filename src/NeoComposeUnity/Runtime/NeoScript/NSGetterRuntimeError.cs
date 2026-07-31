// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// Error thrown by the NeoScript runtime evaluator. Mirrors the
    /// TS-side <c>NSGetterRuntimeError</c>.
    ///
    /// <para>Surfaces when:
    /// <list type="bullet">
    ///   <item><description>a referenced schema key has no entry at runtime</description></item>
    ///   <item><description>an <see cref="Json.ReferencePointer"/>'s
    ///   <c>valueId</c> dereferences a missing
    ///   <see cref="Json.MemberValue"/> row</description></item>
    ///   <item><description>a <c>throw</c> instruction is reached</description></item>
    ///   <item><description>arithmetic / comparison sees a non-numeric operand</description></item>
    ///   <item><description>a force-unwrap (<c>!</c>) hits null</description></item>
    /// </list></para>
    ///
    /// <para>Caught by <see cref="NeoMemberNSProperty.Compute"/> and
    /// surfaced as the <see cref="NSGetterResult.error"/> string. Other
    /// unexpected runtime errors propagate as the generic
    /// <see cref="System.Exception"/> they're thrown as.</para>
    /// </summary>
    public class NSGetterRuntimeError : System.Exception
    {
        public NSGetterRuntimeError(string message) : base(message) { }
    }

    /// <summary>
    /// A persisted-body validation failure raised before that body's first
    /// instruction executes. Authored try/catch blocks must not intercept
    /// these compiler/runtime compatibility failures.
    /// </summary>
    internal sealed class NeoScriptPreExecutionValidationError
        : NSGetterRuntimeError
    {
        internal NeoScriptPreExecutionValidationError(string message)
            : base(message) { }
    }

    /// <summary>
    /// Infrastructure failure used when generated native Function delegates
    /// are unavailable. It preserves the existing public runtime-error shape
    /// while remaining outside authored NeoScript catch clauses.
    /// </summary>
    internal sealed class NativeFunctionDelegateUnavailableError
        : NSGetterRuntimeError
    {
        internal NativeFunctionDelegateUnavailableError(string message)
            : base(message) { }
    }
}
