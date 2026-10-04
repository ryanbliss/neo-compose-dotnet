// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P98 Unity hooks on a rendered placement. Every hook passes its own
    /// name and arguments to a native recorder:
    /// <code>
    /// class Object : IAwake, IOnEnable, IStart, IUpdate, IOnDisable, IOnDestroy {
    ///   void Awake() { RecordAwake("Awake"); }
    ///   void Update(float deltaTime) { RecordUpdate("Update", deltaTime); } ... }
    /// </code>
    /// </summary>
    public partial class NeoTileGridRendererTests
    {
        private static readonly (string name, string interfaceId)[] RecordedUnityHooks =
        {
            ("Awake", "system_e080a0a4-bb13-42a8-a1c5-17548dc5ccdb"),
            ("OnEnable", "system_3c3a67d4-9926-4b11-ac89-bcf30c2b5d90"),
            ("Start", "system_b7e94d04-1c00-4e17-afb2-3de8b40fc5eb"),
            ("Update", "system_9e3923ce-a569-491f-95d8-dca8411611eb"),
            ("OnDisable", "system_81750392-22a9-4881-a117-8d9de3e200cd"),
            ("OnDestroy", "system_fe306522-8df2-4f30-8931-204d8dab4d96"),
        };

        private static readonly (string name, string interfaceId) OnLoadHook =
            ("OnLoad", "system_c1b7f48c-5615-4e61-a071-5ff8b2a70c97");

        private static readonly (string name, string interfaceId) CollisionEnterHook =
            ("OnCollisionEnter2D", "system_6f4099d5-3d13-4bea-b432-3069cbd8b18d");

        private static readonly (string name, string interfaceId)[] RecordedTriggerHooks =
        {
            ("OnTriggerEnter2D", "system_9faab5e9-701c-4cd8-8723-5f9794b8e8c0"),
            ("OnTriggerExit2D", "system_9da00dcd-4035-46cc-8d98-00f5b8d91545"),
        };

        [UnityTest]
        public IEnumerator Lifecycle_UnityHooksFollowTheRenderedGameObject()
        {
            // Unity hooks run in play mode only.
            yield return new EnterPlayMode();
            var calls = new List<string>();
            NeoClient client = HookedClient(calls, RecordedUnityHooks);
            var obj = (TestComposedObject)SpawnAnimationTestObject(client).Info;
            var go = new GameObject("NeoTileGridRenderer lifecycle test");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });
                CollectionAssert.AreEqual(new[] { "Awake", "OnEnable" }, calls, "Start waits for the first frame.");

                yield return null;
                CollectionAssert.AreEqual(new[] { "Awake", "OnEnable", "Start", "Update" }, calls);

                calls.Clear();
                obj.Enabled = false;
                NotifyObjectVisibilityChanged(obj);
                CollectionAssert.AreEqual(new[] { "OnDisable" }, calls);
                yield return null;
                CollectionAssert.AreEqual(new[] { "OnDisable" }, calls, "A disabled GameObject does not tick.");

                client.RunTransaction(() =>
                {
                    obj.Enabled = true;
                    NotifyObjectVisibilityChanged(obj);
                    obj.Enabled = false;
                    NotifyObjectVisibilityChanged(obj);
                });
                CollectionAssert.AreEqual(new[] { "OnDisable" }, calls, "A toggle the transaction undoes runs nothing.");

                obj.Enabled = true;
                NotifyObjectVisibilityChanged(obj);
                yield return null;
                CollectionAssert.AreEqual(new[] { "OnDisable", "OnEnable", "Update" }, calls, "Start runs once.");

                calls.Clear();
                renderer.Clear();
                CollectionAssert.AreEqual(new[] { "OnDisable", "OnDestroy" }, calls);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                client.Dispose();
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator Lifecycle_HooksThatUndoEachOtherStopAtTheRunLimit()
        {
            yield return new EnterPlayMode();
            var calls = new List<string>();
            TestComposedObject? obj = null;
            // OnEnable hides the GameObject and OnDisable shows it again.
            NeoClient client = HookedClient(calls, RecordedUnityHooks, (hook, _, _) =>
            {
                if (obj is null || hook is not ("OnEnable" or "OnDisable"))
                    return;
                obj.Enabled = hook == "OnDisable";
                NotifyObjectVisibilityChanged(obj);
            });
            obj = (TestComposedObject)SpawnAnimationTestObject(client).Info;
            var go = new GameObject("NeoTileGridRenderer lifecycle cycle test");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                LogAssert.Expect(LogType.Exception, new Regex("NeoEffectCycleException"));
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                Assert.AreEqual(NeoClient.EffectRunsPerDrain, calls.Count, "The entry stops for the rest of the drain.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                client.Dispose();
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator Lifecycle_TriggerMessagesNameTheOtherObject()
        {
            yield return new EnterPlayMode();
            var calls = new List<string>();
            NeoClient client = HookedClient(calls, RecordedTriggerHooks);
            var trigger = (TestComposedObject)SpawnAnimationTestObject(client).Info;
            var body = (TestComposedObject)SpawnAnimationTestObject(client, new Vector2Int(5, 5)).Info;
            trigger.Collider = new TestObjectCollider { IsTrigger = true };
            body.Collider = new TestObjectCollider();
            // Both render in one cell, so their colliders overlap.
            var layer = new TestObjectLayerRuntime("object-layer", "Objects", ObjectClassId, "Default", 12,
                new[]
                {
                    new NeoObjectProjection("object-trigger", "object-layer", Vector2Int.zero, new[] { Vector2Int.zero }, trigger, 1),
                    new NeoObjectProjection("object-body", "object-layer", Vector2Int.zero, new[] { Vector2Int.zero }, body, 2),
                });
            var go = new GameObject("NeoTileGridRenderer lifecycle trigger test");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { layer });
                Assert.IsTrue(renderer.TryGetObjectRoot("object-body", out var bodyRoot));
                // Unity sends 2D messages only when one of the pair has a Rigidbody2D.
                bodyRoot.AddComponent<Rigidbody2D>().gravityScale = 0;
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                CollectionAssert.AreEquivalent(
                    new[]
                    {
                        $"OnTriggerEnter2D:{trigger.valueId}>{body.valueId}",
                        $"OnTriggerEnter2D:{body.valueId}>{trigger.valueId}",
                    },
                    calls);

                calls.Clear();
                body.Enabled = false;
                NotifyObjectVisibilityChanged(body);
                yield return new WaitForFixedUpdate();
                // The hidden side's entry is still enabled when its exit's
                // turn comes, so both sides run it (P98 §3.5).
                CollectionAssert.AreEquivalent(
                    new[]
                    {
                        $"OnTriggerExit2D:{trigger.valueId}>{body.valueId}",
                        $"OnTriggerExit2D:{body.valueId}>{trigger.valueId}",
                    },
                    calls);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                client.Dispose();
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator Lifecycle_AnEnterOnlyClassGetsOneRelay()
        {
            // P98 §8: no Stay relay, so no per-step Stay callbacks.
            yield return new EnterPlayMode();
            NeoClient client = HookedClient(new List<string>(), new[] { RecordedTriggerHooks[0] });
            var obj = (TestComposedObject)SpawnAnimationTestObject(client).Info;
            obj.Collider = new TestObjectCollider { IsTrigger = true };
            var go = new GameObject("NeoTileGridRenderer lifecycle relay test");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });
                Assert.IsTrue(renderer.TryGetObjectRoot("object-1", out var root));

                NeoLifecycleRelay[] relays = root.GetComponentsInChildren<NeoLifecycleRelay>(true);

                Assert.AreEqual(1, relays.Length);
                Assert.IsInstanceOf<NeoTriggerEnter2DRelay>(relays[0]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                client.Dispose();
            }
            yield return new ExitPlayMode();
        }

        [Test]
        public void Lifecycle_UnityHooksNeverRunInEditMode()
        {
            var calls = new List<string>();
            NeoClient client = HookedClient(calls, RecordedUnityHooks);
            var obj = (TestComposedObject)SpawnAnimationTestObject(client).Info;
            var go = new GameObject("NeoTileGridRenderer edit-mode lifecycle test");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });
                obj.Enabled = false;
                NotifyObjectVisibilityChanged(obj);
                renderer.Clear();

                CollectionAssert.IsEmpty(calls);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                client.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator Lifecycle_AnUpdateOnlyProjectTicksWithUnitysDeltaTime()
        {
            yield return new EnterPlayMode();
            var calls = new List<string>();
            object? delta = null;
            float expected = 0;
            NeoClient client = HookedClient(calls, new[] { RecordedUnityHooks[3] }, (_, _, args) =>
            {
                delta = args[1];
                expected = Time.deltaTime;
            });
            var obj = (TestComposedObject)SpawnAnimationTestObject(client).Info;
            var go = new GameObject("NeoTileGridRenderer lifecycle update-only test");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });
                yield return null;

                CollectionAssert.AreEqual(new[] { "Update" }, calls);
                Assert.IsInstanceOf<double>(delta, "A NeoScript Float is a double.");
                Assert.AreEqual((double)expected, (double)delta!);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                client.Dispose();
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator Lifecycle_AnUpdateThatHidesALaterObjectStopsItsTick()
        {
            yield return new EnterPlayMode();
            var ticks = new List<string>();
            TestComposedObject? first = null;
            TestComposedObject? second = null;
            // The first Update to run hides the other object.
            NeoClient client = HookedClient(new List<string>(), new[] { RecordedUnityHooks[3], RecordedUnityHooks[4] }, (hook, receiver, _) =>
            {
                ticks.Add($"{hook}:{receiver}");
                if (hook != "Update" || ticks.Count != 1)
                    return;
                TestComposedObject other = receiver == first!.valueId ? second! : first;
                other.Enabled = false;
                NotifyObjectVisibilityChanged(other);
            });
            first = (TestComposedObject)SpawnAnimationTestObject(client).Info;
            second = (TestComposedObject)SpawnAnimationTestObject(client, new Vector2Int(5, 5)).Info;
            var go = new GameObject("NeoTileGridRenderer lifecycle phase-order test");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerOf(first, second) });
                yield return null;

                string ticked = ticks[0].Substring("Update:".Length);
                string hidden = ticked == first.valueId ? second.valueId! : first.valueId!;
                CollectionAssert.AreEqual(new[] { $"Update:{ticked}", $"OnDisable:{hidden}" }, ticks);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                client.Dispose();
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator Lifecycle_AThrowingUpdateLetsTheOthersInThePhaseRun()
        {
            yield return new EnterPlayMode();
            var calls = new List<string>();
            NeoClient client = HookedClient(calls, new[] { RecordedUnityHooks[3] }, (_, _, _) =>
            {
                if (calls.Count == 1)
                    throw new InvalidOperationException("The first Update fails.");
            });
            var first = (TestComposedObject)SpawnAnimationTestObject(client).Info;
            var second = (TestComposedObject)SpawnAnimationTestObject(client, new Vector2Int(5, 5)).Info;
            var go = new GameObject("NeoTileGridRenderer lifecycle failure test");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerOf(first, second) });
                // Unity logs the hook's NeoLifecycleHookException under its cause.
                LogAssert.Expect(LogType.Exception, new Regex("The first Update fails"));
                yield return null;

                CollectionAssert.AreEqual(new[] { "Update", "Update" }, calls);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                client.Dispose();
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator Lifecycle_ACommitSpawnRunsOnLoadBeforeAwake()
        {
            yield return new EnterPlayMode();
            var calls = new List<string>();
            NeoClient client = HookedClient(new List<string>(), new[] { OnLoadHook, RecordedUnityHooks[0] },
                (hook, receiver, _) => calls.Add($"{hook}:{receiver}"));
            var go = new GameObject("NeoTileGridRenderer lifecycle OnLoad test");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                RenderObjectsLayer(renderer, client);

                string spawned = SpawnAnimationTestObject(client).Info.valueId!;

                CollectionAssert.Contains(calls, $"Awake:{spawned}");
                Assert.Less(calls.IndexOf($"OnLoad:{spawned}"), calls.IndexOf($"Awake:{spawned}"));
                Assert.GreaterOrEqual(calls.IndexOf($"OnLoad:{spawned}"), 0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                client.Dispose();
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator Lifecycle_AnAwakeThatRemovesItsObjectNeverEnablesIt()
        {
            yield return new EnterPlayMode();
            var calls = new List<string>();
            NeoClient? client = null;
            bool armed = false;
            client = HookedClient(calls, RecordedUnityHooks, (hook, receiver, _) =>
            {
                if (!armed || hook != "Awake")
                    return;
                var row = (ObjectMemberValue)client!.saveValues[receiver!];
                client.SetWritableValue(NeoValueOwnership.Save, new ObjectMemberValue
                {
                    id = row.id,
                    classId = row.classId,
                    containerId = row.containerId,
                    mark = NeoValueMarks.Removed,
                });
            });
            var go = new GameObject("NeoTileGridRenderer lifecycle Awake removal test");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                RenderObjectsLayer(renderer, client);
                calls.Clear();
                armed = true;

                SpawnAnimationTestObject(client);

                CollectionAssert.AreEqual(new[] { "Awake", "OnDestroy" }, calls);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                client.Dispose();
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator Lifecycle_DespawningInsideATriggerRunsItsExit()
        {
            yield return new EnterPlayMode();
            var calls = new List<string>();
            NeoClient client = HookedClient(calls, RecordedTriggerHooks);
            var trigger = (TestComposedObject)SpawnAnimationTestObject(client).Info;
            var body = (TestComposedObject)SpawnAnimationTestObject(client, new Vector2Int(5, 5)).Info;
            trigger.Collider = new TestObjectCollider { IsTrigger = true };
            body.Collider = new TestObjectCollider();
            // Both render in one cell, so their colliders overlap.
            var objects = new List<NeoObjectProjection>
            {
                new("object-trigger", "object-layer", Vector2Int.zero, new[] { Vector2Int.zero }, trigger, 1),
                new("object-body", "object-layer", Vector2Int.zero, new[] { Vector2Int.zero }, body, 2),
            };
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");
            var go = new GameObject("NeoTileGridRenderer lifecycle trigger despawn test");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(new TestTileGridContent(primitive, Array.Empty<IReadOnlyNeoTileLayerRuntime>(),
                    new[] { new TestObjectLayerRuntime("object-layer", "Objects", ObjectClassId, "Default", 12, objects) }));
                Assert.IsTrue(renderer.TryGetObjectRoot("object-body", out var bodyRoot));
                bodyRoot.AddComponent<Rigidbody2D>().gravityScale = 0;
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                calls.Clear();

                objects.RemoveAt(1);
                primitive.NotifyChanged(new NeoTileGridChangedArgs("town-grid",
                    objectLayers: new[]
                    {
                        new NeoObjectLayerChangedArgs("object-layer", new[] { new NeoObjectInstanceId("object-body") },
                            Array.Empty<NeoObjectInstanceId>(), new[] { Vector2Int.zero }, NeoTileGridChangeSourceKind.Direct, null),
                    }));
                yield return new WaitForFixedUpdate();

                CollectionAssert.Contains(calls, $"OnTriggerExit2D:{trigger.valueId}>{body.valueId}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                client.Dispose();
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator Lifecycle_APlainGameObjectIsANullOther()
        {
            yield return new EnterPlayMode();
            var calls = new List<string>();
            NeoClient client = HookedClient(calls, RecordedTriggerHooks);
            var trigger = (TestComposedObject)SpawnAnimationTestObject(client).Info;
            trigger.Collider = new TestObjectCollider { IsTrigger = true };
            var go = new GameObject("NeoTileGridRenderer lifecycle plain other test");
            var plain = new GameObject("Plain body");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(trigger, "Default", 12) });
                Assert.IsTrue(renderer.TryGetObjectRoot("object-1", out var root));
                plain.transform.position = root.GetComponentInChildren<Collider2D>().bounds.center;
                plain.AddComponent<BoxCollider2D>();
                plain.AddComponent<Rigidbody2D>().gravityScale = 0;
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();

                CollectionAssert.AreEqual(new[] { $"OnTriggerEnter2D:{trigger.valueId}>" }, calls);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(plain);
                UnityEngine.Object.DestroyImmediate(go);
                client.Dispose();
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator Lifecycle_ACollisionPassesItsNormalAndVelocityInCells()
        {
            yield return new EnterPlayMode();
            object? normal = null;
            object? velocity = null;
            NeoClient client = HookedClient(new List<string>(), new[] { CollisionEnterHook }, (_, _, args) =>
            {
                normal = args[2];
                velocity = args[3];
            });
            var wall = (TestComposedObject)SpawnAnimationTestObject(client).Info;
            wall.Collider = new TestObjectCollider();
            var go = new GameObject("NeoTileGridRenderer lifecycle collision test");
            // A plain body, so the renderer never writes its moves back to a Position.
            var ball = new GameObject("NeoTileGridRenderer lifecycle collision ball", typeof(BoxCollider2D));
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.CellSize = 2;
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerOf(wall) });
                ball.transform.position = new Vector3(6, 0, 0);
                var rigidbody = ball.AddComponent<Rigidbody2D>();
                rigidbody.gravityScale = 0;
                // 20 world units per second is 10 cells per second at two units a cell.
                rigidbody.linearVelocity = new Vector2(-20, 0);
                // A batchmode frame is shorter than a fixed step, so wait on game time.
                float deadline = Time.time + 1;
                while (normal is null && Time.time < deadline)
                    yield return null;

                // The recorder reads each argument as a NeoScript vector.
                var hitNormal = (NeoVector2Value)normal!;
                var hitVelocity = (NeoVector2Value)velocity!;
                Assert.AreEqual(1, Mathf.Abs(hitNormal.x), 0.01);
                Assert.AreEqual(0, hitNormal.y, 0.01);
                Assert.AreEqual(10, new Vector2(hitVelocity.x, hitVelocity.y).magnitude, 0.1);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ball);
                UnityEngine.Object.DestroyImmediate(go);
                client.Dispose();
            }
            yield return new ExitPlayMode();
        }

        // Each object in its own cell, in phase order.
        private static TestObjectLayerRuntime ObjectLayerOf(params TestComposedObject[] objects)
        {
            var projections = new NeoObjectProjection[objects.Length];
            for (int i = 0; i < objects.Length; i++)
            {
                var cell = new Vector2Int(i * 2, 0);
                projections[i] = new NeoObjectProjection($"object-{i}", "object-layer", cell, new[] { cell }, objects[i], i + 1);
            }
            return new TestObjectLayerRuntime("object-layer", "Objects", ObjectClassId, "Default", 12, projections);
        }

        // Renders the grid's bound object layer, which follows commits.
        private static void RenderObjectsLayer(NeoTileGridRenderer renderer, NeoClient client)
        {
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            renderer.Render(new TestTileGridContent(primitive, Array.Empty<IReadOnlyNeoTileLayerRuntime>(),
                new[] { primitive.BindReadOnlyObjectLayer<TestAuthoredObjectLayer>(ObjectsLayerClassId, new[] { ObjectClassId }) }));
        }

        // The placement fixture with the hooks on the object class, started.
        // onRecord runs inside the hook, after the call is recorded, with the
        // hook's name, its receiver's id, and the recorder's arguments: the
        // name, then the hook's own. Without record, every hook body is empty.
        private static NeoClient HookedClient(
            List<string> calls,
            (string name, string interfaceId)[] hooks,
            Action<string, string?, object?[]>? onRecord = null,
            bool record = true)
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            NeoSchemaClass objectClass = data.classes[ObjectClassId];
            objectClass.implementsInterfaceIds ??= new List<string>();
            var invokers = new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>();
            foreach (var (name, interfaceId) in hooks)
            {
                string memberId = $"hook-{name}-member";
                string recorderId = $"record-{name}-member";
                FunctionArgumentTypeInfo[] arguments = HookArguments(name);
                NSFunctionMember hook = RecordingHook(memberId, recorderId, name, arguments);
                if (!record)
                    hook.action!.instructions = Array.Empty<Instruction>();
                data.members[memberId] = hook;
                data.members[recorderId] = RecordingFunction(recorderId, arguments);
                objectClass.schema[name] = memberId;
                objectClass.implementsInterfaceIds.Add(interfaceId);
                bool contact = name.Contains("Trigger") || name.Contains("Collision");
                invokers[recorderId] = (_, receiver, args) =>
                {
                    string? receiverId = NeoGeneratedTypesSupport.ValueId(receiver);
                    calls.Add(contact ? $"{name}:{receiverId}>{NeoGeneratedTypesSupport.ValueId(args[1])}" : name);
                    onRecord?.Invoke(name, receiverId, args);
                    return null;
                };
            }
            NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            client.RegisterNativeFunctionInvokers(invokers);
            client.StartScriptRuntime();
            return client;
        }

        // The arguments each hook declares (P98 §3.1).
        private static FunctionArgumentTypeInfo[] HookArguments(string name) => name switch
        {
            "OnCollisionEnter2D" or "OnCollisionStay2D" => new[] { OtherArgument(), VectorArgument("normal"), VectorArgument("relativeVelocity") },
            "OnTriggerEnter2D" or "OnTriggerStay2D" or "OnTriggerExit2D" or "OnCollisionExit2D" => new[] { OtherArgument() },
            "FixedUpdate" or "Update" or "LateUpdate" => new[] { new FunctionArgumentTypeInfo { name = "deltaTime", type = MemberKind.Float, required = true } },
            _ => Array.Empty<FunctionArgumentTypeInfo>(),
        };

        // void <name>(<arguments>) { Record<name>("<name>", <arguments>); }
        private static NSFunctionMember RecordingHook(string id, string recorderId, string name, FunctionArgumentTypeInfo[] arguments)
        {
            var parameters = new List<Variable> { HookParameter("__this__"), HookParameter("__root__") };
            var args = new List<Pointer>
            {
                new ValuePointer
                {
                    type = PointerKind.Value,
                    value = new Value
                    {
                        typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                        value = JToken.FromObject(name),
                    },
                },
            };
            for (int i = 0; i < arguments.Length; i++)
            {
                string variableId = $"__arg_{i}__";
                parameters.Add(HookParameter(variableId, ArgumentTypeInfo(arguments[i])));
                args.Add(new VariablePointer { type = PointerKind.Variable, variableId = variableId });
            }
            return new NSFunctionMember
            {
                id = id,
                projectId = "project-a",
                name = name,
                kind = MemberKind.NSFunction,
                code = "compiled test hook",
                returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                argumentTypes = arguments,
                Dispatch = NeoFunctionDispatchKind.Synchronous,
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = parameters.ToArray(),
                    instructions = new Instruction[]
                    {
                        new FunctionCallInstruction
                        {
                            type = InstructionKind.FunctionCall,
                            call = new CallFunctionPointer
                            {
                                type = PointerKind.CallFunction,
                                memberId = recorderId,
                                receiver = CallReceiver.Instance(new VariablePointer { type = PointerKind.Variable, variableId = "__this__" }),
                                args = args.ToArray(),
                                callSiteId = $"record-{name}",
                            },
                        },
                    },
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                },
            };
        }

        // A native void (string hook, <arguments>) function.
        private static FunctionMember RecordingFunction(string id, FunctionArgumentTypeInfo[] arguments)
        {
            var parameters = new List<FunctionArgumentTypeInfo>
            {
                new() { name = "hook", type = MemberKind.String, required = true },
            };
            parameters.AddRange(arguments);
            return new FunctionMember
            {
                id = id,
                projectId = "project-a",
                name = "Record",
                kind = MemberKind.Function,
                returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                argumentTypes = parameters.ToArray(),
                Dispatch = NeoFunctionDispatchKind.Synchronous,
            };
        }

        private static FunctionArgumentTypeInfo VectorArgument(string name) =>
            new()
            {
                name = name,
                type = MemberKind.Vector2,
                required = true
            };

        private static FunctionArgumentTypeInfo OtherArgument() =>
            new()
            {
                name = "other",
                type = MemberKind.Class,
                classId = ObjectClassId,
                required = false
            };

        private static TypeInfo ArgumentTypeInfo(FunctionArgumentTypeInfo argument) =>
            argument.type == MemberKind.Class
                ? new ClassTypeInfo { type = MemberKind.Class, classId = argument.classId, required = false }
                : new PrimitiveTypeInfo { type = argument.type, required = true };

        private static Variable HookParameter(string id, TypeInfo? typeInfo = null) => new()
        {
            id = id,
            typeInfo = typeInfo ?? new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
            pointer = new VariablePointer { type = PointerKind.Variable, variableId = id },
        };
    }
}
