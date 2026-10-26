// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class P120FunctionTypeInferenceTests
    {
        [TestCase("Nested", 7)]
        [TestCase("Captured", 7)]
        [TestCase("Typed", 10)]
        [TestCase("Once", 3)]
        [TestCase("Order", 2)]
        [TestCase("Combined", 20)]
        [TestCase("TypeNamedCapture", 5)]
        [TestCase("NarrowedCapture", 5)]
        [TestCase("NestedCapture", 5)]
        public void EvaluatesCompilerInferredCalls(string name, int expected)
        {
            var fixture = JObject.Parse(NeoScriptFunctionTypeInferenceParityFixture.Json);
            using NeoClient client = NeoTestSaveStack.LoadClient(fixture["document"]!.ToString());
            JObject member = null!;
            foreach (var property in ((JObject)fixture["document"]!["members"]!).Properties())
            {
                if (property.Value["name"]!.Value<string>() == name)
                    member = (JObject)property.Value;
            }
            Assert.IsNotNull(member);
            var getter = JsonConvert.DeserializeObject<FunctionWithReturnType>(member["getter"]!.ToString())!;
            var logs = new List<string>();
            var context = new NSGetterEvaluator.Context(client, new Dictionary<string, object>(), null)
            {
                DebugSink = value => logs.Add(value.Message)
            };
            Assert.AreEqual(expected, NSGetterEvaluator.Evaluate(getter, context));
            var expectedLogs = name == "Order" ? new[] { "2", "3" } : new string[0];
            if (name == "Once")
                expectedLogs = new[] { "2" };
            CollectionAssert.AreEqual(expectedLogs, logs);
        }
    }
}
