// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.IO;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using Newtonsoft.Json;

namespace NeoCompose.Tests
{
    public sealed class P127PrimitiveIntrinsicTests
    {
        [Test]
        public void EveryCanonicalConformanceMemberHasAnIntrinsicOrDefaultBody()
        {
            var schema = NeoTestExport.Read(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/p127-operators-parity-export.json"));
            int count = 0;
            foreach (var kind in NeoPrimitiveConformance.Table)
                foreach (var row in kind.Value)
                    foreach (var member in schema.interfaces[row.InterfaceId].members)
                    {
                        string resolved = NeoPrimitiveIntrinsics.Resolve(kind.Key, row.InterfaceId, NeoCallableSignature.Name(member.Key));
                        if (resolved == "default")
                            Assert.That(member.Value.implementationMemberId, Is.Not.Null.And.Not.Empty);
                        else
                            Assert.That(NeoPrimitiveIntrinsics.IsMember(resolved), Is.True);
                        count++;
                    }
            Assert.That(count, Is.GreaterThan(100));
        }

        [Test]
        public void RemovedNewPrimitiveMemberIsRefused() => Assert.Throws<JsonSerializationException>(() =>
            JsonConvert.DeserializeObject<Pointer>("{\"type\":\"primitiveMember\",\"primitive\":{\"type\":20,\"required\":true},\"member\":\"New\",\"args\":[]}"));
    }
}
