// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
//
// VENDORED — DO NOT HAND-EDIT.
// Source of record:
// neo-compose/src/models/neoscript/neoscript-constructor-parity-fixture.json
// P43 section 6.1. Construction is the one place two evaluators can implement
// every individual step correctly and still produce different instances,
// because the answer is an ORDER: member initializers, then the base chain,
// then the body, then the call-site initializer block — with the last one
// winning even for a member the body already wrote. Prose cannot pin that; this
// fixture pins it as executable IR shared by both runtimes.
//
// To re-vendor: copy the JSON verbatim and double every `"` for the C#
// verbatim string. Consumed here by NeoConstructorParityTests and on the web
// side by src/models/neoscript/neoscript-constructor-parity.test.ts.

#nullable enable

namespace NeoCompose.Tests
{
    public static class NeoConstructorParityFixture
    {
        public const string Json = @"{
  ""$comment"": ""P43 \u00a76.1 cross-runtime declared-constructor parity fixture. Construction is the one place two evaluators can agree on every individual step and still disagree on the ANSWER, because the answer is an ORDER: member initializers, then the base chain, then the body, then the call-site initializer block \u2014 with the last one winning even for a member the body wrote (\u00a71.2, \u00a76.1 step 4). That order is pinned here as executable IR rather than prose. Consumed by src/models/neoscript/neoscript-constructor-parity.test.ts (web) and NeoConstructorParityTests (neo-compose-dotnet, vendored copy at src/NeoComposeUnity/Tests/NeoConstructorParityFixture.cs). Hand-maintained: a fixture generated from one runtime cannot catch a divergence in the other's source of truth. Each evaluateCases entry is a compiled getter whose sole instruction returns a `declaredConstructor` pointer; run it against `document` with `__this__` = null and `__root__` built from the project's three root members, then read the produced record's schemaKey -> row value for `expectedFields`, or assert the thrown message contains `expectedErrorContains`. P49 \u00a71 extends it with a required constructor: `Gate` and `Latch` carry `requiredConstructorId` rather than `constructorIds`, and `ctor-latch` is a class header's constructor whose `baseArguments` pass the header parameter to the base's required constructor, whose `baseInitializerFields` settle an inherited member from the base clause, and whose `code` is the `init` block. The three P49 cases pin the stage order that adds (base clause block, then init body, then call-site block) and the implicit `new` \u00a71.3 disables."",
  ""document"": {
    ""project"": {
      ""id"": ""project-p43"",
      ""name"": ""P43 constructor parity"",
      ""owner"": {
        ""kind"": ""organization"",
        ""id"": ""org-p43""
      },
      ""author"": {
        ""kind"": ""user"",
        ""id"": ""user-p43""
      },
      ""rootAssetsMemberId"": ""member-root-assets"",
      ""rootSaveFileMemberId"": ""member-root-save"",
      ""rootSessionMemberId"": ""member-root-session"",
      ""defaultPriorityGroupId"": null,
      ""defaultTextureTemplateId"": null,
      ""defaultAudioClipTemplateId"": null,
      ""exportSettings"": null,
      ""createdAt"": 1,
      ""updatedAt"": 1
    },
    ""classes"": [
      {
        ""projectId"": ""project-p43"",
        ""isAbstract"": false,
        ""hiddenInMemberSelector"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""class-base"",
        ""name"": ""Base"",
        ""schema"": {
          ""Tag"": ""member-tag""
        },
        ""constructorIds"": [""ctor-base""]
      },
      {
        ""projectId"": ""project-p43"",
        ""isAbstract"": false,
        ""hiddenInMemberSelector"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""class-derived"",
        ""name"": ""Derived"",
        ""extendsClassId"": ""class-base"",
        ""schema"": {
          ""Note"": ""member-note"",
          ""Prefix"": ""member-prefix""
        },
        ""constructorIds"": [""ctor-derived"", ""ctor-derived-prefixed""]
      },
      {
        ""projectId"": ""project-p43"",
        ""isAbstract"": false,
        ""hiddenInMemberSelector"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""class-foo"",
        ""name"": ""Foo"",
        ""schema"": {
          ""Bar"": ""member-bar"",
          ""Count"": ""member-count""
        },
        ""constructorIds"": [""ctor-foo""]
      },
      {
        ""projectId"": ""project-p43"",
        ""isAbstract"": false,
        ""hiddenInMemberSelector"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""class-thrower"",
        ""name"": ""Thrower"",
        ""schema"": {
          ""ThrowTag"": ""member-throw-tag""
        },
        ""constructorIds"": [""ctor-thrower""]
      },
      {
        ""projectId"": ""project-p43"",
        ""isAbstract"": false,
        ""hiddenInMemberSelector"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""class-reader"",
        ""name"": ""Reader"",
        ""schema"": {
          ""Count"": ""member-reader-count""
        },
        ""constructorIds"": [""ctor-reader""]
      },
      {
        ""projectId"": ""project-p43"",
        ""isAbstract"": false,
        ""hiddenInMemberSelector"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""class-holder"",
        ""name"": ""Holder"",
        ""schema"": {
          ""Label"": ""member-label""
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""isAbstract"": false,
        ""hiddenInMemberSelector"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""class-boom"",
        ""name"": ""Boom"",
        ""schema"": {
          ""BoomLabel"": ""member-boom-label""
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""isAbstract"": false,
        ""hiddenInMemberSelector"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""class-node"",
        ""name"": ""Node"",
        ""schema"": {
          ""Child"": ""member-node-child""
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""isAbstract"": false,
        ""hiddenInMemberSelector"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""class-root-assets"",
        ""name"": ""RootAssets"",
        ""schema"": {}
      },
      {
        ""projectId"": ""project-p43"",
        ""isAbstract"": false,
        ""hiddenInMemberSelector"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""class-root-save"",
        ""name"": ""RootSave"",
        ""schema"": {
          ""WorldTime"": ""member-world-time""
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""isAbstract"": false,
        ""hiddenInMemberSelector"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""class-root-session"",
        ""name"": ""RootSession"",
        ""schema"": {}
      },
      {
        ""projectId"": ""project-p43"",
        ""isAbstract"": false,
        ""hiddenInMemberSelector"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""class-nullable"",
        ""name"": ""Nullable"",
        ""schema"": {
          ""Maybe"": ""member-maybe"",
          ""Kept"": ""member-kept""
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""isAbstract"": false,
        ""hiddenInMemberSelector"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""class-gate"",
        ""name"": ""Gate"",
        ""schema"": {
          ""Mark"": ""member-gate-mark"",
          ""Seal"": ""member-gate-seal""
        },
        ""requiredConstructorId"": ""ctor-gate""
      },
      {
        ""projectId"": ""project-p43"",
        ""isAbstract"": false,
        ""hiddenInMemberSelector"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""class-latch"",
        ""name"": ""Latch"",
        ""extendsClassId"": ""class-gate"",
        ""schema"": {
          ""Note"": ""member-latch-note""
        },
        ""requiredConstructorId"": ""ctor-latch""
      }
    ],
    ""members"": [
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-tag"",
        ""name"": ""Tag"",
        ""kind"": 3,
        ""required"": true,
        ""defaultValue"": {
          ""value"": ""tag""
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-note"",
        ""name"": ""Note"",
        ""kind"": 3,
        ""required"": true,
        ""defaultValue"": {
          ""value"": ""note""
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-bar"",
        ""name"": ""Bar"",
        ""kind"": 3,
        ""required"": true,
        ""defaultValue"": {
          ""value"": ""Bar""
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-count"",
        ""name"": ""Count"",
        ""kind"": 2,
        ""required"": true,
        ""defaultValue"": {
          ""value"": 1
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-label"",
        ""name"": ""Label"",
        ""kind"": 3,
        ""required"": true,
        ""defaultValue"": {
          ""init"": {
            ""code"": ""\""computed\"""",
            ""compiled"": {
              ""compilerRevision"": 3,
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
                      ""value"": ""computed""
                    }
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
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-boom-label"",
        ""name"": ""BoomLabel"",
        ""kind"": 3,
        ""required"": true,
        ""defaultValue"": {
          ""init"": {
            ""code"": ""Explode(\""initializer exploded\"")"",
            ""compiled"": {
              ""compilerRevision"": 3,
              ""parameters"": [
                {
                  ""id"": ""__this__"",
                  ""typeInfo"": {
                    ""type"": 0,
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
                },
                {
                  ""id"": ""__root__"",
                  ""typeInfo"": {
                    ""type"": 0,
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
                      ""value"": ""initializer exploded""
                    }
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
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-node-child"",
        ""name"": ""Child"",
        ""kind"": 7,
        ""classId"": ""class-node"",
        ""required"": true,
        ""defaultValue"": {
          ""init"": {
            ""code"": ""new Node()"",
            ""compiled"": {
              ""compilerRevision"": 3,
              ""parameters"": [
                {
                  ""id"": ""__this__"",
                  ""typeInfo"": {
                    ""type"": 0,
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
                },
                {
                  ""id"": ""__root__"",
                  ""typeInfo"": {
                    ""type"": 0,
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
              ""instructions"": [
                {
                  ""type"": ""return"",
                  ""pointer"": {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""declaredConstructor"",
                      ""info"": {
                        ""schemaClassInfo"": {
                          ""type"": 7,
                          ""required"": true,
                          ""classId"": ""class-node""
                        },
                        ""constructorId"": null,
                        ""args"": [],
                        ""fields"": []
                      }
                    }
                  }
                }
              ],
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class-node""
              }
            }
          }
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-throw-tag"",
        ""name"": ""ThrowTag"",
        ""kind"": 3,
        ""required"": true,
        ""defaultValue"": {
          ""value"": ""quiet""
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-reader-count"",
        ""name"": ""Count"",
        ""kind"": 2,
        ""required"": true,
        ""defaultValue"": {
          ""value"": 1
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-world-time"",
        ""name"": ""WorldTime"",
        ""kind"": 2,
        ""required"": true,
        ""defaultValue"": {
          ""value"": 7
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-root-assets"",
        ""name"": ""Assets"",
        ""kind"": 7,
        ""classId"": ""class-root-assets"",
        ""required"": true,
        ""valueId"": ""value-root-assets""
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-root-save"",
        ""name"": ""Save"",
        ""kind"": 7,
        ""classId"": ""class-root-save"",
        ""required"": true,
        ""valueId"": ""value-root-save""
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-root-session"",
        ""name"": ""Session"",
        ""kind"": 7,
        ""classId"": ""class-root-session"",
        ""required"": true,
        ""valueId"": ""value-root-session""
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-prefix"",
        ""name"": ""Prefix"",
        ""kind"": 3,
        ""required"": true,
        ""defaultValue"": {
          ""value"": ""pre""
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-maybe"",
        ""name"": ""Maybe"",
        ""kind"": 3,
        ""required"": false,
        ""defaultValue"": {
          ""value"": ""maybe""
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-kept"",
        ""name"": ""Kept"",
        ""kind"": 3,
        ""required"": true,
        ""defaultValue"": {
          ""value"": ""kept""
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-gate-mark"",
        ""name"": ""Mark"",
        ""kind"": 3,
        ""required"": true,
        ""defaultValue"": {
          ""value"": ""mark-initializer""
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-gate-seal"",
        ""name"": ""Seal"",
        ""kind"": 3,
        ""required"": true,
        ""defaultValue"": {
          ""value"": ""seal-initializer""
        }
      },
      {
        ""projectId"": ""project-p43"",
        ""accessModifierKind"": ""public"",
        ""locked"": false,
        ""isStatic"": false,
        ""createdAt"": 1,
        ""updatedAt"": 1,
        ""id"": ""member-latch-note"",
        ""name"": ""Note"",
        ""kind"": 3,
        ""required"": true,
        ""defaultValue"": {
          ""value"": ""note-initializer""
        }
      }
    ],
    ""values"": [
      {
        ""id"": ""value-world-time"",
        ""projectId"": ""project-p43"",
        ""value"": 7,
        ""createdAt"": 1,
        ""updatedAt"": 1
      },
      {
        ""id"": ""value-root-assets"",
        ""projectId"": ""project-p43"",
        ""value"": {},
        ""classId"": ""class-root-assets"",
        ""createdAt"": 1,
        ""updatedAt"": 1
      },
      {
        ""id"": ""value-root-save"",
        ""projectId"": ""project-p43"",
        ""value"": {
          ""WorldTime"": ""value-world-time""
        },
        ""classId"": ""class-root-save"",
        ""createdAt"": 1,
        ""updatedAt"": 1
      },
      {
        ""id"": ""value-root-session"",
        ""projectId"": ""project-p43"",
        ""value"": {},
        ""classId"": ""class-root-session"",
        ""createdAt"": 1,
        ""updatedAt"": 1
      }
    ],
    ""constructors"": [
      {
        ""id"": ""ctor-base"",
        ""projectId"": ""project-p43"",
        ""classId"": ""class-base"",
        ""argumentTypes"": [
          {
            ""type"": 3,
            ""required"": true,
            ""name"": ""Tag""
          }
        ],
        ""code"": ""this.Tag = \""base:\"" + Tag;"",
        ""action"": {
          ""compilerRevision"": 3,
          ""parameters"": [
            {
              ""id"": ""__this__"",
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class-base""
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-base""
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
            }
          ],
          ""instructions"": [
            {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""keyOf"",
                  ""memberId"": ""member-tag"",
                  ""keyOf"": {
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""__this__""
                    },
                    ""key"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""Tag""
                      }
                    }
                  }
                },
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""writability"": ""runtime""
              },
              ""operator"": ""="",
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
                          ""value"": ""base:""
                        }
                      },
                      {
                        ""type"": ""variable"",
                        ""variableId"": ""__arg_0__""
                      }
                    ]
                  }
                }
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 0,
            ""required"": true
          }
        },
        ""createdAt"": 1,
        ""updatedAt"": 1
      },
      {
        ""id"": ""ctor-derived"",
        ""projectId"": ""project-p43"",
        ""classId"": ""class-derived"",
        ""argumentTypes"": [
          {
            ""type"": 3,
            ""required"": true,
            ""name"": ""Suffix""
          }
        ],
        ""code"": ""this.Note = \""derived:\"" + this.Tag;"",
        ""action"": {
          ""compilerRevision"": 3,
          ""parameters"": [
            {
              ""id"": ""__this__"",
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class-derived""
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-derived""
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
            }
          ],
          ""instructions"": [
            {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""keyOf"",
                  ""memberId"": ""member-note"",
                  ""keyOf"": {
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""__this__""
                    },
                    ""key"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""Note""
                      }
                    }
                  }
                },
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""writability"": ""runtime""
              },
              ""operator"": ""="",
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
                          ""value"": ""derived:""
                        }
                      },
                      {
                        ""type"": ""keyOf"",
                        ""memberId"": ""member-tag"",
                        ""keyOf"": {
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""__this__""
                          },
                          ""key"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 3,
                                ""required"": true
                              },
                              ""value"": ""Tag""
                            }
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 0,
            ""required"": true
          }
        },
        ""baseArguments"": [
          {
            ""name"": ""Tag"",
            ""code"": ""Suffix""
          }
        ],
        ""compiledBaseArguments"": [
          {
            ""compilerRevision"": 3,
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
              }
            ],
            ""instructions"": [
              {
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""__arg_0__""
                }
              }
            ],
            ""typeInfo"": {
              ""type"": 3,
              ""required"": true
            }
          }
        ],
        ""createdAt"": 1,
        ""updatedAt"": 1
      },
      {
        ""id"": ""ctor-foo"",
        ""projectId"": ""project-p43"",
        ""classId"": ""class-foo"",
        ""argumentTypes"": [
          {
            ""type"": 1,
            ""required"": true,
            ""name"": ""AllCaps""
          }
        ],
        ""code"": ""if (AllCaps) {\n  this.Bar = \""BAR\"";\n}"",
        ""action"": {
          ""compilerRevision"": 3,
          ""parameters"": [
            {
              ""id"": ""__this__"",
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class-foo""
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-foo""
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
                ""type"": 1,
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 1,
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
                      ""type"": ""equalTo"",
                      ""operand1"": {
                        ""type"": ""variable"",
                        ""variableId"": ""__arg_0__""
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
                      ""type"": ""assign"",
                      ""target"": {
                        ""pointer"": {
                          ""type"": ""keyOf"",
                          ""memberId"": ""member-bar"",
                          ""keyOf"": {
                            ""pointer"": {
                              ""type"": ""variable"",
                              ""variableId"": ""__this__""
                            },
                            ""key"": {
                              ""type"": ""value"",
                              ""value"": {
                                ""typeInfo"": {
                                  ""type"": 3,
                                  ""required"": true
                                },
                                ""value"": ""Bar""
                              }
                            }
                          }
                        },
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""writability"": ""runtime""
                      },
                      ""operator"": ""="",
                      ""pointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""BAR""
                        }
                      }
                    }
                  ]
                }
              ],
              ""else"": null
            }
          ],
          ""typeInfo"": {
            ""type"": 0,
            ""required"": true
          }
        },
        ""createdAt"": 1,
        ""updatedAt"": 1
      },
      {
        ""id"": ""ctor-thrower"",
        ""projectId"": ""project-p43"",
        ""classId"": ""class-thrower"",
        ""argumentTypes"": [],
        ""code"": ""throw \""constructor rejected the arguments\"";"",
        ""action"": {
          ""compilerRevision"": 3,
          ""parameters"": [
            {
              ""id"": ""__this__"",
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class-thrower""
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-thrower""
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
              ""type"": ""throw"",
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""constructor rejected the arguments""
                }
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 0,
            ""required"": true
          }
        },
        ""createdAt"": 1,
        ""updatedAt"": 1
      },
      {
        ""id"": ""ctor-reader"",
        ""projectId"": ""project-p43"",
        ""classId"": ""class-reader"",
        ""argumentTypes"": [],
        ""code"": ""this.Count = root.Save.WorldTime;"",
        ""action"": {
          ""compilerRevision"": 3,
          ""parameters"": [
            {
              ""id"": ""__this__"",
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class-reader""
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-reader""
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
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""keyOf"",
                  ""memberId"": ""member-reader-count"",
                  ""keyOf"": {
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""__this__""
                    },
                    ""key"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""Count""
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
                ""type"": ""keyOf"",
                ""memberId"": ""member-world-time"",
                ""keyOf"": {
                  ""pointer"": {
                    ""type"": ""keyOf"",
                    ""memberId"": ""member-root-save"",
                    ""keyOf"": {
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""__root__""
                      },
                      ""key"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""Save""
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
                      ""value"": ""WorldTime""
                    }
                  }
                }
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 0,
            ""required"": true
          }
        },
        ""createdAt"": 1,
        ""updatedAt"": 1
      },
      {
        ""id"": ""ctor-derived-prefixed"",
        ""projectId"": ""project-p43"",
        ""classId"": ""class-derived"",
        ""argumentTypes"": [
          {
            ""type"": 2,
            ""required"": true,
            ""name"": ""Level""
          }
        ],
        ""code"": ""this.Note = \""level:\"" + this.Tag;"",
        ""action"": {
          ""compilerRevision"": 3,
          ""parameters"": [
            {
              ""id"": ""__this__"",
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class-derived""
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-derived""
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
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""keyOf"",
                  ""memberId"": ""member-note"",
                  ""keyOf"": {
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""__this__""
                    },
                    ""key"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""Note""
                      }
                    }
                  }
                },
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""writability"": ""runtime""
              },
              ""operator"": ""="",
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
                          ""value"": ""level:""
                        }
                      },
                      {
                        ""type"": ""keyOf"",
                        ""memberId"": ""member-tag"",
                        ""keyOf"": {
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""__this__""
                          },
                          ""key"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 3,
                                ""required"": true
                              },
                              ""value"": ""Tag""
                            }
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 0,
            ""required"": true
          }
        },
        ""baseArguments"": [
          {
            ""name"": ""Tag"",
            ""code"": ""this.Prefix""
          }
        ],
        ""compiledBaseArguments"": [
          {
            ""compilerRevision"": 3,
            ""parameters"": [
              {
                ""id"": ""__this__"",
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""class-derived""
                },
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 7,
                      ""required"": true,
                      ""classId"": ""class-derived""
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
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""keyOf"",
                  ""memberId"": ""member-prefix"",
                  ""keyOf"": {
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""__this__""
                    },
                    ""key"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""Prefix""
                      }
                    }
                  }
                }
              }
            ],
            ""typeInfo"": {
              ""type"": 3,
              ""required"": true
            }
          }
        ],
        ""createdAt"": 1,
        ""updatedAt"": 1
      },
      {
        ""id"": ""ctor-gate"",
        ""projectId"": ""project-p43"",
        ""classId"": ""class-gate"",
        ""argumentTypes"": [
          {
            ""type"": 3,
            ""required"": true,
            ""name"": ""Mark""
          }
        ],
        ""code"": ""this.Mark = \""gate:\"" + Mark;"",
        ""action"": {
          ""compilerRevision"": 3,
          ""parameters"": [
            {
              ""id"": ""__this__"",
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class-gate""
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-gate""
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
            }
          ],
          ""instructions"": [
            {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""keyOf"",
                  ""memberId"": ""member-gate-mark"",
                  ""keyOf"": {
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""__this__""
                    },
                    ""key"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""Mark""
                      }
                    }
                  }
                },
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""writability"": ""runtime""
              },
              ""operator"": ""="",
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
                          ""value"": ""gate:""
                        }
                      },
                      {
                        ""type"": ""variable"",
                        ""variableId"": ""__arg_0__""
                      }
                    ]
                  }
                }
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 0,
            ""required"": true
          }
        },
        ""createdAt"": 1,
        ""updatedAt"": 1
      },
      {
        ""id"": ""ctor-latch"",
        ""projectId"": ""project-p43"",
        ""classId"": ""class-latch"",
        ""argumentTypes"": [
          {
            ""type"": 3,
            ""required"": true,
            ""name"": ""Key""
          }
        ],
        ""code"": ""this.Note = \""init:\"" + this.Mark;"",
        ""action"": {
          ""compilerRevision"": 3,
          ""parameters"": [
            {
              ""id"": ""__this__"",
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""class-latch""
              },
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-latch""
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
            }
          ],
          ""instructions"": [
            {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""keyOf"",
                  ""memberId"": ""member-latch-note"",
                  ""keyOf"": {
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""__this__""
                    },
                    ""key"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""Note""
                      }
                    }
                  }
                },
                ""typeInfo"": {
                  ""type"": 3,
                  ""required"": true
                },
                ""writability"": ""runtime""
              },
              ""operator"": ""="",
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
                          ""value"": ""init:""
                        }
                      },
                      {
                        ""type"": ""keyOf"",
                        ""memberId"": ""member-gate-mark"",
                        ""keyOf"": {
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""__this__""
                          },
                          ""key"": {
                            ""type"": ""value"",
                            ""value"": {
                              ""typeInfo"": {
                                ""type"": 3,
                                ""required"": true
                              },
                              ""value"": ""Mark""
                            }
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
          ],
          ""typeInfo"": {
            ""type"": 0,
            ""required"": true
          }
        },
        ""baseArguments"": [
          {
            ""name"": ""Mark"",
            ""code"": ""Key""
          }
        ],
        ""compiledBaseArguments"": [
          {
            ""compilerRevision"": 3,
            ""parameters"": [
              {
                ""id"": ""__this__"",
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""class-latch""
                },
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 7,
                      ""required"": true,
                      ""classId"": ""class-latch""
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
              }
            ],
            ""instructions"": [
              {
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""__arg_0__""
                }
              }
            ],
            ""typeInfo"": {
              ""type"": 3,
              ""required"": true
            }
          }
        ],
        ""baseInitializerFields"": [
          {
            ""name"": ""Seal"",
            ""code"": ""\""seal-base-clause\""""
          }
        ],
        ""compiledBaseInitializerFields"": [
          {
            ""compilerRevision"": 3,
            ""parameters"": [
              {
                ""id"": ""__this__"",
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": true,
                  ""classId"": ""class-latch""
                },
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 7,
                      ""required"": true,
                      ""classId"": ""class-latch""
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
                    ""value"": ""seal-base-clause""
                  }
                }
              }
            ],
            ""typeInfo"": {
              ""type"": 3,
              ""required"": true
            }
          }
        ],
        ""createdAt"": 1,
        ""updatedAt"": 1
      }
    ]
  },
  ""evaluateCases"": [
    {
      ""name"": ""member initializers run and the body's untaken branch changes nothing"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-foo""
                  },
                  ""constructorId"": ""ctor-foo"",
                  ""args"": [
                    {
                      ""name"": ""AllCaps"",
                      ""valuePointer"": {
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
                  ],
                  ""fields"": []
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-foo""
        }
      },
      ""expectedFields"": {
        ""Bar"": ""Bar"",
        ""Count"": 1
      }
    },
    {
      ""name"": ""the constructor body overwrites what a member initializer wrote"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-foo""
                  },
                  ""constructorId"": ""ctor-foo"",
                  ""args"": [
                    {
                      ""name"": ""AllCaps"",
                      ""valuePointer"": {
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
                  ],
                  ""fields"": []
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-foo""
        }
      },
      ""expectedFields"": {
        ""Bar"": ""BAR"",
        ""Count"": 1
      }
    },
    {
      ""name"": ""the call-site initializer block is applied last and beats the body"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-foo""
                  },
                  ""constructorId"": ""ctor-foo"",
                  ""args"": [
                    {
                      ""name"": ""AllCaps"",
                      ""valuePointer"": {
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
                  ],
                  ""fields"": [
                    {
                      ""schemaKey"": ""Bar"",
                      ""memberId"": ""member-bar"",
                      ""valuePointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""custom""
                        }
                      }
                    }
                  ]
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-foo""
        }
      },
      ""expectedFields"": {
        ""Bar"": ""custom""
      }
    },
    {
      ""name"": ""precedence is static: the branch the body took does not change it"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-foo""
                  },
                  ""constructorId"": ""ctor-foo"",
                  ""args"": [
                    {
                      ""name"": ""AllCaps"",
                      ""valuePointer"": {
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
                  ],
                  ""fields"": [
                    {
                      ""schemaKey"": ""Bar"",
                      ""memberId"": ""member-bar"",
                      ""valuePointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""custom""
                        }
                      }
                    }
                  ]
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-foo""
        }
      },
      ""expectedFields"": {
        ""Bar"": ""custom""
      }
    },
    {
      ""name"": ""new() on a class with no parameterless constructor means member initializers only"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-foo""
                  },
                  ""constructorId"": null,
                  ""args"": [],
                  ""fields"": []
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-foo""
        }
      },
      ""expectedFields"": {
        ""Bar"": ""Bar"",
        ""Count"": 1
      }
    },
    {
      ""name"": ""the base constructor runs before the derived body, bound by ': base(...)'"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-derived""
                  },
                  ""constructorId"": ""ctor-derived"",
                  ""args"": [
                    {
                      ""name"": ""Suffix"",
                      ""valuePointer"": {
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
                  ],
                  ""fields"": []
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-derived""
        }
      },
      ""expectedFields"": {
        ""Tag"": ""base:x"",
        ""Note"": ""derived:base:x""
      }
    },
    {
      ""name"": ""an init-backed member default is evaluated at construction"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-holder""
                  },
                  ""constructorId"": null,
                  ""args"": [],
                  ""fields"": []
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-holder""
        }
      },
      ""expectedFields"": {
        ""Label"": ""computed""
      }
    },
    {
      ""name"": ""an overridden member's initializer still runs before the call-site block replaces it"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-boom""
                  },
                  ""constructorId"": null,
                  ""args"": [],
                  ""fields"": [
                    {
                      ""schemaKey"": ""BoomLabel"",
                      ""memberId"": ""member-boom-label"",
                      ""valuePointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""override""
                        }
                      }
                    }
                  ]
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-boom""
        }
      },
      ""expectedErrorContains"": ""initializer exploded""
    },
    {
      ""name"": ""a throw inside a constructor body aborts with the thrown message"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-thrower""
                  },
                  ""constructorId"": ""ctor-thrower"",
                  ""args"": [],
                  ""fields"": []
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-thrower""
        }
      },
      ""expectedErrorContains"": ""constructor rejected the arguments""
    },
    {
      ""name"": ""a body reads root.Save and resolves the authored default"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-reader""
                  },
                  ""constructorId"": ""ctor-reader"",
                  ""args"": [],
                  ""fields"": []
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-reader""
        }
      },
      ""expectedFields"": {
        ""Count"": 7
      }
    },
    {
      ""name"": ""unbounded construction through a member initializer hits the depth cap"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-node""
                  },
                  ""constructorId"": null,
                  ""args"": [],
                  ""fields"": []
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-node""
        }
      },
      ""expectedErrorContains"": ""Class construction depth exceeded 64 frames""
    },
    {
      ""name"": ""a ': base(...)' argument reads `this`, so member initializers have already run"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-derived""
                  },
                  ""constructorId"": ""ctor-derived-prefixed"",
                  ""args"": [
                    {
                      ""name"": ""Level"",
                      ""valuePointer"": {
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
                  ],
                  ""fields"": []
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-derived""
        }
      },
      ""expectedFields"": {
        ""Prefix"": ""pre"",
        ""Tag"": ""base:pre"",
        ""Note"": ""level:base:pre""
      }
    },
    {
      ""name"": ""an explicit null in the call-site block assigns null to an optional member"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-nullable""
                  },
                  ""constructorId"": null,
                  ""args"": [],
                  ""fields"": [
                    {
                      ""schemaKey"": ""Maybe"",
                      ""memberId"": ""member-maybe"",
                      ""valuePointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": false
                          },
                          ""value"": null
                        }
                      }
                    }
                  ]
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-nullable""
        }
      },
      ""expectedFields"": {
        ""Maybe"": null,
        ""Kept"": ""kept""
      }
    },
    {
      ""name"": ""a required constructor runs the base clause, then its init body"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-latch""
                  },
                  ""constructorId"": ""ctor-latch"",
                  ""args"": [
                    {
                      ""name"": ""Key"",
                      ""valuePointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""K""
                        }
                      }
                    }
                  ],
                  ""fields"": []
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-latch""
        }
      },
      ""expectedFields"": {
        ""Mark"": ""gate:K"",
        ""Seal"": ""seal-base-clause"",
        ""Note"": ""init:gate:K""
      }
    },
    {
      ""name"": ""the call-site block beats the base clause block on an inherited member"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-latch""
                  },
                  ""constructorId"": ""ctor-latch"",
                  ""args"": [
                    {
                      ""name"": ""Key"",
                      ""valuePointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""K""
                        }
                      }
                    }
                  ],
                  ""fields"": [
                    {
                      ""schemaKey"": ""Seal"",
                      ""memberId"": ""member-gate-seal"",
                      ""valuePointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 3,
                            ""required"": true
                          },
                          ""value"": ""seal-call-site""
                        }
                      }
                    }
                  ]
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-latch""
        }
      },
      ""expectedFields"": {
        ""Mark"": ""gate:K"",
        ""Seal"": ""seal-call-site"",
        ""Note"": ""init:gate:K""
      }
    },
    {
      ""name"": ""the implicit new is rejected on a class with a required constructor"",
      ""getter"": {
        ""compilerRevision"": 3,
        ""parameters"": [
          {
            ""id"": ""__this__"",
            ""typeInfo"": {
              ""type"": 0,
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
          },
          {
            ""id"": ""__root__"",
            ""typeInfo"": {
              ""type"": 0,
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
        ""instructions"": [
          {
            ""type"": ""return"",
            ""pointer"": {
              ""type"": ""function"",
              ""function"": {
                ""type"": ""declaredConstructor"",
                ""info"": {
                  ""schemaClassInfo"": {
                    ""type"": 7,
                    ""required"": true,
                    ""classId"": ""class-latch""
                  },
                  ""constructorId"": null,
                  ""args"": [],
                  ""fields"": []
                }
              }
            }
          }
        ],
        ""typeInfo"": {
          ""type"": 7,
          ""required"": true,
          ""classId"": ""class-latch""
        }
      },
      ""expectedErrorContains"": ""declares a required constructor""
    }
  ]
}";
    }
}
