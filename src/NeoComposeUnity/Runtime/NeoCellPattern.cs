// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// An immutable set of cell offsets relative to an origin — a reusable query
    /// shape for tile grid lookups (interaction reach, areas of effect, footprints).
    /// Translate the pattern at query time with <see cref="GetCells"/>, or pass it
    /// to the pattern-aware lookup extensions on layers and grid content.
    /// </summary>
    /// <remarks>
    /// Factory-built patterns (<see cref="Box(int,int,bool)"/>, <see cref="Cross(int,int,bool)"/>,
    /// <see cref="Ring"/>, <see cref="Rect"/>, <see cref="Line"/>) order their offsets
    /// center-out (ascending Chebyshev distance, ties preserving row-major generation
    /// order), so "first match" queries mean "nearest match". Hand-built patterns
    /// preserve the caller's offset order untouched, so match priority stays
    /// controllable.
    /// </remarks>
    public sealed class NeoCellPattern : IReadOnlyList<Vector2Int>
    {
        private readonly Vector2Int[] offsets;

        public NeoCellPattern(params Vector2Int[] offsets)
        {
            if (offsets is null) throw new ArgumentNullException(nameof(offsets));
            this.offsets = (Vector2Int[])offsets.Clone();
        }

        public NeoCellPattern(IEnumerable<Vector2Int> offsets)
        {
            if (offsets is null) throw new ArgumentNullException(nameof(offsets));
            this.offsets = ToArray(offsets);
        }

        private NeoCellPattern(Vector2Int[] offsets, bool takeOwnership)
        {
            this.offsets = takeOwnership ? offsets : (Vector2Int[])offsets.Clone();
        }

        /// <summary>Offset cell at the pattern's origin (0, 0) only.</summary>
        public static readonly NeoCellPattern Center = new(new[] { Vector2Int.zero }, true);

        /// <summary>The four edge-adjacent offsets (von Neumann neighborhood), center excluded.</summary>
        public static readonly NeoCellPattern FourNeighbors = Cross(1, includeCenter: false);

        /// <summary>The eight surrounding offsets (Moore neighborhood), center excluded.</summary>
        public static readonly NeoCellPattern EightNeighbors = Box(1, includeCenter: false);

        /// <summary>A pattern with no cells — a meaningful "no area of influence" value.</summary>
        public static readonly NeoCellPattern Empty = new(Array.Empty<Vector2Int>(), true);

        public int Count => offsets.Length;

        public Vector2Int this[int index] => offsets[index];

        /// <summary>Yields each offset translated by <paramref name="origin"/>, in pattern order.</summary>
        public IEnumerable<Vector2Int> GetCells(Vector2Int origin)
        {
            foreach (var offset in offsets)
            {
                yield return origin + offset;
            }
        }

        /// <summary>A filled square of cells within <paramref name="radius"/> on both axes.</summary>
        public static NeoCellPattern Box(int radius, bool includeCenter = true)
        {
            return Box(radius, radius, includeCenter);
        }

        /// <summary>A filled rectangle of cells within the given per-axis radii.</summary>
        public static NeoCellPattern Box(int radiusX, int radiusY, bool includeCenter = true)
        {
            if (radiusX < 0) throw new ArgumentOutOfRangeException(nameof(radiusX), radiusX, "Box radiusX must be >= 0.");
            if (radiusY < 0) throw new ArgumentOutOfRangeException(nameof(radiusY), radiusY, "Box radiusY must be >= 0.");

            var cells = new List<Vector2Int>();
            for (int x = -radiusX; x <= radiusX; x++)
            {
                for (int y = -radiusY; y <= radiusY; y++)
                {
                    if (x == 0 && y == 0 && !includeCenter) continue;
                    cells.Add(new Vector2Int(x, y));
                }
            }
            return new NeoCellPattern(SortCenterOut(cells), true);
        }

        /// <summary>The axis-aligned cells within <paramref name="radius"/> (a plus shape).</summary>
        public static NeoCellPattern Cross(int radius, bool includeCenter = true)
        {
            return Cross(radius, radius, includeCenter);
        }

        /// <summary>The axis-aligned cells within the given per-axis radii (a plus shape).</summary>
        public static NeoCellPattern Cross(int radiusX, int radiusY, bool includeCenter = true)
        {
            if (radiusX < 0) throw new ArgumentOutOfRangeException(nameof(radiusX), radiusX, "Cross radiusX must be >= 0.");
            if (radiusY < 0) throw new ArgumentOutOfRangeException(nameof(radiusY), radiusY, "Cross radiusY must be >= 0.");

            var cells = new List<Vector2Int>();
            if (includeCenter)
            {
                cells.Add(Vector2Int.zero);
            }
            for (int x = 1; x <= radiusX; x++)
            {
                cells.Add(new Vector2Int(-x, 0));
                cells.Add(new Vector2Int(x, 0));
            }
            for (int y = 1; y <= radiusY; y++)
            {
                cells.Add(new Vector2Int(0, -y));
                cells.Add(new Vector2Int(0, y));
            }
            return new NeoCellPattern(SortCenterOut(cells), true);
        }

        /// <summary>The hollow square shell of cells exactly <paramref name="radius"/> away (Chebyshev).</summary>
        public static NeoCellPattern Ring(int radius)
        {
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius), radius, "Ring radius must be >= 0.");
            if (radius == 0) return Center;

            var cells = new List<Vector2Int>();
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != radius) continue;
                    cells.Add(new Vector2Int(x, y));
                }
            }
            return new NeoCellPattern(cells.ToArray(), true);
        }

        /// <summary>
        /// A footprint spanning (0, 0) through (size - 1) up-right from the origin,
        /// per Unity Tilemap convention. Use <see cref="Translate"/> to re-anchor.
        /// </summary>
        public static NeoCellPattern Rect(Vector2Int size)
        {
            if (size.x <= 0) throw new ArgumentOutOfRangeException(nameof(size), size, "Rect size.x must be > 0.");
            if (size.y <= 0) throw new ArgumentOutOfRangeException(nameof(size), size, "Rect size.y must be > 0.");

            var cells = new List<Vector2Int>(size.x * size.y);
            for (int x = 0; x < size.x; x++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    cells.Add(new Vector2Int(x, y));
                }
            }
            return new NeoCellPattern(SortCenterOut(cells), true);
        }

        /// <summary>Cells stepping <paramref name="direction"/> from the origin, nearest first.</summary>
        public static NeoCellPattern Line(Vector2Int direction, int length, bool includeOrigin = false)
        {
            if (direction == Vector2Int.zero)
            {
                throw new ArgumentOutOfRangeException(nameof(direction), direction, "Line direction must be non-zero.");
            }
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), length, "Line length must be >= 0.");

            var cells = new List<Vector2Int>(length + 1);
            if (includeOrigin)
            {
                cells.Add(Vector2Int.zero);
            }
            for (int step = 1; step <= length; step++)
            {
                cells.Add(direction * step);
            }
            return new NeoCellPattern(cells.ToArray(), true);
        }

        /// <summary>This pattern with the origin offset prepended (a no-op when already present).</summary>
        public NeoCellPattern WithCenter()
        {
            if (Contains(Vector2Int.zero)) return this;
            var cells = new Vector2Int[offsets.Length + 1];
            cells[0] = Vector2Int.zero;
            Array.Copy(offsets, 0, cells, 1, offsets.Length);
            return new NeoCellPattern(cells, true);
        }

        /// <summary>This pattern with the origin offset removed (a no-op when absent).</summary>
        public NeoCellPattern WithoutCenter()
        {
            if (!Contains(Vector2Int.zero)) return this;
            var cells = new List<Vector2Int>(offsets.Length - 1);
            foreach (var offset in offsets)
            {
                if (offset == Vector2Int.zero) continue;
                cells.Add(offset);
            }
            return new NeoCellPattern(cells.ToArray(), true);
        }

        /// <summary>Every offset shifted by <paramref name="offset"/> — e.g. to re-anchor a <see cref="Rect"/> footprint.</summary>
        public NeoCellPattern Translate(Vector2Int offset)
        {
            if (offset == Vector2Int.zero) return this;
            var cells = new Vector2Int[offsets.Length];
            for (int index = 0; index < offsets.Length; index++)
            {
                cells[index] = offsets[index] + offset;
            }
            return new NeoCellPattern(cells, true);
        }

        /// <summary>
        /// This pattern's offsets followed by <paramref name="other"/>'s offsets that are
        /// not already present. Duplicate offsets within either input are also collapsed.
        /// </summary>
        public NeoCellPattern Union(NeoCellPattern other)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));
            var seen = new HashSet<Vector2Int>();
            var cells = new List<Vector2Int>(offsets.Length + other.offsets.Length);
            foreach (var offset in offsets)
            {
                if (seen.Add(offset)) cells.Add(offset);
            }
            foreach (var offset in other.offsets)
            {
                if (seen.Add(offset)) cells.Add(offset);
            }
            return new NeoCellPattern(cells.ToArray(), true);
        }

        public IEnumerator<Vector2Int> GetEnumerator()
        {
            return ((IEnumerable<Vector2Int>)offsets).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private bool Contains(Vector2Int offset)
        {
            foreach (var candidate in offsets)
            {
                if (candidate == offset) return true;
            }
            return false;
        }

        private static Vector2Int[] ToArray(IEnumerable<Vector2Int> offsets)
        {
            return offsets is ICollection<Vector2Int> collection
                ? CopyCollection(collection)
                : new List<Vector2Int>(offsets).ToArray();
        }

        private static Vector2Int[] CopyCollection(ICollection<Vector2Int> collection)
        {
            var cells = new Vector2Int[collection.Count];
            collection.CopyTo(cells, 0);
            return cells;
        }

        private static Vector2Int[] SortCenterOut(List<Vector2Int> cells)
        {
            var sorted = cells.ToArray();
            var generationOrder = new Dictionary<Vector2Int, int>(sorted.Length);
            for (int index = 0; index < sorted.Length; index++)
            {
                generationOrder[sorted[index]] = index;
            }
            Array.Sort(sorted, (left, right) =>
            {
                int byDistance = ChebyshevDistance(left).CompareTo(ChebyshevDistance(right));
                if (byDistance != 0) return byDistance;
                return generationOrder[left].CompareTo(generationOrder[right]);
            });
            return sorted;
        }

        private static int ChebyshevDistance(Vector2Int offset)
        {
            return Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y));
        }
    }
}
