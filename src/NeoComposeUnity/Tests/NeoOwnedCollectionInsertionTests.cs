// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
namespace NeoCompose.Tests
{
    public partial class NeoOwnedCollectionInsertionTests
    {
        private const string Root = "Packages/com.ryanbliss.neocompose/Tests/owned-collection-";

        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (string group in new[] { "class", "collection", "set", "dictionary", "controls", "snapshots", "paired", "references" })
            {
                var fixture = JObject.Parse(File.ReadAllText(Root + group + "-source.json"));
                foreach (var result in ((JObject)fixture["returns"]!).Properties())
                    yield return new TestCaseData(Root + group + "-export.json", result.Name, result.Value.ToObject<object>(), false).SetName(group + ":" + result.Name);
                foreach (var refusal in ((JObject)fixture["refuses"]!).Properties())
                    yield return new TestCaseData(Root + group + "-export.json", refusal.Name, refusal.Value.ToObject<object>(), true).SetName(group + ":" + refusal.Name);
            }
        }
        [TestCase("removed")]
        [TestCase("replaced")]
        [TestCase("rolledBack")]
        [TestCase("ancestorRemoved")]
        public void PendingIdentityCannotSurviveFailedOrRetiredDestination(string change)
        {
            using var client = ControlClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var plan = new NeoWritePlan(client);
            var batch = new NeoWriteBatch(plan, false);
            var member = new ListMember { id = "pending-list", kind = MemberKind.List, entryMemberId = "unused-entry" };
            object?[] alias = Array.Empty<object?>();
            var row = new ArrayMemberValue { id = Guid.NewGuid().ToString(), value = Array.Empty<string>() };
            int callbacks = 0;
            void Stage(NeoWriteBatch b)
            {
                b.Plan.Set(NeoValueOwnership.Session, row);
                b.TrackCollectionAttachment(alias, row, member, NeoValueOwnership.Session, context, () => callbacks++);
                Assert.IsTrue(b.HasCurrentCollectionAttachment(alias, context));
                if (change == "rolledBack")
                    throw new InvalidOperationException("late stage failure");
            }
            if (change == "rolledBack")
                Assert.Throws<InvalidOperationException>(() => batch.Stage(Stage));
            else
                batch.Stage(Stage);
            if (change == "removed")
                plan.Remove(NeoValueOwnership.Session, row.id);
            if (change == "replaced")
                plan.Set(NeoValueOwnership.Session, new ArrayMemberValue { id = row.id, value = Array.Empty<string>() });
            if (change == "ancestorRemoved")
            {
                var parent = new ArrayMemberValue { id = Guid.NewGuid().ToString(), value = new[] { row.id } };
                plan.Set(NeoValueOwnership.Session, parent);
                batch.Gain(NeoValueOwnership.Session, row.id, parent.id);
                batch.Release(NeoValueOwnership.Session, parent.id, member, "outer");
            }
            Assert.IsFalse(batch.HasCurrentCollectionAttachment(alias, context));
            batch.DiscardAttachments();
            batch.CompleteAttachments();
            Assert.AreEqual(0, callbacks);
            Assert.IsFalse(client.sessionValues.ContainsKey(row.id));
        }

        [Test]
        public void LaterFailedStageKeepsEarlierPendingIdentityAndCallback()
        {
            using var client = ControlClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var plan = new NeoWritePlan(client);
            var batch = new NeoWriteBatch(plan, false);
            var member = new ListMember { id = "pending-list", kind = MemberKind.List, entryMemberId = "unused-entry" };
            object?[] first = new object?[0], rejected = new object?[0];
            var row = new ArrayMemberValue { id = Guid.NewGuid().ToString(), value = Array.Empty<string>() };
            var bad = new ArrayMemberValue { id = Guid.NewGuid().ToString(), value = Array.Empty<string>() };
            int firstCallbacks = 0, rejectedCallbacks = 0;
            batch.Stage(b => { b.Plan.Set(NeoValueOwnership.Session, row); b.TrackCollectionAttachment(first, row, member, NeoValueOwnership.Session, context, () => firstCallbacks++); });
            Assert.Throws<InvalidOperationException>(() => batch.Stage(b =>
            {
                b.Plan.Set(NeoValueOwnership.Session, bad);
                b.TrackCollectionAttachment(rejected, bad, member, NeoValueOwnership.Session, context, () => rejectedCallbacks++);
                b.PendingAttachment(rejected, context);
                throw new InvalidOperationException("late failure");
            }));
            Assert.IsTrue(batch.HasCurrentCollectionAttachment(first, context));
            Assert.IsFalse(batch.HasCurrentCollectionAttachment(rejected, context));
            Assert.IsNull(batch.Collection(NeoValueOwnership.Session, bad.id));
            Assert.AreEqual(0, firstCallbacks);
            plan.Commit();
            batch.CompleteAttachments();
            Assert.AreEqual(1, firstCallbacks);
            Assert.AreEqual(0, rejectedCallbacks);
            Assert.IsFalse(client.sessionValues.ContainsKey(bad.id));
        }

        [Test]
        public void InsertionArgumentSourceFailureRestoresTrace()
        {
            using var client = ControlClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var body = new FunctionWithReturnType { instructions = Array.Empty<Instruction>(), source = new NeoScriptSourceInfo { name = "argument probe", uri = "Insertion.neo" } };
            context.trace.Push(body);
            context.trace.Position(new NeoScriptSourcePosition { line = 3, column = 4 });
            long before = context.trace.CurrentPosition;
            var pointer = new VariablePointer { variableId = "missing", source = new NeoScriptSourcePosition { line = 17, column = 9 } };
            var entry = new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true } };
            var error = Assert.Throws<NSGetterRuntimeError>(() => NSGetterEvaluator.EvaluateCollectionInsertionArgument(pointer, entry, new NeoScriptScope(), context));
            Assert.AreEqual(before, context.trace.CurrentPosition);
            Assert.IsNotNull(error!.NeoScriptFrames);
            StringAssert.Contains("17", error.ToString());
            StringAssert.Contains("Insertion.neo", error.ToString());
        }

        private static NeoClient ControlClient() => NeoTestSaveStack.ClientFromSchema(
            NeoTestExport.Read(File.ReadAllText(Root + "controls-export.json")), assumeCurrentSchema: false);
        private static object? Call(NeoClient client, string name, params object?[] args) =>
            NeoMemberNSFunction.GetOrCreateStatic(client, client.members.Values.Single(m => m is NSFunctionMember && m.name == name).id).InvokeStatic(args);
        [TestCase("RemoveListAliases", "Lists", 22)]
        [TestCase("RemoveMapAliases", "Maps", 22)]
        [TestCase("RemoveSetAliases", "Sets", 22)]
        [TestCase("RepeatedRemoval", "Lists", 2)]
        public void RemovedAliasesKeepOneFreshCopyAndReclaimUnescaped(string method, string key, int expected)
        {
            using var client = ControlClient();
            var holder = (INeoValueReference)Call(client, "MakeRetentionBox")!;
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, holder.valueId!, out ObjectMemberValue? root));
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, root!.value![key], out ArrayMemberValue? list));
            string retired = list!.value![0];
            int before = client.sessionValues.Count;
            Assert.AreEqual(expected, Call(client, method, holder));
            Assert.IsFalse(client.sessionValues.ContainsKey(retired), "The stored identity must retire.");
            if (method != "RepeatedRemoval")
                Assert.Less(client.sessionValues.Count, before, "Unescaped retained rows must be reclaimed.");
        }
        [Test]
        public void RemovedClassEscapesWithFreshIdentityAndPreservedContents()
        {
            using var client = ControlClient();
            var holder = (INeoValueReference)Call(client, "MakeRetentionBox")!;
            client.TryGetValue(NeoValueOwnership.Session, holder.valueId!, out ObjectMemberValue? root);
            client.TryGetValue(NeoValueOwnership.Session, root!.value!["Coins"], out ArrayMemberValue? list);
            string retired = list!.value![0];
            var returned = (INeoValueReference)Call(client, "RemoveAndReturnCoin", holder)!;
            Assert.AreNotEqual(retired, returned.valueId);
            Assert.IsFalse(client.sessionValues.ContainsKey(retired));
            Assert.IsTrue(client.sessionValues.ContainsKey(returned.valueId!));
            Assert.AreEqual(8, Call(client, "ReadCoin", returned));
        }
        [Test]
        public void PriorCachedAliasesAreReclaimedAfterRemoval()
        {
            using var client = ControlClient();
            var holder = (INeoValueReference)Call(client, "MakeRetentionBox")!;
            var ids = client.sessionValues.Keys.ToHashSet();
            Assert.AreEqual(0, Call(client, "RemoveWithoutAlias", holder));
            Assert.IsTrue(client.sessionValues.Keys.All(ids.Contains), "Prior cached aliases must not leave unescaped retained Session allocations.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DirectRemovalUsesOnlyLiveAliasesAndPublishesAfterCommit(bool aliasPresent)
        {
            using var client = ControlClient();
            var holder = (INeoValueReference)Call(client, "MakeRetentionBox")!;
            client.TryGetValue(NeoValueOwnership.Session, holder.valueId!, out ObjectMemberValue? root);
            client.TryGetValue(NeoValueOwnership.Session, root!.value!["Lists"], out ArrayMemberValue? outer);
            string id = outer!.value![0];
            client.TryGetValue(NeoValueOwnership.Session, id, out ArrayMemberValue? old);
            Assert.IsTrue(client.TryInferMemberForValueId(id, out Member? member));
            var context = new NSGetterEvaluator.Context(client, null, null);
            object? alias = aliasPresent ? NSGetterEvaluator.UnwrapRow(old!, context, NeoValueOwnership.Session, member) : null;
            var original = client.sessionValues.ToDictionary(pair => pair.Key, pair => pair.Value);
            var plan = new NeoWritePlan(client);
            var batch = new NeoWriteBatch(plan, held: false);
            var pending = batch.List(NeoValueOwnership.Session, outer.id, context, null);
            pending.Clear(NeoTimestamp.Now());
            batch.Release(NeoValueOwnership.Session, id, member, outer.id);
            batch.Prepare();
            CollectionAssert.AreEquivalent(original.Keys, client.sessionValues.Keys);
            foreach (var pair in original)
                Assert.AreSame(pair.Value, client.sessionValues[pair.Key]);
            if (aliasPresent)
                Assert.AreEqual(id, NSGetterEvaluator.FindRowReference(alias, context)!.valueId);
            plan.Commit();
            Assert.IsFalse(client.sessionValues.ContainsKey(id));
            if (aliasPresent)
            {
                string retained = NSGetterEvaluator.FindRowReference(alias, context)!.valueId;
                Assert.AreNotEqual(id, retained);
                Assert.IsTrue(client.sessionValues.ContainsKey(retained));
            }
            else
                Assert.IsTrue(client.sessionValues.Keys.All(original.ContainsKey));
            var reference = new NSSlotReference(id, null, NeoNSFunctionRuntime.TypeInfoFromBindingMember(client, member!, NeoGenericResolution.EmptyEnv, new HashSet<string>()), null);
            Assert.Catch(() => NSGetterEvaluator.ReadThroughReference(reference, context));
        }
        [Test]
        public void FailedPlanDoesNotPublishRetainedAliasesOrAllocations()
        {
            using var client = ControlClient();
            var holder = (INeoValueReference)Call(client, "MakeRetentionBox")!;
            client.TryGetValue(NeoValueOwnership.Session, holder.valueId!, out ObjectMemberValue? root);
            client.TryGetValue(NeoValueOwnership.Session, root!.value!["Lists"], out ArrayMemberValue? outer);
            string id = outer!.value![0];
            client.TryGetValue(NeoValueOwnership.Session, id, out ArrayMemberValue? old);
            Assert.IsTrue(client.TryInferMemberForValueId(id, out Member? member));
            var context = new NSGetterEvaluator.Context(client, null, null);
            object alias = NSGetterEvaluator.UnwrapRow(old!, context, NeoValueOwnership.Session, member)!;
            var plan = new NeoWritePlan(client);
            var batch = new NeoWriteBatch(plan, held: false);
            var pending = batch.List(NeoValueOwnership.Session, outer.id, context, null);
            pending.Clear(NeoTimestamp.Now());
            batch.Release(NeoValueOwnership.Session, id, member, outer.id);
            batch.Prepare();
            // A competing committed edit makes this prepared plan stale.
            var other = new NeoWritePlan(client);
            other.Set(NeoValueOwnership.Session, new NumberMemberValue { id = "later-write", value = 9 });
            other.Commit();
            var before = client.sessionValues.ToDictionary(pair => pair.Key, pair => pair.Value);
            Assert.Catch(() => plan.Commit());
            CollectionAssert.AreEquivalent(before.Keys, client.sessionValues.Keys);
            foreach (var pair in before)
                Assert.AreSame(pair.Value, client.sessionValues[pair.Key]);
            Assert.AreEqual(id, NSGetterEvaluator.FindRowReference(alias, context)!.valueId);
        }

        private static (NeoClient client, NSGetterEvaluator.Context context, string root, string child, string outer, Member member) RetainedGraph(string factory)
        {
            var client = ControlClient();
            var holder = (INeoValueReference)Call(client, factory)!;
            client.TryGetValue(NeoValueOwnership.Session, holder.valueId!, out ObjectMemberValue? root);
            client.TryGetValue(NeoValueOwnership.Session, root!.value!["Values"], out ArrayMemberValue? outer);
            client.TryGetValue(NeoValueOwnership.Session, outer!.value![0], out ArrayMemberValue? inner);
            Assert.IsTrue(client.TryInferMemberForValueId(inner!.id, out Member? member));
            var context = new NSGetterEvaluator.Context(client, null, null);
            object alias = NSGetterEvaluator.UnwrapRow(inner, context, NeoValueOwnership.Session, member!)!;
            object childAlias = NSGetterEvaluator.ResolveValueIfId(inner.value![0], context,
                NeoValueOwnership.Session, client.TryResolveCollectionEntryMember(member!, inner))!;
            var plan = new NeoWritePlan(client);
            var batch = new NeoWriteBatch(plan, false);
            batch.List(NeoValueOwnership.Session, outer.id, context, null).Clear(NeoTimestamp.Now());
            batch.Release(NeoValueOwnership.Session, inner.id, member, outer.id);
            batch.Prepare();
            Assert.IsTrue(client.sessionValues.ContainsKey(inner.id));
            plan.Commit();
            string retained = NSGetterEvaluator.FindRowReference(alias, context)!.valueId;
            string child = NSGetterEvaluator.FindRowReference(childAlias, context)!.valueId;
            Assert.AreNotEqual(inner.id, retained);
            Assert.IsFalse(client.sessionValues.ContainsKey(inner.id));
            Assert.IsTrue(client.TryFindOwnedParent(NeoValueOwnership.Session, child, out string? parent));
            Assert.AreEqual(retained, parent);
            return (client, context, retained, child, outer.id, member!);
        }

        [TestCase("MakeClassRetention")]
        [TestCase("MakeDeepRetention")]
        public void RetainedContractSurvivesFreshContextAndValidRowReplacement(string factory)
        {
            var graph = RetainedGraph(factory);
            using var client = graph.client;
            Assert.IsTrue(client.TryInferMemberForValueId(graph.root, out Member? inferred));
            Assert.AreSame(graph.member, inferred);
            client.TryGetValue(NeoValueOwnership.Session, graph.root, out ArrayMemberValue? old);
            var plan = new NeoWritePlan(client);
            plan.Set(NeoValueOwnership.Session, new ArrayMemberValue
            {
                id = graph.root,
                value = (string[])old!.value!.Clone(),
                genericBindings = old.genericBindings is null ? null : new(old.genericBindings),
            });
            plan.Commit();
            var fresh = new NSGetterEvaluator.Context(client, null, null);
            Assert.IsTrue(client.TryInferMemberForValueId(graph.root, out inferred));
            Assert.AreSame(graph.member, inferred);
            Assert.IsTrue(client.TryFindOwnedParent(NeoValueOwnership.Session, graph.child, out string? parent));
            Assert.AreEqual(graph.root, parent);
            var import = new NeoWritePlan(client);
            Assert.Throws<InvalidOperationException>(() => client.ImportValueReference(import,
                NeoValueOwnership.Session, graph.child, out bool _, null), "Owned children cannot become parentless in a later context.");
            GC.KeepAlive(fresh);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RetainedContractRejectsChangedSchemaOrBindings(bool bindings)
        {
            var graph = RetainedGraph("MakeClassRetention");
            using var client = graph.client;
            if (bindings)
            {
                client.TryGetValue(NeoValueOwnership.Session, graph.root, out ArrayMemberValue? old);
                var plan = new NeoWritePlan(client);
                plan.Set(NeoValueOwnership.Session, new ArrayMemberValue
                {
                    id = graph.root,
                    value = old!.value,
                    genericBindings = new() { ["different"] = "binding" }
                });
                plan.Commit();
            }
            else
                client.InvalidateSchemaResolutionCaches();
            Assert.Throws<InvalidOperationException>(() => client.TryInferMemberForValueId(graph.root, out _));
            Assert.Throws<InvalidOperationException>(() => client.TryFindOwnedParent(NeoValueOwnership.Session, graph.child, out _));
        }

        [Test]
        public void CurrentPlacementPrecedesRetainedContractAfterReattachment()
        {
            var graph = RetainedGraph("MakeClassRetention");
            using var client = graph.client;
            var plan = new NeoWritePlan(client);
            plan.Set(NeoValueOwnership.Session, new ArrayMemberValue { id = graph.outer, value = new[] { graph.root } });
            plan.Commit();
            client.InvalidateSchemaResolutionCaches();
            Assert.IsTrue(client.TryInferMemberForValueId(graph.root, out Member? inferred));
            Assert.IsInstanceOf<ListMember>(inferred);
            Assert.IsTrue(client.TryFindOwnedParent(NeoValueOwnership.Session, graph.child, out string? parent));
            Assert.AreEqual(graph.root, parent);
        }

        [Test]
        public void RetainedContractIsClearedOnClientDisposal()
        {
            var graph = RetainedGraph("MakeClassRetention");
            var node = graph.client.ExistingValueNode(graph.root)!;
            Assert.IsNotNull(node.retainedRoot);
            graph.client.Dispose();
            Assert.IsNull(node.retainedRoot);
        }

        [Test]
        public void RetainedContractDoesNotCrossOwnershipAndRetiresWithSessionRow()
        {
            var graph = RetainedGraph("MakeClassRetention");
            using var client = graph.client;
            Assert.IsFalse(client.TryInferMemberForValueId(graph.root, out _, NeoValueOwnership.Save));
            var node = client.ExistingValueNode(graph.root)!;
            var plan = new NeoWritePlan(client);
            plan.Remove(NeoValueOwnership.Session, graph.root);
            plan.Commit();
            Assert.IsNull(node.retainedRoot);
            var replace = new NeoWritePlan(client);
            replace.Set(NeoValueOwnership.Session, new ArrayMemberValue { id = graph.root, value = new[] { graph.child } });
            replace.Commit();
            Assert.IsFalse(client.TryInferMemberForValueId(graph.root, out _));
        }

        [TestCaseSource(nameof(Cases))]
        public void Acceptance(string path, string name, object expected, bool refusal)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(path)), assumeCurrentSchema: false);
            var member = client.members.Values.Single(m => m is NSFunctionMember && m.name == name);
            object? Invoke() => NeoMemberNSFunction.GetOrCreateStatic(client, member.id).InvokeStatic(Array.Empty<object?>());
            if (refusal)
                StringAssert.Contains((string)expected, Assert.Catch(() => Invoke()).ToString());
            else
                Assert.AreEqual(expected, Invoke());
        }
    }
}
