// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §1.2. A construction whose type argument wraps its owner's param
    /// (<c>Bag&lt;List&lt;T&gt;&gt;</c> in <c>Holder&lt;T&gt;</c>) closes it
    /// through the owner's environment, as the server's <c>neo test</c>
    /// does. <c>p127-owner-bindings-source.json</c> pins each probe's result
    /// there; the export is that source's <c>neo export</c>.
    /// </summary>
    public class P127OwnerBindingsTests
    {
        private const string Root = "Packages/com.ryanbliss.neocompose/Tests/p127-owner-bindings-";

        public static IEnumerable<TestCaseData> Cases()
        {
            var fixture = JObject.Parse(File.ReadAllText(Root + "source.json"));
            foreach (var result in ((JObject)fixture["returns"]!).Properties())
                yield return new TestCaseData(result.Name, result.Value.ToObject<object>()).SetName(result.Name);
        }

        [TestCaseSource(nameof(Cases))]
        public void ReturnsWhatTheServerReturns(string name, object expected)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(
                NeoTestExport.Read(File.ReadAllText(Root + "export.json")), assumeCurrentSchema: false);
            var function = client.members.Values.Single(member => member is NSFunctionMember && member.name == name);

            object? result = NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(Array.Empty<object?>());

            Assert.AreEqual(expected, result);
        }
    }
}
