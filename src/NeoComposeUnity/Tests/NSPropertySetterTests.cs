// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    public class NSPropertySetterTests
    {
        [Test]
        public void Set_WritesThroughSaveTarget()
        {
            var client = BuildClient(out NSPropertyMember property);
            var node = new NeoMemberNSProperty(client, property, null);

            NSSetterResult result = node.Set("value-receiver", 42);

            Assert.IsTrue(result.ok, result.error);
            Assert.IsFalse(result.pending);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberMemberValue? target));
            Assert.AreEqual(42, target!.value);
        }

        [Test]
        public void Set_WithoutReceiverReturnsErrorAndLogsOnce()
        {
            var client = BuildClient(out NSPropertyMember property);
            var node = new NeoMemberNSProperty(client, property, null);
            LogAssert.Expect(
                LogType.Error,
                new Regex("Cannot invoke setter on a null receiver"));

            NSSetterResult result = node.Set(42);

            Assert.IsFalse(result.ok);
            Assert.IsFalse(result.pending);
            StringAssert.Contains("null receiver", result.error);
        }

        [Test]
        public void Set_WriteToAssetRowAsSaveReturnsOwnershipErrorAndLogsOnce()
        {
            var illegalSetter = Function(new AssignInstruction
            {
                type = InstructionKind.Assign,
                target = new WriteTarget
                {
                    pointer = new ReferencePointer
                    {
                        type = PointerKind.Reference,
                        valueId = "value-receiver",
                    },
                    typeInfo = IntType(),
                    writability = WritabilityKind.Save,
                },
                operatorValue = "=",
                pointer = ValueVariable(),
            });
            var client = BuildClient(
                out NSPropertyMember property,
                baseSetter: illegalSetter);
            var node = new NeoMemberNSProperty(client, property, null);
            LogAssert.Expect(
                LogType.Error,
                new Regex("value-receiver.*not save-owned"));

            NSSetterResult result = node.Set("value-receiver", 9);

            Assert.IsFalse(result.ok);
            Assert.IsFalse(result.pending);
            StringAssert.Contains("not save-owned", result.error);
        }

        [Test]
        public void ActionAssignment_RuntimeWritabilityUsesActualSaveOwnership()
        {
            var client = BuildClient(out _);
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var root = RuntimeRoot(client, ctx);
            ctx = ctx.WithRoot(root);
            var action = Function(new AssignInstruction
            {
                type = InstructionKind.Assign,
                target = new WriteTarget
                {
                    pointer = RootTargetPointer(),
                    typeInfo = IntType(),
                    writability = WritabilityKind.Runtime,
                },
                operatorValue = "=",
                pointer = NumberLiteral(31),
            });
            var scope = new Dictionary<string, object?> { ["__root__"] = root };

            NeoScriptExecutionResult result = NeoScriptExecutor.Execute(
                client,
                action,
                scope,
                ctx);

            Assert.IsFalse(result.IsPaused);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberMemberValue? target));
            Assert.AreEqual(31, target!.value);
        }

        [Test]
        public void ActionAssignment_RuntimeWritabilityUsesActualSessionOwnership()
        {
            var client = BuildClient(out _);
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var root = RuntimeRoot(client, ctx);
            ctx = ctx.WithRoot(root);
            var action = Function(new AssignInstruction
            {
                type = InstructionKind.Assign,
                target = new WriteTarget
                {
                    pointer = KeyOf(KeyOf(RootVariable(), "Session"), "Target"),
                    typeInfo = IntType(),
                    writability = WritabilityKind.Runtime,
                },
                operatorValue = "=",
                pointer = NumberLiteral(47),
            });
            var scope = new Dictionary<string, object?> { ["__root__"] = root };

            NeoScriptExecutionResult result = NeoScriptExecutor.Execute(
                client,
                action,
                scope,
                ctx);

            Assert.IsFalse(result.IsPaused);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                "value-session-target",
                out NumberMemberValue? target));
            Assert.AreEqual(47, target!.value);
        }

        [Test]
        public void ActionAssignment_RuntimeWritabilityRejectsAssetOwnership()
        {
            var client = BuildClient(out _);
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var root = RuntimeRoot(client, ctx);
            ctx = ctx.WithRoot(root);
            var action = Function(new AssignInstruction
            {
                type = InstructionKind.Assign,
                target = new WriteTarget
                {
                    pointer = KeyOf(KeyOf(RootVariable(), "Assets"), "Target"),
                    typeInfo = IntType(),
                    writability = WritabilityKind.Runtime,
                },
                operatorValue = "=",
                pointer = NumberLiteral(31),
            });
            var scope = new Dictionary<string, object?> { ["__root__"] = root };

            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                NeoScriptExecutor.Execute(client, action, scope, ctx));

            StringAssert.Contains("runtime-owned target", error!.Message);
            StringAssert.Contains("Asset-owned", error.Message);
        }

        [Test]
        public void Set_DispatchesMostDerivedSetterByRuntimeClass()
        {
            var client = BuildClient(
                out NSPropertyMember baseProperty,
                baseSetter: SetterWritingTarget(NumberLiteral(1)),
                derivedSetter: SetterWritingTarget(ValueVariable()));
            var node = new NeoMemberNSProperty(client, baseProperty, null);

            NSSetterResult result = node.Set("value-derived-receiver", 73);

            Assert.IsTrue(result.ok, result.error);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberMemberValue? target));
            Assert.AreEqual(73, target!.value);
        }

        [Test]
        public void Set_NormalizesRepresentativeGeneratedValueFamiliesThroughOneBoundary()
        {
            AssertNormalized(
                new PrimitiveTypeInfo { type = MemberKind.Bool, required = true },
                true,
                captured => Assert.AreEqual(true, captured));
            AssertNormalized(
                new PrimitiveTypeInfo { type = MemberKind.Decimal, required = true },
                12.5m,
                captured => Assert.AreEqual("12.5", captured));
            AssertNormalized(
                new EnumTypeInfo
                {
                    type = MemberKind.Enum,
                    required = true,
                    enumId = "enum-test",
                },
                new TestEnumOption("option-a"),
                captured => CollectionAssert.AreEqual(
                    new[] { "option-a" },
                    (string[])captured!));
            AssertNormalized(
                new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = true,
                    classId = "class-receiver",
                },
                new TestValueReference("value-receiver"),
                captured => Assert.IsInstanceOf<IDictionary<string, object?>>(captured));
            AssertNormalized(
                new PrimitiveTypeInfo { type = MemberKind.Vector2, required = true },
                new Vector2(1.25f, -2.5f),
                captured => Assert.AreEqual(
                    new Vector2(1.25f, -2.5f),
                    NeoGeneratedTypesSupport.ReadVector2Value(captured)));
            AssertNormalized(
                new PrimitiveTypeInfo { type = MemberKind.Color, required = true },
                new Color(0.1f, 0.2f, 0.3f, 0.4f),
                captured => Assert.AreEqual(
                    new Color(0.1f, 0.2f, 0.3f, 0.4f),
                    NeoGeneratedTypesSupport.ReadColorValue(captured)));
            AssertNormalized(
                new CollectionTypeInfo
                {
                    type = MemberKind.List,
                    required = true,
                    entryTypeInfo = IntType(),
                },
                new[] { 1, 2, 3 },
                captured => CollectionAssert.AreEqual(
                    new object?[] { 1, 2, 3 },
                    (object?[])captured!));
            AssertNormalized(
                new CollectionTypeInfo
                {
                    type = MemberKind.Dictionary,
                    required = true,
                    entryTypeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.String,
                        required = false,
                    },
                },
                new Dictionary<string, string?> { ["a"] = "A", ["b"] = null },
                captured => CollectionAssert.AreEquivalent(
                    new Dictionary<string, object?> { ["a"] = "A", ["b"] = null },
                    (IDictionary<string, object?>)captured!));
            AssertNormalized(
                new LookupTypeInfo
                {
                    type = MemberKind.Lookup,
                    required = true,
                    entryTypeInfo = new ClassTypeInfo
                    {
                        type = MemberKind.Class,
                        required = true,
                        classId = "class-receiver",
                    },
                    collectionMemberId = "member-receiver-value",
                },
                new[] { new TestValueReference("value-receiver") },
                captured =>
                {
                    var values = (object?[])captured!;
                    Assert.AreEqual(1, values.Length);
                    Assert.IsInstanceOf<IDictionary<string, object?>>(values[0]);
                });
            AssertNormalized(
                new GenericTypeInfo
                {
                    type = MemberKind.Generic,
                    required = true,
                    ownerClassId = "class-generic",
                    genericParamId = "param-t",
                },
                "generic-value",
                captured => Assert.AreEqual("generic-value", captured));
            AssertNormalized(
                new PrimitiveTypeInfo { type = MemberKind.String, required = false },
                null,
                captured => Assert.IsNull(captured));
        }

        [Test]
        public void ActionAssignment_InvokesPropertySetter()
        {
            var client = BuildClient(out NSPropertyMember property);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Asset,
                "value-receiver",
                out ObjectMemberValue? receiverRow));
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var root = RuntimeRoot(client, ctx);
            ctx = ctx.WithRoot(root);
            object? receiver = NSGetterEvaluator.UnwrapRow(
                receiverRow!,
                ctx,
                NeoValueOwnership.Asset);
            var action = Function(new AssignInstruction
            {
                type = InstructionKind.Assign,
                target = new WriteTarget
                {
                    pointer = new CallGetterPointer
                    {
                        type = PointerKind.CallGetter,
                        memberId = property.id,
                        receiver = CallReceiver.Instance(ThisVariable()),
                    },
                    typeInfo = IntType(),
                    writability = WritabilityKind.Setter,
                },
                operatorValue = "=",
                pointer = NumberLiteral(64),
            });
            var scope = new Dictionary<string, object?>
            {
                ["__this__"] = receiver,
                ["__root__"] = root,
            };

            NeoScriptExecutionResult result = NeoScriptExecutor.Execute(
                client,
                action,
                scope,
                ctx.WithThis(receiver));

            Assert.IsFalse(result.IsPaused);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberMemberValue? target));
            Assert.AreEqual(64, target!.value);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ActionAssignment_ConstructorBodySetterCallsDeferredLikeAnUnoptionedFrame(bool constructorBody)
        {
            var client = BuildClient(
                out NSPropertyMember property,
                baseSetter: SetterCallingDeferredThenWritingTarget());
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["member-deferred"] = (_, _, _, deferred) =>
                        NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction>(
                                deferred,
                                "DeferredSetterFunction")
                            .Complete(),
                });
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Asset,
                "value-receiver",
                out ObjectMemberValue? receiverRow));
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var root = RuntimeRoot(client, ctx);
            ctx = ctx.WithRoot(root);
            object? receiver = NSGetterEvaluator.UnwrapRow(
                receiverRow!,
                ctx,
                NeoValueOwnership.Asset);
            var action = Function(new AssignInstruction
            {
                type = InstructionKind.Assign,
                target = new WriteTarget
                {
                    pointer = new CallGetterPointer
                    {
                        type = PointerKind.CallGetter,
                        memberId = property.id,
                        receiver = CallReceiver.Instance(ThisVariable()),
                    },
                    typeInfo = IntType(),
                    writability = WritabilityKind.Setter,
                },
                operatorValue = "=",
                pointer = NumberLiteral(64),
            });
            var scope = new Dictionary<string, object?>
            {
                ["__this__"] = receiver,
                ["__root__"] = root,
            };
            // A constructor body runs immediate, as ExecuteConstructorBody
            // prepares it, yet its setter gets the options a frame without
            // any would give it, so the setter's deferred call still runs.
            var bodyCtx = ctx.WithThis(receiver);
            NeoScriptExecutionOptions? options = null;
            if (constructorBody)
            {
                options = NeoScriptExecutionOptions.ForImmediate(client);
                NeoScriptExecutor.PrepareFunctionContext(bodyCtx, options, -1);
                bodyCtx.constructorBody = true;
            }
            NeoScriptExecutionResult result = NeoScriptExecutor.Execute(
                client,
                action,
                scope,
                bodyCtx,
                options);

            Assert.IsFalse(result.IsPaused);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberMemberValue? target));
            Assert.AreEqual(64, target!.value);
        }

        [Test]
        public void ActionCompoundAssignment_ReadsGetterThenInvokesSetter()
        {
            var client = BuildClient(out NSPropertyMember property);
            client.SetSaveValue(new NumberMemberValue
            {
                id = "value-target",
                value = 10,
                createdAt = "x",
                updatedAt = "x",
            });
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Asset,
                "value-receiver",
                out ObjectMemberValue? receiverRow));
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var root = RuntimeRoot(client, ctx);
            ctx = ctx.WithRoot(root);
            object? receiver = NSGetterEvaluator.UnwrapRow(
                receiverRow!,
                ctx,
                NeoValueOwnership.Asset);
            var callProperty = new CallGetterPointer
            {
                type = PointerKind.CallGetter,
                memberId = property.id,
                receiver = CallReceiver.Instance(ThisVariable()),
            };
            var action = Function(new AssignInstruction
            {
                type = InstructionKind.Assign,
                target = new WriteTarget
                {
                    pointer = callProperty,
                    typeInfo = IntType(),
                    writability = WritabilityKind.Setter,
                },
                operatorValue = "+=",
                pointer = ArithmeticPointer(
                    ArithmeticOpKind.Addition,
                    callProperty,
                    NumberLiteral(6)),
            });
            var scope = new Dictionary<string, object?>
            {
                ["__this__"] = receiver,
                ["__root__"] = root,
            };

            NeoScriptExecutionResult result = NeoScriptExecutor.Execute(
                client,
                action,
                scope,
                ctx.WithThis(receiver));

            Assert.IsFalse(result.IsPaused);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberMemberValue? target));
            Assert.AreEqual(16, target!.value);
        }

        [Test]
        public void Set_SelfAssignmentReturnsCycleErrorAndLogsOnce()
        {
            FunctionWithReturnType recursiveSetter = Function(
                new AssignInstruction
                {
                    type = InstructionKind.Assign,
                    target = new WriteTarget
                    {
                        pointer = new CallGetterPointer
                        {
                            type = PointerKind.CallGetter,
                            memberId = "member-property",
                            receiver = CallReceiver.Instance(ThisVariable()),
                        },
                        typeInfo = IntType(),
                        writability = WritabilityKind.Setter,
                    },
                    operatorValue = "=",
                    pointer = ValueVariable(),
                });
            var client = BuildClient(
                out NSPropertyMember property,
                baseSetter: recursiveSetter);
            var node = new NeoMemberNSProperty(client, property, null);
            LogAssert.Expect(
                LogType.Error,
                new Regex("Circular setter call: 'Computed'.*", RegexOptions.Singleline));

            NSSetterResult result = node.Set("value-receiver", 5);

            Assert.IsFalse(result.ok);
            Assert.IsFalse(result.pending);
            StringAssert.Contains("Circular setter call", result.error);
        }

        [Test]
        public void Set_DeferredFunctionCompletesInlineWithoutPendingWarning()
        {
            var client = BuildClient(
                out NSPropertyMember property,
                baseSetter: SetterCallingDeferredThenWritingTarget());
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["member-deferred"] = (_, _, _, deferred) =>
                        NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction>(
                                deferred,
                                "DeferredSetterFunction")
                            .Complete(),
                });
            var node = new NeoMemberNSProperty(client, property, null);

            NSSetterResult result = node.Set("value-receiver", 17);

            Assert.IsTrue(result.ok, result.error);
            Assert.IsFalse(result.pending);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberMemberValue? target));
            Assert.AreEqual(17, target!.value);
        }

        [Test]
        public void Set_DeferredFunctionFailsInlineWithoutPendingWarning()
        {
            var client = BuildClient(
                out NSPropertyMember property,
                baseSetter: SetterCallingDeferredThenWritingTarget());
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["member-deferred"] = (_, _, _, deferred) =>
                        NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction>(
                                deferred,
                                "DeferredSetterFunction")
                            .Fail(new InvalidOperationException("inline boom")),
                });
            var node = new NeoMemberNSProperty(client, property, null);
            LogAssert.Expect(LogType.Error, new Regex("inline boom"));

            NSSetterResult result = node.Set("value-receiver", 17);

            Assert.IsFalse(result.ok);
            Assert.IsFalse(result.pending);
            StringAssert.Contains("inline boom", result.error);
        }

        [Test]
        public void Set_DeferredFunctionReturnsPendingWarnsOnceAndResumes()
        {
            var client = BuildClient(
                out NSPropertyMember property,
                baseSetter: SetterCallingDeferredThenWritingTarget());
            NeoDeferredFunction? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["member-deferred"] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction>(
                                deferred,
                                "DeferredSetterFunction"),
                });
            var node = new NeoMemberNSProperty(client, property, null);
            LogAssert.Expect(
                LogType.Warning,
                new Regex(
                    "Computed.*member-property.*DeferredSetterFunction.*member-deferred.*did not call Complete/Fail inline",
                    RegexOptions.Singleline));

            NSSetterResult result = node.Set("value-receiver", 29);

            Assert.IsTrue(result.ok, result.error);
            Assert.IsTrue(result.pending);
            Assert.IsNotNull(pending);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberMemberValue? before));
            Assert.AreEqual(0, before!.value);

            pending!.Complete();

            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberMemberValue? after));
            Assert.AreEqual(29, after!.value);
        }

        [Test]
        public void Set_NeverCompletingDeferredFunctionWarnsOnceAndStaysPending()
        {
            var client = BuildClient(
                out NSPropertyMember property,
                baseSetter: SetterCallingDeferredThenWritingTarget());
            NeoDeferredFunction? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["member-deferred"] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction>(
                                deferred,
                                "DeferredSetterFunction"),
                });
            var node = new NeoMemberNSProperty(client, property, null);
            LogAssert.Expect(LogType.Warning, new Regex("did not call Complete/Fail inline"));

            NSSetterResult result = node.Set("value-receiver", 41);

            Assert.IsTrue(result.ok, result.error);
            Assert.IsTrue(result.pending);
            Assert.IsNotNull(pending);
            Assert.IsTrue(pending!.Pending);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Set_DeferredFailureLogsExactlyOneTerminalError()
        {
            var client = BuildClient(
                out NSPropertyMember property,
                baseSetter: SetterCallingDeferredThenWritingTarget());
            NeoDeferredFunction? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["member-deferred"] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction>(
                                deferred,
                                "DeferredSetterFunction"),
                });
            var node = new NeoMemberNSProperty(client, property, null);
            LogAssert.Expect(LogType.Warning, new Regex("did not call Complete/Fail inline"));
            NSSetterResult result = node.Set("value-receiver", 31);
            Assert.IsTrue(result.pending);
            LogAssert.Expect(
                LogType.Error,
                new Regex("NeoScript property setter 'Computed'.*deferred boom", RegexOptions.Singleline));

            pending!.Fail(new InvalidOperationException("deferred boom"));

            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberMemberValue? target));
            Assert.AreEqual(0, target!.value);
        }

        [Test]
        public void Set_PostResumeInstructionFailureLogsExactlyOneTerminalError()
        {
            var setter = Function(
                DeferredCallInstruction(),
                new ThrowInstruction
                {
                    type = InstructionKind.Throw,
                    pointer = StringLiteral("after resume boom"),
                });
            var client = BuildClient(
                out NSPropertyMember property,
                baseSetter: setter);
            NeoDeferredFunction? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["member-deferred"] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction>(
                                deferred,
                                "DeferredSetterFunction"),
                });
            var node = new NeoMemberNSProperty(client, property, null);
            LogAssert.Expect(LogType.Warning, new Regex("did not call Complete/Fail inline"));
            NSSetterResult result = node.Set("value-receiver", 31);
            Assert.IsTrue(result.pending);
            LogAssert.Expect(
                LogType.Error,
                new Regex("NeoScript property setter 'Computed'.*after resume boom", RegexOptions.Singleline));

            pending!.Complete();
        }

        private static void AssertNormalized(
            TypeInfo typeInfo,
            object? input,
            Action<object?> assertCaptured)
        {
            object? captured = null;
            var client = BuildClient(
                out NSPropertyMember property,
                baseSetter: SetterCallingCaptureFunction(),
                propertyType: typeInfo);
            client.RegisterNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    ["member-capture"] = (_, _, args) =>
                    {
                        captured = args[0];
                        return null;
                    },
                });
            var node = new NeoMemberNSProperty(client, property, null);

            NSSetterResult result = node.Set("value-receiver", input);

            Assert.IsTrue(result.ok, result.error);
            Assert.IsFalse(result.pending);
            assertCaptured(captured);
        }

        [Test]
        public void Compute_MemoHitReportsTheDependenciesTheEvaluationReported()
        {
            using var client = BuildClient(out NSPropertyMember property);
            client.SetSaveValue(new NumberMemberValue { id = "value-target", value = 5, createdAt = "x", updatedAt = "x" });
            var node = new NeoMemberNSProperty(client, property, null);
            var key = new NeoClient.GetterMemoKey(
                NeoValueOwnership.Asset, "value-receiver", property.id, NeoValueOwnership.Asset);

            var first = new HashSet<string>();
            using (client.CaptureValueReads(first))
            {
                NSGetterResult miss = node.Compute("value-receiver");
                Assert.IsTrue(miss.ok, miss.error);
                Assert.AreEqual(5, Convert.ToInt32(miss.value));
            }
            Assert.IsNotNull(client.FindMemoizedGetter(key),
                "A row-backed compute under dependency capture must still memoize.");
            CollectionAssert.Contains(first, "value-target");

            var second = new HashSet<string>();
            using (client.CaptureValueReads(second))
                Assert.AreEqual(5, Convert.ToInt32(node.Compute("value-receiver").value));
            CollectionAssert.AreEquivalent(first, second, "A memo hit must report the reads the evaluation reported.");

            client.SetSaveValue(new NumberMemberValue { id = "value-target", value = 9, createdAt = "x", updatedAt = "x" });
            Assert.IsNull(client.FindMemoizedGetter(key), "A write to a read row must drop the entry.");
            var third = new HashSet<string>();
            using (client.CaptureValueReads(third))
                Assert.AreEqual(9, Convert.ToInt32(node.Compute("value-receiver").value));
            CollectionAssert.AreEquivalent(first, third);
        }

        [Test]
        public void Compute_MemoHitOutsideACaptureStillReportsReadsToALaterCapture()
        {
            using var client = BuildClient(out NSPropertyMember property);
            client.SetSaveValue(new NumberMemberValue { id = "value-target", value = 5, createdAt = "x", updatedAt = "x" });
            var node = new NeoMemberNSProperty(client, property, null);

            Assert.AreEqual(5, Convert.ToInt32(node.Compute("value-receiver").value));
            Assert.AreEqual(5, Convert.ToInt32(node.Compute("value-receiver").value));

            var captured = new HashSet<string>();
            using (client.CaptureValueReads(captured))
                Assert.AreEqual(5, Convert.ToInt32(node.Compute("value-receiver").value));
            CollectionAssert.Contains(captured, "value-target");
        }

        [Test]
        public void Compute_MemoizesADerivedListOfScalarsUntilAReadRowChanges()
        {
            using var client = BuildClient(out NSPropertyMember property);
            property.getter = ListGetter(RootTargetPointer(), IntType());
            client.SetSaveValue(new NumberMemberValue { id = "value-target", value = 5, createdAt = "x", updatedAt = "x" });
            var node = new NeoMemberNSProperty(client, property, null);

            var first = (object?[])node.Compute("value-receiver").value!;
            Assert.IsTrue(client.FindMemoizedGetter(ListKey(property)) is { list: not null });
            var second = (object?[])node.Compute("value-receiver").value!;
            Assert.AreNotSame(first, second, "Every hit hands the caller its own array.");
            Assert.AreEqual(5, Convert.ToInt32(second[0]));

            client.SetSaveValue(new NumberMemberValue { id = "value-target", value = 9, createdAt = "x", updatedAt = "x" });
            Assert.IsNull(client.FindMemoizedGetter(ListKey(property)), "A write to a read row must drop the entry.");
            Assert.AreEqual(9, Convert.ToInt32(((object?[])node.Compute("value-receiver").value!)[0]));
        }

        [Test]
        public void Compute_MemoizedListHitResolvesItsRowEntries()
        {
            using var client = BuildClient(out NSPropertyMember property);
            property.getter = ListGetter(
                KeyOf(RootVariable(), "Save"),
                new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "class-root" });
            var node = new NeoMemberNSProperty(client, property, null);

            var first = (object?[])node.Compute("value-receiver").value!;
            Assert.IsTrue(client.FindMemoizedGetter(ListKey(property)) is { list: not null });
            var second = (object?[])node.Compute("value-receiver").value!;
            Assert.AreNotSame(first, second);
            Assert.AreEqual("value-save", ((INeoValueReference)second[0]!).valueId);
        }

        [Test]
        public void ForEach_RecyclesOnlyTheListItsOwnMemoHitLent()
        {
            using var client = BuildClient(out NSPropertyMember property);
            property.getter = ListGetter(RootTargetPointer(), IntType());
            client.SetSaveValue(new NumberMemberValue { id = "value-target", value = 5, createdAt = "x", updatedAt = "x" });
            var node = new NeoMemberNSProperty(client, property, null);
            node.Compute("value-receiver");
            // A C# hit takes the lent array with it and is never handed back.
            var held = (object?[])node.Compute("value-receiver").value!;
            Assert.IsTrue(client.FindMemoizedGetter(ListKey(property)) is { list: not null });

            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Asset, "value-receiver", out ObjectMemberValue? receiverRow));
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var root = RuntimeRoot(client, ctx);
            ctx = ctx.WithRoot(root);
            object? receiver = NSGetterEvaluator.UnwrapRow(receiverRow!, ctx, NeoValueOwnership.Asset);
            var sum = new VariablePointer { type = PointerKind.Variable, variableId = "sum" };
            var body = Function(
                new VariableInstruction
                {
                    type = InstructionKind.Variable,
                    variable = new Variable { id = "sum", pointer = NumberLiteral(0), typeInfo = IntType() },
                },
                new ForEachInstruction
                {
                    type = InstructionKind.ForEach,
                    binding = new LoopBinding
                    {
                        id = "item",
                        typeInfo = IntType(),
                        isReadonly = true,
                        writability = WritabilityKind.ReadOnly,
                    },
                    collectionPointer = new CallGetterPointer
                    {
                        type = PointerKind.CallGetter,
                        memberId = property.id,
                        receiver = CallReceiver.Instance(ThisVariable()),
                    },
                    collectionTypeInfo = property.getter.typeInfo,
                    instructions = new Instruction[]
                    {
                        new AssignInstruction
                        {
                            type = InstructionKind.Assign,
                            target = new WriteTarget { pointer = sum, typeInfo = IntType(), writability = WritabilityKind.Local },
                            operatorValue = "=",
                            pointer = ArithmeticPointer(
                                ArithmeticOpKind.Addition,
                                sum,
                                new VariablePointer { type = PointerKind.Variable, variableId = "item" }),
                        },
                    },
                },
                new ReturnInstruction { type = InstructionKind.Return, pointer = sum });
            body.typeInfo = IntType();
            var scope = new Dictionary<string, object?> { ["__this__"] = receiver, ["__root__"] = root };

            // The first run misses, so its list is not the lent one; later
            // runs hit and hand their lent array back after the snapshot.
            client.SetSaveValue(new NumberMemberValue { id = "value-target", value = 7, createdAt = "x", updatedAt = "x" });
            for (int run = 0; run < 3; run++)
            {
                NeoScriptExecutionResult result = NeoScriptExecutor.Execute(client, body, scope, ctx.WithThis(receiver));
                Assert.AreEqual(7, Convert.ToInt32(result.ReturnValue), $"run {run}");
            }
            Assert.AreEqual(1, held.Length);
            Assert.AreEqual(5, Convert.ToInt32(held[0]), "A list C# holds is never recycled.");
        }

        [Test]
        public void GetterCall_DoesNotReuseAForgottenMemoEntryItsReceiverRemembers()
        {
            using var client = BuildClient(out NSPropertyMember property);
            property.getter = GetterFunction();
            client.SetSaveValue(new NumberMemberValue { id = "value-target", value = 5, createdAt = "x", updatedAt = "x" });
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Asset, "value-receiver", out ObjectMemberValue? receiverRow));
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            ctx = ctx.WithRoot(RuntimeRoot(client, ctx));
            var scope = new Dictionary<string, object?>
            {
                ["__this__"] = NSGetterEvaluator.UnwrapRow(receiverRow!, ctx, NeoValueOwnership.Asset),
            };
            var call = new CallGetterPointer
            {
                type = PointerKind.CallGetter,
                memberId = property.id,
                receiver = CallReceiver.Instance(ThisVariable()),
            };
            int Read() => Convert.ToInt32(NSGetterEvaluator.EvaluatePointer(call, scope, ctx));

            Assert.AreEqual(5, Read());
            Assert.AreEqual(5, Read(), "The receiver remembers its memo entry.");
            client.SetSaveValue(new NumberMemberValue { id = "value-target", value = 9, createdAt = "x", updatedAt = "x" });
            Assert.AreEqual(9, Read(), "A write forgets the entry the receiver remembers.");
            Assert.AreEqual(9, Read());
        }

        [Test]
        public void ComputedOnChanged_NotifiesOnlyTheInstanceWhoseReadRowChanged()
        {
            using var client = BuildClient(out NSPropertyMember property);
            property.getter = ThisCountGetter();
            var first = TestReceiverView.Create(client, "value-receiver");
            var second = TestReceiverView.Create(client, "value-receiver-2");
            var firstValues = new List<int>();
            var secondValues = new List<int>();
            using var firstWatch = first.OnComputedChanged((value, _) => firstValues.Add(value));
            using var secondWatch = second.OnComputedChanged((value, _) => secondValues.Add(value));

            client.SetSaveValue(Number("value-receiver-count", 7));
            CollectionAssert.AreEqual(new[] { 7 }, firstValues, "Subscribing arms the getter without a prior read.");
            CollectionAssert.IsEmpty(secondValues, "Another instance's getter read nothing that changed.");

            client.SetSaveValue(Number("value-receiver-count", 8));
            CollectionAssert.AreEqual(new[] { 7, 8 }, firstValues, "The handler's read re-arms the watch.");

            client.SetSaveValue(Number("value-receiver-2-count", 3));
            CollectionAssert.AreEqual(new[] { 3 }, secondValues);
            CollectionAssert.AreEqual(new[] { 7, 8 }, firstValues);
        }

        [Test]
        public void ComputedOnChanged_HearsALeafWriteAfterItCompletes()
        {
            using var client = BuildClient(out NSPropertyMember property);
            property.getter = ThisCountGetter();
            var view = TestReceiverView.Create(client, "value-receiver");
            Assert.IsTrue(client.TryGetMember("member-count", out JsonMember? count));
            var values = new List<int>();
            using var watch = view.OnComputedChanged((value, _) => values.Add(value));

            Assert.IsTrue(client.TryWriteLeaf(NeoValueOwnership.Save, Number("value-receiver-count", 7), count!, "value"));
            CollectionAssert.AreEqual(new[] { 7 }, values);
            Assert.IsTrue(client.TryWriteLeaf(NeoValueOwnership.Save, Number("value-receiver-count", 9), count!, "value"));
            CollectionAssert.AreEqual(new[] { 7, 9 }, values);
        }

        [Test]
        public void ComputedOnChanged_HearsAGetterWhoseResultIsNotMemoized()
        {
            var rootType = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "class-root" };
            using var client = BuildClient(out NSPropertyMember property, propertyType: rootType);
            // return root.Session; a Session row result is never memoized.
            property.getter = Function(new ReturnInstruction
            {
                type = InstructionKind.Return,
                pointer = KeyOf(RootVariable(), "Session"),
            });
            property.getter.typeInfo = rootType;
            var view = TestReceiverView.Create(client, "value-receiver");
            int changes = 0;
            using var watch = view.WatchAnyChange((owner, changed, _) =>
            {
                if (owner.BackingNode.TryGetSchemaKeyForChild(changed, out string? key) && key == "Computed")
                    changes++;
            });
            Assert.IsTrue(view.ComputeComputed().ok);
            Assert.IsNull(client.FindMemoizedGetter(new NeoClient.GetterMemoKey(
                NeoValueOwnership.Save, "value-receiver", property.id, NeoValueOwnership.Save)),
                "Only the reads are kept; the value is computed again.");

            client.SetWritableValue(NeoValueOwnership.Session, ObjectValue(
                "value-session", "class-root", ("Target", "value-session-target")));
            Assert.AreEqual(1, changes);
            client.SetWritableValue(NeoValueOwnership.Session, ObjectValue(
                "value-session", "class-root", ("Target", "value-session-target")));
            Assert.AreEqual(1, changes, "A watch fires once until the getter is read again.");
            Assert.IsTrue(view.ComputeComputed().ok);
            client.SetWritableValue(NeoValueOwnership.Session, ObjectValue(
                "value-session", "class-root", ("Target", "value-session-target")));
            Assert.AreEqual(2, changes);
        }

        [Test]
        public void ComputedOnChanged_StopsAfterTheSubscriptionIsDisposed()
        {
            using var client = BuildClient(out NSPropertyMember property);
            property.getter = ThisCountGetter();
            var view = TestReceiverView.Create(client, "value-receiver");
            var values = new List<int>();
            IDisposable watch = view.OnComputedChanged((value, _) => values.Add(value));
            client.SetSaveValue(Number("value-receiver-count", 7));
            watch.Dispose();
            client.SetSaveValue(Number("value-receiver-count", 8));
            CollectionAssert.AreEqual(new[] { 7 }, values);
        }

        [Test]
        public void ComputedOnChanged_HearsAGetterWhoseReadFailed()
        {
            using var client = BuildClient(out NSPropertyMember property);
            // if (this.Count == 0) throw "No count yet"; return this.Count;
            property.getter = Function(
                new IfInstruction
                {
                    type = InstructionKind.If,
                    branches = new[]
                    {
                        new ConditionalBranch
                        {
                            expression = new BooleanExpression
                            {
                                condition = new Condition
                                {
                                    type = OperatorKind.EqualTo,
                                    operand1 = KeyOf(ThisVariable(), "Count"),
                                    operand2 = NumberLiteral(0),
                                },
                            },
                            instructions = new Instruction[]
                            {
                                new ThrowInstruction { type = InstructionKind.Throw, pointer = StringLiteral("No count yet") },
                            },
                        },
                    },
                },
                new ReturnInstruction { type = InstructionKind.Return, pointer = KeyOf(ThisVariable(), "Count") });
            property.getter.typeInfo = IntType();
            client.SetSaveValue(Number("value-receiver-count", 0));
            var view = TestReceiverView.Create(client, "value-receiver");
            int changes = 0;
            using var watch = view.WatchAnyChange((owner, changed, _) =>
            {
                if (owner.BackingNode.TryGetSchemaKeyForChild(changed, out string? key) && key == "Computed")
                    changes++;
            });
            NSGetterResult failed = view.ComputeComputed();
            Assert.IsFalse(failed.ok);
            StringAssert.Contains("No count yet", failed.error);

            client.SetSaveValue(Number("value-receiver-count", 4));
            Assert.AreEqual(1, changes, "A failed read still hears a change to what it read.");
            Assert.AreEqual(4, Convert.ToInt32(view.ComputeComputed().value));
        }

        // return this.Count;
        private static FunctionWithReturnType ThisCountGetter()
        {
            FunctionWithReturnType getter = Function(new ReturnInstruction
            {
                type = InstructionKind.Return,
                pointer = KeyOf(ThisVariable(), "Count"),
            });
            getter.typeInfo = IntType();
            return getter;
        }

        private static NumberMemberValue Number(string id, double value) => new()
        {
            id = id,
            value = value,
            createdAt = "x",
            updatedAt = "x",
        };

        private static NeoClient.GetterMemoKey ListKey(NSPropertyMember property) => new(
            NeoValueOwnership.Asset, "value-receiver", property.id, NeoValueOwnership.Asset);

        // return [entry];
        private static FunctionWithReturnType ListGetter(Pointer entry, TypeInfo entryType)
        {
            var listType = new CollectionTypeInfo
            {
                type = MemberKind.List,
                required = true,
                entryTypeInfo = entryType,
            };
            FunctionWithReturnType getter = Function(new ReturnInstruction
            {
                type = InstructionKind.Return,
                pointer = new ListLiteralPointer
                {
                    type = PointerKind.ListLiteral,
                    typeInfo = listType,
                    entries = new[] { entry },
                },
            });
            getter.typeInfo = listType;
            return getter;
        }

        private static NeoClient BuildClient(
            out NSPropertyMember baseProperty,
            FunctionWithReturnType? baseSetter = null,
            FunctionWithReturnType? derivedSetter = null,
            TypeInfo? propertyType = null)
        {
            baseSetter ??= SetterWritingTarget(ValueVariable());
            propertyType ??= IntType();
            var rootAssets = ClassMember(
                "member-root-assets",
                "Assets",
                "class-root",
                "value-assets");
            var rootSave = ClassMember(
                "member-root-save",
                "Save",
                "class-root",
                "value-save",
                NeoMemberStorage.Save);
            var rootSession = ClassMember(
                "member-root-session",
                "Session",
                "class-root",
                "value-session",
                NeoMemberStorage.Session);
            var receiverMember = ClassMember(
                "member-receiver-value",
                "Receiver",
                "class-receiver",
                "value-receiver");
            var targetMember = new IntMember
            {
                id = "member-target",
                projectId = "project-setter",
                name = "Target",
                kind = MemberKind.Int,
                valueId = "value-target",
                createdAt = "x",
                updatedAt = "x",
            };
            var countMember = new IntMember
            {
                id = "member-count",
                projectId = "project-setter",
                name = "Count",
                kind = MemberKind.Int,
                valueId = "value-receiver-count",
                createdAt = "x",
                updatedAt = "x",
            };
            baseProperty = Property(
                "member-property",
                "Computed",
                baseSetter,
                returnTypeInfo: propertyType);
            var derivedProperty = Property(
                "member-derived-property",
                "Computed",
                derivedSetter,
                baseProperty.id,
                propertyType);
            var captureFunction = new FunctionMember
            {
                id = "member-capture",
                projectId = "project-setter",
                name = "CaptureSetterValue",
                kind = MemberKind.Function,
                returnTypeInfo = new VoidTypeInfo
                {
                    type = MemberKind.Void,
                    required = true,
                },
                argumentTypes = new[]
                {
                    FunctionArgument("value", propertyType),
                },
                Dispatch = NeoFunctionDispatchKind.Synchronous,
                createdAt = "x",
                updatedAt = "x",
            };
            var deferredFunction = new FunctionMember
            {
                id = "member-deferred",
                projectId = "project-setter",
                name = "DeferredSetterFunction",
                kind = MemberKind.Function,
                returnTypeInfo = new VoidTypeInfo
                {
                    type = MemberKind.Void,
                    required = true,
                },
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                Dispatch = NeoFunctionDispatchKind.Asynchronous,
                createdAt = "x",
                updatedAt = "x",
            };

            var data = new ProjectData
            {
                project = new Project
                {
                    id = "project-setter",
                    name = "Setter Tests",
                    rootAssetsMemberId = rootAssets.id,
                    rootSaveFileMemberId = rootSave.id,
                    rootSessionMemberId = rootSession.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                members = new Dictionary<string, JsonMember>
                {
                    [rootAssets.id] = rootAssets,
                    [rootSave.id] = rootSave,
                    [rootSession.id] = rootSession,
                    [receiverMember.id] = receiverMember,
                    [targetMember.id] = targetMember,
                    [countMember.id] = countMember,
                    [baseProperty.id] = baseProperty,
                    [derivedProperty.id] = derivedProperty,
                    [captureFunction.id] = captureFunction,
                    [deferredFunction.id] = deferredFunction,
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["value-assets"] = ObjectValue("value-assets", "class-root"),
                    ["value-save"] = ObjectValue(
                        "value-save",
                        "class-root",
                        ("Target", "value-target")),
                    ["value-session"] = ObjectValue(
                        "value-session",
                        "class-root",
                        ("Target", "value-session-target")),
                    ["value-target"] = new NumberMemberValue
                    {
                        id = "value-target",
                        value = 0,
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["value-session-target"] = new NumberMemberValue
                    {
                        id = "value-session-target",
                        value = 0,
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["value-receiver"] = ObjectValue(
                        "value-receiver",
                        "class-receiver",
                        ("Count", "value-receiver-count")),
                    ["value-receiver-count"] = new NumberMemberValue
                    {
                        id = "value-receiver-count",
                        value = 1,
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["value-receiver-2"] = ObjectValue(
                        "value-receiver-2",
                        "class-receiver",
                        ("Count", "value-receiver-2-count")),
                    ["value-receiver-2-count"] = new NumberMemberValue
                    {
                        id = "value-receiver-2-count",
                        value = 2,
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["value-derived-receiver"] = ObjectValue(
                        "value-derived-receiver",
                        "class-derived-receiver"),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["class-root"] = NeoSchemaClass(
                        "class-root",
                        "Root",
                        ("Target", targetMember.id)),
                    ["class-receiver"] = NeoSchemaClass(
                        "class-receiver",
                        "Receiver",
                        ("Computed", baseProperty.id),
                        extraSchema: ("Count", countMember.id)),
                    ["class-derived-receiver"] = NeoSchemaClass(
                        "class-derived-receiver",
                        "DerivedReceiver",
                        ("Computed", derivedProperty.id),
                        extendsClassId: "class-receiver"),
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
            return NeoTestSaveStack.ClientFromSchema(data);
        }

        private static NSPropertyMember Property(
            string id,
            string name,
            FunctionWithReturnType? setter,
            string? extendsMemberId = null,
            TypeInfo? returnTypeInfo = null)
        {
            returnTypeInfo ??= IntType();
            return new NSPropertyMember
            {
                id = id,
                projectId = "project-setter",
                name = name,
                kind = MemberKind.NSProperty,
                code = "return 0;",
                getter = GetterFunction(),
                returnTypeInfo = returnTypeInfo,
                setterCode = setter is null ? null : "root.Save.Target = value;",
                setter = setter,
                extendsMemberId = extendsMemberId,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static FunctionWithReturnType GetterFunction()
        {
            FunctionWithReturnType getter = Function(new ReturnInstruction
            {
                type = InstructionKind.Return,
                pointer = RootTargetPointer(),
            });
            getter.typeInfo = IntType();
            return getter;
        }

        private static FunctionArgumentTypeInfo FunctionArgument(
            string name,
            TypeInfo typeInfo)
        {
            return new FunctionArgumentTypeInfo
            {
                name = name,
                type = typeInfo.type,
                required = typeInfo.required,
                classId = (typeInfo as ClassTypeInfo)?.classId,
                interfaceId = (typeInfo as InterfaceTypeInfo)?.interfaceId,
                enumId = (typeInfo as EnumTypeInfo)?.enumId,
                entryTypeInfo = typeInfo switch
                {
                    CollectionTypeInfo collection => collection.entryTypeInfo,
                    LookupTypeInfo lookup => lookup.entryTypeInfo,
                    _ => null,
                },
                collectionMemberId = (typeInfo as LookupTypeInfo)?.collectionMemberId,
                collectionValueId = (typeInfo as LookupTypeInfo)?.collectionValueId,
                ownerClassId = (typeInfo as GenericTypeInfo)?.ownerClassId,
                genericParamId = (typeInfo as GenericTypeInfo)?.genericParamId,
                typeArguments = (typeInfo as ClassTypeInfo)?.typeArguments,
            };
        }

        private static FunctionWithReturnType SetterCallingDeferredThenWritingTarget()
        {
            return Function(
                DeferredCallInstruction(),
                new AssignInstruction
                {
                    type = InstructionKind.Assign,
                    target = SaveTarget(),
                    operatorValue = "=",
                    pointer = ValueVariable(),
                });
        }

        private static FunctionWithReturnType SetterCallingCaptureFunction()
        {
            return Function(new FunctionCallInstruction
            {
                type = InstructionKind.FunctionCall,
                call = new CallFunctionPointer
                {
                    type = PointerKind.CallFunction,
                    memberId = "member-capture",
                    receiver = CallReceiver.Instance(ThisVariable()),
                    args = new Pointer[] { ValueVariable() },
                    callSiteId = "capture-setter-value",
                },
            });
        }

        private static FunctionCallInstruction DeferredCallInstruction() => new()
        {
            type = InstructionKind.FunctionCall,
            call = new CallFunctionPointer
            {
                type = PointerKind.CallFunction,
                memberId = "member-deferred",
                receiver = CallReceiver.Instance(ThisVariable()),
                args = Array.Empty<Pointer>(),
                callSiteId = "deferred-setter-call",
            },
        };

        private static FunctionWithReturnType SetterWritingTarget(Pointer pointer)
        {
            return Function(new AssignInstruction
            {
                type = InstructionKind.Assign,
                target = SaveTarget(),
                operatorValue = "=",
                pointer = pointer,
            });
        }

        private static FunctionWithReturnType Function(params Instruction[] instructions)
        {
            return new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                instructions = instructions,
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.Null,
                    required = true,
                },
            };
        }

        private static WriteTarget SaveTarget()
        {
            return new WriteTarget
            {
                pointer = RootTargetPointer(),
                typeInfo = IntType(),
                writability = WritabilityKind.Save,
            };
        }

        private static KeyOfPointer RootTargetPointer() =>
            KeyOf(KeyOf(RootVariable(), "Save"), "Target");

        private static PrimitiveTypeInfo IntType() => new()
        {
            type = MemberKind.Int,
            required = true,
        };

        private static VariablePointer RootVariable() => new()
        {
            type = PointerKind.Variable,
            variableId = "__root__",
        };

        private static VariablePointer ThisVariable() => new()
        {
            type = PointerKind.Variable,
            variableId = "__this__",
        };

        private static VariablePointer ValueVariable() => new()
        {
            type = PointerKind.Variable,
            variableId = "__value__",
        };

        private static ValuePointer NumberLiteral(double value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = IntType(),
                value = JToken.FromObject(value),
            },
        };

        private static OperationPointer ArithmeticPointer(
            string op,
            params Pointer[] pointers) => new()
            {
                type = PointerKind.Operation,
                operation = new ArithmeticOperation
                {
                    type = OperationKind.Arithmetic,
                    arithmetic = new ArithmeticOpInfo
                    {
                        type = op,
                        pointers = pointers,
                    },
                },
            };

        private static ValuePointer StringLiteral(string value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.String,
                    required = true,
                },
                value = JToken.FromObject(value),
            },
        };

        private static KeyOfPointer KeyOf(Pointer receiver, string key) => new()
        {
            type = PointerKind.KeyOf,
            keyOf = new KeyOf
            {
                pointer = receiver,
                key = StringLiteral(key),
            },
        };

        private static ClassMember ClassMember(
            string id,
            string name,
            string classId,
            string valueId,
            NeoMemberStorage storage = NeoMemberStorage.Inherit)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-setter",
                name = name,
                kind = MemberKind.Class,
                classId = classId,
                valueId = valueId,
                Storage = storage,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static NeoSchemaClass NeoSchemaClass(
            string id,
            string name,
            (string key, string memberId) schema,
            string? extendsClassId = null,
            (string key, string memberId)? extraSchema = null)
        {
            var entries = new Dictionary<string, string>
            {
                [schema.key] = schema.memberId,
            };
            if (extraSchema is { } extra)
                entries[extra.key] = extra.memberId;
            return new NeoSchemaClass
            {
                id = id,
                projectId = "project-setter",
                name = name,
                schema = entries,
                extendsClassId = extendsClassId,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static ObjectMemberValue ObjectValue(
            string id,
            string classId,
            params (string key, string valueId)[] entries)
        {
            var value = new Dictionary<string, string>();
            foreach (var entry in entries)
                value[entry.key] = entry.valueId;
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = value,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static Dictionary<string, object?> RuntimeRoot(
            NeoClient client,
            NSGetterEvaluator.Context ctx)
        {
            return new Dictionary<string, object?>
            {
                ["Assets"] = client.assets.value is ObjectMemberValue assets
                    ? NSGetterEvaluator.UnwrapRow(
                        assets,
                        ctx,
                        NeoValueOwnership.Asset)
                    : null,
                ["Save"] = client.save.value is ObjectMemberValue save
                    ? NSGetterEvaluator.UnwrapRow(
                        save,
                        ctx,
                        NeoValueOwnership.Save)
                    : null,
                ["Session"] = client.session.value is ObjectMemberValue session
                    ? NSGetterEvaluator.UnwrapRow(
                        session,
                        ctx,
                        NeoValueOwnership.Session)
                    : null,
            };
        }

        /// <summary>The shape generated C# takes for <c>class Receiver</c>'s computed member.</summary>
        private sealed class TestReceiverView : NeoGeneratedClassValue
        {
            private static readonly NeoField<int> Computed = new("Computed");

            private TestReceiverView(NeoClient client, NeoMemberClass node)
                : base(client, node, "class-receiver", false, node.ownership)
            {
            }

            internal static TestReceiverView Create(NeoClient client, string valueId) =>
                NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                    client,
                    new NeoMemberClassWritable(client, "member-receiver-value", valueId, NeoValueOwnership.Save),
                    static (factoryClient, factoryNode) => new TestReceiverView(factoryClient, factoryNode));

            internal NSGetterResult ComputeComputed() => ComputeProperty("Computed");

            private int ComputedValue
            {
                get
                {
                    var result = ComputeProperty("Computed");
                    if (!result.ok)
                        throw new InvalidOperationException(result.error);
                    return Convert.ToInt32(result.value);
                }
            }

            internal IDisposable OnComputedChanged(Action<int, NeoChangeSource> handler) =>
                WatchField(Computed, handler, () => ComputedValue);
        }

        private sealed class TestEnumOption
        {
            public TestEnumOption(string optionId)
            {
                this.optionId = optionId;
            }

            public string optionId
            {
                get;
            }
        }

        private sealed class TestValueReference : INeoValueReference
        {
            public TestValueReference(string valueId)
            {
                this.valueId = valueId;
            }

            public string? valueId
            {
                get;
            }
        }
    }
}
