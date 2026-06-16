// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Storage classes for attribute values (specs/attribute-storage.md).
    /// Mirrors the TS-side <c>AttributeStorage</c> enum; the wire values are
    /// the lowercase strings "inherit" / "static" / "save" / "session".
    /// </summary>
    public enum NeoAttributeStorage
    {
        /// <summary>No declared class — the placement parent decides.</summary>
        Inherit,
        /// <summary>Authored value only; read-only at runtime.</summary>
        Static,
        /// <summary>Writable; persisted in the save-file overlay.</summary>
        Save,
        /// <summary>Writable; in-memory only, resets each session.</summary>
        Session,
    }

    public static class NeoAttributeStorageResolution
    {
        /// <summary>
        /// Parses the wire string form. Absent (null) is
        /// <see cref="NeoAttributeStorage.Inherit"/>; unknown strings throw
        /// so a future storage class fails loud instead of silently reading
        /// as static.
        /// </summary>
        public static NeoAttributeStorage Parse(string? wire)
        {
            switch (wire)
            {
                case null:
                case "inherit":
                    return NeoAttributeStorage.Inherit;
                case "static":
                    return NeoAttributeStorage.Static;
                case "save":
                    return NeoAttributeStorage.Save;
                case "session":
                    return NeoAttributeStorage.Session;
                default:
                    throw new System.InvalidOperationException(
                        $"Unknown attribute storage class '{wire}'.");
            }
        }

        /// <summary>
        /// Maps a concrete storage class onto the value-ownership vocabulary.
        /// <see cref="NeoAttributeStorage.Inherit"/> maps to null — the
        /// placement context decides.
        /// </summary>
        public static NeoValueOwnership? ToOwnership(NeoAttributeStorage storage)
        {
            switch (storage)
            {
                case NeoAttributeStorage.Inherit:
                    return null;
                case NeoAttributeStorage.Static:
                    return NeoValueOwnership.Asset;
                case NeoAttributeStorage.Save:
                    return NeoValueOwnership.Save;
                case NeoAttributeStorage.Session:
                    return NeoValueOwnership.Session;
                default:
                    throw new System.InvalidOperationException(
                        $"Unknown attribute storage '{storage}'.");
            }
        }
    }
}
