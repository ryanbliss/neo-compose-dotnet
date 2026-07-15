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
    /// Vector wrapper retrofit to the assignment convention
    /// (specs/color-member.md §6): get-only <c>Value</c>, wrapper-typed
    /// SetVector* write funnels with value-copy semantics, *OrClear helpers,
    /// distinct null guard for required assignment, and value-based equality
    /// — for one float vector (Vector2) and one int vector (Vector2Int).
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

        [Test]
        public void SetVector2_BoundWrapperCopiesValueWithoutLinking()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var offset = new NeoVector2(client.save.Get<NeoMemberVector2Writable>("Offset"));
            var offsetBefore = offset.Value;

            NeoGeneratedTypesSupport.SetVector2(client.save, "Position", offset);
            var position = new NeoVector2(client.save.Get<NeoMemberVector2Writable>("Position"));
            Assert.AreEqual(offsetBefore, position.Value);

            // Mutating the source afterwards must not affect the target.
            NeoGeneratedTypesSupport.SetVector2(client.save, "Offset", new Vector2(40f, 41f));
            Assert.AreEqual(new Vector2(40f, 41f), offset.Value);
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
        // Fixture.
        // ------------------------------------------------------------------

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
                        required = true,
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
                        required = true,
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
                        }),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id, new()),
                    ["position-value"] = Vector2ValueRow("position-value", 1f, 2f),
                    ["offset-value"] = Vector2ValueRow("offset-value", 3f, 4f),
                    ["cell-value"] = Vector2ValueRow("cell-value", 5f, 6f),
                    ["anchor-value"] = Vector2ValueRow("anchor-value", 7f, 8f),
                    // Deliberately non-integer components behind a Vector2Int
                    // member — the wrapper read must reject it.
                    ["fractional-value"] = Vector2ValueRow("fractional-value", 1.5f, 2f),
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

        private static Vector2MemberValue Vector2ValueRow(string id, float x, float y)
        {
            return new Vector2MemberValue
            {
                id = id,
                value = new NeoVector2Value { x = x, y = y },
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
