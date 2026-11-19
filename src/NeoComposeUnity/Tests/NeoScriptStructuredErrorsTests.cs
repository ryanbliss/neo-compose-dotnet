#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace NeoCompose.Tests
{
    public class NeoScriptStructuredErrorsTests
    {
        private static JObject Fixture() => JObject.Parse(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/structured-errors-parity-fixture.json"));
        public static IEnumerable<string> Cases()
        {
            foreach (JToken test in (JArray)Fixture()["cases"]!)
                yield return test["name"]!.Value<string>()!;
        }

        internal static NeoClient Client()
        {
            JObject fixture = Fixture();
            JObject corpus = JObject.Parse(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/synth-example.json"));
            foreach (string table in new[] { "classes", "members", "constructors" })
            {
                var target = (JObject?)corpus[table] ?? new JObject();
                if (corpus[table] is null)
                    corpus[table] = target;
                foreach (JToken row in (JArray)fixture[table]!)
                    target[row["id"]!.Value<string>()!] = row.DeepClone();
            }
            return NeoTestSaveStack.LoadClient(corpus.ToString());
        }

        internal static NeoScriptException ThrowFixture(NeoClient client, bool generic)
        {
            var getter = generic ? UncaughtGeneric(false) : Fixture()["cases"]![0]!["getter"]!.ToObject<FunctionWithReturnType>()!;
            if (!generic)
                getter.instructions = ((TryInstruction)getter.instructions[0]).instructions;
            return Assert.Throws<NeoScriptException>(() => NSGetterEvaluator.Evaluate(getter, new NSGetterEvaluator.Context(client, null, null)))!;
        }

        [Test]
        public void HostPayloadRetainsGeneratedSubtypeWithoutAttachingRows()
        {
            using NeoClient client = Client();
            client.RegisterGeneratedDetachedClassFactories(StructuredErrorGenerated.StructuredErrorsNeo.NeoDetachedValueFactories);
            JObject fixture = Fixture();
            var getter = fixture["cases"]![0]!["getter"]!.ToObject<FunctionWithReturnType>()!;
            getter.instructions = ((TryInstruction)getter.instructions[0]).instructions;
            int rows = client.sessionValues.Count;
            var failure = Assert.Throws<NeoScriptException>(() => NSGetterEvaluator.Evaluate(getter, new NSGetterEvaluator.Context(client, null, null)))!;
            Assert.IsInstanceOf<StructuredErrorGenerated.NeoArgumentError>(failure.Error);
            Assert.AreEqual("bad", failure.Error.Message);
            Assert.AreSame(failure.Error, failure.Error);
            Assert.AreEqual(rows, client.sessionValues.Count);
        }

        private static FunctionWithReturnType UncaughtGeneric(bool text)
        {
            foreach (JToken test in (JArray)Fixture()["cases"]!)
            {
                if (test["name"]!.Value<string>() != "closed generic catch distinguishes arguments")
                    continue;
                var getter = test["getter"]!.ToObject<FunctionWithReturnType>()!;
                getter.instructions = ((TryInstruction)getter.instructions[0]).instructions;
                if (text)
                {
                    // Change the constructor's closed type, preserving compiler-emitted IR.
                    JObject json = JObject.FromObject(getter);
                    foreach (JProperty property in json.Descendants().OfType<JProperty>())
                        if (property.Name == "typeArguments" && property.Value is JObject arguments)
                            arguments["p122-error-type"] = JObject.FromObject(new PrimitiveTypeInfo { type = MemberKind.String, required = true });
                    return json.ToObject<FunctionWithReturnType>()!;
                }
                return getter;
            }
            throw new InvalidOperationException("Missing generic fixture");
        }

        [Test]
        public void ClosedGenericHostFactoriesPreserveRowsAndDistinguishArguments()
        {
            using NeoClient client = Client();
            client.RegisterGeneratedClassFactories(StructuredErrorGenerated.StructuredErrorsNeo.NeoReadOnlyValueFactories,
                StructuredErrorGenerated.StructuredErrorsNeo.NeoWritableValueFactories);
            StructuredErrorGenerated.GenericError<int>.RegisterErrorFactory(client);
            StructuredErrorGenerated.GenericError<string>.RegisterErrorFactory(client);
            foreach (bool text in new[] { false, true })
            {
                var failure = Assert.Throws<NeoScriptException>(() => NSGetterEvaluator.Evaluate(UncaughtGeneric(text), new NSGetterEvaluator.Context(client, null, null)))!;
                int rows = client.sessionValues.Count;
                if (text)
                    Assert.IsInstanceOf<StructuredErrorGenerated.GenericError<string>>(failure.Error);
                else
                    Assert.IsInstanceOf<StructuredErrorGenerated.GenericError<int>>(failure.Error);
                Assert.AreEqual("typed", failure.Error.Message);
                Assert.AreEqual(rows, client.sessionValues.Count);
                Assert.Greater(rows, 0);
            }
        }

        private static FunctionWithReturnType TupleCatchGetter() =>
            Fixture()["cases"]!.Single(test => test["name"]!.Value<string>() ==
                "tuple generic catch distinguishes arguments and ignores labels")["getter"]!.ToObject<FunctionWithReturnType>()!;

        [Test]
        public void TupleGenericHostFactoriesKeepDistinctRegistrations()
        {
            using NeoClient client = Client();
            client.RegisterGeneratedClassFactories(StructuredErrorGenerated.StructuredErrorsNeo.NeoReadOnlyValueFactories,
                StructuredErrorGenerated.StructuredErrorsNeo.NeoWritableValueFactories);
            StructuredErrorGenerated.GenericError<(int, int)>.RegisterErrorFactory(client);
            StructuredErrorGenerated.GenericError<(int, string)>.RegisterErrorFactory(client);
            foreach (bool text in new[] { false, true })
            {
                var getter = UncaughtGeneric(false);
                JObject json = JObject.FromObject(getter);
                TypeInfo type = text ? client.TypeArguments.Of<(int, string)>() : client.TypeArguments.Of<(int, int)>();
                foreach (JProperty property in json.Descendants().OfType<JProperty>())
                    if (property.Name == "typeArguments" && property.Value is JObject arguments)
                        arguments["p122-error-type"] = JObject.FromObject(type);
                getter = json.ToObject<FunctionWithReturnType>()!;
                var failure = Assert.Throws<NeoScriptException>(() => NSGetterEvaluator.Evaluate(getter, new NSGetterEvaluator.Context(client, null, null)))!;
                if (text)
                    Assert.IsInstanceOf<StructuredErrorGenerated.GenericError<(int, string)>>(failure.Error);
                else
                    Assert.IsInstanceOf<StructuredErrorGenerated.GenericError<(int, int)>>(failure.Error);
                Assert.AreEqual("typed", failure.Error.Message);
            }
        }

        [Test]
        public void CatchClosesReceiverGenericInsideTuple()
        {
            using NeoClient client = Client();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var receiver = Assert.Throws<NeoScriptException>(() => NSGetterEvaluator.Evaluate(UncaughtGeneric(false), ctx))!;
            ctx.BindThis(receiver.Payload.Value);
            var getter = TupleCatchGetter();
            var guarded = (TryInstruction)getter.instructions[0];
            var catchType = (ClassTypeInfo)guarded.catches[1].binding.typeInfo;
            var tuple = (TupleTypeInfo)catchType.typeArguments!["p122-error-type"];
            tuple.elements[0].typeInfo = new GenericTypeInfo
            {
                type = MemberKind.Generic,
                required = true,
                ownerClassId = "p122-generic-error",
                genericParamId = "p122-error-type"
            };
            var closed = (ClassTypeInfo)NSGetterEvaluator.CloseContextType(catchType, ctx);
            Assert.IsTrue(NeoClient.TypeInfoMatches(client.TypeArguments.Of<(int, string)>(), closed.typeArguments!["p122-error-type"]));
            Assert.AreEqual("tuple", NSGetterEvaluator.Evaluate(getter, ctx));
        }

        [Test]
        public void TupleTypeMatchingComparesNestedElementsAcrossArgumentRepresentations()
        {
            using NeoClient client = Client();
            var original = (TupleTypeInfo)client.TypeArguments.Of<(int, (string, int))>();
            var json = JObject.FromObject(original);
            json["name"] = "value";
            var argument = json.ToObject<FunctionArgumentTypeInfo>()!;
            argument.elements![0].name = "Renamed";
            Assert.IsTrue(NeoClient.TypeInfoMatches(original, argument));
            Assert.IsTrue(NeoClient.TypeInfoMatches(argument, original));
            ((TupleTypeInfo)argument.elements[1].typeInfo).elements[0].typeInfo.required = false;
            Assert.IsFalse(NeoClient.TypeInfoMatches(original, argument));
            Assert.IsFalse(NeoClient.TypeInfoMatches(argument, original));
            Assert.IsFalse(NeoClient.TypeInfoMatches(client.TypeArguments.Of<(int, int)>(), client.TypeArguments.Of<(int, int, int)>()));
        }

        [Test]
        public void ClosedConstructorRejectsDifferentTupleBinding()
        {
            using NeoClient client = Client();
            var bindings = new Dictionary<string, TypeBinding>
            {
                ["p122-error-type"] = TestTypeBindings.Tuple(TestTypeBindings.Of(MemberKind.Int), TestTypeBindings.Of(MemberKind.Int)),
            };
            var closed = new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = "p122-generic-error",
                typeArguments = new Dictionary<string, TypeInfo> { ["p122-error-type"] = client.TypeArguments.Of<(int, int)>() }
            };
            Assert.DoesNotThrow(() => NeoGeneratedTypesSupport.ResolveDeclaredConstructor(client, closed,
                "p122-generic-error-constructor", new[] { "message" }, Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>(), bindings));
            foreach (TypeInfo mismatch in new[] { client.TypeArguments.Of<(int, string)>(), client.TypeArguments.Of<(int, int, int)>() })
            {
                closed.typeArguments["p122-error-type"] = mismatch;
                var failure = Assert.Throws<InvalidOperationException>(() => NeoGeneratedTypesSupport.ResolveDeclaredConstructor(client, closed,
                    "p122-generic-error-constructor", new[] { "message" }, Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>(), bindings));
                StringAssert.Contains("does not match closed class", failure!.Message);
            }
        }

        [Test]
        public void InvocationClosesTupleElementsAcrossWireRepresentations()
        {
            using NeoClient client = Client();
            var tuple = (TupleTypeInfo)client.TypeArguments.Of<(int, int)>();
            tuple.elements[0] = new TupleElementTypeInfo
            {
                name = "First",
                typeInfo = new GenericTypeInfo
                {
                    type = MemberKind.Generic,
                    required = false,
                    ownerClassId = "p122-generic-error",
                    genericParamId = "p122-error-type"
                }
            };
            var json = JObject.FromObject(tuple);
            json["name"] = "value";
            var argument = json.ToObject<FunctionArgumentTypeInfo>()!;
            var env = new Dictionary<string, NeoGenericEnvEntry> { ["p122-error-type"] = NeoGenericEnvEntry.Bound("p122-int-type") };
            foreach (TypeInfo input in new TypeInfo[] { tuple, argument })
            {
                var closed = (TupleTypeInfo)NeoNSFunctionRuntime.ResolveInvocationTypeInfo(client, input, env);
                Assert.AreEqual(MemberKind.Int, closed.elements[0].typeInfo.type);
                // P127 §4.2: `T?` bound to `int` closes to `int`.
                Assert.IsTrue(closed.elements[0].typeInfo.required);
                Assert.AreEqual("First", closed.elements[0].name);
                Assert.IsFalse(NeoNSFunctionRuntime.ContainsGeneric(closed));
            }
            Assert.AreEqual(MemberKind.Generic, tuple.elements[0].typeInfo.type);
        }

        [Test]
        public void GenericTupleErrorRebindsWhenCarriedIntoAnotherInvocation()
        {
            using NeoClient client = Client();
            var getter = TupleCatchGetter();
            getter.instructions = ((TryInstruction)getter.instructions[0]).instructions;
            var original = Assert.Throws<NeoScriptException>(() => NSGetterEvaluator.Evaluate(getter,
                new NSGetterEvaluator.Context(client, null, null)))!;
            var expected = new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = "p122-generic-error",
                typeArguments = new Dictionary<string, TypeInfo> { ["p122-error-type"] = client.TypeArguments.Of<(int, string)>() }
            };
            object originalValue = original.Payload.Value!;
            for (int i = 0; i < 2; i++)
            {
                var ctx = new NSGetterEvaluator.Context(client, null, null);
                var reentered = original.ForNativeReentry();
                Assert.IsTrue(NSGetterEvaluator.MatchesError(reentered, expected, ctx));
                Assert.AreEqual(((INeoValueReference)originalValue).valueId,
                    NSGetterEvaluator.FindRowIdByReference(NSGetterEvaluator.ErrorValue(reentered.Payload, ctx), ctx));
                Assert.AreSame(originalValue, original.Payload.Value);
            }
        }

        [Test]
        public void TupleSignaturesAllowRepeatedBindingsButRejectCycles()
        {
            using NeoClient client = Client();
            var tuple = new TupleMember
            {
                id = "p223-signature-probe",
                kind = MemberKind.Tuple,
                elementMemberIds = new[] { "p122-int-type", "p122-int-type" }
            };
            var signature = NeoGenericResolution.SignatureOfMember(client, tuple, NeoGenericResolution.EmptyEnv);
            Assert.AreEqual(MemberKind.Int, signature.elements![0].type);
            Assert.AreEqual(MemberKind.Int, signature.elements[1].type);
            Assert.IsTrue(client.TryGetMember("p223-error-int-tuple-type", out var descriptor));
            var stored = (TupleMember)descriptor!;
            var projected = (TupleTypeInfo)NeoNSFunctionRuntime.TypeInfoFromBindingMember(client, stored,
                NeoGenericResolution.EmptyEnv, new HashSet<string>());
            Assert.IsNull(projected.elements[0].name);
            Assert.IsNull(projected.elements[1].name);
            Assert.DoesNotThrow(() => JsonConvert.DeserializeObject<TypeInfo>(JsonConvert.SerializeObject(projected)));
            stored.elementMemberIds = new[] { stored.id, "p122-int-type" };
            Assert.Throws<InvalidOperationException>(() => NeoGenericResolution.SignatureOfMember(client, stored, NeoGenericResolution.EmptyEnv));
        }

        [Test]
        public void MissingGenericRegistrationLogsOnceEvenInsideCSharpFilter()
        {
            using NeoClient client = Client();
            client.RegisterGeneratedClassFactories(StructuredErrorGenerated.StructuredErrorsNeo.NeoReadOnlyValueFactories,
                StructuredErrorGenerated.StructuredErrorsNeo.NeoWritableValueFactories);
            LogAssert.Expect(LogType.Exception, new Regex("No generated error wrapper.*GenericError.*RegisterErrorFactory"));
            NeoScriptException? original = null;
            Exception? propagated = null;
            try
            {
                try
                {
                    NSGetterEvaluator.Evaluate(UncaughtGeneric(false), new NSGetterEvaluator.Context(client, null, null));
                }
                catch (NeoScriptException failure) when ((original = failure).Error is StructuredErrorGenerated.GenericError<int>) { Assert.Fail("Filter should fail"); }
            }
            catch (Exception failure) { propagated = failure; }
            // Unity Mono propagates filter exceptions; CLR suppresses them. Both
            // retain the logged configuration diagnostic and never choose a wrapper.
            Assert.IsNotNull(original);
            Assert.IsTrue(propagated is InvalidOperationException || ReferenceEquals(propagated, original));
            Assert.Throws<InvalidOperationException>(() => _ = original!.Error);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void InvalidFinallyTransferIsRejectedBeforeBodyEffects()
        {
            using NeoClient client = Client();
            var getter = Fixture()["cases"]![2]!["getter"]!.ToObject<FunctionWithReturnType>()!;
            var guarded = (TryInstruction)getter.instructions[1];
            guarded.finallyInstructions = new Instruction[] { new ReturnInstruction() };
            Assert.Throws<NeoScriptPreExecutionValidationError>(() => NSGetterEvaluator.Evaluate(getter, new NSGetterEvaluator.Context(client, null, null)));
        }

        [TestCaseSource(nameof(Cases))]
        public void ExecutesCompilerEmittedCase(string name)
        {
            JObject fixture = Fixture();
            using NeoClient client = Client();
            foreach (JToken test in (JArray)fixture["cases"]!)
            {
                if (test["name"]!.Value<string>() != name)
                    continue;
                var getter = test["getter"]!.ToObject<FunctionWithReturnType>()!;
                object? result = NSGetterEvaluator.Evaluate(getter, new NSGetterEvaluator.Context(client, null, null));
                Assert.AreEqual(test["expected"]!.Value<string>(), result);
                return;
            }
            Assert.Fail("Missing parity case");
        }
    }
}
