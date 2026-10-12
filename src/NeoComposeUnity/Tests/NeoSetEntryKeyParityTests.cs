// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P108 §3. The .NET half of the Set entry key parity gate: every case in
    /// <see cref="NeoSetEntryKeyParityFixture"/> keys the stored row the way
    /// the TypeScript <c>setEntryKey</c> does.
    /// </summary>
    public class NeoSetEntryKeyParityTests
    {
        [Test]
        public void EveryCaseKeysLikeTheWeb()
        {
            var failures = new List<string>();
            foreach (JObject testCase in (JArray)JObject.Parse(NeoSetEntryKeyParityFixture.Json)["cases"]!)
            {
                string name = testCase.Value<string>("name")!;
                string actual = NeoSetEntryKey.Of(EntryMember(testCase.Value<string>("kind")!), Row(testCase));
                string expected = testCase.Value<string>("key")!;
                if (actual != expected)
                    failures.Add($"[{name}] expected '{expected}', got '{actual}'");
            }

            if (failures.Count > 0)
            {
                Assert.Fail(
                    $"{failures.Count} divergence(s) from the shared Set entry key fixture:\n" +
                    string.Join("\n", failures));
            }
        }

        [Test]
        public void LocalizedStringEntryThrows()
        {
            var member = (StringMember)JsonConvert.DeserializeObject<JsonMember>(
                "{\"id\":\"entry\",\"name\":\"Title\",\"kind\":3}")!;
            var row = new StringMemberValue { id = "entry", value = "text-id" };

            var error = Assert.Throws<InvalidOperationException>(() => NeoSetEntryKey.Of(member, row));

            StringAssert.Contains("Set<string> elements must be @settings(format: .Plain)", error!.Message);
        }

        private static JsonMember EntryMember(string kind)
        {
            var member = new JObject
            {
                ["id"] = "entry",
                ["name"] = "Entry",
                ["kind"] = (int)System.Enum.Parse<MemberKind>(kind),
            };
            if (kind == nameof(MemberKind.String))
                member["format"] = (int)NeoStringFormatKind.Plain;
            if (kind == nameof(MemberKind.Class))
                member["classId"] = "class-entry";
            if (kind == nameof(MemberKind.Enum))
                member["enumId"] = "enum-entry";
            return JsonConvert.DeserializeObject<JsonMember>(member.ToString())!;
        }

        private static MemberValue Row(JObject testCase)
        {
            var row = new JObject
            {
                ["id"] = testCase.Value<string>("id") ?? "entry",
                ["value"] = testCase["value"]!.DeepClone(),
            };
            return JsonConvert.DeserializeObject<MemberValue>(row.ToString())!;
        }
    }
}
