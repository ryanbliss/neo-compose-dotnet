// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.IO;
using System.Collections.Generic;
using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Coverage for the flat <see cref="NeoClient.nodes"/> registry +
    /// dedup behavior on <see cref="NeoMember.Create"/> /
    /// <see cref="NeoMember.CreateWritable"/>.
    ///
    /// The registry's contract:
    ///
        ///   - Every constructed <see cref="NeoMember"/> registers itself
        ///     under <c>MakeNodeKey(member.id, overrideValueId, ownership)</c>.
    ///   - <see cref="NeoMember.Create"/> /
    ///     <see cref="NeoMember.CreateWritable"/> short-circuit to the
    ///     registered instance when one exists for the requested key.
        ///   - <c>overrideValueId</c> being null produces a key scoped by
        ///     ownership; non-null appends <c>"_{valueId}"</c>.
    /// </summary>
    public class NeoClientNodeRegistryTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        private static string LoadFixture(string fileName)
        {
            return File.ReadAllText(Path.Combine(PackageRoot, fileName));
        }

        private static NeoClient LoadClient()
        {
            return NeoTestSaveStack.LoadClient(LoadFixture("synth-example.json"));
        }

        /// <summary>
        /// Wraps <see cref="NeoClient.TryGetMember"/> with an assert
        /// + non-null return so tests can chain through to typed usage
        /// without the nullable flow-analysis fighting them. NUnit's
        /// <c>Assert.IsTrue(TryGet(out var x))</c> doesn't propagate
        /// the not-null narrowing the way an inline <c>if</c> does, so
        /// callers reading <c>x</c> after the assert still see <c>T?</c>.
        /// </summary>
        private static T RequireMember<T>(NeoClient client, string id) where T : Member
        {
            if (!client.TryGetMember(id, out T? member))
            {
                Assert.Fail($"Fixture is missing member '{id}' of type {typeof(T).Name}");
                throw new System.InvalidOperationException("unreachable");
            }
            return member;
        }

        private static NeoMember RequireNode(
            NeoClient client,
            string memberId,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
        {
            if (!client.TryGetNode(memberId, overrideValueId, ownership, out NeoMember? node))
            {
                Assert.Fail(
                    $"Registry is missing node {NeoClient.MakeNodeKey(memberId, overrideValueId, ownership)}");
                throw new System.InvalidOperationException("unreachable");
            }
            return node;
        }

        [Test]
        public void MakeNodeKey_NoOverride_IsBareMemberId()
        {
            Assert.AreEqual("asset:member-x", NeoClient.MakeNodeKey("member-x", null));
            Assert.AreEqual("asset:member-x", NeoClient.MakeNodeKey("member-x", ""));
            Assert.AreEqual("save:member-x", NeoClient.MakeNodeKey(
                "member-x",
                null,
                NeoValueOwnership.Save));
        }

        [Test]
        public void MakeNodeKey_WithOverride_AppendsValueId()
        {
            Assert.AreEqual("asset:member-x_v-7", NeoClient.MakeNodeKey("member-x", "v-7"));
        }

        [Test]
        public void NeoClient_RootsAreRegistered()
        {
            var client = LoadClient();

            // The roots are constructed in NeoClient's ctor; all
            // self-register.
            Assert.AreSame(client.assets, RequireNode(client, "root-assets", null));
            Assert.AreSame(client.save, RequireNode(
                client,
                "root-save",
                null,
                NeoValueOwnership.Save));
            Assert.AreSame(client.session, RequireNode(
                client,
                "root-session",
                null,
                NeoValueOwnership.Session));
        }

        [Test]
        public void Create_ReturnsCachedInstance_OnSecondCall()
        {
            var client = LoadClient();
            var nameMember = RequireMember<StringMember>(client, "member-name");

            var first = NeoMember.Create(client, nameMember, null);
            var second = NeoMember.Create(client, nameMember, null);

            Assert.AreSame(first, second,
                "Create should short-circuit to the cached node, not construct a duplicate");
        }

        [Test]
        public void CreateWritable_ReturnsCachedInstance_OnSecondCall()
        {
            var client = LoadClient();
            var nameMember = RequireMember<StringMember>(client, "member-name");

            var first = NeoMember.CreateWritable(client, nameMember, null);
            var second = NeoMember.CreateWritable(client, nameMember, null);

            Assert.AreSame(first, second);
            Assert.IsInstanceOf<NeoMemberStringWritable>(first);
        }

        [Test]
        public void Create_OverrideValueId_RegistersUnderComposedKey()
        {
            var client = LoadClient();
            var nameMember = RequireMember<StringMember>(client, "member-name");

            var noOverride = NeoMember.Create(client, nameMember, null);
            var withOverride = NeoMember.Create(client, nameMember, "v-str");

            Assert.AreNotSame(noOverride, withOverride,
                "Different override-value ids must compose distinct registry keys");

            Assert.AreSame(noOverride, RequireNode(client, "member-name", null));
            Assert.AreSame(withOverride, RequireNode(client, "member-name", "v-str"));
        }

        [Test]
        public void NeoClient_Nodes_ContainsWalkedChildren()
        {
            var client = LoadClient();
            var heroMember = RequireMember<ClassMember>(client, "member-hero");
            // Construct a Class bound to the stored v-dict row
            // (defaultValue alone wouldn't trigger a child walk —
            // member-hero has no static valueId of its own). v-dict
            // carries `{ Name: "v-name", Level: "v-level" }`; "Level"
            // isn't in the class-hero schema so only the "Name" child
            // is walked + registered.
            var hero = NeoMember.Create(client, heroMember, "v-dict") as NeoMemberClass;
            Assert.IsNotNull(hero);

            Assert.IsTrue(
                client.nodes.ContainsKey("asset:member-hero_v-dict"),
                "Parent registers under its composed key");
            var nameChild = RequireNode(client, "member-name", "v-name");
            Assert.IsInstanceOf<NeoMemberString>(nameChild);
        }

        [Test]
        public void ClassChild_ReadsSchemaDefault_WhenParentValueDoesNotReferenceChildRow()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildDefaultBackedProjectData());
            var cardMember = RequireMember<ClassMember>(client, "member-card");

            var card = NeoMember.Create(client, cardMember, "v-card") as NeoMemberClass;
            Assert.IsNotNull(card);

            var name = card!.Get<NeoMemberString>("Name");
            Assert.AreEqual("Default Name", name.value?.value);
        }

        [Test]
        public void ClassChild_ReadsCompositeDefault_WhenConcreteParentRowOmitsKey()
        {
            ProjectData data = BuildDefaultBackedProjectData();
            var cardMember = (ClassMember)data.members["member-card"];
            cardMember.valueId = "v-card";
            cardMember.defaultValue = new ObjectMemberValueBase
            {
                value = new Dictionary<string, string>
                {
                    ["Name"] = "v-card-default-name",
                },
            };
            data.values["v-card-default-name"] = StringValue(
                "v-card-default-name",
                "Composite Default Name");

            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var card = NeoMember.Create(client, cardMember, null) as NeoMemberClass;

            Assert.IsNotNull(card);
            Assert.AreEqual(
                "Composite Default Name",
                card!.Get<NeoMemberString>("Name").value?.value);
        }

        [Test]
        public void List_ReadsDefaultEntryIds_WhenListHasNoStoredValueRow()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildDefaultBackedProjectData());
            var listMember = RequireMember<ListMember>(client, "member-children");

            var list = NeoMember.Create(client, listMember, null) as NeoMemberList;
            Assert.IsNotNull(list);

            Assert.AreEqual(2, list!.Count);
            Assert.AreEqual(
                "One",
                ((NeoMemberString)list[0]).value?.value);
            Assert.AreEqual(
                "Two",
                ((NeoMemberString)list[1]).value?.value);
        }

        [Test]
        public void ClassListEntry_UsesConcreteRowClass_ForInheritedSchemaDefaults()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildDefaultBackedProjectData());
            var containerMember = RequireMember<ClassMember>(client, "member-container");

            var container = NeoMember.Create(client, containerMember, "v-container") as NeoMemberClass;
            Assert.IsNotNull(container);

            var items = container!.Get<NeoMemberList>("Items");
            Assert.AreEqual(1, items.Count);

            var item = items[0] as NeoMemberClass;
            Assert.IsNotNull(item);
            Assert.AreEqual("class-derived-item", item!.inheritanceChain[0].id);
            Assert.AreEqual(
                "Inherited Name",
                item.Get<NeoMemberString>("Name").value?.value);
        }

        [Test]
        public void Create_FollowedByCreateWritable_ReplacesReadOnlyWithSavedInstance()
        {
            // Assets are constructed before Save and can register
            // read-only children for shared schema members. A later
            // saved construction for the same key must upgrade the
            // registry entry so save-side generated wrappers can get
            // writeable child nodes.
            var client = LoadClient();
            var altMember = RequireMember<StringMember>(client, "member-altname");

            var first = NeoMember.Create(client, altMember, null);
            var second = NeoMember.CreateWritable(client, altMember, null);

            Assert.AreNotSame(first, second);
            Assert.IsInstanceOf<NeoMemberString>(first);
            Assert.IsInstanceOf<NeoMemberStringWritable>(second);
            Assert.AreSame(second, RequireNode(
                client,
                "member-altname",
                null,
                NeoValueOwnership.Session));
        }

        private static ProjectData BuildDefaultBackedProjectData()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "class-root",
                projectId = "project-defaults",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var cardClass = new NeoSchemaClass
            {
                id = "class-card",
                projectId = "project-defaults",
                name = "Card",
                schema = new Dictionary<string, string>
                {
                    ["Name"] = "member-default-name",
                },
            };
            var containerClass = new NeoSchemaClass
            {
                id = "class-container",
                projectId = "project-defaults",
                name = "Container",
                schema = new Dictionary<string, string>
                {
                    ["Items"] = "member-items",
                },
            };
            var baseItemClass = new NeoSchemaClass
            {
                id = "class-base-item",
                projectId = "project-defaults",
                name = "BaseItem",
                schema = new Dictionary<string, string>
                {
                    ["Name"] = "member-inherited-name",
                },
            };
            var derivedItemClass = new NeoSchemaClass
            {
                id = "class-derived-item",
                projectId = "project-defaults",
                name = "DerivedItem",
                extendsClassId = baseItemClass.id,
                schema = new Dictionary<string, string>(),
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-defaults",
                    _id = "project-defaults",
                    name = "Defaults",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    ["root-assets"] = RootMember("root-assets", "v-root-assets", rootClass.id),
                    ["root-save"] = RootMember("root-save", "v-root-save", rootClass.id),
                    ["root-session"] = RootMember("root-session", "v-root-session", rootClass.id),
                    ["member-card"] = new ClassMember
                    {
                        id = "member-card",
                        projectId = "project-defaults",
                        name = "Card",
                        kind = MemberKind.Class,
                        Requirement = NeoMemberRequirementKind.Required,
                        classId = cardClass.id,
                    },
                    ["member-container"] = new ClassMember
                    {
                        id = "member-container",
                        projectId = "project-defaults",
                        name = "Container",
                        kind = MemberKind.Class,
                        Requirement = NeoMemberRequirementKind.Required,
                        classId = containerClass.id,
                    },
                    ["member-default-name"] = new StringMember
                    {
                        id = "member-default-name",
                        projectId = "project-defaults",
                        name = "Name",
                        kind = MemberKind.String,
                        Requirement = NeoMemberRequirementKind.Required,
                        defaultValue = new StringMemberValueBase
                        {
                            value = "Default Name",
                        },
                    },
                    ["member-inherited-name"] = new StringMember
                    {
                        id = "member-inherited-name",
                        projectId = "project-defaults",
                        name = "Inherited Name",
                        kind = MemberKind.String,
                        Requirement = NeoMemberRequirementKind.Required,
                        defaultValue = new StringMemberValueBase
                        {
                            value = "Inherited Name",
                        },
                    },
                    ["member-items"] = new ListMember
                    {
                        id = "member-items",
                        projectId = "project-defaults",
                        name = "Items",
                        kind = MemberKind.List,
                        Requirement = NeoMemberRequirementKind.Required,
                        entryMemberId = "member-item-entry",
                    },
                    ["member-item-entry"] = new ClassMember
                    {
                        id = "member-item-entry",
                        projectId = "project-defaults",
                        name = "Item Entry",
                        kind = MemberKind.Class,
                        Requirement = NeoMemberRequirementKind.Required,
                        classId = baseItemClass.id,
                    },
                    ["member-children"] = new ListMember
                    {
                        id = "member-children",
                        projectId = "project-defaults",
                        name = "Children",
                        kind = MemberKind.List,
                        Requirement = NeoMemberRequirementKind.Required,
                        entryMemberId = "member-list-entry",
                        defaultValue = new ArrayMemberValueBase
                        {
                            value = new[] { "v-entry-one", "v-entry-two" },
                        },
                    },
                    ["member-list-entry"] = new StringMember
                    {
                        id = "member-list-entry",
                        projectId = "project-defaults",
                        name = "Entry",
                        kind = MemberKind.String,
                        Requirement = NeoMemberRequirementKind.Required,
                    },
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["v-root-assets"] = ObjectValue("v-root-assets", rootClass.id),
                    ["v-root-save"] = ObjectValue("v-root-save", rootClass.id),
                    ["v-root-session"] = ObjectValue("v-root-session", rootClass.id),
                    ["v-card"] = ObjectValue("v-card", cardClass.id),
                    ["v-container"] = ObjectValue(
                        "v-container",
                        containerClass.id,
                        new Dictionary<string, string>
                        {
                            ["Items"] = "v-items",
                        }),
                    ["v-items"] = ArrayValue("v-items", "v-derived-item"),
                    ["v-derived-item"] = ObjectValue("v-derived-item", derivedItemClass.id),
                    ["v-entry-one"] = StringValue("v-entry-one", "One"),
                    ["v-entry-two"] = StringValue("v-entry-two", "Two"),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [cardClass.id] = cardClass,
                    [containerClass.id] = containerClass,
                    [baseItemClass.id] = baseItemClass,
                    [derivedItemClass.id] = derivedItemClass,
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
                projectId = "project-defaults",
                name = id,
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = valueId,
                classId = classId,
            };
        }

        private static ObjectMemberValue ObjectValue(
            string id,
            string classId,
            Dictionary<string, string>? value = null)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = value ?? new Dictionary<string, string>(),
            };
        }

        private static ArrayMemberValue ArrayValue(string id, params string[] values)
        {
            return new ArrayMemberValue
            {
                id = id,
                value = values,
            };
        }

        private static StringMemberValue StringValue(string id, string value)
        {
            return new StringMemberValue
            {
                id = id,
                value = value,
            };
        }
    }
}
