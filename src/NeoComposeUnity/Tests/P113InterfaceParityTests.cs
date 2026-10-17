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

        [Test]
        public void ClosedDescendantReadsItsBasesListThroughTheReadOnlyInterface()
        {
            using var app = new Parity.TestProjectNeo(Client());
            InvokeProbe(app.Client, "ToolCrateFirst");

            NeoList<Parity.IReadOnlyTool> contents = ((Parity.IReadOnlyCrate<Parity.IReadOnlyTool>)Parity.Probe.P.Tools).Contents;
            contents.Add(new Parity.Tool { Name = "rake" });

            Assert.AreEqual("hoe", contents[0].Name);
            Assert.AreEqual(new[] { "hoe", "rake" }, Parity.Probe.P.Tools.Contents.Select(tool => tool.Name).ToArray());
        }

        [Test]
        public void InheritedCollectionInterfaceReadKeepsATemporaryDetached()
        {
            using var app = new Parity.TestProjectNeo(Client());
            Parity.IReadOnlyInventory inventory = Parity.Probe.TemporaryInventory();
            var temporary = (NeoGeneratedClassValue)inventory;

            Assert.IsNotNull(temporary.PendingValue);
            NeoReadOnlyList<Parity.IReadOnlySeed> contents = inventory.Contents;

            Assert.AreEqual(1, contents.Count);
            Assert.AreEqual("oat", contents[0].Name);
            Assert.AreSame(contents, inventory.Contents);
            Assert.IsNotNull(temporary.PendingValue, "Reading an inherited collection must not attach its temporary owner.");
        }

        [Test]
        public void ConstructedClassEntriesShareRowsThroughTheReadOnlyInterface()
        {
            using var app = new Parity.TestProjectNeo(Client());
            InvokeProbe(app.Client, "SetupFrames");
            Parity.ToolFrames owner = Parity.Probe.P.ToolFrames;
            var view = (Parity.IReadOnlyFrameCrate<Parity.IReadOnlyTool?>)owner;
            Parity.Frame<Parity.IReadOnlyTool?> frame = view.Frames[0];

            Assert.AreEqual("hoe", frame.Value!.Name);
            Assert.AreEqual(owner.Frames[0].valueId, frame.valueId);
            frame.Value = new Parity.Tool { Name = "rake" };
            Assert.AreEqual("rake", owner.Frames[0].Value!.Name);
            Parity.Frame<Parity.IReadOnlyTool?> second = frame.Clone();
            second.Value = new Parity.Tool { Name = "spade" };
            view.Frames.Add(second);
            Assert.AreEqual("spade", owner.Frames[1].Value!.Name);
        }

        [Test]
        public void ConstructedVectorEntriesShareRowsThroughTheReadOnlyInterface()
        {
            using var app = new Parity.TestProjectNeo(Client());
            InvokeProbe(app.Client, "SetupFrames");
            Parity.VectorFrames owner = Parity.Probe.P.VectorFrames;
            var view = (Parity.IReadOnlyFrameCrate<NeoReadOnlyVector3>)owner;
            Parity.Frame<NeoReadOnlyVector3> frame = view.Frames[0];

            Assert.AreEqual(1f, frame.Value.x);
            Assert.AreEqual(owner.Frames[0].valueId, frame.valueId);
            frame.Value = new NeoReadOnlyVector3(4f, 5f, 6f);
            Assert.AreEqual(4f, owner.Frames[0].Value.x);
            Parity.Frame<NeoReadOnlyVector3> second = frame.Clone();
            second.Value = new NeoReadOnlyVector3(7f, 8f, 9f);
            view.Frames.Add(second);
            Assert.AreEqual(7f, owner.Frames[1].Value.x);
        }

        [Test]
        public void FunctionReturnsItsConstructedClass()
        {
            using var app = new Parity.TestProjectNeo(Client());

            Parity.IReadOnlyCrate<Parity.Seed> crate = Parity.Probe.SeedCrate();

            Assert.IsInstanceOf<Parity.Crate<Parity.Seed>>(crate);
            Assert.AreEqual("oat", crate.Contents[0].Name);
        }

        [Test]
        public void GenericFunctionReturnsARowCSharpPassedIn()
        {
            using var app = new Parity.TestProjectNeo(Client());
            InvokeProbe(app.Client, "Collections");

            Parity.IContainer<Parity.Seed>? first = Parity.Probe.FirstAccepted(Parity.Probe.P.Racks);

            Assert.IsInstanceOf<Parity.Crate<Parity.Seed>>(first);
            Assert.AreEqual(((INeoValueReference)Parity.Probe.P.Racks[0]).valueId, ((INeoValueReference)first!).valueId);
        }

        [Test]
        public void GenericFunctionBuildsAnUnnamedConstructionThroughItsConstraint()
        {
            using var app = new Parity.TestProjectNeo(Client());
            object? crate = InvokeProbe(app.Client, "MakeCrate");

            Parity.ISortedContainer<Parity.Seed> sorted = ReadAs<Parity.ISortedContainer<Parity.Seed>>(app.Client, crate);

            Assert.IsInstanceOf<Parity.Crate<Parity.Seed>>(sorted);
        }

        [Test]
        public void GenericFunctionRefusesARowItsConstraintDoesNotDetermine()
        {
            using var app = new Parity.TestProjectNeo(Client());
            object? bag = InvokeProbe(app.Client, "MakeBag");

            NeoTypeArgumentException error = Assert.Throws<NeoTypeArgumentException>(
                () => ReadAs<Parity.ITagged<Parity.Seed, Parity.Red>>(app.Client, bag));

            Assert.AreEqual(
                "FirstAccepted returned a Bag row as ITagged<Seed, Red>, which the project never names, and IContainer<Seed> doesn't determine Bag. Pass a type argument the project names.",
                error.Message);
        }

        [Test]
        public void ClosedDescendantBridgesItsBasesGenericFunction()
        {
            using var app = new Parity.TestProjectNeo(Client());
            InvokeProbe(app.Client, "ToolCratePick");

            Parity.ISortedContainer<Parity.Tool> sorted = Parity.Probe.P.Tools;

            Assert.AreEqual("rake", sorted.Pick(0).Name);
        }

        /// <summary>A FirstAccepted result C# never held, read as <typeparamref name="T"/>.</summary>
        private static T ReadAs<T>(NeoClient client, object? value)
            where T : class, Parity.IContainer<Parity.Seed> =>
            NeoGeneratedTypesSupport.ReadInterfaceTypeArgument<T, Parity.IContainer<Parity.Seed>>(
                client,
                value,
                "FirstAccepted",
                "T",
                Parity.TestProjectNeo.NeoReadOnlyValueFactories,
                Parity.TestProjectNeo.NeoWritableValueFactories);

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
