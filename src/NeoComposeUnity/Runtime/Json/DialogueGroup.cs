// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    public enum DialogueGroupType
    {
        Standard = 0,
        Lookup = 1,
        Folder = 2,
    }

    [JsonConverter(typeof(DialogueGroupConverter))]
    public abstract class DialogueGroup
    {
        public string id = null!;
        public string projectId = null!;
        public string name = null!;
        public DialogueGroupType type;
        public string? parentDialogueGroupId;
        public LogicCondition[] conditions = null!;
        public string? priorityGroupIdOverride;
        [JsonConverter(typeof(TolerantStringConverter))]
        public string createdAt = null!;
        [JsonConverter(typeof(TolerantStringConverter))]
        public string updatedAt = null!;
    }

    public class StandardDialogueGroup : DialogueGroup { }

    public class LookupDialogueGroup : DialogueGroup
    {
        public string collectionAttributeId = null!;
        public string? collectionValueId;
    }

    public class FolderDialogueGroup : DialogueGroup { }

    public class DialogueGroupConverter : DiscriminatedConverter<DialogueGroup>
    {
        protected override Type? ResolveSubclass(JToken discriminator)
        {
            switch ((DialogueGroupType)discriminator.Value<int>())
            {
                case DialogueGroupType.Standard: return typeof(StandardDialogueGroup);
                case DialogueGroupType.Lookup: return typeof(LookupDialogueGroup);
                case DialogueGroupType.Folder: return typeof(FolderDialogueGroup);
                default: return null;
            }
        }
    }
}
