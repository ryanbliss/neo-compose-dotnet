// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P108 Set members: the generated SDK wrappers (§5) over the synth
    /// fixture, and the evaluator's Set semantics (§4.3) over a hand-built
    /// project.
    /// </summary>
    public class NeoSetTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        private static TestProjectNeo LoadGeneratedClient()
        {
            var stack = NeoTestSaveStack.Create(File.ReadAllText(Path.Combine(PackageRoot, "synth-example.json")));
            return new TestProjectNeo(NeoTestSaveStack.LoadSynchronously(stack.Synchronizer));
        }

        [Test]
        public void NeoSet_AddReturnsFalseOnADuplicateAndWritesNothing()
        {
            var app = LoadGeneratedClient();
            NeoSet<int> lucky = app.Save.Lucky;
            Assert.IsTrue(lucky.Add(3));
            Assert.IsTrue(lucky.Add(7));
            var changes = 0;
            using var subscription = lucky.OnChanged((_, _) => changes++);
            string before = app.SerializeSaveData();

            Assert.IsFalse(lucky.Add(3));

            Assert.AreEqual(0, changes);
            Assert.AreEqual(before, app.SerializeSaveData());
            CollectionAssert.AreEqual(new[] { 3, 7 }, lucky.ToArray());
            Assert.IsTrue(lucky.Contains(7));
            Assert.IsTrue(lucky.Remove(7));
            Assert.IsFalse(lucky.Remove(7));
            Assert.IsFalse(lucky.Contains(7));
            CollectionAssert.AreEqual(new[] { 3 }, lucky.ToArray());
        }

        [Test]
        public void NeoSet_AlgebraRemovesEntriesWithOneWrite()
        {
            var app = LoadGeneratedClient();
            NeoSet<int> lucky = app.Save.Lucky;
            lucky.UnionWith(new[] { 1, 2, 3, 4, 2 });
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, lucky.ToArray());
            var changes = 0;
            using var subscription = lucky.OnChanged((_, _) => changes++);

            lucky.ExceptWith(new[] { 1, 3, 9 });

            Assert.AreEqual(1, changes);
            CollectionAssert.AreEqual(new[] { 2, 4 }, lucky.ToArray());

            lucky.IntersectWith(new[] { 4, 5 });

            Assert.AreEqual(2, changes);
            CollectionAssert.AreEqual(new[] { 4 }, lucky.ToArray());

            lucky.ExceptWith(new[] { 9 });

            Assert.AreEqual(2, changes);
            Assert.IsTrue(lucky.SetEquals(new[] { 4, 4 }));
        }

        [Test]
        public void NeoSet_ValueEntriesComeBackAsReadOnlyWrappers()
        {
            var app = LoadGeneratedClient();
            NeoSet<NeoReadOnlyVector2> waypoints = app.Save.Waypoints;
            Assert.IsTrue(waypoints.Add(new NeoReadOnlyVector2(0f, 1f)));

            // -0 keys like 0 (§3).
            Assert.IsFalse(waypoints.Add(new NeoReadOnlyVector2(-0f, 1f)));

            NeoReadOnlyVector2 entry = waypoints.Single();
            Assert.IsNotInstanceOf<NeoVector2>(entry);
            Assert.AreEqual(new Vector2(0f, 1f), entry.Value);
        }

        [Test]
        public void GenericSet_BoundToAWritableWrapperRejectsEntryEdits()
        {
            var app = LoadGeneratedClient();
            NeoSet<NeoVector2> tags = app.Save.VectorBag.Tags;
            Assert.IsTrue(tags.Add(new NeoVector2(1f, 2f)));

            NeoVector2 entry = tags.Single();
            var error = Assert.Throws<InvalidOperationException>(() => entry.x = 5f);
            StringAssert.Contains("compares entries by value", error.Message);

            Assert.AreEqual(new Vector2(1f, 2f), tags.Single().Value);
            Assert.IsFalse(tags.Add(new NeoVector2(1f, 2f)));
        }

        [Test]
        public void NeoSet_ClassEntriesCompareByIdentity()
        {
            var app = LoadGeneratedClient();
            NeoSet<Hero> allies = app.Save.Allies;
            Assert.IsTrue(allies.Add(new Hero(Name: "Ann")));
            Hero ann = allies.Single();

            Assert.IsFalse(allies.Add(ann));
            Assert.IsTrue(allies.Add(new Hero(Name: "Ann")), "an equal-field Hero is a distinct entry");
            Assert.AreEqual(2, allies.Count);
            Assert.IsTrue(allies.Contains(ann));
        }

        [Test]
        public void NeoEnumSet_WritesOptionIds()
        {
            var app = LoadGeneratedClient();
            NeoEnumSet<Element> elements = app.Save.Elements;

            Assert.IsTrue(elements.Add(Element.ice));
            Assert.IsTrue(elements.Add(Element.fire));
            Assert.IsFalse(elements.Add(Element.ice));

            CollectionAssert.AreEqual(new[] { Element.ice, Element.fire }, elements.ToArray());
            string[] stored = JObject.Parse(app.SerializeSaveData())
                .Descendants()
                .OfType<JArray>()
                .Select(array => array.Select(token => token.ToString()).ToArray())
                .Single(array => array.Contains(Element.ice.OptionId()));
            CollectionAssert.AreEqual(new[] { Element.ice.OptionId(), Element.fire.OptionId() }, stored);
        }

        [Test]
        public void InterfaceSetProperty_AcceptsEverySetImplementer()
        {
            var app = LoadGeneratedClient();
            var owned = new OwnedCrew(Crew: new[] { new Hero(Name: "Ann") }, Tags: new[] { Element.fire });
            app.Save.Allies.Add(new Hero(Name: "Bo"));
            Hero picked = app.Save.Allies.Single();
            var lookup = new PickedCrew(Crew: new[] { new NeoLookupSelection(picked.valueId!) });

            ICrew[] crews = { owned, lookup };
            INeoReadOnlySet<IReadOnlyHero>[] heroes = crews.Select(crew => crew.Crew).ToArray();
            INeoReadOnlySet<Element> tags = ((ICrew)owned).Tags;

            Assert.IsInstanceOf<NeoReadOnlySet<IReadOnlyHero>>(heroes[0]);
            Assert.IsInstanceOf<NeoReadOnlyLookupSet<IReadOnlyHero>>(heroes[1]);
            Assert.IsInstanceOf<NeoReadOnlyEnumSet<Element>>(tags);
            Assert.AreEqual("Ann", heroes[0].Single().Name);
            Assert.IsTrue(heroes[1].Contains(picked));
            Assert.IsTrue(tags.Contains(Element.fire));
        }

        [Test]
        public void ToSet_KeepsTheFirstOccurrenceInOrderByValue()
        {
            using var script = new Script(BuildProjectData());
            script.scope["xs"] = new object?[] { 3.0, 1.0, 3.0, 0.0, 2.0, -0.0, 1.0 };

            object? result = script.Eval(ToSet("xs", reference: false));

            CollectionAssert.AreEqual(new object?[] { 3.0, 1.0, 0.0, 2.0 }, (object?[])result!);
        }

        [Test]
        public void ToSet_KeepsTheFirstOccurrenceInOrderByIdentity()
        {
            using var script = new Script(BuildProjectData());
            object? ann = script.Eval(Ref("ally-a"));
            object? bo = script.Eval(Ref("ally-b"));
            // Same Name as ally-a, different row.
            object? otherAnn = script.Eval(Ref("box-a"));
            script.scope["xs"] = new[] { ann, bo, ann, otherAnn, bo };

            object? result = script.Eval(ToSet("xs", reference: true));

            CollectionAssert.AreEqual(new[] { ann, bo, otherAnn }, (object?[])result!);
        }

        [Test]
        public void StoredSet_AddOfAPresentKeyIsANoOpAndRemoveContainsMatchByKey()
        {
            using var script = new Script(BuildProjectData());
            Pointer lucky = Ref(LuckyValueId);
            var target = new WriteTarget
            {
                pointer = lucky,
                typeInfo = SetOf(new PrimitiveTypeInfo { type = MemberKind.Int, required = true }),
                writability = WritabilityKind.Save,
            };

            script.Mutate(target, CollectionMutationKind.Add, Int(3));
            CollectionAssert.AreEqual(new[] { "lucky-3", "lucky-7" }, script.EntryIds(LuckyValueId));
            script.Mutate(target, CollectionMutationKind.Add, Int(5));
            CollectionAssert.AreEqual(new double?[] { 3, 7, 5 }, script.Numbers(LuckyValueId));
            Assert.AreEqual(true, script.Eval(Contains(lucky, Int(7), reference: false)));

            script.Mutate(target, CollectionMutationKind.Remove, Int(7));

            CollectionAssert.AreEqual(new double?[] { 3, 5 }, script.Numbers(LuckyValueId));
            Assert.AreEqual(false, script.Eval(Contains(lucky, Int(7), reference: false)));
        }

        /// <summary>
        /// Within one execution the Set's pending entries answer Add, Remove,
        /// and Contains by key without committing the batch.
        /// </summary>
        [Test]
        public void PendingSet_MatchesByKeyAcrossOneExecution()
        {
            using var script = new Script(BuildProjectData());
            Pointer lucky = Ref(LuckyValueId);
            var target = new WriteTarget
            {
                pointer = lucky,
                typeInfo = SetOf(new PrimitiveTypeInfo { type = MemberKind.Int, required = true }),
                writability = WritabilityKind.Save,
            };

            script.client.EnterScriptWrites();
            try
            {
                script.Mutate(target, CollectionMutationKind.Add, Int(5));
                Assert.AreEqual(true, script.Eval(Contains(lucky, Int(5), reference: false)));
                script.Mutate(target, CollectionMutationKind.Add, Int(5));
                script.Mutate(target, CollectionMutationKind.Remove, Int(3));
                Assert.AreEqual(false, script.Eval(Contains(lucky, Int(3), reference: false)));
                script.Mutate(target, CollectionMutationKind.Add, Int(3));
                Assert.AreEqual(true, script.Eval(Contains(lucky, Int(3), reference: false)));
            }
            finally
            {
                script.client.ExitScriptWrites();
            }

            CollectionAssert.AreEqual(new double?[] { 7, 5, 3 }, script.Numbers(LuckyValueId));
        }

        /// <summary>
        /// P108 §6. Repeated <c>Contains</c> on a stored value Set reads each
        /// entry row once per getter capture, and a new capture reads again.
        /// </summary>
        [Test]
        public void StoredSetContains_ReadsEachEntryOncePerCapture()
        {
            using var script = new Script(BuildProjectData());
            Pointer lucky = Ref(LuckyValueId);
            int ReadsOf(NeoClient.GetterCaptureFrame capture, string id)
            {
                int reads = 0;
                for (int i = 0; i < capture.valueReads!.Count; i++)
                {
                    if (capture.valueReads.items[i] == id)
                        reads++;
                }
                return reads;
            }

            using (script.client.CaptureValueReads(new HashSet<string>()))
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    var enclosing = script.client.BeginGetterReadCapture();
                    Assert.AreEqual(true, script.Eval(Contains(lucky, Int(3), reference: false)));
                    Assert.AreEqual(true, script.Eval(Contains(lucky, Int(7), reference: false)));
                    Assert.AreEqual(false, script.Eval(Contains(lucky, Int(9), reference: false)));
                    var capture = script.client.EndGetterReadCapture(enclosing);
                    Assert.AreEqual(1, ReadsOf(capture, "lucky-3"), $"pass {pass}");
                    Assert.AreEqual(1, ReadsOf(capture, "lucky-7"), $"pass {pass}");
                    script.client.RecycleGetterCapture(capture);
                }
            }
        }

        /// <summary>
        /// Stored dedupe follows the closed entry member, not the IR's static
        /// <c>reference</c> flag: a T bound to a Class compares by identity
        /// whether or not T is constrained.
        /// </summary>
        [Test]
        public void GenericSet_BoundToAClassComparesByIdentity([Values(false, true)] bool constrained)
        {
            ProjectData data = BuildProjectData();
            if (constrained)
            {
                data.classes[BoxClassId].genericParams![0].constraint = new GenericParamConstraint
                {
                    kind = NeoGenericParamConstraintKind.Class,
                    classId = ItemClassId,
                };
            }
            using var script = new Script(data);
            Pointer unique = Ref(BoxUniqueValueId);
            var target = new WriteTarget
            {
                pointer = unique,
                typeInfo = SetOf(new GenericTypeInfo
                {
                    type = MemberKind.Generic,
                    required = true,
                    ownerClassId = BoxClassId,
                    genericParamId = BoxParamId,
                }),
                writability = WritabilityKind.Save,
            };

            script.Mutate(target, CollectionMutationKind.Add, Ref("box-a"));
            CollectionAssert.AreEqual(new[] { "box-a" }, script.EntryIds(BoxUniqueValueId));
            Assert.AreEqual(true, script.Eval(Contains(unique, Ref("box-a"), reference: constrained)));
            // A free value with box-a's Name is another row: it is imported.
            script.client.SetWritableValue(NeoValueOwnership.Session, new StringMemberValue { id = "loose-name", value = "Ann" });
            script.client.SetWritableValue(NeoValueOwnership.Session, Object("loose", ItemClassId, new()
            {
                ["Name"] = "loose-name"
            }));
            Assert.AreEqual(false, script.Eval(Contains(unique, Ref("loose"), reference: constrained)));

            script.Mutate(target, CollectionMutationKind.Add, Ref("loose"));

            Assert.AreEqual(2, script.EntryIds(BoxUniqueValueId).Length);
            Assert.AreEqual(true, script.Eval(Contains(unique, Ref("box-a"), reference: constrained)));
        }

        /// <summary>
        /// A <c>Set&lt;T&gt;</c> parameter dispatches its mutations on the
        /// container it holds: an owned Set keys its entries, a Lookup set
        /// keys its selected ids.
        /// </summary>
        [Test]
        public void SetParameter_HoldsAnOwnedSetOrALookupSet([Values(AlliesValueId, PickedValueId)] string held)
        {
            using var script = new Script(BuildProjectData());
            script.scope["crew"] = script.Eval(Ref(held));
            var crew = new VariablePointer { type = PointerKind.Variable, variableId = "crew" };
            var target = new WriteTarget
            {
                pointer = crew,
                typeInfo = new LookupTypeInfo
                {
                    type = MemberKind.Lookup,
                    required = true,
                    entryTypeInfo = ItemType,
                },
                writability = WritabilityKind.Local,
            };

            script.Mutate(target, CollectionMutationKind.Add, Ref("ally-a"));
            CollectionAssert.AreEqual(new[] { "ally-a", "ally-b" }, script.EntryIds(held), "ally-a is already held");
            Assert.AreEqual(true, script.Eval(Contains(crew, Ref("ally-a"), reference: true)));

            script.Mutate(target, CollectionMutationKind.Remove, Ref("ally-a"));

            CollectionAssert.AreEqual(new[] { "ally-b" }, script.EntryIds(held));
            Assert.AreEqual(false, script.Eval(Contains(crew, Ref("ally-a"), reference: true)));
        }

        /// <summary>One script run's client, context, and scope.</summary>
        private sealed class Script : System.IDisposable
        {
            internal readonly NeoClient client;
            internal readonly NSGetterEvaluator.Context ctx;
            internal readonly Dictionary<string, object?> scope = new();

            internal Script(ProjectData data)
            {
                client = NeoTestSaveStack.ClientFromSchema(data);
                ctx = new NSGetterEvaluator.Context(client, null, null);
            }

            internal object? Eval(Pointer pointer) => NSGetterEvaluator.EvaluatePointer(pointer, scope, ctx);

            internal void Mutate(WriteTarget target, string mutation, params Pointer[] args)
            {
                NeoScriptExecutor.Execute(client, new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = System.Array.Empty<Variable>(),
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                    instructions = new Instruction[]
                    {
                        new CollectionCallInstruction
                        {
                            type = InstructionKind.CollectionCall,
                            target = target,
                            mutation = mutation,
                            args = args,
                        },
                    },
                }, scope, ctx);
            }

            internal string[] EntryIds(string rowId)
            {
                Assert.IsTrue(client.TryGetValue(rowId, out ArrayMemberValue? row), $"no row '{rowId}'");
                return row!.value!;
            }

            internal double?[] Numbers(string rowId) => EntryIds(rowId)
                .Select(id => client.TryGetValue(id, out NumberMemberValue? entry) ? entry.value : null)
                .ToArray();

            public void Dispose() => client.Dispose();
        }

        private const string ItemClassId = "item-class";
        private const string BoxClassId = "box-class";
        private const string BoxParamId = "box-param-t";
        private const string LuckyValueId = "lucky";
        private const string AlliesValueId = "allies";
        private const string PickedValueId = "picked";
        private const string BoxUniqueValueId = "box-unique";

        private static readonly ClassTypeInfo ItemType = new()
        {
            type = MemberKind.Class,
            required = true,
            classId = ItemClassId,
        };

        private static CollectionTypeInfo SetOf(TypeInfo entry) => new()
        {
            type = MemberKind.Set,
            required = true,
            entryTypeInfo = entry,
        };

        private static Pointer Ref(string valueId) => new ReferencePointer
        {
            type = PointerKind.Reference,
            valueId = valueId,
        };

        private static Pointer Int(int value) => new ValuePointer
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                value = JToken.FromObject(value),
            },
        };

        private static Pointer ToSet(string variableId, bool reference) => new FunctionPointer
        {
            type = PointerKind.Function,
            function = new CollectionQueryFunction
            {
                type = FunctionKind.CollectionQuery,
                info = new FunctionCollectionQueryInfo
                {
                    op = "ToSet",
                    collectionPointer = new VariablePointer { type = PointerKind.Variable, variableId = variableId },
                    reference = reference,
                },
            },
        };

        private static Pointer Contains(Pointer collection, Pointer value, bool reference) => new FunctionPointer
        {
            type = PointerKind.Function,
            function = new ContainsFunction
            {
                type = FunctionKind.Contains,
                info = new FunctionCollectionContainsInfo
                {
                    collectionPointer = collection,
                    valuePointer = value,
                    reference = reference,
                },
            },
        };

        /// <summary>
        /// Save root with an Int Set, an Item Set, a multi-select Item Lookup,
        /// and a <c>Box&lt;T&gt;</c> whose <c>Set&lt;T&gt;</c> is bound to Item.
        /// ally-a and box-a share a Name, so only identity tells them apart.
        /// </summary>
        private static ProjectData BuildProjectData()
        {
            var required = NeoMemberRequirementKind.Required;
            var members = new JsonMember[]
            {
                RootMember("root-assets", "root-assets-value", "root-class"),
                RootMember("root-save", "root-save-value", "save-root-class"),
                RootMember("root-session", "root-session-value", "root-class"),
                new SetMember { id = "lucky-member", name = "Lucky", kind = MemberKind.Set, entryMemberId = "lucky-entry", Requirement = required },
                new IntMember { id = "lucky-entry", name = "Lucky Entry", kind = MemberKind.Int, Requirement = required },
                new SetMember { id = "allies-member", name = "Allies", kind = MemberKind.Set, entryMemberId = "ally-entry", Requirement = required },
                new ClassMember { id = "ally-entry", name = "Ally", kind = MemberKind.Class, classId = ItemClassId, Requirement = required },
                new LookupMember
                {
                    id = "picked-member",
                    name = "Picked",
                    kind = MemberKind.Lookup,
                    declaredTypeInfo = ItemType,
                    Selection = NeoMemberSelectionKind.Multi,
                    Requirement = required,
                },
                new ClassMember { id = "box-member", name = "Box", kind = MemberKind.Class, classId = BoxClassId, Requirement = required },
                new SetMember { id = "box-unique-member", name = "Unique", kind = MemberKind.Set, entryMemberId = "box-entry", Requirement = required },
                new GenericMember { id = "box-entry", name = "Entry", kind = MemberKind.Generic, genericParamId = BoxParamId, Requirement = required },
                new ClassMember { id = "box-binding", name = "T", kind = MemberKind.Class, classId = ItemClassId, Requirement = required },
                new StringMember { id = "item-name", name = "Name", kind = MemberKind.String },
            };
            var bindings = new Dictionary<string, string> { [BoxParamId] = "box-binding" };
            var values = new MemberValue[]
            {
                Object("root-assets-value", "root-class", new()),
                Object("root-session-value", "root-class", new()),
                Object("root-save-value", "save-root-class", new()
                {
                    ["Lucky"] = LuckyValueId,
                    ["Allies"] = AlliesValueId,
                    ["Picked"] = PickedValueId,
                    ["Box"] = "box",
                }),
                new ArrayMemberValue { id = LuckyValueId, value = new[] { "lucky-3", "lucky-7" } },
                new NumberMemberValue { id = "lucky-3", value = 3 },
                new NumberMemberValue { id = "lucky-7", value = 7 },
                new ArrayMemberValue { id = AlliesValueId, value = new[] { "ally-a", "ally-b" } },
                Object("ally-a", ItemClassId, new() { ["Name"] = "ally-a-name" }),
                new StringMemberValue { id = "ally-a-name", value = "Ann" },
                Object("ally-b", ItemClassId, new() { ["Name"] = "ally-b-name" }),
                new StringMemberValue { id = "ally-b-name", value = "Bo" },
                new ArrayMemberValue { id = PickedValueId, value = new[] { "ally-a", "ally-b" } },
                new ObjectMemberValue
                {
                    id = "box",
                    classId = BoxClassId,
                    genericBindings = bindings,
                    value = new Dictionary<string, string> { ["Unique"] = BoxUniqueValueId },
                },
                new ArrayMemberValue { id = BoxUniqueValueId, genericBindings = bindings, value = new[] { "box-a" } },
                Object("box-a", ItemClassId, new() { ["Name"] = "box-a-name" }),
                new StringMemberValue { id = "box-a-name", value = "Ann" },
            };
            var classes = new[]
            {
                Class("root-class", new()),
                Class("save-root-class", new()
                {
                    ["Lucky"] = "lucky-member",
                    ["Allies"] = "allies-member",
                    ["Picked"] = "picked-member",
                    ["Box"] = "box-member",
                }),
                Class(ItemClassId, new() { ["Name"] = "item-name" }),
                Class(BoxClassId, new() { ["Unique"] = "box-unique-member" }),
            };
            classes[3].genericParams = new List<GenericParamDeclaration> { new() { id = BoxParamId, name = "T" } };
            foreach (JsonMember member in members)
                member.projectId = "project-sets";

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-sets",
                    _id = "project-sets",
                    name = "Sets",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = members.ToDictionary(member => member.id),
                values = values.ToDictionary(value => value.id),
                classes = classes.ToDictionary(schemaClass => schemaClass.id),
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            }.WithUserRoot();
        }

        private static ClassMember RootMember(string id, string valueId, string classId) => new()
        {
            id = id,
            name = id,
            kind = MemberKind.Class,
            Requirement = NeoMemberRequirementKind.Required,
            valueId = valueId,
            classId = classId,
        };

        private static ObjectMemberValue Object(string id, string classId, Dictionary<string, string> record) => new()
        {
            id = id,
            classId = classId,
            value = record,
        };

        private static NeoSchemaClass Class(string id, Dictionary<string, string> schema) => new()
        {
            id = id,
            projectId = "project-sets",
            name = id,
            schema = schema,
        };
    }
}
