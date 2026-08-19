// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
//
// VENDORED — DO NOT HAND-EDIT.
// Source of record:
// neo-compose/src/models/neoscript/math-parity-fixture.json
// P69 section 3. `Math` is the one NeoScript intrinsic whose two hosts
// disagree by default — JS `Math.round` is half-up, `System.Math.Round` is
// half-even — so the spec pins one semantic and the divergent host implements
// it by hand. A fixture generated from either runtime would only re-state
// that runtime's answer, so the edges (the midpoint ladder, the directional
// negatives, NaN propagation, the 2^53 boundary, the byte-exact error
// strings, and the decimal arms) are hand-authored and shared verbatim.
//
// To re-vendor: copy the JSON verbatim and double every `"` for the C#
// verbatim string. Consumed here by NeoScriptMathParityTests and on the
// web side by src/models/neoscript/math-parity.test.ts.

#nullable enable

namespace NeoCompose.Tests
{
    public static class NeoScriptMathParityFixture
    {
        public const string Json = @"{
  ""$comment"": ""P69 §3 cross-runtime Math builtin parity fixture. Hand-authored raw IR, never generated from either runtime: a fixture generated from one runtime cannot catch a divergence in the other's source of truth, and `Math` is the one intrinsic whose two hosts disagree by default (JS `Math.round` is half-up, `System.Math.Round` is half-even). Consumed by src/models/neoscript/math-parity.test.ts (web) and NeoScriptMathParityTests (neo-compose-dotnet, vendored verbatim copy). Decimal expectations were checked against Python's `decimal` module (ROUND_HALF_EVEN) as an independent oracle, like src/models/decimal/decimal-parity-fixture.json."",
  ""$evaluateComment"": ""Each case's `pointer` is the compiled IR both runtimes receive: consumers wrap it in a getter with no parameters and a single `return` instruction whose result type is the case's `typeInfo` (the type the resolver infers per P69 §2.1). Exactly one expectation is present per case. `expected` is compared numerically as a double for int/float cases and as the canonical decimal string for decimal cases; ±0 is one value there, since §3 deliberately does not pin negative zero through the JSON harness (`Math.Min(-0.0, 0.0)` is -0.0 in both hosts and the fixture pins only that it is zero). `expectedNaN` is `true` where the result must be NaN, which JSON has no literal for. `expectedError` must match the thrown evaluator error byte-for-byte. Non-finite arguments are constructed with arithmetic IR rather than written as literals for the same reason, and via overflow rather than division because both runtimes throw `Division by zero` instead of yielding an infinity: `1e200 * 1e200` overflows to +Infinity, `-1e200 * 1e200` to -Infinity, and `(1e200 * 1e200) * 0.0` is NaN — IEEE-exact and identical in both hosts."",
  ""evaluateCases"": [
    {
      ""name"": ""Round(0.5) is half-even"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 0.5
                }
              }
            ]
          }
        }
      },
      ""expected"": 0
    },
    {
      ""name"": ""Round(1.5) is half-even"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 1.5
                }
              }
            ]
          }
        }
      },
      ""expected"": 2
    },
    {
      ""name"": ""Round(2.5) is half-even"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 2.5
                }
              }
            ]
          }
        }
      },
      ""expected"": 2
    },
    {
      ""name"": ""Round(3.5) is half-even"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 3.5
                }
              }
            ]
          }
        }
      },
      ""expected"": 4
    },
    {
      ""name"": ""Round(-0.5) is half-even"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -0.5
                }
              }
            ]
          }
        }
      },
      ""expected"": 0
    },
    {
      ""name"": ""Round(-1.5) is half-even"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -1.5
                }
              }
            ]
          }
        }
      },
      ""expected"": -2
    },
    {
      ""name"": ""Round(-2.5) is half-even"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -2.5
                }
              }
            ]
          }
        }
      },
      ""expected"": -2
    },
    {
      ""name"": ""Round(-3.5) is half-even"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -3.5
                }
              }
            ]
          }
        }
      },
      ""expected"": -4
    },
    {
      ""name"": ""Round(2.4) rounds to the nearer integer"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 2.4
                }
              }
            ]
          }
        }
      },
      ""expected"": 2
    },
    {
      ""name"": ""Round(2.6) rounds to the nearer integer"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 2.6
                }
              }
            ]
          }
        }
      },
      ""expected"": 3
    },
    {
      ""name"": ""Round(-2.4) rounds to the nearer integer"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -2.4
                }
              }
            ]
          }
        }
      },
      ""expected"": -2
    },
    {
      ""name"": ""Round(-2.6) rounds to the nearer integer"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -2.6
                }
              }
            ]
          }
        }
      },
      ""expected"": -3
    },
    {
      ""name"": ""Round of an int is the identity"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -7
                }
              }
            ]
          }
        }
      },
      ""expected"": -7
    },
    {
      ""name"": ""Floor(-4.25) rounds toward its own direction"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""floor"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -4.25
                }
              }
            ]
          }
        }
      },
      ""expected"": -5
    },
    {
      ""name"": ""Ceiling(-4.25) rounds toward its own direction"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""ceiling"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -4.25
                }
              }
            ]
          }
        }
      },
      ""expected"": -4
    },
    {
      ""name"": ""Truncate(-4.25) rounds toward its own direction"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""truncate"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -4.25
                }
              }
            ]
          }
        }
      },
      ""expected"": -4
    },
    {
      ""name"": ""Floor(4.25) rounds toward its own direction"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""floor"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 4.25
                }
              }
            ]
          }
        }
      },
      ""expected"": 4
    },
    {
      ""name"": ""Ceiling(4.25) rounds toward its own direction"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""ceiling"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 4.25
                }
              }
            ]
          }
        }
      },
      ""expected"": 5
    },
    {
      ""name"": ""Truncate(4.25) rounds toward its own direction"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""truncate"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 4.25
                }
              }
            ]
          }
        }
      },
      ""expected"": 4
    },
    {
      ""name"": ""Floor of an int is the identity"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""floor"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -7
                }
              }
            ]
          }
        }
      },
      ""expected"": -7
    },
    {
      ""name"": ""Ceiling of an int is the identity"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""ceiling"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -7
                }
              }
            ]
          }
        }
      },
      ""expected"": -7
    },
    {
      ""name"": ""Truncate of an int is the identity"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""truncate"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -7
                }
              }
            ]
          }
        }
      },
      ""expected"": -7
    },
    {
      ""name"": ""Round leaves a value at the 2^53 boundary unchanged"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 9007199254740992.0
                }
              }
            ]
          }
        }
      },
      ""expected"": 9007199254740992
    },
    {
      ""name"": ""Floor leaves a value at the 2^53 boundary unchanged"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""floor"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 9007199254740992.0
                }
              }
            ]
          }
        }
      },
      ""expected"": 9007199254740992
    },
    {
      ""name"": ""Truncate leaves a negative value at the 2^53 boundary unchanged"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""truncate"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -9007199254740992.0
                }
              }
            ]
          }
        }
      },
      ""expected"": -9007199254740992
    },
    {
      ""name"": ""Min of two ints returns the lesser"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""min"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 3
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 7
                }
              }
            ]
          }
        }
      },
      ""expected"": 3
    },
    {
      ""name"": ""Max of two ints returns the greater"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""max"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 3
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 7
                }
              }
            ]
          }
        }
      },
      ""expected"": 7
    },
    {
      ""name"": ""Min widens an int argument beside a float"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""min"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 3
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 2.5
                }
              }
            ]
          }
        }
      },
      ""expected"": 2.5
    },
    {
      ""name"": ""Min returns the widened int when it is the lesser"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""min"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 3
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 7.5
                }
              }
            ]
          }
        }
      },
      ""expected"": 3
    },
    {
      ""name"": ""Min of negative zero and positive zero is zero"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""min"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -0.0
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 0.0
                }
              }
            ]
          }
        }
      },
      ""expected"": 0
    },
    {
      ""name"": ""Min against a positive infinity bound returns the finite argument"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""min"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 1.5
                }
              },
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 1e200
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 1e200
                        }
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": 1.5
    },
    {
      ""name"": ""Max against a negative infinity bound returns the finite argument"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""max"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 1.5
                }
              },
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": -1e200
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 1e200
                        }
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": 1.5
    },
    {
      ""name"": ""Min propagates a NaN first argument"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""min"",
            ""argPointers"": [
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""operation"",
                        ""operation"": {
                          ""type"": ""arithmetic"",
                          ""arithmetic"": {
                            ""type"": ""*"",
                            ""pointers"": [
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              },
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              }
                            ]
                          }
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 0.0
                        }
                      }
                    ]
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 1.0
                }
              }
            ]
          }
        }
      },
      ""expectedNaN"": true
    },
    {
      ""name"": ""Min propagates a NaN second argument"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""min"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 1.0
                }
              },
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""operation"",
                        ""operation"": {
                          ""type"": ""arithmetic"",
                          ""arithmetic"": {
                            ""type"": ""*"",
                            ""pointers"": [
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              },
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              }
                            ]
                          }
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 0.0
                        }
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expectedNaN"": true
    },
    {
      ""name"": ""Max propagates a NaN argument"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""max"",
            ""argPointers"": [
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""operation"",
                        ""operation"": {
                          ""type"": ""arithmetic"",
                          ""arithmetic"": {
                            ""type"": ""*"",
                            ""pointers"": [
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              },
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              }
                            ]
                          }
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 0.0
                        }
                      }
                    ]
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 1.0
                }
              }
            ]
          }
        }
      },
      ""expectedNaN"": true
    },
    {
      ""name"": ""Clamp propagates a NaN value through both comparisons"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""clamp"",
            ""argPointers"": [
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""operation"",
                        ""operation"": {
                          ""type"": ""arithmetic"",
                          ""arithmetic"": {
                            ""type"": ""*"",
                            ""pointers"": [
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              },
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              }
                            ]
                          }
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 0.0
                        }
                      }
                    ]
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 0.0
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 10.0
                }
              }
            ]
          }
        }
      },
      ""expectedNaN"": true
    },
    {
      ""name"": ""Abs propagates NaN"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""abs"",
            ""argPointers"": [
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""operation"",
                        ""operation"": {
                          ""type"": ""arithmetic"",
                          ""arithmetic"": {
                            ""type"": ""*"",
                            ""pointers"": [
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              },
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              }
                            ]
                          }
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 0.0
                        }
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expectedNaN"": true
    },
    {
      ""name"": ""Clamp returns an in-range int value"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""clamp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 5
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 0
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 10
                }
              }
            ]
          }
        }
      },
      ""expected"": 5
    },
    {
      ""name"": ""Clamp pins an int value to the lower bound"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""clamp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -3
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 0
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 10
                }
              }
            ]
          }
        }
      },
      ""expected"": 0
    },
    {
      ""name"": ""Clamp pins an int value to the upper bound"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""clamp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 42
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 0
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 10
                }
              }
            ]
          }
        }
      },
      ""expected"": 10
    },
    {
      ""name"": ""Clamp with an infinite upper bound is one-sided"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""clamp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 12.5
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 0.0
                }
              },
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 1e200
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 1e200
                        }
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": 12.5
    },
    {
      ""name"": ""Clamp with an infinite upper bound still pins the lower bound"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""clamp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -5.5
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 0.0
                }
              },
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 1e200
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 1e200
                        }
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": 0
    },
    {
      ""name"": ""Clamp with an infinite lower bound pins the upper bound"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""clamp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 42.5
                }
              },
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": -1e200
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 1e200
                        }
                      }
                    ]
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 10.0
                }
              }
            ]
          }
        }
      },
      ""expected"": 10
    },
    {
      ""name"": ""NaN bounds do not trip the Clamp order guard"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""clamp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 1.0
                }
              },
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""operation"",
                        ""operation"": {
                          ""type"": ""arithmetic"",
                          ""arithmetic"": {
                            ""type"": ""*"",
                            ""pointers"": [
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              },
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              }
                            ]
                          }
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 0.0
                        }
                      }
                    ]
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 10.0
                }
              }
            ]
          }
        }
      },
      ""expected"": 1
    },
    {
      ""name"": ""Abs of a negative float"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""abs"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -4.25
                }
              }
            ]
          }
        }
      },
      ""expected"": 4.25
    },
    {
      ""name"": ""Abs of a negative int mirrors the int type"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""abs"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -7
                }
              }
            ]
          }
        }
      },
      ""expected"": 7
    },
    {
      ""name"": ""Abs of negative zero is zero"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""abs"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -0.0
                }
              }
            ]
          }
        }
      },
      ""expected"": 0
    },
    {
      ""name"": ""Sign of a negative float is -1"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""sign"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -3.2
                }
              }
            ]
          }
        }
      },
      ""expected"": -1
    },
    {
      ""name"": ""Sign of a positive float is 1"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""sign"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 3.2
                }
              }
            ]
          }
        }
      },
      ""expected"": 1
    },
    {
      ""name"": ""Sign of zero is 0"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""sign"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 0.0
                }
              }
            ]
          }
        }
      },
      ""expected"": 0
    },
    {
      ""name"": ""Sign of negative zero is 0"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""sign"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -0.0
                }
              }
            ]
          }
        }
      },
      ""expected"": 0
    },
    {
      ""name"": ""Sign of a negative int is -1"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""sign"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -7
                }
              }
            ]
          }
        }
      },
      ""expected"": -1
    },
    {
      ""name"": ""Sqrt of a perfect square"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""sqrt"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 9.0
                }
              }
            ]
          }
        }
      },
      ""expected"": 3
    },
    {
      ""name"": ""Sqrt is correctly rounded"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""sqrt"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 2.0
                }
              }
            ]
          }
        }
      },
      ""expected"": 1.4142135623730951
    },
    {
      ""name"": ""Sqrt widens an int argument"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""sqrt"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 16
                }
              }
            ]
          }
        }
      },
      ""expected"": 4
    },
    {
      ""name"": ""Sqrt of a negative value is NaN"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""sqrt"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -1.0
                }
              }
            ]
          }
        }
      },
      ""expectedNaN"": true
    },
    {
      ""name"": ""Clamp rejects reversed bounds"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""clamp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 5.0
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 10.0
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 2.0
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""Math.Clamp requires min <= max.""
    },
    {
      ""name"": ""Clamp rejects reversed decimal bounds"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""clamp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""5""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""10""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""2""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expectedError"": ""Math.Clamp requires min <= max.""
    },
    {
      ""name"": ""Sign of NaN is a runtime error"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""sign"",
            ""argPointers"": [
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""operation"",
                        ""operation"": {
                          ""type"": ""arithmetic"",
                          ""arithmetic"": {
                            ""type"": ""*"",
                            ""pointers"": [
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              },
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              }
                            ]
                          }
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 0.0
                        }
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""Math.Sign is undefined for NaN.""
    },
    {
      ""name"": ""Round rejects an infinite argument"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 1e200
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 1e200
                        }
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""Math.Round requires a finite argument.""
    },
    {
      ""name"": ""Round rejects a NaN argument"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""operation"",
                        ""operation"": {
                          ""type"": ""arithmetic"",
                          ""arithmetic"": {
                            ""type"": ""*"",
                            ""pointers"": [
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              },
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              }
                            ]
                          }
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 0.0
                        }
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""Math.Round requires a finite argument.""
    },
    {
      ""name"": ""Floor rejects an infinite argument"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""floor"",
            ""argPointers"": [
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 1e200
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 1e200
                        }
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""Math.Floor requires a finite argument.""
    },
    {
      ""name"": ""Floor rejects a NaN argument"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""floor"",
            ""argPointers"": [
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""operation"",
                        ""operation"": {
                          ""type"": ""arithmetic"",
                          ""arithmetic"": {
                            ""type"": ""*"",
                            ""pointers"": [
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              },
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              }
                            ]
                          }
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 0.0
                        }
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""Math.Floor requires a finite argument.""
    },
    {
      ""name"": ""Ceiling rejects a negative infinite argument"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""ceiling"",
            ""argPointers"": [
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": -1e200
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 1e200
                        }
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""Math.Ceiling requires a finite argument.""
    },
    {
      ""name"": ""Ceiling rejects a NaN argument"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""ceiling"",
            ""argPointers"": [
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""operation"",
                        ""operation"": {
                          ""type"": ""arithmetic"",
                          ""arithmetic"": {
                            ""type"": ""*"",
                            ""pointers"": [
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              },
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              }
                            ]
                          }
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 0.0
                        }
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""Math.Ceiling requires a finite argument.""
    },
    {
      ""name"": ""Truncate rejects an infinite argument"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""truncate"",
            ""argPointers"": [
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 1e200
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 1e200
                        }
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""Math.Truncate requires a finite argument.""
    },
    {
      ""name"": ""Truncate rejects a NaN argument"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""truncate"",
            ""argPointers"": [
              {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""operation"",
                        ""operation"": {
                          ""type"": ""arithmetic"",
                          ""arithmetic"": {
                            ""type"": ""*"",
                            ""pointers"": [
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              },
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  },
                                  ""value"": 1e200
                                }
                              }
                            ]
                          }
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 0.0
                        }
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""Math.Truncate requires a finite argument.""
    },
    {
      ""name"": ""Min of tied decimals returns the first argument, scale and all"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""min"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""1.10""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""1.1""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""1.10""
    },
    {
      ""name"": ""Max of tied decimals returns the first argument, scale and all"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""max"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""1.1""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""1.10""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""1.1""
    },
    {
      ""name"": ""Min of decimals compares exactly, not lexically"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""min"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""2.5""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""10""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""2.5""
    },
    {
      ""name"": ""Max of decimals compares exactly, not lexically"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""max"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""2.5""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""10""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""10""
    },
    {
      ""name"": ""Min widens an int argument into the decimal arm"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""min"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 3
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""2.5""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""2.5""
    },
    {
      ""name"": ""Clamp returns an in-range decimal value"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""clamp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""5.5""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""0""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""10.0""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""5.5""
    },
    {
      ""name"": ""Clamp pins a decimal value to the upper bound"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""clamp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""42.5""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""0""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""10.0""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""10.0""
    },
    {
      ""name"": ""Clamp pins a decimal value to the lower bound"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""clamp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""-1""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""0""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""10.0""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""0""
    },
    {
      ""name"": ""Floor(-4.25) as a decimal is canonical at scale 0"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""floor"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""-4.25""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""-5""
    },
    {
      ""name"": ""Ceiling(-4.25) as a decimal is canonical at scale 0"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""ceiling"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""-4.25""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""-4""
    },
    {
      ""name"": ""Truncate(-4.25) as a decimal is canonical at scale 0"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""truncate"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""-4.25""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""-4""
    },
    {
      ""name"": ""Floor(4.25) as a decimal is canonical at scale 0"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""floor"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""4.25""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""4""
    },
    {
      ""name"": ""Ceiling(4.25) as a decimal is canonical at scale 0"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""ceiling"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""4.25""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""5""
    },
    {
      ""name"": ""Truncate(4.25) as a decimal is canonical at scale 0"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""truncate"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""4.25""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""4""
    },
    {
      ""name"": ""Round(2.5) as a decimal is half-even, equal to 2.5.Round(0)"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""2.5""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""2""
    },
    {
      ""name"": ""Round(3.5) as a decimal is half-even, equal to 3.5.Round(0)"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""3.5""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""4""
    },
    {
      ""name"": ""Round(-2.5) as a decimal is half-even, equal to -2.5.Round(0)"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""round"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""-2.5""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""-2""
    },
    {
      ""name"": ""Abs of a negative decimal preserves scale"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""abs"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""-4.250""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""4.250""
    },
    {
      ""name"": ""Abs of a positive decimal is the identity"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""abs"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""4.25""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": ""4.25""
    },
    {
      ""name"": ""Sign of a negative decimal is -1"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""sign"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""-0.001""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": -1
    },
    {
      ""name"": ""Sign of a zero decimal is 0"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""sign"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""0""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": 0
    },
    {
      ""name"": ""Sign of a positive decimal is 1"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""mathOp"",
          ""info"": {
            ""op"": ""sign"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""12.5""
                }
              }
            ],
            ""decimal"": true
          }
        }
      },
      ""expected"": 1
    }
  ]
}";
    }
}
