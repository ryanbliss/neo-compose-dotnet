// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoJsonObjectReaderTests
    {
        [Test]
        public void TokenReaderBorrowsObjectAndStopsBeforeNextSibling()
        {
            var array = JArray.Parse("[{\"nested\":{\"value\":[1,2]}},{\"value\":3}]");
            var before = array.DeepClone();
            using var reader = array.CreateReader();
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.Read(), Is.True);
            Assert.That(NeoJsonObjectReader.Read(reader), Is.SameAs(array[0]));
            Assert.That(reader.TokenType, Is.EqualTo(JsonToken.EndObject));
            Assert.That(reader.Read(), Is.True);
            Assert.That(NeoJsonObjectReader.Read(reader), Is.SameAs(array[1]));
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.TokenType, Is.EqualTo(JsonToken.EndArray));
            Assert.That(JToken.DeepEquals(array, before), Is.True);
        }

        [Test]
        public void TextReaderLoadsOneObjectAndPreservesFollowingValue()
        {
            using var reader = new JsonTextReader(new StringReader("[{\"value\":1},2]"));
            reader.Read();
            reader.Read();
            Assert.That(NeoJsonObjectReader.Read(reader).Value<int>("value"), Is.EqualTo(1));
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.Value, Is.EqualTo(2L));
        }

        [Test]
        public void TypedValuesDoNotMutateOrRetainMutableInputSubtrees()
        {
            var input = JArray.Parse("[{\"id\":\"a\",\"value\":{\"x\":1,\"y\":2}},{\"id\":\"b\",\"value\":[\"a\"]},null,{\"id\":\"c\",\"value\":true}]");
            var before = input.DeepClone();
            var values = input.ToObject<List<MemberValue>>();
            Assert.That(values[0], Is.TypeOf<Vector2MemberValue>());
            Assert.That(values[1], Is.TypeOf<ArrayMemberValue>());
            Assert.That(values[2], Is.Null);
            Assert.That(values[3], Is.TypeOf<BoolMemberValue>());
            ((ArrayMemberValue)values[1]).value[0] = "changed";
            Assert.That(JToken.DeepEquals(input, before), Is.True);
        }

        [JsonConverter(typeof(ProbeConverter))]
        public class ProbeNode
        {
            public string type;
            public int value;
            public ProbeNode child;
        }

        public class ProbeConverter : DiscriminatedConverter<ProbeNode>
        {
            public override object ReadJson(JsonReader reader, System.Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader is CountingReader counted && reader.TokenType != JsonToken.Null)
                    counted.UnionVisits++;
                return base.ReadJson(reader, objectType, existingValue, serializer);
            }

            protected override System.Type ResolveSubclass(JToken discriminator) =>
                discriminator.Value<string>() == "node" ? typeof(ProbeNode) : null;
        }

        private sealed class CountingReader : JTokenReader
        {
            internal int Reads;
            internal int UnionVisits;
            internal CountingReader(JToken token) : base(token) { }
            public override bool Read()
            {
                Reads++;
                return base.Read();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PopulationConsumesNestedUnionsAndLeavesFollowingSibling(bool tokenReader)
        {
            const string json = "[{\"type\":\"node\",\"value\":1,\"child\":{\"type\":\"node\",\"value\":2}},null,{\"type\":\"node\",\"value\":3}]";
            JArray input = JArray.Parse(json);
            using JsonReader reader = tokenReader ? input.CreateReader() : new JsonTextReader(new StringReader(json));
            var nodes = JsonSerializer.CreateDefault().Deserialize<List<ProbeNode>>(reader);
            Assert.That(nodes.Count, Is.EqualTo(3));
            Assert.That(nodes[0].child.value, Is.EqualTo(2));
            Assert.That(nodes[1], Is.Null);
            Assert.That(nodes[2].value, Is.EqualTo(3));
            Assert.That(reader.TokenType, Is.EqualTo(JsonToken.EndArray));
            Assert.That(JToken.DeepEquals(input, JArray.Parse(json)), Is.True);
        }

        [Test]
        public void NestedUnionPopulationVisitsEachTokenOnce()
        {
            JToken tree = JValue.CreateNull();
            for (int i = 0; i < 64; i++)
                tree = new JObject { ["type"] = "node", ["value"] = i, ["child"] = tree };
            using var reader = new CountingReader(tree);
            var node = JsonSerializer.CreateDefault().Deserialize<ProbeNode>(reader);
            Assert.That(node.value, Is.EqualTo(63));
            for (int i = 63; i >= 0; i--, node = node.child)
                Assert.That(node.value, Is.EqualTo(i));
            Assert.That(node, Is.Null);
            Assert.That(reader.Reads, Is.EqualTo(64 * 7 + 1));
            Assert.That(reader.UnionVisits, Is.EqualTo(64), "Nested converters must share the reader instead of opening and walking subtrees again.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NestedUnknownDiscriminatorIsStillRejected(bool tokenReader)
        {
            const string json = "{\"type\":\"node\",\"child\":{\"type\":\"unknown\"}}";
            using JsonReader reader = tokenReader ? JObject.Parse(json).CreateReader() : new JsonTextReader(new StringReader(json));
            Assert.That(() => JsonSerializer.CreateDefault().Deserialize<ProbeNode>(reader),
                Throws.TypeOf<JsonSerializationException>().With.Message.Contains("Unknown discriminator"));
        }
    }
}
