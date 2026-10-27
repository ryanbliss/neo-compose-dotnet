// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    public partial class NeoTileGridRendererTests
    {
        [Test]
        public void Render_SizeWritesResizeExistingSpriteAndPreserveItsOrigin()
        {
            var data = BuildPlacementAnimationProjectData();
            data.classes[ObjectClassId].schema["Size"] = "live-size";
            data.members["live-size"] = new Vector3Member
            {
                id = "live-size",
                kind = MemberKind.Vector3,
                name = "Size",
                DeclaredStorage = NeoMemberStorage.Session,
                defaultValue = new Vector3MemberValueBase { value = new NeoVector3Value { x = 1, y = 1 } },
            };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var node = ((NeoGeneratedClassValue)SpawnAnimationTestObject(client).Info).BackingNode;
            var art = CreateTestSprite("resized");
            var value = new SizedSprite(client, node, art);
            var host = new GameObject("Live sprite size");
            try
            {
                var renderer = host.AddComponent<NeoTileGridRenderer>();
                renderer.CellSize = .5f;
                renderer.AddSpriteBoundsColliders = true;
                renderer.Render(NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[] { ObjectLayerWithSingleInstance(value, "Default", 12) });
                var drawn = host.GetComponentInChildren<SpriteRenderer>();
                foreach (var size in new[] { new Vector3(4, 5, 0), Vector3.one, new Vector3(2, 3, 0) })
                {
                    NeoGeneratedTypesSupport.SetValue(NeoGeneratedTypesSupport.AsWritable(node), "Size", NeoValueWritePayload.FromValue(size));
                    Assert.AreSame(drawn, host.GetComponentInChildren<SpriteRenderer>(), "Resizing preserves the object and its controllers.");
                    Assert.That(drawn.bounds.size.x, Is.EqualTo(size.x * .5f).Within(.001f));
                    Assert.That(drawn.bounds.size.y, Is.EqualTo(size.y * .5f).Within(.001f));
                    Assert.That(drawn.transform.localPosition, Is.EqualTo(new Vector3(size.x * .25f, size.y * .25f, 0)));
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
                DestroyTestSprite(art);
            }
        }

        private sealed class SizedSprite : NeoGeneratedWorldObjectValue, INeoSpriteObjectValue
        {
            public SizedSprite(NeoClient client, NeoMemberClass node, Sprite sprite) : base(client, node, ObjectClassId) => Sprite = sprite;
            public string Name => "Sized sprite";
            public NeoReadOnlyVector3 Position => new(BackingNode.Get<NeoMemberVector3>("Position"));
            public NeoReadOnlyVector3 Size => new(BackingNode.Get<NeoMemberVector3>("Size"));
            public bool Enabled => true;
            public Sprite Sprite
            {
                get;
            }
            public bool FlipX => false;
            public bool FlipY => false;
            public string MaskInteraction => NeoSpriteMaskInteraction.None.OptionId();
            public int? SortingOrder => null;
        }
    }
}
