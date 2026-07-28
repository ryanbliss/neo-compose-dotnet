// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections;
using System.Collections.Generic;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HelloWorld.Assets.Tests.PlayMode
{
    /// <summary>
    /// Player-compatible smoke coverage for the generated Hello World client.
    /// This assembly deliberately has no UnityEditor reference, so these tests
    /// exercise the same schema and generated Class/Member runtime used by a game.
    /// </summary>
    public sealed class HelloWorldPlayModeTests
    {
        private const string ProjectResourcePath = "Neo/project";

        [UnityTest]
        public IEnumerator Schema14Export_LoadsClassAndMemberContractInPlayMode()
        {
            Assert.IsTrue(Application.isPlaying, "This gate must run through the PlayMode test runner.");
            yield return null;

            using var store = CreateLoadedStore();
            using var synchronizer = store.CreateNew("playmode-schema-14");
            var schema = synchronizer.Schema;

            Assert.IsNotNull(schema.metadata);
            Assert.AreEqual(14, schema.metadata!.schemaVersion);
            Assert.IsNotEmpty(schema.classes);
            Assert.IsNotEmpty(schema.members);

            var rootSaveMember = schema.members[schema.project.rootSaveFileMemberId];
            Assert.AreEqual(MemberKind.Class, rootSaveMember.kind);
            Assert.IsInstanceOf<ClassMember>(rootSaveMember);

            var rootSaveClass = schema.classes[((ClassMember)rootSaveMember).classId];
            Assert.AreEqual("Save", rootSaveClass.name);
            Assert.IsTrue(rootSaveClass.schema.TryGetValue("Bits", out var bitsMemberId));

            var bitsMember = schema.members[bitsMemberId];
            Assert.AreEqual("Bits", bitsMember.name);
            Assert.AreEqual(MemberKind.Int, bitsMember.kind);

            using var client = HelloWorldNeo.Load(
                    synchronizer,
                    localizationOptions: EnglishLocalizationOptions())
                .GetAwaiter()
                .GetResult();
            Assert.IsInstanceOf<NeoMemberClass>(client.AssetsRoot);
            Assert.IsInstanceOf<NeoMemberClassWritable>(client.SaveRoot);
        }

        [UnityTest]
        public IEnumerator GeneratedClassMemberApi_ReadsWritesAndNotifiesInPlayMode()
        {
            Assert.IsTrue(Application.isPlaying, "This gate must run through the PlayMode test runner.");

            using var store = CreateLoadedStore();
            using var synchronizer = store.CreateNew("playmode-generated-api");
            using var client = HelloWorldNeo.Load(
                    synchronizer,
                    localizationOptions: EnglishLocalizationOptions())
                .GetAwaiter()
                .GetResult();

            Assert.AreSame(Planet.earth, client.Save.World);
            Assert.AreEqual("Hello Earth!", client.Assets.Computed.fullText);

            var startingBits = client.Save.Bits;
            var observedBits = int.MinValue;
            var observedSource = NeoChangeSource.External;
            using var subscription = client.Save.OnChanged(
                Save.Fields.Bits,
                (bits, source) =>
                {
                    observedBits = bits;
                    observedSource = source;
                });

            client.Save.Bits = startingBits + 7;
            client.Save.World = Planet.mars;
            yield return null;

            Assert.AreEqual(startingBits + 7, client.Save.Bits);
            Assert.AreEqual(startingBits + 7, observedBits);
            Assert.AreEqual(NeoChangeSource.Local, observedSource);
            Assert.AreSame(Planet.mars, client.Save.World);
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds(), client.SerializeSaveData());
        }

        // TODO: re-add this test or equivalent once we re-add clips to hello world sample
        // [UnityTest]
        // public IEnumerator GeneratedRecoveryCacheAnimationClip_ExecutesExportedFrameActionInPlayMode()
        // {
        //     Assert.IsTrue(Application.isPlaying, "This gate must run through the PlayMode test runner.");

        //     using var store = CreateLoadedStore();
        //     using var synchronizer = store.CreateNew("playmode-object-animation");
        //     using var client = HelloWorldNeo.Load(
        //             synchronizer,
        //             localizationOptions: EnglishLocalizationOptions())
        //         .GetAwaiter()
        //         .GetResult();
        //     IReadOnlyList<NeoResolvedObjectInstance<RecoveryCacheObject>> placements = client
        //         .Assets
        //         .Worlds
        //         .OldConsoleLanding
        //         .Content
        //         .Objects
        //         .GetObjects<RecoveryCacheObject>();
        //     Assert.AreEqual(1, placements.Count);

        //     RecoveryCacheObject recoveryCache = placements[0].Info;
        //     NeoAnimationClip<RecoveryCacheObject> clip = recoveryCache.Pulse;
        //     var enteredFrames = new List<int>();
        //     using var frameTwo = clip.AddFrameEvent(2, () => enteredFrames.Add(2));
        //     client.Save.Bits = 0;

        //     clip.PlayOnce();
        //     float deadline = Time.realtimeSinceStartup + 2f;
        //     while (clip.IsPlaying && Time.realtimeSinceStartup < deadline)
        //     {
        //         yield return null;
        //     }

        //     Assert.AreEqual(1, client.Save.Bits);
        //     CollectionAssert.AreEqual(new[] { 2 }, enteredFrames);
        //     Assert.IsFalse(clip.IsPlaying);
        // }

        [UnityTest]
        public IEnumerator TileLayerRenderTarget_ReplacementDefersDestroyedWithoutClobberingNewTarget()
        {
            Assert.IsTrue(Application.isPlaying, "This gate must run through the PlayMode test runner.");

            using var store = CreateLoadedStore();
            using var synchronizer = store.CreateNew("playmode-render-target-replacement");
            using var client = HelloWorldNeo.Load(
                    synchronizer,
                    localizationOptions: EnglishLocalizationOptions())
                .GetAwaiter()
                .GetResult();
            var primitive = client.Assets.Worlds.OldConsoleLanding.Content.Primitive;
            var layer = new DeferredDestroyTileLayer();
            var go = new GameObject("Neo TileGrid Deferred Replacement Test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(primitive, new[] { layer });
                var replacedTarget = layer.CreatedTargets[0];

                renderer.Render(primitive, new[] { layer });
                var currentTarget = layer.CreatedTargets[1];

                Assert.AreNotEqual(replacedTarget.Id, currentTarget.Id);
                Assert.AreEqual(1, layer.DestroyingContexts.Count);
                Assert.AreEqual(replacedTarget.Id, layer.DestroyingContexts[0].Target.Id);
                Assert.AreEqual(
                    NeoTileLayerRenderTargetDestroyReason.Replaced,
                    layer.DestroyingContexts[0].Reason);
                Assert.AreEqual(0, layer.DestroyedContexts.Count);
                Assert.IsNotNull(currentTarget.Root);
                Assert.AreSame(renderer.UnityGrid.transform, currentTarget.Root.transform.parent);

                yield return null;

                Assert.AreEqual(1, layer.DestroyedContexts.Count);
                Assert.AreEqual(replacedTarget.Id, layer.DestroyedContexts[0].Target.Id);
                Assert.AreEqual(
                    NeoTileLayerRenderTargetDestroyReason.Replaced,
                    layer.DestroyedContexts[0].Reason);
                Assert.IsTrue(replacedTarget.Root == null);
                Assert.IsTrue(currentTarget.Root != null);
                Assert.AreSame(renderer.UnityGrid.transform, currentTarget.Root.transform.parent);
            }
            finally
            {
                Object.Destroy(go);
            }

            yield return null;
        }

        private sealed class DeferredDestroyTileLayer
            : ReadOnlyNeoTileLayerRuntime, INeoTileLayerRenderTargetProvider
        {
            public DeferredDestroyTileLayer()
                : base(
                    "playmode-deferred-layer",
                    "Deferred",
                    "playmode-empty-tile-class")
            {
            }

            public List<NeoTileLayerRenderTarget> CreatedTargets { get; } = new();

            public List<NeoTileLayerRenderTargetDestroyContext> DestroyingContexts { get; } = new();

            public List<NeoTileLayerRenderTargetDestroyedContext> DestroyedContexts { get; } = new();

            public NeoTileLayerRenderTarget? CreateRenderTarget(
                NeoTileLayerCreateContext context) => null;

            public void OnRenderTargetCreated(NeoTileLayerRenderTargetContext context)
            {
                CreatedTargets.Add(context.Target);
            }

            public void OnInitiallyRendered(NeoTileLayerRenderTargetContext context)
            {
            }

            public void OnRenderTargetChanged(NeoTileLayerRenderTargetChangedContext context)
            {
            }

            public void OnRenderTargetDestroying(
                NeoTileLayerRenderTargetDestroyContext context)
            {
                DestroyingContexts.Add(context);
            }

            public void OnRenderTargetDestroyed(
                NeoTileLayerRenderTargetDestroyedContext context)
            {
                DestroyedContexts.Add(context);
            }
        }

        private static NeoProjectStore CreateLoadedStore()
        {
            var export = Resources.Load<TextAsset>(ProjectResourcePath);
            Assert.IsNotNull(export, $"Missing Resources/{ProjectResourcePath}.json.");

            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(export!.text),
                localStore: new NeoInMemoryLocalSaveStore());
            try
            {
                store.LoadAsync().GetAwaiter().GetResult();
                return store;
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }

        private static NeoLocalizationOptions EnglishLocalizationOptions()
        {
            return new NeoLocalizationOptions
            {
                localeOverride = "en-US",
                preloadSystemLocale = false,
            };
        }
    }
}
