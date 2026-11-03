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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public sealed class P127DictionaryTests
    {
        private NeoClient client = null!;
        private NSGetterEvaluator.Context ctx = null!;
        private NeoScriptScope scope = null!;
        private string comparerClass = null!;
        private ProjectData data = null!;
        private static TypeInfo Type(MemberKind kind) => new PrimitiveTypeInfo { type = kind, required = true };
        private static Pointer Value(object? value) => new ValuePointer { type = PointerKind.Value, primitiveResolved = true, primitive = value };
        private static Pointer Ref(string id) => new ReferencePointer { type = PointerKind.Reference, valueId = id };
        private static CollectionTypeInfo Collection(bool set) => new() { type = set ? MemberKind.Set : MemberKind.Dictionary, required = true, entryTypeInfo = Type(set ? MemberKind.String : MemberKind.Int) };
        [SetUp]
        public void SetUp()
        {
            data = NeoTestExport.Read(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/p127-equality-parity-export.json"));
            comparerClass = data.classes.Values.Single(c => c.name == "Ci").id;
            var save = (ClassMember)data.members[data.project.rootSaveFileMemberId!];
            var root = (ObjectMemberValue)data.values[save.valueId!];
            foreach (string name in new[] { "S", "T", "D", "E" })
            {
                bool set = name is "S" or "T";
                string id = "b6-dict-" + name;
                data.members[id] = set
                    ? new SetMember { id = id, name = name, kind = MemberKind.Set, entryMemberId = id + "-entry", Requirement = NeoMemberRequirementKind.Required }
                    : new DictionaryMember { id = id, name = name, kind = MemberKind.Dictionary, entryMemberId = id + "-entry", Requirement = NeoMemberRequirementKind.Required };
                data.members[id + "-entry"] = set
                    ? new StringMember { id = id + "-entry", name = name + " entry", kind = MemberKind.String, Format = NeoStringFormatKind.Plain, Requirement = NeoMemberRequirementKind.Required }
                    : new IntMember { id = id + "-entry", name = name + " entry", kind = MemberKind.Int, Requirement = NeoMemberRequirementKind.Required };
                data.classes[save.DeclaredClassId!].schema[name] = id;
                root.value![name] = id;
                data.values[id] = set ? new ArrayMemberValue { id = id, value = Array.Empty<string>() }
                    : new ObjectMemberValue { id = id, value = new() };
            }
            data.classes["b6-bag"] = new NeoSchemaClass { id = "b6-bag", name = "ComparerBag", schema = new() { ["S"] = "b6-dict-S", ["T"] = "b6-dict-T" } };
            Open(NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false));
        }
        private void Open(NeoClient value)
        {
            client = value;
            ctx = new NSGetterEvaluator.Context(client, null, null);
            scope = new NeoScriptScope();
        }
        [TearDown] public void TearDown() => client.Dispose();
        private object? Eval(Pointer pointer) => NSGetterEvaluator.EvaluatePointer(pointer, scope, ctx);
        private Pointer NewComparer() => new FunctionPointer
        {
            type = PointerKind.Function,
            function = new ClassConstructorFunction
            {
                type = FunctionKind.ClassConstructor,
                info = new FunctionClassConstructorInfo
                {
                    schemaClassInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = comparerClass },
                    fields = Array.Empty<FunctionClassConstructorField>()
                }
            }
        };
        private Pointer NewCollection(bool set, Pointer comparer) => set
            ? new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CollectionQueryFunction
                {
                    type = FunctionKind.CollectionQuery,
                    info = new FunctionCollectionQueryInfo
                    {
                        op = "ToSet",
                        collectionPointer = Value(new object?[] { "q" }),
                        equality = new EqualitySite { type = Type(MemberKind.String), comparer = comparer }
                    }
                }
            }
            : new DictLiteralPointer
            {
                type = PointerKind.DictLiteral,
                typeInfo = Collection(false),
                equality = new EqualitySite { type = Type(MemberKind.String), comparer = comparer },
                entries = new[] { new DictLiteralPair { key = Value("q"), value = Value(1d) } }
            };
        private AssignInstruction Assignment(string name, Pointer collection) => new()
        {
            type = InstructionKind.Assign,
            operatorValue = "=",
            target = new WriteTarget { pointer = Ref("b6-dict-" + name), typeInfo = Collection(name is "S" or "T"), writability = WritabilityKind.Save },
            pointer = collection
        };
        private NeoScriptExecutionResult Run(params Instruction[] instructions) => NeoScriptExecutor.Execute(client, new FunctionWithReturnType
        {
            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
            parameters = Array.Empty<Variable>(),
            typeInfo = Type(MemberKind.Null),
            instructions = instructions
        }, scope, ctx);
        private void Assign(string name, Pointer collection) => Run(Assignment(name, collection));
        private bool Contains(string name, string key) => (bool)Eval(ContainsPointer(name, key))!;
        private static Pointer ContainsPointer(string name, string key) => new FunctionPointer
        {
            type = PointerKind.Function,
            function = new ContainsFunction
            {
                type = FunctionKind.Contains,
                info = new FunctionCollectionContainsInfo { collectionPointer = Ref("b6-dict-" + name), valuePointer = Value(key), keys = name is "D" or "E" ? true : null }
            }
        };

        [TestCase(false)]
        [TestCase(true)]
        public void StoredInterfaceListInsertionKeepsClosedArguments(bool text)
        {
            client.Dispose();
            data = NeoTestExport.Read(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/p127-equality-parity-export.json"));
            const string listId = "b6-interface-list";
            const string entryId = "b6-interface-entry";
            string interfaceId = NeoPrimitiveConformance.InterfaceId("IComparable`1");
            string parameter = data.interfaces[interfaceId].genericParams!.Single().id;
            var held = TestTypeBindings.FromKey(text ? "string" : "int");
            data.members[entryId] = new ClassMember
            {
                id = entryId,
                name = "Item",
                kind = MemberKind.Class,
                DeclaredInterfaceId = interfaceId,
                interfaceArguments = new() { [parameter] = held },
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.members[listId] = new ListMember
            {
                id = listId,
                name = "Interfaces",
                kind = MemberKind.List,
                entryMemberId = entryId,
                ListKind = NeoListKind.Ordered,
                Requirement = NeoMemberRequirementKind.Required,
            };
            var save = (ClassMember)data.members[data.project.rootSaveFileMemberId!];
            data.classes[save.DeclaredClassId!].schema["Interfaces"] = listId;
            ((ObjectMemberValue)data.values[save.valueId!]).value!["Interfaces"] = listId;
            data.values[listId] = new ArrayMemberValue { id = listId, value = Array.Empty<string>() };
            Open(NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false));
            var entryType = new InterfaceTypeInfo
            {
                type = MemberKind.Interface,
                required = true,
                interfaceId = interfaceId,
                typeArguments = new() { [parameter] = Type(text ? MemberKind.String : MemberKind.Int) },
            };
            object input = text ? "item" : NeoBox.Of(held, 7d);
            var target = new WriteTarget
            {
                pointer = Ref(listId),
                writability = WritabilityKind.Save,
                typeInfo = new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = entryType },
            };
            Run(new AssignInstruction { type = InstructionKind.Assign, operatorValue = "=", target = target, pointer = Value(new object?[] { input }) });
            Run(new CollectionCallInstruction
            {
                type = InstructionKind.CollectionCall,
                mutation = CollectionMutationKind.Add,
                target = target,
                args = new[] { Value(input) },
            });
            string saved = client.SerializeSaveData();
            client.Dispose();
            Open(NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false, loadedSaveContent: saved));
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, listId, out ArrayMemberValue? list), Is.True);
            Assert.That(list!.value, Has.Length.EqualTo(2));
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, list.value![0], out MemberValue? row), Is.True);
            Assert.That(row!.valueType, Is.EqualTo(held));
            object? read = NSGetterEvaluator.UnwrapRow(row, ctx, NeoValueOwnership.Save, client.members[entryId]);
            Assert.That(read is NeoBox box ? box.Payload : read, Is.EqualTo(text ? (object)"item" : 7d));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void StoredComparerRoundTripsThroughSave(bool set)
        {
            string name = set ? "S" : "D";
            Assign(name, NewCollection(set, NewComparer()));
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, "b6-dict-" + name, out MemberValue? row), Is.True);
            Assert.That(row!.comparer!.kind, Is.EqualTo(NeoCollectionComparerKind.Value));
            string comparerId = row.comparer.valueId!;
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, comparerId, out MemberValue? firstComparer), Is.True, "comparer row before save");
            Assert.That(firstComparer!.classId, Is.EqualTo(comparerClass));
            Assert.That(Contains(name, "Q"), Is.True);
            string save = client.SerializeSaveData();
            client.Dispose();
            Open(NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false, loadedSaveContent: save));
            Assert.That(Contains(name, "Q"), Is.True);
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, comparerId, out MemberValue? restored), Is.True);
            Assert.That(restored!.classId, Is.EqualTo(comparerClass));
        }
        [TestCase(true)]
        [TestCase(false)]
        public void OwnedComparerCannotAttachToAnotherStoredCollection(bool set)
        {
            var local = new VariablePointer { type = PointerKind.Variable, variableId = "cmp" };
            var error = Assert.Catch<NSGetterRuntimeError>(() => Run(
                new VariableInstruction { type = InstructionKind.Variable, variable = new Variable { id = "cmp", typeInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = comparerClass }, pointer = NewComparer() } },
                Assignment(set ? "S" : "D", NewCollection(set, local)),
                Assignment(set ? "T" : "E", NewCollection(set, local))));
            Assert.That(error!.Message, Does.Contain("cannot be attached to '" + (set ? "T" : "E") + ".Comparer'. Call Clone() explicitly."));
        }
        [TestCase(true)]
        [TestCase(false)]
        public void DefaultComparerReplacementDropsStoredMetadata(bool set)
        {
            string name = set ? "S" : "D";
            Assign(name, NewCollection(set, NewComparer()));
            object defaultComparer = Eval(new CollectionComparerPointer { type = PointerKind.CollectionComparer, op = "Comparer", collection = Value(new Dictionary<string, object?>()), elementType = Type(MemberKind.String) })!;
            Assign(name, NewCollection(set, Value(defaultComparer)));
            Assert.That(Contains(name, "Q"), Is.False);
            Assert.That(Contains(name, "q"), Is.True);
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, "b6-dict-" + name, out MemberValue? row), Is.True);
            Assert.That(row!.comparer, Is.Null);
        }
        [TestCase(true)]
        [TestCase(false)]
        public void PendingMembershipUsesTheStoredComparer(bool set)
        {
            string name = set ? "S" : "D";
            var result = NeoScriptExecutor.Execute(client, new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = Type(MemberKind.Bool),
                instructions = new Instruction[]
                {
                    Assignment(name, NewCollection(set, NewComparer())),
                    new CollectionCallInstruction { type = InstructionKind.CollectionCall, mutation = "Add", target = Assignment(name, Value(null)).target,
                        args = set ? new[] { Value("a") } : new[] { Value("a"), Value(2d) } },
                    new ReturnInstruction { type = InstructionKind.Return, pointer = ContainsPointer(name, "A") },
                }
            }, scope, ctx);
            Assert.That(result.ReturnValue, Is.EqualTo(true));
        }
        [Test]
        public void ScriptDictionaryAddOverwritesTheEqualEntry()
        {
            Run(Assignment("D", NewCollection(false, NewComparer())),
                new CollectionCallInstruction { type = InstructionKind.CollectionCall, mutation = "Add", target = Assignment("D", Value(null)).target, args = new[] { Value("Q"), Value(2d) } });
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, "b6-dict-D", out ObjectMemberValue? row), Is.True);
            Assert.That(row!.value!.Keys, Is.EqualTo(new[] { "q" }));
            Assert.That(Eval(new KeyOfPointer { type = PointerKind.KeyOf, keyOf = new KeyOf { pointer = Ref(row.id), key = Value("Q") } }), Is.EqualTo(2d));
        }
        [Test]
        public void CopyConstructorPreservesReferenceEntries()
        {
            var reference = new NSSlotReference("target", null, Type(MemberKind.Int), null);
            var source = new Dictionary<string, object?> { ["first"] = reference };
            var copy = (IDictionary<string, object?>)Eval(new DictLiteralPointer { type = PointerKind.DictLiteral, typeInfo = Collection(false), copyFrom = Value(source) })!;
            Assert.That(copy["first"], Is.SameAs(reference));
        }
        [TestCase(true)]
        [TestCase(false)]
        public void ConstructorEvaluatesComparerBeforeArgumentValidation(bool capacity)
        {
            var calls = new List<string>();
            var invokers = new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>();
            Pointer Native(string id, TypeInfo type, object? value)
            {
                if (value is null)
                    type.required = false;
                client.ProjectDataForRuntime.members[id] = new FunctionMember { id = id, name = id, kind = MemberKind.Function, Modifier = NeoMemberModifierKind.Static, argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(), returnTypeInfo = type };
                invokers[id] = (_, _, _, _) => { calls.Add(id); return value; };
                client.RegisterNativeFunctionInvokers(invokers);
                return new CallDelegatePointer { type = PointerKind.CallDelegate, @delegate = Value(new NeoDelegateValue { memberId = id }), args = Array.Empty<Pointer>() };
            }
            var pointer = new DictLiteralPointer { type = PointerKind.DictLiteral, typeInfo = Collection(false), equality = new EqualitySite { type = Type(MemberKind.String), comparer = Native("comparer", Type(MemberKind.Object), null) } };
            if (capacity)
                pointer.capacity = Native("argument", Type(MemberKind.Int), -1d);
            else
                pointer.copyFrom = Native("argument", Collection(false), null);
            var error = Assert.Catch<Exception>(() => Eval(pointer));
            Assert.That(calls, Is.EqualTo(new[] { "argument", "comparer" }));
            Assert.That(error is NSGetterRuntimeError runtime ? runtime.DiagnosticMessage : error!.Message, Is.EqualTo(capacity ? "Specified argument was out of the range of valid values. (Parameter 'capacity')" : "Value cannot be null. (Parameter 'dictionary')"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConstructedFieldsOwnTheirComparers(bool share)
        {
            var local = new VariablePointer { type = PointerKind.Variable, variableId = "cmp" };
            var construct = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new ClassConstructorFunction
                {
                    type = FunctionKind.ClassConstructor,
                    info = new FunctionClassConstructorInfo
                    {
                        schemaClassInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "b6-bag" },
                        fields = new[]
                    {
                        new FunctionClassConstructorField { schemaKey = "S", memberId = "b6-dict-S", valuePointer = NewCollection(true, local) },
                        new FunctionClassConstructorField { schemaKey = "T", memberId = "b6-dict-T", valuePointer = NewCollection(true, share ? local : NewComparer()) },
                    }
                    }
                }
            };
            NeoScriptExecutionResult Invoke() => NeoScriptExecutor.Execute(client, new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "b6-bag" },
                instructions = new Instruction[] {
                new VariableInstruction { type = InstructionKind.Variable, variable = new Variable { id = "cmp", typeInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = comparerClass }, pointer = NewComparer() } },
                new ReturnInstruction { type = InstructionKind.Return, pointer = construct } }
            }, scope, ctx);
            if (share)
            {
                var error = Assert.Catch<NSGetterRuntimeError>(() => Invoke());
                Assert.That(error!.Message, Does.Contain("Comparer").And.Contain("Clone()"));
            }
            else
            {
                object result = Invoke().ReturnValue!;
                var collection = new KeyOfPointer { type = PointerKind.KeyOf, keyOf = new KeyOf { pointer = Value(result), key = Value("S") } };
                Assert.That(Eval(new FunctionPointer { type = PointerKind.Function, function = new ContainsFunction { type = FunctionKind.Contains, info = new FunctionCollectionContainsInfo { collectionPointer = collection, valuePointer = Value("Q") } } }), Is.True);
            }
        }

        [TestCase("capacity")]
        [TestCase("copyFrom")]
        public void ConstructorShapesRejectEvenEmptyEntries(string field)
        {
            var json = new JObject { ["type"] = "dictLiteral", [field] = new JObject { ["type"] = "value" }, ["entries"] = new JArray() };
            Assert.Throws<JsonSerializationException>(() => json.ToObject<Pointer>());
        }
        [TestCase(true)]
        [TestCase(false)]
        public void ConstructorShapesRoundTrip(bool capacity)
        {
            var p = new DictLiteralPointer { type = PointerKind.DictLiteral, typeInfo = Collection(false) };
            var argument = new ValuePointer { type = PointerKind.Value, value = new Value { typeInfo = Type(MemberKind.Int), value = 1 } };
            if (capacity)
                p.capacity = argument;
            else
                p.copyFrom = argument;
            var json = JObject.FromObject(p, JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
            Assert.That(json.Property("entries"), Is.Null);
            Assert.DoesNotThrow(() => json.ToObject<Pointer>());
        }
    }
}
