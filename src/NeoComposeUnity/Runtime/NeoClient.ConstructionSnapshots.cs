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
        // P130 §2. A variant composes declarative layers over its root's
        // construction output. Switching variants retires one layer and
        // composes another without rerunning the receiver constructor, so a
        // root of a class with variants keeps the temporary graph its
        // construction produced, before any layer. The snapshot lives exactly
        // as long as the installed expansion that took it.
        internal sealed class ConstructionSnapshot
        {
            internal readonly ObjectMemberValue Recipe;
            internal readonly ObjectMemberValue Root;
            internal readonly ClassMember Member;
            internal readonly List<MemberValue> Rows;

            internal ConstructionSnapshot(ObjectMemberValue recipe, ObjectMemberValue root, ClassMember member, List<MemberValue> rows)
            {
                Recipe = recipe;
                Root = root;
                Member = member;
                Rows = rows;
            }
        }

        private readonly Dictionary<string, ConstructionSnapshot> constructionSnapshots = new(StringComparer.Ordinal);
        private HashSet<string>? variantClassIds;

        /// <summary>Every run of a sparse root's constructor, as opposed to a restored snapshot.</summary>
        internal int VirtualConstructionCount
        {
            get;
            private set;
        }

        private bool HasVariants(string? classId)
        {
            if (classId is null || data.variants is not { Count: > 0 } variants)
                return false;
            if (variantClassIds is null)
            {
                variantClassIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (VariantRecord record in variants.Values)
                    variantClassIds.Add(record.classId);
            }
            return variantClassIds.Contains(classId);
        }

        /// <summary>
        /// The root's construction output as temporary rows, restored from
        /// its snapshot when its recipe is unchanged; otherwise null.
        /// </summary>
        private NeoGeneratedTypesSupport.RuntimeConstructedClassValue? RestoreConstruction(ObjectMemberValue root)
        {
            if (!constructionSnapshots.TryGetValue(root.id, out ConstructionSnapshot? snapshot)
                || ChangesRecipe(snapshot.Recipe, root))
                return null;
            var plan = new NeoWritePlan(this);
            foreach (MemberValue row in snapshot.Rows)
                plan.Set(NeoValueOwnership.Session, CloneRowForWrite(row));
            plan.Commit();
            if (ResolveValueRow(snapshot.Root.id) is not ObjectMemberValue restored)
                throw new InvalidOperationException($"Construction snapshot for '{root.id}' lost its root row.");
            return new NeoGeneratedTypesSupport.RuntimeConstructedClassValue(restored, snapshot.Member);
        }

        /// <summary>Keeps the temporary graph a construction just produced, for a class with variants.</summary>
        private ConstructionSnapshot? TakeConstructionSnapshot(
            ObjectMemberValue root, NeoGeneratedTypesSupport.RuntimeConstructedClassValue constructed)
        {
            if (!HasVariants(root.classId) || replayAllocationScope is not { } scope)
                return null;
            var rows = new List<MemberValue>(scope.Ids.Count);
            foreach (string id in scope.Ids)
            {
                MemberValue? row = candidateReplay is not null
                    ? candidateReplay.Allocations.GetValueOrDefault(id)
                    : sessionData.values.GetValueOrDefault(id);
                if (row is not null)
                    rows.Add(CloneRowForWrite(row));
            }
            return new ConstructionSnapshot((ObjectMemberValue)CloneRowForWrite(root), constructed.value, constructed.member, rows);
        }

        private void InstallConstructionSnapshot(string rootId, ConstructionSnapshot? snapshot)
        {
            if (snapshot is null)
                constructionSnapshots.Remove(rootId);
            else
                constructionSnapshots[rootId] = snapshot;
        }

        /// <summary>A construction change other than the applied variant, which layers over the output.</summary>
        private static bool ChangesRecipe(ObjectMemberValue before, ObjectMemberValue after) =>
            after.classId != before.classId
            || after.instanceVariantId != before.instanceVariantId
            || after.instanceVariantRowValueId != before.instanceVariantRowValueId
            || after.instanceConstructorId != before.instanceConstructorId
            || (after.value is null) != (before.value is null)
            || !NeoSemanticJson.MapsEqual(after.constructorArgs, before.constructorArgs);
    }
}
