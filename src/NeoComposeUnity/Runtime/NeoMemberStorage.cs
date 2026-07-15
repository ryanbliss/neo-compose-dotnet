// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Storage classes for member values (specs/member-storage.md).
    /// Mirrors the TS-side <c>MemberStorage</c> enum; the wire values are
    /// the lowercase strings "inherit" / "immutable" / "save" / "session".
    /// </summary>
    public enum NeoMemberStorage
    {
        /// <summary>No declared class — the placement parent decides.</summary>
        Inherit,
        /// <summary>Authored value only; read-only at runtime.</summary>
        Immutable,
        /// <summary>Writable; persisted in the save-file overlay.</summary>
        Save,
        /// <summary>Writable; in-memory only, resets each session.</summary>
        Session,
    }

    public static class NeoMemberStorageResolution
    {
        /// <summary>
        /// Parses the wire string form. Absent (null) is
        /// <see cref="NeoMemberStorage.Inherit"/>; unknown strings throw
        /// so a future storage class fails loud instead of silently reading
        /// as immutable.
        /// </summary>
        public static NeoMemberStorage Parse(string? wire)
        {
            switch (wire)
            {
                case null:
                case "inherit":
                    return NeoMemberStorage.Inherit;
                case "immutable":
                    return NeoMemberStorage.Immutable;
                case "save":
                    return NeoMemberStorage.Save;
                case "session":
                    return NeoMemberStorage.Session;
                default:
                    throw new System.InvalidOperationException(
                        $"Unknown member storage class '{wire}'.");
            }
        }

        /// <summary>
        /// Maps a concrete storage class onto the value-ownership vocabulary.
        /// <see cref="NeoMemberStorage.Inherit"/> maps to null — the
        /// placement context decides.
        /// </summary>
        public static NeoValueOwnership? ToOwnership(NeoMemberStorage storage)
        {
            switch (storage)
            {
                case NeoMemberStorage.Inherit:
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
    }
}
