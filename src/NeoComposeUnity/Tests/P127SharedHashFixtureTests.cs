// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript.Hashing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public sealed class P127SharedHashFixtureTests
    {
        private const string Path = "Packages/com.ryanbliss.neocompose/Tests/p127-hash-parity-fixture.json";
        private static JObject Fixture => JObject.Parse(PreserveSurrogates(File.ReadAllText(Path)));

        // Json.NET replaces lone escaped surrogates. Preserve the fixture's
        // UTF-16 code units before tokenization, including malformed pairs.
        private static string PreserveSurrogates(string json)
        {
            var text = new StringBuilder(json.Length);
            for (int i = 0; i < json.Length; i++)
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    if (json[i + 1] == 'u' && i + 5 < json.Length
                        && ushort.TryParse(json.Substring(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code)
                        && char.IsSurrogate((char)code))
                    {
                        text.Append((char)code);
                        i += 5;
                        continue;
                    }
                    text.Append(json[i++]);
                }
                text.Append(json[i]);
            }
            return text.ToString();
        }

        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (JObject item in (JArray)Fixture["cases"]!)
                yield return new TestCaseData(item).SetName("SharedHash_" + (string)item["id"]!);
        }

        [Test]
        public void FixturePinsRevisionAndCaseCount()
        {
            Assert.That((int)Fixture["revision"]!, Is.EqualTo(1));
            Assert.That(((JArray)Fixture["cases"]!).Count, Is.EqualTo(36));
        }

        [TestCaseSource(nameof(Cases))]
        public void SdkHashMatchesCompose(JObject item)
        {
            var type = TypeBindings.Read((JObject)item["type"]!, "shared hash case");
            Assert.That(Hash(type, item["value"]!), Is.EqualTo((int)item["hash"]!));
        }

        private static int Hash(TypeBinding type, JToken value)
        {
            if (value.Type == JTokenType.Null)
                return 0;
            switch (type.Head!.Type)
            {
                case MemberKind.Int:
                    return (int)value;
                case MemberKind.Bool:
                    return NeoHashes.Bool((bool)value);
                case MemberKind.String:
                    return NeoHashes.String((string)value!);
                case MemberKind.Decimal:
                    return NeoHashes.Decimal((string)value!);
                case MemberKind.Float:
                    float number = value.Type != JTokenType.String ? (float)value : (string)value! switch
                    {
                        "NaN" => float.NaN,
                        "Infinity" => float.PositiveInfinity,
                        "-Infinity" => float.NegativeInfinity,
                        "-0" => BitConverter.Int32BitsToSingle(int.MinValue),
                        var text => float.Parse(text, CultureInfo.InvariantCulture),
                    };
                    return NeoHashes.Float(number);
                case MemberKind.Enum:
                    Assert.That(type.Head.EnumId, Is.EqualTo("system_705ccc39-e46e-4c9f-af3e-3ec8fd818709"));
                    return (int)NeoPlayDirectionOptions.FromOptionId((string)value[0]!);
                case MemberKind.Tuple:
                    return NeoTuples.TupleHash(((JArray)value).ToArray(), (index, element) => Hash(type.At(index), element));
                default:
                    throw new InvalidOperationException("Unsupported shared hash type: " + type.Head.Type);
            }
        }
    }
}
