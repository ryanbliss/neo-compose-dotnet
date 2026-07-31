// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
//
// VENDORED — DO NOT HAND-EDIT.
// Source of record:
// neo-compose/src/models/animation/animation-playback-parity-fixture.json
// P48 section 2.3's playback pipeline, as one table both runtimes answer
// (acceptance 7): sparse holds, crop windows at both edges, both directions,
// truncation by the owning clip, StartFrame offsets, the two-row yoyo and its
// mirrored trailing form, fps scaling and nested reversal on a child clip
// track, runtime clamping of a window past the resolved content, and coverage
// that ends early — where the member keeps its last value because the track
// wrote NOTHING rather than replaying its last frame.
//
// To re-vendor: copy the JSON verbatim and double every `"` for the C#
// verbatim string. `parity-fixture-vendoring.sync.test.ts` in neo-compose
// fails when this copy drifts from the source of record; run it with
// DOTNET_SDK_REPO_PATH pointed at this repo.

#nullable enable

namespace NeoCompose.Tests
{
    public static class NeoAnimationPlaybackParityFixture
    {
        public const string Json = @"{
  ""$comment"": ""Cross-runtime fixture for the P48 section 2.3 playback pipeline. One table serves both track kinds and both runtimes: the web resolves it through src/models/animation/animation-playback.ts, and the Unity player must produce byte-identical writes (P48 acceptance 7). Nothing here is sprite-shaped — values are opaque labels — because the pipeline is defined against NeoAnimationSegment<T> and a clip's resolved timeline, not against SpriteInfo."",
  ""$inputComment"": ""A case is a clip (Duration + FPS), an initial value per target member, and an ordered Tracks list. A track's `content` is either a segment (sparse rows under the hold rule) or a child clip (its own sparse frames, its own FPS, and its own nested tracks). Segment tracks advance one content frame per clip frame; child clip tracks advance childFps/parentFps, and their crop window is expressed in the CHILD's frames and applied before that scaling."",
  ""$expectationComment"": ""`frames` is the whole assertion. `writes` is the ordered list of writes the pipeline actually performs at that clip frame — an empty list is the load-bearing case, because P48 section 2.3 says a track outside its window writes NOTHING rather than holding its last frame, and the two are only distinguishable when a second track writes the same member. `values` is the resulting member state after last-write-wins, carried forward from the previous frame when nothing wrote, which is how 'the member keeps its last value' is pinned without inventing a hold mode."",
  ""$coverageComment"": ""Every case declares what it `covers`; the coverage test asserts the union equals the list P48 section 10 requires, so a case deleted or narrowed fails loudly instead of quietly reducing the table."",
  ""$truncationComment"": ""Truncation has two branches and they are not the same fact. `truncation` is WINDOW exhaustion: the crop window runs out of content while the clip is still playing, and the frames that follow are observably empty. `clipEndTruncation` is the CLIP-END branch: the clip's Duration arrives while the track still has content left. That branch cannot be observed by a row in `frames` — a clip has no frame past its Duration, so there is nothing to assert emptiness on. It is asserted instead as a property over this table: some case must still be mid-content at `clipDuration - 1`, which the coverage test checks by asking whether one more clip frame would have written."",
  ""requiredCoverage"": [
    ""childClipTrack"",
    ""clipEndTruncation"",
    ""coverageEndsEarly"",
    ""cropEnd"",
    ""cropStart"",
    ""directionForward"",
    ""directionReverse"",
    ""emptyContent"",
    ""emptyResolvedWindow"",
    ""fpsScaling"",
    ""lastWriteWins"",
    ""nestedReversal"",
    ""noWriteBeforeFirstRow"",
    ""runtimeClamp"",
    ""sparseHold"",
    ""startFrameOffset"",
    ""truncation"",
    ""yoyoLeading"",
    ""yoyoTrailing""
  ],
  ""cases"": [
    {
      ""label"": ""a sparse segment holds each authored row until the next one"",
      ""covers"": [""sparseHold"", ""directionForward""],
      ""clipDuration"": 6,
      ""clipFps"": 8,
      ""initialValues"": { ""Sprite"": null },
      ""tracks"": [
        {
          ""id"": ""walk"",
          ""target"": ""Sprite"",
          ""startFrame"": 0,
          ""direction"": ""forward"",
          ""offsetStartIndex"": 0,
          ""offsetEndIndex"": null,
          ""content"": {
            ""kind"": ""segment"",
            ""duration"": 6,
            ""frames"": [
              { ""index"": 0, ""value"": ""s0"" },
              { ""index"": 3, ""value"": ""s1"" }
            ]
          }
        }
      ],
      ""frames"": [
        {
          ""clipFrame"": 0,
          ""writes"": [{ ""track"": ""walk"", ""value"": ""s0"" }],
          ""values"": { ""Sprite"": ""s0"" }
        },
        {
          ""clipFrame"": 1,
          ""writes"": [{ ""track"": ""walk"", ""value"": ""s0"" }],
          ""values"": { ""Sprite"": ""s0"" }
        },
        {
          ""clipFrame"": 2,
          ""writes"": [{ ""track"": ""walk"", ""value"": ""s0"" }],
          ""values"": { ""Sprite"": ""s0"" }
        },
        {
          ""clipFrame"": 3,
          ""writes"": [{ ""track"": ""walk"", ""value"": ""s1"" }],
          ""values"": { ""Sprite"": ""s1"" }
        },
        {
          ""clipFrame"": 4,
          ""writes"": [{ ""track"": ""walk"", ""value"": ""s1"" }],
          ""values"": { ""Sprite"": ""s1"" }
        },
        {
          ""clipFrame"": 5,
          ""writes"": [{ ""track"": ""walk"", ""value"": ""s1"" }],
          ""values"": { ""Sprite"": ""s1"" }
        }
      ]
    },
    {
      ""label"": ""a segment whose first row is not index zero writes nothing until it is authored"",
      ""covers"": [""sparseHold"", ""noWriteBeforeFirstRow""],
      ""clipDuration"": 4,
      ""clipFps"": 8,
      ""initialValues"": { ""Sprite"": ""idle"" },
      ""tracks"": [
        {
          ""id"": ""late"",
          ""target"": ""Sprite"",
          ""startFrame"": 0,
          ""direction"": ""forward"",
          ""offsetStartIndex"": 0,
          ""offsetEndIndex"": null,
          ""content"": {
            ""kind"": ""segment"",
            ""duration"": 4,
            ""frames"": [{ ""index"": 2, ""value"": ""sA"" }]
          }
        }
      ],
      ""frames"": [
        { ""clipFrame"": 0, ""writes"": [], ""values"": { ""Sprite"": ""idle"" } },
        { ""clipFrame"": 1, ""writes"": [], ""values"": { ""Sprite"": ""idle"" } },
        {
          ""clipFrame"": 2,
          ""writes"": [{ ""track"": ""late"", ""value"": ""sA"" }],
          ""values"": { ""Sprite"": ""sA"" }
        },
        {
          ""clipFrame"": 3,
          ""writes"": [{ ""track"": ""late"", ""value"": ""sA"" }],
          ""values"": { ""Sprite"": ""sA"" }
        }
      ]
    },
    {
      ""label"": ""a crop window trims both edges and the row stops at the window's end"",
      ""covers"": [
        ""cropStart"",
        ""cropEnd"",
        ""coverageEndsEarly"",
        ""directionForward""
      ],
      ""clipDuration"": 5,
      ""clipFps"": 8,
      ""initialValues"": { ""Sprite"": null },
      ""tracks"": [
        {
          ""id"": ""cropped"",
          ""target"": ""Sprite"",
          ""startFrame"": 0,
          ""direction"": ""forward"",
          ""offsetStartIndex"": 1,
          ""offsetEndIndex"": 4,
          ""content"": {
            ""kind"": ""segment"",
            ""duration"": 5,
            ""frames"": [
              { ""index"": 0, ""value"": ""s0"" },
              { ""index"": 1, ""value"": ""s1"" },
              { ""index"": 2, ""value"": ""s2"" },
              { ""index"": 3, ""value"": ""s3"" },
              { ""index"": 4, ""value"": ""s4"" }
            ]
          }
        }
      ],
      ""frames"": [
        {
          ""clipFrame"": 0,
          ""writes"": [{ ""track"": ""cropped"", ""value"": ""s1"" }],
          ""values"": { ""Sprite"": ""s1"" }
        },
        {
          ""clipFrame"": 1,
          ""writes"": [{ ""track"": ""cropped"", ""value"": ""s2"" }],
          ""values"": { ""Sprite"": ""s2"" }
        },
        {
          ""clipFrame"": 2,
          ""writes"": [{ ""track"": ""cropped"", ""value"": ""s3"" }],
          ""values"": { ""Sprite"": ""s3"" }
        },
        { ""clipFrame"": 3, ""writes"": [], ""values"": { ""Sprite"": ""s3"" } },
        { ""clipFrame"": 4, ""writes"": [], ""values"": { ""Sprite"": ""s3"" } }
      ]
    },
    {
      ""label"": ""Reverse plays the whole window backwards"",
      ""covers"": [""directionReverse""],
      ""clipDuration"": 3,
      ""clipFps"": 8,
      ""initialValues"": { ""Sprite"": null },
      ""tracks"": [
        {
          ""id"": ""back"",
          ""target"": ""Sprite"",
          ""startFrame"": 0,
          ""direction"": ""reverse"",
          ""offsetStartIndex"": 0,
          ""offsetEndIndex"": null,
          ""content"": {
            ""kind"": ""segment"",
            ""duration"": 3,
            ""frames"": [
              { ""index"": 0, ""value"": ""s0"" },
              { ""index"": 1, ""value"": ""s1"" },
              { ""index"": 2, ""value"": ""s2"" }
            ]
          }
        }
      ],
      ""frames"": [
        {
          ""clipFrame"": 0,
          ""writes"": [{ ""track"": ""back"", ""value"": ""s2"" }],
          ""values"": { ""Sprite"": ""s2"" }
        },
        {
          ""clipFrame"": 1,
          ""writes"": [{ ""track"": ""back"", ""value"": ""s1"" }],
          ""values"": { ""Sprite"": ""s1"" }
        },
        {
          ""clipFrame"": 2,
          ""writes"": [{ ""track"": ""back"", ""value"": ""s0"" }],
          ""values"": { ""Sprite"": ""s0"" }
        }
      ]
    },
    {
      ""label"": ""StartFrame delays the row, and the member holds before it and after it"",
      ""covers"": [""startFrameOffset"", ""coverageEndsEarly""],
      ""clipDuration"": 4,
      ""clipFps"": 8,
      ""initialValues"": { ""Sprite"": ""idle"" },
      ""tracks"": [
        {
          ""id"": ""delayed"",
          ""target"": ""Sprite"",
          ""startFrame"": 1,
          ""direction"": ""forward"",
          ""offsetStartIndex"": 0,
          ""offsetEndIndex"": null,
          ""content"": {
            ""kind"": ""segment"",
            ""duration"": 2,
            ""frames"": [
              { ""index"": 0, ""value"": ""a"" },
              { ""index"": 1, ""value"": ""b"" }
            ]
          }
        }
      ],
      ""frames"": [
        { ""clipFrame"": 0, ""writes"": [], ""values"": { ""Sprite"": ""idle"" } },
        {
          ""clipFrame"": 1,
          ""writes"": [{ ""track"": ""delayed"", ""value"": ""a"" }],
          ""values"": { ""Sprite"": ""a"" }
        },
        {
          ""clipFrame"": 2,
          ""writes"": [{ ""track"": ""delayed"", ""value"": ""b"" }],
          ""values"": { ""Sprite"": ""b"" }
        },
        { ""clipFrame"": 3, ""writes"": [], ""values"": { ""Sprite"": ""b"" } }
      ]
    },
    {
      ""label"": ""content past the owning clip's Duration truncates silently"",
      ""covers"": [""truncation"", ""startFrameOffset""],
      ""clipDuration"": 3,
      ""clipFps"": 8,
      ""initialValues"": { ""Sprite"": null },
      ""tracks"": [
        {
          ""id"": ""overrun"",
          ""target"": ""Sprite"",
          ""startFrame"": 1,
          ""direction"": ""forward"",
          ""offsetStartIndex"": 0,
          ""offsetEndIndex"": null,
          ""content"": {
            ""kind"": ""segment"",
            ""duration"": 5,
            ""frames"": [
              { ""index"": 0, ""value"": ""s0"" },
              { ""index"": 1, ""value"": ""s1"" },
              { ""index"": 2, ""value"": ""s2"" },
              { ""index"": 3, ""value"": ""s3"" },
              { ""index"": 4, ""value"": ""s4"" }
            ]
          }
        }
      ],
      ""frames"": [
        { ""clipFrame"": 0, ""writes"": [], ""values"": { ""Sprite"": null } },
        {
          ""clipFrame"": 1,
          ""writes"": [{ ""track"": ""overrun"", ""value"": ""s0"" }],
          ""values"": { ""Sprite"": ""s0"" }
        },
        {
          ""clipFrame"": 2,
          ""writes"": [{ ""track"": ""overrun"", ""value"": ""s1"" }],
          ""values"": { ""Sprite"": ""s1"" }
        }
      ]
    },
    {
      ""label"": ""a clip that ends mid-content stops at Duration with content left"",
      ""covers"": [""clipEndTruncation"", ""directionForward""],
      ""clipDuration"": 3,
      ""clipFps"": 8,
      ""initialValues"": { ""Sprite"": null },
      ""tracks"": [
        {
          ""id"": ""long"",
          ""target"": ""Sprite"",
          ""startFrame"": 0,
          ""direction"": ""forward"",
          ""offsetStartIndex"": 0,
          ""offsetEndIndex"": null,
          ""content"": {
            ""kind"": ""segment"",
            ""duration"": 6,
            ""frames"": [
              { ""index"": 0, ""value"": ""s0"" },
              { ""index"": 1, ""value"": ""s1"" },
              { ""index"": 2, ""value"": ""s2"" },
              { ""index"": 3, ""value"": ""s3"" },
              { ""index"": 4, ""value"": ""s4"" },
              { ""index"": 5, ""value"": ""s5"" }
            ]
          }
        }
      ],
      ""frames"": [
        {
          ""clipFrame"": 0,
          ""writes"": [{ ""track"": ""long"", ""value"": ""s0"" }],
          ""values"": { ""Sprite"": ""s0"" }
        },
        {
          ""clipFrame"": 1,
          ""writes"": [{ ""track"": ""long"", ""value"": ""s1"" }],
          ""values"": { ""Sprite"": ""s1"" }
        },
        {
          ""clipFrame"": 2,
          ""writes"": [{ ""track"": ""long"", ""value"": ""s2"" }],
          ""values"": { ""Sprite"": ""s2"" }
        }
      ]
    },
    {
      ""label"": ""the section 2.3 leading-leg yoyo plays s0 s1 s2 s1"",
      ""covers"": [
        ""yoyoLeading"",
        ""directionReverse"",
        ""cropEnd"",
        ""truncation"",
        ""startFrameOffset""
      ],
      ""clipDuration"": 4,
      ""clipFps"": 8,
      ""initialValues"": { ""Sprite"": null },
      ""expectedSequence"": [""s0"", ""s1"", ""s2"", ""s1""],
      ""tracks"": [
        {
          ""id"": ""leadOut"",
          ""target"": ""Sprite"",
          ""startFrame"": 0,
          ""direction"": ""forward"",
          ""offsetStartIndex"": 0,
          ""offsetEndIndex"": null,
          ""content"": {
            ""kind"": ""segment"",
            ""duration"": 3,
            ""frames"": [
              { ""index"": 0, ""value"": ""s0"" },
              { ""index"": 1, ""value"": ""s1"" },
              { ""index"": 2, ""value"": ""s2"" }
            ]
          }
        },
        {
          ""id"": ""leadBack"",
          ""target"": ""Sprite"",
          ""startFrame"": 3,
          ""direction"": ""reverse"",
          ""offsetStartIndex"": 0,
          ""offsetEndIndex"": 2,
          ""content"": {
            ""kind"": ""segment"",
            ""duration"": 3,
            ""frames"": [
              { ""index"": 0, ""value"": ""s0"" },
              { ""index"": 1, ""value"": ""s1"" },
              { ""index"": 2, ""value"": ""s2"" }
            ]
          }
        }
      ],
      ""frames"": [
        {
          ""clipFrame"": 0,
          ""writes"": [{ ""track"": ""leadOut"", ""value"": ""s0"" }],
          ""values"": { ""Sprite"": ""s0"" }
        },
        {
          ""clipFrame"": 1,
          ""writes"": [{ ""track"": ""leadOut"", ""value"": ""s1"" }],
          ""values"": { ""Sprite"": ""s1"" }
        },
        {
          ""clipFrame"": 2,
          ""writes"": [{ ""track"": ""leadOut"", ""value"": ""s2"" }],
          ""values"": { ""Sprite"": ""s2"" }
        },
        {
          ""clipFrame"": 3,
          ""writes"": [{ ""track"": ""leadBack"", ""value"": ""s1"" }],
          ""values"": { ""Sprite"": ""s1"" }
        }
      ]
    },
    {
      ""label"": ""the mirrored trailing-leg yoyo plays s2 s1 s0 s1 from the same segment"",
      ""covers"": [""yoyoTrailing"", ""directionReverse"", ""cropStart"", ""truncation""],
      ""clipDuration"": 4,
      ""clipFps"": 8,
      ""initialValues"": { ""Sprite"": null },
      ""expectedSequence"": [""s2"", ""s1"", ""s0"", ""s1""],
      ""tracks"": [
        {
          ""id"": ""trailOut"",
          ""target"": ""Sprite"",
          ""startFrame"": 0,
          ""direction"": ""reverse"",
          ""offsetStartIndex"": 0,
          ""offsetEndIndex"": null,
          ""content"": {
            ""kind"": ""segment"",
            ""duration"": 3,
            ""frames"": [
              { ""index"": 0, ""value"": ""s0"" },
              { ""index"": 1, ""value"": ""s1"" },
              { ""index"": 2, ""value"": ""s2"" }
            ]
          }
        },
        {
          ""id"": ""trailBack"",
          ""target"": ""Sprite"",
          ""startFrame"": 3,
          ""direction"": ""forward"",
          ""offsetStartIndex"": 1,
          ""offsetEndIndex"": null,
          ""content"": {
            ""kind"": ""segment"",
            ""duration"": 3,
            ""frames"": [
              { ""index"": 0, ""value"": ""s0"" },
              { ""index"": 1, ""value"": ""s1"" },
              { ""index"": 2, ""value"": ""s2"" }
            ]
          }
        }
      ],
      ""frames"": [
        {
          ""clipFrame"": 0,
          ""writes"": [{ ""track"": ""trailOut"", ""value"": ""s2"" }],
          ""values"": { ""Sprite"": ""s2"" }
        },
        {
          ""clipFrame"": 1,
          ""writes"": [{ ""track"": ""trailOut"", ""value"": ""s1"" }],
          ""values"": { ""Sprite"": ""s1"" }
        },
        {
          ""clipFrame"": 2,
          ""writes"": [{ ""track"": ""trailOut"", ""value"": ""s0"" }],
          ""values"": { ""Sprite"": ""s0"" }
        },
        {
          ""clipFrame"": 3,
          ""writes"": [{ ""track"": ""trailBack"", ""value"": ""s1"" }],
          ""values"": { ""Sprite"": ""s1"" }
        }
      ]
    },
    {
      ""label"": ""two rows on one member apply in Tracks order, and a row that stopped stops winning"",
      ""covers"": [""lastWriteWins"", ""startFrameOffset"", ""coverageEndsEarly""],
      ""clipDuration"": 3,
      ""clipFps"": 8,
      ""initialValues"": { ""Sprite"": null },
      ""tracks"": [
        {
          ""id"": ""under"",
          ""target"": ""Sprite"",
          ""startFrame"": 0,
          ""direction"": ""forward"",
          ""offsetStartIndex"": 0,
          ""offsetEndIndex"": null,
          ""content"": {
            ""kind"": ""segment"",
            ""duration"": 3,
            ""frames"": [
              { ""index"": 0, ""value"": ""a0"" },
              { ""index"": 1, ""value"": ""a1"" },
              { ""index"": 2, ""value"": ""a2"" }
            ]
          }
        },
        {
          ""id"": ""over"",
          ""target"": ""Sprite"",
          ""startFrame"": 1,
          ""direction"": ""forward"",
          ""offsetStartIndex"": 0,
          ""offsetEndIndex"": null,
          ""content"": {
            ""kind"": ""segment"",
            ""duration"": 1,
            ""frames"": [{ ""index"": 0, ""value"": ""b0"" }]
          }
        }
      ],
      ""frames"": [
        {
          ""clipFrame"": 0,
          ""writes"": [{ ""track"": ""under"", ""value"": ""a0"" }],
          ""values"": { ""Sprite"": ""a0"" }
        },
        {
          ""clipFrame"": 1,
          ""writes"": [
            { ""track"": ""under"", ""value"": ""a1"" },
            { ""track"": ""over"", ""value"": ""b0"" }
          ],
          ""values"": { ""Sprite"": ""b0"" }
        },
        {
          ""clipFrame"": 2,
          ""writes"": [{ ""track"": ""under"", ""value"": ""a2"" }],
          ""values"": { ""Sprite"": ""a2"" }
        }
      ]
    },
    {
      ""label"": ""a child clip track reversed at half the parent's clock reverses its nested track too"",
      ""covers"": [
        ""childClipTrack"",
        ""fpsScaling"",
        ""directionReverse"",
        ""nestedReversal""
      ],
      ""clipDuration"": 8,
      ""clipFps"": 8,
      ""initialValues"": { ""Sprite"": null },
      ""tracks"": [
        {
          ""id"": ""embedded"",
          ""target"": ""Sprite"",
          ""startFrame"": 0,
          ""direction"": ""reverse"",
          ""offsetStartIndex"": 0,
          ""offsetEndIndex"": null,
          ""content"": {
            ""kind"": ""clip"",
            ""duration"": 4,
            ""fps"": 4,
            ""frames"": [
              { ""index"": 0, ""value"": ""c0"" },
              { ""index"": 2, ""value"": ""c2"" }
            ],
            ""tracks"": [
              {
                ""id"": ""nested"",
                ""startFrame"": 0,
                ""direction"": ""forward"",
                ""offsetStartIndex"": 0,
                ""offsetEndIndex"": null,
                ""content"": {
                  ""kind"": ""segment"",
                  ""duration"": 4,
                  ""frames"": [
                    { ""index"": 1, ""value"": ""n1"" },
                    { ""index"": 3, ""value"": ""n3"" }
                  ]
                }
              }
            ]
          }
        }
      ],
      ""frames"": [
        {
          ""clipFrame"": 0,
          ""writes"": [{ ""track"": ""embedded"", ""value"": ""n3"" }],
          ""values"": { ""Sprite"": ""n3"" }
        },
        {
          ""clipFrame"": 1,
          ""writes"": [{ ""track"": ""embedded"", ""value"": ""n3"" }],
          ""values"": { ""Sprite"": ""n3"" }
        },
        {
          ""clipFrame"": 2,
          ""writes"": [{ ""track"": ""embedded"", ""value"": ""n1"" }],
          ""values"": { ""Sprite"": ""n1"" }
        },
        {
          ""clipFrame"": 3,
          ""writes"": [{ ""track"": ""embedded"", ""value"": ""n1"" }],
          ""values"": { ""Sprite"": ""n1"" }
        },
        {
          ""clipFrame"": 4,
          ""writes"": [{ ""track"": ""embedded"", ""value"": ""n1"" }],
          ""values"": { ""Sprite"": ""n1"" }
        },
        {
          ""clipFrame"": 5,
          ""writes"": [{ ""track"": ""embedded"", ""value"": ""n1"" }],
          ""values"": { ""Sprite"": ""n1"" }
        },
        {
          ""clipFrame"": 6,
          ""writes"": [{ ""track"": ""embedded"", ""value"": ""c0"" }],
          ""values"": { ""Sprite"": ""c0"" }
        },
        {
          ""clipFrame"": 7,
          ""writes"": [{ ""track"": ""embedded"", ""value"": ""c0"" }],
          ""values"": { ""Sprite"": ""c0"" }
        }
      ]
    },
    {
      ""label"": ""a child clip track crops in the child's frames before the fps scaling"",
      ""covers"": [
        ""childClipTrack"",
        ""fpsScaling"",
        ""cropStart"",
        ""cropEnd"",
        ""coverageEndsEarly""
      ],
      ""clipDuration"": 6,
      ""clipFps"": 8,
      ""initialValues"": { ""Sprite"": null },
      ""tracks"": [
        {
          ""id"": ""croppedChild"",
          ""target"": ""Sprite"",
          ""startFrame"": 0,
          ""direction"": ""forward"",
          ""offsetStartIndex"": 1,
          ""offsetEndIndex"": 3,
          ""content"": {
            ""kind"": ""clip"",
            ""duration"": 4,
            ""fps"": 4,
            ""frames"": [
              { ""index"": 0, ""value"": ""c0"" },
              { ""index"": 1, ""value"": ""c1"" },
              { ""index"": 2, ""value"": ""c2"" },
              { ""index"": 3, ""value"": ""c3"" }
            ],
            ""tracks"": []
          }
        }
      ],
      ""frames"": [
        {
          ""clipFrame"": 0,
          ""writes"": [{ ""track"": ""croppedChild"", ""value"": ""c1"" }],
          ""values"": { ""Sprite"": ""c1"" }
        },
        {
          ""clipFrame"": 1,
          ""writes"": [{ ""track"": ""croppedChild"", ""value"": ""c1"" }],
          ""values"": { ""Sprite"": ""c1"" }
        },
        {
          ""clipFrame"": 2,
          ""writes"": [{ ""track"": ""croppedChild"", ""value"": ""c2"" }],
          ""values"": { ""Sprite"": ""c2"" }
        },
        {
          ""clipFrame"": 3,
          ""writes"": [{ ""track"": ""croppedChild"", ""value"": ""c2"" }],
          ""values"": { ""Sprite"": ""c2"" }
        },
        { ""clipFrame"": 4, ""writes"": [], ""values"": { ""Sprite"": ""c2"" } },
        { ""clipFrame"": 5, ""writes"": [], ""values"": { ""Sprite"": ""c2"" } }
      ]
    },
    {
      ""label"": ""a crop end past the resolved content clamps instead of erroring"",
      ""covers"": [""runtimeClamp"", ""coverageEndsEarly""],
      ""clipDuration"": 4,
      ""clipFps"": 8,
      ""initialValues"": { ""Sprite"": null },
      ""tracks"": [
        {
          ""id"": ""optimistic"",
          ""target"": ""Sprite"",
          ""startFrame"": 0,
          ""direction"": ""forward"",
          ""offsetStartIndex"": 0,
          ""offsetEndIndex"": 5,
          ""content"": {
            ""kind"": ""segment"",
            ""duration"": 2,
            ""frames"": [
              { ""index"": 0, ""value"": ""x0"" },
              { ""index"": 1, ""value"": ""x1"" }
            ]
          }
        }
      ],
      ""frames"": [
        {
          ""clipFrame"": 0,
          ""writes"": [{ ""track"": ""optimistic"", ""value"": ""x0"" }],
          ""values"": { ""Sprite"": ""x0"" }
        },
        {
          ""clipFrame"": 1,
          ""writes"": [{ ""track"": ""optimistic"", ""value"": ""x1"" }],
          ""values"": { ""Sprite"": ""x1"" }
        },
        { ""clipFrame"": 2, ""writes"": [], ""values"": { ""Sprite"": ""x1"" } },
        { ""clipFrame"": 3, ""writes"": [], ""values"": { ""Sprite"": ""x1"" } }
      ]
    },
    {
      ""label"": ""a crop window entirely past the resolved content writes nothing at all"",
      ""covers"": [""runtimeClamp"", ""emptyResolvedWindow""],
      ""clipDuration"": 2,
      ""clipFps"": 8,
      ""initialValues"": { ""Sprite"": ""idle"" },
      ""tracks"": [
        {
          ""id"": ""stranded"",
          ""target"": ""Sprite"",
          ""startFrame"": 0,
          ""direction"": ""forward"",
          ""offsetStartIndex"": 3,
          ""offsetEndIndex"": null,
          ""content"": {
            ""kind"": ""segment"",
            ""duration"": 2,
            ""frames"": [
              { ""index"": 0, ""value"": ""x0"" },
              { ""index"": 1, ""value"": ""x1"" }
            ]
          }
        }
      ],
      ""frames"": [
        { ""clipFrame"": 0, ""writes"": [], ""values"": { ""Sprite"": ""idle"" } },
        { ""clipFrame"": 1, ""writes"": [], ""values"": { ""Sprite"": ""idle"" } }
      ]
    },
    {
      ""label"": ""a segment with no frames writes nothing on every frame of its window"",
      ""covers"": [""emptyContent""],
      ""clipDuration"": 3,
      ""clipFps"": 8,
      ""initialValues"": { ""Sprite"": ""idle"" },
      ""tracks"": [
        {
          ""id"": ""unequipped"",
          ""target"": ""Sprite"",
          ""startFrame"": 0,
          ""direction"": ""forward"",
          ""offsetStartIndex"": 0,
          ""offsetEndIndex"": null,
          ""content"": { ""kind"": ""segment"", ""duration"": 3, ""frames"": [] }
        }
      ],
      ""frames"": [
        { ""clipFrame"": 0, ""writes"": [], ""values"": { ""Sprite"": ""idle"" } },
        { ""clipFrame"": 1, ""writes"": [], ""values"": { ""Sprite"": ""idle"" } },
        { ""clipFrame"": 2, ""writes"": [], ""values"": { ""Sprite"": ""idle"" } }
      ]
    }
  ]
}
";
    }
}
