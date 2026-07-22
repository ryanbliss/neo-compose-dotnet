// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Thrown when walking an inheritance chain (via
    /// <see cref="NeoSchemaClass.extendsClassId"/>) revisits a class that has
    /// already been seen — i.e., the chain forms a cycle. Mirrors the
    /// TS-side <c>CircularInheritanceError</c>.
    /// </summary>
    public class CircularInheritanceError : Exception
    {
        public string nodeId { get; }
        public IReadOnlyList<string> chain { get; }

        public CircularInheritanceError(string nodeId, IReadOnlyList<string> chain)
            : base($"Circular inheritance detected at \"{nodeId}\"; chain: {string.Join(" -> ", chain)}")
        {
            this.nodeId = nodeId;
            this.chain = chain;
        }
    }

    /// <summary>
    /// One entry of a merged schema: which key, which member id
    /// resolves it, and which class in the chain owns the entry. Mirrors
    /// the TS-side <c>IMergedSchemaEntry</c>.
    /// </summary>
    public class MergedSchemaEntry
    {
        public string schemaKey { get; }
        public string memberId { get; }
        public string ownerClassId { get; }

        public MergedSchemaEntry(string schemaKey, string memberId, string ownerClassId)
        {
            this.schemaKey = schemaKey;
            this.memberId = memberId;
            this.ownerClassId = ownerClassId;
        }
    }

    /// <summary>
    /// Pair returned by <see cref="NeoSchemaClassInheritance.FindSchemaPlacement"/>:
    /// which Class's schema lists the member, and the schema key
    /// it's listed under. Mirrors TS-side <c>ISchemaPlacement</c>.
    /// </summary>
    public class SchemaPlacement
    {
        public NeoSchemaClass ownerClass { get; }
        public string schemaKey { get; }

        public SchemaPlacement(NeoSchemaClass ownerClass, string schemaKey)
        {
            this.ownerClass = ownerClass;
            this.schemaKey = schemaKey;
        }
    }

    /// <summary>
    /// Static helpers for resolving Class inheritance chains and
    /// merging their schemas. Mirrors the TS-side
    /// <c>resolveInheritanceChain</c> / <c>mergeSchemas</c> helpers in
    /// <c>src/models/classes/inheritance.ts</c>.
    /// </summary>
    public static class NeoSchemaClassInheritance
    {
        /// <summary>
        /// Walks <see cref="NeoSchemaClass.extendsClassId"/> from
        /// <paramref name="classId"/> upward and returns the chain
        /// **child-first** — <c>chain[0]</c> is the requested class and
        /// the final entry is the root ancestor. Stops when an ancestor
        /// can't be resolved (lookup returns null). Throws
        /// <see cref="CircularInheritanceError"/> on cycle.
        /// </summary>
        public static IList<NeoSchemaClass> ResolveChain(
            string classId,
            Func<string, NeoSchemaClass?> lookup)
        {
            List<NeoSchemaClass> chain = new();
            HashSet<string> visited = new();
            NeoSchemaClass? current = lookup(classId);
            while (current is not null)
            {
                if (visited.Contains(current.id))
                {
                    List<string> chainIds = new();
                    foreach (var c in chain) chainIds.Add(c.id);
                    throw new CircularInheritanceError(current.id, chainIds);
                }
                visited.Add(current.id);
                chain.Add(current);
                if (string.IsNullOrEmpty(current.extendsClassId)) break;
                current = lookup(current.extendsClassId!);
            }
            return chain;
        }

        /// <summary>
        /// Merge a child-first chain (as returned by
        /// <see cref="ResolveChain"/>) into a base-first ordered list of
        /// <see cref="MergedSchemaEntry"/>. Later entries (more derived)
        /// replace earlier entries at the same key but do not reorder
        /// the key — so root-ancestor keys come first, then each
        /// descendant's new keys in insertion order.
        /// </summary>
        public static IList<MergedSchemaEntry> MergeSchemas(IList<NeoSchemaClass> chain)
        {
            // Walk base-first so child overrides naturally win.
            List<NeoSchemaClass> baseFirst = new(chain);
            baseFirst.Reverse();

            Dictionary<string, MergedSchemaEntry> map = new();
            List<string> order = new();
            foreach (var schemaClass in baseFirst)
            {
                if (schemaClass.schema is null) continue;
                foreach (var kvp in schemaClass.schema)
                {
                    if (!map.ContainsKey(kvp.Key)) order.Add(kvp.Key);
                    map[kvp.Key] = new MergedSchemaEntry(
                        schemaKey: kvp.Key,
                        memberId: kvp.Value,
                        ownerClassId: schemaClass.id);
                }
            }

            List<MergedSchemaEntry> result = new(order.Count);
            foreach (var key in order)
            {
                result.Add(map[key]);
            }
            return result;
        }

        /// <summary>
        /// Projects a merged Class schema to the fields owned by each
        /// value instance. Class-owned (<see cref="Member.isStatic"/>)
        /// declarations are separate roots and must never be materialized or
        /// traversed through an instance record.
        /// </summary>
        public static IList<MergedSchemaEntry> MergeInstanceSurfaceSchema(
            IList<NeoSchemaClass> chain,
            Func<string, Member?> memberLookup)
        {
            IList<MergedSchemaEntry> merged = MergeSchemas(chain);
            List<MergedSchemaEntry> result = new(merged.Count);
            foreach (MergedSchemaEntry entry in merged)
            {
                Member? member = memberLookup(entry.memberId);
                if (member?.isStatic == true) continue;
                result.Add(entry);
            }
            return result;
        }

        /// <summary>
        /// Backward-compatible name for the complete typed instance surface.
        /// Prefer <see cref="MergeInstanceSurfaceSchema"/> at new call sites.
        /// </summary>
        public static IList<MergedSchemaEntry> MergeInstanceSchema(
            IList<NeoSchemaClass> chain,
            Func<string, Member?> memberLookup) =>
            MergeInstanceSurfaceSchema(chain, memberLookup);

        /// <summary>
        /// Projects the instance surface to fields that own a key/value edge
        /// on every Class row. Declaration-backed read-only fields stay on the
        /// typed surface but are absent from this stored-data projection.
        /// </summary>
        public static IList<MergedSchemaEntry> MergeStoredInstanceSchema(
            IList<NeoSchemaClass> chain,
            Func<string, Member?> memberLookup)
        {
            IList<MergedSchemaEntry> surface =
                MergeInstanceSurfaceSchema(chain, memberLookup);
            List<MergedSchemaEntry> result = new(surface.Count);
            foreach (MergedSchemaEntry entry in surface)
            {
                Member? member = memberLookup(entry.memberId);
                if (member?.isReadOnly == true) continue;
                result.Add(entry);
            }
            return result;
        }

        /// <summary>
        /// Projects the instance surface to declaration-backed read-only
        /// fields. These are visible instance properties but never stored on
        /// an individual Class row.
        /// </summary>
        public static IList<MergedSchemaEntry> MergeReadOnlyMembers(
            IList<NeoSchemaClass> chain,
            Func<string, Member?> memberLookup)
        {
            IList<MergedSchemaEntry> surface =
                MergeInstanceSurfaceSchema(chain, memberLookup);
            List<MergedSchemaEntry> result = new(surface.Count);
            foreach (MergedSchemaEntry entry in surface)
            {
                Member? member = memberLookup(entry.memberId);
                if (member?.isReadOnly != true) continue;
                result.Add(entry);
            }
            return result;
        }

        /// <summary>
        /// Projects a merged Class schema to inherited class-owned
        /// members. The returned entry retains the declaration's owner class so
        /// storage defaults and generated APIs remain anchored to the declaring
        /// class when accessed through a descendant.
        /// </summary>
        public static IList<MergedSchemaEntry> MergeStaticMembers(
            IList<NeoSchemaClass> chain,
            Func<string, Member?> memberLookup)
        {
            IList<MergedSchemaEntry> merged = MergeSchemas(chain);
            List<MergedSchemaEntry> result = new(merged.Count);
            foreach (MergedSchemaEntry entry in merged)
            {
                Member? member = memberLookup(entry.memberId);
                if (member?.isStatic != true) continue;
                result.Add(entry);
            }
            return result;
        }

        /// <summary>
        /// Scans <paramref name="classes"/> for the first Class whose
        /// schema maps any key to <paramref name="memberId"/>. Returns
        /// the owning class and the key it's listed under, or <c>null</c>
        /// when no schema references the member. Mirrors TS-side
        /// <c>findSchemaPlacement</c>.
        ///
        /// <para>Single source of truth for "which Class's schema
        /// lists this member, and under what key?" — used by the
        /// NSProperty evaluator to recover the NSProperty schema key a callGetter
        /// pointer was bound under so it can re-resolve the dispatch
        /// target via the runtime row's classId merged schema.</para>
        ///
        /// <para>Returns the *first* match. A member id appearing in
        /// multiple classes' schemas would be a project-data inconsistency
        /// (each schema entry owns a distinct member id), so no
        /// disambiguation is attempted.</para>
        /// </summary>
        public static SchemaPlacement? FindSchemaPlacement(
            string memberId,
            IEnumerable<NeoSchemaClass> classes)
        {
            foreach (var schemaClass in classes)
            {
                if (schemaClass.schema is null) continue;
                foreach (var kvp in schemaClass.schema)
                {
                    if (kvp.Value == memberId)
                    {
                        return new SchemaPlacement(schemaClass, kvp.Key);
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Walks the <c>extendsMemberId</c> chain from
        /// <paramref name="startId"/>, returning the first non-null value
        /// <paramref name="picker"/> produces along the way. Mirrors
        /// TS-side <c>walkExtendsMemberChain</c>.
        ///
        /// <para>Stops when:
        /// <list type="bullet">
        ///   <item><description><paramref name="picker"/> returns a
        ///   non-null value (returns it),</description></item>
        ///   <item><description>the cursor is missing or has no
        ///   <c>extendsMemberId</c> (returns
        ///   <c>default(T)</c>),</description></item>
        ///   <item><description>a non-matching kind appears in the chain
        ///   when <paramref name="requireKind"/> is set (returns
        ///   <c>default(T)</c>),</description></item>
        ///   <item><description>the iteration cap
        ///   <paramref name="maxHops"/> (default 16) is exceeded —
        ///   defends against accidental cycles.</description></item>
        /// </list></para>
        ///
        /// <para>Used by the NSProperty evaluator to find an inherited
        /// compiled <c>getter</c> or <c>returnTypeInfo</c> on
        /// override-form NSProperty rows that don't carry their own.</para>
        /// </summary>
        public static T? WalkExtendsMemberChain<T>(
            string startId,
            Func<string, Member?> memberLookup,
            Func<Member, T?> picker,
            MemberKind? requireKind = null,
            int maxHops = 16)
            where T : class
        {
            var cursor = memberLookup(startId);
            for (int i = 0; cursor is not null && i < maxHops; i++)
            {
                if (requireKind.HasValue && cursor.kind != requireKind.Value)
                {
                    return null;
                }
                var picked = picker(cursor);
                if (picked is not null) return picked;
                if (string.IsNullOrEmpty(cursor.extendsMemberId)) return null;
                cursor = memberLookup(cursor.extendsMemberId!);
            }
            return null;
        }
    }
}
