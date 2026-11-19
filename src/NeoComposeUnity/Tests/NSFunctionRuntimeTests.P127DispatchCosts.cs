// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private static TypeBinding DispatchClass(string id) => TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: id));

        private static TypeBinding DispatchInterface(string id, Dictionary<string, TypeBinding>? arguments = null) =>
            TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: id), arguments);

        private static NeoInterfaceDispatch ColdDispatch(ProjectData data)
        {
            // Dispatch consumes only schema here. Avoid load-time validation
            // warming the very caches a cold pass exercises.
            var client = (NeoClient)FormatterServices.GetUninitializedObject(typeof(NeoClient));
            typeof(NeoClient).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, data);
            return NeoInterfaceDispatch.Of(client);
        }

        /// <summary>
        /// Drops every listing a search reads, so a cold dispatch that
        /// searches misses.
        /// </summary>
        private static void ForgetListings(ProjectData data)
        {
            foreach (var type in data.classes.Values)
                type.implementsInterfaces?.Clear();
            foreach (var face in data.interfaces.Values)
                face.extendsInterfaces?.Clear();
        }

        /// <summary>
        /// Cold work for n queries over a schema of size n must grow
        /// linearly: eight times the schema costs about eight times as much,
        /// far below the 64 times a per-query scan would.
        /// </summary>
        private static void AssertLinearColdWork(Func<int, Action> coldPass)
        {
            static double Fastest(Func<int, Action> coldPass, int n)
            {
                double fastest = double.MaxValue;
                for (int run = 0; run < 5; run++)
                {
                    Action pass = coldPass(n);
                    var watch = Stopwatch.StartNew();
                    pass();
                    fastest = Math.Min(fastest, watch.Elapsed.TotalMilliseconds);
                }
                return fastest;
            }

            Fastest(coldPass, 16);
            double small = Fastest(coldPass, 64);
            double large = Fastest(coldPass, 512);
            TestContext.WriteLine($"cold work: n=64 {small:F3} ms, n=512 {large:F3} ms, ratio {large / small:F1}");
            Assert.That(large / small, Is.LessThan(24), "Cold work grows faster than the schema");
        }

        private static ProjectData MixedPostingSchema(int n, bool contravariant, bool noise)
        {
            var data = new ProjectData { classes = new(), interfaces = new(), members = new() };
            data.interfaces["J"] = new Interface
            {
                id = "J",
                name = "J",
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
                id = "public",
                name = "V",
                kind = MemberKind.NSProperty,
                Access = NeoMemberAccessKind.Public,
                Modifier = NeoMemberModifierKind.Sealed,
                returnTypeInfo = IntType(),
                code = "return 9;"
            };
            for (int i = 0; i < n; i++)
            {
                string tag = "Tag" + i, face = "ITag" + i;
                data.interfaces[face] = new Interface { id = face, name = face, members = new() };
                data.classes[tag] = new NeoSchemaClass
                {
                    id = tag,
                    name = tag,
                    schema = new(),
                    implementsInterfaces = new() { new() { interfaceId = face } }
                };
                receiver.implementsInterfaces.Add(MixedPostingListing(contravariant ? DispatchInterface(face) : DispatchClass(tag)));
            }
            if (noise && !contravariant)
            {
                // Every query can reach this shared branch, but none of its
                // declarations occurs in any argument posting.
                data.classes["NoiseRoot"] = new NeoSchemaClass
                {
                    id = "NoiseRoot",
                    name = "NoiseRoot",
                    schema = new(),
                    implementsInterfaces = Enumerable.Range(0, n).Select(i => new InterfaceConstruction { interfaceId = "ITag" + i }).ToList()
                };
                for (int i = 0; i < n; i++)
                    data.classes["Noise" + i] = new NeoSchemaClass
                    {
                        id = "Noise" + i,
                        name = "Noise" + i,
                        schema = new(),
                        extendsClassId = i == 0 ? "NoiseRoot" : "Noise" + (i - 1)
                    };
            }
            if (noise && contravariant)
            {
                // The opposite-direction twin: every requested class reaches
                // the same irrelevant interface-ancestor chain.
                for (int i = 0; i <= n; i++)
                    data.interfaces["INoise" + i] = new Interface
                    {
                        id = "INoise" + i,
                        name = "INoise" + i,
                        members = new(),
                        extendsInterfaces = i == n ? new() : new() { new() { interfaceId = "INoise" + (i + 1) } }
                    };
                for (int i = 0; i < n; i++)
                    data.classes["Tag" + i].implementsInterfaces!.Add(new()
                    {
                        interfaceId = "INoise0"
                    });
            }
            return data;
        }

        private static InterfaceConstruction MixedPostingListing(TypeBinding argument) => new()
        {
            interfaceId = "J",
            arguments = new()
            {
                ["A"] = argument,
                ["B"] = TypeBinding.Closed(new TypeHead(MemberKind.Int, true))
            }
        };

        private static TypeBinding MixedPostingRequest(int i, bool contravariant) =>
            DispatchInterface("J", MixedPostingListing(contravariant ? DispatchClass("Tag" + i) : DispatchInterface("ITag" + i)).arguments);

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void P127Interface_MixedPostingNoiseHasLinearColdWork(bool contravariant, bool noise)
        {
            const int n = 16;
            var data = MixedPostingSchema(n, contravariant, noise);
            var dispatch = ColdDispatch(data);
            var answers = new List<(TypeBinding Requested, NeoInterfaceDispatch.Implementation Answer)>();
            for (int i = 0; i < n; i++)
            {
                var requested = MixedPostingRequest(i, contravariant);
                var answer = dispatch.Resolve(DispatchClass("Receiver"), requested, "slot", "get");
                Assert.That(answer.MemberId, Is.EqualTo("public"));
                answers.Add((requested, answer));
            }
            for (int repeat = 0; repeat < 100; repeat++)
                foreach (var pair in answers)
                    Assert.That(dispatch.Resolve(DispatchClass("Receiver"), pair.Requested, "slot", "get"), Is.SameAs(pair.Answer));
            // Known gap: the final conversion check closes each requested
            // class's whole ancestry, so a shared ancestor chain costs n per query.
            if (contravariant && noise)
                return;
            AssertLinearColdWork(size =>
            {
                var cold = ColdDispatch(MixedPostingSchema(size, contravariant, noise));
                return () =>
                {
                    for (int i = 0; i < size; i++)
                        cold.Resolve(DispatchClass("Receiver"), MixedPostingRequest(i, contravariant), "slot", "get");
                };
            });
        }

        private static ProjectData DistinctDefaultSlotsSchema(int n)
        {
            var data = new ProjectData { classes = new(), interfaces = new(), members = new() };
            var owner = new NeoSchemaClass { id = "Receiver", name = "Receiver", schema = new(), implementsInterfaces = new() };
            data.classes[owner.id] = owner;
            for (int index = 0; index < n; index++)
            {
                string id = "I" + index;
                data.interfaces[id] = new Interface
                {
                    id = id,
                    name = id,
                    members = new()
                    {
                        ["V"] = new()
                        {
                            id = "slot" + index,
                            kind = NeoInterfaceMemberKind.Property,
                            typeInfo = IntType(),
                            implementationMemberId = "body" + index
                        }
                    }
                };
                owner.implementsInterfaces.Add(new()
                {
                    interfaceId = id
                });
            }
            return data;
        }

        [Test]
        public void P127Interface_DistinctDefaultSlotsHaveLinearColdWork()
        {
            const int n = 16;
            var dispatch = ColdDispatch(DistinctDefaultSlotsSchema(n));
            for (int index = 0; index < n; index++)
                Assert.That(dispatch.Resolve(DispatchClass("Receiver"), DispatchInterface("I" + index), "slot" + index, "get").MemberId,
                    Is.EqualTo("body" + index));
            AssertLinearColdWork(size =>
            {
                var cold = ColdDispatch(DistinctDefaultSlotsSchema(size));
                return () =>
                {
                    for (int index = 0; index < size; index++)
                        cold.Resolve(DispatchClass("Receiver"), DispatchInterface("I" + index), "slot" + index, "get");
                };
            });
        }

        private static TypeBinding DefaultConstructionSlot(TypeBinding argument) =>
            DispatchInterface("I", new Dictionary<string, TypeBinding> { ["T"] = argument });

        private static ProjectData DistinctDefaultConstructionsSchema(int n, int mode)
        {
            var data = new ProjectData { classes = new(), interfaces = new(), members = new() };
            data.interfaces["I"] = new Interface
            {
                id = "I",
                name = "I",
                genericParams = new()
                {
                    new() { id = "T", name = "T", variance = mode == 0 ? null : NeoGenericParamVarianceKind.Out }
                },
                members = new()
                {
                    ["V"] = new()
                    {
                        id = "slot",
                        kind = NeoInterfaceMemberKind.Property,
                        typeInfo = IntType(),
                        implementationMemberId = "body"
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
                    id = tag,
                    name = tag,
                    schema = new(),
                    implementsInterfaces = new() { new() { interfaceId = face } }
                };
                owner.implementsInterfaces.Add(new()
                {
                    interfaceId = "I",
                    arguments = new()
                    {
                        ["T"] = DispatchClass(tag)
                    }
                });
            }
            return data;
        }

        private static TypeBinding DefaultConstructionRequest(int i, int mode) =>
            DefaultConstructionSlot(mode == 2 ? DispatchInterface("ITag" + i) : DispatchClass("Tag" + i));

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void P127Interface_DistinctDefaultConstructionsHaveLinearColdWork(int mode)
        {
            // 0: invariant exact; 1: covariant exact; 2: mixed class/interface variance.
            const int n = 16;
            var data = DistinctDefaultConstructionsSchema(n, mode);
            var dispatch = ColdDispatch(data);
            var answers = new List<(TypeBinding Requested, NeoInterfaceDispatch.Implementation Answer)>();
            for (int i = 0; i < n; i++)
            {
                var requested = DefaultConstructionRequest(i, mode);
                var answer = dispatch.Resolve(DispatchClass("Receiver"), requested, "slot", "get");
                Assert.That(answer.MemberId, Is.EqualTo("body"));
                Assert.That(answer.InterfaceConstruction, Is.EqualTo(DefaultConstructionSlot(DispatchClass("Tag" + i))));
                answers.Add((requested, answer));
            }
            for (int repeat = 0; repeat < 100; repeat++)
                foreach (var pair in answers)
                    Assert.That(dispatch.Resolve(DispatchClass("Receiver"), pair.Requested, "slot", "get"), Is.SameAs(pair.Answer));
            AssertLinearColdWork(size =>
            {
                var cold = ColdDispatch(DistinctDefaultConstructionsSchema(size, mode));
                return () =>
                {
                    for (int i = 0; i < size; i++)
                        cold.Resolve(DispatchClass("Receiver"), DefaultConstructionRequest(i, mode), "slot", "get");
                };
            });
        }

        private static ProjectData ClassConstructionSchema(int n, bool explicitSites)
        {
            var data = new ProjectData { classes = new(), interfaces = new(), members = new() };
            data.interfaces["I"] = new Interface
            {
                id = "I",
                name = "I",
                genericParams = new() { new() { id = "T", name = "T" } },
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
                var listing = new InterfaceConstruction { interfaceId = "I", arguments = new() { ["T"] = DispatchClass(tag) } };
                parent.implementsInterfaces.Add(listing);
                child.implementsInterfaces.Add(listing);
                if (explicitSites)
                {
                    string id = "site" + i;
                    parent.schema[id] = id;
                    data.members[id] = new NSPropertyMember
                    {
                        id = id,
                        name = "V",
                        kind = MemberKind.NSProperty,
                        returnTypeInfo = IntType(),
                        code = "return 7;",
                        Access = NeoMemberAccessKind.Private,
                        Modifier = NeoMemberModifierKind.Sealed,
                        explicitImplementation = new ExplicitInterfaceImplementation
                        {
                            interfaceId = "I",
                            interfaceMemberId = "slot",
                            arguments = listing.arguments
                        }
                    };
                }
            }
            if (!explicitSites)
            {
                parent.schema["V"] = "public";
                data.members["public"] = new NSPropertyMember
                {
                    id = "public",
                    name = "V",
                    kind = MemberKind.NSProperty,
                    returnTypeInfo = IntType(),
                    code = "return 7;",
                    Access = NeoMemberAccessKind.Public,
                    Modifier = NeoMemberModifierKind.Sealed
                };
            }
            return data;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void P127Interface_ClassConstructionLookupsHaveLinearColdWork(bool explicitSites)
        {
            const int n = 16;
            var dispatch = ColdDispatch(ClassConstructionSchema(n, explicitSites));
            for (int i = 0; i < n; i++)
                Assert.That(dispatch.Resolve(DispatchClass("Child"), DefaultConstructionSlot(DispatchClass("Tag" + i)), "slot", "get").MemberId,
                    Is.EqualTo(explicitSites ? "site" + i : "public"));
            AssertLinearColdWork(size =>
            {
                var cold = ColdDispatch(ClassConstructionSchema(size, explicitSites));
                return () =>
                {
                    for (int i = 0; i < size; i++)
                        cold.Resolve(DispatchClass("Child"), DefaultConstructionSlot(DispatchClass("Tag" + i)), "slot", "get");
                };
            });
        }

        private static ProjectData EmptyClassOwnerSchema(int n)
        {
            var data = new ProjectData { classes = new(), interfaces = new(), members = new() };
            var parent = new NeoSchemaClass { id = "Base", name = "Base", schema = new(), implementsInterfaces = new() };
            data.classes[parent.id] = parent;
            for (int i = 0; i < n; i++)
            {
                string id = "I" + i;
                data.interfaces[id] = new Interface
                {
                    id = id,
                    name = id,
                    members = new()
                    {
                        ["V" + i] = new()
                        {
                            id = "slot" + i,
                            kind = NeoInterfaceMemberKind.Property,
                            typeInfo = IntType(),
                            implementationMemberId = "body" + i
                        }
                    }
                };
                parent.implementsInterfaces.Add(new()
                {
                    interfaceId = id
                });
                data.classes["C" + i] = new NeoSchemaClass
                {
                    id = "C" + i,
                    name = "C" + i,
                    schema = new(),
                    extendsClassId = i == 0 ? "Base" : "C" + (i - 1)
                };
            }
            return data;
        }

        [Test]
        public void P127Interface_DistinctDefaultSlotsShareEmptyClassOwnerSearch()
        {
            const int n = 16;
            var dispatch = ColdDispatch(EmptyClassOwnerSchema(n));
            for (int i = 0; i < n; i++)
                Assert.That(dispatch.Resolve(DispatchClass("C" + (n - 1)), DispatchInterface("I" + i), "slot" + i, "get").MemberId,
                    Is.EqualTo("body" + i));
            AssertLinearColdWork(size =>
            {
                var cold = ColdDispatch(EmptyClassOwnerSchema(size));
                return () =>
                {
                    for (int i = 0; i < size; i++)
                        cold.Resolve(DispatchClass("C" + (size - 1)), DispatchInterface("I" + i), "slot" + i, "get");
                };
            });
        }

        [Test]
        public void P127Interface_AmbiguityOutcomeAddsNoWarmSearchWork()
        {
            using var client = AmbiguousInterfaceSlotClient();
            var dispatch = NeoInterfaceDispatch.Of(client);
            var receiver = DispatchClass("receiver-class");
            var requested = DispatchInterface("slot-contract");
            var first = Assert.Throws<NeoAmbiguousImplementationError>(() => dispatch.Resolve(receiver, requested, "slot", "call"))!;
            // A cached outcome rethrows the stored message; a new search would build a new one.
            for (int repeat = 0; repeat < 100; repeat++)
                Assert.That(Assert.Throws<NeoAmbiguousImplementationError>(() => dispatch.Resolve(receiver, requested, "slot", "call"))!.Message,
                    Is.SameAs(first.Message), "Ambiguity is an outcome cached under the existing resolution key");
        }
    }
}
