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
  ""$comment"": ""P50-P52 cross-runtime NeoScript control-flow parity fixture. Hand-authored raw IR is consumed directly by the web test and must be vendored verbatim into the neo-compose-dotnet Unity tests; do not regenerate it from either compiler/runtime. P50 cases pin ordered loop control, outer-local updates, collection snapshot membership, JavaScript dictionary-value order (including numeric-like keys), empty, derived, and Lookup-contract collections, receiver evaluation count, throw/return propagation, and the shared 10,000-iteration budget. Persisted write-intent needs project records and an effect-capable harness, so it is deliberately covered by compiler and runtime-specific tests rather than this getter-only empty-project fixture."",
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
    }
  ]
}
";
    }
}
