// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public sealed class P127DelegateShapeTests
    {
        private static JObject Closure() => new()
        {
            ["type"] = PointerKind.DelegateClosure,
            ["typeInfo"] = JObject.FromObject(new DelegateTypeInfo
            {
                type = MemberKind.NSDelegate,
                required = true,
                returnTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            }),
            ["action"] = new JObject(),
            ["captures"] = new JArray(),
            ["code"] = "() => 1",
        };

        [TestCase("environment", false)]
        [TestCase("this", true)]
        [TestCase(null, true)]
        [TestCase("invalid", false)]
        public void ClosureTargetMustMatchPlacement(string? placement, bool target)
        {
            var pointer = Closure();
            if (placement is not null)
                pointer["placement"] = placement;
            if (target)
                pointer["target"] = new JObject { ["type"] = PointerKind.Value, ["value"] = 1 };
            Assert.That(Assert.Throws<JsonSerializationException>(() => pointer.ToObject<Pointer>())!.Message,
                Does.Contain("target exactly for environment placement"));
        }

        [TestCase("{'parameterCount':1,'cache':{'owner':'owner','delegateType':{'type':2}}}")]
        [TestCase("{'parameterCount':1,'cache':{'owner':'owner','delegateType':{'type':26},'method':false}}")]
        [TestCase("{'parameterCount':-1}")]
        public void PrimitiveMethodGroupRefusesInvalidDescriptor(string group)
        {
            var pointer = new JObject
            {
                ["type"] = PointerKind.PrimitiveMember,
                ["primitive"] = JObject.FromObject(new PrimitiveTypeInfo { type = MemberKind.Int, required = true }),
                ["member"] = "Parse",
                ["args"] = new JArray(),
                ["methodGroup"] = JObject.Parse(group),
            };
            Assert.Throws<JsonSerializationException>(() => pointer.ToObject<Pointer>());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PrimitiveMemberPointerRoundTripsWithOptionalGroup(bool group)
        {
            var pointer = new PrimitiveMemberPointer
            {
                type = PointerKind.PrimitiveMember,
                primitive = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                member = "Parse",
                args = Array.Empty<Pointer>(),
                methodGroup = group ? new PrimitiveMethodGroup { parameterCount = 1 } : null,
            };
            var stored = JObject.FromObject(pointer);
            Assert.That(stored.Property("methodGroup") is not null, Is.EqualTo(group));
            Assert.That(stored.Property("receiver"), Is.Null);
            var read = (PrimitiveMemberPointer)stored.ToObject<Pointer>()!;
            Assert.That(read.methodGroup?.parameterCount, Is.EqualTo(group ? (int?)1 : null));
        }

        [Test]
        public void StoredCreateClosureNeedsSourceAndCannotCapture()
        {
            var closure = new JObject { ["action"] = new JObject(), ["captures"] = new JArray() };
            var payload = new JObject
            {
                ["comparer"] = (int)NeoBuiltInComparerKind.Create,
                ["family"] = (int)NeoComparerFamilyKind.Comparison,
                ["type"] = TestTypeBindings.JsonFromKey("int"),
                ["delegates"] = new JArray(closure),
            };
            Assert.That(Assert.Throws<JsonSerializationException>(() => NeoStorablePayloads.ValidateBuiltInComparer(payload, "test"))!.Message,
                Does.Contain("has no source to compile"));
            closure["code"] = "(a, b) => a - b";
            closure["captures"] = new JArray(1);
            Assert.That(Assert.Throws<JsonSerializationException>(() => NeoStorablePayloads.ValidateBuiltInComparer(payload, "test"))!.Message,
                Does.Contain("cannot capture values"));
        }
    }
}
