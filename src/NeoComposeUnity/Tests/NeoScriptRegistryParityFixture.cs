// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
//
// VENDORED — DO NOT HAND-EDIT.
// Source of record:
// neo-compose/src/models/neoscript/neoscript-registry-parity-fixture.json
// P42 section 2.3. `Images.<Name>` is the one NeoScript construct whose two
// runtimes resolve from different sources: the TypeScript side resolves the
// registry symbol against the project document while the compiler runs, and
// this side resolves the resulting file id through NeoAssetDatabase while the
// game runs. Their agreement is pinned by this fixture rather than inferred.
//
// To re-vendor: copy the JSON verbatim and double every `"` for the C#
// verbatim string. Consumed here by NeoScriptRegistryParityTests and on the
// web side by src/models/neoscript/neoscript-registry-parity.test.ts.

#nullable enable

namespace NeoCompose.Tests
{
    public static class NeoScriptRegistryParityFixture
    {
        public const string Json = @"{
  ""$comment"": ""P42 §2.3 cross-runtime NeoScript file-registry parity fixture. This is the one place the two evaluators resolve from DIFFERENT sources — the TypeScript side resolves a registry symbol against the project document at compile time, the .NET side resolves the resulting file id through NeoAssetDatabase at run time — so their agreement is pinned here rather than inferred. Consumed by src/models/neoscript/neoscript-registry-parity.test.ts (web) and NeoScriptRegistryParityTests (neo-compose-dotnet, vendored copy). Hand-edit this file; it is not generated, precisely because a fixture generated from one runtime cannot catch a divergence in the other's source of truth."",
  ""registries"": {
    ""Images"": [
      {
        ""symbol"": ""BodyNormalWalkSideLeg"",
        ""fileId"": ""6d1f5f14-0b1a-4a2e-8f6d-2c7b1a4e9d01"",
        ""path"": ""Files/Images/BodyNormalWalkSideLeg.png""
      },
      {
        ""symbol"": ""PantsLongWalkSideLeg"",
        ""fileId"": ""6d1f5f14-0b1a-4a2e-8f6d-2c7b1a4e9d02"",
        ""path"": ""Files/Images/PantsLongWalkSideLeg.png""
      }
    ],
    ""AudioClips"": [
      {
        ""symbol"": ""Footstep"",
        ""fileId"": ""6d1f5f14-0b1a-4a2e-8f6d-2c7b1a4e9d03"",
        ""path"": ""Files/AudioClips/Footstep.wav""
      }
    ]
  },
  ""compileCases"": [
    {
      ""name"": ""an image symbol lowers to its project file record id"",
      ""statement"": ""Sprite.fileId = Images.BodyNormalWalkSideLeg;"",
      ""expectedWriteValue"": ""6d1f5f14-0b1a-4a2e-8f6d-2c7b1a4e9d01""
    },
    {
      ""name"": ""a second image symbol lowers to its own record id"",
      ""statement"": ""Sprite.fileId = Images.PantsLongWalkSideLeg;"",
      ""expectedWriteValue"": ""6d1f5f14-0b1a-4a2e-8f6d-2c7b1a4e9d02""
    },
    {
      ""name"": ""an audio symbol resolves and compiles"",
      ""statement"": ""var clip = AudioClips.Footstep;"",
      ""expectedWriteValue"": null
    },
    {
      ""name"": ""an unknown image symbol is a compile error"",
      ""statement"": ""Sprite.fileId = Images.NotAFile;"",
      ""expectedError"": ""The ImageRegistry has no NeoImage named 'NotAFile'.""
    },
    {
      ""name"": ""an unknown audio symbol is a compile error"",
      ""statement"": ""var clip = AudioClips.NotAClip;"",
      ""expectedError"": ""The AudioClipRegistry has no NeoAudioClip named 'NotAClip'.""
    },
    {
      ""name"": ""an audio clip has no slices"",
      ""statement"": ""var clip = AudioClips.Footstep.Slice(0);"",
      ""expectedError"": ""Slice(index) is only defined on an image reference; an audio clip has no slices.""
    },
    {
      ""name"": ""a bare image reference is not a whole sprite value"",
      ""statement"": ""Sprite = Images.BodyNormalWalkSideLeg;"",
      ""expectedError"": ""Cannot use ImageRef as assign of type SpriteInfo""
    },
    {
      ""name"": ""an arbitrary string is not a file reference"",
      ""statement"": ""Sprite.fileId = \""6d1f5f14-0b1a-4a2e-8f6d-2c7b1a4e9d01\"";"",
      ""expectedError"": ""Cannot use string as assign of type ImageRef""
    }
  ],
  ""$evaluateComment"": ""Each case is an `imageSlice` function pointer as both runtimes receive it: the file half is already the project file record id the compiler resolved, so neither runtime looks a symbol up here. `expected` is the sprite value record that must come out byte-for-byte identical on both. .NET additionally resolves `fileId` through NeoAssetDatabase to obtain the UnityEngine.Sprite; that resolution must not change the record."",
  ""evaluateCases"": [
    {
      ""name"": ""slice zero of a bound sheet"",
      ""fileId"": ""6d1f5f14-0b1a-4a2e-8f6d-2c7b1a4e9d01"",
      ""sliceIndex"": 0,
      ""expected"": {
        ""fileId"": ""6d1f5f14-0b1a-4a2e-8f6d-2c7b1a4e9d01"",
        ""sliceIndex"": 0
      }
    },
    {
      ""name"": ""a later slice of the same sheet"",
      ""fileId"": ""6d1f5f14-0b1a-4a2e-8f6d-2c7b1a4e9d01"",
      ""sliceIndex"": 2,
      ""expected"": {
        ""fileId"": ""6d1f5f14-0b1a-4a2e-8f6d-2c7b1a4e9d01"",
        ""sliceIndex"": 2
      }
    },
    {
      ""name"": ""a different sheet at the same slice"",
      ""fileId"": ""6d1f5f14-0b1a-4a2e-8f6d-2c7b1a4e9d02"",
      ""sliceIndex"": 2,
      ""expected"": {
        ""fileId"": ""6d1f5f14-0b1a-4a2e-8f6d-2c7b1a4e9d02"",
        ""sliceIndex"": 2
      }
    },
    {
      ""name"": ""a negative slice index is a runtime error, not a clamp"",
      ""fileId"": ""6d1f5f14-0b1a-4a2e-8f6d-2c7b1a4e9d01"",
      ""sliceIndex"": -1,
      ""expectedError"": ""Slice index must be 0 or greater.""
    },
    {
      ""name"": ""a fractional slice index is a runtime error"",
      ""fileId"": ""6d1f5f14-0b1a-4a2e-8f6d-2c7b1a4e9d01"",
      ""sliceIndex"": 1.5,
      ""expectedError"": ""Slice index must be a whole number.""
    },
    {
      ""name"": ""an empty file reference is a runtime error"",
      ""fileId"": """",
      ""sliceIndex"": 0,
      ""expectedError"": ""Slice(index) requires a project image reference.""
    }
  ]
}";
    }
}
