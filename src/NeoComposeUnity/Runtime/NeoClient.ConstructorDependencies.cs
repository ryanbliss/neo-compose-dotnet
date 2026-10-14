// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // Constructor arguments are replay references, not owned fields. Keep
        // their inputs across storage transitions (including detached Session
        // clones), without assigning parents or changing reference identities.
        private void RetainConstructorDependencies(MemberValue value)
        {
            var plan = new NeoWritePlan(this);
            StageConstructorDependencies(plan, value);
            if (plan.Rows.Count > 0)
                plan.Commit();
        }

        private void StageConstructorDependencies(NeoWritePlan plan, MemberValue value, NeoValueOwnership? target = null)
        {
            if (value is not ObjectMemberValue { constructorArgs: { Count: > 0 } })
                return;
            NeoValueOwnership targetOwnership = target ?? PersistedOwnership;
            var sourceOwnership = targetOwnership == PersistedOwnership ? NeoValueOwnership.Session : PersistedOwnership;
            var pending = new Queue<(string id, Member? member)>();
            foreach (var link in EnumerateConstructorDependencyLinks(value, targetOwnership))
                pending.Enqueue(link);
            if (pending.Count == 0)
                return;

            var visited = new HashSet<string>();
            var copies = new List<MemberValue>();
            while (pending.Count > 0)
            {
                var (id, member) = pending.Dequeue();
                if (!visited.Add(id))
                    continue;
                MemberValue? row;
                if (!plan.TryGetWritable(targetOwnership, id, out row))
                {
                    // Existing authored identities must keep resolving through
                    // the export, even if Session has an override at that id.
                    if (data.values.ContainsKey(id)
                        || !plan.TryGetWritable(sourceOwnership, id, out row))
                        continue;
                    copies.Add(CloneValueRow(row));
                }
                foreach (var child in EnumerateOwnedChildLinks(row, member))
                    if (ChildOwnership(child.member, targetOwnership) != sourceOwnership)
                        pending.Enqueue((child.valueId, child.member));
                if (member is ListMember list && IsUnorderedList(list))
                {
                    var owner = GetWritableStore(targetOwnership).values.ContainsKey(id) ? targetOwnership : sourceOwnership;
                    var entry = TryResolveCollectionEntryMember(list, row);
                    foreach (var childId in EnumerateContainerMemberValueIds(owner, id))
                        pending.Enqueue((childId, entry));
                }
                foreach (var dependency in EnumerateConstructorDependencyLinks(row, targetOwnership))
                    pending.Enqueue(dependency);
            }

            foreach (var row in copies)
                plan.Set(targetOwnership, row, silent: true);
        }

        private static readonly NeoValueOwnership[] RecipeStores =
        {
            NeoValueOwnership.Session,
            NeoValueOwnership.Save,
            NeoValueOwnership.User,
        };

        /// <summary>
        /// Whether a recipe outside <paramref name="removing"/>, in any
        /// writable store, needs the <paramref name="ownership"/> row
        /// <paramref name="valueId"/> as a constructor argument, directly or
        /// as something an argument owns. A loaded root's footprint holds
        /// what an adopted argument owns, and removes those rows one by one.
        /// </summary>
        private bool IsLiveConstructorInput(
            NeoWritePlan plan, NeoValueOwnership ownership, string valueId, HashSet<string> removing)
        {
            HashSet<string> seen = RentIdSet();
            try
            {
                return Named(valueId);
            }
            finally
            {
                ReturnIdSet(seen);
            }

            bool Named(string id)
            {
                HashSet<string> parents = RentIdSet();
                try
                {
                    CollectPlacementParents(id, parents);
                    plan.CollectParentCandidates(id, parents);
                    foreach (NeoValueOwnership store in RecipeStores)
                        if (plan.TryGetWritable(store, id, out MemberValue? row) && row!.containerId is string container)
                            parents.Add(container);
                    foreach (string parentId in parents)
                    {
                        if (removing.Contains(parentId) || !seen.Add(parentId))
                            continue;
                        bool stored = false;
                        foreach (NeoValueOwnership store in RecipeStores)
                        {
                            if (!plan.TryGetWritable(store, parentId, out MemberValue? parent) || parent!.IsRemoved)
                                continue;
                            stored = true;
                            // A recipe reads its own store's copy first.
                            if (parent is ObjectMemberValue { classId: not null } recipe
                                && (store == ownership || !plan.TryGetWritable(store, id, out _))
                                && ConstructorArgsReference(recipe, id))
                                return true;
                        }
                        // What an input owns is an input too: a collection
                        // argument's entries, and a Class argument's fields.
                        if (stored && Named(parentId))
                            return true;
                    }
                    return false;
                }
                finally
                {
                    ReturnIdSet(parents);
                }
            }
        }

        /// <summary>
        /// Marks the rows of <paramref name="ownership"/> that recipes stored
        /// in the other writable stores name, such as a Save row a Session
        /// stack was built from before either was saved. A recipe reads its
        /// own store's copy first, so that copy releases the row.
        /// </summary>
        private void MarkOtherStoreConstructorInputs(NeoValueOwnership ownership, HashSet<string> reachable)
        {
            var target = GetWritableStore(ownership).values;
            foreach (NeoValueOwnership store in RecipeStores)
            {
                if (store == ownership)
                    continue;
                foreach (MemberValue row in GetWritableStore(store).values.Values)
                {
                    if (row is not ObjectMemberValue { constructorArgs: { Count: > 0 } } recipe
                        || recipe.IsRemoved
                        || !NamesRowIn(recipe, target))
                        continue;
                    foreach (var dependency in EnumerateConstructorDependencyLinks(recipe, ownership))
                        if (!GetWritableStore(store).values.ContainsKey(dependency.id))
                            MarkReachableValue(ownership, dependency.id, reachable, dependency.member);
                }
            }
        }

        private static bool NamesRowIn(ObjectMemberValue recipe, Dictionary<string, MemberValue> values)
        {
            foreach (JToken? token in recipe.constructorArgs!.Values)
                if (token?.Type == JTokenType.String && values.ContainsKey(token.Value<string>()!))
                    return true;
            return false;
        }

        /// <summary>
        /// Whether a recipe stored outside <paramref name="ownership"/> names
        /// <paramref name="valueId"/>, which keeps it reachable there.
        /// </summary>
        private bool OtherStoreRecipeNames(NeoValueOwnership ownership, string parentId, string valueId)
        {
            foreach (NeoValueOwnership store in RecipeStores)
                if (store != ownership
                    && TryGetWritableValue(store, parentId, out MemberValue? parent)
                    && parent is ObjectMemberValue { classId: not null } recipe
                    && !recipe.IsRemoved
                    && ConstructorArgsReference(recipe, valueId)
                    && !GetWritableStore(store).values.ContainsKey(valueId))
                    return true;
            return false;
        }

        private IEnumerable<(string id, Member? member)> EnumerateConstructorDependencyLinks(
            MemberValue value, NeoValueOwnership ownership)
        {
            foreach (var (id, _) in EnumerateTypedConstructorReferences(value, ownership))
                yield return (id, TryInferMemberForValueId(id, out Member? member) ? member : null);
        }

        private IEnumerable<(string id, TypeInfo type)> EnumerateTypedConstructorReferences(
            MemberValue value, NeoValueOwnership ownership)
        {
            if (value is not ObjectMemberValue { constructorArgs: { Count: > 0 } } row
                || row.instanceConstructorId is not string constructorId
                || !data.constructors.TryGetValue(constructorId, out var constructor)
                || constructor.argumentTypes.Length == 0)
                yield break;

            // Only a runtime-constructed Class root carries a genericBindings
            // stamp, so a placed generic instance such as ValueWatcher<int>
            // closes its params through the PLACEMENT that declares it.
            // Without those arguments every generic constructor parameter
            // resolves unbound and the walk throws mid-commit.
            var placement = TryInferMemberForValueId(row.id, out Member? inferred)
                ? inferred as ClassMember
                : null;
            var env = NeoGenericResolution.ResolveInstanceEnv(this, constructor.classId,
                NeoGenericResolution.CloseClassArgumentsFromStamp(row.genericBindings, placement?.classArguments));
            for (int index = 0; index < constructor.argumentTypes.Length; index++)
            {
                // Scalar arguments cannot retain another row. Some detached
                // authored frames have no closed placement until instantiated.
                if (!row.constructorArgs!.TryGetValue(ConstructorParameterId(constructor, index), out var token)
                    || token?.Type != JTokenType.String)
                    continue;
                var parameter = NeoNSFunctionRuntime.ResolveInvocationTypeInfo(this, constructor.argumentTypes[index], env);
                if (parameter.type is not (MemberKind.Class or MemberKind.Interface or MemberKind.List or MemberKind.Set or MemberKind.Dictionary))
                    continue;
                var pending = new Queue<(string id, TypeInfo type)>();
                var visited = new HashSet<string>();
                pending.Enqueue((token.Value<string>()!, parameter));
                while (pending.Count > 0)
                {
                    var (id, type) = pending.Dequeue();
                    if (!visited.Add(id))
                        continue;
                    yield return (id, type);
                    TypeInfo? entryType = type switch
                    {
                        FunctionArgumentTypeInfo argument => argument.entryTypeInfo,
                        CollectionTypeInfo collection => collection.entryTypeInfo,
                        _ => null,
                    };
                    if (entryType is null)
                        continue;
                    MemberValue? argumentRow;
                    var argumentOwnership = saveData.values.ContainsKey(id) ? NeoValueOwnership.Save
                        : data.values.ContainsKey(id) ? NeoValueOwnership.Asset : ownership;
                    if (!TryGetValue(argumentOwnership, id, out argumentRow))
                        continue;
                    if (type.type is MemberKind.List or MemberKind.Set && argumentRow is ArrayMemberValue array)
                    {
                        foreach (var childId in array.value ?? System.Array.Empty<string>())
                            pending.Enqueue((childId, entryType));
                        foreach (var childId in EnumerateContainerMemberValueIds(argumentOwnership, id))
                            pending.Enqueue((childId, entryType));
                    }
                    else if (type.type == MemberKind.Dictionary && argumentRow is ObjectMemberValue dictionary
                        && dictionary.value is not null)
                    {
                        foreach (var childId in dictionary.value.Values)
                            pending.Enqueue((childId, entryType));
                    }
                }
            }
        }

        private bool TryResolveConstructorReferenceMember(ObjectMemberValue row, string valueId, out ClassMember member)
        {
            member = null!;
            if (row.constructorArgs is null || row.instanceConstructorId is not string constructorId
                || !data.constructors.TryGetValue(constructorId, out var constructor))
                return false;
            foreach (var (id, type) in EnumerateTypedConstructorReferences(row, NeoValueOwnership.Session))
            {
                if (id != valueId || type.type is not (MemberKind.Class or MemberKind.Interface))
                    continue;
                string? classId = type is ClassTypeInfo classType ? classType.classId
                    : type is FunctionArgumentTypeInfo argument ? argument.classId : null;
                if (type.type == MemberKind.Interface && TryGetValue(valueId, out ObjectMemberValue? value))
                    classId = value.classId;
                if (classId is null)
                    continue;
                member = new ClassMember
                {
                    id = constructorId + ":" + valueId,
                    name = constructorId,
                    kind = MemberKind.Class,
                    classId = classId,
                    Requirement = type.required ? NeoMemberRequirementKind.Required : NeoMemberRequirementKind.Optional,
                };
                return true;
            }
            return false;
        }

        // Collection inputs have no schema field to provide entry placement.
        // Walk their raw containers to the recipe, then use its declared type;
        // this does not turn replay references into ownership relationships.
        private bool TryResolveConstructorCollectionEntry(string valueId, out ClassMember member)
        {
            var pending = new Queue<string>();
            var visited = new HashSet<string>();
            pending.Enqueue(valueId);
            while (pending.Count > 0)
            {
                string id = pending.Dequeue();
                if (!visited.Add(id))
                    continue;
                if (TryGetValue(id, out MemberValue? row) && !string.IsNullOrEmpty(row.containerId))
                    pending.Enqueue(row.containerId!);
                foreach (var parent in InferMemberParents(id))
                {
                    if (parent.Value is ObjectMemberValue obj
                        && TryResolveConstructorReferenceMember(obj, valueId, out member))
                        return true;
                    if (parent.Value is ArrayMemberValue
                        || parent.Value is ObjectMemberValue { classId: null })
                        pending.Enqueue(parent.Key);
                }
            }
            member = null!;
            return false;
        }
    }
}
