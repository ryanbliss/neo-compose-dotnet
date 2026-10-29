// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    public partial class NSFunctionRuntimeTests
    {
        private static Pointer TupleOut(WriteTarget target, params int[] path) =>
            new JObject
            {
                ["type"] = "out",
                ["target"] = JObject.FromObject(target),
                ["tuplePath"] = new JArray(path),
            }.ToObject<Pointer>()!;

        [TestCase(false)]
        [TestCase(true)]
        public void P127OutNestedTupleAliasesPreserveCurrentSiblings(bool failAfterWriting)
        {
            var pair = new TupleTypeInfo
            {
                type = MemberKind.Tuple,
                required = true,
                elements = new[] { new TupleElementTypeInfo { typeInfo = IntType() }, new TupleElementTypeInfo { typeInfo = IntType() } },
            };
            var rootType = new TupleTypeInfo
            {
                type = MemberKind.Tuple,
                required = true,
                elements = new[] { new TupleElementTypeInfo { typeInfo = pair }, new TupleElementTypeInfo { typeInfo = IntType() } },
            };
            var first = Argument("first", MemberKind.Int);
            var second = Argument("second", MemberKind.Int);
            first.mode = second.mode = NeoParameterModeKind.Out;
            var arguments = new[] { first, second };
            var function = ScriptFunction("out-tuple", "OutTuple", false, IntType(), arguments,
                Action(IntType(), arguments,
                    AssignLocal("__arg_0__", Number(7), IntType()),
                    AssignLocal("__arg_1__", Add(Variable("__arg_0__"), Number(1)), IntType()),
                    Return(failAfterWriting ? Variable("missing") : Variable("__arg_0__"))));
            function.Modifier = NeoMemberModifierKind.Static;
            using NeoClient client = BuildClient(new JsonMember[] { function }, ReceiverClass());
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var caller = new NeoScriptScope();
            var target = new WriteTarget { pointer = Variable("tuple"), typeInfo = rootType, writability = WritabilityKind.Local };
            // Capturing the alias must not read the as-yet uninitialized root.
            var left = (NeoOutReference)NSGetterEvaluator.EvalPointer(TupleOut(target, 0, 0), caller, ctx)!;
            var right = (NeoOutReference)NSGetterEvaluator.EvalPointer(TupleOut(target, 0, 1), caller, ctx)!;
            var root = new RuntimeTuple(new object?[] { new RuntimeTuple(new object?[] { 1d, 2d }, pair), 3d }, rootType);
            caller.SetLocal("tuple", root);
            var forwarding = new NeoScriptScope();
            forwarding.SetLocal("forwarded", left);
            var forwarded = new OutPointer
            {
                type = PointerKind.Out,
                target = new WriteTarget { pointer = Variable("forwarded"), typeInfo = IntType(), writability = WritabilityKind.Local },
            };
            object?[] values = { NSGetterEvaluator.EvalPointer(forwarded, forwarding, ctx), right };
            if (failAfterWriting)
                Assert.Catch(() => NeoNSFunctionRuntime.InvokeImmediate(client, function.id, null, values, ctx));
            else
                Assert.That(NeoNSFunctionRuntime.InvokeImmediate(client, function.id, null, values, ctx), Is.EqualTo(7d));
            Assert.That(left.Read(), Is.EqualTo(7d));
            Assert.That(right.Read(), Is.EqualTo(8d));
            Assert.That(caller.TryGetValue("tuple", out object? result), Is.True);
            var current = (RuntimeTuple)result!;
            Assert.That(current[1], Is.EqualTo(3d));
            Assert.That(((RuntimeTuple)root[0]!)[0], Is.EqualTo(1d), "Replacing a tuple must not mutate the previous value.");
            Assert.That(((RuntimeTuple)root[0]!)[1], Is.EqualTo(2d));
        }

        [TestCase("[]")]
        [TestCase("[-1]")]
        [TestCase("[2]")]
        [TestCase("[0,0]")]
        [TestCase("[0.5]")]
        [TestCase("null")]
        public void P127OutRejectsInvalidTuplePaths(string path)
        {
            string json = "{\"type\":\"out\",\"target\":{\"pointer\":{\"type\":\"variable\",\"variableId\":\"tuple\"},\"typeInfo\":{\"type\":30,\"required\":true,\"elements\":[{\"typeInfo\":{\"type\":2,\"required\":true}},{\"typeInfo\":{\"type\":2,\"required\":true}}]},\"writability\":\"local\"},\"tuplePath\":" + path + "}";
            Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<Pointer>(json));
        }

        [Test]
        public void P127OutDiscardRejectsTuplePath()
        {
            Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<Pointer>("{\"type\":\"out\",\"target\":null,\"tuplePath\":[0]}"));
        }

        [Test]
        public void P127OutStoredMarkersArePositionChecked()
        {
            var parameter = Argument("result", MemberKind.Int);
            parameter.mode = NeoParameterModeKind.Out;
            Assert.That(NeoConstructorOut.Validate(parameter, NeoConstructorOut.Marker(), "__arg_0__"), Is.True);
            foreach (string json in new[] { "null", "0", "{\"out\":false}", "{\"out\":true,\"value\":3}" })
            {
                var error = Assert.Throws<InvalidOperationException>(() => NeoConstructorOut.Validate(parameter, Newtonsoft.Json.Linq.JToken.Parse(json), "__arg_0__"))!;
                Assert.That(error.Message, Is.EqualTo("Constructor out argument '__arg_0__' must be exactly {\"out\":true}."));
            }
            parameter.mode = null;
            var byValue = Assert.Throws<InvalidOperationException>(() => NeoConstructorOut.Validate(parameter, NeoConstructorOut.Marker(), "__arg_0__"))!;
            Assert.That(byValue.Message, Is.EqualTo("Constructor argument '__arg_0__' has an out marker, but the parameter is not out."));
        }

        [TestCase(MemberKind.Int, 0d)]
        [TestCase(MemberKind.String, null)]
        public void P127OutReplayStartsWithItsClosedDefaultAndUsesFreshSlots(MemberKind kind, object? expected)
        {
            using NeoClient client = BuildClient(Array.Empty<JsonMember>(), ReceiverClass());
            var type = new PrimitiveTypeInfo { type = kind, required = true };
            NeoOutReference first = NeoConstructorOut.Replay(client, type);
            NeoOutReference second = NeoConstructorOut.Replay(client, type);
            Assert.That(first.Read(), Is.EqualTo(expected));
            first.Write(5d);
            Assert.That(second.Read(), Is.EqualTo(expected));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void P127OutAliasesWriteImmediately(bool failAfterWriting)
        {
            var first = Argument("first", MemberKind.Int);
            var second = Argument("second", MemberKind.Int);
            first.mode = second.mode = NeoParameterModeKind.Out;
            var arguments = new[] { first, second };
            var body = Action(IntType(), arguments,
                AssignLocal("__arg_0__", Number(7), IntType()),
                AssignLocal("__arg_1__", Add(Variable("__arg_0__"), Number(1)), IntType()),
                Return(failAfterWriting ? Variable("missing") : Add(Variable("__arg_0__"), Variable("__arg_1__"))));
            var function = ScriptFunction("out-alias", "OutAlias", false, IntType(), arguments, body);
            function.Modifier = NeoMemberModifierKind.Static;
            using NeoClient client = BuildClient(new JsonMember[] { function }, ReceiverClass());
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var caller = new NeoScriptScope();
            // Taking an out address must not read this unassigned local.
            Pointer pointer = JsonConvert.DeserializeObject<Pointer>("{\"type\":\"out\",\"target\":{\"pointer\":{\"type\":\"variable\",\"variableId\":\"destination\"},\"typeInfo\":{\"type\":2,\"required\":true},\"writability\":\"local\"}}")!;
            object? alias = NSGetterEvaluator.EvalPointer(pointer, caller, ctx);
            var forwarding = new NeoScriptScope();
            forwarding.SetLocal("forwarded", alias);
            var forwarded = new OutPointer
            {
                type = PointerKind.Out,
                target = new WriteTarget { pointer = Variable("forwarded"), typeInfo = IntType(), writability = WritabilityKind.Local },
            };
            object?[] values = { alias, NSGetterEvaluator.EvalPointer(forwarded, forwarding, ctx) };
            if (failAfterWriting)
                Assert.Catch(() => NeoNSFunctionRuntime.InvokeImmediate(client, function.id, null, values, ctx));
            else
                Assert.That(NeoNSFunctionRuntime.InvokeImmediate(client, function.id, null, values, ctx), Is.EqualTo(16d));
            Assert.That(caller.TryGetValue("destination", out object? result), Is.True);
            Assert.That(result, Is.EqualTo(8d));
        }

        [Test]
        public void P127OutWritesAStoredFieldThroughItsOrdinaryAssignment()
        {
            var argument = Argument("value", MemberKind.Int);
            argument.mode = NeoParameterModeKind.Out;
            var function = ScriptFunction("out-field", "OutField", false, IntType(), new[] { argument },
                Action(IntType(), new[] { argument }, AssignLocal("__arg_0__", Number(12), IntType()), Return(Variable("__arg_0__"))));
            function.Modifier = NeoMemberModifierKind.Static;
            using NeoClient client = BuildMutationClient(function);
            NSGetterEvaluator.Context ctx = client.CreateGetterContext(NeoValueOwnership.Save);
            ctx.BindRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx));
            var caller = new NeoScriptScope();
            caller.SetLocal("__root__", ctx.rootValue);
            var pointer = new OutPointer
            {
                type = PointerKind.Out,
                bindings = new[] { new Variable { id = "captured-root", typeInfo = new ObjectTypeInfo { type = MemberKind.Object }, pointer = Variable("__root__") } },
                target = new WriteTarget { pointer = Key(Key(Variable("captured-root"), "Save"), "Level"), typeInfo = IntType(), writability = WritabilityKind.Save },
            };
            object? alias = NSGetterEvaluator.EvalPointer(pointer, caller, ctx);
            caller.SetLocal("__root__", null);
            Assert.That(NeoNSFunctionRuntime.InvokeImmediate(client, function.id, null, new[] { alias }, ctx), Is.EqualTo(12d));
            Assert.That(caller.TryGetValue("captured-root", out _), Is.False);
            caller.SetLocal("__root__", ctx.rootValue);
            Assert.That(NSGetterEvaluator.EvalPointer(RootLevel(), caller, ctx), Is.EqualTo(12d));
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, "save-level-value", out NumberMemberValue? row), Is.True);
            Assert.That(row!.value, Is.EqualTo(12d));
        }

        [Test]
        public void P127OutTupleFieldKeepsCapturedReceiverAndCurrentSibling()
        {
            var argument = Argument("value", MemberKind.Int);
            argument.mode = NeoParameterModeKind.Out;
            var function = ScriptFunction("out-tuple-field", "OutTupleField", false, IntType(), new[] { argument },
                Action(IntType(), new[] { argument }, AssignLocal("__arg_0__", Number(12), IntType()), Return(Variable("__arg_0__"))));
            function.Modifier = NeoMemberModifierKind.Static;
            ProjectData data;
            using (NeoClient seed = BuildMutationClient(function))
                data = seed.ProjectDataForRuntime;
            data.members["save-level"] = new TupleMember
            {
                id = "save-level",
                name = "Level",
                projectId = ProjectId,
                kind = MemberKind.Tuple,
                Requirement = NeoMemberRequirementKind.Required,
                elementMemberIds = new[] { "tuple-element", "tuple-element" },
            };
            data.members["tuple-element"] = new IntMember
            {
                id = "tuple-element",
                name = "Element",
                projectId = ProjectId,
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.values["save-level-value"] = new ArrayMemberValue { id = "save-level-value", value = new[] { "tuple-first", "tuple-second" } };
            data.values["tuple-first"] = new NumberMemberValue { id = "tuple-first", value = 1 };
            data.values["tuple-second"] = new NumberMemberValue { id = "tuple-second", value = 2 };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NSGetterEvaluator.Context ctx = client.CreateGetterContext(NeoValueOwnership.Save);
            ctx.BindRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx));
            var caller = new NeoScriptScope();
            caller.SetLocal("__root__", ctx.rootValue);
            var pair = new TupleTypeInfo
            {
                type = MemberKind.Tuple,
                required = true,
                elements = new[] { new TupleElementTypeInfo { typeInfo = IntType() }, new TupleElementTypeInfo { typeInfo = IntType() } },
            };
            var target = new WriteTarget { pointer = Key(Key(Variable("captured-root"), "Save"), "Level"), typeInfo = pair, writability = WritabilityKind.Save };
            var pointer = (OutPointer)TupleOut(target, 0);
            pointer.bindings = new[] { new Variable { id = "captured-root", typeInfo = new ObjectTypeInfo { type = MemberKind.Object }, pointer = Variable("__root__") } };
            var left = (NeoOutReference)NSGetterEvaluator.EvalPointer(pointer, caller, ctx)!;
            var rightPointer = (OutPointer)TupleOut(target, 1);
            rightPointer.bindings = pointer.bindings;
            var right = (NeoOutReference)NSGetterEvaluator.EvalPointer(rightPointer, caller, ctx)!;
            caller.SetLocal("__root__", null);
            right.Write(8d);
            Assert.That(NeoNSFunctionRuntime.InvokeImmediate(client, function.id, null, new object?[] { left }, ctx), Is.EqualTo(12d));
            Assert.That(left.Read(), Is.EqualTo(12d));
            Assert.That(right.Read(), Is.EqualTo(8d));
            Assert.That(caller.TryGetValue("captured-root", out _), Is.False);
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, "save-level-value", out ArrayMemberValue? row), Is.True);
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, row!.value![0], out NumberMemberValue? first), Is.True);
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, row.value[1], out NumberMemberValue? second), Is.True);
            Assert.That(first!.value, Is.EqualTo(12d));
            Assert.That(second!.value, Is.EqualTo(8d));
        }

        [Test]
        public void P127OutDiscardHasAnIndependentCell()
        {
            var argument = Argument("value", MemberKind.Int);
            argument.mode = NeoParameterModeKind.Out;
            var function = ScriptFunction("out-discard", "OutDiscard", false, IntType(), new[] { argument },
                Action(IntType(), new[] { argument }, AssignLocal("__arg_0__", Number(9), IntType()), Return(Variable("__arg_0__"))));
            function.Modifier = NeoMemberModifierKind.Static;
            using NeoClient client = BuildClient(new JsonMember[] { function }, ReceiverClass());
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var caller = new NeoScriptScope();
            Pointer pointer = JsonConvert.DeserializeObject<Pointer>("{\"type\":\"out\",\"target\":null}")!;
            object? alias = NSGetterEvaluator.EvalPointer(pointer, caller, ctx);
            Assert.That(NeoNSFunctionRuntime.InvokeImmediate(client, function.id, null, new[] { alias }, ctx), Is.EqualTo(9d));
            Assert.That(caller.LocalBindingCount, Is.Zero);
            Assert.Catch<NSGetterRuntimeError>(() => NeoNSFunctionRuntime.InvokeImmediate(client, function.id, null, new object?[] { 1 }, ctx));
        }
    }
}
