// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Parity = NeoCompose.Tests.PrimitiveInterfaceParity;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 primitive interfaces and generic math: the Unity half of the
    /// parity gate. The cases and their source are a verbatim copy of
    /// neo-compose's <c>src/models/neoscript/primitive-interfaces-parity-fixture.json</c>;
    /// the export is that source's <c>neo export</c>, which neo-compose's
    /// <c>cli/src/commands/primitive-interfaces-parity.test.ts</c> writes when
    /// <c>NEO_P127_PARITY_EXPORT</c> names this package's copy.
    /// </summary>
    public class P127PrimitiveInterfaceParityTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        public static IEnumerable<string> Returns()
        {
            foreach (JProperty testCase in Fixture()["returns"]!.Children<JProperty>())
                yield return testCase.Name;
        }

        [TestCaseSource(nameof(Returns))]
        public void ProbeReturns(string probe)
        {
            using NeoClient client = Client();
            object? value = Invoke(client, probe);
            JToken expected = Fixture()["returns"]![probe]!;
            JToken actual = value is null ? JValue.CreateNull() : JToken.FromObject(value);
            if (expected.Type is JTokenType.Integer or JTokenType.Float)
                Assert.That(actual.Value<double>(), Is.EqualTo(expected.Value<double>()), probe);
            else
                Assert.That(JToken.DeepEquals(expected, actual), Is.True, $"{probe}: {actual}");
        }

        [Test]
        public void ConformanceTableMatchesTheCompilerPin()
        {
            JObject pin = JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "primitive-interface-conformance.json")));

            Assert.That(JToken.DeepEquals(pin["interfaces"], JObject.FromObject(NeoPrimitiveInterfaces.InterfaceIds)), Is.True, "interfaces");
            Assert.That(JToken.DeepEquals(pin["parameters"], JObject.FromObject(NeoPrimitiveInterfaces.ParameterIds)), Is.True, "parameters");
            Assert.That(JToken.DeepEquals(pin["members"], JObject.FromObject(NeoPrimitiveInterfaces.MemberIds)), Is.True, "members");
            Assert.That(JToken.DeepEquals(pin["conformance"], JObject.FromObject(NeoPrimitiveInterfaces.Conformance)), Is.True, "conformance");
        }

        [Test]
        public void EquatableWrappersHashAndCompareByAuthoredEquals()
        {
            using var app = new Parity.TestProjectNeo(Client());
            var five = new Parity.Coin(5);
            var otherFive = new Parity.Coin(5);
            var seven = new Parity.Coin(7);

            Assert.That(five.Equals((object)otherFive), Is.True);
            Assert.That(five.Equals((object)seven), Is.False);
            Assert.That(five.Equals((object)new Parity.Plain(5)), Is.False);
            Assert.That(five.GetHashCode(), Is.EqualTo(1));
            Assert.That(new HashSet<Parity.Coin> { five, otherFive, seven }, Has.Count.EqualTo(2));
            Assert.That(new HashSet<Parity.IReadOnlyCoin> { five, otherFive }, Has.Count.EqualTo(1));
        }

        [Test]
        public void ComparableWrappersSortThroughTheClrComparer()
        {
            using var app = new Parity.TestProjectNeo(Client());
            var versions = new List<Parity.Ver> { new(3), new(1), new(2) };

            versions.Sort();

            Assert.That(versions.Select(version => version.N), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(((IComparable)versions[0]).CompareTo(null), Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => ((IComparable)versions[0]).CompareTo("v1"));
        }

        [Test]
        public void ClrPrimitivesCrossInterfaceParameters()
        {
            using NeoClient client = Client();

            Assert.That(Invoke(client, "ViaInterface", 3), Is.EqualTo(true));
            Assert.That(Invoke(client, "IsCmpInt", 3), Is.EqualTo(true));
            Assert.That(Invoke(client, "LessFloat", 2.5f), Is.EqualTo(true));
            Assert.That(Invoke(client, "ShowDecimal", 1.10m), Is.EqualTo("1.10"));
            NSFunctionMember log = client.members.Values.OfType<NSFunctionMember>()
                .Single(member => member.name == "Log" && member.argumentTypes[0].type == MemberKind.Interface);
            Assert.That(NeoMemberNSFunction.GetOrCreateStatic(client, log.id).InvokeStatic(1.1m), Is.EqualTo("decimal"));
        }

        [Test]
        public void ForeignClrValuesNameTheInterface()
        {
            using NeoClient client = Client();

            var error = Assert.Catch(() => Invoke(client, "ViaInterface", Guid.Empty))!;

            StringAssert.Contains("received a CLR Guid, which is not a value of Interface 'IEquatable'", error.Message);
        }

        [Test]
        public void EvaluatorPrimitivesResolveAsTheirClrInterface()
        {
            using var app = new Parity.TestProjectNeo(Client());
            NeoClient client = app.Client;

            IComparable<decimal>? boxed = NeoGeneratedTypesSupport.ResolveNativeFunctionClassArgument<IComparable<decimal>>(
                client, "1.10", true, Parity.TestProjectNeo.NeoReadOnlyValueFactories, Parity.TestProjectNeo.NeoWritableValueFactories, "result");
            IEquatable<int>? integer = NeoGeneratedTypesSupport.ResolveNativeFunctionClassArgument<IEquatable<int>>(
                client, 3.0, true, Parity.TestProjectNeo.NeoReadOnlyValueFactories, Parity.TestProjectNeo.NeoWritableValueFactories, "result");

            Assert.That(boxed, Is.EqualTo(1.10m));
            Assert.That(integer, Is.EqualTo(3));
        }

        private static NeoClient Client()
        {
            ProjectData data = NeoTestExport.Read(File.ReadAllText(Path.Combine(PackageRoot, "primitive-interfaces-parity-export.json")));
            return NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
        }

        private static JObject Fixture() =>
            JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "primitive-interfaces-parity-fixture.json")));

        private static object? Invoke(NeoClient client, string name, params object?[] args)
        {
            Member function = client.members.Values.Single(member =>
                member is NSFunctionMember && member.name == name);
            return NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(args);
        }
    }
}
