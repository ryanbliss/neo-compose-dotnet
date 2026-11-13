// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// A NeoScript getter or function that returns a class value reads it
    /// whatever owns its row. The generated class member (saved family) and
    /// its explicit `IReadOnly*` implementation agree: an Asset row reads
    /// read-only, a Save row reads writable.
    /// </summary>
    public class NeoAssetOwnedClassReadTests
    {
        private const string ProjectId = "project-asset-owned-reads";
        private const string ArtClassId = "class-art";

        public enum Read
        {
            Getter,
            Method,
            StaticMethod,
        }

        public enum Row
        {
            Asset,
            Save,
            Session,
        }

        // `saved: true` is also the call a NeoScript temporary's stored class
        // member makes for the row it holds.
        [Test]
        public void AnAssetRowReadsReadOnly([Values] Read read, [Values] bool saved, [Values] bool required)
        {
            using NeoClient client = BuildClient();

            TestArt art = Project(client, Evaluate(client, read, Row.Asset), saved, required);

            Assert.AreEqual("v-asset-art", art.valueId);
            Assert.IsTrue(art.IsReadOnly);
            Assert.AreEqual(NeoValueOwnership.Asset, art.ValueOwnership);
            Assert.AreEqual("Asset label", art.Label);
        }

        [Test]
        public void AnOwnedRowReadsWritable(
            [Values] Read read,
            [Values(Row.Save, Row.Session)] Row row,
            [Values] bool saved,
            [Values] bool required)
        {
            using NeoClient client = BuildClient();

            TestArt art = Project(client, Evaluate(client, read, row), saved, required);

            Assert.AreEqual($"v-{row}-art".ToLowerInvariant(), art.valueId);
            Assert.IsFalse(art.IsReadOnly);
            Assert.AreEqual(row == Row.Save ? NeoValueOwnership.Save : NeoValueOwnership.Session, art.ValueOwnership);
            Assert.AreEqual($"{row} label", art.Label);
        }

        // The class member reads `saved: true`, its `IReadOnly*` implementation `saved: false`.
        [Test]
        public void TheClassMemberAndItsReadOnlyInterfaceShareOneView([Values] Row row)
        {
            using NeoClient client = BuildClient();
            object? result = Evaluate(client, Read.Getter, row);

            Assert.AreSame(
                Project(client, result, saved: true, required: true),
                Project(client, result, saved: false, required: true));
        }

        // Generated: ReadTypeArgument<T>(client, ReadRequiredNSPropertyClass(client, result, false, …), …).
        [Test]
        public void AGenericResultReadsByOwnership([Values] Read read, [Values] Row row)
        {
            using NeoClient client = BuildClient();

            TestArt art = NeoGeneratedTypesSupport.ReadTypeArgument<TestArt>(
                client,
                Project(client, Evaluate(client, read, row), saved: false, required: true),
                "T",
                new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>(),
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());

            Assert.AreEqual(row == Row.Asset, art.IsReadOnly);
        }

        // Generated: ReadScriptList<T>(result, entry => ReadRequiredNSPropertyClass(client, entry, true, …)).
        [Test]
        public void ListEntriesReadByTheirOwnOwnership()
        {
            using NeoClient client = BuildClient();
            object?[] entries =
            {
                Evaluate(client, Read.Getter, Row.Asset),
                Evaluate(client, Read.Getter, Row.Save),
            };

            IReadOnlyList<TestArt> arts = NeoGeneratedTypesSupport.ReadScriptList(
                entries,
                entry => Project(client, entry, saved: true, required: true));

            Assert.IsTrue(arts[0].IsReadOnly);
            Assert.IsFalse(arts[1].IsReadOnly);
        }

        [Test]
        public void AClassWithoutAWritableFamilyReadsASaveRowReadOnly()
        {
            using NeoClient client = BuildClient();

            TestArt art = NeoGeneratedTypesSupport.ReadRequiredNSPropertyClass(
                client,
                Evaluate(client, Read.Getter, Row.Save),
                true,
                TestArt.Create,
                null);

            Assert.IsTrue(art.IsReadOnly);
            Assert.AreEqual("Save label", art.Label);
        }

        [Test]
        public void StaleGeneratedCodeWithoutAReadOnlyFactoryNamesTheFix()
        {
            using NeoClient client = BuildClient();
            object? result = Evaluate(client, Read.Getter, Row.Asset);

            var error = Assert.Throws<InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.ReadRequiredNSPropertyClass(client, result, true, null, TestArt.CreateWritable));

            StringAssert.Contains("Asset-owned class value 'v-asset-art'", error!.Message);
            StringAssert.Contains("neo export", error.Message);
        }

        private static TestArt Project(NeoClient client, object? result, bool saved, bool required)
        {
            if (required)
            {
                return NeoGeneratedTypesSupport.ReadRequiredNSPropertyClass(
                    client, result, saved, TestArt.Create, TestArt.CreateWritable, TestArt.CreateDetached);
            }
            return NeoGeneratedTypesSupport.ReadNSPropertyClass(
                    client, result, false, saved, TestArt.Create, TestArt.CreateWritable, TestArt.CreateDetached)
                ?? throw new AssertionException("The nullable read returned null.");
        }

        /// <summary>The evaluator's result, read through the member a generated body calls.</summary>
        private static object? Evaluate(NeoClient client, Read read, Row row)
        {
            string key = $"{read}{row}";
            switch (read)
            {
                case Read.Getter:
                    NSGetterResult computed = client.save.Get<NeoMemberNSProperty>(key).Compute("v-root-save");
                    computed.ThrowIfFailed();
                    return computed.value;
                case Read.Method:
                    return client.save.Get<NeoMemberNSFunction>(key).Invoke("v-root-save", Array.Empty<object?>());
                case Read.StaticMethod:
                    return NeoMemberNSFunction.GetOrCreateStatic(client, MemberId(read, row)).InvokeStatic();
                default:
                    throw new ArgumentOutOfRangeException(nameof(read), read, null);
            }
        }

        private static string MemberId(Read read, Row row) => $"member-{read}-{row}".ToLowerInvariant();

        private static string RootRowId(Row row) => row == Row.Asset ? "v-root-assets" : $"v-root-{row}".ToLowerInvariant();

        private static NeoClient BuildClient()
        {
            var rootSchema = new Dictionary<string, string> { ["Art"] = "member-art" };
            var members = new Dictionary<string, Member>
            {
                ["root-assets"] = ClassMemberOf("root-assets", "class-root", "v-root-assets"),
                ["root-save"] = ClassMemberOf("root-save", "class-root", "v-root-save"),
                ["root-session"] = ClassMemberOf("root-session", "class-root", "v-root-session"),
                ["member-art"] = ClassMemberOf("member-art", ArtClassId),
                ["member-label"] = new StringMember
                {
                    id = "member-label",
                    projectId = ProjectId,
                    name = "Label",
                    kind = MemberKind.String,
                    Requirement = NeoMemberRequirementKind.Required,
                },
            };
            foreach (Read read in System.Enum.GetValues(typeof(Read)))
            {
                foreach (Row row in System.Enum.GetValues(typeof(Row)))
                {
                    Member member = ReturnArt(read, MemberId(read, row), RootRowId(row));
                    members[member.id] = member;
                    rootSchema[$"{read}{row}"] = member.id;
                }
            }
            var data = new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    _id = ProjectId,
                    name = "Asset-owned reads",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = members,
                values = new Dictionary<string, MemberValue>
                {
                    ["v-root-assets"] = ObjectRow("v-root-assets", "class-root", "Art", "v-asset-art"),
                    ["v-root-save"] = ObjectRow("v-root-save", "class-root", "Art", "v-save-art"),
                    ["v-root-session"] = ObjectRow("v-root-session", "class-root", "Art", "v-session-art"),
                    ["v-asset-art"] = ObjectRow("v-asset-art", ArtClassId, "Label", "v-asset-label"),
                    ["v-save-art"] = ObjectRow("v-save-art", ArtClassId, "Label", "v-save-label"),
                    ["v-session-art"] = ObjectRow("v-session-art", ArtClassId, "Label", "v-session-label"),
                    ["v-asset-label"] = new StringMemberValue { id = "v-asset-label", value = "Asset label" },
                    ["v-save-label"] = new StringMemberValue { id = "v-save-label", value = "Save label" },
                    ["v-session-label"] = new StringMemberValue { id = "v-session-label", value = "Session label" },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["class-root"] = new NeoSchemaClass { id = "class-root", projectId = ProjectId, name = "Root", schema = rootSchema },
                    [ArtClassId] = new NeoSchemaClass
                    {
                        id = ArtClassId,
                        projectId = ProjectId,
                        name = "Art",
                        schema = new Dictionary<string, string> { ["Label"] = "member-label" },
                    },
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            }.WithUserRoot();
            return NeoTestSaveStack.ClientFromSchema(data);
        }

        /// <summary>`Art => root.Art;` as a getter, a method, or a static method.</summary>
        private static Member ReturnArt(Read read, string id, string rootRowId)
        {
            var artType = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = ArtClassId };
            var body = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new[]
                {
                    new Variable { id = "__this__" },
                    new Variable { id = "__root__" },
                },
                typeInfo = artType,
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = NeoCellPatternRuntimeTests.Field(rootRowId, "Art"),
                    },
                },
            };
            if (read == Read.Getter)
            {
                return new NSPropertyMember
                {
                    id = id,
                    projectId = ProjectId,
                    name = id,
                    kind = MemberKind.NSProperty,
                    returnTypeInfo = artType,
                    getter = body,
                };
            }
            return new NSFunctionMember
            {
                id = id,
                projectId = ProjectId,
                name = id,
                kind = MemberKind.NSFunction,
                Modifier = read == Read.StaticMethod ? NeoMemberModifierKind.Static : NeoMemberModifierKind.Virtual,
                code = "return Root.Art;",
                returnTypeInfo = artType,
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                Dispatch = NeoFunctionDispatchKind.Synchronous,
                action = body,
            };
        }

        private static ClassMember ClassMemberOf(string id, string classId, string? valueId = null) => new()
        {
            id = id,
            projectId = ProjectId,
            name = id,
            kind = MemberKind.Class,
            Requirement = NeoMemberRequirementKind.Required,
            valueId = valueId,
            DeclaredClassId = classId,
        };

        private static ObjectMemberValue ObjectRow(string id, string classId, string key, string childId) => new()
        {
            id = id,
            classId = classId,
            value = new Dictionary<string, string> { [key] = childId },
        };

        /// <summary>The shape a generated class takes: one C# type, read-only or writable by factory.</summary>
        private sealed class TestArt : NeoGeneratedClassValue
        {
            private TestArt(NeoClient client, NeoMemberClass node, bool isReadOnly, NeoValueOwnership ownership)
                : base(client, node, ArtClassId, isReadOnly, ownership)
            {
            }

            private TestArt(NeoClient client, NeoDetachedValue value, bool isReadOnly)
                : base(client, value, isReadOnly)
            {
            }

            internal static TestArt Create(NeoClient client, NeoMemberClass node) =>
                NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                    client,
                    node,
                    static (factoryClient, factoryNode) => new TestArt(factoryClient, factoryNode, true, NeoValueOwnership.Asset));

            internal static TestArt CreateWritable(NeoClient client, NeoMemberClassWritable node) =>
                NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                    client,
                    node,
                    static (factoryClient, factoryNode) => new TestArt(factoryClient, factoryNode, false, factoryNode.ownership));

            internal static TestArt? CreateDetached(NeoClient client, NeoDetachedValue value, bool saved) =>
                new(client, value, !saved);

            public string Label => node.Get<NeoMemberString>("Label").value!.value;
        }
    }
}
