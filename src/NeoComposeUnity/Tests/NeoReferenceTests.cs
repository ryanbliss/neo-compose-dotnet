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
using Parity = NeoCompose.Tests.ReferenceParity;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P112 references to any value: the Unity half of the parity gate. The
    /// cases and their source are a verbatim copy of neo-compose's
    /// <c>src/models/neoscript/reference-parity-fixture.json</c>; the export
    /// is that source's <c>neo export</c>, which neo-compose's
    /// <c>cli/src/commands/reference-parity.test.ts</c> writes when
    /// <c>NEO_REFERENCE_PARITY_EXPORT</c> names this package's copy. Tests
    /// that need a bound selection add it to <c>Probe.G</c>'s row, because
    /// entry ids derive from member ids that change on every export.
    /// </summary>
    public partial class NeoReferenceTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        public static IEnumerable<string> Returns() => CaseNames("returns");

        public static IEnumerable<string> Refuses() => CaseNames("refuses");

        [TestCaseSource(nameof(Returns))]
        public void ProbeReturns(string probe)
        {
            using NeoClient client = Client(Data());

            Assert.AreEqual(Case("returns", probe).ToObject<object>(), InvokeProbe(client, probe));
        }

        [TestCaseSource(nameof(Refuses))]
        public void ProbeRefuses(string probe)
        {
            StringAssert.Contains(Case("refuses", probe).Value<string>(), ProbeError(Data(), probe));
        }

        [Test]
        public void BoundReferenceWritesItsEntryUntilItIsRemoved()
        {
            ProjectData data = Data();
            Select(data, "Pick", ((ArrayMemberValue)GRow(data, "Xs")).value![0]);
            using NeoClient client = Client(data);

            Assert.AreEqual(52, InvokeProbe(client, "Entry"));
            StringAssert.Contains("which no longer exists", ProbeError(data, "EntryRemoved"));
        }

        [Test]
        public void SetEntryIsNotWrittenInPlace()
        {
            ProjectData data = Data();
            Select(data, "Held", ((ArrayMemberValue)data.values[MemberNamed<SetMember>(data, "Tags").valueId!]).value![0]);

            StringAssert.Contains("names an entry of a Set, which can't be written in place", ProbeError(data, "HeldWrite"));
        }

        [Test]
        public void TargetThatDoesNotFitIsRefusedOnRead()
        {
            ProjectData data = Data();
            ((ArrayMemberValue)GRow(data, "A")).value = new[] { ((ObjectMemberValue)GRoot(data)).value!["Xs"] };

            StringAssert.Contains("but the reference declares int", ProbeError(data, "ReadA"));
        }

        [Test]
        public void ListenersFollowTheTargetAcrossARebind()
        {
            using NeoClient client = Client(Data());
            InvokeProbe(client, "Listen");

            InvokeProbe(client, "WriteCount");
            Assert.AreEqual(41, InvokeProbe(client, "Heard"), "a target write reaches the reference's listener");
            InvokeProbe(client, "RebindToOther");
            Assert.AreEqual(10, InvokeProbe(client, "Heard"), "a rebind fires with the new target's value");
            InvokeProbe(client, "BumpCount");
            Assert.AreEqual(10, InvokeProbe(client, "Heard"), "the old target no longer reaches it");
            InvokeProbe(client, "WriteOther");
            Assert.AreEqual(17, InvokeProbe(client, "Heard"));
        }

        [Test]
        public void SavedClosureKeepsItsCapturedReferenceAcrossAReload()
        {
            ProjectData data = Data();
            string saved;
            using (NeoClient client = Client(data))
            {
                InvokeProbe(client, "StoreHook");
                saved = client.SerializeSaveData();
            }
            using NeoClient reloaded = NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false, loadedSaveContent: saved);

            Assert.AreEqual(10, InvokeProbe(reloaded, "CallHook"), "the closure reads its captured reference and list of references");
        }

        [Test]
        public void SavedReferenceToAnAuthoredSessionRowSurvivesAReload()
        {
            ProjectData data = Data();
            string saved;
            using (var app = new Parity.TestProjectNeo(Client(data)))
            {
                InvokeProbe(app.Client, "SavedPinsAuthored");
                Parity.Probe.Total = 9;
                saved = app.Client.SerializeSaveData();
            }
            using var reloaded = new Parity.TestProjectNeo(
                NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false, loadedSaveContent: saved));

            Assert.AreEqual(2, Parity.Probe.SavedPin!.Value, "the pin names the authored row, which the new session resets");
        }

        [Test]
        public void SessionReferenceToASavedRowResetsWithTheSession()
        {
            ProjectData data = Data();
            string saved;
            using (NeoClient client = Client(data))
            {
                Assert.AreEqual(1, InvokeProbe(client, "PinSaved"));
                saved = client.SerializeSaveData();
            }
            using var reloaded = new Parity.TestProjectNeo(
                NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false, loadedSaveContent: saved));

            Assert.IsNull(Parity.Probe.Pin);
        }

        [Test]
        public void ReferenceToAnEmptySpriteReadsNull()
        {
            using NeoClient client = Client(Data());
            var empty = new Dictionary<string, object?> { ["fileId"] = "", ["sliceIndex"] = 0 };

            Assert.IsNull(NeoGeneratedTypesSupport.ReadReferenceValue<UnityEngine.Sprite>(client, empty));
        }

        [Test]
        public void ReferenceToAProjectedSpriteReadsThatSprite()
        {
            using NeoClient client = Client(Data());
            var texture = new UnityEngine.Texture2D(1, 1);
            var sprite = UnityEngine.Sprite.Create(texture, new UnityEngine.Rect(0, 0, 1, 1), UnityEngine.Vector2.zero);
            try
            {
                Assert.AreSame(sprite, NeoGeneratedTypesSupport.ReadReferenceValue<UnityEngine.Sprite>(client, sprite));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void GeneratedReferenceReadsWritesAndRebinds()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            Parity.Gauge g = Parity.Probe.G;

            Assert.AreEqual(3, g.A.Value);
            g.A.Value = 7;
            Assert.AreEqual(7, g.Count, "a write through the reference reaches its target");
            Assert.IsTrue(g.A.TrySet(8));
            Assert.AreEqual(8, g.Count);
            g.A = g.ReferenceTo(Parity.Gauge.Fields.Other);
            Assert.AreEqual(10, g.A.Value);
            Assert.AreEqual(g.ReferenceTo(Parity.Gauge.Fields.Other), g.A, "references to one slot are equal");
            Assert.AreEqual(g.A, g.RR.Value, "a nested reference reads as the reference it names");
        }

        [Test]
        public void MemoizedReferenceReadFollowsItsTarget()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            Parity.Gauge g = Parity.Probe.G;

            Assert.AreEqual(3, g.A.Value);
            Assert.AreEqual(3, g.A.Value, "a repeated read answers from the memo");
            g.Count = 9;
            Assert.AreEqual(9, g.A.Value, "a target write forgets the memoized read");
            g.A = g.ReferenceTo(Parity.Gauge.Fields.Other);
            Assert.AreEqual(10, g.A.Value, "a rebind reads its new target");
        }

        [Test]
        public void GeneratedReferenceRefusesAReadonlyTarget()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            Parity.Gauge g = Parity.Probe.G;

            NeoReference<int> fixedSlot = g.ReferenceTo(Parity.Gauge.Fields.Fixed);

            Assert.IsFalse(fixedSlot.TrySet(9));
            Assert.Catch<NSGetterRuntimeError>(() => fixedSlot.Value = 9);
            Assert.AreEqual(4, g.Fixed);
        }

        [Test]
        public void GeneratedReferenceRefusesAnAssetTarget()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            int stockN = Parity.Probe.Stock.N;
            NeoReference<int> stock = ((Parity.Tally)Parity.Probe.Stock).ReferenceTo(Parity.Tally.Fields.N);

            Assert.IsFalse(stock.TrySet(stockN + 1));
            Assert.Catch<NSGetterRuntimeError>(() => stock.Value = stockN + 1);
            Assert.AreEqual(stockN, Parity.Probe.Stock.N);
        }

        [Test]
        public void GeneratedRebindPastTheTargetsLifetimeThrows()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            NeoReference<int> sessionSlot = new Parity.Tally().ReferenceTo(Parity.Tally.Fields.N);

            Assert.Catch<NSGetterRuntimeError>(() => Parity.Probe.SavedPin = sessionSlot);
        }

        [Test]
        public void GeneratedReferenceCollectionsSelectTheirTargets()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            Parity.Gauge g = Parity.Probe.G;
            NeoReference<int> count = g.ReferenceTo(Parity.Gauge.Fields.Count);

            g.RefList.Add(count);
            g.RefMap["c"] = count;
            g.RefMap["c"].Value = 8;
            Assert.AreEqual(8, g.RefList[0].Value, "list and dictionary entries name the same slot");
            g.Picks.Add(count);
            Assert.IsTrue(g.Chosen.Contains(count), "a reference to the set reads the set");
            Assert.IsTrue(g.Chosen.TryWritable(out ICollection<NeoReference<int>> chosen));
            chosen.Remove(count);
            Assert.AreEqual(0, g.Picks.Count);
        }

        [Test]
        public void GeneratedCollectionReferenceWritesThroughTryWritable()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            Parity.Gauge g = Parity.Probe.G;

            g.ScoresRef = g.Scores;
            Assert.IsTrue(g.ScoresRef!.TryWritable(out NeoDictionary<int> scores));
            scores["b"] = 2;
            Assert.AreEqual(2, g.Scores["b"]);
        }

        [Test]
        public void GeneratedVariantReferenceWritesItsSlot()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            Parity.Gauge g = Parity.Probe.G;
            InvokeProbe(app.Client, "KindThrough");

            Assert.AreEqual(g.ReferenceTo(Parity.Gauge.Fields.Kind), g.KindRef);
            Assert.AreEqual(g.Kind!.ClassId, g.KindRef!.Value!.ClassId);
            g.KindRef.Value = null;
            Assert.IsNull(g.Kind);
        }

        /// <summary>Gives <c>Probe.G</c>'s Lookup a row that selects <paramref name="entryId"/>.</summary>
        private static void Select(ProjectData data, string lookup, string entryId)
        {
            string rowId = $"{lookup}-selection";
            ((ObjectMemberValue)GRoot(data)).value![lookup] = rowId;
            data.values[rowId] = new ArrayMemberValue
            {
                id = rowId,
                value = new[] { entryId },
            };
        }

        private static MemberValue GRoot(ProjectData data) =>
            data.values[MemberNamed<ClassMember>(data, "G").valueId!];

        private static MemberValue GRow(ProjectData data, string key) =>
            data.values[((ObjectMemberValue)GRoot(data)).value![key]];

        private static T MemberNamed<T>(ProjectData data, string name)
            where T : Member
        {
            return data.members.Values.OfType<T>().Single(member => member.name == name);
        }

        private static string ProbeError(ProjectData data, string probe)
        {
            using NeoClient client = Client(data);
            return Assert.Catch(() => InvokeProbe(client, probe)).ToString();
        }

        private static NeoClient Client(ProjectData data)
        {
            return NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
        }

        private static ProjectData Data()
        {
            return NeoTestExport.Read(File.ReadAllText(Path.Combine(PackageRoot, "reference-parity-export.json")));
        }

        private static IEnumerable<string> CaseNames(string table)
        {
            foreach (JProperty testCase in Fixture()[table]!.Children<JProperty>())
                yield return testCase.Name;
        }

        private static JToken Case(string table, string probe) => Fixture()[table]![probe]!;

        private static JObject Fixture() =>
            JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "reference-parity-fixture.json")));

        private static object? InvokeProbe(NeoClient client, string name)
        {
            Member function = client.members.Values.Single(member =>
                member is NSFunctionMember && member.name == name);
            return NeoMemberNSFunction.GetOrCreateStatic(client, function.id)
                .InvokeStatic(Array.Empty<object?>());
        }
    }
}
