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
        private readonly bool held;
        // By store: a Session copy of a released Save entry keeps its ids.
        private readonly Dictionary<(NeoValueOwnership ownership, string id), PendingCollection> collections = new();
        private List<(NeoValueOwnership ownership, string id, Member? member)>? releases;
        // The entries pending collections gained, by the collection that
        // gained them, or released (null). Their rows don't hold that
        // membership until the batch is prepared.
        private Dictionary<(NeoValueOwnership ownership, string id), string?>? entries;
        // The rows the plan stages since it last handed them off. A read of
        // one of these or of a pending collection must commit first.
        private HashSet<string>? staged;
        // Bits of those ids (Bit), so a read of anything else skips hashing
        // its id. A row read asks many times, with ids that needn't share an instance.
        private ulong touchedBits;
        private ulong collectionBits;
        // Where the pending collections are members: a Class row's field by
        // its key, any other parent by the row alone, unless one's parent is
        // unknown. A read off anything else skips looking for them.
        private Dictionary<(NeoValueOwnership ownership, string parent, string key), string>? fields;
        private HashSet<string>? fieldKeys;
        private HashSet<(NeoValueOwnership ownership, string id)>? parents;
        private bool anyParent;
        private int staging;

        internal NeoWriteBatch(NeoWritePlan plan, bool held)
        {
            Plan = plan;
            this.held = held;
            if (held)
            {
                staged = new HashSet<string>(StringComparer.Ordinal);
                plan.HeldBy = this;
            }
        }

        /// <summary>
        /// The plan mutations stage into. A held batch hands its rows off
        /// (<see cref="TakeRows"/>) and stages on into a fresh one.
        /// </summary>
        internal NeoWritePlan Plan
        {
            get; private set;
        }

        internal bool IsStaging => staging != 0;

        internal bool HasCollections => collections.Count != 0;

        /// <summary>
        /// Bumped whenever a read may newly reach a pending collection.
        /// Unique across batches, so a read site that proved it can't
        /// remembers the shape it proved it under.
        /// </summary>
        internal int Shape
        {
            get; private set;
        }

        private static int nextShape;

        /// <summary>Whether the grid's built layer indexes read a pending collection.</summary>
        internal bool GridDependent
        {
            get; private set;
        }

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
        /// Marks <paramref name="id"/> as something a read must commit first,
        /// and forgets the getters that read it: a memo hit reads nothing,
        /// so it can't commit the batch the way the reads it replaces would.
        /// </summary>
        internal void Touch(string id)
        {
            staged!.Add(id);
            touchedBits |= Bit(id);
            Plan.Client.InvalidateGetterMemoForRow(id);
        }

        /// <summary>Whether a read of <paramref name="id"/> must commit first.</summary>
        // Every row read asks while a batch is held, and nearly all miss.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal bool Touches(string id) => (touchedBits & Bit(id)) != 0 && TouchesUncached(id);

        private bool TouchesUncached(string id) => staged!.Contains(id) || HasCollection(id);

        // One of 64 bits from an id's length and two of its characters.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static ulong Bit(string id) =>
            id.Length == 0 ? 1UL : 1UL << ((id.Length + id[id.Length - 1] * 7 + id[id.Length >> 1] * 31) & 63);

        /// <summary>Whether either store holds a pending collection at <paramref name="id"/>.</summary>
        internal bool HasCollection(string id) =>
            (collectionBits & Bit(id)) != 0
            && (collections.ContainsKey((NeoValueOwnership.Save, id)) || collections.ContainsKey((NeoValueOwnership.Session, id)));

        /// <summary>
        /// The pending collection that owns entry <paramref name="id"/>, or
        /// null when one released it, if a pending collection changed it.
        /// </summary>
        internal bool TryGetEntryParent(NeoValueOwnership ownership, string id, out string? parentId)
        {
            parentId = null;
            return entries?.TryGetValue((ownership, id), out parentId) == true;
        }

        /// <summary>The pending collection at <paramref name="id"/> in <paramref name="ownership"/>'s store.</summary>
        internal PendingCollection? Collection(NeoValueOwnership ownership, string id) =>
            collections.TryGetValue((ownership, id), out PendingCollection? collection) ? collection : null;

        /// <summary>
        /// The pending collection Class row <paramref name="parent"/> holds at
        /// <paramref name="key"/>, when the batch knows it is there.
        /// </summary>
        internal string? PendingField(NeoValueOwnership ownership, string parent, string key) =>
            fields is not null && fields.TryGetValue((ownership, parent, key), out string? id) ? id : null;

        /// <summary>Whether a pending collection is some Class row's field <paramref name="key"/>.</summary>
        internal bool IsFieldKey(string key) => fieldKeys?.Contains(key) == true;

        /// <summary>Whether a pending collection's membership isn't a known Class field.</summary>
        internal bool HasUnkeyedMembers => anyParent || parents is not null;

        /// <summary>Whether a pending collection may be a member of row <paramref name="id"/> that <see cref="PendingField"/> doesn't know.</summary>
        internal bool MayParent(NeoValueOwnership ownership, string id) => anyParent || parents?.Contains((ownership, id)) == true;

        // Records where a new pending collection is a member, which a read may now reach.
        private void Add(NeoValueOwnership ownership, string id, PendingCollection collection)
        {
            collections.Add((ownership, id), collection);
            collectionBits |= Bit(id);
            touchedBits |= Bit(id);
            Shape = ++nextShape;
            GridDependent = GridDependent || Plan.Client.GridDependsOn(id);
            if (!Plan.Client.TryFindOwnedParent(ownership, id, out string? parent))
            {
                anyParent = true;
                return;
            }
            // A Class row has a few fields; a Dictionary's entries can be many.
            if (Plan.TryGet(ownership, parent, out ObjectMemberValue? row) && row!.classId is not null && row.value is not null)
                foreach (var field in row.value)
                    if (field.Value == id)
                    {
                        (fields ??= new())[(ownership, parent, field.Key)] = id;
                        (fieldKeys ??= new HashSet<string>(StringComparer.Ordinal)).Add(field.Key);
                        return;
                    }
            (parents ??= new()).Add((ownership, parent));
        }

        /// <summary>The ids of the pending collections.</summary>
        internal IEnumerable<string> CollectionIds
        {
            get
            {
                foreach (var key in collections.Keys)
                    yield return key.id;
            }
        }

        /// <summary>The target a mutation of pending collection <paramref name="id"/> resolved to.</summary>
        internal object? Target(NeoValueOwnership ownership, string id) => Collection(ownership, id)?.Target;

        /// <summary>
        /// The row a graph walk copies for pending collection
        /// <paramref name="id"/>, whose mutations reach no plan row until the
        /// batch is prepared; null when it holds none.
        /// </summary>
        internal MemberValue? PendingRow(NeoValueOwnership ownership, string id)
        {
            if (!collections.TryGetValue((ownership, id), out PendingCollection? collection) || !collection.Changed)
                return null;
            (collection as PendingList)?.Snapshot();
            return collection.Row;
        }

        /// <summary>Moves row <paramref name="id"/>'s pending membership and collection with the graph that owns it.</summary>
        internal void Rehome(string id, NeoValueOwnership from, NeoValueOwnership to)
        {
            if (entries is not null && entries.Remove((from, id), out string? parentId))
                entries[(to, id)] = parentId;
            if (!collections.Remove((from, id), out PendingCollection? collection))
                return;
            collection.Ownership = to;
            // Its target mutates the store it left.
            collection.Target = null;
            collections[(to, id)] = collection;
            // Where it is a member moved too; rare enough to stop knowing.
            anyParent = true;
            Shape = ++nextShape;
        }

        /// <summary>
        /// Drops pending collection <paramref name="id"/>, whose row a write
        /// replaces or removes: preparing the batch must not restage it.
        /// </summary>
        internal void Discard(NeoValueOwnership ownership, string id) => collections.Remove((ownership, id));

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

        /// <summary>
        /// Hands off the rows staged so far, and stages on into a fresh plan;
        /// null when there are none. Collections stay pending: their rows
        /// cost the list's length to store, which only the full commit pays.
        /// </summary>
        internal NeoWritePlan? TakeRows()
        {
            NeoWritePlan plan = Plan;
            if (plan.Rows.Count == 0 && plan.Bindings.Count == 0)
                return null;
            Plan = new NeoWritePlan(plan.Client) { HeldBy = this };
            staged = new HashSet<string>(StringComparer.Ordinal);
            touchedBits = collectionBits;
            return plan;
        }

        internal PendingList List(NeoValueOwnership ownership, string id, NSGetterEvaluator.Context ctx, object? target)
        {
            if (!collections.TryGetValue((ownership, id), out PendingCollection? collection))
            {
                if (!Plan.TryGet(ownership, id, out ArrayMemberValue? row))
                    throw new NSGetterRuntimeError($"Missing list row '{id}'.");
                collection = new PendingList((ArrayMemberValue)Plan.Client.CloneRowForWrite(row!, sharesArrayEntries: true), ownership, target);
                Add(ownership, id, collection);
            }
            collection.Target ??= target;
            collection.Touch(ctx);
            if (held)
                Touch(id);
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
                collection = new PendingCollection(row, ownership, target);
                Add(ownership, id, collection);
            }
            collection.Target ??= target;
            collection.Touch(ctx);
            if (held)
                Touch(id);
            return collection;
        }

        /// <summary>
        /// Resolves <paramref name="collection"/>'s member and its entries'
        /// once. Inference reads committed rows, which must not commit the
        /// batch under the read that asked.
        /// </summary>
        internal void ResolveMembers(PendingCollection collection, Member? member = null)
        {
            if (collection.MembersResolved)
                return;
            staging++;
            try
            {
                NeoClient client = Plan.Client;
                if (member is null)
                    client.TryInferMemberForValueId(collection.Row.id, out member);
                collection.CollectionMember = member;
                if (member is ListMember or DictionaryMember)
                    collection.EntryMember = client.TryResolveCollectionEntryMember(member, collection.Row);
                collection.MembersResolved = true;
            }
            finally
            {
                staging--;
            }
        }

        /// <summary>Releases an entry a collection dropped, once the batch is prepared.</summary>
        internal void Release(NeoValueOwnership ownership, string id, Member? member)
        {
            (releases ??= new()).Add((ownership, id, member));
            (entries ??= new())[(ownership, id)] = null;
        }

        /// <summary>Records that collection <paramref name="parentId"/> gained entry <paramref name="id"/>.</summary>
        internal void Gain(NeoValueOwnership ownership, string id, string parentId) =>
            (entries ??= new())[(ownership, id)] = parentId;

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
                NeoValueOwnership ownership = collection.Ownership;
                Plan.Set(ownership, row);
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
            internal NeoValueOwnership Ownership;
            internal object? Target;
            internal bool Changed;
            internal NeoTimestamp UpdatedAt;
            // Resolved on the first read that skips a commit (ResolveMembers).
            internal bool MembersResolved;
            internal Member? CollectionMember;
            internal Member? EntryMember;
            private NSGetterEvaluator.Context? context;
            private List<NSGetterEvaluator.Context>? moreContexts;

            internal PendingCollection(MemberValue row, NeoValueOwnership ownership, object? target)
            {
                Row = row;
                Ownership = ownership;
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

            internal PendingList(ArrayMemberValue row, NeoValueOwnership ownership, object? target) : base(row, ownership, target)
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
                    // The first change copies exactly: most executions make
                    // one, and Seal then stores the copy without another.
                    var grown = new string[owned ? Math.Max(4, Count * 2) : Count + 1];
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

            /// <summary>Replaces the entry at <paramref name="index"/>, returning the id it held.</summary>
            internal string Set(int index, string id, NeoTimestamp now)
            {
                string previous = ids[index];
                Own();
                ids[index] = id;
                if (members is not null)
                {
                    members.Remove(previous);
                    members.Add(id);
                }
                Changes(now);
                return previous;
            }

            internal string RemoveAt(int index, NeoTimestamp now)
            {
                string id = ids[index];
                Own();
                Array.Copy(ids, index + 1, ids, index, Count - index - 1);
                ids[--Count] = null!;
                members?.Remove(id);
                Changes(now);
                return id;
            }

            // Copies the committed row's array before the first change.
            private void Own()
            {
                if (owned)
                    return;
                var copy = new string[Count];
                Array.Copy(ids, copy, Count);
                ids = copy;
                owned = true;
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

            /// <summary>Seals the row for a reader that keeps its array, which the next change copies.</summary>
            internal void Snapshot()
            {
                Seal();
                owned = false;
            }
        }
    }

    public partial class NeoClient
    {
        // A batch is held only while NeoScript runs, and nothing outside it
        // may observe what it holds pending. A read or write of a row it
        // stages commits those rows, which costs only what was staged since
        // the last such commit; one of a pending collection, a grid query
        // over one and the outermost execution's exit commit it all. C#
        // change handlers wait for the outermost exit, so none runs inside
        // the write or replay whose read committed the batch.
        private NeoWriteBatch? scriptWriteBatch;
        private int scriptWriteDepth;
        private bool scriptChangeBatchOpen;
        // Plan commits running while a batch is held. Their reads see the
        // committed graph: a batch commit inside one would change it under them.
        private int commitsUnderScriptBatch;

        internal NeoWriteBatch? PendingScriptWrites => scriptWriteBatch;

        internal void EnterScriptWrites()
        {
            // A getter an execution's writes reach is heard once, after it
            // exits, so its watchers read it once and never mid-execution.
            if (scriptWriteDepth++ == 0)
                HoldGetterChanges();
        }

        internal void ExitScriptWrites()
        {
            if (--scriptWriteDepth != 0)
                return;
            try
            {
                CommitScriptWrites();
            }
            finally
            {
                try
                {
                    // Released into the execution's change batch, when it has one.
                    ReleaseGetterChanges();
                }
                finally
                {
                    if (scriptChangeBatchOpen)
                    {
                        scriptChangeBatchOpen = false;
                        EndChangeBatch();
                    }
                }
            }
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
            if (scriptWriteBatch is null)
            {
                scriptWriteBatch = new NeoWriteBatch(new NeoWritePlan(this), held: true);
                if (!scriptChangeBatchOpen)
                {
                    scriptChangeBatchOpen = true;
                    BeginChangeBatch();
                }
            }
            return scriptWriteBatch;
        }

        /// <summary>
        /// The held batch, unless one of its own mutations is staging or a
        /// plan is committing under it: neither may see it commit.
        /// </summary>
        private NeoWriteBatch? CommittableScriptWrites =>
            scriptWriteBatch is { IsStaging: false } batch && commitsUnderScriptBatch == 0 ? batch : null;

        /// <summary>Commits the held batch.</summary>
        internal void CommitScriptWrites()
        {
            if (CommittableScriptWrites is not { } batch)
                return;
            scriptWriteBatch = null;
            try
            {
                batch.Prepare();
                if (batch.Plan.Rows.Count != 0 || batch.Plan.Bindings.Count != 0)
                    CommitScriptPlan(batch.Plan);
            }
            catch
            {
                // Getters that read the mutations pending must not keep them.
                foreach (string id in batch.CollectionIds)
                    InvalidateGetterMemoForRow(id);
                throw;
            }
        }

        /// <summary>Commits the rows the held batch staged, leaving its collections pending.</summary>
        internal void CommitScriptRows()
        {
            if (CommittableScriptWrites?.TakeRows() is NeoWritePlan rows)
                CommitScriptPlan(rows);
        }

        /// <summary>
        /// Commits what the held batch holds of <paramref name="id"/> before
        /// anything reads or writes it.
        /// </summary>
        internal void ObserveScriptWrites(string id)
        {
            if (CommittableScriptWrites is not { } batch || !batch.Touches(id))
                return;
            if (batch.HasCollection(id))
                CommitScriptWrites();
            else
                CommitScriptRows();
        }

        /// <summary>A C# view's read of row <paramref name="id"/>, which must see the held batch's mutations.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal void ObserveHostRead(string? id)
        {
            if (id is not null && scriptWriteBatch?.Touches(id) == true)
                ObserveScriptWrites(id);
        }

        /// <summary>
        /// Commits what a grid query must see: the batch's rows, and its
        /// collections only when a built layer index read one. A layer not
        /// yet built reads, and so commits, what it needs as it builds.
        /// </summary>
        internal void CommitScriptWritesForGrid()
        {
            if (CommittableScriptWrites is not { } batch)
                return;
            if (batch.GridDependent)
                CommitScriptWrites();
            else
                CommitScriptRows();
        }

        /// <summary>Whether a built layer index read row <paramref name="id"/>.</summary>
        internal bool GridDependsOn(string id)
        {
            foreach (NeoTileGridLookupCache cache in gridLookupCacheList)
                if (cache.DependsOn(id))
                    return true;
            return false;
        }

        /// <summary>
        /// Commits a batch plan as the execution's own write: never into a
        /// replay or candidate a read inside it observed the batch from.
        /// </summary>
        private void CommitScriptPlan(NeoWritePlan plan)
        {
            CandidateReplay? candidate = candidateReplay;
            NeoWritePlan? readPlan = candidateReadPlan;
            ReplayAllocationScope? scope = replayAllocationScope;
            NestedConstructorCapture? capture = nestedConstructorCapture;
            candidateReplay = null;
            candidateReadPlan = null;
            replayAllocationScope = null;
            nestedConstructorCapture = null;
            try
            {
                plan.Rebase();
                plan.Commit();
            }
            finally
            {
                candidateReplay = candidate;
                candidateReadPlan = readPlan;
                replayAllocationScope = scope;
                nestedConstructorCapture = capture;
            }
        }
    }
}
