#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    public partial class NSFunctionRuntimeTests
    {
        [Test]
        public void StructuredErrors_FilterCleanupAndCatchSuspendWithoutReplay()
        {
            var filter = NativeFunction("p122-filter", "Filter", deferred: true);
            var cleanup = NativeFunction("p122-cleanup", "Cleanup", deferred: true);
            var caught = NativeFunction("p122-catch", "Caught", deferred: true);
            var inner = TryBlock(new Instruction[] { Throw(Text("original")) });
            inner.finallyInstructions = new Instruction[] { new FunctionCallInstruction { type = InstructionKind.FunctionCall, call = Call(cleanup.id, "cleanup") } };
            var function = ScriptFunction("p122-resume", "Resume", true, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                TryAction(IntType(), TryBlock(new Instruction[] { inner },
                    Catch("error", Compare(OperatorKind.EqualTo, Call(filter.id, "filter"), Number(1)),
                        new FunctionCallInstruction { type = InstructionKind.FunctionCall, call = Call(caught.id, "caught") }, Return(Number(42))),
                    Catch("fallback", null, Return(Number(-1))))));
            using NeoClient client = BuildClient(new JsonMember[] { filter, cleanup, caught, function },
                ReceiverClass(("Filter", filter.id), ("Cleanup", cleanup.id), ("Caught", caught.id), ("Resume", function.id)));
            var pending = new Dictionary<string, NeoDeferredFunction<int>>();
            var calls = new List<string>();
            var invokers = new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>();
            foreach (FunctionMember member in new[] { filter, cleanup, caught })
                invokers[member.id] = (_, _, _, _, deferred) =>
                {
                    calls.Add(member.name);
                    pending[member.name] = NeoGeneratedTypesSupport.ResolveDeferredFunction<NeoDeferredFunction<int>>(deferred, member.name);
                };
            client.RegisterDeferredNativeFunctionInvokers(invokers);
            Task<object?> task = new NeoMemberNSFunction(client, function, null).InvokeAsync("receiver-value", Array.Empty<object?>());
            CollectionAssert.AreEqual(new[] { "Filter" }, calls);
            Assert.IsFalse(task.IsCompleted);
            pending["Filter"].Complete(1);
            CollectionAssert.AreEqual(new[] { "Filter", "Cleanup" }, calls);
            pending["Cleanup"].Complete(0);
            CollectionAssert.AreEqual(new[] { "Filter", "Cleanup", "Caught" }, calls);
            pending["Caught"].Complete(0);
            Assert.IsTrue(task.IsCompleted, "The final callback must settle the invocation.");
            Assert.AreEqual(42, Convert.ToInt32(task.GetAwaiter().GetResult()));
            Assert.AreEqual(3, calls.Count);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void StructuredErrors_NativeReentrySearchesTheNewInvocation(bool deferred, bool synchronousThrow)
        {
            var native = NativeFunction("p122-reenter", "Reenter", deferred: deferred);
            var escape = TryBlock(new Instruction[] { Throw(Text("original")) });
            escape.finallyInstructions = Array.Empty<Instruction>();
            var source = ScriptFunction("p122-escape", "Escape", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(), TryAction(IntType(), escape));
            var target = ScriptFunction("p122-target", "Target", deferred, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                TryAction(IntType(), TryBlock(new Instruction[] { Return(Call(native.id, "native")) }, Catch("error", null, Return(Number(42))))));
            using NeoClient client = BuildClient(new JsonMember[] { native, source, target }, ReceiverClass(("Reenter", native.id), ("Escape", source.id), ("Target", target.id)));
            var original = Assert.Throws<NeoScriptException>(() => new NeoMemberNSFunction(client, source, null).Invoke("receiver-value", Array.Empty<object?>()))!;
            if (deferred)
            {
                NeoDeferredFunction<int>? pending = null;
                client.RegisterDeferredNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [native.id] = (_, _, _, _, value) =>
                    {
                        if (synchronousThrow)
                            throw original;
                        pending = NeoGeneratedTypesSupport.ResolveDeferredFunction<NeoDeferredFunction<int>>(value, native.name);
                    }
                });
                for (int i = 0; i < 2; i++)
                {
                    Task<object?> task = new NeoMemberNSFunction(client, target, null).InvokeAsync("receiver-value", Array.Empty<object?>());
                    if (!synchronousThrow)
                        pending!.Fail(original);
                    Assert.AreEqual(42, Convert.ToInt32(task.GetAwaiter().GetResult()));
                }
            }
            else
            {
                client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker> { [native.id] = (_, _, _, _) => throw original });
                for (int i = 0; i < 2; i++)
                    Assert.AreEqual(42, Convert.ToInt32(new NeoMemberNSFunction(client, target, null).Invoke("receiver-value", Array.Empty<object?>())));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StructuredErrors_ForeignScriptCarrierIsSanitized(bool rowBacked)
        {
            using NeoClient origin = NeoScriptStructuredErrorsTests.Client();
            NeoScriptException foreign = NeoScriptStructuredErrorsTests.ThrowFixture(origin, rowBacked);
            var native = NativeFunction("p122-foreign", "Foreign", deferred: false);
            var target = ScriptFunction("p122-target", "Target", false, IntType(), Array.Empty<FunctionArgumentTypeInfo>(),
                TryAction(IntType(), TryBlock(new Instruction[] { Return(Call(native.id, "native")) },
                    Catch("error", Compare(OperatorKind.EqualTo, Variable("error"), Text(NeoErrorContract.HostMessage)), Return(Number(42))))));
            using NeoClient client = BuildClient(new JsonMember[] { native, target }, ReceiverClass(("Foreign", native.id), ("Target", target.id)));
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker> { [native.id] = (_, _, _, _) => throw foreign });
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("another NeoClient"));
            Assert.AreEqual(42, Convert.ToInt32(new NeoMemberNSFunction(client, target, null).Invoke("receiver-value", Array.Empty<object?>())));
        }

        [Test]
        public void StructuredErrors_DisposalAbandonsWithoutFinally()
        {
            var pause = NativeFunction("p122-pause", "Pause", deferred: true);
            var cleanup = NativeFunction("p122-cleanup", "Cleanup", deferred: true);
            var block = TryBlock(new Instruction[] { Return(Call(pause.id, "pause")) });
            block.finallyInstructions = new Instruction[] { new FunctionCallInstruction { type = InstructionKind.FunctionCall, call = Call(cleanup.id, "cleanup") } };
            var function = ScriptFunction("p122-abandon", "Abandon", true, IntType(), Array.Empty<FunctionArgumentTypeInfo>(), TryAction(IntType(), block));
            using NeoClient client = BuildClient(new JsonMember[] { pause, cleanup, function },
                ReceiverClass(("Pause", pause.id), ("Cleanup", cleanup.id), ("Abandon", function.id)));
            NeoDeferredFunction<int>? pending = null;
            int cleanupCalls = 0;
            client.RegisterDeferredNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
            {
                [pause.id] = (_, _, _, _, deferred) => pending = NeoGeneratedTypesSupport.ResolveDeferredFunction<NeoDeferredFunction<int>>(deferred, pause.name),
                [cleanup.id] = (_, _, _, _, _) => cleanupCalls++,
            });
            Task<object?> task = new NeoMemberNSFunction(client, function, null).InvokeAsync("receiver-value", Array.Empty<object?>());
            Assert.IsFalse(task.IsCompleted);
            Assert.AreEqual(0, cleanupCalls);
            client.Dispose();
            Assert.IsTrue(task.IsCanceled);
            Assert.IsTrue(pending!.CancellationToken.IsCancellationRequested);
            Assert.AreEqual(0, cleanupCalls);
        }
    }
}
