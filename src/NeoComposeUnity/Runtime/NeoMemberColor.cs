// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public class NeoMemberColor
        : NeoMember<ColorMember, ColorMemberValue>
    {
        public NeoMemberColor(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberColor(NeoClient client, ColorMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        protected void SetRaw(Color? newValue)
        {
            SetRaw(newValue.HasValue ? NeoColorValues.FromColor(newValue.Value) : null);
        }

        protected void SetRaw(NeoColorValue? newValue)
        {
            if (member.Requirement == NeoMemberRequirementKind.Required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(member)} requirement is Required");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = newValue;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable, "value");
                // No NotifyChanged() here — the write above already raised it
                // through this node's own OnValueIdChainChanged. See that
                // method's remarks.
                return;
            }

            ColorMemberValue newRow = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = newValue,
            };
            BindNewValue(newRow);
            NotifyChanged();
        }
    }

    public class NeoMemberColorWritable : NeoMemberColor
    {
        public NeoMemberColorWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberColorWritable(NeoClient client, ColorMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        public void Set(Color? newValue) => SetRaw(newValue);
    }

    /// <summary>
    /// Read-only user-facing Color wrapper (specs/color-member.md §5.2).
    /// <see cref="Value"/> and the <see cref="r"/>/<see cref="g"/>/
    /// <see cref="b"/>/<see cref="a"/> channels are get-only here, and
    /// whole-value assignment still flows through generated property setters
    /// calling <see cref="NeoGeneratedTypesSupport.SetColor"/> /
    /// <see cref="NeoGeneratedTypesSupport.SetColorOrClear"/>.
    ///
    /// <para>P42 §4.1 overturns specs/color-member.md §6 decisions 5–6, which
    /// made the <em>whole</em> family get-only: the writable
    /// <see cref="NeoColor"/> now carries write-through channel setters, so
    /// read-only misuse is no longer purely a compile error. The channel
    /// accessors themselves are new in P42 — before it, reading a channel
    /// meant <c>obj.Tint.Value.a</c>.</para>
    ///
    /// <para>Equality is value-based: property getters mint fresh wrapper
    /// instances, so reference equality would always be false.</para>
    /// </summary>
    public class NeoReadOnlyColor
    {
        protected readonly NeoMemberColor? memberNode;
        protected Color detachedValue;

        public NeoReadOnlyColor(Color value)
        {
            detachedValue = value;
        }

        public NeoReadOnlyColor(float r, float g, float b, float a)
        {
            detachedValue = new Color(r, g, b, a);
        }

        public NeoReadOnlyColor(NeoMemberColor member)
        {
            memberNode = member;
        }

        public float r => RequireValue(nameof(r)).r;
        public float g => RequireValue(nameof(g)).g;
        public float b => RequireValue(nameof(b)).b;
        public float a => RequireValue(nameof(a)).a;

        /// <summary>Explicit read access; never needed for writes.</summary>
        public Color Value => memberNode is null
            ? detachedValue
            : NeoColorValues.ReadColor(memberNode);

        /// <inheritdoc cref="NeoReadOnlyVector2.RequireValue"/>
        private protected Color RequireValue(string field) => memberNode is null
            ? detachedValue
            : NeoColorValues.ReadColor(memberNode, field);

        public static implicit operator Color(NeoReadOnlyColor value) => value.Value;

        // Value-based equality (decision 6). Null-safe; the mixed
        // wrapper/native overloads are required explicitly because the
        // Color→NeoColor conversion is declared on the derived class and is
        // not considered when converting to this base type during operator
        // resolution.
        public static bool operator ==(NeoReadOnlyColor? left, NeoReadOnlyColor? right)
        {
            if (left is null) return right is null;
            if (right is null) return false;
            return left.Value == right.Value;
        }

        public static bool operator !=(NeoReadOnlyColor? left, NeoReadOnlyColor? right)
            => !(left == right);

        public static bool operator ==(NeoReadOnlyColor? left, Color right)
            => left is not null && left.Value == right;

        public static bool operator !=(NeoReadOnlyColor? left, Color right)
            => !(left == right);

        public static bool operator ==(Color left, NeoReadOnlyColor? right)
            => right is not null && right.Value == left;

        public static bool operator !=(Color left, NeoReadOnlyColor? right)
            => !(left == right);

        public override bool Equals(object? obj)
        {
            if (obj is NeoReadOnlyColor wrapper) return Value == wrapper.Value;
            if (obj is Color native) return Value == native;
            return false;
        }

        public override int GetHashCode() => Value.GetHashCode();
    }

    /// <summary>
    /// Writable-context Color wrapper. Adds the native→wrapper implicit
    /// conversion so <c>obj.TintColor = Color.red;</c> compiles, and — since
    /// P42 §4.1 — settable <see cref="r"/>/<see cref="g"/>/<see cref="b"/>/
    /// <see cref="a"/> channels.
    ///
    /// <para><b>Binding decides what a channel write means.</b> A
    /// <b>bound</b> instance — one minted from a member node, which is what
    /// generated getters emit — writes through: setting a channel reads the
    /// leaf's current colour, patches the one channel, and writes the whole
    /// leaf straight back through the node. A <b>detached</b> instance — one
    /// carrying a plain value, produced by <c>new NeoColor(Color.red)</c>,
    /// the implicit operator, or a factory argument — is a value copy;
    /// mutating a channel is local until the instance is assigned, and the
    /// assignment itself copies the value through the node rather than
    /// creating a live link. (Before P42 this comment read "value-copy
    /// semantics, never a live link" without qualification; that described
    /// only the detached case.)</para>
    ///
    /// <para>Channel writes reject values outside <c>[0, 1]</c> rather than
    /// clamping them (P42 §1.4 / decision D2), matching
    /// <see cref="Json.NeoColorValueConverter"/> on the read path.</para>
    ///
    /// <para>Read-only enforcement (decision D5) is a runtime throw via
    /// <see cref="NeoStructuredLeafWriteGuard"/>, not a compile error.</para>
    /// </summary>
    public class NeoColor : NeoReadOnlyColor
    {
        private readonly NeoGeneratedClassValue? owner;

        /// <summary>Bound ctor — emitted by generated getters.</summary>
        public NeoColor(NeoMemberColor member)
            : base(member) { }

        /// <summary>
        /// Bound ctor that also records the owning generated value, so channel
        /// setters can honour instance-level read-only (decision D5).
        /// Generated getters on writable families should prefer this overload.
        /// </summary>
        public NeoColor(NeoMemberColor member, NeoGeneratedClassValue owner)
            : base(member)
        {
            this.owner = owner;
        }

        /// <summary>Detached ctor — carries a plain value (factories, assignment).</summary>
        public NeoColor(Color value)
            : base(value) { }

        public NeoColor(float r, float g, float b, float a)
            : base(r, g, b, a) { }

        public new float r
        {
            get => RequireValue(nameof(r)).r;
            set
            {
                Color next = RequireValue(nameof(r));
                next.r = NeoColorValues.RequireChannel(value, nameof(r));
                Write(next, nameof(r));
            }
        }

        public new float g
        {
            get => RequireValue(nameof(g)).g;
            set
            {
                Color next = RequireValue(nameof(g));
                next.g = NeoColorValues.RequireChannel(value, nameof(g));
                Write(next, nameof(g));
            }
        }

        public new float b
        {
            get => RequireValue(nameof(b)).b;
            set
            {
                Color next = RequireValue(nameof(b));
                next.b = NeoColorValues.RequireChannel(value, nameof(b));
                Write(next, nameof(b));
            }
        }

        public new float a
        {
            get => RequireValue(nameof(a)).a;
            set
            {
                Color next = RequireValue(nameof(a));
                next.a = NeoColorValues.RequireChannel(value, nameof(a));
                Write(next, nameof(a));
            }
        }

        public static implicit operator NeoColor(Color value) => new NeoColor(value);

        private void Write(Color next, string field)
        {
            if (memberNode is null)
            {
                detachedValue = next;
                return;
            }

            NeoStructuredLeafWriteGuard
                .RequireWritable<NeoMemberColorWritable>(
                    owner, memberNode, nameof(NeoColor), field)
                .Set(next);
        }
    }

    internal static class NeoColorValues
    {
        /// <summary>
        /// Pure float pass-through — the wire format and
        /// <c>UnityEngine.Color</c> already agree on [0,1] floats; no 0–255
        /// scaling anywhere in the SDK.
        /// </summary>
        public static NeoColorValue FromColor(Color value)
        {
            return new NeoColorValue { r = value.r, g = value.g, b = value.b, a = value.a };
        }

        /// <inheritdoc cref="NeoVectorValues.ReadVector2(NeoMemberVector2, string?)"/>
        public static Color ReadColor(NeoMemberColor member, string? field = null)
        {
            var value = member.value?.value;
            if (value is null)
            {
                throw NeoStructuredLeafReadGuard.MissingValue(
                    "Color", member.member.name, field);
            }
            return ToColor(value);
        }

        public static Color ToColor(NeoColorValue value)
        {
            return new Color(value.r, value.g, value.b, value.a);
        }

        /// <summary>
        /// Write-path counterpart to
        /// <see cref="NeoColorValueConverter.ReadColorComponent"/>: a channel
        /// must be finite and within <c>[0, 1]</c>. P42 §1.4 / decision D2 —
        /// out-of-range channels are <b>rejected, not clamped</b>, so a
        /// per-channel setter can throw. (The spec's §7.4 bullet and its
        /// matching acceptance checkbox still say "clamp"; they are stale, and
        /// §1.4 plus the shipped converter are correct.)
        /// </summary>
        public static float RequireChannel(float value, string channel)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(value),
                    $"Color component '{channel}' must be a finite number.");
            }
            if (value < 0f)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(value),
                    $"Color component '{channel}' must not be less than 0.");
            }
            if (value > 1f)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(value),
                    $"Color component '{channel}' must not be greater than 1.");
            }
            return value;
        }
    }
}
