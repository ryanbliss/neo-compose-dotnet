// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Storage classes for member values (specs/member-storage.md).
    /// Mirrors the TS-side <c>MemberStorageKind</c> enum. Persisted ordinals
    /// are append-only and zero may be omitted on the wire.
    /// </summary>
    public enum NeoMemberStorage
    {
        /// <summary>No declared class — the placement parent decides.</summary>
        Inherit = 0,
        /// <summary>Authored value only; read-only at runtime.</summary>
        Immutable = 1,
        /// <summary>Writable; persisted in the save-file overlay.</summary>
        Save = 2,
        /// <summary>Writable; in-memory only, resets each session.</summary>
        Session = 3,
        /// <summary>
        /// Always writable; Save under a Save parent and Session otherwise
        /// (P93 §1.2). Declared-only: never a resolved ownership.
        /// </summary>
        Writable = 4,
    }

    public static class NeoMemberStorageResolution
    {
        /// <summary>
        /// Validates a persisted storage ordinal. The project reader rejects
        /// strings before Newtonsoft can coerce an enum name.
        /// </summary>
        public static NeoMemberStorage Validate(NeoMemberStorage value)
        {
            switch (value)
            {
                case NeoMemberStorage.Inherit:
                case NeoMemberStorage.Immutable:
                case NeoMemberStorage.Save:
                case NeoMemberStorage.Session:
                case NeoMemberStorage.Writable:
                    return value;
                default:
                    throw new System.InvalidOperationException(
                        $"Unknown member storage ordinal '{(int)value}'.");
            }
        }

        /// <summary>
        /// Maps a concrete storage class onto the value-ownership vocabulary.
        /// <see cref="NeoMemberStorage.Inherit"/> and
        /// <see cref="NeoMemberStorage.Writable"/> map to null — the
        /// placement context decides.
        /// </summary>
        public static NeoValueOwnership? ToOwnership(NeoMemberStorage storage)
        {
            switch (storage)
            {
                case NeoMemberStorage.Inherit:
                case NeoMemberStorage.Writable:
                    return null;
                case NeoMemberStorage.Immutable:
                    return NeoValueOwnership.Asset;
                case NeoMemberStorage.Save:
                    return NeoValueOwnership.Save;
                case NeoMemberStorage.Session:
                    return NeoValueOwnership.Session;
                default:
                    throw new System.InvalidOperationException(
                        $"Unknown member storage '{storage}'.");
            }
        }

        /// <summary>
        /// The ownership of a value placed under a parent (P93 §1.2, Asset is
        /// Immutable). Mirrors the TS-side <c>childEffectiveStorage</c>.
        /// </summary>
        internal static NeoValueOwnership ChildOwnership(
            NeoMemberStorage declared,
            NeoValueOwnership parent)
        {
            switch (declared)
            {
                case NeoMemberStorage.Inherit:
                    return parent;
                case NeoMemberStorage.Writable:
                    return parent == NeoValueOwnership.Save
                        ? NeoValueOwnership.Save
                        : NeoValueOwnership.Session;
                default:
                    return ToOwnership(declared)!.Value;
            }
        }
    }
}
