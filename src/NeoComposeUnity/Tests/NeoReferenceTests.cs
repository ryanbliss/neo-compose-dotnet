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
    /// P112 references to any value: the Unity half of the parity gate. The
    /// cases and their source are a verbatim copy of neo-compose's
    /// <c>src/models/neoscript/reference-parity-fixture.json</c>; the export
    /// is that source's <c>neo export</c>, which neo-compose's
    /// <c>cli/src/commands/reference-parity.test.ts</c> writes when
    /// <c>NEO_REFERENCE_PARITY_EXPORT</c> names this package's copy. Tests
    /// that need a bound selection add it to <c>Probe.G</c>'s row, because
    /// entry ids derive from member ids that change on every export.
    /// </summary>
    public class NeoReferenceTests
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
