// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using NUnit.Framework;
using G = NeoCompose.Tests.P127GlobalizationParity;
using O = NeoCompose.Tests.P127OperatorsParity;
using I = NeoCompose.Tests.P127InterfacesParity;
using E = NeoCompose.Tests.P127EqualityParity;
using T = NeoCompose.Tests.P127TypesParity;
using R = NeoCompose.Tests.P127OrderingParity;

namespace NeoCompose.Tests
{
    public sealed class P127GeneratedIntegrationTests
    {
        private static NeoClient Client(string family) => NeoTestSaveStack.ClientFromSchema(
            NeoTestExport.Read(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/p127-" + family + "-parity-export.json")), assumeCurrentSchema: false);

        [Test]
        public void GeneratedOperatorsOutAndComparerBridgesRunTheirAuthoredBodies()
        {
            using var client = Client("operators");
            using var project = new O.TestProjectNeo(client);
            var a = new O.Fix(7);
            var b = new O.Fix(3);
            Assert.That((a + b).V, Is.EqualTo(10));
            Assert.That((a - b).V, Is.EqualTo(4));
            Assert.That((a * b).V, Is.EqualTo(21));
            Assert.That(a > b, Is.True);
            Assert.That(EqualityComparer<O.Fix>.Default.Equals(a, new O.Fix(7)), Is.True);
            Assert.That(EqualityComparer<O.IReadOnlyFix>.Default.Equals(a, new O.Fix(7)), Is.True);
            Assert.That(Comparer<O.Fix>.Default.Compare(a, b), Is.GreaterThan(0));
            Assert.That(Comparer<O.IReadOnlyFix>.Default.Compare(a, b), Is.GreaterThan(0));
            Assert.That(a.Equals((object)new O.Fix(7)), Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(7));
            Assert.That(O.Fix.TryParse("12", CultureInfo.InvariantCulture, out var parsed), Is.True);
            Assert.That(parsed.V, Is.EqualTo(12));
            Assert.That(O.Fix.TryParse("bad", CultureInfo.InvariantCulture, out var rejected), Is.False);
            Assert.That(rejected, Is.Null);
        }

        [Test]
        public void GeneratedDefaultInterfaceBodiesAndGenericStaticsKeepTheirConstructions()
        {
            using var client = Client("interfaces");
            using var project = new I.TestProjectNeo(client);
            var money = new I.Money();
            Assert.That(((I.IDescribed<int>)money).Describe(), Is.EqualTo("int"));
            Assert.That(((I.IDescribed<string>)money).Describe(), Is.EqualTo("money"));
            I.StaticBox<int>.Count = 10;
            I.StaticBox<string>.Count = 20;
            I.StaticBox<int>.Bump();
            Assert.That(I.StaticBox<int>.Count, Is.EqualTo(11));
            Assert.That(I.StaticBox<string>.Count, Is.EqualTo(20));
            I.StaticBox<int>.Held = 7;
            I.StaticBox<string>.Held = "seven";
            Assert.That(I.StaticBox<int>.Held, Is.EqualTo(7));
            Assert.That(I.StaticBox<string>.Held, Is.EqualTo("seven"));
        }

        [Test]
        public void StoredComparerReturnsItsGeneratedClrImplementation()
        {
            using var client = Client("equality");
            using var project = new E.TestProjectNeo(client);
            Assert.That(E.Probe.StoW(), Is.EqualTo(1));
            var set = E.Store.S;
            Assert.That(set.Comparer, Is.InstanceOf<E.Ci>());
            Assert.That(set.Comparer, Is.SameAs(set.Comparer));
            Assert.That(set.Contains("A"), Is.True);
            Assert.That(set.Add("A"), Is.False);
            Assert.That(set.Comparer.Equals("lower", "LOWER"), Is.True);
            var nongeneric = (System.Collections.IEqualityComparer)set.Comparer;
            Assert.That(nongeneric.Equals("lower", "LOWER"), Is.True);
            Assert.That(nongeneric.GetHashCode("lower"), Is.EqualTo(nongeneric.GetHashCode("LOWER")));
            var comparer = new E.AddMC();
            Assert.That(((IEqualityComparer<E.AddM>)comparer).Equals(new E.AddM(3), new E.AddM(3)), Is.True);
            Assert.That(((IEqualityComparer<E.IReadOnlyAddM>)comparer).Equals(new E.AddM(3), new E.AddM(4)), Is.False);
        }

        [Test]
        public void GeneratedOrderingComparerBridgesAcceptClassAndReadOnlyViews()
        {
            using var client = Client("ordering");
            using var project = new R.TestProjectNeo(client);
            var comparer = new R.NC();
            var first = new R.N(2);
            var second = new R.N(5);
            Assert.That(((IComparer<R.N>)comparer).Compare(first, second), Is.EqualTo(-3));
            Assert.That(((IComparer<R.IReadOnlyN>)comparer).Compare(first, second), Is.EqualTo(-3));
        }

        private sealed class HostProvider : IFormatProvider
        {
            internal readonly NumberFormatInfo Format = new();
            public object? GetFormat(Type? type) => type == typeof(NumberFormatInfo) ? Format : null;
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void GeneratedGenericFunctionReturnsTheSameTypedCultureFormatOrProvider(int kind)
        {
            using var client = Client("types");
            using var project = new T.TestProjectNeo(client);
            if (kind == 0)
            {
                var original = new CultureInfo("");
                Assert.That(T.Probe.Echo<CultureInfo>(original), Is.SameAs(original));
                original.NumberFormat.NegativeSign = "culture";
                Assert.That(T.Probe.Echo<CultureInfo>(original).NumberFormat.NegativeSign, Is.EqualTo("culture"));
            }
            else if (kind == 1)
            {
                var original = new NumberFormatInfo();
                Assert.That(T.Probe.Echo<NumberFormatInfo>(original), Is.SameAs(original));
                original.NegativeSign = "format";
                Assert.That(T.Probe.Echo<NumberFormatInfo>(original).NegativeSign, Is.EqualTo("format"));
            }
            else
            {
                var original = new HostProvider();
                Assert.That(T.Probe.Echo<IFormatProvider>(original), Is.SameAs(original));
                original.Format.NegativeSign = "provider";
                Assert.That(T.Probe.Echo<IFormatProvider>(original), Is.SameAs(original));
                Assert.That(((NumberFormatInfo)T.Probe.Echo<IFormatProvider>(original).GetFormat(typeof(NumberFormatInfo))!).NegativeSign, Is.EqualTo("provider"));
            }
        }

        [Test]
        public void GeneratedCultureFormatAndAuthoredProviderShareWritesAndReloadPayloads()
        {
            NeoClient reloaded;
            CultureInfo culture;
            NumberFormatInfo format;
            using (var client = Client("globalization"))
            using (var project = new G.TestProjectNeo(client))
            {
                // First crossing from NeoScript: authored defaults become CLR objects.
                var initial = G.Probe.StoredCulture;
                Assert.That(G.Probe.StoredCulture, Is.SameAs(initial));
                culture = new CultureInfo("");
                format = new NumberFormatInfo();
                G.Probe.StoredCulture = culture;
                G.Probe.StoredFormat = format;
                Assert.That(G.Probe.StoredCulture, Is.SameAs(culture));
                Assert.That(G.Probe.StoredFormat, Is.SameAs(format));
                culture.NumberFormat.PositiveSign = "host";
                Assert.That(G.Probe.StoredCulture.NumberFormat.PositiveSign, Is.EqualTo("host"));
                G.Probe.StoCultureReadBack();
                G.Probe.StoFormatWrite();
                Assert.That(culture.NumberFormat.NegativeSign, Is.EqualTo("~"));
                Assert.That(format.NumberDecimalSeparator, Is.EqualTo(","));
                var provider = new G.Counting(Answer: new NumberFormatInfo());
                provider.Answer.NegativeSign = "custom";
                G.Probe.StoredProvider = provider;
                var crossed = G.Probe.StoredProvider!;
                Assert.That(crossed, Is.SameAs(G.Probe.StoredProvider));
                var answer = (NumberFormatInfo)crossed.GetFormat(typeof(NumberFormatInfo))!;
                Assert.That(answer, Is.SameAs(((G.Counting)crossed).Answer));
                answer.PositiveSign = "shared";
                Assert.That(((NumberFormatInfo)crossed.GetFormat(typeof(NumberFormatInfo))!).PositiveSign, Is.EqualTo("shared"));
                Assert.That(((G.Counting)crossed).Calls, Is.EqualTo(2));
                reloaded = ReloadSession(client);
            }
            using (reloaded)
            using (var project = new G.TestProjectNeo(reloaded))
            {
                Assert.That(G.Probe.StoredCulture, Is.Not.SameAs(culture));
                Assert.That(G.Probe.StoredCulture.NumberFormat.NegativeSign, Is.EqualTo("~"));
                Assert.That(G.Probe.StoredCulture.NumberFormat.PositiveSign, Is.EqualTo("host"));
                Assert.That(G.Probe.StoredFormat, Is.Not.SameAs(format));
                Assert.That(G.Probe.StoredFormat.NumberDecimalSeparator, Is.EqualTo(","));
                var provider = (G.Counting)G.Probe.StoredProvider!;
                Assert.That(provider.Calls, Is.EqualTo(2));
                Assert.That(provider.Answer.NegativeSign, Is.EqualTo("custom"));
                Assert.That(((NumberFormatInfo)((IFormatProvider)provider).GetFormat(typeof(NumberFormatInfo))!).PositiveSign, Is.EqualTo("shared"));
            }
        }

        private static NeoClient ReloadSession(NeoClient client)
        {
            client.PublishHeldPayloadWrites(NeoValueOwnership.Session);
            var rows = JsonConvert.DeserializeObject<Dictionary<string, MemberValue>>(JsonConvert.SerializeObject(client.sessionValues))!;
            var reloaded = NeoTestSaveStack.ClientFromSchema(client.Synchronizer.Schema, loadedSaveContent: client.SerializeSaveData());
            var plan = new NeoWritePlan(reloaded);
            foreach (var row in rows.Values)
                plan.Set(NeoValueOwnership.Session, row);
            foreach (var member in client.members.Values)
            {
                if (member.Modifier != NeoMemberModifierKind.Static || member is NSFunctionMember
                    || client.ResolveStaticOwnership(member) != NeoValueOwnership.Session)
                    continue;
                client.TryResolveStaticBinding(member, NeoValueOwnership.Session, out string? id);
                plan.Bind(NeoValueOwnership.Session, member.id, true, id);
            }
            plan.Commit();
            return reloaded;
        }
    }
}
