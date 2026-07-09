// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Thrown when walking an inheritance chain (via
    /// <see cref="CustomType.extendsTypeId"/>) revisits a type that has
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
    /// One entry of a merged schema: which key, which attribute id
    /// resolves it, and which type in the chain owns the entry. Mirrors
    /// the TS-side <c>IMergedSchemaEntry</c>.
    /// </summary>
    public class MergedSchemaEntry
    {
        public string schemaKey { get; }
        public string attributeId { get; }
        public string ownerTypeId { get; }

        public MergedSchemaEntry(string schemaKey, string attributeId, string ownerTypeId)
        {
            this.schemaKey = schemaKey;
            this.attributeId = attributeId;
            this.ownerTypeId = ownerTypeId;
        }
    }

    /// <summary>
    /// Pair returned by <see cref="CustomTypeInheritance.FindSchemaPlacement"/>:
    /// which Custom type's schema lists the attribute, and the schema key
    /// it's listed under. Mirrors TS-side <c>ISchemaPlacement</c>.
    /// </summary>
    public class SchemaPlacement
    {
        public CustomType ownerType { get; }
        public string schemaKey { get; }

        public SchemaPlacement(CustomType ownerType, string schemaKey)
        {
            this.ownerType = ownerType;
            this.schemaKey = schemaKey;
        }
    }

    /// <summary>
    /// Static helpers for resolving Custom-type inheritance chains and
    /// merging their schemas. Mirrors the TS-side
    /// <c>resolveInheritanceChain</c> / <c>mergeSchemas</c> helpers in
    /// <c>src/models/custom-types/inheritance.ts</c>.
    /// </summary>
    public static class CustomTypeInheritance
    {
        /// <summary>
        /// Walks <see cref="CustomType.extendsTypeId"/> from
        /// <paramref name="typeId"/> upward and returns the chain
        /// **child-first** — <c>chain[0]</c> is the requested type and
        /// the final entry is the root ancestor. Stops when an ancestor
        /// can't be resolved (lookup returns null). Throws
        /// <see cref="CircularInheritanceError"/> on cycle.
        /// </summary>
        public static IList<CustomType> ResolveChain(
            string typeId,
            Func<string, CustomType?> lookup)
        {
            List<CustomType> chain = new();
            HashSet<string> visited = new();
            CustomType? current = lookup(typeId);
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
                if (string.IsNullOrEmpty(current.extendsTypeId)) break;
                current = lookup(current.extendsTypeId!);
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
        public static IList<MergedSchemaEntry> MergeSchemas(IList<CustomType> chain)
        {
            // Walk base-first so child overrides naturally win.
            List<CustomType> baseFirst = new(chain);
            baseFirst.Reverse();

            Dictionary<string, MergedSchemaEntry> map = new();
            List<string> order = new();
            foreach (var type in baseFirst)
            {
                if (type.schema is null) continue;
                foreach (var kvp in type.schema)
                {
                    if (!map.ContainsKey(kvp.Key)) order.Add(kvp.Key);
                    map[kvp.Key] = new MergedSchemaEntry(
                        schemaKey: kvp.Key,
                        attributeId: kvp.Value,
                        ownerTypeId: type.id);
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
        /// Scans <paramref name="types"/> for the first Custom type whose
        /// schema maps any key to <paramref name="attributeId"/>. Returns
        /// the owning type and the key it's listed under, or <c>null</c>
        /// when no schema references the attribute. Mirrors TS-side
        /// <c>findSchemaPlacement</c>.
        ///
        /// <para>Single source of truth for "which Custom type's schema
        /// lists this attribute, and under what key?" — used by the
        /// NSProperty evaluator to recover the NSProperty schema key a callGetter
        /// pointer was bound under so it can re-resolve the dispatch
        /// target via the runtime row's typeId merged schema.</para>
        ///
        /// <para>Returns the *first* match. An attribute id appearing in
        /// multiple types' schemas would be a project-data inconsistency
        /// (each schema entry owns a distinct attribute id), so no
        /// disambiguation is attempted.</para>
        /// </summary>
        public static SchemaPlacement? FindSchemaPlacement(
            string attributeId,
            IEnumerable<CustomType> types)
        {
            foreach (var t in types)
            {
                if (t.schema is null) continue;
                foreach (var kvp in t.schema)
                {
                    if (kvp.Value == attributeId)
                    {
                        return new SchemaPlacement(t, kvp.Key);
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Walks the <c>extendsAttributeId</c> chain from
        /// <paramref name="startId"/>, returning the first non-null value
        /// <paramref name="picker"/> produces along the way. Mirrors
        /// TS-side <c>walkExtendsAttributeChain</c>.
        ///
        /// <para>Stops when:
        /// <list type="bullet">
        ///   <item><description><paramref name="picker"/> returns a
        ///   non-null value (returns it),</description></item>
        ///   <item><description>the cursor is missing or has no
        ///   <c>extendsAttributeId</c> (returns
        ///   <c>default(T)</c>),</description></item>
        ///   <item><description>a non-matching type appears in the chain
        ///   when <paramref name="requireType"/> is set (returns
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
        public static T? WalkExtendsAttributeChain<T>(
            string startId,
            Func<string, Attribute?> attributeLookup,
            Func<Attribute, T?> picker,
            AttributeType? requireType = null,
            int maxHops = 16)
            where T : class
        {
            var cursor = attributeLookup(startId);
            for (int i = 0; cursor is not null && i < maxHops; i++)
            {
                if (requireType.HasValue && cursor.type != requireType.Value)
                {
                    return null;
                }
                var picked = picker(cursor);
                if (picked is not null) return picked;
                if (string.IsNullOrEmpty(cursor.extendsAttributeId)) return null;
                cursor = attributeLookup(cursor.extendsAttributeId!);
            }
            return null;
        }
    }
}
