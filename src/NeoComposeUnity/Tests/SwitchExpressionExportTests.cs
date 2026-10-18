// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class SwitchExpressionExportTests
    {
        [Test]
        public void ExportedGenericClosedFamilyMatchesTypeScript()
        {
            const string root = "Packages/com.ryanbliss.neocompose/Tests/";
            var project = JObject.Parse(File.ReadAllText(root + "synth-example.json"));
            var fixture = JObject.Parse(File.ReadAllText(root + "switch-expression-example.json"));
            foreach (string kind in new[] { "classes", "members", "constructors" })
            {
                foreach (var property in ((JObject)fixture[kind]).Properties())
                    ((JObject)project[kind])[property.Name] = property.Value;
            }
            using var client = NeoTestSaveStack.LoadClient(project.ToString());
            var member = ((JObject)project["members"]).Properties().Select(p => (JObject)p.Value).Single(m => m["name"].Value<string>() == "Exercise");
            var getter = member["action"].ToObject<FunctionWithReturnType>();
            var node = NeoGeneratedTypesSupport.CreateWritableClassValue(client, "62b9f6ed-a169-45da-8a28-fe241572737d");
            Assert.That(NSGetterEvaluator.Evaluate(getter, new NSGetterEvaluator.Context(client, node, null)), Is.EqualTo("ok 7"));
        }
    }
}
