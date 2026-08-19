// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
//
// VENDORED — DO NOT HAND-EDIT.
// Source of record:
// neo-compose/src/models/neoscript/list-repeat-parity-fixture.json
// P71 section 11. `List.Repeat` must produce entry-for-entry identical lists
// on both runtimes, including the byte-exact negative-count error text, so the
// cases are hand-authored raw IR rather than dumped from either runtime: a
// fixture generated from one side is structurally incapable of catching a
// divergence in the other side's source of truth.
//
// To re-vendor: copy the JSON verbatim and double every `"` for the C#
// verbatim string. Consumed here by NeoScriptListRepeatParityTests and on the
// web side by src/models/neoscript/list-repeat-parity.test.ts.

#nullable enable

namespace NeoCompose.Tests
{
    public static class NeoScriptListRepeatParityFixture
    {
        public const string Json = @"{
  ""$comment"": ""P71 \u00a711 cross-runtime `List.Repeat` parity fixture. Hand-authored raw IR, never generated from either runtime: a fixture generated from one runtime cannot catch a divergence in the other's source of truth. Consumed by src/models/neoscript/list-repeat-parity.test.ts (web) and NeoScriptListRepeatParityTests (neo-compose-dotnet, vendored verbatim copy). Self-contained: every case is literals and arithmetic, so no project document, schema record, or host state is required to run it."",
  ""$evaluateComment"": ""Each case's `pointer` is the compiled IR both runtimes receive: consumers wrap it in a getter with no parameters and a single `return` instruction whose result type is the case's `typeInfo` \u2014 `List<T>` where `T` is the post-join entry type the resolver inferred (P71 \u00a72), the same `entryTypeInfo` the function info carries. Exactly one expectation is present per case. `expectedList` is the produced list compared entry-for-entry, numerically as a double for int/float entry types. `expectedError` must match the thrown evaluator error byte-for-byte. Once-evaluation of the value pointer and shared-reference entry semantics (\u00a73) are not expressible in raw IR \u2014 there is no counter a pure pointer can increment \u2014 so each runtime pins those in its own evaluator unit tests, as does budget exhaustion, whose limits are host configuration rather than fixture data."",
  ""evaluateCases"": [
    {
      ""name"": ""Zero count produces an empty list"",
      ""typeInfo"": {
        ""type"": 6,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""listRepeat"",
          ""info"": {
            ""valuePointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 7
              }
            },
            ""countPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 0
              }
            },
            ""entryTypeInfo"": {
              ""type"": 2,
              ""required"": true
            }
          }
        }
      },
      ""expectedList"": []
    },
    {
      ""name"": ""Positive count repeats the int value"",
      ""typeInfo"": {
        ""type"": 6,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""listRepeat"",
          ""info"": {
            ""valuePointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 7
              }
            },
            ""countPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 3
              }
            },
            ""entryTypeInfo"": {
              ""type"": 2,
              ""required"": true
            }
          }
        }
      },
      ""expectedList"": [7, 7, 7]
    },
    {
      ""name"": ""A count of one produces a single entry"",
      ""typeInfo"": {
        ""type"": 6,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""listRepeat"",
          ""info"": {
            ""valuePointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": -12
              }
            },
            ""countPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 1
              }
            },
            ""entryTypeInfo"": {
              ""type"": 2,
              ""required"": true
            }
          }
        }
      },
      ""expectedList"": [-12]
    },
    {
      ""name"": ""An int value in a float context widens to the joined entry type"",
      ""typeInfo"": {
        ""type"": 6,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 4,
          ""required"": true
        }
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""listRepeat"",
          ""info"": {
            ""valuePointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 0
              }
            },
            ""countPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 4
              }
            },
            ""entryTypeInfo"": {
              ""type"": 4,
              ""required"": true
            }
          }
        }
      },
      ""expectedList"": [0, 0, 0, 0]
    },
    {
      ""name"": ""A float value keeps its exact double bits"",
      ""typeInfo"": {
        ""type"": 6,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 4,
          ""required"": true
        }
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""listRepeat"",
          ""info"": {
            ""valuePointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 4,
                  ""required"": true
                },
                ""value"": 2.5
              }
            },
            ""countPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 3
              }
            },
            ""entryTypeInfo"": {
              ""type"": 4,
              ""required"": true
            }
          }
        }
      },
      ""expectedList"": [2.5, 2.5, 2.5]
    },
    {
      ""name"": ""String entries repeat the same text"",
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
          ""type"": ""listRepeat"",
          ""info"": {
            ""valuePointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""empty""
              }
            },
            ""countPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 2
              }
            },
            ""entryTypeInfo"": {
              ""type"": 3,
              ""required"": true
            }
          }
        }
      },
      ""expectedList"": [""empty"", ""empty""]
    },
    {
      ""name"": ""An empty string is a value like any other"",
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
          ""type"": ""listRepeat"",
          ""info"": {
            ""valuePointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": """"
              }
            },
            ""countPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 2
              }
            },
            ""entryTypeInfo"": {
              ""type"": 3,
              ""required"": true
            }
          }
        }
      },
      ""expectedList"": ["""", """"]
    },
    {
      ""name"": ""Bool entries repeat"",
      ""typeInfo"": {
        ""type"": 6,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 1,
          ""required"": true
        }
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""listRepeat"",
          ""info"": {
            ""valuePointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 1,
                  ""required"": true
                },
                ""value"": true
              }
            },
            ""countPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 2
              }
            },
            ""entryTypeInfo"": {
              ""type"": 1,
              ""required"": true
            }
          }
        }
      },
      ""expectedList"": [true, true]
    },
    {
      ""name"": ""The count is an evaluated expression, not a constant"",
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
          ""type"": ""listRepeat"",
          ""info"": {
            ""valuePointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""slot""
              }
            },
            ""countPointer"": {
              ""type"": ""operation"",
              ""operation"": {
                ""type"": ""arithmetic"",
                ""arithmetic"": {
                  ""type"": ""+"",
                  ""pointers"": [
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
                        ""value"": 3
                      }
                    }
                  ]
                }
              }
            },
            ""entryTypeInfo"": {
              ""type"": 3,
              ""required"": true
            }
          }
        }
      },
      ""expectedList"": [""slot"", ""slot"", ""slot"", ""slot"", ""slot""]
    },
    {
      ""name"": ""An expression evaluating to zero still produces an empty list"",
      ""typeInfo"": {
        ""type"": 6,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""listRepeat"",
          ""info"": {
            ""valuePointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 9
              }
            },
            ""countPointer"": {
              ""type"": ""operation"",
              ""operation"": {
                ""type"": ""arithmetic"",
                ""arithmetic"": {
                  ""type"": ""-"",
                  ""pointers"": [
                    {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 4
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
            ""entryTypeInfo"": {
              ""type"": 2,
              ""required"": true
            }
          }
        }
      },
      ""expectedList"": []
    },
    {
      ""name"": ""A negative count is a runtime error"",
      ""typeInfo"": {
        ""type"": 6,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""listRepeat"",
          ""info"": {
            ""valuePointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 7
              }
            },
            ""countPointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": -1
              }
            },
            ""entryTypeInfo"": {
              ""type"": 2,
              ""required"": true
            }
          }
        }
      },
      ""expectedError"": ""List.Repeat count must be non-negative; got -1.""
    },
    {
      ""name"": ""A computed negative count reports the computed value"",
      ""typeInfo"": {
        ""type"": 6,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""listRepeat"",
          ""info"": {
            ""valuePointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 7
              }
            },
            ""countPointer"": {
              ""type"": ""operation"",
              ""operation"": {
                ""type"": ""arithmetic"",
                ""arithmetic"": {
                  ""type"": ""-"",
                  ""pointers"": [
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
                        ""value"": 4
                      }
                    }
                  ]
                }
              }
            },
            ""entryTypeInfo"": {
              ""type"": 2,
              ""required"": true
            }
          }
        }
      },
      ""expectedError"": ""List.Repeat count must be non-negative; got -3.""
    }
  ]
}";
    }
}
