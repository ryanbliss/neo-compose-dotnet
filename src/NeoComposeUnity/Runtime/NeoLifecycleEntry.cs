// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// What NeoScript has seen of one rendered GameObject whose class has a
    /// Unity hook (P98 §3.2). The renderer queues the entry when the
    /// GameObject spawns, changes activity or despawns, and the drain moves
    /// it to the GameObject's state one hook at a time.
    /// </summary>
    internal sealed class NeoLifecycleEntry
    {
        // Indexed by hook bit; a relay listens for that physics message.
        private static readonly Type?[] RelayTypes = BuildRelayTypes();

        private readonly NeoClient client;
        private readonly NeoLifecyclePhases phases;
        private readonly string valueId;
        private readonly NeoValueOwnership ownership;
        private readonly GameObject gameObject;
        private readonly NeoMemberNSFunction?[] functions = new NeoMemberNSFunction?[NeoLifecycleHookTable.Count];
        private NeoClassNode node;
        // The physics hooks this GameObject has relays for.
        private NeoLifecycleHooks relayHooks;
        private bool awoken;
        private bool started;
        private bool despawned;
        private bool destroyed;

        /// <summary>Slot in each phase list, -1 outside it.</summary>
        internal readonly int[] slots = { -1, -1, -1, -1, -1 };
        internal NeoLifecycleHooks hooks;
        internal bool enabled;
        /// <summary>Queued for the drain; a later change finds it there.</summary>
        internal bool queued;
        /// <summary>In the phases' pending Starts.</summary>
        internal bool startQueued;

        private NeoLifecycleEntry(
            NeoClient client,
            NeoLifecyclePhases phases,
            string valueId,
            NeoValueOwnership ownership,
            GameObject gameObject,
            NeoClassNode node)
        {
            this.client = client;
            this.phases = phases;
            this.valueId = valueId;
            this.ownership = ownership;
            this.gameObject = gameObject;
            this.node = node;
            hooks = node.Hooks & NeoLifecycleHooks.Unity;
            SyncRelays();
        }

        /// <summary>The entry for a GameObject the renderer just drew, or null when its class has no Unity hook.</summary>
        internal static NeoLifecycleEntry? Create(NeoLifecyclePhases phases, NeoGeneratedClassValue value, GameObject gameObject)
        {
            if (value.valueId is not string valueId || value.classId is not string classId)
                return null;
            NeoClassNode node = value.Client.ResolveClassNode(classId);
            if ((node.Hooks & NeoLifecycleHooks.Unity) == 0)
                return null;
            return new NeoLifecycleEntry(value.Client, phases, valueId, value.StorageOwnership, gameObject, node);
        }

        /// <summary>
        /// The entry for a GameObject whose class gained its first Unity hook
        /// in a schema reload, marked with the GameObject's current state so
        /// the reload runs nothing (P98 §4.3).
        /// </summary>
        internal static NeoLifecycleEntry? CreateCurrent(NeoLifecyclePhases phases, NeoGeneratedClassValue value, GameObject gameObject)
        {
            NeoLifecycleEntry? entry = Create(phases, value, gameObject);
            if (entry is null)
                return null;
            entry.awoken = true;
            entry.started = true;
            entry.enabled = entry.Active;
            phases.Sync(entry);
            return entry;
        }

        private bool Active => !despawned && gameObject != null && gameObject.activeInHierarchy;

        internal bool Has(NeoLifecycleHooks hook) => (hooks & hook) != 0;

        /// <summary>Rereads the class's hooks after a schema reload. Runs nothing.</summary>
        internal void Refresh()
        {
            node = client.ResolveClassNode(node.Id);
            hooks = node.Hooks & NeoLifecycleHooks.Unity;
            Array.Clear(functions, 0, functions.Length);
            SyncRelays();
            phases.Sync(this);
        }

        /// <summary>Marks the GameObject gone; the drain runs its OnDisable and OnDestroy.</summary>
        internal void Despawn()
        {
            despawned = true;
            client.QueueUnityTransition(this);
        }

        /// <summary>Moves the entry to its GameObject's state, checking it again after each hook (P98 §3.2).</summary>
        internal void Advance()
        {
            try
            {
                while (true)
                {
                    if (!awoken)
                    {
                        // Spawned and despawned before Awake: nothing runs.
                        if (despawned)
                            return;
                        awoken = true;
                        RunTransition(NeoLifecycleHooks.Awake);
                        continue;
                    }
                    bool active = Active;
                    if (active && !enabled)
                    {
                        enabled = true;
                        phases.Sync(this);
                        if (!started && !startQueued)
                            phases.QueueStart(this);
                        RunTransition(NeoLifecycleHooks.OnEnable);
                        continue;
                    }
                    if (!active && enabled)
                    {
                        enabled = false;
                        phases.Sync(this);
                        RunTransition(NeoLifecycleHooks.OnDisable);
                        continue;
                    }
                    if (despawned && !destroyed)
                    {
                        destroyed = true;
                        RunTransition(NeoLifecycleHooks.OnDestroy);
                    }
                    return;
                }
            }
            finally
            {
                queued = false;
            }
        }

        /// <summary>Runs a physics message the receiver hears while enabled (P98 §3.5).</summary>
        internal void Deliver(NeoLifecycleHooks message, INeoWorldObjectValue? other, Vector2 normal, Vector2 relativeVelocity)
        {
            if (!enabled || !Has(message) || !CountRun(message))
                return;
            bool contact = message is NeoLifecycleHooks.OnCollisionEnter2D or NeoLifecycleHooks.OnCollisionStay2D;
            object?[] args = NeoArgumentArrays.Rent(contact ? 3 : 1);
            args[0] = other;
            if (contact)
            {
                args[1] = normal;
                args[2] = relativeVelocity;
            }
            try
            {
                Run(message, args, departure: false);
            }
            finally
            {
                NeoArgumentArrays.Return(args);
            }
        }

        internal string ValueId => valueId;

        /// <summary>The cached function behind one of the class's hooks.</summary>
        internal NeoMemberNSFunction Function(NeoLifecycleHooks hook)
        {
            int index = NeoLifecycleHookTable.IndexOf(hook);
            return functions[index] ??= client.EffectFunction(node.HookMember(hook), ownership);
        }

        internal void LogFailure(NeoLifecycleHooks hook, Exception exception) =>
            NeoClient.LogLifecycleHookFailure(node, hook, valueId, exception);

        /// <summary>Runs the pending Start of an entry still enabled, or settles one that has none.</summary>
        internal void RunStart()
        {
            startQueued = false;
            if (started || !enabled || despawned)
                return;
            started = true;
            if (Has(NeoLifecycleHooks.Start))
                client.RunOutermostHook(this, NeoLifecycleHooks.Start, Function(NeoLifecycleHooks.Start), valueId, Array.Empty<object?>());
        }

        // A transition hook runs in the drain, so it counts toward the run limit.
        private void RunTransition(NeoLifecycleHooks hook)
        {
            if (!Has(hook) || !CountRun(hook))
                return;
            // A despawn caused by the instance leaving the data reads its departed rows.
            bool departure = despawned
                && (hook & NeoLifecycleHooks.Departure) != 0
                && client.IsDeparted(valueId);
            Run(hook, Array.Empty<object?>(), departure);
        }

        private bool CountRun(NeoLifecycleHooks hook) =>
            client.CountLifecycleRun(this, node.Id, node.HookMember(hook), valueId);

        private void Run(NeoLifecycleHooks hook, object?[] args, bool departure)
        {
            client.RunLifecycleHook(node, hook, Function(hook), valueId, args, departure);
        }

        private void SyncRelays()
        {
            NeoLifecycleHooks wanted = hooks & NeoLifecycleHooks.Physics;
            NeoLifecycleHooks changed = wanted ^ relayHooks;
            if (changed == 0 || gameObject == null)
                return;
            for (int i = 0; i < NeoLifecycleHookTable.Count; i++)
            {
                var hook = (NeoLifecycleHooks)(1u << i);
                if ((changed & hook) == 0)
                    continue;
                if ((wanted & hook) != 0)
                    ((NeoLifecycleRelay)gameObject.AddComponent(RelayTypes[i]!)).Bind(this, hook);
                else if (gameObject.TryGetComponent(RelayTypes[i]!, out Component relay))
                    UnityEngine.Object.Destroy(relay);
            }
            relayHooks = wanted;
        }

        internal void QueueMessage(NeoLifecycleHooks message, Collider2D other, Vector2 normal, Vector2 relativeVelocity) =>
            client.QueueUnityMessage(this, message, other, normal, relativeVelocity);

        internal float CellSize => phases.CellSize;

        private static Type?[] BuildRelayTypes()
        {
            var types = new Type?[NeoLifecycleHookTable.Count];
            types[NeoLifecycleHookTable.IndexOf(NeoLifecycleHooks.OnTriggerEnter2D)] = typeof(NeoTriggerEnter2DRelay);
            types[NeoLifecycleHookTable.IndexOf(NeoLifecycleHooks.OnTriggerStay2D)] = typeof(NeoTriggerStay2DRelay);
            types[NeoLifecycleHookTable.IndexOf(NeoLifecycleHooks.OnTriggerExit2D)] = typeof(NeoTriggerExit2DRelay);
            types[NeoLifecycleHookTable.IndexOf(NeoLifecycleHooks.OnCollisionEnter2D)] = typeof(NeoCollisionEnter2DRelay);
            types[NeoLifecycleHookTable.IndexOf(NeoLifecycleHooks.OnCollisionStay2D)] = typeof(NeoCollisionStay2DRelay);
            types[NeoLifecycleHookTable.IndexOf(NeoLifecycleHooks.OnCollisionExit2D)] = typeof(NeoCollisionExit2DRelay);
            return types;
        }
    }
}
