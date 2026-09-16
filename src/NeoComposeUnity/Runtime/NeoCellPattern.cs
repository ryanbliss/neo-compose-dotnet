// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public enum NeoCellPatternExcluding { None, Center }

    /// <summary>
    /// An immutable set of cell offsets relative to an origin — a reusable query
    /// shape for tile grid lookups (interaction reach, areas of effect, footprints).
    /// Translate the pattern at query time with <see cref="GetCells"/>, or pass it
    /// to the pattern-aware lookup extensions on layers and grid content.
    /// </summary>
    /// <remarks>
    /// Factory-built patterns (<see cref="Box(int,int,NeoCellPatternExcluding)"/>, <see cref="Cross(int,int,NeoCellPatternExcluding)"/>,
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
        public static readonly NeoCellPattern FourNeighbors = Cross(1, excluding: NeoCellPatternExcluding.Center);

        /// <summary>The eight surrounding offsets (Moore neighborhood), center excluded.</summary>
        public static readonly NeoCellPattern EightNeighbors = Box(1, excluding: NeoCellPatternExcluding.Center);

        /// <summary>A pattern with no cells — a meaningful "no area of influence" value.</summary>
        public static readonly NeoCellPattern Empty = new(Array.Empty<Vector2Int>(), true);

        public int Count => offsets.Length;

        public Vector2Int this[int index] => offsets[index];

        /// <summary>Yields each offset translated by <paramref name="origin"/>, in pattern order.</summary>
        public IEnumerable<Vector2Int> GetCells(Vector2Int origin)
        {
            foreach (var offset in offsets)
            {
                yield return new Vector2Int(checked(origin.x + offset.x), checked(origin.y + offset.y));
            }
        }

        /// <summary>A filled square within the given radius.</summary>
        public static NeoCellPattern Box(int radius, NeoCellPatternExcluding excluding = NeoCellPatternExcluding.None) =>
            Box(radius, radius, excluding);

        /// <summary>A filled rectangle within the given per-axis radii.</summary>
        public static NeoCellPattern Box(int radiusX, int radiusY, NeoCellPatternExcluding excluding = NeoCellPatternExcluding.None)
        {
            ValidateRadii(radiusX, radiusY);
            int omit = ExcludedCenter(excluding);
            int count = checked((int)((2L * radiusX + 1) * (2L * radiusY + 1) - omit));
            var cells = new Vector2Int[count];
            int index = 0;
            for (int distance = omit; distance <= Math.Max(radiusX, radiusY); distance++)
                AppendShell(cells, ref index, -radiusX, radiusX, -radiusY, radiusY, distance);
            return new NeoCellPattern(cells, true);
        }

        /// <summary>The axis-aligned cells within the given radius.</summary>
        public static NeoCellPattern Cross(int radius, NeoCellPatternExcluding excluding = NeoCellPatternExcluding.None) =>
            Cross(radius, radius, excluding);

        /// <summary>The axis-aligned cells within the given per-axis radii.</summary>
        public static NeoCellPattern Cross(int radiusX, int radiusY, NeoCellPatternExcluding excluding = NeoCellPatternExcluding.None)
        {
            ValidateRadii(radiusX, radiusY);
            int omit = ExcludedCenter(excluding);
            var cells = new Vector2Int[checked((int)(2L * radiusX + 2L * radiusY + 1 - omit))];
            int index = 0;
            if (omit == 0) cells[index++] = Vector2Int.zero;
            for (int distance = 1; distance <= Math.Max(radiusX, radiusY); distance++)
            {
                if (distance <= radiusX)
                {
                    cells[index++] = new Vector2Int(-distance, 0);
                    cells[index++] = new Vector2Int(distance, 0);
                }
                if (distance <= radiusY)
                {
                    cells[index++] = new Vector2Int(0, -distance);
                    cells[index++] = new Vector2Int(0, distance);
                }
            }
            return new NeoCellPattern(cells, true);
        }

        /// <summary>The hollow square shell at exactly the given Chebyshev distance.</summary>
        public static NeoCellPattern Ring(int radius)
        {
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            if (radius == 0) return Center;
            var cells = new Vector2Int[checked(radius * 8)];
            int index = 0;
            AppendShell(cells, ref index, -radius, radius, -radius, radius, radius);
            return new NeoCellPattern(cells, true);
        }

        /// <summary>A footprint from (0, 0) through (size - 1), ordered center-out.</summary>
        public static NeoCellPattern Rect(Vector2Int size)
        {
            if (size.x <= 0 || size.y <= 0) throw new ArgumentOutOfRangeException(nameof(size));
            var cells = new Vector2Int[checked(size.x * size.y)];
            int index = 0;
            for (int distance = 0; distance < Math.Max(size.x, size.y); distance++)
                AppendShell(cells, ref index, 0, size.x - 1, 0, size.y - 1, distance);
            return new NeoCellPattern(cells, true);
        }

        /// <summary>Origin and the specified number of steps along direction, nearest first.</summary>
        public static NeoCellPattern Line(Vector2Int direction, int length, NeoCellPatternExcluding excluding = NeoCellPatternExcluding.None)
        {
            if (direction == Vector2Int.zero) throw new ArgumentOutOfRangeException(nameof(direction));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            int omit = ExcludedCenter(excluding);
            // Validate the furthest coordinate before allocating the result.
            _ = checked(direction.x * length);
            _ = checked(direction.y * length);
            var cells = new Vector2Int[checked(length + 1 - omit)];
            for (int index = 0; index < cells.Length; index++)
                cells[index] = direction * (index + omit);
            return new NeoCellPattern(cells, true);
        }

        private static void ValidateRadii(int radiusX, int radiusY)
        {
            if (radiusX < 0) throw new ArgumentOutOfRangeException(nameof(radiusX));
            if (radiusY < 0) throw new ArgumentOutOfRangeException(nameof(radiusY));
        }

        private static int ExcludedCenter(NeoCellPatternExcluding excluding) => excluding switch
        {
            NeoCellPatternExcluding.None => 0,
            NeoCellPatternExcluding.Center => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(excluding)),
        };

        // Visit each clipped Chebyshev shell in the original x-outer/y-inner order.
        // When both horizontal edges lie outside the rectangle, only its two
        // vertical edges contribute; skipping interior columns keeps thin shapes linear.
        private static void AppendShell(Vector2Int[] cells, ref int index, int minX, int maxX, int minY, int maxY, int distance)
        {
            int left = Math.Max(minX, -distance), right = Math.Min(maxX, distance);
            int bottom = Math.Max(minY, -distance), top = Math.Min(maxY, distance);
            if (distance > Math.Max(Math.Abs(minY), Math.Abs(maxY)))
            {
                if (left == -distance) AppendColumn(cells, ref index, left, bottom, top);
                if (right == distance && right != left) AppendColumn(cells, ref index, right, bottom, top);
                return;
            }
            for (int x = left; x <= right; x++)
            {
                if (x == -distance || x == distance) AppendColumn(cells, ref index, x, bottom, top);
                else
                {
                    if (bottom == -distance) cells[index++] = new Vector2Int(x, bottom);
                    if (top == distance && top != bottom) cells[index++] = new Vector2Int(x, top);
                }
            }
        }

        private static void AppendColumn(Vector2Int[] cells, ref int index, int x, int bottom, int top)
        {
            for (int y = bottom; y <= top; y++) cells[index++] = new Vector2Int(x, y);
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
                cells[index] = new Vector2Int(checked(offsets[index].x + offset.x), checked(offsets[index].y + offset.y));
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
            var cells = new List<Vector2Int>(checked(offsets.Length + other.offsets.Length));
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

        public bool Contains(Vector2Int offset)
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

    }
}
