// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Tests
{
    /// <summary>
    /// A Class node that binds on read, as a variant application builds to
    /// address its row, binds a child only when the child is read. A listener
    /// hears every descendant's change, so watching such a node, or holding it
    /// from an eagerly bound parent, binds its whole subtree.
    /// </summary>
    public class NeoMemberBindsOnReadTests
    {
        [Test]
        public void BindsAStoredChildWhenItIsFirstRead()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            using var outer = new NeoMemberClassWritable(
                client,
                RequireMember<ClassMember>(client, "member-outer"),
                "v-outer",
                NeoValueOwnership.Save,
                bindsOnRead: true);
            Assert.IsEmpty(outer.BoundChildren, "Construction binds no child.");

            var inner = outer.Get<NeoMemberClass>("Inner");

            Assert.AreEqual(1, outer.BoundChildren.Count, "A read binds only the child it reads.");
            Assert.IsTrue(inner.BindsOnRead, "A child binds on read as its parent does.");
            Assert.IsEmpty(inner.BoundChildren);
            Assert.AreEqual("Label", inner.Get<NeoMemberString>("Label").value?.value);
            Assert.AreSame(inner, outer.Get<NeoMemberClass>("Inner"));
            Assert.AreEqual(2, outer.Count(), "Enumeration binds every child.");
        }

        [Test]
        public void AListenerBindsTheWholeSubtreeAndHearsANestedWrite()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            using var outer = new NeoMemberClassWritable(
                client,
                RequireMember<ClassMember>(client, "member-outer"),
                "v-outer",
                NeoValueOwnership.Save,
                bindsOnRead: true);
            int changes = 0;

            outer.OnChanged += _ => changes++;

            Assert.IsFalse(outer.BindsOnRead);
            Assert.AreEqual(2, outer.BoundChildren.Count);
            var inner = outer.BoundChildren.OfType<NeoMemberClass>().Single();
            Assert.IsFalse(inner.BindsOnRead);
            Assert.AreEqual(1, inner.BoundChildren.Count);
            client.SetWritableValue(NeoValueOwnership.Save, Text("v-label", "Changed"));
            Assert.AreEqual(1, changes, "The listener hears a write its tree had not read.");
        }

        [Test]
        public void AnEagerParentBindsTheSubtreeOfAChildThatBindsOnRead()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            using var shared = (NeoMemberClass)NeoMember.CreateWritable(
                client,
                RequireMember<ClassMember>(client, "member-inner"),
                "v-inner",
                NeoValueOwnership.Save,
                bindsOnRead: true);
            Assert.IsEmpty(shared.BoundChildren);

            using var outer = (NeoMemberClass)NeoMember.CreateWritable(
                client,
                RequireMember<ClassMember>(client, "member-outer"),
                "v-outer",
                NeoValueOwnership.Save);
            int changes = 0;
            outer.OnChanged += _ => changes++;

            Assert.AreSame(shared, outer.Get<NeoMemberClass>("Inner"), "The parent holds the registered node.");
            Assert.IsFalse(shared.BindsOnRead);
            client.SetWritableValue(NeoValueOwnership.Save, Text("v-label", "Changed"));
            Assert.AreEqual(1, changes, "The parent's listener hears the shared child's descendant.");
        }

        [Test]
        public void ClientRootBindsSettledChildrenAndSubscriptionHearsNestedWrites()
        {
            ProjectData data = BuildProjectData();
            ((ClassMember)data.members["root-save"]).DeclaredClassId = "class-outer";
            ((ObjectMemberValue)data.values["v-root-save"]).classId = "class-outer";
            ((ObjectMemberValue)data.values["v-root-save"]).value = new Dictionary<string, string>
            {
                ["Name"] = "v-name",
                ["Inner"] = "v-inner",
            };
            data.values.Remove("v-outer");
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.That(client.save.BoundChildren.Count, Is.EqualTo(2));
            int changes = 0;
            client.save.OnChanged += _ => changes++;
            client.SetWritableValue(NeoValueOwnership.Save, Text("v-label", "Changed"));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(client.save.Get<NeoMemberClass>("Inner").Get<NeoMemberString>("Label").value.value,
                Is.EqualTo("Changed"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedFactoryDefersDescendantsUntilReadOrSubscribed(bool propertyResult)
        {
            ProjectData data = BuildProjectData();
            data.classes["class-root"].schema["Outer"] = "member-outer";
            ((ObjectMemberValue)data.values["v-root-save"]).value["Outer"] = "v-outer";
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            using var view = propertyResult
                ? NeoGeneratedTypesSupport.ReadNSPropertyClass<NeoMemberClass>(client, new NeoReference<NeoMemberClass>(client, "v-outer", null, null), true, true,
                    (_, node) => node, (_, node) => node)
                : (NeoMemberClass)NeoGeneratedTypesSupport.ResolveClassValue(client, "v-outer",
                    new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>(),
                    new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
                    {
                        ["class-outer"] = (_, node) => node,
                    });
            Assert.That(view.BoundChildren, Is.Empty);
            var inner = view.Get<NeoMemberClass>("Inner");
            Assert.That(inner.Get<NeoMemberString>("Label").value.value, Is.EqualTo("Label"));
            int changes = 0;
            view.OnChanged += _ => changes++;
            client.SetWritableValue(NeoValueOwnership.Save, Text("v-label", "Changed"));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(inner.Get<NeoMemberString>("Label").value.value, Is.EqualTo("Changed"));
        }

        private static TMember RequireMember<TMember>(NeoClient client, string id)
            where TMember : NeoCompose.Runtime.Json.Member
        {
            if (!client.TryGetMember(id, out TMember? member))
                Assert.Fail($"Fixture is missing member '{id}'.");
            return member!;
        }

        private static ProjectData BuildProjectData()
        {
            var rootClass = Class("class-root", new Dictionary<string, string>());
            var outerClass = Class("class-outer", new Dictionary<string, string>
            {
                ["Name"] = "member-name",
                ["Inner"] = "member-inner",
            });
            var innerClass = Class("class-inner", new Dictionary<string, string>
            {
                ["Label"] = "member-label",
            });
            return new ProjectData
            {
                project = new Project
                {
                    id = "project-binds-on-read",
                    _id = "project-binds-on-read",
                    name = "Binds on read",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    ["root-assets"] = ClassMemberOf("root-assets", rootClass.id, "v-root-assets"),
                    ["root-save"] = ClassMemberOf("root-save", rootClass.id, "v-root-save"),
                    ["root-session"] = ClassMemberOf("root-session", rootClass.id, "v-root-session"),
                    ["member-outer"] = ClassMemberOf("member-outer", outerClass.id),
                    ["member-inner"] = ClassMemberOf("member-inner", innerClass.id),
                    ["member-name"] = StringMemberOf("member-name"),
                    ["member-label"] = StringMemberOf("member-label"),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["v-root-assets"] = ObjectRow("v-root-assets", rootClass.id, new Dictionary<string, string>()),
                    ["v-root-save"] = ObjectRow("v-root-save", rootClass.id, new Dictionary<string, string>()),
                    ["v-root-session"] = ObjectRow("v-root-session", rootClass.id, new Dictionary<string, string>()),
                    ["v-outer"] = ObjectRow("v-outer", outerClass.id, new Dictionary<string, string>
                    {
                        ["Name"] = "v-name",
                        ["Inner"] = "v-inner",
                    }),
                    ["v-name"] = Text("v-name", "Name"),
                    ["v-inner"] = ObjectRow("v-inner", innerClass.id, new Dictionary<string, string>
                    {
                        ["Label"] = "v-label",
                    }),
                    ["v-label"] = Text("v-label", "Label"),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [outerClass.id] = outerClass,
                    [innerClass.id] = innerClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            }.WithUserRoot();
        }

        private static NeoSchemaClass Class(string id, Dictionary<string, string> schema)
        {
            return new NeoSchemaClass
            {
                id = id,
                projectId = "project-binds-on-read",
                name = id,
                schema = schema,
            };
        }

        private static ClassMember ClassMemberOf(string id, string classId, string? valueId = null)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-binds-on-read",
                name = id,
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = valueId,
                DeclaredClassId = classId,
            };
        }

        private static StringMember StringMemberOf(string id)
        {
            return new StringMember
            {
                id = id,
                projectId = "project-binds-on-read",
                name = id,
                kind = MemberKind.String,
                Requirement = NeoMemberRequirementKind.Required,
            };
        }

        private static ObjectMemberValue ObjectRow(string id, string classId, Dictionary<string, string> value)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = value,
            };
        }

        private static StringMemberValue Text(string id, string value)
        {
            return new StringMemberValue
            {
                id = id,
                value = value,
            };
        }
    }
}
