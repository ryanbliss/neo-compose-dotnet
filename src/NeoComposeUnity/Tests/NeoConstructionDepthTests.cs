// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P43 §7.2.3 — the construction depth cap on the <b>schema-derived</b>
    /// <c>classConstructor</c> arm.
    ///
    /// <para>The declared-constructor arm has its own coverage in
    /// <see cref="NeoDeclaredConstructorTests"/>. This file exists because the
    /// two arms reach the same materializer by different routes: the
    /// <c>classConstructor</c> arm builds member defaults too, and a member
    /// default may be an initializer that constructs again. If that route does
    /// not carry the caller's live evaluator context, every level starts a
    /// fresh construction stack, no cap can ever fire, and a recursive graph
    /// becomes unbounded native recursion — a StackOverflowException, which
    /// .NET cannot catch and which takes the player down with it.</para>
    ///
    /// <para>The graph here is a finite chain rather than a cycle on purpose:
    /// it proves the frames accumulate ACROSS levels while still terminating on
    /// a runtime that has no cap at all, so the test can be run against both
    /// the broken and the fixed runtime.</para>
    /// </summary>
    public class NeoConstructionDepthTests
    {
        private const string ProjectId = "depth-project";

        /// <summary>
        /// Long enough to exceed the 64-frame cap: each link costs two frames
        /// (the class, then its member initializer), matching the TypeScript
        /// evaluator's accounting.
        /// </summary>
        private const int ChainLength = 40;

        [Test]
        public void ClassConstructor_InitializerRecursionTripsTheDepthCap()
        {
            NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    ReturnFunction(ClassConstructorPointer(0)),
                    new NSGetterEvaluator.Context(client, null, null)))!;

            StringAssert.Contains("construction depth exceeded 64", error.Message);
            // The chain names what recursed, alternating class and initializer
            // exactly as `pushConstructionFrame` labels them on the TS side.
            StringAssert.Contains("Chain0 -> Next initializer -> Chain1", error.Message);
        }

        [Test]
        public void ClassConstructor_ShallowInitializerRecursionStillConstructs()
        {
            // The cap must not become a blanket rejection of nesting: the same
            // graph two links deep constructs normally.
            NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            object? result = NSGetterEvaluator.Evaluate(
                ReturnFunction(ClassConstructorPointer(ChainLength - 2)),
                ctx);

            string? valueId = NSGetterEvaluator.FindRowIdByReference(result, ctx);
            Assert.IsNotNull(valueId, "Constructed value has no backing row.");
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                valueId!,
                out ObjectMemberValue? row));
            Assert.IsTrue(
                row!.value!.ContainsKey("Next"),
                "The member initializer's product must be attached.");
        }

        // -------------------------------------------------------------------
        // IR builders.
        // -------------------------------------------------------------------

        private static FunctionWithReturnType ReturnFunction(Pointer pointer)
        {
            return new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = ChainType(0),
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = pointer,
                    },
                },
            };
        }

        private static ClassTypeInfo ChainType(int index)
        {
            return new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = ChainClassId(index),
            };
        }

        private static FunctionPointer ClassConstructorPointer(int index)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new ClassConstructorFunction
                {
                    type = FunctionKind.ClassConstructor,
                    info = new FunctionClassConstructorInfo
                    {
                        schemaClassInfo = ChainType(index),
                        fields = Array.Empty<FunctionClassConstructorField>(),
                    },
                },
            };
        }

        private static string ChainClassId(int index) => $"chain-class-{index}";

        private static string ChainMemberId(int index) => $"chain-next-{index}";

        // -------------------------------------------------------------------
        // Schema: Chain0 -> Chain1 -> … -> Chain{ChainLength-1}, each link an
        // init-backed member default that constructs the next class.
        // -------------------------------------------------------------------

        private static ProjectData BuildProjectData()
        {
            var classes = new Dictionary<string, NeoSchemaClass>();
            var members = new Dictionary<string, JsonMember>();

            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = ProjectId,
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            classes[rootClass.id] = rootClass;

            for (int index = 0; index < ChainLength; index++)
            {
                bool isLast = index == ChainLength - 1;
                var schema = new Dictionary<string, string>();
                if (!isLast)
                {
                    schema["Next"] = ChainMemberId(index);
                    members[ChainMemberId(index)] = new ClassMember
                    {
                        id = ChainMemberId(index),
                        projectId = ProjectId,
                        name = "Next",
                        kind = MemberKind.Class,
                        Requirement = NeoMemberRequirementKind.Required,
                        classId = ChainClassId(index + 1),
                        defaultValue = new ObjectMemberValueBase
                        {
                            init = new InitializerBody
                            {
                                code = "new()",
                                compiled = new FunctionWithReturnType
                                {
                                    compilerRevision =
                                        FunctionWithReturnType.CurrentCompilerRevision,
                                    parameters = Array.Empty<Variable>(),
                                    typeInfo = ChainType(index + 1),
                                    instructions = new Instruction[]
                                    {
                                        new ReturnInstruction
                                        {
                                            type = InstructionKind.Return,
                                            pointer = ClassConstructorPointer(index + 1),
                                        },
                                    },
                                },
                            },
                        },
                    };
                }
                classes[ChainClassId(index)] = new NeoSchemaClass
                {
                    id = ChainClassId(index),
                    projectId = ProjectId,
                    name = $"Chain{index}",
                    schema = schema,
                };
            }

            ClassMember rootAssets = RootMember("root-assets", "Assets", NeoMemberStorage.Immutable, "value-assets");
            ClassMember rootSave = RootMember("root-save", "Save", NeoMemberStorage.Save, "value-save");
            ClassMember rootSession = RootMember("root-session", "Session", NeoMemberStorage.Session, "value-session");
            members[rootAssets.id] = rootAssets;
            members[rootSave.id] = rootSave;
            members[rootSession.id] = rootSession;

            return new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "Construction depth tests",
                    rootAssetsMemberId = rootAssets.id,
                    rootSaveFileMemberId = rootSave.id,
                    rootSessionMemberId = rootSession.id,
                },
                members = members,
                values = new Dictionary<string, MemberValue>
                {
                    ["value-assets"] = ObjectValue("value-assets", rootClass.id),
                    ["value-save"] = ObjectValue("value-save", rootClass.id),
                    ["value-session"] = ObjectValue("value-session", rootClass.id),
                },
                classes = classes,
                constructors = new Dictionary<string, ConstructorRecord>(),
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
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
