// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoCandidateValueInferenceTests
    {
        [Test]
        public void CandidateInferenceKeepsSessionBeforeSaveAndAuthoredParents()
        {
            using var client = CreateClient();
            client.SetWritableValues(NeoValueOwnership.Save, new[] { Parent("save-parent", "first") });
            client.SetWritableValues(NeoValueOwnership.Session, new[] { Parent("session-parent", "second") });
            AssertMember(client, "second-value");
            using (client.ReadCandidate(new NeoWritePlan(client))) AssertMember(client, "second-value");
        }

        [Test]
        public void CandidateReplacementRetainsStoreOrderAndRemovalExposesNextParent()
        {
            using var client = CreateClient();
            client.SetWritableValues(NeoValueOwnership.Session, new[]
            {
                Parent("first-parent", "first", referencesChild: false),
                Parent("second-parent", "second"),
            });
            var plan = new NeoWritePlan(client);
            // Stage in the reverse order. The first parent's replacement must
            // retain its original store position when its reference is added.
            plan.Set(NeoValueOwnership.Session, Parent("second-parent", "second"));
            plan.Set(NeoValueOwnership.Session, Parent("first-parent", "first"));
            using (client.ReadCandidate(plan)) AssertMember(client, "first-value");
            AssertMember(client, "second-value");
            plan.Remove(NeoValueOwnership.Session, "first-parent");
            using (client.ReadCandidate(plan)) AssertMember(client, "second-value");
        }

        [Test]
        public void NewCandidateSessionParentPrecedesSaveWithoutPublishing()
        {
            using var client = CreateClient();
            client.SetWritableValues(NeoValueOwnership.Save, new[] { Parent("save-parent", "first") });
            var plan = new NeoWritePlan(client);
            plan.Set(NeoValueOwnership.Session, Parent("new-parent", "second"));
            using (client.ReadCandidate(plan)) AssertMember(client, "second-value");
            Assert.IsFalse(client.sessionValues.ContainsKey("new-parent"));
            AssertMember(client, "first-value");
        }

        private static void AssertMember(NeoClient client, string expectedId)
        {
            Assert.IsTrue(client.TryInferMemberForValueId("child", out Member? member));
            Assert.AreEqual(expectedId, member!.id);
        }

        private static ObjectMemberValue Parent(string id, string classId, bool referencesChild = true) => new()
        {
            id = id, classId = classId,
            value = referencesChild ? new() { ["Value"] = "child" } : new(),
        };

        private static NeoClient CreateClient()
        {
            var data = new ProjectData
            {
                project = new Project
                {
                    id = "inference", _id = "inference", name = "Inference",
                    rootAssetsMemberId = "assets-root", rootSaveFileMemberId = "save-root", rootSessionMemberId = "session-root",
                },
                classes = new()
                {
                    ["root"] = new NeoSchemaClass { id = "root", name = "Root", schema = new() },
                    ["first"] = new NeoSchemaClass { id = "first", name = "First", schema = new() { ["Value"] = "first-value" } },
                    ["second"] = new NeoSchemaClass { id = "second", name = "Second", schema = new() { ["Value"] = "second-value" } },
                    ["authored"] = new NeoSchemaClass { id = "authored", name = "Authored", schema = new() { ["Value"] = "authored-value" } },
                },
                members = new(), values = new(), enums = new(),
            };
            foreach (string name in new[] { "assets", "save", "session" })
            {
                data.members[name + "-root"] = new ClassMember
                {
                    id = name + "-root", name = name, kind = MemberKind.Class,
                    valueId = name, classId = "root", Requirement = NeoMemberRequirementKind.Required,
                    Storage = name == "save" ? NeoMemberStorage.Save : name == "session" ? NeoMemberStorage.Session : NeoMemberStorage.Immutable,
                };
                data.values[name] = new ObjectMemberValue { id = name, classId = "root", value = new() };
            }
            foreach (string name in new[] { "first", "second", "authored" })
                data.members[name + "-value"] = new StringMember
                {
                    id = name + "-value", name = "Value", kind = MemberKind.String,
                    Requirement = NeoMemberRequirementKind.Required,
                };
            data.values["authored-parent"] = Parent("authored-parent", "authored");
            data.values["child"] = new StringMemberValue { id = "child", value = "value" };
            return NeoTestSaveStack.ClientFromSchema(data);
        }
    }
}
