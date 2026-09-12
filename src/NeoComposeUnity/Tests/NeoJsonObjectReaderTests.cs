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
    }
}
