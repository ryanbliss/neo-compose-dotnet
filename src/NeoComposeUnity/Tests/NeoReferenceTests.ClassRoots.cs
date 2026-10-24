// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime;
using NUnit.Framework;
using Parity = NeoCompose.Tests.ReferenceParity;

namespace NeoCompose.Tests
{
    public partial class NeoReferenceTests
    {
        [Test]
        public void ReferenceReadsFreestandingClassRootAndPreservesWritableIdentity()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            using var value = new Parity.Tally(7);
            var reference = NeoGeneratedTypesSupport.ReadReference<Parity.Tally>(app.Client, (object)value.valueId!)!;

            Assert.AreEqual(value.valueId, reference.TargetId);
            Assert.AreEqual(7, reference.Value.N);
            Assert.AreEqual(value.valueId, reference.Value.valueId);
            reference.Value.N = 11;
            Assert.AreEqual(11, value.N);
        }

        [Test]
        public void ReferenceReadsFreestandingClassThroughReadOnlyContract()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            using var value = new Parity.Tally(7);
            var reference = NeoGeneratedTypesSupport.ReadReference<Parity.IReadOnlyTally>(app.Client, (object)value.valueId!)!;

            Assert.AreEqual(7, reference.Value.N);
            Assert.AreEqual(value.valueId, reference.Value.valueId);
            Assert.IsTrue(reference.Value.TryWritable(out Parity.Tally writable));
            Assert.AreEqual(value.valueId, writable.valueId);
        }

        [Test]
        public void ClassReferenceStillRejectsMissingTarget()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            var reference = NeoGeneratedTypesSupport.ReadReference<Parity.Tally>(app.Client, (object)"missing-class-root")!;

            StringAssert.Contains("no longer exists", Assert.Catch(() => _ = reference.Value)!.Message);
        }

        [Test]
        public void ReadOnlyClassReferencePreservesAssetPermissions()
        {
            using var app = new Parity.TestProjectNeo(Client(Data()));
            var reference = NeoGeneratedTypesSupport.ReadReference<Parity.IReadOnlyTally>(app.Client, (object)Parity.Probe.Stock.valueId!)!;

            Assert.AreEqual(Parity.Probe.Stock.N, reference.Value.N);
            Assert.AreEqual(Parity.Probe.Stock.valueId, reference.Value.valueId);
            Assert.IsFalse(reference.Value.TryWritable(out Parity.Tally _));
        }
    }
}
