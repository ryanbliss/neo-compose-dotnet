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

        public static IEnumerable<string> Refuses()
        {
            foreach (JProperty testCase in Fixture()["refuses"]!.Children<JProperty>())
                yield return testCase.Name;
        }

        [TestCaseSource(nameof(Refuses))]
        public void ProbeRefuses(string probe)
        {
            using NeoClient client = Client();

            Exception error = Assert.Catch(() => Invoke(client, probe))!;

            StringAssert.Contains(Fixture()["refuses"]![probe]!.Value<string>(), error.ToString());
        }

        [Test]
        public void StoredSetRefusesARowOfAClassMadeEquatableLater()
        {
            // The compiler refuses a stored Set of an equatable class, so the
            // runtime guard covers a class that becomes equatable afterwards.
            JObject export = JObject.Parse(File.ReadAllText(ExportPath));
            JObject coin = ExportedClass(export, "Coin");
            JObject token = ExportedClass(export, "Token");
            token["implementsInterfaceIds"] = coin["implementsInterfaceIds"]!.DeepClone();
            token["implementsInterfaceArguments"] = coin["implementsInterfaceArguments"]!.DeepClone();
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(export.ToString()), assumeCurrentSchema: false);

            Exception error = Assert.Catch(() => Invoke(client, "ShelveToken"))!;

            StringAssert.Contains("`Set` elements can't hold `IEquatable` class 'Token', since a stored Set keys entries by row.", error.ToString());
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

            // Authored Equals decides even against itself: NaN readings differ.
            var reading = new Parity.Reading(-1);
            Assert.That(reading.Equals((object)reading), Is.False);
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
        public void GeneratedApiCrossesInterfaceBoundariesBothWays()
        {
            using var app = new Parity.TestProjectNeo(Client());
            var five = new Parity.Coin(5);

            Assert.That(Parity.Probe.ClrTupleOut(), Is.EqualTo((1, "a")));
            Assert.That(Parity.Probe.ClrDecimalOut(), Is.EqualTo(1.10m));
            Assert.That(Parity.Probe.ClrDecimalOut().ToString(), Is.EqualTo("1.10"));
            Assert.That(Parity.Probe.ClrFloatOut(), Is.EqualTo(2.5f));
            Assert.That(Parity.Probe.ClrCoinOut().Equals(five), Is.True);
            IReadOnlyList<IEquatable<decimal>> decimals = Parity.Probe.ClrDecimalsOut();
            Assert.That(decimals[0], Is.EqualTo(1.10m));
            Assert.That(decimals[1], Is.InstanceOf<Parity.IReadOnlyDecBox>());
            var pair = Parity.Probe.ClrPairOut();
            Assert.That(pair.Item1, Is.EqualTo((1, "a")));
            Assert.That(pair.Item2, Is.EqualTo(2.5f));

            Assert.That(Parity.Probe.ClrTupleIn((1, "a")), Is.True);
            Assert.That(Parity.Probe.ClrTupleIn((2, "a")), Is.False);
            Assert.That(Parity.Probe.ClrValuesIn(1.10m, 2.5f, five), Is.EqualTo("true|true|true"));
            Assert.That(Parity.Probe.ClrDecimalsIn(new List<IEquatable<decimal>> { 1.10m, new Parity.DecBox(2.5m) }), Is.EqualTo("2|true|true"));
            Assert.That(Parity.Probe.ClrPairIn(((1, "a"), five)), Is.EqualTo("true|true"));

            var error = Assert.Catch(() => Parity.Probe.ClrTupleIn(new ForeignPair()))!;
            StringAssert.Contains("received a CLR ForeignPair, which is not a value of Interface 'IEquatable'", error.ToString());
            // P127 §4. A tuple is IEquatable only when its elements are.
            Parity.IReadOnlyPlain plain = new Parity.Plain(1);
            var plainError = Assert.Catch(() => Parity.Probe.ClrPlainTupIn((1, plain)))!;
            StringAssert.Contains("which is not a value of Interface 'IEquatable'", plainError.ToString());
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

        [Test]
        public void StoredGenericRowsRunAfterASaveLoad()
        {
            string afterBump = Fixture()["returns"]!.Value<string>("SavedBank")!;
            string saved;
            using (NeoClient client = Client())
            {
                Assert.That(Invoke(client, "SavedBank"), Is.EqualTo(afterBump));
                saved = client.SerializeSaveData();
            }

            using NeoClient reloaded = Client(saved);

            Assert.That(Invoke(reloaded, "ReadSavedBank"), Is.EqualTo(afterBump));
        }

        private sealed class ForeignPair : IEquatable<(int, string)>
        {
            public bool Equals((int, string) other) => true;
        }

        private static string ExportPath => Path.Combine(PackageRoot, "primitive-interfaces-parity-export.json");

        private static NeoClient Client(string? save = null)
        {
            ProjectData data = NeoTestExport.Read(File.ReadAllText(ExportPath));
            return NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false, loadedSaveContent: save);
        }

        private static JObject ExportedClass(JObject export, string name) =>
            export["classes"]!.Children<JProperty>()
                .Select(property => (JObject)property.Value)
                .Single(record => record.Value<string>("name") == name);

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
