// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System.IO;
using NeoCompose.Runtime;
using NUnit.Framework;
using Fixture = NeoCompose.Tests.ClassRootReference420;

namespace NeoCompose.Tests
{
    public class NeoReferenceClassRootTests
    {
        [Test]
        public void ConstructorReferenceReadsUnownedClassThroughBothGeneratedContracts()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(
                "Packages/com.ryanbliss.neocompose/Tests/ClassRootReference420/project.json")), assumeCurrentSchema: false);
            using var app = new Fixture.TestProjectNeo(client);
            using var item = new Fixture.Item();
            using var holder = new Fixture.RefHolder(item);
            NeoMemberLookup lookup = holder.BackingNode.Get<NeoMemberLookup>("Value");
            var writable = NeoGeneratedTypesSupport.ReadReference<Fixture.Item>(client, lookup)!;
            var readOnly = NeoGeneratedTypesSupport.ReadReference<Fixture.IReadOnlyItem>(client, lookup)!;

            Assert.AreEqual(item.valueId, writable.TargetId);
            Assert.AreEqual(writable.TargetId, readOnly.TargetId);
            Assert.AreEqual(7, writable.Value.Number);
            Assert.AreEqual(7, readOnly.Value.Number);
            writable.Value.Number = 9;
            Assert.AreEqual(9, item.Number);
            Assert.AreEqual(9, readOnly.Value.Number);
        }

        [Test]
        public void ConstructorReferenceStillRejectsDeletedClassRoot()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(
                "Packages/com.ryanbliss.neocompose/Tests/ClassRootReference420/project.json")), assumeCurrentSchema: false);
            using var app = new Fixture.TestProjectNeo(client);
            using var item = new Fixture.Item();
            using var holder = new Fixture.RefHolder(item);
            var reference = NeoGeneratedTypesSupport.ReadReference<Fixture.Item>(client, holder.BackingNode.Get<NeoMemberLookup>("Value"))!;
            Assert.AreEqual(7, reference.Value.Number);

            client.RemoveWritableValueAndDescendants(NeoValueOwnership.Session, item.valueId!);

            StringAssert.Contains("no longer exists", Assert.Catch(() => _ = reference.Value)!.Message);
        }

        [Test]
        public void ClassRootReferenceStillRejectsIncompatibleGeneratedContract()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(
                "Packages/com.ryanbliss.neocompose/Tests/ClassRootReference420/project.json")), assumeCurrentSchema: false);
            using var app = new Fixture.TestProjectNeo(client);
            using var item = new Fixture.Item();
            using var holder = new Fixture.RefHolder(item);
            var reference = NeoGeneratedTypesSupport.ReadReference<Fixture.RefHolder>(client, holder.BackingNode.Get<NeoMemberLookup>("Value"))!;

            Assert.Catch(() => _ = reference.Value);
            Assert.AreEqual(7, item.Number);
            Assert.AreEqual(item.valueId, reference.TargetId);
        }
    }
}
