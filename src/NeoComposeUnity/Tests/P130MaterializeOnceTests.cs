// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class P75VirtualInstanceValueTests
    {
        private static ProjectData P130CaptureProject()
        {
            var data = BuildProjectData();
            data.classes["save-root-class"].schema["Input"] = "replay-input";
            data.members["replay-input"] = new IntMember
            {
                id = "replay-input",
                name = "Input",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { value = 3 },
            };
            data.classes["thing-class"].schema["Captured"] = "captured-count";
            data.members["captured-count"] = new IntMember
            {
                id = "captured-count",
                name = "Captured",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                Storage = NeoMemberStorage.Save,
                defaultValue = new NumberMemberValueBase { init = Init(IntTypeInfo(), SavePointer("Input")) },
            };
            return data;
        }

        [Test]
        public void P130CapturedMutableDefaultIsPinnedAndLiteralSiblingIsSparse()
        {
            var data = P130CaptureProject();
            string saved;
            using (var client = NeoTestSaveStack.ClientFromSchema(data))
            {
                var thing = client.save.Get<NeoMemberClassWritable>("Thing");
                var captured = thing.Get<NeoMemberIntWritable>("Captured");
                var literal = thing.Get<NeoMemberIntWritable>("Count");
                Assert.AreEqual(3d, captured.value!.value);
                string capturedId = captured.value.id;
                string literalId = literal.value!.id;
                int count = client.VirtualConstructionCount;
                client.save.Get<NeoMemberIntWritable>("Input").Set(9);
                Assert.AreEqual(count, client.VirtualConstructionCount);
                Assert.AreEqual(3d, captured.value!.value);
                saved = client.SerializeSaveData();
                var values = (JObject)JObject.Parse(saved)["values"]!;
                Assert.IsNotNull(values[capturedId]);
                Assert.IsNull(values[literalId]);
            }
            using var reloaded = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            Assert.AreEqual(3d, reloaded.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberIntWritable>("Captured").value!.value);
        }

        [Test]
        public void P130LiteralDefaultsDoNotPinRows()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var thing = client.save.Get<NeoMemberClassWritable>("Thing");
            string rootId = thing.value!.id;
            string countId = thing.Get<NeoMemberIntWritable>("Count").value!.id;
            var values = (JObject)JObject.Parse(client.SerializeSaveData())["values"]!;
            Assert.IsNull(values[rootId]);
            Assert.IsNull(values[countId]);
        }

        [Test]
        public void P130RestoreAuthoredRevealsMaterializedCapture()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(P130CaptureProject());
            var thing = client.save.Get<NeoMemberClassWritable>("Thing");
            var captured = thing.Get<NeoMemberIntWritable>("Captured");
            Assert.AreEqual(3d, captured.value!.value);
            int count = client.VirtualConstructionCount;
            client.save.Get<NeoMemberIntWritable>("Input").Set(9);
            string authored = client.SerializeSaveData();
            captured.Set(12);
            client.ApplyExternalSaveContent(authored);
            Assert.AreEqual(3d, thing.Get<NeoMemberIntWritable>("Captured").value!.value);
            Assert.AreEqual(count, client.VirtualConstructionCount);
        }

        [Test]
        public void P130ExternalArgumentChangeOnlyRematerializesItsRoot()
        {
            var data = BuildLiveConstructorArgumentProjectData();
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var thing = client.save.Get<NeoMemberClassWritable>("Thing");
            var other = client.save.Get<NeoMemberClassWritable>("Other");
            Assert.AreEqual("from constructor argument", PayloadName(thing));
            Assert.AreEqual("from constructor argument", PayloadName(other));
            int count = client.VirtualConstructionCount;
            var incoming = JObject.Parse(client.SerializeSaveData());
            ReplacePayload(incoming, "constructor-payload", "changed");
            client.ApplyExternalSaveContent(incoming.ToString());
            Assert.AreEqual(count, client.VirtualConstructionCount);
            incoming = JObject.Parse(client.SerializeSaveData());
            var retargeted = ObjectValue("thing-instance", "thing-class");
            retargeted.instanceConstructorId = "thing-ctor";
            retargeted.constructorArgs = new Dictionary<string, JToken?> { ["__arg_0__"] = "alternate-payload" };
            retargeted.genericBindings = new Dictionary<string, string>(((ObjectMemberValue)data.values["thing-instance"]).genericBindings!);
            ((JObject)incoming["values"]!)[retargeted.id] = JObject.FromObject(retargeted);
            client.ApplyExternalSaveContent(incoming.ToString());
            Assert.AreEqual(count + 1, client.VirtualConstructionCount);
            Assert.AreEqual("alternate", PayloadName(thing));
            Assert.AreEqual("from constructor argument", PayloadName(other));
        }

        [Test]
        public void P130VariantSwitchRestoresConstructionWithoutReconstructingReceiver()
        {
            var data = BuildConstructedUnorderedChildrenProjectData();
            AddThingVariant(data);
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var thing = client.save.Get<NeoMemberClassWritable>("Thing");
            var receiver = new SparseThingValue(client, thing);
            var baseline = NeoGeneratedTypesSupport.ResolveBaseVariant<SparseThingValue>(client, "thing-class");
            var other = client.GetOrCreateVariant<SparseThingValue>("thing-variant");
            var entry = Entry(thing);
            string label = entry.Get<NeoMemberStringWritable>("Label").value!.value!;
            int count = client.VirtualConstructionCount;
            NeoGeneratedTypesSupport.ApplyVariant(receiver, baseline);
            NeoGeneratedTypesSupport.ApplyVariant(receiver, other);
            NeoGeneratedTypesSupport.ApplyVariant(receiver, baseline);
            Assert.AreEqual(count, client.VirtualConstructionCount);
            Assert.AreEqual(label, Entry(thing).Get<NeoMemberStringWritable>("Label").value!.value);
        }

        [Test]
        public void P130NewInstanceRunsConstructor()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildConstructedUnorderedChildrenProjectData());
            using var instance = NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(client, "thing-class", "thing-ctor",
                Array.Empty<NeoDeclaredConstructorArgument>());
            // The constructor body builds the entry; no materialized graph supplies it.
            Assert.AreEqual("Sprite", Entry(instance).Get<NeoMemberStringWritable>("Name").value!.value);
        }
    }
}
