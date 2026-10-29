// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Tests
{
    public partial class P75VirtualInstanceValueTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void StoredLookupSelectionDoesNotChangeWhenSiblingReplays(int transitions)
        {
            ProjectData data = BuildUnorderedListProjectData();
            ((ListMember)data.members["thing-items"]).ListKind = NeoListKind.Ordered;
            data.classes["save-root-class"].schema["Areas"] = "areas";
            ((ObjectMemberValue)data.values["value-save"]).value!["Areas"] = "area-list";
            data.members["areas"] = new ListMember
            {
                id = "areas",
                name = "Areas",
                kind = MemberKind.List,
                Storage = NeoMemberStorage.Save,
                Requirement = NeoMemberRequirementKind.Required,
                entryMemberId = "thing-item",
                valueId = "area-list",
            };
            data.values["area-list"] = new ArrayMemberValue { id = "area-list", value = new[] { "area-a", "area-b" } };
            data.values["area-a"] = new StringMemberValue { id = "area-a", value = "A" };
            data.values["area-b"] = new StringMemberValue { id = "area-b", value = "B" };
            data.classes["thing-class"].schema["CurrentArea"] = "current-area";
            data.members["current-area"] = new LookupMember
            {
                id = "current-area",
                name = "CurrentArea",
                kind = MemberKind.Lookup,
                Requirement = NeoMemberRequirementKind.Required,
                collectionMemberId = "areas",
                collectionValueId = "area-list",
                Selection = NeoMemberSelectionKind.Single,
                defaultValue = new ArrayMemberValueBase { value = new[] { "area-a" } },
            };
            string saved;
            using (var client = NeoTestSaveStack.ClientFromSchema(data))
            {
                var owner = client.save.Get<NeoMemberClassWritable>("Thing");
                using var view = new LookupNotificationView(client, owner);
                var heard = new List<string>();
                using var subscription = view.Watch(heard.Add);
                ReplaceItem(owner, "before");
                Assert.That(heard, Is.Empty, "The same unrelated write is silent before transitions.");
                for (int index = 0; index < transitions; index++)
                {
                    string next = index == 0 ? "area-b" : "area-a";
                    owner.SetSerializedValue("CurrentArea", NeoGeneratedTypesSupport.Value(new[] { next }));
                    Assert.That(heard.Count, Is.EqualTo(index + 1));
                    Assert.That(heard[index], Is.EqualTo(next));
                }
                string selected = view.Selected;
                int replays = client.VirtualReplayCount;
                ReplaceItem(owner, "after");
                Assert.That(client.VirtualReplayCount, Is.GreaterThan(replays), "Exercise enclosing-root replay.");
                Assert.That(view.Selected, Is.EqualTo(selected));
                Assert.That(heard.Count, Is.EqualTo(transitions), "Retiring a default is not a selection change.");
                ((NeoMemberStringWritable)client.save.Get<NeoMemberListWritable>("Areas")[transitions == 1 ? 1 : 0]).Set("renamed");
                Assert.That(heard.Count, Is.EqualTo(transitions), "Changing the selected target is not a selection change.");
                saved = client.SerializeSaveData();
            }
            using var reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            var reopenedOwner = reopened.save.Get<NeoMemberClassWritable>("Thing");
            using var reopenedView = new LookupNotificationView(reopened, reopenedOwner);
            var afterReload = new List<string>();
            using var reopenedSubscription = reopenedView.Watch(afterReload.Add);
            string expected = transitions == 1 ? "area-b" : "area-a";
            Assert.That(reopenedView.Selected, Is.EqualTo(expected));
            ReplaceItem(reopenedOwner, "reopened");
            Assert.That(afterReload, Is.Empty);
            string changed = expected == "area-a" ? "area-b" : "area-a";
            reopenedOwner.SetSerializedValue("CurrentArea", NeoGeneratedTypesSupport.Value(new[] { changed }));
            Assert.That(afterReload, Is.EqualTo(new[] { changed }), "A real selection change still publishes after reload.");
        }

        [Test]
        public void SavedEntryInVirtualContainerReplaysBeforeItsSavedChildren()
        {
            var data = BuildNestedProjectData();
            data.classes["thing-class"].schema["Entries"] = "replay-entries";
            data.members["replay-entry"] = new ClassMember
            {
                id = "replay-entry",
                name = "Entry",
                kind = MemberKind.Class,
                DeclaredClassId = "nested-class",
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.members["replay-entries"] = new ListMember
            {
                id = "replay-entries",
                name = "Entries",
                kind = MemberKind.List,
                entryMemberId = "replay-entry",
                ListKind = NeoListKind.Unordered,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ArrayMemberValueBase { value = Array.Empty<string>() },
            };
            JObject saved;
            using (var client = NeoTestSaveStack.ClientFromSchema(data))
            {
                string container = client.save.Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberListWritable>("Entries").value!.id;
                saved = JObject.Parse(client.SerializeSaveData());
                saved["values"]!["z-moved-entry"] = JObject.FromObject(new ObjectMemberValue
                {
                    id = "z-moved-entry",
                    classId = "nested-class",
                    containerId = container,
                    value = new Dictionary<string, string>(),
                    constructorArgs = new Dictionary<string, JToken?>(),
                });
                saved["values"]!["z-moved-entry"]!["instanceConstructorId"] = JValue.CreateNull();
            }
            using (var client = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved.ToString()))
            {
                var entry = (NeoMemberClassWritable)client.save.Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberListWritable>("Entries")[0];
                string deepId = entry.Get<NeoMemberClassWritable>("Deep").value!.id;
                // The parent omits Deep because it is constructor-derived. Its
                // saved child must wait for that parent's placement to replay.
                saved["values"]![deepId] = JObject.FromObject(new ObjectMemberValue
                {
                    id = deepId,
                    classId = "deep-class",
                    constructorArgs = new Dictionary<string, JToken?>(),
                    value = new Dictionary<string, string> { ["Count"] = "saved-count" },
                });
                saved["values"]![deepId]!["instanceConstructorId"] = JValue.CreateNull();
                saved["values"]!["saved-count"] = JObject.FromObject(new NumberMemberValue { id = "saved-count", value = 42 });
            }
            using var reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved.ToString());
            var restored = (NeoMemberClassWritable)reopened.save.Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberListWritable>("Entries")[0];
            Assert.That(restored.value!.id, Is.EqualTo("z-moved-entry"));
            Assert.That(restored.Get<NeoMemberClassWritable>("Deep").Get<NeoMemberIntWritable>("Count").value!.value, Is.EqualTo(42));
        }

        private static void ReplaceItem(NeoMemberClassWritable owner, string value)
        {
            var items = owner.Get<NeoMemberListWritable>("Items");
            items.RemoveAt(0);
            items.AddSerialized(NeoGeneratedTypesSupport.Value(value));
        }

        private sealed class LookupNotificationView : NeoGeneratedClassValue
        {
            internal LookupNotificationView(NeoClient client, NeoMemberClassWritable node)
                : base(client, node, "thing-class", false, NeoValueOwnership.Save) { }
            internal string Selected => node.Get<NeoMemberLookup>("CurrentArea").GetFirstSelected()!.value!.id;
            internal IDisposable Watch(Action<string> callback) => WatchField(new NeoField<string>("CurrentArea"),
                (selected, _) => callback(selected), () => Selected);
        }
    }
}
