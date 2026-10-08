// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// The <c>listRepeat</c> intrinsic (P71 §5.2) at the evaluator seam, where
    /// the shared <see cref="NeoScriptListRepeatParityFixture"/> cannot reach:
    /// operand order, reference identity across entries, and the
    /// argument-domain failure a compiled body can never carry.
    ///
    /// <para>Every message here is quoted in full, never matched by substring:
    /// P71 §5.3 makes these strings a cross-runtime contract, and a partial
    /// match would let one host's wording drift past the gate.</para>
    /// </summary>
    public class P71ListRepeatTests
    {
        // ------------------------------------------------------------------
        // IR deserialization — the wire shape of the two named operands.
        // ------------------------------------------------------------------

        [Test]
        public void Json_ListRepeatFunction_Deserializes()
        {
            var listRepeat = JsonConvert.DeserializeObject<Function>(
                @"{
                    ""type"": ""listRepeat"",
                    ""info"": {
                        ""valuePointer"": { ""type"": ""variable"", ""variableId"": ""v"" },
                        ""countPointer"": { ""type"": ""variable"", ""variableId"": ""n"" },
                        ""entryTypeInfo"": { ""type"": 4, ""required"": true }
                    }
                }");
            Assert.IsInstanceOf<ListRepeatFunction>(listRepeat);
            var info = ((ListRepeatFunction)listRepeat!).info;
            Assert.IsInstanceOf<VariablePointer>(info.valuePointer);
            Assert.IsInstanceOf<VariablePointer>(info.countPointer);
            Assert.AreEqual(MemberKind.Float, info.entryTypeInfo.type);
        }

        /// <summary>
        /// The converter arm has to survive a write as well as a read: a
        /// re-serialized body is what tooling and the loader's own diagnostics
        /// hand back, and a kind that reads but does not round-trip degrades
        /// into an unresolvable discriminator the second time through.
        /// </summary>
        [Test]
        public void Json_ListRepeatFunction_RoundTrips()
        {
            const string wire = @"{
                ""type"": ""listRepeat"",
                ""info"": {
                    ""valuePointer"": { ""type"": ""variable"", ""variableId"": ""v"" },
                    ""countPointer"": { ""type"": ""variable"", ""variableId"": ""n"" },
                    ""entryTypeInfo"": { ""type"": 4, ""required"": true }
                }
            }";
            var first = (ListRepeatFunction)JsonConvert.DeserializeObject<Function>(wire)!;

            string reserialized = JsonConvert.SerializeObject(first);
            Assert.AreEqual(
                FunctionKind.ListRepeat,
                JObject.Parse(reserialized)["type"]!.Value<string>(),
                "The discriminator must survive the write.");

            var second = JsonConvert.DeserializeObject<Function>(reserialized)
                as ListRepeatFunction;
            Assert.IsNotNull(second, "The re-serialized node must resolve to the same arm.");
            Assert.AreEqual(
                "v",
                ((VariablePointer)second!.info.valuePointer).variableId);
            Assert.AreEqual(
                "n",
                ((VariablePointer)second.info.countPointer).variableId);
            Assert.AreEqual(MemberKind.Float, second.info.entryTypeInfo.type);
        }

        // ------------------------------------------------------------------
        // Evaluation semantics (P71 §3).
        // ------------------------------------------------------------------

        [Test]
        public void EvaluatesTheValueBeforeTheCount()
        {
            // Order is contract, not incidental (P71 §3), so it needs an
            // observable difference rather than a reading of the source: the
            // value is itself a repeat with a negative count and the count is
            // a string. Value-first fails on the inner count; count-first
            // would fail on the outer count's type instead.
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                Evaluate(
                    ListRepeat(IntLiteral(7), IntLiteral(-1)),
                    StringLiteral("3")));
            Assert.AreEqual(
                "List.Repeat count must be non-negative; got -1.",
                error!.Message);
        }

        [Test]
        public void RepeatsTheSameReferenceNotACopyPerEntry()
        {
            var entries = (object?[])Evaluate(
                ListLiteral(1, 2),
                IntLiteral(3))!;

            Assert.AreEqual(3, entries.Length);
            Assert.IsTrue(
                ReferenceEquals(entries[0], entries[1])
                    && ReferenceEquals(entries[1], entries[2]),
                "Every entry must be the one evaluated value, not a copy.");
        }

        /// <summary>
        /// NeoScript has no execution budget: a repeat past the old
        /// 10,000-entry cap produces every entry.
        /// </summary>
        [Test]
        public void ProducesMoreEntriesThanTheRetiredBudgetAllowed()
        {
            var entries = (object?[])Evaluate(IntLiteral(7), IntLiteral(20_000))!;
            Assert.AreEqual(20_000, entries.Length);
        }

        [Test]
        public void ZeroCountProducesAnEmptyList()
        {
            var entries = (object?[])Evaluate(StringLiteral("empty"), IntLiteral(0))!;
            Assert.AreEqual(0, entries.Length);
        }

        // ------------------------------------------------------------------
        // Argument domain (P71 §5.3).
        // ------------------------------------------------------------------

        [Test]
        public void RejectsANegativeCountWithThePinnedMessage()
        {
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                Evaluate(IntLiteral(7), IntLiteral(-2)));
            Assert.AreEqual(
                "List.Repeat count must be non-negative; got -2.",
                error!.Message);
        }

        [Test]
        public void RejectsANonIntegralCount()
        {
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                Evaluate(IntLiteral(7), FloatLiteral(2.5)));
            Assert.AreEqual("List.Repeat count must be an integer", error!.Message);
        }

        [Test]
        public void RejectsANonNumericCount()
        {
            // Unreachable from a compiled body — the resolver types `count` as
            // Int — so the wording is pinned here rather than in the fixture.
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                Evaluate(IntLiteral(7), StringLiteral("3")));
            Assert.AreEqual("List.Repeat count must be an integer", error!.Message);
        }

        // ------------------------------------------------------------------
        // Harness.
        // ------------------------------------------------------------------

        /// <summary>
        /// Evaluates `return List.Repeat(&lt;value&gt;, &lt;count&gt;);` as a
        /// `List&lt;Int&gt;`-typed getter.
        /// </summary>
        private static object? Evaluate(Pointer valuePointer, Pointer countPointer)
        {
            return NSGetterEvaluator.Evaluate(
                new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = Array.Empty<Variable>(),
                    typeInfo = ListType(),
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction
                        {
                            type = InstructionKind.Return,
                            pointer = ListRepeat(valuePointer, countPointer),
                        },
                    },
                },
                new NSGetterEvaluator.Context(BuildClient(), null, null));
        }

        private static Pointer ListRepeat(Pointer valuePointer, Pointer countPointer)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new ListRepeatFunction
                {
                    type = FunctionKind.ListRepeat,
                    info = new FunctionListRepeatInfo
                    {
                        valuePointer = valuePointer,
                        countPointer = countPointer,
                        entryTypeInfo = new PrimitiveTypeInfo
                        {
                            type = MemberKind.Int,
                            required = true,
                        },
                    },
                },
            };
        }

        private static CollectionTypeInfo ListType()
        {
            return new CollectionTypeInfo
            {
                type = MemberKind.List,
                required = true,
                entryTypeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.Int,
                    required = true,
                },
            };
        }

        /// <summary>
        /// A list literal produces a fresh reference per evaluation, which is
        /// what makes the shared-reference assertion possible from raw IR.
        /// </summary>
        private static Pointer ListLiteral(params int[] values)
        {
            var entries = new Pointer[values.Length];
            for (int index = 0; index < values.Length; index++)
            {
                entries[index] = IntLiteral(values[index]);
            }
            return new ListLiteralPointer
            {
                type = PointerKind.ListLiteral,
                typeInfo = ListType(),
                entries = entries,
            };
        }

        private static ValuePointer Literal(MemberKind type, JToken? value)
        {
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = new PrimitiveTypeInfo { type = type, required = true },
                    value = value,
                },
            };
        }

        private static Pointer IntLiteral(double value) =>
            Literal(MemberKind.Int, JToken.FromObject(value));

        private static Pointer FloatLiteral(double value) =>
            Literal(MemberKind.Float, JToken.FromObject(value));

        private static Pointer StringLiteral(string value) =>
            Literal(MemberKind.String, JToken.FromObject(value));

        private static NeoClient BuildClient()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "List statics",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", rootClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id),
                    ["root-save-value"] = ObjectValue("root-save-value", rootClass.id),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            }.WithUserRoot());
        }

        private static ClassMember RootMember(string id, string valueId, string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-a",
                name = id,
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = valueId,
                classId = classId,
            };
        }

        private static ObjectMemberValue ObjectValue(string id, string classId)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = new Dictionary<string, string>(),
            };
        }
    }
}
