// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §8.4: the shared fixture's vector and color hashes, which the
    /// generator transcribes from Unity's IL, against the real
    /// <c>UnityEngine</c> types.
    /// </summary>
    public class P127HashingUnityTests
    {
        private const string FixturePath = "Packages/com.ryanbliss.neocompose/Tests/hashing-parity.json";

        [Test]
        public void FixtureMatchesUnityGetHashCode()
        {
            var failures = new List<string>();
            foreach (JToken c in (JArray)JObject.Parse(File.ReadAllText(FixturePath))["unity"]!)
            {
                int[] v = c["v"]!.Select(value => (int)value).ToArray();
                float[] f = v.Select(BitConverter.Int32BitsToSingle).ToArray();
                string type = (string)c["type"]!;
                int hash = type switch
                {
                    "Vector2" => new Vector2(f[0], f[1]).GetHashCode(),
                    "Vector3" => new Vector3(f[0], f[1], f[2]).GetHashCode(),
                    "Color" => new Color(f[0], f[1], f[2], f[3]).GetHashCode(),
                    "Vector2Int" => new Vector2Int(v[0], v[1]).GetHashCode(),
                    "Vector3Int" => new Vector3Int(v[0], v[1], v[2]).GetHashCode(),
                    _ => throw new InvalidOperationException(type),
                };
                if (hash != (int)c["hash"]!)
                    failures.Add($"{c.ToString(Newtonsoft.Json.Formatting.None)} got {hash}");
            }
            Assert.That(failures, Is.Empty, $"{failures.Count} mismatches:\n" + string.Join("\n", failures.Take(10)));
        }
    }
}
