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

        [TestCase(8, 0)]
        [TestCase(8, 1)]
        [TestCase(8, 2)]
        [TestCase(16, 0)]
        [TestCase(16, 1)]
        [TestCase(16, 2)]
        [TestCase(32, 0)]
        [TestCase(32, 1)]
        [TestCase(32, 2)]
        [TestCase(64, 0)]
        [TestCase(64, 1)]
        [TestCase(64, 2)]
        public void P127Interface_DistinctDefaultConstructionsVisitOnlyMatchingCandidates(int n, int mode)
        {
            // 0: invariant exact; 1: covariant exact; 2: mixed class/interface variance.
            var data = new ProjectData { classes = new(), interfaces = new(), members = new() };
            TypeBinding ClassType(string id) => TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: id));
            TypeBinding FaceType(string id) => TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: id));
            TypeBinding Slot(TypeBinding argument) => TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: "I"),
                new Dictionary<string, TypeBinding> { ["T"] = argument });
            data.interfaces["I"] = new Interface
            {
                id = "I", name = "I", genericParams = new()
                {
                    new() { id = "T", name = "T", variance = mode == 0 ? null : NeoGenericParamVarianceKind.Out }
                },
                members = new()
                {
                    ["V"] = new()
                    {
                        id = "slot", kind = NeoInterfaceMemberKind.Property, typeInfo = IntType(), implementationMemberId = "body"
                    }
                }
            };
            var owner = new NeoSchemaClass { id = "Receiver", name = "Receiver", schema = new(), implementsInterfaces = new() };
            data.classes[owner.id] = owner;
            for (int i = 0; i < n; i++)
            {
                string tag = "Tag" + i, face = "ITag" + i;
                data.interfaces[face] = new Interface { id = face, name = face, members = new() };
                data.classes[tag] = new NeoSchemaClass
                {
                    id = tag, name = tag, schema = new(), implementsInterfaces = new() { new() { interfaceId = face } }
                };
                owner.implementsInterfaces.Add(new() { interfaceId = "I", arguments = new() { ["T"] = ClassType(tag) } });
            }
            var client = (NeoClient)FormatterServices.GetUninitializedObject(typeof(NeoClient));
            typeof(NeoClient).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, data);
            var dispatch = NeoInterfaceDispatch.Of(client);
            var answers = new List<(TypeBinding Requested, NeoInterfaceDispatch.Implementation Answer)>();
            for (int i = 0; i < n; i++)
            {
                var requested = Slot(mode == 2 ? FaceType("ITag" + i) : ClassType("Tag" + i));
                var answer = dispatch.Resolve(ClassType("Receiver"), requested, "slot", "get");
                Assert.That(answer.MemberId, Is.EqualTo("body"));
                Assert.That(answer.InterfaceConstruction, Is.EqualTo(Slot(ClassType("Tag" + i))));
                answers.Add((requested, answer));
            }
            var cold = (dispatch.TestDefaultOwnerVisits, dispatch.TestDefaultMemberVisits, dispatch.TestDefaultCandidateVisits,
                dispatch.TestConversions, dispatch.TestGraphPops, dispatch.TestGraphEdges);
            TestContext.WriteLine($"N={n} mode={mode}: owners, members, candidates, conversions, pops, edges={cold}");
            Assert.That(dispatch.TestDefaultOwnerVisits, Is.EqualTo(n));
            Assert.That(dispatch.TestDefaultMemberVisits, Is.EqualTo(n));
            Assert.That(dispatch.TestDefaultCandidateVisits, Is.EqualTo(n), "Distinct constructions must not scan the whole slot");
            Assert.That(dispatch.TestConversions, Is.LessThanOrEqualTo(4 * n), "Pruning must precede the final conversion check");
            Assert.That(dispatch.TestGraphPops, Is.LessThanOrEqualTo(4 * n));
            Assert.That(dispatch.TestGraphEdges, Is.LessThanOrEqualTo(2 * n));
            for (int repeat = 0; repeat < 100; repeat++)
                foreach (var pair in answers)
                    Assert.That(dispatch.Resolve(ClassType("Receiver"), pair.Requested, "slot", "get"), Is.SameAs(pair.Answer));
            Assert.That((dispatch.TestDefaultOwnerVisits, dispatch.TestDefaultMemberVisits, dispatch.TestDefaultCandidateVisits,
                dispatch.TestConversions, dispatch.TestGraphPops, dispatch.TestGraphEdges), Is.EqualTo(cold));
        }

        [TestCase(8, false)]
        [TestCase(8, true)]
        [TestCase(16, false)]
        [TestCase(16, true)]
        [TestCase(32, false)]
        [TestCase(32, true)]
        [TestCase(64, false)]
        [TestCase(64, true)]
        public void P127Interface_ClassConstructionLookupsShareColdIndexes(int n, bool explicitSites)
        {
            var data = new ProjectData { classes = new(), interfaces = new(), members = new() };
            TypeBinding ClassType(string id) => TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: id));
            data.interfaces["I"] = new Interface
            {
                id = "I", name = "I", genericParams = new() { new() { id = "T", name = "T" } },
                members = new() { ["V"] = new() { id = "slot", kind = NeoInterfaceMemberKind.Property, typeInfo = IntType() } }
            };
            var parent = new NeoSchemaClass { id = "Base", name = "Base", schema = new(), implementsInterfaces = new() };
            var child = new NeoSchemaClass { id = "Child", name = "Child", schema = new(), extendsClassId = "Base", implementsInterfaces = new() };
            data.classes[parent.id] = parent;
            data.classes[child.id] = child;
            for (int i = 0; i < n; i++)
            {
                string tag = "Tag" + i;
                data.classes[tag] = new NeoSchemaClass { id = tag, name = tag, schema = new() };
                var listing = new InterfaceConstruction { interfaceId = "I", arguments = new() { ["T"] = ClassType(tag) } };
                parent.implementsInterfaces.Add(listing);
                child.implementsInterfaces.Add(listing);
                if (explicitSites)
                {
                    string id = "site" + i;
                    parent.schema[id] = id;
                    data.members[id] = new NSPropertyMember
                    {
                        id = id, name = "V", kind = MemberKind.NSProperty, returnTypeInfo = IntType(), code = "return 7;",
                        Access = NeoMemberAccessKind.Private, Modifier = NeoMemberModifierKind.Sealed,
                        explicitImplementation = new ExplicitInterfaceImplementation
                        {
                            interfaceId = "I", interfaceMemberId = "slot", arguments = listing.arguments
                        }
                    };
                }
            }
            if (!explicitSites)
            {
                parent.schema["V"] = "public";
                data.members["public"] = new NSPropertyMember
                {
                    id = "public", name = "V", kind = MemberKind.NSProperty, returnTypeInfo = IntType(), code = "return 7;",
                    Access = NeoMemberAccessKind.Public, Modifier = NeoMemberModifierKind.Sealed
                };
            }
            var client = (NeoClient)FormatterServices.GetUninitializedObject(typeof(NeoClient));
            typeof(NeoClient).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, data);
            var dispatch = NeoInterfaceDispatch.Of(client);
            for (int i = 0; i < n; i++)
            {
                var requested = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: "I"), parent.implementsInterfaces[i].arguments);
                Assert.That(dispatch.Resolve(ClassType("Child"), requested, "slot", "get").MemberId,
                    Is.EqualTo(explicitSites ? "site" + i : "public"));
            }
            var declarations = (NeoInterfaceDispatch)typeof(NeoInterfaceDispatch)
                .GetField("declarationDispatch", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dispatch)!;
            TestContext.WriteLine($"N={n} explicit={explicitSites}: explicit visits={declarations.TestExplicitSiteVisits}, inherited listing visits={declarations.TestInheritedListingVisits}");
            Assert.That(declarations.TestExplicitSiteVisits, Is.EqualTo(explicitSites ? 2 * n : 0), "Build each owner's exact-site index once");
            Assert.That(declarations.TestInheritedListingVisits, Is.EqualTo(n), "Share substituted own listings across construction queries");
        }

        [TestCase(8)]
        [TestCase(16)]
        [TestCase(32)]
        [TestCase(64)]
        public void P127Interface_DistinctDefaultSlotsShareEmptyClassOwnerSearch(int n)
        {
            var data = new ProjectData { classes = new(), interfaces = new(), members = new() };
            var parent = new NeoSchemaClass { id = "Base", name = "Base", schema = new(), implementsInterfaces = new() };
            data.classes[parent.id] = parent;
            for (int i = 0; i < n; i++)
            {
                string id = "I" + i;
                data.interfaces[id] = new Interface
                {
                    id = id, name = id, members = new()
                    {
                        ["V" + i] = new()
                        {
                            id = "slot" + i, kind = NeoInterfaceMemberKind.Property, typeInfo = IntType(), implementationMemberId = "body" + i
                        }
                    }
                };
                parent.implementsInterfaces.Add(new() { interfaceId = id });
                data.classes["C" + i] = new NeoSchemaClass
                {
                    id = "C" + i, name = "C" + i, schema = new(), extendsClassId = i == 0 ? "Base" : "C" + (i - 1)
                };
            }
            var client = (NeoClient)FormatterServices.GetUninitializedObject(typeof(NeoClient));
            typeof(NeoClient).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, data);
            var dispatch = NeoInterfaceDispatch.Of(client);
            var receiver = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: "C" + (n - 1)));
            for (int i = 0; i < n; i++)
            {
                var requested = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: "I" + i));
                Assert.That(dispatch.Resolve(receiver, requested, "slot" + i, "get").MemberId, Is.EqualTo("body" + i));
            }
            var declarations = (NeoInterfaceDispatch)typeof(NeoInterfaceDispatch)
                .GetField("declarationDispatch", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dispatch)!;
            TestContext.WriteLine($"N={n}: class owner visits={declarations.TestClassMemberOwnerVisits}");
            Assert.That(declarations.TestClassMemberOwnerVisits, Is.EqualTo(n + 1), "Unrelated default slots share the class-chain index");
            Assert.That(dispatch.TestDefaultCandidateVisits, Is.EqualTo(n));
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
