// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P106 getter subscriptions. The Save root holds a watcher and a box:
    /// <code>
    /// class Watcher { int Left; int Right;
    ///   int Value { get => this.Left; set { this.Left = value; } }
    ///   int Sum => this.Left + this.Right;
    ///   int Echo => root.Save.Other;
    ///   int Boom => this.Left + this.Explode(); }
    /// class LoudWatcher : Watcher { override int Value => this.Left + 100; }
    /// class Box&lt;T&gt; { T Item; T Current => this.Item; }
    /// class SaveRoot { Watcher Watcher; Box&lt;int&gt; Box; int Other;
    ///   void Listen() { this.Watcher.Value.OnChanged += this.Heard; this.Watcher.Left = this.Watcher.Left + 1; }
    ///   void Stop() { this.Watcher.Value.OnChanged -= this.Heard; }
    ///   void StopAll() { this.Watcher.Value.OnChanged.Clear(); } }
    /// </code>
    /// <c>Heard</c>, <c>Also</c>, and <c>Explode</c> are native.
    /// </summary>
    public class NeoGetterListenerTests
    {
        private const string ProjectId = "project-getter-listeners";
        private const string BoxParam = "box-t";

        private sealed class Fixture : IDisposable
        {
            internal NeoClient client = null!;
            internal readonly List<(string handler, int value)> heard = new();
            internal Action<string>? onHeard;
            internal bool explode;

            internal void Subscribe(string getterId, string handler = "member-heard", bool add = true,
                string ownerId = "value-watcher", TypeInfo? observedType = null) =>
                client.EditMemberChangeListener(ownerId, NeoValueOwnership.Save, getterId, observedType ?? IntType(),
                    new NeoDelegateValue { memberId = handler, valueId = "value-save" }, add);

            internal void Write(string valueId, string memberId, int value)
            {
                Assert.IsTrue(client.TryGetMember(memberId, out JsonMember? member));
                Assert.IsTrue(client.TryWriteLeaf(NeoValueOwnership.Save, Number(valueId, value), member!, "value"));
            }

            internal void WriteLeft(int value) => Write("watcher-left", "member-left", value);

            internal void Run(string functionId) =>
                new NeoMemberNSFunction(client, functionId, null, NeoValueOwnership.Save).Invoke("value-save", Array.Empty<object?>());

            public void Dispose() => client.Dispose();
        }

        [Test]
        public void VectorPropertyCompoundCapturesReceiverAndAppliesOperatorOnce()
        {
            var vectorType = new PrimitiveTypeInfo { type = MemberKind.Vector2Int, required = true };
            VariablePointer Local(string name) => new()
            {
                type = PointerKind.Variable,
                variableId = name
            };
            using Fixture fixture = Build(data =>
            {
                data.members["member-point"] = new Vector2IntMember
                {
                    id = "member-point",
                    projectId = ProjectId,
                    name = "Point",
                    kind = MemberKind.Vector2Int,
                    Requirement = NeoMemberRequirementKind.Required,
                };
                data.classes["class-watcher"].schema["Point"] = "member-point";
                data.classes["class-watcher"].schema["Vector"] = "member-vector";
                ((ObjectMemberValue)data.values["value-watcher"]).value!["Point"] = "watcher-point";
                data.values["watcher-point"] = new Vector2MemberValue { id = "watcher-point", value = new NeoVector2Value { x = 3, y = 4 } };
                var setter = Assign(KeyOf(This(), "Point"), Local("__value__"));
                setter.target.typeInfo = vectorType;
                data.members["member-vector"] = Getter("member-vector", "Vector", KeyOf(This(), "Point"), setter, returnType: vectorType);
            });
            var receiver = new ObjectInitializerPointer
            {
                type = PointerKind.ObjectInitializer,
                receiver = new Variable
                {
                    id = "receiver",
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Unknown, required = true },
                    pointer = new ReferencePointer { type = PointerKind.Reference, valueId = "value-watcher" }
                },
                assignments = new[] { Assign(KeyOf(Local("receiver"), "Left"), Add(KeyOf(Local("receiver"), "Left"), Literal(1))) },
            };
            var target = new CallGetterPointer { type = PointerKind.CallGetter, memberId = "member-vector", receiver = CallReceiver.Instance(Local("$assignment_0")) };
            var body = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                instructions = new Instruction[]
                {
                    new AssignInstruction
                    {
                        type = InstructionKind.Assign, operatorValue = "*=",
                        bindings = new[] { new Variable { id = "$assignment_0", typeInfo = new PrimitiveTypeInfo { type = MemberKind.Unknown, required = true }, pointer = receiver } },
                        target = new WriteTarget { pointer = target, typeInfo = vectorType, writability = WritabilityKind.Setter },
                        pointer = new FunctionPointer { type = PointerKind.Function, function = new VectorOpFunction { type = FunctionKind.VectorOp,
                            info = new FunctionVectorOpInfo { kind = "Vector2Int", op = "multiply", argPointers = new Pointer[] { target, Literal(2) } } } },
                    },
                },
            };
            NeoScriptExecutor.Execute(fixture.client, body, new Dictionary<string, object?>(), new NSGetterEvaluator.Context(fixture.client, null, null));
            var point = new NeoMemberVector2Int(fixture.client, "member-point", "watcher-point", NeoValueOwnership.Save);
            Assert.AreEqual(6d, point.value!.value!.x);
            Assert.AreEqual(8d, point.value.value.y);
            Assert.IsTrue(fixture.client.TryGetValue(NeoValueOwnership.Save, "watcher-left", out NumberMemberValue? calls));
            Assert.AreEqual(1d, calls!.value);
        }

        [Test]
        public void VectorDerivedGetterInvalidatesForComponentAndWholeValueWrites()
        {
            using Fixture fixture = Build(data =>
            {
                data.members["member-point"] = new Vector2IntMember
                {
                    id = "member-point",
                    projectId = ProjectId,
                    name = "Point",
                    kind = MemberKind.Vector2Int,
                    Requirement = NeoMemberRequirementKind.Required,
                };
                data.classes["class-watcher"].schema["Point"] = "member-point";
                ((ObjectMemberValue)data.values["value-watcher"]).value!["Point"] = "watcher-point";
                data.values["watcher-point"] = new Vector2MemberValue { id = "watcher-point", value = new NeoVector2Value { x = 3, y = 4 } };
                data.members["member-value"] = Getter("member-value", "Value", new FunctionPointer
                {
                    type = PointerKind.Function,
                    function = new VectorOpFunction
                    {
                        type = FunctionKind.VectorOp,
                        info = new FunctionVectorOpInfo { kind = "Vector2Int", op = "sqrMagnitude", argPointers = new Pointer[] { KeyOf(This(), "Point") } },
                    },
                });
            });
            fixture.Subscribe("member-value");
            var point = new NeoMemberVector2IntWritable(fixture.client, "member-point", "watcher-point", NeoValueOwnership.Save);
            new NeoVector2Int(point).x = 6;
            point.Set(new UnityEngine.Vector2Int(1, 2));
            CollectionAssert.AreEqual(new[] { ("member-heard", 52), ("member-heard", 5) }, fixture.heard);
        }

        [Test]
        public void BitwiseGetterTracksStoredOperandsAndCompoundWritesNotify()
        {
            Pointer Bitwise(Pointer value) => new FunctionPointer
            {
                type = PointerKind.Function,
                function = new BitwiseOpFunction
                {
                    type = FunctionKind.BitwiseOp,
                    info = new FunctionBitwiseOpInfo { op = "or", argPointers = new[] { value, Literal(2) } },
                },
            };
            using Fixture fixture = Build(data =>
            {
                data.members["member-value"] = Getter("member-value", "Value", Bitwise(KeyOf(This(), "Left")));
            });
            fixture.Subscribe("member-value");
            fixture.WriteLeft(8);
            var target = KeyOf(new ReferencePointer { type = PointerKind.Reference, valueId = "value-watcher" }, "Left");
            NeoScriptExecutor.Execute(fixture.client, new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                instructions = new Instruction[] { Assign(target, Bitwise(target)) },
            }, new Dictionary<string, object?>(), new NSGetterEvaluator.Context(fixture.client, null, null));
            // A write notifies even when the derived result stays the same.
            CollectionAssert.AreEqual(new[] { ("member-heard", 10), ("member-heard", 10) }, fixture.heard);
            fixture.WriteLeft(16);
            CollectionAssert.AreEqual(new[] { ("member-heard", 10), ("member-heard", 10), ("member-heard", 18) }, fixture.heard);
        }

        [Test]
        public void SubscribingArmsAndAReadWriteCallsOnceWithTheNewResult()
        {
            using Fixture fixture = Build();
            fixture.Subscribe("member-value");
            Assert.That(fixture.heard, Is.Empty);

            fixture.WriteLeft(5);

            CollectionAssert.AreEqual(new[] { ("member-heard", 5) }, fixture.heard);
        }

        [Test]
        public void ASetterWriteCallsIt()
        {
            using Fixture fixture = Build();
            fixture.Subscribe("member-value");
            Assert.IsTrue(fixture.client.TryGetMember("member-value", out JsonMember? value));

            NSSetterResult result = new NeoMemberNSProperty(fixture.client, (NSPropertyMember)value!, null, NeoValueOwnership.Save)
                .Set("value-watcher", 8);

            Assert.IsTrue(result.ok, result.error);
            CollectionAssert.AreEqual(new[] { ("member-heard", 8) }, fixture.heard);
        }

        [Test]
        public void StalenessCallsEvenWhenTheResultIsEqualButUnreadWritesDoNot()
        {
            using Fixture fixture = Build();
            fixture.Subscribe("member-sum");
            fixture.Subscribe("member-value", "member-also");

            fixture.Write("value-other", "member-other", 4);
            fixture.client.RunTransaction(() =>
            {
                fixture.WriteLeft(1);
                fixture.Write("watcher-right", "member-right", -1);
            });
            fixture.Write("watcher-right", "member-right", -2);

            CollectionAssert.AreEquivalent(new[]
            {
                ("member-heard", 0),
                ("member-also", 1),
                ("member-heard", -1),
            }, fixture.heard, "Sum stayed 0 and still called; Value never read Right or Other.");
        }

        [Test]
        public void HeldWritesCallOnceAfterTheOutermostRelease()
        {
            using Fixture fixture = Build();
            fixture.Subscribe("member-value");

            fixture.client.EnterScriptWrites();
            fixture.WriteLeft(1);
            fixture.client.EnterScriptWrites();
            fixture.WriteLeft(2);
            fixture.client.ExitScriptWrites();
            Assert.That(fixture.heard, Is.Empty);
            fixture.client.ExitScriptWrites();
            fixture.client.RunTransaction(() =>
            {
                fixture.WriteLeft(3);
                fixture.WriteLeft(4);
            });

            CollectionAssert.AreEqual(new[] { ("member-heard", 2), ("member-heard", 4) }, fixture.heard);
        }

        [Test]
        public void AScriptSubscriptionHearsLaterWritesButNotItsOwn()
        {
            using Fixture fixture = Build();

            fixture.Run("member-listen");
            Assert.That(fixture.heard, Is.Empty, "Listen's own Left write committed before the arming read.");
            fixture.WriteLeft(9);
            fixture.Run("member-stop");
            fixture.WriteLeft(10);

            CollectionAssert.AreEqual(new[] { ("member-heard", 9) }, fixture.heard);
        }

        [TestCase("member-value")]
        [TestCase("member-left")]
        public void ANeoScriptHandlerSeesRoot(string observed)
        {
            using Fixture fixture = Build();
            fixture.Subscribe(observed, "member-mirror");

            fixture.WriteLeft(7);

            Assert.IsTrue(fixture.client.TryGetValue(NeoValueOwnership.Save, "value-other", out MemberValue? other));
            Assert.AreEqual(7d, ((NumberMemberValue)other!).value);
        }

        [Test]
        public void IdentityFollowsP62()
        {
            using Fixture fixture = Build();
            fixture.Subscribe("member-value", add: false);
            fixture.Subscribe("member-value");
            fixture.Subscribe("member-value");
            fixture.WriteLeft(1);
            fixture.Subscribe("member-value", add: false);
            fixture.WriteLeft(2);

            CollectionAssert.AreEqual(new[] { ("member-heard", 1) }, fixture.heard);
        }

        [Test]
        public void HandlersRunInOrderAndARemovalWaitsForTheNextDispatch()
        {
            using Fixture fixture = Build();
            fixture.Subscribe("member-value");
            fixture.Subscribe("member-value", "member-also");
            fixture.onHeard = handler =>
            {
                if (handler == "member-heard")
                    fixture.Subscribe("member-value", "member-also", add: false);
            };

            fixture.WriteLeft(1);
            fixture.WriteLeft(2);

            CollectionAssert.AreEqual(new[]
            {
                ("member-heard", 1),
                ("member-also", 1),
                ("member-heard", 2),
            }, fixture.heard);
        }

        [Test]
        public void BaseAndDerivedSpellingsAreOneSubscriptionThatReadsTheOverride()
        {
            using Fixture fixture = Build(data => ((ObjectMemberValue)data.values["value-watcher"]).classId = "class-loud-watcher");
            fixture.Subscribe("member-value");
            fixture.Subscribe("member-value-loud");

            fixture.WriteLeft(1);

            CollectionAssert.AreEqual(new[] { ("member-heard", 101) }, fixture.heard);
        }

        [Test]
        public void AGenericReceiverDeliversTheSubstitutedType()
        {
            using Fixture fixture = Build();
            var error = Assert.Catch<NSGetterRuntimeError>(() => fixture.Subscribe("member-current", ownerId: "value-box",
                observedType: new PrimitiveTypeInfo { type = MemberKind.String, required = true }));
            StringAssert.Contains("no longer matches its compiled type", error!.Message);
            fixture.Subscribe("member-current", ownerId: "value-box");

            fixture.Write("box-item", "member-box-binding", 6);

            CollectionAssert.AreEqual(new[] { ("member-heard", 6) }, fixture.heard);
        }

        [Test]
        public void ANewInstanceInTheSlotStartsWithNoSubscriptions()
        {
            using Fixture fixture = Build();
            fixture.Subscribe("member-value");

            fixture.client.SetWritableValue(NeoValueOwnership.Save, ObjectValue("value-watcher", "class-loud-watcher",
                ("Left", "watcher-left"), ("Right", "watcher-right")));
            fixture.WriteLeft(1);

            Assert.That(fixture.heard, Is.Empty);
        }

        [Test]
        public void RemovingTheReceiverRowEndsItsSubscriptions()
        {
            using Fixture fixture = Build();
            fixture.client.SetWritableValues(NeoValueOwnership.Save, new MemberValue[]
            {
                Number("spare-left", 0),
                Number("spare-right", 0),
                ObjectValue("value-spare", "class-watcher", ("Left", "spare-left"), ("Right", "spare-right")),
            });
            fixture.Subscribe("member-value", ownerId: "value-spare");
            System.Collections.IDictionary subscriptions = GetterSubscriptions(fixture.client);
            Assert.That(subscriptions.Contains("value-spare"), Is.True);

            Assert.IsTrue(fixture.client.TryGetMember("member-watcher", out JsonMember? watcher));
            var plan = new NeoWritePlan(fixture.client);
            fixture.client.StageOwnedRemoval(plan, NeoValueOwnership.Save, "value-spare", watcher);
            plan.Commit();

            Assert.That(subscriptions.Contains("value-spare"), Is.False);
            Assert.That(fixture.heard, Is.Empty);
        }

        [Test]
        public void ASubscriptionWhoseHandlerReceiversAreGoneEndsAtTheNextChange()
        {
            using Fixture fixture = Build();
            fixture.client.SetWritableValue(NeoValueOwnership.Save, ObjectValue("value-listener", "class-save-root"));
            fixture.client.EditMemberChangeListener("value-watcher", NeoValueOwnership.Save, "member-value", IntType(),
                new NeoDelegateValue { memberId = "member-heard", valueId = "value-listener" }, add: true);
            System.Collections.IDictionary subscriptions = GetterSubscriptions(fixture.client);

            var plan = new NeoWritePlan(fixture.client);
            fixture.client.StageOwnedRemoval(plan, NeoValueOwnership.Save, "value-listener", null);
            plan.Commit();
            Assert.That(subscriptions.Contains("value-watcher"), Is.True, "Nothing it read changed yet.");
            fixture.WriteLeft(1);

            Assert.That(subscriptions.Contains("value-watcher"), Is.False);
            Assert.That(fixture.heard, Is.Empty);
        }

        [Test]
        public void ClearEndsTheSubscriptionUntilTheNextOne()
        {
            using Fixture fixture = Build();
            fixture.Subscribe("member-value");
            fixture.Subscribe("member-value", "member-also");

            fixture.Run("member-stop-all");
            fixture.WriteLeft(1);
            Assert.That(GetterSubscriptions(fixture.client).Contains("value-watcher"), Is.False);
            fixture.Subscribe("member-value");
            fixture.WriteLeft(2);

            CollectionAssert.AreEqual(new[] { ("member-heard", 2) }, fixture.heard);
        }

        [Test]
        public void AThrowingHandlerAbortsTheFlushAndTheNextWriteDispatches()
        {
            using Fixture fixture = Build();
            fixture.Subscribe("member-value");
            fixture.Subscribe("member-value", "member-also");
            bool fail = true;
            fixture.onHeard = handler =>
            {
                if (fail && handler == "member-heard")
                    throw new InvalidOperationException("Heard failed.");
            };

            Assert.That(() => fixture.WriteLeft(1), Throws.InstanceOf<Exception>());
            fail = false;
            fixture.WriteLeft(2);

            CollectionAssert.AreEqual(new[]
            {
                ("member-heard", 1),
                ("member-heard", 2),
                ("member-also", 2),
            }, fixture.heard);
        }

        [Test]
        public void AGetterThatThrowsOnRearmKeepsItsSubscription()
        {
            using Fixture fixture = Build();
            fixture.Subscribe("member-boom");

            fixture.explode = true;
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("Boom"));
            Assert.That(() => fixture.WriteLeft(1), Throws.InstanceOf<Exception>());
            fixture.explode = false;
            fixture.WriteLeft(2);

            CollectionAssert.AreEqual(new[] { ("member-heard", 2) }, fixture.heard);
        }

        [Test]
        public void ACSharpWatchAndANeoScriptSubscriptionEachHearOneChange()
        {
            using Fixture fixture = Build();
            var watcher = new NeoMemberClassWritable(fixture.client, "member-watcher", "value-watcher", NeoValueOwnership.Save);
            // Echo => root.Save.Other, so only the getter tells the view it changed.
            Assert.AreEqual(0, Convert.ToInt32(watcher.Get<NeoMemberNSProperty>("Echo").Compute("value-watcher").value));
            int csharp = 0;
            using IDisposable watch = fixture.client.WatchGetters("value-watcher", watcher);
            watcher.OnChanged += _ => csharp++;
            fixture.Subscribe("member-echo");

            fixture.Write("value-other", "member-other", 3);

            Assert.AreEqual(1, csharp);
            CollectionAssert.AreEqual(new[] { ("member-heard", 3) }, fixture.heard);
        }

        [Test]
        public void SequenceWatchTracksOnlyConsumedReadsAndRetiresOldCursors()
        {
            using Fixture fixture = Build(ConfigureSequenceGetter);
            var watcher = new NeoMemberClassWritable(fixture.client, "member-watcher", "value-watcher", NeoValueOwnership.Save);
            var property = watcher.Get<NeoMemberNSProperty>("Echo");
            int changes = 0;
            watcher.OnChanged += _ => changes++;
            IDisposable watch = fixture.client.WatchGetters("value-watcher", watcher);
            var context = fixture.client.CreateGetterContext(NeoValueOwnership.Save);
            var first = (NSGetterEvaluator.ScriptSequence)property.Compute("value-watcher").value!;
            var second = (NSGetterEvaluator.ScriptSequence)property.Compute("value-watcher").value!;
            Assert.That(second, Is.Not.SameAs(first));
            var old = first.GetEnumerator(context);
            fixture.Write("value-other", "member-other", 1);
            Assert.That(changes, Is.Zero, "Creating a cursor reads no generator body.");
            Assert.That(old.MoveNext(context), Is.True);
            Assert.That(old.Current(context), Is.EqualTo(1d));
            fixture.Write("value-other", "member-other", 2);
            Assert.That(changes, Is.EqualTo(1));
            property.Compute("value-watcher");
            Assert.That(old.MoveNext(context), Is.True);
            fixture.Write("value-other", "member-other", 3);
            Assert.That(changes, Is.EqualTo(1), "A cursor from the invalidated generation cannot append.");
            var live = ((NSGetterEvaluator.ScriptSequence)property.Compute("value-watcher").value!).GetEnumerator(context);
            Assert.That(live.MoveNext(context), Is.True);
            watch.Dispose();
            watch = fixture.client.WatchGetters("value-watcher", watcher);
            property.Compute("value-watcher");
            Assert.That(live.MoveNext(context), Is.True);
            fixture.Write("value-other", "member-other", 4);
            Assert.That(changes, Is.EqualTo(1), "A future watch cannot revive a dropped generation.");
            watch.Dispose();
        }

        [Test]
        public void RetainedGeneratorRootsUseTheConsumingContextAfterCacheReplacement()
        {
            using Fixture fixture = Build(ConfigureSequenceGetter);
            var watcher = new NeoMemberClassWritable(fixture.client, "member-watcher", "value-watcher", NeoValueOwnership.Save);
            var property = watcher.Get<NeoMemberNSProperty>("Echo");
            var sequence = (NSGetterEvaluator.ScriptSequence)property.Compute("value-watcher").value!;
            fixture.client.InvalidateSchemaResolutionCaches();
            fixture.Write("value-other", "member-other", 3);
            var consumer = fixture.client.CreateGetterContext(NeoValueOwnership.Save);
            var cursor = sequence.GetEnumerator(consumer);
            Assert.That(cursor.MoveNext(consumer), Is.True);
            Assert.That(cursor.Current(consumer), Is.EqualTo(3d));
            fixture.client.InvalidateSchemaResolutionCaches();
            fixture.Write("value-other", "member-other", 4);
            consumer = fixture.client.CreateGetterContext(NeoValueOwnership.Save);
            Assert.That(cursor.MoveNext(consumer), Is.True);
            Assert.That(cursor.Current(consumer), Is.EqualTo(4d));
            cursor.Dispose(consumer);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SequenceOnChangedDoesNotEnumerateAndReceivesFreshValues(bool cursorResult)
        {
            TypeInfo? observed = null;
            using Fixture fixture = Build(data =>
            {
                ConfigureSequenceGetter(data);
                var property = (NSPropertyMember)data.members["member-echo"];
                if (cursorResult)
                {
                    observed = new InterfaceTypeInfo
                    {
                        type = MemberKind.Interface,
                        required = true,
                        interfaceId = NeoSequenceContract.Enumerator,
                        typeArguments = new() { [NeoSequenceContract.EnumeratorElement] = IntType() }
                    };
                    property.returnTypeInfo = observed;
                    property.getter!.typeInfo = observed;
                    property.getter.generator!.kind = "enumerator";
                }
                else
                    observed = property.returnTypeInfo;
                var protocol = (InterfaceTypeInfo)observed!;
                ((FunctionMember)data.members["member-heard"]).argumentTypes[0] = new FunctionArgumentTypeInfo
                {
                    name = "next",
                    type = MemberKind.Interface,
                    required = true,
                    interfaceId = protocol.interfaceId,
                    typeArguments = protocol.typeArguments,
                };
            });
            var received = new List<object?>();
            fixture.client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["member-heard"] = (_, _, args, _) =>
                {
                    received.Add(args[0]);
                    return null;
                },
            });
            fixture.Subscribe("member-echo", observedType: observed);
            fixture.Write("value-other", "member-other", 1);
            Assert.That(received, Is.Empty);
            var watcher = new NeoMemberClassWritable(fixture.client, "member-watcher", "value-watcher", NeoValueOwnership.Save);
            var property = watcher.Get<NeoMemberNSProperty>("Echo");
            var context = fixture.client.CreateGetterContext(NeoValueOwnership.Save);
            object value = property.Compute("value-watcher").value!;
            var cursor = cursorResult ? (NSGetterEvaluator.ScriptCursor)value : ((NSGetterEvaluator.ScriptSequence)value).GetEnumerator(context);
            Assert.That(cursor.MoveNext(context), Is.True);
            fixture.Write("value-other", "member-other", 2);
            Assert.That(received, Has.Count.EqualTo(1));
            Assert.That(received[0], Is.Not.SameAs(value));
            if (cursorResult)
                Assert.That(() => ((NSGetterEvaluator.ScriptCursor)received[0]!).Current(context), Throws.Exception);
            Assert.That(cursor.MoveNext(context), Is.True);
            fixture.Write("value-other", "member-other", 3);
            Assert.That(received, Has.Count.EqualTo(1), "A retired cursor cannot restore the listener's dependencies.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeSequenceArgumentsPreserveCollectionIdentityAndValidateElementTypes(bool generic)
        {
            using Fixture fixture = Build(data =>
            {
                var function = (FunctionMember)data.members["member-heard"];
                if (generic)
                    function.genericParams = new[] { new FunctionGenericParam { name = "T" } };
                function.argumentTypes[0] = new FunctionArgumentTypeInfo
                {
                    name = "next",
                    type = MemberKind.Interface,
                    required = true,
                    interfaceId = NeoSequenceContract.Enumerable,
                    typeArguments = new() { [NeoSequenceContract.EnumerableElement] = generic ? new FunctionGenericTypeInfo { type = MemberKind.Generic, required = true, ownerMemberId = "member-heard", index = 0 } : IntType() },
                };
            });
            object? received = null;
            fixture.client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["member-heard"] = (_, _, args, _) =>
                {
                    received = args[0];
                    return null;
                },
            });
            var context = fixture.client.CreateGetterContext(NeoValueOwnership.Save);
            var list = NSGetterEvaluator.TagSequenceCollection(new object[] { 1d, 2d },
                new CollectionTypeInfo { type = MemberKind.List, entryTypeInfo = IntType(), required = true }, context);
            fixture.client.InvokeNativeFunction(fixture.client.ResolveNativeFunction("member-heard"), new object(), new object[] { list }, generic ? new TypeInfo[] { IntType() } : null);
            Assert.That(received, Is.SameAs(list));
            var wrong = NSGetterEvaluator.TagSequenceCollection(new object[] { "value" },
                new CollectionTypeInfo
                {
                    type = MemberKind.List,
                    entryTypeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                    required = true
                }, context);
            Assert.That(() => fixture.client.InvokeNativeFunction(fixture.client.ResolveNativeFunction("member-heard"), new object(), new object[] { wrong }, generic ? new TypeInfo[] { IntType() } : null), Throws.Exception);
        }

        [Test]
        public void SequenceClassificationIncludesNullableAndDerivedInterfaces()
        {
            using Fixture fixture = Build(data => data.interfaces["derived-sequence"] = new Interface
            {
                id = "derived-sequence",
                name = "DerivedSequence",
                extendsInterfaceIds = new List<string> { NeoSequenceContract.Enumerable },
            });
            Assert.That(fixture.client.IsSequenceType(new InterfaceTypeInfo
            {
                type = MemberKind.Interface,
                interfaceId = "derived-sequence",
                required = false
            }), Is.True);
            Assert.That(fixture.client.IsSequenceType(new InterfaceTypeInfo
            {
                type = MemberKind.Interface,
                interfaceId = NeoSequenceContract.Enumerator,
                required = false
            }), Is.True);
            Assert.That(fixture.client.IsSequenceType(IntType()), Is.False);
        }

        [Test]
        public void OneSequenceGenerationRetiresAcrossReadOwnershipContexts()
        {
            using Fixture fixture = Build(ConfigureSequenceGetter);
            var watcher = new NeoMemberClassWritable(fixture.client, "member-watcher", "value-watcher", NeoValueOwnership.Save);
            using var watch = fixture.client.WatchGetters("value-watcher", watcher);
            NeoClient.SequenceGeneration Capture(NeoValueOwnership readOwnership)
            {
                var enclosing = fixture.client.BeginGetterReadCapture();
                return fixture.client.MemoizeSequenceReads(new NeoClient.GetterMemoKey(NeoValueOwnership.Save,
                    "value-watcher", "member-echo", readOwnership), fixture.client.EndGetterReadCapture(enclosing))!;
            }
            var first = Capture(NeoValueOwnership.Save);
            var second = Capture(NeoValueOwnership.Session);
            Assert.That(second, Is.SameAs(first));
            Assert.That(first.Entries, Has.Count.EqualTo(2));
            fixture.client.InvalidateGetterMemo();
            Assert.That(first.Live, Is.False);
            Assert.That(Capture(NeoValueOwnership.Save), Is.Not.SameAs(first));
        }

        private static void ConfigureSequenceGetter(ProjectData data)
        {
            var property = (NSPropertyMember)data.members["member-echo"];
            var sequenceType = new InterfaceTypeInfo
            {
                type = MemberKind.Interface,
                required = true,
                interfaceId = NeoSequenceContract.Enumerable,
                typeArguments = new() { [NeoSequenceContract.EnumerableElement] = IntType() },
            };
            Pointer read = ((ReturnInstruction)property.getter!.instructions[0]).pointer!;
            property.returnTypeInfo = sequenceType;
            property.getter.typeInfo = sequenceType;
            property.getter.generator = new GeneratorBody { kind = "enumerable", elementType = IntType() };
            property.getter.instructions = new Instruction[]
            {
                    new YieldReturnInstruction { type = InstructionKind.YieldReturn, yields = true, pointer = read },
                    new YieldReturnInstruction { type = InstructionKind.YieldReturn, yields = true, pointer = read },
            };
        }

        [Test]
        public void ConstructionCodeCantSubscribe()
        {
            using Fixture fixture = Build();
            using (fixture.client.BeginConstructionListeners())
            {
                var error = Assert.Catch<NSGetterRuntimeError>(() => fixture.Subscribe("member-value"));
                StringAssert.Contains("so a constructor can't make one", error!.Message);
                error = Assert.Catch<NSGetterRuntimeError>(() =>
                    fixture.client.ClearMemberChangeListeners("value-watcher", NeoValueOwnership.Save, "member-value", IntType()));
                StringAssert.Contains("so a constructor can't clear one", error!.Message);
            }
        }

        [Test]
        public void NothingReachesTheSaveOrSession()
        {
            using Fixture fixture = Build();
            fixture.Subscribe("member-value");
            fixture.WriteLeft(1);

            string save = fixture.client.SerializeSaveData();
            Assert.That(JObject.Parse(save)["changeListeners"], Is.Null);
            StringAssert.DoesNotContain("member-heard", save);
            Assert.IsTrue(fixture.client.TryGetValue(NeoValueOwnership.Session, "value-session", out MemberValue? session));
            Assert.That(((ObjectMemberValue)session!).changeListeners, Is.Null);
        }

        private static System.Collections.IDictionary GetterSubscriptions(NeoClient client) =>
            (System.Collections.IDictionary)typeof(NeoClient)
                .GetField("getterSubscriptionsByRow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(client)!;

        private static Fixture Build(Action<ProjectData>? configure = null)
        {
            var members = new List<JsonMember>
            {
                RootMember("member-root-assets", "value-assets", "class-root", NeoMemberStorage.Inherit),
                RootMember("member-root-save", "value-save", "class-save-root", NeoMemberStorage.Save),
                RootMember("member-root-session", "value-session", "class-root", NeoMemberStorage.Session),
                new ClassMember
                {
                    id = "member-watcher", projectId = ProjectId, name = "Watcher", kind = MemberKind.Class,
                    DeclaredClassId = "class-watcher", Requirement = NeoMemberRequirementKind.Required,
                },
                new ClassMember
                {
                    id = "member-box", projectId = ProjectId, name = "Box", kind = MemberKind.Class,
                    DeclaredClassId = "class-box", Requirement = NeoMemberRequirementKind.Required,
                },
                new GenericMember
                {
                    id = "member-item", projectId = ProjectId, name = "Item", kind = MemberKind.Generic,
                    genericParamId = BoxParam,
                },
                IntMember("member-box-binding", "Item"),
                IntMember("member-left", "Left"),
                IntMember("member-right", "Right"),
                IntMember("member-other", "Other"),
                Native("member-heard", "Heard", IntType()),
                Native("member-also", "Also", IntType()),
                Native("member-explode", "Explode", null),
                ScriptFunction("member-listen", "Listen",
                    Subscription(add: true),
                    Assign(KeyOf(KeyOf(This(), "Watcher"), "Left"), Add(KeyOf(KeyOf(This(), "Watcher"), "Left"), Literal(1)))),
                ScriptFunction("member-stop", "Stop", Subscription(add: false)),
                ScriptFunction("member-stop-all", "StopAll", new ClearChangeListenersInstruction
                {
                    type = InstructionKind.ClearChangeListeners,
                    target = WatcherValue(),
                }),
                Mirror(),
                Getter("member-value", "Value", KeyOf(This(), "Left"), setter: Assign(KeyOf(This(), "Left"), new VariablePointer
                {
                    type = PointerKind.Variable,
                    variableId = "__value__",
                })),
                Getter("member-value-loud", "Value", Add(KeyOf(This(), "Left"), Literal(100)), extendsMemberId: "member-value"),
                Getter("member-sum", "Sum", Add(KeyOf(This(), "Left"), KeyOf(This(), "Right"))),
                Getter("member-echo", "Echo", KeyOf(KeyOf(new VariablePointer { type = PointerKind.Variable, variableId = "__root__" }, "Save"), "Other")),
                Getter("member-boom", "Boom", Add(KeyOf(This(), "Left"), new CallFunctionPointer
                {
                    type = PointerKind.CallFunction,
                    memberId = "member-explode",
                    receiver = CallReceiver.Instance(This()),
                    args = Array.Empty<Pointer>(),
                    callSiteId = "explode",
                })),
                Getter("member-current", "Current", KeyOf(This(), "Item"),
                    returnType: new GenericTypeInfo { type = MemberKind.Generic, required = true, ownerClassId = "class-box", genericParamId = BoxParam }),
            };
            var values = new List<MemberValue>
            {
                ObjectValue("value-assets", "class-root"),
                ObjectValue("value-session", "class-root"),
                ObjectValue("value-save", "class-save-root", ("Watcher", "value-watcher"), ("Box", "value-box"), ("Other", "value-other")),
                ObjectValue("value-watcher", "class-watcher", ("Left", "watcher-left"), ("Right", "watcher-right")),
                ObjectValue("value-box", "class-box", ("Item", "box-item")),
                Number("watcher-left", 0),
                Number("watcher-right", 0),
                Number("box-item", 0),
                Number("value-other", 0),
            };
            ((ObjectMemberValue)values[4]).genericBindings = new Dictionary<string, string> { [BoxParam] = "member-box-binding" };
            var box = Class("class-box", null, ("Item", "member-item"), ("Current", "member-current"));
            box.genericParams = new List<GenericParamDeclaration> { new() { id = BoxParam, name = "T" } };
            var data = new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "Getter listeners",
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
                        ("Watcher", "member-watcher"), ("Box", "member-box"), ("Other", "member-other"),
                        ("Heard", "member-heard"), ("Also", "member-also"), ("Listen", "member-listen"), ("Stop", "member-stop"),
                        ("Mirror", "member-mirror")),
                    ["class-watcher"] = Class("class-watcher", null,
                        ("Left", "member-left"), ("Right", "member-right"), ("Value", "member-value"),
                        ("Sum", "member-sum"), ("Echo", "member-echo"), ("Boom", "member-boom"), ("Explode", "member-explode")),
                    ["class-loud-watcher"] = Class("class-loud-watcher", "class-watcher", ("Value", "member-value-loud")),
                    ["class-box"] = box,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            }.WithUserRoot();
            foreach (JsonMember member in members)
                data.members[member.id] = member;
            foreach (MemberValue value in values)
                data.values[value.id] = value;
            configure?.Invoke(data);

            var fixture = new Fixture();
            fixture.client = NeoTestSaveStack.ClientFromSchema(data);
            NeoClient.NeoNativeFunctionInvoker Record(string handler) => (_, _, args, _) =>
            {
                fixture.heard.Add((handler, Convert.ToInt32(args[0])));
                fixture.onHeard?.Invoke(handler);
                return null;
            };
            fixture.client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["member-heard"] = Record("member-heard"),
                ["member-also"] = Record("member-also"),
                ["member-explode"] = (_, _, _, _) => fixture.explode ? throw new InvalidOperationException("Boom.") : 0,
            });
            return fixture;
        }

        private static ChangeListenerEditInstruction Subscription(bool add)
        {
            ChangeListenerEditInstruction instruction = add
                ? new AddChangeListenerInstruction { type = InstructionKind.AddChangeListener }
                : new RemoveChangeListenerInstruction { type = InstructionKind.RemoveChangeListener };
            instruction.target = WatcherValue();
            instruction.listener = new MemberTargetPointer
            {
                type = PointerKind.MemberTarget,
                memberId = "member-heard",
                receiver = CallReceiver.Instance(This()),
            };
            return instruction;
        }

        private static ChangeListenerTarget WatcherValue() => new()
        {
            owner = KeyOf(This(), "Watcher"),
            memberId = "member-value",
            typeInfo = IntType(),
            writability = WritabilityKind.Session,
        };

        // void Mirror(int next) { root.Save.Other = next; }
        private static NSFunctionMember Mirror()
        {
            NSFunctionMember mirror = ScriptFunction("member-mirror", "Mirror", Assign(
                KeyOf(KeyOf(new VariablePointer { type = PointerKind.Variable, variableId = "__root__" }, "Save"), "Other"),
                new VariablePointer { type = PointerKind.Variable, variableId = "__arg_0__" }));
            mirror.argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "next", type = MemberKind.Int, required = true } };
            mirror.action!.parameters = new[] { Parameter("__this__"), Parameter("__root__"), Parameter("__arg_0__", IntType()) };
            return mirror;
        }

        private static PrimitiveTypeInfo IntType() => new() { type = MemberKind.Int, required = true };

        private static FunctionMember Native(string id, string name, TypeInfo? argument) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Function,
            returnTypeInfo = argument is null ? IntType() : new VoidTypeInfo { type = MemberKind.Void, required = true },
            argumentTypes = argument is null
                ? Array.Empty<FunctionArgumentTypeInfo>()
                : new[] { new FunctionArgumentTypeInfo { name = "next", type = argument.type, required = true } },
            Dispatch = NeoFunctionDispatchKind.Synchronous,
        };

        private static ClassMember RootMember(string id, string valueId, string classId, NeoMemberStorage storage) => new()
        {
            id = id,
            projectId = ProjectId,
            name = id,
            kind = MemberKind.Class,
            DeclaredClassId = classId,
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
            Requirement = NeoMemberRequirementKind.Required,
        };

        private static NSFunctionMember ScriptFunction(string id, string name, params Instruction[] instructions) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.NSFunction,
            code = "compiled test function",
            returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
            argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            Dispatch = NeoFunctionDispatchKind.Synchronous,
            action = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new[] { Parameter("__this__"), Parameter("__root__") },
                instructions = instructions,
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
            },
        };

        private static NSPropertyMember Getter(string id, string name, Pointer result, AssignInstruction? setter = null,
            string? extendsMemberId = null, TypeInfo? returnType = null) => new()
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.NSProperty,
                code = "compiled test getter",
                extendsMemberId = extendsMemberId,
                returnTypeInfo = returnType ?? IntType(),
                getter = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = Array.Empty<Variable>(),
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction { type = InstructionKind.Return, pointer = result },
                    },
                    typeInfo = returnType ?? IntType(),
                },
                setterCode = setter is null ? null : "this.Left = value;",
                setter = setter is null ? null : new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = Array.Empty<Variable>(),
                    instructions = new Instruction[] { setter },
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                },
            };

        private static Variable Parameter(string id, TypeInfo? typeInfo = null) => new()
        {
            id = id,
            typeInfo = typeInfo ?? new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
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
                typeInfo = IntType(),
                writability = WritabilityKind.Save,
            },
            pointer = value,
        };

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
                typeInfo = IntType(),
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
