// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using NUnit.Framework;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.NeoScript;
namespace NeoCompose.Tests
{
    public class NeoCollectionMutationParityTests
    {
        [Test]
        public void TerminalRemovalReclaimsUnreturnedTemporaryObjects()
        {
            using var client = NeoSourceParityFixture.CreateClient("collection-mutation-parity.json");
            var function = client.members.Values.Single(x => x.name == "NoEscape");
            int before = client.sessionValues.Count;
            for (int iteration = 0; iteration < 3; iteration++)
            {
                Assert.That(NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(), Is.True);
                Assert.That(client.sessionValues.Count, Is.EqualTo(before), "An unreturned temporary must be reclaimed at function exit.");
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RemovedArgumentFromEarlierInvocationRemainsUsable(bool array, bool materializeBeforeCall)
        {
            using var client = NeoSourceParityFixture.CreateClient("collection-mutation-parity.json");
            var create = client.members.Values.Single(x => x.name == (array ? "CreateItems" : "CreateItem"));
            var accept = client.members.Values.Single(x => x.name == (array ? "AcceptItems" : "AcceptItem"));
            var value = NeoMemberNSFunction.GetOrCreateStatic(client, create.id).InvokeStatic();
            var item = array ? ((object?[])value!)[0] : value;
            Assert.That(item, Is.TypeOf<NeoScriptObject>());
            var detached = (NeoScriptObject)item!;
            Assert.That(detached.attachedId, Is.Null);
            if (materializeBeforeCall)
                Assert.That(detached.valueId, Is.Not.Null);
            Assert.That(NeoMemberNSFunction.GetOrCreateStatic(client, accept.id).InvokeStatic(value), Is.True);
            Assert.That(client.TryGetValue(NeoValueOwnership.Session, detached.attachedId!,
                out NeoCompose.Runtime.Json.ObjectMemberValue? retained), Is.True,
                "The caller still owns this returned object after the second invocation exits.");
            Assert.That(NeoMemberNSFunction.GetOrCreateStatic(client, accept.id).InvokeStatic(value), Is.True);
            var read = client.members.Values.Single(x => x.name == "ReadItem");
            Assert.That(NeoMemberNSFunction.GetOrCreateStatic(client, read.id).InvokeStatic(item), Is.EqualTo(9));
        }

        [TestCase("StaticPrimitive")]
        [TestCase("StaticClass")]
        [TestCase("RemovedIndex")]
        [TestCase("RemoveReadd")]
        [TestCase("StaticNested")]
        [TestCase("StaticDictionary")]
        [TestCase("RemovedWrite")]
        [TestCase("ClearAlias")]
        [TestCase("DetachedSaveField")]
        public void CollectionWritesPreserveTheirValues(string name) =>
            NeoSourceParityFixture.AssertTrueCase("collection-mutation-parity.json", name);
    }
}
