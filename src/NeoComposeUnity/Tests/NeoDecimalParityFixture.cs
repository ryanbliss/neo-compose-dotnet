// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
//
// VENDORED — DO NOT HAND-EDIT.
// Source of record: neo-compose/src/models/decimal/decimal-parity-fixture.json
// (specs/decimal-attribute.md §8). This is a verbatim copy embedded as a
// C# constant so the Unity Test Runner can consume the cross-runtime decimal
// parity vectors without asset-loading. Re-vendor this file whenever the web
// fixture regenerates (regenerate the web fixture, then copy it here).

#nullable enable

namespace NeoCompose.Tests
{
    /// <summary>
    /// The cross-runtime decimal parity fixture, vendored from the web repo.
    /// Consumed by <see cref="NeoDecimalMathTests"/>.
    /// </summary>
    public static class NeoDecimalParityFixture
    {
        public const string Json = @"{
  ""$comment"": ""Cross-runtime decimal parity fixture (specs/decimal-attribute.md \u00a78). Generated against Python's decimal module as an independent oracle (IEEE 754-2008 decimal, ROUND_HALF_EVEN). Consumed by src/models/decimal/decimal-math.test.ts (web) and NeoDecimalMathTests (neo-compose-dotnet, vendored copy). Regenerate rather than hand-edit."",
  ""vectors"": [
    {
      ""op"": ""add"",
      ""a"": ""0.1"",
      ""b"": ""0.2"",
      ""expected"": ""0.3""
    },
    {
      ""op"": ""add"",
      ""a"": ""1.10"",
      ""b"": ""0.90"",
      ""expected"": ""2.00""
    },
    {
      ""op"": ""add"",
      ""a"": ""0.9999999999999999999999999999"",
      ""b"": ""0.0000000000000000000000000001"",
      ""expectedError"": ""overflow""
    },
    {
      ""op"": ""add"",
      ""a"": ""-1.5"",
      ""b"": ""1.5"",
      ""expected"": ""0.0""
    },
    {
      ""op"": ""add"",
      ""a"": ""9999999999999999999999999999"",
      ""b"": ""1"",
      ""expectedError"": ""overflow""
    },
    {
      ""op"": ""add"",
      ""a"": ""0.05"",
      ""b"": ""0.05"",
      ""expected"": ""0.10""
    },
    {
      ""op"": ""subtract"",
      ""a"": ""1"",
      ""b"": ""1.000"",
      ""expected"": ""0.000""
    },
    {
      ""op"": ""subtract"",
      ""a"": ""0.3"",
      ""b"": ""0.1"",
      ""expected"": ""0.2""
    },
    {
      ""op"": ""subtract"",
      ""a"": ""-0.1"",
      ""b"": ""0.2"",
      ""expected"": ""-0.3""
    },
    {
      ""op"": ""subtract"",
      ""a"": ""0"",
      ""b"": ""9999999999999999999999999999"",
      ""expected"": ""-9999999999999999999999999999""
    },
    {
      ""op"": ""multiply"",
      ""a"": ""1.5"",
      ""b"": ""2.5"",
      ""expected"": ""3.75""
    },
    {
      ""op"": ""multiply"",
      ""a"": ""1.10"",
      ""b"": ""2"",
      ""expected"": ""2.20""
    },
    {
      ""op"": ""multiply"",
      ""a"": ""-1.1"",
      ""b"": ""2"",
      ""expected"": ""-2.2""
    },
    {
      ""op"": ""multiply"",
      ""a"": ""0.5"",
      ""b"": ""0.5"",
      ""expected"": ""0.25""
    },
    {
      ""op"": ""multiply"",
      ""a"": ""0"",
      ""b"": ""123456.789"",
      ""expected"": ""0.000""
    },
    {
      ""op"": ""multiply"",
      ""a"": ""10000000000000"",
      ""b"": ""10000000000000000"",
      ""expectedError"": ""overflow""
    },
    {
      ""op"": ""multiply"",
      ""a"": ""0.00000000000001"",
      ""b"": ""0.000000000000001"",
      ""expectedError"": ""overflow""
    },
    {
      ""op"": ""multiply"",
      ""a"": ""1.000000000000001"",
      ""b"": ""1.000000000000001"",
      ""expectedError"": ""overflow""
    },
    {
      ""op"": ""compare"",
      ""a"": ""1.10"",
      ""b"": ""1.1"",
      ""expected"": 0
    },
    {
      ""op"": ""compare"",
      ""a"": ""-0"",
      ""b"": ""0"",
      ""expected"": 0
    },
    {
      ""op"": ""compare"",
      ""a"": ""-1"",
      ""b"": ""1"",
      ""expected"": -1
    },
    {
      ""op"": ""compare"",
      ""a"": ""0.3"",
      ""b"": ""0.29999999999999998"",
      ""expected"": 1
    },
    {
      ""op"": ""compare"",
      ""a"": ""100"",
      ""b"": ""99.9999999999999999999999999"",
      ""expected"": 1
    },
    {
      ""op"": ""compare"",
      ""a"": ""-2.5"",
      ""b"": ""-2.50"",
      ""expected"": 0
    },
    {
      ""op"": ""round"",
      ""a"": ""2.5"",
      ""digits"": 0,
      ""expected"": ""2""
    },
    {
      ""op"": ""round"",
      ""a"": ""3.5"",
      ""digits"": 0,
      ""expected"": ""4""
    },
    {
      ""op"": ""round"",
      ""a"": ""2.25"",
      ""digits"": 1,
      ""expected"": ""2.2""
    },
    {
      ""op"": ""round"",
      ""a"": ""2.35"",
      ""digits"": 1,
      ""expected"": ""2.4""
    },
    {
      ""op"": ""round"",
      ""a"": ""-2.5"",
      ""digits"": 0,
      ""expected"": ""-2""
    },
    {
      ""op"": ""round"",
      ""a"": ""-2.35"",
      ""digits"": 1,
      ""expected"": ""-2.4""
    },
    {
      ""op"": ""round"",
      ""a"": ""1.005"",
      ""digits"": 2,
      ""expected"": ""1.00""
    },
    {
      ""op"": ""round"",
      ""a"": ""1.5"",
      ""digits"": 3,
      ""expected"": ""1.5""
    },
    {
      ""op"": ""round"",
      ""a"": ""0.9999999999999999999999999999"",
      ""digits"": 2,
      ""expected"": ""1.00""
    },
    {
      ""op"": ""round"",
      ""a"": ""123.456"",
      ""digits"": 0,
      ""expected"": ""123""
    },
    {
      ""op"": ""divide"",
      ""a"": ""1"",
      ""b"": ""3"",
      ""digits"": 5,
      ""expected"": ""0.33333""
    },
    {
      ""op"": ""divide"",
      ""a"": ""2"",
      ""b"": ""3"",
      ""digits"": 5,
      ""expected"": ""0.66667""
    },
    {
      ""op"": ""divide"",
      ""a"": ""1"",
      ""b"": ""8"",
      ""digits"": 2,
      ""expected"": ""0.12""
    },
    {
      ""op"": ""divide"",
      ""a"": ""1.5"",
      ""b"": ""0.5"",
      ""digits"": 0,
      ""expected"": ""3""
    },
    {
      ""op"": ""divide"",
      ""a"": ""-1"",
      ""b"": ""3"",
      ""digits"": 4,
      ""expected"": ""-0.3333""
    },
    {
      ""op"": ""divide"",
      ""a"": ""1"",
      ""b"": ""-3"",
      ""digits"": 4,
      ""expected"": ""-0.3333""
    },
    {
      ""op"": ""divide"",
      ""a"": ""10"",
      ""b"": ""4"",
      ""digits"": 1,
      ""expected"": ""2.5""
    },
    {
      ""op"": ""divide"",
      ""a"": ""0.1"",
      ""b"": ""0.3"",
      ""digits"": 28,
      ""expected"": ""0.3333333333333333333333333333""
    },
    {
      ""op"": ""divide"",
      ""a"": ""7"",
      ""b"": ""7"",
      ""digits"": 3,
      ""expected"": ""1.000""
    },
    {
      ""op"": ""divide"",
      ""a"": ""1"",
      ""b"": ""0"",
      ""digits"": 5,
      ""expectedError"": ""divideByZero""
    },
    {
      ""op"": ""divide"",
      ""a"": ""1"",
      ""b"": ""0.00"",
      ""digits"": 5,
      ""expectedError"": ""divideByZero""
    },
    {
      ""op"": ""fromFloat"",
      ""float"": 0.1,
      ""digits"": 20,
      ""expected"": ""0.10000000000000000555""
    },
    {
      ""op"": ""fromFloat"",
      ""float"": 0.1,
      ""digits"": 28,
      ""expected"": ""0.1000000000000000055511151231""
    },
    {
      ""op"": ""fromFloat"",
      ""float"": 0.1,
      ""digits"": 17,
      ""expected"": ""0.10000000000000001""
    },
    {
      ""op"": ""fromFloat"",
      ""float"": 0.3,
      ""digits"": 20,
      ""expected"": ""0.29999999999999998890""
    },
    {
      ""op"": ""fromFloat"",
      ""float"": 0.5,
      ""digits"": 3,
      ""expected"": ""0.5""
    },
    {
      ""op"": ""fromFloat"",
      ""float"": -0.5,
      ""digits"": 1,
      ""expected"": ""-0.5""
    },
    {
      ""op"": ""fromFloat"",
      ""float"": 1e+21,
      ""digits"": 0,
      ""expected"": ""1000000000000000000000""
    },
    {
      ""op"": ""fromFloat"",
      ""float"": 123.456,
      ""digits"": 10,
      ""expected"": ""123.4560000000""
    },
    {
      ""op"": ""fromFloat"",
      ""float"": 0.0,
      ""digits"": 5,
      ""expected"": ""0""
    },
    {
      ""op"": ""fromFloat"",
      ""float"": -0.0,
      ""digits"": 2,
      ""expected"": ""0""
    },
    {
      ""op"": ""fromFloat"",
      ""float"": 1e+30,
      ""digits"": 0,
      ""expectedError"": ""overflow""
    },
    {
      ""op"": ""fromFloat"",
      ""float"": 9.313225746154785e-10,
      ""digits"": 28,
      ""expected"": ""0.0000000009313225746154785156""
    },
    {
      ""op"": ""round"",
      ""a"": ""1.5"",
      ""digits"": 29,
      ""expectedError"": ""digitsRange""
    },
    {
      ""op"": ""round"",
      ""a"": ""1.5"",
      ""digits"": -1,
      ""expectedError"": ""digitsRange""
    },
    {
      ""op"": ""divide"",
      ""a"": ""1"",
      ""b"": ""3"",
      ""digits"": 29,
      ""expectedError"": ""digitsRange""
    },
    {
      ""op"": ""fromFloat"",
      ""float"": 0.5,
      ""digits"": 29,
      ""expectedError"": ""digitsRange""
    }
  ]
}
";
    }
}
