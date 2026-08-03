// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HelloWorld.Assets.Scripts;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

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
        public IEnumerator Schema15Export_LoadsClassAndMemberContractInPlayMode()
        {
            Assert.IsTrue(Application.isPlaying, "This gate must run through the PlayMode test runner.");
            yield return null;

            using var store = CreateLoadedStore();
            using var synchronizer = store.CreateNew("playmode-schema-15");
            var schema = synchronizer.Schema;

            Assert.IsNotNull(schema.metadata);
            Assert.AreEqual(16, schema.metadata!.schemaVersion);
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
        public IEnumerator SystemMap_StartsAtExpectedScreenCoordinates_OrbitsAndSupportsShipTravel()
        {
            Assert.IsTrue(Application.isPlaying, "This gate must run through the PlayMode test runner.");

            using var store = CreateLoadedStore();
            var gameplayObject = new GameObject("HelloWorld Orbit Regression Test");
            var gameplay = gameplayObject.AddComponent<HelloWorldGameplay>();

            try
            {
                gameplay.EnterAsync(store.CreateNew("playmode-system-map-orbits"))
                    .GetAwaiter()
                    .GetResult();
                yield return null;
                Canvas.ForceUpdateCanvases();

                var map = FindRect("SystemMap");
                var canvas = map.GetComponentInParent<Canvas>();
                Assert.IsNotNull(canvas);
                Assert.Greater(map.rect.width, 200f, "The system map must have a resolved screen width.");
                Assert.Greater(map.rect.height, 200f, "The system map must have a resolved screen height.");

                var expectedSolarOrder = new[]
                {
                    Planet.mercury, Planet.venus, Planet.earth, Planet.mars, Planet.jupiter,
                    Planet.saturn, Planet.uranus, Planet.neptune, Planet.pluto,
                };
                var orbitScreenPositions = new Dictionary<Planet, Vector2>();

                for (var ring = 0; ring < expectedSolarOrder.Length; ring++)
                {
                    var planet = expectedSolarOrder[ring];
                    var outposts = gameplay.Outposts
                        .Where(outpost => outpost.Planet == planet)
                        .ToArray();
                    Assert.IsNotEmpty(outposts, $"The sample is missing its {planet.Text} outpost.");

                    var marker = outposts.Length == 1
                        ? FindRect($"Planet {outposts[0].FullDisplayText}")
                        : FindRect($"World {planet.optionId}");
                    var anchor = marker.anchorMin;
                    Assert.AreEqual(anchor.x, marker.anchorMax.x, 0.0001f);
                    Assert.AreEqual(anchor.y, marker.anchorMax.y, 0.0001f);
                    Assert.AreEqual(0f, marker.anchoredPosition.x, 0.01f);
                    Assert.AreEqual(0f, marker.anchoredPosition.y, 0.01f);

                    var interpolation = ring / (float)(expectedSolarOrder.Length - 1);
                    var radiusX = Mathf.Lerp(0.085f, 0.47f, interpolation);
                    var radiusY = radiusX * 0.62f;
                    var normalizedOrbitDistance =
                        Mathf.Pow((anchor.x - 0.5f) / radiusX, 2f) +
                        Mathf.Pow((anchor.y - 0.5f) / radiusY, 2f);
                    Assert.AreEqual(
                        1f,
                        normalizedOrbitDistance,
                        0.015f,
                        $"{planet.Text} must start on orbit ring {ring}, not at the sun.");

                    var actualScreenPosition = ScreenPosition(marker, canvas);
                    var expectedScreenPosition = ScreenPositionForAnchor(map, canvas, anchor);
                    Assert.Less(
                        Vector2.Distance(actualScreenPosition, expectedScreenPosition),
                        1.5f,
                        $"{planet.Text} must resolve to its expected screen coordinate.");
                    orbitScreenPositions[planet] = actualScreenPosition;
                }

                Assert.Greater(
                    Vector2.Distance(
                        orbitScreenPositions[Planet.mercury],
                        orbitScreenPositions[Planet.pluto]),
                    100f,
                    "The inner and outer planets must not be clustered at screen center.");

                var mercury = gameplay.Outposts.First(outpost => outpost.Planet == Planet.mercury);
                var mercuryMarker = FindRect($"Planet {mercury.FullDisplayText}");
                var mercuryBeforeOrbit = ScreenPosition(mercuryMarker, canvas);
                yield return new WaitForSecondsRealtime(1f);
                Canvas.ForceUpdateCanvases();
                var mercuryAfterOrbit = ScreenPosition(mercuryMarker, canvas);
                Assert.Greater(
                    Vector2.Distance(mercuryBeforeOrbit, mercuryAfterOrbit),
                    2f,
                    "Mercury must visibly advance along its orbit over time.");

                var destination = gameplay.Outposts.First(outpost => outpost.Planet == Planet.venus);
                destination.Save.Unlocked = true;
                gameplay.TriggerDialogue();
                yield return null;
                Canvas.ForceUpdateCanvases();

                var destinationMarker = FindRect($"Planet {destination.FullDisplayText}");
                var destinationButton = destinationMarker.GetComponent<Button>();
                Assert.IsNotNull(destinationButton);
                Assert.IsTrue(destinationButton.interactable);

                var ship = FindRect("Ship");
                var shipBeforeTravel = ScreenPosition(ship, canvas);
                destinationButton.onClick.Invoke();
                yield return new WaitForSecondsRealtime(0.2f);
                Canvas.ForceUpdateCanvases();

                var shipMidTravel = ScreenPosition(ship, canvas);
                var destinationMidTravel = ScreenPosition(
                    FindRect($"Planet {destination.FullDisplayText}"),
                    canvas);
                Assert.Greater(
                    Vector2.Distance(shipBeforeTravel, shipMidTravel),
                    3f,
                    "The ship must leave its current planet during travel.");
                Assert.Greater(
                    Vector2.Distance(shipMidTravel, destinationMidTravel),
                    3f,
                    "The ship must pass through space before reaching its destination.");

                var deadline = Time.realtimeSinceStartup + 3f;
                while (gameplay.CurrentOutpost.valueId != destination.valueId &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.AreEqual(destination.valueId, gameplay.CurrentOutpost.valueId);
                Assert.Less(
                    Vector2.Distance(ship.anchorMin, destinationMarker.anchorMin),
                    0.001f,
                    "The ship must finish riding the destination planet.");
            }
            finally
            {
                Object.DestroyImmediate(gameplayObject);
            }
        }

        private static RectTransform FindRect(string name)
        {
            var rect = Resources.FindObjectsOfTypeAll<RectTransform>()
                .FirstOrDefault(candidate =>
                    candidate.gameObject.scene.IsValid() &&
                    candidate.gameObject.name == name);
            Assert.IsNotNull(rect, $"Missing runtime UI RectTransform '{name}'.");
            return rect!;
        }

        private static Vector2 ScreenPosition(RectTransform rect, Canvas canvas)
        {
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;
            return RectTransformUtility.WorldToScreenPoint(camera, rect.position);
        }

        private static Vector2 ScreenPositionForAnchor(
            RectTransform parent,
            Canvas canvas,
            Vector2 anchor)
        {
            var corners = new Vector3[4];
            parent.GetWorldCorners(corners);
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;
            var bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var topRight = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return new Vector2(
                Mathf.Lerp(bottomLeft.x, topRight.x, anchor.x),
                Mathf.Lerp(bottomLeft.y, topRight.y, anchor.y));
        }

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
