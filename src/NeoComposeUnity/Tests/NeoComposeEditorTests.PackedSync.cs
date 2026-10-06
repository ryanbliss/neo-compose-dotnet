// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class NeoComposeEditorTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void PackedExport_RejectsConflictingStoredChild(bool partitioned)
        {
            var project = PackedSyncProject(partitioned);
            PackedSyncRows(project, partitioned)["child"] =
                JObject.Parse("{'id':'child','value':'stale','createdAt':0,'updatedAt':0}");
            // A named partition expands when a client first loads it.
            Assert.Throws<JsonSerializationException>(() =>
            {
                ProjectData data = NeoTestExport.Read(project.ToString());
                data.valuePartitions.Load("world:test");
            });
        }

        private static JObject PackedSyncProject(bool partitioned)
        {
            var root = JObject.Parse(ProjectJsonWithFiles(""));
            var parent = JObject.Parse(@"{
                'id':'parent', 'classId':'parent-class', 'createdAt':0, 'updatedAt':0,
                'value':{'Name':{'~packed':{'id':'child','value':'current','createdAt':0,'updatedAt':0}}}
            }");
            var rows = new JObject { ["parent"] = parent };
            if (partitioned)
            {
                parent["mapKey"] = "world:test";
                root["valuePartitions"] = new JObject { ["world:test"] = rows };
            }
            else
                root["values"] = rows;
            return root;
        }

        private static JObject PackedSyncRows(JObject project, bool partitioned) =>
            (JObject)(partitioned ? project["valuePartitions"]!["world:test"]! : project["values"]!);
    }
}
