// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §7 and §8.1. <c>primitive-conformance-fixture.json</c> is copied
    /// verbatim from compose's
    /// <c>packages/neoscript-language/src/primitive-conformance-fixture.json</c>;
    /// the SDK's one table must say exactly what it says.
    /// </summary>
    public class P127PrimitiveConformanceTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        [Test]
        public void TableMatchesTheSharedFixture()
        {
            JObject fixture = JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "primitive-conformance-fixture.json")));
            var interfaces = (JObject)fixture["interfaces"]!;

            Assert.That(
                NeoPrimitiveConformance.InterfaceIds.Select(entry => $"{entry.Key}={entry.Value}"),
                Is.EqualTo(interfaces.Properties().Select(entry => $"{entry.Name}={entry.Value}")));
            Assert.That(NeoPrimitiveConformance.NumberStylesEnumId, Is.EqualTo(fixture.Value<string>("numberStylesEnumId")));
            Assert.That(
                NeoPrimitiveConformance.ContravariantInterfaceIds,
                Is.EquivalentTo(fixture["contravariant"]!.Values<string>().Select(name => interfaces.Value<string>(name!))));

            var rows = ((JObject)fixture["conformance"]!).Properties().ToList();
            Assert.That(System.Enum.GetNames(typeof(NeoConformingTypeKind)), Is.EqualTo(rows.Select(row => row.Name)));
            foreach (JProperty row in rows)
            {
                var kind = (NeoConformingTypeKind)System.Enum.Parse(typeof(NeoConformingTypeKind), row.Name);
                Assert.That(NeoPrimitiveConformance.Table[kind].Select(Spell), Is.EqualTo(row.Value.Values<string>()), row.Name);
            }
        }

        [Test]
        public void MintedComparerConformanceComesFromClassRecords()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(PackageRoot + "/p127-equality-parity-export.json")), assumeCurrentSchema: false);
            foreach (var (id, expected) in new[]
            {
                (NeoSystemComparerClasses.EqualityComparerId, "IEqualityComparer`1<string>"),
                (NeoSystemComparerClasses.ComparerId, "IComparer`1<string>"),
            })
            {
                var binding = TestTypeBindings.FromKey("c:" + id + "<string>", client.ProjectDataForRuntime);
                var closure = NeoInterfaceDispatch.Of(client).Closure(binding);
                Assert.That(closure.Select(item => SpellClosed(item.Head!.InterfaceId!, item.Bindings?.Values.Select(client.TypeRules.KeyOf) ?? Array.Empty<string>())), Does.Contain(expected));
            }
            Assert.That(NeoSystemComparerClasses.NameOf("system_not-a-comparer"), Is.Null);
        }

        private static string SpellClosed(string interfaceId, System.Collections.Generic.IEnumerable<string> arguments)
        {
            string name = NeoPrimitiveConformance.InterfaceIds.Single(entry => entry.Value == interfaceId).Key;
            string joined = string.Join(",", arguments);
            return joined.Length == 0 ? name : $"{name}<{joined}>";
        }

        /// <summary>The fixture's spelling: <c>IShiftOperators`3&lt;S,int,S&gt;</c>.</summary>
        private static string Spell(NeoConformanceConstruction construction) =>
            SpellClosed(construction.InterfaceId, construction.Arguments.Select(SpellArgument));

        private static string SpellArgument(NeoConformanceArgumentKind argument) => argument switch
        {
            NeoConformanceArgumentKind.Self => "S",
            NeoConformanceArgumentKind.Int => "int",
            _ => "bool",
        };
    }
}
