// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeoCompose.Tests
{
    /// <summary>The P111 §9 measurements. Run serially by name on this and the previous SDK.</summary>
    public partial class NeoTileGridRendererTests
    {
        private const int SpriteRigParts = 12;
        private const int SpriteRigCopies = 100;
        private const int NestedPositionWrites = 10_000;

        [Test]
        [Explicit("P111 §9 measurement; run serially by name.")]
        public void SpriteObjectPerformance_NestedPositionWrite()
        {
            // A nested object's Position write costs what an ordinary leaf
            // write costs, here a Position on a class with no world kind.
            var data = BuildClassBackedTileGridProjectData();
            data.classes["plain-class"] = new NeoSchemaClass
            {
                id = "plain-class",
                projectId = "project-a",
                name = "Plain",
                schema = new Dictionary<string, string> { ["Position"] = "object-position-member" },
            };
            foreach (var (id, classId) in new[] { ("nested-part", ObjectClassId), ("plain-row", "plain-class") })
            {
                data.values[id] = new ObjectMemberValue
                {
                    id = id,
                    classId = classId,
                    value = new Dictionary<string, string> { ["Position"] = id + "-position" },
                };
                data.values[id + "-position"] = new Vector3MemberValue
                {
                    id = id + "-position",
                    value = new NeoVector3Value(),
                };
            }
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            _ = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid").LookupCache;
            NeoMemberClassWritable Writable(string id, string classId) => new(client,
                new ClassMember { id = "measured-member", name = "Measured", kind = MemberKind.Class, classId = classId },
                id,
                NeoValueOwnership.Save);
            var nested = Writable("nested-part", ObjectClassId);
            var plain = Writable("plain-row", "plain-class");
            var payloads = new NeoValueWritePayload[2];
            for (int i = 0; i < payloads.Length; i++)
                payloads[i] = NeoAnimationCompiler.Payload(new Vector3MemberValue { value = new NeoVector3Value { x = i } });
            Action Writes(NeoMemberClassWritable owner) => () =>
            {
                for (int i = 0; i < NestedPositionWrites; i++)
                    NeoAnimationCompiler.WriteMember(client, owner, "Position", payloads[i & 1]);
            };
            Action nestedWrites = Writes(nested);
            Action plainWrites = Writes(plain);

            nestedWrites();
            plainWrites();
            var nestedSamples = new List<PerformanceSampling.Sample>();
            var plainSamples = new List<PerformanceSampling.Sample>();
            // Alternating which runs first keeps GC from one landing on the other.
            for (int round = 0; round < 10; round++)
            {
                if (round % 2 == 0)
                    plainSamples.Add(PerformanceSampling.Measure(plainWrites));
                nestedSamples.Add(PerformanceSampling.Measure(nestedWrites));
                if (round % 2 == 1)
                    plainSamples.Add(PerformanceSampling.Measure(plainWrites));
            }

            PerformanceSampling.Report("SpriteObjectPosition", "plain-leaf", $"writes={NestedPositionWrites}", plainSamples, NestedPositionWrites);
            PerformanceSampling.Report("SpriteObjectPosition", "nested-object", $"writes={NestedPositionWrites}", nestedSamples, NestedPositionWrites);
        }

        [UnityTest]
        [Explicit("P111 §9 measurement; run serially by name.")]
        public IEnumerator SpriteObjectPerformance_RigSpawn()
        {
            // A placed object with a dozen sprite children, as a character rig.
            yield return new EnterPlayMode();
            var data = BuildTileGridProjectData();
            for (int i = 0; i < SpriteRigParts; i++)
            {
                data.values[$"rig-part-{i}"] = new ObjectMemberValue
                {
                    id = $"rig-part-{i}",
                    classId = ObjectClassId,
                    value = new Dictionary<string, string>(),
                };
            }
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var sprite = CreateTestSprite("rig-part");
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [ObjectClassId] = (c, n) => new TestHookedSpriteObject(c, n) { Sprite = sprite },
            };
            var rig = ResolveComposedTestObject(client);
            var parts = new INeoWorldObjectValue[SpriteRigParts];
            for (int i = 0; i < SpriteRigParts; i++)
            {
                parts[i] = (TestHookedSpriteObject)NeoGeneratedTypesSupport.ResolveClassValue(
                    client,
                    $"rig-part-{i}",
                    factories,
                    new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            }
            rig.Children = parts;
            var layers = new[] { ObjectLayerOfCopies(rig, SpriteRigCopies) };
            var go = new GameObject("NeoTileGridRenderer sprite rig measurement");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");
                var tiles = new List<ReadOnlyNeoTileLayerRuntime>();
                void Spawn() => renderer.Render(primitive, tiles, layers);

                Spawn();
                var spawn = new List<PerformanceSampling.Sample>();
                for (int round = 0; round < 9; round++)
                {
                    renderer.Clear();
                    spawn.Add(PerformanceSampling.Measure(Spawn));
                }

                PerformanceSampling.Report("SpriteObjectRig", "spawn", $"rigs={SpriteRigCopies} parts={SpriteRigParts}", spawn, SpriteRigCopies);

                // Calibration: the bare component add the spawn now makes per part.
                renderer.Clear();
                var hosts = new GameObject[SpriteRigCopies * SpriteRigParts];
                var adds = new List<PerformanceSampling.Sample>();
                for (int round = 0; round < 9; round++)
                {
                    for (int i = 0; i < hosts.Length; i++)
                        hosts[i] = new GameObject("Part");
                    adds.Add(PerformanceSampling.Measure(() =>
                    {
                        foreach (var host in hosts)
                            host.AddComponent<NeoObjectBehaviour>();
                    }));
                    foreach (var host in hosts)
                        UnityEngine.Object.DestroyImmediate(host);
                }
                PerformanceSampling.Report("SpriteObjectRig", "add-component", $"adds={hosts.Length}", adds, SpriteRigCopies);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(sprite);
                client.Dispose();
            }
            yield return new ExitPlayMode();
        }

        /// <summary>A generated <c>NeoSpriteObject</c> with spawn hooks, as P111 generates it.</summary>
        private sealed class TestHookedSpriteObject
            : NeoGeneratedWorldObjectValue,
              INeoSpriteObjectValue,
              INeoObjectSpawnHooks
        {
            public TestHookedSpriteObject(NeoClient client, NeoMemberClass node)
                : base(client, node, ObjectClassId)
            {
            }

            public string Name => "Part";
            public NeoReadOnlyVector3 Position { get; } = new(Vector3.zero);
            public NeoReadOnlyVector3 Size { get; } = new(Vector3.one);
            public bool Enabled => true;
            public Sprite Sprite { get; set; } = null!;
            public bool FlipX => false;
            public bool FlipY => false;
            public string MaskInteraction => NeoSpriteMaskInteraction.None.OptionId();
            public int? SortingOrder => null;
            public void OnObjectSpawned(NeoObjectBehaviour behaviour)
            {
            }
            public void OnObjectDespawned(NeoObjectBehaviour behaviour)
            {
            }
        }
    }
}
