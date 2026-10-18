// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
// VENDORED — DO NOT HAND-EDIT.
// Source: neo-compose/src/models/neoscript/neoscript-aggregation-parity-fixture.json
namespace NeoCompose.Tests
{
    public static class NeoScriptAggregationParityFixture
    {
        public const string Json = @"{
  ""classes"": [],
  ""evaluateCases"": [
    {
      ""name"": ""group preserves entry identity"",
      ""source"": ""enum Tone { Red, Blue } class Query { public bool Result { get { var item = [1]; var list = [item]; var grouped = list.GroupBy((n) => \""all\""); grouped[\""all\""][0][0] = 9; return item[0] == 9; } } }"",
      ""expected"": true,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""item"",
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
                  }
                ]
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 66
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""list"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              },
              ""pointer"": {
                ""type"": ""listLiteral"",
                ""typeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                },
                ""entries"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""item""
                  }
                ]
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 82
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""grouped"",
              ""typeInfo"": {
                ""type"": 5,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  ""readOnly"": true
                },
                ""readOnly"": true
              },
              ""pointer"": {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""collectionQuery"",
                  ""info"": {
                    ""collectionPointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""list""
                    },
                    ""op"": ""GroupBy"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
                      ""parameters"": [
                        {
                          ""id"": ""n"",
                          ""typeInfo"": {
                            ""type"": 6,
                            ""required"": true,
                            ""entryTypeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          ""pointer"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 6,
                                ""required"": true,
                                ""entryTypeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                }
                              },
                              ""value"": null
                            }
                          }
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
                              ""value"": ""all""
                            }
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 135
                          }
                        }
                      ],
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      }
                    }
                  }
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 127
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 101
            }
          },
          {
            ""type"": ""assign"",
            ""target"": {
              ""pointer"": {
                ""type"": ""keyOf"",
                ""keyOf"": {
                  ""pointer"": {
                    ""type"": ""keyOf"",
                    ""keyOf"": {
                      ""pointer"": {
                        ""type"": ""keyOf"",
                        ""keyOf"": {
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""grouped""
                          },
                          ""key"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 3,
                                ""required"": true
                              },
                              ""value"": ""all""
                            }
                          }
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
                }
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
                ""value"": 9
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 143
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
                      ""type"": ""keyOf"",
                      ""keyOf"": {
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""item""
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
                      }
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 9
                      }
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 169
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""zip preserves entry identity"",
      ""source"": ""enum Tone { Red, Blue } class Query { public bool Result { get { var item = [1]; var result = [item].Zip([0], (a, b) => a); result[0][0] = 9; return item[0] == 9; } } }"",
      ""expected"": true,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""item"",
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
                  }
                ]
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 66
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""result"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                ""readOnly"": true
              },
              ""pointer"": {
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
                          ""type"": 6,
                          ""required"": true,
                          ""entryTypeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        }
                      },
                      ""entries"": [
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""item""
                        }
                      ]
                    },
                    ""op"": ""Zip"",
                    ""otherPointer"": {
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
                            ""value"": 0
                          }
                        }
                      ]
                    },
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
                      ""parameters"": [
                        {
                          ""id"": ""a"",
                          ""typeInfo"": {
                            ""type"": 6,
                            ""required"": true,
                            ""entryTypeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          ""pointer"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 6,
                                ""required"": true,
                                ""entryTypeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                }
                              },
                              ""value"": null
                            }
                          }
                        },
                        {
                          ""id"": ""b"",
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
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""a""
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 121
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 105
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 82
            }
          },
          {
            ""type"": ""assign"",
            ""target"": {
              ""pointer"": {
                ""type"": ""keyOf"",
                ""keyOf"": {
                  ""pointer"": {
                    ""type"": ""keyOf"",
                    ""keyOf"": {
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""result""
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
                }
              },
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""writability"": ""runtime""
            },
            ""operator"": ""="",
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 9
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 125
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
                      ""type"": ""keyOf"",
                      ""keyOf"": {
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""item""
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
                      }
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 9
                      }
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 143
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""aggregate preserves entry identity"",
      ""source"": ""enum Tone { Red, Blue } class Query { public bool Result { get { var item = [1]; var result = [item, item].Aggregate((a, b) => b); result[0] = 9; return item[0] == 9; } } }"",
      ""expected"": true,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""item"",
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
                  }
                ]
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 66
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""result"",
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
                  ""type"": ""collectionQuery"",
                  ""info"": {
                    ""collectionPointer"": {
                      ""type"": ""listLiteral"",
                      ""typeInfo"": {
                        ""type"": 6,
                        ""required"": true,
                        ""entryTypeInfo"": {
                          ""type"": 6,
                          ""required"": true,
                          ""entryTypeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        }
                      },
                      ""entries"": [
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""item""
                        },
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""item""
                        }
                      ]
                    },
                    ""op"": ""Aggregate"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
                      ""parameters"": [
                        {
                          ""id"": ""a"",
                          ""typeInfo"": {
                            ""type"": 6,
                            ""required"": true,
                            ""entryTypeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          ""pointer"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 6,
                                ""required"": true,
                                ""entryTypeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                }
                              },
                              ""value"": null
                            }
                          }
                        },
                        {
                          ""id"": ""b"",
                          ""typeInfo"": {
                            ""type"": 6,
                            ""required"": true,
                            ""entryTypeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          ""pointer"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 6,
                                ""required"": true,
                                ""entryTypeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                }
                              },
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""b""
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 128
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 117
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 82
            }
          },
          {
            ""type"": ""assign"",
            ""target"": {
              ""pointer"": {
                ""type"": ""keyOf"",
                ""keyOf"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""result""
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
                }
              },
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""writability"": ""runtime""
            },
            ""operator"": ""="",
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 9
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 132
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
                      ""type"": ""keyOf"",
                      ""keyOf"": {
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""item""
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
                      }
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 9
                      }
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 147
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""dictionary projection preserves entry identity"",
      ""source"": ""enum Tone { Red, Blue } class Query { public bool Result { get { var item = [1]; var result = [item].ToDictionary((n) => \""key\"", (n) => n); result[\""key\""][0] = 9; return item[0] == 9; } } }"",
      ""expected"": true,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""item"",
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
                  }
                ]
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 66
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""result"",
              ""typeInfo"": {
                ""type"": 5,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                ""readOnly"": true
              },
              ""pointer"": {
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
                          ""type"": 6,
                          ""required"": true,
                          ""entryTypeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        }
                      },
                      ""entries"": [
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""item""
                        }
                      ]
                    },
                    ""op"": ""ToDictionary"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
                      ""parameters"": [
                        {
                          ""id"": ""n"",
                          ""typeInfo"": {
                            ""type"": 6,
                            ""required"": true,
                            ""entryTypeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          ""pointer"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 6,
                                ""required"": true,
                                ""entryTypeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                }
                              },
                              ""value"": null
                            }
                          }
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
                              ""value"": ""key""
                            }
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 122
                          }
                        }
                      ],
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      }
                    },
                    ""valueFunction"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
                      ""parameters"": [
                        {
                          ""id"": ""n"",
                          ""typeInfo"": {
                            ""type"": 6,
                            ""required"": true,
                            ""entryTypeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          ""pointer"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 6,
                                ""required"": true,
                                ""entryTypeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                }
                              },
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 136
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 114
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 82
            }
          },
          {
            ""type"": ""assign"",
            ""target"": {
              ""pointer"": {
                ""type"": ""keyOf"",
                ""keyOf"": {
                  ""pointer"": {
                    ""type"": ""keyOf"",
                    ""keyOf"": {
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""result""
                      },
                      ""key"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""key""
                        }
                      }
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
                }
              },
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""writability"": ""runtime""
            },
            ""operator"": ""="",
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 9
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 140
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
                      ""type"": ""keyOf"",
                      ""keyOf"": {
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""item""
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
                      }
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 9
                      }
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 162
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""select preserves dictionary identity"",
      ""source"": ""enum Tone { Red, Blue } class Query { public bool Result { get { var item = {\""key\"": [1]}; var result = [item].Select((n) => n); result[0][\""key\""][0] = 9; return item[\""key\""][0] == 9; } } }"",
      ""expected"": true,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""item"",
              ""typeInfo"": {
                ""type"": 5,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              },
              ""pointer"": {
                ""type"": ""dictLiteral"",
                ""typeInfo"": {
                  ""type"": 5,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
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
                        ""value"": ""key""
                      }
                    },
                    ""value"": {
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
                    }
                  }
                ]
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 66
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""result"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 5,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                },
                ""readOnly"": true
              },
              ""pointer"": {
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
                          ""type"": 5,
                          ""required"": true,
                          ""entryTypeInfo"": {
                            ""type"": 6,
                            ""required"": true,
                            ""entryTypeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        }
                      },
                      ""entries"": [
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""item""
                        }
                      ]
                    },
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
                      ""parameters"": [
                        {
                          ""id"": ""n"",
                          ""typeInfo"": {
                            ""type"": 5,
                            ""required"": true,
                            ""entryTypeInfo"": {
                              ""type"": 6,
                              ""required"": true,
                              ""entryTypeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              }
                            }
                          },
                          ""pointer"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 5,
                                ""required"": true,
                                ""entryTypeInfo"": {
                                  ""type"": 6,
                                  ""required"": true,
                                  ""entryTypeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                }
                              },
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 125
                          }
                        }
                      ],
                      ""typeInfo"": {
                        ""type"": 5,
                        ""required"": true,
                        ""entryTypeInfo"": {
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
                  ""line"": 1,
                  ""column"": 117
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 91
            }
          },
          {
            ""type"": ""assign"",
            ""target"": {
              ""pointer"": {
                ""type"": ""keyOf"",
                ""keyOf"": {
                  ""pointer"": {
                    ""type"": ""keyOf"",
                    ""keyOf"": {
                      ""pointer"": {
                        ""type"": ""keyOf"",
                        ""keyOf"": {
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""result""
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
                        }
                      },
                      ""key"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""key""
                        }
                      }
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
                }
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
                ""value"": 9
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 129
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
                      ""type"": ""keyOf"",
                      ""keyOf"": {
                        ""pointer"": {
                          ""type"": ""keyOf"",
                          ""keyOf"": {
                            ""pointer"": {
                              ""type"": ""variable"",
                              ""variableId"": ""item""
                            },
                            ""key"": {
                              ""type"": ""value"",
                              ""value"": {
                                ""typeInfo"": {
                                  ""type"": 3,
                                  ""required"": true
                                },
                                ""value"": ""key""
                              }
                            }
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
                      }
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 9
                      }
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 154
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""distinct uses reference identity"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int Result { get { var a = [1]; var b = [1]; return [a, b, a].Distinct().Count; } } }"",
      ""expected"": 2,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""a"",
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
                  }
                ]
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 65
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""b"",
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
                  }
                ]
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 78
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""count"",
                ""info"": {
                  ""collectionPointer"": {
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
                              ""type"": 6,
                              ""required"": true,
                              ""entryTypeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              }
                            }
                          },
                          ""entries"": [
                            {
                              ""type"": ""variable"",
                              ""variableId"": ""a""
                            },
                            {
                              ""type"": ""variable"",
                              ""variableId"": ""b""
                            },
                            {
                              ""type"": ""variable"",
                              ""variableId"": ""a""
                            }
                          ]
                        },
                        ""op"": ""Distinct"",
                        ""reference"": true
                      }
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 116
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 118
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 91
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""ordered list mutation invalidates 0"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var ordered = [2,1].OrderBy((n) => n); List<int> alias = ordered; alias.Add(9); return ordered.ThenBy((n) => 0); } } }"",
      ""expected"": null,
      ""expectedError"": ""requires an ordered result"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""ordered"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""readOnly"": true
              },
              ""pointer"": {
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
                    },
                    ""op"": ""OrderBy"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
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
                                ""type"": 2,
                                ""required"": true
                              },
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 106
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 98
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""alias"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""ordered""
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 110
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""alias""
              },
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""writability"": ""runtime""
            },
            ""mutation"": ""Add"",
            ""args"": [
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
            ],
            ""source"": {
              ""line"": 1,
              ""column"": 137
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""ordered""
                  },
                  ""op"": ""ThenBy"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
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
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 180
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
              },
              ""source"": {
                ""line"": 1,
                ""column"": 172
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 151
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""ordered list mutation invalidates 1"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var ordered = [2,1].OrderBy((n) => n); List<int> alias = ordered; alias.Remove(1); return ordered.ThenBy((n) => 0); } } }"",
      ""expected"": null,
      ""expectedError"": ""requires an ordered result"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""ordered"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""readOnly"": true
              },
              ""pointer"": {
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
                    },
                    ""op"": ""OrderBy"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
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
                                ""type"": 2,
                                ""required"": true
                              },
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 106
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 98
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""alias"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""ordered""
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 110
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""alias""
              },
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""writability"": ""runtime""
            },
            ""mutation"": ""Remove"",
            ""args"": [
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
            ],
            ""source"": {
              ""line"": 1,
              ""column"": 137
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""ordered""
                  },
                  ""op"": ""ThenBy"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
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
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 183
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
              },
              ""source"": {
                ""line"": 1,
                ""column"": 175
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 154
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""ordered list mutation invalidates 2"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var ordered = [2,1].OrderBy((n) => n); List<int> alias = ordered; alias.Clear(); return ordered.ThenBy((n) => 0); } } }"",
      ""expected"": null,
      ""expectedError"": ""requires an ordered result"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""ordered"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""readOnly"": true
              },
              ""pointer"": {
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
                    },
                    ""op"": ""OrderBy"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
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
                                ""type"": 2,
                                ""required"": true
                              },
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 106
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 98
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""alias"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""ordered""
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 110
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""alias""
              },
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""writability"": ""runtime""
            },
            ""mutation"": ""Clear"",
            ""args"": [],
            ""source"": {
              ""line"": 1,
              ""column"": 137
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""ordered""
                  },
                  ""op"": ""ThenBy"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
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
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 181
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
              },
              ""source"": {
                ""line"": 1,
                ""column"": 173
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 152
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""ordered list mutation invalidates 3"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var ordered = [2,1].OrderBy((n) => n); List<int> alias = ordered; alias.Insert(0, 9); return ordered.ThenBy((n) => 0); } } }"",
      ""expected"": null,
      ""expectedError"": ""requires an ordered result"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""ordered"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""readOnly"": true
              },
              ""pointer"": {
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
                    },
                    ""op"": ""OrderBy"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
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
                                ""type"": 2,
                                ""required"": true
                              },
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 106
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 98
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""alias"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""ordered""
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 110
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""alias""
              },
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""writability"": ""runtime""
            },
            ""mutation"": ""Insert"",
            ""args"": [
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
                  ""value"": 9
                }
              }
            ],
            ""source"": {
              ""line"": 1,
              ""column"": 137
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""ordered""
                  },
                  ""op"": ""ThenBy"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
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
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 186
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
              },
              ""source"": {
                ""line"": 1,
                ""column"": 178
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 157
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""ordered list mutation invalidates 4"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var ordered = [2,1].OrderBy((n) => n); List<int> alias = ordered; alias.RemoveAt(0); return ordered.ThenBy((n) => 0); } } }"",
      ""expected"": null,
      ""expectedError"": ""requires an ordered result"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""ordered"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""readOnly"": true
              },
              ""pointer"": {
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
                    },
                    ""op"": ""OrderBy"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
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
                                ""type"": 2,
                                ""required"": true
                              },
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 106
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 98
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""alias"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""ordered""
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 110
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""alias""
              },
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""writability"": ""runtime""
            },
            ""mutation"": ""RemoveAt"",
            ""args"": [
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
            ],
            ""source"": {
              ""line"": 1,
              ""column"": 137
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""ordered""
                  },
                  ""op"": ""ThenBy"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
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
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 185
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
              },
              ""source"": {
                ""line"": 1,
                ""column"": 177
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 156
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""ordered list mutation invalidates 5"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var ordered = [2,1].OrderBy((n) => n); List<int> alias = ordered; alias[0] = 9; return ordered.ThenBy((n) => 0); } } }"",
      ""expected"": null,
      ""expectedError"": ""requires an ordered result"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""ordered"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""readOnly"": true
              },
              ""pointer"": {
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
                    },
                    ""op"": ""OrderBy"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
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
                                ""type"": 2,
                                ""required"": true
                              },
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 106
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 98
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""alias"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""ordered""
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 110
            }
          },
          {
            ""type"": ""assign"",
            ""target"": {
              ""pointer"": {
                ""type"": ""keyOf"",
                ""keyOf"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""alias""
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
                }
              },
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""writability"": ""runtime""
            },
            ""operator"": ""="",
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 9
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 137
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""ordered""
                  },
                  ""op"": ""ThenBy"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
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
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 180
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
              },
              ""source"": {
                ""line"": 1,
                ""column"": 172
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 151
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""ordered list mutation invalidates 6"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var ordered = [2,1].OrderBy((n) => n); List<int> alias = ordered; alias.Sort((a,b) => b - a); return ordered.ThenBy((n) => 0); } } }"",
      ""expected"": null,
      ""expectedError"": ""requires an ordered result"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""ordered"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""readOnly"": true
              },
              ""pointer"": {
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
                    },
                    ""op"": ""OrderBy"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
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
                                ""type"": 2,
                                ""required"": true
                              },
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 106
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 98
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""alias"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""ordered""
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 110
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""alias""
              },
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""writability"": ""runtime""
            },
            ""mutation"": ""Sort"",
            ""args"": [],
            ""comparison"": {
              ""compilerRevision"": 17,
              ""source"": {
                ""uri"": ""Query.neo"",
                ""coordinateSpace"": ""file"",
                ""name"": ""Query.Result <callback>""
              },
              ""parameters"": [
                {
                  ""id"": ""a"",
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
                      ""value"": null
                    }
                  }
                },
                {
                  ""id"": ""b"",
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
                      ""value"": null
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
                      ""type"": ""arithmetic"",
                      ""arithmetic"": {
                        ""type"": ""-"",
                        ""pointers"": [
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""b""
                          },
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""a""
                          }
                        ],
                        ""numeric"": ""int""
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 157
                  }
                }
              ],
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 137
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""ordered""
                  },
                  ""op"": ""ThenBy"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
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
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 194
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
              },
              ""source"": {
                ""line"": 1,
                ""column"": 186
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 165
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""ordered set mutation invalidates 0"",
      ""source"": ""enum Tone { Red, Blue } class Query { public Set<int> Result { get { var ordered = [2,1].ToSet().OrderBy((n) => n); Set<int> alias = ordered; alias.Add(9); return ordered.ThenBy((n) => 0); } } }"",
      ""expected"": null,
      ""expectedError"": ""requires an ordered result"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""ordered"",
              ""typeInfo"": {
                ""type"": 9,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""collectionQuery"",
                  ""info"": {
                    ""collectionPointer"": {
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
                          },
                          ""op"": ""ToSet""
                        }
                      },
                      ""source"": {
                        ""line"": 1,
                        ""column"": 95
                      }
                    },
                    ""op"": ""OrderBy"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
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
                                ""type"": 2,
                                ""required"": true
                              },
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 113
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 105
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 70
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""alias"",
              ""typeInfo"": {
                ""type"": 9,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""ordered""
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 117
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""alias""
              },
              ""typeInfo"": {
                ""type"": 9,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""writability"": ""runtime""
            },
            ""mutation"": ""Add"",
            ""args"": [
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
            ],
            ""source"": {
              ""line"": 1,
              ""column"": 143
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""ordered""
                  },
                  ""op"": ""ThenBy"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
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
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 186
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
              },
              ""source"": {
                ""line"": 1,
                ""column"": 178
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 157
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 9,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          }
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""ordered set mutation invalidates 1"",
      ""source"": ""enum Tone { Red, Blue } class Query { public Set<int> Result { get { var ordered = [2,1].ToSet().OrderBy((n) => n); Set<int> alias = ordered; alias.Remove(1); return ordered.ThenBy((n) => 0); } } }"",
      ""expected"": null,
      ""expectedError"": ""requires an ordered result"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""ordered"",
              ""typeInfo"": {
                ""type"": 9,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""collectionQuery"",
                  ""info"": {
                    ""collectionPointer"": {
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
                          },
                          ""op"": ""ToSet""
                        }
                      },
                      ""source"": {
                        ""line"": 1,
                        ""column"": 95
                      }
                    },
                    ""op"": ""OrderBy"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
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
                                ""type"": 2,
                                ""required"": true
                              },
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 113
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 105
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 70
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""alias"",
              ""typeInfo"": {
                ""type"": 9,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""ordered""
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 117
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""alias""
              },
              ""typeInfo"": {
                ""type"": 9,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""writability"": ""runtime""
            },
            ""mutation"": ""Remove"",
            ""args"": [
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
            ],
            ""source"": {
              ""line"": 1,
              ""column"": 143
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""ordered""
                  },
                  ""op"": ""ThenBy"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
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
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 189
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
              },
              ""source"": {
                ""line"": 1,
                ""column"": 181
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 160
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 9,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          }
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""ordered set mutation invalidates 2"",
      ""source"": ""enum Tone { Red, Blue } class Query { public Set<int> Result { get { var ordered = [2,1].ToSet().OrderBy((n) => n); Set<int> alias = ordered; alias.Clear(); return ordered.ThenBy((n) => 0); } } }"",
      ""expected"": null,
      ""expectedError"": ""requires an ordered result"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""ordered"",
              ""typeInfo"": {
                ""type"": 9,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""collectionQuery"",
                  ""info"": {
                    ""collectionPointer"": {
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
                          },
                          ""op"": ""ToSet""
                        }
                      },
                      ""source"": {
                        ""line"": 1,
                        ""column"": 95
                      }
                    },
                    ""op"": ""OrderBy"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
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
                                ""type"": 2,
                                ""required"": true
                              },
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 113
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 105
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 70
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""alias"",
              ""typeInfo"": {
                ""type"": 9,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""ordered""
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 117
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""alias""
              },
              ""typeInfo"": {
                ""type"": 9,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""writability"": ""runtime""
            },
            ""mutation"": ""Clear"",
            ""args"": [],
            ""source"": {
              ""line"": 1,
              ""column"": 143
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""ordered""
                  },
                  ""op"": ""ThenBy"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
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
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 187
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
              },
              ""source"": {
                ""line"": 1,
                ""column"": 179
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 158
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 9,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          }
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""cached prior keys"",
      ""source"": ""enum Tone { Red, Blue } class Query { public bool Result { get { var counter = [0]; var ordered = [2,1].OrderBy((n) => { counter[0]++; return n; }); var next = ordered.ThenBy((n) => 0); return counter[0] == 2; } } }"",
      ""expected"": true,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""counter"",
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
                      ""value"": 0
                    }
                  }
                ]
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 66
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""ordered"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""readOnly"": true
              },
              ""pointer"": {
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
                    },
                    ""op"": ""OrderBy"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
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
                                ""type"": 2,
                                ""required"": true
                              },
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""assign"",
                          ""target"": {
                            ""pointer"": {
                              ""type"": ""keyOf"",
                              ""keyOf"": {
                                ""pointer"": {
                                  ""type"": ""variable"",
                                  ""variableId"": ""counter""
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
                              }
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
                                    ""type"": ""keyOf"",
                                    ""keyOf"": {
                                      ""pointer"": {
                                        ""type"": ""variable"",
                                        ""variableId"": ""counter""
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
                                ],
                                ""numeric"": ""int""
                              }
                            }
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 122
                          }
                        },
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 136
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 112
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 85
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""next"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""readOnly"": true
              },
              ""pointer"": {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""collectionQuery"",
                  ""info"": {
                    ""collectionPointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""ordered""
                    },
                    ""op"": ""ThenBy"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
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
                                ""type"": 2,
                                ""required"": true
                              },
                              ""value"": null
                            }
                          }
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
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 183
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 175
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 150
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
                      ""type"": ""keyOf"",
                      ""keyOf"": {
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""counter""
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
                      }
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
            ""source"": {
              ""line"": 1,
              ""column"": 187
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""set then by"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { Set<int> n = [21, 12, 11, 22].ToSet(); return n.OrderBy((v) => v / 10).ThenBy((v) => v).Distinct(); } } }"",
      ""expected"": [11, 12, 21, 22],
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""n"",
              ""typeInfo"": {
                ""type"": 9,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
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
                            ""value"": 21
                          }
                        },
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 12
                          }
                        },
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 11
                          }
                        },
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 22
                          }
                        }
                      ]
                    },
                    ""op"": ""ToSet""
                  }
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 106
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""collectionQuery"",
                      ""info"": {
                        ""collectionPointer"": {
                          ""type"": ""function"",
                          ""function"": {
                            ""type"": ""collectionQuery"",
                            ""info"": {
                              ""collectionPointer"": {
                                ""type"": ""variable"",
                                ""variableId"": ""n""
                              },
                              ""op"": ""OrderBy"",
                              ""function"": {
                                ""compilerRevision"": 17,
                                ""source"": {
                                  ""uri"": ""Query.neo"",
                                  ""coordinateSpace"": ""file"",
                                  ""name"": ""Query.Result <callback>""
                                },
                                ""parameters"": [
                                  {
                                    ""id"": ""v"",
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
                                        ""value"": null
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
                                        ""type"": ""arithmetic"",
                                        ""arithmetic"": {
                                          ""type"": ""/"",
                                          ""pointers"": [
                                            {
                                              ""type"": ""variable"",
                                              ""variableId"": ""v""
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
                                          ],
                                          ""numeric"": ""int""
                                        }
                                      }
                                    },
                                    ""source"": {
                                      ""line"": 1,
                                      ""column"": 134
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
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 126
                          }
                        },
                        ""op"": ""ThenBy"",
                        ""function"": {
                          ""compilerRevision"": 17,
                          ""source"": {
                            ""uri"": ""Query.neo"",
                            ""coordinateSpace"": ""file"",
                            ""name"": ""Query.Result <callback>""
                          },
                          ""parameters"": [
                            {
                              ""id"": ""v"",
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
                                  ""value"": null
                                }
                              }
                            }
                          ],
                          ""instructions"": [
                            {
                              ""type"": ""return"",
                              ""pointer"": {
                                ""type"": ""variable"",
                                ""variableId"": ""v""
                              },
                              ""source"": {
                                ""line"": 1,
                                ""column"": 156
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
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 148
                    }
                  },
                  ""op"": ""Distinct""
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 167
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 110
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""enum group"",
      ""source"": ""enum Tone { Red, Blue } class Query { public bool Result { get { Set<Tone> n = [Tone.Red, Tone.Blue].ToSet(); return n.GroupBy((v) => v)[Tone.Red].First() == Tone.Red; } } }"",
      ""expected"": true,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""n"",
              ""typeInfo"": {
                ""type"": 9,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 8,
                  ""required"": true,
                  ""enumId"": ""pending:enum:Query.neo:Tone""
                }
              },
              ""pointer"": {
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
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""pending:enum:Query.neo:Tone""
                        }
                      },
                      ""entries"": [
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 8,
                              ""required"": true,
                              ""enumId"": ""pending:enum:Query.neo:Tone""
                            },
                            ""value"": [
                              ""pending:enum-option:pending:enum:Query.neo:Tone:Red""
                            ]
                          }
                        },
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 8,
                              ""required"": true,
                              ""enumId"": ""pending:enum:Query.neo:Tone""
                            },
                            ""value"": [
                              ""pending:enum-option:pending:enum:Query.neo:Tone:Blue""
                            ]
                          }
                        }
                      ]
                    },
                    ""op"": ""ToSet"",
                    ""enumEntries"": true
                  }
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 107
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 66
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
                      ""type"": ""function"",
                      ""function"": {
                        ""type"": ""first"",
                        ""info"": {
                          ""collectionPointer"": {
                            ""type"": ""keyOf"",
                            ""keyOf"": {
                              ""pointer"": {
                                ""type"": ""function"",
                                ""function"": {
                                  ""type"": ""collectionQuery"",
                                  ""info"": {
                                    ""collectionPointer"": {
                                      ""type"": ""variable"",
                                      ""variableId"": ""n""
                                    },
                                    ""op"": ""GroupBy"",
                                    ""function"": {
                                      ""compilerRevision"": 17,
                                      ""source"": {
                                        ""uri"": ""Query.neo"",
                                        ""coordinateSpace"": ""file"",
                                        ""name"": ""Query.Result <callback>""
                                      },
                                      ""parameters"": [
                                        {
                                          ""id"": ""v"",
                                          ""typeInfo"": {
                                            ""type"": 8,
                                            ""required"": true,
                                            ""enumId"": ""pending:enum:Query.neo:Tone""
                                          },
                                          ""pointer"": {
                                            ""type"": ""value"",
                                            ""value"": {
                                              ""typeInfo"": {
                                                ""type"": 8,
                                                ""required"": true,
                                                ""enumId"": ""pending:enum:Query.neo:Tone""
                                              },
                                              ""value"": null
                                            }
                                          }
                                        }
                                      ],
                                      ""instructions"": [
                                        {
                                          ""type"": ""return"",
                                          ""pointer"": {
                                            ""type"": ""variable"",
                                            ""variableId"": ""v""
                                          },
                                          ""source"": {
                                            ""line"": 1,
                                            ""column"": 135
                                          }
                                        }
                                      ],
                                      ""typeInfo"": {
                                        ""type"": 8,
                                        ""required"": true,
                                        ""enumId"": ""pending:enum:Query.neo:Tone""
                                      }
                                    },
                                    ""enumKey"": true,
                                    ""enumEntries"": true
                                  }
                                },
                                ""source"": {
                                  ""line"": 1,
                                  ""column"": 127
                                }
                              },
                              ""key"": {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 3,
                                    ""required"": true
                                  },
                                  ""value"": ""pending:enum-option:pending:enum:Query.neo:Tone:Red""
                                }
                              }
                            }
                          }
                        }
                      },
                      ""source"": {
                        ""line"": 1,
                        ""column"": 153
                      }
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""pending:enum:Query.neo:Tone""
                        },
                        ""value"": [
                          ""pending:enum-option:pending:enum:Query.neo:Tone:Red""
                        ]
                      }
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 111
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [
            ""builtin:Root"",
            ""pending:class:Query.neo:Query"",
            ""pending:enum:Query.neo:Tone""
          ],
          ""typeNames"": [""Tone""]
        }
      }
    },
    {
      ""name"": ""enum dictionary"",
      ""source"": ""enum Tone { Red, Blue } class Query { public bool Result { get { Set<Tone> n = [Tone.Red, Tone.Blue].ToSet(); return n.ToDictionary((v) => v)[Tone.Blue] == Tone.Blue; } } }"",
      ""expected"": true,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""n"",
              ""typeInfo"": {
                ""type"": 9,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 8,
                  ""required"": true,
                  ""enumId"": ""pending:enum:Query.neo:Tone""
                }
              },
              ""pointer"": {
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
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""pending:enum:Query.neo:Tone""
                        }
                      },
                      ""entries"": [
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 8,
                              ""required"": true,
                              ""enumId"": ""pending:enum:Query.neo:Tone""
                            },
                            ""value"": [
                              ""pending:enum-option:pending:enum:Query.neo:Tone:Red""
                            ]
                          }
                        },
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 8,
                              ""required"": true,
                              ""enumId"": ""pending:enum:Query.neo:Tone""
                            },
                            ""value"": [
                              ""pending:enum-option:pending:enum:Query.neo:Tone:Blue""
                            ]
                          }
                        }
                      ]
                    },
                    ""op"": ""ToSet"",
                    ""enumEntries"": true
                  }
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 107
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 66
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
                      ""type"": ""keyOf"",
                      ""keyOf"": {
                        ""pointer"": {
                          ""type"": ""function"",
                          ""function"": {
                            ""type"": ""collectionQuery"",
                            ""info"": {
                              ""collectionPointer"": {
                                ""type"": ""variable"",
                                ""variableId"": ""n""
                              },
                              ""op"": ""ToDictionary"",
                              ""function"": {
                                ""compilerRevision"": 17,
                                ""source"": {
                                  ""uri"": ""Query.neo"",
                                  ""coordinateSpace"": ""file"",
                                  ""name"": ""Query.Result <callback>""
                                },
                                ""parameters"": [
                                  {
                                    ""id"": ""v"",
                                    ""typeInfo"": {
                                      ""type"": 8,
                                      ""required"": true,
                                      ""enumId"": ""pending:enum:Query.neo:Tone""
                                    },
                                    ""pointer"": {
                                      ""type"": ""value"",
                                      ""value"": {
                                        ""typeInfo"": {
                                          ""type"": 8,
                                          ""required"": true,
                                          ""enumId"": ""pending:enum:Query.neo:Tone""
                                        },
                                        ""value"": null
                                      }
                                    }
                                  }
                                ],
                                ""instructions"": [
                                  {
                                    ""type"": ""return"",
                                    ""pointer"": {
                                      ""type"": ""variable"",
                                      ""variableId"": ""v""
                                    },
                                    ""source"": {
                                      ""line"": 1,
                                      ""column"": 140
                                    }
                                  }
                                ],
                                ""typeInfo"": {
                                  ""type"": 8,
                                  ""required"": true,
                                  ""enumId"": ""pending:enum:Query.neo:Tone""
                                }
                              },
                              ""enumKey"": true,
                              ""enumEntries"": true
                            }
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 132
                          }
                        },
                        ""key"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 3,
                              ""required"": true
                            },
                            ""value"": ""pending:enum-option:pending:enum:Query.neo:Tone:Blue""
                          }
                        }
                      }
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""pending:enum:Query.neo:Tone""
                        },
                        ""value"": [
                          ""pending:enum-option:pending:enum:Query.neo:Tone:Blue""
                        ]
                      }
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 111
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [
            ""builtin:Root"",
            ""pending:class:Query.neo:Query"",
            ""pending:enum:Query.neo:Tone""
          ],
          ""typeNames"": [""Tone""]
        }
      }
    },
    {
      ""name"": ""enum fold single"",
      ""source"": ""enum Tone { Red, Blue } class Query { public bool Result { get { Set<Tone> n = [Tone.Red].ToSet(); return n.Aggregate((a, b) => b) == Tone.Red; } } }"",
      ""expected"": true,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""n"",
              ""typeInfo"": {
                ""type"": 9,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 8,
                  ""required"": true,
                  ""enumId"": ""pending:enum:Query.neo:Tone""
                }
              },
              ""pointer"": {
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
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""pending:enum:Query.neo:Tone""
                        }
                      },
                      ""entries"": [
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 8,
                              ""required"": true,
                              ""enumId"": ""pending:enum:Query.neo:Tone""
                            },
                            ""value"": [
                              ""pending:enum-option:pending:enum:Query.neo:Tone:Red""
                            ]
                          }
                        }
                      ]
                    },
                    ""op"": ""ToSet"",
                    ""enumEntries"": true
                  }
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 96
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 66
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
                      ""type"": ""function"",
                      ""function"": {
                        ""type"": ""collectionQuery"",
                        ""info"": {
                          ""collectionPointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          },
                          ""op"": ""Aggregate"",
                          ""function"": {
                            ""compilerRevision"": 17,
                            ""source"": {
                              ""uri"": ""Query.neo"",
                              ""coordinateSpace"": ""file"",
                              ""name"": ""Query.Result <callback>""
                            },
                            ""parameters"": [
                              {
                                ""id"": ""a"",
                                ""typeInfo"": {
                                  ""type"": 8,
                                  ""required"": true,
                                  ""enumId"": ""pending:enum:Query.neo:Tone""
                                },
                                ""pointer"": {
                                  ""type"": ""value"",
                                  ""value"": {
                                    ""typeInfo"": {
                                      ""type"": 8,
                                      ""required"": true,
                                      ""enumId"": ""pending:enum:Query.neo:Tone""
                                    },
                                    ""value"": null
                                  }
                                }
                              },
                              {
                                ""id"": ""b"",
                                ""typeInfo"": {
                                  ""type"": 8,
                                  ""required"": true,
                                  ""enumId"": ""pending:enum:Query.neo:Tone""
                                },
                                ""pointer"": {
                                  ""type"": ""value"",
                                  ""value"": {
                                    ""typeInfo"": {
                                      ""type"": 8,
                                      ""required"": true,
                                      ""enumId"": ""pending:enum:Query.neo:Tone""
                                    },
                                    ""value"": null
                                  }
                                }
                              }
                            ],
                            ""instructions"": [
                              {
                                ""type"": ""return"",
                                ""pointer"": {
                                  ""type"": ""variable"",
                                  ""variableId"": ""b""
                                },
                                ""source"": {
                                  ""line"": 1,
                                  ""column"": 129
                                }
                              }
                            ],
                            ""typeInfo"": {
                              ""type"": 8,
                              ""required"": true,
                              ""enumId"": ""pending:enum:Query.neo:Tone""
                            }
                          },
                          ""enumEntries"": true
                        }
                      },
                      ""source"": {
                        ""line"": 1,
                        ""column"": 118
                      }
                    },
                    ""operand2"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 8,
                          ""required"": true,
                          ""enumId"": ""pending:enum:Query.neo:Tone""
                        },
                        ""value"": [
                          ""pending:enum-option:pending:enum:Query.neo:Tone:Red""
                        ]
                      }
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 100
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [
            ""builtin:Root"",
            ""pending:class:Query.neo:Query"",
            ""pending:enum:Query.neo:Tone""
          ],
          ""typeNames"": [""Tone""]
        }
      }
    },
    {
      ""name"": ""min NaN stops"",
      ""source"": ""enum Tone { Red, Blue } class Query { public bool Result { get { var value = [1, 2, 3].Min((n) => { if (n > 1) throw \""visited past NaN\""; return Math.Sqrt(-1); }); return value != value; } } }"",
      ""expected"": true,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""value"",
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""pointer"": {
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
                    ""op"": ""Min"",
                    ""keyType"": ""float"",
                    ""nullable"": false,
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
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
                                ""type"": 2,
                                ""required"": true
                              },
                              ""value"": null
                            }
                          }
                        }
                      ],
                      ""instructions"": [
                        {
                          ""type"": ""if"",
                          ""branches"": [
                            {
                              ""expression"": {
                                ""condition"": {
                                  ""type"": ""greaterThan"",
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
                                      ""value"": ""visited past NaN""
                                    }
                                  },
                                  ""source"": {
                                    ""line"": 1,
                                    ""column"": 112
                                  }
                                }
                              ]
                            }
                          ],
                          ""else"": null,
                          ""source"": {
                            ""line"": 1,
                            ""column"": 101
                          }
                        },
                        {
                          ""type"": ""return"",
                          ""pointer"": {
                            ""type"": ""function"",
                            ""function"": {
                              ""type"": ""mathOp"",
                              ""info"": {
                                ""op"": ""sqrt"",
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
                                                ""type"": 2,
                                                ""required"": true
                                              },
                                              ""value"": -1
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
                                        ],
                                        ""numeric"": ""int""
                                      }
                                    }
                                  }
                                ]
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 154
                            }
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 138
                          }
                        }
                      ],
                      ""typeInfo"": {
                        ""type"": 4,
                        ""required"": true
                      }
                    }
                  }
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 91
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 66
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
                    ""type"": ""doesNotEqual"",
                    ""operand1"": {
                      ""type"": ""variable"",
                      ""variableId"": ""value""
                    },
                    ""operand2"": {
                      ""type"": ""variable"",
                      ""variableId"": ""value""
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 164
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 1,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""max ignores NaN"",
      ""source"": ""enum Tone { Red, Blue } class Query { public float Result => [1, 2, 3].Max((n) => n == 2 ? Math.Sqrt(-1) : 1.0 * n); }"",
      ""expected"": 3,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""Max"",
                  ""keyType"": ""float"",
                  ""nullable"": false,
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
                      }
                    ],
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
                            ""type"": ""function"",
                            ""function"": {
                              ""type"": ""mathOp"",
                              ""info"": {
                                ""op"": ""sqrt"",
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
                                                ""type"": 2,
                                                ""required"": true
                                              },
                                              ""value"": -1
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
                                        ],
                                        ""numeric"": ""int""
                                      }
                                    }
                                  }
                                ]
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 101
                            }
                          },
                          ""whenFalse"": {
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
                                      ""value"": 1
                                    }
                                  },
                                  {
                                    ""type"": ""variable"",
                                    ""variableId"": ""n""
                                  }
                                ],
                                ""numeric"": ""float""
                              }
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 83
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 4,
                      ""required"": true
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 75
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 62
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 4,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""sort failure cleanup"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var n = [3, 1, 2]; try { n.Sort((a, b) => { throw \""failed\""; return a - b; }); } catch (string error) { } n.Sort(); return n; } } }"",
      ""expected"": [1, 2, 3],
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""n"",
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
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""try"",
            ""instructions"": [
              {
                ""type"": ""collectionCall"",
                ""target"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""n""
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
                ""mutation"": ""Sort"",
                ""args"": [],
                ""comparison"": {
                  ""compilerRevision"": 17,
                  ""source"": {
                    ""uri"": ""Query.neo"",
                    ""coordinateSpace"": ""file"",
                    ""name"": ""Query.Result <callback>""
                  },
                  ""parameters"": [
                    {
                      ""id"": ""a"",
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
                          ""value"": null
                        }
                      }
                    },
                    {
                      ""id"": ""b"",
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
                          ""value"": null
                        }
                      }
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
                          ""value"": ""failed""
                        }
                      },
                      ""source"": {
                        ""line"": 1,
                        ""column"": 115
                      }
                    },
                    {
                      ""type"": ""return"",
                      ""pointer"": {
                        ""type"": ""operation"",
                        ""operation"": {
                          ""type"": ""arithmetic"",
                          ""arithmetic"": {
                            ""type"": ""-"",
                            ""pointers"": [
                              {
                                ""type"": ""variable"",
                                ""variableId"": ""a""
                              },
                              {
                                ""type"": ""variable"",
                                ""variableId"": ""b""
                              }
                            ],
                            ""numeric"": ""int""
                          }
                        }
                      },
                      ""source"": {
                        ""line"": 1,
                        ""column"": 131
                      }
                    }
                  ],
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 96
                }
              }
            ],
            ""catches"": [
              {
                ""binding"": {
                  ""id"": ""__catch:0:error"",
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""readonly"": true
                },
                ""instructions"": []
              }
            ],
            ""source"": {
              ""line"": 1,
              ""column"": 90
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""n""
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
            ""mutation"": ""Sort"",
            ""args"": [],
            ""keyType"": ""int"",
            ""source"": {
              ""line"": 1,
              ""column"": 176
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""n""
            },
            ""source"": {
              ""line"": 1,
              ""column"": 186
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""sum"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int Result => [1, 2, 3].Sum(); }"",
      ""expected"": 6,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""Sum"",
                  ""keyType"": ""int"",
                  ""nullable"": false
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 73
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 60
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""sum selector"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int Result => [1, 2, 3].Sum((n) => n * 2); }"",
      ""expected"": 12,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""Sum"",
                  ""keyType"": ""int"",
                  ""nullable"": false,
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
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
                            ""type"": ""arithmetic"",
                            ""arithmetic"": {
                              ""type"": ""*"",
                              ""pointers"": [
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""n""
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
                              ],
                              ""numeric"": ""int""
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 81
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 73
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 60
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""sum empty"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int Result => List.Repeat(1, 0).Sum(); }"",
      ""expected"": 0,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
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
                            ""value"": 1
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
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 71
                    }
                  },
                  ""op"": ""Sum"",
                  ""keyType"": ""int"",
                  ""nullable"": false
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 81
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 60
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""sum nullable"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int? Result => [1, 2, 3].Sum((n) => n == 2 ? null : n); }"",
      ""expected"": 4,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""Sum"",
                  ""keyType"": ""int"",
                  ""nullable"": true,
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
                      }
                    ],
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
                                ""type"": 2,
                                ""required"": false
                              },
                              ""value"": null
                            }
                          },
                          ""whenFalse"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 82
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": false
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 74
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 61
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": false
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""sum all null"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int? Result => [1, 2].Sum((n) => n > 0 ? null : n); }"",
      ""expected"": 0,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""Sum"",
                  ""keyType"": ""int"",
                  ""nullable"": true,
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
                      }
                    ],
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
                                  ""type"": ""greaterThan"",
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
                                ""type"": 2,
                                ""required"": false
                              },
                              ""value"": null
                            }
                          },
                          ""whenFalse"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 79
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": false
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 71
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 61
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": false
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""float sum"",
      ""source"": ""enum Tone { Red, Blue } class Query { public float Result => [0.1, 0.2].Sum(); }"",
      ""expected"": 0.30000001192092896,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                        ""type"": 4,
                        ""required"": true
                      }
                    },
                    ""entries"": [
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
                            ""type"": 4,
                            ""required"": true
                          },
                          ""value"": 0.20000000298023224
                        }
                      }
                    ]
                  },
                  ""op"": ""Sum"",
                  ""keyType"": ""float"",
                  ""nullable"": false
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 76
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 62
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 4,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""average"",
      ""source"": ""enum Tone { Red, Blue } class Query { public float Result => [1, 2].Average(); }"",
      ""expected"": 1.5,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""Average"",
                  ""keyType"": ""int"",
                  ""nullable"": false
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 76
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 62
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 4,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""average nullable"",
      ""source"": ""enum Tone { Red, Blue } class Query { public float? Result => [1, 2, 3].Average((n) => n == 2 ? null : n); }"",
      ""expected"": 2,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""Average"",
                  ""keyType"": ""int"",
                  ""nullable"": true,
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
                      }
                    ],
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
                                ""type"": 2,
                                ""required"": false
                              },
                              ""value"": null
                            }
                          },
                          ""whenFalse"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 88
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": false
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 80
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 63
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 4,
          ""required"": false
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""average all null"",
      ""source"": ""enum Tone { Red, Blue } class Query { public float? Result => [1, 2].Average((n) => n > 0 ? null : n); }"",
      ""expected"": null,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""Average"",
                  ""keyType"": ""int"",
                  ""nullable"": true,
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
                      }
                    ],
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
                                  ""type"": ""greaterThan"",
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
                                ""type"": 2,
                                ""required"": false
                              },
                              ""value"": null
                            }
                          },
                          ""whenFalse"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 85
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": false
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 77
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 63
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 4,
          ""required"": false
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""average empty"",
      ""source"": ""enum Tone { Red, Blue } class Query { public float Result => List.Repeat(1, 0).Average(); }"",
      ""expected"": null,
      ""expectedError"": ""Average() found no matching entry."",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
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
                            ""value"": 1
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
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 73
                    }
                  },
                  ""op"": ""Average"",
                  ""keyType"": ""int"",
                  ""nullable"": false
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 87
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 62
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 4,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""decimal sum"",
      ""source"": ""enum Tone { Red, Blue } class Query { public decimal Result { get { List<decimal> n = [1.1, 2.2]; return n.Sum(); } } }"",
      ""expected"": ""3.3"",
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""n"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 20,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""listLiteral"",
                ""typeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  }
                },
                ""entries"": [
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
                      ""value"": ""2.2""
                    }
                  }
                ]
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 69
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""n""
                  },
                  ""op"": ""Sum"",
                  ""keyType"": ""decimal"",
                  ""nullable"": false
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 111
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 99
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 20,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""decimal average"",
      ""source"": ""enum Tone { Red, Blue } class Query { public decimal Result { get { List<decimal> n = [1, 0, 0]; return n.Average(); } } }"",
      ""expected"": ""0.3333333333333333333333333333"",
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""n"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 20,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""listLiteral"",
                ""typeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  }
                },
                ""entries"": [
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
                      ""value"": ""0""
                    }
                  }
                ]
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 69
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""n""
                  },
                  ""op"": ""Average"",
                  ""keyType"": ""decimal"",
                  ""nullable"": false
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 114
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 98
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 20,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""decimal average whole"",
      ""source"": ""enum Tone { Red, Blue } class Query { public decimal Result { get { List<decimal> n = [12, 13]; return n.Average(); } } }"",
      ""expected"": ""12.50000000000000000000000000"",
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""n"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 20,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""listLiteral"",
                ""typeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  }
                },
                ""entries"": [
                  {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 20,
                        ""required"": true
                      },
                      ""value"": ""12""
                    }
                  },
                  {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 20,
                        ""required"": true
                      },
                      ""value"": ""13""
                    }
                  }
                ]
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 69
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""n""
                  },
                  ""op"": ""Average"",
                  ""keyType"": ""decimal"",
                  ""nullable"": false
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 113
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 97
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 20,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""min selector"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int Result => [3, 1, 2].Min((n) => -n); }"",
      ""expected"": -3,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""Min"",
                  ""keyType"": ""int"",
                  ""nullable"": false,
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
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
                            ""type"": ""arithmetic"",
                            ""arithmetic"": {
                              ""type"": ""*"",
                              ""pointers"": [
                                {
                                  ""type"": ""value"",
                                  ""value"": {
                                    ""typeInfo"": {
                                      ""type"": 2,
                                      ""required"": true
                                    },
                                    ""value"": -1
                                  }
                                },
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""n""
                                }
                              ],
                              ""numeric"": ""int""
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 81
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 73
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 60
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""min nullable"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int? Result => [1, 2].Min((n) => n == 1 ? null : n); }"",
      ""expected"": 2,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""Min"",
                  ""keyType"": ""int"",
                  ""nullable"": true,
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
                      }
                    ],
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
                                      ""value"": 1
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
                                ""type"": 2,
                                ""required"": false
                              },
                              ""value"": null
                            }
                          },
                          ""whenFalse"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 79
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": false
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 71
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 61
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": false
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""max"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int Result => [3, 1, 2].Max(); }"",
      ""expected"": 3,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""Max"",
                  ""keyType"": ""int"",
                  ""nullable"": false
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 73
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 60
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""min empty"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int Result => List.Repeat(1, 0).Min(); }"",
      ""expected"": null,
      ""expectedError"": ""Min() found no matching entry."",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
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
                            ""value"": 1
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
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 71
                    }
                  },
                  ""op"": ""Min"",
                  ""keyType"": ""int"",
                  ""nullable"": false
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 81
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 60
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""min by"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int Result => [3, 1, 2].MinBy((n) => -n); }"",
      ""expected"": 3,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""MinBy"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
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
                            ""type"": ""arithmetic"",
                            ""arithmetic"": {
                              ""type"": ""*"",
                              ""pointers"": [
                                {
                                  ""type"": ""value"",
                                  ""value"": {
                                    ""typeInfo"": {
                                      ""type"": 2,
                                      ""required"": true
                                    },
                                    ""value"": -1
                                  }
                                },
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""n""
                                }
                              ],
                              ""numeric"": ""int""
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 83
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  ""keyType"": ""int"",
                  ""nullable"": false
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 75
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 60
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""max by ties"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int Result => [11, 21, 12].MaxBy((n) => n % 10); }"",
      ""expected"": 12,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                          ""value"": 11
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 21
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 12
                        }
                      }
                    ]
                  },
                  ""op"": ""MaxBy"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
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
                            ""type"": ""arithmetic"",
                            ""arithmetic"": {
                              ""type"": ""%"",
                              ""pointers"": [
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""n""
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
                              ],
                              ""numeric"": ""int""
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 86
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  ""keyType"": ""int"",
                  ""nullable"": false
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 78
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 60
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""min by all null"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int Result => [3, 1, 2].MinBy((n) => n > 0 ? null : n); }"",
      ""expected"": 3,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""MinBy"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
                      }
                    ],
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
                                  ""type"": ""greaterThan"",
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
                                ""type"": 2,
                                ""required"": false
                              },
                              ""value"": null
                            }
                          },
                          ""whenFalse"": {
                            ""type"": ""variable"",
                            ""variableId"": ""n""
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 83
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": false
                    }
                  },
                  ""keyType"": ""int"",
                  ""nullable"": false
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 75
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 60
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""aggregate"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int Result => [1, 2, 3].Aggregate((a, n) => a + n); }"",
      ""expected"": 6,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""Aggregate"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
                    ""parameters"": [
                      {
                        ""id"": ""a"",
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
                            ""value"": null
                          }
                        }
                      },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
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
                            ""type"": ""arithmetic"",
                            ""arithmetic"": {
                              ""type"": ""+"",
                              ""pointers"": [
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""a""
                                },
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""n""
                                }
                              ],
                              ""numeric"": ""int""
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 90
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 79
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 60
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""aggregate seeded"",
      ""source"": ""enum Tone { Red, Blue } class Query { public string Result => [1, 2].Aggregate(\""x\"", (a, n) => a + n); }"",
      ""expected"": ""x12"",
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""Aggregate"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
                    ""parameters"": [
                      {
                        ""id"": ""a"",
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
                            ""value"": null
                          }
                        }
                      },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
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
                            ""type"": ""arithmetic"",
                            ""arithmetic"": {
                              ""type"": ""+"",
                              ""pointers"": [
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""a""
                                },
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""n""
                                }
                              ]
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 95
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    }
                  },
                  ""seedPointer"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""value"": ""x""
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 79
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 63
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""aggregate empty seeded"",
      ""source"": ""enum Tone { Red, Blue } class Query { public string Result => List.Repeat(1, 0).Aggregate(\""x\"", (a, n) => a + n); }"",
      ""expected"": ""x"",
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
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
                            ""value"": 1
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
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 74
                    }
                  },
                  ""op"": ""Aggregate"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
                    ""parameters"": [
                      {
                        ""id"": ""a"",
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
                            ""value"": null
                          }
                        }
                      },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
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
                            ""type"": ""arithmetic"",
                            ""arithmetic"": {
                              ""type"": ""+"",
                              ""pointers"": [
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""a""
                                },
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""n""
                                }
                              ]
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 106
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    }
                  },
                  ""seedPointer"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""value"": ""x""
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 90
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 63
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 3,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""aggregate empty"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int Result => List.Repeat(1, 0).Aggregate((a, n) => a + n); }"",
      ""expected"": null,
      ""expectedError"": ""Aggregate() found no matching entry."",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
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
                            ""value"": 1
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
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 71
                    }
                  },
                  ""op"": ""Aggregate"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
                    ""parameters"": [
                      {
                        ""id"": ""a"",
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
                            ""value"": null
                          }
                        }
                      },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
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
                            ""type"": ""arithmetic"",
                            ""arithmetic"": {
                              ""type"": ""+"",
                              ""pointers"": [
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""a""
                                },
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""n""
                                }
                              ],
                              ""numeric"": ""int""
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 98
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 87
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 60
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""distinct"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result => [1, 2, 1, 3].Distinct(); }"",
      ""expected"": [1, 2, 3],
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                          ""value"": 3
                        }
                      }
                    ]
                  },
                  ""op"": ""Distinct""
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 87
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 66
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""distinct null"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int?> Result { get { List<int?> n = [null, 1, null, 1]; return n.Distinct(); } } }"",
      ""expected"": [null, 1],
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""n"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": false
                }
              },
              ""pointer"": {
                ""type"": ""listLiteral"",
                ""typeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 2,
                    ""required"": false
                  }
                },
                ""entries"": [
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
                        ""required"": true
                      },
                      ""value"": 1
                    }
                  }
                ]
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 72
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""n""
                  },
                  ""op"": ""Distinct""
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 124
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 107
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": false
          }
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""distinct decimal"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<decimal> Result { get { List<decimal> n = [1.10, 1.1, 2]; return n.Distinct(); } } }"",
      ""expected"": [""1.10"", ""2""],
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""n"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 20,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""listLiteral"",
                ""typeInfo"": {
                  ""type"": 6,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  }
                },
                ""entries"": [
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
                ]
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 75
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""n""
                  },
                  ""op"": ""Distinct"",
                  ""decimalEntries"": true
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 126
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 109
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 20,
            ""required"": true
          }
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""zip"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result => [1, 2, 3].Zip([10, 20], (a, b) => a + b); }"",
      ""expected"": [11, 22],
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""Zip"",
                  ""otherPointer"": {
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
                          ""value"": 10
                        }
                      },
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 20
                        }
                      }
                    ]
                  },
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
                    ""parameters"": [
                      {
                        ""id"": ""a"",
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
                            ""value"": null
                          }
                        }
                      },
                      {
                        ""id"": ""b"",
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
                            ""value"": null
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
                            ""type"": ""arithmetic"",
                            ""arithmetic"": {
                              ""type"": ""+"",
                              ""pointers"": [
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""a""
                                },
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""b""
                                }
                              ],
                              ""numeric"": ""int""
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 100
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 79
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 66
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""then descending"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result => [21, 12, 11, 22].OrderBy((n) => n / 10).ThenByDescending((n) => n % 10); }"",
      ""expected"": [12, 11, 22, 21],
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
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
                                ""value"": 21
                              }
                            },
                            {
                              ""type"": ""value"",
                              ""value"": {
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                },
                                ""value"": 12
                              }
                            },
                            {
                              ""type"": ""value"",
                              ""value"": {
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                },
                                ""value"": 11
                              }
                            },
                            {
                              ""type"": ""value"",
                              ""value"": {
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                },
                                ""value"": 22
                              }
                            }
                          ]
                        },
                        ""op"": ""OrderBy"",
                        ""function"": {
                          ""compilerRevision"": 17,
                          ""source"": {
                            ""uri"": ""Query.neo"",
                            ""coordinateSpace"": ""file"",
                            ""name"": ""Query.Result <callback>""
                          },
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
                                    ""type"": 2,
                                    ""required"": true
                                  },
                                  ""value"": null
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
                                  ""type"": ""arithmetic"",
                                  ""arithmetic"": {
                                    ""type"": ""/"",
                                    ""pointers"": [
                                      {
                                        ""type"": ""variable"",
                                        ""variableId"": ""n""
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
                                    ],
                                    ""numeric"": ""int""
                                  }
                                }
                              },
                              ""source"": {
                                ""line"": 1,
                                ""column"": 98
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
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 90
                    }
                  },
                  ""op"": ""ThenByDescending"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
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
                            ""type"": ""arithmetic"",
                            ""arithmetic"": {
                              ""type"": ""%"",
                              ""pointers"": [
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""n""
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
                              ],
                              ""numeric"": ""int""
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 130
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
              },
              ""source"": {
                ""line"": 1,
                ""column"": 122
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 66
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""ordered local"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var ordered = [21, 12, 11, 22].OrderBy((n) => n / 10); return ordered.ThenBy((n) => n); } } }"",
      ""expected"": [11, 12, 21, 22],
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""ordered"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""readOnly"": true
              },
              ""pointer"": {
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
                            ""value"": 21
                          }
                        },
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 12
                          }
                        },
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 11
                          }
                        },
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 22
                          }
                        }
                      ]
                    },
                    ""op"": ""OrderBy"",
                    ""function"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Query.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Query.Result <callback>""
                      },
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
                                ""type"": 2,
                                ""required"": true
                              },
                              ""value"": null
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
                              ""type"": ""arithmetic"",
                              ""arithmetic"": {
                                ""type"": ""/"",
                                ""pointers"": [
                                  {
                                    ""type"": ""variable"",
                                    ""variableId"": ""n""
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
                                ],
                                ""numeric"": ""int""
                              }
                            }
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 117
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
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 109
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""collectionPointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""ordered""
                  },
                  ""op"": ""ThenBy"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
                      }
                    ],
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""n""
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 155
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
              },
              ""source"": {
                ""line"": 1,
                ""column"": 147
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 126
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""dictionary keys"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<string> Result => {\""b\"": 1, \""a\"": 2}.Keys; }"",
      ""expected"": [""b"", ""a""],
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""select"",
                ""info"": {
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
                            ""value"": ""b""
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
                            ""value"": ""a""
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
                    ""source"": {
                      ""name"": ""Query.Result"",
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""parameters"": [
                      {
                        ""id"": ""__foreach_key__"",
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
                            ""value"": null
                          }
                        }
                      },
                      {
                        ""id"": ""__foreach_value__"",
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
                            ""value"": null
                          }
                        }
                      }
                    ],
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""__foreach_key__""
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 85
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 69
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 3,
            ""required"": true
          }
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""dictionary values"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result => {\""b\"": 1, \""a\"": 2}.Values; }"",
      ""expected"": [1, 2],
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""collectionQuery"",
                ""info"": {
                  ""op"": ""Values"",
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
                            ""value"": ""b""
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
                            ""value"": ""a""
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
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 82
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 66
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""dictionary projection"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int Result => [1, 2].ToDictionary((n) => \""k\"" + n, (n) => n * 3)[\""k2\""]; }"",
      ""expected"": 6,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""keyOf"",
              ""keyOf"": {
                ""pointer"": {
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
                      ""op"": ""ToDictionary"",
                      ""function"": {
                        ""compilerRevision"": 17,
                        ""source"": {
                          ""uri"": ""Query.neo"",
                          ""coordinateSpace"": ""file"",
                          ""name"": ""Query.Result <callback>""
                        },
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
                                  ""type"": 2,
                                  ""required"": true
                                },
                                ""value"": null
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
                                ""type"": ""arithmetic"",
                                ""arithmetic"": {
                                  ""type"": ""+"",
                                  ""pointers"": [
                                    {
                                      ""type"": ""value"",
                                      ""value"": {
                                        ""typeInfo"": {
                                          ""type"": 3,
                                          ""required"": true
                                        },
                                        ""value"": ""k""
                                      }
                                    },
                                    {
                                      ""type"": ""variable"",
                                      ""variableId"": ""n""
                                    }
                                  ]
                                }
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 87
                            }
                          }
                        ],
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        }
                      },
                      ""valueFunction"": {
                        ""compilerRevision"": 17,
                        ""source"": {
                          ""uri"": ""Query.neo"",
                          ""coordinateSpace"": ""file"",
                          ""name"": ""Query.Result <callback>""
                        },
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
                                  ""type"": 2,
                                  ""required"": true
                                },
                                ""value"": null
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
                                ""type"": ""arithmetic"",
                                ""arithmetic"": {
                                  ""type"": ""*"",
                                  ""pointers"": [
                                    {
                                      ""type"": ""variable"",
                                      ""variableId"": ""n""
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
                                  ],
                                  ""numeric"": ""int""
                                }
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 103
                            }
                          }
                        ],
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 79
                  }
                },
                ""key"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""k2""
                  }
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 60
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""dictionary duplicate"",
      ""source"": ""enum Tone { Red, Blue } class Query { public Dictionary<string, int> Result => [1, 2].ToDictionary((n) => \""same\""); }"",
      ""expected"": null,
      ""expectedError"": ""ToDictionary() found duplicate key 'same'."",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
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
                  ""op"": ""ToDictionary"",
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file"",
                      ""name"": ""Query.Result <callback>""
                    },
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
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": null
                          }
                        }
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
                            ""value"": ""same""
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 107
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 99
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 80
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 5,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          }
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""dictionary special key"",
      ""source"": ""enum Tone { Red, Blue } class Query { public int Result => [1].ToDictionary((n) => \""__proto__\"")[\""__proto__\""]; }"",
      ""expected"": 1,
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""keyOf"",
              ""keyOf"": {
                ""pointer"": {
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
                      ""op"": ""ToDictionary"",
                      ""function"": {
                        ""compilerRevision"": 17,
                        ""source"": {
                          ""uri"": ""Query.neo"",
                          ""coordinateSpace"": ""file"",
                          ""name"": ""Query.Result <callback>""
                        },
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
                                  ""type"": 2,
                                  ""required"": true
                                },
                                ""value"": null
                              }
                            }
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
                                ""value"": ""__proto__""
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 84
                            }
                          }
                        ],
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        }
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 76
                  }
                },
                ""key"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""__proto__""
                  }
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 60
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""group by"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result => [1, 2, 3].GroupBy((n) => n == 2 ? \""even\"" : \""odd\"")[\""odd\""]; }"",
      ""expected"": [1, 3],
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""keyOf"",
              ""keyOf"": {
                ""pointer"": {
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
                      ""op"": ""GroupBy"",
                      ""function"": {
                        ""compilerRevision"": 17,
                        ""source"": {
                          ""uri"": ""Query.neo"",
                          ""coordinateSpace"": ""file"",
                          ""name"": ""Query.Result <callback>""
                        },
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
                                  ""type"": 2,
                                  ""required"": true
                                },
                                ""value"": null
                              }
                            }
                          }
                        ],
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
                                    ""type"": 3,
                                    ""required"": true
                                  },
                                  ""value"": ""even""
                                }
                              },
                              ""whenFalse"": {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 3,
                                    ""required"": true
                                  },
                                  ""value"": ""odd""
                                }
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 91
                            }
                          }
                        ],
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        }
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 83
                  }
                },
                ""key"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""odd""
                  }
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 66
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""dictionary numeric key order"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<string> Result => [2, 10, 1].GroupBy((n) => \""\"" + n).Keys; }"",
      ""expected"": [""1"", ""2"", ""10""],
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""select"",
                ""info"": {
                  ""collectionPointer"": {
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
                                ""value"": 10
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
                        },
                        ""op"": ""GroupBy"",
                        ""function"": {
                          ""compilerRevision"": 17,
                          ""source"": {
                            ""uri"": ""Query.neo"",
                            ""coordinateSpace"": ""file"",
                            ""name"": ""Query.Result <callback>""
                          },
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
                                    ""type"": 2,
                                    ""required"": true
                                  },
                                  ""value"": null
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
                                  ""type"": ""arithmetic"",
                                  ""arithmetic"": {
                                    ""type"": ""+"",
                                    ""pointers"": [
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
                                        ""type"": ""variable"",
                                        ""variableId"": ""n""
                                      }
                                    ]
                                  }
                                }
                              },
                              ""source"": {
                                ""line"": 1,
                                ""column"": 95
                              }
                            }
                          ],
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          }
                        }
                      }
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 87
                    }
                  },
                  ""function"": {
                    ""compilerRevision"": 17,
                    ""source"": {
                      ""name"": ""Query.Result"",
                      ""uri"": ""Query.neo"",
                      ""coordinateSpace"": ""file""
                    },
                    ""parameters"": [
                      {
                        ""id"": ""__foreach_key__"",
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
                            ""value"": null
                          }
                        }
                      },
                      {
                        ""id"": ""__foreach_value__"",
                        ""typeInfo"": {
                          ""type"": 6,
                          ""required"": true,
                          ""entryTypeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""readOnly"": true
                        },
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 6,
                              ""required"": true,
                              ""entryTypeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              },
                              ""readOnly"": true
                            },
                            ""value"": null
                          }
                        }
                      }
                    ],
                    ""instructions"": [
                      {
                        ""type"": ""return"",
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""__foreach_key__""
                        }
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 102
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 69
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 3,
            ""required"": true
          }
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""sort"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var list = [3, 1, 2]; list.Sort(); return list; } } }"",
      ""expected"": [1, 2, 3],
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""list"",
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
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""list""
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
            ""mutation"": ""Sort"",
            ""args"": [],
            ""keyType"": ""int"",
            ""source"": {
              ""line"": 1,
              ""column"": 93
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""list""
            },
            ""source"": {
              ""line"": 1,
              ""column"": 106
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""sort comparison"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var list = [3, 1, 2]; list.Sort((a, b) => b - a); return list; } } }"",
      ""expected"": [3, 2, 1],
      ""expectedError"": null,
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""list"",
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
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""list""
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
            ""mutation"": ""Sort"",
            ""args"": [],
            ""comparison"": {
              ""compilerRevision"": 17,
              ""source"": {
                ""uri"": ""Query.neo"",
                ""coordinateSpace"": ""file"",
                ""name"": ""Query.Result <callback>""
              },
              ""parameters"": [
                {
                  ""id"": ""a"",
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
                      ""value"": null
                    }
                  }
                },
                {
                  ""id"": ""b"",
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
                      ""value"": null
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
                      ""type"": ""arithmetic"",
                      ""arithmetic"": {
                        ""type"": ""-"",
                        ""pointers"": [
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""b""
                          },
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""a""
                          }
                        ],
                        ""numeric"": ""int""
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 113
                  }
                }
              ],
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 93
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""list""
            },
            ""source"": {
              ""line"": 1,
              ""column"": 121
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""sort alias guard 0"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var list = [3, 1, 2]; var alias = list; list.Sort((a, b) => { alias.Add(9); return a - b; }); return list; } } }"",
      ""expected"": null,
      ""expectedError"": ""while its Sort comparison"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""list"",
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
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""alias"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""list""
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 93
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""list""
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
            ""mutation"": ""Sort"",
            ""args"": [],
            ""comparison"": {
              ""compilerRevision"": 17,
              ""source"": {
                ""uri"": ""Query.neo"",
                ""coordinateSpace"": ""file"",
                ""name"": ""Query.Result <callback>""
              },
              ""parameters"": [
                {
                  ""id"": ""a"",
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
                      ""value"": null
                    }
                  }
                },
                {
                  ""id"": ""b"",
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
                      ""value"": null
                    }
                  }
                }
              ],
              ""instructions"": [
                {
                  ""type"": ""collectionCall"",
                  ""target"": {
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""alias""
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
                  ""mutation"": ""Add"",
                  ""args"": [
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
                  ],
                  ""source"": {
                    ""line"": 1,
                    ""column"": 133
                  }
                },
                {
                  ""type"": ""return"",
                  ""pointer"": {
                    ""type"": ""operation"",
                    ""operation"": {
                      ""type"": ""arithmetic"",
                      ""arithmetic"": {
                        ""type"": ""-"",
                        ""pointers"": [
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""a""
                          },
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""b""
                          }
                        ],
                        ""numeric"": ""int""
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 147
                  }
                }
              ],
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 111
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""list""
            },
            ""source"": {
              ""line"": 1,
              ""column"": 165
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""sort alias guard 1"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var list = [3, 1, 2]; var alias = list; list.Sort((a, b) => { alias.Remove(1); return a - b; }); return list; } } }"",
      ""expected"": null,
      ""expectedError"": ""while its Sort comparison"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""list"",
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
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""alias"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""list""
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 93
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""list""
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
            ""mutation"": ""Sort"",
            ""args"": [],
            ""comparison"": {
              ""compilerRevision"": 17,
              ""source"": {
                ""uri"": ""Query.neo"",
                ""coordinateSpace"": ""file"",
                ""name"": ""Query.Result <callback>""
              },
              ""parameters"": [
                {
                  ""id"": ""a"",
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
                      ""value"": null
                    }
                  }
                },
                {
                  ""id"": ""b"",
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
                      ""value"": null
                    }
                  }
                }
              ],
              ""instructions"": [
                {
                  ""type"": ""collectionCall"",
                  ""target"": {
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""alias""
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
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 1
                      }
                    }
                  ],
                  ""source"": {
                    ""line"": 1,
                    ""column"": 133
                  }
                },
                {
                  ""type"": ""return"",
                  ""pointer"": {
                    ""type"": ""operation"",
                    ""operation"": {
                      ""type"": ""arithmetic"",
                      ""arithmetic"": {
                        ""type"": ""-"",
                        ""pointers"": [
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""a""
                          },
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""b""
                          }
                        ],
                        ""numeric"": ""int""
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 150
                  }
                }
              ],
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 111
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""list""
            },
            ""source"": {
              ""line"": 1,
              ""column"": 168
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""sort alias guard 2"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var list = [3, 1, 2]; var alias = list; list.Sort((a, b) => { alias.Clear(); return a - b; }); return list; } } }"",
      ""expected"": null,
      ""expectedError"": ""while its Sort comparison"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""list"",
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
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""alias"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""list""
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 93
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""list""
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
            ""mutation"": ""Sort"",
            ""args"": [],
            ""comparison"": {
              ""compilerRevision"": 17,
              ""source"": {
                ""uri"": ""Query.neo"",
                ""coordinateSpace"": ""file"",
                ""name"": ""Query.Result <callback>""
              },
              ""parameters"": [
                {
                  ""id"": ""a"",
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
                      ""value"": null
                    }
                  }
                },
                {
                  ""id"": ""b"",
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
                      ""value"": null
                    }
                  }
                }
              ],
              ""instructions"": [
                {
                  ""type"": ""collectionCall"",
                  ""target"": {
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""alias""
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
                  ""mutation"": ""Clear"",
                  ""args"": [],
                  ""source"": {
                    ""line"": 1,
                    ""column"": 133
                  }
                },
                {
                  ""type"": ""return"",
                  ""pointer"": {
                    ""type"": ""operation"",
                    ""operation"": {
                      ""type"": ""arithmetic"",
                      ""arithmetic"": {
                        ""type"": ""-"",
                        ""pointers"": [
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""a""
                          },
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""b""
                          }
                        ],
                        ""numeric"": ""int""
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 148
                  }
                }
              ],
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 111
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""list""
            },
            ""source"": {
              ""line"": 1,
              ""column"": 166
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""sort alias guard 3"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var list = [3, 1, 2]; var alias = list; list.Sort((a, b) => { alias[0] = 9; return a - b; }); return list; } } }"",
      ""expected"": null,
      ""expectedError"": ""while its Sort comparison"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""list"",
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
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""alias"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""list""
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 93
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""list""
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
            ""mutation"": ""Sort"",
            ""args"": [],
            ""comparison"": {
              ""compilerRevision"": 17,
              ""source"": {
                ""uri"": ""Query.neo"",
                ""coordinateSpace"": ""file"",
                ""name"": ""Query.Result <callback>""
              },
              ""parameters"": [
                {
                  ""id"": ""a"",
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
                      ""value"": null
                    }
                  }
                },
                {
                  ""id"": ""b"",
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
                      ""value"": null
                    }
                  }
                }
              ],
              ""instructions"": [
                {
                  ""type"": ""assign"",
                  ""target"": {
                    ""pointer"": {
                      ""type"": ""keyOf"",
                      ""keyOf"": {
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""alias""
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
                      }
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
                      ""value"": 9
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 133
                  }
                },
                {
                  ""type"": ""return"",
                  ""pointer"": {
                    ""type"": ""operation"",
                    ""operation"": {
                      ""type"": ""arithmetic"",
                      ""arithmetic"": {
                        ""type"": ""-"",
                        ""pointers"": [
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""a""
                          },
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""b""
                          }
                        ],
                        ""numeric"": ""int""
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 147
                  }
                }
              ],
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 111
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""list""
            },
            ""source"": {
              ""line"": 1,
              ""column"": 165
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    },
    {
      ""name"": ""sort alias guard 4"",
      ""source"": ""enum Tone { Red, Blue } class Query { public List<int> Result { get { var list = [3, 1, 2]; var alias = list; list.Sort((a, b) => { alias.Sort(); return a - b; }); return list; } } }"",
      ""expected"": null,
      ""expectedError"": ""while its Sort comparison"",
      ""getter"": {
        ""compilerRevision"": 17,
        ""source"": {
          ""name"": ""Query.Result"",
          ""uri"": ""Query.neo"",
          ""coordinateSpace"": ""file""
        },
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""pending:class:Query.neo:Query""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""pending:class:Query.neo:Query""
                },
                ""value"": null
              }
            }
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 7,
              ""required"": true,
              ""classId"": ""builtin:Root""
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""builtin:Root""
                },
                ""value"": null
              }
            }
          }
        ],
        ""instructions"": [
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""list"",
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
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 71
            }
          },
          {
            ""type"": ""variable"",
            ""variable"": {
              ""id"": ""alias"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""list""
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 93
            }
          },
          {
            ""type"": ""collectionCall"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""list""
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
            ""mutation"": ""Sort"",
            ""args"": [],
            ""comparison"": {
              ""compilerRevision"": 17,
              ""source"": {
                ""uri"": ""Query.neo"",
                ""coordinateSpace"": ""file"",
                ""name"": ""Query.Result <callback>""
              },
              ""parameters"": [
                {
                  ""id"": ""a"",
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
                      ""value"": null
                    }
                  }
                },
                {
                  ""id"": ""b"",
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
                      ""value"": null
                    }
                  }
                }
              ],
              ""instructions"": [
                {
                  ""type"": ""collectionCall"",
                  ""target"": {
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""alias""
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
                  ""mutation"": ""Sort"",
                  ""args"": [],
                  ""keyType"": ""int"",
                  ""source"": {
                    ""line"": 1,
                    ""column"": 133
                  }
                },
                {
                  ""type"": ""return"",
                  ""pointer"": {
                    ""type"": ""operation"",
                    ""operation"": {
                      ""type"": ""arithmetic"",
                      ""arithmetic"": {
                        ""type"": ""-"",
                        ""pointers"": [
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""a""
                          },
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""b""
                          }
                        ],
                        ""numeric"": ""int""
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 147
                  }
                }
              ],
              ""typeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 111
            }
          },
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""list""
            },
            ""source"": {
              ""line"": 1,
              ""column"": 165
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
        },
        ""dependencies"": {
          ""recordIds"": [""builtin:Root"", ""pending:class:Query.neo:Query""],
          ""typeNames"": []
        }
      }
    }
  ]
}
";
    }
}
