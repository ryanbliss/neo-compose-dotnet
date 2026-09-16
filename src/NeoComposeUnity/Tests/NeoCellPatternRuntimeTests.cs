// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
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
        internal static NeoClient Client(Action<ProjectData>? configure = null)
        {
            ProjectData data = NeoGenericTestFixture.BuildProjectData();
            var seed = JObject.Parse(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/cell-pattern-seed.json"));
            foreach (var item in seed["classes"]!) { var value = item.ToObject<NeoSchemaClass>()!; data.classes[value.id] = value; }
            foreach (var item in seed["members"]!) { var value = item.ToObject<Member>()!; data.members[value.id] = value; }
            foreach (var item in seed["enums"]!) { var value = item.ToObject<NeoCompose.Runtime.Json.Enum>()!; data.enums[value.id] = value; }
            data.constructors ??= new Dictionary<string, ConstructorRecord>();
            foreach (var item in seed["constructors"]!) { var value = item.ToObject<ConstructorRecord>()!; data.constructors[value.id] = value; }
            configure?.Invoke(data);
            return NeoTestSaveStack.ClientFromSchema(data);
        }

        [Test]
        public void NativeFactory_UsesCanonicalConstructorAndPreservesPatternOperations()
        {
            using NeoClient client = Client();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            Assert.IsTrue(NeoCellPatternRuntime.TryInvoke("system_aaad2df6-e31e-5f5d-95b9-e265211faed5", null,
                new object?[] { 2, 1, new[] { NeoCellPatternStorage.CenterId } }, ctx, out object? value));
            CollectionAssert.AreEqual(NeoCellPattern.Box(2, 1, NeoCellPatternExcluding.Center), NeoCellPatternStorage.ReadRuntime(value, ctx));
            string id = NSGetterEvaluator.FindRowIdByReference(value, ctx)!;
            Assert.IsTrue(client.TryGetValue(id, out ObjectMemberValue? row));
            Assert.AreEqual(NeoCellPatternStorage.ConstructorId, row!.instanceConstructorId);
            Assert.IsNotNull(row.constructorArgs);
            Assert.IsTrue(NeoCellPatternRuntime.TryInvoke("system_65a16908-05a6-52f9-a467-4e37e95ba0be", value,
                new object?[] { new Vector2Int(1, 0) }, ctx, out object? contains));
            Assert.AreEqual(true, contains);
        }

        [Test]
        public void SnapshotRead_ReflectsChangedOffsetRowsWithoutChangingPreviousPattern()
        {
            using NeoClient client = Client();
            var payload = NeoCellPatternStorage.Serialize(client, new NeoCellPattern(new Vector2Int(3, 4)))!;
            string id = payload.valueId!;
            var member = new ClassMember { id = "pattern-field", name = "Pattern", kind = MemberKind.Class, classId = NeoCellPatternStorage.ClassId };
            var node = new NeoMemberClassWritable(client, member, id, NeoValueOwnership.Session);
            NeoCellPattern before = NeoCellPatternStorage.ReadRequired(client, node);
            var offsets = (ArrayMemberValue)client.ResolveClassChildRow(node.value!, "_offsets")!;
            string entryId = offsets.value![0];
            Assert.IsTrue(client.TryGetValue(entryId, out Vector2MemberValue? entry));
            entry!.value = NeoVectorValues.FromVector2Int(new Vector2Int(9, 8));
            client.SetWritableValue(NeoValueOwnership.Session, entry);
            NeoCellPattern after = NeoCellPatternStorage.ReadRequired(client, node);
            Assert.AreEqual(new Vector2Int(3, 4), before[0]);
            Assert.AreEqual(new Vector2Int(9, 8), after[0]);
            var env = NeoGenericBindings.Resolve<NeoCellPattern>(client, node);
            Assert.AreEqual(after[0], env.Read(node)[0]);
        }

        [Test]
        public void NativeReturns_NormalizePatternAndPatternListForOrdinaryCountGetter()
        {
            var patternType = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = NeoCellPatternStorage.ClassId };
            using var client = Client(data =>
            {
                data.members["native-pattern"] = new FunctionMember { id = "native-pattern", name = "Pattern", kind = MemberKind.Function,
                    Modifier = NeoMemberModifierKind.Static, returnTypeInfo = patternType, argumentTypes = Array.Empty<FunctionArgumentTypeInfo>() };
                data.members["native-patterns"] = new FunctionMember { id = "native-patterns", name = "Patterns", kind = MemberKind.Function,
                    Modifier = NeoMemberModifierKind.Static, returnTypeInfo = new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = patternType }, argumentTypes = Array.Empty<FunctionArgumentTypeInfo>() };
                data.classes[NeoCellPatternStorage.ClassId].schema["NativePattern"] = "native-pattern";
                data.classes[NeoCellPatternStorage.ClassId].schema["NativePatterns"] = "native-patterns";
                data.members["native-nested"] = new FunctionMember { id = "native-nested", name = "Nested", kind = MemberKind.Function,
                    Modifier = NeoMemberModifierKind.Static, returnTypeInfo = new CollectionTypeInfo { type = MemberKind.List, required = true,
                        entryTypeInfo = new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = patternType } }, argumentTypes = Array.Empty<FunctionArgumentTypeInfo>() };
                data.classes[NeoCellPatternStorage.ClassId].schema["NativeNested"] = "native-nested";
            });
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["native-pattern"] = (_, _, _) => NeoCellPattern.Box(1),
                ["native-patterns"] = (_, _, _) => new[] { NeoCellPattern.Cross(1) },
                ["native-nested"] = (_, _, _) => new[] { new[] { NeoCellPattern.Ring(1) } },
            });
            var ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            Assert.IsTrue(client.TryGetMember("system_51d883c9-d451-5747-bcf0-e314e44bffa8", out NSPropertyMember? count));
            object? value = NSGetterEvaluator.InvokeNativeFunction("native-pattern", null, Array.Empty<object?>(), ctx);
            Assert.AreEqual(9, Convert.ToInt32(NSGetterEvaluator.Evaluate(count!.getter!, ctx.WithThis(value))));
            var values = (object?[])NSGetterEvaluator.InvokeNativeFunction("native-patterns", null, Array.Empty<object?>(), ctx)!;
            Assert.AreEqual(5, Convert.ToInt32(NSGetterEvaluator.Evaluate(count.getter!, ctx.WithThis(values[0]))));
            var dictionary = (IReadOnlyDictionary<string, object?>)NeoCellPatternStorage.NormalizeNativeResult(
                new ReadOnlyPatternResult(), ctx, new CollectionTypeInfo { type = MemberKind.Dictionary, required = true, entryTypeInfo = patternType })!;
            Assert.AreEqual(9, Convert.ToInt32(NSGetterEvaluator.Evaluate(count.getter!, ctx.WithThis(dictionary["pattern"]))));
            var nested = (object?[])NSGetterEvaluator.InvokeNativeFunction("native-nested", null, Array.Empty<object?>(), ctx)!;
            Assert.AreEqual(8, Convert.ToInt32(NSGetterEvaluator.Evaluate(count.getter!, ctx.WithThis(((object?[])nested[0]!)[0]))));
        }

        private sealed class ReadOnlyPatternResult : IReadOnlyDictionary<string, object?>
        {
            private readonly Dictionary<string, object?> values = new() { ["pattern"] = NeoCellPattern.Box(1) };
            public object? this[string key] => values[key];
            public IEnumerable<string> Keys => values.Keys;
            public IEnumerable<object?> Values => values.Values;
            public int Count => values.Count;
            public bool ContainsKey(string key) => values.ContainsKey(key);
            public bool TryGetValue(string key, out object? value) => values.TryGetValue(key, out value);
            public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => values.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Test]
        public void ExcludingCodec_PreservesNativeOptionalAndGenericEnumValues()
        {
            using var client = Client();
            Assert.AreEqual(NeoCellPatternExcluding.Center, NeoCellPatternStorage.ReadOptionalExcluding(NeoCellPatternExcluding.Center));
            var member = new EnumMember { id = "excluding", name = "Excluding", kind = MemberKind.Enum, enumId = NeoCellPatternStorage.ExcludingEnumId };
            var codec = NeoGenericBindings.ResolveForMember<NeoCellPatternExcluding?>(client, member);
            Assert.AreEqual(NeoCellPatternStorage.CenterId, ((string[])codec.Serialize(NeoCellPatternExcluding.Center)!.value!)[0]);
        }

        [Test]
        public void NativeFactory_RejectsOversizedOutputBeforeAllocation()
        {
            using NeoClient client = Client();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            Assert.Throws<NSGetterRuntimeError>(() => NeoCellPatternRuntime.TryInvoke(
                "system_82222600-f67f-5127-bb2d-278381b4ef2f", null, new object?[] { int.MaxValue }, ctx, out _));
        }
    }
}
