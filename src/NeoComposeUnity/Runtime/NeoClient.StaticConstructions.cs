// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private sealed class StaticConstructionState
        {
            internal bool Initializing;
            internal bool Initialized;
            internal Exception? Error;
            internal readonly Dictionary<string, NeoStaticBinding> Bindings = new();
        }
        private readonly Dictionary<TypeBinding, StaticConstructionState> staticConstructionStates = new();
        private object? staticConstructionSchema;
        private readonly Stack<NeoWritePlan> staticInitializationPlans = new();
        private sealed class ConstructionIndex
        {
            internal List<NeoStaticConstruction>? Source;
            internal readonly Dictionary<(string memberId, TypeBinding construction), NeoStaticConstruction> Entries = new();
            internal readonly Dictionary<string, HashSet<NeoStaticConstruction>> ByValue = new();
            internal void AddValue(NeoStaticConstruction entry)
            {
                if (entry.value is not string valueId)
                    return;
                if (!ByValue.TryGetValue(valueId, out var owners))
                    ByValue[valueId] = owners = new();
                owners.Add(entry);
            }
            internal void RemoveValue(NeoStaticConstruction entry)
            {
                if (entry.value is string valueId && ByValue.TryGetValue(valueId, out var owners))
                {
                    owners.Remove(entry);
                    if (owners.Count == 0)
                        ByValue.Remove(valueId);
                }
            }
        }
        private readonly Dictionary<NeoValueOwnership, ConstructionIndex> constructionIndexes = new();

        private ConstructionIndex ConstructionEntries(NeoValueOwnership ownership)
        {
            var source = GetWritableStore(ownership).staticConstructions;
            if (!constructionIndexes.TryGetValue(ownership, out var index))
                constructionIndexes[ownership] = index = new ConstructionIndex();
            if (!ReferenceEquals(index.Source, source))
            {
                index.Source = source;
                index.Entries.Clear();
                index.ByValue.Clear();
                foreach (var entry in source ?? Enumerable.Empty<NeoStaticConstruction>())
                {
                    index.Entries.Add((entry.memberId, NeoInterfaceDispatch.Identity(entry.construction)), entry);
                    index.AddValue(entry);
                }
            }
            return index;
        }

        /// <summary>
        /// Each closed construction's bound value in <paramref name="ownership"/>,
        /// committed or pending in a static initialization or the candidate plan.
        /// They are roots, as a non-generic static's binding is (P127 §5.3).
        /// </summary>
        private IEnumerable<(string memberId, TypeBinding construction, string valueId)> ConstructionRoots(NeoValueOwnership ownership)
        {
            HashSet<(string memberId, TypeBinding construction)>? rebound = null;
            foreach (var plan in staticInitializationPlans.Append(candidateReadPlan))
                if (plan?.ConstructionBindings is not null)
                    foreach (var pair in plan.ConstructionBindings)
                    {
                        if (pair.Key.ownership != ownership)
                            continue;
                        (rebound ??= new()).Add((pair.Key.memberId, pair.Key.construction));
                        if (pair.Value is string valueId)
                            yield return (pair.Key.memberId, pair.Key.construction, valueId);
                    }
            // A pending binding replaces its committed one.
            foreach (var entry in ConstructionEntries(ownership).Entries.Values)
                if (entry.value is string valueId && rebound?.Contains((entry.memberId, entry.construction)) != true)
                    yield return (entry.memberId, entry.construction, valueId);
        }

        internal bool IsInitializingStaticConstruction(TypeBinding construction) =>
            staticConstructionStates.TryGetValue(NeoInterfaceDispatch.Identity(construction), out var state) && state.Initializing;

        internal NeoStaticBinding ConstructedStaticBinding(string memberId, TypeBinding construction)
        {
            construction = NeoInterfaceDispatch.Identity(construction);
            EnsureStaticConstruction(construction);
            if (!staticConstructionStates[construction].Bindings.TryGetValue(memberId, out var binding))
                throw new NSGetterRuntimeError($"Static '{memberId}' does not belong to '{NeoTypeNames.Of(construction, data)}'.");
            return binding;
        }

        internal void EnsureStaticConstruction(TypeBinding construction)
        {
            construction = NeoInterfaceDispatch.Identity(construction);
            if (!ReferenceEquals(staticConstructionSchema, SchemaResolution))
            {
                staticConstructionStates.Clear();
                staticConstructionSchema = SchemaResolution;
            }
            // Non-generic interface defaults carry their construction as a frame too.
            if (construction.Bindings is null)
                return;
            if (staticConstructionStates.TryGetValue(construction, out var existing))
            {
                if (existing.Error is not null)
                {
                    if (existing.Error is NSGetterRuntimeError cached)
                    {
                        cached.Payload.Searched = false;
                        cached.Payload.Selection = null;
                    }
                    throw existing.Error;
                }
                if (existing.Initialized || existing.Initializing)
                    return;
            }
            var state = existing ?? new StaticConstructionState();
            staticConstructionStates[construction] = state;
            state.Initializing = true;
            try
            {
                var environment = NeoInterfaceDispatch.Of(this).Environment(construction);
                IEnumerable<string> ids;
                if (construction.Head!.ClassId is string classId)
                {
                    var owner = data.classes[classId];
                    ids = (owner.schemaKeyOrder ?? Array.Empty<string>()).Concat(owner.schema.Keys).Distinct()
                        .Where(owner.schema.ContainsKey).Select(key => owner.schema[key]);
                }
                else
                {
                    var owner = data.interfaces[construction.Head.InterfaceId!];
                    ids = (owner.memberKeyOrder ?? new List<string>()).Concat(owner.members.Keys).Distinct()
                        .Where(owner.members.ContainsKey).Select(key => owner.members[key])
                        .Where(member => member.IsStatic && member.implementationMemberId is not null)
                        .Select(member => member.implementationMemberId!);
                }
                var fields = ids.Distinct().Select(id => data.members[id])
                    .Where(member => member.Modifier == NeoMemberModifierKind.Static && member is not NSFunctionMember and not FunctionMember and not NSPropertyMember).ToList();
                foreach (var member in fields)
                {
                    var resolved = NeoGenericResolution.SubstituteMember(this, member, environment);
                    state.Bindings[member.id] = new NeoStaticBinding(this, resolved, construction);
                }
                // Only bindings present before initialization are restored values. A sibling's
                // initializer may write this field, but that does not replace its initializer.
                var restored = fields.Where(member => ConstructionEntry(ResolveStaticOwnership(member), member.id, construction) is not null)
                    .Select(member => member.id).ToHashSet();
                if (restored.Count != fields.Count)
                {
                    var plan = new NeoWritePlan(this, requiredSaveFormatRevision: 3);
                    staticInitializationPlans.Push(plan);
                    try
                    {
                        using (ReadCandidate(plan))
                            foreach (var member in fields)
                            {
                                if (restored.Contains(member.id))
                                    continue;
                                var binding = state.Bindings[member.id];
                                InitializerBody? initializer = member.valueId is string sourceId
                                    ? plan.Resolve(NeoValueOwnership.Asset, sourceId)?.init : MemberValueFactory.InitializerOf(member);
                                // A collection whose entries run code builds as its literal does.
                                bool computedTemplate = initializer is null && member.valueId is string templateId
                                    && binding.Member is ListMember and not SetMember or DictionaryMember
                                    && TemplateRunsCode(plan, templateId);
                                if (initializer is not null || computedTemplate)
                                {
                                    var context = CreateGetterContext(binding.Ownership);
                                    context.BindRoot(NeoScriptValueMarshaller.ResolveRoot(this, context));
                                    object? value = initializer is not null
                                        ? NeoGeneratedTypesSupport.EvaluateInitializer(this, context, default, null,
                                            binding.Member, initializer, environment, construction)
                                        : EvaluateStaticTemplate(plan, binding.Member, member.valueId!, context, environment, construction);
                                    // An object static takes a class value by reference, as a class static does (P127 §1.2).
                                    if (value is not null
                                        && (binding.Member is ClassMember
                                            || binding.Member is ObjectMember && NeoScriptExecutor.HoldsStoredClassValue(value, context)))
                                    {
                                        var reference = NSGetterEvaluator.ConstructorReferenceOf(value, context)
                                            ?? throw new NSGetterRuntimeError($"Static initializer '{member.name}' produced no class identity.");
                                        string imported = NeoScriptExecutor.ImportClassValueReference(plan, this, binding.Ownership, reference.valueId, context);
                                        ReplaceConstructionRoot(plan, binding, construction, imported);
                                    }
                                    else
                                    {
                                        object? payload = binding.Member is ObjectMember objectMember && value is NeoBox or NeoTypeValue
                                            ? NeoGeneratedTypesSupport.EvaluatorObjectPayload(this, objectMember, binding.Ownership, value!, context, Guid.NewGuid().ToString(), plan)
                                            : value is null ? null : NeoGeneratedTypesSupport.MaterializeCollectionAssignment(this, binding.Member, value, binding.Ownership, context, plan, environment,
                                                initializerDefault: true);
                                        binding.PrepareInitialValue(plan, payload);
                                    }
                                }
                                else if (member.valueId is string template)
                                {
                                    string copy = PrepareFreshClone(plan, binding.Ownership, NeoValueOwnership.Asset, template,
                                        binding.Member, ListenerCopyIntent.ConstructionProjection, null);
                                    ReplaceConstructionRoot(plan, binding, construction, copy);
                                    NeoTypeSources.PlaceStaticConstructionRoot(this, plan, copy, binding.Member, construction);
                                }
                                else
                                {
                                    TypeInfo type = NeoTypeRules.BindingTypeInfo(this, binding.Member)!;
                                    binding.PrepareInitialValue(plan, NSGetterEvaluator.DefaultOf(type, this));
                                }
                            }
                        // Synchronous side effects commit independently. Their overlapping
                        // rows have already refreshed this candidate before its publication.
                        plan.Rebase();
                        plan.Commit();
                    }
                    finally
                    {
                        staticInitializationPlans.Pop();
                    }
                }
                state.Initialized = true;
            }
            catch (Exception error)
            {
                state.Error = NeoScriptErrorClassification.IsAuthoredCatchable(error)
                    ? StaticInitializationError(construction, error) : error;
                if (state.Error is NSGetterRuntimeError cached)
                {
                    cached.Payload.Searched = false;
                    cached.Payload.Selection = null;
                }
                throw state.Error;
            }
            finally
            {
                state.Initializing = false;
            }
        }

        /// <summary>
        /// Whether a static's template holds a row that runs code: a computed entry
        /// of a collection literal carries its own initializer (P127 §3.2).
        /// </summary>
        private static bool TemplateRunsCode(NeoWritePlan plan, string valueId)
        {
            MemberValue? row = plan.Resolve(NeoValueOwnership.Asset, valueId);
            if (row is null)
                return false;
            if (row.init is not null)
                return true;
            switch (row)
            {
                case ArrayMemberValue { classId: null, value: { } ids }:
                    foreach (string id in ids)
                        if (TemplateRunsCode(plan, id))
                            return true;
                    return false;
                case ObjectMemberValue { classId: null, value: { } pairs }:
                    foreach (string id in pairs.Values)
                        if (TemplateRunsCode(plan, id))
                            return true;
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>
        /// A static's template as its literal builds it, as a declaration default's
        /// copy does: a computed row runs its initializer, and a literal row reads.
        /// </summary>
        private object? EvaluateStaticTemplate(NeoWritePlan plan, Member member, string valueId, NSGetterEvaluator.Context context,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> environment, TypeBinding construction)
        {
            MemberValue row = plan.Resolve(NeoValueOwnership.Asset, valueId)
                ?? throw new NSGetterRuntimeError($"Static member '{member.name}' has a missing template value '{valueId}'.");
            if (row.init is not null)
                return NeoGeneratedTypesSupport.EvaluateInitializer(this, context, default, null, member, row.init, environment, construction);
            if (!TemplateRunsCode(plan, valueId))
                return NSGetterEvaluator.ResolveStoredCollection(
                    NSGetterEvaluator.ResolveValueIfId(valueId, context, NeoValueOwnership.Asset, member), context);
            if (member is ListMember and not SetMember && row is ArrayMemberValue { value: { } ids })
            {
                Member entry = TemplateEntryMember(member, ((ListMember)member).entryMemberId, environment);
                var entries = new object?[ids.Length];
                for (int index = 0; index < ids.Length; index++)
                    entries[index] = EvaluateStaticTemplate(plan, entry, ids[index], context, environment, construction);
                return entries;
            }
            if (member is DictionaryMember dictionary && row is ObjectMemberValue { value: { } pairs })
            {
                Member entry = TemplateEntryMember(member, dictionary.entryMemberId, environment);
                var entries = new Dictionary<string, object?>(pairs.Count, StringComparer.Ordinal);
                foreach (var pair in pairs)
                    entries[pair.Key] = EvaluateStaticTemplate(plan, entry, pair.Value, context, environment, construction);
                return entries;
            }
            throw new NSGetterRuntimeError($"Static member '{member.name}' has a computed entry in a value it cannot build.");
        }

        private Member TemplateEntryMember(Member member, string? entryMemberId, IReadOnlyDictionary<string, NeoGenericEnvEntry> environment)
        {
            if (entryMemberId is null || !TryGetMember(entryMemberId, out Member? entry))
                throw new NSGetterRuntimeError($"Static member '{member.name}' has no entry member '{entryMemberId}'.");
            return NeoGenericResolution.SubstituteMember(this, entry, environment);
        }

        private void RefreshStaticInitializationPlans(NeoWritePlan committed)
        {
            foreach (var pending in staticInitializationPlans)
                if (!ReferenceEquals(pending, committed))
                    pending.RefreshCommittedOverlaps(committed);
        }

        private void ReplaceConstructionRoot(NeoWritePlan plan, NeoStaticBinding binding, TypeBinding construction, string valueId)
        {
            string? previous = binding.ValueId;
            plan.BindConstruction(binding.Ownership, binding.MemberId, construction, valueId);
            if (previous is not null && previous != valueId)
                StageUnlinkedRemovals(plan, binding.Ownership, previous, binding.Member);
        }

        private NeoRuntimeError StaticInitializationError(TypeBinding construction, Exception cause) =>
            new(NeoErrorClass.NeoTypeInitializationError, $"The type initializer for '{NeoTypeNames.Of(construction, data)}' threw an exception.", cause);

        private NeoStaticConstruction? ConstructionEntry(NeoValueOwnership ownership, string memberId, TypeBinding construction) =>
            ConstructionEntries(ownership).Entries.GetValueOrDefault((memberId, construction));

        internal NeoStaticConstruction? ConstructionOfValue(NeoValueOwnership ownership, string valueId)
        {
            if (ownership == NeoValueOwnership.Asset)
                return null;
            if (candidateReadPlan?.ConstructionOfValue(ownership, valueId) is { } candidate)
                return candidate;
            if (ConstructionEntries(ownership).ByValue.TryGetValue(valueId, out var owners))
                foreach (var entry in owners)
                    if (candidateReadPlan?.ConstructionBindings?.TryGetValue((ownership, entry.memberId, entry.construction), out var proposed) != true
                        || proposed == valueId)
                        return entry;
            return null;
        }

        internal bool TryResolveConstructionBinding(Member member, NeoValueOwnership ownership, TypeBinding construction, out string? valueId)
        {
            NoteValueRead(StaticReadKey(member.id, ownership));
            if (candidateReadPlan?.ConstructionBindings?.TryGetValue((ownership, member.id, construction), out valueId) == true)
                return valueId is not null;
            valueId = ConstructionEntry(ownership, member.id, construction)?.value;
            return valueId is not null;
        }

        private void InstallConstructionBindings(NeoWritePlan plan)
        {
            if (plan.ReplacesStaticConstructions)
            {
                PersistedData.staticConstructions = plan.StaticConstructionSnapshot;
                staticConstructionStates.Clear();
                InvalidateGetterMemo();
            }
            if (plan.ConstructionBindings is null)
                return;
            foreach (var pair in plan.ConstructionBindings)
            {
                var store = GetWritableStore(pair.Key.ownership);
                var entries = store.staticConstructions ??= new List<NeoStaticConstruction>();
                var index = ConstructionEntries(pair.Key.ownership);
                var key = (pair.Key.memberId, pair.Key.construction);
                if (index.Entries.TryGetValue(key, out var entry))
                {
                    index.RemoveValue(entry);
                    entry.value = pair.Value;
                }
                else
                {
                    entry = new NeoStaticConstruction { memberId = pair.Key.memberId, construction = pair.Key.construction, value = pair.Value };
                    index.Entries.Add(key, entry);
                    entries.Add(entry);
                }
                index.AddValue(entry);
                InvalidateGetterMemoForRow(StaticReadKey(pair.Key.memberId, pair.Key.ownership));
                TouchWritableStoreUpdatedAt(pair.Key.ownership);
            }
            InvalidateGetterMemo();
        }
    }
}
