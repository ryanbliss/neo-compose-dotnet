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
        public void DelegateDto_UsesOrdinal25RecursiveSignatureAndCallIr()
        {
            const string json = @"{
                'id':'selector','projectId':'project-function','name':'Selector','kind':25,
                'isStatic':false,'accessModifierKind':'public','required':true,
                'returnTypeInfo':{'type':21,'required':true,'ownerClassId':'track','genericParamId':'child'},
                'argumentTypes':[{'name':'amount','type':2,'required':true}],
                'defaultValue':{'value':{'code':'amount => amount','action':{
                    'compilerRevision':7,
                    'parameters':[
                        {'id':'__this__','typeInfo':{'type':7,'required':true,'classId':'receiver-class'},'pointer':{'type':'variable','variableId':'__this__'}},
                        {'id':'__root__','typeInfo':{'type':7,'required':true,'classId':'root-class'},'pointer':{'type':'variable','variableId':'__root__'}},
                        {'id':'__arg_0__','typeInfo':{'type':2,'required':true},'pointer':{'type':'variable','variableId':'__arg_0__'}}
                    ],
                    'instructions':[{'type':'return','pointer':{'type':'variable','variableId':'__arg_0__'}}],
                    'typeInfo':{'type':21,'required':true,'ownerClassId':'track','genericParamId':'child'}
                }}},'createdAt':'x','updatedAt':'x'
            }";

            var member = (DelegateMember)JsonConvert.DeserializeObject<JsonMember>(json)!;

            Assert.AreEqual(25, (int)member.kind);
            Assert.IsInstanceOf<GenericTypeInfo>(member.returnTypeInfo);
            Assert.AreEqual("amount", member.argumentTypes[0].name);
            Assert.AreEqual(7, member.defaultValue!.value!.action!.compilerRevision);

            const string callJson = @"{
                'type':'functionCall','call':{
                    'type':'callDelegate',
                    'delegate':{'type':'variable','variableId':'selector'},
                    'args':[{'type':'value','value':{'typeInfo':{'type':2,'required':true},'value':3}}],
                    'callSiteId':'delegate-0'
                }
            }";
            var instruction = (FunctionCallInstruction)
                JsonConvert.DeserializeObject<Instruction>(callJson)!;
            var call = (CallDelegatePointer)instruction.call;
            Assert.AreEqual("delegate-0", call.callSiteId);
            Assert.AreEqual(1, call.args.Length);
        }

        [Test]
        public void DelegateApi_EnumeratesVariantsThroughSixteenParameters()
        {
            Assert.AreEqual(1, typeof(NeoDelegate<>).GetGenericArguments().Length);
            Assert.AreEqual(9, typeof(NeoDelegate<,,,,,,,,>).GetGenericArguments().Length);
            Assert.AreEqual(17, typeof(NeoDelegate<,,,,,,,,,,,,,,,,>).GetGenericArguments().Length);
            Assert.AreEqual(
                System.Reflection.GenericParameterAttributes.Covariant,
                typeof(NeoDelegate<>).GetGenericArguments()[0].GenericParameterAttributes);
            Assert.AreEqual(
                System.Reflection.GenericParameterAttributes.Contravariant,
                typeof(NeoDelegate<,>).GetGenericArguments()[1].GenericParameterAttributes);
            Type[] sixteenParameterDelegateArguments =
                typeof(NeoDelegate<,,,,,,,,,,,,,,,,>).GetGenericArguments();
            Assert.AreEqual(
                System.Reflection.GenericParameterAttributes.Covariant,
                sixteenParameterDelegateArguments[0].GenericParameterAttributes);
            for (int index = 1; index < sixteenParameterDelegateArguments.Length; index++)
            {
                Assert.AreEqual(
                    System.Reflection.GenericParameterAttributes.Contravariant,
                    sixteenParameterDelegateArguments[index].GenericParameterAttributes);
            }

            SelectorBase selector = new SelectorChild();
            Assert.AreEqual("child", selector.Selector());

            NeoDelegate<string, object> acceptsObject = _ => "input";
            NeoDelegate<string, string> acceptsString = acceptsObject;
            Assert.AreEqual("input", acceptsString("child"));

            Assert.AreEqual(
                "system_88c5d17a-b73e-47a1-a96e-4ebe16e6d200",
                NeoSelectorRefreshKind.OnLoad.optionId);
            Assert.AreEqual(
                "system_dc350ac4-de4b-4d1c-9b46-097dc5b4180f",
                NeoSelectorRefreshKind.PerFrame.optionId);
        }

        private abstract class SelectorBase
        {
            public abstract NeoDelegate<object> Selector { get; }
        }

        private sealed class SelectorChild : SelectorBase
        {
            private static readonly NeoDelegate<string> ChildSelector = () => "child";

            public override NeoDelegate<object> Selector => ChildSelector;
        }

        [Test]
        public void DelegateCall_RequiresCompilerRevisionSeven()
        {
            NeoClient client = BuildClient(Array.Empty<JsonMember>(), ReceiverClass());
            var body = new FunctionWithReturnType
            {
                compilerRevision = 6,
                parameters = new[]
                {
                    Parameter("__this__", new ClassTypeInfo
                    {
                        type = MemberKind.Class,
                        required = true,
                        classId = "receiver-class",
                    }),
                    Parameter("__root__", new ClassTypeInfo
                    {
                        type = MemberKind.Class,
                        required = true,
                        classId = "root-class",
                    }),
                },
                instructions = new Instruction[]
                {
                    new FunctionCallInstruction
                    {
                        type = InstructionKind.FunctionCall,
                        call = new CallDelegatePointer
                        {
                            type = PointerKind.CallDelegate,
                            @delegate = Variable("selector"),
                            args = Array.Empty<Pointer>(),
                            callSiteId = "delegate-revision",
                        },
                    },
                    Return(Literal(IntType(), new JValue(0))),
                },
                typeInfo = IntType(),
            };

            var error = Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                NSGetterEvaluator.Evaluate(
                    body,
                    new NSGetterEvaluator.Context(
                        client,
                        new Dictionary<string, object?>(),
                        new Dictionary<string, object?>())));

            StringAssert.Contains("requires compiler revision 7", error!.Message);
        }

        [Test]
        public void DelegateClosure_CapturesLexicalThisAndBindsArguments()
        {
            NeoClient client = BuildClient(
                Array.Empty<JsonMember>(),
                ReceiverClass(),
                additionalValues: new MemberValue[]
                {
                    new NumberMemberValue
                    {
                        id = "count-a", value = 7, createdAt = "x", updatedAt = "x",
                    },
                    new NumberMemberValue
                    {
                        id = "count-b", value = 100, createdAt = "x", updatedAt = "x",
                    },
                });
            FunctionWithReturnType closureAction = Action(
                IntType(),
                new[] { Argument("amount", MemberKind.Int) },
                Return(Add(Key(Variable("__this__"), "Count"), Variable("__arg_0__"))));
            closureAction.compilerRevision = 7;
            var delegateType = new DelegateTypeInfo
            {
                type = MemberKind.NSDelegate,
                required = true,
                returnTypeInfo = IntType(),
                argumentTypes = new TypeInfo[] { IntType() },
            };
            var getter = new FunctionWithReturnType
            {
                compilerRevision = 7,
                parameters = new[]
                {
                    Parameter("__this__", new ClassTypeInfo
                    {
                        type = MemberKind.Class, required = true, classId = "receiver-class",
                    }),
                    Parameter("__root__", new ClassTypeInfo
                    {
                        type = MemberKind.Class, required = true, classId = "root-class",
                    }),
                },
                instructions = new Instruction[]
                {
                    Return(Literal(
                        delegateType,
                        JObject.FromObject(new NeoDelegateValue
                        {
                            action = closureAction,
                        }))),
                },
                typeInfo = delegateType,
            };
            var authoredThis = new Dictionary<string, object?> { ["Count"] = "count-a" };
            var callerThis = new Dictionary<string, object?> { ["Count"] = "count-b" };
            var authoredContext = new NSGetterEvaluator.Context(
                client, authoredThis, new Dictionary<string, object?>());
            object? closure = NSGetterEvaluator.Evaluate(getter, authoredContext);

            object? result = NSGetterEvaluator.InvokeDelegate(
                closure!,
                new object?[] { 5 },
                new NSGetterEvaluator.Context(
                    client,
                    callerThis,
                    new Dictionary<string, object?>()));

            Assert.AreEqual(12L, Convert.ToInt64(result));
        }

        [Test]
        public void DelegateClosurePointer_CapturesOuterArgumentsAndRoundTrips()
        {
            var delegateType = new DelegateTypeInfo
            {
                type = MemberKind.NSDelegate,
                required = true,
                returnTypeInfo = IntType(),
                argumentTypes = new TypeInfo[] { IntType() },
            };
            var closureAction = new FunctionWithReturnType
            {
                compilerRevision = 12,
                parameters = new[]
                {
                    Parameter("__this__", NullType()),
                    Parameter("__root__", NullType()),
                    Parameter("__lambda_0_arg_0__", IntType()),
                    Parameter("__capture_0_0__", IntType()),
                    Parameter("__capture_0_1__", IntType()),
                },
                instructions = new Instruction[]
                {
                    Return(Add(
                        Add(
                            Variable("__lambda_0_arg_0__"),
                            Variable("__capture_0_0__")),
                        Variable("__capture_0_1__"))),
                },
                typeInfo = IntType(),
            };
            var factory = new FunctionWithReturnType
            {
                compilerRevision = 12,
                parameters = new[]
                {
                    Parameter("__this__", NullType()),
                    Parameter("__root__", NullType()),
                    Parameter("__arg_0__", IntType()),
                    Parameter("__arg_1__", IntType()),
                },
                instructions = new Instruction[]
                {
                    Return(new DelegateClosurePointer
                    {
                        type = PointerKind.DelegateClosure,
                        typeInfo = delegateType,
                        action = closureAction,
                        captures = new Pointer[]
                        {
                            Variable("__arg_0__"),
                            Variable("__arg_1__"),
                        },
                        code = "(int value) => value + min + max",
                    }),
                },
                typeInfo = delegateType,
            };
            NeoClient client = BuildClient(Array.Empty<JsonMember>(), ReceiverClass());
            var context = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null);

            object? closure = NSGetterEvaluator.Evaluate(
                factory,
                context,
                new object?[] { 3, 7 });
            string persistedJson = JsonConvert.SerializeObject(closure);
            NeoDelegateValue persisted =
                JsonConvert.DeserializeObject<NeoDelegateValue>(persistedJson)!;

            CollectionAssert.AreEqual(new object?[] { 3L, 7L }, persisted.captures);
            object? result = NSGetterEvaluator.InvokeDelegate(
                persisted,
                new object?[] { 5 },
                context);
            Assert.AreEqual(15L, Convert.ToInt64(result));
        }

        [Test]
        public void DelegateMemberDefault_AcceptsRevisionTwelveLambdaParameterIdsAtLoad()
        {
            var argument = new FunctionArgumentTypeInfo
            {
                name = "value",
                type = MemberKind.Int,
                required = true,
            };
            var action = new FunctionWithReturnType
            {
                compilerRevision = 12,
                parameters = new[]
                {
                    Parameter("__this__", NullType()),
                    Parameter("__root__", NullType()),
                    Parameter("__lambda_0_arg_0__", IntType()),
                },
                instructions = new Instruction[]
                {
                    Return(Variable("__lambda_0_arg_0__")),
                },
                typeInfo = IntType(),
            };
            var member = new DelegateMember
            {
                id = "revision-twelve-delegate-default",
                projectId = ProjectId,
                name = "Transform",
                kind = MemberKind.NSDelegate,
                required = true,
                returnTypeInfo = IntType(),
                argumentTypes = new[] { argument },
                defaultValue = new DelegateMemberValueBase
                {
                    value = new NeoDelegateValue { action = action },
                },
                createdAt = "x",
                updatedAt = "x",
            };

            Assert.DoesNotThrow(() => BuildClient(
                new JsonMember[] { member },
                ReceiverClass((member.name, member.id))));
        }

        [Test]
        public void GenericEquals_DispatchesRuntimeOverrideBeforeValueEqualityFallback()
        {
            var otherArgument = new FunctionArgumentTypeInfo
            {
                name = "other",
                type = MemberKind.Class,
                required = true,
                classId = "receiver-class",
            };
            FunctionWithReturnType equalsAction = Action(
                BoolType(),
                new[] { otherArgument },
                Return(Boolean(false)));
            equalsAction.compilerRevision = 12;
            NSFunctionMember equals = ScriptFunction(
                "generic-equals",
                "Equals",
                deferred: false,
                BoolType(),
                new[] { otherArgument },
                equalsAction);
            ObjectMemberValue left = ObjectValue("generic-equals-left", "receiver-class");
            ObjectMemberValue right = ObjectValue("generic-equals-right", "receiver-class");
            NeoClient client = BuildClient(
                new JsonMember[] { equals },
                ReceiverClass(("Equals", equals.id)),
                additionalValues: new MemberValue[] { left, right });
            var call = new CallFunctionPointer
            {
                type = PointerKind.CallFunction,
                memberKey = "Equals",
                receiver = CallReceiver.Instance(Variable("__this__")),
                args = new Pointer[] { Variable("__root__") },
                missingMemberFallback = "valueEquality",
                callSiteId = "generic-equals-0",
            };
            var body = new FunctionWithReturnType
            {
                compilerRevision = 12,
                parameters = new[]
                {
                    Parameter("__this__", new ClassTypeInfo
                    {
                        type = MemberKind.Class,
                        required = true,
                        classId = "receiver-class",
                    }),
                    Parameter("__root__", new ClassTypeInfo
                    {
                        type = MemberKind.Class,
                        required = true,
                        classId = "receiver-class",
                    }),
                },
                instructions = new Instruction[] { Return(call) },
                typeInfo = BoolType(),
            };

            object? result = NSGetterEvaluator.Evaluate(
                body,
                new NSGetterEvaluator.Context(client, left.value, right.value));

            Assert.AreEqual(false, result);
        }

        [Test]
        public void GenericEquals_FallsBackForValuesWithoutACustomMember()
        {
            var call = new CallFunctionPointer
            {
                type = PointerKind.CallFunction,
                memberKey = "Equals",
                receiver = CallReceiver.Instance(Number(7)),
                args = new Pointer[] { Number(7) },
                missingMemberFallback = "valueEquality",
                callSiteId = "generic-equals-fallback",
            };
            FunctionWithReturnType body = Action(
                BoolType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Return(call));
            body.compilerRevision = 12;
            NeoClient client = BuildClient(Array.Empty<JsonMember>(), ReceiverClass());

            object? result = NSGetterEvaluator.Evaluate(
                body,
                new NSGetterEvaluator.Context(client, null, null));

            Assert.AreEqual(true, result);
        }

        [Test]
        public void GenericEquals_RejectsACustomMemberThatDoesNotReturnBool()
        {
            var otherArgument = new FunctionArgumentTypeInfo
            {
                name = "other",
                type = MemberKind.Class,
                required = true,
                classId = "receiver-class",
            };
            FunctionWithReturnType equalsAction = Action(
                IntType(),
                new[] { otherArgument },
                Return(Number(1)));
            equalsAction.compilerRevision = 12;
            NSFunctionMember equals = ScriptFunction(
                "invalid-generic-equals",
                "Equals",
                deferred: false,
                IntType(),
                new[] { otherArgument },
                equalsAction);
            ObjectMemberValue receiver = ObjectValue(
                "invalid-generic-equals-receiver",
                "receiver-class");
            NeoClient client = BuildClient(
                new JsonMember[] { equals },
                ReceiverClass(("Equals", equals.id)),
                additionalValues: new MemberValue[] { receiver });
            var call = new CallFunctionPointer
            {
                type = PointerKind.CallFunction,
                memberKey = "Equals",
                receiver = CallReceiver.Instance(Number(1)),
                args = new Pointer[] { Number(1) },
                missingMemberFallback = "valueEquality",
                callSiteId = "generic-equals-invalid",
            };

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.ValidateValueEqualitySignature(
                    call,
                    equals.id,
                    new NSGetterEvaluator.Context(client, receiver.value, null)))!;
            StringAssert.Contains("must return bool", error.Message);
        }

        [Test]
        public void DelegateMemberTarget_InvokesBoundNSFunctionReceiver()
        {
            FunctionArgumentTypeInfo amount = Argument("amount", MemberKind.Int);
            FunctionWithReturnType action = Action(
                IntType(),
                new[] { amount },
                Return(Add(Key(Variable("__this__"), "Count"), Variable("__arg_0__"))));
            var function = ScriptFunction(
                "delegate-target-function",
                "Compute",
                deferred: false,
                IntType(),
                new[] { amount },
                action);
            var count = new IntMember
            {
                id = "delegate-target-count",
                projectId = ProjectId,
                name = "Count",
                kind = MemberKind.Int,
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            ObjectMemberValue receiver = ObjectValue("receiver-value", "receiver-class");
            receiver.value!["Count"] = "delegate-target-count-value";
            NeoClient client = BuildClient(
                new JsonMember[] { function, count },
                ReceiverClass(("Count", count.id), ("Compute", function.id)),
                additionalValues: new MemberValue[]
                {
                    receiver,
                    new NumberMemberValue
                    {
                        id = "delegate-target-count-value",
                        value = 9,
                        createdAt = "x",
                        updatedAt = "x",
                    },
                });
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: new Dictionary<string, object?>());

            object? result = NSGetterEvaluator.InvokeDelegate(
                new NeoDelegateValue
                {
                    memberId = function.id,
                    valueId = "receiver-value",
                },
                new object?[] { 4 },
                ctx);

            Assert.AreEqual(13L, Convert.ToInt64(result));
        }

        [Test]
        public void DelegateMemberTarget_ReportsRecursiveTargetChain()
        {
            DelegateMember first = DelegateMemberTarget(
                "delegate-a", "First", "delegate-b");
            DelegateMember second = DelegateMemberTarget(
                "delegate-b", "Second", "delegate-a");
            NeoClient client = BuildClient(
                new JsonMember[] { first, second },
                ReceiverClass());
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: new Dictionary<string, object?>());

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.InvokeDelegate(
                    new NeoDelegateValue
                    {
                        memberId = first.id,
                        valueId = null,
                    },
                    Array.Empty<object?>(),
                    ctx))!;

            StringAssert.Contains("First[default] -> Second[default] -> First[default]", error.Message);
        }

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
        public void Json_LoopInstructionsRejectMalformedNestedWireShapes()
        {
            JObject validFor = ValidForInstructionJson();
            Assert.IsInstanceOf<ForInstruction>(
                JsonConvert.DeserializeObject<Instruction>(validFor.ToString()));

            JObject explicitNullFor = (JObject)validFor.DeepClone();
            explicitNullFor["iterator"]!["target"]!["writability"] =
                JValue.CreateNull();
            var parsedExplicitNullFor = (ForInstruction)
                JsonConvert.DeserializeObject<Instruction>(
                    explicitNullFor.ToString())!;
            Assert.IsNull(parsedExplicitNullFor.iterator.target.writability);

            JObject malformed = (JObject)validFor.DeepClone();
            malformed["initializer"]!["id"] = "";
            AssertInstructionRejected(malformed);

            malformed = (JObject)validFor.DeepClone();
            ((JObject)malformed["initializer"]!).Remove("pointer");
            AssertInstructionRejected(malformed);

            malformed = (JObject)validFor.DeepClone();
            ((JObject)malformed["condition"]!).Remove("condition");
            AssertInstructionRejected(malformed);

            malformed = (JObject)validFor.DeepClone();
            malformed["iterator"]!["type"] = InstructionKind.FunctionCall;
            AssertInstructionRejected(malformed);

            malformed = (JObject)validFor.DeepClone();
            ((JObject)malformed["iterator"]!).Remove("target");
            AssertInstructionRejected(malformed);

            JObject validForEach = ValidForEachInstructionJson();
            Assert.IsInstanceOf<ForEachInstruction>(
                JsonConvert.DeserializeObject<Instruction>(validForEach.ToString()));

            JObject explicitNullForEach = (JObject)validForEach.DeepClone();
            explicitNullForEach["binding"]!["writability"] =
                JValue.CreateNull();
            var parsedExplicitNullForEach = (ForEachInstruction)
                JsonConvert.DeserializeObject<Instruction>(
                    explicitNullForEach.ToString())!;
            Assert.IsNull(parsedExplicitNullForEach.binding.writability);

            malformed = (JObject)validForEach.DeepClone();
            malformed["binding"]!["id"] = "";
            AssertInstructionRejected(malformed);

            malformed = (JObject)validForEach.DeepClone();
            malformed["binding"]!["readonly"] = false;
            AssertInstructionRejected(malformed);

            malformed = (JObject)validForEach.DeepClone();
            malformed["collectionTypeInfo"]!["required"] = false;
            AssertInstructionRejected(malformed);

            malformed = (JObject)validForEach.DeepClone();
            malformed["collectionTypeInfo"]!["type"] = (int)MemberKind.Int;
            AssertInstructionRejected(malformed);

            malformed = (JObject)validForEach.DeepClone();
            ((JObject)malformed["collectionTypeInfo"]!).Remove("entryTypeInfo");
            AssertInstructionRejected(malformed);

            malformed = (JObject)validForEach.DeepClone();
            malformed["instructions"] = new JObject();
            AssertInstructionRejected(malformed);
        }

        [Test]
        public void ExecutionResult_ThenPreservesAnExistingFailure()
        {
            var error = new NSGetterRuntimeError("original");
            NeoScriptExecutionResult failed =
                NeoScriptExecutionResult.Failed(error);
            int continuationCalls = 0;

            NeoScriptExecutionResult chained = failed.Then(_ =>
            {
                continuationCalls++;
                return NeoScriptExecutionResult.Completed(
                    returned: false,
                    returnValue: null);
            });

            Assert.AreSame(failed, chained);
            Assert.AreSame(error, chained.Failure);
            Assert.AreEqual(0, continuationCalls);
        }

        [Test]
        public void Json_TryInstructionRejectsMalformedNestedWireShapes()
        {
            JObject valid = ValidTryInstructionJson();
            Instruction parsed = JsonConvert.DeserializeObject<Instruction>(
                valid.ToString())!;
            Assert.IsInstanceOf<TryInstruction>(parsed);
            Assert.IsInstanceOf<TryInstruction>(
                JsonConvert.DeserializeObject<Instruction>(
                    JsonConvert.SerializeObject(parsed)));

            JObject recursive = (JObject)valid.DeepClone();
            JObject filter = (JObject)recursive["catches"]![0]!["filter"]!;
            JToken condition = filter["condition"]!.DeepClone();
            filter["connective"] = new JObject
            {
                ["type"] = LogicalOpKind.And,
                ["to"] = new JObject
                {
                    ["condition"] = condition.DeepClone(),
                    ["connective"] = new JObject
                    {
                        ["type"] = LogicalOpKind.Or,
                        ["to"] = new JObject
                        {
                            ["condition"] = condition.DeepClone()
                        }
                    }
                }
            };
            Assert.IsInstanceOf<TryInstruction>(
                JsonConvert.DeserializeObject<Instruction>(
                    recursive.ToString()));

            JObject malformed = (JObject)valid.DeepClone();
            malformed["instructions"] = new JObject();
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["instructions"]![0]!["type"] = "unknownInstruction";
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["catches"]![0]!["filter"]!["condition"]!["type"] =
                "unknownComparison";
            AssertInstructionRejected(malformed);

            malformed = (JObject)recursive.DeepClone();
            malformed["catches"]![0]!["filter"]!["connective"]!["type"] =
                "unknownConnective";
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["catches"] = new JArray();
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["catches"]![0]!["binding"]!["readonly"] = false;
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["catches"]![0]!["binding"]!["typeInfo"]!["type"] =
                (int)MemberKind.Int;
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["catches"]![1]!["binding"]!["id"] = "message";
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["catches"]![0]!["filter"] = null;
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["catches"]![0]!["filter"] = new JObject();
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["catches"]![0]!["instructions"] = new JObject();
            AssertInstructionRejected(malformed);
        }

        [Test]
        public void Json_SwitchInstructionRejectsMalformedNestedWireShapes()
        {
            JObject valid = ValidSwitchInstructionJson();
            Instruction parsed = JsonConvert.DeserializeObject<Instruction>(
                valid.ToString())!;
            Assert.IsInstanceOf<SwitchInstruction>(parsed);
            Assert.IsInstanceOf<SwitchInstruction>(
                JsonConvert.DeserializeObject<Instruction>(
                    JsonConvert.SerializeObject(parsed)));

            JObject malformed = (JObject)valid.DeepClone();
            ((JObject)malformed).Remove("selector");
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["selectorTypeInfo"]!["type"] = (int)MemberKind.Float;
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["sections"] = new JObject();
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["sections"]![0]!["labels"] = new JArray();
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["sections"]![0]!["labels"]![0]!["typeInfo"]!["required"] = false;
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            ((JArray)malformed["sections"]![0]!["labels"]!).Add(
                malformed["sections"]![0]!["labels"]![0]!.DeepClone());
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["defaultInstructions"] = new JObject();
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["sections"]![0]!["labels"]![0]!["value"] =
                9007199254740992L;
            AssertInstructionRejected(malformed);

            malformed = (JObject)valid.DeepClone();
            malformed["selectorTypeInfo"]!["required"] = true;
            malformed["sections"]![0]!["labels"]![0] = JObject.Parse(@"{
                'typeInfo':{'type':0,'required':true},
                'value':null
            }");
            AssertInstructionRejected(malformed);

            AssertInstructionRejected(JObject.Parse("{'type':'switcheroo'}"));
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

        [Test]
        public void Invoke_ForLoopConsumesContinueAndBreakAtTheNearestLoop()
        {
            FunctionWithReturnType body = LoopAction(
                VariableDeclaration("sum", Number(0), IntType()),
                new ForInstruction
                {
                    type = InstructionKind.For,
                    initializer = LocalVariable("i", Number(0), IntType()),
                    condition = Compare(
                        OperatorKind.LessThan,
                        Variable("i"),
                        Number(6)),
                    iterator = AssignLocal(
                        "i",
                        Add(Variable("i"), Number(1)),
                        IntType()),
                    instructions = new Instruction[]
                    {
                        If(Compare(
                            OperatorKind.EqualTo,
                            Variable("i"),
                            Number(2)),
                            new ContinueInstruction
                            {
                                type = InstructionKind.Continue,
                            }),
                        If(Compare(
                            OperatorKind.EqualTo,
                            Variable("i"),
                            Number(5)),
                            new BreakInstruction
                            {
                                type = InstructionKind.Break,
                            }),
                        AssignLocal(
                            "sum",
                            Add(Variable("sum"), Variable("i")),
                            IntType()),
                    },
                },
                Return(Variable("sum")));
            NSFunctionMember function = ScriptFunction(
                "fn-for-loop",
                "ForLoop",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("ForLoop", function.id)));

            object? result = new NeoMemberNSFunction(client, function, null)
                .Invoke("receiver-value", Array.Empty<object?>());

            Assert.AreEqual(8L, Convert.ToInt64(result));
        }

        [Test]
        public void Invoke_ForEachSnapshotsMembershipBeforeLocalClear()
        {
            CollectionTypeInfo listType = ListType(IntType());
            FunctionWithReturnType body = LoopAction(
                VariableDeclaration(
                    "items",
                    List(Number(1), Number(2), Number(3)),
                    listType),
                VariableDeclaration("sum", Number(0), IntType()),
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
                    collectionPointer = Variable("items"),
                    collectionTypeInfo = listType,
                    instructions = new Instruction[]
                    {
                        new CollectionCallInstruction
                        {
                            type = InstructionKind.CollectionCall,
                            target = new WriteTarget
                            {
                                pointer = Variable("items"),
                                typeInfo = listType,
                                writability = WritabilityKind.Local,
                            },
                            mutation = CollectionMutationKind.Clear,
                            args = Array.Empty<Pointer>(),
                        },
                        AssignLocal(
                            "sum",
                            Add(Variable("sum"), Variable("item")),
                            IntType()),
                    },
                },
                Return(Variable("sum")));
            NSFunctionMember function = ScriptFunction(
                "fn-foreach-snapshot",
                "ForEachSnapshot",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("ForEachSnapshot", function.id)));

            object? result = new NeoMemberNSFunction(client, function, null)
                .Invoke("receiver-value", Array.Empty<object?>());

            Assert.AreEqual(6L, Convert.ToInt64(result));
        }

        [Test]
        public void Invoke_ForEachDictionaryMatchesEcmaScriptPropertyOrder()
        {
            PrimitiveTypeInfo stringType = StringType();
            CollectionTypeInfo dictionaryType = DictionaryType(stringType);
            FunctionWithReturnType body = LoopAction(
                stringType,
                VariableDeclaration("seen", Text(""), stringType),
                new ForEachInstruction
                {
                    type = InstructionKind.ForEach,
                    binding = new LoopBinding
                    {
                        id = "item",
                        typeInfo = stringType,
                        isReadonly = true,
                        writability = WritabilityKind.ReadOnly,
                    },
                    collectionPointer = Dictionary(
                        dictionaryType,
                        ("10", "A"),
                        ("2", "B"),
                        ("a", "C"),
                        ("01", "D"),
                        ("4294967294", "E"),
                        ("4294967295", "F"),
                        ("0", "G")),
                    collectionTypeInfo = dictionaryType,
                    instructions = new Instruction[]
                    {
                        AssignLocal(
                            "seen",
                            Add(Variable("seen"), Variable("item")),
                            stringType),
                    },
                },
                Return(Variable("seen")));
            NSFunctionMember function = ScriptFunction(
                "fn-foreach-ecma-order",
                "ForEachEcmaOrder",
                false,
                stringType,
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("ForEachEcmaOrder", function.id)));

            object? result = new NeoMemberNSFunction(client, function, null)
                .Invoke("receiver-value", Array.Empty<object?>());

            // Array indices: 0, 2, 10, 4294967294. Remaining string keys
            // retain insertion order: a, 01, 4294967295.
            Assert.AreEqual("GBAECDF", result);
        }

        [Test]
        public void Invoke_ForEachLookupUsesAndRetainsSessionShadowAfterRemoval()
        {
            LookupTypeInfo lookupType = LookupType(IntType());
            FunctionMember removeShadow = NativeFunction(
                "fn-remove-lookup-shadow",
                "RemoveLookupShadow",
                deferred: false);
            FunctionWithReturnType body = LoopAction(
                VariableDeclaration("seen", Number(0), IntType()),
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
                    collectionPointer = Reference(LookupSelectorValueId),
                    collectionTypeInfo = lookupType,
                    instructions = new Instruction[]
                    {
                        AssignLocal(
                            "seen",
                            Add(
                                Multiply(Variable("seen"), Number(10)),
                                Variable("item")),
                            IntType()),
                        new FunctionCallInstruction
                        {
                            type = InstructionKind.FunctionCall,
                            call = Call(
                                removeShadow.id,
                                "remove-lookup-shadow"),
                        },
                    },
                },
                Return(Variable("seen")));
            NSFunctionMember function = ScriptFunction(
                "fn-foreach-lookup-overlay",
                "ForEachLookupOverlay",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildLookupOverlayClient(
                removeShadow,
                function);
            int removalCalls = 0;
            client.RegisterNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    [removeShadow.id] = (currentClient, _, _) =>
                    {
                        removalCalls++;
                        currentClient.RemoveWritableValueAndDescendantsIfUnlinked(
                            NeoValueOwnership.Session,
                            LookupTargetValueId,
                            new IntMember { id = "lookup-target-removal" });
                        return 0;
                    },
                });

            object? result = new NeoMemberNSFunction(client, function, null)
                .Invoke("receiver-value", Array.Empty<object?>());

            Assert.AreEqual(99L, Convert.ToInt64(result));
            Assert.AreEqual(2, removalCalls);
            Assert.IsFalse(client.HasWritableValue(
                NeoValueOwnership.Session,
                LookupTargetValueId));
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Asset,
                LookupTargetValueId,
                out NumberMemberValue? authored));
            Assert.AreEqual(1, authored!.value);
        }

        [Test]
        public void Invoke_LookupWherePredicateUsesSessionTargetOverlay()
        {
            var predicate = new FunctionWithReturnType
            {
                parameters = new[]
                {
                    Parameter("entry", IntType()),
                },
                instructions = new Instruction[]
                {
                    Return(EqualTo(Variable("entry"), Number(9))),
                },
                typeInfo = BoolType(),
            };
            var where = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new WhereFunction
                {
                    type = FunctionKind.Where,
                    info = new FunctionCollectionBoolInfo
                    {
                        collectionPointer = Reference(LookupSelectorValueId),
                        function = predicate,
                    },
                },
            };
            var count = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CountFunction
                {
                    type = FunctionKind.Count,
                    info = new FunctionCollectionInfo
                    {
                        collectionPointer = where,
                    },
                },
            };
            NSFunctionMember function = ScriptFunction(
                "fn-lookup-where-overlay",
                "LookupWhereOverlay",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                LoopAction(Return(count)));
            NeoClient client = BuildLookupOverlayClient(function);

            object? result = new NeoMemberNSFunction(client, function, null)
                .Invoke("receiver-value", Array.Empty<object?>());

            Assert.AreEqual(2L, Convert.ToInt64(result));
        }

        [Test]
        public void Invoke_ForEachCoercesExplicitIntBindingToDecimal()
        {
            PrimitiveTypeInfo decimalType = DecimalType();
            CollectionTypeInfo listType = ListType(IntType());
            FunctionWithReturnType body = LoopAction(
                decimalType,
                new ForEachInstruction
                {
                    type = InstructionKind.ForEach,
                    binding = new LoopBinding
                    {
                        id = "item",
                        typeInfo = decimalType,
                        isReadonly = true,
                        writability = WritabilityKind.ReadOnly,
                    },
                    collectionPointer = List(Number(42)),
                    collectionTypeInfo = listType,
                    instructions = new Instruction[]
                    {
                        Return(Variable("item")),
                    },
                },
                Return(Text("unreachable")));
            NSFunctionMember function = ScriptFunction(
                "fn-foreach-decimal-binding",
                "ForEachDecimalBinding",
                false,
                decimalType,
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("ForEachDecimalBinding", function.id)));

            object? result = new NeoMemberNSFunction(client, function, null)
                .Invoke("receiver-value", Array.Empty<object?>());

            Assert.AreEqual("42", result);
        }

        [Test]
        public void Invoke_ThrowEscapesNestedLoopsUnchanged()
        {
            CollectionTypeInfo listType = ListType(IntType());
            NSFunctionMember function = ScriptFunction(
                "fn-loop-throw",
                "LoopThrow",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                LoopAction(
                    new ForInstruction
                    {
                        type = InstructionKind.For,
                        initializer = LocalVariable("i", Number(0), IntType()),
                        condition = Compare(
                            OperatorKind.LessThan,
                            Variable("i"),
                            Number(1)),
                        iterator = AssignLocal(
                            "i",
                            Add(Variable("i"), Number(1)),
                            IntType()),
                        instructions = new Instruction[]
                        {
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
                                collectionPointer = List(Number(1)),
                                collectionTypeInfo = listType,
                                instructions = new Instruction[]
                                {
                                    new ThrowInstruction
                                    {
                                        type = InstructionKind.Throw,
                                        pointer = Text("nested loop failure"),
                                    },
                                },
                            },
                        },
                    },
                    Return(Number(0))));
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("LoopThrow", function.id)));

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                new NeoMemberNSFunction(client, function, null)
                    .Invoke("receiver-value", Array.Empty<object?>()))!;

            Assert.AreEqual("nested loop failure", error.Message);
        }

        [Test]
        public void Invoke_ForEachBindingRemainsReadOnlyWithStaleTargetStamp()
        {
            CollectionTypeInfo listType = ListType(IntType());
            NSFunctionMember function = ScriptFunction(
                "fn-foreach-readonly",
                "ForEachReadOnly",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                LoopAction(
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
                        collectionPointer = List(Number(1)),
                        collectionTypeInfo = listType,
                        instructions = new Instruction[]
                        {
                            // Deliberately lie in the target stamp. Runtime
                            // loop state must still enforce binding readonly.
                            AssignLocal("item", Number(2), IntType()),
                        },
                    },
                    Return(Number(0))));
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("ForEachReadOnly", function.id)));

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                new NeoMemberNSFunction(client, function, null)
                    .Invoke("receiver-value", Array.Empty<object?>()))!;

            StringAssert.Contains("read-only foreach iterator", error.Message);
        }

        [Test]
        public void Invoke_NestedForEachWithReusedBindingIdKeepsOuterBindingReadOnly()
        {
            CollectionTypeInfo listType = ListType(IntType());
            var inner = new ForEachInstruction
            {
                type = InstructionKind.ForEach,
                binding = new LoopBinding
                {
                    id = "item",
                    typeInfo = IntType(),
                    isReadonly = true,
                    writability = WritabilityKind.ReadOnly,
                },
                collectionPointer = List(Number(2)),
                collectionTypeInfo = listType,
                instructions = Array.Empty<Instruction>(),
            };
            NSFunctionMember function = ScriptFunction(
                "fn-nested-foreach-readonly",
                "NestedForEachReadOnly",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                LoopAction(
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
                        collectionPointer = List(Number(1)),
                        collectionTypeInfo = listType,
                        instructions = new Instruction[]
                        {
                            inner,
                            // Malformed nested IR reused the outer binding id.
                            // Finishing the inner loop must not remove the
                            // outer loop's runtime read-only registration.
                            AssignLocal("item", Number(3), IntType()),
                        },
                    },
                    Return(Number(0))));
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("NestedForEachReadOnly", function.id)));

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                new NeoMemberNSFunction(client, function, null)
                    .Invoke("receiver-value", Array.Empty<object?>()))!;

            StringAssert.Contains("read-only foreach iterator", error.Message);
        }

        [Test]
        public void Invoke_LoopBodyUsesAFreshChildScopeForEveryIteration()
        {
            FunctionWithReturnType body = LoopAction(
                new ForInstruction
                {
                    type = InstructionKind.For,
                    initializer = LocalVariable("i", Number(0), IntType()),
                    condition = Compare(
                        OperatorKind.LessThan,
                        Variable("i"),
                        Number(2)),
                    iterator = AssignLocal(
                        "i",
                        Add(Variable("i"), Number(1)),
                        IntType()),
                    instructions = new Instruction[]
                    {
                        If(
                            Compare(
                                OperatorKind.EqualTo,
                                Variable("i"),
                                Number(0)),
                            VariableDeclaration(
                                "bodyOnly",
                                Number(7),
                                IntType())),
                        If(
                            Compare(
                                OperatorKind.EqualTo,
                                Variable("i"),
                                Number(1)),
                            Return(Variable("bodyOnly"))),
                    },
                },
                Return(Number(0)));

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                InvokeTryBody(body, IntType()))!;

            StringAssert.Contains("bodyOnly", error.Message);
            StringAssert.Contains("not in scope", error.Message);
        }

        [Test]
        public void Invoke_RevisionThreeRejectsEveryP50InstructionAtAnyDepth()
        {
            CollectionTypeInfo listType = ListType(IntType());
            FunctionWithReturnType[] bodies =
            {
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    new ForInstruction
                    {
                        type = InstructionKind.For,
                        initializer = LocalVariable("i", Number(0), IntType()),
                        condition = Compare(
                            OperatorKind.LessThan,
                            Variable("i"),
                            Number(0)),
                        iterator = AssignLocal(
                            "i",
                            Add(Variable("i"), Number(1)),
                            IntType()),
                        instructions = Array.Empty<Instruction>(),
                    },
                    Return(Number(0))),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    new ForEachInstruction
                    {
                        type = InstructionKind.ForEach,
                        binding = new LoopBinding
                        {
                            id = "item",
                            typeInfo = IntType(),
                            isReadonly = true,
                        },
                        collectionPointer = List(),
                        collectionTypeInfo = listType,
                        instructions = Array.Empty<Instruction>(),
                    },
                    Return(Number(0))),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    new BreakInstruction { type = InstructionKind.Break },
                    Return(Number(0))),
                Action(
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    If(
                        Compare(
                            OperatorKind.EqualTo,
                            Number(1),
                            Number(1)),
                        new ContinueInstruction
                        {
                            type = InstructionKind.Continue,
                        }),
                    Return(Number(0))),
            };

            foreach (FunctionWithReturnType body in bodies)
            {
                body.compilerRevision = 3;
                NeoScriptPreExecutionValidationError error =
                    Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                        InvokeTryBody(body, IntType()))!;
                StringAssert.Contains(
                    "loop IR requires compiler revision 4",
                    error.Message);
            }
        }

        [Test]
        public void Invoke_ForLoopEnforcesTheSharedIterationBudget()
        {
            FunctionWithReturnType body = LoopAction(
                new ForInstruction
                {
                    type = InstructionKind.For,
                    initializer = LocalVariable("i", Number(0), IntType()),
                    condition = Compare(
                        OperatorKind.EqualTo,
                        Number(1),
                        Number(1)),
                    iterator = AssignLocal(
                        "i",
                        Add(Variable("i"), Number(1)),
                        IntType()),
                    instructions = Array.Empty<Instruction>(),
                },
                Return(Number(0)));
            NSFunctionMember function = ScriptFunction(
                "fn-loop-budget",
                "LoopBudget",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("LoopBudget", function.id)));

            NeoScriptResourceLimitError error = Assert.Throws<NeoScriptResourceLimitError>(() =>
                new NeoMemberNSFunction(client, function, null)
                    .Invoke("receiver-value", Array.Empty<object?>()))!;

            Assert.AreEqual(
                "NeoScript loop iteration limit of 10000 exceeded.",
                error.Message);
        }

        [Test]
        public void Invoke_LoopBudgetIsSharedAcrossNestedNSFunctionCalls()
        {
            NSFunctionMember inner = ScriptFunction(
                "fn-loop-budget-inner",
                "LoopBudgetInner",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                LoopAction(
                    new ForInstruction
                    {
                        type = InstructionKind.For,
                        initializer = LocalVariable("i", Number(0), IntType()),
                        condition = Compare(
                            OperatorKind.LessThan,
                            Variable("i"),
                            Number(6000)),
                        iterator = AssignLocal(
                            "i",
                            Add(Variable("i"), Number(1)),
                            IntType()),
                        instructions = Array.Empty<Instruction>(),
                    },
                    Return(Number(0))));
            NSFunctionMember outer = ScriptFunction(
                "fn-loop-budget-outer",
                "LoopBudgetOuter",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                LoopAction(
                    new FunctionCallInstruction
                    {
                        type = InstructionKind.FunctionCall,
                        call = Call(inner.id, "loop-budget-inner-first"),
                    },
                    new FunctionCallInstruction
                    {
                        type = InstructionKind.FunctionCall,
                        call = Call(inner.id, "loop-budget-inner-second"),
                    },
                    Return(Number(0))));
            NeoClient client = BuildClient(
                new JsonMember[] { inner, outer },
                ReceiverClass(
                    ("LoopBudgetInner", inner.id),
                    ("LoopBudgetOuter", outer.id)));

            NeoScriptResourceLimitError error = Assert.Throws<NeoScriptResourceLimitError>(() =>
                new NeoMemberNSFunction(client, outer, null)
                    .Invoke("receiver-value", Array.Empty<object?>()))!;

            Assert.AreEqual(
                "NeoScript loop iteration limit of 10000 exceeded.",
                error.Message);
        }

        [Test]
        public void Invoke_TryDoesNotCatchUnavailableInvokersOrHostExceptions()
        {
            FunctionMember native = NativeFunction(
                "fn-host-boundary",
                "HostBoundary",
                deferred: false);
            NSFunctionMember function = ScriptFunction(
                "fn-try-host-boundary",
                "TryHostBoundary",
                deferred: false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                TryAction(
                    IntType(),
                    TryBlock(
                        new Instruction[]
                        {
                            new FunctionCallInstruction
                            {
                                type = InstructionKind.FunctionCall,
                                call = Call(native.id, "host-boundary"),
                            },
                            Return(Number(0)),
                        },
                        Catch("message", null, Return(Number(1))))));
            NeoClient client = BuildClient(
                new JsonMember[] { native, function },
                ReceiverClass(
                    ("HostBoundary", native.id),
                    ("TryHostBoundary", function.id)));
            NSGetterRuntimeError unavailable =
                Assert.Catch<NSGetterRuntimeError>(() =>
                    new NeoMemberNSFunction(client, function, null)
                        .Invoke("receiver-value", Array.Empty<object?>()))!;
            StringAssert.Contains("requires constructing", unavailable.Message);

            var hostError = new InvalidOperationException("host failure");
            client.RegisterNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    [native.id] = (_, _, _) => throw hostError,
                });
            Assert.AreSame(
                hostError,
                Assert.Throws<InvalidOperationException>(() =>
                    new NeoMemberNSFunction(client, function, null)
                        .Invoke("receiver-value", Array.Empty<object?>())));
        }

        [Test]
        public void Invoke_TryDoesNotCatchCalledBodyValidationFailures()
        {
            FunctionWithReturnType futureRevision = Action(
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Return(Number(0)));
            futureRevision.compilerRevision =
                FunctionWithReturnType.CurrentCompilerRevision + 1;
            AssertCalledValidationBypassesCatch(
                ScriptFunction(
                    "fn-future-revision-callee",
                    "FutureRevisionCallee",
                    false,
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    futureRevision),
                "Unsupported NeoScript compiler revision");

            FunctionWithReturnType oldLoopRevision = Action(
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                new BreakInstruction { type = InstructionKind.Break },
                Return(Number(0)));
            oldLoopRevision.compilerRevision = 3;
            AssertCalledValidationBypassesCatch(
                ScriptFunction(
                    "fn-old-loop-revision-callee",
                    "OldLoopRevisionCallee",
                    false,
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    oldLoopRevision),
                "loop IR requires compiler revision 4");

            CollectionTypeInfo listType = ListType(IntType());
            AssertCalledValidationBypassesCatch(
                ScriptFunction(
                    "fn-malformed-loop-callee",
                    "MalformedLoopCallee",
                    false,
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    LoopAction(
                        new ForEachInstruction
                        {
                            type = InstructionKind.ForEach,
                            binding = new LoopBinding
                            {
                                id = "item",
                                typeInfo = IntType(),
                                isReadonly = false,
                            },
                            collectionPointer = List(Number(1)),
                            collectionTypeInfo = listType,
                            instructions = Array.Empty<Instruction>(),
                        },
                        Return(Number(0)))),
                "foreach loop contains malformed metadata");

            AssertCalledValidationBypassesCatch(
                ScriptFunction(
                    "fn-malformed-switch-callee",
                    "MalformedSwitchCallee",
                    false,
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    SwitchAction(
                        Switch(
                            Number(1),
                            IntType(),
                            new[]
                            {
                                new SwitchSection
                                {
                                    labels = Array.Empty<Value>(),
                                    instructions = Array.Empty<Instruction>(),
                                },
                            }),
                        Return(Number(0)))),
                "malformed case section");

            AssertCalledValidationBypassesCatch(
                ScriptFunction(
                    "fn-malformed-try-callee",
                    "MalformedTryCallee",
                    false,
                    IntType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    TryAction(
                        IntType(),
                        new TryInstruction
                        {
                            type = InstructionKind.Try,
                            instructions = Array.Empty<Instruction>(),
                            catches = Array.Empty<CatchClause>(),
                        },
                        Return(Number(0)))),
                "missing its body or catch clauses");
        }

        [Test]
        public void Invoke_TryDoesNotCatchMalformedNestedControlFlowMetadata()
        {
            Instruction[] malformedInstructions =
            {
                Switch(
                    Number(1),
                    IntType(),
                    new[]
                    {
                        new SwitchSection
                        {
                            labels = Array.Empty<Value>(),
                            instructions = Array.Empty<Instruction>(),
                        },
                    }),
                new TryInstruction
                {
                    type = InstructionKind.Try,
                    instructions = Array.Empty<Instruction>(),
                    catches = Array.Empty<CatchClause>(),
                },
            };

            foreach (Instruction malformed in malformedInstructions)
            {
                FunctionWithReturnType body = TryAction(
                    IntType(),
                    TryBlock(
                        new[] { malformed },
                        Catch("message", null, Return(Number(1)))),
                    Return(Number(0)));

                Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                    InvokeTryBody(body, IntType()));
            }
        }

        [Test]
        public void Invoke_TryStillCatchesDeliberateCalledRuntimeErrors()
        {
            NSFunctionMember callee = ScriptFunction(
                "fn-called-runtime-error",
                "CalledRuntimeError",
                false,
                StringType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                Action(
                    StringType(),
                    Array.Empty<FunctionArgumentTypeInfo>(),
                    Throw(Text("called boom"))));
            NSFunctionMember caller = ScriptFunction(
                "fn-catch-called-runtime-error",
                "CatchCalledRuntimeError",
                false,
                StringType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                TryAction(
                    StringType(),
                    TryBlock(
                        new Instruction[]
                        {
                            new FunctionCallInstruction
                            {
                                type = InstructionKind.FunctionCall,
                                call = Call(callee.id, "called-runtime-error"),
                            },
                            Return(Text("wrong")),
                        },
                        Catch(
                            "message",
                            null,
                            Return(Variable("message"))))));
            NeoClient client = BuildClient(
                new JsonMember[] { callee, caller },
                ReceiverClass(
                    (callee.name, callee.id),
                    (caller.name, caller.id)));

            object? result = new NeoMemberNSFunction(client, caller, null)
                .Invoke("receiver-value", Array.Empty<object?>());

            Assert.AreEqual("called boom", result);
        }

        [Test]
        public void Invoke_TryCatchFiltersScopesAndSelectedCatchFailures()
        {
            object? result = InvokeTryBody(
                TryAction(
                    IntType(),
                    VariableDeclaration("count", Number(0), IntType()),
                    TryBlock(
                        new Instruction[]
                        {
                            AssignLocal("count", Number(2), IntType()),
                            Throw(Text("original")),
                        },
                        Catch(
                            "first",
                            Compare(
                                OperatorKind.EqualTo,
                                Variable("first"),
                                Text("other")),
                            AssignLocal("count", Number(99), IntType())),
                        Catch(
                            "selected",
                            Compare(
                                OperatorKind.EqualTo,
                                Variable("selected"),
                                Text("original")),
                            AssignLocal("count", Number(3), IntType())),
                        Catch(
                            "fallback",
                            null,
                            AssignLocal("count", Number(100), IntType()))),
                    Return(Variable("count"))),
                IntType());
            Assert.AreEqual(3L, Convert.ToInt64(result));

            result = InvokeTryBody(
                TryAction(
                    StringType(),
                    TryBlock(
                        new Instruction[] { Throw(Text("original")) },
                        Catch(
                            "filtered",
                            Compare(
                                OperatorKind.EqualTo,
                                Variable("missing"),
                                Text("never")),
                            Return(Text("wrong"))),
                        Catch(
                            "fallback",
                            null,
                            Return(Variable("fallback"))))),
                StringType());
            Assert.AreEqual("original", result);

            FunctionWithReturnType unmatched = TryAction(
                IntType(),
                TryBlock(
                    new Instruction[] { Throw(Text("original")) },
                    Catch(
                        "message",
                        Compare(
                            OperatorKind.EqualTo,
                            Variable("message"),
                            Text("different")),
                        Return(Number(1)))),
                Return(Number(0)));
            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                InvokeTryBody(unmatched, IntType()))!;
            Assert.AreEqual("original", error.Message);

            result = InvokeTryBody(
                TryAction(
                    StringType(),
                    TryBlock(
                        new Instruction[]
                        {
                            TryBlock(
                                new Instruction[] { Throw(Text("original")) },
                                Catch(
                                    "inner",
                                    Compare(
                                        OperatorKind.EqualTo,
                                        Variable("inner"),
                                        Text("original")),
                                    Throw(Text("selected"))),
                                Catch(
                                    "innerFallback",
                                    null,
                                    Return(Text("wrong")))),
                        },
                        Catch(
                            "outer",
                            null,
                            Return(Variable("outer"))))),
                StringType());
            Assert.AreEqual("selected", result);

            FunctionWithReturnType readOnly = TryAction(
                IntType(),
                TryBlock(
                    new Instruction[] { Throw(Text("original")) },
                    Catch(
                        "message",
                        null,
                        AssignLocal("message", Text("changed"), StringType()),
                        Return(Number(0)))));
            error = Assert.Throws<NSGetterRuntimeError>(() =>
                InvokeTryBody(readOnly, IntType()))!;
            Assert.AreEqual(
                "Cannot assign to a read-only catch message binding.",
                error.Message);

            result = InvokeTryBody(
                TryAction(
                    IntType(),
                    TryBlock(
                        new Instruction[] { Return(Number(7)) },
                        Catch("message", null, Return(Number(0))))),
                IntType());
            Assert.AreEqual(7L, Convert.ToInt64(result));
        }

        [Test]
        public void Invoke_TryPropagatesLoopControlAndRejectsOldRevision()
        {
            FunctionWithReturnType body = TryAction(
                IntType(),
                VariableDeclaration("sum", Number(0), IntType()),
                new ForInstruction
                {
                    type = InstructionKind.For,
                    initializer = LocalVariable("i", Number(0), IntType()),
                    condition = Compare(
                        OperatorKind.LessThan,
                        Variable("i"),
                        Number(4)),
                    iterator = AssignLocal(
                        "i",
                        Add(Variable("i"), Number(1)),
                        IntType()),
                    instructions = new Instruction[]
                    {
                        TryBlock(
                            new Instruction[]
                            {
                                If(
                                    Compare(
                                        OperatorKind.EqualTo,
                                        Variable("i"),
                                        Number(1)),
                                    new ContinueInstruction
                                    {
                                        type = InstructionKind.Continue,
                                    }),
                                If(
                                    Compare(
                                        OperatorKind.EqualTo,
                                        Variable("i"),
                                        Number(3)),
                                    new BreakInstruction
                                    {
                                        type = InstructionKind.Break,
                                    }),
                                AssignLocal(
                                    "sum",
                                    Add(Variable("sum"), Number(1)),
                                    IntType()),
                            },
                            Catch("message", null, Return(Number(99)))),
                    },
                },
                Return(Variable("sum")));
            Assert.AreEqual(
                2L,
                Convert.ToInt64(InvokeTryBody(body, IntType())));

            body.compilerRevision = 5;
            NeoScriptPreExecutionValidationError error =
                Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                    InvokeTryBody(body, IntType()))!;
            StringAssert.Contains("requires compiler revision 6", error.Message);
        }

        [Test]
        public void Invoke_SwitchMatchesEverySupportedLabelShape()
        {
            Assert.AreEqual(
                1,
                InvokeSwitchCase(Number(2), IntType(), SwitchLabel(IntType(), 2)));
            Assert.AreEqual(
                1,
                InvokeSwitchCase(Text("east"), StringType(), SwitchLabel(StringType(), "east")));
            Assert.AreEqual(
                1,
                InvokeSwitchCase(Boolean(true), BoolType(), SwitchLabel(BoolType(), true)));

            EnumTypeInfo enumType = EnumType("enum-direction");
            Assert.AreEqual(
                1,
                InvokeSwitchCase(
                    Literal(enumType, new JArray("option-east")),
                    enumType,
                    SwitchLabel(EnumType("enum-direction"), new JArray("option-east"))));

            PrimitiveTypeInfo optionalInt = IntType(required: false);
            Assert.AreEqual(
                1,
                InvokeSwitchCase(
                    Literal(optionalInt, JValue.CreateNull()),
                    optionalInt,
                    SwitchLabel(NullType(), JValue.CreateNull())));
        }

        [Test]
        public void Invoke_SwitchStackedLabelsPrecedeDefaultAndConsumeBreak()
        {
            FunctionWithReturnType body = SwitchAction(
                VariableDeclaration("result", Number(0), IntType()),
                Switch(
                    Number(2),
                    IntType(),
                    new[]
                    {
                        new SwitchSection
                        {
                            labels = new[]
                            {
                                SwitchLabel(IntType(), 1),
                                SwitchLabel(IntType(), 2),
                            },
                            instructions = new Instruction[]
                            {
                                AssignLocal("result", Number(7), IntType()),
                                new BreakInstruction
                                {
                                    type = InstructionKind.Break,
                                },
                            },
                        },
                    },
                    new Instruction[]
                    {
                        AssignLocal("result", Number(9), IntType()),
                        new BreakInstruction { type = InstructionKind.Break },
                    }),
                Switch(
                    Number(99),
                    IntType(),
                    new[]
                    {
                        new SwitchSection
                        {
                            labels = new[] { SwitchLabel(IntType(), 1) },
                            instructions = new Instruction[]
                            {
                                AssignLocal("result", Number(99), IntType()),
                                new BreakInstruction
                                {
                                    type = InstructionKind.Break,
                                },
                            },
                        },
                    }),
                Return(Variable("result")));
            NSFunctionMember function = ScriptFunction(
                "fn-switch-stacked",
                "SwitchStacked",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("SwitchStacked", function.id)));

            object? result = new NeoMemberNSFunction(client, function, null)
                .Invoke("receiver-value", Array.Empty<object?>());

            Assert.AreEqual(7L, Convert.ToInt64(result));
        }

        [Test]
        public void Invoke_SwitchPropagatesContinueToLoopButConsumesItsOwnBreak()
        {
            FunctionWithReturnType body = SwitchAction(
                VariableDeclaration("sum", Number(0), IntType()),
                new ForInstruction
                {
                    type = InstructionKind.For,
                    initializer = LocalVariable("i", Number(0), IntType()),
                    condition = Compare(
                        OperatorKind.LessThan,
                        Variable("i"),
                        Number(4)),
                    iterator = AssignLocal(
                        "i",
                        Add(Variable("i"), Number(1)),
                        IntType()),
                    instructions = new Instruction[]
                    {
                        Switch(
                            Variable("i"),
                            IntType(),
                            new[]
                            {
                                new SwitchSection
                                {
                                    labels = new[]
                                    {
                                        SwitchLabel(IntType(), 1),
                                    },
                                    instructions = new Instruction[]
                                    {
                                        new ContinueInstruction
                                        {
                                            type = InstructionKind.Continue,
                                        },
                                    },
                                },
                                new SwitchSection
                                {
                                    labels = new[]
                                    {
                                        SwitchLabel(IntType(), 3),
                                    },
                                    instructions = new Instruction[]
                                    {
                                        new BreakInstruction
                                        {
                                            type = InstructionKind.Break,
                                        },
                                    },
                                },
                            }),
                        AssignLocal(
                            "sum",
                            Add(Variable("sum"), Number(10)),
                            IntType()),
                    },
                },
                Return(Variable("sum")));
            NSFunctionMember function = ScriptFunction(
                "fn-switch-loop-control",
                "SwitchLoopControl",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("SwitchLoopControl", function.id)));

            object? result = new NeoMemberNSFunction(client, function, null)
                .Invoke("receiver-value", Array.Empty<object?>());

            Assert.AreEqual(30L, Convert.ToInt64(result));
        }

        [Test]
        public void Invoke_SwitchSectionLocalsDoNotEscape()
        {
            FunctionWithReturnType body = SwitchAction(
                Switch(
                    Number(1),
                    IntType(),
                    new[]
                    {
                        new SwitchSection
                        {
                            labels = new[] { SwitchLabel(IntType(), 1) },
                            instructions = new Instruction[]
                            {
                                VariableDeclaration(
                                    "section-only",
                                    Number(7),
                                    IntType()),
                                new BreakInstruction
                                {
                                    type = InstructionKind.Break,
                                },
                            },
                        },
                    }),
                Return(Variable("section-only")));
            NSFunctionMember function = ScriptFunction(
                "fn-switch-scope",
                "SwitchScope",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("SwitchScope", function.id)));

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                new NeoMemberNSFunction(client, function, null)
                    .Invoke("receiver-value", Array.Empty<object?>()))!;

            StringAssert.Contains("Variable 'section-only' is not in scope", error.Message);
        }

        [Test]
        public void Invoke_SwitchInsideForEachPreservesIteratorReadOnlyBinding()
        {
            CollectionTypeInfo listType = ListType(IntType());
            NSFunctionMember function = ScriptFunction(
                "fn-switch-foreach-readonly",
                "SwitchForEachReadOnly",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                SwitchAction(
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
                        collectionPointer = List(Number(1)),
                        collectionTypeInfo = listType,
                        instructions = new Instruction[]
                        {
                            Switch(
                                Number(1),
                                IntType(),
                                new[]
                                {
                                    new SwitchSection
                                    {
                                        labels = new[]
                                        {
                                            SwitchLabel(IntType(), 1),
                                        },
                                        instructions = new Instruction[]
                                        {
                                            // Deliberately malformed IR:
                                            // the switch child scope must
                                            // retain the enclosing foreach
                                            // iterator's read-only metadata.
                                            AssignLocal(
                                                "item",
                                                Number(2),
                                                IntType()),
                                            new BreakInstruction
                                            {
                                                type = InstructionKind.Break,
                                            },
                                        },
                                    },
                                }),
                        },
                    },
                    Return(Number(0))));
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("SwitchForEachReadOnly", function.id)));

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                new NeoMemberNSFunction(client, function, null)
                    .Invoke("receiver-value", Array.Empty<object?>()))!;

            StringAssert.Contains("read-only foreach iterator", error.Message);
        }

        [Test]
        public void Invoke_SwitchRejectsMismatchedSelectorDuplicateLabelsAndOldRevision()
        {
            SwitchInstruction mismatch = Switch(
                Text("1"),
                IntType(),
                new[]
                {
                    new SwitchSection
                    {
                        labels = new[] { SwitchLabel(IntType(), 1) },
                        instructions = new Instruction[]
                        {
                            new BreakInstruction { type = InstructionKind.Break },
                        },
                    },
                });
            NSGetterRuntimeError error = InvokeSwitchError(mismatch);
            StringAssert.Contains("selector is inconsistent", error.Message);

            SwitchInstruction mismatchedLabel = Switch(
                Number(1),
                IntType(),
                new[]
                {
                    new SwitchSection
                    {
                        labels = new[] { SwitchLabel(StringType(), "1") },
                        instructions = new Instruction[]
                        {
                            new BreakInstruction { type = InstructionKind.Break },
                        },
                    },
                });
            error = InvokeSwitchError(mismatchedLabel, validation: true);
            StringAssert.Contains("case label is inconsistent", error.Message);

            SwitchInstruction duplicate = Switch(
                Number(1),
                IntType(),
                new[]
                {
                    new SwitchSection
                    {
                        labels = new[] { SwitchLabel(IntType(), 1) },
                        instructions = new Instruction[]
                        {
                            new BreakInstruction { type = InstructionKind.Break },
                        },
                    },
                    new SwitchSection
                    {
                        labels = new[] { SwitchLabel(IntType(), 1) },
                        instructions = new Instruction[]
                        {
                            new BreakInstruction { type = InstructionKind.Break },
                        },
                    },
                });
            error = InvokeSwitchError(duplicate, validation: true);
            StringAssert.Contains("duplicate normalized case label", error.Message);

            SwitchInstruction fallthrough = Switch(
                Number(1),
                IntType(),
                new[]
                {
                    new SwitchSection
                    {
                        labels = new[] { SwitchLabel(IntType(), 1) },
                        instructions = Array.Empty<Instruction>(),
                    },
                });
            error = InvokeSwitchError(fallthrough, validation: true);
            StringAssert.Contains("selected section reached its end", error.Message);

            FunctionWithReturnType stale = SwitchAction(
                Switch(
                    Number(1),
                    IntType(),
                    Array.Empty<SwitchSection>()),
                Return(Number(0)));
            stale.compilerRevision = 4;
            NSFunctionMember function = ScriptFunction(
                "fn-switch-revision",
                "SwitchRevision",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                stale);
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("SwitchRevision", function.id)));
            error = Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                new NeoMemberNSFunction(client, function, null)
                    .Invoke("receiver-value", Array.Empty<object?>()))!;
            StringAssert.Contains("requires compiler revision 5", error.Message);
        }

        [Test]
        public void InvokeAsync_TryBodyFilterAndCatchResumeWithoutReplay()
        {
            FunctionMember tryPause = NativeFunction(
                "fn-try-pause",
                "TryPause",
                deferred: true);
            FunctionMember filterPause = NativeFunction(
                "fn-filter-pause",
                "FilterPause",
                deferred: true);
            FunctionMember catchPause = NativeFunction(
                "fn-catch-pause",
                "CatchPause",
                deferred: true);
            NSFunctionMember function = ScriptFunction(
                "fn-try-resume",
                "TryResume",
                deferred: true,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                TryAction(
                    IntType(),
                    VariableDeclaration("count", Number(0), IntType()),
                    TryBlock(
                        new Instruction[]
                        {
                            AssignLocal(
                                "count",
                                Add(Variable("count"), Number(1)),
                                IntType()),
                            new FunctionCallInstruction
                            {
                                type = InstructionKind.FunctionCall,
                                call = Call(tryPause.id, "try-pause"),
                            },
                            Throw(Text("boom")),
                        },
                        Catch(
                            "message",
                            Compare(
                                OperatorKind.EqualTo,
                                Call(filterPause.id, "filter-pause"),
                                Number(1)),
                            AssignLocal(
                                "count",
                                Add(Variable("count"), Number(1)),
                                IntType()),
                            new FunctionCallInstruction
                            {
                                type = InstructionKind.FunctionCall,
                                call = Call(catchPause.id, "catch-pause"),
                            },
                            Return(Variable("count"))),
                        Catch("fallback", null, Return(Number(99))))));
            NeoClient client = BuildClient(
                new JsonMember[] { tryPause, filterPause, catchPause, function },
                ReceiverClass(
                    ("TryPause", tryPause.id),
                    ("FilterPause", filterPause.id),
                    ("CatchPause", catchPause.id),
                    ("TryResume", function.id)));
            NeoDeferredFunction<int>? pendingTry = null;
            NeoDeferredFunction<int>? pendingFilter = null;
            NeoDeferredFunction<int>? pendingCatch = null;
            int tryCalls = 0;
            int filterCalls = 0;
            int catchCalls = 0;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [tryPause.id] = (_, _, _, deferred) =>
                    {
                        tryCalls++;
                        pendingTry = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                tryPause.name);
                    },
                    [filterPause.id] = (_, _, _, deferred) =>
                    {
                        filterCalls++;
                        pendingFilter = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                filterPause.name);
                    },
                    [catchPause.id] = (_, _, _, deferred) =>
                    {
                        catchCalls++;
                        pendingCatch = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                catchPause.name);
                    },
                });

            Task<object?> task = new NeoMemberNSFunction(client, function, null)
                .InvokeAsync("receiver-value", Array.Empty<object?>());
            Assert.AreEqual(1, tryCalls);
            pendingTry!.Complete(0);
            Assert.AreEqual(1, filterCalls);
            pendingFilter!.Complete(1);
            Assert.AreEqual(1, catchCalls);
            pendingCatch!.Complete(0);

            Assert.AreEqual(2L, Convert.ToInt64(task.GetAwaiter().GetResult()));
            Assert.AreEqual(1, tryCalls);
            Assert.AreEqual(1, filterCalls);
            Assert.AreEqual(1, catchCalls);
        }

        [Test]
        public void InvokeAsync_TryFailureBoundaryAndFilterFailurePreserveSemantics()
        {
            FunctionMember fail = NativeFunction(
                "fn-try-fail",
                "TryFail",
                deferred: true);
            NSFunctionMember caught = ScriptFunction(
                "fn-catch-deferred",
                "CatchDeferred",
                deferred: true,
                StringType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                TryAction(
                    StringType(),
                    TryBlock(
                        new Instruction[]
                        {
                            new FunctionCallInstruction
                            {
                                type = InstructionKind.FunctionCall,
                                call = Call(fail.id, "try-fail"),
                            },
                            Return(Text("wrong")),
                        },
                        Catch(
                            "message",
                            null,
                            Return(Variable("message"))))));
            NeoClient client = BuildClient(
                new JsonMember[] { fail, caught },
                ReceiverClass(("TryFail", fail.id), ("CatchDeferred", caught.id)));
            NeoDeferredFunction<int>? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [fail.id] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                fail.name),
                });
            Task<object?> caughtTask =
                new NeoMemberNSFunction(client, caught, null).InvokeAsync(
                    "receiver-value",
                    Array.Empty<object?>());
            pending!.Fail(new NeoDeferredFunctionRuntimeError("deferred boom"));
            Assert.AreEqual(
                "deferred boom",
                caughtTask.GetAwaiter().GetResult());

            client = BuildClient(
                new JsonMember[] { fail, caught },
                ReceiverClass(("TryFail", fail.id), ("CatchDeferred", caught.id)));
            pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [fail.id] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                fail.name),
                });
            Task<object?> hostTask =
                new NeoMemberNSFunction(client, caught, null).InvokeAsync(
                    "receiver-value",
                    Array.Empty<object?>());
            var hostError = new InvalidOperationException("host failure");
            pending!.Fail(hostError);
            Assert.AreSame(
                hostError,
                Assert.Throws<InvalidOperationException>(() =>
                    hostTask.GetAwaiter().GetResult()));

            FunctionMember filterFail = NativeFunction(
                "fn-filter-fail",
                "FilterFail",
                deferred: true);
            NSFunctionMember filterFunction = ScriptFunction(
                "fn-filter-failure",
                "FilterFailure",
                deferred: true,
                StringType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                TryAction(
                    StringType(),
                    TryBlock(
                        new Instruction[] { Throw(Text("original")) },
                        Catch(
                            "filtered",
                            Compare(
                                OperatorKind.EqualTo,
                                Call(filterFail.id, "filter-fail"),
                                Number(1)),
                            Return(Text("wrong"))),
                        Catch(
                            "fallback",
                            null,
                            Return(Variable("fallback"))))));
            client = BuildClient(
                new JsonMember[] { filterFail, filterFunction },
                ReceiverClass(
                    ("FilterFail", filterFail.id),
                    ("FilterFailure", filterFunction.id)));
            pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [filterFail.id] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                filterFail.name),
                });
            Task<object?> filterTask =
                new NeoMemberNSFunction(client, filterFunction, null).InvokeAsync(
                    "receiver-value",
                    Array.Empty<object?>());
            pending!.Fail(new NSGetterRuntimeError("filter failure"));
            Assert.AreEqual("original", filterTask.GetAwaiter().GetResult());
        }

        [Test]
        public void InvokeAsync_SelectedCatchFailureSkipsSiblingsAndReachesOuterTry()
        {
            FunctionMember fail = NativeFunction(
                "fn-selected-catch-fail",
                "SelectedCatchFail",
                deferred: true);
            NSFunctionMember function = ScriptFunction(
                "fn-selected-catch",
                "SelectedCatch",
                deferred: true,
                StringType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                TryAction(
                    StringType(),
                    TryBlock(
                        new Instruction[]
                        {
                            TryBlock(
                                new Instruction[] { Throw(Text("original")) },
                                Catch(
                                    "inner",
                                    Compare(
                                        OperatorKind.EqualTo,
                                        Variable("inner"),
                                        Text("original")),
                                    new FunctionCallInstruction
                                    {
                                        type = InstructionKind.FunctionCall,
                                        call = Call(fail.id, "selected-fail"),
                                    },
                                    Return(Text("wrong-selected"))),
                                Catch(
                                    "innerFallback",
                                    null,
                                    Return(Text("wrong-sibling")))),
                        },
                        Catch(
                            "outer",
                            null,
                            Return(Variable("outer"))))));
            NeoClient client = BuildClient(
                new JsonMember[] { fail, function },
                ReceiverClass(
                    ("SelectedCatchFail", fail.id),
                    ("SelectedCatch", function.id)));
            NeoDeferredFunction<int>? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [fail.id] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                fail.name),
                });

            Task<object?> task = new NeoMemberNSFunction(client, function, null)
                .InvokeAsync("receiver-value", Array.Empty<object?>());
            pending!.Fail(new NSGetterRuntimeError("selected"));

            Assert.AreEqual("selected", task.GetAwaiter().GetResult());

            // Also cover a synchronous selected-catch error thrown while the
            // inner try is itself recovering from a deferred body failure.
            function.action = TryAction(
                StringType(),
                TryBlock(
                    new Instruction[]
                    {
                        TryBlock(
                            new Instruction[]
                            {
                                new FunctionCallInstruction
                                {
                                    type = InstructionKind.FunctionCall,
                                    call = Call(fail.id, "protected-fail"),
                                },
                                Return(Text("wrong-protected")),
                            },
                            Catch(
                                "inner",
                                Compare(
                                    OperatorKind.EqualTo,
                                    Variable("inner"),
                                    Text("original")),
                                Throw(Text("selected"))),
                            Catch(
                                "innerFallback",
                                null,
                                Return(Text("wrong-sibling")))),
                    },
                    Catch(
                        "outer",
                        null,
                        Return(Variable("outer")))));
            client = BuildClient(
                new JsonMember[] { fail, function },
                ReceiverClass(
                    ("SelectedCatchFail", fail.id),
                    ("SelectedCatch", function.id)));
            pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [fail.id] = (_, _, _, deferred) =>
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                fail.name),
                });
            task = new NeoMemberNSFunction(client, function, null)
                .InvokeAsync("receiver-value", Array.Empty<object?>());
            pending!.Fail(new NSGetterRuntimeError("original"));

            Assert.AreEqual("selected", task.GetAwaiter().GetResult());
        }

        [Test]
        public void InvokeAsync_SwitchSelectorAndBodyResumeWithoutReplay()
        {
            FunctionMember selector = NativeFunction(
                "fn-switch-selector",
                "SwitchSelector",
                deferred: true);
            FunctionMember pause = NativeFunction(
                "fn-switch-pause",
                "SwitchPause",
                deferred: true);
            FunctionWithReturnType body = SwitchAction(
                VariableDeclaration("count", Number(0), IntType()),
                Switch(
                    Call(selector.id, "switch-selector"),
                    IntType(),
                    new[]
                    {
                        new SwitchSection
                        {
                            labels = new[] { SwitchLabel(IntType(), 2) },
                            instructions = new Instruction[]
                            {
                                AssignLocal(
                                    "count",
                                    Add(Variable("count"), Number(1)),
                                    IntType()),
                                new FunctionCallInstruction
                                {
                                    type = InstructionKind.FunctionCall,
                                    call = Call(pause.id, "switch-body-pause"),
                                },
                                Return(Variable("count")),
                            },
                        },
                    },
                    new Instruction[] { Return(Number(99)) }));
            NSFunctionMember function = ScriptFunction(
                "fn-switch-resume",
                "SwitchResume",
                deferred: true,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildClient(
                new JsonMember[] { selector, pause, function },
                ReceiverClass(
                    ("SwitchSelector", selector.id),
                    ("SwitchPause", pause.id),
                    ("SwitchResume", function.id)));
            NeoDeferredFunction<int>? pendingSelector = null;
            NeoDeferredFunction<int>? pendingBody = null;
            int selectorCalls = 0;
            int bodyCalls = 0;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [selector.id] = (_, _, _, deferred) =>
                    {
                        selectorCalls++;
                        pendingSelector = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                selector.name);
                    },
                    [pause.id] = (_, _, _, deferred) =>
                    {
                        bodyCalls++;
                        pendingBody = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                pause.name);
                    },
                });
            var node = new NeoMemberNSFunction(client, function, null);

            Task<object?> task = node.InvokeAsync(
                "receiver-value",
                Array.Empty<object?>());
            Assert.AreEqual(1, selectorCalls);
            Assert.AreEqual(0, bodyCalls);
            pendingSelector!.Complete(2);
            Assert.AreEqual(1, selectorCalls);
            Assert.AreEqual(1, bodyCalls);
            Assert.IsFalse(task.IsCompleted);
            pendingBody!.Complete(0);

            Assert.AreEqual(1L, Convert.ToInt64(task.GetAwaiter().GetResult()));
            Assert.AreEqual(1, selectorCalls);
            Assert.AreEqual(1, bodyCalls);
        }

        [Test]
        public void Invoke_RejectsLeakedBreakAsStaleIr()
        {
            NSFunctionMember function = ScriptFunction(
                "fn-leaked-break",
                "LeakedBreak",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                LoopAction(new BreakInstruction
                {
                    type = InstructionKind.Break,
                }));
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("LeakedBreak", function.id)));

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                new NeoMemberNSFunction(client, function, null)
                    .Invoke("receiver-value", Array.Empty<object?>()))!;

            StringAssert.Contains("unconsumed break transfer", error.Message);
            StringAssert.Contains("stale or corrupt", error.Message);
        }

        [Test]
        public void InvokeAsync_ForInitializerAndIteratorResumeWithoutReplay()
        {
            FunctionMember initialize = NativeFunction(
                "fn-loop-initialize",
                "Initialize",
                deferred: true);
            FunctionMember iterate = NativeFunction(
                "fn-loop-iterate",
                "Iterate",
                deferred: true);
            FunctionWithReturnType body = LoopAction(
                VariableDeclaration("count", Number(0), IntType()),
                new ForInstruction
                {
                    type = InstructionKind.For,
                    initializer = LocalVariable(
                        "i",
                        Call(initialize.id, "loop-initialize"),
                        IntType()),
                    condition = Compare(
                        OperatorKind.LessThan,
                        Variable("i"),
                        Number(1)),
                    iterator = AssignLocal(
                        "i",
                        Call(iterate.id, "loop-iterate"),
                        IntType()),
                    instructions = new Instruction[]
                    {
                        AssignLocal(
                            "count",
                            Add(Variable("count"), Number(1)),
                            IntType()),
                    },
                },
                Return(Variable("count")));
            NSFunctionMember function = ScriptFunction(
                "fn-loop-resume-phases",
                "LoopResumePhases",
                deferred: true,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildClient(
                new JsonMember[] { initialize, iterate, function },
                ReceiverClass(
                    ("Initialize", initialize.id),
                    ("Iterate", iterate.id),
                    ("LoopResumePhases", function.id)));
            NeoDeferredFunction<int>? pendingInitialize = null;
            NeoDeferredFunction<int>? pendingIterate = null;
            int initializeCalls = 0;
            int iterateCalls = 0;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [initialize.id] = (_, _, _, deferred) =>
                    {
                        initializeCalls++;
                        pendingInitialize = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                initialize.name);
                    },
                    [iterate.id] = (_, _, _, deferred) =>
                    {
                        iterateCalls++;
                        pendingIterate = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                iterate.name);
                    },
                });
            var node = new NeoMemberNSFunction(client, function, null);

            Task<object?> task = node.InvokeAsync(
                "receiver-value",
                Array.Empty<object?>());
            Assert.IsFalse(task.IsCompleted);
            Assert.AreEqual(1, initializeCalls);
            pendingInitialize!.Complete(0);
            Assert.AreEqual(1, iterateCalls);
            Assert.IsFalse(task.IsCompleted);
            pendingIterate!.Complete(1);

            Assert.AreEqual(1L, Convert.ToInt64(task.GetAwaiter().GetResult()));
            Assert.AreEqual(1, initializeCalls);
            Assert.AreEqual(1, iterateCalls);
        }

        [Test]
        public void InvokeAsync_ForBodyResumeDoesNotReplayCompletedWrites()
        {
            FunctionMember pause = NativeFunction(
                "fn-loop-pause-body",
                "PauseBody",
                deferred: true);
            FunctionWithReturnType body = LoopAction(
                VariableDeclaration("count", Number(0), IntType()),
                new ForInstruction
                {
                    type = InstructionKind.For,
                    initializer = LocalVariable("i", Number(0), IntType()),
                    condition = Compare(
                        OperatorKind.LessThan,
                        Variable("i"),
                        Number(2)),
                    iterator = AssignLocal(
                        "i",
                        Add(Variable("i"), Number(1)),
                        IntType()),
                    instructions = new Instruction[]
                    {
                        AssignLocal(
                            "count",
                            Add(Variable("count"), Number(1)),
                            IntType()),
                        new FunctionCallInstruction
                        {
                            type = InstructionKind.FunctionCall,
                            call = Call(pause.id, "loop-pause-body"),
                        },
                    },
                },
                Return(Variable("count")));
            NSFunctionMember function = ScriptFunction(
                "fn-loop-resume-body",
                "LoopResumeBody",
                deferred: true,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildClient(
                new JsonMember[] { pause, function },
                ReceiverClass(
                    ("PauseBody", pause.id),
                    ("LoopResumeBody", function.id)));
            NeoDeferredFunction<int>? pending = null;
            int invocationCount = 0;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [pause.id] = (_, _, _, deferred) =>
                    {
                        invocationCount++;
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                pause.name);
                    },
                });
            var node = new NeoMemberNSFunction(client, function, null);

            Task<object?> task = node.InvokeAsync(
                "receiver-value",
                Array.Empty<object?>());
            Assert.AreEqual(1, invocationCount);
            pending!.Complete(0);
            Assert.AreEqual(2, invocationCount);
            Assert.IsFalse(task.IsCompleted);
            pending!.Complete(0);

            Assert.AreEqual(2L, Convert.ToInt64(task.GetAwaiter().GetResult()));
            Assert.AreEqual(2, invocationCount);
        }

        [Test]
        public void InvokeAsync_ForConditionResumeDoesNotReplayTheCheck()
        {
            FunctionMember check = NativeFunction(
                "fn-loop-check-condition",
                "CheckCondition",
                deferred: true,
                BoolType());
            FunctionWithReturnType body = LoopAction(
                VariableDeclaration("count", Number(0), IntType()),
                new ForInstruction
                {
                    type = InstructionKind.For,
                    initializer = LocalVariable("i", Number(0), IntType()),
                    condition = Compare(
                        OperatorKind.EqualTo,
                        Call(check.id, "loop-check-condition"),
                        Boolean(true)),
                    iterator = AssignLocal(
                        "i",
                        Add(Variable("i"), Number(1)),
                        IntType()),
                    instructions = new Instruction[]
                    {
                        AssignLocal(
                            "count",
                            Add(Variable("count"), Number(1)),
                            IntType()),
                    },
                },
                Return(Variable("count")));
            NSFunctionMember function = ScriptFunction(
                "fn-loop-resume-condition",
                "LoopResumeCondition",
                deferred: true,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildClient(
                new JsonMember[] { check, function },
                ReceiverClass(
                    ("CheckCondition", check.id),
                    ("LoopResumeCondition", function.id)));
            NeoDeferredFunction<bool>? pending = null;
            int invocationCount = 0;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [check.id] = (_, _, _, deferred) =>
                    {
                        invocationCount++;
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<bool>>(
                                deferred,
                                check.name);
                    },
                });
            var node = new NeoMemberNSFunction(client, function, null);

            Task<object?> task = node.InvokeAsync(
                "receiver-value",
                Array.Empty<object?>());
            Assert.AreEqual(1, invocationCount);
            Assert.IsFalse(task.IsCompleted);
            pending!.Complete(true);
            Assert.AreEqual(2, invocationCount);
            Assert.IsFalse(task.IsCompleted);
            pending!.Complete(false);

            Assert.AreEqual(1L, Convert.ToInt64(task.GetAwaiter().GetResult()));
            Assert.AreEqual(2, invocationCount);
        }

        [Test]
        public void InvokeAsync_ForEachReceiverResumeEvaluatesReceiverOnce()
        {
            CollectionTypeInfo listType = ListType(IntType());
            FunctionMember load = NativeFunction(
                "fn-foreach-load",
                "LoadItems",
                deferred: true,
                listType);
            FunctionWithReturnType body = LoopAction(
                VariableDeclaration("sum", Number(0), IntType()),
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
                    collectionPointer = Call(load.id, "foreach-load"),
                    collectionTypeInfo = listType,
                    instructions = new Instruction[]
                    {
                        AssignLocal(
                            "sum",
                            Add(Variable("sum"), Variable("item")),
                            IntType()),
                    },
                },
                Return(Variable("sum")));
            NSFunctionMember function = ScriptFunction(
                "fn-foreach-resume-receiver",
                "ForEachResumeReceiver",
                deferred: true,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildClient(
                new JsonMember[] { load, function },
                ReceiverClass(
                    ("LoadItems", load.id),
                    ("ForEachResumeReceiver", function.id)));
            NeoDeferredFunction<object?[]>? pending = null;
            int invocationCount = 0;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [load.id] = (_, _, _, deferred) =>
                    {
                        invocationCount++;
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<object?[]>>(
                                deferred,
                                load.name);
                    },
                });
            var node = new NeoMemberNSFunction(client, function, null);

            Task<object?> task = node.InvokeAsync(
                "receiver-value",
                Array.Empty<object?>());
            Assert.AreEqual(1, invocationCount);
            Assert.IsFalse(task.IsCompleted);
            pending!.Complete(new object?[] { 1, 2 });

            Assert.AreEqual(3L, Convert.ToInt64(task.GetAwaiter().GetResult()));
            Assert.AreEqual(1, invocationCount);
        }

        [Test]
        public void InvokeAsync_ForEachBodyResumeDoesNotReplayCompletedWrites()
        {
            FunctionMember pause = NativeFunction(
                "fn-foreach-pause-body",
                "PauseForEachBody",
                deferred: true);
            CollectionTypeInfo listType = ListType(IntType());
            FunctionWithReturnType body = LoopAction(
                VariableDeclaration("count", Number(0), IntType()),
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
                    collectionPointer = List(Number(1), Number(2)),
                    collectionTypeInfo = listType,
                    instructions = new Instruction[]
                    {
                        AssignLocal(
                            "count",
                            Add(Variable("count"), Number(1)),
                            IntType()),
                        new FunctionCallInstruction
                        {
                            type = InstructionKind.FunctionCall,
                            call = Call(pause.id, "foreach-pause-body"),
                        },
                    },
                },
                Return(Variable("count")));
            NSFunctionMember function = ScriptFunction(
                "fn-foreach-resume-body",
                "ForEachResumeBody",
                deferred: true,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildClient(
                new JsonMember[] { pause, function },
                ReceiverClass(
                    ("PauseForEachBody", pause.id),
                    ("ForEachResumeBody", function.id)));
            NeoDeferredFunction<int>? pending = null;
            int invocationCount = 0;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    [pause.id] = (_, _, _, deferred) =>
                    {
                        invocationCount++;
                        pending = NeoGeneratedTypesSupport
                            .ResolveDeferredFunction<NeoDeferredFunction<int>>(
                                deferred,
                                pause.name);
                    },
                });
            var node = new NeoMemberNSFunction(client, function, null);

            Task<object?> task = node.InvokeAsync(
                "receiver-value",
                Array.Empty<object?>());
            Assert.AreEqual(1, invocationCount);
            pending!.Complete(0);
            Assert.AreEqual(2, invocationCount);
            Assert.IsFalse(task.IsCompleted);
            pending!.Complete(0);

            Assert.AreEqual(2L, Convert.ToInt64(task.GetAwaiter().GetResult()));
            Assert.AreEqual(2, invocationCount);
        }

        private const string ProjectId = "project-function";
        private const string LookupEntryMemberId = "member-lookup-overlay-entry";
        private const string LookupSourceMemberId = "member-lookup-overlay-source";
        private const string LookupSourceListValueId = "value-lookup-overlay-source";
        private const string LookupSelectorMemberId = "member-lookup-overlay-selector";
        private const string LookupSelectorValueId = "value-lookup-overlay-selector";
        private const string LookupTargetValueId = "value-lookup-overlay-target";

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

        private static NeoClient BuildLookupOverlayClient(
            params JsonMember[] callables)
        {
            var entryMember = new IntMember
            {
                id = LookupEntryMemberId,
                projectId = ProjectId,
                name = "LookupEntry",
                kind = MemberKind.Int,
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            var sourceMember = new ListMember
            {
                id = LookupSourceMemberId,
                projectId = ProjectId,
                name = "LookupSource",
                kind = MemberKind.List,
                entryMemberId = entryMember.id,
                valueId = LookupSourceListValueId,
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            var selectorMember = new LookupMember
            {
                id = LookupSelectorMemberId,
                projectId = ProjectId,
                name = "LookupSelector",
                kind = MemberKind.Lookup,
                collectionMemberId = sourceMember.id,
                collectionValueId = sourceMember.valueId,
                multiselect = true,
                valueId = LookupSelectorValueId,
                storage = "save",
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            var members = new List<JsonMember>
            {
                entryMember,
                sourceMember,
                selectorMember,
            };
            members.AddRange(callables);
            NeoClient client = BuildClient(
                members.ToArray(),
                ReceiverClass(Array.ConvertAll(
                    callables,
                    member => (member.name, member.id))),
                additionalValues: new MemberValue[]
                {
                    new NumberMemberValue
                    {
                        id = LookupTargetValueId,
                        value = 1,
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    new ArrayMemberValue
                    {
                        id = LookupSourceListValueId,
                        value = new[] { LookupTargetValueId },
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    new ArrayMemberValue
                    {
                        id = LookupSelectorValueId,
                        value = new[]
                        {
                            LookupTargetValueId,
                            LookupTargetValueId,
                        },
                        createdAt = "x",
                        updatedAt = "x",
                    },
                });
            client.SetWritableValue(
                NeoValueOwnership.Save,
                new ArrayMemberValue
                {
                    id = LookupSelectorValueId,
                    value = new[]
                    {
                        LookupTargetValueId,
                        LookupTargetValueId,
                    },
                    createdAt = "x",
                    updatedAt = "x",
                });
            client.SetWritableValue(
                NeoValueOwnership.Session,
                new NumberMemberValue
                {
                    id = LookupTargetValueId,
                    value = 9,
                    createdAt = "x",
                    updatedAt = "x",
                });
            return client;
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

        private static DelegateMember DelegateMemberTarget(
            string id,
            string name,
            string targetMemberId) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.NSDelegate,
            required = true,
            returnTypeInfo = IntType(),
            argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            defaultValue = new DelegateMemberValueBase
            {
                value = new NeoDelegateValue
                {
                    memberId = targetMemberId,
                    valueId = null,
                },
            },
            createdAt = "x",
            updatedAt = "x",
        };

        private static FunctionMember NativeFunction(
            string id,
            string name,
            bool deferred,
            TypeInfo? returnType = null) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Function,
            returnTypeInfo = returnType ?? IntType(),
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

        private static FunctionWithReturnType LoopAction(
            params Instruction[] instructions)
        {
            return LoopAction(IntType(), instructions);
        }

        private static FunctionWithReturnType LoopAction(
            TypeInfo returnType,
            params Instruction[] instructions)
        {
            FunctionWithReturnType body = Action(
                returnType,
                Array.Empty<FunctionArgumentTypeInfo>(),
                instructions);
            body.compilerRevision = 4;
            return body;
        }

        private static FunctionWithReturnType TryAction(
            TypeInfo returnType,
            params Instruction[] instructions)
        {
            FunctionWithReturnType body = Action(
                returnType,
                Array.Empty<FunctionArgumentTypeInfo>(),
                instructions);
            body.compilerRevision = 6;
            return body;
        }

        private static TryInstruction TryBlock(
            Instruction[] instructions,
            params CatchClause[] catches) => new()
        {
            type = InstructionKind.Try,
            instructions = instructions,
            catches = catches,
        };

        private static CatchClause Catch(
            string bindingId,
            BooleanExpression? filter,
            params Instruction[] instructions) => new()
        {
            binding = new CatchBinding
            {
                id = bindingId,
                typeInfo = StringType(),
                isReadonly = true,
            },
            filter = filter,
            instructions = instructions,
        };

        private static ThrowInstruction Throw(Pointer pointer) => new()
        {
            type = InstructionKind.Throw,
            pointer = pointer,
        };

        private static FunctionWithReturnType SwitchAction(
            params Instruction[] instructions)
        {
            FunctionWithReturnType body = Action(
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                instructions);
            body.compilerRevision = 5;
            return body;
        }

        private static SwitchInstruction Switch(
            Pointer selector,
            TypeInfo selectorTypeInfo,
            SwitchSection[] sections,
            Instruction[]? defaultInstructions = null) => new()
        {
            type = InstructionKind.Switch,
            selector = selector,
            selectorTypeInfo = selectorTypeInfo,
            sections = sections,
            defaultInstructions = defaultInstructions,
        };

        private static Value SwitchLabel(TypeInfo typeInfo, object value) => new()
        {
            typeInfo = typeInfo,
            value = value as JToken ?? JToken.FromObject(value),
        };

        private static long InvokeSwitchCase(
            Pointer selector,
            TypeInfo selectorTypeInfo,
            Value label)
        {
            SwitchInstruction instruction = Switch(
                selector,
                selectorTypeInfo,
                new[]
                {
                    new SwitchSection
                    {
                        labels = new[] { label },
                        instructions = new Instruction[]
                        {
                            Return(Number(1)),
                        },
                    },
                },
                new Instruction[] { Return(Number(0)) });
            NSFunctionMember function = ScriptFunction(
                "fn-switch-value",
                "SwitchValue",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                SwitchAction(instruction));
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("SwitchValue", function.id)));
            object? result = new NeoMemberNSFunction(client, function, null)
                .Invoke("receiver-value", Array.Empty<object?>());
            return Convert.ToInt64(result);
        }

        private static void AssertCalledValidationBypassesCatch(
            NSFunctionMember callee,
            string expectedMessage)
        {
            NSFunctionMember caller = ScriptFunction(
                "fn-validation-caller-" + callee.id,
                "ValidationCaller" + callee.name,
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                TryAction(
                    IntType(),
                    TryBlock(
                        new Instruction[]
                        {
                            new FunctionCallInstruction
                            {
                                type = InstructionKind.FunctionCall,
                                call = Call(
                                    callee.id,
                                    "validate-" + callee.id),
                            },
                            Return(Number(0)),
                        },
                        Catch("message", null, Return(Number(1))))));
            NeoClient client = BuildClient(
                new JsonMember[] { callee, caller },
                ReceiverClass(
                    (callee.name, callee.id),
                    (caller.name, caller.id)));

            NeoScriptPreExecutionValidationError error =
                Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                    new NeoMemberNSFunction(client, caller, null)
                        .Invoke("receiver-value", Array.Empty<object?>()))!;
            StringAssert.Contains(expectedMessage, error.Message);
        }

        private static object? InvokeTryBody(
            FunctionWithReturnType body,
            TypeInfo returnType)
        {
            NSFunctionMember function = ScriptFunction(
                "fn-try-test",
                "TryTest",
                false,
                returnType,
                Array.Empty<FunctionArgumentTypeInfo>(),
                body);
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("TryTest", function.id)));
            return new NeoMemberNSFunction(client, function, null)
                .Invoke("receiver-value", Array.Empty<object?>());
        }

        private static NSGetterRuntimeError InvokeSwitchError(
            SwitchInstruction instruction,
            bool validation = false)
        {
            NSFunctionMember function = ScriptFunction(
                "fn-switch-error",
                "SwitchError",
                false,
                IntType(),
                Array.Empty<FunctionArgumentTypeInfo>(),
                SwitchAction(instruction, Return(Number(0))));
            NeoClient client = BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("SwitchError", function.id)));
            return validation
                ? Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                    new NeoMemberNSFunction(client, function, null)
                        .Invoke("receiver-value", Array.Empty<object?>()))!
                : Assert.Throws<NSGetterRuntimeError>(() =>
                    new NeoMemberNSFunction(client, function, null)
                        .Invoke("receiver-value", Array.Empty<object?>()))!;
        }

        private static Variable LocalVariable(
            string id,
            Pointer pointer,
            TypeInfo typeInfo) => new()
        {
            id = id,
            pointer = pointer,
            typeInfo = typeInfo,
        };

        private static VariableInstruction VariableDeclaration(
            string id,
            Pointer pointer,
            TypeInfo typeInfo) => new()
        {
            type = InstructionKind.Variable,
            variable = LocalVariable(id, pointer, typeInfo),
        };

        private static AssignInstruction AssignLocal(
            string id,
            Pointer pointer,
            TypeInfo typeInfo) => new()
        {
            type = InstructionKind.Assign,
            target = new WriteTarget
            {
                pointer = Variable(id),
                typeInfo = typeInfo,
                writability = WritabilityKind.Local,
            },
            operatorValue = "=",
            pointer = pointer,
        };

        private static IfInstruction If(
            BooleanExpression expression,
            params Instruction[] instructions) => new()
        {
            type = InstructionKind.If,
            branches = new[]
            {
                new ConditionalBranch
                {
                    expression = expression,
                    instructions = instructions,
                },
            },
        };

        private static BooleanExpression Compare(
            string operatorKind,
            Pointer left,
            Pointer right) => new()
        {
            condition = new Condition
            {
                type = operatorKind,
                operand1 = left,
                operand2 = right,
            },
        };

        private static CollectionTypeInfo ListType(TypeInfo entryTypeInfo) => new()
        {
            type = MemberKind.List,
            required = true,
            entryTypeInfo = entryTypeInfo,
        };

        private static CollectionTypeInfo DictionaryType(TypeInfo entryTypeInfo) => new()
        {
            type = MemberKind.Dictionary,
            required = true,
            entryTypeInfo = entryTypeInfo,
        };

        private static LookupTypeInfo LookupType(TypeInfo entryTypeInfo) => new()
        {
            type = MemberKind.Lookup,
            required = true,
            entryTypeInfo = entryTypeInfo,
            collectionMemberId = LookupSourceMemberId,
            collectionValueId = LookupSourceListValueId,
        };

        private static ListLiteralPointer List(params Pointer[] entries) => new()
        {
            type = PointerKind.ListLiteral,
            entries = entries,
        };

        private static DictLiteralPointer Dictionary(
            CollectionTypeInfo typeInfo,
            params (string key, string value)[] entries)
        {
            var pairs = new DictLiteralPair[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                pairs[i] = new DictLiteralPair
                {
                    key = Text(entries[i].key),
                    value = Text(entries[i].value),
                };
            }
            return new DictLiteralPointer
            {
                type = PointerKind.DictLiteral,
                typeInfo = typeInfo,
                entries = pairs,
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

        private static PrimitiveTypeInfo IntType(bool required = true) => new()
        {
            type = MemberKind.Int,
            required = required,
        };

        private static PrimitiveTypeInfo NullType() => new()
        {
            type = MemberKind.Null,
            required = true,
        };

        private static EnumTypeInfo EnumType(
            string enumId,
            bool required = true) => new()
        {
            type = MemberKind.Enum,
            required = required,
            enumId = enumId,
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

        private static PrimitiveTypeInfo StringType() => new()
        {
            type = MemberKind.String,
            required = true,
        };

        private static PrimitiveTypeInfo DecimalType() => new()
        {
            type = MemberKind.Decimal,
            required = true,
        };

        private static void AssertInstructionRejected(JObject instruction)
        {
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<Instruction>(instruction.ToString()));
        }

        private static JObject ValidForInstructionJson() => JObject.Parse(@"{
            'type':'for',
            'initializer':{
                'id':'i',
                'typeInfo':{'type':2,'required':true},
                'pointer':{'type':'value','value':{'typeInfo':{'type':2,'required':true},'value':0}}
            },
            'condition':{'condition':{
                'type':'lessThan',
                'operand1':{'type':'variable','variableId':'i'},
                'operand2':{'type':'value','value':{'typeInfo':{'type':2,'required':true},'value':1}}
            }},
            'iterator':{
                'type':'assign',
                'target':{
                    'pointer':{'type':'variable','variableId':'i'},
                    'typeInfo':{'type':2,'required':true},
                    'writability':'local'
                },
                'operator':'++',
                'pointer':{'type':'value','value':{'typeInfo':{'type':2,'required':true},'value':1}}
            },
            'instructions':[]
        }");

        private static JObject ValidForEachInstructionJson() => JObject.Parse(@"{
            'type':'forEach',
            'binding':{
                'id':'item',
                'typeInfo':{'type':2,'required':true},
                'readonly':true,
                'writability':'readOnly'
            },
            'collectionPointer':{
                'type':'listLiteral',
                'typeInfo':{'type':6,'required':true,'entryTypeInfo':{'type':2,'required':true}},
                'entries':[]
            },
            'collectionTypeInfo':{
                'type':6,
                'required':true,
                'entryTypeInfo':{'type':2,'required':true}
            },
            'instructions':[]
        }");

        private static JObject ValidTryInstructionJson() => JObject.Parse(@"{
            ""type"":""try"",
            ""instructions"":[{
                ""type"":""throw"",
                ""pointer"":{""type"":""value"",""value"":{""typeInfo"":{""type"":3,""required"":true},""value"":""boom""}}
            }],
            ""catches"":[
                {
                    ""binding"":{
                        ""id"":""message"",
                        ""typeInfo"":{""type"":3,""required"":true},
                        ""readonly"":true
                    },
                    ""filter"":{
                        ""condition"":{
                            ""type"":""equalTo"",
                            ""operand1"":{""type"":""variable"",""variableId"":""message""},
                            ""operand2"":{""type"":""value"",""value"":{""typeInfo"":{""type"":3,""required"":true},""value"":""boom""}}
                        }
                    },
                    ""instructions"":[]
                },
                {
                    ""binding"":{
                        ""id"":""fallbackMessage"",
                        ""typeInfo"":{""type"":3,""required"":true},
                        ""readonly"":true
                    },
                    ""filter"":null,
                    ""instructions"":[]
                }
            ]
        }");

        private static JObject ValidSwitchInstructionJson() => JObject.Parse(@"{
            'type':'switch',
            'selector':{
                'type':'value',
                'value':{
                    'typeInfo':{'type':2,'required':false},
                    'value':1
                }
            },
            'selectorTypeInfo':{'type':2,'required':false},
            'sections':[
                {
                    'labels':[
                        {
                            'typeInfo':{'type':2,'required':true},
                            'value':1
                        }
                    ],
                    'instructions':[{'type':'break'}]
                }
            ],
            'defaultInstructions':null
        }");

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

        private static ReferencePointer Reference(string valueId) => new()
        {
            type = PointerKind.Reference,
            valueId = valueId,
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

        private static ValuePointer Literal(TypeInfo typeInfo, JToken value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = typeInfo,
                value = value,
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

        private static OperationPointer Multiply(Pointer left, Pointer right) => new()
        {
            type = PointerKind.Operation,
            operation = new ArithmeticOperation
            {
                type = OperationKind.Arithmetic,
                arithmetic = new ArithmeticOpInfo
                {
                    type = ArithmeticOpKind.Multiplication,
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
