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

namespace NeoCompose.Tests
{
    /// <summary>Runs the same variant selector cases as the CLI source fixture.</summary>
    public class VariantSelectorTests
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

            Assert.AreEqual(Fixture()["returns"]![probe]!.ToObject<object>(), InvokeProbe(client, probe));
        }

        [TestCase("Method")]
        [TestCase("Lambda")]
        [TestCase("Wrapper")]
        public void SwitchingAnExistingVariantUpdatesItsConstructedChild(string initialName)
        {
            using NeoClient client = Client();
            var initial = client.variants.Values.Single(variant => variant.name == initialName);
            var higher = client.variants.Values.Single(variant => variant.name == "Higher");
            var node = NeoVariantSupport.InitializeNode(client, initial.classId, initial);
            string rootId = node.value!.id;
            string childId = ReadChild().value!.id;
            Assert.That(ReadRank(), Is.EqualTo(8));
            NeoVariantSupport.ApplyToNode(client, higher, node, node.ownership);
            Assert.That(ReadRank(), Is.EqualTo(12));
            Assert.That(node.value!.id, Is.EqualTo(rootId));
            Assert.That(ReadChild().value!.id, Is.EqualTo(childId));
            NeoVariantSupport.ApplyToNode(client, initial, node, node.ownership);
            Assert.That(ReadRank(), Is.EqualTo(8));

            object? ReadRank()
            {
                Member function = client.members.Values.Single(member => member.name == "ReadRank");
                return NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(new TestReference { valueId = node.value!.id });
            }

            NeoMemberClass ReadChild()
            {
                var child = (NeoMemberClass)node.Get<NeoMemberList>("Children").First();
                return child;
            }
        }

        [Test]
        public void IncomingAnswersPreserveIndependentRecipesPinsAndReload()
        {
            string saved;
            using (NeoClient client = Client())
            {
                var initial = client.variants.Values.Single(variant => variant.name == "Method");
                var higher = client.variants.Values.Single(variant => variant.name == "Higher");
                var node = NeoVariantSupport.InitializeNode(client, initial.classId, initial);
                var reference = new TestReference { valueId = node.value!.id };
                InvokeProbe(client, "ReplaceChild", reference, 77);
                NeoVariantSupport.ApplyToNode(client, higher, node, node.ownership);
                Assert.That(InvokeProbe(client, "ReadRank", reference), Is.EqualTo(12));
                Assert.That(InvokeProbe(client, "ReadSalt", reference), Is.EqualTo(77));
                var child = (NeoMemberClass)node.Get<NeoMemberList>("Children").First();
                Assert.That(client.TryGetVirtualClassChildValueId(child.value!.id, "Rank", out string? rankId), Is.True);
                string rankMemberId = client.classes[child.value.classId!].schema["Rank"];
                var retainedRank = new NeoMemberIntWritable(client, rankMemberId, rankId, child.ownership);
                retainedRank.Set(99);
                InvokeProbe(client, "Offset", 2);
                Assert.That(InvokeProbe(client, "ReadRank", reference), Is.EqualTo(99));
                Assert.That(retainedRank.value!.value, Is.EqualTo(99));
                Assert.That(InvokeProbe(client, "ReadSalt", reference), Is.EqualTo(77));
                NeoVariantSupport.ApplyToNode(client, initial, node, node.ownership);
                Assert.That(InvokeProbe(client, "ReadRank", reference), Is.EqualTo(8));
                Assert.That(InvokeProbe(client, "ReadSalt", reference), Is.EqualTo(77));
                InvokeProbe(client, "Store", reference);
                InvokeProbe(client, "RaiseSaved");
                Assert.That(InvokeProbe(client, "ReadSavedRank"), Is.EqualTo(12));
                saved = client.SerializeSaveData();
            }
            using NeoClient reopened = Client(saved);
            Assert.That(InvokeProbe(reopened, "ReadSavedRank"), Is.EqualTo(12));
            Assert.That(InvokeProbe(reopened, "ReadSavedSalt"), Is.EqualTo(77));
            InvokeProbe(reopened, "Offset", 3);
            Assert.That(InvokeProbe(reopened, "ReadSavedRank"), Is.EqualTo(12));
            Assert.That(InvokeProbe(reopened, "ReadSavedSalt"), Is.EqualTo(77));
        }

        [Test]
        public void RemovingAnAnswerAndReplacingAChildRetiresOldComposition()
        {
            using NeoClient client = Client();
            var initial = client.variants.Values.Single(variant => variant.name == "Method");
            var higher = client.variants.Values.Single(variant => variant.name == "Higher");
            var plain = client.variants.Values.Single(variant => variant.name == "Plain");
            var node = NeoVariantSupport.InitializeNode(client, plain.classId, plain);
            var reference = new TestReference { valueId = node.value!.id };
            InvokeProbe(client, "ReplaceChild", reference, 77);
            NeoVariantSupport.ApplyToNode(client, higher, node, node.ownership);
            Assert.That(InvokeProbe(client, "ReadRank", reference), Is.EqualTo(12));
            NeoVariantSupport.ApplyToNode(client, plain, node, node.ownership);
            Assert.That(InvokeProbe(client, "ReadRank", reference), Is.EqualTo(0));
            Assert.That(InvokeProbe(client, "ReadSalt", reference), Is.EqualTo(77));
            NeoVariantSupport.ApplyToNode(client, higher, node, node.ownership);
            InvokeProbe(client, "ReplaceChild", reference, 91);
            // Replacing the child is an instance write. The incoming answer
            // takes precedence again at the next answering transition.
            Assert.That(InvokeProbe(client, "ReadRank", reference), Is.EqualTo(0));
            Assert.That(InvokeProbe(client, "ReadSalt", reference), Is.EqualTo(91));
            NeoVariantSupport.ApplyToNode(client, plain, node, node.ownership);
            NeoVariantSupport.ApplyToNode(client, higher, node, node.ownership);
            Assert.That(InvokeProbe(client, "ReadRank", reference), Is.EqualTo(12));
        }

        [Test]
        public void NestedStructuredAnswersPreserveIndependentComponents()
        {
            using NeoClient client = Client();
            var initial = client.variants.Values.Single(variant => variant.name == "Method");
            var detailed = client.variants.Values.Single(variant => variant.name == "Detailed");
            var node = NeoVariantSupport.InitializeNode(client, initial.classId, initial);
            var reference = new TestReference { valueId = node.value!.id };
            InvokeProbe(client, "ReplaceChild", reference, 77);
            NeoVariantSupport.ApplyToNode(client, detailed, node, node.ownership);
            var child = (NeoMemberClass)node.Get<NeoMemberList>("Children").First();
            var detail = child.Get<NeoMemberClass>("Detail");
            var point = detail.Get<NeoMemberVector2>("Point");
            Assert.That(point.value!.value!.x, Is.EqualTo(12));
            Assert.That(point.value!.value!.y, Is.EqualTo(77));
        }

        [Test]
        public void IncomingAnswersDoNotFreezeTheChildsOwnVariant()
        {
            using NeoClient client = Client();
            var plain = client.variants.Values.Single(variant => variant.name == "Plain");
            var higher = client.variants.Values.Single(variant => variant.name == "Higher");
            var node = NeoVariantSupport.InitializeNode(client, plain.classId, plain);
            var reference = new TestReference { valueId = node.value!.id };
            InvokeProbe(client, "ReplaceWithOwnVariant", reference);
            Assert.That(InvokeProbe(client, "ReadRank", reference), Is.EqualTo(6));
            NeoVariantSupport.ApplyToNode(client, higher, node, node.ownership);
            Assert.That(InvokeProbe(client, "ReadRank", reference), Is.EqualTo(12));
            NeoVariantSupport.ApplyToNode(client, plain, node, node.ownership);
            Assert.That(InvokeProbe(client, "ReadRank", reference), Is.EqualTo(6));
        }

        [Test]
        public void IncomingAnswersReachTupleElements()
        {
            using NeoClient client = Client();
            var plain = client.variants.Values.Single(variant => variant.name == "Plain");
            var tuple = client.variants.Values.Single(variant => variant.name == "Tuple");
            var node = NeoVariantSupport.InitializeNode(client, plain.classId, plain);
            var reference = new TestReference { valueId = node.value!.id };
            InvokeProbe(client, "ReplaceChild", reference, 77);
            Assert.That(InvokeProbe(client, "ReadPairRank", reference), Is.EqualTo(77));
            NeoVariantSupport.ApplyToNode(client, tuple, node, node.ownership);
            Assert.That(InvokeProbe(client, "ReadPairRank", reference), Is.EqualTo(12));
            NeoVariantSupport.ApplyToNode(client, plain, node, node.ownership);
            Assert.That(InvokeProbe(client, "ReadPairRank", reference), Is.EqualTo(77));
        }

        private sealed class TestReference : INeoValueReference
        {
            public string? valueId
            {
                get; set;
            }
        }

        private static NeoClient Client(string? saved = null)
        {
            ProjectData data = NeoTestExport.Read(File.ReadAllText(Path.Combine(PackageRoot, "variant-selectors-export.json")));
            NeoClient client = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved, assumeCurrentSchema: false);
            // The fixture now uses TrySpawn, which resolves class views through
            // the same registration normally supplied by the generated facade.
            client.RegisterGeneratedClassFactories(
                client.classes.Keys.ToDictionary(id => id,
                    _ => (NeoGeneratedTypesSupport.ReadOnlyClassFactory)((owner, node) => new TestClassView(owner, node, true))),
                client.classes.Keys.ToDictionary(id => id,
                    _ => (NeoGeneratedTypesSupport.WritableClassFactory)((owner, node) => new TestClassView(owner, node, false))));
            return client;
        }

        private sealed class TestClassView : NeoGeneratedClassValue
        {
            internal TestClassView(NeoClient client, NeoMemberClass node, bool readOnly)
                : base(client, node, node.ClassId, readOnly) { }
        }

        private static JObject Fixture() =>
            JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "variant-selectors-fixture.json")));

        private static object? InvokeProbe(NeoClient client, string name, params object?[] args)
        {
            Member function = client.members.Values.Single(member =>
                member is NSFunctionMember && member.name == name);
            var context = new NSGetterEvaluator.Context(client, null, null, valueOwnership: NeoValueOwnership.Session);
            return NeoNSFunctionRuntime.InvokeImmediate(client, function.id, null, args, context);
        }
    }
}
