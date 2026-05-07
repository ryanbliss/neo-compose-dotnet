// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public interface INeoDialogueLogger
    {
        void LogWarning(string message);
        void LogError(string message);
        void LogException(Exception exception);
    }

    public sealed class UnityNeoDialogueLogger : INeoDialogueLogger
    {
        public void LogWarning(string message) => Debug.LogWarning(message);
        public void LogError(string message) => Debug.LogError(message);
        public void LogException(Exception exception) => Debug.LogException(exception);
    }

    public sealed class NeoDialogueRuntimeOptions
    {
        public INeoDialogueLogger? Logger { get; set; }
        public int OnEligibleDebounceMilliseconds { get; set; } = 50;
        public bool OnEligibleEmitAll { get; set; }

        internal INeoDialogueLogger ResolveLogger()
        {
            return Logger ?? new UnityNeoDialogueLogger();
        }
    }

    public sealed class NeoOnEligibleOptions
    {
        public int? DebounceMilliseconds { get; set; }
        public bool EmitAll { get; set; }
    }

    public sealed class NeoDialogueTriggerWarning
    {
        public string message { get; }
        public string? dialogueId { get; }
        public string? groupId { get; }

        public NeoDialogueTriggerWarning(
            string message,
            string? dialogueId = null,
            string? groupId = null)
        {
            this.message = message;
            this.dialogueId = dialogueId;
            this.groupId = groupId;
        }
    }

    public sealed class NeoDialogueTriggerResult
    {
        public bool ok { get; }
        public NeoDialogue? dialogue { get; }
        public Exception? error { get; }
        public IReadOnlyList<NeoDialogueTriggerWarning> warnings { get; }

        private NeoDialogueTriggerResult(
            bool ok,
            NeoDialogue? dialogue,
            Exception? error,
            IReadOnlyList<NeoDialogueTriggerWarning> warnings)
        {
            this.ok = ok;
            this.dialogue = dialogue;
            this.error = error;
            this.warnings = warnings;
        }

        public static NeoDialogueTriggerResult Success(
            NeoDialogue dialogue,
            IReadOnlyList<NeoDialogueTriggerWarning>? warnings = null)
        {
            return new NeoDialogueTriggerResult(true, dialogue, null, warnings ?? Array.Empty<NeoDialogueTriggerWarning>());
        }

        public static NeoDialogueTriggerResult NotFound(
            IReadOnlyList<NeoDialogueTriggerWarning>? warnings = null)
        {
            return new NeoDialogueTriggerResult(false, null, null, warnings ?? Array.Empty<NeoDialogueTriggerWarning>());
        }

        public static NeoDialogueTriggerResult Failed(
            Exception error,
            IReadOnlyList<NeoDialogueTriggerWarning>? warnings = null)
        {
            return new NeoDialogueTriggerResult(false, null, error, warnings ?? Array.Empty<NeoDialogueTriggerWarning>());
        }
    }

    public sealed class NeoDialogueEligibilityError
    {
        public Exception exception { get; }
        public string? dialogueId { get; }
        public string? groupId { get; }

        public NeoDialogueEligibilityError(
            Exception exception,
            string? dialogueId = null,
            string? groupId = null)
        {
            this.exception = exception;
            this.dialogueId = dialogueId;
            this.groupId = groupId;
        }
    }

    public delegate object? NeoDialogueValueResolver(string valueId);

    public sealed class NeoDialogueContext
    {
        public string dialogueId { get; }
        public string? groupId { get; }
        public string? nodeId { get; internal set; }
        public string? optionId { get; internal set; }
        public object? trigger { get; }
        public object? primary { get; internal set; }
        public IReadOnlyDictionary<string, object?> linkedValues { get; }

        public NeoDialogueContext(
            string dialogueId,
            string? groupId,
            object? trigger,
            object? primary,
            IReadOnlyDictionary<string, object?> linkedValues)
        {
            this.dialogueId = dialogueId;
            this.groupId = groupId;
            this.trigger = trigger;
            this.primary = primary;
            this.linkedValues = linkedValues;
        }
    }

    internal sealed class NeoDisposableAction : IDisposable
    {
        private Action? dispose;

        public NeoDisposableAction(Action dispose)
        {
            this.dispose = dispose;
        }

        public void Dispose()
        {
            var current = dispose;
            dispose = null;
            current?.Invoke();
        }
    }
}
