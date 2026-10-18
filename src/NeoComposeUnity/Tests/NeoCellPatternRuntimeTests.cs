// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    public class NeoCellPatternRuntimeTests
    {
        internal const string BoxId = "system_aaad2df6-e31e-5f5d-95b9-e265211faed5";
        internal const string CrossId = "system_df8ebca6-a1d1-57c1-8762-227943fc51d8";
        internal const string RingId = "system_82222600-f67f-5127-bb2d-278381b4ef2f";
        internal const string RectId = "system_d803cfd5-ce53-57da-abc2-7cd69befeb01";
        internal const string LineId = "system_ef0d4cbb-7182-5d93-9bb8-60a5a18f81a5";
        internal const string CenterId = "system_d90a6e51-0b1e-5354-b4e2-ad4155a833f1";
        internal const string FourNeighborsId = "system_57290d57-16dd-54d6-ad1e-81a72bd7f457";
        internal const string EightNeighborsId = "system_a8f2f798-7988-5df8-8294-decdd4316c7e";
        internal const string EmptyId = "system_98d035f6-9f7d-5d2b-ae01-70ce90621333";
        internal const string CountId = "system_51d883c9-d451-5747-bcf0-e314e44bffa8";
        internal const string ExcludingEnumId = "system_072f749f-62fe-5917-9f7e-f9f29c3a7063";
        internal const string NoneOptionId = "system_5c30e23b-1767-5e15-b633-40ae182157a7";
        internal const string CenterOptionId = "system_456431b5-aa5b-5ea0-9f72-0e16f7114e09";

        internal static NeoClient Client(Action<ProjectData>? configure = null)
        {
            ProjectData data = NeoGenericTestFixture.BuildProjectData();
            AddCellPatternSchema(data);
            configure?.Invoke(data);
            return NeoTestSaveStack.ClientFromSchema(data);
        }

        /// <summary>Adds the NeoCellPattern classes, members, enums, constructors, and preset rows.</summary>
        internal static void AddCellPatternSchema(ProjectData data)
        {
            var seed = JObject.Parse(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/cell-pattern-seed.json"));
            foreach (var item in seed["classes"]!)
            {
                var value = item.ToObject<NeoSchemaClass>()!;
                data.classes[value.id] = value;
            }
            foreach (var item in seed["members"]!)
            {
                var value = item.ToObject<Member>()!;
                data.members[value.id] = value;
            }
            foreach (var item in seed["enums"]!)
            {
                var value = item.ToObject<NeoCompose.Runtime.Json.Enum>()!;
                data.enums[value.id] = value;
            }
            data.constructors ??= new Dictionary<string, ConstructorRecord>();
            foreach (var item in seed["constructors"]!)
            {
                var value = item.ToObject<ConstructorRecord>()!;
                data.constructors[value.id] = value;
            }
            foreach (var item in seed["values"]!)
            {
                var value = item.ToObject<MemberValue>()!;
                data.values[value.id] = value;
            }
        }

        internal static readonly ClassTypeInfo PatternType = new()
        {
            type = MemberKind.Class,
            required = true,
            classId = NeoCellPatternOffsets.ClassId
        };

        /// <summary>
        /// A stored pattern row as push writes one: the declared constructor's
        /// recipe and an empty value, like Neowyn's <c>PlantTrait.Pattern</c>.
        /// </summary>
        internal static void AddStoredPattern(ProjectData data, string id, params Vector2Int[] offsets)
        {
            string[] entryIds = offsets.Select((_, index) => $"{id}-offset-{index}").ToArray();
            data.values[id] = new ObjectMemberValue
            {
                id = id,
                classId = NeoCellPatternOffsets.ClassId,
                value = new Dictionary<string, string>(),
                instanceConstructorId = NeoCellPatternOffsets.ConstructorId,
                constructorArgs = new Dictionary<string, JToken?> { ["__arg_0__"] = id + "-offsets" },
            };
            data.values[id + "-offsets"] = new ArrayMemberValue { id = id + "-offsets", value = entryIds };
            for (int index = 0; index < offsets.Length; index++)
            {
                data.values[entryIds[index]] = new Vector2MemberValue
                {
                    id = entryIds[index],
                    value = new NeoVector2Value { x = offsets[index].x, y = offsets[index].y },
                };
            }
        }

        /// <summary>A pending temporary, as <c>new NeoCellPattern(offsets: ...)</c> builds it.</summary>
        internal static object Construct(NeoClient client, params Vector2Int[] offsets)
        {
            var ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            var resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(
                client,
                new ClassTypeInfo { type = MemberKind.Class, required = true, classId = NeoCellPatternOffsets.ClassId },
                NeoCellPatternOffsets.ConstructorId,
                new[] { "offsets" },
                Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>());
            object?[] arguments = resolved.NewArgumentValues();
            arguments[resolved.argumentPositions[0]] = offsets.Select(offset => (object?)NeoVectorValues.FromVector2Int(offset)).ToArray();
            return NSGetterEvaluator.ConstructDeclared(
                resolved,
                arguments,
                Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>(),
                ctx,
                evaluateFieldValues: null,
                replayContext: false)!;
        }

        internal static ValuePointer Literal(TypeInfo typeInfo, JToken value) => new()
        {
            type = PointerKind.Value,
            value = new Value { typeInfo = typeInfo, value = value },
        };

        internal static ValuePointer Int(int value) =>
            Literal(new PrimitiveTypeInfo { type = MemberKind.Int, required = true }, value);

        internal static ValuePointer Excluding(string optionId) =>
            Literal(new EnumTypeInfo { type = MemberKind.Enum, required = true, enumId = ExcludingEnumId }, new JArray(optionId));

        /// <summary>A static call as stored IR spells it.</summary>
        internal static CallFunctionPointer StaticCall(string site, string memberId, params Pointer[] args) => new()
        {
            type = PointerKind.CallFunction,
            callSiteId = site,
            memberId = memberId,
            receiver = CallReceiver.Static(memberId),
            args = args,
        };

        /// <summary>A static getter read as stored IR spells it.</summary>
        internal static CallGetterPointer StaticRead(string memberId) => new()
        {
            type = PointerKind.CallGetter,
            memberId = memberId,
            receiver = CallReceiver.Static(memberId),
        };

        /// <summary>A stored static read, which is how IR reads a preset.</summary>
        internal static StaticMemberPointer PresetRead(string memberId) => new()
        {
            type = PointerKind.StaticMember,
            memberId = memberId,
        };

        private static readonly Vector2Int[] EightNeighbors =
        {
            new(-1, -1), new(-1, 0), new(-1, 1), new(0, -1), new(0, 1), new(1, -1), new(1, 0), new(1, 1),
        };

        [Test]
        public void StoredIr_CallingAFactoryRunsItsNeoScriptBody()
        {
            // IR compiled while Box was native calls it the same way.
            using NeoClient client = Client();
            var ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            object? box = NSGetterEvaluator.EvaluatePointer(
                StaticCall("box", BoxId, Int(1), Literal(new PrimitiveTypeInfo { type = MemberKind.Int }, JValue.CreateNull()), Excluding(CenterOptionId)),
                new NeoScriptScope(0),
                ctx);
            CollectionAssert.AreEqual(EightNeighbors, NeoCellPatternOffsets.Read(box, ctx));
        }

        [Test]
        public void PresetRead_ReturnsItsSharedStoredRow()
        {
            using NeoClient client = Client();
            var ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            object? Evaluate(string memberId) =>
                NSGetterEvaluator.EvaluatePointer(PresetRead(memberId), new NeoScriptScope(0), ctx);
            Vector2Int[] Read(string memberId) => NeoCellPatternOffsets.Read(Evaluate(memberId), ctx);

            string? center = NSGetterEvaluator.FindRowIdByReference(Evaluate(CenterId)!, ctx);
            Assert.IsNotNull(center);
            Assert.AreEqual(center, NSGetterEvaluator.FindRowIdByReference(Evaluate(CenterId)!, ctx));
            CollectionAssert.AreEqual(new[] { Vector2Int.zero }, Read(CenterId));
            CollectionAssert.AreEqual(new[] { new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(0, 1) }, Read(FourNeighborsId));
            CollectionAssert.AreEqual(EightNeighbors, Read(EightNeighborsId));
            CollectionAssert.IsEmpty(Read(EmptyId));
        }

        [Test]
        public void HelperResult_IsADeclaredConstructorTemporaryThatBuildsNoRowsUntilAttached()
        {
            using NeoClient client = Client();
            var ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            object source = Construct(client, EightNeighbors);
            int before = client.sessionValues.Count;
            Assert.IsTrue(NeoCellPatternRuntime.TryInvoke(NeoCellPatternRuntime.TranslateId, source,
                new object?[] { NeoVectorValues.FromVector2Int(new Vector2Int(2, 1)) }, ctx, out object? translated));
            Assert.IsInstanceOf<NeoScriptObject>(translated);
            CollectionAssert.AreEqual(EightNeighbors.Select(offset => offset + new Vector2Int(2, 1)), NeoCellPatternOffsets.Read(translated, ctx));
            Assert.AreEqual(before, client.sessionValues.Count, "A pattern nothing stored stays in slots.");

            string id = NSGetterEvaluator.FindRowIdByReference(translated, ctx)!;
            Assert.IsTrue(client.TryGetValue(id, out ObjectMemberValue? row));
            Assert.AreEqual(NeoCellPatternOffsets.ConstructorId, row!.instanceConstructorId);
            Assert.AreEqual(1, row.constructorArgs!.Count);
            CollectionAssert.AreEqual(EightNeighbors.Select(offset => offset + new Vector2Int(2, 1)), NeoCellPatternOffsets.Read(translated, ctx));
        }

        [Test]
        public void DetachedPattern_IsReadFromItsSlotsOnce()
        {
            using NeoClient client = Client();
            var ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            object source = Construct(client, EightNeighbors);
            Vector2Int[] first = NeoCellPatternOffsets.Read(source, ctx);
            CollectionAssert.AreEqual(EightNeighbors, first);
            Assert.AreSame(first, NeoCellPatternOffsets.Read(source, ctx));
        }

        [Test]
        public void RowRead_IsKeptUntilAnOffsetRowChangesAndAnEnclosingGetterRecordsItsRows()
        {
            using NeoClient client = Client();
            var ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            object source = Construct(client, EightNeighbors);
            string id = NSGetterEvaluator.FindRowIdByReference(source, ctx)!;
            Assert.IsTrue(client.TryGetValue(id, out ObjectMemberValue? row));
            var offsets = (ArrayMemberValue)client.ResolveClassChildRow(row!, "_offsets")!;
            var key = new NeoClient.GetterMemoKey(NeoValueOwnership.Session, id, "pattern-getter", NeoValueOwnership.Session);
            Vector2Int[] Capture()
            {
                NeoClient.GetterCaptureFrame enclosing = client.BeginGetterReadCapture();
                Vector2Int[] read = NeoCellPatternOffsets.Read(source, ctx);
                client.MemoizeGetter(key, 0, null, client.EndGetterReadCapture(enclosing));
                return read;
            }

            Vector2Int[] first = Capture();
            CollectionAssert.AreEqual(EightNeighbors, first);
            Assert.AreSame(first, Capture(), "An unchanged row reads as the same array.");
            Assert.IsNotNull(client.FindMemoizedGetter(key));

            Assert.IsTrue(client.TryGetValue(offsets.value![2], out Vector2MemberValue? entry));
            entry!.value = NeoVectorValues.FromVector2Int(new Vector2Int(9, 8));
            client.SetWritableValue(NeoValueOwnership.Session, entry);
            Assert.IsNull(client.FindMemoizedGetter(key), "A getter that read the offsets through a kept read hears the entry change.");
            var expected = EightNeighbors.ToArray();
            expected[2] = new Vector2Int(9, 8);
            CollectionAssert.AreEqual(expected, Capture());
            CollectionAssert.AreEqual(EightNeighbors, first, "A read array never changes.");

            client.SetWritableValue(NeoValueOwnership.Session, new ArrayMemberValue { id = offsets.id, value = offsets.value.Take(1).ToArray() });
            Assert.IsNull(client.FindMemoizedGetter(key), "A list change drops it too.");
            CollectionAssert.AreEqual(EightNeighbors.Take(1), Capture());
        }

        [Test]
        public void StaticPatternGetter_FollowsARowItRead()
        {
            const string areaId = "member-area";
            var patternType = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = NeoCellPatternOffsets.ClassId };
            // static NeoCellPattern Area => NeoCellPattern.Box(width, 0);
            var area = new NSPropertyMember
            {
                id = areaId,
                projectId = "project-a",
                name = "Area",
                kind = MemberKind.NSProperty,
                Modifier = NeoMemberModifierKind.Static,
                returnTypeInfo = patternType,
                getter = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[] { new Variable { id = "__this__" }, new Variable { id = "__root__" } },
                    typeInfo = patternType,
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction
                        {
                            type = InstructionKind.Return,
                            pointer = StaticCall("area-box", BoxId,
                                new ReferencePointer { type = PointerKind.Reference, valueId = "pattern-width" },
                                Int(0),
                                Excluding(NoneOptionId)),
                        },
                    },
                },
            };
            using NeoClient client = Client(data =>
            {
                data.values["pattern-width"] = new NumberMemberValue { id = "pattern-width", value = 1 };
                data.members[areaId] = area;
                data.classes["class-areas"] = new NeoSchemaClass
                {
                    id = "class-areas",
                    projectId = "project-a",
                    name = "Areas",
                    schema = new Dictionary<string, string> { ["Area"] = areaId },
                };
            });
            var ctx = client.CreateGetterContext(NeoValueOwnership.Save);
            Vector2Int[] Area() => NeoCellPatternOffsets.Read(NSGetterEvaluator.EvaluatePointer(StaticRead(areaId), new NeoScriptScope(0), ctx), ctx);

            var width1 = new[] { new Vector2Int(0, 0), new Vector2Int(-1, 0), new Vector2Int(1, 0) };
            CollectionAssert.AreEqual(width1, Area());

            client.SetWritableValue(NeoValueOwnership.Save, new NumberMemberValue { id = "pattern-width", value = 2 });
            CollectionAssert.AreEqual(new[] { new Vector2Int(0, 0), new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(-2, 0), new Vector2Int(2, 0) }, Area());
        }

        [Test]
        public void ConstructedPattern_RecordsTheArgumentsItWasBuiltWith()
        {
            // A vector read from a row is the row's cached payload, which a
            // later write patches in place. The recipe recorded when the
            // pattern attaches still holds the value it was built with.
            using NeoClient client = Client();
            var ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            NeoVector2Value offset = NeoVectorValues.FromVector2Int(new Vector2Int(1, 2));
            var resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(
                client,
                new ClassTypeInfo { type = MemberKind.Class, required = true, classId = NeoCellPatternOffsets.ClassId },
                NeoCellPatternOffsets.ConstructorId,
                new[] { "offsets" },
                Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>());
            object?[] arguments = resolved.NewArgumentValues();
            arguments[resolved.argumentPositions[0]] = new object?[] { offset };
            object source = NSGetterEvaluator.ConstructDeclared(
                resolved,
                arguments,
                Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>(),
                ctx,
                evaluateFieldValues: null,
                replayContext: false)!;

            offset.y = 9;

            string id = NSGetterEvaluator.FindRowIdByReference(source, ctx)!;
            Assert.IsTrue(client.TryGetValue(id, out ObjectMemberValue? row));
            JToken recorded = row!.constructorArgs!.Values.Single()!;
            Assert.AreEqual(2, recorded[0]!["y"]!.Value<int>(), recorded.ToString());
        }

        [Test]
        public void ConstructedPattern_OffsetComponentWriteReachesItsRow()
        {
            using NeoClient client = Client();
            var ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            object source = Construct(client, EightNeighbors);
            var body = Assignment(
                OffsetY(new VariablePointer { type = PointerKind.Variable, variableId = "source" }, 1),
                new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                Literal(7, MemberKind.Int),
                new Variable { id = "source", typeInfo = PatternType });
            NeoScriptExecutor.Execute(client, body, new Dictionary<string, object?> { ["source"] = source }, ctx);
            var expected = EightNeighbors.ToArray();
            expected[1] = new Vector2Int(expected[1].x, 7);
            CollectionAssert.AreEqual(expected, NeoCellPatternOffsets.Read(source, ctx));
            Assert.IsNotNull(NSGetterEvaluator.FindRowIdByReference(source, ctx));
        }

        [Test]
        public void Preset_RefusesEveryRuntimeWrite()
        {
            // @storage(allowed: .Immutable) keeps a preset read-only in C# and NeoScript.
            using NeoClient client = Client();
            var ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            object? center = NSGetterEvaluator.EvaluatePointer(PresetRead(CenterId), new NeoScriptScope(0), ctx);
            string centerRow = NSGetterEvaluator.FindRowIdByReference(center!, ctx)!;

            Assert.IsFalse(TestCellPattern.Preset(client, CenterId).TryWritable(out TestCellPattern _));
            var replaced = Assert.Throws<InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.StaticBinding(client, CenterId, NeoValueOwnership.Asset)
                    .BindValue(TestCellPattern.New(client, new Vector2Int(5, 5))));
            Assert.AreEqual("Static member 'Center' is Immutable and cannot be written at runtime.", replaced!.Message);

            // NeoCellPattern.Center = p;
            var replace = Assignment(PresetRead(CenterId), PatternType,
                new VariablePointer { type = PointerKind.Variable, variableId = "p" },
                new Variable { id = "p", typeInfo = PatternType });
            var assigned = Assert.Catch<NSGetterRuntimeError>(() => NeoScriptExecutor.Execute(client, replace,
                new Dictionary<string, object?> { ["p"] = Construct(client, new Vector2Int(5, 5)) }, ctx));
            Assert.AreEqual("Static member 'Center' is Immutable and cannot be written at runtime.", assigned!.Message);

            // NeoCellPattern.Center._offsets[0].y = 7;
            var edit = Assignment(OffsetY(PresetRead(CenterId), 0), new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                Literal(7, MemberKind.Int));
            var edited = Assert.Catch<NSGetterRuntimeError>(() =>
                NeoScriptExecutor.Execute(client, edit, new Dictionary<string, object?>(), ctx));
            Assert.AreEqual("Cannot write runtime-owned target because its value is Asset-owned.", edited!.Message);

            center = NSGetterEvaluator.EvaluatePointer(PresetRead(CenterId), new NeoScriptScope(0), ctx);
            Assert.AreEqual(centerRow, NSGetterEvaluator.FindRowIdByReference(center!, ctx));
            CollectionAssert.AreEqual(new[] { Vector2Int.zero }, NeoCellPatternOffsets.Read(center, ctx));
        }

        /// <summary><c>target = value</c>, with a runtime-owned target.</summary>
        private static FunctionWithReturnType Assignment(Pointer target, TypeInfo targetType, Pointer value, params Variable[] parameters) => new()
        {
            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
            parameters = parameters,
            typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
            instructions = new Instruction[]
            {
                new AssignInstruction
                {
                    type = InstructionKind.Assign,
                    operatorValue = "=",
                    pointer = value,
                    target = new WriteTarget
                    {
                        pointer = target,
                        typeInfo = targetType,
                        writability = WritabilityKind.Runtime,
                    },
                },
            },
        };

        /// <summary><c>pattern._offsets[index].y</c></summary>
        private static KeyOfPointer OffsetY(Pointer pattern, int index) =>
            KeyOf(KeyOf(KeyOf(pattern, Literal("_offsets", MemberKind.String)), Literal(index, MemberKind.Int)), Literal("y", MemberKind.String));

        private static KeyOfPointer KeyOf(Pointer receiver, Pointer key) => new()
        {
            type = PointerKind.KeyOf,
            keyOf = new KeyOf { pointer = receiver, key = key },
        };

        private static ValuePointer Literal(object value, MemberKind kind) =>
            Literal(new PrimitiveTypeInfo { type = kind, required = true }, JToken.FromObject(value));

        [Test]
        public void EmptyOffsets_ReplayTwoStoredPatternsWithoutSharedArrayCollision()
        {
            // Empty constructor lists used to hand every row the shared
            // Array.Empty singleton as its origin key, so the second empty
            // pattern's replay threw "Key already in the list".
            using NeoClient client = Client();
            var ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            object first = Construct(client);
            object second = Construct(client);
            string firstId = NSGetterEvaluator.FindRowIdByReference(first, ctx)!;
            string secondId = NSGetterEvaluator.FindRowIdByReference(second, ctx)!;
            Assert.AreNotEqual(firstId, secondId);
            INeoCellPattern firstView = TestCellPattern.Writable(client, firstId);
            INeoCellPattern secondView = TestCellPattern.Writable(client, secondId);
            Assert.AreEqual(0, firstView.Count);
            Assert.AreEqual(0, secondView.Count);
            Assert.AreEqual(0, firstView.Count);
        }

        [Test]
        public void FieldDefaultReadingAPreset_GivesEachReplayedInstanceItsOwnCopy()
        {
            // Neowyn's PlantTrait.Pattern defaults to EightNeighbors. An
            // instance that stores no pattern runs the default again, now
            // against the stored preset: as an omitted field of a sparse row,
            // and while replaying a row's constructor recipe, which is how a
            // partial save rebuilds an instance. An instance that stores a
            // pattern keeps it, with or without a recipe.
            static ClassTypeInfo ClassType(string classId) => new()
            {
                type = MemberKind.Class,
                required = true,
                classId = classId
            };
            string[] holders = { "Sparse", "Replayed", "Stored", "ReplayedStored" };
            Vector2Int[] stored = { new(4, 1), new(4, 1), new(-2, 0) };
            using NeoClient client = Client(data =>
            {
                data.classes["pattern-holder"] = new NeoSchemaClass
                {
                    id = "pattern-holder",
                    projectId = "project-a",
                    name = "PatternHolder",
                    allowedStorage = NeoMemberStorage.Save,
                    schema = new Dictionary<string, string> { ["Pattern"] = "holder-pattern" },
                    constructorIds = new[] { "holder-ctor" },
                };
                data.constructors["holder-ctor"] = new ConstructorRecord
                {
                    id = "holder-ctor",
                    projectId = "project-a",
                    classId = "pattern-holder",
                    argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                    action = new FunctionWithReturnType
                    {
                        compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                        parameters = new[]
                        {
                            new Variable { id = "__this__", typeInfo = ClassType("pattern-holder") },
                            new Variable { id = "__root__", typeInfo = ClassType("save-root-class") },
                        },
                        typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                        instructions = Array.Empty<Instruction>(),
                    },
                };
                data.members["holder-pattern"] = new ClassMember
                {
                    id = "holder-pattern",
                    projectId = "project-a",
                    name = "Pattern",
                    kind = MemberKind.Class,
                    DeclaredClassId = NeoCellPatternOffsets.ClassId,
                    Requirement = NeoMemberRequirementKind.Required,
                    defaultValue = new ObjectMemberValueBase
                    {
                        init = new InitializerBody
                        {
                            code = "NeoCellPattern.EightNeighbors",
                            compiled = new FunctionWithReturnType
                            {
                                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                                parameters = new[]
                                {
                                    new Variable { id = "__this__", typeInfo = ClassType("pattern-holder") },
                                    new Variable { id = "__root__", typeInfo = ClassType("save-root-class") },
                                },
                                typeInfo = ClassType(NeoCellPatternOffsets.ClassId),
                                instructions = new Instruction[]
                                {
                                    new ReturnInstruction { type = InstructionKind.Return, pointer = PresetRead(EightNeighborsId) },
                                },
                            },
                        },
                    },
                };
                foreach (string holder in holders)
                {
                    data.classes["save-root-class"].schema[holder] = "holder-" + holder;
                    data.members["holder-" + holder] = new ClassMember
                    {
                        id = "holder-" + holder,
                        projectId = "project-a",
                        name = holder,
                        kind = MemberKind.Class,
                        DeclaredClassId = "pattern-holder",
                    };
                    bool replayed = holder.StartsWith("Replayed");
                    var record = new Dictionary<string, string>();
                    if (holder.EndsWith("Stored"))
                    {
                        AddStoredPattern(data, holder + "-pattern", stored);
                        record["Pattern"] = holder + "-pattern";
                    }
                    data.values[holder + "-value"] = new ObjectMemberValue
                    {
                        id = holder + "-value",
                        classId = "pattern-holder",
                        value = record,
                        instanceConstructorId = replayed ? "holder-ctor" : null,
                        constructorArgs = replayed ? new Dictionary<string, JToken?>() : null,
                    };
                    ((ObjectMemberValue)data.values["root-save-value"]).value![holder] = holder + "-value";
                }
            });
            var ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            string preset = NSGetterEvaluator.FindRowIdByReference(
                NSGetterEvaluator.EvaluatePointer(PresetRead(EightNeighborsId), new NeoScriptScope(0), ctx)!, ctx)!;
            var owned = new HashSet<string>();
            foreach (string holder in holders)
            {
                string id = client.save.Get<NeoMemberClassWritable>(holder).Get<NeoMemberClassWritable>("Pattern").value!.id;
                if (holder.EndsWith("Stored"))
                {
                    Assert.AreEqual(holder + "-pattern", id, $"{holder} reads its stored pattern, not a copy of the default.");
                    CollectionAssert.AreEqual(stored, TestCellPattern.Writable(client, id, NeoValueOwnership.Save));
                    continue;
                }
                Assert.AreNotEqual(preset, id, $"{holder} must own a copy, not the preset row.");
                Assert.IsTrue(owned.Add(id), $"{holder} must not share its copy.");
                CollectionAssert.AreEqual(EightNeighbors, TestCellPattern.Writable(client, id, NeoValueOwnership.Save));
            }
        }

        private static readonly Vector2Int[] ListedA = { new(2, 0), new(0, 2) };
        private static readonly Vector2Int[] ListedB = { new(-3, 1) };
        private static readonly Vector2Int[] Named = { new(1, 1), new(0, 0) };
        private static readonly Vector2Int[] Boxed = { new(0, -4) };

        /// <summary>
        /// Save fields that hold or name patterns: <c>Reference&lt;NeoCellPattern&gt; Pick</c>
        /// (Center), <c>Set&lt;Reference&lt;NeoCellPattern&gt;&gt; Picks</c> (Center, listed-a),
        /// <c>List&lt;NeoCellPattern&gt; Patterns</c> (listed-a, listed-b),
        /// <c>Dictionary&lt;string, NeoCellPattern&gt; ByName</c> (near), and
        /// <c>Box&lt;NeoCellPattern&gt; Box</c> whose <c>T Value</c> holds boxed.
        /// </summary>
        private static NeoClient PathsClient(out string centerRow)
        {
            string? center = null;
            NeoClient client = Client(data =>
            {
                center = data.members[CenterId].valueId!;
                var root = (ObjectMemberValue)data.values["root-save-value"];
                void AddField(string key, Member member, MemberValue value)
                {
                    member.projectId = "project-a";
                    data.members[member.id] = member;
                    data.classes["save-root-class"].schema[key] = member.id;
                    data.values[value.id] = value;
                    root.value![key] = value.id;
                }
                AddField("Pick", new LookupMember { id = "pick-member", name = "Pick", kind = MemberKind.Lookup, declaredTypeInfo = PatternType },
                    new ArrayMemberValue { id = "pick", value = new[] { center } });
                AddField("Picks", new LookupMember
                {
                    id = "picks-member",
                    name = "Picks",
                    kind = MemberKind.Lookup,
                    declaredTypeInfo = PatternType,
                    Selection = NeoMemberSelectionKind.Multi,
                },
                    new ArrayMemberValue { id = "picks", value = new[] { center, "listed-a" } });
                AddField("Patterns", new ListMember { id = "patterns-member", name = "Patterns", kind = MemberKind.List, entryMemberId = "pattern-entry" },
                    new ArrayMemberValue { id = "patterns", value = new[] { "listed-a", "listed-b" } });
                AddField("ByName", new DictionaryMember { id = "by-name-member", name = "ByName", kind = MemberKind.Dictionary, entryMemberId = "pattern-entry" },
                    new ObjectMemberValue { id = "by-name", value = new Dictionary<string, string> { ["near"] = "near" } });
                var bindings = new Dictionary<string, string> { ["box-t"] = "box-binding" };
                AddField("Box", new ClassMember { id = "box-member", name = "Box", kind = MemberKind.Class, DeclaredClassId = "pattern-box" },
                    new ObjectMemberValue
                    {
                        id = "box",
                        classId = "pattern-box",
                        genericBindings = bindings,
                        value = new Dictionary<string, string> { ["Value"] = "boxed" }
                    });
                data.members["pattern-entry"] = new ClassMember { id = "pattern-entry", name = "Entry", kind = MemberKind.Class, DeclaredClassId = NeoCellPatternOffsets.ClassId };
                data.members["box-value"] = new GenericMember { id = "box-value", name = "Value", kind = MemberKind.Generic, genericParamId = "box-t" };
                data.members["box-binding"] = new ClassMember { id = "box-binding", name = "T", kind = MemberKind.Class, DeclaredClassId = NeoCellPatternOffsets.ClassId };
                data.classes["pattern-box"] = new NeoSchemaClass
                {
                    id = "pattern-box",
                    projectId = "project-a",
                    name = "Box",
                    schema = new Dictionary<string, string> { ["Value"] = "box-value" },
                    genericParams = new List<GenericParamDeclaration> { new() { id = "box-t", name = "T" } },
                };
                AddStoredPattern(data, "listed-a", ListedA);
                AddStoredPattern(data, "listed-b", ListedB);
                AddStoredPattern(data, "near", Named);
                AddStoredPattern(data, "boxed", Boxed);
            });
            centerRow = center!;
            return client;
        }

        /// <summary><c>row.key</c>, as IR reads a field of a stored row.</summary>
        internal static Pointer Field(string rowId, string key) =>
            KeyOf(new ReferencePointer { type = PointerKind.Reference, valueId = rowId }, Literal(key, MemberKind.String));

        private static Pointer SaveField(string key) => Field("root-save-value", key);

        [Test]
        public void Reference_ResolvesToTheStoredRowAndFollowsARetarget()
        {
            using NeoClient client = PathsClient(out string centerRow);
            var ctx = client.CreateGetterContext(NeoValueOwnership.Save);
            object? Evaluate(Pointer pointer) => NSGetterEvaluator.EvaluatePointer(pointer, new NeoScriptScope(0), ctx);
            TestCellPattern Pick() =>
                TestCellPattern.Create(client, (NeoMemberClass)client.save.Get<NeoMemberLookup>("Pick").GetFirstSelected()!);

            // A reference to a preset reads the preset row itself.
            Assert.AreEqual(centerRow, Pick().valueId);
            CollectionAssert.AreEqual(new[] { Vector2Int.zero }, Pick());
            Assert.IsFalse(Pick().TryWritable(out TestCellPattern _), "A preset reached through a reference stays read-only.");
            Assert.AreEqual(centerRow, NSGetterEvaluator.FindRowIdByReference(Evaluate(SaveField("Pick"))!, ctx));

            var key = new NeoClient.GetterMemoKey(NeoValueOwnership.Save, "root-save-value", "pick-getter", NeoValueOwnership.Save);
            Vector2Int[] MemoizePick()
            {
                NeoClient.GetterCaptureFrame enclosing = client.BeginGetterReadCapture();
                Vector2Int[] read = NeoCellPatternOffsets.Read(Evaluate(SaveField("Pick")), ctx);
                client.MemoizeGetter(key, 0, null, client.EndGetterReadCapture(enclosing));
                return read;
            }
            CollectionAssert.AreEqual(new[] { Vector2Int.zero }, MemoizePick());

            client.save.Get<NeoMemberLookupWritable>("Pick").Set(new[] { "listed-b" });
            Assert.IsNull(client.FindMemoizedGetter(key), "A getter that read the reference hears the retarget.");
            CollectionAssert.AreEqual(ListedB, MemoizePick());
            Assert.AreEqual("listed-b", Pick().valueId);
            CollectionAssert.AreEqual(ListedB, Pick());
            Assert.AreEqual(true, Pick().Invoke(NeoCellPatternRuntime.ContainsId, new Vector2Int(-3, 1)));
        }

        [Test]
        public void ReferenceSet_ResolvesEachMember()
        {
            using NeoClient client = PathsClient(out string centerRow);
            var ctx = client.CreateGetterContext(NeoValueOwnership.Save);
            var picks = client.save.Get<NeoMemberLookupWritable>("Picks");
            string eightRow = NSGetterEvaluator.FindRowIdByReference(
                NSGetterEvaluator.EvaluatePointer(PresetRead(EightNeighborsId), new NeoScriptScope(0), ctx)!, ctx)!;
            Assert.IsTrue(picks.Add(eightRow), "A reference picker offers the presets.");

            var view = new NeoReadOnlyLookupSet<TestCellPattern>(client, picks, child => TestCellPattern.Create(client, (NeoMemberClass)child));
            CollectionAssert.AreEqual(new[] { centerRow, "listed-a", eightRow }, view.Select(pattern => pattern.valueId));
            CollectionAssert.AreEqual(new[] { new[] { Vector2Int.zero }, ListedA, EightNeighbors }, view.Select(pattern => pattern.ToArray()));

            // NeoScript reads a multi-select reference as its ids; Picks[i] resolves each row.
            object?[] read = Enumerable.Range(0, 3)
                .Select(index => NSGetterEvaluator.EvaluatePointer(KeyOf(SaveField("Picks"), Literal(index, MemberKind.Int)), new NeoScriptScope(0), ctx))
                .ToArray();
            CollectionAssert.AreEqual(new[] { centerRow, "listed-a", eightRow }, read.Select(entry => NSGetterEvaluator.FindRowIdByReference(entry!, ctx)));
            CollectionAssert.AreEqual(new[] { new[] { Vector2Int.zero }, ListedA, EightNeighbors }, read.Select(entry => NeoCellPatternOffsets.Read(entry, ctx)));
        }

        [Test]
        public void OrdinaryPaths_ListDictionaryAndGenericFieldReadWorkingPatterns()
        {
            using NeoClient client = PathsClient(out _);
            var ctx = client.CreateGetterContext(NeoValueOwnership.Save);
            Vector2Int[] Read(Pointer pointer) => NeoCellPatternOffsets.Read(NSGetterEvaluator.EvaluatePointer(pointer, new NeoScriptScope(0), ctx), ctx);
            TestCellPattern Create(NeoClient factoryClient, NeoMember child) => TestCellPattern.Create(factoryClient, (NeoMemberClass)child);

            var list = new NeoReadOnlyList<TestCellPattern>(client, client.save.Get<NeoMemberList>("Patterns"), Create);
            Assert.AreEqual(2, list.Count);
            CollectionAssert.AreEqual(new[] { ListedA, ListedB }, list.Select(pattern => pattern.ToArray()));
            Assert.AreEqual(ListedB[0], ((INeoCellPattern)list[1])[0]);
            CollectionAssert.AreEqual(ListedB, Read(KeyOf(SaveField("Patterns"), Literal(1, MemberKind.Int))));

            var dictionary = new NeoReadOnlyDictionary<TestCellPattern>(client, client.save.Get<NeoMemberDictionary>("ByName"), Create);
            CollectionAssert.AreEqual(Named, dictionary["near"]);
            CollectionAssert.AreEqual(new[] { "near" }, dictionary.Select(entry => entry.Key));
            CollectionAssert.AreEqual(Named, Read(KeyOf(SaveField("ByName"), Literal("near", MemberKind.String))));

            NeoMember boxed = client.save.Get<NeoMemberClass>("Box").Get<NeoMember>("Value");
            TestCellPattern generic = NeoGenericBindings.Resolve<TestCellPattern>(client, boxed).Read(boxed);
            CollectionAssert.AreEqual(Boxed, generic);
            CollectionAssert.AreEqual(Boxed, Read(KeyOf(SaveField("Box"), Literal("Value", MemberKind.String))));

            // Each reads as a working pattern.
            CollectionAssert.AreEqual(ListedA.Select(offset => offset + Vector2Int.one), list[0].Helper(NeoCellPatternRuntime.TranslateId, Vector2Int.one));
            Assert.AreEqual(true, dictionary["near"].Invoke(NeoCellPatternRuntime.ContainsId, Vector2Int.zero));
            Assert.AreEqual(1, generic.Count);
        }

        [Test]
        public void NonPatternValue_IsRejected()
        {
            using NeoClient client = Client();
            var ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            var error = Assert.Catch<NSGetterRuntimeError>(() => NeoCellPatternOffsets.Read(3, ctx));
            Assert.AreEqual("Cell pattern value must be a NeoCellPattern instance.", error!.Message);
        }
    }

    /// <summary>
    /// The generated NeoCellPattern's shape: it re-lists
    /// <see cref="INeoCellPattern"/>, declares the NeoScript <c>Count</c>,
    /// and runs factories and helpers the way the generated wrappers do.
    /// </summary>
    internal sealed class TestCellPattern : NeoGeneratedCellPatternValue, INeoCellPattern
    {
        private TestCellPattern(NeoClient client, NeoMemberClass node, bool isReadOnly, NeoValueOwnership ownership)
            : base(client, node, NeoCellPatternOffsets.ClassId, isReadOnly, ownership)
        {
        }

        private TestCellPattern(NeoClient client, NeoDetachedValue value, bool isReadOnly)
            : base(client, value, isReadOnly)
        {
        }

        public int Count
        {
            get
            {
                NSGetterResult result = ComputeProperty("Count");
                if (!result.ok)
                    throw new InvalidOperationException(result.error);
                return Convert.ToInt32(result.value);
            }
        }

        internal static TestCellPattern Preset(NeoClient client, string memberId) =>
            Create(client, NeoGeneratedTypesSupport.StaticBinding(client, memberId, NeoValueOwnership.Asset).GetRequiredNode<NeoMemberClass>());

        internal static TestCellPattern Create(NeoClient client, NeoMemberClass node) =>
            NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue<TestCellPattern>(
                client,
                node,
                static (factoryClient, factoryNode) => new TestCellPattern(factoryClient, factoryNode, true, NeoValueOwnership.Asset));

        internal static TestCellPattern Factory(NeoClient client, string memberId, params object?[] args) =>
            Read(client, NeoMemberNSFunction.GetOrCreateStatic(client, memberId).InvokeStatic(args));

        internal static TestCellPattern New(NeoClient client, params Vector2Int[] offsets) =>
            CreateWritable(client, NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                client,
                NeoCellPatternOffsets.ClassId,
                NeoCellPatternOffsets.ConstructorId,
                new[] { new NeoDeclaredConstructorArgument("offsets", offsets) }));

        internal static TestCellPattern Writable(
            NeoClient client,
            string valueId,
            NeoValueOwnership ownership = NeoValueOwnership.Session) =>
            CreateWritable(client, new NeoMemberClassWritable(client,
                new ClassMember { id = "pattern-field", name = "Pattern", kind = MemberKind.Class, DeclaredClassId = NeoCellPatternOffsets.ClassId },
                valueId,
                ownership));

        internal object? Invoke(string memberId, params object?[] args) =>
            Client.ScriptGridQueries.Invoke(memberId, this, args);

        internal TestCellPattern Helper(string memberId, params object?[] args) =>
            Read(Client, Invoke(memberId, args));

        private static TestCellPattern CreateWritable(NeoClient client, NeoMemberClassWritable node) =>
            NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue<TestCellPattern>(
                client,
                node,
                static (factoryClient, factoryNode) => new TestCellPattern(factoryClient, factoryNode, false, factoryNode.ownership));

        private static TestCellPattern Read(NeoClient client, object? result) =>
            NeoGeneratedTypesSupport.ReadRequiredNSPropertyClass<TestCellPattern>(
                client,
                result,
                true,
                null,
                CreateWritable,
                static (factoryClient, factoryValue, factorySaved) => new TestCellPattern(factoryClient, factoryValue, !factorySaved));
    }
}
