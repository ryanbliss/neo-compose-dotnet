// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Tests
{
    public class NeoTileGridRendererTests
    {
        private const string TileClassId = "tile-class";
        private const string ObjectClassId = "object-class";
        private const string ObjectPlacementTileClassId = "object-placement-tile-class";
        /// <summary>
        /// The real system enum id (P48 §2.1). Pinned rather than synthesized
        /// because <see cref="NeoPlayDirection"/> pins the option ids, and a
        /// fixture that invented its own would test the fixture.
        /// </summary>
        private const string PlayDirectionEnumId =
            "system_705ccc39-e46e-4c9f-af3e-3ec8fd818709";
        private const string TileLayerLinkClassId = "tile-layer-link-class";
        private const string TileLayerLinkSystemBaseClassId =
            "tile-layer-link-system-base-class";
        private const string ObjectLayerLinkSystemBaseClassId =
            "object-layer-link-system-base-class";
        private const string TileInstanceClassId = "tile-instance-class";
        private const string BaseTileClassId = "base-tile-class";
        private const string SubTileClassId = "sub-tile-class";
        private const string OtherTileClassId = "other-tile-class";
        private const string BackgroundLayerClassId = "background-layer-class";
        private const string ObjectsLayerClassId = "objects-layer-class";

        [Test]
        public void SchemaNineClassBackedLayerResolvesClassDefaultTileWithoutDefinitionValue()
        {
            var data = BuildClassBackedTileGridProjectData();
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string> { [typeof(TestTile)] = TileClassId });

            var layer = primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });
            var tile = layer.GetTile(new Vector2Int(2, 3));

            Assert.NotNull(tile);
            Assert.IsInstanceOf<TestTile>(tile!.Info);
            Assert.IsNull(tile.Info.valueId);
            Assert.AreEqual(TileClassId, ((TestTile)tile.Info).classId);
            Assert.AreEqual(BackgroundLayerClassId, layer.LayerClassId);
            Assert.AreEqual("background-layer-override", layer.LayerOverrideValueId);
            Assert.AreEqual("Override Background", layer.Name);
            Assert.AreEqual("Default layer description", layer.Description);
            Assert.AreEqual("background-layer-override", layer.valueId);
        }

        [Test]
        public void SchemaNineLayerWithoutOverrideUsesClassDefaults()
        {
            var data = BuildClassBackedTileGridProjectData();
            var backgroundLink = (ObjectMemberValue)data.values["background-link"];
            backgroundLink.value!.Remove("layerOverrideValueId");
            data.values.Remove("background-layer-override");
            data.values.Remove("background-layer-override-name");
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string> { [typeof(TestTile)] = TileClassId });

            var layer = primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });

            Assert.IsNull(layer.LayerOverrideValueId);
            Assert.IsNull(layer.valueId);
            Assert.AreEqual("Default Background", layer.Name);
            Assert.AreEqual("Default layer description", layer.Description);
        }

        [Test]
        public void SchemaTenConcreteLayerLinkInheritsTargetRelationWithoutLayerClassIdSidecar()
        {
            const string abstractLinkClassId = "abstract-tile-layer-link-class";
            var data = BuildClassBackedTileGridProjectData();
            data.classes[abstractLinkClassId] = new NeoSchemaClass
            {
                id = abstractLinkClassId,
                projectId = "project-a",
                name = "Abstract Tile Layer Link",
                schema = new Dictionary<string, string>(),
                extendsClassId = TileLayerLinkSystemBaseClassId,
                Modifier = NeoClassModifierKind.Abstract,
            };
            data.classes[TileLayerLinkClassId].extendsClassId = abstractLinkClassId;
            data.internalRecordRelations!["relation-link-target"].sourceRecordId =
                abstractLinkClassId;
            var backgroundLink = (ObjectMemberValue)data.values["background-link"];
            backgroundLink.value!.Remove("layerClassId");
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string> { [typeof(TestTile)] = TileClassId });

            var layer = primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });

            Assert.AreEqual(BackgroundLayerClassId, layer.LayerClassId);
            Assert.AreEqual("background-layer-override", layer.LayerOverrideValueId);
            Assert.NotNull(layer.GetTile(new Vector2Int(2, 3)));
        }

        [Test]
        public void SchemaTenTargetLayerInheritsExpectedWorldKind()
        {
            const string tileLayerBaseClassId = "tile-layer-base-class";
            var data = BuildClassBackedTileGridProjectData();
            data.classes[tileLayerBaseClassId] = new NeoSchemaClass
            {
                id = tileLayerBaseClassId,
                projectId = "project-a",
                name = "Tile Layer Base",
                schema = new Dictionary<string, string>(),
                Modifier = NeoClassModifierKind.Abstract,
                system = JObject.FromObject(new { worldKind = "tileLayer" }),
            };
            data.classes[BackgroundLayerClassId].extendsClassId = tileLayerBaseClassId;
            data.classes[BackgroundLayerClassId].system = null;
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories());

            var layer = primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });

            Assert.AreEqual(BackgroundLayerClassId, layer.LayerClassId);
        }

        [Test]
        public void SchemaTenLayerClassIdPayloadKeyDoesNotOverrideTargetRelation()
        {
            var data = BuildClassBackedTileGridProjectData();
            var backgroundLink = (ObjectMemberValue)data.values["background-link"];
            backgroundLink.value!["layerClassId"] = ObjectsLayerClassId;
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories());

            var layer = primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });

            Assert.AreEqual(BackgroundLayerClassId, layer.LayerClassId);
        }

        [Test]
        public void SchemaTenObjectGridLinkUsesRelationWithoutLayerClassIdSidecar()
        {
            var data = BuildClassBackedTileGridProjectData();
            ((ObjectMemberValue)data.values["objects-link"]).value!
                .Remove("layerClassId");
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories());

            var layer = primitive.BindReadOnlyObjectLayer<TestAuthoredObjectLayer>(
                ObjectsLayerClassId,
                new[] { ObjectClassId });

            Assert.AreEqual(ObjectsLayerClassId, layer.LayerClassId);
            Assert.AreEqual(1, layer.GetObjects().Count);
        }

        [Test]
        public void SchemaTenObjectCarriedTileLinkUsesRelationWithoutLayerClassIdSidecar()
        {
            var data = BuildClassBackedTileGridProjectData();
            ((ObjectMemberValue)data.values["shop-floor-link"]).value!
                .Remove("layerClassId");
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories());

            var layer = primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });

            var tile = layer.GetTile(new Vector2Int(9, 22));
            Assert.IsNotNull(tile);
            Assert.AreEqual("shop-floor-link", tile!.SourceTileLayerLinkId);
            Assert.AreEqual(BackgroundLayerClassId, tile.LayerId);
        }

        [Test]
        public void SchemaTenGridLinkWithoutTargetRelationFailsClearly()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.internalRecordRelations!.Remove("relation-link-target");
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories());

            var error = Assert.Throws<InvalidOperationException>(() =>
                primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                    BackgroundLayerClassId,
                    new[] { TileClassId }));

            StringAssert.Contains("has no effective", error!.Message);
            StringAssert.Contains(
                InternalRecordRelationKinds.WorldTileLayerLinkTarget,
                error.Message);
        }

        [Test]
        public void SchemaTenGridLinkRejectsAbstractTargetLayer()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.classes[BackgroundLayerClassId].DeclaredModifier = NeoClassModifierKind.Abstract;
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories());

            var error = Assert.Throws<InvalidOperationException>(() =>
                primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                    BackgroundLayerClassId,
                    new[] { TileClassId }));

            StringAssert.Contains("targets abstract layer class", error!.Message);
        }

        [Test]
        public void SchemaTenGridLinkRejectsSameDepthTargetAmbiguity()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.internalRecordRelations!["relation-link-target-conflict"] = Relation(
                "relation-link-target-conflict",
                InternalRecordRelationKinds.WorldTileLayerLinkTarget,
                TileLayerLinkClassId,
                ObjectsLayerClassId);
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories());

            var error = Assert.Throws<InvalidOperationException>(() =>
                primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                    BackgroundLayerClassId,
                    new[] { TileClassId }));

            StringAssert.Contains("ambiguous nearest declarations", error!.Message);
        }

        [Test]
        public void SchemaTenGridLinkRejectsWrongLayerKind()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.classes[BackgroundLayerClassId].system =
                JObject.FromObject(new { worldKind = "objectLayer" });
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories());

            var error = Assert.Throws<InvalidOperationException>(() =>
                primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                    BackgroundLayerClassId,
                    new[] { TileClassId }));

            StringAssert.Contains("inherited world kind is 'objectLayer'", error!.Message);
            StringAssert.Contains("instead of 'tileLayer'", error.Message);
        }

        [Test]
        public void SchemaTenGridLinkRejectsDirectAbstractSystemBase()
        {
            var data = BuildClassBackedTileGridProjectData();
            var backgroundLink = (ObjectMemberValue)data.values["background-link"];
            backgroundLink.classId = TileLayerLinkSystemBaseClassId;
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories());

            var error = Assert.Throws<InvalidOperationException>(() =>
                primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                    BackgroundLayerClassId,
                    new[] { TileClassId }));

            StringAssert.Contains("Tile layer link 'background-link'", error!.Message);
            StringAssert.Contains(
                $"uses abstract link class '{TileLayerLinkSystemBaseClassId}'",
                error.Message);
        }

        [Test]
        public void SchemaTenGridLinkRejectsWrongLinkClassWorldKind()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.classes[TileLayerLinkSystemBaseClassId].system =
                JObject.FromObject(new { worldKind = "objectLayerLink" });
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories());

            var error = Assert.Throws<InvalidOperationException>(() =>
                primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                    BackgroundLayerClassId,
                    new[] { TileClassId }));

            StringAssert.Contains("Tile layer link 'background-link'", error!.Message);
            StringAssert.Contains("inherited world kind is 'objectLayerLink'", error.Message);
            StringAssert.Contains("instead of 'tileLayerLink'", error.Message);
        }

        [Test]
        public void SchemaTenGridLinkRejectsDirectConcreteLinkWorldKind()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.classes[TileLayerLinkClassId].extendsClassId = null;
            data.classes[TileLayerLinkClassId].schema["Tiles"] =
                "tile-layer-link-tiles-member";
            data.classes[TileLayerLinkClassId].system =
                JObject.FromObject(new { worldKind = "tileLayerLink" });
            data.internalRecordRelations!.Remove("relation-link-target");
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories());

            var error = Assert.Throws<InvalidOperationException>(() =>
                primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                    BackgroundLayerClassId,
                    new[] { TileClassId }));

            StringAssert.Contains("Tile layer link 'background-link'", error!.Message);
            StringAssert.Contains(
                "must inherit 'tileLayerLink' from an abstract layer-link system base",
                error.Message);
        }

        [Test]
        public void SchemaTenGridLinkRejectsTargetRelationDeclaredOnSystemBase()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.internalRecordRelations!["relation-link-target"].sourceRecordId =
                TileLayerLinkSystemBaseClassId;
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories());

            var error = Assert.Throws<InvalidOperationException>(() =>
                primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                    BackgroundLayerClassId,
                    new[] { TileClassId }));

            StringAssert.Contains(
                $"Tile layer-link system base '{TileLayerLinkSystemBaseClassId}' must not declare",
                error!.Message);
            StringAssert.Contains("relation 'relation-link-target'", error.Message);
        }

        [Test]
        public void SchemaNineGenericPlacementWritesClassReferenceAndEnforcesGridImports()
        {
            var data = BuildClassBackedTileGridProjectData();
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoTileGridPrimitive.ResolveForSave(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string>
                {
                    [typeof(TestTile)] = TileClassId,
                    [typeof(TestOtherTile)] = OtherTileClassId,
                });
            var layer = primitive.BindWritableTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });
            var lifecycle = new RecordingTileSetLifecycle();
            primitive.Lifecycle = lifecycle;

            var placed = layer.Place<TestTile>(new Vector2Int(7, 8));
            var updated = layer.Place<TestTile>(new Vector2Int(7, 8));
            var rejected = layer.Place<TestOtherTile>(new Vector2Int(9, 10));

            Assert.IsTrue(placed.Ok, placed.Message);
            Assert.IsTrue(updated.Ok, updated.Message);
            Assert.IsFalse(rejected.Ok);
            Assert.AreEqual("tile-grid-asset-not-imported", rejected.ErrorCode);
            Assert.IsNotNull(lifecycle.ExistingInstance);
            Assert.AreEqual(
                BackgroundLayerClassId,
                lifecycle.ExistingInstance!["layerClassId"]!.Value<string>());
            Assert.AreNotEqual(
                "background-link",
                lifecycle.ExistingInstance["layerClassId"]!.Value<string>());
            var resolved = layer.GetTile(new Vector2Int(7, 8));
            Assert.NotNull(resolved);
            Assert.IsInstanceOf<TestTile>(resolved!.Info);
            Assert.IsNull(resolved.Info.valueId);
        }

        [Test]
        public void SchemaNineObjectPlacementWritesClassReferenceInSaveAndSession()
        {
            foreach (bool useSession in new[] { false, true })
            {
                var client = NeoTestSaveStack.ClientFromSchema(
                    BuildClassBackedTileGridProjectData());
                var primitive = useSession
                    ? NeoTileGridPrimitive.ResolveForSession(
                        client,
                        "town-grid",
                        BuildClassBackedReadOnlyFactories(),
                        BuildClassBackedWritableFactories(),
                        new Dictionary<Type, string>
                        {
                            [typeof(TestComposedObject)] = ObjectClassId,
                        })
                    : NeoTileGridPrimitive.ResolveForSave(
                        client,
                        "town-grid",
                        BuildClassBackedReadOnlyFactories(),
                        BuildClassBackedWritableFactories(),
                        new Dictionary<Type, string>
                        {
                            [typeof(TestComposedObject)] = ObjectClassId,
                        });
                var layer = primitive.BindWritableObjectLayer<TestAuthoredObjectLayer>(
                    ObjectsLayerClassId,
                    new[] { ObjectClassId });
                var changed = 0;
                using var subscription = layer.OnChanged(_ => changed++);

                var placed = layer.Spawn<TestComposedObject>(new Vector2Int(4, 5));

                Assert.IsTrue(placed.Ok, placed.Message);
                Assert.AreEqual(1, changed);
                var resolved = layer.GetObject(new Vector2Int(4, 5));
                Assert.NotNull(resolved);
                Assert.IsInstanceOf<TestComposedObject>(resolved!.Info);
                Assert.AreEqual(resolved.InstanceId.Value, resolved.Info.valueId);
                var writtenRows = useSession ? client.sessionValues : client.saveValues;
                var placement = (ObjectMemberValue)writtenRows[resolved.InstanceId.Value];
                Assert.AreEqual(ObjectClassId, placement.classId);
                Assert.AreEqual(ObjectClassId, placement.value!["assetClassId"]);
                Assert.IsFalse(placement.value.ContainsKey("objectValueId"));

                var asset = (TestComposedObject)NeoGeneratedTypesSupport.ResolveClassValue(
                    client,
                    "shop-object",
                    BuildClassBackedReadOnlyFactories(),
                    BuildClassBackedWritableFactories())!;
                var placedAsset = layer.Spawn(new Vector2Int(6, 7), asset);

                Assert.IsTrue(placedAsset.Ok, placedAsset.Message);
                Assert.AreEqual(2, changed);
                var resolvedAsset = layer.GetObject(new Vector2Int(6, 7));
                Assert.NotNull(resolvedAsset);
                Assert.AreEqual(
                    resolvedAsset!.InstanceId.Value,
                    resolvedAsset.Info.valueId);
                var assetPlacement = (ObjectMemberValue)writtenRows[
                    resolvedAsset.InstanceId.Value];
                Assert.AreEqual(ObjectClassId, assetPlacement.value!["assetClassId"]);
                Assert.AreEqual("shop-object", assetPlacement.value["assetValueId"]);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ObjectPlacementRejectsNonOriginFootprintOverlapWithoutWriting(
            bool useClassDefault)
        {
            var data = BuildClassBackedTileGridProjectData();
            ConfigureObjectPlacementFootprint(
                data,
                "shop-1",
                Vector2Int.one,
                Vector2Int.zero,
                new Vector2Int(1, 1));
            ConfigureObjectPlacementFootprint(
                data,
                "shop-object",
                Vector2Int.one,
                Vector2Int.zero,
                new Vector2Int(3, 1));
            if (useClassDefault)
            {
                ((ObjectMemberValue)data.values["shop-object"]).value!
                    .Remove("PlacementTiles");
                data.members["object-placement-tiles-member"].valueId =
                    "shop-object-placement-tiles";
            }

            var client = NeoTestSaveStack.ClientFromSchema(data);
            var readOnlyFactories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            var primitive = NeoTileGridPrimitive.ResolveForSave(
                client,
                "town-grid",
                readOnlyFactories,
                writableFactories,
                new Dictionary<Type, string>
                {
                    [typeof(TestComposedObject)] = ObjectClassId,
                });
            var layer = primitive.BindWritableObjectLayer<TestAuthoredObjectLayer>(
                ObjectsLayerClassId,
                new[] { ObjectClassId });
            var asset = (TestComposedObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                readOnlyFactories,
                writableFactories)!;

            NeoPlacementResult result = useClassDefault
                ? layer.Spawn<TestComposedObject>(new Vector2Int(8, 20))
                : layer.Spawn(new Vector2Int(8, 20), asset);

            Assert.IsFalse(result.Ok);
            Assert.AreEqual("tile-grid-object-cell-occupied", result.ErrorCode);
            StringAssert.Contains("(11, 21)", result.Message);
            Assert.AreEqual(1, layer.GetObjects().Count);
            Assert.AreEqual(0, client.saveValues.Count);
        }

        [Test]
        public void ObjectPlacementsCloneAuthoredChildrenWithExactProvenance()
        {
            var data = BuildClassBackedTileGridProjectData();
            ((ObjectMemberValue)data.values["shop-object"]).value!["Children"] =
                "shop-authored-children";
            data.values["shop-authored-children"] = new ArrayMemberValue
            {
                id = "shop-authored-children",
                value = new[] { "shop-authored-child" },
            };
            data.values["shop-authored-child"] = new ObjectMemberValue
            {
                id = "shop-authored-child",
                classId = TileLayerLinkClassId,
                value = new Dictionary<string, string>(),
            };
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoTileGridPrimitive.ResolveForSave(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string>
                {
                    [typeof(TestComposedObject)] = ObjectClassId,
                });
            var layer = primitive.BindWritableObjectLayer<TestAuthoredObjectLayer>(
                ObjectsLayerClassId,
                new[] { ObjectClassId });
            var asset = (TestComposedObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories())!;

            Assert.IsTrue(layer.Spawn(new Vector2Int(4, 5), asset).Ok);
            Assert.IsTrue(layer.Spawn(new Vector2Int(6, 7), asset).Ok);
            var first = layer.GetObject(new Vector2Int(4, 5))!;
            var second = layer.GetObject(new Vector2Int(6, 7))!;

            var firstRoot = (ObjectMemberValue)client.saveValues[first.InstanceId.Value];
            var secondRoot = (ObjectMemberValue)client.saveValues[second.InstanceId.Value];
            string firstListId = firstRoot.value!["Children"];
            string secondListId = secondRoot.value!["Children"];
            Assert.AreNotEqual(firstListId, secondListId);
            Assert.AreEqual(
                "shop-authored-children",
                client.saveValues[firstListId].sourceValueId);
            Assert.AreEqual(
                "shop-authored-children",
                client.saveValues[secondListId].sourceValueId);

            string firstChildId = ((ArrayMemberValue)client.saveValues[firstListId]).value![0];
            string secondChildId = ((ArrayMemberValue)client.saveValues[secondListId]).value![0];
            Assert.AreNotEqual(firstChildId, secondChildId);
            Assert.AreEqual(
                "shop-authored-child",
                client.saveValues[firstChildId].sourceValueId);
            Assert.AreEqual(
                "shop-authored-child",
                client.saveValues[secondChildId].sourceValueId);
            Assert.AreNotSame(first.Info, second.Info);
        }

        [Test]
        public void AnimationChildOverrideWritesOnlyTheMatchingPlacedChild()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoTileGridPrimitive primitive = NeoTileGridPrimitive.ResolveForSave(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string>
                {
                    [typeof(TestComposedObject)] = ObjectClassId,
                });
            var layer = primitive.BindWritableObjectLayer<TestAuthoredObjectLayer>(
                ObjectsLayerClassId,
                new[] { ObjectClassId });
            var asset = (TestComposedObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories())!;
            Assert.IsTrue(layer.Spawn(new Vector2Int(4, 5), asset).Ok);
            Assert.IsTrue(layer.Spawn(new Vector2Int(6, 7), asset).Ok);
            NeoResolvedObjectInstance first = layer.GetObject(new Vector2Int(4, 5))!;
            NeoResolvedObjectInstance second = layer.GetObject(new Vector2Int(6, 7))!;
            string firstPositionId = PlacedChildPositionId(client, first.InstanceId.Value);
            string secondPositionId = PlacedChildPositionId(client, second.InstanceId.Value);

            NeoAnimationClip<TestComposedObject> clip =
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)first.Info,
                    "Animate");
            clip.PlayOnce();

            Assert.AreEqual(
                9,
                ((Vector3MemberValue)client.sessionValues[firstPositionId]).value!.x);
            Assert.AreEqual(
                1,
                ((Vector3MemberValue)client.sessionValues[secondPositionId]).value!.x);
            Assert.AreNotEqual(firstPositionId, secondPositionId);
        }

        [Test]
        public void AnimationChildTrackResamplesAgainstThePlacedChild()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoTileGridPrimitive primitive = NeoTileGridPrimitive.ResolveForSave(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string>
                {
                    [typeof(TestComposedObject)] = ObjectClassId,
                });
            var layer = primitive.BindWritableObjectLayer<TestAuthoredObjectLayer>(
                ObjectsLayerClassId,
                new[] { ObjectClassId });
            var asset = (TestComposedObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories())!;
            Assert.IsTrue(layer.Spawn(new Vector2Int(4, 5), asset).Ok);
            NeoResolvedObjectInstance placed = layer.GetObject(new Vector2Int(4, 5))!;
            string positionId = PlacedChildPositionId(client, placed.InstanceId.Value);

            NeoAnimationClip<TestComposedObject> clip =
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)placed.Info,
                    "TrackAnimate");
            clip.PlayOnce();
            Assert.AreEqual(
                1,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x);
            clip.Tick(0.1f);
            Assert.AreEqual(
                7,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x);
        }

        [TestCase("fps", "FPS must be at least 1")]
        [TestCase("duplicate-frame", "duplicate frame index 0")]
        // P48 §2.3 deletes P29's fit error — an overrunning track truncates —
        // and replaces it with the row that can never play at all.
        [TestCase(
            "track-start-frame",
            "StartFrame 1 is at or past the owning clip's Duration 1")]
        [TestCase(
            "track-inverted-crop",
            "crop window [2, 2) is empty or inverted")]
        public void AnimationExportValidation_FailsDuringClientLoad(
            string invalidCase,
            string expectedMessage)
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            switch (invalidCase)
            {
                case "fps":
                    ((NumberMemberValue)data.values["parent-clip-fps"]).value = 0;
                    break;
                case "duplicate-frame":
                    ((ArrayMemberValue)data.values["parent-clip-frames"]).value =
                        new[] { "parent-frame-0", "duplicate-parent-frame" };
                    data.values["duplicate-parent-frame"] = new ObjectMemberValue
                    {
                        id = "duplicate-parent-frame",
                        classId = ((ObjectMemberValue)data.values["parent-frame-0"]).classId,
                        value = new Dictionary<string, string>
                        {
                            ["Index"] = "duplicate-parent-frame-index",
                        },
                    };
                    data.values["duplicate-parent-frame-index"] =
                        new NumberMemberValue
                        {
                            id = "duplicate-parent-frame-index",
                            value = 0,
                        };
                    break;
                case "track-start-frame":
                    ((NumberMemberValue)data.values["track-parent-duration"]).value = 1;
                    break;
                case "track-inverted-crop":
                    ((ObjectMemberValue)data.values["track-parent-child"])
                        .value!["OffsetStartIndex"] = "track-crop-start";
                    ((ObjectMemberValue)data.values["track-parent-child"])
                        .value!["OffsetEndIndex"] = "track-crop-end";
                    data.values["track-crop-start"] =
                        new NumberMemberValue { id = "track-crop-start", value = 2 };
                    data.values["track-crop-end"] =
                        new NumberMemberValue { id = "track-crop-end", value = 2 };
                    break;
                default:
                    Assert.Fail($"Unknown invalid case '{invalidCase}'.");
                    break;
            }

            var error = Assert.Throws<InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(data));

            StringAssert.Contains(expectedMessage, error!.Message);
        }

        [Test]
        public void AnimationExportValidation_RejectsActionOutsideTargetMergedSchema()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            data.classes["animation-frame-class"].schema["Actions"] =
                "animation-actions-member";
            data.members["animation-actions-member"] = new ListMember
            {
                id = "animation-actions-member",
                projectId = "project-a",
                name = "Actions",
                kind = MemberKind.List,
                entryMemberId = "animation-action-entry-member",
            };
            data.members["animation-action-entry-member"] = new FunctionRefMember
            {
                id = "animation-action-entry-member",
                projectId = "project-a",
                name = "Action",
                kind = MemberKind.FunctionRef,
            };
            data.members["foreign-action"] = new FunctionMember
            {
                id = "foreign-action",
                projectId = "project-a",
                name = "ForeignAction",
                kind = MemberKind.Function,
                returnTypeInfo = new VoidTypeInfo
                {
                    type = MemberKind.Void,
                    required = true,
                },
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                Dispatch = NeoFunctionDispatchKind.Synchronous,
            };
            ((ObjectMemberValue)data.values["parent-frame-0"]).value!["Actions"] =
                "parent-frame-actions";
            data.values["parent-frame-actions"] = new ArrayMemberValue
            {
                id = "parent-frame-actions",
                value = new[] { "parent-frame-action" },
            };
            data.values["parent-frame-action"] = new ObjectMemberValue
            {
                id = "parent-frame-action",
                value = new Dictionary<string, string>
                {
                    ["functionMemberId"] = "foreign-action",
                },
            };

            var error = Assert.Throws<InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(data));

            StringAssert.Contains("outside target class", error!.Message);
            StringAssert.Contains("foreign-action", error.Message);
        }

        [Test]
        public void AnimationExportValidation_ValidatesInheritedClosedGenericClipPlacement()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            const string paramId = "animation-target-param";
            data.classes["generic-animation-owner"] = new NeoSchemaClass
            {
                id = "generic-animation-owner",
                projectId = "project-a",
                name = "GenericAnimationOwner",
                Modifier = NeoClassModifierKind.Abstract,
                schema = new Dictionary<string, string>
                {
                    ["InheritedClip"] = "generic-animation-slot",
                },
                genericParams = new List<GenericParamDeclaration>
                {
                    new() { id = paramId, name = "TClip" },
                },
            };
            data.classes["closed-animation-owner"] = new NeoSchemaClass
            {
                id = "closed-animation-owner",
                projectId = "project-a",
                name = "ClosedAnimationOwner",
                extendsClassId = "generic-animation-owner",
                schema = new Dictionary<string, string>(),
                extendsGenericBindings = new Dictionary<string, GenericBinding>
                {
                    [paramId] = new()
                    {
                        kind = NeoGenericBindingKind.Member,
                        memberId = "inherited-invalid-clip-binding",
                    },
                },
            };
            data.members["generic-animation-slot"] = new GenericMember
            {
                id = "generic-animation-slot",
                projectId = "project-a",
                name = "InheritedClip",
                kind = MemberKind.Generic,
                genericParamId = paramId,
            };
            data.members["inherited-invalid-clip-binding"] = new ClassMember
            {
                id = "inherited-invalid-clip-binding",
                projectId = "project-a",
                name = "ClipBinding",
                kind = MemberKind.Class,
                classId = "animation-clip-class",
                valueId = "inherited-invalid-clip",
            };
            data.values["inherited-invalid-clip"] = new ObjectMemberValue
            {
                id = "inherited-invalid-clip",
                classId = "animation-clip-class",
                value = new Dictionary<string, string>
                {
                    ["FPS"] = "inherited-invalid-fps",
                    ["Duration"] = "inherited-invalid-duration",
                    ["Frames"] = "inherited-invalid-frames",
                    ["Tracks"] = "inherited-invalid-tracks",
                },
            };
            data.values["inherited-invalid-fps"] = new NumberMemberValue
            {
                id = "inherited-invalid-fps",
                value = 0,
            };
            data.values["inherited-invalid-duration"] = new NumberMemberValue
            {
                id = "inherited-invalid-duration",
                value = 1,
            };
            data.values["inherited-invalid-frames"] = new ArrayMemberValue
            {
                id = "inherited-invalid-frames",
                value = Array.Empty<string>(),
            };
            data.values["inherited-invalid-tracks"] = new ArrayMemberValue
            {
                id = "inherited-invalid-tracks",
                value = Array.Empty<string>(),
            };

            var error = Assert.Throws<InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(data));

            StringAssert.Contains("InheritedClip", error!.Message);
            StringAssert.Contains("FPS must be at least 1", error.Message);
        }

        [Test]
        public void VariantApplication_LeavesTheReceiversPlayingClipAlone()
        {
            // P67 §7.2. Applying a variant writes members; it does not end the
            // object's life. The short-lived wrapper the application borrows
            // over the receiver's node must therefore NOT release the backing
            // value's clips when it is disposed — `ReleaseAnimationClips` is
            // keyed on the value identity, not on the wrapper.
            ProjectData data = BuildPlacementAnimationProjectData();
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            var target = (TestComposedObject)placed.Info;
            NeoAnimationClip<TestComposedObject> clip =
                NeoGeneratedTypesSupport.GetAnimationClip(target, "Animate");
            clip.PlayLoop();

            using (var borrowed = new NeoVariantTargetValue(
                       client,
                       target.BackingNode,
                       target.ValueOwnership))
            {
                Assert.IsNotNull(borrowed);
            }

            Assert.IsTrue(clip.IsPlaying);
            Assert.AreSame(
                clip,
                NeoGeneratedTypesSupport.GetAnimationClip(target, "Animate"));
        }

        [Test]
        public void AnimationCacheInvalidation_StopsPlayersAndRebuildsHandles()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            var target = (TestComposedObject)placed.Info;
            NeoAnimationClip<TestComposedObject> first =
                NeoGeneratedTypesSupport.GetAnimationClip(target, "Animate");
            first.PlayLoop();

            NeoClient.InvalidateAllAnimationClips();
            NeoAnimationClip<TestComposedObject> replacement =
                NeoGeneratedTypesSupport.GetAnimationClip(target, "Animate");

            Assert.IsFalse(first.IsPlaying);
            Assert.AreNotSame(first, replacement);
        }

        [Test]
        public void AnimationTargetRelease_AllowsOnStopToMutateClipCache()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            var target = (TestComposedObject)placed.Info;
            NeoAnimationClip<TestComposedObject> clip =
                NeoGeneratedTypesSupport.GetAnimationClip(target, "Animate");
            clip.OnStop += client.InvalidateAnimationClips;
            clip.PlayLoop();

            Assert.DoesNotThrow(target.Dispose);
            Assert.IsFalse(clip.IsPlaying);
        }

        [Test]
        public void AnimationOverride_SaveOwnedLeafWritesOnlyToSaveOverlay()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            data.members["object-position-member"].DeclaredStorage = NeoMemberStorage.Save;
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            string positionId = PlacedChildPositionId(client, placed.InstanceId.Value);

            NeoAnimationClip<TestComposedObject> clip =
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)placed.Info,
                    "Animate");
            clip.PlayOnce();

            Assert.AreEqual(
                9,
                ((Vector3MemberValue)client.saveValues[positionId]).value!.x);
            Assert.IsFalse(client.sessionValues.ContainsKey(positionId));
        }

        [Test]
        public void AnimationChildTrack_BackwardBoomerangUsesResolvedChildFrames()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            ((NumberMemberValue)data.values["child-clip-duration"]).value = 2;
            ((ArrayMemberValue)data.values["child-clip-frames"]).value =
                new[] { "child-frame-0", "child-frame-1" };
            // Exactly as long as the child, since P48 §2.3 stops a track
            // writing once its window is exhausted rather than clamping it to
            // the child's last frame. A third parent frame would now be a
            // no-write frame, which is a different assertion (see
            // AnimationChildTrack_HoldTailDoesNotRewriteUnchangedChildFrame).
            ((NumberMemberValue)data.values["track-parent-duration"]).value = 2;
            ((NumberMemberValue)data.values["track-parent-child-start"]).value = 0;
            data.values["child-frame-1"] = new ObjectMemberValue
            {
                id = "child-frame-1",
                classId = ((ObjectMemberValue)data.values["child-frame-0"]).classId,
                value = new Dictionary<string, string>
                {
                    ["Index"] = "child-frame-1-index",
                    ["Overrides"] = "child-frame-1-values",
                },
            };
            data.values["child-frame-1-index"] = new NumberMemberValue
            {
                id = "child-frame-1-index",
                value = 1,
            };
            data.values["child-frame-1-values"] = new ObjectMemberValue
            {
                id = "child-frame-1-values",
                classId = ObjectClassId,
                value = new Dictionary<string, string>
                {
                    ["Position"] = "child-frame-1-position",
                },
            };
            data.values["child-frame-1-position"] = new Vector3MemberValue
            {
                id = "child-frame-1-position",
                value = new NeoVector3Value { x = 8, y = 0, z = 0 },
            };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            string positionId = PlacedChildPositionId(client, placed.InstanceId.Value);
            NeoAnimationClip<TestComposedObject> clip =
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)placed.Info,
                    "TrackAnimate");

            clip.PlayLoop(
                NeoPlayMode.Boomerang,
                NeoPlayDirection.Reverse);
            Assert.AreEqual(
                8,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x);
            clip.Tick(0.1f);
            Assert.AreEqual(
                7,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x);
            clip.Tick(0.1f);
            Assert.AreEqual(
                8,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x);
            clip.Tick(0.1f);
            Assert.AreEqual(
                7,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x);
        }

        /// <summary>
        /// P48 §2.1 / acceptance 5: a child <b>clip</b> plays reversed inside a
        /// parent, authored entirely on the track row. The reversal is
        /// <c>t → (D − 1) − t</c> over the child's resolved timeline, so the
        /// parent's first frame shows the child's last.
        /// </summary>
        [Test]
        public void AnimationChildTrack_ReversePlaysTheChildTimelineBackwards()
        {
            ProjectData data = TwoFrameChildClipAnimationProjectData();
            ((ObjectMemberValue)data.values["track-parent-child"])
                .value!["Direction"] = "track-direction-reverse";
            data.values["track-direction-reverse"] = new ArrayMemberValue
            {
                id = "track-direction-reverse",
                value = new[] { NeoPlayDirection.Reverse.optionId },
            };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            string positionId = PlacedChildPositionId(client, placed.InstanceId.Value);
            NeoAnimationClip<TestComposedObject> clip =
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)placed.Info,
                    "TrackAnimate");

            clip.PlayOnce();
            Assert.AreEqual(
                8,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x,
                "a reversed child clip enters at its LAST frame");
            clip.Tick(0.1f);
            Assert.AreEqual(
                7,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x);
        }

        /// <summary>
        /// P48 §2.3 / acceptance 6: the crop window's edges are the two
        /// offsets, applied in the child's own frame space.
        /// </summary>
        [Test]
        public void AnimationChildTrack_CropWindowTrimsTheChildTimeline()
        {
            ProjectData data = TwoFrameChildClipAnimationProjectData();
            ((ObjectMemberValue)data.values["track-parent-child"])
                .value!["OffsetStartIndex"] = "track-crop-start";
            data.values["track-crop-start"] =
                new NumberMemberValue { id = "track-crop-start", value = 1 };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            string positionId = PlacedChildPositionId(client, placed.InstanceId.Value);
            NeoAnimationClip<TestComposedObject> clip =
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)placed.Info,
                    "TrackAnimate");

            clip.PlayOnce();
            Assert.AreEqual(
                8,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x,
                "cropping the first frame out starts the lane on the second");
            // Frame 1 is past the one-frame window, so the track writes nothing
            // and the member keeps its last value rather than replaying.
            ((Vector3MemberValue)client.sessionValues[positionId]).value!.x = 42;
            clip.Tick(0.1f);
            Assert.AreEqual(
                42,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x);
        }

        /// <summary>
        /// P48 §2.3 / acceptance 6: P29's fit error is deleted. A child track
        /// whose content runs past the owning clip's end loads, compiles, and
        /// plays — the tail simply truncates.
        /// </summary>
        [Test]
        public void AnimationChildTrack_OverrunTruncatesInsteadOfFailingTheLoad()
        {
            ProjectData data = TwoFrameChildClipAnimationProjectData();
            // Two child frames scheduled from parent frame 1 of a 2-frame
            // parent: the second child frame lands at parent frame 2, past the
            // end. P29 rejected this document outright.
            ((NumberMemberValue)data.values["track-parent-child-start"]).value = 1;

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            string positionId = PlacedChildPositionId(client, placed.InstanceId.Value);
            NeoAnimationClip<TestComposedObject> clip =
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)placed.Info,
                    "TrackAnimate");

            clip.PlayOnce();
            Assert.AreEqual(
                1,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x,
                "nothing plays before StartFrame");
            clip.Tick(0.1f);
            Assert.AreEqual(
                7,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x,
                "the first child frame still plays; only the tail truncates");
        }

        /// <summary>
        /// The placement fixture with a two-frame child clip and a parent whose
        /// Duration matches it, so a direction or crop change is the only
        /// variable between the tests above.
        /// </summary>
        private static ProjectData TwoFrameChildClipAnimationProjectData()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            ((NumberMemberValue)data.values["child-clip-duration"]).value = 2;
            ((ArrayMemberValue)data.values["child-clip-frames"]).value =
                new[] { "child-frame-0", "child-frame-1" };
            ((NumberMemberValue)data.values["track-parent-duration"]).value = 2;
            ((NumberMemberValue)data.values["track-parent-child-start"]).value = 0;
            data.values["child-frame-1"] = new ObjectMemberValue
            {
                id = "child-frame-1",
                classId = ((ObjectMemberValue)data.values["child-frame-0"]).classId,
                value = new Dictionary<string, string>
                {
                    ["Index"] = "child-frame-1-index",
                    ["Overrides"] = "child-frame-1-values",
                },
            };
            data.values["child-frame-1-index"] =
                new NumberMemberValue { id = "child-frame-1-index", value = 1 };
            data.values["child-frame-1-values"] = new ObjectMemberValue
            {
                id = "child-frame-1-values",
                classId = ObjectClassId,
                value = new Dictionary<string, string>
                {
                    ["Position"] = "child-frame-1-position",
                },
            };
            data.values["child-frame-1-position"] = new Vector3MemberValue
            {
                id = "child-frame-1-position",
                value = new NeoVector3Value { x = 8, y = 0, z = 0 },
            };
            return data;
        }

        [Test]
        public void AnimationChildTrack_HoldTailDoesNotRewriteUnchangedChildFrame()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            ((NumberMemberValue)data.values["track-parent-duration"]).value = 4;
            ((NumberMemberValue)data.values["track-parent-child-start"]).value = 0;
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            string positionId = PlacedChildPositionId(client, placed.InstanceId.Value);
            NeoAnimationClip<TestComposedObject> clip =
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)placed.Info,
                    "TrackAnimate");

            clip.PlayOnce();
            Assert.AreEqual(
                7,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x);
            ((Vector3MemberValue)client.sessionValues[positionId]).value!.x = 42;
            clip.Tick(0.1f);
            clip.Tick(0.1f);

            Assert.AreEqual(
                42,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x,
                "the child hold tail must not re-apply the same last frame");
        }

        [Test]
        public void AnimationLegacyPlacementWithoutCloneProvenanceFailsWithMigrationPath()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            var placement = (ObjectMemberValue)client.saveValues[placed.InstanceId.Value];
            string childListId = placement.value!["Children"];
            string childId = ((ArrayMemberValue)client.saveValues[childListId]).value![0];
            client.saveValues[childId].sourceValueId = null;

            var error = Assert.Throws<InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)placed.Info,
                    "Animate"));

            StringAssert.Contains("legacy pre-0.7 placement", error!.Message);
            StringAssert.Contains("Migrate or recreate", error.Message);
        }

        [Test]
        public void AnimationUnresolvableChildOverrideSkipsAndWarnsExactlyOnce()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            string positionId = PlacedChildPositionId(client, placed.InstanceId.Value);
            RepointPlacedChildProvenance(client, placed.InstanceId.Value);
            LogAssert.Expect(
                LogType.Warning,
                new Regex("child override skipped: no placed Children row"));

            NeoAnimationClip<TestComposedObject> clip =
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)placed.Info,
                    "Animate");
            clip.PlayOnce();

            Assert.AreEqual(
                1,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x,
                "a skipped child override must leave the placed row untouched");
            // Compile-time, once per reference: the warning does not repeat per
            // tick, and nothing else logged.
            clip.Tick(0.1f);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// The spec's granularity is one log per (clip, reference). The clip
        /// cache alone only gets to once per <em>instance</em>, so fifty
        /// placements missing the same authored slot would log fifty times.
        /// </summary>
        [Test]
        public void AnimationUnresolvableChildOverrideWarnsOncePerReferenceAcrossPlacements()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoTileGridPrimitive primitive = NeoTileGridPrimitive.ResolveForSave(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string>
                {
                    [typeof(TestComposedObject)] = ObjectClassId,
                });
            var layer = primitive.BindWritableObjectLayer<TestAuthoredObjectLayer>(
                ObjectsLayerClassId,
                new[] { ObjectClassId });
            var asset = (TestComposedObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories())!;
            Assert.IsTrue(layer.Spawn(new Vector2Int(4, 5), asset).Ok);
            Assert.IsTrue(layer.Spawn(new Vector2Int(6, 7), asset).Ok);
            NeoResolvedObjectInstance first = layer.GetObject(new Vector2Int(4, 5))!;
            NeoResolvedObjectInstance second = layer.GetObject(new Vector2Int(6, 7))!;
            // Both placements lose the same authored slot, so both compiles
            // reach the same (clip, reference) skip.
            RepointPlacedChildProvenance(client, first.InstanceId.Value);
            RepointPlacedChildProvenance(client, second.InstanceId.Value);
            // One Expect: a second warning fails NoUnexpectedReceived below.
            LogAssert.Expect(
                LogType.Warning,
                new Regex("child override skipped: no placed Children row"));

            // Two separate compiles: the clip cache is keyed per instance, so
            // nothing but the client-level dedup stops the second warning.
            NeoAnimationClip<TestComposedObject> firstClip =
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)first.Info,
                    "Animate");
            NeoAnimationClip<TestComposedObject> secondClip =
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)second.Info,
                    "Animate");

            Assert.IsNotNull(firstClip);
            Assert.IsNotNull(secondClip);
            Assert.AreNotSame(
                firstClip,
                secondClip,
                "each placement must compile its own clip, or the dedup is "
                    + "not the thing under test");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// Repoints a placement's only <c>Children</c> row at an authored id no
        /// clip names, so the clip's reference resolves to nothing while every
        /// row still carries provenance — the absent-slot row of the spec's
        /// section 2.2 table, not the legacy pre-0.7 row.
        /// </summary>
        private static void RepointPlacedChildProvenance(
            NeoClient client,
            string placementId)
        {
            var placement = (ObjectMemberValue)client.saveValues[placementId];
            string childListId = placement.value!["Children"];
            string childId = ((ArrayMemberValue)client.saveValues[childListId]).value![0];
            client.saveValues[childId].sourceValueId = "absent-authored-child";
        }

        [Test]
        public void AnimationChildOverrideMatchingTwoPlacedRowsStillThrowsAsAmbiguous()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            AppendSecondAuthoredChild(data);
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            Dictionary<string, string> placedChildIds =
                PlacedChildIdsByAuthoredId(client, placed.InstanceId.Value);
            // Both placed rows now claim the one authored child the clip names.
            client.saveValues[placedChildIds["shop-authored-second-child"]].sourceValueId =
                "shop-authored-child";

            var error = Assert.Throws<InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)placed.Info,
                    "Animate"));

            StringAssert.Contains("maps to multiple placed Children rows", error!.Message);
        }

        [Test]
        public void AnimationSkippedChildTrackIsExcludedFromTheParentScheduleChecks()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            RepointPlacedChildProvenance(client, placed.InstanceId.Value);
            // Pushed past the end of the parent AFTER load, so the authored-graph
            // export check (which already ran and stays strict, section 2.4) is
            // not the one under test: StartFrame 5 on a 2-frame parent is P48
            // §2.3's "the row can never play" error, and the skipped track must
            // never reach it — an optional slot this instance does not have
            // cannot be a reason to fail the whole clip.
            ((NumberMemberValue)client.values["track-parent-child-start"]).value = 5;
            LogAssert.Expect(
                LogType.Warning,
                new Regex("child track 'ChildAnimate' skipped"));

            NeoAnimationClip<TestComposedObject> clip =
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)placed.Info,
                    "TrackAnimate");

            Assert.IsNotNull(clip);
            Assert.DoesNotThrow(() => clip.PlayOnce());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AnimationClipWithOneSkippedTrackStillPlaysTheResolvableTrack()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            AppendSecondAuthoredChild(data);
            AppendSecondChildTrack(data);
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            Dictionary<string, string> placedChildIds =
                PlacedChildIdsByAuthoredId(client, placed.InstanceId.Value);
            string skippedPositionId = ((ObjectMemberValue)client.saveValues[
                placedChildIds["shop-authored-child"]]).value!["Position"];
            string playedPositionId = ((ObjectMemberValue)client.saveValues[
                placedChildIds["shop-authored-second-child"]]).value!["Position"];
            client.saveValues[placedChildIds["shop-authored-child"]].sourceValueId =
                "absent-authored-child";
            LogAssert.Expect(
                LogType.Warning,
                new Regex("child track 'ChildAnimate' skipped"));

            NeoAnimationClip<TestComposedObject> clip =
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)placed.Info,
                    "TrackAnimate");
            clip.PlayOnce();

            Assert.AreEqual(
                7,
                ((Vector3MemberValue)client.sessionValues[playedPositionId]).value!.x,
                "the resolvable track must still resample against its placed child");
            Assert.AreEqual(
                1,
                ((Vector3MemberValue)client.sessionValues[skippedPositionId]).value!.x,
                "the skipped track must contribute no frames");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AnimationFrameWithOneUnresolvableChildStillAppliesItsOtherWrites()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            AppendSecondAuthoredChild(data);
            AppendSecondChildOverride(data);
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            Dictionary<string, string> placedChildIds =
                PlacedChildIdsByAuthoredId(client, placed.InstanceId.Value);
            string skippedPositionId = ((ObjectMemberValue)client.saveValues[
                placedChildIds["shop-authored-child"]]).value!["Position"];
            string siblingPositionId = ((ObjectMemberValue)client.saveValues[
                placedChildIds["shop-authored-second-child"]]).value!["Position"];
            client.saveValues[placedChildIds["shop-authored-child"]].sourceValueId =
                "absent-authored-child";
            LogAssert.Expect(
                LogType.Warning,
                new Regex("frame 0 child override skipped"));

            NeoAnimationClip<TestComposedObject> clip =
                NeoGeneratedTypesSupport.GetAnimationClip(
                    (TestComposedObject)placed.Info,
                    "Animate");
            clip.PlayOnce();

            Assert.AreEqual(
                4,
                ((Vector3MemberValue)client.sessionValues[siblingPositionId]).value!.x,
                "the frame's other child override must still apply");
            Assert.AreEqual(
                1,
                ((Vector3MemberValue)client.sessionValues[skippedPositionId]).value!.x,
                "skipping is scoped to the single unresolvable reference");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AnimationClipWritesToADisabledObjectJustAsItDoesToAnEnabledOne()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            var target = (TestComposedObject)placed.Info;
            string positionId = PlacedChildPositionId(client, placed.InstanceId.Value);
            // Visibility, not lifecycle: a hidden slot is still a live value.
            target.Enabled = false;

            NeoAnimationClip<TestComposedObject> clip =
                NeoGeneratedTypesSupport.GetAnimationClip(target, "Animate");
            clip.PlayOnce();

            Assert.AreEqual(
                9,
                ((Vector3MemberValue)client.sessionValues[positionId]).value!.x,
                "a visibility flag must never gate value writes");
            Assert.IsFalse(target.Enabled, "playback must not touch Enabled");
        }

        /// <summary>
        /// Maps each placed <c>Children</c> row back to the authored row it was
        /// cloned from, so a test can target one slot by authored id instead of
        /// by list position.
        /// </summary>
        private static Dictionary<string, string> PlacedChildIdsByAuthoredId(
            NeoClient client,
            string placementId)
        {
            var placement = (ObjectMemberValue)client.saveValues[placementId];
            var children =
                (ArrayMemberValue)client.saveValues[placement.value!["Children"]];
            var byAuthoredId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string childId in children.value!)
            {
                byAuthoredId[client.saveValues[childId].sourceValueId!] = childId;
            }
            return byAuthoredId;
        }

        /// <summary>
        /// Gives the placement-animation fixture's object a second authored
        /// child, so a test can leave one slot resolvable while breaking the
        /// other.
        /// </summary>
        private static void AppendSecondAuthoredChild(ProjectData data)
        {
            data.values["shop-authored-second-child"] = new ObjectMemberValue
            {
                id = "shop-authored-second-child",
                classId = ((ObjectMemberValue)data.values["shop-authored-child"]).classId,
                value = new Dictionary<string, string>
                {
                    ["Position"] = "shop-authored-second-child-position",
                },
            };
            data.values["shop-authored-second-child-position"] = new Vector3MemberValue
            {
                id = "shop-authored-second-child-position",
                value = new NeoVector3Value { x = 2, y = 0, z = 0 },
            };
            ((ArrayMemberValue)data.values["shop-authored-children"]).value =
                new[] { "shop-authored-child", "shop-authored-second-child" };
        }

        /// <summary>
        /// Adds a second child track to the fixture's TrackAnimate clip, aimed
        /// at the second authored child and starting at frame 0. Requires
        /// <see cref="AppendSecondAuthoredChild"/>.
        /// </summary>
        private static void AppendSecondChildTrack(ProjectData data)
        {
            data.values["track-parent-second-child-lookup"] = new ArrayMemberValue
            {
                id = "track-parent-second-child-lookup",
                value = new[] { "shop-authored-second-child" },
            };
            data.values["track-parent-second-child-key"] = new StringMemberValue
            {
                id = "track-parent-second-child-key",
                value = "ChildAnimate",
            };
            data.values["track-parent-second-child-start"] = new NumberMemberValue
            {
                id = "track-parent-second-child-start",
                value = 0,
            };
            data.values["track-parent-second-child"] = new ObjectMemberValue
            {
                id = "track-parent-second-child",
                classId = ((ObjectMemberValue)data.values["track-parent-child"]).classId,
                value = new Dictionary<string, string>
                {
                    ["Child"] = "track-parent-second-child-lookup",
                    ["ClipKey"] = "track-parent-second-child-key",
                    ["StartFrame"] = "track-parent-second-child-start",
                },
            };
            ((ArrayMemberValue)data.values["track-parent-tracks"]).value =
                new[] { "track-parent-child", "track-parent-second-child" };
        }

        /// <summary>
        /// Gives the fixture's Animate frame a second child override (aimed at
        /// the second authored child) plus its own Overrides on the placement
        /// root. Requires <see cref="AppendSecondAuthoredChild"/>.
        /// </summary>
        private static void AppendSecondChildOverride(ProjectData data)
        {
            data.values["parent-second-child-lookup"] = new ArrayMemberValue
            {
                id = "parent-second-child-lookup",
                value = new[] { "shop-authored-second-child" },
            };
            data.values["parent-second-child-position-override"] = new Vector3MemberValue
            {
                id = "parent-second-child-position-override",
                value = new NeoVector3Value { x = 4, y = 0, z = 0 },
            };
            data.values["parent-second-child-values"] = new ObjectMemberValue
            {
                id = "parent-second-child-values",
                classId = ((ObjectMemberValue)data.values["parent-child-values"]).classId,
                value = new Dictionary<string, string>
                {
                    ["Position"] = "parent-second-child-position-override",
                },
            };
            data.values["parent-second-child-override"] = new ObjectMemberValue
            {
                id = "parent-second-child-override",
                classId = ((ObjectMemberValue)data.values["parent-child-override"]).classId,
                value = new Dictionary<string, string>
                {
                    ["Child"] = "parent-second-child-lookup",
                    ["Overrides"] = "parent-second-child-values",
                },
            };
            ((ArrayMemberValue)data.values["parent-frame-0-child-overrides"]).value =
                new[] { "parent-child-override", "parent-second-child-override" };
        }

        private static NeoResolvedObjectInstance SpawnAnimationTestObject(NeoClient client)
        {
            NeoTileGridPrimitive primitive = NeoTileGridPrimitive.ResolveForSave(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string>
                {
                    [typeof(TestComposedObject)] = ObjectClassId,
                });
            var layer = primitive.BindWritableObjectLayer<TestAuthoredObjectLayer>(
                ObjectsLayerClassId,
                new[] { ObjectClassId });
            var asset = (TestComposedObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories())!;
            Assert.IsTrue(layer.Spawn(new Vector2Int(4, 5), asset).Ok);
            return layer.GetObject(new Vector2Int(4, 5))!;
        }

        [Test]
        public void SchemaNineSharedLayerClassCreatesDistinctGridBoundInstances()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.values["winter-grid"] = new ObjectMemberValue
            {
                id = "winter-grid",
                classId = GridClassId,
                value = new Dictionary<string, string>
                {
                    ["Children"] = "winter-grid-children",
                },
            };
            data.values["winter-grid-children"] = new ArrayMemberValue
            {
                id = "winter-grid-children",
                value = new[] { "winter-background-link" },
            };
            data.values["winter-background-link"] = new ObjectMemberValue
            {
                id = "winter-background-link",
                classId = TileLayerLinkClassId,
                value = new Dictionary<string, string>
                {
                    ["layerClassId"] = BackgroundLayerClassId,
                    ["Tiles"] = "winter-background-tiles",
                },
            };
            data.values["winter-background-tiles"] = new ArrayMemberValue
            {
                id = "winter-background-tiles",
                value = Array.Empty<string>(),
            };
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            var town = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                writableFactories);
            var winter = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "winter-grid",
                factories,
                writableFactories);

            var townLayer = town.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });
            var winterLayer = winter.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });

            Assert.AreEqual(townLayer.GetType(), winterLayer.GetType());
            Assert.AreNotSame(townLayer, winterLayer);
            Assert.AreEqual("background-layer-override", townLayer.LayerOverrideValueId);
            Assert.IsNull(winterLayer.LayerOverrideValueId);
        }

        [Test]
        public void SchemaNineLayerLinkRelationRoutesClassBackedTilesAndSubscriptions()
        {
            var client = NeoTestSaveStack.ClientFromSchema(
                BuildClassBackedTileGridProjectData());
            SeedWritableTileLayerLink(client);
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            var source = (TestTileLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-floor-link",
                factories,
                writableFactories)!;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                writableFactories);
            var layer = primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });
            var tile = layer.GetTile(new Vector2Int(9, 22));
            Assert.NotNull(tile);
            Assert.AreEqual(BackgroundLayerClassId, tile!.LayerId);
            Assert.IsNull(tile.Info.valueId);
            var sprite = CreateTestSprite("schema-nine-live-linked");
            ((TestTile)tile.Info).Sprite = sprite;
            var content = new TestTileGridContent(primitive, new[] { layer });
            var go = new GameObject("NeoTileGridRenderer schema-nine source clear test");
            var changed = 0;

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(content);
                using var subscription = layer.OnChanged(_ => changed++);
                var tilemap = go.GetComponentInChildren<Tilemap>();

                Assert.NotNull(tilemap);
                Assert.NotNull(tilemap!.GetTile(new Vector3Int(9, 22, 0)));

                source.ClearTiles();

                Assert.AreEqual(1, changed);
                Assert.IsNull(layer.GetTile(new Vector2Int(9, 22)));
                Assert.IsNull(tilemap.GetTile(new Vector3Int(9, 22, 0)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(sprite.texture);
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        [Test]
        public void SchemaNineMultipleDirectSourceLinksAggregateAndKeepPrimaryWriteOwner()
        {
            ProjectData data = BuildClassBackedTileGridProjectData();
            var backgroundLink = (ObjectMemberValue)data.values["background-link"];
            backgroundLink.value!.Remove("layerOverrideValueId");
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoTileGridPrimitive.ResolveForSave(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string> { [typeof(TestTile)] = TileClassId });
            var layer = primitive.BindWritableTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });

            Assert.NotNull(layer.GetTile(new Vector2Int(12, 13)));

            var placed = layer.Place<TestTile>(new Vector2Int(14, 15));

            Assert.IsTrue(placed.Ok, placed.Message);
            var created = layer.GetTile(new Vector2Int(14, 15));
            Assert.NotNull(created);
            var placement = (ObjectMemberValue)client.saveValues[created!.InstanceId.Value];
            Assert.AreEqual("background-link-tiles", placement.containerId);
            Assert.AreNotEqual("blocked-path-tiles", placement.containerId);
        }

        [Test]
        public void TileLayerLinkPayloadsResolveThroughCurrentObjectPosition()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildClassBackedTileGridProjectData());
            var factories = BuildClassBackedReadOnlyFactories();
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                BuildClassBackedWritableFactories());

            var tiles = primitive.GetTiles(BackgroundLayerClassId, TileClassId);
            var candidates = primitive.GetTileCandidates<TestTile>(
                BackgroundLayerClassId,
                new Vector2Int(9, 22),
                TileClassId);

            Assert.AreEqual(3, tiles.Count, "all class-backed sources should aggregate");
            var projected = tiles.Single(tile => tile.SourceTileLayerLinkId == "shop-floor-link");
            Assert.AreEqual(new Vector2Int(9, 22), projected.Cell);
            Assert.AreEqual(NeoTileOutputSourceKind.TileLayerLink, projected.SourceKind);
            Assert.AreEqual("shop-1:shop-floor-link:floor-local", projected.InstanceId.Value);
            Assert.AreEqual("shop-1", projected.SourceObjectInstanceId);
            Assert.IsInstanceOf<TestTile>(projected.Info);
            Assert.AreEqual(1, candidates.Count, "only the projected source occupies (9, 22)");
            Assert.AreEqual(NeoTileOutputSourceKind.TileLayerLink, candidates[0].SourceKind);
        }

        [Test]
        public void TileLayerLinkPayloadsStopResolvingWhenSourceTilesAreCleared()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildClassBackedTileGridProjectData());
            SeedWritableTileLayerLink(client);
            var readOnlyFactories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            var source = (TestTileLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-floor-link",
                readOnlyFactories,
                writableFactories)!;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                readOnlyFactories,
                writableFactories);

            Assert.AreEqual(
                3,
                primitive.GetTiles(BackgroundLayerClassId, TileClassId).Count,
                "fixture should expose direct, projected, and blocked-path tiles before clear");

            source.ClearTiles();

            var remaining = primitive.GetTiles(BackgroundLayerClassId, TileClassId);
            Assert.AreEqual(2, remaining.Count);
            Assert.IsFalse(remaining.Any(tile => tile.SourceTileLayerLinkId == "shop-floor-link"));
        }

        [Test]
        public void Render_LiveSyncClearsProjectedTilesWhenSourceTilesAreCleared()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildClassBackedTileGridProjectData());
            SeedWritableTileLayerLink(client);
            var readOnlyFactories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            var source = (TestTileLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-floor-link",
                readOnlyFactories,
                writableFactories)!;
            var tile = (TestTile)NeoGeneratedTypesSupport.CreateReadOnlyClassDefault(
                client,
                TileClassId,
                readOnlyFactories);
            var sprite = CreateTestSprite("live-linked");
            tile.Sprite = sprite;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                readOnlyFactories,
                writableFactories);
            var layer = new ThrowingAfterInitialTileLayerRuntime(primitive);
            var content = new TestTileGridContent(primitive, new[] { layer });
            var go = new GameObject("NeoTileGridRenderer live source clear test");
            var changed = 0;

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(content);
                using var subscription = layer.OnChanged(_ => changed++);

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap, "renderer should create the layer tilemap");
                Assert.IsTrue(renderer.IsLiveSynced);
                Assert.AreSame(content, renderer.CurrentContent);
                Assert.AreEqual(0, layer.GetTilesCalls);
                Assert.AreEqual(1, layer.GetRenderSnapshotCalls);
                Assert.IsNotNull(
                    tilemap!.GetTile(new Vector3Int(9, 22, 0)),
                    "projected class-default tile should render at the object-relative cell");

                layer.ThrowOnGetTiles = true;
                layer.ThrowOnGetRenderSnapshot = true;
                layer.ThrowOnGetTile = true;
                source.ClearTiles();

                Assert.AreEqual(1, changed);
                Assert.IsNull(tilemap.GetTile(new Vector3Int(9, 22, 0)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(sprite.texture);
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        [UnityTest]
        public IEnumerator RenderAsync_LiveSyncFalseKeepsOneShotRenderedTiles()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildClassBackedTileGridProjectData());
            SeedWritableTileLayerLink(client);
            var readOnlyFactories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            var source = (TestTileLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-floor-link",
                readOnlyFactories,
                writableFactories)!;
            var tile = (TestTile)NeoGeneratedTypesSupport.CreateReadOnlyClassDefault(
                client,
                TileClassId,
                readOnlyFactories);
            var sprite = CreateTestSprite("one-shot-linked");
            tile.Sprite = sprite;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                readOnlyFactories,
                writableFactories);
            var layer = new TestGeneratedTileLayerRuntime(primitive);
            var content = new TestTileGridContent(primitive, new[] { layer });
            var go = new GameObject("NeoTileGridRenderer one-shot source clear test");
            Exception? error = null;
            var complete = false;

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                Render();

                for (int frame = 0; frame < 120 && !complete; frame += 1)
                {
                    yield return null;
                }

                if (error != null) throw error;
                Assert.IsTrue(complete, "Async tile grid render did not complete.");

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap, "renderer should create the one-shot layer tilemap");
                Assert.IsFalse(renderer.IsLiveSynced);
                Assert.IsNotNull(
                    tilemap!.GetTile(new Vector3Int(9, 22, 0)),
                    "projected class-default tile should be present after one-shot render");

                source.ClearTiles();

                Assert.AreEqual(2, primitive.GetTiles(BackgroundLayerClassId, TileClassId).Count);
                Assert.IsNotNull(tilemap.GetTile(new Vector3Int(9, 22, 0)));

                async void Render()
                {
                    try
                    {
                        await renderer.RenderAsync(
                            content,
                            new NeoTileGridRenderOptions
                            {
                                LiveSync = false,
                                YieldBeforeRender = false,
                            });
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                    }
                    finally
                    {
                        complete = true;
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(sprite.texture);
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        [Test]
        public void Render_AppliesAuthoredTileLayerSorting()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");
            var go = new GameObject("NeoTileGridRenderer sorting test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    primitive,
                    new[]
                    {
                        new TestTileLayerRuntime(
                            "background-layer",
                            "Background",
                            TileClassId,
                            "Default",
                            42),
                    });

                var tilemapRenderer = go.GetComponentInChildren<TilemapRenderer>();
                Assert.IsNotNull(tilemapRenderer);
                Assert.AreEqual("Default", tilemapRenderer!.sortingLayerName);
                Assert.AreEqual(42, tilemapRenderer.sortingOrder);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Render_DefaultTargetProviderHooksBracketInitialPaintingAndLegacyLifecycle()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var sprite = CreateTestSprite("provider-default-target");
            tile.Sprite = sprite;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
            var phases = new List<string>();
            var layer = new RecordingProviderTileLayerRuntime(
                "background-layer",
                "Background",
                TileClassId,
                null,
                null,
                new[]
                {
                    new NeoResolvedTileInstance(
                        "tile-1",
                        "background-layer",
                        Vector2Int.zero,
                        tile,
                        0),
                },
                phases);
            layer.CreatedCallback = context =>
                Assert.IsNull(context.Target.Tilemap.GetTile(Vector3Int.zero));
            layer.InitiallyRenderedCallback = context =>
                Assert.IsNotNull(context.Target.Tilemap.GetTile(Vector3Int.zero));
            var go = new GameObject("NeoTileGridRenderer default provider target test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Lifecycle = new TileLayerCreatedCallbackLifecycle(
                    () => phases.Add("legacy-created"));

                renderer.Render(primitive, new[] { layer });

                CollectionAssert.AreEqual(
                    new[]
                    {
                        "create",
                        "provider-created",
                        "legacy-created",
                        "initially-rendered",
                    },
                    phases);
                var target = layer.CreatedTargets.Single();
                Assert.AreSame(target.Root, target.Tilemap.gameObject);
                Assert.AreEqual("Tile Layer - Background", target.Root.name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(sprite);
            }
        }

        [UnityTest]
        public IEnumerator RenderAsync_ProviderAndLegacyLifecycleMatchSynchronousPhaseOrder()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");
            var syncPhases = new List<string>();
            var asyncPhases = new List<string>();
            var syncLayer = new RecordingProviderTileLayerRuntime(
                "sync-layer",
                "Sync",
                TileClassId,
                null,
                null,
                Array.Empty<NeoResolvedTileInstance>(),
                syncPhases);
            var asyncLayer = new RecordingProviderTileLayerRuntime(
                "async-layer",
                "Async",
                TileClassId,
                null,
                null,
                Array.Empty<NeoResolvedTileInstance>(),
                asyncPhases);
            var syncGo = new GameObject("NeoTileGridRenderer provider sync timing test");
            var asyncGo = new GameObject("NeoTileGridRenderer provider async timing test");
            Exception? error = null;
            var complete = false;

            try
            {
                var syncRenderer = syncGo.AddComponent<NeoTileGridRenderer>();
                syncRenderer.Lifecycle = new TileLayerCreatedCallbackLifecycle(
                    () => syncPhases.Add("legacy-created"));
                syncRenderer.Render(primitive, new[] { syncLayer });

                var asyncRenderer = asyncGo.AddComponent<NeoTileGridRenderer>();
                asyncRenderer.Lifecycle = new TileLayerCreatedCallbackLifecycle(
                    () => asyncPhases.Add("legacy-created"));
                RenderAsync();

                for (int frame = 0; frame < 120 && !complete; frame += 1)
                {
                    yield return null;
                }

                if (error != null) throw error;
                Assert.IsTrue(complete, "Async provider timing render did not complete.");
                CollectionAssert.AreEqual(syncPhases, asyncPhases);
                CollectionAssert.AreEqual(
                    new[]
                    {
                        "create",
                        "provider-created",
                        "legacy-created",
                        "initially-rendered",
                    },
                    asyncPhases);

                async void RenderAsync()
                {
                    try
                    {
                        await asyncRenderer.RenderAsync(
                            primitive,
                            new[] { asyncLayer },
                            options: new NeoTileGridRenderOptions
                            {
                                YieldBeforeRender = false,
                            });
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                    }
                    finally
                    {
                        complete = true;
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(syncGo);
                UnityEngine.Object.DestroyImmediate(asyncGo);
            }
        }

        [Test]
        public void Render_CustomNestedTargetReceivesInitialAndLiveTilesAndSorting()
        {
            var client = NeoTestSaveStack.ClientFromSchema(
                BuildClassBackedTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var sprite = CreateTestSprite("provider-custom-target");
            tile.Sprite = sprite;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
            var layer = new RecordingProviderTileLayerRuntime(
                "background-layer",
                "Background",
                TileClassId,
                "Default",
                73,
                new[]
                {
                    new NeoResolvedTileInstance(
                        "tile-1",
                        "background-layer",
                        Vector2Int.zero,
                        tile,
                        0),
                });
            layer.TargetFactory = CreateNestedTarget;
            var content = new TestTileGridContent(primitive, new[] { layer });
            var go = new GameObject("NeoTileGridRenderer custom provider target test");
            var changedAfterPainting = false;

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(content);

                var target = layer.CreatedTargets.Single();
                Assert.AreNotSame(target.Root, target.Tilemap.gameObject);
                Assert.AreSame(target.Root.transform, target.Tilemap.transform.parent);
                Assert.IsNotNull(target.Tilemap.GetTile(Vector3Int.zero));
                var tilemapRenderer = target.Tilemap.GetComponent<TilemapRenderer>();
                Assert.AreEqual("Default", tilemapRenderer.sortingLayerName);
                Assert.AreEqual(73, tilemapRenderer.sortingOrder);

                layer.ChangedCallback = context =>
                {
                    changedAfterPainting =
                        context.Target.Tilemap.GetTile(Vector3Int.zero) == null &&
                        context.Target.Tilemap.GetTile(Vector3Int.right) != null;
                };
                layer.SetTiles(new[]
                {
                    new NeoResolvedTileInstance(
                        "tile-2",
                        "background-layer",
                        Vector2Int.right,
                        tile,
                        0),
                });
                primitive.NotifyTileLayerChanged(
                    "background-layer",
                    new[] { Vector2Int.zero },
                    new[] { Vector2Int.right },
                    NeoTileGridChangeSourceKind.Direct,
                    null);

                Assert.IsTrue(changedAfterPainting);
                Assert.AreEqual(1, layer.ChangedContexts.Count);
                Assert.AreSame(target, layer.ChangedContexts[0].Target);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(sprite);
            }
        }

        [Test]
        public void Render_InvalidCustomTargetsHaveDistinctDiagnosticsAndDoNotPoisonRetry()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");

            AssertInvalid(
                context =>
                {
                    var root = new GameObject("Unparented Root");
                    var tilemap = root.AddComponent<Tilemap>();
                    root.AddComponent<TilemapRenderer>();
                    return new NeoTileLayerRenderTarget(root, tilemap);
                },
                "must be parented directly beneath");
            AssertInvalid(
                context =>
                {
                    var root = new GameObject("Missing Renderer Root");
                    root.transform.SetParent(context.Parent, false);
                    var tilemap = root.AddComponent<Tilemap>();
                    return new NeoTileLayerRenderTarget(root, tilemap);
                },
                "must have a TilemapRenderer");
            AssertInvalid(
                context =>
                {
                    var root = new GameObject("Target Root");
                    root.transform.SetParent(context.Parent, false);
                    var outside = new GameObject("Outside Tilemap");
                    outside.transform.SetParent(context.Parent, false);
                    var tilemap = outside.AddComponent<Tilemap>();
                    outside.AddComponent<TilemapRenderer>();
                    return new NeoTileLayerRenderTarget(root, tilemap);
                },
                "must be on the target root or one of its descendants");

            void AssertInvalid(
                Func<NeoTileLayerCreateContext, NeoTileLayerRenderTarget?> factory,
                string expectedCondition)
            {
                var go = new GameObject("NeoTileGridRenderer invalid provider target test");
                try
                {
                    var renderer = go.AddComponent<NeoTileGridRenderer>();
                    var layer = new RecordingProviderTileLayerRuntime(
                        "diagnostic-layer",
                        "Diagnostic",
                        TileClassId);
                    layer.TargetFactory = factory;

                    var error = Assert.Throws<InvalidOperationException>(
                        () => renderer.Render(primitive, new[] { layer }));

                    StringAssert.Contains(nameof(RecordingProviderTileLayerRuntime), error!.Message);
                    StringAssert.Contains("diagnostic-layer", error.Message);
                    StringAssert.Contains(expectedCondition, error.Message);

                    layer.TargetFactory = null;
                    renderer.Render(primitive, new[] { layer });
                    Assert.AreEqual(1, layer.CreatedTargets.Count);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        [Test]
        public void Render_ReusedCustomRootReportsOwningLayer()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");
            var go = new GameObject("NeoTileGridRenderer duplicate provider root test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                var first = new RecordingProviderTileLayerRuntime(
                    "first-layer",
                    "First",
                    TileClassId);
                var second = new RecordingProviderTileLayerRuntime(
                    "second-layer",
                    "Second",
                    TileClassId);
                GameObject? sharedRoot = null;
                first.TargetFactory = context =>
                {
                    sharedRoot = new GameObject("Shared Target Root");
                    sharedRoot.transform.SetParent(context.Parent, false);
                    var firstTilemap = NewTilemap("First Tilemap", sharedRoot.transform);
                    return new NeoTileLayerRenderTarget(sharedRoot, firstTilemap);
                };
                second.TargetFactory = _ =>
                {
                    var secondTilemap = NewTilemap("Second Tilemap", sharedRoot!.transform);
                    return new NeoTileLayerRenderTarget(sharedRoot, secondTilemap);
                };

                var error = Assert.Throws<InvalidOperationException>(
                    () => renderer.Render(primitive, new[] { first, second }));

                StringAssert.Contains("second-layer", error!.Message);
                StringAssert.Contains("already registered to tile layer", error.Message);
                StringAssert.Contains("first-layer", error.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            static Tilemap NewTilemap(string name, Transform parent)
            {
                var tilemapObject = new GameObject(name);
                tilemapObject.transform.SetParent(parent, false);
                var tilemap = tilemapObject.AddComponent<Tilemap>();
                tilemapObject.AddComponent<TilemapRenderer>();
                return tilemap;
            }
        }

        [Test]
        public void Render_ReusedCustomTilemapReportsOwningLayer()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");
            var go = new GameObject("NeoTileGridRenderer duplicate provider tilemap test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                var first = new RecordingProviderTileLayerRuntime(
                    "first-layer",
                    "First",
                    TileClassId);
                var second = new RecordingProviderTileLayerRuntime(
                    "second-layer",
                    "Second",
                    TileClassId);
                NeoTileLayerRenderTarget? sharedTarget = null;
                first.TargetFactory = context =>
                {
                    sharedTarget = CreateNestedTarget(context);
                    return sharedTarget;
                };
                second.TargetFactory = _ => new NeoTileLayerRenderTarget(
                    sharedTarget!.Root,
                    sharedTarget.Tilemap);

                var error = Assert.Throws<InvalidOperationException>(
                    () => renderer.Render(primitive, new[] { first, second }));

                StringAssert.Contains("second-layer", error!.Message);
                StringAssert.Contains("Render target Tilemap", error.Message);
                StringAssert.Contains("already registered to tile layer", error.Message);
                StringAssert.Contains("first-layer", error.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RenderTargetDestruction_ReplacementAndClearNotifyExactlyOnce()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");
            var layer = new RecordingProviderTileLayerRuntime(
                "background-layer",
                "Background",
                TileClassId)
            {
                TargetFactory = CreateNestedTarget,
            };
            var go = new GameObject("NeoTileGridRenderer provider replacement test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(primitive, new[] { layer });
                var replacedId = layer.CreatedTargets.Single().Id;

                renderer.Render(primitive, new[] { layer });

                AssertDestroyPair(
                    layer,
                    replacedId,
                    NeoTileLayerRenderTargetDestroyReason.Replaced);
                var clearedId = layer.CreatedTargets.Last().Id;
                Assert.AreNotEqual(replacedId, clearedId);

                renderer.Clear();

                AssertDestroyPair(
                    layer,
                    clearedId,
                    NeoTileLayerRenderTargetDestroyReason.RendererCleared);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RenderTargetDestruction_ExternalDestroyNotifiesExactlyOnce()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");
            var layer = new RecordingProviderTileLayerRuntime(
                "background-layer",
                "Background",
                TileClassId)
            {
                TargetFactory = CreateNestedTarget,
            };
            var go = new GameObject("NeoTileGridRenderer provider external destroy test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(primitive, new[] { layer });
                var target = layer.CreatedTargets.Single();

                UnityEngine.Object.DestroyImmediate(target.Root);
                renderer.Clear();

                AssertDestroyPair(
                    layer,
                    target.Id,
                    NeoTileLayerRenderTargetDestroyReason.ExternallyDestroyed);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RenderTargetDestruction_CancelledAsyncRenderNotifiesExactlyOnce()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");
            using var cancellation = new CancellationTokenSource();
            var layer = new RecordingProviderTileLayerRuntime(
                "background-layer",
                "Background",
                TileClassId)
            {
                TargetFactory = CreateNestedTarget,
                CreatedCallback = _ => cancellation.Cancel(),
            };
            var go = new GameObject("NeoTileGridRenderer provider cancellation test");
            Exception? error = null;
            var complete = false;

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                RenderAsync();

                Assert.IsTrue(complete, "Cancellation after target creation should be observed immediately.");
                Assert.IsInstanceOf<OperationCanceledException>(error);
                var targetId = layer.CreatedTargets.Single().Id;
                AssertDestroyPair(
                    layer,
                    targetId,
                    NeoTileLayerRenderTargetDestroyReason.RenderCancelled);

                async void RenderAsync()
                {
                    try
                    {
                        await renderer.RenderAsync(
                            primitive,
                            new[] { layer },
                            options: new NeoTileGridRenderOptions
                            {
                                CancellationToken = cancellation.Token,
                                YieldBeforeRender = false,
                            });
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                    }
                    finally
                    {
                        complete = true;
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static NeoTileLayerRenderTarget CreateNestedTarget(
            NeoTileLayerCreateContext context)
        {
            var root = new GameObject("Custom Target Root");
            root.transform.SetParent(context.Parent, false);
            var tilemapObject = new GameObject("Nested Tilemap");
            tilemapObject.transform.SetParent(root.transform, false);
            var tilemap = tilemapObject.AddComponent<Tilemap>();
            tilemapObject.AddComponent<TilemapRenderer>();
            return new NeoTileLayerRenderTarget(root, tilemap);
        }

        private static void AssertDestroyPair(
            RecordingProviderTileLayerRuntime layer,
            string targetId,
            NeoTileLayerRenderTargetDestroyReason reason)
        {
            Assert.AreEqual(
                1,
                layer.DestroyingContexts.Count(context =>
                    context.Target.Id == targetId && context.Reason == reason));
            Assert.AreEqual(
                1,
                layer.DestroyedContexts.Count(context =>
                    context.Target.Id == targetId && context.Reason == reason));
            Assert.AreEqual(
                1,
                layer.DestroyingContexts.Count(context => context.Target.Id == targetId));
            Assert.AreEqual(
                1,
                layer.DestroyedContexts.Count(context => context.Target.Id == targetId));
        }

        [Test]
        public void TryClearTile_UpdatesExistingTilemapWithoutRebuilding()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var sprite = CreateTestSprite("clearable");
            tile.Sprite = sprite;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
            var go = new GameObject("NeoTileGridRenderer incremental clear test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    primitive,
                    new[]
                    {
                        new TestTileLayerRuntime(
                            "background-layer",
                            "Background",
                            TileClassId,
                            null,
                            null,
                            new[]
                            {
                                new NeoResolvedTileInstance(
                                    "tile-1",
                                    "background-layer",
                                    Vector2Int.zero,
                                    tile,
                                    0),
                            }),
                    });

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);
                Assert.IsNotNull(tilemap!.GetTile(Vector3Int.zero));
                var childCount = go.transform.childCount;

                Assert.IsTrue(renderer.TryClearTile("background-layer", Vector2Int.zero));

                Assert.AreEqual(childCount, go.transform.childCount);
                Assert.AreSame(tilemap, go.GetComponentInChildren<Tilemap>());
                Assert.IsNull(tilemap.GetTile(Vector3Int.zero));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(sprite.texture);
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        [UnityTest]
        public IEnumerator RenderAsync_RendersTileLayerOverFrames()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var sprite = CreateTestSprite("async-render");
            tile.Sprite = sprite;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
            var go = new GameObject("NeoTileGridRenderer async render test");
            Exception? error = null;
            var complete = false;

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                Render();

                for (int frame = 0; frame < 120 && !complete; frame += 1)
                {
                    yield return null;
                }

                if (error != null) throw error;
                Assert.IsTrue(complete, "Async tile grid render did not complete.");
                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);
                Assert.IsNotNull(tilemap!.GetTile(Vector3Int.zero));

                async void Render()
                {
                    try
                    {
                        await renderer.RenderAsync(
                            primitive,
                            new[]
                            {
                                new TestTileLayerRuntime(
                                    "background-layer",
                                    "Background",
                                    TileClassId,
                                    null,
                                    null,
                                    new[]
                                    {
                                        new NeoResolvedTileInstance(
                                            "tile-1",
                                            "background-layer",
                                            Vector2Int.zero,
                                            tile,
                                            0),
                                    }),
                            },
                            options: new NeoTileGridRenderOptions
                            {
                                MaxTilesPerFrame = 1,
                                YieldBeforeRender = true,
                            });
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                    }
                    finally
                    {
                        complete = true;
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(sprite.texture);
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        [Test]
        public void RenderAsync_SupersedesInFlightRender_NewestWins()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");
            var go = new GameObject("NeoTileGridRenderer restart test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                Exception? firstError = null;
                var firstCompleted = false;
                var secondCompleted = false;
                var restarted = false;
                // Kick off a second render from inside the first one — the
                // edit-mode equivalent of a caller restarting mid-flight.
                renderer.Lifecycle = new TileLayerCreatedCallbackLifecycle(() =>
                {
                    if (restarted) return;
                    restarted = true;
                    SecondRender();
                });

                FirstRender();

                Assert.IsTrue(restarted, "The first render never reached its tile layer.");
                Assert.IsTrue(secondCompleted, "The newest render must win.");
                Assert.IsFalse(firstCompleted, "The superseded render must not complete.");
                Assert.IsInstanceOf<OperationCanceledException>(firstError);

                async void FirstRender()
                {
                    try
                    {
                        await renderer.RenderAsync(primitive, new[] { EmptyBackgroundLayer() });
                        firstCompleted = true;
                    }
                    catch (Exception exception)
                    {
                        firstError = exception;
                    }
                }

                async void SecondRender()
                {
                    await renderer.RenderAsync(primitive, new[] { EmptyBackgroundLayer() });
                    secondCompleted = true;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            static TestTileLayerRuntime EmptyBackgroundLayer() => new(
                "background-layer",
                "Background",
                TileClassId,
                null,
                null);
        }

        [Test]
        public void Render_ShouldRenderObjectVetoSkipsMarkers_AndTryGetObjectRootFindsRendered()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [ObjectClassId] = (resolvedClient, node) =>
                    new TestComposedObject(resolvedClient, node),
            };
            var obj = (TestComposedObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var sprite = CreateTestSprite("veto-test");
            obj.Sprite = sprite;
            var go = new GameObject("NeoTileGridRenderer veto test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Lifecycle = new VetoObjectLifecycle("object-2");
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[]
                    {
                        new TestObjectLayerRuntime(
                            "object-layer",
                            "Objects",
                            ObjectClassId,
                            null,
                            12,
                            new[]
                            {
                                new NeoResolvedObjectInstance(
                                    "object-1",
                                    "object-layer",
                                    new Vector2Int(0, 0),
                                    new[] { new Vector2Int(0, 0) },
                                    obj,
                                    0),
                                new NeoResolvedObjectInstance(
                                    "object-2",
                                    "object-layer",
                                    new Vector2Int(1, 0),
                                    new[] { new Vector2Int(1, 0) },
                                    obj,
                                    1),
                            }),
                    });

                var layerRoot = go.transform.Find("Object Layer - Objects");
                Assert.IsNotNull(layerRoot);
                var renderedRoot = layerRoot!.Find("Object - object-1");
                Assert.IsNotNull(renderedRoot, "The non-vetoed object should render.");
                Assert.IsNull(
                    layerRoot.Find("Object - object-2"),
                    "ShouldRenderObject returning false must skip the instance.");

                Assert.IsTrue(renderer.TryGetObjectRoot("object-1", out var foundRoot));
                Assert.AreSame(renderedRoot!.gameObject, foundRoot);
                Assert.IsFalse(renderer.TryGetObjectRoot("object-2", out _));
                Assert.IsFalse(renderer.TryGetObjectRoot("missing-object", out _));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(sprite.texture);
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        private sealed class TileLayerCreatedCallbackLifecycle : NeoTileGridLifecycle
        {
            private readonly Action onTileLayerCreated;

            public TileLayerCreatedCallbackLifecycle(Action onTileLayerCreated)
            {
                this.onTileLayerCreated = onTileLayerCreated;
            }

            public override void OnTileLayerCreated(NeoTileLayerContext context)
            {
                onTileLayerCreated();
            }
        }

        private sealed class VetoObjectLifecycle : NeoTileGridLifecycle
        {
            private readonly string vetoedInstanceId;

            public VetoObjectLifecycle(string vetoedInstanceId)
            {
                this.vetoedInstanceId = vetoedInstanceId;
            }

            public override bool ShouldRenderObject(NeoObjectRenderContext context)
            {
                return context.Instance.InstanceId.Value != vetoedInstanceId;
            }
        }

        [Test]
        public void TileLayerLinkQueries_ProjectAuthoredTilesFromTheLinkOrigin()
        {
            var data = BuildClassBackedTileGridProjectData();
            var placement = (ObjectMemberValue)data.values["class-backed-placement"];
            placement.value!["assetValueId"] = "floor-tile";
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            client.RegisterGeneratedClassFactories(factories, writableFactories);
            var link = (TestTileLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "background-link",
                factories,
                writableFactories)!;

            var tiles = link.GetTiles();
            Assert.AreEqual(1, tiles.Count);
            Assert.AreEqual(new Vector2Int(2, 3), tiles[0].Cell);
            Assert.AreEqual(NeoTileOutputSourceKind.TileLayerLink, tiles[0].SourceKind);
            Assert.AreEqual("background-link", tiles[0].SourceTileLayerLinkId);
            Assert.AreEqual(BackgroundLayerClassId, tiles[0].LayerId);
            Assert.AreEqual("floor-tile", tiles[0].Info.valueId);

            Assert.IsNotNull(link.GetTile(new Vector2Int(2, 3)));
            Assert.IsNotNull(link.GetTile<TestTile>(new Vector2Int(2, 3)));
            Assert.IsNull(link.GetTile(new Vector2Int(20, 30)));
        }

        [Test]
        public void TileLayerLinkQueries_ResolveClassDefaultsWithoutDefinitionValues()
        {
            var client = NeoTestSaveStack.ClientFromSchema(
                BuildClassBackedTileGridProjectData());
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            client.RegisterGeneratedClassFactories(factories, writableFactories);
            var link = (TestTileLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "background-link",
                factories,
                writableFactories)!;

            var tile = link.GetTile(new Vector2Int(2, 3));

            Assert.IsNotNull(tile);
            Assert.AreEqual(TileClassId, ((TestTile)tile!.Info).classId);
            Assert.IsNull(tile.Info.valueId);
        }

        [Test]
        public void ObjectLayerLinkQueries_ProjectAuthoredObjectsFromTheLinkOrigin()
        {
            var client = NeoTestSaveStack.ClientFromSchema(
                BuildClassBackedTileGridProjectData());
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            client.RegisterGeneratedClassFactories(factories, writableFactories);
            var link = (TestObjectLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "objects-link",
                factories,
                writableFactories)!;

            var objects = link.GetObjects();
            Assert.AreEqual(1, objects.Count);
            Assert.AreEqual(new Vector2Int(10, 20), objects[0].Cell);
            Assert.AreEqual(ObjectsLayerClassId, objects[0].LayerId);
            Assert.AreEqual("shop-1", objects[0].Info.valueId);
            Assert.IsNotNull(link.GetObject(new Vector2Int(10, 20)));
            Assert.IsNotNull(link.GetObject<TestComposedObject>(new Vector2Int(10, 20)));
        }

        [Test]
        public void ObjectLayerQueries_UseAuthoredPlacementTilesInsteadOfVisualSizeRect()
        {
            var data = BuildClassBackedTileGridProjectData();
            ConfigureObjectPlacementFootprint(
                data,
                new Vector2Int(2, 3),
                Vector2Int.zero,
                new Vector2Int(1, 2),
                new Vector2Int(1, 2));
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                writableFactories);
            var layer = primitive.BindReadOnlyObjectLayer<TestAuthoredObjectLayer>(
                ObjectsLayerClassId,
                new[] { ObjectClassId });

            NeoResolvedObjectInstance placed = layer.GetObjects()[0];

            CollectionAssert.AreEqual(
                new[] { new Vector2Int(10, 20), new Vector2Int(11, 22) },
                placed.Footprint);
            Assert.IsNotNull(layer.GetObject(new Vector2Int(10, 20)));
            Assert.IsNotNull(layer.GetObject(new Vector2Int(11, 22)));
            Assert.IsNull(
                layer.GetObject(new Vector2Int(10, 21)),
                "a visual-span cell with no PlacementTile must remain unoccupied");
            Assert.IsNull(
                layer.GetObject(new Vector2Int(11, 20)),
                "an irregular footprint must not fill its bounding rectangle");
        }

        [Test]
        public void ObjectLayerQueries_EmptyPlacementTilesOccupyOnlyOriginRegardlessOfVisualSize()
        {
            var data = BuildClassBackedTileGridProjectData();
            // The old Size-based expansion overflows its List capacity for this
            // visual span. Occupancy work must be independent of rendered area.
            ConfigureObjectPlacementFootprint(
                data,
                new Vector2Int(50_000, 50_000));
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                writableFactories);
            var layer = primitive.BindReadOnlyObjectLayer<TestAuthoredObjectLayer>(
                ObjectsLayerClassId,
                new[] { ObjectClassId });

            NeoResolvedObjectInstance placed = layer.GetObjects()[0];

            CollectionAssert.AreEqual(
                new[] { new Vector2Int(10, 20) },
                placed.Footprint);
            Assert.IsNotNull(layer.GetObject(new Vector2Int(10, 20)));
            Assert.IsNull(layer.GetObject(new Vector2Int(10, 21)));
        }

        [Test]
        public void ObjectLayerQueries_PlacementTileCellWriteInvalidatesFootprintIndex()
        {
            var data = BuildClassBackedTileGridProjectData();
            ConfigureObjectPlacementFootprint(
                data,
                new Vector2Int(1, 3),
                Vector2Int.zero);
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                writableFactories);
            var layer = primitive.BindReadOnlyObjectLayer<TestAuthoredObjectLayer>(
                ObjectsLayerClassId,
                new[] { ObjectClassId });
            Assert.IsNotNull(layer.GetObject(new Vector2Int(10, 20)));

            client.SetWritableValue(
                NeoValueOwnership.Save,
                new Vector2MemberValue
                {
                    id = "shop-1-placement-cell-0",
                    value = new NeoVector2Value { x = 2, y = 1 },
                });

            Assert.IsNull(layer.GetObject(new Vector2Int(10, 20)));
            NeoResolvedObjectInstance? moved = layer.GetObject(new Vector2Int(12, 21));
            Assert.IsNotNull(moved);
            CollectionAssert.AreEqual(
                new[] { new Vector2Int(12, 21) },
                moved!.Footprint);
        }

        [Test]
        public void TileLayerLinkQueries_RequireTargetRelation()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.internalRecordRelations!.Remove("relation-link-target");
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            client.RegisterGeneratedClassFactories(factories, writableFactories);
            var link = (TestTileLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "background-link",
                factories,
                writableFactories)!;

            var error = Assert.Throws<InvalidOperationException>(() => link.GetTiles());

            StringAssert.Contains("has no effective", error!.Message);
            StringAssert.Contains(
                InternalRecordRelationKinds.WorldTileLayerLinkTarget,
                error.Message);
        }

        [Test]
        public void ObjectLayerLinkQueries_UseRelationWithoutLayerClassIdSidecar()
        {
            var data = BuildClassBackedTileGridProjectData();
            ((ObjectMemberValue)data.values["objects-link"]).value!
                .Remove("layerClassId");
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            client.RegisterGeneratedClassFactories(factories, writableFactories);
            var link = (TestObjectLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "objects-link",
                factories,
                writableFactories)!;

            Assert.AreEqual(ObjectsLayerClassId, link.GetObjects()[0].LayerId);
        }

        [Test]
        public void ObjectLayerLinkQueries_IgnoreLayerClassIdPayloadKey()
        {
            var data = BuildClassBackedTileGridProjectData();
            ((ObjectMemberValue)data.values["objects-link"]).value!["layerClassId"] =
                BackgroundLayerClassId;
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            client.RegisterGeneratedClassFactories(factories, writableFactories);
            var link = (TestObjectLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "objects-link",
                factories,
                writableFactories)!;

            Assert.AreEqual(ObjectsLayerClassId, link.GetObjects()[0].LayerId);
        }

        [Test]
        public void ObjectLayerLinkQueries_RejectWrongLayerKind()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.classes[ObjectsLayerClassId].system =
                JObject.FromObject(new { worldKind = "tileLayer" });
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            client.RegisterGeneratedClassFactories(factories, writableFactories);
            var link = (TestObjectLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "objects-link",
                factories,
                writableFactories)!;

            var error = Assert.Throws<InvalidOperationException>(() => link.GetObjects());

            StringAssert.Contains("inherited world kind is 'tileLayer'", error!.Message);
            StringAssert.Contains("instead of 'objectLayer'", error.Message);
        }

        [Test]
        public void AssetDatabase_ResolvesGeneratedTileAssets()
        {
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            var tile = ScriptableObject.CreateInstance<Tile>();
            try
            {
                database.SetTileAsset(
                    "floor-tile",
                    TileClassId,
                    "Assets/Neo/Generated/Tiles/floor-tile.asset",
                    "hash-1",
                    tile);

                Assert.AreSame(tile, database.TryGetTileBase("floor-tile"));
                var entry = database.TryGetTileEntry("floor-tile");
                Assert.IsNotNull(entry);
                Assert.AreEqual(TileClassId, entry!.TileClassId);
                Assert.AreEqual("hash-1", entry.ContentHash);

                var missing = database.FindMissingTileAssets(new HashSet<string>());
                Assert.AreEqual(1, missing.Length);

                database.RemoveTileAsset("floor-tile");
                Assert.IsNull(database.TryGetTileBase("floor-tile"));

                database.SetTileClassAsset(
                    TileClassId,
                    "Assets/Neo/Generated/Tiles/tile-class.asset",
                    "hash-class",
                    tile);
                Assert.AreSame(tile, database.TryGetTileBaseForClass(TileClassId));
                Assert.AreEqual(0, database.FindMissingTileAssets(new HashSet<string>()).Length);
                Assert.AreEqual(
                    1,
                    database.FindMissingTileClassAssets(new HashSet<string>()).Length);
                database.RemoveTileClassAsset(TileClassId);
                Assert.IsNull(database.TryGetTileBaseForClass(TileClassId));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tile);
                UnityEngine.Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void Render_PrefersEditorGeneratedTileAssetFromAssetDatabase()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            tile.Sprite = CreateTestSprite("fallback");
            var generatedTile = ScriptableObject.CreateInstance<Tile>();
            generatedTile.sprite = CreateTestSprite("editor-generated");
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            database.SetTileAsset(
                "floor-tile",
                TileClassId,
                "Assets/Neo/Generated/Tiles/floor-tile.asset",
                "hash-1",
                generatedTile);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
            var go = new GameObject("NeoTileGridRenderer generated asset test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.AssetDatabase = database;
                renderer.Render(
                    primitive,
                    new[]
                    {
                        new TestTileLayerRuntime(
                            "background-layer",
                            "Background",
                            TileClassId,
                            null,
                            null,
                            new[]
                            {
                                new NeoResolvedTileInstance(
                                    "tile-1",
                                    "background-layer",
                                    Vector2Int.zero,
                                    tile,
                                    0),
                            }),
                    });

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);
                Assert.AreSame(generatedTile, tilemap!.GetTile(Vector3Int.zero));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(generatedTile.sprite.texture);
                UnityEngine.Object.DestroyImmediate(generatedTile.sprite);
                UnityEngine.Object.DestroyImmediate(generatedTile);
                UnityEngine.Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void Render_PrefersEditorGeneratedTileAssetForClassDefaultPlacement()
        {
            var data = BuildClassBackedTileGridProjectData();
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string> { [typeof(TestTile)] = TileClassId });
            var layer = primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });
            var generatedTile = ScriptableObject.CreateInstance<Tile>();
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            database.SetTileClassAsset(
                TileClassId,
                "Assets/Neo/Generated/Tiles/tile-class.asset",
                "hash-class",
                generatedTile);
            var go = new GameObject("NeoTileGridRenderer class asset test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.AssetDatabase = database;
                renderer.Render(primitive, new[] { layer });

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);
                Assert.AreSame(
                    generatedTile,
                    tilemap!.GetTile(new Vector3Int(2, 3, 0)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(generatedTile);
                UnityEngine.Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void Render_UsesSmartTileRuleTileWhenGeneratedTileExposesSmartTile()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var fallbackSprite = CreateTestSprite("fallback");
            tile.Sprite = fallbackSprite;
            var smartTile = new TestSmartTile
            {
                DefaultCollider = NeoSmartTileOptionIds.ColliderNone,
            };
            smartTile.Rules.Add(new TestSmartTileRule
            {
                Collider = NeoSmartTileOptionIds.ColliderSprite,
                Output = NeoSmartTileOptionIds.OutputSingle,
            });
            tile.SmartTile = smartTile;

            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
            var go = new GameObject("NeoTileGridRenderer smart tile test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    primitive,
                    new[]
                    {
                        new TestTileLayerRuntime(
                            "background-layer",
                            "Background",
                            TileClassId,
                            null,
                            null,
                            new[]
                            {
                                new NeoResolvedTileInstance(
                                    "smart-tile-1",
                                    "background-layer",
                                    Vector2Int.zero,
                                    tile,
                                    0),
                            }),
                    });

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);
                var renderedTile = tilemap!.GetTile(Vector3Int.zero);
                Assert.IsInstanceOf<NeoRuleTile>(renderedTile);
                var ruleTile = (NeoRuleTile)renderedTile;
                Assert.AreSame(fallbackSprite, ruleTile.m_DefaultSprite);
                Assert.AreEqual(Tile.ColliderType.None, ruleTile.m_DefaultColliderType);
                Assert.AreEqual(1, ruleTile.m_TilingRules.Count);
                Assert.AreEqual(
                    Tile.ColliderType.Sprite,
                    ruleTile.m_TilingRules[0].m_ColliderType);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(fallbackSprite.texture);
                UnityEngine.Object.DestroyImmediate(fallbackSprite);
            }
        }

        [Test]
        public void Render_PaintingNeighborRefreshesSmartTileAndMatchesSubtype()
        {
            var client = NeoTestSaveStack.ClientFromSchema(
                BuildClassBackedTileGridProjectData());
            var factories = BuildInheritanceTileFactories();
            var smartTileValue = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var neighborValue = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "sub-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var defaultSprite = CreateTestSprite("smart-default");
            var connectedSprite = CreateTestSprite("smart-connected");
            var neighborSprite = CreateTestSprite("subclass-neighbor");
            smartTileValue.Sprite = defaultSprite;
            neighborValue.Sprite = neighborSprite;
            smartTileValue.SmartTile = SmartTileWithInheritsClassNeighbor(
                connectedSprite,
                BaseTileClassId);

            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
            var layer = new MutableTestTileLayerRuntime(
                "background-layer",
                "Background",
                TileClassId);
            layer.SetTile(new NeoResolvedTileInstance(
                "smart-origin",
                "background-layer",
                Vector2Int.zero,
                smartTileValue,
                0));
            var content = new TestTileGridContent(primitive, new[] { layer });
            var go = new GameObject("NeoTileGridRenderer smart neighbor refresh test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(content);

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);
                Assert.IsInstanceOf<NeoRuleTile>(tilemap!.GetTile(Vector3Int.zero));
                Assert.AreSame(defaultSprite, tilemap.GetSprite(Vector3Int.zero));

                layer.SetTile(new NeoResolvedTileInstance(
                    "subclass-neighbor",
                    BackgroundLayerClassId,
                    new Vector2Int(1, 0),
                    neighborValue,
                    1));
                primitive.NotifyTileLayerChanged(
                    "background-layer",
                    Array.Empty<Vector2Int>(),
                    new[] { new Vector2Int(1, 0) },
                    NeoTileGridChangeSourceKind.Direct,
                    null);

                Assert.AreSame(neighborSprite, tilemap.GetSprite(new Vector3Int(1, 0, 0)));
                Assert.AreSame(connectedSprite, tilemap.GetSprite(Vector3Int.zero));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(defaultSprite);
                DestroyTestSprite(connectedSprite);
                DestroyTestSprite(neighborSprite);
            }
        }

        /// <summary>
        /// Smart tile <c>This</c>/<c>NotThis</c> are DEFINITION identity: the
        /// web's <c>ISmartTileNeighborContext</c> pins the compared identity to
        /// "ALWAYS the concrete tile class id ... never a per-derivation
        /// summary id". Unity's built-in constants compare TileBase REFERENCE
        /// identity instead, and the renderer caches one TileBase per placement
        /// value id, so two placements of one tile class held different
        /// instances and a painted run of that tile never joined up.
        /// </summary>
        /// <param name="neighborValueId">
        /// The neighbor placement: a second value of the SMART TILE'S OWN class
        /// (matches) or a value of an unrelated class (does not).
        /// </param>
        [TestCase(
            NeoSmartTileOptionIds.ConditionThis,
            "base-tile-twin",
            true)]
        [TestCase(
            NeoSmartTileOptionIds.ConditionThis,
            "other-tile",
            false)]
        [TestCase(
            NeoSmartTileOptionIds.ConditionNotThis,
            "base-tile-twin",
            false)]
        [TestCase(
            NeoSmartTileOptionIds.ConditionNotThis,
            "other-tile",
            true)]
        public void Render_ThisNeighborComparesTheTileClassNotThePlacementValue(
            string condition,
            string neighborValueId,
            bool expectMatch)
        {
            var client = NeoTestSaveStack.ClientFromSchema(
                BuildClassBackedTileGridProjectData());
            var factories = BuildInheritanceTileFactories();
            var smartTileValue = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "base-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var neighborValue = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                neighborValueId,
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            Assert.AreNotEqual(
                smartTileValue.valueId,
                neighborValue.valueId,
                "the two placements must be distinct values");

            var defaultSprite = CreateTestSprite("smart-default");
            var connectedSprite = CreateTestSprite("smart-connected");
            var neighborSprite = CreateTestSprite("smart-neighbor");
            smartTileValue.Sprite = defaultSprite;
            neighborValue.Sprite = neighborSprite;
            smartTileValue.SmartTile = SmartTileWithSelfNeighbor(
                connectedSprite,
                condition);

            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
            var layer = new MutableTestTileLayerRuntime(
                "background-layer",
                "Background",
                TileClassId);
            layer.SetTile(new NeoResolvedTileInstance(
                "smart-origin",
                "background-layer",
                Vector2Int.zero,
                smartTileValue,
                0));
            var content = new TestTileGridContent(primitive, new[] { layer });
            var go = new GameObject("NeoTileGridRenderer smart This neighbor test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(content);

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);
                Assert.IsInstanceOf<NeoRuleTile>(tilemap!.GetTile(Vector3Int.zero));

                layer.SetTile(new NeoResolvedTileInstance(
                    "smart-neighbor",
                    "background-layer",
                    new Vector2Int(1, 0),
                    neighborValue,
                    1));
                primitive.NotifyTileLayerChanged(
                    "background-layer",
                    Array.Empty<Vector2Int>(),
                    new[] { new Vector2Int(1, 0) },
                    NeoTileGridChangeSourceKind.Direct,
                    null);

                Assert.AreSame(
                    expectMatch ? connectedSprite : defaultSprite,
                    tilemap.GetSprite(Vector3Int.zero),
                    expectMatch
                        ? $"'{condition}' must be satisfied by neighbor '{neighborValueId}'"
                        : $"'{condition}' must be refused by neighbor '{neighborValueId}'");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(defaultSprite);
                DestroyTestSprite(connectedSprite);
                DestroyTestSprite(neighborSprite);
            }
        }

        [Test]
        public void Render_InheritsFromClassRuleRejectsUnrelatedNeighbor()
        {
            var client = NeoTestSaveStack.ClientFromSchema(
                BuildClassBackedTileGridProjectData());
            var factories = BuildInheritanceTileFactories();
            var smartTileValue = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var neighborValue = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "other-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var defaultSprite = CreateTestSprite("smart-default");
            var connectedSprite = CreateTestSprite("smart-connected");
            var neighborSprite = CreateTestSprite("unrelated-neighbor");
            smartTileValue.Sprite = defaultSprite;
            neighborValue.Sprite = neighborSprite;
            smartTileValue.SmartTile = SmartTileWithInheritsClassNeighbor(
                connectedSprite,
                BaseTileClassId);

            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
            var layer = new MutableTestTileLayerRuntime(
                "background-layer",
                "Background",
                TileClassId);
            layer.SetTile(new NeoResolvedTileInstance(
                "smart-origin",
                "background-layer",
                Vector2Int.zero,
                smartTileValue,
                0));
            var content = new TestTileGridContent(primitive, new[] { layer });
            var go = new GameObject("NeoTileGridRenderer smart unrelated neighbor test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(content);

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);

                layer.SetTile(new NeoResolvedTileInstance(
                    "unrelated-neighbor",
                    "background-layer",
                    new Vector2Int(1, 0),
                    neighborValue,
                    1));
                primitive.NotifyTileLayerChanged(
                    "background-layer",
                    Array.Empty<Vector2Int>(),
                    new[] { new Vector2Int(1, 0) },
                    NeoTileGridChangeSourceKind.Direct,
                    null);

                Assert.AreSame(neighborSprite, tilemap!.GetSprite(new Vector3Int(1, 0, 0)));
                Assert.AreSame(defaultSprite, tilemap.GetSprite(Vector3Int.zero));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(defaultSprite);
                DestroyTestSprite(connectedSprite);
                DestroyTestSprite(neighborSprite);
            }
        }

        [Test]
        public void Render_RendersObjectCompositionChildrenInsteadOfParentSprite()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildClassBackedTileGridProjectData());
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            client.RegisterGeneratedClassFactories(factories, writableFactories);
            var parentSprite = CreateTestSprite("parent");
            var childSprite = CreateTestSprite("child-object");
            var tileSprite = CreateTestSprite("child-tile");
            var obj = (TestComposedObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                factories,
                writableFactories)!;
            var tile = (TestTile)NeoGeneratedTypesSupport.CreateReadOnlyClassDefault(
                client,
                TileClassId,
                factories);
            var tileLayerLink = (TestTileLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-floor-link",
                factories,
                writableFactories)!;
            tile.Sprite = tileSprite;
            obj.Sprite = parentSprite;
            obj.Children = new INeoWorldObjectValue[]
            {
                new TestSpriteChild
                {
                    Name = "Sprite Child",
                    Sprite = childSprite,
                    Position = new NeoReadOnlyVector3(0f, 0f, 0f),
                    Size = new NeoReadOnlyVector3(2f, 1f, 0f),
                },
                tileLayerLink,
            };
            var go = new GameObject("NeoTileGridRenderer object composition test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.CellSize = 2f;
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[]
                    {
                        new TestObjectLayerRuntime(
                            "object-layer",
                            "Objects",
                            ObjectClassId,
                            null,
                            12,
                            new[]
                            {
                                new NeoResolvedObjectInstance(
                                    "object-1",
                                    "object-layer",
                                    new Vector2Int(3, 4),
                                    new[] { new Vector2Int(3, 4) },
                                    obj,
                                    1),
                            }),
                    });

                var objectRoot = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1");
                Assert.IsNotNull(objectRoot);
                Assert.AreEqual(new Vector3(6f, 8f, 0f), objectRoot!.localPosition);

                var spriteRenderers = objectRoot.GetComponentsInChildren<SpriteRenderer>();
                Assert.AreEqual(2, spriteRenderers.Length);
                Assert.IsFalse(System.Array.Exists(
                    spriteRenderers,
                    spriteRenderer => spriteRenderer.sprite == parentSprite));

                var spriteChild = objectRoot.Find("Sprite Child");
                Assert.IsNotNull(spriteChild);
                var spriteChildRenderer = spriteChild!.GetComponent<SpriteRenderer>();
                Assert.AreEqual(new Vector3(2f, 1f, 0f), spriteChild.localPosition);
                Assert.AreSame(childSprite, spriteChildRenderer.sprite);
                Assert.AreEqual(4f, spriteChildRenderer.bounds.size.x, 0.0001f);
                Assert.AreEqual(2f, spriteChildRenderer.bounds.size.y, 0.0001f);

                var tileChild = objectRoot.Find("child-tile");
                Assert.IsNotNull(tileChild);
                var tileChildRenderer = tileChild!.GetComponent<SpriteRenderer>();
                Assert.AreEqual(new Vector3(-1f, 5f, 0f), tileChild.localPosition);
                Assert.AreSame(tileSprite, tileChildRenderer.sprite);
                Assert.AreEqual(2f, tileChildRenderer.bounds.size.x, 0.0001f);
                Assert.AreEqual(2f, tileChildRenderer.bounds.size.y, 0.0001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(parentSprite.texture);
                UnityEngine.Object.DestroyImmediate(parentSprite);
                UnityEngine.Object.DestroyImmediate(childSprite.texture);
                UnityEngine.Object.DestroyImmediate(childSprite);
                UnityEngine.Object.DestroyImmediate(tileSprite.texture);
                UnityEngine.Object.DestroyImmediate(tileSprite);
            }
        }

        [Test]
        public void Render_AddsSortingGroupWithLayerSortingAndAuthoredSortAtRoot()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var obj = ResolveComposedTestObject(client);
            obj.SortingGroup = new TestSortingGroup { SortAtRoot = true };
            var go = new GameObject("NeoTileGridRenderer sorting group test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var objectRoot = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1");
                Assert.IsNotNull(objectRoot);
                Assert.IsTrue(objectRoot!.TryGetComponent(
                    out UnityEngine.Rendering.SortingGroup sortingGroup));
                Assert.IsTrue(sortingGroup.sortAtRoot);
                Assert.AreEqual("Default", sortingGroup.sortingLayerName);
                // Layer order 12 plus the instance's authored order 1.
                Assert.AreEqual(13, sortingGroup.sortingOrder);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Render_OmitsSortingGroupWhenObjectAuthoredNone()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var obj = ResolveComposedTestObject(client);
            var go = new GameObject("NeoTileGridRenderer ungrouped object test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var objectRoot = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1");
                Assert.IsNotNull(objectRoot);
                Assert.IsFalse(objectRoot!.TryGetComponent(
                    out UnityEngine.Rendering.SortingGroup _));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Render_AppliesAuthoredSpriteFlipsAndMaskInteraction()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var obj = ResolveComposedTestObject(client);
            var childSprite = CreateTestSprite("masked-child");
            obj.Children = new INeoWorldObjectValue[]
            {
                new TestSpriteChild
                {
                    Name = "Masked Child",
                    Sprite = childSprite,
                    FlipX = true,
                    FlipY = true,
                    MaskInteraction =
                        NeoSpriteMaskInteraction.VisibleInsideMask.optionId,
                },
            };
            var go = new GameObject("NeoTileGridRenderer sprite state test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var child = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1")
                    ?.Find("Masked Child");
                Assert.IsNotNull(child);
                var childRenderer = child!.GetComponent<SpriteRenderer>();
                Assert.IsTrue(childRenderer.flipX);
                Assert.IsTrue(childRenderer.flipY);
                Assert.AreEqual(
                    SpriteMaskInteraction.VisibleInsideMask,
                    childRenderer.maskInteraction);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(childSprite);
            }
        }

        [Test]
        public void Render_AddsAuthoredSpriteSortingOrderToLayerOrder()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var obj = ResolveComposedTestObject(client);
            var nudgedSprite = CreateTestSprite("nudged");
            var plainSprite = CreateTestSprite("plain");
            obj.Children = new INeoWorldObjectValue[]
            {
                new TestSpriteChild
                {
                    Name = "Nudged",
                    Sprite = nudgedSprite,
                    SortingOrder = 5,
                },
                new TestSpriteChild
                {
                    Name = "Plain",
                    Sprite = plainSprite,
                },
            };
            var go = new GameObject("NeoTileGridRenderer sprite order test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var objectRoot = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1");
                Assert.IsNotNull(objectRoot);
                var nudged = objectRoot!.Find("Nudged");
                var plain = objectRoot.Find("Plain");
                Assert.IsNotNull(nudged);
                Assert.IsNotNull(plain);
                // Layer 12 + instance order 1 + composition index 0 + the
                // authored offset 5.
                Assert.AreEqual(
                    18,
                    nudged!.GetComponent<SpriteRenderer>().sortingOrder);
                Assert.AreEqual(
                    14,
                    plain!.GetComponent<SpriteRenderer>().sortingOrder);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(nudgedSprite);
                DestroyTestSprite(plainSprite);
            }
        }

        [Test]
        public void Render_RendersPlacedSpriteObjectThroughItsContract()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories =
                new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
                {
                    [ObjectClassId] = (resolvedClient, node) =>
                        new TestSpriteObject(resolvedClient, node),
                };
            var obj = (TestSpriteObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var sprite = CreateTestSprite("placed-sprite");
            obj.Name = "Placed Sprite";
            obj.Sprite = sprite;
            obj.FlipX = true;
            obj.SortingOrder = -2;
            var go = new GameObject("NeoTileGridRenderer sprite object test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var child = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1")
                    ?.Find("Placed Sprite");
                Assert.IsNotNull(child);
                var childRenderer = child!.GetComponent<SpriteRenderer>();
                Assert.AreSame(sprite, childRenderer.sprite);
                Assert.IsTrue(childRenderer.flipX);
                Assert.IsFalse(childRenderer.flipY);
                Assert.AreEqual(11, childRenderer.sortingOrder);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(sprite);
            }
        }

        [Test]
        public void Render_DeactivatesDisabledSpriteChildInsteadOfSkippingIt()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var obj = ResolveComposedTestObject(client);
            var hatSprite = CreateTestSprite("hat");
            var hairSprite = CreateTestSprite("hair");
            obj.Children = new INeoWorldObjectValue[]
            {
                new TestSpriteChild
                {
                    Name = "Hat",
                    Sprite = hatSprite,
                    Enabled = false,
                },
                new TestSpriteChild
                {
                    Name = "Hair",
                    Sprite = hairSprite,
                },
            };
            var go = new GameObject("NeoTileGridRenderer disabled sprite child test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var objectRoot = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1");
                Assert.IsNotNull(objectRoot);
                var hat = objectRoot!.Find("Hat");
                Assert.IsNotNull(
                    hat,
                    "a disabled child must still be built, then deactivated");
                Assert.IsFalse(hat!.gameObject.activeSelf);
                Assert.AreSame(
                    hatSprite,
                    hat.GetComponent<SpriteRenderer>().sprite,
                    "the subtree must be intact so a runtime write can reveal it");
                var hair = objectRoot.Find("Hair");
                Assert.IsNotNull(hair);
                Assert.IsTrue(hair!.gameObject.activeSelf);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(hatSprite);
                DestroyTestSprite(hairSprite);
            }
        }

        [Test]
        public void Render_DisabledCompositionPartAtDepthTwoHidesItsWholeSubtree()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var obj = ResolveComposedTestObject(client);
            var hatSprite = CreateTestSprite("hat");
            var hat = new TestSpriteChild { Name = "Hat", Sprite = hatSprite };
            var head = new TestComposedChild
            {
                Name = "Head",
                Enabled = false,
                Children = new INeoWorldObjectValue[] { hat },
            };
            obj.Children = new INeoWorldObjectValue[]
            {
                new TestComposedChild
                {
                    Name = "Body",
                    Children = new INeoWorldObjectValue[] { head },
                },
            };
            var go = new GameObject("NeoTileGridRenderer nested visibility test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var body = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1")
                    ?.Find("Body");
                Assert.IsNotNull(body);
                Assert.IsTrue(body!.gameObject.activeSelf);
                var headRoot = body.Find("Head");
                Assert.IsNotNull(headRoot);
                Assert.IsFalse(headRoot!.gameObject.activeSelf);
                var hatRoot = headRoot.Find("Hat");
                Assert.IsNotNull(
                    hatRoot,
                    "the whole subtree of a disabled part must still be built");
                // Unity's activeInHierarchy model: the parent hides the subtree
                // while each child keeps its own value and its own activeSelf.
                Assert.IsTrue(hatRoot!.gameObject.activeSelf);
                Assert.IsFalse(hatRoot.gameObject.activeInHierarchy);
                Assert.IsTrue(hat.Enabled, "a parent must not rewrite a child's value");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(hatSprite);
            }
        }

        [Test]
        public void Render_ReEnablingAPartRestoresExactlyWhatWasThere()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            var client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            var obj = (TestComposedObject)placed.Info;
            var hairSprite = CreateTestSprite("hair");
            var scarSprite = CreateTestSprite("scar");
            var head = new TestComposedChild
            {
                Name = "Head",
                Enabled = false,
                Children = new INeoWorldObjectValue[]
                {
                    new TestSpriteChild { Name = "Hair", Sprite = hairSprite },
                    new TestSpriteChild
                    {
                        Name = "Scar",
                        Sprite = scarSprite,
                        Enabled = false,
                    },
                },
            };
            obj.Children = new INeoWorldObjectValue[] { head };
            var go = new GameObject("NeoTileGridRenderer re-enable test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var headRoot = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1")
                    ?.Find("Head");
                Assert.IsNotNull(headRoot);
                Assert.IsFalse(headRoot!.gameObject.activeSelf);
                var hair = headRoot.Find("Hair");
                var scar = headRoot.Find("Scar");
                Assert.IsNotNull(hair);
                Assert.IsNotNull(scar);
                Assert.IsTrue(hair!.gameObject.activeSelf);
                Assert.IsFalse(scar!.gameObject.activeSelf);

                head.Enabled = true;
                NotifyObjectVisibilityChanged(obj);

                Assert.IsTrue(headRoot.gameObject.activeSelf);
                Assert.IsTrue(
                    hair.gameObject.activeInHierarchy,
                    "re-enabling the parent must restore its enabled children");
                Assert.IsFalse(
                    scar.gameObject.activeSelf,
                    "an independently disabled child must stay disabled");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(hairSprite);
                DestroyTestSprite(scarSprite);
            }
        }

        [Test]
        public void Render_DisabledObjectContributesNoActiveCollider()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var obj = ResolveComposedTestObject(client);
            obj.Enabled = false;
            obj.Collider = new TestObjectCollider
            {
                Size = new NeoReadOnlyVector2(2f, 3f),
            };
            var hatSprite = CreateTestSprite("hat");
            obj.Children = new INeoWorldObjectValue[]
            {
                new TestSpriteChild { Name = "Hat", Sprite = hatSprite },
            };
            var go = new GameObject("NeoTileGridRenderer disabled collider test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.AddSpriteBoundsColliders = true;
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var objectRoot = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1");
                Assert.IsNotNull(objectRoot);
                Assert.IsFalse(objectRoot!.gameObject.activeSelf);
                Assert.IsTrue(
                    objectRoot.TryGetComponent(out BoxCollider2D rootCollider),
                    "the authored collider is still attached, just not active");
                Assert.IsFalse(rootCollider.isActiveAndEnabled);
                var hat = objectRoot.Find("Hat");
                Assert.IsNotNull(hat);
                Assert.IsTrue(hat!.TryGetComponent(out BoxCollider2D childCollider));
                Assert.IsFalse(
                    childCollider.isActiveAndEnabled,
                    "a disabled object's subtree contributes no collider either");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(hatSprite);
            }
        }

        [Test]
        public void Render_DisabledPlacedRootStillSpawnsItsBehaviourDeactivated()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var obj = ResolveComposedTestObject(client);
            obj.Enabled = false;
            var hatSprite = CreateTestSprite("hat");
            obj.Children = new INeoWorldObjectValue[]
            {
                new TestSpriteChild { Name = "Hat", Sprite = hatSprite },
            };
            var go = new GameObject("NeoTileGridRenderer disabled root test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var objectRoot = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1");
                Assert.IsNotNull(
                    objectRoot,
                    "a disabled placed object is deactivated, never skipped");
                Assert.IsFalse(objectRoot!.gameObject.activeSelf);
                Assert.IsTrue(
                    objectRoot.TryGetComponent(out NeoObjectBehaviour behaviour));
                Assert.AreSame(obj, behaviour.Object);
                Assert.IsTrue(renderer.TryGetObjectRoot("object-1", out var foundRoot));
                Assert.AreSame(objectRoot.gameObject, foundRoot);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(hatSprite);
            }
        }

        [Test]
        public void Render_RuntimeEnabledWriteTogglesAnAlreadyRenderedObject()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            var client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            var obj = (TestComposedObject)placed.Info;
            var hatSprite = CreateTestSprite("hat");
            var hat = new TestSpriteChild
            {
                Name = "Hat",
                Sprite = hatSprite,
                Enabled = false,
            };
            obj.Children = new INeoWorldObjectValue[] { hat };
            var go = new GameObject("NeoTileGridRenderer runtime visibility test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var hatRoot = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1")
                    ?.Find("Hat");
                Assert.IsNotNull(hatRoot);
                Assert.IsFalse(hatRoot!.gameObject.activeSelf);

                // The conductor's `hat.Enabled = true`. The test double is a
                // plain object, so the member write that carries the change to
                // the renderer's per-object subscription is issued separately.
                hat.Enabled = true;
                NotifyObjectVisibilityChanged(obj);

                // Read back through the transform cached before the write: a
                // re-render would have destroyed it, so this also proves the
                // toggle reuses the GameObject that was already built.
                Assert.IsTrue(
                    hatRoot.gameObject.activeSelf,
                    "a runtime Enabled write must reveal the object without a re-render");

                hat.Enabled = false;
                NotifyObjectVisibilityChanged(obj);

                Assert.IsFalse(hatRoot.gameObject.activeSelf);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(hatSprite);
            }
        }

        /// <summary>
        /// The hot path: a clip animating a placement writes a leaf on the
        /// placement itself every frame, and none of those writes can carry an
        /// <c>Enabled</c>. Reconciling visibility on them would put the whole
        /// rendered subtree — up to 400 GameObjects for a tile-layer-link child
        /// — on the per-frame budget.
        /// </summary>
        [Test]
        public void Render_UnrelatedMemberWriteLeavesTheVisibilityIndexAlone()
        {
            ProjectData data = BuildPlacementAnimationProjectData();
            var client = NeoTestSaveStack.ClientFromSchema(data);
            NeoResolvedObjectInstance placed = SpawnAnimationTestObject(client);
            var obj = (TestComposedObject)placed.Info;
            var hatSprite = CreateTestSprite("hat");
            var hat = new TestSpriteChild
            {
                Name = "Hat",
                Sprite = hatSprite,
                Enabled = false,
            };
            obj.Children = new INeoWorldObjectValue[] { hat };
            var go = new GameObject("NeoTileGridRenderer visibility gate test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var hatRoot = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1")
                    ?.Find("Hat");
                Assert.IsNotNull(hatRoot);
                Assert.IsFalse(hatRoot!.gameObject.activeSelf);

                // Linking a key the placement does not carry yet rewrites the
                // record itself, which the renderer honours conservatively. The
                // write under test is the second one: a pure leaf write, the
                // shape every subsequent clip frame takes.
                NotifyObjectPositionChanged(obj);

                hat.Enabled = true;
                NotifyObjectPositionChanged(obj);

                Assert.IsFalse(
                    hatRoot.gameObject.activeSelf,
                    "a Position write must not walk the visibility index");

                // The subscription is live and the index is reachable, so the
                // assertion above is a real gate rather than a dead one.
                NotifyObjectVisibilityChanged(obj);

                Assert.IsTrue(
                    hatRoot.gameObject.activeSelf,
                    "an Enabled write must still reconcile visibility");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(hatSprite);
            }
        }

        [Test]
        public void Render_DeactivatesTilesOfADisabledTileLayerLinkChild()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildClassBackedTileGridProjectData());
            var factories = BuildClassBackedReadOnlyFactories();
            var writableFactories = BuildClassBackedWritableFactories();
            client.RegisterGeneratedClassFactories(factories, writableFactories);
            var childSprite = CreateTestSprite("sprite-child");
            var tileSprite = CreateTestSprite("child-tile");
            var obj = (TestComposedObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                factories,
                writableFactories)!;
            var tile = (TestTile)NeoGeneratedTypesSupport.CreateReadOnlyClassDefault(
                client,
                TileClassId,
                factories);
            var tileLayerLink = (TestTileLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-floor-link",
                factories,
                writableFactories)!;
            tile.Sprite = tileSprite;
            // A layer group base is an object base, so a link carries Enabled —
            // but its tiles are bare siblings with no root of their own.
            tileLayerLink.Enabled = false;
            obj.Children = new INeoWorldObjectValue[]
            {
                new TestSpriteChild { Name = "Sprite Child", Sprite = childSprite },
                tileLayerLink,
            };
            var go = new GameObject("NeoTileGridRenderer disabled link test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var objectRoot = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1");
                Assert.IsNotNull(objectRoot);
                var tileChild = objectRoot!.Find("child-tile");
                Assert.IsNotNull(tileChild, "the link's tiles must still be built");
                Assert.IsFalse(tileChild!.gameObject.activeSelf);
                var spriteChild = objectRoot.Find("Sprite Child");
                Assert.IsNotNull(spriteChild);
                Assert.IsTrue(
                    spriteChild!.gameObject.activeSelf,
                    "the disabled link must not hide its siblings");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(childSprite);
                DestroyTestSprite(tileSprite);
            }
        }

        [Test]
        public void Render_DisabledChildStillCountsSoItsParentCompositionRootSurvives()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var obj = ResolveComposedTestObject(client);
            var hatSprite = CreateTestSprite("hat");
            obj.Children = new INeoWorldObjectValue[]
            {
                new TestComposedChild
                {
                    Name = "Head",
                    Children = new INeoWorldObjectValue[]
                    {
                        new TestSpriteChild
                        {
                            Name = "Hat",
                            Sprite = hatSprite,
                            Enabled = false,
                        },
                    },
                },
            };
            var go = new GameObject("NeoTileGridRenderer disabled-only child test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var headRoot = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1")
                    ?.Find("Head");
                Assert.IsNotNull(
                    headRoot,
                    "a disabled child must still count as rendered, or its parent "
                        + "composition root is destroyed as empty");
                Assert.IsTrue(headRoot!.gameObject.activeSelf);
                var hat = headRoot.Find("Hat");
                Assert.IsNotNull(hat);
                Assert.IsFalse(hat!.gameObject.activeSelf);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(hatSprite);
            }
        }

        [Test]
        public void Render_ObjectWithOnlyDisabledChildrenDoesNotDrawItsOwnRootSprite()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories =
                new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
                {
                    [ObjectClassId] = (resolvedClient, node) =>
                        new TestComposedSpriteObject(resolvedClient, node),
                };
            var obj = (TestComposedSpriteObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var rootSprite = CreateTestSprite("root-sprite");
            var hatSprite = CreateTestSprite("hat");
            obj.Name = "Placed Body";
            obj.Sprite = rootSprite;
            obj.Children = new INeoWorldObjectValue[]
            {
                new TestSpriteChild
                {
                    Name = "Hat",
                    Sprite = hatSprite,
                    Enabled = false,
                },
            };
            var go = new GameObject("NeoTileGridRenderer root sprite fallback test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var objectRoot = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1");
                Assert.IsNotNull(objectRoot);
                Assert.IsNull(
                    objectRoot!.Find("Placed Body"),
                    "a disabled child still counts as rendered, so the root sprite "
                        + "fallback must not fire");
                var renderers = objectRoot.GetComponentsInChildren<SpriteRenderer>(true);
                Assert.AreEqual(1, renderers.Length);
                Assert.AreSame(hatSprite, renderers[0].sprite);
                Assert.IsFalse(renderers[0].gameObject.activeSelf);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(rootSprite);
                DestroyTestSprite(hatSprite);
            }
        }

        /// <summary>
        /// The other half of the empty-part edge, and the sharper one: a part
        /// that renders nothing at all is destroyed and does not count, so an
        /// object whose only child is such a part falls back to drawing its own
        /// root sprite — as if it had no composition. Pinned because the
        /// consequence lands on the parent, not on the empty part.
        /// </summary>
        [Test]
        public void Render_ObjectWhoseOnlyChildRendersNothingFallsBackToItsRootSprite()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories =
                new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
                {
                    [ObjectClassId] = (resolvedClient, node) =>
                        new TestComposedSpriteObject(resolvedClient, node),
                };
            var obj = (TestComposedSpriteObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var rootSprite = CreateTestSprite("root-sprite");
            obj.Name = "Placed Body";
            obj.Sprite = rootSprite;
            obj.Children = new INeoWorldObjectValue[]
            {
                // Enabled, but empty: nothing under it renders, so its
                // composition root is destroyed and contributes no count.
                new TestComposedChild { Name = "Head" },
            };
            var go = new GameObject("NeoTileGridRenderer empty part fallback test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(obj, "Default", 12) });

                var objectRoot = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1");
                Assert.IsNotNull(objectRoot);
                Assert.IsNull(
                    objectRoot!.Find("Head"),
                    "a part that renders nothing is destroyed, not kept");
                var placedBody = objectRoot.Find("Placed Body");
                Assert.IsNotNull(
                    placedBody,
                    "with no rendered children left, the parent draws its own "
                        + "root sprite — the flip an empty part causes");
                Assert.AreSame(
                    rootSprite,
                    placedBody!.GetComponent<SpriteRenderer>().sprite);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(rootSprite);
            }
        }

        /// <summary>
        /// Issues a real <c>Enabled</c> write on a placed object so the
        /// renderer's per-object subscription fires with a member the renderer
        /// treats as visibility-bearing. Generated values raise this themselves
        /// on an <c>Enabled</c> write anywhere in the placement's subtree; the
        /// renderer test doubles hold <c>Enabled</c> as a plain property, so a
        /// test sets the property and drives the notification explicitly.
        /// </summary>
        private static void NotifyObjectVisibilityChanged(TestComposedObject target)
        {
            NeoGeneratedTypesSupport.SetValue(
                NeoGeneratedTypesSupport.AsWritable(target.BackingNode),
                "Enabled",
                NeoValueWritePayload.FromValue(target.Enabled));
        }

        /// <summary>
        /// Issues a member write the renderer must NOT treat as
        /// visibility-bearing — the shape every frame of a clip animating the
        /// placement itself takes.
        /// </summary>
        private static void NotifyObjectPositionChanged(TestComposedObject target)
        {
            NeoGeneratedTypesSupport.SetValue(
                NeoGeneratedTypesSupport.AsWritable(target.BackingNode),
                "Position",
                NeoValueWritePayload.FromValue(Vector3.zero));
        }

        [Test]
        public void AuthoredObjectLayerReadsSortingOrderFromItsAuthoredMember()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.classes[ObjectsLayerClassId].schema["SortingOrder"] =
                "objects-layer-sorting-order-member";
            data.members["objects-layer-sorting-order-member"] = SortingOrderMember(
                "objects-layer-sorting-order-member",
                42);
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories());

            var layer = primitive.BindReadOnlyObjectLayer<TestAuthoredObjectLayer>(
                ObjectsLayerClassId,
                new[] { ObjectClassId });

            Assert.AreEqual(42, layer.SortingOrder);
        }

        [Test]
        public void AuthoredTileLayerReadsSortingOrderFromItsAuthoredMember()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.classes[BackgroundLayerClassId].schema["SortingOrder"] =
                "background-layer-sorting-order-member";
            data.members["background-layer-sorting-order-member"] = SortingOrderMember(
                "background-layer-sorting-order-member",
                7);
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories());

            var layer = primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });

            Assert.AreEqual(7, layer.SortingOrder);
        }

        [Test]
        public void RendererReadsWorldObjectMembersThroughContractsNotReflection()
        {
            var rendererSourcePath = RendererSourcePath();
            Assert.IsTrue(
                File.Exists(rendererSourcePath),
                $"Renderer source not found at '{rendererSourcePath}'.");

            var source = File.ReadAllText(rendererSourcePath);
            Assert.IsFalse(
                source.Contains("GetProperty"),
                "NeoTileGridRenderer must read world object members through the "
                    + "generated runtime contracts, not property reflection.");
            Assert.IsFalse(
                source.Contains("using System.Reflection;"),
                "NeoTileGridRenderer must not import System.Reflection.");
        }

        /// <summary>
        /// The spawn hook must observe an active, fully-built root, so the
        /// placed root's visibility is applied only after
        /// <c>NeoObjectBehaviour.Initialize</c>. EditMode cannot observe this
        /// behaviourally — <c>INeoObjectSpawnHooks</c> is gated on
        /// <c>Application.isPlaying</c> — so the ordering is pinned in source,
        /// the same guard style as the reflection check above.
        /// </summary>
        [Test]
        public void RendererAppliesPlacedRootVisibilityOnlyAfterTheSpawnHookRan()
        {
            var rendererSourcePath = RendererSourcePath();
            Assert.IsTrue(
                File.Exists(rendererSourcePath),
                $"Renderer source not found at '{rendererSourcePath}'.");

            var source = File.ReadAllText(rendererSourcePath);
            var initializeIndex = source.IndexOf(
                "behaviour.Initialize(",
                StringComparison.Ordinal);
            var syncIndex = source.IndexOf(
                "SyncObjectVisibility(instance.InstanceId)",
                StringComparison.Ordinal);
            Assert.Greater(
                initializeIndex,
                -1,
                "SpawnObject must still initialize the NeoObjectBehaviour.");
            Assert.Greater(
                syncIndex,
                initializeIndex,
                "SpawnObject must apply the placed root's Enabled state after "
                    + "NeoObjectBehaviour.Initialize, so the spawn hook still sees "
                    + "an active, fully-built root.");
        }

        /// <summary>
        /// The same contract, extended to composition children: a hook calling
        /// <c>GetComponentsInChildren</c> must not silently miss a disabled
        /// layer, so no render path may deactivate anything while it builds.
        /// <c>SyncObjectVisibility</c> — which the test above pins to after
        /// <c>Initialize</c> — is therefore the renderer's single visibility
        /// write. Pinned in source for the same reason as the test above:
        /// <c>INeoObjectSpawnHooks</c> never fires in EditMode.
        /// </summary>
        [Test]
        public void RendererDeactivatesObjectsOnlyFromSyncObjectVisibility()
        {
            var rendererSourcePath = RendererSourcePath();
            Assert.IsTrue(
                File.Exists(rendererSourcePath),
                $"Renderer source not found at '{rendererSourcePath}'.");

            var source = File.ReadAllText(rendererSourcePath);
            var syncStart = source.IndexOf(
                "private void SyncObjectVisibility(",
                StringComparison.Ordinal);
            Assert.Greater(syncStart, -1, "SyncObjectVisibility must still exist.");
            var syncEnd = source.IndexOf(
                "\n        private ",
                syncStart + 1,
                StringComparison.Ordinal);
            Assert.Greater(
                syncEnd,
                syncStart,
                "SyncObjectVisibility must be followed by another member.");

            var occurrences = 0;
            for (var index = source.IndexOf(".SetActive(", StringComparison.Ordinal);
                index >= 0;
                index = source.IndexOf(".SetActive(", index + 1, StringComparison.Ordinal))
            {
                occurrences++;
                Assert.IsTrue(
                    index > syncStart && index < syncEnd,
                    "Every SetActive in NeoTileGridRenderer must live in "
                        + "SyncObjectVisibility. Deactivating a child while the "
                        + "composition is still building hides it from a spawn "
                        + "hook's GetComponentsInChildren.");
            }
            Assert.AreEqual(
                1,
                occurrences,
                "SyncObjectVisibility is the renderer's single visibility write.");
        }

        private static string RendererSourcePath(
            [CallerFilePath] string testSourcePath = "")
        {
            var testsDirectory = Path.GetDirectoryName(testSourcePath)!;
            return Path.Combine(
                Path.GetDirectoryName(testsDirectory)!,
                "Runtime",
                "NeoTileGridRenderer.cs");
        }

        private static IntMember SortingOrderMember(string memberId, int value)
        {
            return new IntMember
            {
                id = memberId,
                projectId = "project-a",
                name = "SortingOrder",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { value = value },
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static TestComposedObject ResolveComposedTestObject(NeoClient client)
        {
            var factories =
                new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
                {
                    [ObjectClassId] = (resolvedClient, node) =>
                        new TestComposedObject(resolvedClient, node),
                };
            return (TestComposedObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
        }

        private static TestObjectLayerRuntime ObjectLayerWithSingleInstance(
            NeoGeneratedClassValue obj,
            string? sortingLayerName,
            int? sortingOrder)
        {
            return new TestObjectLayerRuntime(
                "object-layer",
                "Objects",
                ObjectClassId,
                sortingLayerName,
                sortingOrder,
                new[]
                {
                    new NeoResolvedObjectInstance(
                        "object-1",
                        "object-layer",
                        new Vector2Int(0, 0),
                        new[] { new Vector2Int(0, 0) },
                        obj,
                        1),
                });
        }

        [Test]
        public void TryResolveObjectColliderSpec_ReadsAuthoredColliderMembers()
        {
            var source = new TestColliderSource
            {
                Collider = new TestObjectCollider
                {
                    Size = new NeoReadOnlyVector2(2.5f, 3.5f),
                    Offset = new NeoReadOnlyVector2(0.25f, -0.5f),
                    IsTrigger = true,
                },
            };

            Assert.IsTrue(NeoTileGridRenderer.TryResolveObjectColliderSpec(source, out var spec));
            Assert.AreEqual(2.5f, spec.Size.x);
            Assert.AreEqual(3.5f, spec.Size.y);
            Assert.AreEqual(0.25f, spec.Offset.x);
            Assert.AreEqual(-0.5f, spec.Offset.y);
            Assert.IsTrue(spec.IsTrigger);
        }

        [Test]
        public void TryResolveObjectColliderSpec_DefaultsUnsetOffsetAndTrigger()
        {
            var source = new TestColliderSource
            {
                Collider = new TestObjectCollider
                {
                    Size = new NeoReadOnlyVector2(4f, 5.25f),
                },
            };

            Assert.IsTrue(NeoTileGridRenderer.TryResolveObjectColliderSpec(source, out var spec));
            Assert.AreEqual(4f, spec.Size.x);
            Assert.AreEqual(5.25f, spec.Size.y);
            Assert.AreEqual(Vector2.zero, spec.Offset);
            Assert.IsFalse(spec.IsTrigger);
        }

        [Test]
        public void TryResolveObjectColliderSpec_RejectsColliderWithoutSize()
        {
            var source = new TestColliderSource
            {
                Collider = new TestObjectCollider
                {
                    Size = new NeoReadOnlyVector2(0f, 0f),
                    Offset = new NeoReadOnlyVector2(Vector2.one),
                },
            };

            Assert.IsFalse(NeoTileGridRenderer.TryResolveObjectColliderSpec(source, out _));
        }

        [Test]
        public void TryResolveObjectColliderSpec_RejectsObjectWithoutCollider()
        {
            Assert.IsFalse(
                NeoTileGridRenderer.TryResolveObjectColliderSpec(
                    new TestColliderSource(),
                    out _));
        }

        private sealed class TestTile : NeoGeneratedClassValue, INeoSmartTileSource
        {
            public TestTile(NeoClient client, NeoMemberClass node)
                : base(client, node, TileClassId)
            {
            }

            public Sprite? Sprite { get; set; }
            public INeoSmartTile? SmartTile { get; set; }
        }

        private sealed class TestSmartTile : INeoSmartTile
        {
            public string DefaultCollider { get; set; } =
                NeoSmartTileOptionIds.ColliderSprite;

            public List<INeoSmartTileRule> Rules { get; } = new();

            IReadOnlyList<INeoSmartTileRule> INeoSmartTile.Rules => Rules;
        }

        private sealed class TestSmartTileRule : INeoSmartTileRule
        {
            public List<INeoSmartTileNeighbor> Neighbors { get; } = new();

            public List<Sprite> Sprites { get; } = new();

            public string Output { get; set; } = NeoSmartTileOptionIds.OutputSingle;

            public string Collider { get; set; } = NeoSmartTileOptionIds.ColliderSprite;

            public string RuleTransform { get; set; } =
                NeoSmartTileOptionIds.TransformFixed;

            public double MinAnimationSpeed { get; set; } = 1d;

            public double MaxAnimationSpeed { get; set; } = 1d;

            IReadOnlyList<INeoSmartTileNeighbor> INeoSmartTileRule.Neighbors => Neighbors;

            IReadOnlyList<Sprite> INeoSmartTileRule.Sprites => Sprites;
        }

        private sealed class TestSmartTileNeighbor : INeoSmartTileNeighbor
        {
            public Vector2Int Cell { get; set; }

            public string Condition { get; set; } = NeoSmartTileOptionIds.ConditionThis;

            public string? TileClassId { get; set; }
        }

        private sealed class MutableTestTileLayerRuntime : ReadOnlyNeoTileLayerRuntime
        {
            private readonly Dictionary<Vector2Int, NeoResolvedTileInstance> tilesByCell =
                new();

            public MutableTestTileLayerRuntime(
                string layerId,
                string displayName,
                string expectedClassId)
                : base(layerId, displayName, expectedClassId, null, null)
            {
            }

            public void SetTile(NeoResolvedTileInstance tile)
            {
                tilesByCell[tile.Cell] = tile;
            }

            public override IReadOnlyList<NeoResolvedTileInstance> GetTiles() =>
                new List<NeoResolvedTileInstance>(tilesByCell.Values);

            public override NeoResolvedTileInstance? GetTile(Vector2Int cell) =>
                tilesByCell.TryGetValue(cell, out var tile) ? tile : null;
        }

        /// <summary>
        /// Stands in for a generated <c>NeoTileLayerLink</c>: a layer group
        /// base is an object base, so it is a world object value as well as a
        /// tile layer link.
        /// </summary>
        private sealed class TestTileLayerLink
            : NeoGeneratedClassValue,
              INeoTileLayerLinkValue,
              INeoWorldObjectValue
        {
            private NeoList<string>? tiles;

            public TestTileLayerLink(
                NeoClient client,
                NeoMemberClass node,
                bool isReadOnly = true)
                : base(client, node, TileLayerLinkClassId, isReadOnly)
            {
            }

            public string Name { get; set; } = "";
            public NeoReadOnlyVector3 Position { get; set; } = new(Vector3.zero);
            public NeoReadOnlyVector3 Size { get; set; } = new(Vector3.one);

            public bool Enabled { get; set; } = true;

            public NeoList<string> Tiles =>
                tiles ??= new NeoList<string>(
                    client,
                    writableNode.Get<NeoMemberListWritable>("Tiles"),
                    () => writableNode.GetOrCreateCollection<NeoMemberListWritable>("Tiles"),
                    (_, __) => "",
                    item => NeoGeneratedTypesSupport.Value(item),
                    () => ThrowIfReadOnly("TestTileLayerLink.Tiles"),
                    () => IsReadOnly);

            public void ClearTiles()
            {
                Tiles.Clear();
            }
        }

        private sealed class TestObjectLayerLink
            : NeoGeneratedClassValue,
              INeoObjectLayerLinkValue
        {
            public TestObjectLayerLink(NeoClient client, NeoMemberClass node)
                : base(client, node, ObjectLayerLinkClassId)
            {
            }
        }

        private sealed class TestAuthoredTileLayer : NeoGeneratedTileLayerValue
        {
            public TestAuthoredTileLayer(
                NeoClient client,
                NeoMemberClass node,
                bool isReadOnly = true)
                : base(client, node, BackgroundLayerClassId, isReadOnly)
            {
            }

            public string? Name => node.Get<NeoMemberString>("Name").Text;

            public string? Description =>
                node.Get<NeoMemberString>("Description").Text;

            public NeoPlacementResult Place<TAsset>(Vector2Int cell)
                where TAsset : class => TrySetTileClass<TAsset>(cell);
        }

        private sealed class TestAuthoredObjectLayer : NeoGeneratedObjectLayerValue
        {
            public TestAuthoredObjectLayer(
                NeoClient client,
                NeoMemberClass node,
                bool isReadOnly = true)
                : base(client, node, ObjectsLayerClassId, isReadOnly)
            {
            }

            public NeoPlacementResult Spawn<TAsset>(Vector2Int cell)
                where TAsset : class => TrySpawnClass<TAsset>(cell);

            public NeoPlacementResult Spawn(
                Vector2Int cell,
                INeoValueReference asset) => TrySpawnValue(cell, asset);
        }

        private sealed class TestOtherTile : NeoGeneratedClassValue
        {
            public TestOtherTile(NeoClient client, NeoMemberClass node)
                : base(client, node, OtherTileClassId)
            {
            }
        }

        private sealed class RecordingTileSetLifecycle : NeoTileGridLifecycle
        {
            public JObject? ExistingInstance { get; private set; }

            public override void BeforeSetTile(NeoTileSetContext context)
            {
                ExistingInstance = context.ExistingInstance;
            }
        }

        private sealed class TestTileLayerRuntime : ReadOnlyNeoTileLayerRuntime
        {
            private readonly IReadOnlyList<NeoResolvedTileInstance> tiles;

            public TestTileLayerRuntime(
                string layerId,
                string displayName,
                string expectedClassId,
                string? sortingLayerName,
                int? sortingOrder,
                IReadOnlyList<NeoResolvedTileInstance>? tiles = null)
                : base(
                    layerId,
                    displayName,
                    expectedClassId,
                    sortingLayerName,
                    sortingOrder)
            {
                this.tiles = tiles ?? new List<NeoResolvedTileInstance>();
            }

            public override IReadOnlyList<NeoResolvedTileInstance> GetTiles() => tiles;
        }

        private sealed class RecordingProviderTileLayerRuntime
            : ReadOnlyNeoTileLayerRuntime, INeoTileLayerRenderTargetProvider
        {
            private IReadOnlyList<NeoResolvedTileInstance> tiles;
            private readonly List<string>? phases;

            public RecordingProviderTileLayerRuntime(
                string layerId,
                string displayName,
                string expectedClassId,
                string? sortingLayerName = null,
                int? sortingOrder = null,
                IReadOnlyList<NeoResolvedTileInstance>? tiles = null,
                List<string>? phases = null)
                : base(
                    layerId,
                    displayName,
                    expectedClassId,
                    sortingLayerName,
                    sortingOrder)
            {
                this.tiles = tiles ?? Array.Empty<NeoResolvedTileInstance>();
                this.phases = phases;
            }

            public Func<NeoTileLayerCreateContext, NeoTileLayerRenderTarget?>?
                TargetFactory { get; set; }

            public Action<NeoTileLayerRenderTargetContext>? CreatedCallback { get; set; }

            public Action<NeoTileLayerRenderTargetContext>?
                InitiallyRenderedCallback { get; set; }

            public Action<NeoTileLayerRenderTargetChangedContext>? ChangedCallback { get; set; }

            public List<NeoTileLayerRenderTarget> CreatedTargets { get; } = new();

            public List<NeoTileLayerRenderTargetChangedContext> ChangedContexts { get; } = new();

            public List<NeoTileLayerRenderTargetDestroyContext> DestroyingContexts { get; } = new();

            public List<NeoTileLayerRenderTargetDestroyedContext> DestroyedContexts { get; } = new();

            public override IReadOnlyList<NeoResolvedTileInstance> GetTiles() => tiles;

            public override NeoResolvedTileInstance? GetTile(Vector2Int cell) =>
                tiles.FirstOrDefault(tile => tile.Cell == cell);

            public void SetTiles(IReadOnlyList<NeoResolvedTileInstance> nextTiles)
            {
                tiles = nextTiles;
            }

            public NeoTileLayerRenderTarget? CreateRenderTarget(
                NeoTileLayerCreateContext context)
            {
                phases?.Add("create");
                return TargetFactory?.Invoke(context);
            }

            public void OnRenderTargetCreated(NeoTileLayerRenderTargetContext context)
            {
                phases?.Add("provider-created");
                CreatedTargets.Add(context.Target);
                CreatedCallback?.Invoke(context);
            }

            public void OnInitiallyRendered(NeoTileLayerRenderTargetContext context)
            {
                phases?.Add("initially-rendered");
                InitiallyRenderedCallback?.Invoke(context);
            }

            public void OnRenderTargetChanged(NeoTileLayerRenderTargetChangedContext context)
            {
                ChangedContexts.Add(context);
                ChangedCallback?.Invoke(context);
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

        private sealed class TestObjectLayerRuntime : ReadOnlyNeoObjectLayerRuntime
        {
            private readonly IReadOnlyList<NeoResolvedObjectInstance> objects;

            public TestObjectLayerRuntime(
                string layerId,
                string displayName,
                string expectedClassId,
                string? sortingLayerName,
                int? sortingOrder,
                IReadOnlyList<NeoResolvedObjectInstance>? objects = null)
                : base(
                    layerId,
                    displayName,
                    expectedClassId,
                    sortingLayerName,
                    sortingOrder)
            {
                this.objects = objects ?? new List<NeoResolvedObjectInstance>();
            }

            public override IReadOnlyList<NeoResolvedObjectInstance> GetObjects() =>
                objects;
        }

        private class TestGeneratedTileLayerRuntime
            : ReadOnlyNeoGeneratedTileLayer<TestTile>
        {
            public TestGeneratedTileLayerRuntime(NeoReadOnlyTileGridPrimitive primitive)
                : base(
                    primitive,
                    BackgroundLayerClassId,
                    "Background",
                    TileClassId,
                    null,
                    null)
            {
            }
        }

        private sealed class ThrowingAfterInitialTileLayerRuntime
            : TestGeneratedTileLayerRuntime
        {
            public ThrowingAfterInitialTileLayerRuntime(
                NeoReadOnlyTileGridPrimitive primitive)
                : base(primitive)
            {
            }

            public int GetTilesCalls { get; private set; }
            public int GetRenderSnapshotCalls { get; private set; }
            public bool ThrowOnGetTiles { get; set; }
            public bool ThrowOnGetRenderSnapshot { get; set; }
            public bool ThrowOnGetTile { get; set; }

            internal override NeoTileLayerRenderSnapshot GetRenderSnapshot()
            {
                GetRenderSnapshotCalls += 1;
                if (ThrowOnGetRenderSnapshot)
                {
                    throw new InvalidOperationException(
                        "Live sync must not rebuild the tile layer snapshot.");
                }
                return base.GetRenderSnapshot();
            }

            public override IReadOnlyList<NeoResolvedTileInstance> GetTiles()
            {
                GetTilesCalls += 1;
                if (ThrowOnGetTiles)
                {
                    throw new InvalidOperationException(
                        "Live sync must not reconcile the whole tile layer.");
                }
                return base.GetTiles();
            }

            public override NeoResolvedTileInstance? GetTile(Vector2Int cell)
            {
                if (ThrowOnGetTile)
                {
                    throw new InvalidOperationException(
                        "Live sync should use cached source deltas for simple clears.");
                }
                return base.GetTile(cell);
            }
        }

        private sealed class TestTileGridContent : INeoTileGridContent
        {
            public TestTileGridContent(
                NeoReadOnlyTileGridPrimitive primitive,
                IReadOnlyList<IReadOnlyNeoTileLayerRuntime> tileLayers)
            {
                Primitive = primitive;
                TileLayersInOrder = tileLayers;
                ObjectLayersInOrder = Array.Empty<IReadOnlyNeoObjectLayerRuntime>();
            }

            public NeoReadOnlyTileGridPrimitive Primitive { get; }
            public IReadOnlyList<IReadOnlyNeoTileLayerRuntime> TileLayersInOrder { get; }
            public IReadOnlyList<IReadOnlyNeoObjectLayerRuntime> ObjectLayersInOrder { get; }
            public NeoTileGridRenderer? Renderer => Primitive.Renderer;
            public IDisposable OnChanged(Action<NeoTileGridChangedArgs> handler) =>
                Primitive.OnChanged(handler);
        }

        /// <summary>
        /// Stands in for a generated <c>NeoObject</c>: world kind
        /// <c>object</c>, so it carries the composition, collider, and
        /// sorting group contracts on top of the object base contract.
        /// </summary>
        private sealed class TestComposedObject
            : NeoGeneratedClassValue,
              INeoWorldObjectValue,
              INeoObjectCompositionSource,
              INeoColliderSource,
              INeoSortingGroupSource
        {
            public TestComposedObject(
                NeoClient client,
                NeoMemberClass node,
                bool isReadOnly = true)
                : base(client, node, ObjectClassId, isReadOnly)
            {
            }

            public Sprite? Sprite { get; set; }
            public string Name { get; set; } = "";
            public NeoReadOnlyVector3 Position { get; set; } = new(Vector3.zero);
            public NeoReadOnlyVector3 Size { get; set; } = new(Vector3.one);

            public bool Enabled { get; set; } = true;
            public IReadOnlyList<INeoWorldObjectValue> Children { get; set; } =
                new List<INeoWorldObjectValue>();
            public INeoCollider? Collider { get; set; }
            public INeoSortingGroup? SortingGroup { get; set; }
        }

        /// <summary>
        /// Stands in for a generated <c>NeoSpriteObject</c>: world kind
        /// <c>spriteObject</c>, a leaf that carries the SpriteRenderer state.
        /// </summary>
        private sealed class TestSpriteChild : INeoSpriteObjectValue
        {
            public string? valueId => null;
            public string Name { get; set; } = "";
            public NeoReadOnlyVector3 Position { get; set; } = new(Vector3.zero);
            public NeoReadOnlyVector3 Size { get; set; } = new(Vector3.one);

            public bool Enabled { get; set; } = true;
            public Sprite Sprite { get; set; } = null!;
            public bool FlipX { get; set; }
            public bool FlipY { get; set; }
            public string MaskInteraction { get; set; } =
                NeoSpriteMaskInteraction.None.optionId;
            public int? SortingOrder { get; set; }
        }

        /// <summary>
        /// Stands in for a placed generated <c>NeoSpriteObject</c> — the root
        /// sprite fallback path, which needs a resolvable class value.
        /// </summary>
        private sealed class TestSpriteObject
            : NeoGeneratedClassValue,
              INeoSpriteObjectValue
        {
            public TestSpriteObject(NeoClient client, NeoMemberClass node)
                : base(client, node, ObjectClassId)
            {
            }

            public string Name { get; set; } = "";
            public NeoReadOnlyVector3 Position { get; set; } = new(Vector3.zero);
            public NeoReadOnlyVector3 Size { get; set; } = new(Vector3.one);

            public bool Enabled { get; set; } = true;
            public Sprite Sprite { get; set; } = null!;
            public bool FlipX { get; set; }
            public bool FlipY { get; set; }
            public string MaskInteraction { get; set; } =
                NeoSpriteMaskInteraction.None.optionId;
            public int? SortingOrder { get; set; }
        }

        /// <summary>
        /// Stands in for a nested generated <c>NeoObject</c> composition part —
        /// the character-rig shape, where a part owns its own sprite layers and
        /// hiding it must hide the whole subtree.
        /// </summary>
        private sealed class TestComposedChild
            : INeoWorldObjectValue,
              INeoObjectCompositionSource
        {
            public string? valueId => null;
            public string Name { get; set; } = "";
            public NeoReadOnlyVector3 Position { get; set; } = new(Vector3.zero);
            public NeoReadOnlyVector3 Size { get; set; } = new(Vector3.one);

            public bool Enabled { get; set; } = true;
            public IReadOnlyList<INeoWorldObjectValue> Children { get; set; } =
                new List<INeoWorldObjectValue>();
        }

        /// <summary>
        /// Stands in for a placed generated object that carries both a
        /// composition and its own sprite state — the shape whose root sprite
        /// is drawn only when the composition renders nothing.
        /// </summary>
        private sealed class TestComposedSpriteObject
            : NeoGeneratedClassValue,
              INeoObjectCompositionSource,
              INeoSpriteObjectValue
        {
            public TestComposedSpriteObject(NeoClient client, NeoMemberClass node)
                : base(client, node, ObjectClassId)
            {
            }

            public string Name { get; set; } = "";
            public NeoReadOnlyVector3 Position { get; set; } = new(Vector3.zero);
            public NeoReadOnlyVector3 Size { get; set; } = new(Vector3.one);

            public bool Enabled { get; set; } = true;
            public IReadOnlyList<INeoWorldObjectValue> Children { get; set; } =
                new List<INeoWorldObjectValue>();
            public Sprite Sprite { get; set; } = null!;
            public bool FlipX { get; set; }
            public bool FlipY { get; set; }
            public string MaskInteraction { get; set; } =
                NeoSpriteMaskInteraction.None.optionId;
            public int? SortingOrder { get; set; }
        }

        private sealed class TestSortingGroup : INeoSortingGroup
        {
            public string? valueId => null;
            public bool SortAtRoot { get; set; }
        }

        private sealed class TestObjectCollider : INeoCollider
        {
            public string? valueId => null;
            public NeoReadOnlyVector2 Size { get; set; } = new(Vector2.one);
            public NeoReadOnlyVector2? Offset { get; set; }
            public bool? IsTrigger { get; set; }
        }

        private sealed class TestColliderSource : INeoColliderSource
        {
            public INeoCollider? Collider { get; set; }
        }

        private static Sprite CreateTestSprite(string name)
        {
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(
                texture,
                new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return sprite;
        }

        private static void DestroyTestSprite(Sprite sprite)
        {
            UnityEngine.Object.DestroyImmediate(sprite.texture);
            UnityEngine.Object.DestroyImmediate(sprite);
        }

        private static TestSmartTile SmartTileWithSelfNeighbor(
            Sprite connectedSprite,
            string condition)
        {
            var rule = new TestSmartTileRule();
            rule.Sprites.Add(connectedSprite);
            rule.Neighbors.Add(new TestSmartTileNeighbor
            {
                Cell = new Vector2Int(1, 0),
                Condition = condition,
            });
            var smartTile = new TestSmartTile();
            smartTile.Rules.Add(rule);
            return smartTile;
        }

        private static TestSmartTile SmartTileWithInheritsClassNeighbor(
            Sprite connectedSprite,
            string tileClassId)
        {
            var rule = new TestSmartTileRule();
            rule.Sprites.Add(connectedSprite);
            rule.Neighbors.Add(new TestSmartTileNeighbor
            {
                Cell = new Vector2Int(1, 0),
                Condition = NeoSmartTileOptionIds.ConditionInheritsFromClass,
                TileClassId = tileClassId,
            });
            var smartTile = new TestSmartTile();
            smartTile.Rules.Add(rule);
            return smartTile;
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            BuildInheritanceTileFactories()
        {
            NeoGeneratedTypesSupport.ReadOnlyClassFactory factory =
                (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestTile(resolvedClient, node));
            return new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = factory,
                [BaseTileClassId] = factory,
                [SubTileClassId] = factory,
                [OtherTileClassId] = factory,
            };
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            BuildReadOnlyFactories()
        {
            return new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestTile(resolvedClient, node)),
                [TileLayerLinkClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestTileLayerLink(resolvedClient, node)),
                [ObjectLayerLinkClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestObjectLayerLink(resolvedClient, node)),
            };
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
            BuildWritableFactories()
        {
            return new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
            {
                [TileClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestTile(resolvedClient, node)),
                [TileLayerLinkClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestTileLayerLink(
                            resolvedClient,
                            node,
                            isReadOnly: false)),
            };
        }

        private static void SeedWritableTileLayerLink(NeoClient client)
        {
            client.AddSaveValue(
                "shop-floor-link",
                new ObjectMemberValue
                {
                    id = "shop-floor-link",
                    classId = TileLayerLinkClassId,
                    value = new Dictionary<string, string>
                    {
                        ["TileLayer"] = "shop-floor-link-layer",
                        ["Tiles"] = "shop-floor-link-tiles",
                    },
                });
            client.AddSaveValue(
                "shop-floor-link-tiles",
                new ArrayMemberValue
                {
                    id = "shop-floor-link-tiles",
                    value = new[] { "floor-local" },
                });
        }

        private const string GridClassId = "grid-class";
        private const string ObjectLayerLinkClassId = "object-layer-link-class";

        private static string PlacedChildPositionId(NeoClient client, string placementId)
        {
            var root = (ObjectMemberValue)client.saveValues[placementId];
            var children = (ArrayMemberValue)client.saveValues[root.value!["Children"]];
            var child = (ObjectMemberValue)client.saveValues[children.value![0]];
            return child.value!["Position"];
        }

        private static ProjectData BuildPlacementAnimationProjectData()
        {
            const string clipClassId = "animation-clip-class";
            const string frameClassId = "animation-frame-class";
            const string childOverrideClassId = "animation-child-override-class";
            const string trackClassId = "animation-track-class";
            ProjectData data = BuildClassBackedTileGridProjectData();

            data.members["object-child-entry-member"] = new ClassMember
            {
                id = "object-child-entry-member",
                projectId = "project-a",
                name = "Child",
                kind = MemberKind.Class,
                classId = ObjectClassId,
                createdAt = "x",
                updatedAt = "x",
            };
            ((ListMember)data.members["object-children-member"]).entryMemberId =
                "object-child-entry-member";
            data.members["object-position-member"].DeclaredStorage = NeoMemberStorage.Session;
            // Session-storage Enabled, the shape section 1.1 of the spec calls
            // for on a class that wants visibility written at runtime.
            data.members["object-enabled-member"].DeclaredStorage = NeoMemberStorage.Session;

            data.classes[ObjectClassId].schema["Animate"] = "animate-member";
            data.classes[ObjectClassId].schema["TrackAnimate"] = "track-animate-member";
            data.classes[ObjectClassId].schema["ChildAnimate"] = "child-animate-member";
            data.members["animate-member"] = ClipMember(
                "animate-member",
                "Animate",
                clipClassId,
                "parent-clip");
            data.members["track-animate-member"] = ClipMember(
                "track-animate-member",
                "TrackAnimate",
                clipClassId,
                "track-parent-clip");
            data.members["child-animate-member"] = ClipMember(
                "child-animate-member",
                "ChildAnimate",
                clipClassId,
                "child-clip");

            data.classes[clipClassId] = new NeoSchemaClass
            {
                id = clipClassId,
                projectId = "project-a",
                name = "Animation Clip",
                schema = new Dictionary<string, string>
                {
                    ["FPS"] = "animation-fps-member",
                    ["Duration"] = "animation-duration-member",
                    ["Frames"] = "animation-frames-member",
                    ["Tracks"] = "animation-tracks-member",
                },
                system = JObject.FromObject(new { worldKind = "animationClip" }),
            };
            data.classes[frameClassId] = new NeoSchemaClass
            {
                id = frameClassId,
                projectId = "project-a",
                name = "Animation Frame",
                schema = new Dictionary<string, string>
                {
                    ["Index"] = "animation-index-member",
                    ["Overrides"] = "animation-overrides-member",
                    ["ChildOverrides"] = "animation-child-overrides-member",
                },
            };
            data.classes[childOverrideClassId] = new NeoSchemaClass
            {
                id = childOverrideClassId,
                projectId = "project-a",
                name = "Animation Child Override",
                schema = new Dictionary<string, string>
                {
                    ["Child"] = "animation-child-member",
                    ["Overrides"] = "animation-child-values-member",
                },
            };
            data.classes[trackClassId] = new NeoSchemaClass
            {
                id = trackClassId,
                projectId = "project-a",
                name = "Animation Child Track",
                // P48 §2.2 dispatches a Tracks row by its own class's world
                // kind, so a child track has to say it is one.
                system = JObject.FromObject(new { worldKind = "animationChildTrack" }),
                schema = new Dictionary<string, string>
                {
                    ["Child"] = "animation-track-child-member",
                    ["ClipKey"] = "animation-track-key-member",
                    ["StartFrame"] = "animation-track-start-member",
                    // P48 §2.1's authored playback, on the shared base and so
                    // on this child track too.
                    ["Direction"] = "animation-track-direction-member",
                    ["OffsetStartIndex"] = "animation-track-offset-start-member",
                    ["OffsetEndIndex"] = "animation-track-offset-end-member",
                },
            };
            data.enums[PlayDirectionEnumId] = new NeoCompose.Runtime.Json.Enum
            {
                id = PlayDirectionEnumId,
                projectId = "project-a",
                name = "NeoPlayDirection",
                options = new Dictionary<string, EnumOption>
                {
                    [NeoPlayDirection.Forward.optionId] = new EnumOption { text = "Forward" },
                    [NeoPlayDirection.Reverse.optionId] = new EnumOption { text = "Reverse" },
                },
                optionKeyOrder = new List<string>
                {
                    NeoPlayDirection.Forward.optionId,
                    NeoPlayDirection.Reverse.optionId,
                },
                createdAt = "x",
                updatedAt = "x",
            };

            data.members["animation-fps-member"] = IntMember(
                "animation-fps-member",
                "FPS");
            data.members["animation-duration-member"] = IntMember(
                "animation-duration-member",
                "Duration");
            data.members["animation-index-member"] = IntMember(
                "animation-index-member",
                "Index");
            data.members["animation-track-start-member"] = IntMember(
                "animation-track-start-member",
                "StartFrame");
            data.members["animation-track-offset-start-member"] = IntMember(
                "animation-track-offset-start-member",
                "OffsetStartIndex");
            data.members["animation-track-offset-end-member"] = IntMember(
                "animation-track-offset-end-member",
                "OffsetEndIndex");
            data.members["animation-track-direction-member"] = new EnumMember
            {
                id = "animation-track-direction-member",
                projectId = "project-a",
                name = "Direction",
                kind = MemberKind.Enum,
                enumId = PlayDirectionEnumId,
                Selection = NeoMemberSelectionKind.Single,
                createdAt = "x",
                updatedAt = "x",
            };
            data.members["animation-frames-member"] = ListMember(
                "animation-frames-member",
                "Frames",
                "animation-frame-entry-member");
            data.members["animation-frame-entry-member"] = ClassMember(
                "animation-frame-entry-member",
                "Frame",
                frameClassId);
            data.members["animation-tracks-member"] = ListMember(
                "animation-tracks-member",
                "Tracks",
                "animation-track-entry-member");
            data.members["animation-track-entry-member"] = ClassMember(
                "animation-track-entry-member",
                "Track",
                trackClassId);
            data.members["animation-overrides-member"] = ClassMember(
                "animation-overrides-member",
                "Overrides",
                ObjectClassId);
            data.members["animation-child-overrides-member"] = ListMember(
                "animation-child-overrides-member",
                "ChildOverrides",
                "animation-child-override-entry-member");
            data.members["animation-child-override-entry-member"] = ClassMember(
                "animation-child-override-entry-member",
                "ChildOverride",
                childOverrideClassId);
            data.members["animation-child-member"] = LookupMember(
                "animation-child-member",
                "Child",
                "object-children-member");
            data.members["animation-child-values-member"] = ClassMember(
                "animation-child-values-member",
                "Overrides",
                ObjectClassId);
            data.members["animation-track-child-member"] = LookupMember(
                "animation-track-child-member",
                "Child",
                "object-children-member");
            data.members["animation-track-key-member"] = new StringMember
            {
                id = "animation-track-key-member",
                projectId = "project-a",
                name = "ClipKey",
                kind = MemberKind.String,
                Format = NeoStringFormatKind.Plain,
                createdAt = "x",
                updatedAt = "x",
            };

            ((ObjectMemberValue)data.values["shop-object"]).value!["Children"] =
                "shop-authored-children";
            data.values["shop-authored-children"] = ArrayValue(
                "shop-authored-children",
                "shop-authored-child");
            data.values["shop-authored-child"] = new ObjectMemberValue
            {
                id = "shop-authored-child",
                classId = ObjectClassId,
                value = new Dictionary<string, string>
                {
                    ["Position"] = "shop-authored-child-position",
                },
            };
            data.values["shop-authored-child-position"] = VectorValue(
                "shop-authored-child-position",
                1);

            data.values["parent-clip"] = ClipValue(
                clipClassId,
                "parent-clip",
                "parent-clip-fps",
                "parent-clip-duration",
                "parent-clip-frames",
                "parent-clip-tracks");
            data.values["parent-clip-fps"] = NumberValue("parent-clip-fps", 10);
            data.values["parent-clip-duration"] = NumberValue("parent-clip-duration", 1);
            data.values["parent-clip-frames"] = ArrayValue(
                "parent-clip-frames",
                "parent-frame-0");
            data.values["parent-clip-tracks"] = ArrayValue("parent-clip-tracks");
            data.values["parent-frame-0"] = new ObjectMemberValue
            {
                id = "parent-frame-0",
                classId = frameClassId,
                value = new Dictionary<string, string>
                {
                    ["Index"] = "parent-frame-0-index",
                    ["ChildOverrides"] = "parent-frame-0-child-overrides",
                },
            };
            data.values["parent-frame-0-index"] = NumberValue("parent-frame-0-index", 0);
            data.values["parent-frame-0-child-overrides"] = ArrayValue(
                "parent-frame-0-child-overrides",
                "parent-child-override");
            data.values["parent-child-override"] = new ObjectMemberValue
            {
                id = "parent-child-override",
                classId = childOverrideClassId,
                value = new Dictionary<string, string>
                {
                    ["Child"] = "parent-child-lookup",
                    ["Overrides"] = "parent-child-values",
                },
            };
            data.values["parent-child-lookup"] = ArrayValue(
                "parent-child-lookup",
                "shop-authored-child");
            data.values["parent-child-values"] = new ObjectMemberValue
            {
                id = "parent-child-values",
                classId = ObjectClassId,
                value = new Dictionary<string, string>
                {
                    ["Position"] = "parent-child-position-override",
                },
            };
            data.values["parent-child-position-override"] = VectorValue(
                "parent-child-position-override",
                9);

            data.values["child-clip"] = ClipValue(
                clipClassId,
                "child-clip",
                "child-clip-fps",
                "child-clip-duration",
                "child-clip-frames",
                "child-clip-tracks");
            data.values["child-clip-fps"] = NumberValue("child-clip-fps", 10);
            data.values["child-clip-duration"] = NumberValue("child-clip-duration", 1);
            data.values["child-clip-frames"] = ArrayValue(
                "child-clip-frames",
                "child-frame-0");
            data.values["child-clip-tracks"] = ArrayValue("child-clip-tracks");
            data.values["child-frame-0"] = new ObjectMemberValue
            {
                id = "child-frame-0",
                classId = frameClassId,
                value = new Dictionary<string, string>
                {
                    ["Index"] = "child-frame-0-index",
                    ["Overrides"] = "child-frame-0-values",
                },
            };
            data.values["child-frame-0-index"] = NumberValue("child-frame-0-index", 0);
            data.values["child-frame-0-values"] = new ObjectMemberValue
            {
                id = "child-frame-0-values",
                classId = ObjectClassId,
                value = new Dictionary<string, string>
                {
                    ["Position"] = "child-position-override",
                },
            };
            data.values["child-position-override"] = VectorValue(
                "child-position-override",
                7);

            data.values["track-parent-clip"] = ClipValue(
                clipClassId,
                "track-parent-clip",
                "track-parent-fps",
                "track-parent-duration",
                "track-parent-frames",
                "track-parent-tracks");
            data.values["track-parent-fps"] = NumberValue("track-parent-fps", 10);
            data.values["track-parent-duration"] = NumberValue("track-parent-duration", 2);
            data.values["track-parent-frames"] = ArrayValue("track-parent-frames");
            data.values["track-parent-tracks"] = ArrayValue(
                "track-parent-tracks",
                "track-parent-child");
            data.values["track-parent-child"] = new ObjectMemberValue
            {
                id = "track-parent-child",
                classId = trackClassId,
                value = new Dictionary<string, string>
                {
                    ["Child"] = "track-parent-child-lookup",
                    ["ClipKey"] = "track-parent-child-key",
                    ["StartFrame"] = "track-parent-child-start",
                },
            };
            data.values["track-parent-child-lookup"] = ArrayValue(
                "track-parent-child-lookup",
                "shop-authored-child");
            data.values["track-parent-child-key"] = new StringMemberValue
            {
                id = "track-parent-child-key",
                value = "ChildAnimate",
            };
            data.values["track-parent-child-start"] = NumberValue(
                "track-parent-child-start",
                1);
            return data;

            static ClassMember ClipMember(
                string id,
                string name,
                string classId,
                string valueId) => new()
                {
                    id = id,
                    projectId = "project-a",
                    name = name,
                    kind = MemberKind.Class,
                    classId = classId,
                    valueId = valueId,
                    Storage = NeoMemberStorage.Immutable,
                    createdAt = "x",
                    updatedAt = "x",
                };
            static IntMember IntMember(string id, string name) => new()
            {
                id = id,
                projectId = "project-a",
                name = name,
                kind = MemberKind.Int,
                createdAt = "x",
                updatedAt = "x",
            };
            static ListMember ListMember(string id, string name, string entryId) => new()
            {
                id = id,
                projectId = "project-a",
                name = name,
                kind = MemberKind.List,
                entryMemberId = entryId,
                createdAt = "x",
                updatedAt = "x",
            };
            static ClassMember ClassMember(string id, string name, string classId) => new()
            {
                id = id,
                projectId = "project-a",
                name = name,
                kind = MemberKind.Class,
                classId = classId,
                createdAt = "x",
                updatedAt = "x",
            };
            static LookupMember LookupMember(
                string id,
                string name,
                string collectionId) => new()
                {
                    id = id,
                    projectId = "project-a",
                    name = name,
                    kind = MemberKind.Lookup,
                    collectionMemberId = collectionId,
                    createdAt = "x",
                    updatedAt = "x",
                };
            static ArrayMemberValue ArrayValue(string id, params string[] values) => new()
            {
                id = id,
                value = values,
            };
            static NumberMemberValue NumberValue(string id, double value) => new()
            {
                id = id,
                value = value,
            };
            static Vector3MemberValue VectorValue(string id, float x) => new()
            {
                id = id,
                value = new NeoVector3Value { x = x, y = 0, z = 0 },
            };
            static ObjectMemberValue ClipValue(
                string classId,
                string id,
                string fps,
                string duration,
                string frames,
                string tracks) => new()
                {
                    id = id,
                    classId = classId,
                    value = new Dictionary<string, string>
                    {
                        ["FPS"] = fps,
                        ["Duration"] = duration,
                        ["Frames"] = frames,
                        ["Tracks"] = tracks,
                    },
                };
        }

        private static ProjectData BuildClassBackedTileGridProjectData()
        {
            var data = BuildTileGridProjectData();
            data.metadata = new ProjectExportMetadata
            {
                schemaVersion = NeoProjectExportContract.CurrentSchemaVersion,
                projectId = "project-a",
                versionId = "version-relations",
            };
            data.classes[TileInstanceClassId].schema.Remove("Tile");
            data.classes[TileLayerLinkSystemBaseClassId] = new NeoSchemaClass
            {
                id = TileLayerLinkSystemBaseClassId,
                projectId = "project-a",
                name = "Neo Tile Layer Link",
                schema = new Dictionary<string, string>
                {
                    ["Tiles"] = "tile-layer-link-tiles-member",
                },
                Modifier = NeoClassModifierKind.Abstract,
                system = JObject.FromObject(new { worldKind = "tileLayerLink" }),
            };
            data.classes[ObjectLayerLinkSystemBaseClassId] = new NeoSchemaClass
            {
                id = ObjectLayerLinkSystemBaseClassId,
                projectId = "project-a",
                name = "Neo Object Layer Link",
                schema = new Dictionary<string, string>
                {
                    ["Objects"] = "object-layer-link-objects-member",
                },
                Modifier = NeoClassModifierKind.Abstract,
                system = JObject.FromObject(new { worldKind = "objectLayerLink" }),
            };
            data.classes[TileLayerLinkClassId].extendsClassId =
                TileLayerLinkSystemBaseClassId;
            data.classes[TileLayerLinkClassId].schema.Remove("TileLayer");
            data.classes[TileLayerLinkClassId].schema.Remove("Tiles");
            data.classes[ObjectLayerLinkClassId].extendsClassId =
                ObjectLayerLinkSystemBaseClassId;
            data.classes[ObjectLayerLinkClassId].schema.Remove("ObjectLayer");
            data.classes[ObjectLayerLinkClassId].schema.Remove("Objects");
            data.classes[BackgroundLayerClassId] = new NeoSchemaClass
            {
                id = BackgroundLayerClassId,
                projectId = "project-a",
                name = "Background Tile Layer",
                schema = new Dictionary<string, string>
                {
                    ["Name"] = "background-layer-name-member",
                    ["Description"] = "background-layer-description-member",
                },
                system = JObject.FromObject(new { worldKind = "tileLayer" }),
            };
            data.classes[ObjectsLayerClassId] = new NeoSchemaClass
            {
                id = ObjectsLayerClassId,
                projectId = "project-a",
                name = "Objects Layer",
                schema = new Dictionary<string, string>(),
                system = JObject.FromObject(new { worldKind = "objectLayer" }),
            };
            data.members["background-layer-name-member"] = new StringMember
            {
                id = "background-layer-name-member",
                projectId = "project-a",
                name = "Name",
                kind = MemberKind.String,
                Format = NeoStringFormatKind.Plain,
                defaultValue = new StringMemberValueBase
                {
                    value = "Default Background",
                },
                createdAt = "x",
                updatedAt = "x",
            };
            data.members["background-layer-description-member"] = new StringMember
            {
                id = "background-layer-description-member",
                projectId = "project-a",
                name = "Description",
                kind = MemberKind.String,
                Format = NeoStringFormatKind.Plain,
                defaultValue = new StringMemberValueBase
                {
                    value = "Default layer description",
                },
                createdAt = "x",
                updatedAt = "x",
            };
            data.internalRecordRelations = new Dictionary<string, InternalRecordRelation>
            {
                ["relation-grid-layer"] = Relation(
                    "relation-grid-layer",
                    InternalRecordRelationKinds.WorldGridTileLayer,
                    GridClassId,
                    BackgroundLayerClassId,
                    "a0"),
                ["relation-grid-import"] = Relation(
                    "relation-grid-import",
                    InternalRecordRelationKinds.WorldGridTileImport,
                    GridClassId,
                    TileClassId),
                ["relation-tile-compatible"] = Relation(
                    "relation-tile-compatible",
                    InternalRecordRelationKinds.WorldTileCompatibleLayer,
                    TileClassId,
                    BackgroundLayerClassId),
                ["relation-grid-object-layer"] = Relation(
                    "relation-grid-object-layer",
                    InternalRecordRelationKinds.WorldGridObjectLayer,
                    GridClassId,
                    ObjectsLayerClassId,
                    "b0"),
                ["relation-grid-object-import"] = Relation(
                    "relation-grid-object-import",
                    InternalRecordRelationKinds.WorldGridObjectImport,
                    GridClassId,
                    ObjectClassId),
                ["relation-object-compatible"] = Relation(
                    "relation-object-compatible",
                    InternalRecordRelationKinds.WorldObjectCompatibleLayer,
                    ObjectClassId,
                    ObjectsLayerClassId),
                ["relation-link-target"] = Relation(
                    "relation-link-target",
                    InternalRecordRelationKinds.WorldTileLayerLinkTarget,
                    TileLayerLinkClassId,
                    BackgroundLayerClassId),
                ["relation-object-link-target"] = Relation(
                    "relation-object-link-target",
                    InternalRecordRelationKinds.WorldObjectLayerLinkTarget,
                    ObjectLayerLinkClassId,
                    ObjectsLayerClassId),
            };

            var backgroundLink = (ObjectMemberValue)data.values["background-link"];
            backgroundLink.value!["layerClassId"] = BackgroundLayerClassId;
            backgroundLink.value["layerOverrideValueId"] = "background-layer-override";
            backgroundLink.value.Remove("TileLayer");
            data.values["background-layer-override"] = new ObjectMemberValue
            {
                id = "background-layer-override",
                classId = BackgroundLayerClassId,
                containerId = "background-link",
                value = new Dictionary<string, string>
                {
                    ["Name"] = "background-layer-override-name",
                },
            };
            data.values["background-layer-override-name"] = new StringMemberValue
            {
                id = "background-layer-override-name",
                value = "Override Background",
            };
            var objectsLink = (ObjectMemberValue)data.values["objects-link"];
            objectsLink.value!["layerClassId"] = ObjectsLayerClassId;
            objectsLink.value.Remove("ObjectLayer");
            var shop = (ObjectMemberValue)data.values["shop-1"];
            shop.value!["assetClassId"] = ObjectClassId;
            var shopFloorLink = (ObjectMemberValue)data.values["shop-floor-link"];
            shopFloorLink.value!["layerClassId"] = BackgroundLayerClassId;
            shopFloorLink.value.Remove("TileLayer");
            ((ArrayMemberValue)data.values["background-link-tiles"]).value =
                new[] { "class-backed-placement" };
            data.values["class-backed-placement"] = new ObjectMemberValue
            {
                id = "class-backed-placement",
                classId = TileInstanceClassId,
                containerId = "background-link-tiles",
                value = new Dictionary<string, string>
                {
                    ["Cell"] = "class-backed-placement-cell",
                    ["assetClassId"] = TileClassId,
                },
            };
            data.values["class-backed-placement-cell"] = new Vector2MemberValue
            {
                id = "class-backed-placement-cell",
                value = new NeoVector2Value { x = 2, y = 3 },
            };
            var floorLocal = (ObjectMemberValue)data.values["floor-local"];
            floorLocal.value!["assetClassId"] = TileClassId;
            floorLocal.value.Remove("Tile");
            ((ArrayMemberValue)data.values["town-grid-children"]).value =
                new[] { "background-link", "objects-link", "blocked-path-link" };
            data.values["blocked-path-link"] = new ObjectMemberValue
            {
                id = "blocked-path-link",
                classId = TileLayerLinkClassId,
                value = new Dictionary<string, string>
                {
                    ["layerClassId"] = BackgroundLayerClassId,
                    ["Tiles"] = "blocked-path-tiles",
                },
            };
            data.values["blocked-path-tiles"] = new ArrayMemberValue
            {
                id = "blocked-path-tiles",
                value = new[] { "blocked-path-placement" },
            };
            data.values["blocked-path-placement"] = new ObjectMemberValue
            {
                id = "blocked-path-placement",
                classId = TileInstanceClassId,
                value = new Dictionary<string, string>
                {
                    ["Cell"] = "blocked-path-cell",
                    ["assetClassId"] = TileClassId,
                },
            };
            data.values["blocked-path-cell"] = new Vector2MemberValue
            {
                id = "blocked-path-cell",
                value = new NeoVector2Value { x = 12, y = 13 },
            };
            return data;
        }

        private static void ConfigureObjectPlacementFootprint(
            ProjectData data,
            Vector2Int visualSize,
            params Vector2Int[] placementCells)
        {
            ConfigureObjectPlacementFootprint(
                data,
                "shop-1",
                visualSize,
                placementCells);
        }

        private static void ConfigureObjectPlacementFootprint(
            ProjectData data,
            string objectValueId,
            Vector2Int visualSize,
            params Vector2Int[] placementCells)
        {
            data.classes[ObjectClassId].schema["Size"] = "object-size-member";
            data.classes[ObjectClassId].schema["PlacementTiles"] =
                "object-placement-tiles-member";
            data.classes[ObjectPlacementTileClassId] = new NeoSchemaClass
            {
                id = ObjectPlacementTileClassId,
                projectId = "project-a",
                name = "Object Placement Tile",
                schema = new Dictionary<string, string>
                {
                    ["Cell"] = "object-placement-cell-member",
                },
            };
            data.members["object-size-member"] = new Vector3Member
            {
                id = "object-size-member",
                projectId = "project-a",
                name = "Size",
                kind = MemberKind.Vector3,
            };
            data.members["object-placement-tiles-member"] = new ListMember
            {
                id = "object-placement-tiles-member",
                projectId = "project-a",
                name = "PlacementTiles",
                kind = MemberKind.List,
                entryMemberId = "object-placement-tile-entry-member",
            };
            data.members["object-placement-tile-entry-member"] = new ClassMember
            {
                id = "object-placement-tile-entry-member",
                projectId = "project-a",
                name = "PlacementTile",
                kind = MemberKind.Class,
                classId = ObjectPlacementTileClassId,
            };
            data.members["object-placement-cell-member"] = new Vector2IntMember
            {
                id = "object-placement-cell-member",
                projectId = "project-a",
                name = "Cell",
                kind = MemberKind.Vector2Int,
            };

            var shop = (ObjectMemberValue)data.values[objectValueId];
            string sizeValueId = $"{objectValueId}-size";
            string placementTilesValueId = $"{objectValueId}-placement-tiles";
            shop.value!["Size"] = sizeValueId;
            shop.value["PlacementTiles"] = placementTilesValueId;
            data.values[sizeValueId] = new Vector3MemberValue
            {
                id = sizeValueId,
                value = new NeoVector3Value
                {
                    x = visualSize.x,
                    y = visualSize.y,
                    z = 0,
                },
            };

            var placementValueIds = new string[placementCells.Length];
            for (int index = 0; index < placementCells.Length; index += 1)
            {
                string placementValueId = $"{objectValueId}-placement-{index}";
                string cellValueId = $"{objectValueId}-placement-cell-{index}";
                placementValueIds[index] = placementValueId;
                data.values[placementValueId] = new ObjectMemberValue
                {
                    id = placementValueId,
                    classId = ObjectPlacementTileClassId,
                    containerId = placementTilesValueId,
                    value = new Dictionary<string, string>
                    {
                        ["Cell"] = cellValueId,
                    },
                };
                data.values[cellValueId] = new Vector2MemberValue
                {
                    id = cellValueId,
                    value = new NeoVector2Value
                    {
                        x = placementCells[index].x,
                        y = placementCells[index].y,
                    },
                };
            }
            data.values[placementTilesValueId] = new ArrayMemberValue
            {
                id = placementTilesValueId,
                value = placementValueIds,
            };
        }

        private static InternalRecordRelation Relation(
            string id,
            string kind,
            string sourceClassId,
            string targetClassId,
            string? orderKey = null)
        {
            return new InternalRecordRelation
            {
                id = id,
                projectId = "project-a",
                relationKind = kind,
                sourceRecordKind = "class",
                sourceRecordId = sourceClassId,
                targetRecordKind = "class",
                targetRecordId = targetClassId,
                orderKey = orderKey,
                createdAt = "2026-01-01T00:00:00.000Z",
                updatedAt = "2026-01-01T00:00:00.000Z",
            };
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            BuildClassBackedReadOnlyFactories()
        {
            return new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestTile(resolvedClient, node)),
                [OtherTileClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestOtherTile(resolvedClient, node)),
                [ObjectClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestComposedObject(resolvedClient, node)),
                [TileLayerLinkClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestTileLayerLink(resolvedClient, node)),
                [ObjectLayerLinkClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestObjectLayerLink(resolvedClient, node)),
                [BackgroundLayerClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestAuthoredTileLayer(resolvedClient, node)),
                [ObjectsLayerClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestAuthoredObjectLayer(resolvedClient, node)),
            };
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
            BuildClassBackedWritableFactories()
        {
            return new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
            {
                [ObjectClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestComposedObject(
                            resolvedClient,
                            node,
                            isReadOnly: false)),
                [BackgroundLayerClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestAuthoredTileLayer(
                            resolvedClient,
                            node,
                            isReadOnly: false)),
                [ObjectsLayerClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestAuthoredObjectLayer(
                            resolvedClient,
                            node,
                            isReadOnly: false)),
                [TileLayerLinkClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestTileLayerLink(
                            resolvedClient,
                            node,
                            isReadOnly: false)),
            };
        }

        /// <summary>
        /// Values-native world fixture: the grid value's "Children" ordered
        /// list carries a (empty) direct tile layer link plus an object layer
        /// link whose placed object "shop-1" (at 10,20) carries a
        /// TileLayerLink child "shop-floor-link" projecting "floor-local"
        /// (local cell -1,2 -> grid cell 9,22) into "background-layer".
        /// </summary>
        private static ProjectData BuildTileGridProjectData()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var gridClass = new NeoSchemaClass
            {
                id = GridClassId,
                projectId = "project-a",
                name = "Grid",
                schema = new Dictionary<string, string>
                {
                    ["Children"] = "grid-children-member",
                },
            };
            var tileClass = new NeoSchemaClass
            {
                id = TileClassId,
                projectId = "project-a",
                name = "Tile",
                schema = new Dictionary<string, string>(),
            };
            var objectClass = new NeoSchemaClass
            {
                id = ObjectClassId,
                projectId = "project-a",
                name = "Object",
                schema = new Dictionary<string, string>
                {
                    ["Position"] = "object-position-member",
                    // The renderer tells a visibility write apart from every
                    // other member write by schema key, so the fixture carries
                    // NeoObjectBase.Enabled as a real member rather than only
                    // as a property on the test double.
                    ["Enabled"] = "object-enabled-member",
                    ["Children"] = "object-children-member",
                },
            };
            var tileInstanceClass = new NeoSchemaClass
            {
                id = TileInstanceClassId,
                projectId = "project-a",
                name = "Tile Instance",
                schema = new Dictionary<string, string>
                {
                    ["Cell"] = "tile-instance-cell-member",
                    ["Tile"] = "tile-instance-tile-member",
                },
            };
            var tileLayerLinkClass = new NeoSchemaClass
            {
                id = TileLayerLinkClassId,
                projectId = "project-a",
                name = "Tile Layer Link",
                schema = new Dictionary<string, string>
                {
                    ["TileLayer"] = "tile-layer-link-layer-member",
                    ["Tiles"] = "tile-layer-link-tiles-member",
                },
            };
            var objectLayerLinkClass = new NeoSchemaClass
            {
                id = ObjectLayerLinkClassId,
                projectId = "project-a",
                name = "Object Layer Link",
                schema = new Dictionary<string, string>
                {
                    ["ObjectLayer"] = "object-layer-link-layer-member",
                    ["Objects"] = "object-layer-link-objects-member",
                },
            };
            var baseTileClass = new NeoSchemaClass
            {
                id = BaseTileClassId,
                projectId = "project-a",
                name = "Base Tile",
                schema = new Dictionary<string, string>(),
            };
            var subTileClass = new NeoSchemaClass
            {
                id = SubTileClassId,
                projectId = "project-a",
                name = "Sub Tile",
                extendsClassId = BaseTileClassId,
                schema = new Dictionary<string, string>(),
            };
            var otherTileClass = new NeoSchemaClass
            {
                id = OtherTileClassId,
                projectId = "project-a",
                name = "Other Tile",
                schema = new Dictionary<string, string>(),
            };
            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "World Test",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", rootClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                    ["grid-children-member"] = new ListMember
                    {
                        id = "grid-children-member",
                        projectId = "project-a",
                        name = "Children",
                        kind = MemberKind.List,
                        entryMemberId = "grid-child-entry-member",
                    },
                    ["grid-child-entry-member"] = new ClassMember
                    {
                        id = "grid-child-entry-member",
                        projectId = "project-a",
                        name = "Child",
                        kind = MemberKind.Class,
                        classId = TileLayerLinkClassId,
                    },
                    ["object-position-member"] = new Vector3Member
                    {
                        id = "object-position-member",
                        projectId = "project-a",
                        name = "Position",
                        kind = MemberKind.Vector3,
                    },
                    ["object-enabled-member"] = new BoolMember
                    {
                        id = "object-enabled-member",
                        projectId = "project-a",
                        name = "Enabled",
                        kind = MemberKind.Bool,
                        defaultValue = new BoolMemberValueBase { value = true },
                    },
                    ["object-children-member"] = new ListMember
                    {
                        id = "object-children-member",
                        projectId = "project-a",
                        name = "Children",
                        kind = MemberKind.List,
                        entryMemberId = "grid-child-entry-member",
                    },
                    ["tile-instance-cell-member"] = new Vector2IntMember
                    {
                        id = "tile-instance-cell-member",
                        projectId = "project-a",
                        name = "Cell",
                        kind = MemberKind.Vector2Int,
                    },
                    ["tile-instance-tile-member"] = new LookupMember
                    {
                        id = "tile-instance-tile-member",
                        projectId = "project-a",
                        name = "Tile",
                        kind = MemberKind.Lookup,
                        collectionMemberId = "tile-layer-link-tiles-member",
                    },
                    ["tile-layer-link-layer-member"] = new LookupMember
                    {
                        id = "tile-layer-link-layer-member",
                        projectId = "project-a",
                        name = "TileLayer",
                        kind = MemberKind.Lookup,
                        collectionMemberId = "grid-children-member",
                    },
                    ["tile-layer-link-tiles-member"] = new ListMember
                    {
                        id = "tile-layer-link-tiles-member",
                        projectId = "project-a",
                        name = "Tiles",
                        kind = MemberKind.List,
                        entryMemberId = "tile-layer-link-tile-entry-member",
                        Storage = NeoMemberStorage.Save,
                    },
                    ["tile-layer-link-tile-entry-member"] = new ClassMember
                    {
                        id = "tile-layer-link-tile-entry-member",
                        projectId = "project-a",
                        name = "Tile",
                        kind = MemberKind.Class,
                        classId = TileInstanceClassId,
                    },
                    ["object-layer-link-layer-member"] = new LookupMember
                    {
                        id = "object-layer-link-layer-member",
                        projectId = "project-a",
                        name = "ObjectLayer",
                        kind = MemberKind.Lookup,
                        collectionMemberId = "grid-children-member",
                    },
                    ["object-layer-link-objects-member"] = new ListMember
                    {
                        id = "object-layer-link-objects-member",
                        projectId = "project-a",
                        name = "Objects",
                        kind = MemberKind.List,
                        entryMemberId = "object-layer-link-object-entry-member",
                        ListKind = NeoListKind.Unordered,
                    },
                    ["object-layer-link-object-entry-member"] = new ClassMember
                    {
                        id = "object-layer-link-object-entry-member",
                        projectId = "project-a",
                        name = "Object",
                        kind = MemberKind.Class,
                        classId = ObjectClassId,
                    },
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id),
                    ["root-save-value"] = ObjectValue("root-save-value", rootClass.id),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id),
                    ["floor-tile"] = ObjectValue("floor-tile", TileClassId),
                    ["base-tile"] = ObjectValue("base-tile", BaseTileClassId),
                    // A SECOND placement value of the SAME tile class. Smart
                    // tile `This` is definition identity, so these two must see
                    // each other even though the renderer caches one TileBase
                    // per placement value id.
                    ["base-tile-twin"] = ObjectValue("base-tile-twin", BaseTileClassId),
                    ["sub-tile"] = ObjectValue("sub-tile", SubTileClassId),
                    ["other-tile"] = ObjectValue("other-tile", OtherTileClassId),
                    ["shop-object"] = ObjectValue("shop-object", ObjectClassId),
                    ["town-grid"] = new ObjectMemberValue
                    {
                        id = "town-grid",
                        classId = GridClassId,
                        value = new Dictionary<string, string>
                        {
                            ["Children"] = "town-grid-children",
                        },
                    },
                    ["town-grid-children"] = new ArrayMemberValue
                    {
                        id = "town-grid-children",
                        value = new[] { "background-link", "objects-link" },
                    },
                    ["background-link"] = new ObjectMemberValue
                    {
                        id = "background-link",
                        classId = TileLayerLinkClassId,
                        value = new Dictionary<string, string>
                        {
                            ["TileLayer"] = "background-link-layer",
                            ["Tiles"] = "background-link-tiles",
                        },
                    },
                    ["background-link-layer"] = new ArrayMemberValue
                    {
                        id = "background-link-layer",
                        value = new[] { "background-layer" },
                    },
                    ["background-link-tiles"] = new ArrayMemberValue
                    {
                        id = "background-link-tiles",
                        value = System.Array.Empty<string>(),
                    },
                    ["objects-link"] = new ObjectMemberValue
                    {
                        id = "objects-link",
                        classId = ObjectLayerLinkClassId,
                        value = new Dictionary<string, string>
                        {
                            ["ObjectLayer"] = "objects-link-layer",
                            ["Objects"] = "objects-link-objects",
                        },
                    },
                    ["objects-link-layer"] = new ArrayMemberValue
                    {
                        id = "objects-link-layer",
                        value = new[] { "object-layer" },
                    },
                    ["objects-link-objects"] = new ArrayMemberValue
                    {
                        id = "objects-link-objects",
                        value = System.Array.Empty<string>(),
                    },
                    // Membership by join: shop-1 carries the Objects list
                    // value id as its containerId.
                    ["shop-1"] = new ObjectMemberValue
                    {
                        id = "shop-1",
                        classId = ObjectClassId,
                        containerId = "objects-link-objects",
                        value = new Dictionary<string, string>
                        {
                            ["Position"] = "shop-1-position",
                            ["Children"] = "shop-1-children",
                        },
                    },
                    ["shop-1-position"] = new Vector3MemberValue
                    {
                        id = "shop-1-position",
                        value = new NeoVector3Value { x = 10, y = 20, z = 0 },
                    },
                    ["shop-1-children"] = new ArrayMemberValue
                    {
                        id = "shop-1-children",
                        value = new[] { "shop-floor-link" },
                    },
                    ["shop-floor-link"] = new ObjectMemberValue
                    {
                        id = "shop-floor-link",
                        classId = TileLayerLinkClassId,
                        value = new Dictionary<string, string>
                        {
                            ["TileLayer"] = "shop-floor-link-layer",
                            ["Tiles"] = "shop-floor-link-tiles",
                        },
                    },
                    ["shop-floor-link-layer"] = new ArrayMemberValue
                    {
                        id = "shop-floor-link-layer",
                        value = new[] { "background-layer" },
                    },
                    ["shop-floor-link-tiles"] = new ArrayMemberValue
                    {
                        id = "shop-floor-link-tiles",
                        value = new[] { "floor-local" },
                    },
                    ["floor-local"] = new ObjectMemberValue
                    {
                        id = "floor-local",
                        classId = TileInstanceClassId,
                        value = new Dictionary<string, string>
                        {
                            ["Cell"] = "floor-local-cell",
                            ["Tile"] = "floor-local-tile",
                        },
                    },
                    ["floor-local-cell"] = new Vector2MemberValue
                    {
                        id = "floor-local-cell",
                        value = new NeoVector2Value { x = -1, y = 2 },
                    },
                    ["floor-local-tile"] = new ArrayMemberValue
                    {
                        id = "floor-local-tile",
                        value = new[] { "floor-tile" },
                    },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [GridClassId] = gridClass,
                    [TileClassId] = tileClass,
                    [ObjectClassId] = objectClass,
                    [TileInstanceClassId] = tileInstanceClass,
                    [TileLayerLinkClassId] = tileLayerLinkClass,
                    [ObjectLayerLinkClassId] = objectLayerLinkClass,
                    [BaseTileClassId] = baseTileClass,
                    [SubTileClassId] = subTileClass,
                    [OtherTileClassId] = otherTileClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static ClassMember RootMember(
            string id,
            string valueId,
            string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-a",
                name = id,
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = valueId,
                classId = classId,
            };
        }

        private static ObjectMemberValue ObjectValue(string id, string classId)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = new Dictionary<string, string>(),
            };
        }
    }
}
