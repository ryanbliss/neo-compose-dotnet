// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
//
// VENDORED — DO NOT HAND-EDIT.
// Source of record:
// neo-compose/src/models/neoscript/neoscript-control-flow-parity-fixture.json
// P50-P52 raw-IR parity fixture. The web and Unity evaluators consume the
// identical authored instruction stream so a discriminator, execution-order,
// collection-snapshot, or control-transfer disagreement cannot be hidden by
// either runtime compiler.
//
// To re-vendor: copy the JSON verbatim and double every `"` for the C#
// verbatim string. Consumed here by NeoScriptControlFlowParityTests and on
// the web by src/models/neoscript/neoscript-control-flow-parity.test.ts.

#nullable enable

namespace NeoCompose.Tests
{
    public static class NeoScriptControlFlowParityFixture
    {
        public const string Json = @"{
  ""$comment"": ""P50-P52 cross-runtime NeoScript control-flow parity fixture with 13 P50, 13 P51, and 11 P52 cases. Hand-authored raw IR is consumed directly by the web test and must be vendored verbatim into the neo-compose-dotnet Unity tests; do not regenerate it from either compiler/runtime. P50 cases pin ordered loop control, outer-local updates, collection snapshot membership, JavaScript dictionary-value order (including numeric-like keys), empty, derived, and Lookup-contract collections, receiver evaluation count, throw/return propagation, and the shared 10,000-iteration budget. P51 cases additionally pin optional-int null normalization and switch/loop nesting transfer ownership. P52 cases additionally pin try-inside-switch recovery. Persisted write-intent needs project records and an effect-capable harness, so it is deliberately covered by compiler and runtime-specific tests rather than this getter-only empty-project fixture."",
  ""evaluateCases"": [
    {
      ""name"": ""for consumes continue and break while updating an outer local"",
      ""getter"": {
        ""compilerRevision"": 4,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""sum"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 0
                }
              }
            }
          },
          {
            ""type"": ""for"",
            ""initializer"": {
              ""id"": ""i"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
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
            ""condition"": {
              ""condition"": {
                ""type"": ""lessThan"",
                ""operand1"": {
                  ""type"": ""variable"",
                  ""variableId"": ""i""
                },
                ""operand2"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 6
                  }
                }
              }
            },
            ""iterator"": {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""i""
                },
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""writability"": ""local""
              },
              ""operator"": ""++"",
              ""pointer"": {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""+"",
                    ""pointers"": [
                      {
                        ""type"": ""variable"",
                        ""variableId"": ""i""
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
            },
            ""instructions"": [
              {
                ""type"": ""if"",
                ""branches"": [
                  {
                    ""expression"": {
                      ""condition"": {
                        ""type"": ""equalTo"",
                        ""operand1"": {
                          ""type"": ""variable"",
                          ""variableId"": ""i""
                        },
                        ""operand2"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 1
                          }
                        }
                      }
                    },
                    ""instructions"": [
                      {
                        ""type"": ""continue""
                      }
                    ]
                  }
                ]
              },
              {
                ""type"": ""if"",
                ""branches"": [
                  {
                    ""expression"": {
                      ""condition"": {
                        ""type"": ""equalTo"",
                        ""operand1"": {
                          ""type"": ""variable"",
                          ""variableId"": ""i""
                        },
                        ""operand2"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 4
                          }
                        }
                      }
                    },
                    ""instructions"": [
                      {
                        ""type"": ""break""
                      }
                    ]
                  }
                ]
              },
              {
                ""type"": ""assign"",
                ""target"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""sum""
                  },
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""writability"": ""local""
                },
                ""operator"": ""+="",
                ""pointer"": {
                  ""type"": ""operation"",
                  ""operation"": {
                    ""type"": ""arithmetic"",
                    ""arithmetic"": {
                      ""type"": ""+"",
                      ""pointers"": [
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""sum""
                        },
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""i""
                        }
                      ]
                    }
                  }
                }
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""sum""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 5
    },
    {
      ""name"": ""reverse for loop executes its decrement iterator"",
      ""getter"": {
        ""compilerRevision"": 4,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""digits"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 0
                }
              }
            }
          },
          {
            ""type"": ""for"",
            ""initializer"": {
              ""id"": ""i"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 3
                }
              }
            },
            ""condition"": {
              ""condition"": {
                ""type"": ""greaterThanOrEqualTo"",
                ""operand1"": {
                  ""type"": ""variable"",
                  ""variableId"": ""i""
                },
                ""operand2"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 1
                  }
                }
              }
            },
            ""iterator"": {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""i""
                },
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""writability"": ""local""
              },
              ""operator"": ""--"",
              ""pointer"": {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""-"",
                    ""pointers"": [
                      {
                        ""type"": ""variable"",
                        ""variableId"": ""i""
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
            },
            ""instructions"": [
              {
                ""type"": ""assign"",
                ""target"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""digits""
                  },
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""writability"": ""local""
                },
                ""operator"": ""="",
                ""pointer"": {
                  ""type"": ""operation"",
                  ""operation"": {
                    ""type"": ""arithmetic"",
                    ""arithmetic"": {
                      ""type"": ""+"",
                      ""pointers"": [
                        {
                          ""type"": ""operation"",
                          ""operation"": {
                            ""type"": ""arithmetic"",
                            ""arithmetic"": {
                              ""type"": ""*"",
                              ""pointers"": [
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""digits""
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
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""i""
                        }
                      ]
                    }
                  }
                }
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""digits""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 321
    },
    {
      ""name"": ""false initial condition consumes no iteration"",
      ""getter"": {
        ""compilerRevision"": 4,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""answer"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 7
                }
              }
            }
          },
          {
            ""type"": ""for"",
            ""initializer"": {
              ""id"": ""i"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
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
            ""condition"": {
              ""condition"": {
                ""type"": ""lessThan"",
                ""operand1"": {
                  ""type"": ""variable"",
                  ""variableId"": ""i""
                },
                ""operand2"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 0
                  }
                }
              }
            },
            ""iterator"": {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""i""
                },
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""writability"": ""local""
              },
              ""operator"": ""++"",
              ""pointer"": {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""+"",
                    ""pointers"": [
                      {
                        ""type"": ""variable"",
                        ""variableId"": ""i""
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
            },
            ""instructions"": [
              {
                ""type"": ""assign"",
                ""target"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""answer""
                  },
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""writability"": ""local""
                },
                ""operator"": ""="",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 99
                  }
                }
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""answer""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 7
    },
    {
      ""name"": ""foreach snapshots list membership and preserves order under remove"",
      ""getter"": {
        ""compilerRevision"": 4,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""items"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""listLiteral"",
                ""typeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                ""entries"": [
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
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""seen"",
              ""typeInfo"": {
                ""type"": 3,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            }
          },
          {
            ""type"": ""forEach"",
            ""binding"": {
              ""id"": ""item"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""readonly"": true,
              ""writability"": ""local""
            },
            ""collectionPointer"": {
              ""type"": ""variable"",
              ""variableId"": ""items""
            },
            ""collectionTypeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""instructions"": [
              {
                ""type"": ""assign"",
                ""target"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""seen""
                  },
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""writability"": ""local""
                },
                ""operator"": ""+="",
                ""pointer"": {
                  ""type"": ""operation"",
                  ""operation"": {
                    ""type"": ""arithmetic"",
                    ""arithmetic"": {
                      ""type"": ""+"",
                      ""pointers"": [
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""seen""
                        },
                        {
                          ""type"": ""stringify"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""item""
                          },
                          ""sourceType"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        }
                      ]
                    }
                  }
                }
              },
              {
                ""type"": ""collectionCall"",
                ""target"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""items""
                  },
                  ""typeInfo"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  ""writability"": ""local""
                },
                ""mutation"": ""Remove"",
                ""args"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""item""
                  }
                ]
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""seen""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expected"": ""123""
    },
    {
      ""name"": ""foreach dictionary binds values in collection order"",
      ""getter"": {
        ""compilerRevision"": 4,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""items"",
              ""typeInfo"": {
                ""type"": 5,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""dictLiteral"",
                ""typeInfo"": {
                  ""type"": 5,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                ""entries"": [
                  {
                    ""key"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""second""
                      }
                    },
                    ""value"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 2
                      }
                    }
                  },
                  {
                    ""key"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""first""
                      }
                    },
                    ""value"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 1
                      }
                    }
                  }
                ]
              }
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""seen"",
              ""typeInfo"": {
                ""type"": 3,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            }
          },
          {
            ""type"": ""forEach"",
            ""binding"": {
              ""id"": ""item"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""readonly"": true,
              ""writability"": ""local""
            },
            ""collectionPointer"": {
              ""type"": ""variable"",
              ""variableId"": ""items""
            },
            ""collectionTypeInfo"": {
              ""type"": 5,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""instructions"": [
              {
                ""type"": ""assign"",
                ""target"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""seen""
                  },
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""writability"": ""local""
                },
                ""operator"": ""+="",
                ""pointer"": {
                  ""type"": ""operation"",
                  ""operation"": {
                    ""type"": ""arithmetic"",
                    ""arithmetic"": {
                      ""type"": ""+"",
                      ""pointers"": [
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""seen""
                        },
                        {
                          ""type"": ""stringify"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""item""
                          },
                          ""sourceType"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        }
                      ]
                    }
                  }
                }
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""seen""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expected"": ""21""
    },
    {
      ""name"": ""return escapes nested loops"",
      ""getter"": {
        ""compilerRevision"": 4,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""for"",
            ""initializer"": {
              ""id"": ""i"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
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
            ""condition"": {
              ""condition"": {
                ""type"": ""lessThan"",
                ""operand1"": {
                  ""type"": ""variable"",
                  ""variableId"": ""i""
                },
                ""operand2"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 2
                  }
                }
              }
            },
            ""iterator"": {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""i""
                },
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""writability"": ""local""
              },
              ""operator"": ""++"",
              ""pointer"": {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""+"",
                    ""pointers"": [
                      {
                        ""type"": ""variable"",
                        ""variableId"": ""i""
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
            },
            ""instructions"": [
              {
                ""type"": ""forEach"",
                ""binding"": {
                  ""id"": ""item"",
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""readonly"": true,
                  ""writability"": ""local""
                },
                ""collectionPointer"": {
                  ""type"": ""listLiteral"",
                  ""typeInfo"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  ""entries"": [
                    {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 8
                      }
                    }
                  ]
                },
                ""collectionTypeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""item""
                    }
                  }
                ]
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 0
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 8
    },
    {
      ""name"": ""nested loops share the top-level iteration budget"",
      ""getter"": {
        ""compilerRevision"": 4,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""for"",
            ""initializer"": {
              ""id"": ""outer"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
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
            ""condition"": {
              ""condition"": {
                ""type"": ""lessThan"",
                ""operand1"": {
                  ""type"": ""variable"",
                  ""variableId"": ""outer""
                },
                ""operand2"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 101
                  }
                }
              }
            },
            ""iterator"": {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""outer""
                },
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""writability"": ""local""
              },
              ""operator"": ""++"",
              ""pointer"": {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""+"",
                    ""pointers"": [
                      {
                        ""type"": ""variable"",
                        ""variableId"": ""outer""
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
            },
            ""instructions"": [
              {
                ""type"": ""for"",
                ""initializer"": {
                  ""id"": ""inner"",
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""pointer"": {
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
                ""condition"": {
                  ""condition"": {
                    ""type"": ""lessThan"",
                    ""operand1"": {
                      ""type"": ""variable"",
                      ""variableId"": ""inner""
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 100
                      }
                    }
                  }
                },
                ""iterator"": {
                  ""type"": ""assign"",
                  ""target"": {
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""inner""
                    },
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""writability"": ""local""
                  },
                  ""operator"": ""++"",
                  ""pointer"": {
                    ""type"": ""operation"",
                    ""operation"": {
                      ""type"": ""arithmetic"",
                      ""arithmetic"": {
                        ""type"": ""+"",
                        ""pointers"": [
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""inner""
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
                },
                ""instructions"": []
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 0
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expectedError"": ""NeoScript loop iteration limit of 10000 exceeded.""
    },
    {
      ""name"": ""foreach dictionary uses JavaScript Object.values order for numeric-like keys"",
      ""getter"": {
        ""compilerRevision"": 4,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""seen"",
              ""typeInfo"": {
                ""type"": 3,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            }
          },
          {
            ""type"": ""forEach"",
            ""binding"": {
              ""id"": ""item"",
              ""typeInfo"": {
                ""type"": 3,
                ""required"": true
              },
              ""readonly"": true,
              ""writability"": ""local""
            },
            ""collectionPointer"": {
              ""type"": ""dictLiteral"",
              ""typeInfo"": {
                ""type"": 5,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 3,
                  ""required"": true
                }
              },
              ""entries"": [
                {
                  ""key"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""value"": ""10""
                    }
                  },
                  ""value"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""value"": ""10""
                    }
                  }
                },
                {
                  ""key"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""value"": ""2""
                    }
                  },
                  ""value"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""value"": ""2""
                    }
                  }
                },
                {
                  ""key"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""value"": ""a""
                    }
                  },
                  ""value"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""value"": ""a""
                    }
                  }
                }
              ]
            },
            ""collectionTypeInfo"": {
              ""type"": 5,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 3,
                ""required"": true
              }
            },
            ""instructions"": [
              {
                ""type"": ""assign"",
                ""target"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""seen""
                  },
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""writability"": ""local""
                },
                ""operator"": ""+="",
                ""pointer"": {
                  ""type"": ""operation"",
                  ""operation"": {
                    ""type"": ""arithmetic"",
                    ""arithmetic"": {
                      ""type"": ""+"",
                      ""pointers"": [
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""seen""
                        },
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""item""
                        }
                      ]
                    }
                  }
                }
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""seen""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expected"": ""210a""
    },
    {
      ""name"": ""foreach over an empty collection consumes no iteration"",
      ""getter"": {
        ""compilerRevision"": 4,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""forEach"",
            ""binding"": {
              ""id"": ""item"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""readonly"": true,
              ""writability"": ""local""
            },
            ""collectionPointer"": {
              ""type"": ""listLiteral"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""entries"": []
            },
            ""collectionTypeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""instructions"": [
              {
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 1
                  }
                }
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 0
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 0
    },
    {
      ""name"": ""foreach iterates a hand-authored derived Where collection"",
      ""getter"": {
        ""compilerRevision"": 4,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""sum"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 0
                }
              }
            }
          },
          {
            ""type"": ""forEach"",
            ""binding"": {
              ""id"": ""item"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""readonly"": true,
              ""writability"": ""local""
            },
            ""collectionPointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""where"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    },
                    ""entries"": [
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
                  },
                  ""function"": {
                    ""compilerRevision"": 4,
                    ""parameters"": [
                      {
                        ""id"": ""value"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 0
                          }
                        }
                      }
                    ],
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""operation"",
                          ""operation"": {
                            ""type"": ""boolean"",
                            ""expression"": {
                              ""condition"": {
                                ""type"": ""greaterThan"",
                                ""operand1"": {
                                  ""type"": ""variable"",
                                  ""variableId"": ""value""
                                },
                                ""operand2"": {
                                  ""type"": ""value"",
                                  ""value"": {
                                    ""typeInfo"": {
                                      ""type"": 2,
                                      ""required"": true
                                    },
                                    ""value"": 1
                                  }
                                }
                              }
                            }
                          }
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 1,
                      ""required"": true
                    }
                  }
                }
              }
            },
            ""collectionTypeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""readOnly"": true
            },
            ""instructions"": [
              {
                ""type"": ""assign"",
                ""target"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""sum""
                  },
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""writability"": ""local""
                },
                ""operator"": ""+="",
                ""pointer"": {
                  ""type"": ""operation"",
                  ""operation"": {
                    ""type"": ""arithmetic"",
                    ""arithmetic"": {
                      ""type"": ""+"",
                      ""pointers"": [
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""sum""
                        },
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""item""
                        }
                      ]
                    }
                  }
                }
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""sum""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 5
    },
    {
      ""name"": ""foreach evaluates its derived collection receiver exactly once"",
      ""getter"": {
        ""compilerRevision"": 4,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""sum"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 0
                }
              }
            }
          },
          {
            ""type"": ""forEach"",
            ""binding"": {
              ""id"": ""item"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""readonly"": true,
              ""writability"": ""local""
            },
            ""collectionPointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""select"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    },
                    ""entries"": [
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
                  },
                  ""function"": {
                    ""compilerRevision"": 4,
                    ""parameters"": [
                      {
                        ""id"": ""value"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 0
                          }
                        }
                      }
                    ],
                    ""instructions"": [
                      {
                        ""type"": ""for"",
                        ""initializer"": {
                          ""id"": ""i"",
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""pointer"": {
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
                        ""condition"": {
                          ""condition"": {
                            ""type"": ""lessThan"",
                            ""operand1"": {
                              ""type"": ""variable"",
                              ""variableId"": ""i""
                            },
                            ""operand2"": {
                              ""type"": ""value"",
                              ""value"": {
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                },
                                ""value"": 9999
                              }
                            }
                          }
                        },
                        ""iterator"": {
                          ""type"": ""assign"",
                          ""target"": {
                            ""pointer"": {
                              ""type"": ""variable"",
                              ""variableId"": ""i""
                            },
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""writability"": ""local""
                          },
                          ""operator"": ""++"",
                          ""pointer"": {
                            ""type"": ""operation"",
                            ""operation"": {
                              ""type"": ""arithmetic"",
                              ""arithmetic"": {
                                ""type"": ""+"",
                                ""pointers"": [
                                  {
                                    ""type"": ""variable"",
                                    ""variableId"": ""i""
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
                        },
                        ""instructions"": []
                      },
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""value""
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                }
              }
            },
            ""collectionTypeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""readOnly"": true
            },
            ""instructions"": [
              {
                ""type"": ""assign"",
                ""target"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""sum""
                  },
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""writability"": ""local""
                },
                ""operator"": ""+="",
                ""pointer"": {
                  ""type"": ""operation"",
                  ""operation"": {
                    ""type"": ""arithmetic"",
                    ""arithmetic"": {
                      ""type"": ""+"",
                      ""pointers"": [
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""sum""
                        },
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""item""
                        }
                      ]
                    }
                  }
                }
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""sum""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 1
    },
    {
      ""name"": ""throw escapes a foreach body without visiting later entries"",
      ""getter"": {
        ""compilerRevision"": 4,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""forEach"",
            ""binding"": {
              ""id"": ""item"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""readonly"": true,
              ""writability"": ""local""
            },
            ""collectionPointer"": {
              ""type"": ""listLiteral"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""entries"": [
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
            },
            ""collectionTypeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""instructions"": [
              {
                ""type"": ""throw"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""loop failure""
                  }
                }
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 0
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expectedError"": ""loop failure""
    },
    {
      ""name"": ""foreach consumes ordered values through the Lookup collection contract"",
      ""getter"": {
        ""compilerRevision"": 4,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""seen"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 0
                }
              }
            }
          },
          {
            ""type"": ""forEach"",
            ""binding"": {
              ""id"": ""item"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""readonly"": true,
              ""writability"": ""local""
            },
            ""collectionPointer"": {
              ""type"": ""listLiteral"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""entries"": [
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
                    ""value"": 2
                  }
                }
              ]
            },
            ""collectionTypeInfo"": {
              ""type"": 9,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""collectionMemberId"": ""fixture-items"",
              ""collectionValueId"": null
            },
            ""instructions"": [
              {
                ""type"": ""assign"",
                ""target"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""seen""
                  },
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""writability"": ""local""
                },
                ""operator"": ""="",
                ""pointer"": {
                  ""type"": ""operation"",
                  ""operation"": {
                    ""type"": ""arithmetic"",
                    ""arithmetic"": {
                      ""type"": ""+"",
                      ""pointers"": [
                        {
                          ""type"": ""operation"",
                          ""operation"": {
                            ""type"": ""arithmetic"",
                            ""arithmetic"": {
                              ""type"": ""+"",
                              ""pointers"": [
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""seen""
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
                        {
                          ""type"": ""operation"",
                          ""operation"": {
                            ""type"": ""arithmetic"",
                            ""arithmetic"": {
                              ""type"": ""+"",
                              ""pointers"": [
                                {
                                  ""type"": ""operation"",
                                  ""operation"": {
                                    ""type"": ""arithmetic"",
                                    ""arithmetic"": {
                                      ""type"": ""*"",
                                      ""pointers"": [
                                        {
                                          ""type"": ""variable"",
                                          ""variableId"": ""seen""
                                        },
                                        {
                                          ""type"": ""value"",
                                          ""value"": {
                                            ""typeInfo"": {
                                              ""type"": 2,
                                              ""required"": true
                                            },
                                            ""value"": 9
                                          }
                                        }
                                      ]
                                    }
                                  }
                                },
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""item""
                                }
                              ]
                            }
                          }
                        }
                      ]
                    }
                  }
                }
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""seen""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 42
    },
    {
      ""name"": ""switch matches an int stacked label and only the selected section writes"",
      ""getter"": {
        ""compilerRevision"": 5,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""result"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 0
                }
              }
            }
          },
          {
            ""type"": ""switch"",
            ""selector"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 2
              }
            },
            ""selectorTypeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""sections"": [
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 1
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""assign"",
                    ""target"": {
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""result""
                      },
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""writability"": ""local""
                    },
                    ""operator"": ""="",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 10
                      }
                    }
                  },
                  {
                    ""type"": ""break""
                  }
                ]
              },
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 2
                  },
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 3
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""assign"",
                    ""target"": {
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""result""
                      },
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""writability"": ""local""
                    },
                    ""operator"": ""="",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 20
                      }
                    }
                  },
                  {
                    ""type"": ""break""
                  }
                ]
              }
            ],
            ""defaultInstructions"": [
              {
                ""type"": ""assign"",
                ""target"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""result""
                  },
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""writability"": ""local""
                },
                ""operator"": ""="",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 30
                  }
                }
              },
              {
                ""type"": ""break""
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""result""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 20
    },
    {
      ""name"": ""switch matches a string label and propagates return"",
      ""getter"": {
        ""compilerRevision"": 5,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""switch"",
            ""selector"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""two""
              }
            },
            ""selectorTypeInfo"": {
              ""type"": 3,
              ""required"": true
            },
            ""sections"": [
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""one""
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""wrong""
                      }
                    }
                  }
                ]
              },
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""two""
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""matched""
                      }
                    }
                  }
                ]
              }
            ],
            ""defaultInstructions"": [
              {
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""default""
                  }
                }
              }
            ]
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expected"": ""matched""
    },
    {
      ""name"": ""switch matches a bool label"",
      ""getter"": {
        ""compilerRevision"": 5,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""switch"",
            ""selector"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 1,
                  ""required"": true
                },
                ""value"": true
              }
            },
            ""selectorTypeInfo"": {
              ""type"": 1,
              ""required"": true
            },
            ""sections"": [
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 1,
                      ""required"": true
                    },
                    ""value"": false
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 0
                      }
                    }
                  }
                ]
              },
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 1,
                      ""required"": true
                    },
                    ""value"": true
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 1
                      }
                    }
                  }
                ]
              }
            ],
            ""defaultInstructions"": [
              {
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 2
                  }
                }
              }
            ]
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 1
    },
    {
      ""name"": ""switch matches an enum label by normalized option"",
      ""getter"": {
        ""compilerRevision"": 5,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""switch"",
            ""selector"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 8,
                  ""required"": true,
                  ""enumId"": ""direction""
                },
                ""value"": [""east""]
              }
            },
            ""selectorTypeInfo"": {
              ""type"": 8,
              ""required"": true,
              ""enumId"": ""direction""
            },
            ""sections"": [
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 8,
                      ""required"": true,
                      ""enumId"": ""direction""
                    },
                    ""value"": [""west""]
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""west""
                      }
                    }
                  }
                ]
              },
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 8,
                      ""required"": true,
                      ""enumId"": ""direction""
                    },
                    ""value"": [""east""]
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""east""
                      }
                    }
                  }
                ]
              }
            ],
            ""defaultInstructions"": [
              {
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""unknown""
                  }
                }
              }
            ]
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expected"": ""east""
    },
    {
      ""name"": ""switch matches null for an optional selector"",
      ""getter"": {
        ""compilerRevision"": 5,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""switch"",
            ""selector"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": false
                },
                ""value"": null
              }
            },
            ""selectorTypeInfo"": {
              ""type"": 3,
              ""required"": false
            },
            ""sections"": [
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 0,
                      ""required"": true
                    },
                    ""value"": null
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""null""
                      }
                    }
                  }
                ]
              },
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""value""
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""value""
                      }
                    }
                  }
                ]
              }
            ],
            ""defaultInstructions"": [
              {
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""default""
                  }
                }
              }
            ]
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expected"": ""null""
    },
    {
      ""name"": ""switch matches null for an optional int selector"",
      ""getter"": {
        ""compilerRevision"": 5,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""switch"",
            ""selector"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": false
                },
                ""value"": null
              }
            },
            ""selectorTypeInfo"": {
              ""type"": 2,
              ""required"": false
            },
            ""sections"": [
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 0,
                      ""required"": true
                    },
                    ""value"": null
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""null""
                      }
                    }
                  }
                ]
              },
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 1
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""value""
                      }
                    }
                  }
                ]
              }
            ],
            ""defaultInstructions"": [
              {
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""default""
                  }
                }
              }
            ]
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expected"": ""null""
    },
    {
      ""name"": ""switch runs default when no case matches"",
      ""getter"": {
        ""compilerRevision"": 5,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""switch"",
            ""selector"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""other""
              }
            },
            ""selectorTypeInfo"": {
              ""type"": 3,
              ""required"": true
            },
            ""sections"": [
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""known""
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""case""
                      }
                    }
                  }
                ]
              }
            ],
            ""defaultInstructions"": [
              {
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""default""
                  }
                }
              }
            ]
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expected"": ""default""
    },
    {
      ""name"": ""switch without default falls through when no case matches"",
      ""getter"": {
        ""compilerRevision"": 5,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""result"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 7
                }
              }
            }
          },
          {
            ""type"": ""switch"",
            ""selector"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""other""
              }
            },
            ""selectorTypeInfo"": {
              ""type"": 3,
              ""required"": true
            },
            ""sections"": [
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""known""
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""assign"",
                    ""target"": {
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""result""
                      },
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""writability"": ""local""
                    },
                    ""operator"": ""="",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 99
                      }
                    }
                  },
                  {
                    ""type"": ""break""
                  }
                ]
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""result""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 7
    },
    {
      ""name"": ""switch consumes break and propagates continue to its enclosing for loop"",
      ""getter"": {
        ""compilerRevision"": 5,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""sum"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 0
                }
              }
            }
          },
          {
            ""type"": ""for"",
            ""initializer"": {
              ""id"": ""i"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
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
            ""condition"": {
              ""condition"": {
                ""type"": ""lessThan"",
                ""operand1"": {
                  ""type"": ""variable"",
                  ""variableId"": ""i""
                },
                ""operand2"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 4
                  }
                }
              }
            },
            ""iterator"": {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""i""
                },
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""writability"": ""local""
              },
              ""operator"": ""++"",
              ""pointer"": {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""+"",
                    ""pointers"": [
                      {
                        ""type"": ""variable"",
                        ""variableId"": ""i""
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
            },
            ""instructions"": [
              {
                ""type"": ""switch"",
                ""selector"": {
                  ""type"": ""variable"",
                  ""variableId"": ""i""
                },
                ""selectorTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""sections"": [
                  {
                    ""labels"": [
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 1
                      }
                    ],
                    ""instructions"": [
                      {
                        ""type"": ""continue""
                      }
                    ]
                  },
                  {
                    ""labels"": [
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 2
                      }
                    ],
                    ""instructions"": [
                      {
                        ""type"": ""break""
                      }
                    ]
                  }
                ],
                ""defaultInstructions"": [
                  {
                    ""type"": ""break""
                  }
                ]
              },
              {
                ""type"": ""assign"",
                ""target"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""sum""
                  },
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""writability"": ""local""
                },
                ""operator"": ""="",
                ""pointer"": {
                  ""type"": ""operation"",
                  ""operation"": {
                    ""type"": ""arithmetic"",
                    ""arithmetic"": {
                      ""type"": ""+"",
                      ""pointers"": [
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""sum""
                        },
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""i""
                        }
                      ]
                    }
                  }
                }
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""sum""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 5
    },
    {
      ""name"": ""switch propagates throw from the selected section"",
      ""getter"": {
        ""compilerRevision"": 5,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""switch"",
            ""selector"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 1
              }
            },
            ""selectorTypeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""sections"": [
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 1
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""throw"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""switch boom""
                      }
                    }
                  }
                ]
              }
            ],
            ""defaultInstructions"": [
              {
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 0
                  }
                }
              }
            ]
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expectedError"": ""switch boom""
    },
    {
      ""name"": ""switch evaluates its derived selector exactly once"",
      ""getter"": {
        ""compilerRevision"": 5,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""switch"",
            ""selector"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""first"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""select"",
                      ""info"": {
                        ""collectionPointer"": {
                          ""type"": ""listLiteral"",
                          ""typeInfo"": {
                            ""type"": 6,
                            ""required"": true,
                            ""entryTypeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          ""entries"": [
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
                        },
                        ""function"": {
                          ""compilerRevision"": 4,
                          ""parameters"": [
                            {
                              ""id"": ""value"",
                              ""typeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              },
                              ""pointer"": {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  },
                                  ""value"": 0
                                }
                              }
                            }
                          ],
                          ""instructions"": [
                            {
                              ""type"": ""for"",
                              ""initializer"": {
                                ""id"": ""i"",
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                },
                                ""pointer"": {
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
                              ""condition"": {
                                ""condition"": {
                                  ""type"": ""lessThan"",
                                  ""operand1"": {
                                    ""type"": ""variable"",
                                    ""variableId"": ""i""
                                  },
                                  ""operand2"": {
                                    ""type"": ""value"",
                                    ""value"": {
                                      ""typeInfo"": {
                                        ""type"": 2,
                                        ""required"": true
                                      },
                                      ""value"": 10000
                                    }
                                  }
                                }
                              },
                              ""iterator"": {
                                ""type"": ""assign"",
                                ""target"": {
                                  ""pointer"": {
                                    ""type"": ""variable"",
                                    ""variableId"": ""i""
                                  },
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  },
                                  ""writability"": ""local""
                                },
                                ""operator"": ""++"",
                                ""pointer"": {
                                  ""type"": ""operation"",
                                  ""operation"": {
                                    ""type"": ""arithmetic"",
                                    ""arithmetic"": {
                                      ""type"": ""+"",
                                      ""pointers"": [
                                        {
                                          ""type"": ""variable"",
                                          ""variableId"": ""i""
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
                              },
                              ""instructions"": []
                            },
                            {
                              ""type"": ""return"",
                              ""pointer"": {
                                ""type"": ""variable"",
                                ""variableId"": ""value""
                              }
                            }
                          ],
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        }
                      }
                    }
                  }
                }
              }
            },
            ""selectorTypeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""sections"": [
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 1
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 1
                      }
                    }
                  }
                ]
              }
            ],
            ""defaultInstructions"": [
              {
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 0
                  }
                }
              }
            ]
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 1
    },
    {
      ""name"": ""switch-in-switch consumes inner break before completing the outer section"",
      ""getter"": {
        ""compilerRevision"": 5,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""result"",
              ""typeInfo"": {
                ""type"": 3,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            }
          },
          {
            ""type"": ""switch"",
            ""selector"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 1
              }
            },
            ""selectorTypeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""sections"": [
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 1
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""switch"",
                    ""selector"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 2
                      }
                    },
                    ""selectorTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""sections"": [
                      {
                        ""labels"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 2
                          }
                        ],
                        ""instructions"": [
                          {
                            ""type"": ""assign"",
                            ""target"": {
                              ""pointer"": {
                                ""type"": ""variable"",
                                ""variableId"": ""result""
                              },
                              ""typeInfo"": {
                                ""type"": 3,
                                ""required"": true
                              },
                              ""writability"": ""local""
                            },
                            ""operator"": ""="",
                            ""pointer"": {
                              ""type"": ""value"",
                              ""value"": {
                                ""typeInfo"": {
                                  ""type"": 3,
                                  ""required"": true
                                },
                                ""value"": ""inner""
                              }
                            }
                          },
                          {
                            ""type"": ""break""
                          }
                        ]
                      }
                    ]
                  },
                  {
                    ""type"": ""assign"",
                    ""target"": {
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""result""
                      },
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""writability"": ""local""
                    },
                    ""operator"": ""+="",
                    ""pointer"": {
                      ""type"": ""operation"",
                      ""operation"": {
                        ""type"": ""arithmetic"",
                        ""arithmetic"": {
                          ""type"": ""+"",
                          ""pointers"": [
                            {
                              ""type"": ""variable"",
                              ""variableId"": ""result""
                            },
                            {
                              ""type"": ""value"",
                              ""value"": {
                                ""typeInfo"": {
                                  ""type"": 3,
                                  ""required"": true
                                },
                                ""value"": ""-outer""
                              }
                            }
                          ]
                        }
                      }
                    }
                  },
                  {
                    ""type"": ""break""
                  }
                ]
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""result""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expected"": ""inner-outer""
    },
    {
      ""name"": ""loop-in-switch consumes loop break before completing the selected section"",
      ""getter"": {
        ""compilerRevision"": 5,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""count"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 0
                }
              }
            }
          },
          {
            ""type"": ""switch"",
            ""selector"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 1
              }
            },
            ""selectorTypeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""sections"": [
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 1
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""for"",
                    ""initializer"": {
                      ""id"": ""i"",
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""pointer"": {
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
                    ""condition"": {
                      ""condition"": {
                        ""type"": ""lessThan"",
                        ""operand1"": {
                          ""type"": ""variable"",
                          ""variableId"": ""i""
                        },
                        ""operand2"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 3
                          }
                        }
                      }
                    },
                    ""iterator"": {
                      ""type"": ""assign"",
                      ""target"": {
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""i""
                        },
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""writability"": ""local""
                      },
                      ""operator"": ""="",
                      ""pointer"": {
                        ""type"": ""operation"",
                        ""operation"": {
                          ""type"": ""arithmetic"",
                          ""arithmetic"": {
                            ""type"": ""+"",
                            ""pointers"": [
                              {
                                ""type"": ""variable"",
                                ""variableId"": ""i""
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
                    },
                    ""instructions"": [
                      {
                        ""type"": ""assign"",
                        ""target"": {
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""count""
                          },
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""writability"": ""local""
                        },
                        ""operator"": ""+="",
                        ""pointer"": {
                          ""type"": ""operation"",
                          ""operation"": {
                            ""type"": ""arithmetic"",
                            ""arithmetic"": {
                              ""type"": ""+"",
                              ""pointers"": [
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""count""
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
                      },
                      {
                        ""type"": ""break""
                      }
                    ]
                  },
                  {
                    ""type"": ""assign"",
                    ""target"": {
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""count""
                      },
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""writability"": ""local""
                    },
                    ""operator"": ""+="",
                    ""pointer"": {
                      ""type"": ""operation"",
                      ""operation"": {
                        ""type"": ""arithmetic"",
                        ""arithmetic"": {
                          ""type"": ""+"",
                          ""pointers"": [
                            {
                              ""type"": ""variable"",
                              ""variableId"": ""count""
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
                    }
                  },
                  {
                    ""type"": ""break""
                  }
                ]
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""count""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 11
    },
    {
      ""name"": ""try-inside-switch catches an authored error before the section breaks"",
      ""getter"": {
        ""compilerRevision"": 6,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""result"",
              ""typeInfo"": {
                ""type"": 3,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": """"
                }
              }
            }
          },
          {
            ""type"": ""switch"",
            ""selector"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 1
              }
            },
            ""selectorTypeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""sections"": [
              {
                ""labels"": [
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 1
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""try"",
                    ""instructions"": [
                      {
                        ""type"": ""throw"",
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 3,
                              ""required"": true
                            },
                            ""value"": ""boom""
                          }
                        }
                      }
                    ],
                    ""catches"": [
                      {
                        ""binding"": {
                          ""id"": ""message"",
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""readonly"": true
                        },
                        ""filter"": null,
                        ""instructions"": [
                          {
                            ""type"": ""assign"",
                            ""target"": {
                              ""pointer"": {
                                ""type"": ""variable"",
                                ""variableId"": ""result""
                              },
                              ""typeInfo"": {
                                ""type"": 3,
                                ""required"": true
                              },
                              ""writability"": ""local""
                            },
                            ""operator"": ""="",
                            ""pointer"": {
                              ""type"": ""value"",
                              ""value"": {
                                ""typeInfo"": {
                                  ""type"": 3,
                                  ""required"": true
                                },
                                ""value"": ""caught""
                              }
                            }
                          }
                        ]
                      }
                    ]
                  },
                  {
                    ""type"": ""break""
                  }
                ]
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""result""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expected"": ""caught""
    },
    {
      ""name"": ""try selects the first true filter and skips later clauses"",
      ""getter"": {
        ""compilerRevision"": 6,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""selected"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 0
                }
              }
            }
          },
          {
            ""type"": ""try"",
            ""instructions"": [
              {
                ""type"": ""throw"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""boom""
                  }
                }
              }
            ],
            ""catches"": [
              {
                ""binding"": {
                  ""id"": ""message:false"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""filter"": {
                  ""condition"": {
                    ""type"": ""equalTo"",
                    ""operand1"": {
                      ""type"": ""variable"",
                      ""variableId"": ""message:false""
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""no""
                      }
                    }
                  }
                },
                ""instructions"": [
                  {
                    ""type"": ""assign"",
                    ""target"": {
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""selected""
                      },
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""writability"": ""local""
                    },
                    ""operator"": ""="",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 1
                      }
                    }
                  }
                ]
              },
              {
                ""binding"": {
                  ""id"": ""message:true"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""filter"": {
                  ""condition"": {
                    ""type"": ""equalTo"",
                    ""operand1"": {
                      ""type"": ""variable"",
                      ""variableId"": ""message:true""
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""boom""
                      }
                    }
                  }
                },
                ""instructions"": [
                  {
                    ""type"": ""assign"",
                    ""target"": {
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""selected""
                      },
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""writability"": ""local""
                    },
                    ""operator"": ""="",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 2
                      }
                    }
                  }
                ]
              },
              {
                ""binding"": {
                  ""id"": ""message:fallback"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""instructions"": [
                  {
                    ""type"": ""assign"",
                    ""target"": {
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""selected""
                      },
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""writability"": ""local""
                    },
                    ""operator"": ""="",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 3
                      }
                    }
                  }
                ]
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""selected""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 2
    },
    {
      ""name"": ""try continues past false filters and the fallback catches"",
      ""getter"": {
        ""compilerRevision"": 6,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""try"",
            ""instructions"": [
              {
                ""type"": ""throw"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""boom""
                  }
                }
              }
            ],
            ""catches"": [
              {
                ""binding"": {
                  ""id"": ""message:first"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""filter"": {
                  ""condition"": {
                    ""type"": ""equalTo"",
                    ""operand1"": {
                      ""type"": ""variable"",
                      ""variableId"": ""message:first""
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""one""
                      }
                    }
                  }
                },
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""wrong-first""
                      }
                    }
                  }
                ]
              },
              {
                ""binding"": {
                  ""id"": ""message:second"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""filter"": {
                  ""condition"": {
                    ""type"": ""equalTo"",
                    ""operand1"": {
                      ""type"": ""variable"",
                      ""variableId"": ""message:second""
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""two""
                      }
                    }
                  }
                },
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""wrong-second""
                      }
                    }
                  }
                ]
              },
              {
                ""binding"": {
                  ""id"": ""message:fallback"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""message:fallback""
                    }
                  }
                ]
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""unreachable""
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expected"": ""boom""
    },
    {
      ""name"": ""try propagates the original error when no catch matches"",
      ""getter"": {
        ""compilerRevision"": 6,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""try"",
            ""instructions"": [
              {
                ""type"": ""throw"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""original""
                  }
                }
              }
            ],
            ""catches"": [
              {
                ""binding"": {
                  ""id"": ""message:filtered"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""filter"": {
                  ""condition"": {
                    ""type"": ""equalTo"",
                    ""operand1"": {
                      ""type"": ""variable"",
                      ""variableId"": ""message:filtered""
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""different""
                      }
                    }
                  }
                },
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""wrong""
                      }
                    }
                  }
                ]
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""unreachable""
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expectedError"": ""original""
    },
    {
      ""name"": ""try treats a catchable filter error as false and preserves the original"",
      ""getter"": {
        ""compilerRevision"": 6,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""try"",
            ""instructions"": [
              {
                ""type"": ""throw"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""original""
                  }
                }
              }
            ],
            ""catches"": [
              {
                ""binding"": {
                  ""id"": ""message:failing-filter"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""filter"": {
                  ""condition"": {
                    ""type"": ""equalTo"",
                    ""operand1"": {
                      ""type"": ""operation"",
                      ""operation"": {
                        ""type"": ""arithmetic"",
                        ""arithmetic"": {
                          ""type"": ""/"",
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
                                ""value"": 0
                              }
                            }
                          ]
                        }
                      }
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 0
                      }
                    }
                  }
                },
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""wrong-filter""
                      }
                    }
                  }
                ]
              },
              {
                ""binding"": {
                  ""id"": ""message:original"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""filter"": {
                  ""condition"": {
                    ""type"": ""equalTo"",
                    ""operand1"": {
                      ""type"": ""variable"",
                      ""variableId"": ""message:original""
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""original""
                      }
                    }
                  }
                },
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""message:original""
                    }
                  }
                ]
              },
              {
                ""binding"": {
                  ""id"": ""message:fallback"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""wrong-fallback""
                      }
                    }
                  }
                ]
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""unreachable""
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expected"": ""original""
    },
    {
      ""name"": ""an error in a selected catch escapes siblings to an enclosing try"",
      ""getter"": {
        ""compilerRevision"": 6,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""try"",
            ""instructions"": [
              {
                ""type"": ""try"",
                ""instructions"": [
                  {
                    ""type"": ""throw"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""original""
                      }
                    }
                  }
                ],
                ""catches"": [
                  {
                    ""binding"": {
                      ""id"": ""inner:selected"",
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""readonly"": true
                    },
                    ""filter"": {
                      ""condition"": {
                        ""type"": ""equalTo"",
                        ""operand1"": {
                          ""type"": ""variable"",
                          ""variableId"": ""inner:selected""
                        },
                        ""operand2"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 3,
                              ""required"": true
                            },
                            ""value"": ""original""
                          }
                        }
                      }
                    },
                    ""instructions"": [
                      {
                        ""type"": ""throw"",
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 3,
                              ""required"": true
                            },
                            ""value"": ""selected""
                          }
                        }
                      }
                    ]
                  },
                  {
                    ""binding"": {
                      ""id"": ""inner:sibling"",
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""readonly"": true
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 3,
                              ""required"": true
                            },
                            ""value"": ""wrong-sibling""
                          }
                        }
                      }
                    ]
                  }
                ]
              }
            ],
            ""catches"": [
              {
                ""binding"": {
                  ""id"": ""outer:message"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""outer:message""
                    }
                  }
                ]
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""unreachable""
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expected"": ""selected""
    },
    {
      ""name"": ""return propagates through try without entering catches"",
      ""getter"": {
        ""compilerRevision"": 6,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""try"",
            ""instructions"": [
              {
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 7
                  }
                }
              }
            ],
            ""catches"": [
              {
                ""binding"": {
                  ""id"": ""message:return"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 0
                      }
                    }
                  }
                ]
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": -1
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 7
    },
    {
      ""name"": ""break and continue propagate through try to the enclosing for loop"",
      ""getter"": {
        ""compilerRevision"": 6,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""total"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 0
                }
              }
            }
          },
          {
            ""type"": ""for"",
            ""initializer"": {
              ""id"": ""i"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
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
            ""condition"": {
              ""condition"": {
                ""type"": ""lessThan"",
                ""operand1"": {
                  ""type"": ""variable"",
                  ""variableId"": ""i""
                },
                ""operand2"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 4
                  }
                }
              }
            },
            ""iterator"": {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""i""
                },
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""writability"": ""local""
              },
              ""operator"": ""++"",
              ""pointer"": {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""+"",
                    ""pointers"": [
                      {
                        ""type"": ""variable"",
                        ""variableId"": ""i""
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
            },
            ""instructions"": [
              {
                ""type"": ""try"",
                ""instructions"": [
                  {
                    ""type"": ""if"",
                    ""branches"": [
                      {
                        ""expression"": {
                          ""condition"": {
                            ""type"": ""equalTo"",
                            ""operand1"": {
                              ""type"": ""variable"",
                              ""variableId"": ""i""
                            },
                            ""operand2"": {
                              ""type"": ""value"",
                              ""value"": {
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                },
                                ""value"": 0
                              }
                            }
                          }
                        },
                        ""instructions"": [
                          {
                            ""type"": ""continue""
                          }
                        ]
                      }
                    ]
                  },
                  {
                    ""type"": ""assign"",
                    ""target"": {
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""total""
                      },
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""writability"": ""local""
                    },
                    ""operator"": ""+="",
                    ""pointer"": {
                      ""type"": ""operation"",
                      ""operation"": {
                        ""type"": ""arithmetic"",
                        ""arithmetic"": {
                          ""type"": ""+"",
                          ""pointers"": [
                            {
                              ""type"": ""variable"",
                              ""variableId"": ""total""
                            },
                            {
                              ""type"": ""variable"",
                              ""variableId"": ""i""
                            }
                          ]
                        }
                      }
                    }
                  },
                  {
                    ""type"": ""if"",
                    ""branches"": [
                      {
                        ""expression"": {
                          ""condition"": {
                            ""type"": ""equalTo"",
                            ""operand1"": {
                              ""type"": ""variable"",
                              ""variableId"": ""i""
                            },
                            ""operand2"": {
                              ""type"": ""value"",
                              ""value"": {
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                },
                                ""value"": 2
                              }
                            }
                          }
                        },
                        ""instructions"": [
                          {
                            ""type"": ""break""
                          }
                        ]
                      }
                    ]
                  }
                ],
                ""catches"": [
                  {
                    ""binding"": {
                      ""id"": ""message:loop"",
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""readonly"": true
                    },
                    ""instructions"": [
                      {
                        ""type"": ""assign"",
                        ""target"": {
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""total""
                          },
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""writability"": ""local""
                        },
                        ""operator"": ""="",
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 99
                          }
                        }
                      }
                    ]
                  }
                ]
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""total""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 3
    },
    {
      ""name"": ""writes completed before a caught error remain visible"",
      ""getter"": {
        ""compilerRevision"": 6,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""result"",
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 1
                }
              }
            }
          },
          {
            ""type"": ""try"",
            ""instructions"": [
              {
                ""type"": ""assign"",
                ""target"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""result""
                  },
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""writability"": ""local""
                },
                ""operator"": ""="",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 2
                  }
                }
              },
              {
                ""type"": ""throw"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""recover""
                  }
                }
              }
            ],
            ""catches"": [
              {
                ""binding"": {
                  ""id"": ""message:write"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""instructions"": []
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""result""
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 2
    },
    {
      ""name"": ""try catches a deliberate arithmetic runtime error with its exact message"",
      ""getter"": {
        ""compilerRevision"": 6,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""try"",
            ""instructions"": [
              {
                ""type"": ""variable"",
                ""variable"": {
                  ""id"": ""unused"",
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""pointer"": {
                    ""type"": ""operation"",
                    ""operation"": {
                      ""type"": ""arithmetic"",
                      ""arithmetic"": {
                        ""type"": ""/"",
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
                              ""value"": 0
                            }
                          }
                        ]
                      }
                    }
                  }
                }
              },
              {
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""wrong""
                  }
                }
              }
            ],
            ""catches"": [
              {
                ""binding"": {
                  ""id"": ""message:domain"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""message:domain""
                    }
                  }
                ]
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""unreachable""
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expected"": ""Division by zero""
    },
    {
      ""name"": ""try preserves an empty thrown message"",
      ""getter"": {
        ""compilerRevision"": 6,
        ""parameters"": [],
        ""instructions"": [
          {
            ""type"": ""try"",
            ""instructions"": [
              {
                ""type"": ""throw"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": """"
                  }
                }
              }
            ],
            ""catches"": [
              {
                ""binding"": {
                  ""id"": ""message:empty"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""instructions"": [
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""message:empty""
                    }
                  }
                ]
              }
            ]
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""unreachable""
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        }
      },
      ""expected"": """"
    }
  ]
}
";
    }
}
