// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace NeoCompose.Tests
{
    /// <summary>
    /// <see cref="NeoTileAssetFactory.ResolveSprite"/> discovers a tile's
    /// sprite by reflecting over the generated value's properties, so it is
    /// coupled to whatever C# type codegen projects for a Sprite member.
    ///
    /// <para>P42 §4.1 retyped generated sprite <em>properties</em> from
    /// <c>UnityEngine.Sprite</c> to <see cref="NeoSprite"/>. The bridge
    /// between the two is a user-defined implicit conversion operator, which
    /// <c>Type.IsAssignableFrom</c> cannot see — so a scan filtering on
    /// <c>typeof(Sprite).IsAssignableFrom(...)</c> matched nothing at all on
    /// every generated tile, silently. No tile asset was built and the
    /// tilemap came back empty. These tests pin the projection the scan has
    /// to accept.</para>
    ///
    /// <para>Both shapes are live at once and both are asserted: sprites in
    /// non-property positions (list and dictionary entries, generic slots,
    /// static members, constructor parameters) are still emitted as native
    /// <c>UnityEngine.Sprite</c> per the P42 codegen carve-outs, so the
    /// native arm is not legacy.</para>
    /// </summary>
    public class NeoTileAssetFactorySpriteProjectionTests
    {
        [Test]
        public void ResolveSprite_ReadsAWrapperTypedSpriteProperty()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var sprite = MakeSprite();
            try
            {
                var tile = new WrapperSpriteTile(client) { Sprite = new NeoSprite(sprite) };

                Assert.AreSame(sprite, NeoTileAssetFactory.ResolveSprite(tile));
            }
            finally
            {
                DestroySprite(sprite);
            }
        }

        [Test]
        public void ResolveSprite_ReadsAReadOnlyWrapperTypedSpriteProperty()
        {
            // The read-only family projects NeoReadOnlySprite, which is the
            // base of NeoSprite and equally not assignable to Sprite.
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var sprite = MakeSprite();
            try
            {
                var tile = new ReadOnlyWrapperSpriteTile(client)
                {
                    Sprite = new NeoReadOnlySprite(sprite),
                };

                Assert.AreSame(sprite, NeoTileAssetFactory.ResolveSprite(tile));
            }
            finally
            {
                DestroySprite(sprite);
            }
        }

        [Test]
        public void ResolveSprite_StillReadsANativeSpriteProperty()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var sprite = MakeSprite();
            try
            {
                var tile = new NativeSpriteTile(client) { Sprite = sprite };

                Assert.AreSame(sprite, NeoTileAssetFactory.ResolveSprite(tile));
            }
            finally
            {
                DestroySprite(sprite);
            }
        }

        [Test]
        public void ResolveSprite_FallsBackToASuffixedWrapperProperty()
        {
            // Exercises the "*Sprite"-suffix scan rather than the exact
            // "Sprite"/"Image" lookup, so the widened type filter is proven
            // on the reflection loops too.
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var sprite = MakeSprite();
            try
            {
                var tile = new SuffixedWrapperSpriteTile(client)
                {
                    GroundSprite = new NeoSprite(sprite),
                };

                Assert.AreSame(sprite, NeoTileAssetFactory.ResolveSprite(tile));
            }
            finally
            {
                DestroySprite(sprite);
            }
        }

        [Test]
        public void ResolveSprite_RequiredWrapperWithoutASynchronizedAssetIsNullNotAThrow()
        {
            // NeoReadOnlySprite.Resolve() throws for a required member with no
            // synchronized asset (P42 §4.2). This scan is best-effort — it
            // already tolerates properties it cannot read — so the wrapper
            // path must report "nothing here" and let the caller fall through
            // to the next candidate, exactly as a null native Sprite does.
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var tile = new BoundRequiredSpriteTile(client);

            // The member is required, has a stored row, and no asset database
            // is present — so Resolve() itself throws.
            Assert.Throws<System.InvalidOperationException>(() => tile.Sprite.Resolve());

            Assert.IsNull(NeoTileAssetFactory.ResolveSprite(tile));
        }

        [Test]
        public void CreateTransientTileBase_BuildsATileFromAWrapperTypedProperty()
        {
            // The end-to-end shape the renderer depends on: no tile asset was
            // built at all while the scan was blind to NeoSprite, which is
            // what left GetTile null on the tilemap.
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var sprite = MakeSprite();
            TileBase? tileBase = null;
            try
            {
                var tile = new WrapperSpriteTile(client) { Sprite = new NeoSprite(sprite) };

                tileBase = NeoTileAssetFactory.CreateTransientTileBase(tile);

                Assert.IsNotNull(tileBase);
                Assert.AreSame(sprite, ((Tile)tileBase!).sprite);
            }
            finally
            {
                if (tileBase != null) Object.DestroyImmediate(tileBase);
                DestroySprite(sprite);
            }
        }

        // ------------------------------------------------------------------
        // Generated-value stand-ins. Real generated classes are codegen
        // output and far too heavy to build here; the factory only ever sees
        // them through reflection over their public properties, which is
        // exactly what these reproduce.
        // ------------------------------------------------------------------

        private sealed class WrapperSpriteTile : NeoGeneratedClassValue
        {
            public WrapperSpriteTile(NeoClient client)
                : base(client, client.save, TileClassId) { }

            public NeoSprite? Sprite { get; set; }
        }

        private sealed class ReadOnlyWrapperSpriteTile : NeoGeneratedClassValue
        {
            public ReadOnlyWrapperSpriteTile(NeoClient client)
                : base(client, client.save, TileClassId) { }

            public NeoReadOnlySprite? Sprite { get; set; }
        }

        private sealed class NativeSpriteTile : NeoGeneratedClassValue
        {
            public NativeSpriteTile(NeoClient client)
                : base(client, client.save, TileClassId) { }

            public Sprite? Sprite { get; set; }
        }

        private sealed class SuffixedWrapperSpriteTile : NeoGeneratedClassValue
        {
            public SuffixedWrapperSpriteTile(NeoClient client)
                : base(client, client.save, TileClassId) { }

            public NeoSprite? GroundSprite { get; set; }
        }

        private sealed class BoundRequiredSpriteTile : NeoGeneratedClassValue
        {
            public BoundRequiredSpriteTile(NeoClient client)
                : base(client, client.save, TileClassId)
            {
                Sprite = new NeoSprite(
                    client.save.Get<NeoMemberSpriteWritable>("Portrait"));
            }

            public NeoSprite Sprite { get; }
        }

        // ------------------------------------------------------------------
        // Fixture.
        // ------------------------------------------------------------------

        private const string TileClassId = "tile-class";

        private static Sprite MakeSprite()
        {
            var texture = new Texture2D(4, 4);
            return Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
        }

        private static void DestroySprite(Sprite sprite)
        {
            var texture = sprite.texture;
            Object.DestroyImmediate(sprite);
            if (texture != null) Object.DestroyImmediate(texture);
        }

        private static ProjectData BuildProjectData()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var saveRootClass = new NeoSchemaClass
            {
                id = "save-root-class",
                projectId = "project-a",
                name = "Save Root",
                schema = new Dictionary<string, string>
                {
                    ["Portrait"] = "portrait-member",
                },
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Tile Sprite Projection",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", saveRootClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                    ["portrait-member"] = new SpriteMember
                    {
                        id = "portrait-member",
                        projectId = "project-a",
                        name = "Portrait",
                        kind = MemberKind.Sprite,
                        required = true,
                    },
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id, new()),
                    ["root-save-value"] = ObjectValue(
                        "root-save-value",
                        saveRootClass.id,
                        new Dictionary<string, string> { ["Portrait"] = "portrait-value" }),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id, new()),
                    ["portrait-value"] = new SpriteMemberValue
                    {
                        id = "portrait-value",
                        value = new SpriteValue { fileId = "file-a", sliceIndex = 3 },
                    },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [saveRootClass.id] = saveRootClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static ClassMember RootMember(string id, string valueId, string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-a",
                name = id,
                kind = MemberKind.Class,
                required = true,
                valueId = valueId,
                classId = classId,
            };
        }

        private static ObjectMemberValue ObjectValue(
            string id,
            string classId,
            Dictionary<string, string> record)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = record,
            };
        }
    }
}
