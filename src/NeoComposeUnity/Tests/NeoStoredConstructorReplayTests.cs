// Copyright (c) Ryan Bliss. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>Real CLI export with owned arguments on stored constructor roots.</summary>
    public class NeoStoredConstructorReplayTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase("Read", 6)]
        [TestCase("ReadGeneric", 6)]
        [TestCase("ReadControlled", 6)]
        public void SourceReplay(string probe, int expected)
        {
            using NeoClient client = Client();
            Assert.AreEqual(expected, Invoke(client, probe));
        }

        [Test]
        public void SourceIdentityAndOrdinarySecondAttachment()
        {
            using NeoClient client = Client();
            Assert.AreEqual(true, Invoke(client, "PreserveIdentity"));
            StringAssert.Contains("already owned", Assert.Catch(() => Invoke(client, "OrdinarySecondAttachment")).ToString());
        }

        [Test]
        public void OptimizedReplayCanReassignAFreshChildThenRestoreItsArgument()
        {
            ProjectData data = Data();
            var box = data.classes.Values.Single(type => type.name == "Box");
            var constructor = data.constructors[box.constructorIds![0]];
            var assignment = (AssignInstruction)constructor.action!.instructions[0];
            string itemClassId = data.classes.Values.Single(type => type.name == "Item").id;
            constructor.action.instructions = new Instruction[]
            {
                new AssignInstruction
                {
                    type = InstructionKind.Assign,
                    operatorValue = "=",
                    bindings = assignment.bindings,
                    target = assignment.target,
                    pointer = new FunctionPointer
                    {
                        type = PointerKind.Function,
                        function = new ClassConstructorFunction
                        {
                            type = FunctionKind.ClassConstructor,
                            info = new FunctionClassConstructorInfo
                            {
                                schemaClassInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = itemClassId },
                                fields = Array.Empty<FunctionClassConstructorField>(),
                            },
                        },
                    },
                },
                new AssignInstruction { type = InstructionKind.Assign, operatorValue = "=", bindings = assignment.bindings, target = assignment.target, pointer = assignment.target.pointer },
                assignment,
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
            Assert.AreEqual(6, Invoke(client, "Read"));
            StringAssert.Contains("already owned", Assert.Catch(() => Invoke(client, "OrdinarySecondAttachment")).ToString());
        }

        [TestCase(1, "already owned")]
        [TestCase(2, "already owned")]
        [TestCase(3, "replay failed")]
        public void RejectedReplayRestoresContextAndSessionRows(int mode, string message)
        {
            ProjectData data = Data();
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
            ObjectMemberValue root = ControlledRoot(data);
            var before = client.sessionValues.ToDictionary(pair => pair.Key, pair => pair.Value);
            var replay = typeof(NeoClient).GetMethod("ExpandVirtualInstanceRoot", PrivateInstance)!;
            root.constructorArgs!["__arg_1__"] = mode;
            var error = Assert.Throws<TargetInvocationException>(() => replay.Invoke(client, new object[] { root }));
            StringAssert.Contains(message, error!.InnerException!.Message);
            Assert.IsNull(typeof(NeoClient).GetField("storedConstructorReplayRoot", PrivateInstance)!.GetValue(client));
            CollectionAssert.AreEquivalent(before.Keys, client.sessionValues.Keys);
            foreach (var pair in before)
                Assert.AreSame(pair.Value, client.sessionValues[pair.Key]);
            root.constructorArgs["__arg_1__"] = 0;
            Assert.DoesNotThrow(() => replay.Invoke(client, new object[] { root }));
            Assert.AreEqual(6, Invoke(client, "ReadControlled"));
            StringAssert.Contains("already owned", Assert.Catch(() => Invoke(client, "OrdinarySecondAttachment")).ToString());
        }

        [TestCase("same slot")]
        [TestCase("fresh self assignment")]
        [TestCase("different slot")]
        [TestCase("nested root")]
        [TestCase("external owner")]
        [TestCase("untrusted")]
        [TestCase("class mismatch")]
        public void WrapperWritesUseOnlyTheExactTrustedRootAndSlot(string scenario)
        {
            ProjectData data = Data();
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
            ObjectMemberValue root = ControlledRoot(data);
            string itemId = root.value!["Value"];
            var temporary = new ObjectMemberValue
            {
                id = "temporary-replay-root",
                classId = root.classId,
                value = new Dictionary<string, string>(),
            };
            var nested = new ObjectMemberValue
            {
                id = "nested-constructor-root",
                classId = root.classId,
                value = new Dictionary<string, string>(),
            };
            client.PublishConstructedSessionRows(new List<MemberValue> { temporary, nested });
            SetReplayField(client, "isReplayingVirtualInstance", scenario != "untrusted");
            SetReplayField(client, "replayingVirtualInstanceRootId", root.id);
            SetReplayField(client, "replayingVirtualInstanceClassId", scenario == "class mismatch" ? "other-class" : root.classId);
            try
            {
                using (client.BindStoredConstructorReplayRoot(temporary))
                {
                    var member = (ClassMember)data.members.Values.Single(candidate => candidate.name == "Controlled");
                    using var target = new NeoMemberClassWritable(client, member,
                        scenario == "nested root" ? nested.id : temporary.id, NeoValueOwnership.Session);
                    string key = scenario == "different slot" ? "Other" : "Value";
                    if (scenario == "external owner")
                    {
                        string otherRootId = data.members.Values.Single(candidate => candidate.name == "Stored").valueId!;
                        itemId = ((ObjectMemberValue)data.values[otherRootId]).value!["Value"];
                    }
                    var payload = NeoValueWritePayload.FromValueReference(itemId);
                    if (scenario == "same slot" || scenario == "fresh self assignment")
                    {
                        if (scenario == "fresh self assignment")
                        {
                            var fresh = new ObjectMemberValue { id = "fresh-item", classId = data.classes.Values.Single(type => type.name == "Item").id, value = new Dictionary<string, string>() };
                            client.PublishConstructedSessionRows(new List<MemberValue> { fresh });
                            var freshPayload = NeoValueWritePayload.FromValueReference(fresh.id);
                            target.SetSerializedValue(key, freshPayload);
                            Assert.DoesNotThrow(() => target.SetSerializedValue(key, freshPayload));
                            Assert.AreEqual(fresh.id, ((ObjectMemberValue)client.sessionValues[temporary.id]).value![key]);
                        }
                        Assert.DoesNotThrow(() => target.SetSerializedValue(key, payload));
                        Assert.AreEqual(itemId, ((ObjectMemberValue)client.sessionValues[temporary.id]).value![key]);
                    }
                    else
                    {
                        StringAssert.Contains("already owned", Assert.Catch(() => target.SetSerializedValue(key, payload)).ToString());
                        Assert.IsEmpty(((ObjectMemberValue)client.sessionValues[scenario == "nested root" ? nested.id : temporary.id]).value!);
                    }
                }
                Assert.IsNull(typeof(NeoClient).GetField("storedConstructorReplayRoot", PrivateInstance)!.GetValue(client));
                Assert.IsNull(client.StoredReplayDestinationValueId(temporary.id, "Value", null, null));
            }
            finally
            {
                SetReplayField(client, "isReplayingVirtualInstance", false);
                SetReplayField(client, "replayingVirtualInstanceRootId", null);
                SetReplayField(client, "replayingVirtualInstanceClassId", null);
            }
        }

        [Test]
        public void NestedCorrespondenceRestoresTheOuterScopeAfterFailure()
        {
            ProjectData data = Data();
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
            ObjectMemberValue root = ControlledRoot(data);
            SetReplayField(client, "isReplayingVirtualInstance", true);
            SetReplayField(client, "replayingVirtualInstanceRootId", root.id);
            SetReplayField(client, "replayingVirtualInstanceClassId", root.classId);
            try
            {
                var outer = new ObjectMemberValue { id = "outer-replay", classId = root.classId };
                var inner = new ObjectMemberValue { id = "inner-replay", classId = root.classId };
                using (client.BindStoredConstructorReplayRoot(outer))
                {
                    Assert.AreEqual(root.value!["Value"], client.StoredReplayDestinationValueId(outer.id, "Value", null, null));
                    Assert.Throws<InvalidOperationException>(() =>
                    {
                        using var scope = client.BindStoredConstructorReplayRoot(inner);
                        Assert.IsNull(client.StoredReplayDestinationValueId(outer.id, "Value", null, null));
                        Assert.AreEqual(root.value["Value"], client.StoredReplayDestinationValueId(inner.id, "Value", null, null));
                        throw new InvalidOperationException("nested replay failed");
                    });
                    Assert.IsNull(client.StoredReplayDestinationValueId(inner.id, "Value", null, null));
                    Assert.AreEqual(root.value["Value"], client.StoredReplayDestinationValueId(outer.id, "Value", null, null));
                }
                Assert.IsNull(client.StoredReplayDestinationValueId(outer.id, "Value", null, null));
            }
            finally
            {
                SetReplayField(client, "isReplayingVirtualInstance", false);
                SetReplayField(client, "replayingVirtualInstanceRootId", null);
                SetReplayField(client, "replayingVirtualInstanceClassId", null);
            }
        }

        private static void SetReplayField(NeoClient client, string name, object? value) =>
            typeof(NeoClient).GetField(name, PrivateInstance)!.SetValue(client, value);

        private static ObjectMemberValue ControlledRoot(ProjectData data)
        {
            string classId = data.classes.Values.Single(type => type.name == "ControlledBox").id;
            return data.values.Values.OfType<ObjectMemberValue>().Single(row => row.classId == classId);
        }

        private static ProjectData Data() => NeoTestExport.Read(File.ReadAllText(
            "Packages/com.ryanbliss.neocompose/Tests/stored-constructor-replay-export.json"));

        private static NeoClient Client() => NeoTestSaveStack.ClientFromSchema(Data(), assumeCurrentSchema: false);

        private static object? Invoke(NeoClient client, string name)
        {
            Member function = client.members.Values.Single(member => member is NSFunctionMember && member.name == name);
            return NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(Array.Empty<object?>());
        }
    }
}
