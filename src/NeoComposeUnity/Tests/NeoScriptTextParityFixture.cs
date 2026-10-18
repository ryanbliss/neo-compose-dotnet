// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
//
// VENDORED — DO NOT HAND-EDIT.
// Source of record:
// neo-compose/src/models/neoscript/text-parity-fixture.json
// P116 section 4.5. The string library and number text must agree byte for
// byte on both runtimes, and the two hosts disagree by default on whitespace,
// midpoint rounding, and number formatting. The expectations come from an
// independent oracle, never from either runtime.
//
// To re-vendor: copy the JSON verbatim and double every `"` for the C#
// verbatim string. Consumed here by NeoScriptTextParityTests and on the web
// side by packages/models/src/neoscript/text-parity.test.ts.

#nullable enable

namespace NeoCompose.Tests
{
    public static class NeoScriptTextParityFixture
    {
        public const string Json = @"{
  ""$comment"": ""P116 \u00a74.5 cross-runtime string library and number text parity fixture. Hand-authored raw IR, never generated from either runtime: a fixture dumped from one runtime cannot catch a divergence in that runtime's own logic. Expectations come from an independent oracle (Python's str over UTF-16 code units, decimal quantize with ROUND_HALF_EVEN for float and ROUND_HALF_UP for decimal, and struct float32 round trips). Consumed by packages/models/src/neoscript/text-parity.test.ts (web) and NeoScriptTextParityTests (neo-compose-dotnet, vendored verbatim copy)."",
  ""$evaluateComment"": ""Consumers wrap each case's `pointer` in a parameterless getter whose single `return` has the case's `typeInfo`. Exactly one expectation is present per case. `expected` is compared exactly: strings and bools as-is, int and float results numerically as doubles, decimal results as canonical strings, and `null` for a TryParse that refused its input. `expectedList` is a List<string> compared entry by entry. `expectedError` must match the thrown evaluator error byte for byte. Non-finite floats come from IEEE-exact arithmetic (`1e200 * 1e200`) because JSON has no literal for them."",
  ""evaluateCases"": [
    {
      ""name"": ""Length counts UTF-16 code units of a surrogate pair"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""length"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""a\ud83d\ude00b""
                }
              }
            ]
          }
        }
      },
      ""expected"": 4
    },
    {
      ""name"": ""Length counts a lone surrogate as one unit"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""length"",
            ""argPointers"": [
              {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""textOp"",
                  ""info"": {
                    ""op"": ""substring"",
                    ""argPointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""a\ud83d\ude00b""
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 1
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 1
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
      ""expected"": 1
    },
    {
      ""name"": ""Length of empty is 0"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""length"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            ]
          }
        }
      },
      ""expected"": 0
    },
    {
      ""name"": ""Substring can split a surrogate pair"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""indexOf"",
            ""argPointers"": [
              {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""textOp"",
                  ""info"": {
                    ""op"": ""substring"",
                    ""argPointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""a\ud83d\ude00b""
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
                          ""value"": 2
                        }
                      }
                    ]
                  }
                }
              },
              {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""textOp"",
                  ""info"": {
                    ""op"": ""substring"",
                    ""argPointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""a\ud83d\ude00b""
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 1
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 1
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
      ""expected"": 1
    },
    {
      ""name"": ""Substring of a split pair keeps only its half"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""indexOf"",
            ""argPointers"": [
              {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""textOp"",
                  ""info"": {
                    ""op"": ""substring"",
                    ""argPointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""a\ud83d\ude00b""
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
                          ""value"": 2
                        }
                      }
                    ]
                  }
                }
              },
              {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""textOp"",
                  ""info"": {
                    ""op"": ""substring"",
                    ""argPointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""a\ud83d\ude00b""
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 2
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 1
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
      ""expected"": -1
    },
    {
      ""name"": ""Substring from the low surrogate to the end"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""length"",
            ""argPointers"": [
              {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""textOp"",
                  ""info"": {
                    ""op"": ""substring"",
                    ""argPointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""a\ud83d\ude00b""
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 2
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
      ""expected"": 2
    },
    {
      ""name"": ""Substring at Length is empty"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""substring"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""abc""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 3
                }
              }
            ]
          }
        }
      },
      ""expected"": """"
    },
    {
      ""name"": ""Substring with zero length"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""substring"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""abc""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 1
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
              }
            ]
          }
        }
      },
      ""expected"": """"
    },
    {
      ""name"": ""Substring keeps a whole pair"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""substring"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""a\ud83d\ude00b""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 1
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 2
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\ud83d\ude00""
    },
    {
      ""name"": ""IndexOf finds after a surrogate pair in code units"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""indexOf"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""a\ud83d\ude00b""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""b""
                }
              }
            ]
          }
        }
      },
      ""expected"": 3
    },
    {
      ""name"": ""IndexOf finds a lone low surrogate inside a pair"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""indexOf"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""a\ud83d\ude00b""
                }
              },
              {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""textOp"",
                  ""info"": {
                    ""op"": ""substring"",
                    ""argPointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""a\ud83d\ude00b""
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 2
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 1
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
      ""expected"": 2
    },
    {
      ""name"": ""LastIndexOf finds a lone high surrogate inside a pair"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""lastIndexOf"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""a\ud83d\ude00ba\ud83d\ude00b""
                }
              },
              {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""textOp"",
                  ""info"": {
                    ""op"": ""substring"",
                    ""argPointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""a\ud83d\ude00b""
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 1
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 1
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
      ""expected"": 5
    },
    {
      ""name"": ""IndexOf of CRLF's LF is ordinal"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""indexOf"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\r\n""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\n""
                }
              }
            ]
          }
        }
      },
      ""expected"": 1
    },
    {
      ""name"": ""IndexOf empty is 0"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""indexOf"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""abc""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            ]
          }
        }
      },
      ""expected"": 0
    },
    {
      ""name"": ""IndexOf missing is -1"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""indexOf"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""abc""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""d""
                }
              }
            ]
          }
        }
      },
      ""expected"": -1
    },
    {
      ""name"": ""IndexOf is case-sensitive"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""indexOf"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""abc""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""B""
                }
              }
            ]
          }
        }
      },
      ""expected"": -1
    },
    {
      ""name"": ""LastIndexOf finds the last match"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""lastIndexOf"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""abcabc""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""bc""
                }
              }
            ]
          }
        }
      },
      ""expected"": 4
    },
    {
      ""name"": ""LastIndexOf empty is Length"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""lastIndexOf"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""abc""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            ]
          }
        }
      },
      ""expected"": 3
    },
    {
      ""name"": ""LastIndexOf missing is -1"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""lastIndexOf"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""abc""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""z""
                }
              }
            ]
          }
        }
      },
      ""expected"": -1
    },
    {
      ""name"": ""LastIndexOf of CRLF's CR is ordinal"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""lastIndexOf"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\r\n\r\n""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\r""
                }
              }
            ]
          }
        }
      },
      ""expected"": 2
    },
    {
      ""name"": ""Split keeps leading, trailing, and adjacent empties"",
      ""typeInfo"": {
        ""type"": 6,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""split"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "",a,,b,""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "",""
                }
              }
            ]
          }
        }
      },
      ""expectedList"": ["""", ""a"", """", ""b"", """"]
    },
    {
      ""name"": ""Split with a multi-character separator"",
      ""typeInfo"": {
        ""type"": 6,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""split"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""a--b--c""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""--""
                }
              }
            ]
          }
        }
      },
      ""expectedList"": [""a"", ""b"", ""c""]
    },
    {
      ""name"": ""Split of an empty receiver is one empty entry"",
      ""typeInfo"": {
        ""type"": 6,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""split"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "",""
                }
              }
            ]
          }
        }
      },
      ""expectedList"": [""""]
    },
    {
      ""name"": ""Split with an empty separator returns the receiver"",
      ""typeInfo"": {
        ""type"": 6,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""split"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""a,b""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            ]
          }
        }
      },
      ""expectedList"": [""a,b""]
    },
    {
      ""name"": ""Split with no match returns the receiver"",
      ""typeInfo"": {
        ""type"": 6,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""split"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""abc""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "",""
                }
              }
            ]
          }
        }
      },
      ""expectedList"": [""abc""]
    },
    {
      ""name"": ""PadLeft defaults to spaces"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""padLeft"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""7""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 3
                }
              }
            ]
          }
        }
      },
      ""expected"": ""  7""
    },
    {
      ""name"": ""PadLeft with a padding char"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""padLeft"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""7""
                }
              },
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
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""007""
    },
    {
      ""name"": ""PadRight with a padding char"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""padRight"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""ab""
                }
              },
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
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "".""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""ab...""
    },
    {
      ""name"": ""PadRight defaults to spaces"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""padRight"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""ab""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 4
                }
              }
            ]
          }
        }
      },
      ""expected"": ""ab  ""
    },
    {
      ""name"": ""PadLeft leaves a long receiver unchanged"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""padLeft"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""abcd""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 2
                }
              }
            ]
          }
        }
      },
      ""expected"": ""abcd""
    },
    {
      ""name"": ""PadLeft counts code units"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""padLeft"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\ud83d\ude00""
                }
              },
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
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""-\ud83d\ude00""
    },
    {
      ""name"": ""PadLeft accepts a lone surrogate padding char"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""indexOf"",
            ""argPointers"": [
              {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""textOp"",
                  ""info"": {
                    ""op"": ""padLeft"",
                    ""argPointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""a""
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 2
                        }
                      },
                      {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""textOp"",
                          ""info"": {
                            ""op"": ""substring"",
                            ""argPointers"": [
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 3,
                                    ""required"": true
                                  },
                                  ""value"": ""a\ud83d\ude00b""
                                }
                              },
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  },
                                  ""value"": 1
                                }
                              },
                              {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  },
                                  ""value"": 1
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
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""a""
                }
              }
            ]
          }
        }
      },
      ""expected"": 1
    },
    {
      ""name"": ""Trim strips U+0009"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\ta\t""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+0009"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\ta\t""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\t""
    },
    {
      ""name"": ""TrimEnd strips U+0009"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\ta\t""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\ta""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+0009"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\t""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+000A"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\na\n""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+000A"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\na\n""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\n""
    },
    {
      ""name"": ""TrimEnd strips U+000A"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\na\n""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\na""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+000A"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\n""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+000B"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u000ba\u000b""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+000B"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u000ba\u000b""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u000b""
    },
    {
      ""name"": ""TrimEnd strips U+000B"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u000ba\u000b""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u000ba""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+000B"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u000b""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+000C"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\fa\f""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+000C"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\fa\f""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\f""
    },
    {
      ""name"": ""TrimEnd strips U+000C"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\fa\f""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\fa""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+000C"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\f""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+000D"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\ra\r""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+000D"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\ra\r""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\r""
    },
    {
      ""name"": ""TrimEnd strips U+000D"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\ra\r""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\ra""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+000D"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\r""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+0020"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": "" a ""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+0020"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "" a ""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a ""
    },
    {
      ""name"": ""TrimEnd strips U+0020"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "" a ""
                }
              }
            ]
          }
        }
      },
      ""expected"": "" a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+0020"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "" ""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+0085"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u0085a\u0085""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+0085"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u0085a\u0085""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u0085""
    },
    {
      ""name"": ""TrimEnd strips U+0085"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u0085a\u0085""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u0085a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+0085"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u0085""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+00A0"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u00a0a\u00a0""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+00A0"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u00a0a\u00a0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u00a0""
    },
    {
      ""name"": ""TrimEnd strips U+00A0"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u00a0a\u00a0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u00a0a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+00A0"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u00a0""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+1680"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u1680a\u1680""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+1680"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u1680a\u1680""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u1680""
    },
    {
      ""name"": ""TrimEnd strips U+1680"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u1680a\u1680""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u1680a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+1680"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u1680""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+2000"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u2000a\u2000""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+2000"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2000a\u2000""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u2000""
    },
    {
      ""name"": ""TrimEnd strips U+2000"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2000a\u2000""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u2000a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+2000"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2000""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+2001"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u2001a\u2001""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+2001"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2001a\u2001""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u2001""
    },
    {
      ""name"": ""TrimEnd strips U+2001"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2001a\u2001""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u2001a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+2001"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2001""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+2002"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u2002a\u2002""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+2002"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2002a\u2002""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u2002""
    },
    {
      ""name"": ""TrimEnd strips U+2002"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2002a\u2002""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u2002a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+2002"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2002""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+2003"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u2003a\u2003""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+2003"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2003a\u2003""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u2003""
    },
    {
      ""name"": ""TrimEnd strips U+2003"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2003a\u2003""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u2003a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+2003"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2003""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+2004"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u2004a\u2004""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+2004"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2004a\u2004""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u2004""
    },
    {
      ""name"": ""TrimEnd strips U+2004"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2004a\u2004""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u2004a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+2004"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2004""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+2005"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u2005a\u2005""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+2005"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2005a\u2005""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u2005""
    },
    {
      ""name"": ""TrimEnd strips U+2005"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2005a\u2005""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u2005a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+2005"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2005""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+2006"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u2006a\u2006""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+2006"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2006a\u2006""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u2006""
    },
    {
      ""name"": ""TrimEnd strips U+2006"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2006a\u2006""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u2006a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+2006"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2006""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+2007"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u2007a\u2007""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+2007"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2007a\u2007""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u2007""
    },
    {
      ""name"": ""TrimEnd strips U+2007"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2007a\u2007""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u2007a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+2007"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2007""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+2008"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u2008a\u2008""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+2008"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2008a\u2008""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u2008""
    },
    {
      ""name"": ""TrimEnd strips U+2008"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2008a\u2008""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u2008a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+2008"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2008""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+2009"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u2009a\u2009""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+2009"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2009a\u2009""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u2009""
    },
    {
      ""name"": ""TrimEnd strips U+2009"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2009a\u2009""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u2009a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+2009"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2009""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+200A"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u200aa\u200a""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+200A"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u200aa\u200a""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u200a""
    },
    {
      ""name"": ""TrimEnd strips U+200A"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u200aa\u200a""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u200aa""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+200A"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u200a""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+2028"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u2028a\u2028""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+2028"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2028a\u2028""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u2028""
    },
    {
      ""name"": ""TrimEnd strips U+2028"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2028a\u2028""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u2028a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+2028"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2028""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+2029"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u2029a\u2029""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+2029"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2029a\u2029""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u2029""
    },
    {
      ""name"": ""TrimEnd strips U+2029"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2029a\u2029""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u2029a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+2029"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u2029""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+202F"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u202fa\u202f""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+202F"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u202fa\u202f""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u202f""
    },
    {
      ""name"": ""TrimEnd strips U+202F"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u202fa\u202f""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u202fa""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+202F"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u202f""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+205F"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u205fa\u205f""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+205F"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u205fa\u205f""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u205f""
    },
    {
      ""name"": ""TrimEnd strips U+205F"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u205fa\u205f""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u205fa""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+205F"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u205f""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim strips U+3000"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u3000a\u3000""
              }
            }
          }
        }
      },
      ""expected"": ""a""
    },
    {
      ""name"": ""TrimStart strips U+3000"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u3000a\u3000""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""a\u3000""
    },
    {
      ""name"": ""TrimEnd strips U+3000"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u3000a\u3000""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u3000a""
    },
    {
      ""name"": ""IsNullOrWhiteSpace counts U+3000"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u3000""
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Trim keeps U+FEFF"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\ufeffa\ufeff""
              }
            }
          }
        }
      },
      ""expected"": ""\ufeffa\ufeff""
    },
    {
      ""name"": ""TrimStart keeps U+FEFF"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\ufeffa\ufeff""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\ufeffa\ufeff""
    },
    {
      ""name"": ""TrimEnd keeps U+FEFF"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\ufeffa\ufeff""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\ufeffa\ufeff""
    },
    {
      ""name"": ""IsNullOrWhiteSpace rejects U+FEFF"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\ufeff""
                }
              }
            ]
          }
        }
      },
      ""expected"": false
    },
    {
      ""name"": ""Trim keeps U+180E"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u180ea\u180e""
              }
            }
          }
        }
      },
      ""expected"": ""\u180ea\u180e""
    },
    {
      ""name"": ""TrimStart keeps U+180E"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u180ea\u180e""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u180ea\u180e""
    },
    {
      ""name"": ""TrimEnd keeps U+180E"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u180ea\u180e""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u180ea\u180e""
    },
    {
      ""name"": ""IsNullOrWhiteSpace rejects U+180E"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u180e""
                }
              }
            ]
          }
        }
      },
      ""expected"": false
    },
    {
      ""name"": ""Trim keeps U+200B"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""trim"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u200ba\u200b""
              }
            }
          }
        }
      },
      ""expected"": ""\u200ba\u200b""
    },
    {
      ""name"": ""TrimStart keeps U+200B"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimStart"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u200ba\u200b""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u200ba\u200b""
    },
    {
      ""name"": ""TrimEnd keeps U+200B"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""trimEnd"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u200ba\u200b""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""\u200ba\u200b""
    },
    {
      ""name"": ""IsNullOrWhiteSpace rejects U+200B"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u200b""
                }
              }
            ]
          }
        }
      },
      ""expected"": false
    },
    {
      ""name"": ""IsNullOrWhiteSpace of null"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": false
                  },
                  ""value"": null
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""IsNullOrWhiteSpace of empty"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrWhiteSpace"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""IsNullOrEmpty of null"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrEmpty"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": false
                  },
                  ""value"": null
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""IsNullOrEmpty of empty"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrEmpty"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            ]
          }
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""IsNullOrEmpty of whitespace"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""isNullOrEmpty"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "" ""
                }
              }
            ]
          }
        }
      },
      ""expected"": false
    },
    {
      ""name"": ""Join a list in order"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""join"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "", ""
                }
              },
              {
                ""type"": ""listLiteral"",
                ""typeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  }
                },
                ""entries"": [
                  {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""value"": ""b""
                    }
                  },
                  {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""value"": ""a""
                    }
                  },
                  {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""value"": ""c""
                    }
                  }
                ]
              }
            ]
          }
        }
      },
      ""expected"": ""b, a, c""
    },
    {
      ""name"": ""Join an empty list"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""join"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "",""
                }
              },
              {
                ""type"": ""listLiteral"",
                ""typeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  }
                },
                ""entries"": []
              }
            ]
          }
        }
      },
      ""expected"": """"
    },
    {
      ""name"": ""Join null entries as empty"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""join"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "",""
                }
              },
              {
                ""type"": ""listLiteral"",
                ""typeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 3,
                    ""required"": false
                  }
                },
                ""entries"": [
                  {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""value"": ""a""
                    }
                  },
                  {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": false
                      },
                      ""value"": null
                    }
                  },
                  {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""value"": ""b""
                    }
                  }
                ]
              }
            ]
          }
        }
      },
      ""expected"": ""a,,b""
    },
    {
      ""name"": ""Join a set in foreach order"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""join"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""|""
                }
              },
              {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""collectionQuery"",
                  ""info"": {
                    ""collectionPointer"": {
                      ""type"": ""listLiteral"",
                      ""typeInfo"": {
                        ""type"": 6,
                        ""required"": true,
                        ""entryTypeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        }
                      },
                      ""entries"": [
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 3,
                              ""required"": true
                            },
                            ""value"": ""b""
                          }
                        },
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 3,
                              ""required"": true
                            },
                            ""value"": ""a""
                          }
                        },
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 3,
                              ""required"": true
                            },
                            ""value"": ""b""
                          }
                        }
                      ]
                    },
                    ""op"": ""ToSet""
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": ""b|a""
    },
    {
      ""name"": ""Replace is ordinal"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""replace"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""a\r\nb""
              }
            },
            ""argPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\n""
              }
            },
            ""replacementPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""_""
              }
            }
          }
        }
      },
      ""expected"": ""a\r_b""
    },
    {
      ""name"": ""StartsWith is ordinal"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""startsWith"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""\u00e9t\u00e9""
              }
            },
            ""argPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""e\u0301""
              }
            }
          }
        }
      },
      ""expected"": false
    },
    {
      ""name"": ""EndsWith is ordinal"",
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""stringOp"",
          ""info"": {
            ""op"": ""endsWith"",
            ""receiverPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""abc""
              }
            },
            ""argPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""BC""
              }
            }
          }
        }
      },
      ""expected"": false
    },
    {
      ""name"": ""float 2.5 F0 rounds half to even"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""2""
    },
    {
      ""name"": ""float 2.5 N0 rounds half to even"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""2""
    },
    {
      ""name"": ""float 0.125 F2 rounds half to even"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 0.125
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F2""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0.12""
    },
    {
      ""name"": ""decimal 2.125 F2 rounds half away from zero"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""2.125""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F2""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""2.13""
    },
    {
      ""name"": ""decimal -2.125 F2 rounds half away from zero"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""-2.125""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F2""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""-2.13""
    },
    {
      ""name"": ""float 0.125 N2 rounds half to even"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 0.125
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N2""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0.12""
    },
    {
      ""name"": ""decimal 2.125 N2 rounds half away from zero"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""2.125""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N2""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""2.13""
    },
    {
      ""name"": ""decimal -2.125 N2 rounds half away from zero"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""-2.125""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N2""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""-2.13""
    },
    {
      ""name"": ""decimal 2.5 F0 rounds half away from zero"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
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
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""3""
    },
    {
      ""name"": ""decimal 2.5 N0 rounds half away from zero"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
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
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""3""
    },
    {
      ""name"": ""float 2.675 F2 uses the exact binary value"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 2.674999952316284
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F2""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""2.67""
    },
    {
      ""name"": ""float -0.001 F2 drops the minus sign"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": -0.0010000000474974513
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F2""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0.00""
    },
    {
      ""name"": ""decimal -0.001 N2 drops the minus sign"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
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
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N2""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0.00""
    },
    {
      ""name"": ""float 1e30 F0 is not bound by the decimal envelope"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 1.0000000150474662e30
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""1000000015047466219876688855040""
    },
    {
      ""name"": ""float default F precision is 2"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""1.50""
    },
    {
      ""name"": ""lowercase f with leading-zero precision"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""f007""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""1.5000000""
    },
    {
      ""name"": ""float N28 writes every digit"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  },
                  ""value"": 0.10000000149011612
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N28""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0.1000000014901161193847656250""
    },
    {
      ""name"": ""decimal F pads trailing zeros"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""1.5""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F4""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""1.5000""
    },
    {
      ""name"": ""decimal F keeps a 28-digit scale"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""0.0000000000000000000000000001""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F28""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0.0000000000000000000000000001""
    },
    {
      ""name"": ""N groups 3 integer digits"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 123
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""123""
    },
    {
      ""name"": ""N groups 3 negative integer digits"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -123
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N1""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""-123.0""
    },
    {
      ""name"": ""N groups 4 integer digits"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 1234
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""1,234""
    },
    {
      ""name"": ""N groups 4 negative integer digits"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -1234
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N1""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""-1,234.0""
    },
    {
      ""name"": ""N groups 6 integer digits"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 123456
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""123,456""
    },
    {
      ""name"": ""N groups 6 negative integer digits"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -123456
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N1""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""-123,456.0""
    },
    {
      ""name"": ""N groups 7 integer digits"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 1234567
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""1,234,567""
    },
    {
      ""name"": ""N groups 7 negative integer digits"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -1234567
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N1""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""-1,234,567.0""
    },
    {
      ""name"": ""int -2147483648 D"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -2147483648
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""D""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""-2147483648""
    },
    {
      ""name"": ""int -2147483648 D12"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -2147483648
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""D12""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""-002147483648""
    },
    {
      ""name"": ""int -2147483648 F"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -2147483648
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""-2147483648.00""
    },
    {
      ""name"": ""int -2147483648 N3"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -2147483648
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N3""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""-2,147,483,648.000""
    },
    {
      ""name"": ""int 2147483647 D"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 2147483647
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""D""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""2147483647""
    },
    {
      ""name"": ""int 2147483647 D12"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 2147483647
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""D12""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""002147483647""
    },
    {
      ""name"": ""int 2147483647 F"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 2147483647
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""2147483647.00""
    },
    {
      ""name"": ""int 2147483647 N3"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 2147483647
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N3""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""2,147,483,647.000""
    },
    {
      ""name"": ""int D pads with zeros after the sign"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -5
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""D3""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""-005""
    },
    {
      ""name"": ""int d lowercase"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
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
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""d4""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0042""
    },
    {
      ""name"": ""int F0 has no point"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
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
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""42""
    },
    {
      ""name"": ""int zero D0 is 0"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
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
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""D0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0""
    },
    {
      ""name"": ""float NaN F2"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F2""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""NaN""
    },
    {
      ""name"": ""float NaN N0"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""NaN""
    },
    {
      ""name"": ""float Infinity F2"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F2""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""Infinity""
    },
    {
      ""name"": ""float Infinity N0"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""Infinity""
    },
    {
      ""name"": ""float -Infinity F2"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F2""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""-Infinity""
    },
    {
      ""name"": ""float -Infinity N0"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""-Infinity""
    },
    {
      ""name"": ""int format \""\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 1
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.ToString format must not be empty.""
    },
    {
      ""name"": ""int format \""x9z\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 1
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""x9z""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.ToString format must start with D, F, or N.""
    },
    {
      ""name"": ""int format \""P2\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 1
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""P2""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.ToString format must start with D, F, or N.""
    },
    {
      ""name"": ""int format \""F1a\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 1
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F1a""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.ToString format precision must contain only digits.""
    },
    {
      ""name"": ""int format \""F-1\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 1
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F-1""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.ToString format precision must contain only digits.""
    },
    {
      ""name"": ""int format \""F\\u0661\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 1
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F\u0661""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.ToString format precision must contain only digits.""
    },
    {
      ""name"": ""int format \""F999\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 1
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F999""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.ToString format precision must be 0 to 28.""
    },
    {
      ""name"": ""int format \""N29\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 1
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N29""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.ToString format precision must be 0 to 28.""
    },
    {
      ""name"": ""float format \""\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.ToString format must not be empty.""
    },
    {
      ""name"": ""float format \""x9z\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""x9z""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.ToString format must start with F or N.""
    },
    {
      ""name"": ""float format \""P2\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""P2""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.ToString format must start with F or N.""
    },
    {
      ""name"": ""float format \""F1a\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F1a""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.ToString format precision must contain only digits.""
    },
    {
      ""name"": ""float format \""F-1\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F-1""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.ToString format precision must contain only digits.""
    },
    {
      ""name"": ""float format \""F\\u0661\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F\u0661""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.ToString format precision must contain only digits.""
    },
    {
      ""name"": ""float format \""F999\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F999""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.ToString format precision must be 0 to 28.""
    },
    {
      ""name"": ""float format \""N29\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N29""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.ToString format precision must be 0 to 28.""
    },
    {
      ""name"": ""decimal format \""\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""1""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.ToString format must not be empty.""
    },
    {
      ""name"": ""decimal format \""x9z\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""1""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""x9z""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.ToString format must start with F or N.""
    },
    {
      ""name"": ""decimal format \""P2\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""1""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""P2""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.ToString format must start with F or N.""
    },
    {
      ""name"": ""decimal format \""F1a\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""1""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F1a""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.ToString format precision must contain only digits.""
    },
    {
      ""name"": ""decimal format \""F-1\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""1""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F-1""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.ToString format precision must contain only digits.""
    },
    {
      ""name"": ""decimal format \""F\\u0661\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""1""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F\u0661""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.ToString format precision must contain only digits.""
    },
    {
      ""name"": ""decimal format \""F999\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""1""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""F999""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.ToString format precision must be 0 to 28.""
    },
    {
      ""name"": ""decimal format \""N29\"" error"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""1""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""N29""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.ToString format precision must be 0 to 28.""
    },
    {
      ""name"": ""float format \""D1a\"" fails at the letter"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""float"",
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
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""D1a""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.ToString format must start with F or N.""
    },
    {
      ""name"": ""decimal format \""D1a\"" fails at the letter"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  },
                  ""value"": ""1""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""D1a""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.ToString format must start with F or N.""
    },
    {
      ""name"": ""int format \""D1a\"" fails at the digits"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""format"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 1
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""D1a""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.ToString format precision must contain only digits.""
    },
    {
      ""name"": ""int.Parse \""2147483647\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""2147483647""
                }
              }
            ]
          }
        }
      },
      ""expected"": 2147483647
    },
    {
      ""name"": ""int.TryParse \""2147483647\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""2147483647""
                }
              }
            ]
          }
        }
      },
      ""expected"": 2147483647
    },
    {
      ""name"": ""int.Parse \""2147483648\"" fails"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""2147483648""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.Parse input is outside the int range.""
    },
    {
      ""name"": ""int.TryParse \""2147483648\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""2147483648""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""int.Parse \""-2147483648\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-2147483648""
                }
              }
            ]
          }
        }
      },
      ""expected"": -2147483648
    },
    {
      ""name"": ""int.TryParse \""-2147483648\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-2147483648""
                }
              }
            ]
          }
        }
      },
      ""expected"": -2147483648
    },
    {
      ""name"": ""int.Parse \""-2147483649\"" fails"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-2147483649""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.Parse input is outside the int range.""
    },
    {
      ""name"": ""int.TryParse \""-2147483649\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-2147483649""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""int.Parse \""+5\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""+5""
                }
              }
            ]
          }
        }
      },
      ""expected"": 5
    },
    {
      ""name"": ""int.TryParse \""+5\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""+5""
                }
              }
            ]
          }
        }
      },
      ""expected"": 5
    },
    {
      ""name"": ""int.Parse \""-0\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-0""
                }
              }
            ]
          }
        }
      },
      ""expected"": 0
    },
    {
      ""name"": ""int.TryParse \""-0\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-0""
                }
              }
            ]
          }
        }
      },
      ""expected"": 0
    },
    {
      ""name"": ""int.Parse \"" 7 \"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "" 7 ""
                }
              }
            ]
          }
        }
      },
      ""expected"": 7
    },
    {
      ""name"": ""int.TryParse \"" 7 \"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "" 7 ""
                }
              }
            ]
          }
        }
      },
      ""expected"": 7
    },
    {
      ""name"": ""int.Parse \""\\t7\\n\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\t7\n""
                }
              }
            ]
          }
        }
      },
      ""expected"": 7
    },
    {
      ""name"": ""int.TryParse \""\\t7\\n\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\t7\n""
                }
              }
            ]
          }
        }
      },
      ""expected"": 7
    },
    {
      ""name"": ""int.Parse \""\\u00a07\"" fails"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u00a07""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.Parse input is not an integer.""
    },
    {
      ""name"": ""int.TryParse \""\\u00a07\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u00a07""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""int.Parse \""007\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""007""
                }
              }
            ]
          }
        }
      },
      ""expected"": 7
    },
    {
      ""name"": ""int.TryParse \""007\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""007""
                }
              }
            ]
          }
        }
      },
      ""expected"": 7
    },
    {
      ""name"": ""int.Parse \""1e3\"" fails"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1e3""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.Parse input is not an integer.""
    },
    {
      ""name"": ""int.TryParse \""1e3\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1e3""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""int.Parse \""1.0\"" fails"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1.0""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.Parse input is not an integer.""
    },
    {
      ""name"": ""int.TryParse \""1.0\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1.0""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""int.Parse \""\"" fails"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.Parse input is not an integer.""
    },
    {
      ""name"": ""int.TryParse \""\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""int.Parse \""+\"" fails"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""+""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.Parse input is not an integer.""
    },
    {
      ""name"": ""int.TryParse \""+\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""+""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""int.Parse \""1,000\"" fails"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1,000""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.Parse input is not an integer.""
    },
    {
      ""name"": ""int.TryParse \""1,000\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1,000""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""int.Parse \""0x10\"" fails"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""0x10""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.Parse input is not an integer.""
    },
    {
      ""name"": ""int.TryParse \""0x10\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""0x10""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""int.Parse \""\\u0661\\u0662\"" fails"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u0661\u0662""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.Parse input is not an integer.""
    },
    {
      ""name"": ""int.TryParse \""\\u0661\\u0662\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u0661\u0662""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""int.Parse \""99999999999999999999\"" fails"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""99999999999999999999""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""int.Parse input is outside the int range.""
    },
    {
      ""name"": ""int.TryParse \""99999999999999999999\"""",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""int"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""99999999999999999999""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""float.Parse \""1e3\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1e3""
                }
              }
            ]
          }
        }
      },
      ""expected"": 1000.0
    },
    {
      ""name"": ""float.TryParse \""1e3\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1e3""
                }
              }
            ]
          }
        }
      },
      ""expected"": 1000.0
    },
    {
      ""name"": ""float.Parse \"".5\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "".5""
                }
              }
            ]
          }
        }
      },
      ""expected"": 0.5
    },
    {
      ""name"": ""float.TryParse \"".5\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "".5""
                }
              }
            ]
          }
        }
      },
      ""expected"": 0.5
    },
    {
      ""name"": ""float.Parse \""5.\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""5.""
                }
              }
            ]
          }
        }
      },
      ""expected"": 5.0
    },
    {
      ""name"": ""float.TryParse \""5.\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""5.""
                }
              }
            ]
          }
        }
      },
      ""expected"": 5.0
    },
    {
      ""name"": ""float.Parse \""+5\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""+5""
                }
              }
            ]
          }
        }
      },
      ""expected"": 5.0
    },
    {
      ""name"": ""float.TryParse \""+5\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""+5""
                }
              }
            ]
          }
        }
      },
      ""expected"": 5.0
    },
    {
      ""name"": ""float.Parse \""-0\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-0""
                }
              }
            ]
          }
        }
      },
      ""expected"": 0.0
    },
    {
      ""name"": ""float.TryParse \""-0\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-0""
                }
              }
            ]
          }
        }
      },
      ""expected"": 0.0
    },
    {
      ""name"": ""float.Parse \""-0.0e5\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-0.0e5""
                }
              }
            ]
          }
        }
      },
      ""expected"": 0.0
    },
    {
      ""name"": ""float.TryParse \""-0.0e5\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-0.0e5""
                }
              }
            ]
          }
        }
      },
      ""expected"": 0.0
    },
    {
      ""name"": ""float.Parse \""-1e-50\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-1e-50""
                }
              }
            ]
          }
        }
      },
      ""expected"": 0.0
    },
    {
      ""name"": ""float.TryParse \""-1e-50\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-1e-50""
                }
              }
            ]
          }
        }
      },
      ""expected"": 0.0
    },
    {
      ""name"": ""float.Parse \""1e39\"" fails"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1e39""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.Parse input is outside the float range.""
    },
    {
      ""name"": ""float.TryParse \""1e39\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1e39""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""float.Parse \""1e999\"" fails"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1e999""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.Parse input is outside the float range.""
    },
    {
      ""name"": ""float.TryParse \""1e999\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1e999""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""float.Parse \""3.4028235e38\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""3.4028235e38""
                }
              }
            ]
          }
        }
      },
      ""expected"": 3.4028234663852886e38
    },
    {
      ""name"": ""float.TryParse \""3.4028235e38\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""3.4028235e38""
                }
              }
            ]
          }
        }
      },
      ""expected"": 3.4028234663852886e38
    },
    {
      ""name"": ""float.Parse \""1,000\"" fails"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1,000""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.Parse input is not a number.""
    },
    {
      ""name"": ""float.TryParse \""1,000\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1,000""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""float.Parse \""0x10\"" fails"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""0x10""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.Parse input is not a number.""
    },
    {
      ""name"": ""float.TryParse \""0x10\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""0x10""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""float.Parse \""\\u0661\\u0662\"" fails"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u0661\u0662""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.Parse input is not a number.""
    },
    {
      ""name"": ""float.TryParse \""\\u0661\\u0662\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u0661\u0662""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""float.Parse \""NaN\"" fails"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""NaN""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.Parse input is not a number.""
    },
    {
      ""name"": ""float.TryParse \""NaN\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""NaN""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""float.Parse \""Infinity\"" fails"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""Infinity""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.Parse input is not a number.""
    },
    {
      ""name"": ""float.TryParse \""Infinity\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""Infinity""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""float.Parse \"" 2.5 \"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "" 2.5 ""
                }
              }
            ]
          }
        }
      },
      ""expected"": 2.5
    },
    {
      ""name"": ""float.TryParse \"" 2.5 \"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "" 2.5 ""
                }
              }
            ]
          }
        }
      },
      ""expected"": 2.5
    },
    {
      ""name"": ""float.Parse \""\\u00a02.5\"" fails"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u00a02.5""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.Parse input is not a number.""
    },
    {
      ""name"": ""float.TryParse \""\\u00a02.5\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u00a02.5""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""float.Parse \""1e\"" fails"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1e""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.Parse input is not a number.""
    },
    {
      ""name"": ""float.TryParse \""1e\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1e""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""float.Parse \"".\"" fails"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "".""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""float.Parse input is not a number.""
    },
    {
      ""name"": ""float.TryParse \"".\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "".""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""float.Parse \""0.1\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""0.1""
                }
              }
            ]
          }
        }
      },
      ""expected"": 0.10000000149011612
    },
    {
      ""name"": ""float.TryParse \""0.1\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""0.1""
                }
              }
            ]
          }
        }
      },
      ""expected"": 0.10000000149011612
    },
    {
      ""name"": ""float.Parse \""16777217\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""16777217""
                }
              }
            ]
          }
        }
      },
      ""expected"": 16777216.0
    },
    {
      ""name"": ""float.TryParse \""16777217\"""",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""float"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""16777217""
                }
              }
            ]
          }
        }
      },
      ""expected"": 16777216.0
    },
    {
      ""name"": ""decimal.Parse \""1.50\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1.50""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""1.50""
    },
    {
      ""name"": ""decimal.TryParse \""1.50\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1.50""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""1.50""
    },
    {
      ""name"": ""decimal.Parse \""-0\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0""
    },
    {
      ""name"": ""decimal.TryParse \""-0\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-0""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0""
    },
    {
      ""name"": ""decimal.Parse \""-0.00\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-0.00""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0.00""
    },
    {
      ""name"": ""decimal.TryParse \""-0.00\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-0.00""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0.00""
    },
    {
      ""name"": ""decimal.Parse \"".5\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "".5""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0.5""
    },
    {
      ""name"": ""decimal.TryParse \"".5\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "".5""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0.5""
    },
    {
      ""name"": ""decimal.Parse \""5.\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""5.""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""5""
    },
    {
      ""name"": ""decimal.TryParse \""5.\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""5.""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""5""
    },
    {
      ""name"": ""decimal.Parse \""+5\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""+5""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""5""
    },
    {
      ""name"": ""decimal.TryParse \""+5\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""+5""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""5""
    },
    {
      ""name"": ""decimal.Parse \""007.10\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""007.10""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""7.10""
    },
    {
      ""name"": ""decimal.TryParse \""007.10\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""007.10""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""7.10""
    },
    {
      ""name"": ""decimal.Parse \""1e3\"" fails"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1e3""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.Parse input is not a decimal number.""
    },
    {
      ""name"": ""decimal.TryParse \""1e3\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1e3""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""decimal.Parse \"" 3.25 \"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "" 3.25 ""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""3.25""
    },
    {
      ""name"": ""decimal.TryParse \"" 3.25 \"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "" 3.25 ""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""3.25""
    },
    {
      ""name"": ""decimal.Parse \""\\u00a01\"" fails"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u00a01""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.Parse input is not a decimal number.""
    },
    {
      ""name"": ""decimal.TryParse \""\\u00a01\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u00a01""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""decimal.Parse \""1,000\"" fails"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1,000""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.Parse input is not a decimal number.""
    },
    {
      ""name"": ""decimal.TryParse \""1,000\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1,000""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""decimal.Parse \""\\u0661\"" fails"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u0661""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.Parse input is not a decimal number.""
    },
    {
      ""name"": ""decimal.TryParse \""\\u0661\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\u0661""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""decimal.Parse \"".\"" fails"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "".""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.Parse input is not a decimal number.""
    },
    {
      ""name"": ""decimal.TryParse \"".\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": "".""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""decimal.Parse \""\"" fails"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.Parse input is not a decimal number.""
    },
    {
      ""name"": ""decimal.TryParse \""\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""decimal.Parse \""1234567890123456789012345678\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1234567890123456789012345678""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""1234567890123456789012345678""
    },
    {
      ""name"": ""decimal.TryParse \""1234567890123456789012345678\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""1234567890123456789012345678""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""1234567890123456789012345678""
    },
    {
      ""name"": ""decimal.Parse \""12345678901234567890123456789\"" fails"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""12345678901234567890123456789""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.Parse input has more than 28 significant digits.""
    },
    {
      ""name"": ""decimal.TryParse \""12345678901234567890123456789\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""12345678901234567890123456789""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""decimal.Parse \""0.0000000000000000000000000001\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""0.0000000000000000000000000001""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0.0000000000000000000000000001""
    },
    {
      ""name"": ""decimal.TryParse \""0.0000000000000000000000000001\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""0.0000000000000000000000000001""
                }
              }
            ]
          }
        }
      },
      ""expected"": ""0.0000000000000000000000000001""
    },
    {
      ""name"": ""decimal.Parse \""0.00000000000000000000000000001\"" fails"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""0.00000000000000000000000000001""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.Parse input has more than 28 fractional digits.""
    },
    {
      ""name"": ""decimal.TryParse \""0.00000000000000000000000000001\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""0.00000000000000000000000000001""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""decimal.Parse \""-79228162514264337593543950335\"" fails"",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""parse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-79228162514264337593543950335""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""decimal.Parse input has more than 28 significant digits.""
    },
    {
      ""name"": ""decimal.TryParse \""-79228162514264337593543950335\"""",
      ""typeInfo"": {
        ""type"": 20,
        ""required"": false
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""tryParse"",
            ""number"": ""decimal"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""-79228162514264337593543950335""
                }
              }
            ]
          }
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""Substring negative start"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""substring"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""abc""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -1
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""string.Substring startIndex must not be negative.""
    },
    {
      ""name"": ""Substring start past Length"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""substring"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""abc""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 4
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""string.Substring startIndex must not exceed the string length.""
    },
    {
      ""name"": ""Substring negative length"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""substring"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""abc""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 1
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -1
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""string.Substring length must not be negative.""
    },
    {
      ""name"": ""Substring range past Length"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""substring"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""abc""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 2
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 2
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""string.Substring startIndex plus length must not exceed the string length.""
    },
    {
      ""name"": ""PadLeft negative width"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""padLeft"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""a""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -1
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""string.PadLeft totalWidth must not be negative.""
    },
    {
      ""name"": ""PadLeft empty padding char"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""padLeft"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""a""
                }
              },
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
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""string.PadLeft paddingChar must be exactly one character.""
    },
    {
      ""name"": ""PadLeft surrogate-pair padding char"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""padLeft"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""a""
                }
              },
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
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\ud83d\ude00""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""string.PadLeft paddingChar must be exactly one character.""
    },
    {
      ""name"": ""PadRight negative width"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""padRight"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""a""
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": -1
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""string.PadRight totalWidth must not be negative.""
    },
    {
      ""name"": ""PadRight empty padding char"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""padRight"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""a""
                }
              },
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
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""string.PadRight paddingChar must be exactly one character.""
    },
    {
      ""name"": ""PadRight surrogate-pair padding char"",
      ""typeInfo"": {
        ""type"": 3,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""textOp"",
          ""info"": {
            ""op"": ""padRight"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""a""
                }
              },
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
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""\ud83d\ude00""
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""string.PadRight paddingChar must be exactly one character.""
    }
  ]
}";
    }
}
