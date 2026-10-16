// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;
using UnityEngine;
using static NeoCompose.Tests.NeoCellPatternRuntimeTests;

namespace NeoCompose.Tests
{
    /// <summary>
    /// NeoCellPattern's factories run their NeoScript bodies through the
    /// ordinary evaluator over the seed; its helpers run natively.
    /// </summary>
    public class NeoCellPatternTests
    {
        private NeoClient client = null!;

        [SetUp]
        public void SetUp() => client = Client();

        [TearDown]
        public void TearDown() => client.Dispose();

        private TestCellPattern Box(int x, int? y = null, string excluding = NoneOptionId) =>
            TestCellPattern.Factory(client, BoxId, x, y, excluding);

        private TestCellPattern Cross(int x, int? y = null, string excluding = NoneOptionId) =>
            TestCellPattern.Factory(client, CrossId, x, y, excluding);

        private TestCellPattern Ring(int radius) => TestCellPattern.Factory(client, RingId, radius);

        private TestCellPattern Rect(int x, int y) => TestCellPattern.Factory(client, RectId, new Vector2Int(x, y));

        private TestCellPattern Line(Vector2Int direction, int length, string excluding = NoneOptionId) =>
            TestCellPattern.Factory(client, LineId, direction, length, excluding);

        private TestCellPattern New(params Vector2Int[] offsets) => TestCellPattern.New(client, offsets);

        private static Vector2Int V(int x, int y) => new(x, y);

        /// <summary>x-major, then y, stably ordered by Chebyshev distance from the origin.</summary>
        private static Vector2Int[] Chebyshev(int minX, int maxX, int minY, int maxY, Func<Vector2Int, bool>? keep = null) =>
            Enumerable.Range(minX, maxX - minX + 1)
                .SelectMany(x => Enumerable.Range(minY, maxY - minY + 1).Select(y => new Vector2Int(x, y)))
                .Where(keep ?? (_ => true))
                .OrderBy(Distance)
                .ToArray();

        private static int Distance(Vector2Int cell) => Math.Max(Math.Abs(cell.x), Math.Abs(cell.y));

        [Test]
        public void Box_OrdersNearestFirstForRadiiZeroThroughTwelve()
        {
            for (int x = 0; x <= 12; x++)
            {
                for (int y = 0; y <= 12; y++)
                {
                    CollectionAssert.AreEqual(Chebyshev(-x, x, -y, y), Box(x, y), $"Box({x}, {y})");
                    CollectionAssert.AreEqual(Chebyshev(-x, x, -y, y, cell => cell != Vector2Int.zero), Box(x, y, CenterOptionId),
                        $"Box({x}, {y}, Center)");
                }
            }
            CollectionAssert.AreEqual(Box(3, 3), Box(3));
        }

        [Test]
        public void Rect_OrdersNearestFirstForSizesOneThroughThirteen()
        {
            for (int x = 0; x <= 12; x++)
            {
                for (int y = 0; y <= 12; y++)
                    CollectionAssert.AreEqual(Chebyshev(0, x, 0, y), Rect(x + 1, y + 1), $"Rect({x + 1}, {y + 1})");
            }
        }

        [Test]
        public void Ring_ReturnsEachShellForRadiiZeroThroughTwelve()
        {
            for (int radius = 0; radius <= 12; radius++)
            {
                CollectionAssert.AreEqual(Chebyshev(-radius, radius, -radius, radius, cell => Distance(cell) == radius), Ring(radius),
                    $"Ring({radius})");
            }
        }

        [Test]
        public void CrossAndLine_PutTheOriginFirstUnlessExcluded()
        {
            CollectionAssert.AreEqual(new[] { V(0, 0), V(-1, 0), V(1, 0), V(0, -1), V(0, 1), V(-2, 0), V(2, 0) }, Cross(2, 1));
            CollectionAssert.AreEqual(new[] { V(-1, 0), V(1, 0), V(0, -1), V(0, 1) }, Cross(1, null, CenterOptionId));
            CollectionAssert.AreEqual(new[] { V(0, 0) }, Cross(0));
            CollectionAssert.AreEqual(new[] { V(0, 0), V(-2, 1), V(-4, 2) }, Line(V(-2, 1), 2));
            CollectionAssert.AreEqual(new[] { V(-2, 1), V(-4, 2) }, Line(V(-2, 1), 2, CenterOptionId));
            CollectionAssert.IsEmpty(Line(V(1, 0), 0, CenterOptionId));
        }

        [Test]
        public void Factories_RejectInvalidArguments()
        {
            Throws("Box radii must be non-negative; got -1, -1.", () => Box(-1));
            Throws("Box radii must be non-negative; got 1, -2.", () => Box(1, -2));
            Throws("Cross radii must be non-negative; got -1, -1.", () => Cross(-1));
            Throws("Ring radius must be non-negative; got -1.", () => Ring(-1));
            Throws("Rect size must be positive; got 0, 2.", () => Rect(0, 2));
            Throws("Line direction must be non-zero.", () => Line(Vector2Int.zero, 2));
            Throws("Line length must be non-negative; got -1.", () => Line(V(1, 0), -1));
        }

        [Test]
        public void Presets_ReadTheirStoredRows()
        {
            TestCellPattern center = TestCellPattern.Preset(client, CenterId);
            CollectionAssert.AreEqual(new[] { Vector2Int.zero }, center);
            Assert.AreEqual(1, center.Count);
            CollectionAssert.IsEmpty(TestCellPattern.Preset(client, EmptyId));
            CollectionAssert.AreEqual(Cross(1, null, CenterOptionId), TestCellPattern.Preset(client, FourNeighborsId));
            CollectionAssert.AreEqual(Box(1, null, CenterOptionId), TestCellPattern.Preset(client, EightNeighborsId));
        }

        [Test]
        public void HandBuiltPattern_KeepsItsOrderAndDuplicates()
        {
            TestCellPattern authored = New(V(3, 1), V(0, 0), V(3, 1), V(0, 0));
            CollectionAssert.AreEqual(new[] { V(3, 1), V(0, 0), V(3, 1), V(0, 0) }, authored);
            Assert.AreEqual(true, authored.Invoke(NeoCellPatternRuntime.ContainsId, V(3, 1)));
            Assert.AreEqual(false, authored.Invoke(NeoCellPatternRuntime.ContainsId, V(1, 3)));
            CollectionAssert.AreEqual(new[] { V(3, 1), V(3, 1) }, authored.Helper(NeoCellPatternRuntime.WithoutCenterId));
            CollectionAssert.AreEqual(new[] { V(0, 0), V(2, 0), V(2, 0) }, New(V(2, 0), V(2, 0)).Helper(NeoCellPatternRuntime.WithCenterId));
            Vector2Int[] translated = { V(4, -1), V(1, -2), V(4, -1), V(1, -2) };
            CollectionAssert.AreEqual(translated, authored.Helper(NeoCellPatternRuntime.TranslateId, V(1, -2)));
            CollectionAssert.AreEqual(translated, Cells(authored, V(1, -2)));
        }

        [Test]
        public void Union_KeepsTheFirstOccurrenceAcrossDuplicatesInsideEachInput()
        {
            TestCellPattern authored = New(V(3, 1), V(0, 0), V(3, 1), V(0, 0));
            CollectionAssert.AreEqual(new[] { V(3, 1), V(0, 0), V(2, 0) },
                authored.Helper(NeoCellPatternRuntime.UnionId, New(V(2, 0), V(3, 1), V(2, 0))));
            CollectionAssert.AreEqual(new[] { V(0, 0), V(-1, 0), V(1, 0), V(0, -1), V(0, 1) },
                TestCellPattern.Preset(client, CenterId).Helper(NeoCellPatternRuntime.UnionId, Cross(1)));
        }

        [Test]
        public void NoOpHelpers_ReturnTheReceiver()
        {
            TestCellPattern authored = New(V(3, 1), V(0, 0), V(3, 1), V(0, 0));
            Assert.AreSame(authored, authored.Helper(NeoCellPatternRuntime.WithCenterId));
            TestCellPattern offCenter = New(V(1, 0));
            Assert.AreSame(offCenter, offCenter.Helper(NeoCellPatternRuntime.WithoutCenterId));
            Assert.AreSame(authored, authored.Helper(NeoCellPatternRuntime.TranslateId, Vector2Int.zero));
            Assert.AreNotSame(authored, authored.Helper(NeoCellPatternRuntime.TranslateId, V(1, 0)));
            TestCellPattern center = TestCellPattern.Preset(client, CenterId);
            Assert.AreSame(center, center.Helper(NeoCellPatternRuntime.WithCenterId), "A read-only preset is returned as is.");

            // The evaluator hands back the receiver it called the helper on.
            var ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            object source = Construct(client, V(3, 1), V(0, 0));
            var scope = new NeoScriptScope(new Dictionary<string, object?> { ["p"] = source });
            object? result = NSGetterEvaluator.EvaluatePointer(new CallFunctionPointer
            {
                type = PointerKind.CallFunction,
                callSiteId = "with-center",
                memberId = NeoCellPatternRuntime.WithCenterId,
                receiver = CallReceiver.Instance(new VariablePointer { type = PointerKind.Variable, variableId = "p" }),
                args = Array.Empty<Pointer>(),
            }, scope, ctx);
            Assert.AreSame(source, result);
        }

        [Test]
        public void Helpers_RejectCellsOutsideTheIntegerRangeAndNonVectorArguments()
        {
            // Offsets are stored as float32 vectors, so the largest x they
            // hold exactly is 2^31 - 128.
            Throws("Translated cell.x exceeds the supported integer range.",
                () => New(V(int.MaxValue - 127, 0)).Helper(NeoCellPatternRuntime.TranslateId, V(128, 0)));
            Throws("Translated cell.y is below the supported integer range.",
                () => Cells(New(V(0, int.MinValue)), V(0, -1)));
            Throws("Contains offset must be a Vector2Int.", () => New(V(0, 0)).Invoke(NeoCellPatternRuntime.ContainsId, 3));
            Throws("Cell pattern value must be a NeoCellPattern instance.", () => New(V(0, 0)).Helper(NeoCellPatternRuntime.UnionId, 3));
        }

        [Test]
        public void CSharpSurface_ReadsTheStoredOffsets()
        {
            TestCellPattern pattern = Box(1);
            INeoCellPattern offsets = pattern;
            Assert.AreEqual(9, offsets.Count);
            Assert.AreEqual(9, pattern.Count, "The NeoScript Count agrees.");
            Assert.AreEqual(Vector2Int.zero, offsets[0]);
            var read = new List<Vector2Int>();
            foreach (Vector2Int offset in pattern)
                read.Add(offset);
            CollectionAssert.AreEqual(Chebyshev(-1, 1, -1, 1), read);
        }

        [Test]
        public void CloneOfACloneSurvivesSavingAndReload()
        {
            // A copy's constructor arguments may name its source's rows. The
            // copy must still replay after the source is gone and the save is
            // reloaded into a fresh client.
            Vector2Int[] offsets = { V(2, 0), V(0, 2) };
            ProjectData data = NeoGenericTestFixture.BuildProjectData();
            AddCellPatternSchema(data);
            AddStoredPattern(data, "stored", offsets);
            data.members["save-Copy"] = new ClassMember
            {
                id = "save-Copy",
                projectId = "project-a",
                name = "Copy",
                kind = MemberKind.Class,
                DeclaredClassId = NeoCellPatternOffsets.ClassId,
            };
            data.classes["save-root-class"].schema["Copy"] = "save-Copy";

            string saved;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(data))
            {
                string first = client.CloneValueReference("stored");
                string second = client.CloneValueReference(first);
                client.save.SetSerializedValue("Copy", NeoValueWritePayload.FromValueReference(second));
                client.RunGarbageCollector();
                saved = client.SerializeSaveData();
            }

            using NeoClient reloaded = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            string copy = reloaded.save.Get<NeoMemberClassWritable>("Copy").value!.id;
            TestCellPattern pattern = TestCellPattern.Writable(reloaded, copy, NeoValueOwnership.Save);
            Assert.AreEqual(2, pattern.Count);
            CollectionAssert.AreEqual(offsets, pattern);
        }

        [Test]
        public void Box60_Completes()
        {
            TestCellPattern box = Box(60);
            Assert.AreEqual(121 * 121, ((INeoCellPattern)box).Count);
            Assert.AreEqual(121 * 121, box.Count);
        }

        // Each large input has about 4x the cells of its small one, so linear
        // work takes about 4x as long and a quadratic step about 16x. 8
        // separates the two without timing noise.
        private const double LinearRatio = 8;

        [TestCase("Box", 10, 20)]
        [TestCase("Rect with one side 1", 200, 800)]
        [TestCase("Union of disjoint patterns", 200, 800)]
        [TestCase("Translate", 200, 800)]
        public void Construction_ScalesLinearly(string shape, int small, int large)
        {
            // Helper inputs are built before timing, so a sample measures
            // only the helper and not the factory that made its input.
            Action Run(int n)
            {
                switch (shape)
                {
                    case "Box":
                        return () => Box(n);
                    case "Rect with one side 1":
                        return () => Rect(n, 1);
                    case "Union of disjoint patterns":
                        {
                            TestCellPattern row = Rect(n, 1);
                            TestCellPattern next = row.Helper(NeoCellPatternRuntime.TranslateId, V(0, 1));
                            return () => row.Helper(NeoCellPatternRuntime.UnionId, next);
                        }
                    case "Translate":
                        {
                            TestCellPattern row = Rect(n, 1);
                            return () => row.Helper(NeoCellPatternRuntime.TranslateId, V(2, 3));
                        }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(shape));
                }
            }
            double ratio = CostRatio(Run(small), Run(large));
            Assert.Less(ratio, LinearRatio, $"{shape}: {ratio:F2}");
        }

        /// <summary>
        /// Median time of <paramref name="large"/> over median time of
        /// <paramref name="small"/>. Samples alternate, so load from other
        /// processes lands on both sizes alike.
        /// </summary>
        private static double CostRatio(Action small, Action large)
        {
            small();
            large();
            var smallMs = new double[9];
            var largeMs = new double[9];
            for (int sample = 0; sample < smallMs.Length; sample++)
            {
                smallMs[sample] = TimeMs(small);
                largeMs[sample] = TimeMs(large);
            }
            Array.Sort(smallMs);
            Array.Sort(largeMs);
            return largeMs[largeMs.Length / 2] / smallMs[smallMs.Length / 2];
        }

        private static double TimeMs(Action run)
        {
            var watch = Stopwatch.StartNew();
            run();
            return watch.Elapsed.TotalMilliseconds;
        }

        private static Vector2Int[] Cells(TestCellPattern pattern, Vector2Int origin) =>
            ((object?[])pattern.Invoke(NeoCellPatternRuntime.CellsId, origin)!)
                .Select(cell => NeoGeneratedTypesSupport.ReadVector2IntValue(cell)!.Value)
                .ToArray();

        private static void Throws(string message, TestDelegate run)
        {
            Exception error = Assert.Catch(run);
            StringAssert.Contains(message, error.Message);
        }
    }
}
