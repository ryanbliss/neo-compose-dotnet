// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using Parity = NeoCompose.Tests.ReferenceParity;

namespace NeoCompose.Tests
{
    public partial class NeoReferenceTests
    {
        [Test]
        public void ReferenceSubscriptionSurvivesAdoptionAndRebinding()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            var g = Parity.Probe.G;
            using var board = new Parity.Pinboard(g.ReferenceTo(Parity.Gauge.Fields.Count));
            var heard = new List<int?>();
            var subscription = board.OnChanged(Parity.Pinboard.Fields.Note, (value, _) => heard.Add(value?.Value));
            Parity.Probe.Board = board;
            heard.Clear();
            g.Count = 7;
            board.Note = g.ReferenceTo(Parity.Gauge.Fields.Other);
            g.Count = 8;
            g.Other = 12;
            CollectionAssert.AreEqual(new int?[] { 7, 10, 12 }, heard);
            subscription.Dispose();
            g.Other = 13;
            Assert.That(heard.Count, Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedReferenceFieldHearsEnumTargetWrites(bool virtualReference)
        {
            ProjectData data = Data();
            if (virtualReference)
            {
                var row = (ObjectMemberValue)GRoot(data);
                data.values.Remove(row.value!["FeelRef"]);
                row.value.Remove("FeelRef");
            }
            using var app = new Parity.TestProjectNeo(Client(data));
            Parity.Gauge g = Parity.Probe.G;
            var heard = new List<Parity.Mood>();
            using var subscription = g.OnChanged(Parity.Gauge.Fields.FeelRef, (value, source) => heard.Add(value.Value));
            g.FeelRef.Value = Parity.Mood.Happy;
            g.FeelRef.Value = Parity.Mood.Calm;
            CollectionAssert.AreEqual(new[] { Parity.Mood.Happy, Parity.Mood.Calm }, heard);
        }

        [Test]
        public void GeneratedReferenceFieldMovesItsTargetAndCanBecomeNull()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            Parity.Gauge g = Parity.Probe.G;
            using var board = new Parity.Pinboard();
            var heard = new List<int?>();
            using var subscription = board.OnChanged(Parity.Pinboard.Fields.Note, (value, source) => heard.Add(value?.Value));
            board.Note = g.ReferenceTo(Parity.Gauge.Fields.Count);
            g.Count = 7;
            board.Note = g.ReferenceTo(Parity.Gauge.Fields.Other);
            g.Count = 8;
            g.Other = 12;
            board.Note = null;
            g.Other = 13;
            CollectionAssert.AreEqual(new int?[] { 3, 7, 10, 12, null }, heard);
        }

        [Test]
        public void GeneratedReferenceTargetDoesNotBubbleToOwnerOrCollections()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            Parity.Gauge g = Parity.Probe.G;
            using var external = new Parity.Tally();
            using var board = new Parity.Pinboard(external.ReferenceTo(Parity.Tally.Fields.N));
            g.RefList.Add(board.Note!);
            g.RefMap["external"] = board.Note!;
            int field = 0, owner = 0, list = 0, map = 0;
            using var fieldSub = board.OnChanged(Parity.Pinboard.Fields.Note, (_, _) => field++);
            using var ownerSub = board.OnChanged(_ => owner++);
            using var listSub = g.RefList.OnChanged((_, _) => list++);
            using var mapSub = g.RefMap.OnChanged((_, _) => map++);
            external.N = 9;
            Assert.AreEqual(1, field);
            Assert.AreEqual(0, owner);
            Assert.AreEqual(0, list);
            Assert.AreEqual(0, map);
        }

        [Test]
        public void GeneratedHandleReferenceDoesNotHearTargetContents()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            Parity.Gauge g = Parity.Probe.G;
            using var external = new Parity.Tally();
            g.TRef = external;
            int heard = 0;
            using var subscription = g.OnChanged(Parity.Gauge.Fields.TRef, (_, _) => heard++);
            external.N = 9;
            Assert.AreEqual(0, heard);
            g.TRef = null;
            Assert.AreEqual(1, heard);
        }

        [Test]
        public void GeneratedReferenceFieldCoalescesTargetWritesAndRebindingInTransactions()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            Parity.Gauge g = Parity.Probe.G;
            var heard = new List<int>();
            using var subscription = g.OnChanged(Parity.Gauge.Fields.A, (value, _) => heard.Add(value.Value));
            app.Client.RunTransaction(() =>
            {
                g.Count = 7;
                g.A = g.ReferenceTo(Parity.Gauge.Fields.Other);
                app.Client.RunTransaction(() => g.Other = 11);
                g.Other = 12;
                Assert.IsEmpty(heard);
            });
            CollectionAssert.AreEqual(new[] { 12 }, heard);
            g.Count = 8;
            CollectionAssert.AreEqual(new[] { 12 }, heard);
            g.Other = 13;
            CollectionAssert.AreEqual(new[] { 12, 13 }, heard);
        }

        [Test]
        public void GeneratedReferenceListenersDisposeIndependentlyAndDuringDelivery()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            Parity.Gauge g = Parity.Probe.G;
            int first = 0, second = 0;
            IDisposable? secondSub = null;
            using var firstSub = g.OnChanged(Parity.Gauge.Fields.A, (_, _) =>
            {
                first++;
                if (first == 2)
                    secondSub!.Dispose();
            });
            secondSub = g.OnChanged(Parity.Gauge.Fields.A, (_, _) => second++);
            g.Count = 6;
            Assert.AreEqual(1, first);
            Assert.AreEqual(1, second);
            app.Client.RunTransaction(() => g.Count = 7);
            Assert.AreEqual(2, first);
            Assert.AreEqual(1, second);
            firstSub.Dispose();
            g.Count = 8;
            Assert.AreEqual(2, first);
            Assert.AreEqual(1, second);
            using var third = g.OnChanged(Parity.Gauge.Fields.A, (_, _) => second++);
            g.Count = 9;
            Assert.AreEqual(2, second);
            NeoReference<int> count = g.A;
            g.Dispose();
            count.Value = 10;
            Assert.AreEqual(2, second);
        }

        [Test]
        public void GeneratedReferenceFieldSupportsReentrantWritesAndFailedTransactions()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            Parity.Gauge g = Parity.Probe.G;
            var heard = new List<int>();
            using var subscription = g.OnChanged(Parity.Gauge.Fields.A, (value, _) =>
            {
                heard.Add(value.Value);
                if (value.Value == 7)
                    g.Count = 8;
            });
            app.Client.RunTransaction(() => g.Count = 7);
            CollectionAssert.AreEqual(new[] { 7, 8 }, heard);
            Assert.Throws<InvalidOperationException>(() => app.Client.RunTransaction(() =>
            {
                g.Count = 9;
                throw new InvalidOperationException("transaction body failed");
            }));
            g.Count = 10;
            CollectionAssert.AreEqual(new[] { 7, 8, 9, 10 }, heard);
        }

        [Test]
        public void GeneratedReferenceGetterHearsEachTargetWriteOnce()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            using var tally = new Parity.Tally();
            var heard = new List<int>();
            using var subscription = tally.OnChanged(Parity.Tally.Fields.NRef, (value, _) => heard.Add(value.Value));
            tally.N = 8;
            app.Client.RunTransaction(() =>
            {
                tally.N = 9;
                tally.N = 10;
            });
            CollectionAssert.AreEqual(new[] { 8, 10 }, heard);
        }

        [Test]
        public void GeneratedReferenceFieldFollowsSavedTargetsAfterReloadAndExternalApply()
        {
            var data = Data();
            string saved;
            using (var first = new Parity.TestProjectNeo(Client(data)))
            {
                Parity.Probe.SavedPinsSaved();
                Parity.Probe.Saved = 7;
                saved = first.Client.SerializeSaveData();
            }
            using var app = new Parity.TestProjectNeo(NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false, loadedSaveContent: saved));
            Parity.Gauge g = Parity.Probe.G;
            g.A = Parity.Probe.SavedPin!;
            Parity.Probe.Saved = 9;
            string incoming = app.Client.SerializeSaveData();
            Parity.Probe.Saved = 7;
            var heard = new List<(int, NeoChangeSource)>();
            using var subscription = g.OnChanged(Parity.Gauge.Fields.A, (value, source) => heard.Add((value.Value, source)));
            app.Client.ApplyExternalSaveContent(incoming);
            app.Client.ApplyExternalSaveContent(incoming);
            Parity.Probe.Saved = 10;
            CollectionAssert.AreEqual(new[] { (9, NeoChangeSource.External), (10, NeoChangeSource.Local) }, heard);
        }

        [Test]
        public void GeneratedReferenceCallbackFailureDoesNotLeakIntoTheNextTransaction()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            Parity.Gauge g = Parity.Probe.G;
            using var failed = g.OnChanged(Parity.Gauge.Fields.A, (_, _) => throw new InvalidOperationException("callback failed"));
            Assert.Throws<InvalidOperationException>(() => app.Client.RunTransaction(() => g.Count = 7));
            failed.Dispose();
            var heard = new List<int>();
            using var subscription = g.OnChanged(Parity.Gauge.Fields.A, (value, _) => heard.Add(value.Value));
            app.Client.RunTransaction(() => g.Count = 8);
            CollectionAssert.AreEqual(new[] { 8 }, heard);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedReferenceRebindStillMovesOtherListenersWhenACallbackThrows(bool ownerCallback)
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            using var board = new Parity.Pinboard();
            using var external = new Parity.Tally();
            using var failing = ownerCallback
                ? board.OnChanged(_ => throw new InvalidOperationException("callback failed"))
                : board.OnChanged(Parity.Pinboard.Fields.Note, (_, _) => throw new InvalidOperationException("callback failed"));
            var heard = new List<int>();
            using var surviving = board.OnChanged(Parity.Pinboard.Fields.Note, (value, _) => heard.Add(value!.Value));
            Assert.Throws<InvalidOperationException>(() => board.Note = external.ReferenceTo(Parity.Tally.Fields.N));
            Assert.AreEqual(external.ReferenceTo(Parity.Tally.Fields.N).TargetId, board.Note!.TargetId, "The rebind committed before its callback threw.");
            failing.Dispose();
            external.N = 8;
            CollectionAssert.AreEqual(new[] { 8 }, heard);
        }

        [Test]
        public void GeneratedReferenceGetterCoalescesValueDependencyAndTargetChanges()
        {
            ProjectData data = ConditionalReferenceGetterData(sharedControl: false);
            using var app = new Parity.TestProjectNeo(Client(data));
            using var tally = new Parity.Tally();
            var heard = new List<int>();
            using var subscription = tally.OnChanged(Parity.Tally.Fields.NRef, (value, _) => heard.Add(value.Value));
            tally.N = 8;
            CollectionAssert.AreEqual(new[] { 8 }, heard);
            app.Client.RunTransaction(() => tally.N = 9);
            CollectionAssert.AreEqual(new[] { 8, 9 }, heard);
            tally.N = -1;
            CollectionAssert.AreEqual(new[] { 8, 9, 10 }, heard);
            tally.NRef.Value = 20;
            CollectionAssert.AreEqual(new[] { 8, 9, 10, 20 }, heard);
        }

        [Test]
        public void GeneratedGetterOnlyRebindPreparesAllReceiversBeforeCallbacks()
        {
            using var app = new Parity.TestProjectNeo(Client(ConditionalReferenceGetterData(sharedControl: true)));
            using var first = new Parity.Tally();
            using var second = new Parity.Tally();
            NeoReference<int> secondOther = second.ReferenceTo(new NeoField<int>("Other"));
            using var failing = first.OnChanged(Parity.Tally.Fields.NRef, (_, _) => throw new InvalidOperationException("callback failed"));
            var heard = new List<int>();
            using var surviving = second.OnChanged(Parity.Tally.Fields.NRef, (value, _) => heard.Add(value.Value));
            Assert.Throws<InvalidOperationException>(() => Parity.Probe.Total = -1);
            failing.Dispose();
            secondOther.Value = 20;
            CollectionAssert.AreEqual(new[] { 20 }, heard);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GetterInvalidationDoesNotDelayDialoguePresenterWrites(bool watchGetter)
        {
            ProjectData data = Data();
            data.dialogueGroups["listener-dialogue-group"] = new StandardDialogueGroup
            {
                id = "listener-dialogue-group",
                name = "Listener dialogue",
                type = DialogueGroupType.Standard,
                conditions = Array.Empty<LogicCondition>(),
            };
            data.dialogues["listener-dialogue"] = NeoDialogueTriggerTests.Dialogue(
                "listener-dialogue", "Listener dialogue", "listener-dialogue-group");
            using var app = new Parity.TestProjectNeo(Client(data));
            Parity.Gauge g = Parity.Probe.G;
            int heard = 0;
            int heardInsidePresenter = -1;
            using var other = g.OnChanged(Parity.Gauge.Fields.Other, (_, _) => heard++);
            using var getter = watchGetter ? g.OnChanged(Parity.Gauge.Fields.Doubled, (_, _) => { }) : null;
            app.Dialogues.OnTrigger += _ =>
            {
                g.Other = 99;
                heardInsidePresenter = heard;
            };
            using var trigger = g.OnChanged(Parity.Gauge.Fields.Count, (_, _) =>
            {
                // Keep the getter notification pending, but make its later read
                // a memo hit so no nested NeoScript turn affects this boundary.
                if (watchGetter)
                    _ = g.Doubled;
                Assert.IsTrue(app.Dialogues.TryTrigger("listener-dialogue"));
            });
            g.Count = 7;
            Assert.AreEqual(1, heardInsidePresenter);
            Assert.AreEqual(1, heard);
        }

        [Test]
        public void SharedReferenceTargetRebindingPreservesListenerOrder()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            using var first = new Parity.Tally();
            using var second = new Parity.Tally();
            using var board = new Parity.Pinboard(first.ReferenceTo(Parity.Tally.Fields.N));
            var heard = new List<int>();
            var subscriptions = new List<IDisposable>();
            try
            {
                for (int i = 0; i < 32; i++)
                {
                    int index = i;
                    subscriptions.Add(board.OnChanged(Parity.Pinboard.Fields.Note, (_, _) => heard.Add(index)));
                }
                board.Note = second.ReferenceTo(Parity.Tally.Fields.N);
                CollectionAssert.AreEqual(Enumerable.Range(0, 32), heard);
                heard.Clear();
                first.N = 7;
                Assert.IsEmpty(heard);
                for (int i = 0; i < 32; i += 2)
                    subscriptions[i].Dispose();
                subscriptions.Add(board.OnChanged(Parity.Pinboard.Fields.Note, (_, _) => heard.Add(32)));
                second.N = 8;
                CollectionAssert.AreEqual(Enumerable.Range(0, 32).Where(i => i % 2 != 0).Append(32), heard);
            }
            finally
            {
                foreach (IDisposable subscription in subscriptions)
                    subscription.Dispose();
            }
        }

        private static ProjectData ConditionalReferenceGetterData(bool sharedControl)
        {
            ProjectData data = Data();
            var tallyClass = data.classes.Values.Single(c => c.name == "Tally");
            var other = new IntMember
            {
                id = "test-tally-other",
                name = "Other",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new NumberMemberValueBase { value = 10 },
            };
            data.members[other.id] = other;
            tallyClass.schema["Other"] = other.id;
            NSPropertyMember member = MemberNamed<NSPropertyMember>(data, "NRef");
            var originalReturn = (ReturnInstruction)member.getter!.instructions[0];
            var nReference = (RowOfPointer)originalReturn.pointer;
            member.getter.instructions = new Instruction[]
            {
                new IfInstruction
                {
                    type = InstructionKind.If,
                    branches = new[]
                    {
                        new ConditionalBranch
                        {
                            expression = new BooleanExpression
                            {
                                condition = new Condition
                                {
                                    type = OperatorKind.GreaterThan,
                                    operand1 = sharedControl
                                        ? new ReferencePointer { type = PointerKind.Reference, valueId = MemberNamed<IntMember>(data, "Total").valueId! }
                                        : nReference.place,
                                    operand2 = new ValuePointer
                                    {
                                        type = PointerKind.Value,
                                        value = new Value { typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true }, value = new JValue(0) },
                                    },
                                },
                            },
                            instructions = new Instruction[] { originalReturn },
                        },
                    },
                },
                new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = new RowOfPointer
                    {
                        type = PointerKind.RowOf,
                        place = new KeyOfPointer
                        {
                            type = PointerKind.KeyOf, memberId = other.id,
                            keyOf = new KeyOf
                            {
                                pointer = new VariablePointer { type = PointerKind.Variable, variableId = "__this__" },
                                key = new ValuePointer
                                {
                                    type = PointerKind.Value,
                                    value = new Value { typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true }, value = new JValue("Other") },
                                },
                            },
                        },
                    },
                },
            };
            return data;
        }
    }
}
