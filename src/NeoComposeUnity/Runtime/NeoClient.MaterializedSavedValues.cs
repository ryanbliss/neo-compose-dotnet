// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // P130 §4. A materialized row never replays, so a row built from
        // mutable state would come back different after a save and reload.
        // While a root materializes, each construction step records whether
        // it read mutable state. The rows it produced become saved values,
        // and the attribution is discarded once the root installs.
        internal sealed class ReadFrame
        {
            internal ReadFrame? Parent;
            internal bool Mutable;
        }

        private sealed class SavedValueScope
        {
            internal SavedValueScope? Parent;
            internal string RootId = null!;
            // Session rows this materialization wrote: its own
            // temporaries, never outside state.
            internal readonly HashSet<string> OwnRows = new(StringComparer.Ordinal);
            // Rows written while their step had read mutable state.
            internal readonly HashSet<string> MutableRows = new(StringComparer.Ordinal);
            // Objects an initializer chose from mutable reads: the object row
            // saves, and its fields keep their own attribution.
            internal readonly HashSet<string> MutableObjects = new(StringComparer.Ordinal);
            // Script locals and parameters holding a value read from mutable
            // state. Reading one carries the taint into the row it is written to.
            internal readonly HashSet<string> TaintedLocals = new(StringComparer.Ordinal);
            // Constructor argument arrays with at least one argument read from
            // mutable state, by position. A parameter bound from one is tainted.
            internal readonly Dictionary<object, bool[]> ArgumentTaint =
                new(NeoScript.NSGetterEvaluator.ReferenceEqualityComparer.Instance);
        }

        private SavedValueScope? savedValueScope;
        private ReadFrame? readFrame;

        // Script scopes hold no client, so the materializing one is static.
        // Evaluation is single-threaded; a scope restores its outer one.
        private static NeoClient? localTaintClient;

        private IDisposable BeginSavedValueScope(string rootId)
        {
            var scope = new SavedValueScope { Parent = savedValueScope, RootId = rootId };
            var frame = new ReadFrame { Parent = readFrame };
            NeoClient? outerTaintClient = localTaintClient;
            savedValueScope = scope;
            readFrame = frame;
            localTaintClient = this;
            return new NeoDisposableAction(() =>
            {
                savedValueScope = scope.Parent;
                readFrame = frame.Parent;
                localTaintClient = outerTaintClient;
            });
        }

        /// <summary>Whether reads are being attributed: a root is materializing.</summary>
        internal static bool MeasuresReads => localTaintClient?.readFrame is not null;

        /// <summary>Whether a script local read can carry taint: some local holds a mutable read.</summary>
        internal static bool TracksLocalTaint => localTaintClient?.savedValueScope is { TaintedLocals: { Count: > 0 } };

        internal static void NoteLocalRead(string variableId)
        {
            if (localTaintClient is { readFrame: { Mutable: false } frame, savedValueScope: { } scope }
                && scope.TaintedLocals.Contains(variableId))
                frame.Mutable = true;
        }

        internal readonly struct LocalTaint
        {
            internal readonly ReadFrame? Frame;
            internal readonly bool Prior;
            internal LocalTaint(ReadFrame? frame, bool prior)
            {
                Frame = frame;
                Prior = prior;
            }
        }

        /// <summary>Starts measuring the reads behind one value.</summary>
        internal static LocalTaint BeginLocalTaint()
        {
            if (localTaintClient?.readFrame is not { } frame)
                return default;
            bool prior = frame.Mutable;
            frame.Mutable = false;
            return new LocalTaint(frame, prior);
        }

        /// <summary>
        /// Ends a measurement without charging its reads to the current step:
        /// the value carries them instead. Returns whether it read mutable state.
        /// </summary>
        internal static bool EndValueTaint(LocalTaint taint)
        {
            if (taint.Frame is not { } frame)
                return false;
            bool mutable = frame.Mutable;
            frame.Mutable = taint.Prior;
            return mutable;
        }

        /// <summary>Marks <paramref name="variableId"/> by whether its new value came from mutable reads.</summary>
        internal static void EndLocalTaint(LocalTaint taint, string variableId)
        {
            bool mutable = EndValueTaint(taint);
            if (taint.Frame is null || localTaintClient?.savedValueScope is not { } scope)
                return;
            if (mutable)
                scope.TaintedLocals.Add(variableId);
            else
                scope.TaintedLocals.Remove(variableId);
        }

        /// <summary>Records which of <paramref name="arguments"/> were read from mutable state.</summary>
        internal static void NoteArgumentTaint(object?[] arguments, bool[]? taint)
        {
            if (localTaintClient?.savedValueScope is not { } scope)
                return;
            if (taint is null || Array.IndexOf(taint, true) < 0)
                scope.ArgumentTaint.Remove(arguments);
            else
                scope.ArgumentTaint[arguments] = taint;
        }

        /// <summary>Carries <paramref name="source"/>'s argument taint to a copy of it.</summary>
        internal static void CopyArgumentTaint(object?[] source, object?[] copy)
        {
            if (!ReferenceEquals(source, copy)
                && localTaintClient?.savedValueScope is { } scope
                && scope.ArgumentTaint.TryGetValue(source, out bool[]? taint))
                scope.ArgumentTaint[copy] = taint;
        }

        /// <summary>
        /// Binds the taint of <paramref name="arguments"/> to the parameter
        /// ids <paramref name="parameterId"/> names, until disposed. A
        /// parameter id can recur in a nested call, so the outer taint returns.
        /// </summary>
        internal static ParameterTaint BindParameterTaint(object?[] arguments, Func<int, string?> parameterId)
        {
            if (localTaintClient?.savedValueScope is not { } scope)
                return default;
            scope.ArgumentTaint.TryGetValue(arguments, out bool[]? taint);
            List<(string id, bool tainted)>? prior = null;
            for (int i = 0; i < arguments.Length; i++)
            {
                bool tainted = taint is not null && i < taint.Length && taint[i];
                if (parameterId(i) is not { } id)
                    continue;
                bool was = scope.TaintedLocals.Contains(id);
                if (was == tainted)
                    continue;
                (prior ??= new()).Add((id, was));
                if (tainted)
                    scope.TaintedLocals.Add(id);
                else
                    scope.TaintedLocals.Remove(id);
            }
            return new ParameterTaint(scope, prior);
        }

        internal readonly struct ParameterTaint : IDisposable
        {
            private readonly SavedValueScope? scope;
            private readonly List<(string id, bool tainted)>? prior;

            internal ParameterTaint(object scope, List<(string id, bool tainted)>? prior)
            {
                this.scope = (SavedValueScope)scope;
                this.prior = prior;
            }

            public void Dispose()
            {
                if (scope is null || prior is null)
                    return;
                foreach (var (id, tainted) in prior)
                {
                    if (tainted)
                        scope.TaintedLocals.Add(id);
                    else
                        scope.TaintedLocals.Remove(id);
                }
            }
        }

        /// <summary>Charges the next write to a value measured as <paramref name="mutable"/>.</summary>
        internal static void ChargeNextWrite(bool mutable)
        {
            if (localTaintClient?.readFrame is { } frame)
                frame.Mutable = mutable;
        }

        /// <summary>A construction step whose reads belong only to the rows it produces.</summary>
        internal ReadFrame? EnterReadFrame()
        {
            if (savedValueScope is null)
                return null;
            return readFrame = new ReadFrame { Parent = readFrame };
        }

        /// <summary>
        /// Ends <paramref name="frame"/>. When it read mutable state, the
        /// value it produced saves: a leaf or collection whole, an object as
        /// its own row, since that read chose its class.
        /// </summary>
        internal void ExitReadFrame(ReadFrame? frame, string? producedId)
        {
            if (frame is null)
                return;
            readFrame = frame.Parent;
            if (frame.Mutable && producedId is not null && savedValueScope is { } scope)
            {
                scope.MutableRows.Add(producedId);
                scope.MutableObjects.Add(producedId);
            }
        }

        /// <summary>
        /// Rows a replay commits take the reads made since the step's previous
        /// commit, so a literal written after a capture does not save.
        /// </summary>
        private void AttributeMutableWrites(NeoWritePlan plan)
        {
            if (readFrame is not { } frame || savedValueScope is not { } scope)
                return;
            if (!frame.Mutable)
                return;
            foreach (var key in plan.Rows.Keys)
                if (key.ownership == NeoValueOwnership.Session)
                    scope.MutableRows.Add(key.id);
            frame.Mutable = false;
        }

        private void NoteOwnSessionRow(string id)
        {
            for (var scope = savedValueScope; scope is not null; scope = scope.Parent)
                scope.OwnRows.Add(id);
        }

        private void NoteMutableRead(string dependency)
        {
            if (readFrame is not { Mutable: false } frame || savedValueScope is not { } scope)
                return;
            string id = DependencyValueId(dependency);
            if (id == scope.RootId)
                return;
            if (scope.OwnRows.Contains(id) || IsScopeAllocation(id))
            {
                if (scope.MutableRows.Contains(id))
                    frame.Mutable = true;
                return;
            }
            // Resolving ownership reads rows of its own.
            ReadFrame? reading = readFrame;
            readFrame = null;
            try
            {
                if (TryGetReplayDefaultRow(id, out _))
                    return;
                // Session state resets on load, so an unwritten Session default
                // reads the same after a reload: only a written row is state.
                if (TryGetValueOwnership(id, out NeoValueOwnership ownership) && ownership != NeoValueOwnership.Asset
                    && (ownership != NeoValueOwnership.Session || sessionValues.ContainsKey(id)))
                    frame.Mutable = true;
            }
            finally
            {
                readFrame = reading;
            }
        }

        private bool IsScopeAllocation(string id)
        {
            for (var scope = replayAllocationScope; scope is not null; scope = scope.Parent)
                if (scope.Ids.Contains(id))
                    return true;
            return candidateReplay?.Allocations.ContainsKey(id) == true;
        }

        /// <summary>
        /// The persisted rows of <paramref name="graph"/> that came from
        /// mutable reads: leaves, objects whose class those reads chose, and
        /// collections with every row beneath them, since their membership is
        /// part of the value. Session roots save nothing: nothing reloads them.
        /// </summary>
        private void CollectSavedValues(VirtualExpansionNode graph, PreparedVirtualExpansion expansion)
        {
            if (savedValueScope is not { MutableRows: { Count: > 0 } } scope)
                return;
            if (expansion.RootOwnership is not (NeoValueOwnership.Save or NeoValueOwnership.User))
                return;
            Walk(graph);

            void Walk(VirtualExpansionNode node)
            {
                if (node.independentRoot is not null && node != graph)
                    return;
                if (scope.MutableRows.Contains(node.row.id))
                {
                    if (IsCollection(node))
                    {
                        SaveAll(node);
                        return;
                    }
                    if (IsSavedLeaf(node) || scope.MutableObjects.Contains(node.row.id))
                        Save(node);
                }
                foreach (var child in node.classChildren.Values)
                    Walk(child);
                foreach (var child in node.listChildren)
                    Walk(child);
                foreach (var child in node.dictionaryChildren.Values)
                    Walk(child);
            }

            void SaveAll(VirtualExpansionNode node)
            {
                if (node.independentRoot is not null && node != graph)
                    return;
                Save(node);
                foreach (var child in node.classChildren.Values)
                    SaveAll(child);
                foreach (var child in node.listChildren)
                    SaveAll(child);
                foreach (var child in node.dictionaryChildren.Values)
                    SaveAll(child);
            }

            void Save(VirtualExpansionNode node)
            {
                if (ValueOwner(expansion, node.virtualId) is { } owner)
                    (owner.SavedValues ??= new()).Add(node.virtualId);
            }
        }

        /// <summary>The expansion, outer or partitioned nested, that answers <paramref name="id"/>.</summary>
        private static PreparedVirtualExpansion? ValueOwner(PreparedVirtualExpansion expansion, string id)
        {
            if (expansion.Values.ContainsKey(id))
                return expansion;
            foreach (var nested in expansion.Nested)
                if (ValueOwner(nested, id) is { } owner)
                    return owner;
            return null;
        }

        private static bool IsCollection(VirtualExpansionNode node) =>
            node.member is ListMember or DictionaryMember;

        private static bool IsSavedLeaf(VirtualExpansionNode node) =>
            node.classChildren.Count == 0 && node.listChildren.Count == 0 && node.dictionaryChildren.Count == 0
            && IsLeafRow(node.row, node.member);

        /// <summary>
        /// Writes the expansion's saved values into the overlay without a
        /// change: the value is the one consumers already see. A value the
        /// overlay already holds wins.
        /// </summary>
        private void InstallSavedValues(PreparedVirtualExpansion expansion)
        {
            if (expansion.SavedValues is null)
                return;
            foreach (string id in expansion.SavedValues)
            {
                if (!virtualValues.TryGetValue(id, out MemberValue? row)
                    || !virtualValueOwnership.TryGetValue(id, out NeoValueOwnership ownership))
                    continue;
                if (ownership is not (NeoValueOwnership.Save or NeoValueOwnership.User))
                    continue;
                if (ownership == NeoValueOwnership.User && !IsUserClient)
                    continue;
                if (GetWritableStore(ownership).values.ContainsKey(id))
                    continue;
                StoreWritableValue(ownership, CloneRowForWrite(row));
            }
        }
    }
}
