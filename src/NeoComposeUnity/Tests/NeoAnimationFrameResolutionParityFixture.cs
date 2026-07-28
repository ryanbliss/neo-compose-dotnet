// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
//
// VENDORED — DO NOT HAND-EDIT.
// Source of record:
// neo-compose/src/models/animation/animation-frame-resolution-parity-fixture.json
// This exact fixture keeps P29 section 4.4 sparse resolution and traversal
// vectors aligned across the web authoring preview and Unity runtime, and —
// since P42 section 7.1 — the structured-leaf FIELD override vectors too.
//
// To re-vendor: copy the JSON verbatim and double every `"` for the C#
// verbatim string. `animation-frame-resolution-parity-fixture.sync.test.ts` in
// neo-compose fails when this copy drifts from the source of record; run it
// with DOTNET_SDK_REPO_PATH pointed at this repo.

#nullable enable

namespace NeoCompose.Tests
{
    public static class NeoAnimationFrameResolutionParityFixture
    {
        public const string Json = @"{
  ""$comment"": ""Cross-runtime animation frame resolution fixture (P29 section 4.4, extended by P42 section 7.1). The web authoring preview and Unity runtime must resolve the same sparse frame chain from the playback-start root fallback, regardless of traversal direction or wrap mode. Vendored into NeoAnimationFrameResolutionParityFixture.cs."",
  ""$partialComment"": ""P42 section 1.3: a structured-leaf FIELD override travels as the { \""$partial\"": { ... } } envelope; a whole-leaf override is the bare full record. Both are legal in the same position and a runtime must tell them apart, so a runtime that does not unwrap the envelope fails these vectors rather than passing them by accident."",
  ""$nestedComment"": ""P42 section 1.3, depth: `Collider.Offset` puts the envelope at depth 2, under a nested Class rather than directly under a top-level schema key. The unwrap guard belongs inside the recursion so it fires at any depth, and a runtime that only probes the top level of `overrides` passes every `Position`/`Sprite`/`Tint` vector here and still fails this one."",
  ""$sparseClassComment"": ""P29 section 4.4, deep merge: `Collider` is a nested CLASS, so a bare subset record there is a legal sparse descent — decision D1's ban on bare subsets applies to structured LEAVES, which is why `Offset` carries the envelope and `Collider` does not. Frame 1 writes only `Collider.Offset` and frame 3 only `Collider.Enabled`, so a runtime that replaces a class record instead of merging into it loses whichever key the frame omitted."",
  ""$boundComment"": ""P42 section 1.4: \""as it stands\"" means at apply time on the played instance. `root` is the authored default placement and `boundRoot` is a second placement of the same class bound to a different sprite sheet, with a Position.z, a Collider.Offset and a Tint written by something other than the clip. One `frames` chain resolves against both; `resolvedFrames` and `boundResolvedFrames` must differ exactly where the clip never wrote."",
  ""root"": {
    ""Collider"": {
      ""Enabled"": true,
      ""Offset"": {
        ""x"": 0,
        ""y"": 0
      }
    },
    ""Position"": {
      ""x"": 0,
      ""y"": 0,
      ""z"": 0
    },
    ""Sprite"": {
      ""fileId"": ""file-body-normal-walk-side-leg"",
      ""sliceIndex"": 0
    },
    ""Tint"": {
      ""r"": 1,
      ""g"": 1,
      ""b"": 1,
      ""a"": 1
    },
    ""Visible"": true
  },
  ""boundRoot"": {
    ""Collider"": {
      ""Enabled"": true,
      ""Offset"": {
        ""x"": 4,
        ""y"": 4
      }
    },
    ""Position"": {
      ""x"": -5,
      ""y"": -5,
      ""z"": 7
    },
    ""Sprite"": {
      ""fileId"": ""file-pants-long-walk-side-leg"",
      ""sliceIndex"": 0
    },
    ""Tint"": {
      ""r"": 0.1,
      ""g"": 0.2,
      ""b"": 0.3,
      ""a"": 1
    },
    ""Visible"": true
  },
  ""frames"": [
    {
      ""index"": 1,
      ""overrides"": {
        ""Collider"": {
          ""Offset"": {
            ""$partial"": {
              ""x"": 1
            }
          }
        },
        ""Position"": {
          ""$partial"": {
            ""x"": 1
          }
        },
        ""Sprite"": {
          ""$partial"": {
            ""sliceIndex"": 1
          }
        },
        ""Tint"": {
          ""$partial"": {
            ""a"": 0.5
          }
        }
      }
    },
    {
      ""index"": 2,
      ""overrides"": {
        ""Sprite"": {
          ""fileId"": ""file-body-armored-walk-side-leg"",
          ""sliceIndex"": 2
        }
      }
    },
    {
      ""index"": 3,
      ""overrides"": {
        ""Collider"": {
          ""Enabled"": false
        },
        ""Position"": {
          ""$partial"": {
            ""y"": 3
          }
        },
        ""Sprite"": {
          ""$partial"": {
            ""sliceIndex"": 3
          }
        },
        ""Tint"": {
          ""r"": 0.25,
          ""g"": 0.5,
          ""b"": 0.75,
          ""a"": 1
        },
        ""Visible"": false
      }
    }
  ],
  ""resolvedFrames"": [
    {
      ""Collider"": {
        ""Enabled"": true,
        ""Offset"": {
          ""x"": 0,
          ""y"": 0
        }
      },
      ""Position"": {
        ""x"": 0,
        ""y"": 0,
        ""z"": 0
      },
      ""Sprite"": {
        ""fileId"": ""file-body-normal-walk-side-leg"",
        ""sliceIndex"": 0
      },
      ""Tint"": {
        ""r"": 1,
        ""g"": 1,
        ""b"": 1,
        ""a"": 1
      },
      ""Visible"": true
    },
    {
      ""Collider"": {
        ""Enabled"": true,
        ""Offset"": {
          ""x"": 1,
          ""y"": 0
        }
      },
      ""Position"": {
        ""x"": 1,
        ""y"": 0,
        ""z"": 0
      },
      ""Sprite"": {
        ""fileId"": ""file-body-normal-walk-side-leg"",
        ""sliceIndex"": 1
      },
      ""Tint"": {
        ""r"": 1,
        ""g"": 1,
        ""b"": 1,
        ""a"": 0.5
      },
      ""Visible"": true
    },
    {
      ""Collider"": {
        ""Enabled"": true,
        ""Offset"": {
          ""x"": 1,
          ""y"": 0
        }
      },
      ""Position"": {
        ""x"": 1,
        ""y"": 0,
        ""z"": 0
      },
      ""Sprite"": {
        ""fileId"": ""file-body-armored-walk-side-leg"",
        ""sliceIndex"": 2
      },
      ""Tint"": {
        ""r"": 1,
        ""g"": 1,
        ""b"": 1,
        ""a"": 0.5
      },
      ""Visible"": true
    },
    {
      ""Collider"": {
        ""Enabled"": false,
        ""Offset"": {
          ""x"": 1,
          ""y"": 0
        }
      },
      ""Position"": {
        ""x"": 1,
        ""y"": 3,
        ""z"": 0
      },
      ""Sprite"": {
        ""fileId"": ""file-body-armored-walk-side-leg"",
        ""sliceIndex"": 3
      },
      ""Tint"": {
        ""r"": 0.25,
        ""g"": 0.5,
        ""b"": 0.75,
        ""a"": 1
      },
      ""Visible"": false
    }
  ],
  ""boundResolvedFrames"": [
    {
      ""Collider"": {
        ""Enabled"": true,
        ""Offset"": {
          ""x"": 4,
          ""y"": 4
        }
      },
      ""Position"": {
        ""x"": -5,
        ""y"": -5,
        ""z"": 7
      },
      ""Sprite"": {
        ""fileId"": ""file-pants-long-walk-side-leg"",
        ""sliceIndex"": 0
      },
      ""Tint"": {
        ""r"": 0.1,
        ""g"": 0.2,
        ""b"": 0.3,
        ""a"": 1
      },
      ""Visible"": true
    },
    {
      ""Collider"": {
        ""Enabled"": true,
        ""Offset"": {
          ""x"": 1,
          ""y"": 4
        }
      },
      ""Position"": {
        ""x"": 1,
        ""y"": -5,
        ""z"": 7
      },
      ""Sprite"": {
        ""fileId"": ""file-pants-long-walk-side-leg"",
        ""sliceIndex"": 1
      },
      ""Tint"": {
        ""r"": 0.1,
        ""g"": 0.2,
        ""b"": 0.3,
        ""a"": 0.5
      },
      ""Visible"": true
    },
    {
      ""Collider"": {
        ""Enabled"": true,
        ""Offset"": {
          ""x"": 1,
          ""y"": 4
        }
      },
      ""Position"": {
        ""x"": 1,
        ""y"": -5,
        ""z"": 7
      },
      ""Sprite"": {
        ""fileId"": ""file-body-armored-walk-side-leg"",
        ""sliceIndex"": 2
      },
      ""Tint"": {
        ""r"": 0.1,
        ""g"": 0.2,
        ""b"": 0.3,
        ""a"": 0.5
      },
      ""Visible"": true
    },
    {
      ""Collider"": {
        ""Enabled"": false,
        ""Offset"": {
          ""x"": 1,
          ""y"": 4
        }
      },
      ""Position"": {
        ""x"": 1,
        ""y"": 3,
        ""z"": 7
      },
      ""Sprite"": {
        ""fileId"": ""file-body-armored-walk-side-leg"",
        ""sliceIndex"": 3
      },
      ""Tint"": {
        ""r"": 0.25,
        ""g"": 0.5,
        ""b"": 0.75,
        ""a"": 1
      },
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
