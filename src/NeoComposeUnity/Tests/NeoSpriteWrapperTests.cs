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
    /// <summary>
    /// The NeoReadOnlySprite / NeoSprite wrapper pair (P42 §4.1). Sprite was
    /// the one structured leaf with no wrapper — its generated property
    /// projected straight to <c>UnityEngine.Sprite</c> — so both halves are
    /// new.
    ///
    /// <para>Asserted here: the addressable fields <c>fileId</c> and
    /// <c>sliceIndex</c> read off bound and detached instances; a bound
    /// instance writes a field change through as a read-modify-write of the
    /// whole leaf; a detached instance mutates locally; a field write is
    /// rejected when the bound node is not writable or the owning generated
    /// value is read-only (decision D5); and equality is value-based over the
    /// two fields.</para>
    ///
    /// <para><b>Not</b> asserted here: anything that needs a synchronized
    /// <see cref="NeoAssetDatabase"/>. These are EditMode tests with no
    /// database in Resources, so <c>Resolve()</c> can only be exercised for
    /// its null and required-throw paths. Slice-range behaviour lives in
    /// <c>NeoAssetResolver</c> and is unchanged by P42.</para>
    /// </summary>
    public class NeoSpriteWrapperTests
    {
        // ------------------------------------------------------------------
        // Reads.
        // ------------------------------------------------------------------

        [Test]
        public void BoundWrapper_ReadsAddressableFields()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var portrait = new NeoSprite(client.save.Get<NeoMemberSpriteWritable>("Portrait"));

            Assert.AreEqual("file-a", portrait.fileId);
            Assert.AreEqual(3, portrait.sliceIndex);
            Assert.AreEqual("file-a", portrait.Value!.fileId);
            Assert.AreEqual(3, portrait.Value.sliceIndex);
        }

        [Test]
        public void BoundWrapper_ValueIsACopyOfTheStoredRow()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var portrait = new NeoSprite(client.save.Get<NeoMemberSpriteWritable>("Portrait"));

            var snapshot = portrait.Value!;
            snapshot.sliceIndex = 99;

            // Mutating the returned DTO must not reach the stored row.
            Assert.AreEqual(3, portrait.sliceIndex);
        }

        [Test]
        public void DetachedWrapper_ReadsAddressableFields()
        {
            var detached = new NeoSprite("file-z", 7);

            Assert.AreEqual("file-z", detached.fileId);
            Assert.AreEqual(7, detached.sliceIndex);
        }

        [Test]
        public void Wrapper_ReadingFieldsWithoutAValueThrows()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var badge = new NeoSprite(client.save.Get<NeoMemberSpriteWritable>("Badge"));

            Assert.IsNull(badge.Value);
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                _ = badge.sliceIndex);
            StringAssert.Contains("has no value", error!.Message);
        }

        [Test]
        public void Resolve_OptionalMemberWithoutAnAssetDatabaseIsNull()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var badge = new NeoSprite(client.save.Get<NeoMemberSpriteWritable>("Badge"));

            Assert.IsNull(badge.Resolve());
        }

        [Test]
        public void Resolve_RequiredMemberWithoutASynchronizedAssetThrows()
        {
            // P42 §4.2 moves this throw off the generated getter and onto
            // Resolve() / the implicit NeoSprite → Sprite conversion, so that
            // reading obj.Sprite.sliceIndex on an unsynchronized asset does
            // not throw.
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var portrait = new NeoSprite(client.save.Get<NeoMemberSpriteWritable>("Portrait"));

            Assert.AreEqual(3, portrait.sliceIndex);
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                portrait.Resolve());
            StringAssert.Contains("has no synchronized asset", error!.Message);
        }

        [Test]
        public void DetachedFromSprite_ResolvesBackToTheSameSprite()
        {
            var sprite = MakeSprite();
            try
            {
                NeoSprite wrapper = sprite;

                Assert.AreSame(sprite, wrapper.Resolve());
                // Untracked by any asset database, so it has no addressable
                // value — reported as "no value", not as an exception.
                Assert.IsNull(wrapper.Value);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        // ------------------------------------------------------------------
        // Write-through fields.
        // ------------------------------------------------------------------

        [Test]
        public void BoundWrapper_SliceIndexSetterWritesThrough()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var portrait = new NeoSprite(client.save.Get<NeoMemberSpriteWritable>("Portrait"));

            portrait.sliceIndex = 1;

            var reread = new NeoSprite(client.save.Get<NeoMemberSpriteWritable>("Portrait"));
            Assert.AreEqual(1, reread.sliceIndex);
            // fileId survived the read-modify-write.
            Assert.AreEqual("file-a", reread.fileId);
        }

        [Test]
        public void BoundWrapper_FileIdSetterWritesThrough()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var portrait = new NeoSprite(client.save.Get<NeoMemberSpriteWritable>("Portrait"));

            portrait.fileId = "file-b";

            var reread = new NeoSprite(client.save.Get<NeoMemberSpriteWritable>("Portrait"));
            Assert.AreEqual("file-b", reread.fileId);
            Assert.AreEqual(3, reread.sliceIndex);
        }

        [Test]
        public void DetachedWrapper_FieldSetterStaysLocal()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var detached = new NeoSprite("file-a", 3);

            detached.sliceIndex = 5;

            Assert.AreEqual(5, detached.sliceIndex);
            var portrait = new NeoSprite(client.save.Get<NeoMemberSpriteWritable>("Portrait"));
            Assert.AreEqual(3, portrait.sliceIndex);
        }

        [Test]
        public void FieldSetter_OnNonWritableNodeThrows()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var wrapper = new NeoSprite(new NeoMemberSprite(
                client,
                "portrait-member",
                "portrait-value",
                NeoValueOwnership.Save));

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                wrapper.sliceIndex = 1);
            StringAssert.Contains("read-only", error!.Message);
            Assert.AreEqual(3, wrapper.sliceIndex);
        }

        [Test]
        public void FieldSetter_OnReadOnlyOwnerThrows()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var owner = new NeoReadOnlyClassValueDouble(client, client.save, "save-root-class");
            var wrapper = new NeoSprite(
                client.save.Get<NeoMemberSpriteWritable>("Portrait"),
                owner);

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                wrapper.fileId = "file-b");
            StringAssert.Contains("read-only", error!.Message);
            Assert.AreEqual("file-a", wrapper.fileId);
        }

        [Test]
        public void FieldSetter_WithoutACurrentValueThrows()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var badge = new NeoSprite(client.save.Get<NeoMemberSpriteWritable>("Badge"));

            // A field write is a read-modify-write, so there must be something
            // to modify. Assign a whole value first.
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                badge.sliceIndex = 2);
            StringAssert.Contains("has no value", error!.Message);
        }

        // ------------------------------------------------------------------
        // Equality.
        // ------------------------------------------------------------------

        [Test]
        public void Equality_ComparesAddressableFields()
        {
            var first = new NeoSprite("file-a", 3);
            var second = new NeoReadOnlySprite("file-a", 3);
            var differentSlice = new NeoSprite("file-a", 4);
            var differentFile = new NeoSprite("file-b", 3);

            Assert.IsFalse(ReferenceEquals(first, second));
            Assert.IsTrue(first == second);
            Assert.IsFalse(first != second);
            Assert.IsTrue(first != differentSlice);
            Assert.IsTrue(first != differentFile);

            Assert.IsTrue(first.Equals(second));
            Assert.IsFalse(first.Equals(differentSlice));
            Assert.IsFalse(first.Equals(null));
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        }

        [Test]
        public void Equality_IsNullSafeOnBothSides()
        {
            NeoReadOnlySprite? left = null;
            NeoReadOnlySprite? right = null;
            var detached = new NeoSprite("file-a", 0);

            Assert.IsTrue(left == right);
            Assert.IsFalse(left != right);
            Assert.IsFalse(left == detached);
            Assert.IsTrue(detached != right);
        }

        [Test]
        public void Equality_BoundWrappersCompareByValue()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var portrait = new NeoSprite(client.save.Get<NeoMemberSpriteWritable>("Portrait"));
            var icon = new NeoSprite(client.save.Get<NeoMemberSpriteWritable>("Icon"));

            Assert.IsTrue(portrait != icon);

            icon.sliceIndex = 3;
            icon.fileId = "file-a";

            Assert.IsTrue(portrait == icon);
        }

        // ------------------------------------------------------------------
        // Fixture.
        // ------------------------------------------------------------------

        private static Sprite MakeSprite()
        {
            var texture = new Texture2D(4, 4);
            return Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
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
                    ["Icon"] = "icon-member",
                    ["Badge"] = "badge-member",
                },
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Sprite Wrappers",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", saveRootClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                    ["portrait-member"] = SpriteMemberDefinition("portrait-member", "Portrait", required: true),
                    ["icon-member"] = SpriteMemberDefinition("icon-member", "Icon", required: true),
                    ["badge-member"] = SpriteMemberDefinition("badge-member", "Badge", required: false),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id, new()),
                    ["root-save-value"] = ObjectValue(
                        "root-save-value",
                        saveRootClass.id,
                        new Dictionary<string, string>
                        {
                            ["Portrait"] = "portrait-value",
                            ["Icon"] = "icon-value",
                        }),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id, new()),
                    ["portrait-value"] = SpriteValueRow("portrait-value", "file-a", 3),
                    ["icon-value"] = SpriteValueRow("icon-value", "file-b", 0),
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

        private static SpriteMember SpriteMemberDefinition(string id, string name, bool required)
        {
            return new SpriteMember
            {
                id = id,
                projectId = "project-a",
                name = name,
                kind = MemberKind.Sprite,
                required = required,
            };
        }

        private static SpriteMemberValue SpriteValueRow(string id, string fileId, int sliceIndex)
        {
            return new SpriteMemberValue
            {
                id = id,
                value = new SpriteValue { fileId = fileId, sliceIndex = sliceIndex },
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
