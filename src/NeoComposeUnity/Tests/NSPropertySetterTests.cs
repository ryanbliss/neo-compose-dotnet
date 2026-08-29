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
                dispatch = NeoFunctionDispatchKind.Synchronous,
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
                dispatch = NeoFunctionDispatchKind.Asynchronous,
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
                    ["value-receiver"] = ObjectValue("value-receiver", "class-receiver"),
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
                        ("Computed", baseProperty.id)),
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
                storage = storage,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static NeoSchemaClass NeoSchemaClass(
            string id,
            string name,
            (string key, string memberId) schema,
            string? extendsClassId = null)
        {
            return new NeoSchemaClass
            {
                id = id,
                projectId = "project-setter",
                name = name,
                schema = new Dictionary<string, string>
                {
                    [schema.key] = schema.memberId,
                },
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
            foreach (var entry in entries) value[entry.key] = entry.valueId;
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

        private sealed class TestEnumOption
        {
            public TestEnumOption(string optionId)
            {
                this.optionId = optionId;
            }

            public string optionId { get; }
        }

        private sealed class TestValueReference : INeoValueReference
        {
            public TestValueReference(string valueId)
            {
                this.valueId = valueId;
            }

            public string? valueId { get; }
        }
    }
}
