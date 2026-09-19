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
        // their Session-only inputs when a recipe becomes durable, without
        // assigning parents or copying shared arguments to different identities.
        private void RetainConstructorDependencies(MemberValue value)
        {
            var plan = new NeoWritePlan(this);
            StageConstructorDependencies(plan, value);
            if (plan.Rows.Count > 0) plan.Commit();
        }

        private void StageConstructorDependencies(NeoWritePlan plan, MemberValue value)
        {
            if (value is not ObjectMemberValue { constructorArgs: not null }) return;
            var pending = new Queue<(string id, Member? member)>();
            foreach (var link in EnumerateConstructorDependencyLinks(value, NeoValueOwnership.Session))
                pending.Enqueue(link);
            if (pending.Count == 0) return;

            var visited = new HashSet<string>();
            var copies = new List<MemberValue>();
            while (pending.Count > 0)
            {
                var (id, member) = pending.Dequeue();
                if (!visited.Add(id)) continue;
                MemberValue? row;
                if (!plan.TryGetWritable(NeoValueOwnership.Save, id, out row))
                {
                    // Existing authored identities must keep resolving through
                    // the export, even if Session has an override at that id.
                    if (data.values.ContainsKey(id)
                        || !plan.TryGetWritable(NeoValueOwnership.Session, id, out row)) continue;
                    copies.Add(CloneValueRow(row));
                }
                foreach (var child in EnumerateOwnedChildLinks(row, member))
                    if (child.member is null || DeclaredOwnership(child.member) != NeoValueOwnership.Session)
                        pending.Enqueue((child.valueId, child.member));
                if (member is ListMember list && IsUnorderedList(list))
                {
                    var owner = saveData.values.ContainsKey(id) ? NeoValueOwnership.Save : NeoValueOwnership.Session;
                    var entry = TryResolveCollectionEntryMember(list, row);
                    foreach (var childId in EnumerateContainerMemberValueIds(owner, id))
                        pending.Enqueue((childId, entry));
                }
                foreach (var dependency in EnumerateConstructorDependencyLinks(row, NeoValueOwnership.Session))
                    pending.Enqueue(dependency);
            }

            foreach (var row in copies)
                plan.Set(NeoValueOwnership.Save, row, silent: true);
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
            if (value is not ObjectMemberValue { constructorArgs: not null } row
                || row.instanceConstructorId is not string constructorId
                || !data.constructors.TryGetValue(constructorId, out var constructor)) yield break;

            // A Class row never carries a genericBindings stamp -- stamping is
            // a List/Dictionary creation step -- so a generic instance such as
            // ValueWatcher<int> closes its params through the PLACEMENT that
            // declares it. Without those arguments every generic constructor
            // parameter resolves unbound and the walk throws mid-commit.
            var placement = TryInferMemberForValueId(row.id, out Member? inferred)
                ? inferred as ClassMember
                : null;
            var env = NeoGenericResolution.ResolveInstanceEnv(this, constructor.classId,
                NeoGenericResolution.CloseClassArgumentsFromStamp(row.genericBindings, placement?.classArguments));
            for (int index = 0; index < constructor.argumentTypes.Length; index++)
            {
                var parameter = NeoNSFunctionRuntime.ResolveInvocationTypeInfo(this, constructor.argumentTypes[index], env);
                if (parameter.type is not (MemberKind.Class or MemberKind.Interface or MemberKind.List or MemberKind.Dictionary)
                    || !row.constructorArgs!.TryGetValue(ConstructorParameterId(constructor, index), out var token)
                    || token?.Type != JTokenType.String) continue;
                var pending = new Queue<(string id, TypeInfo type)>();
                var visited = new HashSet<string>();
                pending.Enqueue((token.Value<string>()!, parameter));
                while (pending.Count > 0)
                {
                    var (id, type) = pending.Dequeue();
                    if (!visited.Add(id)) continue;
                    yield return (id, type);
                    TypeInfo? entryType = type switch
                    {
                        FunctionArgumentTypeInfo argument => argument.entryTypeInfo,
                        CollectionTypeInfo collection => collection.entryTypeInfo,
                        _ => null,
                    };
                    if (entryType is null) continue;
                    MemberValue? argumentRow;
                    var argumentOwnership = saveData.values.ContainsKey(id) ? NeoValueOwnership.Save
                        : data.values.ContainsKey(id) ? NeoValueOwnership.Asset : ownership;
                    if (!TryGetValue(argumentOwnership, id, out argumentRow)) continue;
                    if (type.type == MemberKind.List && argumentRow is ArrayMemberValue array)
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
                || !data.constructors.TryGetValue(constructorId, out var constructor)) return false;
            foreach (var (id, type) in EnumerateTypedConstructorReferences(row, NeoValueOwnership.Session))
            {
                if (id != valueId || type.type is not (MemberKind.Class or MemberKind.Interface)) continue;
                string? classId = type is ClassTypeInfo classType ? classType.classId
                    : type is FunctionArgumentTypeInfo argument ? argument.classId : null;
                if (type.type == MemberKind.Interface && TryGetValue(valueId, out ObjectMemberValue? value))
                    classId = value.classId;
                if (classId is null) continue;
                member = new ClassMember
                {
                    id = constructorId + ":" + valueId, name = constructorId,
                    kind = MemberKind.Class, classId = classId,
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
                if (!visited.Add(id)) continue;
                if (TryGetValue(id, out MemberValue? row) && !string.IsNullOrEmpty(row.containerId))
                    pending.Enqueue(row.containerId!);
                foreach (var parent in InferMemberParents(id))
                {
                    if (parent.Value is ObjectMemberValue obj
                        && TryResolveConstructorReferenceMember(obj, valueId, out member)) return true;
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
