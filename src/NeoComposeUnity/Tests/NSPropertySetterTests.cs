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
using JsonAttribute = NeoCompose.Runtime.Json.Attribute;

namespace NeoCompose.Tests
{
    public class NSPropertySetterTests
    {
        [Test]
        public void Set_WritesThroughSaveTarget()
        {
            var client = BuildClient(out NSPropertyAttribute property);
            var node = new NeoAttributeNSProperty(client, property, null);

            NSSetterResult result = node.Set("value-receiver", 42);

            Assert.IsTrue(result.ok, result.error);
            Assert.IsFalse(result.pending);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberAttributeValue? target));
            Assert.AreEqual(42, target!.value);
        }

        [Test]
        public void Set_WithoutReceiverReturnsErrorAndLogsOnce()
        {
            var client = BuildClient(out NSPropertyAttribute property);
            var node = new NeoAttributeNSProperty(client, property, null);
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
                out NSPropertyAttribute property,
                baseSetter: illegalSetter);
            var node = new NeoAttributeNSProperty(client, property, null);
            LogAssert.Expect(
                LogType.Error,
                new Regex("value-receiver.*not save-owned"));

            NSSetterResult result = node.Set("value-receiver", 9);

            Assert.IsFalse(result.ok);
            Assert.IsFalse(result.pending);
            StringAssert.Contains("not save-owned", result.error);
        }

        [Test]
        public void Set_DispatchesMostDerivedSetterByRuntimeType()
        {
            var client = BuildClient(
                out NSPropertyAttribute baseProperty,
                baseSetter: SetterWritingTarget(NumberLiteral(1)),
                derivedSetter: SetterWritingTarget(ValueVariable()));
            var node = new NeoAttributeNSProperty(client, baseProperty, null);

            NSSetterResult result = node.Set("value-derived-receiver", 73);

            Assert.IsTrue(result.ok, result.error);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberAttributeValue? target));
            Assert.AreEqual(73, target!.value);
        }

        [Test]
        public void Set_NormalizesRepresentativeGeneratedValueFamiliesThroughOneBoundary()
        {
            AssertNormalized(
                new PrimitiveTypeInfo { type = AttributeType.Bool, required = true },
                true,
                captured => Assert.AreEqual(true, captured));
            AssertNormalized(
                new PrimitiveTypeInfo { type = AttributeType.Decimal, required = true },
                12.5m,
                captured => Assert.AreEqual("12.5", captured));
            AssertNormalized(
                new EnumTypeInfo
                {
                    type = AttributeType.Enum,
                    required = true,
                    enumId = "enum-test",
                },
                new TestEnumOption("option-a"),
                captured => CollectionAssert.AreEqual(
                    new[] { "option-a" },
                    (string[])captured!));
            AssertNormalized(
                new CustomTypeInfo
                {
                    type = AttributeType.Custom,
                    required = true,
                    typeId = "type-receiver",
                },
                new TestValueReference("value-receiver"),
                captured => Assert.IsInstanceOf<IDictionary<string, object?>>(captured));
            AssertNormalized(
                new PrimitiveTypeInfo { type = AttributeType.Vector2, required = true },
                new Vector2(1.25f, -2.5f),
                captured => Assert.AreEqual(
                    new Vector2(1.25f, -2.5f),
                    NeoGeneratedTypesSupport.ReadVector2Value(captured)));
            AssertNormalized(
                new PrimitiveTypeInfo { type = AttributeType.Color, required = true },
                new Color(0.1f, 0.2f, 0.3f, 0.4f),
                captured => Assert.AreEqual(
                    new Color(0.1f, 0.2f, 0.3f, 0.4f),
                    NeoGeneratedTypesSupport.ReadColorValue(captured)));
            AssertNormalized(
                new CollectionTypeInfo
                {
                    type = AttributeType.List,
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
                    type = AttributeType.Dictionary,
                    required = true,
                    entryTypeInfo = new PrimitiveTypeInfo
                    {
                        type = AttributeType.String,
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
                    type = AttributeType.Lookup,
                    required = true,
                    entryTypeInfo = new CustomTypeInfo
                    {
                        type = AttributeType.Custom,
                        required = true,
                        typeId = "type-receiver",
                    },
                    collectionAttributeId = "attribute-receiver-value",
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
                    type = AttributeType.Generic,
                    required = true,
                    ownerTypeId = "type-generic",
                    genericParamId = "param-t",
                },
                "generic-value",
                captured => Assert.AreEqual("generic-value", captured));
            AssertNormalized(
                new PrimitiveTypeInfo { type = AttributeType.String, required = false },
                null,
                captured => Assert.IsNull(captured));
        }

        [Test]
        public void ActionAssignment_InvokesPropertySetter()
        {
            var client = BuildClient(out NSPropertyAttribute property);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Asset,
                "value-receiver",
                out ObjectAttributeValue? receiverRow));
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
                        attributeId = property.id,
                        thisPointer = ThisVariable(),
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

            NeoActionExecutionResult result = NeoActionExecutor.Execute(
                client,
                action,
                scope,
                ctx.WithThis(receiver));

            Assert.IsFalse(result.IsPaused);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberAttributeValue? target));
            Assert.AreEqual(64, target!.value);
        }

        [Test]
        public void ActionCompoundAssignment_ReadsGetterThenInvokesSetter()
        {
            var client = BuildClient(out NSPropertyAttribute property);
            client.SetSaveValue(new NumberAttributeValue
            {
                id = "value-target",
                value = 10,
                createdAt = "x",
                updatedAt = "x",
            });
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Asset,
                "value-receiver",
                out ObjectAttributeValue? receiverRow));
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
                attributeId = property.id,
                thisPointer = ThisVariable(),
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

            NeoActionExecutionResult result = NeoActionExecutor.Execute(
                client,
                action,
                scope,
                ctx.WithThis(receiver));

            Assert.IsFalse(result.IsPaused);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberAttributeValue? target));
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
                            attributeId = "attribute-property",
                            thisPointer = ThisVariable(),
                        },
                        typeInfo = IntType(),
                        writability = WritabilityKind.Setter,
                    },
                    operatorValue = "=",
                    pointer = ValueVariable(),
                });
            var client = BuildClient(
                out NSPropertyAttribute property,
                baseSetter: recursiveSetter);
            var node = new NeoAttributeNSProperty(client, property, null);
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
                out NSPropertyAttribute property,
                baseSetter: SetterCallingDeferredThenWritingTarget());
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["attribute-deferred"] = (_, _, _, deferred) =>
                        NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction>(
                                deferred,
                                "DeferredSetterFunction")
                            .Complete(),
                });
            var node = new NeoAttributeNSProperty(client, property, null);

            NSSetterResult result = node.Set("value-receiver", 17);

            Assert.IsTrue(result.ok, result.error);
            Assert.IsFalse(result.pending);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberAttributeValue? target));
            Assert.AreEqual(17, target!.value);
        }

        [Test]
        public void Set_DeferredFunctionFailsInlineWithoutPendingWarning()
        {
            var client = BuildClient(
                out NSPropertyAttribute property,
                baseSetter: SetterCallingDeferredThenWritingTarget());
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["attribute-deferred"] = (_, _, _, deferred) =>
                        NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction>(
                                deferred,
                                "DeferredSetterFunction")
                            .Fail(new InvalidOperationException("inline boom")),
                });
            var node = new NeoAttributeNSProperty(client, property, null);
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
                out NSPropertyAttribute property,
                baseSetter: SetterCallingDeferredThenWritingTarget());
            NeoDeferredFunction? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["attribute-deferred"] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction>(
                                deferred,
                                "DeferredSetterFunction"),
                });
            var node = new NeoAttributeNSProperty(client, property, null);
            LogAssert.Expect(
                LogType.Warning,
                new Regex(
                    "Computed.*attribute-property.*DeferredSetterFunction.*attribute-deferred.*did not call Complete/Fail inline",
                    RegexOptions.Singleline));

            NSSetterResult result = node.Set("value-receiver", 29);

            Assert.IsTrue(result.ok, result.error);
            Assert.IsTrue(result.pending);
            Assert.IsNotNull(pending);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberAttributeValue? before));
            Assert.AreEqual(0, before!.value);

            pending!.Complete();

            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "value-target",
                out NumberAttributeValue? after));
            Assert.AreEqual(29, after!.value);
        }

        [Test]
        public void Set_NeverCompletingDeferredFunctionWarnsOnceAndStaysPending()
        {
            var client = BuildClient(
                out NSPropertyAttribute property,
                baseSetter: SetterCallingDeferredThenWritingTarget());
            NeoDeferredFunction? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["attribute-deferred"] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction>(
                                deferred,
                                "DeferredSetterFunction"),
                });
            var node = new NeoAttributeNSProperty(client, property, null);
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
                out NSPropertyAttribute property,
                baseSetter: SetterCallingDeferredThenWritingTarget());
            NeoDeferredFunction? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["attribute-deferred"] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction>(
                                deferred,
                                "DeferredSetterFunction"),
                });
            var node = new NeoAttributeNSProperty(client, property, null);
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
                out NumberAttributeValue? target));
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
                out NSPropertyAttribute property,
                baseSetter: setter);
            NeoDeferredFunction? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["attribute-deferred"] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction>(
                                deferred,
                                "DeferredSetterFunction"),
                });
            var node = new NeoAttributeNSProperty(client, property, null);
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
                out NSPropertyAttribute property,
                baseSetter: SetterCallingCaptureFunction(),
                propertyType: typeInfo);
            client.RegisterNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    ["attribute-capture"] = (_, _, args) =>
                    {
                        captured = args[0];
                        return null;
                    },
                });
            var node = new NeoAttributeNSProperty(client, property, null);

            NSSetterResult result = node.Set("value-receiver", input);

            Assert.IsTrue(result.ok, result.error);
            Assert.IsFalse(result.pending);
            assertCaptured(captured);
        }

        private static NeoClient BuildClient(
            out NSPropertyAttribute baseProperty,
            FunctionWithReturnType? baseSetter = null,
            FunctionWithReturnType? derivedSetter = null,
            TypeInfo? propertyType = null)
        {
            baseSetter ??= SetterWritingTarget(ValueVariable());
            propertyType ??= IntType();
            var rootAssets = CustomAttribute(
                "attribute-root-assets",
                "Assets",
                "type-root",
                "value-assets");
            var rootSave = CustomAttribute(
                "attribute-root-save",
                "Save",
                "type-root",
                "value-save",
                "save");
            var rootSession = CustomAttribute(
                "attribute-root-session",
                "Session",
                "type-root",
                "value-session",
                "session");
            var receiverAttribute = CustomAttribute(
                "attribute-receiver-value",
                "Receiver",
                "type-receiver",
                "value-receiver");
            var targetAttribute = new IntAttribute
            {
                id = "attribute-target",
                projectId = "project-setter",
                name = "Target",
                type = AttributeType.Int,
                valueId = "value-target",
                storage = "save",
                createdAt = "x",
                updatedAt = "x",
            };
            baseProperty = Property(
                "attribute-property",
                "Computed",
                baseSetter,
                returnTypeInfo: propertyType);
            var derivedProperty = Property(
                "attribute-derived-property",
                "Computed",
                derivedSetter,
                baseProperty.id,
                propertyType);
            var captureFunction = new FunctionAttribute
            {
                id = "attribute-capture",
                projectId = "project-setter",
                name = "CaptureSetterValue",
                type = AttributeType.Function,
                returnTypeInfo = new VoidTypeInfo
                {
                    type = AttributeType.Void,
                    required = true,
                },
                argumentTypes = new[]
                {
                    FunctionArgument("value", propertyType),
                },
                deferred = false,
                createdAt = "x",
                updatedAt = "x",
            };
            var deferredFunction = new FunctionAttribute
            {
                id = "attribute-deferred",
                projectId = "project-setter",
                name = "DeferredSetterFunction",
                type = AttributeType.Function,
                returnTypeInfo = new VoidTypeInfo
                {
                    type = AttributeType.Void,
                    required = true,
                },
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                deferred = true,
                createdAt = "x",
                updatedAt = "x",
            };

            var data = new ProjectData
            {
                project = new Project
                {
                    id = "project-setter",
                    name = "Setter Tests",
                    rootAssetsAttributeId = rootAssets.id,
                    rootSaveFileAttributeId = rootSave.id,
                    rootSessionAttributeId = rootSession.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                attributes = new Dictionary<string, JsonAttribute>
                {
                    [rootAssets.id] = rootAssets,
                    [rootSave.id] = rootSave,
                    [rootSession.id] = rootSession,
                    [receiverAttribute.id] = receiverAttribute,
                    [targetAttribute.id] = targetAttribute,
                    [baseProperty.id] = baseProperty,
                    [derivedProperty.id] = derivedProperty,
                    [captureFunction.id] = captureFunction,
                    [deferredFunction.id] = deferredFunction,
                },
                values = new Dictionary<string, AttributeValue>
                {
                    ["value-assets"] = ObjectValue("value-assets", "type-root"),
                    ["value-save"] = ObjectValue(
                        "value-save",
                        "type-root",
                        ("Target", "value-target")),
                    ["value-session"] = ObjectValue("value-session", "type-root"),
                    ["value-target"] = new NumberAttributeValue
                    {
                        id = "value-target",
                        value = 0,
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["value-receiver"] = ObjectValue("value-receiver", "type-receiver"),
                    ["value-derived-receiver"] = ObjectValue(
                        "value-derived-receiver",
                        "type-derived-receiver"),
                },
                types = new Dictionary<string, CustomType>
                {
                    ["type-root"] = CustomType(
                        "type-root",
                        "Root",
                        ("Target", targetAttribute.id)),
                    ["type-receiver"] = CustomType(
                        "type-receiver",
                        "Receiver",
                        ("Computed", baseProperty.id)),
                    ["type-derived-receiver"] = CustomType(
                        "type-derived-receiver",
                        "DerivedReceiver",
                        ("Computed", derivedProperty.id),
                        extendsTypeId: "type-receiver"),
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
            return NeoTestSaveStack.ClientFromSchema(data);
        }

        private static NSPropertyAttribute Property(
            string id,
            string name,
            FunctionWithReturnType? setter,
            string? extendsAttributeId = null,
            TypeInfo? returnTypeInfo = null)
        {
            returnTypeInfo ??= IntType();
            return new NSPropertyAttribute
            {
                id = id,
                projectId = "project-setter",
                name = name,
                type = AttributeType.NSProperty,
                code = "return 0;",
                getter = GetterFunction(),
                returnTypeInfo = returnTypeInfo,
                setterCode = setter is null ? null : "root.Save.Target = value;",
                setter = setter,
                extendsAttributeId = extendsAttributeId,
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
                typeId = (typeInfo as CustomTypeInfo)?.typeId,
                interfaceId = (typeInfo as InterfaceTypeInfo)?.interfaceId,
                enumId = (typeInfo as EnumTypeInfo)?.enumId,
                entryTypeInfo = typeInfo switch
                {
                    CollectionTypeInfo collection => collection.entryTypeInfo,
                    LookupTypeInfo lookup => lookup.entryTypeInfo,
                    _ => null,
                },
                collectionAttributeId = (typeInfo as LookupTypeInfo)?.collectionAttributeId,
                collectionValueId = (typeInfo as LookupTypeInfo)?.collectionValueId,
                ownerTypeId = (typeInfo as GenericTypeInfo)?.ownerTypeId,
                genericParamId = (typeInfo as GenericTypeInfo)?.genericParamId,
                typeArguments = (typeInfo as CustomTypeInfo)?.typeArguments,
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
                    attributeId = "attribute-capture",
                    thisPointer = ThisVariable(),
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
                attributeId = "attribute-deferred",
                thisPointer = ThisVariable(),
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
                    type = AttributeType.Null,
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
            type = AttributeType.Int,
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
                    type = AttributeType.String,
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

        private static CustomAttribute CustomAttribute(
            string id,
            string name,
            string typeId,
            string valueId,
            string? storage = null)
        {
            return new CustomAttribute
            {
                id = id,
                projectId = "project-setter",
                name = name,
                type = AttributeType.Custom,
                customTypeId = typeId,
                valueId = valueId,
                storage = storage,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static CustomType CustomType(
            string id,
            string name,
            (string key, string attributeId) schema,
            string? extendsTypeId = null)
        {
            return new CustomType
            {
                id = id,
                projectId = "project-setter",
                name = name,
                schema = new Dictionary<string, string>
                {
                    [schema.key] = schema.attributeId,
                },
                extendsTypeId = extendsTypeId,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static ObjectAttributeValue ObjectValue(
            string id,
            string typeId,
            params (string key, string valueId)[] entries)
        {
            var value = new Dictionary<string, string>();
            foreach (var entry in entries) value[entry.key] = entry.valueId;
            return new ObjectAttributeValue
            {
                id = id,
                typeId = typeId,
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
                ["Assets"] = client.assets.value is ObjectAttributeValue assets
                    ? NSGetterEvaluator.UnwrapRow(
                        assets,
                        ctx,
                        NeoValueOwnership.Asset)
                    : null,
                ["Save"] = client.save.value is ObjectAttributeValue save
                    ? NSGetterEvaluator.UnwrapRow(
                        save,
                        ctx,
                        NeoValueOwnership.Save)
                    : null,
                ["Session"] = client.session.value is ObjectAttributeValue session
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
