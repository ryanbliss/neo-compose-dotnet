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
    /// the TypeScript <c>setEntryKey</c> does. The fixture's
    /// <c>keyEquivalent</c> and <c>description</c> are server rules (P127 §9.5).
    /// </summary>
    public class NeoSetEntryKeyParityTests
    {
        [Test]
        public void EveryCaseKeysLikeTheWeb()
        {
            var failures = new List<string>();
            using NeoClient client = EmptyClient(out _);
            foreach (JObject testCase in (JArray)JObject.Parse(NeoSetEntryKeyParityFixture.Json)["cases"]!)
            {
                string name = testCase.Value<string>("name")!;
                var rows = new Dictionary<string, MemberValue>(StringComparer.Ordinal);
                if (testCase["rows"] is JObject childRows)
                {
                    foreach (JProperty child in childRows.Properties())
                        rows[child.Name] = Row(child.Name, (JObject)child.Value);
                }
                string actual;
                JsonMember entryMember = EntryMember(testCase.Value<string>("kind")!, testCase.Value<int?>("objectType"));
                MemberValue entry = Row(testCase.Value<string>("id") ?? "entry", testCase);
                MemberValue? ReadRow(string id) => rows.TryGetValue(id, out MemberValue row) ? row : null;
                try
                {
                    actual = NeoSetEntryKey.Of(entryMember, entry, client, ReadRow);
                }
                catch (Exception error)
                {
                    failures.Add($"[{name}] threw {error.GetType().Name}: {error.Message}");
                    continue;
                }
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

            using NeoClient client = EmptyClient(out _);

            var error = Assert.Throws<InvalidOperationException>(() => NeoSetEntryKey.Of(member, row, client));

            StringAssert.Contains("Set<string> elements must be @settings(format: .Plain)", error!.Message);
        }

        /// <summary>
        /// A project with only its roots and the fixture's comparer classes:
        /// box members mint from stored bindings.
        /// </summary>
        private static NeoClient EmptyClient(out ProjectData data)
        {
            const string projectId = "set-entry-key-parity";
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = projectId,
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            ClassMember Root(string id, string? valueId) => new()
            {
                id = id,
                projectId = projectId,
                name = id,
                kind = MemberKind.Class,
                DeclaredClassId = rootClass.id,
                valueId = valueId,
            };
            data = new ProjectData
            {
                project = new Project
                {
                    id = projectId,
                    name = "Set entry key parity",
                    rootAssetsMemberId = "assets-member",
                    rootSaveFileMemberId = "save-member",
                    rootSessionMemberId = "session-member",
                },
                members = new Dictionary<string, JsonMember>
                {
                    ["assets-member"] = Root("assets-member", "assets"),
                    ["save-member"] = Root("save-member", null),
                    ["session-member"] = Root("session-member", null),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["assets"] = new ObjectMemberValue { id = "assets", classId = rootClass.id, value = new Dictionary<string, string>() },
                },
                classes = new Dictionary<string, NeoSchemaClass> { [rootClass.id] = rootClass },
            }.WithUserRoot();
            foreach ((string id, string name) in FixtureComparerClasses)
            {
                data.classes[id] = new NeoSchemaClass
                {
                    id = id,
                    projectId = projectId,
                    name = name,
                    schema = new Dictionary<string, string>(),
                    genericParams = new List<GenericParamDeclaration> { new() { id = id + ".T", name = "T" } },
                };
            }
            NeoSystemComparerClasses.Register(JObject.FromObject(data.classes));
            return NeoTestSaveStack.ClientFromSchema(data);
        }

        private static JsonMember EntryMember(string kind, int? objectType)
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
            if (objectType is int declared)
                member["objectType"] = declared;
            return JsonConvert.DeserializeObject<JsonMember>(member.ToString())!;
        }

        private static readonly (string id, string name)[] FixtureComparerClasses =
        {
            ("system_00000000-0000-4000-8000-0000000000c1", "EqualityComparer"),
            ("system_00000000-0000-4000-8000-0000000000c2", "Comparer"),
        };

        private static MemberValue Row(string id, JObject testCase)
        {
            JToken value = testCase["value"]!.DeepClone();
            var row = new JObject
            {
                ["id"] = id,
                ["value"] = value,
            };
            if (testCase["classId"] is JToken classId)
                row["classId"] = classId.DeepClone();
            MemberValue stored = JsonConvert.DeserializeObject<MemberValue>(row.ToString())!;
            // The fixture pins key rules, not storage: a box's payload may be
            // one storage refuses, such as a Comparer.Create without delegates.
            if (testCase["valueType"] is JObject heldType)
                stored.valueType = heldType.ToObject<TypeBinding>();
            return stored;
        }
    }
}
