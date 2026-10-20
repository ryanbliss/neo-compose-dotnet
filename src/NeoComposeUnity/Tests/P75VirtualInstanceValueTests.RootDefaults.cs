// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class P75VirtualInstanceValueTests
    {
        [TestCase(false, false, false, false)]
        [TestCase(false, false, true, false)]
        [TestCase(false, true, false, false)]
        [TestCase(false, true, true, false)]
        [TestCase(true, false, false, false)]
        [TestCase(true, false, true, false)]
        [TestCase(true, true, false, false)]
        [TestCase(true, true, true, false)]
        [TestCase(false, false, false, true)]
        public async Task AddedComputedDefaultReadsSavedWorldDuringAsyncReload(bool overrides, bool storedClimate, bool constructedDefaults, bool suppliedSeason)
        {
            ProjectData data = BuildProjectData();
            data.classes["thing-class"].schema = new Dictionary<string, string>
            {
                ["Time"] = "world-time",
                ["Climate"] = "world-climate",
            };
            data.classes["thing-class"].schemaKeyOrder = new[] { "Time", "Climate" };
            data.classes["time-class"] = SchemaClass("time-class", "Time", NeoMemberStorage.Save);
            data.classes["time-class"].schema["Season"] = "thing-count";
            data.classes["climate-class"] = SchemaClass("climate-class", "Climate", NeoMemberStorage.Save);
            data.classes["climate-class"].schema["Score"] = "climate-score";
            data.classes["climate-class"].schema["Risk"] = "climate-risk";
            data.members["climate-score"] = new IntMember
            {
                id = "climate-score",
                name = "Score",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { value = 10 },
            };
            foreach (var pair in new[] { ("world-time", "Time", "time-class"), ("world-climate", "Climate", "climate-class") })
                data.members[pair.Item1] = new ClassMember
                {
                    id = pair.Item1,
                    name = pair.Item2,
                    kind = MemberKind.Class,
                    DeclaredClassId = pair.Item3,
                    Requirement = NeoMemberRequirementKind.Required,
                    defaultValue = new ObjectMemberValueBase { value = new Dictionary<string, string>() },
                };
            if (constructedDefaults)
            {
                foreach (string memberId in new[] { "world-time", "world-climate" })
                {
                    var member = (ClassMember)data.members[memberId];
                    member.defaultValue = ConstructedClassDefault(member.DeclaredClassId!);
                }
            }
            if (suppliedSeason)
            {
                data.values["default-season"] = new NumberMemberValue { id = "default-season", value = 8 };
                ((ClassMember)data.members["world-time"]).defaultValue!.value!["Season"] = "default-season";
            }
            data.members["climate-risk"] = new NSPropertyMember
            {
                id = "climate-risk",
                name = "Risk",
                kind = MemberKind.NSProperty,
                returnTypeInfo = IntTypeInfo(),
                getter = RootReadInitializer(PointerKeyOf(new VariablePointer
                {
                    type = PointerKind.Variable,
                    variableId = "__this__",
                }, "Score")).compiled!,
            };
            if (storedClimate)
            {
                var climate = ObjectValue("stored-climate", "climate-class");
                climate.instanceConstructorId = null;
                climate.constructorArgs = new Dictionary<string, JToken?>();
                data.values[climate.id] = climate;
                ((ObjectMemberValue)data.values["thing-instance"]).value!["Climate"] = climate.id;
            }
            // Save itself is a sparse construction. Its new default below
            // forces World to replay while the Save replay is still active.
            ((ObjectMemberValue)data.values["value-save"]).constructorArgs = new Dictionary<string, JToken?>();
            ((ObjectMemberValue)data.values["value-save"]).instanceConstructorId = null;
            string content;
            string timeId;
            string climateId;
            using (NeoClient old = NeoTestSaveStack.ClientFromSchema(data))
            {
                var world = old.save.Get<NeoMemberClassWritable>("Thing");
                var time = world.Get<NeoMemberClassWritable>("Time");
                var climate = world.Get<NeoMemberClassWritable>("Climate");
                timeId = time.value!.id;
                climateId = climate.value!.id;
                if (overrides)
                {
                    time.Get<NeoMemberIntWritable>("Season").Set(17);
                    climate.Get<NeoMemberIntWritable>("Score").Set(23);
                }
                content = old.SerializeSaveData();
            }
            AddForecast("ForecastSeason", PointerKeyOf(PointerKeyOf(SavePointer("Thing"), "Time"), "Season"));
            AddForecast("ForecastScore", PointerKeyOf(PointerKeyOf(SavePointer("Thing"), "Climate"), "Risk"));
            var referenceType = new ReferenceTypeInfo { type = MemberKind.Reference, required = true, targetTypeInfo = IntTypeInfo() };
            var referenceInit = RootReadInitializer(new RowOfPointer
            {
                type = PointerKind.RowOf,
                place = PointerKeyOf(PointerKeyOf(SavePointer("Thing"), "Time"), "Season"),
            });
            referenceInit.compiled!.typeInfo = referenceType;
            data.classes["climate-class"].schema["SeasonRef"] = "season-ref";
            data.members["season-ref"] = new LookupMember
            {
                id = "season-ref",
                name = "SeasonRef",
                kind = MemberKind.Lookup,
                declaredTypeInfo = referenceType,
                defaultValue = new ArrayMemberValueBase { init = referenceInit },
            };
            data.classes["save-root-class"].schema["Forecast"] = "save-forecast";
            data.members["save-forecast"] = new IntMember
            {
                id = "save-forecast",
                name = "Forecast",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase
                {
                    init = RootReadInitializer(PointerKeyOf(PointerKeyOf(SavePointer("Thing"), "Climate"), "ForecastSeason")),
                },
            };
            using NeoClient reopened = await NeoClient.CreateAsync(new RootDefaultLoader(data), content,
                null, NeoLocalization.CreateEmpty(null), null, default);
            var loadedWorld = reopened.save.Get<NeoMemberClassWritable>("Thing");
            var loadedClimate = loadedWorld.Get<NeoMemberClassWritable>("Climate");
            Assert.AreEqual("thing-instance", loadedWorld.value!.id);
            Assert.AreEqual(timeId, loadedWorld.Get<NeoMemberClass>("Time").value!.id);
            Assert.AreEqual(climateId, loadedClimate.value!.id);
            Assert.AreEqual(loadedWorld.Get<NeoMemberClass>("Time").Get<NeoMemberInt>("Season").value!.id,
                loadedClimate.Get<NeoMemberLookup>("SeasonRef").value!.value![0],
                "A reference captured during default evaluation keeps the final logical slot id.");
            Assert.AreEqual(overrides ? 17 : suppliedSeason ? 8 : 5, loadedClimate.Get<NeoMemberInt>("ForecastSeason").value!.value);
            Assert.AreEqual(overrides ? 23 : 10, loadedClimate.Get<NeoMemberInt>("ForecastScore").value!.value);
            Assert.AreEqual(new NeoRandom(reopened.Random.Seed).Value(), reopened.Random.Value(), "Replay must not draw from the gameplay stream.");

            // Omitted and persisted source leaves have the same dependency
            // semantics after load, on repeated writes, and after another load.
            for (int season = 20; season <= 22; season++)
            {
                loadedWorld.Get<NeoMemberClassWritable>("Time").Get<NeoMemberIntWritable>("Season").Set(season);
                Assert.AreEqual(season, reopened.save.Get<NeoMemberClass>("Thing").Get<NeoMemberClass>("Climate").Get<NeoMemberInt>("ForecastSeason").value!.value);
            }
            loadedWorld.Get<NeoMemberClassWritable>("Climate").Get<NeoMemberIntWritable>("Score").Set(31);
            Assert.AreEqual(31, reopened.save.Get<NeoMemberClass>("Thing").Get<NeoMemberClass>("Climate").Get<NeoMemberInt>("ForecastScore").value!.value);
            using NeoClient again = await NeoClient.CreateAsync(new RootDefaultLoader(data), reopened.SerializeSaveData(),
                null, NeoLocalization.CreateEmpty(null), null, default);
            Assert.AreEqual(22, again.save.Get<NeoMemberClass>("Thing").Get<NeoMemberClass>("Climate").Get<NeoMemberInt>("ForecastSeason").value!.value);
            Assert.AreEqual(31, again.save.Get<NeoMemberClass>("Thing").Get<NeoMemberClass>("Climate").Get<NeoMemberInt>("ForecastScore").value!.value);

            void AddForecast(string name, Pointer pointer)
            {
                data.classes["climate-class"].schema[name] = name;
                data.members[name] = new IntMember
                {
                    id = name,
                    name = name,
                    kind = MemberKind.Int,
                    defaultValue = new NumberMemberValueBase { init = RootReadInitializer(pointer) },
                };
            }
        }

        private static ObjectMemberValueBase ConstructedClassDefault(string classId)
        {
            var initializer = RootReadInitializer(new FunctionPointer
            {
                type = PointerKind.Function,
                function = new DeclaredConstructorFunction
                {
                    type = FunctionKind.DeclaredConstructor,
                    info = new DeclaredConstructorInfo
                    {
                        constructorId = null,
                        schemaClassInfo = ClassType(classId),
                        args = Array.Empty<DeclaredConstructorArgument>(),
                        fields = Array.Empty<FunctionClassConstructorField>(),
                    },
                },
            });
            initializer.compiled!.typeInfo = ClassType(classId);
            return new ObjectMemberValueBase { init = initializer };
        }

        [Test]
        public async Task CrossRootDefaultsSupportInterleavedWritesAndReload(
            [Values] bool storedBase, [Values] bool nestedSibling,
            [Values] bool storedClimate, [Values] bool scoreFirst, [Values] bool constructedDefaults)
        {
            ProjectData data = BuildProjectData();
            data.classes["thing-class"].schema = new Dictionary<string, string>
            {
                ["Time"] = "world-time",
                ["Climate"] = "world-climate",
            };
            data.classes["thing-class"].schemaKeyOrder = new[] { "Time", "Climate" };
            data.classes["time-class"] = SchemaClass("time-class", "Time", NeoMemberStorage.Save);
            data.classes["time-class"].schema["Season"] = "thing-count";
            data.members["world-time"] = new ClassMember
            {
                id = "world-time",
                name = "Time",
                kind = MemberKind.Class,
                DeclaredClassId = "time-class",
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase { value = new Dictionary<string, string>() },
            };
            var save = (ObjectMemberValue)data.values["value-save"];
            save.constructorArgs = new Dictionary<string, JToken?>();
            save.instanceConstructorId = null;
            data.classes["save-root-class"].schema["Base"] = "save-base";
            data.classes["save-root-class"].schemaKeyOrder = new[] { "Base", "Thing", "Forecast" };
            data.members["save-base"] = new IntMember
            {
                id = "save-base",
                name = "Base",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { value = 3 },
            };
            data.classes["climate-class"] = SchemaClass("climate-class", "Climate", NeoMemberStorage.Save);
            data.classes["climate-class"].schema["Score"] = "climate-score";
            data.members["climate-score"] = new IntMember
            {
                id = "climate-score",
                name = "Score",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { value = 10 },
            };
            data.classes["thing-class"].schema["Climate"] = "world-climate";
            data.members["world-climate"] = new ClassMember
            {
                id = "world-climate",
                name = "Climate",
                kind = MemberKind.Class,
                DeclaredClassId = "climate-class",
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase { value = new Dictionary<string, string>() },
            };
            if (constructedDefaults)
                foreach (string memberId in new[] { "world-time", "world-climate" })
                {
                    var member = (ClassMember)data.members[memberId];
                    member.defaultValue = ConstructedClassDefault(member.DeclaredClassId!);
                }
            var climate = ObjectValue("stored-climate", "climate-class");
            climate.instanceConstructorId = null;
            climate.constructorArgs = new Dictionary<string, JToken?>();
            if (storedClimate)
            {
                data.values[climate.id] = climate;
                ((ObjectMemberValue)data.values["thing-instance"]).value!["Climate"] = climate.id;
            }
            if (!nestedSibling)
            {
                // The previous row-id ordering scheduled Climate before its parent here.
                data.classes["thing-class"].schema = new Dictionary<string, string>
                {
                    ["Count"] = "thing-count",
                    ["Climate"] = "world-climate",
                };
                data.classes["thing-class"].schemaKeyOrder = null;
            }
            string content;
            using (NeoClient old = NeoTestSaveStack.ClientFromSchema(data))
            {
                if (storedBase)
                    old.save.Get<NeoMemberIntWritable>("Base").Set(40);
                content = old.SerializeSaveData();
            }
            // Save's Forecast forces the Climate replay while Base exists
            // only in Save's active construction when it was not persisted.
            AddDefault("climate-class", "FromSave", SavePointer("Base"));
            AddDefault("save-root-class", "Forecast", PointerKeyOf(PointerKeyOf(SavePointer("Thing"), "Climate"), "FromSave"));
            using NeoClient client = await NeoClient.CreateAsync(new RootDefaultLoader(data), content,
                null, NeoLocalization.CreateEmpty(null), null, default);
            AssertValues(client, storedBase ? 40 : 3);
            string climateId = Climate(client).value!.id;
            if (scoreFirst)
            {
                Climate(client).Get<NeoMemberIntWritable>("Score").Set(11);
                AssertValues(client, storedBase ? 40 : 3);
            }
            for (int value = 50; value <= 51; value++)
            {
                client.save.Get<NeoMemberIntWritable>("Base").Set(value);
                AssertValues(client, value);
                Climate(client).Get<NeoMemberIntWritable>("Score").Set(value + 10);
                AssertValues(client, value);
                Assert.AreEqual(climateId, Climate(client).value!.id);
            }
            using NeoClient again = await NeoClient.CreateAsync(new RootDefaultLoader(data), client.SerializeSaveData(),
                null, NeoLocalization.CreateEmpty(null), null, default);
            AssertValues(again, 51);
            Assert.AreEqual(61, Climate(again).Get<NeoMemberInt>("Score").value!.value);
            Assert.AreEqual(climateId, Climate(again).value!.id);
            again.save.Get<NeoMemberIntWritable>("Base").Set(52);
            AssertValues(again, 52);
            Assert.AreEqual(61, Climate(again).Get<NeoMemberInt>("Score").value!.value);

            // A failure after this dependency graph was installed must not
            // publish candidate defaults or prevent the next valid write.
            var forecast = ((IntMember)data.members["Forecast"]).defaultValue!.init!.compiled!;
            Instruction[] instructions = forecast.instructions;
            string before = again.SerializeSaveData();
            forecast.instructions = new Instruction[]
            {
                new ThrowInstruction { type = InstructionKind.Throw, pointer = StringLiteral("Rejected cross-root candidate") },
            };
            try
            {
                var error = Assert.Throws<InvalidOperationException>(() => again.save.Get<NeoMemberIntWritable>("Base").Set(53));
                StringAssert.Contains("Rejected cross-root candidate", error!.Message);
            }
            finally
            {
                forecast.instructions = instructions;
            }
            Assert.AreEqual(before, again.SerializeSaveData());
            AssertValues(again, 52);
            Assert.AreEqual(climateId, Climate(again).value!.id);
            Assert.AreEqual(61, Climate(again).Get<NeoMemberInt>("Score").value!.value);
            again.save.Get<NeoMemberIntWritable>("Base").Set(54);
            AssertValues(again, 54);

            NeoMemberClassWritable Climate(NeoClient current) =>
                current.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberClassWritable>("Climate");

            void AddDefault(string classId, string name, Pointer pointer)
            {
                data.classes[classId].schema[name] = name;
                data.members[name] = new IntMember
                {
                    id = name,
                    name = name,
                    kind = MemberKind.Int,
                    defaultValue = new NumberMemberValueBase { init = RootReadInitializer(pointer) },
                };
            }

            void AssertValues(NeoClient current, int expected)
            {
                Assert.AreEqual(expected, current.save.Get<NeoMemberClass>("Thing").Get<NeoMemberClass>("Climate").Get<NeoMemberInt>("FromSave").value!.value);
                Assert.AreEqual(expected, current.save.Get<NeoMemberInt>("Forecast").value!.value);
            }
        }

        private static InitializerBody RootReadInitializer(Pointer pointer) => new()
        {
            code = "root.Save.Thing",
            compiled = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new[]
                {
                    ConstructorVariable("__this__", ClassType("climate-class")),
                    ConstructorVariable("__root__", ClassType("__root__")),
                },
                typeInfo = IntTypeInfo(),
                instructions = new Instruction[]
                {
                    new ReturnInstruction { type = InstructionKind.Return, pointer = pointer },
                },
            },
        };

        private sealed class RootDefaultLoader : INeoSaveLoader
        {
            public RootDefaultLoader(ProjectData schema) => Schema = schema;
            public ProjectData Schema
            {
                get;
            }
            public string CustomId => "root-default-save";
            public Awaitable<string?> LoadSaveContentAsync() => NeoAwaitable.FromResult<string?>(null);
            public Awaitable CommitSaveContentAsync(string content, bool replaceSnapshot) => NeoAwaitable.Completed();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RootReadsInConstructorSeeCompletedDefaultsAndConstructorWrites(bool writeThroughRoot)
        {
            ProjectData data = BuildProjectData();
            data.classes["thing-class"].schema["Copied"] = "thing-copied";
            data.classes["thing-class"].schemaKeyOrder = new[] { "Count", "Copied" };
            data.members["thing-copied"] = new IntMember
            {
                id = "thing-copied",
                name = "Copied",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase
                {
                    init = RootReadInitializer(PointerKeyOf(SavePointer("Thing"), "Count")),
                },
            };
            data.classes["thing-class"].constructorIds = new[] { "thing-ctor" };
            ((ObjectMemberValue)data.values["thing-instance"]).instanceConstructorId = "thing-ctor";
            data.constructors["thing-ctor"] = new ConstructorRecord
            {
                id = "thing-ctor",
                classId = "thing-class",
                projectId = "p75-project",
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[]
                    {
                        ConstructorVariable("__this__", ClassType("thing-class")),
                        ConstructorVariable("__root__", ClassType("__root__")),
                    },
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                    instructions = new Instruction[]
                    {
                        Assign("Count", new ValuePointer
                        {
                            type = PointerKind.Value,
                            value = new Value { typeInfo = IntTypeInfo(), value = 9 },
                        }),
                        Assign("Copied", PointerKeyOf(SavePointer("Thing"), "Count")),
                    },
                },
            };
            if (writeThroughRoot)
            {
                var write = (AssignInstruction)data.constructors["thing-ctor"].action!.instructions[0];
                write.target.pointer = PointerKeyOf(SavePointer("Thing"), "Count");
                write.target.writability = WritabilityKind.Save;
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception,
                    new System.Text.RegularExpressions.Regex("Constructor replay cannot write through root"));
                var error = Assert.Throws<InvalidOperationException>(() => NeoTestSaveStack.ClientFromSchema(data));
                StringAssert.Contains("Write through this during construction", error!.ToString());
                return;
            }
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.AreEqual(9, client.save.Get<NeoMemberClass>("Thing").Get<NeoMemberInt>("Copied").value!.value);

            AssignInstruction Assign(string key, Pointer value) => new()
            {
                type = InstructionKind.Assign,
                operatorValue = "=",
                pointer = value,
                target = new WriteTarget
                {
                    pointer = PointerKeyOf(new VariablePointer { type = PointerKind.Variable, variableId = "__this__" }, key),
                    typeInfo = IntTypeInfo(),
                    writability = WritabilityKind.Session,
                },
            };
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NestedComputedDefaultReadsEarlierSiblingThroughSparseRoot(bool random)
        {
            ProjectData data = random ? RandomDefaultProject() : BuildProjectData();
            data.classes["thing-class"].schema["Nested"] = "thing-nested";
            data.classes["thing-class"].schemaKeyOrder = new[] { "Count", "Nested" };
            data.classes["nested-class"] = SchemaClass("nested-class", "Nested", NeoMemberStorage.Save);
            data.classes["nested-class"].schema["Copied"] = "nested-copied";
            data.members["thing-nested"] = new ClassMember
            {
                id = "thing-nested",
                name = "Nested",
                kind = MemberKind.Class,
                DeclaredClassId = "nested-class",
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase { value = new Dictionary<string, string>() },
            };
            data.members["nested-copied"] = new IntMember
            {
                id = "nested-copied",
                name = "Copied",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase
                {
                    init = new InitializerBody
                    {
                        code = "root.Save.Thing.Count",
                        compiled = new FunctionWithReturnType
                        {
                            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                            parameters = new[]
                            {
                                ConstructorVariable("__this__", ClassType("nested-class")),
                                ConstructorVariable("__root__", ClassType("__root__")),
                            },
                            typeInfo = IntTypeInfo(),
                            instructions = new Instruction[]
                            {
                                new ReturnInstruction
                                {
                                    type = InstructionKind.Return,
                                    pointer = PointerKeyOf(SavePointer("Thing"), "Count"),
                                },
                            },
                        },
                    },
                },
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            double expected = random ? IdentityRange("thing-instance") : 5;
            string id = client.save.Get<NeoMemberClass>("Thing").Get<NeoMemberClass>("Nested").value!.id;
            Assert.AreEqual(expected, client.save.Get<NeoMemberClass>("Thing")
                .Get<NeoMemberClass>("Nested").Get<NeoMemberInt>("Copied").value!.value);
            Assert.AreEqual(new NeoRandom(client.Random.Seed).Value(), client.Random.Value());
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: client.SerializeSaveData());
            var nested = reopened.save.Get<NeoMemberClass>("Thing").Get<NeoMemberClass>("Nested");
            Assert.AreEqual(id, nested.value!.id);
            Assert.AreEqual(expected, nested.Get<NeoMemberInt>("Copied").value!.value);
            Assert.AreEqual(new NeoRandom(reopened.Random.Seed).Value(), reopened.Random.Value());
        }
    }
}
