// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;
using JsonEnum = NeoCompose.Runtime.Json.Enum;

namespace NeoCompose.Tests
{
    public class NeoListIndexTests
    {
        [Test]
        public void ValueIdIndexer_IsLazy_AndMissingContractsAreUnambiguous()
        {
            NeoMemberListWritable node = LoadItems(4, out NeoClient client);
            var items = Wrap(client, node);

            Assert.AreEqual(0, node.IndexDiagnostics.IdentityBuildCount);
            Assert.AreEqual("item-2", items["item-2"].Id);
            Assert.AreEqual(1, node.IndexDiagnostics.IdentityBuildCount);
            Assert.AreEqual(4, node.IndexDiagnostics.IdentityBuildEntryCount);

            Assert.IsTrue(items.ContainsId("item-1"));
            Assert.IsTrue(items.TryGetById("item-3", out TestItem? found));
            Assert.AreEqual("item-3", found!.Id);
            Assert.IsFalse(items.TryGetById("missing", out _));
            Assert.Throws<KeyNotFoundException>(() => _ = items["missing"]);
            Assert.Throws<ArgumentNullException>(() => items.ContainsId(null!));
        }

        [Test]
        public void StringAndEnumIndexes_ReturnUniqueAndManyResults()
        {
            NeoMemberListWritable node = LoadItems(6, out NeoClient client);
            var slug = new NeoUniqueListIndex<string, TestItem>(
                client, node, "Slug", CreateItem);
            var category = new NeoMultiListIndex<string, TestItem>(
                client, node, "Category", CreateItem);

            Assert.AreEqual("item-4", slug["slug-4"]!.Id);
            Assert.IsNull(slug["not-present"]);
            CollectionAssert.AreEqual(
                new[] { "item-0", "item-2", "item-4" },
                category["even"].Select(item => item.Id).ToArray());
            Assert.IsEmpty(category["not-present"]);
            Assert.AreEqual(6, slug.Count);
            Assert.AreEqual(2, category.Count);
        }

        [Test]
        public void TypedIndexes_AreReadOnlyDictionaries_AndSupportLinq()
        {
            NeoMemberListWritable node = LoadItems(6, out NeoClient client);
            var slug = new NeoUniqueListIndex<string, TestItem>(
                client, node, "Slug", CreateItem);
            var category = new NeoMultiListIndex<string, TestItem>(
                client, node, "Category", CreateItem);

            IReadOnlyDictionary<string, TestItem> uniqueDictionary = slug;
            IReadOnlyDictionary<string, IReadOnlyList<TestItem>> manyDictionary = category;

            Assert.AreEqual(
                "item-4",
                slug.FirstOrDefault(entry => entry.Key == "slug-4").Value.Id);
            Assert.AreEqual(
                3,
                category.FirstOrDefault(entry => entry.Key == "even").Value.Count);
            Assert.AreEqual(6, uniqueDictionary.Count);
            Assert.AreEqual(2, manyDictionary.Count);
            Assert.IsTrue(category.TryGetValue("even", out IReadOnlyList<TestItem> even));
            Assert.AreEqual(3, even.Count);
            Assert.Throws<KeyNotFoundException>(() => _ = uniqueDictionary["missing"]);
            Assert.Throws<KeyNotFoundException>(() => _ = manyDictionary["missing"]);
            Assert.IsNull(slug["missing"]);
            Assert.IsEmpty(category["missing"]);
        }

        [Test]
        public void MaterializedIndexes_UpdateAfterIndexedFieldEditAndRemoval()
        {
            NeoMemberListWritable node = LoadItems(5, out NeoClient client);
            var slug = new NeoUniqueListIndex<string, TestItem>(
                client, node, "Slug", CreateItem);
            var category = new NeoMultiListIndex<string, TestItem>(
                client, node, "Category", CreateItem);

            Assert.AreEqual("item-2", slug["slug-2"]!.Id);
            Assert.AreEqual(3, category["even"].Count);
            long builds = node.IndexDiagnostics.DerivedBuildCount;

            var item = (NeoMemberClassWritable)node[2];
            item.Get<NeoMemberStringWritable>("Slug").Set("renamed");
            item.Get<NeoMemberEnumWritable>("Category").Set(new[] { "odd" });

            Assert.IsNull(slug["slug-2"]);
            Assert.AreEqual("item-2", slug["renamed"]!.Id);
            Assert.AreEqual(2, category["even"].Count);
            Assert.AreEqual(3, category["odd"].Count);
            Assert.AreEqual(builds, node.IndexDiagnostics.DerivedBuildCount,
                "a leaf edit should update warm indexes instead of rebuilding them");
            Assert.Greater(node.IndexDiagnostics.DerivedIncrementalUpdateCount, 0);

            node.RemoveById("item-2");
            Assert.IsNull(slug["renamed"]);
            Assert.AreEqual(2, category["odd"].Count);
            Assert.IsFalse(node.ContainsValueId("item-2"));
        }

        [Test]
        public void UniqueCollision_InvalidatesWholeIndex_AndCanBeRepaired()
        {
            NeoMemberListWritable node = LoadItems(
                3,
                out NeoClient client,
                slugFor: i => i < 2 ? "duplicate" : "other");
            var slug = new NeoUniqueListIndex<string, TestItem>(
                client, node, "Slug", CreateItem);

            var invalid = Assert.Throws<InvalidOperationException>(
                () => _ = slug["other"]);
            StringAssert.Contains("duplicate", invalid!.Message);

            ((NeoMemberClassWritable)node[1])
                .Get<NeoMemberStringWritable>("Slug")
                .Set("repaired");

            Assert.AreEqual("item-0", slug["duplicate"]!.Id);
            Assert.AreEqual("item-1", slug["repaired"]!.Id);
        }

        [Test]
        public void WarmLookup_OperationCountsProveConstantWorkAtHundredsOfItems()
        {
            const int itemCount = 500;
            NeoMemberListWritable node = LoadItems(itemCount, out NeoClient client);
            var items = Wrap(client, node);
            var slug = new NeoUniqueListIndex<string, TestItem>(
                client, node, "Slug", CreateItem);

            Assert.AreEqual("item-499", slug["slug-499"]!.Id);
            Assert.AreEqual(1, node.IndexDiagnostics.IdentityBuildCount);
            Assert.AreEqual(itemCount, node.IndexDiagnostics.IdentityBuildEntryCount);
            Assert.AreEqual(1, node.IndexDiagnostics.DerivedBuildCount);
            Assert.AreEqual(itemCount, node.IndexDiagnostics.DerivedBuildEntryCount);

            long identityBuildWork = node.IndexDiagnostics.IdentityBuildEntryCount;
            long derivedBuildWork = node.IndexDiagnostics.DerivedBuildEntryCount;
            for (int i = 0; i < 10_000; i++)
            {
                Assert.IsNotNull(slug["slug-499"]);
                Assert.IsTrue(items.ContainsId("item-499"));
            }

            Assert.AreEqual(identityBuildWork, node.IndexDiagnostics.IdentityBuildEntryCount,
                "warm identity lookup must not rescan List membership");
            Assert.AreEqual(derivedBuildWork, node.IndexDiagnostics.DerivedBuildEntryCount,
                "warm custom-index lookup must not reread indexed fields");
            Assert.GreaterOrEqual(node.IndexDiagnostics.DerivedLookupCount, 10_001);
        }

        [Test]
        public void ListIndexDefinition_DeserializesWithAndWithoutIndexes()
        {
            var indexed = JsonConvert.DeserializeObject<JsonMember>(
                "{\"id\":\"items\",\"projectId\":\"p\",\"name\":\"Items\","
                + "\"kind\":6,\"isStatic\":false,\"accessModifierKind\":\"public\",\"entryMemberId\":\"entry\","
                + "\"indexes\":[{\"schemaKey\":\"Slug\",\"unique\":true}]}"
            ) as ListMember;
            Assert.IsNotNull(indexed);
            Assert.AreEqual("Slug", indexed!.indexes![0].schemaKey);
            Assert.IsTrue(indexed.indexes[0].unique);

            var withoutIndexes = JsonConvert.DeserializeObject<JsonMember>(
                "{\"id\":\"items\",\"projectId\":\"p\",\"name\":\"Items\","
                + "\"kind\":6,\"isStatic\":false,\"accessModifierKind\":\"public\",\"entryMemberId\":\"entry\"}"
            ) as ListMember;
            Assert.IsNotNull(withoutIndexes);
            Assert.IsNull(withoutIndexes!.indexes);
        }

        [Test]
        public void NeoScript_StringKeyOfAndListIndex_ResolveByIdentityAndDeclaredKey()
        {
            LoadItems(4, out NeoClient client);
            var context = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: NeoValueOwnership.Asset);

            object? byId = NSGetterEvaluator.Evaluate(
                Getter(new KeyOfPointer
                {
                    type = PointerKind.KeyOf,
                    keyOf = new KeyOf
                    {
                        pointer = new ReferencePointer
                        {
                            type = PointerKind.Reference,
                            valueId = "items-value",
                        },
                        key = StringLiteral("item-2"),
                    },
                }),
                context);
            Assert.AreEqual("item-2", NSGetterEvaluator.FindRowIdByReference(byId, context));

            object? bySlug = NSGetterEvaluator.Evaluate(
                Getter(new FunctionPointer
                {
                    type = PointerKind.Function,
                    function = new ListIndexFunction
                    {
                        type = FunctionKind.ListIndex,
                        info = new FunctionListIndexInfo
                        {
                            collectionPointer = new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "items-value",
                            },
                            listMemberId = "items",
                            schemaKey = "Slug",
                            unique = true,
                            keyKind = ListIndexKeyKind.String,
                            keyPointer = StringLiteral("slug-3"),
                        },
                    },
                }),
                context);
            Assert.AreEqual("item-3", NSGetterEvaluator.FindRowIdByReference(bySlug, context));

            object? firstBySlug = NSGetterEvaluator.Evaluate(
                Getter(new FunctionPointer
                {
                    type = PointerKind.Function,
                    function = new FirstOrDefaultFunction
                    {
                        type = FunctionKind.FirstOrDefault,
                        info = new FunctionCollectionOptionalBoolInfo
                        {
                            collectionPointer = new FunctionPointer
                            {
                                type = PointerKind.Function,
                                function = new ListIndexFunction
                                {
                                    type = FunctionKind.ListIndex,
                                    info = new FunctionListIndexInfo
                                    {
                                        collectionPointer = new ReferencePointer
                                        {
                                            type = PointerKind.Reference,
                                            valueId = "items-value",
                                        },
                                        listMemberId = "items",
                                        schemaKey = "Slug",
                                        unique = true,
                                        keyKind = ListIndexKeyKind.String,
                                    },
                                },
                            },
                        },
                    },
                }),
                context);
            Assert.AreEqual(
                "item-0",
                NSGetterEvaluator.FindRowIdByReference(firstBySlug, context));
        }

        [Test]
        public void NeoScript_ListIndexFunction_DeserializesFromWireShape()
        {
            Function? function = JsonConvert.DeserializeObject<Function>(
                "{\"type\":\"listIndex\",\"info\":{"
                + "\"collectionPointer\":{\"type\":\"reference\",\"valueId\":\"list\"},"
                + "\"listMemberId\":\"items\",\"schemaKey\":\"Slug\","
                + "\"unique\":true,\"keyKind\":\"string\","
                + "\"keyPointer\":{\"type\":\"value\",\"value\":{"
                + "\"typeInfo\":{\"type\":3,\"required\":true},\"value\":\"wood\"}}}}"
            );
            var listIndex = function as ListIndexFunction;
            Assert.IsNotNull(listIndex);
            Assert.AreEqual("items", listIndex!.info.listMemberId);
            Assert.AreEqual("Slug", listIndex.info.schemaKey);
            Assert.IsTrue(listIndex.info.unique);
        }

        private static FunctionWithReturnType Getter(Pointer pointer)
        {
            return new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = pointer,
                    },
                },
                typeInfo = new UnknownTypeInfo
                {
                    type = MemberKind.Unknown,
                    required = false,
                },
            };
        }

        private static ValuePointer StringLiteral(string value)
        {
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.String,
                        required = true,
                    },
                    value = new JValue(value),
                },
            };
        }

        private static NeoList<TestItem> Wrap(NeoClient client, NeoMemberListWritable node)
        {
            return new NeoList<TestItem>(
                client,
                node,
                CreateItem,
                _ => throw new NotSupportedException("Test wrapper is read-only"));
        }

        private static TestItem CreateItem(NeoClient _, NeoMember node) =>
            new((NeoMemberClass)node);

        private static NeoMemberListWritable LoadItems(
            int count,
            out NeoClient client,
            Func<int, string>? slugFor = null)
        {
            client = NeoTestSaveStack.ClientFromSchema(BuildProjectData(count, slugFor));
            var member = (ListMember)client.ProjectDataForRuntime.members["items"];
            return (NeoMemberListWritable)NeoMember.CreateWritable(
                client,
                member,
                null,
                NeoValueOwnership.Save);
        }

        private static ProjectData BuildProjectData(
            int count,
            Func<int, string>? slugFor)
        {
            const string projectId = "list-index-tests";
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = projectId,
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var itemClass = new NeoSchemaClass
            {
                id = "item-class",
                projectId = projectId,
                name = "Item",
                schema = new Dictionary<string, string>
                {
                    ["Slug"] = "slug",
                    ["Category"] = "category",
                },
            };
            var members = new Dictionary<string, JsonMember>
            {
                ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                ["root-save"] = RootMember("root-save", "root-save-value", rootClass.id),
                ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                ["items"] = new ListMember
                {
                    id = "items",
                    projectId = projectId,
                    name = "Items",
                    kind = MemberKind.List,
                    required = true,
                    valueId = "items-value",
                    entryMemberId = "item-entry",
                    indexes = new[]
                    {
                        new ListIndexDefinition { schemaKey = "Slug", unique = true },
                        new ListIndexDefinition { schemaKey = "Category", unique = false },
                    },
                },
                ["item-entry"] = new ClassMember
                {
                    id = "item-entry",
                    projectId = projectId,
                    name = "Item",
                    kind = MemberKind.Class,
                    required = true,
                    classId = itemClass.id,
                },
                ["slug"] = new StringMember
                {
                    id = "slug",
                    projectId = projectId,
                    name = "Slug",
                    kind = MemberKind.String,
                    required = true,
                    localizable = false,
                },
                ["category"] = new EnumMember
                {
                    id = "category",
                    projectId = projectId,
                    name = "Category",
                    kind = MemberKind.Enum,
                    required = true,
                    enumId = "category-enum",
                    multiselect = false,
                },
            };
            var values = new Dictionary<string, MemberValue>
            {
                ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id, new()),
                ["root-save-value"] = ObjectValue("root-save-value", rootClass.id, new()),
                ["root-session-value"] = ObjectValue("root-session-value", rootClass.id, new()),
            };
            var ids = new string[count];
            for (int i = 0; i < count; i++)
            {
                string id = $"item-{i}";
                string slugId = $"slug-value-{i}";
                string categoryId = $"category-value-{i}";
                ids[i] = id;
                values[id] = ObjectValue(id, itemClass.id, new Dictionary<string, string>
                {
                    ["Slug"] = slugId,
                    ["Category"] = categoryId,
                });
                values[slugId] = new StringMemberValue
                {
                    id = slugId,
                    value = slugFor?.Invoke(i) ?? $"slug-{i}",
                    neoLocalizationMode = NeoStringLocalizationMode.Literal,
                };
                values[categoryId] = new ArrayMemberValue
                {
                    id = categoryId,
                    value = new[] { i % 2 == 0 ? "even" : "odd" },
                };
            }
            values["items-value"] = new ArrayMemberValue
            {
                id = "items-value",
                value = ids,
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = projectId,
                    _id = projectId,
                    name = "List Index Tests",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = members,
                values = values,
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [itemClass.id] = itemClass,
                },
                enums = new Dictionary<string, JsonEnum>
                {
                    ["category-enum"] = new JsonEnum
                    {
                        id = "category-enum",
                        projectId = projectId,
                        name = "Category",
                        options = new Dictionary<string, EnumOption>
                        {
                            ["even"] = new EnumOption { text = "Even" },
                            ["odd"] = new EnumOption { text = "Odd" },
                        },
                        optionKeyOrder = new List<string> { "even", "odd" },
                    },
                },
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
                projectId = "list-index-tests",
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

        private sealed class TestItem
        {
            internal TestItem(NeoMemberClass node)
            {
                Node = node;
            }

            internal NeoMemberClass Node { get; }
            internal string Id => Node.value!.id;
        }
    }
}
