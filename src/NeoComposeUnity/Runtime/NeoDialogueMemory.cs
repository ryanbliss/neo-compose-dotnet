// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
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
        void AddChoice(string choiceId);
    }
}
