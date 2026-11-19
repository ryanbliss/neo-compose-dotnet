// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

namespace NeoCompose.Tests
{
    internal static class NeoScriptSequenceParityFixture
    {
        internal const string Json = @"{
  ""source"": ""interface ICapability { int Count { get; } }\nclass Probe {\n public bool CapabilityGuard<T>(List<T> values) => values[0] is ICapability;\n public IEnumerable<int> Powers(int count) { int value = 1; foreach (var i in 0..count) { yield return value; value *= 2; } }\n public int Sum(IEnumerable<int> values) { int sum = 0; foreach (var value in values) sum += value; return sum; }\n public IEnumerator<int> Cleanup() { try { yield return 1; yield return 2; } finally { throw \""cleanup\""; } }\n public int Unstarted(IEnumerator<int> cursor) { cursor.Dispose(); cursor.Dispose(); return 7; }\n public int Started(IEnumerator<int> cursor) { cursor.MoveNext(); cursor.Dispose(); return 0; }\n public List<int> Collect(IEnumerable<int> values) => values;\n public List<int> Remaining(IEnumerator<int> values) => values;\n public IEnumerator<int> Cursor(IEnumerable<int> values) => values;\n public IEnumerable<int> Range(int start, int end) => start..end;\n public IEnumerable<int> Flow() { int i = 0; while (i < 5) { i++; if (i == 2) continue; switch (i) { case 3: yield return 30; break; default: yield return i; break; } if (i == 4) break; } yield break; }\n public int DirectRange(int start, int end) { int sum = 0; foreach (var value in start..end) sum += value; return sum; }\n \n public IEnumerable<int> CleanupOrder(Dictionary<string, int> log) { try { try { yield return 1; yield return 2; } finally { log[\""value\""] = log[\""value\""] * 10 + 2; } } finally { log[\""value\""] = log[\""value\""] * 10 + 3; } }\n public int First(IEnumerable<int> values) { foreach (var value in values) return value; return -1; }\n public int Break(IEnumerable<int> values) { foreach (var value in values) break; return 0; }\n public IEnumerable<int> Nested(IEnumerable<int> values) { foreach (var value in values) foreach (var offset in 0..2) yield return value * 10 + offset; }\n public IEnumerable<T> One<T>(T value) { yield return value; }\n public int Boundaries(IEnumerable<int> source) { List<int> local = source; local = source; List<List<int>> lists = [source]; lists.Add(source); Dictionary<string, List<int>> dict = {\""a\"": source}; dict[\""b\""] = source; (List<int> Values, int Tag) pair = (source, 1); return local.Count() + lists[0].Count() + lists[1].Count() + dict[\""a\""].Count() + dict[\""b\""].Count() + pair.Values.Count(); }\n public IEnumerable<int> Coalesce(IEnumerable<int>? left, IEnumerable<int> right) { var value = left ?? right; return value; }\n public List<int> CoalesceList(IEnumerable<int>? left, IEnumerable<int> right) => left ?? right;\n public IEnumerator<int> CoalesceCursor(IEnumerable<int>? left, IEnumerable<int> right) => left ?? right;\n \n public IEnumerable<int> InterfaceDefault() => 0..2;\n public List<IEnumerable<int>> ListDefault() => [0..2];\n public Dictionary<string, IEnumerable<int>> DictionaryDefault() => {\""a\"": 0..2};\n \n public IEnumerable<int> Graph(int count) { List<int> values = List.Repeat(1, count); foreach (var value in values) yield return value; }\n public IEnumerable<int> Growing(int count) { List<List<int>> graph = []; foreach (var i in 0..count) { graph.Add([i]); yield return i; } yield return graph.Last().First(); }\n  public bool Inspect(List<int> values) { IEnumerable<int> alias = values; return values is IEnumerable<int> && alias is IEnumerable<int>; }\n public bool InspectGeneric<T>(T values) => values is IEnumerable<int>;\n public int InspectNested(List<List<int>> values) => values.Where((v) => v is IEnumerable<int>).Count();\n public IEnumerable<T> Relay<T>(List<T> values) { foreach (var value in values) yield return value; }\n public bool EmptyAliases() { List<int> first = [1].Take(0); List<int> second = [1].Skip(1); List<int> third = 0..0; IEnumerable<int> a = first; IEnumerable<int> b = second; IEnumerable<int> c = third; return first == a && second == b && third == c; }\n\n public bool InspectInner<T>(List<T> values) => values[0] is IEnumerable<int>;\n public bool InspectTuple<T>(List<T> values) { (T Value, int Tag) pair = (values[0], 0); return pair.Value is IEnumerable<int>; }\n public bool InspectSwitch<T>(List<T> values) => values[0] switch { IEnumerable<int> sequence => true, _ => false };\n\n public bool WidenedMerges(bool chooseLeft, int key) {\n   IEnumerable<int>? absent = null;\n   IEnumerable<int> present = 4..6;\n   List<int> values = [1, 2];\n   var coalesced = absent ?? values;\n   var conditional = chooseLeft ? present : values;\n   var switched = key switch { 1 => present, _ => values };\n   IEnumerable<int> assigned = absent ?? [3];\n   return coalesced is IEnumerable<int> && conditional is IEnumerable<int> && switched is IEnumerable<int> && assigned is IEnumerable<int>;\n }\n public IEnumerable<int> WidenedReturn(IEnumerable<int>? absent, List<int> values) => absent ?? values;\n public bool WidenedAlias() {\n   List<int> values = [1]; IEnumerable<int> alias = values;\n   values.Add(2); values.Add(3);\n   if (!(alias is IEnumerable<int>)) throw \""List alias lost collection type\"";\n   List<int> collected = alias;\n   return collected.Count() == 3;\n }\n public bool WidenedTuple() {\n   List<int> values = [1];\n   var source = (A: 1, B: (Values: values, Tag: 2));\n   (int A, (IEnumerable<int> Values, int Tag) B) destination = source;\n   values.Add(2);\n   List<int> alias = destination.B.Values;\n   if (!(destination.B.Values is IEnumerable<int>)) throw \""Widened tuple lost collection type\""; if (alias.Count() != 2) throw \""Widened tuple lost list mutation\""; return true;\n }\n public bool LocalSequenceSlots() {\n   List<int> values = [1];\n   List<IEnumerable<int>> lists = [values]; lists[0] = values;\n   Dictionary<string, IEnumerable<int>> map = {}; map[\""a\""] = values;\n   return lists[0] is IEnumerable<int> && map[\""a\""] is IEnumerable<int>;\n }\n\n public bool TypedBindings() {\n   List<List<int>> nested = [[1], [2]];\n   int count = 0;\n   foreach (IEnumerable<int> item in nested) { if (!(item is IEnumerable<int>)) return false; count++; }\n   Dictionary<string, List<int>> map = {\""a\"": [3]};\n   foreach (IEnumerable<int> item in map.Values) { if (!(item is IEnumerable<int>)) return false; count++; }\n   foreach ((string key, IEnumerable<int> item) in map) { if (!(item is IEnumerable<int>)) return false; count++; }\n   List<(int A, List<int> B)> tuples = [(1, [4])];\n   foreach ((int A, IEnumerable<int> B) pair in tuples) { if (!(pair.B is IEnumerable<int>)) return false; count++; }\n   return count == 5 && nested.Where((IEnumerable<int> item) => item is IEnumerable<int>).Count() == 2\n     && nested.Select((IEnumerable<int> item) => item is IEnumerable<int>).All((item) => item)\n     && tuples.Where(((int A, IEnumerable<int> B) pair) => pair.B is IEnumerable<int>).Count() == 1;\n }\n public IEnumerable<List<int>> ListGenerator() { yield return [1]; yield return [2]; }\n public IEnumerable<int> TypedYield(IEnumerable<List<int>> values) { foreach (IEnumerable<int> item in values) yield return item is IEnumerable<int> ? 1 : 0; }\n public bool TypedSequenceLoop(IEnumerable<List<int>> values) {\n   int count = 0;\n   foreach (IEnumerable<int> item in values) { if (!(item is IEnumerable<int>)) return false; count++; }\n   return count == 2;\n }\n public int ConstraintSum<T extends IEnumerable<int>>(T values) { int sum = 0; foreach (var item in values) sum += item; return sum; }\n\n public List<int> QueryZip(IEnumerable<int> left, IEnumerable<int> right) => left.Zip(right, (a, b) => a + b);\n public List<int> QueryConcat(IEnumerable<int> left, IEnumerable<int> right) => left.Concat(right);\n public List<int> QueryFlatten(IEnumerable<IEnumerable<int>> sources) => sources.SelectMany((source) => source);\n public bool QueryAny(IEnumerable<int> source) => source.Any((n) => n > 0);\n public int QueryCount(IEnumerable<int> source) => source.Count();\n public int QueryFirst(IEnumerable<int> source) => source.First();\n public int QueryConstrained<T extends IEnumerable<int>>(T source) => source.Sum();\n public int QueryDictionary(Dictionary<string, int> source) => source.Where((key, value) => key == \""a\"").Sum((key, value) => value);\n public int QueryDictionarySequence(Dictionary<string, int> source) { IEnumerable<int> values = source; return values.Where((n) => n > 0).Sum(); }\n public int QueryList(List<int> source) => source.Where((n) => n > 1).Sum();\n public IEnumerable<int> QueryShortCircuit() { yield return 1; throw \""resumed\""; }\n public IEnumerable<int> QueryCleanup() { try { yield return 1; yield return 2; } finally { throw \""query cleanup\""; } }\n public List<int> QueryOrdered(IEnumerable<int> source) => source.OrderBy((n) => n % 2).ThenByDescending((n) => n);\n public List<int> QueryMutation() { List<int> items = [1, 2, 3]; return items.Select((item) => { items.Remove(item); return item; }); }\n}\n"",
  ""bodies"": {
    ""CapabilityGuard"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.CapabilityGuard"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 6,
            ""required"": true,
            ""entryTypeInfo"": {
              ""type"": 21,
              ""required"": true,
              ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:CapabilityGuard"",
              ""index"": 0
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 21,
                  ""required"": true,
                  ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:CapabilityGuard"",
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
            ""type"": ""isCheck"",
            ""pointer"": {
              ""type"": ""keyOf"",
              ""keyOf"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""__arg_0__""
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
            ""checkType"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""pending:interface:Probe.neo:ICapability""
            }
          },
          ""source"": {
            ""line"": 23,
            ""column"": 52
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
          ""pending:class:Probe.neo:Probe"",
          ""pending:interface:Probe.neo:ICapability""
        ],
        ""typeNames"": [""ICapability""]
      }
    },
    ""Powers"": {
      ""compilerRevision"": 19,
      ""generator"": {
        ""kind"": ""enumerable"",
        ""elementType"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""source"": {
        ""name"": ""Probe.Powers"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""value"",
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
                ""value"": 1
              }
            }
          },
          ""source"": {
            ""line"": 24,
            ""column"": 46
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""$rangeStart0"",
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
            ""line"": 24,
            ""column"": 61
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""$rangeEnd0"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""__arg_0__""
            }
          },
          ""source"": {
            ""line"": 24,
            ""column"": 61
          }
        },
        {
          ""type"": ""for"",
          ""rangeEndId"": ""$rangeEnd0"",
          ""initializer"": {
            ""id"": ""$rangeIndex0"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""$rangeStart0""
            }
          },
          ""condition"": {
            ""condition"": {
              ""type"": ""lessThan"",
              ""operand1"": {
                ""type"": ""variable"",
                ""variableId"": ""$rangeIndex0""
              },
              ""operand2"": {
                ""type"": ""variable"",
                ""variableId"": ""$rangeEnd0""
              }
            }
          },
          ""iterator"": {
            ""type"": ""assign"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""$rangeIndex0""
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
                      ""type"": ""variable"",
                      ""variableId"": ""$rangeIndex0""
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
          },
          ""instructions"": [
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""i"",
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""$rangeIndex0""
                }
              },
              ""source"": {
                ""line"": 24,
                ""column"": 61
              }
            },
            {
              ""type"": ""yieldReturn"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""value""
              },
              ""yields"": true,
              ""source"": {
                ""line"": 24,
                ""column"": 91
              }
            },
            {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""value""
                },
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""writability"": ""local""
              },
              ""operator"": ""*="",
              ""pointer"": {
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""*"",
                    ""pointers"": [
                      {
                        ""type"": ""variable"",
                        ""variableId"": ""value""
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
                ""line"": 24,
                ""column"": 111
              }
            }
          ],
          ""yields"": true,
          ""source"": {
            ""line"": 24,
            ""column"": 61
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""Sum"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.Sum"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
            ""line"": 25,
            ""column"": 44
          }
        },
        {
          ""type"": ""forEach"",
          ""binding"": {
            ""id"": ""value"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""readonly"": true,
            ""writability"": ""runtime""
          },
          ""collectionPointer"": {
            ""type"": ""variable"",
            ""variableId"": ""__arg_0__""
          },
          ""collectionTypeInfo"": {
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""instructions"": [
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
                        ""type"": ""variable"",
                        ""variableId"": ""value""
                      }
                    ],
                    ""numeric"": ""int""
                  }
                }
              },
              ""source"": {
                ""line"": 25,
                ""column"": 87
              }
            }
          ],
          ""source"": {
            ""line"": 25,
            ""column"": 57
          }
        },
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""variable"",
            ""variableId"": ""sum""
          },
          ""source"": {
            ""line"": 25,
            ""column"": 101
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""Cleanup"": {
      ""compilerRevision"": 19,
      ""generator"": {
        ""kind"": ""enumerator"",
        ""elementType"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""source"": {
        ""name"": ""Probe.Cleanup"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
          ""type"": ""try"",
          ""instructions"": [
            {
              ""type"": ""yieldReturn"",
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
              ""yields"": true,
              ""source"": {
                ""line"": 26,
                ""column"": 44
              }
            },
            {
              ""type"": ""yieldReturn"",
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
              ""yields"": true,
              ""source"": {
                ""line"": 26,
                ""column"": 60
              }
            }
          ],
          ""catches"": [],
          ""finallyInstructions"": [
            {
              ""type"": ""throw"",
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""cleanup""
                }
              },
              ""source"": {
                ""line"": 26,
                ""column"": 88
              }
            }
          ],
          ""yields"": true,
          ""source"": {
            ""line"": 26,
            ""column"": 38
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_1604985c-2d92-4aca-9fcb-16a667aaaf1a"",
        ""typeArguments"": {
          ""system_8c8392fe-41f0-4116-9ac8-e325a187d8aa"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_1604985c-2d92-4aca-9fcb-16a667aaaf1a""
        ],
        ""typeNames"": []
      }
    },
    ""Unstarted"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.Unstarted"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_1604985c-2d92-4aca-9fcb-16a667aaaf1a"",
            ""typeArguments"": {
              ""system_8c8392fe-41f0-4116-9ac8-e325a187d8aa"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_1604985c-2d92-4aca-9fcb-16a667aaaf1a"",
                ""typeArguments"": {
                  ""system_8c8392fe-41f0-4116-9ac8-e325a187d8aa"": {
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
          ""type"": ""functionCall"",
          ""call"": {
            ""type"": ""callFunction"",
            ""memberId"": ""system_9b9cd079-e20d-47e5-8d86-11080b867fe8"",
            ""receiver"": {
              ""kind"": ""instance"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              }
            },
            ""args"": [],
            ""callSiteId"": ""27:64:0"",
            ""source"": {
              ""line"": 27,
              ""column"": 64
            }
          },
          ""source"": {
            ""line"": 27,
            ""column"": 50
          }
        },
        {
          ""type"": ""functionCall"",
          ""call"": {
            ""type"": ""callFunction"",
            ""memberId"": ""system_9b9cd079-e20d-47e5-8d86-11080b867fe8"",
            ""receiver"": {
              ""kind"": ""instance"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              }
            },
            ""args"": [],
            ""callSiteId"": ""27:82:1"",
            ""source"": {
              ""line"": 27,
              ""column"": 82
            }
          },
          ""source"": {
            ""line"": 27,
            ""column"": 68
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
              ""value"": 7
            }
          },
          ""source"": {
            ""line"": 27,
            ""column"": 86
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
          ""pending:class:Probe.neo:Probe"",
          ""system_1604985c-2d92-4aca-9fcb-16a667aaaf1a"",
          ""system_9b9cd079-e20d-47e5-8d86-11080b867fe8""
        ],
        ""typeNames"": []
      }
    },
    ""Started"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.Started"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_1604985c-2d92-4aca-9fcb-16a667aaaf1a"",
            ""typeArguments"": {
              ""system_8c8392fe-41f0-4116-9ac8-e325a187d8aa"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_1604985c-2d92-4aca-9fcb-16a667aaaf1a"",
                ""typeArguments"": {
                  ""system_8c8392fe-41f0-4116-9ac8-e325a187d8aa"": {
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
          ""type"": ""functionCall"",
          ""call"": {
            ""type"": ""callFunction"",
            ""memberId"": ""system_f0c37057-082c-4a23-8c9f-27ea9a17b5be"",
            ""receiver"": {
              ""kind"": ""instance"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              }
            },
            ""args"": [],
            ""callSiteId"": ""28:63:0"",
            ""source"": {
              ""line"": 28,
              ""column"": 63
            }
          },
          ""source"": {
            ""line"": 28,
            ""column"": 48
          }
        },
        {
          ""type"": ""functionCall"",
          ""call"": {
            ""type"": ""callFunction"",
            ""memberId"": ""system_9b9cd079-e20d-47e5-8d86-11080b867fe8"",
            ""receiver"": {
              ""kind"": ""instance"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              }
            },
            ""args"": [],
            ""callSiteId"": ""28:81:1"",
            ""source"": {
              ""line"": 28,
              ""column"": 81
            }
          },
          ""source"": {
            ""line"": 28,
            ""column"": 67
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
            ""line"": 28,
            ""column"": 85
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
          ""pending:class:Probe.neo:Probe"",
          ""system_1604985c-2d92-4aca-9fcb-16a667aaaf1a"",
          ""system_9b9cd079-e20d-47e5-8d86-11080b867fe8"",
          ""system_f0c37057-082c-4a23-8c9f-27ea9a17b5be""
        ],
        ""typeNames"": []
      }
    },
    ""Collect"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.Collect"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
            ""type"": ""sequenceConversion"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""__arg_0__""
            },
            ""operation"": ""collectEnumerable"",
            ""elementType"": {
              ""type"": 2,
              ""required"": true
            }
          },
          ""source"": {
            ""line"": 29,
            ""column"": 55
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
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""Remaining"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.Remaining"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_1604985c-2d92-4aca-9fcb-16a667aaaf1a"",
            ""typeArguments"": {
              ""system_8c8392fe-41f0-4116-9ac8-e325a187d8aa"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_1604985c-2d92-4aca-9fcb-16a667aaaf1a"",
                ""typeArguments"": {
                  ""system_8c8392fe-41f0-4116-9ac8-e325a187d8aa"": {
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
            ""type"": ""sequenceConversion"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""__arg_0__""
            },
            ""operation"": ""collectEnumerator"",
            ""elementType"": {
              ""type"": 2,
              ""required"": true
            }
          },
          ""source"": {
            ""line"": 30,
            ""column"": 57
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
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_1604985c-2d92-4aca-9fcb-16a667aaaf1a""
        ],
        ""typeNames"": []
      }
    },
    ""Cursor"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.Cursor"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
            ""type"": ""sequenceConversion"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""__arg_0__""
            },
            ""operation"": ""getEnumerator"",
            ""elementType"": {
              ""type"": 2,
              ""required"": true
            }
          },
          ""source"": {
            ""line"": 31,
            ""column"": 61
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_1604985c-2d92-4aca-9fcb-16a667aaaf1a"",
        ""typeArguments"": {
          ""system_8c8392fe-41f0-4116-9ac8-e325a187d8aa"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_1604985c-2d92-4aca-9fcb-16a667aaaf1a"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""Range"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.Range"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
          ""id"": ""__arg_1__"",
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
            ""type"": ""range"",
            ""start"": {
              ""type"": ""variable"",
              ""variableId"": ""__arg_0__""
            },
            ""end"": {
              ""type"": ""variable"",
              ""variableId"": ""__arg_1__""
            }
          },
          ""source"": {
            ""line"": 32,
            ""column"": 55
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""Flow"": {
      ""compilerRevision"": 19,
      ""generator"": {
        ""kind"": ""enumerable"",
        ""elementType"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""source"": {
        ""name"": ""Probe.Flow"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""id"": ""i"",
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
            ""line"": 33,
            ""column"": 35
          }
        },
        {
          ""type"": ""while"",
          ""condition"": {
            ""condition"": {
              ""type"": ""lessThan"",
              ""operand1"": {
                ""type"": ""variable"",
                ""variableId"": ""i""
              },
              ""operand2"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 5
                }
              }
            }
          },
          ""instructions"": [
            {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""i""
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
                        ""type"": ""variable"",
                        ""variableId"": ""i""
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
                ""line"": 33,
                ""column"": 62
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
                        ""type"": ""variable"",
                        ""variableId"": ""i""
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
                  },
                  ""instructions"": [
                    {
                      ""type"": ""continue"",
                      ""source"": {
                        ""line"": 33,
                        ""column"": 79
                      }
                    }
                  ]
                }
              ],
              ""else"": null,
              ""source"": {
                ""line"": 33,
                ""column"": 67
              }
            },
            {
              ""type"": ""switch"",
              ""selector"": {
                ""type"": ""variable"",
                ""variableId"": ""i""
              },
              ""selectorTypeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""sections"": [
                {
                  ""labels"": [
                    {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""value"": 3
                    }
                  ],
                  ""instructions"": [
                    {
                      ""type"": ""yieldReturn"",
                      ""pointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 30
                        }
                      },
                      ""yields"": true,
                      ""source"": {
                        ""line"": 33,
                        ""column"": 110
                      }
                    },
                    {
                      ""type"": ""break"",
                      ""source"": {
                        ""line"": 33,
                        ""column"": 127
                      }
                    }
                  ]
                }
              ],
              ""defaultInstructions"": [
                {
                  ""type"": ""yieldReturn"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""i""
                  },
                  ""yields"": true,
                  ""source"": {
                    ""line"": 33,
                    ""column"": 143
                  }
                },
                {
                  ""type"": ""break"",
                  ""source"": {
                    ""line"": 33,
                    ""column"": 159
                  }
                }
              ],
              ""yields"": true,
              ""source"": {
                ""line"": 33,
                ""column"": 89
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
                        ""type"": ""variable"",
                        ""variableId"": ""i""
                      },
                      ""operand2"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""value"": 4
                        }
                      }
                    }
                  },
                  ""instructions"": [
                    {
                      ""type"": ""break"",
                      ""source"": {
                        ""line"": 33,
                        ""column"": 180
                      }
                    }
                  ]
                }
              ],
              ""else"": null,
              ""source"": {
                ""line"": 33,
                ""column"": 168
              }
            }
          ],
          ""yields"": true,
          ""source"": {
            ""line"": 33,
            ""column"": 46
          }
        },
        {
          ""type"": ""yieldBreak"",
          ""yields"": true,
          ""source"": {
            ""line"": 33,
            ""column"": 189
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""DirectRange"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.DirectRange"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
          ""id"": ""__arg_1__"",
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
            ""line"": 34,
            ""column"": 47
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""$rangeStart0"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""__arg_0__""
            }
          },
          ""source"": {
            ""line"": 34,
            ""column"": 60
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""$rangeEnd0"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""__arg_1__""
            }
          },
          ""source"": {
            ""line"": 34,
            ""column"": 60
          }
        },
        {
          ""type"": ""for"",
          ""rangeEndId"": ""$rangeEnd0"",
          ""initializer"": {
            ""id"": ""$rangeIndex0"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""$rangeStart0""
            }
          },
          ""condition"": {
            ""condition"": {
              ""type"": ""lessThan"",
              ""operand1"": {
                ""type"": ""variable"",
                ""variableId"": ""$rangeIndex0""
              },
              ""operand2"": {
                ""type"": ""variable"",
                ""variableId"": ""$rangeEnd0""
              }
            }
          },
          ""iterator"": {
            ""type"": ""assign"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""$rangeIndex0""
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
                      ""type"": ""variable"",
                      ""variableId"": ""$rangeIndex0""
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
          },
          ""instructions"": [
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""value"",
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""$rangeIndex0""
                }
              },
              ""source"": {
                ""line"": 34,
                ""column"": 60
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
                        ""type"": ""variable"",
                        ""variableId"": ""value""
                      }
                    ],
                    ""numeric"": ""int""
                  }
                }
              },
              ""source"": {
                ""line"": 34,
                ""column"": 94
              }
            }
          ],
          ""source"": {
            ""line"": 34,
            ""column"": 60
          }
        },
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""variable"",
            ""variableId"": ""sum""
          },
          ""source"": {
            ""line"": 34,
            ""column"": 108
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [""builtin:Root"", ""pending:class:Probe.neo:Probe""],
        ""typeNames"": []
      }
    },
    ""CleanupOrder"": {
      ""compilerRevision"": 19,
      ""generator"": {
        ""kind"": ""enumerable"",
        ""elementType"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""source"": {
        ""name"": ""Probe.CleanupOrder"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 5,
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
                ""type"": 5,
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
          ""type"": ""try"",
          ""instructions"": [
            {
              ""type"": ""try"",
              ""instructions"": [
                {
                  ""type"": ""yieldReturn"",
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
                  ""yields"": true,
                  ""source"": {
                    ""line"": 36,
                    ""column"": 82
                  }
                },
                {
                  ""type"": ""yieldReturn"",
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
                  ""yields"": true,
                  ""source"": {
                    ""line"": 36,
                    ""column"": 98
                  }
                }
              ],
              ""catches"": [],
              ""finallyInstructions"": [
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
                        ""variableId"": ""__arg_0__""
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
                          ""type"": ""value"",
                          ""value"": {
                            ""typeInfo"": {
                              ""type"": 3,
                              ""required"": true
                            },
                            ""value"": ""value""
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
                    ""type"": ""operation"",
                    ""operation"": {
                      ""type"": ""arithmetic"",
                      ""arithmetic"": {
                        ""type"": ""+"",
                        ""pointers"": [
                          {
                            ""type"": ""operation"",
                            ""operation"": {
                              ""type"": ""arithmetic"",
                              ""arithmetic"": {
                                ""type"": ""*"",
                                ""pointers"": [
                                  {
                                    ""type"": ""keyOf"",
                                    ""keyOf"": {
                                      ""pointer"": {
                                        ""type"": ""variable"",
                                        ""variableId"": ""__arg_0__""
                                      },
                                      ""key"": {
                                        ""type"": ""value"",
                                        ""value"": {
                                          ""typeInfo"": {
                                            ""type"": 3,
                                            ""required"": true
                                          },
                                          ""value"": ""value""
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
                                      ""value"": 10
                                    }
                                  }
                                ],
                                ""numeric"": ""int""
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
                        ],
                        ""numeric"": ""int""
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 36,
                    ""column"": 126
                  }
                }
              ],
              ""yields"": true,
              ""source"": {
                ""line"": 36,
                ""column"": 76
              }
            }
          ],
          ""catches"": [],
          ""finallyInstructions"": [
            {
              ""type"": ""assign"",
              ""bindings"": [
                {
                  ""id"": ""$assignment_1"",
                  ""typeInfo"": {
                    ""type"": ""Unknown"",
                    ""required"": true
                  },
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_0__""
                  }
                }
              ],
              ""target"": {
                ""pointer"": {
                  ""type"": ""keyOf"",
                  ""keyOf"": {
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""$assignment_1""
                    },
                    ""key"": {
                      ""type"": ""value"",
                      ""value"": {
                        ""typeInfo"": {
                          ""type"": 3,
                          ""required"": true
                        },
                        ""value"": ""value""
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
                ""type"": ""operation"",
                ""operation"": {
                  ""type"": ""arithmetic"",
                  ""arithmetic"": {
                    ""type"": ""+"",
                    ""pointers"": [
                      {
                        ""type"": ""operation"",
                        ""operation"": {
                          ""type"": ""arithmetic"",
                          ""arithmetic"": {
                            ""type"": ""*"",
                            ""pointers"": [
                              {
                                ""type"": ""keyOf"",
                                ""keyOf"": {
                                  ""pointer"": {
                                    ""type"": ""variable"",
                                    ""variableId"": ""__arg_0__""
                                  },
                                  ""key"": {
                                    ""type"": ""value"",
                                    ""value"": {
                                      ""typeInfo"": {
                                        ""type"": 3,
                                        ""required"": true
                                      },
                                      ""value"": ""value""
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
                                  ""value"": 10
                                }
                              }
                            ],
                            ""numeric"": ""int""
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
                          ""value"": 3
                        }
                      }
                    ],
                    ""numeric"": ""int""
                  }
                }
              },
              ""source"": {
                ""line"": 36,
                ""column"": 178
              }
            }
          ],
          ""yields"": true,
          ""source"": {
            ""line"": 36,
            ""column"": 70
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""First"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.First"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
          ""type"": ""forEach"",
          ""binding"": {
            ""id"": ""value"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""readonly"": true,
            ""writability"": ""runtime""
          },
          ""collectionPointer"": {
            ""type"": ""variable"",
            ""variableId"": ""__arg_0__""
          },
          ""collectionTypeInfo"": {
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""instructions"": [
            {
              ""type"": ""return"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""value""
              },
              ""source"": {
                ""line"": 37,
                ""column"": 76
              }
            }
          ],
          ""source"": {
            ""line"": 37,
            ""column"": 46
          }
        },
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
            ""line"": 37,
            ""column"": 90
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""Break"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.Break"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
          ""type"": ""forEach"",
          ""binding"": {
            ""id"": ""value"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""readonly"": true,
            ""writability"": ""runtime""
          },
          ""collectionPointer"": {
            ""type"": ""variable"",
            ""variableId"": ""__arg_0__""
          },
          ""collectionTypeInfo"": {
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""instructions"": [
            {
              ""type"": ""break"",
              ""source"": {
                ""line"": 38,
                ""column"": 76
              }
            }
          ],
          ""source"": {
            ""line"": 38,
            ""column"": 46
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
            ""line"": 38,
            ""column"": 83
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""Nested"": {
      ""compilerRevision"": 19,
      ""generator"": {
        ""kind"": ""enumerable"",
        ""elementType"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""source"": {
        ""name"": ""Probe.Nested"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
          ""type"": ""forEach"",
          ""binding"": {
            ""id"": ""value"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""readonly"": true,
            ""writability"": ""runtime""
          },
          ""collectionPointer"": {
            ""type"": ""variable"",
            ""variableId"": ""__arg_0__""
          },
          ""collectionTypeInfo"": {
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""instructions"": [
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""$rangeStart0"",
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
                ""line"": 39,
                ""column"": 90
              }
            },
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""$rangeEnd0"",
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
                    ""value"": 2
                  }
                }
              },
              ""source"": {
                ""line"": 39,
                ""column"": 90
              }
            },
            {
              ""type"": ""for"",
              ""rangeEndId"": ""$rangeEnd0"",
              ""initializer"": {
                ""id"": ""$rangeIndex0"",
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""$rangeStart0""
                }
              },
              ""condition"": {
                ""condition"": {
                  ""type"": ""lessThan"",
                  ""operand1"": {
                    ""type"": ""variable"",
                    ""variableId"": ""$rangeIndex0""
                  },
                  ""operand2"": {
                    ""type"": ""variable"",
                    ""variableId"": ""$rangeEnd0""
                  }
                }
              },
              ""iterator"": {
                ""type"": ""assign"",
                ""target"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""$rangeIndex0""
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
                          ""type"": ""variable"",
                          ""variableId"": ""$rangeIndex0""
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
              },
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
                      ""type"": ""variable"",
                      ""variableId"": ""$rangeIndex0""
                    }
                  },
                  ""source"": {
                    ""line"": 39,
                    ""column"": 90
                  }
                },
                {
                  ""type"": ""yieldReturn"",
                  ""pointer"": {
                    ""type"": ""operation"",
                    ""operation"": {
                      ""type"": ""arithmetic"",
                      ""arithmetic"": {
                        ""type"": ""+"",
                        ""pointers"": [
                          {
                            ""type"": ""operation"",
                            ""operation"": {
                              ""type"": ""arithmetic"",
                              ""arithmetic"": {
                                ""type"": ""*"",
                                ""pointers"": [
                                  {
                                    ""type"": ""variable"",
                                    ""variableId"": ""value""
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
                          {
                            ""type"": ""variable"",
                            ""variableId"": ""offset""
                          }
                        ],
                        ""numeric"": ""int""
                      }
                    }
                  },
                  ""yields"": true,
                  ""source"": {
                    ""line"": 39,
                    ""column"": 119
                  }
                }
              ],
              ""yields"": true,
              ""source"": {
                ""line"": 39,
                ""column"": 90
              }
            }
          ],
          ""yields"": true,
          ""source"": {
            ""line"": 39,
            ""column"": 60
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""One"": {
      ""compilerRevision"": 19,
      ""generator"": {
        ""kind"": ""enumerable"",
        ""elementType"": {
          ""type"": 21,
          ""required"": true,
          ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:One"",
          ""index"": 0
        }
      },
      ""source"": {
        ""name"": ""Probe.One"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:One"",
            ""index"": 0
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:One"",
                ""index"": 0
              },
              ""value"": null
            }
          }
        }
      ],
      ""instructions"": [
        {
          ""type"": ""yieldReturn"",
          ""pointer"": {
            ""type"": ""variable"",
            ""variableId"": ""__arg_0__""
          },
          ""yields"": true,
          ""source"": {
            ""line"": 40,
            ""column"": 42
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 21,
            ""required"": true,
            ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:One"",
            ""index"": 0
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""Boundaries"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.Boundaries"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""local"",
            ""typeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""pointer"": {
              ""type"": ""sequenceConversion"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              },
              ""operation"": ""collectEnumerable"",
              ""elementType"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""source"": {
            ""line"": 41,
            ""column"": 51
          }
        },
        {
          ""type"": ""assign"",
          ""target"": {
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""local""
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
            ""type"": ""sequenceConversion"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""__arg_0__""
            },
            ""operation"": ""collectEnumerable"",
            ""elementType"": {
              ""type"": 2,
              ""required"": true
            }
          },
          ""source"": {
            ""line"": 41,
            ""column"": 77
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""lists"",
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
                  ""type"": ""sequenceConversion"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_0__""
                  },
                  ""operation"": ""collectEnumerable"",
                  ""elementType"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              ]
            }
          },
          ""source"": {
            ""line"": 41,
            ""column"": 93
          }
        },
        {
          ""type"": ""collectionCall"",
          ""target"": {
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""lists""
            },
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
            ""writability"": ""local""
          },
          ""mutation"": ""Add"",
          ""args"": [
            {
              ""type"": ""sequenceConversion"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              },
              ""operation"": ""collectEnumerable"",
              ""elementType"": {
                ""type"": 2,
                ""required"": true
              }
            }
          ],
          ""reference"": true,
          ""source"": {
            ""line"": 41,
            ""column"": 127
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
                      ""value"": ""a""
                    }
                  },
                  ""value"": {
                    ""type"": ""sequenceConversion"",
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""__arg_0__""
                    },
                    ""operation"": ""collectEnumerable"",
                    ""elementType"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                }
              ]
            }
          },
          ""source"": {
            ""line"": 41,
            ""column"": 146
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
                  ""variableId"": ""$assignment_0""
                },
                ""key"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""b""
                  }
                }
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
          ""operator"": ""="",
          ""pointer"": {
            ""type"": ""sequenceConversion"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""__arg_0__""
            },
            ""operation"": ""collectEnumerable"",
            ""elementType"": {
              ""type"": 2,
              ""required"": true
            }
          },
          ""source"": {
            ""line"": 41,
            ""column"": 198
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
                  },
                  ""name"": ""Values""
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""name"": ""Tag""
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
                      ""type"": ""sequenceConversion"",
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""__arg_0__""
                      },
                      ""operation"": ""collectEnumerable"",
                      ""elementType"": {
                        ""type"": 2,
                        ""required"": true
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
                          ""type"": 6,
                          ""required"": true,
                          ""entryTypeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        },
                        ""name"": ""Values""
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""name"": ""Tag""
                      }
                    ]
                  }
                }
              },
              ""source"": {
                ""line"": 41,
                ""column"": 253
              }
            }
          },
          ""source"": {
            ""line"": 41,
            ""column"": 218
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
                    ""type"": ""operation"",
                    ""operation"": {
                      ""type"": ""arithmetic"",
                      ""arithmetic"": {
                        ""type"": ""+"",
                        ""pointers"": [
                          {
                            ""type"": ""operation"",
                            ""operation"": {
                              ""type"": ""arithmetic"",
                              ""arithmetic"": {
                                ""type"": ""+"",
                                ""pointers"": [
                                  {
                                    ""type"": ""operation"",
                                    ""operation"": {
                                      ""type"": ""arithmetic"",
                                      ""arithmetic"": {
                                        ""type"": ""+"",
                                        ""pointers"": [
                                          {
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
                                                          ""variableId"": ""local""
                                                        }
                                                      }
                                                    },
                                                    ""source"": {
                                                      ""line"": 41,
                                                      ""column"": 284
                                                    }
                                                  },
                                                  {
                                                    ""type"": ""function"",
                                                    ""function"": {
                                                      ""type"": ""count"",
                                                      ""info"": {
                                                        ""collectionPointer"": {
                                                          ""type"": ""keyOf"",
                                                          ""keyOf"": {
                                                            ""pointer"": {
                                                              ""type"": ""variable"",
                                                              ""variableId"": ""lists""
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
                                                      }
                                                    },
                                                    ""source"": {
                                                      ""line"": 41,
                                                      ""column"": 303
                                                    }
                                                  }
                                                ],
                                                ""numeric"": ""int""
                                              }
                                            }
                                          },
                                          {
                                            ""type"": ""function"",
                                            ""function"": {
                                              ""type"": ""count"",
                                              ""info"": {
                                                ""collectionPointer"": {
                                                  ""type"": ""keyOf"",
                                                  ""keyOf"": {
                                                    ""pointer"": {
                                                      ""type"": ""variable"",
                                                      ""variableId"": ""lists""
                                                    },
                                                    ""key"": {
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
                                            ""source"": {
                                              ""line"": 41,
                                              ""column"": 322
                                            }
                                          }
                                        ],
                                        ""numeric"": ""int""
                                      }
                                    }
                                  },
                                  {
                                    ""type"": ""function"",
                                    ""function"": {
                                      ""type"": ""count"",
                                      ""info"": {
                                        ""collectionPointer"": {
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
                                                ""value"": ""a""
                                              }
                                            }
                                          }
                                        }
                                      }
                                    },
                                    ""source"": {
                                      ""line"": 41,
                                      ""column"": 342
                                    }
                                  }
                                ],
                                ""numeric"": ""int""
                              }
                            }
                          },
                          {
                            ""type"": ""function"",
                            ""function"": {
                              ""type"": ""count"",
                              ""info"": {
                                ""collectionPointer"": {
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
                                        ""value"": ""b""
                                      }
                                    }
                                  }
                                }
                              }
                            },
                            ""source"": {
                              ""line"": 41,
                              ""column"": 362
                            }
                          }
                        ],
                        ""numeric"": ""int""
                      }
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
                            ""line"": 41,
                            ""column"": 371
                          }
                        }
                      }
                    },
                    ""source"": {
                      ""line"": 41,
                      ""column"": 384
                    }
                  }
                ],
                ""numeric"": ""int""
              }
            }
          },
          ""source"": {
            ""line"": 41,
            ""column"": 266
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""Coalesce"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.Coalesce"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": false,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": false,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__arg_1__"",
          ""typeInfo"": {
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""value"",
            ""typeInfo"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            },
            ""pointer"": {
              ""type"": ""coalesce"",
              ""left"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              },
              ""right"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_1__""
              }
            }
          },
          ""source"": {
            ""line"": 42,
            ""column"": 85
          }
        },
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""variable"",
            ""variableId"": ""value""
          },
          ""source"": {
            ""line"": 42,
            ""column"": 112
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""CoalesceList"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.CoalesceList"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": false,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": false,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__arg_1__"",
          ""typeInfo"": {
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
            ""type"": ""sequenceConversion"",
            ""pointer"": {
              ""type"": ""coalesce"",
              ""left"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              },
              ""right"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_1__""
              }
            },
            ""operation"": ""collectEnumerable"",
            ""elementType"": {
              ""type"": 2,
              ""required"": true
            }
          },
          ""source"": {
            ""line"": 43,
            ""column"": 83
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
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""CoalesceCursor"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.CoalesceCursor"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": false,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": false,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__arg_1__"",
          ""typeInfo"": {
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
            ""type"": ""sequenceConversion"",
            ""pointer"": {
              ""type"": ""coalesce"",
              ""left"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              },
              ""right"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_1__""
              }
            },
            ""operation"": ""getEnumerator"",
            ""elementType"": {
              ""type"": 2,
              ""required"": true
            }
          },
          ""source"": {
            ""line"": 44,
            ""column"": 92
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_1604985c-2d92-4aca-9fcb-16a667aaaf1a"",
        ""typeArguments"": {
          ""system_8c8392fe-41f0-4116-9ac8-e325a187d8aa"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_1604985c-2d92-4aca-9fcb-16a667aaaf1a"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""InterfaceDefault"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.InterfaceDefault"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": ""range"",
            ""start"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 0
              }
            },
            ""end"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""value"": 2
              }
            }
          },
          ""source"": {
            ""line"": 46,
            ""column"": 48
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""ListDefault"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.ListDefault"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": ""listLiteral"",
            ""typeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              }
            },
            ""entries"": [
              {
                ""type"": ""range"",
                ""start"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 0
                  }
                },
                ""end"": {
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
          ""source"": {
            ""line"": 47,
            ""column"": 49
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 6,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 22,
          ""required"": true,
          ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
          ""typeArguments"": {
            ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
              ""type"": 2,
              ""required"": true
            }
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""DictionaryDefault"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.DictionaryDefault"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": ""dictLiteral"",
            ""typeInfo"": {
              ""type"": 5,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                    ""type"": 2,
                    ""required"": true
                  }
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
                    ""value"": ""a""
                  }
                },
                ""value"": {
                  ""type"": ""range"",
                  ""start"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""value"": 0
                    }
                  },
                  ""end"": {
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
            ]
          },
          ""source"": {
            ""line"": 48,
            ""column"": 69
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 5,
        ""required"": true,
        ""entryTypeInfo"": {
          ""type"": 22,
          ""required"": true,
          ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
          ""typeArguments"": {
            ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
              ""type"": 2,
              ""required"": true
            }
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""Graph"": {
      ""compilerRevision"": 19,
      ""generator"": {
        ""kind"": ""enumerable"",
        ""elementType"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""source"": {
        ""name"": ""Probe.Graph"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_0__""
                  },
                  ""entryTypeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              },
              ""source"": {
                ""line"": 50,
                ""column"": 75
              }
            }
          },
          ""source"": {
            ""line"": 50,
            ""column"": 45
          }
        },
        {
          ""type"": ""forEach"",
          ""binding"": {
            ""id"": ""value"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""readonly"": true,
            ""writability"": ""local""
          },
          ""collectionPointer"": {
            ""type"": ""variable"",
            ""variableId"": ""values""
          },
          ""collectionTypeInfo"": {
            ""type"": 6,
            ""required"": true,
            ""entryTypeInfo"": {
              ""type"": 2,
              ""required"": true
            }
          },
          ""instructions"": [
            {
              ""type"": ""yieldReturn"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""value""
              },
              ""yields"": true,
              ""source"": {
                ""line"": 50,
                ""column"": 117
              }
            }
          ],
          ""yields"": true,
          ""source"": {
            ""line"": 50,
            ""column"": 87
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""Growing"": {
      ""compilerRevision"": 19,
      ""generator"": {
        ""kind"": ""enumerable"",
        ""elementType"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""source"": {
        ""name"": ""Probe.Growing"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""graph"",
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
              ""entries"": []
            }
          },
          ""source"": {
            ""line"": 51,
            ""column"": 47
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""$rangeStart0"",
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
            ""line"": 51,
            ""column"": 75
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""$rangeEnd0"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""__arg_0__""
            }
          },
          ""source"": {
            ""line"": 51,
            ""column"": 75
          }
        },
        {
          ""type"": ""for"",
          ""rangeEndId"": ""$rangeEnd0"",
          ""initializer"": {
            ""id"": ""$rangeIndex0"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""$rangeStart0""
            }
          },
          ""condition"": {
            ""condition"": {
              ""type"": ""lessThan"",
              ""operand1"": {
                ""type"": ""variable"",
                ""variableId"": ""$rangeIndex0""
              },
              ""operand2"": {
                ""type"": ""variable"",
                ""variableId"": ""$rangeEnd0""
              }
            }
          },
          ""iterator"": {
            ""type"": ""assign"",
            ""target"": {
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""$rangeIndex0""
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
                      ""type"": ""variable"",
                      ""variableId"": ""$rangeIndex0""
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
          },
          ""instructions"": [
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""i"",
                ""typeInfo"": {
                  ""type"": 2,
                  ""required"": true
                },
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""$rangeIndex0""
                }
              },
              ""source"": {
                ""line"": 51,
                ""column"": 75
              }
            },
            {
              ""type"": ""collectionCall"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""graph""
                },
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
                ""writability"": ""local""
              },
              ""mutation"": ""Add"",
              ""args"": [
                {
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
                      ""type"": ""variable"",
                      ""variableId"": ""i""
                    }
                  ]
                }
              ],
              ""reference"": true,
              ""source"": {
                ""line"": 51,
                ""column"": 105
              }
            },
            {
              ""type"": ""yieldReturn"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""i""
              },
              ""yields"": true,
              ""source"": {
                ""line"": 51,
                ""column"": 121
              }
            }
          ],
          ""yields"": true,
          ""source"": {
            ""line"": 51,
            ""column"": 75
          }
        },
        {
          ""type"": ""yieldReturn"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""first"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""queryEnumerable"",
                  ""pointer"": {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""collectionQuery"",
                      ""info"": {
                        ""collectionPointer"": {
                          ""type"": ""sequenceConversion"",
                          ""operation"": ""queryEnumerable"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""graph""
                          },
                          ""elementType"": {
                            ""type"": 6,
                            ""required"": true,
                            ""entryTypeInfo"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        },
                        ""op"": ""Last""
                      }
                    },
                    ""source"": {
                      ""line"": 51,
                      ""column"": 162
                    }
                  },
                  ""elementType"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              }
            },
            ""source"": {
              ""line"": 51,
              ""column"": 170
            }
          },
          ""yields"": true,
          ""source"": {
            ""line"": 51,
            ""column"": 139
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""Inspect"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.Inspect"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""alias"",
            ""typeInfo"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            },
            ""pointer"": {
              ""type"": ""sequenceConversion"",
              ""operation"": ""retainCollectionType"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              },
              ""collectionType"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            }
          },
          ""source"": {
            ""line"": 52,
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
                    ""type"": ""isCheck"",
                    ""pointer"": {
                      ""type"": ""sequenceConversion"",
                      ""operation"": ""retainCollectionType"",
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""__arg_0__""
                      },
                      ""collectionType"": {
                        ""type"": 6,
                        ""required"": true,
                        ""entryTypeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      }
                    },
                    ""checkType"": {
                      ""type"": 22,
                      ""required"": true,
                      ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                      ""typeArguments"": {
                        ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                          ""type"": 2,
                          ""required"": true
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
                        ""type"": ""isCheck"",
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""alias""
                        },
                        ""checkType"": {
                          ""type"": 22,
                          ""required"": true,
                          ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                          ""typeArguments"": {
                            ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                              ""type"": 2,
                              ""required"": true
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
                    }
                  }
                }
              }
            }
          },
          ""source"": {
            ""line"": 52,
            ""column"": 76
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": [""IEnumerable""]
      }
    },
    ""InspectGeneric"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.InspectGeneric"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:InspectGeneric"",
            ""index"": 0
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:InspectGeneric"",
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
            ""type"": ""isCheck"",
            ""pointer"": {
              ""type"": ""sequenceConversion"",
              ""operation"": ""retainCollectionType"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              },
              ""collectionType"": {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:InspectGeneric"",
                ""index"": 0
              }
            },
            ""checkType"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            }
          },
          ""source"": {
            ""line"": 53,
            ""column"": 45
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": [""IEnumerable""]
      }
    },
    ""InspectNested"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.InspectNested"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": ""value"",
            ""value"": {
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
              ""type"": ""count"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""where"",
                    ""info"": {
                      ""collectionPointer"": {
                        ""type"": ""sequenceConversion"",
                        ""operation"": ""queryEnumerable"",
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""__arg_0__""
                        },
                        ""elementType"": {
                          ""type"": 6,
                          ""required"": true,
                          ""entryTypeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        }
                      },
                      ""function"": {
                        ""compilerRevision"": 19,
                        ""source"": {
                          ""uri"": ""Probe.neo"",
                          ""coordinateSpace"": ""file"",
                          ""name"": ""Probe.InspectNested <callback>""
                        },
                        ""parameters"": [
                          {
                            ""id"": ""v"",
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
                              ""type"": ""isCheck"",
                              ""pointer"": {
                                ""type"": ""sequenceConversion"",
                                ""operation"": ""retainCollectionType"",
                                ""pointer"": {
                                  ""type"": ""variable"",
                                  ""variableId"": ""v""
                                },
                                ""collectionType"": {
                                  ""type"": 6,
                                  ""required"": true,
                                  ""entryTypeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                }
                              },
                              ""checkType"": {
                                ""type"": 22,
                                ""required"": true,
                                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                                ""typeArguments"": {
                                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                }
                              }
                            },
                            ""source"": {
                              ""line"": 54,
                              ""column"": 74
                            }
                          }
                        ],
                        ""typeInfo"": {
                          ""type"": 1,
                          ""required"": true
                        }
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 54,
                    ""column"": 66
                  }
                }
              }
            },
            ""source"": {
              ""line"": 54,
              ""column"": 102
            }
          },
          ""source"": {
            ""line"": 54,
            ""column"": 54
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": [""IEnumerable""]
      }
    },
    ""Relay"": {
      ""compilerRevision"": 19,
      ""generator"": {
        ""kind"": ""enumerable"",
        ""elementType"": {
          ""type"": 21,
          ""required"": true,
          ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Relay"",
          ""index"": 0
        }
      },
      ""source"": {
        ""name"": ""Probe.Relay"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 6,
            ""required"": true,
            ""entryTypeInfo"": {
              ""type"": 21,
              ""required"": true,
              ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Relay"",
              ""index"": 0
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 21,
                  ""required"": true,
                  ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Relay"",
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
          ""type"": ""forEach"",
          ""binding"": {
            ""id"": ""value"",
            ""typeInfo"": {
              ""type"": 21,
              ""required"": true,
              ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Relay"",
              ""index"": 0
            },
            ""readonly"": true,
            ""writability"": ""runtime""
          },
          ""collectionPointer"": {
            ""type"": ""variable"",
            ""variableId"": ""__arg_0__""
          },
          ""collectionTypeInfo"": {
            ""type"": 6,
            ""required"": true,
            ""entryTypeInfo"": {
              ""type"": 21,
              ""required"": true,
              ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Relay"",
              ""index"": 0
            }
          },
          ""instructions"": [
            {
              ""type"": ""yieldReturn"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""value""
              },
              ""yields"": true,
              ""source"": {
                ""line"": 55,
                ""column"": 81
              }
            }
          ],
          ""yields"": true,
          ""source"": {
            ""line"": 55,
            ""column"": 51
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 21,
            ""required"": true,
            ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:Relay"",
            ""index"": 0
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""EmptyAliases"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.EmptyAliases"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""id"": ""first"",
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
                    ""type"": ""sequenceConversion"",
                    ""operation"": ""queryEnumerable"",
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
                    },
                    ""elementType"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  ""op"": ""Take"",
                  ""countPointer"": {
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
                ""line"": 56,
                ""column"": 57
              }
            }
          },
          ""source"": {
            ""line"": 56,
            ""column"": 31
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""second"",
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
                    ""type"": ""sequenceConversion"",
                    ""operation"": ""queryEnumerable"",
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
                    },
                    ""elementType"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  ""op"": ""Skip"",
                  ""countPointer"": {
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
              ""source"": {
                ""line"": 56,
                ""column"": 89
              }
            }
          },
          ""source"": {
            ""line"": 56,
            ""column"": 62
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""third"",
            ""typeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""pointer"": {
              ""type"": ""sequenceConversion"",
              ""pointer"": {
                ""type"": ""range"",
                ""start"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 0
                  }
                },
                ""end"": {
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
              ""operation"": ""collectEnumerable"",
              ""elementType"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""source"": {
            ""line"": 56,
            ""column"": 94
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""a"",
            ""typeInfo"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            },
            ""pointer"": {
              ""type"": ""sequenceConversion"",
              ""operation"": ""retainCollectionType"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""first""
              },
              ""collectionType"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            }
          },
          ""source"": {
            ""line"": 56,
            ""column"": 118
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""b"",
            ""typeInfo"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            },
            ""pointer"": {
              ""type"": ""sequenceConversion"",
              ""operation"": ""retainCollectionType"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""second""
              },
              ""collectionType"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            }
          },
          ""source"": {
            ""line"": 56,
            ""column"": 146
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""c"",
            ""typeInfo"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            },
            ""pointer"": {
              ""type"": ""sequenceConversion"",
              ""operation"": ""retainCollectionType"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""third""
              },
              ""collectionType"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            }
          },
          ""source"": {
            ""line"": 56,
            ""column"": 175
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
                            ""type"": ""operation"",
                            ""operation"": {
                              ""type"": ""boolean"",
                              ""expression"": {
                                ""condition"": {
                                  ""type"": ""equalTo"",
                                  ""operand1"": {
                                    ""type"": ""variable"",
                                    ""variableId"": ""first""
                                  },
                                  ""operand2"": {
                                    ""type"": ""variable"",
                                    ""variableId"": ""a""
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
                                ""type"": ""variable"",
                                ""variableId"": ""second""
                              },
                              ""operand2"": {
                                ""type"": ""variable"",
                                ""variableId"": ""b""
                              },
                              ""reference"": true
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
                        ""type"": ""variable"",
                        ""variableId"": ""third""
                      },
                      ""operand2"": {
                        ""type"": ""variable"",
                        ""variableId"": ""c""
                      },
                      ""reference"": true
                    }
                  }
                }
              }
            }
          },
          ""source"": {
            ""line"": 56,
            ""column"": 203
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": [""IEnumerable""]
      }
    },
    ""InspectInner"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.InspectInner"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 6,
            ""required"": true,
            ""entryTypeInfo"": {
              ""type"": 21,
              ""required"": true,
              ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:InspectInner"",
              ""index"": 0
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 21,
                  ""required"": true,
                  ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:InspectInner"",
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
            ""type"": ""isCheck"",
            ""pointer"": {
              ""type"": ""sequenceConversion"",
              ""operation"": ""retainCollectionType"",
              ""pointer"": {
                ""type"": ""keyOf"",
                ""keyOf"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_0__""
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
              ""collectionType"": {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:InspectInner"",
                ""index"": 0
              }
            },
            ""checkType"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            }
          },
          ""source"": {
            ""line"": 58,
            ""column"": 49
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": [""IEnumerable""]
      }
    },
    ""InspectTuple"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.InspectTuple"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 6,
            ""required"": true,
            ""entryTypeInfo"": {
              ""type"": 21,
              ""required"": true,
              ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:InspectTuple"",
              ""index"": 0
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 21,
                  ""required"": true,
                  ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:InspectTuple"",
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
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""pair"",
            ""typeInfo"": {
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 21,
                    ""required"": true,
                    ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:InspectTuple"",
                    ""index"": 0
                  },
                  ""name"": ""Value""
                },
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""name"": ""Tag""
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
                      ""type"": ""keyOf"",
                      ""keyOf"": {
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""__arg_0__""
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
                          ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:InspectTuple"",
                          ""index"": 0
                        },
                        ""name"": ""Value""
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""name"": ""Tag""
                      }
                    ]
                  }
                }
              },
              ""source"": {
                ""line"": 59,
                ""column"": 74
              }
            }
          },
          ""source"": {
            ""line"": 59,
            ""column"": 48
          }
        },
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""isCheck"",
            ""pointer"": {
              ""type"": ""sequenceConversion"",
              ""operation"": ""retainCollectionType"",
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
                  ""line"": 59,
                  ""column"": 101
                }
              },
              ""collectionType"": {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:InspectTuple"",
                ""index"": 0
              }
            },
            ""checkType"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            }
          },
          ""source"": {
            ""line"": 59,
            ""column"": 90
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": [""IEnumerable"", ""T""]
      }
    },
    ""InspectSwitch"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.InspectSwitch"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 6,
            ""required"": true,
            ""entryTypeInfo"": {
              ""type"": 21,
              ""required"": true,
              ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:InspectSwitch"",
              ""index"": 0
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 21,
                  ""required"": true,
                  ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:InspectSwitch"",
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
            ""type"": ""switchExpression"",
            ""selectorTypeInfo"": {
              ""type"": 21,
              ""required"": true,
              ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:InspectSwitch"",
              ""index"": 0
            },
            ""selector"": {
              ""type"": ""sequenceConversion"",
              ""operation"": ""retainCollectionType"",
              ""pointer"": {
                ""type"": ""keyOf"",
                ""keyOf"": {
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_0__""
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
              ""collectionType"": {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:InspectSwitch"",
                ""index"": 0
              }
            },
            ""selectorId"": ""__switch:0"",
            ""arms"": [
              {
                ""pattern"": {
                  ""kind"": ""type"",
                  ""checkType"": {
                    ""type"": 22,
                    ""required"": true,
                    ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                    ""typeArguments"": {
                      ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  },
                  ""bindingId"": ""__switch:0_arm_0""
                },
                ""value"": {
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
              {
                ""pattern"": {
                  ""kind"": ""discard""
                },
                ""value"": {
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
            ]
          },
          ""source"": {
            ""line"": 60,
            ""column"": 50
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": [""IEnumerable""]
      }
    },
    ""WidenedMerges"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.WidenedMerges"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
        },
        {
          ""id"": ""__arg_1__"",
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
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""absent"",
            ""typeInfo"": {
              ""type"": 22,
              ""required"": false,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            },
            ""pointer"": {
              ""type"": ""value"",
              ""value"": {
                ""typeInfo"": {
                  ""type"": 7,
                  ""required"": false,
                  ""classId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                  ""typeArguments"": {
                    ""0"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                },
                ""value"": null
              }
            }
          },
          ""source"": {
            ""line"": 63,
            ""column"": 4
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""present"",
            ""typeInfo"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            },
            ""pointer"": {
              ""type"": ""range"",
              ""start"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 4
                }
              },
              ""end"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""value"": 6
                }
              }
            }
          },
          ""source"": {
            ""line"": 64,
            ""column"": 4
          }
        },
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
            ""line"": 65,
            ""column"": 4
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""coalesced"",
            ""typeInfo"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            },
            ""pointer"": {
              ""type"": ""coalesce"",
              ""left"": {
                ""type"": ""variable"",
                ""variableId"": ""absent""
              },
              ""right"": {
                ""type"": ""sequenceConversion"",
                ""operation"": ""retainCollectionType"",
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""values""
                },
                ""collectionType"": {
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
            ""line"": 66,
            ""column"": 4
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""conditional"",
            ""typeInfo"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            },
            ""pointer"": {
              ""type"": ""conditional"",
              ""condition"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              },
              ""whenTrue"": {
                ""type"": ""variable"",
                ""variableId"": ""present""
              },
              ""whenFalse"": {
                ""type"": ""sequenceConversion"",
                ""operation"": ""retainCollectionType"",
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""values""
                },
                ""collectionType"": {
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
            ""line"": 67,
            ""column"": 4
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""switched"",
            ""typeInfo"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            },
            ""pointer"": {
              ""type"": ""switchExpression"",
              ""selectorTypeInfo"": {
                ""type"": 2,
                ""required"": true
              },
              ""selector"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_1__""
              },
              ""selectorId"": ""__switch:0"",
              ""arms"": [
                {
                  ""pattern"": {
                    ""kind"": ""constant"",
                    ""constant"": {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""value"": 1
                    }
                  },
                  ""value"": {
                    ""type"": ""variable"",
                    ""variableId"": ""present""
                  }
                },
                {
                  ""pattern"": {
                    ""kind"": ""discard""
                  },
                  ""value"": {
                    ""type"": ""sequenceConversion"",
                    ""operation"": ""retainCollectionType"",
                    ""pointer"": {
                      ""type"": ""variable"",
                      ""variableId"": ""values""
                    },
                    ""collectionType"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  }
                }
              ]
            }
          },
          ""source"": {
            ""line"": 68,
            ""column"": 4
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""assigned"",
            ""typeInfo"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            },
            ""pointer"": {
              ""type"": ""coalesce"",
              ""left"": {
                ""type"": ""variable"",
                ""variableId"": ""absent""
              },
              ""right"": {
                ""type"": ""sequenceConversion"",
                ""operation"": ""retainCollectionType"",
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
                    }
                  ]
                },
                ""collectionType"": {
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
            ""line"": 69,
            ""column"": 4
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
                            ""type"": ""operation"",
                            ""operation"": {
                              ""type"": ""boolean"",
                              ""expression"": {
                                ""condition"": {
                                  ""type"": ""equalTo"",
                                  ""operand1"": {
                                    ""type"": ""isCheck"",
                                    ""pointer"": {
                                      ""type"": ""variable"",
                                      ""variableId"": ""coalesced""
                                    },
                                    ""checkType"": {
                                      ""type"": 22,
                                      ""required"": true,
                                      ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                                      ""typeArguments"": {
                                        ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                                          ""type"": 2,
                                          ""required"": true
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
                                        ""type"": ""isCheck"",
                                        ""pointer"": {
                                          ""type"": ""variable"",
                                          ""variableId"": ""conditional""
                                        },
                                        ""checkType"": {
                                          ""type"": 22,
                                          ""required"": true,
                                          ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                                          ""typeArguments"": {
                                            ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                                              ""type"": 2,
                                              ""required"": true
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
                                ""type"": ""isCheck"",
                                ""pointer"": {
                                  ""type"": ""variable"",
                                  ""variableId"": ""switched""
                                },
                                ""checkType"": {
                                  ""type"": 22,
                                  ""required"": true,
                                  ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                                  ""typeArguments"": {
                                    ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                                      ""type"": 2,
                                      ""required"": true
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
                        ""type"": ""isCheck"",
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""assigned""
                        },
                        ""checkType"": {
                          ""type"": 22,
                          ""required"": true,
                          ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                          ""typeArguments"": {
                            ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                              ""type"": 2,
                              ""required"": true
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
                    }
                  }
                }
              }
            }
          },
          ""source"": {
            ""line"": 70,
            ""column"": 4
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": [""IEnumerable""]
      }
    },
    ""WidenedReturn"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.WidenedReturn"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": false,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": false,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__arg_1__"",
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
            ""type"": ""coalesce"",
            ""left"": {
              ""type"": ""variable"",
              ""variableId"": ""__arg_0__""
            },
            ""right"": {
              ""type"": ""sequenceConversion"",
              ""operation"": ""retainCollectionType"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_1__""
              },
              ""collectionType"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            }
          },
          ""source"": {
            ""line"": 72,
            ""column"": 87
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""WidenedAlias"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.WidenedAlias"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""line"": 74,
            ""column"": 4
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""alias"",
            ""typeInfo"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            },
            ""pointer"": {
              ""type"": ""sequenceConversion"",
              ""operation"": ""retainCollectionType"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""values""
              },
              ""collectionType"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            }
          },
          ""source"": {
            ""line"": 74,
            ""column"": 28
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
            ""line"": 75,
            ""column"": 4
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
                ""value"": 3
              }
            }
          ],
          ""source"": {
            ""line"": 75,
            ""column"": 19
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
                      ""variableId"": ""alias""
                    },
                    ""checkType"": {
                      ""type"": 22,
                      ""required"": true,
                      ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                      ""typeArguments"": {
                        ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                          ""type"": 2,
                          ""required"": true
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
                      ""value"": ""List alias lost collection type""
                    }
                  },
                  ""source"": {
                    ""line"": 76,
                    ""column"": 38
                  }
                }
              ]
            }
          ],
          ""else"": null,
          ""source"": {
            ""line"": 76,
            ""column"": 4
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""collected"",
            ""typeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""pointer"": {
              ""type"": ""sequenceConversion"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""alias""
              },
              ""operation"": ""collectEnumerable"",
              ""elementType"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""source"": {
            ""line"": 77,
            ""column"": 4
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
                      ""type"": ""count"",
                      ""info"": {
                        ""collectionPointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""collected""
                        }
                      }
                    },
                    ""source"": {
                      ""line"": 78,
                      ""column"": 26
                    }
                  },
                  ""operand2"": {
                    ""type"": ""value"",
                    ""value"": {
                      ""typeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      },
                      ""value"": 3
                    }
                  }
                }
              }
            }
          },
          ""source"": {
            ""line"": 78,
            ""column"": 4
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": [""IEnumerable""]
      }
    },
    ""WidenedTuple"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.WidenedTuple"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""line"": 81,
            ""column"": 4
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""source"",
            ""typeInfo"": {
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""name"": ""A""
                },
                {
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
                        },
                        ""name"": ""Values""
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""name"": ""Tag""
                      }
                    ]
                  },
                  ""name"": ""B""
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
                                  ""type"": 6,
                                  ""required"": true,
                                  ""entryTypeInfo"": {
                                    ""type"": 2,
                                    ""required"": true
                                  }
                                },
                                ""name"": ""Values""
                              },
                              {
                                ""typeInfo"": {
                                  ""type"": 2,
                                  ""required"": true
                                },
                                ""name"": ""Tag""
                              }
                            ]
                          }
                        }
                      },
                      ""source"": {
                        ""line"": 82,
                        ""column"": 27
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
                        ""name"": ""A""
                      },
                      {
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
                              },
                              ""name"": ""Values""
                            },
                            {
                              ""typeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              },
                              ""name"": ""Tag""
                            }
                          ]
                        },
                        ""name"": ""B""
                      }
                    ]
                  }
                }
              },
              ""source"": {
                ""line"": 82,
                ""column"": 17
              }
            }
          },
          ""source"": {
            ""line"": 82,
            ""column"": 4
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""destination"",
            ""typeInfo"": {
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""name"": ""A""
                },
                {
                  ""typeInfo"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 22,
                          ""required"": true,
                          ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                          ""typeArguments"": {
                            ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                              ""type"": 2,
                              ""required"": true
                            }
                          }
                        },
                        ""name"": ""Values""
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""name"": ""Tag""
                      }
                    ]
                  },
                  ""name"": ""B""
                }
              ]
            },
            ""pointer"": {
              ""type"": ""sequenceConversion"",
              ""operation"": ""retainCollectionType"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""source""
              },
              ""collectionType"": {
                ""type"": 30,
                ""required"": true,
                ""elements"": [
                  {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""name"": ""A""
                  },
                  {
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
                          },
                          ""name"": ""Values""
                        },
                        {
                          ""typeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          },
                          ""name"": ""Tag""
                        }
                      ]
                    },
                    ""name"": ""B""
                  }
                ]
              }
            }
          },
          ""source"": {
            ""line"": 83,
            ""column"": 4
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
            ""line"": 84,
            ""column"": 4
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
              ""type"": ""sequenceConversion"",
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
                            ""variableId"": ""destination""
                          },
                          ""index"": 1
                        }
                      },
                      ""source"": {
                        ""line"": 85,
                        ""column"": 33
                      }
                    },
                    ""index"": 0
                  }
                },
                ""source"": {
                  ""line"": 85,
                  ""column"": 35
                }
              },
              ""operation"": ""collectEnumerable"",
              ""elementType"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""source"": {
            ""line"": 85,
            ""column"": 4
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
                                  ""variableId"": ""destination""
                                },
                                ""index"": 1
                              }
                            },
                            ""source"": {
                              ""line"": 86,
                              ""column"": 21
                            }
                          },
                          ""index"": 0
                        }
                      },
                      ""source"": {
                        ""line"": 86,
                        ""column"": 23
                      }
                    },
                    ""checkType"": {
                      ""type"": 22,
                      ""required"": true,
                      ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                      ""typeArguments"": {
                        ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                          ""type"": 2,
                          ""required"": true
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
                      ""value"": ""Widened tuple lost collection type""
                    }
                  },
                  ""source"": {
                    ""line"": 86,
                    ""column"": 53
                  }
                }
              ]
            }
          ],
          ""else"": null,
          ""source"": {
            ""line"": 86,
            ""column"": 4
          }
        },
        {
          ""type"": ""if"",
          ""branches"": [
            {
              ""expression"": {
                ""condition"": {
                  ""type"": ""doesNotEqual"",
                  ""operand1"": {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""count"",
                      ""info"": {
                        ""collectionPointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""alias""
                        }
                      }
                    },
                    ""source"": {
                      ""line"": 86,
                      ""column"": 112
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
                      ""value"": ""Widened tuple lost list mutation""
                    }
                  },
                  ""source"": {
                    ""line"": 86,
                    ""column"": 121
                  }
                }
              ]
            }
          ],
          ""else"": null,
          ""source"": {
            ""line"": 86,
            ""column"": 97
          }
        },
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 1,
                ""required"": true
              },
              ""value"": true
            }
          },
          ""source"": {
            ""line"": 86,
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": [""IEnumerable""]
      }
    },
    ""LocalSequenceSlots"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.LocalSequenceSlots"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""line"": 89,
            ""column"": 4
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""lists"",
            ""typeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              }
            },
            ""pointer"": {
              ""type"": ""listLiteral"",
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 22,
                  ""required"": true,
                  ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                  ""typeArguments"": {
                    ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                }
              },
              ""entries"": [
                {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""retainCollectionType"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""values""
                  },
                  ""collectionType"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                }
              ]
            }
          },
          ""source"": {
            ""line"": 90,
            ""column"": 4
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
                ""variableId"": ""lists""
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
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            },
            ""writability"": ""local""
          },
          ""operator"": ""="",
          ""pointer"": {
            ""type"": ""sequenceConversion"",
            ""operation"": ""retainCollectionType"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""values""
            },
            ""collectionType"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""source"": {
            ""line"": 90,
            ""column"": 45
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""map"",
            ""typeInfo"": {
              ""type"": 5,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              }
            },
            ""pointer"": {
              ""type"": ""dictLiteral"",
              ""typeInfo"": {
                ""type"": 5,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 22,
                  ""required"": true,
                  ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                  ""typeArguments"": {
                    ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                }
              },
              ""entries"": []
            }
          },
          ""source"": {
            ""line"": 91,
            ""column"": 4
          }
        },
        {
          ""type"": ""assign"",
          ""bindings"": [
            {
              ""id"": ""$assignment_1"",
              ""typeInfo"": {
                ""type"": ""Unknown"",
                ""required"": true
              },
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""map""
              }
            }
          ],
          ""target"": {
            ""pointer"": {
              ""type"": ""keyOf"",
              ""keyOf"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""$assignment_1""
                },
                ""key"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 3,
                      ""required"": true
                    },
                    ""value"": ""a""
                  }
                }
              }
            },
            ""typeInfo"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            },
            ""writability"": ""local""
          },
          ""operator"": ""="",
          ""pointer"": {
            ""type"": ""sequenceConversion"",
            ""operation"": ""retainCollectionType"",
            ""pointer"": {
              ""type"": ""variable"",
              ""variableId"": ""values""
            },
            ""collectionType"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""source"": {
            ""line"": 91,
            ""column"": 51
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
                    ""type"": ""isCheck"",
                    ""pointer"": {
                      ""type"": ""keyOf"",
                      ""keyOf"": {
                        ""pointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""lists""
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
                    ""checkType"": {
                      ""type"": 22,
                      ""required"": true,
                      ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                      ""typeArguments"": {
                        ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                          ""type"": 2,
                          ""required"": true
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
                        ""type"": ""isCheck"",
                        ""pointer"": {
                          ""type"": ""keyOf"",
                          ""keyOf"": {
                            ""pointer"": {
                              ""type"": ""variable"",
                              ""variableId"": ""map""
                            },
                            ""key"": {
                              ""type"": ""value"",
                              ""value"": {
                                ""typeInfo"": {
                                  ""type"": 3,
                                  ""required"": true
                                },
                                ""value"": ""a""
                              }
                            }
                          }
                        },
                        ""checkType"": {
                          ""type"": 22,
                          ""required"": true,
                          ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                          ""typeArguments"": {
                            ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                              ""type"": 2,
                              ""required"": true
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
                    }
                  }
                }
              }
            }
          },
          ""source"": {
            ""line"": 92,
            ""column"": 4
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": [""IEnumerable""]
      }
    },
    ""TypedBindings"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.TypedBindings"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""id"": ""nested"",
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
                {
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
                    }
                  ]
                }
              ]
            }
          },
          ""source"": {
            ""line"": 96,
            ""column"": 4
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""count"",
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
            ""line"": 97,
            ""column"": 4
          }
        },
        {
          ""type"": ""forEach"",
          ""binding"": {
            ""id"": ""__sequence:item"",
            ""typeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""readonly"": true,
            ""writability"": ""local""
          },
          ""collectionPointer"": {
            ""type"": ""variable"",
            ""variableId"": ""nested""
          },
          ""collectionTypeInfo"": {
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
          ""instructions"": [
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""item"",
                ""typeInfo"": {
                  ""type"": 22,
                  ""required"": true,
                  ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                  ""typeArguments"": {
                    ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                },
                ""pointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""retainCollectionType"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__sequence:item""
                  },
                  ""collectionType"": {
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
                          ""variableId"": ""item""
                        },
                        ""checkType"": {
                          ""type"": 22,
                          ""required"": true,
                          ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                          ""typeArguments"": {
                            ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                              ""type"": 2,
                              ""required"": true
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
                  },
                  ""instructions"": [
                    {
                      ""type"": ""return"",
                      ""pointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 1,
                            ""required"": true
                          },
                          ""value"": false
                        }
                      },
                      ""source"": {
                        ""line"": 98,
                        ""column"": 81
                      }
                    }
                  ]
                }
              ],
              ""else"": null,
              ""source"": {
                ""line"": 98,
                ""column"": 48
              }
            },
            {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""count""
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
                        ""type"": ""variable"",
                        ""variableId"": ""count""
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
                ""line"": 98,
                ""column"": 95
              }
            }
          ],
          ""source"": {
            ""line"": 98,
            ""column"": 4
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""map"",
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
                      ""value"": ""a""
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
                          ""value"": 3
                        }
                      }
                    ]
                  }
                }
              ]
            }
          },
          ""source"": {
            ""line"": 99,
            ""column"": 4
          }
        },
        {
          ""type"": ""forEach"",
          ""binding"": {
            ""id"": ""__sequence:item"",
            ""typeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""readonly"": true,
            ""writability"": ""local""
          },
          ""collectionPointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""collectionQuery"",
              ""info"": {
                ""op"": ""Values"",
                ""collectionPointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""map""
                }
              }
            },
            ""source"": {
              ""line"": 100,
              ""column"": 41
            }
          },
          ""collectionTypeInfo"": {
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
          ""instructions"": [
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""item"",
                ""typeInfo"": {
                  ""type"": 22,
                  ""required"": true,
                  ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                  ""typeArguments"": {
                    ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                },
                ""pointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""retainCollectionType"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__sequence:item""
                  },
                  ""collectionType"": {
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
                          ""variableId"": ""item""
                        },
                        ""checkType"": {
                          ""type"": 22,
                          ""required"": true,
                          ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                          ""typeArguments"": {
                            ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                              ""type"": 2,
                              ""required"": true
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
                  },
                  ""instructions"": [
                    {
                      ""type"": ""return"",
                      ""pointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 1,
                            ""required"": true
                          },
                          ""value"": false
                        }
                      },
                      ""source"": {
                        ""line"": 100,
                        ""column"": 85
                      }
                    }
                  ]
                }
              ],
              ""else"": null,
              ""source"": {
                ""line"": 100,
                ""column"": 52
              }
            },
            {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""count""
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
                        ""type"": ""variable"",
                        ""variableId"": ""count""
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
                ""line"": 100,
                ""column"": 99
              }
            }
          ],
          ""source"": {
            ""line"": 100,
            ""column"": 4
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""#foreach117#collection"",
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
              ""type"": ""variable"",
              ""variableId"": ""map""
            }
          },
          ""source"": {
            ""line"": 101,
            ""column"": 4
          }
        },
        {
          ""type"": ""forEach"",
          ""binding"": {
            ""id"": ""#foreach117#key"",
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
                  ""variableId"": ""#foreach117#collection""
                },
                ""function"": {
                  ""compilerRevision"": 19,
                  ""source"": {
                    ""name"": ""Probe.TypedBindings"",
                    ""uri"": ""Probe.neo"",
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
                ""id"": ""#foreach117#pattern"",
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
                        ""type"": 6,
                        ""required"": true,
                        ""entryTypeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
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
                          ""variableId"": ""#foreach117#key""
                        },
                        {
                          ""type"": ""keyOf"",
                          ""keyOf"": {
                            ""pointer"": {
                              ""type"": ""variable"",
                              ""variableId"": ""#foreach117#collection""
                            },
                            ""key"": {
                              ""type"": ""variable"",
                              ""variableId"": ""#foreach117#key""
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
                              ""type"": 6,
                              ""required"": true,
                              ""entryTypeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              }
                            }
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 101,
                    ""column"": 4
                  }
                }
              },
              ""source"": {
                ""line"": 101,
                ""column"": 4
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
                        ""variableId"": ""#foreach117#pattern""
                      },
                      ""index"": 0
                    }
                  },
                  ""source"": {
                    ""line"": 101,
                    ""column"": 21
                  }
                }
              },
              ""source"": {
                ""line"": 101,
                ""column"": 21
              }
            },
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""item"",
                ""typeInfo"": {
                  ""type"": 22,
                  ""required"": true,
                  ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                  ""typeArguments"": {
                    ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                },
                ""pointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""retainCollectionType"",
                  ""pointer"": {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""tupleElement"",
                      ""info"": {
                        ""tuplePointer"": {
                          ""type"": ""variable"",
                          ""variableId"": ""#foreach117#pattern""
                        },
                        ""index"": 1
                      }
                    },
                    ""source"": {
                      ""line"": 101,
                      ""column"": 43
                    }
                  },
                  ""collectionType"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                }
              },
              ""source"": {
                ""line"": 101,
                ""column"": 43
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
                          ""variableId"": ""item""
                        },
                        ""checkType"": {
                          ""type"": 22,
                          ""required"": true,
                          ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                          ""typeArguments"": {
                            ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                              ""type"": 2,
                              ""required"": true
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
                  },
                  ""instructions"": [
                    {
                      ""type"": ""return"",
                      ""pointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 1,
                            ""required"": true
                          },
                          ""value"": false
                        }
                      },
                      ""source"": {
                        ""line"": 101,
                        ""column"": 92
                      }
                    }
                  ]
                }
              ],
              ""else"": null,
              ""source"": {
                ""line"": 101,
                ""column"": 59
              }
            },
            {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""count""
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
                        ""type"": ""variable"",
                        ""variableId"": ""count""
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
                ""line"": 101,
                ""column"": 106
              }
            }
          ],
          ""source"": {
            ""line"": 101,
            ""column"": 4
          }
        },
        {
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""tuples"",
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
                    },
                    ""name"": ""A""
                  },
                  {
                    ""typeInfo"": {
                      ""type"": 6,
                      ""required"": true,
                      ""entryTypeInfo"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    },
                    ""name"": ""B""
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
                      },
                      ""name"": ""A""
                    },
                    {
                      ""typeInfo"": {
                        ""type"": 6,
                        ""required"": true,
                        ""entryTypeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        }
                      },
                      ""name"": ""B""
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
                                ""value"": 4
                              }
                            }
                          ]
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
                            ""name"": ""A""
                          },
                          {
                            ""typeInfo"": {
                              ""type"": 6,
                              ""required"": true,
                              ""entryTypeInfo"": {
                                ""type"": 2,
                                ""required"": true
                              }
                            },
                            ""name"": ""B""
                          }
                        ]
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 102,
                    ""column"": 41
                  }
                }
              ]
            }
          },
          ""source"": {
            ""line"": 102,
            ""column"": 4
          }
        },
        {
          ""type"": ""forEach"",
          ""binding"": {
            ""id"": ""__sequence:pair"",
            ""typeInfo"": {
              ""type"": 30,
              ""required"": true,
              ""elements"": [
                {
                  ""typeInfo"": {
                    ""type"": 2,
                    ""required"": true
                  },
                  ""name"": ""A""
                },
                {
                  ""typeInfo"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  ""name"": ""B""
                }
              ]
            },
            ""readonly"": true,
            ""writability"": ""local""
          },
          ""collectionPointer"": {
            ""type"": ""variable"",
            ""variableId"": ""tuples""
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
                  },
                  ""name"": ""A""
                },
                {
                  ""typeInfo"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  },
                  ""name"": ""B""
                }
              ]
            }
          },
          ""instructions"": [
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
                        ""type"": 2,
                        ""required"": true
                      },
                      ""name"": ""A""
                    },
                    {
                      ""typeInfo"": {
                        ""type"": 22,
                        ""required"": true,
                        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                        ""typeArguments"": {
                          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        }
                      },
                      ""name"": ""B""
                    }
                  ]
                },
                ""pointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""retainCollectionType"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__sequence:pair""
                  },
                  ""collectionType"": {
                    ""type"": 30,
                    ""required"": true,
                    ""elements"": [
                      {
                        ""typeInfo"": {
                          ""type"": 2,
                          ""required"": true
                        },
                        ""name"": ""A""
                      },
                      {
                        ""typeInfo"": {
                          ""type"": 6,
                          ""required"": true,
                          ""entryTypeInfo"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        },
                        ""name"": ""B""
                      }
                    ]
                  }
                }
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
                          ""type"": ""function"",
                          ""function"": {
                            ""type"": ""tupleElement"",
                            ""info"": {
                              ""tuplePointer"": {
                                ""type"": ""variable"",
                                ""variableId"": ""pair""
                              },
                              ""index"": 1
                            }
                          },
                          ""source"": {
                            ""line"": 103,
                            ""column"": 69
                          }
                        },
                        ""checkType"": {
                          ""type"": 22,
                          ""required"": true,
                          ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                          ""typeArguments"": {
                            ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                              ""type"": 2,
                              ""required"": true
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
                  },
                  ""instructions"": [
                    {
                      ""type"": ""return"",
                      ""pointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 1,
                            ""required"": true
                          },
                          ""value"": false
                        }
                      },
                      ""source"": {
                        ""line"": 103,
                        ""column"": 94
                      }
                    }
                  ]
                }
              ],
              ""else"": null,
              ""source"": {
                ""line"": 103,
                ""column"": 59
              }
            },
            {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""count""
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
                        ""type"": ""variable"",
                        ""variableId"": ""count""
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
                ""line"": 103,
                ""column"": 108
              }
            }
          ],
          ""source"": {
            ""line"": 103,
            ""column"": 4
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
                                            ""type"": ""variable"",
                                            ""variableId"": ""count""
                                          },
                                          ""operand2"": {
                                            ""type"": ""value"",
                                            ""value"": {
                                              ""typeInfo"": {
                                                ""type"": 2,
                                                ""required"": true
                                              },
                                              ""value"": 5
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
                                          ""type"": ""count"",
                                          ""info"": {
                                            ""collectionPointer"": {
                                              ""type"": ""function"",
                                              ""function"": {
                                                ""type"": ""where"",
                                                ""info"": {
                                                  ""collectionPointer"": {
                                                    ""type"": ""sequenceConversion"",
                                                    ""operation"": ""queryEnumerable"",
                                                    ""pointer"": {
                                                      ""type"": ""variable"",
                                                      ""variableId"": ""nested""
                                                    },
                                                    ""elementType"": {
                                                      ""type"": 6,
                                                      ""required"": true,
                                                      ""entryTypeInfo"": {
                                                        ""type"": 2,
                                                        ""required"": true
                                                      }
                                                    }
                                                  },
                                                  ""function"": {
                                                    ""compilerRevision"": 19,
                                                    ""source"": {
                                                      ""uri"": ""Probe.neo"",
                                                      ""coordinateSpace"": ""file"",
                                                      ""name"": ""Probe.TypedBindings <callback>""
                                                    },
                                                    ""parameters"": [
                                                      {
                                                        ""id"": ""__sequence:item"",
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
                                                        ""type"": ""variable"",
                                                        ""variable"": {
                                                          ""id"": ""item"",
                                                          ""typeInfo"": {
                                                            ""type"": 22,
                                                            ""required"": true,
                                                            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                                                            ""typeArguments"": {
                                                              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                                                                ""type"": 2,
                                                                ""required"": true
                                                              }
                                                            }
                                                          },
                                                          ""pointer"": {
                                                            ""type"": ""sequenceConversion"",
                                                            ""operation"": ""retainCollectionType"",
                                                            ""pointer"": {
                                                              ""type"": ""variable"",
                                                              ""variableId"": ""__sequence:item""
                                                            },
                                                            ""collectionType"": {
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
                                                      {
                                                        ""type"": ""return"",
                                                        ""pointer"": {
                                                          ""type"": ""isCheck"",
                                                          ""pointer"": {
                                                            ""type"": ""variable"",
                                                            ""variableId"": ""item""
                                                          },
                                                          ""checkType"": {
                                                            ""type"": 22,
                                                            ""required"": true,
                                                            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                                                            ""typeArguments"": {
                                                              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                                                                ""type"": 2,
                                                                ""required"": true
                                                              }
                                                            }
                                                          }
                                                        },
                                                        ""source"": {
                                                          ""line"": 104,
                                                          ""column"": 65
                                                        }
                                                      }
                                                    ],
                                                    ""typeInfo"": {
                                                      ""type"": 1,
                                                      ""required"": true
                                                    }
                                                  }
                                                }
                                              },
                                              ""source"": {
                                                ""line"": 104,
                                                ""column"": 37
                                              }
                                            }
                                          }
                                        },
                                        ""source"": {
                                          ""line"": 104,
                                          ""column"": 96
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
                                  ""type"": ""collectionQuery"",
                                  ""info"": {
                                    ""collectionPointer"": {
                                      ""type"": ""sequenceConversion"",
                                      ""operation"": ""queryEnumerable"",
                                      ""pointer"": {
                                        ""type"": ""function"",
                                        ""function"": {
                                          ""type"": ""select"",
                                          ""info"": {
                                            ""collectionPointer"": {
                                              ""type"": ""sequenceConversion"",
                                              ""operation"": ""queryEnumerable"",
                                              ""pointer"": {
                                                ""type"": ""variable"",
                                                ""variableId"": ""nested""
                                              },
                                              ""elementType"": {
                                                ""type"": 6,
                                                ""required"": true,
                                                ""entryTypeInfo"": {
                                                  ""type"": 2,
                                                  ""required"": true
                                                }
                                              }
                                            },
                                            ""function"": {
                                              ""compilerRevision"": 19,
                                              ""source"": {
                                                ""uri"": ""Probe.neo"",
                                                ""coordinateSpace"": ""file"",
                                                ""name"": ""Probe.TypedBindings <callback>""
                                              },
                                              ""parameters"": [
                                                {
                                                  ""id"": ""__sequence:item"",
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
                                                  ""type"": ""variable"",
                                                  ""variable"": {
                                                    ""id"": ""item"",
                                                    ""typeInfo"": {
                                                      ""type"": 22,
                                                      ""required"": true,
                                                      ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                                                      ""typeArguments"": {
                                                        ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                                                          ""type"": 2,
                                                          ""required"": true
                                                        }
                                                      }
                                                    },
                                                    ""pointer"": {
                                                      ""type"": ""sequenceConversion"",
                                                      ""operation"": ""retainCollectionType"",
                                                      ""pointer"": {
                                                        ""type"": ""variable"",
                                                        ""variableId"": ""__sequence:item""
                                                      },
                                                      ""collectionType"": {
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
                                                {
                                                  ""type"": ""return"",
                                                  ""pointer"": {
                                                    ""type"": ""isCheck"",
                                                    ""pointer"": {
                                                      ""type"": ""variable"",
                                                      ""variableId"": ""item""
                                                    },
                                                    ""checkType"": {
                                                      ""type"": 22,
                                                      ""required"": true,
                                                      ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                                                      ""typeArguments"": {
                                                        ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                                                          ""type"": 2,
                                                          ""required"": true
                                                        }
                                                      }
                                                    }
                                                  },
                                                  ""source"": {
                                                    ""line"": 105,
                                                    ""column"": 50
                                                  }
                                                }
                                              ],
                                              ""typeInfo"": {
                                                ""type"": 1,
                                                ""required"": true
                                              }
                                            }
                                          }
                                        },
                                        ""source"": {
                                          ""line"": 105,
                                          ""column"": 22
                                        }
                                      },
                                      ""elementType"": {
                                        ""type"": 1,
                                        ""required"": true
                                      }
                                    },
                                    ""op"": ""All"",
                                    ""function"": {
                                      ""compilerRevision"": 19,
                                      ""source"": {
                                        ""uri"": ""Probe.neo"",
                                        ""coordinateSpace"": ""file"",
                                        ""name"": ""Probe.TypedBindings <callback>""
                                      },
                                      ""parameters"": [
                                        {
                                          ""id"": ""item"",
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
                                          ""type"": ""return"",
                                          ""pointer"": {
                                            ""type"": ""variable"",
                                            ""variableId"": ""item""
                                          },
                                          ""source"": {
                                            ""line"": 105,
                                            ""column"": 90
                                          }
                                        }
                                      ],
                                      ""typeInfo"": {
                                        ""type"": 1,
                                        ""required"": true
                                      }
                                    }
                                  }
                                },
                                ""source"": {
                                  ""line"": 105,
                                  ""column"": 79
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
                                ""type"": ""where"",
                                ""info"": {
                                  ""collectionPointer"": {
                                    ""type"": ""sequenceConversion"",
                                    ""operation"": ""queryEnumerable"",
                                    ""pointer"": {
                                      ""type"": ""variable"",
                                      ""variableId"": ""tuples""
                                    },
                                    ""elementType"": {
                                      ""type"": 30,
                                      ""required"": true,
                                      ""elements"": [
                                        {
                                          ""typeInfo"": {
                                            ""type"": 2,
                                            ""required"": true
                                          },
                                          ""name"": ""A""
                                        },
                                        {
                                          ""typeInfo"": {
                                            ""type"": 6,
                                            ""required"": true,
                                            ""entryTypeInfo"": {
                                              ""type"": 2,
                                              ""required"": true
                                            }
                                          },
                                          ""name"": ""B""
                                        }
                                      ]
                                    }
                                  },
                                  ""function"": {
                                    ""compilerRevision"": 19,
                                    ""source"": {
                                      ""uri"": ""Probe.neo"",
                                      ""coordinateSpace"": ""file"",
                                      ""name"": ""Probe.TypedBindings <callback>""
                                    },
                                    ""parameters"": [
                                      {
                                        ""id"": ""__sequence:pair"",
                                        ""typeInfo"": {
                                          ""type"": 30,
                                          ""required"": true,
                                          ""elements"": [
                                            {
                                              ""typeInfo"": {
                                                ""type"": 2,
                                                ""required"": true
                                              },
                                              ""name"": ""A""
                                            },
                                            {
                                              ""typeInfo"": {
                                                ""type"": 6,
                                                ""required"": true,
                                                ""entryTypeInfo"": {
                                                  ""type"": 2,
                                                  ""required"": true
                                                }
                                              },
                                              ""name"": ""B""
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
                                                  },
                                                  ""name"": ""A""
                                                },
                                                {
                                                  ""typeInfo"": {
                                                    ""type"": 6,
                                                    ""required"": true,
                                                    ""entryTypeInfo"": {
                                                      ""type"": 2,
                                                      ""required"": true
                                                    }
                                                  },
                                                  ""name"": ""B""
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
                                          ""id"": ""pair"",
                                          ""typeInfo"": {
                                            ""type"": 30,
                                            ""required"": true,
                                            ""elements"": [
                                              {
                                                ""typeInfo"": {
                                                  ""type"": 2,
                                                  ""required"": true
                                                },
                                                ""name"": ""A""
                                              },
                                              {
                                                ""typeInfo"": {
                                                  ""type"": 22,
                                                  ""required"": true,
                                                  ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                                                  ""typeArguments"": {
                                                    ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                                                      ""type"": 2,
                                                      ""required"": true
                                                    }
                                                  }
                                                },
                                                ""name"": ""B""
                                              }
                                            ]
                                          },
                                          ""pointer"": {
                                            ""type"": ""sequenceConversion"",
                                            ""operation"": ""retainCollectionType"",
                                            ""pointer"": {
                                              ""type"": ""variable"",
                                              ""variableId"": ""__sequence:pair""
                                            },
                                            ""collectionType"": {
                                              ""type"": 30,
                                              ""required"": true,
                                              ""elements"": [
                                                {
                                                  ""typeInfo"": {
                                                    ""type"": 2,
                                                    ""required"": true
                                                  },
                                                  ""name"": ""A""
                                                },
                                                {
                                                  ""typeInfo"": {
                                                    ""type"": 6,
                                                    ""required"": true,
                                                    ""entryTypeInfo"": {
                                                      ""type"": 2,
                                                      ""required"": true
                                                    }
                                                  },
                                                  ""name"": ""B""
                                                }
                                              ]
                                            }
                                          }
                                        }
                                      },
                                      {
                                        ""type"": ""return"",
                                        ""pointer"": {
                                          ""type"": ""isCheck"",
                                          ""pointer"": {
                                            ""type"": ""function"",
                                            ""function"": {
                                              ""type"": ""tupleElement"",
                                              ""info"": {
                                                ""tuplePointer"": {
                                                  ""type"": ""variable"",
                                                  ""variableId"": ""pair""
                                                },
                                                ""index"": 1
                                              }
                                            },
                                            ""source"": {
                                              ""line"": 106,
                                              ""column"": 64
                                            }
                                          },
                                          ""checkType"": {
                                            ""type"": 22,
                                            ""required"": true,
                                            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                                            ""typeArguments"": {
                                              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                                                ""type"": 2,
                                                ""required"": true
                                              }
                                            }
                                          }
                                        },
                                        ""source"": {
                                          ""line"": 106,
                                          ""column"": 60
                                        }
                                      }
                                    ],
                                    ""typeInfo"": {
                                      ""type"": 1,
                                      ""required"": true
                                    }
                                  }
                                }
                              },
                              ""source"": {
                                ""line"": 106,
                                ""column"": 21
                              }
                            }
                          }
                        },
                        ""source"": {
                          ""line"": 106,
                          ""column"": 93
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
            ""line"": 104,
            ""column"": 4
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": [""IEnumerable""]
      }
    },
    ""ListGenerator"": {
      ""compilerRevision"": 19,
      ""generator"": {
        ""kind"": ""enumerable"",
        ""elementType"": {
          ""type"": 6,
          ""required"": true,
          ""entryTypeInfo"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""source"": {
        ""name"": ""Probe.ListGenerator"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
          ""type"": ""yieldReturn"",
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
          },
          ""yields"": true,
          ""source"": {
            ""line"": 108,
            ""column"": 50
          }
        },
        {
          ""type"": ""yieldReturn"",
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
              }
            ]
          },
          ""yields"": true,
          ""source"": {
            ""line"": 108,
            ""column"": 68
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 6,
            ""required"": true,
            ""entryTypeInfo"": {
              ""type"": 2,
              ""required"": true
            }
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""TypedYield"": {
      ""compilerRevision"": 19,
      ""generator"": {
        ""kind"": ""enumerable"",
        ""elementType"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""source"": {
        ""name"": ""Probe.TypedYield"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
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
          ""type"": ""forEach"",
          ""binding"": {
            ""id"": ""__sequence:item"",
            ""typeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""readonly"": true,
            ""writability"": ""runtime""
          },
          ""collectionPointer"": {
            ""type"": ""variable"",
            ""variableId"": ""__arg_0__""
          },
          ""collectionTypeInfo"": {
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            }
          },
          ""instructions"": [
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""item"",
                ""typeInfo"": {
                  ""type"": 22,
                  ""required"": true,
                  ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                  ""typeArguments"": {
                    ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                },
                ""pointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""retainCollectionType"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__sequence:item""
                  },
                  ""collectionType"": {
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
            {
              ""type"": ""yieldReturn"",
              ""pointer"": {
                ""type"": ""conditional"",
                ""condition"": {
                  ""type"": ""isCheck"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""item""
                  },
                  ""checkType"": {
                    ""type"": 22,
                    ""required"": true,
                    ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                    ""typeArguments"": {
                      ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  }
                },
                ""whenTrue"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    },
                    ""value"": 1
                  }
                },
                ""whenFalse"": {
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
              ""yields"": true,
              ""source"": {
                ""line"": 109,
                ""column"": 112
              }
            }
          ],
          ""yields"": true,
          ""source"": {
            ""line"": 109,
            ""column"": 70
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": [""IEnumerable""]
      }
    },
    ""TypedSequenceLoop"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.TypedSequenceLoop"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": {
                      ""type"": 2,
                      ""required"": true
                    }
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
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""count"",
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
            ""line"": 111,
            ""column"": 4
          }
        },
        {
          ""type"": ""forEach"",
          ""binding"": {
            ""id"": ""__sequence:item"",
            ""typeInfo"": {
              ""type"": 6,
              ""required"": true,
              ""entryTypeInfo"": {
                ""type"": 2,
                ""required"": true
              }
            },
            ""readonly"": true,
            ""writability"": ""runtime""
          },
          ""collectionPointer"": {
            ""type"": ""variable"",
            ""variableId"": ""__arg_0__""
          },
          ""collectionTypeInfo"": {
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            }
          },
          ""instructions"": [
            {
              ""type"": ""variable"",
              ""variable"": {
                ""id"": ""item"",
                ""typeInfo"": {
                  ""type"": 22,
                  ""required"": true,
                  ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                  ""typeArguments"": {
                    ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                      ""type"": 2,
                      ""required"": true
                    }
                  }
                },
                ""pointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""retainCollectionType"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__sequence:item""
                  },
                  ""collectionType"": {
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
                          ""variableId"": ""item""
                        },
                        ""checkType"": {
                          ""type"": 22,
                          ""required"": true,
                          ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                          ""typeArguments"": {
                            ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                              ""type"": 2,
                              ""required"": true
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
                  },
                  ""instructions"": [
                    {
                      ""type"": ""return"",
                      ""pointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 1,
                            ""required"": true
                          },
                          ""value"": false
                        }
                      },
                      ""source"": {
                        ""line"": 112,
                        ""column"": 81
                      }
                    }
                  ]
                }
              ],
              ""else"": null,
              ""source"": {
                ""line"": 112,
                ""column"": 48
              }
            },
            {
              ""type"": ""assign"",
              ""target"": {
                ""pointer"": {
                  ""type"": ""variable"",
                  ""variableId"": ""count""
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
                        ""type"": ""variable"",
                        ""variableId"": ""count""
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
                ""line"": 112,
                ""column"": 95
              }
            }
          ],
          ""source"": {
            ""line"": 112,
            ""column"": 4
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
                    ""type"": ""variable"",
                    ""variableId"": ""count""
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
            ""line"": 113,
            ""column"": 4
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": [""IEnumerable""]
      }
    },
    ""ConstraintSum"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.ConstraintSum"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:ConstraintSum"",
            ""index"": 0
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:ConstraintSum"",
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
            ""line"": 115,
            ""column"": 67
          }
        },
        {
          ""type"": ""forEach"",
          ""binding"": {
            ""id"": ""item"",
            ""typeInfo"": {
              ""type"": 2,
              ""required"": true
            },
            ""readonly"": true,
            ""writability"": ""runtime""
          },
          ""collectionPointer"": {
            ""type"": ""variable"",
            ""variableId"": ""__arg_0__""
          },
          ""collectionTypeInfo"": {
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""instructions"": [
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
                        ""type"": ""variable"",
                        ""variableId"": ""item""
                      }
                    ],
                    ""numeric"": ""int""
                  }
                }
              },
              ""source"": {
                ""line"": 115,
                ""column"": 109
              }
            }
          ],
          ""source"": {
            ""line"": 115,
            ""column"": 80
          }
        },
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""variable"",
            ""variableId"": ""sum""
          },
          ""source"": {
            ""line"": 115,
            ""column"": 122
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""QueryZip"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.QueryZip"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__arg_1__"",
          ""typeInfo"": {
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
            ""type"": ""function"",
            ""function"": {
              ""type"": ""collectionQuery"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""queryEnumerable"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_0__""
                  },
                  ""elementType"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                ""op"": ""Zip"",
                ""otherPointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""queryEnumerable"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_1__""
                  },
                  ""elementType"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                ""function"": {
                  ""compilerRevision"": 19,
                  ""source"": {
                    ""uri"": ""Probe.neo"",
                    ""coordinateSpace"": ""file"",
                    ""name"": ""Probe.QueryZip <callback>""
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
                        ""line"": 117,
                        ""column"": 104
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
              ""line"": 117,
              ""column"": 86
            }
          },
          ""source"": {
            ""line"": 117,
            ""column"": 78
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
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""QueryConcat"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.QueryConcat"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              },
              ""value"": null
            }
          }
        },
        {
          ""id"": ""__arg_1__"",
          ""typeInfo"": {
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
            ""type"": ""function"",
            ""function"": {
              ""type"": ""collectionQuery"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""queryEnumerable"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_0__""
                  },
                  ""elementType"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                ""op"": ""Concat"",
                ""otherPointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""queryEnumerable"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_1__""
                  },
                  ""elementType"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              }
            },
            ""source"": {
              ""line"": 118,
              ""column"": 92
            }
          },
          ""source"": {
            ""line"": 118,
            ""column"": 81
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
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""QueryFlatten"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.QueryFlatten"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                    ""type"": 22,
                    ""required"": true,
                    ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                    ""typeArguments"": {
                      ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
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
            ""type"": ""function"",
            ""function"": {
              ""type"": ""collectionQuery"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""queryEnumerable"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_0__""
                  },
                  ""elementType"": {
                    ""type"": 22,
                    ""required"": true,
                    ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                    ""typeArguments"": {
                      ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  }
                },
                ""op"": ""SelectMany"",
                ""function"": {
                  ""compilerRevision"": 19,
                  ""source"": {
                    ""uri"": ""Probe.neo"",
                    ""coordinateSpace"": ""file"",
                    ""name"": ""Probe.QueryFlatten <callback>""
                  },
                  ""parameters"": [
                    {
                      ""id"": ""source"",
                      ""typeInfo"": {
                        ""type"": 22,
                        ""required"": true,
                        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                        ""typeArguments"": {
                          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        }
                      },
                      ""pointer"": {
                        ""type"": ""value"",
                        ""value"": {
                          ""typeInfo"": {
                            ""type"": 22,
                            ""required"": true,
                            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                            ""typeArguments"": {
                              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
                        ""variableId"": ""source""
                      },
                      ""source"": {
                        ""line"": 119,
                        ""column"": 105
                      }
                    }
                  ],
                  ""typeInfo"": {
                    ""type"": 22,
                    ""required"": true,
                    ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                    ""typeArguments"": {
                      ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                        ""type"": 2,
                        ""required"": true
                      }
                    }
                  }
                }
              }
            },
            ""source"": {
              ""line"": 119,
              ""column"": 92
            }
          },
          ""source"": {
            ""line"": 119,
            ""column"": 74
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
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""QueryAny"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.QueryAny"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
            ""type"": ""function"",
            ""function"": {
              ""type"": ""collectionQuery"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""queryEnumerable"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_0__""
                  },
                  ""elementType"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                ""op"": ""Any"",
                ""function"": {
                  ""compilerRevision"": 19,
                  ""source"": {
                    ""uri"": ""Probe.neo"",
                    ""coordinateSpace"": ""file"",
                    ""name"": ""Probe.QueryAny <callback>""
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
                      ""source"": {
                        ""line"": 120,
                        ""column"": 69
                      }
                    }
                  ],
                  ""typeInfo"": {
                    ""type"": 1,
                    ""required"": true
                  }
                }
              }
            },
            ""source"": {
              ""line"": 120,
              ""column"": 61
            }
          },
          ""source"": {
            ""line"": 120,
            ""column"": 51
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""QueryCount"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.QueryCount"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
            ""type"": ""function"",
            ""function"": {
              ""type"": ""count"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""queryEnumerable"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_0__""
                  },
                  ""elementType"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              }
            },
            ""source"": {
              ""line"": 121,
              ""column"": 64
            }
          },
          ""source"": {
            ""line"": 121,
            ""column"": 52
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""QueryFirst"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.QueryFirst"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
            ""type"": ""function"",
            ""function"": {
              ""type"": ""first"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""queryEnumerable"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_0__""
                  },
                  ""elementType"": {
                    ""type"": 2,
                    ""required"": true
                  }
                }
              }
            },
            ""source"": {
              ""line"": 122,
              ""column"": 64
            }
          },
          ""source"": {
            ""line"": 122,
            ""column"": 52
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""QueryConstrained"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.QueryConstrained"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:QueryConstrained"",
            ""index"": 0
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 21,
                ""required"": true,
                ""ownerMemberId"": ""pending:member:pending:class:Probe.neo:Probe:QueryConstrained"",
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
              ""type"": ""collectionQuery"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""queryEnumerable"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""__arg_0__""
                  },
                  ""elementType"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                ""op"": ""Sum"",
                ""keyType"": ""int"",
                ""nullable"": false
              }
            },
            ""source"": {
              ""line"": 123,
              ""column"": 81
            }
          },
          ""source"": {
            ""line"": 123,
            ""column"": 71
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""QueryDictionary"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.QueryDictionary"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 5,
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
                ""type"": 5,
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
            ""type"": ""function"",
            ""function"": {
              ""type"": ""collectionQuery"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""function"",
                  ""function"": {
                    ""type"": ""where"",
                    ""info"": {
                      ""collectionPointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""__arg_0__""
                      },
                      ""function"": {
                        ""compilerRevision"": 19,
                        ""source"": {
                          ""uri"": ""Probe.neo"",
                          ""coordinateSpace"": ""file"",
                          ""name"": ""Probe.QueryDictionary <callback>""
                        },
                        ""parameters"": [
                          {
                            ""id"": ""key"",
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
                            ""id"": ""value"",
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
                                ""type"": ""boolean"",
                                ""expression"": {
                                  ""condition"": {
                                    ""type"": ""equalTo"",
                                    ""operand1"": {
                                      ""type"": ""variable"",
                                      ""variableId"": ""key""
                                    },
                                    ""operand2"": {
                                      ""type"": ""value"",
                                      ""value"": {
                                        ""typeInfo"": {
                                          ""type"": 3,
                                          ""required"": true
                                        },
                                        ""value"": ""a""
                                      }
                                    }
                                  }
                                }
                              }
                            },
                            ""source"": {
                              ""line"": 124,
                              ""column"": 93
                            }
                          }
                        ],
                        ""typeInfo"": {
                          ""type"": 1,
                          ""required"": true
                        }
                      }
                    }
                  },
                  ""source"": {
                    ""line"": 124,
                    ""column"": 76
                  }
                },
                ""op"": ""Sum"",
                ""keyType"": ""int"",
                ""nullable"": false,
                ""function"": {
                  ""compilerRevision"": 19,
                  ""source"": {
                    ""uri"": ""Probe.neo"",
                    ""coordinateSpace"": ""file"",
                    ""name"": ""Probe.QueryDictionary <callback>""
                  },
                  ""parameters"": [
                    {
                      ""id"": ""key"",
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
                      ""id"": ""value"",
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
                        ""variableId"": ""value""
                      },
                      ""source"": {
                        ""line"": 124,
                        ""column"": 125
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
              ""line"": 124,
              ""column"": 108
            }
          },
          ""source"": {
            ""line"": 124,
            ""column"": 64
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [""builtin:Root"", ""pending:class:Probe.neo:Probe""],
        ""typeNames"": []
      }
    },
    ""QueryDictionarySequence"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.QueryDictionarySequence"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 5,
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
                ""type"": 5,
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
          ""type"": ""variable"",
          ""variable"": {
            ""id"": ""values"",
            ""typeInfo"": {
              ""type"": 22,
              ""required"": true,
              ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
              ""typeArguments"": {
                ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            },
            ""pointer"": {
              ""type"": ""sequenceConversion"",
              ""operation"": ""retainCollectionType"",
              ""pointer"": {
                ""type"": ""variable"",
                ""variableId"": ""__arg_0__""
              },
              ""collectionType"": {
                ""type"": 5,
                ""required"": true,
                ""entryTypeInfo"": {
                  ""type"": 2,
                  ""required"": true
                }
              }
            }
          },
          ""source"": {
            ""line"": 125,
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
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""queryEnumerable"",
                  ""pointer"": {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""where"",
                      ""info"": {
                        ""collectionPointer"": {
                          ""type"": ""sequenceConversion"",
                          ""operation"": ""queryEnumerable"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""values""
                          },
                          ""elementType"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        },
                        ""function"": {
                          ""compilerRevision"": 19,
                          ""source"": {
                            ""uri"": ""Probe.neo"",
                            ""coordinateSpace"": ""file"",
                            ""name"": ""Probe.QueryDictionarySequence <callback>""
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
                              ""source"": {
                                ""line"": 125,
                                ""column"": 132
                              }
                            }
                          ],
                          ""typeInfo"": {
                            ""type"": 1,
                            ""required"": true
                          }
                        }
                      }
                    },
                    ""source"": {
                      ""line"": 125,
                      ""column"": 124
                    }
                  },
                  ""elementType"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                ""op"": ""Sum"",
                ""keyType"": ""int"",
                ""nullable"": false
              }
            },
            ""source"": {
              ""line"": 125,
              ""column"": 142
            }
          },
          ""source"": {
            ""line"": 125,
            ""column"": 105
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
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": [""IEnumerable""]
      }
    },
    ""QueryList"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.QueryList"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": ""function"",
            ""function"": {
              ""type"": ""collectionQuery"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""queryEnumerable"",
                  ""pointer"": {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""where"",
                      ""info"": {
                        ""collectionPointer"": {
                          ""type"": ""sequenceConversion"",
                          ""operation"": ""queryEnumerable"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""__arg_0__""
                          },
                          ""elementType"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        },
                        ""function"": {
                          ""compilerRevision"": 19,
                          ""source"": {
                            ""uri"": ""Probe.neo"",
                            ""coordinateSpace"": ""file"",
                            ""name"": ""Probe.QueryList <callback>""
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
                                          ""value"": 1
                                        }
                                      }
                                    }
                                  }
                                }
                              },
                              ""source"": {
                                ""line"": 126,
                                ""column"": 64
                              }
                            }
                          ],
                          ""typeInfo"": {
                            ""type"": 1,
                            ""required"": true
                          }
                        }
                      }
                    },
                    ""source"": {
                      ""line"": 126,
                      ""column"": 56
                    }
                  },
                  ""elementType"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                ""op"": ""Sum"",
                ""keyType"": ""int"",
                ""nullable"": false
              }
            },
            ""source"": {
              ""line"": 126,
              ""column"": 74
            }
          },
          ""source"": {
            ""line"": 126,
            ""column"": 44
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 2,
        ""required"": true
      },
      ""dependencies"": {
        ""recordIds"": [""builtin:Root"", ""pending:class:Probe.neo:Probe""],
        ""typeNames"": []
      }
    },
    ""QueryShortCircuit"": {
      ""compilerRevision"": 19,
      ""generator"": {
        ""kind"": ""enumerable"",
        ""elementType"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""source"": {
        ""name"": ""Probe.QueryShortCircuit"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
          ""type"": ""yieldReturn"",
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
          ""yields"": true,
          ""source"": {
            ""line"": 127,
            ""column"": 48
          }
        },
        {
          ""type"": ""throw"",
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 3,
                ""required"": true
              },
              ""value"": ""resumed""
            }
          },
          ""source"": {
            ""line"": 127,
            ""column"": 64
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""QueryCleanup"": {
      ""compilerRevision"": 19,
      ""generator"": {
        ""kind"": ""enumerable"",
        ""elementType"": {
          ""type"": 2,
          ""required"": true
        }
      },
      ""source"": {
        ""name"": ""Probe.QueryCleanup"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
          ""type"": ""try"",
          ""instructions"": [
            {
              ""type"": ""yieldReturn"",
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
              ""yields"": true,
              ""source"": {
                ""line"": 128,
                ""column"": 49
              }
            },
            {
              ""type"": ""yieldReturn"",
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
              ""yields"": true,
              ""source"": {
                ""line"": 128,
                ""column"": 65
              }
            }
          ],
          ""catches"": [],
          ""finallyInstructions"": [
            {
              ""type"": ""throw"",
              ""pointer"": {
                ""type"": ""value"",
                ""value"": {
                  ""typeInfo"": {
                    ""type"": 3,
                    ""required"": true
                  },
                  ""value"": ""query cleanup""
                }
              },
              ""source"": {
                ""line"": 128,
                ""column"": 93
              }
            }
          ],
          ""yields"": true,
          ""source"": {
            ""line"": 128,
            ""column"": 43
          }
        }
      ],
      ""typeInfo"": {
        ""type"": 22,
        ""required"": true,
        ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
        ""typeArguments"": {
          ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
            ""type"": 2,
            ""required"": true
          }
        }
      },
      ""dependencies"": {
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""QueryOrdered"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.QueryOrdered"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""type"": 22,
            ""required"": true,
            ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
            ""typeArguments"": {
              ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
                ""type"": 2,
                ""required"": true
              }
            }
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 22,
                ""required"": true,
                ""interfaceId"": ""system_d1413390-a2a6-4137-a8f8-e931da847345"",
                ""typeArguments"": {
                  ""system_b0cbeea1-21c9-4164-b6de-20f4f3fbbfa2"": {
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
            ""type"": ""function"",
            ""function"": {
              ""type"": ""collectionQuery"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""queryEnumerable"",
                  ""pointer"": {
                    ""type"": ""function"",
                    ""function"": {
                      ""type"": ""collectionQuery"",
                      ""info"": {
                        ""collectionPointer"": {
                          ""type"": ""sequenceConversion"",
                          ""operation"": ""queryEnumerable"",
                          ""pointer"": {
                            ""type"": ""variable"",
                            ""variableId"": ""__arg_0__""
                          },
                          ""elementType"": {
                            ""type"": 2,
                            ""required"": true
                          }
                        },
                        ""op"": ""OrderBy"",
                        ""function"": {
                          ""compilerRevision"": 19,
                          ""source"": {
                            ""uri"": ""Probe.neo"",
                            ""coordinateSpace"": ""file"",
                            ""name"": ""Probe.QueryOrdered <callback>""
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
                                          ""value"": 2
                                        }
                                      }
                                    ],
                                    ""numeric"": ""int""
                                  }
                                }
                              },
                              ""source"": {
                                ""line"": 129,
                                ""column"": 82
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
                      ""line"": 129,
                      ""column"": 74
                    }
                  },
                  ""elementType"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                ""op"": ""ThenByDescending"",
                ""function"": {
                  ""compilerRevision"": 19,
                  ""source"": {
                    ""uri"": ""Probe.neo"",
                    ""coordinateSpace"": ""file"",
                    ""name"": ""Probe.QueryOrdered <callback>""
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
                        ""line"": 129,
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
              ""line"": 129,
              ""column"": 105
            }
          },
          ""source"": {
            ""line"": 129,
            ""column"": 60
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
        ""recordIds"": [
          ""builtin:Root"",
          ""pending:class:Probe.neo:Probe"",
          ""system_d1413390-a2a6-4137-a8f8-e931da847345""
        ],
        ""typeNames"": []
      }
    },
    ""QueryMutation"": {
      ""compilerRevision"": 19,
      ""source"": {
        ""name"": ""Probe.QueryMutation"",
        ""uri"": ""Probe.neo"",
        ""coordinateSpace"": ""file""
      },
      ""parameters"": [
        {
          ""id"": ""__this__"",
          ""typeInfo"": {
            ""type"": 7,
            ""required"": true,
            ""classId"": ""pending:class:Probe.neo:Probe""
          },
          ""pointer"": {
            ""type"": ""value"",
            ""value"": {
              ""typeInfo"": {
                ""type"": 7,
                ""required"": true,
                ""classId"": ""pending:class:Probe.neo:Probe""
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
            ""id"": ""items"",
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
            }
          },
          ""source"": {
            ""line"": 130,
            ""column"": 37
          }
        },
        {
          ""type"": ""return"",
          ""pointer"": {
            ""type"": ""function"",
            ""function"": {
              ""type"": ""select"",
              ""info"": {
                ""collectionPointer"": {
                  ""type"": ""sequenceConversion"",
                  ""operation"": ""queryEnumerable"",
                  ""pointer"": {
                    ""type"": ""variable"",
                    ""variableId"": ""items""
                  },
                  ""elementType"": {
                    ""type"": 2,
                    ""required"": true
                  }
                },
                ""function"": {
                  ""compilerRevision"": 19,
                  ""source"": {
                    ""uri"": ""Probe.neo"",
                    ""coordinateSpace"": ""file"",
                    ""name"": ""Probe.QueryMutation <callback>""
                  },
                  ""parameters"": [
                    {
                      ""id"": ""item"",
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
                          ""variableId"": ""items""
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
                          ""type"": ""variable"",
                          ""variableId"": ""item""
                        }
                      ],
                      ""source"": {
                        ""line"": 130,
                        ""column"": 98
                      }
                    },
                    {
                      ""type"": ""return"",
                      ""pointer"": {
                        ""type"": ""variable"",
                        ""variableId"": ""item""
                      },
                      ""source"": {
                        ""line"": 130,
                        ""column"": 118
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
              ""line"": 130,
              ""column"": 85
            }
          },
          ""source"": {
            ""line"": 130,
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
        ""recordIds"": [""builtin:Root"", ""pending:class:Probe.neo:Probe""],
        ""typeNames"": []
      }
    }
  }
}";
    }
}
