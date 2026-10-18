// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

namespace NeoCompose.Tests
{
    // Mirrors neo-compose/src/models/neoscript/tuple-parity-fixture.json.
    internal static class NeoScriptTupleParityFixture
    {
        internal const string Json = @"[
  {
    ""name"": ""typed tuple membership conversion"",
    ""source"": ""List<(float, int)> values = [(1.0, 2)]; return values.ToSet().Contains((1, 2));"",
    ""returnType"": ""bool"",
    ""expected"": true,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
                ""type"": 30,
                ""required"": true,
                ""elements"": [
                  {
                    ""typeInfo"": {
                      ""type"": 4,
                      ""required"": true
                    }
                  },
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                ]
              }
            },
            ""pointer"": {
              ""type"": ""listLiteral"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 30,
                  ""required"": true,
                  ""elements"": [
                    {
                      ""typeInfo"": {
                        ""type"": 4,
                        ""required"": true
                      }
                    },
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  ]
                }
              },
              ""entries"": [
                {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleCreate"",
                    ""info"": {
                      ""elementPointers"": [
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
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 4,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 30
                  }
                }
              ]
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
              ""type"": ""contains"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""collectionQuery"",
                    ""info"": {
                      ""collectionPointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""values""
                      },
                      ""op"": ""ToSet""
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 60
                  }
                },
                ""valuePointer"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleCreate"",
                    ""info"": {
                      ""elementPointers"": [
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
                      ],
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 4,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 72
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
            ""column"": 41
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""decimal tuple set scale"",
    ""source"": ""var values = [(1.00, 2), (1.0, 2)].ToSet(); return values.Count;"",
    ""returnType"": ""int"",
    ""expected"": 1,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
              ""type"": 9,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 30,
                ""required"": true,
                ""elements"": [
                  {
                    ""typeInfo"": {
                      ""type"": 4,
                      ""required"": true
                    }
                  },
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                ]
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
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 4,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    },
                    ""entries"": [
                      {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""tupleCreate"",
                          ""info"": {
                            ""elementPointers"": [
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
                            ""typeInfo"": {
                              ""type"": 30,
                              ""required"": true,
                              ""elements"": [
                                {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  }
                                },
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                }
                              ]
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 15
                        }
                      },
                      {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""tupleCreate"",
                          ""info"": {
                            ""elementPointers"": [
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
                            ""typeInfo"": {
                              ""type"": 30,
                              ""required"": true,
                              ""elements"": [
                                {
                                  ""typeInfo"": {
                                    ""type"": 4,
                                    ""required"": true
                                  }
                                },
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                }
                              ]
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 26
                        }
                      }
                    ]
                  },
                  ""op"": ""ToSet""
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 41
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
              ""type"": ""count"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""values""
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 58
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 45
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""three elements"",
    ""source"": ""return (1, 2, 3).Item3;"",
    ""returnType"": ""int"",
    ""expected"": 3,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
              ""type"": ""tupleElement"",
              ""info"": {
                ""tuplePointer"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleCreate"",
                    ""info"": {
                      ""elementPointers"": [
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
                      ],
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 8
                  }
                },
                ""index"": 2
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
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""tuple collection alias mutation"",
    ""source"": ""var values = [1]; var pair = (values, 0); pair.Item1.Add(2); return values.Count + pair.Item1.Count;"",
    ""returnType"": ""int"",
    ""expected"": 4,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
                    ""value"": 1
                  }
                }
              ]
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
            ""id"": ""pair"",
            ""typeInfo"": {
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              ]
            },
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""tupleCreate"",
                ""info"": {
                  ""elementPointers"": [
                    {
                      ""type"": ""variable"",
                      ""variableId"": ""values""
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
                  ],
                  ""typeInfo"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 6,
                          ""required"": true,
                          ""entryTypeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        }
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      }
                    ]
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 30
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 19
          }
        },
        {
          ""type"": ""collectionCall"",
          ""target"": {
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""tupleElement"",
                ""info"": {
                  ""tuplePointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""pair""
                  },
                  ""index"": 0
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 47
              }
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
                ""value"": 2
              }
            }
          ],
          ""source"": {
            ""line"": 1,
            ""column"": 43
          }
        },
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
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""count"",
                      ""info"": {
                        ""collectionPointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""values""
                        }
                      }
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 75
                    }
                  },
                  {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""count"",
                      ""info"": {
                        ""collectionPointer"": {
                          ""type"": ""function"",
                          ""function"": {
                            ""type"": ""tupleElement"",
                            ""info"": {
                              ""tuplePointer"": {
                                ""type"": ""variable"",
                                ""variableId"": ""pair""
                              },
                              ""index"": 0
                            }
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 88
                          }
                        }
                      }
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 94
                    }
                  }
                ],
                ""numeric"": ""int""
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 62
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""tuple handle identity after mutation"",
    ""source"": ""var values = [1]; var pair = (values, 0); values.Add(2); return pair.Item1 == values && pair.Item1.Count == 2;"",
    ""returnType"": ""bool"",
    ""expected"": true,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
                    ""value"": 1
                  }
                }
              ]
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
            ""id"": ""pair"",
            ""typeInfo"": {
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              ]
            },
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""tupleCreate"",
                ""info"": {
                  ""elementPointers"": [
                    {
                      ""type"": ""variable"",
                      ""variableId"": ""values""
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
                  ],
                  ""typeInfo"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 6,
                          ""required"": true,
                          ""entryTypeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        }
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      }
                    ]
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 30
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 19
          }
        },
        {
          ""type"": ""collectionCall"",
          ""target"": {
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""values""
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
                ""value"": 2
              }
            }
          ],
          ""source"": {
            ""line"": 1,
            ""column"": 43
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
                    ""type"": ""operation"",
                    ""operation"": {
                      ""type"": ""boolean"",
                      ""expression"": {
                        ""condition"": {
                          ""type"": ""equalTo"",
                          ""operand1"": {
                            ""type"": ""function"",
                            ""function"": {
                              ""type"": ""tupleElement"",
                              ""info"": {
                                ""tuplePointer"": {
                                  ""type"": ""variable"",
                                  ""variableId"": ""pair""
                                },
                                ""index"": 0
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 69
                            }
                          },
                          ""operand2"": {
                            ""type"": ""variable"",
                            ""variableId"": ""values""
                          },
                          ""reference"": true
                        }
                      }
                    }
                  },
                  ""operand2"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 1,
                        ""required"": true
                      },
                      ""value"": true
                    }
                  }
                },
                ""connective"": {
                  ""type"": ""&&"",
                  ""to"": {
                    ""condition"": {
                      ""type"": ""equalTo"",
                      ""operand1"": {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""count"",
                          ""info"": {
                            ""collectionPointer"": {
                              ""type"": ""function"",
                              ""function"": {
                                ""type"": ""tupleElement"",
                                ""info"": {
                                  ""tuplePointer"": {
                                    ""type"": ""variable"",
                                    ""variableId"": ""pair""
                                  },
                                  ""index"": 0
                                }
                              },
                              ""source"": {
                                ""line"": 1,
                                ""column"": 93
                              }
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 99
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
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 58
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""tuple handle replacement snapshot"",
    ""source"": ""var values = [1]; var pair = (values, 0); values = [2, 3]; return pair.Item1.Count;"",
    ""returnType"": ""int"",
    ""expected"": 1,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
                    ""value"": 1
                  }
                }
              ]
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
            ""id"": ""pair"",
            ""typeInfo"": {
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              ]
            },
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""tupleCreate"",
                ""info"": {
                  ""elementPointers"": [
                    {
                      ""type"": ""variable"",
                      ""variableId"": ""values""
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
                  ],
                  ""typeInfo"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 6,
                          ""required"": true,
                          ""entryTypeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        }
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      }
                    ]
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 30
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 19
          }
        },
        {
          ""type"": ""assign"",
          ""target"": {
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""values""
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
          ""operator"": ""="",
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
          ""source"": {
            ""line"": 1,
            ""column"": 43
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
                    ""type"": ""tupleElement"",
                    ""info"": {
                      ""tuplePointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""pair""
                      },
                      ""index"": 0
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 71
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
            ""column"": 60
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""nested destructuring"",
    ""source"": ""var (a, (_, b)) = (1, (2, 3)); (int _, int _) = (4, 5); return a + b;"",
    ""returnType"": ""int"",
    ""expected"": 4,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
            ""id"": ""#tuple10"",
            ""typeInfo"": {
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                {
                  ""typeInfo"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      }
                    ]
                  }
                }
              ]
            },
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""tupleCreate"",
                ""info"": {
                  ""elementPointers"": [
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
                      ""type"": ""function"",
                      ""function"": {
                        ""type"": ""tupleCreate"",
                        ""info"": {
                          ""elementPointers"": [
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
                          ],
                          ""typeInfo"": {
                            ""type"": 30,
                            ""required"": true,
                            ""elements"": [
                              {
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                }
                              },
                              {
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                }
                              }
                            ]
                          }
                        }
                      },
                      ""source"": {
                        ""line"": 1,
                        ""column"": 23
                      }
                    }
                  ],
                  ""typeInfo"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 30,
                          ""required"": true,
                          ""elements"": [
                            {
                              ""typeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              }
                            },
                            {
                              ""typeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              }
                            }
                          ]
                        }
                      }
                    ]
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 19
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
            ""id"": ""a"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""tupleElement"",
                ""info"": {
                  ""tuplePointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""#tuple10""
                  },
                  ""index"": 0
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 6
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 6
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""b"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""tupleElement"",
                ""info"": {
                  ""tuplePointer"": {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""tupleElement"",
                      ""info"": {
                        ""tuplePointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""#tuple10""
                        },
                        ""index"": 1
                      }
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 9
                    }
                  },
                  ""index"": 1
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 13
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 13
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""#tuple28"",
            ""typeInfo"": {
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              ]
            },
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""tupleCreate"",
                ""info"": {
                  ""elementPointers"": [
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
                  ],
                  ""typeInfo"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      }
                    ]
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 49
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 32
          }
        },
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
            ""column"": 57
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""tuple iteration"",
    ""source"": ""int sum = 0; foreach (var (a, b) in [(1, 2), (3, 4)]) sum += a + b; return sum;"",
    ""returnType"": ""int"",
    ""expected"": 10,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
          },
          ""source"": {
            ""line"": 1,
            ""column"": 1
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""#foreach13#collection"",
            ""typeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 30,
                ""required"": true,
                ""elements"": [
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                ]
              }
            },
            ""pointer"": {
              ""type"": ""listLiteral"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 30,
                  ""required"": true,
                  ""elements"": [
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    },
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  ]
                }
              },
              ""entries"": [
                {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleCreate"",
                    ""info"": {
                      ""elementPointers"": [
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
                      ],
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 38
                  }
                },
                {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleCreate"",
                    ""info"": {
                      ""elementPointers"": [
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
                        }
                      ],
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 46
                  }
                }
              ]
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 14
          }
        },
        {
          ""type"": ""forEach"",
          ""binding"": {
            ""id"": ""#foreach13"",
            ""typeInfo"": {
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              ]
            },
            ""readonly"": true,
            ""writability"": ""local""
          },
          ""collectionPointer"": {
            ""type"": ""variable"",
            ""variableId"": ""#foreach13#collection""
          },
          ""collectionTypeInfo"": {
            ""type"": 6,
            ""required"": true,
            ""entryTypeInfo"": {
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              ]
            }
          },
          ""instructions"": [
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""#foreach13#pattern"",
                ""typeInfo"": {
                  ""type"": 30,
                  ""required"": true,
                  ""elements"": [
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    },
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  ]
                },
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""#foreach13""
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 14
              }
            },
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""a"",
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""pointer"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleElement"",
                    ""info"": {
                      ""tuplePointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""#foreach13#pattern""
                      },
                      ""index"": 0
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 28
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 28
              }
            },
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""b"",
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""pointer"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleElement"",
                    ""info"": {
                      ""tuplePointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""#foreach13#pattern""
                      },
                      ""index"": 1
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 31
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 31
              }
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
                      }
                    ],
                    ""numeric"": ""int""
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 55
              }
            }
          ],
          ""source"": {
            ""line"": 1,
            ""column"": 14
          }
        },
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""variable"",
            ""variableId"": ""sum""
          },
          ""source"": {
            ""line"": 1,
            ""column"": 69
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""lambda destructuring"",
    ""source"": ""return [(1, 2), (3, 4)].Select(((a, b)) => a + b).Sum();"",
    ""returnType"": ""int"",
    ""expected"": 10,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
                    ""type"": ""select"",
                    ""info"": {
                      ""collectionPointer"": {
                        ""type"": ""listLiteral"",
                        ""typeInfo"": {
                          ""type"": 6,
                          ""required"": true,
                          ""entryTypeInfo"": {
                            ""type"": 30,
                            ""required"": true,
                            ""elements"": [
                              {
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                }
                              },
                              {
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                }
                              }
                            ]
                          }
                        },
                        ""entries"": [
                          {
                            ""type"": ""function"",
                            ""function"": {
                              ""type"": ""tupleCreate"",
                              ""info"": {
                                ""elementPointers"": [
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
                                ],
                                ""typeInfo"": {
                                  ""type"": 30,
                                  ""required"": true,
                                  ""elements"": [
                                    {
                                      ""typeInfo"": {
                                        ""type"": 2,
                                        ""required"": true
                                      }
                                    },
                                    {
                                      ""typeInfo"": {
                                        ""type"": 2,
                                        ""required"": true
                                      }
                                    }
                                  ]
                                }
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 9
                            }
                          },
                          {
                            ""type"": ""function"",
                            ""function"": {
                              ""type"": ""tupleCreate"",
                              ""info"": {
                                ""elementPointers"": [
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
                                  }
                                ],
                                ""typeInfo"": {
                                  ""type"": 30,
                                  ""required"": true,
                                  ""elements"": [
                                    {
                                      ""typeInfo"": {
                                        ""type"": 2,
                                        ""required"": true
                                      }
                                    },
                                    {
                                      ""typeInfo"": {
                                        ""type"": 2,
                                        ""required"": true
                                      }
                                    }
                                  ]
                                }
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 17
                            }
                          }
                        ]
                      },
                      ""function"": {
                        ""compilerRevision"": 17,
                        ""source"": {
                          ""coordinateSpace"": ""body"",
                          ""name"": ""getter <callback>""
                        },
                        ""parameters"": [
                          {
                            ""id"": ""#lambda23"",
                            ""typeInfo"": {
                              ""type"": 30,
                              ""required"": true,
                              ""elements"": [
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                },
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                }
                              ]
                            },
                            ""pointer"": {
                              ""type"": ""value"",
                              ""value"": {
                                ""typeInfo"": {
                                  ""type"": 30,
                                  ""required"": true,
                                  ""elements"": [
                                    {
                                      ""typeInfo"": {
                                        ""type"": 2,
                                        ""required"": true
                                      }
                                    },
                                    {
                                      ""typeInfo"": {
                                        ""type"": 2,
                                        ""required"": true
                                      }
                                    }
                                  ]
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
                              ""id"": ""#lambda23#pattern"",
                              ""typeInfo"": {
                                ""type"": 30,
                                ""required"": true,
                                ""elements"": [
                                  {
                                    ""typeInfo"": {
                                      ""type"": 2,
                                      ""required"": true
                                    }
                                  },
                                  {
                                    ""typeInfo"": {
                                      ""type"": 2,
                                      ""required"": true
                                    }
                                  }
                                ]
                              },
                              ""pointer"": {
                                ""type"": ""variable"",
                                ""variableId"": ""#lambda23""
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 33
                            }
                          },
                          {
                            ""type"": ""variable"",
                            ""variable"": {
                              ""id"": ""a"",
                              ""typeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              },
                              ""pointer"": {
                                ""type"": ""function"",
                                ""function"": {
                                  ""type"": ""tupleElement"",
                                  ""info"": {
                                    ""tuplePointer"": {
                                      ""type"": ""variable"",
                                      ""variableId"": ""#lambda23#pattern""
                                    },
                                    ""index"": 0
                                  }
                                },
                                ""source"": {
                                  ""line"": 1,
                                  ""column"": 34
                                }
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 34
                            }
                          },
                          {
                            ""type"": ""variable"",
                            ""variable"": {
                              ""id"": ""b"",
                              ""typeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              },
                              ""pointer"": {
                                ""type"": ""function"",
                                ""function"": {
                                  ""type"": ""tupleElement"",
                                  ""info"": {
                                    ""tuplePointer"": {
                                      ""type"": ""variable"",
                                      ""variableId"": ""#lambda23#pattern""
                                    },
                                    ""index"": 1
                                  }
                                },
                                ""source"": {
                                  ""line"": 1,
                                  ""column"": 37
                                }
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 37
                            }
                          },
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
                              ""column"": 44
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
                    ""column"": 31
                  }
                },
                ""op"": ""Sum"",
                ""keyType"": ""int"",
                ""nullable"": false
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 54
            }
          },
          ""source"": {
            ""line"": 1,
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
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""typed tuple parameter"",
    ""source"": ""NeoDelegate<int, (int, int)> f = ((a, b)) => a + b; return f((3, 4));"",
    ""returnType"": ""int"",
    ""expected"": 7,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
            ""id"": ""f"",
            ""typeInfo"": {
              ""type"": 25,
              ""required"": true,
              ""returnTypeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""argumentTypes"": [
                {
                  ""type"": 30,
                  ""required"": true,
                  ""elements"": [
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    },
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  ]
                }
              ]
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
                  ""argumentTypes"": [
                    {
                      ""type"": 30,
                      ""required"": true,
                      ""elements"": [
                        {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        },
                        {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        }
                      ]
                    }
                  ]
                },
                ""value"": {
                  ""code"": ""((a, b)) => a + b"",
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
                          ""classId"": ""class:inventory""
                        },
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 7,
                              ""required"": true,
                              ""classId"": ""class:inventory""
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
                          ""classId"": ""synthetic:root""
                        },
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 7,
                              ""required"": true,
                              ""classId"": ""synthetic:root""
                            },
                            ""value"": null
                          }
                        }
                      },
                      {
                        ""id"": ""__lambda_0_arg_0__"",
                        ""typeInfo"": {
                          ""type"": 30,
                          ""required"": true,
                          ""elements"": [
                            {
                              ""typeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              }
                            },
                            {
                              ""typeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              }
                            }
                          ]
                        },
                        ""pointer"": {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 30,
                              ""required"": true,
                              ""elements"": [
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                },
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                }
                              ]
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
                          ""id"": ""#lambda18#pattern"",
                          ""typeInfo"": {
                            ""type"": 30,
                            ""required"": true,
                            ""elements"": [
                              {
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                }
                              },
                              {
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                }
                              }
                            ]
                          },
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""__lambda_0_arg_0__""
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 35
                        }
                      },
                      {
                        ""type"": ""variable"",
                        ""variable"": {
                          ""id"": ""a"",
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""pointer"": {
                            ""type"": ""function"",
                            ""function"": {
                              ""type"": ""tupleElement"",
                              ""info"": {
                                ""tuplePointer"": {
                                  ""type"": ""variable"",
                                  ""variableId"": ""#lambda18#pattern""
                                },
                                ""index"": 0
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 36
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 36
                        }
                      },
                      {
                        ""type"": ""variable"",
                        ""variable"": {
                          ""id"": ""b"",
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""pointer"": {
                            ""type"": ""function"",
                            ""function"": {
                              ""type"": ""tupleElement"",
                              ""info"": {
                                ""tuplePointer"": {
                                  ""type"": ""variable"",
                                  ""variableId"": ""#lambda18#pattern""
                                },
                                ""index"": 1
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 39
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 39
                        }
                      },
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
                          ""column"": 46
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
            ""line"": 1,
            ""column"": 1
          }
        },
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""callDelegate"",
            ""delegate"": {
              ""type"": ""variable"",
              ""variableId"": ""f""
            },
            ""args"": [
              {
                ""type"": ""function"",
                ""function"": {
                  ""type"": ""tupleCreate"",
                  ""info"": {
                    ""elementPointers"": [
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
                      }
                    ],
                    ""typeInfo"": {
                      ""type"": 30,
                      ""required"": true,
                      ""elements"": [
                        {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        },
                        {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        }
                      ]
                    }
                  }
                },
                ""source"": {
                  ""line"": 1,
                  ""column"": 62
                }
              }
            ],
            ""callSiteId"": ""1:61:0"",
            ""source"": {
              ""line"": 1,
              ""column"": 61
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 53
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""dictionary receiver evaluated once"",
    ""source"": ""List<int> calls = []; NeoDelegate<Dictionary<string, (int, int)>> read = () => { calls.Add(1); return {\""a\"": (3, 4)}; }; int total = 0; foreach (var (key, (a, b)) in read()) total += a + b; return total + calls.Count;"",
    ""returnType"": ""int"",
    ""expected"": 8,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
            ""id"": ""calls"",
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
              ""entries"": []
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
            ""id"": ""read"",
            ""typeInfo"": {
              ""type"": 25,
              ""required"": true,
              ""returnTypeInfo"": {
                ""type"": 5,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 30,
                  ""required"": true,
                  ""elements"": [
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    },
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  ]
                }
              },
              ""argumentTypes"": []
            },
            ""pointer"": {
              ""type"": ""delegateClosure"",
              ""typeInfo"": {
                ""type"": 25,
                ""required"": true,
                ""returnTypeInfo"": {
                  ""type"": 5,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      }
                    ]
                  }
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
                      ""classId"": ""class:inventory""
                    },
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 7,
                          ""required"": true,
                          ""classId"": ""class:inventory""
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
                      ""classId"": ""synthetic:root""
                    },
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 7,
                          ""required"": true,
                          ""classId"": ""synthetic:root""
                        },
                        ""value"": null
                      }
                    }
                  },
                  {
                    ""id"": ""__capture_0_0__"",
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
                    ""type"": ""collectionCall"",
                    ""target"": {
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""__capture_0_0__""
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
                          ""value"": 1
                        }
                      }
                    ],
                    ""source"": {
                      ""line"": 1,
                      ""column"": 82
                    }
                  },
                  {
                    ""type"": ""return"",
                    ""pointer"": {
                      ""type"": ""dictLiteral"",
                      ""typeInfo"": {
                        ""type"": 5,
                        ""required"": true,
                        ""entryTypeInfo"": {
                          ""type"": 30,
                          ""required"": true,
                          ""elements"": [
                            {
                              ""typeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              }
                            },
                            {
                              ""typeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              }
                            }
                          ]
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
                              ""value"": ""a""
                            }
                          },
                          ""value"": {
                            ""type"": ""function"",
                            ""function"": {
                              ""type"": ""tupleCreate"",
                              ""info"": {
                                ""elementPointers"": [
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
                                  }
                                ],
                                ""typeInfo"": {
                                  ""type"": 30,
                                  ""required"": true,
                                  ""elements"": [
                                    {
                                      ""typeInfo"": {
                                        ""type"": 2,
                                        ""required"": true
                                      }
                                    },
                                    {
                                      ""typeInfo"": {
                                        ""type"": 2,
                                        ""required"": true
                                      }
                                    }
                                  ]
                                }
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 109
                            }
                          }
                        }
                      ]
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 96
                    }
                  }
                ],
                ""typeInfo"": {
                  ""type"": 5,
                  ""required"": true,
                  ""entryTypeInfo"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      }
                    ]
                  }
                }
              },
              ""captures"": [
                {
                  ""type"": ""variable"",
                  ""variableId"": ""calls""
                }
              ],
              ""code"": ""() => { calls.Add(1); return {\""a\"": (3, 4)}; }""
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 23
          }
        },
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
          },
          ""source"": {
            ""line"": 1,
            ""column"": 121
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""#foreach65#collection"",
            ""typeInfo"": {
              ""type"": 5,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 30,
                ""required"": true,
                ""elements"": [
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                ]
              }
            },
            ""pointer"": {
              ""type"": ""callDelegate"",
              ""delegate"": {
                ""type"": ""variable"",
                ""variableId"": ""read""
              },
              ""args"": [],
              ""callSiteId"": ""1:170:0"",
              ""source"": {
                ""line"": 1,
                ""column"": 170
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 136
          }
        },
        {
          ""type"": ""forEach"",
          ""binding"": {
            ""id"": ""#foreach65#key"",
            ""typeInfo"": {
              ""type"": 3,
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
                  ""type"": ""variable"",
                  ""variableId"": ""#foreach65#collection""
                },
                ""function"": {
                  ""compilerRevision"": 17,
                  ""source"": {
                    ""name"": ""getter"",
                    ""coordinateSpace"": ""body""
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
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      },
                      ""pointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 30,
                            ""required"": true,
                            ""elements"": [
                              {
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                }
                              },
                              {
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                }
                              }
                            ]
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
            }
          },
          ""collectionTypeInfo"": {
            ""type"": 6,
            ""required"": true,
            ""entryTypeInfo"": {
              ""type"": 3,
              ""required"": true
            },
            ""readOnly"": true
          },
          ""instructions"": [
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""#foreach65#pattern"",
                ""typeInfo"": {
                  ""type"": 30,
                  ""required"": true,
                  ""elements"": [
                    {
                      ""typeInfo"": {
                        ""type"": 3,
                        ""required"": true
                      }
                    },
                    {
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    }
                  ]
                },
                ""pointer"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleCreate"",
                    ""info"": {
                      ""elementPointers"": [
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""#foreach65#key""
                        },
                        {
                          ""type"": ""keyOf"",
                          ""keyOf"": {
                            ""pointer"": {
                              ""type"": ""variable"",
                              ""variableId"": ""#foreach65#collection""
                            },
                            ""key"": {
                              ""type"": ""variable"",
                              ""variableId"": ""#foreach65#key""
                            }
                          }
                        }
                      ],
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 3,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 30,
                              ""required"": true,
                              ""elements"": [
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                },
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                }
                              ]
                            }
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 136
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 136
              }
            },
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""key"",
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""pointer"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleElement"",
                    ""info"": {
                      ""tuplePointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""#foreach65#pattern""
                      },
                      ""index"": 0
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 150
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 150
              }
            },
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""a"",
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""pointer"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleElement"",
                    ""info"": {
                      ""tuplePointer"": {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""tupleElement"",
                          ""info"": {
                            ""tuplePointer"": {
                              ""type"": ""variable"",
                              ""variableId"": ""#foreach65#pattern""
                            },
                            ""index"": 1
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 155
                        }
                      },
                      ""index"": 0
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 156
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 156
              }
            },
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""b"",
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""pointer"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleElement"",
                    ""info"": {
                      ""tuplePointer"": {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""tupleElement"",
                          ""info"": {
                            ""tuplePointer"": {
                              ""type"": ""variable"",
                              ""variableId"": ""#foreach65#pattern""
                            },
                            ""index"": 1
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 155
                        }
                      },
                      ""index"": 1
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 159
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 159
              }
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
                      }
                    ],
                    ""numeric"": ""int""
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 174
              }
            }
          ],
          ""source"": {
            ""line"": 1,
            ""column"": 136
          }
        },
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
                    ""variableId"": ""total""
                  },
                  {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""count"",
                      ""info"": {
                        ""collectionPointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""calls""
                        }
                      }
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 210
                    }
                  }
                ],
                ""numeric"": ""int""
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 190
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""membership"",
    ""source"": ""var values = [(1, 2), (3, 4)]; return values.Contains((1, 2)) && values.IndexOf((3, 4)) == 1;"",
    ""returnType"": ""bool"",
    ""expected"": true,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
                ""type"": 30,
                ""required"": true,
                ""elements"": [
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                ]
              }
            },
            ""pointer"": {
              ""type"": ""listLiteral"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 30,
                  ""required"": true,
                  ""elements"": [
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    },
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  ]
                }
              },
              ""entries"": [
                {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleCreate"",
                    ""info"": {
                      ""elementPointers"": [
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
                      ],
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 15
                  }
                },
                {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleCreate"",
                    ""info"": {
                      ""elementPointers"": [
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
                        }
                      ],
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 23
                  }
                }
              ]
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
            ""type"": ""operation"",
            ""operation"": {
              ""type"": ""boolean"",
              ""expression"": {
                ""condition"": {
                  ""type"": ""equalTo"",
                  ""operand1"": {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""contains"",
                      ""info"": {
                        ""collectionPointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""values""
                        },
                        ""valuePointer"": {
                          ""type"": ""function"",
                          ""function"": {
                            ""type"": ""tupleCreate"",
                            ""info"": {
                              ""elementPointers"": [
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
                              ],
                              ""typeInfo"": {
                                ""type"": 30,
                                ""required"": true,
                                ""elements"": [
                                  {
                                    ""typeInfo"": {
                                      ""type"": 2,
                                      ""required"": true
                                    }
                                  },
                                  {
                                    ""typeInfo"": {
                                      ""type"": 2,
                                      ""required"": true
                                    }
                                  }
                                ]
                              }
                            }
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 55
                          }
                        }
                      }
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 54
                    }
                  },
                  ""operand2"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 1,
                        ""required"": true
                      },
                      ""value"": true
                    }
                  }
                },
                ""connective"": {
                  ""type"": ""&&"",
                  ""to"": {
                    ""condition"": {
                      ""type"": ""equalTo"",
                      ""operand1"": {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""indexOf"",
                          ""info"": {
                            ""collectionPointer"": {
                              ""type"": ""variable"",
                              ""variableId"": ""values""
                            },
                            ""valuePointer"": {
                              ""type"": ""function"",
                              ""function"": {
                                ""type"": ""tupleCreate"",
                                ""info"": {
                                  ""elementPointers"": [
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
                                    }
                                  ],
                                  ""typeInfo"": {
                                    ""type"": 30,
                                    ""required"": true,
                                    ""elements"": [
                                      {
                                        ""typeInfo"": {
                                          ""type"": 2,
                                          ""required"": true
                                        }
                                      },
                                      {
                                        ""typeInfo"": {
                                          ""type"": 2,
                                          ""required"": true
                                        }
                                      }
                                    ]
                                  }
                                }
                              },
                              ""source"": {
                                ""line"": 1,
                                ""column"": 81
                              }
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 80
                        }
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
          },
          ""source"": {
            ""line"": 1,
            ""column"": 32
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""set deduplication"",
    ""source"": ""var values = [(1, 2), (1, 2), (3, 4)].ToSet(); return values.Count;"",
    ""returnType"": ""int"",
    ""expected"": 2,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
              ""type"": 9,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 30,
                ""required"": true,
                ""elements"": [
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                ]
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
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    },
                    ""entries"": [
                      {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""tupleCreate"",
                          ""info"": {
                            ""elementPointers"": [
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
                            ],
                            ""typeInfo"": {
                              ""type"": 30,
                              ""required"": true,
                              ""elements"": [
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                },
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                }
                              ]
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 15
                        }
                      },
                      {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""tupleCreate"",
                          ""info"": {
                            ""elementPointers"": [
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
                            ],
                            ""typeInfo"": {
                              ""type"": 30,
                              ""required"": true,
                              ""elements"": [
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                },
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                }
                              ]
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 23
                        }
                      },
                      {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""tupleCreate"",
                          ""info"": {
                            ""elementPointers"": [
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
                              }
                            ],
                            ""typeInfo"": {
                              ""type"": 30,
                              ""required"": true,
                              ""elements"": [
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                },
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                }
                              ]
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 31
                        }
                      }
                    ]
                  },
                  ""op"": ""ToSet""
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 44
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
              ""type"": ""count"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""values""
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 61
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 48
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""removal"",
    ""source"": ""var values = [(1, 2), (3, 4)]; values.Remove((1, 2)); return values.Count;"",
    ""returnType"": ""int"",
    ""expected"": 1,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
                ""type"": 30,
                ""required"": true,
                ""elements"": [
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                ]
              }
            },
            ""pointer"": {
              ""type"": ""listLiteral"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 30,
                  ""required"": true,
                  ""elements"": [
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    },
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  ]
                }
              },
              ""entries"": [
                {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleCreate"",
                    ""info"": {
                      ""elementPointers"": [
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
                      ],
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 15
                  }
                },
                {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleCreate"",
                    ""info"": {
                      ""elementPointers"": [
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
                        }
                      ],
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 23
                  }
                }
              ]
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 1
          }
        },
        {
          ""type"": ""collectionCall"",
          ""target"": {
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""values""
            },
            ""typeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 30,
                ""required"": true,
                ""elements"": [
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                ]
              }
            },
            ""writability"": ""local""
          },
          ""mutation"": ""Remove"",
          ""args"": [
            {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""tupleCreate"",
                ""info"": {
                  ""elementPointers"": [
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
                  ],
                  ""typeInfo"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      }
                    ]
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 46
              }
            }
          ],
          ""source"": {
            ""line"": 1,
            ""column"": 32
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
                  ""type"": ""variable"",
                  ""variableId"": ""values""
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 68
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 55
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""handle identity"",
    ""source"": ""var a = [1]; var b = [1]; return (a, 1) != (b, 1) && (a, 1) == (a, 1);"",
    ""returnType"": ""bool"",
    ""expected"": true,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
            ""column"": 1
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
            ""column"": 14
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
                    ""type"": ""operation"",
                    ""operation"": {
                      ""type"": ""boolean"",
                      ""expression"": {
                        ""condition"": {
                          ""type"": ""equalTo"",
                          ""operand1"": {
                            ""type"": ""function"",
                            ""function"": {
                              ""type"": ""tupleEqual"",
                              ""info"": {
                                ""left"": {
                                  ""type"": ""function"",
                                  ""function"": {
                                    ""type"": ""tupleCreate"",
                                    ""info"": {
                                      ""elementPointers"": [
                                        {
                                          ""type"": ""variable"",
                                          ""variableId"": ""a""
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
                                      ""typeInfo"": {
                                        ""type"": 30,
                                        ""required"": true,
                                        ""elements"": [
                                          {
                                            ""typeInfo"": {
                                              ""type"": 6,
                                              ""required"": true,
                                              ""entryTypeInfo"": {
                                                ""type"": 2,
                                                ""required"": true
                                              }
                                            }
                                          },
                                          {
                                            ""typeInfo"": {
                                              ""type"": 2,
                                              ""required"": true
                                            }
                                          }
                                        ]
                                      }
                                    }
                                  },
                                  ""source"": {
                                    ""line"": 1,
                                    ""column"": 34
                                  }
                                },
                                ""right"": {
                                  ""type"": ""function"",
                                  ""function"": {
                                    ""type"": ""tupleCreate"",
                                    ""info"": {
                                      ""elementPointers"": [
                                        {
                                          ""type"": ""variable"",
                                          ""variableId"": ""b""
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
                                      ""typeInfo"": {
                                        ""type"": 30,
                                        ""required"": true,
                                        ""elements"": [
                                          {
                                            ""typeInfo"": {
                                              ""type"": 6,
                                              ""required"": true,
                                              ""entryTypeInfo"": {
                                                ""type"": 2,
                                                ""required"": true
                                              }
                                            }
                                          },
                                          {
                                            ""typeInfo"": {
                                              ""type"": 2,
                                              ""required"": true
                                            }
                                          }
                                        ]
                                      }
                                    }
                                  },
                                  ""source"": {
                                    ""line"": 1,
                                    ""column"": 44
                                  }
                                },
                                ""typeInfo"": {
                                  ""type"": 30,
                                  ""required"": true,
                                  ""elements"": [
                                    {
                                      ""typeInfo"": {
                                        ""type"": 6,
                                        ""required"": true,
                                        ""entryTypeInfo"": {
                                          ""type"": 2,
                                          ""required"": true
                                        }
                                      }
                                    },
                                    {
                                      ""typeInfo"": {
                                        ""type"": 2,
                                        ""required"": true
                                      }
                                    }
                                  ]
                                }
                              }
                            }
                          },
                          ""operand2"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 1,
                                ""required"": true
                              },
                              ""value"": false
                            }
                          }
                        }
                      }
                    }
                  },
                  ""operand2"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 1,
                        ""required"": true
                      },
                      ""value"": true
                    }
                  }
                },
                ""connective"": {
                  ""type"": ""&&"",
                  ""to"": {
                    ""condition"": {
                      ""type"": ""equalTo"",
                      ""operand1"": {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""tupleEqual"",
                          ""info"": {
                            ""left"": {
                              ""type"": ""function"",
                              ""function"": {
                                ""type"": ""tupleCreate"",
                                ""info"": {
                                  ""elementPointers"": [
                                    {
                                      ""type"": ""variable"",
                                      ""variableId"": ""a""
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
                                  ""typeInfo"": {
                                    ""type"": 30,
                                    ""required"": true,
                                    ""elements"": [
                                      {
                                        ""typeInfo"": {
                                          ""type"": 6,
                                          ""required"": true,
                                          ""entryTypeInfo"": {
                                            ""type"": 2,
                                            ""required"": true
                                          }
                                        }
                                      },
                                      {
                                        ""typeInfo"": {
                                          ""type"": 2,
                                          ""required"": true
                                        }
                                      }
                                    ]
                                  }
                                }
                              },
                              ""source"": {
                                ""line"": 1,
                                ""column"": 54
                              }
                            },
                            ""right"": {
                              ""type"": ""function"",
                              ""function"": {
                                ""type"": ""tupleCreate"",
                                ""info"": {
                                  ""elementPointers"": [
                                    {
                                      ""type"": ""variable"",
                                      ""variableId"": ""a""
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
                                  ""typeInfo"": {
                                    ""type"": 30,
                                    ""required"": true,
                                    ""elements"": [
                                      {
                                        ""typeInfo"": {
                                          ""type"": 6,
                                          ""required"": true,
                                          ""entryTypeInfo"": {
                                            ""type"": 2,
                                            ""required"": true
                                          }
                                        }
                                      },
                                      {
                                        ""typeInfo"": {
                                          ""type"": 2,
                                          ""required"": true
                                        }
                                      }
                                    ]
                                  }
                                }
                              },
                              ""source"": {
                                ""line"": 1,
                                ""column"": 64
                              }
                            },
                            ""typeInfo"": {
                              ""type"": 30,
                              ""required"": true,
                              ""elements"": [
                                {
                                  ""typeInfo"": {
                                    ""type"": 6,
                                    ""required"": true,
                                    ""entryTypeInfo"": {
                                      ""type"": 2,
                                      ""required"": true
                                    }
                                  }
                                },
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                }
                              ]
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 61
                        }
                      },
                      ""operand2"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 1,
                            ""required"": true
                          },
                          ""value"": true
                        }
                      }
                    }
                  }
                }
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 27
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""stable set identity"",
    ""source"": ""var a = [1]; var values = [(a, 1), (a, 1)].ToSet(); a.Add(2); return values.Contains((a, 1)) && values.Count == 1;"",
    ""returnType"": ""bool"",
    ""expected"": true,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
            ""column"": 1
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""values"",
            ""typeInfo"": {
              ""type"": 9,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 30,
                ""required"": true,
                ""elements"": [
                  {
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  },
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                ]
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
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 6,
                              ""required"": true,
                              ""entryTypeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              }
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    },
                    ""entries"": [
                      {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""tupleCreate"",
                          ""info"": {
                            ""elementPointers"": [
                              {
                                ""type"": ""variable"",
                                ""variableId"": ""a""
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
                            ""typeInfo"": {
                              ""type"": 30,
                              ""required"": true,
                              ""elements"": [
                                {
                                  ""typeInfo"": {
                                    ""type"": 6,
                                    ""required"": true,
                                    ""entryTypeInfo"": {
                                      ""type"": 2,
                                      ""required"": true
                                    }
                                  }
                                },
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                }
                              ]
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 28
                        }
                      },
                      {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""tupleCreate"",
                          ""info"": {
                            ""elementPointers"": [
                              {
                                ""type"": ""variable"",
                                ""variableId"": ""a""
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
                            ""typeInfo"": {
                              ""type"": 30,
                              ""required"": true,
                              ""elements"": [
                                {
                                  ""typeInfo"": {
                                    ""type"": 6,
                                    ""required"": true,
                                    ""entryTypeInfo"": {
                                      ""type"": 2,
                                      ""required"": true
                                    }
                                  }
                                },
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                }
                              ]
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 36
                        }
                      }
                    ]
                  },
                  ""op"": ""ToSet""
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 49
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 14
          }
        },
        {
          ""type"": ""collectionCall"",
          ""target"": {
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""a""
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
                ""value"": 2
              }
            }
          ],
          ""source"": {
            ""line"": 1,
            ""column"": 53
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
                      ""type"": ""contains"",
                      ""info"": {
                        ""collectionPointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""values""
                        },
                        ""valuePointer"": {
                          ""type"": ""function"",
                          ""function"": {
                            ""type"": ""tupleCreate"",
                            ""info"": {
                              ""elementPointers"": [
                                {
                                  ""type"": ""variable"",
                                  ""variableId"": ""a""
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
                              ""typeInfo"": {
                                ""type"": 30,
                                ""required"": true,
                                ""elements"": [
                                  {
                                    ""typeInfo"": {
                                      ""type"": 6,
                                      ""required"": true,
                                      ""entryTypeInfo"": {
                                        ""type"": 2,
                                        ""required"": true
                                      }
                                    }
                                  },
                                  {
                                    ""typeInfo"": {
                                      ""type"": 2,
                                      ""required"": true
                                    }
                                  }
                                ]
                              }
                            }
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 86
                          }
                        }
                      }
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 85
                    }
                  },
                  ""operand2"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 1,
                        ""required"": true
                      },
                      ""value"": true
                    }
                  }
                },
                ""connective"": {
                  ""type"": ""&&"",
                  ""to"": {
                    ""condition"": {
                      ""type"": ""equalTo"",
                      ""operand1"": {
                        ""type"": ""function"",
                        ""function"": {
                          ""type"": ""count"",
                          ""info"": {
                            ""collectionPointer"": {
                              ""type"": ""variable"",
                              ""variableId"": ""values""
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 1,
                          ""column"": 103
                        }
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
          },
          ""source"": {
            ""line"": 1,
            ""column"": 63
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""null tuple"",
    ""source"": ""(int, int)? pair = null; return pair == null;"",
    ""returnType"": ""bool"",
    ""expected"": true,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
            ""id"": ""pair"",
            ""typeInfo"": {
              ""type"": 30,
              ""required"": false,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              ]
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 30,
                  ""required"": false,
                  ""elements"": [
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    },
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  ]
                },
                ""value"": null
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
              ""type"": ""tupleEqual"",
              ""info"": {
                ""left"": {
                  ""type"": ""variable"",
                  ""variableId"": ""pair""
                },
                ""right"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 30,
                      ""required"": false,
                      ""elements"": [
                        {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        },
                        {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        }
                      ]
                    },
                    ""value"": null
                  }
                },
                ""typeInfo"": {
                  ""type"": 30,
                  ""required"": false,
                  ""elements"": [
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    },
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  ]
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 38
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 26
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""eight elements"",
    ""source"": ""return (1, 2, 3, 4, 5, 6, 7, 8).Item8;"",
    ""returnType"": ""int"",
    ""expected"": 8,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
              ""type"": ""tupleElement"",
              ""info"": {
                ""tuplePointer"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleCreate"",
                    ""info"": {
                      ""elementPointers"": [
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
                        },
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 6
                          }
                        },
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 7
                          }
                        },
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
                      ],
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 8
                  }
                },
                ""index"": 7
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 32
            }
          },
          ""source"": {
            ""line"": 1,
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
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""nine elements"",
    ""source"": ""return (1, 2, 3, 4, 5, 6, 7, 8, 9).Item9;"",
    ""returnType"": ""int"",
    ""expected"": 9,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
              ""type"": ""tupleElement"",
              ""info"": {
                ""tuplePointer"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleCreate"",
                    ""info"": {
                      ""elementPointers"": [
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
                        },
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 6
                          }
                        },
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 7
                          }
                        },
                        {
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            },
                            ""value"": 8
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
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 8
                  }
                },
                ""index"": 8
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 35
            }
          },
          ""source"": {
            ""line"": 1,
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
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""labels"",
    ""source"": ""(int Row, int Col) value = (3, 4); return value.Row + value.Item2;"",
    ""returnType"": ""int"",
    ""expected"": 7,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""name"": ""Row""
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""name"": ""Col""
                }
              ]
            },
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""tupleCreate"",
                ""info"": {
                  ""elementPointers"": [
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
                    }
                  ],
                  ""typeInfo"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""name"": ""Row""
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""name"": ""Col""
                      }
                    ]
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 28
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
            ""type"": ""operation"",
            ""operation"": {
              ""type"": ""arithmetic"",
              ""arithmetic"": {
                ""type"": ""+"",
                ""pointers"": [
                  {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""tupleElement"",
                      ""info"": {
                        ""tuplePointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""value""
                        },
                        ""index"": 0
                      }
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 48
                    }
                  },
                  {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""tupleElement"",
                      ""info"": {
                        ""tuplePointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""value""
                        },
                        ""index"": 1
                      }
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 60
                    }
                  }
                ],
                ""numeric"": ""int""
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 36
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""nested equality"",
    ""source"": ""return (1, (2, \""x\"")) == (1, (2, \""x\""));"",
    ""returnType"": ""bool"",
    ""expected"": true,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
              ""type"": ""tupleEqual"",
              ""info"": {
                ""left"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleCreate"",
                    ""info"": {
                      ""elementPointers"": [
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
                          ""type"": ""function"",
                          ""function"": {
                            ""type"": ""tupleCreate"",
                            ""info"": {
                              ""elementPointers"": [
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
                                      ""type"": 3,
                                      ""required"": true
                                    },
                                    ""value"": ""x""
                                  }
                                }
                              ],
                              ""typeInfo"": {
                                ""type"": 30,
                                ""required"": true,
                                ""elements"": [
                                  {
                                    ""typeInfo"": {
                                      ""type"": 2,
                                      ""required"": true
                                    }
                                  },
                                  {
                                    ""typeInfo"": {
                                      ""type"": 3,
                                      ""required"": true
                                    }
                                  }
                                ]
                              }
                            }
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 12
                          }
                        }
                      ],
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 30,
                              ""required"": true,
                              ""elements"": [
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                },
                                {
                                  ""typeInfo"": {
                                    ""type"": 3,
                                    ""required"": true
                                  }
                                }
                              ]
                            }
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 8
                  }
                },
                ""right"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleCreate"",
                    ""info"": {
                      ""elementPointers"": [
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
                          ""type"": ""function"",
                          ""function"": {
                            ""type"": ""tupleCreate"",
                            ""info"": {
                              ""elementPointers"": [
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
                                      ""type"": 3,
                                      ""required"": true
                                    },
                                    ""value"": ""x""
                                  }
                                }
                              ],
                              ""typeInfo"": {
                                ""type"": 30,
                                ""required"": true,
                                ""elements"": [
                                  {
                                    ""typeInfo"": {
                                      ""type"": 2,
                                      ""required"": true
                                    }
                                  },
                                  {
                                    ""typeInfo"": {
                                      ""type"": 3,
                                      ""required"": true
                                    }
                                  }
                                ]
                              }
                            }
                          },
                          ""source"": {
                            ""line"": 1,
                            ""column"": 29
                          }
                        }
                      ],
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 30,
                              ""required"": true,
                              ""elements"": [
                                {
                                  ""typeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                },
                                {
                                  ""typeInfo"": {
                                    ""type"": 3,
                                    ""required"": true
                                  }
                                }
                              ]
                            }
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 25
                  }
                },
                ""typeInfo"": {
                  ""type"": 30,
                  ""required"": true,
                  ""elements"": [
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    },
                    {
                      ""typeInfo"": {
                        ""type"": 30,
                        ""required"": true,
                        ""elements"": [
                          {
                            ""typeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 3,
                              ""required"": true
                            }
                          }
                        ]
                      }
                    }
                  ]
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 22
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 1
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""snapshot"",
    ""source"": ""var a = (3, 4); var b = a; a = (5, 6); return b.Item1 + a.Item2;"",
    ""returnType"": ""int"",
    ""expected"": 9,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              ]
            },
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""tupleCreate"",
                ""info"": {
                  ""elementPointers"": [
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
                    }
                  ],
                  ""typeInfo"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      }
                    ]
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 9
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
            ""id"": ""b"",
            ""typeInfo"": {
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              ]
            },
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""a""
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 17
          }
        },
        {
          ""type"": ""assign"",
          ""target"": {
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""a""
            },
            ""typeInfo"": {
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              ]
            },
            ""writability"": ""local""
          },
          ""operator"": ""="",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""tupleCreate"",
              ""info"": {
                ""elementPointers"": [
                  {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""value"": 5
                    }
                  },
                  {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""value"": 6
                    }
                  }
                ],
                ""typeInfo"": {
                  ""type"": 30,
                  ""required"": true,
                  ""elements"": [
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    },
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  ]
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 32
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 28
          }
        },
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
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""tupleElement"",
                      ""info"": {
                        ""tuplePointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""b""
                        },
                        ""index"": 0
                      }
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 48
                    }
                  },
                  {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""tupleElement"",
                      ""info"": {
                        ""tuplePointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""a""
                        },
                        ""index"": 1
                      }
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 58
                    }
                  }
                ],
                ""numeric"": ""int""
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 40
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""float conversion"",
    ""source"": ""(float, int) a = (16777217, 1); return a.Item1 == 16777216.0;"",
    ""returnType"": ""bool"",
    ""expected"": true,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 4,
                    ""required"": true
                  }
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              ]
            },
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""tupleCreate"",
                ""info"": {
                  ""elementPointers"": [
                    {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""value"": 16777217
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
                  ""typeInfo"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 4,
                          ""required"": true
                        }
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      }
                    ]
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 18
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
            ""type"": ""operation"",
            ""operation"": {
              ""type"": ""boolean"",
              ""expression"": {
                ""condition"": {
                  ""type"": ""equalTo"",
                  ""operand1"": {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""tupleElement"",
                      ""info"": {
                        ""tuplePointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""a""
                        },
                        ""index"": 0
                      }
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 41
                    }
                  },
                  ""operand2"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 4,
                        ""required"": true
                      },
                      ""value"": 16777216
                    }
                  }
                }
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 33
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  },
  {
    ""name"": ""decimal equality"",
    ""source"": ""(decimal, int) a = (1.00, 1); (decimal, int) b = (1.0, 1); return a == b;"",
    ""returnType"": ""bool"",
    ""expected"": true,
    ""getter"": {
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
            ""classId"": ""class:inventory""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class:inventory""
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
            ""classId"": ""synthetic:root""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""synthetic:root""
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
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  }
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              ]
            },
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""tupleCreate"",
                ""info"": {
                  ""elementPointers"": [
                    {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 20,
                          ""required"": true
                        },
                        ""value"": ""1.00""
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
                  ""typeInfo"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 20,
                          ""required"": true
                        }
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      }
                    ]
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 20
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
            ""id"": ""b"",
            ""typeInfo"": {
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 20,
                    ""required"": true
                  }
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              ]
            },
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""tupleCreate"",
                ""info"": {
                  ""elementPointers"": [
                    {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 20,
                          ""required"": true
                        },
                        ""value"": ""1.0""
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
                  ""typeInfo"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 20,
                          ""required"": true
                        }
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      }
                    ]
                  }
                }
              },
              ""source"": {
                ""line"": 1,
                ""column"": 50
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 31
          }
        },
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""tupleEqual"",
              ""info"": {
                ""left"": {
                  ""type"": ""variable"",
                  ""variableId"": ""a""
                },
                ""right"": {
                  ""type"": ""variable"",
                  ""variableId"": ""b""
                },
                ""typeInfo"": {
                  ""type"": 30,
                  ""required"": true,
                  ""elements"": [
                    {
                      ""typeInfo"": {
                        ""type"": 20,
                        ""required"": true
                      }
                    },
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  ]
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 69
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 60
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 1,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""class:inventory"",
          ""synthetic:root""
        ],
        ""typeNames"": []
      }
    }
  }
]
";
    }
}
