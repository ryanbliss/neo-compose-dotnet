// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    // Execution-local early selections. Public references continue to name the
    // immutable target selected when they were read. Only these exact script
    // occurrences follow the local list's existing bind/detach transitions.
    internal sealed class LocalCollectionSelections
    {
        private readonly Dictionary<object, Origin> origins = new(NSGetterEvaluator.ReferenceEqualityComparer.Instance);
        private readonly Dictionary<(NeoValueOwnership, string), Occurrence> occurrences = new();

        internal sealed class Materialization
        {
            internal readonly NeoWritePlan Plan;
            internal readonly NSGetterEvaluator.Context Context;
            internal readonly NeoValueOwnership Ownership;
            internal Materialization(NeoWritePlan plan, NSGetterEvaluator.Context context, NeoValueOwnership ownership)
            {
                Plan = plan;
                Context = context;
                Ownership = ownership;
            }
        }

        internal sealed class Origin
        {
            internal readonly LocalCollectionSelections Owner;
            internal readonly object Identity;
            internal readonly object Value;
            internal readonly NSGetterEvaluator.Context Context;
            internal readonly Member Member;
            internal readonly HashSet<Occurrence> Occurrences = new();
            internal string? TargetId;
            internal NeoValueOwnership Ownership;
            internal bool FieldBound;

            internal Origin(LocalCollectionSelections owner, object value, Member member, NSGetterEvaluator.Context context)
            {
                Owner = owner;
                Value = value;
                Identity = NSGetterEvaluator.SequenceCollectionIdentity(value);
                Member = member;
                Context = context;
            }
        }

        internal sealed class Occurrence
        {
            internal readonly Origin Origin;
            internal readonly NeoValueOwnership Ownership;
            internal readonly string Id;
            internal ArrayMemberValue? Row;
            internal bool Installed;
            internal Occurrence(Origin origin, NeoValueOwnership ownership, string id)
            {
                Origin = origin;
                Ownership = ownership;
                Id = id;
            }
        }

        internal Origin? Find(object? value) => value is not null
            && origins.TryGetValue(NSGetterEvaluator.SequenceCollectionIdentity(value), out Origin origin) ? origin : null;

        internal bool IsPublicationLocal(object value, NSGetterEvaluator.RowReference? reference)
        {
            Origin? origin = Find(value);
            return origin is { FieldBound: false } && (reference is null
                || reference.valueId == origin.TargetId && reference.ownership == origin.Ownership);
        }

        private Occurrence? Current(NeoValueOwnership ownership, string id, NeoWritePlan plan)
        {
            if (plan.LocalSelections?.CapturedAt(ownership, id) is { } pending
                && ReferenceEquals(pending.Origin.Owner, this)
                && pending.Row is not null && !pending.Row.IsRemoved
                && ReferenceEquals(plan.Resolve(ownership, id), pending.Row))
                return pending;
            return occurrences.TryGetValue((ownership, id), out Occurrence occurrence)
                && occurrence.Row is not null && !occurrence.Row.IsRemoved
                && ReferenceEquals(plan.Resolve(ownership, id), occurrence.Row) ? occurrence : null;
        }

        internal bool TryRead(NeoValueOwnership ownership, string id, NeoWritePlan plan, out object? value)
        {
            if (Current(ownership, id, plan) is { } occurrence)
            {
                value = NSGetterEvaluator.RuntimeCollection(occurrence.Origin.Value, occurrence.Origin.Context);
                return true;
            }
            value = null;
            return false;
        }

        internal bool Matches(NeoValueOwnership ownership, string id, object? value, NeoWritePlan plan) =>
            value is not null && Current(ownership, id, plan) is { } occurrence
                && ReferenceEquals(occurrence.Origin.Identity, NSGetterEvaluator.SequenceCollectionIdentity(value));

        internal static Occurrence? Capture(NeoWritePlan plan, NeoValueOwnership ownership, string id,
            LookupMember member, object value, NSGetterEvaluator.Context context,
            IReadOnlyDictionary<string, NeoGenericEnvEntry>? env)
        {
            if (member.Selection != NeoMemberSelectionKind.Single
                || value is string[]
                || value is not (object?[] or RuntimeSet or IDictionary<string, object?>)
                || NeoReferenceTypes.SlotTypeInfo(context.client, member, env) is not ReferenceTypeInfo { targetTypeInfo: { } target }
                || target.type is not (MemberKind.List or MemberKind.Set or MemberKind.Dictionary or MemberKind.Lookup)
                || NeoReferenceTypes.Category(target, context.client) != NeoReferenceCategory.Handle)
                return null;
            var state = context.allocationTracker.LocalSelections;
            var reference = NSGetterEvaluator.FindRowReference(value, context);
            if (reference is not null && state?.IsPublicationLocal(value, reference) != true)
                return null;
            state ??= context.allocationTracker.LocalSelections = new();
            Origin? origin = state.Find(value);
            bool added = origin is null;
            if (origin is null)
            {
                Member descriptor = NeoGeneratedTypesSupport.LocalSelectionDescriptor(context.client, target);
                origin = new Origin(state, value, descriptor, context);
                state.origins.Add(origin.Identity, origin);
            }
            var occurrence = new Occurrence(origin, ownership, id);
            origin.Occurrences.Add(occurrence);
            context.allocationTracker.ReusableContext = false;
            (plan.LocalSelections ??= new Publication(plan)).Capture(occurrence, added);
            return occurrence;
        }

        // A plan owns the prospective state; rejecting it never changes the
        // current local backing. TakeRows uses the same prepared metadata as a
        // complete commit, without completing the enclosing batch or tracker.
        internal sealed class Publication
        {
            private readonly NeoWritePlan plan;
            private readonly Dictionary<Origin, Change> changes = new();
            private readonly List<Action> journal = new();
            private readonly Dictionary<(NeoValueOwnership ownership, string id), Occurrence> captures = new();
            internal Occurrence? CapturedAt(NeoValueOwnership ownership, string id) =>
                captures.TryGetValue((ownership, id), out Occurrence occurrence) ? occurrence : null;
            private bool prepared;
            private bool installed;
            private List<Change>? preparedChanges;
            internal int JournalCount => journal.Count;

            private sealed class Change
            {
                internal readonly Origin Origin;
                internal object? Snapshot;
                internal string? Target;
                internal NeoValueOwnership Ownership;
                internal bool FieldBound;
                internal bool Transition;
                internal bool Allocated;
                internal HashSet<Occurrence> Captured = new();
                internal readonly List<Occurrence> Retired = new();
                internal readonly List<(Occurrence occurrence, ArrayMemberValue row)> Rows = new();
                internal Change(Origin origin)
                {
                    Origin = origin;
                    Target = origin.TargetId;
                    Ownership = origin.Ownership;
                    FieldBound = origin.FieldBound;
                }
            }

            internal Publication(NeoWritePlan plan) => this.plan = plan;

            private Change Get(Origin origin)
            {
                if (!changes.TryGetValue(origin, out Change change))
                {
                    changes.Add(origin, change = new Change(origin));
                    journal.Add(() => changes.Remove(origin));
                }
                return change;
            }

            internal void Capture(Occurrence occurrence, bool added)
            {
                Change captured = Get(occurrence.Origin);
                captured.Captured.Add(occurrence);
                var key = (occurrence.Ownership, occurrence.Id);
                bool present = captures.TryGetValue(key, out Occurrence previous);
                captures[key] = occurrence;
                journal.Add(() =>
                {
                    if (present)
                        captures[key] = previous;
                    else
                        captures.Remove(key);
                    captured.Captured.Remove(occurrence);
                    Origin origin = occurrence.Origin;
                    origin.Occurrences.Remove(occurrence);
                    if (added && origin.Occurrences.Count == 0 && origin.TargetId is null)
                        origin.Owner.origins.Remove(origin.Identity);
                });
            }

            internal void Transition(Origin origin, string? target, NeoValueOwnership ownership, object? snapshot)
            {
                Change previous = Get(origin);
                var next = new Change(origin)
                {
                    Target = target,
                    Ownership = ownership,
                    Snapshot = snapshot,
                    FieldBound = target is not null,
                    Transition = true,
                    Captured = previous.Captured
                };
                changes[origin] = next;
                journal.Add(() => changes[origin] = previous);
            }

            internal void Rollback(int count)
            {
                for (int i = journal.Count - 1; i >= count; i--)
                    journal[i]();
                journal.RemoveRange(count, journal.Count - count);
                prepared = false;
                preparedChanges = null;
            }

            internal void Discard()
            {
                if (!installed)
                    Rollback(0);
            }

            internal void Prepare()
            {
                if (prepared)
                    return;
                var completed = new List<Change>(changes.Count);
                NeoWritePlan.Checkpoint checkpoint = plan.Open();
                try
                {
                    foreach (Change requested in changes.Values)
                    {
                        Origin origin = requested.Origin;
                        var change = new Change(origin);
                        if (requested.Transition)
                        {
                            change.Target = requested.Target;
                            change.Ownership = requested.Ownership;
                            change.FieldBound = requested.FieldBound;
                            change.Snapshot = requested.Snapshot;
                            change.Transition = true;
                        }
                        if (change.Transition && change.Target is null)
                        {
                            change.Target = NeoGeneratedTypesSupport.MaterializeLocalSelection(plan, origin.Member,
                                change.Snapshot, origin.Context);
                            change.Ownership = NeoValueOwnership.Session;
                            change.Allocated = true;
                        }
                        // A newly captured selector needs publication; existing selectors
                        // need a write only when their local actually changes backing.
                        bool changedTarget = change.Transition && (change.Target != origin.TargetId
                            || change.Ownership != origin.Ownership);
                        foreach (Occurrence occurrence in changedTarget ? origin.Occurrences : requested.Captured)
                        {
                            if (occurrence.Row is null || occurrence.Row.IsRemoved
                                || !ReferenceEquals(plan.Resolve(occurrence.Ownership, occurrence.Id), occurrence.Row))
                            {
                                // An occurrence from another uninstalled plan can still arrive.
                                // Drop only installed or this plan's rejected destination after
                                // this plan succeeds; Stage/plan failure retains prior metadata.
                                if (occurrence.Installed || requested.Captured.Contains(occurrence))
                                    change.Retired.Add(occurrence);
                                continue;
                            }
                            if (plan.HeldBy?.RemovesReference(occurrence.Ownership, occurrence.Id) == true)
                                continue;
                            if (change.Target is null)
                            {
                                change.Target = NeoGeneratedTypesSupport.MaterializeLocalSelection(plan, origin.Member,
                                    change.Snapshot ?? NSGetterEvaluator.CurrentInsertionList(origin.Value), origin.Context);
                                change.Ownership = NeoValueOwnership.Session;
                                change.Allocated = true;
                            }
                            if (occurrence.Installed && occurrence.Row.value is { Length: 1 } selected
                                && selected[0] == change.Target)
                                continue;
                            var row = (ArrayMemberValue)plan.Client.CloneRowForWrite(occurrence.Row);
                            row.value = new[] { change.Target };
                            plan.Set(occurrence.Ownership, row);
                            change.Rows.Add((occurrence, row));
                        }
                        if (change.Target is not null && (change.Transition || change.Rows.Count != 0))
                        {
                            MemberValue? target = plan.Resolve(change.Ownership, change.Target);
                            if (target is null || target.IsRemoved
                                || !(origin.Member is ListMember && target is ArrayMemberValue
                                    || origin.Member is DictionaryMember && target is ObjectMemberValue))
                                throw new InvalidOperationException("An early collection selection has no staged live target.");
                        }
                        if (change.Transition || change.Rows.Count != 0 || change.Retired.Count != 0)
                            completed.Add(change);
                    }
                }
                catch
                {
                    plan.Rollback(checkpoint);
                    throw;
                }
                plan.Close();
                preparedChanges = completed;
                prepared = true;
            }

            internal void Install()
            {
                if (installed)
                    return;
                installed = true;
                foreach (Change change in preparedChanges!)
                {
                    Origin origin = change.Origin;
                    foreach (Occurrence retired in change.Retired)
                    {
                        origin.Occurrences.Remove(retired);
                        if (origin.Owner.occurrences.TryGetValue((retired.Ownership, retired.Id), out Occurrence current)
                            && ReferenceEquals(current, retired))
                            origin.Owner.occurrences.Remove((retired.Ownership, retired.Id));
                    }
                    if (change.Target is null || change.Rows.Count == 0 && !change.Transition)
                        continue;
                    origin.TargetId = change.Target;
                    origin.Ownership = change.Ownership;
                    origin.FieldBound = change.FieldBound;
                    foreach (var item in change.Rows)
                    {
                        var occurrence = item.occurrence;
                        var key = (occurrence.Ownership, occurrence.Id);
                        if (origin.Owner.occurrences.TryGetValue(key, out Occurrence previous)
                            && !ReferenceEquals(previous, occurrence))
                            previous.Origin.Occurrences.Remove(previous);
                        origin.Owner.occurrences[key] = occurrence;
                        occurrence.Row = item.row;
                        occurrence.Installed = true;
                    }
                    if (change.Allocated)
                    {
                        plan.Client.RegisterRetainedRootContract(change.Target, origin.Member);
                        if (plan.HeldBy is { } batch)
                            batch.RegisterRetainedRoot(change.Target, origin.Member, origin.Context.allocationTracker);
                        else
                            origin.Context.allocationTracker.RegisterSessionRoot(change.Target, origin.Member);
                    }
                    NSGetterEvaluator.InstallLocalSelectionBinding(origin.Value, change.Snapshot, origin.Member,
                        change.Target, change.Ownership, origin.Context);
                }
            }
        }
    }
}
