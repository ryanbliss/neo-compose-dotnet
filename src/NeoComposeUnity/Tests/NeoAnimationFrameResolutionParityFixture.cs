// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
//
// VENDORED — DO NOT HAND-EDIT.
// Source of record:
// neo-compose/src/models/animation/animation-frame-resolution-parity-fixture.json
// This exact fixture keeps P29 section 4.4 sparse resolution and traversal
// vectors aligned across the web authoring preview and Unity runtime.

#nullable enable

namespace NeoCompose.Tests
{
    public static class NeoAnimationFrameResolutionParityFixture
    {
        public const string Json = @"{
  ""$comment"": ""Cross-runtime animation frame resolution fixture (P29 section 4.4). The web authoring preview and Unity runtime must resolve the same sparse frame chain from the playback-start root fallback, regardless of traversal direction or wrap mode. Vendored into NeoAnimationFrameResolutionParityFixture.cs."",
  ""root"": {
    ""Position"": {
      ""x"": 0,
      ""y"": 0
    },
    ""Sprite"": ""root"",
    ""Visible"": true
  },
  ""frames"": [
    {
      ""index"": 1,
      ""overrides"": {
        ""Position"": {
          ""x"": 1
        },
        ""Sprite"": ""one""
      }
    },
    {
      ""index"": 3,
      ""overrides"": {
        ""Position"": {
          ""y"": 3
        },
        ""Visible"": false
      }
    }
  ],
  ""resolvedFrames"": [
    {
      ""Position"": {
        ""x"": 0,
        ""y"": 0
      },
      ""Sprite"": ""root"",
      ""Visible"": true
    },
    {
      ""Position"": {
        ""x"": 1,
        ""y"": 0
      },
      ""Sprite"": ""one"",
      ""Visible"": true
    },
    {
      ""Position"": {
        ""x"": 1,
        ""y"": 0
      },
      ""Sprite"": ""one"",
      ""Visible"": true
    },
    {
      ""Position"": {
        ""x"": 1,
        ""y"": 3
      },
      ""Sprite"": ""one"",
      ""Visible"": false
    }
  ],
  ""traversals"": {
    ""onceForward"": [0, 1, 2, 3],
    ""onceBackward"": [3, 2, 1, 0],
    ""repeatForwardWrap"": [0, 1, 2, 3, 0, 1],
    ""repeatBackwardWrap"": [3, 2, 1, 0, 3, 2],
    ""boomerangForward"": [0, 1, 2, 3, 2, 1, 0, 1],
    ""boomerangBackward"": [3, 2, 1, 0, 1, 2, 3, 2]
  }
}";
    }
}
