// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.IO;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public sealed class P127DelegateCacheTests
    {
        private NeoClient client = null!;
        private static readonly PrimitiveTypeInfo IntType = new() { type = MemberKind.Int, required = true };
        private static readonly DelegateTypeInfo Signature = new()
        {
            type = MemberKind.NSDelegate,
            required = true,
            returnTypeInfo = IntType,
            argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
        };

        [SetUp]
        public void SetUp()
        {
            client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/p127-equality-parity-export.json")), assumeCurrentSchema: false);
            client.ProjectDataForRuntime.members["b6-cache-function"] = new FunctionMember
            {
                id = "b6-cache-function",
                name = "Value",
                kind = MemberKind.Function,
                Modifier = NeoMemberModifierKind.Static,
                returnTypeInfo = IntType,
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            };
        }

        [TearDown]
        public void TearDown() => client.Dispose();

        private static Pointer Pointer(string kind)
        {
            var body = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                typeInfo = IntType,
                parameters = Array.Empty<Variable>(),
                instructions = Array.Empty<Instruction>(),
            };
            if (kind == "closure")
                return new DelegateClosurePointer
                {
                    type = PointerKind.DelegateClosure,
                    site = "b6-cache-function:function:0",
                    code = "() => 7",
                    typeInfo = Signature,
                    action = body,
                    captures = Array.Empty<Pointer>(),
                };
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value { typeInfo = Signature },
                valueTemplate = kind == "literal" ? new NeoDelegateValue { action = body, code = "() => 7", placement = "this" }
                    : new NeoDelegateValue { memberId = "b6-cache-function" },
                cache = kind == "group" ? new NeoScriptMethodGroupCache { owner = "b6-cache-function", delegateType = Signature } : null,
            };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private NeoDelegateValue Evaluate(Pointer pointer, object? receiver, object root) =>
            (NeoDelegateValue)NSGetterEvaluator.EvaluatePointer(pointer, new NeoScriptScope(), new NSGetterEvaluator.Context(client, receiver, root))!;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private WeakReference PopulateTemporaryRoot(Pointer pointer)
        {
            object root = new object();
            Evaluate(pointer, new object(), root);
            return new WeakReference(root);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private WeakReference PopulateTemporaryReceiver(Pointer pointer, object root)
        {
            object receiver = new object();
            Evaluate(pointer, receiver, root);
            return new WeakReference(receiver);
        }

        private static void Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        [TestCase("literal")]
        [TestCase("closure")]
        [TestCase("group")]
        public void DelegateCachesReleaseAnExpiredRoot(string kind)
        {
            var pointer = Pointer(kind);
            var root = PopulateTemporaryRoot(pointer);
            Collect();
            Assert.That(root.IsAlive, Is.False);
            GC.KeepAlive(pointer);
            GC.KeepAlive(client);
        }

        [Test]
        public void LiteralCacheReleasesAReceiverWhileItsRootRemainsAlive()
        {
            var pointer = Pointer("literal");
            object root = new object();
            var receiver = PopulateTemporaryReceiver(pointer, root);
            Collect();
            Assert.That(receiver.IsAlive, Is.False);
            GC.KeepAlive(root);
            GC.KeepAlive(pointer);
            GC.KeepAlive(client);
        }

        [TestCase("literal")]
        [TestCase("closure")]
        [TestCase("group")]
        public void LiveCacheKeysRetainDelegateIdentity(string kind)
        {
            var pointer = Pointer(kind);
            object root = new object(), receiver = new object();
            var first = Evaluate(pointer, receiver, root);
            Collect();
            Assert.That(Evaluate(pointer, receiver, root), Is.SameAs(first));
            Assert.That(Evaluate(pointer, receiver, new object()), Is.Not.SameAs(first));
            if (kind == "literal")
                Assert.That(Evaluate(pointer, new object(), root), Is.Not.SameAs(first));
        }
    }
}
