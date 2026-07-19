// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using Member = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Terminal resolution of one generic param at a concrete class
    /// (mirrors the TS-side <c>TGenericEnvEntry</c> in
    /// <c>src/models/classes/generics.ts</c>):
    /// <list type="bullet">
    ///   <item><description><b>bound</b> — the param resolves to a concrete
    ///   member record (the type argument).</description></item>
    ///   <item><description><b>unbound</b> — still open at the resolved
    ///   class; <see cref="unboundParamId"/> is the deepest forward target
    ///   (the param a further descendant must bind).</description></item>
    /// </list>
    /// </summary>
    public sealed class NeoGenericEnvEntry
    {
        private NeoGenericEnvEntry(string? memberId, string? unboundParamId)
        {
            this.memberId = memberId;
            this.unboundParamId = unboundParamId;
        }

        public static NeoGenericEnvEntry Bound(string memberId) =>
            new(memberId, null);

        public static NeoGenericEnvEntry Unbound(string paramId) =>
            new(null, paramId);

        public bool IsBound => memberId is not null;

        /// <summary>Terminal binding member id; set iff <see cref="IsBound"/>.</summary>
        public string? memberId { get; }

        /// <summary>Deepest forward target; set iff not <see cref="IsBound"/>.</summary>
        public string? unboundParamId { get; }
    }

    /// <summary>Admission result of <see cref="NeoGenericResolution.ConstructedSlotAccepts"/>.</summary>
    public sealed class NeoSlotAdmission
    {
        private NeoSlotAdmission(bool ok, string? reason)
        {
            this.ok = ok;
            this.reason = reason;
        }

        public static readonly NeoSlotAdmission Ok = new(true, null);

        public static NeoSlotAdmission Reject(string reason) => new(false, reason);

        public bool ok { get; }

        /// <summary>Human-readable rejection reason; set iff not <see cref="ok"/>.</summary>
        public string? reason { get; }
    }

    /// <summary>
    /// Canonical projection signature of a type argument
    /// (specs/class-generics.md Decision 8) — the fields that change
    /// the projected C#/runtime type, and nothing else. Validation
    /// refinements (min/max, decimalPoints, templateId, column settings)
    /// are deliberately excluded, exactly as C# members like
    /// <c>[Range]</c> don't change a member's type. <see cref="required"/>
    /// IS included: nullability is part of the type (<c>float</c> vs
    /// <c>float?</c>). Mirrors the TS-side <c>TTypeArgumentSignature</c>.
    /// </summary>
    public sealed class NeoClassArgumentSignature
    {
        public MemberKind type;
        public bool required;

        /// <summary>String only. Normalized: absent means localizable.</summary>
        public bool? localizable;

        /// <summary>Enum only.</summary>
        public string? enumId;

        /// <summary>Enum only.</summary>
        public bool? multiselect;

        /// <summary>Class only.</summary>
        public string? classId;

        /// <summary>Class only — recursive argument signatures by target param id.</summary>
        public Dictionary<string, NeoClassArgumentSignature>? args;

        /// <summary>List only — "ordered" or "unordered" (absent normalizes to ordered).</summary>
        public string? listKind;

        /// <summary>Dictionary only — "string" or "enum" (absent normalizes to string).</summary>
        public string? keyKind;

        /// <summary>Dictionary only — set iff <see cref="keyKind"/> is "enum".</summary>
        public string? keyEnumId;

        /// <summary>List / Dictionary only — recursive entry signature.</summary>
        public NeoClassArgumentSignature? entry;
    }

    /// <summary>
    /// Generic-parameter resolution for the dotnet runtime
    /// (specs/class-generics.md §9). Mirrors the web-side single
    /// source of truth, <c>src/models/classes/generics.ts</c> — keep
    /// both sides in sync:
    /// <list type="bullet">
    ///   <item><description>environment resolution walking the
    ///   <c>extendsClassId</c> chain child-first and following forward
    ///   chains downward (<see cref="ResolveEnv(NeoClient, string)"/>);</description></item>
    ///   <item><description>the Decision-10 substitution field partition
    ///   (<see cref="SubstituteMember"/>);</description></item>
    ///   <item><description>the Decision-9 <c>genericBindings</c> value
    ///   stamp (<see cref="ComputeGenericBindingsStamp"/> /
    ///   <see cref="EnvFromStamp"/> / <see cref="StampGenericBindings"/>);</description></item>
    ///   <item><description>invariant class-argument signatures + admission
    ///   (<see cref="SignatureOfMember"/> /
    ///   <see cref="ConstructedSlotAccepts"/>).</description></item>
    /// </list>
    /// One deliberate deviation from the web module: the SDK operates on
    /// wire records directly rather than resolving <c>extendsMemberId</c>
    /// override chains first — the export ships full records and every
    /// other runtime consumer (node construction, NSProperty getter evaluation)
    /// already reads them the same way.
    /// </summary>
    public static class NeoGenericResolution
    {
        /// <summary>Shared empty environment for non-generic contexts.</summary>
        public static readonly IReadOnlyDictionary<string, NeoGenericEnvEntry> EmptyEnv =
            new Dictionary<string, NeoGenericEnvEntry>();

        // ------------------------------------------------------------------
        // Environment resolution.
        // ------------------------------------------------------------------

        /// <summary>
        /// Resolves every generic param declared anywhere in
        /// <paramref name="classId"/>'s inheritance chain to its terminal
        /// entry at <paramref name="classId"/> — mirroring the web's
        /// <c>resolveGenericEnv</c>. Tolerant of incomplete data (mid-edit
        /// drafts): a missing binding resolves to unbound rather than
        /// throwing; write-path validation lives web-side.
        /// </summary>
        public static IReadOnlyDictionary<string, NeoGenericEnvEntry> ResolveEnv(
            NeoClient client,
            string classId)
        {
            var chain = NeoSchemaClassInheritance.ResolveChain(
                classId,
                id => client.TryGetClass(id, out NeoSchemaClass? match) ? match : null);
            return ResolveEnv(chain);
        }

        /// <summary>
        /// Chain-based overload for callers that already resolved the
        /// child-first inheritance chain (e.g.
        /// <see cref="NeoMemberClass"/>'s type context).
        /// </summary>
        public static IReadOnlyDictionary<string, NeoGenericEnvEntry> ResolveEnv(
            IList<NeoSchemaClass> chain)
        {
            Dictionary<string, NeoGenericEnvEntry>? env = null;
            for (int i = 0; i < chain.Count; i++)
            {
                var declared = chain[i].genericParams;
                if (declared is null) continue;
                foreach (var param in declared)
                {
                    env ??= new Dictionary<string, NeoGenericEnvEntry>();
                    env[param.id] = ResolveParamAtChainIndex(chain, i, param.id);
                }
            }
            return env ?? EmptyEnv;
        }

        /// <summary>
        /// Walks one param (declared by <c>chain[declarerIndex]</c>) down
        /// toward <c>chain[0]</c>, following forwards, and returns its
        /// terminal entry — the exact mirror of the web's
        /// <c>resolveParamAtChainIndex</c>. Forward chains are acyclic by
        /// construction (child → parent only), so the walk is bounded by
        /// chain length.
        /// </summary>
        private static NeoGenericEnvEntry ResolveParamAtChainIndex(
            IList<NeoSchemaClass> chain,
            int declarerIndex,
            string paramId)
        {
            string cursor = paramId;
            for (int level = declarerIndex; level > 0; level--)
            {
                var child = chain[level - 1];
                GenericBinding? binding = null;
                if (child.extendsGenericBindings is not null)
                {
                    child.extendsGenericBindings.TryGetValue(cursor, out binding);
                }
                if (binding is null)
                {
                    return NeoGenericEnvEntry.Unbound(cursor);
                }
                if (binding.kind == NeoGenericBindingKinds.Member)
                {
                    return NeoGenericEnvEntry.Bound(binding.memberId!);
                }
                cursor = binding.genericParamId!;
            }
            return NeoGenericEnvEntry.Unbound(cursor);
        }

        /// <summary>
        /// True when every generic param in scope of
        /// <paramref name="classId"/>'s chain is bound — i.e., the class is
        /// instantiable generics-wise. Classes with no generics anywhere in
        /// their chain are trivially closed.
        /// </summary>
        public static bool IsClosedClass(NeoClient client, string classId)
        {
            foreach (var entry in ResolveEnv(client, classId).Values)
            {
                if (!entry.IsBound) return false;
            }
            return true;
        }

        /// <summary>
        /// Binding environment for descending into a Class INSTANCE through
        /// its slot member (specs/class-generics.md §4.1) — the
        /// mirror of the web's <c>resolveInstanceEnv</c>. The chain env of
        /// the row's effective class resolves params bound by named subclasses;
        /// the slot's constructed <c>classArguments</c> overlay the
        /// params the chain leaves unbound — a constructed slot
        /// (<c>GenericTest&lt;Color&gt;</c>) closes the open class AT THE
        /// USAGE SITE without a named subclass, so its arguments are the
        /// terminal bindings for exactly those params. Chain bindings win
        /// where both exist (a named closed subclass picked into a
        /// constructed slot — admission guarantees signature equality, so
        /// they agree).
        ///
        /// Generic-kind arguments (still-open forwards) stay unbound:
        /// enclosing contexts substitute the slot member before descent,
        /// so a concrete instance never reaches here with a forward —
        /// reaching an unbound param during substitution remains the "open
        /// classes are not instantiable" error.
        ///
        /// Identity with <see cref="ResolveEnv(IList{NeoSchemaClass})"/> for
        /// slots without arguments, and the empty env for concrete classes —
        /// concrete documents pay nothing.
        /// </summary>
        public static IReadOnlyDictionary<string, NeoGenericEnvEntry> ResolveInstanceEnv(
            NeoClient client,
            string effectiveClassId,
            IReadOnlyDictionary<string, GenericBinding>? classArguments)
        {
            var chain = NeoSchemaClassInheritance.ResolveChain(
                effectiveClassId,
                id => client.TryGetClass(id, out NeoSchemaClass? match) ? match : null);
            return ResolveInstanceEnv(chain, classArguments);
        }

        /// <summary>
        /// Chain-based overload of
        /// <see cref="ResolveInstanceEnv(NeoClient, string, IReadOnlyDictionary{string, GenericBinding})"/>
        /// for callers that already resolved the child-first inheritance
        /// chain (e.g. <see cref="NeoMemberClass"/>'s type context).
        /// </summary>
        public static IReadOnlyDictionary<string, NeoGenericEnvEntry> ResolveInstanceEnv(
            IList<NeoSchemaClass> chain,
            IReadOnlyDictionary<string, GenericBinding>? classArguments)
        {
            var env = ResolveEnv(chain);
            if (classArguments is null || classArguments.Count == 0)
            {
                return env;
            }
            // Copy before overlaying — ResolveEnv may return the shared
            // EmptyEnv instance, which must never be mutated.
            var overlaid = new Dictionary<string, NeoGenericEnvEntry>(env.Count + classArguments.Count);
            foreach (var pair in env)
            {
                overlaid[pair.Key] = pair.Value;
            }
            foreach (var pair in classArguments)
            {
                if (pair.Value.IsForward) continue;
                if (overlaid.TryGetValue(pair.Key, out NeoGenericEnvEntry existing)
                    && existing.IsBound)
                {
                    continue;
                }
                overlaid[pair.Key] = NeoGenericEnvEntry.Bound(pair.Value.memberId!);
            }
            return overlaid;
        }

        /// <summary>
        /// The first unbound param in <paramref name="env"/>, or <c>null</c>
        /// when every param is bound — the instantiability check for a
        /// Class slot (closed named type OR fully constructed slot).
        /// Callers use the returned id for a precise error. Mirrors the
        /// web's <c>firstUnboundParamId</c>.
        /// </summary>
        public static string? FirstUnboundParamId(
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            foreach (var entry in env.Values)
            {
                if (!entry.IsBound) return entry.unboundParamId;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Stamps (spec Decision 9).
        // ------------------------------------------------------------------

        /// <summary>
        /// Rebuilds a binding environment from a collection value row's
        /// <see cref="MemberValue.genericBindings"/> stamp — the
        /// read-side inverse of <see cref="ComputeGenericBindingsStamp"/>.
        /// </summary>
        public static IReadOnlyDictionary<string, NeoGenericEnvEntry> EnvFromStamp(
            IReadOnlyDictionary<string, string>? stamp)
        {
            if (stamp is null || stamp.Count == 0) return EmptyEnv;
            var env = new Dictionary<string, NeoGenericEnvEntry>(stamp.Count);
            foreach (var pair in stamp)
            {
                env[pair.Key] = NeoGenericEnvEntry.Bound(pair.Value);
            }
            return env;
        }

        /// <summary>
        /// Every generic param referenced anywhere in
        /// <paramref name="member"/>'s subtree: its own
        /// <c>genericParamId</c>, generic-kind constructed arguments, and
        /// recursion through collection entry members and member-kind
        /// arguments (which may themselves be partially generic, e.g.
        /// <c>C&lt;T&gt; : B&lt;List&lt;T&gt;&gt;</c>). Mirrors the web's
        /// <c>referencedGenericParams</c>.
        /// </summary>
        public static HashSet<string> ReferencedGenericParams(
            NeoClient client,
            Member member)
        {
            var result = new HashSet<string>();
            CollectReferencedGenericParams(client, member, new HashSet<string>(), result);
            return result;
        }

        private static void CollectReferencedGenericParams(
            NeoClient client,
            Member member,
            HashSet<string> seen,
            HashSet<string> result)
        {
            if (!string.IsNullOrEmpty(member.id) && !seen.Add(member.id))
            {
                return;
            }
            if (member is GenericMember generic)
            {
                result.Add(generic.genericParamId);
                return;
            }
            if (member is ClassMember classMember)
            {
                if (classMember.classArguments is null) return;
                foreach (var arg in classMember.classArguments.Values)
                {
                    if (arg.IsForward)
                    {
                        result.Add(arg.genericParamId!);
                        continue;
                    }
                    if (!client.TryGetMember(arg.memberId!, out Member? argumentMember)) continue;
                    CollectReferencedGenericParams(client, argumentMember, seen, result);
                }
                return;
            }
            string? entryMemberId = member switch
            {
                ListMember list => list.entryMemberId,
                DictionaryMember dictionary => dictionary.entryMemberId,
                _ => null,
            };
            if (entryMemberId is null) return;
            if (!client.TryGetMember(entryMemberId, out Member? entryMember)) return;
            CollectReferencedGenericParams(client, entryMember, seen, result);
        }

        /// <summary>
        /// Computes the <c>genericBindings</c> stamp for a collection value
        /// row created from <paramref name="collectionMember"/> under
        /// <paramref name="env"/>: the terminal binding member id for
        /// exactly the params the entry subtree references. Returns
        /// <c>null</c> when the subtree references no params (no stamp).
        /// Throws when a referenced param is unresolvable — unreachable when
        /// web-side write validation holds, since open classes cannot be
        /// instantiated.
        /// </summary>
        public static Dictionary<string, string>? ComputeGenericBindingsStamp(
            NeoClient client,
            Member collectionMember,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            var referenced = ReferencedGenericParams(client, collectionMember);
            if (referenced.Count == 0) return null;
            var stamp = new Dictionary<string, string>(referenced.Count);
            foreach (var paramId in referenced)
            {
                if (!env.TryGetValue(paramId, out NeoGenericEnvEntry entry))
                {
                    throw new System.InvalidOperationException(
                        $"ComputeGenericBindingsStamp: generic param '{paramId}' referenced by member '{collectionMember.id}' ({collectionMember.name}) is not present in the binding environment.");
                }
                if (!entry.IsBound)
                {
                    throw new System.InvalidOperationException(
                        $"ComputeGenericBindingsStamp: generic param '{paramId}' referenced by member '{collectionMember.id}' ({collectionMember.name}) is unbound in the binding environment — values of open generic classes cannot be created.");
                }
                stamp[paramId] = entry.memberId!;
            }
            return stamp;
        }

        /// <summary>
        /// Creation-side convenience: computes and assigns the stamp on a
        /// freshly-minted List/Dictionary <paramref name="row"/>. No-op for
        /// non-collection members, for entry subtrees that reference no
        /// params, and for rows that already carry a stamp (the stamp is
        /// immutable — a clone-on-write shadow keeps the original).
        /// </summary>
        public static void StampGenericBindings(
            NeoClient client,
            Member collectionMember,
            MemberValue row,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (collectionMember is not ListMember
                && collectionMember is not DictionaryMember)
            {
                return;
            }
            if (row.genericBindings is not null) return;
            row.genericBindings = ComputeGenericBindingsStamp(client, collectionMember, env);
        }

        /// <summary>
        /// The binding environment a node's freshly-minted collection rows
        /// are stamped under, resolved from the wrapper tree: an enclosing
        /// Class node supplies its type context's environment; an enclosing
        /// collection node supplies its own row's stamp; a parentless node
        /// has the empty environment. Mirrors the web's "creation always
        /// happens where the env is in hand" rule (spec Decision 9).
        /// </summary>
        public static IReadOnlyDictionary<string, NeoGenericEnvEntry> ResolveContextEnv(
            NeoMember? contextParent)
        {
            if (contextParent is NeoMemberClass classNode)
            {
                return classNode.GenericEnv;
            }
            if (contextParent is NeoMemberList list)
            {
                return EnvFromStamp(list.value?.genericBindings);
            }
            if (contextParent is NeoMemberDictionary dictionary)
            {
                return EnvFromStamp(dictionary.value?.genericBindings);
            }
            return EmptyEnv;
        }

        // ------------------------------------------------------------------
        // Substitution (spec Decision 10).
        // ------------------------------------------------------------------

        /// <summary>
        /// Substitutes generic references in <paramref name="member"/>
        /// through <paramref name="env"/> — the mirror of the web's
        /// <c>substituteMember</c>:
        /// <list type="bullet">
        ///   <item><description><b>Generic slots</b> resolve to their
        ///   binding member. The binding supplies <c>kind</c>, all
        ///   per-kind config, <c>defaultValue</c>, and <c>required</c>
        ///   (nullability is part of the type — one <c>T</c> is one type).
        ///   The slot keeps its identity/placement fields: <c>id</c>,
        ///   <c>name</c>, <c>locked</c>, <c>accessModifierKind</c>,
        ///   <c>isVirtual</c>, <c>isAbstract</c>, <c>storage</c>,
        ///   <c>storageKey</c>.
        ///   Preserving <c>id</c> is what keeps parent-value records, child
        ///   node resolution, and NeoScript pointer IR working with zero
        ///   wire changes. <c>extendsMemberId</c> is stripped.</description></item>
        ///   <item><description><b>Constructed Class slots</b> substitute
        ///   generic-kind arguments to their terminal binding members;
        ///   unbound forwards are kept (open context — downstream consumers
        ///   substitute again once a concrete env exists).</description></item>
        ///   <item><description><b>Collections pass through unchanged</b> —
        ///   entry substitution is lazy at entry sites via the value row's
        ///   <c>genericBindings</c> stamp.</description></item>
        ///   <item><description>Everything else is identity.</description></item>
        /// </list>
        /// </summary>
        public static Member SubstituteMember(
            NeoClient client,
            Member member,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (member is GenericMember generic)
            {
                Member binding = ResolveTerminalBinding(
                    client,
                    generic.genericParamId,
                    generic.name,
                    env);
                Member substituted = binding.ShallowClone();
                substituted.id = generic.id;
                substituted.name = generic.name;
                substituted.locked = generic.locked;
                // Accessibility is slot-owned (specs/member-access-modifiers.md
                // §2) — a binding member's modifier must not change the
                // declaring slot's visibility.
                substituted.accessModifierKind = generic.accessModifierKind;
                substituted.isVirtual = generic.isVirtual;
                substituted.isAbstract = generic.isAbstract;
                // Placement fields are slot-owned. A null declaration means
                // "inherit from placement parent" and must not fall back to
                // the binding's own declaration (bindings are type
                // arguments, not placements).
                substituted.storage = generic.storage;
                substituted.storageKey = generic.storageKey;
                substituted.extendsMemberId = null;
                return substituted;
            }
            if (member is ClassMember classMember)
            {
                var args = classMember.classArguments;
                if (args is null) return member;
                bool changed = false;
                var substitutedArgs = new Dictionary<string, GenericBinding>(args.Count);
                foreach (var pair in args)
                {
                    var arg = pair.Value;
                    if (!arg.IsForward)
                    {
                        substitutedArgs[pair.Key] = arg;
                        continue;
                    }
                    if (!env.TryGetValue(arg.genericParamId!, out NeoGenericEnvEntry entry))
                    {
                        throw new System.InvalidOperationException(
                            $"SubstituteMember: constructed argument for param '{pair.Key}' on member '{classMember.name}' ({classMember.id}) forwards generic param '{arg.genericParamId}', which is not present in the binding environment.");
                    }
                    if (!entry.IsBound)
                    {
                        // Open context (e.g. resolving against the open class
                        // itself): keep the forward — downstream consumers
                        // substitute again once a concrete env exists.
                        substitutedArgs[pair.Key] = arg;
                        continue;
                    }
                    substitutedArgs[pair.Key] = new GenericBinding
                    {
                        kind = NeoGenericBindingKinds.Member,
                        memberId = entry.memberId,
                    };
                    changed = true;
                }
                if (!changed) return member;
                var substitutedClass = (ClassMember)classMember.ShallowClone();
                substitutedClass.classArguments = substitutedArgs;
                return substitutedClass;
            }
            return member;
        }

        /// <summary>
        /// Resolves a param to its terminal binding member through
        /// <paramref name="env"/>, with a distinct error per failure mode.
        /// </summary>
        private static Member ResolveTerminalBinding(
            NeoClient client,
            string genericParamId,
            string slotName,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (!env.TryGetValue(genericParamId, out NeoGenericEnvEntry entry))
            {
                throw new System.InvalidOperationException(
                    $"Cannot substitute generic slot '{slotName}': param '{genericParamId}' is not present in the binding environment (is the member referencing a param outside its placement scope?).");
            }
            if (!entry.IsBound)
            {
                throw new System.InvalidOperationException(
                    $"Cannot substitute generic slot '{slotName}': param '{genericParamId}' is unbound in the binding environment — only closed classes are instantiable.");
            }
            if (!client.TryGetMember(entry.memberId!, out Member? bindingMember))
            {
                throw new System.InvalidOperationException(
                    $"Cannot substitute generic slot '{slotName}': binding member '{entry.memberId}' for param '{genericParamId}' does not exist in the document.");
            }
            return bindingMember;
        }

        // ------------------------------------------------------------------
        // Type-argument signatures (spec Decision 8).
        // ------------------------------------------------------------------

        /// <summary>
        /// Computes the projection signature of a member under
        /// <paramref name="env"/>. Generic references resolve through the
        /// environment first, so a partially-generic binding
        /// (<c>C&lt;T&gt; : B&lt;List&lt;T&gt;&gt;</c> seen from
        /// <c>D : C&lt;Float&gt;</c>) signatures as its fully-concrete form.
        /// Mirrors the web's <c>typeArgumentSignatureOfMember</c>.
        /// </summary>
        public static NeoClassArgumentSignature SignatureOfMember(
            NeoClient client,
            Member member,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            HashSet<string>? seen = null)
        {
            seen ??= new HashSet<string>();
            if (!string.IsNullOrEmpty(member.id) && !seen.Add(member.id))
            {
                throw new System.InvalidOperationException(
                    $"SignatureOfMember revisited member '{member.id}' — constructed arguments form a reference cycle.");
            }
            if (member is GenericMember generic)
            {
                Member binding = ResolveTerminalBinding(
                    client,
                    generic.genericParamId,
                    generic.name,
                    env);
                return SignatureOfMember(client, binding, env, seen);
            }
            bool required = member.required;
            if (member is EnumMember enumMember)
            {
                return new NeoClassArgumentSignature
                {
                    type = MemberKind.Enum,
                    required = required,
                    enumId = enumMember.enumId,
                    multiselect = enumMember.multiselect,
                };
            }
            if (member is ClassMember classMember)
            {
                var args = new Dictionary<string, NeoClassArgumentSignature>();
                if (classMember.classArguments is not null)
                {
                    foreach (var pair in classMember.classArguments)
                    {
                        args[pair.Key] = SignatureOfBinding(client, pair.Value, env, seen);
                    }
                }
                return new NeoClassArgumentSignature
                {
                    type = MemberKind.Class,
                    required = required,
                    classId = classMember.classId,
                    args = args,
                };
            }
            if (member is ListMember listMember)
            {
                return new NeoClassArgumentSignature
                {
                    type = MemberKind.List,
                    required = required,
                    listKind = listMember.listKind ?? NeoListKinds.Ordered,
                    entry = EntrySignature(client, listMember.entryMemberId, env, seen),
                };
            }
            if (member is DictionaryMember dictionaryMember)
            {
                return new NeoClassArgumentSignature
                {
                    type = MemberKind.Dictionary,
                    required = required,
                    keyKind = dictionaryMember.keyKind ?? NeoDictionaryKeyKinds.String,
                    keyEnumId = dictionaryMember.keyEnumId,
                    entry = EntrySignature(client, dictionaryMember.entryMemberId, env, seen),
                };
            }
            if (member is StringMember stringMember)
            {
                return new NeoClassArgumentSignature
                {
                    type = MemberKind.String,
                    required = required,
                    // The field initializer defaults to true, so an absent
                    // wire value and an explicit `true` signature identically
                    // — the same normalization as the web module.
                    localizable = stringMember.localizable,
                };
            }
            return new NeoClassArgumentSignature
            {
                type = member.kind,
                required = required,
            };
        }

        private static NeoClassArgumentSignature EntrySignature(
            NeoClient client,
            string entryMemberId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            HashSet<string> seen)
        {
            if (!client.TryGetMember(entryMemberId, out Member? entryMember))
            {
                throw new System.InvalidOperationException(
                    $"SignatureOfMember: entry member '{entryMemberId}' does not exist in the document.");
            }
            return SignatureOfMember(client, entryMember, env, seen);
        }

        /// <summary>
        /// Signature of a binding/argument: forwards resolve through
        /// <paramref name="env"/> first; member bindings signature
        /// directly. Mirrors the web's <c>typeArgumentSignatureOfBinding</c>.
        /// </summary>
        public static NeoClassArgumentSignature SignatureOfBinding(
            NeoClient client,
            GenericBinding binding,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            HashSet<string>? seen = null)
        {
            seen ??= new HashSet<string>();
            if (binding.IsForward)
            {
                if (!env.TryGetValue(binding.genericParamId!, out NeoGenericEnvEntry entry))
                {
                    throw new System.InvalidOperationException(
                        $"SignatureOfBinding: forwarded generic param '{binding.genericParamId}' is not present in the binding environment.");
                }
                if (!entry.IsBound)
                {
                    throw new System.InvalidOperationException(
                        $"SignatureOfBinding: forwarded generic param '{binding.genericParamId}' is unbound — signatures exist only under closed environments.");
                }
                return SignatureOfBinding(
                    client,
                    new GenericBinding
                    {
                        kind = NeoGenericBindingKinds.Member,
                        memberId = entry.memberId,
                    },
                    env,
                    seen);
            }
            if (!client.TryGetMember(binding.memberId!, out Member? member))
            {
                throw new System.InvalidOperationException(
                    $"SignatureOfBinding: binding member '{binding.memberId}' does not exist in the document.");
            }
            return SignatureOfMember(client, member, env, seen);
        }

        /// <summary>
        /// Deep structural equality over projection signatures — invariant
        /// (spec Decision 8: <c>Foo&lt;Child&gt;</c> is not a
        /// <c>Foo&lt;Parent&gt;</c>). Mirrors the web's
        /// <c>signaturesEqual</c>.
        /// </summary>
        public static bool SignaturesEqual(
            NeoClassArgumentSignature a,
            NeoClassArgumentSignature b)
        {
            if (a.type != b.type) return false;
            if (a.required != b.required) return false;
            if (a.type == MemberKind.String)
            {
                return a.localizable == b.localizable;
            }
            if (a.type == MemberKind.Enum)
            {
                return a.enumId == b.enumId && a.multiselect == b.multiselect;
            }
            if (a.type == MemberKind.Class)
            {
                if (a.classId != b.classId) return false;
                var aArgs = a.args ?? new Dictionary<string, NeoClassArgumentSignature>();
                var bArgs = b.args ?? new Dictionary<string, NeoClassArgumentSignature>();
                if (aArgs.Count != bArgs.Count) return false;
                foreach (var pair in aArgs)
                {
                    if (!bArgs.TryGetValue(pair.Key, out NeoClassArgumentSignature bArg)) return false;
                    if (!SignaturesEqual(pair.Value, bArg)) return false;
                }
                return true;
            }
            if (a.type == MemberKind.List)
            {
                if (a.listKind != b.listKind) return false;
                return SignaturesEqual(a.entry!, b.entry!);
            }
            if (a.type == MemberKind.Dictionary)
            {
                if (a.keyKind != b.keyKind) return false;
                if (a.keyEnumId != b.keyEnumId) return false;
                return SignaturesEqual(a.entry!, b.entry!);
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Constructed-slot admission (spec §3.4).
        // ------------------------------------------------------------------

        /// <summary>
        /// Decides whether a value of <paramref name="valueClassId"/> is
        /// admissible in the slot declared by
        /// <paramref name="slotMember"/> under
        /// <paramref name="contextEnv"/> (the enclosing closed context's
        /// environment — used to substitute forwarded arguments). Mirrors
        /// the web's <c>constructedSlotAccepts</c>:
        /// <list type="number">
        ///   <item><description><paramref name="valueClassId"/> must be the
        ///   slot's <c>classId</c> or a descendant (nominal walk);</description></item>
        ///   <item><description><paramref name="valueClassId"/> must be
        ///   closed (open classes are never instantiable);</description></item>
        ///   <item><description>for each constructed argument, the value
        ///   class's terminal binding must signature-match the
        ///   context-substituted argument — invariantly.</description></item>
        /// </list>
        /// <c>isAbstract</c> is deliberately not checked here — value
        /// creation paths already reject abstract classes, and this function
        /// also tests mid-hierarchy compatibility. Generated writable
        /// setters that accept a caller-chosen subclass id (and any future
        /// SDK-side value-admission path) should call this before minting a
        /// row for a constructed slot.
        /// </summary>
        public static NeoSlotAdmission ConstructedSlotAccepts(
            NeoClient client,
            ClassMember slotMember,
            string valueClassId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> contextEnv)
        {
            var chain = NeoSchemaClassInheritance.ResolveChain(
                valueClassId,
                id => client.TryGetClass(id, out NeoSchemaClass? match) ? match : null);
            if (chain.Count == 0)
            {
                return NeoSlotAdmission.Reject(
                    $"class '{valueClassId}' does not exist in the document");
            }
            bool isDescendant = false;
            foreach (var t in chain)
            {
                if (t.id != slotMember.classId) continue;
                isDescendant = true;
                break;
            }
            if (!isDescendant)
            {
                return NeoSlotAdmission.Reject(
                    $"class '{chain[0].name}' is not '{slotMember.classId}' or one of its descendants");
            }
            // The pick's env is its chain env overlaid with the SLOT's
            // constructed arguments: for a constructed slot, the declared
            // class itself (or an open descendant that forwards the params)
            // is closed BY the slot — rejecting it as "open" would leave
            // constructed slots with no valid pick at all (spec §3.4).
            var valueEnv = ResolveInstanceEnv(chain, slotMember.classArguments);
            foreach (var entry in valueEnv.Values)
            {
                if (entry.IsBound) continue;
                return NeoSlotAdmission.Reject(
                    $"type '{chain[0].name}' is open (generic param '{entry.unboundParamId}' is unbound) and cannot be instantiated");
            }
            var args = slotMember.classArguments;
            if (args is null) return NeoSlotAdmission.Ok;
            foreach (var pair in args)
            {
                if (!valueEnv.TryGetValue(pair.Key, out NeoGenericEnvEntry valueBinding))
                {
                    return NeoSlotAdmission.Reject(
                        $"type '{chain[0].name}' does not resolve generic param '{pair.Key}' anywhere in its chain");
                }
                if (!valueBinding.IsBound)
                {
                    return NeoSlotAdmission.Reject(
                        $"type '{chain[0].name}' leaves generic param '{pair.Key}' unbound");
                }
                var slotSignature = SignatureOfBinding(client, pair.Value, contextEnv);
                var valueSignature = SignatureOfBinding(
                    client,
                    new GenericBinding
                    {
                        kind = NeoGenericBindingKinds.Member,
                        memberId = valueBinding.memberId,
                    },
                    valueEnv);
                if (!SignaturesEqual(slotSignature, valueSignature))
                {
                    return NeoSlotAdmission.Reject(
                        $"class-argument signature mismatch for param '{pair.Key}' (arguments compare invariantly)");
                }
            }
            return NeoSlotAdmission.Ok;
        }
    }
}
