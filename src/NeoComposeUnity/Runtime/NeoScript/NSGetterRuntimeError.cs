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
    /// Non-catchable control fault raised when one logical NeoScript
    /// invocation exhausts a deterministic execution or allocation budget.
    /// </summary>
    public sealed class NeoScriptResourceLimitError : NSGetterRuntimeError
    {
        internal NeoScriptResourceLimitError(string message) : base(message) { }
    }

    /// <summary>
    /// Optional per-invocation limits. Values may lower, but cannot exceed,
    /// the runtime safety ceilings represented by the defaults.
    /// </summary>
    public sealed class NeoScriptExecutionBudgetLimits
    {
        public const int DefaultWorkUnits = 100_000;
        public const int DefaultCollectionVisits = 100_000;
        public const int DefaultProducedCollectionEntries = 10_000;
        public const int DefaultConstructedSessionRows = 1_000;
        public const int DefaultProducedStringCharacters = 1024 * 1024;

        public int WorkUnits { get; }
        public int CollectionVisits { get; }
        public int ProducedCollectionEntries { get; }
        public int ConstructedSessionRows { get; }
        public int ProducedStringCharacters { get; }

        public NeoScriptExecutionBudgetLimits(
            int workUnits = DefaultWorkUnits,
            int collectionVisits = DefaultCollectionVisits,
            int producedCollectionEntries = DefaultProducedCollectionEntries,
            int constructedSessionRows = DefaultConstructedSessionRows,
            int producedStringCharacters = DefaultProducedStringCharacters)
        {
            WorkUnits = Validate(
                nameof(workUnits), workUnits, DefaultWorkUnits);
            CollectionVisits = Validate(
                nameof(collectionVisits),
                collectionVisits,
                DefaultCollectionVisits);
            ProducedCollectionEntries = Validate(
                nameof(producedCollectionEntries),
                producedCollectionEntries,
                DefaultProducedCollectionEntries);
            ConstructedSessionRows = Validate(
                nameof(constructedSessionRows),
                constructedSessionRows,
                DefaultConstructedSessionRows);
            ProducedStringCharacters = Validate(
                nameof(producedStringCharacters),
                producedStringCharacters,
                DefaultProducedStringCharacters);
        }

        private static int Validate(string name, int value, int ceiling)
        {
            if (value < 0)
            {
                throw new System.ArgumentOutOfRangeException(
                    name, value, "NeoScript budget limits cannot be negative.");
            }
            if (value > ceiling)
            {
                throw new System.ArgumentOutOfRangeException(
                    name,
                    value,
                    $"NeoScript budget limit cannot exceed the safety ceiling of {ceiling}.");
            }
            return value;
        }
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
