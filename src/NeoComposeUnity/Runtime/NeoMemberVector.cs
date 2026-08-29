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
            if (member.EffectiveRequirement == NeoMemberRequirementKind.Required && newValue is null)
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
            if (member.EffectiveRequirement == NeoMemberRequirementKind.Required && newValue is null)
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
            if (member.EffectiveRequirement == NeoMemberRequirementKind.Required && newValue is null)
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
            if (member.EffectiveRequirement == NeoMemberRequirementKind.Required && newValue is null)
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
    /// Read-only Vector2 wrapper. <see cref="Value"/> and the
    /// <see cref="x"/>/<see cref="y"/> components are get-only here, and
    /// whole-value assignment still flows through generated property setters
    /// calling
    /// <see cref="NeoGeneratedTypesSupport.SetVector2(NeoMemberClassWritable, string, NeoReadOnlyVector2)"/>.
    ///
    /// <para>P42 §4.1 overturns specs/color-member.md §6 decisions 5–6, which
    /// made the <em>whole</em> family get-only: the writable
    /// <see cref="NeoVector2"/> now carries write-through component setters.
    /// Read-only misuse is therefore no longer purely a compile error — see
    /// <see cref="NeoVector2"/> for the runtime guard.</para>
    ///
    /// <para>Equality is value-based: property getters mint fresh wrapper
    /// instances, so reference equality would always be false.</para>
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

        public float x => RequireValue(nameof(x)).x;
        public float y => RequireValue(nameof(y)).y;
        public Vector2 Value => memberNode is null
            ? detachedValue
            : NeoVectorValues.ReadVector2(memberNode);

        /// <summary>
        /// <see cref="Value"/> for a read that has one field to blame, so the
        /// no-value message can name it. A detached wrapper always has a value
        /// and never throws here.
        /// </summary>
        private protected Vector2 RequireValue(string field) => memberNode is null
            ? detachedValue
            : NeoVectorValues.ReadVector2(memberNode, field);

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
    /// Writable-context Vector2 wrapper. Adds the native→wrapper implicit
    /// conversion so <c>obj.Position = new Vector2(…);</c> compiles, and —
    /// since P42 §4.1 — settable <see cref="x"/>/<see cref="y"/> components.
    ///
    /// <para><b>Binding decides what a component write means.</b> A wrapper
    /// minted from a member node (what generated getters emit) is
    /// <b>bound</b>: setting a component reads the leaf's current value,
    /// patches the one component, and writes the whole leaf straight back
    /// through the node. A wrapper built from a plain value — the implicit
    /// operator, <c>new NeoVector2(1f, 2f)</c>, a factory argument — is a
    /// <b>detached</b> copy: mutating it is local until it is assigned, and
    /// assignment itself still copies the value rather than creating a live
    /// link.</para>
    ///
    /// <para>Read-only enforcement (P42 decision D5) is a runtime throw, not
    /// a compile error, because a read-only generated instance still hands
    /// out a wrapper over a writable node. Every component setter goes
    /// through <see cref="NeoStructuredLeafWriteGuard"/> first.</para>
    /// </summary>
    public class NeoVector2 : NeoReadOnlyVector2
    {
        private readonly NeoGeneratedClassValue? owner;

        public NeoVector2(Vector2 value)
            : this(value.x, value.y) { }

        public NeoVector2(float x, float y)
            : base(x, y) { }

        public NeoVector2(NeoMemberVector2 member)
            : base(member) { }

        /// <summary>
        /// Bound ctor that also records the owning generated value, so
        /// component setters can honour instance-level read-only
        /// (decision D5). Generated getters on writable families should
        /// prefer this overload.
        /// </summary>
        public NeoVector2(NeoMemberVector2 member, NeoGeneratedClassValue owner)
            : base(member)
        {
            this.owner = owner;
        }

        public new float x
        {
            get => RequireValue(nameof(x)).x;
            set
            {
                Vector2 next = RequireValue(nameof(x));
                next.x = value;
                Write(next, nameof(x));
            }
        }

        public new float y
        {
            get => RequireValue(nameof(y)).y;
            set
            {
                Vector2 next = RequireValue(nameof(y));
                next.y = value;
                Write(next, nameof(y));
            }
        }

        public static implicit operator NeoVector2(Vector2 value) => new NeoVector2(value);

        private void Write(Vector2 next, string field)
        {
            if (memberNode is null)
            {
                detachedValue = next;
                return;
            }

            NeoStructuredLeafWriteGuard
                .RequireWritable<NeoMemberVector2Writable>(
                    owner, memberNode, nameof(NeoVector2), field)
                .Set(next);
        }
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

        public int x => RequireValue(nameof(x)).x;
        public int y => RequireValue(nameof(y)).y;
        public Vector2Int Value => memberNode is null
            ? detachedValue
            : NeoVectorValues.ReadVector2Int(memberNode);

        /// <inheritdoc cref="NeoReadOnlyVector2.RequireValue"/>
        private protected Vector2Int RequireValue(string field) => memberNode is null
            ? detachedValue
            : NeoVectorValues.ReadVector2Int(memberNode, field);

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
    /// Writable-context Vector2Int wrapper — write-through components when
    /// bound, value copy when detached; see <see cref="NeoVector2"/>.
    /// Components are typed <c>int</c>, so the integrality rule
    /// <see cref="NeoVectorValues"/> enforces on read cannot be violated by a
    /// write through this wrapper.
    /// </summary>
    public class NeoVector2Int : NeoReadOnlyVector2Int
    {
        private readonly NeoGeneratedClassValue? owner;

        public NeoVector2Int(Vector2Int value)
            : this(value.x, value.y) { }

        public NeoVector2Int(int x, int y)
            : base(x, y) { }

        public NeoVector2Int(NeoMemberVector2Int member)
            : base(member) { }

        /// <inheritdoc cref="NeoVector2(NeoMemberVector2, NeoGeneratedClassValue)"/>
        public NeoVector2Int(NeoMemberVector2Int member, NeoGeneratedClassValue owner)
            : base(member)
        {
            this.owner = owner;
        }

        public new int x
        {
            get => RequireValue(nameof(x)).x;
            set
            {
                Vector2Int next = RequireValue(nameof(x));
                next.x = value;
                Write(next, nameof(x));
            }
        }

        public new int y
        {
            get => RequireValue(nameof(y)).y;
            set
            {
                Vector2Int next = RequireValue(nameof(y));
                next.y = value;
                Write(next, nameof(y));
            }
        }

        public static implicit operator NeoVector2Int(Vector2Int value) => new NeoVector2Int(value);

        private void Write(Vector2Int next, string field)
        {
            if (memberNode is null)
            {
                detachedValue = next;
                return;
            }

            NeoStructuredLeafWriteGuard
                .RequireWritable<NeoMemberVector2IntWritable>(
                    owner, memberNode, nameof(NeoVector2Int), field)
                .Set(next);
        }
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

        public float x => RequireValue(nameof(x)).x;
        public float y => RequireValue(nameof(y)).y;
        public float z => RequireValue(nameof(z)).z;
        public Vector3 Value => memberNode is null
            ? detachedValue
            : NeoVectorValues.ReadVector3(memberNode);

        /// <inheritdoc cref="NeoReadOnlyVector2.RequireValue"/>
        private protected Vector3 RequireValue(string field) => memberNode is null
            ? detachedValue
            : NeoVectorValues.ReadVector3(memberNode, field);

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
    /// Writable-context Vector3 wrapper — write-through components when
    /// bound, value copy when detached; see <see cref="NeoVector2"/>.
    /// </summary>
    public class NeoVector3 : NeoReadOnlyVector3
    {
        private readonly NeoGeneratedClassValue? owner;

        public NeoVector3(Vector3 value)
            : this(value.x, value.y, value.z) { }

        public NeoVector3(float x, float y, float z)
            : base(x, y, z) { }

        public NeoVector3(NeoMemberVector3 member)
            : base(member) { }

        /// <inheritdoc cref="NeoVector2(NeoMemberVector2, NeoGeneratedClassValue)"/>
        public NeoVector3(NeoMemberVector3 member, NeoGeneratedClassValue owner)
            : base(member)
        {
            this.owner = owner;
        }

        public new float x
        {
            get => RequireValue(nameof(x)).x;
            set
            {
                Vector3 next = RequireValue(nameof(x));
                next.x = value;
                Write(next, nameof(x));
            }
        }

        public new float y
        {
            get => RequireValue(nameof(y)).y;
            set
            {
                Vector3 next = RequireValue(nameof(y));
                next.y = value;
                Write(next, nameof(y));
            }
        }

        public new float z
        {
            get => RequireValue(nameof(z)).z;
            set
            {
                Vector3 next = RequireValue(nameof(z));
                next.z = value;
                Write(next, nameof(z));
            }
        }

        public static implicit operator NeoVector3(Vector3 value) => new NeoVector3(value);

        private void Write(Vector3 next, string field)
        {
            if (memberNode is null)
            {
                detachedValue = next;
                return;
            }

            NeoStructuredLeafWriteGuard
                .RequireWritable<NeoMemberVector3Writable>(
                    owner, memberNode, nameof(NeoVector3), field)
                .Set(next);
        }
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

        public int x => RequireValue(nameof(x)).x;
        public int y => RequireValue(nameof(y)).y;
        public int z => RequireValue(nameof(z)).z;
        public Vector3Int Value => memberNode is null
            ? detachedValue
            : NeoVectorValues.ReadVector3Int(memberNode);

        /// <inheritdoc cref="NeoReadOnlyVector2.RequireValue"/>
        private protected Vector3Int RequireValue(string field) => memberNode is null
            ? detachedValue
            : NeoVectorValues.ReadVector3Int(memberNode, field);

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
    /// Writable-context Vector3Int wrapper — write-through components when
    /// bound, value copy when detached; see <see cref="NeoVector2"/> and
    /// <see cref="NeoVector2Int"/>.
    /// </summary>
    public class NeoVector3Int : NeoReadOnlyVector3Int
    {
        private readonly NeoGeneratedClassValue? owner;

        public NeoVector3Int(Vector3Int value)
            : this(value.x, value.y, value.z) { }

        public NeoVector3Int(int x, int y, int z)
            : base(x, y, z) { }

        public NeoVector3Int(NeoMemberVector3Int member)
            : base(member) { }

        /// <inheritdoc cref="NeoVector2(NeoMemberVector2, NeoGeneratedClassValue)"/>
        public NeoVector3Int(NeoMemberVector3Int member, NeoGeneratedClassValue owner)
            : base(member)
        {
            this.owner = owner;
        }

        public new int x
        {
            get => RequireValue(nameof(x)).x;
            set
            {
                Vector3Int next = RequireValue(nameof(x));
                next.x = value;
                Write(next, nameof(x));
            }
        }

        public new int y
        {
            get => RequireValue(nameof(y)).y;
            set
            {
                Vector3Int next = RequireValue(nameof(y));
                next.y = value;
                Write(next, nameof(y));
            }
        }

        public new int z
        {
            get => RequireValue(nameof(z)).z;
            set
            {
                Vector3Int next = RequireValue(nameof(z));
                next.z = value;
                Write(next, nameof(z));
            }
        }

        public static implicit operator NeoVector3Int(Vector3Int value) => new NeoVector3Int(value);

        private void Write(Vector3Int next, string field)
        {
            if (memberNode is null)
            {
                detachedValue = next;
                return;
            }

            NeoStructuredLeafWriteGuard
                .RequireWritable<NeoMemberVector3IntWritable>(
                    owner, memberNode, nameof(NeoVector3Int), field)
                .Set(next);
        }
    }

    /// <summary>
    /// Shared read-only gate for the P42 write-through field setters on the
    /// structured-leaf wrappers (<see cref="NeoVector2"/> and friends,
    /// <see cref="NeoColor"/>, <see cref="NeoSprite"/>).
    ///
    /// <para>Decision D5: before P42 no wrapper had a mutation API, so
    /// "read-only misuse is a compile error" held for free. It no longer
    /// does — <c>NeoGeneratedClassValue.writableNode</c> is materialized
    /// without consulting <see cref="NeoGeneratedClassValue.IsReadOnly"/>, so
    /// a read-only generated instance can hand out a wrapper over a writable
    /// node. Two signals are therefore checked, in order:</para>
    /// <list type="number">
    ///   <item><description>the owning generated value's
    ///   <see cref="NeoGeneratedClassValue.IsReadOnly"/>, when the wrapper was
    ///   minted with the owner-carrying bound ctor;</description></item>
    ///   <item><description>the bound node's own writability — a node that is
    ///   not the <c>*Writable</c> kind has no public <c>Set</c> and must never
    ///   be mutated.</description></item>
    /// </list>
    /// <para>Signal 1 is only as good as the call site: generated getters that
    /// still use the single-argument bound ctor fall back to signal 2 alone.
    /// The complementary half of D5 is codegen returning the
    /// <c>NeoReadOnly*</c> wrapper on the read-only family.</para>
    /// <para>That call-site obligation covers EVERY path that hands out a
    /// bound writable-family wrapper, not only the direct property. A
    /// generated collection getter reads its node through
    /// <c>writableNode</c> too, so on a read-only instance its element
    /// children are still the <c>*Writable</c> kind and signal 2 says
    /// "writable" — leaving signal 1 as the only guard. A collection-element
    /// factory that omits the owner reopens exactly the hole the direct
    /// property closes, and does it invisibly, because the property next to it
    /// throws. Codegen therefore passes the owner from the element factory as
    /// well; see `childConverter` in the web repo's
    /// `generate-unity-classes.ts`.</para>
    /// </summary>
    internal static class NeoStructuredLeafWriteGuard
    {
        /// <summary>
        /// Throws unless <paramref name="node"/> may be mutated right now;
        /// otherwise returns it as the writable node kind.
        /// </summary>
        public static TWritable RequireWritable<TWritable>(
            NeoGeneratedClassValue? owner,
            NeoMember node,
            string wrapperName,
            string field)
            where TWritable : NeoMember
        {
            if (owner is not null && owner.IsReadOnly)
            {
                throw new System.InvalidOperationException(
                    $"Cannot write field '{field}' on {wrapperName} because the owning "
                    + $"{owner.GetType().Name} value is read-only.");
            }

            if (node is not TWritable writable)
            {
                throw new System.InvalidOperationException(
                    $"Cannot write field '{field}' on {wrapperName} because the bound Neo "
                    + $"member '{node.member.name}' is read-only.");
            }

            return writable;
        }
    }

    /// <summary>
    /// Shared "this leaf has nothing to read" reporting for the structured-leaf
    /// wrappers (<see cref="NeoReadOnlyVector2"/> and friends,
    /// <see cref="NeoReadOnlyColor"/>, <see cref="NeoReadOnlySprite"/>).
    ///
    /// <para>One message shape per condition. It deliberately does <b>not</b>
    /// claim the member is required: these wrappers are handed out for optional
    /// members too, and "Required Color 'Glow' has no value." was simply false
    /// on the optional ones. "Has no value" is the actionable part, and naming
    /// the field being read is what tells the author which accessor tripped
    /// it.</para>
    /// </summary>
    internal static class NeoStructuredLeafReadGuard
    {
        /// <summary>
        /// <paramref name="field"/> is the component/channel/field being read
        /// (<c>x</c>, <c>a</c>, <c>SliceIndex</c>); null for a whole-value read,
        /// which has no one field to name.
        /// </summary>
        public static System.InvalidOperationException MissingValue(
            string typeName,
            string memberName,
            string? field)
        {
            return new System.InvalidOperationException(
                field is null
                    ? $"{typeName} '{memberName}' has no value."
                    : $"Cannot read '{field}': {typeName} '{memberName}' has no value.");
        }
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

        /// <summary>
        /// <paramref name="field"/> names the component being read, for the
        /// message a component accessor raises when the leaf has no value; a
        /// whole-value read leaves it null. See
        /// <see cref="NeoStructuredLeafReadGuard"/>.
        /// </summary>
        public static Vector2 ReadVector2(NeoMemberVector2 member, string? field = null)
        {
            var value = member.value?.value;
            if (value is null)
            {
                throw NeoStructuredLeafReadGuard.MissingValue(
                    "Vector2", member.member.name, field);
            }
            return ToVector2(value);
        }

        /// <inheritdoc cref="ReadVector2(NeoMemberVector2, string?)"/>
        public static Vector2Int ReadVector2Int(NeoMemberVector2Int member, string? field = null)
        {
            var value = member.value?.value;
            if (value is null)
            {
                throw NeoStructuredLeafReadGuard.MissingValue(
                    "Vector2Int", member.member.name, field);
            }
            return ToVector2Int(value);
        }

        /// <inheritdoc cref="ReadVector2(NeoMemberVector2, string?)"/>
        public static Vector3 ReadVector3(NeoMemberVector3 member, string? field = null)
        {
            var value = member.value?.value;
            if (value is null)
            {
                throw NeoStructuredLeafReadGuard.MissingValue(
                    "Vector3", member.member.name, field);
            }
            return ToVector3(value);
        }

        /// <inheritdoc cref="ReadVector2(NeoMemberVector2, string?)"/>
        public static Vector3Int ReadVector3Int(NeoMemberVector3Int member, string? field = null)
        {
            var value = member.value?.value;
            if (value is null)
            {
                throw NeoStructuredLeafReadGuard.MissingValue(
                    "Vector3Int", member.member.name, field);
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
