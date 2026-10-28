// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
namespace NeoCompose.Tests
{
    internal static class NeoScriptTupleGenericFixture
    {
        internal const string Json = @"{
  ""source"": ""class Probe { public static bool Equal<T>(T left, T right) => (left, 0) == (right, 0); public static bool References<T>(Reference<T> left, Reference<T> right) => (left, 0) == (right, 0); public static bool Membership<T>(Reference<T> left, Reference<T> right) => [(left, 0)].ToSet().Contains((right, 0)); }"",
  ""getters"": [
    {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.Equal"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": ""Unknown"",
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": ""Unknown"",
                ""required"": true
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
        },
        {
          ""id"": ""__arg_0__"",
          ""typeInfo"": {
            ""type"": 21,
            ""required"": true,
            ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Equal"",
            ""index"": 0
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Equal"",
                ""index"": 0
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__arg_1__"",
          ""typeInfo"": {
            ""type"": 21,
            ""required"": true,
            ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Equal"",
            ""index"": 0
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Equal"",
                ""index"": 0
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
                          ""type"": ""variable"",
                          ""variableId"": ""__arg_0__""
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
                              ""type"": 21,
                              ""required"": true,
                              ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Equal"",
                              ""index"": 0
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
                    ""column"": 63
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
                          ""variableId"": ""__arg_1__""
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
                              ""type"": 21,
                              ""required"": true,
                              ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Equal"",
                              ""index"": 0
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
                    ""column"": 76
                  }
                },
                ""typeInfo"": {
                  ""type"": 30,
                  ""required"": true,
                  ""elements"": [
                    {
                      ""typeInfo"": {
                        ""type"": 21,
                        ""required"": true,
                        ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Equal"",
                        ""index"": 0
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
              ""column"": 73
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
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe""
        ],
        ""typeNames"": []
      }
    },
    {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.References"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": ""Unknown"",
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": ""Unknown"",
                ""required"": true
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
        },
        {
          ""id"": ""__arg_0__"",
          ""typeInfo"": {
            ""type"": 29,
            ""required"": true,
            ""targetTypeInfo"": {
              ""type"": 21,
              ""required"": true,
              ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:References"",
              ""index"": 0
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 29,
                ""required"": true,
                ""targetTypeInfo"": {
                  ""type"": 21,
                  ""required"": true,
                  ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:References"",
                  ""index"": 0
                }
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__arg_1__"",
          ""typeInfo"": {
            ""type"": 29,
            ""required"": true,
            ""targetTypeInfo"": {
              ""type"": 21,
              ""required"": true,
              ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:References"",
              ""index"": 0
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 29,
                ""required"": true,
                ""targetTypeInfo"": {
                  ""type"": 21,
                  ""required"": true,
                  ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:References"",
                  ""index"": 0
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
                          ""variableId"": ""__arg_0__""
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
                              ""type"": 29,
                              ""required"": true,
                              ""targetTypeInfo"": {
                                ""type"": 21,
                                ""required"": true,
                                ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:References"",
                                ""index"": 0
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
                    ""column"": 163
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
                          ""variableId"": ""__arg_1__""
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
                              ""type"": 29,
                              ""required"": true,
                              ""targetTypeInfo"": {
                                ""type"": 21,
                                ""required"": true,
                                ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:References"",
                                ""index"": 0
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
                    ""column"": 176
                  }
                },
                ""typeInfo"": {
                  ""type"": 30,
                  ""required"": true,
                  ""elements"": [
                    {
                      ""typeInfo"": {
                        ""type"": 29,
                        ""required"": true,
                        ""targetTypeInfo"": {
                          ""type"": 21,
                          ""required"": true,
                          ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:References"",
                          ""index"": 0
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
              ""column"": 173
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 163
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
          ""pending:class:Probe.neo:Probe""
        ],
        ""typeNames"": []
      }
    },
    {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.Membership"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": ""Unknown"",
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": ""Unknown"",
                ""required"": true
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
        },
        {
          ""id"": ""__arg_0__"",
          ""typeInfo"": {
            ""type"": 29,
            ""required"": true,
            ""targetTypeInfo"": {
              ""type"": 21,
              ""required"": true,
              ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Membership"",
              ""index"": 0
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 29,
                ""required"": true,
                ""targetTypeInfo"": {
                  ""type"": 21,
                  ""required"": true,
                  ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Membership"",
                  ""index"": 0
                }
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__arg_1__"",
          ""typeInfo"": {
            ""type"": 29,
            ""required"": true,
            ""targetTypeInfo"": {
              ""type"": 21,
              ""required"": true,
              ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Membership"",
              ""index"": 0
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 29,
                ""required"": true,
                ""targetTypeInfo"": {
                  ""type"": 21,
                  ""required"": true,
                  ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Membership"",
                  ""index"": 0
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
                        ""type"": ""sequenceConversion"",
                        ""operation"": ""queryEnumerable"",
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
                                    ""type"": 29,
                                    ""required"": true,
                                    ""targetTypeInfo"": {
                                      ""type"": 21,
                                      ""required"": true,
                                      ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Membership"",
                                      ""index"": 0
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
                                      ""variableId"": ""__arg_0__""
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
                                          ""type"": 29,
                                          ""required"": true,
                                          ""targetTypeInfo"": {
                                            ""type"": 21,
                                            ""required"": true,
                                            ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Membership"",
                                            ""index"": 0
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
                                ""column"": 264
                              }
                            }
                          ]
                        },
                        ""elementType"": {
                          ""type"": 30,
                          ""required"": true,
                          ""elements"": [
                            {
                              ""typeInfo"": {
                                ""type"": 29,
                                ""required"": true,
                                ""targetTypeInfo"": {
                                  ""type"": 21,
                                  ""required"": true,
                                  ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Membership"",
                                  ""index"": 0
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
                      ""op"": ""ToSet""
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 280
                  }
                },
                ""valuePointer"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""tupleCreate"",
                    ""info"": {
                      ""elementPointers"": [
                        {
                          ""type"": ""variable"",
                          ""variableId"": ""__arg_1__""
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
                              ""type"": 29,
                              ""required"": true,
                              ""targetTypeInfo"": {
                                ""type"": 21,
                                ""required"": true,
                                ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Membership"",
                                ""index"": 0
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
                    ""column"": 292
                  }
                }
              }
            },
            ""source"": {
              ""line"": 1,
              ""column"": 291
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 263
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
          ""pending:class:Probe.neo:Probe""
        ],
        ""typeNames"": []
      }
    }
  ],
  ""capture"": {
    ""source"": ""class Probe { public static int Capture<T>(T values) { NeoDelegate<int> add = () => { if (values is List<int> list) { list.Add(2); return list.Count; } return 0; }; int ignored = add(); if(values is List<int> list) return list.Count; return 0; } }"",
    ""getter"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.Capture"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": ""Unknown"",
            ""required"": true
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": ""Unknown"",
                ""required"": true
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
        },
        {
          ""id"": ""__arg_0__"",
          ""typeInfo"": {
            ""type"": 21,
            ""required"": true,
            ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Capture"",
            ""index"": 0
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Capture"",
                ""index"": 0
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
            ""id"": ""add"",
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
                ""compilerRevision"": 19,
                ""source"": {
                  ""uri"": ""Probe.neo"",
                  ""coordinateSpace"": ""file"",
                  ""name"": ""Probe.Capture <delegate>""
                },
                ""parameters"": [
                  {
                    ""id"": ""__this__"",
                    ""typeInfo"": {
                      ""type"": ""Unknown"",
                      ""required"": true
                    },
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": ""Unknown"",
                          ""required"": true
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
                  },
                  {
                    ""id"": ""__capture_0_0__"",
                    ""typeInfo"": {
                      ""type"": 21,
                      ""required"": true,
                      ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Capture"",
                      ""index"": 0
                    },
                    ""pointer"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 21,
                          ""required"": true,
                          ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Capture"",
                          ""index"": 0
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
                            ""type"": ""equalTo"",
                            ""operand1"": {
                              ""type"": ""isCheck"",
                              ""pointer"": {
                                ""type"": ""variable"",
                                ""variableId"": ""__capture_0_0__""
                              },
                              ""checkType"": {
                                ""type"": 6,
                                ""required"": true,
                                ""entryTypeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                }
                              },
                              ""bindingId"": ""__pattern:0""
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
                        },
                        ""instructions"": [
                          {
                            ""type"": ""collectionCall"",
                            ""target"": {
                              ""pointer"": {
                                ""type"": ""variable"",
                                ""variableId"": ""__pattern:0""
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
                              ""column"": 119
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
                                    ""variableId"": ""__pattern:0""
                                  }
                                }
                              },
                              ""source"": {
                                ""line"": 1,
                                ""column"": 143
                              }
                            },
                            ""source"": {
                              ""line"": 1,
                              ""column"": 132
                            }
                          }
                        ]
                      }
                    ],
                    ""else"": null,
                    ""source"": {
                      ""line"": 1,
                      ""column"": 87
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
                      ""line"": 1,
                      ""column"": 153
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
                  ""variableId"": ""__arg_0__""
                }
              ],
              ""code"": ""() => { if (values is List<int> list) { list.Add(2); return list.Count; } return 0; }""
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 56
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""ignored"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""pointer"": {
              ""type"": ""callDelegate"",
              ""delegate"": {
                ""type"": ""variable"",
                ""variableId"": ""add""
              },
              ""args"": [],
              ""callSiteId"": ""1:183:0"",
              ""source"": {
                ""line"": 1,
                ""column"": 183
              }
            }
          },
          ""source"": {
            ""line"": 1,
            ""column"": 166
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
                    ""type"": ""isCheck"",
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""__arg_0__""
                    },
                    ""checkType"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    },
                    ""bindingId"": ""__pattern:1""
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
              },
              ""instructions"": [
                {
                  ""type"": ""return"",
                  ""pointer"": {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""count"",
                      ""info"": {
                        ""collectionPointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""__pattern:1""
                        }
                      }
                    },
                    ""source"": {
                      ""line"": 1,
                      ""column"": 227
                    }
                  },
                  ""source"": {
                    ""line"": 1,
                    ""column"": 216
                  }
                }
              ]
            }
          ],
          ""else"": null,
          ""source"": {
            ""line"": 1,
            ""column"": 187
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
            ""line"": 1,
            ""column"": 235
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe""
        ],
        ""typeNames"": []
      }
    }
  }
}";
    }
}
