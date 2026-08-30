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
    /// The vector wrapper contract, as P42 §4.1 leaves it.
    ///
    /// <para>This fixture was originally written to lock the vectors to the
    /// assignment convention of specs/color-member.md §6 decisions 5–6:
    /// <em>no</em> mutation API anywhere in the wrapper family, so read-only
    /// misuse could only ever be a compile error. <b>P42 deliberately
    /// overturns that decision</b> — the writable wrappers now carry
    /// write-through component setters — so the tests that encoded the old
    /// rule are rewritten here rather than deleted, and the SDK changelog
    /// records the reversal.</para>
    ///
    /// <para>What still holds, and is still asserted below: whole-value
    /// assignment goes through the wrapper-typed SetVector* funnels and
    /// <b>copies</b> (assigning a bound wrapper never creates a live link),
    /// the *OrClear helpers clear optional leaves, required assignment has its
    /// own null guard, reads validate integrality for int vectors, and
    /// equality is value-based.</para>
    ///
    /// <para>What is new: a wrapper bound to a member node writes a component
    /// change straight through as a read-modify-write of the whole leaf; a
    /// detached wrapper mutates locally only; and a component write is
    /// rejected at runtime when the bound node is not writable or when the
    /// owning generated value is read-only (decision D5).</para>
    /// </summary>
    public class NeoVectorWrapperRetrofitTests
    {
        // ------------------------------------------------------------------
        // Vector2.
        // ------------------------------------------------------------------

        [Test]
        public void SetVector2_WritesNativeAndDetachedWrapper()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());

            NeoGeneratedTypesSupport.SetVector2(client.save, "Position", new Vector2(9f, 8f));
            var position = new NeoVector2(client.save.Get<NeoMemberVector2Writable>("Position"));
            Assert.AreEqual(new Vector2(9f, 8f), position.Value);

            NeoGeneratedTypesSupport.SetVector2(client.save, "Position", (NeoVector2)new Vector2(7f, 6f));
            Assert.AreEqual(new Vector2(7f, 6f), position.Value);
        }

        // Assignment still copies, even now that a *component* write on a
        // bound wrapper writes through: passing a bound wrapper to a
        // SetVector* funnel transfers its value, it does not link the two
        // leaves. P42 changes the field-mutation rule, not this one.
        [Test]
        public void SetVector2_BoundWrapperCopiesValueWithoutLinking()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var offset = new NeoVector2(client.save.Get<NeoMemberVector2Writable>("Offset"));
            var offsetBefore = offset.Value;

            NeoGeneratedTypesSupport.SetVector2(client.save, "Position", offset);
            var position = new NeoVector2(client.save.Get<NeoMemberVector2Writable>("Position"));
            Assert.AreEqual(offsetBefore, position.Value);

            // Changing the source leaf afterwards must not affect the target,
            // whether the change arrives as a whole value...
            NeoGeneratedTypesSupport.SetVector2(client.save, "Offset", new Vector2(40f, 41f));
            Assert.AreEqual(new Vector2(40f, 41f), offset.Value);
            Assert.AreEqual(offsetBefore, position.Value);

            // ...or as a write-through component write.
            offset.y = 42f;
            Assert.AreEqual(new Vector2(40f, 42f), offset.Value);
            Assert.AreEqual(offsetBefore, position.Value);
        }

        [Test]
        public void SetVector2OrClear_NullClearsOptionalValue()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            Assert.IsNotNull(client.save.Get<NeoMemberVector2Writable>("Offset").value);

            NeoGeneratedTypesSupport.SetVector2OrClear(client.save, "Offset", null);

            Assert.IsNull(client.save.Get<NeoMemberVector2Writable>("Offset").value);
        }

        [Test]
        public void SetVector2_NullWrapperToRequiredThrowsArgumentNull()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());

            var error = Assert.Throws<System.ArgumentNullException>(() =>
                NeoGeneratedTypesSupport.SetVector2(
                    client.save,
                    "Position",
                    (NeoReadOnlyVector2)null!));
            StringAssert.Contains("Position", error!.Message);
        }

        [Test]
        public void Vector2Equality_WrappersCompareByValue()
        {
            var first = new NeoVector2(1.5f, 2.5f);
            var second = new NeoReadOnlyVector2(1.5f, 2.5f);
            var different = new NeoVector2(3f, 4f);

            Assert.IsFalse(ReferenceEquals(first, second));
            Assert.IsTrue(first == second);
            Assert.IsFalse(first != second);
            Assert.IsFalse(first == different);
            Assert.IsTrue(first != different);

            Assert.IsTrue(first == new Vector2(1.5f, 2.5f));
            Assert.IsTrue(new Vector2(1.5f, 2.5f) == first);
            Assert.IsFalse(first != new Vector2(1.5f, 2.5f));
            Assert.IsTrue(first != new Vector2(3f, 4f));

            Assert.IsTrue(first.Equals(second));
            Assert.IsTrue(first.Equals(new Vector2(1.5f, 2.5f)));
            Assert.IsFalse(first.Equals(null));
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        }

        [Test]
        public void Vector2Equality_IsNullSafeOnBothSides()
        {
            NeoReadOnlyVector2? left = null;
            NeoReadOnlyVector2? right = null;
            var detached = new NeoVector2(1f, 2f);

            Assert.IsTrue(left == right);
            Assert.IsFalse(left != right);
            Assert.IsFalse(left == detached);
            Assert.IsTrue(left != detached);
            Assert.IsFalse(detached == right);
            Assert.IsTrue(detached != right);
            Assert.IsFalse(right == new Vector2(1f, 2f));
            Assert.IsTrue(right != new Vector2(1f, 2f));
            Assert.IsFalse(new Vector2(1f, 2f) == left);
            Assert.IsTrue(new Vector2(1f, 2f) != left);
        }

        [Test]
        public void Vector2Equality_BoundWrappersCompareByValue()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            NeoGeneratedTypesSupport.SetVector2(client.save, "Position", new Vector2(5f, 5f));
            NeoGeneratedTypesSupport.SetVector2(client.save, "Offset", new Vector2(5f, 5f));

            var position = new NeoVector2(client.save.Get<NeoMemberVector2Writable>("Position"));
            var offset = new NeoVector2(client.save.Get<NeoMemberVector2Writable>("Offset"));

            Assert.IsTrue(position == offset);

            NeoGeneratedTypesSupport.SetVector2(client.save, "Offset", new Vector2(6f, 6f));
            Assert.IsTrue(position != offset);
        }

        // ------------------------------------------------------------------
        // Vector2Int.
        // ------------------------------------------------------------------

        [Test]
        public void SetVector2Int_WritesNativeAndDetachedWrapper()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());

            NeoGeneratedTypesSupport.SetVector2Int(client.save, "Cell", new Vector2Int(9, 8));
            var cell = new NeoVector2Int(client.save.Get<NeoMemberVector2IntWritable>("Cell"));
            Assert.AreEqual(new Vector2Int(9, 8), cell.Value);

            NeoGeneratedTypesSupport.SetVector2Int(client.save, "Cell", (NeoVector2Int)new Vector2Int(7, 6));
            Assert.AreEqual(new Vector2Int(7, 6), cell.Value);
        }

        [Test]
        public void SetVector2Int_BoundWrapperCopiesValueWithoutLinking()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var anchor = new NeoVector2Int(client.save.Get<NeoMemberVector2IntWritable>("Anchor"));
            var anchorBefore = anchor.Value;

            NeoGeneratedTypesSupport.SetVector2Int(client.save, "Cell", anchor);
            var cell = new NeoVector2Int(client.save.Get<NeoMemberVector2IntWritable>("Cell"));
            Assert.AreEqual(anchorBefore, cell.Value);

            NeoGeneratedTypesSupport.SetVector2Int(client.save, "Anchor", new Vector2Int(40, 41));
            Assert.AreEqual(new Vector2Int(40, 41), anchor.Value);
            Assert.AreEqual(anchorBefore, cell.Value);
        }

        [Test]
        public void SetVector2IntOrClear_NullClearsOptionalValue()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            Assert.IsNotNull(client.save.Get<NeoMemberVector2IntWritable>("Anchor").value);

            NeoGeneratedTypesSupport.SetVector2IntOrClear(client.save, "Anchor", null);

            Assert.IsNull(client.save.Get<NeoMemberVector2IntWritable>("Anchor").value);
        }

        [Test]
        public void SetVector2Int_NullWrapperToRequiredThrowsArgumentNull()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());

            var error = Assert.Throws<System.ArgumentNullException>(() =>
                NeoGeneratedTypesSupport.SetVector2Int(
                    client.save,
                    "Cell",
                    (NeoReadOnlyVector2Int)null!));
            StringAssert.Contains("Cell", error!.Message);
        }

        [Test]
        public void Vector2Int_ReadValidatesIntegerComponents()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var fractional = new NeoVector2Int(
                client.save.Get<NeoMemberVector2IntWritable>("Fractional"));

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                _ = fractional.Value);
            StringAssert.Contains("must be an integer", error!.Message);
        }

        [Test]
        public void Vector2IntEquality_WrappersCompareByValue()
        {
            var first = new NeoVector2Int(1, 2);
            var second = new NeoReadOnlyVector2Int(1, 2);
            var different = new NeoVector2Int(3, 4);

            Assert.IsFalse(ReferenceEquals(first, second));
            Assert.IsTrue(first == second);
            Assert.IsFalse(first != second);
            Assert.IsFalse(first == different);
            Assert.IsTrue(first != different);

            Assert.IsTrue(first == new Vector2Int(1, 2));
            Assert.IsTrue(new Vector2Int(1, 2) == first);
            Assert.IsFalse(first != new Vector2Int(1, 2));
            Assert.IsTrue(first != new Vector2Int(3, 4));

            Assert.IsTrue(first.Equals(second));
            Assert.IsTrue(first.Equals(new Vector2Int(1, 2)));
            Assert.IsFalse(first.Equals(null));
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        }

        [Test]
        public void Vector2IntEquality_IsNullSafeOnBothSides()
        {
            NeoReadOnlyVector2Int? left = null;
            NeoReadOnlyVector2Int? right = null;
            var detached = new NeoVector2Int(1, 2);

            Assert.IsTrue(left == right);
            Assert.IsFalse(left != right);
            Assert.IsFalse(left == detached);
            Assert.IsTrue(left != detached);
            Assert.IsFalse(detached == right);
            Assert.IsTrue(detached != right);
            Assert.IsFalse(right == new Vector2Int(1, 2));
            Assert.IsTrue(right != new Vector2Int(1, 2));
            Assert.IsFalse(new Vector2Int(1, 2) == left);
            Assert.IsTrue(new Vector2Int(1, 2) != left);
        }

        // ------------------------------------------------------------------
        // P42 §4.1 — write-through component setters. Bound writes through,
        // detached does not, read-only throws.
        // ------------------------------------------------------------------

        [Test]
        public void Vector2_BoundComponentSetterWritesThrough()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var position = new NeoVector2(client.save.Get<NeoMemberVector2Writable>("Position"));

            position.y = 42f;

            // The whole leaf was rewritten with only the one component
            // changed, and a freshly minted wrapper sees it.
            var reread = new NeoVector2(client.save.Get<NeoMemberVector2Writable>("Position"));
            Assert.AreEqual(new Vector2(1f, 42f), reread.Value);
            Assert.AreEqual(42f, position.y);
            Assert.AreEqual(1f, position.x);
        }

        [Test]
        public void Vector2_DetachedComponentSetterStaysLocal()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var detached = new NeoVector2(new Vector2(1f, 2f));

            detached.x = 9f;

            Assert.AreEqual(new Vector2(9f, 2f), detached.Value);
            // Nothing was written anywhere — the stored leaf is untouched.
            var position = new NeoVector2(client.save.Get<NeoMemberVector2Writable>("Position"));
            Assert.AreEqual(new Vector2(1f, 2f), position.Value);

            // ...until the detached copy is assigned.
            NeoGeneratedTypesSupport.SetVector2(client.save, "Position", detached);
            Assert.AreEqual(new Vector2(9f, 2f), position.Value);
        }

        [Test]
        public void Vector2_ComponentSetterOnNonWritableNodeThrows()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var readOnlyNode = new NeoMemberVector2(
                client,
                "position-member",
                "position-value",
                NeoValueOwnership.Save);
            var wrapper = new NeoVector2(readOnlyNode);

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                wrapper.x = 5f);
            StringAssert.Contains("read-only", error!.Message);
            StringAssert.Contains("x", error.Message);
            Assert.AreEqual(new Vector2(1f, 2f), wrapper.Value);
        }

        [Test]
        public void Vector2_ComponentSetterOnReadOnlyOwnerThrows()
        {
            // Decision D5: NeoGeneratedClassValue.writableNode is materialized
            // without consulting IsReadOnly, so a read-only generated instance
            // can hand out a wrapper over a writable node. The owner-carrying
            // bound ctor is what closes that hole.
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var owner = ReadOnlyOwner(client);
            var wrapper = new NeoVector2(
                client.save.Get<NeoMemberVector2Writable>("Position"),
                owner);

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                wrapper.y = 5f);
            StringAssert.Contains("read-only", error!.Message);
            Assert.AreEqual(new Vector2(1f, 2f), wrapper.Value);
        }

        [Test]
        public void Vector2Int_BoundComponentSetterWritesThrough()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var cell = new NeoVector2Int(client.save.Get<NeoMemberVector2IntWritable>("Cell"));

            cell.x = 11;

            var reread = new NeoVector2Int(client.save.Get<NeoMemberVector2IntWritable>("Cell"));
            Assert.AreEqual(new Vector2Int(11, 6), reread.Value);
        }

        [Test]
        public void Vector2Int_DetachedComponentSetterStaysLocal()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var detached = new NeoVector2Int(1, 2);

            detached.y = 7;

            Assert.AreEqual(new Vector2Int(1, 7), detached.Value);
            var cell = new NeoVector2Int(client.save.Get<NeoMemberVector2IntWritable>("Cell"));
            Assert.AreEqual(new Vector2Int(5, 6), cell.Value);
        }

        [Test]
        public void Vector2Int_ComponentSetterOnReadOnlyOwnerThrows()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var owner = ReadOnlyOwner(client);
            var wrapper = new NeoVector2Int(
                client.save.Get<NeoMemberVector2IntWritable>("Cell"),
                owner);

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                wrapper.x = 5);
            StringAssert.Contains("read-only", error!.Message);
        }

        [Test]
        public void Vector3_BoundComponentSetterWritesThrough()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var size = new NeoVector3(client.save.Get<NeoMemberVector3Writable>("Size"));

            size.z = 30f;

            var reread = new NeoVector3(client.save.Get<NeoMemberVector3Writable>("Size"));
            Assert.AreEqual(new Vector3(1f, 2f, 30f), reread.Value);
        }

        [Test]
        public void Vector3_DetachedComponentSetterStaysLocal()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var detached = new NeoVector3(1f, 2f, 3f);

            detached.y = 20f;

            Assert.AreEqual(new Vector3(1f, 20f, 3f), detached.Value);
            var size = new NeoVector3(client.save.Get<NeoMemberVector3Writable>("Size"));
            Assert.AreEqual(new Vector3(1f, 2f, 3f), size.Value);
        }

        [Test]
        public void Vector3_ComponentSetterOnNonWritableNodeThrows()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var wrapper = new NeoVector3(new NeoMemberVector3(
                client,
                "size-member",
                "size-value",
                NeoValueOwnership.Save));

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                wrapper.z = 5f);
            StringAssert.Contains("read-only", error!.Message);
        }

        [Test]
        public void Vector3Int_BoundComponentSetterWritesThrough()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var grid = new NeoVector3Int(client.save.Get<NeoMemberVector3IntWritable>("Grid"));

            grid.y = 50;

            var reread = new NeoVector3Int(client.save.Get<NeoMemberVector3IntWritable>("Grid"));
            Assert.AreEqual(new Vector3Int(4, 50, 6), reread.Value);
        }

        [Test]
        public void Vector3Int_DetachedComponentSetterStaysLocal()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var detached = new NeoVector3Int(4, 5, 6);

            detached.z = 60;

            Assert.AreEqual(new Vector3Int(4, 5, 60), detached.Value);
            var grid = new NeoVector3Int(client.save.Get<NeoMemberVector3IntWritable>("Grid"));
            Assert.AreEqual(new Vector3Int(4, 5, 6), grid.Value);
        }

        [Test]
        public void Vector3Int_ComponentSetterOnReadOnlyOwnerThrows()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var owner = ReadOnlyOwner(client);
            var wrapper = new NeoVector3Int(
                client.save.Get<NeoMemberVector3IntWritable>("Grid"),
                owner);

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                wrapper.z = 5);
            StringAssert.Contains("read-only", error!.Message);
        }

        [Test]
        public void BoundComponentSetter_LeavesOtherLeavesAlone()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var position = new NeoVector2(client.save.Get<NeoMemberVector2Writable>("Position"));
            var offset = new NeoVector2(client.save.Get<NeoMemberVector2Writable>("Offset"));

            position.x = 99f;

            Assert.AreEqual(new Vector2(99f, 2f), position.Value);
            Assert.AreEqual(new Vector2(3f, 4f), offset.Value);
        }

        // ------------------------------------------------------------------
        // Missing-value reporting. The message names the component that was
        // read and says nothing about requiredness — an optional member with
        // no row is not "Required Vector2 'Offset'", which is what every one
        // of these reads used to claim.
        // ------------------------------------------------------------------

        [Test]
        public void ComponentAccessor_OnAnOptionalMemberDoesNotClaimItIsRequired()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            NeoGeneratedTypesSupport.SetVector2OrClear(client.save, "Offset", null);
            var offset = new NeoReadOnlyVector2(client.save.Get<NeoMemberVector2>("Offset"));

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                _ = offset.y);
            Assert.AreEqual(
                "Cannot read 'y': Vector2 'Offset' has no value.",
                error!.Message);
        }

        // One message shape per condition: a required member with no value
        // reports the same thing.
        [Test]
        public void ComponentAccessor_OnARequiredMemberReportsTheSameMessage()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var bounds = new NeoReadOnlyVector3(client.save.Get<NeoMemberVector3>("Bounds"));

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                _ = bounds.z);
            Assert.AreEqual(
                "Cannot read 'z': Vector3 'Bounds' has no value.",
                error!.Message);
        }

        // A component write is a read-modify-write, so it fails through the
        // same read — and names the same component.
        [Test]
        public void ComponentSetter_WithoutACurrentValueNamesTheComponent()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            NeoGeneratedTypesSupport.SetVector2OrClear(client.save, "Offset", null);
            var offset = new NeoVector2(client.save.Get<NeoMemberVector2Writable>("Offset"));

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                offset.x = 5f);
            Assert.AreEqual(
                "Cannot read 'x': Vector2 'Offset' has no value.",
                error!.Message);
            // Nothing was composed against a phantom base and written back.
            Assert.IsNull(client.save.Get<NeoMemberVector2Writable>("Offset").value);
        }

        // The whole-value read has no one component to blame, so it names
        // none.
        [Test]
        public void ValueAccessor_WithoutACurrentValueNamesNoComponent()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var bounds = new NeoReadOnlyVector3(client.save.Get<NeoMemberVector3>("Bounds"));

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                _ = bounds.Value);
            Assert.AreEqual("Vector3 'Bounds' has no value.", error!.Message);
        }

        // A detached wrapper always has a value; the field-naming read path
        // must not make one throw.
        [Test]
        public void ComponentAccessors_OnDetachedWrappersNeverThrow()
        {
            Assert.AreEqual(2f, new NeoReadOnlyVector2(1f, 2f).y);
            Assert.AreEqual(1, new NeoReadOnlyVector2Int(1, 2).x);
            Assert.AreEqual(3f, new NeoReadOnlyVector3(1f, 2f, 3f).z);
            Assert.AreEqual(2, new NeoReadOnlyVector3Int(1, 2, 3).y);
            Assert.AreEqual(9f, new NeoVector3(9f, 8f, 7f).x);
        }

        // ------------------------------------------------------------------
        // Fixture.
        // ------------------------------------------------------------------

        private static NeoReadOnlyClassValueDouble ReadOnlyOwner(NeoClient client)
            => new NeoReadOnlyClassValueDouble(client, client.save, "save-root-class");

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
                    ["Position"] = "position-member",
                    ["Offset"] = "offset-member",
                    ["Cell"] = "cell-member",
                    ["Anchor"] = "anchor-member",
                    ["Fractional"] = "fractional-member",
                    ["Size"] = "size-member",
                    ["Grid"] = "grid-member",
                    ["Bounds"] = "bounds-member",
                },
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Vector Wrapper Retrofit",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", saveRootClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                    ["position-member"] = new Vector2Member
                    {
                        id = "position-member",
                        projectId = "project-a",
                        name = "Position",
                        kind = MemberKind.Vector2,
                        Requirement = NeoMemberRequirementKind.Required,
                    },
                    ["offset-member"] = new Vector2Member
                    {
                        id = "offset-member",
                        projectId = "project-a",
                        name = "Offset",
                        kind = MemberKind.Vector2,
                    },
                    ["cell-member"] = new Vector2IntMember
                    {
                        id = "cell-member",
                        projectId = "project-a",
                        name = "Cell",
                        kind = MemberKind.Vector2Int,
                        Requirement = NeoMemberRequirementKind.Required,
                    },
                    ["anchor-member"] = new Vector2IntMember
                    {
                        id = "anchor-member",
                        projectId = "project-a",
                        name = "Anchor",
                        kind = MemberKind.Vector2Int,
                    },
                    ["fractional-member"] = new Vector2IntMember
                    {
                        id = "fractional-member",
                        projectId = "project-a",
                        name = "Fractional",
                        kind = MemberKind.Vector2Int,
                    },
                    ["size-member"] = new Vector3Member
                    {
                        id = "size-member",
                        projectId = "project-a",
                        name = "Size",
                        kind = MemberKind.Vector3,
                        Requirement = NeoMemberRequirementKind.Required,
                    },
                    ["grid-member"] = new Vector3IntMember
                    {
                        id = "grid-member",
                        projectId = "project-a",
                        name = "Grid",
                        kind = MemberKind.Vector3Int,
                        Requirement = NeoMemberRequirementKind.Required,
                    },
                    // Required, and deliberately left without a value row (no
                    // entry in the save record below) so the missing-value
                    // message can be pinned for the required case too.
                    ["bounds-member"] = new Vector3Member
                    {
                        id = "bounds-member",
                        projectId = "project-a",
                        name = "Bounds",
                        kind = MemberKind.Vector3,
                        Requirement = NeoMemberRequirementKind.Required,
                    },
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id, new()),
                    ["root-save-value"] = ObjectValue(
                        "root-save-value",
                        saveRootClass.id,
                        new Dictionary<string, string>
                        {
                            ["Position"] = "position-value",
                            ["Offset"] = "offset-value",
                            ["Cell"] = "cell-value",
                            ["Anchor"] = "anchor-value",
                            ["Fractional"] = "fractional-value",
                            ["Size"] = "size-value",
                            ["Grid"] = "grid-value",
                        }),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id, new()),
                    ["position-value"] = Vector2ValueRow("position-value", 1f, 2f),
                    ["offset-value"] = Vector2ValueRow("offset-value", 3f, 4f),
                    ["cell-value"] = Vector2ValueRow("cell-value", 5f, 6f),
                    ["anchor-value"] = Vector2ValueRow("anchor-value", 7f, 8f),
                    // Deliberately non-integer components behind a Vector2Int
                    // member — the wrapper read must reject it.
                    ["fractional-value"] = Vector2ValueRow("fractional-value", 1.5f, 2f),
                    ["size-value"] = Vector3ValueRow("size-value", 1f, 2f, 3f),
                    ["grid-value"] = Vector3ValueRow("grid-value", 4f, 5f, 6f),
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
                Requirement = NeoMemberRequirementKind.Required,
                valueId = valueId,
                classId = classId,
            };
        }

        private static Vector2MemberValue Vector2ValueRow(string id, float x, float y)
        {
            return new Vector2MemberValue
            {
                id = id,
                value = new NeoVector2Value { x = x, y = y },
            };
        }

        private static Vector3MemberValue Vector3ValueRow(string id, float x, float y, float z)
        {
            return new Vector3MemberValue
            {
                id = id,
                value = new NeoVector3Value { x = x, y = y, z = z },
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
