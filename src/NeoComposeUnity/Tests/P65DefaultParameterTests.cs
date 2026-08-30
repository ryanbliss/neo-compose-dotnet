// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P65 — default parameter values, exercised over hand-built schemas in
    /// the style of <see cref="NSFunctionRuntimeTests"/> and
    /// <see cref="NeoDeclaredConstructorTests"/>: the export DTO's
    /// <c>defaultValue</c> wrapper, callee-side fill at every arity for
    /// NSFunction and native Function calls (§2.5), declared-constructor
    /// subset matching and fill (§2.2), per-kind runtime materialization of
    /// stored defaults (§3.1), the effective-arity positional guard (§2.2),
    /// and the schema-18 export gate (§3.3).
    /// </summary>
    public class P65DefaultParameterTests
    {
        private const string ProjectId = "p65-project";

        // -------------------------------------------------------------------
        // §3.1 — DTO wire shape.
        // -------------------------------------------------------------------

        [Test]
        public void FunctionArgument_DefaultValueWrapperDeserializesTypedPayloads()
        {
            var withString = Deserialize(
                @"{'name':'bar','type':4,'required':true,'defaultValue':{'value':'BAR'}}");
            Assert.IsNotNull(withString.defaultValue);
            Assert.AreEqual("BAR", withString.defaultValue!.value);

            var withInt = Deserialize(
                @"{'name':'count','type':2,'required':true,'defaultValue':{'value':3}}");
            Assert.AreEqual(3L, withInt.defaultValue!.value);

            var withBool = Deserialize(
                @"{'name':'loud','type':1,'required':true,'defaultValue':{'value':true}}");
            Assert.AreEqual(true, withBool.defaultValue!.value);

            var withFloat = Deserialize(
                @"{'name':'rate','type':3,'required':true,'defaultValue':{'value':1.5}}");
            Assert.AreEqual(1.5d, withFloat.defaultValue!.value);

            // A present wrapper with a null payload is an EXPLICIT null
            // default, distinct from the absent wrapper below.
            var withNull = Deserialize(
                @"{'name':'icon','type':4,'required':false,'defaultValue':{'value':null}}");
            Assert.IsNotNull(withNull.defaultValue);
            Assert.IsNull(withNull.defaultValue!.value);

            var withoutDefault = Deserialize(
                @"{'name':'plain','type':4,'required':true}");
            Assert.IsNull(withoutDefault.defaultValue);
        }

        [Test]
        public void FunctionArgument_StructuredDefaultPayloadIsRejected()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                Deserialize(
                    @"{'name':'bad','type':4,'required':true,'defaultValue':{'value':['a']}}"));
            StringAssert.Contains("not a P65 §1.2 constant", error!.Message);
        }

        [Test]
        public void FunctionArgument_DefaultMissingValueKeyIsRejected()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                Deserialize(
                    @"{'name':'bad','type':4,'required':true,'defaultValue':{}}"));
            StringAssert.Contains("missing 'value'", error!.Message);
        }

        // -------------------------------------------------------------------
        // §2.5 — NSFunction fill at min, mid, and max arity.
        // -------------------------------------------------------------------

        [Test]
        public void NSFunction_MinArityFillsEveryTrailingDefault()
        {
            NeoClient client = BuildFunctionClient(out NSFunctionMember function);
            var node = new NeoMemberNSFunction(client, function, null);

            object? result = node.Invoke("receiver-value", new object?[] { "a" });

            Assert.AreEqual("a|2|tail", result);
        }

        [Test]
        public void NSFunction_MidArityFillsOnlyTheOmittedTail()
        {
            NeoClient client = BuildFunctionClient(out NSFunctionMember function);
            var node = new NeoMemberNSFunction(client, function, null);

            object? result = node.Invoke("receiver-value", new object?[] { "a", 9 });

            Assert.AreEqual("a|9|tail", result);
        }

        [Test]
        public void NSFunction_MaxAritySuppliedValuesWin()
        {
            NeoClient client = BuildFunctionClient(out NSFunctionMember function);
            var node = new NeoMemberNSFunction(client, function, null);

            object? result = node.Invoke(
                "receiver-value",
                new object?[] { "a", 9, "z" });

            Assert.AreEqual("a|9|z", result);
        }

        [Test]
        public void NSFunction_BelowMinimumAndAboveMaximumStillThrow()
        {
            NeoClient client = BuildFunctionClient(out NSFunctionMember function);
            var node = new NeoMemberNSFunction(client, function, null);

            NSGetterRuntimeError below = Assert.Throws<NSGetterRuntimeError>(() =>
                node.Invoke("receiver-value", Array.Empty<object?>()))!;
            StringAssert.Contains(
                "expects between 1 and 3 arguments but received 0",
                below.Message);

            NSGetterRuntimeError above = Assert.Throws<NSGetterRuntimeError>(() =>
                node.Invoke(
                    "receiver-value",
                    new object?[] { "a", 9, "z", "extra" }))!;
            StringAssert.Contains(
                "expects between 1 and 3 arguments but received 4",
                above.Message);
        }

        // -------------------------------------------------------------------
        // §3.1 — per-kind runtime materialization of stored defaults.
        // -------------------------------------------------------------------

        [Test]
        public void ParameterDefault_MaterializesEachConstantKind()
        {
            var boolParameter = Argument("flag", MemberKind.Bool);
            boolParameter.defaultValue = new ParameterDefaultValue { value = true };
            Assert.AreEqual(
                true,
                NeoParameterDefaults.DefaultRuntimeValue(boolParameter, "test"));

            var intParameter = Argument("count", MemberKind.Int);
            intParameter.defaultValue = new ParameterDefaultValue { value = 3L };
            Assert.AreEqual(
                3L,
                NeoParameterDefaults.DefaultRuntimeValue(intParameter, "test"));

            var floatParameter = Argument("rate", MemberKind.Float);
            floatParameter.defaultValue = new ParameterDefaultValue { value = 1.5d };
            Assert.AreEqual(
                1.5d,
                NeoParameterDefaults.DefaultRuntimeValue(floatParameter, "test"));

            // A decimal's canonical string form passes through verbatim — the
            // evaluator's decimal stamp is the string, never a CLR decimal.
            var decimalParameter = Argument("price", MemberKind.Decimal);
            decimalParameter.defaultValue = new ParameterDefaultValue { value = "1.5" };
            Assert.AreEqual(
                "1.5",
                NeoParameterDefaults.DefaultRuntimeValue(decimalParameter, "test"));

            var stringParameter = Argument("label", MemberKind.String);
            stringParameter.defaultValue = new ParameterDefaultValue { value = "s" };
            Assert.AreEqual(
                "s",
                NeoParameterDefaults.DefaultRuntimeValue(stringParameter, "test"));

            // The stored single option id becomes the id-array runtime enum
            // shape, the same way an enum member literal default materializes.
            var enumParameter = new FunctionArgumentTypeInfo
            {
                name = "mode",
                type = MemberKind.Enum,
                required = true,
                enumId = "mode-enum",
                defaultValue = new ParameterDefaultValue { value = "option-fast" },
            };
            object? enumValue = NeoParameterDefaults.DefaultRuntimeValue(
                enumParameter,
                "test");
            CollectionAssert.AreEqual(
                new[] { "option-fast" },
                (string[])enumValue!);

            // The explicit null default (legal only on a nullable parameter).
            var classParameter = new FunctionArgumentTypeInfo
            {
                name = "icon",
                type = MemberKind.Class,
                required = false,
                classId = "receiver-class",
                defaultValue = new ParameterDefaultValue { value = null },
            };
            Assert.IsNull(
                NeoParameterDefaults.DefaultRuntimeValue(classParameter, "test"));
        }

        [Test]
        public void ParameterDefault_OmissionWithoutADefaultThrows()
        {
            var parameter = Argument("plain", MemberKind.String);
            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NeoParameterDefaults.DefaultRuntimeValue(parameter, "test"))!;
            StringAssert.Contains(
                "parameter 'plain' was omitted but declares no default",
                error.Message);
        }

        [Test]
        public void NSFunction_EnumAndNullDefaultsFlowThroughInvocation()
        {
            var enumArgument = new FunctionArgumentTypeInfo
            {
                name = "Mode",
                type = MemberKind.Enum,
                required = true,
                enumId = "mode-enum",
                defaultValue = new ParameterDefaultValue { value = "option-fast" },
            };
            NSFunctionMember enumFunction = ScriptFunction(
                "fn-enum-default",
                "PickMode",
                EnumType("mode-enum"),
                new[] { enumArgument },
                Return(Variable("__arg_0__")));

            var nullArgument = new FunctionArgumentTypeInfo
            {
                name = "Icon",
                type = MemberKind.Class,
                required = false,
                classId = "receiver-class",
                defaultValue = new ParameterDefaultValue { value = null },
            };
            NSFunctionMember nullFunction = ScriptFunction(
                "fn-null-default",
                "PickIcon",
                new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = false,
                    classId = "receiver-class",
                },
                new[] { nullArgument },
                Return(Variable("__arg_0__")));

            NeoClient client = BuildClient(
                new JsonMember[] { enumFunction, nullFunction },
                ReceiverClass(
                    ("PickMode", enumFunction.id),
                    ("PickIcon", nullFunction.id)));

            object? mode = new NeoMemberNSFunction(client, enumFunction, null)
                .Invoke("receiver-value", Array.Empty<object?>());
            CollectionAssert.AreEqual(
                new[] { "option-fast" },
                (string[])mode!);

            object? icon = new NeoMemberNSFunction(client, nullFunction, null)
                .Invoke("receiver-value", Array.Empty<object?>());
            Assert.IsNull(icon);
        }

        // -------------------------------------------------------------------
        // §2.5 — native Function calls fill in the evaluator, before dispatch.
        // -------------------------------------------------------------------

        [Test]
        public void NativeFunction_EvaluatorFillsBeforeExactArityDispatch()
        {
            NeoClient client = BuildNativeClient();
            client.RegisterNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    ["fn-native-greet"] = (_, _, args) =>
                        $"{args[0]}{args[1]}",
                });

            object? result = NSGetterEvaluator.Evaluate(
                NativeCallGetter(argumentCount: 1),
                new NSGetterEvaluator.Context(client, null, null));

            // PrepareNativeFunctionInvocation's exact-match check stands, so a
            // successful call proves full arity reached dispatch.
            Assert.AreEqual("hi!", result);
        }

        [Test]
        public void NativeFunction_BelowMinimumThrowsTheRangeError()
        {
            NeoClient client = BuildNativeClient();
            client.RegisterNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    ["fn-native-greet"] = (_, _, args) => $"{args[0]}{args[1]}",
                });

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    NativeCallGetter(argumentCount: 0),
                    new NSGetterEvaluator.Context(client, null, null)))!;
            StringAssert.Contains(
                "expects between 1 and 2 arguments but received 0",
                error.Message);
        }

        // -------------------------------------------------------------------
        // §2.2 — declared constructors: subset matching and fill.
        // -------------------------------------------------------------------

        [Test]
        public void DeclaredConstructor_OmittedDefaultedArgumentFillsFromTheRecord()
        {
            NeoClient client = BuildConstructorClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            object? result = NSGetterEvaluator.Evaluate(
                ReturnFunction(
                    WidgetType(),
                    ConstructorPointer(
                        WidgetType(),
                        "ctor-fill",
                        new DeclaredConstructorArgument
                        {
                            name = "A",
                            valuePointer = StringPointer("hi"),
                        })),
                ctx);

            ObjectMemberValue root = RequireConstructedRoot(client, ctx, result);
            Assert.AreEqual("hi", ReadString(client, root, "Label"));
            Assert.AreEqual("beta", ReadString(client, root, "Tag"));
        }

        [Test]
        public void DeclaredConstructor_AllDefaultsConstructsWithZeroArguments()
        {
            NeoClient client = BuildConstructorClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            // The `new()`-equivalent path: every parameter is defaulted, the
            // compiler resolves `new()` to the constructor, and the call
            // carries no arguments at all.
            object? result = NSGetterEvaluator.Evaluate(
                ReturnFunction(
                    AutoType(),
                    ConstructorPointer(AutoType(), "ctor-auto")),
                ctx);

            ObjectMemberValue root = RequireConstructedRoot(client, ctx, result);
            Assert.AreEqual("auto", ReadString(client, root, "Label"));
        }

        [Test]
        public void DeclaredConstructor_MissingNonDefaultedArgumentKeepsItsError()
        {
            NeoClient client = BuildConstructorClient();

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(
                        WidgetType(),
                        ConstructorPointer(
                            WidgetType(),
                            "ctor-fill",
                            new DeclaredConstructorArgument
                            {
                                name = "B",
                                valuePointer = StringPointer("only-b"),
                            })),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains(
                "Declared constructor 'ctor-fill' on class 'Widget' is missing argument 'A'. Regenerate the NeoScript IR from the current schema.",
                error.Message);
        }

        [Test]
        public void DeclaredConstructor_UnknownArgumentNameIsRejected()
        {
            NeoClient client = BuildConstructorClient();

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(
                        WidgetType(),
                        ConstructorPointer(
                            WidgetType(),
                            "ctor-fill",
                            new DeclaredConstructorArgument
                            {
                                name = "A",
                                valuePointer = StringPointer("hi"),
                            },
                            new DeclaredConstructorArgument
                            {
                                name = "Z",
                                valuePointer = StringPointer("stray"),
                            })),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("names unknown argument 'Z'", error.Message);
        }

        // -------------------------------------------------------------------
        // §2.2 — the positional guard re-judged against effective arities.
        // -------------------------------------------------------------------

        [Test]
        public void ConstructorRecords_CallableArityCollisionIsRejectedAtLoad()
        {
            ProjectData data = BuildConstructorProjectData();
            // `Foo(int A)` alongside `Foo(int A, int B = 2)`: the defaulted
            // signature occupies both (int) and (int, int) positionally, so
            // the shared shortened arity collides at load.
            var single = ConstructorFor(
                "ctor-int",
                "widget-class",
                new[] { IntArgument("A") });
            var defaulted = ConstructorFor(
                "ctor-int-defaulted",
                "widget-class",
                new[] { IntArgument("A"), DefaultedIntArgument("B", 2L) });
            data.constructors[single.id] = single;
            data.constructors[defaulted.id] = defaulted;
            data.classes["widget-class"].constructorIds =
                new[] { single.id, defaulted.id };

            var error = Assert.Throws<InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(data))!;
            StringAssert.Contains("have the same positional signature", error.Message);
            StringAssert.Contains("ctor-int", error.Message);
            StringAssert.Contains("ctor-int-defaulted", error.Message);
        }

        [Test]
        public void ConstructorRecords_DistinctCallableAritiesStillLoad()
        {
            ProjectData data = BuildConstructorProjectData();
            // (string) + (string, string) prefixes never collide with the
            // sibling class's records, so the defaulted overload set loads.
            NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.IsTrue(client.TryGetConstructor(
                "ctor-fill",
                out ConstructorRecord? record));
            Assert.IsNotNull(record);
        }

        // -------------------------------------------------------------------
        // §3.3 — the export gate accepts exactly the current schema.
        // -------------------------------------------------------------------

        [Test]
        public void ExportSchemaVersion_PreviousTwentyEightIsRejectedClosed()
        {
            ProjectData data = BuildConstructorProjectData();
            data.metadata = new ProjectExportMetadata
            {
                schemaVersion = 28,
                projectId = ProjectId,
                versionId = "version-1",
            };
            var error = Assert.Throws<InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(data))!;
            StringAssert.Contains("schema version 28", error.Message);
            StringAssert.Contains(
                $"accepts only schema version {NeoProjectExportContract.CurrentSchemaVersion}",
                error.Message);
            StringAssert.Contains("Re-export", error.Message);
        }

        [Test]
        public void ExportSchemaVersion_CurrentContractIsTwentyNine()
        {
            Assert.AreEqual(29, NeoProjectExportContract.CurrentSchemaVersion);
        }

        // -------------------------------------------------------------------
        // Function-side schema.
        // -------------------------------------------------------------------

        /// <summary>
        /// <c>string Pick(string A, int B = 2, string C = "tail")</c> whose
        /// body renders <c>{A}|{B}|{C}</c>, so one call observes every slot.
        /// </summary>
        private static NeoClient BuildFunctionClient(out NSFunctionMember function)
        {
            FunctionArgumentTypeInfo a = Argument("A", MemberKind.String);
            FunctionArgumentTypeInfo b = Argument("B", MemberKind.Int);
            b.defaultValue = new ParameterDefaultValue { value = 2L };
            FunctionArgumentTypeInfo c = Argument("C", MemberKind.String);
            c.defaultValue = new ParameterDefaultValue { value = "tail" };
            function = ScriptFunction(
                "fn-pick",
                "Pick",
                StringType(),
                new[] { a, b, c },
                Return(Concat(
                    Concat(
                        Concat(Variable("__arg_0__"), Text("|")),
                        Concat(Stringify(Variable("__arg_1__")), Text("|"))),
                    Variable("__arg_2__"))));
            return BuildClient(
                new JsonMember[] { function },
                ReceiverClass(("Pick", function.id)));
        }

        /// <summary>
        /// A native <c>string Greet(string name, string suffix = "!")</c>
        /// member: the fill happens in the evaluator, and the registered
        /// invoker plus the exact-arity dispatch check observe the result.
        /// </summary>
        private static NeoClient BuildNativeClient()
        {
            FunctionArgumentTypeInfo name = Argument("name", MemberKind.String);
            FunctionArgumentTypeInfo suffix = Argument("suffix", MemberKind.String);
            suffix.defaultValue = new ParameterDefaultValue { value = "!" };
            var native = new FunctionMember
            {
                id = "fn-native-greet",
                projectId = ProjectId,
                name = "Greet",
                kind = MemberKind.Function,
                returnTypeInfo = StringType(),
                argumentTypes = new[] { name, suffix },
                Dispatch = NeoFunctionDispatchKind.Synchronous,
                createdAt = "x",
                updatedAt = "x",
            };
            return BuildClient(
                new JsonMember[] { native },
                ReceiverClass(("Greet", native.id)));
        }

        private static FunctionWithReturnType NativeCallGetter(int argumentCount)
        {
            var args = new List<Pointer>();
            if (argumentCount >= 1) args.Add(Text("hi"));
            if (argumentCount >= 2) args.Add(Text("?"));
            return new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                typeInfo = StringType(),
                instructions = new Instruction[]
                {
                    Return(new CallFunctionPointer
                    {
                        type = PointerKind.CallFunction,
                        memberId = "fn-native-greet",
                        receiver = CallReceiver.Instance(Text("receiver")),
                        args = args.ToArray(),
                        callSiteId = "native-fill",
                    }),
                },
            };
        }

        private static NeoClient BuildClient(
            JsonMember[] callables,
            NeoSchemaClass receiverClass)
        {
            ClassMember assets = RootMember("root-assets", "Assets", "root-assets-value");
            ClassMember save = RootMember("root-save", "Save", "root-save-value", NeoMemberStorage.Save);
            ClassMember session = RootMember("root-session", "Session", "root-session-value", NeoMemberStorage.Session);
            var members = new Dictionary<string, JsonMember>
            {
                [assets.id] = assets,
                [save.id] = save,
                [session.id] = session,
            };
            foreach (JsonMember callable in callables) members[callable.id] = callable;

            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "P65 Tests",
                    rootAssetsMemberId = assets.id,
                    rootSaveFileMemberId = save.id,
                    rootSessionMemberId = session.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                members = members,
                values = new Dictionary<string, MemberValue>
                {
                    [assets.valueId!] = ObjectValue(assets.valueId!, "root-class"),
                    [save.valueId!] = ObjectValue(save.valueId!, "root-class"),
                    [session.valueId!] = ObjectValue(session.valueId!, "root-class"),
                    ["receiver-value"] = ObjectValue("receiver-value", receiverClass.id),
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
                    [receiverClass.id] = receiverClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            });
        }

        // -------------------------------------------------------------------
        // Constructor-side schema.
        // -------------------------------------------------------------------

        /// <summary>
        /// <c>class Widget { Widget(string A, string B = "beta") }</c> whose
        /// body assigns <c>Label = A; Tag = B</c>, and
        /// <c>class Auto { Auto(string A = "auto") }</c> assigning
        /// <c>Label = A</c>.
        /// </summary>
        private static NeoClient BuildConstructorClient()
        {
            return NeoTestSaveStack.ClientFromSchema(BuildConstructorProjectData());
        }

        private static ProjectData BuildConstructorProjectData()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = ProjectId,
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var widgetClass = new NeoSchemaClass
            {
                id = "widget-class",
                projectId = ProjectId,
                name = "Widget",
                schema = new Dictionary<string, string>
                {
                    ["Label"] = "widget-label",
                    ["Tag"] = "widget-tag",
                },
                constructorIds = new[] { "ctor-fill" },
            };
            var autoClass = new NeoSchemaClass
            {
                id = "auto-class",
                projectId = ProjectId,
                name = "Auto",
                schema = new Dictionary<string, string>
                {
                    ["Label"] = "auto-label",
                },
                constructorIds = new[] { "ctor-auto" },
            };

            ClassMember rootAssets = RootMember("root-assets", "Assets", "value-assets");
            ClassMember rootSave = RootMember("root-save", "Save", "value-save", NeoMemberStorage.Save);
            ClassMember rootSession = RootMember("root-session", "Session", "value-session", NeoMemberStorage.Session);

            FunctionArgumentTypeInfo fillA = Argument("A", MemberKind.String);
            FunctionArgumentTypeInfo fillB = Argument("B", MemberKind.String);
            fillB.defaultValue = new ParameterDefaultValue { value = "beta" };
            ConstructorRecord fillConstructor = ConstructorFor(
                "ctor-fill",
                widgetClass.id,
                new[] { fillA, fillB },
                FieldAssignment("Label", Variable("__arg_0__")),
                FieldAssignment("Tag", Variable("__arg_1__")));

            FunctionArgumentTypeInfo autoA = Argument("A", MemberKind.String);
            autoA.defaultValue = new ParameterDefaultValue { value = "auto" };
            ConstructorRecord autoConstructor = ConstructorFor(
                "ctor-auto",
                autoClass.id,
                new[] { autoA },
                FieldAssignment("Label", Variable("__arg_0__")));

            return new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "P65 Constructor Tests",
                    rootAssetsMemberId = rootAssets.id,
                    rootSaveFileMemberId = rootSave.id,
                    rootSessionMemberId = rootSession.id,
                },
                members = new Dictionary<string, JsonMember>
                {
                    [rootAssets.id] = rootAssets,
                    [rootSave.id] = rootSave,
                    [rootSession.id] = rootSession,
                    ["widget-label"] = StringField("widget-label", "Label"),
                    ["widget-tag"] = StringField("widget-tag", "Tag"),
                    ["auto-label"] = StringField("auto-label", "Label"),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["value-assets"] = ObjectValue("value-assets", rootClass.id),
                    ["value-save"] = ObjectValue("value-save", rootClass.id),
                    ["value-session"] = ObjectValue("value-session", rootClass.id),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [widgetClass.id] = widgetClass,
                    [autoClass.id] = autoClass,
                },
                constructors = new Dictionary<string, ConstructorRecord>
                {
                    [fillConstructor.id] = fillConstructor,
                    [autoConstructor.id] = autoConstructor,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static StringMember StringField(string id, string name)
        {
            return new StringMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.String,
                Requirement = NeoMemberRequirementKind.Required,
                Format = NeoStringFormatKind.Plain,
                defaultValue = new StringMemberValueBase { value = "base" },
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static ConstructorRecord ConstructorFor(
            string id,
            string classId,
            FunctionArgumentTypeInfo[] argumentTypes,
            params Instruction[] instructions)
        {
            var parameters = new List<Variable>(argumentTypes.Length + 2)
            {
                Parameter("__this__", ClassType(classId)),
                Parameter("__root__", ClassType("root-class")),
            };
            for (int i = 0; i < argumentTypes.Length; i++)
            {
                parameters.Add(Parameter($"__arg_{i}__", argumentTypes[i]));
            }
            return new ConstructorRecord
            {
                id = id,
                projectId = ProjectId,
                classId = classId,
                argumentTypes = argumentTypes,
                code = "// hand-built",
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = parameters.ToArray(),
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.Null,
                        required = true,
                    },
                    instructions = instructions,
                },
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static FunctionPointer ConstructorPointer(
            ClassTypeInfo schemaClassInfo,
            string constructorId,
            params DeclaredConstructorArgument[] args)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new DeclaredConstructorFunction
                {
                    type = FunctionKind.DeclaredConstructor,
                    info = new DeclaredConstructorInfo
                    {
                        schemaClassInfo = schemaClassInfo,
                        constructorId = constructorId,
                        args = args,
                        fields = Array.Empty<FunctionClassConstructorField>(),
                    },
                },
            };
        }

        private static AssignInstruction FieldAssignment(
            string schemaKey,
            Pointer value)
        {
            return new AssignInstruction
            {
                type = InstructionKind.Assign,
                target = new WriteTarget
                {
                    pointer = new KeyOfPointer
                    {
                        type = PointerKind.KeyOf,
                        keyOf = new KeyOf
                        {
                            pointer = Variable("__this__"),
                            key = StringPointer(schemaKey),
                        },
                    },
                    typeInfo = StringType(),
                    writability = WritabilityKind.Session,
                },
                operatorValue = "=",
                pointer = value,
            };
        }

        private static ObjectMemberValue RequireConstructedRoot(
            NeoClient client,
            NSGetterEvaluator.Context ctx,
            object? result)
        {
            Assert.IsNotNull(result);
            string? valueId = NSGetterEvaluator.FindRowIdByReference(result, ctx);
            Assert.IsNotNull(valueId, "Constructed value has no backing row.");
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                valueId!,
                out ObjectMemberValue? row));
            return row!;
        }

        private static string? ReadString(
            NeoClient client,
            ObjectMemberValue root,
            string schemaKey)
        {
            Assert.IsTrue(
                root.value!.TryGetValue(schemaKey, out string childId),
                $"Constructed row has no '{schemaKey}'. Keys: {string.Join(",", root.value.Keys)}");
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                childId,
                out StringMemberValue? child));
            return child!.value;
        }

        // -------------------------------------------------------------------
        // Shared builders.
        // -------------------------------------------------------------------

        private static FunctionArgumentTypeInfo Deserialize(string json)
        {
            return JsonConvert.DeserializeObject<FunctionArgumentTypeInfo>(json)!;
        }

        private static NSFunctionMember ScriptFunction(
            string id,
            string name,
            TypeInfo returnType,
            FunctionArgumentTypeInfo[] arguments,
            params Instruction[] instructions)
        {
            var parameters = new Variable[arguments.Length + 2];
            parameters[0] = Parameter("__this__", ClassType("receiver-class"));
            parameters[1] = Parameter("__root__", ClassType("root-class"));
            for (int i = 0; i < arguments.Length; i++)
            {
                parameters[i + 2] = Parameter($"__arg_{i}__", arguments[i]);
            }
            return new NSFunctionMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.NSFunction,
                code = "compiled test function",
                returnTypeInfo = returnType,
                argumentTypes = arguments,
                Dispatch = NeoFunctionDispatchKind.Synchronous,
                action = new FunctionWithReturnType
                {
                    parameters = parameters,
                    instructions = instructions,
                    typeInfo = returnType,
                },
                createdAt = "x",
                updatedAt = "x",
            };
        }

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

        private static ClassMember RootMember(
            string id,
            string name,
            string valueId,
            NeoMemberStorage storage = NeoMemberStorage.Inherit) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Class,
            classId = "root-class",
            valueId = valueId,
            Storage = storage,
            createdAt = "x",
            updatedAt = "x",
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

        private static FunctionArgumentTypeInfo Argument(
            string name,
            MemberKind type,
            bool required = true) => new()
        {
            name = name,
            type = type,
            required = required,
        };

        private static FunctionArgumentTypeInfo IntArgument(string name) =>
            Argument(name, MemberKind.Int);

        private static FunctionArgumentTypeInfo DefaultedIntArgument(
            string name,
            long value)
        {
            FunctionArgumentTypeInfo argument = IntArgument(name);
            argument.defaultValue = new ParameterDefaultValue { value = value };
            return argument;
        }

        private static ClassTypeInfo ClassType(string classId) => new()
        {
            type = MemberKind.Class,
            required = true,
            classId = classId,
        };

        private static ClassTypeInfo WidgetType() => ClassType("widget-class");

        private static ClassTypeInfo AutoType() => ClassType("auto-class");

        private static PrimitiveTypeInfo StringType() => new()
        {
            type = MemberKind.String,
            required = true,
        };

        private static EnumTypeInfo EnumType(string enumId) => new()
        {
            type = MemberKind.Enum,
            required = true,
            enumId = enumId,
        };

        private static FunctionWithReturnType ReturnFunction(
            TypeInfo returnType,
            Pointer pointer)
        {
            return new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = returnType,
                instructions = new Instruction[]
                {
                    Return(pointer),
                },
            };
        }

        private static Variable Parameter(string id, TypeInfo typeInfo) => new()
        {
            id = id,
            typeInfo = typeInfo,
            pointer = new VariablePointer
            {
                type = PointerKind.Variable,
                variableId = id,
            },
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

        private static ValuePointer Text(string value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = StringType(),
                value = Newtonsoft.Json.Linq.JToken.FromObject(value),
            },
        };

        private static ValuePointer StringPointer(string value) => Text(value);

        private static StringifyPointer Stringify(Pointer pointer) => new()
        {
            type = PointerKind.Stringify,
            pointer = pointer,
            sourceType = new PrimitiveTypeInfo
            {
                type = MemberKind.Int,
                required = true,
            },
        };

        private static OperationPointer Concat(Pointer left, Pointer right) => new()
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
    }
}
