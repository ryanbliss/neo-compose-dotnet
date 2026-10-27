// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
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
        private NeoGenericEnvEntry(
            string? memberId,
            string? unboundParamId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry>? bindings)
        {
            this.memberId = memberId;
            this.unboundParamId = unboundParamId;
            this.bindings = bindings;
        }

        public static NeoGenericEnvEntry Bound(string memberId) =>
            new(memberId, null, null);

        /// <summary>
        /// A bound entry whose member reads params of the class it was
        /// written in (<c>List&lt;T&gt;</c> under <c>{T: Seed}</c>): the
        /// member is kept as written, and <paramref name="bindings"/> carries
        /// those params closed, so a reader overlays them
        /// (<see cref="NeoGenericResolution.BindingEnv"/>). In memory only:
        /// stamps flatten (<see cref="NeoGenericResolution.GenericBindingsStamp"/>).
        /// </summary>
        public static NeoGenericEnvEntry Bound(string memberId, IReadOnlyDictionary<string, NeoGenericEnvEntry> bindings) =>
            new(memberId, null, bindings);

        public static NeoGenericEnvEntry Unbound(string paramId) =>
            new(null, paramId, null);

        public bool IsBound => memberId is not null;

        /// <summary>Terminal binding member id; set iff <see cref="IsBound"/>.</summary>
        public string? memberId
        {
            get;
        }

        /// <summary>Deepest forward target; set iff not <see cref="IsBound"/>.</summary>
        public string? unboundParamId
        {
            get;
        }

        /// <summary>The params <see cref="memberId"/> reads, closed; bound entries only.</summary>
        public IReadOnlyDictionary<string, NeoGenericEnvEntry>? bindings
        {
            get;
        }
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

        public bool ok
        {
            get;
        }

        /// <summary>Human-readable rejection reason; set iff not <see cref="ok"/>.</summary>
        public string? reason
        {
            get;
        }
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

        /// <summary>String only. Normalized: absent means Localized.</summary>
        public NeoStringFormatKind format;

        /// <summary>Enum only.</summary>
        public string? enumId;

        /// <summary>Enum, Lookup and DialogueLookup only.</summary>
        public NeoMemberSelectionKind selection;

        /// <summary>Object only: the narrowed type, absent for <c>object</c>.</summary>
        public NeoObjectTypeKind? objectType;

        /// <summary>Lookup only (P112 §2.1): the <c>Reference&lt;target&gt;</c> it names.</summary>
        public TypeInfo? target;

        /// <summary>Class only.</summary>
        public string? classId;

        /// <summary>Interface only (P113 §4.4).</summary>
        public string? interfaceId;

        /// <summary>Class and Interface — recursive argument signatures by target param id.</summary>
        public Dictionary<string, NeoClassArgumentSignature>? args;

        /// <summary>List only; absent normalizes to Ordered.</summary>
        public NeoListKind listKind;

        /// <summary>Dictionary only; absent normalizes to String.</summary>
        public NeoDictionaryKeyKind keyKind;

        /// <summary>Dictionary only — set iff <see cref="keyKind"/> is Enum.</summary>
        public string? keyEnumId;

        /// <summary>List / Set / Dictionary only — recursive entry signature.</summary>
        public NeoClassArgumentSignature? entry;
        public NeoClassArgumentSignature[]? elements;

        /// <summary>NSDelegate and NSAction only: the callable's type, compared invariantly.</summary>
        public TypeInfo? callable;
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
    ///   <item><description>the binding node's <c>genericBindings</c>
    ///   stamp (<see cref="GenericBindingsStamp"/> /
    ///   <see cref="EnvFromStamp"/>);</description></item>
    ///   <item><description>invariant class-argument signatures + admission
    ///   (<see cref="SignatureOfMember"/> /
    ///   <see cref="ConstructedSlotAccepts"/>).</description></item>
    /// </list>
    /// Sparse <c>extendsMemberId</c> override chains are resolved by
    /// <c>NeoMemberShapeResolution</c> before this module substitutes generic
    /// slots, matching the web runtime's effective-member semantics.
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
            var chain = client.ResolveClassInheritanceChain(classId);
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
                if (declared is null)
                    continue;
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
                TypeBinding? binding = null;
                if (child.extendsGenericBindings is not null)
                {
                    child.extendsGenericBindings.TryGetValue(cursor, out binding);
                }
                if (binding is null)
                {
                    return NeoGenericEnvEntry.Unbound(cursor);
                }
                if (!binding.IsOpen)
                {
                    return NeoGenericEnvEntry.Bound(NeoTypeBindingViews.Id(binding));
                }
                cursor = binding.Param!;
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
                if (!entry.IsBound)
                    return false;
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
            IReadOnlyDictionary<string, TypeBinding>? classArguments,
            IReadOnlyDictionary<string, NeoGenericEnvEntry>? ownerEnv = null)
        {
            var chain = client.ResolveClassInheritanceChain(effectiveClassId);
            return ResolveInstanceEnv(chain, classArguments, client, ownerEnv);
        }

        /// <summary>
        /// Chain-based overload of
        /// <see cref="ResolveInstanceEnv(NeoClient, string, IReadOnlyDictionary{string, TypeBinding}, IReadOnlyDictionary{string, NeoGenericEnvEntry})"/>
        /// for callers that already resolved the child-first inheritance
        /// chain (e.g. <see cref="NeoMemberClass"/>'s type context). A slot
        /// argument closes the row's own params only with members that name
        /// no param, reading an owner's
        /// param in <paramref name="ownerEnv"/>, the environment the slot
        /// was read in; a base binding that wraps a param binds it too
        /// (<c>ListBox&lt;T&gt; : Box&lt;List&lt;T&gt;&gt;</c> in
        /// <c>Box&lt;List&lt;Seed&gt;&gt;</c>). The server's
        /// <c>resolveInstanceEnv</c> given an <c>IInstanceUnification</c>.
        /// </summary>
        public static IReadOnlyDictionary<string, NeoGenericEnvEntry> ResolveInstanceEnv(
            IList<NeoSchemaClass> chain,
            IReadOnlyDictionary<string, TypeBinding>? classArguments,
            NeoClient client,
            IReadOnlyDictionary<string, NeoGenericEnvEntry>? ownerEnv = null)
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
            // A flattened argument map carries the owner's bindings for params
            // outside the row's class (`D = List<T>` under `{T: Seed}` carries
            // `T`), so they read as the owner environment the row's own
            // arguments close in.
            Dictionary<string, NeoGenericEnvEntry>? carriedOwnerEnv = null;
            foreach (var pair in classArguments)
            {
                if (env.ContainsKey(pair.Key) || pair.Value.IsOpen)
                    continue;
                if (carriedOwnerEnv is null)
                {
                    carriedOwnerEnv = new Dictionary<string, NeoGenericEnvEntry>(StringComparer.Ordinal);
                    if (ownerEnv is not null)
                    {
                        foreach (var owner in ownerEnv)
                            carriedOwnerEnv[owner.Key] = owner.Value;
                    }
                }
                carriedOwnerEnv[pair.Key] = NeoGenericEnvEntry.Bound(NeoTypeBindingViews.Id(pair.Value));
            }
            IReadOnlyDictionary<string, NeoGenericEnvEntry> scope = carriedOwnerEnv ?? ownerEnv ?? EmptyEnv;
            foreach (var pair in classArguments)
            {
                NeoGenericEnvEntry? argument = ArgumentEntry(client, pair.Value, scope);
                if (argument is null)
                    continue;
                overlaid.TryGetValue(pair.Key, out NeoGenericEnvEntry? existing);
                if (existing is { IsBound: true })
                {
                    // P127 §1.2: a base slot closes the row's params its
                    // base's binding wraps (`ListBox<T> : Box<List<T>>` in
                    // `Box<List<Seed>>`).
                    BindWrappedParams(client, existing.memberId!, argument, overlaid, scope, new HashSet<string>(StringComparer.Ordinal));
                    continue;
                }
                // A param binds only to a closed type, so one naming a param
                // the owner leaves open (`List<T>` from an open `Hold<T>`)
                // stays open.
                NeoGenericEnvEntry? bound = ClosedEntry(client, argument, scope, null);
                if (bound is null)
                    continue;
                if (existing is null)
                {
                    overlaid[pair.Key] = bound;
                    continue;
                }
                // P127 §1.2: the row's own param its base forwards to binds,
                // unless an earlier argument bound it (`DerivedBox<D> : Box<D>`
                // in `Box<Seed>`); the forward closing below then closes the
                // base's param from it.
                BindOpenParam(overlaid, existing.unboundParamId!, bound);
            }
            // `Bag<T> <- SubBag<U>` constructed as `SubBag<int>`: T forwards to
            // U, its terminal param, so it closes with U. Mirrors the web's
            // `closeForwardedGenericParams`.
            List<string>? forwarded = null;
            foreach (var pair in overlaid)
            {
                if (!pair.Value.IsBound && pair.Value.unboundParamId != pair.Key)
                    (forwarded ??= new List<string>()).Add(pair.Key);
            }
            if (forwarded is null)
                return overlaid;
            foreach (string paramId in forwarded)
            {
                if (overlaid.TryGetValue(overlaid[paramId].unboundParamId!, out NeoGenericEnvEntry? target)
                    && target.IsBound)
                {
                    overlaid[paramId] = target;
                }
            }
            return overlaid;
        }

        /// <summary>
        /// Binds the open param <paramref name="paramId"/> aliases
        /// (<c>Mid&lt;X&gt;</c>'s <c>X</c> is <c>Leaf&lt;L&gt;</c>'s <c>L</c>);
        /// the forward closing then closes every alias of it.
        /// </summary>
        private static void BindOpenParam(Dictionary<string, NeoGenericEnvEntry> env, string paramId, NeoGenericEnvEntry bound)
        {
            string current = paramId;
            // Each step visits another param, so a cycle ends within env.Count steps.
            for (int step = 0; step <= env.Count; step++)
            {
                if (!env.TryGetValue(current, out NeoGenericEnvEntry? entry) || entry.IsBound)
                    return;
                if (entry.unboundParamId == current)
                {
                    env[current] = bound;
                    return;
                }
                current = entry.unboundParamId!;
            }
        }

        /// <summary>
        /// The entry <paramref name="argument"/> stands for in
        /// <paramref name="ownerEnv"/>: a forward's owner param, a stamp's
        /// closed entry, or a member argument's.
        /// </summary>
        private static NeoGenericEnvEntry? ArgumentEntry(
            NeoClient client,
            TypeBinding argument,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> ownerEnv)
        {
            if (argument.IsOpen)
                return OwnerParamEntry(client, argument.Param!, ownerEnv);
            return OwnerArgumentEntry(client, NeoTypeBindingViews.Id(argument), ownerEnv);
        }

        /// <summary>
        /// The type an argument stands for. An argument that is the owner's
        /// own param (<c>T</c> in <c>Hold&lt;T&gt;.Listed: Box&lt;T&gt;</c>)
        /// reads <paramref name="ownerEnv"/>'s entry; null when that's open.
        /// </summary>
        private static NeoGenericEnvEntry? OwnerArgumentEntry(
            NeoClient client,
            string argumentId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> ownerEnv)
        {
            if (!client.TryGetMember(argumentId, out Member? argument))
                return null;
            return argument is GenericMember generic
                ? OwnerParamEntry(client, generic.genericParamId, ownerEnv)
                : NeoGenericEnvEntry.Bound(argumentId);
        }

        /// <summary>The entry <paramref name="paramId"/> terminally binds to in <paramref name="ownerEnv"/>; null when open.</summary>
        private static NeoGenericEnvEntry? OwnerParamEntry(
            NeoClient client,
            string paramId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> ownerEnv)
        {
            if (!ownerEnv.TryGetValue(paramId, out NeoGenericEnvEntry? entry))
                return null;
            for (int step = 0; step < ownerEnv.Count && !entry.IsBound; step++)
            {
                if (!ownerEnv.TryGetValue(entry.unboundParamId!, out entry))
                    return null;
            }
            if (!entry.IsBound || !client.TryGetMember(entry.memberId!, out Member? bound))
                return null;
            return bound is GenericMember ? null : entry;
        }

        /// <summary>
        /// <paramref name="entry"/>, carrying the owner's entries for the
        /// params its member names (<c>List&lt;T&gt;</c> from
        /// <c>Hold&lt;Seed&gt;</c> is <c>List&lt;T&gt;</c> under
        /// <c>{T: Seed}</c>). Null when the owner leaves one open, so the
        /// row's param stays open rather than naming a param outside its
        /// class. The server's <c>closedEntry</c>.
        /// </summary>
        private static NeoGenericEnvEntry? ClosedEntry(
            NeoClient client,
            NeoGenericEnvEntry entry,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> ownerEnv,
            HashSet<string>? seen)
        {
            if (entry.bindings is not null)
                return entry;
            if (!client.TryGetMember(entry.memberId!, out Member? member))
                return null;
            IReadOnlyCollection<string> paramIds = ReferencedGenericParams(client, member);
            if (paramIds.Count == 0)
                return entry;
            var bindings = new Dictionary<string, NeoGenericEnvEntry>(paramIds.Count, StringComparer.Ordinal);
            foreach (string paramId in paramIds)
            {
                if (seen is not null && seen.Contains(paramId))
                    return null;
                if (OwnerParamEntry(client, paramId, ownerEnv) is not { } owner)
                    return null;
                var nested = new HashSet<string>(StringComparer.Ordinal) { paramId };
                if (seen is not null)
                    nested.UnionWith(seen);
                if (ClosedEntry(client, owner, ownerEnv, nested) is not { } bound)
                    return null;
                bindings[paramId] = bound;
            }
            return NeoGenericEnvEntry.Bound(entry.memberId!, bindings);
        }

        /// <summary>
        /// Whether <paramref name="member"/> is a class slot with an argument
        /// that names a param inside it (<c>Box&lt;List&lt;T&gt;&gt;</c>),
        /// which substitution leaves in place, so the row in it reads that
        /// param through the slot's owner environment.
        /// </summary>
        private static bool WrapsParams(NeoClient client, Member member)
        {
            if (member is not ClassMember { TypeArguments: { } arguments })
                return false;
            foreach (TypeBinding argument in arguments.Values)
            {
                if (argument.IsOpen || !client.TryGetMember(NeoTypeBindingViews.Id(argument), out Member? wrapped))
                    continue;
                bool canName = wrapped is GenericMember or ListMember or DictionaryMember or TupleMember
                    || wrapped is ClassMember { TypeArguments: not null };
                if (canName && ReferencedGenericParams(client, wrapped).Count > 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Whether <paramref name="member"/>'s type names a param in a place
        /// substitution leaves it, so the row placed in it reads that param
        /// through the environment the member was read in: a class slot that
        /// <see cref="WrapsParams"/>, or a collection or tuple whose entries
        /// name one. Depends only on the schema, so it's memoized per member
        /// until the client's schema resolutions reset.
        /// </summary>
        internal static bool ReadsOwnerParams(NeoClient client, Member member)
        {
            object resolution = client.SchemaResolution;
            if (ReferenceEquals(Volatile.Read(ref member.readsOwnerParamsResolution), resolution))
                return member.readsOwnerParams;
            bool reads = member switch
            {
                ClassMember => WrapsParams(client, member),
                ListMember or DictionaryMember or TupleMember => ReferencedGenericParams(client, member).Count > 0,
                _ => false,
            };
            member.readsOwnerParams = reads;
            Volatile.Write(ref member.readsOwnerParamsResolution, resolution);
            return reads;
        }

        /// <summary>
        /// <paramref name="member"/> substituted through
        /// <paramref name="env"/>, carrying as its
        /// <see cref="Member.ownerEnvironment"/> the environment its type
        /// reads params in, as the server walk passes its ambient
        /// environment to the row's: <see cref="SubstitutedMemberEnv"/> for a
        /// Generic member whose binding carries the owner's entries,
        /// <paramref name="env"/> when the member
        /// <see cref="ReadsOwnerParams"/> or keeps a forward such a binding
        /// closes, and none otherwise.
        /// </summary>
        internal static Member SubstituteOwnedMember(
            NeoClient client,
            Member member,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env) =>
            SubstituteOwnedMember(client, member, env, ReadsOwnerParams(client, member));

        /// <inheritdoc cref="SubstituteOwnedMember(NeoClient, Member, IReadOnlyDictionary{string, NeoGenericEnvEntry})"/>
        /// <param name="readsOwnerParams">The caller's <see cref="ReadsOwnerParams"/> for <paramref name="member"/>.</param>
        internal static Member SubstituteOwnedMember(
            NeoClient client,
            Member member,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            bool readsOwnerParams)
        {
            Member resolved = SubstituteMember(client, member, env);
            IReadOnlyDictionary<string, NeoGenericEnvEntry> memberEnv = SubstitutedMemberEnv(member, env);
            IReadOnlyDictionary<string, NeoGenericEnvEntry>? ownerEnv;
            if (!ReferenceEquals(memberEnv, env))
                ownerEnv = memberEnv;
            else if ((readsOwnerParams || ForwardsCarriedEntry(resolved, env)) && env.Count > 0)
                ownerEnv = env;
            else
                ownerEnv = null;
            if (ReferenceEquals(resolved.ownerEnvironment, ownerEnv))
                return resolved;
            if (ReferenceEquals(resolved, member))
                resolved = member.ShallowClone();
            resolved.ownerEnvironment = ownerEnv;
            return resolved;
        }

        /// <summary>
        /// Whether <paramref name="resolved"/> is a class slot that kept a
        /// forward because <paramref name="env"/> binds it to an entry
        /// carrying the owner's (<see cref="SubstituteMember(NeoClient, Member, IReadOnlyDictionary{string, NeoGenericEnvEntry})"/>),
        /// so the row in it reads that entry through <paramref name="env"/>.
        /// </summary>
        private static bool ForwardsCarriedEntry(
            Member resolved,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (resolved is not ClassMember { TypeArguments: { } arguments })
                return false;
            foreach (TypeBinding argument in arguments.Values)
            {
                if (argument.IsOpen
                    && env.TryGetValue(argument.Param!, out NeoGenericEnvEntry? entry)
                    && entry.bindings is not null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A collection's entry or a tuple's element read in
        /// <paramref name="env"/>, its row's environment
        /// (<see cref="CollectionEntryEnv"/>). The raw member when nothing
        /// closes it, and a Generic one <paramref name="env"/> leaves open
        /// stays raw until a stamped row binds it.
        /// </summary>
        internal static Member SubstituteEntryMember(
            NeoClient client,
            Member entry,
            IReadOnlyDictionary<string, NeoGenericEnvEntry>? env)
        {
            if (env is null)
                return entry;
            if (entry is GenericMember generic
                && (!env.TryGetValue(generic.genericParamId, out NeoGenericEnvEntry? binding) || !binding.IsBound))
            {
                return entry;
            }
            return SubstituteOwnedMember(client, entry, env);
        }

        /// <summary>
        /// The environment the members of <paramref name="row"/>, placed in
        /// <paramref name="slot"/>, read in: the server walk's per-row
        /// environment, from the slot, the environment it was read in
        /// (<paramref name="ownerEnv"/>, usually its
        /// <see cref="Member.ownerEnvironment"/>) and the row's
        /// <paramref name="stamp"/>. A class row's is its instance
        /// environment: <paramref name="classId"/>'s chain, closed by the
        /// slot's arguments over the stamp, read in the owner's. A collection's
        /// or tuple's (null <paramref name="classId"/>) is its stamp over the
        /// owner's (the server's <c>overlayStamp</c>).
        /// </summary>
        internal static IReadOnlyDictionary<string, NeoGenericEnvEntry> SlotRowEnv(
            NeoClient client,
            Member? slot,
            IReadOnlyDictionary<string, NeoGenericEnvEntry>? ownerEnv,
            IReadOnlyDictionary<string, TypeBinding>? stamp,
            string? classId = null,
            IList<NeoSchemaClass>? chain = null)
        {
            if (classId is null)
                return OverlayStamp(client, ownerEnv ?? EmptyEnv, stamp);
            IReadOnlyDictionary<string, TypeBinding>? arguments = CloseClassArgumentsFromStamp(
                client,
                stamp,
                client.SlotClassArguments(slot as ClassMember, classId));
            return ResolveInstanceEnv(chain ?? client.ResolveClassInheritanceChain(classId), arguments, client, ownerEnv);
        }

        /// <summary><paramref name="env"/> with <paramref name="stamp"/>'s bindings over it: a row's stamp wins over the environment it was reached in.</summary>
        private static IReadOnlyDictionary<string, NeoGenericEnvEntry> OverlayStamp(
            NeoClient client,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            IReadOnlyDictionary<string, TypeBinding>? stamp)
        {
            if (stamp is null || stamp.Count == 0)
                return env;
            if (env.Count == 0)
                return EnvFromStamp(client, stamp);
            var overlaid = new Dictionary<string, NeoGenericEnvEntry>(env.Count + stamp.Count);
            foreach (KeyValuePair<string, NeoGenericEnvEntry> pair in env)
                overlaid[pair.Key] = pair.Value;
            foreach (KeyValuePair<string, TypeBinding> pair in stamp)
                overlaid[pair.Key] = NeoTypeBindings.Entry(pair.Value);
            return overlaid;
        }

        /// <summary>
        /// Binds each open param <paramref name="patternId"/>'s member names
        /// to the part of <paramref name="argument"/>'s type in the same
        /// place, walking the same structure as
        /// <see cref="ReferencedGenericParams"/>: class arguments, tuple
        /// elements and entries. <paramref name="ownerEnv"/> reads the params
        /// <paramref name="argument"/>'s member names. The server's
        /// <c>bindWrappedParams</c>.
        /// </summary>
        private static void BindWrappedParams(
            NeoClient client,
            string patternId,
            NeoGenericEnvEntry argument,
            Dictionary<string, NeoGenericEnvEntry> env,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> ownerEnv,
            HashSet<string> seen)
        {
            if (!seen.Add(patternId))
                return;
            if (!client.TryGetMember(patternId, out Member? pattern))
                return;
            if (pattern is GenericMember generic)
            {
                if (ClosedEntry(client, argument, ownerEnv, null) is { } bound)
                    BindOpenParam(env, generic.genericParamId, bound);
                return;
            }
            if (!client.TryGetMember(argument.memberId!, out Member? argumentMember) || argumentMember.kind != pattern.kind)
                return;
            // The argument's parts read the entries it carries, if any.
            IReadOnlyDictionary<string, NeoGenericEnvEntry> inner = argument.bindings ?? ownerEnv;
            switch (pattern)
            {
                case ClassMember patternSlot when argumentMember is ClassMember argumentSlot:
                    {
                        if (patternSlot.TypeArguments is not { } patternBindings)
                            return;
                        Dictionary<string, TypeBinding>? argumentBindings = argumentSlot.TypeArguments;
                        foreach (KeyValuePair<string, TypeBinding> binding in patternBindings)
                        {
                            if (argumentBindings is null || !argumentBindings.TryGetValue(binding.Key, out TypeBinding? other))
                                continue;
                            // The argument's own forward names the owner's param.
                            NeoGenericEnvEntry? otherEntry = other.IsOpen
                                ? OwnerParamEntry(client, other.Param!, inner)
                                : OwnerArgumentEntry(client, NeoTypeBindingViews.Id(other), inner);
                            if (otherEntry is null)
                                continue;
                            if (!binding.Value.IsOpen)
                            {
                                BindWrappedParams(client, NeoTypeBindingViews.Id(binding.Value), otherEntry, env, inner, seen);
                                continue;
                            }
                            if (ClosedEntry(client, otherEntry, inner, null) is { } bound)
                                BindOpenParam(env, binding.Value.Param!, bound);
                        }
                        return;
                    }
                case TupleMember patternTuple when argumentMember is TupleMember argumentTuple:
                    for (int index = 0; index < patternTuple.elementMemberIds.Length && index < argumentTuple.elementMemberIds.Length; index++)
                        WalkWrapped(client, patternTuple.elementMemberIds[index], argumentTuple.elementMemberIds[index], env, inner, seen);
                    return;
                case ListMember patternList when argumentMember is ListMember argumentList:
                    WalkWrapped(client, patternList.entryMemberId, argumentList.entryMemberId, env, inner, seen);
                    return;
                case DictionaryMember patternDictionary when argumentMember is DictionaryMember argumentDictionary:
                    WalkWrapped(client, patternDictionary.entryMemberId, argumentDictionary.entryMemberId, env, inner, seen);
                    return;
            }
        }

        /// <summary><see cref="BindWrappedParams"/> into a part: <paramref name="argumentId"/> read in <paramref name="ownerEnv"/>.</summary>
        private static void WalkWrapped(
            NeoClient client,
            string patternId,
            string argumentId,
            Dictionary<string, NeoGenericEnvEntry> env,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> ownerEnv,
            HashSet<string> seen)
        {
            if (OwnerArgumentEntry(client, argumentId, ownerEnv) is { } next)
                BindWrappedParams(client, patternId, next, env, ownerEnv, seen);
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
                if (!entry.IsBound)
                    return entry.unboundParamId;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Stamps (spec Decision 9).
        // ------------------------------------------------------------------

        /// <summary>
        /// Rebuilds a binding environment from a binding node's
        /// <see cref="MemberValue.genericBindings"/> stamp — the
        /// read-side inverse of <see cref="GenericBindingsStamp"/>.
        /// </summary>
        public static IReadOnlyDictionary<string, NeoGenericEnvEntry> EnvFromStamp(
            NeoClient client,
            IReadOnlyDictionary<string, TypeBinding>? stamp)
        {
            if (stamp is null || stamp.Count == 0)
                return EmptyEnv;
            var env = new Dictionary<string, NeoGenericEnvEntry>(stamp.Count);
            foreach (var pair in stamp)
                env[pair.Key] = NeoTypeBindings.Entry(pair.Value);
            return env;
        }

        /// <summary>
        /// The environment a container row's entries resolve in, read from
        /// the row alone: its stamp, or what its typeSource resolves to one
        /// hop away (P127 §12.1). Empty when it stores neither.
        /// </summary>
        internal static IReadOnlyDictionary<string, NeoGenericEnvEntry> RowEnv(
            NeoClient client,
            MemberValue? row,
            Member member) =>
            EnvFromStamp(client, NeoTypeSources.EntryStamp(client, row, member));

        /// <summary>
        /// <see cref="RowEnv"/>, or the construction's <paramref name="env"/>
        /// where the source row stores neither a stamp nor a typeSource that
        /// closes anything, such as an inline default being copied.
        /// </summary>
        internal static IReadOnlyDictionary<string, NeoGenericEnvEntry> RowEnvOr(
            NeoClient client,
            MemberValue? row,
            Member member,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (row is null || (row.genericBindings is null && row.typeSource is null))
                return env;
            // A template has no owning binding node yet. Its destination's
            // environment closes the copied entries, not the open source type.
            if (row.typeSource is { ValueId: null, Type: { } template } && NeoTypeSources.HasOpenArm(template))
                return env;
            return NeoTypeSources.EntryStamp(client, row, member) is { } stamp ? EnvFromStamp(client, stamp) : env;
        }

        /// <summary>
        /// The environment a collection node's entries resolve in: its row's
        /// <see cref="SlotRowEnv"/>, its stamp over the environment the
        /// collection was read in, or, for a node reading its member's declaration default
        /// (which has no row to stamp), the closed environment of the class
        /// declaring that member — the web's <c>declaringClassEnv</c>. Null
        /// when neither closes anything.
        /// </summary>
        internal static IReadOnlyDictionary<string, NeoGenericEnvEntry>? CollectionEntryEnv(
            NeoClient client,
            Member collectionMember,
            MemberValue? row,
            bool readsDeclarationDefault) =>
            CollectionEntryEnv(client, collectionMember, NeoTypeSources.EntryStamp(client, row, collectionMember), readsDeclarationDefault);

        /// <summary><see cref="CollectionEntryEnv(NeoClient, Member, MemberValue?, bool)"/> over a stamp already read from a row.</summary>
        internal static IReadOnlyDictionary<string, NeoGenericEnvEntry>? CollectionEntryEnv(
            NeoClient client,
            Member collectionMember,
            IReadOnlyDictionary<string, TypeBinding>? stamp,
            bool readsDeclarationDefault)
        {
            if (stamp is not null || collectionMember.ownerEnvironment is not null)
                return SlotRowEnv(client, collectionMember, collectionMember.ownerEnvironment, stamp);
            if (!readsDeclarationDefault
                || client.SchemaOwnerClassId(collectionMember.id) is not string ownerClassId)
            {
                return null;
            }
            var env = ResolveEnv(client, ownerClassId);
            return env.Count > 0 && FirstUnboundParamId(env) is null ? env : null;
        }

        /// <summary>
        /// Rebuilds a constructed Class placement from a value row's durable
        /// generic stamp. The stamp's arguments are the instance's own, so
        /// they bind first and win for every parameter they close; the
        /// placement then supplies the rest. The server's
        /// <c>stampedClassArguments</c>.
        /// </summary>
        internal static IReadOnlyDictionary<string, TypeBinding>?
            CloseClassArgumentsFromStamp(
                NeoClient client,
                IReadOnlyDictionary<string, TypeBinding>? stamp,
                IReadOnlyDictionary<string, TypeBinding>? placementArguments)
        {
            if ((stamp is null || stamp.Count == 0)
                && (placementArguments is null || placementArguments.Count == 0))
            {
                return null;
            }
            if (stamp is null || stamp.Count == 0)
            {
                return placementArguments;
            }
            var result = new Dictionary<string, TypeBinding>(
                stamp.Count + (placementArguments?.Count ?? 0),
                StringComparer.Ordinal);
            foreach (KeyValuePair<string, TypeBinding> binding in stamp)
                result[binding.Key] = binding.Value;
            if (placementArguments is not null)
            {
                foreach (KeyValuePair<string, TypeBinding> argument in placementArguments)
                    result.TryAdd(argument.Key, argument.Value);
            }
            return result;
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
                if (classMember.TypeArguments is null)
                    return;
                foreach (var arg in classMember.TypeArguments.Values)
                {
                    if (arg.IsOpen)
                    {
                        result.Add(arg.Param!);
                        continue;
                    }
                    if (!client.TryGetMember(NeoTypeBindingViews.Id(arg), out Member? argumentMember))
                        continue;
                    CollectReferencedGenericParams(client, argumentMember, seen, result);
                }
                return;
            }
            if (member is TupleMember tuple)
            {
                foreach (string id in tuple.elementMemberIds)
                    if (client.TryGetMember(id, out Member? element))
                        CollectReferencedGenericParams(client, element, seen, result);
                return;
            }
            string? entryMemberId = member switch
            {
                ListMember list => list.entryMemberId,
                DictionaryMember dictionary => dictionary.entryMemberId,
                _ => null,
            };
            if (entryMemberId is null)
                return;
            if (!client.TryGetMember(entryMemberId, out Member? entryMember))
                return;
            CollectReferencedGenericParams(client, entryMember, seen, result);
        }

        /// <summary>
        /// The stamp holding <paramref name="entries"/>, each the exact closed
        /// type it binds, read in <paramref name="env"/>.
        /// </summary>
        internal static Dictionary<string, TypeBinding> GenericBindingsStamp(
            NeoClient client,
            IEnumerable<KeyValuePair<string, NeoGenericEnvEntry>> entries,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            var stamp = new Dictionary<string, TypeBinding>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, NeoGenericEnvEntry> pair in entries)
                stamp[pair.Key] = NeoTypeBindings.OfEntry(client, pair.Value, env);
            return stamp;
        }

        /// <summary>
        /// The environment <paramref name="entry"/>'s member reads its own
        /// params in: <paramref name="env"/>, overlaid with the owner entries
        /// <paramref name="entry"/> carries. Identity when it carries none.
        /// The server's <c>bindingEnv</c>.
        /// </summary>
        public static IReadOnlyDictionary<string, NeoGenericEnvEntry> BindingEnv(
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            NeoGenericEnvEntry entry)
        {
            if (entry.bindings is null)
                return env;
            var overlaid = new Dictionary<string, NeoGenericEnvEntry>(env.Count + entry.bindings.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, NeoGenericEnvEntry> pair in env)
                overlaid[pair.Key] = pair.Value;
            foreach (KeyValuePair<string, NeoGenericEnvEntry> pair in entry.bindings)
                overlaid[pair.Key] = pair.Value;
            return overlaid;
        }

        /// <summary>
        /// The environment <paramref name="member"/>'s own type reads its
        /// params in once substituted under <paramref name="env"/>: a Generic
        /// member whose binding carries the owner's entries reads those
        /// (P127 §1.2); any other member reads <paramref name="env"/>. The
        /// server's <c>substitutedMemberEnv</c>.
        /// </summary>
        public static IReadOnlyDictionary<string, NeoGenericEnvEntry> SubstitutedMemberEnv(
            Member member,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (member is not GenericMember generic
                || !env.TryGetValue(generic.genericParamId, out NeoGenericEnvEntry? entry)
                || !entry.IsBound)
            {
                return env;
            }
            return BindingEnv(env, entry);
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
        ///   per-kind config and <c>requirement</c>
        ///   (nullability is part of the type — one <c>T</c> is one type).
        ///   The slot keeps its default and identity/placement fields: <c>id</c>,
        ///   <c>name</c>, <c>access</c>, <c>modifier</c>,
        ///   <c>mutability</c>, <c>storage</c>,
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
        ///   <item><description><b>Callable and Variant type positions</b>
        ///   recursively substitute Generic type info while preserving the
        ///   member kind and argument names.</description></item>
        ///   <item><description>Everything else is identity.</description></item>
        /// </list>
        /// </summary>
        public static Member SubstituteMember(
            NeoClient client,
            Member member,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            return SubstituteMember(
                client,
                member,
                env,
                null);
        }

        private static void CopyDeclarationDefault(Member member, MemberValueBase<object?>? declarationDefault)
        {
            switch (member)
            {
                case Member<object?> typed:
                    CopyDeclarationDefault(typed, declarationDefault);
                    break;
                case Member<bool?> typed:
                    CopyDeclarationDefault(typed, declarationDefault);
                    break;
                case Member<double?> typed:
                    CopyDeclarationDefault(typed, declarationDefault);
                    break;
                case Member<string?> typed:
                    CopyDeclarationDefault(typed, declarationDefault);
                    break;
                case Member<Dictionary<string, string>?> typed:
                    CopyDeclarationDefault(typed, declarationDefault);
                    break;
                case Member<string[]?> typed:
                    CopyDeclarationDefault(typed, declarationDefault);
                    break;
                case Member<NeoDelegateValue?> typed:
                    CopyDeclarationDefault(typed, declarationDefault);
                    break;
                case Member<NeoActionValue?> typed:
                    CopyDeclarationDefault(typed, declarationDefault);
                    break;
                case Member<VariantRefValue?> typed:
                    CopyDeclarationDefault(typed, declarationDefault);
                    break;
                case Member<SpriteValue?> typed:
                    CopyDeclarationDefault(typed, declarationDefault);
                    break;
                case Member<FileValue?> typed:
                    CopyDeclarationDefault(typed, declarationDefault);
                    break;
                case Member<NeoVector2Value?> typed:
                    CopyDeclarationDefault(typed, declarationDefault);
                    break;
                case Member<NeoVector3Value?> typed:
                    CopyDeclarationDefault(typed, declarationDefault);
                    break;
                case Member<NeoColorValue?> typed:
                    CopyDeclarationDefault(typed, declarationDefault);
                    break;
                default:
                    throw new InvalidOperationException($"Member '{member.id}' cannot carry a declaration default.");
            }
        }

        private static void CopyDeclarationDefault<T>(Member<T> member, MemberValueBase<object?>? declarationDefault)
        {
            member.defaultValue = declarationDefault is null
                ? null
                : (MemberValueBase<T>)MemberValueFactory.ConvertDeclarationDefault(
                    declarationDefault, typeof(MemberValueBase<T>));
        }

        private static Member SubstituteMember(
            NeoClient client,
            Member member,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            HashSet<string>? activeGenericMembers)
        {
            if (member is GenericMember generic)
            {
                activeGenericMembers ??= new HashSet<string>(StringComparer.Ordinal);
                if (!activeGenericMembers.Add(generic.id))
                {
                    throw new System.InvalidOperationException(
                        $"SubstituteMember revisited Generic member '{generic.id}' ({generic.name}) — generic bindings form a reference cycle.");
                }
                Member binding;
                try
                {
                    Member terminal = ResolveTerminalBinding(
                        client,
                        generic.genericParamId,
                        generic.name,
                        env,
                        out IReadOnlyDictionary<string, NeoGenericEnvEntry> terminalEnv);
                    binding = SubstituteMember(client, terminal, terminalEnv, activeGenericMembers);
                }
                finally
                {
                    activeGenericMembers.Remove(generic.id);
                }
                Member substituted = binding.ShallowClone();
                substituted.id = generic.id;
                substituted.name = generic.name;
                // Binding overrides may omit zero-valued fields inherited
                // from another member. Bake their resolved shape before
                // detaching the substituted member from that inheritance
                // graph, preserving absent versus explicit ordinal zero.
                substituted.DeclaredRequirement = NeoTypeRules.ClosedRequirement(generic.Requirement, binding);
                switch (substituted)
                {
                    case StringMember stringValue when binding is StringMember bindingString:
                        stringValue.DeclaredFormat = bindingString.Format;
                        stringValue.DeclaredSearchBy = bindingString.SearchBy;
                        break;
                    case DictionaryMember dictionaryValue when binding is DictionaryMember bindingDictionary:
                        dictionaryValue.DeclaredKeyKind = bindingDictionary.KeyKind;
                        break;
                    case ListMember listValue when binding is ListMember bindingList:
                        listValue.DeclaredListKind = bindingList.ListKind;
                        break;
                    case EnumMember enumValue when binding is EnumMember bindingEnum:
                        enumValue.DeclaredSelection = bindingEnum.Selection;
                        break;
                    case LookupMember lookupValue when binding is LookupMember bindingLookup:
                        lookupValue.DeclaredSelection = bindingLookup.Selection;
                        break;
                    case DialogueLookupMember dialogueValue when binding is DialogueLookupMember bindingDialogue:
                        dialogueValue.DeclaredSelection = bindingDialogue.Selection;
                        break;
                    case FunctionMember functionValue when binding is FunctionMember bindingFunction:
                        functionValue.DeclaredDispatch = bindingFunction.Dispatch;
                        break;
                    case NSFunctionMember neoScriptFunctionValue when binding is NSFunctionMember bindingFunction:
                        neoScriptFunctionValue.DeclaredDispatch = bindingFunction.Dispatch;
                        neoScriptFunctionValue.DeclaredBodyMode = bindingFunction.BodyMode;
                        break;
                }
                // Accessibility is slot-owned (specs/member-access-modifiers.md
                // §2) — a binding member's modifier must not change the
                // declaring slot's visibility.
                substituted.DeclaredAccess = generic.Access;
                substituted.DeclaredGetterAccess = generic.DeclaredGetterAccess;
                substituted.DeclaredSetterAccess = generic.DeclaredSetterAccess;
                substituted.DeclaredDeclarationForm = generic.DeclaredDeclarationForm;
                substituted.DeclaredAccessors = generic.DeclaredAccessors;
                substituted.DeclaredModifier = generic.Modifier;
                // Read-only changes the containing Class slot's stored shape,
                // so it is declaration metadata rather than type-argument
                // metadata. The binding must never add or remove an instance
                // value edge.
                substituted.DeclaredMutability = generic.Mutability;
                // Partial changes how a closed Class slot is materialized,
                // so it belongs to the Generic declaration rather than the
                // binding member (Partial<TTarget> must stay sparse).
                if (substituted is ClassMember substitutedClassMember)
                {
                    substitutedClassMember.DeclaredPayload = generic.Payload;
                }
                // Placement fields are slot-owned. A null declaration means
                // "inherit from placement parent" and must not fall back to
                // the binding's own declaration (bindings are type
                // arguments, not placements).
                substituted.DeclaredStorage = generic.Storage;
                substituted.storageKey = generic.storageKey;
                substituted.valueId = generic.valueId;
                // P76 §1.3 — distribution is slot-owned. An explicit setting
                // on the slot's own chain wins; otherwise the slot stays
                // absent and the automatic result reads the RESOLVED CONCRETE
                // binding's kind and settings, which is what lets
                // NeoAnimationSegmentFrame<T>.Value pack for a Sprite binding
                // and stay sparse for a Plain String one. The binding
                // declaration's own explicit distribution never carries over:
                // it describes where that member's value lives at its own
                // placement, not at this slot's.
                substituted.DeclaredDistribution = generic.ChainDeclaredDistribution;
                CopyDeclarationDefault(substituted, generic.defaultValue);
                substituted.extendsMemberId = null;
                substituted.substitutedDeclarationIdentity =
                    $"{generic.id}@{binding.id}";
                return substituted;
            }
            if (member is ClassMember classMember)
            {
                var args = classMember.TypeArguments;
                if (args is null)
                    return member;
                bool changed = false;
                var substitutedArgs = new Dictionary<string, TypeBinding>(args.Count);
                foreach (var pair in args)
                {
                    var arg = pair.Value;
                    if (!arg.IsOpen)
                    {
                        substitutedArgs[pair.Key] = arg;
                        continue;
                    }
                    if (!env.TryGetValue(arg.Param!, out NeoGenericEnvEntry entry))
                    {
                        throw new System.InvalidOperationException(
                            $"SubstituteMember: constructed argument for param '{pair.Key}' on member '{classMember.name}' ({classMember.id}) forwards generic param '{arg.Param}', which is not present in the binding environment.");
                    }
                    if (!entry.IsBound || entry.bindings is not null)
                    {
                        // Open context (e.g. resolving against the open class
                        // itself): keep the forward — downstream consumers
                        // substitute again once a concrete env exists. A
                        // binding carrying the owner's entries has no member
                        // of its own, so the forward stays for the instance
                        // env to read through the owner (P127 §1.2).
                        substitutedArgs[pair.Key] = arg;
                        continue;
                    }
                    substitutedArgs[pair.Key] = NeoTypeBindings.ArgumentOfEntry(client, entry, env);
                    changed = true;
                }
                if (!changed)
                    return member;
                var substitutedClass = (ClassMember)classMember.ShallowClone();
                if (classMember.DeclaredInterfaceId is null)
                    substitutedClass.classArguments = substitutedArgs;
                else
                    substitutedClass.interfaceArguments = substitutedArgs;
                return substitutedClass;
            }
            if (member is DelegateMember delegateMember)
            {
                TypeInfo returnTypeInfo = SubstituteNestedTypeInfo(
                    client,
                    delegateMember.returnTypeInfo,
                    env);
                FunctionArgumentTypeInfo[] argumentTypes =
                    SubstituteFunctionArguments(
                        client,
                        delegateMember.argumentTypes,
                        env,
                        out bool argumentsChanged);
                if (ReferenceEquals(returnTypeInfo, delegateMember.returnTypeInfo)
                    && !argumentsChanged)
                {
                    return member;
                }
                var substitutedDelegate =
                    (DelegateMember)delegateMember.ShallowClone();
                substitutedDelegate.returnTypeInfo = returnTypeInfo;
                substitutedDelegate.argumentTypes = argumentTypes;
                return substitutedDelegate;
            }
            if (member is ActionMember actionMember)
            {
                FunctionArgumentTypeInfo[] argumentTypes =
                    SubstituteFunctionArguments(
                        client,
                        actionMember.argumentTypes,
                        env,
                        out bool argumentsChanged);
                if (!argumentsChanged)
                    return member;
                var substitutedAction = (ActionMember)actionMember.ShallowClone();
                substitutedAction.argumentTypes = argumentTypes;
                return substitutedAction;
            }
            // P112 §3.5. A `Reference<T>` names its binding's type.
            if (member is LookupMember { declaredTypeInfo: { } declaredTypeInfo } lookupMember)
            {
                TypeInfo targetTypeInfo = SubstituteNestedTypeInfo(client, declaredTypeInfo, env);
                if (ReferenceEquals(targetTypeInfo, declaredTypeInfo))
                {
                    return member;
                }
                var substitutedLookup = (LookupMember)lookupMember.ShallowClone();
                substitutedLookup.declaredTypeInfo = targetTypeInfo;
                return substitutedLookup;
            }
            if (member is VariantMember variantMember
                && variantMember.targetTypeInfo is not null)
            {
                TypeInfo targetTypeInfo = variantMember.targetTypeInfo is GenericTypeInfo genericTarget
                    ? GenericBindingTypeInfo(client, genericTarget.genericParamId, env, "variant target")
                        ?? variantMember.targetTypeInfo
                    : SubstituteNestedTypeInfo(client, variantMember.targetTypeInfo, env);
                if (ReferenceEquals(targetTypeInfo, variantMember.targetTypeInfo))
                {
                    return member;
                }
                var substitutedVariant =
                    (VariantMember)variantMember.ShallowClone();
                substitutedVariant.targetTypeInfo = targetTypeInfo;
                return substitutedVariant;
            }
            return member;
        }

        /// <summary>
        /// Closes an NSFunction signature through <paramref name="env"/> with
        /// the callable rules <see cref="SubstituteMember(NeoClient, Member, IReadOnlyDictionary{string, NeoGenericEnvEntry})"/>
        /// applies to delegates: bound params take their binding's type and
        /// nullability, unbound forwards stay open.
        /// </summary>
        internal static void SubstituteCallableSignature(
            NeoClient client,
            FunctionArgumentTypeInfo[] argumentTypes,
            TypeInfo returnTypeInfo,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            out FunctionArgumentTypeInfo[] substitutedArguments,
            out TypeInfo substitutedReturn)
        {
            substitutedArguments = SubstituteFunctionArguments(
                client,
                argumentTypes,
                env,
                out _);
            substitutedReturn = SubstituteNestedTypeInfo(
                client,
                returnTypeInfo,
                env);
        }

        private static FunctionArgumentTypeInfo[] SubstituteFunctionArguments(
            NeoClient client,
            FunctionArgumentTypeInfo[] arguments,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            out bool changed)
        {
            changed = false;
            FunctionArgumentTypeInfo[]? substituted = null;
            for (int index = 0; index < arguments.Length; index++)
            {
                FunctionArgumentTypeInfo argument = arguments[index];
                FunctionArgumentTypeInfo next = SubstituteFunctionArgument(
                    client,
                    argument,
                    env);
                if (ReferenceEquals(next, argument))
                    continue;
                substituted ??= (FunctionArgumentTypeInfo[])arguments.Clone();
                substituted[index] = next;
                changed = true;
            }
            return substituted ?? arguments;
        }

        private static FunctionArgumentTypeInfo SubstituteFunctionArgument(
            NeoClient client,
            FunctionArgumentTypeInfo argument,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (argument.type == MemberKind.Generic)
            {
                NeoClassArgumentSignature? signature = GenericBindingSignature(
                    client,
                    argument.genericParamId,
                    env,
                    "callable type");
                if (signature is null)
                    return argument;
                FunctionArgumentTypeInfo resolved = FunctionArgumentFromSignature(argument.name, signature);
                resolved.required = NeoGeneratedTypesSupport.ClosedSlotRequired(resolved, declaredNullable: !argument.required);
                return resolved;
            }
            if (argument.type == MemberKind.Tuple)
            {
                TupleElementTypeInfo[] elements = TupleTypeInfo.MapElements(argument.elements!, type => SubstituteNestedTypeInfo(client, type, env));
                if (ReferenceEquals(elements, argument.elements))
                    return argument;
                var substituted = (FunctionArgumentTypeInfo)argument.ShallowClone();
                substituted.elements = elements;
                return substituted;
            }
            if (argument.type == MemberKind.List
                || argument.type == MemberKind.Set
                || argument.type == MemberKind.Dictionary
                || argument.type == MemberKind.Lookup)
            {
                TypeInfo entryTypeInfo = SubstituteNestedTypeInfo(
                    client,
                    argument.entryTypeInfo!,
                    env);
                if (ReferenceEquals(entryTypeInfo, argument.entryTypeInfo))
                {
                    return argument;
                }
                var substituted =
                    (FunctionArgumentTypeInfo)argument.ShallowClone();
                substituted.entryTypeInfo = entryTypeInfo;
                return substituted;
            }
            if (argument.type is MemberKind.Class or MemberKind.Interface)
            {
                if (argument.typeArguments is null)
                    return argument;
                Dictionary<string, TypeInfo> typeArguments =
                    SubstituteTypeArguments(
                        client,
                        argument.typeArguments,
                        env);
                if (ReferenceEquals(typeArguments, argument.typeArguments))
                {
                    return argument;
                }
                var substituted =
                    (FunctionArgumentTypeInfo)argument.ShallowClone();
                substituted.typeArguments = typeArguments;
                return substituted;
            }
            if (argument.type == MemberKind.NSDelegate)
            {
                TypeInfo returnTypeInfo = SubstituteNestedTypeInfo(
                    client,
                    argument.returnTypeInfo!,
                    env);
                TypeInfo[] argumentTypes = SubstituteNestedTypeInfos(
                    client,
                    argument.argumentTypes!,
                    env);
                if (ReferenceEquals(returnTypeInfo, argument.returnTypeInfo)
                    && ReferenceEquals(argumentTypes, argument.argumentTypes))
                {
                    return argument;
                }
                var substituted =
                    (FunctionArgumentTypeInfo)argument.ShallowClone();
                substituted.returnTypeInfo = returnTypeInfo;
                substituted.argumentTypes = argumentTypes;
                return substituted;
            }
            if (argument.type == MemberKind.NSAction)
            {
                TypeInfo[] argumentTypes = SubstituteNestedTypeInfos(
                    client,
                    argument.argumentTypes!,
                    env);
                if (ReferenceEquals(argumentTypes, argument.argumentTypes))
                {
                    return argument;
                }
                var substituted =
                    (FunctionArgumentTypeInfo)argument.ShallowClone();
                substituted.argumentTypes = argumentTypes;
                return substituted;
            }
            return argument;
        }

        /// <summary>
        /// <paramref name="typeInfo"/> with each generic position closed
        /// through <paramref name="env"/>; an unbound one stays generic.
        /// </summary>
        internal static TypeInfo SubstituteTypeInfo(
            NeoClient client,
            TypeInfo typeInfo,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env) =>
            SubstituteNestedTypeInfo(client, typeInfo, env);

        private static TypeInfo SubstituteNestedTypeInfo(
            NeoClient client,
            TypeInfo typeInfo,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (typeInfo is FunctionArgumentTypeInfo argument)
            {
                return SubstituteFunctionArgument(client, argument, env);
            }
            if (typeInfo is GenericTypeInfo generic)
            {
                TypeInfo? resolved = GenericBindingTypeInfo(
                    client,
                    generic.genericParamId,
                    env,
                    "callable type");
                if (resolved is null)
                    return typeInfo;
                resolved.required = NeoGeneratedTypesSupport.ClosedSlotRequired(resolved, declaredNullable: !generic.required);
                return resolved;
            }
            if (typeInfo is TupleTypeInfo tuple)
            {
                TupleElementTypeInfo[] elements = TupleTypeInfo.MapElements(tuple.elements, type => SubstituteNestedTypeInfo(client, type, env));
                if (ReferenceEquals(elements, tuple.elements))
                    return tuple;
                var substituted = (TupleTypeInfo)tuple.ShallowClone();
                substituted.elements = elements;
                return substituted;
            }
            if (typeInfo is CollectionTypeInfo collection)
            {
                TypeInfo entryTypeInfo = SubstituteNestedTypeInfo(
                    client,
                    collection.entryTypeInfo,
                    env);
                if (ReferenceEquals(entryTypeInfo, collection.entryTypeInfo))
                {
                    return typeInfo;
                }
                var substituted =
                    (CollectionTypeInfo)collection.ShallowClone();
                substituted.entryTypeInfo = entryTypeInfo;
                return substituted;
            }
            if (typeInfo is LookupTypeInfo lookup)
            {
                TypeInfo entryTypeInfo = SubstituteNestedTypeInfo(
                    client,
                    lookup.entryTypeInfo,
                    env);
                if (ReferenceEquals(entryTypeInfo, lookup.entryTypeInfo))
                {
                    return typeInfo;
                }
                var substituted = (LookupTypeInfo)lookup.ShallowClone();
                substituted.entryTypeInfo = entryTypeInfo;
                return substituted;
            }
            if (typeInfo is ReferenceTypeInfo reference)
            {
                TypeInfo target = SubstituteNestedTypeInfo(
                    client,
                    reference.targetTypeInfo,
                    env);
                if (ReferenceEquals(target, reference.targetTypeInfo))
                {
                    return typeInfo;
                }
                var substituted = (ReferenceTypeInfo)reference.ShallowClone();
                substituted.targetTypeInfo = target;
                return substituted;
            }
            if (typeInfo is ClassTypeInfo classType)
            {
                if (classType.typeArguments is null)
                    return typeInfo;
                Dictionary<string, TypeInfo> typeArguments =
                    SubstituteTypeArguments(
                        client,
                        classType.typeArguments,
                        env);
                if (ReferenceEquals(typeArguments, classType.typeArguments))
                {
                    return typeInfo;
                }
                var substituted = (ClassTypeInfo)classType.ShallowClone();
                substituted.typeArguments = typeArguments;
                return substituted;
            }
            if (typeInfo is InterfaceTypeInfo interfaceType)
            {
                if (interfaceType.typeArguments is null)
                    return typeInfo;
                Dictionary<string, TypeInfo> typeArguments =
                    SubstituteTypeArguments(
                        client,
                        interfaceType.typeArguments,
                        env);
                if (ReferenceEquals(typeArguments, interfaceType.typeArguments))
                {
                    return typeInfo;
                }
                var substituted = (InterfaceTypeInfo)interfaceType.ShallowClone();
                substituted.typeArguments = typeArguments;
                return substituted;
            }
            if (typeInfo is DelegateTypeInfo delegateType)
            {
                TypeInfo returnTypeInfo = SubstituteNestedTypeInfo(
                    client,
                    delegateType.returnTypeInfo,
                    env);
                TypeInfo[] argumentTypes = SubstituteNestedTypeInfos(
                    client,
                    delegateType.argumentTypes,
                    env);
                if (ReferenceEquals(returnTypeInfo, delegateType.returnTypeInfo)
                    && ReferenceEquals(argumentTypes, delegateType.argumentTypes))
                {
                    return typeInfo;
                }
                var substituted =
                    (DelegateTypeInfo)delegateType.ShallowClone();
                substituted.returnTypeInfo = returnTypeInfo;
                substituted.argumentTypes = argumentTypes;
                return substituted;
            }
            if (typeInfo is ActionTypeInfo actionType)
            {
                TypeInfo[] argumentTypes = SubstituteNestedTypeInfos(
                    client,
                    actionType.argumentTypes,
                    env);
                if (ReferenceEquals(argumentTypes, actionType.argumentTypes))
                {
                    return typeInfo;
                }
                var substituted = (ActionTypeInfo)actionType.ShallowClone();
                substituted.argumentTypes = argumentTypes;
                return substituted;
            }
            return typeInfo;
        }

        private static Dictionary<string, TypeInfo> SubstituteTypeArguments(
            NeoClient client,
            Dictionary<string, TypeInfo> arguments,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            Dictionary<string, TypeInfo>? substituted = null;
            foreach (var pair in arguments)
            {
                TypeInfo next = SubstituteNestedTypeInfo(
                    client,
                    pair.Value,
                    env);
                if (ReferenceEquals(next, pair.Value))
                    continue;
                substituted ??= new Dictionary<string, TypeInfo>(arguments);
                substituted[pair.Key] = next;
            }
            return substituted ?? arguments;
        }

        private static TypeInfo[] SubstituteNestedTypeInfos(
            NeoClient client,
            TypeInfo[] typeInfos,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            TypeInfo[]? substituted = null;
            for (int index = 0; index < typeInfos.Length; index++)
            {
                TypeInfo next = SubstituteNestedTypeInfo(
                    client,
                    typeInfos[index],
                    env);
                if (ReferenceEquals(next, typeInfos[index]))
                    continue;
                substituted ??= (TypeInfo[])typeInfos.Clone();
                substituted[index] = next;
            }
            return substituted ?? typeInfos;
        }

        private static NeoClassArgumentSignature? GenericBindingSignature(
            NeoClient client,
            string? genericParamId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            string position)
        {
            if (string.IsNullOrEmpty(genericParamId)
                || !env.TryGetValue(genericParamId, out NeoGenericEnvEntry entry))
            {
                throw new System.InvalidOperationException(
                    $"SubstituteMember: {position} references generic param '{genericParamId}', which is not present in the binding environment.");
            }
            if (!entry.IsBound)
                return null;
            if (!client.TryGetMember(entry.memberId!, out Member? binding))
            {
                throw new System.InvalidOperationException(
                    $"SubstituteMember: {position} binding member '{entry.memberId}' for generic param '{genericParamId}' does not exist in the document.");
            }
            return SignatureOfMember(client, binding, BindingEnv(env, entry));
        }

        private static TypeInfo? GenericBindingTypeInfo(
            NeoClient client,
            string? genericParamId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            string position)
        {
            NeoClassArgumentSignature? signature = GenericBindingSignature(
                client,
                genericParamId,
                env,
                position);
            return signature is null
                ? null
                : TypeInfoFromArgumentSignature(signature);
        }

        internal static TypeInfo TypeInfoFromArgumentSignature(
            NeoClassArgumentSignature signature)
        {
            if (signature.callable is { } callable)
                return callable;
            if (signature.type == MemberKind.Class)
            {
                return new ClassTypeInfo
                {
                    type = signature.type,
                    required = signature.required,
                    classId = signature.classId!,
                    typeArguments = TypeArgumentsFromSignatures(signature.args),
                };
            }
            if (signature.type == MemberKind.Interface)
            {
                return new InterfaceTypeInfo
                {
                    type = signature.type,
                    required = signature.required,
                    interfaceId = signature.interfaceId!,
                    typeArguments = TypeArgumentsFromSignatures(signature.args),
                };
            }
            if (signature.type == MemberKind.Enum)
            {
                var enumType = new EnumTypeInfo
                {
                    type = signature.type,
                    required = signature.required,
                    enumId = signature.enumId!,
                };
                return MultiSelectSet(signature, enumType);
            }
            if (signature.type == MemberKind.DialogueLookup)
            {
                return MultiSelectSet(signature, new PrimitiveTypeInfo { type = MemberKind.DialogueLookup, required = signature.required });
            }
            if (signature.type == MemberKind.Object)
            {
                return new ObjectTypeInfo { type = MemberKind.Object, required = signature.required, objectType = signature.objectType };
            }
            if (signature.type == MemberKind.Tuple)
            {
                var elements = new TupleElementTypeInfo[signature.elements!.Length];
                for (int i = 0; i < elements.Length; i++)
                    elements[i] = new TupleElementTypeInfo { typeInfo = TypeInfoFromArgumentSignature(signature.elements[i]) };
                return new TupleTypeInfo { type = MemberKind.Tuple, required = signature.required, elements = elements };
            }
            if (signature.type == MemberKind.List
                || signature.type == MemberKind.Set
                || signature.type == MemberKind.Dictionary)
            {
                return new CollectionTypeInfo
                {
                    type = signature.type,
                    required = signature.required,
                    entryTypeInfo = TypeInfoFromArgumentSignature(
                        signature.entry!),
                    keyEnumId = signature.keyEnumId,
                };
            }
            if (signature.type == MemberKind.Lookup)
            {
                return ReferenceTypeInfoFromSignature(signature);
            }
            if (IsPrimitiveTypeInfoKind(signature.type))
            {
                return new PrimitiveTypeInfo
                {
                    type = signature.type,
                    required = signature.required,
                };
            }
            throw new System.InvalidOperationException(
                $"SubstituteMember: generic binding kind {signature.type} cannot appear in a NeoScript callable type.");
        }

        /// <summary>A multi-select Enum or DialogueLookup argument as the <c>Set</c> of <paramref name="single"/> it is.</summary>
        private static TypeInfo MultiSelectSet(NeoClassArgumentSignature signature, TypeInfo single)
        {
            if (signature.selection != NeoMemberSelectionKind.Multi)
                return single;
            single.required = true;
            return new LookupTypeInfo { type = MemberKind.Lookup, required = signature.required, entryTypeInfo = single };
        }

        /// <summary>A Lookup argument as the reference, or set of them, it is (P112 §2.1).</summary>
        private static TypeInfo ReferenceTypeInfoFromSignature(NeoClassArgumentSignature signature)
        {
            bool multi = signature.selection == NeoMemberSelectionKind.Multi;
            ReferenceTypeInfo reference = NeoReferenceTypes.Reference(signature.target!, multi || signature.required);
            if (!multi)
                return reference;
            return new LookupTypeInfo
            {
                type = MemberKind.Lookup,
                required = signature.required,
                entryTypeInfo = reference,
            };
        }

        private static bool IsPrimitiveTypeInfoKind(MemberKind kind)
        {
            return kind == MemberKind.Null
                || kind == MemberKind.Bool
                || kind == MemberKind.Int
                || kind == MemberKind.Float
                || kind == MemberKind.String
                || kind == MemberKind.DialogueLookup
                || kind == MemberKind.Sprite
                || kind == MemberKind.Audio
                || kind == MemberKind.Vector2
                || kind == MemberKind.Vector2Int
                || kind == MemberKind.Vector3
                || kind == MemberKind.Vector3Int
                || kind == MemberKind.Color
                || kind == MemberKind.Decimal;
        }

        private static Dictionary<string, TypeInfo>? TypeArgumentsFromSignatures(
            Dictionary<string, NeoClassArgumentSignature>? args)
        {
            if (args is not { Count: > 0 })
                return null;
            var typeArguments = new Dictionary<string, TypeInfo>(args.Count);
            foreach (var pair in args)
                typeArguments[pair.Key] = TypeInfoFromArgumentSignature(pair.Value);
            return typeArguments;
        }

        private static FunctionArgumentTypeInfo FunctionArgumentFromSignature(
            string name,
            NeoClassArgumentSignature signature)
        {
            var argument = new FunctionArgumentTypeInfo
            {
                name = name,
                type = signature.type,
                required = signature.required,
            };
            if (signature.type == MemberKind.Class)
            {
                argument.classId = signature.classId;
                argument.typeArguments = TypeArgumentsFromSignatures(signature.args);
            }
            else if (signature.type == MemberKind.Interface)
            {
                argument.interfaceId = signature.interfaceId;
                argument.typeArguments = TypeArgumentsFromSignatures(signature.args);
            }
            else if (signature.type is MemberKind.Enum or MemberKind.DialogueLookup && signature.selection == NeoMemberSelectionKind.Multi)
            {
                argument.type = MemberKind.Lookup;
                argument.entryTypeInfo = ((LookupTypeInfo)TypeInfoFromArgumentSignature(signature)).entryTypeInfo;
            }
            else if (signature.type == MemberKind.Enum)
            {
                argument.enumId = signature.enumId;
            }
            else if (signature.type == MemberKind.Object)
            {
                argument.objectType = signature.objectType;
            }
            else if (signature.callable is DelegateTypeInfo delegateType)
            {
                argument.returnTypeInfo = delegateType.returnTypeInfo;
                argument.argumentTypes = delegateType.argumentTypes;
            }
            else if (signature.callable is ActionTypeInfo actionType)
            {
                argument.argumentTypes = actionType.argumentTypes;
            }
            else if (signature.type == MemberKind.Tuple)
            {
                argument.elements = ((TupleTypeInfo)TypeInfoFromArgumentSignature(signature)).elements;
            }
            else if (signature.type == MemberKind.List
                || signature.type == MemberKind.Set
                || signature.type == MemberKind.Dictionary)
            {
                argument.entryTypeInfo =
                    TypeInfoFromArgumentSignature(signature.entry!);
                argument.keyEnumId = signature.keyEnumId;
            }
            else if (signature.type == MemberKind.Lookup)
            {
                TypeInfo reference = ReferenceTypeInfoFromSignature(signature);
                argument.type = reference.type;
                if (reference is LookupTypeInfo set)
                    argument.entryTypeInfo = set.entryTypeInfo;
                else
                    argument.targetTypeInfo = ((ReferenceTypeInfo)reference).targetTypeInfo;
            }
            else if (!IsPrimitiveTypeInfoKind(signature.type))
            {
                throw new System.InvalidOperationException(
                    $"SubstituteMember: generic binding kind {signature.type} cannot appear in a NeoScript callable type.");
            }
            return argument;
        }

        /// <summary>
        /// Resolves a param to its terminal binding member through
        /// <paramref name="env"/>, with a distinct error per failure mode, and
        /// <paramref name="memberEnv"/>, the environment that member reads
        /// its own params in (<see cref="BindingEnv"/>).
        /// </summary>
        private static Member ResolveTerminalBinding(
            NeoClient client,
            string genericParamId,
            string slotName,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            out IReadOnlyDictionary<string, NeoGenericEnvEntry> memberEnv)
        {
            if (!env.TryGetValue(genericParamId, out NeoGenericEnvEntry entry))
            {
                throw new NeoOpenTypeArgumentException(
                    $"Cannot substitute generic slot '{slotName}': param '{genericParamId}' is not present in the binding environment (is the member referencing a param outside its placement scope?).");
            }
            if (!entry.IsBound)
            {
                throw new NeoOpenTypeArgumentException(
                    $"Cannot substitute generic slot '{slotName}': param '{genericParamId}' is unbound in the binding environment — only closed classes are instantiable.");
            }
            if (!client.TryGetMember(entry.memberId!, out Member? bindingMember))
            {
                throw new System.InvalidOperationException(
                    $"Cannot substitute generic slot '{slotName}': binding member '{entry.memberId}' for param '{genericParamId}' does not exist in the document.");
            }
            memberEnv = BindingEnv(env, entry);
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
            try
            {
                if (member is GenericMember generic)
                {
                    Member binding = ResolveTerminalBinding(
                        client,
                        generic.genericParamId,
                        generic.name,
                        env,
                        out IReadOnlyDictionary<string, NeoGenericEnvEntry> bindingEnv);
                    return SignatureOfMember(client, binding, bindingEnv, seen);
                }
                bool required = member.Requirement == NeoMemberRequirementKind.Required;
                if (member is EnumMember enumMember)
                {
                    return new NeoClassArgumentSignature
                    {
                        type = MemberKind.Enum,
                        required = required,
                        enumId = enumMember.enumId,
                        selection = enumMember.Selection,
                    };
                }
                if (member is ClassMember classMember)
                {
                    var args = new Dictionary<string, NeoClassArgumentSignature>();
                    if (classMember.TypeArguments is not null)
                    {
                        foreach (var pair in classMember.TypeArguments)
                        {
                            args[pair.Key] = SignatureOfBinding(client, pair.Value, env, seen);
                        }
                    }
                    return new NeoClassArgumentSignature
                    {
                        type = classMember.TypeKind,
                        required = required,
                        classId = classMember.DeclaredClassId,
                        interfaceId = classMember.DeclaredInterfaceId,
                        args = args,
                    };
                }
                if (member is TupleMember tuple)
                {
                    var elements = new NeoClassArgumentSignature[tuple.elementMemberIds.Length];
                    for (int i = 0; i < elements.Length; i++)
                        elements[i] = EntrySignature(client, tuple.elementMemberIds[i], env, seen);
                    return new NeoClassArgumentSignature { type = MemberKind.Tuple, required = required, elements = elements };
                }
                if (member is SetMember setMember)
                {
                    return new NeoClassArgumentSignature
                    {
                        type = MemberKind.Set,
                        required = required,
                        entry = EntrySignature(client, setMember.entryMemberId, env, seen),
                    };
                }
                if (member is ListMember listMember)
                {
                    return new NeoClassArgumentSignature
                    {
                        type = MemberKind.List,
                        required = required,
                        listKind = listMember.ListKind,
                        entry = EntrySignature(client, listMember.entryMemberId, env, seen),
                    };
                }
                if (member is DictionaryMember dictionaryMember)
                {
                    return new NeoClassArgumentSignature
                    {
                        type = MemberKind.Dictionary,
                        required = required,
                        keyKind = dictionaryMember.KeyKind,
                        keyEnumId = dictionaryMember.keyEnumId,
                        entry = EntrySignature(client, dictionaryMember.entryMemberId, env, seen),
                    };
                }
                if (member is LookupMember lookupMember)
                {
                    if (lookupMember.declaredTypeInfo is null)
                    {
                        throw new System.InvalidOperationException(
                            $"SignatureOfMember: Lookup '{lookupMember.name}' has no declared type; a type argument can't be a Lookup bound to a collection.");
                    }
                    return new NeoClassArgumentSignature
                    {
                        type = MemberKind.Lookup,
                        required = required,
                        selection = lookupMember.Selection,
                        target = SubstituteNestedTypeInfo(client, lookupMember.declaredTypeInfo, env),
                    };
                }
                if (member is StringMember stringMember)
                {
                    return new NeoClassArgumentSignature
                    {
                        type = MemberKind.String,
                        required = required,
                        format = stringMember.Format,
                    };
                }
                if (member is DialogueLookupMember dialogueLookup)
                {
                    return new NeoClassArgumentSignature
                    {
                        type = MemberKind.DialogueLookup,
                        required = required,
                        selection = dialogueLookup.Selection,
                    };
                }
                if (member is ObjectMember objectMember)
                {
                    return new NeoClassArgumentSignature
                    {
                        type = MemberKind.Object,
                        required = required,
                        objectType = objectMember.objectType,
                    };
                }
                if (member is DelegateMember callable)
                {
                    return new NeoClassArgumentSignature
                    {
                        type = MemberKind.NSDelegate,
                        required = required,
                        callable = new DelegateTypeInfo
                        {
                            type = MemberKind.NSDelegate,
                            required = required,
                            returnTypeInfo = SubstituteNestedTypeInfo(client, callable.returnTypeInfo, env),
                            argumentTypes = SubstituteNestedTypeInfos(client, NeoTypeRules.PlainArguments(callable.argumentTypes), env),
                        },
                    };
                }
                if (member is ActionMember action)
                {
                    // P62 §2.1: never nullable; each argument keeps its own `?`.
                    return new NeoClassArgumentSignature
                    {
                        type = MemberKind.NSAction,
                        required = true,
                        callable = new ActionTypeInfo
                        {
                            type = MemberKind.NSAction,
                            required = true,
                            argumentTypes = SubstituteNestedTypeInfos(client, NeoTypeRules.PlainArguments(action.argumentTypes), env),
                        },
                    };
                }
                return new NeoClassArgumentSignature
                {
                    type = member.kind,
                    required = required,
                };
            }
            finally
            {
                if (!string.IsNullOrEmpty(member.id))
                    seen.Remove(member.id);
            }
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
            TypeBinding binding,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            HashSet<string>? seen = null)
        {
            seen ??= new HashSet<string>();
            if (binding.IsOpen)
            {
                if (!env.TryGetValue(binding.Param!, out NeoGenericEnvEntry entry))
                {
                    throw new System.InvalidOperationException(
                        $"SignatureOfBinding: forwarded generic param '{binding.Param}' is not present in the binding environment.");
                }
                if (!entry.IsBound)
                {
                    throw new System.InvalidOperationException(
                        $"SignatureOfBinding: forwarded generic param '{binding.Param}' is unbound — signatures exist only under closed environments.");
                }
                return SignatureOfEntry(client, entry, BindingEnv(env, entry), seen);
            }
            return SignatureOfMember(client, NeoTypeBindingViews.View(binding), env, seen);
        }

        /// <summary>Signature of a bound environment entry's member, read in <paramref name="env"/>.</summary>
        internal static NeoClassArgumentSignature SignatureOfEntry(
            NeoClient client,
            NeoGenericEnvEntry entry,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env,
            HashSet<string>? seen = null)
        {
            if (!client.TryGetMember(entry.memberId!, out Member? member))
            {
                throw new System.InvalidOperationException(
                    $"SignatureOfBinding: binding member '{entry.memberId}' does not exist in the document.");
            }
            return SignatureOfMember(client, member, env, seen ?? new HashSet<string>());
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
            if (a.type != b.type)
                return false;
            if (a.required != b.required)
                return false;
            if (a.type == MemberKind.String)
            {
                return a.format == b.format;
            }
            if (a.type == MemberKind.Enum)
            {
                return a.enumId == b.enumId && a.selection == b.selection;
            }
            if (a.type == MemberKind.DialogueLookup)
                return a.selection == b.selection;
            if (a.type == MemberKind.Object)
                return a.objectType == b.objectType;
            if (a.type is MemberKind.NSDelegate or MemberKind.NSAction)
                return NeoReferenceTypes.InvariantlyEqual(a.callable!, b.callable!);
            if (a.type is MemberKind.Class or MemberKind.Interface)
            {
                if (a.classId != b.classId)
                    return false;
                if (a.interfaceId != b.interfaceId)
                    return false;
                var aArgs = a.args ?? new Dictionary<string, NeoClassArgumentSignature>();
                var bArgs = b.args ?? new Dictionary<string, NeoClassArgumentSignature>();
                if (aArgs.Count != bArgs.Count)
                    return false;
                foreach (var pair in aArgs)
                {
                    if (!bArgs.TryGetValue(pair.Key, out NeoClassArgumentSignature bArg))
                        return false;
                    if (!SignaturesEqual(pair.Value, bArg))
                        return false;
                }
                return true;
            }
            if (a.type == MemberKind.Tuple)
            {
                if (a.elements!.Length != b.elements!.Length)
                    return false;
                for (int i = 0; i < a.elements.Length; i++)
                    if (!SignaturesEqual(a.elements[i], b.elements[i]))
                        return false;
                return true;
            }
            if (a.type == MemberKind.List)
            {
                if (a.listKind != b.listKind)
                    return false;
                return SignaturesEqual(a.entry!, b.entry!);
            }
            if (a.type == MemberKind.Set)
                return SignaturesEqual(a.entry!, b.entry!);
            if (a.type == MemberKind.Lookup)
            {
                if (a.selection != b.selection)
                    return false;
                return NeoReferenceTypes.InvariantlyEqual(a.target!, b.target!);
            }
            if (a.type == MemberKind.Dictionary)
            {
                if (a.keyKind != b.keyKind)
                    return false;
                if (a.keyEnumId != b.keyEnumId)
                    return false;
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
        /// <paramref name="contextEnv"/> (the enclosing context's
        /// environment — used to substitute forwarded arguments and the
        /// owner params an argument names). Mirrors
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
        /// The Abstract class modifier is deliberately not checked here; value
        /// creation paths already reject abstract classes, and this function
        /// also tests mid-hierarchy compatibility. The runtime judges a
        /// constructed class slot through this wherever the server does:
        /// constructed rows, constructor arguments included
        /// (<c>ValidateConstructedRowShape</c>), default construction
        /// (<c>CreateDefaultClassValueRow</c>) and host writes
        /// (<see cref="NeoClient.RequireSlotAdmits"/>).
        /// </summary>
        public static NeoSlotAdmission ConstructedSlotAccepts(
            NeoClient client,
            ClassMember slotMember,
            string valueClassId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> contextEnv)
        {
            var chain = client.ResolveClassInheritanceChain(valueClassId);
            if (chain.Count == 0)
            {
                return NeoSlotAdmission.Reject(
                    $"class \"{valueClassId}\" does not exist in the document");
            }
            if (slotMember.DeclaredInterfaceId is not null)
            {
                string? refusal = client.InterfaceImplementers.SlotRefusal(
                    slotMember,
                    null,
                    slotMember.name,
                    valueClassId,
                    contextEnv);
                return refusal is null ? NeoSlotAdmission.Ok : NeoSlotAdmission.Reject(refusal);
            }
            bool isDescendant = false;
            foreach (var t in chain)
            {
                if (t.id != slotMember.DeclaredClassId)
                    continue;
                isDescendant = true;
                break;
            }
            if (!isDescendant)
            {
                return NeoSlotAdmission.Reject(
                    $"class \"{chain[0].name}\" is not \"{slotMember.DeclaredClassId}\" or one of its descendants");
            }
            // The pick's env is its chain env overlaid with the SLOT's
            // constructed arguments: for a constructed slot, the declared
            // class itself (or an open descendant that forwards the params)
            // is closed BY the slot — rejecting it as "open" would leave
            // constructed slots with no valid pick at all (spec §3.4).
            var valueEnv = ResolveInstanceEnv(chain, slotMember.classArguments, client, contextEnv);
            foreach (var entry in valueEnv.Values)
            {
                if (entry.IsBound)
                    continue;
                return NeoSlotAdmission.Reject(
                    $"class \"{chain[0].name}\" is open (generic param \"{entry.unboundParamId}\" is unbound) and cannot be instantiated");
            }
            var args = slotMember.classArguments;
            if (args is null)
                return NeoSlotAdmission.Ok;
            foreach (var pair in args)
            {
                if (!valueEnv.TryGetValue(pair.Key, out NeoGenericEnvEntry valueBinding))
                {
                    return NeoSlotAdmission.Reject(
                        $"class \"{chain[0].name}\" does not resolve generic param \"{pair.Key}\" anywhere in its chain");
                }
                if (!valueBinding.IsBound)
                {
                    return NeoSlotAdmission.Reject(
                        $"class \"{chain[0].name}\" leaves generic param \"{pair.Key}\" unbound");
                }
                // A slot argument its context leaves open, at any depth, fits
                // any row, as in an interface slot; the row is judged where
                // its owner closes it (P127 §1.2).
                if (client.TypeRules.BindingText(pair.Value, contextEnv) is null)
                    continue;
                var slotSignature = SignatureOfBinding(client, pair.Value, contextEnv);
                var valueSignature = SignatureOfEntry(client, valueBinding, BindingEnv(valueEnv, valueBinding));
                if (!ConstructedSlotArgumentAccepts(client, slotMember, pair.Key, slotSignature, valueSignature))
                {
                    return NeoSlotAdmission.Reject(
                        $"class-argument signature mismatch for param \"{pair.Key}\" (arguments compare invariantly)");
                }
            }
            return NeoSlotAdmission.Ok;
        }

        private const string AnimationChildOverridesMemberId = "system_a5905750-c472-46a8-89e9-f04f0bf66696";
        private const string AnimationChildOverridesEntryMemberId = "system_86688965-6b26-529e-95f3-a4070f022582";
        private const string AnimationChildOverrideEntryParamId = "system_de23448a-45be-4e3f-8965-072a545e08f0";

        /// <summary>
        /// Whether a slot argument admits the row's: invariantly equal, or, for
        /// an animation frame's child-override entry, the row's override class
        /// descending from the slot's (the server's
        /// <c>constructedSlotArgumentAccepts</c>).
        /// </summary>
        private static bool ConstructedSlotArgumentAccepts(
            NeoClient client,
            ClassMember slotMember,
            string targetParamId,
            NeoClassArgumentSignature slotSignature,
            NeoClassArgumentSignature valueSignature)
        {
            if (SignaturesEqual(slotSignature, valueSignature))
                return true;
            return targetParamId == AnimationChildOverrideEntryParamId
                && slotSignature.required == valueSignature.required
                && slotSignature.type == MemberKind.Class
                && valueSignature.type == MemberKind.Class
                && IsAnimationChildOverrideEntrySlot(client, slotMember)
                && client.ClassChainContains(valueSignature.classId!, slotSignature.classId!);
        }

        /// <summary>
        /// Whether <paramref name="slotMember"/> is, or overrides through
        /// <c>extendsMemberId</c>, an animation frame's child-override list
        /// or its entry.
        /// </summary>
        private static bool IsAnimationChildOverrideEntrySlot(NeoClient client, Member slotMember)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            Member? current = slotMember;
            while (current is not null && !string.IsNullOrEmpty(current.id) && visited.Add(current.id))
            {
                if (current.id is AnimationChildOverridesMemberId or AnimationChildOverridesEntryMemberId)
                    return true;
                if (string.IsNullOrEmpty(current.extendsMemberId) || !client.TryGetMember(current.extendsMemberId!, out current))
                    return false;
            }
            return false;
        }
    }

    /// <summary>
    /// A member names a type parameter its environment leaves open, so the
    /// type it stands for can't be determined. The server's
    /// <c>OpenTypeArgumentError</c>.
    /// </summary>
    internal sealed class NeoOpenTypeArgumentException : InvalidOperationException
    {
        internal NeoOpenTypeArgumentException(string message)
            : base(message)
        {
        }
    }
}
