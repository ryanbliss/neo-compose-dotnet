// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonAttribute = NeoCompose.Runtime.Json.Attribute;

namespace NeoCompose.Tests
{
    public class NSFunctionRuntimeTests
    {
        [Test]
        public void AttributeDto_UsesOrdinal23AndGeneralFunctionCallIr()
        {
            const string json = @"{
                'id':'fn','projectId':'project-function','name':'Compute','type':23,'isStatic':false,
                'code':'return RequiredLevel;','returnTypeInfo':{'type':2,'required':true},
                'argumentTypes':[{'name':'RequiredLevel','type':2,'required':true}],
                'deferred':false,'createdAt':'x','updatedAt':'x',
                'action':{
                    'parameters':[
                        {'id':'__this__','typeInfo':{'type':7,'required':true,'typeId':'receiver-type'},'pointer':{'type':'variable','variableId':'__this__'}},
                        {'id':'__root__','typeInfo':{'type':7,'required':true,'typeId':'root-type'},'pointer':{'type':'variable','variableId':'__root__'}},
                        {'id':'__arg_0__','typeInfo':{'type':2,'required':true},'pointer':{'type':'variable','variableId':'__arg_0__'}}
                    ],
                    'instructions':[{'type':'return','pointer':{'type':'callFunction','attributeId':'other','thisPointer':{'type':'variable','variableId':'__this__'},'args':[],'callSiteId':'call-0'}}],
                    'typeInfo':{'type':2,'required':true}
                }
            }";

            JsonAttribute attribute = JsonConvert.DeserializeObject<JsonAttribute>(json)!;

            var function = (NSFunctionAttribute)attribute;
            Assert.AreEqual((AttributeType)23, function.type);
            Assert.AreEqual("RequiredLevel", function.argumentTypes[0].name);
            var call = (CallFunctionPointer)((ReturnInstruction)function.action.instructions[0]).pointer!;
            Assert.AreEqual("call-0", call.callSiteId);
            StringAssert.Contains("\"type\":23", JsonConvert.SerializeObject(attribute));
        }

        [Test]
        public void Invoke_BindsTypedArgumentsAndReturnsValue()
        {
            FunctionArgumentTypeInfo argument = Argument("RequiredLevel", AttributeType.Int);
            NSFunctionAttribute function = ScriptFunction(
                "fn-identity",
                "Identity",
                deferred: false,
                IntType(),
                new[] { argument },
                Action(
                    IntType(),
                    new[] { argument },
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = Variable("__arg_0__"),
                    }));
            NeoClient client = BuildClient(
                new[] { function },
                ReceiverType(("Identity", function.id)));
            var node = new NeoAttributeNSFunction(client, function, null);

            object? result = node.Invoke("receiver-value", new object?[] { 9 });

            Assert.AreEqual(9, result);
            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(
                () => node.Invoke("receiver-value", Array.Empty<object?>()))!;
            StringAssert.Contains("expects 1 arguments", error.Message);
            StringAssert.Contains("stale/corrupt", error.Message);

            error = Assert.Throws<NSGetterRuntimeError>(
                () => node.Invoke("receiver-value", new object?[] { "nine" }))!;
            StringAssert.Contains("argument 0 'RequiredLevel'", error.Message);
            StringAssert.Contains("declared Int", error.Message);
            StringAssert.Contains("stale/corrupt", error.Message);
        }

        [Test]
        public void Invoke_RejectsNonFiniteFloatAndIntArgumentsAndReturns()
        {
            FunctionArgumentTypeInfo floatArgument = Argument(
                "FloatValue",
                AttributeType.Float);
            NSFunctionAttribute floatArgumentFunction = ScriptFunction(
                "fn-finite-float-argument",
                "FiniteFloatArgument",
                deferred: false,
                IntType(),
                new[] { floatArgument },
                Action(
                    IntType(),
                    new[] { floatArgument },
                    Return(Number(1))));
            FunctionArgumentTypeInfo intArgument = Argument(
                "IntValue",
                AttributeType.Int);
            NSFunctionAttribute intArgumentFunction = ScriptFunction(
                "fn-finite-int-argument",
                "FiniteIntArgument",
                deferred: false,
                IntType(),
                new[] { intArgument },
                Action(
                    IntType(),
                    new[] { intArgument },
                    Return(Number(1))));

            double[] nonFinite =
            {
                double.NaN,
                double.PositiveInfinity,
                double.NegativeInfinity,
            };
            var functions = new List<JsonAttribute>
            {
                floatArgumentFunction,
                intArgumentFunction,
            };
            var members = new List<(string key, string attributeId)>
            {
                (floatArgumentFunction.name, floatArgumentFunction.id),
                (intArgumentFunction.name, intArgumentFunction.id),
            };
            var floatReturns = new List<NSFunctionAttribute>();
            var intReturns = new List<NSFunctionAttribute>();
            for (int i = 0; i < nonFinite.Length; i++)
            {
                NSFunctionAttribute floatReturn = ScriptFunction(
                    $"fn-non-finite-float-return-{i}",
                    $"NonFiniteFloatReturn{i}",
                    deferred: false,
                    FloatType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Action(
                        FloatType(),
                        Array.Empty<FunctionArgumentTypeInfo>(),
                        Return(Floating(nonFinite[i], FloatType()))));
                NSFunctionAttribute intReturn = ScriptFunction(
                    $"fn-non-finite-int-return-{i}",
                    $"NonFiniteIntReturn{i}",
                    deferred: false,
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Action(
                        IntType(),
                        Array.Empty<FunctionArgumentTypeInfo>(),
                        Return(Floating(nonFinite[i], IntType()))));
                functions.Add(floatReturn);
                functions.Add(intReturn);
                members.Add((floatReturn.name, floatReturn.id));
                members.Add((intReturn.name, intReturn.id));
                floatReturns.Add(floatReturn);
                intReturns.Add(intReturn);
            }
            NeoClient client = BuildClient(
                functions.ToArray(),
                ReceiverType(members.ToArray()));

            foreach (double value in nonFinite)
            {
                Assert.Throws<NSGetterRuntimeError>(() =>
                    new NeoAttributeNSFunction(
                        client,
                        floatArgumentFunction,
                        null).Invoke(
                            "receiver-value",
                            new object?[] { value }));
                Assert.Throws<NSGetterRuntimeError>(() =>
                    new NeoAttributeNSFunction(
                        client,
                        intArgumentFunction,
                        null).Invoke(
                            "receiver-value",
                            new object?[] { value }));
            }
            foreach (NSFunctionAttribute function in floatReturns)
            {
                Assert.Throws<InvalidOperationException>(() =>
                    new NeoAttributeNSFunction(client, function, null).Invoke(
                        "receiver-value",
                        Array.Empty<object?>()));
            }
            foreach (NSFunctionAttribute function in intReturns)
            {
                Assert.Throws<InvalidOperationException>(() =>
                    new NeoAttributeNSFunction(client, function, null).Invoke(
                        "receiver-value",
                        Array.Empty<object?>()));
            }
        }

        [Test]
        public void Invoke_NormalizesEnumOptionsToEvaluatorWireShape()
        {
            const string enumId = "enum-required-level";
            var returnType = new EnumTypeInfo
            {
                type = AttributeType.Enum,
                required = true,
                enumId = enumId,
            };
            FunctionArgumentTypeInfo argument = Argument(
                "RequiredLevel",
                AttributeType.Enum);
            argument.enumId = enumId;
            NSFunctionAttribute function = ScriptFunction(
                "fn-enum-identity",
                "EnumIdentity",
                deferred: false,
                returnType,
                new[] { argument },
                Action(
                    returnType,
                    new[] { argument },
                    Return(Variable("__arg_0__"))));
            NeoClient client = BuildClient(
                new JsonAttribute[] { function },
                ReceiverType(("EnumIdentity", function.id)));
            var node = new NeoAttributeNSFunction(client, function, null);

            object? result = node.Invoke(
                "receiver-value",
                new object?[] { new TestEnumOption("level-3") });

            Assert.IsInstanceOf<object?[]>(result);
            CollectionAssert.AreEqual(new[] { "level-3" }, (object?[])result!);
        }

        [Test]
        public void Invoke_NormalizesDialogueReferenceArgumentsToExactWireShapes()
        {
            var dialogueType = new PrimitiveTypeInfo
            {
                type = AttributeType.DialogueLookup,
                required = true,
            };
            FunctionArgumentTypeInfo dialogueArgument = Argument(
                "Dialogue",
                AttributeType.DialogueLookup);
            NSFunctionAttribute singleFunction = ScriptFunction(
                "fn-dialogue-identity",
                "DialogueIdentity",
                deferred: false,
                dialogueType,
                new[] { dialogueArgument },
                Action(
                    dialogueType,
                    new[] { dialogueArgument },
                    Return(Variable("__arg_0__"))));

            var stringType = new PrimitiveTypeInfo
            {
                type = AttributeType.String,
                required = true,
            };
            var dialogueListType = new CollectionTypeInfo
            {
                type = AttributeType.List,
                required = true,
                entryTypeInfo = stringType,
            };
            FunctionArgumentTypeInfo dialogueListArgument = Argument(
                "Dialogues",
                AttributeType.List);
            dialogueListArgument.entryTypeInfo = stringType;
            NSFunctionAttribute listFunction = ScriptFunction(
                "fn-dialogue-list-identity",
                "DialogueListIdentity",
                deferred: false,
                dialogueListType,
                new[] { dialogueListArgument },
                Action(
                    dialogueListType,
                    new[] { dialogueListArgument },
                    Return(Variable("__arg_0__"))));

            NeoClient client = BuildClient(
                new JsonAttribute[] { singleFunction, listFunction },
                ReceiverType(
                    ("DialogueIdentity", singleFunction.id),
                    ("DialogueListIdentity", listFunction.id)));

            object? singleResult = new NeoAttributeNSFunction(
                client,
                singleFunction,
                null).Invoke(
                    "receiver-value",
                    new object?[] { new NeoDialogueReference("dialogue-1") });
            CollectionAssert.AreEqual(
                new[] { "dialogue-1" },
                (object?[])singleResult!);

            object? listResult = new NeoAttributeNSFunction(
                client,
                listFunction,
                null).Invoke(
                    "receiver-value",
                    new object?[]
                    {
                        new[]
                        {
                            new NeoDialogueReference("dialogue-1"),
                            new NeoDialogueReference("dialogue-2"),
                        },
                    });
            CollectionAssert.AreEqual(
                new[] { "dialogue-1", "dialogue-2" },
                (object?[])listResult!);

            Assert.Throws<NSGetterRuntimeError>(() =>
                new NeoAttributeNSFunction(client, singleFunction, null).Invoke(
                    "receiver-value",
                    new object?[] { "dialogue-1" }));
            Assert.Throws<NSGetterRuntimeError>(() =>
                new NeoAttributeNSFunction(client, singleFunction, null).Invoke(
                    "receiver-value",
                    new object?[]
                    {
                        new[]
                        {
                            new NeoDialogueReference("dialogue-1"),
                            new NeoDialogueReference("dialogue-2"),
                        },
                    }));

            FunctionAttribute deserialized =
                JsonConvert.DeserializeObject<FunctionAttribute>(
                    "{'type':13,'isStatic':false,'returnTypeInfo':{'type':18,'required':true}}")!;
            Assert.AreEqual(
                AttributeType.DialogueLookup,
                deserialized.returnTypeInfo.type);
        }

        [Test]
        public void Invoke_MarshalsReceiverGenericDecimalReturn()
        {
            const string genericTypeId = "generic-decimal-receiver-type";
            const string genericParamId = "generic-decimal-receiver-param";
            var returnType = new GenericTypeInfo
            {
                type = AttributeType.Generic,
                required = true,
                ownerTypeId = genericTypeId,
                genericParamId = genericParamId,
            };
            NSFunctionAttribute function = ScriptFunction(
                "fn-generic-decimal-return",
                "GenericDecimalReturn",
                deferred: false,
                returnType,
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    returnType,
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Number(7))));
            var binding = new DecimalAttribute
            {
                id = "attr-generic-decimal-binding",
                projectId = ProjectId,
                name = "Generic Decimal Binding",
                type = AttributeType.Decimal,
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            var genericType = new CustomType
            {
                id = genericTypeId,
                projectId = ProjectId,
                name = "GenericDecimalReceiver",
                schema = new Dictionary<string, string>
                {
                    [function.name] = function.id,
                },
                genericParams = new List<GenericParamDeclaration>
                {
                    new() { id = genericParamId, name = "T" },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            var concreteType = new CustomType
            {
                id = "receiver-type",
                projectId = ProjectId,
                name = "ConcreteDecimalReceiver",
                schema = new Dictionary<string, string>(),
                extendsTypeId = genericType.id,
                extendsGenericBindings = new Dictionary<string, GenericBinding>
                {
                    [genericParamId] = new()
                    {
                        kind = NeoGenericBindingKinds.Attribute,
                        attributeId = binding.id,
                    },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            NeoClient client = BuildClient(
                new JsonAttribute[] { function, binding },
                concreteType,
                new[] { genericType });

            object? result = new NeoAttributeNSFunction(
                client,
                function,
                null).Invoke(
                    "receiver-value",
                    Array.Empty<object?>());

            Assert.AreEqual("7", result);
        }

        [Test]
        public void Invoke_RejectsWrongNominalCustomAndNestedListReturnValues()
        {
            var expectedType = new CustomType
            {
                id = "expected-return-type",
                projectId = ProjectId,
                name = "ExpectedReturn",
                schema = new Dictionary<string, string>(),
                createdAt = "x",
                updatedAt = "x",
            };
            var customReturnType = new CustomTypeInfo
            {
                type = AttributeType.Custom,
                required = true,
                typeId = expectedType.id,
            };
            NSFunctionAttribute wrongCustom = ScriptFunction(
                "fn-wrong-custom-return",
                "WrongCustomReturn",
                deferred: false,
                customReturnType,
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    customReturnType,
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Variable("__this__"))));
            var listReturnType = new CollectionTypeInfo
            {
                type = AttributeType.List,
                required = true,
                entryTypeInfo = IntType(),
            };
            NSFunctionAttribute wrongList = ScriptFunction(
                "fn-wrong-list-return",
                "WrongListReturn",
                deferred: false,
                listReturnType,
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    listReturnType,
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(new ValuePointer
                    {
                        type = PointerKind.Value,
                        value = new Value
                        {
                            typeInfo = listReturnType,
                            value = new JArray("not-an-int"),
                        },
                    })));
            NeoClient client = BuildClient(
                new JsonAttribute[] { wrongCustom, wrongList },
                ReceiverType(
                    (wrongCustom.name, wrongCustom.id),
                    (wrongList.name, wrongList.id)),
                new[] { expectedType });

            InvalidOperationException customError =
                Assert.Throws<InvalidOperationException>(() =>
                    new NeoAttributeNSFunction(
                        client,
                        wrongCustom,
                        null).Invoke(
                            "receiver-value",
                            Array.Empty<object?>()))!;
            StringAssert.Contains("expected-return-type", customError.Message);
            Assert.Throws<InvalidOperationException>(() =>
                new NeoAttributeNSFunction(
                    client,
                    wrongList,
                    null).Invoke(
                        "receiver-value",
                        Array.Empty<object?>()));
        }

        [Test]
        public void Invoke_SubstitutesGenericSignatureFromConcreteReceiver()
        {
            const string genericTypeId = "generic-receiver-type";
            const string genericParamId = "generic-receiver-param";
            const string enumId = "enum-generic-level";
            var returnType = new GenericTypeInfo
            {
                type = AttributeType.Generic,
                required = true,
                ownerTypeId = genericTypeId,
                genericParamId = genericParamId,
            };
            var argument = new FunctionArgumentTypeInfo
            {
                name = "Value",
                type = AttributeType.Generic,
                required = true,
                ownerTypeId = genericTypeId,
                genericParamId = genericParamId,
            };
            NSFunctionAttribute function = ScriptFunction(
                "fn-generic-identity",
                "GenericIdentity",
                deferred: false,
                returnType,
                new[] { argument },
                Action(
                    returnType,
                    new[] { argument },
                    Return(Variable("__arg_0__"))));
            var binding = new EnumAttribute
            {
                id = "attr-generic-enum-binding",
                projectId = ProjectId,
                name = "Generic Enum Binding",
                type = AttributeType.Enum,
                enumId = enumId,
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            var genericType = new CustomType
            {
                id = genericTypeId,
                projectId = ProjectId,
                name = "GenericReceiver",
                schema = new Dictionary<string, string>
                {
                    ["GenericIdentity"] = function.id,
                },
                genericParams = new List<GenericParamDeclaration>
                {
                    new()
                    {
                        id = genericParamId,
                        name = "T",
                    },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            var concreteType = new CustomType
            {
                id = "receiver-type",
                projectId = ProjectId,
                name = "ConcreteReceiver",
                schema = new Dictionary<string, string>(),
                extendsTypeId = genericType.id,
                extendsGenericBindings = new Dictionary<string, GenericBinding>
                {
                    [genericParamId] = new()
                    {
                        kind = NeoGenericBindingKinds.Attribute,
                        attributeId = binding.id,
                    },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            NeoClient client = BuildClient(
                new JsonAttribute[] { function, binding },
                concreteType,
                new[] { genericType });
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: NeoValueOwnership.Asset);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Asset,
                "receiver-value",
                out AttributeValue? receiverRow));
            object receiver = NSGetterEvaluator.UnwrapRow(
                receiverRow!,
                ctx,
                NeoValueOwnership.Asset)!;
            NeoResolvedNSFunction resolvedFunction =
                NeoNSFunctionRuntime.ResolveSignature(client, function.id);
            IReadOnlyDictionary<string, NeoGenericEnvEntry> firstEnv =
                NeoNSFunctionRuntime.ResolveReceiverGenericEnv(
                    client,
                    receiver,
                    ctx,
                    resolvedFunction);
            IReadOnlyDictionary<string, NeoGenericEnvEntry> secondEnv =
                NeoNSFunctionRuntime.ResolveReceiverGenericEnv(
                    client,
                    receiver,
                    ctx,
                    resolvedFunction);
            Assert.AreSame(firstEnv, secondEnv);
            Assert.AreEqual(1, ctx.genericEnvironmentCache.Count);
            var node = new NeoAttributeNSFunction(client, function, null);

            object? result = node.Invoke(
                "receiver-value",
                new object?[] { new TestEnumOption("generic-level-2") });

            Assert.IsInstanceOf<object?[]>(result);
            CollectionAssert.AreEqual(
                new[] { "generic-level-2" },
                (object?[])result!);
        }

        [Test]
        public void GenericSignatureSubstitution_ClosesConstructedCustomAndNestedCollectionTypes()
        {
            const string functionParamId = "function-param";
            const string forwardedParamId = "forwarded-param";
            const string boxParamId = "box-param";
            const string enumId = "enum-constructed-generic";
            var enumBinding = new EnumAttribute
            {
                id = "attr-constructed-enum-binding",
                projectId = ProjectId,
                name = "Constructed Enum Binding",
                type = AttributeType.Enum,
                enumId = enumId,
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            var customBinding = new CustomAttribute
            {
                id = "attr-constructed-custom-binding",
                projectId = ProjectId,
                name = "Constructed Box Binding",
                type = AttributeType.Custom,
                customTypeId = "box-type",
                customTypeArguments = new Dictionary<string, GenericBinding>
                {
                    [boxParamId] = new GenericBinding
                    {
                        kind = NeoGenericBindingKinds.Generic,
                        genericParamId = forwardedParamId,
                    },
                },
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            var boxType = new CustomType
            {
                id = "box-type",
                projectId = ProjectId,
                name = "Box",
                schema = new Dictionary<string, string>(),
                genericParams = new List<GenericParamDeclaration>
                {
                    new() { id = boxParamId, name = "TValue" },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            NeoClient client = BuildClient(
                new JsonAttribute[] { enumBinding, customBinding },
                ReceiverType(),
                new[] { boxType });
            var env = new Dictionary<string, NeoGenericEnvEntry>
            {
                [functionParamId] = NeoGenericEnvEntry.Bound(customBinding.id),
                [forwardedParamId] = NeoGenericEnvEntry.Bound(enumBinding.id),
            };

            var direct = (CustomTypeInfo)NeoNSFunctionRuntime.ResolveInvocationTypeInfo(
                client,
                new GenericTypeInfo
                {
                    type = AttributeType.Generic,
                    required = true,
                    genericParamId = functionParamId,
                },
                env);

            Assert.AreEqual(customBinding.customTypeId, direct.typeId);
            Assert.IsNotNull(direct.typeArguments);
            var directEnum = (EnumTypeInfo)direct.typeArguments![boxParamId];
            Assert.AreEqual(enumId, directEnum.enumId);

            var nested = (CollectionTypeInfo)NeoNSFunctionRuntime.ResolveInvocationTypeInfo(
                client,
                new CollectionTypeInfo
                {
                    type = AttributeType.List,
                    required = true,
                    entryTypeInfo = new CustomTypeInfo
                    {
                        type = AttributeType.Custom,
                        required = true,
                        typeId = boxType.id,
                        typeArguments = new Dictionary<string, TypeInfo>
                        {
                            [boxParamId] = new GenericTypeInfo
                            {
                                type = AttributeType.Generic,
                                required = true,
                                genericParamId = forwardedParamId,
                            },
                        },
                    },
                },
                env);

            var nestedCustom = (CustomTypeInfo)nested.entryTypeInfo;
            var nestedEnum = (EnumTypeInfo)nestedCustom.typeArguments![boxParamId];
            Assert.AreEqual(enumId, nestedEnum.enumId);
        }

        [Test]
        public void GenericSignatureSubstitution_RejectsConstructedBindingCycles()
        {
            const string functionParamId = "function-param-cycle";
            const string boxParamId = "box-param-cycle";
            var cyclicBinding = new CustomAttribute
            {
                id = "attr-cyclic-custom-binding",
                projectId = ProjectId,
                name = "Cyclic Box Binding",
                type = AttributeType.Custom,
                customTypeId = "cyclic-box-type",
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            cyclicBinding.customTypeArguments = new Dictionary<string, GenericBinding>
            {
                [boxParamId] = new GenericBinding
                {
                    kind = NeoGenericBindingKinds.Attribute,
                    attributeId = cyclicBinding.id,
                },
            };
            NeoClient client = BuildClient(
                new JsonAttribute[] { cyclicBinding },
                ReceiverType());

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NeoNSFunctionRuntime.ResolveInvocationTypeInfo(
                    client,
                    new GenericTypeInfo
                    {
                        type = AttributeType.Generic,
                        required = true,
                        genericParamId = functionParamId,
                    },
                    new Dictionary<string, NeoGenericEnvEntry>
                    {
                        [functionParamId] = NeoGenericEnvEntry.Bound(
                            cyclicBinding.id),
                    }))!;

            StringAssert.Contains("binding attribute cycle", error.Message);
            StringAssert.Contains(cyclicBinding.id, error.Message);
        }

        [Test]
        public void Invoke_NormalizesAssetDtosToEvaluatorWireShapes()
        {
            var spriteType = new PrimitiveTypeInfo
            {
                type = AttributeType.Sprite,
                required = true,
            };
            FunctionArgumentTypeInfo spriteArgument = Argument(
                "Portrait",
                AttributeType.Sprite);
            NSFunctionAttribute spriteFunction = ScriptFunction(
                "fn-sprite-identity",
                "SpriteIdentity",
                deferred: false,
                spriteType,
                new[] { spriteArgument },
                Action(
                    spriteType,
                    new[] { spriteArgument },
                    Return(Variable("__arg_0__"))));

            var audioType = new PrimitiveTypeInfo
            {
                type = AttributeType.Audio,
                required = true,
            };
            FunctionArgumentTypeInfo audioArgument = Argument(
                "Voice",
                AttributeType.Audio);
            NSFunctionAttribute audioFunction = ScriptFunction(
                "fn-audio-identity",
                "AudioIdentity",
                deferred: false,
                audioType,
                new[] { audioArgument },
                Action(
                    audioType,
                    new[] { audioArgument },
                    Return(Variable("__arg_0__"))));

            NeoClient client = BuildClient(
                new JsonAttribute[] { spriteFunction, audioFunction },
                ReceiverType(
                    ("SpriteIdentity", spriteFunction.id),
                    ("AudioIdentity", audioFunction.id)));

            object? spriteResult = new NeoAttributeNSFunction(
                client,
                spriteFunction,
                null).Invoke(
                    "receiver-value",
                    new object?[]
                    {
                        new SpriteValue
                        {
                            fileId = "portrait-file",
                            sliceIndex = 2,
                        },
                    });
            object? audioResult = new NeoAttributeNSFunction(
                client,
                audioFunction,
                null).Invoke(
                    "receiver-value",
                    new object?[]
                    {
                        new FileValue { fileId = "voice-file" },
                    });

            Assert.IsInstanceOf<IDictionary<string, object?>>(spriteResult);
            var spriteWire = (IDictionary<string, object?>)spriteResult!;
            Assert.AreEqual("portrait-file", spriteWire["fileId"]);
            Assert.AreEqual(2, spriteWire["sliceIndex"]);

            Assert.IsInstanceOf<IDictionary<string, object?>>(audioResult);
            var audioWire = (IDictionary<string, object?>)audioResult!;
            Assert.AreEqual("voice-file", audioWire["fileId"]);
        }

        [Test]
        public void Invoke_NestedImmediateNSFunctionDispatchesThroughGeneralCallPointer()
        {
            NSFunctionAttribute inner = ScriptFunction(
                "fn-inner",
                "Inner",
                deferred: false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Number(7))));
            NSFunctionAttribute outer = ScriptFunction(
                "fn-outer",
                "Outer",
                deferred: false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Call(inner.id, "nested-inner"))));
            NeoClient client = BuildClient(
                new JsonAttribute[] { inner, outer },
                ReceiverType(("Inner", inner.id), ("Outer", outer.id)));
            var node = new NeoAttributeNSFunction(client, outer, null);

            Assert.AreEqual(7L, Convert.ToInt64(
                node.Invoke("receiver-value", Array.Empty<object?>())));
        }

        [Test]
        public void Invoke_RecursiveNSFunctionStopsAtNamedDepthLimit()
        {
            NSFunctionAttribute recursive = ScriptFunction(
                "fn-recursive",
                "RecurseForever",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Number(0))));
            recursive.action = Action(
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Return(Call(recursive.id, "recursive-call")));
            NeoClient client = BuildClient(
                new JsonAttribute[] { recursive },
                ReceiverType(("RecurseForever", recursive.id)));
            var node = new NeoAttributeNSFunction(client, recursive, null);

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                node.Invoke("receiver-value", Array.Empty<object?>()))!;

            StringAssert.Contains("call depth exceeded 64", error.Message);
            StringAssert.Contains("RecurseForever -> RecurseForever", error.Message);
        }

        [Test]
        public void Invoke_RepeatedCallSiteInCollectionLambdaUsesDistinctDynamicFrames()
        {
            FunctionArgumentTypeInfo nativeArgument = Argument("Value", AttributeType.Int);
            FunctionAttribute native = NativeFunction("fn-map", "MapValue", deferred: false);
            native.argumentTypes = new[] { nativeArgument };
            var listType = new CollectionTypeInfo
            {
                type = AttributeType.List,
                required = true,
                entryTypeInfo = IntType(),
            };
            var lambda = new FunctionWithReturnType
            {
                parameters = new[] { Parameter("entry", IntType()) },
                instructions = new Instruction[]
                {
                    Return(new CallFunctionPointer
                    {
                        type = PointerKind.CallFunction,
                        attributeId = native.id,
                        thisPointer = Variable("__this__"),
                        args = new Pointer[] { Variable("entry") },
                        callSiteId = "map-each",
                    }),
                },
                typeInfo = IntType(),
            };
            var select = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new SelectFunction
                {
                    type = FunctionKind.Select,
                    info = new FunctionCollectionSelectInfo
                    {
                        collectionPointer = new ListLiteralPointer
                        {
                            type = PointerKind.ListLiteral,
                            typeInfo = listType,
                            entries = new Pointer[] { Number(1), Number(2) },
                        },
                        function = lambda,
                    },
                },
            };
            NSFunctionAttribute function = ScriptFunction(
                "fn-select",
                "SelectValues",
                deferred: false,
                listType,
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    listType,
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(select)));
            NeoClient client = BuildClient(
                new JsonAttribute[] { native, function },
                ReceiverType(("MapValue", native.id), ("SelectValues", function.id)));
            int invocationCount = 0;
            client.RegisterNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    [native.id] = (_, _, args) =>
                    {
                        invocationCount++;
                        return args[0];
                    },
                });
            var node = new NeoAttributeNSFunction(client, function, null);

            var result = (object?[])node.Invoke(
                "receiver-value",
                Array.Empty<object?>())!;

            Assert.AreEqual(2, invocationCount);
            Assert.AreEqual(1L, Convert.ToInt64(result[0]));
            Assert.AreEqual(2L, Convert.ToInt64(result[1]));
        }

        [Test]
        public void Invoke_BaseMemberUsesDerivedNSFunctionBodyAndInheritedSignature()
        {
            NSFunctionAttribute baseFunction = ScriptFunction(
                "fn-base",
                "Compute",
                deferred: false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Number(1))));
            NSFunctionAttribute overrideFunction = new()
            {
                id = "fn-derived",
                projectId = ProjectId,
                name = "Compute",
                type = AttributeType.NSFunction,
                code = "return 2;",
                extendsAttributeId = baseFunction.id,
                returnTypeInfo = null!,
                argumentTypes = null!,
                deferred = null,
                action = Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Number(2))),
                createdAt = "x",
                updatedAt = "x",
            };
            var baseType = ReceiverType(("Compute", baseFunction.id));
            var derivedType = ReceiverType(("Compute", overrideFunction.id));
            derivedType.id = "derived-receiver-type";
            derivedType.name = "DerivedReceiver";
            derivedType.extendsTypeId = baseType.id;
            var derivedValue = ObjectValue("derived-receiver-value", derivedType.id);
            NeoClient client = BuildClient(
                new JsonAttribute[] { baseFunction, overrideFunction },
                baseType,
                new[] { derivedType },
                new AttributeValue[] { derivedValue });
            var node = new NeoAttributeNSFunction(client, baseFunction, null);

            Assert.AreEqual(2L, Convert.ToInt64(
                node.Invoke(derivedValue.id, Array.Empty<object?>())));
        }

        [Test]
        public void Invoke_MutationBodyReturnsTheUpdatedSaveValue()
        {
            FunctionArgumentTypeInfo argument = Argument("RequiredLevel", AttributeType.Int);
            NSFunctionAttribute function = ScriptFunction(
                "fn-mutate",
                "SetLevel",
                deferred: false,
                IntType(),
                new[] { argument },
                Action(
                    IntType(),
                    new[] { argument },
                    new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        target = new WriteTarget
                        {
                            pointer = RootLevel(),
                            typeInfo = IntType(),
                            writability = WritabilityKind.Save,
                        },
                        operatorValue = "=",
                        pointer = Variable("__arg_0__"),
                    },
                    Return(RootLevel())));
            NeoClient client = BuildMutationClient(function);
            var node = new NeoAttributeNSFunction(client, function, null);

            object? result = node.Invoke(
                "receiver-value",
                new object?[] { 12 });

            Assert.AreEqual(12L, Convert.ToInt64(result));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "save-level-value",
                out NumberAttributeValue? level));
            Assert.AreEqual(12d, level!.value);
        }

        [Test]
        public void Invoke_ReadYourWritesRefreshesPreviouslyReadMember()
        {
            NSFunctionAttribute function = ScriptFunction(
                "fn-toggle-flag",
                "ToggleFlag",
                deferred: false,
                BoolType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    BoolType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        target = new WriteTarget
                        {
                            pointer = ThisFlag(),
                            typeInfo = BoolType(),
                            writability = WritabilityKind.Save,
                        },
                        operatorValue = "=",
                        pointer = EqualTo(ThisFlag(), Boolean(false)),
                    },
                    Return(ThisFlag())));
            NeoClient client = BuildBooleanMutationClient(function, nested: false);
            var node = new NeoAttributeNSFunction(
                client,
                function,
                null,
                NeoValueOwnership.Save);

            object? result = node.Invoke(
                "root-save-value",
                Array.Empty<object?>());

            Assert.AreEqual(false, result);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "save-flag-value",
                out BoolAttributeValue? flag));
            Assert.AreEqual(false, flag!.value);
        }

        [Test]
        public void Invoke_ReadYourWritesRefreshesNestedCustomMember()
        {
            NSFunctionAttribute function = ScriptFunction(
                "fn-toggle-nested-flag",
                "ToggleNestedFlag",
                deferred: false,
                BoolType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    BoolType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        target = new WriteTarget
                        {
                            pointer = ThisNestedFlag(),
                            typeInfo = BoolType(),
                            writability = WritabilityKind.Save,
                        },
                        operatorValue = "=",
                        pointer = EqualTo(ThisNestedFlag(), Boolean(false)),
                    },
                    Return(ThisNestedFlag())));
            NeoClient client = BuildBooleanMutationClient(function, nested: true);
            var node = new NeoAttributeNSFunction(
                client,
                function,
                null,
                NeoValueOwnership.Save);

            object? result = node.Invoke(
                "root-save-value",
                Array.Empty<object?>());

            Assert.AreEqual(false, result);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "save-flag-value",
                out BoolAttributeValue? flag));
            Assert.AreEqual(false, flag!.value);
        }

        [Test]
        public void InvokeAsync_TwoDeferredCallsResumeLeftToRightExactlyOnce()
        {
            FunctionAttribute native = NativeFunction("fn-native", "Fetch", deferred: true);
            Pointer first = Call(native.id, "fetch-first");
            Pointer second = Call(native.id, "fetch-second");
            NSFunctionAttribute function = ScriptFunction(
                "fn-deferred-script",
                "ComputeLater",
                deferred: true,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Add(first, second))));
            NeoClient client = BuildClient(
                new JsonAttribute[] { native, function },
                ReceiverType(("Fetch", native.id), ("ComputeLater", function.id)));
            int invocationCount = 0;
            NeoDeferredFunction<int>? firstPending = null;
            NeoDeferredFunction<int>? secondPending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [native.id] = (_, _, _, deferred) =>
                    {
                        invocationCount++;
                        var typed = NeoGeneratedTypesSupport.ResolveDeferredFunction<NeoDeferredFunction<int>>(
                            deferred,
                            native.name);
                        if (invocationCount == 1) firstPending = typed;
                        else secondPending = typed;
                    },
                });
            var node = new NeoAttributeNSFunction(client, function, null);

            Task<object?> task = node.InvokeAsync(
                "receiver-value",
                Array.Empty<object?>());

            Assert.IsFalse(task.IsCompleted);
            Assert.AreEqual(1, invocationCount);
            Assert.IsNotNull(firstPending);
            firstPending!.Complete(10);
            Assert.AreEqual(2, invocationCount);
            Assert.IsNotNull(secondPending);
            Assert.IsFalse(task.IsCompleted);
            secondPending!.Complete(20);

            Assert.AreEqual(30L, Convert.ToInt64(task.GetAwaiter().GetResult()));
            Assert.AreEqual(2, invocationCount, "Resuming must not replay either call site.");
        }

        [Test]
        public void InvokeAsync_NestedDeferredNSFunctionResumesOuterTask()
        {
            FunctionAttribute native = NativeFunction("fn-native", "Fetch", deferred: true);
            NSFunctionAttribute inner = ScriptFunction(
                "fn-inner-deferred",
                "InnerDeferred",
                deferred: true,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Call(native.id, "nested-native"))));
            NSFunctionAttribute outer = ScriptFunction(
                "fn-outer-deferred",
                "OuterDeferred",
                deferred: true,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Call(inner.id, "nested-script"))));
            NeoClient client = BuildClient(
                new JsonAttribute[] { native, inner, outer },
                ReceiverType(
                    ("Fetch", native.id),
                    ("InnerDeferred", inner.id),
                    ("OuterDeferred", outer.id)));
            NeoDeferredFunction<int>? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [native.id] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                native.name),
                });
            var node = new NeoAttributeNSFunction(client, outer, null);

            Task<object?> task = node.InvokeAsync(
                "receiver-value",
                Array.Empty<object?>());

            Assert.IsFalse(task.IsCompleted);
            Assert.IsNotNull(pending);
            pending!.Complete(41);
            Assert.AreEqual(41L, Convert.ToInt64(task.GetAwaiter().GetResult()));
        }

        [Test]
        public void InvokeAsync_NestedDeferredNSFunctionInlineCompletionReturnsCompletedTask()
        {
            FunctionAttribute native = NativeFunction("fn-native", "Fetch", deferred: true);
            NSFunctionAttribute inner = ScriptFunction(
                "fn-inner-inline",
                "InnerInline",
                deferred: true,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Call(native.id, "inline-native"))));
            NSFunctionAttribute outer = ScriptFunction(
                "fn-outer-inline",
                "OuterInline",
                deferred: true,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Call(inner.id, "inline-script"))));
            NeoClient client = BuildClient(
                new JsonAttribute[] { native, inner, outer },
                ReceiverType(
                    ("Fetch", native.id),
                    ("InnerInline", inner.id),
                    ("OuterInline", outer.id)));
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [native.id] = (_, _, _, deferred) =>
                        NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                native.name)
                            .Complete(43),
                });
            var node = new NeoAttributeNSFunction(client, outer, null);

            Task<object?> task = node.InvokeAsync(
                "receiver-value",
                Array.Empty<object?>());

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(43L, Convert.ToInt64(task.GetAwaiter().GetResult()));
        }

        [Test]
        public void Invoke_ImmediateNSFunctionRejectsDeferredNativeModeBeforeInvoker()
        {
            FunctionAttribute native = NativeFunction("fn-native", "Fetch", deferred: true);
            NSFunctionAttribute function = ScriptFunction(
                "fn-invalid-mode",
                "InvalidMode",
                deferred: false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Call(native.id, "invalid-deferred-call"))));
            NeoClient client = BuildClient(
                new JsonAttribute[] { native, function },
                ReceiverType(("Fetch", native.id), ("InvalidMode", function.id)));
            int invocationCount = 0;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [native.id] = (_, _, _, _) => invocationCount++,
                });
            var node = new NeoAttributeNSFunction(client, function, null);

            NeoDeferredFunctionRuntimeError error =
                Assert.Throws<NeoDeferredFunctionRuntimeError>(() =>
                    node.Invoke("receiver-value", Array.Empty<object?>()))!;

            StringAssert.Contains("deferred-mode mismatch", error.Message);
            StringAssert.Contains("stale/corrupt", error.Message);
            Assert.AreEqual(0, invocationCount);
        }

        [Test]
        public void DirectDeferredNativeCallRejectsImmediateEffectiveSignature()
        {
            FunctionAttribute native = NativeFunction(
                "fn-immediate-native",
                "ImmediateNative",
                deferred: false);
            NeoClient client = BuildClient(
                new JsonAttribute[] { native },
                ReceiverType(("ImmediateNative", native.id)));
            int invocationCount = 0;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [native.id] = (_, _, _, _) => invocationCount++,
                });

            Task<int> task = client.InvokeDeferredNativeFunction<int>(
                native.id,
                receiver: null,
                args: Array.Empty<object?>());
            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                task.GetAwaiter().GetResult())!;

            StringAssert.Contains("Function 'ImmediateNative'", error.Message);
            StringAssert.Contains("deferred-mode mismatch", error.Message);
            StringAssert.Contains("stale/corrupt", error.Message);
            Assert.AreEqual(0, invocationCount);
        }

        [Test]
        public void InvokeAsync_ClientDisposeCancelsPendingContinuation()
        {
            FunctionAttribute native = NativeFunction("fn-native", "Fetch", deferred: true);
            NSFunctionAttribute function = ScriptFunction(
                "fn-deferred-script",
                "ComputeLater",
                deferred: true,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Call(native.id, "fetch"))));
            NeoClient client = BuildClient(
                new JsonAttribute[] { native, function },
                ReceiverType(("Fetch", native.id), ("ComputeLater", function.id)));
            NeoDeferredFunction<int>? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [native.id] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport.ResolveDeferredFunction<NeoDeferredFunction<int>>(
                            deferred,
                            native.name),
                });
            var node = new NeoAttributeNSFunction(client, function, null);
            Task<object?> task = node.InvokeAsync(
                "receiver-value",
                Array.Empty<object?>());

            client.Dispose();

            Assert.IsNotNull(pending);
            Assert.IsTrue(pending!.CancellationToken.IsCancellationRequested);
            Assert.IsTrue(task.IsCanceled);
            Assert.Throws<ObjectDisposedException>(() => pending.Complete(1));
        }

        [Test]
        public void Construction_RejectsNSFunctionOverrideThatRepeatsSignature()
        {
            NSFunctionAttribute baseFunction = ScriptFunction(
                "fn-base",
                "Compute",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(1))));
            NSFunctionAttribute invalidOverride = ScriptFunction(
                "fn-invalid",
                "Compute",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(2))));
            invalidOverride.extendsAttributeId = baseFunction.id;

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                BuildClient(
                    new JsonAttribute[] { baseFunction, invalidOverride },
                    ReceiverType(("Compute", baseFunction.id))))!;

            StringAssert.Contains("must inherit returnTypeInfo", error.Message);
        }

        [Test]
        public void Construction_RejectsEmptyLocalCode()
        {
            NSFunctionAttribute function = ScriptFunction(
                "fn-empty",
                "Empty",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Number(1))));
            function.code = "   ";

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                BuildClient(
                    new JsonAttribute[] { function },
                    ReceiverType(("Empty", function.id))))!;

            StringAssert.Contains("local code must not be empty", error.Message);
        }

        [Test]
        public void Construction_RejectsStaleStructuredArgumentType()
        {
            FunctionArgumentTypeInfo declared = Argument("Target", AttributeType.Custom);
            declared.typeId = "custom-a";
            FunctionArgumentTypeInfo compiled = Argument("Target", AttributeType.Custom);
            compiled.typeId = "custom-b";
            NSFunctionAttribute function = ScriptFunction(
                "fn-stale-argument",
                "StaleArgument",
                false,
                IntType(),
                new[] { declared },
                Action(
                    IntType(),
                    new[] { compiled },
                    Return(Number(1))));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                BuildClient(
                    new JsonAttribute[] { function },
                    ReceiverType(("StaleArgument", function.id))))!;

            StringAssert.Contains("argument 0 type does not match", error.Message);
        }

        private const string ProjectId = "project-function";

        private static NeoClient BuildClient(
            JsonAttribute[] callables,
            CustomType receiverType,
            CustomType[]? additionalTypes = null,
            AttributeValue[]? additionalValues = null)
        {
            CustomAttribute assets = RootAttribute("root-assets", "Assets", "root-assets-value");
            CustomAttribute save = RootAttribute("root-save", "Save", "root-save-value", "save");
            CustomAttribute session = RootAttribute("root-session", "Session", "root-session-value", "session");
            var attributes = new Dictionary<string, JsonAttribute>
            {
                [assets.id] = assets,
                [save.id] = save,
                [session.id] = session,
            };
            foreach (JsonAttribute callable in callables) attributes[callable.id] = callable;

            var types = new Dictionary<string, CustomType>
            {
                ["root-type"] = new CustomType
                {
                    id = "root-type",
                    projectId = ProjectId,
                    name = "Root",
                    schema = new Dictionary<string, string>(),
                    createdAt = "x",
                    updatedAt = "x",
                },
                [receiverType.id] = receiverType,
            };
            if (additionalTypes is not null)
            {
                foreach (CustomType type in additionalTypes) types[type.id] = type;
            }

            var values = new Dictionary<string, AttributeValue>
            {
                [assets.valueId!] = ObjectValue(assets.valueId!, "root-type"),
                [save.valueId!] = ObjectValue(save.valueId!, "root-type"),
                [session.valueId!] = ObjectValue(session.valueId!, "root-type"),
                ["receiver-value"] = ObjectValue("receiver-value", receiverType.id),
            };
            if (additionalValues is not null)
            {
                foreach (AttributeValue value in additionalValues) values[value.id] = value;
            }

            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "NSFunction Tests",
                    rootAssetsAttributeId = assets.id,
                    rootSaveFileAttributeId = save.id,
                    rootSessionAttributeId = session.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                attributes = attributes,
                values = values,
                types = types,
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            });
        }

        private static NeoClient BuildMutationClient(NSFunctionAttribute function)
        {
            CustomAttribute assets = RootAttribute("root-assets", "Assets", "root-assets-value");
            CustomAttribute save = new()
            {
                id = "root-save",
                projectId = ProjectId,
                name = "Save",
                type = AttributeType.Custom,
                customTypeId = "save-root-type",
                valueId = "root-save-value",
                storage = "save",
                createdAt = "x",
                updatedAt = "x",
            };
            CustomAttribute session = RootAttribute("root-session", "Session", "root-session-value", "session");
            var levelAttribute = new IntAttribute
            {
                id = "save-level",
                projectId = ProjectId,
                name = "Level",
                type = AttributeType.Int,
                valueId = "save-level-value",
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            ObjectAttributeValue saveValue = ObjectValue(save.valueId!, "save-root-type");
            saveValue.value!["Level"] = levelAttribute.valueId!;

            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "NSFunction Mutation Test",
                    rootAssetsAttributeId = assets.id,
                    rootSaveFileAttributeId = save.id,
                    rootSessionAttributeId = session.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                attributes = new Dictionary<string, JsonAttribute>
                {
                    [assets.id] = assets,
                    [save.id] = save,
                    [session.id] = session,
                    [levelAttribute.id] = levelAttribute,
                    [function.id] = function,
                },
                values = new Dictionary<string, AttributeValue>
                {
                    [assets.valueId!] = ObjectValue(assets.valueId!, "root-type"),
                    [save.valueId!] = saveValue,
                    [session.valueId!] = ObjectValue(session.valueId!, "root-type"),
                    ["receiver-value"] = ObjectValue("receiver-value", "receiver-type"),
                    [levelAttribute.valueId!] = new NumberAttributeValue
                    {
                        id = levelAttribute.valueId!,
                        value = 1,
                        createdAt = "x",
                        updatedAt = "x",
                    },
                },
                types = new Dictionary<string, CustomType>
                {
                    ["root-type"] = new CustomType
                    {
                        id = "root-type",
                        projectId = ProjectId,
                        name = "Root",
                        schema = new Dictionary<string, string>(),
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["save-root-type"] = new CustomType
                    {
                        id = "save-root-type",
                        projectId = ProjectId,
                        name = "SaveRoot",
                        schema = new Dictionary<string, string>
                        {
                            ["Level"] = levelAttribute.id,
                        },
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["receiver-type"] = ReceiverType(("SetLevel", function.id)),
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            });
        }

        private static NeoClient BuildBooleanMutationClient(
            NSFunctionAttribute function,
            bool nested)
        {
            CustomAttribute assets = RootAttribute(
                "root-assets",
                "Assets",
                "root-assets-value");
            CustomAttribute save = new()
            {
                id = "root-save",
                projectId = ProjectId,
                name = "Save",
                type = AttributeType.Custom,
                customTypeId = "save-root-type",
                valueId = "root-save-value",
                storage = "save",
                createdAt = "x",
                updatedAt = "x",
            };
            CustomAttribute session = RootAttribute(
                "root-session",
                "Session",
                "root-session-value",
                "session");
            var flagAttribute = new BoolAttribute
            {
                id = "save-flag",
                projectId = ProjectId,
                name = "Flag",
                type = AttributeType.Bool,
                valueId = "save-flag-value",
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            var attributes = new Dictionary<string, JsonAttribute>
            {
                [assets.id] = assets,
                [save.id] = save,
                [session.id] = session,
                [flagAttribute.id] = flagAttribute,
                [function.id] = function,
            };
            var saveSchema = new Dictionary<string, string>
            {
                [function.name] = function.id,
            };
            var values = new Dictionary<string, AttributeValue>
            {
                [assets.valueId!] = ObjectValue(assets.valueId!, "root-type"),
                [save.valueId!] = ObjectValue(save.valueId!, "save-root-type"),
                [session.valueId!] = ObjectValue(session.valueId!, "root-type"),
                [flagAttribute.valueId!] = new BoolAttributeValue
                {
                    id = flagAttribute.valueId!,
                    value = true,
                    createdAt = "x",
                    updatedAt = "x",
                },
            };
            var types = new Dictionary<string, CustomType>
            {
                ["root-type"] = new CustomType
                {
                    id = "root-type",
                    projectId = ProjectId,
                    name = "Root",
                    schema = new Dictionary<string, string>(),
                    createdAt = "x",
                    updatedAt = "x",
                },
            };
            ObjectAttributeValue saveValue = (ObjectAttributeValue)values[save.valueId!];
            if (nested)
            {
                var childAttribute = new CustomAttribute
                {
                    id = "save-child",
                    projectId = ProjectId,
                    name = "Child",
                    type = AttributeType.Custom,
                    customTypeId = "save-child-type",
                    valueId = "save-child-value",
                    required = true,
                    createdAt = "x",
                    updatedAt = "x",
                };
                attributes[childAttribute.id] = childAttribute;
                saveSchema[childAttribute.name] = childAttribute.id;
                saveValue.value![childAttribute.name] = childAttribute.valueId!;
                ObjectAttributeValue childValue = ObjectValue(
                    childAttribute.valueId!,
                    childAttribute.customTypeId);
                childValue.value![flagAttribute.name] = flagAttribute.valueId!;
                values[childValue.id] = childValue;
                types["save-child-type"] = new CustomType
                {
                    id = "save-child-type",
                    projectId = ProjectId,
                    name = "SaveChild",
                    schema = new Dictionary<string, string>
                    {
                        [flagAttribute.name] = flagAttribute.id,
                    },
                    createdAt = "x",
                    updatedAt = "x",
                };
            }
            else
            {
                saveSchema[flagAttribute.name] = flagAttribute.id;
                saveValue.value![flagAttribute.name] = flagAttribute.valueId!;
            }
            types["save-root-type"] = new CustomType
            {
                id = "save-root-type",
                projectId = ProjectId,
                name = "SaveRoot",
                schema = saveSchema,
                createdAt = "x",
                updatedAt = "x",
            };

            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "NSFunction Boolean Mutation Test",
                    rootAssetsAttributeId = assets.id,
                    rootSaveFileAttributeId = save.id,
                    rootSessionAttributeId = session.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                attributes = attributes,
                values = values,
                types = types,
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            });
        }

        private static CustomAttribute RootAttribute(
            string id,
            string name,
            string valueId,
            string? storage = null) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            type = AttributeType.Custom,
            customTypeId = "root-type",
            valueId = valueId,
            storage = storage,
            createdAt = "x",
            updatedAt = "x",
        };

        private static CustomType ReceiverType(
            params (string key, string attributeId)[] members)
        {
            var schema = new Dictionary<string, string>();
            foreach (var member in members) schema[member.key] = member.attributeId;
            return new CustomType
            {
                id = "receiver-type",
                projectId = ProjectId,
                name = "Receiver",
                schema = schema,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static NSFunctionAttribute ScriptFunction(
            string id,
            string name,
            bool deferred,
            TypeInfo returnType,
            FunctionArgumentTypeInfo[] arguments,
            FunctionWithReturnType action) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            type = AttributeType.NSFunction,
            code = "compiled test function",
            returnTypeInfo = returnType,
            argumentTypes = arguments,
            deferred = deferred,
            action = action,
            createdAt = "x",
            updatedAt = "x",
        };

        private static FunctionAttribute NativeFunction(
            string id,
            string name,
            bool deferred) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            type = AttributeType.Function,
            returnTypeInfo = IntType(),
            argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            deferred = deferred,
            createdAt = "x",
            updatedAt = "x",
        };

        private static FunctionWithReturnType Action(
            TypeInfo returnType,
            FunctionArgumentTypeInfo[] arguments,
            params Instruction[] instructions)
        {
            var parameters = new Variable[arguments.Length + 2];
            parameters[0] = Parameter(
                "__this__",
                new CustomTypeInfo
                {
                    type = AttributeType.Custom,
                    required = true,
                    typeId = "receiver-type",
                });
            parameters[1] = Parameter(
                "__root__",
                new CustomTypeInfo
                {
                    type = AttributeType.Custom,
                    required = true,
                    typeId = "root-type",
                });
            for (int i = 0; i < arguments.Length; i++)
            {
                parameters[i + 2] = Parameter($"__arg_{i}__", arguments[i]);
            }
            return new FunctionWithReturnType
            {
                parameters = parameters,
                instructions = instructions,
                typeInfo = returnType,
            };
        }

        private static Variable Parameter(string id, TypeInfo typeInfo) => new()
        {
            id = id,
            typeInfo = typeInfo,
            pointer = Variable(id),
        };

        private static FunctionArgumentTypeInfo Argument(
            string name,
            AttributeType type,
            bool required = true) => new()
        {
            name = name,
            type = type,
            required = required,
        };

        private static PrimitiveTypeInfo IntType() => new()
        {
            type = AttributeType.Int,
            required = true,
        };

        private static PrimitiveTypeInfo BoolType() => new()
        {
            type = AttributeType.Bool,
            required = true,
        };

        private static PrimitiveTypeInfo FloatType() => new()
        {
            type = AttributeType.Float,
            required = true,
        };

        private static ReturnInstruction Return(Pointer pointer) => new()
        {
            type = InstructionKind.Return,
            pointer = pointer,
        };

        private static VariablePointer Variable(string id) => new()
        {
            type = PointerKind.Variable,
            variableId = id,
        };

        private static ValuePointer Number(int value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = IntType(),
                value = JToken.FromObject(value),
            },
        };

        private static ValuePointer Boolean(bool value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = BoolType(),
                value = JToken.FromObject(value),
            },
        };

        private static ValuePointer Floating(double value, TypeInfo typeInfo) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = typeInfo,
                value = new JValue(value),
            },
        };

        private static ValuePointer Text(string value) => new()
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

        private static KeyOfPointer Key(Pointer receiver, string key) => new()
        {
            type = PointerKind.KeyOf,
            keyOf = new KeyOf
            {
                pointer = receiver,
                key = Text(key),
            },
        };

        private static KeyOfPointer RootLevel() =>
            Key(Key(Variable("__root__"), "Save"), "Level");

        private static KeyOfPointer ThisFlag() =>
            Key(Variable("__this__"), "Flag");

        private static KeyOfPointer ThisNestedFlag() =>
            Key(Key(Variable("__this__"), "Child"), "Flag");

        private static CallFunctionPointer Call(string attributeId, string callSiteId) => new()
        {
            type = PointerKind.CallFunction,
            attributeId = attributeId,
            thisPointer = Variable("__this__"),
            args = Array.Empty<Pointer>(),
            callSiteId = callSiteId,
        };

        private static OperationPointer Add(Pointer left, Pointer right) => new()
        {
            type = PointerKind.Operation,
            operation = new ArithmeticOperation
            {
                type = OperationKind.Arithmetic,
                arithmetic = new ArithmeticOpInfo
                {
                    type = ArithmeticOpKind.Addition,
                    pointers = new[] { left, right },
                },
            },
        };

        private static OperationPointer EqualTo(Pointer left, Pointer right) => new()
        {
            type = PointerKind.Operation,
            operation = new BooleanOperation
            {
                type = OperationKind.Boolean,
                expression = new BooleanExpression
                {
                    condition = new Condition
                    {
                        type = OperatorKind.EqualTo,
                        operand1 = left,
                        operand2 = right,
                    },
                },
            },
        };

        private static ObjectAttributeValue ObjectValue(
            string id,
            string typeId) => new()
        {
            id = id,
            typeId = typeId,
            value = new Dictionary<string, string>(),
            createdAt = "x",
            updatedAt = "x",
        };

        private sealed class TestEnumOption
        {
            internal TestEnumOption(string optionId)
            {
                this.optionId = optionId;
            }

            public string optionId { get; }
        }
    }
}
