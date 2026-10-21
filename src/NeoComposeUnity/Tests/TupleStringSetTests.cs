// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class TupleStringSetTests
    {
        private const string FixtureFile = "tuple-string-set-parity.json";

        public static IEnumerable<string> Cases() => NeoSourceParityFixture.Cases(FixtureFile);

        [TestCaseSource(nameof(Cases))]
        public void TupleMembershipMatchesCli(string name) => NeoSourceParityFixture.AssertTrueCase(FixtureFile, name);

        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public void StringKeysAcrossCallsAndLocaleChanges(bool save, bool localized, bool collide)
        {
            using NeoClient seed = NeoSourceParityFixture.CreateClient(FixtureFile);
            var localization = NeoLocalization.CreateEmpty(new ProjectLocalizationExport
            {
                mainLocale = "en-US",
                supportedLocales = new[] { new ProjectLocalizationLocale { locale = "en-US" }, new ProjectLocalizationLocale { locale = "es-MX" } },
            });
            void Load(string locale, string text) => Assert.IsTrue(localization.TryAddLoadedLocale(new ProjectLocalizationLocaleFile
            {
                schemaVersion = 1,
                locale = locale,
                values = new Dictionary<string, string?> { ["greeting"] = text, ["second-greeting"] = locale == "es-MX" ? text : "world" },
            }));
            Load("en-US", "hello");
            Load("es-MX", "hola");
            localization.SetLocale("en-US");
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(seed.Synchronizer.Schema, localization: localization);
            string factory = client.members.Values.Single(m => m.name == "CreateBag").id;
            string id = ((INeoValueReference)NeoMemberNSFunction.GetOrCreateStatic(client, factory).InvokeStatic()!).valueId!;
            NeoValueOwnership ownership = save ? NeoValueOwnership.Save : NeoValueOwnership.Session;
            if (save)
                client.ImportValueReference(ownership, id, out HashSet<string>? _);
            Assert.IsTrue(client.TryGetValue(ownership, id, out ObjectMemberValue? owner));
            string setId = owner!.value!["Pairs"];
            var setMember = (SetMember)client.members.Values.Single(m => m.name == "Pairs");
            Assert.IsTrue(client.TryGetValue(ownership, setId, out ArrayMemberValue? setRow));
            string entryId = setRow!.value!.Single();
            Assert.IsTrue(client.TryGetValue(ownership, entryId, out ArrayMemberValue? pair));
            if (localized)
            {
                Assert.IsTrue(client.TryGetValue(ownership, pair!.value![1], out StringMemberValue? original));
                var textRow = (StringMemberValue)client.CloneRowForWrite(original!);
                textRow.value = "greeting";
                textRow.neoLocalizationMode = null;
                client.SetWritableValue(ownership, textRow);
            }
            if (collide)
            {
                Assert.AreEqual(true, Invoke("AddPair", "world"));
                Assert.IsTrue(client.TryGetValue(ownership, setId, out ArrayMemberValue? twoEntries));
                Assert.IsTrue(client.TryGetValue(ownership, twoEntries!.value![1], out ArrayMemberValue? secondPair));
                Assert.IsTrue(client.TryGetValue(ownership, secondPair!.value![1], out StringMemberValue? original));
                var textRow = (StringMemberValue)client.CloneRowForWrite(original!);
                textRow.value = "second-greeting";
                textRow.neoLocalizationMode = null;
                client.SetWritableValue(ownership, textRow);
            }
            using var node = new NeoMemberListWritable(client, setMember, setId, ownership);
            var tupleMember = (TupleMember)node.EntryMember;
            var context = new NSGetterEvaluator.Context(client, null, null);
            string Key(string text) => NeoSetEntryKey.OfRuntimeTuple(tupleMember, new RuntimeTuple(new object?[] { 1d, text }, new TupleTypeInfo
            {
                type = MemberKind.Tuple,
                elements = new[]
                {
                    new TupleElementTypeInfo { typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true } },
                    new TupleElementTypeInfo { typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true } },
                },
            }), context);
            object? Invoke(string name, string text)
            {
                string method = client.members.Values.Single(m => m.name == name).id;
                using var function = new NeoMemberNSFunction(client, method, null, ownership);
                return function.Invoke(id, new object?[] { text });
            }
            Assert.AreEqual(collide ? 2 : 1, node.Count);
            Assert.AreEqual(Key("hello"), NeoSetEntryKey.Of(tupleMember, pair!, client), "Stored and runtime tuple keys agree");
            Assert.AreEqual(entryId, node.FindSetEntry(Key("hello")));
            Assert.AreEqual(true, Invoke("Has", "hello"));
            localization.SetLocale("es-MX");
            string expected = localized ? "hola" : "hello";
            Assert.AreEqual(entryId, node.FindSetEntry(Key(expected)));
            Assert.IsNull(node.FindSetEntry(Key(localized ? "hello" : "hola")));
            Assert.AreEqual(true, Invoke("Has", expected));
            Load("es-MX", "buenas");
            expected = localized ? "buenas" : "hello";
            Assert.AreEqual(entryId, node.FindSetEntry(Key(expected)));
            Assert.AreEqual(true, Invoke("AddPair", expected));
            Assert.IsTrue(client.TryGetValue(ownership, setId, out ArrayMemberValue? afterAdd));
            Assert.That(afterAdd!.value, Has.Length.EqualTo(collide ? 2 : 1), "A probe equal to the translated tuple must deduplicate.");
            Assert.AreEqual(!collide, Invoke("RemovePair", expected), "Removing one translated collision must leave the other discoverable.");
            Assert.IsTrue(client.TryGetValue(ownership, setId, out ArrayMemberValue? afterRemove));
            Assert.That(afterRemove!.value, Has.Length.EqualTo(collide ? 1 : 0));
            Assert.AreEqual(collide, Invoke("Has", expected));
            Assert.AreEqual(collide, node.FindSetEntry(Key(expected)) is not null);
        }
    }
}
