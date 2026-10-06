// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
namespace NeoCompose.Tests
{
    public class P102QueriesDebugTests
    {
        private const string Root = "Packages/com.ryanbliss.neocompose/Tests/";
        private static JArray Cases() => (JArray)JObject.Parse(File.ReadAllText(Root + "neoscript-queries-debug-parity-fixture.json"))["evaluateCases"]!;
        public static IEnumerable<string> CaseNames()
        {
            foreach (JToken test in Cases())
                yield return test["name"]!.Value<string>()!;
        }
        [TestCaseSource(nameof(CaseNames))]
        public void MatchesSharedFixture(string name)
        {
            JObject test = null!;
            foreach (JObject candidate in Cases())
                if (candidate["name"]!.Value<string>() == name)
                    test = candidate;
            var getter = JsonConvert.DeserializeObject<FunctionWithReturnType>(test["getter"]!.ToString())!;
            NeoClient client = NeoTestSaveStack.LoadClient(File.ReadAllText(Root + "synth-example.json"));
            var logs = new List<string>();
            var events = new List<NeoScriptDebugEvent>();
            var ctx = new NSGetterEvaluator.Context(client, null, null) { DebugSink = value => { logs.Add(value.Message); events.Add(value); } };
            if (test["expectedError"] is JToken error)
            {
                Exception actual = Assert.Catch<Exception>(() => NSGetterEvaluator.Evaluate(getter, ctx))!;
                StringAssert.Contains(error.Value<string>(), actual.Message);
                Assert.IsTrue(actual.Data.Contains("neoScriptFrames"));
            }
            else
            {
                object? result = NSGetterEvaluator.Evaluate(getter, ctx);
                Assert.IsTrue(Equal(test["expected"]!, result is null ? JValue.CreateNull() : JToken.FromObject(result)), $"Expected {test["expected"]}, got {JsonConvert.SerializeObject(result)}");
            }
            if (test["logs"] is JArray expectedLogs)
                CollectionAssert.AreEqual(expectedLogs.ToObject<string[]>(), logs);
            if (test["events"] is JArray expectedEvents)
            {
                var serializer = JsonSerializer.Create(new JsonSerializerSettings { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() });
                Assert.IsTrue(JToken.DeepEquals(expectedEvents, JArray.FromObject(events, serializer)), JArray.FromObject(events, serializer).ToString());
            }
        }
        private static bool Equal(JToken left, JToken right)
        {
            if (left.Type is JTokenType.Integer or JTokenType.Float && right.Type is JTokenType.Integer or JTokenType.Float)
                return left.Value<double>() == right.Value<double>();
            if (left is JArray a && right is JArray b)
            {
                if (a.Count != b.Count)
                    return false;
                for (int i = 0; i < a.Count; i++)
                    if (!Equal(a[i], b[i]))
                        return false;
                return true;
            }
            return JToken.DeepEquals(left, right);
        }
        [Test]
        public void SinkFailuresAndDisabledOutputDoNotChangeExecution()
        {
            JObject test = (JObject)Cases()[0];
            var getter = JsonConvert.DeserializeObject<FunctionWithReturnType>(test["getter"]!.ToString())!;
            NeoClient client = NeoTestSaveStack.LoadClient(File.ReadAllText(Root + "synth-example.json"));
            var ctx = new NSGetterEvaluator.Context(client, null, null) { DebugSink = _ => throw new InvalidOperationException("sink") };
            Assert.AreEqual(true, NSGetterEvaluator.Evaluate(getter, ctx));
            ctx.DebugSink = null;
            Assert.AreEqual(true, NSGetterEvaluator.Evaluate(getter, ctx));
        }
    }
}
