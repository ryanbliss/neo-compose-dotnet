// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
// VENDORED — DO NOT HAND-EDIT.
// Source: neo-compose/src/models/neoscript/neoscript-queries-debug-parity-fixture.json
namespace NeoCompose.Tests
{
    public static class NeoScriptQueriesDebugParityFixture
    {
        public const string Json = @"{
  ""$comment"": ""Hand-authored P102 wire IR shared verbatim by TypeScript and Unity; do not regenerate from compiler output."",
  ""evaluateCases"": [
    {
      ""name"": ""Any stops at first matching entry"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Any"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
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
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""debug"",
                        ""severity"": ""log"",
                        ""message"": {
                          ""type"": ""variable"",
                          ""variableId"": ""n""
                        },
                        ""messageType"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""source"": {
                          ""line"": 6,
                          ""column"": 5
                        }
                      },
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 1,
                              ""required"": true
                            },
                            ""value"": true
                          }
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
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
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        }
      },
      ""expected"": true,
      ""logs"": [""1""],
      ""events"": [
        {
          ""severity"": ""log"",
          ""message"": ""1"",
          ""frames"": [
            {
              ""name"": ""Cases.Run callback"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 6,
              ""column"": 5
            },
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 8,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""All stops at first failing entry"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""All"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
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
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""debug"",
                        ""severity"": ""log"",
                        ""message"": {
                          ""type"": ""variable"",
                          ""variableId"": ""n""
                        },
                        ""messageType"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""source"": {
                          ""line"": 6,
                          ""column"": 5
                        }
                      },
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 1,
                              ""required"": true
                            },
                            ""value"": false
                          }
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
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
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        }
      },
      ""expected"": false,
      ""logs"": [""1""],
      ""events"": [
        {
          ""severity"": ""log"",
          ""message"": ""1"",
          ""frames"": [
            {
              ""name"": ""Cases.Run callback"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 6,
              ""column"": 5
            },
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 8,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""empty Any is false"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Any"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": []
                  }
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        }
      },
      ""expected"": false
    },
    {
      ""name"": ""empty All is true"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""All"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": []
                  },
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 1,
                              ""required"": true
                            },
                            ""value"": false
                          }
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
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
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""Last selects one"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Last"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
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
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": false
        }
      },
      ""expected"": 3
    },
    {
      ""name"": ""Last empty"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Last"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": []
                  }
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": false
        }
      },
      ""expectedError"": ""Last() found no matching entry.""
    },
    {
      ""name"": ""LastOrDefault selects one"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""LastOrDefault"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
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
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": false
        }
      },
      ""expected"": 3
    },
    {
      ""name"": ""LastOrDefault empty"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""LastOrDefault"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": []
                  }
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": false
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""Single selects one"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Single"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
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
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": false
        }
      },
      ""expected"": 3
    },
    {
      ""name"": ""Single empty"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Single"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": []
                  }
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": false
        }
      },
      ""expectedError"": ""Single() found no matching entry.""
    },
    {
      ""name"": ""SingleOrDefault selects one"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""SingleOrDefault"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
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
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": false
        }
      },
      ""expected"": 3
    },
    {
      ""name"": ""SingleOrDefault empty"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""SingleOrDefault"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": []
                  }
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": false
        }
      },
      ""expected"": null
    },
    {
      ""name"": ""Single stops on second match"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Single"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
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
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""debug"",
                        ""severity"": ""log"",
                        ""message"": {
                          ""type"": ""variable"",
                          ""variableId"": ""n""
                        },
                        ""messageType"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""source"": {
                          ""line"": 6,
                          ""column"": 5
                        }
                      },
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 1,
                              ""required"": true
                            },
                            ""value"": true
                          }
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
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
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": false
        }
      },
      ""expectedError"": ""Single() found more than one matching entry."",
      ""logs"": [""1"", ""2""],
      ""events"": [
        {
          ""severity"": ""log"",
          ""message"": ""1"",
          ""frames"": [
            {
              ""name"": ""Cases.Run callback"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 6,
              ""column"": 5
            },
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 8,
              ""column"": 3
            }
          ]
        },
        {
          ""severity"": ""log"",
          ""message"": ""2"",
          ""frames"": [
            {
              ""name"": ""Cases.Run callback"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 6,
              ""column"": 5
            },
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 8,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""SingleOrDefault stops on second match"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""SingleOrDefault"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
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
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""debug"",
                        ""severity"": ""log"",
                        ""message"": {
                          ""type"": ""variable"",
                          ""variableId"": ""n""
                        },
                        ""messageType"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""source"": {
                          ""line"": 6,
                          ""column"": 5
                        }
                      },
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 1,
                              ""required"": true
                            },
                            ""value"": true
                          }
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
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
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": false
        }
      },
      ""expectedError"": ""SingleOrDefault() found more than one matching entry."",
      ""logs"": [""1"", ""2""],
      ""events"": [
        {
          ""severity"": ""log"",
          ""message"": ""1"",
          ""frames"": [
            {
              ""name"": ""Cases.Run callback"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 6,
              ""column"": 5
            },
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 8,
              ""column"": 3
            }
          ]
        },
        {
          ""severity"": ""log"",
          ""message"": ""2"",
          ""frames"": [
            {
              ""name"": ""Cases.Run callback"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 6,
              ""column"": 5
            },
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 8,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""SelectMany flattens in source order"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""SelectMany"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
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
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""listLiteral"",
                          ""typeInfo"": {
                            ""type"": 6,
                            ""required"": true,
                            ""entryTypeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""readOnly"": true
                          },
                          ""entries"": [
                            {
                              ""type"": ""variable"",
                              ""variableId"": ""n""
                            },
                            {
                              ""type"": ""variable"",
                              ""variableId"": ""n""
                            }
                          ]
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [1, 1, 2, 2]
    },
    {
      ""name"": ""OrderBy caches keys"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""OrderBy"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
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
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""debug"",
                        ""severity"": ""log"",
                        ""message"": {
                          ""type"": ""variable"",
                          ""variableId"": ""n""
                        },
                        ""messageType"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""source"": {
                          ""line"": 6,
                          ""column"": 5
                        }
                      },
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""n""
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  ""keyType"": ""int""
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [1, 2, 3],
      ""logs"": [""3"", ""1"", ""2""],
      ""events"": [
        {
          ""severity"": ""log"",
          ""message"": ""3"",
          ""frames"": [
            {
              ""name"": ""Cases.Run callback"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 6,
              ""column"": 5
            },
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 8,
              ""column"": 3
            }
          ]
        },
        {
          ""severity"": ""log"",
          ""message"": ""1"",
          ""frames"": [
            {
              ""name"": ""Cases.Run callback"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 6,
              ""column"": 5
            },
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 8,
              ""column"": 3
            }
          ]
        },
        {
          ""severity"": ""log"",
          ""message"": ""2"",
          ""frames"": [
            {
              ""name"": ""Cases.Run callback"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 6,
              ""column"": 5
            },
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 8,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""OrderBy ties retain source order"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""OrderByDescending"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
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
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
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
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  ""keyType"": ""int""
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [3, 1, 2]
    },
    {
      ""name"": ""Skip1"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Skip"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
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
                  ""countPointer"": {
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
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [2, 3]
    },
    {
      ""name"": ""Skip-1"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Skip"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
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
                  ""countPointer"": {
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
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [1, 2, 3]
    },
    {
      ""name"": ""Take2"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Take"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
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
                  ""countPointer"": {
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
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [1, 2]
    },
    {
      ""name"": ""Take0"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Take"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
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
                  ""countPointer"": {
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
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": []
    },
    {
      ""name"": ""SkipWhile"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""SkipWhile"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
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
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""debug"",
                        ""severity"": ""log"",
                        ""message"": {
                          ""type"": ""variable"",
                          ""variableId"": ""n""
                        },
                        ""messageType"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""source"": {
                          ""line"": 6,
                          ""column"": 5
                        }
                      },
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 1,
                              ""required"": true
                            },
                            ""value"": false
                          }
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
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
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [1, 2, 3],
      ""logs"": [""1""],
      ""events"": [
        {
          ""severity"": ""log"",
          ""message"": ""1"",
          ""frames"": [
            {
              ""name"": ""Cases.Run callback"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 6,
              ""column"": 5
            },
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 8,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""TakeWhile"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""TakeWhile"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
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
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""debug"",
                        ""severity"": ""log"",
                        ""message"": {
                          ""type"": ""variable"",
                          ""variableId"": ""n""
                        },
                        ""messageType"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""source"": {
                          ""line"": 6,
                          ""column"": 5
                        }
                      },
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 1,
                              ""required"": true
                            },
                            ""value"": false
                          }
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
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
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [],
      ""logs"": [""1""],
      ""events"": [
        {
          ""severity"": ""log"",
          ""message"": ""1"",
          ""frames"": [
            {
              ""name"": ""Cases.Run callback"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 6,
              ""column"": 5
            },
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 8,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""Reverse"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Reverse"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
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
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [3, 2, 1]
    },
    {
      ""name"": ""Concat preserves duplicates"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Concat"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
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
                  ""otherPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
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
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [1, 2, 2, 3]
    },
    {
      ""name"": ""ordinal string"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""OrderBy"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""z""
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""A""
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
                  },
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""n""
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    }
                  },
                  ""keyType"": ""string""
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 3,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [""A"", ""a"", ""z""]
    },
    {
      ""name"": ""ordinal decimal"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""OrderBy"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 20,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 20,
                            ""required"": true
                          },
                          ""value"": ""1.0000000000000000001""
                        }
                      },
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
                            ""type"": 20,
                            ""required"": true
                          },
                          ""value"": ""-1""
                        }
                      }
                    ]
                  },
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 20,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""n""
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 20,
                      ""required"": true
                    }
                  },
                  ""keyType"": ""decimal""
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 20,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [""-1"", ""1"", ""1.0000000000000000001""]
    },
    {
      ""name"": ""float debug 0"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""debug"",
            ""severity"": ""warning"",
            ""message"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 4,
                  ""required"": true
                },
                ""value"": 0
              }
            },
            ""messageType"": {
              ""type"": 4,
              ""required"": true
            },
            ""source"": {
              ""line"": 2,
              ""column"": 3
            }
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
                ""value"": 1
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 1,
      ""logs"": [""0""],
      ""events"": [
        {
          ""severity"": ""warning"",
          ""message"": ""0"",
          ""frames"": [
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 2,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""float debug -0.0"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""debug"",
            ""severity"": ""warning"",
            ""message"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 4,
                  ""required"": true
                },
                ""value"": -0.0
              }
            },
            ""messageType"": {
              ""type"": 4,
              ""required"": true
            },
            ""source"": {
              ""line"": 2,
              ""column"": 3
            }
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
                ""value"": 1
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 1,
      ""logs"": [""0""],
      ""events"": [
        {
          ""severity"": ""warning"",
          ""message"": ""0"",
          ""frames"": [
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 2,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""float debug 0.1"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""debug"",
            ""severity"": ""warning"",
            ""message"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 4,
                  ""required"": true
                },
                ""value"": 0.1
              }
            },
            ""messageType"": {
              ""type"": 4,
              ""required"": true
            },
            ""source"": {
              ""line"": 2,
              ""column"": 3
            }
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
                ""value"": 1
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 1,
      ""logs"": [""0.1""],
      ""events"": [
        {
          ""severity"": ""warning"",
          ""message"": ""0.1"",
          ""frames"": [
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 2,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""float debug 1e-05"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""debug"",
            ""severity"": ""warning"",
            ""message"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 4,
                  ""required"": true
                },
                ""value"": 1e-5
              }
            },
            ""messageType"": {
              ""type"": 4,
              ""required"": true
            },
            ""source"": {
              ""line"": 2,
              ""column"": 3
            }
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
                ""value"": 1
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 1,
      ""logs"": [""1e-5""],
      ""events"": [
        {
          ""severity"": ""warning"",
          ""message"": ""1e-5"",
          ""frames"": [
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 2,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""float debug 1000000000.0"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""debug"",
            ""severity"": ""warning"",
            ""message"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 4,
                  ""required"": true
                },
                ""value"": 1000000000.0
              }
            },
            ""messageType"": {
              ""type"": 4,
              ""required"": true
            },
            ""source"": {
              ""line"": 2,
              ""column"": 3
            }
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
                ""value"": 1
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 1,
      ""logs"": [""1e+9""],
      ""events"": [
        {
          ""severity"": ""warning"",
          ""message"": ""1e+9"",
          ""frames"": [
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 2,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""float debug 1.25"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""debug"",
            ""severity"": ""warning"",
            ""message"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 4,
                  ""required"": true
                },
                ""value"": 1.25
              }
            },
            ""messageType"": {
              ""type"": 4,
              ""required"": true
            },
            ""source"": {
              ""line"": 2,
              ""column"": 3
            }
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
                ""value"": 1
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 1,
      ""logs"": [""1.25""],
      ""events"": [
        {
          ""severity"": ""warning"",
          ""message"": ""1.25"",
          ""frames"": [
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 2,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""float debug 123456.789"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""debug"",
            ""severity"": ""warning"",
            ""message"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 4,
                  ""required"": true
                },
                ""value"": 123456.789
              }
            },
            ""messageType"": {
              ""type"": 4,
              ""required"": true
            },
            ""source"": {
              ""line"": 2,
              ""column"": 3
            }
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
                ""value"": 1
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 1,
      ""logs"": [""123456.789""],
      ""events"": [
        {
          ""severity"": ""warning"",
          ""message"": ""123456.789"",
          ""frames"": [
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 2,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""enum Set Reverse"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Reverse"",
                  ""collectionPointer"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 9,
                        ""required"": true,
                        ""entryTypeInfo"": {
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""enum:test""
                        }
                      },
                      ""value"": [""a"", ""b""]
                    }
                  },
                  ""enumEntries"": true
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 8,
            ""required"": true,
            ""enumId"": ""enum:test""
          }
        }
      },
      ""expected"": [[""b""], [""a""]]
    },
    {
      ""name"": ""enum Set Skip"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Skip"",
                  ""collectionPointer"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 9,
                        ""required"": true,
                        ""entryTypeInfo"": {
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""enum:test""
                        }
                      },
                      ""value"": [""a"", ""b""]
                    }
                  },
                  ""enumEntries"": true,
                  ""countPointer"": {
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
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 8,
            ""required"": true,
            ""enumId"": ""enum:test""
          }
        }
      },
      ""expected"": [[""b""]]
    },
    {
      ""name"": ""enum Set Take"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Take"",
                  ""collectionPointer"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 9,
                        ""required"": true,
                        ""entryTypeInfo"": {
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""enum:test""
                        }
                      },
                      ""value"": [""a"", ""b""]
                    }
                  },
                  ""enumEntries"": true,
                  ""countPointer"": {
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
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 8,
            ""required"": true,
            ""enumId"": ""enum:test""
          }
        }
      },
      ""expected"": [[""a""]]
    },
    {
      ""name"": ""enum Set Concat"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Concat"",
                  ""collectionPointer"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 9,
                        ""required"": true,
                        ""entryTypeInfo"": {
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""enum:test""
                        }
                      },
                      ""value"": [""a"", ""b""]
                    }
                  },
                  ""enumEntries"": true,
                  ""otherPointer"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 6,
                        ""required"": true,
                        ""entryTypeInfo"": {
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""enum:test""
                        }
                      },
                      ""value"": [[""a""]]
                    }
                  }
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 8,
            ""required"": true,
            ""enumId"": ""enum:test""
          }
        }
      },
      ""expected"": [[""a""], [""b""], [""a""]]
    },
    {
      ""name"": ""enum Set SelectMany"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""SelectMany"",
                  ""collectionPointer"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 9,
                        ""required"": true,
                        ""entryTypeInfo"": {
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""enum:test""
                        }
                      },
                      ""value"": [""a""]
                    }
                  },
                  ""enumEntries"": true,
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""enum:test""
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 9,
                              ""required"": true,
                              ""entryTypeInfo"": {
                                ""type"": 8,
                                ""required"": true,
                                ""enumId"": ""enum:test""
                              }
                            },
                            ""value"": [""a"", ""b""]
                          }
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 9,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 8,
                        ""required"": true,
                        ""enumId"": ""enum:test""
                      }
                    }
                  }
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 8,
            ""required"": true,
            ""enumId"": ""enum:test""
          }
        }
      },
      ""expected"": [[""a""], [""b""]]
    },
    {
      ""name"": ""enum Set OrderBy"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""OrderBy"",
                  ""collectionPointer"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 9,
                        ""required"": true,
                        ""entryTypeInfo"": {
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""enum:test""
                        }
                      },
                      ""value"": [""a"", ""b""]
                    }
                  },
                  ""enumEntries"": true,
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""enum:test""
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
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
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  ""keyType"": ""int""
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 8,
            ""required"": true,
            ""enumId"": ""enum:test""
          }
        }
      },
      ""expected"": [[""a""], [""b""]]
    },
    {
      ""name"": ""enum Set Last"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Last"",
                  ""collectionPointer"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 9,
                        ""required"": true,
                        ""entryTypeInfo"": {
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""enum:test""
                        }
                      },
                      ""value"": [""a"", ""b""]
                    }
                  },
                  ""enumEntries"": true
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 8,
          ""required"": true,
          ""enumId"": ""enum:test""
        }
      },
      ""expected"": [""b""]
    },
    {
      ""name"": ""enum Set Single"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Single"",
                  ""collectionPointer"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 9,
                        ""required"": true,
                        ""entryTypeInfo"": {
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""enum:test""
                        }
                      },
                      ""value"": [""a""]
                    }
                  },
                  ""enumEntries"": true
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 8,
          ""required"": true,
          ""enumId"": ""enum:test""
        }
      },
      ""expected"": [""a""]
    },
    {
      ""name"": ""fractional float ordering"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""OrderBy"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 4,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
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
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 0.5
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 2.25
                        }
                      }
                    ]
                  },
                  ""keyType"": ""float"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 4,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""n""
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 4,
                      ""required"": true
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [0.5, 1.5, 2.25]
    },
    {
      ""name"": ""bool ordering"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""OrderBy"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 1,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 1,
                            ""required"": true
                          },
                          ""value"": true
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 1,
                            ""required"": true
                          },
                          ""value"": false
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 1,
                            ""required"": true
                          },
                          ""value"": true
                        }
                      }
                    ]
                  },
                  ""keyType"": ""bool"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 1,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""n""
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
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
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 1,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [false, true, true]
    },
    {
      ""name"": ""nullable ascending"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""OrderBy"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": false
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": false
                          },
                          ""value"": 2
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": false
                          },
                          ""value"": null
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": false
                          },
                          ""value"": 1
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": false
                          },
                          ""value"": null
                        }
                      }
                    ]
                  },
                  ""keyType"": ""int"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": false
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""n""
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": false
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": false
          },
          ""readOnly"": true
        }
      },
      ""expected"": [null, null, 1, 2]
    },
    {
      ""name"": ""nullable descending"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""OrderByDescending"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": false
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": false
                          },
                          ""value"": 2
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": false
                          },
                          ""value"": null
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": false
                          },
                          ""value"": 1
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": false
                          },
                          ""value"": null
                        }
                      }
                    ]
                  },
                  ""keyType"": ""int"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": false
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""n""
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": false
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": false
          },
          ""readOnly"": true
        }
      },
      ""expected"": [2, 1, null, null]
    },
    {
      ""name"": ""nonfinite and signed zero ascending"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""OrderBy"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
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
                      }
                    ]
                  },
                  ""keyType"": ""float"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""conditional"",
                          ""condition"": {
                            ""type"": ""operation"",
                            ""operation"": {
                              ""type"": ""boolean"",
                              ""expression"": {
                                ""condition"": {
                                  ""type"": ""equalTo"",
                                  ""operand1"": {
                                    ""type"": ""variable"",
                                    ""variableId"": ""n""
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
                              }
                            }
                          },
                          ""whenTrue"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 4,
                                ""required"": true
                              },
                              ""value"": -0.0
                            }
                          },
                          ""whenFalse"": {
                            ""type"": ""conditional"",
                            ""condition"": {
                              ""type"": ""operation"",
                              ""operation"": {
                                ""type"": ""boolean"",
                                ""expression"": {
                                  ""condition"": {
                                    ""type"": ""equalTo"",
                                    ""operand1"": {
                                      ""type"": ""variable"",
                                      ""variableId"": ""n""
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
                            },
                            ""whenTrue"": {
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
                                        ""value"": -1
                                      }
                                    }
                                  ]
                                }
                              }
                            },
                            ""whenFalse"": {
                              ""type"": ""conditional"",
                              ""condition"": {
                                ""type"": ""operation"",
                                ""operation"": {
                                  ""type"": ""boolean"",
                                  ""expression"": {
                                    ""condition"": {
                                      ""type"": ""equalTo"",
                                      ""operand1"": {
                                        ""type"": ""variable"",
                                        ""variableId"": ""n""
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
                                  }
                                }
                              },
                              ""whenTrue"": {
                                ""type"": ""operation"",
                                ""operation"": {
                                  ""type"": ""arithmetic"",
                                  ""arithmetic"": {
                                    ""type"": ""*"",
                                    ""numeric"": ""float"",
                                    ""pointers"": [
                                      {
                                        ""type"": ""value"",
                                        ""value"": {
                                          ""typeInfo"": {
                                            ""type"": 4,
                                            ""required"": true
                                          },
                                          ""value"": 3.0000000054977558e38
                                        }
                                      },
                                      {
                                        ""type"": ""value"",
                                        ""value"": {
                                          ""typeInfo"": {
                                            ""type"": 4,
                                            ""required"": true
                                          },
                                          ""value"": 3.0000000054977558e38
                                        }
                                      }
                                    ]
                                  }
                                }
                              },
                              ""whenFalse"": {
                                ""type"": ""conditional"",
                                ""condition"": {
                                  ""type"": ""operation"",
                                  ""operation"": {
                                    ""type"": ""boolean"",
                                    ""expression"": {
                                      ""condition"": {
                                        ""type"": ""equalTo"",
                                        ""operand1"": {
                                          ""type"": ""variable"",
                                          ""variableId"": ""n""
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
                                    }
                                  }
                                },
                                ""whenTrue"": {
                                  ""type"": ""operation"",
                                  ""operation"": {
                                    ""type"": ""arithmetic"",
                                    ""arithmetic"": {
                                      ""type"": ""*"",
                                      ""numeric"": ""float"",
                                      ""pointers"": [
                                        {
                                          ""type"": ""operation"",
                                          ""operation"": {
                                            ""type"": ""arithmetic"",
                                            ""arithmetic"": {
                                              ""type"": ""*"",
                                              ""numeric"": ""float"",
                                              ""pointers"": [
                                                {
                                                  ""type"": ""value"",
                                                  ""value"": {
                                                    ""typeInfo"": {
                                                      ""type"": 4,
                                                      ""required"": true
                                                    },
                                                    ""value"": 3.0000000054977558e38
                                                  }
                                                },
                                                {
                                                  ""type"": ""value"",
                                                  ""value"": {
                                                    ""typeInfo"": {
                                                      ""type"": 4,
                                                      ""required"": true
                                                    },
                                                    ""value"": 3.0000000054977558e38
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
                                            ""value"": -1
                                          }
                                        }
                                      ]
                                    }
                                  }
                                },
                                ""whenFalse"": {
                                  ""type"": ""conditional"",
                                  ""condition"": {
                                    ""type"": ""operation"",
                                    ""operation"": {
                                      ""type"": ""boolean"",
                                      ""expression"": {
                                        ""condition"": {
                                          ""type"": ""equalTo"",
                                          ""operand1"": {
                                            ""type"": ""variable"",
                                            ""variableId"": ""n""
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
                                      }
                                    }
                                  },
                                  ""whenTrue"": {
                                    ""type"": ""value"",
                                    ""value"": {
                                      ""typeInfo"": {
                                        ""type"": 4,
                                        ""required"": true
                                      },
                                      ""value"": 0
                                    }
                                  },
                                  ""whenFalse"": {
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
                                              ""value"": -1
                                            }
                                          }
                                        ]
                                      }
                                    }
                                  }
                                }
                              }
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 4,
                      ""required"": true
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [1, 5, 3, 0, 4, 2]
    },
    {
      ""name"": ""nonfinite and signed zero descending"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""OrderByDescending"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
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
                      }
                    ]
                  },
                  ""keyType"": ""float"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""conditional"",
                          ""condition"": {
                            ""type"": ""operation"",
                            ""operation"": {
                              ""type"": ""boolean"",
                              ""expression"": {
                                ""condition"": {
                                  ""type"": ""equalTo"",
                                  ""operand1"": {
                                    ""type"": ""variable"",
                                    ""variableId"": ""n""
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
                              }
                            }
                          },
                          ""whenTrue"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 4,
                                ""required"": true
                              },
                              ""value"": -0.0
                            }
                          },
                          ""whenFalse"": {
                            ""type"": ""conditional"",
                            ""condition"": {
                              ""type"": ""operation"",
                              ""operation"": {
                                ""type"": ""boolean"",
                                ""expression"": {
                                  ""condition"": {
                                    ""type"": ""equalTo"",
                                    ""operand1"": {
                                      ""type"": ""variable"",
                                      ""variableId"": ""n""
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
                            },
                            ""whenTrue"": {
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
                                        ""value"": -1
                                      }
                                    }
                                  ]
                                }
                              }
                            },
                            ""whenFalse"": {
                              ""type"": ""conditional"",
                              ""condition"": {
                                ""type"": ""operation"",
                                ""operation"": {
                                  ""type"": ""boolean"",
                                  ""expression"": {
                                    ""condition"": {
                                      ""type"": ""equalTo"",
                                      ""operand1"": {
                                        ""type"": ""variable"",
                                        ""variableId"": ""n""
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
                                  }
                                }
                              },
                              ""whenTrue"": {
                                ""type"": ""operation"",
                                ""operation"": {
                                  ""type"": ""arithmetic"",
                                  ""arithmetic"": {
                                    ""type"": ""*"",
                                    ""numeric"": ""float"",
                                    ""pointers"": [
                                      {
                                        ""type"": ""value"",
                                        ""value"": {
                                          ""typeInfo"": {
                                            ""type"": 4,
                                            ""required"": true
                                          },
                                          ""value"": 3.0000000054977558e38
                                        }
                                      },
                                      {
                                        ""type"": ""value"",
                                        ""value"": {
                                          ""typeInfo"": {
                                            ""type"": 4,
                                            ""required"": true
                                          },
                                          ""value"": 3.0000000054977558e38
                                        }
                                      }
                                    ]
                                  }
                                }
                              },
                              ""whenFalse"": {
                                ""type"": ""conditional"",
                                ""condition"": {
                                  ""type"": ""operation"",
                                  ""operation"": {
                                    ""type"": ""boolean"",
                                    ""expression"": {
                                      ""condition"": {
                                        ""type"": ""equalTo"",
                                        ""operand1"": {
                                          ""type"": ""variable"",
                                          ""variableId"": ""n""
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
                                    }
                                  }
                                },
                                ""whenTrue"": {
                                  ""type"": ""operation"",
                                  ""operation"": {
                                    ""type"": ""arithmetic"",
                                    ""arithmetic"": {
                                      ""type"": ""*"",
                                      ""numeric"": ""float"",
                                      ""pointers"": [
                                        {
                                          ""type"": ""operation"",
                                          ""operation"": {
                                            ""type"": ""arithmetic"",
                                            ""arithmetic"": {
                                              ""type"": ""*"",
                                              ""numeric"": ""float"",
                                              ""pointers"": [
                                                {
                                                  ""type"": ""value"",
                                                  ""value"": {
                                                    ""typeInfo"": {
                                                      ""type"": 4,
                                                      ""required"": true
                                                    },
                                                    ""value"": 3.0000000054977558e38
                                                  }
                                                },
                                                {
                                                  ""type"": ""value"",
                                                  ""value"": {
                                                    ""typeInfo"": {
                                                      ""type"": 4,
                                                      ""required"": true
                                                    },
                                                    ""value"": 3.0000000054977558e38
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
                                            ""value"": -1
                                          }
                                        }
                                      ]
                                    }
                                  }
                                },
                                ""whenFalse"": {
                                  ""type"": ""conditional"",
                                  ""condition"": {
                                    ""type"": ""operation"",
                                    ""operation"": {
                                      ""type"": ""boolean"",
                                      ""expression"": {
                                        ""condition"": {
                                          ""type"": ""equalTo"",
                                          ""operand1"": {
                                            ""type"": ""variable"",
                                            ""variableId"": ""n""
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
                                      }
                                    }
                                  },
                                  ""whenTrue"": {
                                    ""type"": ""value"",
                                    ""value"": {
                                      ""typeInfo"": {
                                        ""type"": 4,
                                        ""required"": true
                                      },
                                      ""value"": 0
                                    }
                                  },
                                  ""whenFalse"": {
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
                                              ""value"": -1
                                            }
                                          }
                                        ]
                                      }
                                    }
                                  }
                                }
                              }
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 4,
                      ""required"": true
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [2, 0, 4, 3, 1, 5]
    },
    {
      ""name"": ""mixed int and float selector"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""OrderBy"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
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
                  ""keyType"": ""float"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""conditional"",
                          ""condition"": {
                            ""type"": ""operation"",
                            ""operation"": {
                              ""type"": ""boolean"",
                              ""expression"": {
                                ""condition"": {
                                  ""type"": ""equalTo"",
                                  ""operand1"": {
                                    ""type"": ""variable"",
                                    ""variableId"": ""n""
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
                              }
                            }
                          },
                          ""whenTrue"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 4,
                                ""required"": true
                              },
                              ""value"": 0.5
                            }
                          },
                          ""whenFalse"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          }
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 4,
                      ""required"": true
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""readOnly"": true
        }
      },
      ""expected"": [2, 1, 3]
    },
    {
      ""name"": ""dictionary ordering uses key and value"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""OrderBy"",
                  ""collectionPointer"": {
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
                            ""value"": 3
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
                            ""value"": 1
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
                            ""value"": ""third""
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
                      }
                    ]
                  },
                  ""keyType"": ""int"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""key"",
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 0,
                              ""required"": false
                            },
                            ""value"": null
                          }
                        }
                      },
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
                              ""type"": 0,
                              ""required"": false
                            },
                            ""value"": null
                          }
                        }
                      }
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""value""
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
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
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""expected"": [1, 2, 3]
    },
    {
      ""name"": ""dictionary flattening"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""SelectMany"",
                  ""collectionPointer"": {
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
                            ""value"": 3
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
                            ""value"": 1
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
                            ""value"": ""third""
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
                      }
                    ]
                  },
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""key"",
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 0,
                              ""required"": false
                            },
                            ""value"": null
                          }
                        }
                      },
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
                              ""type"": 0,
                              ""required"": false
                            },
                            ""value"": null
                          }
                        }
                      }
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""listLiteral"",
                          ""typeInfo"": {
                            ""type"": 6,
                            ""required"": true,
                            ""entryTypeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""readOnly"": true
                          },
                          ""entries"": [
                            {
                              ""type"": ""variable"",
                              ""variableId"": ""value""
                            },
                            {
                              ""type"": ""variable"",
                              ""variableId"": ""value""
                            }
                          ]
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""expected"": [3, 3, 1, 1, 2, 2]
    },
    {
      ""name"": ""nested callbacks retain captured values and frames"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""All"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""readOnly"": true
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
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run callback"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""function"",
                          ""function"": {
                            ""type"": ""collectionQuery"",
                            ""info"": {
                              ""op"": ""Any"",
                              ""collectionPointer"": {
                                ""type"": ""listLiteral"",
                                ""typeInfo"": {
                                  ""type"": 6,
                                  ""required"": true,
                                  ""entryTypeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  },
                                  ""readOnly"": true
                                },
                                ""entries"": [
                                  {
                                    ""type"": ""variable"",
                                    ""variableId"": ""n""
                                  }
                                ]
                              },
                              ""function"": {
                                ""compilerRevision"": 17,
                                ""parameters"": [
                                  {
                                    ""id"": ""m"",
                                    ""typeInfo"": {
                                      ""type"": 2,
                                      ""required"": true
                                    },
                                    ""pointer"": {
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
                                ],
                                ""source"": {
                                  ""name"": ""Cases.Run inner callback"",
                                  ""uri"": ""Cases.neo"",
                                  ""coordinateSpace"": ""file""
                                },
                                ""instructions"": [
                                  {
                                    ""type"": ""debug"",
                                    ""severity"": ""log"",
                                    ""message"": {
                                      ""type"": ""variable"",
                                      ""variableId"": ""m""
                                    },
                                    ""messageType"": {
                                      ""type"": 2,
                                      ""required"": true
                                    },
                                    ""source"": {
                                      ""line"": 6,
                                      ""column"": 5
                                    }
                                  },
                                  {
                                    ""type"": ""return"",
                                    ""pointer"": {
                                      ""type"": ""operation"",
                                      ""operation"": {
                                        ""type"": ""boolean"",
                                        ""expression"": {
                                          ""condition"": {
                                            ""type"": ""equalTo"",
                                            ""operand1"": {
                                              ""type"": ""variable"",
                                              ""variableId"": ""m""
                                            },
                                            ""operand2"": {
                                              ""type"": ""variable"",
                                              ""variableId"": ""n""
                                            }
                                          }
                                        }
                                      }
                                    },
                                    ""source"": {
                                      ""line"": 8,
                                      ""column"": 3
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
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
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
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        }
      },
      ""expected"": true,
      ""logs"": [""1"", ""2""],
      ""events"": [
        {
          ""severity"": ""log"",
          ""message"": ""1"",
          ""frames"": [
            {
              ""name"": ""Cases.Run inner callback"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 6,
              ""column"": 5
            },
            {
              ""name"": ""Cases.Run callback"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 8,
              ""column"": 3
            },
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 8,
              ""column"": 3
            }
          ]
        },
        {
          ""severity"": ""log"",
          ""message"": ""2"",
          ""frames"": [
            {
              ""name"": ""Cases.Run inner callback"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 6,
              ""column"": 5
            },
            {
              ""name"": ""Cases.Run callback"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 8,
              ""column"": 3
            },
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 8,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""derived class entries preserve base collection traversal"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Any"",
                  ""collectionPointer"": {
                    ""type"": ""listLiteral"",
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 7,
                        ""required"": true,
                        ""classId"": ""p102-base""
                      },
                      ""readOnly"": true
                    },
                    ""entries"": [
                      {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""classConstructor"",
                          ""info"": {
                            ""schemaClassInfo"": {
                              ""type"": 7,
                              ""required"": true,
                              ""classId"": ""p102-derived""
                            },
                            ""fields"": []
                          }
                        }
                      }
                    ]
                  },
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""parameters"": [
                      {
                        ""id"": ""n"",
                        ""typeInfo"": {
                          ""type"": 7,
                          ""required"": true,
                          ""classId"": ""p102-base""
                        },
                        ""pointer"": {
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
                    ],
                    ""source"": {
                      ""name"": ""Cases.Run"",
                      ""uri"": ""Cases.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 1,
                              ""required"": true
                            },
                            ""value"": true
                          }
                        },
                        ""source"": {
                          ""line"": 8,
                          ""column"": 3
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
            ""source"": {
              ""line"": 8,
              ""column"": 3
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        }
      },
      ""expected"": true
    },
    {
      ""name"": ""interface debug summary retains declared identity"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""debug"",
            ""severity"": ""warning"",
            ""message"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 22,
                  ""required"": true,
                  ""interfaceId"": ""p102-interface""
                },
                ""value"": {}
              }
            },
            ""messageType"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""p102-interface""
            },
            ""source"": {
              ""line"": 2,
              ""column"": 3
            }
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
                ""value"": 1
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 1,
      ""logs"": [""(Interface<p102-interface>, Value<<unknown>>)""],
      ""events"": [
        {
          ""severity"": ""warning"",
          ""message"": ""(Interface<p102-interface>, Value<<unknown>>)"",
          ""frames"": [
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 2,
              ""column"": 3
            }
          ]
        }
      ]
    },
    {
      ""name"": ""missing instruction location does not inherit preceding coordinates"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""parameters"": [],
        ""source"": {
          ""name"": ""Cases.Run"",
          ""uri"": ""Cases.neo"",
          ""coordinateSpace"": ""file""
        },
        ""instructions"": [
          {
            ""type"": ""debug"",
            ""severity"": ""warning"",
            ""message"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 4,
                  ""required"": true
                },
                ""value"": 0
              }
            },
            ""messageType"": {
              ""type"": 4,
              ""required"": true
            },
            ""source"": {
              ""line"": 2,
              ""column"": 3
            }
          },
          {
            ""type"": ""debug"",
            ""severity"": ""warning"",
            ""message"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 4,
                  ""required"": true
                },
                ""value"": 0
              }
            },
            ""messageType"": {
              ""type"": 4,
              ""required"": true
            }
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
                ""value"": 1
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""expected"": 1,
      ""logs"": [""0"", ""0""],
      ""events"": [
        {
          ""severity"": ""warning"",
          ""message"": ""0"",
          ""frames"": [
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file"",
              ""line"": 2,
              ""column"": 3
            }
          ]
        },
        {
          ""severity"": ""warning"",
          ""message"": ""0"",
          ""frames"": [
            {
              ""name"": ""Cases.Run"",
              ""uri"": ""Cases.neo"",
              ""coordinateSpace"": ""file""
            }
          ]
        }
      ]
    }
  ],
  ""classes"": [
    {
      ""id"": ""p102-base"",
      ""name"": ""P102Base"",
      ""schema"": {},
      ""projectId"": ""test-project"",
      ""createdAt"": 0,
      ""updatedAt"": 0
    },
    {
      ""id"": ""p102-derived"",
      ""name"": ""P102Derived"",
      ""extendsClassId"": ""p102-base"",
      ""schema"": {},
      ""projectId"": ""test-project"",
      ""createdAt"": 0,
      ""updatedAt"": 0
    }
  ]
}
";
    }
}
