// Shared verbatim with neo-compose/src/models/neoscript/vector-parity-fixture.json.
namespace NeoCompose.Tests
{
    internal static class NeoScriptVectorParityFixture
    {
        internal const string Json = @"{
  ""$comment"": ""P116 shared vector parity fixture. Expected results are independently authored from the specified formulas, with Python struct used solely to encode float32 literals. No runtime generates expected results. Nonfinite expected components use NaN/Infinity/-Infinity string tokens; nonfinite operands use arithmetic IR. The SDK vendors this file verbatim. Signed zero compares numerically in this JSON fixture and has a separate runtime test."",
  ""evaluateCases"": [
    {
      ""name"": ""Vector2 add"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""add"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1.0,
                    ""y"": 3.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 6,
        ""y"": -4
      }
    },
    {
      ""name"": ""Vector2 subtract"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""subtract"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1.0,
                    ""y"": 3.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 4,
        ""y"": -10
      }
    },
    {
      ""name"": ""Vector2 negate"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""negate"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": -5,
        ""y"": 7
      }
    },
    {
      ""name"": ""Vector2 multiply"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""multiply"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0
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
                  ""value"": 2
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 10,
        ""y"": -14
      }
    },
    {
      ""name"": ""Vector2 scaleLeft"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""scaleLeft"",
            ""argPointers"": [
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
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 10,
        ""y"": -14
      }
    },
    {
      ""name"": ""Vector2 divide"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""divide"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0
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
                  ""value"": 2
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 2.5,
        ""y"": -3.5
      }
    },
    {
      ""name"": ""Vector2 min"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""min"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1.0,
                    ""y"": 3.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 1,
        ""y"": -7
      }
    },
    {
      ""name"": ""Vector2 max"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""max"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1.0,
                    ""y"": 3.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 5,
        ""y"": 3
      }
    },
    {
      ""name"": ""Vector2 squared magnitude"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""sqrMagnitude"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 3.0,
                    ""y"": 4.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": 25
    },
    {
      ""name"": ""Vector2 magnitude"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""magnitude"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 3.0,
                    ""y"": 4.0
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
      ""name"": ""Vector2 distance"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""distance"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 3.0,
                    ""y"": 4.0
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
      ""name"": ""Vector2 zero division"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""divide"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0
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
                  ""value"": 0
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""Division by zero""
    },
    {
      ""name"": ""Vector2Int add"",
      ""typeInfo"": {
        ""type"": 15,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2Int"",
            ""op"": ""add"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1,
                    ""y"": 3
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 6,
        ""y"": -4
      }
    },
    {
      ""name"": ""Vector2Int subtract"",
      ""typeInfo"": {
        ""type"": 15,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2Int"",
            ""op"": ""subtract"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1,
                    ""y"": 3
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 4,
        ""y"": -10
      }
    },
    {
      ""name"": ""Vector2Int negate"",
      ""typeInfo"": {
        ""type"": 15,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2Int"",
            ""op"": ""negate"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": -5,
        ""y"": 7
      }
    },
    {
      ""name"": ""Vector2Int multiply"",
      ""typeInfo"": {
        ""type"": 15,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2Int"",
            ""op"": ""multiply"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7
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
                  ""value"": 2
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 10,
        ""y"": -14
      }
    },
    {
      ""name"": ""Vector2Int scaleLeft"",
      ""typeInfo"": {
        ""type"": 15,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2Int"",
            ""op"": ""scaleLeft"",
            ""argPointers"": [
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
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 10,
        ""y"": -14
      }
    },
    {
      ""name"": ""Vector2Int divide"",
      ""typeInfo"": {
        ""type"": 15,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2Int"",
            ""op"": ""divide"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7
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
                  ""value"": 2
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 2,
        ""y"": -3
      }
    },
    {
      ""name"": ""Vector2Int min"",
      ""typeInfo"": {
        ""type"": 15,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2Int"",
            ""op"": ""min"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1,
                    ""y"": 3
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 1,
        ""y"": -7
      }
    },
    {
      ""name"": ""Vector2Int max"",
      ""typeInfo"": {
        ""type"": 15,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2Int"",
            ""op"": ""max"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1,
                    ""y"": 3
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 5,
        ""y"": 3
      }
    },
    {
      ""name"": ""Vector2Int squared magnitude"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2Int"",
            ""op"": ""sqrMagnitude"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 3,
                    ""y"": 4
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": 25
    },
    {
      ""name"": ""Vector2Int magnitude"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2Int"",
            ""op"": ""magnitude"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 3,
                    ""y"": 4
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
      ""name"": ""Vector2Int distance"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2Int"",
            ""op"": ""distance"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0,
                    ""y"": 0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 3,
                    ""y"": 4
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
      ""name"": ""Vector2Int zero division"",
      ""typeInfo"": {
        ""type"": 15,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2Int"",
            ""op"": ""divide"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7
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
                  ""value"": 0
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""Division by zero""
    },
    {
      ""name"": ""Vector3 add"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""add"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0,
                    ""z"": 2.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1.0,
                    ""y"": 3.0,
                    ""z"": -4.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 6,
        ""y"": -4,
        ""z"": -2
      }
    },
    {
      ""name"": ""Vector3 subtract"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""subtract"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0,
                    ""z"": 2.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1.0,
                    ""y"": 3.0,
                    ""z"": -4.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 4,
        ""y"": -10,
        ""z"": 6
      }
    },
    {
      ""name"": ""Vector3 negate"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""negate"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0,
                    ""z"": 2.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": -5,
        ""y"": 7,
        ""z"": -2
      }
    },
    {
      ""name"": ""Vector3 multiply"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""multiply"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0,
                    ""z"": 2.0
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
                  ""value"": 2
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 10,
        ""y"": -14,
        ""z"": 4
      }
    },
    {
      ""name"": ""Vector3 scaleLeft"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""scaleLeft"",
            ""argPointers"": [
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
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0,
                    ""z"": 2.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 10,
        ""y"": -14,
        ""z"": 4
      }
    },
    {
      ""name"": ""Vector3 divide"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""divide"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0,
                    ""z"": 2.0
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
                  ""value"": 2
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 2.5,
        ""y"": -3.5,
        ""z"": 1
      }
    },
    {
      ""name"": ""Vector3 min"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""min"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0,
                    ""z"": 2.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1.0,
                    ""y"": 3.0,
                    ""z"": -4.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 1,
        ""y"": -7,
        ""z"": -4
      }
    },
    {
      ""name"": ""Vector3 max"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""max"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0,
                    ""z"": 2.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1.0,
                    ""y"": 3.0,
                    ""z"": -4.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 5,
        ""y"": 3,
        ""z"": 2
      }
    },
    {
      ""name"": ""Vector3 squared magnitude"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""sqrMagnitude"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 3.0,
                    ""y"": 4.0,
                    ""z"": 0.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": 25
    },
    {
      ""name"": ""Vector3 magnitude"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""magnitude"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 3.0,
                    ""y"": 4.0,
                    ""z"": 0.0
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
      ""name"": ""Vector3 distance"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""distance"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0,
                    ""z"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 3.0,
                    ""y"": 4.0,
                    ""z"": 0.0
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
      ""name"": ""Vector3 zero division"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""divide"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5.0,
                    ""y"": -7.0,
                    ""z"": 2.0
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
                  ""value"": 0
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""Division by zero""
    },
    {
      ""name"": ""Vector3Int add"",
      ""typeInfo"": {
        ""type"": 17,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3Int"",
            ""op"": ""add"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7,
                    ""z"": 2
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1,
                    ""y"": 3,
                    ""z"": -4
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 6,
        ""y"": -4,
        ""z"": -2
      }
    },
    {
      ""name"": ""Vector3Int subtract"",
      ""typeInfo"": {
        ""type"": 17,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3Int"",
            ""op"": ""subtract"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7,
                    ""z"": 2
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1,
                    ""y"": 3,
                    ""z"": -4
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 4,
        ""y"": -10,
        ""z"": 6
      }
    },
    {
      ""name"": ""Vector3Int negate"",
      ""typeInfo"": {
        ""type"": 17,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3Int"",
            ""op"": ""negate"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7,
                    ""z"": 2
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": -5,
        ""y"": 7,
        ""z"": -2
      }
    },
    {
      ""name"": ""Vector3Int multiply"",
      ""typeInfo"": {
        ""type"": 17,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3Int"",
            ""op"": ""multiply"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7,
                    ""z"": 2
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
                  ""value"": 2
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 10,
        ""y"": -14,
        ""z"": 4
      }
    },
    {
      ""name"": ""Vector3Int scaleLeft"",
      ""typeInfo"": {
        ""type"": 17,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3Int"",
            ""op"": ""scaleLeft"",
            ""argPointers"": [
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
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7,
                    ""z"": 2
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 10,
        ""y"": -14,
        ""z"": 4
      }
    },
    {
      ""name"": ""Vector3Int divide"",
      ""typeInfo"": {
        ""type"": 17,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3Int"",
            ""op"": ""divide"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7,
                    ""z"": 2
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
                  ""value"": 2
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 2,
        ""y"": -3,
        ""z"": 1
      }
    },
    {
      ""name"": ""Vector3Int min"",
      ""typeInfo"": {
        ""type"": 17,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3Int"",
            ""op"": ""min"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7,
                    ""z"": 2
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1,
                    ""y"": 3,
                    ""z"": -4
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 1,
        ""y"": -7,
        ""z"": -4
      }
    },
    {
      ""name"": ""Vector3Int max"",
      ""typeInfo"": {
        ""type"": 17,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3Int"",
            ""op"": ""max"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7,
                    ""z"": 2
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1,
                    ""y"": 3,
                    ""z"": -4
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 5,
        ""y"": 3,
        ""z"": 2
      }
    },
    {
      ""name"": ""Vector3Int squared magnitude"",
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3Int"",
            ""op"": ""sqrMagnitude"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 3,
                    ""y"": 4,
                    ""z"": 0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": 25
    },
    {
      ""name"": ""Vector3Int magnitude"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3Int"",
            ""op"": ""magnitude"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 3,
                    ""y"": 4,
                    ""z"": 0
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
      ""name"": ""Vector3Int distance"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3Int"",
            ""op"": ""distance"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0,
                    ""y"": 0,
                    ""z"": 0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 3,
                    ""y"": 4,
                    ""z"": 0
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
      ""name"": ""Vector3Int zero division"",
      ""typeInfo"": {
        ""type"": 17,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3Int"",
            ""op"": ""divide"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 5,
                    ""y"": -7,
                    ""z"": 2
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
                  ""value"": 0
                }
              }
            ]
          }
        }
      },
      ""expectedError"": ""Division by zero""
    },
    {
      ""name"": ""Vector2 normalized"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""normalized"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 1
      }
    },
    {
      ""name"": ""Vector2 normalized zero"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""normalized"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 0
      }
    },
    {
      ""name"": ""Vector2 normalized tiny"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""normalized"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 9.999999974752427e-7
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 0
      }
    },
    {
      ""name"": ""Vector2 dot"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""dot"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": 16
    },
    {
      ""name"": ""Vector2 lerp -1"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""lerp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0
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
                  ""value"": -1.0
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 0
      }
    },
    {
      ""name"": ""Vector2 lerp 0.25"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""lerp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0
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
                  ""value"": 0.25
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 1
      }
    },
    {
      ""name"": ""Vector2 lerp 2"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""lerp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0
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
                  ""value"": 2.0
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 4
      }
    },
    {
      ""name"": ""Vector2 move -2"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""moveTowards"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0
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
                  ""value"": -2.0
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": -2
      }
    },
    {
      ""name"": ""Vector2 move 1"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""moveTowards"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0
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
      ""expected"": {
        ""x"": 0,
        ""y"": 1
      }
    },
    {
      ""name"": ""Vector2 move 5"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""moveTowards"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0
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
                  ""value"": 5.0
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 4
      }
    },
    {
      ""name"": ""Vector2 move identical negative"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""moveTowards"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0
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
                  ""value"": -1.0
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 4
      }
    },
    {
      ""name"": ""Vector3 normalized"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""normalized"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0,
                    ""z"": 0.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 1,
        ""z"": 0
      }
    },
    {
      ""name"": ""Vector3 normalized zero"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""normalized"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0,
                    ""z"": 0.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 0,
        ""z"": 0
      }
    },
    {
      ""name"": ""Vector3 normalized tiny"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""normalized"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 9.999999974752427e-7,
                    ""z"": 0.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 0,
        ""z"": 0
      }
    },
    {
      ""name"": ""Vector3 dot"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""dot"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0,
                    ""z"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0,
                    ""z"": 0.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": 16
    },
    {
      ""name"": ""Vector3 lerp -1"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""lerp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0,
                    ""z"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0,
                    ""z"": 0.0
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
                  ""value"": -1.0
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 0,
        ""z"": 0
      }
    },
    {
      ""name"": ""Vector3 lerp 0.25"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""lerp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0,
                    ""z"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0,
                    ""z"": 0.0
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
                  ""value"": 0.25
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 1,
        ""z"": 0
      }
    },
    {
      ""name"": ""Vector3 lerp 2"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""lerp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0,
                    ""z"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0,
                    ""z"": 0.0
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
                  ""value"": 2.0
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 4,
        ""z"": 0
      }
    },
    {
      ""name"": ""Vector3 move -2"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""moveTowards"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0,
                    ""z"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0,
                    ""z"": 0.0
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
                  ""value"": -2.0
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": -2,
        ""z"": 0
      }
    },
    {
      ""name"": ""Vector3 move 1"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""moveTowards"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0,
                    ""z"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0,
                    ""z"": 0.0
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
      ""expected"": {
        ""x"": 0,
        ""y"": 1,
        ""z"": 0
      }
    },
    {
      ""name"": ""Vector3 move 5"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""moveTowards"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 0.0,
                    ""z"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0,
                    ""z"": 0.0
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
                  ""value"": 5.0
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 4,
        ""z"": 0
      }
    },
    {
      ""name"": ""Vector3 move identical negative"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""moveTowards"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0,
                    ""z"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 4.0,
                    ""z"": 0.0
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
                  ""value"": -1.0
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 4,
        ""z"": 0
      }
    },
    {
      ""name"": ""Vector3 right handed cross"",
      ""typeInfo"": {
        ""type"": 16,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""cross"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1.0,
                    ""y"": 0.0,
                    ""z"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 0.0,
                    ""y"": 1.0,
                    ""z"": 0.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 0,
        ""z"": 1
      }
    },
    {
      ""name"": ""Vector3 dot rounds each addition"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3"",
            ""op"": ""dot"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 16777216.0,
                    ""y"": 1.0,
                    ""z"": -16777216.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 16,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1.0,
                    ""y"": 1.0,
                    ""z"": 1.0
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
      ""name"": ""Vector2 scalar widening rounds before multiplication"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""multiply"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1.0,
                    ""y"": -1.0
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
                  ""value"": 16777217
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 16777216,
        ""y"": -16777216
      }
    },
    {
      ""name"": ""Vector2Int preserves integer precision"",
      ""typeInfo"": {
        ""type"": 15,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2Int"",
            ""op"": ""add"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 16777217,
                    ""y"": -16777217
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 2,
                    ""y"": -2
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 16777219,
        ""y"": -16777219
      }
    },
    {
      ""name"": ""Vector3Int exceeds Unity int32 without overflow"",
      ""typeInfo"": {
        ""type"": 17,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector3Int"",
            ""op"": ""multiply"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 17,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 2147483647,
                    ""y"": -2147483648,
                    ""z"": 16777217
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
                  ""value"": 2
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 4294967294,
        ""y"": -4294967296,
        ""z"": 33554434
      }
    },
    {
      ""name"": ""Vector2Int distance subtracts before float conversion"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2Int"",
            ""op"": ""distance"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 16777217,
                    ""y"": 0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 15,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 16777216,
                    ""y"": 0
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
      ""name"": ""Color lerp includes alpha"",
      ""typeInfo"": {
        ""type"": 19,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Color"",
            ""op"": ""lerp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 19,
                    ""required"": true
                  },
                  ""value"": {
                    ""r"": 0.0,
                    ""g"": 0.25,
                    ""b"": 1.0,
                    ""a"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 19,
                    ""required"": true
                  },
                  ""value"": {
                    ""r"": 1.0,
                    ""g"": 0.75,
                    ""b"": 0.0,
                    ""a"": 1.0
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
                  ""value"": 0.25
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""r"": 0.25,
        ""g"": 0.375,
        ""b"": 0.75,
        ""a"": 0.25
      }
    },
    {
      ""name"": ""Color lower clamp"",
      ""typeInfo"": {
        ""type"": 19,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Color"",
            ""op"": ""lerp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 19,
                    ""required"": true
                  },
                  ""value"": {
                    ""r"": 0.0,
                    ""g"": 0.25,
                    ""b"": 1.0,
                    ""a"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 19,
                    ""required"": true
                  },
                  ""value"": {
                    ""r"": 1.0,
                    ""g"": 0.75,
                    ""b"": 0.0,
                    ""a"": 1.0
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
                  ""value"": -2.0
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""r"": 0,
        ""g"": 0.25,
        ""b"": 1,
        ""a"": 0
      }
    },
    {
      ""name"": ""Color upper clamp"",
      ""typeInfo"": {
        ""type"": 19,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Color"",
            ""op"": ""lerp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 19,
                    ""required"": true
                  },
                  ""value"": {
                    ""r"": 0.0,
                    ""g"": 0.25,
                    ""b"": 1.0,
                    ""a"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 19,
                    ""required"": true
                  },
                  ""value"": {
                    ""r"": 1.0,
                    ""g"": 0.75,
                    ""b"": 0.0,
                    ""a"": 1.0
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
                  ""value"": 2.0
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""r"": 1,
        ""g"": 0.75,
        ""b"": 0,
        ""a"": 1
      }
    },
    {
      ""name"": ""Vector2 infinity and NaN component arithmetic"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""add"",
            ""argPointers"": [
              {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""vectorOp"",
                  ""info"": {
                    ""kind"": ""Vector2"",
                    ""op"": ""multiply"",
                    ""argPointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 14,
                            ""required"": true
                          },
                          ""value"": {
                            ""x"": 1.0,
                            ""y"": 0.0
                          }
                        }
                      },
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
                                  ""value"": 3.4028234663852886e38
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
                      }
                    ]
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1.0,
                    ""y"": 1.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": ""Infinity"",
        ""y"": ""NaN""
      }
    },
    {
      ""name"": ""Vector2 NaN magnitude normalizes to zero"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""normalized"",
            ""argPointers"": [
              {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""vectorOp"",
                  ""info"": {
                    ""kind"": ""Vector2"",
                    ""op"": ""multiply"",
                    ""argPointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 14,
                            ""required"": true
                          },
                          ""value"": {
                            ""x"": 1.0,
                            ""y"": 0.0
                          }
                        }
                      },
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
                                  ""value"": 3.4028234663852886e38
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
                      }
                    ]
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 0,
        ""y"": 0
      }
    },
    {
      ""name"": ""Vector2 NaN min propagates"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""min"",
            ""argPointers"": [
              {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""vectorOp"",
                  ""info"": {
                    ""kind"": ""Vector2"",
                    ""op"": ""multiply"",
                    ""argPointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 14,
                            ""required"": true
                          },
                          ""value"": {
                            ""x"": 1.0,
                            ""y"": 0.0
                          }
                        }
                      },
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
                                  ""value"": 3.4028234663852886e38
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
                      }
                    ]
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 2.0,
                    ""y"": 3.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": 2,
        ""y"": ""NaN""
      }
    },
    {
      ""name"": ""Vector2 NaN max propagates"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""max"",
            ""argPointers"": [
              {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""vectorOp"",
                  ""info"": {
                    ""kind"": ""Vector2"",
                    ""op"": ""multiply"",
                    ""argPointers"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 14,
                            ""required"": true
                          },
                          ""value"": {
                            ""x"": 1.0,
                            ""y"": 0.0
                          }
                        }
                      },
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
                                  ""value"": 3.4028234663852886e38
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
                      }
                    ]
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 2.0,
                    ""y"": 3.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": {
        ""x"": ""Infinity"",
        ""y"": ""NaN""
      }
    },
    {
      ""name"": ""Vector2 NaN lerp propagates"",
      ""typeInfo"": {
        ""type"": 14,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""lerp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 1.0,
                    ""y"": 2.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 3.0,
                    ""y"": 4.0
                  }
                }
              },
              {
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
                                  ""value"": 3.4028234663852886e38
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
      ""expected"": {
        ""x"": ""NaN"",
        ""y"": ""NaN""
      }
    },
    {
      ""name"": ""Color NaN lerp includes alpha"",
      ""typeInfo"": {
        ""type"": 19,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Color"",
            ""op"": ""lerp"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 19,
                    ""required"": true
                  },
                  ""value"": {
                    ""r"": 0.0,
                    ""g"": 0.0,
                    ""b"": 0.0,
                    ""a"": 0.0
                  }
                }
              },
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 19,
                    ""required"": true
                  },
                  ""value"": {
                    ""r"": 1.0,
                    ""g"": 1.0,
                    ""b"": 1.0,
                    ""a"": 1.0
                  }
                }
              },
              {
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
                                  ""value"": 3.4028234663852886e38
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
      ""expected"": {
        ""r"": ""NaN"",
        ""g"": ""NaN"",
        ""b"": ""NaN"",
        ""a"": ""NaN""
      }
    },
    {
      ""name"": ""Vector2 overflow magnitude"",
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""pointer"": {
        ""type"": ""function"",
        ""function"": {
          ""type"": ""vectorOp"",
          ""info"": {
            ""kind"": ""Vector2"",
            ""op"": ""magnitude"",
            ""argPointers"": [
              {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 14,
                    ""required"": true
                  },
                  ""value"": {
                    ""x"": 3.4028234663852886e38,
                    ""y"": 0.0
                  }
                }
              }
            ]
          }
        }
      },
      ""expected"": ""Infinity""
    }
  ]
}
";
    }
}
