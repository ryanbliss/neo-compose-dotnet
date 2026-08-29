// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// once-evaluation of the operands, reference identity across entries,
    /// budget accounting, and the argument-domain failure a compiled body can
    /// never carry.
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
            // value is a five-entry list literal under a two-entry budget and
            // the count is a string. Value-first fails on the budget;
            // count-first would fail on the argument domain instead.
            var error = Assert.Throws<NeoScriptResourceLimitError>(() =>
                Evaluate(
                    ListLiteral(1, 2, 3, 4, 5),
                    StringLiteral("3"),
                    producedCollectionEntries: 2));
            Assert.AreEqual(
                "NeoScript produced collection entry limit of 2 exceeded.",
                error!.Message);
        }

        [Test]
        public void EvaluatesTheValueExactlyOnceWhateverTheCount()
        {
            // The value is a five-entry list literal, so evaluating it charges
            // five produced collection entries; the repeat itself charges four.
            // A budget of exactly nine therefore passes only when the value is
            // evaluated once — a second evaluation would want five more.
            object? result = Evaluate(
                ListLiteral(1, 2, 3, 4, 5),
                IntLiteral(4),
                producedCollectionEntries: 9);

            var entries = (object?[])result!;
            Assert.AreEqual(4, entries.Length);
            foreach (object? entry in entries)
            {
                AssertNumbers(new double[] { 1, 2, 3, 4, 5 }, entry);
            }
        }

        [Test]
        public void EvaluatesTheCountExactlyOnce()
        {
            // Same trick on the other operand: a count expressed as the length
            // of a three-entry list literal charges three entries of its own,
            // and the repeat charges three more. Six is enough for one
            // evaluation of the count and not for two.
            object? result = Evaluate(
                IntLiteral(7),
                CountOfListLiteral(1, 2, 3),
                producedCollectionEntries: 6);

            AssertNumbers(new double[] { 7, 7, 7 }, result);
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

        [Test]
        public void ZeroCountProducesAnEmptyList()
        {
            var entries = (object?[])Evaluate(StringLiteral("empty"), IntLiteral(0))!;
            Assert.AreEqual(0, entries.Length);
        }

        // ------------------------------------------------------------------
        // Budget accounting (P71 §3, P54).
        // ------------------------------------------------------------------

        [Test]
        public void ChargesTheCountToTheSharedCollectionEntryBudget()
        {
            AssertNumbers(
                new double[] { 7, 7, 7 },
                Evaluate(IntLiteral(7), IntLiteral(3), producedCollectionEntries: 3));

            var error = Assert.Throws<NeoScriptResourceLimitError>(() =>
                Evaluate(IntLiteral(7), IntLiteral(3), producedCollectionEntries: 2));
            Assert.AreEqual(
                "NeoScript produced collection entry limit of 2 exceeded.",
                error!.Message);
        }

        [Test]
        public void FailsAnOverBudgetCountBeforeAllocatingAnything()
        {
            var stopwatch = Stopwatch.StartNew();
            var error = Assert.Throws<NeoScriptResourceLimitError>(() =>
                Evaluate(
                    IntLiteral(7),
                    IntLiteral(1_000_000_000),
                    producedCollectionEntries: 10));
            stopwatch.Stop();

            Assert.AreEqual(
                "NeoScript produced collection entry limit of 10 exceeded.",
                error!.Message);
            // A billion-entry allocation could not return in this window; the
            // budget check therefore ran ahead of the fill, not after it.
            Assert.Less(stopwatch.ElapsedMilliseconds, 1_000);
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
        public void RejectsANonIntegralCountBeforeItCanChargeBudget()
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
        /// Entries compare as doubles: NeoScript's Int is an integral Float,
        /// and a literal pointer can surface it as a <c>long</c> or a
        /// <c>double</c> depending on how the JSON operand was written, which
        /// is not what these tests are about.
        /// </summary>
        private static void AssertNumbers(double[] expected, object? produced)
        {
            var entries = produced as object?[];
            Assert.IsNotNull(entries, $"Expected a list, got {produced ?? "null"}.");
            Assert.AreEqual(expected.Length, entries!.Length, "Entry count");
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.AreEqual(
                    expected[index],
                    Convert.ToDouble(entries[index]),
                    $"Entry {index}");
            }
        }

        /// <summary>
        /// Evaluates `return List.Repeat(&lt;value&gt;, &lt;count&gt;);` as a
        /// `List&lt;Int&gt;`-typed getter, optionally under a tightened
        /// collection-entry budget.
        /// </summary>
        private static object? Evaluate(
            Pointer valuePointer,
            Pointer countPointer,
            int? producedCollectionEntries = null)
        {
            var limits = producedCollectionEntries is int cap
                ? new NeoScriptExecutionBudgetLimits(producedCollectionEntries: cap)
                : null;
            return NSGetterEvaluator.Evaluate(
                new FunctionWithReturnType
                {
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
                new NSGetterEvaluator.Context(
                    BuildClient(),
                    null,
                    null,
                    executionBudgetLimits: limits));
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
        /// A list literal is the one pure pointer whose evaluation is
        /// observable: it charges the shared collection-entry budget and
        /// produces a fresh reference, which is what makes both
        /// once-evaluation and shared-reference assertions possible from raw
        /// IR.
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

        private static Pointer CountOfListLiteral(params int[] values)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CountFunction
                {
                    type = FunctionKind.Count,
                    info = new FunctionCollectionOptionalBoolInfo
                    {
                        collectionPointer = ListLiteral(values),
                    },
                },
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
            });
        }

        private static ClassMember RootMember(string id, string valueId, string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-a",
                name = id,
                kind = MemberKind.Class,
                requirement = NeoMemberRequirementKind.Required,
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
