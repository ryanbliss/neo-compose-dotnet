// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.IO;
using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Coverage for the disposal + valueId-tracking surface added to
    /// <see cref="NeoMember"/> / <see cref="NeoClient"/>:
    ///
    ///   - <see cref="NeoMember.Dispose"/> unregisters from
    ///     <see cref="NeoClient.nodes"/>.
    ///   - Disposing a collection-type parent recursively disposes all
    ///     descendants in <c>childMembers</c>.
    ///   - A writable <c>Set</c> shadows the value at its stable id and a
    ///     bound <see cref="NeoMember"/> refreshes its resolved <c>value</c>
    ///     via <see cref="NeoClient.OnSaveValueChanged"/>; <c>ClearOverride</c>
    ///     drops the shadow back to the authored default.
        ///   - <c>*Writable.Remove</c>/<c>RemoveAt</c> on collection classes
    ///     dispose the orphaned child node AND cascade-delete the
    ///     orphaned value graph from
    ///     <see cref="ProjectSaveData.values"/>.
    /// </summary>
    public class NeoMemberDisposeTests
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

        private static T RequireMember<T>(NeoClient client, string id) where T : Member
        {
            if (!client.TryGetMember(id, out T? member))
            {
                Assert.Fail($"Fixture is missing member '{id}'");
                throw new System.InvalidOperationException("unreachable");
            }
            return member;
        }

        // -----------------------------------------------------------------
        // Single-node disposal.
        // -----------------------------------------------------------------

        [Test]
        public void Dispose_UnregistersFromClientNodes()
        {
            var client = LoadClient();
            var nameMember = RequireMember<StringMember>(client, "member-name");

            var node = NeoMember.Create(client, nameMember, null);
            Assert.IsTrue(client.nodes.ContainsKey("asset:member-name"));

            node.Dispose();
            Assert.IsTrue(node.isDisposed);
            Assert.IsFalse(client.nodes.ContainsKey("asset:member-name"));
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var client = LoadClient();
            var nameMember = RequireMember<StringMember>(client, "member-name");
            var node = NeoMember.Create(client, nameMember, null);

            node.Dispose();
            // Calling Dispose twice shouldn't throw or otherwise misbehave.
            Assert.DoesNotThrow(() => node.Dispose());
            Assert.IsTrue(node.isDisposed);
        }

        [Test]
        public void UnregisterNode_DoesntDropDifferentInstanceUnderSameKey()
        {
            // If a second node replaced the first in the registry (via
            // direct `new`, last-write-wins), disposing the first
            // shouldn't yank the second out.
            var client = LoadClient();
            var nameMember = RequireMember<StringMember>(client, "member-name");

            var first = new NeoMemberString(client, nameMember, null);
            var second = new NeoMemberString(client, nameMember, null);
            Assert.AreSame(second, client.nodes["asset:member-name"], "Direct new is last-write-wins");

            first.Dispose();
            // First's Dispose tried to unregister "member-name" but the
            // registry's instance was second, so the entry stays.
            Assert.IsTrue(client.nodes.ContainsKey("asset:member-name"));
            Assert.AreSame(second, client.nodes["asset:member-name"]);
        }

        // -----------------------------------------------------------------
        // Recursive disposal on collection classes.
        // -----------------------------------------------------------------

        [Test]
        public void DisposingParentClass_DisposesChildren()
        {
            var client = LoadClient();
            var heroMember = RequireMember<ClassMember>(client, "member-hero");
            // Bind to v-dict so children get walked.
            var hero = (NeoMemberClass)NeoMember.Create(client, heroMember, "v-dict");
            Assert.IsTrue(client.nodes.ContainsKey("asset:member-name_v-name"),
                "Pre-condition: child registered");
            var child = client.nodes["asset:member-name_v-name"];

            hero.Dispose();
            Assert.IsTrue(hero.isDisposed);
            Assert.IsTrue(child.isDisposed, "Child should be disposed by parent's recursive Dispose");
            Assert.IsFalse(client.nodes.ContainsKey("asset:member-hero_v-dict"));
            Assert.IsFalse(client.nodes.ContainsKey("asset:member-name_v-name"));
        }

        // -----------------------------------------------------------------
        // Save value change events.
        // -----------------------------------------------------------------

        [Test]
        public void AddSaveValue_FiresOnSaveValueChanged()
        {
            var client = LoadClient();
            string? observedValueId = null;
            client.OnSaveValueChanged += vid => observedValueId = vid;

            client.AddSaveValue("member-name", new StringMemberValue
            {
                id = "v-new",
                createdAt = "now",
                updatedAt = "now",
                value = "fresh",
            });

            Assert.AreEqual("v-new", observedValueId);
        }

        [Test]
        public void NeoMember_TracksValue_AfterWritableSet()
        {
            var client = LoadClient();
            var nameMember = RequireMember<StringMember>(client, "member-name");
            // member-name has an authored default in the fixture, so a
            // freshly-constructed standalone node resolves through that default
            // until a writable row is set.
            var node = (NeoMemberStringWritable)NeoMember.CreateWritable(
                client,
                nameMember,
                null,
                NeoValueOwnership.Save);
            Assert.IsNotNull(node.value);
            Assert.AreEqual("Hero", node.value!.value);

            // Stable-id overlay: a write mints a value bound to this
            // (parentless) node and tracks it directly — no override-map hop.
            node.SetLiteralOverride("after-set");

            Assert.IsNotNull(node.value);
            Assert.AreEqual("after-set", node.value!.value);
        }

        [Test]
        public void NeoMember_ValueRevertsToDefault_WhenWritableShadowCleared()
        {
            var client = LoadClient();
            var nameMember = RequireMember<StringMember>(client, "member-name");
            var node = (NeoMemberStringWritable)NeoMember.CreateWritable(
                client,
                nameMember,
                null,
                NeoValueOwnership.Save);
            node.SetLiteralOverride("seeded");
            Assert.IsNotNull(node.value);

            node.ClearOverride();

            // member-name has no authored default → clearing the shadow leaves
            // the resolved value null.
            Assert.IsNull(node.value);
        }

        // -----------------------------------------------------------------
        // Cascade-delete from saveData.values on collection Removes.
        // -----------------------------------------------------------------

        [Test]
        public void DictionaryRemove_DisposesChild_AndCascadesValueDelete()
        {
            var client = LoadClient();
            var inventoryMember = RequireMember<DictionaryMember>(client, "member-inventory");
            // Materialize a Dict by adding a key — DictionarySaved.Set
            // creates the parent + child rows in saveData.values.
            var inv = (NeoMemberDictionaryWritable)NeoMember.CreateWritable(
                client, inventoryMember, null);
            NeoGeneratedTypesSupport.SetValue(
                inv,
                "sword",
                NeoGeneratedTypesSupport.Value("Excalibur"));

            // Capture the registered child + its valueId before removal.
            Assert.IsTrue(inv.TryGet<NeoMemberString>("sword", out NeoMemberString? childBefore));
            string entryValueId = childBefore!.overrideValueId!;
            Assert.IsTrue(client.nodes.ContainsKey($"session:member-name_{entryValueId}"));
            // Round-trip through the client to confirm the entry value
            // is sitting in saveData.values.
            Assert.IsTrue(client.TryGetValue<StringMemberValue>(entryValueId, out _));

            inv.Remove("sword");

            Assert.IsTrue(childBefore.isDisposed);
            Assert.IsFalse(client.nodes.ContainsKey($"session:member-name_{entryValueId}"));
            Assert.IsFalse(client.TryGetValue<StringMemberValue>(entryValueId, out _),
                "Removed entry's value row should be GC'd from saveData");
        }

        [Test]
        public void ListRemoveAt_DisposesChild_AndCascadesValueDelete()
        {
            var client = LoadClient();
            var tagsMember = RequireMember<ListMember>(client, "member-tags");
            var tags = (NeoMemberListWritable)NeoMember.CreateWritable(client, tagsMember, null);
            NeoGeneratedTypesSupport.AddValue(
                tags,
                NeoGeneratedTypesSupport.Value("first"));
            NeoGeneratedTypesSupport.AddValue(
                tags,
                NeoGeneratedTypesSupport.Value("second"));

            var firstChild = (NeoMemberString)tags[0];
            string firstValueId = firstChild.overrideValueId!;
            Assert.IsTrue(client.nodes.ContainsKey($"session:member-name_{firstValueId}"));
            Assert.IsTrue(client.TryGetValue<StringMemberValue>(firstValueId, out _));

            tags.RemoveAt(0);

            Assert.IsTrue(firstChild.isDisposed);
            Assert.IsFalse(client.nodes.ContainsKey($"session:member-name_{firstValueId}"));
            Assert.IsFalse(client.TryGetValue<StringMemberValue>(firstValueId, out _),
                "Removed entry's value row should be GC'd from writable data");
            Assert.AreEqual(1, tags.Count);
        }

        [Test]
        public void ClassRemove_DisposesChild_AndCascadesValueDelete()
        {
            var client = LoadClient();
            var heroMember = RequireMember<ClassMember>(client, "member-hero");
            var hero = (NeoMemberClassWritable)NeoMember.CreateWritable(client, heroMember, null);
            NeoGeneratedTypesSupport.SetValue(
                hero,
                "Name",
                NeoGeneratedTypesSupport.Value("Aragorn"));

            var nameChild = (NeoMemberString)hero["Name"];
            string nameValueId = nameChild.overrideValueId!;
            Assert.IsTrue(client.nodes.ContainsKey($"session:member-name_{nameValueId}"));
            Assert.IsTrue(client.TryGetValue<StringMemberValue>(nameValueId, out _));

            hero.Remove("Name");

            Assert.IsTrue(nameChild.isDisposed);
            Assert.IsFalse(client.nodes.ContainsKey($"session:member-name_{nameValueId}"));
            Assert.IsFalse(client.TryGetValue<StringMemberValue>(nameValueId, out _));
        }

        [Test]
        public void RemoveSaveValueAndDescendants_RecursesIntoNestedValues()
        {
            // Builds a small typed tree directly in saveData and verifies
            // schema-authoritative Class + List ownership is followed.
            var client = LoadClient();
            ((System.Collections.Generic.Dictionary<string, NeoSchemaClass>)client.classes)["test-gc-class"] =
                new NeoSchemaClass
                {
                    id = "test-gc-class",
                    name = "GcRoot",
                    schema = new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["Tags"] = "member-tags",
                    },
                };
            // Use prefixed ids so we don't collide with the synth
            // fixture's authored values map (which already has v-list,
            // v-num, etc. — the cascade test would falsely fail if a
            // removed save id was also present in `data.values`).
            var grandchildA = new StringMemberValue
            {
                id = "test-gca", createdAt = "x", updatedAt = "x",
                value = "a",
            };
            var grandchildB = new StringMemberValue
            {
                id = "test-gcb", createdAt = "x", updatedAt = "x",
                value = "b",
            };
            var listChild = new ArrayMemberValue
            {
                id = "test-list", createdAt = "x", updatedAt = "x",
                value = new[] { "test-gca", "test-gcb" },
            };
            var rootObject = new ObjectMemberValue
            {
                id = "test-root", classId = "test-gc-class", createdAt = "x", updatedAt = "x",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "Tags", "test-list" },
                },
            };
            client.SetSaveValue(grandchildA);
            client.SetSaveValue(grandchildB);
            client.SetSaveValue(listChild);
            client.SetSaveValue(rootObject);

            client.RemoveSaveValueAndDescendants("test-root");

            Assert.IsFalse(client.TryGetValue<MemberValue>("test-root", out _));
            Assert.IsFalse(client.TryGetValue<MemberValue>("test-list", out _));
            Assert.IsFalse(client.TryGetValue<MemberValue>("test-gca", out _));
            Assert.IsFalse(client.TryGetValue<MemberValue>("test-gcb", out _));
        }

        [Test]
        public void RemoveSaveValueAndDescendants_PreservesLookupTargets()
        {
            var client = LoadClient();
            ((System.Collections.Generic.Dictionary<string, NeoSchemaClass>)client.classes)["test-lookup-owner-class"] =
                new NeoSchemaClass
                {
                    id = "test-lookup-owner-class",
                    name = "LookupOwner",
                    schema = new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["Choice"] = "member-choice",
                    },
                };
            client.SetSaveValue(new StringMemberValue
            {
                id = "test-lookup-target", value = "referenced", createdAt = "x", updatedAt = "x",
            });
            client.SetSaveValue(new ArrayMemberValue
            {
                id = "test-lookup-row", value = new[] { "test-lookup-target" },
                createdAt = "x", updatedAt = "x",
            });
            client.SetSaveValue(new ObjectMemberValue
            {
                id = "test-lookup-owner", classId = "test-lookup-owner-class",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["Choice"] = "test-lookup-row",
                },
                createdAt = "x", updatedAt = "x",
            });

            client.RemoveSaveValueAndDescendants("test-lookup-owner");

            Assert.IsFalse(client.saveValues.ContainsKey("test-lookup-owner"));
            Assert.IsFalse(client.saveValues.ContainsKey("test-lookup-row"));
            Assert.IsTrue(client.saveValues.ContainsKey("test-lookup-target"),
                "Lookup selections are references and must not be recursively deleted");
        }

        [Test]
        public void RemoveSaveValueAndDescendants_DefensivelyHandlesOwnedCycles()
        {
            var client = LoadClient();
            var members =
                (System.Collections.Generic.Dictionary<string, NeoCompose.Runtime.Json.Member>)client.members;
            members["test-cycle-child"] = new ClassMember
            {
                id = "test-cycle-child", name = "Child", kind = MemberKind.Class,
                classId = "test-cycle-class",
            };
            ((System.Collections.Generic.Dictionary<string, NeoSchemaClass>)client.classes)["test-cycle-class"] =
                new NeoSchemaClass
                {
                    id = "test-cycle-class",
                    name = "Cycle",
                    schema = new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["Child"] = "test-cycle-child",
                    },
                };
            client.SetSaveValue(new ObjectMemberValue
            {
                id = "test-cycle-a", classId = "test-cycle-class",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["Child"] = "test-cycle-b",
                },
            });
            client.SetSaveValue(new ObjectMemberValue
            {
                id = "test-cycle-b", classId = "test-cycle-class",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["Child"] = "test-cycle-a",
                },
            });

            Assert.DoesNotThrow(() => client.RemoveSaveValueAndDescendants("test-cycle-a"));
            Assert.IsFalse(client.saveValues.ContainsKey("test-cycle-a"));
            Assert.IsFalse(client.saveValues.ContainsKey("test-cycle-b"));
        }

        [Test]
        public void TypedUnlinkedRemoval_CollectsDetachedDictionaryAndListRows()
        {
            var client = LoadClient();
            var dictionaryMember = new DictionaryMember
            {
                id = "test-detached-dictionary-member",
                name = "Detached dictionary",
                kind = MemberKind.Dictionary,
                KeyKind = NeoDictionaryKeyKind.String,
                entryMemberId = "member-tags",
            };
            client.SetSaveValue(new StringMemberValue
            {
                id = "test-detached-leaf", value = "leaf",
            });
            client.SetSaveValue(new ArrayMemberValue
            {
                id = "test-detached-list", value = new[] { "test-detached-leaf" },
            });
            client.SetSaveValue(new ObjectMemberValue
            {
                id = "test-detached-dictionary",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["entry"] = "test-detached-list",
                },
            });

            client.RemoveWritableValueAndDescendantsIfUnlinked(
                NeoValueOwnership.Save,
                "test-detached-dictionary",
                dictionaryMember);

            Assert.IsFalse(client.saveValues.ContainsKey("test-detached-dictionary"));
            Assert.IsFalse(client.saveValues.ContainsKey("test-detached-list"));
            Assert.IsFalse(client.saveValues.ContainsKey("test-detached-leaf"));
        }

        [Test]
        public void TypedRemoval_DoesNotCrossDeclaredStorageOwnership()
        {
            var client = LoadClient();
            var members =
                (System.Collections.Generic.Dictionary<string, NeoCompose.Runtime.Json.Member>)client.members;
            members["test-session-entry"] = new StringMember
            {
                id = "test-session-entry",
                name = "Session entry",
                kind = MemberKind.String,
                Storage = NeoMemberStorage.Session,
            };
            var dictionaryMember = new DictionaryMember
            {
                id = "test-cross-storage-dictionary",
                name = "Cross-storage dictionary",
                kind = MemberKind.Dictionary,
                KeyKind = NeoDictionaryKeyKind.String,
                entryMemberId = "test-session-entry",
            };
            client.SetWritableValue(NeoValueOwnership.Session, new StringMemberValue
            {
                id = "test-session-owned-leaf", value = "keep",
            });
            client.SetSaveValue(new ObjectMemberValue
            {
                id = "test-save-dictionary",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["entry"] = "test-session-owned-leaf",
                },
            });

            client.RemoveWritableValueAndDescendants(
                NeoValueOwnership.Save,
                "test-save-dictionary",
                dictionaryMember);

            Assert.IsFalse(client.saveValues.ContainsKey("test-save-dictionary"));
            Assert.IsTrue(client.sessionValues.ContainsKey("test-session-owned-leaf"));
        }

        // -----------------------------------------------------------------
        // Tombstone removal (mark: "removed").
        // -----------------------------------------------------------------

        [Test]
        public void ClassUnset_TombstonesFieldSparsely_ResolvesUnset()
        {
            var client = LoadClient();
            var heroMember = RequireMember<ClassMember>(client, "member-hero");
            var hero = (NeoMemberClassWritable)NeoMember.CreateWritable(client, heroMember, null);
            NeoGeneratedTypesSupport.SetValue(
                hero, "Name", NeoGeneratedTypesSupport.Value("Aragorn"));
            string nameId = hero.Get<NeoMemberString>("Name").overrideValueId!;

            hero.Unset("Name");

            // Sparse: the record still references the key (it is not dropped), but
            // the child resolves as unset through the tombstone at its stable id.
            Assert.IsTrue(hero.value!.value!.ContainsKey("Name"));
            Assert.AreEqual(nameId, hero.value.value["Name"]);
            Assert.IsTrue(client.sessionValues.TryGetValue(nameId, out MemberValue? row));
            Assert.IsTrue(row!.IsRemoved);
            Assert.IsTrue(hero.TryGet("Name", out NeoMemberString? refetched));
            Assert.IsNull(refetched!.value);
        }

        [Test]
        public void ClassUnset_RequiredField_Throws()
        {
            var client = LoadClient();
            var heroMember = RequireMember<ClassMember>(client, "member-hero");
            var hero = (NeoMemberClassWritable)NeoMember.CreateWritable(client, heroMember, null);
            NeoGeneratedTypesSupport.SetValue(
                hero, "Name", NeoGeneratedTypesSupport.Value("Aragorn"));
            RequireMember<StringMember>(client, "member-name").DeclaredRequirement = NeoMemberRequirementKind.Required;

            Assert.Throws<System.InvalidOperationException>(() => hero.Unset("Name"));
        }

        [Test]
        public void ClassUnset_HardRemovesField_ReclaimsOrphanedSubtree()
        {
            var client = LoadClient();
            // Shadow the save root referencing a Heroes list → one hero → a Name
            // leaf, all written into the save store.
            client.SetSaveValue(new ObjectMemberValue
            {
                id = "v-root-save", classId = "class-root", createdAt = "x", updatedAt = "x",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["Heroes"] = "heroes-list",
                },
            });
            client.SetSaveValue(new ArrayMemberValue
            {
                id = "heroes-list", createdAt = "x", updatedAt = "x",
                value = new[] { "hero-1" },
            });
            client.SetSaveValue(new ObjectMemberValue
            {
                id = "hero-1", classId = "class-hero", createdAt = "x", updatedAt = "x",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["Name"] = "hero-1-name",
                },
            });
            client.SetSaveValue(new StringMemberValue
            {
                id = "hero-1-name", createdAt = "x", updatedAt = "x", value = "Aragorn",
            });
            Assert.IsTrue(client.saveValues.ContainsKey("hero-1"));
            Assert.IsTrue(client.saveValues.ContainsKey("hero-1-name"));

            client.save.Unset("Heroes");

            // The Heroes list is tombstoned in place; the orphaned hero + its Name
            // leaf are reclaimed from the save store (hard remove).
            Assert.IsTrue(client.saveValues.TryGetValue("heroes-list", out MemberValue? listRow));
            Assert.IsTrue(listRow!.IsRemoved);
            Assert.IsFalse(client.saveValues.ContainsKey("hero-1"));
            Assert.IsFalse(client.saveValues.ContainsKey("hero-1-name"));
            // Sparse: the root still references the Heroes slot (record untouched).
            var rootRow = (ObjectMemberValue)client.saveValues["v-root-save"];
            Assert.AreEqual("heroes-list", rootRow.value!["Heroes"]);
        }
    }
}
