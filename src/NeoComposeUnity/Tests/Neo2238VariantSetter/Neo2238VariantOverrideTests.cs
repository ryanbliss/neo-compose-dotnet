#nullable enable
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Tests.VariantOverrides;
namespace NeoCompose.Tests
{
    public class Neo2238VariantOverrideTests
    {
        private NeoClient client = null!;
        private TestProjectNeo project = null!;
        [SetUp]
        public void Setup()
        {
            client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/Neo2238VariantSetter/variant-overrides.json")), assumeCurrentSchema: false);
            project = new TestProjectNeo(client);
        }
        [TearDown]
        public void Cleanup()
        {
            project.Dispose();
            client.Dispose();
        }
        [Test]
        public void RequiredAndOptionalReadsUseInheritedWrapper()
        {
            var item = Probe.Live;
            InventoryItemBase inherited = item;
            Assert.AreEqual("MailboxVariant", item.Placed!.Name);
            Assert.AreEqual(item.Placed.ClassId, inherited.Placed!.ClassId);
            Assert.AreEqual("MailboxVariant", Probe.Same.Placed!.Name);
            Assert.AreEqual("MailboxVariant", Probe.Deep.Placed!.Name);
            Assert.IsNull(Probe.Optional.Placed);
        }
        [Test]
        public void RequiredNullIsRejectedWithoutMutation()
        {
            var item = Probe.Live;
            Assert.Throws<ArgumentNullException>(() => item.Placed = null);
            Assert.Throws<ArgumentNullException>(() => Probe.Same.Placed = null);
            Assert.AreEqual("MailboxVariant", item.Placed!.Name);
        }
        [Test]
        public void WrongTargetIsRejectedWithoutMutation()
        {
            InventoryItemBase inherited = Probe.Live;
            Assert.Throws<InvalidOperationException>(() => inherited.Placed = Probe.BaseItem.Placed);
            Assert.AreEqual("MailboxVariant", inherited.Placed!.Name);
        }
        [TestCase("required")]
        [TestCase("optional")]
        [TestCase("deep")]
        public void RejectedBaseViewWritePreservesSaveAndSubscriptions(string kind)
        {
            InventoryItemBase item = kind switch
            {
                "optional" => Probe.Optional,
                "deep" => Probe.Deep,
                _ => Probe.Live,
            };
            var accepted = Probe.Live.Placed;
            var rejected = Probe.BaseItem.Placed;
            item.Placed = accepted;
            int changed = 0;
            using var subscription = item.OnChanged(InventoryItemBase.Fields.Placed,
                (NeoVariant<PlacedItem>? _, NeoChangeSource __) => changed++);
            string before = client.SerializeSaveData();
            long revision = client.WriteRevision;
            Assert.Throws<InvalidOperationException>(() => item.Placed = rejected);
            Assert.AreEqual(before, client.SerializeSaveData());
            Assert.AreEqual(revision, client.WriteRevision);
            Assert.AreEqual(0, changed);
            Assert.AreEqual(accepted!.ClassId, item.Placed!.ClassId);
            Assert.AreEqual(accepted.VariantId, item.Placed.VariantId);
        }

        [Test]
        public void OptionalNarrowedSlotAcceptsNullAndReportsIt()
        {
            var item = Probe.Optional;
            item.Placed = Probe.Live.Placed;
            int changed = 0;
            NeoVariant<PlacedItem>? latest = item.Placed;
            using var subscription = item.OnChanged(OptionalItem.Fields.Placed,
                (NeoVariant<PlacedItem>? next, NeoChangeSource _) =>
                {
                    latest = next;
                    changed++;
                });
            item.Placed = null;
            Assert.IsNull(item.Placed);
            Assert.IsNull(latest);
            Assert.AreEqual(1, changed);
            var rejected = Probe.BaseItem.Placed;
            string before = client.SerializeSaveData();
            Assert.Throws<InvalidOperationException>(() => item.Placed = rejected);
            Assert.AreEqual(before, client.SerializeSaveData());
            Assert.AreEqual(1, changed);
            Assert.IsNull(item.Placed);
        }

        [Test]
        public void SameTargetOverrideAcceptsBaseAndDescendantSelections()
        {
            var item = Probe.Same;
            item.Placed = Probe.BaseItem.Placed;
            Assert.AreEqual(Probe.BaseItem.Placed!.ClassId, item.Placed!.ClassId);
            item.Placed = Probe.Live.Placed;
            Assert.AreEqual(Probe.Live.Placed!.VariantId, item.Placed!.VariantId);
            string before = client.SerializeSaveData();
            Assert.Throws<ArgumentNullException>(() => item.Placed = null);
            Assert.AreEqual(before, client.SerializeSaveData());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RawAndEnvelopedSelectionsRejectUnrelatedTargetsAtomically(bool envelope)
        {
            var item = Probe.Live;
            string unrelatedId = client.ProjectDataForRuntime.classes.Values.Single(c => c.name == "UnrelatedItem").id;
            var selection = new VariantRefValue { classId = unrelatedId, variantId = null };
            var payload = NeoValueWritePayload.FromValue(envelope ? new NeoValuePayload(selection) : selection);
            var node = item.WritableBackingNode;
            string before = client.SerializeSaveData();
            long revision = client.WriteRevision;
            Assert.Throws<InvalidOperationException>(() => NeoGeneratedTypesSupport.SetValue(node, "Placed", payload));
            Assert.AreEqual(before, client.SerializeSaveData());
            Assert.AreEqual(revision, client.WriteRevision);
            Assert.AreEqual("MailboxVariant", item.Placed!.Name);
        }

        [Test]
        public void ChangedFieldReceivesProjectedWrapper()
        {
            var item = Probe.Live;
            NeoVariant<PlacedItem>? changed = null;
            using var subscription = item.OnChanged(MailboxItem.Fields.Placed,
                (NeoVariant<PlacedItem>? value, NeoChangeSource _) => changed = value);
            item.Placed = Probe.Other.Placed;
            Assert.AreEqual("Base", item.Placed!.Name);
            Assert.AreEqual("Base", changed!.Name);
        }
    }
}
