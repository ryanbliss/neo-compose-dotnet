// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonAttribute = NeoCompose.Runtime.Json.Attribute;

namespace NeoCompose.Tests
{
    public class NeoStaticBindingTests
    {
        [Test]
        public void SessionScalar_ReadsAuthoredThenWritesSameStableValueId()
        {
            NeoClient client = BuildClient();
            NeoStaticBinding binding = NeoGeneratedTypesSupport.StaticBinding(
                client,
                "static-count",
                NeoValueOwnership.Session);

            Assert.AreEqual("static-count-authored", binding.ValueId);
            Assert.AreEqual(
                5,
                NeoGeneratedTypesSupport.ReadInt(
                    binding.GetRequiredNode<NeoAttributeInt>()));

            binding.SetValue(NeoGeneratedTypesSupport.Value(9));

            Assert.AreEqual("static-count-authored", binding.ValueId);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                "static-count-authored",
                out NumberAttributeValue? shadow));
            Assert.AreEqual(9, shadow!.value);
            Assert.Throws<ArgumentNullException>(() => binding.Clear());
        }

        [Test]
        public void OptionalSaveScalar_MaterializesBindingPersistsAndBecomesCollectableAfterClear()
        {
            NeoClient client = BuildClient();
            client.LoadValuePartition("scores:rules-type");
            NeoStaticBinding binding = NeoGeneratedTypesSupport.StaticBinding(
                client,
                "static-score",
                NeoValueOwnership.Save);

            Assert.IsNull(binding.ValueId);
            binding.SetValue(NeoGeneratedTypesSupport.Value(12));
            string valueId = binding.ValueId!;
            Assert.IsNotEmpty(valueId);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                valueId,
                out NumberAttributeValue? storedScore));
            Assert.AreEqual("scores:rules-type", storedScore!.mapKey);
            CollectionAssert.DoesNotContain(client.FindUnlinkedSaveValueIds(), valueId);

            ProjectSaveData saved = JsonConvert.DeserializeObject<ProjectSaveData>(
                client.SerializeSaveData())!;
            Assert.AreEqual(valueId, saved.staticBindings["static-score"]);

            binding.Clear();
            Assert.IsNull(binding.ValueId);
            CollectionAssert.Contains(client.FindUnlinkedSaveValueIds(), valueId);
            Assert.GreaterOrEqual(client.RunGarbageCollector(), 1);
            Assert.IsFalse(client.TryGetValue(valueId, out AttributeValue? _));

            saved = JsonConvert.DeserializeObject<ProjectSaveData>(
                client.SerializeSaveData())!;
            Assert.IsTrue(saved.staticBindings.ContainsKey("static-score"));
            Assert.IsNull(saved.staticBindings["static-score"]);
            Assert.IsTrue(binding.RestoreAuthored());
            Assert.IsFalse(binding.RestoreAuthored());
        }

        [Test]
        public void OptionalSessionList_MaterializesOnlyOnFirstMutation()
        {
            NeoClient client = BuildClient();
            NeoStaticBinding binding = NeoGeneratedTypesSupport.StaticBinding(
                client,
                "static-names",
                NeoValueOwnership.Session);

            NeoAttributeListWritable empty =
                binding.GetNodeOrEmpty<NeoAttributeListWritable>();
            Assert.AreEqual(0, empty.Count);
            Assert.IsNull(binding.ValueId, "an empty read must not create a zombie row");

            NeoAttributeListWritable writable =
                binding.GetOrCreateWritableNode<NeoAttributeListWritable>(
                    Array.Empty<string>());
            writable.AddSerialized(NeoGeneratedTypesSupport.Value("Ada"));

            Assert.IsNotNull(binding.ValueId);
            Assert.AreEqual(1, writable.Count);
            Assert.AreEqual(
                "Ada",
                ((NeoAttributeString)writable[0]).value?.value);
        }

        [Test]
        public void TypeSchemaProjection_ExcludesStaticMembersFromInstances()
        {
            NeoClient client = BuildClient();
            IList<CustomType> chain = CustomTypeInheritance.ResolveChain(
                "rules-type",
                id => client.TryGetType(id, out CustomType? type) ? type : null);

            Assert.That(
                CustomTypeInheritance.MergeInstanceSchema(
                    chain,
                    id => client.TryGetAttribute(id, out JsonAttribute? attr)
                        ? attr
                        : null),
                Is.Empty);
            Assert.That(
                CustomTypeInheritance.MergeStaticMembers(
                    chain,
                    id => client.TryGetAttribute(id, out JsonAttribute? attr)
                        ? attr
                        : null),
                Has.Count.EqualTo(3));
        }

        [Test]
        public void AttributeStorageKey_RoundTripsCanonicalWireName()
        {
            var source = new ListAttribute
            {
                id = "partitioned-list",
                projectId = "static-project",
                name = "Partitioned",
                type = AttributeType.List,
                entryAttributeId = "name-entry",
                storageKey = "values:$parentType",
            };

            string json = JsonConvert.SerializeObject(source);
            StringAssert.Contains("\"storageKey\":\"values:$parentType\"", json);
            StringAssert.DoesNotContain("storageMap", json);
            JsonAttribute roundTrip = JsonConvert.DeserializeObject<JsonAttribute>(json)!;

            Assert.AreEqual("values:$parentType", roundTrip.storageKey);
        }

        [Test]
        public void GetterExecution_CanWriteRuntimeStaticMember()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var staticCount = new StaticMemberPointer
            {
                type = PointerKind.StaticMember,
                attributeId = "static-count",
            };
            var getter = new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                typeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.Int,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        target = new WriteTarget
                        {
                            pointer = staticCount,
                            typeInfo = new PrimitiveTypeInfo
                            {
                                type = AttributeType.Int,
                                required = true,
                            },
                            writability = WritabilityKind.Runtime,
                        },
                        operatorValue = "=",
                        pointer = IntPointer(14),
                    },
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = staticCount,
                    },
                },
            };

            Assert.AreEqual(14L, NSGetterEvaluator.Evaluate(getter, ctx));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                "static-count-authored",
                out NumberAttributeValue? value));
            Assert.AreEqual(14, value!.value);
        }

        [TestCase(FunctionKind.Where)]
        [TestCase(FunctionKind.First)]
        [TestCase(FunctionKind.FirstOrDefault)]
        [TestCase(FunctionKind.Select)]
        public void GetterCollectionCallback_UsesSharedWritableExecutor(
            string callbackKind)
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var staticCount = new StaticMemberPointer
            {
                type = PointerKind.StaticMember,
                attributeId = "static-count",
            };
            var intType = new PrimitiveTypeInfo
            {
                type = AttributeType.Int,
                required = true,
            };
            bool select = callbackKind == FunctionKind.Select;
            var predicate = new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                typeInfo = select
                    ? intType
                    : new PrimitiveTypeInfo
                    {
                        type = AttributeType.Bool,
                        required = true,
                    },
                instructions = new Instruction[]
                {
                    new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        target = new WriteTarget
                        {
                            pointer = staticCount,
                            typeInfo = intType,
                            writability = WritabilityKind.Runtime,
                        },
                        operatorValue = "=",
                        pointer = IntPointer(27),
                    },
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = select ? IntPointer(2) : BoolPointer(true),
                    },
                },
            };
            Pointer collection = new ListLiteralPointer
            {
                type = PointerKind.ListLiteral,
                typeInfo = new CollectionTypeInfo
                {
                    type = AttributeType.List,
                    required = true,
                    entryTypeInfo = intType,
                },
                entries = new Pointer[] { IntPointer(1) },
            };
            Function callback = callbackKind switch
            {
                FunctionKind.Where => new WhereFunction
                {
                    type = FunctionKind.Where,
                    info = new FunctionCollectionBoolInfo
                    {
                        collectionPointer = collection,
                        function = predicate,
                    },
                },
                FunctionKind.First => new FirstFunction
                {
                    type = FunctionKind.First,
                    info = new FunctionCollectionOptionalBoolInfo
                    {
                        collectionPointer = collection,
                        function = predicate,
                    },
                },
                FunctionKind.FirstOrDefault => new FirstOrDefaultFunction
                {
                    type = FunctionKind.FirstOrDefault,
                    info = new FunctionCollectionOptionalBoolInfo
                    {
                        collectionPointer = collection,
                        function = predicate,
                    },
                },
                FunctionKind.Select => new SelectFunction
                {
                    type = FunctionKind.Select,
                    info = new FunctionCollectionSelectInfo
                    {
                        collectionPointer = collection,
                        function = predicate,
                    },
                },
                _ => throw new AssertionException(
                    $"Unexpected collection callback '{callbackKind}'."),
            };
            Pointer callbackPointer = new FunctionPointer
            {
                type = PointerKind.Function,
                function = callback,
            };
            Pointer returnPointer = callbackKind is FunctionKind.Where or FunctionKind.Select
                ? new FunctionPointer
                {
                    type = PointerKind.Function,
                    function = new CountFunction
                    {
                        type = FunctionKind.Count,
                        info = new FunctionCollectionInfo
                        {
                            collectionPointer = callbackPointer,
                        },
                    },
                }
                : callbackPointer;
            var getter = new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                typeInfo = intType,
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = returnPointer,
                    },
                },
            };

            Assert.AreEqual(1, NSGetterEvaluator.Evaluate(getter, ctx));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                "static-count-authored",
                out NumberAttributeValue? value));
            Assert.AreEqual(27, value!.value);
        }

        [Test]
        public void CustomConstructor_ReturnValueEscapesAndOptionalNullIsOmitted()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            object? result = NSGetterEvaluator.Evaluate(
                ReturnFunction(ConstructorPointer("Ada", includeOptionalNull: true)),
                ctx);

            Assert.IsNotNull(result);
            string? valueId = NSGetterEvaluator.FindRowIdByReference(result, ctx);
            Assert.IsNotNull(valueId);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                valueId!,
                out ObjectAttributeValue? row));
            Assert.IsTrue(row!.value!.TryGetValue("Name", out string nameValueId));
            Assert.IsFalse(row.value.ContainsKey("Title"));
            Assert.IsTrue(row.value.TryGetValue("Level", out string levelValueId));
            Assert.IsTrue(row.value.TryGetValue("Tags", out string tagsValueId));
            Assert.IsTrue(row.value.TryGetValue("Stats", out string statsValueId));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                nameValueId,
                out StringAttributeValue? name));
            Assert.AreEqual("Ada", name!.value);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                levelValueId,
                out NumberAttributeValue? level));
            Assert.AreEqual(3, level!.value);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                tagsValueId,
                out ArrayAttributeValue? tags));
            Assert.AreEqual("profile:profile-type", tags!.mapKey);
            Assert.That(tags.value, Has.Length.EqualTo(1));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                tags.value![0],
                out StringAttributeValue? tag));
            Assert.AreEqual("starter", tag!.value);
            Assert.AreEqual("profile:profile-type", tag.mapKey);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                statsValueId,
                out ObjectAttributeValue? stats));
            Assert.IsTrue(stats!.value!.TryGetValue("wins", out string winsId));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                winsId,
                out NumberAttributeValue? wins));
            Assert.AreEqual(4, wins!.value);
        }

        [Test]
        public void GeneratedConstructorMaterializer_PreservesNullableCollectionShape()
        {
            NeoClient client = BuildClient();
            var attributes =
                (Dictionary<string, JsonAttribute>)client.attributes;
            var profileSchema =
                ((Dictionary<string, CustomType>)client.types)["profile-type"]
                    .schema!;
            var nestedStatsEntry = new DictionaryAttribute
            {
                id = "profile-nested-stats-entry",
                projectId = "static-project",
                name = "Nested Stats Entry",
                type = AttributeType.Dictionary,
                required = false,
                entryAttributeId = "profile-stat-entry",
            };
            var nestedStats = new ListAttribute
            {
                id = "profile-nested-stats",
                projectId = "static-project",
                name = "Nested Stats",
                type = AttributeType.List,
                required = false,
                entryAttributeId = nestedStatsEntry.id,
            };
            var enumEntry = new EnumAttribute
            {
                id = "profile-enum-entry",
                projectId = "static-project",
                name = "Choice",
                type = AttributeType.Enum,
                required = false,
                enumId = "profile-enum",
                multiselect = false,
            };
            var enumEntries = new ListAttribute
            {
                id = "profile-enum-entries",
                projectId = "static-project",
                name = "Choices",
                type = AttributeType.List,
                required = false,
                entryAttributeId = enumEntry.id,
            };
            attributes[nestedStatsEntry.id] = nestedStatsEntry;
            attributes[nestedStats.id] = nestedStats;
            attributes[enumEntry.id] = enumEntry;
            attributes[enumEntries.id] = enumEntries;
            ((Dictionary<string, NeoCompose.Runtime.Json.Enum>)client.enums)[
                "profile-enum"] = new NeoCompose.Runtime.Json.Enum
                {
                    id = "profile-enum",
                    projectId = "static-project",
                    name = "Profile Enum",
                    options = new Dictionary<string, EnumOption>
                    {
                        ["ready"] = new EnumOption { text = "Ready" },
                    },
                    optionKeyOrder = new List<string> { "ready" },
                };
            profileSchema["NestedStats"] = nestedStats.id;
            profileSchema["Choices"] = enumEntries.id;

            NeoAttributeCustomWritable profile =
                NeoGeneratedTypesSupport.CreateWritableCustomValue(
                    client,
                    "profile-type",
                    new NeoGeneratedConstructorValue(
                        "Name",
                        "profile-name",
                        "Ada"),
                    new NeoGeneratedConstructorValue(
                        "Tags",
                        "profile-tags",
                        new string?[] { null, "ready" }),
                    new NeoGeneratedConstructorValue(
                        "Stats",
                        "profile-stats",
                        new Dictionary<string, int?>
                        {
                            ["empty"] = null,
                            ["wins"] = 7,
                        }),
                    new NeoGeneratedConstructorValue(
                        "NestedStats",
                        nestedStats.id,
                        new object?[]
                        {
                            new GenericOnlyReadOnlyDictionary<int?>(
                                new Dictionary<string, int?>
                                {
                                    ["empty"] = null,
                                    ["losses"] = 2,
                                }),
                            null,
                        }),
                    new NeoGeneratedConstructorValue(
                        "Choices",
                        enumEntries.id,
                        new object?[]
                        {
                            null,
                            new TestEnumOption("ready"),
                        }));

            string tagsId = profile.value!.value!["Tags"];
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                tagsId,
                out ArrayAttributeValue? tags));
            Assert.That(tags!.value, Has.Length.EqualTo(2));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                tags.value![0],
                out StringAttributeValue? emptyTag));
            Assert.IsNull(emptyTag!.value);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                tags.value[1],
                out StringAttributeValue? readyTag));
            Assert.AreEqual("ready", readyTag!.value);

            string statsId = profile.value.value["Stats"];
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                statsId,
                out ObjectAttributeValue? stats));
            CollectionAssert.AreEquivalent(
                new[] { "empty", "wins" },
                stats!.value!.Keys);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                stats.value["empty"],
                out NumberAttributeValue? emptyStat));
            Assert.IsNull(emptyStat!.value);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                stats.value["wins"],
                out NumberAttributeValue? wins));
            Assert.AreEqual(7, wins!.value);

            string nestedStatsId = profile.value.value["NestedStats"];
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                nestedStatsId,
                out ArrayAttributeValue? nestedStatsRow));
            Assert.That(nestedStatsRow!.value, Has.Length.EqualTo(2));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                nestedStatsRow.value![0],
                out ObjectAttributeValue? genericDictionaryRow));
            Assert.IsTrue(genericDictionaryRow!.value!.ContainsKey("empty"));
            Assert.IsTrue(genericDictionaryRow.value.ContainsKey("losses"));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                genericDictionaryRow.value["empty"],
                out NumberAttributeValue? nestedEmpty));
            Assert.IsNull(nestedEmpty!.value);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                nestedStatsRow.value[1],
                out ObjectAttributeValue? nullDictionaryRow));
            Assert.IsNull(nullDictionaryRow!.value);

            string choicesId = profile.value.value["Choices"];
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                choicesId,
                out ArrayAttributeValue? choicesRow));
            Assert.That(choicesRow!.value, Has.Length.EqualTo(2));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                choicesRow.value![0],
                out ArrayAttributeValue? nullEnumRow));
            Assert.IsNull(nullEnumRow!.value);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                choicesRow.value[1],
                out ArrayAttributeValue? enumRow));
            CollectionAssert.AreEqual(
                new[] { "ready" },
                enumRow!.value);
        }

        [Test]
        public void GeneratedConstructorMaterializer_UnorderedListUsesContainerMembership()
        {
            NeoClient client = BuildClient();
            var attributes =
                (Dictionary<string, JsonAttribute>)client.attributes;
            var profileSchema =
                ((Dictionary<string, CustomType>)client.types)["profile-type"]
                    .schema!;
            var unorderedTags = new ListAttribute
            {
                id = "profile-unordered-tags",
                projectId = "static-project",
                name = "Unordered Tags",
                type = AttributeType.List,
                required = false,
                entryAttributeId = "profile-tag-entry",
                listKind = NeoListKinds.Unordered,
            };
            attributes[unorderedTags.id] = unorderedTags;
            profileSchema["UnorderedTags"] = unorderedTags.id;

            NeoAttributeCustomWritable profile =
                NeoGeneratedTypesSupport.CreateWritableCustomValue(
                    client,
                    "profile-type",
                    new NeoGeneratedConstructorValue(
                        "Name",
                        "profile-name",
                        "Ada"),
                    new NeoGeneratedConstructorValue(
                        "Tags",
                        "profile-tags",
                        new[] { "ordered-a", "ordered-b" }),
                    new NeoGeneratedConstructorValue(
                        "UnorderedTags",
                        unorderedTags.id,
                        new string?[] { "unordered-a", null, "unordered-b" }));

            string orderedListId = profile.value!.value!["Tags"];
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                orderedListId,
                out ArrayAttributeValue? orderedList));
            Assert.That(orderedList!.value, Has.Length.EqualTo(2));
            foreach (string orderedEntryId in orderedList.value!)
            {
                Assert.IsTrue(client.TryGetValue(
                    NeoValueOwnership.Session,
                    orderedEntryId,
                    out StringAttributeValue? orderedEntry));
                Assert.IsNull(orderedEntry!.containerId);
            }

            string unorderedListId = profile.value.value["UnorderedTags"];
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                unorderedListId,
                out ArrayAttributeValue? unorderedList));
            CollectionAssert.IsEmpty(unorderedList!.value!);
            var unorderedValues = new List<string?>();
            foreach (string unorderedEntryId in
                     client.GetUnorderedListEntryIds(unorderedListId))
            {
                Assert.IsTrue(client.TryGetValue(
                    NeoValueOwnership.Session,
                    unorderedEntryId,
                    out StringAttributeValue? unorderedEntry));
                Assert.AreEqual(unorderedListId, unorderedEntry!.containerId);
                unorderedValues.Add(unorderedEntry.value);
            }
            CollectionAssert.AreEquivalent(
                new string?[] { "unordered-a", null, "unordered-b" },
                unorderedValues);
        }

        [Test]
        public void GeneratedConstructorMaterializer_UnorderedCustomMemberAttachesByIdentity()
        {
            NeoClient client = BuildClient();
            var attributes =
                (Dictionary<string, JsonAttribute>)client.attributes;
            var profileSchema =
                ((Dictionary<string, CustomType>)client.types)["profile-type"]
                    .schema!;
            var childEntry = new CustomAttribute
            {
                id = "profile-child-entry",
                projectId = "static-project",
                name = "Child",
                type = AttributeType.Custom,
                required = true,
                customTypeId = "owned-child-type",
            };
            var children = new ListAttribute
            {
                id = "profile-children",
                projectId = "static-project",
                name = "Children",
                type = AttributeType.List,
                required = false,
                entryAttributeId = childEntry.id,
                listKind = NeoListKinds.Unordered,
            };
            attributes[childEntry.id] = childEntry;
            attributes[children.id] = children;
            profileSchema["Children"] = children.id;
            NeoAttributeCustomWritable child = CreateOwnedChild(client, "member");
            string childId = child.value!.id;

            NeoAttributeCustomWritable profile =
                NeoGeneratedTypesSupport.CreateWritableCustomValue(
                    client,
                    "profile-type",
                    new NeoGeneratedConstructorValue(
                        "Name",
                        "profile-name",
                        "Ada"),
                    new NeoGeneratedConstructorValue(
                        "Children",
                        children.id,
                        new[] { new TestValueReference(childId) }));

            string listId = profile.value!.value!["Children"];
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                listId,
                out ArrayAttributeValue? list));
            CollectionAssert.IsEmpty(list!.value!);
            CollectionAssert.AreEqual(
                new[] { childId },
                client.GetUnorderedListEntryIds(listId));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                childId,
                out ObjectAttributeValue? attachedChild));
            Assert.AreEqual(listId, attachedChild!.containerId);
        }

        [Test]
        public void CustomConstructor_RuntimeMismatchStillEvaluatesLaterArguments()
        {
            NeoClient client = BuildClient();
            bool laterArgumentEvaluated = false;
            var effect = new FunctionAttribute
            {
                id = "static-constructor-effect",
                projectId = "static-project",
                name = "Constructor Effect",
                type = AttributeType.Function,
                required = false,
                isStatic = true,
                returnTypeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.String,
                    required = true,
                },
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                deferred = false,
            };
            ((Dictionary<string, JsonAttribute>)client.attributes)[effect.id] =
                effect;
            client.RegisterNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    [effect.id] = (_, _, _) =>
                    {
                        laterArgumentEvaluated = true;
                        return "late";
                    },
                });
            var constructor = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CustomConstructorFunction
                {
                    type = FunctionKind.CustomConstructor,
                    info = new FunctionCustomConstructorInfo
                    {
                        customTypeInfo = ProfileType(),
                        fields = new[]
                        {
                            new FunctionCustomConstructorField
                            {
                                schemaKey = "Name",
                                attributeId = "profile-name",
                                // Wrong at runtime, but pointer evaluation is
                                // still side-effect free and does not validate
                                // the materialized row yet.
                                valuePointer = IntPointer(42),
                            },
                            new FunctionCustomConstructorField
                            {
                                schemaKey = "Title",
                                attributeId = "profile-title",
                                valuePointer = new CallFunctionPointer
                                {
                                    type = PointerKind.CallFunction,
                                    attributeId = effect.id,
                                    receiver = new CallReceiver
                                    {
                                        kind = CallReceiverKind.Static,
                                        attributeId = effect.id,
                                    },
                                    args = Array.Empty<Pointer>(),
                                    callSiteId = "constructor-later-effect",
                                },
                            },
                        },
                    },
                },
            };
            int rowsBefore = client.sessionValues.Count;

            Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(constructor),
                    new NSGetterEvaluator.Context(client, null, null)));

            Assert.IsTrue(laterArgumentEvaluated);
            Assert.AreEqual(
                rowsBefore,
                client.sessionValues.Count,
                "Remaining Session rows: " + DescribeSessionRows(client));
        }

        [Test]
        public void CustomConstructor_ReturnedNestedCollectionEscapesWholeAllocationGroup()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var getter = new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                typeInfo = new CollectionTypeInfo
                {
                    type = AttributeType.List,
                    required = true,
                    entryTypeInfo = new PrimitiveTypeInfo
                    {
                        type = AttributeType.String,
                        required = true,
                    },
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = KeyOf(
                            ConstructorPointer("Ada", includeOptionalNull: false),
                            "Tags"),
                    },
                },
            };

            object? result = NSGetterEvaluator.Evaluate(getter, ctx);

            Assert.IsNotNull(result);
            string tagsId = NSGetterEvaluator.FindRowIdByReference(result, ctx)!;
            Assert.IsNotNull(tagsId);
            Assert.IsTrue(client.TryFindOwnedParent(
                NeoValueOwnership.Session,
                tagsId,
                out string? profileId));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                tagsId,
                out ArrayAttributeValue? _));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                profileId!,
                out ObjectAttributeValue? _));
        }

        [Test]
        public void CustomConstructor_ReturnedNestedCustomEscapesOuterAllocationGroup()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            FunctionPointer parent = OwnedParentConstructorPointer(
                OwnedChildConstructorPointer("nested"));
            var getter = new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                typeInfo = OwnedChildType(),
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = KeyOf(parent, "Child"),
                    },
                },
            };

            object? result = NSGetterEvaluator.Evaluate(getter, ctx);

            Assert.IsNotNull(result);
            string childId = NSGetterEvaluator.FindRowIdByReference(result, ctx)!;
            Assert.IsNotNull(childId);
            Assert.IsTrue(client.TryFindOwnedParent(
                NeoValueOwnership.Session,
                childId,
                out string? parentId));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                childId,
                out ObjectAttributeValue? _));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                parentId!,
                out ObjectAttributeValue? _));
        }

        [Test]
        public void CustomConstructor_MissingRequiredFieldIsRejected()
        {
            NeoClient client = BuildClient();
            var pointer = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CustomConstructorFunction
                {
                    type = FunctionKind.CustomConstructor,
                    info = new FunctionCustomConstructorInfo
                    {
                        customTypeInfo = ProfileType(),
                        fields = Array.Empty<FunctionCustomConstructorField>(),
                    },
                },
            };

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(pointer),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("missing required field 'Name'/'profile-name'", error.Message);
        }

        [Test]
        public void CustomConstructor_DuplicateFieldIsRejectedWithoutPublishingRows()
        {
            NeoClient client = BuildClient();
            int before = client.sessionValues.Count;
            var pointer = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CustomConstructorFunction
                {
                    type = FunctionKind.CustomConstructor,
                    info = new FunctionCustomConstructorInfo
                    {
                        customTypeInfo = ProfileType(),
                        fields = new[]
                        {
                            new FunctionCustomConstructorField
                            {
                                schemaKey = "Name",
                                attributeId = "profile-name",
                                valuePointer = StringPointer("Ada"),
                            },
                            new FunctionCustomConstructorField
                            {
                                schemaKey = "Name",
                                attributeId = "profile-name",
                                valuePointer = StringPointer("Grace"),
                            },
                        },
                    },
                },
            };

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(pointer),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("duplicate field 'Name'", error.Message);
            Assert.AreEqual(before, client.sessionValues.Count);
        }

        [Test]
        public void CustomConstructor_StaleMetadataFailsBeforeArgumentSideEffects()
        {
            NeoClient client = BuildClient();
            int calls = 0;
            client.RegisterNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    ["static-consume"] = (_, _, _) =>
                    {
                        calls++;
                        return "side effect";
                    },
                });
            var pointer = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CustomConstructorFunction
                {
                    type = FunctionKind.CustomConstructor,
                    info = new FunctionCustomConstructorInfo
                    {
                        customTypeInfo = ProfileType(),
                        fields = new[]
                        {
                            new FunctionCustomConstructorField
                            {
                                schemaKey = "Name",
                                attributeId = "profile-name",
                                valuePointer = StringPointer("Ada"),
                            },
                            new FunctionCustomConstructorField
                            {
                                schemaKey = "LegacyTitle",
                                attributeId = "profile-title",
                                valuePointer = new CallFunctionPointer
                                {
                                    type = PointerKind.CallFunction,
                                    attributeId = "static-consume",
                                    receiver = new CallReceiver
                                    {
                                        kind = CallReceiverKind.Static,
                                        attributeId = "static-consume",
                                    },
                                    args = new Pointer[]
                                    {
                                        ConstructorPointer(
                                            "Nested",
                                            includeOptionalNull: false),
                                    },
                                    callSiteId = "stale-constructor-side-effect",
                                },
                            },
                        },
                    },
                },
            };

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(pointer),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("contains stale field", error.Message);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void CustomConstructor_ImmutableOnlyTypeIsRejectedWithoutPublishingRows()
        {
            NeoClient client = BuildClient();
            ((Dictionary<string, CustomType>)client.types)["profile-type"]
                .allowedStorage = "immutable";
            int before = client.sessionValues.Count;

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(ConstructorPointer(
                        "Ada",
                        includeOptionalNull: false)),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("immutable-only Custom type 'Profile'", error.Message);
            Assert.AreEqual(before, client.sessionValues.Count);
        }

        [Test]
        public void GeneratedConstructor_RuntimeShapeFailureIsAtomic()
        {
            NeoClient client = BuildClient();
            int before = client.sessionValues.Count;
            var wrongShape = new NumberAttributeValue
            {
                id = "wrong-profile-name-shape",
                value = 17,
            };

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.CreateWritableCustomValue(
                    client,
                    "profile-type",
                    new Dictionary<string, string>
                    {
                        ["Name"] = wrongShape.id,
                    },
                    new AttributeValue[] { wrongShape }))!;

            StringAssert.Contains("incompatible with schema attribute 'profile-name'", error.Message);
            Assert.AreEqual(before, client.sessionValues.Count);
            Assert.IsFalse(client.HasWritableValue(
                NeoValueOwnership.Session,
                wrongShape.id));
        }

        [Test]
        public void GeneratedConstructor_OrphanStagedRowFailureIsAtomic()
        {
            NeoClient client = BuildClient();
            int before = client.sessionValues.Count;
            var name = new StringAttributeValue
            {
                id = "reachable-profile-name",
                value = "Ada",
            };
            var orphan = new StringAttributeValue
            {
                id = "orphan-constructor-row",
                value = "unreachable",
            };

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.CreateWritableCustomValue(
                    client,
                    "profile-type",
                    new Dictionary<string, string>
                    {
                        ["Name"] = name.id,
                    },
                    new AttributeValue[] { name, orphan }))!;

            StringAssert.Contains("orphan staged row 'orphan-constructor-row'", error.Message);
            Assert.AreEqual(before, client.sessionValues.Count);
            Assert.IsFalse(client.HasWritableValue(
                NeoValueOwnership.Session,
                name.id));
            Assert.IsFalse(client.HasWritableValue(
                NeoValueOwnership.Session,
                orphan.id));
        }

        [Test]
        public void CustomConstructor_NestedCustomAttachesParentlessSessionIdentity()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            FunctionPointer childConstructor = OwnedChildConstructorPointer("nested");
            var parentConstructor = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CustomConstructorFunction
                {
                    type = FunctionKind.CustomConstructor,
                    info = new FunctionCustomConstructorInfo
                    {
                        customTypeInfo = OwnedParentType(),
                        fields = new[]
                        {
                            new FunctionCustomConstructorField
                            {
                                schemaKey = "Child",
                                attributeId = "owned-parent-child",
                                valuePointer = childConstructor,
                            },
                        },
                    },
                },
            };
            var getter = new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                typeInfo = OwnedParentType(),
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = parentConstructor,
                    },
                },
            };

            object? result = NSGetterEvaluator.Evaluate(getter, ctx);
            string parentId = NSGetterEvaluator.FindRowIdByReference(result, ctx)!;
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                parentId,
                out ObjectAttributeValue? parent),
                $"Returned parent '{parentId}' was collected; Session rows: {string.Join(", ", client.sessionValues.Keys)}");
            string childId = parent!.value!["Child"];
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                childId,
                out ObjectAttributeValue? child),
                $"Nested child '{childId}' was collected; Session rows: {string.Join(", ", client.sessionValues.Keys)}");
            Assert.AreEqual("nested", ReadOwnedChildValue(client, child!));
        }

        [Test]
        public void GeneratedConstructor_ParentlessSessionAttachesIdentityButSecondParentRequiresClone()
        {
            NeoClient client = BuildClient();
            NeoAttributeCustomWritable child = CreateOwnedChild(client, "shared");
            string childId = child.value!.id;

            NeoAttributeCustomWritable firstParent = CreateOwnedParent(
                client,
                childId);
            Assert.AreEqual(childId, firstParent.value!.value!["Child"]);
            int beforeSecondParent = client.sessionValues.Count;

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CreateOwnedParent(client, childId))!;

            StringAssert.Contains("already owned by parent value", error.Message);
            StringAssert.Contains("Clone()", error.Message);
            Assert.AreEqual(beforeSecondParent, client.sessionValues.Count);
        }

        [Test]
        public void GeneratedConstructor_SessionStaticBindingCountsAsAnOwnedParent()
        {
            NeoClient client = BuildClient();
            NeoAttributeCustomWritable child = CreateOwnedChild(client, "static");
            string childId = child.value!.id;
            RegisterOwnedChildStatic(
                client,
                "static-owned-child-session",
                NeoValueOwnership.Session);
            client.SetStaticBinding(
                "static-owned-child-session",
                NeoValueOwnership.Session,
                childId);
            int before = client.sessionValues.Count;

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CreateOwnedParent(client, childId))!;

            StringAssert.Contains(
                "already owned by parent value 'static:static-owned-child-session'",
                error.Message);
            StringAssert.Contains("Clone()", error.Message);
            Assert.AreEqual(before, client.sessionValues.Count);
        }

        [Test]
        public void GeneratedConstructor_SaveStaticBindingClonesAggregateIntoSession()
        {
            NeoClient client = BuildClient();
            const string childId = "save-static-child";
            const string valueId = "save-static-child-value";
            SeedOwnedChild(
                client,
                NeoValueOwnership.Save,
                childId,
                valueId,
                "save-static");
            RegisterOwnedChildStatic(
                client,
                "static-owned-child-save",
                NeoValueOwnership.Save);
            client.SetStaticBinding(
                "static-owned-child-save",
                NeoValueOwnership.Save,
                childId);

            NeoAttributeCustomWritable parent = CreateOwnedParent(client, childId);

            string importedChildId = parent.value!.value!["Child"];
            Assert.AreNotEqual(childId, importedChildId);
            Assert.IsTrue(client.HasWritableValue(
                NeoValueOwnership.Session,
                importedChildId));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                importedChildId,
                out ObjectAttributeValue? importedChild));
            Assert.AreEqual("save-static", ReadOwnedChildValue(client, importedChild!));
            Assert.AreNotEqual(valueId, importedChild!.value!["Value"]);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                childId,
                out ObjectAttributeValue? source));
            Assert.IsNotNull(source);
            Assert.IsTrue(client.TryResolveStaticBinding(
                "static-owned-child-save",
                out _,
                out NeoValueOwnership bindingOwnership,
                out string? bindingValueId));
            Assert.AreEqual(NeoValueOwnership.Save, bindingOwnership);
            Assert.AreEqual(childId, bindingValueId);
        }

        [TestCase(NeoValueOwnership.Asset)]
        [TestCase(NeoValueOwnership.Save)]
        public void GeneratedConstructor_CrossStoreAggregateClonesFreshGraphIntoSession(
            NeoValueOwnership sourceOwnership)
        {
            NeoClient client = BuildClient();
            const string childId = "cross-store-child";
            const string valueId = "cross-store-child-value";
            SeedOwnedChild(
                client,
                sourceOwnership,
                childId,
                valueId,
                sourceOwnership.ToString());

            NeoAttributeCustomWritable parent = CreateOwnedParent(client, childId);

            string importedChildId = parent.value!.value!["Child"];
            Assert.AreNotEqual(childId, importedChildId);
            Assert.IsTrue(client.HasWritableValue(
                NeoValueOwnership.Session,
                importedChildId));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                importedChildId,
                out ObjectAttributeValue? importedChild));
            Assert.AreNotEqual(valueId, importedChild!.value!["Value"]);
            Assert.AreEqual(
                sourceOwnership.ToString(),
                ReadOwnedChildValue(client, importedChild!));
            Assert.IsTrue(client.TryGetValue(
                sourceOwnership,
                childId,
                out ObjectAttributeValue? sourceChild));
            Assert.IsNotNull(sourceChild);
        }

        [Test]
        public void GeneratedConstructor_SaveOwnedChildRemainsIndependentAfterParentPromotion()
        {
            NeoClient client = BuildClient();
            const string sourceChildId = "promote-source-child";
            const string sourceValueId = "promote-source-value";
            SeedOwnedChild(
                client,
                NeoValueOwnership.Save,
                sourceChildId,
                sourceValueId,
                "source");
            RegisterOwnedChildStatic(
                client,
                "static-promote-source",
                NeoValueOwnership.Save);
            client.SetStaticBinding(
                "static-promote-source",
                NeoValueOwnership.Save,
                sourceChildId);

            NeoAttributeCustomWritable stagedParent = CreateOwnedParent(
                client,
                sourceChildId);
            string stagedParentId = stagedParent.value!.id;
            string stagedChildId = stagedParent.value.value!["Child"];
            Assert.AreNotEqual(sourceChildId, stagedChildId);

            string savedParentId = client.ImportValueReference(
                NeoValueOwnership.Save,
                stagedParentId,
                out bool sourceMoved);

            Assert.IsTrue(sourceMoved);
            Assert.AreEqual(stagedParentId, savedParentId);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                savedParentId,
                out ObjectAttributeValue? savedParent));
            string savedChildId = savedParent!.value!["Child"];
            Assert.AreEqual(stagedChildId, savedChildId);
            Assert.AreNotEqual(sourceChildId, savedChildId);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                savedChildId,
                out ObjectAttributeValue? savedChild));
            Assert.AreNotEqual(sourceValueId, savedChild!.value!["Value"]);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                sourceChildId,
                out ObjectAttributeValue? sourceChild));
            Assert.AreEqual(sourceValueId, sourceChild!.value!["Value"]);
            Assert.IsTrue(client.TryFindOwnedParent(
                NeoValueOwnership.Save,
                sourceChildId,
                out string? sourceParent));
            Assert.AreEqual("static:static-promote-source", sourceParent);
            Assert.IsTrue(client.TryFindOwnedParent(
                NeoValueOwnership.Save,
                savedChildId,
                out string? destinationParent));
            Assert.AreEqual(savedParentId, destinationParent);
        }

        [Test]
        public void ImportValueReference_CollidingSessionDescendantClonesWholeGraph()
        {
            NeoClient client = BuildClient();
            const string childId = "legacy-collision-child";
            const string childValueId = "legacy-collision-value";
            const string parentId = "legacy-collision-parent";
            SeedOwnedChild(
                client,
                NeoValueOwnership.Save,
                childId,
                childValueId,
                "save-source");
            RegisterOwnedChildStatic(
                client,
                "static-legacy-collision",
                NeoValueOwnership.Save);
            client.SetStaticBinding(
                "static-legacy-collision",
                NeoValueOwnership.Save,
                childId);
            SeedOwnedChild(
                client,
                NeoValueOwnership.Session,
                childId,
                childValueId,
                "session-copy");
            client.SetWritableValue(
                NeoValueOwnership.Session,
                new ObjectAttributeValue
                {
                    id = parentId,
                    typeId = "owned-parent-type",
                    value = new Dictionary<string, string>
                    {
                        ["Child"] = childId,
                    },
                });

            string importedParentId = client.ImportValueReference(
                NeoValueOwnership.Save,
                parentId,
                out bool sourceMoved);

            Assert.IsFalse(sourceMoved);
            Assert.AreNotEqual(parentId, importedParentId);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                importedParentId,
                out ObjectAttributeValue? importedParent));
            string importedChildId = importedParent!.value!["Child"];
            Assert.AreNotEqual(childId, importedChildId);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                importedChildId,
                out ObjectAttributeValue? importedChild));
            Assert.AreEqual("session-copy", ReadOwnedChildValue(
                client,
                importedChild!,
                NeoValueOwnership.Save));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                childId,
                out ObjectAttributeValue? sourceChild));
            Assert.AreEqual(childValueId, sourceChild!.value!["Value"]);
        }

        [Test]
        public void ImportValueReference_ParentedSessionDescendantClonesInsteadOfMoving()
        {
            NeoClient client = BuildClient();
            NeoAttributeCustomWritable child = CreateOwnedChild(
                client,
                "session-child");
            string childId = child.value!.id;
            NeoAttributeCustomWritable parent = CreateOwnedParent(client, childId);
            string parentId = parent.value!.id;

            string importedChildId = client.ImportValueReference(
                NeoValueOwnership.Save,
                childId,
                out bool sourceMoved);

            Assert.IsFalse(sourceMoved);
            Assert.AreNotEqual(childId, importedChildId);
            Assert.IsTrue(client.TryFindOwnedParent(
                NeoValueOwnership.Session,
                childId,
                out string? retainedParentId));
            Assert.AreEqual(parentId, retainedParentId);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                childId,
                out ObjectAttributeValue? retainedChild));
            Assert.AreEqual("session-child", ReadOwnedChildValue(
                client,
                retainedChild!));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                importedChildId,
                out ObjectAttributeValue? importedChild));
            Assert.AreEqual("session-child", ReadOwnedChildValue(
                client,
                importedChild!,
                NeoValueOwnership.Save));
        }

        [Test]
        public void CustomConstructor_SameStableIdUsesExactSourceOwnership()
        {
            NeoClient client = BuildClient();
            const string sharedChildId = "shared-store-child";
            SeedOwnedChild(
                client,
                NeoValueOwnership.Session,
                sharedChildId,
                "shared-session-value",
                "session");
            SeedOwnedChild(
                client,
                NeoValueOwnership.Save,
                sharedChildId,
                "shared-save-value",
                "save");
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                sharedChildId,
                out ObjectAttributeValue? saveChild));
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            object? exactSaveReference = NSGetterEvaluator.UnwrapRow(
                saveChild!,
                ctx,
                NeoValueOwnership.Save);
            ctx = ctx.WithThis(exactSaveReference);
            var getter = new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                typeInfo = OwnedParentType(),
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = OwnedParentConstructorPointer(
                            new VariablePointer
                            {
                                type = PointerKind.Variable,
                                variableId = "__this__",
                            }),
                    },
                },
            };

            object? result = NSGetterEvaluator.Evaluate(getter, ctx);

            string parentId = NSGetterEvaluator.FindRowIdByReference(result, ctx)!;
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                parentId,
                out ObjectAttributeValue? constructedParent));
            string importedChildId = constructedParent!.value!["Child"];
            Assert.AreNotEqual(sharedChildId, importedChildId);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                importedChildId,
                out ObjectAttributeValue? importedChild));
            Assert.AreEqual("save", ReadOwnedChildValue(client, importedChild!));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                sharedChildId,
                out ObjectAttributeValue? sessionChild));
            Assert.AreEqual("session", ReadOwnedChildValue(client, sessionChild!));
        }

        [Test]
        public void CloneOwnedValueReference_CycleAfterPublishedSiblingRollsBackAtomically()
        {
            NeoClient client = BuildClient();
            var attributes =
                (Dictionary<string, JsonAttribute>)client.attributes;
            var types = (Dictionary<string, CustomType>)client.types;
            var rootType = new CustomType
            {
                id = "atomic-root-type",
                projectId = "static-project",
                name = "AtomicRoot",
                schema = new Dictionary<string, string>
                {
                    ["Good"] = "atomic-good-child",
                    ["Loop"] = "atomic-loop-child",
                },
            };
            var goodAttribute = new CustomAttribute
            {
                id = "atomic-good-child",
                projectId = "static-project",
                name = "Good",
                type = AttributeType.Custom,
                required = true,
                customTypeId = "owned-child-type",
            };
            var loopAttribute = new CustomAttribute
            {
                id = "atomic-loop-child",
                projectId = "static-project",
                name = "Loop",
                type = AttributeType.Custom,
                required = true,
                customTypeId = rootType.id,
            };
            var rootAttribute = new CustomAttribute
            {
                id = "atomic-root",
                projectId = "static-project",
                name = "Atomic Root",
                type = AttributeType.Custom,
                required = true,
                customTypeId = rootType.id,
            };
            attributes[goodAttribute.id] = goodAttribute;
            attributes[loopAttribute.id] = loopAttribute;
            attributes[rootAttribute.id] = rootAttribute;
            types[rootType.id] = rootType;
            SeedOwnedChild(
                client,
                NeoValueOwnership.Save,
                "atomic-good-row",
                "atomic-good-value",
                "published-before-cycle");
            client.SetWritableValue(
                NeoValueOwnership.Save,
                new ObjectAttributeValue
                {
                    id = "atomic-root-row",
                    typeId = rootType.id,
                    value = new Dictionary<string, string>
                    {
                        ["Good"] = "atomic-good-row",
                        ["Loop"] = "atomic-root-row",
                    },
                });
            var before = new HashSet<string>(client.sessionValues.Keys);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                client.CloneOwnedValueReferenceForNewParent(
                    NeoValueOwnership.Session,
                    NeoValueOwnership.Save,
                    "atomic-root-row",
                    rootAttribute))!;

            StringAssert.Contains("cycle", error.Message);
            CollectionAssert.AreEquivalent(before, client.sessionValues.Keys);
        }

        [Test]
        public void GeneratedConstructor_ImportedStampFailureRollsBackFreshClone()
        {
            NeoClient client = BuildClient();
            const string childId = "bad-partition-child";
            const string valueId = "bad-partition-value";
            SeedOwnedChild(
                client,
                NeoValueOwnership.Save,
                childId,
                valueId,
                "bad-partition");
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                valueId,
                out StringAttributeValue? sourceValue));
            sourceValue!.mapKey = "unexpected-partition";
            client.SetWritableValue(NeoValueOwnership.Save, sourceValue);
            var before = new HashSet<string>(client.sessionValues.Keys);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CreateOwnedParent(client, childId))!;

            StringAssert.Contains("partition 'unexpected-partition'", error.Message);
            CollectionAssert.AreEquivalent(before, client.sessionValues.Keys);
        }

        [Test]
        public void GeneratedConstructor_IncompatibleCustomRuntimeTypeIsRejectedAtomically()
        {
            NeoClient client = BuildClient();
            var wrong = new ObjectAttributeValue
            {
                id = "wrong-custom-runtime-type",
                typeId = "profile-type",
                value = new Dictionary<string, string>(),
            };
            ((Dictionary<string, AttributeValue>)client.values)[wrong.id] = wrong;
            int before = client.sessionValues.Count;

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CreateOwnedParent(client, wrong.id))!;

            StringAssert.Contains("expects 'owned-child-type'", error.Message);
            StringAssert.Contains("runtime type 'profile-type'", error.Message);
            Assert.AreEqual(before, client.sessionValues.Count);
        }

        [Test]
        public void CustomConstructor_ClosedNamedGenericSubstitutesAndStampsCollections()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            CustomTypeInfo resultType = ClosedStringBoxType();
            var constructor = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CustomConstructorFunction
                {
                    type = FunctionKind.CustomConstructor,
                    info = new FunctionCustomConstructorInfo
                    {
                        customTypeInfo = resultType,
                        fields = new[]
                        {
                            new FunctionCustomConstructorField
                            {
                                schemaKey = "Values",
                                attributeId = "generic-values",
                                valuePointer = new ListLiteralPointer
                                {
                                    type = PointerKind.ListLiteral,
                                    typeInfo = new CollectionTypeInfo
                                    {
                                        type = AttributeType.List,
                                        required = true,
                                        entryTypeInfo = new PrimitiveTypeInfo
                                        {
                                            type = AttributeType.String,
                                            required = true,
                                        },
                                    },
                                    entries = new Pointer[]
                                    {
                                        StringPointer("one"),
                                        StringPointer("two"),
                                    },
                                },
                            },
                        },
                    },
                },
            };
            var getter = new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                typeInfo = resultType,
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = constructor,
                    },
                },
            };

            object? result = NSGetterEvaluator.Evaluate(getter, ctx);
            string rootId = NSGetterEvaluator.FindRowIdByReference(result, ctx)!;
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                rootId,
                out ObjectAttributeValue? root));
            Assert.AreEqual("string-box-type", root!.typeId);
            string valuesId = root.value!["Values"];
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                valuesId,
                out ArrayAttributeValue? values));
            Assert.AreEqual(
                "generic-string-binding",
                values!.genericBindings!["generic-param"]);
            Assert.That(values.value, Has.Length.EqualTo(2));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                values.value![0],
                out StringAttributeValue? first));
            Assert.AreEqual("one", first!.value);
        }

        [Test]
        public void CustomConstructor_OpenGenericFamilyIsRejected()
        {
            NeoClient client = BuildClient();
            CustomTypeInfo openType = ClosedStringBoxType();
            openType.typeId = "generic-box-type";
            var constructor = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CustomConstructorFunction
                {
                    type = FunctionKind.CustomConstructor,
                    info = new FunctionCustomConstructorInfo
                    {
                        customTypeInfo = openType,
                        fields = Array.Empty<FunctionCustomConstructorField>(),
                    },
                },
            };

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    new FunctionWithReturnType
                    {
                        parameters = Array.Empty<Variable>(),
                        typeInfo = openType,
                        instructions = new Instruction[]
                        {
                            new ReturnInstruction
                            {
                                type = InstructionKind.Return,
                                pointer = constructor,
                            },
                        },
                    },
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("Cannot construct open generic Custom type 'GenericBox'", error.Message);
        }

        [Test]
        public void CustomConstructor_UnescapedLocalIsCollectedAtTerminal()
        {
            NeoClient client = BuildClient();
            int rowsBefore = client.sessionValues.Count;
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            Variable profile = ProfileVariable("profile-local", "Ada");
            var getter = StringFunction(
                new VariableInstruction
                {
                    type = InstructionKind.Variable,
                    variable = profile,
                },
                new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = StringPointer("done"),
                });

            Assert.AreEqual("done", NSGetterEvaluator.Evaluate(getter, ctx));

            Assert.AreEqual(
                rowsBefore,
                client.sessionValues.Count,
                "Remaining Session rows: " + DescribeSessionRows(client));
            Assert.AreEqual(0, ctx.rowUnwrapCache.Count);
            Assert.AreEqual(0, ctx.rowReverseIndex.Count);
            Assert.AreEqual(0, ctx.rowCacheKeysByRow.Count);
        }

        [Test]
        public void CustomConstructor_AssignedToStaticParentSurvivesTerminalCleanup()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            Variable profile = ProfileVariable("profile-parented", "Ada");
            var getter = StringFunction(
                new VariableInstruction
                {
                    type = InstructionKind.Variable,
                    variable = profile,
                },
                new AssignInstruction
                {
                    type = InstructionKind.Assign,
                    target = new WriteTarget
                    {
                        pointer = new StaticMemberPointer
                        {
                            type = PointerKind.StaticMember,
                            attributeId = "static-profile",
                        },
                        typeInfo = ProfileType(),
                        writability = WritabilityKind.Runtime,
                    },
                    operatorValue = "=",
                    pointer = new VariablePointer
                    {
                        type = PointerKind.Variable,
                        variableId = profile.id,
                    },
                },
                new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = StringPointer("done"),
                });

            Assert.AreEqual("done", NSGetterEvaluator.Evaluate(getter, ctx));

            NeoStaticBinding binding = NeoGeneratedTypesSupport.StaticBinding(
                client,
                "static-profile",
                NeoValueOwnership.Session);
            Assert.IsNotNull(binding.ValueId);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                binding.ValueId!,
                out ObjectAttributeValue? _));
        }

        [Test]
        public void CustomConstructor_AssignedBelowExternalParentlessSessionValueSurvivesCleanup()
        {
            NeoClient client = BuildClient();
            NeoAttributeCustomWritable oldChild = CreateOwnedChild(client, "old");
            string oldChildId = oldChild.value!.id;
            NeoAttributeCustomWritable host = CreateOwnedParent(
                client,
                oldChildId);
            string hostId = host.value!.id;
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            object? hostValue = NSGetterEvaluator.UnwrapRow(
                host.value,
                ctx,
                NeoValueOwnership.Session);
            var function = StringFunction(
                new AssignInstruction
                {
                    type = InstructionKind.Assign,
                    target = new WriteTarget
                    {
                        pointer = KeyOf(
                            new VariablePointer
                            {
                                type = PointerKind.Variable,
                                variableId = "__this__",
                            },
                            "Child"),
                        typeInfo = OwnedChildType(),
                        writability = WritabilityKind.Runtime,
                    },
                    operatorValue = "=",
                    pointer = OwnedChildConstructorPointer("new"),
                },
                new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = StringPointer("done"),
                });

            Assert.AreEqual(
                "done",
                NSGetterEvaluator.Evaluate(function, ctx.WithThis(hostValue)));

            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                hostId,
                out ObjectAttributeValue? storedHost));
            string attachedChildId = storedHost!.value!["Child"];
            Assert.AreNotEqual(oldChildId, attachedChildId);
            Assert.IsTrue(client.TryFindOwnedParent(
                NeoValueOwnership.Session,
                attachedChildId,
                out string? attachedParentId));
            Assert.AreEqual(hostId, attachedParentId);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                attachedChildId,
                out ObjectAttributeValue? attachedChild));
            Assert.AreEqual("new", ReadOwnedChildValue(client, attachedChild!));
        }

        [Test]
        public void CustomConstructor_PromotionRetargetsLocalAliasesToSaveRows()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            Variable profile = ProfileVariable("profile-promoted", "Ada");
            var alias = new Variable
            {
                id = "profile-alias",
                typeInfo = ProfileType(),
                pointer = new VariablePointer
                {
                    type = PointerKind.Variable,
                    variableId = profile.id,
                },
            };
            var function = new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                typeInfo = ProfileType(),
                instructions = new Instruction[]
                {
                    new VariableInstruction
                    {
                        type = InstructionKind.Variable,
                        variable = profile,
                    },
                    new VariableInstruction
                    {
                        type = InstructionKind.Variable,
                        variable = alias,
                    },
                    new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        target = new WriteTarget
                        {
                            pointer = new StaticMemberPointer
                            {
                                type = PointerKind.StaticMember,
                                attributeId = "static-profile-save",
                            },
                            typeInfo = ProfileType(),
                            writability = WritabilityKind.Save,
                        },
                        operatorValue = "=",
                        pointer = new VariablePointer
                        {
                            type = PointerKind.Variable,
                            variableId = profile.id,
                        },
                    },
                    new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        target = new WriteTarget
                        {
                            pointer = KeyOf(
                                new VariablePointer
                                {
                                    type = PointerKind.Variable,
                                    variableId = alias.id,
                                },
                                "Name"),
                            typeInfo = new PrimitiveTypeInfo
                            {
                                type = AttributeType.String,
                                required = true,
                            },
                            writability = WritabilityKind.Runtime,
                        },
                        operatorValue = "=",
                        pointer = StringPointer("Grace"),
                    },
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new VariablePointer
                        {
                            type = PointerKind.Variable,
                            variableId = alias.id,
                        },
                    },
                },
            };

            object? result = NSGetterEvaluator.Evaluate(function, ctx);

            Assert.IsNotNull(result);
            string rootId = NSGetterEvaluator.FindRowIdByReference(result, ctx)!;
            Assert.AreEqual(
                NeoValueOwnership.Save,
                NSGetterEvaluator.FindRowOwnershipByReference(result, ctx));
            Assert.IsFalse(client.HasWritableValue(
                NeoValueOwnership.Session,
                rootId));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                rootId,
                out ObjectAttributeValue? savedProfile));
            string nameId = savedProfile!.value!["Name"];
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                nameId,
                out StringAttributeValue? savedName));
            Assert.AreEqual("Grace", savedName!.value);
            Assert.IsFalse(ctx.rowCacheKeysByRow.ContainsKey(
                $"{NeoValueOwnership.Session}:{rootId}"));
            Assert.IsTrue(ctx.rowCacheKeysByRow.ContainsKey(
                $"{NeoValueOwnership.Save}:{rootId}"));
        }

        [Test]
        public void CustomConstructor_ThrowPathCollectsUnescapedGraph()
        {
            NeoClient client = BuildClient();
            int rowsBefore = client.sessionValues.Count;
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            Variable profile = ProfileVariable("profile-before-throw", "Ada");
            var getter = StringFunction(
                new VariableInstruction
                {
                    type = InstructionKind.Variable,
                    variable = profile,
                },
                new ThrowInstruction
                {
                    type = InstructionKind.Throw,
                    pointer = StringPointer("stop"),
                });

            Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(getter, ctx));

            Assert.AreEqual(
                rowsBefore,
                client.sessionValues.Count,
                "Remaining Session rows: " + DescribeSessionRows(client));
            Assert.AreEqual(0, ctx.rowUnwrapCache.Count);
            Assert.AreEqual(0, ctx.rowReverseIndex.Count);
            Assert.AreEqual(0, ctx.rowCacheKeysByRow.Count);
        }

        [Test]
        public void CustomConstructor_InvalidNSFunctionReturnIsCollectedBeforeEscape()
        {
            NeoClient client = BuildClient();
            NSFunctionAttribute function = StaticScriptFunction(
                "static-invalid-constructor-return",
                deferred: false,
                new PrimitiveTypeInfo
                {
                    type = AttributeType.Int,
                    required = true,
                },
                new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = ConstructorPointer(
                        "Ada",
                        includeOptionalNull: false),
                });
            ((Dictionary<string, JsonAttribute>)client.attributes)[function.id] =
                function;
            ((Dictionary<string, CustomType>)client.types)["profile-type"]
                .schema![function.id] = function.id;
            int rowsBefore = client.sessionValues.Count;

            Assert.Throws<InvalidOperationException>(() =>
                new NeoAttributeNSFunction(client, function, null)
                    .InvokeStatic(Array.Empty<object?>()));

            Assert.AreEqual(
                rowsBefore,
                client.sessionValues.Count,
                "Remaining Session rows: " + DescribeSessionRows(client));
        }

        [Test]
        public void CustomConstructor_InvalidDeferredNSFunctionReturnIsCollected()
        {
            NeoClient client = BuildClient();
            NeoDeferredFunction<string>? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["static-wait"] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport.ResolveDeferredFunction<
                            NeoDeferredFunction<string>>(
                            deferred,
                            "Wait"),
                });
            Variable profile = ProfileVariable(
                "profile-invalid-deferred-return",
                "Ada");
            NSFunctionAttribute function = StaticScriptFunction(
                "static-invalid-deferred-constructor-return",
                deferred: true,
                new PrimitiveTypeInfo
                {
                    type = AttributeType.Int,
                    required = true,
                },
                new VariableInstruction
                {
                    type = InstructionKind.Variable,
                    variable = profile,
                },
                new FunctionCallInstruction
                {
                    type = InstructionKind.FunctionCall,
                    call = new CallFunctionPointer
                    {
                        type = PointerKind.CallFunction,
                        attributeId = "static-wait",
                        receiver = new CallReceiver
                        {
                            kind = CallReceiverKind.Static,
                            attributeId = "static-wait",
                        },
                        args = Array.Empty<Pointer>(),
                        callSiteId = "invalid-return-wait",
                    },
                },
                new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = new VariablePointer
                    {
                        type = PointerKind.Variable,
                        variableId = profile.id,
                    },
                });
            ((Dictionary<string, JsonAttribute>)client.attributes)[function.id] =
                function;
            ((Dictionary<string, CustomType>)client.types)["profile-type"]
                .schema![function.id] = function.id;
            int rowsBefore = client.sessionValues.Count;

            Task<object?> invocation =
                new NeoAttributeNSFunction(client, function, null)
                    .InvokeStaticAsync(Array.Empty<object?>());

            Assert.IsNotNull(pending);
            Assert.Greater(client.sessionValues.Count, rowsBefore);
            pending!.Complete("ready");
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await invocation);
            Assert.AreEqual(
                rowsBefore,
                client.sessionValues.Count,
                "Remaining Session rows: " + DescribeSessionRows(client));
        }

        [Test]
        public void CustomConstructor_CorruptVoidActionReturnIsCollected()
        {
            NeoClient client = BuildClient();
            int rowsBefore = client.sessionValues.Count;
            var body = new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                typeInfo = new VoidTypeInfo
                {
                    type = AttributeType.Void,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = ConstructorPointer(
                            "Ada",
                            includeOptionalNull: false),
                    },
                },
            };

            Assert.Throws<NSGetterRuntimeError>(() =>
                NeoScriptExecutor.Execute(
                    client,
                    body,
                    new Dictionary<string, object?>(),
                    new NSGetterEvaluator.Context(client, null, null)));

            Assert.AreEqual(rowsBefore, client.sessionValues.Count);
        }

        [Test]
        public void CustomConstructor_DeferredFailureCollectsUnescapedGraph()
        {
            NeoClient client = BuildClient();
            NeoDeferredFunction<string>? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["static-wait"] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport.ResolveDeferredFunction<
                            NeoDeferredFunction<string>>(
                            deferred,
                            "Wait"),
                });
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            Variable profile = ProfileVariable("profile-before-wait", "Ada");
            var getter = StringFunction(
                new VariableInstruction
                {
                    type = InstructionKind.Variable,
                    variable = profile,
                },
                new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = new CallFunctionPointer
                    {
                        type = PointerKind.CallFunction,
                        attributeId = "static-wait",
                        receiver = new CallReceiver
                        {
                            kind = CallReceiverKind.Static,
                            attributeId = "static-wait",
                        },
                        args = Array.Empty<Pointer>(),
                        callSiteId = "constructor-wait",
                    },
                });
            var scope = new Dictionary<string, object?>
            {
                ["__this__"] = null,
                ["__root__"] = null,
                ["__context__"] = null,
            };

            NeoScriptExecutionResult execution = NeoScriptExecutor.Execute(
                client,
                getter,
                scope,
                ctx,
                NeoScriptExecutionOptions.ForDirectFunction(client));

            Assert.IsTrue(execution.IsPaused);
            Assert.IsNotNull(pending);
            string constructedId = AssertSingleConstructedRoot(ctx);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                constructedId,
                out AttributeValue? _));
            Exception? observed = null;
            execution.WhenDeferredSettled(
                _ => Assert.Fail("Expected deferred failure."),
                exception => observed = exception);

            pending!.Fail(new InvalidOperationException("stop waiting"));

            Assert.IsInstanceOf<InvalidOperationException>(observed);
            Assert.IsFalse(client.TryGetValue(
                NeoValueOwnership.Session,
                constructedId,
                out AttributeValue? _));
        }

        [Test]
        public void CustomConstructor_DeferredDialogueDisposalCollectsWithoutResuming()
        {
            NeoClient client = BuildClient();
            NeoDeferredFunction<string>? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["static-wait"] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport.ResolveDeferredFunction<
                            NeoDeferredFunction<string>>(
                            deferred,
                            "Wait"),
                });
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            Variable profile = ProfileVariable("profile-before-dispose", "Ada");
            var getter = StringFunction(
                new VariableInstruction
                {
                    type = InstructionKind.Variable,
                    variable = profile,
                },
                new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = new CallFunctionPointer
                    {
                        type = PointerKind.CallFunction,
                        attributeId = "static-wait",
                        receiver = new CallReceiver
                        {
                            kind = CallReceiverKind.Static,
                            attributeId = "static-wait",
                        },
                        args = Array.Empty<Pointer>(),
                        callSiteId = "constructor-dispose-wait",
                    },
                });
            var scope = new Dictionary<string, object?>
            {
                ["__this__"] = null,
                ["__root__"] = null,
                ["__context__"] = null,
            };

            NeoScriptExecutionResult execution = NeoScriptExecutor.Execute(
                client,
                getter,
                scope,
                ctx,
                NeoScriptExecutionOptions.ForDialogue(client, logger: null));

            Assert.IsTrue(execution.IsPaused);
            Assert.IsNotNull(pending);
            string constructedId = AssertSingleConstructedRoot(ctx);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                constructedId,
                out AttributeValue? _));
            bool resumed = false;
            bool failed = false;
            execution.WhenDeferredSettled(
                _ => resumed = true,
                _ => failed = true);

            execution.Deferred!.DisposeFromOwner("dialogue disposed");

            Assert.IsFalse(resumed);
            Assert.IsFalse(failed);
            Assert.IsFalse(client.TryGetValue(
                NeoValueOwnership.Session,
                constructedId,
                out AttributeValue? _));
        }

        [Test]
        public void CustomConstructor_ImmediateFunctionArgumentRemainsTracked()
        {
            NeoClient client = BuildClient();
            object? received = null;
            string? receivedId = null;
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            client.RegisterNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    ["static-consume"] = (_, _, args) =>
                    {
                        received = args[0];
                        receivedId = NSGetterEvaluator.FindRowIdByReference(
                            received,
                            ctx);
                        return "accepted";
                    },
                });
            var getter = StringFunction(
                new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = new CallFunctionPointer
                    {
                        type = PointerKind.CallFunction,
                        attributeId = "static-consume",
                        receiver = new CallReceiver
                        {
                            kind = CallReceiverKind.Static,
                            attributeId = "static-consume",
                        },
                        args = new Pointer[]
                        {
                            ConstructorPointer("Ada", includeOptionalNull: false),
                        },
                        callSiteId = "constructor-consume",
                    },
                });

            Assert.AreEqual("accepted", NSGetterEvaluator.Evaluate(getter, ctx));
            Assert.IsNotNull(received);
            Assert.IsNotNull(receivedId);
            Assert.IsFalse(client.TryGetValue(
                NeoValueOwnership.Session,
                receivedId!,
                out ObjectAttributeValue? _));
            Assert.IsNull(NSGetterEvaluator.FindRowIdByReference(received, ctx));
            Assert.IsFalse(ctx.rowCacheKeysByRow.ContainsKey(
                $"{NeoValueOwnership.Session}:{receivedId}"));
            foreach (NeoAttribute node in client.nodes.Values)
            {
                Assert.AreNotEqual(receivedId, node.overrideValueId);
                Assert.AreNotEqual(receivedId, node.value?.id);
            }
        }

        private static NeoClient BuildClient()
        {
            var rootType = new CustomType
            {
                id = "root-type",
                projectId = "static-project",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var rulesType = new CustomType
            {
                id = "rules-type",
                projectId = "static-project",
                name = "Rules",
                schema = new Dictionary<string, string>
                {
                    ["Count"] = "static-count",
                    ["Score"] = "static-score",
                    ["Names"] = "static-names",
                },
            };
            // Deliberately put the optional member before the required one in
            // schema order. Constructor IR follows generated C# order instead:
            // required parameters first, then optional parameters defaulted to
            // null.
            var profileType = new CustomType
            {
                id = "profile-type",
                projectId = "static-project",
                name = "Profile",
                schema = new Dictionary<string, string>
                {
                    ["Title"] = "profile-title",
                    ["Level"] = "profile-level",
                    ["Name"] = "profile-name",
                    ["Tags"] = "profile-tags",
                    ["Stats"] = "profile-stats",
                    ["Current"] = "static-profile",
                    ["Saved"] = "static-profile-save",
                    ["Wait"] = "static-wait",
                    ["Consume"] = "static-consume",
                },
            };
            var genericBoxType = new CustomType
            {
                id = "generic-box-type",
                projectId = "static-project",
                name = "GenericBox",
                schema = new Dictionary<string, string>
                {
                    ["Values"] = "generic-values",
                },
                genericParams = new List<GenericParamDeclaration>
                {
                    new GenericParamDeclaration
                    {
                        id = "generic-param",
                        name = "T",
                    },
                },
            };
            var stringBoxType = new CustomType
            {
                id = "string-box-type",
                projectId = "static-project",
                name = "StringBox",
                schema = new Dictionary<string, string>(),
                extendsTypeId = genericBoxType.id,
                extendsGenericBindings = new Dictionary<string, GenericBinding>
                {
                    ["generic-param"] = new GenericBinding
                    {
                        kind = NeoGenericBindingKinds.Attribute,
                        attributeId = "generic-string-binding",
                    },
                },
            };
            var ownedChildType = new CustomType
            {
                id = "owned-child-type",
                projectId = "static-project",
                name = "OwnedChild",
                schema = new Dictionary<string, string>
                {
                    ["Value"] = "owned-child-value",
                },
            };
            var ownedParentType = new CustomType
            {
                id = "owned-parent-type",
                projectId = "static-project",
                name = "OwnedParent",
                schema = new Dictionary<string, string>
                {
                    ["Child"] = "owned-parent-child",
                },
            };
            var rootAssets = RootAttribute(
                "root-assets",
                "Assets",
                "immutable",
                "value-assets");
            var rootSave = RootAttribute(
                "root-save",
                "Save",
                "save",
                "value-save");
            var rootSession = RootAttribute(
                "root-session",
                "Session",
                "session",
                "value-session");
            var count = new IntAttribute
            {
                id = "static-count",
                projectId = "static-project",
                name = "Count",
                type = AttributeType.Int,
                required = true,
                isStatic = true,
                isVirtual = false,
                storage = "session",
                valueId = "static-count-authored",
            };
            var score = new IntAttribute
            {
                id = "static-score",
                projectId = "static-project",
                name = "Score",
                type = AttributeType.Int,
                required = false,
                isStatic = true,
                isVirtual = false,
                storage = "save",
                storageKey = "scores:$parentType",
            };
            var entry = new StringAttribute
            {
                id = "name-entry",
                projectId = "static-project",
                name = "Name",
                type = AttributeType.String,
                required = true,
                isStatic = false,
                localizable = false,
            };
            var names = new ListAttribute
            {
                id = "static-names",
                projectId = "static-project",
                name = "Names",
                type = AttributeType.List,
                required = false,
                isStatic = true,
                isVirtual = false,
                storage = "session",
                entryAttributeId = entry.id,
            };
            var profileName = new StringAttribute
            {
                id = "profile-name",
                projectId = "static-project",
                name = "Name",
                type = AttributeType.String,
                required = true,
                isStatic = false,
                localizable = false,
            };
            var profileTitle = new StringAttribute
            {
                id = "profile-title",
                projectId = "static-project",
                name = "Title",
                type = AttributeType.String,
                required = false,
                isStatic = false,
                localizable = false,
            };
            var profileLevel = new IntAttribute
            {
                id = "profile-level",
                projectId = "static-project",
                name = "Level",
                type = AttributeType.Int,
                required = true,
                isStatic = false,
                defaultValue = new NumberAttributeValueBase { value = 3 },
            };
            var profileTagEntry = new StringAttribute
            {
                id = "profile-tag-entry",
                projectId = "static-project",
                name = "Tag",
                type = AttributeType.String,
                required = false,
                isStatic = false,
                localizable = false,
            };
            var profileTags = new ListAttribute
            {
                id = "profile-tags",
                projectId = "static-project",
                name = "Tags",
                type = AttributeType.List,
                required = true,
                isStatic = false,
                entryAttributeId = profileTagEntry.id,
                storageKey = "profile:$parentType",
                defaultValue = new ArrayAttributeValueBase
                {
                    value = new[] { "profile-tag-default" },
                },
            };
            var profileStatEntry = new IntAttribute
            {
                id = "profile-stat-entry",
                projectId = "static-project",
                name = "Stat",
                type = AttributeType.Int,
                required = false,
                isStatic = false,
            };
            var profileStats = new DictionaryAttribute
            {
                id = "profile-stats",
                projectId = "static-project",
                name = "Stats",
                type = AttributeType.Dictionary,
                required = true,
                isStatic = false,
                entryAttributeId = profileStatEntry.id,
                defaultValue = new ObjectAttributeValueBase
                {
                    value = new Dictionary<string, string>
                    {
                        ["wins"] = "profile-stat-default",
                    },
                },
            };
            var staticProfile = new CustomAttribute
            {
                id = "static-profile",
                projectId = "static-project",
                name = "Current",
                type = AttributeType.Custom,
                required = false,
                isStatic = true,
                isVirtual = false,
                storage = "session",
                customTypeId = profileType.id,
            };
            var staticSaveProfile = new CustomAttribute
            {
                id = "static-profile-save",
                projectId = "static-project",
                name = "Saved",
                type = AttributeType.Custom,
                required = false,
                isStatic = true,
                isVirtual = false,
                storage = "save",
                customTypeId = profileType.id,
            };
            var staticWait = new FunctionAttribute
            {
                id = "static-wait",
                projectId = "static-project",
                name = "Wait",
                type = AttributeType.Function,
                required = false,
                isStatic = true,
                isVirtual = false,
                returnTypeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.String,
                    required = true,
                },
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                deferred = true,
            };
            var staticConsume = new FunctionAttribute
            {
                id = "static-consume",
                projectId = "static-project",
                name = "Consume",
                type = AttributeType.Function,
                required = false,
                isStatic = true,
                isVirtual = false,
                returnTypeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.String,
                    required = true,
                },
                argumentTypes = new[]
                {
                    new FunctionArgumentTypeInfo
                    {
                        name = "Profile",
                        type = AttributeType.Custom,
                        required = true,
                        typeId = profileType.id,
                    },
                },
                deferred = false,
            };
            var genericEntry = new GenericAttribute
            {
                id = "generic-entry",
                projectId = "static-project",
                name = "Entry",
                type = AttributeType.Generic,
                required = true,
                genericParamId = "generic-param",
            };
            var genericValues = new ListAttribute
            {
                id = "generic-values",
                projectId = "static-project",
                name = "Values",
                type = AttributeType.List,
                required = true,
                entryAttributeId = genericEntry.id,
            };
            var genericStringBinding = new StringAttribute
            {
                id = "generic-string-binding",
                projectId = "static-project",
                name = "String Binding",
                type = AttributeType.String,
                required = true,
                localizable = false,
            };
            var ownedChildValue = new StringAttribute
            {
                id = "owned-child-value",
                projectId = "static-project",
                name = "Value",
                type = AttributeType.String,
                required = true,
                localizable = false,
            };
            var ownedParentChild = new CustomAttribute
            {
                id = "owned-parent-child",
                projectId = "static-project",
                name = "Child",
                type = AttributeType.Custom,
                required = true,
                customTypeId = ownedChildType.id,
            };
            var data = new ProjectData
            {
                project = new Project
                {
                    id = "static-project",
                    name = "Static Tests",
                    rootAssetsAttributeId = rootAssets.id,
                    rootSaveFileAttributeId = rootSave.id,
                    rootSessionAttributeId = rootSession.id,
                },
                attributes = new Dictionary<string, JsonAttribute>
                {
                    [rootAssets.id] = rootAssets,
                    [rootSave.id] = rootSave,
                    [rootSession.id] = rootSession,
                    [count.id] = count,
                    [score.id] = score,
                    [entry.id] = entry,
                    [names.id] = names,
                    [profileName.id] = profileName,
                    [profileTitle.id] = profileTitle,
                    [profileLevel.id] = profileLevel,
                    [profileTagEntry.id] = profileTagEntry,
                    [profileTags.id] = profileTags,
                    [profileStatEntry.id] = profileStatEntry,
                    [profileStats.id] = profileStats,
                    [staticProfile.id] = staticProfile,
                    [staticSaveProfile.id] = staticSaveProfile,
                    [staticWait.id] = staticWait,
                    [staticConsume.id] = staticConsume,
                    [genericEntry.id] = genericEntry,
                    [genericValues.id] = genericValues,
                    [genericStringBinding.id] = genericStringBinding,
                    [ownedChildValue.id] = ownedChildValue,
                    [ownedParentChild.id] = ownedParentChild,
                },
                values = new Dictionary<string, AttributeValue>
                {
                    ["value-assets"] = ObjectValue("value-assets", rootType.id),
                    ["value-save"] = ObjectValue("value-save", rootType.id),
                    ["value-session"] = ObjectValue("value-session", rootType.id),
                    ["static-count-authored"] = new NumberAttributeValue
                    {
                        id = "static-count-authored",
                        value = 5,
                    },
                    ["profile-tag-default"] = new StringAttributeValue
                    {
                        id = "profile-tag-default",
                        value = "starter",
                    },
                    ["profile-stat-default"] = new NumberAttributeValue
                    {
                        id = "profile-stat-default",
                        value = 4,
                    },
                },
                valuePartitions = new Dictionary<string, JToken>
                {
                    ["scores:rules-type"] = new JObject(),
                },
                types = new Dictionary<string, CustomType>
                {
                    [rootType.id] = rootType,
                    [rulesType.id] = rulesType,
                    [profileType.id] = profileType,
                    [genericBoxType.id] = genericBoxType,
                    [stringBoxType.id] = stringBoxType,
                    [ownedChildType.id] = ownedChildType,
                    [ownedParentType.id] = ownedParentType,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
            return NeoTestSaveStack.ClientFromSchema(data);
        }

        private static FunctionWithReturnType ReturnFunction(Pointer pointer)
        {
            return new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                typeInfo = ProfileType(),
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = pointer,
                    },
                },
            };
        }

        private static FunctionWithReturnType StringFunction(
            params Instruction[] instructions)
        {
            return new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                typeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.String,
                    required = true,
                },
                instructions = instructions,
            };
        }

        private static NSFunctionAttribute StaticScriptFunction(
            string id,
            bool deferred,
            TypeInfo returnType,
            params Instruction[] instructions)
        {
            return new NSFunctionAttribute
            {
                id = id,
                projectId = "static-project",
                name = id,
                type = AttributeType.NSFunction,
                required = false,
                isStatic = true,
                code = "compiled test function",
                returnTypeInfo = returnType,
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                deferred = deferred,
                action = new FunctionWithReturnType
                {
                    parameters = new[]
                    {
                        new Variable
                        {
                            id = "__root__",
                            typeInfo = new CustomTypeInfo
                            {
                                type = AttributeType.Custom,
                                required = true,
                                typeId = "root-type",
                            },
                            pointer = new VariablePointer
                            {
                                type = PointerKind.Variable,
                                variableId = "__root__",
                            },
                        },
                    },
                    typeInfo = returnType,
                    instructions = instructions,
                },
            };
        }

        private static Variable ProfileVariable(string id, string name)
        {
            return new Variable
            {
                id = id,
                typeInfo = ProfileType(),
                pointer = ConstructorPointer(name, includeOptionalNull: false),
            };
        }

        private static FunctionPointer ConstructorPointer(
            string name,
            bool includeOptionalNull)
        {
            var fields = new List<FunctionCustomConstructorField>
            {
                // Required-first order matches generated public C# constructors
                // even though the schema above stores Title first.
                new FunctionCustomConstructorField
                {
                    schemaKey = "Name",
                    attributeId = "profile-name",
                    valuePointer = StringPointer(name),
                },
            };
            if (includeOptionalNull)
            {
                fields.Add(new FunctionCustomConstructorField
                {
                    schemaKey = "Title",
                    attributeId = "profile-title",
                    valuePointer = NullPointer(),
                });
                fields.Add(new FunctionCustomConstructorField
                {
                    schemaKey = "Level",
                    attributeId = "profile-level",
                    valuePointer = NullPointer(),
                });
            }
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CustomConstructorFunction
                {
                    type = FunctionKind.CustomConstructor,
                    info = new FunctionCustomConstructorInfo
                    {
                        customTypeInfo = ProfileType(),
                        fields = fields.ToArray(),
                    },
                },
            };
        }

        private static CustomTypeInfo ProfileType()
        {
            return new CustomTypeInfo
            {
                type = AttributeType.Custom,
                required = true,
                typeId = "profile-type",
            };
        }

        private static CustomTypeInfo OwnedChildType()
        {
            return new CustomTypeInfo
            {
                type = AttributeType.Custom,
                required = true,
                typeId = "owned-child-type",
            };
        }

        private static CustomTypeInfo OwnedParentType()
        {
            return new CustomTypeInfo
            {
                type = AttributeType.Custom,
                required = true,
                typeId = "owned-parent-type",
            };
        }

        private static FunctionPointer OwnedChildConstructorPointer(string value)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CustomConstructorFunction
                {
                    type = FunctionKind.CustomConstructor,
                    info = new FunctionCustomConstructorInfo
                    {
                        customTypeInfo = OwnedChildType(),
                        fields = new[]
                        {
                            new FunctionCustomConstructorField
                            {
                                schemaKey = "Value",
                                attributeId = "owned-child-value",
                                valuePointer = StringPointer(value),
                            },
                        },
                    },
                },
            };
        }

        private static FunctionPointer OwnedParentConstructorPointer(Pointer child)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CustomConstructorFunction
                {
                    type = FunctionKind.CustomConstructor,
                    info = new FunctionCustomConstructorInfo
                    {
                        customTypeInfo = OwnedParentType(),
                        fields = new[]
                        {
                            new FunctionCustomConstructorField
                            {
                                schemaKey = "Child",
                                attributeId = "owned-parent-child",
                                valuePointer = child,
                            },
                        },
                    },
                },
            };
        }

        private static KeyOfPointer KeyOf(Pointer receiver, string key)
        {
            return new KeyOfPointer
            {
                type = PointerKind.KeyOf,
                keyOf = new KeyOf
                {
                    pointer = receiver,
                    key = StringPointer(key),
                },
            };
        }

        private static NeoAttributeCustomWritable CreateOwnedChild(
            NeoClient client,
            string value)
        {
            var valueRow = new StringAttributeValue
            {
                id = Guid.NewGuid().ToString(),
                value = value,
            };
            return NeoGeneratedTypesSupport.CreateWritableCustomValue(
                client,
                "owned-child-type",
                new Dictionary<string, string>
                {
                    ["Value"] = valueRow.id,
                },
                new AttributeValue[] { valueRow });
        }

        private static NeoAttributeCustomWritable CreateOwnedParent(
            NeoClient client,
            string childId)
        {
            return NeoGeneratedTypesSupport.CreateWritableCustomValue(
                client,
                "owned-parent-type",
                new Dictionary<string, string>
                {
                    ["Child"] = childId,
                },
                Array.Empty<AttributeValue>());
        }

        private static void SeedOwnedChild(
            NeoClient client,
            NeoValueOwnership ownership,
            string childId,
            string valueId,
            string value)
        {
            var valueRow = new StringAttributeValue
            {
                id = valueId,
                value = value,
            };
            var childRow = new ObjectAttributeValue
            {
                id = childId,
                typeId = "owned-child-type",
                value = new Dictionary<string, string>
                {
                    ["Value"] = valueId,
                },
            };
            if (ownership == NeoValueOwnership.Asset)
            {
                var values = (Dictionary<string, AttributeValue>)client.values;
                values[valueId] = valueRow;
                values[childId] = childRow;
                return;
            }
            client.SetWritableValue(ownership, valueRow);
            client.SetWritableValue(ownership, childRow);
        }

        private static void RegisterOwnedChildStatic(
            NeoClient client,
            string attributeId,
            NeoValueOwnership ownership)
        {
            var attribute = new CustomAttribute
            {
                id = attributeId,
                projectId = "static-project",
                name = attributeId,
                type = AttributeType.Custom,
                required = false,
                isStatic = true,
                storage = ownership == NeoValueOwnership.Session
                    ? "session"
                    : "save",
                customTypeId = "owned-child-type",
            };
            ((Dictionary<string, JsonAttribute>)client.attributes)[attributeId] = attribute;
            ((Dictionary<string, CustomType>)client.types)["rules-type"]
                .schema![attributeId] = attributeId;
        }

        private static string? ReadOwnedChildValue(
            NeoClient client,
            ObjectAttributeValue child,
            NeoValueOwnership ownership = NeoValueOwnership.Session)
        {
            string valueId = child.value!["Value"];
            Assert.IsTrue(client.TryGetValue(
                ownership,
                valueId,
                out StringAttributeValue? value));
            return value!.value;
        }

        private static CustomTypeInfo ClosedStringBoxType()
        {
            return new CustomTypeInfo
            {
                type = AttributeType.Custom,
                required = true,
                typeId = "string-box-type",
                typeArguments = new Dictionary<string, TypeInfo>
                {
                    ["generic-param"] = new PrimitiveTypeInfo
                    {
                        type = AttributeType.String,
                        required = true,
                    },
                },
            };
        }

        private static ValuePointer StringPointer(string value)
        {
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = AttributeType.String,
                        required = true,
                    },
                    value = JToken.FromObject(value),
                },
            };
        }

        private static ValuePointer NullPointer()
        {
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = AttributeType.Null,
                        required = false,
                    },
                    value = JValue.CreateNull(),
                },
            };
        }

        private static ValuePointer BoolPointer(bool value)
        {
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = AttributeType.Bool,
                        required = true,
                    },
                    value = JToken.FromObject(value),
                },
            };
        }

        private static ValuePointer IntPointer(int value)
        {
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = AttributeType.Int,
                        required = true,
                    },
                    value = JToken.FromObject(value),
                },
            };
        }

        private static string AssertSingleConstructedRoot(
            NSGetterEvaluator.Context ctx)
        {
            var roots = new HashSet<string>();
            foreach (NSGetterEvaluator.RowReference row in ctx.rowReverseIndex.Values)
            {
                if (row.ownership == NeoValueOwnership.Session)
                {
                    roots.Add(row.valueId);
                }
            }
            Assert.That(roots, Has.Count.EqualTo(1));
            foreach (string root in roots) return root;
            throw new AssertionException("Expected one constructed Session root.");
        }

        private static CustomAttribute RootAttribute(
            string id,
            string name,
            string storage,
            string valueId)
        {
            return new CustomAttribute
            {
                id = id,
                projectId = "static-project",
                name = name,
                type = AttributeType.Custom,
                required = true,
                customTypeId = "root-type",
                storage = storage,
                valueId = valueId,
            };
        }

        private static ObjectAttributeValue ObjectValue(string id, string typeId)
        {
            return new ObjectAttributeValue
            {
                id = id,
                typeId = typeId,
                value = new Dictionary<string, string>(),
            };
        }

        private static string DescribeSessionRows(NeoClient client)
        {
            var descriptions = new List<string>();
            foreach (var pair in client.sessionValues)
            {
                string payload = pair.Value switch
                {
                    StringAttributeValue text => text.value ?? "<null>",
                    NumberAttributeValue number =>
                        number.value?.ToString() ?? "<null>",
                    ObjectAttributeValue obj => obj.value is null
                        ? "<null>"
                        : string.Join("|", obj.value.Keys),
                    ArrayAttributeValue array => array.value is null
                        ? "<null>"
                        : string.Join("|", array.value),
                    _ => "",
                };
                string parent = client.TryFindOwnedParent(
                    NeoValueOwnership.Session,
                    pair.Key,
                    out string? parentId)
                        ? parentId ?? "<null>"
                        : "<none>";
                descriptions.Add(
                    $"{pair.Key}:{pair.Value.GetType().Name}:{pair.Value.typeId}:{payload}:parent={parent}");
            }
            return string.Join(", ", descriptions);
        }

        private sealed class TestEnumOption : INeoEnumOption
        {
            internal TestEnumOption(string optionId)
            {
                this.optionId = optionId;
            }

            public string optionId { get; }
        }

        private sealed class TestValueReference : INeoValueReference
        {
            internal TestValueReference(string valueId)
            {
                this.valueId = valueId;
            }

            public string? valueId { get; }
        }

        /// <summary>
        /// Deliberately implements only the generic read-only dictionary
        /// contract. This exercises the constructor marshaller's cached
        /// KeyValuePair fallback instead of System.Collections.IDictionary.
        /// </summary>
        private sealed class GenericOnlyReadOnlyDictionary<T>
            : IReadOnlyDictionary<string, T>
        {
            private readonly IReadOnlyDictionary<string, T> values;

            internal GenericOnlyReadOnlyDictionary(
                IReadOnlyDictionary<string, T> values)
            {
                this.values = values;
            }

            public T this[string key] => values[key];
            public IEnumerable<string> Keys => values.Keys;
            public IEnumerable<T> Values => values.Values;
            public int Count => values.Count;
            public bool ContainsKey(string key) => values.ContainsKey(key);

            public bool TryGetValue(string key, out T value) =>
                values.TryGetValue(key, out value);

            public IEnumerator<KeyValuePair<string, T>> GetEnumerator() =>
                values.GetEnumerator();

            System.Collections.IEnumerator
                System.Collections.IEnumerable.GetEnumerator() =>
                    GetEnumerator();
        }
    }
}
