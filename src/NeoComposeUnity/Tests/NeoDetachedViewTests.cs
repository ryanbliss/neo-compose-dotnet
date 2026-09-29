// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// A NeoScript temporary returned to C# is read through its generated view
    /// without becoming rows, and becomes rows the moment C# needs one: its
    /// id, a write, a subscription.
    /// </summary>
    public sealed class NeoDetachedViewTests
    {
        private const string ProjectId = "project-detached-view";

        [Test]
        public void ReturnedTemporary_ReadsWithoutRows()
        {
            NeoClient client = BuildClient();
            int before = client.sessionValues.Count;

            TestReport report = ReadReport(client, EvaluateReport(client), out object? result);

            Assert.AreEqual(5, report.Total);
            Assert.IsTrue(report.Ok, "An unwritten member reads its literal default.");
            Assert.AreEqual(1, report.Lines.Count);
            TestLine line = report.Lines[0];
            Assert.AreEqual(3, line.Score);
            Assert.AreEqual("a", line.Label);
            Assert.AreSame(line, report.Lines[0]);
            Assert.AreSame(report.Lines, report.Lines);
            Assert.AreSame(report, ReadReport(client, result));
            foreach (TestLine entry in report.Lines)
                Assert.AreSame(line, entry);
            Assert.AreEqual(before, client.sessionValues.Count, "Nothing needed a row yet.");
        }

        [Test]
        public void ReturnedTemporary_AttachesWhenItsIdIsNeeded()
        {
            NeoClient client = BuildClient();
            TestReport report = ReadReport(client, EvaluateReport(client), out object? result);
            NeoList<TestLine> lines = report.Lines;
            TestLine line = lines[0];
            int before = client.sessionValues.Count;

            string id = report.valueId!;

            Assert.Greater(client.sessionValues.Count, before);
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, id, out ObjectMemberValue? row));
            Assert.AreEqual("class-report", row!.classId);
            Assert.AreEqual(5, report.Total);
            Assert.AreEqual(1, lines.Count, "A list read before the owner attached reads its row after.");
            Assert.AreEqual(3, lines[0].Score);
            Assert.IsNotNull(line.valueId, "The entry attached with its owner.");
            Assert.AreEqual(3, line.Score);
            Assert.AreSame(report, ReadReport(client, result));
        }

        [Test]
        public void ReturnedTemporary_WritesLandOnItsRow()
        {
            NeoClient client = BuildClient();
            TestReport report = ReadReport(client, EvaluateReport(client), out _);
            NeoList<TestLine> lines = report.Lines;

            report.Total = 9;
            lines.RemoveAt(0);

            Assert.AreEqual(9, report.Total);
            Assert.AreEqual(0, lines.Count);
            Assert.AreEqual(0, report.Lines.Count);
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, report.valueId!, out ObjectMemberValue? row));
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, row!.value!["Total"], out NumberMemberValue? total));
            Assert.AreEqual(9, Convert.ToInt32(total!.BoxedValue));
        }

        [Test]
        public void DisposingAPendingView_MakesNoRows()
        {
            NeoClient client = BuildClient();
            int before = client.sessionValues.Count;
            TestReport report = ReadReport(client, EvaluateReport(client), out _);

            report.Dispose();

            Assert.AreEqual(before, client.sessionValues.Count);
        }

        [Test]
        public void UnsavedTemporary_IsReadOnly()
        {
            NeoClient client = BuildClient();

            TestReport report = NeoGeneratedTypesSupport.ReadRequiredNSPropertyClass(
                client,
                EvaluateReport(client),
                false,
                null,
                TestReport.CreateWritable,
                TestReport.CreateDetached);

            Assert.IsTrue(report.IsReadOnly);
            Assert.IsFalse(report.TryWritable(out TestReport _));
            Assert.AreEqual(5, report.Total);
        }

        [Test]
        public void DisposedView_IsNotReused()
        {
            NeoClient client = BuildClient();
            TestReport report = ReadReport(client, EvaluateReport(client), out object? result);
            int before = client.sessionValues.Count;

            report.Dispose();
            TestReport again = ReadReport(client, result);

            Assert.AreNotSame(report, again);
            Assert.AreEqual(5, again.Total);
            Assert.Throws<ObjectDisposedException>(() => _ = report.valueId);
            Assert.AreEqual(before, client.sessionValues.Count, "A disposed view never attaches.");
        }

        [Test]
        public void PendingView_ReadByNeoScript_DoesNotAttach()
        {
            NeoClient client = BuildClient();
            TestReport report = ReadReport(client, EvaluateReport(client), out _);
            int before = client.sessionValues.Count;

            object? total = NSGetterEvaluator.Evaluate(
                new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = Array.Empty<Variable>(),
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction
                        {
                            type = InstructionKind.Return,
                            pointer = new KeyOfPointer
                            {
                                type = PointerKind.KeyOf,
                                keyOf = new KeyOf
                                {
                                    pointer = new VariablePointer { type = PointerKind.Variable, variableId = "__this__" },
                                    key = Literal("Total", MemberKind.String),
                                },
                            },
                        },
                    },
                },
                new NSGetterEvaluator.Context(client, report, null));

            Assert.AreEqual(5, Convert.ToInt32(total));
            Assert.IsNotNull(report.PendingValue);
            Assert.AreEqual(before, client.sessionValues.Count);
        }

        [Test]
        public void ListAdd_ThroughAPendingView_AttachesTheOwner()
        {
            NeoClient client = BuildClient();
            TestReport report = ReadReport(client, EvaluateReport(client), out _);
            NeoList<TestLine> lines = report.Lines;
            TestLine added = NeoGeneratedTypesSupport.ReadRequiredNSPropertyClass(
                client,
                Evaluate(client, LineType, Line()),
                true,
                null,
                TestLine.CreateWritable,
                TestLine.CreateDetached);

            lines.Add(added);

            Assert.IsNull(report.PendingValue);
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual(2, report.Lines.Count);
            Assert.AreEqual(added.valueId, lines[1].valueId);
        }

        [Test]
        public void Subscribing_AttachesThePendingView()
        {
            NeoClient client = BuildClient();
            TestReport report = ReadReport(client, EvaluateReport(client), out _);
            int before = client.sessionValues.Count;

            using IDisposable subscription = report.WatchAnyChange((_, _, _) => { });

            Assert.IsNull(report.PendingValue);
            Assert.Greater(client.sessionValues.Count, before);
        }

        [Test]
        public void DecliningFactory_TakesTheRowPath()
        {
            // A base class's factory declines a subclass it has no arm for.
            NeoClient client = BuildClient();
            int before = client.sessionValues.Count;

            TestReport report = NeoGeneratedTypesSupport.ReadRequiredNSPropertyClass(
                client,
                EvaluateReport(client),
                true,
                null,
                TestReport.CreateWritable,
                static (_, _, _) => null);

            Assert.IsNull(report.PendingValue);
            Assert.IsNotNull(report.valueId);
            Assert.Greater(client.sessionValues.Count, before);
            Assert.AreEqual(5, report.Total);
        }

        [TestCase(2.5)]
        [TestCase(double.PositiveInfinity)]
        public void NonIntegralInt_IsNotStoredInASlot(double total)
        {
            // `new Report { Total = 2.5 }`: an Int slot takes only integral
            // numbers, so the construction takes the row path.
            NeoClient client = BuildClient();

            Assert.IsNotInstanceOf<NeoScriptObject>(Evaluate(client, ReportType, Report(Literal(total, MemberKind.Float))));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LookupField_LivesInASlot(bool fromDefault)
        {
            // `new Pick { Choice = line }`, or `new Pick()` whose Choice
            // defaults to that line: the slot holds the selected ids, and a
            // read resolves them as the row's read does.
            NeoClient client = BuildClient(choiceDefault: fromDefault);
            int before = client.sessionValues.Count;
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var scope = new Dictionary<string, object?>
            {
                ["pick"] = NSGetterEvaluator.EvaluatePointer(
                    fromDefault
                        ? Construct(PickType)
                        : Construct(PickType, Field("Choice", "member-pick-choice", Reference("line-a"))),
                    new Dictionary<string, object?>(),
                    ctx),
            };

            object? label = NSGetterEvaluator.EvaluatePointer(
                Key(Key(Variable("pick"), "Choice"), "Label"),
                scope,
                ctx);

            Assert.IsInstanceOf<NeoScriptObject>(scope["pick"]);
            Assert.AreEqual("asset line", label);
            Assert.AreEqual(before, client.sessionValues.Count, "Nothing needed a row yet.");
            // Its row, once needed, holds the same selection.
            string id = NSGetterEvaluator.AttachDetached((NeoScriptObject)scope["pick"]!, null);
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, id, out ObjectMemberValue? row));
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, row!.value!["Choice"], out ArrayMemberValue? choice));
            CollectionAssert.AreEqual(new[] { "line-a" }, choice!.value);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MultiSelection_BuildsRows(bool lookup)
        {
            // A multi-selection can be mutated through a variable holding
            // it, which only a row tracks.
            NeoClient client = lookup
                ? BuildClient(choiceSelection: NeoMemberSelectionKind.Multi)
                : BuildClient(modeSelection: NeoMemberSelectionKind.Multi);

            object? picked = Evaluate(
                client,
                PickType,
                Construct(PickType, Field("Choice", "member-pick-choice", Reference("line-a"))));

            Assert.IsNotInstanceOf<NeoScriptObject>(picked);
        }

        [Test]
        public void EnumAlias_ReadsTheSlot()
        {
            // `var mode = pick.Mode; pick.Mode = .B; mode` reads .B, as an
            // alias of the member's row does.
            NeoClient client = BuildClient();
            int before = client.sessionValues.Count;
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var scope = new Dictionary<string, object?>
            {
                ["b"] = new object?[] { "option-b" },
            };
            scope["pick"] = NSGetterEvaluator.EvaluatePointer(
                Construct(PickType, Field("Choice", "member-pick-choice", Reference("line-a"))),
                scope,
                ctx);
            scope["mode"] = NSGetterEvaluator.EvaluatePointer(Key(Variable("pick"), "Mode"), scope, ctx);

            NeoScriptExecutor.Execute(client, new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                instructions = new Instruction[]
                {
                    new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        target = new WriteTarget
                        {
                            pointer = Key(Variable("pick"), "Mode"),
                            typeInfo = new EnumTypeInfo
                            {
                                type = MemberKind.Enum,
                                required = true,
                                enumId = "enum-mode",
                            },
                            writability = WritabilityKind.Local,
                        },
                        operatorValue = "=",
                        pointer = Variable("b"),
                    },
                },
            }, scope, ctx);

            Assert.That(
                NSGetterEvaluator.EvaluatePointer(Variable("mode"), scope, ctx),
                Is.EqualTo(new object?[] { "option-b" }));
            Assert.IsNull(((NeoScriptObject)scope["pick"]!).attachedId);
            Assert.AreEqual(before, client.sessionValues.Count, "Nothing needed a row.");
        }

        [TestCase(CollectionMutationKind.Add, 2)]
        [TestCase(CollectionMutationKind.Clear, 0)]
        public void ListAlias_MutatesTheSlot(string mutation, int expected)
        {
            // `var lines = report.Lines; lines.<mutation>(...)` mutates the
            // report's list, as an alias of a row-backed list does. An Add
            // appends to the slot; anything else goes through the row.
            NeoClient client = BuildClient();
            int before = client.sessionValues.Count;
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var scope = new Dictionary<string, object?>();
            scope["report"] = NSGetterEvaluator.EvaluatePointer(Report(Literal(5, MemberKind.Int)), scope, ctx);
            scope["lines"] = NSGetterEvaluator.EvaluatePointer(Key(Variable("report"), "Lines"), scope, ctx);

            NeoScriptExecutor.Execute(client, new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                instructions = new Instruction[]
                {
                    new CollectionCallInstruction
                    {
                        type = InstructionKind.CollectionCall,
                        target = new WriteTarget
                        {
                            pointer = Variable("lines"),
                            typeInfo = new CollectionTypeInfo
                            {
                                type = MemberKind.List,
                                required = true,
                                entryTypeInfo = LineType,
                            },
                            writability = WritabilityKind.Local,
                        },
                        mutation = mutation,
                        args = mutation == CollectionMutationKind.Add
                            ? new Pointer[] { Line() }
                            : Array.Empty<Pointer>(),
                    },
                },
            }, scope, ctx);

            var report = (NeoScriptObject)scope["report"]!;
            Assert.AreEqual(expected, ((object?[])NSGetterEvaluator.EvaluatePointer(Key(Variable("report"), "Lines"), scope, ctx)!).Length);
            Assert.AreEqual(expected, ((object?[])NSGetterEvaluator.EvaluatePointer(Variable("lines"), scope, ctx)!).Length);
            if (mutation == CollectionMutationKind.Add)
            {
                Assert.IsNull(report.attachedId);
                Assert.AreEqual(before, client.sessionValues.Count, "An Add needs no row.");
            }
            else
            {
                Assert.IsNotNull(report.attachedId);
            }
        }

        private static TestReport ReadReport(NeoClient client, object? result) =>
            ReadReport(client, result, out _);

        private static TestReport ReadReport(NeoClient client, object? result, out object? value)
        {
            value = result;
            return NeoGeneratedTypesSupport.ReadRequiredNSPropertyClass(
                client,
                result,
                true,
                null,
                TestReport.CreateWritable,
                TestReport.CreateDetached);
        }

        private static readonly ClassTypeInfo LineType = ClassType("class-line");
        private static readonly ClassTypeInfo ReportType = ClassType("class-report");
        private static readonly ClassTypeInfo PickType = ClassType("class-pick");

        /// <summary><c>return new Report { Total = 5, Lines = [new Line { Score = 3, Label = "a" }] };</c></summary>
        private static object? EvaluateReport(NeoClient client)
        {
            object? result = Evaluate(client, ReportType, Report(Literal(5, MemberKind.Int)));
            Assert.IsInstanceOf<NeoScriptObject>(result);
            return result;
        }

        /// <summary><c>new Line { Score = 3, Label = "a" }</c></summary>
        private static FunctionPointer Line() => Construct(
            LineType,
            Field("Score", "member-line-score", Literal(3, MemberKind.Int)),
            Field("Label", "member-line-label", Literal("a", MemberKind.String)));

        /// <summary><c>new Report { Total = total, Lines = [Line()] }</c></summary>
        private static FunctionPointer Report(ValuePointer total) =>
            Construct(
                ReportType,
                Field("Total", "member-report-total", total),
                Field("Lines", "member-report-lines", new ListLiteralPointer
                {
                    type = PointerKind.ListLiteral,
                    typeInfo = new CollectionTypeInfo
                    {
                        type = MemberKind.List,
                        required = true,
                        entryTypeInfo = LineType,
                    },
                    entries = new Pointer[] { Line() },
                }));

        /// <summary><c>return value;</c></summary>
        private static object? Evaluate(NeoClient client, ClassTypeInfo type, Pointer value) =>
            NSGetterEvaluator.Evaluate(
                new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = Array.Empty<Variable>(),
                    typeInfo = type,
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction { type = InstructionKind.Return, pointer = value },
                    },
                },
                new NSGetterEvaluator.Context(client, null, null));

        private static ClassTypeInfo ClassType(string classId) => new()
        {
            type = MemberKind.Class,
            required = true,
            classId = classId,
        };

        private static FunctionPointer Construct(
            ClassTypeInfo type,
            params FunctionClassConstructorField[] fields) => new()
            {
                type = PointerKind.Function,
                function = new ClassConstructorFunction
                {
                    type = FunctionKind.ClassConstructor,
                    info = new FunctionClassConstructorInfo
                    {
                        schemaClassInfo = type,
                        fields = fields,
                    },
                },
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

        private static KeyOfPointer Key(Pointer receiver, string key) => new()
        {
            type = PointerKind.KeyOf,
            keyOf = new KeyOf
            {
                pointer = receiver,
                key = Literal(key, MemberKind.String),
            },
        };

        private static FunctionClassConstructorField Field(string key, string memberId, Pointer value) => new()
        {
            schemaKey = key,
            memberId = memberId,
            valuePointer = value,
        };

        private static ValuePointer Literal(object value, MemberKind kind) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = new PrimitiveTypeInfo { type = kind, required = true },
                value = JToken.FromObject(value),
            },
        };

        private static NeoClient BuildClient(
            NeoMemberSelectionKind choiceSelection = NeoMemberSelectionKind.Single,
            bool choiceDefault = false,
            NeoMemberSelectionKind modeSelection = NeoMemberSelectionKind.Single)
        {
            var roots = new[]
            {
                RootMember("root-assets", "Assets", NeoMemberStorage.Immutable, "value-assets"),
                RootMember("root-save", "Save", NeoMemberStorage.Save, "value-save"),
                RootMember("root-session", "Session", NeoMemberStorage.Session, "value-session"),
            };
            var members = new Dictionary<string, JsonMember>
            {
                ["member-report-total"] = new IntMember
                {
                    id = "member-report-total",
                    projectId = ProjectId,
                    name = "Total",
                    kind = MemberKind.Int,
                    Requirement = NeoMemberRequirementKind.Required,
                    defaultValue = new NumberMemberValueBase { value = 0 },
                },
                ["member-report-ok"] = new BoolMember
                {
                    id = "member-report-ok",
                    projectId = ProjectId,
                    name = "Ok",
                    kind = MemberKind.Bool,
                    Requirement = NeoMemberRequirementKind.Required,
                    defaultValue = new BoolMemberValueBase { value = true },
                },
                ["member-report-lines"] = new ListMember
                {
                    id = "member-report-lines",
                    projectId = ProjectId,
                    name = "Lines",
                    kind = MemberKind.List,
                    Requirement = NeoMemberRequirementKind.Required,
                    entryMemberId = "member-report-line",
                    defaultValue = new ArrayMemberValueBase { value = Array.Empty<string>() },
                },
                ["member-report-line"] = new ClassMember
                {
                    id = "member-report-line",
                    projectId = ProjectId,
                    name = "Line",
                    kind = MemberKind.Class,
                    Requirement = NeoMemberRequirementKind.Required,
                    classId = "class-line",
                },
                ["member-line-score"] = new IntMember
                {
                    id = "member-line-score",
                    projectId = ProjectId,
                    name = "Score",
                    kind = MemberKind.Int,
                    Requirement = NeoMemberRequirementKind.Required,
                    defaultValue = new NumberMemberValueBase { value = 0 },
                },
                ["member-line-label"] = new StringMember
                {
                    id = "member-line-label",
                    projectId = ProjectId,
                    name = "Label",
                    kind = MemberKind.String,
                    Requirement = NeoMemberRequirementKind.Required,
                    Format = NeoStringFormatKind.Plain,
                    defaultValue = new StringMemberValueBase { value = "" },
                },
            };
            // `class Pick { Line Choice; Mode Mode = .A; }`, Choice a lookup into an asset list of lines.
            members["member-choices"] = new ListMember
            {
                id = "member-choices",
                projectId = ProjectId,
                name = "Choices",
                kind = MemberKind.List,
                entryMemberId = "member-report-line",
                valueId = "value-choices",
            };
            members["member-pick-choice"] = new LookupMember
            {
                id = "member-pick-choice",
                projectId = ProjectId,
                name = "Choice",
                kind = MemberKind.Lookup,
                Requirement = NeoMemberRequirementKind.Required,
                collectionMemberId = "member-choices",
                collectionValueId = "value-choices",
                Selection = choiceSelection,
                defaultValue = choiceDefault
                    ? new ArrayMemberValueBase { value = new[] { "line-a" } }
                    : null,
            };
            members["member-pick-mode"] = new EnumMember
            {
                id = "member-pick-mode",
                projectId = ProjectId,
                name = "Mode",
                kind = MemberKind.Enum,
                Requirement = NeoMemberRequirementKind.Required,
                enumId = "enum-mode",
                Selection = modeSelection,
                defaultValue = new ArrayMemberValueBase { value = new[] { "option-a" } },
            };
            foreach (ClassMember root in roots)
                members[root.id] = root;
            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "Detached views",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = members,
                values = new Dictionary<string, MemberValue>
                {
                    ["value-assets"] = RootValue("value-assets"),
                    ["value-save"] = RootValue("value-save"),
                    ["value-session"] = RootValue("value-session"),
                    ["value-choices"] = new ArrayMemberValue { id = "value-choices", value = new[] { "line-a" } },
                    ["line-a"] = new ObjectMemberValue
                    {
                        id = "line-a",
                        classId = "class-line",
                        value = new Dictionary<string, string>
                        {
                            ["Score"] = "line-a-score",
                            ["Label"] = "line-a-label",
                        },
                    },
                    ["line-a-score"] = new NumberMemberValue { id = "line-a-score", value = 1 },
                    ["line-a-label"] = new StringMemberValue { id = "line-a-label", value = "asset line" },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["class-root"] = SchemaClass("class-root", "Root"),
                    ["class-report"] = SchemaClass(
                        "class-report",
                        "Report",
                        ("Total", "member-report-total"),
                        ("Ok", "member-report-ok"),
                        ("Lines", "member-report-lines")),
                    ["class-line"] = SchemaClass(
                        "class-line",
                        "Line",
                        ("Score", "member-line-score"),
                        ("Label", "member-line-label")),
                    ["class-pick"] = SchemaClass(
                        "class-pick",
                        "Pick",
                        ("Choice", "member-pick-choice"),
                        ("Mode", "member-pick-mode")),
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>
                {
                    ["enum-mode"] = new NeoCompose.Runtime.Json.Enum
                    {
                        id = "enum-mode",
                        projectId = ProjectId,
                        name = "Mode",
                        options = new Dictionary<string, EnumOption>
                        {
                            ["option-a"] = new EnumOption { text = "A" },
                            ["option-b"] = new EnumOption { text = "B" },
                        },
                        optionKeyOrder = new List<string> { "option-a", "option-b" },
                    },
                },
            });
        }

        private static ClassMember RootMember(string id, string name, NeoMemberStorage storage, string valueId) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Class,
            Requirement = NeoMemberRequirementKind.Required,
            classId = "class-root",
            Storage = storage,
            valueId = valueId,
        };

        private static ObjectMemberValue RootValue(string id) => new()
        {
            id = id,
            classId = "class-root",
            value = new Dictionary<string, string>(),
        };

        private static NeoSchemaClass SchemaClass(string id, string name, params (string key, string memberId)[] schema)
        {
            var entries = new Dictionary<string, string>();
            foreach (var (key, memberId) in schema)
                entries[key] = memberId;
            return new NeoSchemaClass
            {
                id = id,
                projectId = ProjectId,
                name = name,
                schema = entries,
            };
        }

        /// <summary>The shape generated C# takes for <c>class Report</c>.</summary>
        private sealed class TestReport : NeoGeneratedClassValue
        {
            private TestReport(NeoClient client, NeoMemberClass node)
                : base(client, node, "class-report", false, node.ownership)
            {
            }

            private TestReport(NeoClient client, NeoDetachedValue value, bool isReadOnly)
                : base(client, value, isReadOnly)
            {
            }

            internal static TestReport CreateWritable(NeoClient client, NeoMemberClassWritable node) =>
                NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                    client,
                    node,
                    static (factoryClient, factoryNode) => new TestReport(factoryClient, factoryNode));

            internal static TestReport CreateDetached(NeoClient client, NeoDetachedValue value, bool saved) =>
                new(client, value, !saved);

            public int Total
            {
                get
                {
                    if (TryReadDetached("Total", out object? detachedValue))
                        return Convert.ToInt32(detachedValue);
                    return NeoGeneratedTypesSupport.ReadInt(node.Get<NeoMemberInt>("Total"))
                        ?? throw new InvalidOperationException("Required int 'Total' has no value.");
                }
                set
                {
                    NeoGeneratedTypesSupport.SetValue(writableNode, "Total", NeoGeneratedTypesSupport.Value(value));
                }
            }

            public bool Ok
            {
                get
                {
                    if (TryReadDetached("Ok", out object? detachedValue))
                        return (bool)detachedValue!;
                    return node.Get<NeoMemberBool>("Ok").value?.value
                        ?? throw new InvalidOperationException("Required bool 'Ok' has no value.");
                }
            }

            public NeoList<TestLine> Lines
            {
                get
                {
                    if (TryReadDetached("Lines", out _))
                    {
                        return DetachedList<TestLine>(
                            "Lines",
                            entry => NeoGeneratedTypesSupport.ReadRequiredNSPropertyClass(client, entry, true, null, TestLine.CreateWritable, TestLine.CreateDetached),
                            (client, child) => TestLine.CreateWritable(client, (NeoMemberClassWritable)child),
                            item => NeoGeneratedTypesSupport.ValueReference(item));
                    }
                    var memberNode = writableNode.Get<NeoMemberListWritable>("Lines");
                    if (TryGetStoredView<NeoList<TestLine>>("Lines", memberNode, out var cached))
                        return cached;
                    return CacheStoredView("Lines", memberNode, new NeoList<TestLine>(
                        client,
                        memberNode,
                        () => writableNode.GetOrCreateCollection<NeoMemberListWritable>("Lines"),
                        (client, child) => TestLine.CreateWritable(client, (NeoMemberClassWritable)child),
                        item => NeoGeneratedTypesSupport.ValueReference(item)));
                }
            }
        }

        /// <summary>The shape generated C# takes for <c>class Line</c>.</summary>
        private sealed class TestLine : NeoGeneratedClassValue
        {
            private TestLine(NeoClient client, NeoMemberClass node)
                : base(client, node, "class-line", false, node.ownership)
            {
            }

            private TestLine(NeoClient client, NeoDetachedValue value, bool isReadOnly)
                : base(client, value, isReadOnly)
            {
            }

            internal static TestLine CreateWritable(NeoClient client, NeoMemberClassWritable node) =>
                NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                    client,
                    node,
                    static (factoryClient, factoryNode) => new TestLine(factoryClient, factoryNode));

            internal static TestLine CreateDetached(NeoClient client, NeoDetachedValue value, bool saved) =>
                new(client, value, !saved);

            public int Score
            {
                get
                {
                    if (TryReadDetached("Score", out object? detachedValue))
                        return Convert.ToInt32(detachedValue);
                    return NeoGeneratedTypesSupport.ReadInt(node.Get<NeoMemberInt>("Score"))
                        ?? throw new InvalidOperationException("Required int 'Score' has no value.");
                }
            }

            public string Label
            {
                get
                {
                    if (TryReadDetached("Label", out object? detachedValue))
                        return (string)detachedValue!;
                    return node.Get<NeoMemberString>("Label").value?.value
                        ?? throw new InvalidOperationException("Required string 'Label' has no value.");
                }
            }
        }
    }
}
