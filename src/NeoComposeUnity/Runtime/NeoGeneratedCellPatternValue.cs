// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>Stored offsets of a generated NeoCellPattern, in pattern order.</summary>
    public interface INeoCellPattern : IReadOnlyList<Vector2Int>
    {
    }

    /// <summary>
    /// Base of the generated system class <c>NeoCellPattern</c>, so a pattern
    /// reads as its offsets without walking rows per access.
    /// </summary>
    public abstract class NeoGeneratedCellPatternValue : NeoGeneratedClassValue, INeoCellPattern
    {
        // The pattern row's node, kept so a repeated read looks its id up once.
        private NeoValueNode? rowNode;

        protected NeoGeneratedCellPatternValue(
            NeoClient client,
            NeoMemberClass node,
            string fallbackClassId,
            bool isReadOnly = true,
            NeoValueOwnership inheritedStorageOwnership = NeoValueOwnership.Asset)
            : base(client, node, fallbackClassId, isReadOnly, inheritedStorageOwnership)
        {
        }

        protected NeoGeneratedCellPatternValue(NeoClient client, NeoDetachedValue value, bool isReadOnly)
            : base(client, value, isReadOnly)
        {
        }

        internal Vector2Int[] Offsets => NeoCellPatternOffsets.Read(this, ref rowNode);

        // Explicit, so the generated NeoScript Count keeps its ordinary meaning.
        int IReadOnlyCollection<Vector2Int>.Count => Offsets.Length;

        Vector2Int IReadOnlyList<Vector2Int>.this[int index] => Offsets[index];

        public OffsetEnumerator GetEnumerator() => new(Offsets);

        IEnumerator<Vector2Int> IEnumerable<Vector2Int>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Enumerates the offsets read when enumeration began.</summary>
        public struct OffsetEnumerator : IEnumerator<Vector2Int>
        {
            private readonly Vector2Int[] offsets;
            private int index;

            internal OffsetEnumerator(Vector2Int[] offsets)
            {
                this.offsets = offsets;
                index = -1;
            }

            public Vector2Int Current => offsets[index];

            object IEnumerator.Current => Current;

            public bool MoveNext() => ++index < offsets.Length;

            public void Reset() => index = -1;

            public void Dispose()
            {
            }
        }
    }
}
