// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Why an automatic <see cref="NeoSubtreeDistributionKind.Sparse"/> result
    /// is growth-risky — the reason an explicit <c>.Packed</c> on this kind
    /// would make the subtree share its parent document's hard size limit
    /// (P76 §2).
    ///
    /// <para>A discriminant rather than prose so the authoring UI owns its copy
    /// and translations while the table stays the single decision point. The
    /// SDK never shows it; it is modelled here because the reason is a
    /// projection of the same table row and would otherwise be a second list
    /// the two runtimes could drift apart on.</para>
    /// </summary>
    public enum NeoDistributionGrowthReason
    {
        /// <summary>The automatic result is Packed; nothing grows.</summary>
        None = 0,
        /// <summary>Entry count grows with content — Dictionary, List.</summary>
        EntryCount = 1,
        /// <summary>Schema and authored child count can grow — Class.</summary>
        AuthoredChildren = 2,
        /// <summary>Arbitrary-length text — Plain String, Decimal.</summary>
        UnboundedText = 3,
        /// <summary>Multi-selection follows collection size — Lookup, DialogueLookup.</summary>
        SelectionCount = 4,
        /// <summary>A closure can carry unbounded code and captures — NSDelegate.</summary>
        ClosureBody = 5,
        /// <summary>Listener count grows with content — NSAction.</summary>
        ListenerCount = 6,
    }

    /// <summary>
    /// The P76 §1 automatic-distribution table: where a value whose effective
    /// member chain declares no <c>distribution</c> physically lives. The C#
    /// twin of <c>src/models/members/member-subtree-distribution.ts</c>.
    ///
    /// <para><b>This is a versioned storage contract.</b> Changing any arm
    /// changes the meaning of already-stored rows, so a future change requires
    /// its own export-schema bump and a redistribution of every affected value
    /// before readers adopt the new result. Editing this table alone must fail
    /// the cross-runtime parity tests rather than reinterpret stored data in
    /// place.</para>
    ///
    /// <para>One table, consulted through <see cref="Automatic"/>. Nothing else
    /// in the SDK may branch on member kind to answer a distribution question:
    /// a scattered switch is exactly how the two runtimes would come to
    /// disagree about which rows a packed parent is allowed to swallow.</para>
    /// </summary>
    public static class NeoSubtreeDistribution
    {
        /// <summary>
        /// One resolved table row. <see cref="Distribution"/> is null for the
        /// value-less kinds, where distribution does not apply at all.
        /// </summary>
        public readonly struct Result
        {
            internal Result(
                NeoSubtreeDistributionKind? distribution,
                NeoDistributionGrowthReason growth)
            {
                Distribution = distribution;
                Growth = growth;
            }

            /// <summary>
            /// The automatic distribution, or null when the kind owns no
            /// stored value (NSProperty, Function, Interface, NSFunction).
            /// </summary>
            public NeoSubtreeDistributionKind? Distribution { get; }

            /// <summary>
            /// Set only when <see cref="Distribution"/> is Sparse because the
            /// payload or child count grows — the §2 warning condition.
            /// </summary>
            public NeoDistributionGrowthReason Growth { get; }
        }

        /// <summary>How one table arm decides.</summary>
        private enum Rule
        {
            /// <summary>A fixed-size shape with no growth axis.</summary>
            Packed,
            /// <summary>Grows with content; the reason rides in the row.</summary>
            Sparse,
            /// <summary><c>.Localized</c> (or absent) packs; <c>.Plain</c> is sparse.</summary>
            StringFormat,
            /// <summary><c>.Single</c> packs; <c>.Multi</c> is sparse.</summary>
            SelectionCardinality,
            /// <summary>Declaration-only or type-only: no stored value to distribute.</summary>
            ValueLess,
            /// <summary>An open slot has no automatic shape; substitute the binding first.</summary>
            OpenSlot,
        }

        private readonly struct Arm
        {
            internal Arm(Rule rule, NeoDistributionGrowthReason growth)
            {
                Rule = rule;
                Growth = growth;
            }

            internal Rule Rule { get; }

            /// <summary>
            /// The growth axis of an unconditionally-Sparse arm, or the axis a
            /// conditional arm names when its Sparse branch is taken.
            /// </summary>
            internal NeoDistributionGrowthReason Growth { get; }
        }

        private static Arm Packed() =>
            new Arm(Rule.Packed, NeoDistributionGrowthReason.None);

        private static Arm Sparse(NeoDistributionGrowthReason growth) =>
            new Arm(Rule.Sparse, growth);

        private static Arm Conditional(Rule rule, NeoDistributionGrowthReason growth) =>
            new Arm(rule, growth);

        private static Arm ValueLess() =>
            new Arm(Rule.ValueLess, NeoDistributionGrowthReason.None);

        /// <summary>
        /// The complete §1 table, keyed by every <see cref="MemberKind"/> that
        /// can be a persisted member record. The reasons in the comments are
        /// the spec's own justification for each arm — never a restriction on
        /// what an author may explicitly choose. Explicit <c>.Sparse</c> and
        /// explicit <c>.Packed</c> are both legal on every value-owning kind,
        /// including the growing ones (§1.2).
        /// </summary>
        private static readonly Dictionary<MemberKind, Arm> Table = new()
        {
            // A stored null has no growth axis.
            [MemberKind.Null] = Packed(),
            // Fixed scalar.
            [MemberKind.Bool] = Packed(),
            // Fixed scalar.
            [MemberKind.Int] = Packed(),
            // A localized value stores one localized-text id; plain text is
            // unbounded. The per-locale bodies stay on their own records
            // under either arm.
            [MemberKind.String] =
                Conditional(Rule.StringFormat, NeoDistributionGrowthReason.UnboundedText),
            // Fixed scalar.
            [MemberKind.Float] = Packed(),
            // Entry count grows with content.
            [MemberKind.Dictionary] = Sparse(NeoDistributionGrowthReason.EntryCount),
            // Entry count grows with content.
            [MemberKind.List] = Sparse(NeoDistributionGrowthReason.EntryCount),
            // Schema and authored child count can grow.
            [MemberKind.Class] = Sparse(NeoDistributionGrowthReason.AuthoredChildren),
            // The enum declaration bounds the selected option ids; enum edits
            // recheck encoded size.
            [MemberKind.Enum] = Packed(),
            // Single selection stores at most one value id; multi-selection
            // follows collection size.
            [MemberKind.Lookup] =
                Conditional(Rule.SelectionCardinality, NeoDistributionGrowthReason.SelectionCount),
            // Computed property, no stored value.
            [MemberKind.NSProperty] = ValueLess(),
            // Fixed `fileId` and `sliceIndex` shape.
            [MemberKind.Sprite] = Packed(),
            // Fixed one-file-id shape.
            [MemberKind.Audio] = Packed(),
            // Declaration only, no stored value.
            [MemberKind.Function] = ValueLess(),
            // Fixed two-number shape.
            [MemberKind.Vector2] = Packed(),
            // Fixed two-integer shape.
            [MemberKind.Vector2Int] = Packed(),
            // Fixed three-number shape.
            [MemberKind.Vector3] = Packed(),
            // Fixed three-integer shape.
            [MemberKind.Vector3Int] = Packed(),
            // Single selection stores at most one dialogue id.
            [MemberKind.DialogueLookup] =
                Conditional(Rule.SelectionCardinality, NeoDistributionGrowthReason.SelectionCount),
            // Fixed four-channel shape.
            [MemberKind.Color] = Packed(),
            // Exact decimals are arbitrary-length strings.
            [MemberKind.Decimal] = Sparse(NeoDistributionGrowthReason.UnboundedText),
            // The open slot has no automatic shape; an explicit member-chain
            // choice still wins.
            [MemberKind.Generic] = new Arm(Rule.OpenSlot, NeoDistributionGrowthReason.None),
            // Type information only; not a persistable member record kind.
            [MemberKind.Interface] = ValueLess(),
            // Declaration only, no stored value.
            [MemberKind.NSFunction] = ValueLess(),
            // Fixed one-member-id shape.
            [MemberKind.FunctionRef] = Packed(),
            // A closure can carry unbounded code and captures.
            [MemberKind.NSDelegate] = Sparse(NeoDistributionGrowthReason.ClosureBody),
            // Listener count grows with content.
            [MemberKind.NSAction] = Sparse(NeoDistributionGrowthReason.ListenerCount),
            // Fixed class, variant, and optional bound-row ids.
            [MemberKind.Variant] = Packed(),
        };

        /// <summary>
        /// The §1 automatic distribution of a member whose effective chain
        /// declares no explicit <c>distribution</c>.
        ///
        /// <para><paramref name="kind"/>, <paramref name="format"/>, and
        /// <paramref name="selection"/> must come from the effective
        /// <b>concrete</b> member — the merged extends chain with generic
        /// slots already substituted for their binding. Absence of either
        /// setting carries the same meaning it does on the member record: an
        /// absent format is Localized and an absent selection is Single, so
        /// callers pass the resolved value rather than a null.</para>
        ///
        /// <para>Throws for <see cref="MemberKind.Generic"/>: an open slot has
        /// no shape of its own, so a caller that has not substituted the
        /// concrete binding is asking a question the table cannot answer.
        /// Answering "not applicable" would silently drop
        /// <c>NeoAnimationSegmentFrame&lt;T&gt;.Value</c> out of packing
        /// (§1.3).</para>
        /// </summary>
        public static Result Automatic(
            MemberKind kind,
            NeoStringFormatKind format,
            NeoMemberSelectionKind selection)
        {
            if (!Table.TryGetValue(kind, out Arm arm))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(kind),
                    $"MemberKind '{kind}' has no P76 §1 automatic distribution arm. "
                    + "Every persistable member kind must declare one.");
            }
            switch (arm.Rule)
            {
                case Rule.Packed:
                    return new Result(
                        NeoSubtreeDistributionKind.Packed,
                        NeoDistributionGrowthReason.None);
                case Rule.Sparse:
                    return new Result(NeoSubtreeDistributionKind.Sparse, arm.Growth);
                case Rule.StringFormat:
                    return format == NeoStringFormatKind.Plain
                        ? new Result(NeoSubtreeDistributionKind.Sparse, arm.Growth)
                        : new Result(
                            NeoSubtreeDistributionKind.Packed,
                            NeoDistributionGrowthReason.None);
                case Rule.SelectionCardinality:
                    return selection == NeoMemberSelectionKind.Multi
                        ? new Result(NeoSubtreeDistributionKind.Sparse, arm.Growth)
                        : new Result(
                            NeoSubtreeDistributionKind.Packed,
                            NeoDistributionGrowthReason.None);
                case Rule.ValueLess:
                    return new Result(null, NeoDistributionGrowthReason.None);
                default:
                    throw new System.InvalidOperationException(
                        "MemberKind.Generic has no automatic distribution. Substitute "
                        + "the concrete binding member first and pass its kind and settings.");
            }
        }

        /// <summary>
        /// True when the member kind owns a stored value and may therefore
        /// declare a <c>distribution</c>. Derived from the table above so the
        /// guard and the automatic result cannot disagree about which kinds
        /// participate.
        /// </summary>
        public static bool KindSupportsDistribution(MemberKind kind) =>
            Table.TryGetValue(kind, out Arm arm) && arm.Rule != Rule.ValueLess;
    }
}
