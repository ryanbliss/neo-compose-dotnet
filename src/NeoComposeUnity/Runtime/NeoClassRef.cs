// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// A strongly typed reference to an authored Neo class. Unlike an
    /// ordinary <see cref="INeoValueReference"/>, this does not identify a
    /// stored value: it selects the class defaults for <typeparamref name="T"/>.
    /// </summary>
    public readonly struct NeoClassRef<T> : IEquatable<NeoClassRef<T>>
        where T : class
    {
        public NeoClassRef(string classId)
        {
            ClassId = string.IsNullOrWhiteSpace(classId)
                ? throw new ArgumentException("Class id cannot be empty.", nameof(classId))
                : classId;
        }

        public string ClassId { get; }

        public bool Equals(NeoClassRef<T> other) =>
            string.Equals(ClassId, other.ClassId, StringComparison.Ordinal);

        public override bool Equals(object? obj) =>
            obj is NeoClassRef<T> other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(ClassId ?? string.Empty);

        public override string ToString() => ClassId ?? string.Empty;

        public static bool operator ==(NeoClassRef<T> left, NeoClassRef<T> right) =>
            left.Equals(right);

        public static bool operator !=(NeoClassRef<T> left, NeoClassRef<T> right) =>
            !left.Equals(right);
    }
}
