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
    ///   void Stop() { this.Watcher.Value.OnChanged -= this.Heard; } }
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
            var error = Assert.Throws<NSGetterRuntimeError>(() => fixture.Subscribe("member-current", ownerId: "value-box",
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
        public void ConstructionCodeCantSubscribe()
        {
            using Fixture fixture = Build();
            using (fixture.client.BeginConstructionListeners())
            {
                var error = Assert.Throws<NSGetterRuntimeError>(() => fixture.Subscribe("member-value"));
                StringAssert.Contains("so a constructor can't make one", error!.Message);
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
                    classId = "class-watcher", Requirement = NeoMemberRequirementKind.Required,
                },
                new ClassMember
                {
                    id = "member-box", projectId = ProjectId, name = "Box", kind = MemberKind.Class,
                    classId = "class-box", Requirement = NeoMemberRequirementKind.Required,
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
            NeoClient.NeoNativeFunctionInvoker Record(string handler) => (_, _, args) =>
            {
                fixture.heard.Add((handler, Convert.ToInt32(args[0])));
                fixture.onHeard?.Invoke(handler);
                return null;
            };
            fixture.client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["member-heard"] = Record("member-heard"),
                ["member-also"] = Record("member-also"),
                ["member-explode"] = (_, _, _) => fixture.explode ? throw new InvalidOperationException("Boom.") : 0,
            });
            return fixture;
        }

        private static ChangeListenerInstruction Subscription(bool add)
        {
            ChangeListenerInstruction instruction = add
                ? new AddChangeListenerInstruction { type = InstructionKind.AddChangeListener }
                : new RemoveChangeListenerInstruction { type = InstructionKind.RemoveChangeListener };
            instruction.target = new ChangeListenerTarget
            {
                owner = KeyOf(This(), "Watcher"),
                memberId = "member-value",
                typeInfo = IntType(),
                writability = WritabilityKind.Session,
            };
            instruction.listener = new MemberTargetPointer
            {
                type = PointerKind.MemberTarget,
                memberId = "member-heard",
                receiver = CallReceiver.Instance(This()),
            };
            return instruction;
        }

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
