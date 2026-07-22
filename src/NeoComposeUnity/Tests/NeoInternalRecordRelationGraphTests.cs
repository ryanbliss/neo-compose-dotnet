// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public sealed class NeoInternalRecordRelationGraphTests
    {
        [Test]
        public void Resolve_InheritsSourcesExpandsTargetsAndPreservesLayerOrder()
        {
            ProjectData data = Data(
                Classes(
                    Class("grid-base"),
                    Class("grid-winter", "grid-base"),
                    Class("tile-family", isAbstract: true),
                    Class("tile-a", "tile-family"),
                    Class("tile-b", "tile-family"),
                    Class("layer-a"),
                    Class("layer-b")),
                Relations(
                    Relation(
                        "import-family",
                        InternalRecordRelationKinds.WorldGridTileImport,
                        "grid-base",
                        "tile-family"),
                    Relation(
                        "layer-b",
                        InternalRecordRelationKinds.WorldGridTileLayer,
                        "grid-base",
                        "layer-b",
                        "b0"),
                    Relation(
                        "layer-a",
                        InternalRecordRelationKinds.WorldGridTileLayer,
                        "grid-base",
                        "layer-a",
                        "a0")));
            var graph = new NeoInternalRecordRelationGraph(data);

            CollectionAssert.AreEqual(
                new[] { "tile-a", "tile-b" },
                graph.ResolveTargetIds(
                    InternalRecordRelationKinds.WorldGridTileImport,
                    "grid-winter"));
            CollectionAssert.AreEqual(
                new[] { "layer-a", "layer-b" },
                graph.ResolveTargetIds(
                    InternalRecordRelationKinds.WorldGridTileLayer,
                    "grid-winter"));
        }

        [Test]
        public void Resolve_NearestSingletonOverridesAncestorWithoutCopiedRows()
        {
            ProjectData data = Data(
                Classes(
                    Class("tile-base"),
                    Class("tile-child", "tile-base"),
                    Class("layer-base"),
                    Class("layer-child")),
                Relations(
                    Relation(
                        "default-base",
                        InternalRecordRelationKinds.WorldTileDefaultLayer,
                        "tile-base",
                        "layer-base"),
                    Relation(
                        "default-child",
                        InternalRecordRelationKinds.WorldTileDefaultLayer,
                        "tile-child",
                        "layer-child")));
            var graph = new NeoInternalRecordRelationGraph(data);

            CollectionAssert.AreEqual(
                new[] { "layer-child" },
                graph.ResolveTargetIds(
                    InternalRecordRelationKinds.WorldTileDefaultLayer,
                    "tile-child"));
            CollectionAssert.AreEqual(
                new[] { "layer-base" },
                graph.ResolveTargetIds(
                    InternalRecordRelationKinds.WorldTileDefaultLayer,
                    "tile-base"));
        }

        [Test]
        public void Resolve_NearestSingletonRejectsDifferentTargetsAtSameDepth()
        {
            ProjectData data = Data(
                Classes(
                    Class("link"),
                    Class("layer-a"),
                    Class("layer-b")),
                Relations(
                    Relation(
                        "target-a",
                        InternalRecordRelationKinds.WorldTileLayerLinkTarget,
                        "link",
                        "layer-a"),
                    Relation(
                        "target-b",
                        InternalRecordRelationKinds.WorldTileLayerLinkTarget,
                        "link",
                        "layer-b")));
            var graph = new NeoInternalRecordRelationGraph(data);

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                graph.ResolveTargetIds(
                    InternalRecordRelationKinds.WorldTileLayerLinkTarget,
                    "link"));

            StringAssert.Contains("ambiguous nearest declarations", error!.Message);
            StringAssert.Contains("targets 'layer-a' and 'layer-b'", error.Message);
        }

        [Test]
        public void Resolve_ChildLayerRedeclarationOverridesInheritedOrderAndKeepsProvenance()
        {
            ProjectData data = Data(
                Classes(
                    Class("grid-base"),
                    Class("grid-child", "grid-base"),
                    Class("layer-a"),
                    Class("layer-b")),
                Relations(
                    Relation(
                        "base-a",
                        InternalRecordRelationKinds.WorldGridTileLayer,
                        "grid-base",
                        "layer-a",
                        "a0"),
                    Relation(
                        "base-b",
                        InternalRecordRelationKinds.WorldGridTileLayer,
                        "grid-base",
                        "layer-b",
                        "b0"),
                    Relation(
                        "child-b",
                        InternalRecordRelationKinds.WorldGridTileLayer,
                        "grid-child",
                        "layer-b",
                        "00")));
            var graph = new NeoInternalRecordRelationGraph(data);

            var effective = graph.Resolve(
                InternalRecordRelationKinds.WorldGridTileLayer,
                "grid-child");

            CollectionAssert.AreEqual(
                new[] { "layer-b", "layer-a" },
                effective.Select(relation => relation.TargetRecordId).ToArray());
            CollectionAssert.AreEqual(
                new[] { "base-b", "child-b" },
                effective[0].RelationIds);
            Assert.AreEqual("grid-child", effective[0].DeclaredSourceRecordId);
            Assert.AreEqual(0, effective[0].SourceAncestryDepth);
        }

        [Test]
        public void ResolveExactTargetId_ResolvesValueToClassWithoutInheritance()
        {
            var relation = Relation(
                "neighbor-tile",
                InternalRecordRelationKinds.WorldSmartTileNeighborTile,
                "neighbor-value",
                "tile-family");
            relation.sourceRecordKind = "value";
            ProjectData data = Data(
                Classes(Class("tile-family", isAbstract: true)),
                Relations(relation));
            var graph = new NeoInternalRecordRelationGraph(data);

            Assert.AreEqual(
                "tile-family",
                graph.ResolveExactTargetId(
                    InternalRecordRelationKinds.WorldSmartTileNeighborTile,
                    "value",
                    "neighbor-value",
                    "class"));
            Assert.IsNull(graph.ResolveExactTargetId(
                InternalRecordRelationKinds.WorldSmartTileNeighborTile,
                "value",
                "other-neighbor",
                "class"));
        }

        private static ProjectData Data(
            Dictionary<string, NeoSchemaClass> classes,
            Dictionary<string, InternalRecordRelation> relations)
        {
            return new ProjectData
            {
                classes = classes,
                internalRecordRelations = relations,
            };
        }

        private static Dictionary<string, NeoSchemaClass> Classes(
            params NeoSchemaClass[] classes)
        {
            var result = new Dictionary<string, NeoSchemaClass>();
            foreach (NeoSchemaClass schemaClass in classes)
            {
                result[schemaClass.id] = schemaClass;
            }
            return result;
        }

        private static Dictionary<string, InternalRecordRelation> Relations(
            params InternalRecordRelation[] relations)
        {
            var result = new Dictionary<string, InternalRecordRelation>();
            foreach (InternalRecordRelation relation in relations)
            {
                result[relation.id] = relation;
            }
            return result;
        }

        private static NeoSchemaClass Class(
            string id,
            string? extendsClassId = null,
            bool isAbstract = false)
        {
            return new NeoSchemaClass
            {
                id = id,
                projectId = "project",
                name = id,
                schema = new Dictionary<string, string>(),
                extendsClassId = extendsClassId,
                isAbstract = isAbstract,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static InternalRecordRelation Relation(
            string id,
            string relationKind,
            string sourceClassId,
            string targetClassId,
            string? orderKey = null)
        {
            return new InternalRecordRelation
            {
                id = id,
                projectId = "project",
                relationKind = relationKind,
                sourceRecordKind = "class",
                sourceRecordId = sourceClassId,
                targetRecordKind = "class",
                targetRecordId = targetClassId,
                orderKey = orderKey,
                createdAt = "x",
                updatedAt = "x",
            };
        }
    }
}
