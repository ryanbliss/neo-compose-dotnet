// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §1.2. A stored type is a <see cref="TypeBinding"/>: a head with
    /// no argument fields and, for a generic head, a binding per param, keyed
    /// by declared param id or built-in position. The wire form matches the
    /// server's <c>TTypeBinding</c> and a stamp holds only closed bindings.
    /// </summary>
    public class P127TypeBindingTests
    {
        private const string Int = "{\"kind\":0,\"type\":{\"type\":2,\"required\":true}}";

        [TestCase("{\"kind\":0,\"type\":{\"type\":2,\"required\":false}}")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":7,\"required\":true,\"classId\":\"box\"},\"bindings\":{\"t\":" + Int + "}}")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":22,\"required\":true,\"interfaceId\":\"i\"}}")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":8,\"required\":true,\"enumId\":\"e\"}}")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":31,\"required\":true,\"objectType\":3}}")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":6,\"required\":true},\"bindings\":{\"0\":" + Int + "}}")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":5,\"required\":true},\"bindings\":{\"0\":{\"kind\":0,\"type\":{\"type\":3,\"required\":true}},\"1\":" + Int + "}}")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":29,\"required\":true},\"bindings\":{\"0\":{\"kind\":0,\"type\":{\"type\":7,\"required\":true,\"classId\":\"seed\"}}}}")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":30,\"required\":true},\"bindings\":{\"0\":" + Int + "}}")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":25,\"required\":true},\"bindings\":{\"0\":{\"kind\":0,\"type\":{\"type\":\"Void\",\"required\":true}},\"1\":" + Int + "}}")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":26,\"required\":true}}")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":\"Opaque\",\"required\":true,\"name\":\"UnityEngine.Object\"}}")]
        [TestCase("{\"kind\":1,\"owner\":{\"type\":7,\"classId\":\"box\"},\"param\":\"t\"}")]
        [TestCase("{\"kind\":1,\"owner\":{\"type\":5},\"param\":\"1\"}")]
        [TestCase("{\"kind\":2,\"type\":{\"type\":30,\"arity\":2}}")]
        [TestCase("{\"kind\":2,\"type\":{\"type\":\"Nullable\"}}")]
        public void EveryShapeRoundTrips(string json)
        {
            TypeBinding binding = JsonConvert.DeserializeObject<TypeBinding>(json)!;
            Assert.That(JsonConvert.SerializeObject(binding), Is.EqualTo(json));
            Assert.That(JsonConvert.DeserializeObject<TypeBinding>(json), Is.EqualTo(binding));
        }

        [TestCase("{\"kind\":0,\"type\":{\"type\":9,\"required\":true}}", "Lookup head")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":21,\"required\":true}}", "generic parameter head")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":\"Unknown\",\"required\":true}}", "Unknown head")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":\"Void\",\"required\":true}}", "void outside")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":25,\"required\":true},\"bindings\":{\"0\":{\"kind\":0,\"type\":{\"type\":\"Void\",\"required\":false}}}}", "void is required")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":6,\"required\":true,\"entryTypeInfo\":{}},\"bindings\":{\"0\":" + Int + "}}", "'entryTypeInfo'")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":2,\"required\":true},\"bindings\":{}}", "empty bindings")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":2,\"required\":true},\"bindings\":{\"0\":" + Int + "}}", "not generic")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":6,\"required\":true}}", "at least 1")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":6,\"required\":true},\"bindings\":{\"1\":" + Int + "}}", "position \\\"0\\\"")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":5,\"required\":true},\"bindings\":{\"0\":" + Int + ",\"1\":" + Int + "}}", "string or an enum")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":5,\"required\":true},\"bindings\":{\"0\":{\"kind\":0,\"type\":{\"type\":3,\"required\":false}},\"1\":" + Int + "}}", "nullable type")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":6,\"required\":true},\"bindings\":{\"0\":{\"kind\":2,\"type\":{\"type\":6}}}}", "nests a definition")]
        [TestCase("{\"kind\":2,\"type\":{\"type\":30,\"arity\":0}}", "arity 0")]
        [TestCase("{\"kind\":1,\"owner\":{\"type\":6},\"param\":\"1\"}", "param 1 of a definition with 1")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":\"Opaque\",\"required\":true,\"name\":\"System.String\"}}", "unknown opaque type")]
        [TestCase("{\"kind\":4}", "unknown kind 4")]
        [TestCase("{\"kind\":3,\"index\":0}", "only a built-in target's parameterTypes hold one")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":6,\"required\":true},\"bindings\":{\"0\":{\"kind\":3,\"index\":0}}}", "only a built-in target's parameterTypes hold one")]
        public void MalformedBindingsAreRefused(string json, string message)
        {
            JsonSerializationException? error = Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<TypeBinding>(json));
            Assert.That(error!.Message, Does.Contain(message.Replace("\\\"", "\"")));
        }

        [TestCase("{\"kind\":1,\"owner\":{\"type\":7,\"classId\":\"box\"},\"param\":\"t\"}", "a stamp can't hold yet")]
        [TestCase("{\"kind\":2,\"type\":{\"type\":6}}", "generic definition")]
        [TestCase("{\"kind\":0,\"type\":{\"type\":\"Opaque\",\"required\":true,\"name\":\"UnityEngine.Object\"}}", "opaque type")]
        public void StampsHoldOnlyClosedBindings(string json, string message)
        {
            string row = "{\"id\":\"row\",\"kind\":6,\"value\":[],\"genericBindings\":{\"t\":" + json + "}}";
            JsonSerializationException? error = Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<MemberValue>(row));
            Assert.That(error!.Message, Does.Contain(message));
        }

        [Test]
        public void StampsRoundTripOnRows()
        {
            string json = "{\"t\":{\"kind\":0,\"type\":{\"type\":6,\"required\":true},\"bindings\":{\"0\":" + Int + "}}}";
            var row = new ArrayMemberValue
            {
                id = "row",
                value = new string[0],
                genericBindings = JsonConvert.DeserializeObject<Dictionary<string, TypeBinding>>(
                    json,
                    new GenericBindingsStampConverter()),
            };
            JObject written = JObject.Parse(JsonConvert.SerializeObject(row));
            Assert.That(written["genericBindings"]!.ToString(Formatting.None), Is.EqualTo(json));
            MemberValue read = JsonConvert.DeserializeObject<MemberValue>(written.ToString())!;
            Assert.That(NeoSemanticJson.MapsEqual(read.genericBindings, row.genericBindings), Is.True);
        }

        [Test]
        public void IdentityPositionsRefuseNullableReferences()
        {
            TypeBinding nullableClass = TypeBinding.Closed(new TypeHead(MemberKind.Class, false, classId: "seed"));
            TypeBinding nullableInt = TypeBinding.Closed(new TypeHead(MemberKind.Int, false));
            Assert.That(
                TypeBindings.PositionViolation(nullableClass, TypeBindingPosition.Identity),
                Does.Contain("no nullable reference"));
            Assert.That(TypeBindings.PositionViolation(nullableInt, TypeBindingPosition.Identity), Is.Null);
            Assert.That(TypeBindings.PositionViolation(nullableClass, TypeBindingPosition.Stamp), Is.Null);
        }

        [Test]
        public void ClosedTypeInfosConvertBothWays()
        {
            var typeInfo = new CollectionTypeInfo
            {
                type = MemberKind.Dictionary,
                required = true,
                keyEnumId = "slot",
                entryTypeInfo = new TupleTypeInfo
                {
                    type = MemberKind.Tuple,
                    required = true,
                    elements = new[]
                    {
                        new TupleElementTypeInfo
                        {
                            typeInfo = new ClassTypeInfo
                            {
                                type = MemberKind.Class,
                                required = false,
                                classId = "box",
                                typeArguments = new Dictionary<string, TypeInfo>
                                {
                                    ["t"] = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                                },
                            },
                        },
                        new TupleElementTypeInfo
                        {
                            typeInfo = new DelegateTypeInfo
                            {
                                type = MemberKind.NSDelegate,
                                required = true,
                                returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                                argumentTypes = new TypeInfo[]
                                {
                                    new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                                },
                            },
                        },
                    },
                },
            };
            TypeBinding binding = NeoTypeBindings.FromTypeInfo(typeInfo);
            Assert.That(
                JsonConvert.SerializeObject(binding),
                Is.EqualTo(
                    "{\"kind\":0,\"type\":{\"type\":5,\"required\":true},\"bindings\":{"
                    + "\"0\":{\"kind\":0,\"type\":{\"type\":8,\"required\":true,\"enumId\":\"slot\"}},"
                    + "\"1\":{\"kind\":0,\"type\":{\"type\":30,\"required\":true},\"bindings\":{"
                    + "\"0\":{\"kind\":0,\"type\":{\"type\":7,\"required\":false,\"classId\":\"box\"},\"bindings\":{\"t\":" + Int + "}},"
                    + "\"1\":{\"kind\":0,\"type\":{\"type\":25,\"required\":true},\"bindings\":{"
                    + "\"0\":{\"kind\":0,\"type\":{\"type\":\"Void\",\"required\":true}},"
                    + "\"1\":{\"kind\":0,\"type\":{\"type\":3,\"required\":true}}}}}}}}"));
            Assert.That(NeoTypeBindings.FromTypeInfo(NeoTypeBindings.ToTypeInfo(binding)), Is.EqualTo(binding));
        }

        private const string Pair = "{\"kind\":0,\"type\":{\"type\":30,\"required\":true,\"names\":[\"Pin\",\"\"]},\"bindings\":{\"0\":" + Int + ",\"1\":" + Int + "}}";

        [Test]
        public void TupleLabelsRoundTripOutsideTypeIdentity()
        {
            TypeBinding labeled = JsonConvert.DeserializeObject<TypeBinding>(Pair)!;
            Assert.That(JsonConvert.SerializeObject(labeled), Is.EqualTo(Pair));
            Assert.That(labeled.Head!.Names, Is.EqualTo(new[] { "Pin", "" }));
            TypeBinding plain = JsonConvert.DeserializeObject<TypeBinding>(Pair.Replace(",\"names\":[\"Pin\",\"\"]", ""))!;
            Assert.That(plain.Head!.Names, Is.Null);
            Assert.That(labeled, Is.EqualTo(plain));
            Assert.That(labeled.GetHashCode(), Is.EqualTo(plain.GetHashCode()));
            var elements = ((TupleTypeInfo)NeoTypeBindings.ToTypeInfo(labeled)).elements;
            Assert.That(elements[0].name, Is.EqualTo("Pin"));
            Assert.That(elements[1].name, Is.Null);
        }

        [TestCase("[\"Pin\"]", "2 elements")]
        [TestCase("[\"\",\"\"]", "no element has a label")]
        [TestCase("[\"Pin\",3]", "strings")]
        [TestCase("{}", "array")]
        public void MalformedTupleLabelsAreRefused(string names, string message)
        {
            string json = Pair.Replace("[\"Pin\",\"\"]", names);
            JsonSerializationException? error = Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<TypeBinding>(json));
            Assert.That(error!.Message, Does.Contain(message));
        }

        [Test]
        public void OnlyDeclarationsAndStampsKeepTupleLabels()
        {
            TypeBinding labeled = JsonConvert.DeserializeObject<TypeBinding>(Pair)!;
            TypeBinding nested = TypeBinding.Closed(
                new TypeHead(MemberKind.List, true),
                new Dictionary<string, TypeBinding> { ["0"] = labeled });
            Assert.That(TypeBindings.PositionViolation(nested, TypeBindingPosition.Stamp), Is.Null);
            Assert.That(TypeBindings.PositionViolation(nested, TypeBindingPosition.Argument), Is.Null);
            Assert.That(TypeBindings.PositionViolation(nested, TypeBindingPosition.Identity), Does.Contain("tuple labels"));
            Assert.That(TypeBindings.PositionViolation(nested, TypeBindingPosition.TypePayload), Does.Contain("tuple labels"));
            Assert.That(
                NeoTypeSource.Violation(new JObject { ["type"] = JObject.Parse(JsonConvert.SerializeObject(nested)) }),
                Does.Contain("tuple labels"));
        }

        [Test]
        public void OpenTypeInfosHaveNoBinding()
        {
            var open = new CollectionTypeInfo
            {
                type = MemberKind.List,
                required = true,
                entryTypeInfo = new GenericTypeInfo { type = MemberKind.Generic, required = true, genericParamId = "t" },
            };
            System.InvalidOperationException? error = Assert.Throws<System.InvalidOperationException>(
                () => NeoTypeBindings.FromTypeInfo(open));
            Assert.That(error!.Message, Does.Contain("'t' is open"));
        }

        /// <summary>
        /// The IR spells a <c>Set&lt;T&gt;</c> position as a Lookup bound to
        /// no collection. A type argument is the declared type, so
        /// <c>Holder&lt;Reference&lt;Set&lt;int&gt;&gt;&gt;</c> binds T to the
        /// Reference head over <c>Set&lt;int&gt;</c>, while the Lookup itself
        /// still has no binding (P127 §12.1).
        /// </summary>
        [Test]
        public void SetSpellingsBindAsTheirDeclaredSets()
        {
            var spelled = new ReferenceTypeInfo
            {
                type = MemberKind.Reference,
                required = true,
                targetTypeInfo = new LookupTypeInfo
                {
                    type = MemberKind.Lookup,
                    required = true,
                    entryTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                },
            };
            System.InvalidOperationException? read = Assert.Throws<System.InvalidOperationException>(
                () => NeoTypeBindings.FromTypeInfo(spelled));
            Assert.That(read!.Message, Does.Contain("A Lookup type has no stored type binding"));

            TypeBinding binding = NeoTypeBindings.FromTypeInfo(NeoTypeRules.Declared(spelled));
            Assert.That(
                binding,
                Is.EqualTo(TestTypeBindings.Reference(TestTypeBindings.Set(TestTypeBindings.Of(MemberKind.Int)))));
            Assert.That(spelled.targetTypeInfo, Is.TypeOf<LookupTypeInfo>(), "Declared copies; it never edits the IR.");
            Assert.That(
                NeoReferenceTypes.InvariantlyEqual(NeoTypeBindings.ToTypeInfo(binding), spelled),
                Is.True,
                "A Set binding reads back as the spelling its type position reads.");

            var bound = new LookupTypeInfo
            {
                type = MemberKind.Lookup,
                required = true,
                collectionMemberId = "tags",
                entryTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
            };
            System.InvalidOperationException? error = Assert.Throws<System.InvalidOperationException>(
                () => NeoTypeRules.Declared(bound));
            Assert.That(error!.Message, Does.Contain("bound to a collection is a read type"));
        }
    }
}
