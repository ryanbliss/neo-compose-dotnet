// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // P98 lifecycle hooks. OnLoad and OnUnload ride P97's instance
        // tracker: an instance's start queues its OnLoad and its stop queues
        // its OnUnload. Renderers queue Unity hooks as entry transitions and
        // physics messages. Everything runs in the effect drain.

        // A null message moves the entry to its GameObject's state.
        private readonly struct UnityHookEvent
        {
            internal readonly NeoLifecycleEntry entry;
            internal readonly NeoLifecycleHooks message;
            internal readonly INeoWorldObjectValue? other;
            internal readonly Vector2 normal;
            internal readonly Vector2 relativeVelocity;

            internal UnityHookEvent(
                NeoLifecycleEntry entry,
                NeoLifecycleHooks message,
                INeoWorldObjectValue? other,
                Vector2 normal,
                Vector2 relativeVelocity)
            {
                this.entry = entry;
                this.message = message;
                this.other = other;
                this.normal = normal;
                this.relativeVelocity = relativeVelocity;
            }
        }

        // Whether the schema has a tracked class or a Unity hook.
        private bool scriptRuntimeStarted;
        // Every hook some class implements.
        private NeoLifecycleHooks schemaHooks;
        private readonly Dictionary<string, NeoLifecycleHooks> interfaceHooks = new(StringComparer.Ordinal);
        private readonly Queue<EffectInstance> pendingUnloads = new();
        // An instance stopped before its OnLoad ran stays queued; the drain skips it.
        private readonly Queue<EffectInstance> pendingLoads = new();
        private readonly Queue<UnityHookEvent> pendingUnityEvents = new();
        // Hook runs per instance id or Unity entry in this drain.
        private readonly Dictionary<object, int> lifecycleRuns = new();
        private Dictionary<Collider2D, INeoWorldObjectValue>? colliderValues;

        /// <summary>Whether renderers deliver Unity hooks: the runtime started and some class has one.</summary>
        internal bool UnityHooksActive =>
            scriptRuntimeStarted && !isDisposed && (schemaHooks & NeoLifecycleHooks.Unity) != 0;

        /// <summary>Whether renderers relay physics messages and index colliders.</summary>
        internal bool PhysicsHooksActive =>
            scriptRuntimeStarted && !isDisposed && (schemaHooks & NeoLifecycleHooks.Physics) != 0;

        private bool LifecycleHooksPending
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return pendingUnloads.Count != 0
                    || pendingLoads.Count != 0
                    || pendingUnityEvents.Count != 0
                    || departedNodes.Count != 0;
            }
        }

        /// <summary>The union of every class's hooks, from each interface closure once.</summary>
        private void ApplyLifecycleSchema()
        {
            NeoLifecycleHooks hooks = NeoLifecycleHooks.None;
            foreach (NeoSchemaClass schemaClass in data.classes.Values)
                hooks |= OwnInterfaceHooks(schemaClass);
            schemaHooks = hooks;
            if ((hooks & NeoLifecycleHooks.Physics) == 0)
                colliderValues = null;
            else
                colliderValues ??= new Dictionary<Collider2D, INeoWorldObjectValue>();
        }

        private NeoLifecycleHooks ClassHooks(IList<NeoSchemaClass> chain)
        {
            NeoLifecycleHooks hooks = NeoLifecycleHooks.None;
            for (int i = 0; i < chain.Count; i++)
                hooks |= OwnInterfaceHooks(chain[i]);
            return hooks;
        }

        private NeoLifecycleHooks OwnInterfaceHooks(NeoSchemaClass schemaClass)
        {
            NeoLifecycleHooks hooks = NeoLifecycleHooks.None;
            if (schemaClass.implementsInterfaceIds is { } interfaceIds)
                foreach (string interfaceId in interfaceIds)
                    hooks |= InterfaceHooks(interfaceId);
            return hooks;
        }

        private NeoLifecycleHooks InterfaceHooks(string interfaceId)
        {
            if (interfaceHooks.TryGetValue(interfaceId, out NeoLifecycleHooks hooks))
                return hooks;
            // An extension cycle contributes nothing more.
            interfaceHooks[interfaceId] = NeoLifecycleHooks.None;
            hooks = NeoLifecycleHookTable.ForInterface(interfaceId);
            if (data.interfaces.TryGetValue(interfaceId, out Interface? contract)
                && contract.extendsInterfaceIds is { } parents)
                foreach (string parent in parents)
                    hooks |= InterfaceHooks(parent);
            interfaceHooks[interfaceId] = hooks;
            return hooks;
        }

        /// <summary>Runs the first pending OnUnload, OnLoad or Unity event, if any.</summary>
        private bool RunPendingLifecycleHook()
        {
            if (pendingUnloads.Count != 0)
            {
                EffectInstance instance = pendingUnloads.Dequeue();
                RunDataHook(instance, NeoLifecycleHooks.OnUnload, departure: departedNodes.ContainsKey(instance.id));
                return true;
            }
            if (pendingLoads.Count != 0)
            {
                EffectInstance instance = pendingLoads.Dequeue();
                if (instance.loadPending)
                {
                    instance.loadPending = false;
                    RunDataHook(instance, NeoLifecycleHooks.OnLoad, departure: false);
                }
                return true;
            }
            if (pendingUnityEvents.Count != 0)
            {
                UnityHookEvent next = pendingUnityEvents.Dequeue();
                if (next.message == NeoLifecycleHooks.None)
                    next.entry.Advance();
                else
                    next.entry.Deliver(next.message, next.other, next.normal, next.relativeVelocity);
                return true;
            }
            return false;
        }

        private void RunDataHook(EffectInstance instance, NeoLifecycleHooks hook, bool departure)
        {
            MergedSchemaEntry entry = instance.node.HookMember(hook);
            if (!CountLifecycleRun(instance.id, instance.node.Id, entry, instance.id))
                return;
            RunLifecycleHook(
                instance.node,
                hook,
                EffectFunction(entry, instance.ownership),
                instance.id,
                Array.Empty<object?>(),
                departure);
        }

        /// <summary>
        /// Counts one drain run of a hook on <paramref name="key"/>, an
        /// instance id or a Unity entry, against <see cref="EffectRunsPerDrain"/>.
        /// </summary>
        /// <returns>False once the limit stops it for the rest of the drain.</returns>
        internal bool CountLifecycleRun(object key, string classId, MergedSchemaEntry entry, string instanceId)
        {
            lifecycleRuns.TryGetValue(key, out int runs);
            lifecycleRuns[key] = ++runs;
            if (runs <= EffectRunsPerDrain)
                return true;
            if (runs == EffectRunsPerDrain + 1)
            {
                Debug.LogException(new NeoEffectCycleException(
                    classId, entry.memberId, instanceId,
                    $"Lifecycle hooks on '{instanceId}' ran {EffectRunsPerDrain} times in one drain and stop until the next one, at '{entry.member?.name}'. Hooks that keep undoing each other's writes keep triggering each other."));
            }
            return false;
        }

        /// <summary>
        /// Runs one hook the drain reached. Its writes commit and its change
        /// callbacks deliver when it returns. A failure is logged, never
        /// thrown (P98 §2.5).
        /// </summary>
        /// <param name="departure">An OnUnload, OnDisable or OnDestroy whose receiver left the data: departed rows win over a replacement.</param>
        internal void RunLifecycleHook(
            NeoClassNode node,
            NeoLifecycleHooks hook,
            NeoMemberNSFunction function,
            string instanceId,
            object?[] args,
            bool departure)
        {
            BeginChangeBatch();
            DepartedReads enclosing = departedReads;
            departedReads = departure ? DepartedReads.Prefer : DepartedReads.Fallback;
            try
            {
                function.Invoke(instanceId, args);
            }
            catch (Exception exception)
            {
                LogLifecycleHookFailure(node, hook, instanceId, exception);
            }
            finally
            {
                departedReads = enclosing;
                EndChangeBatch();
            }
        }

        /// <summary>
        /// Runs a per-frame, application or Start hook as its own outermost
        /// execution. Its writes commit, its change callbacks deliver and the
        /// drain runs before it returns. It runs outside any drain, so it
        /// reads departed rows no more than an effect does (P98 §2.4).
        /// </summary>
        /// <param name="entry">The receiver's entry, read only to log a failure.</param>
        internal void RunOutermostHook(
            NeoLifecycleEntry entry,
            NeoLifecycleHooks hook,
            NeoMemberNSFunction function,
            string instanceId,
            object?[] args)
        {
            HoldGetterChanges();
            BeginChangeBatch();
            try
            {
                function.Invoke(instanceId, args);
            }
            catch (Exception exception)
            {
                entry.LogFailure(hook, exception);
            }
            finally
            {
                try
                {
                    EndChangeBatch();
                }
                finally
                {
                    ReleaseGetterChanges();
                }
            }
        }

        internal static void LogLifecycleHookFailure(NeoClassNode node, NeoLifecycleHooks hook, string instanceId, Exception exception)
        {
            MergedSchemaEntry entry = node.HookMember(hook);
            Debug.LogException(new NeoLifecycleHookException(
                node.Id, entry.memberId, instanceId,
                $"Lifecycle hook '{entry.member?.name}' on '{instanceId}' failed: {exception.Message}",
                exception));
        }

        private void EndLifecycleDrain()
        {
            if (lifecycleRuns.Count != 0)
                lifecycleRuns.Clear();
            if (departedNodes.Count != 0)
                ReleaseDepartedRows();
        }

        /// <summary>Queues a hooked GameObject's move to its current state.</summary>
        internal void QueueUnityTransition(NeoLifecycleEntry entry)
        {
            if (entry.queued || !UnityHooksActive)
                return;
            entry.queued = true;
            pendingUnityEvents.Enqueue(new UnityHookEvent(entry, NeoLifecycleHooks.None, null, default, default));
        }

        /// <summary>Queues a physics message a relay heard, and runs it when the drain may run.</summary>
        internal void QueueUnityMessage(
            NeoLifecycleEntry entry,
            NeoLifecycleHooks message,
            Collider2D other,
            Vector2 normal,
            Vector2 relativeVelocity)
        {
            if (!UnityHooksActive)
                return;
            INeoWorldObjectValue? otherValue = null;
            colliderValues?.TryGetValue(other, out otherValue);
            pendingUnityEvents.Enqueue(new UnityHookEvent(entry, message, otherValue, normal, relativeVelocity));
            DrainScriptRuntime();
        }

        /// <summary>Runs what is pending unless a boundary is open, which runs it when it closes.</summary>
        internal void DrainScriptRuntime()
        {
            if (getterChangeHolds != 0 || !EffectsPending)
                return;
            HoldGetterChanges();
            ReleaseGetterChanges();
        }

        /// <summary>Indexes a collider a renderer hosts for <paramref name="value"/>, so a physics message names it.</summary>
        internal void IndexCollider(Collider2D collider, INeoWorldObjectValue value)
        {
            if (colliderValues is not null && PhysicsHooksActive)
                colliderValues[collider] = value;
        }

        internal void UnindexCollider(Collider2D? collider)
        {
            if (colliderValues is not null && !ReferenceEquals(collider, null))
                colliderValues.Remove(collider);
        }

        /// <summary>
        /// Disposal runs no drain afterward, so every live instance's OnUnload
        /// runs first, while the client is whole. The runtime stops before
        /// they run, so nothing they write starts an instance, queues an
        /// effect or reaches a Unity hook.
        /// </summary>
        private void StopScriptRuntime()
        {
            List<EffectInstance>? unloads = null;
            if (instancesTracked && (schemaHooks & NeoLifecycleHooks.OnUnload) != 0)
                foreach (EffectInstance instance in effectInstances.Values)
                    if (!instance.loadPending && (instance.node.Hooks & NeoLifecycleHooks.OnUnload) != 0)
                        (unloads ??= new List<EffectInstance>()).Add(instance);
            StopEffects();
            pendingUnityEvents.Clear();
            scriptRuntimeStarted = false;
            if (unloads is null)
                return;
            foreach (EffectInstance instance in unloads)
            {
                HoldGetterChanges();
                try
                {
                    RunDataHook(instance, NeoLifecycleHooks.OnUnload, departure: false);
                }
                finally
                {
                    ReleaseGetterChanges();
                }
            }
            lifecycleRuns.Clear();
            DisposeEffectFunctions();
        }
    }
}
