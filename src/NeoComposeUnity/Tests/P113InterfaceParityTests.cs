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
using Parity = NeoCompose.Tests.InterfaceParity;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P113 interface members: the Unity half of the parity gate. The cases and
    /// their source are a verbatim copy of neo-compose's
    /// <c>src/models/neoscript/interface-members-parity-fixture.json</c>; the
    /// export is that source's <c>neo export</c>, which neo-compose's
    /// <c>cli/src/commands/interface-members-parity.test.ts</c> writes when
    /// <c>NEO_INTERFACE_PARITY_EXPORT</c> names this package's copy. The
    /// generated-surface tests read rows NeoScript wrote, so each read builds
    /// its value from the row rather than a cached C# wrapper.
    /// </summary>
    public class P113InterfaceParityTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        private sealed class ForeignContainer : Parity.IContainer<Parity.Seed>
        {
            public IReadOnlyList<Parity.Seed> Contents => Array.Empty<Parity.Seed>();
        }

        public static IEnumerable<string> Returns() => CaseNames("returns");

        public static IEnumerable<string> Refuses() => CaseNames("refuses");

        [TestCaseSource(nameof(Returns))]
        public void ProbeReturns(string probe)
        {
            using NeoClient client = Client();

            Assert.AreEqual(Case("returns", probe).ToObject<object>(), InvokeProbe(client, probe));
        }

        [TestCaseSource(nameof(Refuses))]
        public void ProbeRefuses(string probe)
        {
            using NeoClient client = Client();

            StringAssert.Contains(Case("refuses", probe).Value<string>(), Assert.Catch(() => InvokeProbe(client, probe)).ToString());
        }

        [Test]
        public void SlotReadsADeterminedRowAsItsWritableConstruction()
        {
            using var app = new Parity.TestProjectNeo(Client());
            InvokeProbe(app.Client, "DeterminedRead");

            var crate = Parity.Probe.P.Pouch as Parity.Crate<Parity.Seed>;

            Assert.IsNotNull(crate, "the resolver builds Crate<Seed> from the slot's arguments");
            Assert.IsFalse(crate!.IsReadOnly);
            Assert.AreEqual("rye", crate.Contents[0].Name);
        }

        [Test]
        public void SlotReadsAClosedRowThroughItsClassFactory()
        {
            using var app = new Parity.TestProjectNeo(Client());
            InvokeProbe(app.Client, "ClosedRead");

            Assert.IsInstanceOf<Parity.SeedPouch>(Parity.Probe.P.Pouch);
        }

        [Test]
        public void CollectionsAndBoxReadEachRowAsItsClass()
        {
            using var app = new Parity.TestProjectNeo(Client());
            InvokeProbe(app.Client, "Collections");

            Assert.IsInstanceOf<Parity.Crate<Parity.Seed>>(Parity.Probe.P.Racks[0]);
            Assert.IsInstanceOf<Parity.SeedPouch>(Parity.Probe.P.Racks[1]);
            Assert.IsInstanceOf<Parity.Crate<Parity.Seed>>(Parity.Probe.P.ByName["a"]);
            Assert.IsInstanceOf<Parity.Crate<Parity.Seed>>(Parity.Probe.P.Boxed.Value);
        }

        [Test]
        public void ReferenceReadsItsTargetAsTheInterface()
        {
            using var app = new Parity.TestProjectNeo(Client());
            InvokeProbe(app.Client, "ReferenceRead");

            var favorite = Parity.Probe.P.Favorite as Parity.Crate<Parity.Seed>;

            Assert.IsNotNull(favorite);
            Assert.AreEqual(((INeoValueReference)Parity.Probe.P.Racks[0]).valueId, favorite!.valueId);
            Assert.AreEqual("ref", favorite.Contents[0].Name);
        }

        [Test]
        public void ReferenceWritesAGeneratedValueAndRefusesACSharpClass()
        {
            using var app = new Parity.TestProjectNeo(Client());
            InvokeProbe(app.Client, "Collections");

            Parity.Probe.P.Favorite = Parity.Probe.P.Racks[1];
            ArgumentException error = Assert.Throws<ArgumentException>(() => Parity.Probe.P.Favorite = new ForeignContainer());

            Assert.IsInstanceOf<Parity.SeedPouch>(Parity.Probe.P.Favorite);
            StringAssert.StartsWith("Player.Favorite needs a Neo class value, and ForeignContainer is a C# class.", error.Message);
        }

        [Test]
        public void SlotWriteRefusesACSharpClass()
        {
            using var app = new Parity.TestProjectNeo(Client());

            ArgumentException error = Assert.Throws<ArgumentException>(() => Parity.Probe.P.Pouch = new ForeignContainer());

            StringAssert.StartsWith("Player.Pouch needs a Neo class value, and ForeignContainer is a C# class.", error.Message);
        }

        [Test]
        public void GenericOwnerResolvesItsOwnParamsConstruction()
        {
            using var app = new Parity.TestProjectNeo(Client());
            InvokeProbe(app.Client, "ShelfRead");

            Assert.IsInstanceOf<Parity.Crate<Parity.Seed>>(Parity.Probe.P.SeedShelf.Held);
        }

        [Test]
        public void GetterResultReadsAsItsClass()
        {
            using var app = new Parity.TestProjectNeo(Client());
            InvokeProbe(app.Client, "GetterRead");

            Assert.IsInstanceOf<Parity.Crate<Parity.Seed>>(Parity.Probe.P.PouchView);
        }

        [Test]
        public void ClosedDescendantSatisfiesItsBasesInterface()
        {
            using var app = new Parity.TestProjectNeo(Client());
            InvokeProbe(app.Client, "ToolCrateFirst");

            Parity.ISortedContainer<Parity.Tool> sorted = Parity.Probe.P.Tools;

            Assert.AreEqual("hoe", sorted.First.Name);
        }

        private static NeoClient Client()
        {
            ProjectData data = NeoTestExport.Read(File.ReadAllText(Path.Combine(PackageRoot, "interface-parity-export.json")));
            return NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
        }

        private static IEnumerable<string> CaseNames(string table)
        {
            foreach (JProperty testCase in Fixture()[table]!.Children<JProperty>())
                yield return testCase.Name;
        }

        private static JToken Case(string table, string probe) => Fixture()[table]![probe]!;

        private static JObject Fixture() =>
            JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "interface-members-parity-fixture.json")));

        private static object? InvokeProbe(NeoClient client, string name)
        {
            Member function = client.members.Values.Single(member =>
                member is NSFunctionMember && member.name == name);
            return NeoMemberNSFunction.GetOrCreateStatic(client, function.id)
                .InvokeStatic(Array.Empty<object?>());
        }
    }
}
