// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeoCompose.Tests
{
    /// <summary>The P98 §8 renderer gates, over 1,000 rendered objects.</summary>
    public partial class NeoTileGridRendererTests
    {
        private const int LifecycleObjects = 1_000;

        [UnityTest]
        [Explicit("Lifecycle phase measurement; run serially by name.")]
        public IEnumerator LifecyclePerformance_EmptyUpdatePhase()
        {
            // The phase costs at most 1.1x, and allocates no more than, a
            // plain loop calling each object's cached function with one boxed deltaTime.
            yield return new EnterPlayMode();
            NeoClient client = HookedClient(new List<string>(), new[] { RecordedUnityHooks[3] }, record: false);
            var obj = (TestComposedObject)SpawnAnimationTestObject(client).Info;
            var go = new GameObject("NeoTileGridRenderer lifecycle phase measurement");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerOfCopies(obj, LifecycleObjects) });
                var phases = (NeoLifecyclePhases)typeof(NeoTileGridRenderer)
                    .GetField("lifecyclePhases", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(renderer)!;
                MergedSchemaEntry update = client.ResolveClassNode(ObjectClassId).HookMember(NeoLifecycleHooks.Update);
                var functions = new NeoMemberNSFunction[LifecycleObjects];
                var valueIds = new string[LifecycleObjects];
                for (int i = 0; i < LifecycleObjects; i++)
                {
                    functions[i] = client.EffectFunction(update, obj.StorageOwnership);
                    valueIds[i] = obj.valueId!;
                }
                object deltaTime = 0.016;
                var args = new object?[] { deltaTime };
                void Plain()
                {
                    for (int i = 0; i < LifecycleObjects; i++)
                        functions[i].Invoke(valueIds[i], args);
                }
                void Phase() => phases.Run(NeoLifecyclePhases.Update, deltaTime);

                Phase();
                Plain();
                var plain = new List<PerformanceSampling.Sample>();
                var phase = new List<PerformanceSampling.Sample>();
                for (int round = 0; round < 9; round++)
                {
                    plain.Add(PerformanceSampling.Measure(Plain));
                    phase.Add(PerformanceSampling.Measure(Phase));
                }

                PerformanceSampling.Report("LifecyclePhase", "plain-loop", $"objects={LifecycleObjects}", plain, LifecycleObjects);
                PerformanceSampling.Report("LifecyclePhase", "update-phase", $"objects={LifecycleObjects}", phase, LifecycleObjects);
                Assert.LessOrEqual(Median(phase, sample => sample.Ms), 1.1 * Median(plain, sample => sample.Ms));
                Assert.LessOrEqual(Median(phase, sample => sample.GcBytes), Median(plain, sample => sample.GcBytes));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                client.Dispose();
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        [Explicit("Lifecycle measurement without hooks; run serially by name on this and the previous SDK.")]
        public IEnumerator LifecyclePerformance_NoHooksSpawnAndFrame()
        {
            // Spawn and frame time match the previous SDK, and frames allocate nothing.
            yield return new EnterPlayMode();
            NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildPlacementAnimationProjectData());
            var obj = (TestComposedObject)SpawnAnimationTestObject(client).Info;
            var layers = new[] { ObjectLayerOfCopies(obj, LifecycleObjects) };
            var go = new GameObject("NeoTileGridRenderer no-hook measurement");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");
                var tiles = new List<ReadOnlyNeoTileLayerRuntime>();
                void Spawn() => renderer.Render(primitive, tiles, layers);
                // The callbacks a renderer defines; the previous SDK has no Update or FixedUpdate.
                var callbacks = new List<Action>();
                foreach (string name in new[] { "FixedUpdate", "Update", "LateUpdate" })
                    if (typeof(NeoTileGridRenderer).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance) is { } method)
                        callbacks.Add((Action)Delegate.CreateDelegate(typeof(Action), renderer, method));
                void Frames()
                {
                    for (int frame = 0; frame < 100; frame++)
                        foreach (Action callback in callbacks)
                            callback();
                }

                Spawn();
                Frames();
                var spawn = new List<PerformanceSampling.Sample>();
                var frames = new List<PerformanceSampling.Sample>();
                for (int round = 0; round < 9; round++)
                {
                    renderer.Clear();
                    spawn.Add(PerformanceSampling.Measure(Spawn));
                    frames.Add(PerformanceSampling.Measure(Frames));
                }

                PerformanceSampling.Report("LifecycleNoHooks", "spawn", $"objects={LifecycleObjects}", spawn, LifecycleObjects);
                PerformanceSampling.Report("LifecycleNoHooks", "frames", $"objects={LifecycleObjects} callbacks={callbacks.Count}", frames, 100);
                Assert.AreEqual(0, Median(frames, sample => sample.GcBytes));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                client.Dispose();
            }
            yield return new ExitPlayMode();
        }

        // count projections of one object, so each renders its own GameObject.
        private static TestObjectLayerRuntime ObjectLayerOfCopies(NeoGeneratedClassValue obj, int count)
        {
            var objects = new NeoObjectProjection[count];
            for (int i = 0; i < count; i++)
                objects[i] = new NeoObjectProjection($"object-{i}", "object-layer", Vector2Int.zero, new[] { Vector2Int.zero }, obj, i + 1);
            return new TestObjectLayerRuntime("object-layer", "Objects", ObjectClassId, "Default", 12, objects);
        }

        private static double Median(List<PerformanceSampling.Sample> samples, Func<PerformanceSampling.Sample, double> value)
        {
            var values = samples.ConvertAll(sample => value(sample));
            values.Sort();
            return values[values.Count / 2];
        }
    }
}
