// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeoCompose.Tests
{
    public partial class NeoTileGridRendererTests
    {
        [UnityTest]
        public IEnumerator LiveSpawn_AnimationWritesDoNotReenterSpawnHooks() => ReentrantSpawn(SpawnHookAction.Animate);

        [UnityTest]
        public IEnumerator LiveSpawn_ThrowingHookKeepsOneHierarchyAndCleansUp() => ReentrantSpawn(SpawnHookAction.Throw);

        [UnityTest]
        public IEnumerator LiveSpawn_RemovalDuringHookDoesNotLeaveAnOrphan() => ReentrantSpawn(SpawnHookAction.Remove);

        private enum SpawnHookAction
        {
            Animate, Throw, Remove
        }

        private static IEnumerator ReentrantSpawn(SpawnHookAction action)
        {
            yield return new EnterPlayMode();
            using var client = NeoTestSaveStack.ClientFromSchema(BuildPlacementAnimationProjectData());
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            string? trackedId = null;
            int spawned = 0;
            int despawned = 0;
            NeoAnimationClip<TestComposedObject>? clip = null;
            NeoTileGridPrimitive? primitive = null;
            bool registeredDuringHook = false;
            TestComposedObject Create(NeoClient c, NeoMemberClass node, bool readOnly) => new(c, node, readOnly)
            {
                PositionFromNode = true,
                Collider = new TestObjectCollider(),
                Spawned = behaviour =>
                {
                    if (behaviour.InstanceId.Value != trackedId)
                        return;
                    spawned++;
                    registeredDuringHook = behaviour.Renderer.TryGetObjectRoot(behaviour.InstanceId, out var registered)
                        && ReferenceEquals(registered, behaviour.gameObject);
                    // Stop a broken renderer from recursively spawning forever.
                    if (spawned != 1)
                        return;
                    clip = NeoGeneratedTypesSupport.GetAnimationClip((TestComposedObject)behaviour.Object, "ChildAnimate");
                    clip.PlayLoop();
                    if (action == SpawnHookAction.Remove)
                        Assert.IsTrue(primitive!.TryDespawnObject(trackedId).Ok);
                    if (action == SpawnHookAction.Throw)
                        throw new InvalidOperationException("Spawn initialization failed after animation started.");
                },
                Despawned = behaviour =>
                {
                    if (behaviour.InstanceId.Value == trackedId)
                        despawned++;
                },
            };
            factories[ObjectClassId] = (c, n) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(c, n, () => Create(c, n, true));
            writableFactories[ObjectClassId] = (c, n) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(c, n, () => Create(c, n, false));
            primitive = NeoTileGridPrimitive.ResolveForSave(client, "town-grid", factories, writableFactories);
            var layer = primitive.BindReadOnlyObjectLayer<TestAuthoredObjectLayer>(ObjectsLayerClassId, new[] { ObjectClassId });
            var go = new GameObject("Live spawn animation regression");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(new TestTileGridContent(primitive, Array.Empty<IReadOnlyNeoTileLayerRuntime>(), new[] { layer }));
                trackedId = client.CloneValueReference("shop-object");
                var entry = (NeoGeneratedClassValue)NeoGeneratedTypesSupport.ResolveClassValue(client, trackedId, factories, writableFactories)!;

                if (action == SpawnHookAction.Throw)
                    LogAssert.Expect(LogType.Exception, new Regex("Spawn initialization failed after animation started"));
                var result = WritableObjectLink(client, "objects-link").TrySpawn(new Vector2Int(80, 90), entry);

                Assert.IsTrue(result.Ok, result.Message);
                Assert.AreEqual(1, spawned, "An initial animation frame must not enter a second spawn hook.");
                Assert.IsTrue(registeredDuringHook, "The completed hierarchy is indexed before gameplay initializes it.");
                if (action != SpawnHookAction.Remove)
                {
                    Assert.IsTrue(clip!.IsPlaying);
                    Assert.IsTrue(renderer.TryGetObjectRoot(trackedId, out var root));
                    Assert.AreEqual(1, go.GetComponentsInChildren<NeoObjectBehaviour>(true).Count(b => b.IsRoot && b.InstanceId.Value == trackedId));
                    Assert.IsTrue(root.GetComponent<BoxCollider2D>().enabled);
                    Assert.AreEqual(new Vector3(7, 0, 0), root.transform.localPosition, "The first animation frame is reconciled before TrySpawn returns.");
                    Assert.IsTrue(primitive.TryDespawnObject(trackedId).Ok);
                }
                Assert.AreEqual(1, despawned);
                Assert.IsFalse(renderer.TryGetObjectRoot(trackedId, out _));
                yield return null;
                Assert.IsFalse(go.GetComponentsInChildren<NeoObjectBehaviour>(true).Any(b => b.InstanceId.Value == trackedId));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
            yield return new ExitPlayMode();
        }
    }
}
