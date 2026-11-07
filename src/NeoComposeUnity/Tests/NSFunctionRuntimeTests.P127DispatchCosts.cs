// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class NSFunctionRuntimeTests
    {
        [TestCase(8, false, false)]
        [TestCase(8, false, true)]
        [TestCase(8, true, false)]
        [TestCase(8, true, true)]
        [TestCase(16, false, false)]
        [TestCase(16, false, true)]
        [TestCase(16, true, false)]
        [TestCase(16, true, true)]
        [TestCase(32, false, false)]
        [TestCase(32, false, true)]
        [TestCase(32, true, false)]
        [TestCase(32, true, true)]
        [TestCase(64, false, false)]
        [TestCase(64, false, true)]
        [TestCase(64, true, false)]
        [TestCase(64, true, true)]
        public void P127Interface_MixedPostingNoiseHasLinearColdWork(int n, bool contravariant, bool noise)
        {
            var data = new ProjectData { classes = new(), interfaces = new(), members = new() };
            TypeBinding Integer() => TypeBinding.Closed(new TypeHead(MemberKind.Int, true));
            TypeBinding ClassType(string id) => TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: id));
            TypeBinding InterfaceType(string id) => TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: id));
            InterfaceConstruction Listing(TypeBinding argument) => new()
            {
                interfaceId = "J", arguments = new() { ["A"] = argument, ["B"] = Integer() }
            };
            data.interfaces["J"] = new Interface
            {
                id = "J", name = "J",
                genericParams = new()
                {
                    new() { id = "A", name = "A", variance = contravariant ? NeoGenericParamVarianceKind.In : NeoGenericParamVarianceKind.Out },
                    new() { id = "B", name = "B" }
                },
                members = new() { ["V"] = new() { id = "slot", kind = NeoInterfaceMemberKind.Property, typeInfo = IntType() } }
            };
            var receiver = new NeoSchemaClass { id = "Receiver", name = "Receiver", schema = new() { ["V"] = "public" }, implementsInterfaces = new() };
            data.classes[receiver.id] = receiver;
            data.members["public"] = new NSPropertyMember
            {
                id = "public", name = "V", kind = MemberKind.NSProperty,
                Access = NeoMemberAccessKind.Public, Modifier = NeoMemberModifierKind.Sealed,
                returnTypeInfo = IntType(), code = "return 9;"
            };
            for (int i = 0; i < n; i++)
            {
                string tag = "Tag" + i, face = "ITag" + i;
                data.interfaces[face] = new Interface { id = face, name = face, members = new() };
                data.classes[tag] = new NeoSchemaClass
                {
                    id = tag, name = tag, schema = new(), implementsInterfaces = new() { new() { interfaceId = face } }
                };
                receiver.implementsInterfaces.Add(Listing(contravariant ? InterfaceType(face) : ClassType(tag)));
            }
            if (noise && !contravariant)
            {
                // Every query can reach this shared branch, but none of its
                // declarations occurs in any argument posting.
                data.classes["NoiseRoot"] = new NeoSchemaClass
                {
                    id = "NoiseRoot", name = "NoiseRoot", schema = new(),
                    implementsInterfaces = Enumerable.Range(0, n).Select(i => new InterfaceConstruction { interfaceId = "ITag" + i }).ToList()
                };
                for (int i = 0; i < n; i++)
                    data.classes["Noise" + i] = new NeoSchemaClass
                    {
                        id = "Noise" + i, name = "Noise" + i, schema = new(), extendsClassId = i == 0 ? "NoiseRoot" : "Noise" + (i - 1)
                    };
            }
            if (noise && contravariant)
            {
                // The opposite-direction twin: every requested class reaches
                // the same irrelevant interface-ancestor chain.
                for (int i = 0; i <= n; i++)
                    data.interfaces["INoise" + i] = new Interface
                    {
                        id = "INoise" + i, name = "INoise" + i, members = new(),
                        extendsInterfaces = i == n ? new() : new() { new() { interfaceId = "INoise" + (i + 1) } }
                    };
                for (int i = 0; i < n; i++)
                    data.classes["Tag" + i].implementsInterfaces!.Add(new() { interfaceId = "INoise0" });
            }
            // Dispatch consumes only schema here. Avoid load-time validation
            // warming the very caches whose cold work this test measures.
            var client = (NeoClient)FormatterServices.GetUninitializedObject(typeof(NeoClient));
            typeof(NeoClient).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, data);
            var dispatch = NeoInterfaceDispatch.Of(client);
            var answers = new List<(TypeBinding Requested, NeoInterfaceDispatch.Implementation Answer)>();
            for (int i = 0; i < n; i++)
            {
                var requested = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: "J"),
                    Listing(contravariant ? ClassType("Tag" + i) : InterfaceType("ITag" + i)).arguments);
                var answer = dispatch.Resolve(ClassType("Receiver"), requested, "slot", "get");
                Assert.That(answer.MemberId, Is.EqualTo("public"));
                answers.Add((requested, answer));
            }
            var cold = (dispatch.TestConversions, dispatch.TestGraphPops, dispatch.TestGraphEdges,
                dispatch.TestIndexPops, dispatch.TestIndexEdges, dispatch.TestGlobalNodes, dispatch.TestGlobalEdges);
            TestContext.WriteLine($"N={n} in={contravariant} noise={noise}: {cold}");
            Assert.That(dispatch.TestConversions, Is.EqualTo(2 * n), "Actual recursive conversion calls");
            Assert.That(dispatch.TestGraphPops, Is.EqualTo(2 * n), "Actual query stack pops, including duplicate visits");
            Assert.That(dispatch.TestGraphEdges, Is.EqualTo(n), "Actual query adjacency entries visited");
            Assert.That(dispatch.TestIndexPops, Is.LessThanOrEqualTo(2 * n), "One shared index build, no per-request prefixes");
            Assert.That(dispatch.TestIndexEdges, Is.LessThanOrEqualTo(n), "Retained direct edges, no transitive closure");
            Assert.That(dispatch.TestGlobalNodes, Is.LessThanOrEqualTo(3 * n + 3));
            Assert.That(dispatch.TestGlobalEdges, Is.LessThanOrEqualTo(4 * n));
            for (int repeat = 0; repeat < 100; repeat++)
                foreach (var pair in answers)
                    Assert.That(dispatch.Resolve(ClassType("Receiver"), pair.Requested, "slot", "get"), Is.SameAs(pair.Answer));
            Assert.That((dispatch.TestConversions, dispatch.TestGraphPops, dispatch.TestGraphEdges,
                dispatch.TestIndexPops, dispatch.TestIndexEdges, dispatch.TestGlobalNodes, dispatch.TestGlobalEdges), Is.EqualTo(cold));
        }
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(32)]
        [TestCase(64)]
        public void P127Interface_DistinctDefaultSlotsVisitOwnersAndMembersOnce(int n)
        {
            var data = new ProjectData { classes = new(), interfaces = new(), members = new() };
            var owner = new NeoSchemaClass
            {
                id = "Receiver", name = "Receiver", schema = new(), implementsInterfaces = new()
            };
            data.classes[owner.id] = owner;
            for (int index = 0; index < n; index++)
            {
                string id = "I" + index;
                data.interfaces[id] = new Interface
                {
                    id = id, name = id,
                    members = new()
                    {
                        ["V"] = new()
                        {
                            id = "slot" + index, kind = NeoInterfaceMemberKind.Property,
                            typeInfo = IntType(), implementationMemberId = "body" + index
                        }
                    }
                };
                owner.implementsInterfaces.Add(new() { interfaceId = id });
            }
            var client = (NeoClient)FormatterServices.GetUninitializedObject(typeof(NeoClient));
            typeof(NeoClient).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, data);
            var dispatch = NeoInterfaceDispatch.Of(client);
            var receiver = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: owner.id));
            for (int index = 0; index < n; index++)
            {
                var requested = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: "I" + index));
                Assert.That(dispatch.Resolve(receiver, requested, "slot" + index, "get").MemberId, Is.EqualTo("body" + index));
            }
            TestContext.WriteLine($"N={n}: owners={dispatch.TestDefaultOwnerVisits}, members={dispatch.TestDefaultMemberVisits}, candidates={dispatch.TestDefaultCandidateVisits}");
            Assert.That(dispatch.TestDefaultOwnerVisits, Is.EqualTo(n), "Actual default-owner visits, including index construction");
            Assert.That(dispatch.TestDefaultMemberVisits, Is.EqualTo(n), "Actual default-member visits, including index construction");
            Assert.That(dispatch.TestDefaultCandidateVisits, Is.EqualTo(n), "Only the requested slot's candidate is examined");
        }

        [TestCase(8)]
        [TestCase(16)]
        [TestCase(32)]
        [TestCase(64)]
        public void P127Interface_AmbiguityOutcomeAddsNoWarmSearchWork(int noise)
        {
            using var client = AmbiguousInterfaceSlotClient();
            for (int index = 0; index < noise; index++)
            {
                string id = "Noise" + index;
                client.ProjectDataForRuntime.interfaces[id] = new Interface { id = id, name = id, members = new() };
                client.ProjectDataForRuntime.classes["receiver-class"].implementsInterfaces!.Add(new() { interfaceId = id });
            }
            var dispatch = NeoInterfaceDispatch.Of(client);
            var receiver = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: "receiver-class"));
            var requested = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: "slot-contract"));
            var first = Assert.Throws<NeoAmbiguousImplementationError>(() => dispatch.Resolve(receiver, requested, "slot", "call"))!;
            var cold = (dispatch.TestDefaultOwnerVisits, dispatch.TestDefaultMemberVisits, dispatch.TestDefaultCandidateVisits, dispatch.TestConversions);
            Assert.That(dispatch.TestDefaultOwnerVisits, Is.GreaterThanOrEqualTo(noise));
            for (int repeat = 0; repeat < 100; repeat++)
                Assert.That(Assert.Throws<NeoAmbiguousImplementationError>(() => dispatch.Resolve(receiver, requested, "slot", "call"))!.Message,
                    Is.EqualTo(first.Message));
            var warm = (dispatch.TestDefaultOwnerVisits, dispatch.TestDefaultMemberVisits, dispatch.TestDefaultCandidateVisits, dispatch.TestConversions);
            TestContext.WriteLine($"Noise={noise}: cold={cold}, after 100 warm errors={warm}");
            Assert.That(warm, Is.EqualTo(cold), "Ambiguity is an outcome cached under the existing resolution key");
        }
    }
}
#endif
