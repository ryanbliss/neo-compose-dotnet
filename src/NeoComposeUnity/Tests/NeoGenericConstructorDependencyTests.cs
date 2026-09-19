// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// The save reachability sweep types every constructor argument so it can
    /// retain the rows a recipe replays from. Typing them means resolving the
    /// constructor's declared parameters under the receiver's binding
    /// environment — and for a GENERIC class that environment cannot come from
    /// the row.
    ///
    /// <para>Only List and Dictionary rows carry a <c>genericBindings</c>
    /// stamp (<see cref="NeoGenericResolution.StampGenericBindings"/> no-ops
    /// for every other kind), so a Class instance such as
    /// <c>ValueWatcher&lt;int&gt;</c> closes its parameters through the
    /// PLACEMENT that declares it. When the sweep dropped that placement, every
    /// generic parameter resolved unbound and the walk threw
    /// "Generic NSFunction type '…' is unbound for this receiver." mid-commit
    /// — which took the whole save with it.</para>
    /// </summary>
    public class NeoGenericConstructorDependencyTests
    {
        private const string ProjectId = "generic-ctor-project";
        private const string ParamT = "param-t";
        private const string WatcherValueId = "watcher-row";
        private const string ModifierValueId = "watcher-modifier-row";

        [Test]
        public void SaveSweep_TypesAGenericInstancesConstructorArgumentsThroughItsPlacement()
        {
            NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());

            // The live graph: the save root holds a Watcher<Modifier> whose
            // constructor replays a Save-owned Modifier. The watcher row is
            // written last so its placement is already linked -- exactly the
            // state the sweep walks after a save loads.
            client.SetWritableValue(
                NeoValueOwnership.Save,
                new ObjectMemberValue
                {
                    id = ModifierValueId,
                    classId = "modifier-class",
                    value = new Dictionary<string, string>(),
                });
            client.SetWritableValue(
                NeoValueOwnership.Save,
                new ObjectMemberValue
                {
                    id = "value-save",
                    classId = "root-class",
                    value = new Dictionary<string, string> { ["Quantity"] = WatcherValueId },
                });
            client.SetWritableValue(
                NeoValueOwnership.Save,
                new ObjectMemberValue
                {
                    id = WatcherValueId,
                    classId = "watcher-class",
                    instanceConstructorId = "watcher-constructor",
                    constructorArgs = new Dictionary<string, JToken?>
                    {
                        ["__arg_0__"] = new JValue(ModifierValueId),
                    },
                    value = new Dictionary<string, string>(),
                });

            IReadOnlyList<string> unlinked = client.FindUnlinkedSaveValueIds();

            // The generic parameter resolves to Modifier through the
            // placement, so the row it names is retained rather than reported
            // unlinked -- and typing it no longer throws.
            CollectionAssert.IsEmpty(unlinked);
        }

        private static ProjectData BuildProjectData()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = ProjectId,
                name = "Root",
                schema = new Dictionary<string, string> { ["Quantity"] = "member-quantity" },
            };
            var watcherClass = new NeoSchemaClass
            {
                id = "watcher-class",
                projectId = ProjectId,
                name = "Watcher",
                schema = new Dictionary<string, string>(),
                constructorIds = new[] { "watcher-constructor" },
                genericParams = new List<GenericParamDeclaration>
                {
                    new() { id = ParamT, name = "T" },
                },
            };
            var modifierClass = new NeoSchemaClass
            {
                id = "modifier-class",
                projectId = ProjectId,
                name = "Modifier",
                schema = new Dictionary<string, string>(),
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "Generic constructor dependencies",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, JsonMember>
                {
                    ["root-assets"] = RootMember("root-assets", "Assets", NeoMemberStorage.Immutable, "value-assets"),
                    ["root-save"] = RootMember("root-save", "Save", NeoMemberStorage.Save, "value-save"),
                    ["root-session"] = RootMember("root-session", "Session", NeoMemberStorage.Session, "value-session"),
                    // The placement that closes T. Nothing else can.
                    ["member-quantity"] = new ClassMember
                    {
                        id = "member-quantity",
                        projectId = ProjectId,
                        name = "Quantity",
                        kind = MemberKind.Class,
                        classId = watcherClass.id,
                        Storage = NeoMemberStorage.Save,
                        Requirement = NeoMemberRequirementKind.Optional,
                        classArguments = new Dictionary<string, GenericBinding>
                        {
                            [ParamT] = new()
                            {
                                kind = NeoGenericBindingKind.Member,
                                memberId = "member-binding-modifier",
                            },
                        },
                    },
                    ["member-binding-modifier"] = new ClassMember
                    {
                        id = "member-binding-modifier",
                        projectId = ProjectId,
                        name = "ModifierBinding",
                        kind = MemberKind.Class,
                        classId = "modifier-class",
                        Requirement = NeoMemberRequirementKind.Required,
                    },
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["value-assets"] = ObjectValue("value-assets", "root-class"),
                    ["value-save"] = ObjectValue("value-save", "root-class"),
                    ["value-session"] = ObjectValue("value-session", "root-class"),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [watcherClass.id] = watcherClass,
                    [modifierClass.id] = modifierClass,
                },
                constructors = new Dictionary<string, ConstructorRecord>
                {
                    ["watcher-constructor"] = new ConstructorRecord
                    {
                        id = "watcher-constructor",
                        projectId = ProjectId,
                        classId = watcherClass.id,
                        argumentTypes = new[] { InitialParameter() },
                        action = new FunctionWithReturnType
                        {
                            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                            typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                            parameters = new[]
                            {
                                new Variable { id = "__this__" },
                                new Variable { id = "__root__" },
                                new Variable { id = "__arg_0__", typeInfo = InitialParameter() },
                            },
                            instructions = Array.Empty<Instruction>(),
                        },
                    },
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static FunctionArgumentTypeInfo InitialParameter()
        {
            return new FunctionArgumentTypeInfo
            {
                name = "initial",
                type = MemberKind.Generic,
                required = true,
                ownerClassId = "watcher-class",
                genericParamId = ParamT,
            };
        }

        private static ClassMember RootMember(
            string id,
            string name,
            NeoMemberStorage storage,
            string valueId)
        {
            return new ClassMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                classId = "root-class",
                Storage = storage,
                valueId = valueId,
            };
        }

        private static ObjectMemberValue ObjectValue(string id, string classId)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = new Dictionary<string, string>(),
            };
        }
    }
}
