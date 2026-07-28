// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
//
// VENDORED — DO NOT HAND-EDIT.
// Source of record:
// neo-compose/src/models/animation/animation-frame-resolution-parity-fixture.json
// This exact fixture keeps P29 section 4.4 sparse resolution and traversal
// vectors aligned across the web authoring preview and Unity runtime, and —
// since P42 section 7.1 — the structured-leaf FIELD override vectors for all
// six leaf kinds, plus the int-component accept/reject verdicts.
//
// To re-vendor: copy the JSON verbatim and double every `"` for the C#
// verbatim string. `parity-fixture-vendoring.sync.test.ts` in neo-compose
// fails when this copy drifts from the source of record; run it with
// DOTNET_SDK_REPO_PATH pointed at this repo.

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
  ""$sixKindsComment"": ""P42 section 7.1 requires every case to run across all six structured-leaf kinds. `Cell` is the Vector2Int and `Grid` the Vector3Int; without them the int kinds were the only ones whose resolution nothing pinned, even though they are the two kinds a runtime is most likely to get wrong — an int vector that resolves through a float path looks identical until a component lands on a fraction. `Grid` also carries the whole-value override at frame 2, so the envelope-versus-whole-record discrimination is exercised on an int kind and not only on Sprite."",
  ""$intFieldComment"": ""P42 section 1.1: `int` is the one field type whose accepted range differs from the float kinds it is otherwise shaped like, and the wire cannot tell the two apart — `{\""$partial\"":{\""y\"":-2.25}}` is a legal Vector3 write and an illegal Vector3Int one. `intFieldWrites` is the vector for that, deliberately paired: the same field value appears accepted on the float kind and rejected on the int kind, so a runtime that validates int components as floats fails here rather than silently truncating a frame later. Each runtime runs these through its own field validator; only the verdict is shared, because the diagnostic wording is not."",
  ""root"": {
    ""Cell"": {
      ""x"": 0,
      ""y"": 0
    },
    ""Collider"": {
      ""Enabled"": true,
      ""Offset"": {
        ""x"": 0,
        ""y"": 0
      }
    },
    ""Grid"": {
      ""x"": 0,
      ""y"": 0,
      ""z"": 0
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
    ""Cell"": {
      ""x"": 8,
      ""y"": 9
    },
    ""Collider"": {
      ""Enabled"": true,
      ""Offset"": {
        ""x"": 4,
        ""y"": 4
      }
    },
    ""Grid"": {
      ""x"": -2,
      ""y"": -3,
      ""z"": 5
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
        ""Cell"": {
          ""$partial"": {
            ""x"": 1
          }
        },
        ""Collider"": {
          ""Offset"": {
            ""$partial"": {
              ""x"": 1
            }
          }
        },
        ""Grid"": {
          ""$partial"": {
            ""z"": 2
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
        ""Grid"": {
          ""x"": 7,
          ""y"": 8,
          ""z"": 9
        },
        ""Sprite"": {
          ""fileId"": ""file-body-armored-walk-side-leg"",
          ""sliceIndex"": 2
        }
      }
    },
    {
      ""index"": 3,
      ""overrides"": {
        ""Cell"": {
          ""$partial"": {
            ""y"": 3
          }
        },
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
      ""Cell"": {
        ""x"": 0,
        ""y"": 0
      },
      ""Collider"": {
        ""Enabled"": true,
        ""Offset"": {
          ""x"": 0,
          ""y"": 0
        }
      },
      ""Grid"": {
        ""x"": 0,
        ""y"": 0,
        ""z"": 0
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
      ""Cell"": {
        ""x"": 1,
        ""y"": 0
      },
      ""Collider"": {
        ""Enabled"": true,
        ""Offset"": {
          ""x"": 1,
          ""y"": 0
        }
      },
      ""Grid"": {
        ""x"": 0,
        ""y"": 0,
        ""z"": 2
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
      ""Cell"": {
        ""x"": 1,
        ""y"": 0
      },
      ""Collider"": {
        ""Enabled"": true,
        ""Offset"": {
          ""x"": 1,
          ""y"": 0
        }
      },
      ""Grid"": {
        ""x"": 7,
        ""y"": 8,
        ""z"": 9
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
      ""Cell"": {
        ""x"": 1,
        ""y"": 3
      },
      ""Collider"": {
        ""Enabled"": false,
        ""Offset"": {
          ""x"": 1,
          ""y"": 0
        }
      },
      ""Grid"": {
        ""x"": 7,
        ""y"": 8,
        ""z"": 9
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
      ""Cell"": {
        ""x"": 8,
        ""y"": 9
      },
      ""Collider"": {
        ""Enabled"": true,
        ""Offset"": {
          ""x"": 4,
          ""y"": 4
        }
      },
      ""Grid"": {
        ""x"": -2,
        ""y"": -3,
        ""z"": 5
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
      ""Cell"": {
        ""x"": 1,
        ""y"": 9
      },
      ""Collider"": {
        ""Enabled"": true,
        ""Offset"": {
          ""x"": 1,
          ""y"": 4
        }
      },
      ""Grid"": {
        ""x"": -2,
        ""y"": -3,
        ""z"": 2
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
      ""Cell"": {
        ""x"": 1,
        ""y"": 9
      },
      ""Collider"": {
        ""Enabled"": true,
        ""Offset"": {
          ""x"": 1,
          ""y"": 4
        }
      },
      ""Grid"": {
        ""x"": 7,
        ""y"": 8,
        ""z"": 9
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
      ""Cell"": {
        ""x"": 1,
        ""y"": 3
      },
      ""Collider"": {
        ""Enabled"": false,
        ""Offset"": {
          ""x"": 1,
          ""y"": 4
        }
      },
      ""Grid"": {
        ""x"": 7,
        ""y"": 8,
        ""z"": 9
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
  ""intFieldWrites"": [
    {
      ""label"": ""a whole Vector2Int component"",
      ""kind"": ""Vector2Int"",
      ""field"": ""x"",
      ""value"": 1,
      ""accepted"": true
    },
    {
      ""label"": ""a fractional Vector2Int component"",
      ""kind"": ""Vector2Int"",
      ""field"": ""x"",
      ""value"": 1.5,
      ""accepted"": false
    },
    {
      ""label"": ""a negative whole Vector3Int component"",
      ""kind"": ""Vector3Int"",
      ""field"": ""z"",
      ""value"": -2,
      ""accepted"": true
    },
    {
      ""label"": ""a fractional Vector3Int component"",
      ""kind"": ""Vector3Int"",
      ""field"": ""z"",
      ""value"": -2.25,
      ""accepted"": false
    },
    {
      ""label"": ""the same fractional value on the float vector that shares the field name"",
      ""kind"": ""Vector3"",
      ""field"": ""z"",
      ""value"": -2.25,
      ""accepted"": true
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
