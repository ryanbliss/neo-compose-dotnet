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
  ""$comment"": ""P108 §3 Set entry keys, shared verbatim by setEntryKey (TypeScript) and NeoSetEntryKey (C#). Float components key by their float32 bit pattern as a signed integer. P127 §9.5: an object or interface slot keys a row by id and a box by its canonical runtime type key, '|', and its payload's key. Keys decode stored floats first: \""-0\"" keys as 0 and every NaN as the one NaN, bits 0xFFC00000. A reference keys by its target id whether stored as the id or a one-id array, and a null at any reference-type position keys as \""null\"". keyEquivalent false (default true) means only a comparer can judge the entry, so no duplicate of it is refused. description is the entry as a Set duplicate error names it: a tuple as (1, 2), a box by its payload, and a row whose equality is identity by its id."",
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
    },
    {
      ""name"": ""object slot row is its value id (P127 §9.5)"",
      ""kind"": ""Object"",
      ""id"": ""row-1"",
      ""classId"": ""money"",
      ""value"": {},
      ""key"": ""row-1"",
      ""description"": ""row-1""
    },
    {
      ""name"": ""boxed int keys by type and payload"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""int"",
      ""value"": 3,
      ""key"": ""int|3"",
      ""description"": ""3""
    },
    {
      ""name"": ""boxed float is not boxed int"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""float"",
      ""value"": 3,
      ""key"": ""float|1077936128""
    },
    {
      ""name"": ""boxed negative zero float"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""float"",
      ""value"": -0.0,
      ""key"": ""float|0""
    },
    {
      ""name"": ""boxed decimal normalizes"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""decimal"",
      ""value"": ""1.10"",
      ""key"": ""decimal|1.1""
    },
    {
      ""name"": ""boxed string"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""string"",
      ""value"": ""a"",
      ""key"": ""string|a"",
      ""description"": ""a""
    },
    {
      ""name"": ""boxed Vector2Int"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""Vector2Int"",
      ""value"": {
        ""x"": 1,
        ""y"": 2
      },
      ""key"": ""Vector2Int|1,2""
    },
    {
      ""name"": ""boxed enum keys by option id"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""e:color"",
      ""value"": [""red""],
      ""key"": ""e:color|red"",
      ""description"": ""red""
    },
    {
      ""name"": ""boxed list is identity"",
      ""kind"": ""Object"",
      ""id"": ""list-1"",
      ""valueTypeKey"": ""List<int>"",
      ""value"": [""entry-1""],
      ""key"": ""list-1"",
      ""description"": ""list-1""
    },
    {
      ""name"": ""new object() is identity"",
      ""kind"": ""Object"",
      ""id"": ""object-1"",
      ""valueTypeKey"": ""object"",
      ""value"": {},
      ""key"": ""object-1""
    },
    {
      ""name"": ""null object entry"",
      ""kind"": ""Object"",
      ""value"": null,
      ""key"": ""null"",
      ""description"": ""null""
    },
    {
      ""name"": ""Type entry keys by its runtime type key"",
      ""kind"": ""Object"",
      ""objectType"": 2,
      ""value"": ""List<int>"",
      ""key"": ""List<int>""
    },
    {
      ""name"": ""CultureInfo entry keys by its invariant name"",
      ""kind"": ""Object"",
      ""objectType"": 3,
      ""value"": {
        ""invariant"": true
      },
      ""key"": """"
    },
    {
      ""name"": ""NumberFormatInfo entry is identity"",
      ""kind"": ""Object"",
      ""id"": ""format-1"",
      ""objectType"": 4,
      ""value"": {
        ""invariant"": true
      },
      ""key"": ""format-1""
    },
    {
      ""name"": ""held default comparer keys by payload"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""c:system_00000000-0000-4000-8000-0000000000c1<int>"",
      ""value"": {
        ""comparer"": 0,
        ""family"": 0,
        ""typeKey"": ""int""
      },
      ""key"": ""c:system_00000000-0000-4000-8000-0000000000c1<int>|0:0:int""
    },
    {
      ""name"": ""held Comparer.Create comparer is identity"",
      ""kind"": ""Object"",
      ""id"": ""comparer-1"",
      ""valueTypeKey"": ""c:system_00000000-0000-4000-8000-0000000000c2<int>"",
      ""value"": {
        ""comparer"": 3,
        ""family"": 1,
        ""typeKey"": ""int"",
        ""delegates"": []
      },
      ""key"": ""comparer-1""
    },
    {
      ""name"": ""interface slot box keys by what it holds"",
      ""kind"": ""Class"",
      ""valueTypeKey"": ""int"",
      ""value"": 3,
      ""key"": ""int|3""
    },
    {
      ""name"": ""float NaN decodes to one NaN"",
      ""kind"": ""Float"",
      ""value"": ""NaN"",
      ""key"": ""-4194304""
    },
    {
      ""name"": ""float stored -0 keys as 0"",
      ""kind"": ""Float"",
      ""value"": ""-0"",
      ""key"": ""0""
    },
    {
      ""name"": ""float stored Infinity"",
      ""kind"": ""Float"",
      ""value"": ""Infinity"",
      ""key"": ""2139095040""
    },
    {
      ""name"": ""float stored -Infinity"",
      ""kind"": ""Float"",
      ""value"": ""-Infinity"",
      ""key"": ""-8388608""
    },
    {
      ""name"": ""Vector2 with a NaN component only a comparer judges"",
      ""kind"": ""Vector2"",
      ""value"": {
        ""x"": ""NaN"",
        ""y"": 1
      },
      ""key"": ""-4194304,1065353216"",
      ""keyEquivalent"": false
    },
    {
      ""name"": ""Vector3 stored forms decode per component"",
      ""kind"": ""Vector3"",
      ""value"": {
        ""x"": ""-0"",
        ""y"": 0,
        ""z"": ""Infinity""
      },
      ""key"": ""0,0,2139095040""
    },
    {
      ""name"": ""boxed float NaN"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""float"",
      ""value"": ""NaN"",
      ""key"": ""float|-4194304""
    },
    {
      ""name"": ""boxed float stored -0"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""float"",
      ""value"": ""-0"",
      ""key"": ""float|0""
    },
    {
      ""name"": ""boxed float stored Infinity"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""float"",
      ""value"": ""Infinity"",
      ""key"": ""float|2139095040""
    },
    {
      ""name"": ""boxed Vector2 with a NaN component"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""Vector2"",
      ""value"": {
        ""x"": ""NaN"",
        ""y"": 0
      },
      ""key"": ""Vector2|-4194304,0"",
      ""keyEquivalent"": false
    },
    {
      ""name"": ""boxed tuple keys by its elements"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""(int,string)"",
      ""value"": [""e1"", ""e2""],
      ""rows"": {
        ""e1"": {
          ""value"": 1
        },
        ""e2"": {
          ""value"": ""a""
        }
      },
      ""key"": ""(int,string)|[[2,\""1\""],[3,\""a\""]]"",
      ""description"": ""(1, a)""
    },
    {
      ""name"": ""boxed tuple with repeated element types"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""(int,int)"",
      ""value"": [""e1"", ""e2""],
      ""rows"": {
        ""e1"": {
          ""value"": 1
        },
        ""e2"": {
          ""value"": 2
        }
      },
      ""key"": ""(int,int)|[[2,\""1\""],[2,\""2\""]]"",
      ""description"": ""(1, 2)""
    },
    {
      ""name"": ""boxed nested tuple with repeated element types"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""(int,(int,int))"",
      ""value"": [""e1"", ""e2""],
      ""rows"": {
        ""e1"": {
          ""value"": 1
        },
        ""e2"": {
          ""value"": [""e3"", ""e4""]
        },
        ""e3"": {
          ""value"": 2
        },
        ""e4"": {
          ""value"": 3
        }
      },
      ""key"": ""(int,(int,int))|[[2,\""1\""],[30,[[2,\""2\""],[2,\""3\""]]]]"",
      ""description"": ""(1, (2, 3))""
    },
    {
      ""name"": ""boxed Reference<int> stored as a one-id array keys by target"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""Reference<int>"",
      ""value"": [""v1""],
      ""key"": ""Reference<int>|v1"",
      ""description"": ""v1""
    },
    {
      ""name"": ""boxed Reference<int> stored as an id keys by target"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""Reference<int>"",
      ""value"": ""v1"",
      ""key"": ""Reference<int>|v1""
    },
    {
      ""name"": ""boxed NeoDialogueReference stored as a one-id array"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""NeoDialogueReference"",
      ""value"": [""d1""],
      ""key"": ""NeoDialogueReference|d1""
    },
    {
      ""name"": ""boxed NeoDialogueReference stored as an id"",
      ""kind"": ""Object"",
      ""valueTypeKey"": ""NeoDialogueReference"",
      ""value"": ""d1"",
      ""key"": ""NeoDialogueReference|d1""
    },
    {
      ""name"": ""null Object entry is the null key"",
      ""kind"": ""Object"",
      ""value"": null,
      ""key"": ""null""
    },
    {
      ""name"": ""null interface entry is the null key"",
      ""kind"": ""Class"",
      ""value"": null,
      ""key"": ""null""
    },
    {
      ""name"": ""null string entry of a boxed Set<string>"",
      ""kind"": ""String"",
      ""value"": null,
      ""key"": ""null""
    }
  ]
}";
    }
}
