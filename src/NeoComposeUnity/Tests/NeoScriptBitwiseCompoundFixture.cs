// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
// Compiled from P119 capture-order and compound-assignment test.
namespace NeoCompose.Tests
{
    internal static class NeoScriptBitwiseCompoundFixture
    {
        public const string Json = @"{
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
                ""value"": 8
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
        ""id"": ""rhs"",
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
          ""type"": ""delegateClosure"",
          ""typeInfo"": {
            ""type"": 25,
            ""required"": true,
            ""returnTypeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""argumentTypes"": []
          },
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
              },
              {
                ""id"": ""__capture_1_0__"",
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
                ""type"": ""debug"",
                ""severity"": ""log"",
                ""message"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""rhs""
                  }
                },
                ""messageType"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""source"": {
                  ""line"": 4,
                  ""column"": 32
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
                        ""variableId"": ""__capture_1_0__""
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
                    ""value"": 32
                  }
                },
                ""source"": {
                  ""line"": 4,
                  ""column"": 50
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
                },
                ""source"": {
                  ""line"": 4,
                  ""column"": 66
                }
              }
            ],
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            }
          },
          ""captures"": [
            {
              ""type"": ""variable"",
              ""variableId"": ""values""
            }
          ],
          ""code"": ""() => { Debug.Log(\""rhs\""); values[0] = 32; return 1; }""
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
          ""type"": 2,
          ""required"": true
        },
        ""writability"": ""local""
      },
      ""operator"": ""="",
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""bitwiseOp"",
          ""info"": {
            ""op"": ""or"",
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
                  ""variableId"": ""rhs""
                },
                ""args"": [],
                ""callSiteId"": ""5:23:1"",
                ""source"": {
                  ""line"": 5,
                  ""column"": 23
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
      ""type"": ""variable"",
      ""variable"": {
        ""id"": ""result"",
        ""typeInfo"": {
          ""type"": 2,
          ""required"": true
        },
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
        }
      },
      ""source"": {
        ""line"": 6,
        ""column"": 1
      }
    },
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
        ""type"": ""function"",
        ""function"": {
          ""type"": ""bitwiseOp"",
          ""info"": {
            ""op"": ""shiftLeft"",
            ""argPointers"": [
              {
                ""type"": ""variable"",
                ""variableId"": ""result""
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
      ""source"": {
        ""line"": 7,
        ""column"": 1
      }
    },
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
        ""type"": ""function"",
        ""function"": {
          ""type"": ""bitwiseOp"",
          ""info"": {
            ""op"": ""shiftRight"",
            ""argPointers"": [
              {
                ""type"": ""variable"",
                ""variableId"": ""result""
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
      },
      ""source"": {
        ""line"": 7,
        ""column"": 15
      }
    },
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
        ""type"": ""function"",
        ""function"": {
          ""type"": ""bitwiseOp"",
          ""info"": {
            ""op"": ""shiftRightUnsigned"",
            ""argPointers"": [
              {
                ""type"": ""variable"",
                ""variableId"": ""result""
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
      },
      ""source"": {
        ""line"": 7,
        ""column"": 29
      }
    },
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
        ""type"": ""function"",
        ""function"": {
          ""type"": ""bitwiseOp"",
          ""info"": {
            ""op"": ""xor"",
            ""argPointers"": [
              {
                ""type"": ""variable"",
                ""variableId"": ""result""
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
      ""source"": {
        ""line"": 7,
        ""column"": 44
      }
    },
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
        ""type"": ""function"",
        ""function"": {
          ""type"": ""bitwiseOp"",
          ""info"": {
            ""op"": ""and"",
            ""argPointers"": [
              {
                ""type"": ""variable"",
                ""variableId"": ""result""
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 15
                }
              }
            ]
          }
        }
      },
      ""source"": {
        ""line"": 7,
        ""column"": 57
      }
    },
    {
      ""type"": ""variable"",
      ""variable"": {
        ""id"": ""dict"",
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
                  ""value"": ""mask""
                }
              },
              ""value"": {
                ""type"": ""variable"",
                ""variableId"": ""result""
              }
            }
          ]
        }
      },
      ""source"": {
        ""line"": 8,
        ""column"": 1
      }
    },
    {
      ""type"": ""assign"",
      ""bindings"": [
        {
          ""id"": ""$assignment_2"",
          ""typeInfo"": {
            ""type"": ""Unknown"",
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""variable"",
            ""variableId"": ""dict""
          }
        }
      ],
      ""target"": {
        ""pointer"": {
          ""type"": ""keyOf"",
          ""keyOf"": {
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""$assignment_2""
            },
            ""key"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""value"": ""mask""
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
        ""type"": ""function"",
        ""function"": {
          ""type"": ""bitwiseOp"",
          ""info"": {
            ""op"": ""or"",
            ""argPointers"": [
              {
                ""type"": ""keyOf"",
                ""keyOf"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""$assignment_2""
                  },
                  ""key"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      },
                      ""value"": ""mask""
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
                  ""value"": 16
                }
              }
            ]
          }
        }
      },
      ""source"": {
        ""line"": 9,
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
            ""variableId"": ""dict""
          },
          ""key"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 3,
                ""required"": true
              },
              ""value"": ""mask""
            }
          }
        }
      },
      ""source"": {
        ""line"": 10,
        ""column"": 1
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
}
";
    }
}
