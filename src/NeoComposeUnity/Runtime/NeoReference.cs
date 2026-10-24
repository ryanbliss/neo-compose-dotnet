// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// P112 §8. A reference to a value slot, such as a stat, a vector, or a
    /// list entry. Reads, writes and rebinds follow NeoScript's rules, because
    /// they run through its executor.
    /// </summary>
    public abstract class NeoReference
    {
        internal readonly NeoClient client;

        // The reference member this was read from, and its row, which binds a generic target.
        private readonly LookupMember? holder;
        private readonly string? holderRowId;

        // ReferenceTo's owner. A slot without a row yet reads through it (§2.2).
        private readonly NeoGeneratedClassValue? owner;
        private readonly string? key;

        private protected NeoReference(NeoClient client, string targetId, LookupMember? holder, string? holderRowId)
        {
            this.client = client;
            TargetId = targetId;
            this.holder = holder;
            this.holderRowId = holderRowId;
        }

        private protected NeoReference(NeoClient client, NeoGeneratedClassValue owner, string key, string targetId)
        {
            this.client = client;
            TargetId = targetId;
            this.owner = owner;
            this.key = key;
        }

        /// <summary>The id of the row this reference names.</summary>
        public string TargetId
        {
            get;
        }

        /// <summary>This reference as the evaluator holds it.</summary>
        internal LookupMember? Holder => holder;

        internal NSSlotReference Slot(NSGetterEvaluator.Context ctx) => Slot(ctx, out _);

        internal NSSlotReference Slot(NSGetterEvaluator.Context ctx, out TypeInfo? target)
        {
            if (owner is not null)
            {
                var reference = (NSSlotReference)NSGetterEvaluator.ReferenceToKey(owner, key!, ctx)!;
                target = reference.Target;
                return reference;
            }
            target = holder is null ? null : NSGetterEvaluator.LookupTargetForRow(holder, ctx, holderRowId);
            TypeInfo? slotTarget = target is not null && NeoReferenceTypes.Category(target, client) == NeoReferenceCategory.Slot ? target : null;
            return new NSSlotReference(TargetId, null, slotTarget, holder?.id);
        }

    }

    /// <summary>
    /// P112 §8. A reference to a <typeparamref name="T"/> slot. Two references
    /// are equal when they name the same row.
    /// </summary>
    public sealed class NeoReference<T> : NeoReference, IEquatable<NeoReference<T>>
    {
        internal NeoReference(NeoClient client, string targetId, LookupMember? holder, string? holderRowId)
            : base(client, targetId, holder, holderRowId)
        {
        }

        internal NeoReference(NeoClient client, NeoGeneratedClassValue owner, string key, string targetId)
            : base(client, owner, key, targetId)
        {
        }

        /// <summary>
        /// The target's value. Throws when the target no longer exists, or is
        /// not a <typeparamref name="T"/> (§4.3, §4.4). Setting it writes the
        /// target, and also throws when the target is readonly or its store takes no writes.
        /// </summary>
        public T Value
        {
            get => NeoScriptExecutor.ReadReference<T>(client, this);
            set => NeoScriptExecutor.WriteReference(client, this, value);
        }

        /// <summary>
        /// Sets <see cref="Value"/>, or returns false when the target is
        /// readonly or its store takes no writes.
        /// </summary>
        public bool TrySet(T value) => NeoScriptExecutor.TrySetReference(client, this, value);

        public bool Equals(NeoReference<T>? other) => other is not null && TargetId == other.TargetId;

        public override bool Equals(object? obj) => obj is NeoReference<T> other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(TargetId);

        public override string ToString() => $"NeoReference<{typeof(T).Name}>({TargetId})";
    }
}
