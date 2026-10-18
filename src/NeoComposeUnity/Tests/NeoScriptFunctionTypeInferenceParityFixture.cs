// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
// VENDORED — DO NOT HAND-EDIT.
// Source: neo-compose/src/models/neoscript/function-type-inference-parity-fixture.json
namespace NeoCompose.Tests
{
    public static class NeoScriptFunctionTypeInferenceParityFixture
    {
        public const string Json = @"{
  ""source"": ""@id(\""12000000-0000-4000-8000-000000000001\"")\nclass Inference {\n @id(\""12000000-0000-4000-8000-000000000002\"")\n public static T Identity<T>(T value) => value;\n @id(\""12000000-0000-4000-8000-000000000003\"")\n public static R Map<T, R>(T value, NeoDelegate<R, T> project) => project(value);\n @id(\""12000000-0000-4000-8000-000000000004\"")\n public static T Forward<T>(T value) => Inference.Identity(value);\n @id(\""12000000-0000-4000-8000-000000000005\"")\n public int Nested { get { return Inference.Identity(Inference.Forward(7)); } }\n @id(\""12000000-0000-4000-8000-000000000006\"")\n public int Captured { get { int offset = 4; return Inference.Map(3, (value) => Inference.Identity(value + offset)); } }\n @id(\""12000000-0000-4000-8000-000000000007\"")\n public int Typed { get { NeoDelegate<int, int> project = (value) => value * 2; return Inference.Map(5, project); } }\n @id(\""12000000-0000-4000-8000-000000000008\"")\n public int Once { get { return Inference.Map(Inference.Identity(2), (value) => { Debug.Log(value); return value + 1; }); } }\n @id(\""12000000-0000-4000-8000-000000000009\"")\n public static T Pair<T>(T left, T right) => left;\n @id(\""12000000-0000-4000-8000-000000000010\"")\n public static int Mark(int value) { Debug.Log(value); return value; }\n @id(\""12000000-0000-4000-8000-000000000011\"")\n public int Order { get { return Inference.Pair(Inference.Mark(2), Inference.Mark(3)); } }\n @id(\""12000000-0000-4000-8000-000000000012\"")\n public int Combined { get { return Inference.Map(5, (value) => value switch { int n when (n & 1) == 1 => n << 2, _ => 0 }); } }\n}"",
  ""document"": {
    ""metadata"": {
      ""schemaVersion"": 44,
      ""projectId"": ""project-id"",
      ""versionId"": ""p120"",
      ""semver"": {
        ""major"": 0,
        ""minor"": 1,
        ""patch"": 0,
        ""label"": """"
      }
    },
    ""localization"": {
      ""schemaVersion"": 1,
      ""mainLocale"": ""en-US"",
      ""supportedLocales"": [
        {
          ""locale"": ""en-US"",
          ""sourceLocale"": null,
          ""archivedAt"": null
        }
      ],
      ""textIds"": [],
      ""mainLocaleFileName"": ""en-US.json"",
      ""localeFileNames"": {
        ""en-US"": ""en-US.json""
      },
      ""formatting"": {
        ""syntax"": ""smart-format"",
        ""sourceSyntax"": ""icu""
      }
    },
    ""project"": {
      ""createdAt"": 0,
      ""updatedAt"": 0,
      ""id"": ""project-id"",
      ""name"": ""Test Project"",
      ""owner"": {
        ""kind"": ""organization"",
        ""id"": ""test-organization""
      },
      ""author"": {
        ""kind"": ""user"",
        ""id"": ""test-user""
      },
      ""exportSettings"": null,
      ""rootAssetsMemberId"": ""root-assets-member-id"",
      ""rootSaveFileMemberId"": ""root-save-member-id"",
      ""rootSessionMemberId"": ""root-session-member-id"",
      ""rootUserMemberId"": ""4ed69bfa-b85a-5778-8f83-ae7723e75bad"",
      ""defaultPriorityGroupId"": ""default-priority-group"",
      ""defaultTextureTemplateId"": null,
      ""defaultAudioClipTemplateId"": null,
      ""authorUser"": null,
      ""latestVersion"": null,
      ""latestVersionStatus"": null,
      ""listContext"": {
        ""kind"": ""public""
      },
      ""ownerOrganization"": null
    },
    ""members"": {
      ""12000000-0000-4000-8000-000000000002"": {
        ""id"": ""12000000-0000-4000-8000-000000000002"",
        ""name"": ""Identity"",
        ""kind"": 23,
        ""modifier"": 3,
        ""returnTypeInfo"": {
          ""type"": 21,
          ""required"": true,
          ""ownerMemberId"": ""12000000-0000-4000-8000-000000000002"",
          ""index"": 0
        },
        ""argumentTypes"": [
          {
            ""name"": ""value"",
            ""type"": 21,
            ""required"": true,
            ""ownerMemberId"": ""12000000-0000-4000-8000-000000000002"",
            ""index"": 0
          }
        ],
        ""genericParams"": [
          {
            ""name"": ""T""
          }
        ],
        ""code"": ""return value;"",
        ""projectId"": ""project-id"",
        ""createdAt"": 1700000000000,
        ""updatedAt"": 1700000000000,
        ""action"": {
          ""compilerRevision"": 17,
          ""source"": {
            ""name"": ""Inference.Identity"",
            ""uri"": ""Inference.neo"",
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
              ""id"": ""__arg_0__"",
              ""typeInfo"": {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""12000000-0000-4000-8000-000000000002"",
                ""index"": 0
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 21,
                    ""required"": true,
                    ""ownerMemberId"": ""12000000-0000-4000-8000-000000000002"",
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
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              },
              ""source"": {
                ""line"": 4,
                ""column"": 42
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 21,
            ""required"": true,
            ""ownerMemberId"": ""12000000-0000-4000-8000-000000000002"",
            ""index"": 0
          }
        }
      },
      ""12000000-0000-4000-8000-000000000003"": {
        ""id"": ""12000000-0000-4000-8000-000000000003"",
        ""name"": ""Map"",
        ""kind"": 23,
        ""modifier"": 3,
        ""returnTypeInfo"": {
          ""type"": 21,
          ""required"": true,
          ""ownerMemberId"": ""12000000-0000-4000-8000-000000000003"",
          ""index"": 1
        },
        ""argumentTypes"": [
          {
            ""name"": ""value"",
            ""type"": 21,
            ""required"": true,
            ""ownerMemberId"": ""12000000-0000-4000-8000-000000000003"",
            ""index"": 0
          },
          {
            ""name"": ""project"",
            ""type"": 25,
            ""required"": true,
            ""returnTypeInfo"": {
              ""type"": 21,
              ""required"": true,
              ""ownerMemberId"": ""12000000-0000-4000-8000-000000000003"",
              ""index"": 1
            },
            ""argumentTypes"": [
              {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""12000000-0000-4000-8000-000000000003"",
                ""index"": 0
              }
            ]
          }
        ],
        ""genericParams"": [
          {
            ""name"": ""T""
          },
          {
            ""name"": ""R""
          }
        ],
        ""code"": ""return project(value);"",
        ""projectId"": ""project-id"",
        ""createdAt"": 1700000000000,
        ""updatedAt"": 1700000000000,
        ""action"": {
          ""compilerRevision"": 17,
          ""source"": {
            ""name"": ""Inference.Map"",
            ""uri"": ""Inference.neo"",
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
              ""id"": ""__arg_0__"",
              ""typeInfo"": {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""12000000-0000-4000-8000-000000000003"",
                ""index"": 0
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 21,
                    ""required"": true,
                    ""ownerMemberId"": ""12000000-0000-4000-8000-000000000003"",
                    ""index"": 0
                  },
                  ""value"": null
                }
              }
            },
            {
              ""id"": ""__arg_1__"",
              ""typeInfo"": {
                ""type"": 25,
                ""required"": true,
                ""returnTypeInfo"": {
                  ""type"": 21,
                  ""required"": true,
                  ""ownerMemberId"": ""12000000-0000-4000-8000-000000000003"",
                  ""index"": 1
                },
                ""argumentTypes"": [
                  {
                    ""type"": 21,
                    ""required"": true,
                    ""ownerMemberId"": ""12000000-0000-4000-8000-000000000003"",
                    ""index"": 0
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
                      ""type"": 21,
                      ""required"": true,
                      ""ownerMemberId"": ""12000000-0000-4000-8000-000000000003"",
                      ""index"": 1
                    },
                    ""argumentTypes"": [
                      {
                        ""type"": 21,
                        ""required"": true,
                        ""ownerMemberId"": ""12000000-0000-4000-8000-000000000003"",
                        ""index"": 0
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
                ""type"": ""callDelegate"",
                ""delegate"": {
                  ""type"": ""variable"",
                  ""variableId"": ""__arg_1__""
                },
                ""args"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_0__""
                  }
                ],
                ""callSiteId"": ""1:15:0"",
                ""source"": {
                  ""line"": 6,
                  ""column"": 74
                }
              },
              ""source"": {
                ""line"": 6,
                ""column"": 67
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 21,
            ""required"": true,
            ""ownerMemberId"": ""12000000-0000-4000-8000-000000000003"",
            ""index"": 1
          }
        }
      },
      ""12000000-0000-4000-8000-000000000004"": {
        ""id"": ""12000000-0000-4000-8000-000000000004"",
        ""name"": ""Forward"",
        ""kind"": 23,
        ""modifier"": 3,
        ""returnTypeInfo"": {
          ""type"": 21,
          ""required"": true,
          ""ownerMemberId"": ""12000000-0000-4000-8000-000000000004"",
          ""index"": 0
        },
        ""argumentTypes"": [
          {
            ""name"": ""value"",
            ""type"": 21,
            ""required"": true,
            ""ownerMemberId"": ""12000000-0000-4000-8000-000000000004"",
            ""index"": 0
          }
        ],
        ""genericParams"": [
          {
            ""name"": ""T""
          }
        ],
        ""code"": ""return Inference.Identity(value);"",
        ""projectId"": ""project-id"",
        ""createdAt"": 1700000000000,
        ""updatedAt"": 1700000000000,
        ""action"": {
          ""compilerRevision"": 17,
          ""source"": {
            ""name"": ""Inference.Forward"",
            ""uri"": ""Inference.neo"",
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
              ""id"": ""__arg_0__"",
              ""typeInfo"": {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""12000000-0000-4000-8000-000000000004"",
                ""index"": 0
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 21,
                    ""required"": true,
                    ""ownerMemberId"": ""12000000-0000-4000-8000-000000000004"",
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
                ""type"": ""callFunction"",
                ""memberId"": ""12000000-0000-4000-8000-000000000002"",
                ""receiver"": {
                  ""kind"": ""static"",
                  ""memberId"": ""12000000-0000-4000-8000-000000000002""
                },
                ""args"": [
                  {
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_0__""
                  }
                ],
                ""typeArguments"": [
                  {
                    ""type"": 21,
                    ""required"": true,
                    ""ownerMemberId"": ""12000000-0000-4000-8000-000000000004"",
                    ""index"": 0
                  }
                ],
                ""callSiteId"": ""1:26:0"",
                ""source"": {
                  ""line"": 8,
                  ""column"": 59
                }
              },
              ""source"": {
                ""line"": 8,
                ""column"": 41
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 21,
            ""required"": true,
            ""ownerMemberId"": ""12000000-0000-4000-8000-000000000004"",
            ""index"": 0
          }
        }
      },
      ""12000000-0000-4000-8000-000000000005"": {
        ""id"": ""12000000-0000-4000-8000-000000000005"",
        ""name"": ""Nested"",
        ""kind"": 10,
        ""requirement"": 1,
        ""modifier"": 1,
        ""returnTypeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""code"": ""return Inference.Identity(Inference.Forward(7));"",
        ""projectId"": ""project-id"",
        ""createdAt"": 1700000000000,
        ""updatedAt"": 1700000000000,
        ""getter"": {
          ""compilerRevision"": 17,
          ""source"": {
            ""name"": ""Inference.Nested"",
            ""uri"": ""Inference.neo"",
            ""coordinateSpace"": ""file""
          },
          ""parameters"": [
            {
              ""id"": ""__this__"",
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""12000000-0000-4000-8000-000000000001""
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""12000000-0000-4000-8000-000000000001""
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
                ""type"": ""callFunction"",
                ""memberId"": ""12000000-0000-4000-8000-000000000002"",
                ""receiver"": {
                  ""kind"": ""static"",
                  ""memberId"": ""12000000-0000-4000-8000-000000000002""
                },
                ""args"": [
                  {
                    ""type"": ""callFunction"",
                    ""memberId"": ""12000000-0000-4000-8000-000000000004"",
                    ""receiver"": {
                      ""kind"": ""static"",
                      ""memberId"": ""12000000-0000-4000-8000-000000000004""
                    },
                    ""args"": [
                      {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 7
                        }
                      }
                    ],
                    ""typeArguments"": [
                      {
                        ""type"": 2,
                        ""required"": true
                      }
                    ],
                    ""callSiteId"": ""1:44:0"",
                    ""source"": {
                      ""line"": 10,
                      ""column"": 71
                    }
                  }
                ],
                ""typeArguments"": [
                  {
                    ""type"": 2,
                    ""required"": true
                  }
                ],
                ""callSiteId"": ""1:26:1"",
                ""source"": {
                  ""line"": 10,
                  ""column"": 53
                }
              },
              ""source"": {
                ""line"": 10,
                ""column"": 28
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""12000000-0000-4000-8000-000000000006"": {
        ""id"": ""12000000-0000-4000-8000-000000000006"",
        ""name"": ""Captured"",
        ""kind"": 10,
        ""requirement"": 1,
        ""modifier"": 1,
        ""returnTypeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""code"": ""int offset = 4; return Inference.Map(3, (value) => Inference.Identity(value + offset));"",
        ""projectId"": ""project-id"",
        ""createdAt"": 1700000000000,
        ""updatedAt"": 1700000000000,
        ""getter"": {
          ""compilerRevision"": 17,
          ""source"": {
            ""name"": ""Inference.Captured"",
            ""uri"": ""Inference.neo"",
            ""coordinateSpace"": ""file""
          },
          ""parameters"": [
            {
              ""id"": ""__this__"",
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""12000000-0000-4000-8000-000000000001""
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""12000000-0000-4000-8000-000000000001""
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
                ""id"": ""offset"",
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
                    ""value"": 4
                  }
                }
              },
              ""source"": {
                ""line"": 12,
                ""column"": 30
              }
            },
            {
              ""type"": ""return"",
              ""pointer"": {
                ""type"": ""callFunction"",
                ""memberId"": ""12000000-0000-4000-8000-000000000003"",
                ""receiver"": {
                  ""kind"": ""static"",
                  ""memberId"": ""12000000-0000-4000-8000-000000000003""
                },
                ""args"": [
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
                    ""type"": ""delegateClosure"",
                    ""typeInfo"": {
                      ""type"": 25,
                      ""required"": true,
                      ""returnTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""argumentTypes"": [
                        {
                          ""type"": 2,
                          ""required"": true
                        }
                      ]
                    },
                    ""action"": {
                      ""compilerRevision"": 17,
                      ""source"": {
                        ""uri"": ""Inference.neo"",
                        ""coordinateSpace"": ""file"",
                        ""name"": ""Inference.Captured <delegate>""
                      },
                      ""parameters"": [
                        {
                          ""id"": ""__this__"",
                          ""typeInfo"": {
                            ""type"": 7,
                            ""required"": true,
                            ""classId"": ""12000000-0000-4000-8000-000000000001""
                          },
                          ""pointer"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 7,
                                ""required"": true,
                                ""classId"": ""12000000-0000-4000-8000-000000000001""
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
                          ""id"": ""__lambda_0_arg_0__"",
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
                          ""id"": ""__capture_0_0__"",
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
                            ""type"": ""callFunction"",
                            ""memberId"": ""12000000-0000-4000-8000-000000000002"",
                            ""receiver"": {
                              ""kind"": ""static"",
                              ""memberId"": ""12000000-0000-4000-8000-000000000002""
                            },
                            ""args"": [
                              {
                                ""type"": ""operation"",
                                ""operation"": {
                                  ""type"": ""arithmetic"",
                                  ""arithmetic"": {
                                    ""type"": ""+"",
                                    ""pointers"": [
                                      {
                                        ""type"": ""variable"",
                                        ""variableId"": ""__lambda_0_arg_0__""
                                      },
                                      {
                                        ""type"": ""variable"",
                                        ""variableId"": ""__capture_0_0__""
                                      }
                                    ],
                                    ""numeric"": ""int""
                                  }
                                }
                              }
                            ],
                            ""typeArguments"": [
                              {
                                ""type"": 2,
                                ""required"": true
                              }
                            ],
                            ""callSiteId"": ""1:70:0"",
                            ""source"": {
                              ""line"": 12,
                              ""column"": 99
                            }
                          },
                          ""source"": {
                            ""line"": 12,
                            ""column"": 81
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
                        ""variableId"": ""offset""
                      }
                    ],
                    ""code"": ""(value) => Inference.Identity(value + offset)""
                  }
                ],
                ""typeArguments"": [
                  {
                    ""type"": 2,
                    ""required"": true
                  },
                  {
                    ""type"": 2,
                    ""required"": true
                  }
                ],
                ""callSiteId"": ""1:37:1"",
                ""source"": {
                  ""line"": 12,
                  ""column"": 66
                }
              },
              ""source"": {
                ""line"": 12,
                ""column"": 46
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""12000000-0000-4000-8000-000000000007"": {
        ""id"": ""12000000-0000-4000-8000-000000000007"",
        ""name"": ""Typed"",
        ""kind"": 10,
        ""requirement"": 1,
        ""modifier"": 1,
        ""returnTypeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""code"": ""NeoDelegate<int, int> project = (value) => value * 2; return Inference.Map(5, project);"",
        ""projectId"": ""project-id"",
        ""createdAt"": 1700000000000,
        ""updatedAt"": 1700000000000,
        ""getter"": {
          ""compilerRevision"": 17,
          ""source"": {
            ""name"": ""Inference.Typed"",
            ""uri"": ""Inference.neo"",
            ""coordinateSpace"": ""file""
          },
          ""parameters"": [
            {
              ""id"": ""__this__"",
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""12000000-0000-4000-8000-000000000001""
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""12000000-0000-4000-8000-000000000001""
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
                ""id"": ""project"",
                ""typeInfo"": {
                  ""type"": 25,
                  ""required"": true,
                  ""returnTypeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""argumentTypes"": [
                    {
                      ""type"": 2,
                      ""required"": true
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
                          ""type"": 2,
                          ""required"": true
                        }
                      ]
                    },
                    ""value"": {
                      ""code"": ""(value) => value * 2"",
                      ""action"": {
                        ""compilerRevision"": 17,
                        ""source"": {
                          ""uri"": ""Inference.neo"",
                          ""coordinateSpace"": ""file"",
                          ""name"": ""Inference.Typed <delegate>""
                        },
                        ""parameters"": [
                          {
                            ""id"": ""__this__"",
                            ""typeInfo"": {
                              ""type"": 7,
                              ""required"": true,
                              ""classId"": ""12000000-0000-4000-8000-000000000001""
                            },
                            ""pointer"": {
                              ""type"": ""value"",
                              ""value"": {
                                ""typeInfo"": {
                                  ""type"": 7,
                                  ""required"": true,
                                  ""classId"": ""12000000-0000-4000-8000-000000000001""
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
                            ""id"": ""__lambda_0_arg_0__"",
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
                                      ""variableId"": ""__lambda_0_arg_0__""
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
                              ""line"": 14,
                              ""column"": 70
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
                ""line"": 14,
                ""column"": 27
              }
            },
            {
              ""type"": ""return"",
              ""pointer"": {
                ""type"": ""callFunction"",
                ""memberId"": ""12000000-0000-4000-8000-000000000003"",
                ""receiver"": {
                  ""kind"": ""static"",
                  ""memberId"": ""12000000-0000-4000-8000-000000000003""
                },
                ""args"": [
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
                    ""type"": ""variable"",
                    ""variableId"": ""project""
                  }
                ],
                ""typeArguments"": [
                  {
                    ""type"": 2,
                    ""required"": true
                  },
                  {
                    ""type"": 2,
                    ""required"": true
                  }
                ],
                ""callSiteId"": ""1:75:0"",
                ""source"": {
                  ""line"": 14,
                  ""column"": 101
                }
              },
              ""source"": {
                ""line"": 14,
                ""column"": 81
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""12000000-0000-4000-8000-000000000008"": {
        ""id"": ""12000000-0000-4000-8000-000000000008"",
        ""name"": ""Once"",
        ""kind"": 10,
        ""requirement"": 1,
        ""modifier"": 1,
        ""returnTypeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""code"": ""return Inference.Map(Inference.Identity(2), (value) => { Debug.Log(value); return value + 1; });"",
        ""projectId"": ""project-id"",
        ""createdAt"": 1700000000000,
        ""updatedAt"": 1700000000000,
        ""getter"": {
          ""compilerRevision"": 17,
          ""source"": {
            ""name"": ""Inference.Once"",
            ""uri"": ""Inference.neo"",
            ""coordinateSpace"": ""file""
          },
          ""parameters"": [
            {
              ""id"": ""__this__"",
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""12000000-0000-4000-8000-000000000001""
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""12000000-0000-4000-8000-000000000001""
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
                ""type"": ""callFunction"",
                ""memberId"": ""12000000-0000-4000-8000-000000000003"",
                ""receiver"": {
                  ""kind"": ""static"",
                  ""memberId"": ""12000000-0000-4000-8000-000000000003""
                },
                ""args"": [
                  {
                    ""type"": ""callFunction"",
                    ""memberId"": ""12000000-0000-4000-8000-000000000002"",
                    ""receiver"": {
                      ""kind"": ""static"",
                      ""memberId"": ""12000000-0000-4000-8000-000000000002""
                    },
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
                    ""typeArguments"": [
                      {
                        ""type"": 2,
                        ""required"": true
                      }
                    ],
                    ""callSiteId"": ""1:40:0"",
                    ""source"": {
                      ""line"": 16,
                      ""column"": 65
                    }
                  },
                  {
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
                            ""type"": 2,
                            ""required"": true
                          }
                        ]
                      },
                      ""value"": {
                        ""code"": ""(value) => { Debug.Log(value); return value + 1; }"",
                        ""action"": {
                          ""compilerRevision"": 17,
                          ""source"": {
                            ""uri"": ""Inference.neo"",
                            ""coordinateSpace"": ""file"",
                            ""name"": ""Inference.Once <delegate>""
                          },
                          ""parameters"": [
                            {
                              ""id"": ""__this__"",
                              ""typeInfo"": {
                                ""type"": 7,
                                ""required"": true,
                                ""classId"": ""12000000-0000-4000-8000-000000000001""
                              },
                              ""pointer"": {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 7,
                                    ""required"": true,
                                    ""classId"": ""12000000-0000-4000-8000-000000000001""
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
                              ""id"": ""__lambda_0_arg_0__"",
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
                              ""type"": ""debug"",
                              ""severity"": ""log"",
                              ""message"": {
                                ""type"": ""variable"",
                                ""variableId"": ""__lambda_0_arg_0__""
                              },
                              ""messageType"": {
                                ""type"": 2,
                                ""required"": true
                              },
                              ""source"": {
                                ""line"": 16,
                                ""column"": 83
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
                                        ""variableId"": ""__lambda_0_arg_0__""
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
                                ""line"": 16,
                                ""column"": 101
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
                ],
                ""typeArguments"": [
                  {
                    ""type"": 2,
                    ""required"": true
                  },
                  {
                    ""type"": 2,
                    ""required"": true
                  }
                ],
                ""callSiteId"": ""1:21:1"",
                ""source"": {
                  ""line"": 16,
                  ""column"": 46
                }
              },
              ""source"": {
                ""line"": 16,
                ""column"": 26
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""12000000-0000-4000-8000-000000000009"": {
        ""id"": ""12000000-0000-4000-8000-000000000009"",
        ""name"": ""Pair"",
        ""kind"": 23,
        ""modifier"": 3,
        ""returnTypeInfo"": {
          ""type"": 21,
          ""required"": true,
          ""ownerMemberId"": ""12000000-0000-4000-8000-000000000009"",
          ""index"": 0
        },
        ""argumentTypes"": [
          {
            ""name"": ""left"",
            ""type"": 21,
            ""required"": true,
            ""ownerMemberId"": ""12000000-0000-4000-8000-000000000009"",
            ""index"": 0
          },
          {
            ""name"": ""right"",
            ""type"": 21,
            ""required"": true,
            ""ownerMemberId"": ""12000000-0000-4000-8000-000000000009"",
            ""index"": 0
          }
        ],
        ""genericParams"": [
          {
            ""name"": ""T""
          }
        ],
        ""code"": ""return left;"",
        ""projectId"": ""project-id"",
        ""createdAt"": 1700000000000,
        ""updatedAt"": 1700000000000,
        ""action"": {
          ""compilerRevision"": 17,
          ""source"": {
            ""name"": ""Inference.Pair"",
            ""uri"": ""Inference.neo"",
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
              ""id"": ""__arg_0__"",
              ""typeInfo"": {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""12000000-0000-4000-8000-000000000009"",
                ""index"": 0
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 21,
                    ""required"": true,
                    ""ownerMemberId"": ""12000000-0000-4000-8000-000000000009"",
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
                ""ownerMemberId"": ""12000000-0000-4000-8000-000000000009"",
                ""index"": 0
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 21,
                    ""required"": true,
                    ""ownerMemberId"": ""12000000-0000-4000-8000-000000000009"",
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
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              },
              ""source"": {
                ""line"": 18,
                ""column"": 46
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 21,
            ""required"": true,
            ""ownerMemberId"": ""12000000-0000-4000-8000-000000000009"",
            ""index"": 0
          }
        }
      },
      ""12000000-0000-4000-8000-000000000010"": {
        ""id"": ""12000000-0000-4000-8000-000000000010"",
        ""name"": ""Mark"",
        ""kind"": 23,
        ""modifier"": 3,
        ""returnTypeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""argumentTypes"": [
          {
            ""name"": ""value"",
            ""type"": 2,
            ""required"": true
          }
        ],
        ""code"": ""Debug.Log(value); return value;"",
        ""projectId"": ""project-id"",
        ""createdAt"": 1700000000000,
        ""updatedAt"": 1700000000000,
        ""action"": {
          ""compilerRevision"": 17,
          ""source"": {
            ""name"": ""Inference.Mark"",
            ""uri"": ""Inference.neo"",
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
              ""id"": ""__arg_0__"",
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
              ""type"": ""debug"",
              ""severity"": ""log"",
              ""message"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              },
              ""messageType"": {
                ""type"": 2,
                ""required"": true
              },
              ""source"": {
                ""line"": 20,
                ""column"": 38
              }
            },
            {
              ""type"": ""return"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              },
              ""source"": {
                ""line"": 20,
                ""column"": 56
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""12000000-0000-4000-8000-000000000011"": {
        ""id"": ""12000000-0000-4000-8000-000000000011"",
        ""name"": ""Order"",
        ""kind"": 10,
        ""requirement"": 1,
        ""modifier"": 1,
        ""returnTypeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""code"": ""return Inference.Pair(Inference.Mark(2), Inference.Mark(3));"",
        ""projectId"": ""project-id"",
        ""createdAt"": 1700000000000,
        ""updatedAt"": 1700000000000,
        ""getter"": {
          ""compilerRevision"": 17,
          ""source"": {
            ""name"": ""Inference.Order"",
            ""uri"": ""Inference.neo"",
            ""coordinateSpace"": ""file""
          },
          ""parameters"": [
            {
              ""id"": ""__this__"",
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""12000000-0000-4000-8000-000000000001""
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""12000000-0000-4000-8000-000000000001""
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
                ""type"": ""callFunction"",
                ""memberId"": ""12000000-0000-4000-8000-000000000009"",
                ""receiver"": {
                  ""kind"": ""static"",
                  ""memberId"": ""12000000-0000-4000-8000-000000000009""
                },
                ""args"": [
                  {
                    ""type"": ""callFunction"",
                    ""memberId"": ""12000000-0000-4000-8000-000000000010"",
                    ""receiver"": {
                      ""kind"": ""static"",
                      ""memberId"": ""12000000-0000-4000-8000-000000000010""
                    },
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
                    ""callSiteId"": ""1:37:0"",
                    ""source"": {
                      ""line"": 22,
                      ""column"": 63
                    }
                  },
                  {
                    ""type"": ""callFunction"",
                    ""memberId"": ""12000000-0000-4000-8000-000000000010"",
                    ""receiver"": {
                      ""kind"": ""static"",
                      ""memberId"": ""12000000-0000-4000-8000-000000000010""
                    },
                    ""args"": [
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
                    ""callSiteId"": ""1:56:1"",
                    ""source"": {
                      ""line"": 22,
                      ""column"": 82
                    }
                  }
                ],
                ""typeArguments"": [
                  {
                    ""type"": 2,
                    ""required"": true
                  }
                ],
                ""callSiteId"": ""1:22:2"",
                ""source"": {
                  ""line"": 22,
                  ""column"": 48
                }
              },
              ""source"": {
                ""line"": 22,
                ""column"": 27
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""12000000-0000-4000-8000-000000000012"": {
        ""id"": ""12000000-0000-4000-8000-000000000012"",
        ""name"": ""Combined"",
        ""kind"": 10,
        ""requirement"": 1,
        ""modifier"": 1,
        ""returnTypeInfo"": {
          ""type"": 2,
          ""required"": true
        },
        ""code"": ""return Inference.Map(5, (value) => value switch { int n when (n & 1) == 1 => n << 2, _ => 0 });"",
        ""projectId"": ""project-id"",
        ""createdAt"": 1700000000000,
        ""updatedAt"": 1700000000000,
        ""getter"": {
          ""compilerRevision"": 17,
          ""source"": {
            ""name"": ""Inference.Combined"",
            ""uri"": ""Inference.neo"",
            ""coordinateSpace"": ""file""
          },
          ""parameters"": [
            {
              ""id"": ""__this__"",
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""12000000-0000-4000-8000-000000000001""
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""12000000-0000-4000-8000-000000000001""
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
                ""type"": ""callFunction"",
                ""memberId"": ""12000000-0000-4000-8000-000000000003"",
                ""receiver"": {
                  ""kind"": ""static"",
                  ""memberId"": ""12000000-0000-4000-8000-000000000003""
                },
                ""args"": [
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
                        ""type"": 25,
                        ""required"": true,
                        ""returnTypeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""argumentTypes"": [
                          {
                            ""type"": 2,
                            ""required"": true
                          }
                        ]
                      },
                      ""value"": {
                        ""code"": ""(value) => value switch { int n when (n & 1) == 1 => n << 2, _ => 0 }"",
                        ""action"": {
                          ""compilerRevision"": 17,
                          ""source"": {
                            ""uri"": ""Inference.neo"",
                            ""coordinateSpace"": ""file"",
                            ""name"": ""Inference.Combined <delegate>""
                          },
                          ""parameters"": [
                            {
                              ""id"": ""__this__"",
                              ""typeInfo"": {
                                ""type"": 7,
                                ""required"": true,
                                ""classId"": ""12000000-0000-4000-8000-000000000001""
                              },
                              ""pointer"": {
                                ""type"": ""value"",
                                ""value"": {
                                  ""typeInfo"": {
                                    ""type"": 7,
                                    ""required"": true,
                                    ""classId"": ""12000000-0000-4000-8000-000000000001""
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
                              ""id"": ""__lambda_0_arg_0__"",
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
                                ""type"": ""switchExpression"",
                                ""selectorTypeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                },
                                ""selector"": {
                                  ""type"": ""variable"",
                                  ""variableId"": ""__lambda_0_arg_0__""
                                },
                                ""selectorId"": ""__switch:0"",
                                ""arms"": [
                                  {
                                    ""pattern"": {
                                      ""kind"": ""type"",
                                      ""checkType"": {
                                        ""type"": 2,
                                        ""required"": true
                                      },
                                      ""bindingId"": ""__switch:0_arm_0""
                                    },
                                    ""guard"": {
                                      ""type"": ""operation"",
                                      ""operation"": {
                                        ""type"": ""boolean"",
                                        ""expression"": {
                                          ""condition"": {
                                            ""type"": ""equalTo"",
                                            ""operand1"": {
                                              ""type"": ""function"",
                                              ""function"": {
                                                ""type"": ""bitwiseOp"",
                                                ""info"": {
                                                  ""op"": ""and"",
                                                  ""argPointers"": [
                                                    {
                                                      ""type"": ""variable"",
                                                      ""variableId"": ""__switch:0_arm_0""
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
                                                ""line"": 24,
                                                ""column"": 94
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
                                    },
                                    ""value"": {
                                      ""type"": ""function"",
                                      ""function"": {
                                        ""type"": ""bitwiseOp"",
                                        ""info"": {
                                          ""op"": ""shiftLeft"",
                                          ""argPointers"": [
                                            {
                                              ""type"": ""variable"",
                                              ""variableId"": ""__switch:0_arm_0""
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
                                        ""line"": 24,
                                        ""column"": 109
                                      }
                                    }
                                  },
                                  {
                                    ""pattern"": {
                                      ""kind"": ""discard""
                                    },
                                    ""value"": {
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
                                ]
                              },
                              ""source"": {
                                ""line"": 24,
                                ""column"": 65
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
                ],
                ""typeArguments"": [
                  {
                    ""type"": 2,
                    ""required"": true
                  },
                  {
                    ""type"": 2,
                    ""required"": true
                  }
                ],
                ""callSiteId"": ""1:21:0"",
                ""source"": {
                  ""line"": 24,
                  ""column"": 50
                }
              },
              ""source"": {
                ""line"": 24,
                ""column"": 30
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""4ed69bfa-b85a-5778-8f83-ae7723e75bad"": {
        ""createdAt"": 0,
        ""updatedAt"": 0,
        ""id"": ""4ed69bfa-b85a-5778-8f83-ae7723e75bad"",
        ""projectId"": ""project-id"",
        ""name"": ""User"",
        ""kind"": 7,
        ""classId"": ""96c616e0-e204-5f56-9f77-90b195896a5a"",
        ""requirement"": 1,
        ""storage"": 5,
        ""defaultValue"": {
          ""value"": {}
        },
        ""valueId"": ""1454090d-35f9-594e-b134-05cb3f6c1d60""
      },
      ""root-assets-member-id"": {
        ""createdAt"": 0,
        ""updatedAt"": 0,
        ""id"": ""root-assets-member-id"",
        ""projectId"": ""project-id"",
        ""name"": ""Assets"",
        ""kind"": 7,
        ""classId"": ""root-assets-class-id"",
        ""classArguments"": {},
        ""storage"": 1,
        ""valueId"": ""root-assets-value-id"",
        ""defaultValue"": {
          ""value"": {}
        },
        ""requirement"": 1
      },
      ""root-save-member-id"": {
        ""createdAt"": 0,
        ""updatedAt"": 0,
        ""id"": ""root-save-member-id"",
        ""projectId"": ""project-id"",
        ""name"": ""Save"",
        ""kind"": 7,
        ""classId"": ""root-save-class-id"",
        ""classArguments"": {},
        ""storage"": 2,
        ""valueId"": ""root-save-value-id"",
        ""defaultValue"": {
          ""value"": {}
        },
        ""requirement"": 1
      },
      ""root-session-member-id"": {
        ""createdAt"": 0,
        ""updatedAt"": 0,
        ""id"": ""root-session-member-id"",
        ""projectId"": ""project-id"",
        ""name"": ""Session"",
        ""kind"": 7,
        ""classId"": ""root-session-class-id"",
        ""classArguments"": {},
        ""storage"": 3,
        ""valueId"": ""root-session-value-id"",
        ""defaultValue"": {
          ""value"": {}
        },
        ""requirement"": 1
      }
    },
    ""enums"": {},
    ""interfaces"": {},
    ""classes"": {
      ""12000000-0000-4000-8000-000000000001"": {
        ""id"": ""12000000-0000-4000-8000-000000000001"",
        ""name"": ""Inference"",
        ""schema"": {
          ""Captured"": ""12000000-0000-4000-8000-000000000006"",
          ""Combined"": ""12000000-0000-4000-8000-000000000012"",
          ""Forward"": ""12000000-0000-4000-8000-000000000004"",
          ""Identity"": ""12000000-0000-4000-8000-000000000002"",
          ""Map"": ""12000000-0000-4000-8000-000000000003"",
          ""Mark"": ""12000000-0000-4000-8000-000000000010"",
          ""Nested"": ""12000000-0000-4000-8000-000000000005"",
          ""Once"": ""12000000-0000-4000-8000-000000000008"",
          ""Order"": ""12000000-0000-4000-8000-000000000011"",
          ""Pair"": ""12000000-0000-4000-8000-000000000009"",
          ""Typed"": ""12000000-0000-4000-8000-000000000007""
        },
        ""schemaKeyOrder"": [
          ""Identity"",
          ""Map"",
          ""Forward"",
          ""Nested"",
          ""Captured"",
          ""Typed"",
          ""Once"",
          ""Pair"",
          ""Mark"",
          ""Order"",
          ""Combined""
        ],
        ""projectId"": ""project-id"",
        ""createdAt"": 1700000000000,
        ""updatedAt"": 1700000000000
      },
      ""96c616e0-e204-5f56-9f77-90b195896a5a"": {
        ""createdAt"": 0,
        ""updatedAt"": 0,
        ""id"": ""96c616e0-e204-5f56-9f77-90b195896a5a"",
        ""projectId"": ""project-id"",
        ""name"": ""User"",
        ""schema"": {},
        ""uiVisibility"": 1,
        ""allowedStorage"": 5
      },
      ""root-assets-class-id"": {
        ""createdAt"": 0,
        ""updatedAt"": 0,
        ""id"": ""root-assets-class-id"",
        ""projectId"": ""project-id"",
        ""name"": ""Assets"",
        ""schema"": {},
        ""schemaKeyOrder"": []
      },
      ""root-save-class-id"": {
        ""createdAt"": 0,
        ""updatedAt"": 0,
        ""id"": ""root-save-class-id"",
        ""projectId"": ""project-id"",
        ""name"": ""Save"",
        ""schema"": {},
        ""schemaKeyOrder"": []
      },
      ""root-session-class-id"": {
        ""createdAt"": 0,
        ""updatedAt"": 0,
        ""id"": ""root-session-class-id"",
        ""projectId"": ""project-id"",
        ""name"": ""Session"",
        ""schema"": {},
        ""schemaKeyOrder"": []
      }
    },
    ""constructors"": {},
    ""variants"": {},
    ""variantFolders"": {},
    ""internalRecordRelations"": {},
    ""values"": {
      ""1454090d-35f9-594e-b134-05cb3f6c1d60"": {
        ""createdAt"": 0,
        ""updatedAt"": 0,
        ""id"": ""1454090d-35f9-594e-b134-05cb3f6c1d60"",
        ""projectId"": ""project-id"",
        ""value"": {},
        ""classId"": ""96c616e0-e204-5f56-9f77-90b195896a5a""
      },
      ""root-assets-value-id"": {
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""root-assets-value-id"",
        ""projectId"": ""project-id"",
        ""classId"": ""root-assets-class-id"",
        ""value"": {}
      },
      ""root-save-value-id"": {
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""root-save-value-id"",
        ""projectId"": ""project-id"",
        ""classId"": ""root-save-class-id"",
        ""value"": {}
      },
      ""root-session-value-id"": {
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""root-session-value-id"",
        ""projectId"": ""project-id"",
        ""classId"": ""root-session-class-id"",
        ""value"": {}
      }
    },
    ""valuePartitions"": {},
    ""dialogues"": {},
    ""dialogueGroups"": {},
    ""priorityGroups"": {},
    ""files"": {},
    ""textureTemplates"": {},
    ""audioClipTemplates"": {}
  },
  ""cases"": [
    {
      ""name"": ""Nested"",
      ""expected"": 7
    },
    {
      ""name"": ""Captured"",
      ""expected"": 7
    },
    {
      ""name"": ""Typed"",
      ""expected"": 10
    },
    {
      ""name"": ""Once"",
      ""expected"": 3
    },
    {
      ""name"": ""Order"",
      ""expected"": 2
    },
    {
      ""name"": ""Combined"",
      ""expected"": 20
    }
  ]
}";
    }
}
