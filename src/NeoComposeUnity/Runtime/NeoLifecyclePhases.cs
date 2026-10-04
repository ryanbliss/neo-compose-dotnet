// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// One renderer's enabled hooked entries per per-frame and application
    /// hook (P98 §3.4, §4.3). Leaving clears a slot; a list compacts in place
    /// before its next run, so nothing allocates in a steady frame.
    /// </summary>
    internal sealed class NeoLifecyclePhases
    {
        internal const int FixedUpdate = 0;
        internal const int Update = 1;
        internal const int LateUpdate = 2;
        internal const int ApplicationPause = 3;
        internal const int ApplicationQuit = 4;

        private static readonly NeoLifecycleHooks[] PhaseHooks =
        {
            NeoLifecycleHooks.FixedUpdate,
            NeoLifecycleHooks.Update,
            NeoLifecycleHooks.LateUpdate,
            NeoLifecycleHooks.OnApplicationPause,
            NeoLifecycleHooks.OnApplicationQuit,
        };

        // What one call needs, inline, so a phase reads one array rather
        // than an entry per object. A null entry is a hole.
        private struct Slot
        {
            internal NeoLifecycleEntry? entry;
            internal NeoMemberNSFunction? function;
            internal string valueId;
        }

        private readonly NeoTileGridRenderer renderer;
        private readonly Slot[][] lists =
        {
            Array.Empty<Slot>(),
            Array.Empty<Slot>(),
            Array.Empty<Slot>(),
            Array.Empty<Slot>(),
            Array.Empty<Slot>(),
        };
        private readonly int[] counts = new int[PhaseHooks.Length];
        private readonly int[] holes = new int[PhaseHooks.Length];
        // A phase's one argument, shared by every hook it runs. Phases never nest.
        private readonly object?[] argument = new object?[1];
        // Enabled entries whose Start has not run.
        private readonly List<NeoLifecycleEntry> pendingStarts = new();

        internal NeoLifecyclePhases(NeoTileGridRenderer renderer, NeoClient client)
        {
            this.renderer = renderer;
            Client = client;
        }

        internal NeoClient Client
        {
            get;
        }

        internal float CellSize => renderer.CellSize;

        /// <summary>
        /// Puts an entry in the lists its enabled hooks want, and out of the
        /// others. A slot it keeps forgets its function, which a reload may have replaced.
        /// </summary>
        internal void Sync(NeoLifecycleEntry entry)
        {
            for (int phase = 0; phase < PhaseHooks.Length; phase++)
            {
                bool wanted = entry.enabled && entry.Has(PhaseHooks[phase]);
                int slot = entry.slots[phase];
                if (wanted && slot < 0)
                {
                    int count = counts[phase]++;
                    if (count == lists[phase].Length)
                        Array.Resize(ref lists[phase], Math.Max(4, count * 2));
                    lists[phase][count] = new Slot { entry = entry, valueId = entry.ValueId };
                    entry.slots[phase] = count;
                }
                else if (wanted)
                {
                    lists[phase][slot].function = null;
                }
                else if (slot >= 0)
                {
                    lists[phase][slot] = default;
                    entry.slots[phase] = -1;
                    holes[phase]++;
                }
            }
        }

        internal void QueueStart(NeoLifecycleEntry entry)
        {
            entry.startQueued = true;
            pendingStarts.Add(entry);
        }

        /// <summary>
        /// Runs the pending Starts, then one hook on every entry in the
        /// phase, each as its own outermost execution.
        /// </summary>
        /// <param name="value">The hook's argument, boxed once for every call, or null for none.</param>
        internal void Run(int phase, object? value)
        {
            if (pendingStarts.Count != 0)
                RunPendingStarts();
            if (holes[phase] != 0)
                Compact(phase);
            // Entries enabled during the phase join at the next one.
            int count = counts[phase];
            NeoLifecycleHooks hook = PhaseHooks[phase];
            object?[] args = Array.Empty<object?>();
            if (value is not null)
            {
                argument[0] = value;
                args = argument;
            }
            NeoClient client = Client;
            for (int i = 0; i < count; i++)
            {
                // Read through the field: a hook that enables an entry may grow the array.
                ref Slot slot = ref lists[phase][i];
                if (slot.entry is not { } entry)
                    continue;
                NeoMemberNSFunction function = slot.function ??= entry.Function(hook);
                client.RunOutermostHook(entry, hook, function, slot.valueId, args);
            }
        }

        internal bool IsEmpty(int phase) => counts[phase] == 0 && pendingStarts.Count == 0;

        // A Start that enables another entry queues its Start here too, and
        // it runs in this pass.
        private void RunPendingStarts()
        {
            for (int i = 0; i < pendingStarts.Count; i++)
                pendingStarts[i].RunStart();
            pendingStarts.Clear();
        }

        private void Compact(int phase)
        {
            Slot[] list = lists[phase];
            int count = counts[phase];
            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                if (list[i].entry is not { } entry)
                    continue;
                entry.slots[phase] = kept;
                list[kept++] = list[i];
            }
            Array.Clear(list, kept, count - kept);
            counts[phase] = kept;
            holes[phase] = 0;
        }
    }
}
