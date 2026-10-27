// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoScriptSequenceTests
    {
        private NeoClient client = null!;
        private NSGetterEvaluator.Context context = null!;
        private JObject bodies = null!;

        [SetUp]
        public void Setup()
        {
            client = NeoTestSaveStack.LoadClient(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/synth-example.json"));
            context = new NSGetterEvaluator.Context(client, null, null);
            bodies = (JObject)JObject.Parse(NeoScriptSequenceParityFixture.Json)["bodies"]!;
        }

        [TearDown]
        public void Teardown() => client.Dispose();

        private object? Run(string name, params object?[] arguments) =>
            NSGetterEvaluator.Evaluate(bodies[name]!.ToObject<FunctionWithReturnType>()!, context, arguments);

        [Test]
        public void EnumerableQueriesPreserveListAndDictionaryCalls()
        {
            object? left = Run("Powers", 3d);
            object? right = Run("Powers", 2d);
            Assert.That(Run("QueryZip", left, right), Is.EqualTo(new[] { 2d, 4d }));
            Assert.That(Run("QueryConcat", left, right), Is.EqualTo(new[] { 1d, 2d, 4d, 1d, 2d }));
            Assert.That(Run("QueryFlatten", new object[] { new[] { left, right } }), Is.EqualTo(new[] { 1d, 2d, 4d, 1d, 2d }));
            Assert.That(Run("QueryCount", left), Is.EqualTo(3d));
            Assert.That(Run("QueryOrdered", left), Is.EqualTo(new[] { 4d, 2d, 1d }));
            Assert.That(Run("QueryList", new object[] { new object[] { 1d, 2d, 4d } }), Is.EqualTo(6d));
            var dictionary = new Dictionary<string, object?> { ["a"] = 3d, ["b"] = 7d };
            Assert.That(Run("QueryDictionary", dictionary), Is.EqualTo(3d));
            Assert.That(Run("QueryDictionarySequence", dictionary), Is.EqualTo(10d));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void QueryMutationSnapshotsOldAndNewIr(bool oldQueryIr)
        {
            var body = (JObject)bodies["QueryMutation"]!.DeepClone();
            if (oldQueryIr)
                foreach (var pointer in body.Descendants().OfType<JObject>()
                    .Where(value => (string?)value["operation"] == "queryEnumerable").ToArray())
                    pointer.Replace(pointer["pointer"]!.DeepClone());
            Assert.That(NSGetterEvaluator.Evaluate(body.ToObject<FunctionWithReturnType>()!, context),
                Is.EqualTo(new[] { 1d, 2d, 3d }));
        }

        [Test]
        public void QueryShortCircuitDisposesEnteredGenerators()
        {
            Assert.That(Run("QueryAny", Run("QueryShortCircuit")), Is.True);
            Assert.That(Run("QueryFirst", Run("QueryShortCircuit")), Is.EqualTo(1d));
            Assert.That(() => Run("QueryAny", Run("QueryCleanup")), Throws.Exception.With.Message.Contains("query cleanup"));
            Assert.That(() => Run("QueryZip", Run("QueryCleanup"), Array.Empty<object>()), Throws.Exception.With.Message.Contains("query cleanup"));
        }

        [Test]
        public void CollectionTypesSurviveInterfaceGenericAndTypeTestBoundaries()
        {
            Assert.That(Run("EmptyAliases"), Is.True);
            Assert.That(Run("Inspect", new object[] { Array.Empty<object>() }), Is.True);
            Assert.That(Run("Inspect", new object[] { new object[] { 1d, 2d } }), Is.True);
            context.BindFunctionTypeArguments(new TypeInfo[] { new CollectionTypeInfo
            {
                type = MemberKind.List, required = true, entryTypeInfo = NeoSequenceContract.Int
            } });
            Assert.That(Run("InspectGeneric", new object[] { Array.Empty<object>() }), Is.True);
            context.BindFunctionTypeArguments(null);
            Assert.That(Run("InspectNested", new object[] { new object[] { Array.Empty<object>(), new object[] { 1d } } }), Is.EqualTo(2d));
        }

        [TestCase("InspectInner")]
        [TestCase("InspectTuple")]
        [TestCase("InspectSwitch")]
        public void GenericOperandTypesCloseAtTypeTests(string name)
        {
            var list = new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = NeoSequenceContract.Int };
            context.BindFunctionTypeArguments(new TypeInfo[] { list });
            Assert.That(Run(name, new object[] { new object[] { new object[0] } }), Is.True);
            context = new NSGetterEvaluator.Context(client, null, null);
            context.BindFunctionTypeArguments(new TypeInfo[] { NeoSequenceContract.Int });
            Assert.That(Run(name, new object[] { new object[] { 1d } }), Is.False);
            list = new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true } };
            context = new NSGetterEvaluator.Context(client, null, null);
            context.BindFunctionTypeArguments(new TypeInfo[] { list });
            Assert.That(Run(name, new object[] { new object[] { new object[0] } }), Is.False);
        }

        [Test]
        public void NonSequenceCapabilityGuardDoesNotResolveGenericBindings()
        {
            Assert.That(Run("CapabilityGuard", new object[] { new object[] { new Dictionary<string, object?>() } }), Is.False);
        }

        [Test]
        public void WideningRetainsCollectionsThroughMergesTuplesAndLocalIndexWrites()
        {
            Assert.That(Run("WidenedMerges", false, 0d), Is.True);
            Assert.That(Run("WidenedMerges", true, 1d), Is.True);
            Assert.That(Run("WidenedTuple"), Is.True);
            Assert.That(Run("WidenedAlias"), Is.True);
            Assert.That(Run("LocalSequenceSlots"), Is.True);
            Assert.That(Run("Sum", Run("WidenedReturn", null, new object[] { 1d, 2d })), Is.EqualTo(3d));
        }

        [Test]
        public void TypedLoopAndCallbackBindingsRetainSequenceTypes()
        {
            Assert.That(Run("TypedBindings"), Is.True);
            Assert.That(Run("TypedSequenceLoop", Run("ListGenerator")), Is.True);
            Assert.That(Run("Sum", Run("TypedYield", Run("ListGenerator"))), Is.EqualTo(2d));
            context = new NSGetterEvaluator.Context(client, null, null);
            context.BindFunctionTypeArguments(new TypeInfo[] { new CollectionTypeInfo
            {
                type = MemberKind.List, required = true, entryTypeInfo = NeoSequenceContract.Int
            } });
            Assert.That(Run("ConstraintSum", new object[] { new object[] { 1d, 2d } }), Is.EqualTo(3d));
        }

        [Test]
        public void GeneratorKeepsLoopStateAndCanBeEnumeratedIndependently()
        {
            object? values = Run("Powers", 4d);
            Assert.That(Run("Sum", values), Is.EqualTo(15d));
            Assert.That(Run("Sum", values), Is.EqualTo(15d));
            Assert.That(Run("Collect", Run("Flow")), Is.EqualTo(new object[] { 1d, 30d, 4d }));
        }

        [Test]
        public void CleanupRunsOnceForEnteredRegionsAndNeverForUnstartedCursor()
        {
            Assert.That(Run("Unstarted", Run("Cleanup")), Is.EqualTo(7d));
            Assert.That(() => Run("Started", Run("Cleanup")), Throws.Exception.With.Message.Contains("cleanup"));
        }

        [Test]
        public void CleanupErrorHasTheGeneratorsOwnSourceFrame()
        {
            var error = Assert.Catch(() => Run("Started", Run("Cleanup")));
            var frames = (IReadOnlyList<NeoScriptStackFrame>)error!.Data["neoScriptFrames"]!;
            Assert.That(frames.Any(frame => frame.Name == (string)bodies["Cleanup"]!["source"]!["name"]!), Is.True);
        }

        [Test]
        public void CurrentAndRemainingMaterializationShareCursorPosition()
        {
            object? sequence = Run("Powers", 3d);
            var cursor = (NSGetterEvaluator.ScriptCursor)Run("Cursor", sequence)!;
            Assert.That(() => cursor.Current(context), Throws.TypeOf<NSGetterRuntimeError>());
            Assert.That(cursor.MoveNext(context), Is.True);
            Assert.That(cursor.Current(context), Is.EqualTo(1d));
            Assert.That(cursor.Current(context), Is.EqualTo(1d));
            Assert.That(Run("Remaining", cursor), Is.EqualTo(new object[] { 2d, 4d }));
            Assert.That(cursor.MoveNext(context), Is.False);
            Assert.That(() => cursor.Current(context), Throws.TypeOf<NSGetterRuntimeError>());
            Assert.That(Run("Remaining", cursor), Is.Empty);
        }

        [Test]
        public void RangesAreUpperExclusiveAndEmptyWhenReversed()
        {
            Assert.That(Run("Collect", Run("Range", 2d, 5d)), Is.EqualTo(new object[] { 2d, 3d, 4d }));
            Assert.That(Run("Collect", Run("Range", 5d, 2d)), Is.Empty);
        }

        [Test]
        public void DirectAndGeneratorRangesRejectUnsafeBoundsBeforeIteration()
        {
            Assert.That(Run("DirectRange", 2d, 5d), Is.EqualTo(9d));
            Assert.That(() => Run("DirectRange", 0d, 9007199254740992d), Throws.Exception.With.Message.Contains("safe int"));
            Assert.That(() => Run("Sum", Run("Powers", 9007199254740992d)), Throws.Exception.With.Message.Contains("safe int"));
        }

        [TestCase("First")]
        [TestCase("Break")]
        public void EarlyExitUnwindsFinallyBlocksInOrder(string consumer)
        {
            var log = NSGetterEvaluator.KeepDerived(new Dictionary<string, object?> { ["value"] = 0d }, null, true);
            Run(consumer, Run("CleanupOrder", log));
            Assert.That(log["value"], Is.EqualTo(23d));
        }

        [Test]
        public void NestedGenericAndContextualConversionsMatchTheSharedFixture()
        {
            object? values = Run("Range", 1d, 3d);
            Assert.That(Run("Collect", Run("Nested", values)), Is.EqualTo(new object[] { 10d, 11d, 20d, 21d }));
            Assert.That(Run("Boundaries", values), Is.EqualTo(12d));
            context.BindFunctionTypeArguments(new TypeInfo[] { NeoSequenceContract.Int });
            object? one = Run("One", 42d);
            context.BindFunctionTypeArguments(null);
            Assert.That(Run("Collect", one), Is.EqualTo(new object[] { 42d }));
            Assert.That(Run("Collect", Run("Coalesce", null, values)), Is.EqualTo(new object[] { 1d, 2d }));
            Assert.That(Run("CoalesceList", null, values), Is.EqualTo(new object[] { 1d, 2d }));
            Assert.That(Run("Remaining", Run("CoalesceCursor", null, values)), Is.EqualTo(new object[] { 1d, 2d }));
        }

        [Test]
        public void RetentionTrackingDoesNotKeepDiscardedLocalGraphsAlive()
        {
            var retention = new NeoScriptExecutor.GeneratorRetention();
            WeakReference discarded = TrackTemporaryGraph(retention, context);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(discarded.IsAlive, Is.False);
            GC.KeepAlive(retention);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static WeakReference TrackTemporaryGraph(NeoScriptExecutor.GeneratorRetention retention, NSGetterEvaluator.Context context)
        {
            var value = new LocalList(new object[] { 1d }, 2d, null, true);
            retention.Track(value);
            retention.Flush(context);
            return new WeakReference(value);
        }

        [Test]
        public void GrowingLocalGraphsSurviveSuspension()
        {
            Assert.That(Run("Collect", Run("Growing", 3d)), Is.EqualTo(new object[] { 0d, 1d, 2d, 2d }));
            Assert.That(Run("Sum", Run("Graph", 8d)), Is.EqualTo(8d));
        }

        [TestCase("InterfaceDefault")]
        [TestCase("ListDefault")]
        [TestCase("DictionaryDefault")]
        public void InitializersRejectRuntimeSequencesAtAnyStoredDepth(string name)
        {
            var body = bodies[name]!.ToObject<FunctionWithReturnType>()!;
            var member = new ClassMember { id = "default-sequence", name = "Values", kind = MemberKind.Class };
            var init = new InitializerBody { code = "runtime factory", compiled = body };
            Assert.That(() => NeoGeneratedTypesSupport.EvaluateInitializer(client, context, default, null, member, init, null),
                Throws.Exception.With.Message.Contains("requires an authored class"));
        }

        [Test]
        public void StaleYieldMetadataIsRejectedBeforeExecution()
        {
            var body = bodies["Powers"]!.ToObject<FunctionWithReturnType>()!;
            body.generator = null;
            Assert.That(() => NSGetterEvaluator.Evaluate(body, context, new object?[] { 3d }), Throws.Exception.With.Message.Contains("Yield"));
        }
    }
}
