// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
// VENDORED — DO NOT HAND-EDIT.
// Source: neo-compose/src/models/neoscript/bitwise-parity-fixture.json
namespace NeoCompose.Tests
{
    internal static class NeoScriptBitwiseParityFixture
    {
        public const string Json = @"[
  {
    ""name"": ""complement zero"",
    ""op"": ""not"",
    ""args"": [0],
    ""expected"": -1
  },
  {
    ""name"": ""complement all bits"",
    ""op"": ""not"",
    ""args"": [-1],
    ""expected"": 0
  },
  {
    ""name"": ""and"",
    ""op"": ""and"",
    ""args"": [5, 3],
    ""expected"": 1
  },
  {
    ""name"": ""or"",
    ""op"": ""or"",
    ""args"": [5, 2],
    ""expected"": 7
  },
  {
    ""name"": ""xor"",
    ""op"": ""xor"",
    ""args"": [5, 3],
    ""expected"": 6
  },
  {
    ""name"": ""shiftLeft 0 by 0"",
    ""op"": ""shiftLeft"",
    ""args"": [0, 0],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight 0 by 0"",
    ""op"": ""shiftRight"",
    ""args"": [0, 0],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 0 by 0"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [0, 0],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 0 by 1"",
    ""op"": ""shiftLeft"",
    ""args"": [0, 1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight 0 by 1"",
    ""op"": ""shiftRight"",
    ""args"": [0, 1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 0 by 1"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [0, 1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 0 by 31"",
    ""op"": ""shiftLeft"",
    ""args"": [0, 31],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight 0 by 31"",
    ""op"": ""shiftRight"",
    ""args"": [0, 31],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 0 by 31"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [0, 31],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 0 by 32"",
    ""op"": ""shiftLeft"",
    ""args"": [0, 32],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight 0 by 32"",
    ""op"": ""shiftRight"",
    ""args"": [0, 32],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 0 by 32"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [0, 32],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 0 by 33"",
    ""op"": ""shiftLeft"",
    ""args"": [0, 33],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight 0 by 33"",
    ""op"": ""shiftRight"",
    ""args"": [0, 33],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 0 by 33"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [0, 33],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 0 by -1"",
    ""op"": ""shiftLeft"",
    ""args"": [0, -1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight 0 by -1"",
    ""op"": ""shiftRight"",
    ""args"": [0, -1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 0 by -1"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [0, -1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 0 by -32"",
    ""op"": ""shiftLeft"",
    ""args"": [0, -32],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight 0 by -32"",
    ""op"": ""shiftRight"",
    ""args"": [0, -32],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 0 by -32"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [0, -32],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 1 by 0"",
    ""op"": ""shiftLeft"",
    ""args"": [1, 0],
    ""expected"": 1
  },
  {
    ""name"": ""shiftRight 1 by 0"",
    ""op"": ""shiftRight"",
    ""args"": [1, 0],
    ""expected"": 1
  },
  {
    ""name"": ""shiftRightUnsigned 1 by 0"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [1, 0],
    ""expected"": 1
  },
  {
    ""name"": ""shiftLeft 1 by 1"",
    ""op"": ""shiftLeft"",
    ""args"": [1, 1],
    ""expected"": 2
  },
  {
    ""name"": ""shiftRight 1 by 1"",
    ""op"": ""shiftRight"",
    ""args"": [1, 1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 1 by 1"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [1, 1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 1 by 31"",
    ""op"": ""shiftLeft"",
    ""args"": [1, 31],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftRight 1 by 31"",
    ""op"": ""shiftRight"",
    ""args"": [1, 31],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 1 by 31"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [1, 31],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 1 by 32"",
    ""op"": ""shiftLeft"",
    ""args"": [1, 32],
    ""expected"": 1
  },
  {
    ""name"": ""shiftRight 1 by 32"",
    ""op"": ""shiftRight"",
    ""args"": [1, 32],
    ""expected"": 1
  },
  {
    ""name"": ""shiftRightUnsigned 1 by 32"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [1, 32],
    ""expected"": 1
  },
  {
    ""name"": ""shiftLeft 1 by 33"",
    ""op"": ""shiftLeft"",
    ""args"": [1, 33],
    ""expected"": 2
  },
  {
    ""name"": ""shiftRight 1 by 33"",
    ""op"": ""shiftRight"",
    ""args"": [1, 33],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 1 by 33"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [1, 33],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 1 by -1"",
    ""op"": ""shiftLeft"",
    ""args"": [1, -1],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftRight 1 by -1"",
    ""op"": ""shiftRight"",
    ""args"": [1, -1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 1 by -1"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [1, -1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 1 by -32"",
    ""op"": ""shiftLeft"",
    ""args"": [1, -32],
    ""expected"": 1
  },
  {
    ""name"": ""shiftRight 1 by -32"",
    ""op"": ""shiftRight"",
    ""args"": [1, -32],
    ""expected"": 1
  },
  {
    ""name"": ""shiftRightUnsigned 1 by -32"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [1, -32],
    ""expected"": 1
  },
  {
    ""name"": ""shiftLeft -1 by 0"",
    ""op"": ""shiftLeft"",
    ""args"": [-1, 0],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRight -1 by 0"",
    ""op"": ""shiftRight"",
    ""args"": [-1, 0],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -1 by 0"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-1, 0],
    ""expected"": -1
  },
  {
    ""name"": ""shiftLeft -1 by 1"",
    ""op"": ""shiftLeft"",
    ""args"": [-1, 1],
    ""expected"": -2
  },
  {
    ""name"": ""shiftRight -1 by 1"",
    ""op"": ""shiftRight"",
    ""args"": [-1, 1],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -1 by 1"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-1, 1],
    ""expected"": 2147483647
  },
  {
    ""name"": ""shiftLeft -1 by 31"",
    ""op"": ""shiftLeft"",
    ""args"": [-1, 31],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftRight -1 by 31"",
    ""op"": ""shiftRight"",
    ""args"": [-1, 31],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -1 by 31"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-1, 31],
    ""expected"": 1
  },
  {
    ""name"": ""shiftLeft -1 by 32"",
    ""op"": ""shiftLeft"",
    ""args"": [-1, 32],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRight -1 by 32"",
    ""op"": ""shiftRight"",
    ""args"": [-1, 32],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -1 by 32"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-1, 32],
    ""expected"": -1
  },
  {
    ""name"": ""shiftLeft -1 by 33"",
    ""op"": ""shiftLeft"",
    ""args"": [-1, 33],
    ""expected"": -2
  },
  {
    ""name"": ""shiftRight -1 by 33"",
    ""op"": ""shiftRight"",
    ""args"": [-1, 33],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -1 by 33"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-1, 33],
    ""expected"": 2147483647
  },
  {
    ""name"": ""shiftLeft -1 by -1"",
    ""op"": ""shiftLeft"",
    ""args"": [-1, -1],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftRight -1 by -1"",
    ""op"": ""shiftRight"",
    ""args"": [-1, -1],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -1 by -1"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-1, -1],
    ""expected"": 1
  },
  {
    ""name"": ""shiftLeft -1 by -32"",
    ""op"": ""shiftLeft"",
    ""args"": [-1, -32],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRight -1 by -32"",
    ""op"": ""shiftRight"",
    ""args"": [-1, -32],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -1 by -32"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-1, -32],
    ""expected"": -1
  },
  {
    ""name"": ""shiftLeft 2147483647 by 0"",
    ""op"": ""shiftLeft"",
    ""args"": [2147483647, 0],
    ""expected"": 2147483647
  },
  {
    ""name"": ""shiftRight 2147483647 by 0"",
    ""op"": ""shiftRight"",
    ""args"": [2147483647, 0],
    ""expected"": 2147483647
  },
  {
    ""name"": ""shiftRightUnsigned 2147483647 by 0"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [2147483647, 0],
    ""expected"": 2147483647
  },
  {
    ""name"": ""shiftLeft 2147483647 by 1"",
    ""op"": ""shiftLeft"",
    ""args"": [2147483647, 1],
    ""expected"": -2
  },
  {
    ""name"": ""shiftRight 2147483647 by 1"",
    ""op"": ""shiftRight"",
    ""args"": [2147483647, 1],
    ""expected"": 1073741823
  },
  {
    ""name"": ""shiftRightUnsigned 2147483647 by 1"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [2147483647, 1],
    ""expected"": 1073741823
  },
  {
    ""name"": ""shiftLeft 2147483647 by 31"",
    ""op"": ""shiftLeft"",
    ""args"": [2147483647, 31],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftRight 2147483647 by 31"",
    ""op"": ""shiftRight"",
    ""args"": [2147483647, 31],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 2147483647 by 31"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [2147483647, 31],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 2147483647 by 32"",
    ""op"": ""shiftLeft"",
    ""args"": [2147483647, 32],
    ""expected"": 2147483647
  },
  {
    ""name"": ""shiftRight 2147483647 by 32"",
    ""op"": ""shiftRight"",
    ""args"": [2147483647, 32],
    ""expected"": 2147483647
  },
  {
    ""name"": ""shiftRightUnsigned 2147483647 by 32"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [2147483647, 32],
    ""expected"": 2147483647
  },
  {
    ""name"": ""shiftLeft 2147483647 by 33"",
    ""op"": ""shiftLeft"",
    ""args"": [2147483647, 33],
    ""expected"": -2
  },
  {
    ""name"": ""shiftRight 2147483647 by 33"",
    ""op"": ""shiftRight"",
    ""args"": [2147483647, 33],
    ""expected"": 1073741823
  },
  {
    ""name"": ""shiftRightUnsigned 2147483647 by 33"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [2147483647, 33],
    ""expected"": 1073741823
  },
  {
    ""name"": ""shiftLeft 2147483647 by -1"",
    ""op"": ""shiftLeft"",
    ""args"": [2147483647, -1],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftRight 2147483647 by -1"",
    ""op"": ""shiftRight"",
    ""args"": [2147483647, -1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 2147483647 by -1"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [2147483647, -1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 2147483647 by -32"",
    ""op"": ""shiftLeft"",
    ""args"": [2147483647, -32],
    ""expected"": 2147483647
  },
  {
    ""name"": ""shiftRight 2147483647 by -32"",
    ""op"": ""shiftRight"",
    ""args"": [2147483647, -32],
    ""expected"": 2147483647
  },
  {
    ""name"": ""shiftRightUnsigned 2147483647 by -32"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [2147483647, -32],
    ""expected"": 2147483647
  },
  {
    ""name"": ""shiftLeft -2147483648 by 0"",
    ""op"": ""shiftLeft"",
    ""args"": [-2147483648, 0],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftRight -2147483648 by 0"",
    ""op"": ""shiftRight"",
    ""args"": [-2147483648, 0],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftRightUnsigned -2147483648 by 0"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-2147483648, 0],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftLeft -2147483648 by 1"",
    ""op"": ""shiftLeft"",
    ""args"": [-2147483648, 1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight -2147483648 by 1"",
    ""op"": ""shiftRight"",
    ""args"": [-2147483648, 1],
    ""expected"": -1073741824
  },
  {
    ""name"": ""shiftRightUnsigned -2147483648 by 1"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-2147483648, 1],
    ""expected"": 1073741824
  },
  {
    ""name"": ""shiftLeft -2147483648 by 31"",
    ""op"": ""shiftLeft"",
    ""args"": [-2147483648, 31],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight -2147483648 by 31"",
    ""op"": ""shiftRight"",
    ""args"": [-2147483648, 31],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -2147483648 by 31"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-2147483648, 31],
    ""expected"": 1
  },
  {
    ""name"": ""shiftLeft -2147483648 by 32"",
    ""op"": ""shiftLeft"",
    ""args"": [-2147483648, 32],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftRight -2147483648 by 32"",
    ""op"": ""shiftRight"",
    ""args"": [-2147483648, 32],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftRightUnsigned -2147483648 by 32"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-2147483648, 32],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftLeft -2147483648 by 33"",
    ""op"": ""shiftLeft"",
    ""args"": [-2147483648, 33],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight -2147483648 by 33"",
    ""op"": ""shiftRight"",
    ""args"": [-2147483648, 33],
    ""expected"": -1073741824
  },
  {
    ""name"": ""shiftRightUnsigned -2147483648 by 33"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-2147483648, 33],
    ""expected"": 1073741824
  },
  {
    ""name"": ""shiftLeft -2147483648 by -1"",
    ""op"": ""shiftLeft"",
    ""args"": [-2147483648, -1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight -2147483648 by -1"",
    ""op"": ""shiftRight"",
    ""args"": [-2147483648, -1],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -2147483648 by -1"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-2147483648, -1],
    ""expected"": 1
  },
  {
    ""name"": ""shiftLeft -2147483648 by -32"",
    ""op"": ""shiftLeft"",
    ""args"": [-2147483648, -32],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftRight -2147483648 by -32"",
    ""op"": ""shiftRight"",
    ""args"": [-2147483648, -32],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftRightUnsigned -2147483648 by -32"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-2147483648, -32],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftLeft 4294967296 by 0"",
    ""op"": ""shiftLeft"",
    ""args"": [4294967296, 0],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight 4294967296 by 0"",
    ""op"": ""shiftRight"",
    ""args"": [4294967296, 0],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 4294967296 by 0"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [4294967296, 0],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 4294967296 by 1"",
    ""op"": ""shiftLeft"",
    ""args"": [4294967296, 1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight 4294967296 by 1"",
    ""op"": ""shiftRight"",
    ""args"": [4294967296, 1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 4294967296 by 1"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [4294967296, 1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 4294967296 by 31"",
    ""op"": ""shiftLeft"",
    ""args"": [4294967296, 31],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight 4294967296 by 31"",
    ""op"": ""shiftRight"",
    ""args"": [4294967296, 31],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 4294967296 by 31"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [4294967296, 31],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 4294967296 by 32"",
    ""op"": ""shiftLeft"",
    ""args"": [4294967296, 32],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight 4294967296 by 32"",
    ""op"": ""shiftRight"",
    ""args"": [4294967296, 32],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 4294967296 by 32"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [4294967296, 32],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 4294967296 by 33"",
    ""op"": ""shiftLeft"",
    ""args"": [4294967296, 33],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight 4294967296 by 33"",
    ""op"": ""shiftRight"",
    ""args"": [4294967296, 33],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 4294967296 by 33"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [4294967296, 33],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 4294967296 by -1"",
    ""op"": ""shiftLeft"",
    ""args"": [4294967296, -1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight 4294967296 by -1"",
    ""op"": ""shiftRight"",
    ""args"": [4294967296, -1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 4294967296 by -1"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [4294967296, -1],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft 4294967296 by -32"",
    ""op"": ""shiftLeft"",
    ""args"": [4294967296, -32],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRight 4294967296 by -32"",
    ""op"": ""shiftRight"",
    ""args"": [4294967296, -32],
    ""expected"": 0
  },
  {
    ""name"": ""shiftRightUnsigned 4294967296 by -32"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [4294967296, -32],
    ""expected"": 0
  },
  {
    ""name"": ""shiftLeft -4294967297 by 0"",
    ""op"": ""shiftLeft"",
    ""args"": [-4294967297, 0],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRight -4294967297 by 0"",
    ""op"": ""shiftRight"",
    ""args"": [-4294967297, 0],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -4294967297 by 0"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-4294967297, 0],
    ""expected"": -1
  },
  {
    ""name"": ""shiftLeft -4294967297 by 1"",
    ""op"": ""shiftLeft"",
    ""args"": [-4294967297, 1],
    ""expected"": -2
  },
  {
    ""name"": ""shiftRight -4294967297 by 1"",
    ""op"": ""shiftRight"",
    ""args"": [-4294967297, 1],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -4294967297 by 1"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-4294967297, 1],
    ""expected"": 2147483647
  },
  {
    ""name"": ""shiftLeft -4294967297 by 31"",
    ""op"": ""shiftLeft"",
    ""args"": [-4294967297, 31],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftRight -4294967297 by 31"",
    ""op"": ""shiftRight"",
    ""args"": [-4294967297, 31],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -4294967297 by 31"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-4294967297, 31],
    ""expected"": 1
  },
  {
    ""name"": ""shiftLeft -4294967297 by 32"",
    ""op"": ""shiftLeft"",
    ""args"": [-4294967297, 32],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRight -4294967297 by 32"",
    ""op"": ""shiftRight"",
    ""args"": [-4294967297, 32],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -4294967297 by 32"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-4294967297, 32],
    ""expected"": -1
  },
  {
    ""name"": ""shiftLeft -4294967297 by 33"",
    ""op"": ""shiftLeft"",
    ""args"": [-4294967297, 33],
    ""expected"": -2
  },
  {
    ""name"": ""shiftRight -4294967297 by 33"",
    ""op"": ""shiftRight"",
    ""args"": [-4294967297, 33],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -4294967297 by 33"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-4294967297, 33],
    ""expected"": 2147483647
  },
  {
    ""name"": ""shiftLeft -4294967297 by -1"",
    ""op"": ""shiftLeft"",
    ""args"": [-4294967297, -1],
    ""expected"": -2147483648
  },
  {
    ""name"": ""shiftRight -4294967297 by -1"",
    ""op"": ""shiftRight"",
    ""args"": [-4294967297, -1],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -4294967297 by -1"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-4294967297, -1],
    ""expected"": 1
  },
  {
    ""name"": ""shiftLeft -4294967297 by -32"",
    ""op"": ""shiftLeft"",
    ""args"": [-4294967297, -32],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRight -4294967297 by -32"",
    ""op"": ""shiftRight"",
    ""args"": [-4294967297, -32],
    ""expected"": -1
  },
  {
    ""name"": ""shiftRightUnsigned -4294967297 by -32"",
    ""op"": ""shiftRightUnsigned"",
    ""args"": [-4294967297, -32],
    ""expected"": -1
  },
  {
    ""name"": ""normalize 2147483648"",
    ""op"": ""or"",
    ""args"": [2147483648, 1],
    ""expected"": -2147483647
  },
  {
    ""name"": ""normalize 4294967295"",
    ""op"": ""or"",
    ""args"": [4294967295, 1],
    ""expected"": -1
  },
  {
    ""name"": ""normalize 4294967296"",
    ""op"": ""or"",
    ""args"": [4294967296, 1],
    ""expected"": 1
  },
  {
    ""name"": ""normalize -4294967297"",
    ""op"": ""or"",
    ""args"": [-4294967297, 1],
    ""expected"": -1
  },
  {
    ""name"": ""normalize 9007199254740992"",
    ""op"": ""or"",
    ""args"": [9007199254740992, 1],
    ""expected"": 1
  },
  {
    ""name"": ""normalize -9007199254740992"",
    ""op"": ""or"",
    ""args"": [-9007199254740992, 1],
    ""expected"": 1
  }
]";
    }
}
