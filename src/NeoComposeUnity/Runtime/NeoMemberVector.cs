// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public class NeoMemberVector2
        : NeoMember<Vector2Member, Vector2MemberValue>
    {
        public NeoMemberVector2(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberVector2(NeoClient client, Vector2Member member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        protected void SetRaw(Vector2? newValue)
        {
            SetRaw(newValue.HasValue ? NeoVectorValues.FromVector2(newValue.Value) : null);
        }

        protected void SetRaw(NeoVector2Value? newValue)
        {
            if (member.required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(member)}.{nameof(member.required)} is true");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = newValue;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable, "value");
                NotifyChanged();
                return;
            }

            Vector2MemberValue newRow = new()
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

    public class NeoMemberVector2Writable : NeoMemberVector2
    {
        public NeoMemberVector2Writable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberVector2Writable(NeoClient client, Vector2Member member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        public void Set(Vector2? newValue) => SetRaw(newValue);
    }

    public class NeoMemberVector2Int
        : NeoMember<Vector2IntMember, Vector2MemberValue>
    {
        public NeoMemberVector2Int(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberVector2Int(NeoClient client, Vector2IntMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        protected void SetRaw(Vector2Int? newValue)
        {
            SetRaw(newValue.HasValue ? NeoVectorValues.FromVector2Int(newValue.Value) : null);
        }

        protected void SetRaw(NeoVector2Value? newValue)
        {
            if (member.required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(member)}.{nameof(member.required)} is true");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = newValue;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable, "value");
                NotifyChanged();
                return;
            }

            Vector2MemberValue newRow = new()
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

    public class NeoMemberVector2IntWritable : NeoMemberVector2Int
    {
        public NeoMemberVector2IntWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberVector2IntWritable(NeoClient client, Vector2IntMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        public void Set(Vector2Int? newValue) => SetRaw(newValue);
    }

    public class NeoMemberVector3
        : NeoMember<Vector3Member, Vector3MemberValue>
    {
        public NeoMemberVector3(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberVector3(NeoClient client, Vector3Member member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        protected void SetRaw(Vector3? newValue)
        {
            SetRaw(newValue.HasValue ? NeoVectorValues.FromVector3(newValue.Value) : null);
        }

        protected void SetRaw(NeoVector3Value? newValue)
        {
            if (member.required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(member)}.{nameof(member.required)} is true");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = newValue;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable, "value");
                NotifyChanged();
                return;
            }

            Vector3MemberValue newRow = new()
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

    public class NeoMemberVector3Writable : NeoMemberVector3
    {
        public NeoMemberVector3Writable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberVector3Writable(NeoClient client, Vector3Member member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        public void Set(Vector3? newValue) => SetRaw(newValue);
    }

    public class NeoMemberVector3Int
        : NeoMember<Vector3IntMember, Vector3MemberValue>
    {
        public NeoMemberVector3Int(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberVector3Int(NeoClient client, Vector3IntMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        protected void SetRaw(Vector3Int? newValue)
        {
            SetRaw(newValue.HasValue ? NeoVectorValues.FromVector3Int(newValue.Value) : null);
        }

        protected void SetRaw(NeoVector3Value? newValue)
        {
            if (member.required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(member)}.{nameof(member.required)} is true");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = newValue;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable, "value");
                NotifyChanged();
                return;
            }

            Vector3MemberValue newRow = new()
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

    public class NeoMemberVector3IntWritable : NeoMemberVector3Int
    {
        public NeoMemberVector3IntWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberVector3IntWritable(NeoClient client, Vector3IntMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        public void Set(Vector3Int? newValue) => SetRaw(newValue);
    }

    /// <summary>
    /// Read-only Vector2 wrapper. Assignment convention
    /// (specs/color-member.md §6, decisions 5–6): <see cref="Value"/> is
    /// get-only across the whole wrapper family — writes flow exclusively
    /// through generated property setters calling
    /// <see cref="NeoGeneratedTypesSupport.SetVector2(NeoMemberClassWritable, string, NeoReadOnlyVector2)"/>,
    /// so read-only misuse is a compile error. Equality is value-based:
    /// property getters mint fresh wrapper instances, so reference equality
    /// would always be false.
    /// </summary>
    public class NeoReadOnlyVector2
    {
        protected readonly NeoMemberVector2? memberNode;
        protected Vector2 detachedValue;

        public NeoReadOnlyVector2(Vector2 value)
            : this(value.x, value.y) { }

        public NeoReadOnlyVector2(float x, float y)
        {
            detachedValue = new Vector2(x, y);
        }

        public NeoReadOnlyVector2(NeoMemberVector2 member)
        {
            memberNode = member;
        }

        public float x => Value.x;
        public float y => Value.y;
        public Vector2 Value => memberNode is null
            ? detachedValue
            : NeoVectorValues.ReadVector2(memberNode);

        public static implicit operator Vector2(NeoReadOnlyVector2 value) => value.Value;

        public static bool operator ==(NeoReadOnlyVector2? left, NeoReadOnlyVector2? right)
        {
            if (left is null) return right is null;
            if (right is null) return false;
            return left.Value == right.Value;
        }

        public static bool operator !=(NeoReadOnlyVector2? left, NeoReadOnlyVector2? right)
            => !(left == right);

        public static bool operator ==(NeoReadOnlyVector2? left, Vector2 right)
            => left is not null && left.Value == right;

        public static bool operator !=(NeoReadOnlyVector2? left, Vector2 right)
            => !(left == right);

        public static bool operator ==(Vector2 left, NeoReadOnlyVector2? right)
            => right is not null && right.Value == left;

        public static bool operator !=(Vector2 left, NeoReadOnlyVector2? right)
            => !(left == right);

        public override bool Equals(object? obj)
        {
            if (obj is NeoReadOnlyVector2 wrapper) return Value == wrapper.Value;
            if (obj is Vector2 native) return Value == native;
            return false;
        }

        public override int GetHashCode() => Value.GetHashCode();
    }

    /// <summary>
    /// Writable-context Vector2 wrapper. Adds no mutation API over
    /// <see cref="NeoReadOnlyVector2"/> — only the native→wrapper implicit
    /// conversion so <c>obj.Position = new Vector2(…);</c> produces a
    /// detached instance whose value the generated setter writes through
    /// the node (value-copy semantics, never a live link).
    /// </summary>
    public class NeoVector2 : NeoReadOnlyVector2
    {
        public NeoVector2(Vector2 value)
            : this(value.x, value.y) { }

        public NeoVector2(float x, float y)
            : base(x, y) { }

        public NeoVector2(NeoMemberVector2 member)
            : base(member) { }

        public static implicit operator NeoVector2(Vector2 value) => new NeoVector2(value);
    }

    /// <summary>
    /// Read-only Vector2Int wrapper — assignment convention and value-based
    /// equality; see <see cref="NeoReadOnlyVector2"/>.
    /// </summary>
    public class NeoReadOnlyVector2Int
    {
        protected readonly NeoMemberVector2Int? memberNode;
        protected Vector2Int detachedValue;

        public NeoReadOnlyVector2Int(Vector2Int value)
            : this(value.x, value.y) { }

        public NeoReadOnlyVector2Int(int x, int y)
        {
            detachedValue = new Vector2Int(x, y);
        }

        public NeoReadOnlyVector2Int(NeoMemberVector2Int member)
        {
            memberNode = member;
        }

        public int x => Value.x;
        public int y => Value.y;
        public Vector2Int Value => memberNode is null
            ? detachedValue
            : NeoVectorValues.ReadVector2Int(memberNode);

        public static implicit operator Vector2Int(NeoReadOnlyVector2Int value) => value.Value;

        public static bool operator ==(NeoReadOnlyVector2Int? left, NeoReadOnlyVector2Int? right)
        {
            if (left is null) return right is null;
            if (right is null) return false;
            return left.Value == right.Value;
        }

        public static bool operator !=(NeoReadOnlyVector2Int? left, NeoReadOnlyVector2Int? right)
            => !(left == right);

        public static bool operator ==(NeoReadOnlyVector2Int? left, Vector2Int right)
            => left is not null && left.Value == right;

        public static bool operator !=(NeoReadOnlyVector2Int? left, Vector2Int right)
            => !(left == right);

        public static bool operator ==(Vector2Int left, NeoReadOnlyVector2Int? right)
            => right is not null && right.Value == left;

        public static bool operator !=(Vector2Int left, NeoReadOnlyVector2Int? right)
            => !(left == right);

        public override bool Equals(object? obj)
        {
            if (obj is NeoReadOnlyVector2Int wrapper) return Value == wrapper.Value;
            if (obj is Vector2Int native) return Value == native;
            return false;
        }

        public override int GetHashCode() => Value.GetHashCode();
    }

    /// <summary>
    /// Writable-context Vector2Int wrapper — no mutation API; see
    /// <see cref="NeoVector2"/>.
    /// </summary>
    public class NeoVector2Int : NeoReadOnlyVector2Int
    {
        public NeoVector2Int(Vector2Int value)
            : this(value.x, value.y) { }

        public NeoVector2Int(int x, int y)
            : base(x, y) { }

        public NeoVector2Int(NeoMemberVector2Int member)
            : base(member) { }

        public static implicit operator NeoVector2Int(Vector2Int value) => new NeoVector2Int(value);
    }

    /// <summary>
    /// Read-only Vector3 wrapper — assignment convention and value-based
    /// equality; see <see cref="NeoReadOnlyVector2"/>.
    /// </summary>
    public class NeoReadOnlyVector3
    {
        protected readonly NeoMemberVector3? memberNode;
        protected Vector3 detachedValue;

        public NeoReadOnlyVector3(Vector3 value)
            : this(value.x, value.y, value.z) { }

        public NeoReadOnlyVector3(float x, float y, float z)
        {
            detachedValue = new Vector3(x, y, z);
        }

        public NeoReadOnlyVector3(NeoMemberVector3 member)
        {
            memberNode = member;
        }

        public float x => Value.x;
        public float y => Value.y;
        public float z => Value.z;
        public Vector3 Value => memberNode is null
            ? detachedValue
            : NeoVectorValues.ReadVector3(memberNode);

        public static implicit operator Vector3(NeoReadOnlyVector3 value) => value.Value;

        public static bool operator ==(NeoReadOnlyVector3? left, NeoReadOnlyVector3? right)
        {
            if (left is null) return right is null;
            if (right is null) return false;
            return left.Value == right.Value;
        }

        public static bool operator !=(NeoReadOnlyVector3? left, NeoReadOnlyVector3? right)
            => !(left == right);

        public static bool operator ==(NeoReadOnlyVector3? left, Vector3 right)
            => left is not null && left.Value == right;

        public static bool operator !=(NeoReadOnlyVector3? left, Vector3 right)
            => !(left == right);

        public static bool operator ==(Vector3 left, NeoReadOnlyVector3? right)
            => right is not null && right.Value == left;

        public static bool operator !=(Vector3 left, NeoReadOnlyVector3? right)
            => !(left == right);

        public override bool Equals(object? obj)
        {
            if (obj is NeoReadOnlyVector3 wrapper) return Value == wrapper.Value;
            if (obj is Vector3 native) return Value == native;
            return false;
        }

        public override int GetHashCode() => Value.GetHashCode();
    }

    /// <summary>
    /// Writable-context Vector3 wrapper — no mutation API; see
    /// <see cref="NeoVector2"/>.
    /// </summary>
    public class NeoVector3 : NeoReadOnlyVector3
    {
        public NeoVector3(Vector3 value)
            : this(value.x, value.y, value.z) { }

        public NeoVector3(float x, float y, float z)
            : base(x, y, z) { }

        public NeoVector3(NeoMemberVector3 member)
            : base(member) { }

        public static implicit operator NeoVector3(Vector3 value) => new NeoVector3(value);
    }

    /// <summary>
    /// Read-only Vector3Int wrapper — assignment convention and value-based
    /// equality; see <see cref="NeoReadOnlyVector2"/>.
    /// </summary>
    public class NeoReadOnlyVector3Int
    {
        protected readonly NeoMemberVector3Int? memberNode;
        protected Vector3Int detachedValue;

        public NeoReadOnlyVector3Int(Vector3Int value)
            : this(value.x, value.y, value.z) { }

        public NeoReadOnlyVector3Int(int x, int y, int z)
        {
            detachedValue = new Vector3Int(x, y, z);
        }

        public NeoReadOnlyVector3Int(NeoMemberVector3Int member)
        {
            memberNode = member;
        }

        public int x => Value.x;
        public int y => Value.y;
        public int z => Value.z;
        public Vector3Int Value => memberNode is null
            ? detachedValue
            : NeoVectorValues.ReadVector3Int(memberNode);

        public static implicit operator Vector3Int(NeoReadOnlyVector3Int value) => value.Value;

        public static bool operator ==(NeoReadOnlyVector3Int? left, NeoReadOnlyVector3Int? right)
        {
            if (left is null) return right is null;
            if (right is null) return false;
            return left.Value == right.Value;
        }

        public static bool operator !=(NeoReadOnlyVector3Int? left, NeoReadOnlyVector3Int? right)
            => !(left == right);

        public static bool operator ==(NeoReadOnlyVector3Int? left, Vector3Int right)
            => left is not null && left.Value == right;

        public static bool operator !=(NeoReadOnlyVector3Int? left, Vector3Int right)
            => !(left == right);

        public static bool operator ==(Vector3Int left, NeoReadOnlyVector3Int? right)
            => right is not null && right.Value == left;

        public static bool operator !=(Vector3Int left, NeoReadOnlyVector3Int? right)
            => !(left == right);

        public override bool Equals(object? obj)
        {
            if (obj is NeoReadOnlyVector3Int wrapper) return Value == wrapper.Value;
            if (obj is Vector3Int native) return Value == native;
            return false;
        }

        public override int GetHashCode() => Value.GetHashCode();
    }

    /// <summary>
    /// Writable-context Vector3Int wrapper — no mutation API; see
    /// <see cref="NeoVector2"/>.
    /// </summary>
    public class NeoVector3Int : NeoReadOnlyVector3Int
    {
        public NeoVector3Int(Vector3Int value)
            : this(value.x, value.y, value.z) { }

        public NeoVector3Int(int x, int y, int z)
            : base(x, y, z) { }

        public NeoVector3Int(NeoMemberVector3Int member)
            : base(member) { }

        public static implicit operator NeoVector3Int(Vector3Int value) => new NeoVector3Int(value);
    }

    internal static class NeoVectorValues
    {
        public static NeoVector2Value FromVector2(Vector2 value)
        {
            return new NeoVector2Value { x = value.x, y = value.y };
        }

        public static NeoVector2Value FromVector2Int(Vector2Int value)
        {
            return new NeoVector2Value { x = value.x, y = value.y };
        }

        public static NeoVector3Value FromVector3(Vector3 value)
        {
            return new NeoVector3Value { x = value.x, y = value.y, z = value.z };
        }

        public static NeoVector3Value FromVector3Int(Vector3Int value)
        {
            return new NeoVector3Value { x = value.x, y = value.y, z = value.z };
        }

        public static Vector2 ReadVector2(NeoMemberVector2 member)
        {
            var value = member.value?.value;
            if (value is null)
            {
                throw new System.InvalidOperationException(
                    $"Required Vector2 '{member.member.name}' has no value.");
            }
            return ToVector2(value);
        }

        public static Vector2Int ReadVector2Int(NeoMemberVector2Int member)
        {
            var value = member.value?.value;
            if (value is null)
            {
                throw new System.InvalidOperationException(
                    $"Required Vector2Int '{member.member.name}' has no value.");
            }
            return ToVector2Int(value);
        }

        public static Vector3 ReadVector3(NeoMemberVector3 member)
        {
            var value = member.value?.value;
            if (value is null)
            {
                throw new System.InvalidOperationException(
                    $"Required Vector3 '{member.member.name}' has no value.");
            }
            return ToVector3(value);
        }

        public static Vector3Int ReadVector3Int(NeoMemberVector3Int member)
        {
            var value = member.value?.value;
            if (value is null)
            {
                throw new System.InvalidOperationException(
                    $"Required Vector3Int '{member.member.name}' has no value.");
            }
            return ToVector3Int(value);
        }

        public static Vector2 ToVector2(NeoVector2Value value)
        {
            return new Vector2(value.x, value.y);
        }

        public static Vector2Int ToVector2Int(NeoVector2Value value)
        {
            return new Vector2Int(ToInt(value.x, "x"), ToInt(value.y, "y"));
        }

        public static Vector3 ToVector3(NeoVector3Value value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        public static Vector3Int ToVector3Int(NeoVector3Value value)
        {
            return new Vector3Int(
                ToInt(value.x, "x"),
                ToInt(value.y, "y"),
                ToInt(value.z, "z"));
        }

        private static int ToInt(float value, string component)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)
                || value < int.MinValue
                || value > int.MaxValue
                || System.Math.Truncate(value) != value)
            {
                throw new System.InvalidOperationException(
                    $"Vector component '{component}' must be an integer.");
            }
            return (int)value;
        }
    }
}
