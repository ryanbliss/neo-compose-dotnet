// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public class NeoAttributeColor
        : NeoAttribute<ColorAttribute, ColorAttributeValue>
    {
        public NeoAttributeColor(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeColor(NeoClient client, ColorAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

        protected void SetRaw(Color? newValue)
        {
            SetRaw(newValue.HasValue ? NeoColorValues.FromColor(newValue.Value) : null);
        }

        protected void SetRaw(NeoColorValue? newValue)
        {
            if (attribute.required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(attribute)}.{nameof(attribute.required)} is true");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = newValue;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable);
                NotifyChanged();
                return;
            }

            ColorAttributeValue newRow = new()
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

    public class NeoAttributeColorWritable : NeoAttributeColor
    {
        public NeoAttributeColorWritable(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeColorWritable(NeoClient client, ColorAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

        public void Set(Color? newValue) => SetRaw(newValue);
    }

    /// <summary>
    /// Read-only user-facing Color wrapper (specs/color-attribute.md §5.2,
    /// decisions 5–6). <see cref="Value"/> is get-only on the whole wrapper
    /// family — there is no public mutation API; writes flow exclusively
    /// through generated property setters calling
    /// <see cref="NeoGeneratedTypesSupport.SetColor"/> /
    /// <see cref="NeoGeneratedTypesSupport.SetColorOrClear"/>. Read-only
    /// enforcement is therefore a compile error, not a runtime throw.
    /// Equality is value-based: property getters mint fresh wrapper
    /// instances, so reference equality would always be false.
    /// </summary>
    public class NeoReadOnlyColor
    {
        protected readonly NeoAttributeColor? attributeNode;
        protected Color detachedValue;

        public NeoReadOnlyColor(Color value)
        {
            detachedValue = value;
        }

        public NeoReadOnlyColor(float r, float g, float b, float a)
        {
            detachedValue = new Color(r, g, b, a);
        }

        public NeoReadOnlyColor(NeoAttributeColor attribute)
        {
            attributeNode = attribute;
        }

        /// <summary>Explicit read access; never needed for writes.</summary>
        public Color Value => attributeNode is null
            ? detachedValue
            : NeoColorValues.ReadColor(attributeNode);

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
    /// Writable-context Color wrapper. Adds no mutation API over
    /// <see cref="NeoReadOnlyColor"/> — it only adds the native→wrapper
    /// implicit conversion so <c>obj.TintColor = Color.red;</c> produces a
    /// detached instance whose value the generated setter writes through
    /// the node (value-copy semantics, never a live link).
    /// </summary>
    public class NeoColor : NeoReadOnlyColor
    {
        /// <summary>Bound ctor — emitted by generated getters.</summary>
        public NeoColor(NeoAttributeColor attribute)
            : base(attribute) { }

        /// <summary>Detached ctor — carries a plain value (factories, assignment).</summary>
        public NeoColor(Color value)
            : base(value) { }

        public NeoColor(float r, float g, float b, float a)
            : base(r, g, b, a) { }

        public static implicit operator NeoColor(Color value) => new NeoColor(value);
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

        public static Color ReadColor(NeoAttributeColor attribute)
        {
            var value = attribute.value?.value;
            if (value is null)
            {
                throw new System.InvalidOperationException(
                    $"Required Color '{attribute.attribute.name}' has no value.");
            }
            return ToColor(value);
        }

        public static Color ToColor(NeoColorValue value)
        {
            return new Color(value.r, value.g, value.b, value.a);
        }
    }
}
