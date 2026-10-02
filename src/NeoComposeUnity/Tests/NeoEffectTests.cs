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
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P97 <c>@effect</c> functions. The Save root holds an unordered list
    /// of plants and a watcher slot:
    /// <code>
    /// class Plant { int Count; int Runs;
    ///   @effect void Check() { this.Runs = this.Runs + 1; Record(this.Count + root.Save.Target); }
    ///   void Plain() { this.Runs = this.Runs + 10; } }
    /// class DerivedPlant : Plant {
    ///   @effect(kind: .None) override void Check() { ... }
    ///   @effect override void Plain() { ... } }
    /// class Watcher { @effect void Watch() { root.Save.Target = root.Save.Signal; }
    ///   int Seen => root.Save.Signal + root.Save.Target;
    ///   int NativeSeen => root.Save.NativeOnly;
    ///   int Poked => this.Poke(); }
    /// </code>
    /// <c>Record</c> is native: it counts runs and reads <c>NativeSeen</c>.
    /// <c>Poke</c> is native: it writes <c>plant-0</c>'s Count.
    /// </summary>
    public class NeoEffectTests
    {
        private const string ProjectId = "project-effects";
        private const string PlantsListId = "value-plants";

        private sealed class Fixture : IDisposable
        {
            internal NeoClient client = null!;
            internal readonly List<(string plant, int value)> recorded = new();
            internal string? throwFor;
            internal NeoMemberNSProperty? nativeSeen;

            internal int RunsOf(string plantId) => (int)ReadNumber($"{plantId}-runs");

            internal double ReadNumber(string valueId)
            {
                Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Save, valueId, out MemberValue? row), valueId);
                return ((NumberMemberValue)row!).value!.Value;
            }

            internal int RecordedCount(string plantId) => recorded.FindAll(entry => entry.plant == plantId).Count;

            internal void WriteNumber(string valueId, string memberId, double value)
            {
                Assert.IsTrue(client.TryGetMember(memberId, out JsonMember? member));
                Assert.IsTrue(client.TryWriteLeaf(NeoValueOwnership.Save, Number(valueId, value), member!, "value"));
            }

            public void Dispose() => client.Dispose();
        }

        [Test]
        public void StartRunsEveryLiveInstanceOnce()
        {
            using Fixture fixture = Build(plants: 3);
            Assert.AreEqual(0, fixture.recorded.Count, "Nothing runs before the wrapper starts effects.");

            fixture.client.StartEffects();
            fixture.client.StartEffects();

            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(1, fixture.RunsOf($"plant-{i}"), "An effect's own write never runs it again.");
                Assert.AreEqual(1, fixture.RecordedCount($"plant-{i}"));
            }
            Assert.AreEqual(0, fixture.RunsOf("plant-loose"), "A row no root holds is not live.");
        }

        [Test]
        public void ChangedReadRunsOnlyTheInstanceThatReadIt()
        {
            using Fixture fixture = Build(plants: 3);
            fixture.client.StartEffects();

            fixture.WriteNumber("plant-1-count", "member-count", 50);

            Assert.AreEqual(1, fixture.RunsOf("plant-0"));
            Assert.AreEqual(2, fixture.RunsOf("plant-1"));
            Assert.AreEqual(1, fixture.RunsOf("plant-2"));
            Assert.AreEqual(("plant-1", 50), fixture.recorded[^1]);
        }

        [Test]
        public void EffectWritesRunTheEffectsThatReadThemInTheSameDrain()
        {
            using Fixture fixture = Build(plants: 2);
            fixture.client.StartEffects();
            fixture.recorded.Clear();

            // The watcher copies Signal into Target, which every plant reads.
            fixture.WriteNumber("value-signal", "member-signal", 7);

            Assert.AreEqual(7, fixture.ReadNumber("value-target"));
            CollectionAssert.AreEquivalent(new[] { ("plant-0", 7), ("plant-1", 8) }, fixture.recorded);
        }

        [Test]
        public void GetterWatchersHearTheSettledState()
        {
            using Fixture fixture = Build(plants: 0);
            fixture.client.StartEffects();
            var watcher = new NeoMemberClassWritable(fixture.client, "member-watcher", "value-watcher", NeoValueOwnership.Save);
            // Seen => root.Save.Signal + root.Save.Target, so a Signal write
            // drops it before the effect writes Target.
            Assert.AreEqual(0, Convert.ToInt32(watcher.Get<NeoMemberNSProperty>("Seen").Compute("value-watcher").value));
            var heard = new List<double>();
            using IDisposable watch = fixture.client.WatchGetters("value-watcher", watcher);
            watcher.OnChanged += _ => heard.Add(fixture.ReadNumber("value-target"));

            fixture.WriteNumber("value-signal", "member-signal", 3);

            CollectionAssert.AreEqual(new[] { 3d }, heard, "Effects run before the watchers hear the getter, once.");
        }

        [Test]
        public void NativeReadsAreNotDependencies()
        {
            using Fixture fixture = Build(plants: 1);
            fixture.client.StartEffects();

            fixture.WriteNumber("value-native-only", "member-native-only", 9);

            Assert.AreEqual(1, fixture.RunsOf("plant-0"), "Record's NeoScript read NativeOnly, but the effect did not.");
        }

        [Test]
        public void ValueCallbacksARunReachAreNotDependencies()
        {
            using Fixture fixture = Build(plants: 1);
            var plant = new NeoMemberClassWritable(fixture.client, "member-plant-entry", "plant-0", NeoValueOwnership.Save);
            NeoMemberNSProperty seen = NativeSeen(fixture.client);
            int callbacks = 0;
            plant.OnChanged += _ =>
            {
                callbacks++;
                seen.Compute("value-watcher");
            };
            fixture.client.StartEffects();
            fixture.WriteNumber("plant-0-count", "member-count", 3);
            Assert.Greater(callbacks, 0, "The run's Runs write reached the plant's callback.");

            fixture.WriteNumber("value-native-only", "member-native-only", 9);

            Assert.AreEqual(2, fixture.RunsOf("plant-0"), "The callback read NativeOnly, but the effect did not.");
        }

        [Test]
        public void ACommitRunsEffectsBeforeItsValueCallbacks()
        {
            using Fixture fixture = Build(plants: 1);
            fixture.client.StartEffects();
            var plant = new NeoMemberClassWritable(fixture.client, "member-plant-entry", "plant-0", NeoValueOwnership.Save);
            var seen = new List<(string plant, int value)>();
            plant.OnChanged += _ => seen.Add(fixture.recorded[^1]);

            var plan = new NeoWritePlan(fixture.client);
            plan.Set(NeoValueOwnership.Save, Number("plant-0-count", 50));
            plan.Commit();

            CollectionAssert.IsNotEmpty(seen);
            Assert.AreEqual(("plant-0", 50), seen[0], "The commit's callbacks hear the state its effects settled.");
        }

        [Test]
        public void EffectsThatTriggerEachOtherStopAfterTheCap()
        {
            // Plant: root.Save.Signal = root.Save.Target + 1; Watcher: root.Save.Target = root.Save.Signal.
            using Fixture fixture = Build(plants: 1, plantCheck: Assign(
                RootSave("Signal"),
                Add(RootSave("Target"), Literal(1))));
            LogAssert.Expect(LogType.Exception, new Regex("NeoEffectCycleException"));
            fixture.client.StartEffects();

            LogAssert.Expect(LogType.Exception, new Regex("NeoEffectCycleException"));
            fixture.WriteNumber("value-target", "member-target", 1_000);

            double target = fixture.ReadNumber("value-target");
            Assert.Greater(target, 1_000, "The write that started the drain committed.");
            Assert.LessOrEqual(target, 1_000 + NeoClient.EffectRunsPerDrain + 1);
        }

        [Test]
        public void FailedRunLogsAndKeepsItsReads()
        {
            using Fixture fixture = Build(plants: 2);
            fixture.throwFor = "plant-0";
            LogAssert.Expect(LogType.Exception, new Regex("Record failed for plant-0"));
            fixture.client.StartEffects();

            Assert.AreEqual(1, fixture.RunsOf("plant-0"), "Writes before the throw commit.");
            Assert.AreEqual(1, fixture.RunsOf("plant-1"));

            fixture.throwFor = null;
            fixture.WriteNumber("plant-0-count", "member-count", 4);

            Assert.AreEqual(2, fixture.RunsOf("plant-0"), "The reads before the throw stay indexed.");
            Assert.AreEqual(1, fixture.RunsOf("plant-1"));
        }

        [Test]
        public void RemovalStopsAnInstanceAndAttachingOneStartsIt()
        {
            using Fixture fixture = Build(plants: 2);
            fixture.client.StartEffects();

            var removal = new NeoWritePlan(fixture.client);
            removal.Set(NeoValueOwnership.Save, PlantList("plant-derived", "plant-0"));
            removal.Remove(NeoValueOwnership.Save, "plant-1");
            removal.Remove(NeoValueOwnership.Save, "plant-1-count");
            removal.Remove(NeoValueOwnership.Save, "plant-1-runs");
            removal.Commit();
            fixture.WriteNumber("value-target", "member-target", 5);
            Assert.AreEqual(2, fixture.RecordedCount("plant-0"));
            Assert.AreEqual(1, fixture.RecordedCount("plant-1"), "A removed instance stops.");

            fixture.client.SetWritableValues(NeoValueOwnership.Save, new MemberValue[]
            {
                Number("plant-new-count", 2),
                Number("plant-new-runs", 0),
                Plant("plant-new", "class-plant"),
                PlantList("plant-derived", "plant-0", "plant-new"),
            });
            Assert.AreEqual(1, fixture.RunsOf("plant-new"), "An attached instance runs once.");
            Assert.AreEqual(("plant-new", 7), fixture.recorded[^1]);
        }

        [Test]
        public void ParentWriteAttachesAnOrphanedInstance()
        {
            using Fixture fixture = Build(plants: 0);
            fixture.client.StartEffects();
            fixture.client.SetWritableValues(NeoValueOwnership.Save, new MemberValue[]
            {
                ObjectValue("watcher-2", "class-watcher"),
            });
            fixture.WriteNumber("value-signal", "member-signal", 4);
            Assert.AreEqual(4, fixture.ReadNumber("value-target"));

            var attach = new NeoWritePlan(fixture.client);
            attach.Remove(NeoValueOwnership.Save, "value-watcher");
            attach.Set(NeoValueOwnership.Save, SaveRoot("watcher-2"));
            attach.Commit();
            fixture.WriteNumber("value-target", "member-target", 0);
            fixture.WriteNumber("value-signal", "member-signal", 6);

            Assert.AreEqual(6, fixture.ReadNumber("value-target"), "The attached watcher runs.");
        }

        [Test]
        public void OverrideKindsApplyToTheSubclassAndBelow()
        {
            using Fixture fixture = Build(plants: 1);
            fixture.client.StartEffects();

            Assert.AreEqual(1, fixture.RunsOf("plant-0"), "A base instance runs Check but not the plain base Plain.");
            Assert.AreEqual(10, fixture.RunsOf("plant-derived"), "The subclass turns Check off and Plain on.");
        }

        [Test]
        public void ANativeOverrideResolvesItsEffectAlongTheChain()
        {
            // class QuietPlant : Plant { @effect(kind: .None) native override void Check(); }
            // class QuietLeaf : QuietPlant { native override void Check(); }
            using Fixture fixture = Build(plants: 0, configure: data =>
            {
                data.members["member-check-quiet"] = NativeOverride("member-check-quiet", "member-check", NeoEffectKind.None);
                data.members["member-check-leaf"] = NativeOverride("member-check-leaf", "member-check-quiet", null);
                data.members["member-check-loud"] = NativeOverride("member-check-loud", "member-check", null);
            });

            Assert.AreEqual(NeoEffectKind.None, NativeEffect(fixture.client, "member-check-quiet"));
            Assert.AreEqual(NeoEffectKind.None, NativeEffect(fixture.client, "member-check-leaf"), "Its None holds below it.");
            Assert.AreEqual(NeoEffectKind.Auto, NativeEffect(fixture.client, "member-check-loud"), "Absence inherits.");
        }

        [Test]
        public void AVariantSwapStopsTheOldInstanceAndStartsItsReplacement()
        {
            using Fixture fixture = Build(plants: 1);
            fixture.client.StartEffects();

            var swap = new NeoWritePlan(fixture.client);
            swap.Set(NeoValueOwnership.Save, Plant("plant-0", "class-derived-plant"));
            swap.Commit();
            Assert.AreEqual(11, fixture.RunsOf("plant-0"), "The replacement runs its own Plain once.");

            fixture.WriteNumber("plant-0-count", "member-count", 6);
            Assert.AreEqual(11, fixture.RunsOf("plant-0"), "The replacement turned Check off.");
            Assert.AreEqual(1, fixture.RecordedCount("plant-0"));
        }

        [Test]
        public void AWritableStaticHoldsItsDefaultInstance()
        {
            using Fixture fixture = Build(plants: 0, configure: data =>
            {
                data.members["member-static-plant"] = new ClassMember
                {
                    id = "member-static-plant",
                    projectId = ProjectId,
                    name = "StaticPlant",
                    kind = MemberKind.Class,
                    classId = "class-plant",
                    valueId = "static-plant",
                    Storage = NeoMemberStorage.Save,
                    Modifier = NeoMemberModifierKind.Static,
                    Requirement = NeoMemberRequirementKind.Required,
                };
                data.classes["class-save-root"].schema["StaticPlant"] = "member-static-plant";
                data.values["static-plant-count"] = Number("static-plant-count", 4);
                data.values["static-plant-runs"] = Number("static-plant-runs", 0);
                data.values["static-plant"] = Plant("static-plant", "class-plant", containerId: null);
            });
            fixture.client.StartEffects();
            Assert.AreEqual(1, fixture.RecordedCount("static-plant"));

            fixture.WriteNumber("static-plant-count", "member-count", 5);

            Assert.AreEqual(2, fixture.RecordedCount("static-plant"));
            Assert.AreEqual(("static-plant", 5), fixture.recorded[^1]);
        }

        [Test]
        public void LoadingAPartitionStartsTheInstancesItHoldsAndUnloadingStopsThem()
        {
            using Fixture fixture = Build(plants: 1, configure: data =>
            {
                data.valuePartitions = new Dictionary<string, JToken>
                {
                    ["garden"] = JObject.FromObject(new Dictionary<string, MemberValue>
                    {
                        ["value-bed"] = new ArrayMemberValue { id = "value-bed", value = Array.Empty<string>() },
                    }),
                };
            });
            fixture.client.StartEffects();
            fixture.client.SetWritableValues(NeoValueOwnership.Save, new MemberValue[]
            {
                Number("bed-plant-count", 3),
                Number("bed-plant-runs", 0),
                Plant("bed-plant", "class-plant", containerId: "value-bed"),
            });
            Assert.AreEqual(0, fixture.RecordedCount("bed-plant"), "Its bed is not loaded.");

            fixture.client.LoadValuePartition("garden");
            Assert.AreEqual(1, fixture.RecordedCount("bed-plant"));

            fixture.client.UnloadValuePartition("garden");
            fixture.WriteNumber("value-target", "member-target", 2);
            Assert.AreEqual(1, fixture.RecordedCount("bed-plant"), "Unloading its bed stopped it.");
            Assert.AreEqual(2, fixture.RecordedCount("plant-0"));
        }

        [Test]
        public void AnEffectAGetterReadQueuedRunsWhenTheReadReturns()
        {
            using Fixture fixture = Build(plants: 1);
            fixture.client.StartEffects();
            var watcher = new NeoMemberClassWritable(fixture.client, "member-watcher", "value-watcher", NeoValueOwnership.Save);

            // Poked calls the native Poke, which writes plant-0's Count while
            // the getter's read capture blocks the drain.
            watcher.Get<NeoMemberNSProperty>("Poked").Compute("value-watcher");

            Assert.AreEqual(2, fixture.RunsOf("plant-0"));
            Assert.AreEqual(("plant-0", 40), fixture.recorded[^1]);
        }

        [Test]
        public void AnEffectAValueReadCaptureQueuedRunsWhenTheCaptureEnds()
        {
            using Fixture fixture = Build(plants: 1);
            fixture.client.StartEffects();
            var watcher = new NeoMemberClassWritable(fixture.client, "member-watcher", "value-watcher", NeoValueOwnership.Save);

            // Animation segments resolve inside a value-read capture, which
            // outlasts the getter's own drain.
            using (fixture.client.CaptureValueReads(new HashSet<string>()))
            {
                watcher.Get<NeoMemberNSProperty>("Poked").Compute("value-watcher");
                Assert.AreEqual(1, fixture.RunsOf("plant-0"));
            }

            Assert.AreEqual(2, fixture.RunsOf("plant-0"));
            Assert.AreEqual(("plant-0", 40), fixture.recorded[^1]);
        }

        [Test]
        [Explicit("Effect run measurement; run serially by name.")]
        public void EffectPerformance_RunAgainstDirectCall()
        {
            const int plants = 100;
            const int samples = 5;
            using Fixture effects = Build(plants);
            using Fixture direct = Build(plants);
            using Fixture writes = Build(plants);
            effects.client.StartEffects();
            var function = new NeoMemberNSFunction(direct.client, "member-check", null, NeoValueOwnership.Save);
            Assert.IsTrue(effects.client.TryGetMember("member-count", out JsonMember? count));
            var rows = new NumberMemberValue[2 * plants];
            for (int i = 0; i < rows.Length; i++)
                rows[i] = Number($"plant-{i % plants}-count", 100 + i);

            void WriteEach(Fixture fixture, bool call)
            {
                for (int i = 0; i < rows.Length; i++)
                {
                    Assert.IsTrue(fixture.client.TryWriteLeaf(NeoValueOwnership.Save, rows[i], count!, "value"));
                    if (call)
                        function.Invoke($"plant-{i % plants}", Array.Empty<object?>());
                }
            }

            WriteEach(effects, call: false);
            WriteEach(direct, call: true);
            WriteEach(writes, call: false);
            var effect = PerformanceSampling.Repeat(samples, () => WriteEach(effects, call: false));
            var called = PerformanceSampling.Repeat(samples, () => WriteEach(direct, call: true));
            var written = PerformanceSampling.Repeat(samples, () => WriteEach(writes, call: false));
            Assert.AreEqual(1 + 2 * (1 + samples), effects.RunsOf("plant-0"), "Each Count write runs its plant once.");
            PerformanceSampling.Report("Effect", "write-then-run", $"plants={plants}", effect, rows.Length);
            PerformanceSampling.Report("Effect", "write-then-call", $"plants={plants}", called, rows.Length);
            PerformanceSampling.Report("Effect", "write-only", $"plants={plants}", written, rows.Length);
            // The gate compares the run with the call, net of the write both share (P97 §7).
            double run = Median(effect, sample => sample.Ms) - Median(written, sample => sample.Ms);
            double call = Median(called, sample => sample.Ms) - Median(written, sample => sample.Ms);
            Debug.Log($"[Effect] run/call = {run / call:F2}x; run {Median(effect, sample => sample.GcBytes) - Median(written, sample => sample.GcBytes)} B, call {Median(called, sample => sample.GcBytes) - Median(written, sample => sample.GcBytes)} B per {rows.Length} writes");
            Assert.LessOrEqual(run, 1.5 * call);
        }

        private static double Median(List<PerformanceSampling.Sample> samples, Func<PerformanceSampling.Sample, double> value)
        {
            var values = samples.ConvertAll(sample => value(sample));
            values.Sort();
            return values[values.Count / 2];
        }

        [Test]
        [Explicit("Effect fan-out measurement; run serially by name.")]
        public void EffectPerformance_FanOut()
        {
            const int samples = 5;
            const int writes = 20;
            foreach (int plants in new[] { 100, 1_000 })
            {
                using Fixture fixture = Build(plants);
                fixture.client.StartEffects();
                var counts = new[] { Number("plant-0-count", 1_000), Number("plant-0-count", 1_001) };
                var targets = new[] { Number("value-target", 1), Number("value-target", 2) };
                Assert.IsTrue(fixture.client.TryGetMember("member-count", out JsonMember? count));
                Assert.IsTrue(fixture.client.TryGetMember("member-target", out JsonMember? target));
                void WriteOneCount()
                {
                    for (int write = 0; write < writes; write++)
                        Assert.IsTrue(fixture.client.TryWriteLeaf(NeoValueOwnership.Save, counts[write & 1], count!, "value"));
                }
                void WriteSharedTarget()
                {
                    for (int write = 0; write < writes; write++)
                        Assert.IsTrue(fixture.client.TryWriteLeaf(NeoValueOwnership.Save, targets[write & 1], target!, "value"));
                }

                int rounds = (1 + samples) * writes;
                WriteOneCount();
                var one = PerformanceSampling.Repeat(samples, WriteOneCount);
                Assert.AreEqual(1 + rounds, fixture.RunsOf("plant-0"));
                for (int i = 1; i < plants; i++)
                    Assert.AreEqual(1, fixture.RunsOf($"plant-{i}"), $"Plant {i} read nothing that changed.");

                WriteSharedTarget();
                var shared = PerformanceSampling.Repeat(samples, WriteSharedTarget);
                Assert.AreEqual(1 + 2 * rounds, fixture.RunsOf("plant-0"));
                for (int i = 1; i < plants; i++)
                    Assert.AreEqual(1 + rounds, fixture.RunsOf($"plant-{i}"), $"Plant {i} runs once per Target write.");

                PerformanceSampling.Report("EffectFanOut", "one-count", $"plants={plants}", one, writes);
                PerformanceSampling.Report("EffectFanOut", "shared-target", $"plants={plants}", shared, writes);
            }
        }

        // ------------------------------------------------------------------
        // Fixture.
        // ------------------------------------------------------------------

        private static Fixture Build(int plants, Instruction? plantCheck = null, Action<ProjectData>? configure = null)
        {
            var members = new List<JsonMember>
            {
                RootMember("member-root-assets", "value-assets", "class-root", NeoMemberStorage.Inherit),
                RootMember("member-root-save", "value-save", "class-save-root", NeoMemberStorage.Save),
                RootMember("member-root-session", "value-session", "class-root", NeoMemberStorage.Session),
                new ListMember
                {
                    id = "member-plants", projectId = ProjectId, name = "Plants", kind = MemberKind.List,
                    entryMemberId = "member-plant-entry", ListKind = NeoListKind.Unordered,
                    Requirement = NeoMemberRequirementKind.Required,
                },
                new ClassMember
                {
                    id = "member-plant-entry", projectId = ProjectId, name = "Plant", kind = MemberKind.Class,
                    classId = "class-plant", Requirement = NeoMemberRequirementKind.Required,
                },
                new ClassMember
                {
                    id = "member-watcher", projectId = ProjectId, name = "Watcher", kind = MemberKind.Class,
                    classId = "class-watcher", Requirement = NeoMemberRequirementKind.Required,
                },
                IntMember("member-target", "Target"),
                IntMember("member-signal", "Signal"),
                IntMember("member-native-only", "NativeOnly"),
                IntMember("member-count", "Count"),
                IntMember("member-runs", "Runs"),
                new FunctionMember
                {
                    id = "member-record", projectId = ProjectId, name = "Record", kind = MemberKind.Function,
                    returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                    argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "value", type = MemberKind.Int, required = true } },
                    Dispatch = NeoFunctionDispatchKind.Synchronous,
                },
                new FunctionMember
                {
                    id = "member-poke", projectId = ProjectId, name = "Poke", kind = MemberKind.Function,
                    returnTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                    argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                    Dispatch = NeoFunctionDispatchKind.Synchronous,
                },
                ScriptFunction("member-check", "Check", NeoEffectKind.Auto, null,
                    plantCheck ?? Assign(KeyOf(This(), "Runs"), Add(KeyOf(This(), "Runs"), Literal(1))),
                    plantCheck is null ? RecordCall(Add(KeyOf(This(), "Count"), RootSave("Target"))) : null),
                ScriptFunction("member-plain", "Plain", null, null,
                    Assign(KeyOf(This(), "Runs"), Add(KeyOf(This(), "Runs"), Literal(10)))),
                ScriptFunction("member-check-off", "Check", NeoEffectKind.None, "member-check",
                    Assign(KeyOf(This(), "Runs"), Add(KeyOf(This(), "Runs"), Literal(1)))),
                ScriptFunction("member-plain-on", "Plain", NeoEffectKind.Auto, "member-plain",
                    Assign(KeyOf(This(), "Runs"), Add(KeyOf(This(), "Runs"), Literal(10)))),
                ScriptFunction("member-watch", "Watch", NeoEffectKind.Auto, null,
                    Assign(RootSave("Target"), RootSave("Signal"))),
                Getter("member-seen", "Seen", Add(RootSave("Signal"), RootSave("Target"))),
                Getter("member-native-seen", "NativeSeen", RootSave("NativeOnly")),
                Getter("member-poked", "Poked", new CallFunctionPointer
                {
                    type = PointerKind.CallFunction,
                    memberId = "member-poke",
                    receiver = CallReceiver.Instance(This()),
                    args = Array.Empty<Pointer>(),
                    callSiteId = "poke",
                }),
            };
            var values = new List<MemberValue>
            {
                ObjectValue("value-assets", "class-root"),
                ObjectValue("value-session", "class-root"),
                SaveRoot("value-watcher"),
                ObjectValue("value-watcher", "class-watcher"),
                Number("value-target", 0),
                Number("value-signal", 0),
                Number("value-native-only", 0),
                PlantList(),
            };
            var data = new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "Effects",
                    rootAssetsMemberId = "member-root-assets",
                    rootSaveFileMemberId = "member-root-save",
                    rootSessionMemberId = "member-root-session",
                    createdAt = "x",
                    updatedAt = "x",
                },
                members = new Dictionary<string, JsonMember>(),
                values = new Dictionary<string, MemberValue>(),
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["class-root"] = Class("class-root", null),
                    ["class-save-root"] = Class("class-save-root", null,
                        ("Plants", "member-plants"), ("Watcher", "member-watcher"), ("Target", "member-target"),
                        ("Signal", "member-signal"), ("NativeOnly", "member-native-only")),
                    ["class-plant"] = Class("class-plant", null,
                        ("Count", "member-count"), ("Runs", "member-runs"), ("Check", "member-check"), ("Plain", "member-plain")),
                    ["class-derived-plant"] = Class("class-derived-plant", "class-plant",
                        ("Check", "member-check-off"), ("Plain", "member-plain-on")),
                    ["class-watcher"] = Class("class-watcher", null, ("Watch", "member-watch"), ("Seen", "member-seen"),
                        ("NativeSeen", "member-native-seen"), ("Poked", "member-poked")),
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
            foreach (JsonMember member in members)
                data.members[member.id] = member;
            foreach (MemberValue value in values)
                data.values[value.id] = value;
            configure?.Invoke(data);

            var fixture = new Fixture();
            fixture.client = NeoTestSaveStack.ClientFromSchema(data);
            fixture.client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["member-record"] = (client, receiver, args) =>
                {
                    string plant = NeoGeneratedTypesSupport.ValueId(receiver)!;
                    // NeoScript a native function runs is never an effect's dependency.
                    (fixture.nativeSeen ??= NativeSeen(client)).Compute("value-watcher");
                    if (plant == fixture.throwFor)
                        throw new InvalidOperationException($"Record failed for {plant}.");
                    fixture.recorded.Add((plant, Convert.ToInt32(args[0])));
                    return null;
                },
                ["member-poke"] = (client, _, _) =>
                {
                    Assert.IsTrue(client.TryGetMember("member-count", out JsonMember? count));
                    Assert.IsTrue(client.TryWriteLeaf(NeoValueOwnership.Save, Number("plant-0-count", 40), count!, "value"));
                    return 1;
                },
            });
            // Plants are save rows the game created, as a NeoScript list add writes them.
            var plantRows = new List<MemberValue>
            {
                Number("plant-derived-count", 0),
                Number("plant-derived-runs", 0),
                Plant("plant-derived", "class-derived-plant"),
                Number("plant-loose-count", 0),
                Number("plant-loose-runs", 0),
                Plant("plant-loose", "class-plant", containerId: null),
            };
            var plantIds = new string[plants + 1];
            plantIds[0] = "plant-derived";
            for (int i = 0; i < plants; i++)
            {
                plantRows.Add(Number($"plant-{i}-count", i));
                plantRows.Add(Number($"plant-{i}-runs", 0));
                plantRows.Add(Plant($"plant-{i}", "class-plant"));
                plantIds[i + 1] = $"plant-{i}";
            }
            plantRows.Add(PlantList(plantIds));
            fixture.client.SetWritableValues(NeoValueOwnership.Save, plantRows.ToArray());
            return fixture;
        }

        private static FunctionMember NativeOverride(string id, string extendsMemberId, NeoEffectKind? effect)
        {
            var function = new FunctionMember
            {
                id = id,
                projectId = ProjectId,
                name = "Check",
                kind = MemberKind.Function,
                extendsMemberId = extendsMemberId,
            };
            if (effect is NeoEffectKind kind)
                function.Effect = kind;
            return function;
        }

        private static NeoEffectKind NativeEffect(NeoClient client, string memberId)
        {
            Assert.IsTrue(client.TryGetMember(memberId, out JsonMember? member));
            return ((FunctionMember)member!).Effect;
        }

        private static NeoMemberNSProperty NativeSeen(NeoClient client) =>
            new NeoMemberClassWritable(client, "member-watcher", "value-watcher", NeoValueOwnership.Save)
                .Get<NeoMemberNSProperty>("NativeSeen");

        private static ObjectMemberValue SaveRoot(string watcherId) => ObjectValue("value-save", "class-save-root",
            ("Plants", PlantsListId), ("Watcher", watcherId), ("Target", "value-target"),
            ("Signal", "value-signal"), ("NativeOnly", "value-native-only"));

        private static ArrayMemberValue PlantList(params string[] plantIds) =>
            new()
            {
                id = PlantsListId,
                value = plantIds
            };

        private static ObjectMemberValue Plant(string id, string classId, string? containerId = PlantsListId)
        {
            ObjectMemberValue plant = ObjectValue(id, classId, ("Count", $"{id}-count"), ("Runs", $"{id}-runs"));
            plant.containerId = containerId;
            return plant;
        }

        private static ClassMember RootMember(string id, string valueId, string classId, NeoMemberStorage storage) => new()
        {
            id = id,
            projectId = ProjectId,
            name = id,
            kind = MemberKind.Class,
            classId = classId,
            valueId = valueId,
            Storage = storage,
            Requirement = NeoMemberRequirementKind.Required,
        };

        private static IntMember IntMember(string id, string name) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Int,
        };

        private static NSFunctionMember ScriptFunction(
            string id,
            string name,
            NeoEffectKind? effect,
            string? extendsMemberId,
            params Instruction?[] instructions)
        {
            var function = new NSFunctionMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.NSFunction,
                code = "compiled test function",
                extendsMemberId = extendsMemberId,
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[] { Parameter("__this__"), Parameter("__root__") },
                    instructions = Array.FindAll(instructions, instruction => instruction is not null)!,
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                },
            };
            if (extendsMemberId is null)
            {
                function.returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true };
                function.argumentTypes = Array.Empty<FunctionArgumentTypeInfo>();
                function.Dispatch = NeoFunctionDispatchKind.Synchronous;
            }
            if (effect is NeoEffectKind kind)
                function.Effect = kind;
            return function;
        }

        private static NSPropertyMember Getter(string id, string name, Pointer result) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.NSProperty,
            code = "compiled test getter",
            returnTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
            getter = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                instructions = new Instruction[]
                {
                    new ReturnInstruction { type = InstructionKind.Return, pointer = result },
                },
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
            },
        };

        private static Variable Parameter(string id) => new()
        {
            id = id,
            typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
            pointer = new VariablePointer { type = PointerKind.Variable, variableId = id },
        };

        private static NeoSchemaClass Class(string id, string? extendsClassId, params (string key, string memberId)[] schema)
        {
            var entries = new Dictionary<string, string>();
            foreach (var (key, memberId) in schema)
                entries[key] = memberId;
            return new NeoSchemaClass
            {
                id = id,
                projectId = ProjectId,
                name = id,
                schema = entries,
                extendsClassId = extendsClassId,
            };
        }

        private static ObjectMemberValue ObjectValue(string id, string classId, params (string key, string valueId)[] entries)
        {
            var value = new Dictionary<string, string>();
            foreach (var (key, valueId) in entries)
                value[key] = valueId;
            return new ObjectMemberValue { id = id, classId = classId, value = value };
        }

        private static NumberMemberValue Number(string id, double value) => new() { id = id, value = value };

        private static AssignInstruction Assign(KeyOfPointer target, Pointer value) => new()
        {
            type = InstructionKind.Assign,
            operatorValue = "=",
            target = new WriteTarget
            {
                pointer = target,
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                writability = WritabilityKind.Save,
            },
            pointer = value,
        };

        private static FunctionCallInstruction RecordCall(Pointer value) => new()
        {
            type = InstructionKind.FunctionCall,
            call = new CallFunctionPointer
            {
                type = PointerKind.CallFunction,
                memberId = "member-record",
                receiver = CallReceiver.Instance(This()),
                args = new[] { value },
                callSiteId = "record",
            },
        };

        private static KeyOfPointer RootSave(string key) =>
            KeyOf(KeyOf(new VariablePointer { type = PointerKind.Variable, variableId = "__root__" }, "Save"), key);

        private static VariablePointer This() => new() { type = PointerKind.Variable, variableId = "__this__" };

        private static KeyOfPointer KeyOf(Pointer receiver, string key) => new()
        {
            type = PointerKind.KeyOf,
            keyOf = new KeyOf
            {
                pointer = receiver,
                key = new ValuePointer
                {
                    type = PointerKind.Value,
                    value = new Value
                    {
                        typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                        value = JToken.FromObject(key),
                    },
                },
            },
        };

        private static ValuePointer Literal(int value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                value = JToken.FromObject(value),
            },
        };

        private static OperationPointer Add(Pointer left, Pointer right) => new()
        {
            type = PointerKind.Operation,
            operation = new ArithmeticOperation
            {
                type = OperationKind.Arithmetic,
                arithmetic = new ArithmeticOpInfo
                {
                    type = ArithmeticOpKind.Addition,
                    pointers = new[] { left, right },
                },
            },
        };
    }
}
