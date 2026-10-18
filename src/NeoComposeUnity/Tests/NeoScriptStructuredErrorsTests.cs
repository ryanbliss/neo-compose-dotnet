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
