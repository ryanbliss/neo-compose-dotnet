// Generated from neo-compose src/test-fixtures/numeric/random-ir.json.
namespace NeoCompose.Tests
{
    internal static class NeoScriptRandomIrFixture
    {
        internal const string Json = @"{
  ""value"": {
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
        ""type"": ""return"",
        ""pointer"": {
          ""type"": ""function"",
          ""function"": {
            ""type"": ""randomOp"",
            ""info"": {
              ""op"": ""value"",
              ""argPointers"": []
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 14
          }
        },
        ""source"": {
          ""line"": 1,
          ""column"": 1
        }
      }
    ],
    ""typeInfo"": {
      ""type"": 4,
      ""required"": true
    },
    ""dependencies"": {
      ""recordIds"": [
        ""__root__"",
        ""id-46""
      ],
      ""typeNames"": []
    }
  },
  ""rangeInt"": {
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
          ""id"": ""min"",
          ""typeInfo"": {
            ""type"": 2,
            ""required"": true
          },
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
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""value"": 2147483648
                    }
                  }
                ],
                ""numeric"": ""int""
              }
            }
          }
        },
        ""source"": {
          ""line"": 1,
          ""column"": 1
        }
      },
      {
        ""type"": ""variable"",
        ""variable"": {
          ""id"": ""max"",
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
              ""value"": 2147483647
            }
          }
        },
        ""source"": {
          ""line"": 1,
          ""column"": 24
        }
      },
      {
        ""type"": ""return"",
        ""pointer"": {
          ""type"": ""function"",
          ""function"": {
            ""type"": ""randomOp"",
            ""info"": {
              ""op"": ""rangeInt"",
              ""argPointers"": [
                {
                  ""type"": ""variable"",
                  ""variableId"": ""min""
                },
                {
                  ""type"": ""variable"",
                  ""variableId"": ""max""
                }
              ]
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 65
          }
        },
        ""source"": {
          ""line"": 1,
          ""column"": 46
        }
      }
    ],
    ""typeInfo"": {
      ""type"": 2,
      ""required"": true
    },
    ""dependencies"": {
      ""recordIds"": [
        ""__root__"",
        ""id-46""
      ],
      ""typeNames"": []
    }
  },
  ""rangeFloat"": {
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
        ""type"": ""return"",
        ""pointer"": {
          ""type"": ""function"",
          ""function"": {
            ""type"": ""randomOp"",
            ""info"": {
              ""op"": ""rangeFloat"",
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
                              ""type"": 4,
                              ""required"": true
                            },
                            ""value"": 3
                          }
                        }
                      ],
                      ""numeric"": ""float""
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
                    ""value"": 5
                  }
                }
              ]
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 20
          }
        },
        ""source"": {
          ""line"": 1,
          ""column"": 1
        }
      }
    ],
    ""typeInfo"": {
      ""type"": 4,
      ""required"": true
    },
    ""dependencies"": {
      ""recordIds"": [
        ""__root__"",
        ""id-46""
      ],
      ""typeNames"": []
    }
  },
  ""reset"": {
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
        ""type"": ""functionCall"",
        ""call"": {
          ""type"": ""function"",
          ""function"": {
            ""type"": ""randomOp"",
            ""info"": {
              ""op"": ""initState"",
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
            ""column"": 17
          }
        },
        ""source"": {
          ""line"": 1,
          ""column"": 1
        }
      },
      {
        ""type"": ""return"",
        ""pointer"": {
          ""type"": ""function"",
          ""function"": {
            ""type"": ""randomOp"",
            ""info"": {
              ""op"": ""value"",
              ""argPointers"": []
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 36
          }
        },
        ""source"": {
          ""line"": 1,
          ""column"": 23
        }
      }
    ],
    ""typeInfo"": {
      ""type"": 4,
      ""required"": true
    },
    ""dependencies"": {
      ""recordIds"": [
        ""__root__"",
        ""id-46""
      ],
      ""typeNames"": []
    }
  },
  ""nested"": {
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
          ""id"": ""roll"",
          ""typeInfo"": {
            ""type"": 25,
            ""required"": true,
            ""returnTypeInfo"": {
              ""type"": 4,
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
                  ""type"": 4,
                  ""required"": true
                },
                ""argumentTypes"": []
              },
              ""value"": {
                ""code"": ""() => Random.value"",
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
                      ""type"": ""return"",
                      ""pointer"": {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""randomOp"",
                          ""info"": {
                            ""op"": ""value"",
                            ""argPointers"": []
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 39
                        }
                      },
                      ""source"": {
                        ""line"": 1,
                        ""column"": 33
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
          }
        },
        ""source"": {
          ""line"": 1,
          ""column"": 1
        }
      },
      {
        ""type"": ""return"",
        ""pointer"": {
          ""type"": ""function"",
          ""function"": {
            ""type"": ""randomOp"",
            ""info"": {
              ""op"": ""rangeFloat"",
              ""argPointers"": [
                {
                  ""type"": ""callDelegate"",
                  ""delegate"": {
                    ""type"": ""variable"",
                    ""variableId"": ""roll""
                  },
                  ""args"": [],
                  ""callSiteId"": ""1:71:0"",
                  ""source"": {
                    ""line"": 1,
                    ""column"": 71
                  }
                },
                {
                  ""type"": ""callDelegate"",
                  ""delegate"": {
                    ""type"": ""variable"",
                    ""variableId"": ""roll""
                  },
                  ""args"": [],
                  ""callSiteId"": ""1:79:1"",
                  ""source"": {
                    ""line"": 1,
                    ""column"": 79
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
        ""source"": {
          ""line"": 1,
          ""column"": 47
        }
      }
    ],
    ""typeInfo"": {
      ""type"": 4,
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
}
";
    }
}
