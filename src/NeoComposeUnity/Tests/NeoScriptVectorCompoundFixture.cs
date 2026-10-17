// Compiled P116 indexed compound assignment regression from the shared TypeScript compiler.
// List<Vector2> values = [new(1, 2)]; index() logs index and returns 0;
// scalar() logs scalar and returns 2; values[index()] *= scalar(); return values[0];
namespace NeoCompose.Tests
{
    internal static class NeoScriptVectorCompoundFixture
    {
        internal const string Json = @"{
  ""compilerRevision"": 17,
  ""source"": {
    ""name"": ""getter"",
    ""coordinateSpace"": ""body""
  },
  ""parameters"": [
    {
      ""id"": ""__this__"",
      ""typeInfo"": {
        ""type"": 7,
        ""required"": true,
        ""classId"": ""id-46""
      },
      ""pointer"": {
        ""type"": ""value"",
        ""value"": {
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""id-46""
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
        ""classId"": ""__root__""
      },
      ""pointer"": {
        ""type"": ""value"",
        ""value"": {
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""__root__""
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
        ""id"": ""values"",
        ""typeInfo"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 14,
            ""required"": true
          }
        },
        ""pointer"": {
          ""type"": ""listLiteral"",
          ""typeInfo"": {
            ""type"": 6,
            ""required"": true,
            ""entryTypeInfo"": {
              ""type"": 14,
              ""required"": true
            }
          },
          ""entries"": [
            {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""vectorConstructor"",
                ""info"": {
                  ""vectorType"": 14,
                  ""componentPointers"": [
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
                ""line"": 2,
                ""column"": 25
              }
            }
          ]
        }
      },
      ""source"": {
        ""line"": 2,
        ""column"": 1
      }
    },
    {
      ""type"": ""variable"",
      ""variable"": {
        ""id"": ""index"",
        ""typeInfo"": {
          ""type"": 25,
          ""required"": true,
          ""returnTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""argumentTypes"": []
        },
        ""pointer"": {
          ""type"": ""value"",
          ""value"": {
            ""typeInfo"": {
              ""type"": 25,
              ""required"": true,
              ""returnTypeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""argumentTypes"": []
            },
            ""value"": {
              ""code"": ""() => { Debug.Log(\""index\""); return 0; }"",
              ""action"": {
                ""compilerRevision"": 17,
                ""source"": {
                  ""coordinateSpace"": ""body"",
                  ""name"": ""getter <delegate>""
                },
                ""parameters"": [
                  {
                    ""id"": ""__this__"",
                    ""typeInfo"": {
                      ""type"": 7,
                      ""required"": true,
                      ""classId"": ""id-46""
                    },
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 7,
                          ""required"": true,
                          ""classId"": ""id-46""
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
                      ""classId"": ""__root__""
                    },
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 7,
                          ""required"": true,
                          ""classId"": ""__root__""
                        },
                        ""value"": null
                      }
                    }
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""debug"",
                    ""severity"": ""log"",
                    ""message"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""index""
                      }
                    },
                    ""messageType"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""source"": {
                      ""line"": 3,
                      ""column"": 34
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
                        ""value"": 0
                      }
                    },
                    ""source"": {
                      ""line"": 3,
                      ""column"": 54
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
      },
      ""source"": {
        ""line"": 3,
        ""column"": 1
      }
    },
    {
      ""type"": ""variable"",
      ""variable"": {
        ""id"": ""scalar"",
        ""typeInfo"": {
          ""type"": 25,
          ""required"": true,
          ""returnTypeInfo"": {
            ""type"": 2,
            ""required"": true
          },
          ""argumentTypes"": []
        },
        ""pointer"": {
          ""type"": ""value"",
          ""value"": {
            ""typeInfo"": {
              ""type"": 25,
              ""required"": true,
              ""returnTypeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""argumentTypes"": []
            },
            ""value"": {
              ""code"": ""() => { Debug.Log(\""scalar\""); return 2; }"",
              ""action"": {
                ""compilerRevision"": 17,
                ""source"": {
                  ""coordinateSpace"": ""body"",
                  ""name"": ""getter <delegate>""
                },
                ""parameters"": [
                  {
                    ""id"": ""__this__"",
                    ""typeInfo"": {
                      ""type"": 7,
                      ""required"": true,
                      ""classId"": ""id-46""
                    },
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 7,
                          ""required"": true,
                          ""classId"": ""id-46""
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
                      ""classId"": ""__root__""
                    },
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 7,
                          ""required"": true,
                          ""classId"": ""__root__""
                        },
                        ""value"": null
                      }
                    }
                  }
                ],
                ""instructions"": [
                  {
                    ""type"": ""debug"",
                    ""severity"": ""log"",
                    ""message"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""scalar""
                      }
                    },
                    ""messageType"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""source"": {
                      ""line"": 4,
                      ""column"": 35
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
                        ""value"": 2
                      }
                    },
                    ""source"": {
                      ""line"": 4,
                      ""column"": 56
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
      },
      ""source"": {
        ""line"": 4,
        ""column"": 1
      }
    },
    {
      ""type"": ""assign"",
      ""bindings"": [
        {
          ""id"": ""$assignment_0"",
          ""typeInfo"": {
            ""type"": ""Unknown"",
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""variable"",
            ""variableId"": ""values""
          }
        },
        {
          ""id"": ""$assignment_1"",
          ""typeInfo"": {
            ""type"": ""Unknown"",
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""callDelegate"",
            ""delegate"": {
              ""type"": ""variable"",
              ""variableId"": ""index""
            },
            ""args"": [],
            ""callSiteId"": ""5:13:0"",
            ""source"": {
              ""line"": 5,
              ""column"": 13
            }
          }
        }
      ],
      ""target"": {
        ""pointer"": {
          ""type"": ""keyOf"",
          ""keyOf"": {
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""$assignment_0""
            },
            ""key"": {
              ""type"": ""variable"",
              ""variableId"": ""$assignment_1""
            }
          }
        },
        ""typeInfo"": {
          ""type"": 14,
          ""required"": true
        },
        ""writability"": ""local""
      },
      ""operator"": ""*="",
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""multiply"",
            ""argPointers"": [
              {
                ""type"": ""keyOf"",
                ""keyOf"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""$assignment_0""
                  },
                  ""key"": {
                    ""type"": ""variable"",
                    ""variableId"": ""$assignment_1""
                  }
                }
              },
              {
                ""type"": ""callDelegate"",
                ""delegate"": {
                  ""type"": ""variable"",
                  ""variableId"": ""scalar""
                },
                ""args"": [],
                ""callSiteId"": ""5:26:1"",
                ""source"": {
                  ""line"": 5,
                  ""column"": 26
                }
              }
            ]
          }
        }
      },
      ""source"": {
        ""line"": 5,
        ""column"": 1
      }
    },
    {
      ""type"": ""return"",
      ""pointer"": {
        ""type"": ""keyOf"",
        ""keyOf"": {
          ""pointer"": {
            ""type"": ""variable"",
            ""variableId"": ""values""
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
      ""source"": {
        ""line"": 6,
        ""column"": 1
      }
    }
  ],
  ""typeInfo"": {
    ""type"": 14,
    ""required"": true
  },
  ""dependencies"": {
    ""recordIds"": [
      ""__root__"",
      ""id-46""
    ],
    ""typeNames"": []
  }
}
";
    }
}
