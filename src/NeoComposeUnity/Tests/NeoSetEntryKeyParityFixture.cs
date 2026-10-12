// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
//
// VENDORED — DO NOT HAND-EDIT.
// Source of record:
// neo-compose/src/models/members/set-entry-key-parity-fixture.json
// P108 §3 Set entry key parity fixture. The TypeScript setEntryKey and the
// C# NeoSetEntryKey read the same cases, so a Set that dedupes one way on the
// web cannot dedupe another way in Unity.
//
// To re-vendor: copy the JSON verbatim and double every `"` for the C#
// verbatim string. Consumed here by NeoSetEntryKeyParityTests and on the web
// by packages/models/src/members/set-entry-keys.test.ts.

#nullable enable

namespace NeoCompose.Tests
{
    public static class NeoSetEntryKeyParityFixture
    {
        public const string Json = @"{
  ""$comment"": ""P108 \u00a73 Set entry keys, shared verbatim by setEntryKey (TypeScript) and NeoSetEntryKey (C#). Float components key by their float32 bit pattern as a signed integer."",
  ""cases"": [
    {
      ""name"": ""bool true"",
      ""kind"": ""Bool"",
      ""value"": true,
      ""key"": ""true""
    },
    {
      ""name"": ""int"",
      ""kind"": ""Int"",
      ""value"": 7,
      ""key"": ""7""
    },
    {
      ""name"": ""negative int"",
      ""kind"": ""Int"",
      ""value"": -3,
      ""key"": ""-3""
    },
    {
      ""name"": ""float zero"",
      ""kind"": ""Float"",
      ""value"": 0,
      ""key"": ""0""
    },
    {
      ""name"": ""float negative zero matches zero"",
      ""kind"": ""Float"",
      ""value"": -0.0,
      ""key"": ""0""
    },
    {
      ""name"": ""float rounds to float32"",
      ""kind"": ""Float"",
      ""value"": 0.1,
      ""key"": ""1036831949""
    },
    {
      ""name"": ""float32 stored value"",
      ""kind"": ""Float"",
      ""value"": 0.10000000149011612,
      ""key"": ""1036831949""
    },
    {
      ""name"": ""decimal integer"",
      ""kind"": ""Decimal"",
      ""value"": ""1"",
      ""key"": ""1""
    },
    {
      ""name"": ""decimal trailing zero"",
      ""kind"": ""Decimal"",
      ""value"": ""1.0"",
      ""key"": ""1""
    },
    {
      ""name"": ""decimal trailing zeros"",
      ""kind"": ""Decimal"",
      ""value"": ""1.500"",
      ""key"": ""1.5""
    },
    {
      ""name"": ""decimal negative zero"",
      ""kind"": ""Decimal"",
      ""value"": ""-0.0"",
      ""key"": ""0""
    },
    {
      ""name"": ""decimal keeps integer zeros"",
      ""kind"": ""Decimal"",
      ""value"": ""100"",
      ""key"": ""100""
    },
    {
      ""name"": ""plain string"",
      ""kind"": ""String"",
      ""value"": ""recipe.jam"",
      ""key"": ""recipe.jam""
    },
    {
      ""name"": ""plain string is case-sensitive"",
      ""kind"": ""String"",
      ""value"": ""Recipe.Jam"",
      ""key"": ""Recipe.Jam""
    },
    {
      ""name"": ""vector2"",
      ""kind"": ""Vector2"",
      ""value"": {
        ""x"": 1.5,
        ""y"": -0.0
      },
      ""key"": ""1069547520,0""
    },
    {
      ""name"": ""vector2 rounds components to float32"",
      ""kind"": ""Vector2"",
      ""value"": {
        ""x"": 0.1,
        ""y"": 2
      },
      ""key"": ""1036831949,1073741824""
    },
    {
      ""name"": ""vector2int"",
      ""kind"": ""Vector2Int"",
      ""value"": {
        ""x"": 3,
        ""y"": 4
      },
      ""key"": ""3,4""
    },
    {
      ""name"": ""vector3"",
      ""kind"": ""Vector3"",
      ""value"": {
        ""x"": 1,
        ""y"": 2,
        ""z"": 3
      },
      ""key"": ""1065353216,1073741824,1077936128""
    },
    {
      ""name"": ""vector3int"",
      ""kind"": ""Vector3Int"",
      ""value"": {
        ""x"": -1,
        ""y"": 0,
        ""z"": 1
      },
      ""key"": ""-1,0,1""
    },
    {
      ""name"": ""color"",
      ""kind"": ""Color"",
      ""value"": {
        ""r"": 1,
        ""g"": 0.5,
        ""b"": 0,
        ""a"": 1
      },
      ""key"": ""1065353216,1056964608,0,1065353216""
    },
    {
      ""name"": ""sprite"",
      ""kind"": ""Sprite"",
      ""value"": {
        ""fileId"": ""file-1"",
        ""sliceIndex"": 2
      },
      ""key"": ""file-1:2""
    },
    {
      ""name"": ""empty sprite"",
      ""kind"": ""Sprite"",
      ""value"": {
        ""fileId"": """",
        ""sliceIndex"": 0
      },
      ""key"": "":0""
    },
    {
      ""name"": ""audio"",
      ""kind"": ""Audio"",
      ""value"": {
        ""fileId"": ""clip-1""
      },
      ""key"": ""clip-1""
    },
    {
      ""name"": ""enum entry"",
      ""kind"": ""Enum"",
      ""value"": [""option-a""],
      ""key"": ""option-a""
    },
    {
      ""name"": ""class entry is its value id"",
      ""kind"": ""Class"",
      ""id"": ""value-1"",
      ""value"": {
        ""Name"": ""value-2""
      },
      ""key"": ""value-1""
    },
    {
      ""name"": ""class entries with equal fields stay distinct"",
      ""kind"": ""Class"",
      ""id"": ""value-2"",
      ""value"": {
        ""Name"": ""value-2""
      },
      ""key"": ""value-2""
    }
  ]
}
";
    }
}
