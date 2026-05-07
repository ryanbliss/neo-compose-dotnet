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
        private static readonly System.Random DefaultRandom = new();

        public Func<DateTime>? UtcNow { get; set; }
        public Func<double>? RandomDouble { get; set; }
        public TimeSpan? RecencyHalfLife { get; set; }
        public INeoDialogueLogger? Logger { get; set; }
        public int OnEligibleDebounceMilliseconds { get; set; } = 50;
        public bool OnEligibleEmitAll { get; set; }

        internal INeoDialogueLogger ResolveLogger()
        {
            return Logger ?? new UnityNeoDialogueLogger();
        }

        internal DateTime ResolveUtcNow()
        {
            return (UtcNow ?? (() => DateTime.UtcNow))().ToUniversalTime();
        }

        internal double ResolveRandomDouble()
        {
            var value = (RandomDouble ?? DefaultNextDouble)();
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            if (value < 0) return 0;
            if (value >= 1) return 0.999999999999;
            return value;
        }

        internal TimeSpan ResolveRecencyHalfLife()
        {
            return RecencyHalfLife ?? TimeSpan.FromDays(1);
        }

        private static double DefaultNextDouble()
        {
            lock (DefaultRandom)
            {
                return DefaultRandom.NextDouble();
            }
        }
    }

    public sealed class NeoOnEligibleOptions
    {
        public int? DebounceMilliseconds { get; set; }
        public bool EmitAll { get; set; }
    }

    public sealed class NeoDialogueWatcher : IDisposable
    {
        private Action? dispose;

        internal NeoDialogueWatcher(Action dispose)
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

    public enum NeoDialogueState
    {
        Created,
        Started,
        Finished,
        Disposed,
    }

    public sealed class NeoDialogueTriggerWarning
    {
        public string Message { get; }
        public string? DialogueId { get; }
        public string? GroupId { get; }

        public NeoDialogueTriggerWarning(
            string message,
            string? dialogueId = null,
            string? groupId = null)
        {
            Message = message;
            DialogueId = dialogueId;
            GroupId = groupId;
        }
    }

    public sealed class NeoDialogueTriggerResult
    {
        public bool Ok { get; }
        public NeoDialogue? Dialogue { get; }
        public Exception? Error { get; }
        public IReadOnlyList<NeoDialogueTriggerWarning> Warnings { get; }

        private NeoDialogueTriggerResult(
            bool ok,
            NeoDialogue? dialogue,
            Exception? error,
            IReadOnlyList<NeoDialogueTriggerWarning> warnings)
        {
            Ok = ok;
            Dialogue = dialogue;
            Error = error;
            Warnings = warnings;
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
        public Exception Exception { get; }
        public string? DialogueId { get; }
        public string? GroupId { get; }

        public NeoDialogueEligibilityError(
            Exception exception,
            string? dialogueId = null,
            string? groupId = null)
        {
            Exception = exception;
            DialogueId = dialogueId;
            GroupId = groupId;
        }
    }

    public delegate object? NeoDialogueValueResolver(string valueId);

    public sealed class NeoDialogueContext
    {
        internal object? CurrentPrimary { get; set; }

        public string DialogueId { get; }
        public string? GroupId { get; }
        public string? NodeId { get; internal set; }
        public string? OptionId { get; internal set; }
        public object? Trigger { get; }
        public object? Primary { get; }
        public IReadOnlyDictionary<string, object?> LinkedValues { get; }

        public NeoDialogueContext(
            string dialogueId,
            string? groupId,
            object? trigger,
            object? primary,
            IReadOnlyDictionary<string, object?> linkedValues)
        {
            DialogueId = dialogueId;
            GroupId = groupId;
            Trigger = trigger;
            Primary = primary;
            CurrentPrimary = primary;
            LinkedValues = linkedValues;
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
