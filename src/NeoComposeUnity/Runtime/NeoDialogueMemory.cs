// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    internal readonly struct NeoDialogueMemoryPointer
    {
        public string dialogueId { get; }
        public string? textNodeId { get; }
        public string? optionId { get; }

        public NeoDialogueMemoryPointer(
            string dialogueId,
            string? textNodeId,
            string? optionId)
        {
            this.dialogueId = dialogueId;
            this.textNodeId = textNodeId;
            this.optionId = optionId;
        }
    }

    internal static class NeoDialogueMemoryQueries
    {
        public static int VisitCount(INeoDialogueMemoryStore? memoryStore, string pointer)
        {
            if (!TryParsePointer(pointer, out var parsed)) return 0;
            var dialogueMemory = memoryStore?.FindDialogueMemory(parsed.dialogueId);
            if (dialogueMemory is null) return 0;
            if (parsed.textNodeId is null) return dialogueMemory.VisitCount;
            var textNodeMemory = dialogueMemory.FindTextNodeMemory(parsed.textNodeId);
            if (textNodeMemory is null) return 0;
            if (parsed.optionId is null) return textNodeMemory.VisitCount;
            return textNodeMemory.HasChoice(parsed.optionId) ? 1 : 0;
        }

        public static bool HasVisited(INeoDialogueMemoryStore? memoryStore, string pointer)
        {
            return VisitCount(memoryStore, pointer) > 0;
        }

        public static bool TryParsePointer(
            object? pointer,
            out NeoDialogueMemoryPointer parsed)
        {
            parsed = default;
            if (pointer is not string text) return false;
            var parts = text.Split(',');
            if (parts.Length < 1 || parts.Length > 3) return false;
            string dialogueId = parts[0].Trim();
            if (dialogueId.Length == 0) return false;
            string? textNodeId = parts.Length > 1 && parts[1].Trim().Length > 0
                ? parts[1].Trim()
                : null;
            string? optionId = parts.Length > 2 && parts[2].Trim().Length > 0
                ? parts[2].Trim()
                : null;
            if (optionId is not null && textNodeId is null) return false;
            parsed = new NeoDialogueMemoryPointer(dialogueId, textNodeId, optionId);
            return true;
        }
    }

    public interface INeoDialogueMemoryStore
    {
        INeoDialogueMemory GetOrCreateDialogueMemory(string dialogueId);
        INeoDialogueMemory? FindDialogueMemory(string dialogueId);
    }

    public interface INeoDialogueMemory
    {
        int VisitCount { get; set; }
        string? LastVisitedAt { get; set; }
        INeoTextNodeMemory GetOrCreateTextNodeMemory(string textNodeId);
        INeoTextNodeMemory? FindTextNodeMemory(string textNodeId);
    }

    public interface INeoTextNodeMemory
    {
        int VisitCount { get; set; }
        string? LastVisitedAt { get; set; }
        string? MostRecentChoiceId { get; set; }
        bool HasChoice(string choiceId);
        void AddChoice(string choiceId, string createdAt);
    }
}
