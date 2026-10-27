// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §12.1 "Where": a captured local has a static type, so a stored
    /// closure writes its float captures through the float codec and its
    /// decimal captures as canonical strings, and reads them back by the
    /// capture parameter's type.
    /// </summary>
    public partial class NSFunctionRuntimeTests
    {
        private static readonly string[] P127CaptureIds =
        {
            "__capture_0_0__",
            "__capture_0_1__",
            "__capture_0_2__",
            "__capture_0_3__",
        };

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void P127_StoredClosureCapturesKeepFloatAndDecimalForms(int returned)
        {
            TypeInfo[] captureTypes = { FloatType(), ListType(FloatType()), DecimalType(), IntType() };
            using NeoClient client = BuildClient(Array.Empty<JsonMember>(), ReceiverClass());
            var context = new NSGetterEvaluator.Context(client, thisValue: null, rootValue: null);
            object? closure = NSGetterEvaluator.Evaluate(
                P127CaptureFactory(captureTypes, returned),
                context,
                new object?[] { -0d, new object?[] { -0d, double.NaN, 1.5d }, "-0.00", 0d * -1d });

            JObject written = JObject.Parse(JsonConvert.SerializeObject(closure));
            JArray captures = (JArray)written["captures"]!;
            Assert.That(captures[0].Value<string>(), Is.EqualTo("-0"));
            Assert.That(captures[1].ToString(Formatting.None), Is.EqualTo("[\"-0\",\"NaN\",1.5]"));
            Assert.That(captures[2].Value<string>(), Is.EqualTo("-0.00"));
            // An int's -0 is a number: only a float position writes the symbol.
            Assert.That(captures[3].Type, Is.Not.EqualTo(JTokenType.String));

            NeoDelegateValue persisted = JsonConvert.DeserializeObject<NeoDelegateValue>(written.ToString())!;
            object? result = NSGetterEvaluator.InvokeDelegate(persisted, Array.Empty<object?>(), context);
            switch (returned)
            {
                case 0:
                    Assert.That(double.IsNegative(Convert.ToDouble(result)), Is.True);
                    break;
                case 1:
                    var entries = (object?[])result!;
                    Assert.That(double.IsNegative(Convert.ToDouble(entries[0])), Is.True);
                    Assert.That(entries[1], Is.NaN);
                    Assert.That(Convert.ToDouble(entries[2]), Is.EqualTo(1.5d));
                    // The stored capture itself is untouched.
                    Assert.That(((object?[])persisted.captures![1]!)[0], Is.EqualTo("-0"));
                    break;
                case 2:
                    Assert.That(result, Is.EqualTo("-0.00"));
                    break;
                default:
                    Assert.That(Convert.ToDouble(result), Is.EqualTo(0d));
                    break;
            }
        }

        [Test]
        public void P127_StoredClosureWritesAFunctionGenericCaptureByItsClosedType()
        {
            var typeParameter = new FunctionGenericTypeInfo
            {
                type = MemberKind.Generic,
                required = true,
                ownerMemberId = "p127-generic-owner",
                index = 0,
            };
            using NeoClient client = BuildClient(Array.Empty<JsonMember>(), ReceiverClass());
            var context = new NSGetterEvaluator.Context(client, thisValue: null, rootValue: null);
            context.BindFunctionTypeArguments(new TypeInfo[] { FloatType() });
            object? closure = NSGetterEvaluator.Evaluate(
                P127CaptureFactory(new TypeInfo[] { typeParameter }, 0),
                context,
                new object?[] { double.PositiveInfinity });

            JObject written = JObject.Parse(JsonConvert.SerializeObject(closure));
            Assert.That(written["captures"]![0]!.Value<string>(), Is.EqualTo("Infinity"));
            // A persisted copy keeps the closed types it writes by.
            JObject copied = JObject.Parse(JsonConvert.SerializeObject(((NeoDelegateValue)closure!).PersistedCopy()));
            Assert.That(copied["captures"]![0]!.Value<string>(), Is.EqualTo("Infinity"));
        }

        [Test]
        public void P127_ClosuresShareClosedCaptureTypesWithoutTypeParameters()
        {
            TypeInfo[] captureTypes = { FloatType(), IntType() };
            using NeoClient client = BuildClient(Array.Empty<JsonMember>(), ReceiverClass());
            var context = new NSGetterEvaluator.Context(client, thisValue: null, rootValue: null);
            FunctionWithReturnType factory = P127CaptureFactory(captureTypes, 0);
            var first = (NeoDelegateValue)NSGetterEvaluator.Evaluate(factory, context, new object?[] { 1d, 2d })!;
            var second = (NeoDelegateValue)NSGetterEvaluator.Evaluate(factory, context, new object?[] { -0d, 3d })!;

            Assert.That(second.captureTypes, Is.SameAs(first.captureTypes));
            Assert.That(first.captureTypes![0], Is.SameAs(captureTypes[0]));
            Assert.That(first.captureTypes[1], Is.Null);
            Assert.That(JObject.Parse(JsonConvert.SerializeObject(second))["captures"]![0]!.Value<string>(), Is.EqualTo("-0"));
        }

        [Test]
        public void P127_GenericCapturesCloseForEachClosure()
        {
            var typeParameter = new FunctionGenericTypeInfo
            {
                type = MemberKind.Generic,
                required = true,
                ownerMemberId = "p127-generic-owner",
                index = 0,
            };
            using NeoClient client = BuildClient(Array.Empty<JsonMember>(), ReceiverClass());
            FunctionWithReturnType factory = P127CaptureFactory(new TypeInfo[] { typeParameter }, 0);
            var floatContext = new NSGetterEvaluator.Context(client, thisValue: null, rootValue: null);
            floatContext.BindFunctionTypeArguments(new TypeInfo[] { FloatType() });
            var decimalContext = new NSGetterEvaluator.Context(client, thisValue: null, rootValue: null);
            decimalContext.BindFunctionTypeArguments(new TypeInfo[] { DecimalType() });

            var floats = (NeoDelegateValue)NSGetterEvaluator.Evaluate(factory, floatContext, new object?[] { double.NaN })!;
            var decimalClosure = (NeoDelegateValue)NSGetterEvaluator.Evaluate(factory, decimalContext, new object?[] { "-0.0" })!;

            Assert.That(floats.captureTypes![0]!.type, Is.EqualTo(MemberKind.Float));
            Assert.That(decimalClosure.captureTypes![0]!.type, Is.EqualTo(MemberKind.Decimal));
        }

        [Test]
        public void P127_LoadedClosureDecodesItsCapturesOnce()
        {
            TypeInfo[] captureTypes = { ListType(FloatType()) };
            using NeoClient client = BuildClient(Array.Empty<JsonMember>(), ReceiverClass());
            var context = new NSGetterEvaluator.Context(client, thisValue: null, rootValue: null);
            object? closure = NSGetterEvaluator.Evaluate(
                P127CaptureFactory(captureTypes, 0),
                context,
                new object?[] { new object?[] { double.NaN, -0d } });
            string written = JsonConvert.SerializeObject(closure);
            NeoDelegateValue persisted = JsonConvert.DeserializeObject<NeoDelegateValue>(written)!;
            object? stored = persisted.captures![0];

            NSGetterEvaluator.InvokeDelegate(persisted, Array.Empty<object?>(), context);
            object? decoded = persisted.decodedCaptures![0];
            NSGetterEvaluator.InvokeDelegate(persisted, Array.Empty<object?>(), context);

            Assert.That(((object?[])decoded!)[0], Is.NaN);
            Assert.That(persisted.decodedCaptures[0], Is.SameAs(decoded));
            // The stored capture is never edited in place, so the closure
            // writes the same JSON a worker might be serializing.
            Assert.That(persisted.captures[0], Is.SameAs(stored));
            Assert.That(JsonConvert.SerializeObject(persisted), Is.EqualTo(written));
        }

        [Test]
        public void P127_TupleElementsWriteAndReadByTheirTypes()
        {
            var tupleType = new TupleTypeInfo
            {
                type = MemberKind.Tuple,
                required = true,
                elements = new[]
                {
                    new TupleElementTypeInfo { typeInfo = FloatType() },
                    new TupleElementTypeInfo { typeInfo = IntType() },
                    new TupleElementTypeInfo { typeInfo = ListType(FloatType()) },
                },
            };
            var tuple = new RuntimeTuple(new object?[] { -0d, 0d * -1d, new object?[] { double.NegativeInfinity } }, tupleType);
            JObject written = JObject.Parse(JsonConvert.SerializeObject(tuple));
            JArray elements = (JArray)written["elements"]!;
            Assert.That(elements[0].Value<string>(), Is.EqualTo("-0"));
            Assert.That(elements[1].Type, Is.Not.EqualTo(JTokenType.String));
            Assert.That(elements[2].ToString(Formatting.None), Is.EqualTo("[\"-Infinity\"]"));

            object? stored = NeoJsonRuntimeValue.From(written);
            var read = (IDictionary<string, object?>)NeoStoredNumberJsonConverter.ReadTyped(stored, tupleType)!;
            var readElements = (object?[])read["elements"]!;
            Assert.That(double.IsNegative((double)readElements[0]!), Is.True);
            Assert.That(((object?[])readElements[2]!)[0], Is.EqualTo(double.NegativeInfinity));
            Assert.That(((object?[])((IDictionary<string, object?>)stored!)["elements"]!)[0], Is.EqualTo("-0"));
        }

        [Test]
        public void P127_ReadTypedLeavesValuesWithoutStoredNumbersAlone()
        {
            var entries = new object?[] { "-0", "NaN" };
            Assert.That(NeoStoredNumberJsonConverter.ReadTyped(entries, ListType(StringType())), Is.SameAs(entries));
            var floats = new object?[] { 1.5d, 2L };
            Assert.That(NeoStoredNumberJsonConverter.ReadTyped(floats, ListType(FloatType())), Is.SameAs(floats));
            var byName = new Dictionary<string, object?> { ["a"] = "-0" };
            var decoded = (IDictionary<string, object?>)NeoStoredNumberJsonConverter.ReadTyped(byName, DictionaryType(FloatType()))!;
            Assert.That(double.IsNegative((double)decoded["a"]!), Is.True);
            Assert.That(byName["a"], Is.EqualTo("-0"));
        }

        /// <summary>
        /// A factory whose parameters are captured, in order, by a
        /// no-argument closure returning capture <paramref name="returned"/>.
        /// </summary>
        private static FunctionWithReturnType P127CaptureFactory(TypeInfo[] captureTypes, int returned)
        {
            var closureParameters = new List<Variable>
            {
                Parameter("__this__", NullType()),
                Parameter("__root__", NullType()),
            };
            var factoryParameters = new List<Variable>(closureParameters);
            var capturePointers = new Pointer[captureTypes.Length];
            for (int index = 0; index < captureTypes.Length; index++)
            {
                closureParameters.Add(Parameter(P127CaptureIds[index], captureTypes[index]));
                factoryParameters.Add(Parameter($"__arg_{index}__", captureTypes[index]));
                capturePointers[index] = Variable($"__arg_{index}__");
            }
            var delegateType = new DelegateTypeInfo
            {
                type = MemberKind.NSDelegate,
                required = true,
                returnTypeInfo = captureTypes[returned],
                argumentTypes = Array.Empty<TypeInfo>(),
            };
            return new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = factoryParameters.ToArray(),
                instructions = new Instruction[]
                {
                    Return(new DelegateClosurePointer
                    {
                        type = PointerKind.DelegateClosure,
                        typeInfo = delegateType,
                        action = new FunctionWithReturnType
                        {
                            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                            parameters = closureParameters.ToArray(),
                            instructions = new Instruction[] { Return(Variable(P127CaptureIds[returned])) },
                            typeInfo = captureTypes[returned],
                        },
                        captures = capturePointers,
                        code = "() => captured",
                    }),
                },
                typeInfo = delegateType,
            };
        }
    }
}
