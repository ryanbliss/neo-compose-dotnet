// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoScriptScopeTests
    {
        [Test]
        public void ChildFrame_ReadsParentAndKeepsWritesLocal()
        {
            var parent = new NeoScriptScope();
            parent.SetLocal("captured", "caller");
            NeoScriptScope callback = parent.CreateChild(2);

            Assert.IsTrue(callback.TryGetValue("captured", out object? captured));
            Assert.AreEqual("caller", captured);

            callback.SetLocal("captured", "callback");

            Assert.IsTrue(callback.ContainsLocal("captured"));
            Assert.IsTrue(callback.TryGetValue("captured", out captured));
            Assert.AreEqual("callback", captured);
            Assert.IsTrue(parent.TryGetValue("captured", out captured));
            Assert.AreEqual("caller", captured);
        }

        [Test]
        public void ChildFrame_InheritsReadOnlyDiagnosticsWithoutCopyingThem()
        {
            var parent = new NeoScriptScope();
            parent.MarkReadOnly("iterator", "read-only iterator");
            NeoScriptScope callback = parent.CreateChild(2);

            Assert.IsTrue(callback.TryGetReadOnlyError("iterator", out string? error));
            Assert.AreEqual("read-only iterator", error);

            callback.MarkReadOnly("message", "read-only message");
            Assert.IsTrue(callback.TryGetReadOnlyError("message", out error));
            Assert.AreEqual("read-only message", error);
            Assert.IsFalse(parent.TryGetReadOnlyError("message", out _));
        }

        [Test]
        public void CollectionCallback_AssignmentToCapturedBindingStaysIsolated()
        {
            const string packageRoot =
                "Packages/com.ryanbliss.neocompose/Tests";
            NeoClient client = NeoTestSaveStack.LoadClient(
                File.ReadAllText(Path.Combine(packageRoot, "synth-example.json")));
            var scope = new Dictionary<string, object?>
            {
                ["captured"] = "caller",
            };
            PrimitiveTypeInfo stringType = RequiredType(MemberKind.String);
            PrimitiveTypeInfo boolType = RequiredType(MemberKind.Bool);
            var callback = new FunctionWithReturnType
            {
                parameters = new[]
                {
                    new Variable
                    {
                        id = "item",
                        typeInfo = stringType,
                        pointer = StringValue(""),
                    },
                },
                typeInfo = boolType,
                instructions = new Instruction[]
                {
                    new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        target = new WriteTarget
                        {
                            pointer = VariableValue("captured"),
                            typeInfo = stringType,
                        },
                        operatorValue = "=",
                        pointer = VariableValue("item"),
                    },
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = BoolValue(true),
                    },
                },
            };
            var first = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new FirstFunction
                {
                    type = FunctionKind.First,
                    info = new FunctionCollectionOptionalBoolInfo
                    {
                        collectionPointer = new ListLiteralPointer
                        {
                            type = PointerKind.ListLiteral,
                            typeInfo = new CollectionTypeInfo
                            {
                                type = MemberKind.List,
                                required = true,
                                entryTypeInfo = stringType,
                            },
                            entries = new Pointer[]
                            {
                                StringValue("alpha"),
                                StringValue("beta"),
                            },
                        },
                        function = callback,
                    },
                },
            };

            object? result = NSGetterEvaluator.EvaluatePointer(
                first,
                scope,
                new NSGetterEvaluator.Context(client, null, null));

            Assert.AreEqual("alpha", result);
            Assert.AreEqual("caller", scope["captured"]);
        }

        [TestCase(1)]
        [TestCase(10)]
        [TestCase(100)]
        [TestCase(1_000)]
        public void CallbackFrame_LocalBindingCountDoesNotScaleWithParentSize(
            int parentSize)
        {
            var parent = new NeoScriptScope(parentSize);
            for (int index = 0; index < parentSize; index++)
            {
                parent.SetLocal($"captured:{index}", index);
            }

            int localBindingCount = 0;
            for (int entry = 0; entry < 10_000; entry++)
            {
                NeoScriptScope callback = parent.CreateChild(2);
                callback.SetLocal("index", entry);
                callback.SetLocal("item", entry);
                localBindingCount += callback.LocalBindingCount;
            }

            Assert.AreEqual(20_000, localBindingCount);
        }

        [Test]
        public void NewCallbackFrame_DoesNotDisclosePriorInvocationBindings()
        {
            var parent = new NeoScriptScope();
            NeoScriptScope first = parent.CreateChild(2);
            first.SetLocal("item", "first");
            first.SetLocal("callbackLocal", "private");

            NeoScriptScope second = parent.CreateChild(2);
            second.SetLocal("item", "second");

            Assert.IsTrue(second.TryGetValue("item", out object? item));
            Assert.AreEqual("second", item);
            Assert.IsFalse(second.TryGetValue("callbackLocal", out _));
        }

        private static PrimitiveTypeInfo RequiredType(MemberKind kind) => new()
        {
            type = kind,
            required = true,
        };

        private static VariablePointer VariableValue(string id) => new()
        {
            type = PointerKind.Variable,
            variableId = id,
        };

        private static ValuePointer StringValue(string value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = RequiredType(MemberKind.String),
                value = JToken.FromObject(value),
            },
        };

        private static ValuePointer BoolValue(bool value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = RequiredType(MemberKind.Bool),
                value = JToken.FromObject(value),
            },
        };
    }
}
