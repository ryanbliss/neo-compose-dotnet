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
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    public class NSFunctionRuntimeTests
    {
        [Test]
        public void MemberDto_UsesOrdinal23AndGeneralFunctionCallIr()
        {
            const string json = @"{
                'id':'fn','projectId':'project-function','name':'Compute','kind':23,'isStatic':false,'accessModifierKind':'public',
                'code':'return RequiredLevel;','returnTypeInfo':{'type':2,'required':true},
                'argumentTypes':[{'name':'RequiredLevel','type':2,'required':true}],
                'deferred':false,'createdAt':'x','updatedAt':'x',
                'action':{
                    'parameters':[
                        {'id':'__this__','typeInfo':{'type':7,'required':true,'classId':'receiver-class'},'pointer':{'type':'variable','variableId':'__this__'}},
                        {'id':'__root__','typeInfo':{'type':7,'required':true,'classId':'root-class'},'pointer':{'type':'variable','variableId':'__root__'}},
                        {'id':'__arg_0__','typeInfo':{'type':2,'required':true},'pointer':{'type':'variable','variableId':'__arg_0__'}}
                    ],
                    'instructions':[{'type':'return','pointer':{'type':'callFunction','memberId':'other','receiver':{'kind':'instance','pointer':{'type':'variable','variableId':'__this__'}},'args':[],'callSiteId':'call-0'}}],
                    'typeInfo':{'type':2,'required':true}
                }
            }";

            JsonMember member = JsonConvert.DeserializeObject<JsonMember>(json)!;

            var function = (NSFunctionMember)member;
            Assert.AreEqual((MemberKind)23, function.kind);
            Assert.AreEqual("RequiredLevel", function.argumentTypes[0].name);
            var call = (CallFunctionPointer)((ReturnInstruction)function.action.instructions[0]).pointer!;
            Assert.AreEqual("call-0", call.callSiteId);
            StringAssert.Contains("\"kind\":23", JsonConvert.SerializeObject(member));
        }

        [Test]
        public void Invoke_BindsTypedArgumentsAndReturnsValue()
        {
            FunctionArgumentTypeInfo argument = Argument("RequiredLevel", MemberKind.Int);
            NSFunctionMember function = ScriptFunction(
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
                ReceiverClass(("Identity", function.id)));
            var node = new NeoMemberNSFunction(client, function, null);

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
                MemberKind.Float);
            NSFunctionMember floatArgumentFunction = ScriptFunction(
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
                MemberKind.Int);
            NSFunctionMember intArgumentFunction = ScriptFunction(
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
            var functions = new List<JsonMember>
            {
                floatArgumentFunction,
                intArgumentFunction,
            };
            var members = new List<(string key, string memberId)>
            {
                (floatArgumentFunction.name, floatArgumentFunction.id),
                (intArgumentFunction.name, intArgumentFunction.id),
            };
            var floatReturns = new List<NSFunctionMember>();
            var intReturns = new List<NSFunctionMember>();
            for (int i = 0; i < nonFinite.Length; i++)
            {
                NSFunctionMember floatReturn = ScriptFunction(
                    $"fn-non-finite-float-return-{i}",
                    $"NonFiniteFloatReturn{i}",
                    deferred: false,
                    FloatType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Action(
                        FloatType(),
                        Array.Empty<FunctionArgumentTypeInfo>(),
                        Return(Floating(nonFinite[i], FloatType()))));
                NSFunctionMember intReturn = ScriptFunction(
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
                ReceiverClass(members.ToArray()));

            foreach (double value in nonFinite)
            {
                Assert.Throws<NSGetterRuntimeError>(() =>
                    new NeoMemberNSFunction(
                        client,
                        floatArgumentFunction,
                        null).Invoke(
                            "receiver-value",
                            new object?[] { value }));
                Assert.Throws<NSGetterRuntimeError>(() =>
                    new NeoMemberNSFunction(
                        client,
                        intArgumentFunction,
                        null).Invoke(
                            "receiver-value",
                            new object?[] { value }));
            }
            foreach (NSFunctionMember function in floatReturns)
            {
                Assert.Throws<InvalidOperationException>(() =>
                    new NeoMemberNSFunction(client, function, null).Invoke(
                        "receiver-value",
                        Array.Empty<object?>()));
            }
            foreach (NSFunctionMember function in intReturns)
            {
                Assert.Throws<InvalidOperationException>(() =>
                    new NeoMemberNSFunction(client, function, null).Invoke(
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
                type = MemberKind.Enum,
                required = true,
                enumId = enumId,
            };
            FunctionArgumentTypeInfo argument = Argument(
                "RequiredLevel",
                MemberKind.Enum);
            argument.enumId = enumId;
            NSFunctionMember function = ScriptFunction(
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
                new JsonMember[] { function },
                ReceiverClass(("EnumIdentity", function.id)));
            var node = new NeoMemberNSFunction(client, function, null);

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
                type = MemberKind.DialogueLookup,
                required = true,
            };
            FunctionArgumentTypeInfo dialogueArgument = Argument(
                "Dialogue",
                MemberKind.DialogueLookup);
            NSFunctionMember singleFunction = ScriptFunction(
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
                type = MemberKind.String,
                required = true,
            };
            var dialogueListType = new CollectionTypeInfo
            {
                type = MemberKind.List,
                required = true,
                entryTypeInfo = stringType,
            };
            FunctionArgumentTypeInfo dialogueListArgument = Argument(
                "Dialogues",
                MemberKind.List);
            dialogueListArgument.entryTypeInfo = stringType;
            NSFunctionMember listFunction = ScriptFunction(
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
                new JsonMember[] { singleFunction, listFunction },
                ReceiverClass(
                    ("DialogueIdentity", singleFunction.id),
                    ("DialogueListIdentity", listFunction.id)));

            object? singleResult = new NeoMemberNSFunction(
                client,
                singleFunction,
                null).Invoke(
                    "receiver-value",
                    new object?[] { new NeoDialogueReference("dialogue-1") });
            CollectionAssert.AreEqual(
                new[] { "dialogue-1" },
                (object?[])singleResult!);

            object? listResult = new NeoMemberNSFunction(
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
                new NeoMemberNSFunction(client, singleFunction, null).Invoke(
                    "receiver-value",
                    new object?[] { "dialogue-1" }));
            Assert.Throws<NSGetterRuntimeError>(() =>
                new NeoMemberNSFunction(client, singleFunction, null).Invoke(
                    "receiver-value",
                    new object?[]
                    {
                        new[]
                        {
                            new NeoDialogueReference("dialogue-1"),
                            new NeoDialogueReference("dialogue-2"),
                        },
                    }));

            FunctionMember deserialized =
                JsonConvert.DeserializeObject<FunctionMember>(
                    "{'kind':13,'isStatic':false,'accessModifierKind':'public','returnTypeInfo':{'type':18,'required':true}}")!;
            Assert.AreEqual(
                MemberKind.DialogueLookup,
                deserialized.returnTypeInfo.type);
        }

        [Test]
        public void Invoke_MarshalsReceiverGenericDecimalReturn()
        {
            const string genericClassId = "generic-decimal-receiver-class";
            const string genericParamId = "generic-decimal-receiver-param";
            var returnType = new GenericTypeInfo
            {
                type = MemberKind.Generic,
                required = true,
                ownerClassId = genericClassId,
                genericParamId = genericParamId,
            };
            NSFunctionMember function = ScriptFunction(
                "fn-generic-decimal-return",
                "GenericDecimalReturn",
                deferred: false,
                returnType,
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    returnType,
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Number(7))));
            var binding = new DecimalMember
            {
                id = "member-generic-decimal-binding",
                projectId = ProjectId,
                name = "Generic Decimal Binding",
                kind = MemberKind.Decimal,
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            var genericClass = new NeoSchemaClass
            {
                id = genericClassId,
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
            var concreteClass = new NeoSchemaClass
            {
                id = "receiver-class",
                projectId = ProjectId,
                name = "ConcreteDecimalReceiver",
                schema = new Dictionary<string, string>(),
                extendsClassId = genericClass.id,
                extendsGenericBindings = new Dictionary<string, GenericBinding>
                {
                    [genericParamId] = new()
                    {
                        kind = NeoGenericBindingKinds.Member,
                        memberId = binding.id,
                    },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            NeoClient client = BuildClient(
                new JsonMember[] { function, binding },
                concreteClass,
                new[] { genericClass });

            object? result = new NeoMemberNSFunction(
                client,
                function,
                null).Invoke(
                    "receiver-value",
                    Array.Empty<object?>());

            Assert.AreEqual("7", result);
        }

        [Test]
        public void Invoke_RejectsWrongNominalClassAndNestedListReturnValues()
        {
            var expectedClass = new NeoSchemaClass
            {
                id = "expected-return-class",
                projectId = ProjectId,
                name = "ExpectedReturn",
                schema = new Dictionary<string, string>(),
                createdAt = "x",
                updatedAt = "x",
            };
            var classReturnType = new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = expectedClass.id,
            };
            NSFunctionMember wrongClass = ScriptFunction(
                "fn-wrong-class-return",
                "WrongClassReturn",
                deferred: false,
                classReturnType,
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    classReturnType,
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Variable("__this__"))));
            var listReturnType = new CollectionTypeInfo
            {
                type = MemberKind.List,
                required = true,
                entryTypeInfo = IntType(),
            };
            NSFunctionMember wrongList = ScriptFunction(
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
                new JsonMember[] { wrongClass, wrongList },
                ReceiverClass(
                    (wrongClass.name, wrongClass.id),
                    (wrongList.name, wrongList.id)),
                new[] { expectedClass });

            InvalidOperationException classError =
                Assert.Throws<InvalidOperationException>(() =>
                    new NeoMemberNSFunction(
                        client,
                        wrongClass,
                        null).Invoke(
                            "receiver-value",
                            Array.Empty<object?>()))!;
            StringAssert.Contains("expected-return-class", classError.Message);
            Assert.Throws<InvalidOperationException>(() =>
                new NeoMemberNSFunction(
                    client,
                    wrongList,
                    null).Invoke(
                        "receiver-value",
                        Array.Empty<object?>()));
        }

        [Test]
        public void Invoke_SubstitutesGenericSignatureFromConcreteReceiver()
        {
            const string genericClassId = "generic-receiver-class";
            const string genericParamId = "generic-receiver-param";
            const string enumId = "enum-generic-level";
            var returnType = new GenericTypeInfo
            {
                type = MemberKind.Generic,
                required = true,
                ownerClassId = genericClassId,
                genericParamId = genericParamId,
            };
            var argument = new FunctionArgumentTypeInfo
            {
                name = "Value",
                type = MemberKind.Generic,
                required = true,
                ownerClassId = genericClassId,
                genericParamId = genericParamId,
            };
            NSFunctionMember function = ScriptFunction(
                "fn-generic-identity",
                "GenericIdentity",
                deferred: false,
                returnType,
                new[] { argument },
                Action(
                    returnType,
                    new[] { argument },
                    Return(Variable("__arg_0__"))));
            var binding = new EnumMember
            {
                id = "member-generic-enum-binding",
                projectId = ProjectId,
                name = "Generic Enum Binding",
                kind = MemberKind.Enum,
                enumId = enumId,
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            var genericClass = new NeoSchemaClass
            {
                id = genericClassId,
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
            var concreteClass = new NeoSchemaClass
            {
                id = "receiver-class",
                projectId = ProjectId,
                name = "ConcreteReceiver",
                schema = new Dictionary<string, string>(),
                extendsClassId = genericClass.id,
                extendsGenericBindings = new Dictionary<string, GenericBinding>
                {
                    [genericParamId] = new()
                    {
                        kind = NeoGenericBindingKinds.Member,
                        memberId = binding.id,
                    },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            NeoClient client = BuildClient(
                new JsonMember[] { function, binding },
                concreteClass,
                new[] { genericClass });
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: NeoValueOwnership.Asset);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Asset,
                "receiver-value",
                out MemberValue? receiverRow));
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
            var node = new NeoMemberNSFunction(client, function, null);

            object? result = node.Invoke(
                "receiver-value",
                new object?[] { new TestEnumOption("generic-level-2") });

            Assert.IsInstanceOf<object?[]>(result);
            CollectionAssert.AreEqual(
                new[] { "generic-level-2" },
                (object?[])result!);
        }

        [Test]
        public void GenericSignatureSubstitution_ClosesConstructedClassAndNestedCollectionTypes()
        {
            const string functionParamId = "function-param";
            const string forwardedParamId = "forwarded-param";
            const string boxParamId = "box-param";
            const string enumId = "enum-constructed-generic";
            var enumBinding = new EnumMember
            {
                id = "member-constructed-enum-binding",
                projectId = ProjectId,
                name = "Constructed Enum Binding",
                kind = MemberKind.Enum,
                enumId = enumId,
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            var classBinding = new ClassMember
            {
                id = "member-constructed-class-binding",
                projectId = ProjectId,
                name = "Constructed Box Binding",
                kind = MemberKind.Class,
                classId = "box-class",
                classArguments = new Dictionary<string, GenericBinding>
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
            var boxClass = new NeoSchemaClass
            {
                id = "box-class",
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
                new JsonMember[] { enumBinding, classBinding },
                ReceiverClass(),
                new[] { boxClass });
            var env = new Dictionary<string, NeoGenericEnvEntry>
            {
                [functionParamId] = NeoGenericEnvEntry.Bound(classBinding.id),
                [forwardedParamId] = NeoGenericEnvEntry.Bound(enumBinding.id),
            };

            var direct = (ClassTypeInfo)NeoNSFunctionRuntime.ResolveInvocationTypeInfo(
                client,
                new GenericTypeInfo
                {
                    type = MemberKind.Generic,
                    required = true,
                    genericParamId = functionParamId,
                },
                env);

            Assert.AreEqual(classBinding.classId, direct.classId);
            Assert.IsNotNull(direct.typeArguments);
            var directEnum = (EnumTypeInfo)direct.typeArguments![boxParamId];
            Assert.AreEqual(enumId, directEnum.enumId);

            var nested = (CollectionTypeInfo)NeoNSFunctionRuntime.ResolveInvocationTypeInfo(
                client,
                new CollectionTypeInfo
                {
                    type = MemberKind.List,
                    required = true,
                    entryTypeInfo = new ClassTypeInfo
                    {
                        type = MemberKind.Class,
                        required = true,
                        classId = boxClass.id,
                        typeArguments = new Dictionary<string, TypeInfo>
                        {
                            [boxParamId] = new GenericTypeInfo
                            {
                                type = MemberKind.Generic,
                                required = true,
                                genericParamId = forwardedParamId,
                            },
                        },
                    },
                },
                env);

            var nestedClass = (ClassTypeInfo)nested.entryTypeInfo;
            var nestedEnum = (EnumTypeInfo)nestedClass.typeArguments![boxParamId];
            Assert.AreEqual(enumId, nestedEnum.enumId);
        }

        [Test]
        public void GenericSignatureSubstitution_RejectsConstructedBindingCycles()
        {
            const string functionParamId = "function-param-cycle";
            const string boxParamId = "box-param-cycle";
            var cyclicBinding = new ClassMember
            {
                id = "member-cyclic-class-binding",
                projectId = ProjectId,
                name = "Cyclic Box Binding",
                kind = MemberKind.Class,
                classId = "cyclic-box-class",
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            cyclicBinding.classArguments = new Dictionary<string, GenericBinding>
            {
                [boxParamId] = new GenericBinding
                {
                    kind = NeoGenericBindingKinds.Member,
                    memberId = cyclicBinding.id,
                },
            };
            NeoClient client = BuildClient(
                new JsonMember[] { cyclicBinding },
                ReceiverClass());

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NeoNSFunctionRuntime.ResolveInvocationTypeInfo(
                    client,
                    new GenericTypeInfo
                    {
                        type = MemberKind.Generic,
                        required = true,
                        genericParamId = functionParamId,
                    },
                    new Dictionary<string, NeoGenericEnvEntry>
                    {
                        [functionParamId] = NeoGenericEnvEntry.Bound(
                            cyclicBinding.id),
                    }))!;

            StringAssert.Contains("binding member cycle", error.Message);
            StringAssert.Contains(cyclicBinding.id, error.Message);
        }

        [Test]
        public void Invoke_NormalizesAssetDtosToEvaluatorWireShapes()
        {
            var spriteType = new PrimitiveTypeInfo
            {
                type = MemberKind.Sprite,
                required = true,
            };
            FunctionArgumentTypeInfo spriteArgument = Argument(
                "Portrait",
                MemberKind.Sprite);
            NSFunctionMember spriteFunction = ScriptFunction(
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
                type = MemberKind.Audio,
                required = true,
            };
            FunctionArgumentTypeInfo audioArgument = Argument(
                "Voice",
                MemberKind.Audio);
            NSFunctionMember audioFunction = ScriptFunction(
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
                new JsonMember[] { spriteFunction, audioFunction },
                ReceiverClass(
                    ("SpriteIdentity", spriteFunction.id),
                    ("AudioIdentity", audioFunction.id)));

            object? spriteResult = new NeoMemberNSFunction(
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
            object? audioResult = new NeoMemberNSFunction(
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
            NSFunctionMember inner = ScriptFunction(
                "fn-inner",
                "Inner",
                deferred: false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Number(7))));
            NSFunctionMember outer = ScriptFunction(
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
                new JsonMember[] { inner, outer },
                ReceiverClass(("Inner", inner.id), ("Outer", outer.id)));
            var node = new NeoMemberNSFunction(client, outer, null);

            Assert.AreEqual(7L, Convert.ToInt64(
                node.Invoke("receiver-value", Array.Empty<object?>())));
        }

        [Test]
        public void Invoke_RecursiveNSFunctionStopsAtNamedDepthLimit()
        {
            NSFunctionMember recursive = ScriptFunction(
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
                new JsonMember[] { recursive },
                ReceiverClass(("RecurseForever", recursive.id)));
            var node = new NeoMemberNSFunction(client, recursive, null);

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                node.Invoke("receiver-value", Array.Empty<object?>()))!;

            StringAssert.Contains("call depth exceeded 64", error.Message);
            StringAssert.Contains("RecurseForever -> RecurseForever", error.Message);
        }

        [Test]
        public void Invoke_RepeatedCallSiteInCollectionLambdaUsesDistinctDynamicFrames()
        {
            FunctionArgumentTypeInfo nativeArgument = Argument("Value", MemberKind.Int);
            FunctionMember native = NativeFunction("fn-map", "MapValue", deferred: false);
            native.argumentTypes = new[] { nativeArgument };
            var listType = new CollectionTypeInfo
            {
                type = MemberKind.List,
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
                        memberId = native.id,
                        receiver = CallReceiver.Instance(Variable("__this__")),
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
            NSFunctionMember function = ScriptFunction(
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
                new JsonMember[] { native, function },
                ReceiverClass(("MapValue", native.id), ("SelectValues", function.id)));
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
            var node = new NeoMemberNSFunction(client, function, null);

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
            NSFunctionMember baseFunction = ScriptFunction(
                "fn-base",
                "Compute",
                deferred: false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Number(1))));
            NSFunctionMember overrideFunction = new()
            {
                id = "fn-derived",
                projectId = ProjectId,
                name = "Compute",
                kind = MemberKind.NSFunction,
                code = "return 2;",
                extendsMemberId = baseFunction.id,
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
            var baseClass = ReceiverClass(("Compute", baseFunction.id));
            var derivedClass = ReceiverClass(("Compute", overrideFunction.id));
            derivedClass.id = "derived-receiver-class";
            derivedClass.name = "DerivedReceiver";
            derivedClass.extendsClassId = baseClass.id;
            var derivedValue = ObjectValue("derived-receiver-value", derivedClass.id);
            NeoClient client = BuildClient(
                new JsonMember[] { baseFunction, overrideFunction },
                baseClass,
                new[] { derivedClass },
                new MemberValue[] { derivedValue });
            var node = new NeoMemberNSFunction(client, baseFunction, null);

            Assert.AreEqual(2L, Convert.ToInt64(
                node.Invoke(derivedValue.id, Array.Empty<object?>())));
        }

        [Test]
        public void Invoke_MutationBodyReturnsTheUpdatedSaveValue()
        {
            FunctionArgumentTypeInfo argument = Argument("RequiredLevel", MemberKind.Int);
            NSFunctionMember function = ScriptFunction(
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
            var node = new NeoMemberNSFunction(client, function, null);

            object? result = node.Invoke(
                "receiver-value",
                new object?[] { 12 });

            Assert.AreEqual(12L, Convert.ToInt64(result));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                "save-level-value",
                out NumberMemberValue? level));
            Assert.AreEqual(12d, level!.value);
        }

        [Test]
        public void Invoke_ReadYourWritesRefreshesPreviouslyReadMember()
        {
            NSFunctionMember function = ScriptFunction(
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
            var node = new NeoMemberNSFunction(
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
                out BoolMemberValue? flag));
            Assert.AreEqual(false, flag!.value);
        }

        [Test]
        public void Invoke_ReadYourWritesRefreshesNestedClassMember()
        {
            NSFunctionMember function = ScriptFunction(
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
            var node = new NeoMemberNSFunction(
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
                out BoolMemberValue? flag));
            Assert.AreEqual(false, flag!.value);
        }

        [Test]
        public void InvokeAsync_TwoDeferredCallsResumeLeftToRightExactlyOnce()
        {
            FunctionMember native = NativeFunction("fn-native", "Fetch", deferred: true);
            Pointer first = Call(native.id, "fetch-first");
            Pointer second = Call(native.id, "fetch-second");
            NSFunctionMember function = ScriptFunction(
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
                new JsonMember[] { native, function },
                ReceiverClass(("Fetch", native.id), ("ComputeLater", function.id)));
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
            var node = new NeoMemberNSFunction(client, function, null);

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
            FunctionMember native = NativeFunction("fn-native", "Fetch", deferred: true);
            NSFunctionMember inner = ScriptFunction(
                "fn-inner-deferred",
                "InnerDeferred",
                deferred: true,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Call(native.id, "nested-native"))));
            NSFunctionMember outer = ScriptFunction(
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
                new JsonMember[] { native, inner, outer },
                ReceiverClass(
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
            var node = new NeoMemberNSFunction(client, outer, null);

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
            FunctionMember native = NativeFunction("fn-native", "Fetch", deferred: true);
            NSFunctionMember inner = ScriptFunction(
                "fn-inner-inline",
                "InnerInline",
                deferred: true,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Call(native.id, "inline-native"))));
            NSFunctionMember outer = ScriptFunction(
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
                new JsonMember[] { native, inner, outer },
                ReceiverClass(
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
            var node = new NeoMemberNSFunction(client, outer, null);

            Task<object?> task = node.InvokeAsync(
                "receiver-value",
                Array.Empty<object?>());

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(43L, Convert.ToInt64(task.GetAwaiter().GetResult()));
        }

        [Test]
        public void Invoke_ImmediateNSFunctionRejectsDeferredNativeModeBeforeInvoker()
        {
            FunctionMember native = NativeFunction("fn-native", "Fetch", deferred: true);
            NSFunctionMember function = ScriptFunction(
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
                new JsonMember[] { native, function },
                ReceiverClass(("Fetch", native.id), ("InvalidMode", function.id)));
            int invocationCount = 0;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [native.id] = (_, _, _, _) => invocationCount++,
                });
            var node = new NeoMemberNSFunction(client, function, null);

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
            FunctionMember native = NativeFunction(
                "fn-immediate-native",
                "ImmediateNative",
                deferred: false);
            NeoClient client = BuildClient(
                new JsonMember[] { native },
                ReceiverClass(("ImmediateNative", native.id)));
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
            FunctionMember native = NativeFunction("fn-native", "Fetch", deferred: true);
            NSFunctionMember function = ScriptFunction(
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
                new JsonMember[] { native, function },
                ReceiverClass(("Fetch", native.id), ("ComputeLater", function.id)));
            NeoDeferredFunction<int>? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [native.id] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport.ResolveDeferredFunction<NeoDeferredFunction<int>>(
                            deferred,
                            native.name),
                });
            var node = new NeoMemberNSFunction(client, function, null);
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
            NSFunctionMember baseFunction = ScriptFunction(
                "fn-base",
                "Compute",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(1))));
            NSFunctionMember invalidOverride = ScriptFunction(
                "fn-invalid",
                "Compute",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(IntType(), Array.Empty<FunctionArgumentTypeInfo>(), Return(Number(2))));
            invalidOverride.extendsMemberId = baseFunction.id;

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                BuildClient(
                    new JsonMember[] { baseFunction, invalidOverride },
                    ReceiverClass(("Compute", baseFunction.id))))!;

            StringAssert.Contains("must inherit returnTypeInfo", error.Message);
        }

        [Test]
        public void Construction_RejectsEmptyLocalCode()
        {
            NSFunctionMember function = ScriptFunction(
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
                    new JsonMember[] { function },
                    ReceiverClass(("Empty", function.id))))!;

            StringAssert.Contains("local code must not be empty", error.Message);
        }

        [Test]
        public void Construction_RejectsUnknownNonNullBodyMode()
        {
            NSFunctionMember function = ScriptFunction(
                "fn-body-mode",
                "BodyMode",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Return(Number(1))));
            function.bodyMode = "code";

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                BuildClient(
                    new JsonMember[] { function },
                    ReceiverClass(("BodyMode", function.id))))!;

            StringAssert.Contains("unsupported bodyMode 'code'", error.Message);
        }

        [Test]
        public void Construction_RejectsStaleStructuredArgumentType()
        {
            FunctionArgumentTypeInfo declared = Argument("Target", MemberKind.Class);
            declared.classId = "class-a";
            FunctionArgumentTypeInfo compiled = Argument("Target", MemberKind.Class);
            compiled.classId = "class-b";
            NSFunctionMember function = ScriptFunction(
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
                    new JsonMember[] { function },
                    ReceiverClass(("StaleArgument", function.id))))!;

            StringAssert.Contains("argument 0 type does not match", error.Message);
        }

        private const string ProjectId = "project-function";

        private static NeoClient BuildClient(
            JsonMember[] callables,
            NeoSchemaClass receiverClass,
            NeoSchemaClass[]? additionalClasses = null,
            MemberValue[]? additionalValues = null)
        {
            ClassMember assets = RootMember("root-assets", "Assets", "root-assets-value");
            ClassMember save = RootMember("root-save", "Save", "root-save-value", "save");
            ClassMember session = RootMember("root-session", "Session", "root-session-value", "session");
            var members = new Dictionary<string, JsonMember>
            {
                [assets.id] = assets,
                [save.id] = save,
                [session.id] = session,
            };
            foreach (JsonMember callable in callables) members[callable.id] = callable;

            var classes = new Dictionary<string, NeoSchemaClass>
            {
                ["root-class"] = new NeoSchemaClass
                {
                    id = "root-class",
                    projectId = ProjectId,
                    name = "Root",
                    schema = new Dictionary<string, string>(),
                    createdAt = "x",
                    updatedAt = "x",
                },
                [receiverClass.id] = receiverClass,
            };
            if (additionalClasses is not null)
            {
                foreach (NeoSchemaClass schemaClass in additionalClasses)
                {
                    classes[schemaClass.id] = schemaClass;
                }
            }

            var values = new Dictionary<string, MemberValue>
            {
                [assets.valueId!] = ObjectValue(assets.valueId!, "root-class"),
                [save.valueId!] = ObjectValue(save.valueId!, "root-class"),
                [session.valueId!] = ObjectValue(session.valueId!, "root-class"),
                ["receiver-value"] = ObjectValue("receiver-value", receiverClass.id),
            };
            if (additionalValues is not null)
            {
                foreach (MemberValue value in additionalValues) values[value.id] = value;
            }

            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "NSFunction Tests",
                    rootAssetsMemberId = assets.id,
                    rootSaveFileMemberId = save.id,
                    rootSessionMemberId = session.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                members = members,
                values = values,
                classes = classes,
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            });
        }

        private static NeoClient BuildMutationClient(NSFunctionMember function)
        {
            ClassMember assets = RootMember("root-assets", "Assets", "root-assets-value");
            ClassMember save = new()
            {
                id = "root-save",
                projectId = ProjectId,
                name = "Save",
                kind = MemberKind.Class,
                classId = "save-root-class",
                valueId = "root-save-value",
                storage = "save",
                createdAt = "x",
                updatedAt = "x",
            };
            ClassMember session = RootMember("root-session", "Session", "root-session-value", "session");
            var levelMember = new IntMember
            {
                id = "save-level",
                projectId = ProjectId,
                name = "Level",
                kind = MemberKind.Int,
                valueId = "save-level-value",
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            ObjectMemberValue saveValue = ObjectValue(save.valueId!, "save-root-class");
            saveValue.value!["Level"] = levelMember.valueId!;

            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "NSFunction Mutation Test",
                    rootAssetsMemberId = assets.id,
                    rootSaveFileMemberId = save.id,
                    rootSessionMemberId = session.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                members = new Dictionary<string, JsonMember>
                {
                    [assets.id] = assets,
                    [save.id] = save,
                    [session.id] = session,
                    [levelMember.id] = levelMember,
                    [function.id] = function,
                },
                values = new Dictionary<string, MemberValue>
                {
                    [assets.valueId!] = ObjectValue(assets.valueId!, "root-class"),
                    [save.valueId!] = saveValue,
                    [session.valueId!] = ObjectValue(session.valueId!, "root-class"),
                    ["receiver-value"] = ObjectValue("receiver-value", "receiver-class"),
                    [levelMember.valueId!] = new NumberMemberValue
                    {
                        id = levelMember.valueId!,
                        value = 1,
                        createdAt = "x",
                        updatedAt = "x",
                    },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["root-class"] = new NeoSchemaClass
                    {
                        id = "root-class",
                        projectId = ProjectId,
                        name = "Root",
                        schema = new Dictionary<string, string>(),
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["save-root-class"] = new NeoSchemaClass
                    {
                        id = "save-root-class",
                        projectId = ProjectId,
                        name = "SaveRoot",
                        schema = new Dictionary<string, string>
                        {
                            ["Level"] = levelMember.id,
                        },
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["receiver-class"] = ReceiverClass(("SetLevel", function.id)),
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            });
        }

        private static NeoClient BuildBooleanMutationClient(
            NSFunctionMember function,
            bool nested)
        {
            ClassMember assets = RootMember(
                "root-assets",
                "Assets",
                "root-assets-value");
            ClassMember save = new()
            {
                id = "root-save",
                projectId = ProjectId,
                name = "Save",
                kind = MemberKind.Class,
                classId = "save-root-class",
                valueId = "root-save-value",
                storage = "save",
                createdAt = "x",
                updatedAt = "x",
            };
            ClassMember session = RootMember(
                "root-session",
                "Session",
                "root-session-value",
                "session");
            var flagMember = new BoolMember
            {
                id = "save-flag",
                projectId = ProjectId,
                name = "Flag",
                kind = MemberKind.Bool,
                valueId = "save-flag-value",
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            var members = new Dictionary<string, JsonMember>
            {
                [assets.id] = assets,
                [save.id] = save,
                [session.id] = session,
                [flagMember.id] = flagMember,
                [function.id] = function,
            };
            var saveSchema = new Dictionary<string, string>
            {
                [function.name] = function.id,
            };
            var values = new Dictionary<string, MemberValue>
            {
                [assets.valueId!] = ObjectValue(assets.valueId!, "root-class"),
                [save.valueId!] = ObjectValue(save.valueId!, "save-root-class"),
                [session.valueId!] = ObjectValue(session.valueId!, "root-class"),
                [flagMember.valueId!] = new BoolMemberValue
                {
                    id = flagMember.valueId!,
                    value = true,
                    createdAt = "x",
                    updatedAt = "x",
                },
            };
            var classes = new Dictionary<string, NeoSchemaClass>
            {
                ["root-class"] = new NeoSchemaClass
                {
                    id = "root-class",
                    projectId = ProjectId,
                    name = "Root",
                    schema = new Dictionary<string, string>(),
                    createdAt = "x",
                    updatedAt = "x",
                },
            };
            ObjectMemberValue saveValue = (ObjectMemberValue)values[save.valueId!];
            if (nested)
            {
                var childMember = new ClassMember
                {
                    id = "save-child",
                    projectId = ProjectId,
                    name = "Child",
                    kind = MemberKind.Class,
                    classId = "save-child-class",
                    valueId = "save-child-value",
                    required = true,
                    createdAt = "x",
                    updatedAt = "x",
                };
                members[childMember.id] = childMember;
                saveSchema[childMember.name] = childMember.id;
                saveValue.value![childMember.name] = childMember.valueId!;
                ObjectMemberValue childValue = ObjectValue(
                    childMember.valueId!,
                    childMember.classId);
                childValue.value![flagMember.name] = flagMember.valueId!;
                values[childValue.id] = childValue;
                classes["save-child-class"] = new NeoSchemaClass
                {
                    id = "save-child-class",
                    projectId = ProjectId,
                    name = "SaveChild",
                    schema = new Dictionary<string, string>
                    {
                        [flagMember.name] = flagMember.id,
                    },
                    createdAt = "x",
                    updatedAt = "x",
                };
            }
            else
            {
                saveSchema[flagMember.name] = flagMember.id;
                saveValue.value![flagMember.name] = flagMember.valueId!;
            }
            classes["save-root-class"] = new NeoSchemaClass
            {
                id = "save-root-class",
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
                    rootAssetsMemberId = assets.id,
                    rootSaveFileMemberId = save.id,
                    rootSessionMemberId = session.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                members = members,
                values = values,
                classes = classes,
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            });
        }

        private static ClassMember RootMember(
            string id,
            string name,
            string valueId,
            string? storage = null) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Class,
            classId = "root-class",
            valueId = valueId,
            storage = storage,
            createdAt = "x",
            updatedAt = "x",
        };

        private static NeoSchemaClass ReceiverClass(
            params (string key, string memberId)[] members)
        {
            var schema = new Dictionary<string, string>();
            foreach (var member in members) schema[member.key] = member.memberId;
            return new NeoSchemaClass
            {
                id = "receiver-class",
                projectId = ProjectId,
                name = "Receiver",
                schema = schema,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static NSFunctionMember ScriptFunction(
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
            kind = MemberKind.NSFunction,
            code = "compiled test function",
            returnTypeInfo = returnType,
            argumentTypes = arguments,
            deferred = deferred,
            action = action,
            createdAt = "x",
            updatedAt = "x",
        };

        private static FunctionMember NativeFunction(
            string id,
            string name,
            bool deferred) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Function,
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
                new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = true,
                    classId = "receiver-class",
                });
            parameters[1] = Parameter(
                "__root__",
                new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = true,
                    classId = "root-class",
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
            MemberKind type,
            bool required = true) => new()
        {
            name = name,
            type = type,
            required = required,
        };

        private static PrimitiveTypeInfo IntType() => new()
        {
            type = MemberKind.Int,
            required = true,
        };

        private static PrimitiveTypeInfo BoolType() => new()
        {
            type = MemberKind.Bool,
            required = true,
        };

        private static PrimitiveTypeInfo FloatType() => new()
        {
            type = MemberKind.Float,
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
                    type = MemberKind.String,
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

        private static CallFunctionPointer Call(string memberId, string callSiteId) => new()
        {
            type = PointerKind.CallFunction,
            memberId = memberId,
            receiver = CallReceiver.Instance(Variable("__this__")),
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

        private static ObjectMemberValue ObjectValue(
            string id,
            string classId) => new()
        {
            id = id,
            classId = classId,
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
