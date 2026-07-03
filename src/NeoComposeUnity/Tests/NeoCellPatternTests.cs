// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Linq;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    public class NeoCellPatternTests
    {
        [Test]
        public void Box_ContainsEveryOffsetWithinRadiusSortedCenterOut()
        {
            var pattern = NeoCellPattern.Box(1);

            Assert.AreEqual(9, pattern.Count);
            Assert.AreEqual(Vector2Int.zero, pattern[0]);
            AssertSortedCenterOut(pattern);

            Assert.AreEqual(8, NeoCellPattern.Box(1, includeCenter: false).Count);
            Assert.AreEqual(15, NeoCellPattern.Box(2, 1).Count);
        }

        [Test]
        public void Cross_ContainsOnlyAxisAlignedOffsets()
        {
            var pattern = NeoCellPattern.Cross(2);

            Assert.AreEqual(9, pattern.Count);
            Assert.AreEqual(Vector2Int.zero, pattern[0]);
            Assert.IsTrue(pattern.All(offset => offset.x == 0 || offset.y == 0));
            AssertSortedCenterOut(pattern);

            var noCenter = NeoCellPattern.Cross(1, includeCenter: false);
            Assert.AreEqual(4, noCenter.Count);
            Assert.IsFalse(noCenter.Contains(Vector2Int.zero));
        }

        [Test]
        public void Ring_ContainsExactlyTheShellAtTheRequestedDistance()
        {
            var pattern = NeoCellPattern.Ring(2);

            Assert.AreEqual(16, pattern.Count);
            Assert.IsTrue(pattern.All(offset =>
                Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) == 2));

            Assert.AreEqual(1, NeoCellPattern.Ring(0).Count);
        }

        [Test]
        public void Rect_SpansUpRightFromTheOrigin()
        {
            var pattern = NeoCellPattern.Rect(new Vector2Int(2, 3));

            Assert.AreEqual(6, pattern.Count);
            foreach (var offset in pattern)
            {
                Assert.IsTrue(offset.x is 0 or 1, offset.ToString());
                Assert.IsTrue(offset.y is 0 or 1 or 2, offset.ToString());
            }
        }

        [Test]
        public void Line_StepsAlongTheDirectionNearestFirst()
        {
            var pattern = NeoCellPattern.Line(Vector2Int.up, 2);

            CollectionAssert.AreEqual(
                new[] { new Vector2Int(0, 1), new Vector2Int(0, 2) },
                pattern.ToArray());

            var withOrigin = NeoCellPattern.Line(new Vector2Int(1, 1), 1, includeOrigin: true);
            CollectionAssert.AreEqual(
                new[] { Vector2Int.zero, new Vector2Int(1, 1) },
                withOrigin.ToArray());
        }

        [Test]
        public void Factories_RejectInvalidShapes()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NeoCellPattern.Box(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => NeoCellPattern.Cross(0, -2));
            Assert.Throws<ArgumentOutOfRangeException>(() => NeoCellPattern.Ring(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => NeoCellPattern.Rect(new Vector2Int(0, 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => NeoCellPattern.Rect(new Vector2Int(1, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => NeoCellPattern.Line(Vector2Int.zero, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => NeoCellPattern.Line(Vector2Int.up, -1));
        }

        [Test]
        public void Presets_HaveTheDocumentedShapes()
        {
            CollectionAssert.AreEqual(new[] { Vector2Int.zero }, NeoCellPattern.Center.ToArray());
            Assert.AreEqual(0, NeoCellPattern.Empty.Count);

            Assert.AreEqual(4, NeoCellPattern.FourNeighbors.Count);
            Assert.IsFalse(NeoCellPattern.FourNeighbors.Contains(Vector2Int.zero));
            Assert.IsTrue(NeoCellPattern.FourNeighbors.Contains(Vector2Int.up));

            Assert.AreEqual(8, NeoCellPattern.EightNeighbors.Count);
            Assert.IsTrue(NeoCellPattern.EightNeighbors.Contains(new Vector2Int(1, 1)));
        }

        [Test]
        public void HandBuiltPatterns_PreserveCallerOrder()
        {
            var offsets = new[]
            {
                new Vector2Int(2, 0),
                Vector2Int.zero,
                new Vector2Int(-1, 0),
            };

            var pattern = new NeoCellPattern(offsets);

            CollectionAssert.AreEqual(offsets, pattern.ToArray());
        }

        [Test]
        public void GetCells_TranslatesEveryOffsetByTheOrigin()
        {
            var pattern = new NeoCellPattern(Vector2Int.zero, Vector2Int.up);

            CollectionAssert.AreEqual(
                new[] { new Vector2Int(4, -2), new Vector2Int(4, -1) },
                pattern.GetCells(new Vector2Int(4, -2)).ToArray());
        }

        [Test]
        public void WithCenterAndWithoutCenter_AreNoOpsWhenAlreadySatisfied()
        {
            var withCenter = NeoCellPattern.Cross(1);
            var withoutCenter = NeoCellPattern.FourNeighbors;

            Assert.AreSame(withCenter, withCenter.WithCenter());
            Assert.AreSame(withoutCenter, withoutCenter.WithoutCenter());

            var added = withoutCenter.WithCenter();
            Assert.AreEqual(Vector2Int.zero, added[0]);
            Assert.AreEqual(withoutCenter.Count + 1, added.Count);

            var removed = withCenter.WithoutCenter();
            Assert.AreEqual(withCenter.Count - 1, removed.Count);
            Assert.IsFalse(removed.Contains(Vector2Int.zero));
        }

        [Test]
        public void Translate_ReanchorsEveryOffset()
        {
            var footprint = NeoCellPattern.Rect(new Vector2Int(2, 2))
                .Translate(new Vector2Int(-1, -1));

            Assert.IsTrue(footprint.Contains(new Vector2Int(-1, -1)));
            Assert.IsTrue(footprint.Contains(Vector2Int.zero));
            Assert.AreEqual(4, footprint.Count);
        }

        [Test]
        public void Union_ConcatenatesAndDeduplicatesPreservingOrder()
        {
            var union = NeoCellPattern.Center.Union(NeoCellPattern.Cross(1));

            Assert.AreEqual(5, union.Count);
            Assert.AreEqual(Vector2Int.zero, union[0]);

            var selfUnion = NeoCellPattern.FourNeighbors.Union(NeoCellPattern.FourNeighbors);
            Assert.AreEqual(4, selfUnion.Count);
        }

        private static void AssertSortedCenterOut(NeoCellPattern pattern)
        {
            int previousDistance = 0;
            foreach (var offset in pattern)
            {
                int distance = Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y));
                Assert.GreaterOrEqual(
                    distance,
                    previousDistance,
                    $"Offset {offset} is closer than an earlier offset — pattern is not center-out.");
                previousDistance = distance;
            }
        }
    }
}
