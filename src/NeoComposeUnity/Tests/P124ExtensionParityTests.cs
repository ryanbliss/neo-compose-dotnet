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
using Newtonsoft.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class P124ExtensionParityTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";
        private static JObject Fixture() => JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "extension-members-parity-fixture.json")));
        public static IEnumerable<string> Returns() => Fixture()["returns"]!.Children<JProperty>().Concat(Fixture()["deferredReturns"]!.Children<JProperty>()).Select(property => property.Name);
        public static IEnumerable<string> Refuses() => Fixture()["refuses"]!.Children<JProperty>().Select(property => property.Name);
        private static NeoClient Client()
        {
            var data = NeoTestExport.Read(File.ReadAllText(Path.Combine(PackageRoot, "extension-parity-export.json")));
            return NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false,
                localization: NeoLocalization.LoadMain(data.localization, new LocaleSource()));
        }
        private sealed class LocaleSource : INeoLocalizationLocaleFileSource
        {
            public bool TryLoadResourcesLocale(ProjectLocalizationExport localization, string locale, out ProjectLocalizationLocaleFile? file)
            {
                file = JsonConvert.DeserializeObject<ProjectLocalizationLocaleFile>(File.ReadAllText(Path.Combine(PackageRoot, "extension-parity-export.locale.json")));
                return true;
            }
            public Task<ProjectLocalizationLocaleFile?> LoadStreamingAssetsLocaleAsync(ProjectLocalizationExport localization, string locale, string streamingAssetsRelativePath)
                => Task.FromResult<ProjectLocalizationLocaleFile?>(null);
        }
        private static object? Invoke(NeoClient client, string name)
        {
            Member member = client.members.Values.Single(member => member is NSFunctionMember && member.name == name);
            var function = NeoMemberNSFunction.GetOrCreateStatic(client, member.id);
            return member is NSFunctionMember { Dispatch: NeoFunctionDispatchKind.Asynchronous }
                ? function.InvokeStaticAsync(Array.Empty<object?>()).GetAwaiter().GetResult()
                : function.InvokeStatic(Array.Empty<object?>());
        }
        [TestCaseSource(nameof(Returns))]
        public void ExtensionReturnsFromExportedSource(string name)
        {
            using NeoClient client = Client();
            object? value = Invoke(client, name);
            JToken actual = value is null ? JValue.CreateNull() : JToken.FromObject(value);
            JObject fixture = Fixture();
            JToken expected = fixture["returns"]![name] ?? fixture["deferredReturns"]![name]!;
            if (expected.Type == JTokenType.Integer || expected.Type == JTokenType.Float)
                Assert.That(actual.Value<double>(), Is.EqualTo(expected.Value<double>()), name);
            else
                Assert.That(JToken.DeepEquals(expected, actual), Is.True, $"{name}: {actual}");
        }
        [Test]
        public void SequenceExtensionsAcceptClrReceiversWithoutConsumingDuringValidation()
        {
            using NeoClient client = Client();
            string total = client.members.Values.Single(member => member.name == "SequenceTotal").id;
            string count = client.members.Values.Single(member => member.name == "SequenceCount").id;
            string remaining = client.members.Values.Single(member => member.name == "RemainingValues").id;
            var integer = new PrimitiveTypeInfo { type = MemberKind.Int, required = true };
            var values = new List<int> { 2, 3, 4 };
            Assert.That(NeoExtensionRuntime.Invoke(client, total, values, Array.Empty<object?>()), Is.EqualTo(9));
            Assert.That(NeoExtensionRuntime.Get(client, count, values, new[] { integer }), Is.EqualTo(3));
            using IEnumerator<int> cursor = values.GetEnumerator();
            Assert.That(cursor.MoveNext(), Is.True);
            object? result = NeoExtensionRuntime.Invoke(client, remaining, cursor, Array.Empty<object?>(), new[] { integer });
            Assert.That(result, Is.EquivalentTo(new object[] { 3, 4 }));
            Assert.That(cursor.MoveNext(), Is.False);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception,
                new System.Text.RegularExpressions.Regex("InvalidOperationException: Sequence element has runtime type 'String', expected Int\\."));
            Assert.That(() => NeoExtensionRuntime.Invoke(client, total, new List<string> { "bad" }, Array.Empty<object?>()), Throws.Exception);
        }

        [Test]
        public void ExtensionReceiverUsesItsOwningClient()
        {
            using NeoClient first = Client();
            using NeoClient second = Client();
            string receiverMember = first.members.Values.Single(member => member.name == "Frozen").id;
            string property = first.members.Values.Single(member => member is NSPropertyMember { extension: not null } && member.name == "FullName").id;
            using var firstNode = new NeoMemberClass(first, receiverMember, null);
            var firstReceiver = new ReceiverView(first, firstNode);
            using var secondNode = new NeoMemberClass(second, receiverMember, null);
            var secondReceiver = new ReceiverView(second, secondNode);
            Assert.That(NeoExtensionRuntime.ClientOf(firstReceiver), Is.SameAs(first));
            Assert.That(NeoExtensionRuntime.Get(first, property, firstReceiver), Is.EqualTo("Ada Lovelace"));
            Assert.That(NeoExtensionRuntime.Get(second, property, secondReceiver), Is.EqualTo("Ada Lovelace"));
            StringAssert.Contains("different client", Assert.Throws<NSGetterRuntimeError>(() => NeoExtensionRuntime.Get(second, property, firstReceiver))!.Message);
        }

        [Test]
        public void SequenceExtensionWrappersRetainTheirOwningClient()
        {
            using NeoClient first = Client();
            using NeoClient second = Client();
            string relayed = first.members.Values.Single(member => member.name == "Relayed").id;
            string total = first.members.Values.Single(member => member.name == "SequenceTotal").id;
            string remaining = first.members.Values.Single(member => member.name == "RemainingValues").id;
            var integer = new PrimitiveTypeInfo { type = MemberKind.Int, required = true };
            foreach (NeoClient owner in new[] { first, second })
            {
                object? sequence = NeoExtensionRuntime.Get(owner, relayed, new List<int> { 2, 3, 4 }, new[] { integer });
                Assert.That(NeoExtensionRuntime.ClientOf(sequence), Is.SameAs(owner));
                IEnumerable<int> view = NeoSequences.ReadEnumerable(owner, sequence, integer, value => Convert.ToInt32(value))!;
                Assert.That(NeoExtensionRuntime.ClientOf(view), Is.SameAs(owner));
                Assert.That(NeoExtensionRuntime.Invoke(NeoExtensionRuntime.ClientOf(view)!, total, view, Array.Empty<object?>()), Is.EqualTo(9));
                using IEnumerator<int> cursor = view.GetEnumerator();
                Assert.That(NeoExtensionRuntime.ClientOf(cursor), Is.SameAs(owner));
                Assert.That(cursor.MoveNext(), Is.True);
                Assert.That(NeoExtensionRuntime.Invoke(owner, remaining, cursor, Array.Empty<object?>(), new[] { integer }),
                    Is.EquivalentTo(new object[] { 3, 4 }));
                NeoClient other = ReferenceEquals(owner, first) ? second : first;
                StringAssert.Contains("different client",
                    Assert.Throws<NSGetterRuntimeError>(() => NeoExtensionRuntime.Invoke(other, total, view, Array.Empty<object?>()))!.Message);
            }
        }

        private sealed class ReceiverView : NeoGeneratedClassValue
        {
            internal ReceiverView(NeoClient client, NeoMemberClass node)
                : base(client, node, node.ClassId, true, node.ownership)
            {
            }
        }

        [TestCaseSource(nameof(Refuses))]
        public void ExtensionRefusesFromExportedSource(string name)
        {
            using NeoClient client = Client();
            StringAssert.Contains(Fixture()["refuses"]![name]!.Value<string>(), Assert.Catch(() => Invoke(client, name))!.Message);
        }
    }
}
