// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P98 data hooks on the effect fixture. Check only counts its runs, so
    /// every record is a hook's:
    /// <code>
    /// class Plant : IOnLoad, IOnUnload {
    ///   void OnLoad() { Record(this.Count + 1000); }
    ///   void OnUnload() { Record(this.Count + 2000); } }
    /// </code>
    /// The listener tests give the Session root an action plants subscribe to:
    /// <code>
    /// class SessionRoot { NeoAction OnTick = []; }
    /// class Plant { void Tick() { Record(this.Count + 4000); } }
    /// </code>
    /// </summary>
    public partial class NeoEffectTests
    {
        private const string OnLoadInterfaceId = "system_c1b7f48c-5615-4e61-a071-5ff8b2a70c97";
        private const string OnUnloadInterfaceId = "system_c64e99f9-e8be-4d0a-927f-de20bdf9cdeb";
        private const string OnTickValueId = "value-on-tick";

        [Test]
        public void OnLoadRunsOnceForEveryLiveInstanceAndOnAttach()
        {
            using Fixture fixture = BuildHooked(plants: 2);
            fixture.client.StartScriptRuntime();
            fixture.client.StartScriptRuntime();

            CollectionAssert.AreEquivalent(
                new[] { ("plant-derived", 1000), ("plant-0", 1000), ("plant-1", 1001) },
                fixture.recorded,
                "A row no root holds is not live.");

            fixture.recorded.Clear();
            fixture.WriteNumber("plant-0-count", "member-count", 5);
            Assert.AreEqual(0, fixture.recorded.Count, "A write is no transition.");

            fixture.client.SetWritableValues(NeoValueOwnership.Save, new MemberValue[]
            {
                Number("plant-new-count", 2),
                Number("plant-new-runs", 0),
                Plant("plant-new", "class-plant"),
                PlantList("plant-derived", "plant-0", "plant-1", "plant-new"),
            });
            CollectionAssert.AreEqual(new[] { ("plant-new", 1002) }, fixture.recorded);
        }

        [Test]
        public void OnLoadRunsOnceForEachOfAThousandInstances()
        {
            // P98 §8: start runs each OnLoad once, and attaching one runs one.
            const int plants = 1_000;
            using Fixture fixture = BuildHooked(plants);
            fixture.client.StartScriptRuntime();
            var loaded = new HashSet<string>();
            foreach (var (plant, _) in fixture.recorded)
                Assert.IsTrue(loaded.Add(plant), plant);
            Assert.AreEqual(plants + 1, loaded.Count);

            fixture.recorded.Clear();
            var plantIds = new string[plants + 2];
            plantIds[0] = "plant-derived";
            for (int i = 0; i < plants; i++)
                plantIds[i + 1] = $"plant-{i}";
            plantIds[^1] = "plant-new";
            fixture.client.SetWritableValues(NeoValueOwnership.Save, new MemberValue[]
            {
                Number("plant-new-count", 2),
                Number("plant-new-runs", 0),
                Plant("plant-new", "class-plant"),
                PlantList(plantIds),
            });

            CollectionAssert.AreEqual(new[] { ("plant-new", 1002) }, fixture.recorded);
        }

        [Test]
        public void OnUnloadReadsTheRemovedInstancesLastRows()
        {
            using Fixture fixture = BuildHooked(plants: 2);
            fixture.client.StartScriptRuntime();
            fixture.recorded.Clear();

            RemovePlant(fixture, "plant-1", "plant-derived", "plant-0");

            CollectionAssert.AreEqual(new[] { ("plant-1", 2001) }, fixture.recorded);
            Assert.IsFalse(
                fixture.client.TryGetValue(NeoValueOwnership.Save, "plant-1-count", out _),
                "The drain dropped the departed rows.");
        }

        [Test]
        public void AHookWriteToItsRemovedInstanceFailsAndLogs()
        {
            // void OnUnload() { this.Runs = 7; }
            using Fixture fixture = BuildHooked(plants: 1, onUnload: new Instruction[] { Assign(KeyOf(This(), "Runs"), Literal(7)) });
            fixture.client.StartScriptRuntime();

            LogAssert.Expect(LogType.Exception, new Regex("left the data"));
            RemovePlant(fixture, "plant-0", "plant-derived");

            Assert.IsFalse(
                fixture.client.TryGetValue(NeoValueOwnership.Save, "plant-0-runs", out _),
                "The failed write restored nothing.");
        }

        [Test]
        public void AClassSwapRunsTheOldOnUnloadThenTheNewOnLoad()
        {
            // class DerivedPlant : Plant { override void OnLoad() { Record(this.Count + 3000); } }
            using Fixture fixture = BuildHooked(plants: 1, configure: data =>
            {
                data.members["member-on-load-derived"] = ScriptFunction(
                    "member-on-load-derived", "OnLoad", null, "member-on-load",
                    RecordCall(Add(KeyOf(This(), "Count"), Literal(3000))));
                data.classes["class-derived-plant"].schema["OnLoad"] = "member-on-load-derived";
            });
            fixture.client.StartScriptRuntime();
            fixture.recorded.Clear();

            var same = new NeoWritePlan(fixture.client);
            same.Set(NeoValueOwnership.Save, Plant("plant-0", "class-plant"));
            same.Commit();
            Assert.AreEqual(0, fixture.recorded.Count, "A replacement of the same class is the same instance.");

            var swap = new NeoWritePlan(fixture.client);
            swap.Set(NeoValueOwnership.Save, Plant("plant-0", "class-derived-plant"));
            swap.Commit();

            CollectionAssert.AreEqual(new[] { ("plant-0", 2000), ("plant-0", 3000) }, fixture.recorded);
        }

        [Test]
        public void APartitionUnloadRunsOnUnloadOverItsDepartedRows()
        {
            using Fixture fixture = BuildHooked(plants: 0, configure: data =>
            {
                // An authored plant has no Save row for Check to count in.
                data.members["member-check"] = ScriptFunction("member-check", "Check", NeoEffectKind.Auto, null);
                data.valuePartitions = new Dictionary<string, JToken>
                {
                    ["garden"] = JObject.FromObject(new Dictionary<string, MemberValue>
                    {
                        ["value-bed"] = new ArrayMemberValue { id = "value-bed", value = Array.Empty<string>() },
                        ["bed-plant-count"] = Number("bed-plant-count", 7),
                        ["bed-plant-runs"] = Number("bed-plant-runs", 0),
                        ["bed-plant"] = Plant("bed-plant", "class-plant", containerId: "value-bed"),
                    }),
                };
            });
            fixture.client.StartScriptRuntime();
            fixture.client.LoadValuePartition("garden");
            Assert.AreEqual(("bed-plant", 1007), fixture.recorded[^1]);

            fixture.client.UnloadValuePartition("garden");

            Assert.AreEqual(("bed-plant", 2007), fixture.recorded[^1]);
        }

        [Test]
        public void APartitionUnloadsOnUnloadUnsubscribesFromASessionAction()
        {
            using Fixture fixture = BuildHooked(
                plants: 0,
                onUnload: new Instruction[] { RecordCall(Add(KeyOf(This(), "Count"), Literal(2000))), Unsubscribe() },
                onLoad: new Instruction[] { Subscribe() },
                configure: data =>
                {
                    AddSessionAction(data);
                    data.members["member-check"] = ScriptFunction("member-check", "Check", NeoEffectKind.Auto, null);
                    data.valuePartitions = new Dictionary<string, JToken>
                    {
                        ["garden"] = JObject.FromObject(new Dictionary<string, MemberValue>
                        {
                            ["value-bed"] = new ArrayMemberValue { id = "value-bed", value = Array.Empty<string>() },
                            ["bed-plant-count"] = Number("bed-plant-count", 7),
                            ["bed-plant-runs"] = Number("bed-plant-runs", 0),
                            ["bed-plant"] = Plant("bed-plant", "class-plant", containerId: "value-bed"),
                        }),
                    };
                });
            fixture.client.StartScriptRuntime();
            fixture.client.LoadValuePartition("garden");
            CollectionAssert.AreEquivalent(new[] { "plant-derived", "bed-plant" }, TickListeners(fixture));

            fixture.client.UnloadValuePartition("garden");

            Assert.AreEqual(("bed-plant", 2007), fixture.recorded[^1]);
            CollectionAssert.AreEqual(new[] { "plant-derived" }, TickListeners(fixture));
        }

        [Test]
        public void OnUnloadRemovesItsListenerFromASessionAction()
        {
            using Fixture fixture = BuildSubscribed(plants: 1);
            CollectionAssert.AreEquivalent(new[] { "plant-derived", "plant-0" }, TickListeners(fixture));

            RemovePlant(fixture, "plant-0", "plant-derived");

            CollectionAssert.AreEqual(new[] { "plant-derived" }, TickListeners(fixture));
            TickAction(fixture).Invoke();
            CollectionAssert.AreEqual(new[] { ("plant-derived", 4000) }, fixture.recorded);
        }

        [Test]
        public void ASameClassReplacementKeepsItsListenersFiring()
        {
            using Fixture fixture = BuildSubscribed(plants: 1);
            fixture.WriteNumber("plant-0-count", "member-count", 5);

            var same = new NeoWritePlan(fixture.client);
            same.Set(NeoValueOwnership.Save, Plant("plant-0", "class-plant"));
            same.Commit();
            TickAction(fixture).Invoke();

            CollectionAssert.AreEquivalent(new[] { ("plant-derived", 4000), ("plant-0", 4005) }, fixture.recorded);
        }

        [Test]
        public void AGetterTemporaryNeverRunsOnLoad()
        {
            // class Watcher { int Fresh => new Plant { Count = 9 }.Count; }
            using Fixture fixture = BuildHooked(plants: 0, configure: data =>
            {
                var plant = new ClassTypeInfo { type = MemberKind.Class, classId = "class-plant", required = true };
                var fresh = new FunctionPointer
                {
                    type = PointerKind.Function,
                    function = new ClassConstructorFunction
                    {
                        type = FunctionKind.ClassConstructor,
                        info = new FunctionClassConstructorInfo
                        {
                            schemaClassInfo = plant,
                            fields = new[]
                            {
                                new FunctionClassConstructorField { schemaKey = "Count", memberId = "member-count", valuePointer = Literal(9) },
                            },
                        },
                    },
                };
                data.members["member-fresh"] = Getter("member-fresh", "Fresh", KeyOf(fresh, "Count"));
                data.classes["class-watcher"].schema["Fresh"] = "member-fresh";
            });
            fixture.client.StartScriptRuntime();
            fixture.recorded.Clear();

            NSGetterResult fresh = new NeoMemberClassWritable(fixture.client, "member-watcher", "value-watcher", NeoValueOwnership.Save)
                .Get<NeoMemberNSProperty>("Fresh")
                .Compute("value-watcher");
            fixture.WriteNumber("value-target", "member-target", 1);

            Assert.AreEqual(9, Convert.ToInt32(fresh.value), fresh.error);
            Assert.AreEqual(0, fixture.recorded.Count);
        }

        [Test]
        public void DisposingTheClientRunsEveryLiveOnUnload()
        {
            Fixture fixture = BuildHooked(plants: 1);
            fixture.client.StartScriptRuntime();
            fixture.recorded.Clear();

            fixture.Dispose();

            CollectionAssert.AreEquivalent(new[] { ("plant-derived", 2000), ("plant-0", 2000) }, fixture.recorded);
        }

        [Test]
        public void AReloadRunsOnlyTheOnLoadAClassGained()
        {
            ProjectData schema = null!;
            using Fixture fixture = Build(plants: 1, plantCheck: CountCheckRun(), configure: data => schema = data);
            fixture.client.StartScriptRuntime();

            AddHook(schema, "member-on-load", "OnLoad", OnLoadInterfaceId, RecordCall(Add(KeyOf(This(), "Count"), Literal(1000))));
            fixture.client.InvalidateSchemaResolutionCaches();
            CollectionAssert.AreEquivalent(new[] { ("plant-derived", 1000), ("plant-0", 1000) }, fixture.recorded);

            fixture.client.InvalidateSchemaResolutionCaches();
            Assert.AreEqual(2, fixture.recorded.Count, "A reload that changes no hook runs none.");
        }

        [Test]
        public void AFailingHookLogsAndTheOthersRun()
        {
            using Fixture fixture = BuildHooked(plants: 2);
            fixture.throwFor = "plant-0";

            LogAssert.Expect(LogType.Exception, new Regex("Record failed for plant-0"));
            fixture.client.StartScriptRuntime();

            CollectionAssert.AreEquivalent(new[] { ("plant-derived", 1000), ("plant-1", 1001) }, fixture.recorded);
            // Disposal runs OnUnload, which records too.
            fixture.throwFor = null;
        }

        private static Fixture BuildHooked(
            int plants,
            Instruction[]? onUnload = null,
            Instruction[]? onLoad = null,
            Action<ProjectData>? configure = null) =>
            Build(plants, plantCheck: CountCheckRun(), configure: data =>
            {
                AddHook(data, "member-on-load", "OnLoad", OnLoadInterfaceId,
                    onLoad ?? new Instruction[] { RecordCall(Add(KeyOf(This(), "Count"), Literal(1000))) });
                AddHook(data, "member-on-unload", "OnUnload", OnUnloadInterfaceId,
                    onUnload ?? new Instruction[] { RecordCall(Add(KeyOf(This(), "Count"), Literal(2000))) });
                configure?.Invoke(data);
            });

        // OnLoad subscribes this.Tick to the Session action and OnUnload
        // unsubscribes it. Started, with nothing recorded.
        private static Fixture BuildSubscribed(int plants)
        {
            Fixture fixture = BuildHooked(
                plants,
                onUnload: new Instruction[] { Unsubscribe() },
                onLoad: new Instruction[] { Subscribe() },
                configure: AddSessionAction);
            fixture.client.StartScriptRuntime();
            return fixture;
        }

        private static void AddSessionAction(ProjectData data)
        {
            data.members["member-on-tick"] = new ActionMember
            {
                id = "member-on-tick",
                projectId = ProjectId,
                name = "OnTick",
                kind = MemberKind.NSAction,
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                defaultValue = new ActionMemberValueBase { value = new NeoActionValue() },
            };
            data.members["member-tick"] = ScriptFunction("member-tick", "Tick", null, null,
                RecordCall(Add(KeyOf(This(), "Count"), Literal(4000))));
            data.classes["class-plant"].schema["Tick"] = "member-tick";
            data.classes["class-session-root"] = Class("class-session-root", null, ("OnTick", "member-on-tick"));
            ((ClassMember)data.members["member-root-session"]).classId = "class-session-root";
            data.values["value-session"] = ObjectValue("value-session", "class-session-root", ("OnTick", OnTickValueId));
            data.values[OnTickValueId] = new ActionMemberValue { id = OnTickValueId, value = new NeoActionValue() };
        }

        // root.Session.OnTick += this.Tick;
        private static AddActionListenerInstruction Subscribe() => new()
        {
            type = InstructionKind.AddActionListener,
            target = TickTarget(),
            listener = TickListener(),
        };

        // root.Session.OnTick -= this.Tick;
        private static RemoveActionListenerInstruction Unsubscribe() => new()
        {
            type = InstructionKind.RemoveActionListener,
            target = TickTarget(),
            listener = TickListener(),
        };

        private static WriteTarget TickTarget() => new()
        {
            pointer = KeyOf(KeyOf(new VariablePointer { type = PointerKind.Variable, variableId = "__root__" }, "Session"), "OnTick"),
            typeInfo = TickType(),
            writability = WritabilityKind.Session,
        };

        // The literal a method group lowers to; the evaluator binds it to this.
        private static ValuePointer TickListener() => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = TickType(),
                value = new JObject { ["memberId"] = "member-tick", ["valueId"] = JValue.CreateNull() },
            },
        };

        private static ActionTypeInfo TickType() => new()
        {
            type = MemberKind.NSAction,
            required = true,
            argumentTypes = Array.Empty<TypeInfo>(),
        };

        private static NeoMemberAction TickAction(Fixture fixture) =>
            new(fixture.client, "member-on-tick", OnTickValueId, NeoValueOwnership.Session);

        // The rows the Session action's listeners bind, in stored order.
        private static List<string?> TickListeners(Fixture fixture)
        {
            var rows = new List<string?>();
            foreach (NeoDelegateValue listener in TickAction(fixture).CurrentListeners())
                rows.Add(listener.valueId);
            return rows;
        }

        // Check counts its runs and records nothing.
        private static Instruction CountCheckRun() => Assign(KeyOf(This(), "Runs"), Add(KeyOf(This(), "Runs"), Literal(1)));

        // Plant implements the hook's interface with a script member.
        private static void AddHook(ProjectData data, string memberId, string name, string interfaceId, params Instruction[] body)
        {
            data.members[memberId] = ScriptFunction(memberId, name, null, null, body);
            NeoSchemaClass plant = data.classes["class-plant"];
            plant.schema[name] = memberId;
            (plant.implementsInterfaceIds ??= new List<string>()).Add(interfaceId);
        }

        private static void RemovePlant(Fixture fixture, string plantId, params string[] kept)
        {
            var removal = new NeoWritePlan(fixture.client);
            removal.Set(NeoValueOwnership.Save, PlantList(kept));
            removal.Remove(NeoValueOwnership.Save, plantId);
            removal.Remove(NeoValueOwnership.Save, $"{plantId}-count");
            removal.Remove(NeoValueOwnership.Save, $"{plantId}-runs");
            removal.Commit();
        }
    }
}
