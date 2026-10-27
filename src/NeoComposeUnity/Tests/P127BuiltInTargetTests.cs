// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §12.1. A delegate bound to a built-in static stores the method
    /// .NET binds in place of a <c>memberId</c>:
    /// <c>{ kind: BuiltIn, declaring, name, arity, parameterTypes, explicitInterface? }</c>,
    /// whose parameter types name the method's own type parameters as
    /// <c>{ kind: MethodParam, index }</c>.
    /// </summary>
    public class P127BuiltInTargetTests
    {
        private const string Int = "{\"kind\":0,\"type\":{\"type\":2,\"required\":true}}";
        private const string Float = "{\"kind\":0,\"type\":{\"type\":4,\"required\":true}}";
        private const string Decimal = "{\"kind\":0,\"type\":{\"type\":20,\"required\":true}}";
        private const string StringType = "{\"kind\":0,\"type\":{\"type\":3,\"required\":true}}";
        private const string MethodParam0 = "{\"kind\":3,\"index\":0}";
        private const string NumberBaseOfInt = "{\"kind\":0,\"type\":{\"type\":22,\"required\":true,\"interfaceId\":\"inumberbase\"},\"bindings\":{\"t\":" + Int + "}}";

        // int.Parse(string)
        [TestCase("{\"kind\":0,\"declaring\":" + Int + ",\"name\":\"Parse\",\"arity\":0,\"parameterTypes\":[" + StringType + "]}")]
        // decimal.Compare as an EqualityComparer delegate of another type
        [TestCase("{\"kind\":0,\"declaring\":" + Decimal + ",\"name\":\"Compare\",\"arity\":0,\"parameterTypes\":[" + Decimal + "," + Decimal + "],\"delegateType\":{\"kind\":0,\"type\":{\"type\":25,\"required\":true},\"bindings\":{\"0\":" + Int + ",\"1\":" + Decimal + ",\"2\":" + Decimal + "}}}")]
        // int.CreateChecked<float>(float): its own T is a method param, at any depth
        [TestCase("{\"kind\":0,\"declaring\":" + Int + ",\"name\":\"CreateChecked\",\"arity\":1,\"parameterTypes\":[" + MethodParam0 + ",{\"kind\":0,\"type\":{\"type\":6,\"required\":true},\"bindings\":{\"0\":" + MethodParam0 + "}}],\"typeArguments\":[" + Float + "]}")]
        // int's explicit INumberBase<int>.IsZero
        [TestCase("{\"kind\":0,\"declaring\":" + Int + ",\"name\":\"IsZero\",\"arity\":0,\"parameterTypes\":[" + Int + "],\"explicitInterface\":" + NumberBaseOfInt + "}")]
        // Math.Max(int, int)
        [TestCase("{\"kind\":0,\"declaring\":\"Math\",\"name\":\"Max\",\"arity\":0,\"parameterTypes\":[" + Int + "," + Int + "]}")]
        public void BuiltInTargetsRoundTrip(string json)
        {
            NeoDelegateValue value = JsonConvert.DeserializeObject<NeoDelegateValue>(json)!;
            Assert.That(value.IsBuiltInTarget, Is.True);
            Assert.That(value.IsMemberTarget, Is.False);
            Assert.That(JsonConvert.SerializeObject(value), Is.EqualTo(json));
            Assert.That(JsonConvert.DeserializeObject<NeoDelegateValue>(json)!.builtIn, Is.EqualTo(value.builtIn));
            Assert.That(value.PersistedCopy().builtIn, Is.EqualTo(value.builtIn));
        }

        [TestCase("\"parameterTypes\":[{\"kind\":3,\"index\":1}],\"typeArguments\":[" + Float + "]", 1, "has index 1; its method has 1 type parameter(s)")]
        [TestCase("\"parameterTypes\":[{\"kind\":3,\"index\":0}]", 0, "has index 0; its method has 0 type parameter(s)")]
        [TestCase("\"parameterTypes\":[{\"kind\":3,\"index\":-1}],\"typeArguments\":[" + Float + "]", 1, "index -1")]
        [TestCase("\"parameterTypes\":[{\"kind\":3}],\"typeArguments\":[" + Float + "]", 1, "needs an integer 'index'")]
        [TestCase("\"parameterTypes\":[{\"kind\":3,\"index\":0,\"param\":\"t\"}],\"typeArguments\":[" + Float + "]", 1, "has 'param'")]
        [TestCase("\"parameterTypes\":[{\"kind\":0,\"type\":{\"type\":5,\"required\":true},\"bindings\":{\"0\":" + MethodParam0 + ",\"1\":" + Int + "}}],\"typeArguments\":[" + Float + "]", 1, "keys a Dictionary by a method type parameter")]
        [TestCase("\"parameterTypes\":[" + Int + "],\"typeArguments\":[" + MethodParam0 + "]", 1, "typeArguments[0] is a method type parameter")]
        [TestCase("\"parameterTypes\":[" + Int + "],\"explicitInterface\":{\"kind\":0,\"type\":{\"type\":22,\"required\":true,\"interfaceId\":\"i\"},\"bindings\":{\"t\":" + MethodParam0 + "}}", 0, "is a method type parameter")]
        public void MethodParamsAreLegalOnlyInParameterTypesWithinArity(string fields, int arity, string message)
        {
            string json = "{\"kind\":0,\"declaring\":" + Int + ",\"name\":\"M\",\"arity\":" + arity + "," + fields + "}";
            JsonSerializationException? error = Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<NeoDelegateValue>(json));
            Assert.That(error!.Message, Does.Contain(message));
        }

        [TestCase(MethodParam0)]
        [TestCase("{\"kind\":0,\"type\":{\"type\":6,\"required\":true},\"bindings\":{\"0\":" + MethodParam0 + "}}")]
        public void MethodParamsAreRefusedOutsideABuiltInTarget(string json)
        {
            JsonSerializationException? error = Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<TypeBinding>(json));
            Assert.That(error!.Message, Does.Contain("only a built-in target's parameterTypes hold one"));
            string row = "{\"id\":\"row\",\"kind\":6,\"value\":[],\"genericBindings\":{\"t\":" + json + "}}";
            Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<MemberValue>(row));
            TypeBinding inMemory = TypeBinding.MethodParam(0);
            foreach (TypeBindingPosition position in new[] { TypeBindingPosition.Stamp, TypeBindingPosition.Identity, TypeBindingPosition.TypePayload })
            {
                Assert.That(
                    TypeBindings.PositionViolation(inMemory, position),
                    Does.Contain("only a built-in target's parameterTypes hold one"));
            }
        }

        [TestCase("\"declaring\":\"Math\",\"name\":\"Max\",\"arity\":0,\"parameterTypes\":[],\"explicitInterface\":" + NumberBaseOfInt, "a static class implements no interface")]
        [TestCase("\"declaring\":" + Int + ",\"name\":\"M\",\"arity\":0,\"parameterTypes\":[],\"explicitInterface\":" + Int, "names a Int; it must be a closed interface")]
        [TestCase("\"declaring\":" + Int + ",\"name\":\"M\",\"arity\":0,\"parameterTypes\":[],\"explicitInterface\":{\"kind\":0,\"type\":{\"type\":22,\"required\":false,\"interfaceId\":\"i\"}}", "no nullable reference")]
        [TestCase("\"declaring\":" + Int + ",\"name\":\"M\",\"arity\":0,\"parameterTypes\":[],\"explicitInterface\":{\"kind\":1,\"owner\":{\"type\":22,\"interfaceId\":\"i\"},\"param\":\"t\"}", "only a Type payload holds an open type")]
        [TestCase("\"declaring\":" + Int + ",\"name\":\"M\",\"arity\":0,\"parameterTypes\":[{\"kind\":0,\"type\":{\"type\":3,\"required\":false}}]", "parameterTypes[0] is string?")]
        [TestCase("\"declaring\":\"\",\"name\":\"M\",\"arity\":0,\"parameterTypes\":[]", "needs 'declaring'")]
        [TestCase("\"declaring\":" + Int + ",\"name\":\"\",\"arity\":0,\"parameterTypes\":[]", "needs a non-empty 'name'")]
        [TestCase("\"declaring\":" + Int + ",\"name\":\"M\",\"arity\":-1,\"parameterTypes\":[]", "arity -1")]
        [TestCase("\"declaring\":" + Int + ",\"name\":\"M\",\"arity\":0", "needs a 'parameterTypes' array")]
        [TestCase("\"declaring\":" + Int + ",\"name\":\"M\",\"arity\":1,\"parameterTypes\":[]", "no 'typeArguments'")]
        [TestCase("\"declaring\":" + Int + ",\"name\":\"M\",\"arity\":1,\"parameterTypes\":[],\"typeArguments\":[" + Int + "," + Int + "]", "2 type argument(s) for arity 1")]
        [TestCase("\"declaring\":" + Int + ",\"name\":\"M\",\"arity\":0,\"parameterTypes\":[],\"typeArguments\":[]", "arity 0 and 'typeArguments'")]
        [TestCase("\"declaring\":" + Int + ",\"name\":\"M\",\"arity\":0,\"parameterTypes\":[],\"typeArguments\":[" + Int + "]", "1 type argument(s) for arity 0")]
        [TestCase("\"declaring\":" + Int + ",\"name\":\"M\",\"arity\":0,\"parameterTypes\":[],\"delegateType\":" + Int, "it must be a closed NeoDelegate")]
        [TestCase("\"declaring\":" + Int + ",\"name\":\"M\",\"arity\":0,\"parameterTypes\":[],\"valueId\":null", "has 'valueId'")]
        public void MalformedBuiltInTargetsAreRefused(string fields, string message)
        {
            JsonSerializationException? error = Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<NeoDelegateValue>("{\"kind\":0," + fields + "}"));
            Assert.That(error!.Message, Does.Contain(message));
        }

        [TestCase("{\"kind\":1,\"declaring\":\"Math\",\"name\":\"Max\",\"arity\":0,\"parameterTypes\":[]}", "unknown member target kind 1")]
        [TestCase("{\"kind\":0,\"memberId\":\"m\",\"declaring\":\"Math\",\"name\":\"Max\",\"arity\":0,\"parameterTypes\":[]}", "exactly one of")]
        public void ATargetIsExactlyOneArm(string json, string message)
        {
            JsonSerializationException? error = Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<NeoDelegateValue>(json));
            Assert.That(error!.Message, Does.Contain(message));
        }

        [Test]
        public void BuiltInListenersKeepTheSetInvariant()
        {
            const string target = "{\"kind\":0,\"declaring\":\"Debug\",\"name\":\"Log\",\"arity\":0,\"parameterTypes\":[" + StringType + "]}";
            string single = "{\"listeners\":[" + target + ",{\"memberId\":\"m\",\"valueId\":null}]}";
            NeoActionValue action = JsonConvert.DeserializeObject<NeoActionValue>(single)!;
            Assert.That(action.listeners[0].IsBuiltInTarget, Is.True);
            Assert.That(JsonConvert.SerializeObject(action), Is.EqualTo(single));
            // The listener identity is an in-memory key; nothing writes it.
            Assert.That(NeoActionValue.ListenerIdentity(action.listeners[0]), Does.StartWith("\u0000"));
            Assert.That(JsonConvert.SerializeObject(action), Does.Not.Contain("\u0000").And.Not.Contain("\\u0000"));

            const string dictionary = "{\"kind\":0,\"type\":{\"type\":5,\"required\":true},\"bindings\":{\"0\":" + StringType + ",\"1\":" + Int + "}}";
            const string reordered = "{\"kind\":0,\"type\":{\"type\":5,\"required\":true},\"bindings\":{\"1\":" + Int + ",\"0\":" + StringType + "}}";
            string duplicate = "{\"listeners\":["
                + "{\"kind\":0,\"declaring\":\"Debug\",\"name\":\"Log\",\"arity\":0,\"parameterTypes\":[" + dictionary + "]},"
                + "{\"kind\":0,\"declaring\":\"Debug\",\"name\":\"Log\",\"arity\":0,\"parameterTypes\":[" + reordered + "]}]}";
            JsonSerializationException? error = Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<NeoActionValue>(duplicate));
            Assert.That(error!.Message, Does.Contain("duplicates an earlier listener identity"));

            string typed = "{\"listeners\":[{\"kind\":0,\"declaring\":\"Debug\",\"name\":\"Log\",\"arity\":0,\"parameterTypes\":[],\"delegateType\":{\"kind\":0,\"type\":{\"type\":25,\"required\":true},\"bindings\":{\"0\":" + Int + "}}}]}";
            error = Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<NeoActionValue>(typed));
            Assert.That(error!.Message, Does.Contain("a listener always has its action's delegate type"));
        }
    }
}
