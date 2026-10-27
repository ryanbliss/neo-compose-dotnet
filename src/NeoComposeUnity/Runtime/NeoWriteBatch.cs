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
        private List<(NeoValueOwnership ownership, string id, Member? member, string parentId)>? releases;
        // The entries pending collections gained, by the collection that
        // gained them, or released (null). Their rows don't hold that
        // membership until the batch is prepared.
        private Dictionary<(NeoValueOwnership ownership, string id), (string? parent, RemovalContext? context)>? entries;
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
        // Values inserted during this held batch are not row aliases until the
        // whole owning graph commits. Reusing one must publish that graph first.
        private Dictionary<object, Attachment>? pendingAttachments;
        private Dictionary<(NeoValueOwnership ownership, string id), Attachment>? attachmentsByRow;
        private List<Action>? collectionRollbacks;
        private List<object>? attachmentJournal;
        private List<Action>? attachmentCallbacks;
        private List<Action>? requiredPublicationCallbacks;
        private Dictionary<NeoScriptAllocationTracker, NeoScriptAllocationTracker.DeferredCompletion>? deferredCompletions;
        private Dictionary<string, NeoScriptAllocationTracker>? retainedRootOwners;

        internal void RegisterRetainedRoot(string id, Member member, NeoScriptAllocationTracker tracker)
        {
            tracker.RegisterSessionRoot(id, member);
            (retainedRootOwners ??= new(StringComparer.Ordinal)).Add(id, tracker);
        }

        internal void PreserveRetainedAlias(NeoScriptObject value, NSGetterEvaluator.Context context) =>
            context.allocationTracker.MarkEscaped(value, context, retainedRootOwners);

        private int publicationDrainState;
        private int completionState;
        private bool publicationHookRegistered;
        private Exception? completionFailure;

        internal void AfterPublication(Action callback) =>
            (requiredPublicationCallbacks ??= new()).Add(callback);

        internal NeoScriptAllocationTracker.DeferredCompletion DeferCompletion(
            NeoScriptAllocationTracker tracker, NSGetterEvaluator.Context context)
        {
            if (completionState != 0)
                throw new InvalidOperationException("Cannot defer an execution to a completed write batch.");
            if ((deferredCompletions ??= new()).TryGetValue(tracker, out var completion))
                return completion;
            completion = new NeoScriptAllocationTracker.DeferredCompletion(tracker, context);
            deferredCompletions.Add(tracker, completion);
            return completion;
        }

        internal void RegisterPublicationHook()
        {
            if (publicationHookRegistered)
                return;
            publicationHookRegistered = true;
            Plan.AfterCommit(DrainRequiredPublication);
        }

        private void RecordCompletionFailure(Exception error) => completionFailure ??= error;

        private void DrainRequiredPublication()
        {
            if (publicationDrainState != 0)
                return;
            publicationDrainState = 1;
            var callbacks = requiredPublicationCallbacks;
            requiredPublicationCallbacks = null;
            try
            {
                if (callbacks is not null)
                    foreach (Action callback in callbacks)
                        try
                        {
                            callback();
                        }
                        catch (Exception error) { RecordCompletionFailure(error); }
            }
            finally { publicationDrainState = 2; }
            if (completionFailure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(completionFailure).Throw();
        }

        internal void FinishCommit(Exception? failure, bool published)
        {
            if (failure is not null)
                RecordCompletionFailure(failure);
            // A reentrant completion cannot overtake the detached remap queue.
            if (completionState != 0 || publicationDrainState == 1)
                return;
            completionState = 1;
            try
            {
                if (published)
                {
                    try
                    {
                        DrainRequiredPublication();
                    }
                    catch (Exception error) { RecordCompletionFailure(error); }
                    try
                    {
                        CompleteAttachments();
                    }
                    catch (Exception error) { RecordCompletionFailure(error); }
                }
                else
                {
                    Plan.LocalSelections?.Discard();
                    requiredPublicationCallbacks = null;
                    publicationDrainState = 2;
                    DiscardAttachments();
                }
                var completions = deferredCompletions;
                deferredCompletions = null;
                if (completions is not null)
                {
                    // All remaps are visible before any caller's roots can be
                    // reclaimed. A different caller may have returned an alias.
                    foreach (var completion in completions.Values)
                        try
                        {
                            completion.PreserveReturns(retainedRootOwners);
                        }
                        catch (Exception error) { RecordCompletionFailure(error); }
                    foreach (var completion in completions.Values)
                        try
                        {
                            completion.Complete();
                        }
                        catch (Exception error) { RecordCompletionFailure(error); }
                }
            }
            finally
            {
                retainedRootOwners = null;
                completionState = 2;
            }
            if (completionFailure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(completionFailure).Throw();
        }

        internal bool HasPendingAttachment(object value) => pendingAttachments?.ContainsKey(NSGetterEvaluator.SequenceCollectionIdentity(value)) == true;

        internal bool HasCurrentCollectionAttachment(object value, NSGetterEvaluator.Context context) =>
            CurrentCollectionAttachment(value, context) is not null;

        internal Attachment? CurrentCollectionAttachment(object value, NSGetterEvaluator.Context context) =>
            pendingAttachments?.TryGetValue(NSGetterEvaluator.SequenceCollectionIdentity(value), out var attachment) == true
                && CurrentAttachment(attachment) && !RemovesAlias(value, context) ? attachment : null;


        internal bool RemovesAlias(object value, NSGetterEvaluator.Context context)
        {
            if (releases is null || releases.Count == 0)
                return false;
            string? id = null;
            NeoValueOwnership ownership = NeoValueOwnership.Session;
            if (pendingAttachments?.TryGetValue(NSGetterEvaluator.SequenceCollectionIdentity(value), out var attachment) == true && attachment.Row is { } inserted)
            {
                id = inserted.id;
                ownership = attachment.Ownership;
            }
            else if (context.rowReverseIndex.TryGetValue(value, out var reference))
            {
                // Class handles retain the existing pending move path. The
                // barrier protects extraction from removed collection payloads.
                if (reference.classId is not null || reference.member is ClassMember)
                    return false;
                id = reference.valueId;
                ownership = reference.ownership;
            }
            if (id is null)
                return false;
            return RemovesReference(ownership, id);
        }

        internal bool RemovesReference(NeoValueOwnership ownership, string id)
        {
            if (releases is null || releases.Count == 0)
                return false;
            var visited = new HashSet<(NeoValueOwnership, string)>();
            staging++;
            try
            {
                using (Plan.Client.ReadCandidate(Plan))
                    while (visited.Add((ownership, id)))
                    {
                        if (TryGetEntryParent(ownership, id, out string? pendingParent))
                        {
                            if (pendingParent is null)
                                return true;
                            id = pendingParent;
                        }
                        else if (Plan.Client.TryFindOwnedParent(ownership, id, out string? parent, out var parentOwnership)
                            || Plan.Client.TryFindRemovalParent(this, ownership, id, out parent, out parentOwnership))
                        {
                            id = parent;
                            ownership = parentOwnership;
                        }
                        else
                            break;
                    }
                return false;
            }
            finally { staging--; }
        }


        // A released aggregate loses its placement before its descendants do.
        // Keep only this batch's exact validated row contract for the removal walk.
        internal sealed class RemovalContext
        {
            private readonly MemberValue row;
            private readonly NeoValueNode.RetainedRootContract contract;

            internal RemovalContext(NeoClient client, MemberValue row, Member member)
            {
                this.row = row;
                contract = new NeoValueNode.RetainedRootContract(member, client.SchemaResolution, row);
            }

            internal Member? Resolve(NeoClient client, MemberValue current)
            {
                if (!ReferenceEquals(row, current) || current.IsRemoved)
                    return null;
                if (!ReferenceEquals(contract.schema, client.SchemaResolution) || !contract.MatchesBindings(current))
                    throw new InvalidOperationException("A pending removal no longer has its validated schema contract.");
                return contract.member switch
                {
                    ListMember when current is ArrayMemberValue => contract.member,
                    DictionaryMember when current is ObjectMemberValue { constructorArgs: null } => contract.member,
                    _ => null,
                };
            }
        }

        internal Member? RemovalMember(NeoValueOwnership ownership, string id, MemberValue row)
        {
            if (!ReferenceEquals(Plan.Resolve(ownership, id), row))
                return null;
            if (entries?.TryGetValue((ownership, id), out var entry) == true)
                return entry.context?.Resolve(Plan.Client, row);
            return AttachmentForRow(ownership, id)?.Removal?.Resolve(Plan.Client, row);
        }

        internal void TrackAttachment(object value, Action? completed = null)
        {
            value = NSGetterEvaluator.SequenceCollectionIdentity(value);
            if ((pendingAttachments ??= new(NSGetterEvaluator.ReferenceEqualityComparer.Instance)).TryAdd(value, new Attachment()))
                (attachmentJournal ??= new()).Add(value);
            if (completed is not null)
                (attachmentCallbacks ??= new()).Add(completed);
        }

        internal sealed class Attachment
        {
            internal object Value = null!;
            internal NSGetterEvaluator.Context Context = null!;
            internal MemberValue? Row;
            internal Member? Member;
            internal RemovalContext? Removal;
            internal NeoValueOwnership Ownership;
        }

        internal void TrackCollectionAttachment(object value, MemberValue row, Member member,
            NeoValueOwnership ownership, NSGetterEvaluator.Context context, Action completed)
        {
            var attachment = new Attachment
            {
                Row = row,
                Member = member,
                Ownership = ownership,
                Value = value,
                Context = context,
                Removal = member is ListMember or DictionaryMember ? new RemovalContext(Plan.Client, row, member) : null
            };
            object identity = NSGetterEvaluator.SequenceCollectionIdentity(value);
            if (!(pendingAttachments ??= new(NSGetterEvaluator.ReferenceEqualityComparer.Instance)).TryAdd(identity, attachment))
                throw new NSGetterRuntimeError("Collection is already owned by another entry.");
            (attachmentJournal ??= new()).Add(identity);
            (attachmentsByRow ??= new()).Add((ownership, row.id), attachment);
            (attachmentCallbacks ??= new()).Add(() =>
            {
                if (CurrentAttachment(attachment))
                    completed();
            });
        }

        internal Attachment? AttachmentForRow(NeoValueOwnership ownership, string id) =>
            attachmentsByRow is not null && attachmentsByRow.TryGetValue((ownership, id), out var attachment) ? attachment : null;

        private bool CurrentAttachment(Attachment attachment) => attachment.Row is { } row
            && (!TryGetEntryParent(attachment.Ownership, row.id, out string? parent) || parent is not null)
            && (ReferenceEquals(Plan.Resolve(attachment.Ownership, row.id), row)
                || Collection(attachment.Ownership, row.id) is { } pending
                    && ReferenceEquals(Plan.Resolve(attachment.Ownership, row.id), pending.Row));

        internal PendingCollection? PendingAttachment(object? value, NSGetterEvaluator.Context context)
        {
            if (value is null || pendingAttachments is null
                || !pendingAttachments.TryGetValue(NSGetterEvaluator.SequenceCollectionIdentity(value), out Attachment attachment)
                || !CurrentAttachment(attachment))
                return null;
            if (Collection(attachment.Ownership, attachment.Row!.id) is { } existing)
            {
                existing.Touch(context);
                return existing;
            }
            PendingCollection? pending = null;
            // Like a mutation, resolving a staged child's parent must read the
            // candidate without recursively committing its owning collection.
            Stage(_ =>
            {
                pending = attachment.Member switch
                {
                    ListMember => List(attachment.Ownership, attachment.Row.id, context, value),
                    DictionaryMember => Dictionary(attachment.Ownership, attachment.Row.id, context, value),
                    _ => throw new InvalidOperationException("An attachment view must be an owned collection."),
                };
                ResolveMembers(pending, attachment.Member);
            });
            return pending;
        }

        internal void CompleteAttachments()
        {
            Exception? failure = null;
            var callbacks = attachmentCallbacks;
            attachmentCallbacks = null;
            try
            {
                if (callbacks is not null)
                    foreach (Action callback in callbacks)
                        try
                        {
                            callback();
                        }
                        catch (Exception error) { failure ??= error; }
            }
            finally { DiscardAttachments(); }
            if (failure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        internal void DiscardAttachments()
        {
            pendingAttachments?.Clear();
            attachmentsByRow?.Clear();
            attachmentJournal?.Clear();
            attachmentCallbacks?.Clear();
        }


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
            int attachmentCount = attachmentJournal?.Count ?? 0;
            int callbackCount = attachmentCallbacks?.Count ?? 0;
            int collectionCount = collectionRollbacks?.Count ?? 0;
            int releaseCount = releases?.Count ?? 0;
            staging++;
            try
            {
                using (Plan.Client.ReadCandidate(Plan))
                    mutate(this);
            }
            catch
            {
                Plan.Rollback(checkpoint);
                if (releases is not null && releases.Count > releaseCount)
                    releases.RemoveRange(releaseCount, releases.Count - releaseCount);
                if (collectionRollbacks is not null)
                {
                    for (int i = collectionRollbacks.Count - 1; i >= collectionCount; i--)
                        collectionRollbacks[i]();
                    collectionRollbacks.RemoveRange(collectionCount, collectionRollbacks.Count - collectionCount);
                }
                if (attachmentJournal is not null)
                {
                    for (int i = attachmentJournal.Count - 1; i >= attachmentCount; i--)
                    {
                        object value = attachmentJournal[i];
                        if (pendingAttachments!.TryGetValue(value, out var attachment) && attachment.Row is { } row)
                            attachmentsByRow!.Remove((attachment.Ownership, row.id));
                        pendingAttachments.Remove(value);
                    }
                    attachmentJournal.RemoveRange(attachmentCount, attachmentJournal.Count - attachmentCount);
                }
                if (attachmentCallbacks is not null)
                    attachmentCallbacks.RemoveRange(callbackCount, attachmentCallbacks.Count - callbackCount);
                throw;
            }
            finally
            {
                staging--;
                if (staging == 0)
                    collectionRollbacks?.Clear();
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

        /// <summary>Whether a writable store holds a pending collection at <paramref name="id"/>.</summary>
        internal bool HasCollection(string id) =>
            (collectionBits & Bit(id)) != 0
            && (collections.ContainsKey((NeoValueOwnership.Save, id))
                || collections.ContainsKey((NeoValueOwnership.Session, id))
                || collections.ContainsKey((NeoValueOwnership.User, id)));

        /// <summary>
        /// The pending collection that owns entry <paramref name="id"/>, or
        /// null when one released it, if a pending collection changed it.
        /// </summary>
        internal bool TryGetEntryParent(NeoValueOwnership ownership, string id, out string? parentId)
        {
            if (entries?.TryGetValue((ownership, id), out var entry) == true)
            {
                parentId = entry.parent;
                return true;
            }
            parentId = null;
            return false;
        }

        /// <summary>
        /// <see cref="TryGetEntryParent(NeoValueOwnership, string, out string)"/>
        /// in any writable store: an authored row can be an entry of a
        /// writable collection, which records it under its own store.
        /// </summary>
        internal bool TryGetEntryParent(string id, out string? parentId)
        {
            parentId = null;
            return entries is not null
                && (TryGetEntryParent(NeoValueOwnership.Session, id, out parentId)
                    || TryGetEntryParent(NeoValueOwnership.Save, id, out parentId)
                    || TryGetEntryParent(NeoValueOwnership.User, id, out parentId));
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
            ulong previousCollections = collectionBits, previousTouched = touchedBits;
            int previousShape = Shape;
            bool previousGrid = GridDependent, previousAnyParent = anyParent;
            (NeoValueOwnership ownership, string parent, string key)? addedField = null;
            string? previousField = null;
            bool addedFieldKey = false;
            (NeoValueOwnership ownership, string id)? addedParent = null;
            if (staging != 0)
                (collectionRollbacks ??= new()).Add(() =>
                {
                    collections.Remove((ownership, id));
                    collectionBits = previousCollections;
                    touchedBits = previousTouched;
                    Shape = previousShape;
                    GridDependent = previousGrid;
                    anyParent = previousAnyParent;
                    if (addedField is { } field)
                    {
                        if (previousField is null)
                            fields!.Remove(field);
                        else
                            fields![field] = previousField;
                        if (addedFieldKey)
                            fieldKeys!.Remove(field.key);
                    }
                    if (addedParent is { } parent)
                        parents!.Remove(parent);
                });
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
                        addedField = (ownership, parent, field.Key);
                        (fields ??= new()).TryGetValue(addedField.Value, out previousField);
                        fields[addedField.Value] = id;
                        addedFieldKey = (fieldKeys ??= new HashSet<string>(StringComparer.Ordinal)).Add(field.Key);
                        return;
                    }
            if ((parents ??= new()).Add((ownership, parent)))
                addedParent = (ownership, parent);
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
            if (entries is not null && entries.Remove((from, id), out var entry))
                entries[(to, id)] = (entry.parent, null);
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
            Plan.LocalSelections?.Prepare();
            NeoWritePlan plan = Plan;
            if (plan.Rows.Count == 0 && plan.Bindings.Count == 0 && plan.ListenerEntries?.Count is not > 0)
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
        internal void Release(NeoValueOwnership ownership, string id, Member? member, string parentId)
        {
            Plan.Client.CaptureListenerMove(Plan, ownership, ownership, id, member, parentId);
            (releases ??= new()).Add((ownership, id, member, parentId));
            SetEntryParent(ownership, id, null, member is ListMember or DictionaryMember && Plan.Resolve(ownership, id) is { } row
                ? new RemovalContext(Plan.Client, row, member) : null);
        }

        /// <summary>Records that collection <paramref name="parentId"/> gained entry <paramref name="id"/>.</summary>
        internal void Gain(NeoValueOwnership ownership, string id, string parentId) =>
            SetEntryParent(ownership, id, parentId);

        private void SetEntryParent(NeoValueOwnership ownership, string id, string? parentId, RemovalContext? context = null)
        {
            var key = (ownership, id);
            bool present = (entries ??= new()).TryGetValue(key, out var previous);
            if (staging != 0)
                (collectionRollbacks ??= new()).Add(() =>
                {
                    if (present)
                        entries[key] = previous;
                    else
                        entries.Remove(key);
                });
            entries[key] = (parentId, context);
        }

        /// <summary>Stages every changed collection's row and the entries it released.</summary>
        internal void Prepare()
        {
            RegisterPublicationHook();
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
            foreach (NeoValueOwnership ownership in new[] { NeoValueOwnership.Session, NeoValueOwnership.Save, NeoValueOwnership.User })
            {
                List<(string id, Member? member)>? roots = null;
                foreach (var release in releases)
                    if (release.ownership == ownership)
                        (roots ??= new()).Add((release.id, release.member));
                if (roots is not null)
                {
                    var contexts = new Dictionary<object, NSGetterEvaluator.Context>();
                    var members = new Dictionary<string, Member?>(StringComparer.Ordinal);
                    foreach (var release in releases)
                    {
                        if (release.ownership != ownership)
                            continue;
                        members[release.id] = release.member;
                        Collection(ownership, release.parentId)?.ForEachContext(ctx =>
                        {
                            if (ctx is not null)
                                contexts[ctx.rowReverseIndex] = ctx;
                        });
                    }
                    Plan.Client.StageUnlinkedRemovals(Plan, ownership, roots, removed =>
                    {
                        foreach (string id in removed)
                            if (AttachmentForRow(ownership, id) is { } attachment)
                                contexts[attachment.Context.rowReverseIndex] = attachment.Context;
                        var removedIds = new HashSet<string>(removed, StringComparer.Ordinal);
                        var associations = new Dictionary<string, Dictionary<object, NSGetterEvaluator.Context>>(StringComparer.Ordinal);
                        void Associate(string id, NSGetterEvaluator.Context context)
                        {
                            if (!associations.TryGetValue(id, out var caches))
                                associations[id] = caches = new();
                            caches[context.rowReverseIndex] = context;
                        }
                        // Build the join before live-alias checks can prune dead
                        // weak entries from these existing participating indexes.
                        foreach (var context in contexts.Values)
                        {
                            if (removedIds.Count <= context.rowAliases.Keys.Count)
                            {
                                foreach (string id in removedIds)
                                    if (context.rowAliases.Contains(new NSGetterEvaluator.RowKey(ownership, id)))
                                        Associate(id, context);
                            }
                            else
                            {
                                foreach (var key in context.rowAliases.Keys)
                                    if (key.ownership == ownership && removedIds.Contains(key.rowId))
                                        Associate(key.rowId, context);
                            }
                        }
                        foreach (string id in removed)
                            if (AttachmentForRow(ownership, id) is { } attachment)
                                Associate(id, attachment.Context);
                        var retained = new Dictionary<(NeoValueOwnership scope, string id), string>();
                        // The existing removal walk is descendants first. Across
                        // all caches, retain a parent before any aliased child.
                        for (int index = removed.Count - 1; index >= 0; index--)
                            if (associations.TryGetValue(removed[index], out var caches))
                                foreach (var context in caches.Values)
                                    NSGetterEvaluator.RetainRemovedCollectionAlias(Plan, context, ownership,
                                        removed[index], members, retained, this);
                    });
                }
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
            private NeoScript.OrderedMap<string, string>? ordered;
            private HashSet<string>? members;
            // A value Set's entry ids by P108 key, built on the first lookup.
            private NeoSetEntryIndex? setEntries;
            private Member? setEntryMember;
            private NeoWritePlan? setPlan;

            internal PendingList(ArrayMemberValue row, NeoValueOwnership ownership, object? target) : base(row, ownership, target)
            {
                ids = row.value ?? Array.Empty<string>();
                Count = ids.Length;
            }

            internal int Count
            {
                get; private set;
            }

            internal string this[int index] => ordered is null ? ids[index] : ordered.Snapshot()[index];

            private NeoScript.OrderedMap<string, string> Ordered()
            {
                if (ordered is not null)
                    return ordered;
                ordered = new NeoScript.OrderedMap<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < Count; i++)
                    ordered.Add(ids[i], ids[i]);
                return ordered;
            }

            internal void AddSetEntry(string id, NeoTimestamp now)
            {
                if (!Ordered().Add(id, id))
                    return;
                Count++;
                members?.Add(id);
                setEntries?.Add(SetEntryKey(id), id);
                Changes(now);
            }

            internal bool RemoveSetEntry(string id, NeoTimestamp now)
            {
                if (!Ordered().Remove(id))
                    return false;
                ForgetSetEntry(id);
                Count--;
                members?.Remove(id);
                Changes(now);
                return true;
            }


            /// <summary>Whether the list holds <paramref name="id"/>, through a set built once.</summary>
            internal bool Contains(string id)
            {
                if (ordered is not null)
                    return ordered.Contains(id);
                if (members is null)
                {
                    members = new HashSet<string>(StringComparer.Ordinal);
                    for (int index = 0; index < Count; index++)
                        members.Add(ids[index]);
                }
                return members.Contains(id);
            }

            /// <summary>
            /// The id of the value Set entry whose P108 key is <paramref name="key"/>,
            /// through a map built once and kept with the list's changes.
            /// </summary>
            internal string? FindSetEntry(string key, Member entryMember, NeoWritePlan plan)
            {
                if (setEntries is null)
                {
                    setEntryMember = entryMember;
                    setPlan = plan;
                    setEntries = new NeoSetEntryIndex(Count);
                    for (int index = 0; index < Count; index++)
                        setEntries.Add(SetEntryKey(this[index]), this[index]);
                }
                return setEntries.TryGetValue(key, out string? id) ? id : null;
            }

            internal int IndexOf(string id) => Array.IndexOf(ordered?.Snapshot() ?? ids, id, 0, Count);

            private string SetEntryKey(string id)
            {
                if (!setPlan!.TryGet(Ownership, id, out MemberValue? entry))
                    throw new NSGetterRuntimeError($"Set '{Row.id}' entry '{id}' has no row.");
                return NeoSetEntryKey.Of(setEntryMember!, entry!, setPlan.Client, id => setPlan.TryGet(Ownership, id, out MemberValue? child) ? child : null);
            }

            private void ForgetSetEntry(string id)
            {
                if (setEntries is null)
                    return;
                string key = SetEntryKey(id);
                setEntries.Remove(key, id);
            }

            internal void Insert(int index, string id, NeoTimestamp now)
            {
                if (ordered is not null)
                    Own();
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
                setEntries?.Add(SetEntryKey(id), id);
                Changes(now);
            }

            /// <summary>Replaces the entry at <paramref name="index"/>, returning the id it held.</summary>
            internal void Reorder(string[] order, NeoTimestamp now)
            {
                if (order.Length != Count)
                    throw new NSGetterRuntimeError("Sort changed the list entry count.");
                ids = order;
                owned = true;
                ordered = null;
                Changes(now);
            }

            internal string Set(int index, string id, NeoTimestamp now)
            {
                Own();
                string previous = ids[index];
                ForgetSetEntry(previous);
                ids[index] = id;
                setEntries?.Add(SetEntryKey(id), id);
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
                Own();
                string id = ids[index];
                ForgetSetEntry(id);
                Array.Copy(ids, index + 1, ids, index, Count - index - 1);
                ids[--Count] = null!;
                members?.Remove(id);
                Changes(now);
                return id;
            }

            // Copies the committed row's array before the first change.
            private void Own()
            {
                if (ordered is not null)
                {
                    ids = ordered.Snapshot();
                    ordered = null;
                    owned = false;
                }
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
                var cleared = ordered is null ? new string[Count] : ordered.Snapshot();
                if (ordered is null)
                    Array.Copy(ids, cleared, Count);
                ordered?.Clear();
                ids = Array.Empty<string>();
                owned = true;
                Count = 0;
                members?.Clear();
                setEntries?.Clear();
                Changes(now);
                return cleared;
            }

            /// <summary>Gives the row the exact array it stores.</summary>
            internal void Seal()
            {
                if (ordered is not null)
                    ids = ordered.Snapshot();
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
            {
                scriptChangeBatchOpen = true;
                BeginChangeBatch();
                HoldGetterChanges();
            }
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
                // Released into the execution's change batch, when it has
                // one. Closed first: an effect the release runs is an
                // execution of its own, with its own batch.
                bool endBatch = scriptChangeBatchOpen;
                scriptChangeBatchOpen = false;
                try
                {
                    ReleaseGetterChanges();
                }
                finally
                {
                    if (endBatch)
                        EndChangeBatch();
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
            Exception? failure = null;
            bool completed = false;
            try
            {
                batch.Prepare();
                batch.RegisterPublicationHook();
                if (batch.Plan.Rows.Count != 0 || batch.Plan.Bindings.Count != 0 || batch.Plan.ListenerEntries?.Count > 0)
                    CommitScriptPlan(batch.Plan);
                completed = true;
            }
            catch (Exception error)
            {
                failure = error;
                // A failed preparation never published its pending reads. A
                // notification failure after installation is not a rollback.
                if (!batch.Plan.Installed)
                    foreach (string id in batch.CollectionIds)
                        InvalidateGetterMemoForRow(id);
            }
            batch.FinishCommit(failure, completed || batch.Plan.Installed);
        }

        internal bool DeferScriptAllocationCompletion(NeoScriptAllocationTracker tracker,
            NSGetterEvaluator.Context context, in NeoScriptExecutionResult result)
        {
            if (scriptWriteDepth <= 1 || CommittableScriptWrites is not { } batch)
                return false;
            tracker.DeferCompletion(batch, context, in result);
            return true;
        }

        internal bool TryFindRemovalParent(NeoWriteBatch batch, NeoValueOwnership scope, string id,
            out string? parent, out NeoValueOwnership parentScope) =>
            TryFindOwningParentRow(scope, id, out parent, out parentScope, batch);

        internal bool CommitRemovedScriptReference(NeoValueOwnership ownership, string id)
        {
            if (CommittableScriptWrites is not { } batch || !batch.RemovesReference(ownership, id))
                return false;
            CommitScriptWrites();
            return true;
        }

        internal bool CommitRemovedScriptAlias(object value, NSGetterEvaluator.Context context)
        {
            if (CommittableScriptWrites is not { } batch || !batch.RemovesAlias(value, context))
                return false;
            CommitScriptWrites();
            return true;
        }

        internal void CommitScriptAttachment(object value)
        {
            if (CommittableScriptWrites is { } batch && batch.HasPendingAttachment(value))
                CommitScriptWrites();
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

        internal bool HasPendingScriptWrites => scriptWriteBatch is not null;

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
