// Generated from neo-compose/src/test-fixtures/numeric/math-reference.json.
namespace NeoCompose.Tests
{
    internal static class NeoScriptExtendedMathFixture
    {
        internal const string Json = @"{
  ""getters"": {
    ""pow"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        },
        {
          ""id"": ""arg1"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""pow"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  },
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg1""
                  }
                ]
              }
            }
          }
        }
      ]
    },
    ""exp"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""exp"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  }
                ]
              }
            }
          }
        }
      ]
    },
    ""log"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""log"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  }
                ]
              }
            }
          }
        }
      ]
    },
    ""log10"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""log10"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  }
                ]
              }
            }
          }
        }
      ]
    },
    ""sin"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""sin"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  }
                ]
              }
            }
          }
        }
      ]
    },
    ""cos"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""cos"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  }
                ]
              }
            }
          }
        }
      ]
    },
    ""tan"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""tan"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  }
                ]
              }
            }
          }
        }
      ]
    },
    ""asin"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""asin"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  }
                ]
              }
            }
          }
        }
      ]
    },
    ""acos"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""acos"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  }
                ]
              }
            }
          }
        }
      ]
    },
    ""atan"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""atan"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  }
                ]
              }
            }
          }
        }
      ]
    },
    ""atan2"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        },
        {
          ""id"": ""arg1"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""atan2"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  },
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg1""
                  }
                ]
              }
            }
          }
        }
      ]
    },
    ""lerp"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        },
        {
          ""id"": ""arg1"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        },
        {
          ""id"": ""arg2"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""lerp"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  },
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg1""
                  },
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg2""
                  }
                ]
              }
            }
          }
        }
      ]
    },
    ""inverselerp"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        },
        {
          ""id"": ""arg1"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        },
        {
          ""id"": ""arg2"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""inverselerp"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  },
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg1""
                  },
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg2""
                  }
                ]
              }
            }
          }
        }
      ]
    },
    ""movetowards"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        },
        {
          ""id"": ""arg1"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        },
        {
          ""id"": ""arg2"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""movetowards"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  },
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg1""
                  },
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg2""
                  }
                ]
              }
            }
          }
        }
      ]
    },
    ""repeat"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        },
        {
          ""id"": ""arg1"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""repeat"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  },
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg1""
                  }
                ]
              }
            }
          }
        }
      ]
    },
    ""pingpong"": {
      ""compilerRevision"": 19,
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__root__"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": false
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": false
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""arg0"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        },
        {
          ""id"": ""arg1"",
          ""typeInfo"": {
            ""type"": 4,
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 4,
                ""required"": true
              },
              ""value"": 0
            }
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 4,
        ""required"": true
      },
      ""instructions"": [
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""mathOp"",
              ""info"": {
                ""op"": ""pingpong"",
                ""argPointers"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg0""
                  },
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""arg1""
                  }
                ]
              }
            }
          }
        }
      ]
    }
  },
  ""cases"": [
    {
      ""op"": ""pow"",
      ""inputs"": [
        0,
        0
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        0,
        2147483648
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        0,
        1065353216
      ],
      ""expected"": 0
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        0,
        3212836864
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        0,
        2139095040
      ],
      ""expected"": 0
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        0,
        4286578688
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        0,
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2147483648,
        0
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2147483648,
        2147483648
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2147483648,
        1065353216
      ],
      ""expected"": 2147483648
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2147483648,
        3212836864
      ],
      ""expected"": 4286578688
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2147483648,
        2139095040
      ],
      ""expected"": 0
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2147483648,
        4286578688
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2147483648,
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1065353216,
        0
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1065353216,
        2147483648
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1065353216,
        1065353216
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1065353216,
        3212836864
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1065353216,
        2139095040
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1065353216,
        4286578688
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1065353216,
        2143289344
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        3212836864,
        0
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        3212836864,
        2147483648
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        3212836864,
        1065353216
      ],
      ""expected"": 3212836864
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        3212836864,
        3212836864
      ],
      ""expected"": 3212836864
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        3212836864,
        2139095040
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        3212836864,
        4286578688
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        3212836864,
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2139095040,
        0
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2139095040,
        2147483648
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2139095040,
        1065353216
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2139095040,
        3212836864
      ],
      ""expected"": 0
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2139095040,
        2139095040
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2139095040,
        4286578688
      ],
      ""expected"": 0
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2139095040,
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        4286578688,
        0
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        4286578688,
        2147483648
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        4286578688,
        1065353216
      ],
      ""expected"": 4286578688
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        4286578688,
        3212836864
      ],
      ""expected"": 2147483648
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        4286578688,
        2139095040
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        4286578688,
        4286578688
      ],
      ""expected"": 0
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        4286578688,
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2143289344,
        0
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2143289344,
        2147483648
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2143289344,
        1065353216
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2143289344,
        3212836864
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2143289344,
        2139095040
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2143289344,
        4286578688
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2143289344,
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        0,
        1056964608
      ],
      ""expected"": 0
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2147483648,
        1056964608
      ],
      ""expected"": 0
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1,
        1056964608
      ],
      ""expected"": 439682291
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2147483649,
        1056964608
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        8388607,
        1056964608
      ],
      ""expected"": 536870911
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        8388608,
        1056964608
      ],
      ""expected"": 536870912
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1056964608,
        1056964608
      ],
      ""expected"": 1060439283
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        3204448256,
        1056964608
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1065353216,
        1056964608
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        3212836864,
        1056964608
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1073741823,
        1056964608
      ],
      ""expected"": 1068827891
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1073741824,
        1056964608
      ],
      ""expected"": 1068827891
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        3221225472,
        1056964608
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2139095039,
        1056964608
      ],
      ""expected"": 1602224127
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        4286578687,
        1056964608
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2139095040,
        1056964608
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        4286578688,
        1056964608
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2143289344,
        1056964608
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1070141403,
        1056964608
      ],
      ""expected"": 1067478169
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1078530011,
        1056964608
      ],
      ""expected"": 1071833029
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1266679809,
        1056964608
      ],
      ""expected"": 1166016512
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1325400064,
        1056964608
      ],
      ""expected"": 1194657011
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        0,
        1073741824
      ],
      ""expected"": 0
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2147483648,
        1073741824
      ],
      ""expected"": 0
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1,
        1073741824
      ],
      ""expected"": 0
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2147483649,
        1073741824
      ],
      ""expected"": 0
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        8388607,
        1073741824
      ],
      ""expected"": 0
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        8388608,
        1073741824
      ],
      ""expected"": 0
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1056964608,
        1073741824
      ],
      ""expected"": 1048576000
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        3204448256,
        1073741824
      ],
      ""expected"": 1048576000
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1065353216,
        1073741824
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        3212836864,
        1073741824
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1073741823,
        1073741824
      ],
      ""expected"": 1082130430
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1073741824,
        1073741824
      ],
      ""expected"": 1082130432
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        3221225472,
        1073741824
      ],
      ""expected"": 1082130432
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2139095039,
        1073741824
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        4286578687,
        1073741824
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2139095040,
        1073741824
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        4286578688,
        1073741824
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        2143289344,
        1073741824
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1070141403,
        1073741824
      ],
      ""expected"": 1075702247
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1078530011,
        1073741824
      ],
      ""expected"": 1092479463
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1266679809,
        1073741824
      ],
      ""expected"": 1468006402
    },
    {
      ""op"": ""pow"",
      ""inputs"": [
        1325400064,
        1073741824
      ],
      ""expected"": 1585446912
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        0
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        2147483648
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        1
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        2147483649
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        8388607
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        8388608
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        1056964608
      ],
      ""expected"": 1070795084
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        3204448256
      ],
      ""expected"": 1058751896
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        1065353216
      ],
      ""expected"": 1076754516
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        3212836864
      ],
      ""expected"": 1052531378
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        1073741823
      ],
      ""expected"": 1089237796
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        1073741824
      ],
      ""expected"": 1089237798
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        3221225472
      ],
      ""expected"": 1040880981
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        2139095039
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        4286578687
      ],
      ""expected"": 0
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        2139095040
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        4286578688
      ],
      ""expected"": 0
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        1070141403
      ],
      ""expected"": 1083830127
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        1078530011
      ],
      ""expected"": 1102651429
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        1266679809
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""exp"",
      ""inputs"": [
        1325400064
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        0
      ],
      ""expected"": 4286578688
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        2147483648
      ],
      ""expected"": 4286578688
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        1
      ],
      ""expected"": 3268316880
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        2147483649
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        8388607
      ],
      ""expected"": 3266227280
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        8388608
      ],
      ""expected"": 3266227280
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        1056964608
      ],
      ""expected"": 3207688728
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        3204448256
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        1065353216
      ],
      ""expected"": 0
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        3212836864
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        1073741823
      ],
      ""expected"": 1060205079
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        1073741824
      ],
      ""expected"": 1060205080
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        3221225472
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        2139095039
      ],
      ""expected"": 1118925336
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        4286578687
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        2139095040
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        4286578688
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        1070141403
      ],
      ""expected"": 1055339994
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        1078530011
      ],
      ""expected"": 1066567299
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        1266679809
      ],
      ""expected"": 1099240850
    },
    {
      ""op"": ""log"",
      ""inputs"": [
        1325400064
      ],
      ""expected"": 1101784711
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        0
      ],
      ""expected"": 4286578688
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        2147483648
      ],
      ""expected"": 4286578688
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        1
      ],
      ""expected"": 3258149364
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        2147483649
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        8388607
      ],
      ""expected"": 3256334360
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        8388608
      ],
      ""expected"": 3256334360
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        1056964608
      ],
      ""expected"": 3197771931
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        3204448256
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        1065353216
      ],
      ""expected"": 0
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        3212836864
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        1073741823
      ],
      ""expected"": 1050288282
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        1073741824
      ],
      ""expected"": 1050288283
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        3221225472
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        2139095039
      ],
      ""expected"": 1109008539
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        4286578687
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        2139095040
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        4286578688
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        1070141403
      ],
      ""expected"": 1044960167
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        1078530011
      ],
      ""expected"": 1056868974
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        1266679809
      ],
      ""expected"": 1088893160
    },
    {
      ""op"": ""log10"",
      ""inputs"": [
        1325400064
      ],
      ""expected"": 1091915670
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        0
      ],
      ""expected"": 0
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        2147483648
      ],
      ""expected"": 2147483648
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        1
      ],
      ""expected"": 1
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        2147483649
      ],
      ""expected"": 2147483649
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        8388607
      ],
      ""expected"": 8388607
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        8388608
      ],
      ""expected"": 8388608
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        1056964608
      ],
      ""expected"": 1056274244
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        3204448256
      ],
      ""expected"": 3203757892
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        1065353216
      ],
      ""expected"": 1062693540
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        3212836864
      ],
      ""expected"": 3210177188
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        1073741823
      ],
      ""expected"": 1063831480
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        1073741824
      ],
      ""expected"": 1063831479
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        3221225472
      ],
      ""expected"": 3211315127
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        2139095039
      ],
      ""expected"": 3204815283
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        4286578687
      ],
      ""expected"": 1057331635
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        2139095040
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        4286578688
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        1070141403
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        1078530011
      ],
      ""expected"": 3015425326
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        1266679809
      ],
      ""expected"": 1063573604
    },
    {
      ""op"": ""sin"",
      ""inputs"": [
        1325400064
      ],
      ""expected"": 3212355529
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        0
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        2147483648
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        1
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        2147483649
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        8388607
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        8388608
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        1056964608
      ],
      ""expected"": 1063299392
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        3204448256
      ],
      ""expected"": 1063299392
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        1065353216
      ],
      ""expected"": 1057640768
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        3212836864
      ],
      ""expected"": 1057640768
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        1073741823
      ],
      ""expected"": 3201634607
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        1073741824
      ],
      ""expected"": 3201634611
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        3221225472
      ],
      ""expected"": 3201634611
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        2139095039
      ],
      ""expected"": 1062887318
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        4286578687
      ],
      ""expected"": 1062887318
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        2139095040
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        4286578688
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        1070141403
      ],
      ""expected"": 3007036718
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        1078530011
      ],
      ""expected"": 3212836864
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        1266679809
      ],
      ""expected"": 1055226922
    },
    {
      ""op"": ""cos"",
      ""inputs"": [
        1325400064
      ],
      ""expected"": 1047758359
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        0
      ],
      ""expected"": 0
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        2147483648
      ],
      ""expected"": 2147483648
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        1
      ],
      ""expected"": 1
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        2147483649
      ],
      ""expected"": 2147483649
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        8388607
      ],
      ""expected"": 8388607
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        8388608
      ],
      ""expected"": 8388608
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        1056964608
      ],
      ""expected"": 1057741435
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        3204448256
      ],
      ""expected"": 3205225083
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        1065353216
      ],
      ""expected"": 1070029091
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        3212836864
      ],
      ""expected"": 3217512739
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        1073741823
      ],
      ""expected"": 3222001588
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        1073741824
      ],
      ""expected"": 3222001585
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        3221225472
      ],
      ""expected"": 1074517937
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        2139095039
      ],
      ""expected"": 3206323914
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        4286578687
      ],
      ""expected"": 1058840266
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        2139095040
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        4286578688
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        1070141403
      ],
      ""expected"": 3417213514
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        1078530011
      ],
      ""expected"": 867941678
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        1266679809
      ],
      ""expected"": 1073695054
    },
    {
      ""op"": ""tan"",
      ""inputs"": [
        1325400064
      ],
      ""expected"": 3229790848
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        0
      ],
      ""expected"": 0
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        2147483648
      ],
      ""expected"": 2147483648
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        1
      ],
      ""expected"": 1
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        2147483649
      ],
      ""expected"": 2147483649
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        8388607
      ],
      ""expected"": 8388607
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        8388608
      ],
      ""expected"": 8388608
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        1056964608
      ],
      ""expected"": 1057360530
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        3204448256
      ],
      ""expected"": 3204844178
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        1065353216
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        3212836864
      ],
      ""expected"": 3217625051
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        1073741823
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        1073741824
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        3221225472
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        2139095039
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        4286578687
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        2139095040
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        4286578688
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        1070141403
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        1078530011
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        1266679809
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""asin"",
      ""inputs"": [
        1325400064
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        0
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        2147483648
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        1
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        2147483649
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        8388607
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        8388608
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        1056964608
      ],
      ""expected"": 1065749138
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        3204448256
      ],
      ""expected"": 1074137746
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        1065353216
      ],
      ""expected"": 0
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        3212836864
      ],
      ""expected"": 1078530011
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        1073741823
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        1073741824
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        3221225472
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        2139095039
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        4286578687
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        2139095040
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        4286578688
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        1070141403
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        1078530011
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        1266679809
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""acos"",
      ""inputs"": [
        1325400064
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        0
      ],
      ""expected"": 0
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        2147483648
      ],
      ""expected"": 2147483648
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        1
      ],
      ""expected"": 1
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        2147483649
      ],
      ""expected"": 2147483649
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        8388607
      ],
      ""expected"": 8388607
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        8388608
      ],
      ""expected"": 8388608
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        1056964608
      ],
      ""expected"": 1055744824
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        3204448256
      ],
      ""expected"": 3203228472
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        1065353216
      ],
      ""expected"": 1061752795
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        3212836864
      ],
      ""expected"": 3209236443
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        1073741823
      ],
      ""expected"": 1066252044
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        1073741824
      ],
      ""expected"": 1066252045
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        3221225472
      ],
      ""expected"": 3213735693
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        2139095039
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        4286578687
      ],
      ""expected"": 3217625051
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        2139095040
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        4286578688
      ],
      ""expected"": 3217625051
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        1070141403
      ],
      ""expected"": 1065385804
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        1078530011
      ],
      ""expected"": 1067556293
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        1266679809
      ],
      ""expected"": 1070141402
    },
    {
      ""op"": ""atan"",
      ""inputs"": [
        1325400064
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        0,
        0
      ],
      ""expected"": 0
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        0,
        2147483648
      ],
      ""expected"": 1078530011
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        0,
        1065353216
      ],
      ""expected"": 0
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        0,
        3212836864
      ],
      ""expected"": 1078530011
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        0,
        2139095040
      ],
      ""expected"": 0
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        0,
        4286578688
      ],
      ""expected"": 1078530011
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        0,
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2147483648,
        0
      ],
      ""expected"": 2147483648
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2147483648,
        2147483648
      ],
      ""expected"": 3226013659
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2147483648,
        1065353216
      ],
      ""expected"": 2147483648
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2147483648,
        3212836864
      ],
      ""expected"": 3226013659
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2147483648,
        2139095040
      ],
      ""expected"": 2147483648
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2147483648,
        4286578688
      ],
      ""expected"": 3226013659
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2147483648,
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1065353216,
        0
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1065353216,
        2147483648
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1065353216,
        1065353216
      ],
      ""expected"": 1061752795
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1065353216,
        3212836864
      ],
      ""expected"": 1075235812
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1065353216,
        2139095040
      ],
      ""expected"": 0
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1065353216,
        4286578688
      ],
      ""expected"": 1078530011
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1065353216,
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        3212836864,
        0
      ],
      ""expected"": 3217625051
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        3212836864,
        2147483648
      ],
      ""expected"": 3217625051
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        3212836864,
        1065353216
      ],
      ""expected"": 3209236443
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        3212836864,
        3212836864
      ],
      ""expected"": 3222719460
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        3212836864,
        2139095040
      ],
      ""expected"": 2147483648
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        3212836864,
        4286578688
      ],
      ""expected"": 3226013659
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        3212836864,
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2139095040,
        0
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2139095040,
        2147483648
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2139095040,
        1065353216
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2139095040,
        3212836864
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2139095040,
        2139095040
      ],
      ""expected"": 1061752795
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2139095040,
        4286578688
      ],
      ""expected"": 1075235812
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2139095040,
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        4286578688,
        0
      ],
      ""expected"": 3217625051
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        4286578688,
        2147483648
      ],
      ""expected"": 3217625051
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        4286578688,
        1065353216
      ],
      ""expected"": 3217625051
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        4286578688,
        3212836864
      ],
      ""expected"": 3217625051
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        4286578688,
        2139095040
      ],
      ""expected"": 3209236443
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        4286578688,
        4286578688
      ],
      ""expected"": 3222719460
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        4286578688,
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2143289344,
        0
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2143289344,
        2147483648
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2143289344,
        1065353216
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2143289344,
        3212836864
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2143289344,
        2139095040
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2143289344,
        4286578688
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2143289344,
        2143289344
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        0,
        1056964608
      ],
      ""expected"": 0
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2147483648,
        1056964608
      ],
      ""expected"": 2147483648
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1,
        1056964608
      ],
      ""expected"": 2
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2147483649,
        1056964608
      ],
      ""expected"": 2147483650
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        8388607,
        1056964608
      ],
      ""expected"": 16777214
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        8388608,
        1056964608
      ],
      ""expected"": 16777216
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1056964608,
        1056964608
      ],
      ""expected"": 1061752795
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        3204448256,
        1056964608
      ],
      ""expected"": 3209236443
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1065353216,
        1056964608
      ],
      ""expected"": 1066252045
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        3212836864,
        1056964608
      ],
      ""expected"": 3213735693
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1073741823,
        1056964608
      ],
      ""expected"": 1068086373
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1073741824,
        1056964608
      ],
      ""expected"": 1068086373
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        3221225472,
        1056964608
      ],
      ""expected"": 3215570021
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2139095039,
        1056964608
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        4286578687,
        1056964608
      ],
      ""expected"": 3217625051
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2139095040,
        1056964608
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        4286578688,
        1056964608
      ],
      ""expected"": 3217625051
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2143289344,
        1056964608
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1070141403,
        1056964608
      ],
      ""expected"": 1067556293
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1078530011,
        1056964608
      ],
      ""expected"": 1068817419
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1266679809,
        1056964608
      ],
      ""expected"": 1070141402
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1325400064,
        1056964608
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        0,
        1073741824
      ],
      ""expected"": 0
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2147483648,
        1073741824
      ],
      ""expected"": 2147483648
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1,
        1073741824
      ],
      ""expected"": 0
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2147483649,
        1073741824
      ],
      ""expected"": 2147483648
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        8388607,
        1073741824
      ],
      ""expected"": 4194304
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        8388608,
        1073741824
      ],
      ""expected"": 4194304
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1056964608,
        1073741824
      ],
      ""expected"": 1048239024
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        3204448256,
        1073741824
      ],
      ""expected"": 3195722672
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1065353216,
        1073741824
      ],
      ""expected"": 1055744824
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        3212836864,
        1073741824
      ],
      ""expected"": 3203228472
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1073741823,
        1073741824
      ],
      ""expected"": 1061752794
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1073741824,
        1073741824
      ],
      ""expected"": 1061752795
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        3221225472,
        1073741824
      ],
      ""expected"": 3209236443
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2139095039,
        1073741824
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        4286578687,
        1073741824
      ],
      ""expected"": 3217625051
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2139095040,
        1073741824
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        4286578688,
        1073741824
      ],
      ""expected"": 3217625051
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        2143289344,
        1073741824
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1070141403,
        1073741824
      ],
      ""expected"": 1059745830
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1078530011,
        1073741824
      ],
      ""expected"": 1065385804
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1266679809,
        1073741824
      ],
      ""expected"": 1070141402
    },
    {
      ""op"": ""atan2"",
      ""inputs"": [
        1325400064,
        1073741824
      ],
      ""expected"": 1070141403
    },
    {
      ""op"": ""lerp"",
      ""inputs"": [
        3223322624,
        1073741824,
        1056964608
      ],
      ""expected"": 3196059648
    },
    {
      ""op"": ""lerp"",
      ""inputs"": [
        2147483648,
        0,
        1065353216
      ],
      ""expected"": 0
    },
    {
      ""op"": ""lerp"",
      ""inputs"": [
        1065353216,
        1077936128,
        3212836864
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""lerp"",
      ""inputs"": [
        1065353216,
        1077936128,
        1073741824
      ],
      ""expected"": 1077936128
    },
    {
      ""op"": ""lerp"",
      ""inputs"": [
        1077936128,
        1065353216,
        1048576000
      ],
      ""expected"": 1075838976
    },
    {
      ""op"": ""lerp"",
      ""inputs"": [
        1065353216,
        1065353216,
        1056964608
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""lerp"",
      ""inputs"": [
        0,
        1065353216,
        1056964608
      ],
      ""expected"": 1056964608
    },
    {
      ""op"": ""lerp"",
      ""inputs"": [
        2139095039,
        2139095039,
        1056964608
      ],
      ""expected"": 2139095039
    },
    {
      ""op"": ""lerp"",
      ""inputs"": [
        2139095039,
        1,
        1056964608
      ],
      ""expected"": 2130706431
    },
    {
      ""op"": ""lerp"",
      ""inputs"": [
        2139095040,
        1065353216,
        1056964608
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""lerp"",
      ""inputs"": [
        1065353216,
        2139095040,
        1056964608
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""lerp"",
      ""inputs"": [
        2143289344,
        1065353216,
        1056964608
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""lerp"",
      ""inputs"": [
        1065353216,
        2143289344,
        1056964608
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""lerp"",
      ""inputs"": [
        1065353216,
        3212836864,
        1056964608
      ],
      ""expected"": 0
    },
    {
      ""op"": ""inverselerp"",
      ""inputs"": [
        3223322624,
        1073741824,
        1056964608
      ],
      ""expected"": 1059760811
    },
    {
      ""op"": ""inverselerp"",
      ""inputs"": [
        2147483648,
        0,
        1065353216
      ],
      ""expected"": 0
    },
    {
      ""op"": ""inverselerp"",
      ""inputs"": [
        1065353216,
        1077936128,
        3212836864
      ],
      ""expected"": 0
    },
    {
      ""op"": ""inverselerp"",
      ""inputs"": [
        1065353216,
        1077936128,
        1073741824
      ],
      ""expected"": 1056964608
    },
    {
      ""op"": ""inverselerp"",
      ""inputs"": [
        1077936128,
        1065353216,
        1048576000
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""inverselerp"",
      ""inputs"": [
        1065353216,
        1065353216,
        1056964608
      ],
      ""expected"": 0
    },
    {
      ""op"": ""inverselerp"",
      ""inputs"": [
        0,
        1065353216,
        1056964608
      ],
      ""expected"": 1056964608
    },
    {
      ""op"": ""inverselerp"",
      ""inputs"": [
        2139095039,
        2139095039,
        1056964608
      ],
      ""expected"": 0
    },
    {
      ""op"": ""inverselerp"",
      ""inputs"": [
        2139095039,
        1,
        1056964608
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""inverselerp"",
      ""inputs"": [
        2139095040,
        1065353216,
        1056964608
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""inverselerp"",
      ""inputs"": [
        1065353216,
        2139095040,
        1056964608
      ],
      ""expected"": 2147483648
    },
    {
      ""op"": ""inverselerp"",
      ""inputs"": [
        2143289344,
        1065353216,
        1056964608
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""inverselerp"",
      ""inputs"": [
        1065353216,
        2143289344,
        1056964608
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""inverselerp"",
      ""inputs"": [
        1065353216,
        3212836864,
        1056964608
      ],
      ""expected"": 1048576000
    },
    {
      ""op"": ""movetowards"",
      ""inputs"": [
        3223322624,
        1073741824,
        1056964608
      ],
      ""expected"": 3221225472
    },
    {
      ""op"": ""movetowards"",
      ""inputs"": [
        2147483648,
        0,
        1065353216
      ],
      ""expected"": 0
    },
    {
      ""op"": ""movetowards"",
      ""inputs"": [
        1065353216,
        1077936128,
        3212836864
      ],
      ""expected"": 0
    },
    {
      ""op"": ""movetowards"",
      ""inputs"": [
        1065353216,
        1077936128,
        1073741824
      ],
      ""expected"": 1077936128
    },
    {
      ""op"": ""movetowards"",
      ""inputs"": [
        1077936128,
        1065353216,
        1048576000
      ],
      ""expected"": 1076887552
    },
    {
      ""op"": ""movetowards"",
      ""inputs"": [
        1065353216,
        1065353216,
        1056964608
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""movetowards"",
      ""inputs"": [
        0,
        1065353216,
        1056964608
      ],
      ""expected"": 1056964608
    },
    {
      ""op"": ""movetowards"",
      ""inputs"": [
        2139095039,
        2139095039,
        1056964608
      ],
      ""expected"": 2139095039
    },
    {
      ""op"": ""movetowards"",
      ""inputs"": [
        2139095039,
        1,
        1056964608
      ],
      ""expected"": 2139095039
    },
    {
      ""op"": ""movetowards"",
      ""inputs"": [
        2139095040,
        1065353216,
        1056964608
      ],
      ""expected"": 2139095040
    },
    {
      ""op"": ""movetowards"",
      ""inputs"": [
        1065353216,
        2139095040,
        1056964608
      ],
      ""expected"": 1069547520
    },
    {
      ""op"": ""movetowards"",
      ""inputs"": [
        2143289344,
        1065353216,
        1056964608
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""movetowards"",
      ""inputs"": [
        1065353216,
        2143289344,
        1056964608
      ],
      ""expected"": 1056964608
    },
    {
      ""op"": ""movetowards"",
      ""inputs"": [
        1065353216,
        3212836864,
        1056964608
      ],
      ""expected"": 1056964608
    },
    {
      ""op"": ""repeat"",
      ""inputs"": [
        3223322624,
        1073741824
      ],
      ""expected"": 1069547520
    },
    {
      ""op"": ""repeat"",
      ""inputs"": [
        2147483648,
        0
      ],
      ""error"": ""Math.Repeat requires length > 0.""
    },
    {
      ""op"": ""repeat"",
      ""inputs"": [
        1065353216,
        1077936128
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""repeat"",
      ""inputs"": [
        1065353216,
        1077936128
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""repeat"",
      ""inputs"": [
        1077936128,
        1065353216
      ],
      ""expected"": 0
    },
    {
      ""op"": ""repeat"",
      ""inputs"": [
        1065353216,
        1065353216
      ],
      ""expected"": 0
    },
    {
      ""op"": ""repeat"",
      ""inputs"": [
        0,
        1065353216
      ],
      ""expected"": 0
    },
    {
      ""op"": ""repeat"",
      ""inputs"": [
        2139095039,
        2139095039
      ],
      ""expected"": 0
    },
    {
      ""op"": ""repeat"",
      ""inputs"": [
        2139095039,
        1
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""repeat"",
      ""inputs"": [
        2139095040,
        1065353216
      ],
      ""error"": ""Math.Repeat requires finite t.""
    },
    {
      ""op"": ""repeat"",
      ""inputs"": [
        1065353216,
        2139095040
      ],
      ""error"": ""Math.Repeat requires finite length.""
    },
    {
      ""op"": ""repeat"",
      ""inputs"": [
        2143289344,
        1065353216
      ],
      ""error"": ""Math.Repeat requires finite t.""
    },
    {
      ""op"": ""repeat"",
      ""inputs"": [
        1065353216,
        2143289344
      ],
      ""error"": ""Math.Repeat requires finite length.""
    },
    {
      ""op"": ""repeat"",
      ""inputs"": [
        1065353216,
        3212836864
      ],
      ""error"": ""Math.Repeat requires length > 0.""
    },
    {
      ""op"": ""pingpong"",
      ""inputs"": [
        3223322624,
        1073741824
      ],
      ""expected"": 1069547520
    },
    {
      ""op"": ""pingpong"",
      ""inputs"": [
        2147483648,
        0
      ],
      ""error"": ""Math.PingPong requires length > 0.""
    },
    {
      ""op"": ""pingpong"",
      ""inputs"": [
        1065353216,
        1077936128
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pingpong"",
      ""inputs"": [
        1065353216,
        1077936128
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pingpong"",
      ""inputs"": [
        1077936128,
        1065353216
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pingpong"",
      ""inputs"": [
        1065353216,
        1065353216
      ],
      ""expected"": 1065353216
    },
    {
      ""op"": ""pingpong"",
      ""inputs"": [
        0,
        1065353216
      ],
      ""expected"": 0
    },
    {
      ""op"": ""pingpong"",
      ""inputs"": [
        2139095039,
        2139095039
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pingpong"",
      ""inputs"": [
        2139095039,
        1
      ],
      ""expected"": 2143289344
    },
    {
      ""op"": ""pingpong"",
      ""inputs"": [
        2139095040,
        1065353216
      ],
      ""error"": ""Math.PingPong requires finite t.""
    },
    {
      ""op"": ""pingpong"",
      ""inputs"": [
        1065353216,
        2139095040
      ],
      ""error"": ""Math.PingPong requires finite length.""
    },
    {
      ""op"": ""pingpong"",
      ""inputs"": [
        2143289344,
        1065353216
      ],
      ""error"": ""Math.PingPong requires finite t.""
    },
    {
      ""op"": ""pingpong"",
      ""inputs"": [
        1065353216,
        2143289344
      ],
      ""error"": ""Math.PingPong requires finite length.""
    },
    {
      ""op"": ""pingpong"",
      ""inputs"": [
        1065353216,
        3212836864
      ],
      ""error"": ""Math.PingPong requires length > 0.""
    }
  ]
}
";
    }
}
