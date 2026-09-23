// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NeoCompose.Runtime;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P92 §9.2 EditMode measurements. Written against contracts that predate
    /// P92 too, so the same file measures the baseline on main, where a group
    /// sits on the object root and has no sort-point pair.
    /// </summary>
    public partial class NeoTileGridRendererTests
    {
        private static readonly int[] SortPointObjectCounts = { 100, 200, 400 };
        private const int SortPointSamples = 5;
        private const int SortPointWrites = 1_000;

        [Test]
        [Explicit("P92 §9.2 before/after measurement; run serially by name.")]
        public void SortPointPerformance_SpawnMoveAndSortPointWrite()
        {
            foreach (int count in SortPointObjectCounts)
            {
                MeasureSortPointWorld(count);
            }
        }

        private static void MeasureSortPointWorld(int count)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildSortPointProjectData());
            var sprite = CreateTestSprite("sort-point-art");
            var objects = SpawnSortPointObjects(client, count + 1);
            // One composition child with its own group, under the first object.
            var part = objects[count];
            objects.RemoveAt(count);
            part.Name = "Part";
            part.Position = new NeoReadOnlyVector3(0, 1, 0);
            part.Children = new INeoWorldObjectValue[]
            {
                new TestSpriteChild { Name = "Part Art", Sprite = sprite },
            };
            part.SortingGroup = SortingGroupOf(client, part);
            var projections = new List<NeoObjectProjection>();
            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                var cell = new Vector2Int((int)obj.Position.Value.x, (int)obj.Position.Value.y);
                var children = new List<INeoWorldObjectValue>();
                if (i == 0) children.Add(part);
                for (int layer = 0; layer < 3; layer++)
                {
                    children.Add(new TestSpriteChild
                    {
                        Name = $"Layer {layer}", Sprite = sprite, Position = new(0, layer * 0.5f, 0),
                    });
                }
                obj.Children = children;
                obj.SortingGroup = SortingGroupOf(client, obj);
                projections.Add(new NeoObjectProjection(
                    $"object-{i}", "object-layer", cell, new[] { cell }, obj, i));
            }
            var layerRuntime = new TestObjectLayerRuntime(
                "object-layer", "Objects", ObjectClassId, "Default", 12, projections);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");
            var tiles = new List<ReadOnlyNeoTileLayerRuntime>();

            var placed = objects[0];
            var placedGroup = NeoGeneratedTypesSupport.AsWritable(
                ((TestNodeSortingGroup)placed.SortingGroup!).BackingNode);
            var partGroup = NeoGeneratedTypesSupport.AsWritable(
                ((TestNodeSortingGroup)part.SortingGroup!).BackingNode);
            var placedNode = NeoGeneratedTypesSupport.AsWritable(placed.BackingNode);
            var positions = new[]
            {
                NeoValueWritePayload.FromValue(new Vector3(0.25f, 0, 0)),
                NeoValueWritePayload.FromValue(new Vector3(0.5f, 0, 0)),
            };
            var points = new[] { new Vector2(0.5f, 0.25f), new Vector2(0.75f, 0.5f) };
            void WritePositions()
            {
                for (int i = 0; i < SortPointWrites; i++)
                    NeoGeneratedTypesSupport.SetValue(placedNode, "Position", positions[i & 1]);
            }
            void WritePoints(NeoMemberClassWritable group)
            {
                for (int i = 0; i < SortPointWrites; i++)
                    NeoGeneratedTypesSupport.SetVector2(group, "SortPoint", points[i & 1]);
            }

            // The store write alone, before any renderer subscribes.
            WritePoints(placedGroup);
            var storeOnly = Sample(() => WritePoints(placedGroup));

            GameObject? go = null;
            void Spawn()
            {
                go = new GameObject("Sort point performance");
                go.AddComponent<NeoTileGridRenderer>()
                    .Render(primitive, tiles, new[] { layerRuntime });
            }
            void Despawn()
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                go = null;
            }
            try
            {
                Spawn();
                Despawn();
                var spawn = new List<SortPointSample>();
                for (int sample = 0; sample < SortPointSamples; sample++)
                {
                    spawn.Add(Measure(Spawn));
                    Despawn();
                }

                Spawn();
                int transforms = go!.GetComponentsInChildren<Transform>(true).Length;
                WritePositions();
                var move = Sample(WritePositions);
                WritePoints(placedGroup);
                var placedPoint = Sample(() => WritePoints(placedGroup));
                WritePoints(partGroup);
                var childPoint = Sample(() => WritePoints(partGroup));

                Report("spawn", count, spawn, count);
                Report("move", count, move, SortPointWrites);
                Report("sortpoint-store-only", count, storeOnly, SortPointWrites);
                Report("sortpoint-placed", count, placedPoint, SortPointWrites);
                Report("sortpoint-child", count, childPoint, SortPointWrites);
                TestContext.WriteLine($"P92 objects={count} transforms={transforms}");
            }
            finally
            {
                Despawn();
                DestroyTestSprite(sprite);
            }
        }

        /// <summary>
        /// Places <paramref name="count"/> clones of the grouped test asset,
        /// two cells apart so no footprint overlaps.
        /// </summary>
        private static List<TestComposedObject> SpawnSortPointObjects(NeoClient client, int count)
        {
            var primitive = NeoTileGridPrimitive.ResolveForSave(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string> { [typeof(TestComposedObject)] = ObjectClassId });
            var layer = primitive.BindWritableObjectLayer<TestAuthoredObjectLayer>(
                ObjectsLayerClassId,
                new[] { ObjectClassId });
            var asset = (TestComposedObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories())!;
            var spawned = new List<TestComposedObject>(count);
            for (int i = 0; i < count; i++)
            {
                var cell = new Vector2Int(20 + 2 * (i % 40), 20 + 2 * (i / 40));
                Assert.IsTrue(layer.SpawnClone(cell, asset).Ok, $"Could not place clone {i} at {cell}.");
                var obj = (TestComposedObject)layer.GetObjectProjection(cell)!.Info;
                obj.Position = new NeoReadOnlyVector3(cell.x, cell.y, 0);
                spawned.Add(obj);
            }
            return spawned;
        }

        private readonly struct SortPointSample
        {
            public SortPointSample(double ms, long gcBytes, long threadBytes)
            {
                Ms = ms;
                GcBytes = gcBytes;
                ThreadBytes = threadBytes;
            }

            public double Ms { get; }

            /// <summary>Unity's "GC Allocated In Frame" counter.</summary>
            public long GcBytes { get; }

            /// <summary><see cref="GC.GetAllocatedBytesForCurrentThread"/>.</summary>
            public long ThreadBytes { get; }
        }

        private static List<SortPointSample> Sample(Action action)
        {
            var samples = new List<SortPointSample>();
            for (int sample = 0; sample < SortPointSamples; sample++) samples.Add(Measure(action));
            return samples;
        }

        private static SortPointSample Measure(Action action)
        {
            using var recorder = new ProfilerRecorder(
                ProfilerCategory.Memory,
                "GC Allocated In Frame",
                1,
                ProfilerRecorderOptions.WrapAroundWhenCapacityReached
                    | ProfilerRecorderOptions.SumAllSamplesInFrame);
            Assert.IsTrue(recorder.Valid, "Unity GC allocation counter is unavailable.");
            recorder.Start();
            long gcBefore = recorder.CurrentValue;
            long threadBefore = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            long threadAfter = GC.GetAllocatedBytesForCurrentThread();
            long gcAfter = recorder.CurrentValue;
            recorder.Stop();
            return new SortPointSample(
                stopwatch.Elapsed.TotalMilliseconds,
                gcAfter - gcBefore,
                threadAfter - threadBefore);
        }

        private static void Report(string name, int count, List<SortPointSample> samples, int per)
        {
            var ms = samples.Select(sample => sample.Ms).OrderBy(value => value).ToArray();
            var bytes = samples.Select(sample => sample.GcBytes).OrderBy(value => value).ToArray();
            var threadBytes = samples.Select(sample => sample.ThreadBytes).OrderBy(value => value).ToArray();
            TestContext.WriteLine(
                $"P92 {name} objects={count} per={per} " +
                $"medianMs={ms[ms.Length / 2]:F3} minMs={ms[0]:F3} maxMs={ms[^1]:F3} " +
                $"medianBytes={bytes[bytes.Length / 2]} minBytes={bytes[0]} maxBytes={bytes[^1]} " +
                $"medianThreadBytes={threadBytes[threadBytes.Length / 2]} " +
                $"samplesMs=[{string.Join(",", samples.Select(sample => sample.Ms.ToString("F3")))}] " +
                $"samplesBytes=[{string.Join(",", samples.Select(sample => sample.GcBytes))}]");
        }
    }
}
