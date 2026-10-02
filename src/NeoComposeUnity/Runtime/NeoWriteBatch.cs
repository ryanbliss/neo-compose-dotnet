// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// NeoScript collection mutations staged into one plan. A list grows in
    /// a buffer its row takes once, when the batch is prepared, and the
    /// entries mutations release are staged then too: a row is an exact
    /// array, so writing it per mutation would make N mutations cost N².
    /// The client holds one open across a NeoScript execution
    /// (<see cref="NeoClient.ScriptWriteBatch"/>); a mutation outside one
    /// prepares and commits its own.
    /// </summary>
    internal sealed class NeoWriteBatch
    {
        internal readonly NeoWritePlan Plan;
        private readonly bool held;
        private readonly Dictionary<(NeoValueOwnership ownership, string id), PendingCollection> collections = new();
        private List<(NeoValueOwnership ownership, string id, Member? member)>? releases;
        // The entries pending lists gained or released. Their rows don't
        // hold that membership until the batch is prepared.
        private HashSet<string>? entries;
        private int staging;

        internal NeoWriteBatch(NeoWritePlan plan, bool held)
        {
            Plan = plan;
            this.held = held;
        }

        internal bool IsStaging => staging != 0;

        /// <summary>Runs one mutation. One that throws leaves the batch as it was.</summary>
        internal void Stage(Action<NeoWriteBatch> mutate)
        {
            NeoWritePlan.Checkpoint checkpoint = Plan.Open();
            staging++;
            try
            {
                using (Plan.Client.ReadCandidate(Plan))
                    mutate(this);
            }
            catch
            {
                Plan.Rollback(checkpoint);
                throw;
            }
            finally
            {
                staging--;
            }
            Plan.Close();
        }

        /// <summary>
        /// Whether the batch stages <paramref name="id"/>'s row, mutates it
        /// or changes its membership, so a read of it must wait for the commit.
        /// </summary>
        internal bool Stages(string id) =>
            Plan.Rows.ContainsKey((NeoValueOwnership.Save, id))
            || Plan.Rows.ContainsKey((NeoValueOwnership.Session, id))
            || collections.ContainsKey((NeoValueOwnership.Save, id))
            || collections.ContainsKey((NeoValueOwnership.Session, id))
            || entries?.Contains(id) == true;

        /// <summary>Whether a pending list gained or released <paramref name="id"/>.</summary>
        internal bool HoldsEntry(string id) => entries?.Contains(id) == true;

        /// <summary>The target a mutation of a pending collection resolved to.</summary>
        internal object? Target(NeoValueOwnership ownership, string id) =>
            collections.TryGetValue((ownership, id), out PendingCollection? collection) ? collection.Target : null;

        /// <summary>The target a mutation of pending collection <paramref name="id"/> resolved to, in whichever store holds it.</summary>
        internal object? Target(string id) => Target(NeoValueOwnership.Save, id) ?? Target(NeoValueOwnership.Session, id);

        /// <summary>
        /// Runs <paramref name="callback"/> once the mutation commits. A held
        /// batch commits later, so it runs now: the allocation tracker an
        /// execution exits must already see the parents its roots gained.
        /// </summary>
        internal void AfterCommit(Action callback)
        {
            if (held)
                callback();
            else
                Plan.AfterCommit(callback);
        }

        internal PendingList List(NeoValueOwnership ownership, string id, NSGetterEvaluator.Context ctx, object target)
        {
            if (!collections.TryGetValue((ownership, id), out PendingCollection? collection))
            {
                if (!Plan.TryGet(ownership, id, out ArrayMemberValue? row))
                    throw new NSGetterRuntimeError($"Missing list row '{id}'.");
                collection = new PendingList((ArrayMemberValue)Plan.Client.CloneRowForWrite(row!, sharesArrayEntries: true), target);
                collections.Add((ownership, id), collection);
            }
            collection.Touch(ctx);
            return (PendingList)collection;
        }

        internal PendingCollection Dictionary(NeoValueOwnership ownership, string id, NSGetterEvaluator.Context ctx, object target)
        {
            if (!collections.TryGetValue((ownership, id), out PendingCollection? collection))
            {
                if (!Plan.TryGet(ownership, id, out ObjectMemberValue? row))
                    throw new NSGetterRuntimeError($"Missing dictionary row '{id}'.");
                row = (ObjectMemberValue)Plan.Client.CloneRowForWrite(row!);
                row.value ??= new Dictionary<string, string>();
                // Staged now: a dictionary changes in place, so its row is
                // always current.
                Plan.Set(ownership, row, silent: true);
                collection = new PendingCollection(row, target);
                collections.Add((ownership, id), collection);
            }
            collection.Touch(ctx);
            return collection;
        }

        /// <summary>Releases an entry a collection dropped, once the batch is prepared.</summary>
        internal void Release(NeoValueOwnership ownership, string id, Member? member)
        {
            (releases ??= new()).Add((ownership, id, member));
            (entries ??= new HashSet<string>(StringComparer.Ordinal)).Add(id);
        }

        internal void Gain(string id) => (entries ??= new HashSet<string>(StringComparer.Ordinal)).Add(id);

        /// <summary>Stages every changed collection's row and the entries it released.</summary>
        internal void Prepare()
        {
            foreach (var pair in collections)
            {
                PendingCollection collection = pair.Value;
                if (!collection.Changed)
                    continue;
                MemberValue row = collection.Row;
                if (collection is PendingList list)
                    list.Seal();
                row.updatedAt = collection.UpdatedAt;
                Plan.Set(pair.Key.ownership, row);
                NeoValueOwnership ownership = pair.Key.ownership;
                collection.ForEachContext(ctx => Plan.AfterCommit(() =>
                    NSGetterEvaluator.RefreshCachedRowAfterWrite(row, ctx, ownership)));
            }
            if (releases is null)
                return;
            foreach (NeoValueOwnership ownership in new[] { NeoValueOwnership.Session, NeoValueOwnership.Save })
            {
                List<(string id, Member? member)>? roots = null;
                foreach (var release in releases)
                    if (release.ownership == ownership)
                        (roots ??= new()).Add((release.id, release.member));
                if (roots is not null)
                    Plan.Client.StageUnlinkedRemovals(Plan, ownership, roots);
            }
        }

        /// <summary>A collection the batch mutates, and the contexts whose cached rows it refreshes.</summary>
        internal class PendingCollection
        {
            internal readonly MemberValue Row;
            internal readonly object Target;
            internal bool Changed;
            internal NeoTimestamp UpdatedAt;
            private NSGetterEvaluator.Context? context;
            private List<NSGetterEvaluator.Context>? moreContexts;

            internal PendingCollection(MemberValue row, object target)
            {
                Row = row;
                Target = target;
            }

            internal void Changes(NeoTimestamp now)
            {
                Changed = true;
                UpdatedAt = now;
            }

            internal void Touch(NSGetterEvaluator.Context ctx)
            {
                if (context is null)
                    context = ctx;
                else if (!ReferenceEquals(context, ctx) && moreContexts?.Contains(ctx) != true)
                    (moreContexts ??= new()).Add(ctx);
            }

            internal void ForEachContext(Action<NSGetterEvaluator.Context> action)
            {
                action(context!);
                if (moreContexts is not null)
                    foreach (NSGetterEvaluator.Context ctx in moreContexts)
                        action(ctx);
            }
        }

        /// <summary>
        /// A list's entry ids in a buffer that grows by doubling. It starts
        /// on the committed row's array and copies before its first change.
        /// </summary>
        internal sealed class PendingList : PendingCollection
        {
            private string[] ids;
            private bool owned;
            private HashSet<string>? members;

            internal PendingList(ArrayMemberValue row, object target) : base(row, target)
            {
                ids = row.value ?? Array.Empty<string>();
                Count = ids.Length;
            }

            internal int Count
            {
                get; private set;
            }

            internal string this[int index] => ids[index];

            /// <summary>Whether the list holds <paramref name="id"/>, through a set built once.</summary>
            internal bool Contains(string id)
            {
                if (members is null)
                {
                    members = new HashSet<string>(StringComparer.Ordinal);
                    for (int index = 0; index < Count; index++)
                        members.Add(ids[index]);
                }
                return members.Contains(id);
            }

            internal void Insert(int index, string id, NeoTimestamp now)
            {
                if (!owned || Count == ids.Length)
                {
                    var grown = new string[Math.Max(4, Count * 2)];
                    Array.Copy(ids, grown, Count);
                    ids = grown;
                    owned = true;
                }
                Array.Copy(ids, index, ids, index + 1, Count - index);
                ids[index] = id;
                Count++;
                members?.Add(id);
                Changes(now);
            }

            internal string RemoveAt(int index, NeoTimestamp now)
            {
                string id = ids[index];
                if (!owned)
                {
                    var copy = new string[Count];
                    Array.Copy(ids, copy, Count);
                    ids = copy;
                    owned = true;
                }
                Array.Copy(ids, index + 1, ids, index, Count - index - 1);
                ids[--Count] = null!;
                members?.Remove(id);
                Changes(now);
                return id;
            }

            /// <summary>Empties the list, returning the ids it held.</summary>
            internal string[] Clear(NeoTimestamp now)
            {
                var cleared = new string[Count];
                Array.Copy(ids, cleared, Count);
                ids = Array.Empty<string>();
                owned = true;
                Count = 0;
                members?.Clear();
                Changes(now);
                return cleared;
            }

            /// <summary>Gives the row the exact array it stores.</summary>
            internal void Seal()
            {
                if (Count != ids.Length)
                {
                    var exact = new string[Count];
                    Array.Copy(ids, exact, Count);
                    ids = exact;
                }
                ((ArrayMemberValue)Row).value = ids;
            }
        }
    }

    public partial class NeoClient
    {
        // A batch is held only while NeoScript runs, and nothing outside it
        // may observe what it holds pending: a read or leaf write of a row it
        // stages, any other write plan, a native call, a grid query and the
        // outermost execution's exit each commit it first.
        private NeoWriteBatch? scriptWriteBatch;
        private int scriptWriteDepth;

        internal NeoWriteBatch? PendingScriptWrites => scriptWriteBatch;

        internal void EnterScriptWrites() => scriptWriteDepth++;

        internal void ExitScriptWrites()
        {
            if (--scriptWriteDepth == 0)
                CommitScriptWrites();
        }

        /// <summary>
        /// The batch a NeoScript mutation of collection <paramref name="rowId"/>
        /// stages into, opened on first use; null when it must commit alone.
        /// Replays and candidate reads see only what one plan stages, and a
        /// virtual row's write replays its expansion, which reads may not lag.
        /// </summary>
        internal NeoWriteBatch? ScriptWriteBatch(string rowId)
        {
            if (scriptWriteDepth == 0
                || candidateReadPlan is not null
                || candidateReplay is not null
                || replayAllocationScope is not null
                || nestedConstructorCapture is not null
                || isReplayingVirtualInstance
                || virtualValues.ContainsKey(rowId)
                || virtualRootByFootprintId.ContainsKey(rowId))
                return null;
            return scriptWriteBatch ??= new NeoWriteBatch(new NeoWritePlan(this), held: true);
        }

        /// <summary>Commits the held batch, unless one of its own mutations is staging.</summary>
        internal void CommitScriptWrites()
        {
            NeoWriteBatch? batch = scriptWriteBatch;
            if (batch is null || batch.IsStaging)
                return;
            scriptWriteBatch = null;
            batch.Prepare();
            if (batch.Plan.Rows.Count == 0)
                return;
            batch.Plan.Rebase();
            batch.Plan.Commit();
        }

        private void CommitScriptWritesReading(string id)
        {
            if (!scriptWriteBatch!.IsStaging && scriptWriteBatch.Stages(id))
                CommitScriptWrites();
        }

        /// <summary>Commits the held batch when a memoized getter read anything it holds.</summary>
        private void CommitScriptWritesObserved(GetterMemoEntry entry)
        {
            NeoWriteBatch batch = scriptWriteBatch!;
            if (batch.IsStaging)
                return;
            bool observed = entry.readsGrid;
            if (!observed && entry.valueReads is not null)
            {
                foreach (string id in entry.valueReads)
                {
                    observed = batch.Stages(id);
                    if (observed)
                        break;
                }
            }
            if (!observed && entry.reads is not null)
            {
                foreach (GetterRead read in entry.reads)
                {
                    observed = read.content is null && batch.Stages(read.id);
                    if (observed)
                        break;
                }
            }
            if (observed)
                CommitScriptWrites();
        }
    }
}
