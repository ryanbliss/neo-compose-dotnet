// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class NeoOwnedCollectionInsertionTests
    {
        private static FunctionWithReturnType EmptyBody() => new()
        {
            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
            parameters = Array.Empty<Variable>(),
            instructions = Array.Empty<Instruction>(),
            typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
        };

        private static (string outer, string inner, Member member, object alias) HeldRemoval(
            NeoClient client, NSGetterEvaluator.Context context, string key = "Lists")
        {
            var holder = (INeoValueReference)Call(client, "MakeRetentionBox")!;
            client.TryGetValue(NeoValueOwnership.Session, holder.valueId!, out ObjectMemberValue? root);
            client.TryGetValue(NeoValueOwnership.Session, root!.value![key], out ArrayMemberValue? outer);
            client.TryGetValue(NeoValueOwnership.Session, outer!.value![0], out MemberValue? inner);
            Assert.IsTrue(client.TryInferMemberForValueId(inner!.id, out Member? member));
            object alias = NSGetterEvaluator.UnwrapRow(inner, context, NeoValueOwnership.Session, member)!;
            return (outer.id, inner.id, member!, alias);
        }

        private static NeoWriteBatch StageRemoval(NeoClient client, NSGetterEvaluator.Context context,
            (string outer, string inner, Member member, object alias) graph)
        {
            var batch = client.ScriptWriteBatch(graph.outer)!;
            batch.Stage(b =>
            {
                b.List(NeoValueOwnership.Session, graph.outer, context, null).Clear(NeoTimestamp.Now());
                b.Release(NeoValueOwnership.Session, graph.inner, graph.member, graph.outer);
            });
            return batch;
        }

        private static void FinishHeldFrame(NeoClient client, NSGetterEvaluator.Context context, object? result)
        {
            context.allocationTracker.EnterExecution();
            client.EnterScriptWrites();
            try
            {
                var terminal = NeoScriptExecutionResult.Completed(true, result);
                context.allocationTracker.ExitExecution(client, context, in terminal);
            }
            finally { client.ExitScriptWrites(); }
        }

        [TestCase(16)]
        [TestCase(32)]
        [TestCase(64)]
        [TestCase(128)]
        public void EmptyExecutionsPreserveOuterBatchAndPublishOneFinalPrefix(int count)
        {
            using var client = ControlClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var graph = HeldRemoval(client, context);
            int publications = 0, entries = 0;
            client.OnWritableValuesPublished += (_, plan) =>
            {
                if (plan.Rows.TryGetValue((NeoValueOwnership.Session, graph.inner), out var value)
                    && value is ArrayMemberValue row)
                {
                    publications++;
                    entries += row.value!.Length;
                }
            };
            client.EnterScriptWrites();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var batch = client.ScriptWriteBatch(graph.inner)!;
                    batch.Stage(b =>
                    {
                        var list = b.List(NeoValueOwnership.Session, graph.inner, context, null);
                        var child = new NumberMemberValue { id = Guid.NewGuid().ToString(), value = i };
                        b.Plan.Set(NeoValueOwnership.Session, child);
                        list.Insert(list.Count, child.id, NeoTimestamp.Now());
                        b.Gain(NeoValueOwnership.Session, child.id, graph.inner);
                    });
                    NeoScriptExecutor.Execute(client, EmptyBody(), new Dictionary<string, object?>(), context);
                    Assert.AreSame(batch, client.PendingScriptWrites);
                }
                Assert.AreEqual(0, publications);
            }
            finally { client.ExitScriptWrites(); }
            Assert.AreEqual(1, publications);
            Assert.AreEqual(count + 1, entries);
            Assert.IsFalse(context.allocationTracker.HasDeferredCompletion);
        }

        [TestCase("Lists", false)]
        [TestCase("Maps", false)]
        [TestCase("Sets", false)]
        [TestCase("Lists", true)]
        [TestCase("Maps", true)]
        [TestCase("Sets", true)]
        public void OuterCommitFinalizesRetainedRootsAfterFrameReturns(string key, bool escapes)
        {
            using var client = ControlClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var graph = HeldRemoval(client, context, key);
            var original = client.sessionValues.Keys.ToHashSet();
            client.EnterScriptWrites();
            try
            {
                var batch = StageRemoval(client, context, graph);
                FinishHeldFrame(client, context, escapes ? graph.alias : null);
                Assert.AreSame(batch, client.PendingScriptWrites);
                Assert.IsTrue(client.sessionValues.ContainsKey(graph.inner));
                // Reusing this tracker must not erase earlier return evidence.
                FinishHeldFrame(client, context, null);
                Assert.IsTrue(context.allocationTracker.HasDeferredCompletion);
            }
            finally { client.ExitScriptWrites(); }
            Assert.IsFalse(context.allocationTracker.HasDeferredCompletion);
            Assert.IsFalse(client.sessionValues.ContainsKey(graph.inner));
            if (escapes)
            {
                var retained = NSGetterEvaluator.FindRowReference(graph.alias, context)!;
                Assert.AreNotEqual(graph.inner, retained.valueId);
                Assert.IsTrue(client.sessionValues.ContainsKey(retained.valueId));
                Assert.IsTrue(client.TryInferMemberForValueId(retained.valueId, out Member? _));
            }
            else
                Assert.IsTrue(client.sessionValues.Keys.All(original.Contains));
        }

        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [TestCase(false, 3)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        [TestCase(true, 3)]
        public void DeferredCallersPreserveEachOthersRetainedReturns(bool sharedCache, int returnedCaller) =>
            AssertDeferredCallers(sharedCache, returnedCaller, false, false);

        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public void DeferredCallerEscapeSurvivesActiveOwnerAndPublicationFailure(
            bool sharedCache, bool active, bool fails) =>
            AssertDeferredCallers(sharedCache, 1, active, fails);

        private static void AssertDeferredCallers(bool sharedCache, int returnedCaller, bool active, bool fails)
        {
            using var client = ControlClient();
            var first = sharedCache ? client.CreateGetterContext(NeoValueOwnership.Session)
                : new NSGetterEvaluator.Context(client, null, null);
            var second = sharedCache ? client.CreateGetterContext(NeoValueOwnership.Session)
                : new NSGetterEvaluator.Context(client, null, null);
            var graph = HeldRemoval(client, first);
            client.TryGetValue(NeoValueOwnership.Session, graph.inner, out MemberValue? inner);
            object secondAlias = NSGetterEvaluator.UnwrapRow(inner!, second, NeoValueOwnership.Session, graph.member)!;
            var before = client.sessionValues.Keys.ToHashSet();
            string? retainedId = null;
            var primary = new InvalidOperationException("cross-caller published failure");
            client.EnterScriptWrites();
            try
            {
                var batch = client.ScriptWriteBatch(graph.outer)!;
                batch.Stage(b => b.List(NeoValueOwnership.Session, graph.outer, first, null).Changes(NeoTimestamp.Now()));
                object? firstReturn = returnedCaller == 1 ? graph.alias : returnedCaller == 3
                    ? new LocalList(Array.Empty<object?>(), new object?[] { graph.alias }, null, true) : null;
                FinishHeldFrame(client, first, firstReturn);
                Assert.IsTrue(first.allocationTracker.HasDeferredCompletion);
                Assert.AreEqual(graph.inner, NSGetterEvaluator.FindRowReference(graph.alias, first)!.valueId);
                StageRemoval(client, second, graph);
                FinishHeldFrame(client, second, returnedCaller is 2 or 3 ? secondAlias : null);
                // Reuse must preserve each caller's earlier return evidence.
                FinishHeldFrame(client, first, null);
                FinishHeldFrame(client, second, null);
                if (active)
                {
                    first.allocationTracker.EnterExecution();
                    second.allocationTracker.EnterExecution();
                }
                if (fails)
                    client.OnWritableValuesPublished += (_, plan) =>
                    {
                        if (ReferenceEquals(plan, batch.Plan))
                            throw primary;
                    };
                batch.TrackAttachment(new object(), () =>
                {
                    retainedId = NSGetterEvaluator.FindRowReference(graph.alias, first)!.valueId;
                    Assert.AreEqual(retainedId, NSGetterEvaluator.FindRowReference(secondAlias, second)!.valueId);
                    Assert.AreNotEqual(graph.inner, retainedId);
                    Assert.IsTrue(client.sessionValues.ContainsKey(retainedId));
                });
            }
            finally
            {
                if (fails)
                    Assert.AreSame(primary, Assert.Throws<InvalidOperationException>(() => client.ExitScriptWrites()));
                else
                    client.ExitScriptWrites();
            }
            if (active)
            {
                Assert.IsTrue(client.sessionValues.ContainsKey(retainedId!));
                first.allocationTracker.ExitExecution(client, first, default);
                second.allocationTracker.ExitExecution(client, second, default);
            }
            Assert.IsFalse(first.allocationTracker.HasDeferredCompletion);
            Assert.IsFalse(second.allocationTracker.HasDeferredCompletion);
            Assert.IsNotNull(retainedId);
            Assert.IsFalse(client.sessionValues.ContainsKey(graph.inner));
            Assert.AreEqual(returnedCaller != 0, client.sessionValues.ContainsKey(retainedId!));
            if (returnedCaller == 0)
                Assert.IsTrue(client.sessionValues.Keys.All(before.Contains));
            else
            {
                Assert.AreEqual(retainedId, NSGetterEvaluator.FindRowReference(graph.alias, first)!.valueId);
                Assert.AreEqual(retainedId, NSGetterEvaluator.FindRowReference(secondAlias, second)!.valueId);
            }
        }

        private sealed class ThrowingReturn : System.Collections.IEnumerable
        {
            private readonly Exception error;
            internal ThrowingReturn(Exception? error = null) => this.error = error ?? new InvalidOperationException("finalizer escape failure");
            public System.Collections.IEnumerator GetEnumerator() => throw error;
        }

        [Test]
        public void FailedReturnTraversalStillPreservesLaterReturnsAndReleasesAllTrackers()
        {
            using var client = ControlClient();
            var first = client.CreateGetterContext(NeoValueOwnership.Session);
            var second = client.CreateGetterContext(NeoValueOwnership.Session);
            var graph = HeldRemoval(client, first);
            var failure = new InvalidOperationException("return traversal");
            client.EnterScriptWrites();
            var batch = client.ScriptWriteBatch(graph.outer)!;
            batch.Stage(b => b.List(NeoValueOwnership.Session, graph.outer, first, null).Changes(NeoTimestamp.Now()));
            FinishHeldFrame(client, first, new ThrowingReturn(failure));
            FinishHeldFrame(client, first, graph.alias);
            StageRemoval(client, second, graph);
            FinishHeldFrame(client, second, null);
            Assert.AreSame(failure, Assert.Throws<InvalidOperationException>(() => client.ExitScriptWrites()));
            var reference = NSGetterEvaluator.FindRowReference(graph.alias, first)!;
            Assert.AreNotEqual(graph.inner, reference.valueId);
            Assert.IsTrue(client.sessionValues.ContainsKey(reference.valueId));
            Assert.IsFalse(first.allocationTracker.HasDeferredCompletion);
            Assert.IsFalse(second.allocationTracker.HasDeferredCompletion);
        }

        [TestCase(false, 16)]
        [TestCase(false, 32)]
        [TestCase(false, 64)]
        [TestCase(false, 128)]
        [TestCase(true, 16)]
        [TestCase(true, 32)]
        [TestCase(true, 64)]
        [TestCase(true, 128)]
        public void ManyDeferredCallersShareOneRetainedGraphAndOnePublication(bool sharedCache, int count)
        {
            using var client = ControlClient();
            var contexts = Enumerable.Range(0, count).Select(_ => sharedCache
                ? client.CreateGetterContext(NeoValueOwnership.Session)
                : new NSGetterEvaluator.Context(client, null, null)).ToArray();
            var graph = HeldRemoval(client, contexts[0]);
            client.TryGetValue(NeoValueOwnership.Session, graph.inner, out MemberValue? inner);
            var aliases = contexts.Select(context =>
                NSGetterEvaluator.UnwrapRow(inner!, context, NeoValueOwnership.Session, graph.member)!).ToArray();
            int publications = 0;
            client.OnWritableValuesPublished += (_, _) => publications++;
            client.EnterScriptWrites();
            try
            {
                var batch = client.ScriptWriteBatch(graph.outer)!;
                for (int index = 0; index < count; index++)
                {
                    var context = contexts[index];
                    batch.Stage(b => b.List(NeoValueOwnership.Session, graph.outer, context, null).Changes(NeoTimestamp.Now()));
                    FinishHeldFrame(client, context, aliases[index]);
                }
                StageRemoval(client, contexts[count - 1], graph);
            }
            finally { client.ExitScriptWrites(); }
            Assert.AreEqual(1, publications);
            string retainedId = NSGetterEvaluator.FindRowReference(aliases[0], contexts[0])!.valueId;
            Assert.AreNotEqual(graph.inner, retainedId);
            Assert.IsTrue(client.sessionValues.ContainsKey(retainedId));
            for (int index = 0; index < count; index++)
            {
                Assert.IsFalse(contexts[index].allocationTracker.HasDeferredCompletion);
                Assert.AreEqual(retainedId, NSGetterEvaluator.FindRowReference(aliases[index], contexts[index])!.valueId);
            }
        }

        [TestCase(16)]
        [TestCase(64)]
        public void SeparatePublicRemovalsPreserveOtherCachedGraphs(int count)
        {
            using var client = ControlClient();
            var holders = new List<INeoValueReference>();
            var retired = new List<string>();
            for (int index = 0; index < count; index++)
            {
                var holder = (INeoValueReference)Call(client, "MakeRetentionBox")!;
                holders.Add(holder);
                Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, holder.valueId!, out ObjectMemberValue? root));
                Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, root!.value!["Lists"], out ArrayMemberValue? lists));
                retired.Add(lists!.value![0]);
            }
            for (int index = 0; index < count; index++)
            {
                Assert.AreEqual(22, Call(client, "RemoveListAliases", holders[index]));
                Assert.IsFalse(client.sessionValues.ContainsKey(retired[index]));
                if (index + 1 < count)
                    Assert.IsTrue(client.sessionValues.ContainsKey(retired[index + 1]));
            }
            GC.KeepAlive(holders);
        }

        [TestCase(false, 16, false)]
        [TestCase(false, 32, false)]
        [TestCase(false, 64, false)]
        [TestCase(false, 128, false)]
        [TestCase(true, 16, false)]
        [TestCase(true, 32, false)]
        [TestCase(true, 64, false)]
        [TestCase(true, 128, false)]
        [TestCase(false, 32, true)]
        [TestCase(true, 32, true)]
        public void DistinctGraphsAndCachesRetainOnlyTheirAssociatedRoots(bool sharedCache, int count, bool unrelated)
        {
            using var client = ControlClient();
            var graphs = new List<(NSGetterEvaluator.Context context,
                (string outer, string inner, Member member, object alias) graph)>();
            var kept = new List<object>();
            for (int index = 0; index < count; index++)
            {
                var context = sharedCache ? client.CreateGetterContext(NeoValueOwnership.Session)
                    : new NSGetterEvaluator.Context(client, null, null);
                graphs.Add((context, HeldRemoval(client, context)));
                if (unrelated)
                    for (int extra = 0; extra < 4; extra++)
                        kept.Add(HeldRemoval(client, context).alias);
            }
            int publications = 0;
            client.OnWritableValuesPublished += (_, _) => publications++;
            client.EnterScriptWrites();
            try
            {
                foreach (var item in graphs)
                {
                    StageRemoval(client, item.context, item.graph);
                    FinishHeldFrame(client, item.context, item.graph.alias);
                }
            }
            finally { client.ExitScriptWrites(); }
            Assert.AreEqual(1, publications);
            var fresh = new HashSet<string>();
            foreach (var item in graphs)
            {
                var reference = NSGetterEvaluator.FindRowReference(item.graph.alias, item.context)!;
                Assert.AreNotEqual(item.graph.inner, reference.valueId);
                Assert.IsTrue(fresh.Add(reference.valueId));
                Assert.IsTrue(client.sessionValues.ContainsKey(reference.valueId));
                Assert.IsFalse(item.context.allocationTracker.HasDeferredCompletion);
            }
            GC.KeepAlive(kept);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ParentAndDescendantAliasesAcrossCachesShareOneRetainedGraph(bool descendantFirst)
        {
            using var client = ControlClient();
            var type = client.classes.Values.Single(value => value.name == "Deep");
            var instance = NeoGeneratedTypesSupport.CreateWritableClassValue(client, type.id,
                new NeoGeneratedConstructorValue("Values", type.schema["Values"],
                    new object?[] { new object?[] { new object?[] { 3d } } }));
            client.TryGetValue(NeoValueOwnership.Session, instance.value!.value!["Values"], out ArrayMemberValue? outer);
            client.TryGetValue(NeoValueOwnership.Session, outer!.value![0], out ArrayMemberValue? middle);
            client.TryGetValue(NeoValueOwnership.Session, middle!.value![0], out ArrayMemberValue? leaf);
            Assert.IsTrue(client.TryInferMemberForValueId(middle.id, out Member? middleMember));
            Assert.IsTrue(client.TryInferMemberForValueId(leaf!.id, out Member? leafMember));
            var parentContext = new NSGetterEvaluator.Context(client, null, null);
            var childContext = new NSGetterEvaluator.Context(client, null, null);
            object parentAlias = NSGetterEvaluator.UnwrapRow(middle, parentContext, NeoValueOwnership.Session, middleMember!)!;
            object childAlias = NSGetterEvaluator.UnwrapRow(leaf, childContext, NeoValueOwnership.Session, leafMember!)!;
            client.EnterScriptWrites();
            try
            {
                var batch = client.ScriptWriteBatch(outer.id)!;
                foreach (var context in descendantFirst ? new[] { childContext, parentContext } : new[] { parentContext, childContext })
                {
                    batch.Stage(b => b.List(NeoValueOwnership.Session, outer.id, context, null).Changes(NeoTimestamp.Now()));
                    FinishHeldFrame(client, context, ReferenceEquals(context, parentContext) ? parentAlias : childAlias);
                }
                batch.Stage(b =>
                {
                    b.List(NeoValueOwnership.Session, outer.id, parentContext, null).Clear(NeoTimestamp.Now());
                    b.Release(NeoValueOwnership.Session, middle.id, middleMember, outer.id);
                });
            }
            finally { client.ExitScriptWrites(); }
            string freshParent = NSGetterEvaluator.FindRowReference(parentAlias, parentContext)!.valueId;
            string freshChild = NSGetterEvaluator.FindRowReference(childAlias, childContext)!.valueId;
            Assert.AreNotEqual(middle.id, freshParent);
            Assert.AreNotEqual(leaf.id, freshChild);
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, freshParent, out ArrayMemberValue? retained));
            Assert.AreEqual(freshChild, retained!.value![0]);
            Assert.IsTrue(client.sessionValues.ContainsKey(freshChild));
        }

        [Test]
        public void ReadForcedCommitDoesNotReclaimActiveTrackerRoots()
        {
            using var client = ControlClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var graph = HeldRemoval(client, context);
            var original = client.sessionValues.Keys.ToHashSet();
            client.EnterScriptWrites();
            try
            {
                StageRemoval(client, context, graph);
                FinishHeldFrame(client, context, null);
                context.allocationTracker.EnterExecution();
                client.CommitScriptWrites();
                string retained = NSGetterEvaluator.FindRowReference(graph.alias, context)!.valueId;
                Assert.IsTrue(client.sessionValues.ContainsKey(retained));
                context.allocationTracker.ExitExecution(client, context, default);
                Assert.IsFalse(client.sessionValues.ContainsKey(retained));
            }
            finally { client.ExitScriptWrites(); }
            Assert.IsTrue(client.sessionValues.Keys.All(original.Contains));
        }

        [Test]
        public void EarlyPublicationFailureStillRemapsReturnsAndPreservesPrimaryError()
        {
            using var client = ControlClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var graph = HeldRemoval(client, context);
            var primary = new InvalidOperationException("published event failure");
            client.EnterScriptWrites();
            var batch = StageRemoval(client, context, graph);
            FinishHeldFrame(client, context, graph.alias);
            client.OnWritableValuesPublished += (_, plan) =>
            {
                if (ReferenceEquals(plan, batch.Plan))
                    throw primary;
            };
            Assert.AreSame(primary, Assert.Throws<InvalidOperationException>(() => client.ExitScriptWrites()));
            Assert.IsTrue(batch.Plan.Installed);
            Assert.IsFalse(context.allocationTracker.HasDeferredCompletion);
            string retained = NSGetterEvaluator.FindRowReference(graph.alias, context)!.valueId;
            Assert.AreNotEqual(graph.inner, retained);
            Assert.IsTrue(client.sessionValues.ContainsKey(retained));
            Assert.IsFalse(client.sessionValues.ContainsKey(graph.inner));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RequiredCompletionDrainsOnceAndCannotBeOvertaken(bool earlierFailure)
        {
            using var client = ControlClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var plan = new NeoWritePlan(client);
            var batch = new NeoWriteBatch(plan, false);
            var order = new List<int>();
            var first = new InvalidOperationException("earlier event");
            var remap = new InvalidOperationException("remap");
            var attachment = new InvalidOperationException("attachment");
            batch.AfterPublication(() =>
            {
                order.Add(1);
                batch.FinishCommit(null, true);
                CollectionAssert.AreEqual(new[] { 1 }, order);
                throw remap;
            });
            batch.AfterPublication(() => order.Add(2));
            batch.TrackAttachment(new object(), () => { order.Add(3); throw attachment; });
            batch.TrackAttachment(new object(), () => order.Add(4));
            context.allocationTracker.DeferCompletion(batch, context, default);
            Assert.AreSame(earlierFailure ? first : remap,
                Assert.Throws<InvalidOperationException>(() => batch.FinishCommit(earlierFailure ? first : null, true)));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, order);
            Assert.IsFalse(context.allocationTracker.HasDeferredCompletion);
            batch.FinishCommit(null, true);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, order);
        }

        [Test]
        public void FailedPreparedPlanDiscardsDeferredPublicationWithoutLosingOriginalRows()
        {
            using var client = ControlClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var graph = HeldRemoval(client, context);
            var plan = new NeoWritePlan(client);
            var batch = new NeoWriteBatch(plan, false);
            batch.List(NeoValueOwnership.Session, graph.outer, context, null).Clear(NeoTimestamp.Now());
            batch.Release(NeoValueOwnership.Session, graph.inner, graph.member, graph.outer);
            context.allocationTracker.DeferCompletion(batch, context, default);
            batch.Prepare();
            var competing = new NeoWritePlan(client);
            competing.Set(NeoValueOwnership.Session, new NumberMemberValue { id = "competing", value = 9 });
            competing.Commit();
            var original = client.sessionValues.ToDictionary(x => x.Key, x => x.Value);
            var failure = Assert.Catch(() => plan.Commit())!;
            Assert.IsFalse(plan.Installed);
            Assert.AreSame(failure, Assert.Catch(() => batch.FinishCommit(failure, false)));
            Assert.IsFalse(context.allocationTracker.HasDeferredCompletion);
            CollectionAssert.AreEquivalent(original.Keys, client.sessionValues.Keys);
            foreach (var row in original)
                Assert.AreSame(row.Value, client.sessionValues[row.Key]);
            Assert.AreEqual(graph.inner, NSGetterEvaluator.FindRowReference(graph.alias, context)!.valueId);
            string target = ((ArrayMemberValue)client.sessionValues[graph.inner]).value![0];
            var reference = new NSSlotReference(target, null, new PrimitiveTypeInfo { type = MemberKind.Int, required = true }, null);
            Assert.AreEqual(3, NSGetterEvaluator.ReadThroughReference(reference, context));
        }

        [Test]
        public void PartialRowsCannotFinalizeAnOuterHeldRemoval()
        {
            using var client = ControlClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var graph = HeldRemoval(client, context);
            client.EnterScriptWrites();
            try
            {
                var batch = StageRemoval(client, context, graph);
                batch.Plan.Set(NeoValueOwnership.Session, new NumberMemberValue { id = "partial-only", value = 3 });
                FinishHeldFrame(client, context, graph.alias);
                client.CommitScriptRows();
                Assert.AreSame(batch, client.PendingScriptWrites);
                Assert.IsTrue(context.allocationTracker.HasDeferredCompletion);
                Assert.IsTrue(client.sessionValues.ContainsKey(graph.inner));
                Assert.IsTrue(client.sessionValues.ContainsKey("partial-only"));
            }
            finally { client.ExitScriptWrites(); }
            Assert.IsFalse(context.allocationTracker.HasDeferredCompletion);
            Assert.IsFalse(client.sessionValues.ContainsKey(graph.inner));
            Assert.IsTrue(client.sessionValues.ContainsKey(NSGetterEvaluator.FindRowReference(graph.alias, context)!.valueId));
        }

        [Test]
        public void OldCompletionDoesNotClearAReentrantNewBatch()
        {
            using var client = ControlClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var first = HeldRemoval(client, context);
            var second = HeldRemoval(client, context);
            client.EnterScriptWrites();
            try
            {
                var oldBatch = StageRemoval(client, context, first);
                FinishHeldFrame(client, context, first.alias);
                NeoWriteBatch? newer = null;
                oldBatch.AfterPublication(() =>
                {
                    newer = StageRemoval(client, context, second);
                    FinishHeldFrame(client, context, second.alias);
                });
                client.CommitScriptWrites();
                Assert.AreSame(newer, client.PendingScriptWrites);
                Assert.IsTrue(context.allocationTracker.HasDeferredCompletion);
                Assert.IsFalse(client.sessionValues.ContainsKey(first.inner));
                Assert.IsTrue(client.sessionValues.ContainsKey(second.inner));
            }
            finally { client.ExitScriptWrites(); }
            Assert.IsFalse(context.allocationTracker.HasDeferredCompletion);
            foreach (object alias in new[] { first.alias, second.alias })
                Assert.IsTrue(client.sessionValues.ContainsKey(NSGetterEvaluator.FindRowReference(alias, context)!.valueId));
        }

        [Test]
        public void FinalizerFailureReleasesEveryDeferredTracker()
        {
            using var client = ControlClient();
            var first = new NSGetterEvaluator.Context(client, null, null);
            var second = new NSGetterEvaluator.Context(client, null, null);
            var batch = new NeoWriteBatch(new NeoWritePlan(client), false);
            // Deliberately malformed terminal enumeration exercises failure draining,
            // not a supported public return contract or a new escape policy.
            first.allocationTracker.RegisterSessionRoot("not-published");
            var result = NeoScriptExecutionResult.Completed(true, new ThrowingReturn());
            first.allocationTracker.DeferCompletion(batch, first, in result);
            second.allocationTracker.DeferCompletion(batch, second, default);
            StringAssert.Contains("finalizer escape failure", Assert.Throws<InvalidOperationException>(() => batch.FinishCommit(null, true))!.Message);
            Assert.IsFalse(first.allocationTracker.HasDeferredCompletion);
            Assert.IsFalse(second.allocationTracker.HasDeferredCompletion);
        }
    }
}
