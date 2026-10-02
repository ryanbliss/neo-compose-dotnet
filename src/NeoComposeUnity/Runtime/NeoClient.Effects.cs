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
        // P97 effects: an @effect function runs for every live instance of
        // its class, then again whenever a read of its last run changes. Its
        // reads sit in the getter memo's dependency index under an Effect
        // key, so a change queues it where it would forget a getter. The
        // queue drains at the outermost getter-change release, before the
        // watchers hear the settled getters.

        /// <summary>How often one effect may run in a drain before it stops for that drain.</summary>
        public const int EffectRunsPerDrain = 100;

        private sealed class EffectState
        {
            internal readonly GetterMemoKey key;
            internal readonly MergedSchemaEntry entry;
            internal NeoMemberNSFunction? function;
            internal List<GetterRead>? reads;
            internal int drain;
            internal int runs;
            internal bool queued;
            internal bool stopped;

            internal EffectState(GetterMemoKey key, MergedSchemaEntry entry)
            {
                this.key = key;
                this.entry = entry;
            }
        }

        private sealed class EffectInstance
        {
            internal readonly NeoClassNode node;
            internal readonly NeoValueOwnership ownership;
            internal readonly GetterMemoKey[] keys;
            internal string? containerId;

            internal EffectInstance(NeoClassNode node, NeoValueOwnership ownership, GetterMemoKey[] keys, string? containerId)
            {
                this.node = node;
                this.ownership = ownership;
                this.keys = keys;
                this.containerId = containerId;
            }
        }

        private bool effectsRequested;
        // Whether a requested schema declares an @effect: without one, every
        // hook below stays a field check.
        private bool effectsStarted;
        private readonly Dictionary<GetterMemoKey, EffectState> effectsByKey = new();
        private readonly Dictionary<string, EffectInstance> effectInstances = new(StringComparer.Ordinal);
        // A stopped state may stay queued; the drain skips it.
        private readonly Queue<EffectState> pendingEffects = new();
        // Rows whose effect instances a drain must start, stop or keep.
        private readonly List<string> dirtyEffectRows = new();
        private readonly HashSet<string> dirtyEffectRowSet = new(StringComparer.Ordinal);
        // Writable effect rows no live graph holds yet: a later commit may
        // attach one by writing only its new parent.
        private readonly HashSet<string> effectOrphans = new(StringComparer.Ordinal);
        private bool effectOrphansDirty;
        private bool effectRescanPending;
        // A boundary released effects it could not run, which the blocker's
        // own exit runs.
        private bool effectDrainDeferred;
        private readonly Dictionary<string, bool> effectLiveness = new(StringComparer.Ordinal);
        private readonly List<string> effectLivenessPath = new();
        private readonly Dictionary<(string memberId, NeoValueOwnership ownership), NeoMemberNSFunction> effectFunctions = new();
        // The run's own capture, which native calls suspend.
        private List<GetterRead>? effectReadCapture;
        // The running effect, which its own writes never queue (P97 §2.4).
        private GetterMemoKey? runningEffect;
        private int effectDrain;

        /// <summary>
        /// Starts the project's <c>@effect</c> functions: each runs once for
        /// every live instance, then again whenever what it read changes.
        /// The generated client wrapper calls this once its native function
        /// invokers are registered. Idempotent.
        /// </summary>
        public void StartEffects()
        {
            EnsureNotDisposed();
            if (effectsRequested)
                return;
            effectsRequested = true;
            ApplyEffectSchema();
        }

        private void ApplyEffectSchema()
        {
            if (!effectsRequested)
                return;
            DisposeEffectFunctions();
            bool hasEffects = false;
            foreach (Member member in data.members.Values)
            {
                if (member is NSFunctionMember { DeclaredEffect: NeoEffectKind.Auto })
                {
                    hasEffects = true;
                    break;
                }
            }
            if (!hasEffects)
            {
                StopEffects();
                return;
            }
            effectsStarted = true;
            effectRescanPending = true;
            HoldGetterChanges();
            ReleaseGetterChanges();
        }

        private void StopEffects()
        {
            effectsStarted = false;
            foreach (var pair in effectsByKey)
                if (pair.Value.reads is { } reads)
                    UnindexReads(pair.Key, reads);
            effectsByKey.Clear();
            effectInstances.Clear();
            pendingEffects.Clear();
            dirtyEffectRows.Clear();
            dirtyEffectRowSet.Clear();
            effectOrphans.Clear();
            effectOrphansDirty = false;
            effectRescanPending = false;
            effectDrainDeferred = false;
            DisposeEffectFunctions();
        }

        private void DisposeEffectFunctions()
        {
            foreach (NeoMemberNSFunction function in effectFunctions.Values)
                function.Dispose();
            effectFunctions.Clear();
            foreach (EffectState state in effectsByKey.Values)
                state.function = null;
        }

        private bool EffectsPending
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return effectsStarted
                    && (pendingEffects.Count != 0 || dirtyEffectRows.Count != 0 || effectOrphansDirty || effectRescanPending);
            }
        }

        // An effect is its own outermost execution: it never runs inside
        // another, a replay, a candidate read or a dependency capture.
        private bool CanDrainEffects =>
            !isDisposed
            && scriptWriteDepth == 0
            && commitsUnderScriptBatch == 0
            && candidateReplay is null
            && candidateReadPlan is null
            && replayAllocationScope is null
            && nestedConstructorCapture is null
            && !isReplayingVirtualInstance
            && getterReadCapture is null
            && getterValueReadCapture is null
            && capturedValueReads is null;

        /// <summary>A write or removal at <paramref name="id"/> in one store; <paramref name="value"/> is null for a removal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NoteEffectRowChange(NeoValueOwnership ownership, string id, MemberValue? value)
        {
            if (effectsStarted)
                TrackEffectRowChange(ownership, id, value);
        }

        private void TrackEffectRowChange(NeoValueOwnership ownership, string id, MemberValue? value)
        {
            // Only a row that holds others can attach an orphan's ancestor.
            if (effectOrphans.Count != 0 && value is ObjectMemberValue or ArrayMemberValue)
                effectOrphansDirty = true;
            // A row's kind is its member's, so only an object row or a
            // removal can start, keep or stop an instance.
            if (value is not (null or ObjectMemberValue))
                return;
            if (effectInstances.TryGetValue(id, out EffectInstance? tracked))
            {
                // A write that leaves an instance in place, in a store no
                // nearer than the one it resolves from, keeps it live.
                if (value is ObjectMemberValue { IsRemoved: false } row
                    && row.classId == tracked.node.Id
                    && row.containerId == tracked.containerId
                    && ownership <= tracked.ownership)
                    return;
                MarkEffectRow(id);
            }
            else if (value is null
                ? effectOrphans.Contains(id)
                : value is ObjectMemberValue { IsRemoved: false, classId: { } classId }
                    && ResolveClassNode(classId).Effects.Length != 0)
                MarkEffectRow(id);
        }

        private void MarkEffectRow(string id)
        {
            if (dirtyEffectRowSet.Add(id))
                dirtyEffectRows.Add(id);
        }

        /// <summary>A loaded or unloaded authored row: its instance, and the effects that read it or its container.</summary>
        private void NoteEffectPartitionRow(MemberValue row)
        {
            if (!effectsStarted)
                return;
            if (effectInstances.ContainsKey(row.id)
                || row is ObjectMemberValue { classId: { } classId } && ResolveClassNode(classId).Effects.Length != 0)
                MarkEffectRow(row.id);
            InvalidateGetterMemoForRow(row.id);
            if (!string.IsNullOrEmpty(row.containerId))
                InvalidateGetterMemoForRow(row.containerId!);
        }

        /// <summary>
        /// A loaded partition may hold orphaned writable rows; an unloaded one
        /// may have held live ones.
        /// </summary>
        private void NoteEffectPartitionChange(bool loaded)
        {
            if (!effectsStarted)
                return;
            if (loaded)
            {
                if (effectOrphans.Count != 0)
                    effectOrphansDirty = true;
                return;
            }
            foreach (var pair in effectInstances)
                if (pair.Value.ownership != NeoValueOwnership.Asset)
                    MarkEffectRow(pair.Key);
        }

        /// <summary>A static binding changed, which may attach an orphan.</summary>
        private void NoteEffectBindingChange()
        {
            if (effectsStarted && effectOrphans.Count != 0)
                effectOrphansDirty = true;
        }

        private void QueueEffect(GetterMemoKey key)
        {
            if (runningEffect is { } running && running.Equals(key))
                return;
            if (effectsByKey.TryGetValue(key, out EffectState? state))
                Enqueue(state);
        }

        private void Enqueue(EffectState state)
        {
            if (state.queued)
                return;
            state.queued = true;
            pendingEffects.Enqueue(state);
        }

        private void DrainEffects()
        {
            if (!CanDrainEffects)
            {
                effectDrainDeferred = true;
                return;
            }
            effectDrainDeferred = false;
            effectDrain++;
            while (true)
            {
                SyncEffectInstances();
                if (pendingEffects.Count == 0)
                    return;
                EffectState state = pendingEffects.Dequeue();
                state.queued = false;
                if (state.stopped)
                    continue;
                GetterMemoKey key = state.key;
                if (state.drain != effectDrain)
                {
                    state.drain = effectDrain;
                    state.runs = 0;
                }
                if (state.runs == EffectRunsPerDrain)
                {
                    state.runs++;
                    Debug.LogException(new NeoEffectCycleException(
                        ClassIdOfEffect(key), key.memberId, key.rowId,
                        $"Effect '{state.entry.member?.name}' on '{key.rowId}' ran {EffectRunsPerDrain} times in one drain and stops until its next change. Effects that write what each other read keep triggering each other."));
                    continue;
                }
                if (state.runs > EffectRunsPerDrain)
                    continue;
                state.runs++;
                RunEffect(state);
            }
        }

        /// <summary>Runs effects a blocked boundary released, once the blocker exits.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void DrainDeferredEffects()
        {
            if (effectDrainDeferred && getterChangeHolds == 0 && CanDrainEffects)
            {
                HoldGetterChanges();
                ReleaseGetterChanges();
            }
        }

        private void RunEffect(EffectState state)
        {
            // Value callbacks the run's writes reach run after its capture:
            // what they read is never the effect's.
            BeginChangeBatch();
            try
            {
                RunEffectCaptured(state);
            }
            finally
            {
                EndChangeBatch();
            }
        }

        private void RunEffectCaptured(EffectState state)
        {
            GetterMemoKey key = state.key;
            // Its previous reads stay indexed through the run, since a run
            // usually reads the same rows again.
            GetterCaptureFrame enclosing = BeginGetterReadCapture(readsOnly: true);
            effectReadCapture = getterReadCapture;
            runningEffect = key;
            GetterCaptureFrame capture;
            try
            {
                (state.function ??= EffectFunction(state)).Invoke(key.rowId, Array.Empty<object?>());
            }
            catch (Exception exception)
            {
                Debug.LogException(new NeoEffectException(
                    ClassIdOfEffect(key), key.memberId, key.rowId,
                    $"Effect '{state.entry.member?.name}' on '{key.rowId}' failed: {exception.Message}",
                    exception));
            }
            finally
            {
                runningEffect = null;
                effectReadCapture = null;
                capture = EndGetterReadCapture(enclosing);
            }
            // A run that detached its own instance stopped it, which
            // unindexed its reads.
            if (state.stopped)
            {
                RecycleGetterCapture(capture);
                return;
            }
            // Kept as read: a deterministic run repeats its reads in order, and
            // the index takes a repeat as a no-op.
            List<GetterRead> reads = capture.reads!;
            if (state.reads is { } previous)
            {
                if (SameReads(previous, reads))
                {
                    RecycleGetterCapture(capture);
                    return;
                }
                UnindexReads(key, previous);
            }
            state.reads = reads;
            IndexReads(key, reads);
        }

        // A rewritten row is read through its new row's id, an equal string.
        private static bool SameReads(List<GetterRead> previous, List<GetterRead> reads)
        {
            if (previous.Count != reads.Count)
                return false;
            for (int i = 0; i < reads.Count; i++)
            {
                GetterRead was = previous[i];
                GetterRead read = reads[i];
                if (!ReferenceEquals(was.content, read.content)
                    || was.cell != read.cell
                    || was.tile != read.tile
                    || !string.Equals(was.id, read.id))
                    return false;
            }
            return true;
        }

        private NeoMemberNSFunction EffectFunction(EffectState state)
        {
            GetterMemoKey key = state.key;
            if (!effectFunctions.TryGetValue((key.memberId, key.ownership), out NeoMemberNSFunction? function))
                effectFunctions[(key.memberId, key.ownership)] = function =
                    new NeoMemberNSFunction(this, (NSFunctionMember)state.entry.member!, null, key.ownership);
            return function;
        }

        private string ClassIdOfEffect(GetterMemoKey key) =>
            effectInstances.TryGetValue(key.rowId, out EffectInstance? instance) ? instance.node.Id : "";

        /// <summary>Whether a native call runs inside an effect's own capture, which it must not record into.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool InEffectCapture() =>
            effectReadCapture is not null && ReferenceEquals(getterReadCapture, effectReadCapture);

        /// <summary>Starts, replaces or stops the instances at the changed rows.</summary>
        private void SyncEffectInstances()
        {
            if (effectRescanPending)
            {
                effectRescanPending = false;
                MarkEveryEffectRow();
            }
            if (effectOrphansDirty)
            {
                effectOrphansDirty = false;
                foreach (string id in effectOrphans)
                    MarkEffectRow(id);
            }
            if (dirtyEffectRows.Count == 0)
                return;
            // An instance's ancestors are shared, so each is walked once.
            effectLiveness.Clear();
            for (int i = 0; i < dirtyEffectRows.Count; i++)
                SyncEffectInstance(dirtyEffectRows[i]);
            dirtyEffectRows.Clear();
            dirtyEffectRowSet.Clear();
            effectLiveness.Clear();
        }

        // First runs go by store, then by row order (P97 §2.7). A virtual
        // row belongs to its root's store.
        private void MarkEveryEffectRow()
        {
            List<string>? virtualSave = null;
            List<string>? virtualSession = null;
            MarkEffectRows(data.values);
            foreach (var pair in virtualValues)
            {
                if (!IsEffectRow(pair.Value) || !virtualValueOwnership.TryGetValue(pair.Key, out NeoValueOwnership ownership))
                    continue;
                if (ownership == NeoValueOwnership.Asset)
                    MarkEffectRow(pair.Key);
                else if (ownership == NeoValueOwnership.Save)
                    (virtualSave ??= new List<string>()).Add(pair.Key);
                else
                    (virtualSession ??= new List<string>()).Add(pair.Key);
            }
            MarkEffectRows(saveValues);
            if (virtualSave is not null)
                foreach (string id in virtualSave)
                    MarkEffectRow(id);
            MarkEffectRows(sessionValues);
            if (virtualSession is not null)
                foreach (string id in virtualSession)
                    MarkEffectRow(id);
            foreach (string id in effectInstances.Keys)
                MarkEffectRow(id);
        }

        private void MarkEffectRows(IEnumerable<KeyValuePair<string, MemberValue>> rows)
        {
            foreach (var pair in rows)
                if (IsEffectRow(pair.Value))
                    MarkEffectRow(pair.Key);
        }

        private bool IsEffectRow(MemberValue row) =>
            row is ObjectMemberValue { IsRemoved: false, classId: { } classId }
            && ResolveClassNode(classId).Effects.Length != 0;

        private void SyncEffectInstance(string id)
        {
            NeoClassNode? node = null;
            string? containerId = null;
            if (TryGetCommittedOwnership(id, out NeoValueOwnership ownership)
                && ResolveValueRow(id) is ObjectMemberValue { IsRemoved: false, classId: { } classId } row)
            {
                NeoClassNode candidate = ResolveClassNode(classId);
                if (candidate.Effects.Length != 0)
                {
                    node = candidate;
                    containerId = row.containerId;
                }
            }
            bool live = node is not null && IsEffectInstanceLive(ownership, id);
            if (effectInstances.TryGetValue(id, out EffectInstance? tracked))
            {
                if (live && ReferenceEquals(tracked.node, node) && tracked.ownership == ownership)
                {
                    tracked.containerId = containerId;
                    return;
                }
                StopEffectInstance(id, tracked);
            }
            if (live)
            {
                effectOrphans.Remove(id);
                StartEffectInstance(id, node!, ownership, containerId);
            }
            else if (node is not null && ownership != NeoValueOwnership.Asset)
                effectOrphans.Add(id);
            else
                effectOrphans.Remove(id);
        }

        private void StartEffectInstance(string id, NeoClassNode node, NeoValueOwnership ownership, string? containerId)
        {
            MergedSchemaEntry[] effects = node.Effects;
            var keys = new GetterMemoKey[effects.Length];
            for (int i = 0; i < effects.Length; i++)
            {
                var key = new GetterMemoKey(ownership, id, effects[i].memberId, ownership, DependentKind.Effect);
                keys[i] = key;
                var state = new EffectState(key, effects[i]);
                effectsByKey[key] = state;
                Enqueue(state);
            }
            effectInstances[id] = new EffectInstance(node, ownership, keys, containerId);
        }

        private void StopEffectInstance(string id, EffectInstance instance)
        {
            foreach (GetterMemoKey key in instance.keys)
            {
                if (!effectsByKey.Remove(key, out EffectState? state))
                    continue;
                state.stopped = true;
                if (state.reads is { } reads)
                    UnindexReads(key, reads);
            }
            effectInstances.Remove(id);
        }

        // An authored row is live; a writable row is live while an authored
        // row, its store's root or a static binding owns it, through its
        // owned parents (P97 §2.1).
        private bool IsEffectInstanceLive(NeoValueOwnership ownership, string id)
        {
            effectLivenessPath.Clear();
            string current = id;
            bool live = false;
            // An owned chain is a tree, so a cycle is malformed data.
            for (int depth = 0; depth < 256; depth++)
            {
                if (ownership == NeoValueOwnership.Asset)
                {
                    live = true;
                    break;
                }
                if (effectLiveness.TryGetValue(current, out live))
                    break;
                effectLivenessPath.Add(current);
                if (!TryFindOwnedParent(ownership, current, out string? parent))
                    break;
                if (parent.StartsWith("static:", StringComparison.Ordinal))
                {
                    live = true;
                    break;
                }
                if (parent.StartsWith("member:", StringComparison.Ordinal))
                {
                    live = IsRootMemberEdge(parent) || IsStaticDefaultEdge(parent, ownership, current);
                    break;
                }
                if (!TryGetCommittedOwnership(parent, out ownership))
                    break;
                current = parent;
            }
            foreach (string visited in effectLivenessPath)
                effectLiveness[visited] = live;
            return live;
        }

        private bool IsRootMemberEdge(string edge)
        {
            Project project = data.project;
            return IsMemberEdge(edge, project.rootSaveFileMemberId)
                || IsMemberEdge(edge, project.rootSessionMemberId);
        }

        // An unrebound writable static holds its default through its member edge.
        private bool IsStaticDefaultEdge(string edge, NeoValueOwnership ownership, string id) =>
            TryGetMember(edge.Substring("member:".Length), out Member? member)
            && member.Modifier == NeoMemberModifierKind.Static
            && IsStaticBoundTo(member, ownership, id);

        private static bool IsMemberEdge(string edge, string? memberId) =>
            memberId is not null
            && edge.Length == "member:".Length + memberId.Length
            && string.CompareOrdinal(edge, "member:".Length, memberId, 0, memberId.Length) == 0;
    }
}
