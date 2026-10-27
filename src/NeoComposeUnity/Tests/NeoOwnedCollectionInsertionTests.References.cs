// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class NeoOwnedCollectionInsertionTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ReferenceReadCommitsOnlyItsRemovedAncestor(bool place, bool alreadyCommitted)
        {
            using var client = ControlClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var graph = HeldRemoval(client, context, place ? "Coins" : "Lists");
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, graph.inner, out MemberValue? row));
            string target;
            NSSlotPlace? slot = null;
            if (place)
            {
                var coin = (ObjectMemberValue)row!;
                string memberId = client.classes[coin.classId!].schema["Value"];
                target = coin.value!["Value"];
                slot = NSSlotPlace.Member(graph.alias, "Value", memberId);
            }
            else
                target = ((ArrayMemberValue)row!).value![0];
            var reference = new NSSlotReference(target, slot,
                new PrimitiveTypeInfo { type = MemberKind.Int, required = true }, null);
            Assert.AreEqual(place ? 7 : 3, NSGetterEvaluator.ReadThroughReference(reference, context));
            int published = 0;
            client.OnWritableValuesPublished += (_, _) => published++;
            client.EnterScriptWrites();
            try
            {
                var batch = StageRemoval(client, context, graph);
                Assert.IsTrue(batch.RemovesReference(NeoValueOwnership.Session, target));
                Assert.IsFalse(batch.RemovesReference(NeoValueOwnership.Save, target));
                if (alreadyCommitted)
                    client.CommitScriptWrites();
                Assert.Catch(() => NSGetterEvaluator.ReadThroughReference(reference, context));
                Assert.AreEqual(1, published);
                Assert.AreEqual(target, reference.TargetId);
                Assert.IsFalse(client.sessionValues.ContainsKey(target));
                string retained = NSGetterEvaluator.FindRowReference(graph.alias, context)!.valueId;
                Assert.AreNotEqual(graph.inner, retained);
                Assert.IsTrue(client.sessionValues.ContainsKey(retained));
            }
            finally { client.ExitScriptWrites(); }
            Assert.AreEqual(1, published);
        }

        [TestCase("unrelated")]
        [TestCase("gain")]
        [TestCase("rollback")]
        public void ReferenceReadLeavesUnrelatedOrReattachedWorkPending(string mode)
        {
            using var client = ControlClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var graph = HeldRemoval(client, context);
            client.TryGetValue(NeoValueOwnership.Session, graph.inner, out ArrayMemberValue? row);
            string target = row!.value![0];
            var reference = new NSSlotReference(target, null,
                new PrimitiveTypeInfo { type = MemberKind.Int, required = true }, null);
            var unrelated = HeldRemoval(client, context, "Sets");
            int publications = 0;
            client.OnWritableValuesPublished += (_, _) => publications++;
            client.EnterScriptWrites();
            try
            {
                var batch = mode == "unrelated" ? StageRemoval(client, context, unrelated) : client.ScriptWriteBatch(graph.outer)!;
                if (mode == "gain")
                {
                    StageRemoval(client, context, graph);
                    batch.Stage(b =>
                    {
                        b.List(NeoValueOwnership.Session, graph.outer, context, null).Insert(0, graph.inner, NeoTimestamp.Now());
                        b.Gain(NeoValueOwnership.Session, graph.inner, graph.outer);
                    });
                }
                if (mode == "rollback")
                    Assert.Throws<InvalidOperationException>(() => batch.Stage(b =>
                    {
                        b.Release(NeoValueOwnership.Session, graph.inner, graph.member, graph.outer);
                        throw new InvalidOperationException("rejected release");
                    }));
                Assert.IsFalse(batch.RemovesReference(NeoValueOwnership.Session, target));
                if (mode != "gain")
                    Assert.AreEqual(3, NSGetterEvaluator.ReadThroughReference(reference, context));
                Assert.AreSame(batch, client.PendingScriptWrites);
                Assert.AreEqual(0, publications);
            }
            finally { client.ExitScriptWrites(); }
        }

        [TestCase("row")]
        [TestCase("shape")]
        [TestCase("bindings")]
        [TestCase("schema")]
        public void RemovalContextCannotAuthorizeReplacedOrReboundRows(string change)
        {
            using var client = ControlClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var graph = HeldRemoval(client, context);
            var plan = new NeoWritePlan(client);
            var batch = new NeoWriteBatch(plan, false);
            batch.Stage(b => b.Release(NeoValueOwnership.Session, graph.inner, graph.member, graph.outer));
            var row = plan.Resolve(NeoValueOwnership.Session, graph.inner)!;
            Assert.AreSame(graph.member, batch.RemovalMember(NeoValueOwnership.Session, graph.inner, row));
            Assert.IsNull(batch.RemovalMember(NeoValueOwnership.Save, graph.inner, row));
            if (change == "row" || change == "shape")
            {
                MemberValue next = change == "row"
                    ? new ArrayMemberValue { id = graph.inner, value = ((ArrayMemberValue)row).value }
                    : new NumberMemberValue { id = graph.inner, value = 7 };
                plan.Set(NeoValueOwnership.Session, next);
                Assert.IsNull(batch.RemovalMember(NeoValueOwnership.Session, graph.inner, next));
                Assert.IsNull(batch.RemovalMember(NeoValueOwnership.Session, graph.inner, row));
            }
            else
            {
                if (change == "bindings")
                    row.typeSource = NeoTypeSource.OfType(TestTypeBindings.List(TestTypeBindings.Of(MemberKind.Int)));
                else
                    client.InvalidateSchemaResolutionCaches();
                Assert.Throws<InvalidOperationException>(() => batch.RemovalMember(NeoValueOwnership.Session, graph.inner, row));
            }
        }

        [Test]
        public void FailedGainRestoresEarlierReleaseContext()
        {
            using var client = ControlClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var graph = HeldRemoval(client, context);
            var plan = new NeoWritePlan(client);
            var batch = new NeoWriteBatch(plan, false);
            batch.Stage(b => b.Release(NeoValueOwnership.Session, graph.inner, graph.member, graph.outer));
            var row = plan.Resolve(NeoValueOwnership.Session, graph.inner)!;
            Assert.Throws<InvalidOperationException>(() => batch.Stage(b =>
            {
                b.Gain(NeoValueOwnership.Session, graph.inner, graph.outer);
                Assert.IsNull(b.RemovalMember(NeoValueOwnership.Session, graph.inner, row));
                throw new InvalidOperationException("rejected reattachment");
            }));
            Assert.AreSame(graph.member, batch.RemovalMember(NeoValueOwnership.Session, graph.inner, row));
            Assert.IsTrue(batch.RemovesReference(NeoValueOwnership.Session, ((ArrayMemberValue)row).value![0]));
        }

        [TestCase("closed")]
        [TestCase("string")]
        [TestCase("missing")]
        [TestCase("partial")]
        [TestCase("open")]
        [TestCase("scope")]
        [TestCase("parent")]
        [TestCase("optional")]
        [TestCase("concrete")]
        public void CollectionReferenceUsesExactTargetStamp(string mode)
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Root + "paired-export.json"));
            var set = schema.members.Values.OfType<SetMember>().First(m => schema.members[m.entryMemberId] is GenericMember);
            var generic = (GenericMember)schema.members[set.entryMemberId];
            var integer = schema.members.Values.First(m => m.kind == MemberKind.Int && m.Requirement == NeoMemberRequirementKind.Required);
            var text = schema.members.Values.First(m => m.kind == MemberKind.String && m.Requirement == NeoMemberRequirementKind.Required);
            if (mode == "optional")
                set.Requirement = NeoMemberRequirementKind.Optional;
            using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            var plan = new NeoWritePlan(client);
            string id = Guid.NewGuid().ToString();
            string genericOwner = schema.classes.Values.Single(c => c.genericParams?.Any(p => p.id == generic.genericParamId) == true).id;
            TypeBinding bound = mode == "string" ? TestTypeBindings.Of(text) : TestTypeBindings.Of(integer);
            string parentId = Guid.NewGuid().ToString();
            NeoTypeSource? typeSource = mode switch
            {
                "missing" => null,
                // The parent stamps only an unrelated param, not the one the type names.
                "partial" => NeoTypeSource.OfType(TestTypeBindings.Set(TestTypeBindings.Open(genericOwner, generic.genericParamId)), parentId),
                "open" => NeoTypeSource.OfType(TestTypeBindings.Set(TestTypeBindings.Open(genericOwner, generic.genericParamId))),
                _ => NeoTypeSource.OfType(TestTypeBindings.Set(bound, required: mode != "optional")),
            };
            plan.Set(NeoValueOwnership.Session, new ArrayMemberValue { id = id, value = Array.Empty<string>(), typeSource = typeSource });
            if (mode == "scope")
                plan.Set(NeoValueOwnership.Save, new ArrayMemberValue { id = id, value = Array.Empty<string>(), typeSource = NeoTypeSource.OfType(TestTypeBindings.Set(TestTypeBindings.Of(text))) });
            if (mode == "partial")
                plan.Set(NeoValueOwnership.Session, new ObjectMemberValue { id = parentId, classId = genericOwner, value = new(), genericBindings = TestTypeBindings.Stamp(("unrelated", TestTypeBindings.Of(integer))) });
            if (mode == "parent")
            {
                var type = (GenericTypeInfo)((LookupTypeInfo)NeoReferenceTypes.SlotTypeInfo(client, set)!).entryTypeInfo!;
                plan.Set(NeoValueOwnership.Session, new ObjectMemberValue { id = parentId, classId = type.ownerClassId, value = new(), genericBindings = TestTypeBindings.Stamp((generic.genericParamId, TestTypeBindings.Of(text))) });
            }
            // Reader controls use a candidate overlay so deliberately conflicting
            // parent/typeSource shapes do not weaken normal write validation.
            using (client.ReadCandidate(plan))
            {
                var context = new NSGetterEvaluator.Context(client, null, null);
                Member member = mode == "concrete"
                    ? client.members.Values.OfType<SetMember>().First(m => client.members[m.entryMemberId].kind == MemberKind.Int)
                    : set;
                var place = mode == "parent"
                    ? NSSlotPlace.Member(NSGetterEvaluator.ResolveValueIfId(parentId, context)!, "Value", member.id)
                    : NSSlotPlace.Static(member.id);
                var reference = new NSSlotReference(id, null, null, null);
                if (mode == "missing")
                {
                    Exception? error = Assert.Catch(() => NSGetterEvaluator.TargetSlotType(reference, place, context));
                    Assert.That(error!.Message, Does.Contain("is missing typeSource"));
                    return;
                }
                if (mode == "partial")
                {
                    var error = Assert.Throws<InvalidOperationException>(() => NSGetterEvaluator.TargetSlotType(reference, place, context));
                    Assert.That(error!.Message, Does.Contain("doesn't stamp"));
                    return;
                }
                TypeInfo actual = NSGetterEvaluator.TargetSlotType(reference, place, context);
                if (mode == "open")
                {
                    // §12.1: an open type with no valueId reads as a member
                    // default's template, which closes nothing, so it fits no
                    // closed Reference. The validator refuses it outside a
                    // default (P127TypeSourceTests).
                    Assert.IsTrue(NeoNSFunctionRuntime.ContainsGeneric(actual));
                    var closed = NeoReferenceTypes.SlotTypeInfo(client, set, NeoGenericResolution.EnvFromStamp(
                        client,
                        TestTypeBindings.Stamp((generic.genericParamId, bound))))!;
                    Assert.IsFalse(NeoReferenceTypes.SlotFits(actual, closed));
                    return;
                }
                TypeInfo expected = NeoReferenceTypes.SlotTypeInfo(client, member,
                    mode == "concrete" ? null : NeoGenericResolution.EnvFromStamp(client, TestTypeBindings.Stamp((generic.genericParamId, bound))))!;
                Assert.IsTrue(NeoReferenceTypes.InvariantlyEqual(expected, actual));
                Assert.IsFalse(NeoNSFunctionRuntime.ContainsGeneric(actual));
                if (mode != "concrete")
                {
                    var wrong = NeoReferenceTypes.SlotTypeInfo(client, set, NeoGenericResolution.EnvFromStamp(
                        client,
                        TestTypeBindings.Stamp((generic.genericParamId, TestTypeBindings.Of(mode == "string" ? integer : text)))))!;
                    Assert.IsFalse(NeoReferenceTypes.SlotFits(actual, wrong));
                }
                if (mode == "optional")
                {
                    Assert.IsFalse(actual.required);
                    var required = new LookupTypeInfo { type = MemberKind.Lookup, required = true, entryTypeInfo = ((LookupTypeInfo)actual).entryTypeInfo };
                    Assert.IsFalse(NeoReferenceTypes.SlotFits(actual, required));
                }
            }
        }
    }
}
