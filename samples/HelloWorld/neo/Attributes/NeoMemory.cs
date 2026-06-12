// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("6c6f3bb8-30a0-4132-b0d4-cce75943aedd", Hidden = true, ExtraJson = @"{""extendsTypeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
public sealed class NeoMemory
{
    [NeoDictionary("6223e7c9-a37a-480d-820b-c70f53c6eb0d", Locked = true, EntryChainJson = @"[{""customTypeId"":""48f37cd8-69d2-4cd3-ae44-7cfed7912415"",""defaultValue"":{""value"":{}},""extendsAttributeId"":null,""id"":""3611d8af-6bfe-4015-93c4-2b611b33f2b6"",""locked"":true,""name"":""NeoDialogueMemory"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""},""type"":7}]", DefaultJson = @"{""value"":{}}", ExtraJson = @"{""extendsAttributeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""replaceValue""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
    public IReadOnlyDictionary<string, NeoDialogueMemory> DialogueMemories { get; init; }
}
