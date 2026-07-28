// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Read-only user-facing Sprite wrapper (P42 §4.1). Sprite is the one
    /// structured leaf that had no wrapper pair — its generated property
    /// projected straight to <see cref="UnityEngine.Sprite"/> — so both halves
    /// of the pair are new here; the shape deliberately mirrors
    /// <see cref="NeoReadOnlyVector3"/> and <see cref="NeoReadOnlyColor"/>.
    ///
    /// <para>The addressable fields are <see cref="FileId"/> and
    /// <see cref="SliceIndex"/> (P42 decision D11 — Sprite's author-facing
    /// field names are PascalCase on every public surface, while the stored
    /// record keys stay <c>fileId</c>/<c>sliceIndex</c>). <see cref="Value"/>
    /// exposes the pair together as the wire DTO — which therefore keeps the
    /// lowercase stored keys — always as a copy so a caller cannot reach
    /// through and mutate the live row. <see cref="Resolve()"/> turns them
    /// into a <see cref="UnityEngine.Sprite"/> through the synchronized
    /// <see cref="NeoAssetDatabase"/> — for a bound instance by delegating to
    /// <see cref="NeoMemberSprite.Resolve"/>, which is the single resolution
    /// path in the SDK.</para>
    ///
    /// <para>A detached instance comes in two flavours. Built from
    /// <c>fileId</c>/<c>sliceIndex</c> it carries the addressable value and
    /// resolves through the database like a bound one. Built from a
    /// <see cref="UnityEngine.Sprite"/> — the implicit operator, i.e.
    /// <c>obj.Sprite = someUnitySprite;</c> — it carries the sprite itself and
    /// only reverse-resolves to a <c>fileId</c> when something actually asks
    /// for one. Reverse resolution is deliberately <em>not</em> done eagerly
    /// in the conversion: the generated setter reverse-resolves at write time
    /// with the member's <c>templateId</c>, and doing it twice would either
    /// duplicate or skip that template validation.</para>
    ///
    /// <para>Equality is value-based over <c>(fileId, sliceIndex)</c>, as
    /// elsewhere in the wrapper family, because property getters mint fresh
    /// instances. Only the wrapper/wrapper operators are declared: unlike
    /// <c>Vector3</c> and <c>Color</c>, <see cref="UnityEngine.Sprite"/> is a
    /// reference type, so mixed wrapper/native <c>==</c> overloads would make
    /// <c>wrapper == null</c> ambiguous.</para>
    /// </summary>
    public class NeoReadOnlySprite
    {
        protected readonly NeoMemberSprite? memberNode;
        protected SpriteValue? detachedValue;
        protected readonly Sprite? detachedSprite;

        /// <summary>
        /// True when this instance was detached from a
        /// <see cref="UnityEngine.Sprite"/> rather than from an addressable
        /// value, so <see cref="detachedValue"/> is a reverse-resolution cache
        /// rather than the source of truth.
        /// </summary>
        protected readonly bool detachedFromSprite;

        /// <summary>Detached ctor — carries a Unity sprite (assignment).</summary>
        public NeoReadOnlySprite(Sprite? value)
        {
            detachedSprite = value;
            detachedFromSprite = true;
        }

        /// <summary>Detached ctor — carries an addressable value.</summary>
        public NeoReadOnlySprite(string fileId, int sliceIndex)
        {
            detachedValue = new SpriteValue { fileId = fileId, sliceIndex = sliceIndex };
        }

        /// <summary>Bound ctor — emitted by generated getters.</summary>
        public NeoReadOnlySprite(NeoMemberSprite member)
        {
            memberNode = member;
        }

        /// <summary>
        /// The leaf's current addressable value, or null when the member has
        /// no value (or when a detached Unity sprite is not tracked by the
        /// asset database). Always a fresh copy — the returned DTO is a plain
        /// mutable record and mutating it must not reach the stored row.
        /// </summary>
        public SpriteValue? Value => Copy(CurrentValue());

        /// <summary>
        /// The file this sprite addresses. Throws when the leaf has no value,
        /// matching <c>NeoVectorValues.ReadVector3</c> / <c>ReadColor</c>.
        /// </summary>
        public string FileId => RequireValue(nameof(FileId)).fileId;

        /// <summary>
        /// The slice this sprite addresses within its file (P42 §1.1);
        /// single-sprite textures use 0. An index past the end of the file's
        /// slices is not validated here — §1.4 makes that a runtime skip at
        /// resolution time, and <see cref="Resolve()"/> already returns null
        /// for one. A <em>negative</em> index is a different matter and is
        /// rejected by the writable setter; see
        /// <see cref="NeoSpriteValues.RequireSliceIndex"/>.
        /// </summary>
        public int SliceIndex => RequireValue(nameof(SliceIndex)).sliceIndex;

        /// <summary>
        /// Resolves the addressable value through the synchronized
        /// <see cref="NeoAssetDatabase"/>. Null when the member has no value,
        /// when the file is not in the database, or when
        /// <see cref="SliceIndex"/> is out of range for the file's slices.
        ///
        /// <para>For a required member with no synchronized asset this throws
        /// instead — P42 §4.2 moves that throw off the generated getter and
        /// onto <c>Resolve()</c> and the implicit
        /// <c>NeoSprite → UnityEngine.Sprite</c> conversion, so that reading
        /// <c>obj.Sprite.SliceIndex</c> on an unsynchronized asset does not
        /// throw.</para>
        /// </summary>
        public Sprite? Resolve()
        {
            var resolved = ResolveOrNull();
            if (resolved == null && memberNode is not null && memberNode.member.required)
            {
                throw new System.InvalidOperationException(
                    $"Required Sprite '{memberNode.member.name}' has no synchronized asset.");
            }
            return resolved;
        }

        /// <summary>
        /// <see cref="Resolve()"/> without the required-member throw — the
        /// same resolution, reporting "nothing to show" as null.
        ///
        /// <para>This exists for best-effort <em>discovery</em> callers, which
        /// inspect whatever sprite-ish members a value happens to expose and
        /// move on to the next candidate when one yields nothing
        /// (<see cref="NeoTileAssetFactory.ResolveSprite"/> is the caller that
        /// motivated it). For those, a required member whose asset is not
        /// synchronized is one more empty candidate, not an error to raise at
        /// the caller — the same way the scan already tolerates a property it
        /// cannot read. A <see cref="Value"/> null-check alone does not cover
        /// this: the required throw fires when a row IS present and its file
        /// simply is not in the asset database, so <see cref="Value"/> is
        /// non-null in exactly the throwing case.</para>
        ///
        /// <para>Deliberately not public. Ordinary reads must keep going
        /// through <see cref="Resolve()"/> so the required-member contract
        /// (P42 §4.2) stays loud everywhere an author can see it.</para>
        /// </summary>
        internal Sprite? ResolveOrNull()
        {
            if (memberNode is not null) return memberNode.Resolve();

            // A wrapper detached from a Unity sprite resolves back to that
            // sprite — until it acquires an addressable value, either by
            // reverse-resolving one (which round-trips to the same sprite) or
            // by having a field written on it, at which point the addressable
            // value is what the caller asked for.
            if (detachedFromSprite && detachedValue is null) return detachedSprite;
            return NeoAssetResolver.ResolveSprite(null, detachedValue);
        }

        public static implicit operator Sprite?(NeoReadOnlySprite value) => value.Resolve();

        // Value-based equality over the addressable fields. Mixed
        // wrapper/native overloads are deliberately absent — see the type
        // doc.
        public static bool operator ==(NeoReadOnlySprite? left, NeoReadOnlySprite? right)
        {
            if (left is null) return right is null;
            if (right is null) return false;
            return SameValue(left.CurrentValue(), right.CurrentValue());
        }

        public static bool operator !=(NeoReadOnlySprite? left, NeoReadOnlySprite? right)
            => !(left == right);

        public override bool Equals(object? obj)
        {
            if (obj is NeoReadOnlySprite wrapper)
            {
                return SameValue(CurrentValue(), wrapper.CurrentValue());
            }
            return false;
        }

        public override int GetHashCode()
        {
            var value = CurrentValue();
            if (value is null) return 0;
            return System.HashCode.Combine(value.fileId, value.sliceIndex);
        }

        /// <summary>
        /// The live addressable value without copying — for internal reads
        /// only. Never hand this instance to a caller.
        /// </summary>
        private protected SpriteValue? CurrentValue()
        {
            if (memberNode is not null) return memberNode.value?.value;
            if (!detachedFromSprite) return detachedValue;

            // Reverse-resolve the detached sprite once, on demand. An
            // untracked sprite has no addressable value; that is reported as
            // "no value" rather than as an exception, because the write path
            // (NeoGeneratedTypesSupport.SpriteValue) is where an untracked
            // sprite is meant to be rejected, with the member's templateId in
            // hand.
            if (detachedValue is not null) return detachedValue;
            if (detachedSprite == null) return null;
            var database = NeoAssetDatabase.LoadDefault();
            int sliceIndex = -1;
            var entry = database?.TryGetEntryForSprite(detachedSprite, out sliceIndex);
            if (entry is null) return null;
            detachedValue = new SpriteValue
            {
                fileId = entry.FileId,
                sliceIndex = sliceIndex,
            };
            return detachedValue;
        }

        private protected SpriteValue RequireValue(string field)
        {
            var value = CurrentValue();
            if (value is null)
            {
                throw NeoStructuredLeafReadGuard.MissingValue(
                    "Sprite", memberNode?.member.name ?? "Sprite", field);
            }
            return value;
        }

        private static SpriteValue? Copy(SpriteValue? value)
        {
            return value is null
                ? null
                : new SpriteValue { fileId = value.fileId, sliceIndex = value.sliceIndex };
        }

        private static bool SameValue(SpriteValue? left, SpriteValue? right)
        {
            if (left is null) return right is null;
            if (right is null) return false;
            return left.sliceIndex == right.sliceIndex
                && string.Equals(left.fileId, right.fileId, System.StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Writable-context Sprite wrapper. Adds the native→wrapper implicit
    /// conversion so <c>obj.Sprite = someUnitySprite;</c> compiles once the
    /// generated property type moves from <see cref="UnityEngine.Sprite"/> to
    /// this type, and — P42 §4.1 — settable <see cref="FileId"/> and
    /// <see cref="SliceIndex"/>.
    ///
    /// <para><b>Binding decides what a field write means.</b> A wrapper minted
    /// from a member node (what generated getters emit) is <b>bound</b>:
    /// setting a field reads the leaf's current value, patches the one field,
    /// and writes the whole leaf straight back through
    /// <see cref="NeoMemberSpriteWritable.Set(SpriteValue)"/> — the §1.2
    /// read-modify-write. A wrapper built from a plain value is a
    /// <b>detached</b> copy: mutating it is local until it is assigned.</para>
    ///
    /// <para>Read-only enforcement (decision D5) is a runtime throw via
    /// <see cref="NeoStructuredLeafWriteGuard"/>, not a compile error.</para>
    /// </summary>
    public class NeoSprite : NeoReadOnlySprite
    {
        private readonly NeoGeneratedClassValue? owner;

        /// <summary>Bound ctor — emitted by generated getters.</summary>
        public NeoSprite(NeoMemberSprite member)
            : base(member) { }

        /// <inheritdoc cref="NeoVector2(NeoMemberVector2, NeoGeneratedClassValue)"/>
        public NeoSprite(NeoMemberSprite member, NeoGeneratedClassValue owner)
            : base(member)
        {
            this.owner = owner;
        }

        /// <summary>Detached ctor — carries a Unity sprite (assignment).</summary>
        public NeoSprite(Sprite? value)
            : base(value) { }

        /// <summary>Detached ctor — carries an addressable value.</summary>
        public NeoSprite(string fileId, int sliceIndex)
            : base(fileId, sliceIndex) { }

        public new string FileId
        {
            get => base.FileId;
            set
            {
                if (value is null) throw new System.ArgumentNullException(nameof(value));
                var next = RequireValue(nameof(FileId));
                Write(new SpriteValue { fileId = value, sliceIndex = next.sliceIndex }, nameof(FileId));
            }
        }

        public new int SliceIndex
        {
            get => base.SliceIndex;
            set
            {
                var next = RequireValue(nameof(SliceIndex));
                Write(
                    new SpriteValue
                    {
                        fileId = next.fileId,
                        sliceIndex = NeoSpriteValues.RequireSliceIndex(value),
                    },
                    nameof(SliceIndex));
            }
        }

        public static implicit operator NeoSprite(Sprite? value) => new NeoSprite(value);

        private void Write(SpriteValue next, string field)
        {
            if (memberNode is null)
            {
                // Detached: the mutation is local. A wrapper detached from a
                // Unity sprite becomes value-carrying from here on — its
                // reverse-resolution cache is now the authored value.
                detachedValue = next;
                return;
            }

            NeoStructuredLeafWriteGuard
                .RequireWritable<NeoMemberSpriteWritable>(
                    owner, memberNode, nameof(NeoSprite), field)
                .Set(next);
        }
    }

    internal static class NeoSpriteValues
    {
        /// <summary>
        /// A slice index addresses a slice of the imported texture, so the
        /// smallest legal value is 0. Negative indices are <b>rejected</b> at
        /// the setter, the same way <see cref="NeoColorValues.RequireChannel"/>
        /// rejects a channel outside <c>[0, 1]</c>.
        ///
        /// <para>This closes a hole rather than adding a rule: NeoScript's
        /// field-assign path already refuses a negative <c>sliceIndex</c>
        /// ("Sprite field 'sliceIndex' must be 0 or greater"), and the
        /// animation apply path skips one. Only the C# wrapper wrote it
        /// through — producing a row that no resolver can ever turn into a
        /// sprite and that nothing anywhere reports.</para>
        ///
        /// <para>The upper bound is deliberately not checked: how many slices
        /// a file has is known only to a synchronized
        /// <see cref="NeoAssetDatabase"/>, so §1.4 keeps that a resolution-time
        /// null rather than a write-time throw.</para>
        /// </summary>
        public static int RequireSliceIndex(int value)
        {
            if (value < 0)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(value),
                    $"Sprite field '{nameof(NeoSprite.SliceIndex)}' must be 0 or greater.");
            }
            return value;
        }
    }
}
