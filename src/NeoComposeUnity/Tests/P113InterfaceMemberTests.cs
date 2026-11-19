// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P113 interface members in the SDK: which rows an interface slot admits,
    /// the slot arguments a determined row reads, every write path's
    /// implements-and-stamped check, and the generated interface read and write
    /// helpers. <c>IContainer&lt;T&gt;</c> is implemented by a closed
    /// <c>Box</c>, an <c>OpenBox&lt;U&gt;</c> it determines, a
    /// <c>Keyed&lt;K, V&gt;</c> it can't determine, and not by <c>Plain</c>.
    /// <c>IntBox</c> and <c>CultureBox</c> implement it with other arguments
    /// than the slots' <c>IContainer&lt;string&gt;</c>, and <c>TwoBox</c>
    /// implements both <c>IContainer&lt;int&gt;</c> and
    /// <c>IContainer&lt;string&gt;</c>. <c>SeedMaker</c> and
    /// <c>SproutMaker</c> implement the covariant <c>IProducer&lt;out X&gt;</c>
    /// over <c>Seed</c> and its subclass <c>Sprout</c>.
    /// </summary>
    public class P113InterfaceMemberTests
    {
        private const string ContainerId = "iface-container";
        private const string ContainerT = "iface-container-t";
        private const string BoxId = "class-box";
        private const string OpenBoxId = "class-open-box";
        private const string OpenBoxU = "class-open-box-u";
        private const string KeyedId = "class-keyed";
        private const string KeyedK = "class-keyed-k";
        private const string KeyedV = "class-keyed-v";
        private const string PlainId = "class-plain";
        private const string IntBoxId = "class-int-box";
        private const string CultureBoxId = "class-culture-box";
        private const string HolderP = "class-generic-holder-p";
        private const string TwoBoxId = "class-two-box";
        private const string ProducerId = "iface-producer";
        private const string ProducerX = "iface-producer-x";
        private const string SeedId = "class-seed";
        private const string SproutId = "class-sprout";
        private const string SeedMakerId = "class-seed-maker";
        private const string SproutMakerId = "class-sprout-maker";

        private interface ITestContainer
        {
        }

        private interface ITestResolvedContainer
        {
        }

        private interface ITestUnnamedContainer
        {
        }

        private sealed class TestBox : NeoGeneratedClassValue, ITestContainer, ITestResolvedContainer
        {
            internal TestBox(NeoClient client, NeoMemberClass node, bool readOnly)
                : base(client, node, BoxId, readOnly)
            {
            }
        }

        private sealed class TestOpenBox : NeoGeneratedClassValue, ITestResolvedContainer
        {
            internal TestOpenBox(NeoClient client, NeoMemberClass node, bool readOnly)
                : base(client, node, OpenBoxId, readOnly)
            {
            }
        }

        private interface ITestPair<A, B>
        {
        }

        private sealed class NotNeo : ITestContainer
        {
        }

        static P113InterfaceMemberTests()
        {
            // What codegen's `NeoResolveIContainer<T>` registers: the open
            // row's construction for this C# interface.
            NeoInterfaceResolvers<ITestResolvedContainer>.Register((client, node, readOnly) =>
                node.ClassId == OpenBoxId ? new TestOpenBox(client, node, readOnly) : null);
        }

        [Test]
        public void Determination_SeparatesClosedDeterminedUndeterminedAndUnimplemented()
        {
            var client = LoadClient();
            var implementers = client.InterfaceImplementers;

            Assert.AreEqual(NeoInterfaceDeterminationKind.Closed, implementers.Determination(BoxId, ContainerId).kind);
            NeoInterfaceDetermination open = implementers.Determination(OpenBoxId, ContainerId);
            Assert.IsTrue(open.Determines);
            Assert.AreEqual(OpenBoxU, open.paramByPosition![ContainerT]);
            Assert.IsFalse(implementers.Determination(KeyedId, ContainerId).Determines);
            Assert.AreEqual(
                NeoInterfaceDeterminationKind.NotImplemented,
                implementers.Determination(PlainId, ContainerId).kind);
            CollectionAssert.AreEqual(
                new[] { BoxId, OpenBoxId, KeyedId, IntBoxId, CultureBoxId, TwoBoxId },
                implementers.Implementers(ContainerId));
        }

        [Test]
        public void RowRefusal_RefusesAnUnimplementedClassOrAnUnstampedOpenRow()
        {
            var implementers = LoadClient().InterfaceImplementers;
            var unstamped = new ObjectMemberValue { id = "row" };
            var stamped = new ObjectMemberValue { id = "row", genericBindings = KeyedStamp() };

            Assert.AreEqual(
                "Value \"row\" is a binding node and stores no stamp.",
                implementers.RowRefusal(() => "Holder.Slot", "row", unstamped, KeyedId, ContainerId));
            Assert.IsNull(implementers.RowRefusal(() => "Holder.Slot", "row", stamped, KeyedId, ContainerId));
            Assert.AreEqual(
                "Holder.Slot can't hold a Plain, because Plain doesn't implement IContainer.",
                implementers.RowRefusal(() => "Holder.Slot", "row", unstamped, PlainId, ContainerId));
            Assert.IsNull(implementers.RowRefusal(() => "Holder.Slot", "row", unstamped, BoxId, ContainerId));
        }

        [Test]
        public void SlotClassArguments_ReadsTheSlotsArgumentsByPositionForADeterminedRow()
        {
            var client = LoadClient();
            Assert.IsTrue(client.TryGetMember("member-slot", out ClassMember? slot));

            var open = client.InterfaceImplementers.SlotClassArguments(slot!, OpenBoxId);
            Assert.IsNotNull(open);
            Assert.AreEqual(StringArgument, open![OpenBoxU]);
            Assert.IsNull(client.InterfaceImplementers.SlotClassArguments(slot!, BoxId));
            Assert.AreSame(open, client.InterfaceImplementers.SlotClassArguments(slot!, OpenBoxId));
        }

        [Test]
        public void ClassSlotWrite_HoldsAStampedRowTheInterfaceLeavesOpen()
        {
            var client = LoadClient();
            AddRow(client, "keyed-row", KeyedId);
            AddRow(client, "stamped-row", KeyedId, KeyedStamp());
            var holder = client.save;

            var error = Assert.Throws<InvalidOperationException>(() =>
                holder.SetSerializedValue("Slot", Reference("keyed-row")))!;

            Assert.AreEqual("Value \"keyed-row\" is a binding node and stores no stamp.", error.Message);
            Assert.IsFalse(holder.TryGet<NeoMemberClass>("Slot", out _));
            holder.SetSerializedValue("Slot", Reference("stamped-row"));
            Assert.AreEqual(KeyedId, holder.Get<NeoMemberClass>("Slot").ClassId);
        }

        [Test]
        public void ClassSlotWrite_RefusesARowThatImplementsOtherArguments()
        {
            var client = LoadClient();
            AddRow(client, "int-row", IntBoxId);
            var holder = client.save;

            var error = Assert.Throws<InvalidOperationException>(() =>
                holder.SetSerializedValue("Slot", Reference("int-row")))!;

            Assert.AreEqual(
                "Holder.Slot can't hold a IntBox, because IntBox implements IContainer<int>, not IContainer<string>.",
                error.Message);
            Assert.IsFalse(holder.TryGet<NeoMemberClass>("Slot", out _));
        }

        [Test]
        public void CollectionWrites_RefuseARowThatImplementsOtherArguments()
        {
            var client = LoadClient();
            AddRow(client, "int-row", IntBoxId);
            var list = client.save.GetOrCreateCollection<NeoMemberListWritable>("Containers");
            var dictionary = client.save.GetOrCreateCollection<NeoMemberDictionaryWritable>("ByName");

            var add = Assert.Throws<InvalidOperationException>(() => list.AddSerialized(Reference("int-row")))!;
            var set = Assert.Throws<InvalidOperationException>(() =>
                dictionary.SetSerialized("a", Reference("int-row")))!;

            Assert.AreEqual(
                "Containers can't hold a IntBox, because IntBox implements IContainer<int>, not IContainer<string>.",
                add.Message);
            Assert.AreEqual(
                "ByName can't hold a IntBox, because IntBox implements IContainer<int>, not IContainer<string>.",
                set.Message);
            Assert.AreEqual(0, list.Count);
        }

        [Test]
        public void ClassSlotWrite_HoldsARowThroughAnyConstructionItsClassImplements()
        {
            var client = LoadClient();
            AddRow(client, "two-row", TwoBoxId);
            var holder = client.save;

            // TwoBox : IContainer<int>, IContainer<string> fits IContainer<string> by its second.
            holder.SetSerializedValue("Slot", Reference("two-row"));
            Assert.AreEqual(TwoBoxId, holder.Get<NeoMemberClass>("Slot").ClassId);
            ClassMember typeSlot = InterfaceSlot("member-type-slot", "TypeSlot", ObjectArgument(NeoObjectTypeKind.Type));
            NeoSlotAdmission refused = NeoGenericResolution.ConstructedSlotAccepts(client, typeSlot, TwoBoxId, NeoGenericResolution.EmptyEnv);
            Assert.AreEqual(
                "TypeSlot can't hold a TwoBox, because TwoBox implements IContainer<int> and IContainer<string>, not IContainer<Type>.",
                refused.reason);
        }

        [Test]
        public void ConstructedSlotAccepts_ConvertsACovariantArgument()
        {
            var client = LoadClient();
            ClassMember seeds = InterfaceSlotWithArgument("member-seeds", "Seeds", TestTypeBindings.Class(SeedId), ProducerId, ProducerX);
            ClassMember sprouts = InterfaceSlotWithArgument("member-sprouts", "Sprouts", TestTypeBindings.Class(SproutId), ProducerId, ProducerX);

            // IProducer<Sprout> converts to IProducer<Seed>, not back.
            Assert.IsTrue(NeoGenericResolution.ConstructedSlotAccepts(client, seeds, SproutMakerId, NeoGenericResolution.EmptyEnv).ok);
            Assert.AreEqual(
                "Sprouts can't hold a SeedMaker, because SeedMaker implements IProducer<Seed>, not IProducer<Sprout>.",
                NeoGenericResolution.ConstructedSlotAccepts(client, sprouts, SeedMakerId, NeoGenericResolution.EmptyEnv).reason);
        }

        [Test]
        public void ConstructedSlotAccepts_ComparesObjectTypeArguments()
        {
            var client = LoadClient();
            ClassMember slot = InterfaceSlot("member-type-slot", "TypeSlot", ObjectArgument(NeoObjectTypeKind.Type));

            NeoSlotAdmission admission = NeoGenericResolution.ConstructedSlotAccepts(
                client,
                slot,
                CultureBoxId,
                NeoGenericResolution.EmptyEnv);

            Assert.IsFalse(admission.ok);
            Assert.AreEqual(
                "TypeSlot can't hold a CultureBox, because CultureBox implements IContainer<CultureInfo>, not IContainer<Type>.",
                admission.reason);
        }

        [TestCase("generic-forward", TestName = "ConstructedSlotAccepts_ReadsAForwardedArgumentInItsContext")]
        [TestCase("generic-nested", TestName = "ConstructedSlotAccepts_ReadsANestedForwardedArgumentInItsContext")]
        public void ConstructedSlotAccepts_ReadsAnArgumentInItsContext(string shape)
        {
            var client = LoadClient();
            TypeBinding argument = shape == "generic-forward"
                ? HolderParam
                : TestTypeBindings.List(HolderParam);
            ClassMember slot = InterfaceSlotWithArgument("member-forward-slot", "Forwarded", argument);
            var open = new Dictionary<string, NeoGenericEnvEntry> { [HolderP] = NeoGenericEnvEntry.Unbound(HolderP) };
            var closed = new Dictionary<string, NeoGenericEnvEntry> { [HolderP] = NeoTypeBindings.Entry(StringArgument) };

            // An argument its context leaves open, at any depth, fits every implementer.
            Assert.IsTrue(NeoGenericResolution.ConstructedSlotAccepts(client, slot, IntBoxId, open).ok);
            Assert.IsTrue(NeoGenericResolution.ConstructedSlotAccepts(client, slot, CultureBoxId, open).ok);
            NeoSlotAdmission refused = NeoGenericResolution.ConstructedSlotAccepts(client, slot, IntBoxId, closed);

            string wanted = shape == "generic-forward" ? "string" : "List<string>";
            Assert.AreEqual(
                $"Forwarded can't hold a IntBox, because IntBox implements IContainer<int>, not IContainer<{wanted}>.",
                refused.reason);
            Assert.AreEqual(
                shape == "generic-forward",
                NeoGenericResolution.ConstructedSlotAccepts(client, slot, BoxId, closed).ok);
        }

        [Test]
        public void ClassSlotWrite_AdmitsClosedAndDeterminedRows()
        {
            var client = LoadClient();
            AddRow(client, "box-row", BoxId);
            AddRow(client, "open-row", OpenBoxId);
            var holder = client.save;

            holder.SetSerializedValue("Slot", Reference("box-row"));
            Assert.AreEqual(BoxId, holder.Get<NeoMemberClass>("Slot").ClassId);
            holder.SetSerializedValue("Slot", Reference("open-row"));
            Assert.AreEqual(OpenBoxId, holder.Get<NeoMemberClass>("Slot").ClassId);
        }

        [Test]
        public void ListWrites_RefuseAnUnstampedRowTheInterfaceLeavesOpen()
        {
            var client = LoadClient();
            AddRow(client, "box-row", BoxId);
            AddRow(client, "keyed-row", KeyedId);
            AddRow(client, "stamped-row", KeyedId, KeyedStamp());
            var list = client.save.GetOrCreateCollection<NeoMemberListWritable>("Containers");

            var add = Assert.Throws<InvalidOperationException>(() => list.AddSerialized(Reference("keyed-row")))!;
            Assert.AreEqual("Value \"keyed-row\" is a binding node and stores no stamp.", add.Message);
            list.AddSerialized(Reference("box-row"));
            var set = Assert.Throws<InvalidOperationException>(() => list.SetSerialized(0, Reference("keyed-row")))!;
            Assert.AreEqual("Value \"keyed-row\" is a binding node and stores no stamp.", set.Message);
            list.AddSerialized(Reference("stamped-row"));
            Assert.AreEqual(2, list.Count);
        }

        [Test]
        public void DictionaryWrite_RefusesAnUnstampedRowTheInterfaceLeavesOpen()
        {
            var client = LoadClient();
            AddRow(client, "keyed-row", KeyedId);
            var dictionary = client.save.GetOrCreateCollection<NeoMemberDictionaryWritable>("ByName");

            var error = Assert.Throws<InvalidOperationException>(() =>
                dictionary.SetSerialized("a", Reference("keyed-row")))!;

            Assert.AreEqual("Value \"keyed-row\" is a binding node and stores no stamp.", error.Message);
        }

        [Test]
        public void InterfaceSlot_ClassValueClonesAndReloads()
        {
            using var client = LoadClient();
            AddRow(client, "box-row", BoxId);
            client.save.SetSerializedValue("Slot", Reference("box-row"));
            string held = client.save.Get<NeoMemberClass>("Slot").value!.id;

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
            var cloned = clone.Get<NeoMemberClass>("Slot");
            Assert.AreEqual(BoxId, cloned.ClassId);
            Assert.AreNotEqual(held, cloned.value!.id);
            Assert.IsInstanceOf<TestBox>(NeoGeneratedTypesSupport.ResolveInterfaceValue<ITestContainer>(client, cloned, readOnly: false));

            using var reloaded = NeoTestSaveStack.ClientFromSchema(client.Synchronizer.Schema, loadedSaveContent: client.SerializeSaveData());
            var slot = reloaded.save.Get<NeoMemberClass>("Slot");
            Assert.AreEqual(BoxId, slot.ClassId);
            Assert.AreEqual(held, slot.value!.id);
        }

        [Test]
        public void ResolveInterfaceValue_UsesTheClassFactory()
        {
            var client = LoadClient();
            AddRow(client, "box-row", BoxId);
            client.save.SetSerializedValue("Slot", Reference("box-row"));
            var slot = client.save.Get<NeoMemberClass>("Slot");

            var value = NeoGeneratedTypesSupport.ResolveInterfaceValue<ITestContainer>(client, slot, readOnly: false);

            Assert.IsInstanceOf<TestBox>(value);
            Assert.IsFalse(((TestBox)value!).IsReadOnly);
            Assert.IsNull(NeoGeneratedTypesSupport.ResolveInterfaceValue<ITestContainer>(client, null, readOnly: false));
        }

        [Test]
        public void ResolveInterfaceValue_UsesTheRegisteredResolverForAnOpenRow()
        {
            var client = LoadClient();
            AddRow(client, "open-row", OpenBoxId);
            client.save.SetSerializedValue("Slot", Reference("open-row"));
            var slot = client.save.Get<NeoMemberClass>("Slot");

            var value = NeoGeneratedTypesSupport.ResolveInterfaceValue<ITestResolvedContainer>(
                client,
                slot,
                readOnly: true);

            Assert.IsInstanceOf<TestOpenBox>(value);
            Assert.IsTrue(((TestOpenBox)value!).IsReadOnly);
        }

        [Test]
        public void ResolveInterfaceValue_ThrowsForAnOpenRowNoConstructionNames()
        {
            var client = LoadClient();
            AddRow(client, "open-row", OpenBoxId);
            client.save.SetSerializedValue("Slot", Reference("open-row"));
            var slot = client.save.Get<NeoMemberClass>("Slot");

            var error = Assert.Throws<NeoTypeArgumentException>(() =>
                NeoGeneratedTypesSupport.ResolveInterfaceValue<ITestUnnamedContainer>(client, slot, readOnly: false))!;

            Assert.AreEqual(
                "A OpenBox row can't be read as ITestUnnamedContainer, because the project never names that construction.",
                error.Message);
        }

        [Test]
        public void InterfaceValueReference_RefusesACSharpClass()
        {
            var error = Assert.Throws<ArgumentException>(() =>
                NeoGeneratedTypesSupport.InterfaceValueReference(new NotNeo(), "Holder.Slot"))!;

            StringAssert.StartsWith("Holder.Slot needs a Neo class value, and NotNeo is a C# class.", error.Message);
            Assert.IsNull(NeoGeneratedTypesSupport.InterfaceValueReference(null, "Holder.Slot"));
        }

        [Test]
        public void TypeArguments_MapsAConstructedInterfaceInItsParamsOrder()
        {
            var client = LoadClient();
            client.TypeArguments.Register(
                new Dictionary<Type, string>(),
                new Dictionary<Type, string>(),
                new Dictionary<Type, string>
                {
                    [typeof(ITestContainer)] = "iface-plain-alias",
                    [typeof(IReadOnlyCollection<>)] = ContainerId,
                    [typeof(ITestPair<,>)] = ContainerId,
                });

            var info = (InterfaceTypeInfo)client.TypeArguments.Of<IReadOnlyCollection<string>>();

            Assert.AreEqual(ContainerId, info.interfaceId);
            Assert.AreEqual(MemberKind.String, info.typeArguments![ContainerT].type);
            Assert.AreEqual("iface-plain-alias", ((InterfaceTypeInfo)client.TypeArguments.Of<ITestContainer>()).interfaceId);
            var arity = Assert.Throws<NeoTypeArgumentException>(() =>
                client.TypeArguments.Of<ITestPair<int, int>>())!;
            StringAssert.EndsWith("has 2 arguments, and IContainer takes 1.", arity.Message);
        }

        private static NeoClient LoadClient()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData().WithUserRoot());
            client.RegisterGeneratedClassFactories(
                new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
                {
                    [BoxId] = (factoryClient, node) => new TestBox(factoryClient, node, readOnly: true),
                },
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
                {
                    [BoxId] = (factoryClient, node) => new TestBox(factoryClient, node, readOnly: false),
                });
            return client;
        }

        private sealed class ValueRef : INeoValueReference
        {
            internal ValueRef(string valueId) => this.valueId = valueId;

            public string? valueId
            {
                get;
            }
        }

        private static NeoValueWritePayload Reference(string valueId) =>
            NeoValueWritePayload.FromValueReference(valueId, null);

        private static void AddRow(
            NeoClient client,
            string id,
            string classId,
            Dictionary<string, TypeBinding>? genericBindings = null)
        {
            client.SetSaveValue(new ObjectMemberValue
            {
                id = id,
                classId = classId,
                createdAt = "x",
                updatedAt = "x",
                value = new Dictionary<string, string>(),
                genericBindings = genericBindings,
            });
        }

        /// <summary>The stamp a <c>Keyed&lt;int, string&gt;</c> row stores.</summary>
        private static Dictionary<string, TypeBinding> KeyedStamp() => new()
        {
            [KeyedK] = TestTypeBindings.Of(MemberKind.Int),
            [KeyedV] = StringArgument,
        };

        private static ProjectData BuildProjectData()
        {
            var container = new Interface
            {
                id = ContainerId,
                projectId = "project",
                name = "IContainer",
                members = new Dictionary<string, InterfaceMember>(),
                genericParams = new List<GenericParamDeclaration> { new() { id = ContainerT, name = "T" } },
            };
            return new ProjectData
            {
                project = new Project
                {
                    id = "project",
                    _id = "project",
                    name = "Interface Members",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                interfaces = new Dictionary<string, Interface>
                {
                    [ContainerId] = container,
                    [ProducerId] = new Interface
                    {
                        id = ProducerId,
                        projectId = "project",
                        name = "IProducer",
                        members = new Dictionary<string, InterfaceMember>(),
                        genericParams = new List<GenericParamDeclaration>
                        {
                            new() { id = ProducerX, name = "X", variance = NeoGenericParamVarianceKind.Out },
                        },
                    },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["class-root"] = Class("class-root", "Root"),
                    ["class-holder"] = Class("class-holder", "Holder", new Dictionary<string, string>
                    {
                        ["Slot"] = "member-slot",
                        ["Containers"] = "member-containers",
                        ["ByName"] = "member-by-name",
                    }),
                    [BoxId] = Implementing(Class(BoxId, "Box"), StringArgument),
                    [OpenBoxId] = Implementing(
                        Class(OpenBoxId, "OpenBox", genericParams: new[] { (OpenBoxU, "U") }),
                        Forward(OpenBoxU)),
                    [KeyedId] = Implementing(
                        Class(KeyedId, "Keyed", genericParams: new[] { (KeyedK, "K"), (KeyedV, "V") }),
                        Forward(KeyedV)),
                    [PlainId] = Class(PlainId, "Plain"),
                    [IntBoxId] = Implementing(Class(IntBoxId, "IntBox"), TestTypeBindings.Of(MemberKind.Int)),
                    [CultureBoxId] = Implementing(Class(CultureBoxId, "CultureBox"), ObjectArgument(NeoObjectTypeKind.CultureInfo)),
                    [TwoBoxId] = Implementing(Class(TwoBoxId, "TwoBox"), TestTypeBindings.Of(MemberKind.Int), StringArgument),
                    [SeedId] = Class(SeedId, "Seed"),
                    [SproutId] = Extending(Class(SproutId, "Sprout"), SeedId),
                    [SeedMakerId] = Producing(Class(SeedMakerId, "SeedMaker"), SeedId),
                    [SproutMakerId] = Producing(Class(SproutMakerId, "SproutMaker"), SproutId),
                    ["class-generic-holder"] = Class(
                        "class-generic-holder",
                        "GenericHolder",
                        genericParams: new[] { (HolderP, "P") }),
                },
                members = new Dictionary<string, Member>
                {
                    ["root-assets"] = RootMember("root-assets", "class-root"),
                    ["root-save"] = RootMember("root-save", "class-holder"),
                    ["root-session"] = RootMember("root-session", "class-root"),
                    ["member-slot"] = InterfaceSlot("member-slot", "Slot"),
                    ["member-containers"] = new ListMember
                    {
                        id = "member-containers",
                        projectId = "project",
                        name = "Containers",
                        kind = MemberKind.List,
                        entryMemberId = "member-container-entry",
                    },
                    ["member-by-name"] = new DictionaryMember
                    {
                        id = "member-by-name",
                        projectId = "project",
                        name = "ByName",
                        kind = MemberKind.Dictionary,
                        entryMemberId = "member-container-entry",
                    },
                    ["member-container-entry"] = InterfaceSlot("member-container-entry", "Container"),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = Row("root-assets-value", "class-root"),
                    ["root-save-value"] = Row("root-save-value", "class-holder"),
                    ["root-session-value"] = Row("root-session-value", "class-root"),
                },
            };
        }

        private static readonly TypeBinding StringArgument = TestTypeBindings.Of(MemberKind.String);
        private static readonly TypeBinding HolderParam = TestTypeBindings.Open("class-generic-holder", HolderP);

        // An interface arm comes from the wire, which marks its declared
        // fields the way the export does.
        private static ClassMember InterfaceSlot(string id, string name, TypeBinding? argument = null) =>
            InterfaceSlotWithArgument(id, name, argument ?? StringArgument);

        private static ClassMember InterfaceSlotWithArgument(
            string id,
            string name,
            TypeBinding argument,
            string interfaceId = ContainerId,
            string paramId = ContainerT)
        {
            return (ClassMember)JsonConvert.DeserializeObject<Member>(
                $"{{'id':'{id}','projectId':'project','name':'{name}','kind':7,'interfaceId':'{interfaceId}',"
                    + $"'interfaceArguments':{{'{paramId}':{JsonConvert.SerializeObject(argument)}}},"
                    + "'createdAt':'x','updatedAt':'x'}")!;
        }

        private static TypeBinding ObjectArgument(NeoObjectTypeKind objectType) =>
            TypeBinding.Closed(new TypeHead(MemberKind.Object, true, objectType: objectType));

        private static TypeBinding Forward(string paramId) =>
            TestTypeBindings.Open(paramId.Substring(0, paramId.LastIndexOf('-')), paramId);

        private static NeoSchemaClass Implementing(NeoSchemaClass schemaClass, params TypeBinding[] arguments)
        {
            schemaClass.implementsInterfaces = new List<InterfaceConstruction>();
            foreach (TypeBinding argument in arguments)
            {
                schemaClass.implementsInterfaces.Add(new InterfaceConstruction
                {
                    interfaceId = ContainerId,
                    arguments = new Dictionary<string, TypeBinding> { [ContainerT] = argument },
                });
            }
            return schemaClass;
        }

        private static NeoSchemaClass Producing(NeoSchemaClass schemaClass, string producedClassId)
        {
            schemaClass.implementsInterfaces = new List<InterfaceConstruction>
            {
                new()
                {
                    interfaceId = ProducerId,
                    arguments = new Dictionary<string, TypeBinding> { [ProducerX] = TestTypeBindings.Class(producedClassId) },
                },
            };
            return schemaClass;
        }

        private static NeoSchemaClass Extending(NeoSchemaClass schemaClass, string baseClassId)
        {
            schemaClass.extendsClassId = baseClassId;
            return schemaClass;
        }

        private static NeoSchemaClass Class(
            string id,
            string name,
            Dictionary<string, string>? schema = null,
            (string Id, string Name)[]? genericParams = null)
        {
            var schemaClass = new NeoSchemaClass
            {
                id = id,
                projectId = "project",
                name = name,
                schema = schema ?? new Dictionary<string, string>(),
            };
            if (genericParams is null)
                return schemaClass;
            schemaClass.genericParams = new List<GenericParamDeclaration>();
            foreach (var param in genericParams)
                schemaClass.genericParams.Add(new GenericParamDeclaration { id = param.Id, name = param.Name });
            return schemaClass;
        }

        private static ClassMember RootMember(string id, string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project",
                name = id,
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = id + "-value",
                DeclaredClassId = classId,
            };
        }

        private static ObjectMemberValue Row(string id, string classId) =>
            new()
            {
                id = id,
                classId = classId,
                createdAt = "x",
                updatedAt = "x",
                value = new Dictionary<string, string>(),
            };
    }
}
