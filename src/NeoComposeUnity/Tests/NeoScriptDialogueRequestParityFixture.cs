// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
//
// VENDORED — DO NOT HAND-EDIT.
// Source of record:
// neo-compose/src/models/neoscript/dialogue-request-parity-fixture.json
// P105 section 2.5. NeoScript dialogue requests must return the same result,
// and throw the byte-exact same errors, on both runtimes.
//
// To re-vendor: copy the JSON verbatim and double every `"` for the C#
// verbatim string. Consumed here by NeoDialogueTriggerTests and on the web
// side by src/models/neoscript/dialogue-request-parity.test.ts.

#nullable enable

namespace NeoCompose.Tests
{
    public static class NeoScriptDialogueRequestParityFixture
    {
        public const string Json = @"{
  ""$comment"": ""P105 \u00a72.5 cross-runtime dialogue request parity fixture. Hand-authored raw IR, never generated from either runtime. Consumed by src/models/neoscript/dialogue-request-parity.test.ts (web) and NeoScriptDialogueRequestParityTests (neo-compose-dotnet, vendored verbatim copy)."",
  ""$evaluateComment"": ""Consumers wrap each `pointer` in a getter with no parameters and a single `return` instruction typed bool. Each runtime evaluates with no dialogue presenter and no dialogue or group the ids name, so every request is not triggered. `expectedResult` is the returned bool, or null when a `?.` call short-circuits. `expectedError` must match the thrown evaluator error byte-for-byte. Exactly one expectation is present per case."",
  ""evaluateCases"": [
    {
      ""name"": ""TryTrigger by id"",
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""dialogue"",
          ""info"": {
            ""op"": ""TryTrigger"",
            ""dialogueIdPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""dialogue-parity""
              }
            }
          }
        }
      },
      ""expectedResult"": false
    },
    {
      ""name"": ""CanTrigger by id"",
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""dialogue"",
          ""info"": {
            ""op"": ""CanTrigger"",
            ""dialogueIdPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""dialogue-parity""
              }
            }
          }
        }
      },
      ""expectedResult"": false
    },
    {
      ""name"": ""TryTrigger on a standard group"",
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""dialogueGroup"",
          ""info"": {
            ""op"": ""TryTrigger"",
            ""groupId"": ""group-parity""
          }
        }
      },
      ""expectedResult"": false
    },
    {
      ""name"": ""CanTrigger on a lookup group with a null value"",
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""dialogueGroup"",
          ""info"": {
            ""op"": ""CanTrigger"",
            ""groupId"": ""group-parity"",
            ""valuePointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 0,
                  ""required"": false
                },
                ""value"": null
              }
            }
          }
        }
      },
      ""expectedResult"": false
    },
    {
      ""name"": ""TryTrigger through a null optional reference"",
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""dialogue"",
          ""info"": {
            ""op"": ""TryTrigger"",
            ""dialogueIdPointer"": {
              ""type"": ""keyOf"",
              ""keyOf"": {
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 0,
                      ""required"": false
                    },
                    ""value"": null
                  }
                },
                ""key"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 0
                  }
                }
              },
              ""optional"": true
            }
          }
        }
      },
      ""expectedResult"": null
    },
    {
      ""name"": ""TryTrigger with a null dialogue id"",
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""dialogue"",
          ""info"": {
            ""op"": ""TryTrigger"",
            ""dialogueIdPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 0,
                  ""required"": false
                },
                ""value"": null
              }
            }
          }
        }
      },
      ""expectedError"": ""TryTrigger needs a dialogue id string, but got null.""
    },
    {
      ""name"": ""CanTrigger with a null dialogue id"",
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""dialogue"",
          ""info"": {
            ""op"": ""CanTrigger"",
            ""dialogueIdPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 0,
                  ""required"": false
                },
                ""value"": null
              }
            }
          }
        }
      },
      ""expectedError"": ""CanTrigger needs a dialogue id string, but got null.""
    }
  ]
}";
    }
}
