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
    /// <para>Only binding nodes carry a <c>genericBindings</c> stamp, so a
    /// Class instance such as
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

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(true, false, true)]
        public void SaveSweep_TypesAGenericInstancesConstructorArgumentsThroughItsPlacement(
            bool variantArgument, bool sourceInSession, bool cloneToSession)
        {
            var data = BuildProjectData();
            if (variantArgument)
            {
                data.classes["variant-class"] = new NeoSchemaClass
                {
                    id = "variant-class",
                    projectId = ProjectId,
                    name = "NeoVariant",
                    schema = new Dictionary<string, string>(),
                    system = new JObject { ["worldKind"] = "variant" },
                    genericParams = new List<GenericParamDeclaration> { new() { id = "target", name = "TObject" } },
                };
                data.constructors["watcher-constructor"].argumentTypes = new[]
                {
                    new FunctionArgumentTypeInfo { name = "initial", type = MemberKind.Class, required = true,
                        classId = "variant-class", typeArguments = new Dictionary<string, TypeInfo>
                        { ["target"] = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "modifier-class" } } },
                };
            }
            data.constructors["watcher-constructor"].action.parameters[2].typeInfo =
                data.constructors["watcher-constructor"].argumentTypes[0];
            data.classes["modifier-class"].schema["Amount"] = "modifier-amount";
            data.members["modifier-amount"] = new IntMember
            {
                id = "modifier-amount",
                projectId = ProjectId,
                name = "Amount",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var sourceOwnership = sourceInSession ? NeoValueOwnership.Session : NeoValueOwnership.Save;
            client.SetWritableValue(sourceOwnership, new NumberMemberValue { id = "modifier-amount-row", value = 7 });

            // The live graph: the save root holds a Watcher<Modifier> whose
            // constructor replays a Save-owned Modifier. The watcher row is
            // written last so its placement is already linked -- exactly the
            // state the sweep walks after a save loads.
            client.SetWritableValue(
                sourceOwnership,
                new ObjectMemberValue
                {
                    id = ModifierValueId,
                    classId = "modifier-class",
                    value = new Dictionary<string, string> { ["Amount"] = "modifier-amount-row" },
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
                        ["__arg_0__"] = variantArgument
                            ? JObject.FromObject(new VariantRefValue { classId = "modifier-class", variantId = "named-variant", rowValueId = ModifierValueId })
                            : new JValue(ModifierValueId),
                    },
                    value = new Dictionary<string, string>(),
                });

            Assert.IsTrue(client.TryGetWritableValue(NeoValueOwnership.Save, ModifierValueId, out MemberValue? retained));
            Assert.IsTrue(client.TryGetWritableValue(NeoValueOwnership.Save, "modifier-amount-row", out MemberValue? retainedChild));
            Assert.AreEqual(7, ((NumberMemberValue)retainedChild!).value);
            if (cloneToSession)
            {
                client.CloneOwnedValueReferenceForNewParent(NeoValueOwnership.Session, NeoValueOwnership.Save,
                    WatcherValueId, data.members["member-quantity"]);
                Assert.IsTrue(client.TryGetWritableValue(NeoValueOwnership.Session, ModifierValueId, out _));
                Assert.IsTrue(client.TryGetWritableValue(NeoValueOwnership.Session, "modifier-amount-row", out _));
            }

            IReadOnlyList<string> unlinked = client.FindUnlinkedSaveValueIds();

            // The generic parameter resolves to Modifier through the
            // placement, so the row it names is retained rather than reported
            // unlinked -- and typing it no longer throws.
            CollectionAssert.IsEmpty(unlinked);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SaveSweepDoesNotTypeDetachedScalarOrOmittedConstructorArguments(bool hasScalarArgument)
        {
            var data = BuildProjectData();
            data.classes["watcher-class"].allowedStorage = NeoMemberStorage.Save;
            var row = ObjectValue(WatcherValueId, "watcher-class");
            row.instanceConstructorId = "watcher-constructor";
            row.constructorArgs = new Dictionary<string, JToken?>();
            if (hasScalarArgument)
                row.constructorArgs["__arg_0__"] = new JValue(60);
            data.values[row.id] = row;
            // A detached authored animation frame has no closed placement.
            // Scalar and omitted arguments cannot reference retained rows.
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.IsFalse(client.TryInferMemberForValueId(row.id, out _));
            Assert.DoesNotThrow(() => client.FindUnlinkedSaveValueIds());
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
                        DeclaredClassId = watcherClass.id,
                        Storage = NeoMemberStorage.Save,
                        Requirement = NeoMemberRequirementKind.Optional,
                        classArguments = new Dictionary<string, TypeBinding>
                        {
                            [ParamT] = TestTypeBindings.Class("modifier-class"),
                        },
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
            }.WithUserRoot();
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
                DeclaredClassId = "root-class",
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
