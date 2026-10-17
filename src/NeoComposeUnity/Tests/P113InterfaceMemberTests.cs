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
    /// closed-or-determined check, and the generated interface read and write
    /// helpers. <c>IContainer&lt;T&gt;</c> is implemented by a closed
    /// <c>Box</c>, an <c>OpenBox&lt;U&gt;</c> it determines, a
    /// <c>Keyed&lt;K, V&gt;</c> it can't determine, and not by <c>Plain</c>.
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
            Assert.IsTrue(open.Admits);
            Assert.AreEqual(OpenBoxU, open.paramByPosition![ContainerT]);
            Assert.IsFalse(implementers.Determination(KeyedId, ContainerId).Admits);
            Assert.AreEqual(
                NeoInterfaceDeterminationKind.NotImplemented,
                implementers.Determination(PlainId, ContainerId).kind);
            CollectionAssert.AreEqual(new[] { BoxId, OpenBoxId }, implementers.Implementers(ContainerId));
        }

        [Test]
        public void UndeterminedRowMessage_NamesTheParameterTheInterfaceLeavesOpen()
        {
            var client = LoadClient();

            Assert.AreEqual(
                "Holder.Slot can't hold a Keyed, because IContainer doesn't determine Keyed's parameter K.",
                client.InterfaceImplementers.UndeterminedRowMessage("Holder.Slot", KeyedId, ContainerId));
            Assert.AreEqual(
                "Holder.Slot can't hold a Plain, because Plain doesn't implement IContainer.",
                client.InterfaceImplementers.UndeterminedRowMessage("Holder.Slot", PlainId, ContainerId));
            Assert.IsNull(client.InterfaceImplementers.UndeterminedRowMessage("Holder.Slot", BoxId, ContainerId));
        }

        [Test]
        public void SlotClassArguments_ReadsTheSlotsArgumentsByPositionForADeterminedRow()
        {
            var client = LoadClient();
            Assert.IsTrue(client.TryGetMember("member-slot", out ClassMember? slot));

            var open = client.InterfaceImplementers.SlotClassArguments(slot!, OpenBoxId);
            Assert.IsNotNull(open);
            Assert.AreEqual("member-string", open![OpenBoxU].memberId);
            Assert.IsNull(client.InterfaceImplementers.SlotClassArguments(slot!, BoxId));
            Assert.AreSame(open, client.InterfaceImplementers.SlotClassArguments(slot!, OpenBoxId));
        }

        [Test]
        public void ClassSlotWrite_RefusesAnUndeterminedRow()
        {
            var client = LoadClient();
            AddRow(client, "keyed-row", KeyedId);
            var holder = client.save;

            var error = Assert.Throws<InvalidOperationException>(() =>
                holder.SetSerializedValue("Slot", Reference("keyed-row")))!;

            Assert.AreEqual(
                "Holder.Slot can't hold a Keyed, because IContainer doesn't determine Keyed's parameter K.",
                error.Message);
            Assert.IsFalse(holder.TryGet<NeoMemberClass>("Slot", out _));
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
        public void ListWrites_RefuseAnUndeterminedRow()
        {
            var client = LoadClient();
            AddRow(client, "box-row", BoxId);
            AddRow(client, "keyed-row", KeyedId);
            var list = client.save.GetOrCreateCollection<NeoMemberListWritable>("Containers");

            var add = Assert.Throws<InvalidOperationException>(() => list.AddSerialized(Reference("keyed-row")))!;
            StringAssert.StartsWith("Containers can't hold a Keyed", add.Message);
            list.AddSerialized(Reference("box-row"));
            var set = Assert.Throws<InvalidOperationException>(() => list.SetSerialized(0, Reference("keyed-row")))!;
            StringAssert.StartsWith("Containers can't hold a Keyed", set.Message);
            Assert.AreEqual(1, list.Count);
        }

        [Test]
        public void DictionaryWrite_RefusesAnUndeterminedRow()
        {
            var client = LoadClient();
            AddRow(client, "keyed-row", KeyedId);
            var dictionary = client.save.GetOrCreateCollection<NeoMemberDictionaryWritable>("ByName");

            var error = Assert.Throws<InvalidOperationException>(() =>
                dictionary.SetSerialized("a", Reference("keyed-row")))!;

            StringAssert.StartsWith("ByName can't hold a Keyed", error.Message);
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

        private static NeoValueWritePayload Reference(string valueId) =>
            NeoValueWritePayload.FromValueReference(valueId, null);

        private static void AddRow(NeoClient client, string id, string classId)
        {
            client.SetSaveValue(new ObjectMemberValue
            {
                id = id,
                classId = classId,
                createdAt = "x",
                updatedAt = "x",
                value = new Dictionary<string, string>(),
            });
        }

        private static ProjectData BuildProjectData()
        {
            var stringArgument = new GenericBinding { kind = NeoGenericBindingKind.Member, memberId = "member-string" };
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
                interfaces = new Dictionary<string, Interface> { [ContainerId] = container },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["class-root"] = Class("class-root", "Root"),
                    ["class-holder"] = Class("class-holder", "Holder", new Dictionary<string, string>
                    {
                        ["Slot"] = "member-slot",
                        ["Containers"] = "member-containers",
                        ["ByName"] = "member-by-name",
                    }),
                    [BoxId] = Implementing(Class(BoxId, "Box"), stringArgument),
                    [OpenBoxId] = Implementing(
                        Class(OpenBoxId, "OpenBox", genericParams: new[] { (OpenBoxU, "U") }),
                        Forward(OpenBoxU)),
                    [KeyedId] = Implementing(
                        Class(KeyedId, "Keyed", genericParams: new[] { (KeyedK, "K"), (KeyedV, "V") }),
                        Forward(KeyedV)),
                    [PlainId] = Class(PlainId, "Plain"),
                },
                members = new Dictionary<string, Member>
                {
                    ["root-assets"] = RootMember("root-assets", "class-root"),
                    ["root-save"] = RootMember("root-save", "class-holder"),
                    ["root-session"] = RootMember("root-session", "class-root"),
                    ["member-string"] = new StringMember
                    {
                        id = "member-string",
                        projectId = "project",
                        name = "Text",
                        kind = MemberKind.String,
                    },
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

        // An interface arm comes from the wire, which marks its declared
        // fields the way the export does.
        private static ClassMember InterfaceSlot(string id, string name)
        {
            return (ClassMember)JsonConvert.DeserializeObject<Member>(
                $"{{'id':'{id}','projectId':'project','name':'{name}','kind':7,'interfaceId':'{ContainerId}',"
                    + $"'interfaceArguments':{{'{ContainerT}':{{'kind':1,'memberId':'member-string'}}}},"
                    + "'createdAt':'x','updatedAt':'x'}")!;
        }

        private static GenericBinding Forward(string paramId) =>
            new()
            {
                kind = NeoGenericBindingKind.Generic,
                genericParamId = paramId
            };

        private static NeoSchemaClass Implementing(NeoSchemaClass schemaClass, GenericBinding argument)
        {
            schemaClass.implementsInterfaceIds = new List<string> { ContainerId };
            schemaClass.implementsInterfaceArguments = new Dictionary<string, Dictionary<string, GenericBinding>>
            {
                [ContainerId] = new() { [ContainerT] = argument },
            };
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
