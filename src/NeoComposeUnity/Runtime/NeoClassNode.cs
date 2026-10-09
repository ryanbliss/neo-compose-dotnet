// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// One class of the exported schema, resolved once: its inheritance chain,
    /// its merged instance members with their member records, and each member
    /// by schema key. Readers hold the node and its entries instead of looking
    /// class and member ids up again. Built on first use per class id and
    /// dropped with the client's other schema caches.
    /// </summary>
    internal sealed class NeoClassNode
    {
        private readonly Dictionary<string, MergedSchemaEntry> surfaceByKey;

        internal NeoClassNode(
            string id,
            IList<NeoSchemaClass> chain,
            IReadOnlyDictionary<string, Member> members,
            NeoLifecycleHooks hooks)
        {
            Id = id;
            Chain = chain;
            IList<MergedSchemaEntry> merged = NeoSchemaClassInheritance.MergeSchemas(chain);
            var surface = new List<MergedSchemaEntry>(merged.Count);
            surfaceByKey = new Dictionary<string, MergedSchemaEntry>(
                merged.Count,
                System.StringComparer.Ordinal);
            foreach (MergedSchemaEntry entry in merged)
            {
                members.TryGetValue(entry.memberId, out Member? member);
                entry.member = member;
                if (!NeoSchemaClassInheritance.IsInstanceMember(member))
                    continue;
                surface.Add(entry);
                surfaceByKey.TryAdd(entry.schemaKey, entry);
            }
            Surface = surface;
            Effects = CollectEffects(surface);
            if (hooks != NeoLifecycleHooks.None)
                hookMembers = CollectHooks(ref hooks);
            Hooks = hooks;
            Tracked = Effects.Length != 0 || (hooks & NeoLifecycleHooks.Data) != 0;
        }

        // The most-derived member of each hook, by bit position. A bit whose
        // member is not an instance function implements nothing to call.
        private MergedSchemaEntry?[] CollectHooks(ref NeoLifecycleHooks hooks)
        {
            var members = new MergedSchemaEntry?[NeoLifecycleHookTable.Count];
            for (int i = 0; i < NeoLifecycleHookTable.Count; i++)
            {
                var hook = (NeoLifecycleHooks)(1u << i);
                if ((hooks & hook) == 0)
                    continue;
                if (SurfaceMember(NeoLifecycleHookTable.MemberName(i)) is { member: NSFunctionMember } entry)
                    members[i] = entry;
                else
                    hooks &= ~hook;
            }
            return members;
        }

        // A surface entry is the class's most-derived record of its key, so
        // its resolved effect is this class's (P97 §1.4).
        private static MergedSchemaEntry[] CollectEffects(List<MergedSchemaEntry> surface)
        {
            List<MergedSchemaEntry>? effects = null;
            foreach (MergedSchemaEntry entry in surface)
            {
                if (entry.member is NSFunctionMember { Effect: NeoEffectKind.Auto })
                    (effects ??= new List<MergedSchemaEntry>()).Add(entry);
            }
            return effects?.ToArray() ?? System.Array.Empty<MergedSchemaEntry>();
        }

        internal string Id
        {
            get;
        }

        /// <summary>False once the schema caches dropped the node; a holder resolves the class again.</summary>
        internal bool live = true;

        /// <summary>Child-first: the class itself, then each ancestor. Empty when the id names no class.</summary>
        internal IList<NeoSchemaClass> Chain
        {
            get;
        }

        /// <summary>Whether <paramref name="classId"/> is this class or one of its ancestors.</summary>
        internal bool Extends(string classId)
        {
            for (int i = 0; i < Chain.Count; i++)
            {
                if (Chain[i].id == classId)
                    return true;
            }
            return false;
        }

        /// <summary>Every instance member, static declarations excluded.</summary>
        internal IList<MergedSchemaEntry> Surface
        {
            get;
        }

        /// <summary>The functions the runtime runs for every live instance (P97).</summary>
        internal MergedSchemaEntry[] Effects
        {
            get;
        }

        /// <summary>The lifecycle hooks the class implements (P98 §4.1).</summary>
        internal NeoLifecycleHooks Hooks
        {
            get;
        }

        private readonly MergedSchemaEntry?[]? hookMembers;

        /// <summary>The member that implements one of <see cref="Hooks"/>.</summary>
        internal MergedSchemaEntry HookMember(NeoLifecycleHooks hook) =>
            hookMembers![NeoLifecycleHookTable.IndexOf(hook)]!;

        /// <summary>Whether the runtime tracks the class's live instances: it has effects, OnLoad or OnUnload.</summary>
        internal bool Tracked
        {
            get;
        }

        internal MergedSchemaEntry? SurfaceMember(string schemaKey) =>
            surfaceByKey.TryGetValue(schemaKey, out MergedSchemaEntry entry) ? entry : null;
    }
}
