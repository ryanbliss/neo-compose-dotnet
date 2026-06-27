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
    /// dedup behavior on <see cref="NeoAttribute.Create"/> /
    /// <see cref="NeoAttribute.CreateWritable"/>.
    ///
    /// The registry's contract:
    ///
        ///   - Every constructed <see cref="NeoAttribute"/> registers itself
        ///     under <c>MakeNodeKey(attribute.id, overrideValueId, ownership)</c>.
    ///   - <see cref="NeoAttribute.Create"/> /
    ///     <see cref="NeoAttribute.CreateWritable"/> short-circuit to the
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
        /// Wraps <see cref="NeoClient.TryGetAttribute"/> with an assert
        /// + non-null return so tests can chain through to typed usage
        /// without the nullable flow-analysis fighting them. NUnit's
        /// <c>Assert.IsTrue(TryGet(out var x))</c> doesn't propagate
        /// the not-null narrowing the way an inline <c>if</c> does, so
        /// callers reading <c>x</c> after the assert still see <c>T?</c>.
        /// </summary>
        private static T RequireAttribute<T>(NeoClient client, string id) where T : Attribute
        {
            if (!client.TryGetAttribute(id, out T? attr))
            {
                Assert.Fail($"Fixture is missing attribute '{id}' of type {typeof(T).Name}");
                throw new System.InvalidOperationException("unreachable");
            }
            return attr;
        }

        private static NeoAttribute RequireNode(
            NeoClient client,
            string attributeId,
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
        {
            if (!client.TryGetNode(attributeId, overrideValueId, ownership, out NeoAttribute? node))
            {
                Assert.Fail(
                    $"Registry is missing node {NeoClient.MakeNodeKey(attributeId, overrideValueId, ownership)}");
                throw new System.InvalidOperationException("unreachable");
            }
            return node;
        }

        [Test]
        public void MakeNodeKey_NoOverride_IsBareAttributeId()
        {
            Assert.AreEqual("asset:attr-x", NeoClient.MakeNodeKey("attr-x", null));
            Assert.AreEqual("asset:attr-x", NeoClient.MakeNodeKey("attr-x", ""));
            Assert.AreEqual("save:attr-x", NeoClient.MakeNodeKey(
                "attr-x",
                null,
                NeoValueOwnership.Save));
        }

        [Test]
        public void MakeNodeKey_WithOverride_AppendsValueId()
        {
            Assert.AreEqual("asset:attr-x_v-7", NeoClient.MakeNodeKey("attr-x", "v-7"));
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
            var nameAttr = RequireAttribute<StringAttribute>(client, "attr-name");

            var first = NeoAttribute.Create(client, nameAttr, null);
            var second = NeoAttribute.Create(client, nameAttr, null);

            Assert.AreSame(first, second,
                "Create should short-circuit to the cached node, not construct a duplicate");
        }

        [Test]
        public void CreateWritable_ReturnsCachedInstance_OnSecondCall()
        {
            var client = LoadClient();
            var nameAttr = RequireAttribute<StringAttribute>(client, "attr-name");

            var first = NeoAttribute.CreateWritable(client, nameAttr, null);
            var second = NeoAttribute.CreateWritable(client, nameAttr, null);

            Assert.AreSame(first, second);
            Assert.IsInstanceOf<NeoAttributeStringWritable>(first);
        }

        [Test]
        public void Create_OverrideValueId_RegistersUnderComposedKey()
        {
            var client = LoadClient();
            var nameAttr = RequireAttribute<StringAttribute>(client, "attr-name");

            var noOverride = NeoAttribute.Create(client, nameAttr, null);
            var withOverride = NeoAttribute.Create(client, nameAttr, "v-str");

            Assert.AreNotSame(noOverride, withOverride,
                "Different override-value ids must compose distinct registry keys");

            Assert.AreSame(noOverride, RequireNode(client, "attr-name", null));
            Assert.AreSame(withOverride, RequireNode(client, "attr-name", "v-str"));
        }

        [Test]
        public void NeoClient_Nodes_ContainsWalkedChildren()
        {
            var client = LoadClient();
            var heroAttr = RequireAttribute<CustomAttribute>(client, "attr-hero");
            // Construct a Custom bound to the stored v-dict row
            // (defaultValue alone wouldn't trigger a child walk —
            // attr-hero has no static valueId of its own). v-dict
            // carries `{ Name: "v-name", Level: "v-level" }`; "Level"
            // isn't in the type-hero schema so only the "Name" child
            // is walked + registered.
            var hero = NeoAttribute.Create(client, heroAttr, "v-dict") as NeoAttributeCustom;
            Assert.IsNotNull(hero);

            Assert.IsTrue(
                client.nodes.ContainsKey("asset:attr-hero_v-dict"),
                "Parent registers under its composed key");
            var nameChild = RequireNode(client, "attr-name", "v-name");
            Assert.IsInstanceOf<NeoAttributeString>(nameChild);
        }

        [Test]
        public void CustomChild_ReadsSchemaDefault_WhenParentValueDoesNotReferenceChildRow()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildDefaultBackedProjectData());
            var cardAttr = RequireAttribute<CustomAttribute>(client, "attr-card");

            var card = NeoAttribute.Create(client, cardAttr, "v-card") as NeoAttributeCustom;
            Assert.IsNotNull(card);

            var name = card!.Get<NeoAttributeString>("Name");
            Assert.AreEqual("Default Name", name.value?.value);
        }

        [Test]
        public void List_ReadsDefaultEntryIds_WhenListHasNoStoredValueRow()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildDefaultBackedProjectData());
            var listAttr = RequireAttribute<ListAttribute>(client, "attr-children");

            var list = NeoAttribute.Create(client, listAttr, null) as NeoAttributeList;
            Assert.IsNotNull(list);

            Assert.AreEqual(2, list!.Count);
            Assert.AreEqual(
                "One",
                ((NeoAttributeString)list[0]).value?.value);
            Assert.AreEqual(
                "Two",
                ((NeoAttributeString)list[1]).value?.value);
        }

        [Test]
        public void CustomListEntry_UsesConcreteRowType_ForInheritedSchemaDefaults()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildDefaultBackedProjectData());
            var containerAttr = RequireAttribute<CustomAttribute>(client, "attr-container");

            var container = NeoAttribute.Create(client, containerAttr, "v-container") as NeoAttributeCustom;
            Assert.IsNotNull(container);

            var items = container!.Get<NeoAttributeList>("Items");
            Assert.AreEqual(1, items.Count);

            var item = items[0] as NeoAttributeCustom;
            Assert.IsNotNull(item);
            Assert.AreEqual("type-derived-item", item!.inheritanceChain[0].id);
            Assert.AreEqual(
                "Inherited Name",
                item.Get<NeoAttributeString>("Name").value?.value);
        }

        [Test]
        public void Create_FollowedByCreateWritable_ReplacesReadOnlyWithSavedInstance()
        {
            // Assets are constructed before Save and can register
            // read-only children for shared schema attributes. A later
            // saved construction for the same key must upgrade the
            // registry entry so save-side generated wrappers can get
            // writeable child nodes.
            var client = LoadClient();
            var altAttr = RequireAttribute<StringAttribute>(client, "attr-altname");

            var first = NeoAttribute.Create(client, altAttr, null);
            var second = NeoAttribute.CreateWritable(client, altAttr, null);

            Assert.AreNotSame(first, second);
            Assert.IsInstanceOf<NeoAttributeString>(first);
            Assert.IsInstanceOf<NeoAttributeStringWritable>(second);
            Assert.AreSame(second, RequireNode(
                client,
                "attr-altname",
                null,
                NeoValueOwnership.Session));
        }

        private static ProjectData BuildDefaultBackedProjectData()
        {
            var rootType = new CustomType
            {
                id = "type-root",
                projectId = "project-defaults",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var cardType = new CustomType
            {
                id = "type-card",
                projectId = "project-defaults",
                name = "Card",
                schema = new Dictionary<string, string>
                {
                    ["Name"] = "attr-default-name",
                },
            };
            var containerType = new CustomType
            {
                id = "type-container",
                projectId = "project-defaults",
                name = "Container",
                schema = new Dictionary<string, string>
                {
                    ["Items"] = "attr-items",
                },
            };
            var baseItemType = new CustomType
            {
                id = "type-base-item",
                projectId = "project-defaults",
                name = "BaseItem",
                schema = new Dictionary<string, string>
                {
                    ["Name"] = "attr-inherited-name",
                },
            };
            var derivedItemType = new CustomType
            {
                id = "type-derived-item",
                projectId = "project-defaults",
                name = "DerivedItem",
                extendsTypeId = baseItemType.id,
                schema = new Dictionary<string, string>(),
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-defaults",
                    _id = "project-defaults",
                    name = "Defaults",
                    rootAssetsAttributeId = "root-assets",
                    rootSaveFileAttributeId = "root-save",
                    rootSessionAttributeId = "root-session",
                },
                attributes = new Dictionary<string, NeoCompose.Runtime.Json.Attribute>
                {
                    ["root-assets"] = RootAttribute("root-assets", "v-root-assets", rootType.id),
                    ["root-save"] = RootAttribute("root-save", "v-root-save", rootType.id),
                    ["root-session"] = RootAttribute("root-session", "v-root-session", rootType.id),
                    ["attr-card"] = new CustomAttribute
                    {
                        id = "attr-card",
                        projectId = "project-defaults",
                        name = "Card",
                        type = AttributeType.Custom,
                        required = true,
                        customTypeId = cardType.id,
                    },
                    ["attr-container"] = new CustomAttribute
                    {
                        id = "attr-container",
                        projectId = "project-defaults",
                        name = "Container",
                        type = AttributeType.Custom,
                        required = true,
                        customTypeId = containerType.id,
                    },
                    ["attr-default-name"] = new StringAttribute
                    {
                        id = "attr-default-name",
                        projectId = "project-defaults",
                        name = "Name",
                        type = AttributeType.String,
                        required = true,
                        defaultValue = new StringAttributeValueBase
                        {
                            value = "Default Name",
                        },
                    },
                    ["attr-inherited-name"] = new StringAttribute
                    {
                        id = "attr-inherited-name",
                        projectId = "project-defaults",
                        name = "Inherited Name",
                        type = AttributeType.String,
                        required = true,
                        defaultValue = new StringAttributeValueBase
                        {
                            value = "Inherited Name",
                        },
                    },
                    ["attr-items"] = new ListAttribute
                    {
                        id = "attr-items",
                        projectId = "project-defaults",
                        name = "Items",
                        type = AttributeType.List,
                        required = true,
                        entryAttributeId = "attr-item-entry",
                    },
                    ["attr-item-entry"] = new CustomAttribute
                    {
                        id = "attr-item-entry",
                        projectId = "project-defaults",
                        name = "Item Entry",
                        type = AttributeType.Custom,
                        required = true,
                        customTypeId = baseItemType.id,
                    },
                    ["attr-children"] = new ListAttribute
                    {
                        id = "attr-children",
                        projectId = "project-defaults",
                        name = "Children",
                        type = AttributeType.List,
                        required = true,
                        entryAttributeId = "attr-list-entry",
                        defaultValue = new ArrayAttributeValueBase
                        {
                            value = new[] { "v-entry-one", "v-entry-two" },
                        },
                    },
                    ["attr-list-entry"] = new StringAttribute
                    {
                        id = "attr-list-entry",
                        projectId = "project-defaults",
                        name = "Entry",
                        type = AttributeType.String,
                        required = true,
                    },
                },
                values = new Dictionary<string, AttributeValue>
                {
                    ["v-root-assets"] = ObjectValue("v-root-assets", rootType.id),
                    ["v-root-save"] = ObjectValue("v-root-save", rootType.id),
                    ["v-root-session"] = ObjectValue("v-root-session", rootType.id),
                    ["v-card"] = ObjectValue("v-card", cardType.id),
                    ["v-container"] = ObjectValue(
                        "v-container",
                        containerType.id,
                        new Dictionary<string, string>
                        {
                            ["Items"] = "v-items",
                        }),
                    ["v-items"] = ArrayValue("v-items", "v-derived-item"),
                    ["v-derived-item"] = ObjectValue("v-derived-item", derivedItemType.id),
                    ["v-entry-one"] = StringValue("v-entry-one", "One"),
                    ["v-entry-two"] = StringValue("v-entry-two", "Two"),
                },
                types = new Dictionary<string, CustomType>
                {
                    [rootType.id] = rootType,
                    [cardType.id] = cardType,
                    [containerType.id] = containerType,
                    [baseItemType.id] = baseItemType,
                    [derivedItemType.id] = derivedItemType,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static CustomAttribute RootAttribute(
            string id,
            string valueId,
            string customTypeId)
        {
            return new CustomAttribute
            {
                id = id,
                projectId = "project-defaults",
                name = id,
                type = AttributeType.Custom,
                required = true,
                valueId = valueId,
                customTypeId = customTypeId,
            };
        }

        private static ObjectAttributeValue ObjectValue(
            string id,
            string typeId,
            Dictionary<string, string>? value = null)
        {
            return new ObjectAttributeValue
            {
                id = id,
                typeId = typeId,
                value = value ?? new Dictionary<string, string>(),
            };
        }

        private static ArrayAttributeValue ArrayValue(string id, params string[] values)
        {
            return new ArrayAttributeValue
            {
                id = id,
                value = values,
            };
        }

        private static StringAttributeValue StringValue(string id, string value)
        {
            return new StringAttributeValue
            {
                id = id,
                value = value,
            };
        }
    }
}
